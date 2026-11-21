// atf_lab.cs (R45, after rc5 (2), 2026-11-16) - Anargon's Filter Arranger (in Measurement Tools, tool 3 of the first screen):
// the lab build and the work behind its screen.
//
// Why (Anargon, 2026-11-16): "Add all of them into the tablet as a universal option ... It should just automatically be
// mix and matchable on ram so you could, say, unload and reload filters instead of having to do over 60 flashes";
// "This is also rearrangable right? so you can rearrange the filters as you please on RAM". The precision screen
// (experiments/precision/RESULTS.md) left candidates that only the tablet can decide: Coil Linearization tables B and C,
// the hull clamp (F8), Window Scale on or off. The lab build carries every position filter; which ones run and in which
// order is a list in RAM that the PC sets over USB strings, so another combination takes seconds instead of a flash.
// For testing only: the lab build is never a release build, and Claude never flashes it (Anargon does).
//
// What runs: Window Scale inside the stock position routine (on / off only: it corrects the stock answer per coil
// window, before anything else), then the frame's position through the list, slot 1 first, up to 8 slots. A filter id
// per slot (ID_*); each filter is the same routine as in the normal builds, so the build's own order (DefaultOrder, the
// order rc5 (2) runs them in) behaves exactly like the normal build, plus about 1 us for the list. Two new filters:
// Hull Mark keeps the last 8 positions at its place in the list, Hull Clamp keeps the position between the lowest and
// highest of them on each axis (F8: nothing past where the pen was; the ALPI replay in experiments/precision/circsim.cs
// took a square corner's overshoot from 0.19-0.79 mm to 0 with no noise, still-pen spread unchanged). The usual order
// with it: ... Coil Linearization, Hull Mark, ALPI, Hull Clamp, Dynamic Anti-Chatter.
// Not in the list: Movement Smoothing and Position Interpolation (inside the stock position routine: fixed when the
// build is made) and the scan settings (Coil Hold, Frequency Hold, 12-bit Readings, Tilt Frame Skip ...), which change
// how the coils are read, not the position.
//
// When a new order starts: at the next pen loss (the search restart, and any frame with no pen), so every filter starts
// clean; filters that join then start with no history.
//
// RAM (RAM = 0x20001EE0, free: Switch Match's SW ends at 0x20001ECF, Anti-Chatter's DAC at 0x20001EDF, the string
// buffer Patches.COUNT_BUF starts at 0x20001F80; RAM survives an update and is not cleared at power-up, so each request
// carries a check):
//   +0  requested order (8 nibbles, slot 1 in bits 0-3, 0 = empty)   +4  ~requested order (the request counts only then)
//   +8  pending word (USB strings 184-199 shift a nibble in)          +12 the order in use
//   +16 requested flags, +17 ~flags, +18 flags in use: bit 0 Window Scale off, bit 1 shadow on, bit 2 cursor off
//   +19 hull head, +20 hull count, +21 / +22 shadow X / Y (int8)
//   +24 / +26 X / Y before the list (shadow)                           +28 8 x X, +44 8 x Y (the hull history)
// Shadow (flag bit 1): report bytes 10 and 11 (zero in every report of report mode 4 so far; the vendor reports are 12
// bytes) carry the position before the list minus the position sent, X and Y, as int8 in report units (1/200 mm in
// report mode 4; +-127 = +-0.635 mm, saturated). Only in report mode 4 with the pen in range. Tilt is untouched.
// USB strings (Patches.CounterString, never 200): 181 status; 182 pending low byte -> requested flags; 183 pending ->
// requested order; 184 + v shifts nibble v into the pending word. Every one answers the status (STATUS_LEN characters):
//   "ADTL1:<order in use, 8 hex><flags in use, 2 hex>:<requested order><requested flags>:<pending, 8 hex>:<valid>:
//    <default order><default flags>:<available, 4 hex>"     (one line; valid: bit 0 order, bit 1 flags)
//   available: bit k = filter id k is in this build, bit 0 = Window Scale, bit 15 = cursor off.
// Cursor off (flag bit 2, since 2026-11-17; Anargon: "typing C as a separate option, like 1-10 or whatever, turns the
// cursor off on the firmware prior to recording"): the pen's normal reports (ID 7) still carry the position, pressure
// and tilt, but say "out of range" to Windows (status bit 5 and the tip / button bits 0-3 cleared, bit 4 set: padding
// in the pen's report descriptor, which stock never sets), so the pen moves no cursor and clicks nothing while this
// program reads the pen (Lab.InRange). It works from the request at once, not at the next pen loss. A new order
// without c, d, or unplugging the tablet turns the cursor back on.
using System;
using Gd32Emu;

namespace AnargonsTabletTools
{
    public static class Lab
    {
        public const int RAM = 0x20001EE0, BYTES = 60;
        public const int REQ = 0, REQN = 4, PEND = 8, ACT = 12, REQF = 16, REQFN = 17, ACTF = 18, HEAD = 19, COUNT = 20,
            SHX = 21, SHY = 22, PREX = 24, PREY = 26, HX = 28, HY = 44, HN = 8;
        public const int F_WS_OFF = 1, F_SHADOW = 2, F_CURSOR_OFF = 4;
        // cursor off (Anargon, 2026-11-17: "typing C as a separate option, like 1-10 or whatever, turns the cursor off on the
        // firmware prior to recording"): QUIET marks a pen report sent as "out of range" while the pen is in range;
        // AVAIL_CURSOR in the status's available bits = this build has it
        public const int QUIET = 0x10, AVAIL_CURSOR = 0x8000;
        // the pen is in range: status bit 5, or QUIET (cursor off). 0xC0 (out of range) has neither
        public static bool InRange(int status) { return (status & 0x30) != 0; }
        public const int STRING = 181, STRING_FLAGS = 182, STRING_ORDER = 183, STRING_NIBBLE = 184, STRING_LAST = 199;
        public const int SLOTS = 8, STATUS_LEN = 54;
        public const int ID_SWM = 1, ID_JR = 2, ID_AI = 3, ID_CLA = 4, ID_CLB = 5, ID_CLC = 6, ID_ALPI = 7, ID_DAC = 8,
            ID_HM = 9, ID_HC = 10, ID_MAX = 10;
        public static readonly string[] TAG = new string[] { "WS", "SWM", "JR", "AI", "CL-A", "CL-B", "CL-C", "ALPI", "DAC", "HM", "HC" };
        public static readonly string[] NAME = new string[] { "Window Scale", "Switch Match", "Jump Rejection",
            "Aggressive Position Interpolation", "Coil Linearization A", "Coil Linearization B", "Coil Linearization C",
            "ALPI", "Dynamic Anti-Chatter", "Hull Mark", "Hull Clamp" };
        public static readonly string[] WHAT = new string[] {
            "per coil window, by pressure; inside the stock routine, so on / off only",
            "no step when the coils or the read length switch",
            "replaces a reading far off the pen's path (momentum model; ALPI builds normally leave it out)",
            "average of the last 4-16 positions (adds delay)",
            "the first table (rc5)",
            "relearned on the rc5 ruler lines (the default since R45)",
            "table B with ALPI's 5.6 ms delay taken out (for use before ALPI)",
            "the trained filter",
            "pulls a reading that is off the line back, only then",
            "remembers the last 8 positions at this place in the order (for Hull Clamp)",
            "keeps the position between the lowest and highest Hull Mark positions: nothing past the pen" };

