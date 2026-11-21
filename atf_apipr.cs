// atf_apipr.cs (R45) - APIP's robust filters (Anargon: "put the filters each as options under a dedicated APIP
// section"). They take the place of APIP's tracker (atf_apip.cs) after its coil-distance correction, and keep
// everything else of APIP: its training, its bands, its noise now (the firmware's own average), its reset after a
// lift or 20 mm off.
//
// What it is: 2, 3 or 4 trackers of the pen's position and speed run side by side (Kalman filters with a constant
// speed; the first expects a still or steady pen, each next one a 30 x livelier pen). Every reading, each tracker
// predicts it, and the filter weighs the trackers by how well each predicted the last readings (an interacting
// multiple model filter, exact mixing). A reading far off every prediction (the tablet's coil-switch spikes) is
// weighed as one of the rare wild readings (10 % of them, 30 x the noise wide) and moves the trackers very little.
// Each axis has its own trackers and weights. All whole numbers: positions in Q8 of 1/100 mm, the trackers'
// uncertainty in units of the noise (from the firmware's noise average: 1 / noise = KINV / n).
// The twin checks the firmware against the C# copy here (Axis) on every frame, bit for bit.
// Scratch study (2026-11-11, before this was built): on a tablet-like test (X 0.44 mm, Y 0.23 mm of noise with
// the coil-switch spikes), the noise held still was 0.15 / 0.08 mm with 2 trackers and 0.12 / 0.06 mm with 4,
// against 0.38 / 0.23 for APIP's tracker and 0.72 / 0.38 for a Position Interpolation-like average of 4; the worst
// spikes about 0.5 mm against 4.3 and 5.9. Tracking while moving was no worse than APIP's tracker.                                                                                                                                                                             
using System;
using System.Text;
using Gd32Emu;

namespace AnargonsTabletTools
{
    // the robust filter's constants as the firmware holds them (after the APIP table), read back for the C# copy
    public class RobustK
    {
        public int M, A, B, C0;
        public int[] Q4 = new int[Apip.NBT * 4], Q2 = new int[Apip.NBT * 4], Q1 = new int[Apip.NBT * 4];   // band * 4 + tracker
        public int[] TG1 = new int[176], TG2 = new int[176], THL = new int[176], TW = new int[256], TSP = new int[256], TEX = new int[64];
    }

    // one axis of the robust filter, as RAM holds it (ApipRobust.ROB + ApipRobust.AXB x axis); DS, C, T, U, L and EX
    // are worked out again every reading
    public class RobustAxis
    {
        public int[] X = new int[4], V = new int[4], P00 = new int[4], P01 = new int[4], P11 = new int[4], DS = new int[4];
        public int XB, VB, Z, INV, N, Z1, Z2;
        public int[] MU = new int[4], C = new int[4], T = new int[4], U = new int[4], L = new int[4], EX = new int[4];
    }

    public static class ApipRobust
    {
        // ---- the settings (Build.SETTINGS keys; Build.ApipKind: 1 = APIP's tracker, 2-4 = this filter's trackers)
        public const int MIN_M = 2, MAX_M = 4;
        // the trackers: the first one's tracking index (lam: how far the pen's speed changes per reading, against
        // the noise) at 1000 readings per second, each next one RATIO x livelier (picked in the scratch study: the
        // least noise held whose tracking while moving was no worse than APIP's tracker), a 0.3 % chance per reading
        // of switching trackers, and 10 % wild readings 30 x the noise wide
        static readonly double[] LAM0 = new double[] { 0, 0, 1e-2, 1e-3, 1e-5 };
        public const double RATIO = 30, PSWITCH = 0.003, EPS = 0.1, WILD = 900;
        public const int F0 = 20, F1 = 24, F2 = 28;          // P00 in Q20, P01 in Q24, P11 in Q28 (units of the noise)
        public static readonly int KINV = (int)P.Rint(16777216.0 * Apip.NOISE_N);   // 1 / noise (Q24) = KINV / n
        // RAM: axis a at ROB + AXB a: tracker j at 32 j (X, V, P00, P01, P11, DS), then XB, VB (the filter's position
        // and speed), Z (the reading), INV, N (the noise average, as APIP's), Z1, Z2 (the last two readings), REC (the
        // band's record), then the weights at SET: MU (Q16, summing to 65536), C, T, U, L, EX (4 j each)
        // (free: 0x200019FC-0x20001F7F is unused by stock and every other block of ours; R45-rc2: the ALPI training build's
        // ring reaches into it, but a training build has no filter)
        public const int ROB = 0x20001A00, AXB = 256;
        public const int O_XB = 128, O_VB = 132, O_Z = 136, O_INV = 140, O_N = 144, O_Z1 = 148, O_Z2 = 152, O_REC = 156;
        public const int O_SET = 160, O_MU = 0, O_C = 16, O_T = 32, O_U = 48, O_L = 64, O_EX = 80;
        // constants (flash, after the APIP table): M, A, B (the switch weights, Q16), C0 (log of the wild share, Q8),
        // per band 4 x (Q4, Q2, Q1) (the trackers' step noise), then the tables over log2 of S (TG1 - C0, TG2, THL), over
        // the log chance (TW, TSP) and 2^-f (TEX)
        public const int K_M = 0, K_A = 8, K_B = 12, K_C0 = 16, K_Q = 20, Q_BAND = 48;
        public const int K_TG1 = K_Q + Apip.NBT * Q_BAND, K_TG2 = K_TG1 + 352, K_THL = K_TG2 + 352, K_TW = K_THL + 352, K_TSP = K_TW + 512, K_TEX = K_TSP + 512, K_END = K_TEX + 128;

