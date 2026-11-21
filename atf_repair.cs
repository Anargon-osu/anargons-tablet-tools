// atf_repair.cs - Anargon's Basic Tablet Recovery (anargons-basic-tablet-recovery): repairs a Deco 640 whose flash
// was left damaged, from one full dump of it (main.bin, as `xppen-tools dump` writes it).
//
// The damage it knows (both tablets reported in rev 35 had exactly this):
//   - everything from 0x0800C188 to the firmware end erased: the scatter-load table and the compressed .data are lost,
//     the firmware cannot start. R42: byte for byte what flashing a 35208-byte restore image leaves (an older
//     xppen-tools' deco01v3-code.bin / deco01v3-stock.hex, or `xppen-tools ihex --ic 902` of that code.bin);
//   - the bootloader settings page 0x08003400 erased except its boot-state word (the bootloader wrote only that word).
// Not damage (R42): changes another mod made at this tool's own patch sites (Movement Smoothing off, scan timing,
// 1 ms report interval...). Repair.Examine undoes them; the result must match the stored page CRCs exactly.
// Code pages are checked page by page against stored CRCs (atf_stockref.cs); damaged code is repaired only from a
// verified stock copy of the same build found on this PC. R42: the erased end is restored the same way, from the
// user's own copy (any complete dump or image of that build: FindEnd, checked against StockRef.END_CRC). This tool
// stores no XP-Pen firmware bytes. Anything else - an unknown build, a build of this tool,
// damaged code with no stock copy - is not repaired. A project maintainer or experienced firmware developer must
// inspect the dump before a device-specific repair is made.
//
// Outputs, for NAME:
//   NAME-specific-patcher.hex  the complete stock firmware of the tablet's build plus a 100-byte start-up step: if the
//                              settings page is erased, it writes the stock settings with boot state 1 (update
//                              requested) and restarts, so the tablet comes back in update mode (DFU) by itself, once.
//                              With the page intact it runs exactly like stock.
//   name-stock.hex             the stock firmware of that build, complete (flash it after the patcher).
//   NAME-repaired-main.bin     the dump with every damaged byte corrected (reference; for ROM-loader recovery only).
//   NAME-repair-report.txt     what was found and changed.
using System;
using System.Text;
using Gd32Emu;

namespace AnargonsTabletTools
{
    public class RepairResult
    {
        public bool ok, damaged, pageBroken;
        public bool needCopy;                          // R42: the end is erased and no copy of the user's own was found
        public string build, refFile, endFile;         // endFile: the user's file the end was restored from (R42)
        public string[] searched;                      // the folders FindEnd looked in
        public int known = -1, tailBad, tailErased, codePagesBad, codeBytesFixed;
        public StrList lines = new StrList();          // the report
        public StrList why = new StrList();            // why it cannot be repaired (empty if it can)
        public byte[] stockApp;                        // BASE..end of the stock firmware of this build
        public byte[] repaired;                        // the whole 64 KB, corrected
        public string edits = "";                      // changes by another mod that were undone (not damage), R42
    }

    // R42: a firmware compared with the verified stock build its build string names (Repair.Examine)
    public class FwCheck
    {
        public int known = -1;                         // index in Layout.KNOWN; -1: not a verified build
        public string build;
        public bool truncated;                         // the input stops before the firmware end
        public int tailErased, tailOther;              // end bytes (0x0800C188..) erased / changed in an unknown way
        public bool endOk;                             // R42: the end matches END_CRC (known edits undone)
        public bool tailCounted;                       // R42: tailErased / tailOther were counted against a copy of the end
        public int pagesEdited, pagesOther;            // code pages changed only at known places / in unknown ways
        public int pagesMissing;                       // R43: code pages past the end of a truncated input (not compared)
        public string pages = "";                      // the pages changed in unknown ways
        public StrList edits = new StrList();          // the known changes, e.g. "ADC samples per step 45 -> 4"
        // R45: how each known change was made (which instructions or data bytes, where, stock and found values), the
        // value found per change (Repair.EDIT_NAMES order, -1 = stock; the dead band: its first changed value), and
        // whose published mod the changes match (Repair.MOD_NAMES), else null
        public StrList how = new StrList();
        public int[] editNow;
        public bool deadZero;
        public string origin;
        public byte[] work;                            // BASE..end with the known changes undone and the end restored
        public byte[] stock;                           // = work when that is exactly the stock firmware (CRC-32), else null
        public bool Damaged() { return tailErased >= Repair.ERASED_MIN; }
        public bool Edited() { return edits.Count() > 0; }
    }

    public static class Repair
    {
        public const string NAME = "anargons-basic-tablet-recovery", TITLE = "Anargon's Basic Tablet Recovery";   // option 2 of the first screen; --recovery
        public const int BASE = 0x08003800, FULL = 0x10000, PAGE = 0x08003400;
        // the bootloader settings page as the stock firmware keeps it: identical in every dump seen (both stock builds,
        // and Anargon's tablet before the damage). Word 0 is the boot state (0 run, 1 update requested).
        public static readonly int[] PAGE_STOCK = new int[] { 0, -1, 0x201, 0xFF, 0x32, 0x32, 0 };

        // "this dump's firmware is damaged in the way the repair tool knows": a verified build string, and its end (from
        // 0x0800C188) erased. For the flasher's input and backup checks. Up to R41 any difference there counted, so a
        // complete firmware with another mod's 1 ms report interval (two .data bytes) was called damaged (R42).
        public static bool LooksDamaged(byte[] app, int n) { return Examine(app, n).Damaged(); }
        public const string DAMAGED = "This firmware is DAMAGED: its end (from 0x0800C188) is erased, so it cannot start. A restore image that stops there does this (like the 35208-byte deco01v3-stock.hex or ic902-stock.hex from older xppen-tools). Repair the tablet first with " + TITLE + " (option 2 at start-up).";

