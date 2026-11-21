// atf_twin.cs - the Deco 640 tablet as a board on the GD32 emulator (gd32emu.cs): pen model, coil hooks, and the
// digital-twin checks, measurements and scenarios the patcher runs.
using System;
using System.Text;
using Gd32Emu;

namespace AnargonsTabletTools
{
    // ======================================================================================== pen
    public class Pen
    {
        // R43: a coil is described only by its centre and its effective range (the distance from its centre, in coil
        // pitches, where its response falls to half of the response at the centre): Anargon's coils may be
        // multi-layered and not circular. The response is the one measured on Anargon's tablet (MultiCoil.RESP),
        // stretched to the effective range: 1.34 pitches at touch (R43), wider with height. Until R42 the twin used a
        // bell curve (half response at 1.47 pitches). RANGE_MM sets how the signal and the effective range change with
        // height (the old model's numbers, kept). The twin's line sums have no floor; the tablet's do (about 72 per
        // conversion, R43).
        public const double GAP_MM = 1.0, COIL_MM = 4.0, RANGE_MM = 7.5, RANGE_TOUCH = 1.34, TOUCH_STRENGTH = 30000.0;
        public double x = 16.3, y = 9.6, f0 = 505000.0, Q = 120.0, strength = 5200.0, range = RANGE_TOUCH, noise = 6.0;
        public bool present = true, hasHeight = false, resonator = false, integrate = false;
        public const double INT_GAIN = 50000.0;      // integrating receiver: 1 / 20 us (FAST's 10-sample window ~ the old scale)
        public double height = 0.0;
        public Rng rng;
        public double re = 0.0, im = 0.0, drive = 0.0;
        public long tLast = 0;
        public double gStrength, gRange;

        public Pen(long seed) { rng = new Rng(seed); }

        public void Geometry()
        {
            double d = height + GAP_MM + COIL_MM;
            double dt = GAP_MM + COIL_MM;
            double h2 = RANGE_MM * RANGE_MM;
            double fall = (h2 + dt * dt) / (h2 + d * d);
            gStrength = TOUCH_STRENGTH * fall * fall;
            gRange = RANGE_TOUCH * P.Sqrt((h2 + d * d) / (h2 + dt * dt));
        }

        // the response of coil `coil` to the pen, for effective range rg (pitches)
        public double G(int axis, int coil, double rg)
        {
            double p = axis == 0 ? x : y;
            double d = coil - p;
            if (d < 0) d = -d;
            return MultiCoil.Response(d * MultiCoil.HALF / rg);
        }

        // sets gStrength, gRange and returns the transmit coupling
        public double Coupling(Machine mc)
        {
            if (!hasHeight) { gStrength = strength; gRange = range; return G(TabletBoard.Of(mc).txAxis, TabletBoard.Of(mc).txMain, range); }
            Geometry();
            double tx = 0.0;
            if (TabletBoard.Of(mc).txX >= 1 && TabletBoard.Of(mc).txX <= 32) tx += G(0, TabletBoard.Of(mc).txX, gRange);
            if (TabletBoard.Of(mc).txY >= 1 && TabletBoard.Of(mc).txY <= 19) tx += G(1, TabletBoard.Of(mc).txY, gRange);
            return tx;
        }

        // integrating receiver: the ADC reads the integral of the ring-down since the integrator was released
        public double Integrated(Machine mc)
        {
            if (!present || TabletBoard.Of(mc).rxAxis < 0) return 0.0;
            long now = mc.cpu.cycles;
            long tr = TabletBoard.Of(mc).tRel >= 0 && TabletBoard.Of(mc).tRel <= now ? TabletBoard.Of(mc).tRel : now;
            double tau = Q / (Math.PI * f0);
            if (resonator)
            {
                if (tr < tLast) tr = tLast;
                double tx0 = Coupling(mc);
                double a0 = P.Sqrt(re * re + im * im) * P.Exp(-((double)(tr - tLast) / 72000000.0) / tau);
                return a0 * tau * (1 - P.Exp(-((double)(now - tr) / 72000000.0) / tau)) * INT_GAIN * G(TabletBoard.Of(mc).rxAxis, TabletBoard.Of(mc).rxCoil, gRange);
            }
            if (TabletBoard.Of(mc).txAxis < 0) return 0.0;
            double fTx = TabletBoard.Of(mc).txFreq;
            double q = 2 * Q * (fTx - f0) / f0;
            double L = 1 / P.Sqrt(1 + q * q);
            double pulses = TabletBoard.Of(mc).txPeriod != 0 ? (double)(TabletBoard.Of(mc).txEnd - TabletBoard.Of(mc).txStart) / (double)TabletBoard.Of(mc).txPeriod : 0.0;
            double build = 1 - P.Exp(-pulses / fTx / tau);
            double tx = Coupling(mc);
            double a = gStrength * tx * G(TabletBoard.Of(mc).rxAxis, TabletBoard.Of(mc).rxCoil, gRange) * L * build;
            double t1 = (double)(tr - TabletBoard.Of(mc).txEnd) / 72000000.0, t2 = (double)(now - TabletBoard.Of(mc).txEnd) / 72000000.0;
            if (t1 < 0) t1 = 0;
            if (t2 < t1) t2 = t1;
            return a * tau * (P.Exp(-t1 / tau) - P.Exp(-t2 / tau)) * INT_GAIN;
        }

        public void DecayTo(long cyc)
        {
            double k = P.Exp(-((double)(cyc - tLast) / 72000000.0) / (Q / (Math.PI * f0)));
            re *= k;
            im *= k;
            tLast = cyc;
        }

        public void BurstOn(Machine mc)
        {
            DecayTo(mc.cpu.cycles);
            drive = 0.0;
            if (present && TabletBoard.Of(mc).txAxis >= 0)
            {
                double tx = Coupling(mc);
                drive = gStrength * tx;
            }
        }

        public void BurstOff(Machine mc)
        {
            double tau = Q / (Math.PI * f0);
            double T = (double)(TabletBoard.Of(mc).txEnd - TabletBoard.Of(mc).txStart) / 72000000.0;
            DecayTo(TabletBoard.Of(mc).txStart);
            double e = P.Exp(-T / tau);
            re *= e;
            im *= e;
            tLast = TabletBoard.Of(mc).txEnd;
            if (drive != 0.0 && T > 0)
            {
                double dw = 2 * Math.PI * (TabletBoard.Of(mc).txFreq - f0);
                double ph = -2 * Math.PI * ((f0 * (double)TabletBoard.Of(mc).txStart / 72000000.0) % 1.0);
                double nr = P.Cos(dw * T) - e, ni = P.Sin(dw * T);
                double dr = 1 / tau, di = dw;
                double den = dr * dr + di * di;
                double qr = (nr * dr + ni * di) / den, qi = (ni * dr - nr * di) / den;
                double k = drive / tau;
                double cr = P.Cos(ph), ci = P.Sin(ph);
                re += k * (qr * cr - qi * ci);
                im += k * (qr * ci + qi * cr);
            }
            drive = 0.0;
        }

        public int Adc(Machine mc, double tAfter)
        {
            double amp;
            if (integrate) amp = Integrated(mc);
            else if (resonator)
            {
                if (!present || TabletBoard.Of(mc).rxAxis < 0) amp = 0.0;
                else
                {
                    DecayTo(mc.cpu.cycles);
                    Coupling(mc);
                    amp = P.Sqrt(re * re + im * im) * G(TabletBoard.Of(mc).rxAxis, TabletBoard.Of(mc).rxCoil, gRange);
                }
            }
            else
            {
                double fTx = TabletBoard.Of(mc).txFreq;
                if (!present || TabletBoard.Of(mc).txAxis < 0 || TabletBoard.Of(mc).rxAxis < 0) amp = 0.0;
                else
                {
                    double tau = Q / (Math.PI * f0);
                    double q = 2 * Q * (fTx - f0) / f0;
                    double L = 1 / P.Sqrt(1 + q * q);
                    double pulses = TabletBoard.Of(mc).txPeriod != 0 ? (double)(TabletBoard.Of(mc).txEnd - TabletBoard.Of(mc).txStart) / (double)TabletBoard.Of(mc).txPeriod : 0.0;
                    double build = 1 - P.Exp(-pulses / fTx / tau);
                    double tx = Coupling(mc);
                    amp = gStrength * tx * G(TabletBoard.Of(mc).rxAxis, TabletBoard.Of(mc).rxCoil, gRange) * L * build * P.Exp(-(tAfter > 0 ? tAfter : 0) / tau);
                }
            }
            double v = amp + rng.Gauss(0, noise);
            double rv = P.Rint(v);
            if (P.IsNaN(rv)) rv = 0;
            long iv = rv > 4095 ? 4095 : (rv < 0 ? 0 : (long)rv);
            // R43: the ADC gives all 12 bits; the stock sampling loop clears the low 3 itself (bic #7, the instruction
            // 12-bit Readings removes), so stock still sees multiples of 8 here
            return (int)iv;
        }
    }


    // ======================================================================================== the tablet as a board
    // The Deco 640 around the GD32F350: coil multiplexers (seen through the firmware's transmit / receive functions),
    // the pen (through the ADC), and shortcuts for the firmware's busy-wait delays.
    public class TabletBoard : Board
    {
        public const int H_DELAY_R4 = 0, H_SKIP = 1, H_DELAY_158 = 2, H_TX_X = 3, H_TX_Y = 4, H_SEL_X = 5, H_SEL_Y = 6, H_RX_X = 7, H_RX_Y = 8, H_FRAME = 9, H_GPIO_LOW = 10;
        public Pen pen;
        public int[] skipTo = new int[0x8000];
        public LongList markCyc = new LongList(), markTag = new LongList();
        public LongList procCyc = new LongList();      // per frame: frame restart - last ADC conversion (processing)
        public int txAxis = -1, rxAxis = -1, txMain = -1, txX = -1, txY = -1, rxCoil = -1;
        public long txStart = 0, txEnd = 0, txPeriod = 0, tRel = -1, lastAdc = -1;
        public double txFreq = 0.0;
        public TabletBoard(Pen p) { pen = p; }
        public static TabletBoard Of(Machine mc) { return (TabletBoard)mc.board; }

        public override int[] BootStops(Machine mc) { return Twin.FindAll(mc.flash, new byte[] { 0x20, 0x78, 0x01, 0x28 }, 0x3800, Twin.LayoutOf(mc.flash).searchEnd - 0x08000000); }

