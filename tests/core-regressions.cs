// Compile with the project sources and /main:AnargonsTabletTools.Tests.CoreRegressions.
// Optional argument: an untouched stock main.bin. This executable never opens a real tablet.
using System;
using System.Text;
using Gd32Emu;

namespace AnargonsTabletTools.Tests
{
    // R42: at each frame restart (0x08007084) the line buffers still hold the frame that just ended: its five X and five
    // Y line sums (/ 4 outside the fast stage, as the recorder stores them) and both centre coils
    public class FrameLinesBoard : TabletBoard
    {
        public System.Collections.Generic.List<int[]> frames = new System.Collections.Generic.List<int[]>();
        public FrameLinesBoard(Pen p) : base(p) { }
        public override bool Hook(Machine mc, int hk)
        {
            bool r = base.Hook(mc, hk);
            if (hk == H_FRAME)
            {
                int shift = mc.Peek(Sched.VB_ADDR, 1) != 0 ? 2 : 0;
                int[] f = new int[12];
                for (int j = 0; j < 10; j++) { int v = mc.Peek((j < 5 ? Sched.XBUF : Sched.YBUF) + 12 * (j % 5 + 1) + 4, 4) >> shift; f[j] = v > 0xFFFF ? 0xFFFF : v; }
                f[10] = mc.Peek(Sched.C_ADDR + 6, 1); f[11] = mc.Peek(Sched.C_ADDR + 7, 1);
                frames.Add(f);
                pen.x += 0.004; pen.y += 0.003;
            }
            return r;
        }
    }

    // R43: the raw readings in the sample buffer (0x2000092C, the first four of the last step) at every frame end:
    // how many there are, and how many are not multiples of 8 (stock clears the low 3 bits of every reading)
    public class SampleBitsBoard : TabletBoard
    {
        public long n, odd;
        public SampleBitsBoard(Pen p) : base(p) { }
        public override bool Hook(Machine mc, int hk)
        {
            bool r = base.Hook(mc, hk);
            if (hk == H_FRAME)
                for (int i = 0; i < 4; i++) { int v = mc.Peek(0x2000092C + 2 * i, 2); if (v == 0) continue; n++; if ((v & 7) != 0) odd++; }
            return r;
        }
    }

    // R41: watches the frequency sweep. A sweep is mixed when a later step runs in another hover stage than step 0;
    // the scheduler must keep every mixed sweep away from the frequency estimator (0x08007604). Each sweep step reads
    // through 0x0800454C with step kind 4 and its index in C[1].
    public class SweepBoard : TabletBoard
    {
        public const int HK_EST = 200, HK_STEP = 202;
        public int stage0 = -1, mixedSeen = 0, mixedEstimated = 0, estimates = 0;
        bool mixed = false;
        public SweepBoard(Pen p) : base(p) { }
        public override void Attach(Machine mc)
        {
            base.Attach(mc);
            mc.SetHook(Fw.A(0x08007604), HK_EST); mc.SetHook(Fw.A(0x0800454C), HK_STEP);
        }
        public override bool Hook(Machine mc, int hk)
        {
            if (hk == HK_STEP)
            {
                if (mc.Peek(Sched.S_ADDR, 1) != 4) return false;
                int k = mc.Peek(Sched.C_ADDR + 1, 1), stage = mc.Peek(Sched.VB_ADDR, 1);
                if (k == 0) { stage0 = stage; mixed = false; }
                else if (stage != stage0 && !mixed) { mixed = true; mixedSeen++; }
                return false;
            }
            if (hk == HK_EST) { estimates++; if (mixed) mixedEstimated++; return false; }
            return base.Hook(mc, hk);
        }
    }

    public static class CoreRegressions
    {
        static int checks;
        static void Check(bool ok, string name)
        {
            checks++;
            if (!ok) throw new Stop("FAIL: " + name);
        }

        // R45-rc3 parallel runs (tests/run-regressions.py --jobs): ADT_TEST_GROUPS=a,b runs only those groups and
        // ADT_TEST_LIST_GROUPS=1 prints every group ("GROUP name") and runs none; unset, every group runs, as before.
        // Every check is inside a group: setup outside one runs in every process, so a check there would be counted
        // once per process.
        static bool Group(string name)
        {
            if (Environment.GetEnvironmentVariable("ADT_TEST_LIST_GROUPS") == "1") { P.Line("GROUP " + name); return false; }
            string only = Environment.GetEnvironmentVariable("ADT_TEST_GROUPS");
            return string.IsNullOrEmpty(only) || ("," + only + ",").IndexOf("," + name + ",", StringComparison.Ordinal) >= 0;
        }

        static byte[] BlankFlash()
        {
            byte[] image = new byte[0x10000];
            for (int i = 0; i < image.Length; i++) image[i] = (byte)0xFF;
            return image;
        }

        static string Record(int type, int address, byte[] data)
        {
            int sum = data.Length + (address >> 8) + (address & 255) + type;
            StringBuilder text = new StringBuilder(":" + F.HexDigits(data.Length, 2, true) + F.HexDigits(address, 4, true) + F.HexDigits(type, 2, true));
            for (int i = 0; i < data.Length; i++) { sum += data[i] & 255; text.Append(F.HexDigits(data[i] & 255, 2, true)); }
            return text.ToString() + F.HexDigits((-sum) & 255, 2, true) + "\n";
        }

        static void BadHex(string text, string name)
        {
            bool rejected = false;
            try { IntelHex.Parse(text, "test.hex", new int[1]); } catch (Stop) { rejected = true; }
            Check(rejected, "HEX rejects " + name);
        }

        static void HexInputs()
        {
            string header = Record(4, 0, new byte[] { 8, 0 });
            string data = Record(0, 0x3800, new byte[] { 0x12, 0x34 });
            string end = Record(1, 0, new byte[0]);
            int[] first = new int[1];
            byte[] parsed = IntelHex.Parse(header + data + end, "test.hex", first);
            Check(first[0] == Build.BASE && parsed.Length == 2 && parsed[0] == 0x12 && parsed[1] == 0x34, "HEX reads the declared address and bytes");
            parsed = IntelHex.Parse(header + data + data + end, "test.hex", first);
            Check(parsed.Length == 2, "HEX allows identical overlapping records");
            parsed = IntelHex.Parse(P.Lower(header + data + Record(3, 0, new byte[] { 0, 0, 0, 0 }) + Record(5, 0, new byte[] { 8, 0, 0x38, 1 }) + end), "test.hex", first);
            Check(parsed.Length == 2, "HEX accepts lowercase digits and standard entry metadata");
            parsed = IntelHex.Parse(header + data + Record(0, 0x3803, new byte[] { 0x56 }) + end, "test.hex", first);
            Check(parsed.Length == 4 && (parsed[2] & 255) == 255, "HEX fills unwritten gaps with erased bytes");
            BadHex("", "an empty file"); BadHex(":00\n", "a short record");
            BadHex(header + P.Sub(data, 0, P.Len(data) - 1) + "0\n" + end, "an odd number of hex digits");
            BadHex(header + ":02380000123481\n" + end, "a bad checksum");
            BadHex(header + ":0338000012347F\n" + end, "a mismatched byte count");
            BadHex(header + ":0238000012ZZ80\n" + end, "a non-hex digit");
            BadHex(header + data, "a missing end marker");
            BadHex(header + data + end + end, "a second end marker");
            BadHex(header + data + end + data, "data after the end marker");
            BadHex(header + end, "a file with no data");
            BadHex(header + data + Record(0, 0x3801, new byte[] { 0x99 }) + end, "conflicting overlaps");
            BadHex(Record(4, 0, new byte[] { 7, 255 }) + data + end, "an address below flash");
            BadHex(Record(4, 0, new byte[] { 8, 1 }) + data + end, "an address above flash");
            BadHex(header + Record(0, 0xFFFF, new byte[] { 0, 1 }) + end, "a record crossing the flash boundary");
            BadHex(Record(4, 0, new byte[] { 8 }) + data + end, "malformed address metadata");
            BadHex(Record(4, 1, new byte[] { 8, 0 }) + data + end, "address metadata with a nonzero offset");
            BadHex(header + data + Record(3, 0, new byte[] { 0, 0, 0 }) + end, "malformed entry metadata");
            BadHex(header + data + Record(1, 0, new byte[] { 0 }), "a malformed end marker");
            BadHex(header + data + Record(6, 0, new byte[0]) + end, "an unknown record type");
        }

        static void Branch(int offset, bool conditional, bool accepted)
        {
            int address = 0x08004000;
            Asm assembler = new Asm(address);
            if (conditional) assembler.Bcc("eq", "target"); else assembler.B("target");
            assembler.labels.Set("target", address + 4 + offset);
            byte[] code = null;
            try { code = assembler.Finish(); }
            catch (Stop) { Check(!accepted, "reject branch offset " + F.Str(offset)); return; }
            Check(accepted, "accept branch offset " + F.Str(offset));
            byte[] image = BlankFlash();
            P.Copy(code, 0, image, address - 0x08000000, code.Length);
            Machine machine = new Machine(image, false, null, false);
            machine.cpu.r[15] = address;
            machine.cpu.Z = 1;
            machine.cpu.Step();
            Check(machine.cpu.r[15] == address + 4 + offset, "branch reaches its requested destination");
        }

        static void Branches()
        {
            Branch(-2048, false, true); Branch(2046, false, true);
            Branch(-2050, false, false); Branch(2048, false, false);
            Branch(-4096, false, false); Branch(4094, false, false);
            Branch(1, false, false); Branch(-1, false, false);
            Branch(-256, true, true); Branch(254, true, true);
            Branch(-258, true, false); Branch(256, true, false);
            Branch(1, true, false); Branch(-1, true, false);
        }

