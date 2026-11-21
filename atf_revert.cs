// atf_revert.cs (R45) - builds of this tool, set back to stock firmware (Anargon: "It should also revert mods made BY
// this patcher").
//
// A build of this tool cannot be patched again: its hooks and literals are already changed, and its build string is
// replaced. Before R45 such a file was refused as input. Now:
//   1. Every build carries an UNDO RECORD (OwnRevert.Record, allocated just before the build options text): for every
//      byte of the input firmware the build changed, the stock value, grouped by the modification that changed it.
//      Reverting applies it, drops everything after the firmware end (the added code and tables), and accepts the result
//      only if its CRC-32 equals the one recorded at build time (and, for a verified build, Layout.KNOWN's).
//      The stored bytes are the stock value XOR the built value: the record is then no copy of firmware code, so a
//      code signature (atf_layout.cs) can never match inside it a second time.
//   2. A build made before the record existed is set back from the user's own stock copy: the firmware build its code
//      layout shows (Layout.KnownByCode), searched in the same folders as the recovery tool's end search
//      (Repair.SearchRoots: the input's folder and the one above, this folder, the program's folder and the one above,
//      3 levels deep, which covers dumps/), any 64 KB dump, application .bin or .hex of that build that is exact stock
//      once known edits of other mods are undone (Repair.Examine).
// Either way the notice says what the build changed and how (OwnRevert.Detail), like another mod's edits (atf_repair.cs).
using System;
using System.Text;
using Gd32Emu;

namespace AnargonsTabletTools
{
    public class OwnRevertResult
    {
        public byte[] stock;                  // BASE.. the input firmware the build was made from, exact (null: not possible)
        public string why = "";               // why not
        public bool fromRecord;               // set back from the build's own undo record (else from `copy`)
        public string badRecord;              // the build has an undo record that failed its checks: why (else null)
        public string copy;                   // the user's stock copy it came from
        public string options;                // the build's options text, or its version string
        public StrList how = new StrList();   // one line per modification: what and where
        public int added, addedFrom;          // bytes after the firmware end (added code and tables), and where they start
    }

    public static class OwnRevert
    {
        public const int MAGIC = 0x55544441, FORMAT = 1;              // "ADTU"
        const int GAP = 3;                                            // unchanged bytes merged into one run at most

        // a build of this tool: its build string, or the byte every build of it changes (as Build.ReadInput always checked)
        public static bool IsOwnBuild(byte[] app, int n)
        {
            string b = Build.ReadBuildString(app, n);
            return (b != null && P.StartsWith(b, Build.VERSION)) || (n > 0x190 && (app[0x18C] & 0xFF) != 0x30);
        }

        // ------------------------------------------------------------------------ the record (at build time)
        // layout (little-endian): MAGIC u32, FORMAT u8, tags u8, runs u16, firmware length u32 (from BASE), its CRC-32
        // u32; each tag: length u8 + ASCII; each run: offset u16 (from BASE), length u8, tag u8 (255 = none), then that
        // many bytes of stock ^ built; last, the CRC-32 of everything before it (u32)
        public static byte[] Record(Image im)
        {
            im.Settle();
            int n = im.orig.Length;
            if (n > 0xFFFF + 1) throw new Stop("internal: undo record: firmware longer than 64 KB");
            int[] map = new int[im.tags.Count()];
            for (int i = 0; i < map.Length; i++) map[i] = -1;
            Buf runs = new Buf();
            StrList used = new StrList();
            int nr = 0;
            for (int i = 0; i < n; )
            {
                if (im.data[i] == im.orig[i]) { i++; continue; }
                int t = im.writer[i], tg = 255;
                if (t >= 0)
                {
                    if (map[t] < 0) { map[t] = used.Count(); used.Add(im.tags.Get(t)); }
                    tg = map[t];
                }
                int s = i, e = i + 1;                                     // [s, e): a run of one tag
                while (e < n && e - s < 255)
                {
                    int k = e;
                    while (k < n && k - e < GAP && im.data[k] == im.orig[k]) k++;
                    if (k >= n || k - s >= 255 || im.data[k] == im.orig[k] || im.writer[k] != t) break;
                    e = k + 1;
                }
                runs.U16(s); runs.U8(e - s); runs.U8(tg);
                for (int k = s; k < e; k++) runs.U8((im.orig[k] ^ im.data[k]) & 0xFF);
                nr++;
                i = e;
            }
            if (used.Count() > 254 || nr > 0xFFFF) throw new Stop("internal: undo record too large");
            Buf b = new Buf();
            b.U32(MAGIC); b.U8(FORMAT); b.U8(used.Count()); b.U16(nr); b.U32(n); b.U32((int)Txt.Crc32(im.orig, 0, n));
            for (int i = 0; i < used.Count(); i++)
            {
                string s = used.Get(i);
                if (P.Len(s) > 120) s = P.Sub(s, 0, 120);
                b.U8(P.Len(s)); for (int k = 0; k < P.Len(s); k++) b.U8(P.CharAt(s, k) & 0x7F);
            }
            b.Add(runs);
            b.U32((int)Txt.Crc32(b.d, 0, b.n));
            return b.Take();
        }

