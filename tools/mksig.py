#!/usr/bin/env python3
"""Generate atf_symbols.cs: one byte signature per firmware location the tool uses (see atf_layout.cs).

usage: python -I tools/mksig.py OUT.cs WRITES.txt REF.bin[:MAP] [REF.bin[:MAP] ...] -- SOURCE.cs [SOURCE.cs ...]

  REF.bin   full 64 KB stock dumps. The FIRST is the reference build (2025-02-14): the addresses written in the source.
            Every further one is a build the signatures must also work on, with MAP = how its code moved relative to the
            reference, as comma-separated FROM-TO=SHIFT ranges of reference addresses (hex, shift signed decimal), e.g.
            080041CC-080042A7=-2,080042A8-08005867=-4,0800589C-0800FFFF=12 (addresses outside every range: shift 0).
            The map is only the ground truth to CHECK against; the tool itself finds locations by signature.
  WRITES    any text with patch log lines ("   0x0800xxxx: aabb -> ccdd ..." / "   0x0800xxxx: +N bytes ...", e.g. the
            regression matrix output): every byte this tool may rewrite is masked, so built images resolve too.
  SOURCE    the C# sources. Symbols = every Fw.A(0x...), Fw.Ptr(0x...), Fw.Sym(0x...), ly.A(0x...), .A(0x...) address, plus the call
            target of every BL / B.W the patches expect to find at a site (Image.Expect compares targets, not bytes).

A signature is a window of reference bytes around the location, with these bytes masked: call / long-branch encodings,
literal-pool offsets (LDR/ADR pc-relative), 4-aligned words that point into flash, bytes this tool rewrites, and bytes
that differ in any further build. The shortest window (>= 12 fixed bytes) that matches exactly once in every build's
firmware region, at the right address, is chosen. A location without one is an error: fix it before shipping.

R42: the output holds no firmware bytes. Each window is written as its mask ("x" a byte that must match, "." a masked
byte), a 16-bit quick check of its first four fixed bytes, and the first 64 bits of the SHA-256 of the window with every
masked byte set to 0 (see fingerprint() and Layout.Resolve). The window bytes themselves cannot be recovered from that.
"""
import glob
import hashlib
import re
import struct
import sys

BASE = 0x08003800


def load(path):
    b = open(path, 'rb').read()
    assert len(b) == 0x10000, path + ': need a full 64 KB dump'
    return b


def fw_end(b):
    """the image end from the scatter-load table (same rule as Layout.FindEnd)"""
    n = 0x10000 - 0x3800
    for p in range(0, n - 32, 4):
        k, mx = 0, -1
        while True:
            o = 0x3800 + p + 16 * k
            if o + 16 > 0x10000:
                break
            ld, ex, sz, fn = struct.unpack_from('<4I', b, o)
            if not (BASE <= ld < 0x08010000 and 0x20000000 <= ex < 0x20002000 and sz < 0x2000 and BASE < fn < BASE + n):
                break
            mx = max(mx, ld)
            k += 1
        if k >= 2 and mx > BASE + p:
            return mx
    raise SystemExit('no scatter-load table found')


def quick16(b0, b1, b2, b3):
    """16-bit quick check of four bytes (same as Layout.Quick16): only skips most positions before the SHA-256"""
    w = b0 | (b1 << 8) | (b2 << 16) | (b3 << 24)
    return ((w * 2654435761) & 0xFFFFFFFF) >> 16


def fingerprint(pat):
    """(mask, quick check, fingerprint) of a window given as a list of byte or None (masked)"""
    mask = ''.join('.' if x is None else 'x' for x in pat)
    fixed = [i for i, x in enumerate(pat) if x is not None]
    q = quick16(*(pat[i] for i in fixed[:4]))
    fp = hashlib.sha256(bytes(0 if x is None else x for x in pat)).hexdigest()[:16]
    return mask, f'{q:04x}', fp


def parse_map(s):
    rng = []
    for part in filter(None, s.split(',')):
        r, sh = part.split('=')
        lo, hi = (int(x, 16) for x in r.split('-'))
        rng.append((lo, hi, int(sh)))
    return lambda a: a + next((sh for lo, hi, sh in rng if lo <= a <= hi), 0)


