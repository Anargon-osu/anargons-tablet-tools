# Running the tool's algorithms on the PC: an OpenTabletDriver plugin

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

**Status: design proposal (R45-rc3).** Nothing in this document is built yet. Building it needs OpenTabletDriver's
plugin library, which is a download; that waits for Anargon's go-ahead. Numbers marked *(twin)* or *(tablet)* were
measured for the firmware; numbers marked *(estimate)* were not measured and must be test-fitted before anyone
quotes them as limits.

The idea, from Anargon: take the tool's filtering algorithms out of the tablet's firmware and run them on the PC, as a
plugin for [OpenTabletDriver](https://opentabletdriver.net) (OTD), the open-source tablet driver. A user who wants
the full suite can then use it **without flashing anything**, and, because OTD already supports hundreds of tablets,
the same plugin works on **any tablet OTD supports**, as long as the tablet is described the way these documents
describe the Deco 640.

This document explains which parts of the tool can move to the PC and which cannot, and why; how the plugin would be
built so that it stays exactly equal to the firmware version; and what "the full suite on the PC" would take. It is
written for the same reader as `FIRMWARE_DESIGN.md`: someone who programs but has not worked on tablet firmware.

---

## 1. Background: how a pen position reaches a program

1. **The tablet senses.** Its chip drives the coil grid, reads the pen's answer on many coils, and turns the readings
   into a position, a pressure and a tilt (`FIRMWARE_DESIGN.md` sections 7 and 8). This happens about 220 times a
   second on stock firmware and up to 1000 times a second with this tool's presets.
2. **The tablet reports.** It puts the result into a small USB message, the **report** (10 bytes on the Deco 640:
   report ID, status bits, X, Y, pressure, tilt), and the PC fetches one per USB poll (`FIRMWARE_DESIGN.md` 6.2).
3. **A driver translates.** The XP-Pen driver, or OTD instead of it, reads the reports, maps the tablet area to the
   screen, applies pressure curves and filters, and hands the result to the operating system as cursor and pen input.

OTD's step 3 is a **pipeline**: each report passes through a chain of elements, and plugins can add elements. A filter
plugin receives each report, may change it (or hold it back, or send extra ones), and passes it on. OTD lets a filter
choose where it sits: **before the area mapping**, where positions are still in the tablet's own units (what this
tool's algorithms are written for), or **after it**, in screen units.

**The key consequence:** a PC-side plugin only ever sees what the tablet *reports*. Everything that happens before the
report, the coil readings, the scan timing, which coils are read and how often, never reaches the PC on a stock
tablet. That splits the tool's features into two layers.

---

## 2. Two layers: sensing and processing

| Feature (FIRMWARE_DESIGN.md section) | Layer | Can it run in an OTD plugin? |
|---|---|---|
| Report-rate presets QUICK to ULTRA, 1 ms USB interval (6.2, 9) | sensing | **No.** They decide how often the tablet measures and reports. A PC cannot make the tablet measure more often. |
| 12-bit Readings, Frequency Hold, Coil Hold, Multi-coil Read (7, 8) | sensing | **No.** They work on coil readings, which a stock tablet never sends. |
| Hover step-up, Reach hold, Long-range Pickup, Tilt Frame Skip (10) | sensing | **No.** They decide how the tablet scans while the pen is high. |
| Bootloader Timer Off, the AGC fix (3.3, 7) | sensing | **No.** They remove noise at its cause, inside the chip. |
| Removing stock Position Interpolation and Movement Smoothing (12) | processing, inside the tablet | **No.** A filter the tablet already applied cannot be undone afterwards: its delay is already in the report. |
| Jump Rejection (12) | processing | **Yes.** It only needs the stream of positions. |
| ALPI: coil-distance correction, alpha-beta tracker, still-pen gains (12) | processing | **Yes,** with one change: its bands (4.4 below). |
| ALPI's robust filter (several trackers side by side, `atf_apipr.cs`) | processing | **Yes,** same change. |
| Soft Pressure and other pressure curves | processing | **Yes,** and OTD already has pressure curves of its own. |

So the plugin can carry the **processing** layer: ALPI and its robust filter, Jump Rejection, and pressure shaping.
The **sensing** layer stays firmware-only, because it is about *how the tablet measures*, not about what is done with
the measurement.

This also answers which firmware to combine the plugin with:

- **Stock firmware:** works, but the stock filters (Position Interpolation, Movement Smoothing) still run in the
  tablet, so the plugin filters positions that were already averaged. The result is calmer but keeps stock's delay.
- **A build of this tool with its own filters off:** the best combination on a Deco 640: the sensing layer's noise
  removal and report rate in the tablet, the processing layer on the PC. Two filters in a row (the firmware's ALPI and
  the plugin's) would only add delay, so a build for use with the plugin must leave ALPI and Jump Rejection out.

---

## 3. What "compatible with all tablets" means

The processing layer needs only four things from a tablet, all of which OTD provides for every tablet it supports:

1. a stream of reports with position, pressure and in-range state;
2. the position's **units** (how many report units per millimetre), from OTD's description of the tablet (its
   digitizer width in millimetres and its maximum X and Y);
3. **time** between reports (OTD can measure it);
4. for the **coil-distance correction** only: the coil pitch (the distance between coil centres, 5.080 mm on X and
   5.083 mm on Y on the Deco 640). It comes from a teardown, a datasheet, or the porting guide's steps
   (`porting-guide.md`); without it, the correction is left off and the tracker still works.

"So long as they follow relevant docs" therefore means: a tablet profile that states its units, its report rate and,
if known, its coil pitch, plus a training run on that tablet (4.6). Nothing in the processing layer depends on the
Deco 640's chip, firmware or USB format.

---

## 4. Design

### 4.1 One copy of each algorithm, shared

The tool already has a **C# copy** of every filter it builds into firmware, and the twin checks, on every frame of
every build, that the firmware's result equals the C# copy's **bit for bit**:

| Algorithm | C# copy | Firmware check |
|---|---|---|
| ALPI coil-distance correction and tracker | `Apip.Corrected`, `Apip.Axis` (`atf_apip.cs`) | twin, every frame of every ALPI build |
| ALPI robust filter | `ApipRobust.Axis` (`atf_apipr.cs`) | twin, every frame |
| Multi-coil Read's fit | `MultiCoil.Calc` (`atf_multicoil.cs`) | twin (sensing layer: not for the plugin) |

The plugin would **compile these same source files**, not a rewrite. Then the guarantee carries over: the plugin
computes exactly what the firmware computes for the same input, because it is the same code that the twin already
holds the firmware to. The algorithms are whole-number arithmetic (positions in fixed-point 1/100 mm), so the result
does not depend on the PC's floating-point unit either.

To make that possible, the first step is to move the pure arithmetic (the per-reading functions and their state
classes) into files that do not need the rest of the tool (no emulator, no firmware builder), for example
`atf_filters.cs`. The tool keeps using them as before; the twin checks prove nothing changed.

### 4.2 The plugin project, kept apart from the tool

This tool is one dependency-free program (`AGENTS.md`, "dependency-free"): nothing to install, built with the C#
compiler that ships with Windows. An OTD plugin cannot be: it must reference OTD's plugin library and target the .NET
version OTD runs on. So the plugin is a **separate project** in its own folder (for example `otd-plugin/`), with its
own build, and the tool's `build.cmd` never touches it. The tool does not depend on the plugin, and the plugin depends
on the tool only through the shared source files of 4.1.

### 4.3 The filters it would offer

| Plugin filter | From | Settings shown in OTD |
|---|---|---|
| ALPI | `Apip` (correction, tracker, still-pen gains) | the profile file (4.5); correction on or off |
| ALPI robust | `ApipRobust` (2, 3 or 4 trackers) | the profile file; number of trackers |
| Jump Rejection | the firmware's Jump Rejection, as a C# copy | none (for setups without ALPI; ALPI replaces it, as in the firmware) |

All three sit **before the area mapping**, in tablet units, and handle one report at a time: each report in gives one
report out, at once. OTD can also run filters on a timer that send reports faster than the tablet does (upsampling);
these filters would not, because a guessed position between two real ones adds no information (the project's rule:
remove noise at its cause, and do not add work that adds delay or guesses, `AGENTS.md` "noise first").

The OTD names for these concepts (in OTD 0.6: filter elements that consume and emit device reports, with a pipeline
position before or after the transform, and property attributes for settings) must be checked against the OTD
version the plugin is built for; OTD's plugin interface has changed between releases.

### 4.4 Bands without a signal level

In the firmware, ALPI keeps separate gains for each **band** of signal strength (touching, low hover, high hover),
because a weaker signal gives noisier positions (`FIRMWARE_DESIGN.md` 12). The firmware knows the signal strength;
a standard report does not carry it. The plugin has three sources instead:

1. **Touching or not**, from the report's pressure and tip state: exact, and the most important split.
2. **The noise now.** ALPI already measures the current noise itself, from the second differences of the readings (a
   pen's own movement adds almost nothing to them at these rates), because within one band the noise still changes
   3 times with the height *(twin)*. The plugin can use this measured noise to choose between the hover bands.
3. **Hover distance**, where OTD reports one for the tablet (some tablets send it).

Option 2 is the one that works on every tablet. Whether it separates low and high hover as well as the firmware's
signal level does is the first thing to measure, on recorded reports, before the plugin is built around it.

### 4.5 Units and profiles

The firmware ALPI works in 1/100 mm (or 1/200 mm in the XP-Pen driver's report mode 4). The plugin converts OTD's
report units to the same fixed-point millimetres with the units of section 3, so the trained numbers mean the same
thing on every tablet. A **profile** is a small text file per tablet and pen: the units, the coil pitch (if known) and
the trained table (the 16-segment correction per band and axis, and the gains), in the same format as the tool's
`apip-<preset>.txt`, so a Deco 640 user can reuse a training made with the tool.

### 4.6 Training on the PC

The firmware training (`FIRMWARE_DESIGN.md` 12) records positions inside the tablet and reads them out over USB. On
the PC the data is already there: every report passes through the plugin. A **recorder** mode would write each report
(time, position, pressure, in-range) to a file while the user draws normally; the tool's training code then fits the
table and gains, as it does today, and writes the profile. The recording stays on the user's PC.

### 4.7 Cost

Each filter is a few dozen whole-number operations per report and axis; at 1000 reports a second that is a tiny load
for any PC *(estimate)*. The delay the plugin adds is the filter's own (none for a pen moving at a steady speed, for
the tracker) plus the time OTD takes to pass a report along its pipeline.

---

## 5. The full suite on the PC: a raw-reading mode

The sensing layer cannot move to the PC on a stock tablet, because the coil readings never leave it. It could move
partly if the tablet sent its **readings** instead of a finished position:

- **Firmware side:** a build that keeps scanning as today (timing, coil choice and frequency search must stay in the
  chip: they react within microseconds), but sends the readings each frame uses, the five lines around the pen on each
  axis, the frequency readings for pressure, the signal level, instead of, or next to, the position. The tool's
  measurement firmware and line recorder already send such readings on the Deco 640 (`FIRMWARE_DESIGN.md` 12,
  `atf_apip.cs`), so this is an extension of existing work there.
- **Bandwidth:** five lines per axis at 12 bits, plus a few frequency and level values, is a few tens of bytes per
  frame, which fits in one 64-byte full-speed USB report per millisecond *(estimate: to be test-fitted in the twin
  before it is quoted)*.
- **PC side:** an OTD plugin with its own **report parser** (OTD lets plugins read tablets with unusual report
  formats) that computes the position with the same code the firmware runs (the five-line arithmetic,
  `MultiCoil.Calc`'s fit), then the processing layer.

What that buys: Multi-coil Read, the coil-distance correction from real signal levels, and the exact bands, all
changeable on the PC without flashing again. What it does not buy: the report rate and the scan itself stay in the
firmware, and every tablet needs its own raw-mode firmware, so "all tablets" holds only for the processing layer of
sections 2-4.

---

## 6. Risks and limits

| Risk | Why it matters | What to do |
|---|---|---|
| Two filters in a row | a firmware filter plus the plugin's filter doubles the delay for no gain | use the plugin with stock firmware or a tool build with its filters off (section 2) |
| OTD's plugin interface changes | a plugin built for one OTD release may not load in another | pin the OTD version; keep the OTD-facing code a thin shell around the shared algorithms |
| Bands from noise alone | may separate low and high hover less well than the signal level | measure on recordings first (4.4) |
| OTD replaces the XP-Pen driver | the user has to switch drivers | the tool's `--setup` already explains OTD and the XP-Pen driver side by side |
| Trust | a plugin is code the user runs, like a patcher package | publish source; same rules as `patcher-packages.md` |

---

## 7. Proposed order of work

1. Move the filter arithmetic into tool-independent files (4.1); the twin checks prove nothing changed.
2. Record OTD-style report streams (from the twin's simulated pen first, then from a tablet) and check, offline, that
   the shared ALPI code gives the firmware's results, and how well noise-based bands work (4.4).
3. **Needs Anargon's go-ahead:** fetch OTD's plugin library for one pinned OTD version, build the plugin shell, and
   test it with OTD on a PC.
4. Profiles and PC-side training (4.5, 4.6).
5. Optional, Deco 640 first: the raw-reading mode (section 5).

See also: [how the tablet works](FIRMWARE_DESIGN.md), [porting to another tablet](porting-guide.md),
[patcher packages](patcher-packages.md).
