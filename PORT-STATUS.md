# PORT-STATUS — The Block, Unity port

**This is the living ledger. Read it immediately after `CLAUDE.md`, before doing anything else.**
It is the only thing that survives a lost session. Conversation history is not a source of truth;
this file is.

---

## RESUME HERE

**Next action:** start **U9 — enter/exit state machine + seated driver.**

**U8 is done**, user-confirmed 2026-08-13: the Mustang drives and feels right.

### What U9 has to build

Replace `DebugVehicleSwitch.cs` — **delete that file**, it is U8 scaffolding that teleports on `V`
with no door, no proximity test and no animation. What it does leave behind is the right shape for
the swap: disable the controller you are leaving, enable the one you are taking, point the camera at
the new `IChaseTarget`. Keep that much.

The web build's mode enum is in `src/game/modes.ts` and is only six labels:

```
'onFoot' | 'entering' | 'driving' | 'exiting' | 'transition' | 'rhythm'
```

`entering`/`exiting` exist to freeze input while the door swings, so a trigger cannot fire
mid-teleport. `transition` is for the interior fade (U13) and `rhythm` for U22 — U9 needs the first
four. Run state lives in `src/game/game-state.ts`; the fields U9 cares about are `mode` and
`activeVehicle`.

Timings and geometry already in `config.vehicle`, all ported gameplay numbers:

| field | value | means |
| --- | --- | --- |
| `enterRadius` | 4.5 m | how close Joe must stand — already on `TheBlockConfig.VehicleSpec` |
| `enterDoorOpenTime` | 0.55 s | door swings before Joe is hidden |
| `enterDoorCloseDelay` | 0.5 s | after hiding, before the door shuts → `driving` |
| `exitDoorCloseTime` | 0.6 s | door open while stepping out, then shuts |
| `exitSideOffset` | 1.8 m | how far beside the car Joe reappears |

**The door is already rigged and needs no new asset.** `config.vehicle.cars[].door` for the Mustang
is `{ jointName: 'Door_L_6', axis: 'z', openAngle: -1.134, lerpRate: 8 }`, and
`TheBlockConfig.DoorSpec` already carries it. The GLB's armature puts the joint at the real hinge,
so rotating the bone is enough and the skinned mesh follows — same trick `CarWheel` uses. Note
`axis` is in the MODEL's frame, and `openAngle` is a three.js radian, so both need converting.

**⚠ The seated driver is where `Convert.ModelOffset`'s unverified X finally gets tested.** The
Mustang's seat block is `config.vehicle.driver.seats['/models/optimized/mustang.glb']` =
`{ x: -2.31, y: -0.84, z: -0.1, yaw: -π/2, scale: 0.95 }`. Every model-local offset ported so far
has had `x = 0`, so the X negation in `ModelOffset` has never once been exercised — its own doc
comment says to check it against a landmark the first time a non-zero X appears, and calls out a
driver's seat by name. **This is that moment.** If the driver ends up sitting in the passenger seat,
the sign is wrong; fix it in `Convert`, never at the call site.

### U8 reference — tuning knobs

All serialized on `CarController` (select the spawned `Mustang` during Play and edit in the
Inspector; the values live on `Assets/Prefabs/Vehicles/Mustang.prefab`):

| feels wrong | knob | now |
| --- | --- | --- |
| sluggish / too eager off the line | `motorTorque` | 1600 Nm |
| won't stop, or stops dead | `brakeTorque` | 3000 Nm |
| coasts forever / drags to a halt | `coastBrake` | 450 Nm |
| understeers, or spins at speed | `steerAtTopSpeed` | 0.35 |
| steering too slow/twitchy to reach lock | `steerRate` | 120 °/s |
| leans or rolls in corners | `centerOfMass` | (0, 0.35, −0.1) |
| handbrake won't step the back out | `handbrakeGrip` | 0.45 |

Suspension and tyre-grip numbers are NOT here — they are baked into the prefab by `CarBuilder` and
live as constants at the top of `Assets/Editor/CarBuilder.cs`. Change them there and re-run
**The Block → Build Mustang**, which rebuilds the prefab in place so the scene keeps its reference.

