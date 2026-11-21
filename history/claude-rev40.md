# R40 - Claude revision record

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

Contributor: Claude (Anthropic). The exact model version is not recorded in this repository. Date: 2026-11-08.
Input: the R39-rc source ZIP. Read `history/claude-rev39.md` and `history/claude-rev38.md` first; this record covers
only what R40 changed.

## Reported

Anargon ran calibration on Windows. After the measurement update, the readiness check read no reports at all
("no reports at all from 1 open collection(s)"), printed "Calibration firmware is not sending readable telemetry. ID 2
uses an alternate tablet format ... reflash the calibration build and try again", and then only offered "Press Enter to
continue with the preset defaults". There was no way to try again or reflash from there, and the message blamed ID 2
although nothing had arrived. He asked how to restart from that point and said it should restart by itself.

## Changes

- **`Cli.ReadyCheck`** (atf_cli.cs) replaces the one-shot check in `Calibrate`. A failed check reopens the tablet and
  checks again by itself `READY_AUTO` = 3 times (1 s apart), then asks: Enter = check again, `f` = flash the same checked
  calibration build again, `d` = skip calibration (the final build uses the preset defaults). Other answers ask again;
  it never falls through to the defaults without `d`. A tablet that cannot be opened gets the same choice at once (the
  open already retried for 20 s), without the "rest the tip" prompt. The setup help under that message keeps its own
  line breaks (the Linux udev rule was run into one wrapped paragraph before).
- **`f`** goes through `BeforeFlash` (this run's backup is reused, no second backup) and `FlashSteps` with
  `measurementUpdate` set, exactly like the first calibration update, then runs the readiness check again. It costs one
  more recorded update; the option text says so.
- **`Cli.ReadyProblem`** names what was seen, using the new `CalSource.Why()` (`P.HidWhy()` for the HID reader): no
  reports at all (close OpenTabletDriver / the XP-Pen driver), other report IDs or ordinary pen reports (the
  calibration firmware may not be running: `f`), a reader failure, too much pressure, or too few in-range readings.
- New calibration checks with a scripted `CalSource` (no tablet): readings at once; automatic re-check succeeds without
  a keypress; Enter re-checks and never falls through; `d`; `f` and re-asking on other answers; Enter succeeding after
  the automatic re-checks; failed open asks at once with the reason; no-reports and other-ID messages.
- Revision label R40 (`Cli.RELEASE_VERSION`, both VERSION files), README, CHANGELOG, DOCS/calibration.md (Preflight),
  AGENTS.md rows, CLAUDE.md, model provenance. gd32emu: no source change.

## Not changed

The cause of "no reports at all" on Anargon's PC is not established. It may be the collections not being listed yet
right after the update (the automatic reopen covers that), another driver holding the tablet, or Windows not passing
the measurement firmware's ID 7 reports to this program (CLAUDE.md open item 11). With the calibration firmware still
installed, `--hid-check` shows which report IDs arrive without flashing anything.

## Verification (Linux, no tablet, no Windows)

| Check | Result |
|---|---|
| Mono `mcs -langversion:5` build, `twin --selftest` | ok |
| `tools/cs2java.py` translation, `javac`, Java `twin --selftest` | ok |
| `tests/run-regressions.py --stock` February fixture, .NET 8 (scratch copy retargeted from net10.0) | core 130, host 96, calibration 97 passed (88 before, plus 9 readiness checks) |
| `tests/run-tests.py --require-all-firmware`, February and December fixtures | 54 passed, 0 failed, 0 skipped |
| gd32emu `tests/EmulatorTests` | 51 passed |
| Host suite without fixtures (Mono) | 85 passed |
| Mutations: no automatic re-check; `d` forced on any answer; old ID 2 message for no reports | each fails its readiness check |
| Real program in a Linux pseudo-terminal (no tablet, so the open fails), fake helper | Step 1 `b`, calibration `y`, typed UPDATE; Enter re-checked; `f` reused this run's backup and ran `dfu` + `update` again; `d` printed "Calibration skipped" and reached Step 5. Helper ran `dfu`, `dump`, `exit-dfu`, then `dfu` + `update` twice |

The automatic re-checks with an open tablet that sends nothing (Anargon's case) were exercised only with the scripted
source, not with a real HID device.

Still open: why no reports arrived on Anargon's PC (hardware), one Windows check of the typed UPDATE confirmation, and
physical tablet behaviour.
