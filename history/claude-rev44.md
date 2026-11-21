# R44 - Claude Opus 5.5 (Anthropic), 2026-11-10

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

Model: Claude Opus 5.5 (`claude-opus-5-5`), Anthropic. Same conversation as R42 and R43. Requests are Anargon's.
R44 is a copy of R43 (`rev-output/R43`) with the changes below. The spec is R43 history Part 12.

## Part 1: simpler calibration

### Request

Anargon's R44 spec (2026-11-10), in short:

- **Zone markers:** only for the steps that already had a target position (the middle, and later bottom left and so
  on). The LED turns on only while the cursor is within 7 mm each way (14 x 14 mm) of the drawing area's true
  centre, and blinks only while the step is recording.
- **No-cursor warning:** calibration must say there is no cursor, so pen-button presses and the mouse do not
  interrupt it.
- **Minimal pressure:** 4096 ("2048 is now too light").
- **Detection limit, worded as given:** "Move the pen up until the point where the LED stops shining, then bring it
  down until it starts shining again. This is your tablet's detection limit. Hold it at that position during
  calibration."
- **Slow lift:** not timed; resonance-strength dependent; stop collecting as soon as the pen is lost.
- **Tablet driver:** say it simply: exit the OpenTabletDriver window, or XP-Pen's driver from the system tray.

Then: "Ok, start on 44."

### What was done

**Firmware (calibration-pass build only; `atf_sched.cs`, `atf_patcher.cs`):**

