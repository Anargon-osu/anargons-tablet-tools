# R45 - Claude Opus 5.5 (Anthropic), 2026-11-10 to 2026-11-16

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

Model: Claude Opus 5.5 (`claude-opus-5-5`), Anthropic. Same conversation as R42-R44. Requests are Anargon's.
R45 is a copy of R44 (`rev-output/R44`) with the changes below. Anargon: "R45 is the last planned revision before
going public"; everything is in R45-rc1, its fixes in R45-rc2 (Parts 11 to 13) and R45-rc3 (Parts 14 and 15), R45-rc4 (Part 16) and R45-rc5 (Part 17). Tablet runs were authorised by Anargon for this revision; every image
flashed passed the twin gate and went through xppen-tools with its typed confirmation
(`experiments/noise45/flash.sh`).

## Part 1: calibration levels and the fallback

### Request

Anargon's R45 spec (2026-11-10): step 4 Calibration, step 5 Noise-Related Patches, step 6 Extra QOL Patches;
calibration in levels ("the more complex, the better") that unlock settings; and "default values produced by all
calibration steps that I have done, including APIP" for everyone who measures less.

### What was built

- **Levels** (`Cli.CalibrationStep`, `CAL_LEVELS`):
  - **Fast Pass:** Pen frequency tuning (`cal`), shown with the setting's own text. It conflicts with measured
    levels (they need the stock scan frequency), so the levels are not cumulative there.
  - **Standard:** Tablet signal thresholds (the measurement firmware and its five positions); unlocks Multi-coil
    Read (`Cli.LockedBy`).
  - **Full:** ALPI training after the patches; unlocks ALPI, with its Tracker on by default.
