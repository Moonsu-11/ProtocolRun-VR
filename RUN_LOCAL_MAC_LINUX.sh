#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/backend"
PYTHON_BIN="${PYTHON_BIN:-python3.12}"
command -v "$PYTHON_BIN" >/dev/null || { echo 'Python 3.12 is required.' >&2; exit 1; }
if [ ! -x .venv/bin/python ]; then
  "$PYTHON_BIN" -m venv .venv
fi
.venv/bin/python -m pip install -r requirements.txt
exec .venv/bin/python run_local.py
