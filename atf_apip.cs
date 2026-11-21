// atf_apip.cs (R44) - Algorithmic Position Interpolation (APIP), Anargon's design: pen noise is mostly predictable, so
// the tool learns it on the user's PC from the user's own tablet and the firmware runs the result.
//
// It replaces Position Interpolation and Movement Smoothing, and (R45) Jump Rejection (all off in an APIP build). Two parts, both per
// RESONANCE BAND (the strength of the pen's signal, which follows the pen's height) and per axis:
//   1. Coil-distance correction (no delay): the position the stock arithmetic reports bends a little between two
//      coils, the same way at every coil. Moving pens visit every distance from the coil centres equally often, so the
//      share of readings at each distance shows the bend; the correction undoes it (histogram equalisation over one
//      coil pitch, 16 segments, linear in between). Distances are measured from report position 0 (the origin does not
//      matter, only the pitch: 5.080 mm on X, 5.083 mm on Y, R43).
//   2. Noise filter: an alpha-beta tracker. It predicts the next position from the last position and speed, and moves
//      towards each reading by alpha (speed by beta). For a pen at a steady speed its prediction is exact, so it adds
//      no steady delay (a moving average always trails). Its gains come from the measured noise of each band (the noise
//      gradient over resonance: weaker signal, more noise) and from how the user's pen accelerates. A reading far off the
//      prediction (more than 2.5 x the noise) blends towards quick gains (alpha 0.5), up to fully at 6 x, so a sudden
//      turn is followed at once.
//      R45: the gate opens only when three readings in a row are off to the same side (it sees the smallest of the
//      three); fewer that far off (the tablet's noise has spikes, often two in a row: in the twin 2.5 % of hover
//      readings are 6 x off) count as 2.5 x. This also replaces Jump Rejection, which ran before APIP and turned such
//      a spike into a 3-reading ramp of millimetres (it coasts on the speed the spike gave it) that APIP then followed. And the noise the
//      gate is measured against is the noise now: the firmware keeps its own average of the readings' second
//      differences (a pen's own movement adds almost nothing to them at these rates), because within one band the
//      noise still changes 3 x with the height (twin). Touching has its own band (by pressure): the twin's signal
//      level cannot tell touching from a low hover.
//   3. Still-pen gains (R45, Anargon: "dynamic gain capability for movement"): while the pen is still or moves steadily,
//      the tracker's residuals average to nothing, so it slides (in 20 ms) towards much smaller gains: a longer memory
//      and less shake. A steady movement still has no delay, because the tracker follows a steady speed exactly. The
//      average of the residuals (1/8 per reading) passing 1.5 x the noise, or a fully open gate, means the pen changed
//      its movement: the moving gains return at once. The still gains come from the centre hold (how much a hand
//      holding still drifts against the noise; none measurable: a memory of 100 ms).
// Training (Cli.TrainApip): a training firmware (normal cursor, so the tablet driver and any program keep working)
// records every frame's position, signal level, pressure and hover stage in RAM (R45-rc2: also the detection signal
// and the pen's frequency, so the same readings measure what Tablet signal thresholds measures: Cli.FromTraining); the
// PC reads them through USB string
// 180 (Windows: HidD_GetIndexedString, which needs no access rights and works while the driver reads the tablet). The
// CLI shows a table of 20 x 20 mm areas for three resonance bands (touching, low hover, high hover); a cell turns into
// OK when that area has enough moving readings in that band. Then a short centre hold (the +-7 mm LED box of the
// calibration steps) anchors the noise gradient. The result is apip-<preset>.txt, built into the firmware as a table.
// Every APIP build is checked in the twin: the firmware's filter must equal its C# copy (Apip.Axis) on every frame.
using System;
using System.Text;
using Gd32Emu;

namespace AnargonsTabletTools
{
    // one trained model, as the firmware table holds it (integers), and what it was trained on
    public class ApipModel
    {
        public string preset = "", trained = "", file = null;
        public bool fullRes = true, fallback = false;                 // R45: fallback = Anargon's training (Apip.Fallback)
        public int rate = 0;
        public int[] thr = new int[Apip.NB - 1];                      // level thresholds: band = how many are <= level
        public int[] A = new int[Apip.NBT * 2], B = new int[Apip.NBT * 2];      // moving gains: alpha (Q12), beta (Q16)
        // R45: still-pen gains (Q12, Q16), the noise to start from (N0: the firmware's noise average, report units:
        // NOISE_N x sigma), and the step per reading towards the still gains (256 = all the way)
        public int[] AS = new int[Apip.NBT * 2], BS = new int[Apip.NBT * 2], N0 = new int[Apip.NBT * 2], STP = new int[Apip.NBT * 2];
        public int[][] W = new int[Apip.NBT * 2][];                   // coil-distance correction, 17 points, 1/16 unit
        public double[] sigma = new double[Apip.NBT * 2];             // measured noise (report units), for the file
        public int[] count = new int[Apip.NBT * 2];
        public long samples = 0, moving = 0, hold = 0;
        // R45-rc2: the fast stage's pick-up and drop levels the training measured in the background (Cli.FromTraining),
        // in rounded readings like a calibration file; -1 = none (Anargon's fallback training, or an R45-rc1 file)
        public int pick = -1, drop = -1;
        // R45: the filter a table holds (FromTable): 0 = APIP's tracker, 2-4 = the robust filter (atf_apipr.cs) with its
        // constants
        public int models = 0;
        public RobustK rk = null;
        public ApipModel() { for (int i = 0; i < W.Length; i++) W[i] = new int[Apip.SEG + 1]; }
    }

    // the filter state of one axis (Q8 report units) and the frame's inputs, as in RAM (Apip.ST)
    // R45: e = residual average, ts = stillness (0..256), rp / rp2 = the last two residuals, n = noise average, z1 / z2
    // = the last two corrected readings (all Q8 but ts)
    public class ApipAxis { public int x, v, e, ts, rp, rp2, n, z1, z2; }

    // training samples (one per frame with the pen in range), in order; seq counts frames as the firmware numbered them
    public class ApipData
    {
        public int n = 0;
        public int[] x = new int[4096], y = new int[4096], lvl = new int[4096], aux = new int[4096];
        // R45-rc2: the detection signal (the weaker of the strongest X and Y line's last sample, as the calibration
        // readout's signal page; 0 = not measured this frame) and the pen's frequency field (Hz)
        public int[] sig = new int[4096], fq = new int[4096];
        public long[] seq = new long[4096];
        public long last = -1;
        public int reads = 0, failed = 0;
        public void Add(long s, int xx, int yy, int l, int a) { Add(s, xx, yy, l, a, 0, 0); }
        public void Add(long s, int xx, int yy, int l, int a, int sg, int f)
        {
            if (n == x.Length)
            {
                int m = n * 2;
                int[] g; long[] gs;
                g = new int[m]; P.Copy(x, g, n); x = g; g = new int[m]; P.Copy(y, g, n); y = g;
                g = new int[m]; P.Copy(lvl, g, n); lvl = g; g = new int[m]; P.Copy(aux, g, n); aux = g;
                g = new int[m]; P.Copy(sig, g, n); sig = g; g = new int[m]; P.Copy(fq, g, n); fq = g;
                gs = new long[m]; for (int i = 0; i < n; i++) gs[i] = seq[i]; seq = gs;
            }
            x[n] = xx; y[n] = yy; lvl[n] = l; aux[n] = a; sig[n] = sg; fq[n] = f; seq[n] = s; n++;
        }
        // R45-rc2: the pen's frequency of sample i in Hz (16 Hz steps), or 0 when the firmware had none
        public long Hz(int i) { return fq[i] > 0 ? (fq[i] + (long)Apip.FREQ_BASE) * 16 : 0; }
        public int Pressure(int i) { return (aux[i] & 0xFFF) << 2; }
        public int Stage(int i) { return (aux[i] >> 12) & 3; }
        // sample i and the one BACK frames earlier are from one unbroken stretch of frames
        public bool Linked(int i, int back) { return i - back >= 0 && seq[i] - seq[i - back] == back; }
    }

    public static class Apip
    {
        // bands: NB hover levels (fast stage), then (R45) touching, Steady Hover, Reach Stage; segments per pitch
        public const int NB = 6, NBT = 9, SEG = 16;
        public const int STRING = 180;                                // USB string: training read-out
        // RAM (free: stock and every other block of ours leave 0x20001070-0x20001F7F unused)
        //   ST    filter: +0 x, +4 vx, +8 y, +12 vy (s32, Q8 of 1/100 mm), +16 out x, +18 out y, +20 WARM when valid,
        //         +21 unit (R45: 1 = positions in 1/200 mm, the XP-Pen driver's report mode 4; 0 = 1/100 mm);
        //         +22 touching (R45), +24 zx, +26 zy, +28 level, +30 stage, +31 path (0 no pen, 1 no new reading,
        //         2 filtered) of the last frame (+16 / +18 / +24 / +26 in the report's own unit)
        //         R45, each axis at its state (x +0, y +8) + 32 e, + 36 ts, + 48 rp, + 52 n, + 64 z1, + 68 z2, + 80 rp2
        //         (s32, see ApipAxis); so x at +32 ... +83, y at +40 ... +91
        //   RAM   training ring: +0 written (u16), +2 read (u16), +4 MAGIC when set up
        //   RING  256 samples of 12 bytes (R45-rc2; 8 before): x, y (the report's unit), level, pressure / 4 | stage << 12
        //         | unit << 14 (R45: UNIT_FLAG = positions in 1/200 mm, the XP-Pen driver's report mode 4), then the
        //         detection signal and the pen's frequency / 16 - FREQ_BASE (0 = none)
        //   DESC  the read-out descriptor (up to 244 bytes)
        // R45-rc2: the ring and the read-out now reach 0x20001DF3, over the robust filter's RAM (atf_apipr.cs ROB). A
        // training build has no filter (Build.BuildOne: no ALPI filter with apipTrain), so the two are never in one build.
        public const int ST = 0x20001080, RAM = 0x200010F0, RING = 0x20001100, RING_N = 256, SAMPLE = 12, DESC = 0x20001D00;
        public const int MAGIC = 0xA9E1, WARM = 0xA7, PER_READ = 20, KEEP = RING_N - 8, UNITS = 6;
        // R45-rc2: the frequency field counts 16 Hz steps from 393.216 kHz, so the pen's 457-540 kHz fit in 15 bits
        public const int FREQ_BASE = 0x6000;
        public const int TAB_MAGIC = 0x50495041, TRAIN_MAGIC = 0x54495041;   // "APIP", "APIT"
        public const int REC = 48, TAB_BYTES = 32 + NBT * 2 * REC + 4;    // + the routine's address (Sched.Patch), for the twin's timing
        public const int AM = 2048, BM = 10923;                       // quick gains: alpha 0.5 (Q12), beta 1/6 (Q16)
        public const uint MX = 8454660, MY = 8449670;                 // 2^32 / pitch (508.0 and 508.3 report units)
        public const double PITCH_X = 508.0, PITCH_Y = 508.3;
        public const int MAX_X = 16000, MAX_Y = 9000;
        // Frame time charged in the frame model (Build.FrameNeedUs), like Aggressive Position Interpolation: both run in
        // the frame restart, after the frame's work, in the time the model keeps spare (25 us). The twin measured APIP's
        // routine at about 510 cycles (7 us) per new reading, at most about 700 without an interrupt. So every APIP
        // build's twin check also runs the same pen with the routine returning at once, and fails if APIP makes the
        // frames longer (the mean frame, pen lifts left out, by more than PERIOD_SLACK) or fewer. February, mean frame
        // with / without: ULTRA 966.29 / 966.10 us, RAPID 1283.28 / 1281.17, FAST 2616.10 / 2602.10, QUICK 3330.32 /
        // 3330.22, the same number of frames. Where the frame has room the difference is not APIP's time but its
        // positions: they change what the firmware does next (FAST has 290 us spare). Stopping the clock for the
        // routine's length is no model of it: that also holds back the scan's interrupts, which the routine does not.
        public const int APIP_US = 1;
        public const double PERIOD_SLACK = 0.01;
        // R45: the robust filters (atf_apipr.cs) take longer than the tracker. How much longer ULTRA's mean frame was with
        // them than with the tracker, in us (twin, February, Anargon's training; their routine alone: 1527 / 2028 / 2530
        // cycles for 2 / 3 / 4 trackers against the tracker's 662, both axes, no interrupts): 974.39 / 984.59 / 994.24
        // against 966.35. They are charged that, less what the twin gate lets APIP add to the mean frame (PERIOD_SLACK of
        // the period): at ULTRA 2 trackers fit, 3 and 4 do not.
        public static readonly double[] ROB_EXTRA_US = new double[] { 0, 0, 8.1, 18.3, 27.9 };
        // KIND: 0 off, 1 the tracker, 2-4 the robust filter with that many trackers
        public static int ProcUs(int kind, int periodUs)
        {
            if (kind <= 0) return 0;
            if (kind == 1) return APIP_US;
            double x = ROB_EXTRA_US[kind < ROB_EXTRA_US.Length ? kind : ROB_EXTRA_US.Length - 1] - PERIOD_SLACK * periodUs;
            return APIP_US + (x > 0 ? (int)(x + 0.999) : 0);
        }
        public const double GATE_LO = 2.5, GATE_HI = 6.0;
        // R45: the firmware's noise average n (of |second difference|, kept to at most 2 n) settles at NOISE_N x sigma
        // for normal noise; the gate's start and width and the end of stillness (1.5 x sigma) are n x K / 256
        public const double NOISE_N = 1.81;
        public const int KG1 = 354, KGD = 495, KES = 212;
        // still-pen gains: the time to reach them, and their longest memory (2 / alpha)
        public const double STILL_RAMP_S = 0.02, STILL_MEM_S = 0.1;
        public const int STATE_BYTES = 96;
        public static double reqSeconds = 1.0;                        // seconds of moving pen needed per 20 x 20 mm area and band
        public const int COLS = 8, ROWS = 5, AREA = 2000;             // 20 x 20 mm areas (the last row is 10 mm tall)
        // R45: the firmware's position is in 1/100 mm, but in 1/200 mm while the XP-Pen driver has the tablet in its own
        // report mode (4). Training samples carry UNIT_FLAG then; ApipData keeps every position in 1/200 mm (FINE per
        // 1/100 mm), and the model stays in 1/100 mm.
        public const int FINE = 2, UNIT_FLAG = 0x4000;
        // the profile format: 2 since R45 (format 1 could hold a training made in the driver's mode with the wrong unit)
        public const int FORMAT = 2;
        public static readonly string[] BAND_NAMES = new string[] { "Touching", "Low hover", "High hover" };

