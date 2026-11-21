# Pen resonance and tablet signal thresholds

<img src="../misc/AIdisclosure-most_docs.png" align="right" width="280" alt="Note: This document is mostly AI generated, with only minor corrections done by a human (stated to the AI).">

Pen resonance measurement is for inspecting the pen's resonant frequency. Tablet signal thresholds is a separate procedure for pickup/drop behavior. Neither procedure chooses dual-coil or multi-coil settings.

| Function | Purpose | Changes firmware? |
|---|---|---|
| `--hid-check` | Count native HID reports for five seconds and show received report IDs | No |
| `--pen-resonance` | Read the firmware's tracked resonance estimate for five seconds | No; measurement firmware must already be installed |
| `--calibrate` (Tablet signal thresholds) | Measure signal and position holds, then tune pickup/drop thresholds and jump margin | Yes, measurement and final normal updates |
| `--startup-calibration` (Pen frequency tuning, experimental) | Select a scan-frequency offset using the existing position-jitter objective | Included in the generated firmware when enabled |

## Check the PC HID connection

Close OpenTabletDriver and the XP-Pen driver, keep the pen in range, then run:

```text
AnargonsTabletTools --hid-check
```

This reads the current firmware's HID traffic without starting xppen-tools or flashing. Ordinary stock pen reports are sufficient to check transport; they do not contain the measurement firmware's resonance telemetry. No reports means the connection is not yet established by this check: inspect device presence, driver ownership and `--setup` output before trying calibration.

Windows uses the system HID APIs (`hid.dll`, `setupapi.dll` and `kernel32.dll`) and Raw Input (`user32.dll`). The HID readout requires no WinUSB package, driver replacement or third-party HID library.

On Windows the tablet appears as three interfaces. Interface 0 (two mice and a keyboard) and interface 1 (the pen, report ID 7, which carries pen reports and calibration telemetry) are held by Windows itself, so a program cannot open them for reading; interface 2 (vendor-defined) can be opened but stays silent unless a driver switches the tablet to its alternate report mode. Since R41 the pen collection is read through Raw Input, which receives the same reports from Windows' own reader. `--hid-check` prints one line per collection saying how it is read or why it is not. Before R41 only interface 2 was read, which is why every firmware showed "no reports at all from 1 open collection(s)". Backup and flashing still use the external xppen-tools executable and its own supported device-access setup.

## Read pen resonance

Build, check and install measurement firmware from your tablet's full dump (stock, or changed by a mod whose changes this tool undoes; see the README):

```text
AnargonsTabletTools main.bin measurement/ --rate fast --calibration-pass --flash
```

This setup step performs a real update through xppen-tools, requires a fresh backup and leaves measurement firmware installed deliberately. It disables bootloader maintenance for the measurement image. Omitting `--flash` builds and checks the image only.

Then hold the pen steady in range with no tip pressure and run:

```text
AnargonsTabletTools --pen-resonance
```

The result summarizes the firmware's tracked resonance estimate, transmitted in 16 Hz increments. Pressure changes a resonant pen's reported frequency, so compare readings with no tip pressure and the same placement. This exposes the existing firmware estimate; it does not perform an independent frequency-response sweep to establish a new resonance peak. It does not select a scan-frequency offset or modify a calibration profile. Restore checked normal firmware after using measurement firmware.

Restore normal FAST firmware with a separate checked build and update:

```text
AnargonsTabletTools main.bin normal/ --rate fast --flash
```

The original stock dump remains the build input for both steps. Keep all backups and the generated logs.

## Tune tablet signal thresholds

The legacy `--calibrate` command now labels its task Tablet signal thresholds. It measures one non-STOCK preset using the stock scan frequency, temporarily replaces normal cursor reports with measurement telemetry, then builds and flashes normal firmware with the measured settings or the preset defaults.

## Before starting

