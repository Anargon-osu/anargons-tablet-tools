// atf_stillnoise.cs (R45-rc5 (3), 2026-11-17) - Measurement Tools: Anargon's Still Pen Noise Test.
//
// Why (Anargon, 2026-11-17): "Build and package the still pen noise test from standard": the still hold of Standard
// calibration (the pen held still over the middle, its shake in mm) as a test of its own. Anargon (2026-11-16) also
// wants the lowest-noise defaults updated, "but lets do testing first": this is that testing. It ports the scoring of
// Claude-workarea/experiments/noise45/noisept.py into the program (AGENTS.md rule 18).
//
// What it does: reads the tablet's normal pen reports (what the cursor follows) with the same reader as calibration,
// for a few seconds while the pen is held still, and scores the hold with every spike kept in (a spike is part of
// the shake a user sees): the spread (sd), the widest swing (highest to lowest), and the frame-to-frame steps. Nothing
// is flashed: it measures whatever firmware the tablet runs now. On the Filter Arranger firmware (a --lab build) each
// hold is labelled with the filter order in use, and the next order can be set from this screen (ArrangerOps), so
// orders can be compared one after another. Every hold is saved under export/measurements/still-noise: a .csv of its
// reports and one line in still-noise.txt.
//
// The digital twin: with ADT_LAB_TWIN set to a firmware image, the reports come from the twin running that image
// (LabPort, the Filter Arranger's twin), its pen held at one place. The gate drives this screen that way.
using System;
using System.Text;
using Gd32Emu;

namespace AnargonsTabletTools
{
    // the work behind the screen: reading the pen reports and scoring a still hold
    public static class StillNoiseOps
    {
        public const string INTRO = "How much the cursor shakes while the pen is held still: the tablet's normal pen reports "
            + "(what the cursor follows), read for a few seconds, with every spike kept in. Nothing is flashed: it measures "
            + "the firmware the tablet runs now.";
        public const string HOW = "Hold the pen still for the whole measurement: in a pen stand over the middle of the drawing "
            + "area, or resting on its tip. The cursor keeps following the pen. Exit your tablet driver first: " + Cli.DRIVER_HOW;
        public const string TELEMETRY = "The tablet runs this tool's measurement firmware (calibration readings, no cursor). "
            + "Flash a normal build first.";
        public const string WAIT_ORDER = "The new filter order starts when the pen leaves the tablet: lift the pen out of range, "
            + "put it back, then press Enter.";
        public const string NO_LAB = "Setting a filter order needs the Filter Arranger firmware (a --lab build).";

        public const double UNITS_MM = 100.0;               // normal pen reports: 1/100 mm
        public const int DEFAULT_SECS = 10, MIN_SECS = 1, MAX_SECS = 60, KEEP = 64;
        public static readonly double[] STEP_MM = new double[] { 0.05, 0.1 };
        public const int GAP_X = 3;                         // a step across a gap over 3x the usual interval is not counted

        // one hold's score (mm); why = null when scored, else why not
        public class Score
        {
            public string label = "", why = null;
            public int n = 0, lost = 0, tip = 0, tele = 0, steps = 0;
            public double secs = 0, rate = 0, mx = 0, my = 0;
            public double sdX = 0, sdY = 0, ppX = 0, ppY = 0, rmsX = 0, rmsY = 0;
            public int[] bigX = new int[2], bigY = new int[2];
        }