- **Fallback** (`Options.fallback`, wizard only): `Build.FB_*` thresholds, RAPID 135 / 90 with a Jump Rejection
  margin of 1.293 mm (Anargon's 2026-11-09 file), ULTRA 93 / 64 (R44 log); `Apip.Fallback`, Anargon's own ULTRA
  training (Part 2).
- "Higher collection values" and coil midpoints were tried in the twin: no gain, nothing built.

## Part 2: ALPI (APIP renamed) and the tablet training

- **Units:** the XP-Pen driver's report mode 4 (1/200 mm, unit flag ST+21). Profile format 2; format 1 is refused.
- **Filter:** live noise estimate plus still gains; the gate needs 3 residuals on the same side; ALPI also replaces
  Jump Rejection, whose coasting turned spikes into ramps.
- **Tablet training (2026-11-11):** `rev-output/hw45/real` (602479 readings, a 3597-reading hold). It is now
  `Apip.Fallback` (identical to the file on ULTRA).
- **Tablet result, still pen at the centre** (the driver's injected moves, against no filter):

  | Build | X | Y |
  |---|---:|---:|
  | Tracker | 0.08x | 0.06x |
  | Robust, 2 trackers | 0.18x | 0.20x |
  | R43 build | 0.28x | 0.21x |

  So the Tracker is the default with Full calibration; the Robust filters (2-4 trackers, a robust IMM) stay
  experimental. Their bench result had used the R44 Tracker; the real noise is heavy-tailed, which keeps shifting
  their weight to the quick tracker.

## Part 3: why ULTRA is noisy with the pen still, and the two holds

Silent study builds (no cursor), still touching pen, ULTRA with 12-bit Readings (`experiments/noise45`,
`rev-output/hw45/noise`). Two causes, both a re-centre with no stay-put margin:

- **Drive-step hop (main cause, both axes).** After every sweep (twice per 16 frames) the firmware moves the drive
  step C[3] to the strongest sweep step (`0x08007840`, C[3] += peak - 2). ULTRA's short sweep readings are polluted
  (twin: neighbouring steps differ 2.5x), so the pick hops at random (tablet: the weaker step 84 % of the time). Each
  hop changes pen strength about 30 % and moves the position about 0.5 mm, because the lines do not scale evenly.
- **Coil-window flip.** The scans drive the pen through the centre coils, so the centre line reads 2.5-2.8 % low
  (X and Y), and between two coils the window re-centres every frame (X 57 % of frames at the first spot).

Tablet A/B, holding the drive step (`lines-ab.csv`, 4 s blocks, median of 5): X spread 0.175 -> 0.125 mm, Y 0.102 ->
0.080; strength wobble 8.7 % -> 3 %.

Built as settings, default on for paced presets:

- **Frequency Hold** (`fhold`, `Sched.HOLD_ADDR`, `HOLD_AGREE` = 3, 8 since rc2, Part 12): a move of 2 or more
  steps happens at once, a one-step move only after that many sweeps agree.
- **Coil Hold** (`chold`, `COIL_MARGIN` = 3): a one-coil re-centre is undone unless the new peak line beats the
  centre line by 3/32 of the centre's height above the weakest of the five lines. Twin: Y flips at a midpoint 50 % ->
  0.
- **Both, silent product build on the tablet:** X 0.116 / Y 0.083 mm, against 0.183 / 0.119 with neither; strength
  sd 1.5 %. The pen had drifted off the flip zone, so Coil Hold at a midpoint is not yet shown on the tablet.
- **Twin, hover pressure:** with both holds RAPID's short frequency readings show no pressure while the pen hovers
  (0 of 800 reports; Frequency Hold off 56, Coil Hold off 656, both off 800). The core suite checks both.

## Part 4: the LED pulse and scan noise

- **The calibration and training LED pulses** (`Sched.LED_FADE`): 360 ms, 60 ms full, 120 ms eased down, 60 ms at
  10 %, 120 ms up (gamma 2.2), after Anargon's rounds ("doesn't dim enough", "holding a bit too long", "a touch too
  aggressive", "the window must be equal").
- **Switching the LED during a scan step adds noise** (tablet, silent builds, still pen): PWM from the step timer made
  readings noisier at ULTRA (X 1.20x, Y 1.38x) and FAST (2.0x / 2.2x), not at QUICK (0.89x / 0.92x). Changing it
  only at the frame restart is clean at ULTRA (0.96x / 1.03x). So the step timer drives the PWM only where a step's
  burst and settle are at least 81 us (`Sched.LedStages`: QUICK, Steady Hover, Reach Stage); elsewhere it changes
  once per frame.

## Part 5: report rate and noise

Anargon asked how fast the tablet could go above 1000/s before noise passes 0.030 mm. Raw per-frame noise with the
pen touching is flat from 300 to 1000/s (QUICK 0.10 / 0.07 mm, FAST 0.08 / 0.06, RAPID 0.14 / 0.07, ULTRA 0.11 /
0.07-0.10), so a fixed-time filter improves as about 1/sqrt(rate). The cap is scan timing: the shortest step is
about 62-65 us, so about 1700/s with 3 + 3 lines, about 2000/s with the sweep every other frame and trimmed
processing, about 2500/s without processing or pressure. *(hardware, calculation)*

## Part 6: firmware made by other mods, and by this tool

### Request

Anargon (2026-11-12), quoting the README's "Use your tablet's untouched full stock dump": "Out of date; the tool
should generally scan and revert mods applied to builds in general, and revert said mods back to stock (namely mods
made by mei and mily). The relevant notice in the CLI should also say what was modified, and how". Then: "It should
also revert mods made BY this patcher".

### What was built

- **Other mods** (`Repair.Examine`): every known edit site records whether it changed; `Repair.HowLines` says what
  changed and how (the instruction kind and address, or the `.data` byte, with the stock and the found value), and
  `MOD_EDITS` / `Origin` name the published mod it matches:

  | Mod | Dead band | Samples | Pulses | Poll | Tick | bInterval |
  |---|---|---|---|---|---|---|
  | Mei FILTERON-s4-b20 | stock | 4 | 20 | 10 | 50 | 1 |
  | Mei FILTERON-s4-b28 | stock | 4 | 28 | 10 | 50 | 1 |
  | Mei nofilter-s4-b16 | 0 | 4 | 16 | 10 | 50 | 1 |
  | Mei nofilter-s4-b20 | 0 | 4 | 20 | 10 | 50 | 1 |
  | Mily | 0 | stock | stock | stock | stock | 1 |

  The wizard shows the notice once, in a box; the command line prints it; the recovery tool lists the changes as
  "not damage".
- **This tool's builds** (`atf_revert.cs`, `OwnRevert`):
  - `Image` keeps the input as it came and, for each byte, the modification group that last changed it
    (`Image.Begin` attributes the previous group's changes).
  - Every build carries an undo record (magic "ADTU", format 1), allocated after every change and before the options
    text: a header (firmware length, its CRC-32), the group names, then runs of changed bytes (offset, length,
    group), stored as stock XOR built so no stock code sequence appears twice in the image and every signature still
    resolves once; a CRC-32 of the record ends it.
  - Reading a build (`Build.ReadInput`): the record is found, checked and applied, and the result must have the
    stored CRC-32. A build without a usable record (older than R45-rc1, or damaged) is set back from a stock copy of
    the same firmware near it (`Repair.SearchRoots`: its folder, the one above, the program's folder, their dumps, 3
    levels deep). With neither, it is refused with what to add.
  - The notice ("THIS TOOL'S CHANGES WERE UNDONE") lists each group with its byte count, places and address range,
    and the added code after the firmware end (left out).
- **Checked** (scratch `revtest.cs`, February dump): QUICK, FAST, RAPID, ULTRA, ULTRA + ALPI, ULTRA + Multi-coil and
  a RAPID measurement build each set back to exact stock (CRC-32 B65DB693), and built again from that to the same
  image apart from the 4-byte build ID; a 64 KB dump of a tablet running the ALPI build too; a record with one byte
  changed is not used (the stock copy beside it is); an R45 build from 2026-11-11 with no record is set back from a
  stock dump next to it. The record does not change how the build's code is found.

## Part 7: what each calibration level needs with ALPI

### Request

Anargon: "it would be best if calibration might be mostly skippable with ALPI selected; review what steps do not
apply to calibration per level".

### Findings *(code analysis)*

- Standard puts only three values into a build: the fast stage's pick-up and drop levels, and Jump Rejection's
  margin. The boost level is always 0; everything else (rest frequency, contact signal, lift, pressure in the air,
  the flash count) is shown or saved only.
- With ALPI, Jump Rejection is replaced, so Standard adds only the fast stage's pick-up and drop, which the
  fallback (else the preset) covers. The ALPI training cannot derive them: it records only above its own drop
  threshold, at the final build's stages.
- Two RAPID runs on Anargon's tablet 10 h apart (93 / 72 and 135 / 90) did not repeat, so a fresh measurement is not
  clearly better than the fallback.

### What was built

- **Full runs Standard's measurement only when ALPI cannot be used** on the preset. If ALPI ends up off (switched
  off in step 5, or not usable), a "Signal thresholds" screen offers the measurement before the build.
- **Fast Pass keeps the fallback Jump Rejection margin** (RAPID 1.293 mm); it fell to 1.0 mm before, although the
  margin does not depend on the scan frequency.
- **The command line refuses `--startup-calibration` with `--apip`.**
- Not built (options for later): Standard cut down to steps 2 and 3 (steps 0, 1 and 4 feed nothing into the build);
  a Jump Rejection margin from the ALPI hold.
- Side findings, not changed: `Apip.Mismatch` checks only the preset and 12-bit Readings, so a training made with
  Multi-coil off is reused with it on; `Apip.Fallback` uses ULTRA's band thresholds on every preset.

## Part 8: checked, not built

- **Multi-coil Read with ALPI** (Anargon: "see digitally if enhanced, directly made multi coil read still is worse
  with ALPI noise-wise compared to stock"). Offline replay of the tablet's line recordings through the product's C#
  copies (`MultiCoil.Calc`, `Apip.Axis`; the stock path reproduces the reported positions on 96-100 % of frames):

  | Recording | Multi-coil / stock, before ALPI (spread X / Y) | after ALPI (spread X / Y) | after ALPI (jitter X / Y) |
  |---|---|---|---|
  | lines-hold (ULTRA, both holds) | 1.27 / 1.33 | 1.04 / 0.99 | 1.16 / 1.11 |
  | rate-ultra (ULTRA, both holds) | 1.34 / 1.15 | 0.97 / 0.79 | 1.11 / 1.02 |
  | rate-rapid (RAPID) | 0.98 / 1.28 | 1.06 / 0.83 | 1.16 / 1.23 |

  No noise benefit with the holds; its 12-bit X clusters come from the 5-line fit and are gone after ALPI. It stays
  experimental.
- **Background training for the "lateral X noise"** (Anargon: readings between the ALPI training states, "seeking to
  destroy" it; later: "X was actually meant to be Y, as the shake was vertical up and down").
  - On the training data (recorded before the holds), both axes have a narrow peak just before a coil midpoint,
    0.3-0.4 mm wide: touching, worst / best coil position 2.86x on Y and 2.40x on X.
  - A noise and gain model per coil position on top of ALPI gave no gain in replay (real data and a best-case
    synthetic test, for the Tracker and the Robust filter). Readings between the training states would add at most
    0.7 % more data.
  - The peak matches the coil-window flip: a simulation of the stock position routine with the centre line 2.7 % low
    gives a 2.6-2.9x peak about 0.4 mm wide, with the window switching in up to 94 % of frames; with Coil Hold it is
    about 1.3x with 0-2 % switches, as for coils that read evenly.
  - So nothing was built. The next step is a tablet run with the pen at a Y coil midpoint, with both holds on, and if
    a peak is left, a fix at its source (the drive coil's line reading low) rather than a filter.

## Part 9: other changes

- **Build options as text:** the last bytes of every image name the build and its settings (nothing reads it).
- **Calibration-step background readings** (an earlier misreading of the background-training request) were taken
  out again; the prompt / answer split of `Screen` stays.
- **`NiceAesth/xppen-dumps`:** its maintainer only wants the December dump, so Anargon is removing the February files
  there (Anargon, 2026-11-12). The R42 note about that repository is out of date on this point.

## Part 10: tests

Suites (`tests/run-regressions.py`, 2026-11-12, every source change of Parts 6-9 included):

| Fixture | Core | Host (fake xppen-tools) | Calibration |
|---|---:|---:|---:|
| February stock dump | 215 passed | 112 passed | 187 passed |
| December stock dump (Doodle's) | 215 passed | 112 passed | 187 passed |
| No dump | 117 passed | 101 passed | 79 passed |

New core checks: the notice says how each change was made and names Mei's and Mily's mods (R45 Part 6); a 64 KB
dump of a tablet running an ULTRA + ALPI build is set back to exact stock with its undo record; the record does not
change how the build's code is found; a damaged record is not used; with both holds RAPID's short readings show no
hover pressure. Wizard dry runs (piped answers, no xppen-tools, nothing sent to the tablet): Full on ULTRA goes from
step 4 straight to the patches; with ALPI switched off in step 5 the "Signal thresholds" screen offers the
measurement.

## Part 11: R45-rc2 - calibration in the background, fewer steps, menu order

### Request

Anargon: "I believe the RC process will have more than two versions because of us making a mistake. 1. ALPI should
combine all calibration steps as necessary, running them in the background, rather than having a separate
calibration step for each. Maybe even add a macro algorithm for pen resonance. 2. There might be a duplicate
calibration step? 3. Remove the penless calibration step since you cant measure anything there anyways; its always
lost." And: the menus should be "Calibration (with or without ALPI), Noise related settings (from ALPI to Multicoil,
etc.), then other settings".

### Findings *(code analysis, Anargon's calibration logs)*

- Part 7 said the training cannot give the pick-up levels: it recorded only positions and a level, above the build's
  own drop level. Both can change: the training firmware can record the detection signal and the frequency, and use
  the measurement firmware's levels.
- Duplicates in Standard: the readiness check and step 2 asked for the same light touch back to back; the slow lift
  looked for the same limit as the highest hover, and nothing from it reached a build. In both RAPID runs
  (`hw/calibration-rapid.txt`, `hw42/calibration-rapid-R42.txt`) its minimum was 72, the coils' floor.
- The no-pen step gave only a note: the firmware sends readings only while it tracks the pen.
- The patch screen showed its categories in the settings list's order (noise, position filters, ALPI, Multi-coil
  Read); that order also sets the file-name tags, so the screen now has its own order.

### What was built

- **Training samples are 12 bytes** (`Apip.SAMPLE`): positions, level, pressure and stage as before, plus the
  detection signal (the weaker of the strongest X and Y line's last reading, as the calibration telemetry's page 0)
  and the pen frequency (`RESONANCE / 16` above `Apip.FREQ_BASE`). The read-out sends 6 units per sample. The ring
  (256 samples at `0x20001100`) now reaches into the robust filter's RAM, which a training build never has.
  `Apip.TrainCheck` also asks for a signal and a frequency in at least 90 % of samples.
- **The training firmware uses the measurement levels** (pick-up 40, drop 24, `Cli.MEASURE_PICK` / `MEASURE_DROP`),
  except with expert timing or gain overrides, where measured levels cannot be used.
- **`Cli.FromTraining`:** light touch = fast-stage readings under pressure 4096; normal hover = the centre hold (else
  all fast-stage hover readings); highest hover = the weakest 5 % of the fast-stage hover readings (at least 1000).
  Levels by `Cli.Thresholds`, the guided steps' rule, never above the preset's. Shown after the training with the
  pen's frequency at a light touch and hovering; saved in the training file (`pick=`, `drop=`; rc1 ignores the keys)
  and used by `UseLevels` (wizard) and `--apip FILE`, unless a calibration file is given or the build cannot use
  them.
- **Standard has three steps** (light touch, normal hover, highest hover); the first measures right after the
  readiness check. `CalSource.CollectUntilLost`, `LIFT_MAX_SECS` and the twin's lift are gone; calibration files lose
  `signal_away` and `signal_lift_min`.
- **Noise-Related Patches:** ALPI, noise tuning, position filters, Multi-coil Read (`Cli.NOISE_CATS`); step 4 names
  "Standard (without ALPI)" and "Full (with ALPI)".
- `Cli.Pct` sorts with `Array.Sort`; the options text says `MEASUREMENT-LEVELS` for measurement and training builds.

### Limits

- The training keeps the final build's hover stages, so its weakest fast-stage readings are where Steady Hover takes
  over, not the detection limit: the levels can come out higher than Standard's (never above the preset's).
- Pressure in the air is not counted and the report rate is not measured from a training.
- Not built: the "macro algorithm for pen resonance" (choosing the drive offset during the training). No measurement
  shows an offset that beats the pen's own frequency; Pen frequency tuning (Fast Pass) already searches one at boot,
  and measured levels need the stock frequency.
- Twin only: the pen was off the tablet during rc2.

### Tests

| Fixture | Core | Host (fake xppen-tools) | Calibration |
|---|---:|---:|---:|
| February stock dump | 215 passed | 112 passed | 200 passed |
| December stock dump (Doodle's) | 215 passed | 112 passed | 200 passed |
| No dump | 117 passed | 101 passed | 79 passed |

Twin results of the new check (simulated training, 6 s plus the hold): ULTRA pick-up 96 / drop 64, RAPID 135 / 90.
Wizard dry run (piped answers, no xppen-tools; the tool read the flash count, string 173, from the tablet at start):
step 4 shows the new names and texts, step 5 shows ALPI first and Multi-coil Read last, step 5 says the levels are
measured during the training, and the ULTRA training build passes the twin gate (408 frames read through string 180,
none missing).

## Part 12: R45-rc2 - the sideways shake (Frequency Hold 3 -> 8)

### Request

Anargon, on the final ULTRA + ALPI build: "Ive put it on a noisy section in the latest firmware, analyze this noise.
its now sideways." Then: "A/B test it then put it in RC2 once fixed." Data and write-up: `rev-output/hw45/lateral`
(outside the package).

### Findings *(tablet: the tablet's own mode-4 reports, then silent study builds; pen resting on its tip)*

- The reports: X sat at two positions 0.30-0.37 mm apart, flipping about 4 times a second (median 29 ms each).
- The study build at the same spot: the coil window held (Coil Hold works at a coil midpoint), but the drive step
  hopped between 32 (90 %) and 31. The sweep picked 31 in about a third of frames, so Frequency Hold's 3 in a row came
  about 2.7 times a second. On 31, X read 0.26 mm further left and the pen 15 % stronger; the lines change shape with
  the step, so removing the floor does not remove the jump. ALPI filters the noise around the hops but follows each
  hop as a real move.

### A/B on the tablet

`experiments/noise45/mkfh.py` (on top of mkstudy.py and mkhold.py, scratch copies only): the one-step rule's count
comes from the millisecond counter (4.096 s blocks of 3, 5, 8 and 12 sweeps in a row) and the recorder packs it.
Silent build, twin-checked over all four blocks, 132 s at the spot (`lines-fhab.csv`, `fhstats.py`), first block
left out (the pen settling), 7-8 blocks per rule:

| Sweeps in a row | 3 | 5 | 8 | 12 |
|---|---:|---:|---:|---:|
| Drive-step changes per second | 6.6 | 1.1 | 0 | 0 |
| X spread, median 4 s block (mm) | 0.098 | 0.085 | 0.055 | 0.056 |
| Y spread, median 4 s block (mm) | 0.064 | 0.060 | 0.070 | 0.056 |

### What was built

- `Sched.HOLD_AGREE` = 8 (setting text updated). Moves of 2 or more steps still happen at once; a real one-step
  change waits up to 8 sweeps (about 65 ms at ULTRA, was about 25 ms).
- On the tablet: the final ULTRA + ALPI build rebuilt with 8 (`rev-output/hw45/final-rc2`, CRC 0x3A3D; 5 bytes differ
  from the build with 3: the compare and the build record). 60 s of its reports at the spot: X spread 0.013 mm, Y
  0.015 (median 4 s block); the build with 3, 30 s at the first spot about 1 mm away: 0.120 / 0.038.

### Not fixed

- **Short X bursts:** in every rule, for 1-3 s about once a minute, raw X noise triples (frame to frame 0.075 ->
  0.24 mm) while Y, pressure, the drive step and the coil window stay put. The X line right of the centre then reads
  at two levels (about 389 and 404) in runs. ALPI lets part of it through: one 1.5 s burst of 0.2-0.3 mm left dips in
  the 60 s check of the final build. Cause not found here; found and fixed in Part 13 (the bootloader's SysTick).
- **Slow wander:** about 0.3 mm over 2-3 s, in every rule and on both axes (probably the pen itself moving).

## Part 13: R45-rc2 - the midpoint bursts (the bootloader's SysTick)

### Request

Anargon: "I noticed that the noise is still really bad for these midpoints where there's coil switch up latency
between coils though. Id like for that to be resolved." Asked whether the fix would be too specific to one tablet:
it is not (see "What is tablet-specific"). Data and write-up: `rev-output/hw45/lateral` (outside the package); study
patches `experiments/noise45/mkstamp.py` and `mkst.py` (scratch copies only).

### Findings *(tablet, silent study builds, pen resting at a coil midpoint)*

- The bursts of Part 12 are one disturbance per frame, at a moment that drifts through the frame by about 13.5 us a
  second (a full pass every 74 s, the same on two days). The coil being read at that moment reads about 15 high, over
  a window of about 40 us; at a midpoint one such reading moves X or Y about 0.4 mm. It visits the coils in reading
  order, a few seconds each (y+1, y0, y-1, ... x+1, x0), and hits up to all frames at a burst's peak.
- A study build that stamps every frame with the tablet's own time (TIMER2's tick count and counter, in
  microseconds) measured the tablet's crystal against the PC's USB clock: +13.45 ppm (17 s pieces: 13.44-13.48). The
  bursts in the same recording come round every 74.2-74.4 s, a drift of 13.45-13.48 us a second. So the disturbance
  runs on the tablet's own clock, once every 1 ms of it, at a fixed moment of the tablet's millisecond: each coil's
  band of extra readings sits 75-80 us after the previous coil's (`lines-stamp.csv`).
- No timer the firmware sets up runs at 1 ms (excitation 2 us, steps 11 us, the 101 us tick, the 694 us delay timer,
  and TIMER13 only for short waits). The vendor bootloader does: it calls SysTick_Config(clock / 1000), sets
  SysTick's priority to 0 (the highest) and jumps to the firmware with SysTick still running. The firmware's SysTick
  handler is empty and nothing else uses it (its deep-sleep code saves and restores the state). So every 1 ms the
  interrupt cut in, even into the scan's own interrupt. The twin starts at the firmware, so it never had SysTick.

### A/B on the tablet

`mkst.py`, on top of the stamp build: every frame turns SysTick's interrupt on or off by the millisecond counter
(4.096 s blocks; the counter itself keeps running, so the on blocks keep their phase), and the recorder marks the
block. 240 s at the midpoint (`lines-systick.csv`), 28 blocks each, 6 frames of a pen event left out:

| SysTick interrupt | on | off |
|---|---:|---:|
| X frame-to-frame step, median block (mm) | 0.073 | 0.072 |
| X frame-to-frame step, worst block (mm) | 0.195 | 0.075 |
| Y frame-to-frame step, worst block (mm) | 0.139 | 0.064 |
| Noisiest coil reading, worst block (sd) | 7.5 | 2.0 |
| Extra at the worst moment of the tablet's millisecond (readings) | +8 to +13 | under +1 |

With it on, the bursts came at 30, 105 and 179 s (74-75 s apart) with smaller ones at 96 and 171 s; with it off, every
block stayed at the quiet level.

### What was built

- `Sched`: the search-restart routine (run at boot, before the firmware's main loop, and on every pen loss) clears
  `SYST_CSR` (12 bytes, nothing per frame). Every scheduler level gets it (only ULTRA was measured). The build log's
  scheduler line ends with "bootloader's SysTick off".
- On the tablet: the final ULTRA + ALPI build rebuilt with it (`rev-output/hw45/final-rc2b`, CRC 0x5085; the same
  settings and trained model as `final-rc2`). 160 s of its reports at the midpoint, against the rc2 build before it
  (60 s, 0.4 mm away):

| Final ULTRA + ALPI, the tablet's reports | rc2 before | SysTick off |
|---|---:|---:|
| Worst 4 s block, X / Y spread (mm) | 0.106 / 0.106 | 0.018 / 0.014 |
| Median 4 s block, X / Y spread (mm) | 0.013 / 0.015 | 0.011 / 0.012 |
| Report-to-report step, X / Y (mm) | 0.0092 / 0.0077 | 0.0039 / 0.0050 |
| Seconds over 2.5x the median spread | 17 in 60 s | 0 in 160 s |

- Flashes: the stamp build, the A/B build, then the fixed final build (counter 78 -> 81).

### What is tablet-specific

Only how fast the moment drifted: that is the crystal's offset from the PC's USB clock, different on every tablet (a
burst every 74 s here, faster or slower elsewhere, or parked on one coil for minutes on a tablet whose crystal
matches the PC's). The cause, the bootloader's SysTick, is the same on every Deco 640, and the fix uses no measured
number.

## Part 14: R45-rc3 - low hover, hover height, rename, porting guide

### Requests

Anargon (2026-11-12, after rc2): low hover "still shakes too much", and the hover height felt lower with each
revision; "pushing the hover limits for the 60hz stage again"; the rate limit "without pressure or pen buttons or
tilt", with the settings built and run, not modelled; the rename to `anargons-tablet-tools`; a developer and agent
guide that is "as general purpose as possible", with a dump from the backup set as the example; revisions after R45
named after their pull request. The tablet moved to another PC during rc3: nothing in this part was flashed, and
Anargon decides when.

### Findings *(tablet recordings with the silent study build `fw-hover`, CRC 0x9C8E, flashes 82-85; then twin)*

Data: `rev-output/hw45/noise/lines-hover.csv` (75 s of low hover), `lines-lift.csv` (slow lifts),
`lines-hover2-notlifts.csv`; analysis `experiments/noise45/hoverstats.py` (recorder slot 0, aux >> 12 = stage + 4).

- **Hover-stage flapping.** At low hover the firmware ran exactly 24 fast frames, then 3 Steady Hover frames, about
  37 times a second; the position moved 0.12-0.2 mm at each switch. The lifts show the same pattern all the way up
  the fast stage's range. Cause: at `m_nc` (800/1000 Hz hover hold) a slower stage's level was compared with the fast
  stage's boost-enter level. A slower stage reads the pen stronger (Steady Hover's 40 pulses about 2.3x the fast
  stage's 16 on the tablet, reach about 6x), so a pen too weak for the fast stage read "strong" and went straight
  back; after 24 weak frames it dropped again. *(tablet, code analysis)*
- **The same rule lost the pen higher up** (twin): the reach stage stepped up to the fast stage at about 16 mm, where
  the fast stage cannot see the pen. Twin hover hold: 16.8 mm on the final rc2 build, 22.0 mm on a plain R45 ULTRA
  build and on R43, 14.5 mm on stock: Anargon's sense that hover got lower matches the final build (ULTRA with
  ALPI and its settings). *(twin)*
- **Tilt frames move the position at hover.** In the fast stage, tilt frames 1-3 of every 16 shift Y by +0.21 /
  +0.44 / -0.20 mm and X by -0.17 mm on the first; 66 % of the worst one's positions are more than 0.5 mm off. On
  those frames the stock tilt scan runs (mode 5, no sweep) and one Y line reads much higher. Touching, the shift is
  smaller (X about 0.27 mm on one frame in 16, `lines-hold.csv`); Steady Hover shows none (every phase within
  0.03 mm, lift recording). *(tablet)*

### What was built

- **Step-up level per stage** (`Build.StepUpLevel`, `cfg.enterUp1` / `enterUp2`, `Sched` at `m_nc`): the fast
  stage's boost-enter level as the slower stage reads it, `enter x ringup(stage) / ringup(fast) x 1.25`,
  `ringup(n) = 1 - exp(-n pi / 300)` (the twin pen's Q; the model gives Steady Hover 2.2x, the tablet 2.3x). Twin:
  no flapping; fast to Steady Hover at 7.4 mm, back at 6.5 mm; hold 22.0 mm, found again on the way down at 18.5 mm.
  Low hover above 7.4 mm now runs in Steady Hover (500 reports/s). A "probe only" version (no absolute step-up) put
  the reach stage in at 5.7 mm and was dropped.
- **Reach hold** (`Build.REACH_HOLD` = 0.6): the reach stage's scan and sweep drop levels are 0.6 of their old value;
  its pick-up levels stay. Twin at ULTRA: hold 25.8-26.4 mm (22.0 before), no pick-ups without a pen. Dropping only
  the scan level stopped at 24.2 mm (the sweep dropped the pen). More reach samples made it worse (48: 20.4 mm).
- **Tilt Frame Skip** (setting `tiltskip`, default on, `--no-tilt-skip`): hovering in the fast stage (stage 0,
  pressure under 4), a tilt frame is planned as a normal frame, so the tilt scan does not run; tilt keeps its last
  value. First version (`r4_tilt`, not kept): ALPI carried on along its own path (2 x its last output - the one
  before) on tilt frames; in the twin a still pen shook 0.034 mm instead of 0.004 and QUICK's hover delay went from
  2 to 23 ms, because ALPI's movement check learns from those readings.
- **Bootloader Timer Off** (setting `systick`, default on, `--no-timer-off`): rc2's SysTick fix as a setting
  (Anargon: disclose every firmware change).
- **Rename:** folder, executable, project file, namespace and texts (`Anargon's Tablet Tools`); the tablet stays
  "Deco 640". History files, older changelog entries, archived model notes and generated verification outputs keep
  the old name. `Store.Dir` reads the old `AnargonsDeco640Tools` state folder until the new one has a `state.ini`.
- **Porting guide** (`DOCS/porting-guide.md`) and `tools/survey.py`: general steps from a full dump to a frame
  budget, each with its cross-check. Worked example: the G640 Rev B dump from the backup set (read-only). It is a
  different chip family from the Deco 640: flash at address 0, application at 0x6000, 59 interrupts, none of the
  GD32F3x0 peripheral addresses, USB 1.10 (update mode PID FFF2, application PID 0914). Its SysTick handlers are
  stubs that loop forever, so the Deco 640's SysTick trap cannot apply there; the Deco 640's application handler
  only returns, which is why its bootloader's SysTick cost time unseen. *(code analysis)*
- **Revision names:** after R45, `PR #X` (AGENTS.md section 4).
- **ALPI only with Full** (Anargon: "ALPI is not and should not be on without selecting full, so the flow for a no
  calibration ALPI should be full > skip alpi steps > alpi defaults with mismatched calibration + frequency
  defaults"). An rc3 draft had turned the Tracker on by default everywhere (with Anargon's training whenever there
  was none of the user's own, and `--no-alpi`); that is reverted: the setting is off by default as in rc2, a build
  with ALPI on and no training stops, and the several-presets path builds without it. In the training step,
  `s` (skip) now uses the defaults (`ApipCli.Skip`): Anargon's training, the fallback pick-up levels (`Build.FB_*`)
  or the preset's, and the stock frequency, with a note that they were not measured on this tablet; `f` is the same
  key. Without 12-bit Readings there is no fallback training, so skip builds without ALPI and says why. To build
  without ALPI under Full, turn it off in step 5. Kept from the draft: ALPI shows as not available with Pen
  frequency tuning (every training is made at the stock frequency). Checked with a scratch harness calling the
  training step with piped answers, no tablet: ULTRA and RAPID skip to Anargon's training with the fallback levels;
  ULTRA without 12-bit Readings builds without ALPI.

### Rate limit, built and run *(twin; `experiments/noise45/mkfast.py`, scratch copies; Feb dump, ULTRA)*

Frames with only the position: no frequency sweep step (no pressure or button updates while tracking) and no tilt
frames. Write-up: `rev-output/hw45/noise/speed-limit.txt`.

| Build | Work per frame | Runs every | Frames/s | Hover hold | Drawing delay (no ALPI) |
|---|---:|---:|---:|---:|---:|
| ULTRA (USB-locked) | 943 us | 1000 us | 1000 | 25.8 mm | 5.6 ms |
| position only, 5 + 5 lines | 850 us | 877 us | 1140 | 25.8 mm | 5.5 ms |
| position only, 3 + 3 lines | 620 us | 647 us | 1545 | 25.7 mm | 4.4 ms |

Asking for a shorter period changes nothing: the frames already run back to back. The earlier model said about
2000 frames/s; built, 1545. USB full speed still carries 1000 reports a second, so faster frames help only if the
firmware combines them per report. Nothing was built from this.

### Not done

- The step-up levels, the reach hold and Tilt Frame Skip still need a low hover recording and a lift on the tablet
  (the rc3 build was flashed in Part 15, without a pen check).
- With Tilt Frame Skip, tilt is not updated while hovering close in the fast stage.

## Part 15: R45-rc3, continued - the flash, documents for developers, packages that change firmware, tests

### Requests

Anargon (2026-11-12, the tablet back on this PC): "flash my tablet immediately with the most fixed build with the
least noise right now"; then documents "verbose enough for, id say, a university student in cybersecurity who has
basic programming language understanding but not one who knows assembly", noted in AGENTS.md ("specifically for docs,
not for user facing things"); "separate one tutorial for self modding, another for converting the self mod to a
patcherpkg"; a G430S example in the porting instructions; the OpenTabletDriver idea ("convert the algorithms into an
OTD plugin, then make it run PC side if a user really wants to use the full suite ... unilaterally compatible with
all tablets, so long as they follow relevant docs"); links to the three private repositories ("recovery instructions
can be found in the recovery folder" of deco640-resources). Tests: "make testing multithreaded (for both github and
for here) ... multi instance compatible so agents can do one per build date"; "capped to 50% of a cpus total usage
collectively, unless ran with --uncapped"; "Github tests should always be --uncapped"; GitHub needs "an .env that
always clones the directory with February and December builds", which "should git clone/checkout the selected link to
the required directory (of your choosing specific to tests) rather than just a link".

### The flash *(tablet)*

`rev-output/hw45/final-rc3/tracker/[VOLATILE]_ultra-APIP.hex`: the rc3 tool, `--rate ultra --apip
hw45/final-rc3/apip-ultra.txt` (Anargon's training, as in rc2b) with rc3's defaults (12-bit Readings, both holds,
Bootloader Timer Off, Tilt Frame Skip, the reach hold). Twin gate passed; xppen-tools with its typed confirmation,
CRC 0x129B; bootloader counter 86 (81 after rc2b: four flashes happened on Anargon's other PC). No pen check yet.

### What was built

- **Documents:** `DOCS/FIRMWARE_DESIGN.md` rewritten for that reader (section 0 explains a microcontroller, memory
  maps, registers and instructions, interrupts and peripherals with C examples; every later section says why, not
  only what); `DOCS/porting-guide.md` likewise, with a step 0 (which instruction set: Thumb or MIPS32, with
  `tools/survey.py --isa/--base`) and a worked G430S example (`ic901-*.bin`: consistent with a Microchip PIC32MX, MIPS32,
  60 MHz from a 12 MHz crystal; what a port would need: a MIPS core and PIC32 peripherals in the emulator, a MIPS
  assembler with delay slots, MIPS-aware fingerprints); `DOCS/tutorial-self-modding.md` and
  `DOCS/tutorial-patcherpkg.md`; `DOCS/otd-plugin.md` (design proposal: the processing layer, ALPI and Jump Rejection,
  can run in an OTD plugin for any tablet, sharing the C# copies the twin already holds the firmware to; the sensing
  layer cannot, because coil readings never leave a stock tablet; a raw-reading firmware mode would move part of it).
  AGENTS.md: rule 17 and section 3b (documents verbose, screens short), "Where to start" rows; README: the three
  repositories.
- **Package extension point** (`atf_build.cs`, `atf_layout.cs`): `IPatch` (`Name`, `Apply(Image)`),
  `Build.AddPatch` / `ClearPatches` / `EXTRA`; `BuildOne` applies them after the tool's own groups, each as
  `Package: <name>`, before the undo record, unless the build is a measurement, training or return-to-stock build
  (`ExtraApplies`; the log says so); `PACKAGE=<name>` in the options text. `Layout.AddSignatures` adds fingerprint
  lines (identical duplicates ignored, a differing line for a known address stops) and resolves again.
- **ZIP reader** (`atf_packages.cs`): `\` in an entry name counts as `/` before the path checks (Windows PowerShell
  5.1's `Compress-Archive`); `..`, a leading `/`, `:` and empty parts are still refused.
- **`ADT_TEARDOWN_URL`** defaults to `https://github.com/Anargon-osu/deco640-resources/tree/HEAD/recovery`
  (`Conf.TEARDOWN_DEFAULT`; an empty value in `.env` also means the default).
- **Tools:** `mksig.py` splits `REF[:MAP]` at the last colon only when a map follows (Windows paths), and expands
  wildcards itself; `survey.py --isa thumb|mips32 --base ADDRESS`.
- **Runners:** `tests/run-regressions.py` compiles the three suites at once, lists each suite's groups
  (`ADT_TEST_LIST_GROUPS=1`) and starts every group as its own process (`ADT_TEST_GROUPS=name`) in its own folder;
  `--only`, `--whole`, `--uncapped`, a receipt with the CPU used. `tests/cpucap.py`: by default every capped run on
  the computer together uses at most 50 % of the CPU (Windows: one named job object with a hard rate cap, children
  put in it paused and then resumed when the runner sits in another job; Linux and macOS: the runner pauses its
  processes while over their share); `ADT_TEST_CPU_CAP` tells a child run that its parent applies the cap.
  `tests/run-tests.py`: the cap is made before the suite and every process goes through it; `--build feb|dec|all`
  (all: both builds in threads at the same time); `--uncapped` passed on to the regressions; `ADT_TEST_DUMPS_REPO` /
  `_REF` cloned or fetched into `tests/work/dumps-repo/<repository name>-<8 hex digits of the link>/` (git with no
  prompts, `core.longpaths`, the token as an `http.extraHeader` given through `GIT_CONFIG_*` environment variables,
  so it is neither on the command line nor in `.git/config`; a lock file shared by every run; only 64 KB files are
  read, symbolic links skipped, Git LFS pointer files named in the error).
- **Groups:** the host suite's `cli-firmware` (four twin-gated builds, 67 s, the length of every run) is four groups
  (`cli-firmware`, `cli-measurement-dry`, `cli-measurement-flash`, `cli-flash-fails`; 11 checks as before;
  `FlashSteps` only passes the file to the fake xppen-tools, so the stock dump stands in for a normal image).
- **Paths:** the suites' folders are `core`, `host`, `cal` and the group's name, and the calibration suite writes to
  `cal-work`: the old names put the calibration suite's temporary file (`pass1.calibration.txt.tmp-` and 32 hex
  digits, written by the emulator's `P.AtomicWriteAllBytes`) past 260 characters inside `run-tests.py`'s folders.
- **GitHub** (`.github/workflows/tests.yml`, `tests/prepare-emulator.py`): `--uncapped` everywhere; stock-firmware
  checks on pushes and on pull requests from this repository; the dumps repository variables; `GD32EMU_TOKEN` (or
  `ADT_TEST_DUMP_TOKEN`) for a private emulator repository, as a header; `tests/work/dumps-repo/` left out of the
  uploaded logs. Not run on GitHub from here (Anargon does all GitHub work).

### Tests

| Run | Core | Host (fake xppen-tools) | Calibration | Time |
|---|---:|---:|---:|---|
| February stock dump, capped, with the December run alongside | 215 passed | 112 passed | 200 passed | 75.9 s |
| December stock dump, capped, with the February run alongside | 215 passed | 112 passed | 200 passed | 76.3 s |
| February stock dump, uncapped, alone | 215 passed | 112 passed | 200 passed | 26.5 s (before the split: 67.9 s) |
| No dump | 117 passed | 101 passed | 79 passed | 16.3 s |

- `tests/run-tests.py --require-all-firmware`, capped, both builds at once, in a work folder as deep as Anargon's
  source folder: 54 passed, 0 failed, 0 skipped, in 198 s (each build's regressions: 527 checks).
- `tests/test-infrastructure.py`: 7 passed.
- Dumps repository (scratch, a local git repository standing in, since the real ones are private and not cloned
  here): three syncs at once share one clone through the lock; February and December found by CRC, a zero-filled
  64 KB file ignored; the token not in `.git/config`; a new upstream commit picked up by the next sync; `http://`
  refused.
- Package, end to end in scratch: a `Compress-Archive` ZIP listed by the selector, the typed approval, compiled,
  handed over to the wizard; the same patch built into an ULTRA image (twin gate passed) gives pressure 3044 at
  518 kHz in the twin, the curve's value. `mksig.py` from PowerShell with `src\*.cs`: the same signature line as
  before.
- The tutorials' self-mod, on a copy of the source: core 215, host 112, calibration 200.
- The final tool, with no package, rebuilds the flashed rc3 image (February stock dump, `--rate ultra --apip
  hw45/final-rc3/apip-ultra.txt`): every logged change identical; the only differing bytes are the 4-byte build ID in
  the scheduler table, which `BuildOne` seeds from the build time on purpose.

### Follow-up: what a package is, and the recovery links

Anargon: recovery links "should link specifically towards the folder /recovery, not just the general repository";
and a patcherpkg "is a format that contains raw c code", which "also handles patching steps for each tablet,
alongside allowing for the removal, addition, etc. of options depending on the tablet (with the caveat that its
obviously implemented by the developer as their own separate thing)". (The package format is C# source, R44 Part 2.)

- **Recovery links** go to `https://github.com/Anargon-osu/deco640-resources/tree/HEAD/recovery` everywhere: README,
  AGENTS.md, `FIRMWARE_DESIGN.md` section 16, the self-modding tutorial and the changelog. The program's own recovery
  messages already did (`Conf.TEARDOWN_DEFAULT`). Links that are about the stock dumps stay on the repository.
- **A package could add firmware changes, but not options.** Handing over to the wizard, it could not add an option,
  hide one or change one, and its patch could not see the chosen settings. New in `atf_build.cs`:
  - `Build.AddSetting(Setting, IPatch)`: an option in the new category "Package options" (`Build.PKG_CAT`, last on
    the Extra QOL Patches screen) or a named one. Its patch runs only when the option is on, in a
    `Package option: <name>` group after the `AddPatch` groups; the options text names it `PACKAGE-OPTION=<KEY>` and
    its tag goes into the file name. Key, name, tag, text and category are checked (ASCII, lengths, no duplicates).
  - `Build.HideSetting(key, why)`: not shown on any screen, and unavailable on every preset, so every build treats it
    as off. Volatile Changes cannot be hidden (RAPID and ULTRA need it).
  - `Build.Option(key)`: one of the tool's options to change (name, text, default, tag).
  - `Build.IsOn(key)` and `Build.BuildingPreset()` inside `Apply`.
  - The first of these calls works on a copy of the settings list; `Build.ClearPatches()` puts the built-in list back.
- **Docs:** `patcher-packages.md` (what a package is, the two kinds: a tablet this tool patches, or another tablet with
  its own steps and checks; the API table), `tutorial-patcherpkg.md` (part 0, part 2, new part 3.11),
  `porting-guide.md` ("Shipping a port", checklist row 10), AGENTS.md, README, DESIGN, CHANGELOG.
- **Checked:**
  - A test package (Soft Pressure as an option, Tilt Frame Skip hidden, Long-range Pickup off by default) through the
    selector, the approval and the wizard: the option shows under "Package options", Tilt Frame Skip is on neither
    screen, Long-range Pickup starts OFF. With the option on at ULTRA: `[VOLATILE]_ultra-SP.hex`, the curve in the
    build log with the preset and settings the patch saw, `PACKAGE-OPTION=SOFTP` in the options text, twin check passed.
  - A harness linked to the tool: 18 checks (the refusals, the built-in list untouched, `ClearPatches` restores the
    list, the hidden option and the default) all pass.
  - Suites with the February dump: core 215, host 112, calibration 200 (527 checks, 48 processes, 37 s, capped).

### Not done

- The OpenTabletDriver plugin is a design (`DOCS/otd-plugin.md`); building it needs OTD's plugin library, a download,
  which waits for Anargon's go-ahead.
- The GitHub workflow changes and the dumps-repository clone have not run against the real (private) repositories:
  Anargon does all GitHub work. The clone picks dumps by CRC, so the repository's layout does not matter, but files
  kept in Git LFS would not be fetched (the runner says so).
- rc3's hover changes still need a low hover recording and a lift on the tablet.

## Part 16: R45-rc4 - the dynamic Steady Hover ladder and Switch Match

Anargon's requests (2026-11-13): a "dynamic speed limiter" that holds the noise of a low hover and lowers the rate
only as far as it must; after the first rc4 build: "1000hz is WAY too low; it should test the upper extremes",
"ridiculously dynamic, as in it holds 1000hz ... it slowly starts decreasing the hz until it matches noise wise with
7mm 1000hz, so it doesnt just suddenly drop to 500, or 333", and two bugs: "the cursor also jumps when switching
between hover states" and "the stepping of the coils are too noticable if you draw 45* lines, by a lot".

### 16.1 rc3's hover bugs, from tablet recordings (pen on a 3 mm stand)

- **Probe loop:** a failed probe never re-armed, so a still pen got a fast try every 16 frames (29 a second), each
  0.36 mm off. *(hardware)*
- **`StepUpLevel` ignored the samples ratio:** fast <-> Steady Hover still flapped (24-26 fast + 3 Steady Hover
  frames). *(hardware)*
- **Tablet gains:** Steady Hover (20 x 40) / fast (4 x 16) read 10.7-11.6 where they switched, reach (24 x 254) /
  Steady Hover 1.43-1.47; fit `Gain = samples^1.17 x (1 - exp(-pulses pi / 75))`. The twin's pen (Q 300) differs:
  reach / Steady Hover 2.7, Steady Hover / fast 9 at 1 mm to 14-15 at 7 mm. *(hardware, twin)*
- **Noise model** (frame-to-frame second difference, worse axis): `k / (level - floor)` mm, k about
  2.05 x sqrt(samples), floor = the level with no pen (fast 54, Steady Hover 68). *(hardware)*
- A recorded sample's stage tag is the next frame's; a reading's stage is the previous tag. *(code analysis)*

### 16.2 The ladder (`Build.BuildLadder`, `Sched` labels `m_air` / `setlad`)

- First rc4 build: fast, 500/s, 333/s, reach (ULTRA), noise target level 82. Tablet, 3 mm stand: 60 s all at
  1000/s (level median 91, so no step down), 0 switches (rc3: 3660); lifts: 70 one-step changes in order, none under
  15 frames (no flapping). Anargon: the rate falls too early and too far. *(hardware)*
- Fine ladder: the fast stage keeps the preset's rate down to `LAD_MARGIN_FAST` (1.12) x its drop level. The noise
  target is ULTRA's fast stage there (`NoiseRef`, level 77 with 12-bit Readings). Every `LAD_PERIOD_STEP` (25 us) of
  frame time up to 4000 us, the strongest read that fits the frame (at most 45 samples) becomes a step if it reads at
  least `LAD_STEP` (1.2) x the last step (by `Gain`); each step is kept down to the level where its noise model
  reaches the target (`NoiseLevel`), its drop levels lowered to match (`LowerDrop`). ULTRA: 1000/s 4 x 16,
  930 5 x 16, 909 6 x 16, 870 9 x 16, 833 11 x 16, 800 14 x 16, 755 17 x 16, 714 20 x 17, 678 23 x 18, 635 28 x 17,
  597 32 x 18, 556 36 x 20, 519 39 x 24, 482 44 x 26, 426 44 x 40, reach. At most `LAD_MAX` (24) steps. *(twin)*
- Firmware: down after `LAD_DOWN_FAST` (8) frames under the fast stage's level or `LAD_DOWN` (2) under a Steady
  Hover step's; up after `LAD_UP` (4, reach 2) frames at or above the step-up level. Two frames after a step down the
  ratio of the new read to the level it left (VBX+10) is measured, and that step's step-up level becomes that ratio
  x the faster step's level x `LAD_MARGIN` (1.25), stored at `VL_ADDR` (0x20000FA8, 48 bytes, a halfword per step,
  kept across pen loss, cleared at cold boot). VBX: +8 running step, +9 frames above, +10 level left at, +12 no step
  up before the first full sweep, +14 frames until the ratio is measured, +15 pacing of the running step (a
  USB-frame-locked preset paces steps that are not whole milliseconds by TIMER16). *(twin)*
- A found pen at 800/1000 Hz starts at the first step with Steady Hover's 40 pulses (`Cfg.ladAcq`) until one full
  sweep: from 625/s 12 x 30 the first sweep picked a drive step 2 higher and every report showed pressure.
  *(twin, `misctests/hoverpressure`)*

### 16.3 Why the cursor jumps at a switch: the cold first read

- Per-slot gains (`experiments/noise45/slotfit.py`, tablet, fast step 0, pen still), the five lines of a window in
  the order read: X -0.44 / -0.05 / +0.03 / +0.07 / +0.08, Y -0.16 / -0.06 / +0.01 / +0.06 / +0.06. The first line
  read is cold: the pen is not ringing yet; the others are read while it still rings from the line before. The
  pattern changes with the read length, the drive step (C[3]) and pressure (the sign flips at high pressure).
  *(hardware)*
- So every coil window, read length and drive step has its own position bias, smooth inside it. Drags at 45 degrees
  (`rev-output/hw45/drag/drag45.csv`, the silent drag recorder): jumps at X window switches 0.238 mm median (90 %
  0.463), Y 0.091, elsewhere 0.04. Lifts (`lifts-rc4.csv`, `switchjump.py`): fast -> Steady Hover X 0.668 mm, back
  0.480, the others 0.05-0.21. *(hardware)*
- A warm-up reading before the first line would cost 76 us a frame: ULTRA's frame is 861 us of scan (1 sweep step
  84 us + 10 readings x 76 us) + 82 us processing on average, 130 us worst; no room. *(calculation, twin)*

### 16.4 Switch Match (`atf_switchmatch.cs`, setting `smatch`, `--no-switch-match`)

- At a switch (X window C[6], Y window C[7], drive step C[3], ladder step VBX+8 or stage VB[0]; captured in
  `start_x`), if 16 frames of history exist, a straight line is fitted through the last 16 outputs from their four
  quarter sums: slope `S = (Q2 + Q3 - Q0 - Q1) / (T2 + T3 - T0 - T1)`. Gates: speed `4 |Bn| <= 3 Bd` (150 mm/s per
  axis), bend `|Q0 - Q1 - Q2 + Q3| <= 144` (0.18 mm). The correction is the mean of (line - reading) over the first
  `M` = 4 frames, capped at 1.5 mm, then scaled by `(D0 - dmax) / D0` (dmax = the farthest the reading has gone from
  the switch reading; D0 = 508 units, half a coil) and `(TF - t) / TF` (TF = 20000 ticks of 25 us, 500 ms). Time
  comes from a per-step table of frame periods (`TickTable`). Units: report mode 4 = 1/200 mm, else 1/100 mm. RAM
  `SW` = 0x20001E00, 208 bytes (layout in the file header). Runs in the frame restart before Jump Rejection and
  ALPI, on D+8 / D+10; reset in the search routine.
- Offline (`experiments/noise45/switchblend.py` float and integer, `stepblend.py`): drags X 0.036 / Y 0.040 mm
  median at switches, used at 69 of 75; hover steps 0 -> 1 X 0.053 / Y 0.046, 1 -> 0 0.033 / 0.018, others
  <= 0.054. Scratch simulations of flicks, lines and zigzags: 62 of 64 flicks untouched. *(hardware recordings,
  offline)*
- The first bend gate (0.10 mm) rejected 26 of 75 real switches (noise); 0.18 mm keeps the turns out. A 0.6 mm cap
  left the 0.67 mm hover jump at 0.20 mm; now 1.5 mm. *(offline)*
- Twin, ULTRA + ALPI: gate passed, frame budget 844 + 130 + 1 = 975 of 1000 us. The twin has no cold first read, so
  it cannot show the fix; its still-pen numbers (X 0.068 / Y 0.020 mm against 0.048-0.091 / 0.005) are twin noise.
  Flashed to the tablet (CRC 0xBDDE, sends cursor). No firmware-vs-C# twin harness: Anargon judged it not worth it.
- **Precision (Anargon: it must not just smooth):** it adds nothing outside the half coil after a switch, and no
  delay. Inside it, the position keeps the old window's bias, fading to the new one's: the step becomes a ramp.
  The source fix, correcting the first-read line's gain per read length, drive step and pressure before the position
  arithmetic, needs those gains measured on the tablet (not done).
- **Core suite, `fw-hover-pressure`:** its no-holds RAPID case (short frequency readings, Frequency Hold and Coil
  Hold off, a pen 3 kHz below pressure) failed on February with Switch Match: 801 of 801 reports with pressure.
  Isolated in a scratch harness: the routine built in but never called, nothing captured (only the code layout and
  frame timing changed): 779; capture without the call: 0; the call without the capture: 0; the position write
  removed: 701; the setting off: 0. So that case is chaotic in the twin without the holds, and is now reported, not
  checked; with the holds (default) it stays at 0, and the suites pass on February (528 checks) and December (529).
  *(twin)*

### 16.5 Not done

- The fine ladder at real hover heights on the tablet (the rc4 lifts were 78 % outside the LED zone).
- A hands-on judgement of Switch Match on 45-degree lines and hover changes.
- RAPID and FAST on the tablet (their builds with Switch Match pass the twin gate: frames 1.27 / 2.00 ms, processing
  0.08 / 0.09 ms; only ULTRA was flashed).
- The source fix above, and the tilt frames' own first-read offset (the outer line is read first there).

### 16.6 R45-rc4 (1): a noise point at a coil midpoint, Switch Match off by default

- Anargon: "I found a new point of noise, measure without flashing through the cursor". Pen still on the stand, "maybe
  hovering a little". Recorded read-only with `experiments/vendorlog.exe 30 --log` (the XP-Pen driver keeps the
  tablet in report mode 4, so the pen collection (ID 7) was silent and the vendor collection carried the reports):
  `rev-output/hw45/noisept/stand-v-201215.log`, analysed with `experiments/noise45/noisept.py`. 29992 reports in
  30 s (1000/s throughout: the fast stage, no ladder step), status 0xA0 (in range, no pressure). X at 64.48 mm =
  coil phase 0.50 (an exact midpoint), Y at 61.90 mm = phase 0.79. X sd 0.297 mm, p-p 2.705, frame-to-frame rms
  0.041 mm; Y sd 0.138 mm, p-p 1.025, rms 0.009. Strongest periods X 160-386 ms, Y 232-372 ms: a slow drift, not
  frame noise. *(hardware, cursor output after ALPI)*
- The rc4 stand recording read raw frame noise of about 0.1 mm there, which ALPI's still gain would bring far
  below 0.3 mm; slow drift has to come from something with a long memory. Switch Match has one: each switch sets an
  offset from 4 frames and a 16-frame line fit, and keeps it up to 500 ms. Simulation (scratch, `switchblend.sm_int`
  on a still pen whose window flips with a mean dwell of 3-100 frames, 0.1 mm frame noise, then a 0.02 exponential
  filter): no real step between the windows: sd 0.010 -> 0.061-0.122 mm, p-p 0.07 -> 0.40-0.94 mm; a 0.24 mm step:
  sd 0.020-0.083 -> 0.080-0.128 mm, p-p up to 1.07 mm. Without the slow filter (0.04 mm noise) it was 0.040 ->
  0.054-0.066 mm sd. The twin's still pen: Y 0.005 mm without, 0.020 with, 0.005 again without. *(simulation, twin)*
- So Switch Match is now experimental and off by default (`--switch-match` turns it on; `--no-switch-match` is still
  accepted). It does nothing for the offset itself (16.4) and adds drift where precision matters most. The tablet
  still runs the Switch Match build; the same point with `rev-output/hw45/noise/rc4-1/ultra/[VOLATILE]_ultra-APIP.hex`
  (Switch Match off, twin gate passed) is the A/B still to do. Suites: February and December 528 checks each, pass.

### 16.7 R45-rc4 (2): the true position error, measured along a ruler

- Anargon, on the halfway idea (match each window to the point halfway between the two readings at a switch): "Isnt
  that the equivalent to grabbing the two 'switching' positions of a pen then guessing true center based off of
  that? Also, measure the true position distortion of such a system; we should ideally target 0.1mm max". Yes: the
  halfway point is only right if the two windows are wrong by the same amount in opposite directions, and the
  per-window fit (16.3) says they are not.
- Offline first, on the freehand drags (`rev-output/hw45/drag/drag45.csv`, 6 drags, all the same way), comparing no
  switch handling, Switch Match, Switch Match without its time fade and the halfway point:
  - `experiments/noise45/distort.py`: sideways error from each drag's straight line, the hand's slow bend taken off
    (a +-60-frame mean): 99 % 0.20-0.23 mm and 9-16 % of frames over 0.1 mm for all four.
  - `experiments/noise45/coildist.py`: smooth motion (a cubic per 400 ms) plus one error curve over the position
    within a coil (20 points), shared by all drags. The hand's wobble does not repeat with the coils, so it averages
    out. X 0.146-0.158 mm peak to peak, Y 0.196-0.227, nearly the same for all four. Fitted on each half of the drags
    on its own, the curves differ by up to 0.12-0.23 mm, so freehand drags cannot settle 0.1 mm. *(hardware, offline)*
- So the measurement needs a straight reference: a ruler. I first flashed the silent drag recorder for it (two flashes:
  the recorder, then the rc4sm ULTRA build back, CRC 0xBDDE); Anargon wanted it recorded through the normal firmware's
  cursor instead, so it was. They laid a 15 cm ruler corner to corner (the line came out at 32 degrees) and slid the
  pen along its edge about 5 times each way, lifting at each end: `experiments/vendorlog.exe 90 --log
  rev-output/hw45/ruler/ruler45.log`, 80109 reports in 90 s, 13 strokes, 10 full passes of 140-152 mm at 20-35 mm/s.
- `experiments/noise45/ruler.py LOG --trim` (full passes only, the first and last 150 ms of each and the outer 10 mm
  of the line left out, 114 mm kept; sideways error after a 9-report mean; each pass's own offset taken off, since
  the ruler or the pen angle moved by up to 0.4 mm between passes):
  - repeatable error (the mean at each 0.25 mm of the line over the passes going one way): 0.676 mm peak to peak
    one way, 0.793 the other;
  - its slow part (a +-10 mm mean: the tablet's warp, or the ruler bending): 0.25 / 0.32 mm;
  - its coil-scale part (the rest): 0.64 / 0.62 mm peak to peak, at most 0.33 / 0.35 mm from zero, sd 0.09 / 0.11;
  - forward minus back at the same spot, coil-scale part: up to 0.30 mm, rms 0.07;
  - short sideways steps (a line through 15 reports on each side): median 0.014 mm, 90 % 0.058, 99 % 0.21,
    largest 0.68; 4.4 % over 0.1 mm;
  - scatter between passes at the same spot: sd 0.16 mm, so each mean of about 5 passes still carries about
    0.07 mm of the hand. Without the trimming, the ends of the line reach 1-1.4 mm (the pen landing and lifting, the
    corner of the area).
  *(hardware, cursor output after ALPI, ULTRA with Switch Match on; one line)*
- Reading it: the firmware on the tablet does not meet the 0.1 mm target. The largest part is not the switch step
  but a wave of a few tenths of a millimetre across the coils, plus a slower warp. It is the same with or without
  Switch Match in the offline comparison, so it has to be fixed where the position is worked out (the line
  arithmetic and the cold first read, 16.3), not by matching at switches. One line on one tablet, by hand: the other
  areas, other pressures and pen angles are guesses. What was changed after this is 16.8.

### 16.8 R45-rc4 (2): Window Scale, and Switch Match back on with gates

- Anargon: "So, whats your proposed fix? Don't pack it without adding the fix", then "Actually, dont even propose
  it. Just fix it." Everything below was tried offline on `rev-output/hw45/drag/drag45.csv` (6 drags, one direction,
  ULTRA, the line readings of every frame), each scored on the switch jumps (a line through 15 ms on each side) and,
  where it mattered, on each half of the drags separately.
- **Line-level fixes, none held up** (`experiments/noise45/gainfix.py`, `slotlearn.py`):
  - dividing each line by the stand-measured slot gains (16.3) x 0.5 / 1 / 1.5: X jumps 0.24 -> 0.29-2.4 mm;
  - only the cold line, gain -0.6 .. +0.3: better on one half at best (0.10 / 0.36), worse on the other;
  - leaving the cold line out (the ratio from the other side): 0.81-1.40 mm; the parabola alone: 0.17 / 0.31;
  - a Gaussian peak fit (logs of three lines): 0.26 / 0.24;
  - a gain on the centre line, -4 % .. +13 %: the jump moves by about 0.15 mm per 1 %, best at -1 % on one half
    and 0 % on the other;
  - per-slot gains learned from the four lines two windows share at a switch, linear in pressure: no better on the
    other half (X 0.27 / 0.10, Y 0.19 / 0.11, against 0.24 / 0.09).
  So the readings are too sensitive for a line-by-line correction: a 1 % error in one line's gain is a 0.15 mm jump.
- **The jump as a stretch.** Listing every switch with its pressure (scratch `jcorr.py`): on X the signed jump is
  +0.05 .. +0.41 mm up to pressure about 9500 and -0.09 .. -0.47 mm above about 10000; drag 3 sits about 0.3 mm lower
  than drag 4 at the same pressure (not explained; tilt and the held drive step were not recorded). A stretch k of
  the position inside the window gives a jump of exactly k coils at any switch (derivation in `atf_winscale.cs`).
  `experiments/noise45/scalelearn.py`:
  - learning k on the tablet from the switches (k0 + k1 pressure, rate 0.25-0.5): X 0.11-0.12 mm in one order of the
    drags, 0.22-0.27 in the other (about 40 switches is too few to learn the slope);
  - a fixed k per pressure, fitted on one half and tested on the other: X 0.15-0.17 mm median, 0.31-0.40 at 90 %
    (raw 0.24 / 0.46); Y about unchanged (0.07-0.09);
  - the firmware's table (all drags, medians per 2048 of pressure, knots between bins, the exact integer
    arithmetic): X 0.083 / 0.279, Y 0.050 / 0.218. Knots X 176, 176, 178, 190, 180, -16, -150, -150, -196; Y -32,
    -32, 22, 83, 50, 1, 7, -53, -127 (Q12). Hover: the drags' only hover switches in the fast stage give X +0.068
    (1) and Y -0.011 (3), so the lightest touch's value is used in the air.
  *(hardware recordings, offline)*
- **Switch Match's drift, fixed at its cause.** A minimum speed alone (15 mm/s) did not stop it: in a simulated
  still pen whose window flips every 3-30 frames, the slow part stayed 0.05-0.11 mm (raw 0.02-0.05), because the
  flips inside the 16-frame history make the fitted line look like motion. With the second gate (no switch of that
  axis in the 16 frames before) the output equals raw at every flip rate and noise (0.04 and 0.10 mm). Drags with
  both gates: X 0.045 / Y 0.042 mm median (ungated 0.035 / 0.040). *(simulation, offline; `distort.py` sm_var)*
- **Together (Window Scale, then gated Switch Match), offline:** X 0.037 mm median (90 % 0.17; half 1 0.040 / 0.11),
  Y 0.036 (0.17). The wave across each coil is unchanged (X 0.16, Y 0.22 mm p-p): neither touches it.
- **Firmware.** `atf_winscale.cs`: `ws_x` / `ws_y` replace the two calls of the stock position routine (the calls
  Multi-coil Read takes over; the two are never built together), copy its three stack arguments, call it, and add
  `(r - centre x 1018) x k >> 12` while VB+0 = 0. Setting `wscale` (default on; ULTRA only: the table was measured at
  ULTRA's read length; not with Multi-coil Read), `--no-window-scale`. Switch Match: the 15 mm/s gate for coil-window
  switches (r5 bit 2 marks drive-step and ladder-step switches), the per-axis count at axis block +24, the model
  (`SwitchMatch.Model`, optional step conditions) to match; on by default again (`--no-switch-match`).
- **Twin.** The gate passes; its position-call check now knows Window Scale (`WindowScale.Installed` by the
  wrapper's first instruction, `McTwinBoard.ws`): 1600 of 1600 calls equal `WindowScale.Apply`, 1460 changed. Still
  pen: neither 0.098 / 0.005 mm, Switch Match only 0.101 / 0.006, Window Scale only 0.122 / 0.007, both 0.124 /
  0.008. The twin has no cold reading, so its windows agree and Window Scale adds a k-coil step where X flips; on
  the tablet the step is there to begin with. Suites: February and December, 528 checks each, pass. *(twin)*
- Not done: the tablet. Window Scale's effect has to be checked on the tablet (drags and the ruler again), and its
  table may need other values for another tablet or pen, for the slower hover steps, and for RAPID and FAST.

## Part 17: R45-rc5 - Dynamic Anti-Chatter, Coil Linearization, and the first ruler test of five position ideas

### 17.1 Dynamic Anti-Chatter

- Anargon: "switch match is just not the solution", and asked for Dynamic Anti-Chatter: "Prevents chatter by only
  applying smoothing on detection of chatter", on by default, with as little delay as possible.
- **What it does** (`atf_antichatter.cs`, routine `dachat`, setting `dac`, default on, `--no-anti-chatter`): the
  last stage of every frame, after ALPI. Per axis, d2 = p0 - 2 p1 + p2 (this frame and the two before); when |d2| is
  over 0.10 mm the frame is pulled toward the one before by ws = w x min(1, (|d2| - 0.10) / 0.10), w = 0.4 ms / frame
  period (at most 0.5, one value per Steady Hover step). The history holds inputs, never outputs, so nothing builds
  up; a still pen without chatter is never touched; nothing is predicted.
- **Offline, the 45-degree drags, ULTRA, before ALPI** (`experiments/noise45/dacvariants.py`): X chatter (|d2|,
  99 %) 0.514 -> 0.281 mm, Y 0.424 -> 0.246 mm, mean delay 0.118 / 0.075 ms (the first, full-pull build: 0.275 mm at
  0.180 ms). It does not touch the error that repeats at the same place (switch steps, the coil wave).
  *(hardware recordings, offline)*
- **Firmware:** RAM `0x20001ED0` (16 bytes), cleared on pen loss and in the search restart; threshold halved in
  report mode 2; at most 133 cycles a frame; single-stepped against `DynamicAntiChatter.Model` on 40000 inputs:
  0 mismatches (`experiments/noise45/dacstep.cs`). *(emulator, twin)*

### 17.2 Gemini's roadmap, PR #1 option A, and free CPU time

- Gemini Hydrogen 4's roadmap (`DOCS/roadmap-after-r45.md`) arrived in the same revision.
- **PR #1, option A** (a correction table over the offset from the window's centre coil, knots every 0.1 coil),
  fitted on one half of the drags and tested on the other (`experiments/noise45/compare5.py`): worse than Window
  Scale alone (X switch jump 0.072 -> 0.182 mm on one half, Y coil wave 0.29 -> 0.43 mm); the two halves' tables
  disagree by up to 2 mm at the window edges. Not built. *(hardware recordings, offline)*
- **CPU time at ULTRA** (`experiments/noise45/cpuprof.cs`, twin, 300 ms): the CPU never sleeps; per 1 ms frame
  245 us in the main loop and 782 us in interrupts, mostly the scan's own interrupt code. Nothing moved. *(twin)*
- **The Window Scale twin check now uses the stock routine's own answer** (`McTwinBoard`, hook `H_SR`): its C# copy
  differs on degenerate lines (3392 of 200000 random line sets), and one such frame once failed the ULTRA gate
  (X lines 186,1088,1137,1190,868 centre 10: stock 2799, copy -2799). *(twin)*

### 17.3 Five ideas against the coil-scale error, offline

- Anargon: the coil-scale error "is still bad" (target 0.1 mm), "Make even more ideas, maybe 5 even", "we are only
  keeping the best one and it is opt in by default. These ideas are for our reference only."
- **Coil Linearization** ("code density", `experiments/noise45/coillin.py`): a pen moving at a steady speed spends
  equal time over every part of a coil, so where the reports bunch up the arithmetic squeezes and where they thin
  out it stretches; the running share of reports gives the true phase. On `ruler45.log`, learned on half the passes
  and tested on the other: the coil-scale part 11-26 % smaller, the direction part not better; the drags were too
  short and slow for it.
- **Pen-Anchored Crossfade** (lines chosen by the pen, two 3-line parabolas crossfaded) and **Midpoint Gaussian**
  (a log ratio of four lines): worse offline (X switch 0.25 mm against 0.04 with Window Scale and Switch Match; MG
  chatter 0.93-1.02 mm at 99 %).
- **Cross-Axis Table** (X error by Y phase and back) from the R43 strokes and the ruler: about 0.1 mm peak to peak,
  not repeatable between passes.
- **Both-Window Blend** (read both windows near a midpoint, blend by phase): best in a simulation where the centre
  line reads about 1.9 % low per pressure (`experiments/noise45/fictional.py`; X direction part 0.023 mm against
  Window Scale's 0.077), but it needs a second window read, which ULTRA has no time for. A per-pressure centre gain
  that beat Window Scale in the same simulation failed on the real drags (X jump 0.245 against 0.072 mm), so the
  simulation's single cause is too simple. *(hardware recordings offline; simulation)*

### 17.4 All five built, and checked against their C# copies

- Anargon asked to build all five as firmware, test them in the twin, then on the tablet.
- **Coil Linearization** (`atf_coillin.cs`, routine `clin`, setting `clin`, opt in at first, `--coil-linearization`): per
  axis the phase inside the coil from the R43 grid, then position + T(phase), T linear between 64 knots per coil,
  one table per axis learned on all 10 ruler passes of `ruler45.log`. Memoryless, right before Dynamic Anti-Chatter,
  about 60 cycles per axis; single-stepped 0 mismatches (119 cycles at most).
- **The other four** went into a test-only file, `atf_posideas.cs` (settings `pax`, `mgauss`, `xaxis`, `ablend`).
  Both-Window Blend was built as **Alternating Blend**: within 1/4 coil of a midpoint the window alternates every
  frame and the other window's position is interpolated from the frames before and after, so every position is one
  frame late (twin: +1.02 ms); it replaced Switch Match. `experiments/noise45/ideastep.cs` single-stepped each routine
  against its C# copy: 0 mismatches (the stock routine reads leftover stack on flat lines, so the harness zeroes the
  stack first). All ULTRA + ALPI builds passed the twin gate (`rev-output/hw45/rc5/ultra-{cl,pax,mg,xa,ab}`). The
  twin's "cursor delay" moves about +-1 ms with switch handling (it measures a direction bias), so it says nothing
  about real delay here. *(emulator, twin)*

### 17.5 The tablet ruler test

- **Setup** (2026-11-16): each build flashed in turn (`experiments/noise45/flash.sh`), then Anargon slid the pen
  along a clipped ruler for 90 s; recorded read-only through the normal firmware's cursor reports (`vendorlog.exe`,
  report mode 4, OpenTabletDriver running). Recordings, analyses and readers: `referencedata/` (its README describes
  every file).
- **Scoring** (`experiments/noise45/ruler6.py`): pressed pieces joined across tip-off breaks under 400 ms and 3 mm;
  passes split at turns, kept at 80 mm or more, 150 ms trimmed at each end; one line fitted through all builds and
  one common stretch; sideways error after a 9-report mean, minus each pass's own offset; the coil-locked part
  folded by X and Y coil phase (the X error is the X fold / |sin a|); steps = jumps over 0.3 mm off the 10-report
  trend that stay. The first scorer (`ruler.py`) cut passes at the tip-off breaks the worn surface causes (the pen
  hops over a bump for about 12 ms) and shrank the stretch to 67 mm; that was a bug in the scorer, not short strokes.
- **Round 1** (33 degrees, the same ruler spot for all six; passes got faster with each build):

  | Build | Passes | Speed | X coil-locked | Y coil-locked | Steps/s |
  |---|---|---|---|---|---|
  | rc5 control (WS, SM, DAC) | 9 | 24 mm/s | 0.29 mm | 0.14 mm | 0.1 |
  | Coil Linearization | 17 | 42 mm/s | 0.12 mm | 0.08 mm | 0.1 |
  | Pen-Anchored Crossfade | 20 | 73 mm/s | 0.42 mm | 0.32 mm | 1.1 |
  | Midpoint Gaussian | 23 | 56 mm/s | 0.57 mm | 0.59 mm | 11.9 |
  | Cross-Axis Table | 39 | 102 mm/s | 0.20 mm | 0.15 mm | 0.4 |
  | Alternating Blend | 49 | 154 mm/s | 0.51 mm | 0.34 mm | 15.0 |

  Control against Coil Linearization at matched speeds (15-52 mm/s): X 0.26 -> 0.15 mm, Y 0.14 -> 0.06 mm.
  Pen-Anchored, Midpoint Gaussian and Alternating Blend make real steps of 1.3-2 mm (12-15 a second for MG and AB)
  and double the coil-locked error; Cross-Axis does nothing measurable. *(hardware, cursor)*
- **Round 2** (Coil Linearization against the control on lines it was not learned on): the other diagonal (-34
  degrees) X 0.20 -> 0.08 mm, Y 0.14 -> 0.05 mm, steps 0.8 -> 0.5 /s; a shallow line (-10 degrees, sideways almost
  pure Y, so X is not resolved) Y 0.08 -> 0.065 mm, steps 0.5 -> 0.2 /s. Noise along the line is not worse.
  *(hardware, cursor)*
- **What is left:** 0.05-0.17 mm sd of repeatable error along each line that follows neither axis's coils (worn spots
  on the surface, the ruler, the hand); Coil Linearization does not change it. The 0.1 mm target is met by the
  coil-locked part on two of three lines, not by the whole error.

### 17.6 Decisions

- Anargon chose Coil Linearization for the tablet (`rev-output/hw45/rc5/ultra-cl`, CRC 0xAF0F) and asked to finish
  rc5 with it for publishing. It stayed opt in for rc5 (its table is one tablet and pen); in rc5 (1) Anargon made it
  the default for FAST, RAPID and ULTRA (`--no-coil-linearization` turns it off).
- The four failed ideas are removed from the program (only the best one is kept). `atf_posideas.cs` and the
  single-stepping harness are kept in `experiments/noise45/` for reference. Removing them leaves every build
  byte-identical (ULTRA + ALPI with and without Coil Linearization, FAST with it, FAST, RAPID and ULTRA defaults;
  only the per-build id differs). *(build comparison, twin gate)*
- Not done: Coil Linearization's table for other tablets and pens (it would need a ruler recording of each), and
  for RAPID and FAST read lengths (learned at ULTRA).

### 17.7 rc5 (2): Coil Linearization before ALPI, and the local models

- **Question (Anargon):** can the remaining errors be fixed (forward vs back up to 5 osu!px, the X coil wave)?
- **The ruler recordings (rc5, with and without Coil Linearization):** about half of the forward-minus-back
  difference repeats with the coils. Fitted per pass, each pass's pattern is shifted the way the pen travels, and
  in 5 of 6 recordings the shift grows with speed (about 3-7 ms; other diagonal at about 100 mm/s: 0.2-0.9 mm). So
  it is a delay of the wave, not a hysteresis of the coil window. A first answer said "not delay" from too few
  passes per speed band; corrected. *(hardware recordings, cursor; per-pass shift fit)*
- **Cause:** Coil Linearization ran after ALPI. ALPI (an alpha-beta tracker) follows a straight move with no delay
  but delays a wiggle, so the correction was applied at the wrong place for a moving pen.
- **Offline replay** (`experiments/noise45/lagsim.cs`, gate `lagsim_check.py`): ALPI's C# copy (`Apip.Axis`, the
  touching band of Anargon's training at ULTRA, its own coil correction zeroed) and `CoilLinearization.Apply` on a
  110 mm straight line, 1000 frames/s, 4 passes each way, the wave the table undoes, noise 0.05 mm sd:

  | Speed | ALPI alone: wave delay | ALPI, then CL: fwd-back rms / worst | CL, then ALPI: rms / worst |
  |---|---|---|---|
  | 20 mm/s | 4.6 ms | 0.017 / 0.05 mm | 0.008 / 0.03 mm |
  | 50 mm/s | 5.7 ms | 0.04 / 0.13 mm | 0.009 / 0.03 mm |
  | 100 mm/s | 5.7 ms | 0.08 / 0.22 mm | 0.010 / 0.03 mm |
  | 150 mm/s | 6.6 ms | 0.10 / 0.24 mm | 0.009 / 0.03 mm |

  Other diagonal (-34 deg); 33 and -10 deg the same; at 0.10 mm noise CL first stays at 0.016-0.020 mm rms. CL
  first equals the no-wave noise floor. The table was learned after ALPI from slow passes (20 mm/s, both
  directions), where ALPI barely shifts or flattens the wave (shift 0.09 mm), so it is kept. *(simulation)*
- **Change:** `clin` is called before `apfilt` (`atf_sched.cs`); packaged as rc5 (2). Twin: all six builds pass (ULTRA + ALPI with and without Coil Linearization, FAST with and without, RAPID, ULTRA), ALPI equal to its C# copy on all 848 frames; only the ULTRA + ALPI + Coil Linearization build changes (two calls swapped, 0x0800D30C and 0x0800D310), the others are byte-identical to rc5 (1) but for the build id. *(twin, build comparison)*
  Not yet on the tablet: a ruler test (forward vs back at several speeds) needs a flash and Anargon.
- **Local models:** Anargon asked to offload code writing to Anargon's GPU (vibeslop swarm). The replay was a swarm task:
  OmniCoder-9B ran out of its 60 minutes (one answer rebuilt the ALPI model every frame, about 100 s per check),
  Devstral Small 2 24B ended with compile errors and the wrong structure (direction of travel taken for the axis);
  Anargon: take its last complete answer and fix it, and Claude rewrote all but the argument parsing. With the
  earlier bake-off (20 models on the Dynamic Anti-Chatter routine), Anargon set a 32B multi-agent session as the
  minimum for this project: AGENTS.md section 2a.

### 17.8 rc5 (3): Measurement Tools, the still pen noise test, and Standard's measuring as an entry

- **Requests (Anargon, 2026-11-16/17):** the Filter Arranger "is a test suite type class, so it should not be part of
  the primary tablet patcher"; tool 3 becomes "the whole measurement suite", and models are "required to contribute
  to the measurement suite instead of having them run their own py's" (AGENTS.md rule 18). Then: "Build and package
  the still pen noise test from standard, user calibration too. Dont remove any user facing calibrations though."
  Asked back: two entries (the still-pen noise test, and Standard's measuring as Tablet signal thresholds), the
  wizard's step 4 kept; package = this zip. The noise test is also the testing Anargon wants before the lowest-noise
  defaults change ("lets do testing first").
- **Measurement Tools (`atf_measure.cs`):** an `IMeasurement` per entry (name, label, text, its screen), listed in
  `MeasurementTools.ALL`. Entries: the Filter Arranger (moved from the patcher), the Still Pen Noise Test, Tablet
  signal thresholds. The first screen warns that the tools are only allowed for the listed tablets (`Cli.TOOLS_TABLETS`,
  for now the Deco 640) and that only tool 1 was thoroughly tested by a human. The startup backup runs only for tool 1.
- **Still Pen Noise Test (`atf_stillnoise.cs`):** reads the normal firmware's pen reports (report ID 7, 1/100 mm)
  with calibration's reader (`P.HidOpenPen`, Raw Input on Windows), no flashing; scores a still hold the way
  `experiments/noise45/noisept.py` did, with every spike kept in (Anargon: rank by the noise held, spikes in): sd and
  highest-to-lowest per axis, the rms frame-to-frame step and the steps over 0.05 / 0.10 mm. A step across a gap over
  3x the usual report interval is not counted (the reader can drop a few reports between its 1 s chunks), but the
  gap's jump still widens the swing. On the Filter Arranger firmware each hold carries the order in use, and `o`
  sets the next one through `ArrangerOps.SetOrder` (it starts at the next pen loss; the screen will not measure while
  one is waiting). Saved: `export/measurements/still-noise` (.csv per hold, `still-noise.txt` one line per hold).
- **Tablet signal thresholds (`atf_thresholds.cs`):** runs `Cli.Calibrate` (the same code as step 4) for one preset
  with the default settings and Anargon's fallback, from the stock dump `Cli.PickInput` finds. `Cli.calStandalone`
  changes only the texts that promised a build after it (the step title, `d` at the readiness check, the last prompt);
  the wizard never sets it. The measurement firmware has no cursor, so the entry then offers to flash back what the
  backup before it read (`Cli.tabletApp`, written as `previous-firmware.hex`), unless that backup is damaged.
  `Cli.calSource` lets the gate replace the tablet reader (null in the program).
- **Gate (scratch, `mtgate.cs`, compiled with the product sources, no USB):** 34 checks. The scoring on
  hand-made reports (alternating 0.01 mm: sd 0.005 mm, step rms 0.01 mm; three 0.11 mm spikes: 6 steps over both
  limits; a 10 ms gap; lost, calibration and tip reports); the screen in the twin on ULTRA (about 1000 reports/s,
  its numbers recounted from the saved .csv), the ULTRA + ALPI lab build (the build's order shown, `o 1 6 10` taken
  and the next hold labelled `SWM > ALPI > DAC`, `o 99` refused) and the FAST lab build (about 500 reports/s); the
  thresholds entry end to end with a fake xppen-tools (dfu, dump, update, dfu, update: the flashed measurement
  firmware is the built one and the twin measures that image, the firmware put back equals the backup byte for byte,
  the saved file loads with `--calibration`, the LED zone was set), stopping with `d` and leaving it with `s`, and
  the wizard's step 4 texts and values unchanged. A twin-only bug was found and fixed on the way: the twin reader
  restarted its clock every second, so a 2 s hold read as 1 s.
- **Not done:** no run on the real tablet (testing filter orders needs Anargon's hands and a lab build flashed by
  Anargon). README and DOCS do not describe Measurement Tools yet (docs on request).
