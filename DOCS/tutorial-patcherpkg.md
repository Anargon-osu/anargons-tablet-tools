# Tutorial: turning your mod into a patcher package

<img src="../misc/AIdisclosure-most_docs.png" align="right" width="280" alt="Note: This document is mostly AI generated, with only minor corrections done by a human (stated to the AI).">

The first tutorial, [`tutorial-self-modding.md`](tutorial-self-modding.md), added a **Soft Pressure** curve by editing
this tool's source and rebuilding it. That works for you, but anyone else who wants your mod would need your rebuilt
copy of the whole tool. This tutorial turns the same mod into a **patcher package**: a small folder (or ZIP) of C#
source that the official tool compiles and runs. Other people keep the official tool, drop your package next to it,
and get your mod in the normal wizard, with the normal twin check, backup and flashing steps.

Every step below was carried out on a copy of the R45-rc3 tool while this tutorial was written; the screen text and
log lines are copied from that run. It assumes you have read the first tutorial up to part 5.

---

## 0. What a patcher package is, and what it is not

A patcher package is raw C# source, and it is **the patcher for a tablet**: it holds that tablet's patching steps, and
it decides which options the user is offered, adding its own, removing the tool's options its tablet does not have,
and changing others. You, the package's developer, write all of that as your own code; the tool supplies the
building blocks (part 2) and runs the result. This tutorial's package is the simplest kind: one change for the Deco
640, whose firmware the tool already patches, handed to the tool's own wizard (part 3.11 turns it into an option and
fits the other options). A package for a tablet the tool does not patch runs its own steps from start to end
(`patcher-packages.md`, and "Shipping a port" in `porting-guide.md`).

A package is also **code you ask other people to run**. That is the whole point of it, and also its danger, so it is worth
being exact about what the tool does and does not protect against.

**What happens when someone runs a package** (`atf_packages.cs`):

1. When the `patcherpkgs` folder next to the tool holds anything, the tool's first screen becomes "Choose the
   patcher:". Listing the packages only *reads* them; no package code runs at this point.
2. The tool reads the package and refuses it, with the reason, if it holds anything but `package.ini`, C# files under
   `src/` and an optional README: no DLLs, no EXEs, no scripts. In a ZIP it also refuses names that try to escape the
   folder (`..`, a leading `/`, a drive letter `C:`, empty parts like `//`), encrypted or ZIP64 entries, more than 512
   files or more than 16 MB. These are the classic "zip slip" and "zip bomb" checks.
3. It computes a SHA-256 over every file's name, length and bytes. The first time that exact content is chosen, the
   tool shows a full-screen warning and asks the user to type `I know what I am doing.`. It remembers the hash, so the
   same content does not ask again, and **any** change to any file asks again.
4. It compiles `src/` with the C# compiler that ships with Windows' own .NET Framework (never one found on `PATH`, so a
   planted `csc.exe` in some folder is not picked up), with a reference to the tool itself.
5. It loads the compiled code into its own process and calls the package's `Run` method.

**What it is not: a sandbox.** From step 5 on, the package runs with the user's full access to their PC, exactly like
the tool itself. It could delete files, read documents, or flash the tablet without any of the tool's checks. The
guards above make a package *reviewable* (it is plain source, nothing hidden in a binary) and make sure the user runs
*exactly* what they approved (the hash), but nothing checks what the source *does*. Packages are not signed.

This is a deliberate trade-off. The alternative, a real sandbox (a separate process with restricted rights, talking to
the tool through a narrow interface), would make every package far harder to write and would still have to be trusted
with the one thing that matters most: the bytes flashed into the tablet. So the tool chose openness instead: source
only, approval of the exact content, and rules for authors (part 5) that the docs state plainly.

**What follows for you as an author:** write your package so that a careful user can read it in a few minutes. Keep
it small, comment what each part does, and do nothing the user would not expect from its description.

---

## 1. What changes compared with the self-mod

