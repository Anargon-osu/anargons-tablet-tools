# Deco 640 firmware mod — revision map, rev 11 → rev 23

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

Model: Claude Opus 4.8 (Max effort), Anthropic (as Anargon recorded on 2026-11-09).

Source: this chat only. Starting point: **rev 11** = the `summary.md` uploaded at the start of this chat. Each of my replies counts as one revision, so the 12 replies are **rev 12 to rev 23**.

- **Rev 18** was a turn that ended mid-work without any message to you. Its work was reported in rev 19.
- **6 of the 12 revisions** delivered files: 14, 16, 17, 19, 20 and 23.
- **summary.md labels:** the copies I wrote carried their own counter ("rev 3"–"rev 6"). Under this numbering they are rev 17, 19, 20 and 23.
- **This document** is a 13th reply but not a firmware revision.

## Overview

| Rev | Your request (short) | Files delivered | Headline |
|---|---|---|---|
| 12 | Read summary, look into its open issues | none | bInterval = 3 caps output at 500/s; scan structure mapped |
| 13 | Continue, no patches until main.bin | none | AGC + 4 samples zeroes lines / hard-faults; pen-state bands; timing model |
| 14 | main.bin + xppen-tools; bInterval broke the device; no buttons | `deco640_usbsim.py` | Digital USB twin; bInterval literal edit works; bootloader erase/program rules |
| 15 | Use the emulator to fix the issues; range is to 0x0800FFFF | none | Real-time scan twin; SOF-locked levels; new patcher (not delivered) |
| 16 | Continue | `deco640_patcher.py` v2, `deco640_sim.py` | Patcher v2 delivered; 1000 Hz cut to 8 pulses |
| 17 | No position updates on hardware; check bugs; update summary | patcher, sim, `summary.md` | 8 pulses too weak; 11-bit frame-number wrap; frame-start race |
| 18 | Still not working; level names; 500 usable but noisy | *(no message sent)* | (work reported in rev 19) |
| 19 | Pinout photo — correct? Continue | patcher, sim, `summary.md` | BOOT0 needs 3.3 V; ULTRA 3-step sweep failure; new levels; dual-RX |
| 20 | `--runtime-pid` error; version 727; parallel tests; 800 noisy, 1000 no input; 435 claim; `update` | patcher, sim, `summary.md` | Search sum threshold explains ULTRA; hover boost; `update` flashing |
| 21 | Wiggle calibration idea; 1000 still no input; RAPID+filters refused | none | Rev-20 RAM overlap bug; calibration built; tablet area |
| 22 | Blink LED 10× during calibration | none | LED = PC13; two crash-class bugs caught; FAST/QUICK reverted |
| 23 | Calibrate only at boot (3 s); oddity detector | patcher, sim, `summary.md` | Boot-window calibration; outlier dropping |

---

## Rev 12 — first look at the issues
**Files:** none (tool budget ran out).

**Discovered**
- **USB bInterval is 3 on all HID IN endpoints.** It lives in the compressed `.data` and is unpacked to RAM at boot. Hosts round 3 down to 2 ms, which caps output at 500 reports/s. That matches "~500 Hz at the 1000 setting", and the old summary's "already allows 1000 Hz" was wrong.
- **Proposed fix:** write 1 into RAM at 0x2000018F, 0x200001A8 and 0x200001C1 before USB starts.
- **Frame structure:** 20 steps per report.
  - Sweep: 6 frequency steps on the centre Y coil.
  - X lines at −4, −2, −1, 0, +1, +2, +4 (the ±4 lines feed tilt).
  - The same 7 lines on Y.
- **TIM16 (the step timer)** runs at 1 µs per tick, so tick/poll values are microseconds. This explains stock ~220 Hz and the s4/b16 build's ~600 Hz.
- **Clock:** 72 MHz, so the old pacer's microsecond timing was correct. TIM17 is not used by stock code.
- **Instability sources named:**
  - An adaptive settle delay (corrected in rev 13).
  - Reports are dropped, not queued, when the endpoint is busy.
  - 400/600/800 can't be evenly spaced on 1 ms polling.
