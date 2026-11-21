# R38 - Claude revision record

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

Contributor: Claude (Anthropic). The exact model version is not recorded in this repository. Date: 2026-11-08.
Input: `anargons-deco640-tools-R37-rc.zip` as delivered by the R37 Codex session. Read CLAUDE.md, CODEX.md,
AGENTS.md and `history/codex-rev36.md` / `codex-rev37.md` first; this record only covers what R38 changed.

## Reported against R37-rc (Anargon), and what the source showed

| # | Report | Root cause found in the R37-rc source |
|---|---|---|
| 1 | The startup dump no longer runs alongside the steps; everything stops until it is read | `Cli.Wizard` started with `StartupBackup.Wait()`, so the whole wizard blocked for the full ~3 minute dump. The overlapping text in Anargon's quote ("...2 framesore flashing / Backup: put xppen-tools...") is the older cursor-rewind redraw, which R37's forward-only `Screen.Ask` had already removed; R37 then also added the blocking wait. |
| 2 | A second backup is made although one was already made | `Cli.BeforeFlash` always ran a new complete capture (`Cli.Backup`), even right after a successful startup capture, and after waiting for a running one. Calibration plus the final flash meant up to three dumps per run. |
| 3 | The AI disclosure box is gone | No AI disclosure text existed anywhere in the R37-rc sources (it dates from rev 28, see `claude-rev24-30.md`). |
| 4 | Nothing can be typed at xppen-tools' `Type exactly: UPDATE 28BD:FFF1 RUNTIME=28BD:2904` prompt | `P.RunInResult` (gd32emu.cs platform layer) started every helper with `CreateNoWindow = true`. On Windows that gives a console child its own hidden console; the interactive update inherited no usable keyboard, so its prompt waited on input nobody could type. Separately, stdout and stderr shared one partial-line buffer, which glued stderr into the prompt line (`> [INFO] Device: ...`). |

## Changes

- **AI disclosure (permanent).** `Screen.AI_DISCLOSURE` + `Screen.Disclosure()` (atf_cli.cs) print Anargon's exact
  box, 80 columns, at the top of every `Screen.Step` and `Screen.DangerAsk` screen (menu, all wizard steps,
  calibration steps, backup/flash, recovery tool, full-screen warnings), and once at the start of every other run
  (`--check`, `--compat`, `--return-to-stock`, `--hid-check`, `--pen-resonance`, `--flash-count`, scripted builds and
  flashes); only `--help`, `--version`, `--setup` and `twin` commands print without it. AGENTS.md rule 12
  forbids removing it; `HostRegressions.Disclosure` fails if the text changes or a screen loses it.
