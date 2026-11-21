#!/usr/bin/env python3
"""Thumb-2 disassembler for the Deco 640 firmware (stdlib only, no capstone).

usage: python -I tools/dis.py IMAGE START [END|+LEN] [START [END|+LEN] ...]
IMAGE is a .bin (full 64 KB dump or application-only) or an Intel HEX file. Addresses are hex.
Covers the instruction subset the digital twin executes (the firmware uses nothing else).
"""
import struct
import sys

M32 = 0xFFFFFFFF
REG = ['r0', 'r1', 'r2', 'r3', 'r4', 'r5', 'r6', 'r7', 'r8', 'r9', 'r10', 'r11', 'ip', 'sp', 'lr', 'pc']
COND = ['eq', 'ne', 'cs', 'cc', 'mi', 'pl', 'vs', 'vc', 'hi', 'ls', 'ge', 'lt', 'gt', 'le', '', '']
SHIFT = ['lsl', 'lsr', 'asr', 'ror']
DP_NAMES = {0: 'and', 1: 'bic', 2: 'orr', 3: 'orn', 4: 'eor', 8: 'add', 10: 'adc', 11: 'sbc', 13: 'sub', 14: 'rsb'}
DP_TEST = {0: 'tst', 4: 'teq', 8: 'cmn', 13: 'cmp'}


def sx(v, bits):
    return v - (1 << bits) if v & (1 << (bits - 1)) else v


def load(path):
    """64 KB flash image (0xFF-filled) from a .bin (full dump or app-only) or Intel HEX."""
    flash = bytearray(b'\xff' * 0x10000)
    raw = open(path, 'rb').read()
    if raw[:1] == b':':
        base = 0
        for line in raw.decode('ascii', 'replace').splitlines():
            line = line.strip()
            if not line:
                continue
            rec = bytes.fromhex(line[1:])
            n, a, t = rec[0], (rec[1] << 8) | rec[2], rec[3]
            if t == 0 and 0x08000000 <= base + a < 0x08010000:
                o = base + a - 0x08000000
                flash[o:o + n] = rec[4:4 + n]
            elif t == 4:
                base = ((rec[4] << 8) | rec[5]) << 16
        return bytes(flash)
    if len(raw) >= 0x3804 and struct.unpack_from('<I', raw, 0x3800)[0] & 0x2FFE0000 == 0x20000000:
        flash[:min(len(raw), 0x10000)] = raw[:0x10000]
    else:
        flash[0x3800:0x3800 + len(raw)] = raw[:0x10000 - 0x3800]
    return bytes(flash)


def regs(mask, n=16):
    return '{' + ', '.join(REG[k] for k in range(n) if mask & (1 << k)) + '}'


def expand_imm(imm12):
    if (imm12 >> 10) == 0:
        imm8 = imm12 & 0xFF
        return [imm8, (imm8 << 16) | imm8, (imm8 << 24) | (imm8 << 8), imm8 * 0x01010101][(imm12 >> 8) & 3]
    unrot = 0x80 | (imm12 & 0x7F)
    rot = (imm12 >> 7) & 0x1F
    return ((unrot >> rot) | (unrot << (32 - rot))) & M32


def shift_txt(t, imm5):
    if t == 0 and imm5 == 0:
        return ''
    if t == 3 and imm5 == 0:
        return ', rrx'
    return f', {SHIFT[t]} #{imm5 if imm5 or t == 0 else 32}'


