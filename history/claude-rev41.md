# R41 - Claude revision record

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

Contributor: Claude Opus 5.5 (Anthropic, `claude-opus-5-5`). Date: 2026-11-08. Input: the R40-rc source ZIP. Read
`history/claude-rev40.md` first; this record covers only what R41 changed. Work was done on Anargon's Windows 11 PC,
with the real tablet plugged in. Parts 1-4 used it read-only; for Part 5 Anargon authorised flashing (4 updates).

## Reported

`--hid-check` and the calibration readiness check printed "No input reports received. no reports at all from 1 open
collection(s)" with stock, modded and calibration firmware, while OpenTabletDriver saw the tablet and the same program
worked in a Linux VM with USB passthrough. Anargon asked for the cause and a full rewrite of the reading step.

## How the cause was found

1. Windows' device records (`HKLM\SYSTEM\CurrentControlSet\Enum\HID\VID_28BD&PID_2904*`) list the tablet as
   MI_00 Col01/Col02 mouse (0001:0002), Col03 keyboard (0001:0006); MI_01 **HID-compliant pen (000D:0002)**;
   MI_02 vendor-defined (FF0A:0001). The twin's USB validator puts endpoint 0x82 (report mode 2, 10-byte ID 7) on
   interface 1 and 0x83/0x03 (mode 4, 12-byte ID 2) on interface 2.
2. A read-only probe (`Claude-workarea/experiments/hidprobe.cs`, not part of this repository) listed every HID
   collection, tried the same `CreateFile(GENERIC_READ, share read/write)` as R40, read the openable tablet
   collections with ReadFile and, at the same time, registered Raw Input for 000D:0002 and 000D:0001.
3. With the tablet plugged in, the pen hovering, the XP-Pen driver service running and OpenTabletDriver closed:

| Collection | R40's open | ReadFile, 8 s | Raw Input, 8 s |
|---|---|---|---|
| MI_00 Col01, Col02 mouse, Col03 keyboard (8-byte) | ACCESS_DENIED (5) | - | (not registered) |
| MI_01 pen 000D:0002 (10-byte) | **SHARING_VIOLATION (32)** | - | **3199 reports, all ID 7, 400/s** |
| MI_02 vendor FF0A:0001 (12-byte) | ok | **0 reports** | (not registered) |

Sample report: `07 a0 36 12 5b 12 00 00 3c c4`. R40 skipped the failed opens silently (`if (h.IsInvalid) continue;`)
and read only MI_02: the "1 open collection". The firmware was never the problem. Linux's hidraw allows concurrent
opens, so the same design worked there.

## Changes (gd32emu.cs platform block; atf_cli.cs; tools/cs2java.py)

- **Windows reader rewritten** (`P.HidOpenPen` -> `HidOpenWindows`). Each tablet collection's usage and report size is
  read with an access-0 handle (allowed on every collection). Digitizer collections (usage page 000D) are read
  through **Raw Input**: `WinRawSession` owns a thread, a message-only window and a `RegisterRawInputDevices`
  registration (`RIDEV_INPUTSINK`, so it also works while the console is in the background). `WM_INPUT` ->
  `GetRawInputData` -> RIM_TYPEHID blocks are split into reports (`HidStoreBlock`) and go into the unchanged
  `HidStore`, filtered by the device name (`vid_28bd&pid_2904`). Other collections with reports of 10 bytes or more
  are read with ReadFile as before. Each collection is read one way only, so nothing is counted twice.
- **Nothing is skipped silently.** `P.HidDescribe()` returns one line per collection: read through Raw Input /
  ReadFile, or not read and why (held by Windows - access denied / sharing violation, too short for pen reports,
  Raw Input failed: reason). `--hid-check` prints them. `HidWhy` says what is being read when nothing arrives
  ("no reports at all (reading interface 1 pen via Raw Input, interface 2 vendor via ReadFile)"); the prefix
  "no reports at all" that `ReadyProblem` matches is kept. Opening fails only when no collection can be read, and
  then lists every collection's reason.
- `HidClose` stops the Raw Input session (its thread removes the registration with `RIDEV_REMOVE` and destroys the
  window) within the same 1 s bound as the other readers; reopening registers again. Reports from a closed session
  are discarded by the existing generation guard.
