# PORT-STATUS — The Block, Unity port

**This is the living ledger. Read it immediately after `CLAUDE.md`, before doing anything else.**
It is the only thing that survives a lost session. Conversation history is not a source of truth;
this file is.

---

## Standing remark — every unit asks "can Unity do this better?"

**This is a rebuild, not a transcription.** Before building any `U`, ask the question explicitly and
write the answer down in that unit's notes: *what did the web build settle for here because three.js
or Rapier could not do better, and what does Unity give us instead?*

The game must stay the same game — same missions, same world, same feel. But the mechanism
underneath is free, and the point of the port is engine breadth, so a faithful copy of a workaround
is a wasted unit. Where Unity has a real mechanism, take it and make the thing **feel better** than
the original did.

Already banked, as evidence this is a real rule and not a slogan: the facade tint is a material
asset instead of a load-time recolour (U1); `CharacterController` with `stepOffset` replaces
hand-rolled collide-and-slide (U6); the car is a Rigidbody on WheelColliders instead of a kinematic
capsule snapped to the road (U8); one Joe is reparented into the seat instead of a second skinned
body being mounted (U9); the bike leans and has real suspension and real collisions (U10); the
baked-in parked cars are cut out at the submesh level at build time, which the web build had no
edit-time step to do (U11). Still
queued: NavMesh police instead of "drive straight at the player" (U19), Addressables instead of
one big download (U15), UI Toolkit instead of DOM overlays (U25).

The counterweight is port rule 5 in `CLAUDE.md`: **design intent carries, scar tissue does not** —
and telling them apart is the actual work. Tank controls stayed (U6) because they are the design.
Kinematic vehicles went (U8, U10) because they were a Rapier limitation. When it is genuinely
unclear, re-test before inheriting.

---

## RESUME HERE

**Next action: U13 — places.** The gas station and the police station are both ingested and placed
now (the user added them on 2026-08-15), so U13 is no longer blocked on assets. It is also no longer
purely additive: **the gas station is placed wrong and needs fixing** — the user flagged it on
sight after the U12 play-test, so treat it as U13's first job rather than a leftover. Nothing about
its geometry has been diagnosed yet; look at it in the Scene view before theorising, and expect the
usual suspects from `Place_*` work — a collision proxy node, a model lying on its own axis, or a
config y that assumed the original asset. The fix belongs in `WorldBuilder.AssetAliases`, not baked
into the file, same as the pizza place's.

The rest of U13 is the pizza interior and the lot cars.

**U12 is done** — the user confirmed on 2026-08-15 that the roads, the water and the beach all read
right. Last build: **18 placed, 0 missing, 177 colliders**.

### What U12 built

**Roads are splines now, and that is the unit's real answer to the standing question.** The web
build cloned one 8 m tile per A→B segment and rotated it, which is all three.js offered, and it
shows at every bend as two quads overlapping in a hard V. `com.unity.splines` gets a
`SplineContainer` per polyline and `WorldBuilder.Roads.cs` extrudes a ribbon along it, so a corner
is a curve — **1864 m of spline against 1859.5 m of raw polyline**, the 0.24% being exactly the
smoothing.

**The `SplineContainer` is kept on each road object on purpose, not discarded after the mesh.** It
is the reason to use splines at all: U17's traffic and U19's police both want a centreline they can
sample at an arbitrary distance with a tangent, and re-deriving one from the raw polyline would
disagree with the visible geometry at precisely the corners this unit smoothed. The splines are
authored in world space with the object at the origin, so a sampled point needs no transform.

**No collider on the roads**, matching the web build. They sit 2 cm above the plate and flush with
district pavement, and a wheel meeting a 2 cm lip at 20 m/s would feel it.

**The road surface is generated, not the web's `road-straight.glb`.** That tile's paint is
*geometry*, which does not survive being stretched along a curve. `CreateRoadTexture` writes asphalt
plus a double-yellow centre and white edge lines with U across the road and V along it, so the
markings hold a constant pitch through a bend whatever the segment length — the thing the stretched
tile could not do.

**The sea is a port of the original's shader, not a stand-in.** URP has no built-in water (Unity 6's
Water System is HDRP-only) and every free Asset Store option would have meant re-tuning by eye and
throwing away numbers `config.sea.surface` already carries. `Assets/Shaders/Water.shader` runs the
same maths: three vertex swells, two counter-scrolling normal layers, fresnel with the sub-1 ceiling
that keeps far water blue instead of grey, a depth tint, one Blinn glint, a shore foam band.