Controls while driving: `W`/`S` throttle and brake-then-reverse, `A`/`D` steer, `Space` handbrake.
`V` gets in and out until U9 replaces it with `E` and a real door.

Measured in Play with synthetic input, if any of it ever looks wrong later: spawns on the lot with
four wheels grounded, caps at 20.10 m/s and −7.03 m/s, brakes through zero, steers right on `D`,
tracks straight to 0.045 m over 176 m, holds upright 1.0000 through a 72° turn at speed, and stops
dead against a building.

**U7 is done** — the user confirmed walk, sprint and jump all read right on 2026-08-13.

Its blend was verified programmatically too: `Joe_Idle` at 0 m/s, `Joe_Walk` at 2, a 50/50
walk-sprint blend at 4.5 and `Joe_Sprint` at 7, with the jump transition entering and returning on
landing. **If sprint ever comes up again**, the two candidates, in order:

1. `PlayerAnimator.speedBlendRate` (12 m/s per second) means a standing start takes ~0.6 s for the
   blend to climb 0 → 7, while the controller is already at full speed on frame one. A short burst
   therefore never reaches the sprint clip. Raising the rate, or making it asymmetric so it speeds
   up faster than it slows down, is the first thing to try.
2. `JoeAnimatorBuilder.SprintClipSpeed` (5.58) sets the 1.25× playback correction. Movement speed
   itself is `config.player.movement.sprintSpeed` = 7.0 and was never touched by U7.

Rebuild the graph any time with **The Block → Build Joe Animator** — `Joe.controller` is generated
from `Assets/Editor/JoeAnimatorBuilder.cs`, not hand-authored, so re-run it after any new clip
lands rather than editing the graph in the Animator window.

**Clips still missing.** None of these block anything; each just falls through:

| clip | Mixamo name | falls back to |
| --- | --- | --- |
| jog | Jog Forward | the 50/50 walk-sprint blend at 4.5 m/s |
| falling | Falling Idle | holds the jump pose |
| exhausted | Standing Idle 02 Exhausted | idle |

When one arrives: import it Humanoid with **Avatar Definition: Copy From Other Avatar → JoeAvatar**,
name the clip `Joe_<Thing>`, then re-run the builder.

**U6 is done** — the user confirmed the controls feel right. Controls:

| key | does |
| --- | --- |
| `W` / `S` | forward / back along whatever Joe faces |
| `A` / `D` | turn Joe left / right (tank controls — the camera follows the body, it does not steer it) |
| `Shift` | sprint, 7.0 m/s, drains stamina |
| `Alt` | jog, 4.5 m/s |
| nothing | walk, 2.0 m/s |
| `Space` | jump |

**Downtown was rendering as a nest of grey spikes and is fixed** (2026-08-13). Unity's static
batching had replaced its 122,678-vertex mesh with a `Combined Mesh (root: scene)` built on a 16-bit
index buffer, so every index past 65,535 wrapped. The collider kept using the real asset mesh, which
is why the world felt right and looked shredded — and why it survived the U1 checkpoint. See memory
`static-batching-shreds-big-meshes`.

**The world is generated, not hand-placed.** `World.unity` holds four roots:
`Main Camera`, `Directional Light`, `Player_Joe`, and `World` — everything under `World` is
WorldBuilder's output and is destroyed and rebuilt on every run. **Never hand-edit anything under
`World`**; change `config.ts` or `WorldBuilder.cs` instead.

**Cars are spawned at runtime, not placed in the scene.** `CarSpawner` on the `Vehicles` root reads
`config.vehicle.cars`, probes for ground under each spawn, and instantiates the prefab. WorldBuilder
owns the static world; anything that drives away from where it started belongs to the spawner. U13's
lot cars and U17's traffic grow from there.

The pipeline, end to end:

```
game repo  src/config.ts
   → scripts/export-config.mjs            (the game repo's ONLY permitted change)
   → tools/export-config.sh               (this repo — holds the port-specific paths)
   → Assets/StreamingAssets/theblock-config.json   (gitignored, 61 KB, whole config)
   → TheBlockConfig.Load()                (Assets/Scripts/Core/TheBlockConfig.cs)
   → The Block → Build World              (Assets/Editor/WorldBuilder.cs, applies Convert)
```

