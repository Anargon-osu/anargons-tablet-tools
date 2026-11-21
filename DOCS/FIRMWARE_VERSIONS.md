# Registered firmware layouts

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

The builder registers February and December 2025 stock layouts. The comparison tables below preserve the reference byte/disassembly map used by the layout resolver; they are not a new physical-board comparison. The latest local candidate run had a verified February fixture but no complete December fixture. See [verification](verification.md) for current coverage and fixture provenance.

| | February build | December build |
|---|---|---|
| Build string (USB string 5) | `2025-02-14_release001` | `2025-12-23_release001` |
| Build string location | `0x0800BC9A` | `0x0800BCA6` |
| Firmware ends at | `0x0800C7CC` (`0x8FCC` bytes) | `0x0800C7D8` (`0x8FD8` bytes, 12 more) |
| Code region ends / `.data` starts | `0x0800C188` | `0x0800C194` |
| Layout registration | February reference | December reference; full current fixture run required |

## 1. What is the same

- **The whole bootloader area is byte-identical** (`0x08000000`-`0x080037FF`): bootloader code, its config page (`0x08003000`) and the boot-state page. Nothing in flash tells the two boards apart, so whether December tablets have different hardware cannot be told from dumps.
- **RAM layout is identical:** both builds reference the same 124 RAM addresses, so every RAM variable (pen report buffer `0x200005F0`, scan state, AGC, resonance...) is in the same place.
- **USB layout:** the reference comparison records the same VID/PID, three HID interfaces/report descriptors and 3 ms report interval, with different build strings.

## 2. What XP-Pen changed (three functional changes)

| # | Where (Feb -> Dec) | February | December | Effect (inferred from the code) |
|---|---|---|---|---|
| 1 | movement dead-band setup, `0x080041C4` | coefficients 70, 60, 50, 45, 40, 35, 30, 25... | 80, 80, 60, 45, 40, 35, 30, 25... | **stronger Movement Smoothing at slow speeds**; the function is 2 bytes shorter |
| 2 | pen pick-up decision, `0x08005840` -> `0x0800583C` | after 32 search tries, track if signal > threshold + 100 | same, but if the status byte at `0x200006A8` is 2 and the threshold is above 30, it is lowered by 30 first | **easier pen pick-up in one search state**; 12 bytes longer |
| 3 | search probe frequency list, `0x0800BFBE` -> `0x0800BFCA` | table indices 15, 19, **21**, 26, 29, 32, 36 | 15, 19, **22**, 26, 29, 32, 36 | the third search probe is one step higher in frequency |

Plus one change with no effect: two loads at `0x0800425E` swapped registers (compiler ordering), 2 bytes shorter. The compressed `.data` differs by relocations and changed tables. Matching USB descriptors do not establish identical analog behavior or physical hardware.

## 3. How the code moved (the offset map)

Everything else is the same code at a new address. Rule of thumb: **December address = February address + shift**.

| February range | Shift | Why |
|---|---|---|
| `0x08003800` - `0x080041CA` | 0 | before the first change |
| `0x080041CC` - `0x080042A6` | -2 | dead-band function 2 bytes shorter |
| `0x080042A8` - `0x08005866` | -4 | plus the register-swap edit |
| `0x0800589C` - `0x0800C7CC` | +12 | after the 12-byte pick-up change (includes `.data`) |

### This tool's patch sites in December

Every site still exists with the same instruction. Branch sites (calls) differ only in their encoded jump, because the function they call also moved.

| Group | February | December | Shift |
|---|---|---|---|
| hardware divide | `0x0800398C` | `0x0800398C` | 0 |
| dead-band floor | `0x08004258` | `0x08004256` | -2 |
| dead-band coefficients | `0x080041C4`... | **different layout** (retuned, change 1) | - |
| scan samples | `0x08005302` | `0x080052FE` | -4 |
| pen / pressure / tilt hooks, frame-done | `0x080040F4`-`0x0800416A` | same addresses | 0 |
| LED, telemetry sends | `0x08004814`, `0x08004B32`, `0x08004B66` | `0x08004810`, `0x08004B2E`, `0x08004B62` | -4 |
| scan pulses / poll / tick, AGC cap, band-edge, scheduler, thresholds, search, calibration, Multi-coil Read's position calls (R43), probes, interpolation | `0x08005966`-`0x0800BC22` | each +12 | +12 |
| bInterval literals in `.data` | `0x0800C63B`, `0x0800C658` | `0x0800C647`, `0x0800C664` | +12 |