**⚠ The water shader is UNLIT deliberately.** The original computes its own single-light response,
and putting URP's PBR underneath would light it twice and double the specular. It reads
`_MainLightPosition` / `_MainLightColor`, so the sun still drives the glint. Its shadow casting is
off too: the swells are a vertex displacement the shadow pass does not run, so a cast shadow would
be the flat plane's outline and would not move.

**The beach is a real floor.** `Assets/Shaders/Beach.shader` ports the sand's grain, blotch and
wet-band shading onto a normal PBR surface, and the mesh is displaced to `SeaGeometry.SeabedHeight`
with a MeshCollider — the player walks DOWN it into the water rather than looking at a picture of a
beach.

**⚠ The ground plate's collider had to be trimmed at the shore, and this was not obvious.** The
plate is solid at y −0.05 while the beach ramps to −3, so an untrimmed plate holds the player up on
an invisible sheet a few centimetres under the water and the entire beach becomes scenery. The
visual plane keeps its full 1400 m — the water is opaque and drawn above it — but the solid part now
stops at the waterline, and seaward of that the beach mesh is the only floor. The web build does the
same thing in `physics.addGround` and the comment there is the only reason it was caught.

**The shore wall is on the `Ignore Raycast` layer**, which is Unity's answer to the web's
`markNonGround`. A wall is not a floor: a downward probe started inside it — the side probe on the
exit-a-vehicle path does exactly that — reads its top as ground and lifts the caller 8 m. That layer
is excluded from the default raycast mask, so probes miss it while collision is untouched.

**One source of truth for the waterline: `Assets/Scripts/World/SeaGeometry.cs`.** The sand mesh, the
water shader and the sand shader all key off the same ramp, and a mismatch is a tide line that does
not sit on the water. It also owns the handedness: `config.sea.shoreX` is −430 and the web's sea
runs to more negative x, so **in Unity the sea is EAST, at larger x**, and every derived edge here is
produced by converting the config's own expression rather than re-deriving it with a flipped sign.

**⚠ "Kerbs" were phantom scope.** The ledger said "roads, kerbs and the sea" for months; grepping
the original shows no kerb system exists at all — kerbs are baked into the district meshes and
appear only in comments. U12 is roads + sea. Nothing was skipped.

**⚠ `com.unity.splines` 2.8.x does not compile on Unity 6000.5** — `SplineInstantiate.cs` calls
`Object.GetInstanceID()`, which is obsolete-as-*error* there (`CS0619`, not a warning). **2.9.0 is
the minimum**; it guards the call behind `UNITY_6000_4_OR_NEWER`. And editing `manifest.json` by
hand did nothing: `packages-lock.json` keeps pinning the old version and a refresh never
re-resolves. Install through Package Manager. See memory `package-version-needs-package-manager`.

### What U11 built

**All 9 districts were already placed by U5's WorldBuilder** — the unit's real content was three
rendering faults, and the first one was not what it looked like.

**⚠ The white shards were never a blending problem.** They were the wrong CORNER of the atlas.
`assets_Foliage` is a 512² image whose leaves occupy only u [0, 0.25] × v [0, 0.25] — the
bottom-left sixteenth — and the rest is blank white. glTFast decides per TEXTURE whether the
imported image came out vertically flipped and compensates with a negative Y scale in the material's
`_ST`; on these districts that decision is wrong, and wrong INCONSISTENTLY: `FoliageTrees.001`
through `.004` all sample the same image through four different glTF texture entries and only `.001`
came out unflipped. The other three sampled v ∈ [0.75, 1] — pure white. `WorldBuilder.UnflipV`
takes the flip back out. See memory `gltfast-spurious-v-flip`.

The diagnosis in the old note — `alphaMode: BLEND` with ZWrite off — was real but was the *second*
fault, and fixing only it left the trees exactly as white as before. Alpha clipping went in anyway
and is what makes them read as leaves rather than as translucent smears: hard edges, depth written,
sorted with the opaque geometry, and a shadow with leaf-shaped holes, which a blended canopy cannot
cast at all.