        static void Unlock(Machine machine)
        {
            machine.FmcWrite(0x40022004, 0x45670123);
            machine.FmcWrite(0x40022004, unchecked((int)0xCDEF89AB));
        }

        public class CleanupBoard : TabletBoard
        {
            bool protectReset;
            public CleanupBoard(Pen pen, bool protect) : base(pen) { protectReset = protect; }
            public override void Attach(Machine machine)
            {
                base.Attach(machine);
                Layout layout = Twin.LayoutOf(machine.flash);
                machine.SetHook(layout.A(0x08009A1C), 100);
                machine.SetHook(layout.A(0x08009A40), 101);
            }
            public override bool Hook(Machine machine, int hook)
            {
                // Inject a failure of the firmware's erase/program helper for page 0 only. The emulator's
                // bootWp switch protects the entire bootloader and cannot expose this selective failure.
                int address = machine.cpu.r[0];
                if ((hook == 100 || hook == 101) && protectReset && address >= Sched.BL_PAGE0 && address < Sched.BL_PAGE0 + 1024)
                {
                    machine.fmcStat |= 0x10;
                    machine.cpu.r[15] = machine.cpu.r[14] & ~1;
                    return true;
                }
                return base.Hook(machine, hook);
            }
        }

        static void Word(byte[] image, int offset, int value)
        {
            for (int i = 0; i < 4; i++) image[offset + i] = (byte)((value >> (i * 8)) & 255);
        }

        static void Cleanup(string file)
        {
            InputInfo info = new InputInfo();
            byte[] app = Build.ReadInput(file, info);
            Check(info.boot != null && info.stock, "cleanup test uses an untouched full stock dump");
            bool[] settings = Cli.Defaults();
            string[] off = new string[] { "boost", "reach", "longrange", "cal", "odd", "multicoil", "interp2", "blcounter", "volatile", "bootloader" };
            for (int i = 0; i < off.Length; i++) settings[Build.SetIndex(off[i])] = false;
            Options options = new Options(); options.cleanup = true;
            bool wasQuiet = Build.quiet; Build.quiet = true;
            byte[] cleanup;
            try { cleanup = Build.BuildOne(app, info, null, Build.Lv("500"), settings, false, false, options).Bytes(); }
            finally { Build.quiet = wasQuiet; }
            int[] stubEntry = new int[1];
            byte[] wrapper = Sched.BlBlob(0x0800F000, Machine.LE(info.boot, 0xC00, 4), stubEntry);
            for (int mode = 0; mode < 3; mode++)
            {
                byte[] image = Twin.FromApp(cleanup);
                P.Copy(info.boot, image, info.boot.Length);
                P.Copy(wrapper, 0, image, 0x2800, wrapper.Length);
                Word(image, 0xF40, Sched.BlPatchWord());
                Word(image, 0x15C, mode == 1 ? 0x08002805 : stubEntry[0] | 1);
                Word(image, 0x2C00, Build.COUNTER_MAGIC);
                long stubBefore = Txt.Crc32(image, 0x2800, 1024), resetBefore = Txt.Crc32(image, 0, 1024);
                Pen pen = new Pen(1);
                Machine machine = new Machine(image, true, new CleanupBoard(pen, mode == 2), false);
                Check(P.Eq(machine.Boot(3000000), "ok"), "cleanup test boots in the twin");
                machine.AttachUsb(true); machine.RunMs(30);
                int status = machine.Peek(Sched.VS_ADDR + 13, 1);
                if (mode == 0)
                {
                    Check(status == 0, "normal cleanup succeeds");
                    Check(Txt.Crc32(machine.flash, 0, 1024) == Txt.Crc32(info.boot, 0, 1024), "cleanup restores the complete reset page");
                    Check(Machine.LE(machine.flash, 0x2800, 4) == -1 && Machine.LE(machine.flash, 0x2C00, 4) == -1, "cleanup erases its old stub and counter after verification");
                }
                else
                {
                    Check(status == (mode == 1 ? 3 : 4), mode == 1 ? "unknown reset redirect stops cleanup" : "failed reset-page write stops cleanup");
                    Check(Txt.Crc32(machine.flash, 0, 1024) == resetBefore && Txt.Crc32(machine.flash, 0x2800, 1024) == stubBefore,
                        "failed cleanup preserves the reset target and repair stub");
                    Check(Machine.LE(machine.flash, 0x2C00, 4) == Build.COUNTER_MAGIC, "failed cleanup preserves the counter page");
                }
            }
        }

        static void FlashIsolation()
        {
            byte[] input = BlankFlash();
            Machine first = new Machine(input, false, null, false);
            Machine second = new Machine(input, false, null, false);
            input[0x4100] = 0;
            Check((first.flash[0x4100] & 0xFF) == 255, "caller cannot change a running machine's flash");
            Unlock(first); first.FmcWrite(0x40022010, 1);
            first.Write(0x08004000, 4, 0x12345678);
            Check(Machine.LE(first.flash, 0x4000, 4) == 0x12345678, "unlocked flash programs an erased word");
            Check(Machine.LE(second.flash, 0x4000, 4) == -1 && Machine.LE(input, 0x4000, 4) == -1, "programming stays inside one machine");
            first.Write(0x08004000, 4, 0);
            Check((first.fmcStat & 4) != 0 && Machine.LE(first.flash, 0x4000, 4) == 0x12345678, "non-erased programming sets PGERR and preserves the word");
            first.flash[0x3FFF] = 0; first.flash[0x4400] = 0;
            first.FmcWrite(0x40022014, 0x08004000); first.FmcWrite(0x40022010, 0x42);
            Check(Machine.LE(first.flash, 0x4000, 4) == -1 && first.flash[0x3FFF] == 0 && first.flash[0x4400] == 0, "erase affects exactly one page");
            bool locked = false;
            try { second.Write(0x08004000, 4, 1); } catch (Fault) { locked = true; }
            Check(locked, "locked flash rejects programming");
            bool previousProtection = Machine.bootWp;
            try
            {
                Machine.bootWp = true;
                Unlock(second); second.FmcWrite(0x40022010, 1);
                second.Write(0x08002C00, 4, 1);
                Check((second.fmcStat & 0x10) != 0 && Machine.LE(second.flash, 0x2C00, 4) == -1, "write protection prevents a bootloader program");
                second.flash[0x2C00] = 0;
                second.FmcWrite(0x40022014, 0x08002C00); second.FmcWrite(0x40022010, 0x42);
                Check(second.flash[0x2C00] == 0, "write protection prevents a bootloader erase");
            }
            finally { Machine.bootWp = previousProtection; }
            first.SystemReset();
            Check((first.PeriphRead(0x40021024) & (1 << 28)) != 0, "software reset records SWRSTF");
            Check((first.fmcCtl & 0x80) != 0, "reset locks the flash controller");
        }

        static void StockRestoration()
        {
            byte[] stock = new byte[0x3800];
            for (int i = 0; i < stock.Length; i++) stock[i] = (byte)(i * 17);
            for (int i = 0x2800; i < 0x3000; i++) stock[i] = (byte)255;
            byte[] current = BlankFlash(); P.Copy(stock, current, stock.Length);
            Check(Cli.StockBootloaderRestored(current, stock), "restored bootloader passes the final stock guard");
            int[] critical = new int[] { 0x15C, 0xF40, 0x27FF, 0x2800, 0x2FFF, 0x2C00 };
            for (int i = 0; i < critical.Length; i++)
            {
                int address = critical[i]; byte saved = current[address]; current[address] = (byte)(saved ^ 1);
                Check(!Cli.StockBootloaderRestored(current, stock), "unrestored boot byte blocks final stock flash: " + F.Hx(address, 4));
                current[address] = saved;
            }
            current[0x3010] = (byte)(current[0x3010] ^ 1); current[0x3400] = 2;
            Check(Cli.StockBootloaderRestored(current, stock), "vendor data and update-state changes do not block a restored bootloader");
            Check(!Cli.StockBootloaderRestored(new byte[0xFFFF], stock), "incomplete final backup cannot prove restoration");
            Check(!Cli.StockBootloaderRestored(current, new byte[0x2800]), "incomplete stock bootloader cannot prove restoration");
        }

        static void PositionReports()
        {
            byte[] data = new byte[10]; data[0] = 7; data[1] = 0xA0;
            Report report = new Report(0, 2, data);
            Check(Twin.Tracked(report) && Twin.PositionReport(report), "normal in-range ID7 contains coordinates");
            data[1] = 0x80;
            Check(!Twin.Tracked(report) && !Twin.PositionReport(report), "normal leave report is not a position");
            data[1] = 0xC0; data[8] = 0xA0; data[9] = 0xE1;
            Check(Twin.Tracked(report) && Twin.PositionReport(report), "telemetry E1 with saved in-range flags contains coordinates");
            data[8] = 0x80;
            Check(Twin.Tracked(report) && !Twin.PositionReport(report), "telemetry E1 without in-range flags cannot measure coordinates");
            data[8] = 0xA0;
            int[] pages = new int[] { 0xE0, 0xE2, 0xE3 };
            for (int i = 0; i < pages.Length; i++)
            {
                data[9] = (byte)pages[i];
                Check(Twin.Tracked(report) && !Twin.PositionReport(report), "telemetry non-position page cannot be used as X/Y");
            }
            data[9] = 0xE4;
            Check(!Twin.Tracked(report), "unsupported telemetry page is not accepted as tracking");
            data[9] = 0xE1; data[0] = 2;
            Check(!Twin.Tracked(report) && !Twin.PositionReport(report), "ID2 cannot impersonate a telemetry position");
            data[0] = 7; data[1] = 0x80;
            Check(!Twin.Tracked(report), "telemetry marker without C0 framing is rejected");
            Check(!Twin.PositionReport(new Report(0, 2, new byte[9])) && !Twin.PositionReport(new Report(0, 1, data)), "short or wrong-endpoint reports cannot contain tablet coordinates");
        }

