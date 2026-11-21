# Reference data: pen movement recordings

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

Recordings of a pen moving on Anargon's Deco 640 (GD32F350 controller, the February 2025 stock firmware build plus
this tool's builds), made while developing R43 - R45. They are kept here so anyone can re-run the analyses, test a
new idea offline against real hardware data, or compare their own tablet with this one. Nothing in the program reads
these files; they are reference material only. Recorded by Anargon, organised by Claude (Anthropic).

Only recordings where the pen **moves** are here (ruler lines, freehand drags, strokes, the ALPI training). Still-pen
recordings (pen on a stand, hover heights) are not.

Size: about 95 MB, almost all plain text (CSV-like). Total area: 160 x 90 mm, 100 report units per mm in the normal
pen report, 200 per mm in report mode 4 (used by most files below).

## Folders

| Folder | What | When | Read it with |
|---|---|---|---|
| `ruler/` | pen slid along a 15 cm ruler, many passes each way (true position error) | 2026-11-13, 2026-11-16 | `tools/ruler.py`, `tools/ruler6.py` |
| `drags/` | 6 freehand drags at 45 degrees, with the coil line readings of every frame | 2026-11-13 | `tools/drivestudy.py` (`load()`) |
| `strokes-r43/` | ruler strokes side to side and up and down at FAST, RAPID, ULTRA, with line readings (used to locate the coils) | 2026-11-09 | `tools/linedecode.py` |
| `alpi-training/` | the ALPI training at ULTRA: the pen moved over 40 squares at three heights | 2026-11-11 | `atf_apip.cs` (`ApipData`) or any CSV reader |
| `tools/` | the Python readers and analyses named above (copies from the work area; Python 3, nothing to install), and `lagsim.cs`, the offline replay of ALPI and Coil Linearization behind rc5 (2) (C#, built with the program's sources; build line in its header) | | |

## Coordinates and the coil grid

- Reported millimetres: X = 0 at the left edge, Y = 0 at the top (as the cursor moves).
- Coil centres (R43, measured from `strokes-r43/`): X coil k at 163.53 - 5.080 k mm (coil 1 at the right, 32 coils),
  Y coil k at 96.42 - 5.083 k mm (coil 1 at the bottom, 19 coils). The pitch is 0.2 inch. A position's "coil phase"
  is how far it is between two coil centres (0 = on a centre, 0.5 = halfway, where the firmware switches its window).
- The outermost coil on each side is at or past the area's edge, so positions within about 5 mm of an edge behave
  differently (the recordings show jumps there); the analyses leave the ends of each line out.

## ruler/ - ruler lines (true position error)

A ruler is straight, so how far each report is from a straight line through all passes, measured sideways, is the
tablet's position error (plus the hand: pen angle, pressure, the ruler moving a little between passes; each pass's
own offset is taken off). Recorded through the **normal firmware's cursor reports**, read-only, with
`vendorlog.exe SECONDS --log FILE` (the tablet's vendor HID collection; the driver keeps the tablet in report mode 4).

File format (`*.log`): a header line `t_ms,collection,bytes`, then one line per report: the PC's time in ms, the HID
collection it came from, and the report's bytes in hex. Each report is 12 bytes, report ID 2:

| Byte | Meaning |
|---|---|
| 0 | 0x02 (report ID) |
| 1 | status: 0xA0 pen in range, 0xA1 tip down (bit 0), 0xC0 / 0xF0 pen leaving / out of range |
| 2-3 | X, little endian, 1/200 mm |
| 4-5 | Y, little endian, 1/200 mm |
| 6-7 | pressure, little endian, 0 - 16383 |
| 8-11 | tilt and unused |

The recordings (build = the firmware on the tablet; CRC = xppen-tools' check of the flashed image; speeds are the
median pass speed):

| File | Firmware | Line | Notes |
|---|---|---|---|
| `ruler45.log` | R45-rc4 ULTRA + ALPI, Switch Match on | 32 deg, through (68, 41) mm | 2026-11-13, 90 s, 13 strokes at about 20 mm/s, XP-Pen driver; another ruler spot (a mover under the tablet was removed since), so compare it only with itself |
| `i1-control.log` | R45-rc5 ULTRA + ALPI (Window Scale, Switch Match), CRC 0x18B1 | 33 deg, through (67, 40) mm | 2026-11-16 round 1, 90 s, 9 passes, 24 mm/s |
| `i2-cl.log` | rc5 + Coil Linearization, CRC 0xAF0F | same line | 17 passes, 42 mm/s |
| `i3-pax.log` | rc5 with Pen-Anchored Crossfade instead of Window Scale, CRC 0xF89E | same line | 20 passes, 73 mm/s |
| `i4-mg.log` | rc5 with Midpoint Gaussian instead of Window Scale, CRC 0x34C8 | same line | 23 passes, 56 mm/s |
| `i5-xa.log` | rc5 + Cross-Axis Table, CRC 0x4917 | same line | 39 passes, 102 mm/s |
| `i6-ab.log` | rc5 with Alternating Blend instead of Switch Match, CRC 0xB38F | same line | 49 passes, 154 mm/s |
| `r2-cl-diag2.log` | rc5 + Coil Linearization | -34 deg (the other diagonal), through (89, 46) mm | 2026-11-16 round 2, 90 s, about 50-120 mm/s |
| `r2-ctl-diag2.log` | rc5 control | same line | 90 s, about 90-140 mm/s |
| `r2-ctl-shallow.log` | rc5 control | -10 deg, through (78, 39) mm | 90 s, about 100 mm/s |
| `r2-cl-shallow.log` | rc5 + Coil Linearization | same line | 40 s, about 100 mm/s |

Pen-Anchored Crossfade, Midpoint Gaussian, Cross-Axis Table and Alternating Blend were test builds. After this test
they failed and are not in the program; only Coil Linearization is kept (part of Misc. Optimizations).

All 2026-11-16 recordings ran with OpenTabletDriver (the XP-Pen driver closed); the ruler was held by clips and may
have moved slightly between passes. The tablet's surface has worn spots: the pen sometimes hops over them, which shows
as a jump at the same spot in every recording and as the tip lifting for about 12 ms in the middle of a pass. The
round 1 builds were each recorded once, in order, and the passes got faster each time, so compare them on the
coil-locked numbers below, which barely depend on speed.

The analyses are next to the logs:

- `ideas-analysis.txt`: round 1, all six builds, one common 134 mm stretch of the line (`tools/ruler6.py`).
- `ideas-analysis-cl-vs-control.txt`: round 1, rc5 control against Coil Linearization, passes of 15-52 mm/s only.
- `r2-analysis-diag2.txt`, `r2-analysis-shallow.txt`: round 2.

What they found (coil-locked error = the part that repeats with each axis's coils, folded by coil phase; X and Y as
the error of that axis, estimated from the sideways error; steps = jumps of more than 0.3 mm that stay; round 1's
control and Coil Linearization rows use only passes of 15-52 mm/s, so their speeds match):

| Line | Build | X coil-locked | Y coil-locked | Steps |
|---|---|---|---|---|
| round 1 (33 deg) | rc5 control | 0.26 mm | 0.14 mm | 0.1 / s |
| round 1 (33 deg) | Coil Linearization | 0.15 mm | 0.06 mm | 0.1 / s |
| round 1 | Pen-Anchored | 0.42 mm | 0.32 mm | 1.1 / s |
| round 1 | Midpoint Gaussian | 0.57 mm | 0.59 mm | 11.9 / s |
| round 1 | Cross-Axis | 0.20 mm | 0.15 mm | 0.4 / s |
| round 1 | Alternating Blend | 0.51 mm | 0.34 mm | 15.0 / s |
| other diagonal (-34 deg) | rc5 control | 0.20 mm | 0.14 mm | 0.8 / s |
| other diagonal (-34 deg) | Coil Linearization | 0.08 mm | 0.05 mm | 0.5 / s |
| shallow (-10 deg) | rc5 control | (not resolved) | 0.08 mm | 0.5 / s |
| shallow (-10 deg) | Coil Linearization | (not resolved) | 0.065 mm | 0.2 / s |

Coil Linearization's table was learned from `ruler45.log`, a line close to round 1's; the other two lines show it
also works where it was not learned. On the shallow line the sideways direction is almost pure Y, so X is not
resolved there. The rest of the repeatable error (0.05 - 0.17 mm sd along the line) does not follow one axis's coils
(worn spots on the surface, the ruler, the hand), and Coil Linearization does not change it.

Run the analyses (from this folder):

    python tools/ruler.py ruler/i1-control.log --trim
    python tools/ruler6.py ruler
    python tools/ruler6.py ruler --set "r2-ctl-diag2:rc5 control,r2-cl-diag2:Coil Linearization" --need 2

## drags/ - freehand drags with line readings

`drag45.csv`: 6 freehand drags at about 45 degrees, all the same way, at ULTRA, recorded by a silent drag-recorder
build (it sends no cursor; the PC read its RAM ring instead). Columns `seq,x,y,lvl,aux,sig,fq`; every frame is 3
consecutive rows (`seq`, `seq + 1`, `seq + 2`):

- row 1 (`aux & 0x3000 == 0`): the stock position x, y, signal level, `aux` = pressure / 4 (bit 14 = mode 4 units),
  `sig` = the scan's window centres (`cx | cy << 6`);
- row 2 (`aux & 0x2000`): the five X line sums around the centre, in x, y, lvl, sig, fq;
- row 3 (`aux & 0x1000`): the five Y line sums.

`tools/drivestudy.py` has `load()`, which returns one dictionary per frame. These drags showed the jump at each coil
window switch (X 0.24 mm median) that Window Scale and Switch Match were built for (history Part 16.3 and 16.8).

## strokes-r43/ - strokes for the coil positions

`<speed>-h1/h2/v1/v2-75s.csv`: 75 s of ruler strokes with the pen touching, side to side (h) and up and down (v), at
FAST, RAPID and ULTRA, recorded with the R42 line-recorder builds through the normal pen collection
(`hwdual.exe log`). Columns `time_us,b0..b9`: each row is one raw 10-byte pen report (ID 7). Between normal reports
the recorder sends pages 0xF0 - 0xF4 that carry the line sums of a frame (X and Y lines centre-2..centre+2, centres,
positions, pressure, AGC, stage); `tools/linedecode.py` decodes them. `RESULTS.md` and `coils-*.txt` are the coil
centres found from them (the grid above).

## alpi-training/ - the ALPI training

`train.csv`: Anargon's ALPI training at ULTRA (602479 readings in 177 s): the pen moved over 40 squares at each of
three heights (touching, low hover, high hover); `progress.txt` is the coverage screen at the end. `hold.csv` (3597
readings) is the short still hold recorded with it. Columns `seq,x,y,lvl,aux`: position in 1/200 mm, the detection
signal level, and `aux` = pressure / 4 (bits 0-11), hover stage (bits 12-13), mode 4 units (bit 14). This is the
training built into the program as ALPI's fallback (`Apip.Fallback`).