        // ---------------------------------------------------------------- R42: edited or damaged?
        // The places other Deco 640 mods change (this tool's own patch sites), with their stock value, per verified
        // build (Layout.KNOWN order), read from both stock dumps and checked against them by the core suite. Only the
        // changed byte is listed: the immediate of a `movs`, or a compressed .data literal. Undoing the changes in a
        // code page must make that page's CRC-32 match StockRef.PAGE_CRC exactly; otherwise the page counts as changed
        // in a way this tool does not know.
        public static readonly string[] EDIT_NAMES = new string[] { "Movement Smoothing (dead band)", "ADC samples per step", "excitation pulses per step", "step poll", "step tick", "AGC delay cap", "position average at rest", "position average while moving", "USB report interval" };
        static readonly string[] EDIT_UNITS = new string[] { "", "", "", " us", " us", "", "", "", " ms" };
        public static readonly int[][] EDIT_AT = new int[][] {
            new int[] { 0x080041C4, 0x080041C6, 0x080041CA, 0x080041CE, 0x080041D2, 0x080041D6, 0x080041DA, 0x080041DE, 0x08004258, 0x08005302, 0x0800710E, 0x08005DA2, 0x08005DAC, 0x080075D6, 0x080075DA, 0x08005BDA, 0x08005BE0, 0x0800C63B, 0x0800C658 },
            new int[] { 0x080041C4, 0x080041C6, 0x080041CC, 0x080041D0, 0x080041D4, 0x080041D8, 0x080041DC, 0x08004256, 0x080052FE, 0x0800711A, 0x08005DAE, 0x08005DB8, 0x080075E2, 0x080075E6, 0x08005BE6, 0x08005BEC, 0x0800C647, 0x0800C664 } };
        public static readonly int[][] EDIT_STOCK = new int[][] {
            new int[] { 70, 25, 60, 50, 45, 40, 35, 30, 20, 45, 40, 50, 200, 30, 30, 5, 3, 3, 3 },
            new int[] { 80, 25, 60, 45, 40, 35, 30, 20, 45, 40, 50, 200, 30, 30, 5, 3, 3, 3 } };
        public static readonly int[][] EDIT_KIND = new int[][] {
            new int[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 5, 5, 6, 7, 8, 8 },
            new int[] { 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 5, 5, 6, 7, 8, 8 } };
        public const int ERASED_MIN = 16;            // erased end bytes that make a dump damaged (both real cases: 1599 / 1611)

        // compares a firmware (BASE.., n bytes) with the verified stock build its build string names
        public static FwCheck Examine(byte[] app, int n) { return Examine(app, n, KnownIndex(Build.ReadBuildString(app, n))); }
        public static FwCheck Examine(byte[] app, int n, int known) { return Examine(app, n, known, null); }
        // refEnd: the stock end (0x0800C188..end) from the user's own copy (FindEnd), or null. Without one, an end that
        // does not match END_CRC is only classified (erased or changed) and left as it is.
        public static FwCheck Examine(byte[] app, int n, int known, byte[] refEnd)
        {
            FwCheck c = new FwCheck();
            c.known = known;
            if (known < 0) return c;
            KnownBuild kb = Layout.KNOWN[known];
            c.build = kb.build;
            int cut = StockRef.CUT - BASE, end = kb.end - BASE, m = n < end ? n : end;
            c.truncated = n < end;
            byte[] s = new byte[end];
            for (int i = 0; i < end; i++) s[i] = i < m ? app[i] : (byte)0xFF;
            int[] at = EDIT_AT[known], sv = EDIT_STOCK[known], kd = EDIT_KIND[known];
            int[] now = new int[EDIT_NAMES.Length], was = new int[EDIT_NAMES.Length], chg = new int[at.Length];
            for (int k = 0; k < now.Length; k++) now[k] = -1;
            for (int j = 0; j < chg.Length; j++) chg[j] = -1;
            bool deadZero = true;
            // code pages: a page that differs only at known places is "edited"; undone, it must match its stored CRC
            long[] pc = StockRef.PAGE_CRC[known];
            for (int p = 0; p < pc.Length; p++)
            {
                int a = p * 0x400, len = a + 0x400 <= cut ? 0x400 : cut - a;
                if (a + len > m) { c.pagesMissing++; continue; }      // cut off: the truncation warning covers it (R43)
                if (Txt.Crc32(s, a, len) == pc[p]) continue;
                byte[] t = new byte[len];
                P.Copy(s, a, t, 0, len);
                int fixd = 0;
                for (int j = 0; j < at.Length; j++)
                {
                    int o = at[j] - BASE - a;
                    if (o < 0 || o >= len) continue;
                    int v = t[o] & 0xFF;
                    if (v != sv[j] && v != 0xFF) { t[o] = (byte)sv[j]; fixd++; }
                }
                if (fixd > 0 && Txt.Crc32(t, 0, len) == pc[p])
                {
                    for (int j = 0; j < at.Length; j++)
                    {
                        int o = at[j] - BASE - a;
                        if (o < 0 || o >= len) continue;
                        int v = s[a + o] & 0xFF;
                        if (v == sv[j]) continue;
                        chg[j] = v;
                        if (now[kd[j]] < 0) { now[kd[j]] = v; was[kd[j]] = sv[j]; }
                        if (kd[j] == 0 && v != 0) deadZero = false;
                    }
                    P.Copy(t, 0, s, a, len);
                    c.pagesEdited++;
                }
                else { c.pagesOther++; c.pages += (c.pagesOther > 1 ? ", " : "") + F.Hx(BASE + a, 8); }
            }
            // the end (R42: only its CRC-32 is stored). The known .data literals are edits; undone, the end must match
            // END_CRC. Otherwise it is compared with refEnd (the user's own copy) and restored from it; with no copy it is
            // only classified: 16 or more 0xFF bytes = erased (damage), else changed in an unknown way.
            int el = end - cut, have = m < end ? m : end;
            byte[] eu = new byte[el];
            P.Copy(s, cut, eu, 0, el);
            int[] ev = new int[at.Length];
            for (int j = 0; j < at.Length; j++)
            {
                ev[j] = -1;
                int o = at[j] - BASE - cut;
                if (o < 0 || o >= el || cut + o >= m) continue;
                int v = eu[o] & 0xFF;
                if (v != sv[j] && v != 0xFF) { ev[j] = v; eu[o] = (byte)sv[j]; }
            }
            c.endOk = !c.truncated && Txt.Crc32(eu, 0, el) == StockRef.END_CRC[known];
            if (c.endOk || (c.truncated && refEnd == null && Erased(s, cut, have) < ERASED_MIN))
            {
                // intact, or the part a truncated input has (the truncation warning covers the rest): keep the undone edits
                for (int j = 0; j < at.Length; j++)
                {
                    if (ev[j] < 0) continue;
                    chg[j] = ev[j];
                    if (now[kd[j]] < 0) { now[kd[j]] = ev[j]; was[kd[j]] = sv[j]; }
                }
                P.Copy(eu, 0, s, cut, el);
            }
            else if (refEnd != null)
            {
                c.tailCounted = true;
                for (int i = 0; i < el; i++)
                {
                    int o = cut + i, v = refEnd[i] & 0xFF;
                    if (o >= m) continue;                       // not in the input (truncated): the truncation warning covers it
                    int b = s[o] & 0xFF;
                    if (b == v) continue;
                    int j = -1;
                    for (int q = 0; q < at.Length; q++) if (at[q] - BASE == o) j = q;
                    if (j >= 0 && b != 0xFF && v == sv[j]) { chg[j] = b; if (now[kd[j]] < 0) { now[kd[j]] = b; was[kd[j]] = v; } }
                    else if (b == 0xFF) c.tailErased++;
                    else c.tailOther++;
                    s[o] = (byte)v;
                }
            }
            else
            {
                int ff = Erased(s, cut, have);
                if (ff >= ERASED_MIN) c.tailErased = ff;
                else c.tailOther = 1;                           // changed; how many bytes is not known without a copy
            }
            for (int k = 0; k < now.Length; k++)
            {
                if (now[k] < 0) continue;
                if (k == 0) c.edits.Add(deadZero ? "Movement Smoothing off (dead band set to 0)" : "Movement Smoothing (dead band) changed");
                else c.edits.Add(EDIT_NAMES[k] + " " + F.Str(was[k]) + " -> " + F.Str(now[k]) + EDIT_UNITS[k]);
            }
            c.editNow = now; c.deadZero = now[0] >= 0 && deadZero;
            c.how = HowLines(app, m, at, sv, kd, chg, c.edits);
            c.origin = Origin(c);
            c.work = s;
            if (!c.truncated && c.pagesOther == 0 && c.tailErased == 0 && c.tailOther == 0 && Txt.Crc32(s, 0, end) == kb.crc) c.stock = s;
            return c;
        }

