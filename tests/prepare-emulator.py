#!/usr/bin/env python3
"""Fetch a reproducible emulator input for CI; never execute downloaded scripts."""
import base64
import hashlib
import os
from pathlib import Path
import re
import subprocess
import sys
import urllib.request

ROOT = Path(__file__).resolve().parent.parent
DEST = ROOT / "gd32emu"
# R45: with no GD32EMU_* setting, the emulator this release was tested with comes from its own repository
# (Anargon-osu/GD32EMU, a separate project next to this one) and must match this hash. The hash is of the file with
# LF line ends, the way git stores it (.gitattributes: *.cs eol=lf). Update both when the emulator changes.
DEFAULT_URL = "https://raw.githubusercontent.com/Anargon-osu/GD32EMU/HEAD/gd32emu.cs"
DEFAULT_SHA256 = "7affa45d0bdab5466cc40b11df3595c5d7dd46e9dbfdbc3c7eab2c57f234ae18"


def matches(data, digest):
    """the bytes as they are, or with CRLF line ends turned into LF (a checkout on Windows)"""
    return digest in (hashlib.sha256(data).hexdigest(), hashlib.sha256(data.replace(b"\r\n", b"\n")).hexdigest())


def git_env():
    """git without prompts; GD32EMU_TOKEN (for a private emulator repository) goes in a header given through the
    environment, so it is neither on the command line nor written into gd32emu/.git/config"""
    env = os.environ.copy()
    env["GIT_TERMINAL_PROMPT"] = "0"
    token = os.environ.get("GD32EMU_TOKEN", "")
    if token:
        index = int(env.get("GIT_CONFIG_COUNT", "0") or "0")
        env["GIT_CONFIG_KEY_%d" % index] = "http.extraHeader"
        env["GIT_CONFIG_VALUE_%d" % index] = "Authorization: Basic " + base64.b64encode(
            ("x-access-token:" + token).encode("utf-8")).decode("ascii")
        env["GIT_CONFIG_COUNT"] = str(index + 1)
    return env


def git(*args, cwd=ROOT):
    return subprocess.check_output(["git"] + list(args), cwd=cwd, text=True, env=git_env(),
                                   stdin=subprocess.DEVNULL).strip()


def main():
    source = DEST / "gd32emu.cs"
    digest = os.environ.get("GD32EMU_SHA256", "").lower()
    url = os.environ.get("GD32EMU_URL", "")
    if not url and not digest and not os.environ.get("GD32EMU_REPO"):
        url, digest = DEFAULT_URL, DEFAULT_SHA256
    if source.exists():
        entry = git("ls-files", "--stage", "gd32emu")
        if entry.startswith("160000 "):
            expected = entry.split()[1]
            if git("rev-parse", "HEAD", cwd=DEST) != expected:
                raise ValueError("The emulator submodule is not at its pinned commit.")
            print("Using pinned emulator submodule " + expected)
            return
        if not re.fullmatch(r"[0-9a-f]{64}", digest):
            raise ValueError("Vendored emulator source requires GD32EMU_SHA256.")
    elif url:
        if not url.startswith("https://") or not re.fullmatch(r"[0-9a-f]{64}", digest):
            raise ValueError("GD32EMU_URL must use HTTPS and have a pinned GD32EMU_SHA256.")
        DEST.mkdir(exist_ok=True)
        request = urllib.request.Request(url)
        if os.environ.get("GD32EMU_TOKEN"):
            request.add_header("Authorization", "Bearer " + os.environ["GD32EMU_TOKEN"])
        with urllib.request.urlopen(request, timeout=60) as response:
            if not response.geturl().startswith("https://"):
                raise ValueError("Emulator redirects must use HTTPS.")
            data = response.read(2 * 1024 * 1024 + 1)
        if len(data) > 2 * 1024 * 1024:
            raise ValueError("Emulator source exceeds 2 MB.")
        if not matches(data, digest):
            raise ValueError("The downloaded emulator does not match GD32EMU_SHA256" + (
                " (the pinned DEFAULT_SHA256 in tests/prepare-emulator.py)" if url == DEFAULT_URL else "") + ".")
        source.write_bytes(data)
    else:
        repository = os.environ.get("GD32EMU_REPO") or os.environ["GITHUB_REPOSITORY"].split("/")[0] + "/gd32emu"
        revision = os.environ.get("GD32EMU_REF", "")
        if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository):
            raise ValueError("GD32EMU_REPO must be owner/name.")
        if not re.fullmatch(r"[0-9a-fA-F]{40}", revision):
            raise ValueError("GD32EMU_REF must be an exact 40-character commit, not a moving branch or tag.")
        DEST.mkdir(exist_ok=True)
        git("init", cwd=DEST)
        git("remote", "add", "origin", "https://github.com/" + repository + ".git", cwd=DEST)
        git("fetch", "--depth", "1", "origin", revision, cwd=DEST)
        git("checkout", "--detach", "FETCH_HEAD", cwd=DEST)
        if git("rev-parse", "HEAD", cwd=DEST).lower() != revision.lower() or not source.is_file():
            raise ValueError("The fetched emulator does not match the pinned source.")
        print("Using emulator commit " + revision)
        return
    if not matches(source.read_bytes(), digest):
        raise ValueError("Emulator source does not match GD32EMU_SHA256.")
    print("Using emulator source SHA256 " + digest)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        # Download failures can contain private URLs. Report their type only.
        message = str(error) if isinstance(error, ValueError) else type(error).__name__
        sys.exit("Emulator setup failed: " + message)