        // records: 10 report bytes + the time in microseconds each (P.HidCollect's format)
        public static Score Measure(int[] r, string label)
        {
            Score s = new Score();
            s.label = label;
            int recs = r.Length / 11;
            long[] xs = new long[recs + 1], ys = new long[recs + 1], ts = new long[recs + 1];
            for (int i = 0; i + 10 < r.Length; i += 11)
            {
                if (r[i] != 7) continue;
                if (r[i + 1] == 0xC0) { s.tele++; continue; }           // the measurement firmware's readings
                if (!Lab.InRange(r[i + 1])) { s.lost++; continue; }      // out of range (Lab.InRange: also the cursor-off mark)
                xs[s.n] = r[i + 2] | (r[i + 3] << 8); ys[s.n] = r[i + 4] | (r[i + 5] << 8); ts[s.n] = r[i + 10];
                if ((r[i + 1] & 1) != 0 || (r[i + 6] | (r[i + 7] << 8)) > 0) s.tip++;
                s.n++;
            }
            if (s.n == 0 && s.tele > 0) { s.why = TELEMETRY; return s; }
            if (s.n < 20) { s.why = "Too few pen reports (" + F.Str(s.n) + "): hold the pen in range over the tablet, and check that the tablet driver is closed."; return s; }
            s.secs = (ts[s.n - 1] - ts[0]) / 1e6;
            s.rate = s.secs > 0 ? (s.n - 1) / s.secs : 0;
            s.mx = Mean(xs, s.n) / UNITS_MM; s.my = Mean(ys, s.n) / UNITS_MM;
            s.sdX = Twin.Sd(xs, s.n) / UNITS_MM; s.sdY = Twin.Sd(ys, s.n) / UNITS_MM;
            s.ppX = Spread(xs, s.n) / UNITS_MM; s.ppY = Spread(ys, s.n) / UNITS_MM;
            // frame to frame: only between reports that follow each other (the reader can drop a few between chunks)
            long[] dt = new long[s.n];
            for (int i = 1; i < s.n; i++) dt[i - 1] = ts[i] - ts[i - 1];
            long gap = GAP_X * Cli.Pct(dt, s.n - 1, 0.5);
            if (gap < 1) gap = 1;
            double sx = 0, sy = 0;
            for (int i = 1; i < s.n; i++)
            {
                if (ts[i] - ts[i - 1] > gap) continue;
                double dx = (xs[i] - xs[i - 1]) / UNITS_MM, dy = (ys[i] - ys[i - 1]) / UNITS_MM;
                s.steps++; sx += dx * dx; sy += dy * dy;
                for (int k = 0; k < STEP_MM.Length; k++)
                {
                    if (Abs(dx) > STEP_MM[k]) s.bigX[k]++;
                    if (Abs(dy) > STEP_MM[k]) s.bigY[k]++;
                }
            }
            if (s.steps > 0) { s.rmsX = P.Sqrt(sx / s.steps); s.rmsY = P.Sqrt(sy / s.steps); }
            return s;
        }
        static double Abs(double v) { return v < 0 ? -v : v; }
        static double Mean(long[] v, int n) { double t = 0; for (int i = 0; i < n; i++) t += v[i]; return n > 0 ? t / n : 0; }
        static long Spread(long[] v, int n)
        {
            long lo = v[0], hi = v[0];
            for (int i = 1; i < n; i++) { if (v[i] < lo) lo = v[i]; if (v[i] > hi) hi = v[i]; }
            return hi - lo;
        }

        // why nothing could be scored, from what the reader saw (P.HidWhy); null when it says nothing
        public static string Problem(string why)
        {
            if (why == null || P.Len(why) == 0) return null;
            if (P.IndexOf(why, "reports arrive with ID") >= 0)
                return "The tablet sends reports this test cannot read (" + why + "), so its driver is probably running, or ran "
                    + "since the tablet was plugged in. Exit it: " + Cli.DRIVER_HOW + " Then unplug the tablet, plug it back in, and measure again.";
            return "No pen reports: " + why + ".";
        }

        // the label of a hold: the filter order in use (Filter Arranger firmware), else the firmware as built
        public static string Label(Lab.Status st)
        {
            if (st == null) return "the firmware as built";
            string s = ArrangerOps.OrderText(st.act);
            if ((st.avail & 1) != 0 && (st.actF & Lab.F_WS_OFF) != 0) s += ", Window Scale off";
            return s;
        }
        public static bool Waiting(Lab.Status st) { return st != null && (ArrangerOps.OrderWaiting(st) || ArrangerOps.FlagsWaiting(st)); }

        // the hold: a short settle, then SECS of reports with one line per second (where the pen is, or LOST)
        public static int[] Hold(PenReader rd, int secs)
        {
            rd.Collect(0.3);
            Cli.IntBuf all = new Cli.IntBuf();
            for (int k = 0; k < secs; k++)
            {
                int[] r = rd.Collect(1.0);
                for (int i = 0; i < r.Length; i++) all.Add(r[i]);
                P.Line("        " + Live(r) + "   " + F.Str(secs - k - 1) + " s left");
            }
            return all.Take();
        }
        static string Live(int[] r)
        {
            int n = 0; long x = 0, y = 0;
            for (int i = 0; i + 10 < r.Length; i += 11)
            {
                if (r[i] != 7 || r[i + 1] == 0xC0 || !Lab.InRange(r[i + 1])) continue;
                x = r[i + 2] | (r[i + 3] << 8); y = r[i + 4] | (r[i + 5] << 8); n++;
            }
            if (n == 0) return "pen LOST";
            return "pen at X " + F.Fx(x / UNITS_MM, 2) + " mm, Y " + F.Fx(y / UNITS_MM, 2) + " mm (" + F.Str(n) + " reports)";
        }

