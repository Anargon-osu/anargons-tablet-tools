// atf_patcher.cs - XPPen Deco 640 (GD32F350, "deco01v3", 2025-02-14 release) firmware builder.
//
// Build:  build.cmd  (uses the C# compiler that ships with Windows; no installs), or the .csproj.
//   AnargonsTabletTools.exe                                   step-by-step wizard
//   AnargonsTabletTools.exe deco01v3-main.bin build --rate fast
//   AnargonsTabletTools.exe deco01v3-main.bin build --all
//   AnargonsTabletTools.exe --check some.hex                  digital-twin check of any image
//   AnargonsTabletTools.exe twin IMAGE ...                    the digital twin's own tests (see "AnargonsTabletTools.exe twin --help")
//
// Every build is checked in the digital twin (atf_twin.cs, built into the same exe) before it can be flashed.
// The resident bootloader's ERASE clears 0x08003800-0x0800FFFF and PROGRAM writes nothing without it, so every image
// is complete. Added code lives in free flash after the stock image (0x0800C7CC-0x0800FFFF).
// Written in the same plain C# subset as the twin; reference/deco640_patcher.py is the Python original it must match.
using System;
using System.Text;
using Gd32Emu;

namespace AnargonsTabletTools
{
    // ------------------------------------------------------------------------------------ small containers
    public class StrList
    {
        public string[] a = new string[16];
        public int n = 0;
        public void Add(string s) { if (n == a.Length) { string[] g = new string[n * 2]; P.Copy(a, g, n); a = g; } a[n] = s; n++; }
        public string Get(int i) { return a[i]; }
        public int Count() { return n; }
    }

    public class StrIntMap
    {
        public string[] k = new string[64];
        public int[] v = new int[64];
        public int n = 0;
        public int Find(string key) { for (int i = 0; i < n; i++) if (P.Eq(k[i], key)) return i; return -1; }
        public bool Has(string key) { return Find(key) >= 0; }
        public int Get(string key) { int i = Find(key); if (i < 0) throw new Stop("internal: unknown label " + key); return v[i]; }
        public void Set(string key, int val)
        {
            int i = Find(key);
            if (i >= 0) { v[i] = val; return; }
            if (n == k.Length) { string[] gk = new string[n * 2]; int[] gv = new int[n * 2]; P.Copy(k, gk, n); P.Copy(v, gv, n); k = gk; v = gv; }
            k[n] = key; v[n] = val; n++;
        }
    }

