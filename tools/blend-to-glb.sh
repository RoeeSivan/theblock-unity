#!/usr/bin/env bash
# Export a .blend to a .glb this project can import.
#
#   tools/blend-to-glb.sh <in.blend> <out.glb>
#
# For the districts the web build never shipped a source for - the parking lot and Reichman
# are hand-modelled, so there is no Sketchfab original to fall back on and the shipped GLB is
# a Draco/webp download build. Re-exporting from the .blend gives Unity raw geometry to do
# its own per-platform compression on (CLAUDE.md port rule 3).
#
# The .blend is READ ONLY here. Blender runs with -b (no UI) and never saves, so this cannot
# touch the game repo beyond reading it (port rule 4).
set -euo pipefail

BLENDER="${BLENDER:-/Applications/Blender.app/Contents/MacOS/Blender}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ $# -ne 2 ]; then
  echo "usage: tools/blend-to-glb.sh <in.blend> <out.glb>" >&2
  exit 2
fi

IN="$1"
OUT="$2"

if [ ! -x "$BLENDER" ]; then
  echo "blend-to-glb: no Blender at $BLENDER" >&2
  echo "              set BLENDER to the executable inside Blender.app and retry" >&2
  exit 2
fi

if [ ! -f "$IN" ]; then
  echo "blend-to-glb: no such .blend: $IN" >&2
  exit 2
fi

mkdir -p "$(dirname "$OUT")"

# Blender writes its banner and addon chatter to stdout; keep only our own lines and errors.
"$BLENDER" -b "$IN" -P "$HERE/blend-to-glb.py" -- "$OUT" 2>&1 |
  grep -E "blend-to-glb:|^  |Error|Traceback" || true

[ -f "$OUT" ] || { echo "blend-to-glb: export produced no file" >&2; exit 1; }
echo "blend-to-glb: $(du -h "$OUT" | cut -f1) at $OUT"
