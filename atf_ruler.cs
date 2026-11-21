// atf_ruler.cs (R45, 2026-11-17) - Measurement Tools: Anargon's Ruler Line Test.
//
// Why (Anargon, 2026-11-17): "I thought we'd do irl tests then rearrange the filter orders live to find the lowest
// distortion defaults". Distortion here is the position error the cursor repeats at the same place of the tablet
// (Anargon's target: 0.1 mm, history Parts 16 and 17). The ruler line test measures it: the pen is drawn along a ruler,
// back and forth, and every pass is compared with the straight line. This ports the scoring of
// Claude-workarea/experiments/noise45/ruler6.py (the rc5 ruler test, history Part 17) into the program (AGENTS.md
// rule 18), with Anargon's display idea for it: the drawing area as a box with the target line, its ends in mm from
// the edges, and the pen's trace of the last 3 s.
//
// What it does: reads the tablet's normal pen reports (what the cursor follows) while the pen is drawn along a ruler,
// with the same reader as the Still Pen Noise Test. Nothing is flashed. On the Filter Arranger firmware (a --lab
// build) each recording is labelled with the filter order in use, and the next order can be set from this screen, so
// orders are compared on the same ruler line without flashing. The recordings of one session are scored together: one
// line for all of them (the ruler stays put) and one stretch of it that every recording covers. Every recording is
// saved under export/measurements/ruler (a .csv of its reports), with the session's table in ruler.txt.
//
// Hover mode (h, 2026-11-17): the hover coil wave was never measured on a ruler (precision plan L8 / M3; the ALPI
// training shows 0.25-0.29 mm rms at high hover, recorded on an R44 build). The pen sits in a holder that keeps its
// tip above the tablet, and the holder is slid along the ruler; every report with the pen in range is scored (it
// cannot touch, so any pressure is pressure in the air, counted apart). Anargon: "warn people to slide the pen holder
// over the ruler instead of the pen, with the ruler preferably not blocking the pen from the tablet, for interference
// reasons". A recording's mode is in its label (HOVER_TAG), so a saved file loads back as what it was; switching the
// mode starts a new session.
//
// Scoring (as ruler6.py; mm):
//  - passes: reports with the tip down, joined across breaks under 400 ms and 3 mm, split where the pen turns back by
//    20 mm or more; passes of 80 mm or more are kept, without their first and last 150 ms. A line shorter than that
//    inside the drawing area (vertical: 90 mm) keeps passes of 0.7 of its length there (R45, 2026-11-17: Anargon's
//    vertical line D2 had passes of 64-90 mm, none counted);
//  - one line fitted to the passes of every recording, and one common stretch: the longest run of 0.25 mm bins that
//    every scored recording covers with 3 or more passes each way, 2 mm in from each end;
//  - the sideways error (a 9-report mean; each pass's own offset over the stretch is taken off) is averaged per bin over
//    the passes, each pass weighing the same: the repeatable error. Its slow part (a +-10 mm mean: the ruler's own bend,
//    the surface) and its coil-scale part (the rest: what the coils and the position filters do), forward minus back,
//    and the scatter from pass to pass;
//  - the coil-locked error: the coil-scale part (the mean of both directions) fitted as f(X coil phase) + g(Y coil
//    phase), on the R43 coil grid. The sideways direction is -sin(a) X + cos(a) Y, so an X error shows at sin(a) of its
//    size and a Y error at cos(a). Both need a line that crosses both axes' coils: within 15 degrees of an axis neither
//    shows (along X the Y phase hardly changes, and the X error is along the line), only the error across the line
//    that follows the coils along it (2026-11-17: the table showed Y 0.000 on Anargon's horizontal line, now "-");
//  - jumps per axis: a report more than 0.3 mm off the trend of the 10 reports before it is a flick when it is back
//    within 3 reports, a step when it stays off (0.2 mm over the next 5 to 15 reports), else a burst.
//
// Sessions and lines (2026-11-17, Anargon: "D1 and D2 share sessions for some reason"): a recording drawn on another
// line than the session's first (3 degrees or 3 mm apart) starts a new session by itself, so two ruler lines are never
// scored as one. l N picks the target line the box shows (the diagonal, the other diagonal, across, up the middle) and
// starts a new session too. A file saved before the reader was opened afresh per recording (PenReader.Open) can hold
// reports whose time ran out (int.MinValue): FromReports gives them the time of the report before plus 1 ms and says so.
//
// The digital twin: with ADT_LAB_TWIN set to a firmware image, the reports come from the twin (LabPort), its pen moved
// along PenReader.twinPath. The gate drives this screen that way.
using System;
using System.Text;
using Gd32Emu;

namespace AnargonsTabletTools
{
    public static class RulerOps
    {
        public const string INTRO = "How far the cursor is from where the pen really is: the pen is drawn along a ruler, back "
            + "and forth, and every pass is compared with the straight line. It finds the error that repeats at the same place "
            + "of the tablet (distortion), the steps and the flicks. Nothing is flashed: it measures the firmware the tablet runs now.";
        public const string HOW = "Lay a ruler on the tablet (about 15 cm long) and tape it or hold it so it cannot move. Draw "
            + "along it with the tip pressed, slowly and steadily, from one end to the other and back, about 5 times each way "
            + "per recording. Every recording of a session must use the same ruler line; start a new session (n) when the "
            + "ruler moves. The pen clicks while it is pressed, so keep an empty window under the cursor. Exit your tablet "
            + "driver first: " + Cli.DRIVER_HOW;
        public const string HOW_HOVER = "Hover mode: put the pen in a holder that keeps its tip a few mm above the tablet. Slide "
            + "the holder along the ruler, not the pen, so the height stays the same: from one end to the other and back, about 5 "
            + "times each way per recording. Lay the ruler so the pen tip hovers over the bare tablet beside it, not over the "
            + "ruler: anything between the pen and the tablet can disturb the reading. Every recording of a session must use the "
            + "same ruler line and holder; start a new session (n) when either moves. Exit your tablet driver first: " + Cli.DRIVER_HOW;
        public const string HOVER_TAG = ", hover";          // ends the label of a hover-mode recording
        // the target lines (l N): x0, y0, x1, y1 in mm, Y from the top; 1 is the rc5 line (history Part 17)
        public static readonly double[][] TARGETS = new double[][] {
            new double[] { 20, 75, 140, 15 }, new double[] { 20, 15, 140, 75 }, new double[] { 10, 45, 150, 45 }, new double[] { 80, 5, 80, 85 } };
        public static readonly string[] TARGET_NAMES = new string[] { "the diagonal", "the other diagonal", "across the middle", "up the middle" };
        static readonly string[] TARGET_ENDS = new string[] {
            "from 20 mm in from the left and 15 mm up from the bottom of the drawing area to 20 mm in from the right and 15 mm down from the top",
            "from 20 mm in from the left and 15 mm down from the top of the drawing area to 20 mm in from the right and 15 mm up from the bottom",
            "across the middle of the drawing area, 45 mm from the top and the bottom, from 10 mm in from the left to 10 mm in from the right",
            "up the middle of the drawing area, 80 mm from the left and the right, from 5 mm below the top to 5 mm above the bottom (the area "
                + "is 90 mm tall, so a pass counts from 63 mm)" };
        public static string TargetText(int k)
        {
            return "the target line, " + TARGET_NAMES[k] + ": put the ruler so the pen tip runs along it, " + TARGET_ENDS[k] + " (the drawing "
                + "area is 160 x 90 mm). The diagonals show both axes' coil error; a line along an axis shows only the error across it.";
        }
        public const string TRACE_TEXT = "# = where the pen was pressed in the last 3 s, o = where it hovered";
        public const string WAIT_ORDER = StillNoiseOps.WAIT_ORDER;