- `HidD_GetPreparsedData`, `HidD_FreePreparsedData` and `HidD_GetIndexedString` return a one-byte BOOLEAN; they are
  now declared `[return: MarshalAs(UnmanagedType.U1)]`. `HidP_GetCaps` is checked for `HIDP_STATUS_SUCCESS`.
- The SetupAPI enumeration is shared by the reader and `HidIndexedString` (`WinHidPaths`).
- `tools/cs2java.py`: `HidDescribe()` stub. Linux and macOS readers unchanged.
- Revision label R41 (`Cli.RELEASE_VERSION`, both VERSION files), AGENTS.md row, CLAUDE.md (R41 section, open item 11
  resolved), CHANGELOG, DOCS/calibration.md, model provenance, gd32emu CLAUDE.md and history.

## Verification (Windows 11, .NET Framework csc /langversion:5)

| Check | Result |
|---|---|
| Tool build (csc, C# 5) and `twin --selftest` | ok |
| **`--hid-check` on the real tablet (pen hovering)** | **Report ID 7: 2006 received** (5 s); collections listed: pen via Raw Input, vendor via ReadFile, two mice and keyboard "too short for pen reports" |
| Open / read 1 s / close, four times in one process (real tablet) | 402, 401, 401, 400 ID-7 reports; close 1 - 3 ms; 0 reports collected after close |
| gd32emu `tests/EmulatorTests` | 55 passed (51 before + Raw Input block split, short-block rejection, collection naming, open-error naming) |
| core / host / calibration regressions, stock February fixture (`backups/deco01v3-main.bin`) | 130 / 95 / 97 passed |
| R40 host regressions on the same PC, same fixture (comparison) | 95 passed (R40's record lists 96 on Linux; the difference is the platform, not R41) |

Not run: the Java translation (no Java or Python on this PC; the only Java-visible change is the `HidDescribe` stub),
the .NET 10 `dotnet publish` build, and calibration with the measurement build installed. The measurement build sends
its telemetry as 10-byte ID 7 reports on the same pen collection, so it is expected to arrive through Raw Input the
same way; confirm with `--hid-check` ("Measurement telemetry is present") after the calibration update.

Note: `backups/ic902-main.bin` on Anargon's PC is a damaged dump (end erased from 0x0800C188, the rev 3 - 5 pattern);
the tool refuses it, correctly. `backups/deco01v3-main.bin` is intact.

## Part 2: Step 5 (Settings) delay readouts

Anargon found the millisecond readouts on the settings step confusing. They were reproduced with the real screen code
in the twin (no tablet). The full diagnosis is in `Claude-workarea/rev-output/SETTINGS-MS-READOUTS.md`.

### What was wrong

- **The cursor-latency measurement itself (`Twin.MeasureLatency`, since rev 31).** It fitted a straight line to each
  slide direction, each over a different stretch of the coils. The reported position jumps where the tracked coil
  switches, and a jump near the end of either stretch moved the result.
  - The result swung by up to 100 ms with the start position alone: FAST + Aggressive Interpolation gave -28 to
    +81 ms, FAST without interpolation -16 to +19 ms. Negative results were clamped to 0.
  - The screen's one start position (14.5 coils) happened to give plausible numbers for most presets. It gave
    impossible ones without interpolation (FAST 0.1 ms, RAPID 0.2 ms).
- **Units.** Frame, processing and per-filter "Measured" values were printed in microseconds with six decimals,
  next to latency in ms.
- **The per-filter "Measured" line showed CPU cycles per frame,** not the delay the filter adds. The value was the
  difference of two noisy runs clipped at 0, so it often read "0 cycles".
- **Conditions were not stated.** Only a contact-strength pen at about 50 mm/s was measured. While hovering, Hover
  Boost halves the report rate and every report-averaging filter's delay doubles.
- **Labels.** "STOCK NOW ... the firmware on your tablet" was the input dump. The Aggressive Interpolation text gave
  ms windows only for 1000/s and 500/s.

### Changes (atf_twin.cs, atf_cli.cs, atf_build.cs)

- **`Twin.LatencyRun`**: one slide run with seed, start position, speed and pen strength as parameters. The lag is
  now the **median, over 0.1-coil bins of the true position, of the backward-minus-forward difference** of the
  reported position, divided by twice the speed. A per-position difference cancels the coils' own position error,
  and the median ignores the bins at a coil-switch jump.
  - Over 8 start phases every configuration now varies by under 0.6 ms (`experiments/delaybins.cs`).
  - Results: QUICK without interpolation 1.8 ms (half its 3.3 ms report interval); RAPID default 5.9 ms drawing,
    11.8 ms hovering (400/s, Hover Boost); stock dump 16.6 / 22.7 ms.
  - `MeasureLatency` keeps its single-run interface (seed 7, start 14.5, clamped at 0) on top of it.
- **`Twin.MeasureDelay`**: the mean of 2 runs (seed and coil phase vary), for drawing (strength 60000) and hovering
  (5200, the twin's ordinary pen), with each run's report rate. Display helpers: `DelayText` (whole ms,
  "under 1 ms"), `AddedDelayText`, `Ms` (CPU time in ms, "under 0.01 ms").
- **Settings step**: under Smoothing, Position Interpolation and Aggressive Interpolation, "Measured: about N ms more
  cursor delay while drawing, about M ms more while hovering (PRESET)", measured on vs off.
  - The four builds and the stock baseline are measured in parallel (`LatencyJob`), in about 2-3 s.
  - The CPU-cycle cost is gone. `Build.CALC_TIME` (defined but never printed until now) and a paragraph on the
    conditions are printed once below the list.
  - Aggressive Interpolation gets its window lengths and speed thresholds for the chosen preset (`Cli.WindowText`,
    e.g. RAPID 20 / 10 / 5 ms, still below 160 mm/s) and the note that Hover Boost doubles them. Its fixed text no
    longer quotes ms for 1000/500 only.
- **Settings check and build measurements**: everything in ms. "STOCK ... cursor delay about 17 ms drawing, about
  23 ms hovering (your input file, 220/s)"; per preset, drawing and hovering delay with "(Hover Boost: 400/s)" when
  the rate drops, then frame and processing on their own line. Raw values and spreads go to the verbose log.
  `Build.StockLatency` is replaced by `Build.StockDelay` (cached per input).

### Verification (Windows, csc)

| Check | Result |
|---|---|
| Settings screen and check rendered by the real code (`experiments/msreadout2.cs`), FAST and RAPID | values above; MeasureCosts 2.3 - 3.0 s, CheckChoice 2.2 s |
| `--flash-dry-run --rate rapid` (prints xppen-tools commands only; no xppen-tools beside the build) | frame lines in ms, "Cursor delay: about 6 ms drawing, about 12 ms hovering (Hover Boost: 400/s)", stock line |
| core / host / calibration regressions, February fixture (scratch copy) | 130 / 95 / 97 passed |
| gd32emu EmulatorTests; `twin --selftest` | 55 passed; ok |
| New console text | ASCII only |

## Part 3: hover pressure spikes (investigation)

Fixed in Part 4. This part is kept as first written.

Anargon reported short pressure spikes while the pen hovers at 800/1000 reports/s. This was investigated only. At
Anargon's request work moved to Part 2, and the root cause still needs a decision and a hardware check.

- **Hardware, read-only (`experiments/spikelog.cs`, Raw Input, 60 s).** The tablet ran `AnargonSS727_20261101`,
  peaking at 800 reports/s (4 reports per 5 ms) with the tablet tilted up.
  - In the first 1.3 s, with the pen close, the tip bit flickered on and off every 10-40 ms at pressure 50-1000:
    31 events, the position still within 0.2 mm.
  - With the pen higher there were no pressure events, but dropouts (status 0xC0, about 19 ms gap) came at a
    strictly periodic 60 or 92 ms. That looks like a periodic hover stage, not random loss.
  - Data: `experiments/spikes-hover-60s.csv`.
- **Firmware analysis by a Claude subagent (code analysis + twin; not on hardware; summarised).**
  - Tip-pressed band 509-540 kHz needs 3 sweeps; 500-509 kHz is "hovering tip".
  - Pressure = (moving average 1/5 of f - 509000) x 16383 / 14000; fixed amplitude gate 255; no hysteresis.
  - In the twin, false pressure appears at RAPID and ULTRA (and a little at QUICK), never on stock timing.
  - Causes, ranked:
    1. The pen's ring-down carries over between the tool's short sweep steps and biases the estimate upwards
       (Q-dependent).
    2. A hover-stage switch in the middle of a sweep spread over frames mixes reading lengths (twin: 57 of 201
       sweeps at ULTRA, 45 of 527 at RAPID). A tool bug.
    3. A thin margin: hover sits about one sweep step below 509 kHz.
  - Proposed: (A) drop a sweep whose hover stage changed part-way; (B) an entry gate before the tip band; (C) fix
    the sweep timing at the fast presets after checking `--pen-resonance` on hardware; (D) a ~20 ms filter only as a
    fallback (in the twin it removes only about half the false episodes).
- **Latent bug found on the way (confirmed by reading atf_sched.cs:511-522 and the cold-boot path).** Aggressive
  Interpolation's slot index `VF[1]` is never initialised.
  - When no pen is tracked only the count `VF[0]` is reset. After power-up the first slot write goes to
    `VF + 8 + 4 x (random 0-255)`, anywhere up to `0x20001367`; after that the index is masked to 0-15.
  - Probably a one-off stray 4-byte write into unused RAM. Not fixed (it is a firmware change and needs the twin
    gate).

## Part 4: hover pressure spikes (fixed in the twin; not yet on the tablet)

Anargon asked for fixes A to D from Part 3 where they are a good idea, and pointed out that the calibration step
already reads the pen frequency, so it can stand in for a separate `--pen-resonance` run. Each fix was measured in
the twin first (`res-int`: resonator pen with an integrating receiver, pen 4 mm up, 2 s per run, Q and resting
frequency f0 varied). The harness is the Part 3 subagent's `spike.cs`, rebuilt against R41.

### What the twin showed (Claude, twin)

- **The short sweep readings are the root cause, and worse than Part 3 measured.** RAPID's 5-sample sweep steps read a
  hovering pen up to 9 kHz high (Q 150-300). With Q 150-300 and f0 506-507 kHz, every sweep read inside the pressure band, which is a
  pen pressed all the time while hovering. Light presses (to 511 kHz) were missed (2 of 5 at Q 60 and Q 150) or
  flickered on and off during the hold. Removing the pen's leftover ringing in the model (ablation) removed all of it.
- **Stock-length steps fix it if neighbouring sweep steps are not back to back.** One 45-sample, 40-pulse step per
  frame (RAPID) read within 0.1-0.4 kHz of the true frequency at every Q and f0 tried, and never showed pressure in
  the air. Three steps back to back (FAST) still carry over a little, about like stock: worst case Q 300, f0 507 kHz,
  read 509.8 kHz (stock reads 508.4 kHz for that pen).
- **No faster sweep was reliable.** Medium steps two per frame (RAPID chunk 2: 21/30, 27/24, 33/18) were biased
  one way or the other depending on Q and f0.

| RAPID, pen 4 mm up | short readings (R40) | Accurate Pressure Readings |
|---|---|---|
| Q 60, f0 505 / 507 kHz | 503.2 / 506.0 kHz, no pressure | 505.0 / 507.0 kHz, no pressure |
| Q 150, f0 505 / 506 / 507 kHz | 6 % / 100 % / 100 % of sweeps in the pressure band | 0 % / 0 % / 0.5 %, no pressure |
| Q 300, f0 505 / 507 kHz | 31 false episodes in 2 s / pressed all the time | 0 / 0 episodes |
| press to 515 kHz, Q 150 (5 presses) | 28 episodes (flicker), tip after 35 ms | 5 clean presses, tip after 34 ms |
| press to 511 kHz, Q 60 | 2 of 5 missed, tip after 126 ms | none missed, tip after 62 ms (stock 39 ms) |

### Decisions

- **C, done as a setting: Accurate Pressure Readings** (`accp`, Noise-related tuning, default on, file tag `AP`,
  `--no-accurate-pressure`). `Build.Accurate()` picks the most stock-length steps per frame that fit next to the scan
  with every optional filter charged: QUICK the whole sweep every frame (300 updates/s, no cost), FAST 3 per frame
  (188/s instead of 375), RAPID 1 (100/s instead of 300). ULTRA has no room (`n/a`). `Build.Effective()` applies it in
  `BuildOne`, except with expert `--samples`/`--burst` overrides. The Settings screen shows the updates per second for
  the chosen preset. Pressure updates at RAPID drop below stock (about 220/s), and in the twin light presses register
  about 20 ms later than stock. Both are the price of readings that are right.
- **A, done: drop a sweep that crosses a hover-stage switch.** `setmode` sets `VQ` (`0x20000F52`) when the stage
  changes while a sweep is paused at a chunk boundary (`V[3]` != 0); every fresh sweep clears it; `fin` then skips
  `0x08007840` and only starts the next scan phase (`bl 0x08009B58`, as `0x08007840` would). A flag set by the switch
  itself, rather than a stage saved at the sweep start, cannot drop the first sweep after a search. Twin, ULTRA, Q 300:
  9 false episodes in 2 s -> 1 (f0 505 kHz), 8 -> 5 (507 kHz); no mixed sweep reaches the estimator. With RAPID's sweep
  now over 6 frames this matters there too.
- **B, not done: entry gate.** With a 2.5 kHz stability limit it blocked real presses: RAPID missed 5 of 5 presses and
  ULTRA 2 of 5, because their short readings never agree that closely. With accurate readings it has nothing left to
  catch.
- **D, not done: a ~20 ms filter.** Anargon preferred no filter. In the twin it left most episodes and cannot help a
  pen read as pressed all the time, and it would add 20 ms to every pen-down.
- **`VF[1]` stray write, done.** The slot index is masked before the write too (`atf_sched.cs`, Aggressive
  Interpolation), so a random power-up value cannot write outside the 16-slot ring.
- **The calibration step is the hardware check** (Anargon's suggestion). The normal and highest hover steps now keep
  every fresh frequency reading and count the in-range reports that showed pressure. The results print "Pen frequency
  while hovering: median, highest; N of M readings at or above 509 kHz; pressure shown with the pen in the air", and
  advice when they reach the band (turn the setting on, use a slower preset than ULTRA, or report the numbers). The
  calibration build uses the final build's settings, so it measures the timing that will be flashed. Saved as
  `air_*` keys (not read back).
- Labels: RAPID "low latency, sweep spread over frames", ULTRA "lowest latency, noisiest, short pressure readings".

### Verification (Windows, csc /langversion:5; Java 25 via cs2java)

- All five presets build and pass the builder's twin gate (boot, USB, AGC, pen detection, measurements).
- Twin checks added to the core suite: RAPID with short readings shows pressure while a Q 150 pen hovers at 506 kHz,
  RAPID with Accurate Pressure Readings shows none, and on ULTRA no sweep with a stage switch reaches the frequency
  estimator. Calibration suite: hover-frequency decoding, the summary, and the advice.
- The shared code translates with `tools/cs2java.py` and compiles with javac; the Java build gives the same RAPID
  build log line and twin result.
- Nothing was flashed. The hardware evidence is still only Part 3's read-only log.

## Part 5: on the real tablet (Anargon's Deco 640, 2026-11-08 evening)

Anargon authorised DFU use for calibration and quality tests. Every image passed the builder's twin gate first, and
xppen-tools verified CRC and vector after each update. Logs and data: `Claude-workarea/rev-output/hw/`.

- **Starting point.** The tablet ran `AnargonSS727_20261101`, a build of this tool, with the Bootloader Counter at 12.
  Full backup `hw/dumps/20261108-230419` (device CRC16 0x67CC verified). Four updates tonight, so the count is now 16:
  measurement build, calibrated R41 RAPID, Dual-coil RAPID, calibrated R41 RAPID again.
- **Calibration (Tablet signal thresholds, RAPID) through the new Windows reader.** It ran step by step with the tool's
  own `HidSource`, `Decode`, `Derive` and `SaveCal` (`experiments/hwcal.cs`), with Anargon positioning the pen.
  - Calibration reports arrived at 800 per second.
  - The first minimal-pressure try read 2559 (limit 2048); the second was fine.
  - Normal hover: signal 152, 802 reports in range, frequency median 506.992 kHz, highest 508.992 kHz, none of 401
    readings at 509 kHz, no pressure.
  - Highest hover: signal 96, still tracked, highest 508.832 kHz.
  - Light contact: 510.208 kHz.
  - Result: pick-up 93, drop 72, Jump Rejection margin 1.06 mm (`hw/calibration-rapid.txt`).
  - **This pen hovers about 2 kHz below the pressure threshold.** That is the "close pen" case in which the twin's short
    sweep readings showed pressure in the air.
- **Hover, before and after** (20 s close hover each, `hw/before-close-hover-20s.csv`, `hw/after-close-hover-20s.csv`).
  - The old firmware sent 111 reports/s, mostly at 60/s (Reach Stage) even 1-2 mm up; R41 sent 702/s (median 708/s).
  - Gaps over 10 ms: 1005 before, 3 after. No pressure in the air in either log.
  - One 0.95 s pressure episode in the R41 log was a real touch: Anargon felt it, and the profile rose and fell smoothly.
  - Pressure changed in exact 10 ms steps: 100 updates per second, Accurate Pressure Readings working on hardware.
  - The Part 3 flicker (31 events in 1.3 s, short readings) was not reproduced on the old firmware tonight, because it
    stayed in Reach Stage. A hardware A/B of short against stock-length readings at the same height is still to do.
- **Position noise, pen propped with the tip resting, hands off** (15 s each, `hw/noise-*.csv`; jitter = sd of
  successive differences / sqrt 2):

  | Build | X sd / jitter | Y sd / jitter |
  |---|---|---|
  | R41 RAPID (calibrated) | 0.134 / 0.087 mm | 0.055 / 0.033 mm |
  | R41 RAPID, repeated after the A/B | 0.115 / 0.079 mm | 0.101 / 0.065 mm |
  | RAPID + Dual-coil Mode | **0.038 / 0.025 mm** | **0.245 / 0.108 mm, 13 jumps of 1.5-5.5 mm** |

  - **Dual-coil Mode:** reading the neighbouring coil cut X jitter to about a third.
  - On Y the position jumped about 1 coil pitch (5.1 mm) 13 times in 15 s, each for 8-14 reports, with the pen still.
    Without the jumps Y was still noisier (0.090 mm sd). This is the uncalibrated second-coil gain (`rx2Gain` 1.15)
    the rev 11-23 notes warned "bends lines".
  - It is not usable as built. A per-tablet, per-axis gain measured in calibration might keep the X gain without the
    jumps; that is untested.
  - The setting's text now says this.
- **Mily's dump** (`ic902-main.bin` from Anargon's friend) against `firmware/anargon-backups1/deco01v3-main.bin`.
  - The bootloader, pages 0x2800/0x2C00 and the settings page are identical.
  - The boot-state page differs only in words 1-6, which a DFU update leaves erased.
  - The application is February stock with Movement Smoothing off (9 bytes) and a 1 ms USB interval (2 bytes).
  - So it is flash-compatible with this tablet, on the same bootloader without the counter.
- **Tool changes from the session.**
  - A flash that replaces this tool's measurement firmware makes no new backup. It applies only while calibration
    telemetry is arriving and the folder keeps its first backup (`dumps/primary-dump`), which then stands in for the
    checks (`Cli.MainBackupForMeasurement`). Anargon asked for this; a separate final flash had re-dumped the
    measurement build for 3 minutes. Host tests: +3.
  - Calibration texts: step 0 is "no pen" (30 cm from every part, button bar included); "middle" means the middle of
    the drawing area below the button bar; normal hover gets a reference (a little less than a pencil lying flat, half
    a phone, 3-8 mm is fine); the LED cue (blinks while tracked) is given for the limit and the lift.

## Still open

- Windows' pen input still receives the measurement firmware's ID 7 reports at the same time and may move or click the
  cursor while calibration runs (unchanged from before; the tablet does not move the cursor sensibly then anyway).
- Hover pressure spikes: on hardware R41 showed no pressure in the air and 100 pressure updates/s (Part 5), but a
  same-height A/B of short against stock-length readings has not been run. Light-press latency at RAPID (twin: about
  60 against 40 ms for stock) has not been measured on hardware.
- Dual-coil Mode: Y jumps by a coil pitch on hardware (Part 5); a measured per-axis second-coil gain is untested.
- The old firmware (`AnargonSS727_20261101`) sat in Reach Stage (60/s) at close hover; not investigated.
- ULTRA keeps short frequency readings (no room in its 1 ms frame) and only gets the dropped mixed sweeps.
- The settings delays are twin values for a steady 50 mm/s slide; they have not been measured on a real tablet.
