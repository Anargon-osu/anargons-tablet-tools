# Claude outputs in this chat: revision 33

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

Source: this chat (conversation 4, continued after revision 32). Model: Claude Opus 5.5.

| Revision | Package | Request |
|---|---|---|
| **33** | deco640-rev33.zip | After the December analysis: build fixes (a) and (b) "WITH (c)", and make (c) flexible so that ANY firmware revision can be applied if XP-Pen keeps moving the offsets; worst case, users go back to the February or December build |

---

## Revision 33

### The request, as understood
- (a) The compatibility check must cover every firmware function, table and literal the added code uses, and report
  the max-pressure fix correctly.
- (b) The firmware's end must come from the dump, not from February's `0x0800C7CC`.
- (c) Not a December offset table, but something that works for later XP-Pen builds too, in the patcher **and** the
  digital twin.

### Delivered
- **`deco640_layout.cs` (new):** `Layout` resolves every firmware location for one image; `Fw.A(feb)` / `Fw.Ptr(feb)`
  for patch code; `Layout.FindEnd` (scatter-load table); `Layout.KNOWN` (February and December: build string, end,
  CRC-32 of the stock image) with a cross-check of every resolved address.
- **`deco640_symbols.cs` (generated):** 82 signatures. **`tools/mksig.py` (new):** the generator (see `DESIGN.md`,
  "Firmware layout", for how to run it).
- **Patcher:** every firmware address in `deco640_patcher.cs` and `deco640_sched.cs` goes through `Fw.A` / `Fw.Ptr`
  (107 uses). `PROBE_TABLE` and `SMOOTH_IMM` removed: the probe list is read through the firmware's own pointer
  (`Fw.Ptr(0x08005E38)`), and Movement Smoothing walks the dead-band setup's `movs`/`strb` run.
  `PressureClamp` reads both table pointers with `Fw.Ptr` and searches only the firmware region.
- **`Image`:** `Begin` / `Fail` make every modification group transactional; `Expect` compares a BL/B.W by its
  resolved target; added code starts at the firmware end (also for truncated inputs, as before).
- **`Build`:** `ReadInput` takes the end from the scatter-load table (else the known build's end, else the last
  non-0xFF byte); an input without the table is truncated; `info.layout`; the log shows "N/82 firmware locations
  found; offset from the 2025-02-14 layout: ..." and any location not found; the unknown-build warning now explains
  that locations are found by code and points to the verified builds. `STOCK_END`, `STOCK_CRC`, `BUILD_KNOWN`,
  `BUILD_PUBLIC` removed.
- **Twin:** the tablet model's hooks and the AGC check's call resolve from the image (`Twin.LayoutOf`); the code-end
  bounds come from the layout.
- **Build files:** `deco640.csproj` and `build.cmd` compile the two new files.

### How it was verified (twin and Java translation; no hardware)

| Check | Result |
|---|---|
| February: 100+ builds (5 presets x 10 setting combinations x bInterval on/off, plus return-to-stock and calibration pass), compared with revision 32 with a fixed clock | byte-identical |
| February truncated input (`0x8988` bytes, xppen-tools size) | identical builds; only the skip message lost its address |
| December, the same matrix: every patch-log line mapped back with the offset map | 3806 lines, 0 differences (calls differ only in encoding); added code the same size, 12 bytes later |
| December resolution | 82/82; offsets found 0 / -2 / -4 / +12 = the hand-made map |
| December compatibility check | 0 of 9 incompatible (revision 32: 6 of 9) |
| December, all five presets, full twin check | all pass; frame timing equal to February; latency within 0.4 ms (STOCK preset 16.0 vs 17.9 ms) |
| December + Bootloader Counter | passes |
| Corrupted patch site | only that modification flagged |
| Signature duplicated elsewhere | refused as ambiguous (its modification flagged) |
| Already-built image as input | all modifications incompatible (refused), as before |
| 4 simulated future builds: December relinked with 16 bytes inserted at `0x0800538C`, `0x08006F5C`, `0x080084E2`, `0x0800A70C` (branches, literals and pointers fixed, `.data` stored uncompressed with a copy routine; build string changed) | stock image passes the twin; 80/82 found (the two compressed-`.data` bytes missing, so the 1 ms USB fix is left out); all presets build and pass the twin |
| Same simulated build with December's build string kept | every moved location distrusted by the known-build cross-check (as designed) |

### Found and fixed along the way
- A signature window starting in the second half of a 32-bit call was decoded out of sync, leaving the call's target
  bits unmasked; the generator now decodes from 64 bytes before the window and masks any BL-looking pair.
- A window reaching back across a function start broke when code was inserted between functions; the generator now
  prefers windows inside the symbol's own function (4 of 82 still cross one).
- The scatter-load table's copy-routine words are even (no Thumb bit); the first table detector required odd ones.
- A truncated input made the missing `.data` location abort the whole build (revision 32 skipped just that fix);
  restored.
- The real C# build files list sources explicitly; the two new files were added.

### Follow-ups in revision 33 (Anargon's hardware report and requests)
- Project renamed **Anargon's Tablet Firmware** (folder, executable and code keep `deco640`).
- Chip marking: Appendix A's original "UG902C" was right; the revision-31 "correction" was removed everywhere.
- **Settings:** section "Warranty and Firmware Discreetness"; Discreet split into **Volatile Changes** (`.data`,
  default on, required for RAPID/ULTRA) and **Bootloader Changes** (default off, permanent); Bootloader Counter needs
  Bootloader Changes and is no longer marked experimental (Anargon: confirmed working). Warranty text shortened and
  split the same way. Every setting has a one-sentence tagline. `--no-volatile`, `--bootloader-changes`; `--discreet`
  = both off.
- **LED** (`Patches.LedOn`, `ledtick`, `ledgate`): the four stock "off" calls (`0x080047E6` pen leaves, `0x080052C4`,
  `0x08005838` start-up, `0x080086CA` 1 Hz hover blink) now switch it on, or go through the calibration blink. The
  blink is time-based (millisecond counter, 200 ms on / off) instead of frame-counted, runs every frame and in the
  search, so it stays steady with or without a pen. Twin (new GPIO output model in gd32emu): normal build lit
  throughout with and without a pen; calibration-pass build blinks 2.5 Hz with and without a pen.
- **Tablet Calibration "always LOST"**: the Windows reader opened one HID collection; it now opens all of the
  tablet's collections and keeps report-ID-7 reports from any; with nothing received it says why. Steps can be
  redone (Enter) or passed (Shift+Enter, or `n` + Enter where Shift cannot be detected). Not yet tested on Windows.
- "You are done! Click on me again if you want to reflash your firmware." after a successful flash.
- **Pressure check:** the clamp is on the frequency-table index, not on pressure; pressure reaches 16383 as in stock.
- **Interpolation check:** Aggressive Position Interpolation's windows are reports, not ms, and noise defeats its
  speed test (shaky when still); setting text corrected; time-based fix proposed (CLAUDE.md, section 0).
- Review findings from the (usage-limited) review run, checked and fixed inline: twin-only hooks missing in a
  build no longer crash the gate; truncated dumps of unknown builds and dumps cut inside `.data` handled; no false
  "not the untouched build" warning for a truncated verified build.

### Not changed / still open
- Open issue 8: the tablet's own firmware is still not read before flashing.
- A build that changes code at a patch site (as December did in the dead-band function) needs a person to look; the
  tool leaves that modification out.
- `--check IMAGE` prints "1 ms reports" for any image: it shows the interval it expects, not one it measured
  (behaviour from earlier revisions, unchanged).
- Nothing in this revision has run on a real tablet.
