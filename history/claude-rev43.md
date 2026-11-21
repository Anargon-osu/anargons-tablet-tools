# R43 - Claude Opus 5.5 (Anthropic), 2026-11-09

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

Model: Claude Opus 5.5 (`claude-opus-5-5`), Anthropic. Same conversation as R42. Requests are Anargon's. R42 was
packaged first ("R43 is when testing will commence"). Data: `Claude-workarea/rev-output/hw43` (`still/`, `move/`,
`fit/`, each with a `RESULTS.md`); tools: `Claude-workarea/experiments`.

## Part 1: still tests

### Request

"Tablet is ready, do as many still tests as possible first, BEFORE we start with any moving tests." Then: an extended
pass with the pen resting, and all four extra passes.

### What was done

- Flashed the R42 line-recorder builds (`rev-output/hw42b`, twin-gated in R42) at QUICK, FAST, RAPID and ULTRA, and
  logged 30 s of reports each with `experiments/hwdual.exe log`. 16 updates, each CRC- and vector-verified by
  xppen-tools. Anargon set the pen up in a stand and confirmed before every pen step.
- Passes:
  - tip resting in the middle;
  - hover (Anargon modified the stand; notebooks blocked too much signal);
  - near the lower-left edge (a low hover, not touching);
  - "highest" hover (lower than the hover pass);
  - held in the hand.

### Findings

- **ULTRA while hovering:** false touches in up to 40 % of reports (pressure up to about 5000), about 1.7 mm off
  the other speeds, and wobbling about 1 mm.
- **FAST at the weakest hover:** lost 6.5 s of 30 s in short gaps (longest 180 ms), at a line peak of about 1100.
  No gaps at 1450, or with the tip down.
- **RAPID and ULTRA lines at hover are nearly flat:** a floor of about 300 / 220 with the pen adding only 20-50. This
  weak signal is the root cause of both problems above.
- The stock formula plus a constant offset equals the firmware's position on every centre-peak copy (R42's finding,
  confirmed on hardware).

## Part 2: moving tests and the coil grid

### Request

"The moving tests are REALLY important because these will reveal coil positions. Make them dual pass." Later: skip
QUICK ("multi coil read isnt really worth it below 500hz"), and skip a second vertical pass if FAST agrees to within
1 mm ("our tablet coil area is standardized").

### What was done

- **Strokes:** 75 s ruler strokes, side to side and up and down, two passes each at ULTRA and RAPID. FAST got two
  side-to-side passes and one up-and-down pass. A coverage map was printed after every recording.
- **Afterwards:** calibrated plain R42 RAPID was flashed back (CRC 0xFA2E).
- **Coil centres:** a coil's centre is where its two neighbours read the same. Per coil, the reported mm was fitted
  against the floor-free 3-line offset. X was taken from the up-and-down strokes and Y from the side-to-side ones,
  with the other direction as a lag check (`experiments/coilpos.py`, `coilmerge.py`, `coilcmp.py`).

### Findings

- **The coils sit on an even 0.2 inch (5.08 mm) grid** in reported mm, the same at every speed within 0.24 mm:
  - X: 32 coils, coil k at 163.53 - 5.080 k (coil 1 at the right);
  - Y: 19 coils, 96.42 - 5.083 k (coil 1 at the bottom).
- **The edges:** the second coil from each edge is pulled 0.3-0.6 mm off the grid, and the outermost coil is at or
  past the edge of the reported area.
- **ULTRA:** sent 2 wild reports, off the drawing area.
- **Fix:** `experiments/linefit.py` assumed 4.97 / 4.74 mm per coil; corrected to 5.08.

## Part 3: the fit

### Request

"Yes, do the fit now, but also try to rebuild the entirety of the multi coil read stack now based off of this new
information, with the goal of just making it a dynamic general purpose multi coil reading." Then: "the coils may
potentially be multi layered and not circular, so any terminology in relation to that should just relate to the
coil's center and effective range rather than a specific shape" (AGENTS.md rule 16).

### Findings (offline, on the recordings)

- **The floor.** A floor plus bell-curve fit (`experiments/floorfit.py`) showed a floor of about 55-66 per read at
  weak signal. With a strong signal the fitted floor went negative, which shows the bell shape is wrong.
- **The floor, exactly.** Lines of coils that do not exist (past the edges) read 216 at ULTRA, 288 at RAPID and 632
  at FAST, stable to 0 / 0 / +-8. That is about 72 per conversion; the sum holds one conversion fewer than the
  reads. The firmware uses `69 x reads - 60`, which fits all three within 3.
