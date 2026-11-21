#!/usr/bin/env python3
"""Read-only first look at a tablet firmware dump (stdlib only). See DOCS/porting-guide.md.

usage: python -I tools/survey.py IMAGE.bin [--part NAME=PART.bin ...] [--isa thumb|mips32] [--base ADDRESS]
--isa and --base override the guesses: a small part (a boot ROM of a few hundred instructions) has too few markers to
recognise, and its address cannot be read from itself (for example a PIC32's boot flash at 0xBFC00000).
Prints: size and SHA-256, which instruction set the image looks like (Arm Thumb or MIPS32), then
- Arm Thumb (Cortex-M): image base, vector tables (stack top, reset, SysTick, which interrupts have their own
  handler), 32-bit peripheral address literals per region;
- MIPS32: where the code runs (from its jump targets) and which address segments its constants point into;
then blank areas, where each --part file sits inside IMAGE, and USB device and configuration descriptors (VID, PID,
endpoints, bInterval). It prints addresses, counts and values only, never firmware bytes, so its output can go into
notes and docs.
"""
import hashlib
import struct
import sys
from collections import Counter

CORE = {2: 'NMI', 3: 'HardFault', 4: 'MemManage', 5: 'BusFault', 6: 'UsageFault', 11: 'SVC', 12: 'DebugMon',
        14: 'PendSV', 15: 'SysTick'}
# Peripheral bases of the one chip family the emulator models (GD32F3x0, the Deco 640's). A dump that matches none
# of these is another family: identify it from its own reference manual before trusting anything else.
KNOWN_GD32F3X0 = {
    0x40012C00: 'TIMER0', 0x40000400: 'TIMER2', 0x40002000: 'TIMER13', 0x40014000: 'TIMER14',
    0x40014400: 'TIMER15', 0x40014800: 'TIMER16', 0x40012400: 'ADC', 0x40020000: 'DMA', 0x40021000: 'RCU',
    0x40022000: 'FMC', 0x50000000: 'USBFS', 0x48000000: 'GPIOA', 0x48000400: 'GPIOB', 0x48000800: 'GPIOC',
}


def word(b, o):
    return struct.unpack_from('<I', b, o)[0]