        public override void Attach(Machine mc)
        {
            mc.vtor = 0x08003800;                      // the tablet's bootloader starts the firmware here
            byte[] img = mc.flash;
            if (mc.scan)
            {
                // the pen model attaches to the firmware's coil, receive, delay and frame routines wherever this
                // firmware build keeps them (atf_layout.cs)
                Layout ly = Twin.LayoutOf(img);
                mc.SetHook(ly.A(0x08008154), H_DELAY_158); mc.SetHook(ly.A(0x080080B8), H_DELAY_158);
                mc.SetHook(ly.A(0x08005818), H_SKIP); skipTo[(ly.A(0x08005818) - 0x08000000) >> 1] = ly.A(0x08005830);
                mc.SetHook(ly.A(0x0800997C), H_TX_X); mc.SetHook(ly.A(0x080099CC), H_TX_Y);
                mc.SetHook(ly.A(0x080088B8), H_SEL_X); mc.SetHook(ly.A(0x080088E8), H_SEL_Y);
                mc.SetHook(ly.A(0x0800990C), H_RX_X); mc.SetHook(ly.A(0x08009944), H_RX_Y);
                mc.SetHook(ly.A(0x08007084), H_FRAME);
                mc.SetHook(ly.A(0x080084C4), H_GPIO_LOW);
            }
            else
            {
                int[] w1 = Twin.FindAll(img, new byte[] { 0x20, 0x68, 0x00, 0x28, 0xfc, 0xd1 }, 0x3800, 0x10000);
                for (int i = 0; i < w1.Length; i++) mc.SetHook(w1[i], H_DELAY_R4);
                int[] w2 = Twin.FindAll(img, new byte[] { 0x00, 0x22, 0x52, 0x1c, 0x82, 0x42, 0xfc, 0xd3, 0x5b, 0x1c, 0x8b, 0x42, 0xf8, 0xd3 }, 0x3800, 0x10000);
                for (int i = 0; i < w2.Length; i++) { mc.SetHook(w2[i], H_SKIP); skipTo[(w2[i] - 0x08000000) >> 1] = w2[i] + 14; }
            }
        }

        public override bool Hook(Machine mc, int hk)
        {
            Cpu c = mc.cpu;
            if (hk == H_DELAY_R4) { mc.Write(c.r[4], 4, 0); return false; }
            if (hk == H_SKIP) { c.r[15] = skipTo[(c.r[15] - 0x08000000) >> 1]; return true; }
            if (hk == H_DELAY_158) { mc.Write(0x20000158, 4, 0); return false; }
            if (hk == H_TX_X || hk == H_TX_Y)
            {
                txAxis = hk == H_TX_X ? 0 : 1;
                txMain = c.r[0];
                txX = -1; txY = -1;
                markCyc.Add(c.cycles); markTag.Add(0);
                return false;
            }
            if (hk == H_SEL_X) { txX = c.r[0]; return false; }
            if (hk == H_SEL_Y) { txY = c.r[0]; return false; }
            if (hk == H_RX_X || hk == H_RX_Y) { rxAxis = hk == H_RX_X ? 0 : 1; rxCoil = c.r[0]; return false; }
            if (hk == H_GPIO_LOW) { if (c.r[0] == 0x48000400 && (c.r[1] & 0x100) != 0) tRel = c.cycles; return false; }
            markCyc.Add(c.cycles); markTag.Add(1);
            if (lastAdc >= 0) procCyc.Add(c.cycles - lastAdc);
            return false;
        }

        public override int Adc(Machine mc)
        {
            if (!mc.scan) { mc.adcSeq++; return 300; }
            long now = mc.cpu.cycles;
            lastAdc = now;
            return pen.Adc(mc, (double)(now - txEnd) / 72000000.0);
        }

        public override void TimerEdge(Machine mc, int tb, bool on)
        {
            if (tb != 0x40012C00) return;                 // TIMER0 = excitation carrier
            if (on)
            {
                txStart = mc.cpu.cycles;
                txPeriod = (mc.Car(tb) + 1) * mc.Psc(tb);
                txFreq = 72000000.0 / (double)txPeriod;
                if (pen.resonator) pen.BurstOn(mc);
            }
            else
            {
                txEnd = mc.cpu.cycles;
                if (pen.resonator) pen.BurstOff(mc);
            }
        }
    }
    // ======================================================================================== checks
    public class Out
    {
        public StringBuilder buf = null;
        public bool failed = false;
        public void Say(string s)
        {
            if (P.StartsWith(s, "RESULT ") && (P.EndsWith(s, " fail") || P.Eq(s, "RESULT fail"))) failed = true;
            if (buf != null) buf.Append(s + "\n"); else P.Line(s);
        }
    }

    public class SensResult { public int n = -1; public string why = ""; public Machine mc = null; }

    // averaged cursor delay (Twin.MeasureDelay): drawing and, optionally, hovering; ms, spread = max - min over the runs
    public class Latency
    {
        public bool ok = false, hasHover = false;
        public double drawMs, drawSpread, drawRate, hoverMs, hoverSpread, hoverRate;
    }

    public class FrameStats
    {
        public int frames = 0, reports = 0;
        public double periodMean, periodMin, periodMax, procMean, procMax, workMean, workMax;
        public bool ok = false;
    }

    public class Precision
    {
        public bool ok = false;
        public double sdX, sdY, p2p, maxErr, mmPerCoil;
        public int n;
    }

    public static class Twin
    {
        public static readonly double[] SENS_FREQS = new double[] { 496000.0, 503000.0, 510000.0, 518000.0 };
        public static bool receiverIntegrate = false;
        public static double measStrength = 60000.0, measNoise = 6.0;   // pen for --measure (set with --pen-strength / --pen-noise)
        public const double PS_PER_CYCLE = 13888.888888888889;

        public static byte[] FromApp(byte[] app)
        {
            byte[] flash = new byte[0x10000];
            for (int i = 0; i < flash.Length; i++) flash[i] = (byte)0xFF;
            P.Copy(app, 0, flash, 0x3800, app.Length < 0x10000 - 0x3800 ? app.Length : 0x10000 - 0x3800);
            return flash;
        }

        // microseconds with picosecond resolution (one CPU cycle = 13888.889 ps)
        public static string Us(double cycles) { return F.Fx(cycles / 72.0, 6) + " us"; }

        // frame timing with a pen moving slowly across the surface (so every filter has work to do)
        public static FrameStats MeasureFrames(byte[] img, double ms, bool rt)
        {
            FrameStats fs = new FrameStats();
            Pen pen = new Pen(1);
            pen.x = 15.2; pen.y = 9.6; pen.f0 = 505000.0; pen.Q = 300.0; pen.strength = 60000.0;
            StringBuilder why = new StringBuilder();
            Machine mc = ScanMachine(img, pen, rt, why);
            if (mc == null) return fs;
            try
            {
                mc.RunMs(60);
                long t0 = mc.cpu.cycles;
                int p0 = TabletBoard.Of(mc).procCyc.n, m0 = TabletBoard.Of(mc).markCyc.n, r0 = mc.reports.n;
                for (int t = 0; t < (int)ms; t++) { pen.x = 15.2 + 2.0 * t / ms; mc.RunMs(1); }
                double sum = 0, mx = 0; int n = 0;
                for (int i = p0; i < TabletBoard.Of(mc).procCyc.n; i++) { double v = TabletBoard.Of(mc).procCyc.a[i]; sum += v; if (v > mx) mx = v; n++; }
                fs.procMean = n > 0 ? sum / n : 0; fs.procMax = mx;
                long last = -1; double ps = 0, pmin = 1e300, pmax = 0; int np = 0;
                long first = -1; double ws = 0, wmax = 0; int nw = 0;
                for (int i = m0; i < TabletBoard.Of(mc).markCyc.n; i++)
                {
                    long c = TabletBoard.Of(mc).markCyc.a[i];
                    if (TabletBoard.Of(mc).markTag.a[i] == 0) { if (first < 0) first = c; continue; }
                    if (last >= 0) { double d = c - last; ps += d; np++; if (d < pmin) pmin = d; if (d > pmax) pmax = d; }
                    if (first >= 0) { double w = c - first; ws += w; nw++; if (w > wmax) wmax = w; }
                    last = c; first = -1;
                }
                fs.frames = np; fs.periodMean = np > 0 ? ps / np : 0; fs.periodMin = np > 0 ? pmin : 0; fs.periodMax = pmax;
                fs.workMean = nw > 0 ? ws / nw : 0; fs.workMax = wmax;
                fs.reports = mc.reports.n - r0;
                fs.ok = np > 2;
            }
            catch (Fault) { }
            catch (Stop) { }
            return fs;
        }

        // position precision: still-pen spread and the worst error while sliding across two coils
        public static Precision MeasurePrecision(byte[] img, bool rt)
        {
            Precision pr = new Precision();
            Pen pen = new Pen(1);
            pen.x = 16.3; pen.y = 9.6; pen.f0 = 505000.0; pen.Q = 300.0; pen.strength = measStrength; pen.noise = measNoise;
            StringBuilder why = new StringBuilder();
            Machine mc = ScanMachine(img, pen, rt, why);
            if (mc == null) return pr;
            try
            {
                mc.RunMs(80);
                int r0 = mc.reports.n;
                mc.RunMs(120);
                long[] xs = new long[mc.reports.n], ys = new long[mc.reports.n];
                int n = 0;
                for (int i = r0; i < mc.reports.n; i++)
                {
                    Report r = mc.reports.a[i];
                    if (!PositionReport(r)) continue;
                    xs[n] = (r.data[2] & 0xFF) | ((r.data[3] & 0xFF) << 8); ys[n] = (r.data[4] & 0xFF) | ((r.data[5] & 0xFF) << 8); n++;
                }
                if (n < 5) return pr;
                long mnx = xs[0], mxx = xs[0];
                for (int i = 0; i < n; i++) { if (xs[i] < mnx) mnx = xs[i]; if (xs[i] > mxx) mxx = xs[i]; }
                pr.sdX = Sd(xs, n) / 100.0; pr.sdY = Sd(ys, n) / 100.0; pr.p2p = (mxx - mnx) / 100.0;
                // slide 2 coil pitches in 400 ms; fit reported X against the true position
                int ms = 400, r1 = mc.reports.n;
                double[] tx = new double[ms * 4], rx = new double[ms * 4];
                int m = 0;
                for (int t = 0; t < ms; t++)
                {
                    pen.x = 15.5 + 2.0 * t / ms;
                    int before = mc.reports.n;
                    mc.RunMs(1);
                    for (int i = before; i < mc.reports.n; i++)
                    {
                        Report r = mc.reports.a[i];
                        if (!PositionReport(r) || m >= tx.Length) continue;
                        tx[m] = pen.x; rx[m] = (r.data[2] & 0xFF) | ((r.data[3] & 0xFF) << 8); m++;
                    }
                }
                if (m < 10) return pr;
                double sx = 0, sy = 0, sxx = 0, sxy = 0;
                for (int i = 0; i < m; i++) { sx += tx[i]; sy += rx[i]; sxx += tx[i] * tx[i]; sxy += tx[i] * rx[i]; }
                double slope = (m * sxy - sx * sy) / (m * sxx - sx * sx), icpt = (sy - slope * sx) / m, worst = 0;
                for (int i = 0; i < m; i++) { double e = rx[i] - (icpt + slope * tx[i]); if (e < 0) e = -e; if (e > worst) worst = e; }
                pr.maxErr = worst / 100.0; pr.mmPerCoil = slope / 100.0; pr.n = m; pr.ok = true;
            }
            catch (Fault) { }
            catch (Stop) { }
            return pr;
        }

