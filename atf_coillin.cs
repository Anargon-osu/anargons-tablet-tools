// atf_coillin.cs (R45-rc5) - Coil Linearization: a fixed correction of the position inside each coil.
//
// Why (Anargon: the coil-scale error "is still bad"; target 0.1 mm true position error; "we are only keeping the best
// one and it is opt in by default"). On Anargon's ruler recording (rev-output/hw45/ruler/ruler45.log, through the rc4sm
// ULTRA firmware's cursor) the error that repeats at the same place of every coil is 0.5-0.75 mm peak to peak along
// the line. Five ideas were weighed (history Part 17); this is the only one that measured better: learned on half of
// the ruler passes and tested on the other half (experiments/noise45/coillin_ruler.py --self), the coil-scale part
// went 0.75 / 0.56 -> 0.64 / 0.50 mm and 0.57 / 0.71 -> 0.50 / 0.53 mm (11-26 % less); the direction-dependent part did
// not improve (0.32 -> 0.35, 0.11 -> 0.18 mm). On the drags (drag45.csv) it did not help: they are too short and slow
// for the method. So it is opt in, for testing on the tablet.
// On the tablet (R45-rc5, 2026-11-16; referencedata/ruler, three ruler lines through the cursor of rc5 ULTRA + ALPI with
// and without it) it generalised: the coil-scale error went X 0.20-0.26 -> 0.08-0.15 mm and Y 0.08-0.14 -> 0.05-0.07 mm,
// with no more jumps or noise. The table is one tablet and pen, at ULTRA.
// R45 (2026-11-17): the table is table B, learned again on those three rc5 ruler lines (experiments/precision/cltables.py).
// In Anargon's play session and ruler session (Filter Arranger, experiments/precision/RESULTS.md) it beat the first table
// (AX / AY, still the Filter Arranger's CL-A) on a ruler line it was not learned on, with fewer flicks in play.
// Part of Misc. Optimizations (Build, "misc").
//
// How it is learned (experiments/noise45/coillin.py, "code density"): a pen moving at a steady speed spends equal time
// over every part of a coil, so the reported position should fall evenly over the phase inside each coil. Where the
// reports bunch up the arithmetic squeezes, where they thin out it stretches; the running share of the reports gives
// the true phase for each reported one. The table here was learned on all 10 full ruler passes (both directions,
// speed weights from a polynomial per pass, whole coils only). One table per axis, the same for every coil.
//
// What: per axis, on the frame's final position in report units (D+8 X, D+10 Y; 1/200 mm in report mode 4, else
// 1/100 mm, doubled for the phase and the correction halved): the phase inside the coil from the R43 coil grid
// (coil k at 163.53 - 5.080 k mm on X, 96.42 - 5.083 k mm on Y, experiments/noise45/noisept.py; the drags confirm the
// grid to 0.04 mm), then position + T(phase), T linear between 64 knots per coil. Nothing is remembered between
// frames: no delay, nothing ahead of the pen, a still pen cannot drift. It runs right before ALPI: the filter delays
// the coil wave about 5-6 ms, so a correction after it would miss a moving pen (referencedata/tools/lagsim.cs).
//
// Firmware: "clin", called with Bl. Table: 65 bytes per axis (X then Y; knot 64 = knot 0), each the correction in
// 1/200 mm + 128; padded to 132 bytes. Cost: about 60 cycles per axis (three hardware divides).
using System;

namespace AnargonsTabletTools
{
    public static class CoilLinearization
    {
        // knots 0..63 of the phase, corrections in 1/200 mm: table B (code density on the three rc5 ruler lines, 6 harmonics)
        public static readonly int[] TX = new int[] { 4, 2, 1, -1, -3, -4, -6, -7, -8, -9, -11, -12, -13, -14, -14, -15, -16, -17, -17, -17, -17, -17, -17, -16, -15, -15, -14, -13, -12, -11, -11, -10, -8, -7, -6, -4, -3, -1, 1, 2, 5, 7, 9, 12, 14, 16, 18, 20, 21, 21, 22, 22, 22, 21, 21, 20, 19, 17, 16, 14, 12, 10, 8, 6 };
        public static readonly int[] TY = new int[] { -12, -13, -13, -13, -13, -12, -12, -12, -12, -12, -13, -13, -14, -15, -15, -15, -14, -13, -12, -10, -8, -6, -4, -2, 0, 2, 5, 7, 9, 11, 13, 14, 15, 15, 15, 15, 15, 15, 14, 14, 14, 14, 14, 14, 14, 13, 13, 12, 11, 10, 9, 7, 5, 4, 2, 0, -2, -4, -5, -7, -8, -9, -11, -12 };
        // the first table (rc5; all 10 passes of the rc4sm ruler line, coillin.learn fit=5 whole): the Filter Arranger's CL-A
        public static readonly int[] AX = new int[] { 5, 4, 3, 3, 3, 1, 1, -3, -3, -4, -5, -6, -9, -11, -13, -14, -14, -14, -15, -15, -15, -15, -16, -16, -17, -17, -17, -16, -15, -14, -12, -11, -9, -7, -6, -5, -5, -3, -1, 0, 2, 3, 5, 7, 9, 12, 14, 17, 19, 20, 21, 22, 22, 20, 19, 17, 18, 16, 15, 14, 12, 10, 8, 6 };
        public static readonly int[] AY = new int[] { -12, -13, -13, -14, -12, -11, -9, -8, -8, -7, -8, -9, -12, -12, -12, -12, -11, -10, -9, -8, -6, -3, -1, 1, 1, 1, 2, 4, 7, 9, 12, 15, 17, 17, 17, 17, 16, 15, 15, 16, 17, 18, 18, 16, 17, 16, 15, 15, 13, 8, 3, 0, -2, -7, -9, -10, -11, -12, -12, -12, -11, -10, -10, -11 };
        public const int NK = 64, BYTES = 132;
        // the coil grid in 1/2000 mm: coil 0's centre (163.53 / 96.42 mm) and the pitch (5.080 / 5.083 mm); BASE = coil 0
        // plus 3 pitches, so the distance stays positive for every position the tablet reports
        public static readonly int[] OFF10 = new int[] { 327060, 192840 }, P10 = new int[] { 10160, 10166 };
        static int Base(int q) { return OFF10[q] + 3 * P10[q]; }
        static string[] Rg(params string[] r) { return r; }

