# Claude revision map — rev1 to rev11

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

**Scope:** built only from this chat (the visible conversation plus the compaction note at its start). No memory files, no transcript file. Some tool results were cleared from the chat; numbers that come from those are taken from Claude's own message text and marked *(per message)*.

**Numbering:** "Reply N" = the Nth assistant response after the compaction note. "Rev N" = the Nth time a file was delivered (a `present_files` call).

---

## How many revisions?

- **11 delivered outputs** (rev1–rev11). This document is a 12th delivery and is not counted.
- **29 assistant replies** before this one:
  - 11 delivered files
  - 3 changed a file but never delivered it (Replies 17, 19, 26)
  - 15 were inline only (analysis, lists, diagrams, advice)
- **`deco640_patcher.py` has 7 distinct states** (P1–P7). 5 were delivered; 2 never were: P4 (superseded by P5) and **P7, the current file, which contains the pressure clamp**.
- **Not counted:** the first session, before compaction. The compaction note only says that it produced the patcher, the stock-complete hex, a raw hex, max_1000/800 and lownoise_R2/R3/R4/R9 preset hexes, and a README.

## Index

| Rev | Reply | Delivered | Headline |
|---|---|---|---|
| 1 | 1 | patcher (P1), README, stock-complete hex | 4-axis matrix + step-by-step wizard |
| 2 | 3 | patcher (P2), README, stock-complete hex | explanation header, step-5 flash hand-off |
| 3 | 7 | patcher (P3), README | xppen-tools does not write `.data` |
| 4 | 10 | `deco01v3-data.bin`, stock-complete hex, `deco01v3-main.bin` | dump comparison + recovery files |
| 5 | 18 | `deco01v3-data-pokeflasher.hex` | self-writing `.data` restorer |
| 6 | 20 | patcher (P5) | stale `--speed` argparse fix |
| 7 | 21 | patcher (P6) | rates reverted to 220/400/600/800/1000 |
| 8 | 23 | two `droptest` hexes | strong/mid-sweep diagnostics |
| 9 | 25 | `deco640_fastscan_pressurefix.hex` | pressure-index clamp |
| 10 | 27 | `summary.md` (v1) | project summary |
| 11 | 28 | `summary.md` (v2) | cold-start handoff + new open issues |

---

## Per-revision detail

### Rev 1 — Reply 1 — patcher P1
- **Sent:** `build_matrix()`, `estimate()`, `wizard()`; SPEEDS 220 Stock / 400 Quick / 600 Fast / 800 Rapid / 1000 Ultra; reduction levels Low/Med/High; four export types; CLI `--wizard --speed --interp --reduction --export`. README section added. Old fixed-preset hexes and `deco01v3-main.bin` removed from outputs.
- **Discovered:**
  - Ultra + Direct + High estimates 2.3 ms cursor / 8 ms detection window, matching the user's example.
  - Any speed ≥ 400 adds cave code, so those builds are always complete (erase-safe), never truncated.
  - Tool-based export leaves a scratch `.bin`; it is now auto-wiped.
  - Stock's detection estimate wrongly used the (ignored) reduction setting; fixed (4.5 ms) *(per message)*.
- **Hiccup:** first README edit failed (no "## Presets" header); re-inserted before the audit section.

### Rev 2 — Reply 3 — patcher P2
- **Sent:** 5-line step header at the top, `TEARDOWN_URL` / `RECOVERY_URL` placeholders, `flash_via_tool()`, wizard Step 5 ("push to tablet? 1/0"), CLI `--flash --pid --runtime-pid` (defaults FFF1 / 2904).
- **Discovered:** nothing new about the hardware. Flash step tested with xppen-tools absent (prints the manual command).
- **Later proven wrong:** its warnings said complete builds are "erase-safe" via xppen-tools (see Rev 3).

### Rev 3 — Reply 7 — patcher P3
- **Trigger:** user's flash log: `expected CRC 0x5497, device returned 0x7F73`.
- **Discovered:**
  - The tool uses CRC-16/XMODEM.
  - Code region (0x08003800–0x0800c188) = `0x723A` (the earlier truncated flash verified with that); full complete image = `0x5497`.
  - Device value `0x7F73` = your code + all-`0xFF` `.data`, reproduced byte for byte.
  - So xppen-tools `program` writes only the code region and ignores `.data` records.
- **Sent:** rewritten flash warnings in the patcher; README section "xppen-tools does not write .data".
- **Correction:** Claude's earlier advice to restore `.data` via a complete hex through xppen-tools was wrong; Claude apologized.