        static byte[] TabletConfig()
        {
            return new byte[] {
                9, 2, 91, 0, 3, 1, 0, 0x80, 50,
                9, 4, 0, 0, 1, 3, 1, 2, 0,
                9, 0x21, 0x11, 1, 0, 1, 0x22, 13, 0,
                7, 5, 0x81, 3, 8, 0, 3,
                9, 4, 1, 0, 1, 3, 1, 2, 0,
                9, 0x21, 0x11, 1, 0, 1, 0x22, 13, 0,
                7, 5, 0x82, 3, 64, 0, 3,
                9, 4, 2, 0, 2, 3, 0, 0, 0,
                9, 0x21, 0x11, 1, 0, 1, 0x22, 13, 0,
                7, 5, 0x83, 3, 12, 0, 3,
                7, 5, 3, 3, 10, 0, 10 };
        }

        static byte[][] HidReports()
        {
            byte[][] reports = new byte[3][];
            for (int i = 0; i < 3; i++) reports[i] = new byte[] { 5, 1, 9, 2, 0xA1, 1, 0x75, 8, 0x95, 1, 0x81, 2, 0xC0 };
            return reports;
        }

        static void RejectUsb(byte[] device, byte[] config, byte[][] reports, string name)
        {
            string reason = Twin.ValidateTabletUsb(device, config, reports, 0);
            Check(reason != null && P.Len(reason) > 0, "USB rejects " + name + " with a reason");
        }

        static void UsbSchema()
        {
            byte[] device = new byte[] { 18, 1, 0, 2, 0, 0, 0, 64, 0xBD, 0x28, 4, 0x29, 0, 1, 1, 2, 3, 1 };
            byte[] config = TabletConfig(); byte[][] reports = HidReports();
            Check(Twin.ValidateTabletUsb(device, config, reports, 3) == null, "USB accepts the stock endpoint layout and complete HID reports");
            config[88] = 20;
            Check(Twin.ValidateTabletUsb(device, config, reports, 3) == null, "USB accepts the supported 20-byte OUT endpoint");
            config[33] = 1; config[58] = 1; config[83] = 1;
            Check(Twin.ValidateTabletUsb(device, config, reports, 1) == null, "USB accepts the patched IN intervals");
            Check(Twin.ValidateTabletUsb(device, config, reports, 3) != null, "USB rejects a generated build with unexpected IN intervals");
            config = TabletConfig(); config[54] = 0x84; RejectUsb(device, config, reports, "EP82 renamed to unsupported EP84");
            config = TabletConfig(); config[54] = 2; RejectUsb(device, config, reports, "EP82 changed to an OUT endpoint");
            config = TabletConfig(); config[55] = 2; RejectUsb(device, config, reports, "a bulk pen endpoint");
            config = TabletConfig(); config[56] = 0; RejectUsb(device, config, reports, "zero max packet size");
            config = TabletConfig(); config[56] = 32; RejectUsb(device, config, reports, "the wrong pen max packet size");
            config = TabletConfig(); config[88] = 12; RejectUsb(device, config, reports, "an unsupported OUT max packet size");
            config = TabletConfig(); config[14] = 0xFF; RejectUsb(device, config, reports, "a non-HID tablet interface");
            config = TabletConfig(); config[52] = 6; RejectUsb(device, config, reports, "a short endpoint descriptor");
            config = TabletConfig(); config[34] = 1; RejectUsb(device, config, reports, "an invalid interface descriptor length");
            config = TabletConfig(); config[2]--; RejectUsb(device, config, reports, "a mismatched configuration total length");
            config = TabletConfig(); config[18] = 8; RejectUsb(device, config, reports, "a truncated HID declaration");
            config = TabletConfig(); config[19] = 0x30; RejectUsb(device, config, reports, "a missing HID declaration");
            config = TabletConfig(); config[25] = 0; RejectUsb(device, config, reports, "a zero HID report declaration length");
            byte[] duplicateHid = new byte[100]; config = TabletConfig();
            P.Copy(config, duplicateHid, 27); P.Copy(config, 18, duplicateHid, 27, 9); P.Copy(config, 27, duplicateHid, 36, config.Length - 27);
            duplicateHid[2] = 100; duplicateHid[25] = 0;
            RejectUsb(device, duplicateHid, reports, "a zero-length HID declaration followed by a duplicate valid declaration");
            config = TabletConfig(); config[25] = 1; config[26] = 4; RejectUsb(device, config, reports, "an oversized HID report declaration");
            config = TabletConfig(); reports[1] = null; RejectUsb(device, config, reports, "a missing HID report response");
            reports = HidReports(); reports[1] = new byte[0]; RejectUsb(device, config, reports, "an empty HID report response");
            reports = HidReports(); reports[1] = new byte[12]; RejectUsb(device, config, reports, "a truncated HID report response");
            reports = HidReports(); reports[1][12] = 0x75; RejectUsb(device, config, reports, "a truncated HID item despite a matching response length");
            reports = HidReports(); reports[1][12] = 0; RejectUsb(device, config, reports, "an unclosed HID collection");
            reports = HidReports(); reports[0][9] = 9; RejectUsb(device, config, reports, "a HID input report larger than its endpoint");
            reports = HidReports(); reports[1][10] = 0x91; RejectUsb(device, config, reports, "a HID descriptor without input data");
            reports = HidReports(); device[8] = 0; RejectUsb(device, config, reports, "an unexpected VID"); device[8] = 0xBD;
            byte[] shortDevice = new byte[17]; P.Copy(device, shortDevice, shortDevice.Length);
            RejectUsb(shortDevice, config, reports, "a truncated device descriptor");
            byte[] noPen = new byte[84]; config = TabletConfig();
            P.Copy(config, noPen, 52); P.Copy(config, 59, noPen, 52, config.Length - 59);
            noPen[2] = 84; noPen[38] = 0;
            RejectUsb(device, noPen, reports, "a missing pen endpoint even when the configuration is structurally valid");
        }

        static void UsbFirmwareRejections(byte[] stock)
        {
            int[] sites = Twin.FindAll(stock, new byte[] { 0x82, 3, 64 }, 0x3800, stock.Length);
            Check(sites.Length == 1, "USB firmware test identifies the unique compressed pen endpoint literal");
            string file = P.PathJoin("tests/work", "core-invalid-usb.bin");
            P.MakeDirs("tests/work");
            bool quiet = Build.quiet; Build.quiet = true;
            try
            {
                byte[] changed = new byte[stock.Length]; P.Copy(stock, changed, stock.Length);
                changed[sites[0] - 0x08000000] = 0x84; P.WriteAllBytes(file, changed);
                Out output = new Out(); output.buf = new StringBuilder();
                Check(!Twin.Describe(changed, "invalid pen endpoint", output) && output.failed
                    && P.IndexOf(output.buf.ToString(), "Unsupported USB endpoint address 0x84") >= 0,
                    "Describe rejects an unsupported endpoint address with its reason");
                Check(Twin.Run(new string[] { file, "--checks", "usb" }) == 1, "standalone USB checks reject the malformed firmware");
                Check(Twin.Run(new string[] { file, "--checks", "boot" }) == 1, "standalone boot checks reject the same malformed firmware");
                Check(Cli.Gate(file, 3, false, false) == 0, "builder gate rejects the same malformed firmware");
                P.Copy(stock, changed, stock.Length); changed[sites[0] - 0x08000000 + 2] = 32;
                output = new Out(); output.buf = new StringBuilder();
                Check(!Twin.Describe(changed, "wrong pen packet", output) && output.failed
                    && P.IndexOf(output.buf.ToString(), "endpoint 0x82 max packet size must be 64") >= 0,
                    "Describe applies the tablet packet-size contract beyond generic enumeration");
            }
            finally { Build.quiet = quiet; P.DeleteFile(file); }
        }

        // R41: Accurate Pressure Readings: stock-length sweep steps, as many per frame as fit
        static void AccurateLevels()
        {
            Check(Build.Accurate(Build.Lv("300"), null).chunk == 0, "QUICK: the whole stock-length sweep in every frame");
            Check(Build.Accurate(Build.Lv("500"), null).chunk == 3, "FAST: 3 stock-length sweep steps per frame");
            Level a = Build.Accurate(Build.Lv("800"), null);
            Check(a.chunk == 1 && a.swSamples == Build.ACC_SAMPLES && a.swBurst == Build.ACC_BURST, "RAPID: 1 stock-length sweep step per frame");
            Check(Build.FrameNeedUs(a, a.samples, a.burst, a.swSamples, a.swBurst, 10, true, true, false) <= a.period - 25, "RAPID's accurate sweep fits its frame with every filter on");
            Check(Build.Accurate(Build.Lv("1000"), null) == null && Build.Unavailable("accp", Build.Lv("1000"), null) != null, "ULTRA has no room for stock-length readings");
            Check(Build.Unavailable("accp", Build.Lv("220"), null) != null, "STOCK already reads at stock length");
            Check(Math.Abs(Build.SweepRate(a) - 100) < 0.5 && Math.Abs(Build.SweepRate(Build.Lv("800")) - 300) < 0.5, "RAPID: 100 pressure updates per second accurate, 300 short");
            Options o = new Options(); o.samples = 6;
            Check(Build.Effective(Build.Lv("800"), Cli.Defaults(), o) == Build.Lv("800"), "expert timing overrides keep the preset's own sweep");
        }

