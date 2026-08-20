#!/usr/bin/env bash
# Take the press photographs on the macOS Player, unattended.
#
#   tools/press-shots.sh [outdir] [shot ...]
#
# One launch per photograph, for the same reason tools/perf-sweep.sh runs one launch per
# configuration: the hour, the staging and the HUD are all read once, at boot or on the way out of
# the title menu, so re-posing a live session leaves half the world in the previous shot's state.
#
# With no shot names it takes the whole list. With names it takes only those, which is what
# re-framing wants - shoot, look at the PNG, edit one number in PhotoProbe.cs's shot table, rebuild,
# and re-shoot that one frame.
set -uo pipefail

# ⚠ A DEVELOPMENT PLAYER, and this is the failure everybody hits first. PhotoProbe lives inside
# `#if UNITY_EDITOR || DEVELOPMENT_BUILD`, so a release Player keeps the class and strips every
# member that runs: no argument is parsed, no photograph is taken, and NOTHING IS LOGGED, because
# the code that would log is the code that is missing. Build it with
# The Block -> Build macOS Player (Development).
APP="${APP:-Builds/macOS/The Block.app}"
PLAYER_LOG="$HOME/Library/Logs/DefaultCompany/TheBlockUnity/Player.log"
OUT="${1:-press-out}"
shift || true
TIMEOUT="${TIMEOUT:-240}"

ALL=(
  01-hero-boulevard
  02-motorcycle
  03-police-night
  04-falafel-stand
  05-seven-eleven
  06-reichman-university
  07-reichman-lot
  08-beach-sea
  09-auto-shop
  10-police-station
  11-gameplay-hud
  12-helicopter
  13-jetski
)
SHOTS=("$@")
if [ ${#SHOTS[@]} -eq 0 ]; then SHOTS=("${ALL[@]}"); fi

if [ ! -d "$APP" ]; then
  echo "press-shots: no Player at $APP" >&2
  echo "             build it with The Block -> Build macOS Player (Development)" >&2
  exit 2
fi

# The Player's working directory is its own bundle, not this repo, so -photoOut has to be absolute
# or the PNGs land somewhere nobody looks.
mkdir -p "$OUT"
ABS_OUT="$(cd "$OUT" && pwd)"

echo "press-shots: ${#SHOTS[@]} shot(s) into $ABS_OUT/"
n=0
for shot in "${SHOTS[@]}"; do
  n=$(( n + 1 ))
  printf '  [%2d/%2d] %-24s ' "$n" "${#SHOTS[@]}" "$shot"

  # A stale log would be parsed as this run's result.
  rm -f "$PLAYER_LOG"

  # THROUGH `open -n`, NEVER THE RAW BINARY. A backgrounded raw launch is never activated by the
  # window server: Unity picks its Metal device and then blocks forever with no window, no error
  # and no timeout. perf-sweep.sh:74-95 is the full account of that detour.
  # PHOTO_ARGS is how a frame gets re-framed WITHOUT a rebuild, which matters because the shot
  # table is compiled into the Player and a rebuild is twelve minutes:
  #   PHOTO_ARGS="-photoFill 1.8 -photoElevation 2" SUFFIX="-b" tools/press-shots.sh press-out 04-falafel-stand
  # SUFFIX keeps the variant beside the original instead of overwriting it, so two framings can be
  # compared rather than remembered.
  # shellcheck disable=SC2086
  open -n "$APP" --args -photoShot "$shot" -photoOut "$ABS_OUT" \
    ${SUFFIX:+-photoSuffix "$SUFFIX"} ${PHOTO_ARGS:-}

  # `open` returns before the process exists, so wait for it to APPEAR before waiting for it to go.
  appeared=0
  for _ in $(seq 1 15); do
    if pgrep -f "$APP/Contents/MacOS/" >/dev/null 2>&1; then appeared=1; break; fi
    sleep 2
  done
  if [ "$appeared" -eq 0 ]; then echo "NEVER STARTED"; continue; fi

  waited=0
  while true; do
    sleep 2
    waited=$(( waited + 2 ))

    # PhotoProbe calls Application.Quit() the moment the file is written, so the process
    # disappearing IS the shot finishing.
    if ! pgrep -f "$APP/Contents/MacOS/" >/dev/null 2>&1; then break; fi

    if [ "$waited" -ge "$TIMEOUT" ]; then
      echo -n "TIMEOUT "
      pkill -9 -f "$APP/Contents/MacOS/" 2>/dev/null
      sleep 2
      break
    fi
  done

  name="$shot${SUFFIX:-}"
  [ -f "$PLAYER_LOG" ] && cp "$PLAYER_LOG" "$ABS_OUT/$name.log"

  if [ -f "$ABS_OUT/$name.png" ]; then
    size=$(( $(stat -f%z "$ABS_OUT/$name.png") / 1024 ))
    if grep -q "PHOTOPROBE FAILED" "$ABS_OUT/$name.log" 2>/dev/null; then
      echo "ok but FAILED line (${size} KB) - $(grep -m1 -o 'PHOTOPROBE FAILED.*' "$ABS_OUT/$name.log")"
    else
      echo "ok (${size} KB)"
    fi
  elif grep -q "PHOTOPROBE" "$ABS_OUT/$name.log" 2>/dev/null; then
    echo "NO IMAGE - $(grep -m1 -o 'PHOTOPROBE.*' "$ABS_OUT/$name.log")"
  else
    echo "NO IMAGE, and no PHOTOPROBE line at all - is this a Development build?"
  fi
done

echo "press-shots: done. $ABS_OUT/"