        public static int SwitchA(int M) { double bb = PSWITCH / (M - 1); return (int)P.Rint((1 - PSWITCH - bb) * 65536); }
        public static int SwitchB(int M) { return (int)P.Rint(PSWITCH / (M - 1) * 65536); }
        // the constants for M trackers on LV: each band's tracking index moved to the band's rate (per reading squared)
        public static byte[] Constants(int M, Level lv)
        {
            byte[] k = new byte[K_END];
            P32(k, K_M, M); P32(k, K_A, SwitchA(M)); P32(k, K_B, SwitchB(M));
            P32(k, K_C0, (int)P.Rint(256 * P.Log(EPS / (1 - EPS))));
            for (int b = 0; b < Apip.NBT; b++)
            {
                double f = 1000.0 / Apip.BandRate(lv, b);
                for (int j = 0; j < M; j++)
                {
                    double l = LAM0[M] * f * f, q;
                    for (int r = 0; r < j; r++) l *= RATIO;
                    q = l * l;
                    if (q > 2.0) q = 2.0;
                    int o = K_Q + b * Q_BAND + 12 * j;
                    P32(k, o, (int)P.Rint(q / 4 * (1 << F0)));
                    P32(k, o + 4, (int)P.Rint(q / 2 * (1 << F1)));
                    double q1 = P.Rint(q * (1 << F2));
                    P32(k, o + 8, q1 > (1 << 29) ? 1 << 29 : (int)q1);
                }
            }
            for (int i = 0; i < 176; i++)
            {
                double S = (double)(1 << (i >> 4)) * (1 + ((i & 15) + 0.5) / 16.0);
                P16(k, K_TG1 + 2 * i, (int)P.Rint(256 * 0.5 * P.Log(1 + WILD / S)) - G32(k, K_C0));   // less the log of the wild share
                P16(k, K_TG2 + 2 * i, (int)P.Rint(65536 * 0.5 * WILD / (S + WILD)));
                P16(k, K_THL + 2 * i, (int)P.Rint(256 * 0.5 * P.Log(S)));
            }
            for (int i = 0; i < 256; i++)
            {
                double l = -8 + (i + 0.5) / 16.0;
                P16(k, K_TW + 2 * i, (int)P.Rint(32768 / (1 + P.Exp(l))));
                P16(k, K_TSP + 2 * i, (int)P.Rint(256 * P.Log(1 + P.Exp(l))));
            }
            for (int i = 0; i < 64; i++) P16(k, K_TEX + 2 * i, (int)P.Rint(65535 * P.Exp(-i / 64.0 * P.Log(2.0))));
            return k;
        }
        public static RobustK Read(byte[] flash, int o)
        {
            RobustK k = new RobustK();
            k.M = G32(flash, o + K_M); k.A = G32(flash, o + K_A); k.B = G32(flash, o + K_B); k.C0 = G32(flash, o + K_C0);
            for (int b = 0; b < Apip.NBT; b++)
                for (int j = 0; j < 4; j++)
                {
                    int q = o + K_Q + b * Q_BAND + 12 * j;
                    k.Q4[b * 4 + j] = G32(flash, q); k.Q2[b * 4 + j] = G32(flash, q + 4); k.Q1[b * 4 + j] = G32(flash, q + 8);
                }
            for (int i = 0; i < 176; i++) { k.TG1[i] = G16(flash, o + K_TG1 + 2 * i); k.TG2[i] = G16(flash, o + K_TG2 + 2 * i); k.THL[i] = G16s(flash, o + K_THL + 2 * i); }
            for (int i = 0; i < 256; i++) { k.TW[i] = G16(flash, o + K_TW + 2 * i); k.TSP[i] = G16(flash, o + K_TSP + 2 * i); }
            for (int i = 0; i < 64; i++) k.TEX[i] = G16(flash, o + K_TEX + 2 * i);
            return k;
        }
        static void P16(byte[] b, int o, int v) { b[o] = (byte)(v & 0xFF); b[o + 1] = (byte)((v >> 8) & 0xFF); }
        static void P32(byte[] b, int o, int v) { P16(b, o, v & 0xFFFF); P16(b, o + 2, (v >> 16) & 0xFFFF); }
        static int G16(byte[] b, int o) { return (b[o] & 0xFF) | ((b[o + 1] & 0xFF) << 8); }
        static int G16s(byte[] b, int o) { int v = G16(b, o); return v >= 0x8000 ? v - 0x10000 : v; }
        static int G32(byte[] b, int o) { return G16(b, o) | (G16(b, o + 2) << 16); }