        // R41 twin pens: a resonator 4 mm above the tablet, integrating receiver
        static Pen AirPen(double q, double f0)
        {
            Pen pen = new Pen(1);
            pen.x = 16.3; pen.y = 9.6; pen.f0 = f0; pen.Q = q; pen.strength = 60000; pen.noise = 6; pen.range = Pen.RANGE_TOUCH;
            pen.hasHeight = true; pen.height = 4; pen.resonator = true; pen.integrate = true;
            return pen;
        }
        static byte[] Preset(byte[] app, InputInfo info, string key, bool accurate) { return Preset(app, info, key, accurate, true); }
        // R45: HOLDS = Frequency Hold and Coil Hold (on by default)
        static byte[] Preset(byte[] app, InputInfo info, string key, bool accurate, bool holds)
        {
            bool[] st = Cli.Defaults(); st[Build.SetIndex("accp")] = accurate;
            if (!holds) { st[Build.SetIndex("fhold")] = false; st[Build.SetIndex("chold")] = false; }
            bool quiet = Build.quiet; Build.quiet = true;
            try { return Twin.FromApp(Build.BuildOne(app, info, null, Build.Lv(key), st, true, false, new Options()).Bytes()); }
            finally { Build.quiet = quiet; }
        }
        static Machine Hover(byte[] img, TabletBoard board, int ms)
        {
            Machine machine = new Machine(img, true, board, false);
            Check(P.Eq(machine.Boot(3000000), "ok"), "hover test build boots");
            machine.AttachUsb(true);
            for (int t = 0; t < ms; t++) machine.RunMs(1);
            return machine;
        }
        // ID 7 reports after the first 300 ms that show the tip or pressure
        static int Pressed(Machine machine)
        {
            int n = 0;
            for (int i = 0; i < machine.reports.n; i++)
            {
                Report r = machine.reports.a[i];
                if (r.ep != 2 || r.cycle < 300L * 72000 || r.data == null || r.data.Length < 8 || (r.data[0] & 0xFF) != 7) continue;
                if ((r.data[1] & 1) != 0 || ((r.data[6] & 0xFF) | ((r.data[7] & 0xFF) << 8)) > 0) n++;
            }
            return n;
        }

        static void HoverPressure(string file)
        {
            InputInfo info = new InputInfo();
            byte[] app = Build.ReadInput(file, info);
            // a Q 150 pen resting at 506 kHz, 3 kHz below where pressure starts: short readings read it as pressed
            // R45: with Frequency Hold and Coil Hold the twin shows no pressure in the air even with short readings (the
            // held drive step keeps the sweep's frequency estimate steady; rc3: 0 of 800 reports, 56 with only Frequency
            // Hold off, 800 with both off). R45-rc4: a found pen is held at the slowest Steady Hover step (40 pulses)
            // until its first whole sweep, so the drive step starts right and the fast stage's short sweeps keep it
            // there: 0 even with both holds off. The control is now a pen resonating well inside the pressure band (518 kHz: 800 of 800; 511 kHz: 8).
            Machine control = Hover(Preset(app, info, "800", false, false), new TabletBoard(AirPen(150, 518000)), 1300);
            Check(Pressed(control) > 100, "twin: a pen resonating in the pressure band shows pressure (control)");
            Machine shortRead = Hover(Preset(app, info, "800", false, false), new TabletBoard(AirPen(150, 506000)), 1300);
            // R45-rc4 (Switch Match): without the holds this case is chaotic in the twin. With Switch Match's routine built in
            // but never called (only the code layout and frame timing changed) it showed pressure in 779 of 801 reports; with
            // the call, 0; without the setting, 0. So it is reported, not checked; the holds (default on) keep it at 0 below.
            Console.WriteLine("  info: RAPID, short frequency readings, no holds: pressure in " + F.Str(Pressed(shortRead)) + " reports after 300 ms (twin; chaotic without the holds)");
            Machine shortHeld = Hover(Preset(app, info, "800", false, true), new TabletBoard(AirPen(150, 506000)), 1300);
            Check(Pressed(shortHeld) == 0, "twin: with Frequency Hold and Coil Hold, short frequency readings show no pressure while the pen hovers");
            Machine accurate = Hover(Preset(app, info, "800", true), new TabletBoard(AirPen(150, 506000)), 1300);
            Check(Pressed(accurate) == 0, "twin: RAPID with Accurate Pressure Readings shows no pressure while the pen hovers");
            // Fix A: ULTRA's sweep takes 6 frames; a hover-stage switch inside one must keep it from the frequency estimate.
            // R45-rc4: a still pen no longer switches stages (dynamic Steady Hover), so this pen moves up and down through
            // the switch between the fast stage and Steady Hover (twin scale 6 <-> 18 mm, 1.2 s a round trip); until
            // rc3 it hovered still at 4 mm and the stages switched by themselves.
            Pen moving = AirPen(300, 505000);
            moving.height = 6;
            SweepBoard board = new SweepBoard(moving);
            Machine sweepRun = Hover(Preset(app, info, "1000", true), board, 0);
            for (int t = 0; t < 2300; t++)
            {
                int ph = t % 1200;
                moving.height = 6 + 12.0 * (ph < 600 ? ph : 1200 - ph) / 600.0;
                sweepRun.RunMs(1);
            }
            Check(board.mixedSeen > 0 && board.mixedEstimated == 0 && board.estimates > 100, "twin: sweeps with a hover-stage switch part-way are dropped (" + F.Str(board.mixedSeen) + " seen)");
        }