        public const double UNITS_MM = 100.0;               // normal pen reports: 1/100 mm
        public const int DEFAULT_SECS = 90, MIN_SECS = 5, MAX_SECS = 300, KEEP = 16;
        public const double BIN = 0.25, PASS_MM = 80, PASS_FRAC = 0.7, TRIM_MS = 150, JOIN_MS = 400, JOIN_MM = 3, TURN_MM = 20, EDGE_MM = 2;
        public const double MIN_STRETCH = 20, SLOW_BINS = 40, JUMP_MM = 0.3, BACK_MM = 0.12, STAY_MM = 0.2, AXIS_MIN = 0.25;
        public const double LINE_DEG = 3, LINE_MM = 3;      // another line than the session's: a new session
        public const int NEED = 3, MIN_REPORTS = 100, MEAN_H = 4, PHASES = 16;
        public static readonly double[] XC = new double[] { 163.53, 5.080 }, YC = new double[] { 96.42, 5.083 };   // R43: coil k at a - pitch k
        public const double AREA_W = 160, AREA_H = 90;
        public const int MAP_W = 64, MAP_H = 18, TRACE_SECS = 3;      // the live trace: the last 3 s (Anargon; 10 s until 2026-11-17)

        // one recording: the pen reports with the pen in range (mm, ms)
        public class Rec
        {
            public string label = "", file = null, why = null;
            public bool hover = false;                  // hover mode: every report in range is scored, not only tip down
            public int n = 0, lost = 0, tele = 0, down = 0, guessed = 0;      // guessed: reports whose time had run out
            public double[] t = new double[0], x = new double[0], y = new double[0];
            public int[] p = new int[0];
            public bool Use(int i) { return hover || p[i] > 0; }
        }

        public static bool IsHover(string label) { return label != null && P.EndsWith(label, HOVER_TAG); }

        // records: 10 report bytes + the time in microseconds each (P.HidCollect's format); the mode from the label
        public static Rec FromReports(int[] r, string label) { return FromReports(r, label, IsHover(label)); }
        public static Rec FromReports(int[] r, string label, bool hover)
        {
            Rec c = new Rec();
            c.label = label; c.hover = hover;
            int recs = r.Length / 11;
            c.t = new double[recs]; c.x = new double[recs]; c.y = new double[recs]; c.p = new int[recs];
            for (int i = 0; i + 10 < r.Length; i += 11)
            {
                if (r[i] != 7) continue;
                if (r[i + 1] == 0xC0) { c.tele++; continue; }
                if (!Lab.InRange(r[i + 1])) { c.lost++; continue; }      // Lab.InRange: also the cursor-off mark
                c.x[c.n] = (r[i + 2] | (r[i + 3] << 8)) / UNITS_MM; c.y[c.n] = (r[i + 4] | (r[i + 5] << 8)) / UNITS_MM;
                c.p[c.n] = r[i + 6] | (r[i + 7] << 8); c.t[c.n] = r[i + 10] / 1000.0;
                // a time that ran out (a reader open over 35.8 minutes; files saved before 2026-11-17): the report before + 1 ms
                if (r[i + 10] < 0) { c.t[c.n] = c.n > 0 ? c.t[c.n - 1] + 1.0 : 0.0; c.guessed++; }
                if (c.p[c.n] > 0) c.down++;
                c.n++;
            }
            if (c.n == 0 && c.tele > 0) c.why = StillNoiseOps.TELEMETRY;
            else if (hover && c.n < MIN_REPORTS) c.why = "Too few reports with the pen in range (" + F.Str(c.n) + "): slide the pen holder along the ruler with the pen in it, and check that the tablet driver is closed.";
            else if (hover && c.down * 2 > c.n) c.why = "Most reports have the tip down (" + F.Str(c.down) + " of " + F.Str(c.n) + "): in hover mode the pen sits in its holder above the tablet. To draw with the tip down, switch hover mode off (h).";
            else if (!hover && c.down < MIN_REPORTS) c.why = "Too few reports with the tip down (" + F.Str(c.down) + "): draw along the ruler with the pen pressed, and check that the tablet driver is closed.";
            return c;
        }

        // ---- one scored recording
        public class Result
        {
            public string label = "", why = null;
            public bool hover = false;
            public int passes = 0, plus = 0, minus = 0, breaks = 0, tipBreaks = 0, gapsDown = 0, pressure = 0, p10 = 0, p90 = 0, inAir = 0, airPress = 0, guessed = 0;
            public double speed = 0, onems = 0, secsDown = 0;
            public double[] rep = new double[2], slow = new double[2], cs = new double[2], sd = new double[2], scat = new double[2];
            public double fb = 0, fbMax = 0, fx = 0, fy = 0, ex = -1, ey = -1, rmsAll = 0, rmsLeft = 0;
            public int[] flicks = new int[2], steps = new int[2], bursts = new int[2];
            public double[] f = new double[PHASES], g = new double[PHASES];
            public bool scored = false, alongX = true;      // alongX: the line runs nearer the X axis than the Y axis
            // the headline: the coil-scale error, sd, the mean of both directions
            public double Distortion() { return (sd[0] + sd[1]) / 2; }
            public double JumpsPerSec(int[] k) { return secsDown > 0 ? (k[0] + k[1]) / secsDown : 0; }
        }
        public class Session
        {
            public string why = null;
            public double mx = 0, my = 0, ux = 1, uy = 0, lo = 0, hi = 0, minPass = PASS_MM;
            public int n = 0;
            public Result[] res = new Result[0];
            public double Angle() { return Math.Atan2(uy, ux) * 180.0 / Math.PI; }
            public int Best()
            {
                int b = -1;
                for (int i = 0; i < n; i++) if (res[i].scored && (b < 0 || res[i].Distortion() < res[b].Distortion())) b = i;
                return b;
            }
        }

        class Segs
        {
            public int[][] a = new int[16][]; public int n = 0;
            public void Add(int[] s) { if (n == a.Length) { int[][] g = new int[n * 2][]; for (int i = 0; i < n; i++) g[i] = a[i]; a = g; } a[n++] = s; }
            public int[][] Take() { int[][] o = new int[n][]; for (int i = 0; i < n; i++) o[i] = a[i]; return o; }
        }

        // a line through the points: centre and direction (orthogonal fit)
        static double[] LineFit(double[] x, double[] y, int n)
        {
            double mx = 0, my = 0;
            for (int i = 0; i < n; i++) { mx += x[i]; my += y[i]; }
            mx /= n; my /= n;
            double sxx = 0, syy = 0, sxy = 0;
            for (int i = 0; i < n; i++) { double a = x[i] - mx, b = y[i] - my; sxx += a * a; syy += b * b; sxy += a * b; }
            double th = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
            return new double[] { mx, my, Math.Cos(th), Math.Sin(th) };
        }
        // the line one recording was drawn on (its reports in use); null with too few
        public static double[] OwnLine(Rec r)
        {
            if (r == null || r.why != null) return null;
            int m = 0;
            for (int i = 0; i < r.n; i++) if (r.Use(i)) m++;
            if (m < MIN_REPORTS) return null;
            double[] x = new double[m], y = new double[m];
            m = 0;
            for (int i = 0; i < r.n; i++) if (r.Use(i)) { x[m] = r.x[i]; y[m] = r.y[i]; m++; }
            return LineFit(x, y, m);
        }
        // b on the line a: the angles within LINE_DEG, and b's centre within LINE_MM of a
        public static bool SameLine(double[] a, double[] b)
        {
            double dot = Math.Abs(a[2] * b[2] + a[3] * b[3]);
            if (dot > 1) dot = 1;
            if (Math.Acos(dot) * 180.0 / Math.PI > LINE_DEG) return false;
            return Math.Abs(-(b[0] - a[0]) * a[3] + (b[1] - a[1]) * a[2]) <= LINE_MM;
        }
        public static string LineText(double[] L)
        {
            double ang = Math.Atan2(L[3], L[2]) * 180.0 / Math.PI;
            if (ang > 90) ang -= 180; if (ang <= -90) ang += 180;
            return F.Fx(ang, 1) + " degrees through X " + F.Fx(L[0], 1) + " mm, Y " + F.Fx(L[1], 1) + " mm";
        }
        // the shortest pass kept on the line L: PASS_MM, or 0.7 of the line's length inside the drawing area if that is less
        public static double MinPass(double[] L)
        {
            double lo = -1e9, hi = 1e9;
            double[] c = new double[] { L[0], L[1] }, u = new double[] { L[2], L[3] }, size = new double[] { AREA_W, AREA_H };
            for (int k = 0; k < 2; k++)
            {
                if (Math.Abs(u[k]) < 1e-9) continue;
                double t1 = -c[k] / u[k], t2 = (size[k] - c[k]) / u[k];
                if (t1 > t2) { double s = t1; t1 = t2; t2 = s; }
                if (t1 > lo) lo = t1;
                if (t2 < hi) hi = t2;
            }
            double chord = hi - lo;
            return chord > 0 && PASS_FRAC * chord < PASS_MM ? PASS_FRAC * chord : PASS_MM;
        }
        static double[] Along(Rec r, double[] L)
        {
            double[] a = new double[r.n];
            for (int i = 0; i < r.n; i++) a[i] = (r.x[i] - L[0]) * L[2] + (r.y[i] - L[1]) * L[3];
            return a;
        }
        public static double[] RunMean(double[] v, int n, int h)
        {
            double[] c = new double[n + 1], o = new double[n];
            for (int i = 0; i < n; i++) c[i + 1] = c[i] + v[i];
            for (int i = 0; i < n; i++) { int lo = i - h < 0 ? 0 : i - h, hi = i + h + 1 > n ? n : i + h + 1; o[i] = (c[hi] - c[lo]) / (hi - lo); }
            return o;
        }
        static double Pct(double[] v, int n, double q)
        {
            if (n == 0) return 0;
            double[] s = new double[n]; Array.Copy(v, s, n); Array.Sort(s);
            int k = (int)(q * n); if (k > n - 1) k = n - 1;
            return s[k];
        }
        public static double Phase(double mm, double[] grid)
        {
            double k = (grid[0] - mm) / grid[1];
            return k - Math.Floor(k);
        }