**⚠ `_AlphaClip` on an imported glTFast material does nothing**, which is what the old note was
reaching for. The surface mode is baked at import from the glTF's `alphaMode`. So the alpha-clip
pass builds a separate URP/Lit material asset per district per material and rebinds the slot — the
same answer U1 reached for the facade tint, and the imported material is only ever read.

**⚠ A pattern list matched by substring will surprise you: "tree" is inside "CityGen_S`tree`ts".**
The first build alpha-clipped every district's road surface. The guard is not a better pattern, it
is asking the right question FIRST — `IsBlended()`, because an alpha cutout only ever fixes
something that is blended to begin with, and the name match then only has to choose among those.

**Cities 2 and 3 got a submesh split, in Unity, not in Blender.** Their parked cars are merged into
the same 300k-vertex mesh as the streets and buildings, so `hideMaterials` could not disable the
renderer without taking the district with it. WorldBuilder now rebuilds the mesh without those
submeshes. **The cars were 86% of the geometry** — 186,186 of city 2's 216,515 triangles — so the
surviving vertices are compacted rather than left in place, taking the mesh from 304,797 vertices to
39,121 and the asset to 5.8 MB. They leave collision with the geometry, which matters: an invisible
but solid parked car is exactly what U17's traffic would pile into.

**Empty material slots were rendering magenta.** A glTF primitive that names no material leaves the
Unity slot null and Unity draws the error shader — the small pink rectangles on the pavement in
every procedural district. They now get the glTF spec's default material: white, metallic, rough.
Deliberately drab; inventing a look for it would hide the fact that the asset says nothing there.

**The generated folders are swept every build, and are gitignored.** `Assets/Materials/City/Cutout/`
and `Assets/Meshes/Generated/` are output, so anything in them this build did not write is deleted —
otherwise a corrected pattern list leaves a plausible-looking `.mat` behind that nothing references.
That is how the six stale `CityGen_Streets` cutouts got cleaned up rather than lingering. Both
folders derive entirely from the gitignored district GLBs, so a fresh clone rebuilds them along with
everything else under `World`.

**Foliage still collides — left open on purpose, low priority.** See "Deferred" below.

**MSAA is off** (`PC_RPAsset`, `antiAliasing = 0`), so the `_AlphaToMask` the cutout materials carry
is inert. Turning MSAA on would soften the leaf edges via alpha-to-coverage — a real improvement,
and a global render-quality change with a cost, so it belongs to U30's perf pass and not here.

### U10 tuning knobs, if the bike ever needs re-feeling

**All serialized on `MotorcycleController`** — select the spawned `Motorcycle`
during Play and edit in the Inspector; the values live on `Assets/Prefabs/Vehicles/Motorcycle.prefab`:

| feels wrong | knob | now |
| --- | --- | --- |
| too eager / too slow off the line | `motorTorque` | 950 Nm |
| won't stop, or stops dead | `brakeTorque` | 1200 Nm |
| pitches over the bars braking | `frontBrakeShare` | 0.55 |
| coasts forever / drags to a halt | `coastBrake` | 220 Nm |
| steering too slow or too twitchy | `steerRate` | 200 °/s |
| understeers, or spins at speed | `steerAtTopSpeed` | 0.30 |
| **wobbles, or lies down** | `uprightSpring` / `uprightPredict` | 9000 / 0.35 s |
| leans too much, or not enough | `maxLeanDegrees` | 32° |
| lean snaps instead of rolling in | `leanRate` | 180 °/s |
| `Space` won't step the back out | `skidGrip` | 0.35 |

Suspension, tyre grip, mass, wheel radius and the chassis box are NOT here — they are baked into the
prefab by `MotorcycleBuilder` and live as constants at the top of `Assets/Editor/MotorcycleBuilder.cs`.
Change them there and re-run **The Block → Build Motorcycle**, which rebuilds the prefab in place so
the scene keeps its reference.

Controls while riding: `W`/`S` throttle and brake-then-reverse, `A`/`D` steer, `Space` rear-brake
skid, `R` back to the spawn. `E` gets on and off — the bike sits 8 m west of the Mustang on the lot,
and `E` picks whichever is nearer.

### What U10 built

**U10 is done** — the user confirmed on 2026-08-15 that riding the motorcycle feels right.

