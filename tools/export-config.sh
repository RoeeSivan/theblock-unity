#!/usr/bin/env bash
# Refresh Assets/StreamingAssets/theblock-config.json from the original game repo.
#
# The exporter itself lives in the game repo (scripts/export-config.mjs) — it is the
# one and only change that repo is permitted to receive. This wrapper holds the
# port-specific wiring so the exporter stays generic.
#
#   tools/export-config.sh            refresh the JSON
#   tools/export-config.sh --check    exit 1 if the JSON is stale (CI-ish guard)
#
# Point GAME_REPO elsewhere if the original moves.
set -euo pipefail

GAME_REPO="${GAME_REPO:-$HOME/Desktop/Year B/Semester B/From idea to app using AI/Final project}"
UNITY_REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$UNITY_REPO/Assets/StreamingAssets/theblock-config.json"

if [ ! -f "$GAME_REPO/scripts/export-config.mjs" ]; then
  echo "export-config: exporter not found at $GAME_REPO/scripts/export-config.mjs" >&2
  echo "               set GAME_REPO to the original project's root and retry" >&2
  exit 2
fi

exec node "$GAME_REPO/scripts/export-config.mjs" --out "$OUT" "$@"
