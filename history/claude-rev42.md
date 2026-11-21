# R42 - Claude Opus 5.5 (Anthropic), 2026-11-09

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

Model: Claude Opus 5.5 (`claude-opus-5-5`), Anthropic. Same conversation as R41. Requests are Anargon's.

## Part 1: "DAMAGED" on a stock dump with one change

### Request

Anargon tested Mily's dump (`ic902-main.bin`, February build) with R41 and got "This firmware is DAMAGED: its end (from
0x0800C188: the start-up table and .data) is missing or wrong, so the firmware cannot start" and, in the recovery tool,
"THIS DUMP CANNOT BE REPAIRED BY THIS TOOL. Code bytes are damaged (pages 0x08004000)". R41 had called the same file
flash-compatible. Anargon suspected that xppen-tools does not save the compressed `.data` in its dumps, and asked for
a diagnosis first ("drop all prior instructions until you specifically finish tasks in relation to this issue").
Context in `rev-output/firmware-w-dates/NOTE.txt`: Mily's tablet already ran a mod (Mei's, derived from Anargon's
February firmware) with Mily's own changes; Anomy's December tablet was damaged after a flash of one of Mei's mods;
Doodle's is a December dump; the four `.hex` files are Mei's mods.

### Findings

- **Mily's dump is complete and not damaged.** It differs from Anargon's stock February dump in exactly 32 bytes:
  - the settings page words 2 - 6 erased (normal after any update);
  - the dead-band coefficients and floor at `0x080041C4`-`0x08004258` set to 0 (Movement Smoothing off);
  - the two bInterval literals in the compressed `.data` (`0x0800C63B`, `0x0800C658`) 3 -> 1.

  These are byte for byte this tool's own `SmoothingOff` and `Binterval` patches. Its end runs to `0x0800C7CB`
  like stock. *(dump analysis)*
- **R41's verdict was a false positive of this tool.** `Repair.LooksDamaged` counted any difference from the stored
  stock end as damage, so the two bInterval bytes made it "missing or wrong". `Repair.Analyse` counted any code page
  whose CRC differed as "damaged code bytes", so the smoothing page (`0x08004000`) needed a stock copy. Reproduced
  with the R41 build on a copy of the file. *(code analysis, reproduced)*
- **xppen-tools' `main.bin` dumps are complete.** Every `main.bin` on Anargon's PC from a healthy tablet runs to the
  firmware end. The current xppen-tools (`xppen-tools.exe`, 360448 bytes, sha256 `0619b4f0...`) flashed the R41
  measurement build in full ("Range: 0x08003800 + 0x9834 bytes"), and its dump read back all 38964 bytes identical,
  3756 bytes past `0x0800C188`, with 0xFF after the end. Each dump is CRC-checked against the device ("Local
  CRC16/XMODEM: 0x6E6B / Device range CRC: 0x6E6B"). *(hardware logs from R41 Part 5)*
- **The bytes are lost in the restore image, not the dump.** `xppen-tools dump` writes four files: `ic902-main.bin`
  "full code flash", `ic902-bootloader.bin`, `ic902-code.bin` "application image" and `ic902-stock.hex`
  "application restore image".
  - **Older releases** (files named `deco01v3-*`) cut `code.bin` at a fixed 35208 bytes (`0x8988`, ending at
    `0x0800C188`), and `stock.hex` is that code.bin converted.
  - **Bundles checked:** the two older backup bundles on Anargon's PC each hold a 35208-byte `deco01v3-stock.hex`.
    The February one is byte-identical to `xppen-tools ihex deco01v3-code.bin --ic 902` with today's xppen-tools;
    the December one is identical to the same conversion of the December code.
  - **The current release** cuts at the last non-0xFF byte. That is complete for a healthy tablet, but it reproduces
    the damage for a damaged one.

  *(xppen-tools strings and offline runs of `ihex` / `patch`)*
- **xppen-tools' offline commands work on 35208-byte images only.**
  - `xppen-tools patch` lists "Deco 01 V3 (deco01v3): 35208 bytes" as the only accepted Deco image. It refuses a
    `main.bin` ("Could not identify a supported patch target"). It also refuses both stock code.bins: "does not have
    the recovered Deco 01 V3 application vector". Its built-in profiles (`500hz`, `500hz-no-smoothing`,
    `stock-no-smoothing`) target another build, as rev 14 found for its bInterval offsets.
  - `xppen-tools ihex --ic 902` refuses a `main.bin` ("reset vector is invalid") and converts a 35208-byte code.bin to
    a 35208-byte hex.

  *(offline runs, no device)*
