#!/usr/bin/env python3
"""Compile the C# regression suites and run every test group as its own process, all at once.

Each suite (core, host, calibration) is split into groups: ADT_TEST_LIST_GROUPS=1 lists them, ADT_TEST_GROUPS=a,b
runs only those. Every group runs in its own folder under the work directory, so runs side by side (one per stock
dump, say) never share a file. All groups start together; only low free memory holds a start back.
CPU: by default this run, together with every other capped test run on this computer, uses at most 50 % of the
total CPU (tests/cpucap.py); --uncapped runs as fast as possible (GitHub always passes it).
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import threading
import time
import uuid

ROOT = Path(__file__).resolve().parent.parent
CASES = (
    ("core-regressions.cs", "AnargonsTabletTools.Tests.CoreRegressions"),
    ("host-regressions.cs", "AnargonsTabletTools.Tests.HostRegressions"),
    ("calibration-regressions.cs", "AnargonsTabletTools.CalibrationRegressions"),
)
MARKERS = {"core-regressions": r"^core regressions: (\d+) passed$",
           "host-regressions": r"^RESULT host regressions: (\d+) passed \(fake xppen-tools only\)$",
           "calibration-regressions": r"^RESULT calibration regressions passed: (\d+)$"}
TOTALS = {"core-regressions": "core regressions: %d passed",
          "host-regressions": "RESULT host regressions: %d passed (fake xppen-tools only)",
          "calibration-regressions": "RESULT calibration regressions passed: %d"}
GROUP_TIMEOUT = 900          # seconds per group process
# R45: short folder names (work/cal/fw-background/...). Windows limits paths to 260 characters, and run-tests.py nests
# a whole regression run inside its own work folder; with the long names the calibration suite's files passed the
# limit in a source folder 118 characters deep.
SHORT = {"core-regressions": "core", "host-regressions": "host", "calibration-regressions": "cal"}
MIN_FREE_MB = 1024           # a group starts only with this much free memory (or when nothing else runs)


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / "tests" / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


CPUCAP = load("cpucap", "cpucap.py")


def prepare_work(path):
    if path is not None:
        path = path.resolve()
        path.mkdir(parents=True, exist_ok=False)
        return path
    parent = ROOT / "tests/work/regressions"
    parent.mkdir(parents=True, exist_ok=True)
    return Path(tempfile.mkdtemp(prefix="run-", dir=parent))


def free_memory_mb():
    """Free physical memory in MB, or None where it cannot be read cheaply."""
    try:
        if os.name == "nt":
            import ctypes

            class Status(ctypes.Structure):
                _fields_ = [("length", ctypes.c_uint32), ("load", ctypes.c_uint32), ("total", ctypes.c_uint64),
                            ("free", ctypes.c_uint64), ("page_total", ctypes.c_uint64), ("page_free", ctypes.c_uint64),
                            ("virtual_total", ctypes.c_uint64), ("virtual_free", ctypes.c_uint64),
                            ("extended", ctypes.c_uint64)]
            status = Status()
            status.length = ctypes.sizeof(status)
            if ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(status)):
                return status.free // (1024 * 1024)
        elif os.path.exists("/proc/meminfo"):
            for line in Path("/proc/meminfo").read_text().splitlines():
                if line.startswith("MemAvailable:"):
                    return int(line.split()[1]) // 1024
    except (OSError, ValueError, AttributeError):
        pass
    return None


def find_emulator():
    emulator = os.environ.get("GD32EMU_DIR", "")
    candidates = [ROOT / emulator] if emulator else [ROOT / "gd32emu", ROOT.parent / "gd32emu"]
    return next((path.resolve() for path in candidates if (path / "gd32emu.cs").is_file()), None)


DOTNET = threading.Lock()    # dotnet builds of the one project file run one at a time


def compile_suite(filename, entry, emulator, folder, cap):
    """Builds one suite into folder; returns the command that runs it."""
    if os.name == "nt":
        windows = Path(os.environ["WINDIR"]) / "Microsoft.NET"
        compiler = windows / "Framework64" / "v4.0.30319" / "csc.exe"
        if not compiler.is_file():
            compiler = windows / "Framework" / "v4.0.30319" / "csc.exe"
        executable = folder / "regressions.exe"
        command = [str(compiler), "/nologo", "/langversion:5", "/optimize+", "/main:" + entry,
                   "/out:" + str(executable), str(emulator / "gd32emu.cs")]
        command += [str(path) for path in sorted(ROOT.glob("atf_*.cs"))] + [str(ROOT / "tests" / filename)]
        cap.run(command, cwd=ROOT, check=True, timeout=300, stdin=subprocess.DEVNULL)
        return [str(executable)]
    with DOTNET:
        cap.run(["dotnet", "build", "AnargonsTabletTools.csproj", "-c", "Release", "-o", str(folder),
                        "-p:SelfContained=false", "-p:PublishSingleFile=false", "-p:StartupObject=" + entry,
                        "-p:BaseIntermediateOutputPath=" + str(folder / "obj") + os.sep,
                        "-p:RegressionSource=tests/" + filename, "-p:Gd32EmuDir=" + str(emulator) + "/"],
                       cwd=ROOT, check=True, timeout=300, stdin=subprocess.DEVNULL)
    return ["dotnet", str(folder / "AnargonsTabletTools.dll")]


def group_env(base, folder, groups=None, listing=False):
    env = dict(base)
    env.pop("ADT_TEST_GROUPS", None)
    env.pop("ADT_TEST_LIST_GROUPS", None)
    if groups:
        env["ADT_TEST_GROUPS"] = groups
    if listing:
        env["ADT_TEST_LIST_GROUPS"] = "1"
    env["ADT_STATE_DIR"] = str(folder / "state")
    env.setdefault("ADT_TEST_COUNT_STRING", "unavailable")   # never read a real tablet's counter string
    return env


def list_groups(command, folder, env, cap):
    folder.mkdir()
    result = cap.run(command, cwd=folder, env=group_env(env, folder, listing=True), text=True,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL,
                            encoding="utf-8", errors="replace", timeout=300)
    (folder / "result.txt").write_text(result.stdout, encoding="utf-8")
    groups = re.findall(r"^GROUP (\S+)$", result.stdout, re.MULTILINE)
    if result.returncode != 0 or not groups or len(set(groups)) != len(groups):
        raise RuntimeError("could not list the groups (exit %s):\n%s" % (result.returncode, result.stdout))
    return groups


class Task:
    def __init__(self, suite, group, command, folder):
        self.suite, self.group, self.command, self.folder = suite, group, command, folder
        self.process = self.started = self.seconds = self.exit_code = self.count = None
        self.status, self.output = "pending", ""

    @property
    def name(self):
        return self.suite + "/" + (self.group or "(whole suite)")


def run_tasks(tasks, env, cap):
    """Starts every task at once (memory permitting) and waits for all; prints one line per finished task."""
    pending, running, held = list(tasks), [], False
    while pending or running:
        while pending:
            free = free_memory_mb()
            if running and free is not None and free < MIN_FREE_MB:
                if not held:
                    print("  (free memory under %d MB: further groups start as others finish)" % MIN_FREE_MB, flush=True)
                    held = True
                break
            task = pending.pop(0)
            task.folder.mkdir(parents=True)
            task.log = open(task.folder / "result.txt", "wb")
            task.started = time.monotonic()
            task.process = cap.popen(task.command, cwd=task.folder, env=group_env(env, task.folder, task.group),
                                            stdout=task.log, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL)
            running.append(task)
        time.sleep(0.1)
        for task in list(running):
            code = task.process.poll()
            if code is None and time.monotonic() - task.started > GROUP_TIMEOUT:
                task.process.kill()
                code = task.process.wait()
                task.status = "timeout"
            if code is None:
                continue
            running.remove(task)
            task.log.close()
            task.seconds = time.monotonic() - task.started
            task.exit_code = code
            task.output = (task.folder / "result.txt").read_text(encoding="utf-8", errors="replace")
            matches = re.findall(MARKERS[task.suite], task.output, re.MULTILINE)
            task.count = int(matches[0]) if len(matches) == 1 else None
            passed = task.status != "timeout" and code == 0 and task.count is not None and task.count > 0
            task.status = "passed" if passed else ("timeout" if task.status == "timeout" else "failed")
            print("  %-6s %s: %s checks, %.1f s" % ("ok" if passed else "FAIL", task.name,
                  task.count if task.count is not None else "?", task.seconds), flush=True)


class Usage(threading.Thread):
    """Samples the capped processes' CPU time each second: average and busiest 3 s, as % of all CPUs."""

    def __init__(self, cap):
        threading.Thread.__init__(self, daemon=True)
        self.cap, self.cpus, self.samples = cap, os.cpu_count() or 1, []
        self.done = threading.Event()

    def run(self):
        while True:
            used = self.cap.used_seconds()
            if used is None:
                return
            self.samples.append((time.monotonic(), used))
            if self.done.wait(1.0):
                self.samples.append((time.monotonic(), self.cap.used_seconds()))
                return

    def text(self):
        if len(self.samples) < 2:
            return None
        share = lambda a, b: 100.0 * (b[1] - a[1]) / max(1e-6, (b[0] - a[0]) * self.cpus)
        peak = max(share(self.samples[i], self.samples[i + 3]) for i in range(len(self.samples) - 3)) \
            if len(self.samples) > 3 else share(self.samples[0], self.samples[-1])
        return "%.0f %% of all %d CPUs on average, %.0f %% in the busiest 3 s" % (
            share(self.samples[0], self.samples[-1]), self.cpus, peak)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--stock", type=Path, help="optional verified 64 KB stock dump")
    parser.add_argument("--work", type=Path, help="fresh output directory; default creates a unique tests/work/regressions/run-* folder")
    parser.add_argument("--run-id", default=uuid.uuid4().hex, help="invocation identifier recorded in the result receipt")
    parser.add_argument("--uncapped", action="store_true", help="no CPU cap: run as fast as possible (GitHub always uses it)")
    parser.add_argument("--only", help="comma list of SUITE or SUITE/GROUP to run, e.g. core/fw-boot,host")
    parser.add_argument("--whole", action="store_true", help="run each suite as one process (no groups), to cross-check the totals")
    args = parser.parse_args()
    if args.stock is not None and not args.stock.is_file():
        parser.error("no such stock dump: " + str(args.stock))
    emulator = find_emulator()
    if emulator is None:
        sys.exit("gd32emu source not found; set GD32EMU_DIR to its folder")
    try:
        work = prepare_work(args.work)
    except FileExistsError:
        parser.error("choose a fresh --work directory: " + str(args.work))
    try:
        cap = CPUCAP.Cap(args.uncapped)
    except CPUCAP.CapError as error:
        sys.exit(str(error))
    began = time.monotonic()
    only = [item.strip() for item in (args.only or "").split(",") if item.strip()]
    # a suite by its full name (core-regressions) or its short one (core, host, cal or calibration)
    full = {short: stem for stem, short in SHORT.items()}
    full["calibration"] = "calibration-regressions"
    only = [full.get(item.split("/", 1)[0], item.split("/", 1)[0]) + item[len(item.split("/", 1)[0]):] for item in only]
    stock = [str(args.stock.resolve())] if args.stock else []
    env = os.environ.copy()
    proof = {"run_id": args.run_id, "status": "running", "cpu_cap": cap.text, "cases": []}
    print("regression run: " + args.run_id, flush=True)
    print("CPU: " + cap.text, flush=True)

    # 1. compile the three suites at once
    folders, commands, errors = {}, {}, {}

    def build(case):
        filename, entry = case
        stem = Path(filename).stem
        try:
            if not (ROOT / "tests" / filename).is_file():
                raise RuntimeError("required regression source is missing: " + filename)
            folders[stem] = work / SHORT[stem]
            folders[stem].mkdir()
            commands[stem] = compile_suite(filename, entry, emulator, folders[stem], cap) + stock
        except Exception as error:
            errors[stem] = error
    threads = [threading.Thread(target=build, args=(case,)) for case in CASES]
    for thread in threads:
        thread.start()
    for thread in threads:
        thread.join()
    print("compiled %d of %d suites, %.1f s" % (len(commands), len(CASES), time.monotonic() - began), flush=True)

    # 2. list each suite's groups, then 3. run one process per group, all at once
    tasks = []
    for filename, _ in CASES:
        stem = Path(filename).stem
        if stem in errors:
            continue
        wanted = [item.split("/", 1)[1] for item in only if "/" in item and item.split("/", 1)[0] == stem]
        if only and stem not in only and not wanted:
            continue
        if args.whole:
            tasks.append(Task(stem, None, commands[stem], folders[stem] / "_whole"))
            continue
        try:
            groups = list_groups(commands[stem], folders[stem] / "_list", env, cap)
        except (RuntimeError, OSError, subprocess.SubprocessError) as error:
            errors[stem] = error
            continue
        unknown = [group for group in wanted if group not in groups]
        if unknown:
            errors[stem] = RuntimeError("no such group: " + ", ".join(unknown) + " (groups: " + ", ".join(groups) + ")")
            continue
        for group in (wanted or groups):
            tasks.append(Task(stem, group, commands[stem], folders[stem] / group))
    print("running %d group%s at once" % (len(tasks), "" if len(tasks) == 1 else "s"), flush=True)
    usage = Usage(cap)
    usage.start()
    run_tasks(tasks, env, cap)
    usage.done.set()
    usage.join()

    # 4. totals per suite, in the suites' own words; full output of every failed group
    failures = 0
    for filename, _ in CASES:
        stem = Path(filename).stem
        mine = [task for task in tasks if task.suite == stem]
        if stem in errors:
            failures += 1
            print("FAILED regression executable: %s (%s)" % (filename, errors[stem]), flush=True)
            proof["cases"].append({"name": stem, "status": "failed", "error": str(errors[stem])})
            continue
        if not mine:
            continue
        bad = [task for task in mine if task.status != "passed"]
        for task in bad:
            print("---- %s (exit %s, %s) ----" % (task.name, task.exit_code, task.status), flush=True)
            print(task.output, end="" if task.output.endswith("\n") else "\n", flush=True)
        total = sum(task.count or 0 for task in mine)
        if bad:
            failures += 1
            print("FAILED regression executable: %s (groups: %s)" % (filename, ", ".join(t.group or "whole" for t in bad)), flush=True)
        elif not only:
            print(TOTALS[stem] % total, flush=True)
        else:
            print("%s: %d passed in the chosen groups" % (stem, total), flush=True)
        proof["cases"].append({"name": stem, "status": "failed" if bad else "passed", "count": total,
            "executable": commands[stem][-1 - len(stock)], "command": commands[stem],
            "groups": [{"name": task.group or "(whole suite)", "status": task.status, "exit_code": task.exit_code,
                        "count": task.count, "seconds": round(task.seconds or 0, 2),
                        "log": str(task.folder.relative_to(work) / "result.txt"),
                        "output_sha256": hashlib.sha256(task.output.encode("utf-8")).hexdigest()} for task in mine]})
    proof["status"] = "failed" if failures else "passed"
    proof["seconds"] = round(time.monotonic() - began, 1)
    proof["cpu_used"] = usage.text()
    receipt = work / "result.json"
    receipt.write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
    print("regressions: %d checks in %d processes, %.1f s (CPU: %s)" % (sum(t.count or 0 for t in tasks), len(tasks),
          proof["seconds"], cap.text), flush=True)
    if proof["cpu_used"]:
        print("CPU used: " + proof["cpu_used"] + (" (every capped run in the shared job)" if cap.shared else ""), flush=True)
    print("regression receipt: " + str(receipt), flush=True)
    cap.close()
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
