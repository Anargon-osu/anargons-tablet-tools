// Compile with the project sources and /main:AnargonsTabletTools.CalibrationRegressions.
// Optional argument: an untouched stock main.bin. No test opens a real tablet.
using System;
using System.IO;
using System.Text;
using Gd32Emu;

namespace AnargonsTabletTools
{
    public static class CalibrationRegressions
    {
        static int checks;
        // R45-rc3 parallel runs (tests/run-regressions.py --jobs): ADT_TEST_GROUPS=a,b runs only those groups and
        // ADT_TEST_LIST_GROUPS=1 prints every group ("GROUP name") and runs none; unset, every group runs, as before.
        // Every check is inside a group: setup outside one runs in every process, so a check there would be counted
        // once per process.
        static bool Group(string name)
        {
            if (Environment.GetEnvironmentVariable("ADT_TEST_LIST_GROUPS") == "1") { P.Line("GROUP " + name); return false; }
            string only = Environment.GetEnvironmentVariable("ADT_TEST_GROUPS");
            return string.IsNullOrEmpty(only) || ("," + only + ",").IndexOf("," + name + ",", StringComparison.Ordinal) >= 0;
        }

        static void Check(bool ok, string name)
        {
            checks++;
            if (!ok) throw new Stop("FAIL: " + name);
        }

        static void Record(Cli.IntBuf records, int page, int time, int x, int y, int pressure, int status)
        {
            int[] data = new int[] { 7, 0xC0, x & 255, (x >> 8) & 255, y & 255, (y >> 8) & 255,
                pressure & 255, (pressure >> 8) & 255, status, 0xE0 | page, time };
            for (int i = 0; i < data.Length; i++) records.Add(data[i]);
        }

        static void DecodeChecks()
        {
            Cli.IntBuf records = new Cli.IntBuf();
            Record(records, 0, 0, 200, 300, 10, 0);
            Check(Cli.Decode(records.Take(), true).nSig == 0, "signal before a pressure page is not treated as light contact");
            Record(records, 1, 1000, 8000, 4500, 0, 0x20);
            Record(records, 0, 2000, 200, 300, 10, 0);
            Check(Cli.Decode(records.Take(), true).nSig == 1, "fresh in-range pressure permits a signal reading");
            Record(records, 1, 3000, 8000, 4500, 5000, 0x20);          // R44: above the 4096 light touch
            Record(records, 0, 4000, 200, 300, 10, 0);
            Check(Cli.Decode(records.Take(), true).nSig == 1, "heavy pressure is excluded from light-contact readings");
            Check(Cli.Decode(records.Take(), false).nSig == 2, "unfiltered steps retain tracked pressed readings");

            records = new Cli.IntBuf();
            Record(records, 1, 1000, 8000, 4500, 0, 0x20);
            Record(records, 0, 102000, 200, 300, 10, 0);
            Check(Cli.Decode(records.Take(), true).nSig == 0, "stale pressure is not reused for a later signal");
            Record(records, 1, 103000, 8000, 4500, 0, 0);
            Record(records, 0, 104000, 200, 300, 10, 0);
            Check(Cli.Decode(records.Take(), true).nSig == 0, "out-of-range position does not qualify signal");

            records = new Cli.IntBuf();
            Record(records, 1, 10000, 8000, 4500, 0, 0x20);
            Record(records, 0, 5000, 200, 300, 10, 0);
            Check(Cli.Decode(records.Take(), true).nSig == 0, "backward timestamps invalidate pressure association");
            int[] raw = records.Take(); raw[0] = 2; raw[11] = 2;
            Check(Cli.Decode(raw, true).nRep == 0, "alternate-format ID 2 reports are never treated as calibration data");
            raw[0] = 7; raw[11] = 7; raw[1] = 0xA0; raw[12] = 0xA0;
            Check(Cli.Decode(raw, true).nRep == 0, "ordinary ID 7 pen reports are never calibration data");
            raw[1] = 0xC0; raw[12] = 0xC0; raw[9] = 0xE5; raw[20] = 0xE5;
            Check(Cli.Decode(raw, true).nRep == 0, "unknown telemetry pages are refused");
            raw[9] = 0xE4; raw[20] = 0xE4;
            Check(Cli.Decode(raw, true).nRep > 0, "R43: page 4 (R42 Dual-coil probe builds) is still a calibration page, its content ignored");
            Check(!Cli.TelemetryRecord(new int[3], 0), "short report is refused");

            records = new Cli.IntBuf();
            Record(records, 1, 1000, 16001, 4500, 0, 0x20);
            Record(records, 0, 2000, 200, 300, 10, 0);
            Check(Cli.Decode(records.Take(), true).nSig == 0, "invalid tablet coordinates cannot supply pressure context");

            records = new Cli.IntBuf();
            Record(records, 2, 0, 31562, 0, 0, 0);
            Check(Cli.Decode(records.Take(), true).nFreq == 0, "frequency needs fresh in-range pressure context");
            Record(records, 1, 1000, 8000, 4500, 0, 0x20);
            Record(records, 2, 2000, 31562, 0, 0, 0);
            Cli.Tele frequency = Cli.Decode(records.Take(), true);
            Check(frequency.nFreq == 1 && frequency.freq[0] == 504992, "frequency page decodes in 16 Hz steps");
            Check(P.IndexOf(Cli.ResonanceText(frequency), "504.992 kHz") >= 0, "resonance display preserves telemetry resolution");
            Record(records, 2, 102000, 31562, 0, 0, 0);
            Check(Cli.Decode(records.Take(), true).nFreq == 1, "stale pressure cannot qualify resonance");
            Record(records, 1, 103000, 8000, 4500, 5000, 0x20);      // R44: above the 4096 light touch
            Record(records, 2, 104000, 31562, 0, 0, 0);
            Check(Cli.Decode(records.Take(), false).nFreq == 1, "pressed resonance is excluded even from unfiltered signal steps");
            Record(records, 1, 105000, 8000, 4500, 0, 0x20);
            Record(records, 2, 106000, 65535, 0, 0, 0);
            Check(Cli.Decode(records.Take(), true).nFreq == 1, "out-of-band frequency is refused");
            AirChecks();
        }