        // the passes of one recording (report indices); counts its breaks into res
        static int[][] Passes(Rec r, double[] a, Result res, double minLen)
        {
            Segs groups = new Segs();
            Cli.IntBuf cur = new Cli.IntBuf();
            for (int i = 0; i < r.n; i++)
            {
                if (!r.Use(i)) continue;
                if (cur.n > 0)
                {
                    int j = cur.a[cur.n - 1];
                    double gap = r.t[i] - r.t[j], dist = Hyp(r.x[i] - r.x[j], r.y[i] - r.y[j]);
                    if (gap > JOIN_MS || dist > JOIN_MM) { groups.Add(cur.Take()); cur = new Cli.IntBuf(); }
                    else if (i - j > 1) { res.breaks++; res.tipBreaks++; }
                    else if (gap > 3) { res.breaks++; res.gapsDown++; }
                }
                cur.Add(i);
            }
            if (cur.n > 0) groups.Add(cur.Take());
            Segs kept = new Segs();
            for (int k = 0; k < groups.n; k++)
            {
                int[][] parts = SplitTurns(groups.a[k], a);
                for (int m = 0; m < parts.Length; m++)
                {
                    int[] s = parts[m];
                    if (Math.Abs(a[s[s.Length - 1]] - a[s[0]]) < minLen) continue;
                    double t0 = r.t[s[0]], t1 = r.t[s[s.Length - 1]];
                    Cli.IntBuf in1 = new Cli.IntBuf();
                    for (int q = 0; q < s.Length; q++) if (r.t[s[q]] - t0 >= TRIM_MS && t1 - r.t[s[q]] >= TRIM_MS) in1.Add(s[q]);
                    if (in1.n > MIN_REPORTS) kept.Add(in1.Take());
                }
            }
            return kept.Take();
        }
        static double Hyp(double a, double b) { return Math.Sqrt(a * a + b * b); }
        // split at every turn: where the pen comes back TURN_MM or more from the farthest point of its way, the pass ends
        // there (that point is its last report). ruler6.py split at the farthest point of the whole run either way, which
        // misses turns when the pen goes back and forth without lifting and comes back to where it started (the gate's
        // made-up passes found it).
        static int[][] SplitTurns(int[] s, double[] a)
        {
            Segs o = new Segs();
            int st = 0, hi = 0, lo = 0, ext = 0, dir = 0;
            for (int i = 1; i < s.Length; i++)
            {
                double v = a[s[i]];
                if (dir == 0)
                {
                    if (v > a[s[hi]]) hi = i;
                    if (v < a[s[lo]]) lo = i;
                    if (a[s[hi]] - a[s[st]] > TURN_MM && a[s[hi]] - v > TURN_MM) { dir = 1; ext = hi; }
                    else if (a[s[st]] - a[s[lo]] > TURN_MM && v - a[s[lo]] > TURN_MM) { dir = -1; ext = lo; }
                    else if (v - a[s[st]] > TURN_MM) { dir = 1; ext = hi; continue; }
                    else if (a[s[st]] - v > TURN_MM) { dir = -1; ext = lo; continue; }
                    else continue;
                }
                else if (dir * (v - a[s[ext]]) > 0) { ext = i; continue; }
                else if (dir * (a[s[ext]] - v) <= TURN_MM) continue;
                // a turn at ext: [st .. ext] is one pass, the next starts after it, going the other way
                o.Add(Piece(s, st, ext));
                st = ext + 1; dir = -dir;
                if (st > i) { st = i; }
                ext = st;
                for (int q = st; q <= i; q++) if (dir * (a[s[q]] - a[s[ext]]) > 0) ext = q;
                hi = st; lo = st;
            }
            if (st < s.Length) o.Add(Piece(s, st, s.Length - 1));
            return o.Take();
        }
        static int[] Piece(int[] s, int from, int to)
        {
            int[] p = new int[to - from + 1];
            Array.Copy(s, from, p, 0, to - from + 1);
            return p;
        }
        static int Dir(int[] s, double[] a) { return a[s[s.Length - 1]] > a[s[0]] ? 0 : 1; }      // 0 = forward (+), 1 = back (-)