- **Issue #7:** the "zero padding" the clamp used is inside the 66-byte USB string 4 ("UG902_BPG0607L-A").
- **Free space in-place:** almost none. The divide routines give ~50 B, and the smoothing function ~250 B when smoothing is off.
- **Patcher's "emulator" estimate** was only a formula.

**Open / later corrected**
- The "variable settle delay lengthens steps" claim was corrected in rev 13. The firmware trades delay for samples, so the window length stays roughly constant.

## Rev 13 — research only (no patches)
**Files:** none.

**Discovered**
- **AGC vs low sample counts.** Each step waits an adaptive delay d (0–30) and takes `samples−d` readings. The sum routine has an off-by-one and an unsigned compare. With 4 samples:
  - d = 0–2 sums 3, 2, then 1 reading.
  - d = 3–4 sums to zero (pen dropped).
  - d ≥ 5 reads past RAM and hard-faults (input stops until replug).
  - Stock is safe (45 samples > cap 30).
  - With 4 samples, the reading the AGC watches is taken early in the ring-down, so the AGC climbs often.
- **The old summary's divide-by-zero explanation doesn't hold.** The app never enables the divide trap, and the table reads stay inside flash.
- **Pen-state bands by resonance:**
  - 457–480 kHz: button state.
  - 480–500 kHz: button state.
  - 500–540 kHz: tip.
  - Anything else: invalid, so pen reports stop.
  - Full-pressure overshoot past 540 kHz would therefore stop reports.
- **Tracking speed:** the sweep moves only 2–3 of its 40 steps per frame, so button jumps take several frames to follow. Chunking made this slower.
- **Timing model:** step ≈ 2 + 2·(burst+2) + 15 + 2.15·samples µs.
  - Stock 40/45 ≈ 197 µs, which is why its tick is 200.
  - 16/4 ≈ 62 µs.
- **Guard rules:**
  - samples ≥ cap+2 (or lower the cap at 0x080075D6/0x080075DA).
  - samples ≤ 99 (ADC buffer).
  - burst ≤ 254 (255 never stops the excitation timer).
- **Device side ignores bInterval,** so a RAM write before USB starts is enough.
- **Pen report:** 10 bytes, EP2, dropped if the endpoint is busy.
- **Smoothing function** can be reused when smoothing is off (only a vendor diagnostic reads its output).
- **SWD:** the app never reconfigures PA13/PA14, so hot-attach RAM reads were suggested.

**Later corrected**
- "Hold the PA15 express key for DFU" — you said the tablet has no physical buttons (rev 14).

## Rev 14 — main.bin + xppen-tools; digital USB
**Files:** `deco640_usbsim.py` (boot + virtual USB host).

**Discovered**
- **The twin boots the unmodified firmware and enumerates it** like a PC. Stock: VID 28BD / PID 2904, bInterval 3/3/3, strings and report descriptors all match.
- **bInterval bytes in this build:**
  - EP81 is a literal at **0x0800C63B**, and EP82 copies it via a back-reference.
  - EP83 is a literal at **0x0800C658**.
  - Changing both from 03 to 01 enumerates with bInterval 1/1/1; the config descriptor is the only USB-visible change.
- **xppen-tools' own Deco 01 V3 bInterval edits (0x0800C07B/0x0800C098) target a different, smaller build.** In this build 0x0800C098 is part of a GPIO address, and the twin faults at startup. This is a likely way the earlier bInterval change broke the tablet.
- **Bootloader rules (from main.bin):**
  - ERASE wipes 0x08003800–0x0800FFFF and is the only thing that opens the write window.
  - PROGRAM without an ERASE in the same DFU session writes nothing.
  - So "truncated, never erase" cannot work; images must be complete.
- **The 0x0800C188 "end of code"** came from xppen-tools' hard-coded IC-902 size (0x8988).
- **Confirmed in emulation:** the AGC failure (3–4 → zero, 5 → fault) and that the old clamp slot changes USB string 4.

## Rev 15 — real-time scan twin, new patcher (undelivered)
**Files:** none (tool budget ran out before copying).

