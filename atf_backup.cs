// atf_backup.cs - startup backups and xppen-tools arguments. No firmware is programmed here.
using System;
using Gd32Emu;

namespace AnargonsTabletTools
{
    public class BackupCapture : Job
    {
        public string folder, tool, path, error, log = "";
        public byte[] flash;
        public bool restoreRuntime, echo, restored;
        public volatile bool done;
        public volatile string phase = "starting xppen-tools";   // what it is doing now (StartupBackup.State)
        public double started;
        public string incomplete;                // R42: restore images next to the dump that stop before the firmware end
        // R45-rc5 (4), atf_modcheck.cs: inDfu = the tablet is in update mode already (no `dfu`); check = ask the tablet
        // for its firmware CRCs first (only with a backup on file), and stop before the dump when it runs modified
        // firmware whose dump may be skipped (deferred: the dump waits for the user's choice)
        public bool inDfu, check, deferred;
        public FwState state;

        class Runner : ToolRunner
        {
            BackupCapture c;
            public Runner(BackupCapture b) { c = b; }
            public override string[] Run(string args) { return c.RunTool(args); }
        }

        // R42: `xppen-tools dump` also writes ic902-code.bin and ic902-stock.hex ("application restore image"). Older
        // xppen-tools releases cut both at a fixed 35208 bytes (0x0800C188; their deco01v3-code.bin / -stock.hex), and on
        // a damaged tablet they end where its flash does. Flashed, such an image erases the start-up table and .data
        // (byte for byte the damage on Anargon's and Anomy's tablets). One that stops before the firmware end of the
        // dump is renamed so it cannot be flashed by mistake. Returns what was renamed, or null.
        public static string CheckRestoreImages(string dir, byte[] full)
        {
            if (full == null || full.Length != 0x10000) return null;
            byte[] app = new byte[0x10000 - 0x3800];
            P.Copy(full, 0x3800, app, 0, app.Length);
            FwCheck ck = Repair.Examine(app, app.Length);
            int end = ck.known >= 0 ? Layout.KNOWN[ck.known].end : Layout.FindEnd(app, 0, app.Length);
            if (end < 0) return null;                // no known end to compare with
            string[] names = new string[] { "ic902-code.bin", "ic902-stock.hex" };
            string msg = null;
            for (int i = 0; i < names.Length; i++)
            {
                string f = P.PathJoin(dir, names[i]);
                if (!P.FileExists(f)) continue;
                int last;
                try
                {
                    if (i == 0) last = Build.BASE + P.ReadAllBytes(f).Length;
                    else { int[] lo = new int[1]; byte[] d = IntelHex.Read(f, lo); last = lo[0] + d.Length; }
                }
                catch (Exception) { continue; }
                if (last >= end) continue;
                string to = f + ".INCOMPLETE-DO-NOT-FLASH";
                string done;
                // R43 review: a file that cannot be renamed (locked, read-only) is reported, and the backup still counts
                try { P.WriteAllBytes(to, P.ReadAllBytes(f)); P.DeleteFile(f); done = "Renamed to " + P.FileName(to) + "."; }
                catch (Exception e) { done = "It could not be renamed (" + P.Msg(e) + "): do not flash it."; }
                msg = (msg != null ? msg + " " : "") + names[i] + " (written by xppen-tools) stops at " + F.Hx(last, 8) + ", before the firmware end " + F.Hx(end, 8) + ": flashed, it would erase the start-up table and .data. " + done;
            }
            return msg;
        }

        // ProcessStartInfo.Arguments uses double-quote/backslash escaping on all supported runtimes.
        public static string Quote(string value)
        {
            string result = "\"";
            int slashes = 0;
            for (int i = 0; i < P.Len(value); i++)
            {
                int c = P.CharAt(value, i);
                if (c == '\\') { slashes++; continue; }
                if (c == '"') result += Screen.Rule('\\', slashes * 2 + 1) + "\"";
                else result += Screen.Rule('\\', slashes) + (char)c;
                slashes = 0;
            }
            return result + Screen.Rule('\\', slashes * 2) + "\"";
        }

