# Claude outputs in this chat: revisions 24 to 30

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

Model: Claude Opus 4.8 (Max effort), Anthropic (as Anargon recorded on 2026-11-09).

Source: this chat only. The chat started from your revision 23 (the uploaded `summary.md`, which called itself
"rev 7"). My packages were wrongly numbered rev 8 to rev 14; the correct numbers are below.

**7 revisions were delivered (24-30).** Three earlier turns ended at the tool limit with no files.

| Your revision | My label | Package | Turn |
|---|---|---|---|
| (none) | - | no files | 1: upload of revision 23 |
| (none) | - | no files | 2: C# instead of Python, faster twin |
| (none) | - | no files | 3: UART pins, ULTRA frequency theory |
| **24** | rev 8 | deco640-rev8.zip | 4: make C# the primary patcher |
| **25** | rev 9 | deco640-rev9.zip | 5: 800/1000 Hz need contact, wizard rework |
| **26** | rev 10 | deco640-rev10.zip | 6: calibration must read the firmware's own values |
| **27** | rev 11 | deco640-rev11.zip | 7: step 4 crash, ic902 input, backup, multi-platform |
| **28** | rev 12 | deco640-rev12.zip | 8: counter must survive a stock reflash, Discreet |
| **29** | rev 13 | deco640-rev13.zip | 9: increment in the bootloader, return-to-stock, GD32 emulator |
| **30** | rev 14 | deco640-rev14.zip | 10: only on leaving DFU state 2, flashing, aggressive interpolation, AGENTS.md |

---

## Turns 1-3 (no files delivered)

What was found:
- **Excite All Coils could not work.** The coils sit on 8-way analog switches; two coils can only be on together if
  they are exactly 8 apart. Excite All Coils was switching on coils 8/16/24 away, not neighbours.
- **Tracking already drives two coils per pulse** (the X and Y coils crossing under the pen). Only the pen search used
  a single coil, which led to Dual-coil Search.
- **Pen detection uses one ADC sample:** the last sample of the strongest line (pick up above 300, drop below 200).
  The search uses line sums (above 100).
- **ULTRA lost pressure because its sweep readings were too short** (4 samples, 16 pulses) to measure the pen's
  frequency. The pen's frequency itself did not move.
- **Startup Calibration and ULTRA interact.** A press within 3 s of plugging in starts calibration, which scans at
  offset frequencies; at ULTRA that weakened the signal enough to lose the pen.
- **RAPID's Hover Boost exit used FAST's fixed level**, but RAPID reads on a smaller scale (5 vs 10 samples), so only
  a press could get it out of boost.
- **First sign of an integrating receiver.** The DISC_ADC / DISC_OPA labels and PB8/PB9 pulses around each reading
  suggested it; the twin still modelled a fading sample.
- **UART:** PA9 = pin 30 = D_TX, PA10 = pin 31 = D_RX.
- **Twin work:** a new Python twin matched the old one exactly (same cycles, reports, RAM), a physical pen model
  (height in mm, coil plane 1 mm under the surface), and the start of the C# port.
- **Turn 3 warning:** an in-progress build had broken pickup, so nothing from that turn was to be flashed.

## Revision 24 (my "rev 8")

Delivered: C# patcher and twin in one `deco640.exe`, `build.cmd` (no installs), `.csproj`, GitHub workflow; Python
kept as reference; 3-stage hover (fast, Hover Boost, Reach Stage 60 Hz); Long-range Pickup; Dual-coil Search;
ULTRA sweep readings lengthened.

Found:
- **Cause of the broken pickup from turn 3:** the search ran at 3-72 MHz instead of ~500 kHz. Moving the search
  frequency list into RAM also moved the frequency table, because the firmware finds one from the other. Fixed with
  hooks on the four lookup instructions.
- **C# matches Python exactly:** every image byte-identical, and the twin output identical (via Java translation).
- **Speed:** twin check about 0.5 s per build (Python about 60 s).
- **ULTRA pressure in the twin:** 16383 steady, versus 11945 at FAST for the same pen, so ULTRA's pressure curve
  differs.

## Revision 25 (my "rev 9")

Delivered:
- per-stage pick-up/drop thresholds and boost level scaled to reading length, plus `--detect-scale`;
- quick searches on FAST's readings;
- REACH preset removed;
- first Aggressive Interpolation;
- measurements at 72 MHz (ps resolution, mm precision);
- new screens and shorter settings text;
- optional calibration step;
- fix for flashing stalling on attempt 2.

Found:
- **Why 800/1000 Hz needed contact:** the receiver integrates, so short readings read proportionally smaller and never
  passed the stock 300/200 thresholds or the boost level 1600 except at contact.
- **Twin reproduction (integrating receiver):** rev 24 RAPID/ULTRA boosted at about 1.5 mm and picked up from
  8.0/6.4 mm; with the fix, 11.8 mm, like FAST.
- **The "AGC delaying means step up" rule caused stage flapping** with an integrating receiver; removed.
- **Flashing stall on attempt 2:** reading xppen-tools' error stream to the end before its normal output deadlocked
  once the progress output filled the pipe.
- **Movement Smoothing measures 0 cycles:** its code always runs, and "off" only zeroes its coefficients.