**Discovered / built**
- **The twin now runs the scan in real time:** timers, interrupt priorities, ADC with a synthetic pen, USB start-of-frame and host polling. It reproduces the hardware:
  - Stock: 226 Hz.
  - Old s4/b16 build: 603 frames/s but 500 reports/s at the PC.
  - Old 1000 setting: 966 frames/s → 500 reports/s.
- **New patcher (delivered in rev 16):**
  - bInterval 1 plus start-of-frame-locked levels; exact 1000/500/333/250 in the twin.
  - Step-model timing guard.
  - AGC cap = samples−2.
  - Clamp bound derived from the tables and moved out of string 4.
  - Band-edge hold: 540–545 kHz → 539.999 kHz.
  - Complete images only.
  - Tilt split across frames.
  - 3-step sweep at 1000 Hz.
  - Flash sequence dfu → erase → program → finalize.
- **Per-level cost (twin):**
  - 1000: ~6× stock noise, 959 of 1000 µs used, 5–15 ms dropout on a button press.
  - 500: ~2× stock noise, 1.66 of 2 ms.
  - 333/250: about stock noise.
- **Not built:** dead-zone fusion. Also, instant 20 kHz frequency jumps lose the pen even on stock.

## Rev 16 — patcher v2 + twin delivered
**Files:** `deco640_patcher.py` (v2), `deco640_sim.py` (replaces usbsim).

**Discovered / changed**
- **Button test, final 500 Hz build:** press with no dropout, release ~5 ms. Stock drops >20 ms on release. 1000 Hz: ~5 ms on both press and release.
- **1000 Hz cut from 10 to 8 pulses** for timing headroom (959 → 908 µs).
- **Fusion not built:** the sweep reads only the Y-centre coil with ~3 µs less settling, so reuse would bias position.

**Problems found later in this build:** 8 pulses too weak, frame-number wrap bug, frame-start race, poll 5 µs (all rev 17).

## Rev 17 — "no position updates" on hardware
**Files:** patcher, sim, `summary.md` (its own label: "rev 3").

**Discovered**
- **1000 Hz's 8 pulses were too weak.** Calibrated against the s4/b16 build proven on the tablet, the twin needs 1.5–2× more pen signal. Added a 16-pulse floor and a twin sensitivity check (fails the 8-pulse build, passes the rest).
- **USB frame numbers wrap at 2048** at full speed, not 16384. This caused a ~5 ms stall every ~2 s; fixed with 11-bit math.
- **Frame-start race** on tilt frames: the step timer could start a frame early. Fixed.
- **Poll** raised from 5 to 10 µs for CPU headroom.
- **1000 Hz made experimental:** 16 pulses only fit with slower frequency tracking, so a button press can drop the pen.
- **summary.md:** issues 4, 5, 7 closed; 2, 3, 6 done (untested on hardware); 1 open.

**Problems found later:** the 3-step sweep can't acquire at some hover frequencies; 3 samples too few for the pen search (rev 19, rev 20).

## Rev 18 — (no message sent)
**Request:** "Even worse, still doesn't work; levels STOCK 220 / QUICK 300 / FAST 500 / RAPID 800 / ULTRA 1000; 500 usable but too noisy; keep researching other frequencies."

**Work done in the turn (reported in rev 19):** reproduced the ULTRA acquisition failure, implemented the new level table and timer pacing, and started dual-RX. The turn ended before a reply was written.

## Rev 19 — pinout photo + new levels
**Files:** patcher, sim, `summary.md` ("rev 4").

**Discovered**
- **Photo check:**
  - The yellow line is pin 38 = PA15 and the blue line is pin 44 = BOOT0, with pin 1 at the photo's top-left. The crystal, USB and UART pad positions fit that orientation.
  - PA15 to ground at power-on = vendor DFU: correct.
  - **BOOT0 must be pulled to 3.3 V, not ground** (ground is its normal state). Measure first, and use ~1 kΩ.
  - D_RX/D_TX ≈ PA10/PA9; check by continuity.
