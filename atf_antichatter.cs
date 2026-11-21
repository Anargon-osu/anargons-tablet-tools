// atf_antichatter.cs (R45-rc5) - Dynamic Anti-Chatter: smoothing only on the frames that chatter.
//
// Why (Anargon, rc5: "switch match is just not the solution"; "Prevents chatter by only applying smoothing on
// detection of chatter"). With Window Scale and Switch Match the switch steps are small, but single frames still jump
// out of line: on the 45-degree drags (rev-output/hw45/drag/drag45.csv, experiments/noise45/compare5.py) 44 % of
// frames have a second difference over 0.10 mm on X, 99 % up to 0.51 mm (Y 0.42 mm). A filter on every frame would
// add its delay all the time; this one adds it only where a frame chatters.
//
// What: per axis, the noise of a frame is its second difference d2 = p0 - 2 p1 + p2 (p0 this frame's position, p1 and
// p2 the two before). Over 0.10 mm the frame is pulled toward the one before: out = p0 + ws (p1 - p0). The pull is
// soft (rc5, Anargon: "if theres any way to absolutely minimize the latency to way lower then that would be nice"):
// ws = w x min(1, (|d2| - 0.10 mm) / 0.10 mm), so a frame just over the line gets almost none and only a frame
// 0.20 mm or more off it gets the full w, the previous frame's weight that gives 0.4 ms of delay at this frame's
// period (w = 0.4 ms / period, at most 0.5). Otherwise the position passes unchanged.
// Hard vs soft offline (experiments/noise45/dacvariants.py, drag45.csv, ULTRA 1000 Hz): the same X chatter 99 %
// (0.275 hard, 0.281 soft mm; Y 0.237 / 0.246) at 35 % less mean delay on X (0.180 -> 0.118 ms over all frames,
// 0.262 ms on a smoothed one) and 43 % less on Y (0.131 -> 0.075 ms); the most is still 0.4 ms. The history always holds the INPUT positions, never the outputs, so
// nothing builds up or drifts, and a still pen with no chatter is never touched. It is the last stage of the frame,
// after Jump Rejection and ALPI; nothing is placed ahead of the pen (no momentum or prediction).
// Offline on the drags, before ALPI, with Window Scale (compare5.py, both test halves): X frame chatter 99 % 0.51 ->
// 0.28 mm, frames over 0.10 mm 44 -> 26 %; Y 0.42 -> 0.24 mm; switch jumps and the wave across each coil unchanged.
// It was active on about a quarter of the frames there (after ALPI it should be fewer; not measured).
//
// Firmware: "dachat" runs in the frame restart right after ALPI, on D+8 / D+10 (the frame's position, report units:
// 1/200 mm in report mode 4, else 1/100 mm, where the threshold is halved). The weight comes from a table with one byte
// per Steady Hover ladder step (VBX+8), or per hover stage (VB[0]) without the ladder. No pen (D+7 = 255) clears the
// history; so does the search restart (EmitReset). RAM at DAC (right after Switch Match's block, inside the free
// 0x20001070-0x20001F7F): +0 X p1, +2 Y p1, +4 X p2, +6 Y p2 (u16), +8 frames of history (0..2).
using System;

namespace AnargonsTabletTools
{
    public static class DynamicAntiChatter
    {
        public const int DAC = 0x20001ED0, BYTES = 16;
        public const int THR = 20;                 // 0.10 mm in 1/200 mm; half of it (10) in the 1/100 mm unit
        public const int MAX_DELAY_US = 400;       // the delay of an averaged frame

        static string[] Rg(params string[] r) { return r; }

        // One byte per frame period: the previous frame's weight (256 = 1) for 0.4 ms of delay, at most 128 (0.5).
        // The length is rounded up to a multiple of 4 (padding 0) so the next data block stays word-aligned.
        public static byte[] WeightTable(int[] periodsUs)
        {
            int n = Math.Max(4, (periodsUs.Length + 3) / 4 * 4);
            byte[] t = new byte[n];
            for (int i = 0; i < periodsUs.Length; i++)
            {
                int p = periodsUs[i] > 0 ? periodsUs[i] : 1000;
                t[i] = (byte)Math.Min(128, (256 * MAX_DELAY_US + p / 2) / p);
            }
            return t;
        }