        // Coil Linearization tables B (L1: code density on the three rc5 ruler lines, 6 harmonics; the product's table since
        // R45, CoilLinearization.TX / TY) and C (L2: the same with speed x 5.6 ms taken out of every pass, the wave before
        // ALPI). experiments/precision/cltables.py, 1/200 mm.
        public static readonly int[] BX = CoilLinearization.TX;
        public static readonly int[] BY = CoilLinearization.TY;
        public static readonly int[] CX = new int[] { 3, 1, -1, -3, -4, -5, -7, -8, -9, -10, -11, -13, -14, -16, -17, -18, -19, -20, -21, -22, -23, -23, -23, -23, -22, -21, -20, -19, -18, -16, -15, -13, -11, -9, -7, -4, -2, 1, 4, 7, 10, 13, 16, 19, 21, 24, 26, 27, 29, 29, 29, 29, 28, 26, 25, 23, 20, 18, 16, 13, 11, 9, 7, 5 };
        public static readonly int[] CY = new int[] { -12, -12, -13, -12, -12, -11, -11, -10, -10, -11, -12, -13, -15, -16, -17, -18, -18, -17, -16, -15, -13, -11, -9, -7, -5, -2, 1, 4, 7, 10, 12, 14, 15, 16, 17, 17, 17, 17, 18, 18, 18, 18, 18, 18, 17, 17, 16, 15, 14, 12, 11, 9, 7, 5, 2, 0, -2, -4, -6, -7, -8, -9, -10, -11 };

        static string[] Rg(params string[] r) { return r; }
        public static StrIntMap labels = null;      // the last lab build's routine addresses (Sched.Patch; for labstep.cs)

        // ---- the build's own order and what it carries (Sched.Patch, before Force)
        public static int DefaultOrder(Cfg c)
        {
            int[] ids = new int[SLOTS]; int n = 0;
            if (c.switchMatch) ids[n++] = ID_SWM;
            if (c.odd != null) ids[n++] = ID_JR;
            if (c.aggressive) ids[n++] = ID_AI;
            if (c.coilLin) ids[n++] = ID_CLA;
            if (c.apip != null) ids[n++] = ID_ALPI;
            if (c.antiChatter) ids[n++] = ID_DAC;
            return Encode(ids, n);
        }
        public static int DefaultFlags(Cfg c) { return c.winScale || c.multiCoil ? 0 : F_WS_OFF; }
        public static int Available(Cfg c)
        {
            int m = 0;
            for (int k = 1; k <= ID_MAX; k++) if (k != ID_ALPI || c.apip != null) m |= 1 << k;
            if (c.winScale && !c.multiCoil) m |= 1;
            return m | AVAIL_CURSOR;
        }
        // every arrangeable filter is built in (its routine, table, capture and reset); oddMm = Jump Rejection's margin;
        // Window Scale only where the preset has it (Build.Unavailable: the fastest hover step at ULTRA, not with Multi-coil)
        public static void Force(Cfg c, double oddMm, bool wsAvailable)
        {
            c.switchMatch = true; c.antiChatter = true; c.coilLin = true;
            if (wsAvailable && !c.multiCoil) c.winScale = true;
            if (c.odd == null) { OddCfg oc = new OddCfg(); oc.margin = (int)P.Rint(oddMm * 100); oc.maxRun = 3; c.odd = oc; }
        }

        public static int Encode(int[] ids, int n)
        {
            int w = 0;
            for (int i = n - 1; i >= 0; i--) w = (w << 4) | (ids[i] & 15);
            return w;
        }
        // the filter ids in run order (empty slots skipped, as the firmware does)
        public static int[] Decode(int w)
        {
            int n = 0; int[] t = new int[SLOTS];
            for (int i = 0; i < SLOTS; i++) { int k = (w >> (4 * i)) & 15; if (k != 0) t[n++] = k; }
            int[] o = new int[n]; for (int i = 0; i < n; i++) o[i] = t[i];
            return o;
        }

