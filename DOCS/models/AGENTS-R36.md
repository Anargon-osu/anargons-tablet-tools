> Historical record preserved from the R36 audit checkout. Paths, packaging and repository descriptions below describe that earlier checkout; current instructions are in the root AGENTS.md and DOCS guides. See [model provenance](../model-provenance.md) for corrected R36 attribution.

# AGENTS.md - read this first (every AI model and every contributor)

<img src="../../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

This repository is **Anargon's Deco 640 Tools** (folder `anargons-deco640-tools`, executable `AnargonsDeco640Tools`; called `deco640` up to rev 33), a firmware builder - so far for the XPPen Deco 640 drawing tablet. It is maintained by
**Anargon** and has been developed with AI models. The chip emulator it depends on, **gd32emu**, is a separate
repository; see `EMULATOR.md` for how the two connect.

You are probably working in a fork or on a branch for a pull request, together with a person: **your partner** (the
contributor you are working for). This page tells you who has worked here before, the rules every change follows,
and where the accumulated knowledge is.

---

## 1. Before you change anything: read every model file

Each AI model that has worked on this project keeps its own notes file at the top of the repository, named after the
model family (`CLAUDE.md`, and for example `GEMINI.md`, `GPT.md` or `CODEX.md` if those models contribute). **Each
file holds that model's discoveries, what it verified and how, and what it left open.** The findings are not repeated
here on purpose, so no single file goes stale.

- **Read all of them**, not just the one with your own name. Other models have found things you will otherwise
  rediscover the hard way, or contradict without knowing.
- If two files disagree, the more recent revision wins. Say so in your PR description so your partner and the
  maintainers can settle it.

Current model files: **`CLAUDE.md`** (history in `history/claude-rev*.md`) and **`CODEX.md`** (rev 36 audit).
Human contributors can start with `CONTRIBUTING.md`; `AUDIT.md` records the current release assessment.

---

## 2. Models that have worked on this project

