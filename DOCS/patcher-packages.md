# Patcher packages

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

Since R44 the tool can run other people's patchers. A patcher package (`.patcherpkg`) is raw C# source code: nothing
in it is compiled in advance, so anyone can read what it does before running it. The tool compiles it with the C#
compiler that comes with Windows and runs it inside this tool.

**A package is the patcher for a tablet.** It holds that tablet's patching steps, and it decides which options the
user is offered: it can add options of its own, remove the tool's options its tablet does not have, and change
others. The package's developer writes all of that as their own code; the tool supplies the building blocks below and
runs the result.

There are two kinds:

| Kind | What it does | What it uses |
|---|---|---|
| A patcher for a tablet whose firmware this tool already patches (the Deco 640's builds) | adds its own firmware changes and options to the tool's wizard, so its builds still go through the settings screens, the twin check, the backup and the flashing steps | `IPatch`, `Build.AddPatch`, `Build.AddSetting`, `Build.HideSetting`, `Build.Option`, `Layout.AddSignatures`, `Cli.Run` (below) |
| A patcher for another tablet | runs its own steps from start to end: reading the dump, offering its options, patching, writing the file | its own code. The twin, backup and flashing steps are built for the Deco 640 (its chip, board model and USB IDs) and do not check another tablet's image, so the package brings its own checks. It can reuse the screen helpers (`Screen`), the file and hex helpers (`P`, `Build.WriteHex`) and, for a Cortex-M tablet, the Thumb-2 assembler (`Asm`). See `porting-guide.md`, "Shipping a port" |

## Folders

The tool makes these folders next to `AnargonsTabletTools.exe` when it starts:

| Folder | What goes there |
|---|---|
| `patcherpkgs` | Patcher packages |
| `import` | Your stock firmware file (`ic902-main.bin` from xppen-tools, or `deco01v3-main.bin`) |
| `export` | Built firmware files and their logs (Step 2's default) |
| `dumps` | Every tablet backup this tool makes |

Step 1 looks first for the latest tablet backup (`last-dump/ic902-main.bin`), then for `ic902-main.bin`, then for
`deco01v3-main.bin`. For each, it looks in `import` first, then the current folder, the output folder and the
program's folder. A build of this tool is set back to the stock firmware it was made from; one that cannot be
(no usable undo record and no stock copy near it) is skipped. You can also type a file or a folder.

## The selector

When `patcherpkgs` holds at least one package, the first screen is "Choose the patcher:". 1 is this tool. When the
folder is empty, the screen is not shown.

## What a package looks like

A `.patcherpkg` file (an ordinary ZIP) or a folder, with:

```text
package.ini
src/*.cs          (any depth under src/)
README.txt or README.md (optional)
```

`package.ini`:

```ini
format=1
name=My Patcher
entry=MyPatcher.Menu
revision=3
```

- `name`: 1-60 printable ASCII characters. It cannot be this tool's own name.
- `entry`: the class to run. It needs `public static int Run(string[] args)`. The number it returns is the tool's exit
  code.
- `revision`: optional, shown next to the name.

**Only C# source is accepted.** A package with anything else (a `.dll`, an `.exe`, a script, an unsafe path, an
encrypted or ZIP64 file) is skipped, and the selector says why. Limits: 512 files, 16 MB.

## How it runs

1. The first time a package's exact content runs, the tool shows a full-screen warning and asks you to type
   `I know what I am doing.` It remembers the content's SHA-256 (the same for the ZIP and the folder form). Any change
   asks again.
2. The tool compiles `src/` with Windows' own `csc.exe` (C# 5, from the running .NET Framework, never from PATH), with
   a reference to this tool. Nothing runs if it does not compile.
3. It loads the result and calls `Run`. The package can use this tool's public classes.

## Patching steps and options from a package

A package for a tablet whose firmware this tool patches does not have to replace the tool: it adds its own firmware
changes and options, then hands over to the normal wizard, so its changes go through the same settings screens, twin
check, backup and flashing steps as the tool's own.

| Member | What it does |
|---|---|
| `interface IPatch { string Name(); void Apply(Image im); }` | one firmware change; `Apply` checks with `im.Expect` and writes with `im.Wr` / `im.Branch` / `Asm` in the cave |
| `Build.AddPatch(IPatch p)` | a change that every build in this run makes: after the tool's own changes, in its own `Package: <name>` group, before the undo record; the options text gets `PACKAGE=<name>` |
| `Build.AddSetting(Setting s, IPatch p)` | an option of the package's own: `new Setting(key, name, Build.PKG_CAT, default, tag, "what it does\|what it costs")`. It shows under "Package options" at the end of the Extra QOL Patches screen (or in another category it names), its tag goes into the file name when it differs from its default, and `p` runs only in builds where it is on, after the `AddPatch` changes, in a `Package option: <name>` group; the options text gets `PACKAGE-OPTION=<KEY>`. Keys and tags are 1-16 and 1-8 letters or digits, and a key cannot repeat one of the tool's |
| `Build.HideSetting(string key, string why)` | removes one of the tool's options for this tablet: it is not shown, and every build treats it as off (the settings line in the build log says `n/a`). Volatile Changes cannot be hidden, because RAPID and ULTRA need it |
| `Build.Option(string key)` | one of the tool's options, to change for this tablet: its `name`, `what`, `dflt` (default) or `tag`. Change options only through this, never through `Build.SETTINGS` directly |
| `Build.IsOn(string key)`, `Build.BuildingPreset()` | inside `Apply`: whether an option is on in the build being made, and that build's preset (`.key`, `.name`), so a patch can adapt to the other choices |
| `Build.ClearPatches()` | undoes everything above: changes, options, hidden and changed options. Call it in a `finally` when the package is done, so the built-in tool is unchanged afterwards |
| `Layout.AddSignatures(string[] lines)` | fingerprints (from `tools/mksig.py`) for firmware locations the tool does not know; identical lines are ignored, a line that differs from a known one stops |
| `Cli.Run(new string[] { "--wizard" })` | starts the normal wizard, as if the user had chosen the firmware mod |

The option keys of the tool's own settings are in `Build.SETTINGS` (`atf_build.cs`), for example `odd` (Jump
Rejection), `fullres` (12-bit Readings), `tiltskip` (Tilt Frame Skip) and `longrange` (Long-range Pickup).