        // ================================================================ firmware
        static int R(string r) { return Asm.R(r); }
        static void Ld(Asm a, string rt, string rn, int off) { if (R(rt) < 8 && R(rn) < 8 && off < 128) a.Ldr(rt, rn, off); else a.LdrW(rt, rn, off); }
        static void St(Asm a, string rt, string rn, int off) { if (R(rt) < 8 && R(rn) < 8 && off < 128) a.Str(rt, rn, off); else a.StrW(rt, rn, off); }
        static void LdH(Asm a, string rt, string rn, int off) { a.W32(0xF8B0 | R(rn), (R(rt) << 12) | off); }                         // ldrh.w
        static void Smull(Asm a, string lo, string hi, string rn, string rm) { a.W32(0xFB80 | R(rn), (R(lo) << 12) | (R(hi) << 8) | R(rm)); }
        static void Smlal(Asm a, string lo, string hi, string rn, string rm) { a.W32(0xFBC0 | R(rn), (R(lo) << 12) | (R(hi) << 8) | R(rm)); }
        static void Ssat(Asm a, string rd, int bits, string rn) { a.W32(0xF300 | R(rn), (R(rd) << 8) | (bits - 1)); }
        static void Usat(Asm a, string rd, int bits, string rn) { a.W32(0xF380 | R(rn), (R(rd) << 8) | bits); }
        static void Ubfx(Asm a, string rd, string rn, int lsb, int width) { a.W32(0xF3C0 | R(rn), ((lsb >> 2) << 12) | (R(rd) << 8) | ((lsb & 3) << 6) | (width - 1)); }
        static void LsrR(Asm a, string rd, string rn, string rm) { a.W32(0xFA20 | R(rn), 0xF000 | (R(rd) << 8) | R(rm)); }
        static void Add(Asm a, string rd, string rn, string rm, int type, int amt) { a.DpRegW(Asm.OP_ADD, false, rd, rn, rm, type, amt); }
        static void Add(Asm a, string rd, string rn, string rm) { Add(a, rd, rn, rm, Asm.SH_LSL, 0); }
        static void Sub(Asm a, string rd, string rn, string rm) { a.DpRegW(Asm.OP_SUB, false, rd, rn, rm, Asm.SH_LSL, 0); }
        static void Orr(Asm a, string rd, string rn, string rm, int type, int amt) { a.DpRegW(Asm.OP_ORR, false, rd, rn, rm, type, amt); }
        static void AddI(Asm a, string rd, string rn, int v) { a.DpImmW(Asm.OP_ADD, false, rd, rn, v); }
        static void RsbI(Asm a, string rd, string rn, int v) { a.DpImmW(Asm.OP_RSB, false, rd, rn, v); }
        static void Zero(Asm a, string rd) { a.DpImmW(Asm.OP_ORR, false, rd, "pc", 0); }
        static void LsrI(Asm a, string rd, string rm, int amt) { a.LsrW(rd, rm, amt, false); }
        // rd = (x * y) >> sh (the low word of the 64-bit product shifted); hi is clobbered
        static void MulSh(Asm a, string rd, string hi, string x, string y, int sh) { Smull(a, rd, hi, x, y); LsrI(a, rd, rd, sh); Orr(a, rd, rd, hi, Asm.SH_LSL, 32 - sh); }
        static readonly string[] SAVE = new string[] { "r4", "r5", "r6", "r7", "r8", "r9", "r10", "r11" };

