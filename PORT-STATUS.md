# PORT-STATUS — The Block, Unity port

**This is the living ledger. Read it immediately after `CLAUDE.md`, before doing anything else.**
It is the only thing that survives a lost session. Conversation history is not a source of truth;
this file is.

---

## RESUME HERE

**Next action:** U2 — character import.

Bring one Mixamo character into Unity as a **Humanoid** rig with a walk clip that plays. Importing
as Humanoid is the point of the unit: it retargets onto Unity's own bone map and makes the
`mixamorig:` namespace bug class from the web build structurally impossible.

- Source: `<game-repo>/source-assets/models/*.fbx` — these are the raw Mixamo downloads (~45–63 MB
  each), which is what rule 3 asks for. `joe idle.fbx` + `Walking man.fbx` are the obvious pair.
- In the FBX importer: **Rig → Animation Type: Humanoid**, then **Configure…** and confirm every
  required bone mapped green.
- Split it the Unity way: **one** model as the avatar/mesh, the rest imported animation-only with
  **Avatar Definition: Copy From Other Avatar**. Do not import the same skeleton nine times.
- Drop it in `World.unity` on the downtown pavement (ground is y≈0.15) and confirm the walk clip
  loops in the Animation preview.

**Do NOT** build the character controller here — that is U6. U2 ends when a character stands in the
scene with a looping walk clip.

**Nothing is half-built.** No `wip` units.

**Open thread — district source assets.** Every district (`first-one`, `procedural-city-2`…`-7`,
`reichman`, plus `parking-lot` and `road-straight`) exists **only** as its Draco/webp-1024²
shipped GLB. `source-assets/` holds characters, vehicles and props — no city. The user is looking
for the original downloads. If they land, put them in a gitignored folder **outside** the repo
(free LFS is 1 GiB, shared with the original project) and prefer them in U11/U12/U13.

**Requires:** a session with cwd `~/TheBlockUnity` (the MCP server is scoped to that path) and the
game repo added via `/add-dir`. See `CLAUDE.md` §2.

---

## Units

