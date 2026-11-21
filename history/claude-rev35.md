# Claude outputs in this chat: revision 35

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

Source: this chat (conversation 4, continued after revision 34). Model: Claude Opus 5.5.

| Revision | Package | Request |
|---|---|---|
| **35** | anargons-deco640-tools-rev35.zip | a repair tool for damaged tablets inside the program (Anargon's and Anomy's broken dumps), Anomy's patcher and stock files; rename to anargons-deco640-tools; first screen "Choose the tool"; teardown document for recovery steps; `.env`; GitHub tests with the emulator set by variables; publish-ready; WTFPL; flash count read from the tablet (Bootloader / Firmware / local PC); fixed USB IDs; no HID warning in calibration |

## The two damaged tablets
Both dumps (Anargon's, February build; Anomy's, December build): every code byte intact; everything from
`0x0800C188` to the firmware end erased (1599 / 1611 bytes `0xFF`) - what a flasher that stops at xppen-tools'
IC-902 size leaves; settings page `0x08003400` with only its boot state (normal after any update, as the verifier
showed). Delivered first, by hand: `anomy-stock.hex` (December stock, CRC 4525CA51, twin-checked) and the first
`Anomy-specific-patcher.hex`. **Anargon: the first patcher worked on Anomy's tablet.**

## Delivered
- **Anargon's Basic Tablet Recovery** (`atf_repair.cs`, `atf_stockref.cs`, option 2 of the first screen,
  `--recovery`): only a full 64 KB `main.bin`; code checked per 1 KB page against stored CRCs; the end restored from
  stored stock bytes; damaged code fixed only from a verified stock copy on the PC; result must equal the build's
  CRC. Writes `NAME-specific-patcher.hex` (stock + a start-up step: if the last settings word is erased, write the
  stock settings, then boot state 1, and software-reset through the firmware's own DFU tail - the tablet comes back
  once in update mode), `name-stock.hex`, `NAME-repaired-main.bin`, a report; checks the stock file, the patcher and
  the repair itself (from power-on, on the tablet's own flash) in the twin. Anything it cannot repair gets the "send
  it to an expert, a friend or an AI model" page. The flasher refuses damaged dumps.
- **Names:** Anargon's Deco 640 Tools (`anargons-deco640-tools`, `AnargonsDeco640Tools`); first screen
  "Choose the tool: 1. Anargon's Tablet Firmware Mod 2. Anargon's Basic Tablet Recovery".
- **`.env`** (`atf_config.cs`, `.env.example`): teardown link (`ADT_TEARDOWN_URL`, used by every recovery
  instruction), support link, xppen-tools path, state folder, emulator folder, test dumps.
- **GitHub:** `tests.yml` (Linux with the .NET SDK, Windows with `build.cmd`) runs `tests/run-tests.sh`; the
  emulator comes from `GD32EMU_URL` or `GD32EMU_REPO`/`GD32EMU_REF`, test dumps from `ADT_TEST_STOCK_DUMP_URL` (see
  README). `build.yml` builds Windows with `build.cmd`. `.gitignore`s, WTFPL `LICENSE` in both repositories.
- **Flash count from the tablet:** `Patches.CounterString` - USB string 173 `ADTC1:<B|F|N>:<count>` built from page
  `0x08002C00` on request; PC order live string > telemetry > dumps > local PC, labelled Bootloader / Firmware /
  local PC; `--flash-count`. AGENTS.md rule 11: never change the page, records or string 173.
- USB IDs are constants; the calibration step's HID warning removed.
- Verifier fixes: emulator SWRSTF/RSTFC (the bootloader keeps update mode after a software reset); patcher writes the
  boot state last and triggers on the last settings word; erased settings page is not damage; FixWorks expects the
  real update-mode result.

## Independent verification (two rounds, two verifiers each)
- Round 1 (recovery patcher): no brick risk; found the missing SWRSTF model (the twin showed the wrong bootloader
  path), the interrupted-write retry gap, and that an erased settings page is normal - all fixed.
- Round 2 (string 173 and PC side): the readout behaves exactly like stock for every other descriptor (types 0-4 x
  indices 0-255 swept on both builds and three variants), RAM `0x20001F80` untouched by every variant, counts match
  `ReadFlashCount` through 128 real update cycles (page full + compaction). Found and fixed: counts above 999999 and
  corrupt bases (now N / -1 on both sides, capped at 999999); telemetry and rev 34 `dumps.txt` lines labelled
  Firmware regardless (now from the tablet's own dumps); a live "N" now beats stale dumps; malformed strings rejected;
  a dump erased past the build string is now recognised by its code pages; a contradictory recovery message.

## Verified (no hardware)
| Check | Result |
|---|---|
| `tests/run-tests.sh` with both stock dumps, real .NET 8 build | see the final reply / tests35.txt |
| both broken dumps through the recovery tool | repaired; stock CRC exact; patcher leaves the tablet in update mode with the settings restored (twin) |
| string 173 | stock: not answered; firmware counter `ADTC1:F:000001`; Bootloader Counter `B:000001`, after an update `B:000002` |
| Bootloader Counter lifecycle and return-to-stock with SWRSTF modelled | install, power-on, count, restore: as before |
| `.NET` single-file build vs Java translation | identical recovery files |

## Follow-up: Anargon's tablet and Linux / macOS
- Anargon's tablet runs an earlier build with the Bootloader Counter at 2. Twin, for counters installed by rev 33 and
  rev 34: flashing a rev 35 build without Bootloader Changes writes 0 bootloader bytes, the update counts (3), string
  173 reports `ADTC1:B:000003`; a rev 35 counter build also writes 0 bytes (routine byte-identical).
- Linux: all hidraw nodes of the tablet are read; string 173 through usbfs (`USBDEVFS_CONTROL`, libc only). macOS:
  IOKit `IOHIDManager` input reports on a run-loop thread; string 173 through `IOUSBDeviceInterface::DeviceRequest`.
  `--setup` prints the udev rule (Linux) or the Input Monitoring step (macOS). Compiled for linux-x64, osx-arm64 and
  win-x64; not run with a tablet.

## Not done / open
- The Windows CI job and `build.cmd` were not run here (no Windows); the Linux CI path was run locally.
- How the two tablets were damaged is unknown.
