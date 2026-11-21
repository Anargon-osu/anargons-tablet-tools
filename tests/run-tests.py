#!/usr/bin/env python3
"""Portable integration checks; Python is a contributor/test dependency only.

run-tests.py EXECUTABLE [--smoke | --require-firmware | --require-all-firmware] [--build feb|dec|all] [--uncapped]
No flags: smoke checks plus configured stock dumps, with explicit skips.
--require-firmware requires February; --require-all-firmware requires both builds.
--build: which stock builds to test (default all: February and December at the same time, each in its own threads
and processes). Two runs with --build feb and --build dec side by side work too (separate work folders).
--uncapped: no CPU cap (tests/cpucap.py; by default every capped test run together uses at most 50 % of the CPU).
GitHub always passes it.

Stock dumps, per build, the first that is set wins:
  ADT_TEST_STOCK_DUMP / ADT_TEST_STOCK_DUMP_DEC   a local path (relative to the project) or an HTTPS link
  ADT_TEST_DUMPS_REPO (+ ADT_TEST_DUMPS_REF)       an HTTPS git repository holding the dumps. It is cloned (and on
      later runs fetched and checked out again) into tests/work/dumps-repo/<name>/. Layout: a folder named dumps/
      with one folder per firmware version, named after it, each holding that version's untouched 64 KB
      ic902-main.bin (dumps/2025-02-14_release001/ic902-main.bin). The folder name must be the version the firmware
      names itself. February and December are found by their firmware CRC and get every check; any other version
      gets the digital-twin checks (compatibility, all five presets), so a new firmware version is tested by adding
      its folder; recovery repairs only versions it has fingerprints for, so for any other it is a skip. A repository without dumps/ is searched for the two known dumps by CRC
      instead. Nothing in the clone is run: only .bin-sized files are read. Several runs at once share it through a
      lock file.
ADT_TEST_DUMP_TOKEN, if set, is sent for HTTPS downloads and for the git clone (as a header, never in the link, so it
is not stored in the clone's .git/config). In the environment or .env.
"""
import argparse
import base64
import contextlib
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
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parent.parent


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / "tests" / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


DUMP = load("dumptool", "dumptool.py")
CPUCAP = load("cpucap", "cpucap.py")
DUMPS_REPO_DIR = ROOT / "tests" / "work" / "dumps-repo"


def settings():
    result = {}
    path = ROOT / ".env"
    if path.is_file():
        for line in path.read_text(encoding="utf-8-sig").splitlines():
            text = line.strip()
            if not text or text.startswith("#") or "=" not in text:
                continue
            key, value = text.split("=", 1)
            key, value = key.strip(), value.strip()
            if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
                value = value[1:-1]
            result[key] = value
    result.update(os.environ)
    return result