        // end-to-end cursor latency (ms): a strong pen slid forward then back at a steady speed; the reported position
        // lags the true one by (interpolation group delay + movement-smoothing dead-band drag + half the report
        // interval). Returns -1 if the pen was not tracked. (One run; the Settings step uses MeasureDelay.)
        public static double MeasureLatency(byte[] img, bool rt)
        {
            double[] r = LatencyRun(img, rt, 7, 14.5, LAT_SPEED, measStrength);
            if (r == null) return -1;
            return r[0] > 0 ? r[0] : 0;
        }

        // R41: one slide run with its conditions as parameters. Returns { lag ms, reports per second while sliding }, or
        // null if the pen was not tracked. The start position x0 sets the coil phase, the seed the pen noise.
        // How the lag is found: reports are binned by the pen's true position (0.1-coil bins over the two coil pitches
        // both directions cover); in each bin the backward-minus-forward difference of the reported position is twice
        // the distance the cursor trails, and its median over the bins, divided by the speed, is the lag. A per-position
        // difference cancels the coils' own position error, and the median ignores the few bins where the reported
        // position jumps (the tracked coil switches at a slightly different place in each direction).
        // Until R40 a straight line was fitted to each direction instead, over different stretches of the coils, so a
        // jump near the end of either stretch moved the result: it swung by up to 100 ms with the start position alone
        // (FAST, Aggressive Interpolation: -28 to +81 ms). The median-bin result varies by under 0.6 ms (8 start phases).
        public const double LAT_SPEED = 10.0;                    // coils per second (5.1 mm per coil: about 51 mm/s)
        public const double HOVER_STRENGTH = 5200.0;             // the twin's ordinary pen: weak enough for Hover Boost
        const int LAT_BINS = 20;                                 // 0.1-coil bins from x0 + 0.5 to x0 + 2.5
        public static double[] LatencyRun(byte[] img, bool rt, long seed, double x0, double v, double strength)
        {
            Pen pen = new Pen(seed);
            pen.x = x0; pen.y = 9.6; pen.f0 = 505000.0; pen.Q = 300.0; pen.strength = strength; pen.noise = measNoise;
            StringBuilder why = new StringBuilder();
            Machine mc = ScanMachine(img, pen, rt, why);
            if (mc == null) return null;
            double span = 3.0, lo = x0 + 0.5, w = 0.1;          // coils travelled; binned stretch
            int legMs = (int)(span / v * 1000);
            int seen = 0, slidMs = 0, n = 0;
            double[] sumR = new double[2 * LAT_BINS], sumX = new double[2 * LAT_BINS];
            int[] cnt = new int[2 * LAT_BINS];
            double sx = 0, sy = 0, sxx = 0, sxy = 0;
            try
            {
                mc.RunMs(150);
                for (int dir = 0; dir < 2; dir++)
                {
                    for (int t = 0; t < legMs; t++)
                    {
                        int before = mc.reports.n;
                        for (int s = 0; s < 10; s++) { double tt = t + s / 10.0; pen.x = dir == 0 ? x0 + v * tt / 1000 : x0 + span - v * tt / 1000; mc.RunMs(0.1); }
                        if (t < 40) continue;                    // let the filters settle after the direction change
                        slidMs++;
                        for (int i = before; i < mc.reports.n; i++)
                        {
                            Report r = mc.reports.a[i];
                            if (!PositionReport(r)) continue;
                            seen++;
                            if (pen.x < lo) continue;
                            int bin = (int)((pen.x - lo) / w);
                            if (bin >= LAT_BINS) continue;
                            double rx = (r.data[2] & 0xFF) | ((r.data[3] & 0xFF) << 8);
                            sumR[dir * LAT_BINS + bin] += rx; sumX[dir * LAT_BINS + bin] += pen.x; cnt[dir * LAT_BINS + bin]++;
                            sx += pen.x; sy += rx; sxx += pen.x * pen.x; sxy += pen.x * rx; n++;
                        }
                    }
                }
            }
            catch (Fault) { return null; }
            catch (Stop) { return null; }
            if (n < 20 || slidMs == 0) return null;
            double d = n * sxx - sx * sx;
            double slope = d != 0 ? (n * sxy - sx * sy) / d : 0; // report units per coil; negative: X is reported inverted
            double mag = slope < 0 ? -slope : slope;
            if (mag <= 0.01) return null;
            double[] lags = new double[LAT_BINS];
            int m = 0;
            for (int b = 0; b < LAT_BINS; b++)
            {
                int f = b, k = LAT_BINS + b;
                if (cnt[f] == 0 || cnt[k] == 0) continue;
                double c = lo + (b + 0.5) * w;                   // compare both directions at the bin centre
                double rf = sumR[f] / cnt[f] - slope * (sumX[f] / cnt[f] - c), rb = sumR[k] / cnt[k] - slope * (sumX[k] / cnt[k] - c);
                lags[m++] = (rb - rf) / (2 * slope * v) * 1000;
            }
            if (m < LAT_BINS / 2) return null;
            return new double[] { Median(lags, m), seen * 1000.0 / slidMs };
        }
        // median of the first N values (sorts them in place; N is small)
        static double Median(double[] a, int n)
        {
            for (int i = 1; i < n; i++) { double x = a[i]; int j = i - 1; while (j >= 0 && a[j] > x) { a[j + 1] = a[j]; j--; } a[j + 1] = x; }
            return n % 2 == 1 ? a[n / 2] : (a[n / 2 - 1] + a[n / 2]) / 2;
        }

        // R41: the cursor delay the Settings step shows, the mean of LAT_RUNS runs (pen noise seed and coil phase vary;
        // the spread is kept for the logs). Drawing = a contact-strength pen (measStrength) at LAT_SPEED; hovering =
        // HOVER_STRENGTH at the same speed, where Hover Boost may lower the report rate (hoverRate).
        public const int LAT_RUNS = 2;
        public static Latency MeasureDelay(byte[] img, bool rt, bool hover)
        {
            Latency l = new Latency();
            double[] d = new double[LAT_RUNS], h = new double[LAT_RUNS];
            for (int k = 0; k < LAT_RUNS; k++)
            {
                double[] r = LatencyRun(img, rt, 11 + k, 14.5 + 0.25 * k, LAT_SPEED, measStrength);
                if (r == null) return l;
                d[k] = r[0]; l.drawRate += r[1] / LAT_RUNS;
                if (!hover) continue;
                double[] q = LatencyRun(img, rt, 11 + k, 14.5 + 0.25 * k, LAT_SPEED, HOVER_STRENGTH);
                if (q == null) return l;
                h[k] = q[0]; l.hoverRate += q[1] / LAT_RUNS;
            }
            l.drawMs = Mean(d); l.drawSpread = Spread(d);
            if (hover) { l.hoverMs = Mean(h); l.hoverSpread = Spread(h); l.hasHover = true; }
            l.ok = true;
            return l;
        }
        static double Mean(double[] a) { double s = 0; for (int i = 0; i < a.Length; i++) s += a[i]; return s / a.Length; }
        static double Spread(double[] a)
        {
            double lo = a[0], hi = a[0];
            for (int i = 1; i < a.Length; i++) { if (a[i] < lo) lo = a[i]; if (a[i] > hi) hi = a[i]; }
            return hi - lo;
        }
        // a delay for the screen, in whole ms (the measurement resolves about 0.5 ms; it never prints 0.0)
        public static string DelayText(double ms)
        {
            if (ms < 0.5) return "under 1 ms";
            int r = (int)P.Rint(ms);
            return "about " + F.Str(r < 1 ? 1 : r) + " ms";
        }
        // the difference between two delays: "about 5 ms more", "under 1 ms more", "about 2 ms less"
        public static string AddedDelayText(double ms)
        {
            if (ms < 1.0 && ms > -1.0) return "under 1 ms more";
            if (ms < 0) return "about " + F.Str((int)P.Rint(-ms)) + " ms less";
            return "about " + F.Str((int)P.Rint(ms)) + " ms more";
        }
        // CPU time for the screen, in ms like everything else there
        public static string Ms(double cycles)
        {
            double ms = cycles / 72000.0;
            return ms < 0.01 ? "under 0.01 ms" : F.Fx(ms, 2) + " ms";
        }

        public static void Measure(byte[] img, Out o, bool rt)
        {
            o.Say("== measurements (digital twin" + (rt ? ", paced to the real 72 MHz chip" : ", full PC speed") + "; resolution 1 CPU cycle = 13888.889 ps)");
            FrameStats fs = MeasureFrames(img, 200, rt);
            if (fs.ok)
            {
                o.Say("   frame period: mean " + Us(fs.periodMean) + ", min " + Us(fs.periodMin) + ", max " + Us(fs.periodMax) + " (" + F.Str(fs.frames) + " frames)");
                o.Say("   frame work (scan + processing): mean " + Us(fs.workMean) + ", max " + Us(fs.workMax));
                o.Say("   processing after the last reading: mean " + Us(fs.procMean) + ", max " + Us(fs.procMax));
            }
            else o.Say("   frame timing: no frames measured");
            Precision pr = MeasurePrecision(img, rt);
            if (pr.ok)
            {
                o.Say("   precision: report step 0.010 mm; still pen spread X " + F.Fx(pr.sdX, 3) + " mm, Y " + F.Fx(pr.sdY, 3) + " mm (sd), " + F.Fx(pr.p2p, 3) + " mm peak-to-peak");
                o.Say("   accuracy: worst position error while sliding across 2 coils " + F.Fx(pr.maxErr, 3) + " mm (" + F.Fx(pr.mmPerCoil, 3) + " mm per coil, " + F.Str(pr.n) + " reports)");
                o.Say("   (spread uses the twin's synthetic noise: compare builds with it; Tablet Calibration measures the real thing)");
            }
            else o.Say("   precision: the pen was not tracked");
            double lat = MeasureLatency(img, rt);
            if (lat >= 0) o.Say("   cursor latency (steady slide): " + F.Fx(lat, 1) + " ms (position interpolation + movement smoothing + report interval)");
            else o.Say("   cursor latency: the pen was not tracked");
            o.Say("RESULT measure period=" + F.Fx(fs.periodMean / 72.0, 6) + " proc=" + F.Fx(fs.procMean / 72.0, 6) + " sd=" + F.Fx(pr.sdX, 3) + " err=" + F.Fx(pr.maxErr, 3) + " lat=" + F.Fx(lat, 1));
            if (!fs.ok || !pr.ok || lat < 0) o.Say("RESULT measure fail");
        }

