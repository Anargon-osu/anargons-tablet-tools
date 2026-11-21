// atf_switchmatch.cs (R45-rc4) - Switch Match: no position step when the reading conditions change.
//
// Why (Anargon, rc4: "the stepping of the coils are too noticable if you draw 45* lines"; "the cursor also jumps when
// switching between hover states"). Each reading drives the pen with a short burst and then listens. The pen is still
// ringing from the last burst when the next one starts, so the five lines of a window are not read alike: on Anargon's
// tablet, fast stage, pen still (rev-output/hw45/drive, slotfit.py) the first line read came out 44 % low on X and
// the next four -5, +3, +7 and +8 %. The pattern depends on the burst and listen lengths (the hover step), on the
// drive frequency step and on the pen's frequency (pressure). So the position carries a bias that depends on which
// coil window is read, at which step and drive step, and it changes all at once when one of those changes:
//   - drags at 45 degrees (rev-output/hw45/drag/drag45.csv): at a coil-window switch X jumped 0.24 mm (median,
//     90 % 0.46 mm), Y 0.09 mm, against 0.04 mm between two frames elsewhere;
//   - hover lifts (rev-output/hw45/rc4test/lifts-rc4.csv): fast stage -> Steady Hover X 0.67 mm, back 0.48 mm.
// The bias itself is smooth inside a window; only the switch makes it a step. Warming the pen with an extra reading
// first would cost 76 us a frame (over ULTRA's budget) and still leave the gradient.
//
// What: at a switch the new reading is compared with a straight line through the last 16 frames' positions (past
// data only: nothing is placed ahead of the pen), the difference is taken off and refined over the first 4 frames,
// then the correction fades out as the pen moves away (gone after half a coil, by the farthest distance reached so
// noise does not fade it) and with time (gone after 500 ms). It is left out when the line is fast (over 150 mm/s on
// that axis) or bends (the second difference of its quarter means over 0.18 mm), where a straight line would not
// predict the pen well, and capped at 1.5 mm. Offline (experiments/noise45/switchblend.py, stepblend.py, this exact
// integer arithmetic): drag switches X 0.036 / Y 0.040 mm (median), fast -> Steady Hover X 0.05 mm, the other step
// changes 0.02-0.05 mm; 62 of 64 simulated flicks untouched (the other two up to 0.15 mm more off for a moment).
//
// rc4 (1) turned it off: with the pen still between two coils the window flips, and each match turned a few frames of
// noise into an offset lasting up to 500 ms (drift). rc4 (2) turns it on again with two gates: a coil-window switch
// is matched only while the pen moves at 15 mm/s or more, and only when that axis did not switch in the 16 frames
// before (the flips in the history would also fake a slope); drive-step and ladder-step switches need only the
// second. A simulated still pen with a flipping window then comes out the same as without Switch Match.
//
// Firmware: start_x (each frame's X fine scan) copies the reading conditions: X and Y window centres (C[6], C[7]),
// the drive step (C[3]) and the ladder step (VBX+8, or the hover stage VB[0] without the ladder). "swm" runs in the
// frame restart before Jump Rejection and ALPI, on D+8 / D+10 (the frame's position, report units). Time is counted
// in 25 us ticks from the per-step frame periods (a table, TickTable). RAM at SW (free: stock and every other block
// leave 0x20001070-0x20001F7F unused; the ALPI training ring and read-out end at 0x20001DF3):
//   +0 n (frames of history, 0..16), +1 head (next history slot), +2 unit shift (0 = 1/200 mm, report mode 4;
//   1 = 1/100 mm), +3 1 = a new reading this frame (set by start_x), +4..7 this reading's X window, Y window, drive
//   step, step; +8..11 the last reading's; +12 time now (ticks, u16); +14 / +15 X / Y refinement count (0..M while
//   refining, M afterwards, 255 = no correction).
//   +16 X / +48 Y: +0 A (sum of the 16 positions), +4 S (slope, Q12 units per tick), +8 Ts (sum of the 16 times,
//   relative to the switch), +12 acc (sum of 16 x (line - reading) over the refinement frames), +16 o0 (the
//   correction, s16), +18 the reading at the switch, +20 the farthest distance from it, +22 the switch time.
//   +80: 16 history slots of 8 bytes: time, X out, Y out.
namespace AnargonsTabletTools
{
    public static class SwitchMatch
    {
        public const int SW = 0x20001E00, BYTES = 208;
        public const int N = 16, M = 4, TICK_US = 25;
        public const int VG4 = 3;       // speed: 4 |slope| <= 3 (1/200 mm per 25 us tick = 150 mm/s)
        public const int AG = 144;      // bend: |Q0 - Q1 - Q2 + Q3| (sums of 4) <= 144 (0.18 mm)
        public const int OM = 300;      // cap 1.5 mm
        public const int D0 = 508;      // gone after half a coil (2.54 mm)
        public const int TF = 20000;    // gone after 500 ms
        // rc4 (2): a coil-window switch is matched only with the pen moving at 15 mm/s or more (10 x 4 |slope| >= 3)
        // and no other switch of that axis in the 16 frames before it; drive-step and ladder-step switches need only
        // the second. Each axis block's +24 counts the frames since its last switch (up to 255).
        public const int VMIN10 = 10, STAB = 24;
        static string[] Rg(params string[] r) { return r; }