        // 0xFF bytes in s[a..b)
        static int Erased(byte[] s, int a, int b) { int k = 0; for (int i = a; i < b; i++) if ((s[i] & 0xFF) == 0xFF) k++; return k; }

        // R45 (Anargon: "say what was modified, and how"): one line per known change, e.g. "ADC samples per step 45 -> 4:
        // the number in 1 instruction of the scan step setup changed (movs at 0x08005302), stock 45, found 4". A site's
        // instruction is read from the input (only its number was changed); a site from 0x0800C188 is in the compressed
        // start-up data (.data).
        static readonly string[] EDIT_WHERE = new string[] { "the dead-band setup", "the scan step setup", "the scan step setup", "the step timer setup", "the step timer setup", "the gain control (AGC) setup", "the position averaging setup", "the position averaging setup", "the USB endpoint descriptors (bInterval)" };
        static StrList HowLines(byte[] app, int m, int[] at, int[] sv, int[] kd, int[] chg, StrList edits)
        {
            StrList l = new StrList();
            int e = 0;
            for (int k = 0; k < EDIT_NAMES.Length; k++)
            {
                int n = 0, first = -1, last = -1;
                bool data = false, same = true;
                string ins = null, stockV = "", nowV = "";
                for (int j = 0; j < at.Length; j++)
                {
                    if (kd[j] != k || chg[j] < 0) continue;
                    n++; if (first < 0) first = j; last = j;
                    if (at[j] >= StockRef.CUT) data = true;
                    else
                    {
                        int o = at[j] - BASE + 1, op = o < m ? app[o] & 0xF8 : -1;
                        string ii = op == 0x20 ? "movs" : op == 0x28 ? "cmp" : "instruction";
                        if (ins == null) ins = ii; else if (!P.Eq(ins, ii)) ins = "instruction";
                    }
                    if (chg[j] != chg[first]) same = false;
                    stockV += (n > 1 ? ", " : "") + F.Str(sv[j]); nowV += (n > 1 ? ", " : "") + F.Str(chg[j]);
                }
                if (n == 0) continue;
                string what = e < edits.Count() ? edits.Get(e) : EDIT_NAMES[k];
                e++;
                string where = n == 1 ? Hx(at[first]) : n == 2 ? Hx(at[first]) + " and " + Hx(at[last]) : Hx(at[first]) + " to " + Hx(at[last]);
                string how;
                if (data) how = "the number in " + F.Str(n) + " byte" + (n > 1 ? "s" : "") + " of " + EDIT_WHERE[k] + " in the compressed start-up data changed (.data, at " + where + ")";
                else how = "the number in " + F.Str(n) + " instruction" + (n > 1 ? "s" : "") + " of " + EDIT_WHERE[k] + " changed (" + (ins != null && !P.Eq(ins, "instruction") ? ins + " " : "") + "at " + where + ")";
                if (n > 1 && same) how += ", stock " + stockV + ", all " + F.Str(chg[first]);
                else how += ", stock " + stockV + ", found " + nowV;
                l.Add(what + ": " + how);
            }
            return l;
        }

        // R45: published Deco 640 mods whose changes this tool knows, by the value each leaves (EDIT_NAMES order; -1 =
        // stock; dead band 0 = all set to 0): Mei's four hex files (February build, "ic902-binterval1-...") and Mily's
        // dump (2025-02-14_release001-Mily.bin), checked by the core suite.
        public static readonly string[] MOD_NAMES = new string[] {
            "Mei's mod ic902-binterval1-FILTERON-s4-b20-tick50-poll10.hex", "Mei's mod ic902-binterval1-FILTERON-s4-b28-tick50-poll10.hex",
            "Mei's mod ic902-binterval1-nofilter-s4-b16-tick50-poll10.hex", "Mei's mod ic902-binterval1-nofilter-s4-b20-tick50-poll10.hex",
            "Mily's firmware (Movement Smoothing off, 1 ms reports)" };
        static readonly int[][] MOD_EDITS = new int[][] {
            new int[] { -1, 4, 20, 10, 50, -1, -1, -1, 1 }, new int[] { -1, 4, 28, 10, 50, -1, -1, -1, 1 },
            new int[] { 0, 4, 16, 10, 50, -1, -1, -1, 1 }, new int[] { 0, 4, 20, 10, 50, -1, -1, -1, 1 },
            new int[] { 0, -1, -1, -1, -1, -1, -1, -1, 1 } };
        static string Origin(FwCheck c)
        {
            if (c.editNow == null || !c.Edited()) return null;
            for (int i = 0; i < MOD_EDITS.Length; i++)
            {
                bool ok = true;
                for (int k = 0; k < EDIT_NAMES.Length && ok; k++)
                {
                    int want = MOD_EDITS[i][k], got = c.editNow[k];
                    if (k == 0) ok = want < 0 ? got < 0 : c.deadZero;
                    else ok = got == want;
                }
                if (ok) return "the same changes as " + MOD_NAMES[i];
            }
            // other numbers in Mei's scheme (their file names spell them out: bInterval, filter, samples, pulses, tick, poll)
            int[] v = c.editNow;
            if (v[1] >= 0 && v[2] >= 0 && v[3] >= 0 && v[4] >= 0 && v[5] < 0 && v[6] < 0 && v[7] < 0 && (v[0] < 0 || c.deadZero))
                return "the kind of changes Mei's mods make (as if named ic902-binterval" + (v[8] >= 0 ? F.Str(v[8]) : "3") + "-" + (v[0] < 0 ? "FILTERON" : "nofilter") + "-s" + F.Str(v[1]) + "-b" + F.Str(v[2]) + "-tick" + F.Str(v[4]) + "-poll" + F.Str(v[3]) + ")";
            return null;
        }