- **Why ULTRA failed:** the 3-step sweep can't recover when the pen search lands up to 2 steps off. The search probes indices 15/19/21/26/29/32/36. The twin tracked at only 2 of 5 hover frequencies. ULTRA now uses the 6-step sweep spread over frames, with a full sweep on acquisition.
- **Levels as requested.** QUICK and RAPID are timer-paced; at the PC they show 3/3/4 ms and 1/1/1/2 ms intervals.
- **FAST changed to 22/12** (pulses/samples): ~25% less noise in the twin, 89% of the frame. *(Reverted in rev 22.)*
- **RAPID/ULTRA** briefly drop the pen on a barrel-button press.
- **Research on other measurements for position:**
  - The sweep duplicates Y line 3 only: a Y-only 10–20% potential.
  - Tilt lines are too weak.
  - Dual-RX (`--dual-rx`, `--rx2-gain` ~1.25): saves ~260 µs/frame, but a wrong gain bends lines. About 20% less noise than old FAST, only ~10% better than spending the time on samples.
  - Conclusion: no hidden position channel.

**Problems found later:** ULTRA's 3 samples vs the search threshold (rev 20); the `erase` step lacked `--runtime-pid` (you reported it in rev 20); FAST 22/12 drops the pen on button press (rev 22).

## Rev 20 — runtime-pid, 727, parallel tests, ULTRA, 435 claim
**Files:** patcher, sim, `summary.md` ("rev 5").

**Discovered / changed**
- **Flashing:** a single `xppen-tools update FILE --runtime-pid 2904 --pid FFF1`, with DFU re-entry and retry. The old `erase` step was missing `--runtime-pid`.
- **Version** locked to 727 (`AnargonSS_727_…`).
- **Twin checks run in parallel processes**, one per core, including sensitivity at 4 hover frequencies.
- **Likely ULTRA cause:** the firmware's pen search needs a line **sum above a fixed 100**.
  - 3 samples (2 summed) need ~50 counts per sample.
  - The proven 4-sample build needs ~33, and RAPID ~25.
  - Fix: 4-sample floor, and ULTRA moved to 4 samples (97–100% of its 1 ms frame in the twin).
- **Noise physics:** precision scales with excitation × √(summed samples). RAPID sits at ~1/6–1/7 of stock, which fits ~7 mm of jitter. Noise grows with height because the signal falls.
- **Hover boost (new):** weak signal → long-integration frames at half rate. Default threshold 700 (`--boost-at`). In the twin, jitter while boosted was 3–4× lower.
- **"~435 kHz pickup" not supported:**
  - It is table index 5–6 (433.7 / 436.4 kHz).
  - The search only uses 461–533 kHz.
  - The firmware treats anything under 457 kHz as "no pen".
  - Driving the pen there gives only ~1–7% of the signal.
  - Check: read 0x200006BB while hovering.

**Problems found later:** hover-boost settings overlapped the tilt-line cache in RAM, breaking pacing (rev 21); FAST 22/12 button issue (rev 22).

## Rev 21 — wiggle calibration, overlap bug (undelivered)
**Files:** none (tool budget ran out).

**Discovered / built**
- **Bug in rev 20 (all paced levels):** the hover-boost settings shared RAM with the tilt-line cache. After the first tilt frame the step tick and frame period became 0, so pacing broke. Fixed by moving boost to its own block.
- **Wiggle calibration built:**
  - Trigger: reported pressure ≥ 15500, held.
  - Tries scan-frequency offsets −3..+3 around the tracked resonance, 40 frames each, scored by summed |Δx|+|Δy|.
  - Twin result for offsets −3..+2: 1259, 684, 342, 191, 569, 1038 (offset 0 best).
  - At +3 the pen dropped and the run aborted (fixed in rev 22).
- **Why offsets instead of absolute 490–550 kHz:** the table tops out at 545.5 kHz, and a frequency picked at full pressure would sit ~25 kHz off the pen at hover.
- **Tracking ceiling:** the firmware drops the pen if its 6-step window would pass the top of the table, so tracking is limited to ~533 kHz. Calibration therefore triggers on reported pressure, not frequency.
- **Active area** (pen HID descriptor): 160 × 90 mm, exactly 100 report units per mm.
- **RAPID + smoothing + averaging:** now trims to 4 samples instead of refusing.
- **Version string** gains a level letter (`AnargonSS_727_U…`).
- **ULTRA:** the twin can't reproduce "no input", even with code 60% slower than modelled. Asked for SWD reads.
- **Coil current** can't be raised from firmware: the drive is already a 50% duty square wave on one coil.

