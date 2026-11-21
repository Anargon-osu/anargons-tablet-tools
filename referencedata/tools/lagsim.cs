// AI DISCLOSURE: written by AI. Argument parsing by the vibeslop local swarm (Devstral 24B, round 2 of task
// alpi-lag-sim, 2026-11-16); its simulation loop and metrics were wrong (direction mixed up with axis, one axis per
// direction, wrong rms and shift) and were rewritten by Claude (Opus 5.5, Anthropic). AI DISCLOSURE END
//
// LagSim: replays ALPI (its C# copy, Apip.Axis, the touching band of Anargon's training at ULTRA, its own coil
// correction W zeroed) and Coil Linearization (CoilLinearization.Apply) on a synthetic ruler line at 1000 frames/s,
// both directions, 4 passes each, at 20 / 50 / 100 / 150 mm/s. The synthetic wave is the one Coil Linearization's
// table undoes. Per speed: the forward-minus-back sideways difference (rms, max), the coil-scale p-p of the mean of
// both directions, and the shift between the two directions' patterns (forward(a) = back(a - 2 shift); a lag tau of
// the wave gives shift = speed * tau). Gate (work area): experiments/noise45/lagsim_check.py.
// Build from anargons-tablet-tools: csc /nologo /langversion:5 /main:AnargonsTabletTools.LagSim /out:lagsim.exe
//   ..\gd32emu\gd32emu.cs atf_*.cs referencedata\tools\lagsim.cs
//   lagsim --order none|cl-only|alpi-cl|cl-alpi --wave 0|1 --noise MM --angle DEG --seed N
using System;
using System.Globalization;

namespace AnargonsTabletTools
{
    public static class LagSim
    {
        const string USAGE = "Usage: lagsim --order none|cl-only|alpi-cl|cl-alpi [alpi-cl] --wave 0|1 [1] --noise MM [0.05] --angle DEG [33] --seed N [1]";
        const int NBIN = 400;
        const double BIN = 0.25;

        static double Gauss(Random rng)
        {
            return Math.Sqrt(-2.0 * Math.Log(1.0 - rng.NextDouble())) * Math.Cos(2.0 * Math.PI * rng.NextDouble());
        }

        // the reading whose Coil Linearization gives t (fixed point, so the table undoes the wave exactly)
        static int Wave(int q, int t)
        {
            int z = t;
            for (int k = 0; k < 6; k++) z = t - (CoilLinearization.Apply(q, z, 4) - z);
            return z;
        }

        // one axis: q = 0 X, 1 Y; ord 0 none, 1 cl-only, 2 alpi-cl (the R45-rc5 firmware order), 3 cl-alpi
        static int Out(int ord, ApipModel m, int rec, ApipAxis s, int q, int z, bool warm)
        {
            if (ord == 0) return z;
            if (ord == 1) return CoilLinearization.Apply(q, z, 4);
            if (ord == 2) return CoilLinearization.Apply(q, Apip.Axis(m, rec, s, z, warm, 1), 4);
            if (ord == 4) return Apip.Axis(m, rec, s, z, warm, 1);
            return Apip.Axis(m, rec, s, CoilLinearization.Apply(q, z, 4), warm, 1);
        }