        // R42: changes another mod made at known places are edits, not damage; an erased end is damage. Uses the stock
        // fixture (either verified build) and copies of it changed the way real dumps were (Mily's: Movement Smoothing
        // off + 1 ms report interval; Mei's: scan timing; Anargon's and Anomy's: the end erased by a 35208-byte image).
        static byte[] AppOf(byte[] full) { byte[] a = new byte[0x10000 - 0x3800]; P.Copy(full, 0x3800, a, 0, a.Length); return a; }
        static string tempDir = null;
        static string TempFile(string name, byte[] data)
        {
            if (tempDir == null) { tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "r42-core " + Guid.NewGuid().ToString("N")); P.MakeDirs(tempDir); }
            string f = P.PathJoin(tempDir, name); P.WriteAllBytes(f, data); return f;
        }
        static bool Has(string s, string part) { return s != null && P.IndexOf(s, part) >= 0; }
        static string ReadFails(string f)
        {
            try { InputInfo info = new InputInfo(); Build.ReadInput(f, info); return null; }
            catch (Stop e) { return e.msg; }
        }
        static void RepairChecks(string file)
        {
            byte[] full = P.ReadAllBytes(file);
            if (full.Length != 0x10000) return;
            byte[] app = AppOf(full);
            FwCheck c = Repair.Examine(app, app.Length);
            if (c.known < 0) return;
            KnownBuild kb = Layout.KNOWN[c.known];
            int BASE = Build.BASE, end = kb.end - BASE, cut = StockRef.CUT - BASE;
            int[] at = Repair.EDIT_AT[c.known], sv = Repair.EDIT_STOCK[c.known];
            bool table = true;
            for (int j = 0; j < at.Length; j++) if ((app[at[j] - BASE] & 0xFF) != sv[j]) table = false;
            Check(table, "every known edit site holds its stock value in " + kb.build);
            Check(c.stock != null && !c.Edited() && !c.Damaged() && c.pagesOther == 0, "the stock dump is exactly stock, with no edits");
            // this tool's own patches (the ones other mods copy) only touch listed sites, and are all recognised
            byte[] stock = new byte[end];
            P.Copy(app, stock, end);
            Fw.Use(stock, kb.build);
            Image im = new Image(stock);
            Patches.SmoothingOff(im); Patches.Scan(im, 4, 20, 50, 10); Patches.AgcCap(im, 4); Patches.InterpOff(im); Patches.Binterval(im);
            bool listed = true; int changed = 0;
            for (int i = 0; i < end; i++)
            {
                if (im.data[i] == stock[i]) continue;
                changed++;
                bool hit = false;
                for (int j = 0; j < at.Length; j++) if (at[j] - BASE == i) hit = true;
                if (!hit) listed = false;
            }
            Check(listed && changed == at.Length, "the patch sites other mods change are exactly the listed sites (" + F.Str(changed) + " of " + F.Str(at.Length) + ")");
            byte[] modFull = new byte[0x10000];
            P.Copy(full, modFull, 0x10000);
            P.Copy(im.data, 0, modFull, 0x3800, end);
            FwCheck m = Repair.Examine(AppOf(modFull), 0x10000 - 0x3800);
            Check(!m.Damaged() && m.stock != null && Txt.Crc32(m.stock, 0, end) == kb.crc && m.pagesOther == 0 && m.tailOther == 0, "every one of those changes is undone to the exact stock firmware");
            string list = Repair.EditList(m);
            Check(Has(list, "Movement Smoothing off") && Has(list, "ADC samples per step 45 -> 4") && Has(list, "excitation pulses per step 40 -> 20") && Has(list, "step poll 50 -> 10 us") && Has(list, "step tick 200 -> 50 us") && Has(list, "USB report interval 3 -> 1 ms") && Has(list, "position average at rest 5 -> 1"), "the changes are named with their old and new values: " + list);
            // the flasher's input: the edited dump builds from exact stock, and is not called damaged
            string mf = TempFile("edited-main.bin", modFull);
            InputInfo info = new InputInfo();
            Build.ReadInput(mf, info);
            Check(info.undone != null && info.stock && info.crc == kb.crc && !info.truncated, "an edited dump is read as the exact stock firmware, with a note of what was undone");
            // R45 (Anargon: "say what was modified, and how"): each change with its instructions or .data bytes and
            // addresses, and the published mod it matches (Mei's and Mily's, made here with this tool's own patches)
            string det = "";
            for (int i = 0; info.undoneHow != null && i < info.undoneHow.Count(); i++) det += info.undoneHow.Get(i) + " | ";
            Check(Has(det, "ADC samples per step 45 -> 4: the number in 1 instruction of the scan step setup changed (movs at " + F.Hx(at[9 - (c.known == 0 ? 0 : 1)], 8) + ")") && Has(det, "compressed start-up data") && Has(det, "set back to its stock value"), "the notice says how each change was made: " + det);
            Check(m.origin == null, "a mix no published mod makes is not given a name");
            for (int v = 0; v < 2; v++)
            {
                Image mi = new Image(stock);
                if (v == 0) { Patches.SmoothingOff(mi); Patches.Scan(mi, 4, 16, 50, 10); Patches.Binterval(mi); }
                else { Patches.SmoothingOff(mi); Patches.Binterval(mi); }
                byte[] mf2 = new byte[0x10000]; P.Copy(full, mf2, 0x10000); P.Copy(mi.data, 0, mf2, 0x3800, end);
                FwCheck mo = Repair.Examine(AppOf(mf2), 0x10000 - 0x3800);
                string want = v == 0 ? "Mei's mod ic902-binterval1-nofilter-s4-b16-tick50-poll10.hex" : "Mily's firmware";
                Check(mo.stock != null && mo.origin != null && Has(mo.origin, want), "recognised as " + want + ": " + (mo.origin != null ? mo.origin : "(none)"));
            }
            // a page with a known edit AND another change is not explained by the known edits
            byte[] mix = new byte[0x10000]; P.Copy(modFull, mix, 0x10000);
            mix[at[0] - 0x08000000 + 1] ^= 0x01;              // the opcode byte next to a known site
            FwCheck x = Repair.Examine(AppOf(mix), 0x10000 - 0x3800);
            Check(x.pagesOther == 1 && x.stock == null && !x.Damaged(), "a page changed besides its known sites counts as changed in an unknown way");
            string xr = ReadFails(TempFile("mixed-main.bin", mix));
            Check(xr != null && Has(xr, "not erased") && !Has(xr, "DAMAGED"), "such a dump is refused as changed, not as damaged");
            // R43 review: the same unknown change with no known edit beside it (R42 built from it with no warning)
            byte[] unk = new byte[0x10000]; P.Copy(full, unk, 0x10000);
            unk[at[0] - 0x08000000 + 1] ^= 0x01;
            FwCheck u = Repair.Examine(AppOf(unk), 0x10000 - 0x3800);
            string ur = ReadFails(TempFile("unknown-main.bin", unk));
            Check(u.pagesOther == 1 && !u.Edited() && ur != null && Has(ur, "not erased") && !Has(ur, "DAMAGED"), "a dump changed only in an unknown place is refused as changed too");
            // R45 (Anargon: "It should also revert mods made BY this patcher"): every build carries an undo record, so a
            // dump of a tablet running one is set back to the exact stock firmware it was made from
            {
                InputInfo osi = new InputInfo();
                byte[] oapp = Build.ReadInput(TempFile("own-stock-main.bin", full), osi);
                bool[] ost = Cli.Defaults(); Build.ApipOff(ost); ost[Build.SetIndex("apip")] = true;
                Options oo = new Options(); oo.fallback = true; oo.apip = Apip.Fallback(Build.Lv("1000"), true);
                bool oq = Build.quiet; Build.quiet = true;
                byte[] oimg;
                try { oimg = Build.BuildOne(oapp, osi, null, Build.Lv("1000"), ost, true, false, oo).Bytes(); }
                finally { Build.quiet = oq; }
                Check(P.Eq(Layout.For(oimg, 0, oimg.Length, null).KnownByCode(), kb.build), "the undo record does not change how the build's code is found (" + kb.build + ")");
                byte[] od = new byte[0x10000];
                for (int k = 0; k < od.Length; k++) od[k] = (byte)0xFF;
                P.Copy(full, 0, od, 0, 0x3800); P.Copy(oimg, 0, od, 0x3800, oimg.Length);
                InputInfo ori = new InputInfo();
                Build.ReadInput(TempFile("own-build-main.bin", od), ori);
                string oh = "";
                for (int k = 0; ori.undoneHow != null && k < ori.undoneHow.Count(); k++) oh += ori.undoneHow.Get(k) + " | ";
                Check(ori.reverted && ori.stock && ori.crc == kb.crc && !ori.modded && Has(oh, "1 ms USB report interval") && Has(oh, "Added code and tables after the firmware end") && Has(oh, "set back from the undo record"), "a dump of a tablet running an ULTRA + ALPI build is set back to exact stock with its undo record, and says what each part changed: " + oh);
                // a damaged record is never used: the stock firmware then comes from the user's own copy (here the
                // stock dump in the same folder), and the notice says why
                int ra = -1;
                for (int k = (oimg.Length - 24) & ~3; k >= 0x4000 && ra < 0; k -= 4) if (Machine.LE(oimg, k, 4) == OwnRevert.MAGIC) ra = k;
                Check(ra > 0, "the build carries its undo record");
                od[0x3800 + ra + 40] ^= 0x10;
                InputInfo obi = new InputInfo();
                string ob = null;
                try { Build.ReadInput(TempFile("own-badrec-main.bin", od), obi); } catch (Stop e) { ob = e.msg; }
                string obh = "";
                for (int k = 0; obi.undoneHow != null && k < obi.undoneHow.Count(); k++) obh += obi.undoneHow.Get(k) + " | ";
                Check(ob == null && obi.stock && obi.crc == kb.crc && Has(obh, "undo record was not used") && Has(obh, "comes from your own copy"), "a damaged undo record is not used; the stock copy beside it is: " + (ob != null ? ob : obh));
            }
            // the real damage: a 35208-byte image (xppen-tools' old code.bin / stock.hex) flashed after the erase
            byte[] cutd = new byte[0x10000]; P.Copy(full, cutd, 0x10000);
            for (int i = 0x3800 + cut; i < 0x10000; i++) cutd[i] = (byte)0xFF;
            FwCheck d = Repair.Examine(AppOf(cutd), 0x10000 - 0x3800);
            Check(d.Damaged() && d.tailErased >= end - cut - 16 && d.pagesOther == 0, "an end erased from 0x0800C188 is damage (" + F.Str(d.tailErased) + " of " + F.Str(end - cut) + " bytes erased)");
            string dr = ReadFails(TempFile("cut-main.bin", cutd));
            Check(dr != null && Has(dr, "DAMAGED") && Has(dr, "erased"), "the flasher refuses an erased end as damaged");
            // R42: the tool stores no copy of the end; it restores it from the user's own files. With none: it stops.
            string none = P.PathJoin(P.DirName(mf), "no copies here");
            P.MakeDirs(none);
            RepairResult rr = Repair.Analyse(cutd, none, new string[] { none });
            Check(!rr.ok && rr.needCopy && rr.stockApp == null && Has(rr.why.Get(0), "no copy of your own"), "with no copy of the user's own, the recovery tool stops and asks for one");
            // the edited dump next to it (another mod's 1 ms report interval in the end): undone, it is the copy used
            rr = Repair.Analyse(cutd, P.DirName(mf), new string[] { P.DirName(mf) });
            Check(rr.ok && rr.damaged && Txt.Crc32(rr.stockApp, 0, end) == kb.crc && Has(rr.endFile, "edited-main.bin"), "the recovery tool restores the erased end to exact stock from the user's own edited dump");
            // a .hex built by this tool works as the copy too; a copy with one more changed byte in its end does not
            string hexDir = P.PathJoin(P.DirName(mf), "built image only");
            P.MakeDirs(hexDir);
            Build.WriteHex(im.data, BASE, P.PathJoin(hexDir, "rapid.hex"));
            byte[] bad = new byte[0x10000]; P.Copy(full, bad, 0x10000);
            bad[0x3800 + cut + 100] ^= 0x01;
            P.WriteAllBytes(P.PathJoin(hexDir, "a-changed-main.bin"), bad);
            rr = Repair.Analyse(cutd, hexDir, new string[] { hexDir });
            Check(rr.ok && Txt.Crc32(rr.stockApp, 0, end) == kb.crc && Has(rr.endFile, "rapid.hex"), "a built image is a valid copy, and a copy with a changed end is skipped");
            Check(Repair.EndOf(c.known, P.PathJoin(hexDir, "a-changed-main.bin")) == null && Repair.EndOf(c.known, file) != null, "a file's end is used only when its checksum matches");
            // the same erased end on an edited firmware is still repaired, with no stock copy needed
            byte[] both = new byte[0x10000]; P.Copy(modFull, both, 0x10000);
            for (int i = 0x3800 + cut; i < 0x10000; i++) both[i] = (byte)0xFF;
            rr = Repair.Analyse(both, P.DirName(mf), new string[] { P.DirName(mf) });
            Check(rr.ok && rr.damaged && P.Len(rr.edits) > 0 && Txt.Crc32(rr.stockApp, 0, end) == kb.crc, "an edited firmware with an erased end is repaired to exact stock");
            // Mily's case in the recovery tool: edited, complete, not damaged
            rr = Repair.Analyse(modFull, none, new string[] { none });
            Check(rr.ok && !rr.damaged && P.Len(rr.edits) > 0 && Txt.Crc32(rr.stockApp, 0, end) == kb.crc, "the recovery tool reports an edited dump as not damaged and writes exact stock");
            // one changed .data byte that is not a known literal (not erased): changed, not damaged
            byte[] t1 = new byte[0x10000]; P.Copy(full, t1, 0x10000);
            t1[0x3800 + cut + 40] ^= 0x10;
            FwCheck t = Repair.Examine(AppOf(t1), 0x10000 - 0x3800);
            Check(t.tailOther == 1 && !t.tailCounted && !t.endOk && !t.Damaged(), "an unknown .data change is not called damage");
            // restore images next to a dump: a 35208-byte one is renamed, a complete one is kept
            string dir = P.DirName(mf);
            byte[] code = new byte[cut]; P.Copy(full, 0x3800, code, 0, cut);
            P.WriteAllBytes(P.PathJoin(dir, "ic902-code.bin"), code);
            Build.WriteHex(code, BASE, P.PathJoin(dir, "ic902-stock.hex"));
            string ri = BackupCapture.CheckRestoreImages(dir, full);
            Check(ri != null && !P.FileExists(P.PathJoin(dir, "ic902-code.bin")) && !P.FileExists(P.PathJoin(dir, "ic902-stock.hex"))
                && P.FileExists(P.PathJoin(dir, "ic902-stock.hex.INCOMPLETE-DO-NOT-FLASH")) && Has(ri, "0x0800c188"), "35208-byte restore images next to a dump are renamed");
            byte[] whole = new byte[end]; P.Copy(full, 0x3800, whole, 0, end);
            P.WriteAllBytes(P.PathJoin(dir, "ic902-code.bin"), whole);
            Build.WriteHex(whole, BASE, P.PathJoin(dir, "ic902-stock.hex"));
            Check(BackupCapture.CheckRestoreImages(dir, full) == null && P.FileExists(P.PathJoin(dir, "ic902-stock.hex")), "complete restore images are kept");
            P.WriteAllBytes(P.PathJoin(dir, "ic902-code.bin"), code);
            Build.WriteHex(code, BASE, P.PathJoin(dir, "ic902-stock.hex"));
            Check(BackupCapture.CheckRestoreImages(dir, cutd) != null && !P.FileExists(P.PathJoin(dir, "ic902-stock.hex")), "on a damaged tablet its restore images (they stop where its flash does) are renamed too");
            // R43 review: a restore image that cannot be renamed is reported (not flashable by this tool), and does not
            // turn the backup into a failure
            string locked = P.PathJoin(dir, "locked");
            P.MakeDirs(locked);
            P.WriteAllBytes(P.PathJoin(locked, "ic902-code.bin"), code);
            P.MakeDirs(P.PathJoin(locked, "ic902-code.bin.INCOMPLETE-DO-NOT-FLASH"));    // a folder in the way: the rename fails
            string lr = null; bool threw = false;
            try { lr = BackupCapture.CheckRestoreImages(locked, full); } catch (Exception) { threw = true; }
            Check(!threw && lr != null && Has(lr, "could not be renamed") && Has(lr, "do not flash it"), "a restore image that cannot be renamed is reported, without an exception");
        }