- **The response, measured with no shape assumed** (`experiments/coilfit.py`). Each copy was normalised by its
  five-line sum, which does not force any line to 1; a first pass scaled by the largest line made a fake flat top.
  - Response: 1, 0.92 at 0.5 pitch, 0.70 at 1, 0.40 at 1.5, 0.115 at 2, about 0 from 2.5.
  - Effective range: 1.34 pitches at touch.
  - The same on both axes, and at RAPID and ULTRA; FAST is 4 % wider.
- **Line noise** (`experiments/linenoise.py`). It depends on the line's place in the reading order and on where the
  pen sits:
  - FAST's centre-2 line is 4-20x noisier than the others;
  - at ULTRA, four lines move together (the pen signal wobbling).
- **Best possible noise** (`experiments/bound.py`). A fit weighted by each recording's own line noise came near the
  best possible: RAPID resting Y 0.43 -> 0.05 mm. But weights taken from one recording made strokes noisier
  (`experiments/straight.py`), so they do not carry over. With equal weights, strokes were as straight as stock or
  straighter.
- **Effective range per frame vs tracked.** Choosing it per frame was noisy. Tracking it slowly helped at the edges
  and at the highest hover (FAST high X 0.30 -> 0.22 mm) and cost a little at mid hover.
- **Fitting the floor in every frame** was worse than the fixed floor.

## Part 4: Multi-coil Read

### Design (`atf_multicoil.cs`; specification `experiments/mcint.py`)

- **Hooks:** both calls of the stock position routine (X `0x080083BE`, Y `0x0800840A`) go to the new routine. It
  calls the stock routine itself, then:
  - removes the floor (`69 x reads - 60`, reads = samples - AGC delay);
  - leaves out lines past the edges;
  - runs two Gauss-Newton steps on the pen offset against the measured response (equal weights, the pen signal
    eliminated, at most 0.3 pitch per step);
  - tracks each axis's effective range (Q8 scale 0.8-1.8x; one axis per frame checks a wider or narrower range and
    moves 4/256 toward the better fit).
- **Integer arithmetic only:** the firmware cannot count on the FPU, and the emulator does not run it.
- **Fallback:** the stock result unchanged when the peak is off the centre line, the reads are not positive, the stock
  result is more than a pitch away, or a step fails.
- **Speed-ups,** after a first version cost 83 us per frame:
  - the five sums kept in r8-r12 with `mla`;
  - divides once per pass (reciprocals);
  - the range check on one axis per frame.

  Worst frame now 59.2 us more than stock (RAPID, pen swept 0-8 mm, measured pen response), charged `MC_US = 64`.
  RAPID keeps 35 us spare; ULTRA has none, so there the setting is n/a.
- **Measured gains, offline, with the final integer arithmetic.** Frame-to-frame noise, stock -> Multi-coil:

  | Case | Stock | Multi-coil |
  |---|---|---|
  | RAPID resting, Y | 0.42 mm | 0.19 mm |
  | RAPID held in the hand, X | 0.27 mm | 0.14 mm |
  | RAPID hovering, X / Y | 0.76 / 0.73 mm | 0.45 / 0.43 mm |
  | FAST, highest hover, X / Y | 0.30 / 0.27 mm | 0.21 / 0.18 mm |
  | ULTRA hovering, X / Y | 1.16 / 1.79 mm | 0.81 / 1.07 mm |

  Up to 10 % worse in 3 of 30 cases: FAST and ULTRA tip X, and FAST hover Y. Two correction steps instead of one
  matter at hover (RAPID hover Y 0.43 vs 0.52 mm).

### Removed

- Dual-coil Mode: the second-coil read, the dual-coil search hooks, the probe telemetry page, the gains and their
  calibration, `DUAL_DEFAULT`, `--dual-coil` and `--rx2-gain`. The flags now give a clear message. Page 4 of old
  measurement builds is still accepted and ignored, and `dual_gain_*` keys in old calibration files are ignored.

### Signatures

- Regenerated with the write log of a full build matrix plus an R42 Dual-coil build, so bytes that build rewrote stay
  masked and R42 Dual-coil dumps still resolve.
- 3 added (`0x08005432`, `0x080083BE`, `0x0800840A`; December -4 / +12 / +12 as the offset map says), 5 removed
  (only Dual-coil used them). The other 86 are byte-identical.

### Twin

- **The pen model** uses `MultiCoil.RESP` at an effective range (1.34 pitches at touch, widening with height by the
  old geometry). This replaces the bell curve (half response at 1.47).