class Suite:
    # cap: a cpucap.Cap made BEFORE the suite, so every process it starts is inside it (None: plain subprocess)
    def __init__(self, executable, work=None, run_id=None, cap=None, uncapped=False):
        self.cap, self.uncapped = cap, uncapped
        self.lock = threading.Lock()            # February and December run in threads at the same time
        self.executable = str(Path(executable).resolve())
        if work is None:
            (ROOT / "tests" / "work").mkdir(parents=True, exist_ok=True)
            self.work = Path(tempfile.mkdtemp(prefix="run-", dir=ROOT / "tests" / "work"))
        else:
            self.work = Path(work).resolve()
            self.work.mkdir(parents=True, exist_ok=False)
        self.run_id = run_id or uuid.uuid4().hex
        self.regression_runs = []
        self.env = os.environ.copy()
        self.env["ADT_STATE_DIR"] = str(self.work / "state")
        # A nonempty invalid stand-in represents a disconnected tablet and avoids
        # querying a real USB device when contributors run the checks locally.
        self.env["ADT_TEST_COUNT_STRING"] = "unavailable"
        self.passed = self.failed = self.skipped = 0

    def check(self, condition, description):
        with self.lock:
            print(("  ok    " if condition else "  FAIL  ") + description, flush=True)
            if condition:
                self.passed += 1
            else:
                self.failed += 1

    def skip(self, description):
        with self.lock:
            print("  skip  " + description, flush=True)
            self.skipped += 1

    def process(self, args, timeout, **kwargs):
        """subprocess.run, inside the CPU cap when the suite has one"""
        if self.cap is not None:
            return self.cap.run(args, timeout=timeout, **kwargs)
        return subprocess.run(args, timeout=timeout, **kwargs)

    def run(self, name, args, expected=0, env=None, cwd=None, text=""):
        run_env = self.env.copy()
        if env:
            run_env.update(env)
        try:
            result = self.process([self.executable] + args, 600, input=text, text=True,
                                  stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                  cwd=cwd or self.work, env=run_env,
                                  encoding="utf-8", errors="replace")
            output, status = result.stdout, result.returncode
        except (OSError, subprocess.TimeoutExpired) as error:
            output, status = str(error), None
        (self.work / (name + ".txt")).write_text(output, encoding="utf-8")
        valid = status != 0 and status is not None if expected == "failure" else status == expected
        self.check(valid, name + ": exit status " + str(status))
        return output

    def has(self, output, pattern, description):
        self.check(re.search(pattern, output) is not None, description)

    def fetch(self, source, target, token):
        if source.startswith("https://"):
            request = urllib.request.Request(source, headers={"Accept": "application/octet-stream"})
            if token:
                request.add_header("Authorization", "Bearer " + token)
            with urllib.request.urlopen(request, timeout=60) as response:
                if not response.geturl().startswith("https://"):
                    raise ValueError("Dump redirects must use HTTPS")
                content = response.read(65537)
            target.write_bytes(content)
        elif "://" in source:
            raise ValueError("Dump links must use HTTPS")
        else:
            path = Path(source).expanduser()
            if not path.is_absolute():
                path = ROOT / path
            target.write_bytes(path.read_bytes())

    def smoke(self):
        print("== smoke checks (no tablet or stock firmware required)", flush=True)
        output = self.run("selftest", ["twin", "--selftest"])
        self.has(output, r"RESULT selftest done", "twin self-test assertions complete")
        output = self.run("help", ["--help"])
        self.has(output, "Anargon's Basic Tablet Recovery", "help names both tools")
        output = self.run("menu", [], expected=2)
        self.has(output, r"2\.  Anargon's Basic Tablet Recovery", "first screen offers both tools")
        (self.work / "small.bin").write_bytes(bytes(1000))
        output = self.run("recovery-small", ["--recovery", "small.bin", "r0", "--name", "T"], expected="failure")
        self.has(output, "exactly 65536 bytes", "recovery refuses incomplete dumps")
        (self.work / "some.hex").write_text(":00000001FF\n", encoding="ascii")
        output = self.run("recovery-hex", ["--recovery", "some.hex", "r0", "--name", "T"], expected="failure")
        self.has(output, "is not a .bin", "recovery refuses HEX input")
        for mode, count in (("B", 42), ("F", 7)):
            output = self.run("counter-" + mode, ["--flash-count"], env={"ADT_TEST_COUNT_STRING": "ADTC1:%s:%06d" % (mode, count)})
            label = "Bootloader" if mode == "B" else "Firmware"
            self.has(output, r"Recorded updates: " + str(count) + r"/100,000 \[According to " + label + r"\]", "counter value and provenance: " + label)
        output = self.run("counter-local", ["--flash-count"])
        self.has(output, r"\[According to local PC\]", "disconnected tablet uses local counter")
        output = self.run("setup", ["--setup"])
        self.has(output, r"udev|Input Monitoring|OpenTabletDriver and the XP-Pen driver", "setup explains OS permissions")
        output = self.run("pid", ["x.bin", "--pid", "1234"], expected="failure")
        self.has(output, "USB IDs are fixed", "USB IDs cannot be changed")

    def firmware(self, expected_build, source, token):
        target = self.work / ("stock-" + expected_build + ".bin")
        try:
            self.fetch(source, target, token)
            build = DUMP.build_of(target.read_bytes())
            if build != expected_build:
                raise ValueError("wrong stock build")
        except Exception as error:
            self.check(False, expected_build + ": could not load verified stock dump (" + type(error).__name__ + ")")
            return
        self.check(True, build + ": untouched 64 KB stock dump verified by firmware CRC")
        self.run_regressions(build, target)
        output = self.run("compat-" + build, ["--compat", str(target)])
        self.has(output, "verdict: COMPATIBLE", build + ": all modifications compatible")
        folder = self.work / ("all-" + build)
        folder.mkdir()
        output = self.run("all-" + build, [str(target), str(folder), "--all"], cwd=folder)
        self.check(output.count("Digital-twin check passed") == 5, build + ": all five presets pass the digital twin")
        self.check(output.count("Flash count:    readable over USB") == 5, build + ": all presets answer USB counter string 173")
        self.check({path.name for path in folder.glob("*.hex")} ==
                   {"[VOLATILE]_" + speed + ".hex" for speed in ("stock", "quick", "fast", "rapid", "ultra")},
                   build + ": exactly five speed-named firmware images disclose volatile changes")
        output = self.run("recovery-good-" + build, ["--recovery", str(target), "rec-ok-" + build, "--name", "T"])
        self.has(output, "No damage found", build + ": stock firmware is not repaired")
        damaged = self.work / ("damaged-" + build + ".bin")
        data = bytearray(target.read_bytes())
        data[0xC188:0x10000] = b"\xff" * (0x10000 - 0xC188)
        data[0x3404:0x3434] = b"\xff" * 0x30
        damaged.write_bytes(data)
        output = self.run("flasher-damaged-" + build, [str(damaged), "flasher-" + build, "--rate", "fast"], expected="failure")
        self.has(output, "This firmware is DAMAGED", build + ": flasher refuses damaged firmware")
        recovery = self.work / ("recovery with spaces-" + build)
        output = self.run("recovery-" + build, ["--recovery", str(damaged), str(recovery), "--name", "Test"])
        self.has(output, "settings restored, tablet waiting in update mode", build + ": twin verifies repair and update mode")
        self.check((recovery / "Test-specific-patcher.hex").is_file(), build + ": repair image produced")
        commands = [line.strip() for line in output.splitlines() if re.match(r"[12]\. xppen-tools update ", line.strip())]
        self.check(len(commands) == 2
                   and ('"' + str(recovery / "Test-specific-patcher.hex") + '"') in commands[0]
                   and ('"' + str(recovery / "test-stock.hex") + '"') in commands[1],
                   build + ": recovery commands quote absolute paths in the chosen output folder")
        stock_hex = recovery / "test-stock.hex"
        try:
            end, crc = DUMP.KNOWN[build]
            self.check(DUMP.hex_crc(stock_hex, end) == crc, build + ": restored firmware has exact stock CRC")
        except (OSError, ValueError) as error:
            self.check(False, build + ": invalid recovered stock HEX (" + type(error).__name__ + ")")

    def other_version(self, version, source):
        """a firmware version in dumps/ with no stored fixture: what the digital twin can check without one"""
        name = re.sub(r"[^A-Za-z0-9_.-]+", "-", version)
        target = self.work / ("stock-" + name + ".bin")
        target.write_bytes(Path(source).read_bytes())
        output = self.run("compat-" + name, ["--compat", str(target)])
        self.has(output, "verdict: COMPATIBLE", version + ": all modifications compatible")
        folder = self.work / ("all-" + name)
        folder.mkdir()
        output = self.run("all-" + name, [str(target), str(folder), "--all"], cwd=folder)
        self.check(output.count("Digital-twin check passed") == 5, version + ": all five presets pass the digital twin")
        self.check(len(list(folder.glob("*.hex"))) == 5, version + ": five firmware images written")
        # recovery repairs only versions whose fingerprints it stores; any other version must be refused, not repaired
        try:
            result = self.process([self.executable, "--recovery", str(target), "rec-ok-" + name, "--name", "T"], 600,
                                  input="", text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, cwd=self.work,
                                  env=self.env, encoding="utf-8", errors="replace")
            output = result.stdout
        except (OSError, subprocess.TimeoutExpired) as error:
            output = str(error)
        (self.work / ("recovery-good-" + name + ".txt")).write_text(output, encoding="utf-8")
        if "No damage found" in output:
            self.check(True, version + ": stock firmware is not repaired")
        elif "This firmware build is not one the repair tool knows" in output:
            self.skip(version + ": recovery cannot repair this version yet (it has no stored fingerprints for it), and says so")
        else:
            self.check(False, version + ": recovery neither checked the dump nor refused it")

    def run_regressions(self, build, stock):
        work = self.work / ("regressions-" + build)
        run_id = self.run_id + "-" + build
        output = self.work / ("regressions-" + build + ".txt")
        self.regression_runs.append({"build": build, "work": str(work), "run_id": run_id, "output": str(output)})
        command = [sys.executable, "-I", str(ROOT / "tests/run-regressions.py"),
                   "--stock", str(stock), "--work", str(work), "--run-id", run_id]
        if self.uncapped:
            command.append("--uncapped")    # capped: the child sees ADT_TEST_CPU_CAP and stays inside this run's cap
        regressions = self.process(command, 1800, cwd=ROOT, env=self.env,
            text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL,
            encoding="utf-8", errors="replace")
        output.write_text(regressions.stdout, encoding="utf-8")
        self.check(regressions.returncode == 0, build + ": firmware-backed host/calibration/core regressions")

    def write_receipt(self):
        proof = {"run_id": self.run_id, "status": "failed" if self.failed else "passed",
                 "passed": self.passed, "failed": self.failed, "skipped": self.skipped,
                 "regression_runs": self.regression_runs}
        (self.work / "result.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")