| In the self-mod (tutorial 1) | In the package |
|---|---|
| `Patches.SoftPressure(Image im)` added to `atf_patcher.cs` | a class of your own that implements `IPatch`, in `src/` |
| a `Setting` in `Build.SETTINGS`, with its own category | none in the simple version: choosing the package at the first screen turns the mod on (part 3.4); or `Build.AddSetting`, an option on the settings screens (part 3.11) |
| a line in `Build.BuildOne` with `!o.telemetry && !o.apipTrain` | `Build.AddPatch(...)`: the tool runs every added patch for you, and leaves them out of builds where they do not belong |
| a `--soft-pressure` command-line switch | none: the package hands over to the normal wizard |
| new fingerprints written into `atf_symbols.cs` | `Layout.AddSignatures(...)` called from your package (part 4) |
| `build.cmd` to rebuild the tool | nothing: the tool compiles the package each time it runs it |

The code that writes the firmware, the `Image`, `Fw.A` and `Asm` calls, stays the same.

---

## 2. What the tool offers packages

All of them are public members of the tool (namespace `AnargonsTabletTools`), so a package can call them directly.

### `IPatch` (`atf_build.cs`)

```csharp
public interface IPatch
{
    string Name();                 // shown in the build log and the build options text
    void Apply(Image im);          // check with im.Expect, write with im.Wr / im.Branch, add code with Asm in the cave
}
```

An *interface* in C# is a contract: any class that has these two methods can be handed to the tool as an `IPatch`.
The tool does not need to know your class; it only calls `Name()` and `Apply(im)`.

### `Build.AddPatch(IPatch p)` and `Build.ClearPatches()`

`AddPatch` puts your patch on a list. Every firmware the tool builds afterwards, in the same run, applies the list:

- **After all of the tool's own changes**, so your code sees the firmware as the chosen settings left it, and can use
  the cave space after the tool's own code.
- **In a transaction of its own**, named `Package: <your Name()>`. If your `Apply` finds a firmware location missing,
  the transaction is rolled back and the build is refused with the reason, as for any of the tool's own changes. A
  failed `Expect` stops the build too. Nothing half-patched is ever written out.
- **Before the undo record**, so the record also holds the stock bytes your patch replaced. A build with your package
  can be set back to stock like any other build (`FIRMWARE_DESIGN.md` 14.3).
- **Never in the tool's measuring builds**: the calibration measurement build, the ALPI training build and the
  return-to-stock build leave every package patch out, and the build log says so in one line:
  `package modifications: left out of this build (measurement, training or return-to-stock)`. Those builds are the
  tool's instruments; the measurement build even uses the same two send calls as Soft Pressure.
- **In the build options text**, as `PACKAGE=<name>` (spaces become `-`), so anyone reading a dump of the tablet can
  see that a package was involved.

`ClearPatches` empties the list, and undoes the option changes below. Call it when your package is done (part 3.4
shows where).

### Options: `Build.AddSetting`, `Build.HideSetting`, `Build.Option`

The options the wizard offers are the tool's settings list, `Build.SETTINGS`. A package fits that list to its tablet:

- `Build.AddSetting(Setting s, IPatch p)` adds an option of yours. The user sees it under "Package options" at the
  end of step 6 (Extra QOL Patches) and switches it like any other; your patch runs only in builds where it is on.
- `Build.HideSetting(key, why)` removes one of the tool's options that your tablet does not have. It is not shown, and
  every build treats it as off.
- `Build.Option(key)` gives you one of the tool's options to change: its default, its name or its text.

The first of these calls makes the package its own copy of the list, and `ClearPatches` puts the tool's list back, so
the built-in tool is unchanged once your package returns. Part 3.11 uses all three.

### Inside `Apply`: `Build.IsOn(key)` and `Build.BuildingPreset()`

While a build runs your patch, `Build.IsOn(key)` says whether an option is on in that build, and
`Build.BuildingPreset()` gives its preset (`.key` is the rate, for example `1000`; `.name` is `ultra`). A patch can
use them to adapt, for example to leave out a step that another option already covers.

### `Layout.AddSignatures(string[] lines)` (`atf_layout.cs`)

Adds firmware fingerprints for locations the tool does not know yet. Only needed when your package changes a place
in the firmware that the tool itself never touches; part 4 covers it.