State: `todo` · `wip` (half-built — the notes column MUST say exactly what and what's next) · `done`

### Tier 0 — Pipeline
| id | unit | state | commit | notes |
| --- | --- | --- | --- | --- |
| U0 | Project setup — Unity, MCP, git, LFS, docs | done | `dacca07` | Unity 6000.5.8f1 URP; MCP v10.1.2 HTTP Local :8080; remote pushed |
| U1 | glTF import path — glTFast + Draco, downtown solid | done | `5a0b58f` | glTFast 6.19.0 + Draco 5.4.3; `World.unity` is build scene 0; asset needed zero fixup |
| U2 | Character import — Mixamo FBX as Humanoid, walk clip | todo | | Humanoid retargeting kills the `mixamorig:` bug class |
| U3 | `Convert` handedness helper | todo | | Verify empirically vs a landmark, never assume |
| U4 | `export-config.mjs` → `theblock-config.json` | todo | | Lives in the GAME repo — its only permitted change |
| U5 | `WorldBuilder` Editor script | todo | | Re-runnable; conversion happens here, not in the exporter |

### Tier 1 — Traversal
| id | unit | state | commit | notes |
| --- | --- | --- | --- | --- |
| U6 | Character controller + camera follow | todo | | ports `src/player/` |
| U7 | Animator state machine (idle/walk/run/jump) | todo | | |

### Tier 2 — Vehicles
| id | unit | state | commit | notes |
| --- | --- | --- | --- | --- |
| U8 | Vehicle base + one drivable car | todo | | Rigidbody + WheelColliders |
| U9 | Enter/exit state machine + seated driver | todo | | mirrors `game/game-state.ts` mode enum |
| U10 | Motorcycle | todo | | feel is re-derived, not ported — budget real time |

### Tier 3 — World
| id | unit | state | commit | notes |
| --- | --- | --- | --- | --- |
| U11 | All 9 districts via WorldBuilder | todo | | No district has a source original — see the open thread in RESUME HERE |
| U12 | Roads, ground, sea | todo | | |
| U13 | Places — pizza + interior, gas, police station, lot cars | todo | | |
| U14 | Map + minimap | todo | | |
| U15 | Addressables streaming | todo | | ONLY if the profiler says so — measure first |

### Tier 4 — Living world
| id | unit | state | commit | notes |
| --- | --- | --- | --- | --- |
| U16 | Pedestrian crowd (NavMesh agents) | todo | | |
| U17 | Traffic — graph, cars, lights | todo | | |
| U18 | Run-over + blood VFX | todo | | Root Motion ON — the clip's motion IS the knockback |
| U19 | Police pursuit + wanted level | todo | | real NavMesh; do NOT inherit the straight-line hack untested |

### Tier 5 — Missions
| id | unit | state | commit | notes |
| --- | --- | --- | --- | --- |
| U20 | Mission framework + campaign director + persistence | todo | | |
| U21 | M1 pizza delivery | todo | | |
| U22 | M2 rhythm / dance minigame | todo | | |
| U23 | Helicopter + M3 rooftop rescue | todo | | |
| U24 | Jetski + M4 chase | todo | | |

### Tier 6 — Shell
| id | unit | state | commit | notes |
| --- | --- | --- | --- | --- |
| U25 | HUD + in-game UI (UI Toolkit) | todo | | |
| U26 | Menus — title, character select, briefing, controls, pause | todo | | |
| U27 | Audio — sfx, engine, ambient, radio | todo | | |
| U28 | Economy + fuel + power-ups | todo | | |
| U29 | Character roster | todo | | |

### Tier 7 — Ship
| id | unit | state | commit | notes |
| --- | --- | --- | --- | --- |
| U30 | macOS build + perf pass | todo | | watch texture memory — it killed web mobile |
| U31 | iOS / iPad | todo | | free 7-day Xcode provisioning; $99 only for distribution |
| U32 | Multiplayer | todo | | DEFERRED by decision — revisit only here |

---

## How to close a unit

A unit is **not done** until all three are true:

1. It play-tests correctly in the Editor, confirmed **by the user** (I cannot see the Game view).
2. This file is updated — state → `done`, commit hash filled in, `RESUME HERE` rewritten to the
   next action.
3. The commit lands.

If a unit **cannot** be finished, set it to `wip` and write in the notes exactly what is built,
what is not, and the next concrete action. Then update `RESUME HERE` to point at it. A `wip` unit
with a vague note is the one failure mode this whole system exists to prevent.

---

## Decisions log

Dated one-liners. These are settled — do not re-litigate them without the user reopening.

- **2026-08-12** — Scope is the **full game**, not a slice. No deadline; resumability matters more
  than speed.
- **2026-08-12** — **Unity-idiomatic, same game.** Where Unity offers a better mechanism than the
  web version's workaround, Unity wins (NavMesh police, Addressables streaming). Same missions,
  same world, same feel.
- **2026-08-12** — **Multiplayer deferred to U32.** `src/mp` + `src/net` (2,263 lines) rides on
  Supabase Realtime; none of that transport carries to Unity.
- **2026-08-12** — **Autonomous units with a checkpoint each.** Build a unit fully, update this
  ledger, commit, report. User play-tests at unit boundaries.
- **2026-08-12** — **Desktop (macOS) is the priority target**; iPad is a wanted bonus, never a
  constraint on design.
- **2026-08-12** — **No money spent, ever.** Unity Personal only. No Unity Cloud, no Unity AI
  (it bills credits), no Asset Store, no paid LFS.
- **2026-08-12** — Transport for MCP is **HTTP Local**; the remote option requires a Coplay API key
  and is off the table for the same reason.
- **2026-08-12** (U1) — **The facade tint is a material asset, not code.** The web build recolours
  `facade_5` in code at load because it cannot author materials; Unity can. `Facade.mat` costs
  nothing at runtime and is editable without a rebuild. Unity wins, per the rule above.
- **2026-08-12** (U1) — **Downtown gets one collider over the whole mesh.** `city.noCollidePatterns`
  matches node *or* material names; `first-one.glb` has no per-object nodes and its only foliage
  material (`AM113_072_Washingtonia_filifera`) matches no pattern — so the shipped web build
  collides with its palms too. This is faithful, not a shortcut. Build the noCollide filtering when
  the first multi-node district lands, not before.