        public string[] RunTool(string args)
        {
            log += "$ xppen-tools " + args + "\n";
            string[] result = P.RunInResult(P.DirName(tool), tool, args, "        ", echo);
            log += result[1] + "\nexit status: " + result[0] + "\n";
            return result;
        }

        public override void Run()
        {
            string latest = P.PathJoin(P.FullPath(folder), "last-dump");
            try
            {
                if (tool == null) { error = "xppen-tools was not found. Set ADT_XPPEN_TOOLS or put it next to this program."; return; }
                P.MakeDirs(latest);
                string stamp = P.Now("stamp");
                // A failed dfu command can mean the tablet is already in DFU. Only a successful fresh dump is accepted.
                if (!inDfu)
                {
                    phase = "xppen-tools is switching the tablet to update mode";
                    RunTool("dfu " + Cli.XPPEN_IDS);
                    P.Sleep(2.0);
                }
                if (check && ModCheck.BackupOnFile(folder) != null)
                {
                    phase = "xppen-tools is checking the tablet's firmware (a few CRCs)";
                    state = ModCheck.Query(new Runner(this), folder);
                    log += "Firmware check: the tablet runs " + state.Text() + " (" + state.Detail() + ").\n";
                    if (ModCheck.NoSkip(folder, state) == null)
                    {
                        deferred = true;
                        log += "Not dumped yet: modified firmware, and " + ModCheck.BackupOnFile(folder) + " is on file. The dump waits for your choice.\n";
                        return;
                    }
                }
                string pending = P.PathJoin(latest, "pending-" + stamp);
                for (int n = 2; P.DirExists(pending); n++) pending = P.PathJoin(latest, "pending-" + stamp + "-" + F.Str(n));
                P.MakeDirs(pending);
                phase = "xppen-tools is reading the tablet";
                string[] result = RunTool("dump " + Quote(pending));
                string dumped = P.PathJoin(pending, "ic902-main.bin");
                if (!P.Eq(result[0], "0") || !P.FileExists(dumped))
                {
                    error = "The startup/flash backup failed. See last-dump/backup.log; no flash is allowed.";
                    return;
                }
                byte[] bytes = P.ReadAllBytes(dumped);
                if (bytes.Length != 0x10000)
                {
                    error = "The backup must contain exactly 65536 bytes; received " + F.Str(bytes.Length) + ". No flash is allowed.";
                    return;
                }
                // Keep a complete, immutable copy every time, including the bootloader of THIS tablet.
                string root = Cli.DumpsDir(P.FullPath(folder));
                string archive = P.PathJoin(root, stamp);
                for (int n = 2; P.DirExists(archive); n++) archive = P.PathJoin(root, stamp + "-" + F.Str(n));
                P.MakeDirs(archive);
                P.WriteAllBytes(P.PathJoin(archive, "ic902-main.bin"), bytes);
                string primary = Cli.PrimaryDump(P.FullPath(folder));
                if (!P.FileExists(primary)) { P.MakeDirs(P.DirName(primary)); P.AtomicWriteAllBytes(primary, bytes); }
                path = P.PathJoin(latest, "ic902-main.bin");
                P.AtomicWriteAllBytes(path, bytes);
                P.DeleteFile(dumped);
                flash = bytes;
                log += "Saved complete backup: " + path + "\nArchived: " + archive + "\n";
                incomplete = CheckRestoreImages(pending, bytes);
                if (incomplete != null) log += incomplete + "\n";
            }
            catch (Exception e) { error = "Backup failed: " + P.Msg(e); flash = null; path = null; }
            finally
            {
                if (tool != null && restoreRuntime)
                {
                    phase = "xppen-tools is returning the tablet to normal mode";
                    string[] result = RunTool("exit-dfu --pid " + Cli.PID_DFU);
                    restored = P.Eq(result[0], "0");
                    if (!restored) log += "The tablet may still be in update mode. Run xppen-tools exit-dfu --pid " + Cli.PID_DFU + ".\n";
                }
                try { P.MakeDirs(latest); P.AtomicWriteAllBytes(P.PathJoin(latest, "backup.log"), P.Utf8Bytes(log + (error != null ? error + "\n" : ""))); }
                catch (Exception) { if (error == null) error = "Backup log could not be saved in " + latest + "."; }
                done = true;
            }
        }
    }

