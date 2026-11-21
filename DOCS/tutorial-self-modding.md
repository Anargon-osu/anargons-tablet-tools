# Tutorial: modding the Deco 640 firmware yourself

<img src="../misc/AIdisclosure-most_docs.png" align="right" width="280" alt="Note: This document is mostly AI generated, with only minor corrections done by a human (stated to the AI).">

This tutorial walks through adding your own modification to the Deco 640's firmware with this tool's source code, from
finding the right place in the firmware to flashing the result. The example adds a **softer pressure curve**: light
presses report less pressure, a full press still reports the maximum. Every step was carried out on a copy of the
R45-rc3 source while this tutorial was written, and every number below comes from that run.

It is written for a reader who can program in C, C# or Python but has never written assembly. Read
`FIRMWARE_DESIGN.md` section 0 first (registers, instructions, interrupts, memory-mapped hardware) and section 14 (how
the tool changes firmware); this tutorial builds on both. When you are done, the next tutorial,
[`tutorial-patcherpkg.md`](tutorial-patcherpkg.md), turns the same mod into a patcher package that other people can
run without rebuilding the tool.

---

## 0. Before you start

### What you need

| Item | Why | Where |
|---|---|---|
| Windows | `build.cmd` uses the C# compiler that ships with Windows; nothing to install | - |
| This tool's source and the emulator's source, side by side | the tool builds in the emulator ("the twin") to check every image | [Anargon-osu/anargons-tablet-tools](https://github.com/Anargon-osu/anargons-tablet-tools), [Anargon-osu/GD32EMU](https://github.com/Anargon-osu/GD32EMU) |
| A full dump of your own tablet (`ic902-main.bin`, 64 KB) | the firmware you build from; the tool also keeps backups in `dumps/` | xppen-tools `dump`, or the tool's own backup |
| Python 3 (standard library only) | the helper tools: disassembler, fingerprint generator, test runner | python.org |
| Both stock dumps (February and December builds) | only if your mod needs a **new** firmware location (part 6) | [Anargon-osu/deco640-resources](https://github.com/Anargon-osu/deco640-resources) |

### Safety rules, and why

- **The digital twin decides, not you.** Every image is run in the emulator before the tool offers to flash it: it
  must start, enumerate on USB, find the pen and keep its timing. An image that fails is never flashed. This exists
  because a revision-8 test build that overran its timing stopped a tablet until it was flashed again
  (`FIRMWARE_DESIGN.md` 9.1).
- **Never touch the bootloader pages, the counter page `0x08002C00` or USB string 173, and never read USB string 200**
  (AGENTS.md rules 11 and 15; string 200 restarts the tablet into update mode).
- **Keep a full backup.** The tool takes one before every flash. If something goes wrong anyway, the recovery steps are
  in the [`recovery` folder](https://github.com/Anargon-osu/deco640-resources/tree/HEAD/recovery) of Anargon-osu/deco640-resources, and
  `FIRMWARE_DESIGN.md` section 16 explains the options.
- **One change at a time.** If a build misbehaves, you want to know which change did it.
- **No XP-Pen bytes in anything you publish.** Write what an instruction does and where it is, never copy runs of the
  firmware's bytes into source or documents (AGENTS.md rule 15). The disassembler prints bytes for your own study only.

---

## 1. How the tool changes a firmware, in code

Five pieces of the source do all the work. You will use each of them in the example.

### `Image`: the firmware being built (`atf_patcher.cs`)

An `Image` is the firmware held in memory as a byte array, starting at the firmware's first address, `0x08003800`.
Its methods:

| Method | What it does |
|---|---|
| `im.Rd(address, n)` | read `n` bytes |
| `im.Expect(address, bytes)` | **check** that the bytes at `address` are what the stock firmware has there; if not, stop with "This firmware does not have the code this tool expects". Every change starts with this. |
| `im.Wr(address, bytes, note)` | write bytes and add a line to the build log (`note` explains the change) |
| `im.Branch(site, expected, target, link, note)` | `Expect`, then replace the 4-byte instruction at `site` with a jump (`link` false, `b.w`) or a call (`link` true, `bl`) to `target` |
| `im.BeginAlloc()` / `im.EndAlloc(asm, note)` | place new code in **the cave**, the free flash after the firmware's end (`FIRMWARE_DESIGN.md` section 2) |
| `im.Begin(name)` / `im.Fail(e)` | wrap one modification in a **transaction**: if it cannot find one of its locations, everything it wrote is undone and the build either stops or reports it, never half-applies it |

### `Fw.A`: finding a location in any build (`atf_layout.cs`)

The source writes every firmware location as its address in the **February** stock build, wrapped in `Fw.A(...)`.
At build time `Fw.A(0x08004B32)` looks that location up in the firmware actually being built, by its fingerprint
(`FIRMWARE_DESIGN.md` 14.1), and returns its address there: `0x08004B32` in February, `0x08004B2E` in December. A
location that is not found stops the modification; it is never guessed. A February address with no fingerprint at
all is an "internal error": you forgot part 6.

### `Asm`: writing machine code with method calls (`atf_patcher.cs`)

You never type machine code. An `Asm` object collects instructions through method calls and encodes them; labels are
names you jump to. The methods you need for most mods:

| `Asm` call | Thumb instruction | In C terms |
|---|---|---|
| `a.Movs("r0", 7)` | `movs r0, #7` | `r0 = 7;` (0-255) |
| `a.Mov32("r2", 0x200005F0)` | `movw` + `movt` | `r2 = 0x200005F0;` (any 32-bit value) |
| `a.Ldrb("r0", "r2", 0)` / `a.Ldrh(...)` / `a.Ldr(...)` | `ldrb` / `ldrh` / `ldr` | `r0 = *(uint8_t *)(r2 + 0);` (16-bit, 32-bit) |
| `a.Strb(...)` / `a.Strh("r0", "r2", 6)` / `a.Str(...)` | `strb` / `strh` / `str` | `*(uint16_t *)(r2 + 6) = r0;` |
| `a.AddsR("r0", "r0", "r1")`, `a.SubsR(...)` | `adds`, `subs` | `r0 = r0 + r1;` |
| `a.Muls("r0", "r1")` | `muls` | `r0 = r0 * r1;` |
| `a.Lsrs("r0", "r0", 14)`, `a.Lsls(...)` | `lsrs`, `lsls` | `r0 = r0 >> 14;` |
| `a.CmpI("r0", 7)` then `a.Bcc("ne", "done")` | `cmp`, `bne` | `if (r0 != 7) goto done;` |
| `a.Label("done")` | - | `done:` |
| `a.PushW(regs, true)` / `a.PopW(regs, true)` | `push.w` / `pop.w` | save registers (and `lr`) on the stack, and restore them |
| `a.BWA(address)` / `a.BlA(address)` | `b.w` / `bl` | `goto` / call a firmware address |
| `a.BxLr()` | `bx lr` | `return;` |

The registers `r0`-`r3` carry a function's first four arguments and its result; a function may change them freely.
`r4`-`r11` must be given back unchanged. `lr` holds the return address. The assembler checks ranges (a `movs` value
above 255, a branch too far) and stops with "assembler: ..." rather than produce wrong code.

### `Build.BuildOne`: the list of modifications (`atf_build.cs`)

`BuildOne` applies the modifications in a fixed order, one transaction each, for example:

```csharp
im.Begin("Position Interpolation"); try { if (!On(on, "interp")) Patches.InterpOff(im); } catch (Stop e) { im.Fail(e); }
```

`On(on, "interp")` is true when the user turned that setting on. The settings themselves are the table
`Build.SETTINGS`. After the modifications come the undo record (the stock value of every byte the build changed, so a
later run can set the build back to stock) and a plain-text list of the build's options. Both are automatic: a change
you add is recorded like the built-in ones.

### The twin gate (`Cli.Gate`)

After writing the image, the tool loads it into the emulator and runs the start-up, the USB enumeration, the gain
control, the pen detection and the timing checks. The result is printed as "Digital-twin check passed" or "FAILED".

---

## 2. Find what to change

The example changes the pressure in each pen report. To do that you need to know where the report is and when it is
sent. Three sources, in this order:

1. **The documents.** `FIRMWARE_DESIGN.md` 6.1 gives the report's layout (report ID, status, X, Y, pressure, tilt).
   `CLAUDE.md` (rev 26) says the pen report buffer is at RAM `0x200005F0` and is sent from `0x08004B32` and
   `0x08004B66`, through the function at `0x08009CB0`.
2. **The source.** `atf_sched.cs` already hooks those two sends in the calibration build (search for `tsend`): its
   comments confirm the layout, with the report at `r5` and the pressure read as `Ldrh("r0", "r5", 6)`.
3. **The disassembler, on your own dump** (output for your eyes only; it shows firmware bytes):

   ```text
   python -I tools/dis.py ic902-main.bin 0x08004B2E +12
   ```

   Around the first send it shows, in C terms:

   ```c
   r1 = 0x200005F0;        // ldr r1, [pc, #...]: the report buffer
   r0 = 0x82;              // movs r0, #130: USB endpoint 2, device to PC (the pen endpoint)
   send(r0, r1, ...);      // bl 0x08009CB0
   ```

   The second send, at `0x08004B66`, loads the same buffer and endpoint with a length of 10 bytes. Just before it,
   the firmware clears bytes 6 and 7 (the pressure) when the status byte says the pen is in range but not touching.

So the plan: **just before each send, rewrite the 16-bit pressure at `0x200005F0 + 6`**. Both calls are `bl`
instructions (calls), which makes them ideal hook sites: replace each with a call to our own code, which changes the
pressure and then continues into the real send function as if nothing happened.

**Why pressure is safe to change here:** the report is already finished, nothing in the firmware reads it again, and
the firmware rebuilds it from scratch every frame. Changing it cannot disturb the scan, the hover stages or the
filters.

---

## 3. Write the modification

The curve: `pressure = (p x p + 16383) / 16384`, with `p` from 0 to 16383. At `p = 0` it gives 0; any press above 0
stays above 0, so the "tip touching" status and the pressure never disagree; a full press, 16383, stays 16383; half
pressure, 8192, becomes 4096. Dividing by 16384 is a right shift by 14 bits, which costs one instruction.

Add this to the `Patches` class in `atf_patcher.cs`, next to `BandEdge` (which is built the same way):

```csharp
// Soft Pressure: light presses report less pressure. Every pen report passes through this code just before the
// firmware sends it; pressure = (p x p + 16383) / 16384.
public static void SoftPressure(Image im)
{
    int send = Fw.A(0x08009CB0);                  // the firmware's "send this report over USB" function
    int[] sites = new int[] { Fw.A(0x08004B32), Fw.A(0x08004B66) };   // the two calls that send a pen report

    Asm a = new Asm(im.BeginAlloc());             // our code goes into the cave
    a.PushW(new string[] { "r0", "r1", "r2", "r3" }, true);    // keep the send function's arguments and lr
    a.Mov32("r2", Sched.REPORT);                  // r2 = 0x200005F0, the report
    a.Ldrb("r0", "r2", 0); a.CmpI("r0", 7); a.Bcc("ne", "done");   // only standard pen reports (report ID 7)
    a.Ldrh("r0", "r2", 6);                        // r0 = pressure, 0..16383
    a.Muls("r0", "r0");                           // r0 = p x p
    a.Mov32("r1", 16383); a.AddsR("r0", "r0", "r1");
    a.Lsrs("r0", "r0", 14);                       // r0 = (p x p + 16383) / 16384
    a.Strh("r0", "r2", 6);                        // write it back into the report
    a.Label("done");
    a.PopW(new string[] { "r0", "r1", "r2", "r3" }, true);     // the arguments and lr as they were
    a.BWA(send);                                  // carry on into the real send function
    int at = im.EndAlloc(a, "soft pressure curve: pressure = (p x p + 16383) / 16384");

    for (int i = 0; i < sites.Length; i++)
    {
        int[] bl = Asm.EncBl(sites[i], send, true);   // what an untouched "bl send" at this site looks like
        im.Branch(sites[i], Image.Hw2(bl[0], bl[1]), at, true, "pen report send -> soft pressure curve");
    }
}
```

The same code in C, to check the logic:

```c
void soft_pressure_hook(int r0, int r1, int r2, int r3)    /* called instead of send(...) */
{
    uint8_t *report = (uint8_t *)0x200005F0;
    if (report[0] == 7) {
        uint32_t p = *(uint16_t *)(report + 6);
        *(uint16_t *)(report + 6) = (p * p + 16383) >> 14;
    }
    send(r0, r1, r2, r3);       /* b.w: the real send returns straight to the firmware's caller */
}
```

**The details that make it correct:**

- **Why save `r0`-`r3`.** The firmware loaded the send function's arguments into `r0`-`r2` before the call; our code
  uses those registers, so it saves them first and restores them before passing control on.
- **Why save `lr`, and why `b.w` at the end.** The `bl` at the hook site put the return address (the instruction after
  the site) into `lr`. Our code keeps it on the stack, puts it back, and then *jumps* (not calls) into the real send
  function. When send returns, it returns straight to the firmware's caller, exactly as before the change. This is a
  *tail call*.
- **Why `Expect` with a computed instruction.** `Asm.EncBl(site, send, true)` encodes "call `send` from `site`", which is
  exactly what an untouched firmware has there. `im.Branch` checks it before writing, so the mod refuses a firmware
  whose code at that place is different, instead of corrupting it. No firmware bytes are written into the source: the
  expected instruction is computed from two addresses.
- **Why the report-ID check.** Only report ID 7 has its pressure at byte 6. Any other report passes unchanged.
- **The cost.** 15 instructions, well under a microsecond at 72 MHz, once per report.

---

## 4. Make it a setting

A setting appears in one of the wizard's two settings screens and in the build's options text. The screens are
**Noise-Related Patches** (categories `apip`, `scan`, `corr`, `latency`, `noise`, `exp`, in that order, with Multi-coil Read last;
inside a category the settings follow `Cli.SCREEN_ORDER`, then the list order) and
**Extra QOL Patches** (`hover`, `storage`); `Cli.NOISE_CATS` and `Cli.QOL_CATS` list them. A pressure curve is not
about noise, so it gets a category of its own on the QOL screen. Four edits.

**4.1 The setting** (`atf_build.cs`, in `Build.SETTINGS`, for example before `"volatile"`):

```csharp
new Setting("softp", "Soft Pressure", "pen", false, "SP", "Light presses report less pressure; a full press still reports the maximum.|Changes how pressure feels in every program."),
```

The fields are: the key (used in code), the name (shown), the category, the default (`false`: off), a short tag, and
the description: what it does, then `|`, then what it costs.

**4.1b The category** (`atf_build.cs`, `Build.CATEGORIES`, pairs of key and heading; and `atf_cli.cs`, `QOL_CATS`):

```csharp
"hover", "Hover and pickup range", "pen", "Pen feel", "exp", "Experimental hardware", ...     // CATEGORIES
QOL_CATS = new string[] { "hover", "pen", "storage" };                                        // the QOL screen
```

The first attempt for this tutorial used the existing `exp` category instead. The regression suites caught it: the
setting then showed after Multi-coil Read on the Noise-Related Patches screen, against the order Anargon chose, and
the check "Noise-Related Patches: ALPI first, then noise tuning, position filters, Multi-coil Read last" failed.

**4.2 The transaction** (`atf_build.cs`, in `BuildOne`, just before the line `im.Begin("Firmware version string")`):

```csharp
im.Begin("Soft Pressure"); try { if (On(on, "softp") && !o.telemetry && !o.apipTrain) Patches.SoftPressure(im); } catch (Stop e) { im.Fail(e); }
```

The two extra conditions leave it out of the calibration measurement build and the ALPI training build. Those builds
are the tool's measuring instruments; the measurement build even uses the same two send calls for its own readings, so
`Expect` would stop it.

**4.3 A command-line switch** (`atf_cli.cs`, in `Cli.Run`), following how `--multi-coil` is handled:

```csharp
bool softp = false;                                               // with the other option variables
else if (P.Eq(a, "--soft-pressure")) softp = true;                // in the argument loop
st[Build.SetIndex("softp")] = softp;                              // where the settings array st is filled
```

---

## 5. Build, check and measure

**5.1 Compile:** run `build.cmd`. It compiles the tool and runs the twin's self-test.

**5.2 Build an image** (ULTRA, with the new switch):

```text
AnargonsTabletTools.exe import\ic902-main.bin export --rate ultra --soft-pressure
```

**5.3 Read the log.** The build writes `main-<time>.log` next to the image. Its change list now holds your lines (the
addresses are where the tool placed things in this run):

```text
   0x0800d28c: +40 bytes  soft pressure curve: pressure = (p x p + 16383) / 16384
   0x08004b32: (old) -> (new) pen report send -> soft pressure curve
   0x08004b66: (old) -> (new) pen report send -> soft pressure curve
```

and the options text ends in `SOFTP`. The console ends with **"Digital-twin check passed"**.

**5.4 Measure what it does, in the twin.** The `twin` command runs an image with a simulated pen. `--pen-f0` sets the
pen's resonant frequency, which is what pressure is computed from (`FIRMWARE_DESIGN.md` 8.3):

```text
AnargonsTabletTools.exe twin export\[VOLATILE]_ultra.hex --run 150 --pen-f0 518000 --pen-height 0
```

Run it on a build without the switch and on one with it. On the copy used for this tutorial *(twin, ULTRA, February)*:

| Pen frequency | Pressure without | Pressure with Soft Pressure | (p x p + 16383) / 16384 |
|---|---|---|---|
| 514 kHz | 2857 | 499 | 499 |
| 518 kHz | 7062 | 3044 | 3044 |
| 522 kHz | 11945 | 8709 | 8709 |
| 525 kHz | 16383 | 16383 | 16383 |

The firmware does exactly what the C version says.

**5.5 Run the regression suites** with your February dump, so you know nothing else changed:

```text
python -I tests/run-regressions.py --stock import\ic902-main.bin
```

On the tutorial's copy all three suites passed with the new setting in place: core 215, host 112, calibration 200
checks, in 68 s. When something fails, the runner names the group and prints its output, as it did for the first
placement of the setting (4.1b).

---

## 6. If your mod needs a new location

The example only used locations the tool already knows (`0x08009CB0`, `0x08004B32`, `0x08004B66`). A mod that changes
a different instruction needs a **new fingerprint**, so that `Fw.A` can find that place in every firmware build. Until
it has one, the build stops with "internal: 0x080xxxxx is used as a firmware address but has no signature (run
tools/mksig.py)".

1. Write the February address in your code as `Fw.A(0x080xxxxx)`.
2. Collect the change lines of builds you made into one file, for example in PowerShell
   `Get-Content export\main-*.log | Set-Content writes.txt`. They tell the generator which bytes the tool rewrites; it
   leaves those out of the fingerprint, so a built image still resolves (the twin finds its hooks in the built image).
3. Generate fingerprints for the file that holds your mod, **into a separate file**, with both stock dumps. The text
   after the December dump is the map of how its code moved (`FIRMWARE_VERSIONS.md`); the generator only uses it to
   check its own result. The generator expands wildcards such as `atf_*.cs` itself, so this works in cmd and PowerShell:

   ```text
   python -I tools/mksig.py my-sigs.cs writes.txt FEB.bin DEC.bin:080041CC-080042A7=-2,080042A8-08005867=-4,0800589C-0800FFFF=12 -- atf_patcher.cs
   ```

4. It prints how many signatures it wrote. It **fails loudly** for a location it cannot make unique in both builds:
   pick a nearby instruction inside the same function, or make the window longer.
5. Copy **only the line for your new address** (its first field) from `my-sigs.cs` into `atf_symbols.cs`, and leave
   every other line exactly as it is. The tool's own lines were made from the change lines of a whole matrix of builds;
   regenerating them from your few logs would leave fewer bytes out, and some built images might stop resolving.
6. Once your mod builds, repeat steps 2-5 with that build's log added, so the bytes your mod rewrites near its own
   location are left out of its fingerprint too.

What the generator does, briefly: for each location it takes the shortest window of bytes around it (at least 12 bytes
it can rely on) that occurs exactly once in each build, at the right address. It masks out bytes that change when code
moves (call and jump offsets, offsets to stored constants, pointers into flash) and bytes the tool rewrites, and stores
only the mask, a 16-bit quick check and a 64-bit hash. The firmware bytes cannot be recovered from it, which is why
`atf_symbols.cs` can be published.

---

## 7. Flash it

Use the tool's own flashing steps; they take a fresh backup, check that your tablet runs the same firmware build the
image was made from, and run xppen-tools, which asks you to type its own confirmation:

- **Wizard:** run `AnargonsTabletTools.exe` with no arguments, choose the firmware mod, turn **Soft Pressure** on in
  Extra QOL Patches (under "Pen feel"), build, and accept the flash.
- **Command line:** add `--flash` to the build command of 5.2.

Afterwards, draw a few light and firm strokes in a program that shows pressure. To undo it, build and flash without the
setting, or flash stock firmware.

---

## 8. Checklist

| # | Step | Done when |
|---|---|---|
| 1 | Find the place | you can say in C terms what the instructions there do, and where the data lives |
| 2 | Write the change | every write is preceded by `Expect`; registers you use are saved; control returns where it would have |
| 3 | Make it a setting | it shows in the wizard and in the options text |
| 4 | Build | the log lists your changes; "Digital-twin check passed" |
| 5 | Measure | the twin shows the effect you expected, with numbers |
| 6 | Regressions | all three suites pass with your dump |
| 7 | Flash | through the tool, with a backup; one change per test |

Next: [turn this mod into a patcher package](tutorial-patcherpkg.md).