        public static int[] FindAll(byte[] img, byte[] pat, int lo, int hi) { return Mem.FindAll(img, pat, lo, hi); }

        // the firmware layout of a full 64 KB flash image (resolved from its own code; cached)
        public static Layout LayoutOf(byte[] flash) { return Layout.For(flash, 0x3800, 0x10000 - 0x3800, null); }
        // the firmware locations the twin's tablet model attaches to; a build without all of them cannot be checked
        public static readonly int[] HOOKS = new int[] { 0x08008154, 0x080080B8, 0x08005818, 0x08005830, 0x0800997C, 0x080099CC,
            0x080088B8, 0x080088E8, 0x0800990C, 0x08009944, 0x08007084, 0x080084C4, 0x0800454C };
        public static string MissingHooks(byte[] flash)
        {
            Layout l = LayoutOf(flash);
            string s = "";
            for (int i = 0; i < HOOKS.Length; i++) if (!l.Has(HOOKS[i])) s += (P.Len(s) > 0 ? ", " : "") + F.Hx(HOOKS[i], 8);
            return P.Len(s) > 0 ? s : null;
        }
        public static int Hexv(string s, int i) { return (int)F.ParseInt("0x" + P.Sub(s, i, 2)); }

        public static byte[] LoadImage(string path)
        {
            byte[] flash = new byte[0x10000];
            for (int i = 0; i < flash.Length; i++) flash[i] = (byte)0xFF;
            byte[] raw = P.ReadAllBytes(path);
            if (P.EndsWithHex(path) || (raw.Length > 0 && raw[0] == (byte)':'))
            {
                int[] first = new int[1];
                byte[] data = IntelHex.Parse(P.Ascii(raw), path, first);
                P.Copy(data, 0, flash, first[0] - 0x08000000, data.Length);
                return flash;
            }
            if (raw.Length > 0x3804 && (Machine.LE(raw, 0, 4) & 0x2FFE0000) == 0x20000000 && (Machine.LE(raw, 0x3800, 4) & 0x2FFE0000) == 0x20000000)
                P.Copy(raw, 0, flash, 0, raw.Length < 0x10000 ? raw.Length : 0x10000);
            else
                P.Copy(raw, 0, flash, 0x3800, raw.Length < 0x10000 - 0x3800 ? raw.Length : 0x10000 - 0x3800);
            return flash;
        }

