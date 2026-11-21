#!/bin/sh
# Linux / macOS: builds one self-contained ./AnargonsTabletTools executable, next to this file (needs the .NET 10 SDK).
# Needs the separate gd32emu emulator source - see DOCS/EMULATOR.md for where it is looked for.
set -e
cd "$(dirname "$0")"
if ! command -v dotnet >/dev/null 2>&1; then
  echo "Building from source needs the .NET 10 SDK. Download a prebuilt package to run without installing the SDK."
  exit 1
fi
# GD32EMU_DIR from .env (see .env.example) unless it is set in the environment
if [ -z "${GD32EMU_DIR:-}" ] && [ -f .env ]; then
  GD32EMU_DIR=$(sed -n 's/^[[:space:]]*GD32EMU_DIR=[[:space:]]*//p' .env | tail -n 1 | tr -d '"\r')
fi
case "$(uname -s)-$(uname -m)" in
  Linux-x86_64) RID=linux-x64 ;; Linux-aarch64) RID=linux-arm64 ;;
  Darwin-x86_64) RID=osx-x64 ;; Darwin-arm64) RID=osx-arm64 ;;
  *) echo "Unsupported platform: $(uname -s) $(uname -m). Supported: Linux x64/arm64, macOS Intel/Apple Silicon."; exit 1 ;;
esac
EMU="${GD32EMU_DIR:-}"
[ -z "$EMU" ] && [ -f gd32emu/gd32emu.cs ] && EMU=gd32emu
[ -z "$EMU" ] && [ -f ../gd32emu/gd32emu.cs ] && EMU=../gd32emu
if [ ! -f "$EMU/gd32emu.cs" ]; then
  echo "The gd32emu source was not found. Keep the separate projects side by side,"
  echo "or set GD32EMU_DIR to its source folder. See DOCS/EMULATOR.md."
  exit 1
fi
echo "Using the emulator from $EMU"
dotnet publish AnargonsTabletTools.csproj -c Release -r "$RID" -p:Gd32EmuDir="$EMU/" -o .publish
cp .publish/AnargonsTabletTools ./AnargonsTabletTools
./AnargonsTabletTools twin --selftest
echo "built ./AnargonsTabletTools"