## Rev 22 — LED blink, two crash-class bugs (undelivered)
**Files:** none (tool budget ran out).

**Discovered / built**
- **LED = PC13 (chip pin 2), active-low.** The firmware lights it while the pen is in range, turns it off when the pen leaves, and blinks once at boot.
- **10 blinks spread over the calibration run.** In the twin: 9 visible when the last offset loses the pen.
- **Calibration fixes:**
  - A dropout keeps the best offset so far.
  - Re-arm only after a real ~1 s release.
  - Boost is frozen during the run.
  - FAST twin results: 678, 390, 144, 81, 304, 541 → offset 0.
- **Bugs caught before delivery:**
  - **Label collision:** the "keep best offset" step jumped into a copy loop and wrote to address 3. That would have crashed the tablet at the end of calibration. The assembler now rejects duplicate labels.
  - **Silent build failures:** an out-of-range field and an oversized literal pool meant earlier tests ran stale images. Fixed with an in-range field, MOVW/MOVT constants and exit-code checks.
- **Hover boost** now needs 24 weak frames in a row, and pen search runs in fast mode.
- **FAST/QUICK reverted to 20/10 and 30/18.** With 22/12, FAST lost the pen on a barrel-button press in the twin, because longer bursts respond to a narrower frequency band.

## Rev 23 — boot-window calibration + oddity detector
**Files:** patcher, sim, `summary.md` ("rev 6"). Copied out first this time.

**Discovered / built**
- **All five levels build and pass the twin gate:** boot, USB, AGC, and pickup at 4 hover frequencies.
- **Calibration only within 3 s of boot**, timed by the firmware's ms counter at 0x20000408.
  - Press hard and hold until the LED stops blinking.
  - Early release: no change (retry allowed inside the window).
  - Missing the window: calibration is off until the next plug-in.
  - A dropout keeps the best offset so far.
  - Twin: all of these behave as described.
- **Oddity detector:**
  - Predicts each position from the last two (constant velocity).
  - Drops a sample more than 3× the running noise + 1 mm off that path (`--odd-mm`, `--no-odd`); the report holds the last good position.
  - Accepts the next sample after 3 drops in a row.
  - Twin: interference on one coil moved X by up to 7.8 mm without the detector; with it, all 3 bad samples were dropped. A smooth stroke had 0 drops.
- **SWD addresses:**
  - Calibration state: 0x20000ECD.
  - Chosen offset: 0x20000ED6.
  - Dropped-sample count: 0x20000F0C.
- **ULTRA:** unchanged; still unresolved on hardware.

---

## Corrections to earlier claims (this chat)
| Claim | Made in | Corrected in |
|---|---|---|
| Adaptive delay lengthens every step | rev 12 | rev 13 (delay is traded for samples) |
| Hold the PA15 express key for DFU | rev 13 | rev 14 (no physical buttons) |
| Old summary: bInterval "already allows 1000 Hz" | rev 11 | rev 12 |
| Old summary: xppen-tools only writes to 0x0800C188, never erase | rev 11 | rev 14 (bootloader erases to 0x0800FFFF) |
| Old summary: 100%-pressure lockup = divide by zero | rev 11 | rev 13 (AGC underflow / band edge) |
| 1000 Hz at 8 pulses is fine | rev 16 | rev 17 |
| 3-step sweep fixes 1000 Hz tracking | rev 15 | rev 19 |
| 3 samples is enough for ULTRA | rev 17 | rev 20 |
| FAST at 22/12 is a free noise win | rev 19 | rev 22 |

## Status at rev 23
- **Hardware:** FAST usable but noisy; RAPID works but ~7 mm of jitter at hover; ULTRA has given no input on every build tried.
- **Rev 23 features not yet tested on the tablet:** hover boost, boot calibration, oddity detector.
- **Still open:**
  - ULTRA root cause (needs live SWD reads).
  - RAPID/ULTRA barrel-button dropouts.
  - Hover-boost threshold calibration.
  - Dual-RX gain calibration.
  - Sweep → line-3 fusion (designed, not built).
  - Twin timing is approximate.