        // ================================================================ firmware
        // the frame's signal level, as the calibration readout and Steady Hover read it: the strongest X line's sum per
        // reading (0 when the peak is outside the buffer), at most 0xFFFF. Returns r0; uses r1-r3.
        public static void EmitSubs(Asm a, bool filter) { EmitSubs(a, filter, 0, 0); }
        // R45: MODELS > 0 = the robust filter with that many trackers (its routine aprx, constants at KONST) instead of
        // the tracker
        public static void EmitSubs(Asm a, bool filter, int konst, int models)
        {
            a.Label("aplevel");
            a.Mov32("r0", Sched.XBUF); a.Ldr("r1", "r0", 108); a.CmpI("r1", 6); a.Bcc("ls", "apl_go"); a.Movs("r0", 0); a.BxLr();
            a.Label("apl_go");
            a.Movs("r2", 12); a.Muls("r2", "r1"); a.AddsR("r2", "r2", "r0"); a.Ldr("r2", "r2", 4);
            a.Mov32("r3", Sched.S_ADDR); a.Ldrb("r0", "r3", 8); a.Ldrb("r3", "r3", 5); a.SubsR("r3", "r3", "r0");
            a.Bcc("gt", "apl_c"); a.Movs("r3", 1);
            a.Label("apl_c"); a.Udiv("r0", "r2", "r3"); a.Movw("r3", 0xFFFF); a.CmpR("r0", "r3"); a.Bcc("ls", "apl_r"); a.Mov("r0", "r3");
            a.Label("apl_r"); a.BxLr();
            if (!filter) return;
            if (models > 0) { ApipRobust.EmitAxis(a, konst, models); return; }
            // one axis (C# copy: Axis). In: r0 = reading, r1 = state (x, v), r2 = band record, r3 = 2^32 / pitch,
            // r7 = 0 when the filter starts over. Out: r0 = the position to report. Keeps r4-r7. R45: the axis's other
            // state at state + 32 ... + 83 (Apip.ST).
            // R45: the reading's unit is 1 / (100 << u) mm, u = ST+21 (1 in the XP-Pen driver's report mode 4, else 0).
            // The state stays in 1/100 mm (Q8) either way: the reading goes in shifted by 8 - u and the output comes out
            // by the same; the pitch multiplier and the axis maximum follow the unit. ip holds u.
            a.Label("apx");
            a.Push(Rg("r4", "r5", "r6"), true);
            a.Mov32("r4", ST); a.Ldrb("r5", "r4", 21); a.Mov("ip", "r5"); a.LsrsR("r3", "r5");
            a.MulW("r4", "r0", "r3");                                       // distance from report 0, in pitches (Q32)
            a.Lsrs("r5", "r4", 28); a.Lsls("r6", "r4", 4); a.Lsrs("r6", "r6", 24);   // segment, 1/256 within it
            a.Lsls("r5", "r5", 1); a.AddsR("r5", "r5", "r2");
            a.Ldrh("r4", "r5", 12); a.Sxth("r4", "r4"); a.Ldrh("r5", "r5", 14); a.Sxth("r5", "r5");
            a.SubsR("r5", "r5", "r4"); a.Muls("r5", "r6"); a.Asrs("r5", "r5", 8); a.AddsR("r4", "r4", "r5");
            a.Mov("r5", "ip"); a.Movs("r6", 8); a.SubsR("r6", "r6", "r5"); a.LslsR("r0", "r6");
            a.Lsls("r4", "r4", 4); a.AddsR("r0", "r0", "r4");               // corrected reading, Q8 of 1/100 mm
            a.Cbnz("r7", "apx_w");
            // start over at the reading: no speed, no stillness, the band's noise
            a.Label("apx_set"); a.Str("r0", "r1", 0); a.Str("r0", "r1", 64); a.Str("r0", "r1", 68);
            a.Movs("r4", 0); a.Str("r4", "r1", 4); a.Str("r4", "r1", 32); a.Str("r4", "r1", 36); a.Str("r4", "r1", 48); a.Str("r4", "r1", 80);
            a.Ldrh("r4", "r2", 8); a.Lsls("r4", "r4", 8); a.Str("r4", "r1", 52); a.B("apx_out");
            a.Label("apx_w");
            // R45, the noise now: n += (min(|second difference of the readings|, 2 n) - n) / 32, from 1 to 4096 units
            a.Ldr("r4", "r1", 64); a.Ldr("r5", "r1", 68); a.Str("r4", "r1", 68); a.Str("r0", "r1", 64);
            a.SubsR("r5", "r5", "r4"); a.SubsR("r5", "r5", "r4"); a.AddsR("r5", "r5", "r0");
            a.CmpI("r5", 0); a.Bcc("ge", "apx_n1"); a.Rsbs("r5");
            a.Label("apx_n1"); a.Ldr("r4", "r1", 52); a.Lsls("r6", "r4", 1); a.CmpR("r5", "r6"); a.Bcc("ls", "apx_n2"); a.Mov("r5", "r6");
            a.Label("apx_n2"); a.SubsR("r5", "r5", "r4"); a.Asrs("r5", "r5", 5); a.AddsR("r4", "r4", "r5");
            a.Movs("r5", 1); a.Lsls("r5", "r5", 8); a.CmpR("r4", "r5"); a.Bcc("ge", "apx_n3"); a.Mov("r4", "r5");
            a.Label("apx_n3"); a.Lsls("r5", "r5", 12); a.CmpR("r4", "r5"); a.Bcc("le", "apx_n4"); a.Mov("r4", "r5");
            a.Label("apx_n4"); a.Str("r4", "r1", 52);                       // r4 = noise (Q8) to the end of the gate
            a.Ldr("r5", "r1", 0); a.Ldr("r6", "r1", 4); a.AddsR("r5", "r5", "r6");     // prediction
            a.SubsR("r0", "r0", "r5");                                       // r = reading - prediction
            a.AddsI3("r6", "r0", 0); a.Bcc("pl", "apx_ab"); a.Rsbs("r6");
            a.Label("apx_ab");
            a.Movs("r3", 1); a.Lsls("r3", "r3", 19); a.CmpR("r6", "r3"); a.Bcc("cc", "apx_g");
            a.AddsR("r0", "r0", "r5"); a.B("apx_set");                       // 20 mm off: start over at the reading
            a.Label("apx_g");
            // the gate sees the smallest of this and the last two residuals, and nothing when any points the other way;
            // it opens from 2.5 x the noise (n x KG1 / 256) to fully at 6 x (+ n x KGD / 256)
            a.Ldr("r5", "r1", 80); a.Ldr("r3", "r1", 48); a.Str("r3", "r1", 80); a.Str("r0", "r1", 48);
            a.Eors("r5", "r0"); a.Bcc("mi", "apx_t0"); a.Eors("r5", "r0");
            a.CmpI("r5", 0); a.Bcc("ge", "apx_p0"); a.Rsbs("r5");
            a.Label("apx_p0"); a.CmpR("r5", "r6"); a.Bcc("cs", "apx_p1"); a.Mov("r6", "r5");
            a.Label("apx_p1"); a.Mov("r5", "r3");
            a.Eors("r5", "r0"); a.Bcc("mi", "apx_t0"); a.Eors("r5", "r0");
            a.CmpI("r5", 0); a.Bcc("ge", "apx_p2"); a.Rsbs("r5");
            a.Label("apx_p2"); a.CmpR("r5", "r6"); a.Bcc("cs", "apx_p3"); a.Mov("r6", "r5");
            a.Label("apx_p3");
            a.Movw("r5", KG1); a.Muls("r5", "r4"); a.Lsrs("r5", "r5", 8); a.SubsR("r6", "r6", "r5"); a.Bcc("le", "apx_t0");
            a.Movw("r5", KGD); a.Muls("r5", "r4"); a.Lsrs("r5", "r5", 8); a.CmpR("r6", "r5"); a.Bcc("cs", "apx_t1");
            a.Lsls("r6", "r6", 8); a.Udiv("r6", "r6", "r5"); a.B("apx_t");
            a.Label("apx_t0"); a.Movs("r6", 0); a.B("apx_t");
            a.Label("apx_t1"); a.Movs("r6", 1); a.Lsls("r6", "r6", 8);
            a.Label("apx_t");                                                // r6 = blend towards the quick gains, 0..256
            // with the gate shut, a residual counts as at most the gate's start (a lone spike moves little)
            a.Cbnz("r6", "apx_rc");
            a.Movw("r5", KG1); a.Muls("r5", "r4"); a.Lsrs("r5", "r5", 8);
            a.CmpR("r0", "r5"); a.Bcc("le", "apx_c3"); a.Mov("r0", "r5");
            a.Label("apx_c3"); a.Rsbs("r5"); a.CmpR("r0", "r5"); a.Bcc("ge", "apx_rc"); a.Mov("r0", "r5");
            a.Label("apx_rc");
            // stillness: e += (r - e) / 8; |e| over 1.5 x the noise (n x KES / 256) or a fully open gate ends it, else
            // it grows by the band's step (at most 256)
            a.Ldr("r3", "r1", 32); a.SubsR("r5", "r0", "r3"); a.Asrs("r5", "r5", 3); a.AddsR("r3", "r3", "r5"); a.Str("r3", "r1", 32);
            a.CmpI("r3", 0); a.Bcc("ge", "apx_e"); a.Rsbs("r3");
            a.Label("apx_e");
            a.Movs("r5", KES); a.Muls("r5", "r4"); a.Lsrs("r5", "r5", 8); a.CmpR("r3", "r5"); a.Bcc("hi", "apx_mv");
            a.Lsrs("r5", "r6", 8); a.Cbnz("r5", "apx_mv");
            a.Ldr("r4", "r1", 36); a.Ldrh("r5", "r2", 46); a.AddsR("r4", "r4", "r5");
            a.Movs("r5", 1); a.Lsls("r5", "r5", 8); a.CmpR("r4", "r5"); a.Bcc("ls", "apx_s"); a.Mov("r4", "r5"); a.B("apx_s");
            a.Label("apx_mv"); a.Movs("r4", 0);
            a.Label("apx_s"); a.Str("r4", "r1", 36);                         // r4 = stillness
            // gains: the moving ones towards the still ones by the stillness, then towards the quick ones by the gate
            a.Ldrh("r3", "r2", 0); a.Ldrh("r5", "r2", 4); a.SubsR("r5", "r5", "r3"); a.Muls("r5", "r4"); a.Asrs("r5", "r5", 8); a.AddsR("r4", "r3", "r5");
            a.Movw("r5", AM); a.SubsR("r5", "r5", "r4"); a.Muls("r5", "r6"); a.Asrs("r5", "r5", 8); a.AddsR("r4", "r4", "r5");
            a.Mov("r5", "r0"); a.Muls("r5", "r4"); a.Asrs("r5", "r5", 12);
            a.Ldr("r4", "r1", 0); a.Ldr("r3", "r1", 4); a.AddsR("r4", "r4", "r3"); a.AddsR("r4", "r4", "r5"); a.Str("r4", "r1", 0);
            a.Ldr("r3", "r1", 36);
            a.Ldrh("r5", "r2", 2); a.Ldrh("r4", "r2", 6); a.SubsR("r4", "r4", "r5"); a.Muls("r4", "r3"); a.Asrs("r4", "r4", 8); a.AddsR("r5", "r5", "r4");
            a.Movw("r3", BM); a.SubsR("r3", "r3", "r5"); a.Muls("r3", "r6"); a.Asrs("r3", "r3", 8); a.AddsR("r5", "r5", "r3");
            a.Movw("r3", 0xFFFF); a.CmpR("r0", "r3"); a.Bcc("le", "apx_c1"); a.Mov("r0", "r3");
            a.Label("apx_c1"); a.Rsbs("r3"); a.CmpR("r0", "r3"); a.Bcc("ge", "apx_c2"); a.Mov("r0", "r3");
            a.Label("apx_c2"); a.Muls("r0", "r5"); a.Asrs("r0", "r0", 16);
            a.Ldr("r3", "r1", 4); a.AddsR("r3", "r3", "r0"); a.Str("r3", "r1", 4);
            a.Ldr("r0", "r1", 0);
            a.Label("apx_out");
            a.Mov("r5", "ip"); a.Movs("r6", 128); a.LsrsR("r6", "r5"); a.AddsR("r0", "r0", "r6");
            a.Movs("r6", 8); a.SubsR("r6", "r6", "r5"); a.AsrsR("r0", "r6"); a.CmpI("r0", 0); a.Bcc("ge", "apx_p"); a.Movs("r0", 0);
            a.Label("apx_p"); a.Ldrh("r3", "r2", 10); a.LslsR("r3", "r5"); a.CmpR("r0", "r3"); a.Bcc("le", "apx_q"); a.Mov("r0", "r3");
            a.Label("apx_q"); a.Pop(Rg("r4", "r5", "r6"), true);
        }

        static string[] Rg(params string[] r) { return r; }

        // called from the frame restart, where the position of the frame is final (after Jump Rejection): APIP replaces
        // it with the filtered one. Uses r0-r3.
        public static void EmitFilter(Asm a, int tab, bool stages) { EmitFilter(a, tab, stages, false); }
        // R45: ROBUST = each axis through the robust filter (aprx, its state at ApipRobust.ROB)
        public static void EmitFilter(Asm a, int tab, bool stages, bool robust)
        {
            a.Label("apfilt");
            a.Push(Rg("r4", "r5", "r6", "r7"), true);
            a.Mov32("r4", ST); a.Mov32("r6", Sched.D_ADDR);
            a.Ldrb("r0", "r6", 7); a.CmpI("r0", 255); a.Bcc("ne", "ap_pen");
            a.Movs("r0", 0); a.Strb("r0", "r4", 20); a.Strb("r0", "r4", 31); a.B("ap_end");     // no pen: start over next time
            a.Label("ap_pen");
            a.Bl("aplevel"); a.Strh("r0", "r4", 28);
            if (stages) { a.Mov32("r2", Sched.VB_ADDR); a.Ldrb("r3", "r2", 0); a.CmpI("r3", 2); a.Bcc("ls", "ap_st"); a.Movs("r3", 2); a.Label("ap_st"); }
            else a.Movs("r3", 0);
            a.Strb("r3", "r4", 30);
            // R45: touching (pressure / 4 above 0, as the training reads it) has its own band
            a.Mov32("r1", Sched.PRESSURE); a.Ldrh("r1", "r1", 6); a.Lsrs("r1", "r1", 2); a.Cbz("r1", "ap_tz"); a.Movs("r1", 1);
            a.Label("ap_tz"); a.Strb("r1", "r4", 22);
            // R45: the position's unit (ST+21): 1/200 mm in the XP-Pen driver's report mode 4, else 1/100 mm; another
            // unit than last frame starts the filter over
            a.Mov32("r0", Sched.REPORT_MODE); a.Ldrb("r0", "r0", 0); a.Movs("r1", 0); a.CmpI("r0", 4); a.Bcc("ne", "ap_u"); a.Movs("r1", 1);
            a.Label("ap_u"); a.Ldrb("r0", "r4", 21); a.CmpR("r0", "r1"); a.Bcc("eq", "ap_u2");
            a.Strb("r1", "r4", 21); a.Movs("r0", 0); a.Strb("r0", "r4", 20);
            a.Label("ap_u2");
            a.Ldrh("r0", "r6", 8); a.Ldrh("r1", "r6", 10); a.Strh("r0", "r4", 24); a.Strh("r1", "r4", 26);
            a.Ldrb("r7", "r4", 20); a.CmpI("r7", WARM); a.Bcc("ne", "ap_cold");
            // the same position as reported last time: the firmware made no new reading this frame (keep it)
            a.Ldrh("r2", "r4", 16); a.CmpR("r0", "r2"); a.Bcc("ne", "ap_new");
            a.Ldrh("r2", "r4", 18); a.CmpR("r1", "r2"); a.Bcc("ne", "ap_new");
            a.Movs("r0", 1); a.Strb("r0", "r4", 31); a.B("ap_end");
            a.Label("ap_cold"); a.Movs("r7", 0);
            a.Label("ap_new"); a.Movs("r0", 2); a.Strb("r0", "r4", 31);
            // band: Steady Hover / Reach have their own, then touching; a hover counts the level thresholds it reaches
            a.Ldrh("r0", "r4", 28); a.Ldrb("r3", "r4", 30); a.Movs("r2", 0);
            a.Cbz("r3", "ap_lv"); a.AddsI3("r2", "r3", NB); a.B("ap_b");
            a.Label("ap_lv"); a.Ldrb("r1", "r4", 22); a.Cbz("r1", "ap_lh"); a.Movs("r2", NB); a.B("ap_b");
            a.Label("ap_lh"); a.Mov32("r1", tab + 8);
            for (int i = 0; i < NB - 1; i++) { a.Ldrh("r5", "r1", 2 * i); a.CmpR("r0", "r5"); a.Bcc("cc", "ap_b"); a.AddsI("r2", 1); }
            a.Label("ap_b");
            a.Movs("r1", 2 * REC); a.Muls("r2", "r1"); a.Mov32("r1", tab + 32); a.AddsR("r5", "r2", "r1");
            string fx = robust ? "aprx" : "apx";
            a.Ldrh("r0", "r6", 8); if (robust) a.Mov32("r1", ApipRobust.ROB); else a.Mov("r1", "r4");
            a.Mov("r2", "r5"); a.Mov32("r3", tab); a.Ldr("r3", "r3", 20);
            a.Bl(fx); a.Strh("r0", "r4", 16);
            a.Ldrh("r0", "r6", 10); if (robust) a.Mov32("r1", ApipRobust.ROB + ApipRobust.AXB); else { a.Mov("r1", "r4"); a.AddsI("r1", 8); }
            a.Mov("r2", "r5"); a.AddsI("r2", REC); a.Mov32("r3", tab); a.Ldr("r3", "r3", 24);
            a.Bl(fx); a.Strh("r0", "r4", 18);
            a.Ldrh("r0", "r4", 16); a.Strh("r0", "r6", 8); a.Ldrh("r0", "r4", 18); a.Strh("r0", "r6", 10);
            a.Movs("r0", WARM); a.Strb("r0", "r4", 20);
            a.Label("ap_end"); a.Pop(Rg("r4", "r5", "r6", "r7"), true);
        }

