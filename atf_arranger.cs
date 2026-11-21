// atf_arranger.cs (R45, after rc5 (2), 2026-11-17) - Anargon's Filter Arranger, the screen (Measurement Tools, tool 3 of
// the first screen). Written by local models on Anargon's GPU (vibeslop swarm), checked by Claude. Layout v1 (2026-11-16):
// Devstral 24B, after OmniCoder 9B failed two rounds. Layout v2 (2026-11-17, Anargon: "format it like the others", the
// settings screens): OmniCoder 9B passed in its first round (4.3 min). Claude wrote the outline, the work behind it
// (ArrangerOps in atf_lab.cs) and the gate (17 twin cases). The screen only shows things and calls ArrangerOps; it
// never talks to the tablet itself, and every name and text comes from ArrangerOps.
using Gd32Emu;

namespace AnargonsTabletTools
{
    public static class FilterArranger
    {
        public const string TITLE = "Anargon's Filter Arranger", SUB = "For testing purposes only";

        static void Heading(string h) { P.Line("  " + h); P.Line("  " + Screen.Rule('-', P.Len(h))); P.Line(""); }

        static void Entry(string key, string name, string state, string what, string cost, string na)
        {
            P.Line("    " + Txt.PadL(key, 2) + ".  " + Txt.PadR(name + " ", 34) + state);
            Screen.Para(what, 12);
            if (cost != null) Cli.Hanging("Cost: ", cost, 12);
            if (na != null) P.Line("            (" + na + ")");
            P.Line("");
        }

        public static int Run()
        {
            LabPort port = ArrangerOps.Connect();
            string note = null;

            while (true)
            {
                Screen.Step(TITLE + " (" + SUB + ")");
                Lab.Status st = ArrangerOps.Read(port);

                if (st == null)
                {
                    Screen.Para(ArrangerOps.NO_TABLET, 4);
                    P.Line("");
                    string r = P.Trim(Screen.Ask("    Enter = look again, q = back: > "));
                    if (P.Eq(r, "q") || P.Eq(r, "Q") || (P.Len(r) == 0 && !P.StdinIsTty())) return 0;
                    continue;
                }

                Screen.Para(ArrangerOps.INTRO, 4);
                P.Line("");
                Heading("In use now");
                P.Line("    Order:         " + ArrangerOps.OrderText(st.act));
                P.Line("    Switches:      " + ArrangerOps.FlagsText(st.actF, st.avail));
                if (ArrangerOps.OrderWaiting(st)) P.Line("    Waiting:       " + ArrangerOps.OrderText(st.req) + ArrangerOps.WAITING);
                if (ArrangerOps.FlagsWaiting(st)) P.Line("    Waiting:       " + ArrangerOps.FlagsText(st.reqF, st.avail) + ArrangerOps.WAITING);
                P.Line("    Build's own:   " + ArrangerOps.OrderText(st.def));
                P.Line("");

                for (int k = 1; k <= ArrangerOps.EntryCount(); k++)
                {
                    string g = ArrangerOps.EntryGroup(k);
                    if (g != null) Heading(g);
                    Entry(F.Str(k), ArrangerOps.EntryName(k), ArrangerOps.EntryState(st, k), ArrangerOps.EntryWhat(k), ArrangerOps.EntryCost(k), ArrangerOps.EntryInBuild(st, k) ? null : ArrangerOps.NOT_IN_BUILD);
                }

                Heading(ArrangerOps.SWITCHES);
                Entry("w", ArrangerOps.WS_NAME, ArrangerOps.WsState(st), ArrangerOps.WS_WHAT, null, ArrangerOps.WsInBuild(st) ? null : ArrangerOps.NOT_IN_BUILD);
                Entry("s", ArrangerOps.SHADOW_NAME, ArrangerOps.ShadowState(st), ArrangerOps.SHADOW_WHAT, null, null);

                Screen.Para(ArrangerOps.FIXED, 4);
                P.Line("");
                if (note != null) { Screen.Para(note, 4); P.Line(""); note = null; }

                Screen.Para(ArrangerOps.HELP, 4);

                string c = P.Trim(Screen.Ask("    > "));
                if (P.Len(c) == 0)
                {
                    if (!P.StdinIsTty()) return 0;
                    continue;
                }
                if (P.Eq(c, "q") || P.Eq(c, "Q")) return 0;

                ArrangerOps.Result res;

                if (P.Eq(c, "w") || P.Eq(c, "W"))
                {
                    res = ArrangerOps.ToggleWindowScale(port, st);
                }
                else if (P.Eq(c, "s") || P.Eq(c, "S"))
                {
                    res = ArrangerOps.ToggleShadow(port, st);
                }
                else if (P.Eq(c, "d") || P.Eq(c, "D"))
                {
                    res = ArrangerOps.UseBuildOrder(port, st);
                }
                else
                {
                    res = ArrangerOps.SetOrder(port, st, c);
                }

                note = res.message;
            }
        }
    }
}
