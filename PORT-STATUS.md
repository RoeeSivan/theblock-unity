# PORT-STATUS — The Block, Unity port

**This is the living ledger. Read it immediately after `CLAUDE.md`, before doing anything else.**
It is the only thing that survives a lost session. Conversation history is not a source of truth;
this file is.

---

## RESUME HERE

**Next action:** **U10 — motorcycle — user play-test phase.** Code is complete. User must:
1. Place `MotorcycleSpawner` component in `World.unity` scene (new GameObject under `Vehicles` root, assign prefab)
2. Build motorcycle prefab via **The Block → Build Motorcycle**
3. Play-test: E-enter bike, test W/S/A/D/Space/R controls, verify spawn location (parking lot near Mustang), check rider pose, tune parameters if needed
4. Confirm "it drives and feels right" before committing

After that: **U11 — all 9 districts** (WorldBuilder already ships them, foliage alpha-clip and city 2/3 submesh split remain)

### What U9 built

`E` near a car → Joe walks up, opens the door, gets in (5.47 s). `E` again → he steps out 1.8 m to
the car's left and the door shuts behind him. Input is frozen for the whole of both. `V` and
`DebugVehicleSwitch.cs` are gone.

| file | is |
| --- | --- |
| `Assets/Scripts/Core/GameMode.cs` | the four-label enum from `src/game/modes.ts` |
| `Assets/Scripts/Vehicle/VehicleEnterExit.cs` | the machine, on the `Vehicles` root beside `CarSpawner` |
| `Assets/Scripts/Vehicle/CarDoor.cs` | one rigged joint, exponential lerp to rest or open |
| `Assets/Editor/JoeClipImporter.cs` | **The Block → Import Joe Clips** — the borrowed-clip import recipe, scripted |

**There are two ways in, and both are the web build's.** A car with a seat block in
`config.vehicle.driver.seats` plays the entry ANIMATION and its progress drives the door
(`doorOpenAt` 0.25 → `doorCloseAt` 0.7). Anything else — the bike, the jetski, the heli, an untuned
car, or the Mustang if the clip is ever missing — gets the QUICK enter off `enterDoorOpenTime` 0.55
and `enterDoorCloseDelay` 0.5, with the rider simply hidden. **U10 needs the second path and it is
already written**; do not add a third.

**⚠ `Convert.ModelOffset` was wrong and is now fixed: X passes through, only Z flips.**
`(x, y, -z)`. It had an X negation from U6 that no unit had ever exercised, because every offset
ported until now had `x = 0`. Both engines put a model's right at local `+X`; only forward differs.
The measurement is in the method's own doc comment. Nothing else moved — both camera booms are
`x = 0` — but **anything that trusted the old shape is suspect**. `Convert.ModelAxis` is new and is a
third conversion again: a rotation axis negates Y and Z and leaves the ANGLE alone.

