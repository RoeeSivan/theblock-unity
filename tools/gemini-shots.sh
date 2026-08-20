#!/usr/bin/env bash
# Build the reference photograph set that Gemini draws the LinkedIn key art from.
#
#   tools/gemini-shots.sh [frame ...]
#
# The artwork is GTA-box-art style: an irregular grid of comic panels, ONE subject per panel. That
# is the whole reason this script exists rather than "just use press-out/". A press shot is a wide
# establishing frame; a panel reference has to be a tight portrait of one thing, because an image
# model draws what it can see and a subject occupying 8% of a 4K frame is not visible to it.
#
# So the set is half reuse, half re-shoot:
#
#   - Four frames in press-out/ are already the right shape and are copied through untouched.
#   - Four are re-shot TIGHT and at golden hour, through tools/press-shots.sh.
#
# ⚠ NO REBUILD IS NEEDED and that is not luck. Every dial these re-shoots turn - hour, azimuth,
# elevation, fill, fov, aim lift - is an argv override that PhotoProbe.Configure already parses
# (PhotoProbe.cs:265-282). Only Stand, Focus, Radius, Target and Stage are compiled into the shot
# table, and every viewpoint wanted here is reachable from a stand that already exists.
#
# With no frame names it does the whole set. With names it re-shoots only those, which is what the
# framing loop wants: shoot -> look at the PNG -> edit one number in Flags() -> shoot that one
# again. The numbers below are STARTING numbers, not settled ones.

set -uo pipefail

cd "$(dirname "$0")/.." || exit 2

# Shot into a boring path, published into the one with spaces in its name. press-shots.sh passes
# -photoOut through an UNQUOTED `open --args` expansion, and there is no reason to find out how
# that treats "photos for gemini" when a copy at the end costs nothing.
RAW="press-out/gemini-raw"
OUT="photos for gemini"

# One suffix for every re-shoot, so a re-shot frame lands beside its press-out original instead of
# overwriting it. Each base shot is used exactly once, so one suffix cannot collide.
SUFFIX_TAG="-gem"

ALL=(character scooter jetski)

# `publish` assembles the delivery folder from whatever is already in $RAW, without relaunching the
# Player. The framing loop converges long before the last shoot, and re-taking three good frames to
# reprint a JPEG is nine minutes for nothing.
PUBLISH_ONLY=0
if [ "${1:-}" = "publish" ]; then PUBLISH_ONLY=1; shift; fi

FRAMES=("$@")
if [ ${#FRAMES[@]} -eq 0 ]; then FRAMES=("${ALL[@]}"); fi

# --- what each re-shoot is ---------------------------------------------------------------------

# Which entry in PhotoProbe's compiled shot table each frame borrows its Stand/Focus/Radius from.
Base() {
  case "$1" in
    # 03's Target is empty and its Focus is Vector3.zero, so Frame() falls back to Stand - the
    # camera aims at the PLAYER, not at a building. That makes it the character-portrait rig, and
    # it is the only entry in the table that is one. StandYaw 0 faces +Z and azimuth 0 puts the
    # camera at +Z looking back, so the pose is frontal.
    character) echo "03-police-night" ;;
    scooter)   echo "02-motorcycle" ;;
    jetski)    echo "13-jetski" ;;
    *)         echo "" ;;
  esac
}

# ⚠ THERE IS NO hero-car FRAME, and the reason is arithmetic rather than taste. Fill is CLAMPED to
# 6 (PhotoProbe.cs:743), so the tightest frame any shot can reach is a box 2 * radius / 6 tall.
# 07-reichman-lot targets LotCars, whose measured bounds are about 100 m across, which puts its
# floor at a 33 m box - a 5 m car is 15% of that, at any fov, at any Fill this side of a rebuild.
# The GTA-style car panel is therefore drawn by the model from 05-skyline-sunset's parked lot
# rather than photographed. The clamp is also why `character` is shot loose and CROPPED below: its
# radius is 12, so its floor is a 4 m box and a person is never more than about half of it.

# ⚠ Fill is a DIAL, NOT A FRACTION - it is a 3D diagonal measured against a 16:9 frame, so a person
# standing inside a Radius = 12 shot needs a Fill far above 1 before they fill anything. The same
# trap is worse on hero-car: LotCars' measured bounds are about 100 m across, so closing on a
# single car needs a very large number and may not be reachable at all.
#
# Hour is pinned near 17.4 on purpose. press-out/v2-goldenhour/07-reichman-lot.png proves the sky
# goes burnt-orange-into-teal there, which is both the reference covers' palette and the project's
# own #FF6A00. A negative hour would leave the built midday lighting, which is what made the
# existing frames flat.
Flags() {
  case "$1" in
    # ⚠ MEASURED, not read off the docstring: azimuth 0 puts the camera at -Z of the focus, so with
    # StandYaw 0 facing +Z it photographs the back of the player's head. 180 is the frontal pose.
    # The lens is long on purpose - a chest-up portrait at fov 40 would need the camera about 1.2 m
    # from the face, which distorts it; fov 24 buys the same crop from 3.2 m back.
    character) echo "-photoHour 16.9 -photoAzimuth 150 -photoElevation -3 -photoFill 6 -photoFov 35 -photoAimLift 1.0" ;;
    # Fill 3 with AimLift 0.6 framed a 2.13 m box centred at 0.6 and guillotined the rider's head at
    # 1.9. The aim has to sit near the middle of the SUBJECT, not near the ground.
    scooter)   echo "-photoHour 17.2 -photoAzimuth 210 -photoElevation 2 -photoFill 2.1 -photoFov 42 -photoAimLift 1.0" ;;
    jetski)    echo "-photoHour 17.4 -photoAzimuth 300 -photoElevation 4 -photoFill 3 -photoFov 45" ;;
    *)         echo "" ;;
  esac
}