        // R42: the line recorder (multi-coil study) copies every line of a frame at frame end and sends the copy in the
        // next five reports (pages 0xF0-0xF4); every decoded copy must be exactly one frame's lines and centre coils
        static int U16(byte[] d, int i) { return (d[i] & 0xFF) | ((d[i + 1] & 0xFF) << 8); }
        static void LineRecorder(string file)
        {
            InputInfo info = new InputInfo();
            byte[] app = Build.ReadInput(file, info);
            bool quiet = Build.quiet; Build.quiet = true;
            byte[] img, plain, plainRec, tel, telRec;
            try
            {
                Options o = new Options(); o.telemetry = true; o.lineRec = true;
                Options ot = new Options(); ot.telemetry = true;
                telRec = Build.BuildOne(app, info, null, Build.Lv("800"), Cli.Defaults(), true, false, o).Bytes();
                tel = Build.BuildOne(app, info, null, Build.Lv("800"), Cli.Defaults(), true, false, ot).Bytes();
                img = Twin.FromApp(telRec);
                Options r = new Options(); r.lineRec = true;
                plain = Build.BuildOne(app, info, null, Build.Lv("800"), Cli.Defaults(), true, false, new Options()).Bytes();
                plainRec = Build.BuildOne(app, info, null, Build.Lv("800"), Cli.Defaults(), true, false, r).Bytes();
            }
            finally { Build.quiet = quiet; }
            Check(plain.Length == plainRec.Length && telRec.Length > tel.Length, "the line recorder exists only in measurement builds");   // builds differ by their build id
            Pen pen = new Pen(11);
            pen.x = 12.3; pen.y = 7.6; pen.f0 = 505000; pen.Q = 150; pen.strength = 60000; pen.noise = 60; pen.range = Pen.RANGE_TOUCH;
            pen.hasHeight = true; pen.height = 2; pen.resonator = true; pen.integrate = true;
            FrameLinesBoard b = new FrameLinesBoard(pen);
            Machine mc = Hover(img, b, 800);
            int copies = 0, matched = 0, broken = 0, expect = 0;
            byte[][] pg = new byte[5][];
            for (int i = 0; i < mc.reports.n; i++)
            {
                Report rep = mc.reports.a[i];
                if (rep.ep != 2 || rep.data == null || rep.data.Length < 10 || (rep.data[0] & 0xFF) != 7) continue;
                int k = (rep.data[9] & 0xFF) - 0xF0;
                if (k < 0 || k > 4) continue;                       // odd frames carry the usual pages
                if (k != expect) { broken++; expect = 0; if (k != 0) continue; }
                pg[k] = rep.data; expect = k + 1;
                if (k < 4) continue;
                expect = 0; copies++;
                int[] v = new int[] { U16(pg[0], 2), U16(pg[0], 4), U16(pg[0], 6), U16(pg[1], 2), U16(pg[1], 4), U16(pg[1], 6), U16(pg[2], 2), U16(pg[2], 4), U16(pg[2], 6), U16(pg[3], 2), pg[1][8] & 0xFF, pg[2][8] & 0xFF };
                for (int f = b.frames.Count - 1; f >= 0; f--)
                {
                    bool same = true;
                    for (int q = 0; q < 12 && same; q++) if (b.frames[f][q] != v[q]) same = false;
                    if (same) { matched++; break; }
                }
            }
            Check(copies > 40 && matched == copies && broken == 0, "line recorder: each copy is one frame's ten lines and centre coils (" + F.Str(matched) + " of " + F.Str(copies) + ")");
        }

        // R42: the signatures are fingerprints (no firmware bytes), and the SHA-256 behind them gives the published answers
        static void NoVendorBytes()
        {
            Check(Sha256.Hex(Sha256.Hash(new byte[0], 0, 0)) == "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
                && Sha256.Hex(Sha256.Hash(P.Utf8Bytes("abc"), 0, 3)) == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
                && Sha256.Hex(Sha256.Hash(P.Utf8Bytes("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq"), 0, 56)) == "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1",
                "SHA-256 gives the FIPS 180-4 test answers (empty, abc, two blocks)");
            bool form = true;
            for (int i = 0; i < FwSigs.S.Length; i++)
            {
                string[] f = P.Split(FwSigs.S[i], '|');
                if (f.Length != 6 || P.Len(f[3]) != 4 || P.Len(f[4]) != 16) { form = false; continue; }
                for (int k = 0; k < P.Len(f[2]); k++) { int ch = P.CharAt(f[2], k); if (ch != 'x' && ch != '.') form = false; }
            }
            Check(form, "every signature is a mask and a fingerprint, with no firmware bytes (" + F.Str(FwSigs.S.Length) + ")");
            Check(StockRef.END_CRC.Length == Layout.KNOWN.Length && StockRef.PAGE_CRC.Length == Layout.KNOWN.Length, "the stock reference holds checksums only, one set per verified build");
        }