        // ---- firmware: everything below is called from the scheduler's own block (Sched.Patch), so Bl reaches it
        // "labchain", in place of the stage calls at the frame restart. Keeps r4-r7.
        public static void EmitChain(Asm a, Cfg c)
        {
            int av = Available(c);
            a.Label("labchain");
            a.Push(Rg("r4", "r5", "r6"), true);
            a.Mov32("r5", RAM); a.Mov32("r6", Sched.D_ADDR);
            a.Ldrb("r0", "r6", 7); a.CmpI("r0", 255); a.Bcc("ne", "lch_pen");
            a.Bl("labtake");                                                            // no pen: a new order may start
            a.Label("lch_pen");
            a.Ldrh("r0", "r6", 8); a.Strh("r0", "r5", PREX); a.Ldrh("r0", "r6", 10); a.Strh("r0", "r5", PREY);
            a.Ldr("r4", "r5", ACT);
            a.Label("lch_l");
            a.CmpI("r4", 0); a.BccW("eq", "lch_done");
            a.Movs("r0", 15); a.Ands("r0", "r4"); a.Lsrs("r4", "r4", 4);
            string[] fn = new string[] { null, "swm", "labjr", "labai", "clin", "labclb", "labclc", "apfilt", "dachat", "labhm", "labhc" };
            for (int k = 1; k <= ID_MAX; k++)
            {
                if ((av & (1 << k)) == 0) continue;
                a.CmpI("r0", k); a.Bcc("ne", "lch_n" + F.Str(k)); a.Bl(fn[k]); a.B("lch_l");
                a.Label("lch_n" + F.Str(k));
            }
            a.B("lch_l");                                                               // 0 or an id not in this build: skipped
            a.Label("lch_done");
            // shadow: position before the list minus the one sent, int8 each (0 with no pen)
            a.Movs("r2", 0); a.Movs("r3", 0);
            a.Ldrb("r0", "r6", 7); a.CmpI("r0", 255); a.Bcc("eq", "lch_sw");
            a.Ldrh("r2", "r5", PREX); a.Ldrh("r0", "r6", 8); a.SubsR("r2", "r2", "r0"); Sat8(a, "r2", "lch_x");
            a.Ldrh("r3", "r5", PREY); a.Ldrh("r0", "r6", 10); a.SubsR("r3", "r3", "r0"); Sat8(a, "r3", "lch_y");
            a.Label("lch_sw"); a.Strb("r2", "r5", SHX); a.Strb("r3", "r5", SHY);
            a.Pop(Rg("r4", "r5", "r6"), true);

            // "labtake": the request if it is valid, else the build's own order; uses r0-r3 only
            a.Label("labtake");
            a.Mov32("r1", RAM);
            a.Ldr("r0", "r1", REQ); a.Ldr("r2", "r1", REQN); a.Mvns("r2", "r2"); a.CmpR("r0", "r2"); a.Bcc("eq", "lt_o");
            a.Mov32("r0", c.labDefault);
            a.Label("lt_o");
            a.Ldr("r2", "r1", ACT); a.Str("r0", "r1", ACT); a.CmpR("r0", "r2"); a.Bcc("eq", "lt_same");
            // the order changed: filters that sat out start with no history (ALPI's and Aggressive Interpolation's own
            // reset needs a frame with no pen, which may not come before the next pen)
            a.Movs("r0", 0);
            a.Mov32("r2", Sched.VF_ADDR); a.Strb("r0", "r2", 0);
            if (c.apip != null) { a.Mov32("r2", Apip.ST); a.Strb("r0", "r2", 20); a.Strb("r0", "r2", 31); }
            a.Label("lt_same");
            a.Ldrb("r0", "r1", REQF); a.Ldrb("r2", "r1", REQFN); a.Eors("r2", "r0"); a.CmpI("r2", 255); a.Bcc("eq", "lt_f");
            a.Movs("r0", c.labDefaultFlags);
            a.Label("lt_f"); a.Strb("r0", "r1", ACTF);
            a.Movs("r0", 0); a.Strb("r0", "r1", HEAD); a.Strb("r0", "r1", COUNT); a.Strb("r0", "r1", SHX); a.Strb("r0", "r1", SHY);
            a.BxLr();

            // Jump Rejection and Aggressive Position Interpolation: the normal builds' inline blocks, as routines
            a.Label("labjr"); a.Push(Rg("r4"), true); Sched.EmitJumpRejection(a, c.odd); a.Pop(Rg("r4"), true);
            a.Label("labai"); a.Push(Rg("r4"), true); Sched.EmitAggressive(a); a.Pop(Rg("r4"), true);

            // "labhm": Hull Mark
            a.Label("labhm");
            a.Push(Rg("r4"), true);
            a.Mov32("r1", RAM); a.Mov32("r2", Sched.D_ADDR);
            a.Ldrb("r0", "r2", 7); a.CmpI("r0", 255); a.Bcc("ne", "hm_p");
            a.Movs("r0", 0); a.Strb("r0", "r1", HEAD); a.Strb("r0", "r1", COUNT); a.B("hm_e");
            a.Label("hm_p");
            a.Ldrb("r3", "r1", HEAD); a.Movs("r0", HN - 1); a.Ands("r3", "r0");
            a.Lsls("r4", "r3", 1); a.AddsR("r4", "r4", "r1");
            a.Ldrh("r0", "r2", 8); a.Strh("r0", "r4", HX); a.Ldrh("r0", "r2", 10); a.Strh("r0", "r4", HY);
            a.AddsI("r3", 1); a.Movs("r0", HN - 1); a.Ands("r3", "r0"); a.Strb("r3", "r1", HEAD);
            a.Ldrb("r0", "r1", COUNT); a.CmpI("r0", HN); a.Bcc("cs", "hm_e"); a.AddsI("r0", 1); a.Strb("r0", "r1", COUNT);
            a.Label("hm_e");
            a.Pop(Rg("r4"), true);

            // "labhc": Hull Clamp
            a.Label("labhc");
            a.Push(Rg("r4", "r5", "r6", "r7"), true);
            a.Mov32("r5", RAM); a.Mov32("r6", Sched.D_ADDR);
            a.Ldrb("r0", "r6", 7); a.CmpI("r0", 255); a.Bcc("eq", "hc_e");
            a.Ldrb("r7", "r5", COUNT); a.Cbz("r7", "hc_e"); a.CmpI("r7", HN); a.Bcc("ls", "hc_n"); a.Movs("r7", HN);
            a.Label("hc_n");
            for (int q = 0; q < 2; q++)
            {
                string s = q == 0 ? "x" : "y";
                a.Movw("r2", 0xFFFF); a.Movs("r3", 0); a.Movs("r1", 0);
                a.Label("hc_l" + s);
                a.Lsls("r0", "r1", 1); a.AddsR("r0", "r0", "r5"); a.Ldrh("r0", "r0", q == 0 ? HX : HY);
                a.CmpR("r0", "r2"); a.Bcc("cs", "hc_a" + s); a.Mov("r2", "r0"); a.Label("hc_a" + s);
                a.CmpR("r0", "r3"); a.Bcc("ls", "hc_b" + s); a.Mov("r3", "r0"); a.Label("hc_b" + s);
                a.AddsI("r1", 1); a.CmpR("r1", "r7"); a.Bcc("cc", "hc_l" + s);
                a.Ldrh("r0", "r6", 8 + 2 * q);
                a.CmpR("r0", "r2"); a.Bcc("cs", "hc_c" + s); a.Mov("r0", "r2"); a.Label("hc_c" + s);
                a.CmpR("r0", "r3"); a.Bcc("ls", "hc_d" + s); a.Mov("r0", "r3"); a.Label("hc_d" + s);
                a.Strh("r0", "r6", 8 + 2 * q);
            }
            a.Label("hc_e");
            a.Pop(Rg("r4", "r5", "r6", "r7"), true);

            // "labsend": in place of the pen report send (0x08009CB0): the shadow into report bytes 10 and 11, and
            // cursor off
            a.Label("labsend");
            a.Push(Rg("r0", "r1", "r2", "r3", "r4"), true);
            a.Mov32("r4", RAM);
            a.Ldrb("r0", "r4", ACTF); a.Lsls("r0", "r0", 30); a.Bcc("pl", "ls_q");
            a.Mov32("r0", Sched.REPORT_MODE); a.Ldrb("r0", "r0", 0); a.CmpI("r0", 4); a.Bcc("ne", "ls_q");
            a.Mov32("r1", Sched.REPORT); a.Movs("r2", 0); a.Movs("r3", 0);
            a.Ldrb("r0", "r1", 1); a.Lsls("r0", "r0", 26); a.Bcc("pl", "ls_w");          // status bit 5: in range
            a.Ldrb("r2", "r4", SHX); a.Ldrb("r3", "r4", SHY);
            a.Label("ls_w"); a.Strb("r2", "r1", 10); a.Strb("r3", "r1", 11);
            // cursor off (flag bit 2; from a valid request at once, else the flags in use): a normal pen report (ID 7,
            // not report mode 4) with the pen in range goes out as "out of range" for Windows, so the pen moves no
            // cursor and clicks nothing: status bits 0-3 (tip, barrel, eraser, invert) and 5 (in range) cleared,
            // bit 4 set (padding in the pen's report descriptor, never set by stock), the rest unchanged
            a.Label("ls_q");
            a.Ldrb("r0", "r4", REQF); a.Ldrb("r2", "r4", REQFN); a.Eors("r2", "r0"); a.CmpI("r2", 255); a.Bcc("eq", "ls_qf");
            a.Ldrb("r0", "r4", ACTF);
            a.Label("ls_qf");
            a.Lsls("r0", "r0", 29); a.Bcc("pl", "ls_o");                                   // bit 2
            a.Mov32("r0", Sched.REPORT_MODE); a.Ldrb("r0", "r0", 0); a.CmpI("r0", 4); a.Bcc("eq", "ls_o");
            a.Mov32("r1", Sched.REPORT);
            a.Ldrb("r0", "r1", 0); a.CmpI("r0", 7); a.Bcc("ne", "ls_o");
            a.Ldrb("r0", "r1", 1); a.Lsls("r2", "r0", 26); a.Bcc("pl", "ls_o");          // status bit 5: in range
            a.Movs("r2", 0xC0); a.Ands("r0", "r2"); a.Movs("r2", QUIET); a.Orrs("r0", "r2"); a.Strb("r0", "r1", 1);
            a.Label("ls_o");
            a.PopW(Rg("r0", "r1", "r2", "r3", "r4"), true); a.BWA(Fw.A(0x08009CB0));
        }