        public static string Esc(string s)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < P.Len(s); i++)
            {
                int ch = P.CharAt(s, i);
                if (ch >= 32 && ch < 127 && ch != '\\' && ch != '"') sb.Append((char)ch);
                else sb.Append("\\u" + F.HexDigits(ch, 4, false));
            }
            return sb.ToString();
        }

        public static string Hex2(int v) { return F.HexDigits(v & 0xFF, 2, false); }

        static string ValidateHidReport(byte[] report, int packet)
        {
            if (report == null || report.Length == 0) return "report descriptor is empty";
            int depth = 0, stack = 0, size = 0, count = 0, id = 0;
            int[] globals = new int[48], inputBits = new int[256];
            bool input = false;
            for (int i = 0; i < report.Length; )
            {
                int prefix = report[i++] & 255;
                if (prefix == 0xFE) return "long HID items are not supported by the tablet check";
                int length = prefix & 3; if (length == 3) length = 4;
                if (length > report.Length - i) return "truncated HID item";
                int type = (prefix >> 2) & 3, tag = prefix >> 4;
                long value = 0;
                for (int k = 0; k < length; k++) value |= (long)(report[i + k] & 255) << (8 * k);
                i += length;
                if (type == 3) return "reserved HID item type";
                if (type == 1)
                {
                    if (tag == 7 || tag == 9)
                    {
                        if (length == 0 || value == 0 || value > (tag == 7 ? 32 : 4096)) return "invalid HID report size or count";
                        if (tag == 7) size = (int)value; else count = (int)value;
                    }
                    else if (tag == 8)
                    {
                        if (length != 1 || value == 0) return "invalid HID report ID";
                        id = (int)value;
                    }
                    else if (tag == 10)
                    {
                        if (length != 0 || stack == 16) return "invalid HID global push";
                        globals[stack * 3] = size; globals[stack * 3 + 1] = count; globals[stack * 3 + 2] = id; stack++;
                    }
                    else if (tag == 11)
                    {
                        if (length != 0 || stack == 0) return "invalid HID global pop";
                        stack--; size = globals[stack * 3]; count = globals[stack * 3 + 1]; id = globals[stack * 3 + 2];
                    }
                    else if (tag > 11 || length == 0) return "invalid HID global item";
                }
                else if (type == 0)
                {
                    if (tag == 10)
                    {
                        if (length != 1 || value > 6 || depth == 32) return "invalid HID collection";
                        depth++;
                    }
                    else if (tag == 12)
                    {
                        if (length != 0 || depth == 0) return "unbalanced HID end collection";
                        depth--;
                    }
                    else if (tag == 8 || tag == 9 || tag == 11)
                    {
                        if (length == 0 || depth == 0 || size == 0 || count == 0 || size * count > 32768) return "invalid HID data item";
                        if (tag == 8)
                        {
                            inputBits[id] += size * count; input = true;
                            if (inputBits[id] > (packet - (id == 0 ? 0 : 1)) * 8) return "HID input report exceeds its IN endpoint packet size";
                        }
                    }
                    else return "unknown HID main item";
                }
            }
            if (depth != 0 || stack != 0) return "unbalanced HID collection or global stack";
            return input ? null : "HID descriptor has no input reports";
        }

        public static string ValidateTabletUsb(byte[] device, byte[] config, byte[][] reports, int expectedInterval)
        {
            if (device == null || device.Length != 18 || (device[0] & 255) != 18 || (device[1] & 255) != 1)
                return "device descriptor must be 18 bytes";
            if ((device[7] & 255) != 64) return "endpoint zero max packet size must be 64";
            if ((device[17] & 255) != 1) return "expected one tablet USB configuration";
            int vid = (device[8] & 255) | ((device[9] & 255) << 8), pid = (device[10] & 255) | ((device[11] & 255) << 8);
            if (vid != 0x28BD || pid != 0x2904) return "expected tablet VID 28BD and PID 2904";
            UsbConfiguration c;
            try { c = UsbConfiguration.Parse(config); } catch (Stop e) { return e.msg; }
            if (c.interfaces != 3) return "expected exactly three HID interfaces";
            for (int i = 0; i < 256; i++)
                if (c.active[i] && (i > 2 || c.classes[i] != 3)) return "expected HID interfaces 0, 1 and 2";
            int[] addresses = new int[] { 0x81, 0x82, 0x83, 0x03 }, owners = new int[] { 0, 1, 2, 2 }, packets = new int[] { 8, 64, 12, 10 };
            for (int i = 0; i < addresses.Length; i++)
            {
                int address = addresses[i]; string endpoint = "endpoint 0x" + Hex2(address);
                if (c.endpointInterface[address] != owners[i]) return endpoint + " must belong to interface " + F.Str(owners[i]);
                if (c.attributes[address] != 3) return endpoint + " must use interrupt transfers";
                if (c.maxPacket[address] != packets[i] && !(address == 3 && c.maxPacket[address] == 20))
                    return endpoint + " max packet size must be " + (address == 3 ? "10 or 20" : F.Str(packets[i]));
                if (address >= 0x80 && expectedInterval > 0 && c.interval[address] != expectedInterval)
                    return endpoint + " bInterval must be " + F.Str(expectedInterval) + " (got " + F.Str(c.interval[address]) + ")";
            }
            for (int i = 0; i < 256; i++)
                if (c.endpointInterface[i] >= 0 && i != 0x81 && i != 0x82 && i != 0x83 && i != 3) return "unexpected tablet endpoint 0x" + Hex2(i);
            int[] declared = new int[3]; bool[] seenHid = new bool[3]; int number = -1, alternate = -1;
            for (int i = 9; i < config.Length; i += config[i] & 255)
            {
                int length = config[i] & 255, type = config[i + 1] & 255;
                if (type == 4) { number = config[i + 2] & 255; alternate = config[i + 3] & 255; }
                else if (type == 0x21 && alternate == 0)
                {
                    if (number < 0 || number > 2 || seenHid[number] || length != 9 || (config[i + 5] & 255) != 1 || (config[i + 6] & 255) != 0x22)
                        return "each tablet HID interface needs one complete report descriptor declaration";
                    seenHid[number] = true;
                    declared[number] = (config[i + 7] & 255) | ((config[i + 8] & 255) << 8);
                }
            }
            for (int i = 0; i < 3; i++)
            {
                string prefix = "HID interface " + F.Str(i) + ": ";
                if (declared[i] < 1 || declared[i] > 1024) return prefix + "declared report descriptor length must be between 1 and 1024 bytes";
                if (reports == null || reports.Length != 3 || reports[i] == null || reports[i].Length != declared[i]) return prefix + "report descriptor is missing or its length differs from the HID declaration";
                string reason = ValidateHidReport(reports[i], c.maxPacket[0x81 + i]);
                if (reason != null) return prefix + reason;
            }
            return null;
        }

        public static bool Describe(byte[] img, string label, Out o)
        {
            return Describe(img, label, o, 0);
        }

        public static bool Describe(byte[] img, string label, Out o, int expectedInterval)
        {
            o.Say("== " + label);
            Machine mc = new Machine(img, false, new TabletBoard(null), false);
            string st;
            try { st = mc.Boot(2000000); }
            catch (Exception) { st = "emulator stopped"; }
            if (!P.Eq(st, "ok"))
            {
                o.Say("   BOOT FAILED: " + st);
                o.Say("   On the tablet this image would hang or fault at power-on (no USB device appears).");
                o.Say("RESULT boot fail");
                return false;
            }
            o.Say("   boot: ok (" + F.Str(mc.cpu.icount) + " instructions to the main loop)");
            o.Say("RESULT boot ok " + F.Str(mc.cpu.icount));
            VirtualHost host = mc.AttachUsb(false);
            try { host.Enumerate(); }
            catch (Fault e) { o.Say("   ENUMERATION FAILED: " + e.msg); o.Say("RESULT usb fail"); return false; }
            catch (Stop e) { o.Say("   ENUMERATION FAILED: " + e.msg); o.Say("RESULT usb fail"); return false; }
            byte[] d = host.dev, cfg = host.cfg;
            byte[][] reports = new byte[3][];
            for (int itf = 0; itf < 3; itf++)
            {
                try { reports[itf] = host.GetDescriptor(0x22, 0, 1024, itf, 0x81); }
                catch (Fault e) { o.Say("   USB VALIDATION FAILED: HID interface " + F.Str(itf) + ": " + e.msg); o.Say("RESULT usb fail"); return false; }
                catch (Stop e) { o.Say("   USB VALIDATION FAILED: HID interface " + F.Str(itf) + ": " + e.msg); o.Say("RESULT usb fail"); return false; }
            }
            string reason = ValidateTabletUsb(d, cfg, reports, expectedInterval);
            if (reason != null) { o.Say("   USB VALIDATION FAILED: " + reason); o.Say("RESULT usb fail"); return false; }
            int vid = (d[8] & 0xFF) | ((d[9] & 0xFF) << 8), pid = (d[10] & 0xFF) | ((d[11] & 0xFF) << 8);
            o.Say("   device: VID " + F.HexDigits(vid, 4, true) + " PID " + F.HexDigits(pid, 4, true) + ", EP0 max packet " + F.Str(d[7] & 0xFF));
            int itfs = 0, hid = 0;
            StringBuilder eps = new StringBuilder();
            bool anyEp = false;
            int i = 0;
            while (i + 2 <= cfg.Length)
            {
                int L = cfg[i] & 0xFF, T = cfg[i + 1] & 0xFF;
                if (L == 0) break;
                if (T == 4)
                {
                    itfs++;
                    if ((cfg[i + 5] & 0xFF) == 3) hid++;
                    o.Say("   interface " + F.Str(cfg[i + 2] & 0xFF) + " class " + F.Str(cfg[i + 5] & 0xFF) + " sub " + F.Str(cfg[i + 6] & 0xFF) + " proto " + F.Str(cfg[i + 7] & 0xFF) + " eps " + F.Str(cfg[i + 4] & 0xFF));
                }
                if (T == 5)
                {
                    if (anyEp) eps.Append(",");
                    anyEp = true;
                    eps.Append(Hex2(cfg[i + 2]) + ":" + F.Str(cfg[i + 6] & 0xFF));
                    o.Say("     endpoint 0x" + Hex2(cfg[i + 2]) + " attr " + F.Str(cfg[i + 3] & 0xFF) + " maxpkt " + F.Str((cfg[i + 4] & 0xFF) | ((cfg[i + 5] & 0xFF) << 8)) + " bInterval " + F.Str(cfg[i + 6] & 0xFF));
                }
                i += L;
            }
            for (int k = 1; k <= 5; k++)
            {
                byte[] sd = host.GetDescriptor(3, k, 255, 0x0409, 0x80);
                int blen = sd.Length - 2;
                StringBuilder txt = new StringBuilder();
                bool stop = false;
                for (int j = 0; j + 1 < blen; j += 2)
                {
                    int ch = (sd[2 + j] & 0xFF) | ((sd[3 + j] & 0xFF) << 8);
                    if (ch == 0) { stop = true; }
                    if (!stop) txt.Append((char)ch);
                }
                if (!stop && blen > 0 && (blen & 1) != 0) txt.Append((char)0xFFFD);
                int nul = blen;
                for (int j2 = 0; j2 + 1 < blen; j2 += 2) if (sd[2 + j2] == 0 && sd[3 + j2] == 0) { nul = j2; break; }
                bool junk = false;
                for (int j3 = nul; j3 < blen; j3++) if (sd[2 + j3] != 0) junk = true;
                o.Say("   string " + F.Str(k) + ": \"" + Esc(txt.ToString()) + "\"" + (junk ? "  <-- non-zero bytes after the terminator" : ""));
            }
            // the flash-count readout (Patches.CounterString): USB string 173, absent (stalled) on stock firmware
            string cstr = "none";
            try
            {
                byte[] cs = host.GetDescriptor(3, Patches.COUNT_STRING, 255, 0x0409, 0x80);
                if (cs.Length >= 4 && (cs[1] & 0xFF) == 3)
                {
                    StringBuilder ct = new StringBuilder();
                    for (int j = 2; j + 1 < cs.Length && j < (cs[0] & 0xFF); j += 2) ct.Append((char)((cs[j] & 0xFF) | ((cs[j + 1] & 0xFF) << 8)));
                    cstr = ct.ToString();
                }
            }
            catch (Fault) { cstr = "none"; }
            catch (Stop) { cstr = "none"; }
            o.Say("   string " + F.Str(Patches.COUNT_STRING) + " (flash count): " + (P.Eq(cstr, "none") ? "not answered" : "\"" + Esc(cstr) + "\""));
            o.Say("RESULT countstring " + cstr);
            for (int itf = 0; itf < 3; itf++)
            {
                o.Say("   HID report descriptor, interface " + F.Str(itf) + ": " + F.Str(reports[itf].Length) + " bytes (validated)");
            }
            o.Say("RESULT usb " + F.HexDigits(vid, 4, true) + " " + F.HexDigits(pid, 4, true) + " interfaces=" + F.Str(itfs) + " hid=" + F.Str(hid) + " endpoints=" + eps.ToString());
            return true;
        }

        public static int AgcCheck(byte[] img, Out o)
        {
            int k = FindAll(img, new byte[] { 0x01, 0x21, 0x01, 0x71, 0x01, 0x70, 0x2d, 0x21, 0x41, 0x71 }, 0x3800, 0x10000).Length > 0
                ? FindAll(img, new byte[] { 0x01, 0x21, 0x01, 0x71, 0x01, 0x70, 0x2d, 0x21, 0x41, 0x71 }, 0x3800, 0x10000)[0] - 0x08000000 : -1;
            if (k < 0)
            {
                int[] k2 = FindAll(img, new byte[] { 0x01, 0x21, 0x01, 0x71, 0x01, 0x70 }, 0x3800, 0x10000);
                k = k2.Length > 0 ? k2[0] - 0x08000000 : -1;
            }
            if (k < 0) { o.Say("== AGC check: sample-count site not found"); o.Say("RESULT agc skip"); return -1; }
            int samples = img[k + 6] & 0xFF;
            int cap = 30;
            for (int j = 0x3800; j < LayoutOf(img).searchEnd - 0x08000000; j += 2)
            {
                if ((img[j] & 0xFF) == 0xd2 && (img[j + 1] & 0xFF) == 0xb2 && (img[j + 3] & 0xFF) == 0x2a && (img[j + 4] & 0xFF) == 0x10 && (img[j + 5] & 0xFF) == 0xd9 && (img[j + 7] & 0xFF) == 0x22)
                {
                    cap = img[j + 2] & 0xFF;
                    break;
                }
            }
            o.Say("== AGC check: samples/step = " + F.Str(samples) + ", AGC delay cap = " + F.Str(cap));
            StringBuilder bad = new StringBuilder();
            bool anyBad = false;
            for (int d = 0; d <= cap; d++)
            {
                Machine mc = new Machine(img, false, new TabletBoard(null), false);
                if (mc.Boot(2000000) != "ok") { o.Say("   (boot failed)"); o.Say("RESULT agc fail"); return 0; }
                for (int i = 1; i < 100; i++) mc.Poke(0x2000092C + 2 * i, 2, 1000);
                mc.Poke(0x20000698 + 5, 1, samples);
                mc.Poke(0x20000698 + 8, 1, d);
                bool brk = false;
                try
                {
                    if (mc.Call(Twin.LayoutOf(img).A(0x0800454C), 0x20001F00, 100000) == 0) { bad.Append("   AGC delay " + F.Str(d) + " -> zero (pen dropped)\n"); anyBad = true; }
                }
                catch (Fault) { bad.Append("   AGC delay " + F.Str(d) + " -> FAULT (input hangs)\n"); anyBad = true; brk = true; }
                catch (Stop) { bad.Append("   AGC delay " + F.Str(d) + " -> FAULT (input hangs)\n"); anyBad = true; brk = true; }
                if (brk) break;
            }
            if (!anyBad) o.Say("   ok: every AGC delay leaves at least one summed sample");
            string[] bl = P.Lines(bad.ToString());
            for (int i2 = 0; i2 < bl.Length; i2++) if (P.Len(bl[i2]) > 0) o.Say(bl[i2]);
            o.Say("RESULT agc " + (anyBad ? "fail" : "ok"));
            return anyBad ? 0 : 1;
        }

        public static Machine ScanMachine(byte[] img, Pen pen, bool rt, StringBuilder why)
        {
            Machine mc;
            try
            {
                mc = new Machine(img, true, new TabletBoard(pen), rt);
                string st = mc.Boot(3000000);
                if (!P.Eq(st, "ok")) { why.Append(st); return null; }
                mc.AttachUsb(true);
            }
            catch (Fault e) { why.Append(e.msg); return null; }
            catch (Stop e) { why.Append(e.msg); return null; }
            return mc;
        }

        // a report from a tracked pen: in range, or a calibration-pass telemetry report (sent only while tracking)
        public static bool Tracked(Report r)
        {
            if (r == null || r.ep != 2 || r.data == null || r.data.Length < 10 || (r.data[0] & 255) != 7) return false;
            if ((r.data[1] & 0x20) != 0) return true;
            int page = r.data[9] & 255;
            return (r.data[1] & 255) == 0xC0 && ((page >= 0xE0 && page <= 0xE3) || (page >= 0xF0 && page <= 0xF4));     // R42: line recorder pages
        }

        // Only normal in-range coordinates or the calibration position page contain genuine X/Y values.
        // Calibration signal, frequency and counter pages reuse the same bytes for unrelated readings.
        public static bool PositionReport(Report r)
        {
            if (!Tracked(r)) return false;
            if ((r.data[1] & 0x20) != 0) return true;
            return (r.data[9] & 255) == 0xE1 && (r.data[8] & 0x20) != 0;
        }

        public static int InRange(Machine mc, long since)
        {
            int n = 0;
            for (int i = 0; i < mc.reports.n; i++)
            {
                Report r = mc.reports.a[i];
                if (r.cycle >= since && Tracked(r)) n++;
            }
            return n;
        }

        public static SensResult SensJob(byte[] img, double f0, bool rt)
        {
            SensResult res = new SensResult();
            Pen pen = new Pen(1);
            pen.x = 16.3; pen.y = 9.6; pen.f0 = f0; pen.strength = 6200.0; pen.Q = 300.0;
            StringBuilder why = new StringBuilder();
            Machine mc = ScanMachine(img, pen, rt, why);
            if (mc == null) { res.why = "STOPPED (" + why.ToString() + ")"; return res; }
            res.mc = mc;
            try
            {
                long tAtt = mc.cpu.cycles;
                mc.RunMs(60);
                long t0 = mc.cpu.cycles;
                mc.RunMs(30);
                while (InRange(mc, t0) == 0 && mc.cpu.cycles - tAtt < 400L * 72000000L / 1000L) mc.RunMs(10);
                res.n = InRange(mc, t0);
            }
            catch (Fault e) { res.why = "STOPPED (" + e.msg + ")"; }
            catch (Stop e) { res.why = "STOPPED (" + e.msg + ")"; }
            return res;
        }

        public static double Sd(long[] v, int n)
        {
            double m = 0.0;
            for (int i = 0; i < n; i++) m += v[i];
            m = m / n;
            double acc = 0.0;
            for (int i = 0; i < n; i++) acc += (v[i] - m) * (v[i] - m);
            return P.Sqrt(acc / n);
        }

        public static Machine ScanReport(byte[] img, double ms, Out o, bool rt, bool[] okOut)
        {
            StringBuilder why = new StringBuilder();
            Machine mc = ScanMachine(img, new Pen(1), rt, why);
            okOut[0] = false;
            if (mc == null) { o.Say("== scan: STOPPED: " + why.ToString()); o.Say("RESULT scan fail"); return null; }
            long t0;
            try
            {
                mc.RunMs(50);
                t0 = mc.cpu.cycles;
                mc.RunMs(ms);
            }
            catch (Fault e) { o.Say("== scan: STOPPED at " + F.Fx((double)mc.cpu.cycles / 72000000.0 * 1e3, 1) + " ms: " + e.msg); o.Say("RESULT scan fail"); return mc; }
            catch (Stop e) { o.Say("== scan: STOPPED at " + F.Fx((double)mc.cpu.cycles / 72000000.0 * 1e3, 1) + " ms: " + e.msg); o.Say("RESULT scan fail"); return mc; }
            int nr = 0;
            long[] cyc = new long[mc.reports.n], xs = new long[mc.reports.n], ys = new long[mc.reports.n];
            for (int i = 0; i < mc.reports.n; i++)
            {
                Report r = mc.reports.a[i];
                if (r.cycle >= t0 && PositionReport(r))
                {
                    cyc[nr] = r.cycle;
                    xs[nr] = (r.data[2] & 0xFF) | ((r.data[3] & 0xFF) << 8);
                    ys[nr] = (r.data[4] & 0xFF) | ((r.data[5] & 0xFF) << 8);
                    nr++;
                }
            }
            o.Say("== scan: " + F.Fx(ms, 0) + " ms of tablet time (synthetic pen, host polls every " + F.Str(mc.hostInterval) + " ms)");
            double rate = 0.0, sx = 0.0, sy = 0.0;
            if (nr > 2)
            {
                double sum = 0.0, mn = 1e300, mx = -1e300;
                for (int i = 1; i < nr; i++)
                {
                    double iv = (double)(cyc[i] - cyc[i - 1]) / 72000000.0 * 1e3;
                    sum += iv;
                    if (iv < mn) mn = iv;
                    if (iv > mx) mx = iv;
                }
                double mean = sum / (nr - 1);
                rate = 1000 / mean;
                sx = Sd(xs, nr);
                sy = Sd(ys, nr);
                o.Say("   pen reports: " + F.Str(nr) + " -> " + F.Fx(rate, 0) + "/s, interval min " + F.Fx(mn, 2) + " ms max " + F.Fx(mx, 2) + " ms");
                o.Say("   position noise (synthetic): X sd " + F.Fx(sx, 1) + ", Y sd " + F.Fx(sy, 1) + " report units");
            }
            else o.Say("   no pen reports reached the host");
            double bsum = 0.0, bmax = -1e300;
            int bn = 0;
            long first = -1;
            for (int i = 0; i < TabletBoard.Of(mc).markCyc.n; i++)
            {
                long c = TabletBoard.Of(mc).markCyc.a[i];
                if (c < t0) continue;
                if (TabletBoard.Of(mc).markTag.a[i] == 0 && first < 0) first = c;
                else if (TabletBoard.Of(mc).markTag.a[i] == 1)
                {
                    if (first >= 0)
                    {
                        double b = (double)(c - first) / 72000000.0 * 1e6;
                        bsum += b; bn++;
                        if (b > bmax) bmax = b;
                    }
                    first = -1;
                }
            }
            if (bn > 0) o.Say("   frame work (first step -> end of processing): mean " + F.Fx(bsum / bn, 0) + " us, max " + F.Fx(bmax, 0) + " us");
            o.Say("RESULT scan reports=" + F.Str(nr) + " rate=" + F.Fx(rate, 1) + " sx=" + F.Fx(sx, 1) + " sy=" + F.Fx(sy, 1));
            okOut[0] = nr > 2;
            if (!okOut[0]) o.Say("RESULT scan fail");
            return mc;
        }

        // ---------------------------------------------------------------- scenarios ("0:h=1;150:h=12,x=10")
        public static string[] rampKey = new string[16];
        public static double[][] rampT = new double[16][], rampV = new double[16][];
        public static int[] rampN = new int[16];
        public static int rampKeys = 0;

        public static void ParseRamp(string text)
        {
            rampKeys = 0;
            string[] parts = P.Split(text, ';');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = P.Trim(parts[i]);
                if (P.Len(part) == 0) continue;
                int colon = P.IndexOf(part, ":");
                double t = P.ParseDouble(P.Sub(part, 0, colon));
                string[] items = P.Split(P.Sub(part, colon + 1, P.Len(part) - colon - 1), ',');
                for (int j = 0; j < items.Length; j++)
                {
                    int eq = P.IndexOf(items[j], "=");
                    string k = P.Trim(P.Sub(items[j], 0, eq));
                    double v = P.ParseDouble(P.Sub(items[j], eq + 1, P.Len(items[j]) - eq - 1));
                    int slot = -1;
                    for (int q = 0; q < rampKeys; q++) if (P.Eq(rampKey[q], k)) slot = q;
                    if (slot < 0) { slot = rampKeys++; rampKey[slot] = k; rampT[slot] = new double[64]; rampV[slot] = new double[64]; rampN[slot] = 0; }
                    rampT[slot][rampN[slot]] = t; rampV[slot][rampN[slot]] = v; rampN[slot]++;
                }
            }
        }

        public static double RampAt(int slot, double t)
        {
            double[] ts = rampT[slot], vs = rampV[slot];
            int n = rampN[slot];
            if (t <= ts[0]) return vs[0];
            for (int i = 0; i + 1 < n; i++)
            {
                if (t <= ts[i + 1]) return ts[i + 1] > ts[i] ? vs[i] + (vs[i + 1] - vs[i]) * (t - ts[i]) / (ts[i + 1] - ts[i]) : vs[i + 1];
            }
            return vs[n - 1];
        }

        public static void ApplyRamp(Pen pen, double t)
        {
            for (int s = 0; s < rampKeys; s++)
            {
                double v = RampAt(s, t);
                string k = rampKey[s];
                if (P.Eq(k, "h")) { pen.height = v; pen.hasHeight = true; }
                else if (P.Eq(k, "x")) pen.x = v;
                else if (P.Eq(k, "y")) pen.y = v;
                else if (P.Eq(k, "f0")) pen.f0 = v;
                else if (P.Eq(k, "s")) pen.strength = v;
                else if (P.Eq(k, "present")) pen.present = v >= 0.5;
            }
        }

        public static string WatchText(Machine mc, int[] watch, int nw)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < nw; i++) { if (i > 0) sb.Append(" "); sb.Append(F.Hx(watch[i], 8) + "=" + F.Str(mc.Peek(watch[i], 1))); }
            return sb.ToString();
        }

        public static Machine RunScenario(byte[] img, double ms, double every, int[] watch, int nw, Pen pen, Out o, bool rt)
        {
            ApplyRamp(pen, 0.0);
            StringBuilder why = new StringBuilder();
            Machine mc = ScanMachine(img, pen, rt, why);
            if (mc == null) { o.Say("== run: STOPPED: " + why.ToString()); o.Say("RESULT run fail"); return null; }
            o.Say("== run: " + F.Fx(ms, 0) + " ms, report every " + F.Fx(every, 0) + " ms");
            int[] last = new int[nw];
            for (int i = 0; i < nw; i++) last[i] = mc.Peek(watch[i], 1);
            int ev = (int)every;
            try
            {
                for (int t = 1; t <= (int)ms; t++)
                {
                    ApplyRamp(pen, (double)t);
                    mc.RunMs(1);
                    bool changed = false;
                    for (int i = 0; i < nw; i++) if (mc.Peek(watch[i], 1) != last[i]) changed = true;
                    if (changed || t % ev == 0)
                    {
                        int nrep = 0, ir = 0;
                        Report lastr = null;
                        for (int i = 0; i < mc.reports.n; i++)
                        {
                            Report r = mc.reports.a[i];
                            if (r.ep != 2) continue;
                            nrep++;
                            if ((r.data[1] & 0x20) != 0) ir++;
                            lastr = r;
                        }
                        string tail = "";
                        if (lastr != null)
                        {
                            byte[] d = lastr.data;
                            tail = " last " + Hex2(d[1]) + " x=" + F.Str((d[2] & 0xFF) | ((d[3] & 0xFF) << 8)) + " y=" + F.Str((d[4] & 0xFF) | ((d[5] & 0xFF) << 8)) + " p=" + F.Str((d[6] & 0xFF) | ((d[7] & 0xFF) << 8));
                        }
                        string h = pen.hasHeight ? " h=" + F.Fx(pen.height, 2) : "";
                        o.Say("   t=" + F.Str(t) + h + " reports=" + F.Str(nrep) + " inrange=" + F.Str(ir) + tail + (nw > 0 ? " " + WatchText(mc, watch, nw) : "") + (changed ? "  <-- changed" : ""));
                        for (int i = 0; i < nw; i++) last[i] = mc.Peek(watch[i], 1);
                    }
                }
            }
            catch (Fault e) { o.Say("   STOPPED: " + e.msg); o.Say("RESULT run fail"); return mc; }
            catch (Stop e) { o.Say("   STOPPED: " + e.msg); o.Say("RESULT run fail"); return mc; }
            int total = 0, inr = 0;
            for (int i = 0; i < mc.reports.n; i++) if (mc.reports.a[i].ep == 2) { total++; if ((mc.reports.a[i].data[1] & 0x20) != 0) inr++; }
            int half0 = total / 2, idx = 0, hn = 0, tipn = 0;
            long[] tip = new long[total + 1];
            for (int i = 0; i < mc.reports.n; i++)
            {
                Report r = mc.reports.a[i];
                if (r.ep != 2) continue;
                if (idx >= half0)
                {
                    hn++;
                    if ((r.data[1] & 0x21) == 0x21) tip[tipn++] = (r.data[6] & 0xFF) | ((r.data[7] & 0xFF) << 8);
                }
                idx++;
            }
            if (hn > 0)
            {
                double m = 0.0; long mn = long.MaxValue, mx = 0;
                for (int i = 0; i < tipn; i++) { m += tip[i]; if (tip[i] < mn) mn = tip[i]; if (tip[i] > mx) mx = tip[i]; }
                m = tipn > 0 ? m / tipn : 0.0;
                double sd = tipn > 0 ? Sd(tip, tipn) : 0.0;
                o.Say("   second half: " + F.Str(hn) + " reports, tip down in " + F.Str(tipn) + ", pressure mean " + F.Fx(m, 0) + " sd " + F.Fx(sd, 0) + " min " + F.Str(tipn > 0 ? mn : 0) + " max " + F.Str(tipn > 0 ? mx : 0));
            }
            o.Say("RESULT run reports=" + F.Str(total) + " inrange=" + F.Str(inr));
            return mc;
        }

        public static bool HoverPickup(byte[] img, double h, bool rt)
        {
            Pen pen = new Pen(1);
            pen.f0 = 505000.0; pen.Q = 300.0; pen.height = h; pen.hasHeight = true; pen.integrate = receiverIntegrate;
            StringBuilder why = new StringBuilder();
            Machine mc = ScanMachine(img, pen, rt, why);
            if (mc == null) return false;
            try
            {
                mc.RunMs(60);
                while (InRange(mc, 0) == 0 && mc.cpu.cycles < 400L * 72000000L / 1000L) mc.RunMs(10);
            }
            catch (Fault) { return false; }
            catch (Stop) { return false; }
            return InRange(mc, 0) > 0;
        }

        public static bool NewInRange(Machine mc, int seen)
        {
            for (int i = seen; i < mc.reports.n; i++) { if (Tracked(mc.reports.a[i])) return true; }
            return false;
        }

        public static void HoverTest(byte[] img, int[] watch, int nw, Out o, double hmax, double speed, bool rt)
        {
            o.Say("== hover range (physical pen: coil plane " + F.Fx(Pen.GAP_MM, 1) + " mm under the surface, pen coil " + F.Fx(Pen.COIL_MM, 1) + " mm above the tip, Q 300, 505 kHz)");
            double pick;
            if (!HoverPickup(img, 0.0, rt)) { pick = 0.0; o.Say("   a still pen is NOT picked up even on the surface"); }
            else if (HoverPickup(img, hmax, rt)) { pick = hmax; o.Say("   a still pen is picked up even at " + F.Fx(hmax, 1) + " mm (the test limit)"); }
            else
            {
                double lo = 0.0, hi = hmax;
                for (int i = 0; i < 9; i++) { double mid = (lo + hi) / 2; if (HoverPickup(img, mid, rt)) lo = mid; else hi = mid; }
                pick = lo;
                o.Say("   a still pen is picked up from up to " + F.Fx(pick, 1) + " mm above the surface");
            }
            Pen pen = new Pen(1);
            pen.f0 = 505000.0; pen.Q = 300.0; pen.height = 1.0; pen.hasHeight = true; pen.integrate = receiverIntegrate;
            StringBuilder why = new StringBuilder();
            Machine mc = ScanMachine(img, pen, rt, why);
            if (mc == null) { o.Say("   STOPPED: " + why.ToString()); o.Say("RESULT hover fail"); return; }
            double hold = -1.0, found = -1.0;
            try
            {
                mc.RunMs(150);
                int[] last = new int[nw];
                for (int i = 0; i < nw; i++) last[i] = mc.Peek(watch[i], 1);
                int seen = mc.reports.n, quiet = 0;
                double h = 1.0;
                while (h < hmax)
                {
                    h += speed / 1000;
                    pen.height = h;
                    mc.RunMs(1);
                    if (NewInRange(mc, seen)) { hold = h; quiet = 0; } else quiet++;
                    seen = mc.reports.n;
                    for (int i = 0; i < nw; i++)
                    {
                        int v = mc.Peek(watch[i], 1);
                        if (v != last[i]) o.Say("   raising: " + F.Hx(watch[i], 8) + " " + F.Str(last[i]) + " -> " + F.Str(v) + " at " + F.Fx(h, 1) + " mm");
                        last[i] = v;
                    }
                    if (hold >= 0 && quiet > 300 && h > hold + 2) break;
                }
                o.Say("   raising " + F.Fx(speed, 0) + " mm/s: pen held up to " + F.Fx(hold, 1) + " mm");
                while (h > 0.0)
                {
                    h = h - speed / 1000 > 0.0 ? h - speed / 1000 : 0.0;
                    pen.height = h;
                    mc.RunMs(1);
                    if (found < 0 && NewInRange(mc, seen)) { found = h; o.Say("   lowering: pen found at " + F.Fx(h, 1) + " mm"); }
                    seen = mc.reports.n;
                    for (int i = 0; i < nw; i++)
                    {
                        int v = mc.Peek(watch[i], 1);
                        if (v != last[i]) o.Say("   lowering: " + F.Hx(watch[i], 8) + " " + F.Str(last[i]) + " -> " + F.Str(v) + " at " + F.Fx(h, 1) + " mm");
                        last[i] = v;
                    }
                }
            }
            catch (Fault e) { o.Say("   STOPPED: " + e.msg); o.Say("RESULT hover fail"); return; }
            catch (Stop e) { o.Say("   STOPPED: " + e.msg); o.Say("RESULT hover fail"); return; }
            o.Say("RESULT hover pickup=" + F.Fx(pick, 1) + " hold=" + F.Fx(hold, 1) + " found=" + F.Fx(found, 1));
            if (pick < 0 || hold < 0 || found < 0) o.Say("RESULT hover fail");
        }

        public static bool Selftest(Out o)
        {
            o.Say("== selftest");
            Rng rng = new Rng(1);
            o.Say("   random " + F.Fx(rng.Random(), 15) + " " + F.Fx(rng.Random(), 15) + " " + F.Fx(rng.Random(), 15));
            StringBuilder g = new StringBuilder("   gauss");
            for (int i = 0; i < 5; i++) g.Append(" " + F.Fx(rng.Gauss(0, 6.0), 12));
            o.Say(g.ToString());
            o.Say("   f2b " + F.Hx(P.FloatBits(1.0 / 3.0), 8) + " " + F.Hx(P.FloatBits(505000.0 / 7.0), 8) + " " + F.Hx(P.FloatBits(-2.5e-8), 8) + " " + F.Hx(P.FloatBits(3.4e38), 8));
            o.Say("   fx " + F.Fx(0.25, 1) + " " + F.Fx(0.35, 1) + " " + F.Fx(-1.05, 1) + " " + F.Fx(2.5, 1) + " " + F.Fx(1e-9, 1));
            Rng check = new Rng(1);
            bool ok = check.Random() == 0.13436424411240122 && check.Random() == 0.8474337369372327
                && P.FloatBits(1.0 / 3.0) == 0x3EAAAAAB && P.FloatBits(505000.0 / 7.0) == 0x478CE76E
                && P.FloatBits(-2.5e-8) == unchecked((int)0xB2D6BF95) && P.FloatBits(3.4e38) == 0x7F7FC99E
                && P.Rint(2.5) == 2 && P.Rint(3.5) == 4;
            if (!ok) { o.Say("RESULT selftest fail"); return false; }
            o.Say("RESULT selftest done");
            return true;
        }

        public static void ReportSpeed(Machine mc)
        {
            if (mc != null && mc.wall > 0)
                P.Line("# twin speed: " + P.Fixed3((double)mc.cpu.cycles / 72000000.0 / mc.wall) + "x real time (" + P.Fixed3((double)mc.cpu.cycles / mc.wall / 1e6) + " M cycles/s)");
        }

        public const string USAGE = "AnargonsTabletTools twin IMAGE [--checks boot,usb,agc,sens,scan] [--scan MS] [--freqs F,...] [--speed uncapped|72mhz]\n"
            + "  --run MS [--ramp \"0:h=1;150:h=12\"] [--every MS] [--watch ADDR,...]   scripted run\n"
            + "  --hover [--watch ADDR,...] [--hover-max MM] [--hover-speed MMS]       hover range (physical pen)\n"
            + "  --measure                                                           frame timing and position precision\n"
            + "  --persist FILE  keep the bootloader area (flash counter) between runs; --boot-wp  write-protect it\n"
            + "  --pen-x/-y/-f0/-q/-strength/-noise/-seed/-height N, --pen-model legacy|resonator, --receiver fade|integrate\n"
            + "  --selftest\n"
            + "--speed uncapped runs as fast as the PC allows; 72mhz paces the emulation to the real chip. Both give the same results.";

        public static int Run(string[] argv)
        {
            string checks = null, speed = "uncapped", ramp = "", image = null, model = "legacy";
            double scanMs = 0, runMs = 0, every = 10, hoverMax = 30, hoverSpeed = 20;
            bool hover = false, selft = false, measure = false, calsim = false;
            string persist = null;
            int blErase = 0, blState = -1; bool blBoot = false;
            int[] watch = new int[32]; int nw = 0;
            double[] freqs = null;
            Pen pen = new Pen(1);
            bool hasH = false; double hval = 0; long seed = 1;
            try
            {
                for (int i = 0; i < argv.Length; i++)
                {
                    string a = argv[i];
                    if (P.Eq(a, "-h") || P.Eq(a, "--help")) { P.Line(USAGE); return 0; }
                    if (P.Eq(a, "--selftest")) { selft = true; continue; }
                    if (P.Eq(a, "--hover")) { hover = true; continue; }
                    if (P.Eq(a, "--measure")) { measure = true; continue; }
                    if (P.Eq(a, "--calibrate-sim")) { calsim = true; continue; }
                    if (P.Eq(a, "--boot-wp")) { Machine.bootWp = true; continue; }
                    if (P.Eq(a, "--bl-boot")) { blBoot = true; continue; }
                    if (P.StartsWith(a, "-"))
                    {
                        if (i + 1 >= argv.Length) throw new Stop(a);
                        string v = argv[++i];
                        if (P.Eq(a, "--checks")) checks = v;
                        else if (P.Eq(a, "--speed")) speed = v;
                        else if (P.Eq(a, "--ramp")) ramp = v;
                        else if (P.Eq(a, "--scan")) scanMs = P.ParseDouble(v);
                        else if (P.Eq(a, "--run")) runMs = P.ParseDouble(v);
                        else if (P.Eq(a, "--every")) every = P.ParseDouble(v);
                        else if (P.Eq(a, "--hover-max")) hoverMax = P.ParseDouble(v);
                        else if (P.Eq(a, "--hover-speed")) hoverSpeed = P.ParseDouble(v);
                        else if (P.Eq(a, "--watch")) { string[] ws = P.Split(v, ','); for (int k = 0; k < ws.Length; k++) if (P.Len(ws[k]) > 0) watch[nw++] = (int)F.ParseInt(ws[k]); }
                        else if (P.Eq(a, "--freqs")) { string[] fs = P.Split(v, ','); freqs = new double[fs.Length]; for (int k = 0; k < fs.Length; k++) freqs[k] = P.ParseDouble(fs[k]); }
                        else if (P.Eq(a, "--pen-x")) pen.x = P.ParseDouble(v);
                        else if (P.Eq(a, "--pen-y")) pen.y = P.ParseDouble(v);
                        else if (P.Eq(a, "--pen-f0")) pen.f0 = P.ParseDouble(v);
                        else if (P.Eq(a, "--pen-q")) pen.Q = P.ParseDouble(v);
                        else if (P.Eq(a, "--pen-strength")) pen.strength = P.ParseDouble(v);
                        else if (P.Eq(a, "--pen-noise")) pen.noise = P.ParseDouble(v);
                        else if (P.Eq(a, "--pen-seed")) seed = F.ParseInt(v);
                        else if (P.Eq(a, "--pen-height")) { hasH = true; hval = P.ParseDouble(v); }
                        else if (P.Eq(a, "--pen-model")) model = v;
                        else if (P.Eq(a, "--receiver")) receiverIntegrate = P.Eq(v, "integrate");
                        else if (P.Eq(a, "--persist")) persist = v;
                        else if (P.Eq(a, "--bl-erase")) blErase = (int)F.ParseInt(v);
                        else if (P.Eq(a, "--bl-state")) { string[] ft = P.Split(v, ','); blState = ((int)F.ParseInt(ft[0]) << 8) | (int)F.ParseInt(ft[1]); }
                        else throw new Stop(a);
                        continue;
                    }
                    image = a;
                }
            }
            catch (Exception e) { P.Line("bad arguments (" + P.Msg(e) + "); see --help"); return 2; }
            Out o = new Out();
            if (!P.Eq(speed, "uncapped") && !P.Eq(speed, "72mhz")) { P.Line("--speed must be uncapped or 72mhz"); return 2; }
            if (checks != null)
            {
                string[] requested = P.Split(checks, ',');
                for (int i = 0; i < requested.Length; i++)
                    if (P.Len(requested[i]) == 0 || P.IndexOf(",boot,usb,agc,sens,scan,", "," + requested[i] + ",") < 0)
                    { P.Line("Unknown check: " + requested[i] + ". Use boot,usb,agc,sens,scan."); return 2; }
            }
            if (!(every >= 1 && every <= 2147483647.0) || !(hoverSpeed > 0 && hoverSpeed <= 2147483647.0))
            { P.Line("--every must be at least 1 ms and --hover-speed must be positive; both must be finite."); return 2; }
            if (!(scanMs >= 0 && scanMs <= 2147483647.0) || !(runMs >= 0 && runMs <= 2147483647.0) || !(hoverMax > 0 && hoverMax <= 2147483647.0))
            { P.Line("Run lengths must be finite and non-negative, and --hover-max must be positive."); return 2; }
            bool rt = P.Eq(speed, "72mhz");
            if (selft) { if (!Selftest(o)) return 1; if (image == null) return 0; }
            if (image == null) { P.Line(USAGE); return 2; }
            byte[] img = LoadImage(image);
            if (persist != null && P.FileExists(persist)) { byte[] keep = P.ReadAllBytes(persist); P.Copy(keep, 0, img, 0, keep.Length < 0x3800 ? keep.Length : 0x3800); }
            if (blErase != 0 || blBoot || blState >= 0)
            {
                // the bootloader's own code on this image: its page erase (as an update does), or a power-on from reset
                Machine bm = new Machine(img, false, new TabletBoard(null), false);
                img = bm.flash;                         // state transitions and persistence use this machine's flash
                bool bootOk = true;
                try
                {
                    if (blState >= 0)
                    {
                        // the boot state as it is in flash, then the bootloader's own set_boot_state(new state)
                        int from = blState >> 8, to = blState & 0xFF;
                        img[0x3400] = (byte)from; img[0x3401] = 0; img[0x3402] = 0; img[0x3403] = 0;
                        bm.cpu.r[13] = 0x20001600;
                        bm.Call(0x08000F40, to, 4000000);
                        o.Say("   boot state " + F.Str(from) + " -> " + F.Str(to) + " (now " + F.Str(img[0x3400] & 0xFF) + "); flash counter " + F.Str(Build.ReadFlashCount(img)));
                    }
                    else if (blErase != 0)
                    {
                        bm.FmcWrite(0x40022004, 0x45670123); bm.FmcWrite(0x40022004, unchecked((int)0xCDEF89AB));
                        bm.cpu.r[13] = 0x20001600;
                        bm.Call(0x080009B4, blErase, 2000000);
                        o.Say("   bootloader erased " + F.Hx(blErase, 8) + "; flash counter now " + F.Str(Build.ReadFlashCount(img)));
                    }
                    else
                    {
                        Cpu c = bm.cpu;
                        bm.vtor = 0x08000000;
                        c.r[13] = Machine.LE(img, 0, 4); c.r[15] = Machine.LE(img, 4, 4) & ~1; c.r[14] = -1;
                        long n = 0;
                        while (n < 5000000 && (c.r[15] & 0xFFFFFFFFL) < 0x08003800L) { c.Step(); n++; }
                        bootOk = (c.r[15] & 0xFFFFFFFFL) >= 0x08003800L && (c.r[15] & 0xFFFFFFFFL) < 0x08010000L;
                        o.Say((c.r[15] & 0xFFFFFFFFL) >= 0x08003800L ? "   bootloader started the firmware at " + F.Hx(c.r[15], 8) + " after " + F.Str(n) + " instructions" : "   bootloader did NOT reach the firmware");
                    }
                }
                catch (Fault e) { o.Say("   STOPPED: " + e.msg); bootOk = false; }
                catch (Stop e) { o.Say("   STOPPED: " + e.msg); bootOk = false; }
                if (persist != null) { byte[] keep = new byte[0x3800]; P.Copy(img, keep, 0x3800); P.WriteAllBytes(persist, keep); }
                return bootOk ? 0 : 1;
            }
            if (runMs > 0)
            {
                Pen mk = new Pen(seed);
                mk.x = pen.x; mk.y = pen.y; mk.f0 = pen.f0; mk.Q = pen.Q; mk.strength = pen.strength; mk.noise = pen.noise;
                mk.hasHeight = hasH; mk.height = hval; mk.resonator = P.Eq(model, "resonator"); mk.integrate = receiverIntegrate;
                ParseRamp(ramp);
                for (int s = 0; s < rampKeys; s++) if (P.Eq(rampKey[s], "h") && !mk.hasHeight) { mk.hasHeight = true; mk.height = RampAt(s, 0.0); }
                Machine mc = RunScenario(img, runMs, every, watch, nw, mk, o, rt);
                if (mc != null) img = mc.flash;
                if (persist != null)
                {
                    byte[] keep = new byte[0x3800]; P.Copy(img, keep, 0x3800); P.WriteAllBytes(persist, keep);
                    o.Say("   flash counter in the bootloader area: " + F.Str(Build.ReadFlashCount(img)) + " (status " + F.Str(mc != null ? mc.Peek(0x20000F3C, 1) : -1) + ", bootloader step " + F.Str(mc != null ? mc.Peek(0x20000F3D, 1) : -1) + ")");
                }
                ReportSpeed(mc);
                return !o.failed && mc != null ? 0 : 1;
            }
            if (hover) { HoverTest(img, watch, nw, o, hoverMax, hoverSpeed, rt); return o.failed ? 1 : 0; }
            if (measure) { measStrength = pen.strength != 5200.0 ? pen.strength : 60000.0; measNoise = pen.noise; Measure(img, o, rt); return o.failed ? 1 : 0; }
            if (calsim) return Cli.SimCalibrate(img, image) ? 0 : 1;
            string cl = checks != null ? checks : "boot,usb,agc,sens" + (scanMs > 0 ? ",scan" : "");
            bool passed = true;
            if (P.IndexOf("," + cl + ",", ",boot,") >= 0 || P.IndexOf("," + cl + ",", ",usb,") >= 0) passed = Describe(img, image, o);
            if (passed && P.IndexOf("," + cl + ",", ",agc,") >= 0) passed = AgcCheck(img, o) == 1;
            if (passed && P.IndexOf("," + cl + ",", ",sens,") >= 0)
            {
                o.Say("== detection sensitivity (weak pen that the build proven on the tablet still tracks)");
                double[] fl = freqs != null ? freqs : SENS_FREQS;
                for (int i = 0; i < fl.Length; i++)
                {
                    SensResult r = SensJob(img, fl[i], rt);
                    bool good = r.n > 0;
                    passed = passed && good;
                    string nt = r.n >= 0 ? F.Str(r.n) : r.why;
                    o.Say("   Q 300 strength 6200 at " + F.Fx(fl[i] / 1000, 0) + " kHz: " + nt + " in-range reports -> " + (good ? "ok" : "PEN NOT DETECTED"));
                    o.Say("RESULT sens " + F.Fx(fl[i], 0) + " " + (r.n >= 0 ? F.Str(r.n) : "STOPPED"));
                    ReportSpeed(r.mc);
                }
            }
            if (passed && P.IndexOf("," + cl + ",", ",scan,") >= 0)
            {
                bool[] ok = new bool[1];
                Machine mc2 = ScanReport(img, scanMs > 0 ? scanMs : 60.0, o, rt, ok);
                passed = ok[0];
                ReportSpeed(mc2);
            }
            o.Say("RESULT " + (passed ? "pass" : "fail"));
            return passed ? 0 : 1;
        }
    }
}
