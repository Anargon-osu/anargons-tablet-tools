#!/usr/bin/env python3
"""Compile one misctests harness with the project sources and run it.

    python -I misctests/run.py NAME [ARGS...]      (NAME = laddertest, steptrace, hoverpressure, ...)
    python -I misctests/run.py --list

The harness is misctests/NAME.cs; its entry point is the static class in it that has Main. The executable goes to
tests/work/misctests/NAME.exe and is rebuilt only when a source file is newer. It runs with ADT_STATE_DIR in that
folder and ADT_XPPEN_TOOLS pointing at a file that does not exist, so it can never reach a real tablet or this PC's
saved state. Windows only for now (the .NET Framework csc.exe, nothing to install), like the regression suites there.
"""
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parent.parent
HERE = ROOT / "misctests"
WORK = ROOT / "tests" / "work" / "misctests"


def find_emulator():
    emulator = os.environ.get("GD32EMU_DIR", "")
    candidates = [ROOT / emulator] if emulator else [ROOT / "gd32emu", ROOT.parent / "gd32emu"]
    return next((path.resolve() for path in candidates if (path / "gd32emu.cs").is_file()), None)


def entry_of(source):
    text = source.read_text(encoding="ascii")
    ns = re.search(r"^\s*namespace\s+([\w.]+)", text, re.M)
    for m in re.finditer(r"static\s+class\s+(\w+)", text):
        if re.search(r"static\s+int\s+Main\s*\(", text[m.end():]):
            return (ns.group(1) + "." if ns else "") + m.group(1)
    sys.exit(source.name + ": no static class with Main")


def main():
    if len(sys.argv) < 2 or sys.argv[1] in ("-h", "--help"):
        print(__doc__)
        return 2
    if sys.argv[1] == "--list":
        for f in sorted(HERE.glob("*.cs")):
            first = f.read_text(encoding="ascii").splitlines()[0]
            print(f.stem.ljust(16) + first.split(" - ", 1)[-1])
        return 0
    if os.name != "nt":
        sys.exit("run.py builds with the .NET Framework csc.exe (Windows); see misctests/README.md")
    name = sys.argv[1]
    source = HERE / (name + ".cs")
    if not source.is_file():
        sys.exit("no misctests/" + name + ".cs (try --list)")
    emulator = find_emulator()
    if emulator is None:
        sys.exit("gd32emu source not found; set GD32EMU_DIR to its folder")
    sources = [source, emulator / "gd32emu.cs"] + sorted(ROOT.glob("atf_*.cs"))
    WORK.mkdir(parents=True, exist_ok=True)
    exe = WORK / (name + ".exe")
    if not exe.is_file() or exe.stat().st_mtime < max(s.stat().st_mtime for s in sources):
        windows = Path(os.environ["WINDIR"]) / "Microsoft.NET"
        csc = windows / "Framework64" / "v4.0.30319" / "csc.exe"
        if not csc.is_file():
            csc = windows / "Framework" / "v4.0.30319" / "csc.exe"
        cmd = [str(csc), "/nologo", "/langversion:5", "/optimize+", "/nowarn:168,219", "/main:" + entry_of(source),
               "/out:" + str(exe)] + [str(s) for s in sources]
        built = subprocess.run(cmd, cwd=ROOT, stdin=subprocess.DEVNULL, capture_output=True, text=True)
        if built.returncode != 0:
            sys.stdout.write(built.stdout + built.stderr)
            return built.returncode
    env = dict(os.environ)
    env["ADT_STATE_DIR"] = str(WORK / "state")
    env["ADT_XPPEN_TOOLS"] = str(WORK / "no-xppen-tools.exe")
    env.setdefault("ADT_TEST_COUNT_STRING", "unavailable")
    return subprocess.run([str(exe)] + sys.argv[2:], stdin=subprocess.DEVNULL, env=env).returncode


if __name__ == "__main__":
    sys.exit(main())
