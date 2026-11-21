// atf_measure.cs (R45, after rc5 (2), 2026-11-17) - tool 3 of the first screen: Anargon's Measurement Tools.
//
// Why (Anargon, 2026-11-17): the Filter Arranger "is a test suite type class, so it should not be part of the primary
// tablet patcher"; "tool 3 is the whole measurement suite"; and models should be "required to contribute to the
// measurement suite instead of having them run their own py's" (AGENTS.md rule 18). Until now every tablet measurement
// was a Python script in Claude-workarea/experiments that only its author ran; here each one is an entry anyone can run
// from the program, under the product's rules (C# 5 subset, ASCII, nothing to install, the AI DISCLOSURE box).
//
// How to add a measurement: write a class that implements IMeasurement (its screen in its own atf_*.cs file, its work
// in functions the screen calls, like ArrangerOps for the Filter Arranger), add one line to MeasurementTools.ALL, and
// give it a gate that runs it against the digital twin. A measurement reads the tablet through the normal firmware's
// cursor reports unless the user flashes something for it; it never reads USB string 200, and never writes to the
// tablet except through functions the user chose on its screen. R45-rc5 (3): Tablet signal thresholds is the one entry
// that flashes (Standard calibration's measurement firmware, then the firmware back), each time after xppen-tools'
// typed confirmation.
using Gd32Emu;

namespace AnargonsTabletTools
{
    // one entry of Measurement Tools
    public interface IMeasurement
    {
        string Name();          // "Anargon's Filter Arranger"
        string Label();         // "For testing purposes only", or null
        string What();          // one or two sentences under the name
        int Run();              // its screen; returns when the user goes back (0 = ended normally)
    }

    public static class MeasurementTools
    {
        public const string TITLE = "Anargon's Measurement Tools";
        public const string WHAT = "For testing purposes only. Tests and measurements on a real tablet, for development.";
        public const string INTRO = "Tests and measurements on a real tablet, for development and for comparing builds. Only "
            + "Tablet signal thresholds flashes the tablet (a measurement firmware, then the firmware it ran before), and only "
            + "after you confirm.";
        public static bool realtime = false;           // the twin checks paced to the simulated clock (--speed 72mhz)

        // every measurement, in the order shown (numbered from 1)
        public static readonly IMeasurement[] ALL = new IMeasurement[] { new ArrangerEntry(), new StillNoiseEntry(), new ThresholdsEntry(),
            new RulerEntry() };
        // R45 (Anargon, 2026-11-17): the Play Session Test is out of the program ("Its too risky": it scores recordings of
        // normal play); its scoring is kept for offline use only, experiments/precision/playscore.cs

        public static int Run()
        {
            while (true)
            {
                Screen.Step(TITLE);
                Screen.Para(INTRO, 4);
                P.Line("");
                for (int i = 0; i < ALL.Length; i++)
                {
                    IMeasurement m = ALL[i];
                    P.Line("    " + F.Str(i + 1) + ".  " + m.Name());
                    Screen.Para((m.Label() != null ? m.Label() + ". " : "") + m.What(), 8);
                    P.Line("");
                }
                string range = ALL.Length == 1 ? "1" : "1-" + F.Str(ALL.Length);
                string c = P.Trim(Screen.Ask("    Measurement (" + range + ", q = quit): > "));
                if (P.Eq(c, "q") || P.Eq(c, "Q") || (P.Len(c) == 0 && !P.StdinIsTty())) return 0;
                int k = -1;
                if (P.Len(c) > 0 && P.Len(c) < 4)
                {
                    k = 0;
                    for (int j = 0; j < P.Len(c); j++) { int ch = P.CharAt(c, j); if (ch < '0' || ch > '9') { k = -1; break; } k = k * 10 + (ch - '0'); }
                }
                if (k >= 1 && k <= ALL.Length) ALL[k - 1].Run();
            }
        }
    }

    // Anargon's Filter Arranger (atf_lab.cs: the lab build and ArrangerOps; atf_arranger.cs: its screen)
    class ArrangerEntry : IMeasurement
    {
        public string Name() { return FilterArranger.TITLE; }
        public string Label() { return FilterArranger.SUB; }
        public string What() { return "Rearranges the position filters in the tablet's RAM, no flashing. Needs the Filter Arranger firmware (a --lab build)."; }
        public int Run() { return FilterArranger.Run(); }
    }

    // Anargon's Still Pen Noise Test (atf_stillnoise.cs)
    class StillNoiseEntry : IMeasurement
    {
        public string Name() { return StillNoise.TITLE; }
        public string Label() { return StillNoise.SUB; }
        public string What() { return "How much the cursor shakes with the pen held still, read from the normal pen reports, no flashing. On the Filter Arranger firmware it compares filter orders."; }
        public int Run() { return StillNoise.Run(); }
    }

    // Tablet signal thresholds: Standard calibration's measuring on its own (atf_thresholds.cs)
    class ThresholdsEntry : IMeasurement
    {
        public string Name() { return ThresholdsTest.TITLE; }
        public string Label() { return ThresholdsTest.SUB; }
        public string What() { return "How high your tablet picks up the pen, saved for --calibration. Flashes a measurement firmware, then the firmware the tablet ran before."; }
        public int Run() { return ThresholdsTest.Run(); }
    }

    // Anargon's Ruler Line Test (atf_ruler.cs)
    class RulerEntry : IMeasurement
    {
        public string Name() { return RulerTest.TITLE; }
        public string Label() { return RulerTest.SUB; }
        public string What() { return "How far the cursor is from a ruler line the pen is drawn along (distortion, steps, flicks), from the normal pen reports, no flashing. On the Filter Arranger firmware it compares filter orders."; }
        public int Run() { return RulerTest.Run(); }
    }
}
