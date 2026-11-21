# misctests: ready-made digital-twin harnesses

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

Small programs for looking at one behaviour of a built firmware image in the digital twin, written while working on a
revision and kept here so the next person (or model) does not have to write them again. They are not part of the
regression suites (`tests/`): nothing runs them automatically and they check nothing by themselves. They print
numbers; you read them.

None of them opens a real tablet. Everything runs in the twin (the gd32emu emulator running the firmware, with a
simulated pen).

## Running one

```
python -I misctests/run.py --list
python -I misctests/run.py NAME ARGS...
```

`run.py` compiles `misctests/NAME.cs` together with every `atf_*.cs` and the emulator (`gd32emu.cs`, found the same
way as the suites find it: `GD32EMU_DIR`, `./gd32emu` or `../gd32emu`), writes `tests/work/misctests/NAME.exe`, and runs
it. It recompiles only when a source is newer than the executable. It sets `ADT_STATE_DIR` to a folder under
`tests/work/misctests/` and `ADT_XPPEN_TOOLS` to a file that does not exist, so a harness can never use this PC's
saved state or reach xppen-tools.

It uses the .NET Framework compiler that comes with Windows (`csc.exe`), so it runs on Windows only for now. The code
itself keeps to what the project's own code uses (C# 5), so it would also build with the .NET SDK.

## The harnesses

| Name | What it shows | Arguments |
|---|---|---|
| `laddertest` | R45-rc4 dynamic Steady Hover: which ladder step (rate) the firmware runs at each pen height, and whether it switches back and forth | `HEX still H1,H2,...` or `HEX sweep MM_PER_S` |
| `steptrace` | every drive step of every frame (pulses and frequency), with the hover stage, at fixed pen heights or with the pen lost | `HEX HEIGHTS MS` |
| `hoverpressure` | the core suite's `fw-hover-pressure` setup frame by frame: pressure shown in the air, the sweep's drive step, Frequency Hold, finished and dropped sweeps | `STOCK KEY HOLDS ACCP [HEIGHT]` |

`HEX` is a built image, for example `rev-output/hw45/rc4/ultra/[VOLATILE]_ultra-APIP.hex` in the work area, or any
`.hex` the tool wrote. `STOCK` is a stock `main.bin` (February or December); `hoverpressure` builds its own image
from it, the way the suite does. Each `.cs` file starts with a longer description of its output.

Examples:

```
python -I misctests/run.py laddertest "path/to/[VOLATILE]_ultra-APIP.hex" still 1,3,5,7,10,14,17,21
python -I misctests/run.py laddertest "path/to/[VOLATILE]_rapid.hex" sweep 5
python -I misctests/run.py steptrace "path/to/[VOLATILE]_ultra-APIP.hex" 1,3,5,lost 300
python -I misctests/run.py hoverpressure path/to/deco01v3-main.bin 800 1 0
```

## Things to know when reading the output

- **Heights are on the twin's scale, not pen-tip millimetres.** The twin's pen model puts the signal at the coil
  inside the pen, and its constants are estimates. On Anargon's tablet a pen tip 3 mm above the surface reads like
  8.2 on this scale. Never quote a twin height as a real one.
- **The ladder step** is RAM `VBX+8` (`Sched.VBX_ADDR + 8`): 0 is the fast stage, the last step is the reach stage
  when the preset has one. The stage byte `VB[0]` is 0 fast, 1 Steady Hover, 2 reach.
- **The level** is `VB+8`: this frame's signal level in the running step's own units (a longer read reads higher), so
  levels of different steps cannot be compared directly.
- A pen that starts 19 mm or more away from a cold start is not picked up in the twin; this was already so in rc3.
- After the pen is lost, the lost-pen search takes turns between two reads (Steady Hover's and the long-range one),
  so `laddertest sweep` prints many step changes at one fixed level there. That is the search, not the ladder.

## Adding one

Put `NAME.cs` here. Use the namespace `AnargonsTabletTools.MiscTests`, give it one static class with
`public static int Main(string[] a)`, and start the file with a comment line `// misctests/NAME.cs - what it shows`
(`--list` prints that line). Keep it ASCII, read RAM addresses from `Sched`'s constants rather than writing numbers,
and never make it open a real tablet. Then add a row to the table above.