### `Cli.Run(new string[] { "--wizard" })`

Starts the tool's normal wizard, exactly as if the user had picked the firmware mod at the first screen: choosing the
input file, settings, building, the twin check, the backup and the flash with xppen-tools' own confirmation. Handing
over to the wizard is the safest way to build and flash, because your package then never has to touch the flashing
code at all.

---

## 3. Step by step

### 3.1 The folder

```text
SoftPressure\
    package.ini
    README.md
    src\
        SoftPressure.cs
```

Any number of `.cs` files can go under `src\`, in sub-folders too. Nothing else is allowed besides `package.ini` and
the README (`README.md` or `README.txt`).

### 3.2 `package.ini`

```ini
format=1
name=Soft Pressure
entry=SoftPressure.Menu
revision=1
```

- `format=1`: the only format this tool reads.
- `name`: what the first screen shows; 1-60 plain ASCII characters, and not the tool's own name.
- `entry`: the full name (namespace, dot, class) of the class with the `Run` method.
- `revision`: optional, shown next to the name. Raise it with every change you publish, so users can tell versions
  apart.

### 3.3 The patch, as an `IPatch`

`src\SoftPressure.cs`, first part. Compare it with tutorial 1, part 3: the body of `Apply` is the same code, moved
into a class of its own.

```csharp
using AnargonsTabletTools;

namespace SoftPressure
{
    // Soft Pressure: light presses report less pressure. Every pen report passes through this code just before the
    // firmware sends it; pressure = (p x p + 16383) / 16384.
    public class Curve : IPatch
    {
        const int REPORT = 0x200005F0;                // the pen report in RAM (the tool's Sched.REPORT)

        public string Name() { return "Soft Pressure"; }

        public void Apply(Image im)
        {
            int send = Fw.A(0x08009CB0);              // the firmware's "send this report over USB" function
            int[] sites = new int[] { Fw.A(0x08004B32), Fw.A(0x08004B66) };   // the two calls that send a pen report

            Asm a = new Asm(im.BeginAlloc());         // our code goes into the cave
            a.PushW(new string[] { "r0", "r1", "r2", "r3" }, true);    // keep the send function's arguments and lr
            a.Mov32("r2", REPORT);
            a.Ldrb("r0", "r2", 0); a.CmpI("r0", 7); a.Bcc("ne", "done");   // only standard pen reports (report ID 7)
            a.Ldrh("r0", "r2", 6);                    // r0 = pressure, 0..16383
            a.Muls("r0", "r0");                       // r0 = p x p
            a.Mov32("r1", 16383); a.AddsR("r0", "r0", "r1");
            a.Lsrs("r0", "r0", 14);                   // r0 = (p x p + 16383) / 16384
            a.Strh("r0", "r2", 6);                    // write it back into the report
            a.Label("done");
            a.PopW(new string[] { "r0", "r1", "r2", "r3" }, true);     // the arguments and lr as they were
            a.BWA(send);                              // carry on into the real send function
            int at = im.EndAlloc(a, "soft pressure curve: pressure = (p x p + 16383) / 16384");

            for (int i = 0; i < sites.Length; i++)
            {
                int[] bl = Asm.EncBl(sites[i], send, true);   // what an untouched "bl send" at this site looks like
                im.Branch(sites[i], Image.Hw2(bl[0], bl[1]), at, true, "pen report send -> soft pressure curve");
            }
        }
    }
```

What is different from the self-mod:

- **`using AnargonsTabletTools;`** makes the tool's classes (`Image`, `Fw`, `Asm`, `Build`, `Cli`, `Layout`) usable by
  their short names. The tool's own code is in that namespace; a package is compiled *against* it, not into it.
- **`REPORT` is written out.** The tool's `Sched.REPORT` is public and holds the same address; the package spells it
  out so that a reviewer sees the address without opening the tool's source. Either works.
- **No `!o.telemetry && !o.apipTrain` condition.** The tool handles that for every package (part 2).

### 3.4 The entry point

Second part of the same file:

```csharp
    // what the tool calls after the user chose this package and typed the approval
    public static class Menu
    {
        public static int Run(string[] args)
        {
            Build.AddPatch(new Curve());              // every firmware built from now on gets Soft Pressure
            try { return Cli.Run(new string[] { "--wizard" }); }    // the normal wizard: settings, twin, backup, flash
            finally { Build.ClearPatches(); }         // leave the tool as it was, whatever happened
        }
    }
}
```

- The tool requires exactly `public static int Run(string[] args)`; the number it returns becomes the tool's exit
  code, and `Cli.Run` already returns the right one. `args` is empty today.
- **Why `try`/`finally`.** The list of added patches is shared by the whole process. `finally` runs whether the wizard
  ends normally, the user closes the input, or an error is thrown, so no later build in the same process can pick up
  your patch by accident.
- **Turning it on and off.** In the self-mod, Soft Pressure was a setting. In the package, choosing the package *is*
  the setting: pick **2. Soft Pressure** to build with it, **1** (the built-in tool) to build without it. If your
  package holds several patches, ask in `Run` before handing over, for example with
  `Console.ReadLine()`, and add only the ones the user wants.

### 3.5 The README

Write what the package does, what it changes in the firmware, and what it costs, in plain words. A user deciding
whether to type the approval phrase should find the answer here. The tutorial's package used:

```text
Soft Pressure
=============

