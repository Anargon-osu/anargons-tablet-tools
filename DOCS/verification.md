# Verification

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

This candidate has automated checks for generated firmware, host workflow decisions and packaged executables. Physical tablet qualification remains incomplete. Build success, a passing emulator or a cross-published binary does not establish native USB behavior or physical hover performance on another OS.

## Evidence included with a release

The delivery verification receipt records the source/archive hashes and the checks performed. The corrected source ZIP has exactly two project directories and README.md at the top level; any included evidence belongs inside the project documentation. Native release packages carry their own checksums and smoke checks. Do not use an intermediate consolidated-package manifest as evidence for a different source layout.

Required negative-path tests can expect a nonzero exit status. A check passes only when its observed status matches the expected one. Missing required measurements and required fixture files fail; explicitly optional unavailable checks are recorded separately.

## Recorded baseline

Before consolidation into this checkout, the same behavioral fixes passed the following local Windows checks. The generated record shipped with each release identifies the checks run on that release's exact source snapshot.

| Check | Recorded result |
|---|---|
| Core regressions with February firmware | 95 passed |
| Host workflows using a fake xppen-tools | 52 passed |
| Calibration regressions with February firmware | 63 passed |
| Emulator and simulated reader sessions | 31 passed |
| Executable smoke checks | 20 passed |
| Python infrastructure / package helper tests | 4 / 10 passed |
| February integration, including all five presets and recovery | 37 passed, 0 failed, 1 explicit December skip |
| Workflow syntax | Passed actionlint; hosted execution not run locally |
| Self-contained publish | Both tools for all five OS/architecture targets |

A simulated February FAST upgrade from the original revision 35 Bootloader Counter build advanced the count from 2 to 3, made zero candidate bootloader writes and returned `ADTC1:B:000003`. This is scoped to that firmware, preset and setting combination, not a physical interruption test.

The February full-dump fixture SHA-256 was `26e6bfaa2f9e4ccc41e95b34661f3eb66e0e814c4afd36483b6d0dc0bee7329d`. Its application CRC is `B65DB693`, ending at `0x0800C7CC`. The registered December reference is CRC `4525CA51`, ending at `0x0800C7D8`; a matching complete December fixture was unavailable for that run. Private dumps are not included in source or release archives.

## R45-rc3 record

Local Windows run, 2026-11-12, on the final R45-rc3 sources (the runners of history Part 15):

| Fixture | Core regressions | Host workflows (fake xppen-tools) | Calibration regressions |
|---|---:|---:|---:|
| February stock dump | 215 passed | 112 passed | 200 passed |
| December stock dump | 215 passed | 112 passed | 200 passed |
| No dump | 117 passed | 101 passed | 79 passed |