        // r = clamp(r, -127, 127) (int8 when stored with strb)
        static void Sat8(Asm a, string r, string l)
        {
            a.CmpI(r, 127); a.Bcc("le", l + "a"); a.Movs(r, 127); a.B(l + "b");
            a.Label(l + "a"); a.Mov("r0", r); a.AddsI("r0", 127); a.Bcc("pl", l + "b"); a.Movs(r, 0x81);
            a.Label(l + "b");
        }

        // in the search restart (boot and every pen loss), after the other filters' resets. Keeps r2 and r3.
        public static void EmitReset(Asm a)
        {
            a.Push(Rg("r2", "r3"), false); a.Bl("labtake"); a.Pop(Rg("r2", "r3"), false);
        }

        // Window Scale's lab gate (in ws_k, r1 free): flag bit 0 skips the correction
        public static void EmitWsGate(Asm a, string skip)
        {
            a.Mov32("r1", RAM + ACTF); a.Ldrb("r1", "r1", 0); a.Lsls("r1", "r1", 31); a.Bcc("mi", skip);
        }

        // the twin: does this image's Window Scale carry the lab gate (its wrapper loads RAM + ACTF)?
        public static bool WsGated(byte[] flash)
        {
            Asm t = new Asm(0x08000000); t.Mov32("r1", RAM + ACTF); byte[] pat = t.Finish();
            Layout ly = Twin.LayoutOf(flash);
            int site = ly.A(0x080083BE), o = site - 0x08000000;
            if (o < 0 || o + 4 > flash.Length) return false;
            byte[] b = new byte[] { flash[o], flash[o + 1], flash[o + 2], flash[o + 3] };
            if (!Asm.IsBl(b)) return false;
            int w = (Asm.DecBl(site, b) & ~1) - 0x08000000;
            for (int i = w; i >= 0 && i + pat.Length <= flash.Length && i < w + 200; i += 2)
            {
                bool m = true;
                for (int k = 0; k < pat.Length && m; k++) if (flash[i + k] != pat[k]) m = false;
                if (m) return true;
            }
            return false;
        }