Light presses report less pressure; a full press still reports the maximum.
pressure = (p x p + 16383) / 16384. Built with the normal wizard; left out of measurement builds.
```

### 3.6 Check that it compiles

You do not need the tool's source for this, only the official `AnargonsTabletTools.exe`. From the package folder:

```text
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:library /langversion:5 /r:path\to\AnargonsTabletTools.exe /out:%TEMP%\package-check.dll src\*.cs
```

No output means it compiled. `/target:library` makes a DLL instead of a program, `/langversion:5` holds the code to
C# 5 (the language level of Windows' built-in compiler, which the tool uses too: no `$"..."` strings, no `?.`, no
`=>` methods), and `/r:` lets your code see the tool's classes. The DLL is only a check; you never ship it (the tool
would refuse it).

### 3.7 Try it as a folder

Copy the whole `SoftPressure` folder into `patcherpkgs` next to `AnargonsTabletTools.exe` and start the tool. The
folder form is the easiest while you are still changing the code. From the tutorial's run:

```text
  Choose the patcher:
  -------------------
    1.  Anargon's Tablet Tools (built in)

    2.  Soft Pressure (revision 1)
        patcherpkgs\SoftPressure.patcherpkg

    Patcher packages are C# code that this tool compiles and runs. They run with your account's
    full access, so only run packages from people you trust.

    Patcher (Enter = 1): > 2

    PATCHER PACKAGE: Soft Pressure

    This package is C# code. This tool compiles it and runs it with your account's full
    access to this PC. This tool cannot check what it does, and it can flash your tablet
    without this tool's checks.

    Only run packages from people you trust. This is asked again whenever the package
    changes.

    File: patcherpkgs\SoftPressure.patcherpkg
    SHA-256: 1d3bc2c1d013c8ec41d1ace7643841c82144bb31a954f847f8363ebca2020c9f

    Type "I know what I am doing." to run it, or press Enter to go back > I know what I am doing.

    Compiling Soft Pressure with Windows' C# compiler...

  Step 1/6: Stock firmware file
```

(That run used the ZIP form of part 3.9; the folder form shows the folder's name instead.) "Step 1/6" is the normal
wizard: the package has handed over. Because the hash covers every file, the approval is asked again after each edit
while you work on the package; that is the guard working, not a fault.

### 3.8 Build with it

Go through the wizard as usual. The build's `main-<time>.log` now lists the package's changes after the tool's own,
and the options text ends with the package's name (`(old) -> (new)` stands for the two instructions; addresses are
where the tool placed things in this run, an ULTRA build from the February firmware):

```text
   0x0800d3b4: +40 bytes  soft pressure curve: pressure = (p x p + 16383) / 16384
   0x08004b32: (old) -> (new) pen report send -> soft pressure curve
   0x08004b66: (old) -> (new) pen report send -> soft pressure curve
   0x0800d3dc: +729 bytes  undo record (729 bytes): the stock value of every changed byte, to set this build back to stock
   0x0800d6b8: +157 bytes  build options as text (...): ... VOLATILE PACKAGE=Soft-Pressure
