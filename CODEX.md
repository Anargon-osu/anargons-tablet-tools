# Codex findings: current R37 handoff

<img src="misc/AIdisclosure-most_docs.png" align="right" width="280" alt="Note: This document is mostly AI generated, with only minor corrections done by a human (stated to the AI).">

Contributor: Codex (GPT 6 Astra Ultra [Max thinking with Multiagentic Capability]). The coordinator and original R37 delegates were verified as `gpt-6-astra` / `ultra` in local session metadata. R36 is the same model and setting, confirmed by the user (2026-11-09). See [model provenance](DOCS/model-provenance.md).

This is a rolling release with two separate repositories. Read the complete [R37 revision record](history/codex-rev37.md) and original contributor rules in AGENTS.md before making changes. Root history/ in the tablet repository is the durable per-revision handoff; it must not be replaced by assumptions from chat or by the intermediate consolidated app/ packaging layout.

R37 changes console prompt rendering, owned helper lifetimes, read-only native HID/resonance diagnostics, naming and calibration wording. Physical tablet behavior remains unverified. Earlier findings and limits are preserved below.

---

> Historical record preserved from the R36 audit checkout. Paths, packaging and repository descriptions below describe that earlier checkout; current instructions are in the root AGENTS.md and DOCS guides. See [model provenance](DOCS/model-provenance.md) for corrected R36 attribution.

# codex findings

Contributor: Codex (GPT 6 Astra Ultra [Max thinking with Multiagentic Capability]), OpenAI. Delivery: rev 36 audit candidate,
2026-11-03. See `AUDIT.md` for the detailed assessment and final verification record, and
`history/codex-rev36.md` for the change record.

## findings established by source/disassembly

- Calibration telemetry only hooked ID 7 send paths, while firmware report mode 4 used 12-byte ID 2 pen reports and
  bypassed those hooks. Opening more HID collections did not address that routing defect. The measurement build now pins
  report mode 2; normal firmware keeps vendor report selection.
- Startup frequency calibration's jitter objective does not constrain signal or physical hover range. The stock
  frequency is therefore the default; measured threshold profiles exclude unmeasured frequency/dual-coil changes.
- Telemetry is only emitted during tracking. Absence of pen-away reports cannot establish the receiver noise floor
  or justify a signal-to-noise or second-coil recommendation.
- Host update success previously ignored process status, and dry run invoked real commands. These were corrected
  without changing the deployed counter format.
- Emulator gate and CLI success paths accepted skipped/failed conditions. Simulations shared writable flash, and
  short branch validation admitted displacements that the instruction could not encode.
- Bootloader cleanup must verify the live reset target before removing the stub, and the host must verify the
  restoration from a fresh backup before replacing the cleanup application.

## evidence and limits

Regressions distinguish synthetic packets, fake external tools, actual stock firmware running in the twin and native
executable smoke checks. `AUDIT.md` records what actually ran. No real tablet was flashed or calibrated in this audit;
there is no new hardware evidence for hover height, native HID APIs or flash timing.

These findings update older `CLAUDE.md` suggestions that broad collection opening alone addressed LOST (then explicitly
unconfirmed), that calibration
could measure untracked noise, or that skipped checks and a no-fixture CI pass establish release readiness. Earlier
notes remain preserved as history. The bootloader counter protocol is intentionally unchanged.

## documentation refresh

The workspace `summary.md` and `DESIGN.md` now lead with the current C# candidate and retain the older Python
record below. This project's `DESIGN.md` and `EMULATOR.md`, and gd32emu's `DESIGN.md`, describe the current startup
backup, calibration/restoration, verification gates and native-session limits. Startup captures live beside the
executable; pre-flash captures live under the selected output folder. Both retain complete archived dumps.
The download index records the fresh Windows binary and its separate rebuild evidence. The audited C# sources
remain unchanged by this documentation refresh.

## final quality pass

The second review found that a failed first measurement update could leave calibration firmware installed without
tracking it. State is now conservative before the update starts and clears only after a successful normal update.
The update call explicitly identifies measurement firmware; an output-path collision no longer controls that state.
Measurement builds disable bootloader maintenance in their copied settings and defer it to the normal build.

Current prompts describe mechanisms and verification limits instead of guaranteed hover, chip life or OS access.
Recovery commands use quoted full paths. The complete bundle preserves both source trees and runtime notices;
its final verification record binds command results and executable hashes to the frozen source manifest.
See `AUDIT.md` and `../../artifacts/verification/result.json` for the distinction between this pass and prior checks.