def reloc_mask(b, start, length):
    """bytes in [start, start+length) that hold build-relative encodings. Decoding starts 64 bytes earlier so the
    instruction boundaries are in sync when the window starts in the middle of a 32-bit instruction (Thumb decoding
    re-synchronises within a few instructions); as a second guard, a halfword that follows a BL/B.W first half is
    masked with it."""
    m = set()
    a = max(BASE, start - 64)
    while a < start + length:
        o = a - 0x08000000
        h1 = b[o] | (b[o + 1] << 8)
        if (h1 >> 11) in (0x1D, 0x1E, 0x1F):
            h2 = b[o + 2] | (b[o + 3] << 8)
            if (h1 & 0xF800) == 0xF000 and (h2 & 0x8000):               # BL, B.W, B<c>.W
                m.update(range(a, a + 4))
                if a + 2 == start:
                    m.update(range(a + 2, a + 4))
            elif (h1 & 0xFF7F) == 0xF85F or (h1 & 0xFF7F) == 0xF81F:   # LDR(B/H).W literal
                m.update(range(a + 2, a + 4))
            a += 4
            continue
        if 0x4800 <= h1 <= 0x4FFF or 0xA000 <= h1 <= 0xA7FF:            # LDR literal, ADR
            m.add(a)
        a += 2
    for a in range(start - 2, start + length, 2):                       # any BL/B.W-looking pair, whatever the sync
        o = a - 0x08000000
        if o < 0x3800:
            continue
        h1, h2 = b[o] | (b[o + 1] << 8), b[o + 2] | (b[o + 3] << 8)
        if (h1 & 0xF800) == 0xF000 and (h2 & 0xD000) in (0xD000, 0x9000):
            m.update(range(a, a + 4))
    for w in range(start & ~3, start + length, 4):                        # pointers into flash (literal pools, tables)
        if w < BASE or w + 4 > 0x08010000:
            continue
        v = struct.unpack_from('<I', b, w - 0x08000000)[0]
        if 0x08000000 <= v < 0x08010000:
            m.update(range(w, w + 4))
    return m


def matches(b, lo, hi, pat):
    """addresses where pat (list of byte or None) matches in b, searching halfword-aligned in [lo, hi)"""
    out = []
    f0 = next(i for i, x in enumerate(pat) if x is not None)
    v0 = pat[f0]
    for a in range(lo, hi - len(pat) + 1, 2):
        o = a - 0x08000000
        if b[o + f0] != v0:
            continue
        if all(x is None or b[o + i] == x for i, x in enumerate(pat)):
            out.append(a)
            if len(out) > 1:
                break
    return out


def dec_bl(src, b4):
    h1, h2 = b4 & 0xFFFF, b4 >> 16
    s = (h1 >> 10) & 1
    j1, j2 = (h2 >> 13) & 1, (h2 >> 11) & 1
    i1, i2 = (~(j1 ^ s)) & 1, (~(j2 ^ s)) & 1
    off = (s << 24) | (i1 << 23) | (i2 << 22) | ((h1 & 0x3FF) << 12) | ((h2 & 0x7FF) << 1)
    if s:
        off -= 1 << 25
    return src + 4 + off