        // ------------------------------------------------------------------------ reverting
        public static OwnRevertResult Revert(byte[] app, int n, string inputPath)
        {
            OwnRevertResult r = new OwnRevertResult();
            r.options = OptionsOf(app, n);
            int at = FindRecord(app, n);
            if (at >= 0 && Apply(app, n, at, r)) return r;
            // no record (a build made before R45-rc1), or one that fails its checks: the stock firmware its code was made
            // from, from the user's copy
            string what = at >= 0 ? "but " + r.why : "made before builds carried an undo record";
            if (at >= 0) r.badRecord = r.why;
            Layout lt = Layout.For(app, 0, n, null);
            string kb = lt.KnownByCode();
            int known = -1;
            for (int i = 0; i < Layout.KNOWN.Length; i++) if (kb != null && P.Eq(kb, Layout.KNOWN[i].build)) known = i;
            if (known < 0)
            {
                r.why = "This file is a build of this tool (" + r.options + "), " + what + ", and its code does not match a firmware build this tool knows, so it cannot be set back to stock. Use a dump of stock firmware (ic902-main.bin from your first backup, or deco01v3-main.bin).";
                return r;
            }
            string dir = inputPath != null ? P.DirName(P.FullPath(inputPath)) : ".";
            string[] got = new string[1];
            byte[] s = FindStock(known, Repair.SearchRoots(dir, "."), got, inputPath);
            if (s == null)
            {
                r.why = "This file is a build of this tool (" + r.options + "), " + what + ". It was made from " + Layout.KNOWN[known].build + ", but no stock copy of that firmware was found in this folder, the program's folder or their dumps (3 levels deep). Put your first backup (dumps/primary-dump/ic902-main.bin) or deco01v3-main.bin next to it, or choose it as the input.";
                return r;
            }
            r.stock = s; r.copy = got[0];
            int len = s.Length < n ? s.Length : n;
            Describe(app, n, s, null, null, len, r);
            return r;
        }

        // the build options text (Build.OptionsText, the last bytes of every R45 build), else the build string
        static string OptionsOf(byte[] app, int n)
        {
            byte[] v = Build.Encode(Build.VERSION + "_");
            for (int i = n - v.Length; i >= 0x4000; i--)
            {
                bool ok = true;
                for (int k = 0; k < v.Length && ok; k++) if (app[i + k] != v[k]) ok = false;
                if (!ok) continue;
                StringBuilder sb = new StringBuilder();
                for (int k = i; k < n && app[k] != 0 && k - i < 600; k++) { int c = app[k] & 0xFF; sb.Append(c >= 32 && c < 127 ? (char)c : '?'); }
                if (P.IndexOf(sb.ToString(), " ") > 0) return sb.ToString();   // the options text has words; the descriptor does not
            }
            string b = Build.ReadBuildString(app, n);
            return b != null ? b : "no build string";
        }

        static int FindRecord(byte[] app, int n)
        {
            for (int at = ((n - 24) & ~3); at >= 0x4000; at -= 4)
                if (Machine.LE(app, at, 4) == MAGIC && (app[at + 4] & 0xFF) == FORMAT) return at;
            return -1;
        }

        static int U16(byte[] d, int o) { return (d[o] & 0xFF) | ((d[o + 1] & 0xFF) << 8); }

