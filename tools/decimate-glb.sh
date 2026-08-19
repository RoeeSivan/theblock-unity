#!/usr/bin/env bash
# Reduce a .glb's triangle count in place, headless, with a backup taken first.
#
#   tools/decimate-glb.sh <in.glb> <target-triangles> [out.glb]
#
# U30b round 2 found `Assets/Models/Vehicles/jetski.glb` at 1,190,600 triangles with no LODGroup and
# two of them in the scene - a quarter of the world's geometry for two jetskis, on a frame that is
# GEOMETRY bound. A per-asset census then found the parked lot cars are worse still. This is the
# tool for both.
#
# Blender runs with -b (no UI), so this needs no Blender window open and does not disturb one that
# is. It never writes to the input unless the input IS the output, and in that case it takes a
# backup first and says where.
#
# ⚠ The result must be pixel-diffed at CLOSE RANGE before it is kept. A decimated hull looks
# identical from 40 m and can be visibly faceted at 3 m, which is exactly where a player parks.
set -euo pipefail

BLENDER="${BLENDER:-/Applications/Blender.app/Contents/MacOS/Blender}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ $# -lt 2 ]; then
  echo "usage: tools/decimate-glb.sh <in.glb> <target-triangles> [out.glb]" >&2
  exit 2
fi

IN="$1"
TARGET="$2"
OUT="${3:-$1}"
# PLANAR by default. COLLAPSE shattered the jetski's hull at close range and was reverted - see the
# long note in decimate-glb.py. Pass COLLAPSE explicitly only for organic shapes.
MODE="${4:-PLANAR}"

if [ ! -x "$BLENDER" ]; then
  echo "decimate-glb: no Blender at $BLENDER" >&2
  echo "              set BLENDER to the executable inside Blender.app and retry" >&2
  exit 2
fi

if [ ! -f "$IN" ]; then
  echo "decimate-glb: no such .glb: $IN" >&2
  exit 2
fi

# Overwriting the source is the normal case - Unity addresses the asset by path, so a decimated
# twin at a new path would mean re-pointing the prefab, the scene object and the texture rebind.
# The backup is what makes that safe, and it goes OUTSIDE the repo: this file is LFS-tracked and a
# 37 MB spare copy in the working tree is 37 MB of a 1 GiB free quota shared with the other repo.
if [ "$IN" = "$OUT" ]; then
  BACKUP="${TMPDIR:-/tmp}/$(basename "$IN").backup-$(date +%Y%m%d-%H%M%S)"
  cp "$IN" "$BACKUP"
  echo "decimate-glb: original backed up to $BACKUP"
  echo "decimate-glb: revert with  cp '$BACKUP' '$IN'"
fi

# `-b` with NO file argument: the script imports the .glb itself. Passing the .glb to -b silently
# starts Blender on its default cube instead, which is how the first version of this tool "succeeded"
# without touching anything.
BEFORE_MTIME="$(stat -f "%m" "$OUT" 2>/dev/null || echo none)"

"$BLENDER" -b -P "$HERE/decimate-glb.py" -- "$IN" "$OUT" "$TARGET" "$MODE" 2>&1 |
  grep -E "decimate-glb:|Error|Traceback" || true

[ -f "$OUT" ] || { echo "decimate-glb: produced no file" >&2; exit 1; }

# "The file exists" is NOT proof it was written - the output path is usually the input path, so an
# untouched original passes that test perfectly. Check it actually changed.
AFTER_MTIME="$(stat -f "%m" "$OUT")"
if [ "$BEFORE_MTIME" = "$AFTER_MTIME" ]; then
  echo "decimate-glb: FAILED - $OUT was never rewritten (still $BEFORE_MTIME)" >&2
  exit 1
fi

echo "decimate-glb: $(du -h "$OUT" | cut -f1) at $OUT"