        // C# copy of one axis with one weight: the outputs for the inputs p (the first two frames pass unchanged).
        public static int[] Model(int[] p, int w, int thr)
        {
            int[] o = new int[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                o[i] = p[i];
                if (i < 2) continue;
                int d2 = p[i] - 2 * p[i - 1] + p[i - 2];
                if (d2 < 0) d2 = -d2;
                if (d2 <= thr) continue;
                int ws = w * Math.Min(d2 - thr, thr) / thr;                 // soft: grows with the noise over the threshold
                int v = p[i] + ((ws * (p[i - 1] - p[i])) >> 8);
                o[i] = v < 0 ? 0 : v;
            }
            return o;
        }

        // In the search restart (boot and every pen loss): no history. Uses only r0 and r1.
        public static void EmitReset(Asm a)
        {
            a.Mov32("r1", DAC); a.Movs("r0", 0); a.Strb("r0", "r1", 8);
        }

        // "dachat", called with Bl after ALPI. r4 weight, r5 threshold, r6 D, r7 DAC.
        public static void Emit(Asm a, int wTab, int nW, bool ladder)
        {
            a.Label("dachat");
            a.Push(Rg("r4", "r5", "r6", "r7"), true);
            a.Mov32("r7", DAC); a.Mov32("r6", Sched.D_ADDR);
            a.Ldrb("r0", "r6", 7); a.CmpI("r0", 255); a.Bcc("ne", "dc_pen");
            a.Movs("r0", 0); a.Strb("r0", "r7", 8); a.B("dc_end");             // no pen: forget the history
            a.Label("dc_pen");
            a.Movs("r5", THR);
            a.Mov32("r1", Sched.REPORT_MODE); a.Ldrb("r1", "r1", 0); a.CmpI("r1", 4); a.Bcc("eq", "dc_u");
            a.Movs("r5", THR / 2);                                               // 1/100 mm unit
            a.Label("dc_u");
            if (ladder) { a.Mov32("r1", Sched.VBX_ADDR); a.Ldrb("r1", "r1", 8); }
            else { a.Mov32("r1", Sched.VB_ADDR); a.Ldrb("r1", "r1", 0); }
            a.CmpI("r1", nW - 1); a.Bcc("ls", "dc_w"); a.Movs("r1", nW - 1);
            a.Label("dc_w"); a.Mov32("r2", wTab); a.LdrbR("r4", "r2", "r1");
            EmitAxis(a, 0); EmitAxis(a, 1);
            a.Ldrb("r0", "r7", 8); a.CmpI("r0", 2); a.Bcc("cs", "dc_end"); a.AddsI("r0", 1); a.Strb("r0", "r7", 8);
            a.Label("dc_end");
            a.Pop(Rg("r4", "r5", "r6", "r7"), true);
        }

        // One axis: r0 = p0 (the input); with two frames of history and |d2| over the threshold, D's field gets
        // p0 + ((ws (p1 - p0)) >> 8), ws = w min(|d2| - thr, thr) / thr; then p2 = p1, p1 = p0 (inputs).
        static void EmitAxis(Asm a, int q)
        {
            string s = q == 0 ? "x" : "y";
            int df = 8 + 2 * q, h1 = 2 * q, h2 = 4 + 2 * q;
            a.Ldrh("r0", "r6", df);
            a.Ldrb("r1", "r7", 8); a.CmpI("r1", 2); a.Bcc("cc", "dc_h" + s);
            a.Ldrh("r1", "r7", h1); a.Ldrh("r2", "r7", h2);
            a.Lsls("r3", "r1", 1); a.SubsR("r3", "r0", "r3"); a.AddsR("r3", "r3", "r2");   // d2 = p0 - 2 p1 + p2
            a.CmpI("r3", 0); a.Bcc("ge", "dc_a" + s); a.Rsbs("r3");
            a.Label("dc_a" + s); a.CmpR("r3", "r5"); a.Bcc("le", "dc_h" + s);              // quiet frame: unchanged
            a.SubsR("r3", "r3", "r5"); a.CmpR("r3", "r5"); a.Bcc("ls", "dc_r" + s); a.Mov("r3", "r5");   // e = min(|d2| - thr, thr)
            a.Label("dc_r" + s); a.Muls("r3", "r4"); a.Udiv("r3", "r3", "r5");              // soft weight w e / thr
            a.SubsR("r2", "r1", "r0"); a.Muls("r2", "r3"); a.Asrs("r2", "r2", 8); a.AddsR("r3", "r0", "r2");
            a.Bcc("pl", "dc_s" + s); a.Movs("r3", 0);
            a.Label("dc_s" + s); a.Strh("r3", "r6", df);
            a.Label("dc_h" + s);
            a.Ldrh("r1", "r7", h1); a.Strh("r1", "r7", h2); a.Strh("r0", "r7", h1);
        }
    }
}