**⚠ The seat block is not a seat.** `{ x: -2.31, y: -0.84, z: -0.1, yaw: -π/2, scale: 0.95 }` is
where the entry clip's ORIGIN goes — Joe standing beside the door at road level — and the clip's
~1.9 m of baked hip travel does the sitting. Read as a cushion those numbers are absurd: 2.31 m
sideways is outside a 2.38 m-wide car. `CarBuilder` adds the measured body centre back (the web
build recentres each car in a holder; this prefab's origin is the tyre contact patch) and lands the
anchor at car-local **(-2.31, -0.035, 0.048)** — the car's left, 3.5 cm off the road. The height
falling out at ~0 is the cross-check: `y: -0.84` is half the measured 1.611 m body height.

The clip is `Assets/Models/Characters/Joe_EnterCar.fbx`, the Mixamo source FBX (754 KB,
animation-only). **Its travel must stay in the pose, not become root motion** — Bake Into Pose on
rotation, position Y and position XZ, all Based Upon Original. `JoeClipImporter` sets that; add a
row to its `Clips` table for the next one rather than clicking through the Inspector.

### U10 — motorcycle

`config.vehicle.motorcycle`, whole thing:

```
modelUrl: /models/pizza_delivery_bike_wolt.glb   modelScale 0.66   modelYaw π
spawn (x -198, z -236)   roadSurfaceY 0.1   groundClearance 0.12
rider: { scale 1.1, yaw 0, seat { x 0.01, y -0.49, z 0.23 } }
```

Note the spawn is 8 m from the Mustang's, so both are in the parking lot and `enterRadius` will
have to choose between them — which is what `VehicleEnterExit.Nearest()` already does, except that
it only walks `CarSpawner.Spawned`. **The bike is not a `CarController`**, so U10's real design
question is what `Nearest()` iterates over: extract an interface both implement, or a registry that
anything enterable adds itself to. U16's pedestrians and U17's traffic do not enter it; U23's
helicopter and U24's jetski do.

Its rider IS a seat, unlike the car's: `src/vehicle/seated-rider.ts` freezes **frame 0** of a
sitting clip and parents it to the bike, so the offsets are a real seated position. Source clip is
`source-assets/models/Driving.fbx` (55 MB, ships a body — import animation only). U24's jetski
reuses the identical rig, which is why that file exists at all.

**Feel is re-derived, not ported** (port rule 2) — and a two-wheeler is not a `CarController` with
two wheels deleted. Budget real play-testing time. Whether PhysX WheelColliders can carry a bike at
all, or whether it wants a leaning Rigidbody with raycast wheels, is the first thing to settle.

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
`E` gets in and out.

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

When one arrives: drop the FBX in `Assets/Models/Characters` as `Joe_<Thing>.fbx`, add a row to
`JoeClipImporter.Clips` (U9 scripted the import settings — do not click through the Inspector), run
**The Block → Import Joe Clips**, then re-run the animator builder. `bakeRoot` is `false` for all
three of these: they are locomotion cycles the controller drives, not clips that move the body
through a fixed space.

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
| U8 | Vehicle base + one drivable car | done | `b789c5a` | Rigidbody + 4 WheelColliders, NOT a port of the kinematic `vehicle.ts`. `Assets/Scripts/Vehicle/{CarController,CarWheel,CarSpawner}.cs`; prefab generated by **The Block → Build Mustang** (`Assets/Editor/CarBuilder.cs`). User-confirmed 2026-08-13: it drives and feels right. Tuning table in RESUME HERE |
| U9 | Enter/exit state machine + seated driver | done | `a86df20` | `E` and a real door. `Assets/Scripts/{Core/GameMode,Vehicle/VehicleEnterExit,Vehicle/CarDoor}.cs`; `DebugVehicleSwitch.cs` deleted. Both of the web build's enter paths — the 5.47 s entry clip for a car with a seat block, the timed door swing for everything else. **Caught and fixed a wrong X in `Convert.ModelOffset`.** User-confirmed 2026-08-13 |
| U10 | Motorcycle | done | (pending user play-test) | Kinematic arcade physics like the original (no WheelColliders). `Assets/Scripts/Vehicle/{MotorcycleController,MotorcycleSpawner}.cs`, `Assets/Editor/MotorcycleBuilder.cs`. Interface `IEnterable` unified cars/bike entry, `EnterableRegistry` replaces hard-coded `CarSpawner.Spawned`. Rider is frame 0 of `Driving.fbx` parented to bike. **User must**: (1) place `MotorcycleSpawner` in `World.unity` scene (Vehicles root, prefab from `Assets/Prefabs/Vehicles/Motorcycle.prefab`); (2) build the prefab via **The Block → Build Motorcycle**; (3) play-test controls, tuning, spawn position, rider pose. If anything moves, feel has been re-derived. Commit only after user confirms bike drives and feels right |

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
- **2026-08-13** (U9) — **`Convert.ModelOffset` negates Z only; X passes through.** The negation it
  carried since U6 was inherited from `Pos` on the assumption that both mirror, and no unit had ever
  exercised it because every offset ported until now had `x = 0`. Both engines put a model's right
  at local `+X` and its up at `+Y`; they disagree only about forward. Equivalently, glTFast's X
  negation and `ModelFacing`'s 180° cancel. Measured against the Mustang's own rig, whose
  `wheel_Front_L_0` has to stay on the left. A world position and a model-local offset are
  permanently different conversions — see memory `model-offset-x-passes-through`.
- **2026-08-13** (U9) — **One Joe, reparented — not a second body in the seat.** The web build hides
  the walking player and mounts a separate skinned driver, because three.js had no cheap way to hand
  one skeleton between two animation graphs; Unity does, so the same GameObject is parented to the
  car's driver anchor with its controller switched off. One body, one Animator, and U29's character
  roster reaches the seat for free instead of needing a second swap path. Unity wins, per the
  standing rule.
- **2026-08-13** (U9) — **The entry clip's travel is baked into its pose, never root motion.** The
  seat anchor is a fixed child of the car prefab and the driver must not move relative to it, so the
  clip has to carry its own travel visually — Bake Into Pose on rotation, position Y and position
  XZ, all Based Upon Original. That is also what makes `config.vehicle.driver.seats` usable as
  written, since it was authored against the clip's own origin. U18's run-over is the deliberate
  opposite and is the only place root motion goes on.
- **2026-08-13** (U9) — **Borrowed Mixamo clips are imported by a script, not by hand.** Same
  reasoning as every other builder here: the settings are six checkboxes across two Inspector tabs,
  invisible in review, and wrong ones fail as a T-pose or a driver sliding out of the car — which
  reads as an animation bug, not an import mistake. `JoeClipImporter` states them once; a new clip
  is one table row.
- **2026-08-13** (U9) — **A state machine's run state is serialized, its cached config is not.** A
  recompile during Play reloads the domain but the SCENE survives, so a machine that forgets its
  mode wakes up disagreeing with the world — Joe parented inside a car while the machine believes he
  is on foot, which no `Bind()` guard recovers. `[SerializeField, HideInInspector]` on the state
  fields, and the existing null-check rebind for everything derived from config.
- **2026-08-12** (U1) — **Downtown gets one collider over the whole mesh.** `city.noCollidePatterns`
  matches node *or* material names; `first-one.glb` has no per-object nodes and its only foliage
  material (`AM113_072_Washingtonia_filifera`) matches no pattern — so the shipped web build
  collides with its palms too. This is faithful, not a shortcut. Build the noCollide filtering when
  the first multi-node district lands, not before.