Package changes and package options are left out of the calibration measurement build, the ALPI training build and
the return-to-stock build; the build log says so in one line. A failed `Expect` or a missing location stops the
build, as for the tool's own changes.

The step-by-step walk-through, with a complete example package, is
[`tutorial-patcherpkg.md`](tutorial-patcherpkg.md); the firmware side is in
[`tutorial-self-modding.md`](tutorial-self-modding.md).

## Making the ZIP

The files must be at the top level of the ZIP. With PowerShell, from inside the package folder:

```powershell
Compress-Archive -Path package.ini, src, README.md -DestinationPath ..\MyPatcher.zip
Rename-Item ..\MyPatcher.zip MyPatcher.patcherpkg
```

`Compress-Archive` only writes `.zip` names, hence the rename. Windows PowerShell 5.1 stores `src\X.cs` with a
backslash; since R45-rc3 the tool reads `\` as a folder mark and then applies the same path checks (R44 to R45-rc2
refuse such ZIPs as "unsafe file path"; ship the folder for those).

The .NET 8 builds (Linux, macOS, `build.sh`) have no compiler to use, so they say packages need the Windows build.

## What the tool cannot check

A package runs with your account's full access. It can flash the tablet without this tool's checks. The tool does not
sign or check packages; the typed approval and the SHA-256 are the only guards. Only run packages from people you
trust.

**For package authors:** flash only through this tool's own steps, so every image still gets the twin check, the
backup and the tablet build check:

1. `Cli.Gate(...)`: the digital-twin check of the built image.
2. `Cli.BeforeFlash(folder, info)`: the backup and the check that the tablet runs the build the image came from.
3. `Cli.FlashSteps(...)`: runs xppen-tools, which asks for its typed confirmation.

Never read USB string 200 (it puts the tablet into update mode), and never change page `0x08002C00` or string 173
(AGENTS.md rule 11).