### Resolved helper functions and tables

| Used for | February | December |
|---|---|---|
| flash: erase page / program N words | `0x08009A1C` / `0x08009A40` | `0x08009A28` / `0x08009A4C` |
| USB send | `0x08009CB0` | `0x08009CBC` |
| coil select / receive / transmit helpers | `0x080098EC`, `0x0800990C`, `0x08009944`, `0x08009980`, `0x080099D0` | each +12 |
| GPIO, scan and frame helpers | `0x080074AC`, `0x08007674`, `0x08007B30`, `0x080084C4`, `0x080084C8`, `0x08008E94`, `0x08009B58`, `0x08005E84`, `0x08006F54`, `0x08007604`, `0x0800765A`, `0x08007840` | each +12 |
| unchanged | `0x08003A12`, `0x080040C4` | same |
| probe frequency table | `0x0800BFBE` | `0x0800BFCA` (one value changed) |
| pressure-table pointers (read as data) | `0x0800766C` / `0x08007670` | `0x08007678` / `0x0800767C` |

## 4. Layout resolution and validation

`atf_symbols.cs` stores signatures for patch sites, called functions, tables, literal-pool pointers, edited `.data` bytes and twin hooks. Build-dependent instruction bytes are masked where appropriate. The resolver requires one unambiguous match and validates registered-build locations against the reference layout. A missing or ambiguous location is incompatible; normal builds stop instead of applying a reference offset blindly.

The firmware end comes from its own startup scatter-load table. The builder does not trim every input to the February boundary. Movement-smoothing patches inspect the coefficient loads for the resolved build, and the twin uses the same resolved addresses for coil/timing hooks.

A later firmware whose code moved may still resolve, but compatibility is not guaranteed by its date or by these offset tables. Changed code must satisfy signatures, expected-byte checks, timing/profile validation and the required twin gate before an image can be offered for flashing. Register a new reference only after inspecting its complete input and running the full fixture-backed checks.

## 5. Incomplete update images

Some IC-902 dump/update paths stop at `0x0800C188` (`0x8988` application bytes). R42 found the main one: older xppen-tools dumps wrote `deco01v3-code.bin` and `deco01v3-stock.hex` ("application restore image") at a fixed 35208 bytes, and `xppen-tools ihex --ic 902` converts such a code.bin as it is. Flashed, that image leaves the start-up table and `.data` erased (both damaged tablets of rev 35 reproduced byte for byte). The full `main.bin` of a dump is complete; never flash a `code.bin` or `stock.hex` without checking that it reaches the firmware end (`0x0800C7CC` February, `0x0800C7D8` December). This tool renames such files next to its backups to `*.INCOMPLETE-DO-NOT-FLASH`. That boundary omits the February startup data. The December code ends at `0x0800C194`, so the same boundary also omits its last 12 code bytes. A date-string match cannot detect or repair this truncation. Use the complete original 65536-byte dump and retain its SHA-256; damaged firmware requires the recovery workflow, which restores the end from the user's own complete copy of that build (checked by CRC-32), never from guessed data.

Normal application erases begin at `0x08003800`. A comparison showing equal bootloader/RAM layouts does not qualify cross-flashing between tablets or establish equal physical hardware. Build from the connected tablet's own stock source and keep its complete backups.

## 6. Fixture requirements

| Reference | Application end | Recorded CRC | Current local evidence |
|---|---|---|---|
| February | `0x0800C7CC` | `B65DB693` | Full fixture-backed candidate checks recorded |
| December | `0x0800C7D8` | `4525CA51` | Matching full fixture unavailable in the recorded local run |

The February full-dump SHA-256 is `26e6bfaa2f9e4ccc41e95b34661f3eb66e0e814c4afd36483b6d0dc0bee7329d`. A renamed file or modified build string is not a second stock fixture. Full release checks require both registered fixtures and report missing inputs as failure; explicit February-only candidate checks retain the December gap.