- **Background dump during the steps.** `Wizard` no longer waits at its start. `StartupBackup.Poll()` (atf_backup.cs)
  collects a finished dump between screens without blocking; the warning list shows "running in the background"
  until then. `Wait()` is now called only where the dump is needed: when the chosen input is the very file the
  running dump will write (offered as the default when nothing else exists), in `BeforeFlash` (calibration and every
  flash), for the recovery tool, and at program exit. The dump thread never prints; it owns its xppen-tools child,
  which Ctrl+C and exit still kill (R37's `StopChildren` and Windows job, unchanged). Step 1 now turns an I/O error
  while reading the input (for example the dump replacing `last-dump/ic902-main.bin` at that moment) into a retry
  warning instead of ending the program.
- **One backup per run.** `Cli.sessionBackup` records the run's successful complete backup (startup or pre-flash).
  `BeforeFlash(folder, info)` uses it; if the startup dump is still running it waits for that one instead of starting
  another. A new capture is made only when the run has no successful backup (failed startup capture, command-line
  `--flash`, `--calibrate`, `--calibration-pass --flash`). `BeforeFlash(folder, info, true)` keeps return-to-stock's
  second read, which verifies the restored bootloader before the stock application is flashed (R36 safety rule).
  `Cli.backupLeftDfu` tells `FlashSteps` whether the tablet is already in update mode; a reused startup backup has
  returned it to normal mode, so `dfu` runs first. `Cli.Run` resets this per-run state.
- **Interactive xppen-tools input** (gd32emu.cs, `P.ToolStart` / `P.RunInResult`). Interactive helpers (`echo`):
  `CreateNoWindow = false`, stdin inherited, so xppen-tools runs in this console with this keyboard exactly like a
  manual run. Quiet background helpers keep the hidden console and closed stdin (R37 behaviour), so they cannot take
  the menu's input.
- **Helper output layout** (`P.LinePump`, `P.CharPump`, `P.PromptTicker`). Separate partial lines per stream; whole
  read chunks are echoed under one lock so a burst stays together; a prompt is shown once the helper has been quiet
  for 150 ms (at most 1 s after it appeared), so log lines flushed right after the prompt print above it and the
  prompt is the last line on screen. Text after an answered prompt starts a new, indented line.
- Docs: README "Backups and updates", DOCS/DESIGN.md calibration paragraph, gd32emu DOCS/DESIGN.md process paragraph,
  CHANGELOG, AGENTS.md rule 12 and model row, CLAUDE.md R38 section, model provenance. Revision label R38
  (`Cli.RELEASE_VERSION`, both VERSION files).
- Unchanged: firmware patches, twin gates, compatibility checks, the counter page/records/string 173 (rule 11),
  bootloader paths, calibration logic.

## Verification (no tablet, no Windows)

Ran on Linux in this session. Not run on Windows and not on a real tablet.

| Check | Result |
|---|---|
| Mono `mcs -langversion:5` build of tool and emulator; `twin --selftest` | ok |
| `tests/run-regressions.py` on .NET 8 (scratch copy retargeted from net10.0; no .NET 10 SDK available) | core 106, host 81 (R37: 57 here without a stock fixture), calibration 56 passed; same core/calibration counts as unmodified R37-rc |
| `tests/run-tests.py` smoke on the .NET 8 build | 20 passed, 2 skipped (no stock dumps) |
| `tools/cs2java.py` translation, `javac`, Java `twin --selftest` | ok |
| New host checks (24): disclosure text/box/screens/CLI; wizard reaches step 3 while a held dump runs; running dump's file offered and waited for only when chosen; flash during the startup dump waits and reuses it; no second backup later in the run; `dfu` before update after reuse; return-to-stock fresh read kept; failed startup dump leads to one new capture; prompt layout; interactive start settings; typed confirmation reaches an interactive helper | all pass |
| Mutation check: re-adding the wizard wait, `CreateNoWindow = true`, removing the disclosure, forcing a second backup, gluing stderr into stdout | each makes a named host check fail |
| Real program in a Linux pseudo-terminal, Python fake xppen-tools that holds its dump for 40 s | menu and steps 1-3 completed while the dump ran, disclosure on all four screens; Ctrl+C at step 3 left no xppen-tools process |
| Flash path in a pseudo-terminal (.NET 8 harness): startup dump still running, then flash | waited for it (one `dump` in the helper log), `dfu`, update prompt last on screen with the `[INFO]` lines above it, typed `UPDATE 28BD:FFF1 RUNTIME=28BD:2904` echoed and accepted, success |

The input fix is a Windows console fact (hidden console from `CREATE_NO_WINDOW`) reasoned from the Win32/.NET process
model, not reproduced here. The Linux runs prove inheritance of the caller's input and the layout, not Windows console
behaviour. **First check on Windows:** build with `build.cmd`, flash a checked build from the wizard, and type the
UPDATE confirmation; it must echo and be accepted. A failing case is visible immediately and writes nothing (the
helper has sent no state-changing packet before the confirmation).

## Open

- Windows console confirmation as above; physical tablet behaviour is unchanged from the R37 open list.
- Ctrl+C during the startup dump leaves the tablet in update mode (the dump's `exit-dfu` is never run once
  cancellation starts). Leave it with `xppen-tools exit-dfu --pid FFF1`. Not changed in R38.
- `DOCS/verification/real-console-enter.txt` contains a harness that refers to the rejected consolidated
  `release/.../app` layout and does not run as written in this tree. Left as a historical record.