**The bike is a Rigidbody on two WheelColliders, not a port of `motorcycle.ts`.** That file is
kinematic — scalar speed and heading through a Rapier character controller with a ray snapping it to
the road — for exactly the reason the car was, and U8 already ruled that scar tissue. What the swap
buys, none of which the web build has: it collides with the world and the cars instead of sliding
through them, it has suspension so a kerb is a bump rather than a teleport, it keeps its momentum
(U18's run-over and U19's ramming inherit that), and **it leans**.

| file | is |
| --- | --- |
| `Assets/Scripts/Vehicle/MotorcycleController.cs` | drive, steer, the upright stabiliser, the lean |
| `Assets/Scripts/Vehicle/MotorcycleSpawner.cs` | one-shot spawn + ground probe, on the `Vehicles` root |
| `Assets/Editor/MotorcycleBuilder.cs` | **The Block → Build Motorcycle** — generates the prefab |
| `Assets/Scripts/Vehicle/IEnterable.cs` | + `UsesEntryAnimation`, `ShowRiderOnQuickMount` |
| `Assets/Models/Characters/Joe_Driving.fbx` | the seated riding pose, imported as `Joe_Ride` |

**A two-wheeled Rigidbody has no roll stability and falls over on frame one.** `Stabilize()` is what
holds it up, and it runs whether or not anyone is riding — a parked bike has to stand there too, and
this model has no kickstand. The torque is a spring toward world up measured against where the roll
will BE in `uprightPredict` seconds rather than where it is now; that look-ahead IS the damping term.
Correcting only the current error makes a pendulum and the bike wobbles forever. As shipped it is
about 3.6× over-damped, which is the safe side of the choice — it will feel firmly held rather than
floaty, and `uprightPredict` is the knob if that reads as stiff.

**The lean is on a separate `Lean` node, and the Rigidbody never rolls.** Rolling a two-wheeler's
body is not a lean, it is a fall. The pivot sits between the prefab root and `Visual`, and the rider
anchor hangs off it too so Joe leans with the bike instead of staying bolt upright on top of it. The
target angle is read off the physics — `tan(lean) = v·ω / g`, the angle at which gravity and the
corner's centripetal force line up — not off the steering key, so a stationary bike does not lean and
a bike sliding sideways out of a `Space` skid still does.

**Wheel geometry is stated, not measured, and that is a property of this asset.** The Mustang's rig
names its own corners; `pizza_delivery_bike_wolt.glb` is two nodes — `Bike` and `WoltBox` — each one
merged mesh with no wheel to find. So the radius is `WheelRadiusFraction` (0.22 of body height →
0.268 m) and the axles go one radius in from each end of the bounding box, which is a fact about
bikes rather than a guess about this model. Nothing visible depends on it: there is no wheel mesh to
spin either, so `CarWheel` has no counterpart here and the shipped web build never rotated one.

**The chassis box's WIDTH is overridden.** Measured bounds are 1.037 m across because they span the
mirrors and the bars; colliding as a metre-wide brick makes the bike handle like a car in traffic.
`ChassisWidth` forces 0.5 m — bike plus rider. Length and height stay measured.

**The rider seat block IS a seat, unlike the car's.** `{x: 0.01, y: -0.49, z: 0.23}` is measured from
the body centre and lands at prefab-local **(0.01, 0.238, -0.23)** once the centre is added back —
the same correction `CarBuilder` applies, and the arithmetic reproduces the web build's rider height
exactly (`surface + 0.728 − 0.49`), which is the cross-check that the centre-add-back is right.
`Convert.ModelOffset` for the offset, `Convert.RotFromRadians` for the yaw with **no** extra π: the
web build adds one to turn a Mixamo body that faces `+Z` in a `-Z`-forward engine, and Unity's
forward already is `+Z`.

**`Nearest()` now walks `EnterableRegistry`, and vehicles register THEMSELVES.** Registration moved
out of the spawners in `OnEnable`/`OnDisable`, because a spawner cannot know when its vehicle is
destroyed and a stale entry means `E` aims at a corpse. `EnterableRegistry.All` also sweeps dead
entries on the way out — a destroyed MonoBehaviour reached through an *interface* reference does not
compare equal to null, since the overloaded operator lives on `Object` and an interface does not
carry it, so the sweep goes through the concrete type to ask the question at all.

