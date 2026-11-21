#!/usr/bin/env python3
"""CPU cap for the test runners (run-regressions.py and run-tests.py).

Capped (the default): every process a test run starts, together with those of every other capped test run on this
computer, uses at most CAP_PERCENT of the computer's total CPU time (all cores together). It does not limit how many
processes run: they all start at once and share the cap. --uncapped turns it off; GitHub always passes it.
Start every child process through Cap.popen / Cap.run so it is inside the cap.

Windows: one named job object with a hard CPU rate cap. A runner outside any job joins it itself, so what it starts is
inside too. A runner inside another program's job (the Python launcher puts python.exe in one) cannot join a second
job tree, so it starts each child paused, puts the child in the shared job and then lets it run; this needs that job
to let children break away, which the launcher's does. If neither works, the run gets a job of its own with an equal
share of the cap among the live capped runs, which are counted through lock files.
Linux and macOS: the runner reads its child processes' CPU time every 0.1 s and pauses them (SIGSTOP, then SIGCONT)
while they are over their share, an equal share among the live capped runs.

  python -I tests/cpucap.py selftest PERCENT   runs a busy child under a private cap of PERCENT and prints the
                                                share of one core it got (test-infrastructure.py uses it)
"""
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import threading
import time
import uuid

CAP_PERCENT = 50
ENV = "ADT_TEST_CPU_CAP"          # tells child processes that a parent run already applies the cap
JOB_NAME = "Local\\AnargonsTabletToolsTestCpuCap"
REGISTRY = Path(tempfile.gettempdir()) / "anargons-tablet-tools-test-cpu"


class CapError(Exception):
    pass


def _try_lock(handle):
    """Exclusive, non-blocking lock on an open file; False if another run holds it."""
    try:
        if os.name == "nt":
            import msvcrt
            handle.seek(0)
            msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
        else:
            import fcntl
            fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        return True
    except OSError:
        return False


def live_runs(registry=REGISTRY):
    """Capped runs alive now: lock files whose lock is held. Stale files (their run ended) are removed."""
    count = 0
    for path in registry.glob("*.lock"):
        try:
            handle = open(path, "r+")
        except OSError:
            continue
        held = not _try_lock(handle)
        handle.close()
        if held:
            count += 1
        else:
            try:
                path.unlink()
            except OSError:
                pass
    return max(1, count)


def cpu_seconds(text):
    """ps TIME ([[dd-]hh:]mm:ss[.ss]) to seconds."""
    days = 0
    if "-" in text:
        day, text = text.split("-", 1)
        days = int(day)
    seconds = 0.0
    for part in text.split(":"):
        seconds = seconds * 60 + float(part)
    return days * 86400 + seconds


def descendants(tree, root):
    """Every process below root in {pid: (ppid, cpu seconds)}."""
    children = {}
    for pid, (ppid, _) in tree.items():
        children.setdefault(ppid, []).append(pid)
    found, stack = [], list(children.get(root, ()))
    while stack:
        pid = stack.pop()
        if pid != root and pid not in found:
            found.append(pid)
            stack.extend(children.get(pid, ()))
    return found


def _linux_tree():
    tick = os.sysconf("SC_CLK_TCK")
    tree = {}
    for name in os.listdir("/proc"):
        if not name.isdigit():
            continue
        try:
            with open("/proc/" + name + "/stat", "rb") as handle:
                text = handle.read().decode("ascii", "replace")
        except OSError:
            continue
        fields = text[text.rfind(")") + 2:].split()      # from field 3 (state): ppid = 4, utime = 14, stime = 15
        tree[int(name)] = (int(fields[1]), (int(fields[11]) + int(fields[12])) / tick)
    return tree