        // per step (ladder) or per hover stage: frame period in ticks
        public static byte[] TickTable(int[] periodsUs)
        {
            byte[] o = new byte[2 * periodsUs.Length + (periodsUs.Length % 2 == 1 ? 2 : 0)];
            for (int i = 0; i < periodsUs.Length; i++)
            {
                int t = (periodsUs[i] + TICK_US / 2) / TICK_US;
                if (t < 1) t = 1;
                if (t > 1000) t = 1000;
                o[2 * i] = (byte)(t & 0xFF); o[2 * i + 1] = (byte)(t >> 8);
            }
            return o;
        }

        // in start_x (after its subs r1, #156; r2 is free there, r0 is set after): the reading conditions
        public static void EmitCapture(Asm a, bool ladder)
        {
            a.Push(Rg("r3"), false);
            a.Mov32("r2", Sched.C_ADDR); a.Mov32("r3", SW);
            a.Ldrb("r0", "r2", 6); a.Strb("r0", "r3", 4);
            a.Ldrb("r0", "r2", 7); a.Strb("r0", "r3", 5);
            a.Ldrb("r0", "r2", 3); a.Strb("r0", "r3", 6);
            if (ladder) { a.Mov32("r2", Sched.VBX_ADDR); a.Ldrb("r0", "r2", 8); }
            else { a.Mov32("r2", Sched.VB_ADDR); a.Ldrb("r0", "r2", 0); }
            a.Strb("r0", "r3", 7);
            a.Movs("r0", 1); a.Strb("r0", "r3", 3);
            a.Pop(Rg("r3"), false);
        }

        // in the search restart (boot and every pen loss): start over (also clears power-up RAM)
        public static void EmitReset(Asm a)
        {
            a.Mov32("r1", SW); a.Movs("r0", 0);
            a.Strb("r0", "r1", 0); a.Strb("r0", "r1", 1); a.Strb("r0", "r1", 3);
            a.Movs("r0", 255); a.Strb("r0", "r1", 14); a.Strb("r0", "r1", 15);
            a.Movs("r0", 0);                                                      // rc4 (2): frames since each axis's last switch
            a.Mov32("r1", SW + 16 + STAB); a.Strb("r0", "r1", 0);
            a.Mov32("r1", SW + 48 + STAB); a.Strb("r0", "r1", 0);
        }

