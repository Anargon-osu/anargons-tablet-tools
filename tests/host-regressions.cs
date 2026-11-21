// Host-side regressions use a fake xppen-tools executable. They never access USB or program a tablet.
using System;
using System.IO;
using System.Reflection;
using System.Text;
using Gd32Emu;

namespace AnargonsTabletTools.Tests
{
    // R41: stands in for the tablet when the flash path checks for measurement firmware (calibration telemetry)
    public class FakeProbe : Cli.CalSource
    {
        public bool telemetry = false;
        public override string Open() { return ""; }
        public override int[] Collect(int step, double seconds)
        {
            if (!telemetry) return new int[0];
            return new int[] { 7, 0xC0, 0x40, 0x1F, 0x94, 0x11, 0, 0, 0x20, 0xE1, 1000 };   // one page-1 record, in range
        }
        public override void Close() { }
        public override string Why() { return ""; }
    }

    public static class HostRegressions
    {
        static int checks;
        static string folder, tool, trace;

        static void Check(bool condition, string name)
        {
            checks++;
            if (!condition) throw new Stop("FAIL: " + name);
        }

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
        static void Set(string key, string value) { Environment.SetEnvironmentVariable(key, value); }
        static string Mode() { return Environment.GetEnvironmentVariable("ADT_HOST_MODE") ?? "ok"; }