        // USB strings 181-199 (Patches.CounterString): r4 = index, ends with r4 = the answer and a jump to send
        public static void EmitStrings(Asm a, int send, int defOrder, int defFlags, int avail)
        {
            a.Label("lab_str");
            a.Push(Rg("r5", "r6", "r7"), false);
            a.Mov32("r2", RAM);
            a.CmpI("r4", STRING_FLAGS); a.Bcc("ne", "lab_s1");
            a.Ldr("r0", "r2", PEND); a.Strb("r0", "r2", REQF); a.Mvns("r0", "r0"); a.Strb("r0", "r2", REQFN);
            a.Movs("r0", 0); a.Str("r0", "r2", PEND); a.B("lab_ans");
            a.Label("lab_s1"); a.CmpI("r4", STRING_ORDER); a.Bcc("ne", "lab_s2");
            a.Ldr("r0", "r2", PEND); a.Str("r0", "r2", REQ); a.Mvns("r0", "r0"); a.Str("r0", "r2", REQN);
            a.Movs("r0", 0); a.Str("r0", "r2", PEND); a.B("lab_ans");
            a.Label("lab_s2"); a.CmpI("r4", STRING_NIBBLE); a.Bcc("cc", "lab_ans");
            a.Ldr("r0", "r2", PEND); a.Lsls("r0", "r0", 4); a.Mov("r1", "r4"); a.SubsI("r1", STRING_NIBBLE); a.Orrs("r0", "r1");
            a.Str("r0", "r2", PEND);
            a.Label("lab_ans");
            a.Mov32("r5", Patches.COUNT_BUF);
            a.Movw("r0", 0x0300 | (2 + 2 * STATUS_LEN)); a.Strh("r0", "r5", 0); a.AddsI("r5", 2);
            Chars(a, "ADTL1:");
            a.Ldr("r1", "r2", ACT); Hex(a, 8, "h1"); a.Ldrb("r1", "r2", ACTF); a.Lsls("r1", "r1", 24); Hex(a, 2, "h2");
            Chars(a, ":");
            a.Ldr("r1", "r2", REQ); Hex(a, 8, "h3"); a.Ldrb("r1", "r2", REQF); a.Lsls("r1", "r1", 24); Hex(a, 2, "h4");
            Chars(a, ":");
            a.Ldr("r1", "r2", PEND); Hex(a, 8, "h5");
            Chars(a, ":");
            a.Movs("r3", '0');
            a.Ldr("r0", "r2", REQ); a.Ldr("r1", "r2", REQN); a.Mvns("r1", "r1"); a.CmpR("r0", "r1"); a.Bcc("ne", "lab_v1"); a.AddsI("r3", 1);
            a.Label("lab_v1");
            a.Ldrb("r0", "r2", REQF); a.Ldrb("r1", "r2", REQFN); a.Eors("r0", "r1"); a.CmpI("r0", 255); a.Bcc("ne", "lab_v2"); a.AddsI("r3", 2);
            a.Label("lab_v2"); a.Strh("r3", "r5", 0); a.AddsI("r5", 2);
            Chars(a, ":" + HexText(defOrder, 8) + HexText(defFlags, 2) + ":" + HexText(avail, 4));
            a.Mov32("r4", Patches.COUNT_BUF);
            a.Pop(Rg("r5", "r6", "r7"), false);
            a.BWA(send);
        }
        static void Chars(Asm a, string s)
        {
            for (int i = 0; i < P.Len(s); i++) { a.Movs("r0", P.CharAt(s, i)); a.Strh("r0", "r5", 0); a.AddsI("r5", 2); }
        }
        // the top n nibbles of r1 as hex characters at r5 (r0, r1, r6)
        static void Hex(Asm a, int n, string l)
        {
            a.Movs("r6", n);
            a.Label("lab_" + l);
            a.Lsrs("r0", "r1", 28); a.Lsls("r1", "r1", 4); a.CmpI("r0", 10); a.Bcc("cc", "lab_" + l + "d"); a.AddsI("r0", 7);
            a.Label("lab_" + l + "d"); a.AddsI("r0", '0'); a.Strh("r0", "r5", 0); a.AddsI("r5", 2);
            a.SubsI("r6", 1); a.Bcc("ne", "lab_" + l);
        }

        public static string HexText(int v, int n)
        {
            string s = "";
            for (int i = n - 1; i >= 0; i--) { int d = (v >> (4 * i)) & 15; s += (char)(d < 10 ? '0' + d : 'A' + d - 10); }
            return s;
        }
        public static int ParseHex(string s)
        {
            int v = 0;
            for (int i = 0; i < P.Len(s); i++)
            {
                int ch = P.CharAt(s, i), d;
                if (ch >= '0' && ch <= '9') d = ch - '0';
                else if (ch >= 'A' && ch <= 'F') d = ch - 'A' + 10;
                else if (ch >= 'a' && ch <= 'f') d = ch - 'a' + 10;
                else return -1;
                v = (v << 4) | d;
            }
            return v;
        }

        // ---- C# copies (labstep.cs compares them with the firmware)
        public class HullState { public int head = 0, count = 0; public int[] x = new int[HN], y = new int[HN]; }
        public static void HullMark(HullState h, int pen, int x, int y)
        {
            if (pen == 255) { h.head = 0; h.count = 0; return; }
            int i = h.head & (HN - 1);
            h.x[i] = x; h.y[i] = y; h.head = (i + 1) & (HN - 1);
            if (h.count < HN) h.count++;
        }
        public static int HullClamp(HullState h, int q, int pen, int v)
        {
            if (pen == 255 || h.count == 0) return v;
            int n = h.count > HN ? HN : h.count, lo = 0xFFFF, hi = 0;
            int[] t = q == 0 ? h.x : h.y;
            for (int i = 0; i < n; i++) { if (t[i] < lo) lo = t[i]; if (t[i] > hi) hi = t[i]; }
            return v < lo ? lo : (v > hi ? hi : v);
        }
        public static int Shadow(int pre, int now)
        {
            int d = pre - now;
            return d > 127 ? 127 : (d < -127 ? -127 : d);
        }

        // ---- the PC side: a parsed status
        public class Status
        {
            public int act, actF, req, reqF, pend, valid, def, defF, avail;
            public bool OrderValid() { return (valid & 1) != 0; }
            public bool FlagsValid() { return (valid & 2) != 0; }
        }
        public static Status Parse(string s)
        {
            if (s == null || P.Len(s) != STATUS_LEN || !P.StartsWith(s, "ADTL1:")) return null;
            Status t = new Status();
            t.act = ParseHex(P.Sub(s, 6, 8)); t.actF = ParseHex(P.Sub(s, 14, 2));
            t.req = ParseHex(P.Sub(s, 17, 8)); t.reqF = ParseHex(P.Sub(s, 25, 2));
            t.pend = ParseHex(P.Sub(s, 28, 8)); t.valid = ParseHex(P.Sub(s, 37, 1));
            t.def = ParseHex(P.Sub(s, 39, 8)); t.defF = ParseHex(P.Sub(s, 47, 2)); t.avail = ParseHex(P.Sub(s, 50, 4));
            if (t.actF < 0 || t.reqF < 0 || t.valid < 0 || t.defF < 0 || t.avail < 0) return null;
            return t;
        }

