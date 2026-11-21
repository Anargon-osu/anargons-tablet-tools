// atf_thresholds.cs (R45-rc5 (3), 2026-11-17) - Measurement Tools: Tablet signal thresholds, the measuring of Standard
// calibration on its own.
//
// Why (Anargon, 2026-11-17): "Build and package the still pen noise test from standard, user calibration too. Dont
// remove any user facing calibrations though." The Tablet Firmware Mod's step 4 is unchanged; this entry runs the same
// code (Cli.Calibrate: the measurement firmware, its twin check, the backup before flashing, the three pen positions,
// the saved calibration file) with no firmware build after it (Cli.calStandalone changes only the texts that would
// promise one). The measurement firmware sends readings instead of a cursor, so afterwards the entry offers to flash
// back what the tablet ran before, from the backup made before the measurement (Cli.tabletApp).
//
// It flashes the tablet twice (the measurement firmware, then the firmware back), each time only after xppen-tools'
// own typed confirmation. Its gate replaces the tablet reader with the digital twin (Cli.calSource) and xppen-tools
// with a fake one.
using System;
using Gd32Emu;

namespace AnargonsTabletTools
{
    public static class ThresholdsTest
    {
        public const string TITLE = "Tablet signal thresholds", SUB = "Standard calibration's measuring";
        public const string INTRO = "Standard calibration's measuring, on its own: how high your tablet picks up the pen, "
            + "your pen's frequency, and whether pressure shows while hovering. It builds a measurement firmware from your "
            + "stock firmware, backs up the tablet and flashes it (there is no cursor while it runs), walks you through three "
            + "pen positions, and saves the values to a file (calibration-PRESET.txt, for --calibration). Then it flashes back "
            + "the firmware the tablet ran before. The Tablet Firmware Mod's own calibration (step 4) is unchanged.";
        public const string RESTORE_FILE = "previous-firmware.hex";      // not "*-measurement.hex": that is the measurement firmware

        public static int Run()
        {
            string folder = Folders.Of(Folders.EXPORT);
            Screen.Step(TITLE + " (" + SUB + ")");
            Screen.Para(INTRO, 4);
            P.Line("");

            // the stock firmware the measurement firmware is built from (Cli.PickInput: the tablet's own dump first)
            StrList notes = new StrList();
            string dflt = Cli.PickInput(folder, notes);
            for (int i = 0; i < notes.Count(); i++) Screen.Para(notes.Get(i), 4);
            byte[] app = null;
            InputInfo info = null;
            while (app == null)
            {
                Screen.Para("Stock firmware to build the measurement firmware from. Enter = " + (P.FileExists(dflt) ? P.FullPath(dflt)
                    : "nothing found yet: put ic902-main.bin (from xppen-tools) in " + Folders.Of(Folders.IMPORT) + ", or type a file"), 4);
                string f = P.Replace(P.Trim(Screen.Ask("    File (Enter = this one, q = back): > ")), "\"", "");
                if (P.Eq(P.Lower(f), "q")) return 0;
                if (P.Len(f) == 0) f = dflt;
                if (!P.FileExists(f)) { Screen.Para("File: " + f + " was not found.", 4); P.Line(""); continue; }
                InputInfo inf = new InputInfo();
                byte[] a;
                try { a = Build.ReadInput(f, inf); }
                catch (Stop e) { Screen.Para("File: " + e.msg, 4); P.Line(""); continue; }
                catch (Exception e) { Screen.Para("File: " + f + " could not be read (" + P.Msg(e) + ").", 4); P.Line(""); continue; }
                if (inf.modded) { Screen.Para("File: " + f + " is a build of this tool, not stock firmware.", 4); P.Line(""); continue; }
                app = a; info = inf;
            }

            // the preset: the measurement firmware reads the pen as often as the build it is for
            Level[] lv = new Level[Build.LEVELS.Length];
            int nl = 0, dfl = 1;
            for (int i = 0; i < Build.LEVELS.Length; i++)
                if (Build.LEVELS[i].period > 0) { if (P.Eq(Build.LEVELS[i].key, "500")) dfl = nl + 1; lv[nl++] = Build.LEVELS[i]; }
            Level level = null;
            P.Line("");
            Screen.Para("Preset to measure for (the measured levels belong to it):", 4);
            P.Line("");
            for (int i = 0; i < nl; i++) { P.Line("    " + F.Str(i + 1) + ".  " + lv[i].label); P.Line(""); }
            while (level == null)
            {
                string c = P.Trim(Screen.Ask("    Preset (1-" + F.Str(nl) + ", Enter = " + F.Str(dfl) + ", q = back): > "));
                if (P.Eq(P.Lower(c), "q")) return 0;
                int k = P.Len(c) == 0 ? dfl : Num(c);
                if (k >= 1 && k <= nl) level = lv[k - 1];
            }

            // Standard's measuring, as the wizard's step 4 runs it: the default settings, Anargon's fallback values
            bool[] st = Cli.Defaults();
            st[Build.SetIndex("cal")] = false;
            Options o = new Options();
            o.fallback = true;
            P.MakeDirs(folder);
            Build.LOG.SetFolder(folder);
            Cli.calStandalone = true;
            try { Cli.Calibrate(app, info, level, st, folder, o, MeasurementTools.realtime); }
            finally { Cli.calStandalone = false; }
            if (Cli.calibrationFirmwareActive) Restore(folder);
            return 0;
        }