def instruction_set(b):
    """('thumb' | 'mips32' | 'unknown', evidence). Counted per KB of non-blank data:
    Thumb: 'bx lr' (return) and 'push {..., lr}' (function start) halfwords;
    MIPS32 (little-endian): 'jr ra' (return) and 'addiu sp, sp, -n' (function start) words."""
    kb = max(1, (len(b) - b.count(0xFF)) // 1024)
    half = struct.unpack('<%dH' % (len(b) // 2), b[:len(b) // 2 * 2])
    words = struct.unpack('<%dI' % (len(b) // 4), b[:len(b) // 4 * 4])
    thumb = sum(1 for h in half if h == 0x4770) + sum(1 for h in half if h & 0xFF00 == 0xB500)
    mips = sum(1 for w in words if w == 0x03E00008) + sum(1 for w in words if w >> 16 == 0x27BD and w & 0x8000)
    ev = 'Thumb markers %.1f per KB, MIPS32 markers %.1f per KB' % (thumb / float(kb), mips / float(kb))
    if thumb > 3 * kb and thumb > 4 * mips:
        return 'thumb', ev
    if mips > 3 * kb and mips > 4 * thumb:
        return 'mips32', ev
    return 'unknown', ev


MIPS_SEGMENTS = ((0x80000000, 0xA0000000, 'KSEG0 (cached view of physical memory)'),
                 (0xA0000000, 0xC0000000, 'KSEG1 (uncached view of the same physical memory)'))


def mips_report(b):
    """Where MIPS32 code runs and which segments its constants use. Returns the most likely load address."""
    words = struct.unpack('<%dI' % (len(b) // 4), b[:len(b) // 4 * 4])
    regions, upper = Counter(), Counter()
    for w in words:
        if w >> 26 in (2, 3):                                  # j / jal: target = PC's top 4 bits + 28 bits
            regions[((w & 0x3FFFFFF) << 2) & 0x0FF00000] += 1
        if w >> 26 == 0x0F and (w >> 21) & 31 == 0:            # lui rt, imm: the upper half of a 32-bit constant
            upper[(w & 0xFFFF) << 16] += 1
    top = regions.most_common(1)[0][0] if regions else 0
    print('MIPS32: j/jal targets mostly in xx%07X.. (the top 4 bits come from where the code runs)' % top)
    for lo, hi, what in MIPS_SEGMENTS:
        mine = sorted((v, c) for v, c in upper.items() if lo <= v < hi)
        if mine:
            print('  constants into %s: %s' % (what, ', '.join('0x%04X.... (%d)' % (v >> 16, c) for v, c in mine if c > 1)))
    return 0x90000000 | top if top else 0


def handler_kind(b, base, v):
    """'stub' for a handler that only loops forever (b .), 'empty' for one that only returns (bx lr), else 'code'.
    A running interrupt whose handler is a stub hangs the chip; one whose handler is empty costs time unseen."""
    t = (v & ~1) - base
    if v == 0 or t < 0 or t + 2 > len(b):
        return 'none'
    h = struct.unpack_from('<H', b, t)[0]
    return 'stub' if h == 0xE7FE else 'empty' if h == 0x4770 else 'code'


def table_at(b, base, o):
    """Vector table at offset o, or None. A table starts with a RAM stack top and an odd (Thumb) reset address."""
    if o + 64 > len(b):
        return None
    sp, rv = word(b, o), word(b, o + 4)
    if not (0x20000000 <= sp <= 0x20100000 and sp % 4 == 0):
        return None
    for k in (1, 2, 3):
        v = word(b, o + 4 * k)
        if not (v & 1 and base + o <= v < base + len(b)):
            return None
    n = 2
    while o + 4 * n + 4 <= len(b) and n < 256:
        v = word(b, o + 4 * n)
        if not (v == 0 or (v & 1 and base <= v < base + len(b))):
            break
        n += 1
    vec = [word(b, o + 4 * k) for k in range(n)]
    own, empty = [], []
    for k in range(2, n):
        kind = handler_kind(b, base, vec[k])
        name = CORE.get(k, 'IRQ%d' % (k - 16)) if k < 16 else 'IRQ%d' % (k - 16)
        if kind == 'code':
            own.append(name)
        elif kind == 'empty':
            empty.append(name)
    systick = handler_kind(b, base, vec[15]) if n > 15 else '-'
    return sp, rv, (vec[15] if n > 15 else None), n, own, empty, systick


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    b = open(argv[1], 'rb').read()
    parts, force_isa, force_base = [], None, None
    rest = argv[2:]
    while rest:
        a = rest.pop(0)
        if a in ('--isa', '--base') and rest:
            v = rest.pop(0)
            if a == '--isa':
                if v not in ('thumb', 'mips32'):
                    print('--isa must be thumb or mips32')
                    return 2
                force_isa = v
            else:
                force_base = int(v, 0)
        elif a == '--part':
            continue
        elif '=' in a:
            name, path = a.split('=', 1)
            parts.append((name, open(path, 'rb').read()))
    print('image %s: %d bytes, SHA-256 %s' % (argv[1], len(b), hashlib.sha256(b).hexdigest()))
    isa, evidence = instruction_set(b)
    if force_isa:
        isa, evidence = force_isa, evidence + '; set with --isa'
    print('instruction set: %s (%s)' % ({'thumb': 'Arm Thumb (Cortex-M)', 'mips32': 'MIPS32, little-endian',
                                         'unknown': 'not recognised'}[isa], evidence))
    if isa == 'thumb':
        base = word(b, 4) & 0xFFF00000
        print('image base 0x%08X (from the reset address 0x%08X)' % (base, word(b, 4)))
    elif isa == 'mips32':
        base = mips_report(b)
        print('image base 0x%08X (assumed: the KSEG0 address of the region most jumps go to)' % base)
    else:
        base = 0
        if force_base is None:
            print('image base unknown: identify the processor first (DOCS/porting-guide.md, step 0)')
    if force_base is not None:
        base = force_base
        print('image base 0x%08X (set with --base)' % base)

    tables = []
    for o in range(0, len(b) - 64, 0x100) if isa == 'thumb' else ():
        t = table_at(b, base, o)
        if t:
            tables.append((o, t))
    for o, (sp, rv, st, n, own, empty, stk) in tables:
        print('vector table at 0x%08X: stack top 0x%08X, reset 0x%08X, %d entries (%d interrupts)'
              % (base + o, sp, rv, n, max(0, n - 16)))
        print('  SysTick handler 0x%s: %s' % ('%08X' % st if st is not None else '-',
              {'stub': 'loops forever (SysTick must be off here)', 'empty': 'returns at once (a running SysTick costs time unseen)',
               'code': 'has code', 'none': 'none', '-': '-'}[stk]))
        print('  handlers with code: ' + (', '.join(own) or 'none'))
        if empty:
            print('  handlers that only return: ' + ', '.join(empty))

    page = 0x400
    runs, start = [], None
    for o in range(0, len(b), page):
        blank = b[o:o + page].count(0xFF) == len(b[o:o + page])
        if blank and start is None:
            start = o
        if not blank and start is not None:
            runs.append((start, o)); start = None
    if start is not None:
        runs.append((start, len(b)))
    for s, e in runs:
        print('blank 0x%08X..0x%08X (%d KB)' % (base + s, base + e - 1, (e - s) // 1024))
    last = len(b) - 1
    while last > 0 and b[last] == 0xFF:
        last -= 1
    print('last programmed byte 0x%08X' % (base + last))
    for name, pb in parts:
        at = b.find(pb)
        print('part %s (%d bytes): %s' % (name, len(pb), 'at 0x%08X' % (base + at) if at >= 0 else 'NOT found as a whole'))

    bounds = [0] + [o for o, _ in tables if o > 0] + [len(b)]
    for i in range(len(bounds) - 1) if isa == 'thumb' else ():
        lo, hi = bounds[i], bounds[i + 1]
        lits = Counter()
        for o in range(lo, hi - 3, 4):
            v = word(b, o)
            if 0x40000000 <= v < 0x60000000 or 0xE000E000 <= v < 0xE000F000:
                lits[v] += 1
        known = sorted(set(KNOWN_GD32F3X0[v] for v in lits if v in KNOWN_GD32F3X0))
        print('region 0x%08X..0x%08X: %d peripheral literals; GD32F3x0 bases among them: %s'
              % (base + lo, base + hi - 1, len(lits), ', '.join(known) if known else 'none'))
        print('  ' + ' '.join('%08X%s' % (v, ('x%d' % c) if c > 1 else '') for v, c in sorted(lits.items())))

    for o in range(0, len(b) - 18):
        if b[o] == 18 and b[o + 1] == 1 and b[o + 2:o + 4] in (b'\x00\x02', b'\x10\x01', b'\x00\x01') \
                and b[o + 7] in (8, 16, 32, 64) and b[o + 17] in (1, 2, 3, 4):
            vid, pid = struct.unpack_from('<HH', b, o + 8)
            print('USB device descriptor at 0x%08X: USB %x.%02x, VID %04X, PID %04X, endpoint 0 max %d'
                  % (base + o, b[o + 3], b[o + 2], vid, pid, b[o + 7]))
    for o in range(0, len(b) - 9):
        if b[o] != 9 or b[o + 1] != 2:
            continue
        tot = struct.unpack_from('<H', b, o + 2)[0]
        if not (18 <= tot <= 512 and 1 <= b[o + 4] <= 8 and o + tot <= len(b)):
            continue
        k, eps, ok = o, [], True
        while k < o + tot:
            ln, ty = b[k], b[k + 1]
            if ln < 2:
                ok = False
                break
            if ty == 5 and ln == 7:
                eps.append('0x%02X %s max %d bInterval %d' % (b[k + 2], ['control', 'iso', 'bulk', 'interrupt'][b[k + 3] & 3],
                                                              struct.unpack_from('<H', b, k + 4)[0], b[k + 6]))
            k += ln
        if ok and k == o + tot and eps:
            print('USB configuration at 0x%08X (%d bytes): endpoints %s' % (base + o, tot, '; '.join(eps)))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
