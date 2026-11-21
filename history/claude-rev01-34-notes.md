# Revisions 1 - 34: notes and the rev 23 summary (moved from CLAUDE.md in R45)

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

Kept as they were when CLAUDE.md carried them; parts are out of date (later revisions changed or removed features
named here). CLAUDE.md has the current findings.

## Revision notes, rev 1 - 34 (rev 34: see section 6 and `history/claude-rev34.md`)
Every revision Claude delivered, conversation by conversation, in the same layout as the full records in `history/`
(those files have every detail and each conversation's own corrections). Revision numbers are Anargon's.

| Revisions | Conversation | Model | Full record |
|---|---|---|---|
| 1 - 11 | 1: Python patcher, first brick and recovery | Claude Sonnet 5.5 (max effort) and Claude Opus 4.8 | `history/claude-rev01-11.md` |
| 12 - 23 | 2: Python digital twin | Claude Opus 4.8 (Max effort) | `history/claude-rev11-23.md` |
| 24 - 30 | 3: C# patcher and twin | Claude Opus 4.8 (Max effort) | `history/claude-rev24-30.md` |
| 31 - 32 | 4: this repository's current form | Claude Opus 4.8; follow-ups Claude Sonnet 5.5 and Claude Opus 5.5 | `history/claude-rev31-32.md` |
| 33 | 4 (continued): any firmware build | Claude Opus 5.5 | `history/claude-rev33.md` |

### Revisions 1 - 11 (conversation 1)

| Rev | Delivered | Headline |
|---|---|---|
| 1 | patcher (P1), README, stock-complete hex | 4-axis matrix + step-by-step wizard |
| 2 | patcher (P2), README, stock-complete hex | explanation header, step-5 flash hand-off |
| 3 | patcher (P3), README | xppen-tools does not write `.data` |
| 4 | `deco01v3-data.bin`, stock-complete hex, `deco01v3-main.bin` | dump comparison + recovery files |
| 5 | `deco01v3-data-pokeflasher.hex` | self-writing `.data` restorer |
| 6 | patcher (P5) | stale `--speed` argparse fix |
| 7 | patcher (P6) | rates reverted to 220/400/600/800/1000 |
| 8 | two `droptest` hexes | strong/mid-sweep diagnostics |
| 9 | `deco640_fastscan_pressurefix.hex` | pressure-index clamp |
| 10 | `summary.md` (v1) | project summary |
| 11 | `summary.md` (v2) | cold-start handoff + new open issues |

#### Rev 1 - 2
- Speed matrix (220 / 400 / 600 / 800 / 1000), wizard, export types; flash hand-off to xppen-tools.
- Any speed of 400 and up adds code after the stock image, so those builds are complete images.
#### Rev 3
- From Anargon's flash log (`expected CRC 0x5497, device returned 0x7F73`): xppen-tools uses CRC-16/XMODEM; the code
  region alone is `0x723A`, the complete image `0x5497`, and `0x7F73` is "your code + all-`0xFF` `.data`",
  reproduced byte for byte. **xppen-tools wrote only the code region and ignored `.data`.** Earlier advice to restore
  `.data` through xppen-tools was wrong and was corrected.
#### Rev 4
- Anargon's own dump: code identical to the reference, `.data` (`0x0800C188`-`0x0800C7CC`) all `0xFF`; dump checksum
  `0x7F73`, exactly the failed-verify state. `.data` cut out (1604 bytes).
#### Rev 5
- The firmware's own flash-controller code mapped (unlock keys, page erase, halfword program, busy flag). A
  self-writing `.data` restorer (1752-byte HEX at `0x08003800`) was checked in emulation with a flash-controller model:
  802 halfword programs reproduce `.data` exactly. **Anargon reported the tablet unbricked** (route not stated).
#### Rev 6 - 7
- Rate labels briefly misread as 220/300/375/440/500 and reverted to 220/400/600/800/1000 on Anargon's instruction.
#### Rev 8
- Drop-test builds raised samples/pulses **without raising the step timers**, so steps overran; one soft-bricked the
  tablet. (The origin of the later timing guard.)
#### Rev 9
- Pressure path: piecewise-linear lookup with 76 breakpoints (0 - 180000); index clamped to 74. Checked in isolation.
#### Rev 10 - 11
- Summaries; the rev 11 cold-start handoff added the memory map, patch sites and recovery pinout. The patcher with
  the clamp wired in (P7) was never delivered in this conversation.

### Revisions 12 - 23 (conversation 2)

| Rev | Request (short) | Files delivered | Headline |
|---|---|---|---|
| 12 | Read summary, look into its open issues | none | bInterval = 3 caps output at 500/s; scan structure mapped |
| 13 | Continue, no patches until main.bin | none | AGC + 4 samples zeroes lines / hard-faults; pen-state bands; timing model |
| 14 | main.bin + xppen-tools; bInterval broke the device | `deco640_usbsim.py` | Digital USB twin; bInterval literal edit works; bootloader erase/program rules |
| 15 | Use the emulator to fix the issues | none | Real-time scan twin; SOF-locked levels |
| 16 | Continue | patcher v2, `deco640_sim.py` | Patcher v2 delivered; 1000 Hz cut to 8 pulses |
| 17 | No position updates on hardware | patcher, sim, `summary.md` | 8 pulses too weak; 11-bit frame-number wrap; frame-start race |
| 18 | Still not working; level names | *(no message sent)* | (work reported in rev 19) |
| 19 | Pinout photo; continue | patcher, sim, `summary.md` | BOOT0 needs 3.3 V; ULTRA 3-step sweep failure; new levels; dual-RX |
| 20 | runtime-pid, version 727, ULTRA, 435 claim | patcher, sim, `summary.md` | Search sum threshold explains ULTRA; hover boost; `update` flashing |
| 21 | Wiggle calibration idea | none | Rev-20 RAM overlap bug; calibration built; tablet area |
| 22 | Blink LED during calibration | none | LED = PC13; two crash-class bugs caught; FAST/QUICK reverted |
| 23 | Calibrate only at boot; oddity detector | patcher, sim, `summary.md` | Boot-window calibration; outlier dropping |

#### Rev 12
- bInterval is 3 on every HID IN endpoint, inside the compressed `.data`; hosts round it to 2 ms, capping output at
  500/s. A frame is 20 steps (6-step sweep + 7 X lines + 7 Y lines). TIM16 ticks at 1 us; clock 72 MHz.
#### Rev 13
- AGC: each step waits a delay d (0 - 30) and sums `samples - d` readings. With 4 samples, d = 3 - 4 sums to zero
  (pen dropped) and d >= 5 reads past RAM and hard-faults (input stops until replug).
- Pen state by resonance: 457 - 480 and 480 - 500 kHz button states, 500 - 540 kHz tip, anything else invalid.
- Timing model: step = 2 + 2 * (burst + 2) + 15 + 2.15 * samples us (stock 40/45 = 197 us, hence its 200 us tick).
#### Rev 14
- The twin boots the unmodified firmware and enumerates it like a PC. bInterval literals: EP81 at `0x0800C63B`
  (EP82 copies it), EP83 at `0x0800C658`; 03 -> 01 enumerates as 1/1/1.
- **xppen-tools' own Deco 01 V3 bInterval edit (`0x0800C07B`/`0x0800C098`) targets a different build**; here it hits
  a GPIO address and the twin faults at startup.
- Bootloader: ERASE wipes `0x08003800`-`0x0800FFFF` and is the only thing that opens the write window; PROGRAM
  without ERASE writes nothing. Images must be complete. `0x0800C188` was xppen-tools' hard-coded IC-902 size.
#### Rev 15 - 16
- Real-time scan twin (timers, interrupts, ADC, USB start-of-frame) reproduces the hardware: stock 226 Hz, old
  s4/b16 603 frames/s but 500 reports/s. Patcher v2: bInterval 1, start-of-frame-locked levels, AGC cap =
  samples - 2, table-derived clamp, band-edge hold, complete images only.
#### Rev 17
- 8 pulses too weak (16-pulse floor + twin sensitivity check). USB frame numbers wrap at 2048, not 16384 (a ~5 ms
  stall every ~2 s). Frame-start race on tilt frames fixed.
#### Rev 19
- BOOT0 must be pulled to **3.3 V** (~1 kOhm), not ground; PA15 to ground at power-on = vendor DFU. ULTRA's 3-step
  sweep could not recover from a search landing up to 2 steps off; it went back to the 6-step sweep spread over frames.
#### Rev 20
- Flashing as one `xppen-tools update FILE --runtime-pid 2904 --pid FFF1`. Version locked to 727.
- **The pen search needs a line sum above a fixed 100**: 3 samples could not reach it; 4-sample floor.
- Hover boost introduced. "~435 kHz pickup" not supported (search only 461 - 533 kHz; under 457 kHz = no pen).
#### Rev 21 - 22
- Rev 20 bug: hover-boost settings shared RAM with the tilt-line cache, zeroing the frame period after the first tilt
  frame. Wiggle calibration built; active area 160 x 90 mm at 100 units per mm.
- LED = PC13, active-low. Two crash-class bugs caught before delivery (a label collision that would have written to
  address 3; silent build failures from an oversized literal pool). FAST/QUICK reverted to 20/10 and 30/18.
#### Rev 23
- Calibration only within 3 s of boot; oddity detector (drop a sample more than 3x the running noise + 1 mm off the
  constant-velocity path, accept after 3 drops). All five levels pass the twin gate. This is the rev 23 summary at the end of this file.

| Claim | Made in | Corrected in |
|---|---|---|
| Adaptive delay lengthens every step | rev 12 | rev 13 (delay is traded for samples) |
| Hold the PA15 express key for DFU | rev 13 | rev 14 (no physical buttons) |
| bInterval "already allows 1000 Hz" | rev 11 | rev 12 |
| xppen-tools only writes to 0x0800C188, never erase | rev 11 | rev 14 (bootloader erases to 0x0800FFFF) |
| 100%-pressure lockup = divide by zero | rev 11 | rev 13 (AGC underflow / band edge) |
| 1000 Hz at 8 pulses is fine | rev 16 | rev 17 |
| 3-step sweep fixes 1000 Hz tracking | rev 15 | rev 19 |
| 3 samples is enough for ULTRA | rev 17 | rev 20 |
| FAST at 22/12 is a free noise win | rev 19 | rev 22 |

### Revisions 24 - 30 (conversation 3)

| Revision | Old label | Package | Request |
|---|---|---|---|
| 24 | rev 8 | deco640-rev8.zip | make C# the primary patcher |
| 25 | rev 9 | deco640-rev9.zip | 800/1000 Hz need contact, wizard rework |
| 26 | rev 10 | deco640-rev10.zip | calibration must read the firmware's own values |
| 27 | rev 11 | deco640-rev11.zip | step 4 crash, ic902 input, backup, multi-platform |
| 28 | rev 12 | deco640-rev12.zip | counter must survive a stock reflash, Discreet |
| 29 | rev 13 | deco640-rev13.zip | increment in the bootloader, return-to-stock, GD32 emulator |
| 30 | rev 14 | deco640-rev14.zip | only on leaving DFU state 2, flashing, aggressive interpolation, AGENTS.md |

Before rev 24 (three turns with no files): Excite All Coils could not work (8-way switches); tracking already drives
two coils per pulse; pen detection uses one ADC sample (pick up > 300, drop < 200), the search uses line sums
(> 100); ULTRA lost pressure because its sweep readings were too short; first sign of an integrating receiver.

#### Rev 24
- C# patcher and twin in one `AnargonsTabletTools.exe`, `build.cmd` with no installs, GitHub workflow; 3-stage hover; Long-range
  Pickup; Dual-coil Search; longer ULTRA sweep readings.
- Broken pickup explained: moving the search frequency list into RAM also moved the frequency table (the firmware finds
  one from the other), so the search ran at megahertz. Fixed with hooks on the four lookup instructions.
- C# matched Python exactly (every byte identical); twin check ~0.5 s per build instead of ~60 s.
#### Rev 25
- **Why 800/1000 Hz needed contact:** the receiver integrates, so short readings never passed the stock 300/200
  thresholds or boost level 1600. Thresholds scaled to reading length; in the twin pick-up rose from 8.0/6.4 mm to
  11.8 mm, like FAST. The "AGC delaying means step up" rule caused stage flapping and was removed.
- Flashing stalled on attempt 2: reading xppen-tools' error stream to the end deadlocked once its progress output
  filled the pipe.
#### Rev 26
- Two-pass Tablet Calibration. The pen report buffer is `0x200005F0`, sent from `0x08004B32` and `0x08004B66` via
  `0x08009CB0`; telemetry is written at the moment of sending.

  | Preset | Pick-up / drop |
  |---|---|
  | FAST | 226 / 174 |
  | RAPID | 93 / 72 |
  | ULTRA | 64 / 48 |

#### Rev 27
- Input order (ic902-main first), AI notice, warnings under the question, xppen-tools backup, multi-platform builds.
  Step 4 crash: smoothing could not fit ULTRA's frame (1009 us; revisited in rev 31). The end-of-image counter did not
  survive a stock reflash.
#### Rev 28
- Flash counter moved to the bootloader area; Discreet; warranty warning. Two empty pages (`0x08002800`,
  `0x08002C00`); the bootloader never reads them and rewrites `0x08003400` on every boot.
- Design: `fcount` (cold-boot branch of the search hook) uses the firmware's helpers `0x08009A1C` (erase page) and
  `0x08009A40` (program words) on page `0x08002C00`. Build id at scheduler table +80. Result in VS+8 (count) and VS+12
  (0 written, 1 not). Twin flash controller: keys, program into erased words only (else PGERR), page erase, ENDF;
  `--boot-wp` sets WPERR below `0x08003800`; `--persist` keeps the bootloader area. Discreet: no `fcount`, no
  bInterval patch, no marker. Telemetry pages rotate over 8 frames (signal, position, frequency, counter).
#### Rev 29
- Bootloader Counter (erase hook), `--return-to-stock`, the GD32 emulator split out (GD32-only). The tablet is not an
  STM32. Two bugs caught by the twin before release (data block allocated after the code address was fixed; a MOVS
  between a compare and its branch destroyed the flags). Results identical before and after the emulator split.
- Design: `Sched.BlWrapper()` assembles the routine at `0x08002800`; `BlPatchWord()` = `b.w 0x08002800` at the hook.
  `pcmp` / `pwrite` compare and rewrite page `0x08000C00`; `blinst` installs (status VS+13); `blclean` restores.
#### Rev 30
- Bootloader Counter moved to `set_boot_state`; the patcher runs xppen-tools again with your typed confirmation as the
  safety step; Aggressive Position Interpolation rewritten; `summary.md` became `AGENTS.md` (now `CLAUDE.md`).

  | Transition | Count |
  |---|---|
  | 0 -> 0, 1 -> 0, 2 -> 2 | none |
  | 2 -> 0, 2 -> 1 | +1 |

  | Build | Still-pen spread | Peak-to-peak |
  |---|---|---|
  | Standard | 0.031 mm | 0.12 mm |
  | New aggressive | 0.011 mm | 0.03 mm |

- Design: the routine runs at `set_boot_state(r0)`, reads the state word, counts only on 2 -> not 2 (unlock
  `0x08000A30`, program `0x08000A50`), then the displaced prologue and `b.w` back. Aggressive interpolation: VF block
  (`0x20000F60`), ring of 16, window 16/8/4 by speed, applied after Jump Rejection.

### Revisions 31 - 32 (conversation 4)

| Revision | Old label | Package | Request |
|---|---|---|---|
| 31 | rev 15 | deco640-rev15.zip | calibration before settings, counter reliability, code quality, Dual-coil merge, filters fit every frame, stock-vs-chosen latency, smarter Jump Rejection, categories, locked version |
| 32 | rev 16 | deco640-rev16.zip, then deco640-rev32.zip | reset-vector self-heal, compatibility check and warnings, PC flash-count store, one-time warnings, docs; follow-ups |

#### Rev 31
- Jump Rejection momentum model (the `odd` block): keep inside the L1 corridor between the stop point S = last and
  the coast point E = last + 1.5 v, widened by running noise + margin; a spike is replaced by the plain coast
  C = last + v (not frozen); after `maxRun` rejections it is let through. A sudden stop is inside the corridor.
- Hover hold at 800/1000 Hz (`cfg.holdHover`): a found pen is handed to Hover Boost; at label `m_nc` a clearly strong
  signal steps straight to the fast stage. FAST/QUICK byte-identical to rev 30.
- Filter frame budget: Movement Smoothing was billed a fictional 35 us (it costs ~1 us; the dead-band runs anyway);
  `SMOOTH_US = 1`, `INTERP2_US = 1`; `FiltersFit`; every build logs its budget.
- Cursor latency (`Twin.MeasureLatency`): slide forward then back; halving the gap cancels the fixed coil bias.
- Dual-coil Mode (`dualcoil`); settings categories; version lock (`AnargonSS727_<date>`, descriptor resized); wizard
  reordered; bootloader hardening (`wchk` full-routine check) + bootloader-side self-heal (hook word committed last).
#### Rev 32
- **Reset-vector self-heal (opt-in, with the Bootloader Counter).** `BlStub` after the counter routine in page
  `0x08002800` (`BlBlob` returns both; `stubEntry` is its address). The literal at `0x0800015C` (stock `0x08000C39`)
  is redirected to the stub, which fills any erased word of page `0x08000C00` from the stored copy before the boot
  code runs. Stored in app flash: the stock page `0x08000C00` (`stockAt`) and page `0x08000000` with the literal
  already redirected (`stock0At`). Routines `p0cmp` / `p0write` / `p0clean`; `blinst` order wrapper+stub -> p0write
  -> hook; `blclean` removes the redirect before erasing the stub's page; status VS+14.
- **Compatibility check (`Compat`):** probe mode in `Image.Expect` records each modification's mismatches in one pass
  with no writes; `--compat`; force mode applies anyway after the warning.
- **Warning pages (`Screen.DangerAsk`):** SOME (comma list, y/n), ALL (type `I know what I am doing.`), critical
  error after a caught flash failure, "DO NOT UNPLUG" after a counter flash.
- **PC flash-count store (`Store`):** `%APPDATA%\AnargonsTabletTools\state.ini` (or `~/.local/state/AnargonsTabletTools`,
  `ATF_STATE_DIR`); `[According to Tablet]` / `[According to PC]`; one-time warnings (`Screen.WarnOnce`).

  | Twin check | Result |
  |---|---|
  | install, then boot | boots through the stub |
  | boot code erased, redirect installed | stub repairs it, boots |
  | boot code erased, stock (control) | faults at `0x08000C38` |
  | page 0 erased (residual) | faults from reset |
  | second boot (idempotent) | 0 bytes changed |
  | write-protected chip | nothing written, still boots |
  | state transitions | counts only on 2 -> other |
  | return-to-stock | both pages restored, boots |
  | RAPID build on a FAST-installed chip | 0 bytes changed in all four bootloader pages |

- Follow-ups: brick-window estimate (~85 ms, Claude Sonnet 5.5), risk review and unplug page (Claude Opus 5.5),
  stub-page exception, Discreet recommendation, docs, repository split, and this renumbering.

### Revision 33 (conversation 4, continued)

| Revision | Old label | Package | Request |
|---|---|---|---|
| 33 | - | deco640-rev33.zip | December analysis fixes (a) compatibility covers every call/table, (b) real firmware end, (c) flexible enough for ANY later XP-Pen build |

#### Rev 33
- **Symbols.** Patch code writes firmware locations by their February address: `Fw.A(0x0800398C)`, `Fw.Ptr(0x0800766C)`
  (the word stored there, e.g. a table pointer). `Layout` (`atf_layout.cs`) resolves each for the firmware being
  patched: exactly one signature match = the location, none or several = not found (never guessed). An address with
  no signature is an internal error, so a new hard-coded address cannot slip in unchecked.
- **Signatures** (`atf_symbols.cs`, generated by `tools/mksig.py` from both stock dumps and the regression log of
  every byte the tool writes): shortest window (>= 12 fixed bytes, preferably inside the symbol's function) that is
  unique in both builds at the right address. Masked: BL/B.W encodings, LDR/ADR literal offsets, flash pointers,
  bytes the tool rewrites (so built images resolve too - the twin needs that), and bytes that differ between builds.
- **Known builds** (`Layout.KNOWN`: build string, end, CRC): the resolved addresses must equal the stored ones; a
  location that differs is distrusted (an image that is not the untouched build its string names).
- **`Image.Expect` on calls:** a BL/B.W is compared by where it goes (the February target, resolved), not by bytes.
- **Transactions:** `Image.Begin(tag)` snapshots, `Image.Fail(e)` rolls a group back on a missing location: probe ->
  incompatible, forced -> "not applied", normal -> refused with the reason.
- **Movement Smoothing** off now walks the dead-band setup's `movs`/`strb` run from the function start (December
  retuned the values and moved them: 8 sites in February, 7 in December).
- **Max-pressure fix** reads the two table pointers through `Fw.Ptr`; the search-probe list through the firmware's
  own pointer to it (`Fw.Ptr(0x08005E38)`).
- **Input:** the end comes from the scatter-load table (`Layout.FindEnd`), else the known build's end, else the last
  non-0xFF byte; no table = truncated. Added code still starts at the firmware end, so a truncated input leaves the
  place of `.data` free (byte-identical to rev 32 builds).
- **Twin:** its hooks (coil switching, receive, delays, frame, GPIO, the AGC check call) resolve from the image it
  runs (`Twin.LayoutOf`).

  | Check | Result |
  |---|---|
  | February, 100+ build matrix (every preset x 10 setting combinations x bInterval, cleanup, telemetry) | byte-identical to rev 32 |
  | February truncated (`0x8988`) input | identical to rev 32 except skip-message wording |
  | December, same matrix: patch logs mapped to February with the offset map | 3806 lines, 0 differences (calls differ only in encoding) |
  | December compatibility | 0 of 9 incompatible (rev 32: 6 of 9 flagged, 26 calls unchecked) |
  | December, all five presets in the twin | all pass; frame timing and latency as February |
  | December + Bootloader Counter | passes |
  | 4 simulated future builds (December relinked with 16 bytes inserted at 4 places, `.data` uncompressed) | 80/82 found; only the two compressed-`.data` bytes missing (correctly: 1 ms USB fix left out); all presets pass the twin |
  | corrupted site / duplicated signature / xppen-tools-size dump | only that modification flagged / refused as ambiguous / truncated warning |
  | an already-built image as input | refused (all incompatible), as before |

## The rev 23 summary (Appendix A of CLAUDE.md until R45; kept unchanged)

> **Numbering note.** This appendix is the summary uploaded at the start of revision 24 (it is revision 23's
> summary). It uses its own counter: its "rev 7" is project revision 23, and its "rev 1-6" are the summary's earlier
> copies, not project revisions 1-6. The real per-revision history of revisions 1-30 is in `history/`.

# XPPen Deco 640 Firmware Mod - Summary (rev 7, 2026-10-30)

> Complete record of the project up to rev 7. Everything is verified against `deco01v3-main.bin`, the xppen-tools
> binary (strings only, never run) or the digital twin (`deco640_sim.py`) unless marked otherwise.
> **Nothing in rev 7 has been run on a real tablet.** The rev 6 handoff is kept unchanged in `orig/summary.md`.

## 1. What this is
- **Target:** XPPen Deco 640, MCU GD32F350C8T6 (marked "UG902C"), Cortex-M4F, 64 KB flash, 8 KB SRAM, 72 MHz.
  Firmware "deco01v3", stock build string `2025-02-14_release001`.
- **Goal:** a higher, stable report rate with low noise, without breaking pressure, tilt or the pen buttons.
- **Files in `./`:**

| File | What it is |
|---|---|
| `deco640_patcher.py` | Builds firmware variants (wizard or command line), checks each one in the twin, can flash |
| `deco640_sim.py` | Digital twin: Thumb-2 interpreter plus models of the timers, interrupts, ADC, USB, a synthetic pen and a PC host |
| `deco01v3-main.bin` | Full 64 KB stock dump. **Use this as the input** |
| `deco01v3-stock.hex` | Truncated xppen-tools dump (ends at 0x0800C188). Used to test the truncation warning |
| `DESIGN.md` | Firmware map, locked decisions and the rev 7 change list |
| `orig/` | Pristine copies of the five original attachments |
| `tools/dis.py` | Capstone Thumb-2 disassembler (`python -I tools/dis.py IMAGE.bin START [END\|+LEN]`) |
| `tools/boothist.py` | Compares where two images spend their boot time |
| `tools/intervals.py` | Report and frame timeline for an image (`IMAGE [settle_ms] [ms] [pen strength]`) |
| `tools/boost_test.py` | Hover-boost ramp and touch test (`IMAGE [f0] [Q] [peak strength]`) |
| `tools/cal_test.py` | Startup-calibration test: one hard press, then hover (`IMAGE press_f0`) |
| `build/` | Test outputs. `build/all/` holds all 6 presets with default settings plus their logs |

## 2. History before this session (rev 1-6, condensed)
- **Rates wrong or unstable: fixed.**
  - bInterval 3 capped output at 500 reports/s. The literals in the compressed `.data` are now patched to 1.
  - Frames are paced to the USB start-of-frame (1000/500 Hz) or by a timer (300/800 Hz), and reports go out at a fixed phase.
  - Also fixed: the 11-bit USB frame-number wrap and a race between frame planning and the step timer.
- **AGC fix.** The firmware's adaptive sampling delay could exceed the sample count, which zeroed line sums or read past RAM (hard fault). Its cap is now set to samples-2.
- **Pressure.**
  - The table index is clamped, with the bound derived from the tables.
  - A resonance overshoot at 540-545 kHz (full pressure) is held at 539.999 kHz instead of stopping all reports.
- **Tilt and sweep work is spread across frames,** so no frame overruns.
- **Hover boost:** long, strong readings at half rate when the signal is weak.
- **Wiggle calibration:** picks the steadiest scan-frequency offset at boot.
- **Oddity detector (now "Jump Rejection"):** drops a single reading that jumps far off the predicted path.
- **Dual-RX (experimental):** reads a second coil in the same ring-down.
- **Software divide replaced by the hardware UDIV instruction.**
- **Flashing:** `xppen-tools update FILE --runtime-pid 2904 --pid FFF1`.
  - The bootloader's ERASE clears 0x08003800-0x0800FFFF, and PROGRAM writes nothing without an ERASE first, so every image must be complete.
  - The patcher refuses to flash a build that has not passed the twin.
- **Hardware results before rev 7:**
  - FAST (500) works.
  - RAPID (800) works but has about 7 mm of jitter at hover.
  - ULTRA (1000) gave no input on the builds tried.
  - Rev 5 builds were bad: boost state overlapped the tilt cache. Fixed in rev 6.
- **"435 kHz pickup" claim from earlier chats: not supported.**
  - The search only probes 461-533 kHz.
  - The firmware treats a resonance under 457 kHz as "no pen".

## 3. Rev 7 - what was asked
The user's requests, all now implemented:
1. A new **REACH** preset: whole tablet at about 60 Hz with maximum excitation, for the most hover height.
   It replaces the earlier "NERF" idea and was locked as a 6th rate preset.
2. **Tune Hover Boost** (not remove it):
   - switch on closer to the surface (threshold 700 -> 1600);
   - touching the surface forces full rate;
   - the switch-back level is measured live (1.375x) instead of a fixed 2.1x, to stop the shake.
3. **Calibration redesign:**
   - one hard press arms it;
   - it measures with the pen near zero pressure;
   - the run lasts about 4 s and the LED blinks slower;
   - per-pressure-zone offsets were requested (see section 7).
4. **Excite All Coils** as an experimental toggle, default off, labelled "likely no effect".
5. **Wizard:**
   - numbered steps; typing a number moves straight to the next step;
   - every setting shown as `[Enabled]` / `[Disabled]` with one sentence on what it does;
   - Position Interpolation on by default, Smoothing off.
6. **Console and logs:**
   - short console output per build;
   - details in `main-[TIME].log`;
   - everything, including the twin's full output, in `verbose-[TIME].log`.
7. **Input file:**
   - taken as-is, with no tail recovery;
   - a `.data` truncation warning;
   - two build-number warnings (exact texts below).
8. **Naming:**
   - output files named `superspeed-<preset>[-w<setting>...].hex`;
   - version string `AnargonSS727_<build date YYYYMMDD>`.
9. **ASCII-only output,** worded for a casual tech user.

## 4. Rev 7 - what was done

### 4.1 Patcher, Python side
- **Version string** is `AnargonSS727_YYYYMMDD`, exactly 21 characters, written into the USB string-5 descriptor.
  The per-level letter is gone, and `--no-mark` keeps the vendor string.
- **Presets** (`LEVEL_ORDER` = 220, 300, 500, 800, 1000, 60). Names: stock / quick / fast / rapid / ultra / reach.
  `--rate` accepts either the number or the name.
- **REACH** is `('REACH (60) ...', 16667 us, 24 samples, 254 pulses, tilt every 4, no chunking, sweep on tilt frames, sweep 6, split 2)`.
  - It is timer-paced at exactly 60 Hz.
  - The step tick is capped at 255 us (the timer reload is one byte; longer steps just pay one extra 10 us poll).
  - Frame work is about 12.6 ms out of 16.7 ms.
  - The scheduler's "start anyway" fallback was 400 polls (about 5 ms). It is now `max(400, period_us // 10)`, so a 60 Hz frame doesn't start early.
- **Input handling** (`read_input` / `check_input`):
  - The file is taken as-is, and the embedded copy of the stock tail was removed.
  - A full dump has the bootloader stripped; anything past the stock end (0x0800C7CC) is ignored.
  - The input's build string, CRC and size go to the main log.
- **Truncated input** (ends before 0x0800C7CC) prints the exact warning
  `Your inputted file is potentially truncated, and is missing .data.` and a plain-English explanation:
  - what `.data` is (start-up values copied into memory at power-on);
  - why its absence makes flashing risky;
  - advice to press Ctrl-C and re-run with `main.bin`.
- **What happens after a truncation warning:**
  - On the command line there is a 5-second pause when run interactively.
  - Patches whose bytes are missing (the bInterval literals and the max-pressure tables) are skipped and logged as SKIPPED instead of crashing.
  - The output is labelled "INCOMPLETE image".
  - It fails the twin boot, which is noted as expected, and is never offered for flashing.
- **Build-number warnings** (exact texts):
  - build is not `2025-02-14_release001`:
    `Warning: You are running this tool on a firmware version it is not intended for. You may experience issues with your tablet firmware.`
  - and also not `2025-12-23_release001`:
    `Warning: This build also does not match any other known public builds. You may want to join https://www.discord.gg/tablet and go to Tablet Firmware > Support > Tablet Requests, within the "XPpen Deco 640" thread`
- **Unexpected code at a patch site** now gives a plain message ("does not have the code this tool expects at 0x..., use the stock 2025-02-14 firmware") instead of "internal: unexpected bytes".
- **Console output per build:**
  `Building Preset i/6: <label>`, `(One frame every N microseconds)`, `Check main-<time>.log ...`,
  `If there are any problems, you can open a discussion send verbose-<time>.log`, the "vibecoded slop" warning, then
  `Wrote <file>: complete image 0x8003800..0x... (N bytes, M bytes of flash left)`.
  - The preset number is its position in `LEVEL_ORDER`.
- **Logs:** written next to the output.
  - `main-YYYYMMDD-HHMMSS.log` has the input, settings, scan parameters and every byte change.
  - `verbose-...log` has the same plus the screen output and the twin's full detail.
  - A run in the same second gets a `-2` suffix.
- **File names:** `superspeed-<preset>` followed by suffixes.
  - Filters and experimental extras add `-w<tag>` when on: `wsmoothing`, `wposinterpolation`, `wtwocoil`, `wallcoils`.
  - Default-on features add `-no<tag>` when off: `nohoverboost`, `nocalibration`, `nojumprejection`.
  - Custom tuning flags add `-custom`.
  - The default FAST build is `superspeed-fast-wposinterpolation.hex`.
  - An explicit `.hex` output name is still accepted.
- **Wizard steps:**
  1. Input file.
  2. Output folder.
  3. Preset: 1-6, or 7 for all presets; Enter means FAST.
  4. Settings: type numbers such as "1 3" to flip them; Enter keeps them.
  - Then one review line: Enter builds, `c` cancels.
  - Each setting is shown as `[Enabled]/[Disabled]/[N/A] Name` with one sentence.
  - The two filters also show `Calculation time: under 0.01 MS ...`. The twin measured about 0.14 ms of processing per frame with or without them.
  - Interpolation also shows "Removing this on top of smoothing makes the pen output raw positions."
- **Settings:**

| Setting | Default | Not available for |
|---|---|---|
| Movement Smoothing | off | - |
| Position Interpolation | **on** | - |
| Hover Boost | on | STOCK, QUICK, REACH |
| Startup Calibration | on | STOCK |
| Jump Rejection | on | STOCK, REACH (the pen moves too far between readings at 60/s) |
| Two-coil Read (experimental) | off | STOCK |
| Excite All Coils (experimental) | off | - |

- **New command-line flags:** `--all` (build every preset into a folder) and `--all-coils`. `--interp` now defaults to on.
- **Other validation changes:**
  - `--cal-span` is limited to 1..3; a span of 4 would overwrite the cold-boot marker.
  - Interpolation is no longer charged against the frame budget (the twin shows it is negligible), so ULTRA builds with it on.
- **Flashing** is offered only for a single build that passed the twin and came from a complete input.

### 4.2 Twin check output (Phase 2)
- The patcher's `sim_gate` captures the twin's detail into the verbose log and prints only:
  `== <file>`, `boot: ok (N instructions ...)`, `device VID is as expected`, `USB interface Values are as expected`,
  `== Running Parallel Noise checks and checking targets.` / `- This step takes the longest`,
  `== Automatic Gain Control (AGC) check` (+ result), `== Running detection sensitivity tests...` (+ result),
  and then passed or FAILED.
- **What the USB check verifies:** VID 28BD, PID 2904, three HID interfaces, and every IN endpoint at the patched bInterval.
- **Twin change (`deco640_sim.py`):** if no pen report arrives in the normal 60 + 30 ms window, the sensitivity job keeps running in 10 ms slices, up to 400 ms after USB attach.
  - REACH needs about 107 ms to find the pen.
  - The existing presets behave exactly as before.
- **Bug found and fixed:** the instruction count after boot was first read after USB enumeration (37178 instead of 20421). It now comes from the twin's own boot line. The firmware itself was fine.

### 4.3 Firmware asm (Phase 3)
- **Hover Boost (P3.1).** New RAM block `VBX = 0x20000F20`:
  - +0 countdown to the capture;
  - +2 leave level;
  - +4 last fast-scan level.
- **Signal level fix (found during testing).**
  - The old level was `line sum / (samples-1)`. When the pen is close, the AGC skips samples, so a close pen read as weak and could trigger boost. That may explain the shake the user saw.
  - The level is now `sum / (samples - AGC delay)`.
  - Boost never starts while the AGC is delaying, since that means the pen is close.
- **Entering boost:** level below `BOOST_ENTER = 1600` for 24 frames in a row.
- **Touch:** reported pressure above `BOOST_TOUCH = 50` drops to full rate at once and resets the weak-frame count.
- **Leaving boost:**
  - Two boost frames after entering, the leave level is set to `1.375 x boost level x 1600 / last fast level`.
  - That scaling factor is limited to between 1 and 4, and the result is capped at `BOOST_LEAVE_MAX = 3600`.
  - Why the scaling: with a quick lift the plain 1.375x rule would switch back too early and flap. The twin caught this case: a leave point equal to a fast level of about 422 against an enter level of 1600.
  - Why the cap: the boost level stops rising at about 3900 once the AGC engages, so a higher leave point was unreachable.
- **Startup Calibration (P3.2):**
  - A single press at pressure 15500 or more within 3 s of boot starts it.
  - It measures only while the pen is in range with pressure under 500. A press or loss of the pen pauses the run without aborting it.
  - Each offset runs `4 s / 7 / frame period` frames (10% settling): FAST about 285, REACH about 34.
  - The frame constants now use MOVW, since they no longer fit an 8-bit immediate.
  - The LED toggles every 200 ms, i.e. blinks at 2.5 Hz instead of about 16 Hz.
  - The best offset is still kept until power-off.
  - **The 8 pressure zones were not built.** One offset is used for all pressures; see section 7.
- **Excite All Coils (P3.3).**
  - The firmware's TX group select (fn_08008F30 for X with 4 groups at 0x0800C0C8; fn_08008FF8 for Y with 3 groups at 0x0800C0E8) drives one active-low enable line and sets the others high.
  - With the toggle on, the calls at 0x080088CE and 0x080088FE go to new code that drives every group's line low.
  - It applies to the X and Y TX select only.

## 5. Verification (twin only)
| Check | Result |
|---|---|
| Baseline rev 6 FAST | passes (AGC ok, pen detected at all 4 frequencies) |
| All 6 presets with defaults (`--all`, `build/all/`) | all build, fit in flash (12.4-14.3 KB free) and pass. **Built before the last two boost changes; only FAST was re-gated after them** |
| FAST, ULTRA (interpolation on), REACH, FAST + all coils | pass |
| REACH report rate | 61/s, intervals 15-18 ms (1 ms poll quantisation), frame work about 12.6 ms |
| REACH vs FAST, weak pen (strength 1200) | REACH: 9 reports, first at 107 ms. FAST: 0 |
| Boost ramp (Q 300, 505 kHz, strength 50000 -> 4000 -> 50000, 30 ms touch) | Final build: enters once going down, the touch forces full rate, re-enters while weak, leaves once going up. **No flapping.** At 518 kHz the twin pen reads as touching (pressure about 9500), so it stays at full rate |
| Calibration (REACH, `--cal-span 1`) | A press at 526 or 531 kHz arms it; it steps -1/0/+1 while hovering and finishes in about 2 s with a best offset picked. A press at 536 kHz did not register as a touch in the twin |
| Warnings | truncated `.hex`: warning, 3 SKIPPED lines, INCOMPLETE, twin boot fails (expected). Build string 2025-12-23: first warning only. Unknown build: both |
| Wizard (piped input "3", "2 1") | `superspeed-fast-wsmoothing.hex`, passes, flash prompt shown |
| Version string, ASCII | `AnargonSS727_20261030` (21 characters); patcher source and output are ASCII-only |

## 6. Needs the real tablet
- **Boost thresholds.** The twin's signal scale is not calibrated to the hardware, and its noise is far below the real 7 mm hover jitter.
  - Read the level (`VB+8`, 0x20000EC8), the mode (`VB+0`) and the AGC delay (`S+8`, 0x200006A0) over SWD at several heights.
  - Then tune `--boost-at` (1600) and `BOOST_LEAVE_MAX` (3600).
- **Calibration.**
  - Confirm a firm press reaches pressure 15500 and that the 500 "near zero" limit suits the real pen.
  - The full run should take about 4 s; the twin only ran a shortened one.
  - The chosen offset is at `VB+22`.
- **REACH.** Check the real hover height gain, and the about 110 ms needed to find the pen at its long steps.
- **Excite All Coils.** Boots and passes the twin; whether it helps is unknown, and is expected to make no difference.
- **Still open from rev 6:** RAPID/ULTRA barrel-button dropouts, dual-RX gain calibration, ULTRA "no input" on hardware.

## 7. Decisions and deviations to review
- **REACH runs at 16667 us (exactly 60/s, timer-paced)** rather than 16000 us (62.5/s, locked to the USB frame clock).
- **Jump Rejection is not used on REACH:** constant-velocity prediction over 16.7 ms rejects normal curves.
- **Boost leave rule** goes beyond the locked plain 1.375x: it is scaled for a quick lift and capped below the AGC plateau. There is also a new "AGC active = close" rule. All of this came from flaws the twin exposed.
- **Calibration zones:** only one offset (no per-pressure zones) is used, because the way to fill the 8 zone offsets from a single press was never settled.
  The options: baseline plus a fixed slope, slow learning while drawing, or a short staged press at several depths. Say which, and it can be added in `VBX`.
- **File-name suffixes** for features that are on by default (`-no...`) and for the extras were my choice.

## 8. Useful live reads (SWD hot-attach, read only)
- AGC delays: 0x200003E8 (4 bytes). Scan state `S`: 0x20000698 (S[5] samples, S[8] AGC delay).
- Resonance: 0x20000410. Tracked frequency index C[3]: 0x200006BB.
- Scheduler `V`: 0x20000E80.
- `VB` (0x20000EC0):
  - +0 mode, +8 level;
  - +12 offset, +13 calibration state (0 idle / 1 run / 2 done / 3 window closed), +22 best offset;
  - +60 cold-boot marker.
- `VC` (0x20000F00): +12 rejected-sample count.
- `VBX` (0x20000F20): +0 countdown, +2 leave level, +4 last fast level.
- Line sums: X at 0x20000754, Y at 0x200007CC (12 bytes per line, sum at +4).
