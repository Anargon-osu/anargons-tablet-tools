# R39 - Claude revision record

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

Contributor: Claude (Anthropic). The exact model version is not recorded in this repository. Date: 2026-11-08.
Input: the R38-rc source ZIP. Read `history/claude-rev38.md` first; this record covers only what R39 changed.

## Requested

Anargon asked for an option to use the background backup dump as the firmware file, a re-check of the integration
against the xppen-tools build he supplied, and a repackage as R39. He also supplied verified February and December
stock dumps, so the firmware-backed suites could run for the first time with both fixtures.

## Changes

- **Step 1 `b` option** (`Cli.Wizard`, `Cli.RunBackup`, atf_cli.cs). When this run has a startup backup (running or
  finished), Step 1 shows `b = this run's tablet backup` with its path on its own line, and the prompt offers
  `or b for the tablet backup`. Choosing `b` waits for a backup still being read (`StartupBackup.Wait`) and then uses
  `Cli.sessionBackup`. With no backup in this run, `b` is refused with a reason; a failed backup is refused and never
  used. The chosen dump goes through the same input checks as any file (a build of this tool is rejected).
- **Test fix** (tests/host-regressions.cs). "the tool's log output and its prompt stay on separate lines" required the
  `[INFO]` line to follow a newline, so it failed when stderr was echoed before stdout under load. It now accepts either
  order (the property is that the lines stay separate) and prints the captured output if it fails.
- New host checks: `b` offered, waits and builds from the backup; refused without a backup; refused after a failed one.
- Revision label R39 (`Cli.RELEASE_VERSION`, both VERSION files), README, CHANGELOG, AGENTS.md rows, CLAUDE.md, model
  provenance. gd32emu: no source change.

## xppen-tools re-check

R38's commands and console handling were compared with the supplied xppen-tools build; nothing needed to change.

## Not included

An emulated tablet dump over USB was started during this session and stopped before completion. None of that work is
in R39: the emulator is unchanged from R38, and R39 was built from the R38-rc ZIP contents, not from that working copy.

## Verification (Linux, no tablet, no Windows)

| Check | Result |
|---|---|
| Mono `mcs -langversion:5` build, `twin --selftest` | ok |
| `tools/cs2java.py` translation, `javac`, Java `twin --selftest` | ok |
| `tests/run-regressions.py --stock` February fixture, .NET 8 (scratch copy retargeted from net10.0) | core 130, host 96, calibration 88 passed |
| `tests/run-tests.py --require-all-firmware`, February and December fixtures | 54 passed, 0 failed, 0 skipped |
| gd32emu `tests/EmulatorTests` | 51 passed |
| Host suite without fixtures (Mono) | 85 passed; the fixed check also passed three concurrent runs on R38-rc plus the fix (81 each) |
| Mutation: `b` handling disabled | "choosing b waits for the running backup and builds from it" fails |
| Real program in a Linux pseudo-terminal, fake helper returning the February dump after 5 s | `b` at Step 1 waited for the dump, used it, reached Step 3; helper ran `dfu`, `dump`, `exit-dfu` once each |

The R38 record's counts were taken without fixtures; with the February fixture R38-rc plus the test fix gave core 130,
host 92, calibration 88.

Still open, as in R38: one Windows check of the typed UPDATE confirmation, and physical tablet behaviour.
