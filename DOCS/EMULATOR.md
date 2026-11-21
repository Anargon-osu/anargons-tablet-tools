# Emulator integration

<img src="../misc/disclosure-GD32dependency-docs.png" width="100%" alt="Note: This project depends on the GD32EMU project, which allows for the digital testing of your tablet through emulation, including both software and hardware tests. Because it is a dependency, builds in this repository bundle this project.">

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

The app compiles the chip core from `../gd32emu/gd32emu.cs` into its own executable. The tablet board and pen model are supplied by `atf_twin.cs`. `gd32emu_cli.cs` has a separate entry point and is not compiled into the tablet tool. The chip core remains in the separate gd32emu repository. Record and test the emulator revision used for a tablet-tool release.

## Component boundary

| Source | Responsibility |
|---|---|
| `atf_twin.cs` | Tablet coils, pen/receiver model, scenarios, checks and measurements |
| Other `atf_*.cs` | Firmware building, calibration, recovery and update workflow |
| `../gd32emu/gd32emu.cs` | CPU, memory, peripherals, reset, flash and native host utilities |
| `../gd32emu/gd32emu_cli.cs` | Standalone emulator CLI |

No separate emulator DLL is needed. The shared core builds with the Windows C# 5 compiler and .NET 10 SDK.

## Shared interface

| Interface | Tablet-tool use |
|---|---|
| `Machine`, `RunMs`, `Call`, `Peek`, `LE`, `vtor`, `bootWp` | Isolated boots/scenarios, bootloader calls, bounds and write-protection checks |
| `Cpu`, `Step`, `Fault` | Instruction execution, reset paths and explicit fault reporting |
| `Board` hooks | ADC readings, coil switching, timer/GPIO events and receiver behavior |
| USB device core and scripted host | Enumeration, descriptors, report intervals and pen packets |
| `P`, `F`, `Job`, `Stop` | Platform access, files/processes, background work, errors and formatting |
| Simulated 72 MHz cycle clock | Reproducible model timing; not hardware calibration |

Each `Machine` copies its input flash. Scenarios that retain writes must read `machine.flash`, not the original array. Current device profiles implement 64 KB flash and 8 KB SRAM; larger variants are refused. This is not a complete Cortex-M4 or GD32F3x0 implementation.

## Source discovery

The normal build uses the separate sibling repository `../gd32emu`. Component scripts and the app project also support an explicit `GD32EMU_DIR` or `-p:Gd32EmuDir=...` development override. The tablet workflows prepare the separate emulator source using the configured repository/ref; local builds can use the sibling checkout.

When changing shared interfaces, build both executables and rerun their regression suites. Changes to CPU, peripherals or native host utilities belong in `gd32emu/`; tablet behavior belongs in this tablet repository.

## Test entry points

- `AnargonsTabletTools twin --selftest` checks deterministic numeric primitives.
- The tablet-specific `--bl-boot`, `--bl-state`, `--bl-erase`, `--persist` and `--boot-wp` options belong to `AnargonsTabletTools twin IMAGE ...`, not standalone gd32emu. Scenario failures return nonzero.
- From the repository root, `python -I tests/run-regressions.py [--stock PATH] [--uncapped] [--only SUITE/GROUP,...]` compiles core, fake-helper host and calibration harnesses and runs every test group as its own process at once (capped at 50 % of the CPU together with other capped runs unless `--uncapped`). Cases include branch bounds, flash isolation, strict HEX, cleanup failures and telemetry report routing.
- `python -I tests/run-tests.py EXECUTABLE --smoke` checks the executable without stock firmware. `--require-all-firmware` requires both known fixtures, all presets and recovery checks; `--build feb|dec|all` picks the builds (all: both at the same time); the fixtures come from `ADT_TEST_DUMPS_REPO` (cloned into `tests/work/dumps-repo/`, `dumps/<firmware version>/ic902-main.bin`; other versions there get the digital-twin checks) or `ADT_TEST_STOCK_DUMP` / `_DEC`.
- The emulator build scripts execute `../gd32emu/tests/EmulatorTests.cs`. Listing devices is only a smoke check; standalone gd32emu has no `--selftest` option.
- The project workflows and `tests/package.py` coordinate native checks and release packaging. See the [contributor guide](CONTRIBUTING.md) and [release instructions](releasing.md).

## Native host sessions

`P.RunInResult` preserves process status and drains stdout/stderr concurrently. Quiet background captures receive closed stdin; interactive updates retain stdin for helper confirmation. Absolute argument quoting and atomic latest backup publication do not remove the external xppen-tools dependency.

`HidOpenPen` closes the previous session first. Session generations prevent late readers/callbacks from writing into a newer packet buffer. Framing rejects short or unrelated reports. `HidClose` cancels reads and waits within one shared one-second budget; a new session is refused while an old reader remains alive. Windows uses pending-read cancellation, Linux nonblocking descriptors and macOS session-owned callback/run-loop resources. Reader threads own cleanup, and calibration closes its source in `finally`.

Synthetic lifecycle tests do not qualify permissions, driver coexistence or disconnect/reconnect on a physical system. Analog levels/noise and flash erase/program timing remain model limits. See [verification](verification.md) for actual execution evidence and remaining platform/hardware work.