        public static string EditList(FwCheck c)
        {
            string s = "";
            for (int i = 0; i < c.edits.Count(); i++) s += (i > 0 ? ", " : "") + c.edits.Get(i);
            return s;
        }
        // the input file was changed by another mod at known places only: undone, it is exactly stock. One line for the
        // warnings under every screen; UndoneDetail says what and how.
        public static string UndoneText(FwCheck c)
        {
            string who = c.origin != null ? "; " + c.origin : "";
            if (c.stock == null) return "Another mod changed this " + c.build + " firmware (" + EditList(c) + who + "). Set back to stock, but the file is still cut short (see the warning above).";
            return "Another mod changed this " + c.build + " firmware (" + EditList(c) + who + "). Each change was set back to its stock value and the result is exact stock (checked), so this tool builds from that.";
        }
        // R45: the full notice: whose mod, what each change was and how it was made, and how it was undone
        public static StrList UndoneDetail(FwCheck c)
        {
            StrList l = new StrList();
            l.Add("This " + c.build + " firmware was changed by another mod" + (c.origin != null ? ": " + c.origin : "") + ". What it changed, and how:");
            for (int i = 0; i < c.how.Count(); i++) l.Add("  - " + c.how.Get(i));
            if (c.stock != null) l.Add("Each of these was set back to its stock value. Then every code page matches its stored checksum and the whole firmware is exact stock " + c.build + " (CRC-32 checked), so this tool builds from that. The other mod's settings are not carried over (this tool's own settings apply).");
            else l.Add("Each of these was set back to its stock value, but the file is cut short, so the result cannot be checked as a whole (see the truncation warning).");
            return l;
        }
        // changed in a way this tool does not know (complete, not erased): it cannot rebuild stock from it
        public static string ChangedText(FwCheck c)
        {
            string what = "";
            if (c.pagesOther > 0) what = "code page" + (c.pagesOther > 1 ? "s " : " ") + c.pages;
            if (c.tailOther > 0) what += (P.Len(what) > 0 ? ", and " : "") + (c.tailCounted ? F.Str(c.tailOther) + " bytes of " : "") + "its start-up table and .data";
            if (c.Edited()) what += " (besides changes it knows: " + EditList(c) + ")";
            return "This firmware is " + c.build + " changed by another mod in places this tool does not know: " + what + ". It is complete and not erased, so the tablet may run fine, but this tool cannot get stock firmware back from it. Use a backup from before that mod, or flash stock firmware and dump the tablet again.";
        }
        // one line for a tablet backup that shows known edits
        public static string TabletText(FwCheck c)
        {
            return "The tablet runs " + c.build + " changed by another mod (" + EditList(c) + (c.origin != null ? "; " + c.origin : "") + "). Complete, not damaged.";
        }

        static int KnownIndex(string b) { for (int i = 0; i < Layout.KNOWN.Length; i++) if (P.Eq(b, Layout.KNOWN[i].build)) return i; return -1; }
        static string Hx(int a) { return F.Hx(a, 8); }
        static void SetW(byte[] d, int o, int v) { for (int k = 0; k < 4; k++) d[o + k] = (byte)((v >> (8 * k)) & 0xFF); }