        // ---- the session: every recording scored on one line and one stretch
        public static Session Score(Rec[] recs, int nr)
        {
            Session ss = new Session();
            ss.n = nr; ss.res = new Result[nr];
            bool anyHover = false;
            for (int k = 0; k < nr; k++)
            {
                ss.res[k] = new Result(); ss.res[k].label = recs[k].label; ss.res[k].why = recs[k].why; ss.res[k].hover = recs[k].hover; ss.res[k].guessed = recs[k].guessed;
                if (recs[k].hover) anyHover = true;
            }
            int tot = 0;
            for (int k = 0; k < nr; k++) if (recs[k].why == null) for (int i = 0; i < recs[k].n; i++) if (recs[k].Use(i)) tot++;
            if (tot < MIN_REPORTS) { ss.why = "No recording has enough reports " + (anyHover ? "with the pen in range." : "with the tip down."); return ss; }
            double[] ax = new double[tot], ay = new double[tot];
            int m = 0;
            for (int k = 0; k < nr; k++) if (recs[k].why == null) for (int i = 0; i < recs[k].n; i++) if (recs[k].Use(i)) { ax[m] = recs[k].x[i]; ay[m] = recs[k].y[i]; m++; }
            double[] L = LineFit(ax, ay, tot);
            ss.minPass = MinPass(L);
            int[][][] ps = new int[nr][][];
            int kept = 0;
            for (int k = 0; k < nr; k++)
            {
                if (recs[k].why != null) continue;
                ps[k] = Passes(recs[k], Along(recs[k], L), ss.res[k], ss.minPass);
                for (int j = 0; j < ps[k].Length; j++) kept += ps[k][j].Length;
            }
            if (kept == 0) { ss.why = "No pass of " + F.Fx(ss.minPass, 0) + " mm or more along one line: " + (anyHover ? "slide the pen holder" : "draw") + " the whole length of the ruler, back and forth."; return ss; }
            ax = new double[kept]; ay = new double[kept]; m = 0;
            for (int k = 0; k < nr; k++)
                if (ps[k] != null)
                    for (int j = 0; j < ps[k].Length; j++) for (int q = 0; q < ps[k][j].Length; q++) { ax[m] = recs[k].x[ps[k][j][q]]; ay[m] = recs[k].y[ps[k][j][q]]; m++; }
            L = LineFit(ax, ay, kept);
            ss.mx = L[0]; ss.my = L[1]; ss.ux = L[2]; ss.uy = L[3];
            double[][] a = new double[nr][], d = new double[nr][];
            int lob = int.MaxValue, hib = int.MinValue, scored = 0;
            for (int k = 0; k < nr; k++)
            {
                if (ps[k] == null) continue;
                Rec r = recs[k];
                a[k] = Along(r, L); d[k] = new double[r.n];
                for (int i = 0; i < r.n; i++) d[k][i] = -(r.x[i] - L[0]) * L[3] + (r.y[i] - L[1]) * L[2];
                Result res = ss.res[k];
                res.passes = ps[k].Length;
                for (int j = 0; j < ps[k].Length; j++) { if (Dir(ps[k][j], a[k]) == 0) res.plus++; else res.minus++; }
                if (res.plus < NEED || res.minus < NEED)
                {
                    res.why = "Too few passes: " + F.Str(res.plus) + " one way, " + F.Str(res.minus) + " the other (" + F.Str(NEED) + " each way needed, each "
                        + F.Fx(ss.minPass, 0) + " mm or longer along the line).";
                    continue;
                }
                scored++;
                for (int j = 0; j < ps[k].Length; j++)
                    for (int q = 0; q < ps[k][j].Length; q++)
                    {
                        int b = (int)Math.Floor(a[k][ps[k][j][q]] / BIN);
                        if (b < lob) lob = b;
                        if (b > hib) hib = b;
                    }
            }
            if (scored == 0) { ss.why = "No recording has " + F.Str(NEED) + " passes each way."; return ss; }
            int nb = hib - lob + 1;
            bool[] ok = new bool[nb];
            for (int b = 0; b < nb; b++) ok[b] = true;
            for (int k = 0; k < nr; k++)
            {
                if (ps[k] == null || ss.res[k].why != null) continue;
                int[][] cover = new int[2][]; cover[0] = new int[nb]; cover[1] = new int[nb];
                for (int j = 0; j < ps[k].Length; j++)
                {
                    int[] s = ps[k][j];
                    double lo = a[k][s[0]], hi = lo;
                    for (int q = 0; q < s.Length; q++) { double v = a[k][s[q]]; if (v < lo) lo = v; if (v > hi) hi = v; }
                    int b0 = (int)Math.Floor(lo / BIN) - lob, b1 = (int)Math.Floor(hi / BIN) - lob, di = Dir(s, a[k]);
                    for (int b = b0; b <= b1; b++) if (b >= 0 && b < nb) cover[di][b]++;
                }
                for (int b = 0; b < nb; b++) if (cover[0][b] < NEED || cover[1][b] < NEED) ok[b] = false;
            }
            int bs = 0, be = 0, rs = -1;
            for (int b = 0; b < nb; b++)
            {
                if (!ok[b]) { rs = -1; continue; }
                if (rs < 0) rs = b;
                if (b + 1 - rs > be - bs) { bs = rs; be = b + 1; }
            }
            ss.lo = (bs + lob) * BIN + EDGE_MM; ss.hi = (be + lob) * BIN - EDGE_MM;
            if (ss.hi - ss.lo < MIN_STRETCH)
            {
                ss.why = "The recordings share no stretch of the line " + F.Fx(MIN_STRETCH, 0) + " mm or longer with " + F.Str(NEED)
                    + " passes each way in every one. If the ruler moved, start a new session (n).";
                return ss;
            }
            for (int k = 0; k < nr; k++) if (ps[k] != null && ss.res[k].why == null) Metrics(recs[k], a[k], d[k], ps[k], ss, ss.res[k]);
            return ss;
        }