        // training firmware, in the frame restart: one sample per frame with the pen in range, and the LED zone (the
        // same box test as the calibration pass's report hook, from the frame's position). Keeps r4.
        public static void EmitRecorder(Asm a, bool stages)
        {
            a.Push(Rg("r4", "r5", "r6", "r7"), false);
            a.Mov32("r6", Sched.D_ADDR); a.Mov32("r5", RAM); a.Mov32("r7", Sched.ZONE_ADDR);
            a.Ldrb("r0", "r6", 7); a.CmpI("r0", 255); a.Bcc("ne", "rc_pen");
            a.Movs("r0", 0); a.Strb("r0", "r7", 1); a.B("rc_end");
            a.Label("rc_pen");
            a.Ldrh("r0", "r5", 4); a.Movw("r1", MAGIC); a.CmpR("r0", "r1"); a.Bcc("eq", "rc_go");
            a.Movs("r0", 0); a.Strh("r0", "r5", 0); a.Strh("r0", "r5", 2); a.Strh("r1", "r5", 4);   // first use: empty ring
            a.Label("rc_go");
            a.Bl("aplevel"); a.Mov("r4", "r0");
            a.Ldrh("r0", "r5", 0); a.Uxtb("r1", "r0"); a.Lsls("r1", "r1", 2); a.Lsls("r2", "r1", 1); a.AddsR("r1", "r1", "r2");   // 12 bytes a sample
            a.Mov32("r2", RING); a.AddsR("r1", "r1", "r2");
            a.Ldrh("r2", "r6", 8); a.Strh("r2", "r1", 0); a.Ldrh("r2", "r6", 10); a.Strh("r2", "r1", 2); a.Strh("r4", "r1", 4);
            a.Mov32("r2", Sched.PRESSURE); a.Ldrh("r2", "r2", 6); a.Lsrs("r2", "r2", 2); a.Movw("r3", 0xFFF); a.CmpR("r2", "r3"); a.Bcc("ls", "rc_p"); a.Mov("r2", "r3");
            a.Label("rc_p");
            if (stages) { a.Mov32("r3", Sched.VB_ADDR); a.Ldrb("r3", "r3", 0); a.CmpI("r3", 3); a.Bcc("ls", "rc_s"); a.Movs("r3", 3); a.Label("rc_s"); }
            else a.Movs("r3", 0);
            a.Lsls("r3", "r3", 12); a.Orrs("r2", "r3");
            // R45: the unit of x and y (the XP-Pen driver's report mode 4 has them in 1/200 mm)
            a.Mov32("r3", Sched.REPORT_MODE); a.Ldrb("r3", "r3", 0); a.CmpI("r3", 4); a.Bcc("ne", "rc_u");
            a.Movs("r3", 1); a.Lsls("r3", "r3", 14); a.Orrs("r2", "r3");
            a.Label("rc_u"); a.Strh("r2", "r1", 6);
            // R45-rc2: the detection signal, as the calibration readout's signal page: the strongest X and Y line's last
            // sample (line k at BUF + 12 k, +8; the peak index at BUF + 108, valid up to 6), the weaker of the two, 0
            // when either was not measured this frame
            a.Mov32("r3", Sched.XBUF); a.Ldr("r2", "r3", 108); a.Movs("r4", 0); a.CmpI("r2", 6); a.Bcc("hi", "rc_sg");
            a.Lsls("r2", "r2", 2); a.AddsR("r3", "r3", "r2"); a.Lsls("r2", "r2", 1); a.AddsR("r3", "r3", "r2"); a.Ldrh("r4", "r3", 8);
            a.Mov32("r3", Sched.YBUF); a.Ldr("r2", "r3", 108); a.CmpI("r2", 6); a.Bcc("hi", "rc_s0");
            a.Lsls("r2", "r2", 2); a.AddsR("r3", "r3", "r2"); a.Lsls("r2", "r2", 1); a.AddsR("r3", "r3", "r2"); a.Ldrh("r2", "r3", 8);
            a.CmpR("r2", "r4"); a.Bcc("cs", "rc_sg"); a.Mov("r4", "r2"); a.B("rc_sg");
            a.Label("rc_s0"); a.Movs("r4", 0);
            a.Label("rc_sg"); a.Strh("r4", "r1", 8);
            // and the pen's frequency (the firmware's resonance estimate, Hz) / 16 - FREQ_BASE, 0 below it, at most 0x7FFF
            a.Mov32("r3", Sched.RESONANCE); a.Ldr("r2", "r3", 0); a.Lsrs("r2", "r2", 4); a.Movw("r3", FREQ_BASE);
            a.CmpR("r2", "r3"); a.Bcc("hi", "rc_f1"); a.Movs("r2", 0); a.B("rc_fs");
            a.Label("rc_f1"); a.SubsR("r2", "r2", "r3"); a.Movw("r3", 0x7FFF); a.CmpR("r2", "r3"); a.Bcc("ls", "rc_fs"); a.Mov("r2", "r3");
            a.Label("rc_fs"); a.Strh("r2", "r1", 10);
            a.AddsI("r0", 1); a.Strh("r0", "r5", 0);                       // counted only once written whole
            // LED zone: off outside the target's box, the fade while a step records (Sched.ZONE_ADDR). R45: the box is in
            // 1/100 mm (halve a mode-4 position), and with a target the LED shows a hover only (off while the tip touches:
            // the centre hold keeps readings without pressure)
            a.Movs("r4", 1);
            a.Ldrb("r0", "r7", 0); a.Ldrb("r1", "r7", 2); a.Eors("r1", "r0"); a.CmpI("r1", Sched.ZONE_CHECK); a.Bcc("ne", "rc_z");
            a.Lsrs("r1", "r0", 1); a.Cbz("r1", "rc_rec");
            a.Mov32("r3", Sched.REPORT_MODE); a.Ldrb("r3", "r3", 0); a.SubsI("r3", 4);   // r3 = 0 in mode 4
            a.Ldrh("r1", "r6", 8); a.Cbnz("r3", "rc_ux"); a.Lsrs("r1", "r1", 1);
            a.Label("rc_ux"); a.Movw("r2", Sched.ZONE_X - Sched.ZONE_HALF); a.SubsR("r1", "r1", "r2");
            a.Movw("r2", 2 * Sched.ZONE_HALF); a.CmpR("r1", "r2"); a.Bcc("hi", "rc_out");
            a.Ldrh("r1", "r6", 10); a.Cbnz("r3", "rc_uy"); a.Lsrs("r1", "r1", 1);
            a.Label("rc_uy"); a.Movw("r2", Sched.ZONE_Y - Sched.ZONE_HALF); a.SubsR("r1", "r1", "r2");
            a.Movw("r2", 2 * Sched.ZONE_HALF); a.CmpR("r1", "r2"); a.Bcc("hi", "rc_out");
            a.Mov32("r1", Sched.PRESSURE); a.Ldrh("r1", "r1", 6); a.Cbz("r1", "rc_rec");
            a.Label("rc_out"); a.Movs("r4", 0); a.B("rc_z");
            a.Label("rc_rec"); a.Lsls("r0", "r0", 31); a.Bcc("eq", "rc_z"); a.Movs("r4", 2);
            a.Label("rc_z"); a.Strb("r4", "r7", 1);
            a.Label("rc_end"); a.Pop(Rg("r4", "r5", "r6", "r7"), false);
        }

        // USB string 180 (training firmware, in the GET_DESCRIPTOR hook): the unread samples, at most PER_READ (20; 31
        // before R45-rc2) per request, oldest first; at most the newest KEEP when the PC fell behind. Every UTF-16 unit is
        // a 15-bit value + 0x100 (never 0, never a surrogate): first the number of the first sample (15 bits), then x, y,
        // level, pressure / 4 | stage << 12, and (R45-rc2) the detection signal and the frequency field per sample.
        // r4 = the descriptor for the stock send; r5-r7 kept.
        public static void EmitReadout(Asm a, int send)
        {
            a.Label("cs_apip");
            a.Push(Rg("r5", "r6", "r7"), false);
            a.Mov32("r5", RAM); a.Movs("r3", 0); a.Movs("r1", 0);
            a.Ldrh("r0", "r5", 4); a.Movw("r1", MAGIC); a.CmpR("r0", "r1"); a.Bcc("ne", "cs_apn");
            a.Ldrh("r0", "r5", 0); a.Ldrh("r1", "r5", 2); a.SubsR("r3", "r0", "r1"); a.Uxth("r3", "r3");
            a.CmpI("r3", KEEP); a.Bcc("ls", "cs_apk"); a.SubsI("r0", KEEP); a.Uxth("r1", "r0"); a.Movs("r3", KEEP);
            a.Label("cs_apk"); a.CmpI("r3", PER_READ); a.Bcc("ls", "cs_apn"); a.Movs("r3", PER_READ);
            a.Label("cs_apn");
            a.Mov32("r4", DESC);
            a.Lsls("r0", "r3", 3); a.Lsls("r2", "r3", 2); a.AddsR("r0", "r0", "r2"); a.AddsI("r0", 4);      // 12 bytes a sample
            a.Movw("r2", 0x0300); a.Orrs("r0", "r2"); a.Strh("r0", "r4", 0);
            a.Movw("r6", 0x7FFF); a.Mov("r0", "r1"); a.Ands("r0", "r6"); a.Movw("r6", 0x100); a.AddsR("r0", "r0", "r6"); a.Strh("r0", "r4", 2);
            a.AddsI("r4", 4); a.Mov32("r7", RING);
            a.Label("cs_apl"); a.Cbz("r3", "cs_apd");
            a.Uxtb("r2", "r1"); a.Lsls("r2", "r2", 2); a.Lsls("r0", "r2", 1); a.AddsR("r2", "r2", "r0"); a.AddsR("r2", "r2", "r7");
            a.Ldrh("r0", "r2", 0); a.AddsR("r0", "r0", "r6"); a.Strh("r0", "r4", 0);
            a.Ldrh("r0", "r2", 2); a.AddsR("r0", "r0", "r6"); a.Strh("r0", "r4", 2);
            a.Ldrh("r0", "r2", 4); a.Movw("r5", 0x7FFF); a.CmpR("r0", "r5"); a.Bcc("ls", "cs_apv"); a.Mov("r0", "r5");
            a.Label("cs_apv"); a.AddsR("r0", "r0", "r6"); a.Strh("r0", "r4", 4);
            a.Ldrh("r0", "r2", 6); a.AddsR("r0", "r0", "r6"); a.Strh("r0", "r4", 6);
            a.Ldrh("r0", "r2", 8); a.CmpR("r0", "r5"); a.Bcc("ls", "cs_aps2"); a.Mov("r0", "r5");          // R45-rc2
            a.Label("cs_aps2"); a.AddsR("r0", "r0", "r6"); a.Strh("r0", "r4", 8);
            a.Ldrh("r0", "r2", 10); a.AddsR("r0", "r0", "r6"); a.Strh("r0", "r4", 10);
            a.AddsI("r4", 12); a.AddsI("r1", 1); a.Uxth("r1", "r1"); a.SubsI("r3", 1); a.B("cs_apl");
            a.Label("cs_apd");
            a.Mov32("r5", RAM); a.Ldrh("r0", "r5", 4); a.Movw("r2", MAGIC); a.CmpR("r0", "r2"); a.Bcc("ne", "cs_aps");
            a.Strh("r1", "r5", 2);
            a.Label("cs_aps");
            a.Mov32("r4", DESC); a.Pop(Rg("r5", "r6", "r7"), false);
            a.BWA(send);
        }

        // the firmware table (TAB_BYTES): magic, bands, level thresholds, pitch multipliers, quick gains, then per band
        // and axis (R45): alpha (Q12), beta (Q16), still-pen alpha and beta, the noise to start from (N0), the axis
        // maximum, the 17-point coil-distance correction (1/16 unit), and the step towards the still gains
        public static byte[] Table(ApipModel m) { return Table(m, null); }
        // R45: K = the robust filter's constants (ApipRobust.Constants), appended after the table; the header's +18 then
        // holds the number of trackers and each band record's +0 the offset of its step noise in K
        public static byte[] Table(ApipModel m, byte[] kc)
        {
            byte[] t = new byte[TAB_BYTES + (kc != null ? kc.Length : 0)];
            Put32(t, 0, TAB_MAGIC); Put16(t, 4, NBT); Put16(t, 6, NB);
            for (int i = 0; i < NB - 1; i++) Put16(t, 8 + 2 * i, m.thr[i]);
            Put32(t, 20, unchecked((int)MX)); Put32(t, 24, unchecked((int)MY)); Put16(t, 28, AM); Put16(t, 30, BM);
            for (int i = 0; i < NBT * 2; i++)
            {
                int o = 32 + i * REC;
                Put16(t, o, m.A[i]); Put16(t, o + 2, m.B[i]); Put16(t, o + 4, m.AS[i]); Put16(t, o + 6, m.BS[i]); Put16(t, o + 8, m.N0[i]);
                Put16(t, o + 10, i % 2 == 0 ? MAX_X : MAX_Y);
                for (int k = 0; k <= SEG; k++) Put16(t, o + 12 + 2 * k, m.W[i][k]);
                Put16(t, o + 46, m.STP[i]);
                if (kc != null) Put16(t, o, ApipRobust.K_Q + (i / 2) * ApipRobust.Q_BAND);
            }
            if (kc != null) { Put16(t, 18, I32(kc, ApipRobust.K_M)); for (int i = 0; i < kc.Length; i++) t[TAB_BYTES + i] = kc[i]; }
            return t;
        }
        static void Put16(byte[] t, int o, int v) { t[o] = (byte)(v & 0xFF); t[o + 1] = (byte)((v >> 8) & 0xFF); }
        static void Put32(byte[] t, int o, int v) { Put16(t, o, v & 0xFFFF); Put16(t, o + 2, (v >> 16) & 0xFFFF); }
        static int U16(byte[] t, int o) { return (t[o] & 0xFF) | ((t[o + 1] & 0xFF) << 8); }
        static int S16(byte[] t, int o) { int v = U16(t, o); return v >= 0x8000 ? v - 0x10000 : v; }
        static int I32(byte[] t, int o) { return U16(t, o) | (U16(t, o + 2) << 16); }

        // where an image keeps the APIP table (flash address), or -1
        public static int FindTable(byte[] flash) { return FindMagic(flash, TAB_MAGIC, TAB_BYTES); }
        public static bool IsTraining(byte[] flash) { return FindMagic(flash, TRAIN_MAGIC, 8) >= 0; }
        static int FindMagic(byte[] flash, int magic, int len)
        {
            int hit = -1;
            for (int o = 0x3800; o + len <= flash.Length; o += 4)
                if (I32(flash, o) == magic && U16(flash, o + 4) == (magic == TAB_MAGIC ? NBT : 0x5452)) { if (hit >= 0) return -1; hit = o; }
            return hit < 0 ? -1 : 0x08000000 + hit;
        }
        public static byte[] TrainMarker() { byte[] t = new byte[8]; Put32(t, 0, TRAIN_MAGIC); Put16(t, 4, 0x5452); Put16(t, 6, STRING); return t; }

