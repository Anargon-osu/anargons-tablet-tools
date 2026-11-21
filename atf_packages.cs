// atf_packages.cs - R44: the folders this program keeps next to itself, and the patcher-package selector.
//
// A patcher package is C# source that this program compiles and runs in its own process. It is trusted code, not a
// sandbox: it runs with the user's full access. The guards (CLAUDE.md, R42 note on rev36b-rc's loader):
//   - C# only: a package holds package.ini, C# files under src/ and an optional README; anything else (DLLs, EXEs,
//     scripts) refuses the whole package. Listing packages never runs their code. Since 2026-11-17 one folder around
//     the whole package is taken off and operating-system leftovers (__MACOSX, .DS_Store) are left out (AddAll).
//   - No PATH lookups: the compiler is the one beside the running .NET Framework (Windows' own csc.exe). The .NET 8
//     build has none and says so; it never falls back to `dotnet` from PATH.
//   - Typed approval of the exact content: the first run of a package, and the first run after any change to it,
//     needs "I know what I am doing." typed. Approval is kept by SHA-256 in the hidden state file.
//   - Packages are not signed. A package can bypass this tool's twin gate; the docs ask package authors to flash
//     only through Cli.Gate and Cli.FlashSteps.
using System;
using System.IO;
using System.Text;
using Gd32Emu;

namespace AnargonsTabletTools
{
    // R44 (Anargon): folders next to the program, made at the start of every interactive run
    public static class Folders
    {
        public const string IMPORT = "import", EXPORT = "export", PACKAGES = "patcherpkgs", DUMPS = "dumps";
        public static readonly string[] ALL = new string[] { PACKAGES, IMPORT, EXPORT, DUMPS };
        public static string Of(string name) { return P.PathJoin(P.ExeDir(), name); }
        public static void Make() { for (int i = 0; i < ALL.Length; i++) P.MakeDirs(Of(ALL[i])); }
    }

    public class PatcherPackage
    {
        public string path, shown, name, entry, revision = "", hash;
        public StrList names = new StrList();            // relative paths, forward slashes
        public System.Collections.Generic.List<byte[]> data = new System.Collections.Generic.List<byte[]>();
    }

    public static class Packages
    {
        public const string BUILTIN = "Anargon's Tablet Tools";
        public const string EXT = ".patcherpkg";
        public const int MAX_FILES = 512, MAX_BYTES = 16 * 1024 * 1024;
        public const string TRUST_PHRASE = "I know what I am doing.";

        // ------------------------------------------------------------------ reading a package (no code runs here)
        static void Add(PatcherPackage p, string name, byte[] b, ref long total)
        {
            if (P.Len(name) == 0 || name.StartsWith("/") || name.IndexOf('\\') >= 0 || name.IndexOf(':') >= 0 || name.IndexOf("//") >= 0)
                throw new IOException("unsafe file path " + name);
            foreach (char c in name) if (c < 33 && c != ' ' || c > 126) throw new IOException("file names must be plain ASCII: " + name);
            string[] parts = name.Split('/');
            foreach (string part in parts)
                if (!SafePart(part)) throw new IOException("unsafe file path " + name);
            string low = name.ToLowerInvariant();
            bool ok = low == "package.ini" || low == "readme.txt" || low == "readme.md" || (low.StartsWith("src/") && low.EndsWith(".cs"));
            if (!ok) throw new IOException("only C# source is accepted, and " + name + " is not (allowed: package.ini, src/*.cs, README)");
            for (int i = 0; i < p.names.Count(); i++)
                if (p.names.Get(i).ToLowerInvariant() == low) throw new IOException("the file " + name + " is in the package twice");
            if (p.names.Count() >= MAX_FILES) throw new IOException("more than " + MAX_FILES + " files");
            total += b.Length;
            if (total > MAX_BYTES) throw new IOException("bigger than 16 MB");
            p.names.Add(name); p.data.Add(b);
        }
        static bool SafePart(string part) { return !(part == "." || part == ".." || part.Length == 0 || part.EndsWith(" ") || part.EndsWith(".")); }