        static void Metrics(Rec r, double[] a, double[] d, int[][] ps, Session ss, Result res)
        {
            int blo = (int)Math.Floor(ss.lo / BIN), nb = (int)Math.Floor(ss.hi / BIN) - blo + 1;
            double[][] sum = new double[2][], sq = new double[2][]; int[][] cnt = new int[2][];
            for (int di = 0; di < 2; di++) { sum[di] = new double[nb]; sq[di] = new double[nb]; cnt[di] = new int[nb]; }
            double[] psum = new double[nb]; int[] pcnt = new int[nb];
            double[] sp = new double[ps.Length]; int nsp = 0;
            Cli.IntBuf pres = new Cli.IntBuf();
            int dts = 0, one = 0;
            for (int j = 0; j < ps.Length; j++)
            {
                int[] s = ps[j];
                int di = Dir(s, a);
                double[] dv = new double[s.Length];
                for (int q = 0; q < s.Length; q++) dv[q] = d[s[q]];
                double[] ds = RunMean(dv, s.Length, MEAN_H);
                int inn = 0; double off = 0;
                for (int q = 0; q < s.Length; q++) { double av = a[s[q]]; if (av >= ss.lo && av <= ss.hi) { inn++; off += ds[q]; } }
                if (inn < MIN_REPORTS) continue;
                off /= inn;
                for (int b = 0; b < nb; b++) { psum[b] = 0; pcnt[b] = 0; }
                for (int q = 0; q < s.Length; q++)
                {
                    double av = a[s[q]];
                    if (av < ss.lo || av > ss.hi) continue;
                    int b = (int)Math.Floor(av / BIN) - blo;
                    if (b < 0 || b >= nb) continue;
                    psum[b] += ds[q] - off; pcnt[b]++;
                }
                for (int b = 0; b < nb; b++)
                    if (pcnt[b] > 0) { double v = psum[b] / pcnt[b]; sum[di][b] += v; sq[di][b] += v * v; cnt[di][b]++; }
                double span = Math.Abs(a[s[s.Length - 1]] - a[s[0]]), secs = (r.t[s[s.Length - 1]] - r.t[s[0]]) / 1000.0;
                if (secs > 0) sp[nsp++] = span / secs;
                for (int q = 0; q < s.Length; q++) pres.Add(r.p[s[q]]);
                for (int q = 1; q < s.Length; q++) { dts++; if (r.t[s[q]] - r.t[s[q - 1]] < 1.5) one++; }
            }
            res.speed = Pct(sp, nsp, 0.5);
            double[] pd = new double[pres.n]; for (int i = 0; i < pres.n; i++) pd[i] = pres.a[i];
            res.pressure = (int)Pct(pd, pres.n, 0.5); res.p10 = (int)Pct(pd, pres.n, 0.1); res.p90 = (int)Pct(pd, pres.n, 0.9);
            res.inAir = pres.n; res.airPress = 0;
            for (int i = 0; i < pres.n; i++) if (pres.a[i] > 0) res.airPress++;
            res.onems = dts > 0 ? 100.0 * one / dts : 0;
            // the repeatable error per direction, its slow and coil-scale parts
            double[][] coil = new double[2][]; bool[][] has = new bool[2][];
            for (int di = 0; di < 2; di++)
            {
                coil[di] = new double[nb]; has[di] = new bool[nb];
                int nv = 0; for (int b = 0; b < nb; b++) if (cnt[di][b] >= NEED) nv++;
                if (nv < 2) { res.why = "Too few passes cover the stretch in one direction."; return; }
                double[] vals = new double[nv]; int[] at = new int[nv]; double q2 = 0; int k = 0;
                for (int b = 0; b < nb; b++)
                {
                    if (cnt[di][b] < NEED) continue;
                    double mean = sum[di][b] / cnt[di][b];
                    vals[k] = mean; at[k] = b; k++;
                    double var = (sq[di][b] - cnt[di][b] * mean * mean) / (cnt[di][b] - 1);
                    q2 += var > 0 ? var : 0;
                }
                double[] slow = RunMean(vals, nv, (int)SLOW_BINS);
                double fmin = 1e9, fmax = -1e9, vmin = 1e9, vmax = -1e9, smin = 1e9, smax = -1e9, f2 = 0;
                for (int i = 0; i < nv; i++)
                {
                    double fast = vals[i] - slow[i];
                    coil[di][at[i]] = fast; has[di][at[i]] = true;
                    if (fast < fmin) fmin = fast; if (fast > fmax) fmax = fast;
                    if (vals[i] < vmin) vmin = vals[i]; if (vals[i] > vmax) vmax = vals[i];
                    if (slow[i] < smin) smin = slow[i]; if (slow[i] > smax) smax = slow[i];
                    f2 += fast * fast;
                }
                res.rep[di] = vmax - vmin; res.slow[di] = smax - smin; res.cs[di] = fmax - fmin;
                res.sd[di] = Math.Sqrt(f2 / nv); res.scat[di] = Math.Sqrt(q2 / nv);
            }
            // forward minus back, and the coil-locked fit on the mean of both directions
            int nboth = 0; double fb2 = 0, fbm = 0;
            for (int b = 0; b < nb; b++) if (has[0][b] && has[1][b]) { double df = coil[0][b] - coil[1][b]; nboth++; fb2 += df * df; if (Math.Abs(df) > fbm) fbm = Math.Abs(df); }
            if (nboth == 0) { res.why = "The two directions cover no bin of the stretch together."; return; }
            res.fb = Math.Sqrt(fb2 / nboth); res.fbMax = fbm;
            double[] ev = new double[nboth]; int[] px = new int[nboth], py = new int[nboth];
            int e = 0;
            for (int b = 0; b < nb; b++)
            {
                if (!(has[0][b] && has[1][b])) continue;
                ev[e] = (coil[0][b] + coil[1][b]) / 2;
                double av = (b + blo + 0.5) * BIN, xx = ss.mx + av * ss.ux, yy = ss.my + av * ss.uy;
                px[e] = ((int)(Phase(xx, XC) * PHASES)) % PHASES; py[e] = ((int)(Phase(yy, YC) * PHASES)) % PHASES;
                e++;
            }
            // each axis's error needs a line that crosses both axes' coils (header): else "-", and only the phase that
            // changes along the line is fitted (on a vertical line X moves a millimetre or two, and a fit to that took up
            // 0.07-0.09 mm of other error on Anargon's D2)
            bool both = Math.Abs(ss.ux) >= AXIS_MIN && Math.Abs(ss.uy) >= AXIS_MIN;
            res.alongX = Math.Abs(ss.ux) >= Math.Abs(ss.uy);
            double[] f = res.f, g = res.g;
            for (int it = 0; it < 20; it++)
            {
                if (both || res.alongX) Fold(f, ev, px, g, py, nboth);
                if (both || !res.alongX) Fold(g, ev, py, f, px, nboth);
            }
            double t2 = 0, r2 = 0;
            for (int i = 0; i < nboth; i++) { t2 += ev[i] * ev[i]; double rr = ev[i] - f[px[i]] - g[py[i]]; r2 += rr * rr; }
            res.rmsAll = Math.Sqrt(t2 / nboth); res.rmsLeft = Math.Sqrt(r2 / nboth);
            res.fx = Spread(f); res.fy = Spread(g);
            res.ex = both ? res.fx / Math.Abs(ss.uy) : -1;
            res.ey = both ? res.fy / Math.Abs(ss.ux) : -1;
            // jumps per axis, raw reports, inside the stretch
            for (int j = 0; j < ps.Length; j++)
            {
                res.secsDown += (r.t[ps[j][ps[j].Length - 1]] - r.t[ps[j][0]]) / 1000.0;
                Jumps(r.x, ps[j], a, r, ss, res, 0);
                Jumps(r.y, ps[j], a, r, ss, res, 1);
            }
            res.scored = true;
        }
        static void Fold(double[] arr, double[] ev, int[] idx, double[] other, int[] oidx, int n)
        {
            double[] acc = new double[PHASES]; int[] c = new int[PHASES];
            for (int i = 0; i < n; i++) { acc[idx[i]] += ev[i] - other[oidx[i]]; c[idx[i]]++; }
            for (int i = 0; i < PHASES; i++) arr[i] = c[i] > 0 ? acc[i] / c[i] : 0.0;
        }
        static double Spread(double[] v)
        {
            double lo = v[0], hi = v[0];
            for (int i = 1; i < v.Length; i++) { if (v[i] < lo) lo = v[i]; if (v[i] > hi) hi = v[i]; }
            return hi - lo;
        }
        static void Jumps(double[] V, int[] s, double[] a, Rec r, Session ss, Result res, int q)
        {
            int k = 11;
            while (k < s.Length - 16)
            {
                int i = s[k];
                double v = (V[s[k - 1]] - V[s[k - 11]]) / 10.0, base0 = V[s[k - 1]];
                double off = V[i] - (base0 + v);
                bool inside = a[i] >= ss.lo && a[i] <= ss.hi && r.x[i] > 3 && r.y[i] > 3;
                if (Math.Abs(off) > JUMP_MM && inside)
                {
                    bool back = false;
                    for (int m = 1; m <= 3; m++) if (Math.Abs(V[s[k + m]] - (base0 + v * (m + 1))) < BACK_MM) back = true;
                    double later = 0;
                    for (int m = k + 5; m < k + 16; m++) later += V[s[m]] - (base0 + v * (m - (k - 1)));
                    later /= 11.0;
                    if (back) res.flicks[q]++;
                    else if (Math.Abs(later) > STAY_MM && later * off > 0) res.steps[q]++;
                    else res.bursts[q]++;
                    k += 4;
                }
                else k++;
            }
        }

        // ---- text
        public static StrList Table(Session ss)
        {
            StrList l = new StrList();
            if (ss.why != null) l.Add(ss.why);
            else
            {
                l.Add("Line at " + F.Fx(ss.Angle(), 1) + " degrees through X " + F.Fx(ss.mx, 1) + " mm, Y " + F.Fx(ss.my, 1) + " mm; compared over "
                    + F.Fx(ss.hi - ss.lo, 0) + " mm of it (the stretch every recording covers with " + F.Str(NEED) + " or more passes each way).");
                l.Add("");
            }
            l.Add("        " + Txt.PadR("", 34) + "distortion       coil-locked     fwd-back  steps  flicks  passes");
            l.Add("        " + Txt.PadR("", 34) + "sd (+ / -)       X       Y       rms       per s  per s   + / -");
            int best = ss.Best();
            for (int i = 0; i < ss.n; i++)
            {
                Result r = ss.res[i];
                string lb = r.label;
                if (P.Len(lb) > 32) lb = P.Sub(lb, 0, 29) + "...";
                string row = "  " + Txt.PadL(F.Str(i + 1), 3) + ".  " + Txt.PadR(lb, 34);
                if (!r.scored) { l.Add(row + (r.why != null ? r.why : "not scored")); continue; }
                row += Txt.PadR(F.Fx(r.sd[0], 3) + " / " + F.Fx(r.sd[1], 3), 17) + Txt.PadR(Ax(r.ex), 8) + Txt.PadR(Ax(r.ey), 8)
                    + Txt.PadR(F.Fx(r.fb, 3), 10) + Txt.PadR(F.Fx(r.JumpsPerSec(r.steps), 1), 7) + Txt.PadR(F.Fx(r.JumpsPerSec(r.flicks), 1), 8)
                    + F.Str(r.plus) + " / " + F.Str(r.minus) + (i == best && ss.n > 1 ? "   <- lowest" : "");
                l.Add(row);
            }
            if (ss.why == null)
            {
                l.Add("");
                l.Add("  distortion: the error that repeats at the same place along the line at the coils' scale (the coils and the");
                l.Add("  position filters; the ruler's own bend is taken out), sd in mm. coil-locked: the part that follows each axis's");
                l.Add("  coils, as error on that axis, highest to lowest (- = a line along an axis cannot show it: ruler.txt has the");
                l.Add("  error across that line that follows the coils along it).");
            }
            return l;
        }
        static string Ax(double v) { return v < 0 ? "-" : F.Fx(v, 3); }