        // the model inside a table (as the firmware reads it)
        public static int Routine(byte[] flash, int at) { return I32(flash, at - 0x08000000 + TAB_BYTES - 4) & ~1; }
        public static ApipModel FromTable(byte[] flash, int at)
        {
            int o = at - 0x08000000;
            ApipModel m = new ApipModel();
            for (int i = 0; i < NB - 1; i++) m.thr[i] = U16(flash, o + 8 + 2 * i);
            for (int i = 0; i < NBT * 2; i++)
            {
                int r = o + 32 + i * REC;
                m.A[i] = U16(flash, r); m.B[i] = U16(flash, r + 2); m.AS[i] = U16(flash, r + 4); m.BS[i] = U16(flash, r + 6); m.N0[i] = U16(flash, r + 8);
                for (int k = 0; k <= SEG; k++) m.W[i][k] = S16(flash, r + 12 + 2 * k);
                m.STP[i] = U16(flash, r + 46);
            }
            m.models = U16(flash, o + 18);
            if (m.models >= ApipRobust.MIN_M && m.models <= ApipRobust.MAX_M && o + TAB_BYTES + ApipRobust.K_END <= flash.Length) m.rk = ApipRobust.Read(flash, o + TAB_BYTES);
            else m.models = 0;
            return m;
        }

        // ================================================================ the C# copy (the twin checks the firmware against it)
        public static int Band(ApipModel m, int level, int stage, bool touching)
        {
            if (stage > 0) return NB + (stage > 2 ? 2 : stage);
            if (touching) return NB;
            int b = 0;
            for (int i = 0; i < NB - 1; i++) { if (level < m.thr[i]) break; b++; }
            return b;
        }
        public static int Band(ApipModel m, ApipData d, int i) { return Band(m, d.lvl[i], d.Stage(i), d.Pressure(i) > 0); }

        static void Start(ApipModel m, int rec, ApipAxis s, int zc) { s.x = zc; s.v = 0; s.e = 0; s.ts = 0; s.rp = 0; s.rp2 = 0; s.n = m.N0[rec] << 8; s.z1 = zc; s.z2 = zc; }

        // one axis of one frame: s = this axis's state, rec = band * 2 + axis; warm false = start over at the reading;
        // z in 1 / (100 << u) mm (R45: u = 1 in the XP-Pen driver's report mode 4), the state in Q8 of 1/100 mm
        // the coil-distance correction: the reading Z (1 / (100 << u) mm) in Q8 of 1/100 mm, corrected (both filters)
        public static int Corrected(ApipModel m, int rec, int z, int u)
        {
            int ax = rec & 1;
            uint ph = unchecked((uint)z * ((ax == 0 ? MX : MY) >> u));
            int i = (int)(ph >> 28), fr = (int)((ph << 4) >> 24);
            int w0 = m.W[rec][i], w1 = m.W[rec][i + 1];
            int corr = w0 + (((w1 - w0) * fr) >> 8);
            return (z << (8 - u)) + (corr << 4);
        }
        public static int Axis(ApipModel m, int rec, ApipAxis s, int z, bool warm, int u)
        {
            int ax = rec & 1;
            int zc = Corrected(m, rec, z, u);
            if (!warm) Start(m, rec, s, zc);
            else
            {
                int z1 = s.z1, z2 = s.z2;
                s.z2 = z1; s.z1 = zc;
                int d2 = z2 - z1 - z1 + zc, ad = d2 < 0 ? -d2 : d2, cap = s.n << 1;
                if ((uint)ad > (uint)cap) ad = cap;
                int n = s.n + ((ad - s.n) >> 5);
                if (n < 256) n = 256;
                if (n > 1 << 20) n = 1 << 20;
                s.n = n;
                int pred = s.x + s.v, r = zc - pred, ar = r < 0 ? -r : r;
                if (ar >= 1 << 19) Start(m, rec, s, zc);
                else
                {
                    int arp = s.rp < 0 ? -s.rp : s.rp, arq = s.rp2 < 0 ? -s.rp2 : s.rp2, g;
                    if ((r ^ s.rp) < 0 || (r ^ s.rp2) < 0) g = 0;
                    else { g = (uint)arq < (uint)ar ? arq : ar; if ((uint)arp < (uint)g) g = arp; }
                    s.rp2 = s.rp; s.rp = r;
                    int g1 = (int)((uint)(n * KG1) >> 8), d = g - g1, t;
                    if (d <= 0) t = 0;
                    else { int gd = (int)((uint)(n * KGD) >> 8); t = (uint)d >= (uint)gd ? 256 : (int)((uint)(d << 8) / (uint)gd); }
                    if (t == 0) { if (r > g1) r = g1; if (r < -g1) r = -g1; }
                    s.e = s.e + ((r - s.e) >> 3);
                    uint ae = (uint)(s.e < 0 ? -s.e : s.e), es = (uint)(n * KES) >> 8;
                    if (ae > es || t >= 256) s.ts = 0;
                    else { s.ts += m.STP[rec]; if (s.ts > 256) s.ts = 256; }
                    int a0 = m.A[rec] + (((m.AS[rec] - m.A[rec]) * s.ts) >> 8);
                    int al = a0 + (((AM - a0) * t) >> 8);
                    int nx = pred + ((r * al) >> 12);
                    int b0 = m.B[rec] + (((m.BS[rec] - m.B[rec]) * s.ts) >> 8);
                    int be = b0 + (((BM - b0) * t) >> 8);
                    int rc = r > 65535 ? 65535 : r < -65535 ? -65535 : r;
                    s.v = s.v + ((rc * be) >> 16);
                    s.x = nx;
                }
            }
            int o = (s.x + (128 >> u)) >> (8 - u);
            if (o < 0) o = 0;
            int mx = (ax == 0 ? MAX_X : MAX_Y) << u;
            return o > mx ? mx : o;
        }

        // ================================================================ training: reading the samples
        // decodes one read-out (UTF-16 units, without the descriptor header) into D; returns how many samples it held
        public static int Take(ApipData d, int[] u, int nu)
        {
            if (nu < 1) return 0;
            for (int i = 0; i < nu; i++) if (u[i] < 0x100 || u[i] > 0x80FF) return -1;
            int first = u[0] - 0x100, n = (nu - 1) / UNITS;
            long s0;
            if (d.last < 0) s0 = first;
            else { long delta = (first - (d.last + 1)) & 0x7FFF; s0 = d.last + 1 + delta; }
            for (int k = 0; k < n; k++)
            {
                int o = 1 + UNITS * k, aux = u[o + 3] - 0x100, f = (aux & UNIT_FLAG) != 0 ? 1 : FINE;
                d.Add(s0 + k, (u[o] - 0x100) * f, (u[o + 1] - 0x100) * f, u[o + 2] - 0x100, aux, u[o + 4] - 0x100, u[o + 5] - 0x100);
            }
            if (n > 0) d.last = s0 + n - 1;
            d.reads++;
            return n;
        }
        // from raw UTF-16LE bytes (Windows' HidD_GetIndexedString: the characters only, then zeros)
        public static int TakeChars(ApipData d, byte[] b, int from, int count)
        {
            int[] u = new int[128];
            int nu = 0;
            for (int i = from; i + 1 < from + count && i + 1 < b.Length && nu < u.Length; i += 2)
            {
                int v = (b[i] & 0xFF) | ((b[i + 1] & 0xFF) << 8);
                if (v == 0) break;
                u[nu++] = v;
            }
            return Take(d, u, nu);
        }

        // ================================================================ training: coverage (the CLI table)
        // the resonance band a sample shows in: 0 touching, 1 low hover, 2 high hover. Low hover = a level of at least a
        // quarter of the touching level (about 5 mm on the twin's pen); Steady Hover and Reach are high hover.
        public static int UserBand(ApipData d, int i, int touch)
        {
            if (d.Pressure(i) > 0) return 0;
            if (d.Stage(i) > 0) return 2;
            return 4 * d.lvl[i] >= touch ? 1 : 2;
        }
        public static int Rate(Level lv) { return lv.period > 0 ? (int)P.Rint(1e6 / lv.period) : 222; }
        // frames over which "moving" is judged (20 ms) and the distance that counts (0.4 mm, i.e. 20 mm/s)
        public static int MoveFrames(int rate) { int k = rate / 50; return k < 1 ? 1 : k; }
        public static bool Moving(ApipData d, int i, int k)
        {
            if (!d.Linked(i, k)) return false;
            int dx = d.x[i] - d.x[i - k], dy = d.y[i] - d.y[i - k];
            return (dx < 0 ? -dx : dx) + (dy < 0 ? -dy : dy) >= 40 * FINE;
        }
        public static int TouchLevel(ApipData d)
        {
            int[] h = new int[4097];
            int n = 0;
            for (int i = 0; i < d.n; i++) if (d.Pressure(i) > 0 && d.Stage(i) == 0) { int v = d.lvl[i] >> 4; h[v > 4096 ? 4096 : v]++; n++; }
            if (n == 0) return 0;
            int c = 0;
            for (int v = 0; v <= 4096; v++) { c += h[v]; if (2 * c >= n) return v * 16 + 8; }
            return 0;
        }
        // cells[band, row, col] = moving pen time, in frames of the preset. A reading of a slower stage (Steady Hover,
        // Reach Stage) counts the preset's frames it takes, so every band needs the same pen time, not the same count.
        public static int[,,] Coverage(ApipData d, Level lv, out int touch)
        {
            int[,,] c = new int[3, ROWS, COLS];
            touch = TouchLevel(d);
            int rate = Rate(lv);
            int[] sr = new int[] { rate, BandRate(lv, NB), BandRate(lv, NB + 1), rate };   // by stage
            for (int i = 0; i < d.n; i++)
            {
                int r = sr[d.Stage(i) & 3];
                if (r < 1) r = rate;
                if (!Moving(d, i, MoveFrames(r))) continue;
                int col = d.x[i] / (AREA * FINE), row = d.y[i] / (AREA * FINE);
                if (col < 0 || col >= COLS || row < 0 || row >= ROWS) continue;
                int w = (int)P.Rint((double)rate / r);
                c[UserBand(d, i, touch), row, col] += w < 1 ? 1 : w;
            }
            return c;
        }
        public static int Need(int rate, int row) { int n = (int)P.Rint(rate * reqSeconds); return row == ROWS - 1 ? (n + 1) / 2 : n; }
        public static bool Complete(int[,,] c, int rate)
        {
            for (int b = 0; b < 3; b++) for (int r = 0; r < ROWS; r++) for (int q = 0; q < COLS; q++) if (c[b, r, q] < Need(rate, r)) return false;
            return true;
        }
        public static int Done(int[,,] c, int rate, int band)
        {
            int n = 0;
            for (int r = 0; r < ROWS; r++) for (int q = 0; q < COLS; q++) if (c[band, r, q] >= Need(rate, r)) n++;
            return n;
        }
        // the table: one block of 8 x 5 areas per band, left to right and top to bottom as on the tablet; a number is
        // the percent of the readings that area needs, OK = enough
        public static StrList Table(int[,,] c, int rate)
        {
            StrList l = new StrList();
            StringBuilder h = new StringBuilder("    ");
            for (int b = 0; b < 3; b++) h.Append(Txt.PadR(BAND_NAMES[b] + " (" + F.Str(Done(c, rate, b)) + "/" + F.Str(ROWS * COLS) + ")", 26));
            l.Add(h.ToString().TrimEnd());
            for (int r = 0; r < ROWS; r++)
            {
                StringBuilder s = new StringBuilder("    ");
                for (int b = 0; b < 3; b++)
                {
                    for (int q = 0; q < COLS; q++)
                    {
                        int need = Need(rate, r), have = c[b, r, q];
                        int pc = need > 0 ? (int)((long)have * 100 / need) : 100;
                        s.Append(have >= need ? "OK" : Txt.PadL(F.Str(pc > 99 ? 99 : pc), 2));
                        if (q < COLS - 1) s.Append(" ");
                    }
                    if (b < 2) s.Append("   ");
                }
                l.Add(s.ToString());
            }
            return l;
        }

        // ================================================================ training: the model
        static double Median(double[] a, int n)
        {
            if (n <= 0) return 0;
            double[] b = new double[n];
            for (int i = 0; i < n; i++) b[i] = a[i];
            Array.Sort(b);
            return n % 2 == 1 ? b[n / 2] : (b[n / 2 - 1] + b[n / 2]) / 2;
        }
        // a sample's position in 1/100 mm (ApipData keeps 1/200 mm)
        public static double Z(ApipData d, int i, int ax) { return (ax == 0 ? d.x[i] : d.y[i]) / (double)FINE; }

        // the noise of each band and axis from second differences (a smooth pen movement adds almost nothing to them at
        // these rates): sigma = 1.4826 x median |d2| / sqrt 6. Returns sigma, sets n and the band's median level.
        static double Noise(ApipData d, ApipModel m, int band, int ax, out int n, out double level)
        {
            double[] v = new double[d.n], lv = new double[d.n];
            n = 0; int nl = 0;
            for (int i = 0; i < d.n; i++)
            {
                if (Band(m, d, i) != band) continue;
                lv[nl++] = d.lvl[i];
                if (!d.Linked(i, 2)) continue;
                double d2 = Z(d, i, ax) - 2 * Z(d, i - 1, ax) + Z(d, i - 2, ax);
                v[n++] = d2 < 0 ? -d2 : d2;
            }
            level = Median(lv, nl);
            if (n < 20) return -1;
            return 1.4826 * Median(v, n) / P.Sqrt(6.0);
        }

        // how the pen accelerates while moving (report units per frame^2): second differences over k frames carry the
        // pen's own acceleration x k^2 plus the noise (6 sigma^2); the median keeps it to ordinary movement.
        static double Accel(ApipData d, ApipModel m, int band, int ax, double sigma, int k, int rate)
        {
            double[] v = new double[d.n];
            int n = 0, km = MoveFrames(rate);
            for (int i = 0; i < d.n; i++)
            {
                if (band >= 0 && Band(m, d, i) != band) continue;
                if (!d.Linked(i, 2 * k) || !Moving(d, i, km)) continue;
                double a = Z(d, i, ax) - 2 * Z(d, i - k, ax) + Z(d, i - 2 * k, ax);
                v[n++] = a * a;
            }
            if (n < 100) return -1;
            double e = Median(v, n) / 0.455 - 6 * sigma * sigma;
            return e > 0 ? P.Sqrt(e) / ((double)k * k) : -1;
        }

        // steady-state gains of the alpha-beta tracker for tracking index lam (Kalata): the Kalman filter for a pen with
        // random acceleration of that size against that noise
        public static double AlphaFor(double lam)
        {
            double r = (4 + lam - P.Sqrt(8 * lam + lam * lam)) / 4;
            return 1 - r * r;
        }
        public static double BetaFor(double al) { return 2 * (2 - al) - 4 * P.Sqrt(1 - al); }
        // the least smoothing allowed: a memory of at most about 25 ms (2 / alpha frames)
        public static double AlphaMin(int rate) { double a = 2.0 / (rate * 0.025); return a < 0.05 ? 0.05 : a > 0.6 ? 0.6 : a; }

        // frames per second of a band: Steady Hover and Reach Stage read longer, so they run slower
        public static int BandRate(Level lv, int band)
        {
            int rate = Rate(lv);
            if (band == NB + 1) { int[] bst = Build.BoostOf(lv.key); return bst != null ? (int)P.Rint(1e6 / bst[0]) : rate; }
            if (band == NB + 2) return (int)P.Rint(1e6 / Build.REACH_MODE[0]);
            return rate;
        }