        // ---------------------------------------------------------------- analysis
        public static RepairResult Analyse(byte[] full, string folder) { return Analyse(full, folder, SearchRoots(folder, folder)); }
        // roots: where FindEnd looks for the user's own copy of the firmware end (tests pass their own)
        public static RepairResult Analyse(byte[] full, string folder, string[] roots)
        {
            RepairResult r = new RepairResult();
            if (full.Length != FULL || (Machine.LE(full, 0, 4) & 0x2FFE0000) != 0x20000000)
            { r.why.Add("This is not a main.bin. Only the full 64 KB dump from `xppen-tools dump` works here."); return r; }
            byte[] app = new byte[FULL - 0x3800];
            P.Copy(full, 0x3800, app, 0, app.Length);
            r.build = Build.ReadBuildString(app, app.Length);
            r.lines.Add("Firmware build: " + (r.build != null ? r.build : "(no build string found)"));
            if (r.build != null && P.StartsWith(r.build, Build.VERSION))
            { r.why.Add("The tablet runs a build of Anargon's Tablet Firmware Mod (" + r.build + "), not stock firmware. Its code is not stored anywhere to repair it from."); return r; }
            r.known = KnownIndex(r.build);
            if (r.known < 0 && r.build == null)
            {
                // no build string (erased with the rest): recognise the build by its intact code pages
                for (int k = 0; k < Layout.KNOWN.Length && r.known < 0; k++)
                {
                    long[] pk = StockRef.PAGE_CRC[k];
                    int ok = 0;
                    for (int p = 0; p < pk.Length; p++) { int a = p * 0x400, n = a + 0x400 <= StockRef.CUT - BASE ? 0x400 : StockRef.CUT - BASE - a; if (Txt.Crc32(app, a, n) == pk[p]) ok++; }
                    if (ok * 2 >= pk.Length && Txt.Crc32(app, 0, 0x400) == pk[0]) { r.known = k; r.build = Layout.KNOWN[k].build; r.lines.Add("Build recognised by its code: " + F.Str(ok) + " of " + F.Str(pk.Length) + " code pages match " + r.build); }
                }
            }
            if (r.known < 0)
            { r.why.Add("This firmware build is not one the repair tool knows (it knows " + Layout.KNOWN[0].build + " and " + Layout.KNOWN[1].build + "). It cannot tell damaged bytes from XP-Pen's own changes."); return r; }
            KnownBuild kb = Layout.KNOWN[r.known];
            int cut = StockRef.CUT - BASE, end = kb.end - BASE;
            // R42: changes another mod made at known places (this tool's own patch sites) are not damage: they are
            // undone and reported, and only what is left counts as damaged
            FwCheck ck = Examine(app, app.Length, r.known);
            if (!ck.endOk)
            {
                // R42: no stored copy of the end. Restore it from the user's own files, or stop here.
                string[] got = new string[1];
                r.searched = roots;
                byte[] refEnd = FindEnd(r.known, roots, got);
                if (refEnd == null)
                {
                    r.lines.Add("Firmware end (" + Hx(StockRef.CUT) + "-" + Hx(kb.end - 1) + ", start-up table and .data): "
                        + (ck.Damaged() ? F.Str(ck.tailErased) + " of " + F.Str(end - cut) + " bytes erased" : "changed") + " - no copy of your own found to restore it from");
                    r.needCopy = true;
                    r.why.Add("The end of this firmware is " + (ck.Damaged() ? "erased" : "changed") + ", and no copy of your own was found to restore it from.");
                    return r;
                }
                r.endFile = got[0];
                ck = Examine(app, app.Length, r.known, refEnd);
            }
            byte[] stock = ck.work;                       // known changes undone, the end restored from the user's copy
            r.codePagesBad = ck.pagesOther;
            if (ck.Edited())
            {
                r.edits = EditList(ck);
                r.lines.Add("Changed by another mod (not damage)" + (ck.origin != null ? ", " + ck.origin : "") + ", undone:");
                for (int i = 0; i < ck.how.Count(); i++) r.lines.Add("  - " + ck.how.Get(i));
            }
            if (r.codePagesBad > 0)
            {
                r.lines.Add("Code pages that do not match " + kb.build + ": " + ck.pages);
                byte[] refApp = FindStock(kb, folder, r);
                if (refApp == null)
                {
                    r.why.Add("Code pages " + ck.pages + " differ from " + kb.build + " in ways this tool does not know, and no stock copy of " + kb.build + " was found next to this program to repair them from.");
                    return r;
                }
                for (int i = 0; i < cut; i++) if (stock[i] != refApp[i]) { stock[i] = refApp[i]; r.codeBytesFixed++; }
                r.lines.Add("Code bytes corrected from " + r.refFile + ": " + F.Str(r.codeBytesFixed));
            }
            else r.lines.Add("Code (" + Hx(BASE) + "-" + Hx(StockRef.CUT - 1) + "): intact, every page matches " + kb.build + (ck.pagesEdited > 0 ? " once the changes above are undone" : ""));
            // the end (0x0800C188..end): start-up table and .data
            r.tailBad = ck.tailErased + ck.tailOther; r.tailErased = ck.tailErased;
            r.lines.Add(r.tailBad == 0 ? "Firmware end (" + Hx(StockRef.CUT) + "-" + Hx(kb.end - 1) + ", start-up table and .data): " + (ck.Edited() ? "present, intact apart from the changes above" : "intact")
                : "Firmware end (" + Hx(StockRef.CUT) + "-" + Hx(kb.end - 1) + ", start-up table and .data): " + F.Str(r.tailBad) + " of " + F.Str(end - cut) + " bytes wrong (" + F.Str(r.tailErased) + " erased) - restored from your own copy " + r.endFile);
            if (Txt.Crc32(stock, 0, end) != kb.crc) { r.why.Add("Internal: the reconstructed firmware does not match " + kb.build + " (CRC-32). Nothing was written."); return r; }
            r.stockApp = stock;
            // the bootloader settings page
            bool pageOk = Machine.LE(full, 0x3418, 4) != -1;
            r.pageBroken = !pageOk;             // normal after any update (the bootloader keeps only the boot state); not damage
            StringBuilder pg = new StringBuilder();
            for (int i = 0; i < 7; i++) pg.Append((i > 0 ? " " : "") + F.HexDigits(Machine.LE(full, 0x3400 + 4 * i, 4) & 0xFFFFFFFFL, 8, false));
            r.lines.Add("Bootloader settings page " + Hx(PAGE) + ": " + (pageOk ? "present (" + pg.ToString() + ")" : "settings erased (" + pg.ToString() + "), normal after an update - the patcher rewrites them"));
            // the corrected 64 KB
            byte[] rep = new byte[FULL];
            P.Copy(full, rep, FULL);
            for (int i = 0x3800; i < FULL; i++) rep[i] = (byte)0xFF;
            P.Copy(stock, 0, rep, 0x3800, end);
            if (r.pageBroken) for (int i = 1; i < 7; i++) SetW(rep, 0x3400 + 4 * i, PAGE_STOCK[i]);
            r.repaired = rep;
            r.damaged = r.codePagesBad > 0 || r.tailBad > 0;
            r.ok = true;
            return r;
        }

        // a verified stock copy of build kb on this PC (the bundled deco01v3 files, or the tablet's primary dump)
        static byte[] FindStock(KnownBuild kb, string folder, RepairResult r)
        {
            string[] dirs = new string[] { ".", folder, P.ExeDir() };
            string[] names = new string[] { "deco01v3-main.bin", "ic902-main.bin", "dumps/primary-dump/ic902-main.bin", Cli.CANDIDATES[0] };
            for (int d = 0; d < dirs.Length; d++)
                for (int k = 0; k < names.Length; k++)
                {
                    string f = P.PathJoin(dirs[d], names[k]);
                    if (!P.FileExists(f)) continue;
                    byte[] b = P.ReadAllBytes(f);
                    int off = b.Length == FULL ? 0x3800 : 0, n = kb.end - BASE;
                    if (b.Length < off + n) continue;
                    if (Txt.Crc32(b, off, n) != kb.crc) continue;
                    byte[] a = new byte[n]; P.Copy(b, off, a, 0, n);
                    r.refFile = f;
                    return a;
                }
            return null;
        }

