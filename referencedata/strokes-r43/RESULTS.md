# R43 moving tests: coil positions on Anargon's tablet (2026-11-09)

<img src="../../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

Line-recorder builds (`rev-output/hw42b/`), 75 s ruler strokes, pen touching. ULTRA and RAPID: 2 side-to-side + 2
up-and-down passes; FAST: 2 side-to-side + 1 up-and-down (pass 2 skipped: FAST already agreed to 0.24 mm). QUICK
skipped (multi-coil is not worth it below 500 Hz). Afterwards the tablet got calibrated plain R42 RAPID back
(`hw42/plain/[VOLATILE]rapid-ST.hex`, CRC 0xFA2E). 19 updates in the whole R43 session, all CRC and vector verified.

Files: `<speed>-h*/v*-75s.csv` (hwdual log), `coils-<speed>.txt` (per speed), `coils-compare.txt` (all speeds).

## Method

A coil's centre is where its two neighbours read the same (b = d). Per line copy: centre coil k and the floor-free
offset (b - d) / (2(2c - b - d)), paired with the nearest touching position report; per coil a straight-line fit of
reported mm against that offset, intercept = coil centre. X from the up-and-down strokes (X barely moves), Y from the
side-to-side strokes, and the other way round as a lag check (they agree to 0.05 mm on most coils).

## Result: a standard 0.2 inch grid

Reported mm, X = 0 at the left edge, Y = 0 at the top.

| Axis | Coils | Pitch | Centre of coil k (inner coils) | Largest difference between speeds |
|---|---|---|---|---|
| X | 32, coil 1 at the right | 5.080 mm | 163.53 - 5.080 k | 0.21 mm (coil 18) |
| Y | 19, coil 1 at the bottom | 5.083 mm | 96.42 - 5.083 k | 0.24 mm (coil 15) |

Agreed centres (median of ULTRA, RAPID, FAST), mm:

- X coils 1-32: 160.50 (edge), 153.62, 148.24, 143.22, 138.14, 133.04, 127.95, 122.88, 117.83, 112.72, 107.65, 102.59,
  97.48, 92.41, 87.32, 82.21, 77.18, 72.04, 67.01, 61.90, 56.87, 51.76, 46.67, 41.59, 36.54, 31.44, 26.37, 21.28,
  16.20, 11.04, 5.68, -0.18 (edge)
- Y coils 1-19: 89.99 (edge), 86.67, 81.23, 76.09, 70.95, 65.91, 60.83, 55.74, 50.66, 45.60, 40.52, 35.53, 30.37,
  25.30, 20.22, 15.14, 9.85, 4.36, 0.00 (edge)

- Inner coils (X 3-30, Y 3-17) sit within about 0.1 mm of the straight grid.
- The second coil from each edge is pulled 0.3-0.6 mm off the grid (X 2 and 31, Y 2 and 18); the outermost coil on
  each side is at or past the edge of the reported area, where the reported position is clamped (X 0-160, Y 0-90),
  so its value here is the clamp, not the coil. On the grid: X coil 1 at 158.45, X coil 32 at 0.97, Y coil 1 at
  91.34 (past the bottom), Y coil 19 at -0.16.
- Not dependent on speed: ULTRA, RAPID and FAST give the same grid. The geometry can be a fixed table for multi-coil
  work and a shorter calibration.

## Other findings

- ULTRA sent 2 wild reports in 300 s of strokes, off the drawing area (181.5, 14.1 mm touching; 281.6, 217.8 mm).
- `experiments/linefit.py` assumed 4.97 / 4.74 mm per coil; corrected to 5.08 / 5.08. Coil-unit numbers were right;
  its mm numbers (also in `../still/RESULTS.md`) were 2 % (X) and 7 % (Y) low.