```

The console ends with "Digital-twin check passed", as for any build. The package's code is the same as the
self-mod's, and so are its effects: the twin measurements in tutorial 1, part 5.4, apply unchanged (on the tutorial's
copy the package build gave the same pressures, for example 7062 becoming 3044 at 518 kHz).

### 3.9 Make the `.patcherpkg` file

A `.patcherpkg` is an ordinary ZIP with the files **at its top level**: `package.ini` and `src/` must not sit inside
another folder in the ZIP. With PowerShell, from inside the package folder:

```powershell
Compress-Archive -Path package.ini, src, README.md -DestinationPath ..\SoftPressure.zip
Rename-Item ..\SoftPressure.zip SoftPressure.patcherpkg
```

Two details found while writing this tutorial:

- `Compress-Archive` only accepts a name ending in `.zip`, hence the rename. (Given `.patcherpkg` directly, it stops
  with ".patcherpkg is not a supported archive file format".)
- Windows PowerShell 5.1, the one built into Windows, stores `src\SoftPressure.cs` with a backslash, although the ZIP
  format uses `/`. R45-rc3 reads `\` as a folder mark and then applies the same safety checks. **Older versions (R44 to
  R45-rc2) refuse such a ZIP** with "unsafe file path"; for those, ship the folder, or use a ZIP program that writes
  `/`.

Running `Compress-Archive -Path SoftPressure` from the folder *above* instead puts everything under
`softpressure/` inside the ZIP; the selector then skips the package with
"only C# source is accepted, and softpressure/package.ini is not (allowed: package.ini, src/*.cs, README)".

### 3.10 Publish it

- Publish the source, not only the ZIP, so people can read it before running it.
- Publish the **SHA-256** the approval screen showed you. A user compares it with what their screen shows; if they
  match, they are about to run exactly what you published. The hash is the same for the folder and the ZIP form of the
  same files.
- Say which tool version you tested with, and raise `revision` with every change.

### 3.11 Make it an option, and fit the options to your tablet

So far, choosing the package always adds Soft Pressure. A package can instead offer it as an option on the settings
screens, and fit the tool's own options to its tablet. Only `Run` changes; the `Curve` class stays as it is:

```csharp
public static int Run(string[] args)
{
    try
    {
        // a new option: key, name, category, default, file-name tag, "what it does|what it costs"
        Build.AddSetting(new Setting("softp", "Soft Pressure", Build.PKG_CAT, false, "SP",
            "A softer pressure curve: light presses report less pressure, a full press still reports the maximum.|Light lines need a little more pressure."),
            new Curve());
        // an option this tablet does not have (an example only: the Deco 640 does sense tilt)
        Build.HideSetting("tiltskip", "this tablet has no tilt sensing");
        // one of the tool's options, with another default for this tablet
        Build.Option("longrange").dflt = false;
        return Cli.Run(new string[] { "--wizard" });
    }
    finally { Build.ClearPatches(); }       // undoes all three: the built-in tool is unchanged afterwards
}
```

`Build.AddPatch` is gone: `AddSetting` takes the patch together with the option. Because the content changed, the
tool asks for the typed approval again (part 0).

What step 6 (Extra QOL Patches) then shows, from the run made for this tutorial: Long-range Pickup starts OFF, Tilt
Frame Skip is on neither settings screen, and the new option comes last:

```text
     3.  Long-range Pickup                 [ OFF ]
            Uses stronger pulses while looking for the pen, so it is found from higher up.
            Cost: Each long search takes about 0.1 s.
...
  Package options
  ---------------

     7.  Soft Pressure                     [ OFF ]
            A softer pressure curve: light presses report less pressure, a full press still
            reports the maximum.
            Cost: Light lines need a little more pressure.