        // R45 (2026-11-17, Anargon: "check if the logic for custom made patcherpkg imports ... work"): every file of a
        // package goes through here. Two real-world ZIPs were refused: a ZIP of the package folder itself (Windows'
        // "Compress", tar or .NET with the folder: every file under "MyPatcher/"; Windows' "Extract All" of such a ZIP
        // gives the same extra folder), and a ZIP made on a Mac (__MACOSX/, .DS_Store). Now one folder around the whole
        // package is taken off when package.ini is not at the top, and the leftovers operating systems add are left out:
        // they are not code, are never compiled and are not part of the content hash. Every other file is checked by Add.
        static void AddAll(PatcherPackage p, StrList names, System.Collections.ArrayList data)
        {
            string wrap = Wrapper(names);
            long total = 0;
            for (int i = 0; i < names.Count(); i++)
            {
                string n = names.Get(i);
                if (Leftover(n)) continue;
                Add(p, wrap != null ? n.Substring(wrap.Length) : n, (byte[])data[i], ref total);
            }
        }
        static bool Leftover(string name)
        {
            string low = name.ToLowerInvariant();
            if (low.StartsWith("__macosx/")) return true;
            string last = low.Substring(low.LastIndexOf('/') + 1);
            return last == ".ds_store" || last == "thumbs.db" || last == "desktop.ini";
        }
        // the one folder every file is in ("MyPatcher/"), when package.ini is not at the top; null if there is none
        static string Wrapper(StrList names)
        {
            string w = null;
            for (int i = 0; i < names.Count(); i++)
            {
                string n = names.Get(i);
                if (Leftover(n)) continue;
                if (n.ToLowerInvariant() == "package.ini") return null;
                int s = n.IndexOf('/');
                if (s <= 0) return null;
                string top = n.Substring(0, s + 1);
                if (w == null) w = top;
                else if (top != w) return null;
            }
            if (w == null) return null;
            string part = w.Substring(0, w.Length - 1);
            if (!SafePart(part) || part.IndexOf(':') >= 0) return null;       // then Add refuses the full names
            foreach (char c in part) if (c < 32 || c > 126) return null;
            return w;
        }

        static int U16(byte[] b, int o) { if (o < 0 || o + 2 > b.Length) throw new IOException("damaged ZIP file"); return b[o] | (b[o + 1] << 8); }
        static long U32(byte[] b, int o) { if (o < 0 || o + 4 > b.Length) throw new IOException("damaged ZIP file"); return (long)((uint)b[o] | ((uint)b[o + 1] << 8) | ((uint)b[o + 2] << 16) | ((uint)b[o + 3] << 24)); }

        // a .patcherpkg is an ordinary ZIP; this reads stored and deflated entries with what .NET Framework has built in
        static void ReadZip(PatcherPackage p, byte[] z)
        {
            int eocd = -1;
            for (int i = z.Length - 22; i >= 0 && i >= z.Length - 22 - 65535; i--)
                if (z[i] == 0x50 && z[i + 1] == 0x4B && z[i + 2] == 5 && z[i + 3] == 6) { eocd = i; break; }
            if (eocd < 0) throw new IOException("not a ZIP file");
            int count = U16(z, eocd + 10);
            long cd = U32(z, eocd + 16);
            if (count == 0xFFFF || cd == 0xFFFFFFFFL) throw new IOException("ZIP64 packages are not supported");
            if (count > MAX_FILES) throw new IOException("more than " + MAX_FILES + " files");
            long total = 0; int at = (int)cd;
            StrList names = new StrList(); System.Collections.ArrayList data = new System.Collections.ArrayList();
            for (int k = 0; k < count; k++)
            {
                if (U32(z, at) != 0x02014B50L) throw new IOException("damaged ZIP directory");
                int flags = U16(z, at + 8), method = U16(z, at + 10);
                long crc = U32(z, at + 16), csize = U32(z, at + 20), usize = U32(z, at + 24);
                int nlen = U16(z, at + 28), xlen = U16(z, at + 30), clen = U16(z, at + 32);
                long local = U32(z, at + 42);
                if (at + 46 + nlen > z.Length) throw new IOException("damaged ZIP directory");
                // Windows PowerShell 5.1 Compress-Archive writes "src\X.cs": read '\' as the folder mark, then check as usual
                string name = Encoding.ASCII.GetString(z, at + 46, nlen).Replace('\\', '/');
                at += 46 + nlen + xlen + clen;
                if (name.EndsWith("/")) { if (usize != 0) throw new IOException("damaged ZIP directory"); continue; }   // a folder entry
                if ((flags & 1) != 0) throw new IOException("encrypted ZIP entries are not supported");
                total += usize;
                if (usize > MAX_BYTES || csize > z.Length || total > MAX_BYTES) throw new IOException("bigger than 16 MB");
                if (U32(z, (int)local) != 0x04034B50L) throw new IOException("damaged ZIP file");
                int dat = (int)local + 30 + U16(z, (int)local + 26) + U16(z, (int)local + 28);
                if (dat < 0 || dat + csize > z.Length) throw new IOException("damaged ZIP file");
                byte[] b = new byte[usize];
                if (method == 0) { if (csize != usize) throw new IOException("damaged ZIP entry " + name); Array.Copy(z, dat, b, 0, (int)usize); }
                else if (method == 8)
                {
                    using (System.IO.Compression.DeflateStream ds = new System.IO.Compression.DeflateStream(new MemoryStream(z, dat, (int)csize), System.IO.Compression.CompressionMode.Decompress))
                    {
                        int got = 0, n;
                        while (got < b.Length && (n = ds.Read(b, got, b.Length - got)) > 0) got += n;
                        if (got != b.Length || ds.ReadByte() >= 0) throw new IOException("damaged ZIP entry " + name);
                    }
                }
                else throw new IOException("ZIP compression method " + method + " is not supported (use normal ZIP)");
                if (Txt.Crc32(b, 0, b.Length) != crc) throw new IOException("damaged ZIP entry " + name + " (checksum)");
                names.Add(name); data.Add(b);
            }
            AddAll(p, names, data);
        }

