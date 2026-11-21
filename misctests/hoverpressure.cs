// misctests/hoverpressure.cs - the core suite's fw-hover-pressure setup, frame by frame, in the digital twin.
// Run: python -I misctests/run.py hoverpressure STOCK KEY HOLDS ACCP [HEIGHT]
//   STOCK  a stock main.bin (February or December)      KEY    preset: 500, 800 or 1000
//   HOLDS  1 = Frequency Hold and Coil Hold on (default), 0 = off
//   ACCP   1 = Accurate Pressure Readings on, 0 = off    HEIGHT pen height on the twin scale (default 4)
// Never opens a real tablet. Builds the image like the suite (Cli.Defaults + those settings), then hovers a Q 150 pen
// resting at 506 kHz (3 kHz below where pressure starts) for 1.3 s.
//
// Per 50 ms: the ladder steps seen (VBX+8, as a string: "3210" = started at step 3, ended at 0), the level, pen
// reports, reports showing pressure or the tip, the sweep's drive step C[3], the Frequency Hold step / agree count,
// finished sweeps ("fins") and sweeps dropped as mixed ("mixes"), and the last report's flags / pressure.
// The suite wants: no holds > 100 pressed after 300 ms; holds 0; Accurate Pressure 0.
// R45-rc4 found with it: a found pen started at a 30-pulse step, its first sweep picked a step 2 higher, and every
// report showed pressure; Cfg.ladAcq now starts it at the first 40-pulse step.
using System;
using System.Text;
using Gd32Emu;

namespace AnargonsTabletTools.MiscTests
{
    public static class HoverPressure
    {
        public static int Main(string[] a)
        {
            if (a.Length < 4) { Console.WriteLine("usage: hoverpressure STOCK KEY HOLDS ACCP [HEIGHT]"); return 2; }
            System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
            InputInfo info = new InputInfo();
            byte[] app = Build.ReadInput(a[0], info);
            bool holds = a[2] == "1", accp = a[3] == "1";
            bool[] st = Cli.Defaults(); st[Build.SetIndex("accp")] = accp;
            if (!holds) { st[Build.SetIndex("fhold")] = false; st[Build.SetIndex("chold")] = false; }
            Build.quiet = true;
            byte[] img = Twin.FromApp(Build.BuildOne(app, info, null, Build.Lv(a[1]), st, true, false, new Options()).Bytes());
            Pen pen = new Pen(1);
            pen.x = 16.3; pen.y = 9.6; pen.f0 = Environment.GetEnvironmentVariable("HP_F0") != null ? int.Parse(Environment.GetEnvironmentVariable("HP_F0")) : 506000; pen.Q = 150; pen.strength = 60000; pen.noise = 6; pen.range = Pen.RANGE_TOUCH;
            pen.hasHeight = true; pen.height = a.Length > 4 ? double.Parse(a[4], inv) : 4; pen.resonator = true; pen.integrate = true;
            Machine m = new Machine(img, true, new TabletBoard(pen), false);
            Console.WriteLine("boot " + m.Boot(3000000));
            m.AttachUsb(true);
            int seenRep = 0, total = 0, pv1 = 0, pvq = 0;
            string tr = Environment.GetEnvironmentVariable("HP_TRACE");          // HP_TRACE=N: every 1 ms for the first N ms
            int trace = tr != null ? int.Parse(tr) : 0;
            for (int w = 0; w < 26; w++)
            {
                StringBuilder steps = new StringBuilder(); int last = -1; int lv = 0, fins = 0, mixes = 0;
                for (int t = 0; t < 50; t++)
                {
                    for (int u = 0; u < 20; u++)
                    {
                        m.RunMs(0.05);
                        int v1 = m.Read(Sched.V_ADDR + 1, 1), vq = m.Read(Sched.VQ_ADDR, 1);
                        if (v1 != 0 && pv1 == 0) fins++;
                        if (vq != 0 && pvq == 0) mixes++;
                        pv1 = v1; pvq = vq;
                    }
                    int p = m.Read(Sched.VBX_ADDR + 8, 1);
                    if (p != last) { steps.Append(p); last = p; }
                    if (trace > w * 50 + t)
                        Console.WriteLine("      " + (w * 50 + t) + " ms: step " + p + " acq " + m.Read(Sched.VBX_ADDR + 12, 1) + " fresh " + m.Read(Sched.V_ADDR + 1, 1)
                            + " mixed " + m.Read(Sched.VQ_ADDR, 1) + " C3 " + m.Read(Sched.C_ADDR + 3, 1) + " hold " + m.Read(Sched.HOLD_ADDR, 1) + "/" + m.Read(Sched.HOLD_ADDR + 1, 1)
                            + " level " + (m.Read(Sched.VB_ADDR + 8, 4) & 0xFFFF));
                    lv = m.Read(Sched.VB_ADDR + 8, 4) & 0xFFFF;
                }
                int pr = 0, rep = 0, lastP = -1;
                for (; seenRep < m.reports.n; seenRep++)
                {
                    Report r = m.reports.a[seenRep];
                    if (r.ep != 2 || r.data == null || r.data.Length < 8 || (r.data[0] & 0xFF) != 7) continue;
                    rep++;
                    int press = (r.data[6] & 0xFF) | ((r.data[7] & 0xFF) << 8);
                    if ((r.data[1] & 1) != 0 || press > 0) pr++;
                    lastP = (r.data[1] & 0xFF) * 65536 + press;
                }
                if (w >= 6) total += pr;
                Console.WriteLine((w * 50).ToString().PadLeft(5) + " ms: steps " + steps + "  level " + lv + "  reports " + rep + "  pressed " + pr
                    + "  C3 " + m.Read(Sched.C_ADDR + 3, 1) + "  hold " + m.Read(Sched.HOLD_ADDR, 1) + "/" + m.Read(Sched.HOLD_ADDR + 1, 1)
                    + "  fins " + fins + " mixes " + mixes + "  last flags/pressure 0x" + lastP.ToString("X"));
            }
            Console.WriteLine("pressed after 300 ms: " + total);
            return 0;
        }
    }
}
