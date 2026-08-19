#!/usr/bin/env bash
# Run the whole per-feature performance sweep on the macOS Player, unattended.
#
#   tools/perf-sweep.sh [outdir]
#
# Eight units owe U30b a per-feature frame delta and every one of them has been waiting on somebody
# driving the game while somebody else watches a counter. None of them needs a human: a triangle
# count and a millisecond are numbers. This launches the Development Player once per configuration,
# waits for it to measure one thing and quit, and keeps its log.
#
# ONE LAUNCH PER CONFIGURATION, deliberately - see PerfProbe.cs. The switches are PlayerPrefs read
# at Awake all over the world, so flipping one mid-session leaves half the scene in the old state
# and produces a number that looks plausible and is wrong.
#
# THE ARMS ARE INTERLEAVED (on, off, on, off) rather than run in blocks, so a laptop that heats up
# over the sweep heats up over BOTH arms of every feature instead of over one of them. Two runs of
# the same configuration are the noise floor, and a delta smaller than its own noise floor is not a
# finding.
set -uo pipefail

# PlayerBuilder's OutputDirectory is a single constant - BOTH menu items, release and development,
# write here. Builds/macOS-dev is a leftover from an older convention and is NOT where a fresh
# development build lands. A release Player compiles PerfProbe out entirely, so if the sweep reports
# "NO RESULT LINE" on every run, the answer is almost always that this .app is a release build.
APP="${APP:-Builds/macOS/The Block.app}"
BIN="$APP/Contents/MacOS/TheBlockUnity"
PLAYER_LOG="$HOME/Library/Logs/DefaultCompany/TheBlockUnity/Player.log"
OUT="${1:-perf-sweep-out}"
REPEATS="${REPEATS:-2}"
TIMEOUT="${TIMEOUT:-180}"

# feature:pose. baseline is not padding - both its arms are identical, so the spread between them
# is this machine's noise floor on the day, and every other delta is read against it.
#
# Overridable, because a follow-up almost never wants all eleven: SWEEP="occlusion:lotcars
# occlusion:falafel" tools/perf-sweep.sh out/ re-runs two rows instead of forty-four.
MATRIX=(
  "baseline:lotcars"
  "crowd:crowd"
  "lotcars:lotcars"
  "jetski:beach"
  "sea:beach"
  "ragdolls:crowd"
  "streetprops:crowd"
  "vehicledamage:lotcars"
  "autoshop:autoshop"
  "falafel:falafel"
  "heli:station"
)
if [ -n "${SWEEP:-}" ]; then MATRIX=($SWEEP); fi

if [ ! -x "$BIN" ]; then
  echo "perf-sweep: no Player at $BIN" >&2
  echo "            build it with The Block -> Build macOS Player (Development)" >&2
  exit 2
fi

mkdir -p "$OUT"
total=$(( ${#MATRIX[@]} * 2 * REPEATS ))
n=0
echo "perf-sweep: $total runs, ~${TIMEOUT}s ceiling each, into $OUT/"

for entry in "${MATRIX[@]}"; do
  feature="${entry%%:*}"
  pose="${entry##*:}"

  for repeat in $(seq 1 "$REPEATS"); do
    for state in on off; do
      n=$(( n + 1 ))
      label="${feature}-${state}-${repeat}"
      printf '  [%2d/%2d] %-28s ' "$n" "$total" "$label"

      # A stale log would be silently parsed as this run's result.
      rm -f "$PLAYER_LOG"

      # THROUGH `open`, NEVER THE RAW BINARY - this was a full diagnostic detour and is the single
      # least obvious thing in this file.
      #
      # Running "$APP/Contents/MacOS/TheBlockUnity" from a shell, backgrounded, starts a process
      # the window server never activates. Unity gets as far as picking the Metal device and then
      # blocks: Player.log stops one line before `Metal RecreateSurface`, no window is ever created,
      # and what a person sees is a black screen that sits there indefinitely - 171 s observed, with
      # no timeout and no error. The same bundle double-clicked in Finder runs perfectly, which is
      # what proved the build was fine and the launch was not.
      #
      # `open -n` registers with LaunchServices and gives the app a real GUI session. It returns
      # immediately, so the wait below polls for the process instead of using `wait`.
      # FREEZE=on empties the world of the crowd and the traffic in BOTH arms. Use it for any
      # delta smaller than they are: two identical `on` runs of the occlusion arm sampled 3.6 M
      # triangles apart because the streaming had reached different states, and no amount of
      # repeating averages out a world that never holds still. See PerfProbe.FreezeWorld.
      open -n "$APP" --args \
        -perfFeature "$feature" -perfState "$state" -perfPose "$pose" -perfRepeat "$repeat" \
        -perfFreeze "${FREEZE:-off}"

      # Wait for it to APPEAR first. `open` returns before the process exists, so polling straight
      # for "is it gone" would read the gap before launch as a finished run and record nothing.
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

        # The probe calls Application.Quit() the moment its sample is written, so the process
        # disappearing IS the run finishing. Match on the bundle path so a stray Unity Editor or
        # another app of the same name is never mistaken for this Player.
        if ! pgrep -f "$APP/Contents/MacOS/" >/dev/null 2>&1; then break; fi

        if [ "$waited" -ge "$TIMEOUT" ]; then
          echo -n "TIMEOUT "
          pkill -9 -f "$APP/Contents/MacOS/" 2>/dev/null
          sleep 2
          break
        fi
      done

      if [ -f "$PLAYER_LOG" ]; then
        cp "$PLAYER_LOG" "$OUT/$label.log"
        if grep -q "PERFPROBE RESULT" "$OUT/$label.log"; then
          echo "ok  ($(grep -o 'frameMs=[0-9.]*' "$OUT/$label.log" | head -1))"
        else
          echo "NO RESULT LINE"
        fi
      else
        echo "NO LOG"
      fi
    done
  done
done

echo "perf-sweep: done. Parse with: tools/perf-report.py $OUT"