- Keep your tablet's original complete stock dump and existing full backups.
- Use a tested xppen-tools build for your OS, placed beside the application or configured with `ADT_XPPEN_TOOLS`.
- Exit your tablet driver: close the OpenTabletDriver window, or right-click XP-Pen's icon (XPPenTablet) in the system tray, next to the clock, and exit it. Run `AnargonsTabletTools --setup` and resolve any device permissions.
- There is no cursor while the measurement firmware runs. Do not press the pen's buttons (they change the readings) and do not use the mouse (a click can take the keys away from the tool's window); use only the keyboard. The tool says this before flashing and on every step.
- Keep experimental Pen frequency tuning off. Do not override scan timing for measured profiles. Multi-coil Read (R43) may stay on: it does not change the scan. Dual-coil Mode was removed in R43; the Dual-coil gains in older calibration files are ignored.
- Use the same pen, preset and tablet position when comparing the result with the default firmware.

From the executable folder, for example:

```text
AnargonsTabletTools main.bin output/ --rate fast --calibrate
```

Use `.\AnargonsTabletTools.exe` in PowerShell or `./AnargonsTabletTools` on Linux/macOS. The command performs real updates; xppen-tools asks for its confirmation before writing. A new full backup is required before each update.

## Preflight

After the measurement update, rest the tip lightly on the tablet's middle. The LED shines when the tip is within 7 mm of the middle (R44). Preflight requires genuine signal, position and pressure telemetry with pressure below 4096 (R44; it was 2048, too light). Ordinary pen reports are not calibration data. If only another report ID arrives, check that the measurement firmware was installed and that another driver is not holding the device. Timed steps do not begin until this preflight succeeds.

The preflight's light touch is also the first step's (R45-rc2): once the readings arrive, the tool measures that light touch straight away, so keep the tip resting there.

A failed preflight does not end calibration. The tool reopens the tablet and checks again by itself three times (after an update, Windows can list the tablet's collections a moment late), then asks: Enter checks again, `f` flashes the same checked calibration build again (one more update; the run's backup is reused), and `d` skips calibration so the final build uses the preset defaults. The message says what arrived instead: no reports at all, other report IDs, ordinary pen reports, or a reader failure. If no reports arrive at all, exit the tablet driver (as above) before pressing Enter.

## The three steps