    public static class Txt
    {
        public static string PadL(string s, int w) { while (P.Len(s) < w) s = " " + s; return s; }
        public static string PadR(string s, int w) { while (P.Len(s) < w) s = s + " "; return s; }
        public static string HexBytes(byte[] b)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < b.Length; i++) sb.Append(F.HexDigits(b[i] & 0xFF, 2, false));
            return sb.ToString();
        }
        // greedy word wrap on spaces (like Python's textwrap.wrap for these texts)
        public static StrList Wrap(string text, int width)
        {
            StrList outl = new StrList();
            string[] words = P.Split(text, ' ');
            string line = "";
            for (int i = 0; i < words.Length; i++)
            {
                string w = words[i];
                if (P.Len(w) == 0) continue;
                if (P.Len(line) == 0) line = w;
                else if (P.Len(line) + 1 + P.Len(w) <= width) line = line + " " + w;
                else { outl.Add(line); line = w; }
            }
            if (P.Len(line) > 0) outl.Add(line);
            return outl;
        }
        public static long Crc32(byte[] b, int off, int n)
        {
            long c = 0xFFFFFFFFL;
            for (int i = 0; i < n; i++)
            {
                c ^= b[off + i] & 0xFF;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320L : c >> 1;
            }
            return c ^ 0xFFFFFFFFL;
        }
    }

    // ------------------------------------------------------------------------------------ Thumb-2 assembler
    public class Asm
    {
        public const int K_H = 0, K_HH = 1, K_W = 2, K_B = 3, K_BCC = 4, K_CBZ = 5, K_LDRLIT = 6, K_BL = 7, K_ADR = 8, K_BCCW = 9;
        public int bas, pc;
        public int[] kind = new int[256], at = new int[256], x1 = new int[256], x2 = new int[256], x3 = new int[256];
        public string[] lab = new string[256];
        public int n = 0;
        public StrIntMap labels = new StrIntMap();
        public bool literalAsMov32 = false;

        public Asm(int b) { bas = b; pc = b; }

        public void Item(int k, int a1, int a2, int a3, string l, int size)
        {
            if (n == kind.Length)
            {
                int m = n * 2;
                int[] k2 = new int[m], t2 = new int[m], y1 = new int[m], y2 = new int[m], y3 = new int[m]; string[] l2 = new string[m];
                P.Copy(kind, k2, n); P.Copy(at, t2, n); P.Copy(x1, y1, n); P.Copy(x2, y2, n); P.Copy(x3, y3, n); P.Copy(lab, l2, n);
                kind = k2; at = t2; x1 = y1; x2 = y2; x3 = y3; lab = l2;
            }
            kind[n] = k; at[n] = pc; x1[n] = a1; x2[n] = a2; x3[n] = a3; lab[n] = l; n++;
            pc += size;
        }

        public static int R(string r)
        {
            if (P.Eq(r, "sp")) return 13;
            if (P.Eq(r, "lr")) return 14;
            if (P.Eq(r, "pc")) return 15;
            if (P.Eq(r, "ip")) return 12;
            return (int)F.ParseInt(P.Sub(r, 1, P.Len(r) - 1));
        }
        static void Chk(bool ok, string what) { if (!ok) throw new Stop("assembler: " + what); }

        public void Label(string name) { Chk(!labels.Has(name), "duplicate label " + name); labels.Set(name, pc); }
        public void Align4() { if (pc % 4 != 0) H(0xBF00); }
        public void H(int v) { Item(K_H, v & 0xFFFF, 0, 0, null, 2); }
        public void W32(int h1, int h2) { Item(K_HH, h1 & 0xFFFF, h2 & 0xFFFF, 0, null, 4); }
        public void Movs(string rd, int imm) { Chk(imm >= 0 && imm < 256, "movs imm"); H(0x2000 | (R(rd) << 8) | imm); }
        public void CmpI(string rn, int imm) { Chk(imm >= 0 && imm < 256, "cmp imm"); H(0x2800 | (R(rn) << 8) | imm); }
        public void AddsI(string rd, int imm) { Chk(imm >= 0 && imm < 256, "adds imm"); H(0x3000 | (R(rd) << 8) | imm); }
        public void SubsI(string rd, int imm) { Chk(imm >= 0 && imm < 256, "subs imm"); H(0x3800 | (R(rd) << 8) | imm); }
        public void AddsR(string rd, string rn, string rm) { H(0x1800 | (R(rm) << 6) | (R(rn) << 3) | R(rd)); }
        public void SubsR(string rd, string rn, string rm) { H(0x1A00 | (R(rm) << 6) | (R(rn) << 3) | R(rd)); }
        public void Lsls(string rd, string rm, int imm) { Chk(imm >= 0 && imm < 32, "lsls"); H((imm << 6) | (R(rm) << 3) | R(rd)); }
        public void Lsrs(string rd, string rm, int imm) { Chk(imm >= 0 && imm < 32, "lsrs"); H(0x0800 | (imm << 6) | (R(rm) << 3) | R(rd)); }
        public void Asrs(string rd, string rm, int imm) { Chk(imm > 0 && imm <= 32, "asrs"); H(0x1000 | ((imm & 31) << 6) | (R(rm) << 3) | R(rd)); }
        public void LdrbR(string rt, string rn, string rm) { H(0x5C00 | (R(rm) << 6) | (R(rn) << 3) | R(rt)); }
        public void StrbR(string rt, string rn, string rm) { H(0x5400 | (R(rm) << 6) | (R(rn) << 3) | R(rt)); }
        public void Eors(string rd, string rm) { H(0x4040 | (R(rm) << 3) | R(rd)); }
        public void Ands(string rd, string rm) { H(0x4000 | (R(rm) << 3) | R(rd)); }
        public void Orrs(string rd, string rm) { H(0x4300 | (R(rm) << 3) | R(rd)); }
        public void Muls(string rd, string rm) { H(0x4340 | (R(rm) << 3) | R(rd)); }
        public void LslsR(string rd, string rs) { H(0x4080 | (R(rs) << 3) | R(rd)); }
        public void LsrsR(string rd, string rs) { H(0x40C0 | (R(rs) << 3) | R(rd)); }
        public void AsrsR(string rd, string rs) { H(0x4100 | (R(rs) << 3) | R(rd)); }
        public void Tst(string rn, string rm) { H(0x4200 | (R(rm) << 3) | R(rn)); }
        public void Bics(string rd, string rm) { H(0x4380 | (R(rm) << 3) | R(rd)); }
        public void Rsbs(string rd) { H(0x4240 | (R(rd) << 3) | R(rd)); }
        public void Uxth(string rd, string rm) { H(0xB280 | (R(rm) << 3) | R(rd)); }
        public void Uxtb(string rd, string rm) { H(0xB2C0 | (R(rm) << 3) | R(rd)); }
        public void Sxtb(string rd, string rm) { H(0xB240 | (R(rm) << 3) | R(rd)); }
        public void Sxth(string rd, string rm) { H(0xB200 | (R(rm) << 3) | R(rd)); }
        public void Mov(string rd, string rm) { int d = R(rd), m = R(rm); H(0x4600 | ((d >> 3) << 7) | (m << 3) | (d & 7)); }
        public void CmpR(string rn, string rm)
        {
            int nn = R(rn), m = R(rm);
            if (nn < 8 && m < 8) H(0x4280 | (m << 3) | nn); else H(0x4500 | ((nn >> 3) << 7) | (m << 3) | (nn & 7));
        }
        public void Ldrb(string rt, string rn, int imm) { Chk(imm >= 0 && imm < 32, "ldrb imm"); H(0x7800 | (imm << 6) | (R(rn) << 3) | R(rt)); }
        public void Strb(string rt, string rn, int imm) { Chk(imm >= 0 && imm < 32, "strb imm"); H(0x7000 | (imm << 6) | (R(rn) << 3) | R(rt)); }
        public void Ldrh(string rt, string rn, int imm) { Chk(imm % 2 == 0 && imm >= 0 && imm < 64, "ldrh imm"); H(0x8800 | ((imm / 2) << 6) | (R(rn) << 3) | R(rt)); }
        public void Strh(string rt, string rn, int imm) { Chk(imm % 2 == 0 && imm >= 0 && imm < 64, "strh imm"); H(0x8000 | ((imm / 2) << 6) | (R(rn) << 3) | R(rt)); }
        public void Ldr(string rt, string rn, int imm) { Chk(imm % 4 == 0 && imm >= 0 && imm < 128, "ldr imm"); H(0x6800 | ((imm / 4) << 6) | (R(rn) << 3) | R(rt)); }
        public void Str(string rt, string rn, int imm) { Chk(imm % 4 == 0 && imm >= 0 && imm < 128, "str imm"); H(0x6000 | ((imm / 4) << 6) | (R(rn) << 3) | R(rt)); }
        public static int Mask(string[] regs) { int m = 0; for (int i = 0; i < regs.Length; i++) m |= 1 << R(regs[i]); return m; }
        public void Push(string[] regs, bool lr) { H(0xB400 | (lr ? 0x100 : 0) | Mask(regs)); }
        public void Pop(string[] regs, bool pcr) { H(0xBC00 | (pcr ? 0x100 : 0) | Mask(regs)); }
        public void PushW(string[] regs, bool lr) { W32(0xE92D, Mask(regs) | (lr ? 1 << 14 : 0)); }
        public void PopW(string[] regs, bool lr) { W32(0xE8BD, Mask(regs) | (lr ? 1 << 14 : 0)); }
        public void BxLr() { H(0x4770); }
        public void Bx(string rm) { H(0x4700 | (R(rm) << 3)); }
        public void Mvns(string rd, string rm) { H(0x43C0 | (R(rm) << 3) | R(rd)); }
        public void AddsI3(string rd, string rn, int imm) { Chk(imm >= 0 && imm < 8, "adds imm3"); H(0x1C00 | (imm << 6) | (R(rn) << 3) | R(rd)); }
        public void SubsI3(string rd, string rn, int imm) { Chk(imm >= 0 && imm < 8, "subs imm3"); H(0x1E00 | (imm << 6) | (R(rn) << 3) | R(rd)); }   // R43
        public void SubSp(int imm) { Chk(imm % 4 == 0 && imm < 512, "sub sp"); H(0xB080 | (imm / 4)); }
        public void AddSp(int imm) { Chk(imm % 4 == 0 && imm < 512, "add sp"); H(0xB000 | (imm / 4)); }
        public void StrSp(string rt, int imm) { Chk(imm % 4 == 0 && imm < 1024, "str sp"); H(0x9000 | (R(rt) << 8) | (imm / 4)); }
        public void Raw16(int v) { H(v); }
        public void Raw(byte[] d) { for (int i = 0; i < d.Length; i += 2) H((d[i] & 0xFF) | ((i + 1 < d.Length ? d[i + 1] & 0xFF : 0) << 8)); }
        public void B(string l) { Item(K_B, 0, 0, 0, l, 2); }
        public void Bcc(string cond, string l) { Item(K_BCC, Cond(cond), 0, 0, l, 2); }
        public void Cbz(string rn, string l) { Item(K_CBZ, R(rn), 0, 0, l, 2); }
        public void Cbnz(string rn, string l) { Item(K_CBZ, R(rn), 1, 0, l, 2); }
        public void Bl(string l) { Item(K_BL, 0, 1, 0, l, 4); }
        public void BlA(int target) { Item(K_BL, target, 1, 0, null, 4); }
        public void BW(string l) { Item(K_BL, 0, 0, 0, l, 4); }
        public void BWA(int target) { Item(K_BL, target, 0, 0, null, 4); }
        public void LdrLitV(string rt, int value) { if (literalAsMov32) Mov32(rt, value); else Item(K_LDRLIT, R(rt), value, 0, null, 2); }
        public void Movw(string rd, int imm) { int[] e = EncMovw(R(rd), imm & 0xFFFF); W32(e[0], e[1]); }
        public void Movt(string rd, int imm) { int[] e = EncMovw(R(rd), imm & 0xFFFF); W32(e[0] | 0x0080, e[1]); }
        public void Mov32(string rd, int v) { Movw(rd, v & 0xFFFF); if ((F.Lsr(v, 16)) != 0) Movt(rd, F.Lsr(v, 16)); }
        public void Udiv(string rd, string rn, string rm) { W32(0xFBB0 | R(rn), 0xF0F0 | (R(rd) << 8) | R(rm)); }
        public void Sdiv(string rd, string rn, string rm) { W32(0xFB90 | R(rn), 0xF0F0 | (R(rd) << 8) | R(rm)); }       // R43
        public void Mla(string rd, string rn, string rm, string ra) { W32(0xFB00 | R(rn), (R(ra) << 12) | (R(rd) << 8) | R(rm)); }   // R43: rd = rn * rm + ra
        public void LdrSp(string rt, int imm) { Chk(imm % 4 == 0 && imm < 1024, "ldr sp"); H(0x9800 | (R(rt) << 8) | (imm / 4)); }   // R43
        public void Mls(string rd, string rn, string rm, string ra) { W32(0xFB00 | R(rn), (R(ra) << 12) | (R(rd) << 8) | 0x10 | R(rm)); }
        public void MvnI0(string rd) { W32(0xF06F, R(rd) << 8); }
        // ---- R43 (Multi-coil Read v2): 32-bit forms, so r8-r12 and lr work like low registers
        public const int OP_AND = 0, OP_ORR = 2, OP_EOR = 4, OP_ADD = 8, OP_SUB = 13, OP_RSB = 14;
        public const int SH_LSL = 0, SH_LSR = 1, SH_ASR = 2;
        // the 12-bit "modified immediate" for v, or -1 when v has no such form (ARM ARM ThumbExpandImm, inverted)
        public static int EncImm(int v)
        {
            if (v >= 0 && v < 256) return v;
            for (int rot = 8; rot < 32; rot++)
            {
                int b = (v << rot) | F.Lsr(v, 32 - rot);                       // v rotated left = the unrotated byte
                if (b >= 0x80 && b < 0x100) return (rot << 7) | (b & 0x7F);
            }
            return -1;
        }
        // data processing with a modified immediate: op rd, rn, #v (S sets the flags; rd 15 = cmp/cmn/tst; rn 15 = mov)
        public void DpImmW(int op, bool s, string rd, string rn, int v)
        {
            int e = EncImm(v);
            Chk(e >= 0, "no immediate form for " + F.Str(v));
            W32(0xF000 | (((e >> 11) & 1) << 10) | (op << 5) | (s ? 0x10 : 0) | R(rn), (((e >> 8) & 7) << 12) | (R(rd) << 8) | (e & 0xFF));
        }
        // data processing with a shifted register: op rd, rn, rm, <type> #amount
        public void DpRegW(int op, bool s, string rd, string rn, string rm, int type, int amount)
        {
            Chk(amount >= 0 && amount < 32, "shift");
            W32(0xEA00 | (op << 5) | (s ? 0x10 : 0) | R(rn), ((amount >> 2) << 12) | (R(rd) << 8) | ((amount & 3) << 6) | (type << 4) | R(rm));
        }
        public void CmpIW(string rn, int v) { DpImmW(OP_SUB, true, "pc", rn, v); }
        public void CmnRW(string rn, string rm) { DpRegW(OP_ADD, true, "pc", rn, rm, SH_LSL, 0); }
        public void CmpRW(string rn, string rm, int lsl) { DpRegW(OP_SUB, true, "pc", rn, rm, SH_LSL, lsl); }
        public void AsrW(string rd, string rm, int amount, bool s) { DpRegW(OP_ORR, s, rd, "pc", rm, SH_ASR, amount); }
        public void LsrW(string rd, string rm, int amount, bool s) { DpRegW(OP_ORR, s, rd, "pc", rm, SH_LSR, amount); }
        public void LdrbW(string rt, string rn, int imm) { Chk(imm >= 0 && imm < 4096, "ldrb.w imm"); W32(0xF890 | R(rn), (R(rt) << 12) | imm); }
        public void LdrW(string rt, string rn, int imm) { Chk(imm >= 0 && imm < 4096, "ldr.w imm"); W32(0xF8D0 | R(rn), (R(rt) << 12) | imm); }
        public void StrW(string rt, string rn, int imm) { Chk(imm >= 0 && imm < 4096, "str.w imm"); W32(0xF8C0 | R(rn), (R(rt) << 12) | imm); }
        public void LslRW(string rd, string rn, string rm) { W32(0xFA00 | R(rn), 0xF000 | (R(rd) << 8) | R(rm)); }
        public void Clz(string rd, string rm) { W32(0xFAB0 | R(rm), 0xF080 | (R(rd) << 8) | R(rm)); }
        public void MulW(string rd, string rn, string rm) { Mla(rd, rn, rm, "pc"); }
        // SMLA<x><y> rd = ra + rn.half * rm.half (signed 16-bit halves; top = the upper half); ra null = SMUL<x><y>
        public void Smla(string rd, string rn, bool nTop, string rm, bool mTop, string ra)
        {
            W32(0xFB10 | R(rn), ((ra == null ? 15 : R(ra)) << 12) | (R(rd) << 8) | (nTop ? 0x20 : 0) | (mTop ? 0x10 : 0) | R(rm));
        }
        // IT <cond>: the next instruction runs only when cond holds (16-bit flag-setting forms then leave the flags)
        public void It(string cond) { H(0xBF08 | (Cond(cond) << 4)); }
        // conditional B.W (+-1 MB) to a label or an absolute address
        public void BccW(string cond, string l) { Item(K_BCCW, Cond(cond), 0, 0, l, 4); }
        public void BccWA(string cond, int target) { Item(K_BCCW, Cond(cond), target, 0, null, 4); }
        public static int[] EncBccW(int src, int dst, int cond)
        {
            int off = dst - (src + 4);
            Chk(off % 2 == 0 && off >= -(1 << 20) && off < (1 << 20), "b<cond>.w range " + F.Hx(src, 8) + " -> " + F.Hx(dst, 8));
            int S = (off >> 20) & 1, J2 = (off >> 19) & 1, J1 = (off >> 18) & 1, imm6 = (off >> 12) & 0x3F, imm11 = (off >> 1) & 0x7FF;
            return new int[] { 0xF000 | (S << 10) | (cond << 6) | imm6, 0x8000 | (J1 << 13) | (J2 << 11) | imm11 };
        }

        public static int Cond(string c)
        {
            string[] names = new string[] { "eq", "ne", "cs", "cc", "mi", "pl", "vs", "vc", "hi", "ls", "ge", "lt", "gt", "le" };
            for (int i = 0; i < names.Length; i++) if (P.Eq(names[i], c)) return i;
            throw new Stop("assembler: condition " + c);
        }

        public static int[] EncMovw(int rd, int imm16)
        {
            int i = (imm16 >> 11) & 1, imm4 = (imm16 >> 12) & 0xF, imm3 = (imm16 >> 8) & 7, imm8 = imm16 & 0xFF;
            return new int[] { 0xF240 | (i << 10) | imm4, (imm3 << 12) | (rd << 8) | imm8 };
        }

        public static int[] EncBl(int src, int dst, bool link)
        {
            int off = dst - (src + 4);
            Chk(off % 2 == 0 && off >= -(1 << 24) && off < (1 << 24), "branch range " + F.Hx(src, 8) + " -> " + F.Hx(dst, 8));
            int S = (off >> 24) & 1, I1 = (off >> 23) & 1, I2 = (off >> 22) & 1;
            int J1 = (~(I1 ^ S)) & 1, J2 = (~(I2 ^ S)) & 1;
            int imm10 = (off >> 12) & 0x3FF, imm11 = (off >> 1) & 0x7FF;
            return new int[] { 0xF000 | (S << 10) | imm10, (link ? 0xD000 : 0x9000) | (J1 << 13) | (J2 << 11) | imm11 };
        }

        // BL / B.W (32-bit, unconditional) at bytes b; DecBl gives its destination when it sits at src
        public static bool IsBl(byte[] b) { return b.Length >= 4 && (b[1] & 0xF8) == 0xF0 && ((b[3] & 0xD0) == 0xD0 || (b[3] & 0xD0) == 0x90); }
        public static int DecBl(int src, byte[] b)
        {
            int h1 = (b[0] & 0xFF) | ((b[1] & 0xFF) << 8), h2 = (b[2] & 0xFF) | ((b[3] & 0xFF) << 8);
            int S = (h1 >> 10) & 1, J1 = (h2 >> 13) & 1, J2 = (h2 >> 11) & 1;
            int I1 = (~(J1 ^ S)) & 1, I2 = (~(J2 ^ S)) & 1;
            int off = (S << 24) | (I1 << 23) | (I2 << 22) | ((h1 & 0x3FF) << 12) | ((h2 & 0x7FF) << 1);
            if (S != 0) off -= 1 << 25;
            return src + 4 + off;
        }

        public int Target(int i) { return lab[i] != null ? labels.Get(lab[i]) : x1[i]; }

        public byte[] Finish()
        {
            if (pc % 4 != 0) H(0xBF00);
            int[] poolV = new int[64], poolA = new int[64];
            int np = 0;
            int items = n;
            for (int i = 0; i < items; i++)
            {
                if (kind[i] != K_LDRLIT) continue;
                bool found = false;
                for (int q = 0; q < np; q++) if (poolV[q] == x2[i]) found = true;
                if (!found) { poolV[np] = x2[i]; poolA[np] = pc; np++; Item(K_W, x2[i], 0, 0, null, 4); }
            }
            Bytes outb = new Bytes();
            int exp = bas;
            for (int i = 0; i < n; i++)
            {
                int k = kind[i], a = at[i];
                Chk(a == exp, "layout");
                if (k == K_H) { Put16(outb, x1[i]); exp += 2; }
                else if (k == K_HH) { Put16(outb, x1[i]); Put16(outb, x2[i]); exp += 4; }
                else if (k == K_W) { Put16(outb, x1[i] & 0xFFFF); Put16(outb, F.Lsr(x1[i], 16)); exp += 4; }
                else if (k == K_B)
                {
                    int off = Target(i) - (a + 4);
                    Chk(off % 2 == 0 && off >= -2048 && off <= 2046, "b range to " + lab[i]);
                    Put16(outb, 0xE000 | ((off >> 1) & 0x7FF)); exp += 2;
                }
                else if (k == K_BCC)
                {
                    int off = Target(i) - (a + 4);
                    Chk(off % 2 == 0 && off >= -256 && off <= 254, "bcc range to " + lab[i]);
                    Put16(outb, 0xD000 | (x1[i] << 8) | ((off >> 1) & 0xFF)); exp += 2;
                }
                else if (k == K_CBZ)
                {
                    int off = Target(i) - (a + 4);
                    Chk(off >= 0 && off <= 126 && off % 2 == 0, "cbz range to " + lab[i]);
                    int imm = off / 2;
                    Put16(outb, 0xB100 | (x2[i] != 0 ? 0x800 : 0) | ((imm >> 5) << 9) | ((imm & 31) << 3) | x1[i]); exp += 2;
                }
                else if (k == K_LDRLIT)
                {
                    int tgt = 0;
                    for (int q = 0; q < np; q++) if (poolV[q] == x2[i]) tgt = poolA[q];
                    int off = tgt - ((a + 4) & ~3);
                    Chk(off >= 0 && off < 1024 && off % 4 == 0, "literal range");
                    Put16(outb, 0x4800 | (x1[i] << 8) | (off / 4)); exp += 2;
                }
                else if (k == K_BL)
                {
                    int[] e = EncBl(a, Target(i), x2[i] != 0);
                    Put16(outb, e[0]); Put16(outb, e[1]); exp += 4;
                }
                else if (k == K_BCCW)
                {
                    int[] e = EncBccW(a, lab[i] != null ? labels.Get(lab[i]) : x2[i], x1[i]);
                    Put16(outb, e[0]); Put16(outb, e[1]); exp += 4;
                }
            }
            return outb.Take(outb.n);
        }

        static void Put16(Bytes b, int v) { b.Add(v & 0xFF); b.Add((v >> 8) & 0xFF); }
    }

    // ------------------------------------------------------------------------------------ the image being patched
    public class Image
    {
        public const int BASE = 0x08003800, FLASH_END = 0x08010000;
        public byte[] data;
        public int len;
        public int cave, inputEnd;
        public StrList log = new StrList(), skipped = new StrList();
        // compatibility probe: when on, Expect records a per-modification mismatch (tagged by `tag`) instead of
        // throwing, so a whole build's site checks can be collected in one pass. probeAll = every modification whose
        // sites were checked; probeBad = those with at least one site that is not the stock code this tool expects.
        public bool probe = false;
        // force: the user confirmed the aggressive incompatibility warning ("I know what I am doing"). Expect no longer
        // throws on a mismatch; the patch is applied at its expected offset anyway (this is the path that can corrupt
        // .data on a firmware the offsets do not match - which is exactly what the warning is about).
        public bool force = false;
        public string tag = "";
        public StrList probeAll = new StrList(), probeBad = new StrList();
        public bool Lenient() { return probe || force; }
        void ProbeAdd(StrList l, string t) { for (int i = 0; i < l.Count(); i++) if (P.Eq(l.Get(i), t)) return; l.Add(t); }
        public void ProbeSeen(string t) { if (probe) ProbeAdd(probeAll, t); }
        public void ProbeFail(string t) { if (probe) { ProbeAdd(probeAll, t); ProbeAdd(probeBad, t); } }
        // a patch whose site could not be verified could not be computed safely either: record it as skipped (force)
        // or as a probe failure, and tell the caller to skip applying it
        public bool Skip(string why) { if (probe) { ProbeFail(tag); return true; } if (force) { skipped.Add("FORCED: " + tag + " not applied (" + why + ")"); return true; } return false; }

        public Image(byte[] app)
        {
            data = new byte[FLASH_END - BASE];
            for (int i = 0; i < data.Length; i++) data[i] = (byte)0xFF;
            P.Copy(app, data, app.Length);
            len = app.Length;
            inputEnd = BASE + app.Length;
            orig = new byte[app.Length]; P.Copy(app, orig, app.Length);
            writer = new int[app.Length];
            for (int i = 0; i < writer.Length; i++) writer[i] = -1;
            cave = inputEnd;
            // added code starts right after this firmware's own image (its end comes from the firmware layout) - also
            // when a truncated input stops earlier, so the place of the missing .data stays free
            if (Fw.cur != null && Fw.cur.end > BASE && Fw.cur.end >= inputEnd) cave = Fw.cur.end;
        }
        // transactional modification groups: a group that cannot find one of its firmware locations is rolled back
        // completely, so a forced build never carries half a modification
        byte[] snapData; int snapLen, snapCave, snapLog, snapSkip;
        // R45: the input firmware as it came, and which modification group (tags) last changed each of its bytes, for the
        // undo record every build carries (OwnRevert.Record). A group's changes are found when the next group begins (or
        // Settle), by comparing with the snapshot taken at its start, so direct writes to `data` count too.
        public byte[] orig;
        public int[] writer;
        public StrList tags = new StrList();
        int lastTag = -1;
        int TagIndex(string t) { for (int i = 0; i < tags.Count(); i++) if (P.Eq(tags.Get(i), t)) return i; tags.Add(t); return tags.Count() - 1; }
        void Attribute()
        {
            if (lastTag < 0 || snapData == null) return;
            for (int i = 0; i < writer.Length; i++) if (data[i] != snapData[i]) writer[i] = lastTag;
        }
        // attributes the open group's changes now (before reading `writer`)
        public void Settle() { Attribute(); if (snapData != null) P.Copy(data, snapData, data.Length); }
        public void Begin(string t)
        {
            Attribute();
            tag = t; lastTag = TagIndex(t);
            if (snapData == null) snapData = new byte[data.Length];
            P.Copy(data, snapData, data.Length); snapLen = len; snapCave = cave; snapLog = log.Count(); snapSkip = skipped.Count();
        }
        // called with the exception a group threw. A missing firmware location rolls the group back and is reported
        // (probe: incompatible; forced: not applied; normal: refused with the reason). Anything else is rethrown.
        public void Fail(Stop e)
        {
            if (!Layout.IsMissing(e)) throw e;
            P.Copy(snapData, data, data.Length); len = snapLen; cave = snapCave;
            StrList l2 = new StrList(); for (int i = 0; i < snapLog; i++) l2.Add(log.Get(i)); log = l2;
            StrList s2 = new StrList(); for (int i = 0; i < snapSkip; i++) s2.Add(skipped.Get(i)); skipped = s2;
            if (probe) { ProbeFail(tag); return; }
            if (force) { skipped.Add("FORCED: " + tag + " not applied - " + e.msg + " in this firmware"); return; }
            throw new Stop("This firmware does not have all the code \"" + tag + "\" needs (" + e.msg + "), so it cannot be patched safely.");
        }
        public bool Present(int a, int n) { return a + n <= inputEnd; }
        public byte[] Rd(int a, int n) { byte[] b = new byte[n]; P.Copy(data, a - BASE, b, 0, n); return b; }
        public int Rd32(int a) { return Machine.LE(data, a - BASE, 4); }
        public void Wr(int a, byte[] b, string note)
        {
            if (a + b.Length > FLASH_END) throw new Stop("patch at " + F.Hx(a, 8) + " runs past the end of flash");
            int end = a - BASE + b.Length;
            if (end > len) len = end;
            byte[] old = Rd(a, b.Length);
            P.Copy(b, 0, data, a - BASE, b.Length);
            string line = b.Length <= 4
                ? "   " + F.Hx(a, 8) + ": " + Txt.PadL(Txt.HexBytes(old), 8) + " -> " + Txt.PadR(Txt.HexBytes(b), 8) + " " + note
                : "   " + F.Hx(a, 8) + ": +" + F.Str(b.Length) + " bytes  " + note;
            log.Add(line);
        }
        public static byte[] Hw(int a) { return new byte[] { (byte)(a & 0xFF), (byte)((a >> 8) & 0xFF) }; }
        public static byte[] Hw2(int a, int b) { return new byte[] { (byte)(a & 0xFF), (byte)((a >> 8) & 0xFF), (byte)(b & 0xFF), (byte)((b >> 8) & 0xFF) }; }
        public static bool Same(byte[] x, byte[] y) { if (x.Length != y.Length) return false; for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false; return true; }
        public void Expect(int a, byte[] b)
        {
            bool ok = Same(Rd(a, b.Length), b);
            // a call or long branch (BL / B.W) is encoded relative to where it sits, so in a firmware whose code moved
            // its bytes differ although it does the same thing: compare WHERE it goes instead (the February target,
            // moved through the layout). The call target is itself a resolved firmware location.
            if (!ok && b.Length == 4 && Asm.IsBl(b) && Fw.cur != null)
            {
                int fs = Fw.cur.FebOf(a), ft = fs >= 0 ? Asm.DecBl(fs, b) : -1;
                byte[] cur = Rd(a, 4);
                if (ft != -1 && Asm.IsBl(cur) && (cur[3] & 0x50) == (b[3] & 0x50) && Fw.cur.Has(ft)) ok = Asm.DecBl(a, cur) == Fw.cur.A(ft);
            }
            if (probe) { if (ok) ProbeSeen(tag); else ProbeFail(tag); return; }
            if (!ok)
            {
                if (force) { skipped.Add("FORCED: patch at " + F.Hx(a, 8) + " applied although the firmware differs from the stock code there (" + tag + ")"); return; }
                throw new Stop("This firmware does not have the code this tool expects at " + F.Hx(a, 8) + " (" + tag + "), so it cannot be patched safely. Use an untouched stock firmware dump (the 2025-02-14 and 2025-12-23 builds are verified).");
            }
        }
        public void Branch(int site, byte[] exp, int target, bool link, string note)
        {
            Expect(site, exp);
            int[] e = Asm.EncBl(site, target, link);
            Wr(site, Hw2(e[0], e[1]), note);
        }
        public int BeginAlloc() { cave = (cave + 3) & ~3; return cave; }
        public int EndAlloc(Asm a, string note)
        {
            byte[] code = a.Finish();
            int at = cave;
            Wr(at, code, note);
            cave = at + code.Length + (code.Length & 2);
            return at;
        }
        public int AllocData(byte[] blob, string note)
        {
            int at = (cave + 3) & ~3;
            Wr(at, blob, note);
            cave = at + blob.Length;
            return at;
        }
        public byte[] Bytes() { byte[] b = new byte[len]; P.Copy(data, b, len); return b; }
    }

    // ------------------------------------------------------------------------------------ in-place patches
    public static class Patches
    {
        public static byte[] PRESSURE_SIG = new byte[] { 0xb0, 0xfb, 0xf1, 0xf1, 0x03, 0xeb, 0x81, 0x04 };

        public static void Udiv(Image im)
        {
            Asm a = new Asm(Fw.A(0x0800398C));
            a.Cbz("r1", "zero"); a.Udiv("r2", "r0", "r1"); a.Mls("r1", "r2", "r1", "r0"); a.Mov("r0", "r2"); a.BxLr();
            a.Label("zero"); a.Mov("r1", "r0"); a.MvnI0("r0"); a.BxLr();
            byte[] code = a.Finish();
            byte[] full = new byte[44];
            P.Copy(code, full, code.Length);
            for (int i = code.Length; i + 1 < 44; i += 2) { full[i] = (byte)0x00; full[i + 1] = (byte)0xBF; }
            im.Expect(Fw.A(0x0800398C), new byte[] { 0x30, 0xb5, 0x0b, 0x46 });
            im.Wr(Fw.A(0x0800398C), full, "software divide -> hardware UDIV (same results, div-by-zero behaviour kept)");
        }

        public static void SmoothingOff(Image im)
        {
            // The dead-band setup stores its speed coefficients into a RAM table with a run of `movs r1/r2, #c` and
            // `strb r1/r2, [r0, #k]` right after `push {r4-r7, lr}; ldr r0, =table`. XP-Pen retunes these values
            // between builds (and the run's layout with them), so they are found by walking that run rather than at
            // fixed addresses: every coefficient load in it becomes 0.
            int fn = Fw.A(0x080041C0);
            im.Expect(fn, new byte[] { 0xf0, 0xb5 });
            int[] imm = new int[16];
            int nimm = 0, nstr = 0, p = fn + 2;
            byte[] h0 = im.Rd(p, 2);
            if ((h0[1] & 0xFF) != 0x48) { if (im.Skip("deadband coefficient table not found")) return; throw new Stop("deadband setup does not load its table as expected; Movement Smoothing cannot be turned off safely"); }
            p += 2;
            while (p < fn + 64)
            {
                byte[] h = im.Rd(p, 2);
                int hw = (h[0] & 0xFF) | ((h[1] & 0xFF) << 8);
                if ((hw & 0xF800) == 0x2000 && (((hw >> 8) & 7) == 1 || ((hw >> 8) & 7) == 2) && nimm < 16) { imm[nimm++] = p; p += 2; continue; }   // movs r1/r2, #c
                if ((hw & 0xF800) == 0x7000 && ((hw >> 3) & 7) == 0 && ((hw & 7) == 1 || (hw & 7) == 2)) { nstr++; p += 2; continue; }   // strb r1/r2, [r0, #k]
                break;
            }
            if (nimm < 2 || nstr < 8) { if (im.Skip("deadband coefficient table not found")) return; throw new Stop("deadband coefficients not laid out as expected; Movement Smoothing cannot be turned off safely"); }
            for (int i = 0; i < nimm; i++)
            {
                byte[] h = im.Rd(imm[i], 2);
                im.Wr(imm[i], new byte[] { 0, h[1] }, "speed-adaptive deadband coefficient " + F.Str(h[0] & 0xFF) + " -> 0");
            }
            im.Expect(Fw.A(0x08004258), new byte[] { 0x14, 0x20 }); im.Wr(Fw.A(0x08004258), new byte[] { 0x00, 0x20 }, "deadband floor 20 -> 0");
        }

        public static void InterpOff(Image im)
        {
            im.Expect(Fw.A(0x08005BDA), Image.Hw(0x2005)); im.Wr(Fw.A(0x08005BDA), Image.Hw(0x2001), "position average at rest 5 -> 1 (off)");
            im.Expect(Fw.A(0x08005BE0), Image.Hw(0x2003)); im.Wr(Fw.A(0x08005BE0), Image.Hw(0x2001), "position average moving 3 -> 1 (off)");
        }

        public static void Scan(Image im, int samples, int burst, int tick, int poll)
        {
            im.Expect(Fw.A(0x08005302), Image.Hw(0x212D)); im.Wr(Fw.A(0x08005302), Image.Hw(0x2100 | samples), "ADC samples/step 45 -> " + F.Str(samples));
            im.Expect(Fw.A(0x0800710E), Image.Hw(0x2228)); im.Wr(Fw.A(0x0800710E), Image.Hw(0x2200 | burst), "excitation pulses/step 40 -> " + F.Str(burst));
            im.Expect(Fw.A(0x08005DA2), Image.Hw(0x2532)); im.Wr(Fw.A(0x08005DA2), Image.Hw(0x2500 | poll), "step poll 50 -> " + F.Str(poll) + " us");
            im.Expect(Fw.A(0x08005DAC), Image.Hw(0x20C8)); im.Wr(Fw.A(0x08005DAC), Image.Hw(0x2000 | tick), "step tick 200 -> " + F.Str(tick) + " us");
        }

        public static void AgcCap(Image im, int samples)
        {
            int cap = samples - 2 < 30 ? samples - 2 : 30;
            if (cap == 30) return;
            im.Expect(Fw.A(0x080075D6), Image.Hw(0x2A1E)); im.Wr(Fw.A(0x080075D6), Image.Hw(0x2A00 | cap), "AGC delay cap 30 -> " + F.Str(cap) + " (samples-2)");
            im.Expect(Fw.A(0x080075DA), Image.Hw(0x221E)); im.Wr(Fw.A(0x080075DA), Image.Hw(0x2200 | cap), "AGC delay clamp 30 -> " + F.Str(cap));
        }

        // R43: 12-bit Readings. The stock sampling loop clears the low 3 bits of every ADC reading before storing it
        // (bic r0, r0, #7 between the RDATA load and the store to the sample buffer); it is the only place the firmware
        // reads the ADC. Two "mov r0, r0" in its place keep all 12 bits. The levels compared with these readings are
        // raised by Build.FULLRES_BIAS in the same build.
        // (addresses written out, not named: tools/mksig.py finds the locations by their Fw.A(0x...) / .A(0x...) text)
        public static void FullRes(Image im)
        {
            im.Expect(Fw.A(0x08004720), Image.Hw2(0xF020, 0x0007));
            im.Wr(Fw.A(0x08004720), Image.Hw2(0x4600, 0x4600), "ADC readings: keep all 12 bits (stock clears the low 3)");
        }
        // is 12-bit Readings installed in this image (twin checks, which need the matching Multi-coil floor)?
        public static bool FullResIn(byte[] flash)
        {
            int o = Twin.LayoutOf(flash).A(0x08004720) - 0x08000000;
            return o >= 0 && o + 4 <= flash.Length && flash[o] == 0x00 && flash[o + 1] == 0x46 && flash[o + 2] == 0x00 && flash[o + 3] == 0x46;
        }

        public static void Binterval(Image im)
        {
            int[] febs = new int[] { Fw.Sym(0x0800C63B), Fw.Sym(0x0800C658) };
            string[] eps = new string[] { "EP81/EP82", "EP83" };
            for (int i = 0; i < 2; i++)
            {
                // a truncated input stops before .data, where these literals live: skip (the input warning covers it)
                bool cut = im.inputEnd < Fw.cur.end || !Fw.cur.endFromTable;
                if (cut && !Fw.cur.Has(febs[i])) { im.skipped.Add(eps[i] + " bInterval fix - not in the input file (truncated)"); continue; }
                int site = Fw.A(febs[i]);
                if (!im.Present(site, 1)) { im.skipped.Add(F.Hx(site, 8) + ": " + eps[i] + " bInterval fix - not in the input file (truncated)"); continue; }
                int[] sites = new int[] { site, site };
                im.Expect(sites[i], new byte[] { 0x03 }); im.Wr(sites[i], new byte[] { 0x01 }, eps[i] + " bInterval 3 -> 1 ms (compressed .data literal)");
            }
        }

        public static int Find(byte[] b, byte[] pat, int from)
        {
            for (int i = from; i + pat.Length <= b.Length; i++)
            {
                bool ok = true;
                for (int k = 0; k < pat.Length; k++) if (b[i + k] != pat[k]) { ok = false; break; }
                if (ok) return i;
            }
            return -1;
        }

        public static void PressureClamp(Image im)
        {
            im.ProbeSeen(im.tag);
            byte[] b = im.Rd(Image.BASE, Fw.cur.searchEnd - Image.BASE);     // the firmware itself, not added code
            int i = Find(b, PRESSURE_SIG, 0);
            if (i < 0 || Find(b, PRESSURE_SIG, i + 1) != -1) { if (im.Skip("pressure index signature not found once")) return; throw new Stop("pressure index site not found exactly once"); }
            int hook = Image.BASE + i;
            int tOut = Fw.Ptr(0x0800766C), tIn = Fw.Ptr(0x08007670);       // the firmware's pointers to its two pressure tables
            if (!(im.Present(tOut, 4) && im.Present(tIn, 4 * 128)))
            {
                im.skipped.Add(F.Hx(hook, 8) + ": max-pressure fix - the pressure tables (" + F.Hx(tOut, 8) + ", " + F.Hx(tIn, 8) + ") are not in the input file (truncated)");
                return;
            }
            int nn = 1;
            while (nn < 128 && (im.Rd32(tIn + 4 * nn) & 0xFFFFFFFFL) > (im.Rd32(tIn + 4 * (nn - 1)) & 0xFFFFFFFFL)) nn++;
            if (tOut + 4 * nn != tIn) { if (im.Skip("pressure tables not laid out as expected")) return; throw new Stop("pressure tables are not laid out as expected; clamp not derivable"); }
            int maxidx = nn - 2;
            Asm a = new Asm(im.BeginAlloc());
            a.Udiv("r1", "r0", "r1"); a.CmpI("r1", maxidx); a.Bcc("ls", "ok"); a.Movs("r1", maxidx);
            a.Label("ok"); a.BWA(hook + 4);
            int at = im.EndAlloc(a, "pressure-table index clamp (<= " + F.Str(maxidx) + ", from a " + F.Str(nn) + "-entry table)");
            im.Expect(hook, new byte[] { 0xb0, 0xfb, 0xf1, 0xf1 });
            int[] e = Asm.EncBl(hook, at, false);
            im.Wr(hook, Image.Hw2(e[0], e[1]), "pressure index -> clamp");
        }

        // LED in the calibration-pass build only (every other build keeps the stock LED behaviour): the stock firmware
        // turns the LED (PC13, active-low) off at start-up, whenever the pen leaves, and every other 500 ms while the
        // pen hovers. Those four "off" calls go to the scheduler's LED gate instead, so the LED stays lit and blinks only
        // while a calibration step is being measured.
        public static void LedOn(Image im, int target)
        {
            im.Branch(Fw.A(0x080047E6), Image.Hw2(0xF003, 0xFE6F), target, true, "LED (PC13): stays on when the pen leaves");
            im.Branch(Fw.A(0x080052C4), Image.Hw2(0xF003, 0xB900), target, false, "LED (PC13): stays on (pen state reset)");
            im.Branch(Fw.A(0x08005838), Image.Hw2(0xF002, 0xBE46), target, false, "LED (PC13): stays on after the start-up flash");
            im.Branch(Fw.A(0x080086CA), Image.Hw2(0xF7FF, 0xFEFD), target, true, "LED (PC13): no 1 Hz blink while hovering");
        }

        // ---- flash-count readout over USB (rev 35). FIXED FOREVER - see AGENTS.md, rule 11: string descriptor index
        // COUNT_STRING (173), text "ADTC1:<S>:<6 digits>" (S = B: the Bootloader Counter is installed and counts, F: the
        // firmware's counter page, N: no counter page on this tablet), built on request in RAM at COUNT_BUF. The PC reads
        // it with an ordinary USB string request (Windows: HidD_GetIndexedString), so the count is known without a dump.
        // The stock GET_DESCRIPTOR handler compares the index against every string it knows and falls through to a
        // stall at the call patched here; only index 173 of type STRING is answered, everything else stalls as before.
        public const int COUNT_STRING = 173, COUNT_BUF = 0x20001F80;
        public static void CounterString(Image im) { CounterString(im, false); }

        // R44: ZONE (calibration-pass build only) also answers strings Sched.ZONE_STRING .. + 2 x ZONE_TARGETS - 1:
        // 176 + 2 x target + recording sets the LED zone (Sched.ZONE_ADDR) and answers "ADTZ1:<target><recording>",
        // so the PC knows the tablet took it. The stall path keeps r0 / r1 (the stall's arguments) untouched.
        public static void CounterString(Image im, bool zone) { CounterString(im, zone, false); }

        // R44: APIP (APIP training build only) also answers string Apip.STRING with the recorded samples (Apip.EmitReadout)
        public static void CounterString(Image im, bool zone, bool apip) { CounterString(im, zone, apip, null); }

        // Filter Arranger (lab build, atf_lab.cs): also strings Lab.STRING .. Lab.STRING_LAST (181-199; 200 is the stock
        // DFU entry and stays the stock's): the filter order and flags in RAM, each answered with the status
        public static void CounterString(Image im, bool zone, bool apip, Cfg lab)
        {
            int site = Fw.A(0x0800AF72), send = Fw.A(0x0800B04E);
            int stall = Asm.DecBl(site, im.Rd(site, 4));
            Asm a = new Asm(im.BeginAlloc());
            a.CmpI("r7", 3); a.Bcc("ne", "cs_stall"); a.CmpI("r4", COUNT_STRING); a.Bcc("eq", "cs_go");
            if (apip) { a.CmpI("r4", Apip.STRING); a.Bcc("ne", "cs_noap"); a.B("cs_apip"); a.Label("cs_noap"); }
            if (zone)
            {
                a.CmpI("r4", Sched.ZONE_STRING); a.Bcc("cc", "cs_stall");
                a.CmpI("r4", Sched.ZONE_STRING + 2 * Sched.ZONE_TARGETS - 1); a.Bcc("ls", "cs_zone");
            }
            if (lab != null)
            {
                a.CmpI("r4", Lab.STRING); a.Bcc("cc", "cs_stall");
                a.CmpI("r4", Lab.STRING_LAST); a.BccW("ls", "lab_str");
            }
            a.Label("cs_stall"); a.BWA(stall);                             // every other request: the stock stall
            if (zone)
            {
                a.Label("cs_zone");
                a.Mov32("r2", Sched.ZONE_ADDR); a.Mov("r3", "r4"); a.SubsI("r3", Sched.ZONE_STRING);
                a.Strb("r3", "r2", 0); a.Movs("r0", Sched.ZONE_CHECK); a.Eors("r0", "r3"); a.Strb("r0", "r2", 2);
                a.Mov32("r2", COUNT_BUF);
                a.Movw("r0", 0x0300 | 18); a.Strh("r0", "r2", 0);
                string zhead = "ADTZ1:";
                for (int i = 0; i < 6; i++) { a.Movs("r0", P.CharAt(zhead, i)); a.Strh("r0", "r2", 2 + 2 * i); }
                a.Lsrs("r0", "r3", 1); a.AddsI("r0", '0'); a.Strh("r0", "r2", 14);
                a.Movs("r0", 1); a.Ands("r0", "r3"); a.AddsI("r0", '0'); a.Strh("r0", "r2", 16);
                a.Mov("r4", "r2"); a.BWA(send);
            }
            a.Label("cs_go");
            a.Push(new string[] { "r5", "r6", "r7" }, false);
            a.Mov32("r5", COUNT_BUF);
            a.Mov32("r0", Build.COUNTER_PAGE); a.Ldr("r1", "r0", 0); a.Mov32("r2", Build.COUNTER_MAGIC); a.CmpR("r1", "r2"); a.Bcc("ne", "cs_none");
            a.Ldr("r6", "r0", 4); a.Ldr("r2", "r0", 8); a.Mvns("r2", "r2"); a.CmpR("r6", "r2"); a.Bcc("ne", "cs_none");
            a.Mov32("r2", Build.COUNT_MAX); a.CmpR("r6", "r2"); a.Bcc("hi", "cs_none");   // corrupt base: no counter (as ReadFlashCount)
            a.AddsI("r0", 16);
            a.Label("cs_scan");
            a.Mov32("r3", Build.COUNTER_PAGE + 0x400); a.CmpR("r0", "r3"); a.Bcc("cs", "cs_cnt");
            a.Ldr("r1", "r0", 0); a.Ldr("r2", "r0", 4);
            a.AddsI3("r3", "r1", 1); a.Bcc("ne", "cs_rec");
            a.AddsI3("r3", "r2", 1); a.Bcc("eq", "cs_cnt");               // both words erased: end of the records
            a.Label("cs_rec"); a.Mvns("r3", "r2"); a.CmpR("r1", "r3"); a.Bcc("ne", "cs_nx"); a.AddsI("r6", 1);
            a.Label("cs_nx"); a.AddsI("r0", 8); a.B("cs_scan");
            a.Label("cs_cnt");
            a.Mov32("r2", Build.COUNT_MAX); a.CmpR("r6", "r2"); a.Bcc("ls", "cs_cap"); a.Mov("r6", "r2");   // shown as 999999 above it
            a.Label("cs_cap");
            a.Movs("r7", 'F');
            a.Mov32("r0", Sched.BL_ERASE); a.Ldr("r0", "r0", 0); a.Mov32("r1", Sched.BlPatchWord()); a.CmpR("r0", "r1"); a.Bcc("ne", "cs_txt");
            a.Movs("r7", 'B'); a.B("cs_txt");
            a.Label("cs_none"); a.Movs("r7", 'N'); a.Movs("r6", 0);
            a.Label("cs_txt");
            a.Movw("r0", 0x0300 | 30); a.Strh("r0", "r5", 0);
            string head = "ADTC1:";
            for (int i = 0; i < 6; i++) { a.Movs("r0", P.CharAt(head, i)); a.Strh("r0", "r5", 2 + 2 * i); }
            a.Strh("r7", "r5", 14); a.Movs("r0", ':'); a.Strh("r0", "r5", 16);
            int[] div = new int[] { 100000, 10000, 1000, 100, 10, 1 };
            for (int k = 0; k < 6; k++)
            {
                a.Mov32("r1", div[k]); a.Udiv("r2", "r6", "r1"); a.Mls("r6", "r2", "r1", "r6");
                a.AddsI("r2", '0'); a.Strh("r2", "r5", 18 + 2 * k);
            }
            a.Mov("r4", "r5"); a.Pop(new string[] { "r5", "r6", "r7" }, false);
            a.BWA(send);                                                    // the handler's own "send this descriptor"
            if (apip) Apip.EmitReadout(a, send);
            if (lab != null) Lab.EmitStrings(a, send, lab.labDefault, lab.labDefaultFlags, Lab.Available(lab));
            int at = im.EndAlloc(a, "flash-count readout: USB string " + F.Str(COUNT_STRING) + " (\"ADTC1:<B|F|N>:<count>\")"
                + (apip ? ", ALPI training samples: USB string " + F.Str(Apip.STRING) : "")
                + (lab != null ? ", Filter Arranger: USB strings " + F.Str(Lab.STRING) + "-" + F.Str(Lab.STRING_LAST) : ""));
            im.Branch(Fw.A(0x0800AF72), Image.Hw2(0xF7FF, 0xFDD1), at, true, "GET_DESCRIPTOR: unknown index -> flash-count readout, else stall as before");
        }

        public static void BandEdge(Image im)
        {
            Asm a = new Asm(im.BeginAlloc());
            a.AddsR("r0", "r0", "r3"); a.LdrLitV("r1", 540000); a.CmpR("r0", "r1"); a.Bcc("cc", "ok");
            a.LdrLitV("r1", 545000); a.CmpR("r0", "r1"); a.Bcc("cs", "ok"); a.LdrLitV("r0", 539999);
            a.Label("ok"); a.Str("r0", "r2", 4); a.BWA(Fw.A(0x0800765A));
            int at = im.EndAlloc(a, "resonance band-edge hold (540-545 kHz -> 539.999 kHz)");
            im.Expect(Fw.A(0x08007656), Image.Hw2(0x4418, 0x6050));
            int[] e = Asm.EncBl(Fw.A(0x08007656), at, false);
            im.Wr(Fw.A(0x08007656), Image.Hw2(e[0], e[1]), "resonance estimate -> band-edge hold");
        }
    }
}