        // one axis (C# copy: Axis). In: r0 = reading, r1 = this axis's state (ROB + AXB x axis), r2 = band record, r3 =
        // 2^32 / pitch, r4 = Apip.ST, r7 = 0 when the filter starts over. Out: r0 = the position to report. Keeps r4-r11.
        // KONST = the constants (after the APIP table).
        public static void EmitAxis(Asm a, int konst, int M)
        {
            a.Label("aprx");
            a.PushW(SAVE, true);
            // the coil-distance correction, as APIP's tracker (apx): r0 = the corrected reading, Q8 of 1/100 mm
            a.Ldrb("r5", "r4", 21); a.Mov("ip", "r5"); a.LsrsR("r3", "r5");
            a.MulW("r4", "r0", "r3");
            a.Lsrs("r5", "r4", 28); a.Lsls("r6", "r4", 4); a.Lsrs("r6", "r6", 24);
            a.Lsls("r5", "r5", 1); a.AddsR("r5", "r5", "r2");
            a.Ldrh("r4", "r5", 12); a.Sxth("r4", "r4"); a.Ldrh("r5", "r5", 14); a.Sxth("r5", "r5");
            a.SubsR("r5", "r5", "r4"); a.Muls("r5", "r6"); a.Asrs("r5", "r5", 8); a.AddsR("r4", "r4", "r5");
            a.Mov("r5", "ip"); a.Movs("r6", 8); a.SubsR("r6", "r6", "r5"); a.LslsR("r0", "r6");
            a.Lsls("r4", "r4", 4); a.AddsR("r0", "r0", "r4");
            St(a, "r2", "r1", O_REC); St(a, "r0", "r1", O_Z);
            a.CmpI("r7", 0); a.BccW("eq", "apr_set");
            // the noise now, as apx: n += (min(|second difference of the readings|, 2 n) - n) / 32, from 1 to 4096 units
            Ld(a, "r4", "r1", O_Z1); Ld(a, "r5", "r1", O_Z2); St(a, "r4", "r1", O_Z2); St(a, "r0", "r1", O_Z1);
            a.SubsR("r5", "r5", "r4"); a.SubsR("r5", "r5", "r4"); a.AddsR("r5", "r5", "r0");
            a.AsrW("r6", "r5", 31, false); a.Eors("r5", "r6"); a.SubsR("r5", "r5", "r6");
            Ld(a, "r4", "r1", O_N); a.Lsls("r6", "r4", 1); a.CmpR("r5", "r6"); a.It("hi"); a.Mov("r5", "r6");
            a.SubsR("r5", "r5", "r4"); a.Asrs("r5", "r5", 5); a.AddsR("r4", "r4", "r5");
            a.DpImmW(Asm.OP_SUB, false, "r4", "r4", 256); Usat(a, "r4", 20, "r4"); AddI(a, "r4", "r4", 256);
            St(a, "r4", "r1", O_N); a.Mov32("r5", KINV); a.Udiv("r5", "r5", "r4"); St(a, "r5", "r1", O_INV);
            // 20 mm off the filter's position: start over at the reading
            Ld(a, "r4", "r1", O_XB); a.SubsR("r6", "r0", "r4"); a.AsrW("r3", "r6", 31, false); a.Eors("r6", "r3"); a.SubsR("r6", "r6", "r3");
            a.CmpIW("r6", 1 << 19); a.BccW("cs", "apr_set");
            a.Mov32("r2", konst);
            Track(a, M); Chances(a, M); Mixture(a, M);
            a.Label("apr_out");
            // the output, as apx: rounded to the report's unit, 0 ... the axis maximum
            Ld(a, "r0", "r1", O_XB);
            a.Mov32("r4", Apip.ST); a.Ldrb("r5", "r4", 21);
            a.Movs("r6", 128); a.LsrsR("r6", "r5"); a.AddsR("r0", "r0", "r6");
            a.Movs("r6", 8); a.SubsR("r6", "r6", "r5"); a.AsrsR("r0", "r6"); a.CmpI("r0", 0); a.It("lt"); a.Movs("r0", 0);
            Ld(a, "r2", "r1", O_REC); a.Ldrh("r3", "r2", 10); a.LslsR("r3", "r5"); a.CmpR("r0", "r3"); a.It("gt"); a.Mov("r0", "r3");
            a.PopW(SAVE, true); a.BxLr();
            // start over at the reading: every tracker there, still, with the noise's uncertainty; equal weights; the
            // band's noise to start from
            a.Label("apr_set");
            a.Movs("r4", 0); a.Mov32("r5", 1 << F0); a.Mov32("r6", 1 << F2); a.Mov32("r3", 65536 / M);
            for (int j = 0; j < M; j++)
            {
                int o = 32 * j;
                St(a, "r0", "r1", o); St(a, "r4", "r1", o + 4); St(a, "r5", "r1", o + 8); St(a, "r4", "r1", o + 12); St(a, "r6", "r1", o + 16);
                if (j > 0) St(a, "r3", "r1", O_SET + O_MU + 4 * j);
            }
            a.Mov32("r3", 65536 / M + 65536 - (65536 / M) * M); St(a, "r3", "r1", O_SET + O_MU);
            St(a, "r0", "r1", O_XB); St(a, "r4", "r1", O_VB); St(a, "r0", "r1", O_Z1); St(a, "r0", "r1", O_Z2);
            Ld(a, "r2", "r1", O_REC); a.Ldrh("r4", "r2", 8); a.Lsls("r4", "r4", 8); St(a, "r4", "r1", O_N);
            a.Mov32("r5", KINV); a.Udiv("r5", "r5", "r4"); St(a, "r5", "r1", O_INV);
            a.BW("apr_out");
        }
        // the trackers: mixing, prediction, the robust update and each one's log chance of this reading
        static void Track(Asm a, int M)
        {
            int ka = SwitchA(M), kb = SwitchB(M);                 // the switch weights, as Constants stores them
            int sh1 = F1 - F0 - 1, sh2 = F2 - F0, sh3 = F2 - F1, k1s = 24 + F1 - F0, p11s = 24 + F1 - F2;   // K1 = a1 (R << n) >> k1s
            // pass 1: the spreads (DS) and the mixed uncertainty G (r6 = G00, r7 = G01, r8 = G11)
            Ld(a, "r3", "r1", O_XB); Ld(a, "r5", "r1", O_INV);
            a.Movs("r6", 0); a.Movs("r7", 0); Zero(a, "r8"); Zero(a, "r9"); Zero(a, "r10"); Zero(a, "r11");
            for (int i = 0; i < M; i++)
            {
                int o = 32 * i;
                Ld(a, "r0", "r1", o); a.SubsR("r0", "r0", "r3"); MulSh(a, "r0", "lr", "r0", "r5", 14); Ssat(a, "r0", 16, "r0"); St(a, "r0", "r1", o + 20);
                Ld(a, "r4", "r1", O_SET + O_MU + 4 * i);
                Ld(a, "lr", "r1", o + 8); a.Mla("lr", "r0", "r0", "lr"); Smlal(a, "r6", "r7", "r4", "lr");
                Ld(a, "lr", "r1", o + 12); Smlal(a, "r8", "r9", "r4", "lr");
                Ld(a, "lr", "r1", o + 16); Smlal(a, "r10", "r11", "r4", "lr");
            }
            LsrI(a, "r6", "r6", 16); Orr(a, "r6", "r6", "r7", Asm.SH_LSL, 16);
            LsrI(a, "r7", "r8", 16); Orr(a, "r7", "r7", "r9", Asm.SH_LSL, 16);
            LsrI(a, "r8", "r10", 16); Orr(a, "r8", "r8", "r11", Asm.SH_LSL, 16);
            // pass 2a: the switch weights (c, t, u), mixing and prediction (r3 = XB, r11 = this band's step noise)
            Ld(a, "r11", "r1", O_REC); LdH(a, "r11", "r11", 0); Add(a, "r11", "r2", "r11");
            for (int j = 0; j < M; j++)
            {
                int o = 32 * j;
                Ld(a, "r4", "r1", O_SET + O_MU + 4 * j); a.Movw("lr", ka); a.MulW("r5", "lr", "r4"); a.Lsrs("r5", "r5", 16);
                AddI(a, "r4", "r5", kb); St(a, "r4", "r1", O_SET + O_C + 4 * j);
                a.Lsls("r5", "r5", 15); a.Udiv("r5", "r5", "r4");                                                   // t
                RsbI(a, "ip", "r5", 32768); a.MulW("ip", "ip", "r5"); LsrI(a, "ip", "ip", 15);                     // u
                Ld(a, "r0", "r1", o); a.SubsR("r0", "r0", "r3"); MulSh(a, "r4", "lr", "r5", "r0", 15); a.AddsR("r4", "r4", "r3");
                Ld(a, "r0", "r1", o + 4); Ld(a, "lr", "r1", O_VB); Sub(a, "r0", "r0", "lr"); MulSh(a, "r9", "r10", "r5", "r0", 15); Add(a, "r9", "r9", "lr");
                St(a, "r9", "r1", o + 4); Add(a, "r4", "r4", "r9"); St(a, "r4", "r1", o);
                Ld(a, "r0", "r1", o + 8); a.SubsR("r0", "r0", "r6"); MulSh(a, "r0", "lr", "r5", "r0", 15); a.AddsR("r0", "r0", "r6");
                Ld(a, "r4", "r1", o + 20); a.MulW("r10", "r4", "r4"); MulSh(a, "r10", "lr", "ip", "r10", 15); Add(a, "r0", "r0", "r10"); Ssat(a, "r0", 30, "r0");
                Ld(a, "r9", "r1", o + 12); Sub(a, "r9", "r9", "r7"); MulSh(a, "r9", "lr", "r5", "r9", 15); Add(a, "r9", "r9", "r7"); Ssat(a, "r9", 30, "r9");
                Ld(a, "r10", "r1", o + 16); Sub(a, "r10", "r10", "r8"); MulSh(a, "r10", "lr", "r5", "r10", 15); Add(a, "r10", "r10", "r8"); Ssat(a, "r10", 30, "r10");
                Add(a, "r0", "r0", "r9", Asm.SH_ASR, sh1); Add(a, "r0", "r0", "r10", Asm.SH_ASR, sh2); Ld(a, "r4", "r11", 12 * j); a.AddsR("r0", "r0", "r4"); Usat(a, "r0", 31, "r0"); St(a, "r0", "r1", o + 8);
                Add(a, "r9", "r9", "r10", Asm.SH_ASR, sh3); Ld(a, "r4", "r11", 12 * j + 4); Add(a, "r9", "r9", "r4"); St(a, "r9", "r1", o + 12);
                Ld(a, "r4", "r11", 12 * j + 8); Add(a, "r10", "r10", "r4"); St(a, "r10", "r1", o + 16);
            }
            // pass 2b: the update (r3 = the reading)
            Ld(a, "r3", "r1", O_Z);
            for (int j = 0; j < M; j++)
            {
                int o = 32 * j;
                Ld(a, "r4", "r1", o + 8); AddI(a, "r5", "r4", 1 << F0); a.Clz("r6", "r5"); a.LslRW("r5", "r5", "r6");
                Ubfx(a, "r7", "r5", 27, 4); a.DpRegW(Asm.OP_SUB, false, "r7", "r7", "r6", Asm.SH_LSL, 4); AddI(a, "r7", "r7", 176);   // table row
                LsrI(a, "r5", "r5", 16); a.MvnI0("r8"); a.Udiv("r5", "r8", "r5");                                                  // R
                a.LslRW("r8", "r5", "r6");                                                                                         // R << n
                Ld(a, "r9", "r1", o + 12); MulSh(a, "r10", "r11", "r9", "r8", k1s);                                                 // K1
                LsrI(a, "r8", "r8", 4); RsbI(a, "r8", "r8", 1 << 24);                                                              // K0
                Ld(a, "r11", "r1", o); Sub(a, "r11", "r3", "r11");                                                                 // rr
                Ld(a, "ip", "r1", O_INV); MulSh(a, "ip", "lr", "r11", "ip", 14); Ssat(a, "ip", 16, "ip"); a.MulW("ip", "ip", "ip");
                MulSh(a, "ip", "lr", "ip", "r5", 29); RsbI(a, "r5", "r6", 11); LsrR(a, "ip", "ip", "r5"); Usat(a, "ip", 16, "ip");  // u8
                Add(a, "r7", "r2", "r7", Asm.SH_LSL, 1);
                LdH(a, "r5", "r7", K_TG1); RsbI(a, "r6", "r5", 0);
                LdH(a, "r5", "r7", K_TG2); a.MulW("r5", "r5", "ip"); Add(a, "r6", "r6", "r5", Asm.SH_ASR, 16);                    // l
                AddI(a, "r5", "r6", 2048); a.AsrW("r5", "r5", 4, false); Usat(a, "lr", 8, "r5"); Add(a, "lr", "r2", "lr", Asm.SH_LSL, 1);
                LdH(a, "r0", "lr", K_TW); a.CmpI("r5", 255); a.It("le"); LdH(a, "r6", "lr", K_TSP);                               // w, sp
                MulSh(a, "r5", "lr", "r8", "r0", 15); MulSh(a, "r8", "lr", "r10", "r0", 15);                                      // K0 w, K1 w
                MulSh(a, "r0", "lr", "r5", "r11", 24); Ld(a, "r10", "r1", o); Add(a, "r0", "r0", "r10"); St(a, "r0", "r1", o);
                MulSh(a, "r0", "lr", "r8", "r11", 24); Ld(a, "r10", "r1", o + 4); Add(a, "r0", "r0", "r10"); St(a, "r0", "r1", o + 4);
                MulSh(a, "r0", "lr", "r5", "r4", 24); a.SubsR("r0", "r4", "r0"); St(a, "r0", "r1", o + 8);
                MulSh(a, "r0", "lr", "r5", "r9", 24); Sub(a, "r0", "r9", "r0"); St(a, "r0", "r1", o + 12);
                MulSh(a, "r0", "lr", "r8", "r9", p11s); Ld(a, "r4", "r1", o + 16); a.SubsR("r0", "r4", "r0"); St(a, "r0", "r1", o + 16);
                LdH(a, "r5", "r7", K_THL); a.SubsR("r4", "r6", "r5"); a.DpRegW(Asm.OP_SUB, false, "r4", "r4", "ip", Asm.SH_LSR, 1);
                St(a, "r4", "r1", O_SET + O_L + 4 * j);
            }
        }
        // the weights (leaves r5 = 4 x the likeliest tracker)
        static void Chances(Asm a, int M)
        {
            int sb = O_SET;
            Ld(a, "r4", "r1", sb + O_L); a.Movs("r5", 0);
            for (int j = 1; j < M; j++) { Ld(a, "r0", "r1", sb + O_L + 4 * j); a.CmpR("r0", "r4"); a.H(0xBFC4); a.Mov("r4", "r0"); a.Movs("r5", 4 * j); }   // itt gt
            a.Movw("r6", 369); a.Movw("r7", K_TEX); Add(a, "r7", "r2", "r7"); Zero(a, "r8");
            for (int j = 0; j < M; j++)
            {
                Ld(a, "r0", "r1", sb + O_L + 4 * j); a.SubsR("r0", "r4", "r0"); Usat(a, "r0", 15, "r0"); a.Muls("r0", "r6"); a.Asrs("r0", "r0", 8);
                Ubfx(a, "ip", "r0", 2, 6); a.Lsrs("r0", "r0", 8); Add(a, "ip", "r7", "ip", Asm.SH_LSL, 1); LdH(a, "ip", "ip", 0); LsrR(a, "ip", "ip", "r0");
                Ld(a, "r0", "r1", sb + O_C + 4 * j); a.MulW("ip", "ip", "r0"); LsrI(a, "ip", "ip", 8);
                St(a, "ip", "r1", sb + O_EX + 4 * j); Add(a, "r8", "r8", "ip");
            }
            a.MvnI0("r0"); a.Udiv("r9", "r0", "r8"); Zero(a, "r10");
            for (int j = 0; j < M; j++)
            {
                Ld(a, "r0", "r1", sb + O_EX + 4 * j); a.MulW("r0", "r0", "r9"); a.Lsrs("r0", "r0", 16); a.CmpI("r0", 1); a.It("cc"); a.Movs("r0", 1);
                St(a, "r0", "r1", sb + O_MU + 4 * j); Add(a, "r10", "r10", "r0");
            }
            a.AddsR("r0", "r1", "r5"); Ld(a, "ip", "r0", sb + O_MU); RsbI(a, "r10", "r10", 65536); Add(a, "ip", "ip", "r10"); St(a, "ip", "r0", sb + O_MU);
        }
        // the filter's position and speed: the weighted trackers (r5 = 4 x the likeliest)
        static void Mixture(Asm a, int M)
        {
            Add(a, "r0", "r1", "r5", Asm.SH_LSL, 3);
            Ld(a, "r4", "r0", 0); Ld(a, "r6", "r0", 4); Zero(a, "r8"); Zero(a, "r9"); Zero(a, "r10"); Zero(a, "r11");
            for (int j = 0; j < M; j++)
            {
                int o = 32 * j;
                Ld(a, "lr", "r1", O_SET + O_MU + 4 * j); Ld(a, "ip", "r1", o); Sub(a, "ip", "ip", "r4"); Smlal(a, "r8", "r9", "lr", "ip");
                Ld(a, "ip", "r1", o + 4); Sub(a, "ip", "ip", "r6"); Smlal(a, "r10", "r11", "lr", "ip");
            }
            LsrI(a, "r8", "r8", 16); Orr(a, "r8", "r8", "r9", Asm.SH_LSL, 16); Add(a, "r8", "r8", "r4"); St(a, "r8", "r1", O_XB);
            LsrI(a, "r10", "r10", 16); Orr(a, "r10", "r10", "r11", Asm.SH_LSL, 16); Add(a, "r10", "r10", "r6"); St(a, "r10", "r1", O_VB);
        }

