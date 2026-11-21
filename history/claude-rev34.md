# Claude outputs in this chat: revision 34

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

Source: this chat (conversation 4, continued after revision 33). Model: Claude Opus 5.5.

| Revision | Package | Request |
|---|---|---|
| **34** | anargons-tablet-firmware-rev34.zip | LED back to stock (lit during calibration, blinking while a step is in progress); rename the project itself from deco640; close open issue 8 with backups into `dumps/` (whole flash once as `primary-dump`, then the code section only), required every time |

## Delivered
- **Rename:** `deco640/` -> `anargons-tablet-firmware/`, `deco640.csproj` -> `AnargonsTabletFirmware.csproj`, executable
  `AnargonsTabletFirmware`, sources `deco640_*.cs` -> `atf_*.cs`, namespace `Deco640Twin` -> `AnargonsTabletFirmware`,
  `DECO640_STATE_DIR` -> `ATF_STATE_DIR`, state folder `AnargonsTabletFirmware` (the old `deco640/state.ini` is still
  read), cs2java class `Atf`. `history/` keeps the old names (it records what was delivered then); old package names
  (`deco640-revN.zip`) are kept where they are cited.
- **LED:** the rev 33 "always on" redirects now apply to the calibration-pass build only. `ledtick` (every tracked
  frame) blinks 200 ms on / 200 ms off in that build and while Startup Calibration runs, and otherwise leaves the LED
  alone; the new `ledsearch` (search restart) holds it lit in the calibration-pass build. Twin (PC13 read from GPIOC
  OCTL): calibration-pass build on with no pen, blinking 200 ms while the pen is tracked, on again after it leaves;
  a normal FAST build follows stock exactly (lit with the pen, stock 500 ms hover toggle).
- **Backups (`Cli.Backup`, `BeforeFlash`, `TabletMatches`):** every flash path makes a backup first and refuses to
  flash without one. First backup: `dumps/primary-dump/ic902-main.bin` (whole 64 KB). Later: `dumps/<date-time>/
  ic902-code.bin` (0x08003800-0x0800FFFF; the full dump xppen-tools writes is deleted after the code section is cut
  out). `dumps/dumps.txt`: one line per backup (time, file, kind, build, CRC-32, flash counter). Pre-rev-34
  `backups/` folders: the oldest becomes the primary dump. The flash count is also read back from `dumps.txt`.
- **Tablet check (open issue 8):** stock tablet -> its build string must equal the image's source build; tablet
  running one of our builds -> `Layout.SameCode` (every location found in both at the same address) and
  `Layout.KnownByCode` (names the verified build it came from). Mismatch: full-screen page, typed
  `I know what I am doing.` to continue. Without xppen-tools next to the program, the printed manual commands now
  include `xppen-tools dump dumps\<date-time>` before the update.
- `P.DeleteFile` added to gd32emu (and its Java translation).

## Verified (twin and a stand-in xppen-tools script; no hardware)
| Check | Result |
|---|---|
| February: all five presets | pass the twin |
| December: all five presets | pass the twin |
| twin selftest | ok |
| backup 1, stock February tablet | primary dump written, matched, flashed |
| backup 2, tablet running our February build | code-section dump, matched by layout, flashed |
| backup 3, stock December tablet, February image | stopped by the warning page |
| tablet running our December build, February image | stopped; "a build of this tool made from 2025-12-23_release001" |
| December image on a December tablet | flashed |

## Not changed / open
- xppen-tools has no partial dump that this tool knows of, so every backup still reads the whole flash (~3 min);
  only the code section is kept.
- The calibration-pass LED uses "pen tracked" as "a step in progress": the firmware cannot see the PC's step state.
- `dumps.txt` shows "(no build string)" for our builds made from December (their descriptor is found by layout only).
- Not run with the real xppen-tools or on a tablet.