## Revision 26 (my "rev 10")

Delivered: two-pass Tablet Calibration. A calibration build sends internal readings in the pen report; the app runs
5 standard steps (including minimal pressure below 2048), derives per-tablet thresholds, boost level and jump margin,
saves them, and builds the calibrated firmware.

Found:
- **The pen report buffer is 0x200005F0.** It is sent from two places, 0x08004B32 and 0x08004B66, both calling the
  USB send routine 0x08009CB0. Writing telemetry at the frame restart was overwritten, so it is now written at the
  moment of sending.
- **Twin calibration results:**

  | Preset | Pick-up / drop |
  |---|---|
  | FAST | 226 / 174 |
  | RAPID | 93 / 72 |
  | ULTRA | 64 / 48 |

- **Fixes made along the way:**
  - The twin check needed to count telemetry reports as tracking.
  - The decoder needed to skip readings of line sets not measured that frame.

## Revision 27 (my "rev 11")

Delivered:
- **Input:** files are tried in the order ic902-main.bin first, deco01v3 last, and modded dumps are skipped.
- **Screens:** new AI notice text, warnings listed under the question.
- **Settings:** smoothing unavailable on ULTRA, and the chosen settings are checked after step 4.
- **Version marker:** now changed silently.
- **xppen-tools:** automatic backup (`dfu` + `dump`) with one updating progress line; flashing printed as commands to
  type.
- **Counter:** one stored at the end of each image.
- **Platforms:** Windows, Linux and macOS single-file builds, and Linux tablet reading.

Found:
- **Step 4 crash:** smoothing cannot fit ULTRA's 1 ms frame (1009 us of work).
- **The counter at the end of the image** only counts builds from this tool and does not survive a stock reflash
  (you raised this in the next turn).
- **My mistake:** I misread "manually type it into xppen-tools" as "never run the command" (corrected in revision 30).

## Revision 28 (my "rev 12")

Delivered: flash counter moved into the bootloader area, the Discreet setting, the warranty warning, AI DISCLOSURE
title, counter page in the calibration readout.

Found:
- **Two empty 1 KB pages in the bootloader area:** 0x08002800 and 0x08002C00. The bootloader's own data is at
  0x08003000-0x0800341B (VID/PID, serial, config).
- **The stock firmware already has the GD32 flash routines** (unlock, program, erase, lock, plus helpers at
  0x08009A1C and 0x08009A40).
- **Running the bootloader from reset in the twin:** it never reads the empty pages, so no checksum covers them. It
  rewrites 0x08003400 on every boot.
- **Twin counter results:** reboots do not count, new builds count once, write protection is reported, and a full page
  rolls over correctly.
- **Bug found and fixed:** adding the counter page to the telemetry stopped all signal readings; the pages now rotate
  over 8 frames.

## Revision 29 (my "rev 13")

Delivered: experimental Bootloader Counter (erase hook), `--return-to-stock`, the GD32 emulator split into its own
project (GD32-only, STM32 refused).

Found:
- **The tablet is not an STM32.** It is a GD32F350: GD32 flash-size register, GD32 USB core, no ST traces anywhere.
- **The bootloader's program routine is identical to the firmware's.** Every possible hook point sits in pages the
  bootloader runs at power-on.
- **Two bugs the twin caught before release:**
  - the data block was allocated after the code address was fixed;
  - a MOVS between a compare and its branch destroyed the flags.
- **The tablet's results were identical before and after the emulator split.**
- **The emulator needed system reset:** the stock bootloader requests a reset after writing its config.

## Revision 30 (my "rev 14")

Delivered:
- Bootloader Counter moved to `set_boot_state`, counting only when leaving state 2.
- The patcher runs xppen-tools again; your typed confirmation is the safety step.
- Commands shown without paths and run in the patcher's folder.
- An already-in-DFU tablet continues straight to the next command.
- Aggressive Position Interpolation rewritten.
- `summary.md` renamed to the cumulative `AGENTS.md`.

Found:
- **Boot state is word 0 of 0x08003400**, changed only by `set_boot_state` (0x08000F40). Setting state 2 writes only
  that one word, so 2 means "update or erase in progress".
- **Twin results for the new counter:**

  | Transition | Count |
  |---|---|
  | 0 -> 0, 1 -> 0, 2 -> 2 | none |
  | 2 -> 0, 2 -> 1 | +1 |

- **Your suspicion about the old Aggressive Interpolation was right:** it wobbled slightly more than standard while
  moving.
- **The new one:**

  | Build | Still-pen spread | Peak-to-peak |
  |---|---|---|
  | Standard | 0.031 mm | 0.12 mm |
  | New aggressive | 0.011 mm | 0.03 mm |

  It costs a few microseconds of processing; its real cost is latency of about half the averaging window.

---

## Still open after revision 30
- Nothing from revisions 24-30 has been confirmed on the tablet: hover at RAPID/ULTRA, the new aggressive
  interpolation, calibration, both counters, return-to-stock.
- "State 2 = update in progress" comes from reading the bootloader code, not from tracing a real DFU session.
- The already-in-DFU crash could not be reproduced here.
- Windows-only and platform code (HID, cursor positioning, .NET 8 single-file builds) was compiled only on your side.