def _ps_tree():
    output = subprocess.run(["ps", "-A", "-o", "pid=,ppid=,time="], stdout=subprocess.PIPE,
                            stderr=subprocess.DEVNULL, text=True, timeout=10).stdout
    tree = {}
    for line in output.splitlines():
        parts = line.split()
        if len(parts) == 3:
            tree[int(parts[0])] = (int(parts[1]), cpu_seconds(parts[2]))
    return tree


class Cap:
    """Start once per run, before any child process. text says what was applied; shared is True when the CPU
    accounting covers every capped run (the shared Windows job)."""

    def __init__(self, uncapped, percent=CAP_PERCENT, job_name=JOB_NAME, registry=REGISTRY):
        self.percent, self.job_name, self.registry = percent, job_name, registry
        self.applied = self.shared = False
        self._stop = threading.Event()
        self._lock = self._job = self._thread = self._children_job = None
        self._child_flags = 0
        if uncapped:
            self.text = "uncapped"
            if os.name == "nt":
                try:
                    self._windows(rate=False)      # a job with no cap, only to count the CPU time used
                except Exception:
                    self._job = self._children_job = None
            return
        if os.environ.get(ENV):
            self.text = os.environ[ENV] + " (applied by the run that started this one)"
            return
        self._register()
        try:
            if os.name == "nt":
                self._windows()
            else:
                self._posix()
        except Exception as error:
            raise CapError("the CPU cap could not be applied (%s: %s); run with --uncapped to run without it"
                           % (type(error).__name__, error))
        self.applied = True
        os.environ[ENV] = self.text

    def _register(self):
        self.registry.mkdir(parents=True, exist_ok=True)
        path = self.registry / ("%d-%s.lock" % (os.getpid(), uuid.uuid4().hex[:12]))
        self._lock = open(path, "w+")
        if not _try_lock(self._lock):
            raise CapError("could not lock " + str(path))

    def share(self):
        return self.percent / live_runs(self.registry)

    # ---------------------------------------------------------------- starting child processes
    def popen(self, args, **kwargs):
        """subprocess.Popen, with the child inside the cap."""
        if self._children_job is None:
            return subprocess.Popen(args, **kwargs)
        kwargs["creationflags"] = kwargs.get("creationflags", 0) | self._child_flags
        process = subprocess.Popen(args, **kwargs)
        handle = int(process._handle)
        if not self._kernel.AssignProcessToJobObject(self._children_job, handle):
            error = self._ctypes.get_last_error()
            process.kill()
            process.wait()
            raise CapError("could not put a test process under the CPU cap (Windows error %d)" % error)
        status = self._ntdll.NtResumeProcess(handle)
        if status != 0:
            process.kill()
            process.wait()
            raise CapError("could not resume a test process (status 0x%08X)" % (status & 0xFFFFFFFF))
        return process

    def run(self, args, input=None, timeout=None, check=False, **kwargs):
        """subprocess.run, with the child inside the cap."""
        if self._children_job is None:
            return subprocess.run(args, input=input, timeout=timeout, check=check, **kwargs)
        if input is not None:
            kwargs["stdin"] = subprocess.PIPE
        with self.popen(args, **kwargs) as process:
            try:
                out, err = process.communicate(input, timeout=timeout)
            except subprocess.TimeoutExpired:
                process.kill()
                process.communicate()
                raise
        if check and process.returncode:
            raise subprocess.CalledProcessError(process.returncode, args, out, err)
        return subprocess.CompletedProcess(args, process.returncode, out, err)

    # ---------------------------------------------------------------- Windows: job object
    def _windows(self, rate=True):
        import ctypes
        from ctypes import wintypes
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        ntdll = ctypes.WinDLL("ntdll")
        kernel.CreateJobObjectW.restype = wintypes.HANDLE
        kernel.CreateJobObjectW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR]
        kernel.SetInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]
        kernel.QueryInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD,
                                                     ctypes.c_void_p]
        kernel.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]
        kernel.IsProcessInJob.argtypes = [wintypes.HANDLE, wintypes.HANDLE, ctypes.POINTER(wintypes.BOOL)]
        kernel.GetCurrentProcess.restype = wintypes.HANDLE
        kernel.CloseHandle.argtypes = [wintypes.HANDLE]
        ntdll.NtResumeProcess.argtypes = [wintypes.HANDLE]
        ntdll.NtResumeProcess.restype = ctypes.c_long
        self._kernel, self._ntdll, self._ctypes = kernel, ntdll, ctypes

        class Rate(ctypes.Structure):          # JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
            _fields_ = [("flags", wintypes.DWORD), ("rate", wintypes.DWORD)]

        class Basic(ctypes.Structure):         # JOBOBJECT_BASIC_LIMIT_INFORMATION
            _fields_ = [("user", ctypes.c_int64), ("job_user", ctypes.c_int64), ("flags", wintypes.DWORD),
                        ("min_ws", ctypes.c_size_t), ("max_ws", ctypes.c_size_t), ("processes", wintypes.DWORD),
                        ("affinity", ctypes.c_size_t), ("priority", wintypes.DWORD), ("scheduling", wintypes.DWORD)]

        class Extended(ctypes.Structure):      # JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            _fields_ = [("basic", Basic), ("io", ctypes.c_uint64 * 6), ("process_memory", ctypes.c_size_t),
                        ("job_memory", ctypes.c_size_t), ("peak_process", ctypes.c_size_t),
                        ("peak_job", ctypes.c_size_t)]

        def check(ok, what):
            if not ok:
                raise OSError(ctypes.get_last_error(), what)

        def set_rate(job, percent):
            # enable + hard cap; the rate is in 1/100 % of all processors together
            value = Rate(0x1 | 0x4, max(1, min(10000, int(percent * 100))))
            check(kernel.SetInformationJobObject(job, 15, ctypes.byref(value), ctypes.sizeof(value)), "cpu rate")

        def create(name):
            job = kernel.CreateJobObjectW(None, name)
            check(job, "CreateJobObject")
            existed = ctypes.get_last_error() == 183
            if not existed:
                limits = Extended()
                limits.basic.flags = 0x2000    # kill what is left in the job when the last run closes it
                check(kernel.SetInformationJobObject(job, 9, ctypes.byref(limits), ctypes.sizeof(limits)), "limits")
            return job, existed

        me = kernel.GetCurrentProcess()
        inside = wintypes.BOOL()
        check(kernel.IsProcessInJob(me, None, ctypes.byref(inside)), "IsProcessInJob")
        breakaway = 0
        if inside.value:
            limits = Extended()
            if kernel.QueryInformationJobObject(None, 9, ctypes.byref(limits), ctypes.sizeof(limits), None):
                if limits.basic.flags & 0x1000:          # children leave that job by themselves
                    breakaway = 0x4
                elif limits.basic.flags & 0x800:         # children may leave it when asked
                    breakaway = 0x4 | 0x01000000
        if rate:
            job, existed = create(self.job_name)
            if not existed:
                set_rate(job, self.percent)
            shared = "%d %% of all CPUs, shared by every capped test run on this PC" % self.percent
        else:
            job, _ = create(None)
            shared = "uncapped"
        if not inside.value and kernel.AssignProcessToJobObject(job, me):
            self._job, self.text, self.shared = job, shared, rate
            return
        if breakaway:
            # CREATE_SUSPENDED (+ CREATE_BREAKAWAY_FROM_JOB): each child is put in the job before it runs
            self._job = self._children_job = job
            self._child_flags, self.text, self.shared = breakaway, shared, rate
            return
        kernel.CloseHandle(job)
        if not rate:
            self._job = None                   # no CPU accounting; the run itself is not affected
            return
        job, _ = create(None)
        set_rate(job, self.share())
        check(kernel.AssignProcessToJobObject(job, me), "AssignProcessToJobObject")
        self._job = job
        self.text = "%d %% of all CPUs, split equally between the capped test runs on this PC" % self.percent
        self._thread = threading.Thread(target=self._rebalance, args=(set_rate,), daemon=True)
        self._thread.start()

    def _rebalance(self, set_rate):
        while not self._stop.wait(1.0):
            try:
                set_rate(self._job, self.share())
            except OSError:
                pass

    # ---------------------------------------------------------------- Linux, macOS: pause when over the share
    def _posix(self):
        self._tree = _linux_tree if os.path.exists("/proc/self/stat") else _ps_tree
        self._tree()                           # fails here, not later, if the process table cannot be read
        self.text = "%d %% of all CPUs, split equally between the capped test runs on this computer" % self.percent
        self._thread = threading.Thread(target=self._govern, daemon=True)
        self._thread.start()

    def _govern(self):
        me, cpus = os.getpid(), os.cpu_count() or 1
        last, credit, share, share_at = {}, 0.0, self.percent / 100.0, 0.0
        before = time.monotonic()
        while not self._stop.wait(0.1):
            now = time.monotonic()
            if now >= share_at:
                share, share_at = self.share() / 100.0, now + 1.0
            try:
                tree = self._tree()
            except Exception:
                continue
            pids = descendants(tree, me)
            used = sum(max(0.0, tree[pid][1] - last.get(pid, 0.0)) for pid in pids)
            last = dict((pid, tree[pid][1]) for pid in pids)
            allowed = share * cpus             # CPU seconds per second
            credit = min(credit + allowed * (now - before) - used, allowed * 0.2)
            before = now
            if credit < 0:
                pause = min(-credit / allowed, 1.0)
                self._signal(pids, signal.SIGSTOP)
                try:
                    self._stop.wait(pause)
                finally:
                    self._signal(pids, signal.SIGCONT)
                credit += allowed * pause
                before = time.monotonic()

    @staticmethod
    def _signal(pids, number):
        for pid in pids:
            try:
                os.kill(pid, number)
            except OSError:
                pass

    def used_seconds(self):
        """CPU seconds used so far by the capped processes (Windows: the whole job, so with a shared job every capped
        run in it; Linux, macOS: this run's finished child processes), or None if this run cannot tell."""
        if os.name == "nt":
            if self._job is None:
                return None
            info = (self._ctypes.c_int64 * 6)()   # JOBOBJECT_BASIC_ACCOUNTING_INFORMATION, 100 ns units first
            if not self._kernel.QueryInformationJobObject(self._job, 1, info, 48, None):
                return None
            return (info[0] + info[1]) / 1e7
        import resource
        usage = resource.getrusage(resource.RUSAGE_CHILDREN)
        return usage.ru_utime + usage.ru_stime

    def close(self):
        self._stop.set()
        if self._thread is not None:
            self._thread.join(2.0)
        if self._lock is not None:
            path = Path(self._lock.name)
            self._lock.close()
            try:
                path.unlink()
            except OSError:
                pass
            self._lock = None


def selftest(percent):
    """Busy child under a private cap: prints the share of one core it got over 3 s."""
    private = Path(tempfile.mkdtemp(prefix="cpucap-selftest-"))
    os.environ.pop(ENV, None)
    cap = Cap(False, percent, JOB_NAME + "Selftest" + uuid.uuid4().hex[:8], private)
    try:
        busy = "import time\nt = time.monotonic()\nwhile time.monotonic() - t < 3: pass\nprint(time.process_time())\n"
        result = cap.run([sys.executable, "-I", "-c", busy], stdout=subprocess.PIPE, text=True, timeout=60)
        print("allowed %.3f got %.3f (%s)" % (min(1.0, percent / 100.0 * (os.cpu_count() or 1)),
                                              float(result.stdout.strip()) / 3.0, cap.text))
    finally:
        cap.close()
    return 0


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "selftest":
        sys.exit(selftest(float(sys.argv[2])))
    sys.exit(__doc__)