        // the string requests that set the order and the flags; the last answer (null: no answer)
        public static string SendOrder(LabPort port, int w)
        {
            for (int i = 7; i >= 0; i--) if (port.Get(STRING_NIBBLE + ((w >> (4 * i)) & 15)) == null) return null;
            return port.Get(STRING_ORDER);
        }
        public static string SendFlags(LabPort port, int f)
        {
            if (port.Get(STRING_NIBBLE + ((f >> 4) & 15)) == null || port.Get(STRING_NIBBLE + (f & 15)) == null) return null;
            return port.Get(STRING_FLAGS);
        }
    }

    // how the screen reaches the tablet: USB strings, or (ADT_LAB_TWIN = a lab image) the digital twin running it
    public class LabPort
    {
        public Machine mc = null; public VirtualHost host = null; public Pen pen = null;
        public string twin = null;
        public static LabPort Open()
        {
            LabPort p = new LabPort();
            string t = P.Env("ADT_LAB_TWIN");
            if (t != null && P.Len(t) > 0)
            {
                // a pen touching at a fixed place, as the twin gate's checks use (TrainCheck): tracked, so a new order
                // starts at the next pen loss exactly as on the tablet
                p.twin = t;
                p.pen = new Pen(5);
                p.pen.x = 10.3; p.pen.y = 7.6; p.pen.f0 = 505000; p.pen.Q = 300; p.pen.hasHeight = true; p.pen.height = 0; p.pen.integrate = true;
                System.Text.StringBuilder why = new System.Text.StringBuilder();
                p.mc = Twin.ScanMachine(Twin.LoadImage(t), p.pen, false, why);
                if (p.mc == null) throw new Stop("the twin did not start " + t + ": " + why.ToString());
                p.host = new VirtualHost(p.mc.usb);
                p.mc.RunMs(80);
            }
            return p;
        }
        public string Get(int index)
        {
            if (index < Lab.STRING || index > Lab.STRING_LAST) throw new Stop("Filter Arranger: string " + F.Str(index) + " is not one of its own");
            if (mc == null) { string s = P.HidIndexedString(0x28BD, 0x2904, index); return s != null && P.Len(s) > 0 ? s : null; }
            try
            {
                // a request starts from the main loop: VirtualHost runs the USB interrupt to completion at once, and
                // started inside another interrupt the emulator takes no more interrupts (R44, Cli.TwinSource.ToMainLoop)
                for (int i = 0; i < 1000000 && (mc.cpu.excDepth > 0 || mc.cpu.primask != 0); i++) mc.RunMs(0.0002);
                byte[] d = host.GetDescriptor(3, index, 255, 0x0409, 0x80);
                if (d.Length < 4 || (d[1] & 0xFF) != 3) return null;
                string r = "";
                for (int j = 2; j + 1 < d.Length && j < (d[0] & 0xFF); j += 2) r += (char)((d[j] & 0xFF) | ((d[j + 1] & 0xFF) << 8));
                return r;
            }
            catch (Fault) { return null; }
            catch (Stop) { return null; }
        }
        // the twin only: the pen leaves the tablet and comes back (a new order starts at the pen loss)
        public void PenAway()
        {
            if (mc == null) return;
            pen.present = false; mc.RunMs(60); pen.present = true; mc.RunMs(60);
        }
    }

    // ---- the Filter Arranger (Measurement Tools, atf_measure.cs): the work behind it. The screen itself
    // (FilterArranger.Run, atf_arranger.cs) only shows what these return and calls them; nothing in it talks to the tablet
    // or knows the strings.
    public static class ArrangerOps
    {
        // what an action did: ok = the tablet took it; message = a warning or why not (null: nothing to say)
        public class Result { public bool ok = false; public string message = null; }

        public const string INTRO = "Which position filters run, and in which order, on the tablet now. Kept in RAM: nothing is "
            + "flashed, and unplugging the tablet goes back to the build's own order. A new order starts when the pen next "
            + "leaves the tablet's range.";
        public const string NO_TABLET = "No tablet answered with the Filter Arranger firmware. This tool works only with that "
            + "firmware: it has every position filter built in, and the order they run in is kept in the tablet's RAM, so it "
            + "can be changed here without flashing. Build it with tool 1 and the command-line option --lab (for example: "
            + "--rate ultra --apip <training file> --lab), check it in the digital twin there, and flash it. Unplugging the "
            + "tablet always goes back to the build's own order.";
        public const string FIXED = "Fixed in this build: Movement Smoothing and Position Interpolation (inside the stock "
            + "position routine) and the scan settings (Coil Hold, Frequency Hold, 12-bit Readings ...).";
        public const string HELP = "Type the new order as filter numbers, first runs first, up to 8 (for example 1 2 5 6 7 10). "
            + "Add c to turn the cursor off (1 2 5 6 7 10 c; c alone keeps the order). "
            + "Or: w = Window Scale on / off, s = shadow bytes on / off, d = the build's own order, 0 = no filters, "
            + "Enter = look again, q = back.";
        public const string WAITING = "  (lift the pen out of range)";
        public const string NOT_IN_BUILD = "not in this firmware";
        public const string SWITCHES = "Switches (not in the order)";

