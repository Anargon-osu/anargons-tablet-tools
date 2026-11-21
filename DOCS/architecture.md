# Architecture

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

The two separate repositories build two C# executables. `AnargonsTabletTools.csproj` compiles the tablet tool together with the chip core in `../gd32emu/gd32emu.cs`. `../gd32emu/gd32emu.csproj` adds the standalone emulator CLI. The tablet board model stays in `atf_twin.cs`; the chip core provides CPU, memory, peripherals and host utilities.

## Build and update flow

1. Interactive startup begins a complete background capture through xppen-tools and returns the tablet to runtime mode afterward. Source selection waits for completion.
2. The user selects a complete dump and preset. A dump with another mod's changes at known places (Mei's and Mily's mods: `Repair.Examine`, `Repair.MOD_NAMES`) has them set back to stock; the notice says what each was and how it was made, and the result must be exact stock (page checksums, whole-firmware CRC-32) or the dump is refused. A build of this tool is set back first (`atf_revert.cs`): every build carries an undo record (`OwnRevert.Record`, allocated just before the options text; `Image` notes which modification group last changed each input byte), with the stock value of each changed byte stored XOR the built value, so no stock code sequence appears twice in the image and every signature still resolves once. The record and the result are CRC-32 checked; a build without a usable record is set back from the user's own stock copy (`Repair.SearchRoots`). The layout resolver finds patch sites, called functions, tables and literals by signature. Each patch checks the expected original bytes.
3. The builder validates scan timing and any saved calibration profile, applies patches transactionally, and writes a complete application image.
4. The digital twin checks boot, USB, gain control and pen detection, then measures required frame timing, position spread and latency. A failed or missing required result prevents flashing.
5. An actual update requires a fresh complete backup and a comparison with the source firmware. The process runner preserves the external helper's exit status and drains both output streams. Dry-run has a separate preview path.

Startup captures live beside the executable. Pre-flash captures live under the selected output folder. Each location has `last-dump/ic902-main.bin` and full archived captures in `dumps/`. Publication of the latest complete dump is atomic; a failed or incomplete capture preserves the previous valid file.

## Calibration and restoration

The first pass builds checked telemetry firmware at the selected preset's fast stage and stock frequency. Normal report mode 4 uses 12-byte ID 2 pen packets on endpoint 3; telemetry hooks use the ID 7 path, so measurement firmware selects report mode 2 before constructing reports. The host requires calibration markers and fresh in-range position/pressure context before accepting signal samples.

Measurement builds disable bootloader-maintenance flags on a copied settings array. The final normal build retains the user's settings and performs any required maintenance power wait. A measurement-update flag explicitly marks the update type. The session treats measurement firmware as possibly installed before invoking the helper, retains that state after failure, and clears it only after a successful normal update. Reusing an output filename does not determine firmware state.

Calibration measures fast-stage pickup/drop thresholds and a position-spread-based jump margin. Thresholds cannot exceed their preset defaults. Frequency tuning, custom scan timing and unmeasured hover-stage transitions cannot be combined with those saved profiles. Multi-coil Read (R43) can: it changes how positions are worked out, not the scan. See [calibration](calibration.md).

The R37 `--hid-check` command reads native reports for five seconds without a backup or update. `--pen-resonance` separately reads the firmware's tracked resonance telemetry from an installed measurement build; it does not tune thresholds, select coils or perform a frequency-response sweep. Windows uses the built-in HID APIs without WinUSB. Installing measurement firmware remains an explicit, gated update; restore a checked normal image afterwards.

## Simulation and native boundaries

Each emulated `Machine` owns a flash copy. Scenarios that persist changes must save `machine.flash`. The current device profiles implement 64 KB flash and 8 KB SRAM. The instruction set and peripheral model are driven by the firmware under test; they are not complete Cortex-M4 or GD32F3x0 implementations.

The simulated clock runs at 72 MHz. Wall-time pacing does not calibrate instruction costs or flash erase/program latency against hardware. ADC levels and noise are supplied by the board model; physical hover height is not a model output.

The generic USB host validates descriptor bounds and active endpoints, then polls only advertised interrupt IN endpoints. Its existing interval policy maps `bInterval` 1 to 1 ms and 3 to 2 ms; this is a model policy, not a physical USB timing measurement. The tablet checks additionally require the supported interface/endpoint layout, packet types and sizes, and valid HID report descriptors. Standalone checks and the build gate use the same tablet validator.

Native readers use session generations to reject late callbacks and stale packets. Close requests cancellation and waits within a bounded deadline. Windows uses overlapped-read cancellation, Linux nonblocking device reads and macOS session-owned run-loop/callback resources. These paths still require physical device tests on each OS.

## Detailed references

- [Builder and scheduler design](DESIGN.md)
- [Firmware behavior and memory map](FIRMWARE_DESIGN.md)
- [Registered firmware layouts](FIRMWARE_VERSIONS.md)
- [Shared emulator interfaces](EMULATOR.md)
- [Chip emulator design](../../gd32emu/DOCS/DESIGN.md)
- [Verification scope](verification.md)
