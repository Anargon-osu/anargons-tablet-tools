// misctests/recordergaps.cs - where the ALPI-training recorder (USB string 180) skips samples, in the digital twin.
// Run: python -I misctests/run.py recordergaps HEX [HEIGHT] [MS] [EVERY]   (HEIGHT twin scale, default 0; MS 1000;
//   EVERY = read string 180 every EVERY ms, default 20 as the silent check)
// Never opens a real tablet. The same setup as the study tool's silent check (a pen at x 10.3, y 7.6, 505 kHz, Q 300;
// 300 ms to settle, then string 180 read every EVERY ms). One read carries at most 20 samples (12 bytes each in a
// 255-byte string), so at a true 1000 frames/s a 20 ms interval sometimes leaves one behind: R45-rc4 ULTRA, 4 in 1 s. For each gap in the sample sequence: the samples on both sides
// (seq, x, y, level, tag = aux >> 12 & 3, pressure = aux & 0xFFF) and the ladder step / stage at the read.
using System;
using System.Text;
using Gd32Emu;

namespace AnargonsTabletTools.MiscTests
{
    public static class RecorderGaps
    {
        public static int Main(string[] a)
        {
            if (a.Length < 1) { Console.WriteLine("usage: recordergaps HEX [HEIGHT] [MS]"); return 2; }
            System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
            byte[] flash = Twin.LoadImage(a[0]);
            double h = a.Length > 1 ? double.Parse(a[1], inv) : 0;
            int ms = a.Length > 2 ? int.Parse(a[2]) : 1000, every = a.Length > 3 ? int.Parse(a[3]) : 20;
            Pen pen = new Pen(5);
            pen.x = 10.3; pen.y = 7.6; pen.f0 = 505000; pen.Q = 300; pen.hasHeight = true; pen.height = h; pen.integrate = true;
            StringBuilder why = new StringBuilder();
            Machine mc = Twin.ScanMachine(flash, pen, false, why);
            if (mc == null) { Console.WriteLine(why.ToString()); return 1; }
            VirtualHost host = new VirtualHost(mc.usb);
            mc.RunMs(300);
            ApipData skip = new ApipData();
            if (Apip.Read(mc, host, skip) < 0) { Console.WriteLine("read-out refused"); return 1; }
            ApipData d = new ApipData(); d.last = skip.last;
            int[] stepAt = new int[4096];
            for (int t = 0; t < ms; t++)
            {
                mc.RunMs(1);
                if (t % every == every - 1)
                {
                    int before = d.n;
                    if (Apip.Read(mc, host, d) < 0) { Console.WriteLine("read-out refused"); return 1; }
                    int st = mc.Read(Sched.VBX_ADDR + 8, 1) * 10 + mc.Read(Sched.VB_ADDR, 1);
                    for (int i = before; i < d.n; i++) stepAt[i] = st;
                }
            }
            int gaps = 0;
            for (int i = 1; i < d.n; i++)
            {
                if (d.seq[i] == d.seq[i - 1] + 1) continue;
                gaps++;
                for (int k = Math.Max(0, i - 2); k <= Math.Min(d.n - 1, i + 1); k++)
                    Console.WriteLine((k == i ? "  > " : "    ") + "seq " + d.seq[k] + "  x " + d.x[k] + " y " + d.y[k] + "  level " + d.lvl[k] + "  tag " + ((d.aux[k] >> 12) & 3)
                        + "  pressure " + (d.aux[k] & 0xFFF) + "  step/stage at read " + (stepAt[k] / 10) + "/" + (stepAt[k] % 10));
                Console.WriteLine();
            }
            Console.WriteLine(d.n + " samples, " + gaps + " gaps, " + mc.reports.n + " IN reports");
            return 0;
        }
    }
}