        // parses and applies the record at AT; false (r.why says why) if it is not intact or the result is not the
        // firmware the build was made from
        static bool Apply(byte[] app, int n, int at, OwnRevertResult r)
        {
            int tags = app[at + 5] & 0xFF, nr = U16(app, at + 6), len = Machine.LE(app, at + 8, 4);
            long crc = Machine.LE(app, at + 12, 4) & 0xFFFFFFFFL;
            if (len <= 0x1000 || len > at) { r.why = "its undo record is damaged"; return false; }
            int p = at + 16;
            string[] names = new string[tags];
            for (int i = 0; i < tags; i++)
            {
                if (p >= n) { r.why = "its undo record is cut short"; return false; }
                int l = app[p] & 0xFF;
                if (p + 1 + l > n) { r.why = "its undo record is cut short"; return false; }
                StringBuilder sb = new StringBuilder();
                for (int k = 0; k < l; k++) sb.Append((char)(app[p + 1 + k] & 0x7F));
                names[i] = sb.ToString(); p += 1 + l;
            }
            int[] ro = new int[nr], rl = new int[nr], rt = new int[nr], rp = new int[nr];
            for (int i = 0; i < nr; i++)
            {
                if (p + 4 > n) { r.why = "its undo record is cut short"; return false; }
                ro[i] = U16(app, p); rl[i] = app[p + 2] & 0xFF; rt[i] = app[p + 3] & 0xFF; rp[i] = p + 4;
                if (rl[i] == 0 || ro[i] + rl[i] > len || rp[i] + rl[i] > n) { r.why = "its undo record is damaged"; return false; }
                p += 4 + rl[i];
            }
            if (p + 4 > n || (Machine.LE(app, p, 4) & 0xFFFFFFFFL) != Txt.Crc32(app, at, p - at)) { r.why = "its undo record is damaged (checksum)"; return false; }
            byte[] s = new byte[len];
            P.Copy(app, s, len);
            for (int i = 0; i < nr; i++) for (int k = 0; k < rl[i]; k++) s[ro[i] + k] = (byte)((app[ro[i] + k] ^ app[rp[i] + k]) & 0xFF);
            if (Txt.Crc32(s, 0, len) != crc) { r.why = "set back with its undo record it does not give the firmware it was made from (CRC-32)"; return false; }
            r.stock = s; r.fromRecord = true;
            Describe(app, n, s, names, Tagger(ro, rl, rt, len), len, r);
            return true;
        }
        // the tag of every changed byte (-1 none)
        static int[] Tagger(int[] ro, int[] rl, int[] rt, int len)
        {
            int[] t = new int[len];
            for (int i = 0; i < len; i++) t[i] = -1;
            for (int i = 0; i < ro.Length; i++) for (int k = 0; k < rl[i]; k++) t[ro[i] + k] = rt[i] == 255 ? -1 : rt[i];
            return t;
        }

        // what the build changed: per modification (names / tag) or, without a record, in total; and what it added
        static void Describe(byte[] app, int n, byte[] s, string[] names, int[] tag, int len, OwnRevertResult r)
        {
            int groups = names != null ? names.Length + 1 : 1;
            int[] bytes = new int[groups], places = new int[groups], lo = new int[groups], hi = new int[groups];
            for (int g = 0; g < groups; g++) { lo[g] = -1; hi[g] = -1; }
            int prevG = -2, prevI = -10;
            for (int i = 0; i < len; i++)
            {
                if (app[i] == s[i]) continue;
                int g = names == null ? 0 : tag[i] < 0 ? names.Length : tag[i];
                bytes[g]++;
                if (g != prevG || i > prevI + 1 + GAP) places[g]++;
                if (lo[g] < 0) lo[g] = i;
                hi[g] = i; prevG = g; prevI = i;
            }
            for (int g = 0; g < groups; g++)
            {
                if (bytes[g] == 0) continue;
                string what = names == null ? "Changes in the firmware (which modification made each is not known without an undo record)" : g < names.Length ? names[g] : "Other changes";
                string where = places[g] == 1 && lo[g] == hi[g] ? "at " + F.Hx(Build.BASE + lo[g], 8) : "between " + F.Hx(Build.BASE + lo[g], 8) + " and " + F.Hx(Build.BASE + hi[g], 8);
                bool data = Build.BASE + lo[g] >= StockRef.CUT;
                r.how.Add(what + ": " + F.Str(bytes[g]) + " byte" + (bytes[g] > 1 ? "s" : "") + " in " + F.Str(places[g]) + " place" + (places[g] > 1 ? "s" : "") + " " + where + (data ? " (the compressed start-up data)" : ""));
            }
            int end = n;
            while (end > len && (app[end - 1] & 0xFF) == 0xFF) end--;
            r.added = end - len; r.addedFrom = Build.BASE + len;
        }