        // R43: line copies recorded on Anargon's tablet (rev-output/hw43: still/tip-mid-rapid, move/fast-h1,
        // still/hover-ultra, still/tip-edge-fast; 16 evenly spaced calls each), run through the Python specification
        // experiments/mcint.py: lines c-2..c+2, centre coil, peak line, reads, axis -> result (-1: the stock routine runs)
        static readonly int[][] MC_VECTORS = new int[][] {
            new int[] { 400, 640, 680, 512, 312, 17, 3, 5, 0, 17015 },
            new int[] { 416, 640, 688, 520, 312, 17, 3, 5, 0, 17004 },
            new int[] { 400, 632, 664, 512, 312, 17, 3, 5, 0, 17016 },
            new int[] { 416, 640, 680, 504, 312, 17, 3, 5, 0, 16978 },
            new int[] { 400, 640, 688, 512, 312, 17, 3, 5, 0, 17018 },
            new int[] { 416, 640, 688, 512, 312, 17, 3, 5, 0, 16992 },
            new int[] { 408, 640, 680, 512, 312, 17, 3, 5, 0, 17002 },
            new int[] { 416, 640, 688, 512, 312, 17, 3, 5, 0, 16992 },
            new int[] { 400, 640, 688, 512, 312, 17, 3, 5, 0, 17018 },
            new int[] { 416, 640, 688, 512, 312, 17, 3, 5, 0, 16992 },
            new int[] { 400, 640, 688, 520, 312, 17, 3, 5, 0, 17029 },
            new int[] { 416, 640, 688, 512, 312, 17, 3, 5, 0, 16992 },
            new int[] { 400, 640, 680, 512, 312, 17, 3, 5, 0, 17015 },
            new int[] { 416, 640, 680, 512, 312, 17, 3, 5, 0, 16989 },
            new int[] { 400, 640, 688, 512, 312, 17, 3, 5, 0, 17018 },
            new int[] { 416, 640, 688, 512, 312, 17, 3, 5, 0, 16992 },
            new int[] { 1856, 4992, 6488, 5064, 1808, 4, 3, 10, 0, 4074 },
            new int[] { 1712, 5312, 7400, 5856, 1400, 17, 3, 10, 1, 17329 },
            new int[] { 1928, 4488, 5208, 640, 632, 32, 3, 10, 0, 32366 },
            new int[] { 1392, 4272, 6816, 6520, 3304, 14, 3, 10, 1, 14631 },
            new int[] { 2048, 4360, 4792, 3016, 872, 27, 3, 10, 0, 27161 },
            new int[] { 1400, 3576, 5248, 4312, 1568, 10, 3, 10, 1, 10299 },
            new int[] { 1768, 4584, 4984, 3312, 896, 5, 3, 10, 0, 4834 },
            new int[] { 2280, 5280, 5800, 3792, 816, 6, 3, 10, 1, 5812 },
            new int[] { 632, 3568, 6088, 4728, 1504, 2, 3, 10, 0, 2187 },
            new int[] { 624, 3632, 5176, 2896, 616, 2, 3, 10, 1, 1801 },
            new int[] { 640, 3144, 6008, 5064, 2008, 2, 3, 10, 0, 2343 },
            new int[] { 2408, 5440, 6384, 4176, 840, 7, 3, 10, 1, 6865 },
            new int[] { 1416, 3992, 5376, 4096, 1352, 28, 3, 10, 0, 28510 },
            new int[] { 960, 2880, 4760, 5176, 2936, 10, 4, 10, 1, -1 },
            new int[] { 1880, 4136, 4616, 3024, 920, 27, 3, 10, 0, 27205 },
            new int[] { 2512, 5616, 6424, 4392, 1056, 12, 3, 10, 1, 11965 },
            new int[] { 224, 232, 232, 232, 216, 16, 2, 4, 0, -1 },
            new int[] { 216, 216, 232, 232, 232, 8, 3, 4, 1, 9274 },
            new int[] { 216, 232, 232, 232, 224, 15, 2, 4, 0, -1 },
            new int[] { 224, 224, 232, 224, 224, 9, 3, 4, 1, 9162 },
            new int[] { 224, 232, 232, 232, 224, 15, 2, 4, 0, -1 },
            new int[] { 224, 224, 240, 240, 224, 9, 3, 4, 1, 9588 },
            new int[] { 216, 216, 224, 224, 232, 17, 5, 4, 0, -1 },
            new int[] { 216, 224, 224, 240, 232, 9, 4, 4, 1, -1 },
            new int[] { 224, 224, 240, 232, 232, 16, 3, 4, 0, 16852 },
            new int[] { 216, 216, 224, 232, 224, 9, 4, 4, 1, -1 },
            new int[] { 224, 224, 232, 232, 224, 16, 3, 4, 0, 16682 },
            new int[] { 216, 224, 232, 240, 232, 9, 4, 4, 1, -1 },
            new int[] { 224, 232, 232, 240, 232, 17, 4, 4, 0, -1 },
            new int[] { 224, 224, 240, 232, 224, 9, 3, 4, 1, 9452 },
            new int[] { 240, 240, 232, 232, 216, 15, 1, 4, 0, -1 },
            new int[] { 224, 232, 232, 224, 224, 8, 2, 4, 1, -1 },
            new int[] { 888, 1256, 1528, 1440, 1016, 30, 3, 10, 0, 30784 },
            new int[] { 648, 1248, 1528, 1472, 1224, 2, 3, 10, 1, 2514 },
            new int[] { 936, 1272, 1440, 1424, 888, 30, 3, 10, 0, 30650 },
            new int[] { 648, 1264, 1512, 1464, 1232, 2, 3, 10, 1, 2512 },
            new int[] { 928, 1256, 1456, 1376, 1000, 30, 3, 10, 0, 30731 },
            new int[] { 648, 1248, 1480, 1464, 1248, 2, 3, 10, 1, 2540 },
            new int[] { 944, 1256, 1472, 1376, 984, 30, 3, 10, 0, 30713 },
            new int[] { 648, 1256, 1528, 1464, 1224, 2, 3, 10, 1, 2507 },
            new int[] { 1192, 1408, 1360, 960, 648, 30, 2, 10, 0, -1 },
            new int[] { 648, 1256, 1504, 1448, 1232, 2, 3, 10, 1, 2514 },
            new int[] { 928, 1272, 1448, 1400, 1000, 30, 3, 10, 0, 30735 },
            new int[] { 648, 1264, 1504, 1488, 1192, 2, 3, 10, 1, 2493 },
            new int[] { 944, 1040, 1496, 1408, 992, 30, 3, 10, 0, 30903 },
            new int[] { 648, 1248, 1520, 1464, 1232, 2, 3, 10, 1, 2516 },
            new int[] { 936, 1256, 1448, 1400, 992, 30, 3, 10, 0, 30737 },
            new int[] { 648, 1248, 1528, 1472, 1232, 2, 3, 10, 1, 2516 },
        };

        // R43: 12-bit Readings - on by default where the scan is paced, the one stock instruction that clears the low 3
        // bits replaced, the readings no longer multiples of 8, and the pen tracked as with rounded readings (the
        // levels are raised by FULLRES_BIAS to match)
        static void FullResChecks(string file)
        {
            bool[] d = Cli.Defaults();
            Check(d[Build.SetIndex("fullres")], "R43: 12-bit Readings is on by default");
            Check(Build.Unavailable("fullres", Build.Lv("220"), d) != null, "R43: STOCK keeps the vendor's rounded readings");
            Check(Build.Unavailable("fullres", Build.Lv("300"), d) == null && Build.Unavailable("fullres", Build.Lv("500"), d) == null
                && Build.Unavailable("fullres", Build.Lv("800"), d) == null && Build.Unavailable("fullres", Build.Lv("1000"), d) == null, "R43: every paced preset offers 12-bit Readings");
            InputInfo info = new InputInfo();
            byte[] app = Build.ReadInput(file, info);
            bool[] off = Cli.Defaults(); off[Build.SetIndex("fullres")] = false;
            byte[] on12, rounded;
            bool quiet = Build.quiet; Build.quiet = true;
            try
            {
                on12 = Twin.FromApp(Build.BuildOne(app, info, null, Build.Lv("1000"), d, true, false, new Options()).Bytes());
                rounded = Twin.FromApp(Build.BuildOne(app, info, null, Build.Lv("1000"), off, true, false, new Options()).Bytes());
            }
            finally { Build.quiet = quiet; }
            Check(Patches.FullResIn(on12) && !Patches.FullResIn(rounded) && !Patches.FullResIn(Twin.LoadImage(file)), "R43: only a 12-bit build replaces the rounding instruction");
            long[] n = new long[2], odd = new long[2], tracked = new long[2];
            byte[][] imgs = new byte[][] { rounded, on12 };
            for (int k = 0; k < 2; k++)
            {
                Pen pen = new Pen(11);
                pen.x = 12.3; pen.y = 7.6; pen.f0 = 505000; pen.Q = 150; pen.strength = 60000; pen.noise = 60; pen.range = Pen.RANGE_TOUCH;
                pen.hasHeight = true; pen.height = 2; pen.resonator = true; pen.integrate = true;
                SampleBitsBoard b = new SampleBitsBoard(pen);
                Machine mc = Hover(imgs[k], b, 600);
                n[k] = b.n; odd[k] = b.odd;
                for (int i = 0; i < mc.reports.n; i++) if (mc.reports.a[i].cycle >= 300L * 72000 && Twin.Tracked(mc.reports.a[i])) tracked[k]++;
            }
            Check(n[0] > 1000 && odd[0] == 0, "R43: with rounded readings every reading is a multiple of 8 (" + F.Str(n[0]) + " readings)");
            Check(n[1] > 1000 && odd[1] * 2 > n[1], "R43: with 12-bit Readings most readings keep their low bits (" + F.Str(odd[1]) + " of " + F.Str(n[1]) + ")");
            Check(tracked[0] > 200 && tracked[1] * 100 >= tracked[0] * 95 && tracked[1] * 100 <= tracked[0] * 105,
                "R43: the hovering pen is tracked as with rounded readings (" + F.Str(tracked[1]) + " vs " + F.Str(tracked[0]) + " reports)");
        }