### Rev 4 — Reply 10 — recovery files
- **Trigger:** user uploaded `ic902-main.bin` (their device dump).
- **Discovered:**
  - App code identical to the reference (XMODEM `0x723A`); `.data` region 0x0800c188–0x0800c7cc all `0xFF`.
  - Dump's full-app XMODEM = `0x7F73`, i.e. exactly the failed-verify state.
  - A differing cluster at 0x08003400 (28-byte span, `01…` in reference vs `00…` in dump); Claude called it a 1-byte boot flag, which the output doesn't actually establish.
  - Cut `deco01v3-data.bin` (1604 B, sha `a68048a26d55…`); it matches the `.data` inside the stock-complete hex.
- **Sent:** `deco01v3-data.bin`, stock-complete hex, `deco01v3-main.bin`, with three write options (data only at 0x0800c188, complete app, full 64 KB).

### Rev 5 — Reply 18 — poke-flasher (built in Reply 17, delivered after the user pointed out it was missing)
- **Discovered (Reply 17):**
  - The firmware's own FMC code: unlock keys written to FMC+4, erase = PER (bit 1) + START (bit 6) with address at +0x14, program = PG bit then halfword stores, busy flag in FMC_STAT bit 0.
  - No GPIO set/reset writes were found, so the LED pin couldn't be identified; Claude guessed GPIOB pin 2.
  - Emulation with an FMC model: 802 halfword programs reproduce `.data` exactly (sha `a68048a26d55…`).
- **Sent:** 1752-byte Intel HEX at 0x08003800–0x08003ed8.
- **Caveats:** only 1 LED toggle was observed in emulation; the ~4–5 Hz blink rate was an estimate and the 7 Hz target was not verified; the LED clock/register offsets are guesses.
- **Afterwards:** the user reported the tablet unbricked; which recovery route they used wasn't stated.

### Rev 6 — Reply 20 — patcher P5
- **Context:** Reply 19 (P4, not delivered) relabeled rates to 220/300/375/440/500, set the pacer period to `(900 − Hz)/0.4`, made Interpolation ON / Medium the wizard default, and fixed the dispatch so a bare run launches the wizard (cause: `if a.list or not(infile and outfile)` ran first).
- **Discovered (Reply 20):** `--speed` argparse choices were still the old list and rejected 500; fixed. Builds at 500 and 375 gave 9.0 ms and 11.7 ms latency estimates.
- **Sent:** patcher with those fixes.
- **Note:** the user's "compress this chat" instruction in this turn was not addressed in Claude's reply.

### Rev 7 — Reply 21 — patcher P6
- **Trigger:** "No, immediately stop. Why 500Hz? I want 1000."
- **Sent:** labels back to 220/400/600/800/1000, period back to `1_000_000/speed`, argparse choices and defaults restored (Direct / High). The bare-run wizard fix was kept.
- **Discovered:** the 300/375/440/500 relabel (Reply 19) was a misread of the user's measurements; the user's actual idea is to reuse the dead pressure-sweep frequencies for extra position samples. Not implemented.

