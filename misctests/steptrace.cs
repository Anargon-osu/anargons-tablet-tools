// misctests/steptrace.cs - every drive step of every frame, per pen height, in the digital twin.
// Run: python -I misctests/run.py steptrace HEX HEIGHTS MS     (HEIGHTS = 1,3,5 or "lost"; MS = how long per height)
// Never opens a real tablet.
//
// One line per frame: the hover stage byte (VB[0]: 0 fast, 1 Steady Hover, 2 reach), the frame time, then each
// drive step as PULSES@kHz, repeats as *N. A slow read inside a run of fast frames shows as a stage-1 line with
// longer bursts (rc3's probe frames did). Height "lost": the pen is found at 2 mm, then taken away.
// Count the patterns with: ... | sed 's/  [0-9]* us//' | sort | uniq -c
using System;
using System.Text;
using Gd32Emu;

namespace AnargonsTabletTools.MiscTests
{
    public static class StepTrace
    {
        public static int Main(string[] a)
        {
            if (a.Length < 3) { Console.WriteLine("usage: steptrace HEX HEIGHTS MS"); return 2; }
            System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
            byte[] flash = Twin.LoadImage(a[0]);
            double ms = double.Parse(a[2], inv);
            foreach (string hs in a[1].Split(','))
            {
                bool lost = hs == "lost";
                double h = lost ? 2.0 : double.Parse(hs, inv);
                Pen pen = new Pen(7); pen.f0 = 505000; pen.Q = 300; pen.hasHeight = true; pen.integrate = true; pen.height = h;
                StringBuilder why = new StringBuilder();
                Machine mc = Twin.ScanMachine(flash, pen, false, why);
                if (mc == null) { Console.WriteLine(why.ToString()); return 1; }
                mc.RunMs(400);
                if (lost) { pen.present = false; mc.RunMs(30); }
                TabletBoard tb = TabletBoard.Of(mc);
                Console.WriteLine("== height " + hs);
                long lastEnd = tb.txEnd; int seen = tb.markTag.n;
                long fStart = mc.cpu.cycles;
                StringBuilder steps = new StringBuilder(); string prev = null; int run = 0;
                for (double t = 0; t < ms; t += 0.002)
                {
                    mc.RunMs(0.002);
                    if (tb.txEnd != lastEnd && tb.txEnd > tb.txStart && tb.txPeriod > 0)
                    {
                        lastEnd = tb.txEnd;
                        string s = ((tb.txEnd - tb.txStart + tb.txPeriod / 2) / tb.txPeriod) + "@" + (int)(tb.txFreq / 1000);
                        if (s == prev) run++; else { if (prev != null) steps.Append(prev + (run > 1 ? "*" + run : "") + " "); prev = s; run = 1; }
                    }
                    while (seen < tb.markTag.n)
                    {
                        if (tb.markTag.a[seen] == 1)
                        {
                            if (prev != null) steps.Append(prev + (run > 1 ? "*" + run : "") + " ");
                            long now = tb.markCyc.a[seen];
                            Console.WriteLine("stage " + mc.Read(Sched.VB_ADDR, 1) + "  " + ((now - fStart) / 72.0).ToString("F0", inv) + " us  " + steps.ToString());
                            fStart = now; steps.Length = 0; prev = null; run = 0;
                        }
                        seen++;
                    }
                }
            }
            return 0;
        }
    }
}