        // R41: the hover steps record every fresh frequency reading and the reports that carried pressure, and the
        // results say when this build can show pressure with the pen in the air
        static void AirChecks()
        {
            Cli.IntBuf hover = new Cli.IntBuf(), edge = new Cli.IntBuf();
            Record(hover, 1, 1000, 8000, 4500, 0, 0x20);
            Record(hover, 2, 2000, 31562, 0, 0, 0);                  // 504.992 kHz, no pressure
            Record(hover, 1, 3000, 8000, 4500, 300, 0x20);           // a false press
            Record(hover, 2, 4000, 31875, 0, 0, 0);                  // 510.000 kHz, in the pressure band
            Record(edge, 1, 1000, 8000, 4500, 0, 0x20);
            Record(edge, 2, 2000, 31500, 0, 0, 0);                   // 504.000 kHz
            Cli.Tele h = Cli.Decode(hover.Take(), false), e = Cli.Decode(edge.Take(), false);
            Check(h.nAirFreq == 2 && h.nIn == 2 && h.nPress == 1 && h.nFreq == 2, "hover steps keep every frequency reading and count reports with pressure");
            Cli.Tele[] steps = new Cli.Tele[] { new Cli.Tele(), h, e };            // R45-rc2: light touch, normal and highest hover
            Cli.CalResult r = Cli.Derive(steps, Build.Lv("800"));
            Check(r.nAir == 3 && r.nAirHigh == 1 && r.airPress == 1 && r.airIn == 3 && r.airMax == 510000 && r.airMed == 504992,
                "hover frequency summary counts readings at or above 509 kHz");
            Check(P.IndexOf(Cli.AirText(r), "1 of 3 readings at or above it") >= 0 && P.IndexOf(Cli.AirText(r), "1 of 3 reports") >= 0,
                "hover frequency text names the readings and the false presses");
            bool[] st = Cli.Defaults();
            Check(P.IndexOf(Cli.AirAdvice(r, Build.Lv("800"), st), "with Accurate Pressure Readings on") >= 0, "advice with the setting on");
            st[Build.SetIndex("accp")] = false;
            Check(P.IndexOf(Cli.AirAdvice(r, Build.Lv("800"), st), "turn it on in the Noise-Related Patches") >= 0, "advice with the setting off");
            Check(P.IndexOf(Cli.AirAdvice(r, Build.Lv("1000"), Cli.Defaults()), "too short") >= 0, "advice on ULTRA");
            Cli.CalResult quiet = Cli.Derive(new Cli.Tele[] { new Cli.Tele(), e, e }, Build.Lv("800"));
            Check(!Cli.AirPressRisk(quiet) && Cli.AirAdvice(quiet, Build.Lv("800"), Cli.Defaults()) == null, "no advice without readings in the pressure band");
        }

        // A scripted tablet for the readiness check: each Open() starts a new attempt; GOOD[attempt] says whether that
        // attempt sends calibration readings. Nothing here opens a real tablet.
        class ScriptedSource : Cli.CalSource
        {
            public bool[] good; public string why = "no reports at all from 1 open collection(s)"; public string openError = "";
            public int opens, collects;
            public override string Open() { opens++; return openError; }
            public override int[] Collect(int step, double seconds)
            {
                collects++;
                Cli.IntBuf records = new Cli.IntBuf();
                if (opens - 1 < good.Length && good[opens - 1])
                    for (int i = 0; i < 8; i++)
                    {
                        Record(records, 1, i * 2000 + 1000, 8000, 4500, 300, 0x20);
                        Record(records, 0, i * 2000 + 2000, 200, 300, 10, 0);
                    }
                return records.Take();
            }
            public override void Close() { }
            public override string Why() { return Collects() ? "" : why; }
            bool Collects() { return opens - 1 < good.Length && good[opens - 1]; }
        }