        // ---- the numbered entries, laid out like the wizard's settings (Anargon: "format it like the others"): grouped
        // like the Noise-Related Patches screen, numbered in the order the filters normally run. The number the user
        // types is the entry number; the firmware's filter id is ENTRY_ID[number] (the ids stay as built).
        static readonly int[] ENTRY_ID = new int[] { 0, Lab.ID_SWM, Lab.ID_CLA, Lab.ID_CLB, Lab.ID_CLC, Lab.ID_HM, Lab.ID_ALPI,
            Lab.ID_HC, Lab.ID_JR, Lab.ID_AI, Lab.ID_DAC };
        static readonly string[] ENTRY_GROUP = new string[] { null, "Position corrections (no cursor delay)", null, null, null,
            "ALPI and Hull Clamp", null, null, "Position filters (noise against delay)", null, null };
        static readonly string[] ENTRY_WHAT = new string[] { null,
            "Matches the reading to the line the pen was following when the coils or the read length switch, so the cursor does not step there.",
            "Corrects the position inside each coil with a fixed table: the table the normal builds use.",
            "A table relearned on three ruler lines (offline, on lines it was not learned on: X 0.111 -> 0.101 mm, Y 0.058 -> 0.040 mm).",
            "Table B with ALPI's 5.6 ms delay taken out, for use before ALPI.",
            "Remembers the last 8 positions at its place in the order, for Hull Clamp.",
            "The trained tracking filter, with this build's training.",
            "Keeps the position between the lowest and highest positions Hull Mark kept, so the cursor never goes past where the pen was (the overshoot at a corner).",
            "Replaces a single reading that jumps far off the pen's path with the path's next step.",
            "Averages the last 4-16 positions, by speed.",
            "Pulls a position more than 0.10 mm off the line through the last two back toward the previous one; every other frame passes as it is." };
        static readonly string[] ENTRY_COST = new string[] { null,
            "Only while the pen moves. Uses past positions only.",
            "None: no delay.",
            "Only one Coil Linearization table at a time.",
            "Only one Coil Linearization table at a time.",
            "None on its own.",
            "No delay of its own.",
            "Needs Hull Mark earlier in the order.",
            "A real, sudden change of direction can lose its first reading. ALPI builds normally leave it out.",
            "Adds cursor delay, most when moving slowly.",
            "Up to 0.4 ms of delay, only on the frames it pulls." };
        public const string WS_NAME = "Window Scale", SHADOW_NAME = "Shadow bytes";
        public const string WS_WHAT = "Corrects the position inside each group of coils the tablet reads, by pressure. It works "
            + "inside the stock position routine, so it is on or off, not in the order.";
        public const string SHADOW_WHAT = "Report bytes 10 and 11 carry the position before the filters minus the one sent, so one "
            + "recording holds both (report mode 4 only).";

        public static int EntryCount() { return ENTRY_ID.Length - 1; }              // entries are numbered 1 .. EntryCount()
        public static string EntryGroup(int n) { return ENTRY_GROUP[n]; }           // the heading this entry starts, or null
        public static string EntryName(int n) { return Lab.NAME[ENTRY_ID[n]]; }
        public static string EntryWhat(int n) { return ENTRY_WHAT[n]; }
        public static string EntryCost(int n) { return ENTRY_COST[n]; }            // or null
        public static bool EntryInBuild(Lab.Status st, int n) { return (st.avail & (1 << ENTRY_ID[n])) != 0; }
        // "[ 1st ]" .. "[ 8th ]" = its place in the order in use, "[ OFF ]" = not in it, "[ N/A ]" = not in this firmware
        public static string EntryState(Lab.Status st, int n)
        {
            if (!EntryInBuild(st, n)) return "[ N/A ]";
            int[] ids = Lab.Decode(st.act);
            for (int i = 0; i < ids.Length; i++) if (ids[i] == ENTRY_ID[n]) return "[ " + F.Str(i + 1) + (i == 0 ? "st" : i == 1 ? "nd" : i == 2 ? "rd" : "th") + " ]";
            return "[ OFF ]";
        }
        public static bool WsInBuild(Lab.Status st) { return (st.avail & 1) != 0; }
        public static string WsState(Lab.Status st) { return !WsInBuild(st) ? "[ N/A ]" : (st.actF & Lab.F_WS_OFF) != 0 ? "[ OFF ]" : "[ ON  ]"; }
        public static string ShadowState(Lab.Status st) { return (st.actF & Lab.F_SHADOW) != 0 ? "[ ON  ]" : "[ OFF ]"; }


        // the tablet (or, with ADT_LAB_TWIN set to a lab image, the digital twin running it)
        public static LabPort Connect() { return LabPort.Open(); }
        // the tablet's state; null = no tablet with the Filter Arranger firmware answered
        public static Lab.Status Read(LabPort port) { return Lab.Parse(port.Get(Lab.STRING)); }


        public static bool OrderWaiting(Lab.Status st) { return st.OrderValid() && st.req != st.act; }
        // cursor off works at once, so it never waits for the pen
        public static bool FlagsWaiting(Lab.Status st) { return st.FlagsValid() && ((st.reqF ^ st.actF) & ~Lab.F_CURSOR_OFF) != 0; }
        public static bool CursorInBuild(Lab.Status st) { return st != null && (st.avail & Lab.AVAIL_CURSOR) != 0; }
        public static bool CursorOff(Lab.Status st) { return CursorInBuild(st) && (Cur(st) & Lab.F_CURSOR_OFF) != 0; }
        public const string CURSOR_OFF_TEXT = "off (c): the pen moves no cursor and clicks nothing";

        // "SWM > CL-A > ALPI > DAC"; an empty order: "(no filters: the stock position)"
        public static string OrderText(int w)
        {
            int[] ids = Lab.Decode(w);
            if (ids.Length == 0) return "(no filters: the stock position)";
            string s = "";
            for (int i = 0; i < ids.Length; i++) s += (i > 0 ? " > " : "") + (ids[i] <= Lab.ID_MAX ? Lab.TAG[ids[i]] : "?");
            return s;
        }
        // "Window Scale on, shadow bytes off"
        public static string FlagsText(int f, int avail)
        {
            return "Window Scale " + ((avail & 1) == 0 ? "not in this build" : (f & Lab.F_WS_OFF) != 0 ? "off" : "on")
                + ", shadow bytes " + ((f & Lab.F_SHADOW) != 0 ? "on" : "off")
                + ((avail & Lab.AVAIL_CURSOR) != 0 ? ", cursor " + ((f & Lab.F_CURSOR_OFF) != 0 ? "off" : "on") : "");
        }