- **LED zone state** at RAM `0x20000F54` (`Sched.ZONE_ADDR`, in the free bytes after VQ):
  - +0 state: bit 0 recording, bits 1-3 target (0 none, 1 the drawing area's centre);
  - +1 LED: 0 off, 1 on, 2 blinking;
  - +2 state ^ 0xA5. RAM survives an update and is not cleared at power-up, so the state only counts when this
    matches. Until the PC sets a zone, the LED is on while the pen is tracked.
- **The PC sets it through USB strings 176-179** (`176 + 2 x target + recording`). `Patches.CounterString` answers
  them only in the calibration-pass build, with `ADTZ1:<target><recording>`, so the PC knows the tablet took it.
  Every other index, and these indices on every other build, still stall. The stall path keeps r0 / r1 (the stall's
  arguments) untouched; the stock handler does not use 176-179 (checked against its index chain: 4, 5, 20-31, 98,
  100-105, 109-111, 120-124, 131, 168, 200).
- **Each pen report decides the LED** (`tsend`, before the telemetry pages replace the report's bytes): off unless
  the status says in range (bit 5); with a target, off unless the report's X and Y are both within 700 units
  (7.00 mm) of (8000, 4500); otherwise on, or blinking while recording.
- **`ledtick`** (every frame) and **`ledgate`** (the stock LED calls) apply that byte; **`ledsearch`** (search
  restart: no pen) clears it, so the LED goes off when the pen is lost. Before R44 this build kept the LED lit
  without a pen and blinked it whenever the pen was tracked.

**PC (`atf_cli.cs`):**

- `CalSource.Zone(target, recording)`; `HidSource` sends it with `P.HidIndexedString` and checks the reply,
  `TwinSource` through the twin's USB host. Each step sets its target before its instructions (the no-pen step has
  none, steps 2-5 the middle), switches recording on for its settling and measured readings, then off. The readiness
  check sets the middle target. If the tablet does not confirm, the step says once that the LED only shows whether
  it sees the pen.
- **No-cursor warning** before flashing the measurement firmware, at the readiness check and on the first step;
  later steps carry a one-line reminder. Every step also shows the LED legend.
- **4096 light touch:** `Cli.LIGHT_TOUCH` replaces every 2048 in the calibration code (live readout, `Decode`,
  readiness and step messages).
- **Detection limit:** Anargon's sentence, after "Start at normal hover over the middle of the drawing area."
- **Untimed slow lift:** `CalSource.CollectUntilLost` collects from the surface until the first 0.25 s with no
  calibration report after the pen was tracked (the firmware only sends them while it tracks the pen), at most
  60 s (`LIFT_MAX_SECS`). The readout counts seconds up and says when the tablet lost the pen. The twin's lift
  rises 4 mm/s and stops 250 ms after the last tracked report.
- **Driver wording** (`DRIVER_EXIT`): "Exit your tablet driver: close the OpenTabletDriver window, or right-click
  XP-Pen's icon (XPPenTablet) in the system tray, next to the clock, and exit it." Before flashing, in the
  readiness check's no-reports message and in the step messages.

### Found while testing

- **The twin's USB host froze a running scan machine.** `VirtualHost` runs the USB interrupt to completion at
  once; when the CPU was already inside another interrupt, the emulator finished that one too, outside its run
  loop's bookkeeping, and then took no more interrupts (the millisecond counter stopped). `TwinSource.ToMainLoop`
  now steps the machine to its main loop, with interrupts enabled, before each request. The emulator is unchanged.
- **A stall reaches the twin's host as an empty answer,** not an exception.

### Results (twin)

| Check | Result |
|---|---|
| Pen over the centre (coils 16.44, 10.12), QUICK / FAST / RAPID / ULTRA | reports 7998-7999, 4496-4497 |
| Before the PC sets a zone, pen tracked | LED on 400/400 ms |
| Centre target, pen inside | on |
| Recording, pen inside | blinks (400-600 of 1000 ms) |
| Pen 9 mm right (reports 8905-8909) | off (0/400 ms) |
| Pen 5 mm right | inside: blinks |
| No target, recording, pen anywhere | blinks |
| No pen (with or without recording) | off (0/400 ms) |
| String 180 (past the range) | stalls |
| Normal build, string 179 | stalls |
| Slow lift, FAST | 1065 signal readings, lost after 5.95 s (23.8 mm in the twin: its heights are relative) |
| Five steps played in the twin, FAST | ok, pick 230, drop 177 |

Calibration suite: 187 checks pass with February stock. Two older checks used 3000 as "heavy pressure"; they now use
5000 (above 4096). Full suites with February and December: see Part 2.

### Not done

- Not run on the tablet: the measurement firmware with the zone, the HID string requests while Raw Input reads, and
  the untimed lift with a real hand.
- Whether closing the OpenTabletDriver window also stops its background daemon was not checked; the readiness check
  still catches a driver that holds the tablet.
- APIP (Anargon's noise-reversal plan) is not started; its centre hold will use the same box.

## Part 2: folders, patcher packages and wording

### Requests

- **Tablet result (Anargon, 2026-11-10):** "1000hz is stable with 12 byte ADC and matches prior 500hz performance.
  Note: this is without multicoil read, and is supposedly the lower noise build." (12-bit Readings, ULTRA, no
  Multi-coil Read.)
- **Folder fix:** "Folder structure is now fully messed up because you started working outside of the work area.
  Can you fix ./ please?"
- **The flow:** "Continue. This might be the last revision, so after you do everything, do a quick CI pass." Steps:
  0. make `./patcherpkgs`, `./import`, `./export` and the other folders at once; 0b. a patcher-package selector (C#
  only); 1. choose the tool; 2. input file; 3. output folder; 4 onwards: the other steps, reworded after Anargon's
  examples ("Input File Directory: >", "Output Folder: >", short warnings).

### What was done

**Folder fix (outside this tree).** On 2026-11-09 (R42) Claude had moved 14 cut restore images (35208 bytes, they end
at `0x0800C188`) out of Anargon's folders into a new `QUARANTINE-cut-firmware-DO-NOT-FLASH` folder. Each was moved
back to its own folder and renamed `<name>.INCOMPLETE-DO-NOT-FLASH`, the name this tool gives such files since R42,
so none can be flashed by mistake. SHA-256 checked before and after each move; the quarantine folder is gone.
Record: `Claude-workarea/rev-output/cut-firmware-record/` (README.txt, MOVED.tsv, RESTORED.tsv, RESTORED.txt;
removing the suffix gives the original name). Nothing else outside the work area was touched.

**Folders (`atf_packages.cs`, `Folders`).** At start (no arguments, and the wizard) the tool makes `import`, `export`,
`dumps` and `patcherpkgs` next to the program. Step 1 looks in `import` first (`PickInput`: the latest backup, then
`ic902-main.bin`, then `deco01v3-main.bin`, each in `import`, the current folder, the output folder, the program's
folder) and takes a typed folder (`Cli.InputInFolder`). Step 2's Enter is `export`.

**Patcher packages (`atf_packages.cs`, `Packages`; `DOCS/patcher-packages.md`).** rev36b-rc had them; RC37 dropped
them (R42 Part 1 listed what a return needs). When `patcherpkgs` holds a package, the first screen is
"Choose the patcher:" (1 = this tool); with none it is not shown.

- **C# source only:** a `.patcherpkg` ZIP or a folder with `package.ini` (`format=1`, `name`, `entry`, optional
  `revision`), `src/**.cs` and an optional README. Anything else (a `.dll`, an `.exe`, a script, `..` or absolute
  paths, duplicates) skips the package, and the selector says why.