        // the full notice (like Repair.UndoneDetail) and the one-line warning
        public static StrList Detail(OwnRevertResult r, string build)
        {
            StrList l = new StrList();
            l.Add("This file is a build of this tool, not stock firmware: " + r.options + ". What it changed, and how:");
            for (int i = 0; i < r.how.Count(); i++) l.Add("  - " + r.how.Get(i));
            if (r.added > 0) l.Add("  - Added code and tables after the firmware end: " + F.Str(r.added) + " bytes from " + F.Hx(r.addedFrom, 8) + " (left out)");
            if (P.IndexOf(r.options, "BOOTLOADER-") >= 0) l.Add("  - Its bootloader-area flash counter is not part of the firmware and stays on the tablet (Return to stock removes it)");
            if (r.fromRecord) l.Add("Each changed byte was set back from the undo record this build carries (the stock values, stored when it was built), and everything after the firmware end was left out. The result is exact " + (build != null ? build : "stock firmware") + " (CRC-32 checked), so this tool builds from that. The build's settings are not carried over (this tool's own settings apply).");
            else l.Add((r.badRecord != null ? "This build's undo record was not used (" + r.badRecord + ")" : "This build carries no undo record (made before R45-rc1)") + ", so the stock firmware comes from your own copy: " + r.copy + ", exact " + (build != null ? build : "stock firmware") + " (CRC-32 checked). This tool builds from that; the build's settings are not carried over.");
            return l;
        }
        public static string Line(OwnRevertResult r, string build)
        {
            return "This file is a build of this tool (" + r.options + "). It was set back to exact stock " + (build != null ? build : "firmware") + (r.fromRecord ? " with the undo record it carries" : " from your copy " + r.copy) + ", and this tool builds from that.";
        }

        // ------------------------------------------------------------------------ the user's stock copy
        static byte[] FindStock(int known, string[] roots, string[] got, string skip)
        {
            StrList seen = new StrList();
            int[] budget = new int[] { Repair.SEARCH_FILES };
            string sk = skip != null ? P.Lower(P.FullPath(skip)) : "";
            for (int depth = 0; depth <= Repair.SEARCH_DEPTH; depth++)
                for (int i = 0; i < roots.Length; i++)
                {
                    byte[] e = Scan(known, roots[i], depth, seen, budget, got, sk);
                    if (e != null) return e;
                }
            return null;
        }
        static byte[] Scan(int known, string dir, int depth, StrList seen, int[] budget, string[] got, string skip)
        {
            if (budget[0] <= 0) return null;
            if (depth > 0)
            {
                string[] sub = P.ListDirs(dir);
                for (int i = 0; i < sub.Length; i++) { byte[] e = Scan(known, sub[i], depth - 1, seen, budget, got, skip); if (e != null) return e; }
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
                if (P.Eq(P.Lower(P.FullPath(files[i])), skip)) continue;
                budget[0]--;
                byte[] e = StockOf(known, files[i]);
                if (e != null) { got[0] = files[i]; return e; }
            }
            return null;
        }
        // build `known` as exact stock from one file (another mod's known edits undone), else null
        static byte[] StockOf(int known, string f)
        {
            try
            {
                long size = P.FileSize(f);
                byte[] a;
                if (P.EndsWith(P.Lower(f), ".hex"))
                {
                    if (size > Repair.HEX_MAX) return null;
                    int[] lo = new int[1];
                    a = IntelHex.Read(f, lo);
                    if (lo[0] != Build.BASE) return null;
                }
                else if (size == Repair.FULL) { byte[] full = P.ReadAllBytes(f); a = new byte[Repair.FULL - 0x3800]; P.Copy(full, 0x3800, a, 0, a.Length); }
                else if (size >= Layout.KNOWN[known].end - Build.BASE && size <= Repair.FULL - 0x3800) a = P.ReadAllBytes(f);
                else return null;
                FwCheck c = Repair.Examine(a, a.Length, known);
                return c.stock;
            }
            catch (Exception) { return null; }
        }

        // a growing byte buffer
        class Buf
        {
            public byte[] d = new byte[1024];
            public int n = 0;
            public void U8(int v) { if (n == d.Length) { byte[] g = new byte[d.Length * 2]; P.Copy(d, g, n); d = g; } d[n++] = (byte)(v & 0xFF); }
            public void U16(int v) { U8(v); U8(v >> 8); }
            public void U32(int v) { U16(v & 0xFFFF); U16((v >> 16) & 0xFFFF); }
            public void Add(Buf o) { for (int i = 0; i < o.n; i++) U8(o.d[i]); }
            public byte[] Take() { byte[] b = new byte[n]; P.Copy(d, b, n); return b; }
        }
    }
}