        static void ReadFolder(PatcherPackage p, string dir)
        {
            long total = 0;
            string root = Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            if (files.Length > MAX_FILES) throw new IOException("more than " + MAX_FILES + " files");
            StrList names = new StrList(); System.Collections.ArrayList data = new System.Collections.ArrayList();
            foreach (string f in files)
            {
                if ((File.GetAttributes(f) & FileAttributes.ReparsePoint) != 0) throw new IOException("links are not accepted: " + f.Substring(root.Length));
                long len = new FileInfo(f).Length;
                total += len;
                if (len > MAX_BYTES || total > MAX_BYTES) throw new IOException("bigger than 16 MB");
                names.Add(f.Substring(root.Length).Replace('\\', '/')); data.Add(File.ReadAllBytes(f));
            }
            AddAll(p, names, data);
        }

        static string Value(string ini, string key, bool need)
        {
            foreach (string raw in ini.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq > 0 && !line.StartsWith("#") && line.Substring(0, eq).Trim() == key) return line.Substring(eq + 1).Trim();
            }
            if (need) throw new IOException("package.ini has no " + key + "= line");
            return "";
        }

        // the package at path (a .patcherpkg file or a folder), checked; throws IOException with the reason
        public static PatcherPackage Inspect(string path)
        {
            PatcherPackage p = new PatcherPackage();
            p.path = path; p.shown = P.PathJoin(Folders.PACKAGES, Path.GetFileName(path.TrimEnd('\\', '/')));
            if (Directory.Exists(path)) ReadFolder(p, path);
            else
            {
                if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("a .zip is not read as a package: if it is one, rename it to " + Path.GetFileNameWithoutExtension(path) + EXT
                        + " (a patcher package is a ZIP with that ending, as an osu! .osz is)");
                if (!path.EndsWith(EXT, StringComparison.OrdinalIgnoreCase)) throw new IOException("only " + EXT + " files and package folders with C# source are accepted");
                if (new FileInfo(path).Length > MAX_BYTES) throw new IOException("bigger than 16 MB");
                ReadZip(p, File.ReadAllBytes(path));
            }
            int ini = -1; bool src = false;
            for (int i = 0; i < p.names.Count(); i++)
            {
                if (p.names.Get(i).ToLowerInvariant() == "package.ini") ini = i;
                if (p.names.Get(i).ToLowerInvariant().StartsWith("src/")) src = true;
            }
            if (ini < 0 || !src) throw new IOException("a package needs package.ini and C# files under src/");
            string text = Encoding.ASCII.GetString(p.data[ini]);
            if (Value(text, "format", true) != "1") throw new IOException("unsupported package format (this tool reads format=1)");
            p.name = Value(text, "name", true); p.entry = Value(text, "entry", true); p.revision = Value(text, "revision", false);
            if (p.name.Length == 0 || p.name.Length > 60) throw new IOException("the package name must be 1-60 characters");
            foreach (char c in p.name) if (c < 32 || c > 126) throw new IOException("the package name must be plain ASCII");
            if (p.name.Equals(BUILTIN, StringComparison.OrdinalIgnoreCase)) throw new IOException("a package cannot use the built-in tool's name");
            if (p.entry.Length == 0 || p.entry.StartsWith(".") || p.entry.EndsWith(".")) throw new IOException("invalid entry class " + p.entry);
            foreach (char c in p.entry) if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || c == '.')) throw new IOException("invalid entry class " + p.entry);
            p.hash = Hash(p);
            return p;
        }

        // SHA-256 over every file's name, length and bytes in name order: the same content gives the same hash as a
        // ZIP or as a folder, and any change to any file gives another
        public static string Hash(PatcherPackage p)
        {
            int n = p.names.Count();
            int[] order = new int[n]; for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, delegate(int a, int b) { return string.CompareOrdinal(p.names.Get(a), p.names.Get(b)); });
            MemoryStream ms = new MemoryStream();
            for (int k = 0; k < n; k++)
            {
                byte[] head = Encoding.ASCII.GetBytes(p.names.Get(order[k]) + "\n" + p.data[order[k]].Length + "\n");
                ms.Write(head, 0, head.Length); ms.Write(p.data[order[k]], 0, p.data[order[k]].Length);
            }
            byte[] all = ms.ToArray();
            return Sha256.Hex(Sha256.Hash(all, 0, all.Length));
        }

        public static System.Collections.Generic.List<PatcherPackage> Scan(string dir, StrList skipped)
        {
            System.Collections.Generic.List<PatcherPackage> found = new System.Collections.Generic.List<PatcherPackage>();
            if (!Directory.Exists(dir)) return found;
            string[] entries = Directory.GetFileSystemEntries(dir);
            Array.Sort(entries, StringComparer.OrdinalIgnoreCase);
            foreach (string e in entries)
            {
                try { found.Add(Inspect(e)); }
                catch (Exception x) { skipped.Add("Skipped " + P.PathJoin(Folders.PACKAGES, Path.GetFileName(e)) + ": " + x.Message + "."); }
            }
            return found;
        }

        // ------------------------------------------------------------------ the selector (step 0b)
        // Returns -1 for this program's own tool, else the package's exit code. Shown only when the folder has
        // something in it, so most people never see it.
        public static int Select(string dir)
        {
            while (true)
            {
                StrList skipped = new StrList();
                System.Collections.Generic.List<PatcherPackage> found = Scan(dir, skipped);
                if (found.Count == 0 && skipped.Count() == 0) return -1;
                StrList saved = Screen.warnings; Screen.warnings = new StrList();
                for (int i = 0; i < skipped.Count(); i++) Screen.Warn(skipped.Get(i));
                Screen.Step("Choose the patcher:");
                P.Line("    1.  " + BUILTIN + " (built in)");
                P.Line("");
                for (int i = 0; i < found.Count; i++)
                {
                    P.Line("    " + F.Str(i + 2) + ".  " + found[i].name + (found[i].revision.Length > 0 ? " (revision " + found[i].revision + ")" : ""));
                    P.Line("        " + found[i].shown);
                    P.Line("");
                }
                Screen.Para("Patcher packages are C# code that this tool compiles and runs. They run with your account's full access, so only run packages from people you trust.", 4);
                P.Line("");
                string a;
                try { a = P.Trim(Screen.Ask("    Patcher (Enter = 1): > ")); }
                finally { Screen.warnings = saved; }
                if (P.Len(a) == 0 || a == "1") return -1;
                int k = -1;
                for (int i = 0; i < found.Count; i++) if (a == F.Str(i + 2)) k = i;
                if (k < 0) continue;
                PatcherPackage pk = found[k];
                if (!Trusted(pk)) continue;
                int r = Run(pk);
                if (r != int.MinValue) return r;
                Screen.Ask("    Press Enter to go back: > ");
            }
        }

        static bool Trusted(PatcherPackage p)
        {
            if (Store.PackageTrusted(p.hash)) return true;
            StrList L = new StrList();
            L.Add("PATCHER PACKAGE: " + p.name);
            L.Add("");
            L.Add("This package is C# code. This tool compiles it and runs it with your account's full access to this PC. This tool cannot check what it does, and it can flash your tablet without this tool's checks.");
            L.Add("");
            L.Add("Only run packages from people you trust. This is asked again whenever the package changes.");
            L.Add("");
            L.Add("File: " + p.shown);
            L.Add("SHA-256: " + p.hash);
            string ans = Screen.DangerAsk(L, "Type \"" + TRUST_PHRASE + "\" to run it, or press Enter to go back > ");
            if (!P.Eq(P.Trim(ans), TRUST_PHRASE)) return false;
            Store.TrustPackage(p.hash);
            return true;
        }

        // the compiler that ships with the running .NET Framework (never one from PATH); null on .NET 8
        public static string Compiler()
        {
            string c = Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "csc.exe");
            return File.Exists(c) ? c : null;
        }

        // compiles and runs the package; int.MinValue = it did not run (the reason is printed)
        public static int Run(PatcherPackage chosen)
        {
            string work = Path.Combine(Path.GetTempPath(), "AnargonsPatcher-" + Guid.NewGuid().ToString("N"));
            ResolveEventHandler resolve = null;
            try
            {
                PatcherPackage p = Inspect(chosen.path);          // read again: run exactly what was approved
                if (p.hash != chosen.hash) { P.Line("    " + p.shown + " changed after it was chosen. Nothing was run."); return int.MinValue; }
                string csc = Compiler();
                string host = typeof(Cli).Assembly.Location;
                if (csc == null || P.Len(host) == 0)
                {
                    P.Line("    Patcher packages need the Windows build of this tool (build.cmd): this build has no C# compiler beside it.");
                    return int.MinValue;
                }
                Directory.CreateDirectory(work);
                StringBuilder rsp = new StringBuilder();
                rsp.Append("/nologo /target:library /langversion:5 /optimize+ /out:\"" + Path.Combine(work, "package.dll") + "\" /r:\"" + host + "\"\n");
                for (int i = 0; i < p.names.Count(); i++)
                {
                    if (!p.names.Get(i).ToLowerInvariant().EndsWith(".cs")) continue;
                    string f = Path.Combine(work, p.names.Get(i).Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(f));
                    File.WriteAllBytes(f, p.data[i]);
                    rsp.Append("\"" + f + "\"\n");
                }
                File.WriteAllText(Path.Combine(work, "files.rsp"), rsp.ToString());
                P.Line("");
                P.Line("    Compiling " + p.name + " with Windows' C# compiler...");
                string[] r = P.RunProcess(csc, "@\"" + Path.Combine(work, "files.rsp") + "\"");
                if (r[0] != "0" || !File.Exists(Path.Combine(work, "package.dll")))
                {
                    P.Line("");
                    P.Line("    " + p.name + " did not compile (C# 5, see the messages above). Nothing was run.");
                    return int.MinValue;
                }
                System.Reflection.Assembly hostAsm = typeof(Cli).Assembly;
                resolve = delegate(object s, ResolveEventArgs e) { return new System.Reflection.AssemblyName(e.Name).Name == hostAsm.GetName().Name ? hostAsm : null; };
                AppDomain.CurrentDomain.AssemblyResolve += resolve;
                System.Reflection.Assembly a = System.Reflection.Assembly.Load(File.ReadAllBytes(Path.Combine(work, "package.dll")));
                Type t = a.GetType(p.entry, false);
                System.Reflection.MethodInfo m = t == null ? null : t.GetMethod("Run", new Type[] { typeof(string[]) });
                if (m == null || !m.IsStatic || !m.IsPublic || m.ReturnType != typeof(int))
                {
                    P.Line("    " + p.name + ": its entry " + p.entry + " needs public static int Run(string[] args). Nothing was run.");
                    return int.MinValue;
                }
                try { return (int)m.Invoke(null, new object[] { new string[0] }); }
                catch (System.Reflection.TargetInvocationException e)
                {
                    Exception inner = e.InnerException != null ? e.InnerException : e;
                    if (inner is Stop) throw (Stop)inner;            // input ended, as in the built-in tool
                    P.Line(""); P.Line("    " + p.name + " stopped with an error: " + inner.Message);
                    return 1;
                }
            }
            catch (IOException e) { P.Line("    " + chosen.shown + ": " + e.Message + ". Nothing was run."); return int.MinValue; }
            finally
            {
                if (resolve != null) AppDomain.CurrentDomain.AssemblyResolve -= resolve;
                try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch (Exception) { }
            }
        }
    }
}