**The ground plate is built too, and it belongs to U12.** `config.ground` is a 1400 × 1400 m plane
at y −0.05, and it was pulled forward because the districts are islands: a car that left one had
nothing under it and fell forever, which no play-test survives. Only the plate — roads, kerbs and
the sea are still U12's. It sits marginally below every district so district ground always wins a
ground probe.

Last build: **12 placed, 2 missing, 109 colliders** — the ground plate, 8 districts, the 7-Eleven,
the pizza place.

**Missing assets — the world builds fine without them, they are logged not fatal:**

| config url | needed for | status |
| --- | --- | --- |
| `gas-station.glb` | U13, fuel | not yet ingested |
| `police-station.glb` | U13, U19 | not yet ingested |

**The parking lot and Reichman are in** (2026-08-13). The user re-modelled both in Blender rather
than falling back on the shipped GLBs, and both reproduce `config.ts`'s stated geometry exactly:

- **Parking lot** — 165 × 116 m, asphalt top at y 0.08, stall lines 0.09–0.11. Spans Unity
  X[134.4, 299.4] / Z[−304, −188], the mirror of the web build's X[−299, −134].
- **Reichman** — 36.1 × 31.6 m, 31 m tall. Its south edge lands at z −185.08 against the
  `config.ts` note "the school's south edge (z~-185.1)", clearing the lot's near edge by 2.92 m
  against its "~3 m", with both centred at x 216.90 against its "aligned in X". Three independent
  landmarks, so the export orientation is confirmed rather than assumed.

Sources are `blender/parkinglot.blend` and `blender/reichman.blend` **in the game repo**, exported
by `tools/blend-to-glb.sh` here. That script only ever READS the .blend (Blender runs `-b` and never
saves), so port rule 4 holds.

**Hebrew text is NOT mirrored by the X negation** — checked by eye on Reichman's sign, which reads
`אוניברסיטת רייכמן` correctly. Worth knowing before someone "fixes" it; see memory
`x-negation-does-not-mirror-text`.

**Pizza place is a stand-in, and it needed three fixes** — all of them in
`WorldBuilder.AssetAliases` rather than baked into the file, so the download stays as downloaded and
the correction stays visible in the build report. User-confirmed 2026-08-13.

- It shipped a **collision proxy**: a `Collider` node holding a coarse box at 100× non-uniform
  scale, meant for physics and never to be drawn. It rendered as a grey slab over the shop and was
  the first thing a downward raycast hit, so ground probes read its roof. `HideCollisionProxies`
  now disables `Collider*` nodes on every place. Expect this on any Sketchfab prop — see memory
  `sketchfab-collider-proxy-node`.
- It **lay on its back**: the GLB's node chain leaves local Y and Z swapped, so the lamp post ran
  3.28 m along Z instead of standing up. Corrected with `ExtraEuler = (-90, 0, 0)`.
- Its **pivot is at the model's centre**, not its base, so half of it was underground.
  `ExtraY = 0.15` rests it on the pavement.

Stand-ins also **skip the config's `hideNodes`**: those name parts of the original model, and this
one happens to share the name `PizzaLight` — which is its lamp post, not the original's light.

**Known issues, all belong to U11:**
- Foliage renders as white shards — imported `alphaMode: BLEND` with ZWrite off. Alpha-clip is the
  right fix but glTFast's Shader Graph ignores `_AlphaClip`; the surface mode has to be driven
  another way. Attempted and reverted, not left half-applied.
- Cities 2 and 3 each have one renderer that mixes the baked-in parked cars with real geometry, so
  `hideMaterials` cannot strip them without taking buildings too. They stay visible; a submesh split
  in Blender is the fix. Every other district hides its cars cleanly.
- Districts are merged meshes, so `noCollidePatterns` almost never matches — a district gets 2–4
  whole-mesh colliders, palms included. The web build has the same hole, so this is faithful rather
  than broken, but raw multi-node sources would let the filter actually work.