        // every number of one recording (for ruler.txt)
        public static StrList Detail(Result r, int k)
        {
            StrList l = new StrList();
            l.Add("Recording " + F.Str(k) + ": " + r.label);
            if (!r.scored) { l.Add("  " + (r.why != null ? r.why : "not scored")); return l; }
            string pr = r.hover
                ? "hover, pressure in the air in " + F.Str(r.airPress) + " of " + F.Str(r.inAir) + " reports"
                : "pressure median " + F.Str(r.pressure) + " (10-90 % " + F.Str(r.p10) + "-" + F.Str(r.p90) + ")";
            l.Add("  passes " + F.Str(r.passes) + " (" + F.Str(r.plus) + " +, " + F.Str(r.minus) + " -), speed median " + F.Fx(r.speed, 0) + " mm/s, " + pr
                + ", reports 1 ms apart " + F.Fx(r.onems, 1) + " %");
            if (r.guessed > 0)
                l.Add("  times guessed for " + F.Str(r.guessed) + " reports (the report before + 1 ms): the reader's clock had run out (saved before 2026-11-17)");
            for (int di = 0; di < 2; di++)
                l.Add("  direction " + (di == 0 ? "+" : "-") + ": repeatable p-p " + F.Fx(r.rep[di], 3) + " mm; slow p-p " + F.Fx(r.slow[di], 3) + "; coil-scale p-p "
                    + F.Fx(r.cs[di], 3) + ", sd " + F.Fx(r.sd[di], 3) + "; pass scatter sd " + F.Fx(r.scat[di], 3));
            l.Add("  forward minus back (coil-scale): max " + F.Fx(r.fbMax, 3) + " mm, rms " + F.Fx(r.fb, 3));
            l.Add("  coil-locked: X phase p-p " + F.Fx(r.fx, 3) + " mm sideways (X error " + Ax(r.ex) + "), Y phase p-p " + F.Fx(r.fy, 3) + " (Y error " + Ax(r.ey)
                + "); rms " + F.Fx(r.rmsAll, 3) + " -> " + F.Fx(r.rmsLeft, 3) + " after both");
            if (r.ex < 0 && r.ey < 0)
                l.Add("     along an axis: " + (r.alongX ? "the Y error that follows the X coils, p-p " + F.Fx(r.fx, 3) : "the X error that follows the Y coils, p-p " + F.Fx(r.fy, 3))
                    + " mm; neither axis's own coil error shows on this line");
            l.Add("     X phase 0..15/16: " + Row(r.f));
            l.Add("     Y phase 0..15/16: " + Row(r.g));
            l.Add("  breaks inside a pass: " + F.Str(r.breaks) + " (tip off " + F.Str(r.tipBreaks) + ", report gaps over 3 ms with the " + (r.hover ? "pen in range " : "tip down ")
                + F.Str(r.gapsDown) + ")");
            for (int q = 0; q < 2; q++)
                l.Add("  " + (q == 0 ? "X" : "Y") + ": flicks " + F.Str(r.flicks[q]) + ", steps " + F.Str(r.steps[q]) + ", bursts " + F.Str(r.bursts[q])
                    + " in " + F.Fx(r.secsDown, 1) + " s of passes");
            return l;
        }
        static string Row(double[] v)
        {
            string s = "";
            for (int i = 0; i < v.Length; i++) s += (i > 0 ? " " : "") + (v[i] >= 0 ? "+" : "") + F.Fx(v[i], 2);
            return s;
        }

        // the drawing area as a box: the target line (.), and the trace of a recording from fromMs on (# down, o up)
        public static int Target = 0;           // the target line the screen shows (TARGETS; l N)
        public static StrList Map(Rec r, double fromMs)
        {
            char[][] g = new char[MAP_H][];
            for (int i = 0; i < MAP_H; i++) { g[i] = new char[MAP_W]; for (int j = 0; j < MAP_W; j++) g[i][j] = ' '; }
            double[] T = TARGETS[Target];
            for (int i = 0; i <= 400; i++)
            {
                double f = i / 400.0;
                Put(g, T[0] + f * (T[2] - T[0]), T[1] + f * (T[3] - T[1]), '.');
            }
            if (r != null) for (int i = 0; i < r.n; i++) if (r.t[i] >= fromMs) Put(g, r.x[i], r.y[i], r.p[i] > 0 ? '#' : 'o');
            StrList l = new StrList();
            string edge = "+" + new string('-', MAP_W) + "+";
            l.Add(edge);
            for (int i = 0; i < MAP_H; i++) l.Add("|" + new string(g[i]) + "|");
            l.Add(edge);
            return l;
        }
        static void Put(char[][] g, double x, double y, char c)
        {
            int col = (int)(x / AREA_W * MAP_W), row = (int)(y / AREA_H * MAP_H);
            if (col < 0) col = 0; if (col >= MAP_W) col = MAP_W - 1; if (row < 0) row = 0; if (row >= MAP_H) row = MAP_H - 1;
            if (c == '.' && g[row][col] != ' ') return;
            if (c == 'o' && g[row][col] == '#') return;
            g[row][col] = c;
        }

        public const string OTHER_REPORTS = "pen LOST: the tablet sends another driver's reports.";
        public static readonly string[] OTHER_FIX = new string[] {
            "Close that driver (or the app or game with OpenTabletDriver built in),",
            "then unplug and replug the tablet." };
        public const int TICK_MS = 200, TICKS = 1000 / TICK_MS;      // the live view: redrawn every 200 ms (Anargon)

        // one recording: a short settle, then SECS of reports. live = the box with the last 3 s and the pen's state,
        // redrawn in place every 200 ms (Anargon, 2026-11-17; the screen is drawn once, then only these lines are
        // overwritten, as the ALPI training's live table, so it does not flicker). Enter stops it (Anargon: a way to
        // cancel a recording; P.KeyHit, as the ALPI training's "stop early"): null = stopped, nothing to keep
        public static int[] Record(PenReader rd, int secs, bool live, string title, string label)
        {
            rd.Collect(0.3);
            Cli.IntBuf all = new Cli.IntBuf();
            int nt = secs * TICKS;
            int[][] ring = new int[TRACE_SECS * TICKS][];
            int top = -1, qe = -1;
            if (live)
            {
                Screen.Step(title);
                P.Line("    Recording: " + label);
                P.Line("");
                top = P.CursorTop();
                qe = P.QuickEditOff();            // a click in the window must not pause the output (and the reading)
            }
            // live: the reports are read on their own thread (Collector), so drawing the screen loses none of them
            Collector col = null;
            if (live) { col = new Collector(rd, nt); P.StartBackground(col); }
            try
            {
                int k = 0;
                while (k < nt)
                {
                    int[] r;
                    if (!live) r = rd.Collect(TICK_MS / 1000.0);
                    else
                    {
                        // take every 200 ms piece read since the last drawing, then draw once with the newest
                        r = col.Next();
                        if (r == null)
                        {
                            if (P.KeyHit()) return null;
                            if (!col.Finished()) { P.Sleep(0.01); continue; }
                            r = col.Next();                 // the last piece may have come in just before it finished
                            if (r == null) break;
                        }
                        int[] more = col.Next();
                        while (more != null)
                        {
                            for (int i = 0; i < r.Length; i++) all.Add(r[i]);
                            ring[k % ring.Length] = r;
                            k++;
                            r = more;
                            more = col.Next();
                        }
                    }
                    for (int i = 0; i < r.Length; i++) all.Add(r[i]);
                    ring[k % ring.Length] = r;
                    if (live && P.KeyHit()) return null;
                    bool second = (k + 1) % TICKS == 0;
                    k++;
                    if (!live && !second) continue;
                    string now = Live(r);
                    // no pen report, but reports with another ID: a driver switched the tablet to its own reports
                    bool other = P.Eq(now, "pen LOST") && P.IndexOf(rd.Why(), "reports arrive with ID") >= 0;
                    if (other) now = OTHER_REPORTS;
                    int left = (nt - k + TICKS - 1) / TICKS;
                    if (!live) { P.Line("        " + now + (other ? " " + OTHER_FIX[0] + " " + OTHER_FIX[1] : "") + "   " + F.Str(left) + " s left"); continue; }
                    Cli.IntBuf tail = new Cli.IntBuf();
                    for (int j = 0; j < ring.Length; j++) { int[] c = ring[(k + j) % ring.Length]; if (c != null) for (int i = 0; i < c.Length; i++) tail.Add(c[i]); }
                    if (top >= 0) P.SetCursor(0, top);
                    StrList m = Map(FromReports(tail.Take(), label), -1e18);
                    for (int i = 0; i < m.Count(); i++) P.Line("    " + m.Get(i));
                    P.Line("    " + TRACE_TEXT);
                    P.Line("");
                    P.Line(Txt.PadR("    " + now, 79));
                    P.Line(Txt.PadR("    " + F.Str(left) + " s left   (Enter = stop, nothing is saved)", 79));
                    P.Line(Txt.PadR(other ? "    " + OTHER_FIX[0] : "", 79));
                    P.Line(Txt.PadR(other ? "    " + OTHER_FIX[1] : "", 79));
                }
            }
            finally
            {
                if (col != null) col.StopAndWait();
                if (live) P.QuickEditRestore(qe);
            }
            return all.Take();
        }

