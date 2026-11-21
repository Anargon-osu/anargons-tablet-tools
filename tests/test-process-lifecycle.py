#!/usr/bin/env python3
"""Compile a fake child tree and verify Windows parent exit, Ctrl+C and forced termination."""
import ctypes
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time


def main():
    if os.name != "nt":
        print("SKIP Windows Job Object lifecycle checks (requires Windows)")
        return 0
    root = Path(__file__).resolve().parent.parent
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.OpenProcess.argtypes = [ctypes.c_uint32, ctypes.c_int, ctypes.c_uint32]
    kernel.OpenProcess.restype = ctypes.c_void_p
    kernel.WaitForSingleObject.argtypes = [ctypes.c_void_p, ctypes.c_uint32]
    kernel.CloseHandle.argtypes = [ctypes.c_void_p]
    kernel.TerminateProcess.argtypes = [ctypes.c_void_p, ctypes.c_uint32]

    def running(pid):
        handle = kernel.OpenProcess(0x100000, False, pid)
        if not handle:
            return False
        try:
            return kernel.WaitForSingleObject(handle, 0) == 0x102
        finally:
            kernel.CloseHandle(handle)

    def stop(pid):
        handle = kernel.OpenProcess(1, False, pid)
        if handle:
            try:
                kernel.TerminateProcess(handle, 99)
            finally:
                kernel.CloseHandle(handle)

    with tempfile.TemporaryDirectory(prefix="deco-process-lifetime-") as temporary:
        work = Path(temporary)
        compiler = Path(os.environ["WINDIR"]) / "Microsoft.NET/Framework64/v4.0.30319/csc.exe"
        if not compiler.is_file():
            compiler = compiler.parent.parent / "Framework/v4.0.30319/csc.exe"
        executable = work / "lifetime.exe"
        sources = [root.parent / "gd32emu/gd32emu.cs", *sorted(root.glob("atf_*.cs")), root / "tests/process-lifecycle.cs"]
        subprocess.run([str(compiler), "/nologo", "/langversion:5", "/optimize+",
                        "/main:AnargonsTabletTools.Tests.ProcessLifecycle", "/out:" + str(executable),
                        *map(str, sources)], check=True, timeout=120)
        for mode in ("exit", "kill", "cancel", "cancel-gate"):
            folder = work / mode
            folder.mkdir()
            hidden = subprocess.STARTUPINFO()
            hidden.dwFlags = subprocess.STARTF_USESHOWWINDOW
            hidden.wShowWindow = 0
            pids = []
            with (folder / "output.txt").open("w") as log:
                parent = subprocess.Popen([str(executable), mode, str(folder)], stdin=subprocess.DEVNULL,
                                          stdout=log, stderr=log, startupinfo=hidden,
                                          creationflags=subprocess.CREATE_NEW_CONSOLE)
                try:
                    deadline = time.monotonic() + 15
                    while not (folder / "ready").is_file():
                        if parent.poll() is not None or time.monotonic() > deadline:
                            raise AssertionError(mode + ": parent failed before ready: " + (folder / "output.txt").read_text())
                        time.sleep(.02)
                    pids = [int((folder / (name + ".pid")).read_text()) for name in ("child", "leaf")]
                    if mode == "kill":
                        parent.kill()
                    parent.wait(timeout=10)
                    deadline = time.monotonic() + 5
                    while any(running(pid) for pid in pids) and time.monotonic() < deadline:
                        time.sleep(.02)
                    assert not any(running(pid) for pid in pids), mode + ": orphan child or grandchild"
                    if mode in ("exit", "cancel-gate"):
                        assert parent.returncode == 0, mode + ": " + (folder / "output.txt").read_text()
                    if mode == "cancel-gate":
                        assert (folder / "gate-passed").is_file(), "shutdown did not reject a subsequent command"
                    print("PASS process lifetime: " + mode, flush=True)
                finally:
                    if parent.poll() is None:
                        parent.kill()
                        parent.wait(timeout=10)
                    for name in ("child", "leaf"):
                        pid_file = folder / (name + ".pid")
                        if pid_file.is_file():
                            stop(int(pid_file.read_text()))
    print("RESULT Windows process lifetime: 4 passed (fake child tree only)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