class Dis:
    def __init__(self, img):
        self.img = img

    def half(self, a):
        o = a - 0x08000000
        return self.img[o] | (self.img[o + 1] << 8)

    def word(self, a):
        return struct.unpack_from('<I', self.img, a - 0x08000000)[0]

    def lit(self, a):
        try:
            return f'={self.word(a):#010x}'
        except struct.error:
            return ''

    def one(self, a):
        """(size, text, comment) of the instruction at A."""
        h1 = self.half(a)
        if (h1 >> 11) in (0b11101, 0b11110, 0b11111):
            txt, cm = self.d32(a, h1, self.half(a + 2))
            return 4, txt, cm
        txt, cm = self.d16(a, h1)
        return 2, txt, cm

    # ------------------------------------------------------------------ 16-bit
    def d16(self, a, h):
        r = REG
        if (h >> 14) == 0:
            opc = (h >> 9) & 0x1F
            rd, rn = h & 7, (h >> 3) & 7
            if opc < 0b01100:
                t, imm5 = opc >> 2, (h >> 6) & 0x1F
                if t == 0 and imm5 == 0:
                    return f'movs {r[rd]}, {r[rn]}', ''
                return f'{SHIFT[t]}s {r[rd]}, {r[rn]}, #{imm5 if imm5 or t == 0 else 32}', ''
            if opc in (0b01100, 0b01101):
                return f'{"adds" if opc == 0b01100 else "subs"} {r[rd]}, {r[rn]}, {r[(h >> 6) & 7]}', ''
            if opc in (0b01110, 0b01111):
                return f'{"adds" if opc == 0b01110 else "subs"} {r[rd]}, {r[rn]}, #{(h >> 6) & 7}', ''
            rr, imm, o = (h >> 8) & 7, h & 0xFF, (opc >> 2) & 3
            return f'{["movs", "cmp", "adds", "subs"][o]} {r[rr]}, #{imm}', (f'{imm:#x}' if imm > 9 else '')
        if (h >> 10) == 0b010000:
            o, rdn, rm = (h >> 6) & 0xF, h & 7, (h >> 3) & 7
            if o == 9:
                return f'rsbs {r[rdn]}, {r[rm]}, #0', ''
            names = ['ands', 'eors', 'lsls', 'lsrs', 'asrs', 'adcs', 'sbcs', 'rors', 'tst', 'rsbs', 'cmp', 'cmn',
                     'orrs', 'muls', 'bics', 'mvns']
            return f'{names[o]} {r[rdn]}, {r[rm]}', ''
        if (h >> 10) == 0b010001:
            o, rm, rdn = (h >> 8) & 3, (h >> 3) & 0xF, ((h >> 4) & 8) | (h & 7)
            if o == 0:
                return f'add {r[rdn]}, {r[rm]}', ''
            if o == 1:
                return f'cmp {r[rdn]}, {r[rm]}', ''
            if o == 2:
                return f'mov {r[rdn]}, {r[rm]}', ''
            return f'{"blx" if h & 0x80 else "bx"} {r[rm]}', ''
        if (h >> 11) == 0b01001:
            la = ((a + 4) & ~3) + (h & 0xFF) * 4
            return f'ldr {r[(h >> 8) & 7]}, [pc, #{(h & 0xFF) * 4}]', f'{la:#010x} {self.lit(la)}'
        if (h >> 12) == 0b0101:
            names = ['str', 'strh', 'strb', 'ldrsb', 'ldr', 'ldrh', 'ldrb', 'ldrsh']
            return f'{names[(h >> 9) & 7]} {r[h & 7]}, [{r[(h >> 3) & 7]}, {r[(h >> 6) & 7]}]', ''
        if (h >> 13) == 0b011:
            B, L, imm = (h >> 12) & 1, (h >> 11) & 1, (h >> 6) & 0x1F
            off = imm if B else imm * 4
            return f'{"ldr" if L else "str"}{"b" if B else ""} {r[h & 7]}, [{r[(h >> 3) & 7]}, #{off}]', ''
        if (h >> 12) == 0b1000:
            L, imm = (h >> 11) & 1, ((h >> 6) & 0x1F) * 2
            return f'{"ldrh" if L else "strh"} {r[h & 7]}, [{r[(h >> 3) & 7]}, #{imm}]', ''
        if (h >> 12) == 0b1001:
            return f'{"ldr" if (h >> 11) & 1 else "str"} {r[(h >> 8) & 7]}, [sp, #{(h & 0xFF) * 4}]', ''
        if (h >> 11) == 0b10100:
            return f'adr {r[(h >> 8) & 7]}, {((a + 4) & ~3) + (h & 0xFF) * 4:#010x}', ''
        if (h >> 11) == 0b10101:
            return f'add {r[(h >> 8) & 7]}, sp, #{(h & 0xFF) * 4}', ''
        if (h >> 12) == 0b1011:
            if (h >> 8) == 0xB0:
                return f'{"sub" if h & 0x80 else "add"} sp, #{(h & 0x7F) * 4}', ''
            if (h & 0x0500) == 0x0100:
                off = ((((h >> 9) & 1) << 5) | ((h >> 3) & 0x1F)) << 1
                return f'{"cbnz" if h & 0x800 else "cbz"} {r[h & 7]}, {a + 4 + off:#010x}', ''
            if (h >> 8) == 0xB2:
                return f'{["sxth", "sxtb", "uxth", "uxtb"][(h >> 6) & 3]} {r[h & 7]}, {r[(h >> 3) & 7]}', ''
            if (h >> 9) == 0b1011010:
                return f'push {regs((h & 0xFF) | (0x4000 if h & 0x100 else 0))}', ''
            if (h >> 9) == 0b1011110:
                return f'pop {regs((h & 0xFF) | (0x8000 if h & 0x100 else 0))}', ''
            if (h & 0xFFE8) == 0xB660:
                return f'cps{"id" if h & 0x10 else "ie"} i', ''
            if (h >> 8) == 0xBA:
                return f'{["rev", "rev16", "?", "revsh"][(h >> 6) & 3]} {r[h & 7]}, {r[(h >> 3) & 7]}', ''
            if (h >> 8) == 0xBE:
                return f'bkpt #{h & 0xFF}', ''
            if (h >> 8) == 0xBF:
                if h & 0xF:
                    fc, mask = (h >> 4) & 0xF, h & 0xF
                    n = 4 - ((mask & -mask).bit_length() - 1)
                    s = ''.join('t' if ((mask >> (4 - k)) & 1) == (fc & 1) else 'e' for k in range(1, n))
                    return f'it{s} {COND[fc]}', ''
                return ['nop', 'yield', 'wfe', 'wfi', 'sev'][min((h >> 4) & 0xF, 4)], ''
        if (h >> 12) == 0b1100:
            return f'{"ldm" if (h >> 11) & 1 else "stm"} {r[(h >> 8) & 7]}!, {regs(h & 0xFF, 8)}', ''
        if (h >> 12) == 0b1101:
            c = (h >> 8) & 0xF
            if c == 0xE:
                return f'udf #{h & 0xFF}', ''
            if c == 0xF:
                return f'svc #{h & 0xFF}', ''
            return f'b{COND[c]} {a + 4 + sx(h & 0xFF, 8) * 2:#010x}', ''
        if (h >> 11) == 0b11100:
            return f'b {a + 4 + sx(h & 0x7FF, 11) * 2:#010x}', ''
        return f'.short {h:#06x}', '??'

    # ------------------------------------------------------------------ 32-bit
    def d32(self, a, h1, h2):
        r = REG
        op1, op2, rn = (h1 >> 11) & 3, (h1 >> 4) & 0x7F, h1 & 0xF
        if op1 == 1:
            if (op2 & 0b1100100) == 0:
                o, W, L = (h1 >> 7) & 3, (h1 >> 5) & 1, (h1 >> 4) & 1
                if rn == 13 and W and o == 2 and not L:
                    return f'push.w {regs(h2)}', ''
                if rn == 13 and W and o == 1 and L:
                    return f'pop.w {regs(h2)}', ''
                return f'{"ldm" if L else "stm"}{"ia" if o == 1 else "db"}.w {r[rn]}{"!" if W else ""}, {regs(h2)}', ''
            if (op2 & 0b1100100) == 0b0000100:
                o1, o2, o3 = (h1 >> 7) & 3, (h1 >> 4) & 3, (h2 >> 4) & 0xF
                if o1 == 1 and o2 == 1 and o3 in (0, 1):
                    return f'tb{"h" if o3 else "b"} [{r[rn]}, {r[h2 & 0xF]}{", lsl #1" if o3 else ""}]', ''
                P, U, W, L = (h1 >> 8) & 1, (h1 >> 7) & 1, (h1 >> 5) & 1, (h1 >> 4) & 1
                rt, rt2, imm = h2 >> 12, (h2 >> 8) & 0xF, (h2 & 0xFF) * 4
                if (o1 & 2) == 0 and o2 in (0, 1) and not P and not W:
                    return f'ldrex/strex {h1:04x} {h2:04x}', ''
                off = imm if U else -imm
                m = f'[{r[rn]}, #{off}]{"!" if W else ""}' if P else f'[{r[rn]}], #{off}'
                return f'{"ldrd" if L else "strd"} {r[rt]}, {r[rt2]}, {m}', ''
            if (op2 & 0b1100000) == 0b0100000:
                o, S, rd, rm = (h1 >> 5) & 0xF, (h1 >> 4) & 1, (h2 >> 8) & 0xF, h2 & 0xF
                imm5, t = (((h2 >> 12) & 7) << 2) | ((h2 >> 6) & 3), (h2 >> 4) & 3
                sh, s = shift_txt(t, imm5), ('s' if S else '')
                if o == 2 and rn == 15:
                    if not sh:
                        return f'mov{s}.w {r[rd]}, {r[rm]}', ''
                    return f'{SHIFT[t]}{s}.w {r[rd]}, {r[rm]}, #{imm5 if imm5 or t == 0 else 32}', ''
                if o == 3 and rn == 15:
                    return f'mvn{s}.w {r[rd]}, {r[rm]}{sh}', ''
                if rd == 15 and S and o in DP_TEST:
                    return f'{DP_TEST[o]}.w {r[rn]}, {r[rm]}{sh}', ''
                return f'{DP_NAMES.get(o, "dp?")}{s}.w {r[rd]}, {r[rn]}, {r[rm]}{sh}', ''
            return self.cp(h1, h2)
        if op1 == 2:
            if not (h2 & 0x8000):
                i, imm3, rd, imm8 = (h1 >> 10) & 1, (h2 >> 12) & 7, (h2 >> 8) & 0xF, h2 & 0xFF
                if (op2 & 0b0100000) == 0:
                    o, S = (h1 >> 5) & 0xF, (h1 >> 4) & 1
                    v = expand_imm((i << 11) | (imm3 << 8) | imm8)
                    s, cm = ('s' if S else ''), (f'{v:#x}' if v > 9 else '')
                    if o == 2 and rn == 15:
                        return f'mov{s}.w {r[rd]}, #{v}', cm
                    if o == 3 and rn == 15:
                        return f'mvn{s}.w {r[rd]}, #{v}', f'= {(~v) & M32:#x}'
                    if rd == 15 and S and o in DP_TEST:
                        return f'{DP_TEST[o]}.w {r[rn]}, #{v}', cm
                    return f'{DP_NAMES.get(o, "dp?")}{s}.w {r[rd]}, {r[rn]}, #{v}', cm
                o = (h1 >> 4) & 0x1F
                imm12 = (i << 11) | (imm3 << 8) | imm8
                if o == 0:
                    if rn == 15:
                        return f'adr.w {r[rd]}, {((a + 4) & ~3) + imm12:#010x}', ''
                    return f'addw {r[rd]}, {r[rn]}, #{imm12}', ''
                if o == 0b01010:
                    return f'subw {r[rd]}, {r[rn]}, #{imm12}', ''
                if o in (0b00100, 0b01100):
                    v = (rn << 12) | imm12
                    return f'{"movw" if o == 0b00100 else "movt"} {r[rd]}, #{v}', f'{v:#x}'
                lsb, w = (imm3 << 2) | ((h2 >> 6) & 3), h2 & 0x1F
                if o == 0b11100:
                    return f'ubfx {r[rd]}, {r[rn]}, #{lsb}, #{w + 1}', ''
                if o == 0b10100:
                    return f'sbfx {r[rd]}, {r[rn]}, #{lsb}, #{w + 1}', ''
                if o == 0b10110:
                    if rn == 15:
                        return f'bfc {r[rd]}, #{lsb}, #{w - lsb + 1}', ''
                    return f'bfi {r[rd]}, {r[rn]}, #{lsb}, #{w - lsb + 1}', ''
                if o in (0b11000, 0b11010):
                    return f'usat {r[rd]}, #{w}, {r[rn]}', ''
                if o in (0b10000, 0b10010):
                    return f'ssat {r[rd]}, #{w + 1}, {r[rn]}', ''
                return f'plain-imm? {h1:04x} {h2:04x}', '??'
            o1, S, J1, J2 = (h2 >> 12) & 7, (h1 >> 10) & 1, (h2 >> 13) & 1, (h2 >> 11) & 1
            if (o1 & 0b101) == 0:
                if (op2 & 0b0111000) != 0b0111000:
                    off = sx((S << 20) | (J2 << 19) | (J1 << 18) | ((h1 & 0x3F) << 12) | ((h2 & 0x7FF) << 1), 21)
                    return f'b{COND[(h1 >> 6) & 0xF]}.w {a + 4 + off:#010x}', ''
                if (op2 & 0b1111110) == 0b0111000:
                    return f'msr {h2 & 0xFF}, {r[rn]}', ''
                if (op2 & 0b1111110) == 0b0111110:
                    return f'mrs {r[(h2 >> 8) & 0xF]}, {h2 & 0xFF}', ''
                return 'nop.w / barrier', ''
            I1, I2 = 1 - (J1 ^ S), 1 - (J2 ^ S)
            off = sx((S << 24) | (I1 << 23) | (I2 << 22) | ((h1 & 0x3FF) << 12) | ((h2 & 0x7FF) << 1), 25)
            t = a + 4 + off
            if (o1 & 0b101) == 0b101:
                return f'bl {t:#010x}', ''
            if (o1 & 0b101) == 0b001:
                return f'b.w {t:#010x}', ''
            return f'blx {t:#010x}', ''
        # op1 == 3
        if (op2 & 0b1110001) == 0 or (op2 & 0b1100111) in (1, 3, 5):
            Sg, sz, L, rt = (h1 >> 8) & 1, (h1 >> 5) & 3, (h1 >> 4) & 1, h2 >> 12
            nm = ('ldr' if L else 'str') + ('s' if Sg else '') + {0: 'b', 1: 'h', 2: ''}.get(sz, '?')
            if rn == 15:
                U, imm = (h1 >> 7) & 1, h2 & 0xFFF
                la = ((a + 4) & ~3) + (imm if U else -imm)
                return f'{nm}.w {r[rt]}, [pc, #{imm if U else -imm}]', f'{la:#010x} {self.lit(la) if sz == 2 else ""}'
            if (h1 >> 7) & 1:
                return f'{nm}.w {r[rt]}, [{r[rn]}, #{h2 & 0xFFF}]', ''
            if (h2 >> 11) & 1:
                P, U, W, imm = (h2 >> 10) & 1, (h2 >> 9) & 1, (h2 >> 8) & 1, h2 & 0xFF
                off = imm if U else -imm
                if P:
                    return f'{nm} {r[rt]}, [{r[rn]}, #{off}]{"!" if W else ""}', ''
                return f'{nm} {r[rt]}, [{r[rn]}], #{off}', ''
            shv = (h2 >> 4) & 3
            return f'{nm}.w {r[rt]}, [{r[rn]}, {r[h2 & 0xF]}{f", lsl #{shv}" if shv else ""}]', ''
        if (op2 & 0b1110000) == 0b0100000:
            o1, o2, rd, rm = (h1 >> 4) & 0xF, (h2 >> 4) & 0xF, (h2 >> 8) & 0xF, h2 & 0xF
            if o2 == 0 and o1 < 8:
                return f'{SHIFT[o1 >> 1]}{"s" if o1 & 1 else ""}.w {r[rd]}, {r[rn]}, {r[rm]}', ''
            if (o2 & 8) and o1 < 6:
                rot = ((h2 >> 4) & 3) * 8
                nm = {0: 'sxth', 1: 'uxth', 4: 'sxtb', 5: 'uxtb'}.get(o1, 'xt?')
                rs = f', ror #{rot}' if rot else ''
                if rn == 15:
                    return f'{nm}.w {r[rd]}, {r[rm]}{rs}', ''
                return f'{nm[0]}xta{nm[3]} {r[rd]}, {r[rn]}, {r[rm]}{rs}', ''
            if (o1 & 0xC) == 8 and (o2 & 0xC) == 8:
                o = ((o1 & 3) << 2) | (o2 & 3)
                nm = {0b1100: 'clz', 0b0100: 'rev', 0b0101: 'rev16', 0b0110: 'rbit', 0b0111: 'revsh'}.get(o, 'misc?')
                return f'{nm} {r[rd]}, {r[rm]}', ''
            return f'dp-reg? {h1:04x} {h2:04x}', '??'
        if (op2 & 0b1111000) == 0b0110000:
            o1, o2, ra, rd, rm = (h1 >> 4) & 7, (h2 >> 4) & 3, h2 >> 12, (h2 >> 8) & 0xF, h2 & 0xF
            if o1 == 0 and o2 == 0:
                if ra == 15:
                    return f'mul {r[rd]}, {r[rn]}, {r[rm]}', ''
                return f'mla {r[rd]}, {r[rn]}, {r[rm]}, {r[ra]}', ''
            if o1 == 0 and o2 == 1:
                return f'mls {r[rd]}, {r[rn]}, {r[rm]}, {r[ra]}', ''
            if o1 == 1:
                xy = ('t' if h2 & 0x20 else 'b') + ('t' if h2 & 0x10 else 'b')
                if ra == 15:
                    return f'smul{xy} {r[rd]}, {r[rn]}, {r[rm]}', ''
                return f'smla{xy} {r[rd]}, {r[rn]}, {r[rm]}, {r[ra]}', ''
            return f'mul? {h1:04x} {h2:04x}', '??'
        if (op2 & 0b1111000) == 0b0111000:
            o1, o2, lo, hi, rm = (h1 >> 4) & 7, (h2 >> 4) & 0xF, h2 >> 12, (h2 >> 8) & 0xF, h2 & 0xF
            if o1 == 3 and o2 == 0xF:
                return f'udiv {r[hi]}, {r[rn]}, {r[rm]}', ''
            if o1 == 1 and o2 == 0xF:
                return f'sdiv {r[hi]}, {r[rn]}, {r[rm]}', ''
            nm = {(2, 0): 'umull', (0, 0): 'smull', (6, 0): 'umlal', (4, 0): 'smlal'}.get((o1, o2), 'lmul?')
            return f'{nm} {r[lo]}, {r[hi]}, {r[rn]}, {r[rm]}', ''
        return self.cp(h1, h2)

    def cp(self, h1, h2):
        if (h2 & 0x0F00) == 0x0A00:
            D, Vd, N, M = (h1 >> 6) & 1, (h2 >> 12) & 0xF, (h2 >> 7) & 1, (h2 >> 5) & 1
            Vn, Vm = h1 & 0xF, h2 & 0xF
            sd, sn, sm = (Vd << 1) | D, (Vn << 1) | N, (Vm << 1) | M
            if (h1 & 0xFFE0) == 0xEE00 and (h2 & 0x0F7F) == 0x0A10:
                rt = h2 >> 12
                return (f'vmov {REG[rt]}, s{sn}' if h1 & 0x10 else f'vmov s{sn}, {REG[rt]}'), ''
            if (h1 & 0xFFB8) == 0xEEB8 and (h2 & 0x0E50) == 0x0A40:
                opc2, op = h1 & 7, (h2 >> 7) & 1
                if opc2 == 0:
                    return f'vcvt.f32.{"s32" if op else "u32"} s{sd}, s{sm}', ''
                return f'vcvt{"" if op else "r"}.{"s32" if opc2 == 5 else "u32"}.f32 s{sd}, s{sm}', ''
            if (h1 & 0xFFB0) == 0xEE80 and (h2 & 0x0E50) == 0x0A00:
                return f'vdiv.f32 s{sd}, s{sn}, s{sm}', ''
            if (h1 & 0xFFB0) == 0xEE30 and (h2 & 0x0E10) == 0x0A00:
                return f'{"vsub" if h2 & 0x40 else "vadd"}.f32 s{sd}, s{sn}, s{sm}', ''
            if (h1 & 0xFFB0) == 0xEE20 and (h2 & 0x0E10) == 0x0A00:
                return f'vmul.f32 s{sd}, s{sn}, s{sm}', ''
        return f'cp? {h1:04x} {h2:04x}', '??'


def listing(img, start, end):
    d = Dis(img)
    a, out = start, []
    while a < end:
        size, txt, cm = d.one(a)
        raw = bytes(img[a - 0x08000000:a - 0x08000000 + size]).hex()
        out.append(f'{a:08x}: {raw:<10s} {txt}' + (f'   ; {cm}' if cm else ''))
        a += size
    return out


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    img = load(argv[0])
    args = argv[1:]
    for i in range(0, len(args), 2):
        a = int(args[i], 16)
        e = args[i + 1] if i + 1 < len(args) else '+0x40'
        end = a + int(e[1:], 16) if e.startswith('+') else int(e, 16)
        print(f'---- {a:#010x}..{end:#010x}')
        print('\n'.join(listing(img, a, end)))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
