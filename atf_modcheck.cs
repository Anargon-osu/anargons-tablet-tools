// atf_modcheck.cs (R45-rc5 (4), 2026-11-17) - is the tablet's firmware modified? A few CRCs from the tablet instead
// of a dump.
//
// Why (Anargon, 2026-11-17): "find a method for specific detection of modified firmware. If the firmware doesnt match
// say, a hash within a specific section, it automatically is modded, and modded firmware shall be given the option to
// skip xppen-dump (which will now work on any blocker step)".
//
// How: in update mode, xppen-tools' read-only command `crc ADDRESS SIZE` asks the bootloader for the CRC-16/XMODEM of
// one flash range. A dump reads all 64 KB through the same kind of query, many times over (about 2.5 minutes); one CRC
// takes a moment. The check asks for up to three ranges:
//   1. The start, 0x08003800-0x0800398B: the vector table and the first code. No build of this tool changes it (the
//      first byte it changes is 0x0800398C, the byte Cli.Note looks at), Mily's and Mei's edits are later too, and
//      February and December differ in it. So it names the stock build the firmware was made from.
//   2. The firmware, 0x08003800 up to that build's end (code and .data): the stock CRC means stock firmware.
//   3. The end, 0x0800C188 up to the build's end: all erased is the rev 35 damage, not a modification.
// A start that matches no stock build is firmware this tool does not know.
// Checked offline (scratch, R45-rc5 (4)) on February and December stock, Anargon's and Mily's February dumps, Anomy's
// damaged December dump, and this tool's FAST, ULTRA and lab builds of both: every build keeps its stock start, Mily's
// edit reads as modified, Anomy's dump as damaged.
//
// Only fingerprints are stored (AGENTS.md rule 15): the CRCs of the two known stock builds. Your own stock backup
// (dumps/primary-dump) adds its own, so a stock build this tool does not know is still recognised as stock.
//
// Skipping the dump (Cli.BeforeFlash, the wizard's b, the startup backup) is offered only for modified firmware, and
// only with a backup on file (dumps/primary-dump, in the folder used or next to the program) whose start is the
// tablet's. That backup then stands in for the dump, as for the measurement firmware since R41
// (Cli.MainBackupForMeasurement): the check that the image was built from the tablet's firmware build
// (Cli.TabletMatches) uses it. Stock, damaged, unknown or unchecked firmware is always dumped, and Enter (the default)
// dumps modified firmware too.
using System;
using System.Text;
using Gd32Emu;

namespace AnargonsTabletTools
{
    // runs one xppen-tools command; returns { exit status, output }
    public abstract class ToolRunner { public abstract string[] Run(string args); }

    // the foreground check: shows each command and the CRC it got, not xppen-tools' whole output
    public class ShownRunner : ToolRunner
    {
        string tool;
        public ShownRunner(string t) { tool = t; }
        public override string[] Run(string args)
        {
            P.Line("        $ xppen-tools " + args);
            string[] r = P.RunInResult(P.DirName(tool), tool, args, "        ", false);
            int c = ModCheck.Parse(r[1]);
            P.Line("          " + (c >= 0 && P.Eq(r[0], "0") ? "CRC " + ModCheck.Hex(c) : "no CRC (exit status " + r[0] + ")"));
            return r;
        }
    }

    public class FwState
    {
        public const int UNCHECKED = 0, STOCK = 1, MODDED = 2, DAMAGED = 3, UNKNOWN = 4;
        public int kind = UNCHECKED;
        public string from;                              // the stock build its start matches (null: none)
        public string why;                               // UNCHECKED: what failed
        public int end = -1, startCrc = -1, fwCrc = -1, stockCrc = -1, endCrc = -1;

        public string Text()
        {
            if (kind == STOCK) return "stock firmware (" + from + ")";
            if (kind == MODDED) return "modified firmware, made from " + from;
            if (kind == DAMAGED) return "damaged firmware (" + from + " with its end erased from 0x0800C188)";
            if (kind == UNKNOWN) return "firmware this tool does not know (its start matches no stock build)";
            return "firmware that could not be checked (" + (why != null ? why : "no check ran") + ")";
        }

        // the CRCs, for the screen and the logs
        public string Detail()
        {
            if (startCrc < 0) return "no CRC";
            string s = "start CRC " + ModCheck.Hex(startCrc);
            if (fwCrc >= 0) s += ", firmware to 0x" + F.HexDigits(end - 1, 8, true) + " CRC " + ModCheck.Hex(fwCrc) + (kind != STOCK ? ", stock " + ModCheck.Hex(stockCrc) : "");
            if (endCrc >= 0) s += ", end CRC " + ModCheck.Hex(endCrc) + (kind == DAMAGED ? " (erased)" : "");
            return s;
        }
    }

    public static class ModCheck
    {
        public const int BASE = 0x08003800, START_END = 0x0800398C, TAIL = 0x0800C188;
        // fingerprints of Layout.KNOWN, in its order: CRC-16/XMODEM of [BASE, START_END) and of [BASE, end)
        public static readonly int[] KNOWN_START = new int[] { 0xB662, 0x601B };
        public static readonly int[] KNOWN_FW = new int[] { 0x5497, 0x69A2 };