        // the routine, called from the frame restart (keeps r4-r7)
        public static void Emit(Asm a, int tickTab, int nTicks)
        {
            a.Label("swm");
            a.Push(Rg("r4", "r5", "r6", "r7"), true);
            a.Mov32("r7", SW); a.Mov32("r6", Sched.D_ADDR);
            a.Ldrb("r0", "r6", 7); a.CmpI("r0", 255); a.Bcc("ne", "sw_pen");
            a.Movs("r0", 0); a.Strb("r0", "r7", 0); a.Strb("r0", "r7", 3);
            a.Movs("r0", 255); a.Strb("r0", "r7", 14); a.Strb("r0", "r7", 15);
            a.BW("sw_end");
            a.Label("sw_pen");
            a.Ldrb("r0", "r7", 3); a.CmpI("r0", 0); a.BccW("eq", "sw_end");      // no new reading this frame
            a.Movs("r0", 0); a.Strb("r0", "r7", 3);
            // the report's unit; another unit than the history's starts over
            a.Mov32("r1", Sched.REPORT_MODE); a.Ldrb("r1", "r1", 0); a.Movs("r2", 0); a.CmpI("r1", 4); a.Bcc("eq", "sw_u"); a.Movs("r2", 1);
            a.Label("sw_u"); a.Ldrb("r0", "r7", 2); a.CmpR("r0", "r2"); a.Bcc("eq", "sw_u2");
            a.Strb("r2", "r7", 2); a.Movs("r0", 0); a.Strb("r0", "r7", 0);
            a.Movs("r0", 255); a.Strb("r0", "r7", 14); a.Strb("r0", "r7", 15);
            a.Label("sw_u2");
            // time now += this reading's frame period
            a.Ldrb("r0", "r7", 7); a.CmpI("r0", nTicks - 1); a.Bcc("ls", "sw_t"); a.Movs("r0", nTicks - 1);
            a.Label("sw_t"); a.Lsls("r0", "r0", 1); a.Mov32("r1", tickTab); a.AddsR("r1", "r1", "r0"); a.Ldrh("r0", "r1", 0);
            a.Ldrh("r1", "r7", 12); a.AddsR("r1", "r1", "r0"); a.Strh("r1", "r7", 12);
            // r5: bit 0 X switched, bit 1 Y switched (drive step or ladder step: both)
            a.Movs("r5", 0);
            a.Ldrb("r0", "r7", 0); a.Cbz("r0", "sw_ns");
            a.Ldrb("r0", "r7", 6); a.Ldrb("r1", "r7", 10); a.CmpR("r0", "r1"); a.Bcc("ne", "sw_b");
            a.Ldrb("r0", "r7", 7); a.Ldrb("r1", "r7", 11); a.CmpR("r0", "r1"); a.Bcc("ne", "sw_b");
            a.Ldrb("r0", "r7", 4); a.Ldrb("r1", "r7", 8); a.CmpR("r0", "r1"); a.Bcc("eq", "sw_nx"); a.AddsI("r5", 1);
            a.Label("sw_nx"); a.Ldrb("r0", "r7", 5); a.Ldrb("r1", "r7", 9); a.CmpR("r0", "r1"); a.Bcc("eq", "sw_ns"); a.AddsI("r5", 2);
            a.B("sw_ns");
            a.Label("sw_b"); a.Movs("r5", 7);                                   // bit 2: a drive-step or ladder-step switch
            a.Label("sw_ns");
            a.Ldr("r0", "r7", 4); a.Str("r0", "r7", 8);
            EmitAxis(a, 0); EmitAxis(a, 1);
            // the history slot's time; next slot; n up to 16
            a.Ldrb("r2", "r7", 1); a.Movs("r1", 15); a.Ands("r2", "r1"); a.Lsls("r1", "r2", 3); a.AddsR("r1", "r1", "r7"); a.AddsI("r1", 80);
            a.Ldrh("r0", "r7", 12); a.Strh("r0", "r1", 0);
            a.AddsI("r2", 1); a.Movs("r1", 15); a.Ands("r2", "r1"); a.Strb("r2", "r7", 1);
            a.Ldrb("r0", "r7", 0); a.CmpI("r0", N); a.Bcc("cs", "sw_end"); a.AddsI("r0", 1); a.Strb("r0", "r7", 0);
            a.Label("sw_end");
            a.Pop(Rg("r4", "r5", "r6", "r7"), true);
        }

