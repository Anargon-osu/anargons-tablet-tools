#!/usr/bin/env bash
# Compatibility entry point. The Python runner also works directly on Windows.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
exec "${PYTHON:-python3}" -I "$ROOT/tests/run-tests.py" "$@"