        // ================================================================ the C# copy (the twin checks the firmware against it)
        const long M32 = 0xFFFFFFFFL;
        static int Ss(int v, int bits) { int hi = (1 << (bits - 1)) - 1, lo = -hi - 1; return v > hi ? hi : v < lo ? lo : v; }
        static int Us(int v, int bits) { long hi = (1L << bits) - 1; return v < 0 ? 0 : v > hi ? (int)hi : v; }
        static int MulSh(int x, int y, int sh) { return (int)(((long)x * y) >> sh); }
        static int Clz(int v) { int n = 0; while (n < 32 && ((v >> (31 - n)) & 1) == 0) n++; return n; }

        static void Start(RobustK k, RobustAxis s, int zc, int n0)
        {
            for (int j = 0; j < k.M; j++) { s.X[j] = zc; s.V[j] = 0; s.P00[j] = 1 << F0; s.P01[j] = 0; s.P11[j] = 1 << F2; s.MU[j] = 65536 / k.M; }
            s.MU[0] += 65536 - (65536 / k.M) * k.M;
            s.XB = zc; s.VB = 0; s.Z1 = zc; s.Z2 = zc; s.N = n0 << 8; s.INV = KINV / s.N;
        }
        // one axis of one frame (the firmware's aprx): rec = band * 2 + axis, warm false = start over at the reading,
        // z in 1 / (100 << u) mm; returns the position to report
        public static int Axis(ApipModel m, RobustK k, int rec, RobustAxis s, int z, bool warm, int u)
        {
            int zc = Apip.Corrected(m, rec, z, u);
            s.Z = zc;
            if (!warm) Start(k, s, zc, m.N0[rec]);
            else
            {
                int z1 = s.Z1, z2 = s.Z2;
                s.Z2 = z1; s.Z1 = zc;
                int d2 = z2 - z1 - z1 + zc, sg = d2 >> 31, ad = (d2 ^ sg) - sg, cap = s.N << 1;
                if ((ad & M32) > (cap & M32)) ad = cap;
                int n = s.N + ((ad - s.N) >> 5);
                n = 256 + Us(n - 256, 20);                              // 256 ... 256 + 2^20 - 1
                s.N = n; s.INV = KINV / n;
                int r = zc - s.XB, sr = r >> 31, ar = (r ^ sr) - sr;
                if ((ar & M32) >= 1 << 19) Start(k, s, zc, m.N0[rec]);
                else Step(k, rec >> 1, s, zc);
            }
            int o = (s.XB + (128 >> u)) >> (8 - u);
            if (o < 0) o = 0;
            int mx = ((rec & 1) == 0 ? Apip.MAX_X : Apip.MAX_Y) << u;
            return o > mx ? mx : o;
        }
        static void Step(RobustK k, int band, RobustAxis s, int z)
        {
            int M = k.M, sh1 = F1 - F0 - 1, sh2 = F2 - F0, sh3 = F2 - F1;
            // the switch weights
            for (int j = 0; j < M; j++)
            {
                int num = (int)((((long)k.A * s.MU[j]) & M32) >> 16), c = k.B + num; s.C[j] = c;
                int t = (int)(((long)num << 15) / c); s.T[j] = t; s.U[j] = (int)((((long)(32768 - t) * t) & M32) >> 15);
            }
            int xb = s.XB, vb = s.VB, inv = s.INV;
            long g0 = 0, g1 = 0, g2 = 0;
            for (int i = 0; i < M; i++)
            {
                int ds = Ss(MulSh(s.X[i] - xb, inv, 14), 16); s.DS[i] = ds;
                g0 += (long)s.MU[i] * (s.P00[i] + ds * ds); g1 += (long)s.MU[i] * s.P01[i]; g2 += (long)s.MU[i] * s.P11[i];
            }
            int G0 = (int)(g0 >> 16), G1 = (int)(g1 >> 16), G2 = (int)(g2 >> 16);
            for (int j = 0; j < M; j++)
            {
                int t = s.T[j], u = s.U[j], q = band * 4 + j;
                int x0 = MulSh(t, s.X[j] - xb, 15) + xb, v0 = MulSh(t, s.V[j] - vb, 15) + vb;
                s.V[j] = v0; s.X[j] = x0 + v0;
                int ds = s.DS[j];
                int p0 = Ss(MulSh(t, s.P00[j] - G0, 15) + G0 + MulSh(u, ds * ds, 15), 30);
                int p1 = Ss(MulSh(t, s.P01[j] - G1, 15) + G1, 30), p2 = Ss(MulSh(t, s.P11[j] - G2, 15) + G2, 30);
                s.P00[j] = Us(p0 + (p1 >> sh1) + (p2 >> sh2) + k.Q4[q], 31);
                s.P01[j] = p1 + (p2 >> sh3) + k.Q2[q];
                s.P11[j] = p2 + k.Q1[q];
            }
            for (int j = 0; j < M; j++)
            {
                int a0 = s.P00[j], a1 = s.P01[j], a2 = s.P11[j], S = a0 + (1 << F0);
                int n = Clz(S); long Sn = ((long)S << n) & M32;
                int idx = (int)((Sn >> 27) & 15) - 16 * n + 176;
                int R = (int)(M32 / (Sn >> 16));
                int Rn = (int)((((long)R) << n) & M32);
                int K0 = (1 << 24) - (int)((Rn & M32) >> 4);
                int K1 = (int)(((long)a1 * Rn) >> (24 + F1 - F0));
                int rr = z - s.X[j];
                int rho = Ss(MulSh(rr, inv, 14), 16), r2 = rho * rho;
                int v29 = (int)(((long)r2 * R) >> 29), u8 = Us((int)((v29 & M32) >> (11 - n)), 16);
                int l = -k.TG1[idx] + ((k.TG2[idx] * u8) >> 16), li = (l + 2048) >> 4, lc = Us(li, 8);   // TG1 holds TG1 - C0
                int wi = k.TW[lc], sp = li > 255 ? l : k.TSP[lc];
                int K0w = MulSh(K0, wi, 15), K1w = MulSh(K1, wi, 15);
                s.X[j] = s.X[j] + MulSh(K0w, rr, 24); s.V[j] = s.V[j] + MulSh(K1w, rr, 24);
                s.P00[j] = a0 - MulSh(K0w, a0, 24); s.P01[j] = a1 - MulSh(K0w, a1, 24); s.P11[j] = a2 - MulSh(K1w, a1, 24 + F1 - F2);
                s.L[j] = sp - k.THL[idx] - (u8 >> 1);
            }
            // the weights
            int mx = s.L[0], jm = 0;
            for (int j = 1; j < M; j++) if (s.L[j] > mx) { mx = s.L[j]; jm = j; }
            long sum = 0;
            for (int j = 0; j < M; j++)
            {
                int l2 = (Us(mx - s.L[j], 15) * 369) >> 8, sk = l2 >> 8;
                int e = sk >= 32 ? 0 : k.TEX[(l2 >> 2) & 63] >> sk;
                s.EX[j] = (int)((((long)e * s.C[j]) & M32) >> 8); sum = (sum + s.EX[j]) & M32;
            }
            long rec = M32 / sum; int ms = 0;
            for (int j = 0; j < M; j++) { int mm = (int)((((long)s.EX[j] * rec) & M32) >> 16); if (mm < 1) mm = 1; s.MU[j] = mm; ms += mm; }
            s.MU[jm] += 65536 - ms;
            // the mixture
            long sx = 0, sv = 0; int xr = s.X[jm], vr = s.V[jm];
            for (int j = 0; j < M; j++) { sx += (long)s.MU[j] * (s.X[j] - xr); sv += (long)s.MU[j] * (s.V[j] - vr); }
            s.XB = xr + (int)(sx >> 16); s.VB = vr + (int)(sv >> 16);
        }