    // The startup dump runs on a background thread of this program, and that thread owns its xppen-tools child
    // (Ctrl+C or exit kills it; see P.StartChild). It never prints, so the steps keep going while it reads the tablet.
    // Its result is picked up between screens (Poll) and waited for only where the dump itself is needed (Wait):
    // its file as the input, calibration, flashing, and program exit. Only the firmware mod (tool 1) starts it.
    // R45-rc5 (4): with a backup on file it first checks the tablet's firmware (atf_modcheck.cs). Modified firmware that
    // can be rebuilt is not dumped on its own: the tablet goes back to normal mode, and the steps that need a dump ask
    // (Enter = dump, s = skip). The dump could not be stopped once running, so this choice comes before it.
    public static class StartupBackup
    {
        static BackupCapture pending;
        // the last startup backup stopped before its dump (modified firmware); Cli.tabletState says what it found
        public static bool deferred = false;

        public static void Begin(string folder)
        {
            if (pending != null) return;
            deferred = false;
            string tool = Cli.FindTool();
            if (tool == null) { Screen.Warn("No startup backup: xppen-tools was not found. Put it next to this program (or set ADT_XPPEN_TOOLS). Flashing needs a backup."); return; }
            pending = new BackupCapture();
            pending.folder = P.FullPath(folder); pending.tool = tool; pending.restoreRuntime = true; pending.started = P.NowSec(); pending.check = true;
            try { P.StartBackground(pending); }
            catch (Exception e) { pending.error = "Could not start backup: " + P.Msg(e); pending.done = true; }
            Screen.Warn("Startup backup is running in the background (about 3 minutes; the tablet is in update mode meanwhile).");
        }

        // a startup dump was started and its result has not been collected yet
        public static bool Running() { return pending != null; }
        // what the running startup dump is doing, and for how long (null: none running); xppen-tools does the work, so a
        // step that takes far longer than usual is named as such (no tablet, or xppen-tools waiting)
        public static string State()
        {
            BackupCapture c = pending;
            if (c == null) return null;
            int s = (int)(P.NowSec() - c.started);
            return c.phase + ", " + F.Str(s) + " s so far" + (s > 300 ? " (a dump usually takes about 3 minutes: check that the tablet is plugged in)" : "");
        }
        // the file the running startup dump will write (null: none running)
        public static string Target() { return pending == null ? null : P.PathJoin(P.PathJoin(pending.folder, "last-dump"), "ic902-main.bin"); }

        // never blocks: collects the result once the background dump has finished
        public static void Poll() { if (pending != null && pending.done) Finish(); }

        public static void Wait()
        {
            if (pending == null) return;
            if (!pending.done) P.Line("    Waiting for the startup backup (a full tablet dump can take about 3 minutes)...");
            while (!pending.done) P.Sleep(0.05);
            Finish();
        }

        static void Finish()
        {
            BackupCapture c = pending;
            pending = null;                      // first: collected exactly once, even if Note prints or fails
            if (c.state != null) Cli.tabletState = c.state;
            if (c.deferred)
            {
                deferred = true;
                Screen.SetWarn("Startup backup", "Startup backup: not made yet. The tablet runs " + c.state.Text() + ", which need not be dumped; you choose when a step needs the dump (s skips it)."
                    + (c.restored ? "" : " The tablet may still be in update mode; see last-dump/backup.log."));
                return;
            }
            if (c.flash != null)
            {
                try
                {
                    Cli.Note(c.folder, c.flash, c.path, "whole flash (startup)");
                    if (c.error == null) Cli.sessionBackup = c.path;     // this run's backup: no second one before flashing
                }
                catch (Exception e) { c.error = "The dump was saved, but its history could not be recorded: " + P.Msg(e); }
            }
            if (c.incomplete != null) Screen.SetWarn("Restore image", "Restore image: " + c.incomplete);
            Screen.SetWarn("Startup backup", c.error != null ? "Startup backup: " + c.error
                : "Startup backup saved: " + c.path + (c.restored ? "." : ". The tablet may still be in update mode; see last-dump/backup.log."));
        }
    }
}
