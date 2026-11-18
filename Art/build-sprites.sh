#!/usr/bin/env bash
# Converts every SVG under Art/ into a PNG under Assets/Resources/Sprites/, keeping subfolders.
# Scale rule: author SVGs at 120 px = 1 Unity unit (a full-screen mock is 1080 x 1920).
# PNGs render at 1x and Unity imports them at 120 pixels per unit (SpriteImportSettings.cs).
# That is sharp on a 1080p phone and keeps textures small: the look is chunky on purpose.
# Art/AppIcon/ is the Android app icon, not a sprite: Art/build_app_icon.py renders it into
# Assets/AppIcon/ instead.
#
# Runs Art/check-style.sh first and stops if any SVG breaks the house style.
# Only re-renders an SVG when it is newer than its PNG.
# Usage: Art/build-sprites.sh [--force] [subfolder of Art/, e.g. Games/G001_PaddleRally]
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$ROOT/Art"
DST="$ROOT/Assets/Resources/Sprites"
FORCE=0
ONLY=""
for arg in "$@"; do
    case "$arg" in
        --force) FORCE=1 ;;
        *) ONLY="${arg%/}" ;;
    esac
done

command -v rsvg-convert >/dev/null || { echo "rsvg-convert not found (install librsvg)" >&2; exit 1; }

SEARCH="$SRC"
[[ -n "$ONLY" ]] && SEARCH="$SRC/$ONLY"
[[ -d "$SEARCH" ]] || { echo "No folder $SEARCH" >&2; exit 1; }

"$ROOT/Art/check-style.sh" "$SEARCH"

count=0
while IFS= read -r -d '' svg; do
    rel="${svg#"$SRC"/}"
    png="$DST/${rel%.svg}.png"
    if [[ $FORCE -eq 1 || ! -f "$png" || "$svg" -nt "$png" ]]; then
        mkdir -p "$(dirname "$png")"
        rsvg-convert --zoom=1 --format=png --output="$png" "$svg"
        echo "  $rel -> Assets/Resources/Sprites/${rel%.svg}.png"
        count=$((count + 1))
    fi
done < <(find "$SEARCH" -name '*.svg' -not -path '*/_*' -not -path "$SRC/AppIcon/*" -print0)

echo "Converted $count SVG(s)."

if [[ -z "$ONLY" || "$ONLY" == "AppIcon" ]]; then
    FLAGS=()
    [[ $FORCE -eq 1 ]] && FLAGS+=(--force)
    python3 "$SRC/build_app_icon.py" "$ROOT" "${FLAGS[@]}"
fi