**District GLBs are gitignored** (40–85 MB each; free LFS is 1 GiB and shared with the original
repo). Working copies live in `Assets/Models/City/`, zips in `~/TheBlockSource/cities/zips/`. A
fresh clone opens `World.unity` with the districts missing until those are restored — deliberate.
`first-one.glb` is the exception: 240 KB and the only copy anywhere, so it is committed.

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
| U2 | Character import — Mixamo FBX as Humanoid, walk clip | done | `13cea9f` | `JoeAvatar` isHuman, 52 bones; clips `Joe_Idle`/`Joe_Walk` loop. Bones were `mixamorig7:` — suffix varies per export, Humanoid makes it moot |
| U3 | `Convert` handedness helper | done | `16fe0ee` | Negate X. `Assets/Scripts/Core/Convert.cs`; verified 8/8 against the placed scene objects |
| U4 | `export-config.mjs` → `theblock-config.json` | done | `62d917a` | Whole config, not a subset — the game repo gets one change ever, so a subset would force re-editing it at U12/U13/U17. 61 KB, byte-identical across runs |
| U5 | `WorldBuilder` Editor script | done | `62d917a` | Menu **The Block → Build World**. User-confirmed 2026-08-13: their run reproduced the report line for line — 9 placed, 4 missing, 96 colliders |

### Tier 1 — Traversal
| id | unit | state | commit | notes |
| --- | --- | --- | --- | --- |
| U6 | Character controller + camera follow | done | `1905f94` | `Assets/Scripts/Player/{PlayerController,FollowCamera}.cs` on `Player_Joe` / `Main Camera`. User-confirmed 2026-08-13: controls feel right |
| U7 | Animator state machine (idle/walk/run/jump) | done | `2525c3b` | Graph generated by **The Block → Build Joe Animator**; `PlayerAnimator.cs` drives it. User-confirmed 2026-08-13: walk, sprint and jump all read right. Missing jog/falling/exhausted clips all fall through cleanly — see the clip table below |

### Tier 2 — Vehicles
| id | unit | state | commit | notes |
| --- | --- | --- | --- | --- |
| U8 | Vehicle base + one drivable car | done | `b789c5a` | Rigidbody + 4 WheelColliders, NOT a port of the kinematic `vehicle.ts`. `Assets/Scripts/Vehicle/{CarController,CarWheel,CarSpawner,DebugVehicleSwitch}.cs`; prefab generated by **The Block → Build Mustang** (`Assets/Editor/CarBuilder.cs`). User-confirmed 2026-08-13: it drives and feels right. Tuning table in RESUME HERE. `DebugVehicleSwitch` (`V`) is scaffolding U9 deletes |
| U9 | Enter/exit state machine + seated driver | todo | | **Next unit.** Mirrors `game/modes.ts` — `onFoot`/`entering`/`driving`/`exiting`. Deletes `DebugVehicleSwitch.cs`. Door bone + timings are already in config and in `TheBlockConfig.DoorSpec`; the seat offset is the first non-zero X ever ported, so it finally tests `Convert.ModelOffset`. Full brief in RESUME HERE |
| U10 | Motorcycle | todo | | feel is re-derived, not ported — budget real time |

### Tier 3 — World
| id | unit | state | commit | notes |
| --- | --- | --- | --- | --- |
| U11 | All 9 districts via WorldBuilder | todo | | Placement, colliders and the foliage filter ship in U5's WorldBuilder; `reichman` + `parking-lot` were ingested during U8 (both re-modelled in Blender). What is left: the foliage alpha-clip fix and the city 2/3 submesh split |
| U12 | Roads, ground, sea | todo | | The 1400 m ground plate was pulled forward into U8 — a car needs somewhere to land. Roads, kerbs and the sea are still open |
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
- **2026-08-12** — **Handedness is X negation.** `Convert.Pos = (-x, y, z)`, `Convert.Yaw = -y`.
  Established empirically on five district assets plus a landmark gap measurement, not assumed.
  Closes the biggest open risk in the port. See memory `handedness-negate-x`.
- **2026-08-12** — **Districts are built from raw Sketchfab originals, not the shipped GLBs.**
  The raw downloads share the exact coordinate frame `config.ts` documents, so they need no Blender
  normalize pass — which removes the only real cost objection. See memory
  `district-sources-match-config`.
