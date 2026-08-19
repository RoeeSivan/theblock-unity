# Kanban board

The course brief requires a kanban board and a screenshot of it in the submission PDF
(requirement #2). This directory holds the dataset and the tooling that publishes it.

**Board:** _(URL added at first push)_
**Screenshots:** `docs/kanban-board.png` (board view, for the PDF) · `docs/kanban-final.png`
(full height, every card visible)

## What this board covers

**Both phases of the project, on one board.** `The Block` was built twice:

| Phase | Engine | Repo | Dates |
| --- | --- | --- | --- |
| 1 | Three.js + Rapier + Vite + TS | https://github.com/RoeeSivan/Finalproject | 2026-04-17 → 08-03 |
| 2 | Unity 6 (URP) | https://github.com/RoeeSivan/theblock-unity | 2026-08-12 → |

Phase-2 cards carry the lime **`Unity port`** label; phase-1 cards carry no phase label. The `Done`
and `Cancelled / Parked` columns are sorted chronologically by `board-data.mjs`, so the lime band
starts exactly at the pivot and runs unbroken to the bottom — the pivot is visible rather than
argued.

**Every date and every commit hash on this board is real**, read off `git log` in both repos (142
hashes: 68 in `Finalproject`, 74 here) plus the two projects' decision ledgers. Trello derives a
card's created-date from its id and it cannot be backdated, so the board is an accurate *record* of
the project rather than an artifact that existed from day one.

The tooling was written for the Three.js repo (`scripts/kanban/` there) and **copied** here. That
repo is frozen at one additive script and is never modified again, so its copy stays as it was; this
one is the live one.

## Files

| File | Purpose |
| --- | --- |
| `board.json` | The dataset — every list, label and card. **Edit this, never the board by hand.** |
| `board-data.mjs` | Loads + validates `board.json`, sorts Done / Cancelled chronologically. |
| `render-html.mjs` | `board.json` → `board.html`, a local preview for reviewing before publishing. |
| `push-to-trello.mjs` | Publishes `board.json` to Trello over the REST API. |
| `capture-board.mjs` | Screenshots a public Trello board via the Chrome DevTools Protocol. |

Dependency-free: Node 22 and the Chrome installed on this machine are the only requirements.

## Rebuilding the board

```bash
node tools/kanban/push-to-trello.mjs                # dry run — prints the tree, no network
node tools/kanban/render-html.mjs                   # writes board.html to review locally

read -s "TRELLO_KEY?API key: "; echo                # keeps credentials out of shell history
read -s "TRELLO_TOKEN?Token: ";  echo
export TRELLO_KEY TRELLO_TOKEN
node tools/kanban/push-to-trello.mjs --push         # creates a NEW board, prints its URL
```

Credentials come from the environment only and are never written to disk. An API key needs a Trello
Power-Up (https://trello.com/power-ups/admin → API Key tab → the **Token** link beside it); leave the
iframe connector URL blank, since this is not a real Power-Up. Revoke the token at
https://trello.com/my/account → Applications when finished.

Note that `--push` always creates a *new* board; it does not update an existing one, and it never
touches a board it did not create.

## Screenshots

The board must be publicly visible while capturing, then set back to private:

```bash
curl -s -X PUT "https://api.trello.com/1/boards/<id>/prefs/permissionLevel?value=public&key=$TRELLO_KEY&token=$TRELLO_TOKEN"

node tools/kanban/capture-board.mjs "<board-url>" docs/kanban-board.png --screen
node tools/kanban/capture-board.mjs "<board-url>" docs/kanban-final.png

curl -s -X PUT "https://api.trello.com/1/boards/<id>/prefs/permissionLevel?value=private&key=$TRELLO_KEY&token=$TRELLO_TOKEN"
```

`chrome --screenshot` on its own is not enough here: Trello serves logged-out visitors a marketing
header, a cookie banner and a first-visit "About this board" dialog that dims the board, its lists
scroll independently so tall columns get cropped, and `captureBeyondViewport` tiles the content
instead of extending it. `capture-board.mjs` drives Chrome over the DevTools Protocol to remove those
overlays, expand the lists, grow the viewport to the board's measured bounding box, and clip to it.

`--screen` keeps the lists scrolling normally and produces a landscape image — the board as it
actually looks on a monitor. Without it, every list is expanded so all 117 cards appear in one tall
image; with `Done` at 87 cards that is a very tall PNG, which is the point of having both shots.
