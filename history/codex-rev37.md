# R37 - Codex revision record

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

Contributor: Codex (GPT 6 Astra Ultra [Max thinking with Multiagentic Capability]), OpenAI. Date: 2026-11-04. R37 is a rolling project revision, not a new semantic-version series.

## Read this before the next revision

The approved source layout contains two separate repositories, `anargons-deco640-tools/` and `gd32emu/`, side by side, plus the handoff `README.md`. The tablet source files and its build scripts belong directly in `anargons-deco640-tools/`; there is no `app/` wrapper. Emulator source belongs only in the separate gd32emu repository. Preserve each repository's AGENTS.md rules and build/CI entrypoints.

The tablet project's root `history/` is the durable revision handoff. Read the root CLAUDE.md and CODEX.md files and the latest history entries before contributing. Add a complete revision record when delivering changes; future models must be able to resume without persistent chat context. General guides live under each project's DOCS folder. Model entrypoints and history remain at the established root locations.

## Model attribution

The user confirmed R36 as Codex (GPT 6 Astra Ultra [Max thinking with Multiagentic Capability]). The R37 coordinator initially claimed the exact model and effort were unavailable; that was incorrect. Local session `turn_context` records identify the coordinator and all original R37 delegates as `gpt-6-astra` with `effort=ultra`. Delegate responsibilities covered console/process investigation, architecture, documentation, process implementation, naming/calibration implementation, and independent review. Later structure-correction delegates inherited the same model and effort without overrides. [Model provenance](../DOCS/model-provenance.md) records the evidence and the limits of the older R36 delegate manifest.

## Requested defects and changes

The user reported repeated backup text overwriting menu choices and Enter becoming unusable; xppen-tools surviving forced Ctrl+C exit; unverified tablet-to-PC HID communication; overly long firmware names; calibration language wrongly suggesting dual/multi-coil tuning; unwanted prebuilt executables and site claims; and missing exact model provenance. R36B's behavioral improvements were approved, but its approval did not authorize changing the established two-repository layout.

## Implementation

- `atf_cli.cs`: Screen.Ask writes warnings and then the prompt in forward order. It no longer rewinds the cursor using a row count that ignores terminal wrapping and scrolling. Live calibration output is also forward-only. The exact repeated backup literal in the user's screenshot was absent from the inspected source; do not claim an exact reproduction of that old binary's output.
- `../gd32emu/gd32emu.cs`: both subprocess runners register owned children. Windows uses a kill-on-close Job Object assigned before children launch, so child/grandchild ownership has no post-launch assignment gap. Ctrl+C/exit handlers reject subsequent launches and stop owned processes. Quiet background helpers receive closed stdin; interactive flashing keeps the helper's confirmation input. Windows forced termination is covered by OS job cleanup. Managed cleanup cannot provide the same guarantee for Unix SIGKILL.
- Native HID remains built from Windows system APIs, with no WinUSB or third-party HID library. New `--hid-check` reads for five seconds and counts all received report IDs; ID 2 can prove transport without being mistaken for measurement telemetry. Reader generation guards and bounded close remain intact.
- New `--pen-resonance` reads an already-installed measurement image for five seconds. Telemetry page 2 carries the firmware's tracked resonance in 16 Hz increments. Decode accepts fresh in-range light-contact context and reports median/range/sample count. This is a tracked resonance estimate, not an independent resonance-peak sweep, and makes no coil recommendation.
- Existing `--calibrate` is labelled Tablet signal thresholds. Existing startup frequency tuning remains explicitly experimental and uses a positional-jitter objective. It is not peak-resonance calibration. CLI aliases/profile formats remain compatible.
- `--calibration-pass --flash` now explicitly performs the checked measurement installation, with the existing twin gate, fresh backup and measurement-state tracking. A successful deliberate install prints the restoration warning but returns success. Dry-run makes no tablet commands. Restore a normal checked preset afterwards.
- Auto filenames use the speed name, omit enabled defaults and use short tags for changed options. Enabling Volatile Changes prefixes `[VOLATILE]`; explicit user filenames receive that prefix too. Measurement images remain distinctly named.
- The deployed counter pages, records, string descriptor 173 and compatibility protocol were not changed. No new firmware patch objective was introduced for resonance.

## Structural mistake and correction

The first R37 delivery used the generated consolidated release staging tree as its baseline because its C# sources matched a later R36 artifact. That conflated behavioral recency with approval of repository structure. It retained an `app/` wrapper and embedded gd32emu in the tablet tree, then rewrote AGENTS.md to endorse the consolidation. The user rejected that structure and supplied a screenshot of the correct sibling repositories.

The corrected delivery retains the reviewed R37 behavioral source but restores the original sibling projects, their root source/build files, separate contributor rules and workflows. Historical revision notes are restored to root history/ rather than buried under DOCS/models. No original revision records are omitted. The generic-model claim is corrected to Codex (GPT 6 Astra Ultra [Max thinking with Multiagentic Capability]) using actual session metadata. The archive is named `anargons-deco640-tools-R37-rc.zip`; its top-level entries are exactly the two project folders and README.md. Source handoffs contain no prebuilt executables or generated firmware.

## Verification performed

The R37 behavioral source passed the Windows system-compiler builds, 130 firmware-backed core checks, 67 host checks with a fake xppen-tools, 88 calibration checks, 51 emulator/reader checks, and 37 integration checks including all five February preset gates and recovery. A complete December fixture was unavailable and was explicitly skipped. Java translation, compilation and numeric self-tests passed.

Four native Windows process-lifecycle cases passed: normal exit, forced parent termination, actual generated Ctrl+C, and refusal to launch a new helper after cancellation; child and grandchild processes were checked. A real hidden Windows console at 48 columns accepted 12 empty Enter responses across wrapped warnings and scrolling. These checks do not establish the exact screenshot reproduction or physical USB behavior.

Those behavioral results were first recorded against the intermediate staging layout. The corrected handoff requires fresh extracted-source builds, path/packaging checks, source comparisons and checksum/inventory verification; its accompanying delivery receipt records those correction checks. Earlier successful receipts must not be silently presented as a full test run of a different source manifest.

## Still open and next hardware checks

No real tablet was flashed, calibrated or used for native HID reads during this R37 work. Run `--hid-check` on the user's system first. Then test deliberate measurement installation and `--pen-resonance`, and restore normal firmware. Keep the pen unpressed when comparing resonance estimates. A true independent peak-response sweep is not implemented.

Physical hover at RAPID/ULTRA, real signal/noise/hover-height behavior, native Linux/macOS access, and real flash timing remain hardware work. Preserve the earlier CLAUDE/CODEX findings and explicit maintenance risks. A passing twin gate does not close those issues. The corrected archive must continue to keep source projects separate and keep this history usable by the next contributor.
