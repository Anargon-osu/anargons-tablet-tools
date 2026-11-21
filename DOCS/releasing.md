# Building a release

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

The tablet builder and gd32emu remain separate repositories. Publish each project from its own checkout. A source handoff puts `anargons-tablet-tools/`, `gd32emu/`, and `README.md` at the ZIP's top level, without an extra wrapper folder, generated firmware or prebuilt executables. Root model files and the tablet repository's root history/ are retained as contributor entrypoints.

From the tablet repository root, run `build.cmd` on Windows or `bash build.sh` on Linux/macOS. The Windows build uses the system .NET Framework compiler; self-contained publishing uses the .NET 10 SDK. Keep the emulator as a sibling `../gd32emu/` checkout or set `GD32EMU_DIR`.

## Checks

```text
python -I tests/test-infrastructure.py
python -I tests/run-regressions.py
python -I tests/run-tests.py ./AnargonsTabletTools --smoke
```

Use `AnargonsTabletTools.exe` on Windows. Locally these runs are capped at 50 % of the CPU together; add `--uncapped` to run as fast as possible (the GitHub workflow always does). For firmware qualification, configure `ADT_TEST_DUMPS_REPO` (a git repository holding the stock dumps as `dumps/<firmware version>/ic902-main.bin`, cloned into `tests/work/dumps-repo/`; every version folder there is tested) or `ADT_TEST_STOCK_DUMP` and `ADT_TEST_STOCK_DUMP_DEC`, then run `tests/run-tests.py` with `--require-all-firmware`. A February-only `--require-firmware` candidate must record its missing December coverage. Run the emulator's own build/tests from its separate repository too.

The project uses rolling revision labels such as R37 up to R45; after R45 a revision is named after its pull request (`PR #X`, AGENTS.md section 4). Do not introduce semantic-version renumbering.

## Native release packages

The tablet repository's `.github/workflows/` prepares the configured emulator source, builds and tests native targets, then packages successful binaries with `tests/package.py`. No repository variables are needed: without them the workflow takes the stock dumps from `Anargon-osu/deco640-resources` and `gd32emu.cs` from `Anargon-osu/GD32EMU`, checked against the SHA-256 pinned in `tests/prepare-emulator.py` (update it with the emulator). A private repository needs the `ADT_TEST_DUMP_TOKEN` secret. A tag starting with `R` builds a release; a tag with `-rc` in it (`R45-rc6`) becomes a pre-release. Run `python -I tests/package.py --help` for its executable/RID arguments; for example, `python -I tests/package.py ./AnargonsTabletTools.exe win-x64`. Native releases use ZIP packages on every supported platform. The gd32emu repository has its own build workflow. Complete GitHub release downloads may include self-contained executables and runtime notices; the source handoff contains source only.

Keep personal `.env` files, SDK/cache directories, tablet dumps, generated firmware, state and local build outputs out of source archives. Verify the archive inventory and SHA-256, extract it into a fresh folder, and build using the sibling project paths before delivery. Keep checks tied to the exact source revisions used. This project has no website or GitHub Pages dependency.

See [verification scope](verification.md) for physical hardware and firmware-fixture limits.
