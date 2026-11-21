# Claude outputs in this chat: revisions 31 to 32

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

Source: this chat only. The chat started from revision 30 (`deco640-rev14.zip`, which used the old counter "rev 14").
My packages were labelled "rev 15" and "rev 16"; under the project's numbering they are **revision 31** and
**revision 32**. Revision 32 was redelivered several times as follow-ups were added; they are listed under it.

Models: Claude Opus 4.8 for revisions 31 and 32; Claude Sonnet 5.5 and Claude Opus 5.5 for the follow-ups (noted per
item).

| Revision | My label | Package | Request |
|---|---|---|---|
| **31** | rev 15 | deco640-rev15.zip | calibration before settings, Bootloader Counter reliability, code quality, Dual-coil merge, filters must fit every frame (esp. 1000 Hz), stock-vs-chosen latency, smarter Jump Rejection, setting categories, locked version string |
| **32** | rev 16 | deco640-rev16.zip | reset-vector self-heal, compatibility check with aggressive warnings, PC flash-count store with provenance, one-time warnings, docs restructure; then the follow-ups below |

---

## Revision 31 (my "rev 15")

Delivered:
- Wizard reordered: Tablet Calibration (step 4) before Settings (step 5), with a Dual-coil recommendation from the
  calibrated hover signal-to-noise.
- Jump Rejection rewritten as a momentum model (stop-to-coast corridor) and moved beside Movement Smoothing.
- Two-coil Read + Dual-coil Search merged into one Dual-coil Mode.
- Filter frame budget corrected so every filter fits every preset; each build logs its frame budget.
- Cursor latency measured in the twin; stock vs chosen shown before building and in each build's measurements.
- Settings grouped into categories.
- Version string locked to `AnargonSS727_<build date>` (corrected mid-work from "Anargon727").
- 800/1000 Hz: a found pen is acquired into Hover Boost so a weak hover is held (Anargon reported these rates still
  needed contact).
- Bootloader Counter verified end to end in the twin and hardened: full-routine check before touching the boot page;
  bootloader-side self-heal from a stored stock copy; hook word committed last.

Found:
- **Movement Smoothing was billed a fictional 35 us** per frame. The stock dead-band runs whether on or off, so turning
  it on costs about 1 us; that charge alone was why it "did not fit" ULTRA.
- **The old constant-velocity Jump Rejection rejected sudden stops**; the corridor model keeps them.
- **The reset path** is vector -> 0x08000164 -> trampoline 0x08000150 -> literal at 0x0800015C -> boot code
  0x08000C38, inside the page the Bootloader Counter rewrites. Twin: corrupting the hook word still boots; corrupting
  the boot body faults. So rev 31's self-heal could not cover a power loss during that page's body rewrite.

## Revision 32 (my "rev 16")

Delivered:
- **Reset-vector self-heal** (opt-in, bundled with `--bootloader-counter`, as Anargon chose over making it default): a
  repair stub in page 0x08002800, reached by redirecting the literal at 0x0800015C, repairs page 0x08000C00 on every
  boot before the boot code runs. Install order: routine+stub -> page-0 redirect -> boot-page hook. Return-to-stock
  removes the redirect before erasing the stub's page.
- **Per-modification compatibility check** (probe mode in `Image.Expect`), `--compat`, and full-screen warning pages:
  SOME (comma list, y/n), ALL (type `I know what I am doing.`), and a critical-error page after a caught flash failure.
  Continuing is an explicit forced build.
- **Hidden PC flash-count store** (`%APPDATA%\deco640\state.ini`), counter text `X/100,000 [According to Tablet]` or
  `[According to PC]`, bumped per flash; **one-time-only warnings** (first: "no tablet dump found").
- Docs: DESIGN split per project, new FIRMWARE_DESIGN.md, per-revision design notes moved into the model file.

Found (all twin-verified):
- With the redirect installed, an erased boot body **boots through the stub**; the same erasure on stock faults at
  0x08000C38. Idempotent re-boot, write-protect safe-fail (nothing written, still boots), counting unchanged
  (2 -> other only) and return-to-stock (both pages restored) all pass.
- **The irreducible window moved, it did not disappear:** erasing page 0 in the installed image faults from reset.
  No flash page can be rewritten atomically.
- **Compatibility probe:** stock reads fully compatible (no false positives); 2 broken modifications are listed
  exactly; a foreign image reads ALL incompatible.
- **Bug caught during testing:** the settings "fit" check built non-forced and threw on an incompatible firmware,
  trapping the user in step 5 before the warning could show. The compatibility gate was moved in front of it.

### Follow-ups delivered as revision 32 updates
- **Brick-window estimate** (Claude Sonnet 5.5): about **85 ms typical, ~150 ms worst case** for the one page-0
  rewrite (erase ~60 ms + 256 words). Datasheet-typical figures, not measured.
- **Risk review** (Claude Opus 5.5): the realistic risk is replugging right after a flash and the install not yet
  having run on hardware, not the window itself. Added a **"DO NOT UNPLUG" page** after every Bootloader Counter
  flash; fixed the setting text (it said "one page, about 50 ms").
- **Stub-page exception** (Claude Opus 5.5, twin): the redirect and the hook are written once, but the stub's own page
  is rewritten if a later build's stub differs. Today every preset produces an identical stub (0 bytes changed when a
  RAPID build boots on a FAST-installed chip). Proposed fix, not built: keep the backup copy at one fixed address.
- **Discreet** described as highly recommended for infrequent flashers.
- **Docs and repositories** (Claude Opus 5.5): AGENTS.md made outward-facing (model list, rules for forks/PRs, read
  every model file); the record moved to CLAUDE.md with findings stated for any reader; FIRMWARE_DESIGN.md deepened;
  deco640 and gd32emu split into separate repositories with their own workflows and top-level build scripts, and
  EMULATOR.md describing the connection; chip named precisely (GD32F350C8T6); revisions 1 - 30 history added and the
  whole record renumbered to the project's revision numbers.

---

## Corrections made in this chat
| Claim | Corrected |
|---|---|
| The interrupted-install brick could not be closed at all | rev 32 closed it for the boot-code page, at the cost of one page-0 rewrite |
| The page rewrite takes "~15 ms" (and the setting said "about 50 ms") | ~85 ms typical per page (calculation) |
| Bootloader Counter pages are written "only on the first flash" | true for page 0 and the hook; the stub's page can be rewritten (stub-page exception) |
| Revision labels "rev 7" - "rev 16" | they were the old summary's counter; project revisions 23 - 32 |

## Still open after revision 32
- Nothing from revisions 24 - 32 has been confirmed on the tablet: hover at RAPID/ULTRA, Aggressive Interpolation,
  calibration, both counters, the reset-vector self-heal, return-to-stock.
- The stub-page fix (fixed backup address) is proposed, not built.
- The Windows `build.cmd`, the .NET builds and the GitHub workflows of the split repositories were not run here (no
  .NET); only the Java translation was compiled and tested.