        public static int Main(string[] args)
        {
            string order = "alpi-cl";
            int wave = 1;
            double noise = 0.05;
            double angle = 33.0;
            int seed = 1;
            for (int i = 0; i < args.Length; i++)
            {
                string t = args[i];
                if (i + 1 >= args.Length) { Console.Error.WriteLine(USAGE); return 2; }
                if (t == "--order") order = args[++i];
                else if (t == "--wave") wave = int.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (t == "--noise") noise = double.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (t == "--angle") angle = double.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (t == "--seed") seed = int.Parse(args[++i], CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine(USAGE); return 2; }
            }
            int ord;
            if (order == "none") ord = 0;
            else if (order == "cl-only") ord = 1;
            else if (order == "alpi-cl") ord = 2;
            else if (order == "cl-alpi") ord = 3;
            else if (order == "alpi-only") ord = 4;
            else { Console.Error.WriteLine(USAGE); return 2; }

            double th = Math.PI * angle / 180.0, ux = Math.Cos(th), uy = Math.Sin(th), cx = 78.0, cy = 42.0;
            ApipModel m = Apip.Fallback(Build.Lv("1000"), true);
            for (int r = 0; r < m.W.Length; r++) if (m.W[r] != null) m.W[r] = new int[m.W[r].Length];
            int band = Apip.Band(m, 0, 0, true), recX = band * 2, recY = band * 2 + 1;
            Random rng = new Random(seed);
            int[] speeds = new int[] { 20, 50, 100, 150 };

            for (int si = 0; si < speeds.Length; si++)
            {
                int v = speeds[si];
                double[,] sum = new double[2, NBIN];
                int[,] cnt = new int[2, NBIN];
                for (int di = 0; di < 2; di++)
                {
                    int dir = di == 0 ? 1 : -1;
                    for (int rep = 0; rep < 4; rep++)
                    {
                        ApipAxis sx = new ApipAxis(), sy = new ApipAxis();
                        for (int i = 0; v * i / 1000.0 <= 110.0; i++)
                        {
                            // each pass starts a quarter frame later, so the 4 passes sample the line at other points
                            double a = dir * (-55.0 + v * (i + rep / 4.0) / 1000.0);
                            int xt = (int)Math.Round((cx + a * ux) * 200), yt = (int)Math.Round((cy + a * uy) * 200);
                            int zx = xt, zy = yt;
                            if (wave == 1) { zx = Wave(0, xt); zy = Wave(1, yt); }
                            zx += (int)Math.Round(Gauss(rng) * noise * 200);
                            zy += (int)Math.Round(Gauss(rng) * noise * 200);
                            int ox = Out(ord, m, recX, sx, 0, zx, i > 0), oy = Out(ord, m, recY, sy, 1, zy, i > 0);
                            if (i < 200) continue;
                            double xr = ox / 200.0, yr = oy / 200.0;
                            double ar = (xr - cx) * ux + (yr - cy) * uy, dr = -(xr - cx) * uy + (yr - cy) * ux;
                            if (ar < -50.0 || ar >= 50.0) continue;
                            int b = (int)Math.Floor((ar + 50.0) / BIN);
                            if (b < 0 || b >= NBIN) continue;
                            sum[di, b] += dr; cnt[di, b]++;
                        }
                    }
                }

                bool[] used = new bool[NBIN];
                double[] F = new double[NBIN], K = new double[NBIN];
                int nu = 0;
                double s2 = 0, fbMax = 0, mMax = double.NegativeInfinity, mMin = double.PositiveInfinity;
                for (int b = 0; b < NBIN; b++)
                {
                    if (cnt[0, b] == 0 || cnt[1, b] == 0) continue;
                    used[b] = true; nu++;
                    F[b] = sum[0, b] / cnt[0, b]; K[b] = sum[1, b] / cnt[1, b];
                    double df = F[b] - K[b], mid = (F[b] + K[b]) / 2.0;
                    s2 += df * df;
                    if (Math.Abs(df) > fbMax) fbMax = Math.Abs(df);
                    if (mid > mMax) mMax = mid;
                    if (mid < mMin) mMin = mid;
                }
                double fbRms = nu > 0 ? Math.Sqrt(s2 / nu) : 0.0, coilPp = nu > 0 ? mMax - mMin : 0.0;

                double bestErr = double.PositiveInfinity, bestShift = 0.0;
                for (int k = -40; k <= 40; k++)
                {
                    double sh = k * 0.025, err = 0.0;
                    int n = 0;
                    for (int b = 0; b < NBIN; b++)
                    {
                        if (!used[b]) continue;
                        double at = -50.0 + (b + 0.5) * BIN - 2.0 * sh;
                        double u = (at + 50.0) / BIN - 0.5;
                        int j0 = (int)Math.Floor(u);
                        if (j0 < 0 || j0 + 1 >= NBIN || !used[j0] || !used[j0 + 1]) continue;
                        double f = u - j0, kat = K[j0] * (1 - f) + K[j0 + 1] * f, e = F[b] - kat;
                        err += e * e; n++;
                    }
                    if (n == 0) continue;
                    err /= n;
                    if (err < bestErr) { bestErr = err; bestShift = sh; }
                }
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "speed={0} fb_rms={1:0.0000} fb_max={2:0.0000} coil_pp={3:0.0000} shift={4:0.000}", v, fbRms, fbMax, coilPp, bestShift));
            }
            return 0;
        }
    }
}