# --- the published set -------------------------------------------------------------------------

# name|source. Order is the order to hand them to Gemini in; the numbers are part of the filename
# because an image model is told "panel 3 is the jetski" and the user has to be able to see which
# file that was.
Published() {
  cat <<EOF
01-character|$RAW/03-police-night$SUFFIX_TAG.png
02-scooter-rider|$RAW/02-motorcycle$SUFFIX_TAG.png
03-jetski|$RAW/13-jetski$SUFFIX_TAG.png
04-police|press-out/10-police-station.png
05-skyline-sunset|press-out/v2-goldenhour/07-reichman-lot.png
06-hud-gameplay|press-out/11-gameplay-hud.png
07-helicopter|press-out/12-helicopter.png
08-auto-shop|press-out/09-auto-shop.png
EOF
}

# Panel crops, in SOURCE pixel coordinates, as ffmpeg's w:h:x:y. Empty means publish the whole
# frame. This exists because of the Fill clamp above: where the camera cannot get closer, the 4K
# frame still holds far more pixels than a reference needs, so the tight panel is cut out of it.
# A 700 x 1250 crop delivered at its native size is a better reference than a full frame in which
# the subject is 400 px tall.
Crop() {
  case "$1" in
    # The player stands at roughly x 1750-2100, y 660-1610 of the 4K frame. 3:4 around him, with
    # headroom, which is the shape a character panel wants.
    01-character) echo "800:1070:1520:555" ;;
    *)            echo "" ;;
  esac
}

# --- phase 1: shoot ------------------------------------------------------------------------------

mkdir -p "$RAW"

if [ "$PUBLISH_ONLY" -eq 1 ]; then
  echo "gemini-shots: publish only - no Player launches."
fi

[ "$PUBLISH_ONLY" -eq 1 ] || echo "gemini-shots: re-shooting ${#FRAMES[@]} frame(s) into $RAW/"
for frame in "${FRAMES[@]}"; do
  [ "$PUBLISH_ONLY" -eq 1 ] && break
  base="$(Base "$frame")"
  if [ -z "$base" ]; then
    echo "gemini-shots: no such frame '$frame' - one of: ${ALL[*]}" >&2
    exit 2
  fi
  echo
  echo "── $frame  (from $base)"
  PHOTO_ARGS="$(Flags "$frame")" SUFFIX="$SUFFIX_TAG" tools/press-shots.sh "$RAW" "$base"
done

# --- phase 2 + 3: publish ------------------------------------------------------------------------

# Only publish on a whole run. A single-frame re-shoot is a look-and-nudge iteration, and
# rewriting the delivery folder on every nudge would hand the user a half-tuned set.
if [ "$PUBLISH_ONLY" -eq 0 ] && [ ${#FRAMES[@]} -ne ${#ALL[@]} ]; then
  echo
  echo "gemini-shots: partial run - $RAW/ updated, '$OUT/' left alone."
  echo "              look at the PNG, edit Flags() in this script, re-run the frame."
  echo "              run with no arguments to publish the whole set."
  exit 0
fi

echo
echo "gemini-shots: publishing into $OUT/"
mkdir -p "$OUT/_full"

missing=0
while IFS='|' read -r name src; do
  [ -z "$name" ] && continue
  if [ ! -f "$src" ]; then
    printf '  %-20s MISSING - %s\n' "$name" "$src"
    missing=$(( missing + 1 ))
    continue
  fi

  cp "$src" "$OUT/_full/$name.png"

  # Cut the panel out first, where one is defined. ffmpeg rather than sips because sips only crops
  # centred and a panel is almost never in the middle of the frame.
  cut="$src"
  box="$(Crop "$name")"
  if [ -n "$box" ]; then
    cut="$RAW/$name-crop.png"
    ffmpeg -y -loglevel error -i "$src" -vf "crop=$box" "$cut" </dev/null || cut="$src"
  fi

  # 2048 px on the long edge, JPEG q92. A 4-7 MB 4K PNG is past what an image model reads and the
  # extra detail is never looked at; sips is built in, so this adds no dependency.
  sips -s format jpeg -s formatOptions 92 -Z 2048 "$cut" --out "$OUT/$name.jpg" >/dev/null 2>&1

  if [ -f "$OUT/$name.jpg" ]; then
    printf '  %-20s ok (%s KB)\n' "$name" "$(( $(stat -f%z "$OUT/$name.jpg") / 1024 ))"
  else
    printf '  %-20s SIPS FAILED - %s\n' "$name" "$src"
    missing=$(( missing + 1 ))
  fi
done < <(Published)

# The wordmark, so Gemini draws the title in the game's own letterforms instead of inventing a
# typeface. Copied flat, not downscaled - it is 1024² and 4 KB of solid black and white.
if [ -f Assets/Icons/AppIcon.png ]; then
  cp Assets/Icons/AppIcon.png "$OUT/logo-the-block.png"
  echo "  logo-the-block       ok"
else
  echo "  logo-the-block       MISSING - Assets/Icons/AppIcon.png"
  missing=$(( missing + 1 ))
fi

echo
if [ "$missing" -gt 0 ]; then
  echo "gemini-shots: done with $missing gap(s). $OUT/"
  exit 1
fi
echo "gemini-shots: done. $OUT/"