- **That restore image reproduces both damaged tablets exactly.** The simulation: start from the healthy dump, erase
  `0x08003800`-`0x0800FFFF` (the bootloader's ERASE), then program the 35208-byte hex of that build's code.bin. The
  result equals the damaged dump in all 51200 application bytes, both for Anomy's December tablet and for Anargon's
  February tablet in rev 35 (`backups/ic902-main.bin`). On December the cut also drops the last 12 code bytes. The
  code is the tablet's own stock code, so what was written was that tablet's stock firmware up to `0x0800C188`, not
  one of Mei's mods. A complete stock image flashed by an older xppen-tools `update` that stopped at `0x0800C188`
  (rev 3's flash log: expected CRC of the complete image `0x5497`, device `0x7F73`) leaves the same bytes, so the
  dumps cannot tell the two apart. All four of Mei's hex files are complete (`0x08003800`-`0x0800C7CB`, 36812 bytes). xppen-tools'
  verification passes on such a flash, because it checks the CRC of what it wrote and the reset vector, and both are
  in the first 35208 bytes. *(simulation against the dumps)*
- **Cut files on Anargon's PC** (scan of the whole `xppen-tools-windows-x64(1)` folder, 111 complete hex files):
  - `deco01v3-stock.hex` in the root, `misc/deco01v3-stock.hex` and `misc/stock.hex`;
  - `backups/deco01v3-code.bin` and `backups/deco01v3-stock.hex`;
  - `backups/ic902-code.bin` and `backups/ic902-stock.hex` (the dump of the damaged tablet, which also stops there);
  - `firmware/anargon-backups1/` and `firmware/anargon-backups2/`: `deco01v3-code.bin` and `deco01v3-stock.hex`;
  - a third backup folder under `firmware/`: `deco01v3-code.bin` and `deco01v3-stock-external.hex`;
  - also `misc/deco640-stockoriginalmain.txt` (the same February hex under a `.txt` name, found by a second scan by
    content).

  At Anargon's request all 14 were moved to `xppen-tools-windows-x64(1)/QUARANTINE-cut-firmware-DO-NOT-FLASH/`,
  sorted by corruption type (A: February, start-up table and `.data` missing; B: December, also the last 12 code
  bytes), then by original folder. Its `README.txt` and `MOVED.tsv` give the original paths and SHA-256s. *(scan)*
- **Doodle's December dump is stock** (it matches the stored December CRC). Anomy's code pages, from a different
  tablet, are identical to it. *(dump analysis)*

### Changes

- `atf_repair.cs`:
  - **`Repair.Examine`** compares a firmware with the verified build its string names, and returns a `FwCheck`.
  - **Known edit sites (`EDIT_AT` / `EDIT_STOCK` / `EDIT_KIND`):** this tool's own patch sites, per build, with their
    stock values from both stock dumps. February has 19, December 18: the dead-band immediates and floor, ADC samples,
    excitation pulses, step poll, step tick, AGC cap (2), position averages (2) and the two bInterval literals.
  - **Undoing a code page:** a page that differs is undone at its known sites. It counts as edited only if it then
    matches its stored CRC; otherwise it is changed in an unknown way.
  - **The end (from `0x0800C188`):** known literals count as edits; other bytes count as erased or other; all are
    restored from the stored copy.
  - **Exact stock:** `stock` is set only when nothing unknown is left and the whole-image CRC-32 matches.
  - **`Damaged()` means an erased end** (16 or more bytes; the real cases have 1599 and 1611). `LooksDamaged` uses it.
- **The damage message** now says "erased" and names the likely cause. New texts: `UndoneText`, `ChangedText`,
  `TabletText`.
- **`Repair.Analyse`** uses `Examine`. Known changes are listed as "Changed by another mod (not damage), undone: ...".
  Only unknown page changes need a stock copy. An edited, complete dump is "No damage found ... with changes made by
  another mod". The written stock firmware is exact.
- **`Build.ReadInput`:**
  - an erased end is refused as damaged;
  - known edits whose result is exact stock: it builds from that stock and `InputInfo.undone` says what it undid (log,
    wizard warning, `CheckInput`);
  - a truncated input with only known edits gets them undone and stays truncated;
  - unknown changes in the end, or known edits with unknown code changes, are refused with `ChangedText`;
  - unknown code-only changes go on as before (layout mismatch warnings).
- **`Cli.Note`** (every backup): the DAMAGED box only for an erased end; a dump with known edits gets one line ("The
  tablet runs ... with changes from another mod (...). Its firmware is complete, not damaged.").
- **`BackupCapture.CheckRestoreImages`** runs after every successful dump. An `ic902-code.bin` / `ic902-stock.hex` that
  stops before the firmware end (the known build's end, else the scatter-load table) is renamed to
  `*.INCOMPLETE-DO-NOT-FLASH`. The rename goes into `backup.log` and is shown (startup backup: a warning line;
  before a flash: printed).
- `VERSION` and `RELEASE_VERSION` R42. gd32emu unchanged (still R41).

### Verification (Windows 11, .NET Framework csc /langversion:5; Java 25 via cs2java)

| Check | Result |
|---|---|
| R42 recovery on Mily's dump | "Changed by another mod (not damage), undone: Movement Smoothing off (dead band set to 0), USB report interval 3 -> 1 ms"; no damage; stock written; twin checks pass |
| R42 recovery on Anomy's / Doodle's dumps | unchanged: 1611 of 1616 bytes erased, restored / no damage |
| `Examine` on Anargon's stock, Anargon's rev 35 damaged dump | stock / damaged (1599 erased) |
| RAPID built from Mily's dump vs from Anargon's stock dump | identical except the 4-byte per-build ID at `0x0800C850` (a CRC of the build time) |
| Core suite, February and December stock | 161 passed each (17 new: edit table vs stock dump, this tool's patches = the listed sites, undo to exact stock, names and values, input read as stock, mixed page refused as changed, erased end refused as damaged, recovery of erased / edited / both, unknown `.data` change not damage, restore images renamed / kept) |
| Host suite | 101 passed (3 new: an older xppen-tools' 35208-byte bundle renamed and reported, recorded in backup.log) |
| Calibration suite, emulator tests, twin self-test | 104, 55, ok |
| Java translation (shared code) | compiles; recovery on Mily's dump gives the same verdict |

Nothing was flashed for R42. The hardware evidence is R41's flash and dump logs.

### Still open

- Dual-coil gain measurement (Anargon's R42 request, paused for this). Anargon has moved the pen since R41's
  measurements, so it needs new ones; the step must be easy to repeat.
- What exactly was flashed onto Anomy's tablet is not known: a 35208-byte restore image, or a complete stock image
  through an older xppen-tools `update`. The dump only shows that tablet's own stock code, written to `0x0800C188`.
  Anomy's flash log (or the xppen-tools version used) would settle it.

## Part 2: Dual-coil gains on the real tablet (2026-11-09)

Three updates (bootloader count 16 -> 19), all gated in the twin first. Data: `Claude-workarea/rev-output/hw42/`.

| | Dual-coil (measured gains) | Plain |
|---|---|---|
| tip resting, 15 s: X / Y shake (sd) | 0.111 / 0.105 mm | 0.032 / 0.046 mm |
| slow stroke: reports over 0.5 mm off the path (X / Y) | 334 / 49 | 0 / 0 |
| coil-pitch jumps | none (R41: 27 in 15 s) | none |

- **Gains:** X 1.30 / 1.27, Y 1.25 / 1.24 (centre-1 / centre+1); now `Sched.Cfg.DUAL_DEFAULT`.
- **Not a pure gain:** the second-coil reading has a floor (about 72 in /4 units). Its ratio also changes per coil,
  with height, and between touching and hovering.
- **Verdict:** the jumps are gone, but it is shakier than plain, so it stays off.

## Part 3: wording, names, and a new look at multi-coil

### Requests (Anargon)
- Restore `/100,000` after the flash count ("a standard courtesy for all firmware tools").
- Rename Hover Boost to something that says it trades report rate for stability. Files with 7/8/9 (the hover
  settings) off get one name for people who hover as a skill.
- `[VOLATILE]_[SPEED].hex`.
- Accept other mods' benign edits (bInterval and the like) all the way.
- Rewrite every explanation: shorter, plain words, more formatting. Use Anargon's measurement example, with the
  Terminology note at the end of every output. Note the style in AGENTS.md.
- Model labels:
  - R36 and R37: Codex (GPT 6 Astra Ultra [Max thinking with Multiagentic Capability]);
  - revisions 12-30: Claude Opus 4.8 (Max effort);
  - tables ordered by date.
- Do not bin multi-coil: rethink it (a "depth field" per coil).
- Physical tests last, including noise per speed on the real tablet.

### Findings
- **Stock position formula:** `0x08005432` (X at `0x080083BE`, Y at `0x0800840A`). Five line sums: a = c-2, b = c-1,
  c, d = c+1, e = c+2. W = 1018.
  - Parabola: P2 = cc*W - (b-d)*W / (2(2c-b-d)).
  - 4-line ratio: P1 = cc*W + (b-d)*W / (b-c-d+e) if d >= b, else / (a-b-c+d).
  - Result: the mean of the two, or P2 alone if they differ by more than W.

  The C# copy matched every twin frame. It differs only when the peak sits at entry 1: the firmware then reads past
  the buffer, and the next frame re-centres. *(code analysis, twin)*
- **Why R42 Dual-coil was shakier:** it replaced the two lines that matter most (b and d) with weaker ring-down
  reads. It added no reads. *(analysis)*
- **Multi-coil potential (twin, Q 150, 0-8 mm):** a fit to all five lines with a known profile could reach this
  much less position noise than stock:

  | Noise type | Touching | 8 mm |
  |---|---|---|
  | same on every line | 1.5x | 2.4x |
  | grows with signal | 2.8x | 3.3x |

  Real data decides which noise type applies. *(twin)*
- **When to copy the lines:** at report time, the X line buffer was already cleared for the next frame in 449 of 1200
  reports. At frame end (the `tilt` hook) both buffers and both positions are whole. *(twin)*
- **Stock's slower hover delay (twin):**

  | Stock with | Drawing | Hovering |
  |---|---|---|
  | both filters | 16.9 ms | 22.7 ms |
  | no Movement Smoothing | 14.6 ms | 17.2 ms |
  | neither filter | 2.1 ms | 2.8 ms |

  So the extra hover delay is mostly Movement Smoothing, partly Position Interpolation. *(twin)*
- **Mei's mods** change only known sites (smoothing, samples 45->4, pulses 40->16/20/28, poll, tick, bInterval).
  Dumps of tablets running them build from exact stock. *(real files)*
- **Patcher packages (rev36b-rc):** they ran unsigned C# from ZIPs in-process. RC37 dropped the loader, and R42 has
  none. *(code review)*

### Changes
- `Cli.CounterText`, calibration readout: `N/100,000`; AGENTS.md rules 13-14 and section 3a.
- Setting `boost` named Steady Hover (tag `SH`); `Build.RawHover`, `-rawhover`; `Build.VOLATILE_PREFIX`.
- Text rewrite: settings table, shared texts, `Cli.Block` / `Cli.Heading` / `Cli.Terminology` measurement format,
  gate check lines, calibration results, recovery tool, flashing, backup.
- Line recorder: `Cfg.lineRec`, `Options.lineRec`, `Sched.LR_ADDR`; snapshot at `tilt`, pages in `tsend` on even
  frames; `Twin.Tracked` counts pages `0xF0`-`0xF4`.
- Model relabel and date-ordered tables in both projects.

### Verification
- Core suite 164, calibration 111 (February and December), host 101 (February only, as in R41: its fake dump is a
  February build). Java translation compiles and passes the twin self-test.
- Recorder images for QUICK, FAST, RAPID and ULTRA (`rev-output/hw42b/`) pass the program's own twin gate
  (`--check`). Every decoded copy matched a real frame end.

## Part 4: no XP-Pen firmware bytes in the source

Anargon: "gauge how much copyrighted material is on the patcher and emulator (just vaguely) and try to see if theres
any way to avoid copyright issues by actually eliminating them rather than circumventing them", then: "I somehow want
to keep signature finding while not breaking copyright", and "i also somehow want to remove the stock reference but
that breaks repairability if you cant find a way".

### What was there
| What | Where | Bytes |
|---|---|---|
| The February and December firmware ends | `atf_stockref.cs` (`TAIL`) | 3220 |
| Signature windows | `atf_symbols.cs` | 1162 literal bytes in 91 windows |
| Instruction checks at patch sites | patcher | 87 in 29 checks |
| Search patterns | patcher, twin | 58 in 7 arrays |
| gd32emu | - | none |

### Findings
- **What the end holds** (February, 1604 bytes from `0x0800C188`): the last 53 entries of the X linearisation
  table, the Y linearisation table (48), a 76-entry curve table, the 76-entry pressure breakpoints (`k * 2400`), the
  pressure curve (8 + 8 points), the start-up table (2 entries) and the compressed `.data` (495 bytes; 1224 bytes
  unpacked: USB and HID descriptors, the capability string, callback pointers, a few settings). Measured tables
  cannot be rebuilt from facts, so rebuilding the end without a copy would mean retyping XP-Pen's tables.
  *(dump analysis; decompressor read from `0x0800405C`)*
- **December's end** is February's with the code shift applied: the tables are identical, 72 bytes differ (pointers
  +12, and the compressor's choices around them). Converting one into the other would need armlink's exact
  compressor, so a copy must be of the same build. *(dump comparison)*
- **The user's own copy is almost always there.** Every older xppen-tools `dump` wrote a complete `main.bin` next to
  its cut `code.bin` / `stock.hex`, in the same folder. On Anargon's PC: 22 exact February ends, 3 exact December
  ends, 136 February ends that differ only by this tool's 1 ms edit, 12 erased. *(file survey)*
- **Hashes vs encodings:** a SHA-256 fingerprint of a 12+ byte window cannot be turned back into the bytes. Parity,
  XOR or compression of the end would still carry its content, so they were not used.
- **Public repository `NiceAesth/xppen-dumps`, `stock/Deco 640`** (git blob SHA-1 from the GitHub API listing,
  compared with local files; nothing downloaded): `ic902-main.bin` = Anargon's February dump; `ic902-bootloader.bin`
  = its bootloader area; `ic902-code.bin` and `ic902-stock.hex` = the cut 35208-byte restore image, byte for byte
  the quarantined files (the size alone shows it: 99059 bytes of Intel HEX is exactly 35208 data bytes). No December
  Deco 640 dump there. The `Deco 01 V3` folders hold a different firmware (different bootloader).

### Changes
- `atf_symbols.cs`: `FEB|offset|mask|quick16|fp64|DEC` (converted from the byte windows, windows unchanged);
  `tools/mksig.py` writes this format (`quick16`, `fingerprint`). `Layout.Parse` / `Resolve` match by quick check,
  then fingerprint; `Sha256` in `atf_layout.cs` (longs, for the Java translation).
- `atf_stockref.cs`: `TAIL` removed, `END_CRC` added (`0xDEB66FA2` February, `0xD49B3513` December).
- `atf_repair.cs`: `Examine(app, n, known, refEnd)`; `FwCheck.endOk`, `tailCounted`; `Analyse(full, folder, roots)`;
  `SearchRoots`, `FindEnd`, `EndOf`; `RepairResult.needCopy`, `endFile`, `searched`; the "no copy" page (`NeedCopy`).
- `P.ListFiles`, `P.FileSize` (gd32emu `P` and the Java `P` in `tools/cs2java.py`).
- Tests: core `NoVendorBytes` (SHA-256 test answers, signature format), repair with no copy, with an edited dump,
  with a built `.hex`, and a copy with a changed end skipped; host `FakeEnd` (the fake dump's end is made to match
  `END_CRC` by solving 32 bit flips, with the start-up table's addresses and sizes).
- AGENTS.md rule 15; README "Firmware files"; DESIGN, CONTRIBUTING, FIRMWARE_VERSIONS, CHANGELOG.
- Stray files moved to `rev-output/R42-strays/` (a tablet dump, built images, an exe, logs, a test folder).

### Verification
- All 178 firmware files on Anargon's PC (stock, Mily, Mei, every build, the cut files): identical resolved
  addresses and match counts, byte signatures vs fingerprints. About 4.5 ms per image instead of 2.5.
- Anargon's and Anomy's damaged dumps, each with a healthy dump of the same build next to it: patcher, stock image
  and repaired dump byte-identical to the build before this change (C# and Java). Alone: the "no copy" page.
- Core 170, calibration 111, host 101 with February; with December core and calibration pass and the host suite
  stops at `TabletMatches` as before (its fake tablet is February).
- Java translation compiles, passes the self-test and gives the same `--compat` output on both stock dumps.