- **2026-08-12** — **District GLBs stay out of git.** 40–85 MB each; free LFS is 1 GiB and shared
  with the original repo. Working copies in `Assets/Models/City/` are gitignored, zips archived in
  `~/TheBlockSource/cities/zips/`. `first-one.glb` is the exception — 240 KB and the only copy in
  existence, so it is committed.
- **2026-08-13** (U4) — **The exporter dumps the WHOLE config, not the subset U5 needs.** The game
  repo is permitted exactly one added file, so a subset would force re-editing it at U12, U13, U17
  and U20. The whole thing is 61 KB and `TheBlockConfig` ignores unknown fields, so the C# model can
  stay a subset and grow per unit while the exporter never changes again.
- **2026-08-13** (U4) — **No timestamp in the export; a `$sourceSha256` instead.** A timestamp would
  break byte-identical re-runs, which is what makes a stale export detectable at all.
- **2026-08-13** (U5) — **The scene is a pure function of the config plus the assets on disk.**
  WorldBuilder destroys its own root and rebuilds every run, so nothing under `World` may be
  hand-edited. Placement, the facade rebind, car hiding and colliders all live in the builder — not
  in the scene file, where they would be invisible and unreproducible.
- **2026-08-13** (U5) — **Foliage is excluded from collision only when the WHOLE renderer is
  foliage.** The district GLBs are merged meshes; "any material matches" stripped collision from
  entire districts. A mixed mesh collides, palms included — the same hole the web build has.
- **2026-08-13** (U5) — **Substitute models go in `WorldBuilder.AssetAliases`, never renamed or
  re-authored on disk.** A rename hides the substitution and an edited file hides the fix; the alias
  table carries the file name plus whatever rotation and lift that stand-in needs, and warns on
  every build. First entry is the pizza place, which needed all three.
- **2026-08-13** (U5) — **A stand-in ignores the config's `hideNodes`.** Those names describe the
  original model's parts, and a shared name means the wrong thing: the pizza substitute's `PizzaLight`
  is its lamp post, not the light the web build hides.
- **2026-08-13** (U6) — **Model-local offsets need `Convert.ModelOffset`, not `Convert.Pos`.** A
  world position only crosses the handedness change; an offset in a model's own frame also crosses a
  convention change, because three.js faces `-Z` and Unity faces `+Z`. Through `Pos` the chase
  camera lands in the character's face. Z verified against Joe; X is still unverified, since every
  offset ported so far has `x = 0`.
- **2026-08-13** (U6) — **Tank controls carry over.** A/D turn the body, W/S drive along its facing,
  and the camera trails rather than steers. This is the original's design, not a three.js
  limitation, so rule 5 says it stays.
- **2026-08-13** (U6) — **Unity's `CharacterController` replaces the Rapier kinematic capsule plus
  hand-rolled collide-and-slide.** Same behaviour, one component, and it brings `stepOffset` — which
  the web build had no equivalent for and which is what gets Joe up a Florentin curb.
- **2026-08-13** (U5) — **Districts are never `BatchingStatic`.** Batching rebuilds a >65k-vertex
  mesh on a 16-bit index buffer and shreds it, while the collider keeps using the real asset mesh —
  so the world feels right and looks wrong, which is how it survived a checkpoint. Nothing to win
  either way: a district is one to four huge meshes and batching exists to merge small draws. The
  flags are listed one by one in `SetDistrictStaticFlags`, because passing "everything except
  batching" as an all-bits value is normalised back to Everything. See memory
  `static-batching-shreds-big-meshes`.
- **2026-08-13** (U7) — **`Joe.controller` is generated, not hand-authored.** Same reasoning as
  WorldBuilder: a graph built in the Animator window is invisible in review and impossible to
  reproduce. `JoeAnimatorBuilder` rebuilds the asset in place so the GUID survives and the scene
  keeps its reference.
- **2026-08-13** (U7) — **One 1-D blend tree covers the whole gait ladder.** Jog gets no state and
  no clip: at 4.5 m/s it is simply where the blend sits between walk and sprint. A jog clip can drop
  in later as a third threshold without touching anything else.
- **2026-08-13** (U7) — **Root motion stays off; clip cadence is corrected instead.** `Joe_Sprint`
  carries real root motion authored at 5.58 m/s while the controller moves at 7.0, so the blend tree
  plays it at 1.25× rather than letting the clip drive position. The controller owns position
  everywhere on foot. U18's run-over is the deliberate exception.
