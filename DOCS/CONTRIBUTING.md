# Contributing

<img src="../misc/AIdisclosure-header-docs.png" width="100%" alt="Note: This project is entirely VIBECODED, without any code quality checks performed by a human. You may find the project hard to work with.">

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

The tablet tool and gd32emu are separate repositories. Keep board-specific behavior here and chip or native platform behavior in the sibling `../gd32emu/` repository. Start with the [architecture](architecture.md) and the relevant source.

## Code map

| Task | Source |
|---|---|
| Menu, CLI, calibration workflow and update decisions | `atf_cli.cs` |
| Start folders and patcher packages (C# source only) | `atf_packages.cs` |
| Full captures, backup retention and external command arguments | `atf_backup.cs` |
| Presets, timing budget, settings and build assembly | `atf_build.cs` |
| Strict Intel HEX input | `atf_hex.cs` |
| Firmware layout and generated signatures | `atf_layout.cs`, `atf_symbols.cs` |
| Thumb assembler and guarded byte patches | `atf_patcher.cs` |
| Generated scheduler, telemetry and bootloader maintenance | `atf_sched.cs` |
| Tablet/pen simulation and measurements | `atf_twin.cs` |
| Recovery analysis and stock checksums (no firmware bytes) | `atf_repair.cs`, `atf_stockref.cs` |
| CPU, memory, peripherals and native host helpers | `../gd32emu/gd32emu.cs` |
| Standalone emulator command line | `../gd32emu/gd32emu_cli.cs` |

Use `PascalCase` for classes and methods and `camelCase` for fields and locals. Follow existing uppercase constants and register notation where applicable. New host logic should use descriptive names and explicit units for time, bytes, reports and ADC sums.

## Implementation rules

1. Resolve firmware locations through `Fw.A` or `Fw.Ptr` and check the expected original bytes before patching. Regenerate `atf_symbols.cs` with `tools/mksig.py` when adding a location. Do not guess a missing address.
2. Preserve timing/profile checks and the digital-twin gate. Failed or missing required measurements must fail.
3. Keep bootloader maintenance opt-in. Measurement firmware must not inherit maintenance settings from the final build.
4. Keep the shared C# 5 subset: no generics, LINQ, `out`/`ref`, unsigned types or newer language syntax outside the native platform layer. Add no dependencies: the project builds with Windows' own compiler only.
5. Keep console output ASCII. Describe the action, required placement or failure directly.
6. Use fake helpers for automated host tests. Automated checks must not connect to or flash a physical tablet.
7. State which results are simulated, native executable tests or physical hardware observations.

The deployed flash-counter format is a compatibility contract. Preserve page `0x08002C00`, its `AnSC` marker, base/complement and record layout; the routine at `0x08002800`; and USB string 173 using `ADTC1:<B|F|N>:<6 digits>` at RAM `0x20001F80`. Always show the count against the chip's rated minimum, `N/100,000` (AGENTS.md rule 13).

## Build and test

Run `build.cmd` on Windows or `bash build.sh` on Linux/macOS from the repository root. The SDK path follows [global.json](../global.json); publishing uses .NET 10.0.403 with patch roll-forward. Python test scripts need Python 3.12 or newer and use only the standard library. From this repository root:

```text
python -I tests/test-infrastructure.py
python -I tests/run-regressions.py
python -I tests/run-tests.py ./AnargonsTabletTools --smoke
```

Use `./AnargonsTabletTools.exe` for the executable on Windows. The emulator component's build scripts compile and run `../gd32emu/tests/EmulatorTests.cs`; listing devices is only a smoke check.

**How the runners use the computer.** `run-regressions.py` compiles the three suites (core, host, calibration), asks each for its groups, and starts every group as its own process at once (about 48 with a stock dump), each in its own folder under the work directory; only low free memory holds a start back. `run-tests.py` starts the February and December checks at the same time. Two runs side by side (one per stock build, say, or several agents at once) never share a file. By default everything these runners start, together with every other capped test run on the computer, uses at most **50 % of the total CPU** (`tests/cpucap.py`: a shared Windows job object with a hard rate cap; on Linux and macOS the runner pauses its processes while they are over their share). `--uncapped` runs as fast as possible; GitHub always passes it. Each run prints the CPU it used. The regression folders use short names (`core`, `host`, `cal`, then the group) because Windows limits paths to 260 characters.

```text
python -I tests/run-regressions.py --stock FEB.bin                    # capped, one stock build
python -I tests/run-regressions.py --stock FEB.bin --uncapped --only host/patcher-packages,core
python -I tests/run-tests.py ./AnargonsTabletTools --build dec        # only the December checks
```

For firmware coverage, `run-tests.py` needs both untouched stock dumps. The simplest way is a git repository that holds them: set `ADT_TEST_DUMPS_REPO` (in `.env`, `.env.example` names the resources repository) and, for a private repository, `ADT_TEST_DUMP_TOKEN`. The runner clones it into `tests/work/dumps-repo/` (and fetches it again on later runs) and reads its `dumps/` folder: one folder per firmware version, named as the firmware names itself, each holding that version's untouched `ic902-main.bin` (`dumps/2025-02-14_release001/ic902-main.bin`, `dumps/2025-12-23_release001/ic902-main.bin`). February and December get every check; any other version folder gets the digital-twin checks (compatibility and all five presets), so a new firmware version is tested by adding its folder. Recovery repairs only the versions it has fingerprints for; for any other it must refuse, and the run reports a skip. A dump whose own version differs from its folder name fails. A repository without `dumps/` is searched for the two known dumps by firmware CRC instead. Nothing in the clone is run, and the token is sent as a header, never stored in the clone. `ADT_TEST_STOCK_DUMP` and `ADT_TEST_STOCK_DUMP_DEC` (a path or an HTTPS link) win for their build. Then, from this repository root:

```text
python -I tests/run-tests.py ./AnargonsTabletTools --require-all-firmware
```

`--require-firmware` requires February while reporting a missing December fixture explicitly. It is useful local coverage but does not qualify both registered builds. The tests check the recorded stock CRC. Keep each fixture's source and complete SHA-256; changing a firmware date string does not produce another valid stock fixture.

The native release package helper documents its inputs with `python -I tests/package.py --help`.

## Review and release

Make focused changes with a reproducer for behavioral defects. Run the affected regression harness and full firmware checks when generated code or its gate changes. Pull requests should explain the resulting behavior, commands run, firmware/preset coverage and remaining limits. Follow [release instructions](releasing.md) for source-bound packaging and [verification](verification.md) for the physical checks still required.