| Revisions | Model | Made by | Notes / history | What it did |
|---|---|---|---|---|
| 1 - 11 | Claude Sonnet 5.5 (max effort) and Claude Opus 4.8 | Anthropic | `history/claude-rev01-11.md` | the first Python patcher and wizard; found that xppen-tools left `.data` erased and **recovered the bricked tablet** (`.data` restore, self-writing restorer); pressure clamp; first summary |
| 12 - 23 | Claude Opus 4.8 (Max effort) | Anthropic | `history/claude-rev11-23.md` | Python digital twin (USB, then real-time scan); bInterval and AGC root causes; frame pacing; the five levels; hover boost; wiggle calibration; oddity detector (now Jump Rejection) |
| 24 - 30 | Claude Opus 4.8 (Max effort) | Anthropic | `history/claude-rev24-30.md` | conversion to C# (one Windows build, no installs) with the twin; integrating-receiver thresholds; Tablet Calibration; flash counter; Bootloader Counter; emulator split |
| 31 - 32 | Claude Opus 4.8 | Anthropic | `history/claude-rev31-32.md` | Jump Rejection momentum model, calibration before settings, compatibility check and warning pages, reset-vector self-heal, PC flash-count store |
| 33 | Claude Opus 5.5 | Anthropic | `history/claude-rev33.md` | any firmware build: every firmware location found by signature (patcher and twin), December build supported and verified, compatibility check covers every call and table, real firmware end |
| 34 | Claude Opus 5.5 | Anthropic | `history/claude-rev34.md` | renamed to `anargons-deco640-tools` / `AnargonsDeco640Tools`; LED back to stock (calibration-pass build blinks while measuring); backup before every flash into `dumps/` and the tablet's firmware build checked before flashing (open issue 8) |
| 35 | Claude Opus 5.5 | Anthropic | `history/claude-rev35.md` | anargons-basic-tablet-recovery (repairs a damaged tablet from its main.bin; Anomy's patcher and stock files), start-up menu, plural name |
| 36 candidate | Codex (GPT 6 Astra Ultra [Max thinking with Multiagentic Capability]) | OpenAI | `CODEX.md`, `history/codex-rev36.md` | calibration routing/profile and stock frequency defaults; startup full backups; dry-run/exit-status safety; strict inputs, emulator isolation and fail-closed checks; OS/test/release audit |
| 32 follow-ups | Claude Sonnet 5.5, Claude Opus 5.5 | Anthropic | `history/claude-rev31-32.md` | brick-window calculation, risk review, unplug warning, documentation and repository split |

All of these keep their findings in **`CLAUDE.md`** (one file per model family); the `history/` files are the full
per-revision record each Claude session wrote, including its own corrections.

**When you contribute:** add a row (revisions, your model name and maker, your notes file, what you did), create or
extend your own notes file, and add a `history/<model>-rev<from>-<to>.md` for the revisions you delivered.
**Revision numbers** count deliveries across the whole project (Anargon's numbering): continue from the last one in
this table. If you are unsure of your exact model version, say so instead of guessing. Human
contributors are welcome to add themselves too.

---

## 3. Rules for every change (Anargon's zero-trust model)

Follow these in your fork and in every pull request:

1. **Never patch blind.** Every change to the firmware first checks that the exact stock code is at that spot
   (`Image.Expect`). A mismatch stops the build. The interactive tool does not override byte checks or timing limits.
2. **Never flash unchecked.** Every build boots in the digital twin and passes the boot / USB / gain-control /
   pen-detection checks before it can be offered for flashing. Do not weaken or skip this gate.
3. **Do not touch the bootloader unless your partner explicitly asks.** Normal builds change only the firmware area
   (`0x08003800` and up). Bootloader writes exist only in the opt-in Bootloader Counter and in return-to-stock.
4. **Verify before claiming.** "Works" means shown in the twin or on a real tablet, and you say which. Anything not run
   on a real tablet is labelled that way, in your notes file and in your PR description.
5. **State the risk plainly.** If something cannot be made fully safe, say so and give the size of what remains. Do
   not hide it from your partner.
6. **Keep the shared C# subset** (C# 5: no generics, LINQ, `out`/`ref` or unsigned types). It must compile with
   Windows' built-in `csc.exe` and translate to Java with `tools/cs2java.py`, which is how the code is tested without
   a C# compiler.
7. **Keep console output ASCII**, worded for a casual user.
8. **Record what you learn in your own model file**: each finding, who found it, and how it was verified (code
   analysis, twin, or hardware). Mention the new findings in your PR description.
9. **Changes to the emulator belong in the gd32emu repository**, as a PR there. Do not copy it into this repository.
10. **Never hard-code a firmware address.** Write it as its February address through the layout - `Fw.A(0x0800....)`,
   or `Fw.Ptr(...)` for a word stored there - and regenerate `atf_symbols.cs` with `tools/mksig.py` (see
   `DESIGN.md`). The layout finds the location in whichever firmware build is loaded and refuses when it cannot;
   an address without a signature stops the build. If you verify a new XP-Pen build, add it to `Layout.KNOWN` and as a
   reference dump to `mksig.py`.
11. **NEVER change where or how the flash count is kept and read - not in any revision, fork or PR.** Tablets in the
   field carry it, and every older and newer copy of this tool reads it:
   - the counter page **`0x08002C00`** in the bootloader area: `+0 "AnSC"` (0x43536E41), `+4 base`, `+8 ~base`, then
     8-byte records `{id, ~id}` from `+16` (count = base + valid records); bootloader records use id `0xB007B007`;
   - the Bootloader Counter's routine page **`0x08002800`** and its hook at `set_boot_state` (`0x08000F40`);
   - the USB readout: **string descriptor index 173**, text **`ADTC1:<B|F|N>:<6 digits>`** (B Bootloader Counter
     installed, F firmware counter, N no counter), built in RAM at **`0x20001F80`** (`Patches.CounterString`).
   If something new is needed, add it next to these (a new string index, a new record id) and keep the old ones
   working; never move, reformat or reuse them. The PC side prefers the tablet (Bootloader, then Firmware) over the
   local PC copy, which counts only the flashes that PC performed.

---

## 4. Pull requests

A good PR description for this project says:
- what changed, and which presets or settings it affects;
- how it was verified: twin checks run (all five presets gate?), hardware tested or not;
- any new risk and its size;
- which model files you updated.

---

## 5. Where to start

| You want to... | Read |
|---|---|
| understand how the tablet firmware works | `FIRMWARE_DESIGN.md` |
| know how the February and December stock builds differ, with every offset, and how the tool handles any build | `FIRMWARE_VERSIONS.md` |
| change the builder or the digital twin | `DESIGN.md`, then the code |
| understand or change how this repository uses the emulator | `EMULATOR.md` |
| know what has been found, done, left open, and what to test on hardware | every model file (`CLAUDE.md`, ...) |
| build and run | `README.md` |
