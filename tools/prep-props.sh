#!/usr/bin/env bash
# Prep the three U35h street props from their raw Sketchfab downloads.
#
#   tools/prep-props.sh [source-dir]      default: ~/TheBlockSource/props
#
# The raw files are 35 MB with 4096² textures and are NOT in the repo (LFS is shared with the
# original and already full) - they live beside the district sources in ~/TheBlockSource. What
# this writes into Assets/Models/Props/ is ~1 MB each: transforms baked, origin at the base,
# textures ≤ 1024², the bin decimated. Re-run after changing a budget below.
set -euo pipefail

BLENDER="${BLENDER:-/Applications/Blender.app/Contents/MacOS/Blender}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
SRC="${1:-$HOME/TheBlockSource/props}"
OUT="$ROOT/Assets/Models/Props"

if [ ! -x "$BLENDER" ]; then
  echo "prep-props: no Blender at $BLENDER (set BLENDER)" >&2
  exit 2
fi
mkdir -p "$OUT"

#        input                         output          scale  max_tris  max_tex
prep() {
  local in="$SRC/$1" out="$OUT/$2"
  [ -f "$in" ] || { echo "prep-props: missing $in" >&2; exit 2; }
  "$BLENDER" -b -P "$HERE/prep-props.py" -- "$in" "$out" "$3" "$4" "$5" 2>&1 |
    grep -E "prep-props:|^  |Error|Traceback" || true
  [ -f "$out" ] || { echo "prep-props: no output for $1" >&2; exit 1; }
  echo "prep-props: $(du -h "$out" | cut -f1) at $out"
}

prep modern_bench_1.glb          bench.glb      1.0  0     1024
prep public_trash_bin_1.glb      trash-bin.glb  1.0  2500  1024
prep traffic_cone_game_ready.glb cone.glb       0.025 0    1024   # file is 29 m tall (Sketchfab x100 root); 0.025 → 0.74 m