- **Why it mattered:** with the bell curve, Multi-coil Read looked 4x shakier at FAST in the twin (model mismatch;
  the twin's touch signal also drives the AGC to 7 of 10 reads, unlike the tablet). With the measured response, every
  preset passes and Multi-coil is as steady as stock or steadier: FAST sliding error 4.35 -> 3.40 mm.
- **No floor in the twin.** Adding 72 per conversion to the twin's ADC passed every gate too, but whether pen
  detection sees that floor on the tablet is not known, so it was left out.
- **The gate** runs `MultiCoil.TwinCheck` on every build with Multi-coil Read. The pen is swept across the coils at
  touch and 5 mm; every position call is compared with `MultiCoil.Calc` (given the firmware's own stock result).
- **Results** (`experiments/mccheck.cs`, measured pen response): RAPID 19,140 calls, all equal; FAST the same.
- **The C# stock copy is not exact on degenerate input** (`experiments/stockcall.cs`). All-zero lines give the
  centre coil in the firmware and 0 in the copy; random large sums differ in sign (3392 of 200000). On real sums they
  agree, so `Calc` takes the firmware's stock result as an input.

### Verification

| Check | Result |
|---|---|
| Core suite, February / December | 187 / 187 passed (R42: 170) |
| Host suite, February | 101 passed (December stops at `TabletMatches`, as in R42: its fake tablet is a February build) |
| Calibration suite, February / December | 111 / 111 passed |
| C# copy vs the Python specification, 60 recorded line copies | equal |
| Firmware vs C# copy in the twin (RAPID, FAST, 0-8 mm) | equal on every call |
| Gate: FAST and RAPID with Multi-coil Read; ULTRA marks it n/a | pass |

### Open

- **Multi-coil Read has not run on the tablet.** First test: RAPID with and without it, the pen still at touch and at
  hover, then ruler strokes.
- **The default pick-up / drop levels for ULTRA (64 / 48) and RAPID (93 / 72) are at or below the measured floor of
  about 72 per conversion,** if pen detection's sample carries the floor too. That could explain ULTRA's false
  touches. Not checked: the line recorder does not record that sample.
- **Lines centre-3 / centre+3** (the outer pair, read on tilt frames) are not recorded either. Recording them would
  allow a floor measured on the tablet while running, and a wider window at hover.

## Part 5: Multi-coil Read for ULTRA (second version)

### Request

Anargon: make Multi-coil Read work at ULTRA, to find the tablet's true report-rate limit later (possibly by sending
the data as a raw USB stream and reading it with an OpenTabletDriver I/O driver).

### What was done

- **ULTRA's real frame measured:** the scan takes about 844 us of the 1000 us frame. That, not the arithmetic, is the
  limit on reports per second. The receiver sits behind analog switches, so coils are read one at a time (two only
  if 8 apart); the position arithmetic runs after the reads.
- **The arithmetic rebuilt to replace the stock routine's instead of running after it** (`atf_multicoil.cs`):
  - gates first, using only the arguments: where one fails, the stock routine is entered with the caller's own
    registers, stack and return address;
  - start at the 3-line parabola on a 1/16-pitch grid; two Gauss-Newton steps, each reading one table word per line
    (response and its change, 91 words), 16-bit multiplies, three divides per step;
  - the effective range fixed at 17/16 of the measured one; no RAM.
- **Tests:** `experiments/mccheck.cs` (cost and twin check per preset); the core suite's Multi-coil checks rewritten
  (64 recorded line copies against `mcint.py`, every gate, both edges, ULTRA offered, cost against `MC_US`).

### Findings

- **Fixed vs tracked effective range** (geometric mean of frame-to-frame noise / stock, 30 still axis recordings):

  | Version | Noise vs stock | Lower than stock |
  |---|---|---|
  | First version (tracked per axis) | 0.73 | 25 of 30 |
  | New arithmetic, tracked | 0.72 | 26 of 30 |
  | New arithmetic, fixed 17/16 | 0.69 | 27 of 30 |

  The 3 worse cases (FAST tip X, FAST hover Y, ULTRA tip X) are at most 1.15 x stock in every version.
  *(hardware recordings, offline)*
- **Cost, single-stepped alone on 6000 inputs** (no interrupts):

  | Path | Cycles |
  |---|---|
  | Stock routine | 503 mean, 544 most |
  | Multi-coil Read, fit | 282 most |
  | Multi-coil Read, gates then stock routine | stock + 31 most |

  So a frame takes less time with it than without. Charged `MC_US = 1`. The twin's costliest X + Y frame varies by
  tens of cycles with the interrupts that land in it (ULTRA 1779 without, 1481 with), so the suite uses the
  single-stepped numbers. *(emulator)*

### Verification

| Check | Result |
|---|---|
| C# copy vs `mcint.py`, 64 recorded line copies | equal |
| Firmware vs C# copy, 6000 single-stepped inputs, February and December | 0 differ (fit used on 2503) |
| Firmware vs C# copy in the twin, FAST / RAPID / ULTRA, 0-6 mm, February | 0 differ of 1500 / 2314 / 2902 calls |
| Firmware vs C# copy in the twin, ULTRA, December | 0 differ of 2902 calls |
| Core suite, February / December | 192 / 192 passed |
| Host suite, February | 101 passed (December stops at `TabletMatches`, as before) |
| Calibration suite, February / December | 111 / 111 passed |

### Open

- **Not yet run on the tablet.** First test: ULTRA and RAPID with and without it, the pen still at touch and at
  hover, then ruler strokes.

## Part 6: review of R38-R43

### Request

Anargon: check and review the latest revisions that have no review yet. `AUDIT.md` and `DOCS/verification.md`
cover R36 and R37 (Codex); R38-R43 had none.

### Checked and found right

- **R43 assembler encodings** (`Sdiv`, `Mla`, `LdrSp`, `EncImm`, `DpImmW`, `DpRegW`, `LdrbW` / `LdrW` / `StrW`,
  `LslRW`, `Clz`, `Smla`, `It`, `EncBccW`) against the ARM Architecture Reference Manual. One latent note:
  `LsrW` / `AsrW` with shift 0 would encode a shift of 32; nothing calls them with 0.
- **The emulator instructions Multi-coil Read relies on** (gd32emu, unchanged since R42): IT block advance, flags set
  only outside an IT block, `MLA` / `MLS`, `SMLAxy` (N = bit 5, M = bit 4), `CLZ`.
- **Multi-coil Read:** gates, fit, twin hooks (both calls, the table), cost and frame budget; the twin's pen model.
- **R42 `Repair.Examine` / `Analyse` / `FindEnd` / `EndOf` / `ScanDir`:** every decision is CRC-guarded; folder
  listings catch their exceptions.
- **R42 fingerprints:** `Sha256` and the masked Quick16 + 64-bit search in `Layout`.
- **R42 line recorder:** its RAM (`0x20001040`-`0x2000106F`) overlaps nothing (stock stays below `0x20000E70`, our
  blocks end at `0x20000FA7`, string 173's buffer starts at `0x20001F80`); every page holds what its comment says;
  the copy (frame end) and the send (report time) cannot overlap, since a copy is taken only when nothing is left to
  send and the pages count is set last.

Not covered in this pass: the R41 Raw Input reader and the R38 console and process changes beyond the startup
backup.

### Findings

| # | Severity | Finding | Fix |
|---|---|---|---|
| 1 | Medium | `Build.ReadInput` (R42) accepted a verified build with a code page changed in an unknown way when no known edit was present, with no warning. One flipped bit in any code page was enough (4 of 4 places tried on February stock). R41 refused such input, and R42's design says it is refused. | Refused with `Repair.ChangedText`, with or without known edits. Pages past the end of a truncated input are counted apart (`pagesMissing`), not as changed. |
| 2 | Low | `BackupCapture.CheckRestoreImages` (R42): a cut restore image that could not be renamed (locked, read-only) threw, so a saved, good backup was reported as "Backup failed" and could not be used. | The rename failure is reported ("could not be renamed ...: do not flash it") and the backup counts. |
| 3 | Low | `Layout.For`'s cache reused the last layout for any image with the same CRC-32, length and build string. A real collision is about 1 in 4 billion, but the result would be another image's addresses. | It keeps a copy of the bytes and compares them. |
| 4 | Test gap | The host suite never passed with the December fixture (R41-R43; its fake tablet always ran the February build) or with no fixture (R42 and R43 checked; its fake tablet dumped zeros). Both stopped at `TabletMatches` in the measurement-firmware check. | The fake tablet runs the fixture's build, or the stock-like image when there is no fixture. |
| 5 | Test gap | The host suite's stock-like image (zeros, with a build string and an end matching `END_CRC`) only passed the input check because of finding 1. | Each of its code pages is made to match `PAGE_CRC` the same way as its end (`ForgeCrc`). With every page and the end matching, its whole CRC-32 equals the build's, which is how finding 3 showed up. |
| 6 | Note | With a zero denominator the stock position routine returns the previous call's value. Multi-coil Read's gates never take that path. | None needed. |
| 7 | Note | The emulator's cycle model is approximate (1 per instruction, 3 per taken branch, +1 per memory access; a real divide takes 2-12 cycles and flash has wait states). Multi-coil Read's margin to stock (282 against 544 cycles) is large enough that this does not change the result. | None needed. |

### Verification

| Check | Result |
|---|---|
| A one-bit change in a code page of February stock, no known edit (4 places) | before: accepted, no warning; now: refused, "changed by another mod in places this tool does not know" |
| Core suite, February / December (new: an unknown-only change is refused; an unrenamable restore image is reported without an exception) | 194 / 194 passed |
| Host suite, February / December / no fixture | 101 / 101 / 90 passed (December and no fixture: first full pass) |
| Calibration suite, February / December | 111 / 111 passed |
| Product build (`csc`, C# 5) | builds, no warnings |

### Open

- Not covered: the R41 Raw Input reader, and the R38 console and process changes beyond the startup backup.

## Part 7: Multi-coil Read on the tablet (ULTRA)

### Request

Anargon: test Multi-coil Read on the real tablet at ULTRA (1000 Hz), with the line maths readable live while the pen
is in use. Chosen: a measurement build first, then a normal build.

### What was done

- **Tool:** `experiments/mclive.cs` (built against this tree). Modes: `build` (ULTRA line recorder + Multi-coil,
  twin-checked), `normal`, `rec` / `pen` (live readout once a second plus a CSV), `analyse`, `dump` (one line per
  copy). It recomputes both the stock routine and Multi-coil from the recorded lines, so both are compared on the
  same frames.
- **Measurement build** (`rev-output/hw43/multicoil/`, February stock): product gate passed; flashed, CRC 0xCA28
  verified. Five 30 s still passes, Anargon confirming each: tip in the middle, hover, lower-left edge (done twice;
  the first was a coil further out than R43's), highest hover, held in the hand. At 1000 Hz the normal hover is
  already the highest the pen reaches (Anargon), so hover and highest are two takes of one height.
- **Normal build** (`rev-output/hw43/multicoil-normal/`, defaults + Multi-coil, no calibration: the saved one is for
  RAPID): product gate passed; flashed, CRC 0xCEDD verified. No live log: Anargon had no time for it.

### Findings

- **The firmware runs Multi-coil Read on the tablet:** in the middle, its position equals the C# copy on 100 % of
  copies on both axes in every pass (stock: 0-13 %). At the edges it follows Multi-coil on a smooth curve (95 %
  within about 1 unit), not stock (hundreds off).
- **Noise on the same frames, Multi-coil against stock** (SD of value minus the mean of 8 neighbours each side):

  | Pass | X | Y |
  |---|---|---|
  | Tip in the middle | 0.140 vs 0.210 mm (0.67x) | 0.215 vs 0.154 mm (1.40x) |
  | Hover | 0.885 vs 1.236 mm (0.72x) | 0.602 vs 0.965 mm (0.62x) |
  | Lower-left edge (second take) | 0.975 vs 0.835 mm (1.17x) | 0.649 vs 0.788 mm (0.82x) |
  | Highest hover | 0.811 vs 0.959 mm (0.85x) | 1.185 vs 1.892 mm (0.63x) |
  | Held in the hand | 0.192 vs 0.203 mm (0.95x) | 0.084 vs 0.111 mm (0.76x) |

  Lower in 8 of 10, and on both axes at every hover. Higher in two, both small and with the pen down or at an edge.
  R43's offline run had the tip-in-the-middle pair the other way round (X 1.15x, Y 0.70x), so where the pen sits
  between coils matters more there than the method.
- **Reported positions** (what the PC gets): hover X 0.70 mm with 123 jumps, Y 0.60 mm with 20; R43's stock pass
  had 846 and 880 jumps. Other day, stand not identical: a hint, not a measurement.
- **The recorded position is not the routine's result plus a constant.** It is a straight line of it in the middle
  (X 0.9983 x result + 1528, Y 0.9973 x result + 548; twin fit, worst 0.9 off), bends near the edges, and is held
  at a limit past them (the first edge take: reported Y 89.99 mm the whole time).

### Open

- Strokes and live use on the normal build (not done: no time).
- Tip in the middle, Y 1.40x: check where in the pitch the pen sat and whether the fit is weaker there.
- RAPID with and without Multi-coil Read on the tablet.

## Part 8: more readings at ULTRA, and why the readings step by 8

### Request

Anargon: ULTRA is still too noisy; with the pen at a very low hover in the middle (it stays there; the tablet cannot
be moved), try reading configurations that fit 1 ms.

### What was done

- **Budget:** ULTRA's busiest frame is 844 us of scan plus 130 us of processing against 975 us (1000 less the
  25 us margin); 4 samples and 16 pulses are both at their minimums, so the product accepts nothing else. The twin's
  busiest frame is 964 us of 1000 (processing at most 122 us), so about 30 us is really free.
- **Builds** (ULTRA, line recorder, Multi-coil Read; B and C from `experiments/burst-src`, a copy of the sources with
  the 25 us margin removed, experiment only): A 4 samples / 16 pulses, B 5 / 16, C 4 / 17. All passed the product
  gate; twin busiest frame A 964, B 985, C 991 us; frame period 1000 us in all three, at touch and at 4 mm.
- **Tablet:** each flashed (CRC 0x29BC, 0xFE8B, 0x3886, all verified) and recorded 30 s; the pen did not move
  (mean position within 0.2 mm).

### Findings

| Build | Lines, X | Lines, Y | Reported X / Y | Reports per second |
|---|---|---|---|---|
| A: 4 samples, 16 pulses | 0.364 mm | 0.390 mm | 0.135 / 0.164 mm | 999.7 |
| B: 5 samples, 16 pulses | 0.364 mm | 0.198 mm | 0.118 / 0.082 mm | 999.5 |
| C: 4 samples, 17 pulses | 0.444 mm | 0.301 mm | 0.194 / 0.160 mm | 994.7 |

- **B is the better buy** (Y halved, X the same, no reports lost); C lost about 5 reports a second on the tablet.
- **Every reading is rounded down to a multiple of 8 by the stock firmware:** its sampling loop clears the lowest
  3 bits of each 12-bit ADC result before storing it (`bic #7` at 0x08004720, February). The ADC itself is set to
  12 bits, right-aligned, no oversampling. At this hover the pen adds about 18 per reading over a floor of about
  54, so about 2 steps: the rounding is a large part of the noise. Stock's 45 samples hide it; ULTRA's 4 do not.
  The twin copies the rounding in its ADC model (`atf_twin.cs`, `& ~7`) instead of leaving it to the firmware.

### Open

- Keep all 12 bits (remove the rounding): no latency, no time cost. The Multi-coil floor (`69 x reads - 60`) was
  measured on rounded readings and moves up by about 3.5 per reading; the twin's ADC model should then return the
  full value.
- Algorithmic Position Interpolation (Anargon's idea: noise learned on the PC, a denoiser in the firmware replacing
  Position Interpolation).
- The tablet is on build C (measurement build, no cursor).

## Part 9: 12-bit Readings

### Request

Anargon: test 12-bit readings, as a setting in the noise section, on by default. (Also asked: in the newly added
step, the 30 s centre hold of Algorithmic Position Interpolation's training, the LED blinks only while the pen is
within 2 mm of the centre of the drawing area, and the tool says so. That step does not exist yet; it comes with
Algorithmic Position Interpolation.)

### What was done

- **New setting "12-bit Readings"** (`fullres`, noise section, on by default, file tag `12B`, every paced preset;
  STOCK keeps the vendor's readings). It replaces the stock `bic r0, r0, #7` at 0x08004720 (December 0x0800471C)
  with two `mov r0, r0`. That loop is the firmware's only ADC data read (checked: no other use of the data
  register).
- **Levels kept equal:** the cleared bits average 3.5, so a 12-bit build adds `Build.FULLRES_BIAS` (4) to every
  pick-up, drop and boost level. The levels are compared with single readings (the strongest line's last sample),
  and every level this tool sets, saved calibration files included, is in rounded readings. The calibration pass
  build turns 12-bit Readings off, so new calibration files stay in rounded readings too.
- **Multi-coil Read's floor** follows the setting: `73 x reads - 62` with 12-bit readings (3.5 per read higher,
  exact at 4 reads), `69 x reads - 60` without. The firmware and `MultiCoil.Calc` take the mode; the twin checks
  detect it from the image (`Patches.FullResIn`).
- **Twin ADC model:** returns all 12 bits (`& ~7` removed). Stock still sees multiples of 8, because its own loop
  clears them.
- **Signature:** 1 added (0x08004720), from `tools/mksig.py` with the R43 write log plus the new write; every other
  line unchanged.
- **Tests:** core suite: on by default, not on STOCK, only a 12-bit build replaces the instruction, rounded readings
  are all multiples of 8 in the twin and 12-bit ones mostly not, the pen is tracked the same (within 5 %), and the
  Multi-coil twin check with both floors. February and December: core 205, host 101, calibration 111
  passed each.
- **Tablet:** build D (ULTRA, 4 samples, 16 pulses, Multi-coil Read, line recorder, 12-bit), passed the product
  gate; flashed (CRC 0x6CD0, verified) and recorded 30 s at the same low hover
  (`rev-output/hw43/fullres/lowhover-D-12bit-30s.csv`). The firmware equalled the C# copy (12-bit floor) on 100 %.

### Findings

Per-second noise, median over 30 s (`mclive seconds`; mm). A, B, C are Part 8's rounded builds.

| Build | Multi-coil X | Stock arithmetic X | Multi-coil Y | Stock arithmetic Y | Reported X | Reported Y |
|---|---|---|---|---|---|---|
| A: 4 / 16, rounded | 0.367 | 0.471 | 0.372 | 0.400 | 0.133 | 0.144 |
| B: 5 / 16, rounded | 0.363 | 0.407 | 0.190 | 0.228 | 0.116 | 0.072 |
| D: 4 / 16, 12-bit | 0.340 | 0.192 | 0.126 | 0.145 | 0.115 | 0.054 |

- **12-bit Readings is the bigger win than any reading length:** against A, the reported (cursor) noise is 0.38x
  on Y and 0.86x on X; the stock arithmetic on the same lines 0.41x (X) and 0.36x (Y). D beats B on every number
  without B's longer readings.
- **Multi-coil Read's X is now the limit:** 0.34-0.37 mm in A, B and D alike, so not from the readings. Its X
  results fall into several clusters 0.2-0.5 mm apart (the stock arithmetic's are one smooth spread), and their
  mean sits about 0.9 mm from stock's. At this hover its result depends on where the fit starts (a fit to the
  touch-measured response, a fixed effective range, two steps). With 12-bit readings the stock arithmetic is
  steadier on X here (0.19 against 0.34).
- Y varied during D (seconds 14-18 and 26-29 noisier on every number), most likely the pen or something nearby;
  the medians leave those seconds out.
- **Against Anargon's 1x reference (stock ULTRA: stock arithmetic, rounded readings, Position Interpolation):**
  first estimated from A (X 0.171, Y 0.155 mm), then recorded (Part 11).

## Part 11: the 1x reference and 12-bit without Multi-coil Read, on the tablet

### Request

Anargon: test item 11 (Multi-coil Read) on the tablet: flash twice and measure. Also: keep this work in R43 (it was
never closed); check the window is wide enough and otherwise make the user zoom out with Ctrl and -; leave item 3's
alignment alone. Algorithmic Position Interpolation will supersede both Position Interpolation and Movement
Smoothing, learned from a long stretch of osu! play over the whole area.

### What was done

- `experiments/mclive.cs`: `--nomc` builds the measurement build without Multi-coil Read (file
  `ultra-linerec-*.hex`; the twin check then wants the stock arithmetic on every copy).
- Build E (ULTRA 4 / 16, rounded readings, no Multi-coil: the 1x reference with the line recorder) and build F
  (the same with 12-bit Readings) passed the twin check and the product gate; flashed (E CRC 0x9835, F CRC 0x0B8E,
  both verified) and recorded 30 s each at the same low hover (`rev-output/hw43/nomc/`). The firmware equalled the
  stock-arithmetic C# copy on 100 % of copies in both.
- Item 11's text now gives these results instead of "Not yet tried on a tablet"; item 5 (12-bit Readings) now
  gives F against E (0.38x sideways, 0.26x up and down) instead of D against A.
- Suites after these changes, February and December: core 205, host 101, calibration 111 passed each.
- **Window width:** every screen (`Screen.Step`, `Screen.DangerAsk`) first calls `Screen.FitWidth`: when the
  console window is 96 columns or narrower it says so and waits until it is wider (zoom out with Ctrl and -, or
  Ctrl + mouse wheel, or widen the window). Skipped when output is not a console window (`P.WindowWidth`, new in
  gd32emu, -1 then). The Terminology lines were rewrapped to fit 96 columns (they were 107).

### Findings

Reported (cursor) noise, per-second medians over 30 s (mm), against E:

| Build | Readings | Multi-coil | X | Y | 2D | vs 1x |
|---|---|---|---|---|---|---|
| E | rounded | off | 0.196 | 0.156 | 0.251 | 1x (reference) |
| A | rounded | on | 0.133 | 0.144 | 0.196 | 0.78x |
| D | 12-bit | on | 0.115 | 0.054 | 0.127 | 0.51x |
| F | 12-bit | off | 0.074 | 0.040 | 0.084 | 0.34x |

- **12-bit Readings alone (F) is the quietest:** 2.6x less shake on X, 3.9x less on Y, 3.0x less overall than
  stock ULTRA.
- **Multi-coil Read helps only with rounded readings** (A against E: X 0.68x, Y 0.92x). With 12-bit Readings it
  makes the cursor shakier (D against F: X 1.6x, Y 1.4x): its fit is now the noise source, most on X.
- The estimate from A was close (E measured X 0.196, Y 0.156 against the estimated 0.171, 0.155).
- E's Y varied (25-75 % range 0.143-0.263); its median is used.

### Open

- Multi-coil Read at hover with 12-bit readings: fix the fit (see Part 9) or leave it off by default (it is).
- A few rarely shown messages (calibration errors, recovery steps) are still wider than 96 columns.
- The tablet first got a normal ULTRA build with Multi-coil Read and 12-bit Readings, as Anargon asked (CRC
  0x982D), then, also at Anargon's request, the quieter one: normal ULTRA with 12-bit Readings and no Multi-coil
  Read (`mclive normal --nomc`; passed the product gate; CRC 0x227B, verified). Both send about 900 pen reports
  per second with the pen at the low hover, as report ID 2 on the vendor interface (the tablet driver's mode), so
  `mclive pen`, which reads ID 7, saw none; their cursor noise was not recorded.

## Part 12: R44 spec (Anargon; not built)

- **Calibration positions per step, with the LED:** each step has its own target (for example the true centre of
  the drawing area). The LED turns on only while the cursor is inside a 7 mm box around it, and blinks only while
  the step records. The PC has to tell the measurement firmware when a step records (a new PC-to-tablet command).
- **Calibration must stay simple for most people,** the new pen training included. Checked against this revision's
  flow: five short Enter-driven steps (25 s of recording, repeatable, optional, defaults if skipped) are fine. The
  hard parts are the two flashes with no cursor in between, reading "under 2048" while touching lightly, guessing
  the middle (the LED box fixes this), holding the pen at the detection limit (step 4), the 10 s slow lift, and
  being told to close the tablet driver.
- **Algorithmic Position Interpolation:** pen noise is mostly predictable. The tool trains on the user's PC (about
  10 min of general tablet use over the whole area, then a centre hold) and writes the steps that reverse the noise
  into flash, where the firmware runs them. It replaces Position Interpolation and Movement Smoothing, must run
  quickly, and must add less cursor delay than Aggressive Position Interpolation (twin, ULTRA: about 8 ms). Noise is
  defined as a gradient over distance and resonance (signal strength). The training firmware has to send cursor
  reports and raw readings together, with the tablet driver running.
- **Refinements (Anargon, same day):**
  - The LED box is 7 mm each way (14 x 14 mm), and only steps that already had a target position get one (centre,
    bottom left, ...).
  - The centre hold works the same as the other steps (that box) and goes back to its length before 30 s.
  - Calibration warns that there is no cursor, so no pen-button presses or mouse use interrupt it.
  - Light touch: under 4096 (2048 was too light).
  - Detection limit, in Anargon's words: "Move the pen up until the point where the LED stops shining, then bring
    it down until it starts shining again. This is your tablet's detection limit. Hold it at that position during
    calibration."
  - The slow lift is not timed: it follows the signal strength and stops collecting as soon as the pen is lost.
  - The driver: just exit the OpenTabletDriver window, or XP-Pen's driver from the system tray (on Anargon's PC it
    is "XPPenTablet", 4.0.18, and it was running: it is what puts the tablet in its own report mode, ID 2).

### Open

- Multi-coil Read on X at hover: test fit variants offline on D's recorded lines (more steps to convergence, a
  start that does not jump, the effective range at hover).
- Algorithmic Position Interpolation, with the LED in its centre hold (within 2 mm of the drawing-area centre).
- Rename Multi-coil Read to "Enhanced Coil Reading" (Anargon).
- The tablet is on build D (measurement build, no cursor).

## Part 10: xppen-tools dumps checked for truncation

- Every firmware file in github.com/NiceAesth/xppen-dumps was checked against the image's own end (its scatter-load
  table): only `stock/Deco 640` is cut (code.bin and stock.hex stop at 0x0800C188, 1604 bytes before 0x0800C7CC).
  The other IC-902 folders are complete (padded to the same 35208 bytes), and so are the IC-901 and IC-903 ones.
- A real `dump` with Anargon's xppen-tools (the Sep 19 build) of build C (ends 0x0800D9E0) gave code.bin and
  stock.hex to the last byte, byte-exact: this build does not cut. The cut files came from older builds.
  (`dump` does not enter DFU itself: `dfu`, then `dump DIR --pid FFF1`, then `exit-dfu`.)