        public static string Hex(int crc) { return "0x" + F.HexDigits(crc, 4, true); }

        // CRC-16/XMODEM (polynomial 0x1021, start 0), what xppen-tools and the bootloader compute
        public static int Xmodem(byte[] d, int off, int len)
        {
            int c = 0;
            for (int i = 0; i < len; i++)
            {
                c ^= (d[off + i] & 0xFF) << 8;
                for (int k = 0; k < 8; k++) c = (c & 0x8000) != 0 ? ((c << 1) ^ 0x1021) & 0xFFFF : (c << 1) & 0xFFFF;
            }
            return c;
        }
        public static int XmodemErased(int len)
        {
            byte[] b = new byte[len];
            for (int i = 0; i < len; i++) b[i] = 0xFF;
            return Xmodem(b, 0, len);
        }

        // the backup on file that can stand in for a dump: dumps/primary-dump (the first complete backup), in FOLDER or
        // next to the program (where the startup backup keeps it); null: none
        public static string BackupOnFile(string folder)
        {
            string[] c = new string[] { Cli.PrimaryDump(P.FullPath(folder)), Cli.PrimaryDump(P.FullPath(P.ExeDir())) };
            for (int i = 0; i < c.Length; i++)
            {
                try { if (P.FileExists(c[i]) && P.FileSize(c[i]) == 0x10000) return c[i]; }
                catch (Exception) { }
            }
            return null;
        }

        static byte[] AppOf(string main)
        {
            if (main == null) return null;
            byte[] flash;
            try { flash = P.ReadAllBytes(main); }
            catch (Exception) { return null; }
            if (flash.Length != 0x10000) return null;
            byte[] app = new byte[0x10000 - 0x3800];
            P.Copy(flash, 0x3800, app, 0, app.Length);
            return app;
        }

        // the same test as Cli.Note: a build of this tool (its build string, or its first changed byte)
        static bool OwnBuild(byte[] app)
        {
            string b = Build.ReadBuildString(app, app.Length);
            return (b != null && P.StartsWith(b, Build.VERSION)) || (app[START_END - BASE] & 0xFF) != 0x30;
        }

        class Ref { public string name; public int start, end, fw; }

        // the stock builds the tablet is compared with: the known ones, then your own stock backup (if it is stock,
        // complete and not one of them)
        static Ref[] Refs(string folder)
        {
            Ref[] r = new Ref[Layout.KNOWN.Length + 1];
            int n = 0;
            for (int i = 0; i < Layout.KNOWN.Length && i < KNOWN_FW.Length; i++)
            {
                Ref x = new Ref(); x.name = Layout.KNOWN[i].build; x.start = KNOWN_START[i]; x.end = Layout.KNOWN[i].end; x.fw = KNOWN_FW[i];
                r[n++] = x;
            }
            string main = BackupOnFile(folder);
            byte[] app = AppOf(main);
            if (app != null && !OwnBuild(app))
            {
                FwCheck ck = Repair.Examine(app, app.Length);
                int end = ck.known >= 0 ? Layout.KNOWN[ck.known].end : Layout.FindEnd(app, 0, app.Length);
                if (!ck.Damaged() && end > TAIL && end <= BASE + app.Length)
                {
                    Ref x = new Ref(); x.start = Xmodem(app, 0, START_END - BASE); x.end = end; x.fw = Xmodem(app, 0, end - BASE);
                    string b = Build.ReadBuildString(app, app.Length);
                    x.name = "your backup's firmware, " + (b != null ? b : "no build string");
                    bool dup = false;
                    for (int i = 0; i < n; i++) if (r[i].start == x.start && r[i].end == x.end && r[i].fw == x.fw) dup = true;
                    if (!dup) r[n++] = x;
                }
            }
            Ref[] o = new Ref[n];
            for (int i = 0; i < n; i++) o[i] = r[i];
            return o;
        }

        public static string Command(int addr, int size) { return "crc 0x" + F.HexDigits(addr, 8, true) + " " + F.Str(size) + " --pid " + Cli.PID_DFU; }

        // xppen-tools prints "[INFO] CRC16/XMODEM(<address>, <size>) = 0x<crc>"; -1: no such line
        public static int Parse(string output)
        {
            string[] lines = P.Lines(output);
            for (int i = 0; i < lines.Length; i++)
            {
                int a = P.IndexOf(lines[i], "CRC16/XMODEM(");
                if (a < 0) continue;
                string rest = P.Sub(lines[i], a, P.Len(lines[i]) - a);
                int e = P.IndexOf(rest, ") = 0x");
                if (e < 0) continue;
                int v = 0, d = 0;
                for (int k = e + 6; k < P.Len(rest) && d < 4; k++, d++)
                {
                    int ch = P.CharAt(rest, k), h = ch >= '0' && ch <= '9' ? ch - '0' : ch >= 'a' && ch <= 'f' ? ch - 'a' + 10 : ch >= 'A' && ch <= 'F' ? ch - 'A' + 10 : -1;
                    if (h < 0) break;
                    v = v * 16 + h;
                }
                if (d > 0) return v;
            }
            return -1;
        }

