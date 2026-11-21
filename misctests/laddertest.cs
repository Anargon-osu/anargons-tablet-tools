// misctests/laddertest.cs - Steady Hover ladder in the digital twin (R45-rc4). Run: python -I misctests/run.py laddertest HEX MODE ...
// Never opens a real tablet: everything runs in the twin (gd32emu).
//
//   still H1,H2,...   a still pen at each height (twin scale, not pen-tip mm), reached from below (raised from 0.5 mm
//                     at 20 mm/s) and from above (lowered from H+6 at 20 mm/s); held 800 ms. Prints the ladder step
//                     and stage at the end, the level, and how many step changes happened while held (want 0).
//   sweep MM_PER_S    raised from 0.5 mm until the pen is lost + 2 mm, then lowered to 0.5 mm. Prints every step
//                     change with its height and level. Changes at one level (58, 66, 68 ...) after "lost" are the
//                     lost-pen search taking turns between its two reads, not the ladder.
//
// An optional last argument Q=N sets the pen's Q (default 300, the twin's usual pen; Anargon's pen rings up as about
// Q 75, which matters for the many-pulse reads: the reach stage's 254 pulses read 2.7 x Steady Hover's 40 at Q 300,
// 1.45 x on the tablet).
//
// HEX is a built image (for example rev-output/hw45/rc4/ultra/[VOLATILE]_ultra-APIP.hex). A pen that starts 19 mm or
// more away from a cold start is not picked up in the twin (rc3 as well): "from above" at 13 mm and up shows level 0.
using System;
using Gd32Emu;

namespace AnargonsTabletTools.MiscTests
{
    public static class LadderTest
    {
        static double Q = 300;
        static Machine Make(byte[] flash, double h, Pen pen)
        {
            pen.f0 = 505000; pen.Q = Q; pen.hasHeight = true; pen.integrate = true; pen.height = h;
            System.Text.StringBuilder why = new System.Text.StringBuilder();
            Machine mc = Twin.ScanMachine(flash, pen, false, why);
            if (mc == null) throw new Exception(why.ToString());
            return mc;
        }
        static int Step(Machine mc) { return mc.Read(Sched.VBX_ADDR + 8, 1); }
        static int Level(Machine mc) { return mc.Read(Sched.VB_ADDR + 8, 4) & 0xFFFF; }
        // holds MS milliseconds; changes[0] = step changes seen
        static string Hold(Machine mc, int ms, int[] changes)
        {
            int last = Step(mc); changes[0] = 0;
            for (int t = 0; t < ms * 4; t++) { mc.RunMs(0.25); int p = Step(mc); if (p != last) { changes[0]++; last = p; } }
            return "step " + Step(mc) + " (stage " + mc.Read(Sched.VB_ADDR, 1) + ") level " + Level(mc);
        }
        static void Ramp(Machine mc, Pen pen, double from, double to, double speed)
        {
            double h = from, dir = to > from ? 1 : -1;
            while ((to - h) * dir > 0) { h += dir * speed / 1000.0; if ((to - h) * dir < 0) h = to; pen.height = h; mc.RunMs(1); }
        }
        public static int Main(string[] a)
        {
            if (a.Length < 3) { Console.WriteLine("usage: laddertest HEX still H1,H2,... | laddertest HEX sweep MM_PER_S"); return 2; }
            byte[] flash = Twin.LoadImage(a[0]);
            if (a.Length > 3 && a[a.Length - 1].StartsWith("Q=")) Q = double.Parse(a[a.Length - 1].Substring(2), System.Globalization.CultureInfo.InvariantCulture);
            System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
            if (a[1] == "still")
            {
                foreach (string hs in a[2].Split(','))
                {
                    double h = double.Parse(hs, inv);
                    int[] c1 = new int[1], c2 = new int[1];
                    Pen p1 = new Pen(7); Machine m1 = Make(flash, 0.5, p1); m1.RunMs(300); Ramp(m1, p1, 0.5, h, 20); m1.RunMs(200);
                    string r1 = Hold(m1, 800, c1);
                    Pen p2 = new Pen(9); Machine m2 = Make(flash, h + 6, p2); m2.RunMs(500); Ramp(m2, p2, h + 6, h, 20); m2.RunMs(200);
                    string r2 = Hold(m2, 800, c2);
                    Console.WriteLine("h " + h.ToString("F1", inv) + ": from below " + r1 + " (" + c1[0] + " changes)   from above " + r2 + " (" + c2[0] + " changes)");
                }
                return 0;
            }
            if (a[1] == "sweep")
            {
                double speed = double.Parse(a[2], inv);
                Pen pen = new Pen(7); Machine mc = Make(flash, 0.5, pen); mc.RunMs(300);
                int last = Step(mc); double h = 0.5, lost = -1; int quiet = 0;
                for (int pass = 0; pass < 2; pass++)
                {
                    while (true)
                    {
                        if (pass == 0) { h += speed / 1000; if (lost >= 0 && h > lost + 2) break; if (h > 45) break; }
                        else { h -= speed / 1000; if (h < 0.5) break; }
                        pen.height = h;
                        mc.RunMs(1);
                        int p = Step(mc);
                        bool inRange = mc.Read(Sched.D_ADDR + 7, 1) != 255;
                        if (pass == 0) { if (inRange) quiet = 0; else if (++quiet > 60 && lost < 0) { lost = h; Console.WriteLine("  lost at " + h.ToString("F2", inv)); } }
                        if (p != last) { Console.WriteLine((pass == 0 ? "up   " : "down ") + h.ToString("F2", inv) + " mm: step " + last + " -> " + p + " (level " + Level(mc) + ")"); last = p; }
                    }
                }
                return 0;
            }
            Console.WriteLine("unknown mode " + a[1]);
            return 2;
        }
    }
}