```

Typing `7` switches it on. With ULTRA chosen, the file is named `[VOLATILE]_ultra-SP.hex` (an option's tag goes into
the file name when it differs from its default), the digital-twin check passes, and the build log shows the curve
and the option in the options text:

```text
   0x0800d350: +40 bytes  soft pressure curve (preset ultra, 12-bit Readings on, Tilt Frame Skip off, Long-range Pickup off)
   0x08004b32: 05f0bdf8 -> 08f00dfc pen report send -> soft pressure curve
   0x08004b66: 05f0a3f8 -> 08f0f3fb pen report send -> soft pressure curve
   ... AnargonSS727_20261112 ULTRA JUMP-REJECTION 12BIT FREQ-HOLD COIL-HOLD TIMER-OFF INTERP STEADY-HOVER REACH VOLATILE PACKAGE-OPTION=SOFTP ...
```

The text in brackets came from the patch itself, which for this run described what it saw with `Build.IsOn` and
`Build.BuildingPreset`:

```csharp
string seen = "preset " + Build.BuildingPreset().name + ", 12-bit Readings " + (Build.IsOn("fullres") ? "on" : "off")
    + ", Tilt Frame Skip " + (Build.IsOn("tiltskip") ? "on" : "off") + ", Long-range Pickup " + (Build.IsOn("longrange") ? "on" : "off");