- **The ZIP is read in-house** (`System.IO.Compression.DeflateStream` from System.dll; no new reference): stored and
  deflate only, CRC-32 checked, no ZIP64 or encryption, 512 files, 16 MB.
- **Approval:** the first run of each exact content shows a full-screen page (full access, can flash without this
  tool's checks) and needs `I know what I am doing.` typed. The SHA-256 covers the sorted names, sizes and bytes, so
  the ZIP and folder forms match; it is stored in `Store` (`package_<sha>`) and checked again just before compiling.
- **Compile:** `csc.exe` from the running .NET Framework's folder (`RuntimeEnvironment.GetRuntimeDirectory`), never
  from PATH; `/langversion:5`, a reference to this program; sources through a response file in a temporary folder
  that is deleted afterwards. Nothing runs if it does not compile. The .NET 8 builds have no compiler and say so.
- **Run:** `Assembly.Load`, `public static int Run(string[] args)`; its return value is the exit code.
- **Not done:** signing (there is no key infrastructure, and a signature check in an unsigned open-source tool proves
  little), and enforcing the gate: a package runs in this process, so it can call xppen-tools itself. The approval
  page and the doc say so; the doc asks authors to flash only through `Cli.Gate`, `Cli.BeforeFlash` and
  `Cli.FlashSteps`.

**Wording.** After Anargon's examples: "Input File Directory: >", "Output Folder: >", "Enter = <full path>", one
short paragraph per step on what it does, plainer preset names ("XP-Pen's own timing", "low delay", "lowest delay,
most noise, shorter pressure readings"), "Patcher (Enter = 1): >", "Tool (1 or 2): >". The startup-backup warning
lost its second sentence. The repair message reads "It can still be used: those changes were undone ..." (Anargon's
example had "While this firmware can be useddamage:").

**Volatile Changes warning (new, every step).** Anargon's text: "Volatile changes change how the tablet enumerates
with your system, making an incomplete flash much more likely to permanently brick your tablet." Used: "Volatile
Changes change how the tablet connects to your system (a USB report every 1 ms). They are in the part of the firmware
that an incomplete flash loses, so only flash complete files made by this tool. A stock firmware update undoes them."
Why: an incomplete flash stops the tablet with or without Volatile Changes, and the bootloader survives it, so the
tablet can be reflashed (rev 3 - 5, rev 35, R42). "Permanently" and "more likely" would be wrong.

### Found while testing

- **The warnings changed order between steps:** replacing the update count moved it to the end. `Screen.SetWarn` now
  replaces it in place.
- A compiler warning in the ZIP reader's 32-bit read (sign extension; harmless for the values read) was removed.

### Results

| Check | Result |
|---|---|
| Suites, February stock | core 205, host 112, calibration 187: all pass |
| Suites, December stock | core 205, host 112, calibration 187: all pass |
| New host checks (11) | the four folders made; no selector when empty; a package read from a folder and from a ZIP (stored and deflated) with one hash; a `.dll`, a loose `.exe`, a `..` path and a `.py` refused with the reason; approval, compile, run and exit code 7; same content runs without asking; a changed package asked again and not run without the phrase; a package that does not compile runs nothing |
| `twin --selftest` (Windows build) | passes |
| Screens, piped answers in a scratch folder with no xppen-tools | folders made, selector, approval page, a sample package compiled and ran; Steps 1-5, build and flash prompt worded as above; a typed folder at Step 1 found its file, an empty one gave the warning |
| Source and docs | ASCII (only the rev 1 - 23 records hold older non-ASCII text, as in R43); line endings as in R43 |

The calibration suite builds and runs the pass-1 build in the twin, so the pass-1 gate is covered.

### Not done

- Not run on the tablet: anything in Part 2 (the screens only read the tablet's update count, USB string 173).
- The .NET 8 build was not compiled here (no .NET SDK on this PC); GitHub's Linux job would.
- **Algorithmic Position Interpolation (APIP) is not in R44** (Anargon noticed, 2026-11-10). It was in the R44 spec
  (R43 history Part 12) and is still open.