**⚠ `VehicleEnterExit.activeVehicle` was `[SerializeField]` on an interface type, which Unity cannot
serialize.** It silently stored nothing, so the whole mid-Play-recompile guard that field exists for
was doing nothing for the one piece of state the scene cannot rebuild. It is now stored as a
`MonoBehaviour` and cast back through a property.

**No third enter path was added** — the two flags on `IEnterable` parameterise the quick mount
instead. `UsesEntryAnimation` is false on the bike, so it never plays Joe's car-entry clip (it would
have: that test was `RiderAnchor != null && clipLength > 0`, and both are true on a bike).
`ShowRiderOnQuickMount` is true, so the rider stays visible rather than being hidden as an untuned
car's is. A door-less vehicle also skips the door timings entirely and mounts in
`doorlessMountSeconds` (0.35 s) rather than paying config's 1.05 s of waiting for a swing that does
not exist.

**`Joe_Driving.fbx` is the 55 MB with-skin Mixamo download**, same as `Joe_Sprint` and `Joe_Jumping`.
Only its animation is used; its body never appears. It imports as `Joe_Ride`, 5.00 s, looping, root
baked into pose, and `JoeAnimatorBuilder` gives it a `Ride` state off an `Any State` transition on a
new `Ride` bool. U24's jetski reuses that exact state, which is why the clip is its own file.
The `Ride` parameter is declared even when the clip is missing, because `PlayerAnimator` sets it on
every mount and `SetBool` against an absent parameter warns once per call forever.