| Step | Placement | Timed reading |
|---|---|---|
| Minimal pressure | Rest the tip on the middle of the drawing area (below the button bar), barely touching with almost no weight; keep the live pressure below 4096. | 4 seconds |
| Normal hover | Hold the pen still at your normal hover height, about 5 mm: a little less than a wooden pencil lying flat, or half a phone's thickness (3 to 8 mm is fine). Keep reference objects beside the tablet, not under the pen. | 4 seconds |
| Highest hover | Start at normal hover over the middle. "Move the pen up until the point where the LED stops shining, then bring it down until it starts shining again. This is your tablet's detection limit. Hold it at that position during calibration." (Anargon's wording) | 4 seconds after positioning |

R45-rc2 (Anargon) removed two steps. The no-pen step measured nothing: the firmware sends readings only while it tracks the pen, so it always showed LOST. The slow lift looked for the same detection limit as the highest hover, nothing from it went into a build, and on Anargon's tablet it ended at the coils' floor (72) both times. The readiness check and the first step asked for the same light touch one after the other; the first step now measures right after the check.

The highest-hover step provides a 10-second live positioning preview. Press Enter to measure the selected height or `r` to repeat positioning. After each timed step, Enter advances and `r` repeats it. Short settling collections keep movement between poses out of the stationary holds.

**The LED marks the step's target (R44).** It is off with no pen, or with the pen more than 7 mm from the middle of the drawing area on either axis (a 14 x 14 mm box around (80, 45) mm); on inside the box; and it blinks (2.5 Hz) only while the step is recording. So the middle and the detection limit can be found without watching the screen. The tool sets the target and the recording state on the tablet through USB string requests that only the measurement firmware answers (`ADTZ1:` replies); if the tablet does not confirm one, the tool says so and the LED only shows whether the pen is seen.

LOST during light contact or the 5 mm hold needs investigation. If the highest-hover hold is not tracked, the preset's pickup/drop defaults are retained.

## With ALPI: measured in the background (R45-rc2)

Full calibration with ALPI runs no measurement firmware and no guided steps. Anargon: "ALPI should combine all calibration steps as necessary, running them in the background". The ALPI training firmware records, with every sample, the detection signal (the weaker of the strongest X and Y line's last reading, the value the pick-up and drop levels compare with) and the pen's frequency. Its fast stage keeps the pen down to the measurement firmware's levels (pick-up 40, drop 24), so the cursor follows the pen a little higher than usual during the training. From the training's readings (`Cli.FromTraining`):

| Guided step | From the training |
|---|---|
| Minimal pressure | fast-stage readings with pressure below 4096 (signal, pressure, the pen's resting frequency) |
| Normal hover | the 4-second centre hold (else every fast-stage hover reading) |
| Highest hover | the weakest 5 % of the fast-stage hover readings (at least 1000 of them) |

The pick-up and drop levels follow the same rule as the guided steps (`Cli.Thresholds`), never above the preset's. They are saved in the training file (`pick=`, `drop=`) and used when it is reused, also with `--apip FILE` (a `--calibration` file wins). If a build cannot use them (for example with Pen frequency tuning on), it keeps Anargon's levels or the preset's and says so. With expert timing or gain overrides the training keeps the build's own levels and measures none.

Limits: the training keeps the final build's hover stages, so a weak pen moves to Steady Hover before the fast stage loses it. The weakest fast-stage readings are therefore where Steady Hover takes over, not the detection limit itself, and the levels can come out higher than Standard's (never higher than the preset's). Pressure in the air cannot be counted (a training reading with pressure cannot be told from a light touch), and the report rate is not measured. Jump Rejection's margin is not needed: ALPI replaces it. Checked in the digital twin only (ULTRA 96 / 64, RAPID 135 / 90); not yet on the tablet.

Skipping the training (R45-rc3, Anargon: "full > skip alpi steps > alpi defaults with mismatched calibration + frequency defaults"): ALPI stays on with Anargon's training, the pick-up levels are Anargon's (RAPID, ULTRA) or the preset's, and the scan stays at the stock frequency. The wizard says these were not measured on this tablet. ALPI is never on without Full: the setting is off by default, locked below Full in the wizard, and on the command line only `--apip FILE` turns it on.

## Results and saved profiles

The result reports measured signal levels, report rate, light-contact pressure and position spread at 5 mm. The spread includes hand movement and is not a direct measurement of electronic noise. No-pen receiver noise cannot be measured because telemetry stops when tracking stops.

Since R41 it also reports the pen frequency while hovering (the normal and highest hover steps): the median and highest firmware frequency reading, how many readings reached 509 kHz, where pressure starts, and how many reports showed pressure with the pen in the air. This is the hardware check for the preset's frequency readings, measured with the same timing the final build uses, so a separate `--pen-resonance` run is not needed for it. If the readings reach the pressure band, the result says what to change: turn on Accurate Pressure Readings, or on ULTRA use a slower preset. The saved file records the numbers as `air_freq_median`, `air_freq_max`, `air_freq_readings`, `air_freq_in_press_band`, `air_reports` and `air_press_reports`. They are not read back when the file is reused.

Saved files use format 2 and the `default-fast-stock-frequency` profile. Each file belongs to its recorded preset. Older or incompatible files must be recreated. Reuse a valid file with:

```text
AnargonsTabletTools main.bin output/ --rate fast --calibration output/calibration-fast.txt
```

The fast-stage pickup and drop thresholds cannot exceed preset defaults. Boost-stage transitions, second-coil gain and startup frequency selection are not measured by this procedure.

## Finish with normal firmware

The measurement build does not provide normal cursor reports. `--calibrate` includes the final normal update; if measurement fails after the first update, the command can restore the preset with defaults. In the wizard, complete the final normal flash choice.

An update may program the image before reporting a verification or reconnect failure. The tool therefore treats measurement firmware as possibly installed until a normal update succeeds. If the session exits with that warning, build a normal preset from your original stock dump and flash its checked image, or restore stock. Preserve the backups and logs if restoration fails.

Compare pickup/drop height, still hover, tip pressure and buttons against the default at the same tablet position. A successful simulated or live measurement session does not by itself qualify physical hover range on every preset and OS. Follow the [hardware verification sequence](verification.md#hardware-verification).