        // R45 (2026-11-17): the live view's reader thread. P.HidCollect starts its buffer afresh at each call, so the
        // time spent drawing between two calls would lose that time's reports (and a gap in the reports reads as a
        // jump); here the calls follow each other directly and the screen takes the pieces when it is ready
        public sealed class Collector : Job
        {
            readonly PenReader rd;
            readonly object gate = new object();
            readonly int[][] pieces;
            int made = 0, taken = 0;
            bool stop = false, finished = false;
            public Collector(PenReader r, int n) { rd = r; pieces = new int[n][]; }
            public override void Run()
            {
                try
                {
                    for (int k = 0; k < pieces.Length; k++)
                    {
                        lock (gate) { if (stop) break; }
                        int[] c = rd.Collect(TICK_MS / 1000.0);
                        lock (gate) { pieces[k] = c; made = k + 1; }
                    }
                }
                catch (Exception) { }      // the reader failed: the screen shows the pen lost and the recording ends
                finally { lock (gate) { finished = true; } }
            }
            public int[] Next() { lock (gate) { return taken < made ? pieces[taken++] : null; } }
            public bool Finished() { lock (gate) { return finished; } }
            // at most one piece (200 ms) is still being read
            public void StopAndWait()
            {
                lock (gate) { stop = true; }
                for (int t = 0; t < 200 && !Finished(); t++) P.Sleep(0.01);
            }
        }
        static string Live(int[] r)
        {
            int n = 0, down = 0; long x = 0, y = 0;
            for (int i = 0; i + 10 < r.Length; i += 11)
            {
                if (r[i] != 7 || r[i + 1] == 0xC0 || !Lab.InRange(r[i + 1])) continue;
                x = r[i + 2] | (r[i + 3] << 8); y = r[i + 4] | (r[i + 5] << 8); n++;
                if ((r[i + 6] | (r[i + 7] << 8)) > 0) down++;
            }
            if (n == 0) return "pen LOST";
            return "pen " + (down > 0 ? "down" : "up") + " at X " + F.Fx(x / UNITS_MM, 2) + " mm, Y " + F.Fx(y / UNITS_MM, 2) + " mm (" + F.Str(n) + " reports)";
        }