        static void SetGains(ApipModel m, int i, double sigma, double accel, int rate) { SetGainsLam(m, i, sigma, accel / sigma, rate); }
        static void SetGainsLam(ApipModel m, int i, double sigma, double lam, int rate)
        {
            double al = AlphaFor(lam), amin = AlphaMin(rate);
            if (al < amin) al = amin;
            if (al > 0.9) al = 0.9;
            double be = BetaFor(al);
            m.A[i] = Clamp((int)P.Rint(al * 4096), 1, 4095);
            m.B[i] = Clamp((int)P.Rint(be * 65536), 0, 32767);
            m.N0[i] = Clamp((int)P.Rint(NOISE_N * sigma), 1, 4096);
            m.sigma[i] = sigma;
        }
        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }
        // R45 still-pen gains of record i: tremor = how a hand holding still drifts (report units per frame^2 at the
        // band's rate; < 0 = not measured: a third of the moving alpha)
        public static double AlphaStillMin(int rate) { double a = 2.0 / (rate * STILL_MEM_S); return a < 0.02 ? 0.02 : a > 0.6 ? 0.6 : a; }
        static void SetStill(ApipModel m, int i, double sigma, double tremor, int rate)
        {
            SetStillAlpha(m, i, tremor >= 0 ? AlphaFor(tremor / sigma) : m.A[i] / 4096.0 / 3, rate);
        }
        static void SetStillAlpha(ApipModel m, int i, double al, int rate)
        {
            double am = m.A[i] / 4096.0, amin = AlphaStillMin(rate);
            if (al < amin) al = amin;
            if (al > am) al = am;
            m.AS[i] = Clamp((int)P.Rint(al * 4096), 1, 4095);
            m.BS[i] = Clamp((int)P.Rint(BetaFor(al) * 65536), 0, 32767);
            int fr = (int)P.Rint(rate * STILL_RAMP_S); if (fr < 1) fr = 1;
            m.STP[i] = Clamp((256 + fr - 1) / fr, 1, 256);
        }
        // the hand's drift while held still (the centre hold), report units per frame^2: second differences of the
        // means of W-frame windows (25 ms) carry the drift x W^2 plus the noise (6 sigma^2 / W); a drift that does not
        // stand out of the noise (less than half of it again) counts as none
        static double Tremor(ApipData d, int ax, double sigma, int rate)
        {
            int w = (int)P.Rint(rate * 0.025); if (w < 1) w = 1;
            double[] mean = new double[d.n / w + 1];
            bool[] ok = new bool[mean.Length];
            int nm = 0;
            for (int i = 0; i + w <= d.n; i += w, nm++)
            {
                ok[nm] = d.Linked(i + w - 1, w - 1) && (i == 0 || d.Linked(i, 1));
                double sm = 0;
                for (int j = i; j < i + w; j++) sm += Z(d, j, ax);
                mean[nm] = sm / w;
            }
            double[] v = new double[nm];
            int n = 0;
            for (int j = 2; j < nm; j++)
            {
                if (!ok[j] || !ok[j - 1] || !ok[j - 2]) continue;
                double a = mean[j] - 2 * mean[j - 1] + mean[j - 2];
                v[n++] = a * a;
            }
            if (n < 30) return -1;
            double noise = 6 * sigma * sigma / w, e = Median(v, n) / 0.455 - noise;
            return e > 0.5 * noise ? P.Sqrt(e) / ((double)w * w) : 0;
        }

        // the coil-distance correction of one band and axis (17 points, 1/16 unit), from the moving samples away from
        // the edges; shrunk towards none while there are few (weight n / (n + 3000)); at most 1 mm
        static int[] Warp(ApipData d, ApipModel m, int band, int ax, int rate, out int used)
        {
            int[] w = new int[SEG + 1];
            double[] h = new double[64];
            int k = MoveFrames(rate);
            used = 0;
            double pitch = ax == 0 ? PITCH_X : PITCH_Y;
            int lo = 600 * FINE, hi = ((ax == 0 ? MAX_X : MAX_Y) - 600) * FINE;
            for (int i = 0; i < d.n; i++)
            {
                int z = ax == 0 ? d.x[i] : d.y[i];                           // 1/200 mm
                if (z < lo || z > hi || Band(m, d, i) != band || !Moving(d, i, k)) continue;
                uint ph = unchecked((uint)z * ((ax == 0 ? MX : MY) / FINE));
                h[(int)(ph >> 26)]++; used++;
            }
            if (used < 500) return w;
            double[] s = new double[64];
            for (int j = 0; j < 64; j++) s[j] = (h[(j + 63) % 64] + 2 * h[j] + h[(j + 1) % 64]) / 4;
            double[] c = new double[65];
            double acc = 0, mean = 0;
            for (int j = 0; j <= 64; j++)
            {
                c[j] = (acc / used - j / 64.0) * pitch;
                if (j < 64) acc += s[j];
            }
            for (int j = 0; j < 64; j++) mean += s[j] * (c[j] + c[j + 1]) / 2;
            mean /= used;
            double wt = (double)used / (used + 3000);
            for (int q = 0; q <= SEG; q++)
            {
                double v = (c[4 * q] - mean) * wt * 16;
                w[q] = Clamp((int)P.Rint(v), -1600, 1600);
            }
            w[SEG] = w[0];
            return w;
        }

        // the whole model: thresholds from the touching level, the noise gradient over resonance (least squares of
        // log noise against log level over the bands with data, anchored at the centre hold), gains, corrections
        public static ApipModel Fit(ApipData d, ApipData hold, Level lv, bool fullRes)
        {
            ApipModel m = new ApipModel();
            m.preset = lv.key; m.fullRes = fullRes; m.rate = Rate(lv);
            m.samples = d.n; m.hold = hold != null ? hold.n : 0;
            int touch = TouchLevel(d);
            if (touch <= 0)
            {
                double[] all = new double[d.n];
                for (int i = 0; i < d.n; i++) all[i] = d.lvl[i];
                touch = (int)(Median(all, d.n) * 1.5);
            }
            if (touch < 64) touch = 64;
            double[] f = new double[] { 1.0 / 16, 1.0 / 8, 1.0 / 4, 1.0 / 2, 3.0 / 4 };
            for (int i = 0; i < NB - 1; i++) { m.thr[i] = (int)P.Rint(touch * f[i]); if (i > 0 && m.thr[i] <= m.thr[i - 1]) m.thr[i] = m.thr[i - 1] + 1; }
            int km = MoveFrames(m.rate);
            for (int i = 0; i < d.n; i++) if (Moving(d, i, km)) m.moving++;
            int ka = m.rate / 100; if (ka < 1) ka = 1;
            for (int ax = 0; ax < 2; ax++)
            {
                double[] sg = new double[NBT], lvB = new double[NBT];
                int[] nB = new int[NBT];
                double sw = 0, sx = 0, sy = 0, sxx = 0, sxy = 0;
                for (int b = 0; b < NBT; b++)
                {
                    sg[b] = Noise(d, m, b, ax, out nB[b], out lvB[b]);
                    if (b < NB && nB[b] >= 200 && sg[b] > 0 && lvB[b] > 0)
                    {
                        double wq = P.Sqrt(nB[b]), lx = P.Log(lvB[b]), ly = P.Log(sg[b]);
                        sw += wq; sx += wq * lx; sy += wq * ly; sxx += wq * lx * lx; sxy += wq * lx * ly;
                    }
                }
                double slope = -1.0, icpt, pooled = -1, tremor = -1;
                double den = sw * sxx - sx * sx;
                if (sw > 0 && den > 1e-9 * sw * sw) slope = (sw * sxy - sx * sy) / den;
                if (slope < -2) slope = -2; if (slope > 0) slope = 0;
                icpt = sw > 0 ? (sy - slope * sx) / sw : P.Log(5.0) - slope * P.Log(touch / 4.0);
                // the centre hold: a known spot held still, so its noise is the best single measurement
                if (hold != null && hold.n >= 200)
                {
                    int nh; double lh;
                    ApipModel hm = new ApipModel(); hm.thr = m.thr;
                    double[] all = new double[hold.n];
                    for (int i = 0; i < hold.n; i++) all[i] = hold.lvl[i];
                    lh = Median(all, hold.n);
                    double sh = NoiseAll(hold, ax, out nh);
                    if (sh > 0 && lh > 0) icpt = P.Log(sh) - slope * P.Log(lh);
                    if (sh > 0) tremor = Tremor(hold, ax, sh, m.rate);
                }
                pooled = -1;
                for (int b = 0; b < NBT; b++)
                {
                    double L = lvB[b];
                    if (L <= 0) L = b == 0 ? m.thr[0] / 2.0 : b < NB - 1 ? P.Sqrt((double)m.thr[b - 1] * m.thr[b]) : b == NB - 1 ? m.thr[NB - 2] * 1.25 : b == NB ? touch : m.thr[0] / 2.0;
                    double model = P.Exp(icpt + slope * P.Log(L < 1 ? 1 : L));
                    double s = model;
                    if (sg[b] > 0) { double wq = nB[b] / (nB[b] + 500.0); s = P.Exp(wq * P.Log(sg[b]) + (1 - wq) * P.Log(model)); }
                    if (s < 0.5) s = 0.5; if (s > 500) s = 500;
                    sg[b] = s; m.count[b * 2 + ax] = nB[b];
                }
                // Steady Hover / Reach Stage without readings: the weakest hover band (their signal is the weakest)
                if (nB[NB + 1] < 200) sg[NB + 1] = nB[NB + 1] > 0 ? P.Sqrt(sg[NB + 1] * sg[0]) : sg[0];
                if (nB[NB + 2] < 200) sg[NB + 2] = nB[NB + 2] > 0 ? P.Sqrt(sg[NB + 2] * sg[NB + 1]) : sg[NB + 1];
                pooled = Accel(d, m, -1, ax, sg[NB - 1], ka, m.rate);
                if (pooled <= 0) pooled = 1e5 / ((double)m.rate * m.rate);
                for (int b = 0; b < NBT; b++)
                {
                    int br = BandRate(lv, b);
                    double ac = Accel(d, m, b, ax, sg[b], ka, m.rate);
                    if (ac <= 0) { double fq = (double)m.rate / br; ac = pooled * fq * fq; }   // per frame squared at the band's rate
                    SetGains(m, b * 2 + ax, sg[b], ac, br);
                    double tq = (double)m.rate / br;
                    SetStill(m, b * 2 + ax, sg[b], tremor >= 0 ? tremor * tq * tq : -1, br);
                    int used;
                    m.W[b * 2 + ax] = Warp(d, m, b, ax, m.rate, out used);
                }
            }
            m.trained = P.Now("stamp");
            return m;
        }
        static double NoiseAll(ApipData d, int ax, out int n)
        {
            double[] v = new double[d.n];
            n = 0;
            for (int i = 0; i < d.n; i++)
            {
                if (!d.Linked(i, 2)) continue;
                double d2 = Z(d, i, ax) - 2 * Z(d, i - 1, ax) + Z(d, i - 2, ax);
                v[n++] = d2 < 0 ? -d2 : d2;
            }
            return n < 20 ? -1 : 1.4826 * Median(v, n) / P.Sqrt(6.0);
        }

        // a model for builds that only measure (the Settings check): every band 5 units of noise, no correction
        public static ApipModel Neutral(Level lv, bool fullRes)
        {
            ApipModel m = new ApipModel();
            m.preset = lv.key; m.fullRes = fullRes; m.rate = Rate(lv); m.trained = "untrained";
            int[] t = new int[] { 200, 400, 800, 1600, 2400 };
            for (int i = 0; i < NB - 1; i++) m.thr[i] = t[i];
            for (int i = 0; i < NBT * 2; i++) { int br = BandRate(lv, i / 2); SetGains(m, i, 5.0, 1e5 / ((double)br * br), br); SetStill(m, i, 5.0, -1, br); }
            return m;
        }

        // R45 (Anargon: "This example will be used as fall back if APIP calibration is not done but APIP is still
        // chosen"): Anargon's own R45 training on the real tablet, ULTRA with 12-bit Readings, 2026-11-11 - 602479
        // readings (290816 moving) over the whole area at touching, low and high hover, and a 3597-reading centre hold,
        // in the XP-Pen driver's report mode (1/200 mm, read as such). Kept exactly as fitted, coil-distance corrections
        // included (every Deco 640 has its coils on the same grid). Bands 0-2 had (almost) no readings: Fit filled them
        // from the noise gradient over the others. On the tablet (Tracker, ULTRA, pen still at the centre) it held the
        // cursor at 0.08x / 0.06x the spread of the unfiltered firmware (X / Y). Another preset: each band's tracking
        // index, moving and still, moved to that preset's band rate (per frame squared), as Fit does for bands without
        // readings, and the still ramp re-timed (SetStillAlpha).
        public const string FB_PRESET = "1000", FB_TRAINED = "20261111-110934";
        static readonly int[] FB_THR = new int[] { 8, 17, 34, 68, 102 };
        // per record (band * 2 + axis): A, B, N0 (moving gains, noise), AS, BS, STP (still gains, ramp step)
        static readonly int[] FB_G = new int[] {
            328, 219, 905, 82, 13, 13,          // band 0 x
            328, 219, 497, 82, 13, 13,          // band 0 y
            328, 219, 394, 82, 13, 13,          // band 1 x
            420, 364, 154, 100, 20, 13,         // band 1 y
            433, 386, 167, 82, 13, 13,          // band 2 x
            576, 697, 79, 140, 39, 13,          // band 2 y
            818, 1457, 102, 100, 20, 13,        // band 3 x
            937, 1945, 65, 153, 47, 13,         // band 3 y
            1114, 2826, 24, 202, 82, 13,        // band 4 x
            1162, 3096, 21, 264, 141, 13,       // band 4 y
            1952, 10017, 8, 342, 239, 13,       // band 5 x
            1967, 10205, 8, 420, 363, 13,       // band 5 y
            1058, 2524, 13, 272, 149, 13,       // touching x
            1066, 2566, 10, 382, 299, 13,       // touching y
            1325, 4127, 10, 601, 762, 26,       // Steady Hover x
            1369, 4440, 9, 748, 1206, 26,       // Steady Hover y
            2458, 17706, 8, 2458, 17714, 256,   // Reach Stage x
            2458, 17706, 7, 2458, 17714, 256,   // Reach Stage y
        };
        // noise per record, mm
        static readonly double[] FB_SIG = new double[] { 5.0000, 2.7470, 2.1783, 0.8533, 0.9233, 0.4354, 0.5628, 0.3587, 0.1333, 0.1180, 0.0450, 0.0447, 0.0727, 0.0545, 0.0544, 0.0514, 0.0418, 0.0384 };
        // coil-distance corrections per record (SEG + 1 points over one coil pitch)
        static readonly int[] FB_W = new int[] {
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            358, 216, 169, 179, 14, -172, -371, -485, 39, 51, 11, -23, -94, -161, -118, 412, 358,
            530, 227, -60, -361, -370, -371, -509, -570, -536, -554, -450, 135, 733, 740, 749, 681, 530,
            90, 95, 102, 127, 76, 6, -17, -62, -24, -42, -69, -130, -141, -65, 19, 50, 90,
            114, 1, -141, -251, -147, -14, -9, -36, 23, 167, 67, 5, 11, 62, 62, 90, 114,
            -185, -116, -71, 19, 91, 135, 82, 170, 206, 206, 126, 4, -97, -144, -182, -228, -185,
            -66, 146, 221, 145, 158, 136, 63, 29, -25, -42, -77, -145, -43, -163, -224, -96, -66,
            71, 60, 56, 24, -50, -109, -182, -213, -137, -35, 56, 58, 87, 106, 104, 108, 71,
            76, 38, -1, -70, -73, -96, -79, -113, -163, -61, 18, 95, 140, 58, 108, 127, 76,
            115, 7, -96, -185, -206, -187, -174, -112, 3, 118, 119, 9, 82, 121, 236, 176, 115,
            -164, -190, -290, -360, -246, -24, 109, 223, 205, 229, 211, 192, 188, 105, -41, -119, -164,
            48, 58, -54, -45, -71, -62, -103, 5, 59, 98, 44, -7, 64, 40, -18, -27, 48,
            -70, -176, -204, -82, -7, 38, 38, 9, -30, -33, -25, 34, 276, 201, 85, -26, -70,
        };
        // Anargon's training for LV (12-bit Readings, as it was trained), or null
        public static ApipModel Fallback(Level lv, bool fullRes)
        {
            if (lv == null || lv.period <= 0 || !fullRes) return null;
            Level src = Build.Lv(FB_PRESET);
            ApipModel m = new ApipModel();
            m.preset = lv.key; m.fullRes = true; m.rate = Rate(lv); m.trained = FB_TRAINED; m.fallback = true;
            for (int i = 0; i < NB - 1; i++) m.thr[i] = FB_THR[i];
            for (int i = 0; i < NBT * 2; i++)
            {
                int br = BandRate(lv, i / 2), bs = BandRate(src, i / 2), g = i * 6;
                double sg = FB_SIG[i] * 100;
                for (int k = 0; k <= SEG; k++) m.W[i][k] = FB_W[i * (SEG + 1) + k];
                if (br == bs)
                {
                    m.A[i] = FB_G[g]; m.B[i] = FB_G[g + 1]; m.N0[i] = FB_G[g + 2]; m.sigma[i] = sg;
                    m.AS[i] = FB_G[g + 3]; m.BS[i] = FB_G[g + 4]; m.STP[i] = FB_G[g + 5];
                }
                else
                {
                    double q = (double)bs / br;
                    SetGainsLam(m, i, sg, Lam(FB_G[g] / 4096.0) * q * q, br);
                    SetStillAlpha(m, i, AlphaFor(Lam(FB_G[g + 3] / 4096.0) * q * q), br);
                }
            }
            return m;
        }
        // the tracking index of an alpha-beta step with gain AL and beta = BetaFor(AL): beta / sqrt(1 - alpha)
        static double Lam(double al) { return BetaFor(al) / P.Sqrt(1 - al); }