        // one axis (q 0 = X, 1 = Y). In: r7 = SW, r5 = switch bits. Uses r4 = the reading, r6 = the axis block.
        static void EmitAxis(Asm a, int q)
        {
            string s = q == 0 ? "x" : "y";
            int blk = SW + 16 + 32 * q, kb = 14 + q, hp = 2 + 2 * q;
            a.Mov32("r6", blk);
            a.Mov32("r0", Sched.D_ADDR); a.Ldrh("r4", "r0", 8 + 2 * q);
            a.Ldrb("r1", "r6", STAB);                                             // frames since this axis's last switch
            a.Movs("r0", 1 << q); a.Tst("r5", "r0"); a.Bcc("ne", "sw_s" + s);
            a.CmpI("r1", 255); a.Bcc("eq", "sw_c0" + s); a.AddsI("r1", 1); a.Strb("r1", "r6", STAB);
            a.Label("sw_c0" + s); a.BW("sw_r" + s);
            // a switch: the last correction ends here; a new one needs a full history, no other switch of this axis
            // in it (a still pen between two coils flips the window back and forth), and a slow, straight line
            a.Label("sw_s" + s);
            a.Movs("r0", 0); a.Strb("r0", "r6", STAB);
            a.Movs("r0", 255); a.Strb("r0", "r7", kb); a.Movs("r0", 0); a.Strh("r0", "r6", 16);
            a.CmpI("r1", N); a.BccW("cc", "sw_r" + s);
            a.Ldrb("r0", "r7", 0); a.CmpI("r0", N); a.BccW("cc", "sw_r" + s);
            a.Push(Rg("r4", "r5", "r6"), false); a.SubSp(32);                     // sp+0 Q0..Q3, sp+16 T0..T3
            a.Mov32("r0", SW + 80); a.Ldrb("r2", "r7", 1); a.Movs("r1", 15); a.Ands("r2", "r1");   // r2 = oldest slot
            a.Ldrh("r6", "r7", 12); a.Movs("r3", 0); a.Movs("r4", 0); a.Movs("r5", 0);
            a.Label("sw_fl" + s);
            a.Lsls("r7", "r2", 3); a.AddsR("r7", "r7", "r0");
            a.Ldrh("r1", "r7", hp); a.AddsR("r4", "r4", "r1");
            a.Ldrh("r1", "r7", 0); a.SubsR("r1", "r1", "r6"); a.Sxth("r1", "r1"); a.AddsR("r5", "r5", "r1");
            a.AddsI("r2", 1); a.Movs("r1", 15); a.Ands("r2", "r1");
            a.Movs("r1", 3); a.Ands("r1", "r3"); a.CmpI("r1", 3); a.Bcc("ne", "sw_fn" + s);
            a.Lsrs("r1", "r3", 2); a.Lsls("r1", "r1", 2); a.Mov("r7", "sp"); a.AddsR("r7", "r7", "r1");
            a.Str("r4", "r7", 0); a.Str("r5", "r7", 16); a.Movs("r4", 0); a.Movs("r5", 0);
            a.Label("sw_fn" + s);
            a.AddsI("r3", 1); a.CmpI("r3", N); a.Bcc("cc", "sw_fl" + s);
            a.LdrSp("r4", 0); a.LdrSp("r5", 4); a.LdrSp("r6", 8); a.LdrSp("r7", 12);
            a.AddsR("r0", "r6", "r7"); a.SubsR("r0", "r0", "r4"); a.SubsR("r0", "r0", "r5");     // r0 = Bn
            a.SubsR("r2", "r4", "r5"); a.SubsR("r2", "r2", "r6"); a.AddsR("r2", "r2", "r7");     // r2 = bend
            a.AddsR("r3", "r4", "r5"); a.AddsR("r3", "r3", "r6"); a.AddsR("r3", "r3", "r7");     // A
            a.Mov32("r7", blk); a.Str("r3", "r7", 0);
            a.LdrSp("r4", 16); a.LdrSp("r5", 20); a.LdrSp("r6", 24); a.LdrSp("r3", 28);
            a.AddsR("r1", "r6", "r3"); a.SubsR("r1", "r1", "r4"); a.SubsR("r1", "r1", "r5");     // r1 = Bd
            a.AddsR("r3", "r3", "r4"); a.AddsR("r3", "r3", "r5"); a.AddsR("r3", "r3", "r6");     // Ts
            a.Str("r3", "r7", 8);
            a.Mov32("r7", SW); a.Ldrb("r3", "r7", 2);                                             // unit shift
            a.CmpI("r1", 0); a.Bcc("le", "sw_no" + s);
            a.CmpI("r2", 0); a.Bcc("ge", "sw_g1" + s); a.Rsbs("r2"); a.Label("sw_g1" + s);
            a.LslsR("r2", "r3"); a.Movw("r4", AG); a.CmpR("r2", "r4"); a.Bcc("hi", "sw_no" + s);
            a.Mov("r2", "r0"); a.CmpI("r2", 0); a.Bcc("ge", "sw_g2" + s); a.Rsbs("r2"); a.Label("sw_g2" + s);
            a.AddsI("r3", 2); a.LslsR("r2", "r3"); a.Movs("r4", VG4); a.Muls("r4", "r1"); a.CmpR("r2", "r4"); a.Bcc("hi", "sw_no" + s);
            // a coil-window switch also needs the pen moving (15 mm/s: 10 x 4 |slope| >= 3), so a still pen never gets
            // an offset; drive-step and ladder-step switches are matched at any speed
            a.LdrSp("r3", 36); a.Lsrs("r3", "r3", 3); a.Bcc("cs", "sw_mv" + s);
            a.Lsls("r3", "r2", 3); a.AddsR("r3", "r3", "r2"); a.AddsR("r3", "r3", "r2");
            a.CmpR("r3", "r4"); a.Bcc("cc", "sw_no" + s);
            a.Label("sw_mv" + s);
            a.Lsls("r0", "r0", 12); a.Sdiv("r0", "r0", "r1"); a.Mov32("r2", blk); a.Str("r0", "r2", 4);
            a.Movs("r0", 1); a.B("sw_fd" + s);
            a.Label("sw_no" + s); a.Movs("r0", 0);
            a.Label("sw_fd" + s);
            a.AddSp(32); a.Pop(Rg("r4", "r5", "r6"), false); a.Mov32("r7", SW);
            a.Cbz("r0", "sw_r" + s);
            a.Movs("r0", 0); a.Strb("r0", "r7", kb); a.Str("r0", "r6", 12); a.Strh("r0", "r6", 20);
            a.Ldrh("r0", "r7", 12); a.Strh("r0", "r6", 22); a.Strh("r4", "r6", 18);
            // refinement: o0 = mean over the first M frames of (line - reading)
            a.Label("sw_r" + s);
            a.Ldrb("r0", "r7", kb); a.CmpI("r0", M); a.Bcc("cs", "sw_f" + s);
            a.Ldrh("r2", "r7", 12); a.Ldrh("r3", "r6", 22); a.SubsR("r2", "r2", "r3"); a.Uxth("r2", "r2");
            a.Lsls("r2", "r2", 4); a.Ldr("r3", "r6", 8); a.SubsR("r2", "r2", "r3");
            a.Ldr("r3", "r6", 4); a.Muls("r2", "r3"); a.Asrs("r2", "r2", 12); a.Ldr("r3", "r6", 0); a.AddsR("r2", "r2", "r3");
            a.Lsls("r3", "r4", 4); a.SubsR("r2", "r2", "r3");
            a.Ldr("r3", "r6", 12); a.AddsR("r3", "r3", "r2"); a.Str("r3", "r6", 12);
            a.AddsI("r0", 1); a.Strb("r0", "r7", kb);
            a.Lsls("r0", "r0", 4); a.Sdiv("r3", "r3", "r0");
            a.Movw("r0", OM); a.Ldrb("r1", "r7", 2); a.LsrsR("r0", "r1");
            a.CmpR("r3", "r0"); a.Bcc("le", "sw_c1" + s); a.Mov("r3", "r0"); a.Label("sw_c1" + s);
            a.Rsbs("r0"); a.CmpR("r3", "r0"); a.Bcc("ge", "sw_c2" + s); a.Mov("r3", "r0"); a.Label("sw_c2" + s);
            a.Strh("r3", "r6", 16);
            // fade: by the farthest distance from the switch reading and by time
            a.Label("sw_f" + s);
            a.Movs("r3", 0);
            a.Ldrb("r0", "r7", kb); a.CmpI("r0", 255); a.Bcc("eq", "sw_o" + s);
            a.Ldrh("r0", "r6", 18); a.SubsR("r0", "r4", "r0"); a.Bcc("pl", "sw_a" + s); a.Rsbs("r0"); a.Label("sw_a" + s);
            a.Ldrh("r2", "r6", 20); a.CmpR("r0", "r2"); a.Bcc("ls", "sw_m" + s); a.Strh("r0", "r6", 20); a.Mov("r2", "r0"); a.Label("sw_m" + s);
            a.Movw("r0", D0); a.Ldrb("r1", "r7", 2); a.LsrsR("r0", "r1");
            a.CmpR("r2", "r0"); a.Bcc("cs", "sw_k" + s);
            a.SubsR("r2", "r0", "r2");
            a.Ldrh("r1", "r7", 12); a.Ldrh("r3", "r6", 22); a.SubsR("r1", "r1", "r3"); a.Uxth("r1", "r1");
            a.Movw("r3", TF); a.CmpR("r1", "r3"); a.Bcc("cs", "sw_k" + s);
            a.SubsR("r1", "r3", "r1");
            a.Ldrh("r3", "r6", 16); a.Sxth("r3", "r3"); a.Muls("r3", "r2"); a.Sdiv("r3", "r3", "r0");
            a.Muls("r3", "r1"); a.Movw("r0", TF); a.Sdiv("r3", "r3", "r0");
            a.B("sw_o" + s);
            a.Label("sw_k" + s); a.Movs("r0", 255); a.Strb("r0", "r7", kb); a.Movs("r3", 0);
            a.Label("sw_o" + s);
            a.AddsR("r0", "r4", "r3"); a.Bcc("pl", "sw_p" + s); a.Movs("r0", 0); a.Label("sw_p" + s);
            a.Mov32("r1", Sched.D_ADDR); a.Strh("r0", "r1", 8 + 2 * q);
            a.Ldrb("r2", "r7", 1); a.Movs("r1", 15); a.Ands("r2", "r1"); a.Lsls("r2", "r2", 3); a.AddsR("r2", "r2", "r7"); a.AddsI("r2", 80);
            a.Strh("r0", "r2", hp);
        }