        public static byte[] Table() { return Table(TX, TY); }
        public static byte[] Table(int[] tx, int[] ty)
        {
            byte[] t = new byte[BYTES];
            for (int q = 0; q < 2; q++)
            {
                int[] k = q == 0 ? tx : ty;
                for (int i = 0; i <= NK; i++) t[65 * q + i] = (byte)(k[i % NK] + 128);
            }
            return t;
        }

        // C# copy of the firmware arithmetic for one axis: v = the report value, mode = the report mode
        public static int Apply(int q, int v, int mode) { return Apply(q, v, mode, TX, TY); }
        // the same with other tables (the Filter Arranger's tables B and C, atf_lab.cs)
        public static int Apply(int q, int v, int mode, int[] tx, int[] ty)
        {
            int v2 = mode == 4 ? v : 2 * v;
            int d = Base(q) - 10 * v2;
            if (d < 0) return v;
            int p = P10[q], r = d - (d / p) * p, u = 64 * r, i = u / p, f = u - i * p;
            int[] k = q == 0 ? tx : ty;
            int a = k[i] + 128, b = k[(i + 1) % NK] + 128;
            int c = (a * (p - f) + b * f + p / 2) / p - 128;
            if (mode != 4) c >>= 1;
            int o = v + c;
            return o < 0 ? 0 : o;
        }

        // "clin": r4 a, r5 = 1 in the 1/100 mm unit, r6 D, r7 the axis's table.
        public static void Emit(Asm a, int tab) { Emit(a, tab, "clin", "cl_"); }
        // entry label and label prefix: the Filter Arranger build has three copies, one per table (atf_lab.cs)
        public static void Emit(Asm a, int tab, string entry, string L)
        {
            a.Label(entry);
            a.Push(Rg("r4", "r5", "r6", "r7"), true);
            a.Mov32("r6", Sched.D_ADDR);
            a.Ldrb("r0", "r6", 7); a.CmpI("r0", 255); a.Bcc("eq", L + "end");             // no pen: nothing to correct
            a.Movs("r5", 0);
            a.Mov32("r1", Sched.REPORT_MODE); a.Ldrb("r1", "r1", 0); a.CmpI("r1", 4); a.Bcc("eq", L + "u");
            a.Movs("r5", 1);                                                              // 1/100 mm unit
            a.Label(L + "u");
            EmitAxis(a, tab, 0, L); EmitAxis(a, tab, 1, L);
            a.Label(L + "end");
            a.Pop(Rg("r4", "r5", "r6", "r7"), true);
        }

        static void EmitAxis(Asm a, int tab, int q, string L)
        {
            string s = q == 0 ? "x" : "y";
            int df = 8 + 2 * q;
            a.Mov32("r7", tab + 65 * q);
            a.Ldrh("r1", "r6", df); a.LslsR("r1", "r5");                                // v in 1/200 mm
            a.Movs("r2", 10); a.Muls("r1", "r2");
            a.Mov32("r2", Base(q)); a.SubsR("r2", "r2", "r1"); a.Bcc("mi", L + "n" + s);  // d = BASE - 10 v (past the grid: unchanged)
            a.Mov32("r3", P10[q]);
            a.Udiv("r1", "r2", "r3"); a.Muls("r1", "r3"); a.SubsR("r2", "r2", "r1");      // r = d mod pitch
            a.Lsls("r2", "r2", 6); a.Udiv("r1", "r2", "r3");                              // knot i = 64 r / pitch
            a.Mov("r4", "r1"); a.Muls("r4", "r3"); a.SubsR("r2", "r2", "r4");             // f = 64 r - i pitch
            a.LdrbR("r4", "r7", "r1"); a.AddsI("r1", 1); a.LdrbR("r0", "r7", "r1");      // a = T[i] + 128, b = T[i + 1] + 128
            a.Muls("r0", "r2"); a.SubsR("r2", "r3", "r2"); a.Muls("r4", "r2"); a.AddsR("r0", "r0", "r4");   // a (p - f) + b f
            a.Lsrs("r4", "r3", 1); a.AddsR("r0", "r0", "r4"); a.Udiv("r0", "r0", "r3");   // rounded / pitch
            a.SubsI("r0", 128);
            a.CmpI("r5", 0); a.Bcc("eq", L + "h" + s); a.Asrs("r0", "r0", 1);              // half in the 1/100 mm unit
            a.Label(L + "h" + s);
            a.Ldrh("r1", "r6", df); a.AddsR("r1", "r1", "r0"); a.Bcc("pl", L + "s" + s); a.Movs("r1", 0);
            a.Label(L + "s" + s); a.Strh("r1", "r6", df);
            a.Label(L + "n" + s);
        }
    }
}
