#!/usr/bin/env bash
# Checks SVGs against the Ace Barcade house style (docs/STYLE.md). Exits 1 on any problem.
# Usage: Art/check-style.sh [file or folder ...]   (default: all of Art/)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
command -v python3 >/dev/null || { echo "python3 not found" >&2; exit 2; }
exec python3 -I "$ROOT/Art/check_style.py" "$ROOT" "$@"