        static void MultiCoilChecks(string file)
        {
            int bad = 0, fit = 0;
            for (int i = 0; i < MC_VECTORS.Length; i++)
            {
                int[] v = MC_VECTORS[i];
                int want = v[9] < 0 ? MultiCoil.STOCK : v[9];
                if (MultiCoil.Calc(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], false) != want) bad++;
                if (want != MultiCoil.STOCK) fit++;
            }
            Check(bad == 0, "R43: Multi-coil Read's C# copy equals its specification on " + F.Str(MC_VECTORS.Length) + " recorded line copies");
            Check(fit * 2 > MC_VECTORS.Length, "R43: the recorded copies exercise the fit, not only the stock routine");
            int S = MultiCoil.STOCK, F5 = MultiCoil.Floor(5, false);
            Check(MultiCoil.Calc(300, 600, 700, 500, 300, 12, 2, 5, 0, false) == S, "R43: a peak off the centre line goes to the stock routine");
            Check(MultiCoil.Calc(300, 600, 700, 500, 300, 12, 3, 0, 0, false) == S, "R43: no reads per line goes to the stock routine");
            Check(MultiCoil.Calc(300, 750, 700, 500, 300, 12, 3, 5, 0, false) == S && MultiCoil.Calc(300, 500, 700, 750, 300, 12, 3, 5, 0, false) == S,
                "R43: a neighbour line above the centre line goes to the stock routine");
            Check(MultiCoil.Calc(300, 700, 700, 700, 300, 12, 3, 5, 0, false) == S, "R43: both neighbours equal to the centre line go to the stock routine");
            Check(MultiCoil.Calc(F5, F5, F5 + MultiCoil.VC_MIN - 1, F5, F5, 12, 3, 5, 0, false) == S && MultiCoil.Calc(F5, F5, F5 + MultiCoil.VC_MIN, F5, F5, 12, 3, 5, 0, false) != S,
                "R43: a centre line less than " + F.Str(MultiCoil.VC_MIN) + " above the floor goes to the stock routine");
            Check(MultiCoil.Calc(300, 600, F5 + (1 << 20) + MultiCoil.VC_MIN, 500, 300, 12, 3, 5, 0, false) == S, "R43: an absurdly large centre line goes to the stock routine");
            // at the edges the lines of coils that do not exist are left out: the last X coil has none past it (d, e),
            // the first Y coil none before it (a, b), so changing e or a changes nothing
            int last = MultiCoil.Calc(5000, 6000, 7000, 3000, 900, 32, 3, 10, 0, false), first = MultiCoil.Calc(900, 3000, 7000, 6000, 5000, 1, 3, 10, 1, false);
            Check(last != S && last == MultiCoil.Calc(5000, 6000, 7000, 3000, 1, 32, 3, 10, 0, false) && first != S && first == MultiCoil.Calc(1, 3000, 7000, 6000, 5000, 1, 3, 10, 1, false),
                "R43: at the first and last coil the fit applies and leaves out the lines of coils that do not exist");
            bool[] d = Cli.Defaults();
            Check(!d[Build.SetIndex("multicoil")], "R43: Multi-coil Read is off by default (experimental, not tried on a tablet)");
            Check(Build.Unavailable("multicoil", Build.Lv("220"), d) != null && Build.Unavailable("multicoil", Build.Lv("300"), d) != null, "R43: not offered below 500 reports per second");
            Check(Build.Unavailable("multicoil", Build.Lv("500"), d) == null && Build.Unavailable("multicoil", Build.Lv("800"), d) == null
                && Build.Unavailable("multicoil", Build.Lv("1000"), d) == null, "R43: FAST, RAPID and ULTRA offer Multi-coil Read");
            InputInfo info = new InputInfo();
            byte[] app = Build.ReadInput(file, info);
            bool wasQuiet = Build.quiet; Build.quiet = true;
            try
            {
                byte[] plain = Twin.FromApp(Build.BuildOne(app, info, null, Build.Lv("800"), d, true, false, new Options()).Bytes());
                Check(!MultiCoil.Installed(plain), "R43: a build without it keeps both stock position calls");
                bool[] st = Cli.Defaults(); st[Build.SetIndex("multicoil")] = true;
                string[] keys = new string[] { "500", "800", "1000" };
                for (int k = 0; k < keys.Length; k++)
                {
                    byte[] img = Twin.FromApp(Build.BuildOne(app, info, null, Build.Lv(keys[k]), st, true, false, new Options()).Bytes());
                    Check(MultiCoil.Installed(img), "R43: " + keys[k] + " build calls Multi-coil Read");
                    McResult r = MultiCoil.TwinCheck(img, new double[] { 0, 6 }, 300, 300);
                    Check(r.calls > 300 && r.miss == 0 && r.fit * 4 > r.calls, "R43: " + keys[k] + ": the firmware routine equals its C# copy on every one of " + F.Str(r.calls)
                        + " position calls" + (r.first != null ? " (first difference: " + r.first + ")" : ""));
                    if (k == 0)
                    {
                        // R43: the floor follows 12-bit Readings (3.5 per read higher); a rounded build checks the other one
                        Check(Patches.FullResIn(img), "R43: the default Multi-coil build has 12-bit Readings");
                        bool[] st0 = Cli.Defaults(); st0[Build.SetIndex("multicoil")] = true; st0[Build.SetIndex("fullres")] = false;
                        byte[] img0 = Twin.FromApp(Build.BuildOne(app, info, null, Build.Lv(keys[k]), st0, true, false, new Options()).Bytes());
                        McResult r0 = MultiCoil.TwinCheck(img0, new double[] { 0, 6 }, 300, 300);
                        Check(!Patches.FullResIn(img0) && r0.calls > 300 && r0.miss == 0 && r0.fit * 4 > r0.calls, "R43: " + keys[k] + " with rounded readings: the firmware routine equals its C# copy on every one of "
                            + F.Str(r0.calls) + " position calls" + (r0.first != null ? " (first difference: " + r0.first + ")" : ""));
                    }
                    if (k != keys.Length - 1) continue;
                    // the frame cost, measured alone (no interrupts, which make the twin's costliest frame vary by tens
                    // of cycles): the fit path must be cheaper than the stock routine, and the gates on the stock path
                    // (X and Y) must fit the MC_US charged
                    McCost c = MultiCoil.Cost(img);
                    Check(c.miss == 0 && c.fit > 0, "R43: every one of " + F.Str(c.n) + " single-stepped position calls gives Calc's or the stock routine's result"
                        + (c.firstMiss != null ? " (first: " + c.firstMiss + ")" : ""));
                    Check(c.fitMax < c.stockMax, "R43: the fit path (at most " + F.Str(c.fitMax) + " cycles) costs less than the stock routine (" + F.Str(c.stockMax) + ")");
                    Check(2 * c.gateMax <= MultiCoil.MC_US * 72, "R43: the gates add at most " + F.Str(c.gateMax) + " cycles per axis, within the " + F.Str(MultiCoil.MC_US) + " us charged");
                }
            }
            finally { Build.quiet = wasQuiet; }
        }

        static void Firmware(string file)
        {
            if (Group("fw-repair")) RepairChecks(file);
            if (Group("fw-boot"))
            {
                byte[] image = Twin.LoadImage(file);
                long before = Txt.Crc32(image, 0, image.Length);
                Out output = new Out(); output.buf = new StringBuilder();
                Check(Twin.Describe(image, file, output), "stock boots and enumerates");
                Check(Twin.AgcCheck(image, output) == 1, "stock gain control is checked");
                SensJobT first = new SensJobT(), second = new SensJobT();
                first.img = image; first.f = 503000; second.img = image; second.f = 518000;
                P.RunParallel(new Job[] { first, second }, 2);
                Check(first.r != null && first.r.n > 0 && second.r != null && second.r.n > 0, "parallel checks both track a pen");
                Check(Txt.Crc32(image, 0, image.Length) == before, "checks leave the input firmware unchanged");
                UsbFirmwareRejections(image);
                bool[] ok = new bool[1];
                Twin.ScanReport(image, 40, output, false, ok);
                Check(ok[0], "scan check receives tracked pen reports");
            }
            if (Group("fw-cleanup")) Cleanup(file);
            if (Group("fw-hover-pressure")) HoverPressure(file);
            if (Group("fw-line-recorder")) LineRecorder(file);
            if (Group("fw-multicoil")) MultiCoilChecks(file);
            if (Group("fw-fullres")) FullResChecks(file);
        }

        public static int Main(string[] args)
        {
            try
            {
                if (Group("branches")) Branches();
                if (Group("flash-isolation")) FlashIsolation();
                if (Group("hex-inputs")) HexInputs();
                if (Group("stock-restoration")) StockRestoration();
                if (Group("position-reports")) PositionReports();
                if (Group("usb-schema")) UsbSchema();
                if (Group("accurate-levels")) AccurateLevels();
                if (Group("no-vendor-bytes")) NoVendorBytes();
                if (Group("twin-basics"))
                {
                    Out output = new Out(); output.buf = new StringBuilder();
                    Check(Twin.Selftest(output), "deterministic numeric self-test");
                    output.Say("RESULT run fail");
                    Check(output.failed, "scenario failures reach the command exit status");
                    Check(Twin.AgcCheck(BlankFlash(), output) == -1, "missing AGC site is explicitly unverified");
                    Check(Twin.Run(new string[] { "unused.bin", "--checks", "typo" }) == 2, "unknown check cannot pass without running a test");
                    Check(Twin.Run(new string[] { "unused.bin", "--run", "10", "--every", "0.5" }) == 2, "sub-millisecond output interval cannot divide by zero");
                }
                if (args.Length > 0) Firmware(args[0]);
                P.Line("core regressions: " + F.Str(checks) + " passed");
                return 0;
            }
            catch (Stop e) { P.Line(e.msg); return 1; }
            catch (Fault e) { P.Line("FAIL: " + e.msg); return 1; }
        }
    }
}