        // ---- RAM (the twin's comparison)
        public static void Get(RobustAxis s, byte[] b, int o)
        {
            for (int j = 0; j < 4; j++)
            {
                int q = o + 32 * j;
                s.X[j] = G32(b, q); s.V[j] = G32(b, q + 4); s.P00[j] = G32(b, q + 8); s.P01[j] = G32(b, q + 12); s.P11[j] = G32(b, q + 16); s.DS[j] = G32(b, q + 20);
                s.MU[j] = G32(b, o + O_SET + O_MU + 4 * j); s.C[j] = G32(b, o + O_SET + O_C + 4 * j); s.T[j] = G32(b, o + O_SET + O_T + 4 * j);
                s.U[j] = G32(b, o + O_SET + O_U + 4 * j); s.L[j] = G32(b, o + O_SET + O_L + 4 * j); s.EX[j] = G32(b, o + O_SET + O_EX + 4 * j);
            }
            s.XB = G32(b, o + O_XB); s.VB = G32(b, o + O_VB); s.Z = G32(b, o + O_Z); s.INV = G32(b, o + O_INV); s.N = G32(b, o + O_N); s.Z1 = G32(b, o + O_Z1); s.Z2 = G32(b, o + O_Z2);
        }
        // the state that carries over to the next reading (the trackers, the filter's position and speed, the noise,
        // the last readings, the weights) is the same as the firmware's; null, or what differs
        public static string Differs(RobustAxis s, int M, byte[] b, int o)
        {
            RobustAxis f = new RobustAxis(); Get(f, b, o);
            for (int j = 0; j < M; j++)
            {
                if (f.X[j] != s.X[j] || f.V[j] != s.V[j]) return "tracker " + F.Str(j) + " position or speed";
                if (f.P00[j] != s.P00[j] || f.P01[j] != s.P01[j] || f.P11[j] != s.P11[j]) return "tracker " + F.Str(j) + " uncertainty";
                if (f.MU[j] != s.MU[j]) return "tracker " + F.Str(j) + " weight";
            }
            if (f.XB != s.XB || f.VB != s.VB) return "position or speed";
            if (f.Z != s.Z || f.Z1 != s.Z1 || f.Z2 != s.Z2) return "readings";
            if (f.N != s.N || f.INV != s.INV) return "noise";
            return null;
        }
    }
}