        // the measurement firmware may be on the tablet: flash back what the backup before it read
        static void Restore(string folder)
        {
            Screen.Step("Putting back the firmware the tablet ran before");
            byte[] app = Cli.tabletApp;
            string none = "The tablet runs the measurement firmware now, so there is no cursor. ";
            if (app == null)
            {
                Screen.Para(none + "This run has no backup of what it ran before: build and flash your firmware with the Tablet Firmware Mod (tool 1).", 4);
                P.Line(""); Screen.Ask("    Press Enter > ");
                return;
            }
            if (Repair.Examine(app, app.Length).Damaged())
            {
                Screen.Para(none + "The backup of what it ran before is damaged (erased from 0x0800C188), so it is not flashed back: build and flash your firmware with the Tablet Firmware Mod (tool 1), or check the backup with " + Repair.TITLE + " (tool 2).", 4);
                P.Line(""); Screen.Ask("    Press Enter > ");
                return;
            }
            string build = Cli.tabletBuild != null ? Cli.tabletBuild : "no build string";
            string what = Cli.tabletModded ? "a build of this tool (" + build + ")" : "stock firmware (" + build + ")";
            string hex = P.PathJoin(folder, RESTORE_FILE);
            Build.WriteHex(app, Build.BASE, hex);
            // R45-rc5 (4): the dump of the modified firmware it ran was skipped, so only the backup on file can go back
            if (Cli.dumpSkipped)
                Screen.Para(none + "The modified firmware it ran before was not dumped (you skipped that), so it is not on file. Next: flash your backup on file, "
                    + what + ", from " + Cli.sessionBackup + " (written as " + hex + "), or build your firmware again with the Tablet Firmware Mod (tool 1).", 4);
            else Screen.Para(none + "Next: flash back what it ran before, " + what + ", from the backup " + Cli.sessionBackup + " (written as " + hex + ").", 4);
            P.Line("");
            P.Line("    Enter = flash it " + (Cli.dumpSkipped ? "" : "back ") + "now (xppen-tools asks you to type a confirmation first)");
            P.Line("    s     = leave the measurement firmware on the tablet (flash a build later with the Tablet Firmware Mod)");
            while (true)
            {
                string a = P.Lower(P.Trim(Screen.Ask("    > ")));
                if (P.Eq(a, "s")) { P.Line(""); P.Line("    Left as it is: there is no cursor until other firmware is flashed."); return; }
                if (P.Len(a) > 0) continue;
                bool ok;
                try { ok = Cli.FlashSteps(hex, false, false); }
                catch (Stop e) { ok = false; P.Line("    " + e.msg); }
                if (ok)
                {
                    P.Line(""); P.Line("    The firmware is back. Try hovering and drawing before real work."); P.Line("");
                    Screen.Ask("    Press Enter > ");
                    return;
                }
                P.Line("");
                Screen.Para("The update did not complete. Some firmware may already be written, so the tablet may not work until a complete image is flashed. Enter = try again, s = stop here.", 4);
            }
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