- **2026-08-13** (U6) — **No Cinemachine yet.** The chase camera is fifteen lines with a specific
  feel to reproduce; a camera framework earns its place at U23's helicopter and U26's menus, not
  here.
- **2026-08-13** (U8) — **The car is a Rigidbody on four WheelColliders, not a port of `vehicle.ts`.**
  The web build's car is kinematic — a scalar speed and heading pushed through a Rapier character
  controller with a ray snapping it to the road — because Rapier's vehicle controller was unusable
  there. That is scar tissue under port rule 5, and PhysX gives real suspension, momentum and
  collisions that U17's traffic, U18's run-over and U19's ramming all inherit for free. Gameplay
  numbers carry (20 m/s cap, 7 m/s reverse, ~34° lock); every physics number is re-derived.
  Chosen by the user over a raycast-suspension middle path and a 1:1 kinematic port.
- **2026-08-13** (U8) — **`config.vehicle`'s physics fields are deliberately NOT in the C# model.**
  `accel`, `brakeDecel`, `friction`, `steerRatio`, `wheelReturn`, `colliderHeight`,
  `colliderBottomGap`, `blockedRatio`, `blockBleedMinSpeed`, `maxClimbRate` and `characterOffset`
  all describe the kinematic car. Under PhysX they are outputs of mass, suspension and tyre
  friction, not inputs. Declaring them would invite someone to wire them up and be wrong, so their
  absence is the statement. Replacements are serialized on `CarController` where they can be tuned
  against the real thing.
- **2026-08-13** (U8) — **`Convert.ModelFacing` is the rotational twin of `ModelOffset`.** three.js
  drives an object down `-Z`, Unity down `+Z`, so a model with a FRONT needs a 180° yaw that a
  district never does. The Mustang proves the two flips compose into exactly that one rotation: its
  `wheel_Front_L_0` bone imports at `(0.992, 0.479, -1.562)` and lands at `(-0.992, 0.479, 1.562)`
  — front and left, which is what the bone calls itself. The same 180° that points the nose down
  `+Z` also puts the L/R names back on Unity's hands.
- **2026-08-13** (U8) — **A car prefab is generated by `CarBuilder`, never assembled by hand.** Same
  reasoning as WorldBuilder and JoeAnimatorBuilder: four WheelColliders dragged into place are
  invisible in review and silently wrong after a re-export. Wheel radius and corner assignment are
  MEASURED off the rig — corners by the sign of the bone's position, never by its name, because the
  X negation makes `_L_` arrive on Unity's right until the facing rotation is applied.
- **2026-08-13** (U8) — **The prefab root's origin is the tyre contact patch.** The model's own
  origin sits 0.1 m below its tyres, so anchoring there makes `config`'s Y-less `spawn` plus
  `roadSurfaceY` directly usable as "put the car here" instead of burying or floating it.
- **2026-08-13** (U8) — **`x-negation-does-not-mirror-text`.** Checked by eye rather than reasoned:
  Reichman's Hebrew sign reads `אוניברסיטת רייכמן` correctly after import. The negate-X convention
  is a change of basis, not a visual mirror, so signage needs no compensation.
- **2026-08-13** (U8) — **Blender exports get `export_image_webp_fallback=True`.** A texture stored
  as .webp in a .blend exports as one, which writes `EXT_texture_webp` into extensions**Required**;
  glTFast cannot read it and rejects the entire file, importing it as a `DefaultAsset` so
  WorldBuilder just says "missing" while the real error hides in the Inspector. The fallback demotes
  it to extensionsUsed. Forcing JPEG would be smaller but drops alpha, and Reichman's flag is an
  alpha decal.
- **2026-08-12** (U1) — **Downtown gets one collider over the whole mesh.** `city.noCollidePatterns`
  matches node *or* material names; `first-one.glb` has no per-object nodes and its only foliage
  material (`AM113_072_Washingtonia_filifera`) matches no pattern — so the shipped web build
  collides with its palms too. This is faithful, not a shortcut. Build the noCollide filtering when
  the first multi-node district lands, not before.
