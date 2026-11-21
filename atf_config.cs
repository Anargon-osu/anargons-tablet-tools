// atf_config.cs - the program's settings, from a .env file (KEY=VALUE lines) next to the program or in the current
// folder; a variable set in the environment wins over the file. Every key is listed, with its default, in .env.example.
using System;
using Gd32Emu;

namespace AnargonsTabletTools
{
    public static class Conf
    {
        static StrList keys = null, vals = null;

        static void Load()
        {
            if (keys != null) return;
            keys = new StrList(); vals = new StrList();
            string[] files = new string[] { P.PathJoin(P.ExeDir(), ".env"), ".env" };
            for (int f = 0; f < files.Length; f++)
            {
                if (!P.FileExists(files[f])) continue;
                string[] lines;
                try { lines = P.Lines(P.Replace(P.Utf8(P.ReadAllBytes(files[f])), "\uFEFF", "")); } catch (Exception) { continue; }
                for (int i = 0; i < lines.Length; i++)
                {
                    string ln = P.Trim(lines[i]);
                    if (P.StartsWith(ln, "export ")) ln = P.Trim(P.Sub(ln, 7, P.Len(ln) - 7));
                    int eq = P.IndexOf(ln, "=");
                    if (P.Len(ln) == 0 || P.StartsWith(ln, "#") || eq <= 0) continue;
                    string k = P.Trim(P.Sub(ln, 0, eq)), v = P.Trim(P.Sub(ln, eq + 1, P.Len(ln) - eq - 1));
                    if (P.Len(v) >= 2 && ((P.StartsWith(v, "\"") && P.EndsWith(v, "\"")) || (P.StartsWith(v, "'") && P.EndsWith(v, "'")))) v = P.Sub(v, 1, P.Len(v) - 2);
                    bool seen = false;
                    for (int j = 0; j < keys.Count(); j++) if (P.Eq(keys.Get(j), k)) seen = true;
                    if (!seen) { keys.Add(k); vals.Add(v); }       // the file next to the program wins over the current folder's
                }
            }
        }

        // the value of KEY: the environment, else .env, else dflt (empty values count as unset)
        public static string Get(string key, string dflt)
        {
            string e = P.Env(key);
            if (e != null && P.Len(e) > 0) return e;
            Load();
            for (int i = 0; i < keys.Count(); i++) if (P.Eq(keys.Get(i), key) && P.Len(vals.Get(i)) > 0) return vals.Get(i);
            return dflt;
        }

        // ---------------------------------------------------------------- the keys (see .env.example)
        // the teardown instructions document: opening the tablet, its update-mode pad and the chip-recovery pins. Every
        // recovery instruction in the program points here. Default (Anargon, R45): the recovery folder of
        // Anargon-osu/deco640-resources ("HEAD" = its default branch).
        public const string TEARDOWN_DEFAULT = "https://github.com/Anargon-osu/deco640-resources/tree/HEAD/recovery";
        public static string TeardownUrl() { return Get("ADT_TEARDOWN_URL", TEARDOWN_DEFAULT); }
        public static string Teardown()
        {
            string u = TeardownUrl();
            return P.Len(u) > 0 ? "the teardown instructions document (" + u + ")" : "the teardown instructions document (its link is not set: ADT_TEARDOWN_URL in .env)";
        }
        // where people are sent for help with a firmware build the tool does not know
        public static string SupportUrl() { return Get("ADT_SUPPORT_URL", "https://www.discord.gg/tablet"); }
        public static string SupportWhere() { return Get("ADT_SUPPORT_WHERE", "Tablet Firmware > Support > Tablet Requests, within the \"XPpen Deco 640\" thread"); }
        // the folder for the hidden PC-side state (flash count, one-time notes); empty = the per-user default
        public static string StateDir() { string s = Get("ADT_STATE_DIR", ""); return P.Len(s) > 0 ? s : Get("ATF_STATE_DIR", ""); }
        // xppen-tools: an explicit path, else it is looked for next to this program and in the current folder
        public static string XppenTools() { return Get("ADT_XPPEN_TOOLS", ""); }
    }
}