        // ================================================================ the profile file (apip-<preset>.txt)
        public static void Save(string path, ApipModel m)
        {
            StringBuilder sb = new StringBuilder("# Anargon's Tablet Tools: ALgorithmic pen Position Interpolation training " + m.trained + "\r\n");
            sb.Append("# " + F.Str((int)m.samples) + " readings (" + F.Str((int)m.moving) + " moving), centre hold " + F.Str((int)m.hold) + "\r\n");
            sb.Append("format=" + F.Str(FORMAT) + "\r\ntrained=" + m.trained + "\r\npreset=" + m.preset + "\r\nfullres=" + (m.fullRes ? "1" : "0") + "\r\nrate=" + F.Str(m.rate) + "\r\n");
            sb.Append("thresholds=" + Join(m.thr, m.thr.Length) + "\r\n");
            string[] ax = new string[] { "x", "y" };
            for (int i = 0; i < NBT * 2; i++)
            {
                string k = "band" + F.Str(i / 2) + "_" + ax[i % 2];
                sb.Append("# " + k + ": noise " + F.Fx(m.sigma[i] / 100.0, 4) + " mm from " + F.Str(m.count[i]) + " readings\r\n");
                sb.Append(k + "=" + F.Str(m.A[i]) + "," + F.Str(m.B[i]) + "," + F.Str(m.N0[i]) + "\r\n");
                sb.Append(k + "_warp=" + Join(m.W[i], SEG + 1) + "\r\n");
                sb.Append(k + "_still=" + F.Str(m.AS[i]) + "," + F.Str(m.BS[i]) + "," + F.Str(m.STP[i]) + "\r\n");
            }
            // R45-rc2: the fast stage's pick-up and drop levels measured in the background (R45-rc1 ignores these keys)
            if (m.pick > 0 && m.drop > 0)
                sb.Append("# pick-up and drop levels measured in the background (rounded readings, like a calibration file)\r\npick=" + F.Str(m.pick) + "\r\ndrop=" + F.Str(m.drop) + "\r\n");
            P.AtomicWriteAllBytes(path, Build.Encode(sb.ToString()));
        }
        static string Join(int[] v, int n) { StringBuilder s = new StringBuilder(); for (int i = 0; i < n; i++) { if (i > 0) s.Append(","); s.Append(F.Str(v[i])); } return s.ToString(); }
        static int[] Ints(string v, int n, string key)
        {
            string[] p = P.Split(v, ',');
            if (p.Length != n) throw new Stop("Invalid ALPI training file: " + key + " needs " + F.Str(n) + " numbers.");
            int[] r = new int[n];
            for (int i = 0; i < n; i++)
            {
                string t = P.Trim(p[i]);
                bool neg = P.StartsWith(t, "-");
                string dg = neg ? P.Sub(t, 1, P.Len(t) - 1) : t;
                if (P.Len(dg) == 0 || P.Len(dg) > 6) throw new Stop("Invalid ALPI training file: " + key + " has a bad number.");
                for (int c = 0; c < P.Len(dg); c++) if (P.CharAt(dg, c) < '0' || P.CharAt(dg, c) > '9') throw new Stop("Invalid ALPI training file: " + key + " has a bad number.");
                r[i] = (int)F.ParseInt(dg) * (neg ? -1 : 1);
            }
            return r;
        }
        // parses and checks the whole file before anything is used
        public static ApipModel Load(string path)
        {
            string[] lines;
            try { lines = P.Lines(P.Ascii(P.ReadAllBytes(path))); }
            catch (Exception) { throw new Stop("Cannot read the ALPI training file: " + path); }
            ApipModel m = new ApipModel();
            m.file = path;
            m.trained = "(no date)";
            bool[] got = new bool[NBT * 6];
            bool fmt = false, th = false, pre = false;
            for (int li = 0; li < lines.Length; li++)
            {
                string ln = P.Trim(lines[li]);
                if (P.Len(ln) == 0 || P.StartsWith(ln, "#")) continue;
                int eq = P.IndexOf(ln, "=");
                if (eq <= 0) throw new Stop("Invalid ALPI training line " + F.Str(li + 1) + ": expected name=value.");
                string k = P.Trim(P.Sub(ln, 0, eq)), v = P.Trim(P.Sub(ln, eq + 1, P.Len(ln) - eq - 1));
                if (P.Eq(k, "format")) { if (!P.Eq(v, F.Str(FORMAT))) throw new Stop("This ALPI training file has another format; train again with this version."); fmt = true; }
                else if (P.Eq(k, "preset")) { m.preset = v; pre = true; }
                else if (P.Eq(k, "trained"))
                {
                    if (P.Len(v) == 0 || P.Len(v) > 20) throw new Stop("Invalid ALPI training date.");
                    for (int c = 0; c < P.Len(v); c++) if ((P.CharAt(v, c) < '0' || P.CharAt(v, c) > '9') && P.CharAt(v, c) != '-') throw new Stop("Invalid ALPI training date.");
                    m.trained = v;
                }
                else if (P.Eq(k, "fullres")) m.fullRes = P.Eq(v, "1");
                else if (P.Eq(k, "rate")) m.rate = Ints(v, 1, k)[0];
                else if (P.Eq(k, "thresholds")) { m.thr = Ints(v, NB - 1, k); th = true; }
                else if (P.Eq(k, "pick")) m.pick = Ints(v, 1, k)[0];                // R45-rc2
                else if (P.Eq(k, "drop")) m.drop = Ints(v, 1, k)[0];
                else if (P.StartsWith(k, "band"))
                {
                    int us = P.IndexOf(k, "_");
                    if (us < 5) throw new Stop("Invalid ALPI training key: " + k);
                    int[] bn = Ints(P.Sub(k, 4, us - 4), 1, k);
                    int b = bn[0];
                    string rest = P.Sub(k, us + 1, P.Len(k) - us - 1);
                    int ax = P.StartsWith(rest, "x") ? 0 : P.StartsWith(rest, "y") ? 1 : -1;
                    bool warp = P.Eq(rest, "x_warp") || P.Eq(rest, "y_warp"), still = P.Eq(rest, "x_still") || P.Eq(rest, "y_still");
                    if (b < 0 || b >= NBT || ax < 0 || (!warp && !still && P.Len(rest) != 1)) throw new Stop("Invalid ALPI training key: " + k);
                    int i = b * 2 + ax, kind = warp ? 1 : still ? 2 : 0;
                    if (got[i * 3 + kind]) throw new Stop("Duplicate ALPI training value: " + k);
                    got[i * 3 + kind] = true;
                    if (still)
                    {
                        int[] g = Ints(v, 3, k);
                        if (g[0] < 1 || g[0] > 4095 || g[1] < 0 || g[1] > 32767 || g[2] < 1 || g[2] > 256)
                            throw new Stop("Invalid ALPI training value: " + k);
                        m.AS[i] = g[0]; m.BS[i] = g[1]; m.STP[i] = g[2];
                    }
                    else if (warp)
                    {
                        int[] w = Ints(v, SEG + 1, k);
                        for (int q = 0; q <= SEG; q++) if (w[q] < -1600 || w[q] > 1600) throw new Stop("Invalid ALPI training value: " + k + " (at most 1 mm).");
                        if (w[SEG] != w[0]) throw new Stop("Invalid ALPI training value: " + k + " (its ends must match).");
                        m.W[i] = w;
                    }
                    else
                    {
                        int[] g = Ints(v, 3, k);
                        if (g[0] < 1 || g[0] > 4095 || g[1] < 0 || g[1] > 32767 || g[2] < 1 || g[2] > 4096)
                            throw new Stop("Invalid ALPI training value: " + k);
                        m.A[i] = g[0]; m.B[i] = g[1]; m.N0[i] = g[2];
                    }
                }
            }
            if (!fmt || !pre || !th) throw new Stop("This ALPI training file is incomplete; train again.");
            for (int i = 0; i < got.Length; i++) if (!got[i]) throw new Stop("This ALPI training file is incomplete; train again.");
            for (int i = 0; i < NB - 1; i++) if (m.thr[i] < 1 || m.thr[i] > 65535 || (i > 0 && m.thr[i] <= m.thr[i - 1])) throw new Stop("Invalid ALPI training thresholds.");
            if ((m.pick != -1 || m.drop != -1) && !(m.drop > 0 && m.pick > m.drop && m.pick <= 65535)) throw new Stop("Invalid ALPI training pick-up levels: pick > drop > 0.");
            return m;
        }
        // why M cannot be built into this preset with these settings, or null
        public static string Mismatch(ApipModel m, Level lv, bool fullRes)
        {
            if (m == null) return "not trained";
            if (!P.Eq(m.preset, lv.key)) return "trained on " + m.preset + " Hz, not " + lv.key + " Hz";
            if (m.fullRes != fullRes) return "trained with 12-bit Readings " + (m.fullRes ? "on" : "off");
            return null;
        }
        public static string FileName(Level lv) { return "apip-" + lv.name + ".txt"; }

        // ================================================================ twin
        // the firmware's filter against the C# copy: at every frame restart the twin compares the state and output the
        // last frame left in RAM with what Axis gives from the state before and the inputs the firmware recorded
        public class TwinBoard : TabletBoard
        {
            public const int H_AP = 400, H_APRET = 401;
            public ApipModel m; public ApipCheck res; public long from = long.MaxValue;
            public int fn;
            byte[] prev = null, prevR = null;
            long t0 = -1, lastFrame = -1; int ret = -1;
            public bool compare = true;                // false: the run without the routine, frame periods only
            public TwinBoard(Pen p, ApipModel model, ApipCheck r, int routine) : base(p) { m = model; res = r; fn = routine; }
            public override void Attach(Machine mc) { base.Attach(mc); if (fn > 0) mc.SetHook(fn, H_AP); }
            public override bool Hook(Machine mc, int hk)
            {
                // the routine's own time, from its first instruction to the one it returns to (interrupts included)
                if (hk == H_AP) { t0 = mc.cpu.cycles; if (ret < 0) { ret = mc.cpu.r[14] & ~1; mc.SetHook(ret, H_APRET); } return false; }
                if (hk == H_APRET) { if (t0 >= 0 && mc.cpu.cycles >= from) res.cycles.Add(mc.cpu.cycles - t0); t0 = -1; return false; }
                if (hk == H_FRAME && mc.cpu.cycles >= from)
                {
                    if (lastFrame >= 0) (compare ? res.period : res.basePeriod).Add(mc.cpu.cycles - lastFrame);
                    lastFrame = mc.cpu.cycles;
                }
                if (hk == H_FRAME && mc.cpu.cycles >= from && compare)
                {
                    byte[] cur = new byte[STATE_BYTES];
                    for (int i = 0; i < STATE_BYTES; i++) cur[i] = (byte)mc.Peek(ST + i, 1);
                    // R45: the robust filter's state too
                    byte[] curR = null;
                    if (m.models > 0) { curR = new byte[2 * ApipRobust.AXB]; for (int i = 0; i < curR.Length; i++) curR[i] = (byte)mc.Peek(ApipRobust.ROB + i, 1); }
                    if (prev != null) Compare(mc, prev, cur, prevR, curR);
                    prev = cur; prevR = curR;
                }
                return base.Hook(mc, hk);
            }
            void Compare(Machine mc, byte[] p, byte[] c, byte[] pr, byte[] cr)
            {
                int path = c[31];
                res.frames++;
                bool ok;
                string what;
                if (path == 0) { ok = c[20] == 0; what = "no pen"; res.idle++; }
                else if (path == 1) { ok = Same(p, c, 0, 22) && Same(p, c, 32, STATE_BYTES) && (pr == null || Same(pr, cr, 0, pr.Length)); what = "no new reading"; res.held++; }
                else if (path == 2 && m.models > 0)
                {
                    // R45: the robust filter (atf_apipr.cs)
                    res.filtered++;
                    bool warm = p[20] == WARM && p[21] == c[21];
                    int zx = U16(c, 24), zy = U16(c, 26), lvl = U16(c, 28), st = c[30], u = c[21];
                    int b = Band(m, lvl, st, c[22] != 0);
                    RobustAxis rx = new RobustAxis(), ry = new RobustAxis();
                    ApipRobust.Get(rx, pr, 0); ApipRobust.Get(ry, pr, ApipRobust.AXB);
                    int ox = ApipRobust.Axis(m, m.rk, b * 2, rx, zx, warm, u), oy = ApipRobust.Axis(m, m.rk, b * 2 + 1, ry, zy, warm, u);
                    if (u != 0) res.fine++;
                    // the still-pen tracker carries the most weight
                    if (Likeliest(rx, m.models) == 0 || Likeliest(ry, m.models) == 0) res.still++;
                    string dx = ApipRobust.Differs(rx, m.models, cr, 0), dy = ApipRobust.Differs(ry, m.models, cr, ApipRobust.AXB);
                    ok = U16(c, 16) == ox && U16(c, 18) == oy && dx == null && dy == null && c[20] == WARM;
                    if (b != res.lastBand) res.bandChanges++;
                    res.lastBand = b; res.bands |= 1 << b;
                    if (ox != zx || oy != zy) res.changed++;
                    what = "frame (" + F.Str(zx) + ", " + F.Str(zy) + ") level " + F.Str(lvl) + " stage " + F.Str(st) + " band " + F.Str(b) + (warm ? "" : " start")
                        + ": firmware (" + F.Str(U16(c, 16)) + ", " + F.Str(U16(c, 18)) + "), C# (" + F.Str(ox) + ", " + F.Str(oy) + ")"
                        + (dx != null ? ", X " + dx + " differs" : "") + (dy != null ? ", Y " + dy + " differs" : "");
                }
                else if (path == 2)
                {
                    res.filtered++;
                    bool warm = p[20] == WARM && p[21] == c[21];                 // R45: another unit starts over
                    int zx = U16(c, 24), zy = U16(c, 26), lvl = U16(c, 28), st = c[30], u = c[21];
                    int b = Band(m, lvl, st, c[22] != 0);
                    ApipAxis sx = new ApipAxis(), sy = new ApipAxis();
                    sx.x = I32(p, 0); sx.v = I32(p, 4); sy.x = I32(p, 8); sy.v = I32(p, 12);
                    Get(sx, p, 0); Get(sy, p, 8);
                    int ox = Axis(m, b * 2, sx, zx, warm, u), oy = Axis(m, b * 2 + 1, sy, zy, warm, u);
                    if (u != 0) res.fine++;
                    if (sx.ts >= 256 || sy.ts >= 256) res.still++;
                    ok = U16(c, 16) == ox && U16(c, 18) == oy && Is(sx, c, 0) && Is(sy, c, 8) && c[20] == WARM;
                    if (b != res.lastBand) res.bandChanges++;
                    res.lastBand = b; res.bands |= 1 << b;
                    if (ox != zx || oy != zy) res.changed++;
                    what = "frame (" + F.Str(zx) + ", " + F.Str(zy) + ") level " + F.Str(lvl) + " stage " + F.Str(st) + " band " + F.Str(b) + (warm ? "" : " start")
                        + ": firmware (" + F.Str(U16(c, 16)) + ", " + F.Str(U16(c, 18)) + "), C# (" + F.Str(ox) + ", " + F.Str(oy) + ")";
                }
                else { ok = false; what = "unknown path " + F.Str(path); }
                if (ok) res.match++;
                else { res.miss++; if (res.first == null) res.first = what; }
            }
            static int Likeliest(RobustAxis s, int M) { int jm = 0; for (int j = 1; j < M; j++) if (s.MU[j] > s.MU[jm]) jm = j; return jm; }
            static bool Same(byte[] a, byte[] b, int from, int to) { for (int i = from; i < to; i++) if (a[i] != b[i]) return false; return true; }
            // one axis's state as RAM holds it (o = 0 for x, 8 for y)
            static void Get(ApipAxis s, byte[] b, int o)
            {
                s.x = I32(b, o); s.v = I32(b, o + 4); s.e = I32(b, o + 32); s.ts = I32(b, o + 36);
                s.rp = I32(b, o + 48); s.n = I32(b, o + 52); s.z1 = I32(b, o + 64); s.z2 = I32(b, o + 68); s.rp2 = I32(b, o + 80);
            }
            static bool Is(ApipAxis s, byte[] b, int o)
            {
                ApipAxis f = new ApipAxis(); Get(f, b, o);
                return f.x == s.x && f.v == s.v && f.e == s.e && f.ts == s.ts && f.rp == s.rp && f.rp2 == s.rp2 && f.n == s.n && f.z1 == s.z1 && f.z2 == s.z2;
            }
        }