        // ---------------------------------------------------------------- R42: the end from the user's own files
        // This tool stores no XP-Pen firmware, only checksums. An erased end is restored from a file the user already
        // has: any complete dump or firmware image of the same build (xppen-tools' main.bin, this tool's backups, a
        // .hex built by this tool). Its end, with this tool's known edits undone, must match END_CRC exactly.
        public const int SEARCH_DEPTH = 3, SEARCH_FILES = 4000, HEX_MAX = 400000;
        // the dump's folder and the one above it, the output folder, the current folder, this program's folder and the
        // one above it
        public static string[] SearchRoots(string dumpDir, string outDir)
        {
            StrList l = new StrList();
            string d = Bare(dumpDir), x = Bare(P.ExeDir());
            string[] c = new string[] { d, d != null ? P.DirName(d) : null, Bare(outDir), Bare("."), x, x != null ? P.DirName(x) : null };
            for (int i = 0; i < c.Length; i++)
            {
                if (c[i] == null || P.Len(c[i]) == 0 || !P.DirExists(c[i])) continue;
                string f = c[i];
                bool dup = false;
                for (int k = 0; k < l.Count(); k++) if (P.Eq(P.Lower(l.Get(k)), P.Lower(f))) dup = true;
                if (!dup) l.Add(f);
            }
            string[] o = new string[l.Count()];
            for (int i = 0; i < o.Length; i++) o[i] = l.Get(i);
            return o;
        }
        // a full path without a trailing separator (so its parent is the folder above), or null
        static string Bare(string p)
        {
            if (p == null || P.Len(p) == 0) return null;
            string f = P.FullPath(p);
            while (P.Len(f) > 3 && (P.EndsWith(f, "\\") || P.EndsWith(f, "/"))) f = P.Sub(f, 0, P.Len(f) - 1);
            return f;
        }
        // nearest first: the files in every root, then one folder level down, and so on (SEARCH_DEPTH levels)
        public static byte[] FindEnd(int known, string[] roots, string[] got)
        {
            StrList seen = new StrList();
            int[] budget = new int[] { SEARCH_FILES };
            for (int depth = 0; depth <= SEARCH_DEPTH; depth++)
                for (int i = 0; i < roots.Length; i++)
                {
                    byte[] e = ScanDir(known, roots[i], depth, seen, budget, got);
                    if (e != null) return e;
                }
            return null;
        }
        static byte[] ScanDir(int known, string dir, int depth, StrList seen, int[] budget, string[] got)
        {
            if (budget[0] <= 0) return null;
            if (depth > 0)
            {
                string[] sub = P.ListDirs(dir);
                for (int i = 0; i < sub.Length; i++)
                {
                    byte[] e = ScanDir(known, sub[i], depth - 1, seen, budget, got);
                    if (e != null) return e;
                }
                return null;
            }
            string key = P.Lower(P.FullPath(dir));
            for (int k = 0; k < seen.Count(); k++) if (P.Eq(seen.Get(k), key)) return null;
            seen.Add(key);
            string[] files = P.ListFiles(dir);
            for (int i = 0; i < files.Length && budget[0] > 0; i++)
            {
                string lf = P.Lower(files[i]);
                if (!P.EndsWith(lf, ".bin") && !P.EndsWith(lf, ".hex")) continue;
                budget[0]--;
                byte[] e = EndOf(known, files[i]);
                if (e != null) { got[0] = files[i]; return e; }
            }
            return null;
        }
        // the end of build `known` in one file, if that file has it intact (known edits undone); else null
        public static byte[] EndOf(int known, string f)
        {
            KnownBuild kb = Layout.KNOWN[known];
            int len = kb.end - StockRef.CUT;
            byte[] e = new byte[len];
            try
            {
                long size = P.FileSize(f);
                if (P.EndsWith(P.Lower(f), ".hex"))
                {
                    if (size > HEX_MAX) return null;
                    int[] lo = new int[1];
                    byte[] d = IntelHex.Read(f, lo);
                    if (lo[0] > StockRef.CUT || lo[0] + d.Length < kb.end) return null;
                    P.Copy(d, StockRef.CUT - lo[0], e, 0, len);
                }
                else if (size == FULL) P.Copy(P.ReadAllBytes(f), StockRef.CUT - 0x08000000, e, 0, len);
                else if (size >= kb.end - BASE && size <= FULL - 0x3800) P.Copy(P.ReadAllBytes(f), StockRef.CUT - BASE, e, 0, len);
                else return null;
            }
            catch (Exception) { return null; }
            int[] at = EDIT_AT[known], sv = EDIT_STOCK[known];
            for (int j = 0; j < at.Length; j++)
            {
                int o = at[j] - StockRef.CUT;
                if (o < 0 || o >= len) continue;
                int v = e[o] & 0xFF;
                if (v != sv[j] && v != 0xFF) e[o] = (byte)sv[j];
            }
            return Txt.Crc32(e, 0, len) == StockRef.END_CRC[known] ? e : null;
        }