February and December ran side by side, capped together at 50 % of the CPU (75.9 s and 76.3 s); February
alone and uncapped took 26.5 s. `tests/run-tests.py --require-all-firmware` (both builds at once, capped): 54 passed, 0 failed, 0 skipped, in 198 s (each build's regressions: 527 checks).
`tests/test-infrastructure.py`: 7 passed. Nothing of this ran on GitHub from here.

Rerun 2026-11-13 after the package-options follow-up (history Part 15, `Build.AddSetting` / `HideSetting` /
`Option`): February, December and no dump give the same counts as the table (527, 527 and 297 checks, 36-37 s and
18 s, capped). The options were checked by hand: a test package through the selector and the wizard (twin check
passed) and an 18-check harness linked to the tool.

On the tablet (rc3): the final ULTRA + ALPI build, flashed 2026-11-12 (CRC 0x129B, bootloader counter 86); no low
hover recording or lift yet.

## R45-rc2 record

Local Windows run, 2026-11-12, `tests/run-regressions.py` on the R45-rc2 sources:

| Fixture | Core regressions | Host workflows (fake xppen-tools) | Calibration regressions |
|---|---:|---:|---:|
| February stock dump | 215 passed | 112 passed | 200 passed |
| December stock dump | 215 passed | 112 passed | 200 passed |
| No dump | 117 passed | 101 passed | 79 passed |

New calibration checks: the three guided steps; the training firmware at the measurement levels passes its check with
a detection signal and a pen frequency in every sample (ULTRA, RAPID); a simulated training gives pick-up levels
within the preset's that a build accepts; the training file keeps them and refuses levels out of order; ALPI shows
first in Noise-Related Patches. The background measurement has not run on the tablet yet.

On the tablet (rc2): Frequency Hold's 8-sweep rule, a silent A/B against 3, 5 and 12 sweeps with the pen resting
between two drive steps, then the final ULTRA + ALPI build rebuilt with it (see `history/claude-rev45.md`, Part 12).

On the tablet (rc2): the bootloader's SysTick switched off, a silent A/B with the interrupt on and off in 4 s blocks
(240 s at a coil midpoint), then the final ULTRA + ALPI build rebuilt with it and 160 s of its reports (Part 13). The
twin cannot show this one: it starts at the firmware, without the bootloader's SysTick.

## R45-rc1 record

Local Windows run, 2026-11-12, `tests/run-regressions.py` on the R45-rc1 sources:

| Fixture | Core regressions | Host workflows (fake xppen-tools) | Calibration regressions |
|---|---:|---:|---:|
| February stock dump | 215 passed | 112 passed | 187 passed |
| December stock dump | 215 passed | 112 passed | 187 passed |
| No dump | 117 passed | 101 passed | 79 passed |

On Anargon's tablet in R45 (silent study builds, see `history/claude-rev45.md`): Frequency Hold and Coil Hold, the
LED pulse's effect on scan noise, an ALPI training and its Tracker, then the final ULTRA + ALPI build. Not yet on the
tablet: Coil Hold with the pen at a coil midpoint, and an ALPI training made with both holds on.

## Test coverage and limits

- Synthetic cases exercise framing, input bounds, instruction/flash errors, cancellation, stale sessions and expected failure statuses. Fake-helper cases verify backup retention, command arguments, dry-run behavior and update accounting without a physical device.
- Firmware cases execute actual stock code in the tablet board model. They check calibration report routing, boot/USB/gain control/pen detection, scan measurements, all presets, recovery and optional maintenance.
- Windows packaged executables were run. Linux/macOS targets were cross-published and checked for their expected binary headers; native execution on those systems was not performed in the local run.
- Analog signal and noise are relative model inputs. Flash erase/program latency is not physically calibrated.
- xppen-tools is external. Supplying and testing its platform build and native dependencies remains necessary for backup and flashing support.

Required full-release firmware checks use `--require-all-firmware` and need both stock fixtures. An explicit February-only candidate run uses `--require-firmware`; it must retain the December gap. PR smoke checks do not use private fixture credentials. The project workflows define these scopes; a local syntax check is not a hosted CI run.

## Hardware verification

Use one tablet and its own original full stock dump. Record the OS, architecture, executable hash, driver state, firmware build, pen and preset. Keep bootloader changes off for the initial calibration checks.

1. Confirm interactive startup captures exactly 65536 bytes, saves the latest dump and a full archived copy, and returns the tablet from update mode. Confirm a failed capture retains the previous valid backup and blocks flashing.
2. Start with FAST defaults. Check tip contact, pressure range, both barrel buttons and hover before faster presets.
3. Run calibration. Verify genuine signal/position/pressure at preflight and repeat all three poses. Preserve the live results and errors, including alternate report IDs or missing signal.
4. Flash the normal result. Compare pickup/drop height, still hover and slow lift with defaults at the same position. Repeat independently for QUICK, RAPID and ULTRA; do not reuse another preset's profile.
5. Repeat native access and calibration on each advertised OS/architecture. Test driver closure, disconnect/reconnect and two consecutive calibration sessions.
6. Validate recovery and optional bootloader maintenance separately, with preserved full backups and an established hardware recovery route. Keep uninterrupted power for the required post-flash wait.

A failed hardware item remains a failure for the corresponding advertised behavior. Bootloader maintenance retains a power-loss window during reset-page rewriting; the emulator does not make that write atomic.