        // the result of one hold, for the screen
        public static StrList Describe(Score s, int k)
        {
            StrList l = new StrList();
            l.Add("Hold " + F.Str(k) + ": " + s.label);
            if (s.why != null) { l.Add("  " + s.why); return l; }
            l.Add("  " + Txt.PadR("Shake (sd)", 20) + "X " + Mm(s.sdX) + "   Y " + Mm(s.sdY));
            l.Add("  " + Txt.PadR("Widest swing", 20) + "X " + Mm(s.ppX) + "   Y " + Mm(s.ppY) + "   (highest to lowest)");
            l.Add("  " + Txt.PadR("Frame to frame", 20) + "X " + Mm(s.rmsX) + "   Y " + Mm(s.rmsY) + "   (typical step, rms)");
            for (int i = 0; i < STEP_MM.Length; i++)
                l.Add("  " + Txt.PadR("Steps over " + F.Fx(STEP_MM[i], 2) + " mm", 20) + "X " + Txt.PadR(Txt.PadL(F.Str(s.bigX[i]), 7), 10)
                    + "   Y " + Txt.PadR(Txt.PadL(F.Str(s.bigY[i]), 7), 10) + "   (of " + F.Str(s.steps) + " steps)");
            l.Add("  " + F.Str(s.n) + " reports in " + F.Fx(s.secs, 1) + " s (" + F.Fx(s.rate, 0) + " per second), pen at X " + F.Fx(s.mx, 1) + " mm, Y " + F.Fx(s.my, 1) + " mm");
            if (s.tip > 0) l.Add("  Tip down (pressure) in " + F.Str(s.tip) + " of them");
            if (s.lost > 0) l.Add("  The pen was lost in " + F.Str(s.lost) + " reports");
            return l;
        }
        static string Mm(double v) { return Txt.PadL(F.Fx(v, 4), 7) + " mm"; }

        // every scored hold so far, one line each
        public static StrList Table(Score[] s, int n)
        {
            StrList l = new StrList();
            l.Add("Results so far (mm, spikes kept in):");
            l.Add("        " + Txt.PadR("", 36) + "shake (sd)        widest swing      steps > " + F.Fx(STEP_MM[1], 2));
            l.Add("        " + Txt.PadR("", 36) + "X        Y        X        Y        X     Y");
            for (int i = 0; i < n; i++)
            {
                string lb = s[i].label;
                if (P.Len(lb) > 34) lb = P.Sub(lb, 0, 31) + "...";
                l.Add("  " + Txt.PadL(F.Str(i + 1), 3) + ".  " + Txt.PadR(lb, 36) + Txt.PadR(F.Fx(s[i].sdX, 4), 9) + Txt.PadR(F.Fx(s[i].sdY, 4), 9)
                    + Txt.PadR(F.Fx(s[i].ppX, 3), 9) + Txt.PadR(F.Fx(s[i].ppY, 3), 9) + Txt.PadR(F.Str(s[i].bigX[1]), 6) + F.Str(s[i].bigY[1]));
            }
            return l;
        }