        // ---------------------------------------------------------------- the patcher
        // stock firmware + a start-up step reached from the firmware's own start (the word __rt_entry jumps through to
        // main): if the settings page has no settings (word +0x18, the last one, is 0xFFFFFFFF - as every update leaves
        // it), erase it, write the stock settings and then boot state 1, and restart through the firmware's own
        // DFU-entry tail (a software reset, so the bootloader stays in update mode); otherwise continue to main.
        // So after a flash of the patcher the tablet comes back once in update mode, ready for the stock firmware.
        public static byte[] Patcher(byte[] stockApp, string build)
        {
            Layout ly = Fw.Use(stockApp, build);
            int lit = Fw.A(0x0800395C), main = Fw.Ptr(0x0800395C);
            int erase = Fw.A(0x08009A1C), prog = Fw.A(0x08009A40), reset = Fw.A(0x0800B070);
            if ((main & 1) == 0 || main < BASE || main >= BASE + stockApp.Length) throw new Stop("internal: the firmware's start does not jump to its main function as expected");
            int at = (BASE + stockApp.Length + 3) & ~3;
            byte[] code = null;
            int tab = at;                                  // a flash address, so every pass assembles to the same length
            for (int pass = 0; pass < 3; pass++)
            {
                Asm a = new Asm(at);
                a.Mov32("r0", PAGE); a.Ldr("r0", "r0", 0x18); a.AddsI3("r0", "r0", 1); a.Bcc("eq", "fix");   // last settings word erased
                a.Mov32("r0", main); a.Bx("r0");
                a.Label("fix");
                a.Mov32("r0", PAGE); a.BlA(erase);
                // words 1-12 first, the boot-state word last: an interrupted write leaves word 6 erased (tried again
                // on the next start) or a complete page with boot state still erased (the bootloader treats it as 0)
                a.Mov32("r0", PAGE + 4); a.Mov32("r1", tab + 4); a.Movs("r2", 12); a.BlA(prog);
                a.Mov32("r0", PAGE); a.Mov32("r1", tab); a.Movs("r2", 1); a.BlA(prog);
                a.BWA(reset);
                a.Align4();
                code = a.Finish();
                if (pass == 2 && tab != at + code.Length) throw new Stop("internal: patcher layout did not settle");
                tab = at + code.Length;
            }
            // the last pass was assembled with the table address it computed: stable only if the length did not change
            if (tab != at + code.Length) throw new Stop("internal: patcher layout did not settle");
            byte[] img = new byte[tab + 13 * 4 - BASE];
            for (int i = 0; i < img.Length; i++) img[i] = (byte)0xFF;
            P.Copy(stockApp, img, stockApp.Length);
            P.Copy(code, 0, img, at - BASE, code.Length);
            for (int i = 0; i < 13; i++) SetW(img, tab - BASE + 4 * i, i < PAGE_STOCK.Length ? (i == 0 ? 1 : PAGE_STOCK[i]) : -1);
            SetW(img, lit - BASE, at | 1);
            return img;
        }

        // ---------------------------------------------------------------- interactive / command line
        public static int Run(string inpath, string name, string outdir, bool rt)
        {
            string folder = outdir != null ? outdir : ".";
            byte[] full = null;
            string path = inpath;
            while (true)
            {
                Screen.Step(TITLE + " - step 1/3: the tablet's main.bin");
                Screen.Para("Dump the tablet: `xppen-tools dfu --runtime-pid 2904 --pid FFF1` (skip if it is already in update mode), then `xppen-tools dump DUMPDIR`. Give the DUMPDIR/ic902-main.bin it writes (no .hex files or code-only dumps).", 4);
                P.Line("");
                if (path == null) path = P.Replace(P.Trim(Screen.Ask("    main.bin > ")), "\"", "");
                string why = null;
                if (P.Len(path) == 0) why = "Give the path of the main.bin.";
                else if (!P.FileExists(path)) why = path + " was not found.";
                else if (!P.EndsWith(P.Lower(path), ".bin")) why = path + " is not a .bin: only the main.bin that `xppen-tools dump` writes is accepted.";
                else { full = P.ReadAllBytes(path); if (full.Length != FULL) why = path + " is " + F.Str(full.Length) + " bytes; a main.bin is exactly 65536 bytes (the whole flash)."; }
                if (why == null) break;
                if (inpath != null || !P.StdinIsTty()) { P.Line("    " + why); return 1; }
                Screen.SetWarn("main.bin:", "main.bin: " + why);
                path = null;
            }
            Screen.SetWarn("main.bin:", "");
            if (name == null)
            {
                Screen.Step(TITLE + " - step 2/3: whose tablet");
                Screen.Para("A name for the files (for example the tablet owner's name).", 4);
                name = P.Trim(Screen.Ask("    Name [tablet] > "));
            }
            name = CleanName(name);
            Screen.Step(TITLE + " - step 3/3: analysis");
            RepairResult r = Analyse(full, folder, SearchRoots(P.DirName(P.FullPath(path)), folder));
            for (int i = 0; i < r.lines.Count(); i++) P.Line("    " + r.lines.Get(i));
            P.Line("");
            if (!r.ok) { if (r.needCopy) NeedCopy(path, r); else Expert(path, r); return 1; }
            if (!r.damaged && P.Len(r.edits) > 0) { Screen.Para("No damage found: stock " + r.build + " changed by another mod (" + r.edits + "). Nothing to repair." + (r.pageBroken ? " (A mostly empty settings page is normal after an update.)" : "") + " A stock firmware file is written anyway, if you want to go back to stock.", 4); }
            else if (!r.damaged) { Screen.Para("No damage found: this is stock " + r.build + ". Nothing to repair." + (r.pageBroken ? " (A mostly empty settings page is normal after an update.)" : "") + " A stock firmware file is written anyway, if you want one.", 4); }
            string patcher = P.PathJoin(folder, name + "-specific-patcher.hex"), stock = P.PathJoin(folder, P.Lower(name) + "-stock.hex");
            string repBin = P.PathJoin(folder, name + "-repaired-main.bin"), report = P.PathJoin(folder, name + "-repair-report.txt");
            P.MakeDirs(folder);
            byte[] pimg = Patcher(r.stockApp, r.build);
            Build.WriteHex(r.stockApp, BASE, stock);
            Build.WriteHex(pimg, BASE, patcher);
            P.WriteAllBytes(repBin, r.repaired);
            // check both in the digital twin; the patcher on the corrected bootloader area (its page intact, so it runs
            // as stock: the twin has no update-mode host to answer the restart)
            Build.LOG.SetFolder(folder);
            int okStock = Cli.Gate(stock, 3, false, rt);
            byte[] g = new byte[FULL]; P.Copy(r.repaired, g, FULL);
            for (int i = 0x3800; i < FULL; i++) g[i] = (byte)0xFF;
            P.Copy(pimg, 0, g, 0x3800, pimg.Length);
            string gp = P.PathJoin(folder, name + "-patcher-check.bin");
            P.WriteAllBytes(gp, g);
            int okPatch = Cli.Gate(gp, 3, false, rt);
            P.DeleteFile(gp);
            // and the repair itself: the patcher on THIS tablet's flash, its settings page as the flash leaves it, must
            // restore the settings and restart the tablet into update mode
            bool okFix;
            {
                okFix = FixWorks(full, pimg);
                P.Line("    Settings-page repair in the digital twin: " + (okFix ? "ok (" + lastFix + ")" : "FAILED"));
            }
            if (okFix == false) okPatch = 0;
            StringBuilder rep = new StringBuilder("# " + TITLE + " report, " + P.Now("stamp") + "\r\n# input: " + path + "\r\n");
            for (int i = 0; i < r.lines.Count(); i++) rep.Append(r.lines.Get(i) + "\r\n");
            rep.Append("stock check in the twin: " + (okStock == 1 ? "passed" : "FAILED") + "\r\npatcher check in the twin: " + (okPatch == 1 ? "passed" : "FAILED") + "\r\n");
            P.DeleteFile(report); P.AppendText(report, rep.ToString());
            if (okStock != 1 || okPatch != 1)
            {
                StrList L = new StrList();
                L.Add("THE REPAIR FILES FAILED THEIR DIGITAL-TWIN CHECK. DO NOT FLASH THEM.");
                L.Add("");
                L.Add("Keep " + path + " and " + report + ". Ask a project maintainer to look at them before repairing this tablet.");
                Screen.DangerAsk(L, "Press Enter to acknowledge > ");
                return 1;
            }
            P.Line("");
            P.Line("    Wrote " + patcher);
            P.Line("          " + stock);
            P.Line("          " + repBin + "   (reference only)");
            P.Line("          " + report);
            P.Line("");
            Screen.Para("To repair the tablet, flash both with `xppen-tools update` (it writes the whole file; a tool that stops at 0x0800C188 is what caused this damage):", 4);
            P.Line("      1. xppen-tools update " + BackupCapture.Quote(P.FullPath(patcher)) + " --runtime-pid 2904 --pid FFF1");
            Screen.Para("   The tablet restores its settings page, then restarts by itself in update mode and waits for step 2. (If you unplug it instead, it starts as stock next time.)", 4);
            P.Line("      2. xppen-tools update " + BackupCapture.Quote(P.FullPath(stock)) + " --runtime-pid 2904 --pid FFF1");
            Screen.Para("   Then dump it again and check the new main.bin here: it should say no damage.", 4);
            P.Line("");
            Screen.Para("If xppen-tools cannot reach the tablet at all, it has to be opened: see " + Conf.Teardown() + ".", 4);
            return 0;
        }