        static int Ask(ToolRunner run, int addr, int size, FwState s)
        {
            string[] r = run.Run(Command(addr, size));
            int c = P.Eq(r[0], "0") && P.IndexOf(r[1], "[ERROR]") < 0 ? Parse(r[1]) : -1;
            if (c < 0) s.why = "xppen-tools crc " + F.Hx(addr, 8) + " gave no CRC, exit status " + r[0];
            return c;
        }

        // the check; the tablet must be in update mode
        public static FwState Query(ToolRunner run, string folder)
        {
            FwState s = new FwState();
            Ref[] refs = Refs(folder);
            s.startCrc = Ask(run, BASE, START_END - BASE, s);
            if (s.startCrc < 0) return s;
            Ref r = null;
            for (int i = 0; i < refs.Length && r == null; i++) if (refs[i].start == s.startCrc) r = refs[i];
            if (r == null) { s.kind = FwState.UNKNOWN; return s; }
            s.from = r.name; s.end = r.end; s.stockCrc = r.fw;
            s.fwCrc = Ask(run, BASE, r.end - BASE, s);
            if (s.fwCrc < 0) return s;
            for (int i = 0; i < refs.Length; i++)
                if (refs[i].start == s.startCrc && refs[i].end == r.end && refs[i].fw == s.fwCrc) { s.kind = FwState.STOCK; s.from = refs[i].name; return s; }
            s.endCrc = Ask(run, TAIL, r.end - TAIL, s);
            if (s.endCrc < 0) return s;
            s.kind = s.endCrc == XmodemErased(r.end - TAIL) ? FwState.DAMAGED : FwState.MODDED;
            return s;
        }

        // null: the dump may be skipped; else why not
        public static string NoSkip(string folder, FwState s)
        {
            if (s == null || s.kind != FwState.MODDED) return "the tablet does not run modified firmware";
            string main = BackupOnFile(folder);
            if (main == null) return "there is no backup on file (dumps/primary-dump) to use instead";
            byte[] app = AppOf(main);
            if (app == null) return main + " could not be read";
            if (Repair.Examine(app, app.Length).Damaged()) return "the backup on file, " + main + ", is damaged";
            if (Xmodem(app, 0, START_END - BASE) != s.startCrc) return "the backup on file, " + main + ", holds another firmware build than the one the tablet's firmware was made from";
            return null;
        }

        // Enter = dump (the default), s = skip; true: skipped (the backup on file is in use)
        public static bool AskSkip(string folder, FwState s, bool fresh)
        {
            string main = BackupOnFile(folder);
            Screen.Para("The tablet runs " + s.Text() + " (" + s.Detail() + "). It can be rebuilt, so it need not be dumped: your backup "
                + main + " holds the firmware it was made from" + (fresh ? ", and two more CRCs check the bootloader area instead." : "."), 4);
            P.Line("");
            P.Line("    Enter = dump it anyway (about 3 minutes)");
            P.Line("    s     = skip the dump");
            while (true)
            {
                string a = P.Lower(P.Trim(Screen.Ask("    > ")));
                if (P.Len(a) == 0) return false;
                if (P.Eq(a, "s")) return true;
            }
        }

        // the backup on file stands in for the dump. tabletFlash stays empty: it would show the backup's bootloader and
        // counter, not the tablet's
        public static void UseBackupOnFile(string folder, FwState s)
        {
            string main = BackupOnFile(folder);
            byte[] app = AppOf(main);
            Cli.tabletApp = app; Cli.tabletFlash = null;
            Cli.tabletBuild = Build.ReadBuildString(app, app.Length);
            Cli.tabletModded = OwnBuild(app);
            Cli.sessionBackup = main; Cli.dumpSkipped = true;
            try
            {
                string list = P.PathJoin(Cli.DumpsDir(P.FullPath(folder)), "dumps.txt");
                P.MakeDirs(P.DirName(list));
                P.AppendText(list, P.Now("stamp") + "  (no dump)  the tablet ran " + s.Text() + " (" + s.Detail() + "); dump skipped, used " + main + "\r\n");
            }
            catch (Exception) { }
            P.Line("    Dump skipped. Using " + main + " for the checks before flashing.");
        }

        // return to stock, step 3 (instead of reading the whole flash): the bootloader code must be the stock one and the
        // two counter pages erased (Cli.StockBootloaderRestored). 1 yes, 0 no, -1 not checked
        public static int BootRestored(ToolRunner run, byte[] stockBoot)
        {
            if (stockBoot == null || stockBoot.Length < 0x2800) return -1;
            FwState s = new FwState();
            int code = Ask(run, 0x08000000, 0x2800, s);
            if (code < 0) return -1;
            int pages = Ask(run, 0x08002800, 0x800, s);
            if (pages < 0) return -1;
            return code == Xmodem(stockBoot, 0, 0x2800) && pages == XmodemErased(0x800) ? 1 : 0;
        }
    }
}