int at = im.EndAlloc(a, "soft pressure curve (" + seen + ")");
```

A hidden option shows as `n/a` in the build log's settings line. Volatile Changes cannot be hidden, because RAPID and
ULTRA need it; the tool refuses with the reason. The keys of the tool's own options are in `Build.SETTINGS`
(`atf_build.cs`).

---

## 4. When your package needs a new firmware location

Soft Pressure only uses locations the tool already knows (`0x08009CB0`, `0x08004B32`, `0x08004B66`), so it needs no
fingerprints of its own. A package that changes some other instruction must tell the tool how to find that place in
every firmware build, because `Fw.A` only knows the locations listed in the tool's `atf_symbols.cs`
(tutorial 1, part 6, explains why fingerprints instead of fixed addresses). Until it does, the build stops with
"internal: 0x080xxxxx is used as a firmware address but has no signature (run tools/mksig.py)".

You need the tool's source for this step (for `tools/mksig.py`), Python, and both stock dumps
([Anargon-osu/deco640-resources](https://github.com/Anargon-osu/deco640-resources)).

1. Collect the change lines of builds you made with the tool into one file, for example in PowerShell
   `Get-Content export\main-*.log | Set-Content writes.txt`. They tell the generator which bytes the tool rewrites; it
   leaves those out of the fingerprint, so a built image still resolves (the twin finds its hooks in the built image).
2. Run the generator on **your package's** sources only, with both stock dumps and the December code-shift map
   (`FIRMWARE_VERSIONS.md`). The generator expands `src\*.cs` itself, so this works in cmd and PowerShell too:

   ```text
   python -I tools/mksig.py pkg-sigs.cs writes.txt FEB.bin DEC.bin:080041CC-080042A7=-2,080042A8-08005867=-4,0800589C-0800FFFF=12 -- src\*.cs
   ```

3. `pkg-sigs.cs` holds one quoted line per `Fw.A(...)` address in your sources. **Keep only the lines for addresses that
   are not already in the tool's `atf_symbols.cs`**: a line for a known address must match the tool's own exactly, and
   it may not, because the tool's lines were made from the change lines of a whole matrix of builds.
4. Hand the lines to the tool in `Run`, before anything resolves an address (below).
5. Once your package builds, repeat steps 1-3 with that build's log added and replace your lines, so that the bytes
   your own patch rewrites near its location are left out of its fingerprint too.

The call in `Run`:

   ```csharp
   Layout.AddSignatures(new string[] {
       "08004B2E|14|xxxxxx....xxxx.xxx|f155|e2a04a715fa2f530|08004B2A",
   });
   ```

The line above was generated for this tutorial for `0x08004B2E`, an instruction a few bytes before the first send call.
Its fields: the February address; where the location sits inside the fingerprint window; the mask (`x` = byte that
must match, `.` = byte ignored); a 16-bit quick check; the first 64 bits of a SHA-256 of the window; and the address
it was verified at in the December build. Like the tool's own fingerprints, it holds no firmware bytes. On the
tutorial's copy, after this call the tool knew 91 locations instead of 90, and `Fw.A(0x08004B2E)` resolved to
`0x08004B2E` on the February firmware and to `0x08004B2A` on the December one. A second call with the same line is
ignored; a line that disagrees with a known one stops with "a package signature for 08004B2E differs from the one
already known".

---

## 5. Rules for package authors, and why

The tool cannot enforce these, so they are on you.

| Rule | Why |
|---|---|
| Flash only through the tool's steps: hand over to the wizard, or `Cli.Gate`, then `Cli.BeforeFlash`, then `Cli.FlashSteps` | the twin check, the backup and the "same firmware build" check are what stand between a mistake and a tablet that does not start |
| `Expect` before every write | your patch then refuses a firmware it does not understand, instead of corrupting it |
| Never read USB string 200 | it restarts the tablet into update mode (`FIRMWARE_DESIGN.md` 6.6) |
| Never change the counter page `0x08002C00` or string 173 | the flash counter must stay trustworthy (`FIRMWARE_DESIGN.md` 15) |
| No XP-Pen firmware bytes in the package, its README or anything you publish | the firmware is XP-Pen's; describe locations by address and meaning, as the tool does (AGENTS.md rule 15) |
| Do nothing the README does not describe; no network access, no files outside the tool's folders | the user approved *this* description; anything else breaks that trust |
| Keep it small and readable | the approval phrase is only worth something if a user can actually read the code |

---

## 6. When something goes wrong

| What you see | Cause | What to do |
|---|---|---|
| "Skipped patcherpkgs\X: only C# source is accepted, and softpressure/package.ini is not ..." | the files are inside a folder in the ZIP | zip from inside the package folder (3.9) |
| "Skipped ...: unsafe file path src\X.cs" | a PowerShell 5.1 ZIP on R44 to R45-rc2 | ship the folder, or update the tool |
| ".patcherpkg is not a supported archive file format" | `Compress-Archive` with a `.patcherpkg` name | write `.zip`, then rename (3.9) |
| "ZIP compression method N is not supported (use normal ZIP)" | a ZIP program's special compression | re-zip with `Compress-Archive` |
| "Soft Pressure did not compile (C# 5, see the messages above)" | a C# error, or newer C# syntax | run the compile check of 3.6 and fix what it names |
| "its entry X needs public static int Run(string[] args)" | `entry` in `package.ini` does not name the class, or `Run` has another form | `entry` is namespace dot class; `Run` exactly as in 3.4 |
| The build stops: "This firmware does not have all the code "Package: ..." needs" | a location your patch uses was not found in this firmware | a new location needs fingerprints (part 4); or the user's firmware is a build you did not test |
| The build stops: "This firmware does not have the code this tool expects" | an `Expect` in your patch failed | the instruction there is not what you expected: another change (the tool's or another package's) already rewrote it, or the firmware is different |
| The build stops: "internal: 0x080xxxxx is used as a firmware address but has no signature" | your patch uses a location the tool has no fingerprint for | part 4 |
| "a package signature for ... differs from the one already known" | you kept a line for an address the tool already knows | remove that line (part 4, step 3) |

---

## 7. Checklist

| # | Step | Done when |
|---|---|---|
| 1 | The patch is an `IPatch` | `Apply` holds the self-mod's code; every write follows an `Expect` |
| 2 | The entry point | `Run` adds the patch, hands over to the wizard, clears the list in `finally` |
| 3 | Compile check | the `csc` command of 3.6 prints nothing |
| 4 | Folder test | the selector lists it, the approval and compile pass, the wizard starts |
| 5 | Build | the log lists the package's lines and `PACKAGE=...`; "Digital-twin check passed" |
| 6 | Measure | the twin shows the same effect as the self-mod |
| 7 | ZIP | made from inside the folder; the selector lists the `.patcherpkg` |
| 8 | Publish | source, README, SHA-256, tested tool version, `revision` raised |

Back to [the self-modding tutorial](tutorial-self-modding.md) or [how the tablet works](FIRMWARE_DESIGN.md).