        // runs the patcher from reset on the tablet's own flash in the twin; true if the settings page ends up with the
        // stock settings and the tablet in update mode (boot state 1, the bootloader waiting after the patcher's
        // software reset) - or, if the bootloader's update mode timed out, running with pen reports
        public static string lastFix = "";
        public static bool FixWorks(byte[] full, byte[] pimg)
        {
            lastFix = "";
            byte[] img = new byte[FULL];
            P.Copy(full, img, FULL);
            for (int i = 0x3800; i < FULL; i++) img[i] = (byte)0xFF;
            P.Copy(pimg, 0, img, 0x3800, pimg.Length);
            for (int i = 0x3404; i < 0x3434; i++) img[i] = (byte)0xFF;       // the page as the update that flashes the patcher leaves it
            Pen pen = new Pen(1);
            pen.hasHeight = true; pen.height = 3.0;
            // from power-on: the patcher's start-up step runs before the firmware's main loop and restarts the chip, so
            // the twin's usual "boot to the main loop" never completes here - run it step by step instead
            Machine mc = new Machine(img, true, new TabletBoard(pen), false);
            img = mc.flash;                              // inspect the machine's writes, not the immutable input
            try
            {
                string boot = mc.Boot(3000000);
                // Waiting in update mode exhausts the instruction budget; an actual CPU fault cannot prove repair.
                if (!P.Eq(boot, "ok") && !P.StartsWith(boot, "step limit at pc=")) { lastFix = boot; return false; }
            }
            catch (Fault e) { lastFix = e.msg; return false; }
            catch (Stop e) { lastFix = e.msg; return false; }
            int before = mc.reports.n;
            try { mc.AttachUsb(true); for (int t = 0; t < 300; t++) mc.RunMs(1); }
            catch (Fault e) { lastFix = e.msg; return false; }
            catch (Stop e) { lastFix = e.msg; return false; }
            for (int i = 1; i < PAGE_STOCK.Length; i++) if (Machine.LE(img, 0x3400 + 4 * i, 4) != PAGE_STOCK[i]) return false;
            if ((img[0x3400] & 0xFF) == 1 && (mc.cpu.r[15] & 0xFFFFFFFFL) < 0x08003800L) { lastFix = "settings restored, tablet waiting in update mode"; return true; }
            if ((img[0x3400] & 0xFF) == 0 && mc.reports.n > before) { lastFix = "settings restored, update mode timed out, firmware running"; return true; }
            return false;
        }

        // R42: the end is erased and no copy of the user's own was found
        static void NeedCopy(string path, RepairResult r)
        {
            StrList L = new StrList();
            L.Add("NO COPY OF THIS FIRMWARE'S END WAS FOUND.");
            L.Add("");
            L.Add(r.why.Get(0));
            L.Add("This tool carries no XP-Pen firmware. It restores the end from a file you already have.");
            L.Add("");
            L.Add("What works: any complete dump or firmware file of " + r.build + " from before the damage:");
            L.Add("  - main.bin, deco01v3-main.bin or ic902-main.bin (from xppen-tools dump)");
            L.Add("  - a .hex built by this tool");
            L.Add("Put it in the folder of " + (path != null ? path : "the main.bin") + ", then run the repair again. It is checked before it is used.");
            L.Add("");
            string w = "";
            if (r.searched != null) for (int i = 0; i < r.searched.Length; i++) w += (i > 0 ? ", " : "") + r.searched[i];
            L.Add("Searched: " + w + " (and " + F.Str(SEARCH_DEPTH) + " folder levels below each).");
            L.Add("Flash nothing made from this dump until then.");
            Screen.DangerAsk(L, "Press Enter to acknowledge > ");
        }

        // the "send it to someone" page
        static void Expert(string path, RepairResult r)
        {
            StrList L = new StrList();
            L.Add("THIS DUMP CANNOT BE REPAIRED BY THIS TOOL.");
            L.Add("");
            for (int i = 0; i < r.why.Count(); i++) L.Add(r.why.Get(i));
            L.Add("");
            L.Add("Keep " + (path != null ? path : "the main.bin") + " and any older full backups. Ask a project maintainer to look at this dump, and flash nothing made from it until then.");
            L.Add("");
            L.Add("Recovery steps if the tablet cannot be reached at all: " + Conf.Teardown() + ".");
            Screen.DangerAsk(L, "Press Enter to acknowledge > ");
        }

        static string CleanName(string s)
        {
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < P.Len(s); i++)
            {
                int c = P.CharAt(s, i);
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_') b.Append((char)c);
            }
            string o = b.ToString(); return P.Len(o) > 0 ? o : "tablet";
        }
    }
}