        // export/measurements/still-noise: STAMP-holdK.csv (every report) and a line in still-noise.txt; returns the .csv
        public static string Folder() { return P.PathJoin(P.PathJoin(Folders.Of(Folders.EXPORT), "measurements"), "still-noise"); }
        public static string Save(string dir, Score s, int[] r, int k, string stamp)
        {
            P.MakeDirs(dir);
            string name = stamp + "-hold" + F.Str(k) + ".csv";
            StringBuilder sb = new StringBuilder("t_us,status,x,y,pressure,tilt_x,tilt_y\r\n");
            for (int i = 0; i + 10 < r.Length; i += 11)
            {
                if (r[i] != 7) continue;
                sb.Append(F.Str(r[i + 10])).Append(',').Append(F.Str(r[i + 1])).Append(',').Append(F.Str(r[i + 2] | (r[i + 3] << 8))).Append(',')
                  .Append(F.Str(r[i + 4] | (r[i + 5] << 8))).Append(',').Append(F.Str(r[i + 6] | (r[i + 7] << 8))).Append(',')
                  .Append(F.Str(r[i + 8])).Append(',').Append(F.Str(r[i + 9])).Append("\r\n");
            }
            P.AtomicWriteAllBytes(P.PathJoin(dir, name), Build.Encode(sb.ToString()));
            string sum = P.PathJoin(dir, "still-noise.txt");
            if (!P.FileExists(sum)) P.AppendText(sum, "# Anargon's Still Pen Noise Test: one line per hold (mm, spikes kept in)\r\n");
            P.AppendText(sum, stamp + "  hold " + F.Str(k) + "  " + s.label + "  sd X " + F.Fx(s.sdX, 4) + " Y " + F.Fx(s.sdY, 4)
                + "  widest X " + F.Fx(s.ppX, 3) + " Y " + F.Fx(s.ppY, 3) + "  step rms X " + F.Fx(s.rmsX, 4) + " Y " + F.Fx(s.rmsY, 4)
                + "  steps>0.05 X " + F.Str(s.bigX[0]) + " Y " + F.Str(s.bigY[0]) + "  steps>0.10 X " + F.Str(s.bigX[1]) + " Y " + F.Str(s.bigY[1])
                + "  " + F.Str(s.n) + " reports in " + F.Fx(s.secs, 1) + " s  pen at " + F.Fx(s.mx, 1) + "," + F.Fx(s.my, 1) + " mm  " + name + "\r\n");
            return P.PathJoin(dir, name);
        }
    }

    // the twin only: moves the twin's pen every millisecond (the Ruler Line Test's gate draws its passes with it)
    public interface IPenPath { void Move(Pen p, long ms); }

    // the pen reports: the tablet (P.HidOpenPen, calibration's reader) or, with ADT_LAB_TWIN set, the twin a LabPort runs
    public class PenReader
    {
        public static IPenPath twinPath = null;     // the twin only; null = the pen stays where LabPort put it
        LabPort port;
        bool open = false;
        long c0 = -1, moved = 0;
        public PenReader(LabPort p) { port = p; }
        bool InTwin() { return port != null && port.mc != null; }
        // opened afresh for every recording (R45, 2026-11-17): a report's time is the microseconds since the reader
        // opened, in an int (P.HidCollect), so it runs out 35.8 minutes after the open. A reader kept open for a whole
        // evening gave every later report the same time, and the Ruler Line Test then found no passes (Anargon's D1 to
        // D3 recordings).
        public string Open()
        {
            if (InTwin()) return "";
            if (open) { P.HidClose(); open = false; }
            string err = P.HidOpenPen();
            open = P.Len(err) == 0;
            return err;
        }
        public int[] Collect(double secs)
        {
            if (!InTwin()) return P.HidCollect(secs);
            Machine mc = port.mc;
            int r0 = mc.reports.n;
            if (c0 < 0) c0 = mc.cpu.cycles;             // one time origin for every call, as P.HidCollect's
            int ms = (int)(secs * 1000);
            for (int t = 0; t < ms; t++) { if (twinPath != null) twinPath.Move(port.pen, moved++); mc.RunMs(1); }
            Cli.IntBuf b = new Cli.IntBuf();
            for (int i = r0; i < mc.reports.n; i++)
            {
                Report r = mc.reports.a[i];
                if (r.ep != 2 || r.data == null || r.data.Length < 10) continue;
                for (int k = 0; k < 10; k++) b.Add(r.data[k] & 0xFF);
                b.Add((int)((r.cycle - c0) / 72));
            }
            return b.Take();
        }
        public void Close() { if (open) { P.HidClose(); open = false; } }
        public string Why() { return InTwin() ? "" : P.HidWhy(); }
    }

    // the screen: shows what StillNoiseOps returns and calls it
    public static class StillNoise
    {
        public const string TITLE = "Anargon's Still Pen Noise Test", SUB = "For testing purposes only";

