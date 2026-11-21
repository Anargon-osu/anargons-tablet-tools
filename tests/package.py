#!/usr/bin/env python3
"""Package a published binary and smoke-test the extracted release contents."""
import argparse
import base64
import gzip
import io
import json
import urllib.request
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import re
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parent.parent


def download(url, maximum):
    with urllib.request.urlopen(url, timeout=120) as response:
        if not response.geturl().startswith("https://"):
            raise ValueError("Runtime package requests must remain on HTTPS")
        content = response.read(maximum + 1)
    if len(content) > maximum:
        raise ValueError("Runtime metadata/package exceeds its size limit")
    return content


def get_json(url):
    content = download(url, 2 * 1024 * 1024)
    if content[:2] == b"\x1f\x8b":
        content = gzip.decompress(content)
    return json.loads(content)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("executable", type=Path)
    parser.add_argument("rid", choices=("win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"))
    parser.add_argument("--runtime-version", help="exact bundled .NET runtime version; omit for the Windows Framework build")
    args = parser.parse_args()
    binary = args.executable.resolve()
    output = ROOT / "packages"
    output.mkdir(exist_ok=True)
    name = "anargons-tablet-tools-" + args.rid
    with tempfile.TemporaryDirectory(prefix="adt package-") as temp:
        temp = Path(temp)
        stage = temp / name
        stage.mkdir()
        target = stage / ("AnargonsTabletTools.exe" if args.rid.startswith("win-") else "AnargonsTabletTools")
        shutil.copy2(binary, target)
        target.chmod(0o755)
        for filename in ("README.md", "LICENSE", ".env.example"):
            shutil.copy2(ROOT / filename, stage / filename)
        if args.runtime_version:
            if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", args.runtime_version):
                parser.error("--runtime-version must be an exact numeric version")
            nuget = Path(os.environ.get("NUGET_PACKAGES", str(Path.home() / ".nuget/packages")))
            notices = stage / "licenses"
            notices.mkdir()
            for kind in ("runtime", "host"):
                pack = nuget / ("microsoft.netcore.app." + kind + "." + args.rid) / args.runtime_version
                filenames = ("LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT")
                if not all((pack / notice).is_file() for notice in filenames):
                    # SDK-installed host packs may lack cache notices; retrieve the exact
                    # official NuGet package and verify its published SHA-512 before reading them.
                    package_name = "microsoft.netcore.app." + kind + "." + args.rid
                    base = "https://api.nuget.org/v3/"
                    entry = get_json(base + "registration5-gz-semver2/" + package_name + "/" + args.runtime_version + ".json")["catalogEntry"]
                    catalog = get_json(entry) if isinstance(entry, str) else entry
                    if catalog.get("packageHashAlgorithm") != "SHA512":
                        raise ValueError("Unexpected NuGet checksum algorithm")
                    url = base + "flatcontainer/" + package_name + "/" + args.runtime_version + "/" + package_name + "." + args.runtime_version + ".nupkg"
                    content = download(url, 512 * 1024 * 1024)
                    if base64.b64encode(hashlib.sha512(content).digest()).decode("ascii") != catalog["packageHash"]:
                        raise ValueError("NuGet package checksum mismatch: " + package_name)
                    with zipfile.ZipFile(io.BytesIO(content)) as runtime_package:
                        for notice in filenames:
                            (notices / ("dotnet-" + kind + "-" + notice)).write_bytes(runtime_package.read(notice))
                else:
                    for notice in filenames:
                        shutil.copy2(pack / notice, notices / ("dotnet-" + kind + "-" + notice))
        (stage / "SHA256SUMS.txt").write_text("".join(
            hashlib.sha256(path.read_bytes()).hexdigest() + "  " + path.relative_to(stage).as_posix() + "\n"
            for path in sorted(stage.rglob("*")) if path.is_file()), encoding="utf-8")
        archive = output / (name + ".zip")
        with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as package:
            for path in sorted(stage.rglob("*")):
                if path.is_file():
                    package.write(path, name + "/" + path.relative_to(stage).as_posix())
        with zipfile.ZipFile(archive) as package:
            package.extractall(temp / "extracted")
        extracted = temp / "extracted" / name
        executable = extracted / target.name
        executable.chmod(0o755)
        env = os.environ.copy()
        env["DOTNET_ROOT"] = str(temp / "no-runtime")
        env["DOTNET_MULTILEVEL_LOOKUP"] = "0"
        env["ADT_STATE_DIR"] = str(temp / "state")
        for command in ([str(executable), "--help"], [str(executable), "twin", "--selftest"]):
            subprocess.run(command, cwd=extracted, env=env, check=True, stdin=subprocess.DEVNULL, timeout=60)
    (output / (archive.name + ".sha256")).write_text(
        hashlib.sha256(archive.read_bytes()).hexdigest() + "  " + archive.name + "\n", encoding="ascii")
    print("Packaged and smoke-tested " + str(archive))


if __name__ == "__main__":
    main()