        // C# copy of the routine for one axis (the twin harness checks the firmware against it): tk = frame times in
        // ticks, p = readings, c = reading conditions, u = unit shift; st = the drive and ladder step part of the
        // conditions (null: every switch is a coil-window switch). Returns the outputs.
        public static int[] Model(int[] tk, int[] p, int[] c, int u) { return Model(tk, p, c, u, null); }
        public static int[] Model(int[] tk, int[] p, int[] c, int u, int[] st)
        {
            int n = p.Length; int[] outp = new int[n]; int[] ht = new int[N], hv = new int[N];
            int cnt = 0, head = 0, k = 255, A = 0, S = 0, Ts = 0, acc = 0, o0 = 0, ps = 0, dmax = 0, tref = 0, since = 0;
            for (int i = 0; i < n; i++)
            {
                int raw = p[i], tn = tk[i] & 0xFFFF;
                bool sw = i > 0 && c[i] != c[i - 1];
                int was = since;
                if (sw) since = 0; else if (since < 255) since++;
                if (sw)
                {
                    k = 255; o0 = 0;
                    bool stepSw = st != null && st[i] != st[i - 1];
                    if (cnt >= N && was >= N)
                    {
                        int[] Q = new int[4], T = new int[4];
                        for (int j = 0; j < N; j++)
                        {
                            int sl = (head + j) & 15;
                            Q[j >> 2] += hv[sl]; T[j >> 2] += (short)((ht[sl] - tn) & 0xFFFF);
                        }
                        int Bn = Q[2] + Q[3] - Q[0] - Q[1], Bd = T[2] + T[3] - T[0] - T[1], d2 = Q[0] - Q[1] - Q[2] + Q[3];
                        int b4 = System.Math.Abs(Bn) << (u + 2);
                        if (Bd > 0 && (System.Math.Abs(d2) << u) <= AG && b4 <= VG4 * Bd && (stepSw || VMIN10 * b4 >= VG4 * Bd))
                        {
                            A = Q[0] + Q[1] + Q[2] + Q[3]; S = (Bn << 12) / Bd; Ts = T[0] + T[1] + T[2] + T[3];
                            tref = tn; acc = 0; k = 0; ps = raw; dmax = 0;
                        }
                    }
                }
                if (k < M)
                {
                    int dt = (tn - tref) & 0xFFFF;
                    int pred16 = A + ((S * (16 * dt - Ts)) >> 12);
                    acc += pred16 - 16 * raw; k++;
                    o0 = acc / (k << 4);
                    int lim = OM >> u;
                    if (o0 > lim) o0 = lim;
                    if (o0 < -lim) o0 = -lim;
                }
                int o = 0;
                if (k != 255)
                {
                    int d = System.Math.Abs(raw - ps); if (d > dmax) dmax = d;
                    int d0 = D0 >> u, e = (tn - tref) & 0xFFFF;
                    if (dmax >= d0 || e >= TF) k = 255;
                    else { o = o0 * (d0 - dmax) / d0; o = o * (TF - e) / TF; }
                }
                int v = raw + o; if (v < 0) v = 0;
                outp[i] = v; ht[head] = tn; hv[head] = v; head = (head + 1) & 15; if (cnt < N) cnt++;
            }
            return outp;
        }
    }
}