        // the actions; each sends its request, and (twin only) moves the pen out of range and back so it starts
        public static Result SetOrder(LabPort port, Lab.Status st, string typed)
        {
            Result r = new Result();
            Parsed pr = ParseOrder(typed, st.avail);
            r.message = pr.why;
            if (pr.word < 0 && !pr.keep) return r;
            bool ok = true;
            if (!pr.keep)
            {
                Lab.Status chk = Lab.Parse(Lab.SendOrder(port, pr.word));
                ok = chk != null && chk.OrderValid() && chk.req == pr.word;
            }
            // c in the order: the cursor off; an order without it: on (only builds that have it)
            if (ok && CursorInBuild(st))
            {
                int f = pr.cursorOff ? Cur(st) | Lab.F_CURSOR_OFF : Cur(st) & ~Lab.F_CURSOR_OFF;
                if (f != Cur(st))
                {
                    Lab.Status cf = Lab.Parse(Lab.SendFlags(port, f));
                    ok = cf != null && cf.FlagsValid() && cf.reqF == f;
                }
            }
            if (pr.keep) { r.ok = ok; if (!ok) r.message = "The tablet did not take it (no answer). Look again and retry."; return r; }
            return Done(port, r, ok);
        }
        public static Result ToggleWindowScale(LabPort port, Lab.Status st)
        {
            Result r = new Result();
            if ((st.avail & 1) == 0) { r.message = "Window Scale is not in this build."; return r; }
            return Flags(port, r, Cur(st) ^ Lab.F_WS_OFF);
        }
        public static Result ToggleShadow(LabPort port, Lab.Status st) { return Flags(port, new Result(), Cur(st) ^ Lab.F_SHADOW); }
        public static Result UseBuildOrder(LabPort port, Lab.Status st)
        {
            Result r = new Result();
            Lab.Status chk = Lab.Parse(Lab.SendOrder(port, st.def));
            if (chk == null || !chk.OrderValid() || chk.req != st.def) return Done(port, r, false);
            return Flags(port, r, st.defF);
        }

        static int Cur(Lab.Status st) { return st.FlagsValid() ? st.reqF : st.actF; }
        static Result Flags(LabPort port, Result r, int f)
        {
            Lab.Status chk = Lab.Parse(Lab.SendFlags(port, f));
            return Done(port, r, chk != null && chk.FlagsValid() && chk.reqF == f);
        }
        static Result Done(LabPort port, Result r, bool ok)
        {
            r.ok = ok;
            if (!ok) r.message = (r.message != null ? r.message + " " : "") + "The tablet did not take it (no answer, or a different order came back). Look again and retry.";
            port.PenAway();
            return r;
        }

        // "1 2 5 6 7 10" (entry numbers) -> the order word; word -1 and the reason when it cannot run; a warning in why otherwise
        // cursorOff: c was in it; keep: c alone, the order stays as it is
        public class Parsed { public int word = -1; public string why = null; public bool cursorOff = false, keep = false; }
        static Parsed Bad(string why) { Parsed p = new Parsed(); p.why = why; return p; }
        public static Parsed ParseOrder(string typed, int avail)
        {
            // c (or C) on its own, like a filter number: the cursor off (Anargon, 2026-11-17)
            bool off = false;
            string src = " " + typed + " ", c = "";
            for (int i = 1; i + 1 < P.Len(src); i++)
            {
                int ch = P.CharAt(src, i);
                if ((ch == 'c' || ch == 'C') && Sep(P.CharAt(src, i - 1)) && Sep(P.CharAt(src, i + 1))) { off = true; c += " "; }
                else c += (char)ch;
            }
            if (off && (avail & Lab.AVAIL_CURSOR) == 0) return Bad("This firmware has no cursor-off option (c): flash the newer Filter Arranger build.");
            if (off && P.Len(P.Trim(c)) == 0) { Parsed k = new Parsed(); k.keep = true; k.cursorOff = true; return k; }
            string why = null;
            int[] ids = new int[Lab.SLOTS]; int n = 0, v = -1;
            string t = c + " ";
            for (int i = 0; i < P.Len(t); i++)
            {
                int ch = P.CharAt(t, i);
                if (ch >= '0' && ch <= '9') { v = (v < 0 ? 0 : v * 10) + (ch - '0'); if (v > 99) { return Bad("Not a filter number in this firmware."); } continue; }
                if (ch != ' ' && ch != ',' && ch != '>' && ch != '\t') { return Bad("Type filter numbers, like 1 2 6 10."); }
                if (v < 0) continue;
                if (v == 0 && n == 0 && P.Eq(P.Trim(c), "0")) { v = -1; continue; }
                if (v < 1 || v > EntryCount() || (avail & (1 << ENTRY_ID[v])) == 0) { return Bad("Filter " + F.Str(v) + " is not in this firmware."); }
                int id = ENTRY_ID[v];
                for (int k = 0; k < n; k++) if (ids[k] == id) { return Bad(Lab.NAME[id] + " is in the order twice: each filter keeps one history, so it can run once."); }
                if (n == Lab.SLOTS) { return Bad("At most " + F.Str(Lab.SLOTS) + " filters."); }
                ids[n++] = id; v = -1;
            }
            int cl = 0, hm = -1, hc = -1, ap = -1, clAt = -1;
            for (int k = 0; k < n; k++)
            {
                if (ids[k] == Lab.ID_CLA || ids[k] == Lab.ID_CLB || ids[k] == Lab.ID_CLC) { cl++; clAt = k; }
                if (ids[k] == Lab.ID_HM) hm = k;
                if (ids[k] == Lab.ID_HC) hc = k;
                if (ids[k] == Lab.ID_ALPI) ap = k;
            }
            if (cl > 1) { return Bad("Only one Coil Linearization table at a time (each corrects the same coil error)."); }
            if (hc >= 0 && (hm < 0 || hm > hc)) { return Bad("Hull Clamp needs Hull Mark earlier in the order (it clamps to the positions Hull Mark kept)."); }
            if (clAt >= 0 && ap >= 0 && clAt > ap) why = "Note: Coil Linearization after ALPI corrects the coil wave about 5-6 ms late on a moving pen (the normal builds run it before ALPI).";
            Parsed ok = new Parsed(); ok.word = Lab.Encode(ids, n); ok.why = why; ok.cursorOff = off;
            return ok;
        }
        static bool Sep(int ch) { return ch == ' ' || ch == ',' || ch == '>' || ch == '	'; }
    }
}