        static string readyOutput;
        static int Ready(ScriptedSource source, string input)
        {
            TextReader oldIn = Console.In; TextWriter oldOut = Console.Out;
            StringWriter output = new StringWriter();
            Console.SetIn(new StringReader(input)); Console.SetOut(output);
            int result;
            try { result = Cli.ReadyCheck(source); }
            catch (Stop) { result = -1; }
            finally { Console.SetIn(oldIn); Console.SetOut(oldOut); }
            // wrapped paragraphs: compare words, not line breaks
            readyOutput = string.Join(" ", output.ToString().Split(new char[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            return result;
        }

        static void ReadyChecks()
        {
            Cli.readyPause = 0;
            ScriptedSource s = new ScriptedSource(); s.good = new bool[] { true };
            Check(Ready(s, "\n") == Cli.READY && s.opens == 1, "readings at the first check go straight to the timed steps");

            s = new ScriptedSource(); s.good = new bool[] { false, false, true };
            Check(Ready(s, "\n") == Cli.READY && s.opens == 3 && readyOutput.IndexOf("checking again (2 of", StringComparison.Ordinal) >= 0,
                "a failed check reopens the tablet and checks again by itself, without a keypress");

            s = new ScriptedSource(); s.good = new bool[0];
            Check(Ready(s, "\n\n") == -1 && s.opens == 2 + Cli.READY_AUTO, "after the automatic re-checks it asks; Enter checks again (it never falls through to the defaults)");
            Check(readyOutput.IndexOf("No reports are arriving from the tablet (no reports at all", StringComparison.Ordinal) >= 0
                && readyOutput.IndexOf("ID 2 uses", StringComparison.Ordinal) < 0, "no reports at all is reported as such, not as an ID 2 problem");

            s = new ScriptedSource(); s.good = new bool[0];
            Check(Ready(s, "\nd\n") == Cli.USE_DEFAULTS && s.opens == 1 + Cli.READY_AUTO, "d continues with the preset defaults");
            s = new ScriptedSource(); s.good = new bool[0];
            Check(Ready(s, "\nx\nf\n") == Cli.REFLASH, "f flashes the calibration build again; other answers ask again");

            s = new ScriptedSource(); s.good = new bool[] { false, false, false, false, true };
            Check(Ready(s, "\n\n") == Cli.READY && s.opens == 2 + Cli.READY_AUTO, "Enter after the automatic re-checks checks again and continues once readings arrive");

            s = new ScriptedSource(); s.good = new bool[0]; s.openError = "no Deco 640 found";
            Check(Ready(s, "d\n") == Cli.USE_DEFAULTS && s.opens == 1 && s.collects == 0
                && readyOutput.IndexOf("tip resting lightly on the tablet >", StringComparison.Ordinal) < 0
                && readyOutput.IndexOf("could not be opened for calibration: no Deco 640 found", StringComparison.Ordinal) >= 0,
                "a failed open asks at once, with the reason (the open already waited)");

            s = new ScriptedSource(); s.good = new bool[0]; s.why = "reports arrive with ID 2; calibration requires report ID 7";
            Check(Ready(s, "\nd\n") == Cli.USE_DEFAULTS && readyOutput.IndexOf("may not be running: flash it again (f)", StringComparison.Ordinal) >= 0,
                "other report IDs point to flashing the calibration build again");
        }

        static void NamingChecks()
        {
            bool[] settings = Cli.Defaults();
            Level fast = Build.Lv("500");
            Check(P.Eq(Build.AutoName(fast, settings, false), "[VOLATILE]_fast.hex"), "default filename shows speed and volatile status only");
            settings[Build.SetIndex("smoothing")] = true;
            Check(P.Eq(Build.AutoName(fast, settings, false), "[VOLATILE]_fast-SM.hex"), "default-off extra uses short tag");
            settings[Build.SetIndex("interp")] = false;
            Check(P.Eq(Build.AutoName(fast, settings, false), "[VOLATILE]_fast-SM-noPI.hex"), "disabled default uses short no tag");
            settings = Cli.Defaults(); settings[Build.SetIndex("volatile")] = false;
            Check(P.Eq(Build.AutoName(fast, settings, false), "fast.hex"), "no volatile prefix when disabled on supported speed");
            Check(P.Eq(Build.AutoName(Build.Lv("1000"), settings, false), "[VOLATILE]_ultra.hex"), "required volatile changes retain prefix despite requested off");
            Check(P.Eq(Build.MarkVolatile(P.PathJoin("builds", "chosen.hex"), settings, Build.Lv("1000")), P.PathJoin("builds", "[VOLATILE]_chosen.hex")), "explicit filename gets prefix without changing its directory");
            Check(P.Eq(Build.MarkVolatile("[VOLATILE]_chosen.hex", Cli.Defaults(), fast), "[VOLATILE]_chosen.hex"), "volatile prefix is not duplicated");
            Check(P.Eq(Build.MarkVolatile("[VOLATILE]chosen.hex", Cli.Defaults(), fast), "[VOLATILE]chosen.hex"), "an older-style prefix is not doubled either");
            // R42: every hover helper off = "-rawhover" instead of one -no tag each
            settings = Cli.Defaults(); Level rapid = Build.Lv("800");
            settings[Build.SetIndex("boost")] = false;
            Check(P.Eq(Build.AutoName(rapid, settings, false), "[VOLATILE]_rapid-noSH.hex"), "Steady Hover off alone keeps its own tag");
            settings[Build.SetIndex("longrange")] = false;
            Check(P.Eq(Build.AutoName(rapid, settings, false), "[VOLATILE]_rapid-rawhover.hex"), "every hover helper off is named rawhover");
            settings[Build.SetIndex("smoothing")] = true;
            Check(P.Eq(Build.AutoName(rapid, settings, false), "[VOLATILE]_rapid-SM-rawhover.hex"), "rawhover goes after the other tags");
            settings = Cli.Defaults(); settings[Build.SetIndex("longrange")] = false;
            Check(P.Eq(Build.AutoName(Build.Lv("300"), settings, false), "[VOLATILE]_quick-rawhover.hex"), "QUICK has only Long-range Pickup, so it alone makes rawhover");
            Check(P.Eq(Build.AutoName(Build.Lv("220"), settings, false), "[VOLATILE]_stock.hex"), "STOCK has no hover helpers, so never rawhover");
        }

        static Cli.Tele Measured(long signal)
        {
            Cli.Tele t = new Cli.Tele(); t.nSig = 20; t.nPos = 20; t.nRep = 40; t.seconds = 4;
            for (int i = 0; i < 20; i++) { t.sig[i] = signal; t.sigMax[i] = signal; t.lvl[i] = 500; t.px[i] = 8000; t.py[i] = 4500; }
            return t;
        }

        static string CalibrationText(string values)
        {
            return "format=2\nprofile=default-fast-stock-frequency\npreset=500\n" + values;
        }

        static void RejectFile(string path, string contents, string name)
        {
            P.WriteAllBytes(path, Build.Encode(contents));
            Options options = new Options(); options.calPick = 77; options.calDrop = 33;
            bool rejected = false;
            try { Cli.LoadCal(path, options); } catch (Stop) { rejected = true; }
            Check(rejected, name);
            Check(options.calPick == 77 && options.calDrop == 33, "invalid calibration is applied atomically: " + name);
        }

        static void CalibrationChecks(string work)
        {
            bool[] defaults = Cli.Defaults();
            Check(!defaults[Build.SetIndex("cal")], "default builds retain the stock scan frequency");
            Check(Build.CalibrationProfileError(defaults, new Options()) == null, "default scan is eligible for calibration");
            defaults[Build.SetIndex("cal")] = true;
            Check(Build.CalibrationProfileError(defaults, new Options()) != null, "startup frequency changes invalidate measured thresholds");
            defaults[Build.SetIndex("cal")] = false; defaults[Build.SetIndex("multicoil")] = true;
            // R43: Multi-coil Read changes how positions are computed, not the scan the thresholds are measured with
            Check(Build.CalibrationProfileError(defaults, new Options()) == null, "R43: Multi-coil Read leaves the measured scan unchanged, so calibration accepts it");
            defaults[Build.SetIndex("multicoil")] = false;
            Options custom = new Options(); custom.samples = 9;
            Check(Build.CalibrationProfileError(defaults, custom) != null, "custom sample timing is refused");

            Cli.Tele[] measurements = new Cli.Tele[] { Measured(8000), Measured(5000), Measured(4000) };
            for (int i = 1; i < Build.LEVELS.Length; i++)
            {
                Level level = Build.LEVELS[i]; Cli.CalResult result = Cli.Derive(measurements, level);
                Check(result.ok && result.pick <= Build.Pick(level.samples, level.burst, 1.0)
                    && result.drop <= Build.Drop(level.samples, level.burst, 1.0) && result.pick > result.drop,
                    level.name + ": strong hover cannot tighten default pickup thresholds");
                Check(result.boostAt == 0 && result.away == -1, level.name + ": absent noise does not retune hover transitions");
            }
            Cli.CalResult calibration = Cli.Derive(measurements, Build.Lv("500")); calibration.preset = "500";
            measurements[1].nPos = 0;
            Check(!Cli.Derive(measurements, Build.Lv("500")).ok, "missing hover positions cannot produce a usable calibration");
            measurements[1].nPos = 20;
            string file = P.PathJoin(work, "saved.txt"); Cli.SaveCal(file, calibration);
            Options restored = new Options();
            Check(P.Eq(Cli.LoadCal(file, restored), "500") && restored.calPick == calibration.pick && restored.calDrop == calibration.drop,
                "current calibration file round-trips its measured profile");
            RejectFile(file, "preset=500\npick=100\ndrop=70\nboost_at=0\nodd_mm=0.5\n", "legacy file without measurement profile is refused");
            RejectFile(file, CalibrationText("pick=100\ndrop=70\nboost_at=0\nodd_mm=NaN\n"), "NaN margin is refused");
            RejectFile(file, CalibrationText("pick=100\ndrop=70\nboost_at=0\nodd_mm=Infinity\n"), "infinite margin is refused");
            RejectFile(file, CalibrationText("pick=4294967396\ndrop=70\nboost_at=0\nodd_mm=0.5\n"), "overflowed threshold is refused");
            RejectFile(file, CalibrationText("pick=100\ndrop=100\nboost_at=0\nodd_mm=0.5\n"), "unordered thresholds are refused");
            RejectFile(file, CalibrationText("pick=301\ndrop=70\nboost_at=0\nodd_mm=0.5\n"), "threshold above the preset default is refused");
            RejectFile(file, CalibrationText("pick=100\npick=101\ndrop=70\nboost_at=0\nodd_mm=0.5\n"), "duplicate values are refused");
            RejectFile(file, CalibrationText("pick=100\nboost_at=0\nodd_mm=0.5\n"), "missing drop value is refused");
            P.DeleteFile(file);
        }

        static Pen TestPen()
        {
            // Match a known sensitivity-gate pen so this test isolates USB format selection.
            Pen pen = new Pen(1); pen.f0 = 503000; pen.Q = 300; pen.strength = 6200;
            return pen;
        }

        static Cli.Tele Reports(Machine machine, int start)
        {
            Cli.IntBuf records = new Cli.IntBuf();
            for (int i = start; i < machine.reports.n; i++)
            {
                Report report = machine.reports.a[i];
                if (report.ep != 2 || report.data.Length < 10) continue;
                for (int k = 0; k < 10; k++) records.Add(report.data[k] & 255);
                records.Add((int)(report.cycle / 72));
            }
            return Cli.Decode(records.Take(), false);
        }

        static void MeasurementBootloaderChecks(byte[] app, InputInfo info)
        {
            bool[] settings = Cli.Defaults();
            settings[Build.SetIndex("bootloader")] = true;
            settings[Build.SetIndex("blcounter")] = true;
            string path = Cli.BuildPass1(app, info, Build.Lv("500"), settings, "cal-work", false);
            Check(path != null, "measurement build passes its gate with bootloader maintenance requested for the final build");
            Check(settings[Build.SetIndex("bootloader")] && settings[Build.SetIndex("blcounter")],
                "measurement build preserves the caller's final bootloader settings");
            byte[] measurement = Twin.LoadImage(path);
            P.Copy(info.boot, measurement, info.boot.Length);
            StringBuilder why = new StringBuilder();
            Machine machine = Twin.ScanMachine(measurement, TestPen(), false, why);
            Check(machine != null, "measurement build boots with the tablet's stock bootloader: " + why.ToString());
            machine.RunMs(30);
            bool unchanged = true;
            for (int i = 0; i < info.boot.Length; i++) if (machine.flash[i] != info.boot[i]) unchanged = false;
            Check(unchanged && machine.flashWrites == 0, "measurement build performs no bootloader or counter-page writes");

            Image normal = Build.BuildOne(app, info, null, Build.Lv("500"), settings, true, false, new Options());
            byte[] normalFlash = Twin.FromApp(normal.Bytes());
            P.Copy(info.boot, normalFlash, info.boot.Length);
            why = new StringBuilder();
            Machine restored = Twin.ScanMachine(normalFlash, TestPen(), false, why);
            Check(restored != null && restored.flashWrites > 0
                && Machine.LE(restored.flash, Sched.BL_ERASE - 0x08000000, 4) == Sched.BlPatchWord(),
                "final normal build retains the requested Bootloader Counter installation");
            P.DeleteFile(path);
        }

        static void NormalReportMode(Image image, string preset)
        {
            StringBuilder why = new StringBuilder(); Machine control = Twin.ScanMachine(Twin.FromApp(image.Bytes()), TestPen(), false, why);
            Check(control != null, preset + ": normal firmware boots for report-mode control");
            control.RunMs(180);
            Check(Twin.InRange(control, 0) > 5, preset + ": normal firmware tracks the control pen before report-mode change");
            control.Write(Sched.REPORT_MODE, 1, 4); int start = control.reports.n;
            // The virtual host automatically polls every advertised interrupt IN endpoint.
            control.RunMs(180);
            int alternateReports = 0;
            for (int i = start; i < control.reports.n; i++)
                if (control.reports.a[i].ep == 3 && control.reports.a[i].data.Length == 12 && control.reports.a[i].data[0] == 2) alternateReports++;
            if (alternateReports <= 5)
            {
                P.Line("Control report mode=" + F.Str(control.Read(Sched.REPORT_MODE, 1)) + ", reports=" + F.Str(control.reports.n - start));
                for (int i = start; i < control.reports.n && i < start + 8; i++)
                    P.Line("Control ep=" + F.Str(control.reports.a[i].ep) + " data=" + Txt.HexBytes(control.reports.a[i].data));
            }
            Check(control.Read(Sched.REPORT_MODE, 1) == 4 && alternateReports > 5 && Reports(control, start).nRep == 0,
                preset + ": normal mode 4 automatically delivers genuine 12-byte ID 2 reports on EP3 without telemetry");
            Check(control.Read(Sched.VB_ADDR + 12, 1) == 0 && control.Read(Sched.VB_ADDR + 13, 1) == 0,
                preset + ": default firmware has no startup frequency calibration state or offset");
        }

        // R44: milliseconds (of MS) the LED (PC13, active-low) is on
        static int LedOnMs(Machine machine, int ms)
        {
            int on = 0;
            for (int t = 0; t < ms; t++) { machine.RunMs(1); if ((machine.periph.Get(0x48000814, 0) & 0x2000) == 0) on++; }
            return on;
        }

        // the reported position (median of the position pages since START), in report units
        static int[] Where(Machine machine, int start)
        {
            Cli.Tele t = Reports(machine, start);
            if (t.nPos == 0) return new int[] { -1, -1 };
            long[] x = new long[t.nPos], y = new long[t.nPos];
            P.Copy(t.px, x, t.nPos); P.Copy(t.py, y, t.nPos);
            return new int[] { (int)Cli.Pct(x, t.nPos, 0.5), (int)Cli.Pct(y, t.nPos, 0.5) };
        }

        // R44 LED zone in the calibration-pass build: the PC sets the target and "recording" through USB strings
        // 176-179; the LED is off without a pen or outside the 14 x 14 mm box, on inside it, blinking while recording
        static void LedZoneChecks(byte[] img, string name)
        {
            Cli.TwinSource src = new Cli.TwinSource(); src.img = img;
            Check(P.Len(src.Open()) == 0, name + ": LED zone: calibration firmware boots");
            Machine m = src.mc; Pen pen = src.pen;
            pen.present = true; pen.height = 5; pen.x = 16.44; pen.y = 10.12;
            m.RunMs(300);
            int at = m.reports.n; int on = LedOnMs(m, 400); int[] c = Where(m, at);
            P.Line("   " + name + ": pen over the centre reports " + F.Str(c[0]) + ", " + F.Str(c[1]) + "; LED on " + F.Str(on) + "/400 ms before any zone");
            Check(on >= 380, name + ": before the PC sets a zone, the LED is on while the pen is tracked");
            bool inside = Math.Abs(c[0] - Sched.ZONE_X) <= Sched.ZONE_HALF && Math.Abs(c[1] - Sched.ZONE_Y) <= Sched.ZONE_HALF;
            Check(inside, name + ": the twin pen over the centre reports inside the 14 x 14 mm box");
            Check(src.Zone(Cli.ZONE_CENTRE, false), name + ": the tablet confirms the centre target");
            on = LedOnMs(m, 400);
            Check(on >= 380, name + ": centre target, pen inside: LED on (" + F.Str(on) + "/400)");
            Check(src.Zone(Cli.ZONE_CENTRE, true), name + ": the tablet confirms recording");
            on = LedOnMs(m, 1000);
            Check(on >= 400 && on <= 600, name + ": recording, pen inside: LED blinks (" + F.Str(on) + "/1000 ms on)");
            // move 9 mm right (X coils run right to left: lower coil number = further right), still tracked
            pen.x = 16.44 - 9.0 / 5.08;
            m.RunMs(100); at = m.reports.n; on = LedOnMs(m, 400); int[] o = Where(m, at);
            P.Line("   " + name + ": pen 9 mm off the centre reports " + F.Str(o[0]) + ", " + F.Str(o[1]) + "; LED on " + F.Str(on) + "/400 ms");
            Check(o[0] >= 0 && Math.Abs(o[0] - Sched.ZONE_X) > Sched.ZONE_HALF && on <= 20, name + ": recording, pen outside the box: LED off");
            pen.x = 16.44 - 5.0 / 5.08;                 // 5 mm right: inside the box
            m.RunMs(100); at = m.reports.n; on = LedOnMs(m, 1000); o = Where(m, at);
            Check(o[0] >= 0 && Math.Abs(o[0] - Sched.ZONE_X) <= Sched.ZONE_HALF && on >= 400 && on <= 600,
                name + ": 5 mm off the centre is inside the box: LED blinks (" + F.Str(on) + "/1000, x " + F.Str(o[0]) + ")");
            pen.x = 16.44 - 9.0 / 5.08;
            Check(src.Zone(Cli.ZONE_NONE, true), name + ": the tablet confirms recording without a target");
            m.RunMs(100); on = LedOnMs(m, 1000);
            Check(on >= 400 && on <= 600, name + ": no target, pen anywhere: LED blinks while recording (" + F.Str(on) + "/1000)");
            Check(src.Zone(Cli.ZONE_NONE, false), name + ": the tablet confirms no target, not recording");
            on = LedOnMs(m, 400);
            Check(on >= 380, name + ": no target, not recording, pen tracked: LED on (" + F.Str(on) + "/400)");
            pen.present = false;
            m.RunMs(300); on = LedOnMs(m, 400);
            Check(on == 0, name + ": no pen: LED off (" + F.Str(on) + "/400)");
            Check(src.Zone(Cli.ZONE_CENTRE, true), name + ": centre target and recording again");
            on = LedOnMs(m, 400);
            Check(on == 0, name + ": no pen while recording: LED off (" + F.Str(on) + "/400)");
            string bad = "";
            try { src.ToMainLoop(); byte[] d = src.host.GetDescriptor(3, Sched.ZONE_STRING + 2 * Sched.ZONE_TARGETS, 255, 0x0409, 0x80); bad = d.Length == 0 ? "stall" : F.Str(d.Length); }     // the virtual host returns nothing for a stall
            catch (Fault) { bad = "stall"; }
            catch (Stop) { bad = "stall"; }
            Check(P.Eq(bad, "stall"), name + ": a string past the zone range still stalls (" + bad + ")");
            Check(src.zones.Count() == 5, name + ": every confirmed zone was recorded");
        }

        // R45-rc2 (Anargon: "ALPI should combine all calibration steps as necessary, running them in the background"):
        // the training firmware records each sample's detection signal and pen frequency, and the training measures
        // the guided steps' values from them (Cli.FromTraining)
        static void BackgroundChecks(byte[] app, InputInfo info)
        {
            string[] keys = new string[] { "1000", "800" };
            for (int k = 0; k < keys.Length; k++)
            {
                Level lv = Build.Lv(keys[k]);
                bool[] st = Cli.Defaults();
                string[] off = new string[] { "apip", "apipr2", "apipr3", "apipr4", "interp", "interp2", "smoothing", "odd", "cal", "bootloader", "blcounter" };
                for (int i = 0; i < off.Length; i++) st[Build.SetIndex(off[i])] = false;
                Options o = new Options(); o.apipTrain = true; o.calPick = Cli.MEASURE_PICK; o.calDrop = Cli.MEASURE_DROP;
                Image img = Build.BuildOne(app, info, null, lv, st, true, false, o);
                Check(P.IndexOf(Build.OptionsText(lv, st, o, Cli.MEASURE_PICK, Cli.MEASURE_DROP, false, null), "(MEASUREMENT-LEVELS)") >= 0,
                    lv.name + ": the training build's options text does not call its levels measured");
                byte[] fl = Twin.FromApp(img.Bytes());
                TrainCheckResult tc = Apip.TrainCheck(fl);
                Check(tc.ok, lv.name + ": the training firmware at the measurement levels passes its check (" + F.Str(tc.signals) + " of "
                    + F.Str(tc.samples) + " samples with a signal, " + F.Str(tc.freqs) + " with a pen frequency) " + tc.why);
                ApipData hold = new ApipData();
                ApipData d = Apip.SimTrain(fl, 6, 3 + k, hold);
                bool fr = Build.On(st, "fullres") && Build.Unavailable("fullres", lv, st) == null;
                Cli.CalResult r = Cli.FromTraining(d, hold, lv, fr);
                StrList l = Cli.Describe(r);
                for (int i = 0; i < l.Count(); i++) P.Line("   " + lv.name + " in the background: " + l.Get(i));
                int pickCap = Build.Pick(lv.samples, lv.burst, 1.0), dropCap = Build.Drop(lv.samples, lv.burst, 1.0);
                Check(r.ok && r.background && r.pick > r.drop && r.drop >= 24 && r.pick <= pickCap && r.drop <= dropCap,
                    lv.name + ": the training's readings give pick-up levels within the preset's (" + F.Str(r.pick) + "/" + F.Str(r.drop) + ", caps " + F.Str(pickCap) + "/" + F.Str(dropCap) + ")");
                Check(r.hover > 0 && r.levelHover > 0 && r.nAir > 0 && P.IndexOf(Cli.AirText(r), "kHz") >= 0 && P.IndexOf(Cli.AirText(r), "reports") < 0,
                    lv.name + ": the centre hold gives the normal hover, and the hovering frequency is reported without a pressure-in-the-air claim");
                bool[] fs = Cli.Defaults();
                Options fo = new Options(); fo.calPick = r.pick; fo.calDrop = r.drop;
                string why = null;
                try { Build.BuildOne(app, info, null, lv, fs, true, false, fo); }
                catch (Stop e) { why = e.msg; }
                Check(why == null, lv.name + ": a build takes the levels measured in the background " + why);
            }
            ApipModel m = Apip.Fallback(Build.Lv("1000"), true);
            string path = P.PathJoin("cal-work", "background-levels.txt");
            m.pick = 80; m.drop = 60; Apip.Save(path, m);
            ApipModel back = Apip.Load(path);
            Check(back.pick == 80 && back.drop == 60, "a training file keeps the levels measured in the background");
            m.pick = 50; Apip.Save(path, m);
            string bad = null;
            try { Apip.Load(path); } catch (Stop e) { bad = e.msg; }
            Check(bad != null, "a training file with pick-up levels out of order is refused");
            P.DeleteFile(path);
            Cli.CalResult none = Cli.FromTraining(new ApipData(), null, Build.Lv("1000"), false);
            Check(!none.ok && none.notes.Count() > 0, "a training without hover readings measures nothing");
            // R45-rc2 (Anargon): Noise-Related Patches shows ALPI first and Multi-coil Read last
            int[] map = new int[Build.SETTINGS.Length];
            int n = Cli.ShowGroup(Cli.Defaults(), new Level[] { Build.Lv("1000") }, Cli.NOISE_CATS, Cli.CAL_FULL, map);
            bool ordered = n > 2 && P.Eq(Build.SETTINGS[map[0]].cat, "apip") && P.Eq(Build.SETTINGS[map[n - 1]].key, "multicoil");
            for (int i = 1; i < n; i++) if (CatAt(Build.SETTINGS[map[i]].cat) < CatAt(Build.SETTINGS[map[i - 1]].cat)) ordered = false;
            Check(ordered, "Noise-Related Patches: ALPI first, then noise tuning, position filters, Multi-coil Read last");
        }
        static int CatAt(string cat) { for (int i = 0; i < Cli.NOISE_CATS.Length; i++) if (P.Eq(Cli.NOISE_CATS[i], cat)) return i; return -1; }

        static void FirmwareChecks(string path)
        {
            InputInfo info = new InputInfo(); byte[] app = Build.ReadInput(path, info);
            if (Group("fw-fixture")) Check(!info.truncated && info.stock && info.boot != null, "routing fixture is a supported full stock image");
            bool[] settings = Cli.Defaults();
            string[] off = new string[] { "boost", "reach", "longrange", "cal", "odd", "multicoil" };
            for (int i = 0; i < off.Length; i++) settings[Build.SetIndex(off[i])] = false;
            Build.quiet = true;
            try
            {
                if (Group("fw-measurement-bootloader")) MeasurementBootloaderChecks(app, info);
                for (int i = 1; i < Build.LEVELS.Length; i++)
                {
                    if (!Group("fw-telemetry-" + Build.LEVELS[i].name)) continue;
                    Level level = Build.LEVELS[i]; Options telemetry = new Options(); telemetry.telemetry = true; telemetry.calPick = 40; telemetry.calDrop = 24;
                    Image image = Build.BuildOne(app, info, null, level, settings, true, false, telemetry);
                    StringBuilder why = new StringBuilder(); Machine machine = Twin.ScanMachine(Twin.FromApp(image.Bytes()), TestPen(), false, why);
                    Check(machine != null, level.name + ": calibration firmware boots: " + why.ToString());
                    machine.Write(Sched.REPORT_MODE, 1, 4); int before = machine.reports.n;
                    machine.RunMs(180);
                    Cli.Tele decoded = Reports(machine, before);
                    Check(machine.Read(Sched.REPORT_MODE, 1) == 2 && decoded.nPos > 5 && decoded.nSig > 5,
                        level.name + ": alternate host report mode produces genuine ID 7 position and signal telemetry");
                    LedZoneChecks(Twin.FromApp(image.Bytes()), level.name);
                    NormalReportMode(Build.BuildOne(app, info, null, level, Cli.Defaults(), true, false, new Options()), level.name);
                }
                if (Group("fw-normal-build"))
                {
                    Image normal = Build.BuildOne(app, info, null, Build.Lv("500"), Cli.Defaults(), true, false, new Options());
                    // R44: only the calibration-pass build takes LED zones; a normal build stalls their strings as before
                    Cli.TwinSource plain = new Cli.TwinSource(); plain.img = Twin.FromApp(normal.Bytes());
                    Check(P.Len(plain.Open()) == 0 && !plain.Zone(Cli.ZONE_CENTRE, true) && plain.zones.Count() == 0,
                        "a normal build does not answer the LED zone strings");
                    string normalPath = P.PathJoin("cal-work", "normal.hex");
                    P.DeleteFile(normalPath + ".calibration.txt");
                    Build.WriteHex(normal.Bytes(), Build.BASE, normalPath);
                    Check(Twin.Run(new string[] { normalPath, "--calibrate-sim" }) != 0,
                        "ordinary firmware cannot report successful simulated calibration");
                    Check(!P.FileExists(normalPath + ".calibration.txt"), "failed simulated calibration does not save a usable-looking file");
                    P.DeleteFile(normalPath);
                }
                if (Group("fw-guided-steps"))
                {
                    // R45-rc2 (Anargon): no no-pen step, no slow lift; the readiness check's light touch is the first step's
                    Check(Cli.CAL_TITLES.Length == 3 && Cli.CAL_ASK.Length == 3 && Cli.CAL_SECS.Length == 3
                        && P.Eq(Cli.CAL_TITLES[0], "minimal pressure") && P.Eq(Cli.CAL_TITLES[2], "highest hover"),
                        "three guided steps: light touch, normal hover, highest hover");
                    Options tele = new Options(); tele.telemetry = true; tele.calPick = Cli.MEASURE_PICK; tele.calDrop = Cli.MEASURE_DROP;
                    Image pass1 = Build.BuildOne(app, info, null, Build.Lv("500"), settings, true, false, tele);
                    Check(Cli.SimCalibrate(Twin.FromApp(pass1.Bytes()), P.PathJoin("cal-work", "pass1")),
                        "FAST: the three steps played in the twin (LED zones) give usable thresholds");
                    P.DeleteFile(P.PathJoin("cal-work", "pass1") + ".calibration.txt");
                }
                if (Group("fw-background")) BackgroundChecks(app, info);
            }
            finally { Build.quiet = false; }
        }

        public static int Main(string[] args)
        {
            try
            {
                string work = "cal-work"; P.MakeDirs(work);
                if (Group("decode")) DecodeChecks();
                if (Group("ready")) ReadyChecks();
                if (Group("naming")) NamingChecks();
                if (Group("calibration-files")) CalibrationChecks(work);
                if (args.Length > 0) FirmwareChecks(args[0]);
                else P.Line("SKIP: calibration routing on stock firmware (no fixture supplied)");
                P.Line("RESULT calibration regressions passed: " + F.Str(checks));
                return 0;
            }
            catch (Stop e) { P.Line(e.msg); return 1; }
            catch (Fault e) { P.Line("FAIL: emulator fault: " + e.msg); return 1; }
        }
    }
}