        // a pen sliding and turning over the area at touch and hover heights; every frame compared
        public static ApipCheck TwinCheck(byte[] flash, int msEach)
        {
            ApipCheck res = new ApipCheck();
            int at = FindTable(flash);
            if (at < 0) { res.first = "no ALPI table in this image"; return res; }
            ApipModel m = FromTable(flash, at);
            int fn = Routine(flash, at);
            // the same image with the routine returning at once (bx lr at its start): the frames without APIP's time
            byte[] without = (byte[])flash.Clone();
            if (fn > 0x08000000 && fn - 0x08000000 + 2 <= without.Length) { without[fn - 0x08000000] = 0x70; without[fn - 0x08000000 + 1] = 0x47; }
            // R45: the hover run in the XP-Pen driver's report mode (4, positions in 1/200 mm)
            double[] heights = new double[] { 0, 4 };
            int[] modes = new int[] { 2, 4 };
            for (int hi = 0; hi < heights.Length; hi++)
            {
                string e1 = RunPen(flash, m, res, fn, true, hi, heights[hi], modes[hi], msEach);
                if (e1 == null && fn > 0) e1 = RunPen(without, m, res, 0, false, hi, heights[hi], modes[hi], msEach);
                if (e1 != null) { res.first = e1; return res; }
            }
            return res;
        }
        static string RunPen(byte[] flash, ApipModel m, ApipCheck res, int fn, bool compare, int hi, double height, int mode, int msEach)
        {
            Pen pen = new Pen(21 + hi);
            pen.x = 9.3; pen.y = 6.6; pen.f0 = 505000; pen.Q = 300; pen.hasHeight = true; pen.height = height; pen.integrate = true; pen.noise = 6.0;
            TwinBoard b = new TwinBoard(pen, m, res, fn);
            b.compare = compare;
            Machine mc = new Machine(flash, true, b, false);
            string st = mc.Boot(3000000);
            if (!P.Eq(st, "ok")) return "boot: " + st;
            mc.AttachUsb(true);
            mc.RunMs(80);
            mc.Poke(Sched.REPORT_MODE, 1, mode);
            b.from = mc.cpu.cycles;
            double vx = 12.0, vy = 7.0;
            try
            {
                for (int t = 0; t < msEach; t++)
                {
                    if (t % 150 == 0) { vx = -vx * 1.3; vy = -vy * 0.8; }   // turns: the quick gains get used
                    if (t % 450 < 300) { pen.x += vx / 1000.0; pen.y += vy / 1000.0; }   // and held still: the still gains
                    if (t == msEach / 2) pen.present = false;              // lift out and back: the filter starts over
                    if (t == msEach / 2 + 30) pen.present = true;
                    mc.RunMs(1);
                }
            }
            catch (Fault e) { return "stopped: " + e.msg; }
            catch (Stop e) { return "stopped: " + e.msg; }
            return null;
        }

        // the training firmware in the twin: reads the samples back through USB string 180 while a pen moves, and
        // compares them with the positions the reports carried
        public static TrainCheckResult TrainCheck(byte[] flash)
        {
            TrainCheckResult r = new TrainCheckResult();
            Pen pen = new Pen(5);
            pen.x = 10.3; pen.y = 7.6; pen.f0 = 505000; pen.Q = 300; pen.hasHeight = true; pen.height = 0; pen.integrate = true;
            StringBuilder why = new StringBuilder();
            Machine mc = Twin.ScanMachine(flash, pen, false, why);
            if (mc == null) { r.why = why.ToString(); return r; }
            VirtualHost host = new VirtualHost(mc.usb);
            ApipData d = new ApipData();
            try
            {
                mc.RunMs(80);
                Read(mc, host, d);                                            // whatever the boot left
                d = new ApipData();
                int r0 = mc.reports.n;
                for (int t = 0; t < 400; t++)
                {
                    pen.x += 0.01; pen.y += 0.006; mc.RunMs(1);
                    if (t % 10 == 9) { if (Read(mc, host, d) < 0) { r.why = "read-out refused"; return r; } }
                }
                Read(mc, host, d);
                int pos = 0;
                for (int i = r0; i < mc.reports.n; i++) if (Twin.PositionReport(mc.reports.a[i])) pos++;
                r.samples = d.n; r.reports = pos; r.reads = d.reads;
                int gaps = 0;
                for (int i = 1; i < d.n; i++) if (d.seq[i] != d.seq[i - 1] + 1) gaps++;
                r.gaps = gaps;
                int touch = 0, lvOk = 0, sgOk = 0, fqOk = 0;
                for (int i = 0; i < d.n; i++)
                {
                    if (d.lvl[i] > 0) lvOk++;
                    if (d.x[i] > 0 && d.x[i] <= MAX_X * FINE && d.y[i] > 0 && d.y[i] <= MAX_Y * FINE) touch++;
                    if (d.sig[i] > 0) sgOk++;
                    if (d.Hz(i) >= 457000 && d.Hz(i) <= 540000) fqOk++;                 // R45-rc2
                }
                r.signals = sgOk; r.freqs = fqOk;
                r.ok = d.n > 100 && gaps == 0 && lvOk == d.n && touch == d.n && d.n >= pos * 9 / 10 && sgOk >= d.n * 9 / 10 && fqOk >= d.n * 9 / 10;
                if (!r.ok) r.why = F.Str(d.n) + " samples for " + F.Str(pos) + " position reports, " + F.Str(gaps) + " gaps, " + F.Str(d.n - lvOk) + " without a level, "
                    + F.Str(d.n - sgOk) + " without a detection signal, " + F.Str(d.n - fqOk) + " without a pen frequency";
            }
            catch (Fault e) { r.why = e.msg; }
            catch (Stop e) { r.why = e.msg; }
            return r;
        }

        // one read-out request through the twin's USB host; returns the samples it held (-1: no answer)
        public static int Read(Machine mc, VirtualHost host, ApipData d)
        {
            for (int i = 0; i < 1000000 && (mc.cpu.excDepth > 0 || mc.cpu.primask != 0); i++) mc.RunMs(0.0002);
            byte[] b = host.GetDescriptor(3, STRING, 255, 0x0409, 0x80);
            if (b == null || b.Length < 4 || (b[1] & 0xFF) != 3) return -1;
            int len = b[0] & 0xFF;
            return TakeChars(d, b, 2, (len < b.Length ? len : b.Length) - 2);
        }