**Verified in Play with synthetic input** (the rest is the user's to judge): spawns at Unity
(198, −236) upright with `upness` 1.0000 and both wheels grounded; the registry holds the Mustang and
the bike 8 m apart, so `Nearest()` genuinely has to choose; `E` mounts in 0.35 s with Joe parented to
`RiderAnchor`, `Joe_Ride` looping, all 9 renderers on and the camera 7.0 m back on config's boom;
throttle gives ~11 m/s² and tracks dead straight — 153 m with `x` unchanged to 2 decimal places.
**One bug caught that way and fixed**: at the 20 m/s cap the motor cut but nothing bled the
overshoot, so it held 22.6 m/s. `capped` now takes the coast brake as well.

**Two things the user did by hand that were quietly wrong, both corrected:** the scene's
`MotorcycleSpawner.motorcyclePrefab` pointed at `pizza_delivery_bike_wolt copy.prefab` — the raw
imported model, which has no `MotorcycleController` — and the GLB itself was named
`pizza_delivery_bike_wolt copy.glb`, which no exact-name lookup would find. The stray prefab is
deleted, the GLB carries its config name, and the spawner points at `Motorcycle.prefab`. The spawner
now says so by name if it is ever handed a raw model again.

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

**The ground plate** is a 1400 × 1400 m plane at y −0.05 from `config.ground`, pulled forward into U8
because the districts are islands: a car that left one had nothing under it and fell forever. It
sits marginally below every district so district ground always wins a ground probe. **Its collider
stops at the shore** (U12) — see the U12 notes for why an untrimmed one deletes the beach.

Last build: **18 placed, 0 missing, 177 colliders** — the plate, the roads, the water, the beach,
the shore wall, 9 districts and 4 places.

**Every config asset is now ingested.** The gas station and police station landed 2026-08-15; the
gas station's *placement* is wrong and is U13's first job.

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

**Known issues — all three were U11's.** The white foliage and the mixed car renderers are fixed;
see "What U11 built" above. The merged-mesh colliders are not fixed and not forgotten — they moved
to the **Deferred** section, with the trigger that would make them worth doing.

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
| U10 | Motorcycle | done | `80f7fa4` | Rigidbody + 2 WheelColliders + an always-on upright stabiliser + a visual lean, NOT the original's kinematic model. `Assets/Scripts/Vehicle/{MotorcycleController,MotorcycleSpawner}.cs`, `Assets/Editor/MotorcycleBuilder.cs`. `IEnterable` gained `UsesEntryAnimation` + `ShowRiderOnQuickMount` so one enter/exit machine still serves both; vehicles now self-register with `EnterableRegistry`. Rider is `Joe_Driving.fbx` → `Joe_Ride`, a real looping state, parented to the bike's seat. **Caught and fixed: an interface `[SerializeField]` Unity was never serializing, and a speed cap that held 22.6 m/s against 20.** User-confirmed 2026-08-15: riding feels right |

### Tier 3 — World
| id | unit | state | commit | notes |
| --- | --- | --- | --- | --- |
| U11 | All 9 districts via WorldBuilder | done | `21857c3` | Placement and colliders shipped in U5; U11 is the three rendering faults that survived it. Foliage: the white shards were a spurious V flip in glTFast's `_ST`, NOT the blend mode — `WorldBuilder.UnflipV`, plus a real alpha-clip pass that rebinds to generated URP/Lit materials because `_AlphaClip` on an imported glTFast material is inert. Cities 2/3: baked cars stripped at the SUBMESH level in Unity — 86% of the mesh — instead of a Blender split, out of collision as well as sight. Empty material slots were drawing magenta and now get glTF's default material. **Caught and fixed: a substring pattern list that alpha-clipped every road, because "tree" is inside "CityGen_Streets".** Foliage colliders left open on purpose — see Deferred. User-confirmed 2026-08-15 |
| U12 | Roads, ground, sea | done | `7dc8208` | Roads are `com.unity.splines` + a generated ribbon, NOT the web's per-segment stretched tile: 1864 m of spline vs 1859.5 m of polyline, corners curved, markings continuous through them. The `SplineContainer`s are kept as U17/U19's centreline. Road surface texture is generated because the web tile's paint is geometry. Sea is a port of `sea-surface.ts` into `Assets/Shaders/{Water,Beach}.shader` (URP has no built-in water) — unlit on purpose, since the original does its own lighting. Beach is a displaced MeshCollider you walk down. `Assets/Scripts/World/SeaGeometry.cs` owns the waterline and its handedness — the sea is Unity **+x**. **Caught and fixed: the ground plate's collider held the player up over the whole beach; it now stops at the shore. "Kerbs" were phantom scope — no such system exists in the original.** Splines needs ≥2.9.0 on Unity 6.5. User-confirmed 2026-08-15 |
| U13 | Places — pizza + interior, gas, police station, lot cars | todo | | **Starts with a fix, not an addition: the gas station is placed wrong** (user-flagged 2026-08-15, undiagnosed). Both station GLBs are ingested and building. Correction goes in `WorldBuilder.AssetAliases` |
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

## Deferred — known, low priority, fix if it ever becomes worth it

**Not** the decisions log: these are open, and picking one up needs no permission. Each says what
would trigger it. A `wip` unit is work half-done; this is work deliberately not started.

- **Foliage collides.** `noCollidePatterns` matches node or material names and a merged district has
  neither, so each district takes 2–4 whole-mesh colliders with the palms inside them — the same
  hole the web build has. **The fix is now cheap**: U11's `Compact()` already builds a mesh from a
  chosen subset of submeshes, so a foliage-free COLLIDER mesh is that call again with the foliage
  submeshes dropped, assigned to the `MeshCollider` instead of the `MeshFilter`. The cost is what
  holds it back — a second full copy of every district's geometry in memory, for canopies that start
  above head height and that neither Joe nor a vehicle can reach today. **Trigger:** anything that
  gets a player INTO a canopy (U23's helicopter is the obvious one), or a U30 profiler pass that
  makes it a memory question rather than a gameplay one.

---

## Decisions log

Dated one-liners. These are settled — do not re-litigate them without the user reopening.

- **2026-08-15** — **Every unit opens with "can Unity do this better?"** and closes with the answer
  written into its notes. Not a new decision so much as the 2026-08-12 "Unity-idiomatic, same game"
  call promoted to a per-unit checklist item, because it kept getting remembered only after the fact.
  Same game, better mechanism, better feel. See the standing remark at the top of this file.
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
- **2026-08-15** (U10) — **The bike is a Rigidbody on two WheelColliders, not a port of
  `motorcycle.ts`.** Same call as U8's car and for the same reason: the web build's kinematic
  speed-and-heading model is a Rapier workaround, not a statement about two-wheelers. It buys real
  collisions, suspension, momentum and a lean. Gameplay numbers carry (20 m/s cap, 7 m/s reverse,
  ~34° lock); every physics number is re-derived.
- **2026-08-15** (U10) — **The lean is visual, on its own pivot; the Rigidbody stays upright.**
  Rolling the body of a two-wheeler is not a lean, it is a fall. The rider anchor hangs off the same
  pivot so Joe leans with it. The angle is read off `v·ω / g` rather than off the steering key, so it
  is right during a skid and absent when parked.
- **2026-08-15** (U10) — **A two-wheeler needs an active upright torque, always on.** Two contact
  points give a Rigidbody no roll stability whatsoever, riderless or not, and this model has no
  kickstand. The damping term is a look-ahead on angular velocity, not a `-kω` — correcting only the
  present error makes a pendulum.
- **2026-08-15** (U10) — **Enterable vehicles register themselves, in `OnEnable`/`OnDisable`.** A
  spawner cannot know when its vehicle dies, and a stale registry entry is `E` aimed at a corpse.
  The registry also sweeps dead entries itself, because a destroyed MonoBehaviour reached through an
  INTERFACE reference does not compare equal to null — the operator is on `Object`, and an interface
  does not carry it.
- **2026-08-15** (U10) — **The quick mount is parameterised, not duplicated.** Two defaulted members
  on `IEnterable` (`UsesEntryAnimation`, `ShowRiderOnQuickMount`) cover the difference between
  getting into a car and getting onto a bike; a door-less vehicle also skips the door timings rather
  than waiting 1.05 s for a swing it does not have. U23's helicopter and U24's jetski are meant to
  land as two more flag values, not a third code path.
- **2026-08-15** (U10) — **A `[SerializeField]` on an interface type serializes NOTHING.** Unity
  writes no value and gives no warning, so `VehicleEnterExit`'s mid-Play-recompile guard was silently
  not guarding the one field it most needed to. Store the concrete `MonoBehaviour` and cast back.
- **2026-08-15** (U11) — **Cutout foliage is a generated URP/Lit material, not a setting on the
  imported one.** glTFast bakes the surface mode into its Shader Graph material at import from the
  glTF's `alphaMode`, so `_AlphaClip` on it is inert — the fix has to be a separate material asset,
  which is the same call U1 made for the facade tint and for the same reason. The imported material
  is read for its texture and factors and never written. Its metal-roughness and occlusion MAPS are
  deliberately not copied: glTF packs those channels differently from URP/Lit, so carrying them
  across would be silently wrong. None of the materials this touches has one.
- **2026-08-15** (U11) — **Which blended materials are really cutouts is a port-side judgement, and
  the leftovers get named in the build report.** The web build had one material path and never made
  the distinction, so there is nothing in `config.ts` to port. `CutoutMaterialPatterns` decides, and
  every material still transparent after the pass is listed under STILL BLENDED — so a wrong call
  shows up as a list to check rather than as a mystery.
- **2026-08-15** (U11) — **Ask `IsBlended()` before matching the name.** Patterns are substrings and
  "tree" is inside "CityGen_S`tree`ts", which alpha-clipped every road surface on the first build.
  A tighter pattern is not the fix; the precondition is, because a cutout only ever repairs
  something that is blended to begin with.
- **2026-08-15** (U11) — **Baked-in parked cars are stripped at the submesh level in Unity, not
  split in Blender.** WorldBuilder owns the mesh at build time, so the split is a build step and the
  .glb on disk stays as downloaded — the same principle as `AssetAliases`. The vertices are
  compacted, not just the indices dropped: the cars are 86% of city 2's triangles. Stripping also
  takes them out of collision, which tinting or hiding would not have.
- **2026-08-15** (U11) — **Generated asset folders are swept every build.** `Cutout/` and
  `Meshes/Generated/` are build OUTPUT, so anything in them the current build did not write is
  deleted. Without the sweep they are append-only and a corrected pattern list leaves behind a
  plausible-looking material that nothing references — the same invisible-and-unreproducible failure
  that keeps the world out of the scene file.
- **2026-08-12** (U1) — **Downtown gets one collider over the whole mesh.** `city.noCollidePatterns`
  matches node *or* material names; `first-one.glb` has no per-object nodes and its only foliage
  material (`AM113_072_Washingtonia_filifera`) matches no pattern — so the shipped web build
  collides with its palms too. This is faithful, not a shortcut. Build the noCollide filtering when
  the first multi-node district lands, not before.