        static int FakeTool(string[] args)
        {
            int start = args.Length > 0 && args[0] == "--fake" ? 1 : 0;
            if (start >= args.Length) return 10;
            string command = args[start];
            string file = Environment.GetEnvironmentVariable("ADT_HOST_TRACE");
            if (file != null) File.AppendAllText(file, command + "\n");
            if (command == "arguments")
            {
                for (int i = start + 1; i < args.Length; i++) Console.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(args[i])));
                return 0;
            }
            if (command == "streams")
            {
                for (int i = 0; i < 20000; i++) { Console.Out.Write('o'); Console.Error.Write('e'); }
                return 3;
            }
            if (command == "status") { Console.WriteLine("Everything looks fine."); return 9; }
            if (command == "stdin")
            {
                // closed stdin reads as empty; a runner that hands the child no readable stdin handle at all can make the
                // read throw instead, which is also "no menu input" (any text at all is the failure)
                try { return Console.In.ReadToEnd().Trim().Length == 0 ? 0 : 12; }
                catch (IOException) { return 0; }
            }
            if (command == "confirm")
            {
                // xppen-tools' typed confirmation, with a log line flushed right after the prompt
                Console.Out.Write("Type exactly: UPDATE\n> "); Console.Out.Flush();
                Console.Error.WriteLine("[INFO] Device: fake tablet"); Console.Error.Flush();
                string answer = Console.ReadLine();
                return answer == "UPDATE" ? 0 : 14;
            }
            if (command == "update")
            {
                if (Mode() == "update-fails") { Console.WriteLine("Starting update..."); return 7; }
                if (Mode() == "update-error") { Console.WriteLine("[ERROR] Verification failed"); return 0; }
                return 0; // a successful silent process is still a successful process
            }
            if (command == "dump")
            {
                if (Mode() == "no-file") return 0;
                if (Mode() == "slow") System.Threading.Thread.Sleep(350);
                if (Mode() == "hold")      // an in-progress dump: it finishes only when the test releases it
                {
                    string release = Environment.GetEnvironmentVariable("ADT_HOST_RELEASE");
                    for (int i = 0; i < 1200 && !File.Exists(release); i++) System.Threading.Thread.Sleep(50);
                }
                Directory.CreateDirectory(args[start + 1]);
                string source = Environment.GetEnvironmentVariable("ADT_HOST_STOCK");
                byte[] bytes = source != null ? File.ReadAllBytes(source) : new byte[0x10000];
                if (Mode() == "short") bytes = new byte[100];
                if (Mode() == "oversized") bytes = new byte[0x10001];
                File.WriteAllBytes(Path.Combine(args[start + 1], "ic902-main.bin"), bytes);
                if (Mode() == "old-bundle" && bytes.Length == 0x10000)     // R42: an older xppen-tools' 35208-byte code.bin / stock.hex
                {
                    byte[] code = new byte[35208];
                    Array.Copy(bytes, 0x3800, code, 0, code.Length);
                    File.WriteAllBytes(Path.Combine(args[start + 1], "ic902-code.bin"), code);
                    Build.WriteHex(code, 0x08003800, Path.Combine(args[start + 1], "ic902-stock.hex"));
                }
                if (Mode() == "dump-fails") return 6;
                return 0;
            }
            return command == "dfu" || command == "exit-dfu" ? 0 : 11;
        }

        static string ShellQuote(string text) { return "'" + text.Replace("'", "'\"'\"'") + "'"; }

        static void MakeTool()
        {
            string assembly = Assembly.GetExecutingAssembly().Location;
            string tools = Path.Combine(folder, "tools with spaces \u00E9");
            Directory.CreateDirectory(tools);
            tool = Path.Combine(tools, P.IsWindows() ? "xppen-tools.exe" : "xppen-tools");
            if (P.IsWindows()) File.Copy(assembly, tool);
            else
            {
                File.WriteAllText(tool, "#!/bin/sh\nexec dotnet " + ShellQuote(assembly) + " --fake \"$@\"\n", new UTF8Encoding(false));
                string[] chmod = P.RunProcess("chmod", "+x " + BackupCapture.Quote(tool));
                if (chmod[0] != "0") throw new Exception("FAIL: fake tool is executable");   // every process needs it
                if (Group("fake-tool")) Check(true, "fake tool is executable");
            }
            Set("ADT_XPPEN_TOOLS", tool);
            Set("ADT_HOST_TRACE", trace);
            Set("ADT_TEST_COUNT_STRING", "ADTC1:N:000000"); // even count readout stays isolated from real USB
        }

        // R45-rc5 (4): the read-only firmware check (`crc`, atf_modcheck.cs) is left out; this fake answers it with an
        // error, so every check falls back to the dump as before
        static string Commands() { return File.Exists(trace) ? File.ReadAllText(trace).Replace("crc\n", "") : ""; }

        static void Utf8Configuration()
        {
            string previous = Directory.GetCurrentDirectory();
            string value = Path.Combine(folder, "pen \u00E9");
            File.WriteAllText(Path.Combine(folder, ".env"), "ADT_HOST_UTF8_LOCATION=\"" + value + "\"\n", new UTF8Encoding(true));
            try
            {
                Directory.SetCurrentDirectory(folder);
                Check(Conf.Get("ADT_HOST_UTF8_LOCATION", "") == value, "UTF-8 configuration with a BOM preserves non-ASCII paths");
            }
            finally { Directory.SetCurrentDirectory(previous); }
        }

        static BackupCapture Capture(string mode, bool restore)
        {
            Set("ADT_HOST_MODE", mode);
            BackupCapture capture = new BackupCapture();
            capture.folder = folder; capture.tool = tool; capture.restoreRuntime = restore;
            capture.Run();
            Check(capture.done, "backup always signals completion: " + mode);
            return capture;
        }

        static void ProcessStatusAndArguments()
        {
            string[] values = new string[] { "", "with spaces", "a\\\"b", "C:\\trailing\\", "single'quote", "a\tb" };
            string arguments = "arguments";
            for (int i = 0; i < values.Length; i++) arguments += " " + BackupCapture.Quote(values[i]);
            string[] result = P.RunInResult(Path.GetDirectoryName(tool), tool, arguments, "", false);
            string expected = "";
            for (int i = 0; i < values.Length; i++) expected += Convert.ToBase64String(Encoding.UTF8.GetBytes(values[i])) + "\n";
            Check(result[0] == "0" && result[1].Replace("\r\n", "\n") == expected, "quoted arguments survive spaces, quotes, trailing slashes and empty values");
            result = P.RunInResult(folder, tool, "status", "", false);
            Check(result[0] == "9" && result[1].Contains("looks fine"), "process failure is preserved despite reassuring output");
            result = P.RunInResult(folder, tool, "streams", "", false);
            Check(result[0] == "3" && result[1].Length == 40000, "stdout and stderr are drained together without deadlock");
            result = P.RunInResult(folder, tool, "stdin", "", false);
            Check(result[0] == "0", "background commands receive closed stdin instead of the menu input (exit " + result[0] + ": " + result[1].Trim() + ")");
        }

        static void HidTransportCounts()
        {
            // Inject synthetic reports into the platform's routing boundary without opening USB.
            BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            Type platform = typeof(P);
            string[] names = new string[] { "hidRun", "hidGeneration", "hidN", "hidOther", "hidOtherId", "hidT0", "hidReportCounts" };
            FieldInfo[] fields = new FieldInfo[names.Length]; object[] saved = new object[names.Length];
            for (int i = 0; i < names.Length; i++) { fields[i] = platform.GetField(names[i], flags); saved[i] = fields[i].GetValue(null); }
            MethodInfo store = platform.GetMethod("HidStore", flags);
            try
            {
                fields[0].SetValue(null, true); fields[1].SetValue(null, 52); fields[2].SetValue(null, 0);
                fields[5].SetValue(null, P.NowSec()); fields[6].SetValue(null, new int[256]);
                byte[] ordinary = new byte[12]; ordinary[0] = 2;
                store.Invoke(null, new object[] { ordinary, ordinary.Length, 51 });
                Check(P.HidReportCounts()[2] == 0, "reports from a previous HID session are ignored");
                store.Invoke(null, new object[] { ordinary, ordinary.Length, 52 });
                Check(P.HidReportCounts()[2] == 1 && (int)fields[2].GetValue(null) == 0,
                    "ordinary ID 2 confirms transport without becoming calibration telemetry");
                byte[] measured = new byte[10]; measured[0] = 7;
                store.Invoke(null, new object[] { measured, measured.Length, 52 });
                Check(P.HidReportCounts()[7] == 1 && (int)fields[2].GetValue(null) == 1,
                    "ID 7 is counted and retained for telemetry decoding");
                int[] counts = P.HidReportCounts(); counts[2] = 999;
                Check(P.HidReportCounts()[2] == 1, "diagnostic count snapshots cannot modify reader state");
                fields[0].SetValue(null, false);
                store.Invoke(null, new object[] { ordinary, ordinary.Length, 52 });
                Check(P.HidReportCounts()[2] == 1, "stopped HID sessions cannot add reports");
            }
            finally { for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, saved[i]); }
        }

        static void Backups()
        {
            string before = Commands();
            BackupCapture first = Capture("ok", true);
            Check(first.error == null && first.flash != null && first.restored, "complete backup succeeds and exits update mode");
            Check(File.Exists(Path.Combine(folder, "last-dump", "ic902-main.bin")), "latest complete dump is saved in last-dump");
            Check(File.Exists(Cli.PrimaryDump(folder)), "first complete backup is also preserved");
            Check(Commands().Substring(before.Length) == "dfu\ndump\nexit-dfu\n", "startup backup restores runtime only after dumping");
            byte[] original = File.ReadAllBytes(first.path);
            string[] failures = new string[] { "dump-fails", "no-file", "short", "oversized" };
            for (int i = 0; i < failures.Length; i++)
            {
                BackupCapture failed = Capture(failures[i], true);
                Check(failed.error != null && failed.flash == null && failed.restored, "failed/incomplete dump is rejected and runtime restored: " + failures[i]);
                Check(Convert.ToBase64String(File.ReadAllBytes(first.path)) == Convert.ToBase64String(original), "failed dump preserves the last successful backup: " + failures[i]);
            }
            // R42: restore images that stop at 0x0800C188 next to a dump are renamed so they cannot be flashed by mistake
            string held = Environment.GetEnvironmentVariable("ADT_HOST_STOCK");
            Set("ADT_HOST_STOCK", StockLike());
            BackupCapture old = Capture("old-bundle", true);
            Set("ADT_HOST_STOCK", held);
            string[] renamed = Directory.GetFiles(Path.Combine(folder, "last-dump"), "*.INCOMPLETE-DO-NOT-FLASH", SearchOption.AllDirectories);
            string[] left = Directory.GetFiles(Path.Combine(folder, "last-dump"), "ic902-stock.hex", SearchOption.AllDirectories);
            Check(old.error == null && old.incomplete != null && old.incomplete.Contains("0x0800c188") && renamed.Length == 2 && left.Length == 0,
                "an older xppen-tools' 35208-byte restore images are renamed and reported");
            Check(File.ReadAllText(Path.Combine(folder, "last-dump", "backup.log")).Contains("INCOMPLETE-DO-NOT-FLASH"), "the rename is recorded in backup.log");
            int archives = Directory.GetDirectories(Cli.DumpsDir(folder)).Length;
            BackupCapture next = Capture("ok", false);
            Check(next.error == null && Directory.GetDirectories(Cli.DumpsDir(folder)).Length == archives + 1, "each backup retains a new full immutable copy");
            Check(File.ReadAllBytes(next.path).Length == 65536, "latest backup remains exactly one full flash");
        }

        static void FlashStatusAndDryRun()
        {
            string image = Path.Combine(folder, "firmware image.hex");
            File.WriteAllText(image, ":00000001FF\n");
            int beforeCount = Store.FlashCount();
            int count = beforeCount < 0 ? 0 : beforeCount; // an unknown local count becomes 1 on the first successful update
            Set("ADT_HOST_MODE", "update-fails");
            Check(!Cli.FlashSteps(image, true, false) && Store.FlashCount() == beforeCount, "failed update is not counted as a successful flash");
            Set("ADT_HOST_MODE", "update-error");
            Check(!Cli.FlashSteps(image, true, false) && Store.FlashCount() == beforeCount, "explicit verification error also fails with zero process status");
            Set("ADT_HOST_MODE", "ok");
            Check(Cli.FlashSteps(image, true, false) && Store.FlashCount() == count + 1, "successful silent update counts exactly once");
            string[] failedMeasurements = new string[] { "update-fails", "update-error" };
            for (int i = 0; i < failedMeasurements.Length; i++)
            {
                Cli.calibrationFirmwareActive = false;
                Set("ADT_HOST_MODE", failedMeasurements[i]);
                Check(!Cli.FlashSteps(image, true, true) && Cli.calibrationFirmwareActive && Store.FlashCount() == count + 1,
                    "a first failed measurement update is treated as possibly installed: " + failedMeasurements[i]);
            }
            Set("ADT_HOST_MODE", "ok");
            Check(Cli.FlashSteps(image, true, true) && Cli.calibrationFirmwareActive, "successful measurement flash is tracked until normal firmware is restored");
            string normal = Path.Combine(folder, "normal firmware.hex"); File.Copy(image, normal);
            Set("ADT_HOST_MODE", "update-fails");
            Check(!Cli.FlashSteps(normal, true, false) && Cli.calibrationFirmwareActive, "a failed normal update cannot claim measurement firmware was removed");
            Set("ADT_HOST_MODE", "update-error");
            Check(!Cli.FlashSteps(normal, true, false) && Cli.calibrationFirmwareActive, "a verification error cannot clear the measurement-firmware state");
            Set("ADT_HOST_MODE", "ok");
            Check(Cli.FlashSteps(image, true, false) && !Cli.calibrationFirmwareActive, "successful normal flash clears measurement state even when its path matches the measurement image");
            string before = Commands();
            Check(Cli.Flash(image, Cli.PID_DFU, Cli.PID_RUN, 1, true), "dry-run helper completes");
            Check(Commands() == before && Store.FlashCount() == count + 3, "dry-run helper executes no tablet commands and never increments the counter");
            Check(Cli.Run(new string[] { "--flash-dry-run", "--calibrate", "--rate", "fast" }) == 1, "dry run rejects a live calibration request before any device command");
            Check(Cli.Run(new string[] { "--flash-dry-run", "--recovery", "--rate", "fast" }) == 1, "dry run rejects recovery before any device command");
            Check(Commands() == before, "conflicting dry-run requests do not execute the tool");
            Check(Cli.Run(new string[] { "--samples", "4294967316", "--rate", "fast" }) == 1, "overflowing integer options cannot wrap to a valid setting");
            Check(Cli.Run(new string[] { "--samples", "20.5", "--rate", "fast" }) == 1, "fractional sample counts cannot be rounded silently");
            Check(Cli.Run(new string[] { "--detect-scale", "NaN", "--rate", "fast" }) == 1, "NaN detection thresholds are rejected");
            Check(Cli.Run(new string[] { "--odd-mm", "Infinity", "--rate", "fast" }) == 1, "infinite jump margins are rejected");
        }

        static void Startup()
        {
            string before = Commands();
            string release = Path.Combine(folder, "release startup dump");
            Set("ADT_HOST_MODE", "hold");
            Set("ADT_HOST_RELEASE", release);
            try
            {
                StartupBackup.Begin(folder);
                Check(StartupBackup.Running() && !File.Exists(Path.Combine(folder, "last-dump", "backup.log")),
                    "startup backup begins without blocking the menu");
                File.WriteAllText(release, "go");
                StartupBackup.Wait();
                Check(File.Exists(Path.Combine(folder, "last-dump", "backup.log")), "startup backup records its outcome");
                Check(Commands().Substring(before.Length) == "dfu\ndump\nexit-dfu\n", "background backup performs capture and returns to runtime");
            }
            finally
            {
                File.WriteAllText(release, "go");
                StartupBackup.Wait();
                Set("ADT_HOST_MODE", "ok");
            }
        }

        static void ClosedAcknowledgement()
        {
            TextReader previous = Console.In;
            try
            {
                Console.SetIn(new StringReader(""));
                bool refused = false;
                try { Screen.DangerAsk(new StrList(), "test acknowledgement > "); }
                catch (Stop) { refused = true; }
                Check(refused && P.InputEnded(), "closed input cannot acknowledge a safety prompt");
            }
            finally
            {
                Console.SetIn(new StringReader("\n")); P.ReadLine(); // reset the EOF flag for subsequent independent CLI checks
                Console.SetIn(previous);
                Console.WriteLine(); // redirected EOF has no terminal echo to finish the prompt line
            }
        }

        static void SequentialPrompt()
        {
            TextReader input = Console.In; TextWriter output = Console.Out;
            StrList warnings = Screen.warnings;
            StringWriter captured = new StringWriter();
            try
            {
                Console.SetIn(new StringReader("\n")); Console.SetOut(captured);
                Screen.warnings = new StrList();
                Screen.Warn("Backup warning that must appear once.");
                Screen.Warn("Second warning that must appear once.");
                string answer = Screen.Ask("Type 1-6 (Enter = 3) > ");
                string text = captured.ToString();
                Check(answer == "" && !P.InputEnded(), "empty Enter accepts a menu default");
                Check(text.IndexOf("Type 1-6", StringComparison.Ordinal) > text.IndexOf("Second warning", StringComparison.Ordinal),
                    "warnings precede the input prompt");
                Check(text.IndexOf("Backup warning", StringComparison.Ordinal) == text.LastIndexOf("Backup warning", StringComparison.Ordinal)
                    && text.IndexOf("Second warning", StringComparison.Ordinal) == text.LastIndexOf("Second warning", StringComparison.Ordinal),
                    "each warning is printed once per prompt");
                Check(text.IndexOf('\r') < 0 && text.IndexOf((char)27) < 0, "menu prompt uses forward-only output without cursor control");
            }
            finally { Console.SetIn(input); Console.SetOut(output); Screen.warnings = warnings; }
        }

        // ---- R38: AI disclosure, background dump, one backup per run, interactive xppen-tools input

        static void EndInput() { Console.SetIn(new StringReader("\n")); P.ReadLine(); }   // clears the EOF flag a Stop left behind

        // a 64 KB dump the tool reads as an untouched stock build (zeros elsewhere: host checks only, no firmware). R43
        // review: the build of the stock fixture the suite was given (else 2025-02-14), so the fake tablet and the
        // fixture's backups agree; with the December fixture the suite used to stop at TabletMatches.
        static string fixtureBuild = "2025-02-14_release001";
        static string StockLike()
        {
            byte[] d = new byte[0x10000];
            d[3] = 0x20; d[0x3803] = 0x20; d[0x3800 + 0x18C] = 0x30;
            string build = fixtureBuild;
            int k = 0x3800 + Build.BUILD_STR_AT - Build.BASE;
            d[k - 2] = (byte)(2 + 2 * build.Length); d[k - 1] = 3;
            for (int i = 0; i < build.Length; i++) d[k + 2 * i] = (byte)build[i];
            int known = 0;
            while (!P.Eq(Layout.KNOWN[known].build, build)) known++;
            FakeEnd(d, known);                                  // code pages and an end that pass the checks (see FakeEnd)
            string path = Path.Combine(folder, "stock like.bin");
            File.WriteAllBytes(path, d);
            return path;
        }

        // R42: the tool keeps only a CRC-32 of each build's end (no firmware bytes), so the fake end is made to match it:
        // zeros, the stock value at this tool's known edit sites, the start-up table (addresses and sizes: the end comes
        // from it), and the last four bytes chosen so the CRC-32 is END_CRC. R43 review: each code page is made to match
        // its stored CRC (PAGE_CRC) the same way, since a code page changed in an unknown way is now refused. With every
        // piece's CRC and length equal to stock, the whole image's CRC-32 equals the build's too.
        static void FakeEnd(byte[] d, int known)
        {
            KnownBuild kb = Layout.KNOWN[known];
            int cut = StockRef.CUT - 0x08000000, end = kb.end - 0x08000000, len = end - cut;
            long[] pc = StockRef.PAGE_CRC[known];
            for (int p = 0; p < pc.Length; p++)
            {
                int a = 0x3800 + p * 0x400, n = a + 0x400 <= cut ? 0x400 : cut - a;
                ForgeCrc(d, a, n, pc[p]);
            }
            for (int i = cut; i < end; i++) d[i] = 0;
            int[] at = Repair.EDIT_AT[known], sv = Repair.EDIT_STOCK[known];
            for (int j = 0; j < at.Length; j++) if (at[j] >= StockRef.CUT) d[at[j] - 0x08000000] = (byte)sv[j];
            // start-up table: .data copied from just after the table, then the zero-initialised RAM; its load address is the end
            int tab = end - 0x200 - 32, data = tab + 32;
            int[] ent = new int[] { 0x08000000 + data, 0x20000000, 1224, 0x0800405C, kb.end, 0x200004C8, 2472, 0x0800735C };
            for (int k = 0; k < ent.Length; k++) for (int b = 0; b < 4; b++) d[tab + 4 * k + b] = (byte)((ent[k] >> (8 * b)) & 0xFF);
            ForgeCrc(d, cut, len, StockRef.END_CRC[known]);
        }

        // flips bits in the last four bytes of d[from, from + len) so that its CRC-32 is crc. CRC-32 is linear, so flipping
        // the right ones of those 32 bits reaches any value: each bit's effect is measured, then the flips are solved over GF(2).
        static void ForgeCrc(byte[] d, int from, int len, long crc)
        {
            int cut = from, end = from + len;
            long now = Txt.Crc32(d, cut, len), target = crc ^ now;
            long[] bv = new long[32], bm = new long[32];
            for (int bit = 0; bit < 32; bit++)
            {
                int o = end - 4 + bit / 8; byte f = (byte)(1 << (bit % 8));
                d[o] ^= f; long v = Txt.Crc32(d, cut, len) ^ now; d[o] ^= f;
                long m = 1L << bit;
                for (int k = 31; k >= 0 && v != 0; k--)
                {
                    if (((v >> k) & 1) == 0) continue;
                    if (bv[k] == 0) { bv[k] = v; bm[k] = m; v = 0; break; }
                    v ^= bv[k]; m ^= bm[k];
                }
            }
            long flips = 0;
            for (int k = 31; k >= 0; k--) if (((target >> k) & 1) != 0) { if (bv[k] == 0) throw new Exception("fake image: CRC not reachable"); target ^= bv[k]; flips ^= bm[k]; }
            for (int bit = 0; bit < 32; bit++) if (((flips >> bit) & 1) != 0) d[end - 4 + bit / 8] ^= (byte)(1 << (bit % 8));
            if (Txt.Crc32(d, cut, len) != crc) throw new Exception("fake image: CRC mismatch at " + F.Hx(0x08000000 + from, 8));
        }

        static void Disclosure()
        {
            string expected = "This program is vibecoded. There are basic protections,|like emulation-based checking and firmware compatibility|checking, but no amount of checks can guarantee a brick-free|process.||No human has also approved the codebase, so reliability is|NOT GUARANTEED, however, several code and structure passes|have been done to make this project... Kind of bearable to use.||You hate it, I hate it, we all do, however, the one who is|flashing this onto your tablet is you, and only you.|Nobody else can be held liable for any issues|caused from your attempt at flashing this.";
            Check(string.Join("|", Screen.AI_DISCLOSURE) == expected, "the AI disclosure is complete and unchanged (it must never be removed)");
            TextReader input = Console.In; TextWriter output = Console.Out; bool wipe = Screen.wipe;
            try
            {
                StringWriter step = new StringWriter(); Console.SetOut(step); Screen.wipe = false;
                Screen.Step("Step 3/6: Preset");
                string text = step.ToString();
                int at = text.IndexOf("#    AI DISCLOSURE", StringComparison.Ordinal);
                Check(at >= 0 && at < text.IndexOf("Step 3/6", StringComparison.Ordinal), "every step screen starts with the AI disclosure box");
                bool all = true, width = true;
                for (int i = 0; i < Screen.AI_DISCLOSURE.Length; i++) if (text.IndexOf("#        " + Screen.AI_DISCLOSURE[i], StringComparison.Ordinal) < 0) all = false;
                string[] rows = text.Replace("\r\n", "\n").Split('\n');
                for (int i = 0; i < rows.Length; i++) if (rows[i].StartsWith("    #", StringComparison.Ordinal) && rows[i].Length != 80) width = false;
                Check(all && width, "the disclosure box shows every line, 80 columns wide");
                StringWriter danger = new StringWriter(); Console.SetOut(danger); Console.SetIn(new StringReader("\n"));
                Screen.DangerAsk(new StrList(), "ack > ");
                Check(danger.ToString().IndexOf("AI DISCLOSURE", StringComparison.Ordinal) >= 0, "full-screen warnings keep the AI disclosure");
                StringWriter cli = new StringWriter(); Console.SetOut(cli);
                Cli.Run(new string[] { "--compat", Path.Combine(folder, "missing firmware.bin") });
                Check(cli.ToString().IndexOf("AI DISCLOSURE", StringComparison.Ordinal) >= 0, "command-line runs show the AI disclosure first");
            }
            finally { Console.SetIn(input); Console.SetOut(output); Screen.wipe = wipe; }
        }

        static void OneBackupPerRun()
        {
            string savedStock = Environment.GetEnvironmentVariable("ADT_HOST_STOCK");
            TextWriter output = Console.Out; StrList warnings = Screen.warnings;
            try
            {
                string stock = StockLike();
                Set("ADT_HOST_STOCK", stock);
                InputInfo info = new InputInfo(); Build.ReadInput(stock, info);
                string image = Path.Combine(folder, "one backup.hex"); File.WriteAllText(image, ":00000001FF\n");
                Console.SetOut(new StringWriter());
                Cli.sessionBackup = null; Cli.tabletApp = null;
                string before = Commands();
                Set("ADT_HOST_MODE", "slow");
                StartupBackup.Begin(folder);
                Check(StartupBackup.Running(), "the startup backup is underway");
                Check(Cli.BeforeFlash(folder, info) && !Cli.backupLeftDfu && Cli.sessionBackup != null, "a flash reached during the startup backup waits for it and uses it");
                Check(Commands().Substring(before.Length) == "dfu\ndump\nexit-dfu\n", "no second backup is started while the first is underway");
                before = Commands();
                Check(Cli.BeforeFlash(folder, info) && !Cli.backupLeftDfu && Commands() == before, "a later flash in the same run makes no second backup");
                Set("ADT_HOST_MODE", "ok");
                Check(Cli.FlashSteps(image, Cli.backupLeftDfu, false) && Commands().Substring(before.Length) == "dfu\nupdate\n",
                    "after the startup backup returned the tablet to normal mode, flashing enters update mode first");
                before = Commands();
                Check(Cli.BeforeFlash(folder, info, true) && Cli.backupLeftDfu && Commands().Substring(before.Length) == "dfu\ndump\n",
                    "return-to-stock still reads the tablet again to verify its bootloader");
                Cli.sessionBackup = null; Cli.tabletApp = null;
                Set("ADT_HOST_MODE", "dump-fails"); StartupBackup.Begin(folder); StartupBackup.Wait();
                Check(Cli.sessionBackup == null, "a failed startup backup is never used for flashing");
                Set("ADT_HOST_MODE", "ok"); before = Commands();
                Check(Cli.BeforeFlash(folder, info) && Cli.backupLeftDfu && Commands().Substring(before.Length) == "dfu\ndump\n",
                    "only a failed first backup leads to a second one");
                // R41: replacing this tool's measurement firmware reuses the backup of the real firmware (no new dump)
                FakeProbe probe = (FakeProbe)Cli.measurementProbe;
                Cli.sessionBackup = null; Cli.tabletApp = null; probe.telemetry = true; before = Commands();
                Check(Cli.BeforeFlash(folder, info) && !Cli.backupLeftDfu && Commands() == before && Cli.sessionBackup == Cli.PrimaryDump(folder) && Cli.tabletApp != null,
                    "measurement firmware on the tablet: no new backup, the first backup of the real firmware is used");
                Cli.sessionBackup = null; Cli.tabletApp = null; probe.telemetry = false; before = Commands();
                Check(Cli.BeforeFlash(folder, info) && Cli.backupLeftDfu && Commands().Substring(before.Length) == "dfu\ndump\n",
                    "without measurement telemetry the tablet is backed up as before");
                Cli.sessionBackup = null; Cli.tabletApp = null; probe.telemetry = true; before = Commands();
                Check(Cli.BeforeFlash(folder, info, true) && Cli.backupLeftDfu && Commands().Substring(before.Length) == "dfu\ndump\n",
                    "a fresh read (return to stock) still reads the tablet even with measurement firmware");
                probe.telemetry = false;
            }
            finally { Console.SetOut(output); Screen.warnings = warnings; Set("ADT_HOST_STOCK", savedStock); Set("ADT_HOST_MODE", "ok"); }
        }

        static void WizardDuringBackup()
        {
            string savedStock = Environment.GetEnvironmentVariable("ADT_HOST_STOCK"), cwd = Directory.GetCurrentDirectory();
            TextReader input = Console.In; TextWriter output = Console.Out; StrList warnings = Screen.warnings;
            string release = Path.Combine(folder, "release first dump"), release2 = Path.Combine(folder, "release second dump");
            try
            {
                string stock = StockLike();
                Set("ADT_HOST_STOCK", stock); Set("ADT_HOST_MODE", "hold"); Set("ADT_HOST_RELEASE", release);
                string work = Path.Combine(folder, "wizard one"); Directory.CreateDirectory(work);
                Directory.SetCurrentDirectory(work);
                Cli.sessionBackup = null; Cli.tabletApp = null;
                StartupBackup.Begin(Path.Combine(work, "program folder"));
                StringWriter screen = new StringWriter(); Console.SetOut(screen);
                Console.SetIn(new StringReader(stock + "\n" + work + "\n"));     // step 1 and 2; input ends at step 3
                bool ended = false;
                try { Cli.Wizard(null, null, false); } catch (Stop) { ended = true; }
                string text = screen.ToString();
                Check(ended && text.IndexOf("Step 3/6", StringComparison.Ordinal) >= 0 && StartupBackup.Running() && !File.Exists(release),
                    "the wizard reaches the preset step while the startup dump is still running");
                Check(text.IndexOf("Waiting for the startup backup", StringComparison.Ordinal) < 0, "no step waits for the background dump unless it needs it");
                Check(text.IndexOf("Startup backup is running in the background", StringComparison.Ordinal) >= 0, "the steps say that the dump is running");
                File.WriteAllText(release, "go"); StartupBackup.Wait(); EndInput();

                // nothing to build from yet: the running dump's own file is offered, and choosing it waits for it
                Set("ADT_HOST_RELEASE", release2);
                string work2 = Path.Combine(folder, "wizard two"); Directory.CreateDirectory(work2);
                Directory.SetCurrentDirectory(work2);
                Cli.sessionBackup = null; Cli.tabletApp = null; Screen.warnings = new StrList();
                StartupBackup.Begin(Path.Combine(work2, "program folder"));
                string target = StartupBackup.Target();
                System.Threading.Thread releaser = new System.Threading.Thread(delegate() { System.Threading.Thread.Sleep(700); File.WriteAllText(release2, "go"); });
                releaser.Start();
                screen = new StringWriter(); Console.SetOut(screen);
                Console.SetIn(new StringReader("\n" + work2 + "\n"));
                ended = false;
                try { Cli.Wizard(null, null, false); } catch (Stop) { ended = true; }
                releaser.Join();
                text = screen.ToString();
                int waited = text.IndexOf("Waiting for the startup backup", StringComparison.Ordinal);
                Check(text.IndexOf("Enter = " + target, StringComparison.Ordinal) >= 0, "with no dump yet, step 1 offers the file the running backup writes");
                Check(ended && waited > 0 && waited < text.IndexOf("Step 2/6", StringComparison.Ordinal) && text.IndexOf("Step 3/6", StringComparison.Ordinal) > 0
                    && Cli.sessionBackup == target, "choosing it waits for that dump, then continues with it");
            }
            finally
            {
                File.WriteAllText(release, "go"); File.WriteAllText(release2, "go"); StartupBackup.Wait(); EndInput();
                Directory.SetCurrentDirectory(cwd); Console.SetIn(input); Console.SetOut(output); Screen.warnings = warnings;
                Set("ADT_HOST_STOCK", savedStock); Set("ADT_HOST_MODE", "ok");
            }
        }

        // R39: Step 1 offers this run's tablet backup as the firmware file ("b")
        static void WizardBackupOption()
        {
            string savedStock = Environment.GetEnvironmentVariable("ADT_HOST_STOCK"), cwd = Directory.GetCurrentDirectory();
            TextReader input = Console.In; TextWriter output = Console.Out; StrList warnings = Screen.warnings;
            string release = Path.Combine(folder, "release option dump");
            try
            {
                string stock = StockLike();
                Set("ADT_HOST_STOCK", stock); Set("ADT_HOST_MODE", "hold"); Set("ADT_HOST_RELEASE", release);
                string work = Path.Combine(folder, "wizard option"); Directory.CreateDirectory(work);
                Directory.SetCurrentDirectory(work);
                Cli.sessionBackup = null; Cli.tabletApp = null; Screen.warnings = new StrList();
                StartupBackup.Begin(Path.Combine(work, "program folder"));
                string target = StartupBackup.Target();
                System.Threading.Thread releaser = new System.Threading.Thread(delegate() { System.Threading.Thread.Sleep(700); File.WriteAllText(release, "go"); });
                releaser.Start();
                StringWriter screen = new StringWriter(); Console.SetOut(screen);
                Console.SetIn(new StringReader("b\n" + work + "\n"));
                bool ended = false;
                try { Cli.Wizard(null, null, false); } catch (Stop) { ended = true; }
                releaser.Join();
                string text = screen.ToString();
                int waited = text.IndexOf("Waiting for the startup backup", StringComparison.Ordinal);
                Check(text.IndexOf("b = this run's tablet backup (not finished: ", StringComparison.Ordinal) >= 0 && text.Replace("\r\n", "\n").IndexOf("\n        " + target + "\n", StringComparison.Ordinal) >= 0
                    && text.IndexOf("Input File Directory: > ", StringComparison.Ordinal) >= 0,
                    "step 1 offers the background backup as the firmware file");
                Check(ended && waited > 0 && waited < text.IndexOf("Step 2/6", StringComparison.Ordinal) && text.IndexOf("Using this run's tablet backup: " + target, StringComparison.Ordinal) > waited
                    && text.IndexOf("Step 3/6", StringComparison.Ordinal) > 0 && Cli.sessionBackup == target, "choosing b waits for the running backup and builds from it");
                StartupBackup.Wait(); EndInput();

                // no backup in this run: b is not offered, typing it explains why and asks again
                Cli.sessionBackup = null; Cli.tabletApp = null; Screen.warnings = new StrList();
                screen = new StringWriter(); Console.SetOut(screen);
                Console.SetIn(new StringReader("b\n" + stock + "\n" + work + "\n"));
                ended = false;
                try { Cli.Wizard(null, null, false); } catch (Stop) { ended = true; }
                text = screen.ToString();
                Check(ended && text.IndexOf("b = this run's tablet backup", StringComparison.Ordinal) < 0 && text.IndexOf("there is no tablet backup in this run", StringComparison.Ordinal) > 0
                    && text.IndexOf("Step 3/6", StringComparison.Ordinal) > 0, "without a backup in this run, b is refused with a reason and another file can be chosen");
                EndInput();

                // a failed startup backup is never used as the firmware file
                Set("ADT_HOST_MODE", "dump-fails");
                Cli.sessionBackup = null; Cli.tabletApp = null; Screen.warnings = new StrList();
                StartupBackup.Begin(Path.Combine(work, "failed folder"));
                screen = new StringWriter(); Console.SetOut(screen);
                Console.SetIn(new StringReader("b\n" + stock + "\n" + work + "\n"));
                ended = false;
                try { Cli.Wizard(null, null, false); } catch (Stop) { ended = true; }
                text = screen.ToString();
                Check(ended && text.IndexOf("this run's tablet backup did not complete", StringComparison.Ordinal) > 0 && text.IndexOf("Step 3/6", StringComparison.Ordinal) > 0
                    && Cli.sessionBackup == null, "a failed background backup is refused as the firmware file");
            }
            finally
            {
                File.WriteAllText(release, "go"); StartupBackup.Wait(); EndInput();
                Directory.SetCurrentDirectory(cwd); Console.SetIn(input); Console.SetOut(output); Screen.warnings = warnings;
                Set("ADT_HOST_STOCK", savedStock); Set("ADT_HOST_MODE", "ok");
            }
        }

        static void PromptLayout()
        {
            TextWriter output = Console.Out;
            try
            {
                StringWriter screen = new StringWriter(); Console.SetOut(screen);
                P.LinePump lp = new P.LinePump("  ");
                string a = "Type exactly: UPDATE 28BD:FFF1\n> ", b = "[INFO] Device: UGTABLET\n[INFO] Protocol: 902 framed DFU\n";
                for (int i = 0; i < a.Length; i++) lp.Put(0, a[i]);
                for (int i = 0; i < b.Length; i++) lp.Put(1, b[i]);
                System.Threading.Thread.Sleep(P.LinePump.QUIET + 100); lp.Tick();
                Check(screen.ToString().Replace("\r\n", "\n") == "  Type exactly: UPDATE 28BD:FFF1\n  [INFO] Device: UGTABLET\n  [INFO] Protocol: 902 framed DFU\n  > ",
                    "log lines flushed after a prompt print above it, never glued to it, and the prompt is last");
                screen = new StringWriter(); Console.SetOut(screen);
                lp = new P.LinePump("");
                for (int i = 0; i < a.Length; i++) lp.Put(0, a[i]);
                for (int n = 0; n < 40 && screen.ToString().IndexOf("> ", StringComparison.Ordinal) < 0; n++)
                {
                    string chatter = "[INFO] busy " + n + "\n";
                    for (int i = 0; i < chatter.Length; i++) lp.Put(1, chatter[i]);
                    System.Threading.Thread.Sleep(50); lp.Tick();
                }
                Check(screen.ToString().IndexOf("> ", StringComparison.Ordinal) > 0, "a prompt still appears while the other stream keeps printing");
            }
            finally { Console.SetOut(output); }
        }

        static void InteractiveInput()
        {
            System.Diagnostics.ProcessStartInfo quiet = P.ToolStart(folder, tool, "dump x", false), typed = P.ToolStart(folder, tool, "update x", true);
            Check(quiet.CreateNoWindow && quiet.RedirectStandardInput, "background tool commands get no console input");
            Check(!typed.CreateNoWindow && !typed.RedirectStandardInput,
                "interactive tool commands run in this console with its input (no hidden console on Windows)");
            // a copy of this program whose own input is "UPDATE" runs the fake confirmation prompt interactively
            string self = Assembly.GetExecutingAssembly().Location;
            System.Diagnostics.ProcessStartInfo psi = P.IsWindows() ? new System.Diagnostics.ProcessStartInfo(self, "probe-interactive")
                : new System.Diagnostics.ProcessStartInfo("dotnet", BackupCapture.Quote(self) + " probe-interactive");
            psi.UseShellExecute = false; psi.RedirectStandardInput = true; psi.RedirectStandardOutput = true;
            using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
            {
                p.StandardInput.WriteLine("UPDATE"); p.StandardInput.Close();
                string shown = p.StandardOutput.ReadToEnd();
                bool exited = p.WaitForExit(30000);
                Check(exited && p.ExitCode == 0 && shown.IndexOf("Type exactly: UPDATE", StringComparison.Ordinal) >= 0,
                    "the typed confirmation reaches an interactive xppen-tools prompt");
                // stdout and stderr can arrive in either order; what matters is that each keeps its own line
                string rows = "\n" + shown.Replace("\r\n", "\n");
                Check(rows.IndexOf("> [INFO]", StringComparison.Ordinal) < 0 && rows.IndexOf("\n  [INFO] Device: fake tablet\n", StringComparison.Ordinal) >= 0,
                    "the tool's log output and its prompt stay on separate lines (output: " + shown.Replace("\r", "").Replace("\n", " | ") + ")");
            }
        }

        // R44: a small ZIP writer (stored or deflated entries) for the patcher-package tests
        static void W16(Stream s, int v) { s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8)); }
        static void W32(Stream s, long v) { for (int i = 0; i < 4; i++) s.WriteByte((byte)(v >> (8 * i))); }
        static byte[] Zip(string[] names, string[] texts, bool[] deflate)
        {
            MemoryStream z = new MemoryStream(), cd = new MemoryStream();
            for (int i = 0; i < names.Length; i++)
            {
                byte[] raw = Encoding.ASCII.GetBytes(texts[i]), body = raw, name = Encoding.ASCII.GetBytes(names[i]);
                if (deflate[i])
                {
                    MemoryStream m = new MemoryStream();
                    using (System.IO.Compression.DeflateStream d = new System.IO.Compression.DeflateStream(m, System.IO.Compression.CompressionMode.Compress, true)) d.Write(raw, 0, raw.Length);
                    body = m.ToArray();
                }
                long crc = Txt.Crc32(raw, 0, raw.Length), at = z.Length;
                int method = deflate[i] ? 8 : 0;
                W32(z, 0x04034B50); W16(z, 20); W16(z, 0); W16(z, method); W16(z, 0); W16(z, 0); W32(z, crc); W32(z, body.Length); W32(z, raw.Length); W16(z, name.Length); W16(z, 0);
                z.Write(name, 0, name.Length); z.Write(body, 0, body.Length);
                W32(cd, 0x02014B50); W16(cd, 20); W16(cd, 20); W16(cd, 0); W16(cd, method); W16(cd, 0); W16(cd, 0); W32(cd, crc); W32(cd, body.Length); W32(cd, raw.Length);
                W16(cd, name.Length); W16(cd, 0); W16(cd, 0); W16(cd, 0); W16(cd, 0); W32(cd, 0); W32(cd, at);
                cd.Write(name, 0, name.Length);
            }
            long cdAt = z.Length; byte[] c = cd.ToArray(); z.Write(c, 0, c.Length);
            W32(z, 0x06054B50); W16(z, 0); W16(z, 0); W16(z, names.Length); W16(z, names.Length); W32(z, c.Length); W32(z, cdAt); W16(z, 0);
            return z.ToArray();
        }

        // R44 (Anargon): the folders made at the start, and the patcher-package selector (C# source only)
        static void PatcherPackages()
        {
            TextReader input = Console.In; TextWriter output = Console.Out; StrList warnings = Screen.warnings;
            string dir = Path.Combine(folder, "patcherpkgs");
            try
            {
                Folders.Make();
                for (int i = 0; i < Folders.ALL.Length; i++) Check(Directory.Exists(Folders.Of(Folders.ALL[i])), "the " + Folders.ALL[i] + " folder is made next to the program");
                Directory.CreateDirectory(dir);
                StringWriter screen = new StringWriter(); Console.SetOut(screen);
                Check(Packages.Select(dir) == -1 && screen.ToString().Length == 0, "with no packages the selector is not shown");

                string code = "namespace TestPkg { public static class Menu { public static int Run(string[] args) { Gd32Emu.P.Line(\"PACKAGE RAN \" + AnargonsTabletTools.Cli.RELEASE_VERSION); return 7; } } }";
                string ini = "format=1\r\nname=Test Patcher\r\nentry=TestPkg.Menu\r\nrevision=T1\r\n";
                string good = Path.Combine(dir, "good"); Directory.CreateDirectory(Path.Combine(good, "src"));
                File.WriteAllText(Path.Combine(good, "package.ini"), ini);
                File.WriteAllText(Path.Combine(good, "src", "Menu.cs"), code);
                File.WriteAllBytes(Path.Combine(dir, "good.patcherpkg"), Zip(new string[] { "package.ini", "src/", "src/Menu.cs" }, new string[] { ini, "", code }, new bool[] { false, false, true }));
                File.WriteAllBytes(Path.Combine(dir, "dll.patcherpkg"), Zip(new string[] { "package.ini", "src/Menu.cs", "lib/x.dll" }, new string[] { ini, code, "MZ" }, new bool[] { false, false, false }));
                File.WriteAllBytes(Path.Combine(dir, "climb.patcherpkg"), Zip(new string[] { "package.ini", "src/../../evil.cs" }, new string[] { ini, code }, new bool[] { false, false }));
                File.WriteAllText(Path.Combine(dir, "tool.exe"), "MZ");
                string script = Path.Combine(dir, "script"); Directory.CreateDirectory(Path.Combine(script, "src"));
                File.WriteAllText(Path.Combine(script, "package.ini"), ini); File.WriteAllText(Path.Combine(script, "src", "run.py"), "print(1)");

                StrList skipped = new StrList();
                System.Collections.Generic.List<PatcherPackage> found = Packages.Scan(dir, skipped);
                string sk = "";
                for (int i = 0; i < skipped.Count(); i++) sk += skipped.Get(i) + " | ";
                Check(found.Count == 2 && found[0].hash == found[1].hash && found[0].name == "Test Patcher",
                    "a package is read from a folder and from a .patcherpkg ZIP (stored and deflated), with the same content hash");
                Check(skipped.Count() == 4 && sk.Contains("dll.patcherpkg: only C# source") && sk.Contains("climb.patcherpkg: unsafe file path")
                    && sk.Contains("tool.exe: only .patcherpkg") && sk.Contains("script: only C# source"), "anything but C# source is refused, with the reason: " + sk);

                if (Packages.Compiler() == null)
                {
                    // Linux and macOS: packages compile with the C# compiler that ships with Windows, so nothing can run here.
                    // The selector must still say why and run nothing, however the packages are approved.
                    screen = new StringWriter(); Console.SetOut(screen);
                    Console.SetIn(new StringReader("2\n" + Packages.TRUST_PHRASE + "\n\n1\n"));
                    int refused = Packages.Select(dir);
                    string said = screen.ToString();
                    Check(refused == -1 && said.IndexOf("Patcher packages need the Windows build", StringComparison.Ordinal) >= 0 && said.IndexOf("PACKAGE RAN", StringComparison.Ordinal) < 0,
                        "without Windows' C# compiler an approved package is refused with the reason and nothing runs");
                    return;
                }

                screen = new StringWriter(); Console.SetOut(screen);
                Console.SetIn(new StringReader("2\n" + Packages.TRUST_PHRASE + "\n"));
                int r = Packages.Select(dir);
                string text = screen.ToString();
                Check(r == 7 && text.IndexOf("PACKAGE RAN " + Cli.RELEASE_VERSION, StringComparison.Ordinal) >= 0 && text.IndexOf("PATCHER PACKAGE: Test Patcher", StringComparison.Ordinal) >= 0
                    && text.IndexOf("Skipped " + Path.Combine(Folders.PACKAGES, "tool.exe"), StringComparison.Ordinal) >= 0 && text.IndexOf("AI DISCLOSURE", StringComparison.Ordinal) >= 0,
                    "after the typed approval the package compiles, runs with this tool's classes and returns its exit code");

                screen = new StringWriter(); Console.SetOut(screen);
                Console.SetIn(new StringReader("3\n"));
                r = Packages.Select(dir);
                text = screen.ToString();
                Check(r == 7 && text.IndexOf("PATCHER PACKAGE:", StringComparison.Ordinal) < 0 && text.IndexOf("PACKAGE RAN", StringComparison.Ordinal) >= 0,
                    "the same content (here as the ZIP) runs without asking again");

                File.WriteAllText(Path.Combine(good, "src", "Menu.cs"), code.Replace("return 7", "return 8"));
                screen = new StringWriter(); Console.SetOut(screen);
                Console.SetIn(new StringReader("2\n\n1\n"));
                r = Packages.Select(dir);
                text = screen.ToString();
                Check(r == -1 && text.IndexOf("PATCHER PACKAGE: Test Patcher", StringComparison.Ordinal) >= 0 && text.IndexOf("PACKAGE RAN", StringComparison.Ordinal) < 0,
                    "a changed package is asked about again, is not run without the phrase, and 1 picks the built-in tool");

                File.WriteAllText(Path.Combine(good, "src", "Menu.cs"), "this is not C#");
                screen = new StringWriter(); Console.SetOut(screen);
                Console.SetIn(new StringReader("2\n" + Packages.TRUST_PHRASE + "\n\n1\n"));
                r = Packages.Select(dir);
                text = screen.ToString();
                Check(r == -1 && text.IndexOf("did not compile", StringComparison.Ordinal) >= 0 && text.IndexOf("PACKAGE RAN", StringComparison.Ordinal) < 0,
                    "a package that does not compile runs nothing");
            }
            finally { Console.SetIn(input); Console.SetOut(output); Screen.warnings = warnings; }
        }

        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "probe-interactive")
            {
                string[] r = P.RunInResult(Path.GetTempPath(), Cli.FindTool(), "confirm", "  ", true);
                return r[0] == "0" ? 0 : 20;
            }
            if (args.Length > 0 && (args[0] == "--fake" || args[0] == "dfu" || args[0] == "dump" || args[0] == "exit-dfu" || args[0] == "update" || args[0] == "arguments" || args[0] == "status" || args[0] == "streams" || args[0] == "stdin" || args[0] == "confirm")) return FakeTool(args);
            try
            {
                folder = Path.Combine(Path.GetTempPath(), "deco host quality " + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                trace = Path.Combine(folder, "commands.txt");
                Set("ADT_STATE_DIR", Path.Combine(folder, "state"));
                if (args.Length > 0) Set("ADT_HOST_STOCK", Path.GetFullPath(args[0]));
                if (args.Length > 0)
                {
                    byte[] fx = File.ReadAllBytes(args[0]);
                    int o = fx.Length >= 0x10000 ? 0x3800 : 0;
                    byte[] fa = new byte[fx.Length - o];
                    Array.Copy(fx, o, fa, 0, fa.Length);
                    string fb = Build.ReadBuildString(fa, fa.Length);
                    for (int k = 0; k < Layout.KNOWN.Length; k++) if (P.Eq(fb, Layout.KNOWN[k].build)) fixtureBuild = fb;
                }
                // R43 review: with no stock fixture the fake tablet runs the stock-like image (it used to dump 64 KB of
                // zeros, so the measurement-firmware check stopped at TabletMatches, as with the December fixture)
                if (args.Length == 0) Set("ADT_HOST_STOCK", StockLike());
                Cli.measurementProbe = new FakeProbe();          // R41: the suite never reads a real tablet
                if (Group("utf8-config")) Utf8Configuration();
                MakeTool();
                if (Group("process-status")) ProcessStatusAndArguments();
                if (Group("hid-transport")) HidTransportCounts();
                if (Group("backups")) Backups();
                if (Group("flash-status")) FlashStatusAndDryRun();
                if (Group("startup")) Startup();
                if (Group("closed-acknowledgement")) ClosedAcknowledgement();
                if (Group("sequential-prompt")) SequentialPrompt();
                if (Group("disclosure")) Disclosure();
                if (Group("one-backup-per-run")) OneBackupPerRun();
                if (Group("wizard-during-backup")) WizardDuringBackup();
                if (Group("wizard-backup-option")) WizardBackupOption();
                if (Group("prompt-layout")) PromptLayout();
                if (Group("interactive-input")) InteractiveInput();
                if (Group("patcher-packages")) PatcherPackages();
                // R45: the four CLI builds (each a twin-gated build of about 15 s) are four groups, so the runner can run
                // them side by side; until R45-rc3 they were one 67 s group that set the length of every run
                if (args.Length > 0 && Group("cli-firmware"))
                {
                    string before = Commands();
                    Set("ADT_HOST_MODE", "ok");
                    Check(Cli.Run(new string[] { args[0], Path.Combine(folder, "dry build.hex"), "--rate", "fast", "--flash-dry-run" }) == 0, "full CLI dry-run builds and checks firmware");
                    Check(Commands() == before, "full CLI dry-run executes no DFU, dump or update command");
                    Check(File.Exists(Path.Combine(folder, "[VOLATILE]_dry build.hex")), "explicit firmware filenames disclose volatile changes");
                    before = Commands();
                    string forbidden = Path.Combine(folder, "discreet rapid.hex");
                    Check(Cli.Run(new string[] { args[0], forbidden, "--rate", "rapid", "--discreet" }) == 1 && !File.Exists(forbidden), "discreet mode cannot silently enable firmware data changes for RAPID");
                    Check(Commands() == before, "rejected storage settings execute no tablet commands");
                }
                if (args.Length > 0 && Group("cli-measurement-dry"))
                {
                    string before = Commands();
                    Set("ADT_HOST_MODE", "ok");
                    string measurement = Path.Combine(folder, "measurement");
                    Directory.CreateDirectory(measurement);
                    Check(Cli.Run(new string[] { args[0], measurement, "--rate", "fast", "--calibration-pass", "--flash-dry-run" }) == 0,
                        "measurement dry-run builds and gates telemetry firmware");
                    Check(Commands() == before && !Cli.calibrationFirmwareActive && !Cli.measurementInstallRequested,
                        "measurement dry-run never contacts a tablet or claims installation");
                }
                if (args.Length > 0 && Group("cli-measurement-flash"))
                {
                    string before = Commands();
                    Set("ADT_HOST_MODE", "ok");
                    string measurement = Path.Combine(folder, "measurement");
                    Directory.CreateDirectory(measurement);
                    Check(Program.Main(new string[] { args[0], measurement, "--rate", "fast", "--calibration-pass", "--flash" }) == 0,
                        "explicit measurement installation returns success after a verified update");
                    Check(Cli.calibrationFirmwareActive && Cli.measurementInstallRequested && Commands().Substring(before.Length) == "dfu\ndump\nupdate\n",
                        "measurement CLI captures a fresh backup and tracks the intentional telemetry installation");
                    // FlashSteps only hands the file to xppen-tools (here the fake, which does not read it): the stock
                    // dump stands in for any normal image
                    Check(Cli.FlashSteps(args[0], true, false) && !Cli.calibrationFirmwareActive,
                        "normal update restores the measurement installation");
                }
                if (args.Length > 0 && Group("cli-flash-fails"))
                {
                    Set("ADT_HOST_MODE", "update-fails");
                    Check(Cli.Run(new string[] { args[0], Path.Combine(folder, "flash build.hex"), "--rate", "fast", "--flash" }) == 1, "CLI returns failure when xppen-tools update fails");
                    Set("ADT_HOST_MODE", "ok");
                }
                P.Line("RESULT host regressions: " + F.Str(checks) + " passed (fake xppen-tools only)");
                return 0;
            }
            catch (Exception error) { P.Line(error.ToString()); return 1; }
        }
    }
}