        public static int Run()
        {
            LabPort port = ArrangerOps.Connect();
            PenReader rd = new PenReader(port);
            StillNoiseOps.Score[] done = new StillNoiseOps.Score[StillNoiseOps.KEEP];
            int nd = 0, holds = 0, secs = StillNoiseOps.DEFAULT_SECS;
            string note = null, stamp = P.Now("stamp");
            StrList last = null;
            try
            {
                while (true)
                {
                    Screen.Step(TITLE + " (" + SUB + ")");
                    Lab.Status st = ArrangerOps.Read(port);
                    Screen.Para(StillNoiseOps.INTRO, 4);
                    P.Line("");
                    Screen.Para(StillNoiseOps.HOW, 4);
                    P.Line("");
                    if (st != null)
                    {
                        P.Line("    Filters now:   " + StillNoiseOps.Label(st));
                        if (StillNoiseOps.Waiting(st)) P.Line("    Waiting:       " + ArrangerOps.OrderText(st.req) + ArrangerOps.WAITING);
                        P.Line("");
                    }
                    if (nd > 0)
                    {
                        StrList t = StillNoiseOps.Table(done, nd);
                        for (int i = 0; i < t.Count(); i++) P.Line("    " + t.Get(i));
                        P.Line("");
                    }
                    if (last != null)
                    {
                        for (int i = 0; i < last.Count(); i++) P.Line("    " + last.Get(i));
                        P.Line("");
                        last = null;
                    }
                    if (note != null) { Screen.Para(note, 4); P.Line(""); note = null; }
                    P.Line("    Enter = measure for " + F.Str(secs) + " s");
                    P.Line("    t N   = measure for N seconds from now on (" + F.Str(StillNoiseOps.MIN_SECS) + " to " + F.Str(StillNoiseOps.MAX_SECS) + ")");
                    if (st != null) P.Line("    o ... = set the filter order first, like o 1 2 6 10 (the Filter Arranger's numbers)");
                    P.Line("    q     = back");
                    P.Line("");
                    string c = P.Trim(Screen.Ask("    > "));
                    string lc = P.Lower(c);
                    if (P.Eq(lc, "q")) return 0;
                    if (P.StartsWith(lc, "t ") || P.Eq(lc, "t"))
                    {
                        string v = P.Trim(P.Sub(c, 1, P.Len(c) - 1));
                        int n = Num(v);
                        if (n < StillNoiseOps.MIN_SECS || n > StillNoiseOps.MAX_SECS) note = "Type t and a number of seconds from " + F.Str(StillNoiseOps.MIN_SECS) + " to " + F.Str(StillNoiseOps.MAX_SECS) + ", like t 10.";
                        else { secs = n; note = "Each measurement now takes " + F.Str(secs) + " s."; }
                        continue;
                    }
                    if (P.StartsWith(lc, "o ") || P.Eq(lc, "o"))
                    {
                        if (st == null) { note = StillNoiseOps.NO_LAB; continue; }
                        ArrangerOps.Result res = ArrangerOps.SetOrder(port, st, P.Trim(P.Sub(c, 1, P.Len(c) - 1)));
                        note = res.message;
                        if (res.ok) note = (note != null ? note + " " : "") + "Lift the pen out of range and put it back: the new order starts then.";
                        continue;
                    }
                    if (P.Len(c) > 0) { note = "Press Enter to measure, or type t N, " + (st != null ? "o and an order, " : "") + "or q."; continue; }
                    if (StillNoiseOps.Waiting(st)) { note = StillNoiseOps.WAIT_ORDER; continue; }
                    string err = rd.Open();
                    if (P.Len(err) > 0) { note = "The tablet could not be opened: " + err; continue; }
                    P.Line("");
                    P.Line("    Measuring for " + F.Str(secs) + " s: keep the pen still.");
                    int[] rec = StillNoiseOps.Hold(rd, secs);
                    holds++;
                    StillNoiseOps.Score s = StillNoiseOps.Measure(rec, StillNoiseOps.Label(st));
                    if (s.why != null && s.n == 0 && s.tele == 0) { string pr = StillNoiseOps.Problem(rd.Why()); if (pr != null) s.why = pr; }
                    last = StillNoiseOps.Describe(s, holds);
                    if (s.why != null) continue;
                    string csv = StillNoiseOps.Save(StillNoiseOps.Folder(), s, rec, holds, stamp);
                    last.Add("  Saved: " + csv);
                    if (nd == done.Length) { for (int i = 1; i < nd; i++) done[i - 1] = done[i]; nd--; }
                    done[nd++] = s;
                }
            }
            finally { rd.Close(); }
        }

        static int Num(string v)
        {
            if (P.Len(v) == 0 || P.Len(v) > 3) return -1;
            int n = 0;
            for (int i = 0; i < P.Len(v); i++) { int ch = P.CharAt(v, i); if (ch < '0' || ch > '9') return -1; n = n * 10 + (ch - '0'); }
            return n;
        }
    }
}