@contextlib.contextmanager
def held(path, wait=900):
    """A lock file shared by every test run on this computer (made with O_EXCL); one left by a stopped run expires."""
    began = time.monotonic()
    while True:
        try:
            handle = os.open(str(path), os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            break
        except FileExistsError:
            try:
                if time.time() - path.stat().st_mtime > wait:
                    path.unlink()
                    continue
            except OSError:
                continue
            if time.monotonic() - began > wait:
                raise RuntimeError("another test run has held " + str(path) + " for too long")
            time.sleep(0.5)
    try:
        os.write(handle, str(os.getpid()).encode("ascii"))
        os.close(handle)
        yield
    finally:
        try:
            path.unlink()
        except OSError:
            pass


def sync_repository(url, ref, token, root=DUMPS_REPO_DIR, allow_local=False):
    """Clone url (or fetch it again) into its own folder under root and check out ref (empty: the default branch).
    Returns the folder. allow_local is for the infrastructure check only: real runs take https:// links."""
    if not (url.startswith("https://") or allow_local):
        raise ValueError("ADT_TEST_DUMPS_REPO must be an https:// link")
    # a short folder name (the repository's name and 8 hex digits of the link): Windows limits paths to 260 characters
    tail = re.sub(r"[^A-Za-z0-9_.-]+", "-", url.rstrip("/").rsplit("/", 1)[-1]).strip("-.")
    tail = (tail[:-4] if tail.lower().endswith(".git") else tail)[:32] or "repository"
    name = tail + "-" + hashlib.sha256(url.encode("utf-8")).hexdigest()[:8]
    folder = Path(root) / name
    folder.parent.mkdir(parents=True, exist_ok=True)
    env = os.environ.copy()
    env["GIT_TERMINAL_PROMPT"] = "0"          # never wait for a password prompt
    env["GCM_INTERACTIVE"] = "never"          # Git Credential Manager: no sign-in window
    # settings for these git processes only, given through the environment: core.longpaths lets Git for Windows write
    # paths past 260 characters; the token goes in a header, so it is neither on the command line nor written into
    # .git/config (GitHub reads it as user x-access-token with the token as password)
    settings = [("core.longpaths", "true")]
    if token:
        settings.append(("http.extraHeader", "Authorization: Basic " + base64.b64encode(
            ("x-access-token:" + token).encode("utf-8")).decode("ascii")))
    index = int(env.get("GIT_CONFIG_COUNT", "0") or "0")
    for key, value in settings:
        env["GIT_CONFIG_KEY_%d" % index], env["GIT_CONFIG_VALUE_%d" % index] = key, value
        index += 1
    env["GIT_CONFIG_COUNT"] = str(index)

    def git(*args):
        result = subprocess.run(["git"] + list(args), cwd=folder, env=env, stdin=subprocess.DEVNULL,
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
                                encoding="utf-8", errors="replace", timeout=600)
        if result.returncode != 0:
            raise RuntimeError("git %s: exit %d: %s" % (args[0] if args[0] != "-c" else args[2], result.returncode,
                                                       result.stdout.strip()[-400:]))
        return result.stdout

    with held(folder.parent / (name + ".lock")):
        if not (folder / ".git").is_dir():
            folder.mkdir(parents=True, exist_ok=True)
            git("init", "-q")
            git("remote", "add", "origin", url)
        else:
            git("remote", "set-url", "origin", url)
        git("fetch", "-q", "--depth", "1", "--no-tags", "origin", ref or "HEAD")
        git("-c", "core.symlinks=false", "checkout", "-q", "--force", "--detach", "FETCH_HEAD")
    return folder


def version_dumps(folder):
    """(dumps folder, [(version, its ic902-main.bin)]) from the repository's dumps/ folder (the top one, else the first
    folder named dumps), one entry per version folder; None when the repository has no dumps/ folder"""
    folder = Path(folder)
    top = folder / "dumps"
    if not top.is_dir() or top.is_symlink():
        nested = sorted(path for path in folder.rglob("dumps") if path.is_dir() and not path.is_symlink()
                        and ".git" not in path.relative_to(folder).parts)
        if not nested:
            return None
        top = nested[0]
    versions = [(path.name, path / "ic902-main.bin") for path in sorted(top.iterdir())
                if path.is_dir() and not path.is_symlink() and not path.name.startswith(".")]
    return top, versions


def dumps_in(folder):
    """({build: path}, lfs) for the untouched 64 KB stock dumps anywhere in folder, picked by firmware CRC; lfs is
    True when it holds Git LFS pointer files instead of contents"""
    found, lfs = {}, False
    for path in sorted(Path(folder).rglob("*")):
        if ".git" in path.relative_to(folder).parts or path.is_symlink() or not path.is_file():
            continue
        try:
            size = path.stat().st_size
            if size == 0x10000:
                build = DUMP.build_of(path.read_bytes())
                if build and build not in found:
                    found[build] = path
            elif size < 200 and path.read_bytes().startswith(b"version https://git-lfs"):
                lfs = True
        except OSError:
            continue                          # unreadable (a path too long for this system, say): not a dump we can use
    return found, lfs


KEYS = {"feb": "ADT_TEST_STOCK_DUMP", "dec": "ADT_TEST_STOCK_DUMP_DEC"}


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("executable")
    parser.add_argument("--work", type=Path, help="fresh output directory; default creates a unique tests/work/run-* folder")
    parser.add_argument("--run-id", help="invocation identifier recorded in the result receipt")
    parser.add_argument("--build", choices=("feb", "dec", "all"), default="all",
                        help="stock build(s) to test; all (default) tests both at the same time")
    parser.add_argument("--uncapped", action="store_true", help="no CPU cap: run as fast as possible (GitHub always uses it)")
    modes = parser.add_mutually_exclusive_group()
    modes.add_argument("--smoke", action="store_true")
    modes.add_argument("--require-firmware", action="store_true")
    modes.add_argument("--require-all-firmware", action="store_true")
    args = parser.parse_args()
    builds = ("feb", "dec") if args.build == "all" else (args.build,)
    if args.require_all_firmware and args.build != "all":
        parser.error("--require-all-firmware tests both builds: leave out --build")
    if args.require_firmware and "feb" not in builds:
        parser.error("--require-firmware needs February: use --build feb or all")
    try:
        cap = CPUCAP.Cap(args.uncapped)       # before the suite: every process it starts is inside the cap
    except CPUCAP.CapError as error:
        sys.exit(str(error))
    try:
        try:
            suite = Suite(args.executable, args.work, args.run_id, cap=cap, uncapped=args.uncapped)
        except FileExistsError:
            parser.error("choose a fresh --work directory: " + str(args.work))
        print("CPU: " + cap.text, flush=True)
        suite.smoke()
        config = settings()
        if not args.smoke:
            token = config.get("ADT_TEST_DUMP_TOKEN", "")
            sources = {build: config[KEYS[build]] for build in builds if config.get(KEYS[build], "")}
            repository = config.get("ADT_TEST_DUMPS_REPO", "")
            failed = set()
            others = []                       # versions in dumps/ without a stored fixture: digital-twin checks
            if repository:
                print("== stock dumps from " + repository, flush=True)
                try:
                    folder = sync_repository(repository, config.get("ADT_TEST_DUMPS_REF", ""), token)
                    layout, lfs = version_dumps(folder), False
                    if layout is not None:
                        top, versions = layout
                        where = top.relative_to(folder).as_posix()
                        suite.check(len(versions) > 0, where + "/ holds %d firmware version folder(s)" % len(versions))
                        for version, path in versions:
                            shown = where + "/" + version + "/ic902-main.bin"
                            data = path.read_bytes() if path.is_file() and not path.is_symlink() else b""
                            if len(data) != 0x10000:
                                pointer = data.startswith(b"version https://git-lfs")
                                suite.check(False, version + ": " + shown + (" is a Git LFS pointer file, which is not fetched"
                                            if pointer else " is missing or not 64 KB"))
                                continue
                            build, own = DUMP.build_of(data), DUMP.build_string(data)
                            if own is None:
                                suite.check(False, version + ": " + shown + " names no firmware version: not an untouched stock dump")
                                continue
                            if own != version:
                                suite.check(False, version + ": " + shown + " is firmware " + own + ": its folder must be named " + own)
                                continue
                            if build is None and version in DUMP.VERSIONS.values():
                                suite.check(False, version + ": " + shown + " fails the firmware CRC: not an untouched stock dump")
                                continue
                            if build is not None:
                                if build in builds and build not in sources:
                                    sources[build] = str(path)
                                    suite.check(True, version + ": untouched " + build + " stock dump verified by firmware CRC (" + shown + ")")
                            elif args.build == "all":
                                others.append((version, path))
                                suite.check(True, version + ": a version without a stored fixture (" + shown + "), digital-twin checks")
                    else:
                        found, lfs = dumps_in(folder)
                        for build in builds:
                            if build not in sources and build in found:
                                sources[build] = str(found[build])
                                suite.check(True, build + ": stock dump found in the repository (" +
                                            found[build].relative_to(folder).as_posix() + ")")
                    for build in builds:
                        if build not in sources:
                            failed.add(build)
                            suite.check(False, build + ": ADT_TEST_DUMPS_REPO holds no untouched 64 KB " + build +
                                        " stock dump (dumps/" + DUMP.VERSIONS[build] + "/ic902-main.bin)" +
                                        (" (it holds Git LFS pointer files, which are not fetched)" if lfs else ""))
                except Exception as error:
                    failed.update(build for build in builds if build not in sources)
                    suite.check(False, "ADT_TEST_DUMPS_REPO could not be cloned or updated (%s: %s)"
                                % (type(error).__name__, error))
            threads = []
            for build in builds:
                if build in sources:
                    threads.append(threading.Thread(target=suite.firmware, args=(build, sources[build], token)))
                elif build in failed:
                    continue
                elif args.require_all_firmware or (args.require_firmware and build == "feb"):
                    suite.check(False, KEYS[build] + " or ADT_TEST_DUMPS_REPO is required: firmware verification is incomplete")
                else:
                    suite.skip(KEYS[build] + " and ADT_TEST_DUMPS_REPO missing; " + build + " firmware was not tested")
            for version, path in others:
                threads.append(threading.Thread(target=suite.other_version, args=(version, path)))
            for thread in threads:
                thread.start()
            for thread in threads:
                thread.join()
        suite.write_receipt()
        print("== %d passed, %d failed, %d skipped" % (suite.passed, suite.failed, suite.skipped))
        print("logs: " + str(suite.work))
        return 1 if suite.failed else 0
    finally:
        cap.close()


if __name__ == "__main__":
    sys.exit(main())