def main(argv):
    out, writes = argv[0], argv[1]
    k = argv.index('--')
    refs, srcs = argv[2:k], argv[k + 1:]
    # cmd.exe and PowerShell pass "src\*.cs" through as it is (only Unix shells expand it): expand it here
    srcs = [f for s in srcs for f in (sorted(glob.glob(s)) or [s])]
    imgs = []
    for r in refs:
        # REF.bin[:MAP]: the map follows the LAST colon and always holds '=', so a Windows path (C:\...) stays whole
        path, sep, mp = r.rpartition(':')
        if not sep or '=' not in mp:
            path, mp = r, ''
        imgs.append((load(path), parse_map(mp)))
    ends = [fw_end(b) for b, _ in imgs]

    # bytes the tool rewrites (reference addresses)
    wr = set()
    for line in open(writes, errors='replace'):
        m = re.match(r'\s+0x(080[0-9A-Fa-f]{5}):\s+(?:\+(\d+) bytes|([0-9a-fA-F]+) -> )', line)
        if not m:
            continue
        a = int(m.group(1), 16)
        n = int(m.group(2)) if m.group(2) else len(m.group(3)) // 2
        if a < ends[0]:
            wr.update(range(a, a + n))

    # symbols
    syms = {}
    src = '\n'.join(open(s).read() for s in srcs)
    for m in re.finditer(r'\b(A|Ptr|Sym)\(0x(080[0-9A-Fa-f]{5})\)', src):
        syms[int(m.group(2), 16)] = m.group(1)
    ref = imgs[0][0]
    for m in re.finditer(r'(?:Expect|Branch)\(Fw\.A\(0x(080[0-9A-Fa-f]{5})\), Image\.Hw2\((0x[0-9A-Fa-f]+), (0x[0-9A-Fa-f]+)\)', src):
        site, h1, h2 = int(m.group(1), 16), int(m.group(2), 16), int(m.group(3), 16)
        if (h1 & 0xF800) == 0xF000 and (h2 & 0xD000) in (0xD000, 0x9000):
            syms.setdefault(dec_bl(site, h1 | (h2 << 16)), 'A')
    for a in syms:
        assert BASE <= a < ends[0], f'{a:#x} is outside the reference firmware'

    def prologue(a):
        o = a - 0x08000000
        h1, h2 = ref[o] | (ref[o + 1] << 8), ref[o + 2] | (ref[o + 3] << 8)
        return (h1 & 0xFF00) == 0xB500 or (h1 == 0xE92D and (h2 & 0x4000))      # push {..., lr} / push.w {..., lr}

    def bounds(s):
        """the symbol's own function: from its prologue to the next one (a window inside it survives code being
        added or removed in other functions)"""
        lo = s
        while lo > BASE and s - lo < 512 and not prologue(lo):
            lo -= 2
        hi = s + 2
        while hi < ends[0] and hi - s < 512 and not prologue(hi):
            hi += 2
        return (lo if prologue(lo) else BASE), hi

    lines, bad, wide = [], [], []
    for s in sorted(syms):
        best = None
        flo, fhi = bounds(s)
        for inside in (True, False):
          for length in range(8, 81, 2):
              for back in range(0, min(length - 2, 40) + 1, 2):
                  st = s - back
                  if st & 1:
                      st -= 1
                  if st < BASE or st + length > ends[0]:
                      continue
                  if not (st <= s < st + length):
                      continue
                  if inside and (st < flo or st + length > fhi):
                      continue
                  masked = wr | reloc_mask(ref, st, length)
                  pat = [None if a in masked else ref[a - 0x08000000] for a in range(st, st + length)]
                  ok = True
                  for (b, mp), e in list(zip(imgs, ends))[1:]:
                      st2 = mp(s) - (s - st)
                      if st2 < BASE or st2 + length > e:
                          ok = False
                          break
                      m2 = reloc_mask(b, st2, length)
                      for i in range(length):
                          if pat[i] is not None and (st2 + i in m2 or b[st2 + i - 0x08000000] != pat[i]):
                              pat[i] = None
                  if not ok or sum(x is not None for x in pat) < 12:
                      continue
                  if all(matches(b, BASE, e, pat) == [mp(s) - (s - st)] for (b, mp), e in zip(imgs, ends)):
                      best = (st, pat)
                      break
              if best:
                  break
          if best:
              if not inside:
                  wide.append(s)
              break
        if not best:
            bad.append(s)
            continue
        st, pat = best
        mask, q, fp = fingerprint(pat)
        cols = '|'.join(f'{mp(s):08X}' for _, mp in imgs[1:])
        lines.append(f'            "{s:08X}|{s - st}|{mask}|{q}|{fp}|{cols}",')
    if bad:
        raise SystemExit('no unique signature for: ' + ', '.join(f'{a:#010x}' for a in bad))
    with open(out, 'w', newline='\n') as f:
        f.write('// atf_symbols.cs - GENERATED by tools/mksig.py; do not edit by hand (see atf_layout.cs).\n')
        f.write('// One line per firmware location: February address | offset of the location in the signature | mask\n')
        f.write('// (x = byte that must match, . = masked) | 16-bit quick check of the first four fixed bytes | first 64 bits of the\n')
        f.write('// SHA-256 of the window with masked bytes set to 0 | its address in each further verified build (checked,\n')
        f.write('// Layout.KNOWN). No firmware bytes are stored here (R42).\n')
        f.write('namespace AnargonsTabletTools\n{\n    public static class FwSigs\n    {\n        public static readonly string[] S = new string[] {\n')
        f.write('\n'.join(lines) + '\n        };\n    }\n}\n')
    print(f'{len(lines)} signatures -> {out}' + (f' ({len(wide)} cross a function boundary: ' + ', '.join(f'{a:#010x}' for a in wide) + ')' if wide else ''))


if __name__ == '__main__':
    main(sys.argv[1:])