### Rev 8 — Reply 23 — drop-test hexes
- **Sent:** `deco640_droptest_strongsweep.hex` (samples 20 / burst 32) and `deco640_droptest_midsweep.hex` (12 / 24), in-place builds.
- **Theory:** button/pressure presses shift the pen's resonance; a starved fast sweep loses lock. Slow laptop = USB saturation.
- **Flaw (admitted in Reply 25):** samples/burst were raised without raising `tick`/`poll`, so scan steps overran their timers; the user reports a softbrick from the drop test (which of the two wasn't stated).

### Rev 9 — Reply 25 — `deco640_fastscan_pressurefix.hex`
- **Discovered (Reply 24, analysis):**
  - Position/pressure path `fn_08007604` converts the resonance peak with a piecewise-linear lookup (tables at 0x0800c31c outputs and 0x0800c44c breakpoints, 2400 spacing, divide at 0x08007652).
  - Hypothesis: at 100% pressure a peak overshoot indexes past the table, producing a zero divisor and stopping input. Unconfirmed: DIV_0_TRP wasn't checked (SCB_CCR not found in the app).
- **Discovered (Reply 25):** table has 76 breakpoints (0…180000), so the index is clamped to ≤ 74; free zero runs at 0x080038cc (28 B), 0x080038ec (20 B), 0x08003904 (72 B), 0x0800bc77 (33 B).
- **Sent:** hook at 0x0800762c becomes `b.w 0x0800bc78`; routine `udiv; cmp r1,#74; bls; movs r1,#74; b.w 0x08007630`. Emulation: peak 240000 → 74, 180000 → 74, 12000 → 5, 0 → 0.
- **Caveats:** verified in isolation only; the base is the same fast-scan raw build that shows the reported dropouts.

### Rev 10 — Reply 27 — `summary.md` v1
- Overview, fixed/open split, external issues, and a note that nothing was tested on hardware.

### Rev 11 — Reply 28 — `summary.md` v2
- **Sent:** rewritten as a cold-start handoff (memory map, patch-site addresses, recovery pinout). Added the user's three open items (dead-zone sensor fusion, wrong/unstable rates, DFU failure detection + retry), "make patcher standalone", and removed all `-complete` mentions.
- **Self-RE issues added (inferences, unconfirmed on hardware):**
  - Cave code lies past 0x0800c188, which xppen-tools never programs.
  - No guard tying samples/burst to tick/poll.
  - Clamp hard-codes the 76-entry table bound and needs padding before the version string.

---

## Changed but never delivered

- **Reply 17:** poke-flasher built; delivered only after the user asked (Rev 5).
- **Reply 19:** patcher P4; superseded by P5 (Rev 6).
- **Reply 26 (patcher P7):** pressure clamp folded into the patcher.
  - Added `PRESSURE_SIG` and `apply_pressure_clamp()`, wired into `apply_all` and `build_recompile`.
  - Tested on a raw build and a `--speed 1000` build: log shows `clamp @ 0x800bc78 (hook 0x800762c)`; overshoot clamps; version descriptor intact.
  - **The `build_recompile` wiring was not tested.**
  - **This is the current file in outputs but was never delivered**; the last delivered patcher (Rev 7) predates the clamp.

## Inline-only replies and what each established

| Reply | Topic | Established |
|---|---|---|
| 2 | Step-4 export list | the four EXPORTS options and what each does |
| 4, 5 | Flowchart (v1, v2) | full path map; legacy `--preset/--rate` path has no flash step |
| 6 | Line-by-line strings | every description/warning, ordered by step |
| 8 | "Pico required?" | needs an external SWD/UART path; Pico or any adapter works |
| 9 | UART plan | BOOT0 high, PA9/PA10 + GND, 3.3 V, write full image |
| 11 | Self-write idea | FMC unlock keys at 0x0800832c / 0x08008330; erased `.data` implies pages aren't write-protected (inference) |
| 12 | Two-stage + PA15 | code and `.data` share the 1 KB page 0x0800c000–0x0800c400 (page size assumed); two pointers (0x0800766c → 0x0800c31c, 0x08007b2c → 0x0800c25c) hit that `.data` |
| 13 | "Rewrite it regardless" | 632-byte table isn't formula-generated; ~506 B compressed; 177 B free in code region, 72 B max contiguous *(per message)* |
| 14 | Mod vs recovery | the mod never touches `.data`; recovery is one-time; relocation idea |
| 15 | Compressed installer | code 35208 B + `.data` 1604 B; zlib 24798 / lzma 22316; scratch ≈ 21.4 KB; doesn't fit (RAM size assumed 8 KB) |
| 16 | "Program bytes one by one" | table + loop (not 401 literal instructions; Claude's first read was wrong) fits easily |
| 22 | Dead-slot idea | phase sequencer at 0x08005D80: `tbb` jump table, phase byte at 0x20000698, ~7 phases; truly simultaneous sampling is impossible, same-frame oversampling is the achievable version |
| 24 | 100% pressure | see Rev 9 |
| 29 | "Thank you!" | none |

---

## Corrections and weakly supported claims

1. Reply 3's warnings (complete = erase-safe via xppen-tools) and Reply 7's earlier advice were wrong; corrected in Rev 3.
2. Reply 10 called the 0x08003400 difference a 1-byte flag; the output shows a 28-byte cluster.
3. Reply 12's "bootloader reconfigures PA15 before reading it" is an inference from one GPIO-config caller (0x08000bc4); not traced.
4. Replies 9 and 11 mention GD ROM fallback pins (PA14/PA15) and a BOOT0 + USB-DFU route; neither was verified in this chat.
5. Reply 15's 8 KB SRAM figure was assumed in the script.
6. Rev 5's LED blink rate and pin are unverified.
7. Reply 19's rate relabel misread the user's numbers; reverted in Rev 7.
8. Rev 8 raised scan lengths without raising step timers (softbrick).
9. Rev 9's description of s4/b16 as "known-good" glossed over that it's the config with the reported dropouts.
10. Replies 17, 19 and 26 changed files without delivering them; the user called out 17 and 19.

## Current state of the outputs

- **Latest patcher:** `deco640_patcher.py` = P7 (clamp included). Only this file has the clamp wired in.
- **Latest summary:** `summary.md` v2.
- **README:** last updated in Rev 3; doesn't mention the clamp or the bare-run wizard.
- **Only hex with the clamp:** `deco640_fastscan_pressurefix.hex`.
- **Known-bad diagnostics:** the two `droptest` hexes (one softbricked the tablet).
- **Untested on hardware this round:** everything from Rev 6 onward (user can't test right now).