        // ---- simulated training (tests): strokes over the whole area at three heights, then the centre hold
        public static ApipData SimTrain(byte[] flash, int seconds, long seed, ApipData hold)
        {
            ApipData d = new ApipData();
            Pen pen = new Pen(seed);
            pen.f0 = 505000; pen.Q = 300; pen.hasHeight = true; pen.integrate = true; pen.x = 16; pen.y = 9.5;
            StringBuilder why = new StringBuilder();
            Machine mc = Twin.ScanMachine(flash, pen, false, why);
            if (mc == null) throw new Stop("twin: " + why.ToString());
            VirtualHost host = new VirtualHost(mc.usb);
            Rng rng = new Rng(seed + 7);
            mc.RunMs(80);
            Read(mc, host, new ApipData());
            double tx = 16, ty = 9.5, vx = 0, vy = 0;
            int phase = 0;
            for (int t = 0; t < seconds * 1000; t++)
            {
                if (t % 700 == 0)
                {
                    phase = (t / 700) % 3;                                    // touching, low hover, high hover in turn
                    pen.height = phase == 0 ? 0 : phase == 1 ? 3 : 7;
                    pen.f0 = phase == 0 ? 513000 : 505000;                     // the twin shows pressure from the frequency
                }
                if (t % 350 == 0) { tx = 1 + 31.5 * rng.Random(); ty = 0.5 + 18.5 * rng.Random(); }
                // a smooth pen (critically damped towards the target), at most 0.04 pitches per ms (about 200 mm/s)
                vx += (tx - pen.x) * 0.0004 - vx * 0.04; vy += (ty - pen.y) * 0.0004 - vy * 0.04;
                double sp = P.Sqrt(vx * vx + vy * vy);
                if (sp > 0.04) { vx *= 0.04 / sp; vy *= 0.04 / sp; }
                pen.x += vx; pen.y += vy;
                mc.RunMs(1);
                if (t % 10 == 9) Read(mc, host, d);
            }
            Read(mc, host, d);
            if (hold != null)
            {
                pen.x = 16.3; pen.y = 9.6; pen.height = 4; pen.f0 = 505000;
                mc.RunMs(300);
                Read(mc, host, new ApipData());
                for (int t = 0; t < 4000; t++) { mc.RunMs(1); if (t % 10 == 9) Read(mc, host, hold); }
                Read(mc, host, hold);
            }
            return d;
        }
    }

    public class ApipCheck
    {
        public long frames, match, miss, filtered, held, idle, changed, bandChanges, fine, still;   // fine: filtered frames in 1/200 mm; still: at the still gains
        public int bands = 0, lastBand = -1;
        public string first;
        public LongList cycles = new LongList();      // each call of the routine (interrupts included)
        public LongList period = new LongList(), basePeriod = new LongList();   // frame restart to restart, with and without the routine
        // the mean frame in microseconds, without the gaps of a lift or a search (frames over twice the median)
        public static double MeanUs(LongList l)
        {
            if (l.n == 0) return 0;
            long[] v = new long[l.n];
            for (int i = 0; i < l.n; i++) v[i] = l.a[i];
            Array.Sort(v);
            long med = v[v.Length / 2];
            double s = 0; int n = 0;
            for (int i = 0; i < v.Length; i++) if (v[i] <= 2 * med) { s += v[i]; n++; }
            return s / n / 72.0;
        }
        // APIP must not make the frames longer or fewer than the same run without it
        public bool PeriodOk()
        {
            if (period.n < 100 || basePeriod.n < 100) return false;
            return MeanUs(period) <= MeanUs(basePeriod) * (1 + Apip.PERIOD_SLACK) && period.n * 100 >= basePeriod.n * 99;
        }
        public bool Ok() { return miss == 0 && filtered > 100 && changed > 0 && cycles.n > 100 && fine > 0 && fine < filtered && still > 0 && still < filtered && PeriodOk(); }
        // the costliest call without an interrupt inside (calls above twice the median had one)
        public long CostMax()
        {
            if (cycles.n == 0) return 0;
            long[] s = new long[cycles.n];
            for (int i = 0; i < cycles.n; i++) s[i] = cycles.a[i];
            Array.Sort(s);
            long med = s[s.Length / 2], mx = 0;
            for (int i = 0; i < s.Length; i++) if (s[i] <= 2 * med && s[i] > mx) mx = s[i];
            return mx;
        }
        public long CostMedian() { if (cycles.n == 0) return 0; long[] s = new long[cycles.n]; for (int i = 0; i < cycles.n; i++) s[i] = cycles.a[i]; Array.Sort(s); return s[s.Length / 2]; }
    }
    public class TrainCheckResult { public bool ok; public int samples, reports, reads, gaps, signals, freqs; public string why = ""; }

    // R44: the wizard's APIP training. Anargon: it ends when every 20 x 20 mm area has enough readings in every
    // resonance band (not after a fixed time); the CLI shows a table whose numbers turn into OK.
    public static class ApipCli
    {
        static string Pc1(int a) { double v = a * 100.0 / 4096; return v < 10 ? F.Fx(v, 1) + "%" : F.Str((int)P.Rint(v)) + "%"; }
        public const int VID = 0x28BD, PID = 0x2904;
        public const double HOLD_S = 4.0, SETTLE_S = 0.3;
        static string Ask(string p) { return Screen.Ask(p); }

        static Options Clone(Options o)
        {
            Options c = new Options();
            c.samples = o.samples; c.burst = o.burst; c.tick = o.tick; c.poll = o.poll; c.boostAt = o.boostAt; c.calSpan = o.calSpan;
            c.oddMm = o.oddMm; c.detectScale = o.detectScale; c.calPick = o.calPick; c.calDrop = o.calDrop; c.fallback = o.fallback;
            return c;
        }

        // R45: what a training that does not finish leaves: Anargon's training (Apip.Fallback) when there is one
        static ApipModel fb = null;
        static string NoTrain() { return fb != null ? "ALPI uses " + FB_NAME + " instead (fallback)" : "building without ALPI"; }
        public const string FB_NAME = "Anargon's training";
        static bool UseFallback(Options o) { if (fb == null) return false; o.apip = fb; return true; }
        // R45-rc3 (Anargon): ALPI comes only with Full calibration, and skipping its training there means the defaults
        // ("full > skip alpi steps > alpi defaults with mismatched calibration + frequency defaults"): Anargon's
        // training, the fallback pick-up levels (Options.fallback, Build.FB_*) or the preset's, and the stock scan
        // frequency (Full never uses Pen frequency tuning). To build without ALPI, turn it off in step 5.
        static string SkipWord() { return fb != null ? "skip (use the defaults: " + FB_NAME + ")" : "skip (" + FB_NAME + " needs 12-bit Readings, so the build is made without ALPI)"; }
        static bool Skip(Options o)
        {
            P.Line("");
            if (fb == null) { P.Line("    Skipped: building without ALPI (" + FB_NAME + " needs 12-bit Readings)."); return false; }
            Screen.Para("Skipped: ALPI uses the defaults - " + FB_NAME + ", the pick-up levels from " + Build.FB_WHO + " or the preset, and the stock pen frequency. None of them were measured on your tablet, so they may not match it; train ALPI later for a better fit.", 4);
            return UseFallback(o);
        }

        // true: o.apip is set (trained now, a saved training, or R45 Anargon's as the fallback); false: build without APIP
        public static bool Train(byte[] app, InputInfo info, Level lv, bool[] st, string folder, Options o, bool realtime)
        {
            bool fr = Build.On(st, "fullres") && Build.Unavailable("fullres", lv, st) == null;
            fb = Apip.Fallback(lv, fr);
            string path = P.PathJoin(folder, Apip.FileName(lv));
            ApipModel saved = null;
            if (P.FileExists(path))
            {
                try { saved = Apip.Load(path); if (Apip.Mismatch(saved, lv, fr) != null) saved = null; }
                catch (Stop) { saved = null; }
            }
            Screen.Step("ALPI training");
            Screen.Para("ALgorithmic pen Position Interpolation (ALPI) learns your tablet's noise while you use it, then the firmware removes it. It is trained once per preset, in three steps: a training firmware (the cursor keeps working), normal use of the tablet until every part of it has enough readings at every pen height, and a 4-second hold in the middle. The same readings also measure, in the background, what Standard calibration measures: how high your tablet picks up the pen, and your pen's frequency.", 4);
            P.Line("");
            string a;
            if (saved != null)
            {
                Screen.Para("Found your training for " + P.Upper(lv.name) + ": " + path + " (" + saved.trained + ").", 4);
                P.Line("");
                a = P.Lower(P.Trim(Ask("    Enter = use it, t = train again, s = " + SkipWord() + ": > ")));
                if (P.Eq(a, "s") || P.Eq(a, "f") || P.InputEnded()) return Skip(o);
                if (!P.Eq(a, "t")) { o.apip = saved; P.Line(""); P.Line("    Using " + path + "."); UseLevels(app, info, lv, st, o, saved); return true; }
            }
            else
            {
                if (fb != null) { Screen.Para("Without a training of your own, ALPI uses " + FB_NAME + " (made on ULTRA" + (P.Eq(lv.key, Apip.FB_PRESET) ? "" : ", moved to " + P.Upper(lv.name) + "'s rate") + "). Your own training fits your tablet better.", 4); P.Line(""); }
                a = P.Lower(P.Trim(Ask("    Enter = start the training, s = " + SkipWord() + ": > ")));
                if (P.Eq(a, "s") || P.Eq(a, "f") || P.InputEnded()) return Skip(o);
            }

            Screen.Step("ALPI training 1/3: training firmware");
            bool[] s2 = new bool[st.Length];
            P.Copy(st, s2, st.Length);
            // the training firmware reports what the APIP build will filter: everything as chosen, except the filters
            // APIP replaces (R45: Jump Rejection too); startup calibration and bootloader maintenance belong to the final
            // flash
            string[] off = new string[] { "apip", "apipr2", "apipr3", "apipr4", "interp", "interp2", "smoothing", "odd", "cal", "bootloader", "blcounter" };
            for (int i = 0; i < off.Length; i++) s2[Build.SetIndex(off[i])] = false;
            Options o1 = Clone(o);
            o1.apipTrain = true;
            // R45-rc2: the fast stage keeps the pen down to the measurement firmware's levels, so the readings show how
            // high the pen can be tracked (the background measurement, Cli.FromTraining). Not with expert timing or gain
            // overrides: measured levels cannot be used with them, so the training then keeps the build's own levels.
            bool measure = Build.CalibrationProfileError(s2, o1) == null;
            if (measure) { o1.calPick = Cli.MEASURE_PICK; o1.calDrop = Cli.MEASURE_DROP; }
            string outp = Build.MarkVolatile(P.PathJoin(folder, lv.name + "-apip-training.hex"), s2, lv);
            int g = 0;
            try
            {
                Build.BuildOne(app, info, outp, lv, s2, true, false, o1);
                Cli.gateHeading = Cli.Heading(lv, s2) + " - ALPI training build";
                g = Cli.Gate(outp, Build.VolatileOn(s2, lv) ? 1 : 3, info.truncated, realtime);
            }
            catch (Stop e) { P.Line("    " + e.msg); g = 0; }
            finally { Cli.gateHeading = null; }
            if (g != 1) { P.Line(""); Ask("    The training build failed its check; " + NoTrain() + ". Press Enter > "); return UseFallback(o); }
            P.Line("");
            Screen.Para("Next: flash the training firmware. With it the cursor works as usual (without Position Interpolation, Movement Smoothing and Jump Rejection, and it follows the pen a little higher than usual), so keep your tablet driver and programs running.", 4);
            P.Line("");
            if (!Cli.BeforeFlash(folder, info)) { P.Line(""); Ask("    ALPI training skipped; " + NoTrain() + ". Press Enter > "); return UseFallback(o); }
            if (!Cli.FlashSteps(outp, Cli.backupLeftDfu, false)) { P.Line("    The training update did not complete; " + NoTrain() + "."); return UseFallback(o); }

            ApipData d = Collect(lv);
            if (d == null) return Abandon(o);
            ApipData hold = Hold();
            if (hold == null) return Abandon(o);

            Screen.Step("ALPI training results");
            ApipData h2 = hold.n >= 200 ? hold : null;
            ApipModel m = Apip.Fit(d, h2, lv, fr);
            // R45-rc2 (Anargon: "ALPI should combine all calibration steps as necessary, running them in the
            // background"): the same readings measure what Tablet signal thresholds measures
            Cli.CalResult cr = Cli.FromTraining(d, h2, lv, fr);
            if (!measure) { cr.ok = false; cr.notes.Add("Expert timing or gain overrides are set, so the pick-up levels were not measured."); }
            if (cr.ok) { m.pick = cr.pick; m.drop = cr.drop; }
            Apip.Save(path, m);
            m.file = path;
            StrList l = Describe(m, d);
            for (int i = 0; i < l.Count(); i++) { P.Line("    " + l.Get(i)); Build.LOG.Main("   ALPI training: " + l.Get(i)); }
            P.Line("");
            P.Line("    Measured in the background (what Standard calibration measures):");
            StrList cl = Cli.Describe(cr);
            for (int i = 0; i < cl.Count(); i++) { Screen.Para(cl.Get(i), 4); Build.LOG.Main("   ALPI training, measured: " + cl.Get(i)); }
            P.Line("");
            P.Line("    Saved to " + path + " (reuse it with --apip, or the wizard finds it in this folder)");
            o.apip = m;
            UseLevels(app, info, lv, st, o, m);
            P.Line(""); Ask("    Press Enter to build the ALPI firmware > ");
            return true;
        }

        // R45-rc2: the pick-up and drop levels a training measured go into the build, unless levels came from elsewhere
        // already (a calibration file) or this build's scan cannot use them (it then keeps the fallback or the preset's)
        static void UseLevels(byte[] app, InputInfo info, Level lv, bool[] st, Options o, ApipModel m)
        {
            if (m == null || m.pick <= 0 || m.drop <= 0 || o.calPick >= 0 || o.calDrop >= 0) return;
            o.calPick = m.pick; o.calDrop = m.drop;
            string why = null;
            Build.quiet = true;
            try { Build.BuildOne(app, info, null, lv, st, true, false, o); }
            catch (Stop e) { why = e.msg; }
            finally { Build.quiet = false; }
            P.Line("");
            if (why == null) { P.Line("    The build uses the measured levels: pick up above " + F.Str(m.pick) + ", drop below " + F.Str(m.drop) + "."); return; }
            o.calPick = -1; o.calDrop = -1;
            Screen.Para("The measured pick-up levels do not fit these settings (" + why + "), so the build keeps " + (Build.FallbackFor(lv, st, o) >= 0 ? Build.FB_WHO : "the preset's own levels") + ".", 4);
        }

        static bool Abandon(Options o)
        {
            P.Line("");
            Screen.Para((fb != null ? "ALPI uses " + FB_NAME + " instead (fallback)." : "Building without ALPI.") + " Until you flash another build the tablet keeps the training firmware, which works as a normal tablet without Position Interpolation, Movement Smoothing and Jump Rejection.", 4);
            P.Line(""); Ask("    Press Enter > ");
            return UseFallback(o);
        }

        // the reader of USB string 180 (the tablet restarts after the flash, so it is tried for up to 20 s)
        static bool Open()
        {
            for (int i = 0; i < 20; i++) { if (P.HidStringOpen(VID, PID, Apip.STRING)) return true; P.Sleep(1.0); }
            return false;
        }
        static int ReadOnce(ApipData d)
        {
            byte[] b = P.HidStringRead(Apip.STRING);
            return b == null ? -1 : Apip.TakeChars(d, b, 0, b.Length);
        }
        // reads for SECONDS into D (null: all failed); keep = only readings in the centre box with the pen in the air
        static int ReadFor(ApipData d, double seconds, bool keep)
        {
            double t0 = P.NowSec();
            int fails = 0;
            ApipData tmp = keep ? new ApipData() : d;
            if (keep) tmp.last = d.last;
            while (P.NowSec() - t0 < seconds)
            {
                int n = ReadOnce(tmp);
                if (n < 0) { if (++fails > 50) return -1; P.Sleep(0.02); continue; }
                fails = 0;
                if (n < Apip.PER_READ) P.Sleep(0.005);
            }
            if (keep)
            {
                for (int i = 0; i < tmp.n; i++)
                {
                    int dx = tmp.x[i] - Sched.ZONE_X * Apip.FINE, dy = tmp.y[i] - Sched.ZONE_Y * Apip.FINE, h = Sched.ZONE_HALF * Apip.FINE;
                    if (dx < -h || dx > h || dy < -h || dy > h || tmp.Pressure(i) > 0) continue;
                    d.Add(tmp.seq[i], tmp.x[i], tmp.y[i], tmp.lvl[i], tmp.aux[i], tmp.sig[i], tmp.fq[i]);
                }
                d.last = tmp.last;
            }
            return d.n;
        }

        static ApipData Collect(Level lv)
        {
            Screen.Step("ALPI training 2/3: use your tablet");
            Screen.Para("Use the tablet as you normally do: draw, write, move the cursor, hover, play. Cover the whole drawing area at three heights: touching, low hover (the tip within about 5 mm) and high hover (higher, while the cursor still follows). Only a moving pen counts. The LED pulses while the tablet collects.", 4);
            P.Line("");
            Screen.Para("Each block is the drawing area in 20 x 20 mm squares (left to right, top to bottom, as on your screen). A square needs about " + F.Fx(Apip.reqSeconds, 0) + " second of moving pen at that height (half for the bottom row, which is half a square); a number is how far it is (percent), OK = enough. The step ends by itself when every square shows OK; Enter stops it early.", 4);
            P.Line("");
            P.Line("    Connecting to the tablet...");
            if (!Open()) { P.Line("    The tablet does not answer the training read-out (USB string " + F.Str(Apip.STRING) + ")."); return null; }
            ApipData d = new ApipData();
            int rate = Apip.Rate(lv), top = P.CursorTop(), fails = 0, qe = P.QuickEditOff();
            double t0 = P.NowSec(), shown = -1;
            bool stopped = false, lost = false;
            Zone(Cli.ZONE_NONE, true);                                        // R45: the LED pulses while collecting
            try
            {
                ReadOnce(new ApipData());                                     // whatever was recorded before this step
                while (true)
                {
                    int n = ReadOnce(d);
                    if (n < 0)
                    {
                        // R45: what was collected still goes through the checks below (it skipped them before)
                        if (++fails > 100) { P.Line(""); P.Line("    The tablet stopped answering the training read-out."); lost = true; break; }
                        P.Sleep(0.02);
                    }
                    else { fails = 0; if (n < Apip.PER_READ) P.Sleep(0.005); }
                    double now = P.NowSec();
                    if (now - shown >= 0.5)
                    {
                        shown = now;
                        int touch;
                        int[,,] c = Apip.Coverage(d, lv, out touch);
                        bool done = Apip.Complete(c, rate);
                        StrList l = Apip.Table(c, rate);
                        l.Add("");
                        l.Add("    " + F.Str(d.n) + " readings, " + F.Fx(now - t0, 0) + " s" + (done ? ": every square is OK." : ".  Enter = stop early"));
                        if (top >= 0) P.SetCursor(0, top);
                        for (int i = 0; i < l.Count(); i++) P.Line(Txt.PadR(l.Get(i), 79));
                        if (done) break;
                    }
                    if (P.KeyHit()) { stopped = true; break; }
                }
            }
            finally { P.QuickEditRestore(qe); Zone(Cli.ZONE_NONE, false); }
            P.Line("");
            if (stopped || lost) P.Line("    Stopped early: the squares without OK use what the others learned.");
            Build.LOG.Main("   ALPI training: " + F.Str(d.n) + " readings in " + F.Fx(P.NowSec() - t0, 0) + " s (" + F.Str(d.reads) + " reads)" + (stopped ? ", stopped early" : lost ? ", the tablet stopped answering" : ""));
            if (d.n < 1000) { P.Line("    Too few readings to train (" + F.Str(d.n) + ")."); return null; }
            return d;
        }

        static bool Zone(int target, bool recording)
        {
            return P.Eq(P.HidIndexedString(VID, PID, Cli.ZoneString(target, recording)), Cli.ZoneReply(target, recording));
        }

        static ApipData Hold()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Screen.Step("ALPI training 3/3: centre hold");
                Screen.Para("Last, hold the pen still at a low hover in the middle of the tablet, within 7 mm of the centre. The LED is on while the tip hovers there, and off outside or while it touches. Press Enter when it is on: it then pulses while recording (" + F.Fx(HOLD_S, 0) + " s). Keep still until it stops pulsing. This hold is also the normal-hover measurement.", 4);
                P.Line("");
                bool z = Zone(Cli.ZONE_CENTRE, false);
                if (!z) P.Line("    (The tablet did not confirm the LED zone; hold the pen at the centre anyway.)");
                Ask("    Press Enter when the LED is on > ");
                Zone(Cli.ZONE_CENTRE, true);
                ApipData hold = new ApipData();
                ApipData skip = new ApipData();
                if (ReadFor(skip, SETTLE_S, false) < 0) { Zone(Cli.ZONE_NONE, false); P.Line("    The tablet stopped answering the training read-out."); return null; }
                hold.last = skip.last;
                P.Line("    Recording...");
                int n = ReadFor(hold, HOLD_S, true);
                Zone(Cli.ZONE_NONE, false);
                if (n < 0) { P.Line("    The tablet stopped answering the training read-out."); return null; }
                P.Line("    " + F.Str(n) + " readings in the centre box.");
                if (n >= 200) return hold;
                string a = P.Lower(P.Trim(Ask("    Too few readings in the box. Try again? (Y/n): > ")));
                if (P.Eq(a, "n") || P.InputEnded()) return new ApipData();
            }
            return new ApipData();
        }

        // what was learned, one line per resonance band
        public static StrList Describe(ApipModel m, ApipData d)
        {
            StrList l = new StrList();
            l.Add(F.Str((int)m.samples) + " readings (" + F.Str((int)m.moving) + " moving), centre hold " + F.Str((int)m.hold) + ", " + F.Str(m.rate) + " per second");
            l.Add("");
            l.Add("Signal band            readings   noise X    noise Y    follows per reading (still pen)");
            for (int b = 0; b < Apip.NBT; b++)
            {
                string name = b == Apip.NB ? "Touching" : b == Apip.NB + 1 ? "Steady Hover" : b == Apip.NB + 2 ? "Reach Stage" : b == 0 ? "Hover, level < " + F.Str(m.thr[0])
                    : b == Apip.NB - 1 ? "Hover, level " + F.Str(m.thr[Apip.NB - 2]) + "+" : "Hover, level " + F.Str(m.thr[b - 1]) + "-" + F.Str(m.thr[b] - 1);
                int n = m.count[b * 2] > m.count[b * 2 + 1] ? m.count[b * 2] : m.count[b * 2 + 1];
                l.Add(Txt.PadR(name, 22) + " " + Txt.PadL(F.Str(n), 8) + "   " + Txt.PadL(F.Fx(m.sigma[b * 2] / 100.0, 3), 5) + " mm   " + Txt.PadL(F.Fx(m.sigma[b * 2 + 1] / 100.0, 3), 5) + " mm   "
                    + F.Str((int)P.Rint(m.A[b * 2] * 100.0 / 4096)) + "% / " + F.Str((int)P.Rint(m.A[b * 2 + 1] * 100.0 / 4096)) + "% ("
                    + Pc1(m.AS[b * 2]) + " / " + Pc1(m.AS[b * 2 + 1]) + ")");
            }
            l.Add("");
            l.Add("(noise: how much a still pen's position moves between readings; bands without readings use the noise");
            l.Add(" gradient fitted over the others. \"follows\": how far each reading moves the cursor towards it; a reading far off");
            l.Add(" the path is followed at once. While the pen is still or moves steadily it is followed less, as in brackets.)");
            return l;
        }
    }
}