        // ---- files: export/measurements/ruler: STAMP-recK.csv (every report) and the session's table in ruler.txt
        public static string Folder() { return P.PathJoin(P.PathJoin(Folders.Of(Folders.EXPORT), "measurements"), "ruler"); }
        public const string CSV_HEAD = "t_us,status,x,y,pressure,tilt_x,tilt_y";
        public static string Save(string dir, string label, int[] r, int k, string stamp)
        {
            P.MakeDirs(dir);
            string name = stamp + "-rec" + F.Str(k) + ".csv";
            StringBuilder sb = new StringBuilder("# Anargon's Ruler Line Test: " + label + "\r\n" + CSV_HEAD + "\r\n");
            for (int i = 0; i + 10 < r.Length; i += 11)
            {
                if (r[i] != 7) continue;
                sb.Append(F.Str(r[i + 10])).Append(',').Append(F.Str(r[i + 1])).Append(',').Append(F.Str(r[i + 2] | (r[i + 3] << 8))).Append(',')
                  .Append(F.Str(r[i + 4] | (r[i + 5] << 8))).Append(',').Append(F.Str(r[i + 6] | (r[i + 7] << 8))).Append(',')
                  .Append(F.Str(r[i + 8])).Append(',').Append(F.Str(r[i + 9])).Append("\r\n");
            }
            P.AtomicWriteAllBytes(P.PathJoin(dir, name), Build.Encode(sb.ToString()));
            return P.PathJoin(dir, name);
        }
        // a saved recording back (the same records as when it was made); null if it is not one
        public static Rec Load(string path)
        {
            string text = Encoding.ASCII.GetString(P.ReadAllBytes(path));
            string[] lines = P.Split(P.Replace(text, "\r", ""), '\n');
            string label = "";
            Cli.IntBuf b = new Cli.IntBuf();
            bool head = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string s = lines[i];
                if (P.Len(s) == 0) continue;
                if (P.StartsWith(s, "#")) { int c = P.IndexOf(s, ": "); if (c >= 0) label = P.Sub(s, c + 2, P.Len(s) - c - 2); continue; }
                if (P.Eq(s, CSV_HEAD)) { head = true; continue; }
                string[] f = P.Split(s, ',');
                if (!head || f.Length != 7) return null;
                int[] v = new int[7];
                for (int j = 0; j < 7; j++) v[j] = (int)P.ParseDouble(f[j]);
                b.Add(7); b.Add(v[1]); b.Add(v[2] & 255); b.Add(v[2] >> 8); b.Add(v[3] & 255); b.Add(v[3] >> 8); b.Add(v[4] & 255); b.Add(v[4] >> 8);
                b.Add(v[5]); b.Add(v[6]); b.Add(v[0]);
            }
            if (!head) return null;
            Rec r = FromReports(b.Take(), label);
            r.file = path;
            return r;
        }
        public static void Report(string dir, Session ss, Rec[] recs, string stamp)
        {
            P.MakeDirs(dir);
            StringBuilder sb = new StringBuilder();
            sb.Append("# " + stamp + ", after recording " + F.Str(ss.n) + "\r\n");
            StrList t = Table(ss);
            for (int i = 0; i < t.Count(); i++) sb.Append(t.Get(i)).Append("\r\n");
            sb.Append("\r\n");
            for (int k = 0; k < ss.n; k++)
            {
                StrList d = Detail(ss.res[k], k + 1);
                for (int i = 0; i < d.Count(); i++) sb.Append(d.Get(i)).Append("\r\n");
                if (recs[k].file != null) sb.Append("  file: " + recs[k].file + "\r\n");
            }
            sb.Append("\r\n");
            P.AppendText(P.PathJoin(dir, "ruler.txt"), sb.ToString());
        }
    }

    // the screen: shows what RulerOps returns and calls it
    public static class RulerTest
    {
        public const string TITLE = "Anargon's Ruler Line Test", SUB = "For testing purposes only";

        public static int Run()
        {
            LabPort port = ArrangerOps.Connect();
            PenReader rd = new PenReader(port);
            RulerOps.Rec[] recs = new RulerOps.Rec[RulerOps.KEEP];
            int nr = 0, made = 0, secs = RulerOps.DEFAULT_SECS;
            string note = null, stamp = P.Now("stamp");
            bool hover = false;
            StrList last = null;
            double[] line = null;       // the line of the session's first recording (a recording on another line starts a new session)
            try
            {
                while (true)
                {
                    Screen.Step(TITLE + " (" + SUB + ")");
                    Lab.Status st = ArrangerOps.Read(port);
                    Screen.Para(RulerOps.INTRO, 4);
                    P.Line("");
                    Screen.Para(hover ? RulerOps.HOW_HOVER : RulerOps.HOW, 4);
                    P.Line("");
                    StrList m = RulerOps.Map(null, 0);
                    for (int i = 0; i < m.Count(); i++) P.Line("    " + m.Get(i));
                    Screen.Para(". = " + RulerOps.TargetText(RulerOps.Target), 4);
                    P.Line("");
                    P.Line("    Mode:          " + (hover ? "hover (the pen in its holder, the holder slid along the ruler)" : "drawing with the tip down"));
                    if (st == null) P.Line("");
                    if (st != null)
                    {
                        P.Line("    Filters now:   " + StillNoiseOps.Label(st));
                        if (ArrangerOps.CursorInBuild(st)) P.Line("    Cursor:        " + (ArrangerOps.CursorOff(st) ? ArrangerOps.CURSOR_OFF_TEXT : "on (add c to the order to turn it off)"));
                        if (StillNoiseOps.Waiting(st)) P.Line("    Waiting:       " + ArrangerOps.OrderText(st.req) + ArrangerOps.WAITING);
                        P.Line("");
                    }
                    if (last != null)
                    {
                        for (int i = 0; i < last.Count(); i++) P.Line("    " + last.Get(i));
                        P.Line("");
                        last = null;
                    }
                    if (note != null) { Screen.Para(note, 4); P.Line(""); note = null; }
                    P.Line("    Enter = record for " + F.Str(secs) + " s (recording " + F.Str(nr + 1) + " of this session)");
                    P.Line("    t N   = record for N seconds from now on (" + F.Str(RulerOps.MIN_SECS) + " to " + F.Str(RulerOps.MAX_SECS) + ")");
                    if (st != null) P.Line("    o ... = set the filter order first, like o 1 2 6 10 (the Filter Arranger's numbers)");
                    if (ArrangerOps.CursorInBuild(st)) P.Line("            c = cursor off: o 1 3 6 10 c (o c keeps the order; no c = on)");
                    P.Line("    h     = " + (hover ? "hover mode off: draw with the tip down again" : "hover mode: the pen in a holder, the holder slid along the ruler") + " (starts a new session)");
                    P.Line("    l N   = target line: 1 diagonal, 2 other diagonal, 3 across, 4 up (a new session)");
                    P.Line("    n     = new session (the ruler moved: forget this session's recordings)");
                    P.Line("    q     = back");
                    P.Line("");
                    string c = P.Trim(Screen.Ask("    > "));
                    string lc = P.Lower(c);
                    if (P.Eq(lc, "q")) return 0;
                    if (P.Eq(lc, "n")) { nr = 0; line = null; stamp = P.Now("stamp"); note = "New session: the next recording starts it."; continue; }
                    if (P.StartsWith(lc, "l ") || P.Eq(lc, "l"))
                    {
                        int k = Num(P.Trim(P.Sub(c, 1, P.Len(c) - 1)));
                        if (k < 1 || k > RulerOps.TARGETS.Length) { note = "Type l and a number from 1 to " + F.Str(RulerOps.TARGETS.Length) + ", like l 2."; continue; }
                        RulerOps.Target = k - 1; nr = 0; line = null; stamp = P.Now("stamp");
                        note = "Target line " + F.Str(k) + ", " + RulerOps.TARGET_NAMES[k - 1] + ", new session: lay the ruler along the dots.";
                        continue;
                    }
                    if (P.Eq(lc, "h"))
                    {
                        hover = !hover; nr = 0; line = null; stamp = P.Now("stamp");
                        note = hover ? "Hover mode on, new session. Put the pen in its holder and slide the holder along the ruler, not the pen; keep the "
                                + "ruler out from under the pen tip."
                            : "Hover mode off, new session: draw along the ruler with the tip down.";
                        continue;
                    }
                    if (P.StartsWith(lc, "t ") || P.Eq(lc, "t"))
                    {
                        int n = Num(P.Trim(P.Sub(c, 1, P.Len(c) - 1)));
                        if (n < RulerOps.MIN_SECS || n > RulerOps.MAX_SECS) note = "Type t and a number of seconds from " + F.Str(RulerOps.MIN_SECS) + " to " + F.Str(RulerOps.MAX_SECS) + ", like t 90.";
                        else { secs = n; note = "Each recording now takes " + F.Str(secs) + " s."; }
                        continue;
                    }
                    if (P.StartsWith(lc, "o ") || P.Eq(lc, "o"))
                    {
                        if (st == null) { note = StillNoiseOps.NO_LAB; continue; }
                        string arg = P.Trim(P.Sub(c, 1, P.Len(c) - 1));
                        ArrangerOps.Result res = ArrangerOps.SetOrder(port, st, arg);
                        note = res.message;
                        // c alone: only the cursor (at once); an order: it starts at the next pen loss, the cursor at once
                        bool onlyC = ArrangerOps.ParseOrder(arg, st.avail).keep;
                        Lab.Status now = res.ok ? ArrangerOps.Read(port) : null;
                        string cur = now != null && ArrangerOps.CursorInBuild(now) ? (ArrangerOps.CursorOff(now) ? " The cursor is off now." : " The cursor is on.") : "";
                        if (res.ok && onlyC) note = "Cursor off: the pen moves no cursor and clicks nothing now. An order without c turns it back on.";
                        else if (res.ok) note = (note != null ? note + " " : "") + "Lift the pen out of range and put it back: the new order starts then." + cur;
                        continue;
                    }
                    if (P.Len(c) > 0) { note = "Press Enter to record, or type t N, " + (st != null ? "o and an order, " : "") + "h, l N, n or q."; continue; }
                    if (StillNoiseOps.Waiting(st)) { note = RulerOps.WAIT_ORDER; continue; }
                    if (nr == recs.Length) { note = "This session holds " + F.Str(RulerOps.KEEP) + " recordings: start a new one (n)."; continue; }
                    string err = rd.Open();
                    if (P.Len(err) > 0) { note = "The tablet could not be opened: " + err; continue; }
                    string label = StillNoiseOps.Label(st) + (hover ? RulerOps.HOVER_TAG : "");
                    P.Line("");
                    P.Line("    Recording for " + F.Str(secs) + " s: " + (hover ? "slide the pen holder" : "draw") + " along the ruler, back and forth, about 5 times each way.");
                    int[] raw = RulerOps.Record(rd, secs, P.StdinIsTty(), TITLE + " (" + SUB + ")", label);
                    if (raw == null) { note = "Recording stopped (Enter): nothing was saved."; continue; }
                    made++;
                    RulerOps.Rec r = RulerOps.FromReports(raw, label);
                    if (r.n == 0 && r.tele == 0) { string pr = StillNoiseOps.Problem(rd.Why()); if (pr != null) r.why = pr; }
                    last = new StrList();
                    if (r.why != null) { last.Add("Recording " + F.Str(made) + ": " + label); last.Add("  " + r.why); continue; }
                    // another line than the session's: a session of its own (Anargon's D1 and D2 were scored as one)
                    double[] own = RulerOps.OwnLine(r);
                    if (nr > 0 && line != null && own != null && !RulerOps.SameLine(line, own))
                    {
                        nr = 0; stamp = P.Now("stamp");
                        last.Add("This recording is on another line (" + RulerOps.LineText(own) + ") than the session's (" + RulerOps.LineText(line) + "):");
                        last.Add("it starts a new session.");
                        last.Add("");
                    }
                    if (nr == 0) line = own;
                    r.file = RulerOps.Save(RulerOps.Folder(), label, raw, made, stamp);
                    recs[nr++] = r;
                    RulerOps.Session ss = RulerOps.Score(recs, nr);
                    RulerOps.Report(RulerOps.Folder(), ss, recs, stamp);
                    last.Add("Session (" + F.Str(nr) + " recording" + (nr == 1 ? "" : "s") + ", mm):");
                    StrList t = RulerOps.Table(ss);
                    for (int i = 0; i < t.Count(); i++) last.Add(t.Get(i));
                    last.Add("");
                    last.Add("Saved: " + r.file + " (every number: ruler.txt next to it)");
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
