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
edit-time step to do (U11); the map is a live second camera rather than a boot-time bake (U14); the
districts' textures are extracted out of their .glbs so Unity's per-platform compression can run on
them at all, which glTFast's sub-assets had silently skipped (U15); and a rammed traffic car becomes
a real Rigidbody wreck, which thirty Rapier vehicles could never have afforded (U17). Still queued:
NavMesh police instead of "drive straight at the player" (U19), UI Toolkit instead of DOM overlays
(U25).

**U17 adds a second kind of answer, and it is not a Unity feature at all: measure the original.**
Its population is not a number anyone picked — it is 130 cars over 12,759 m of network, read off the
web build's own config and applied per metre of street in range. The version with a chosen constant
gridlocked; the version that asks the original what its density was does not. Where the shipped game
already encodes a decision, porting the DECISION beats porting the number.

**U15 is also the rule's counter-example, and the more useful one.** Its planned answer was
Addressables, and the measurement said no: streaming 13.5 GB in chunks is still 13.5 GB, and the
real fault was a format nothing had ever set. "Can Unity do this better?" has to be allowed to
answer *not like that* — the question earns its place by being measured, not by producing a Unity
feature every time.

The counterweight is port rule 5 in `CLAUDE.md`: **design intent carries, scar tissue does not** —
and telling them apart is the actual work. Tank controls stayed (U6) because they are the design.
Kinematic vehicles went (U8, U10) because they were a Rapier limitation. When it is genuinely
unclear, re-test before inheriting.

---

## RESUME HERE

**Next action: the user play-tests U17.** It is built, committed (`2ea3c54`) and measured, and the
scene is saved with it. U17 is `wip` until the user says it reads right — everything below is what
to look at and what is already known-good.

**What to drive around and judge** (I cannot see the Game view; these are the questions I could not
answer by measurement):

1. **Which side is the traffic on?** It should be the right, Israeli-style. Verified by arithmetic
   against the web build's own lane expression, never by eye.
2. **Do the poles read as traffic lights?** One per approach, on the kerb to the driver's right,
   head facing the oncoming cars. Red / red+amber / green / amber, 26 s cycle, junctions desynced.
3. **Does the queueing feel like traffic** rather than like cars taking turns to be stuck?
4. **Ram one.** It should become a real wreck and stay one until you drive away. `wreckOnImpact` on
   `TrafficCar` turns that off in one click if it is bad.
5. **Density.** 13 cars around the starting lot is the shipped game's own density (below), which
   may read as quiet. `densityScale` on `TrafficSystem` is the knob — it is live in Play.

**`T` toggles all traffic off and on in Play**, the same debug affordance `C` gives the crowd. Both
are debug-only and both can go once U17 is confirmed.

**Already measured, so do not re-derive:** 97 nodes / 142 streets / 70 lit / 230 crossings — the same
numbers U16 had, because it is now literally the same graph object. 12,759 m baked at 2 m samples,
233 poles. Sim cost **0.029 ms per physics step**, lights **0.012 ms per frame**. Over 3½ minutes:
13 live against a target of 13, no gridlock, nobody reaching the stuck escape, no car more than
0.25 m off its lane centreline, every car's Y inside the road band, 230/230 crossings gated.

**If it needs another pass, the knobs are all serialized on `TrafficSystem`** (select
`World/Traffic` during Play): `densityScale`, `cullDistance`, the spawn ring, and every number from
`config.traffic`. Nothing needs a rebuild to try.

**U17b is the next unit after this one** — carjacking, and generalising `CarBuilder` past the
Mustang. It also inherits U13's deferred lot-car promotion. See its row.

**Rebuild order:** The Block → **Build NPC Animator** → **Build Pedestrians** → **Build Traffic
Cars** → **Build World + NavMesh (slow)**. Plain **Build World** is the fast path and KEEPS the last
bake — it lifts the `Crossings` group, the carve volumes and the `NavMeshSurface` out of the old root
and re-attaches them, re-binding `NavMesh.asset` from disk (see the U17 decision: the component copy
alone silently dropped it), and it never sweeps `Assets/Navigation/Generated/`. Run the slow one
after anything that moves a district or a street. In practice "slow" is ~3 s at 0.4 m voxels; the
name is a warning that the bake is main-thread with no progress bar. **The traffic pass runs on both
paths** — nothing in it bakes, and the lights must come from the same graph the crossings did.

**U16's performance note** (user's call, 2026-08-15: *"flag this step as low performance, we will
try to make it better later"*). Measured, not guessed — the numbers are in the decisions log:
- The crowd's steady cost is ~0: frame time is the same with the crowd on and off (`C` toggles it in
  Play; that key is debug-only and can go). What stuttered was the SPAWN BURST — 90 agents
  instantiated, warped and pathed in one frame — and the vendor's five-LOD skinned meshes stacking up
  to 2,960 SMRs. Both are fixed (trickled spawn, LODs 0+2 only). What is left to improve is
  density: 60 live agents at 20–60 m reads as a street but not a busy one, and pushing it up means
  looking at NavMesh agent count and skinning cost together, with the profiler, not by feel.
- The 111 build warnings (`Main Object Name … does not match filename`) are U15's compressed
  material clones keeping their source name inside a district-prefixed file. Cosmetic, from URP's
  material upgrader. One line in `WorldBuilder.Textures.cs` (`material.name = fileName`) silences
  them; not done yet.

**U7b is done** — swimming, user-confirmed 2026-08-15. It was **never a row in the 32**: the web
build has the state, the sequence forgot it, and the port would have shipped a sea that drowns you.
Filed under U7 because it is one more pose on that state machine. See *What U7b built* below for the
three things that were nearly wrong — the capsule-centre offset, the per-frame damping, and the
shore wall that has to block cars while letting a swimmer through.

**Worth a look while planning the rest:** the same "is it in config.ts but not in the 32?" question
has not been asked systematically. Swimming was found by accident, from a question about animations.

### What U17 built

| file | is |
| --- | --- |
| `Assets/Scripts/Traffic/TrafficNetwork.cs` | the baked graph asset + `SampleLane` / `PointAt` |
| `Assets/Scripts/Traffic/TrafficGeometry.cs` | `traffic-ai.ts`: the front cone, the 2D SAT, the junction bezier |
| `Assets/Scripts/Traffic/TrafficSystem.cs` | the pool and the drive loop, in `FixedUpdate` |
| `Assets/Scripts/Traffic/TrafficCar.cs` | one car's state, its paint swap, and the wreck flip |
| `Assets/Scripts/Traffic/TrafficLightSystem.cs` | the 70 controllers; fills `Crossing.Gate` in `Start` |
| `Assets/Scripts/Traffic/TrafficLightPole.cs` | three lamps as ONE renderer with three submeshes |
| `Assets/Editor/WorldBuilder.Traffic.cs` | bakes the network, places 233 poles, wires the scene |
| `Assets/Editor/TrafficCarBuilder.cs` | **The Block → Build Traffic Cars** — 3 prefabs + paints |
| `Assets/Models/Props/traffic-light.glb` | the shipped 65 KB pole, transcoded (see the decision log) |

**The lamps are one renderer, not three.** The model animates coloured discs sliding behind a
translucent lens — unusable in three.js and no better here, plus the whole model is ONE `BLEND`
material, so 233 poles would have sat in the transparent queue. Those nodes are destroyed at build,
the housing is rebuilt as an opaque URP/Lit asset, and three quads become one mesh with three
submeshes positioned from the original lamps' own bounds. Switching a light is an assignment into a
shared-material array, so every pole showing the same state still batches.

**Handedness: the lane offset is `Cross(up, tangent)`, not the web's `(-tz, tx)`.** Those are the
same physical side written for opposite handednesses, and transcribing the arithmetic literally puts
every car in the oncoming lane. Cross-checked against the web build's own expression on the 3-lane
avenue: both land the inner lane at Unity x +5.30.

**Not ported, on purpose:** `config.traffic.hijack` (U17b), and `carCount` is read but is not the
pool size — it is one half of the density the pool is sized from.

**U15 is done** — the user confirmed on 2026-08-15. The measurement its row demanded came back loud
and rejected Addressables: 13.5 GB of scene memory, 96% textures, because glTFast's .glb textures
are sub-assets no `TextureImporter` ever compresses. The unit became the compression pass instead
(see its row and the decisions log). **13,498 → 3,204 MB.** The pipeline is **The Block → Compress
Textures** once after any district .glb changes (~4 min, writes `Assets/Textures/Generated/`), then
every **Build World** rebinds automatically. Both are run; the scene is current.

**Two U12-era faults surfaced by that play-test are fixed and confirmed** (2026-08-15), neither
caused by U15 — both in the decisions log:

1. **`config.fog` was never ported**, so the 320 m far plane sliced the skyline. The world draws to
   1500 m with the config's haze rescaled onto it (328–1313 m, `#9FB8D4`); shadows 50 → 150 m.
   `Assets/Scripts/World/Atmosphere.cs` owns the far plane and the fog band **together**.
2. **The ground plate showed through the sea's wave troughs** — 0.37 m of swell against a plate at
   −0.05 m. `WorldBuilder.BuildGroundMesh` cuts the sea's rectangle out of the plate.

**What U16 built** (all of it re-runnable; the numbers below are from the build that is in the scene
now — **22 placed, 0 missing, 288 colliders**, 22 because Navigation reports itself as a placed item):

- `Assets/Editor/WorldBuilder.Navigation.cs` — the traffic graph (97 nodes / 142 streets, ported
  from `traffic-graph.ts`), **172 `Not Walkable` volumes** carving all 12.7 km of carriageway,
  **230 zebra crossings** on **70 lit intersections** (3 approaches dropped — street under 20 m),
  and the NavMesh bake: **963 × 805 m @ 0.25 m voxels**, districts only, from PhysicsColliders.
- `Assets/Scripts/Npc/` — `Crossing` + `CrossingRegistry` (the gate), `Pedestrian` (agent + manual
  kerb control), `CrowdSpawner` (pool of 40 following the player), `NpcAppearance` (face × shirt).
- `Assets/Editor/NpcAnimatorBuilder.cs` → `Npc.controller`; `Assets/Editor/NpcBuilder.cs` → 12
  `Assets/Prefabs/Npc/Ped_*.prefab`.
- `TheBlockConfig` gained `TrafficSpec` / `StreetSpec` (+ a `JsonConverter`, because the exporter
  emits a street as either a bare point array or an object with lane metadata) and `LightsSpec`.
- Scene: one `Crowd` root holding `CrowdSpawner` with all 12 prefabs.

**Two things the plan had wrong, corrected here so they are not re-derived:**

1. ~~"the vendor prefabs carry all 5 LODs as ~30 always-on SkinnedMeshRenderers with NO LODGroup"~~
   — **false.** Each character prefab has a real 5-level `LODGroup` (6 renderers per level, screen
   heights 0.7/0.4/0.2/0.05/0) and an Animator already bound to `npc_hmn_01mAvatar`. There was no
   perf problem to solve and no bone rebinding to do. `NpcBuilder` exists for a different reason —
   see (2) — and for adding the agent, the capsule and the appearance table.
2. ~~"the web build has no crosswalks"~~ — **false, and it was my claim, from grepping `crosswalk`
   when the code says `crossing`.** `traffic.ts:99-124` derives one zebra per approach of every lit
   intersection and `crowd.ts:43` walks two dedicated crossers over each. What the web build has
   NOT got is any connection between those crossings and the rest of the crowd.

**The real U16 gotcha:** the pack's 12 prefabs reference `npc_casual_set_00/Materials`, which is the
**built-in Standard** shader, while the URP twins sit unused in `MaterialsUPR` beside them — same 54
names, unrelated GUIDs. Dropped in as-is every pedestrian renders magenta. `NpcBuilder.RebindToUrp`
rebinds by name: **455 slots**. Memory: `asset-store-prefabs-ship-built-in-materials`.

**Known and deliberate:** rooftops bake walkable — the bake cannot tell a flat roof from a pavement,
and downtown is one mesh so there is nothing to mark. Both the spawner and the re-target reject
samples more than a storey off the current height. If anyone is ever seen on a roof, that band is
the thing to tighten, not the bake. **The car park is excluded outright** (`UnwalkableDistricts` in
`WorldBuilder.Navigation.cs`) — one open slab swallowed the whole spawn ring, and the web build never
seeded people there either.

**U14 is done** — the user confirmed on 2026-08-15 that the minimap and the `M` map read right.

**U13 is done** — the user confirmed on 2026-08-15 that the station, the lot and the interior all
read right. Current build: **21 placed, 0 missing, 288 colliders** — 21 because U15's atmosphere
pass reports itself as a placed item; it was 20 through U14.

**One thing carried forward, deliberately, into U21:** the interior *looks* right but its
**mission mechanics are not settled** — the user's words on accepting it. Nothing is broken; what is
missing is the shape the delivery mission wants from the room (where the counter hand-off happens,
what the exit pad means once you are carrying pizzas, whether stepping out should be the thing that
starts the shift). U21 owns that, and it is expected to change `Assets/Scripts/World/Interior.cs`
rather than build beside it. Do not treat the current doorway behaviour as settled design.

**U12 is done** — the user confirmed on 2026-08-15 that the roads, the water and the beach all read
right.

### What U14 built

**The base layer is a live camera, and that is this unit's answer to the standing question.** The
web build bakes the world top-down once at boot into a 2048² render target, reads it back into a
canvas and draws that image under everything — and skips the bake outright on touch, because the
cost is not the resolution, it is that rendering the whole world once compiles every shader and
uploads every texture in the same frame. Unity renders a second camera like any other, so
`Assets/Scripts/UI/MapCamera.cs` is an orthographic camera pointed straight down into a 1024²
RenderTexture: no readback, no boot spike, and the map shows the world as it *is* — parked cars,
and later U17's traffic and U19's police cars, moving on it. `config.map.bakeRes` and
`districtFill` are therefore not ported, and `TheBlockConfig.MapSpec` says why in place.

**Both states redraw at 12 fps, which is one step past the web build.** It caps only the collapsed
minimap and lets the open map repaint every frame so panning stays responsive; there is nothing to
pan here — the open map is fixed on the whole world — so the cap covers both, and the thing being
skipped is a full second camera pass over the city rather than a canvas repaint.

**The overlay is UI Toolkit, arriving eleven units before U25 said it would.** The map *is* UI, so
the choice could not be deferred: `MapView` paints district outlines, POI dots and the player arrow
in `generateVisualContent` with Painter2D — near enough a 1:1 port of the canvas code — and labels
are pooled `Label` children, because Painter2D draws shapes and has no text. The web build's greedy
first-come label guard ports exactly: districts claim their rectangle before POI names, and a label
that would overlap an earlier one is dropped rather than stacked. `HudBuilder.cs`
(**The Block → Build Map HUD**) creates the `PanelSettings` and theme asset a fresh URP project does
not have, plus the HUD and Map Camera objects; it is idempotent, like WorldBuilder.

**Orientation is verified, not assumed.** The map camera sits at `(90, 180, 0)`, which puts screen
right on world **−X** and screen down on world **+Z** — the web map's own frame, since its `+x` is
Unity's `−x`. So the sea is on the left in both, and the overlay's world→panel transform is written
against the camera's actual `transform.right`/`up` rather than a guess. Measured in Play: with the
player facing world `+Z`, the arrow draws tip-down.

**`MapRegistry` is the flexibility hook, and missions are its real customer.** It is the port of
`world/registry.ts` — static, so a district's outline outlives the meshes it was measured from
(U15's streaming needs exactly that), and cleared on entering Play rather than trusted to be empty.
`AddPoi`/`RemovePoi` by name is what U20's campaign director and U21's delivery will hang their
objective markers on.

**⚠ A `PlaceSpec` in `config.ts` has no `name`** — the pin's label is typed into `map-pois.ts`, not
read off the place. Reading the missing field back gave all four landmarks a null label, which is a
`NullReferenceException` in the label pass and not a blank pin. The names are literals in
`MapPois.cs` now, and both label placers skip a nameless pin.

**Emoji POI glyphs are not ported.** The web build draws `⛽`/`🚓`/`🏪` instead of the dot for those
three pins; Unity's default UI font has no emoji, so they would render as boxes. Every pin gets its
kind-coloured dot and its name until U25 settles HUD typography and can add an emoji-capable font.

**Not in this unit, on purpose:** cop blips (`drawCops`) belong to U19 with the pursuit that
produces them; the rival arrow and arena ring (`map-rival.ts`) to U32 with multiplayer; and the dev
zone-paint / road-draw tools are authoring tools for `config.ts`, which is authored in the original
repo, so they have nothing to author here.

### What U13 built

**The gas station was lying on its side, and the cause is the same one the pizza place had.** Its
Sketchfab export wraps the model in `Sketchfab_model` (Rx −90) → `GLTF_SceneRootNode` (Rx +90) — a
pair that cancels in three.js and does not survive glTFast, so the model arrived with its local Y and
Z swapped: 24.5 m "tall" (that was its depth), 13.1 m "deep", and its base 5.36 m below the road. It
now measures 27.6 × 13.1 × 24.5 m with its base on y 0. The fix is `Rx(-90)` in
`WorldBuilder.AssetAliases`, and the entry has **no `File`** — that is the new part: the table now
distinguishes a stand-in for a model we do not have from a correction to the real asset, because a
stand-in must skip the config's `hideNodes` and the real asset must not. The build report says
"corrected on import" rather than "stand-in".

**Lot cars are real GameObjects, and that is this unit's answer to the standing question.** The web
build draws them as one `THREE.InstancedMesh` per source mesh per model, because a few hundred cloned
cars would be thousands of draw calls in three's forward renderer. That is the right answer there and
the wrong one here: an InstancedMesh is a single renderable with one bounding sphere over the whole
lot, so every instance draws whenever any part of the lot is on screen and nothing can be culled
individually. Unity GPU-instances identical mesh/material pairs anyway, so the draw-call win survives
while each car is culled on its own bounds and carries an `LODGroup` that stops drawing it past
180 m.

**The layout is ported bit for bit, PRNG included.** `Mulberry32` is the web build's generator in
`uint` arithmetic (`Math.imul` is a wrapping 32-bit multiply, `>>>` is what `uint` shifts already
are), so seed 1337 produces the same lot: **101 cars, 40 Tesla / 18 Audi / 43 Avenger**, none of them
inside the `keepClear` rectangle the Mustang and the bike spawn in. The grid is generated in the web
build's own coordinates and each car is converted at placement — converting `bounds` and `keepClear`
first would swap their X ends and invert every comparison in the loop.

**Paint is a generated material per model per colour, not a per-instance colour.** The web build
clones the body material white and drives the colour per instance because InstancedMesh has nowhere
else to put it; here it is a material asset (same call as U1's facade tint and U11's cutouts) and
that is also what KEEPS the instancing — a `MaterialPropertyBlock` would give every car its own draw
call. 18 materials for the whole lot. The paint slot is found by material name (`CarPrimaryColor`, or
`primary` on the Tesla), the same convention the web build matches on, and the colour goes into
glTFast's `baseColorFactor` as sRGB (memory: `gltfast-basecolorfactor-gamma`).

**⚠ `tesla.glb` and `avenger.glb` would not import at all: required WebP.** Both name
`EXT_texture_webp` in `extensionsRequired`, which glTFast cannot read, so Unity imported them as
`DefaultAsset`s and WorldBuilder could only say "missing" — the same trap U8 hit from the Blender
side and solved with `export_image_webp_fallback`. These have no source asset anywhere and cannot be
re-exported, so `tools/glb-webp-to-png.py` transcodes the embedded images (JPEG where there is no
alpha, PNG where there is), flattens the extension's texture indirection and drops it from
`extensionsRequired`. Geometry is untouched — Draco stays compressed. Run it once per file; the
result is what is committed.

**The car's box is measured off an UNROTATED probe.** Renderer bounds are world-space and
axis-aligned, so measuring a car already turned into its stall gives the bounding box of a bounding
box, which grows with the yaw. The probe is also where the ride height comes from: the car is placed
by its own underside against `lotCars.y`, not by the web build's "recentre the body and add half the
height", which assumes a centred pivot. And the `BoxCollider` divides the measurement back out by
the model scale — **the Avenger is scale 37.4**, so skipping that makes its collider a kilometre wide
and nothing can get into the lot at all.

**The interior is a real room a kilometre away, entered by teleport** — the web build's design, and
it carries: a second Unity scene would stop the street simulating the moment you walk in, which
U21's delivery timer and U19's wanted level both care about. `Assets/Scripts/World/Interior.cs` owns
the doorway; WorldBuilder writes its fields through `SerializedObject` at build time.

**Two of the web build's three interior chores turned out to be three.js tax.** Its room lights are
switched off while you are on the street because three's forward renderer charges every light against
every shaded fragment city-wide; URP culls per object, so three lamps a kilometre away cost nothing
and simply stay on. Its sun is dimmed on entry to keep daylight out of the room; the room has a
ceiling and URP shadows it. What is left is fog and ambient, which are global render settings in both
engines — so those are still saved on the way in and put back on the way out, and that swap is what
makes the inside feel like an inside.

**`E` is shared with getting into a vehicle, and the doorway defers.** A car parked outside the
storefront puts both in range at once, so `VehicleEnterExit.HasVehicleInReach` decides it rather than
Update order — the vehicle wins, which is the web build's precedence too.

**⚠ A teleport must switch the `CharacterController` off across the write.** It caches its own
position and will sweep the capsule from the pizzeria back to the city if left enabled, which reads
as the player being dragged through every building on the way. The camera is snapped afterwards for
the same reason the web build snaps it: otherwise the boom lerps across a kilometre of city while the
player stands in a lit room.

**Out of scope on purpose, both chosen by the user:** the counter NPC and the pizza-box pickups are
U21's, since the mission is what consumes them; and promoting a parked filler car into a drivable one
(`E` on any lot car, in the web build) is left until `CarBuilder` is generalised past the Mustang,
which is really U17's work. The fade behind the teleport is U25's.

**Verified by measurement, not by eye** (the rest is the user's to judge): gas station base on y 0 at
27.6 × 13.1 × 24.5 m; 101 cars between Unity x [137.8, 294.4] and z [−297.2, −195.8], all inside the
lot's own 165 × 116 m, wheels resting at y 0.10, zero cars in `keepClear`; entering the interior
lands the player at (−1000, 0.3, 1002.8) with the warm fog and ambient applied, and leaving puts them
at (−28, 0.3, −100) with the street's restored.

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

### What U7b built — swimming

**The sequence never had a row for swimming.** The web build has it (`config.sea.swim`, and
`player.ts` carries the state), the 32 units did not, and nothing downstream would have noticed —
the port would simply have shipped a sea you drown in. It is filed as `U7b` because it is U7's
state machine plus one pose, but it could not be built until U12 put water in the world.

Four pieces, no new systems:

- **`SeaGeometry.IsSwimming`** — a region test, not a raycast, because the water deliberately has no
  collider. The web writes `x < shoreX`; here it is `x > ShoreX`, and that sign lives in this one
  method. Depth is measured from the swimmer's float height, not from sea level, which is what the
  web does and is not a rounding detail: it starts the swim **6.4 m** past the waterline instead of
  11.7 m.
- **`PlayerController.Float`** — the buoyancy spring, replacing gravity outright rather than adding
  to it. Two traps: `swim.surfaceY` is a **capsule-centre** height while Unity's transform is at the
  feet, so it is used as `surfaceY − capsuleCenterY` (miss it and Joe floats waist-deep in his own
  shins); and the web damps per **frame**, which quietly ties the settle to the frame rate — raised
  to `Mathf.Pow(damping, dt * 60)` here, same curve at 60 fps and the same curve everywhere else.
- **The shore wall had to stop blocking the player.** It is one collider serving two purposes: cars
  must not drive out to sea, the swimmer must walk straight through. The web build solves it with a
  per-obstacle `obstacleFilter` predicate; Unity has the mechanism built in —
  `CharacterController.excludeLayers`, aimed at the Ignore Raycast layer that `WorldBuilder` already
  puts the wall (and nothing else) on. One line, no new layer, no new marker component.
- **`Joe_Swim`** — the animator gets a `Swim` bool and one looping state on an Any State transition,
  same shape as `Ride`. Crossfade is 0.35 s rather than the gait 0.18: water is entered by walking
  into it, so that blend IS the transition from upright to prone, and at 0.18 Joe snaps flat.

Wading needs no state and does not have one — the seabed is a real MeshCollider, so under 6.4 m out
the controller simply walks down it and gravity holds the feet on it.

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

`Joe_Swim` (U7b) is the worked example of exactly that path — 55 MB with-skin FBX out of the
original's `source-assets/`, one row in `JoeClipImporter.Clips` with `bakeRoot: false`, two menu
items, done. **The original's `source-assets/models/` is worth reading before assuming a clip is
missing**; it holds the raw Mixamo download for everything the web build animates.

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
| U7b | Swimming | done | `3190b43` | **Not in the original 32 — the sequence never had a row for it and the port would have silently lost it.** Belongs to U7's state machine, but needed U12's sea to exist, so it lands here. `Pose.Swim` outranks every other pose; buoyancy spring replaces gravity outright (`PlayerController.Float`); `SeaGeometry.IsSwimming` owns the region + depth test and its X sign. Clip is `Swimming.fbx` from the original's `source-assets/`, imported as `Joe_Swim`, `bakeRoot: false` — a locomotion cycle, not a fixed-space clip. **The player had to be let THROUGH the shore wall it shares with the cars** — `excludeLayers` on the CharacterController, which is Unity's answer to the web's `obstacleFilter`. User-confirmed 2026-08-15 |

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
| U12 | Roads, ground, sea | done | `7dc8208` (+ fixes 2026-08-15) | **Two faults found at U15's play-test, both fixed — see the decision log: `config.fog` was never ported, so the 320 m far plane sliced the skyline; and the ground plate showed through the sea's wave troughs (0.37 m of swell vs a plate at −0.05 m), now cut out of the plate mesh.** Roads are `com.unity.splines` + a generated ribbon, NOT the web's per-segment stretched tile: 1864 m of spline vs 1859.5 m of polyline, corners curved, markings continuous through them. The `SplineContainer`s are kept as U17/U19's centreline. Road surface texture is generated because the web tile's paint is geometry. Sea is a port of `sea-surface.ts` into `Assets/Shaders/{Water,Beach}.shader` (URP has no built-in water) — unlit on purpose, since the original does its own lighting. Beach is a displaced MeshCollider you walk down. `Assets/Scripts/World/SeaGeometry.cs` owns the waterline and its handedness — the sea is Unity **+x**. **Caught and fixed: the ground plate's collider held the player up over the whole beach; it now stops at the shore. "Kerbs" were phantom scope — no such system exists in the original.** Splines needs ≥2.9.0 on Unity 6.5. User-confirmed 2026-08-15 |
| U13 | Places — pizza + interior, gas, police station, lot cars | done | `211abc2` | User-confirmed 2026-08-15. Gas station was Y/Z swapped by the Sketchfab export's cancelling root matrices; `Rx(-90)` in `AssetAliases`, whose entries can now correct the REAL asset (`File = null`) instead of only swapping in a stand-in. Lot cars are 101 real GameObjects with per-car culling and `LODGroup`s, NOT an InstancedMesh — same seeded layout as the web build (`Mulberry32` in `uint`), paint as 18 generated materials so the instancing survives. Interior is a teleport cell with the fog/ambient swap; its lights stay on and the sun stays up, both of which the web build only fights because of three's forward renderer. **Caught and fixed: `tesla.glb`/`avenger.glb` require `EXT_texture_webp` and glTFast rejects the whole file — `tools/glb-webp-to-png.py`; and a BoxCollider that ignores the model scale is a kilometre wide on the 37.4× Avenger.** NPC + pizza pickups deferred to U21, lot-car promotion to U17, the fade to U25 — all by the user's call. **The interior's MISSION mechanics are explicitly unsettled and belong to U21** — the room is right, what the delivery does inside it is not |
| U14 | Map + minimap | done | `8ea9fc4` | User-confirmed 2026-08-15. The base layer is a LIVE second camera into a 1024² RenderTexture (`Assets/Scripts/UI/MapCamera.cs`), not the web's boot-time bake — no readback, no shader-compile spike, and moving cars show. UI Toolkit, eleven units before U25: `MapView` paints outlines/dots/arrow with Painter2D and pools `Label`s for text, `GameMap` owns the panel and the `M` toggle, `MapRegistry` is the port of `world/registry.ts` and the hook missions add objective pins to. Both states capped at 12 fps — the web caps only the minimap. Camera at `(90, 180, 0)` puts screen right on world −X, matching the web map's frame; verified against `transform.right`. **Caught and fixed: `PlaceSpec` has no `name` in config.ts — the pin labels live in `map-pois.ts`, and reading the absent field crashed the label pass; and a 16-bit RT depth that made Metal log "memoryless depth surface" as an error.** Emoji pin glyphs deferred to U25 (no emoji font), cop blips to U19, rival/arena to U32 |
| U15 | World memory — texture compression (was: Addressables) | done | `4b7a93d` | User-confirmed 2026-08-15. The measurement the row demanded REJECTED Addressables: 13.5 GB of scene memory, 96% textures, and streaming 13.5 GB in chunks is still 13.5 GB. Real cause: glTFast textures are .glb SUB-ASSETS with no TextureImporter, so nothing ever compressed them — 12.9 GB raw RGB24. **The Block → Compress Textures** (`TextureCompressor.cs`) slices the embedded PNG/JPEGs verbatim out of the GLB container into `Assets/Textures/Generated/`; `GeneratedTextureImporter.cs` makes the first import BC1/BC7 with settings derived from the file NAME (so a Library wipe cannot lose them); `WorldBuilder.Textures.cs` clones .glb materials and rebinds — 688 slots. **Scene texture memory 13,498 → 3,204 MB (4.2×).** Caught: texture names are NOT unique in a .glb (seven "Untitled" in city 4) — resolver matches name+size+alpha and refuses to guess, 12 refusals reported; and NPOT+mips silently skips block compression while claiming DXT1 — `npotScale ToLarger`, which was 8.9 GB of the win. Memories: `gltfast-textures-never-compressed`, `npot-mips-skip-block-compression` |

### Tier 4 — Living world
| id | unit | state | commit | notes |
| --- | --- | --- | --- | --- |
| U16 | Pedestrian crowd (NavMesh agents) + zebra crossings | done | `0dc4398` + `27058ae` | User-confirmed 2026-08-15 — flagged **low performance, revisit later** by the user (see RESUME HERE). The pavement is not enforced, it is the only thing that exists: `WorldBuilder.Navigation.cs` carves all 12.7 km of `config.traffic.network` **Not Walkable** (172 volumes over 142 streets), which disconnects the two sides of every road, so the only route across is a gated `NavMeshLink` at one of **230 zebras** on 70 lit intersections — derived from the same graph and the same `stopLineDist + crossingSetback` as `traffic.ts`. NavMesh baked 963 × 805 m @ 0.4 m voxels over the DISTRICTS only (car park excluded) (`CollectObjects.Children`, PhysicsColliders) → `Assets/Navigation/Generated/NavMesh.asset`. `config.traffic` ported (`TrafficSpec`, `StreetSpec` + its union converter). Crowd is a **pool of 60 that follows the player**, trickled in 6 per sweep, not the web build's ~400 seeded-at-boot-and-frozen — `CrowdSpawner`/`Pedestrian`/`NpcAppearance`. Zero of the 80 hand-recorded rectangles and strips in `npc.config.ts` are ported and none are needed. **Caught: the pack's prefabs reference the BUILT-IN Standard materials while the URP twins sit unused beside them — 455 slots rebound, or every pedestrian is magenta. Then, at play-test: zebras 2 cm UNDER the street (GroundY took the lowest hit — the ground plate — z-fighting up as orange); the vendor's five LODs are 33 skinned meshes per person, all posed every frame whether drawn or not, and an unposed one swapping in by LOD change draws at bind pose against a walked-off skeleton — the 'exploding pedestrian'; and 90 agents spawned in one frame was the stutter, not the crowd's steady cost, which measured as zero.** LODs 0+2 only now (395 → 158 SMRs), spawn trickled, car park excluded. ⚠ Rooftops bake walkable and are filtered at spawn/re-target by a height band, not by the bake |
| U17 | Traffic — graph, cars, lights | wip — built, awaiting the user's play-test | `2ea3c54` | Cars, lights and phases on U16's graph, which is now derived ONCE by the traffic pass and handed to the navigation pass — the crossings and the lights key off the same node numbering by construction. `Crossing.IsClearOfTraffic` deleted; `TrafficLightSystem` fills `Crossing.Gate` for all 230. **The population is DERIVED, not configured**: 130 cars over 12,759 m is one car per 98 m, so the live count is the metres of centreline in range divided by that — a fixed 32 was the plan and it gridlocked the city in under a minute, because the disc around the starting lot holds 1,230 m and 32 there is jam density. The graph is BAKED to a ScriptableObject at build time (6,590 Y-samples), so the runtime casts no rays for traffic at all. Kinematic while driving, a real Rigidbody wreck when rammed. **Caught and fixed, both by measuring rather than looking: `GroundY` could return a ROOF (downtown's avenue baked at 6–10 m) and the fast `Build World` was silently losing the whole NavMesh — `PasteComponentValues` does not carry `navMeshData`, so the crowd failed to spawn with nothing in the console.** Cars stop BEHIND the zebra, which the original does not. Carjacking split out to U17b |
| U17b | Carjack + `CarBuilder` past the Mustang | todo | | `config.traffic.hijack` — `E` on a stopped street car promotes it to a drivable `Vehicle`, the sim slot recycling far away (`traffic-cars.ts` `nearestStopped`/`hold`/`claim`, `transitions.ts hijackTrafficCar`). **Also owns U13's deferred lot-car promotion**, which is the same mechanism. The blocker is that `CarBuilder` only builds the Mustang, and `config.vehicle.cars` has full drivable specs (door joint, seat, paint) for Tesla/Audi/Avenger — but **none of those three GLBs has a wheel node at all**, so `FindWheelBones` has nothing to find and their wheel geometry must be STATED the way `MotorcycleBuilder` states the bike's. Split out of U17 by the user, 2026-08-15, to keep U17 to one checkpoint |
| U18 | Run-over + blood VFX | todo | | Root Motion ON — the clip's motion IS the knockback. **Owes U16 one thing:** a struck pedestrian must leave the NavMesh, so the agent has to be disabled for the knockback and re-`Warp`ed after. Panic-fleeing into the road (if it is wanted) is the same switch — the carve makes the road unreachable by pathfinding on purpose |
| U19 | Police pursuit + wanted level | todo | | real NavMesh; do NOT inherit the straight-line hack untested |

### Tier 5 — Missions
| id | unit | state | commit | notes |
| --- | --- | --- | --- | --- |
| U20 | Mission framework + campaign director + persistence | todo | | |
| U21 | M1 pizza delivery | todo | | Owns the interior's mission mechanics, left open at U13 by the user: the counter hand-off, what the exit pad means while carrying pizzas, whether leaving starts the shift. Expect to change `Interior.cs`, not build beside it |
| U22 | M2 rhythm / dance minigame | todo | | |
| U23 | Helicopter + M3 rooftop rescue | todo | | |
| U24 | Jetski + M4 chase | todo | | |

### Tier 6 — Shell
| id | unit | state | commit | notes |
| --- | --- | --- | --- | --- |
| U25 | HUD + in-game UI (UI Toolkit) | todo | | Panel scaffolding already exists from U14 (`HudBuilder`, `HudPanelSettings`) — extend it, do not build a second panel. Owes U14 two things: an emoji-capable font so POI pins can draw their `⛽`/`🚓`/`🏪` glyphs again, and the fade behind U13's interior teleport |
| U26 | Menus — title, character select, briefing, controls, pause | todo | | **Settings → Display wants a Radar on/off toggle** (user, 2026-08-15) that hides U14's collapsed minimap while playing. `M` must still open the full map with the radar off — the toggle is about the always-on widget, not the map |
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

- **2026-08-15** (U16) — **The pavement is not enforced, it is the only thing that exists.** The web
  build's pedestrians drift into the road because nothing there knows a road is a thing: a 4096²
  top-down material mask, a 67 MB GPU readback, a session-long boolean grid, straight-line movement
  between sampled points, and — when that was not enough — eighty rectangles and strips recorded by
  hand beside the pavements rather than on them. **None of it is ported and none of it is replaced.**
  All 12.7 km of `config.traffic.network` is carved `Not Walkable`, which disconnects the two sides
  of every street, so being in the road is not unlikely, it is unrepresentable. This is the answer
  to the standing remark for U16, and it is the strong form of it: the mechanism is not a better
  version of the web build's, there is no equivalent of the web build's at all.
- **2026-08-15** (U16) — **A crossing is a hole in connectivity, not a scripted walk.** With the
  carriageway carved, the only route to the far pavement is a `NavMeshLink` at a zebra, so an
  ordinary wanderer crosses at a zebra because there is nowhere else — no pedestrian is assigned to
  a crossing at all. The web build's crossings are real (`traffic.ts`) but serve two dedicated
  pingpong walkers each while the rest of the crowd ignores roads entirely. `autoTraverseOffMeshLink`
  is OFF so `Pedestrian` owns the kerb, and `Crossing.Gate` is the seam U17 hands the light to —
  the same shape as `CrossingSpec.mayCross`.
- **2026-08-15** (U16) — **The crowd is a pool that follows the player, not a population.** The web
  build creates several hundred pedestrians at boot and freezes them individually past 90 m, because
  a three.js pedestrian is cheap to hold and dear to create. A NavMeshAgent is the reverse, and a
  frozen one still sits in the avoidance solver. 40 live agents that recycle from behind you to
  ahead of you — rerolling face and shirt each time — read denser than 400 frozen ones and cost a
  fraction. It also means `npc.config.ts`'s `paintedZones`, `strips` and `zones` have no port: where
  people can stand is the NavMesh's answer now.
- **2026-08-15** (U16 play-test) — **The stutter was the spawn burst, not the crowd. Measured:
  frame time with 60 agents on = frame time with them off = 20.0 ms.** So "too many people" was
  never the fault; 90 `Instantiate`+`Warp`+`SetDestination` in one `Awake` was, and the vendor's
  five LODs multiplied it (33 skinned meshes per person, all posed every frame regardless of what
  the LODGroup draws — 2,960 SMRs for 90 people, 747 visible). Spawn is trickled 6 per sweep,
  LODs 1/3/4 are DESTROYED at build (not disabled — a disabled SMR is still owned by the animator),
  and the animator culls completely off-screen. `AlwaysAnimate` was tried in between and was
  wrong: it doubled the cost and fixed nothing, because the "exploding pedestrian" was an SMR that
  had never been posed drawing at bind pose on LOD swap, and removing those SMRs is the fix. The
  user flagged the unit low-performance for later; the number to beat is density, not frame time.
- **2026-08-15** (U16 play-test) — **`Build World` no longer bakes; `Build World + NavMesh (slow)`
  does.** The 0.25 m bake froze the editor long enough, twice, that the user force-quit it, and a
  main-thread freeze with no progress bar is indistinguishable from a crash. At 0.4 m the whole
  bake is ~3 s, and the split is kept anyway: the fast build lifts the previous navigation out of
  the old root and re-attaches it, and never sweeps `Assets/Navigation/Generated/` — which it did
  once, deleting the zebras' mesh and material out from under 230 kept crossings.
- **2026-08-15** (U16 play-test) — **`GroundY` is "lowest hit that is not the ground plate."** The
  first version took the lowest hit outright, which under every district is the plate at −0.05,
  2 cm below the street at 0 — and a zebra painted there z-fights up through the district mesh
  as bars of that mesh's OWN texture. Orange stripes, in this case. It reads as a material fault
  and is a 5 cm height fault; check the height before the shader.
- **2026-08-15** (U16) — **U17 inherits U16's traffic graph; it must not build a second one.**
  `config.traffic` is ported in full (`TrafficSpec`, `StreetSpec` + a union `JsonConverter`,
  `LightsSpec`) and `WorldBuilder.Navigation.cs` already builds the 97-node graph, finds the 70 lit
  intersections and places the 230 crossings. U17 adds cars, lights and phases on top and replaces
  `Crossing.IsClearOfTraffic` with the controller.
  **Settled harder at U17:** the graph is not merely shared, it is derived by the traffic pass —
  which runs on EVERY build — and passed into `BuildNavigation`. The navigation pass no longer
  builds one at all.

- **2026-08-15** (U17) — **How many cars is measured, not chosen.** The web build's own two numbers
  are 130 cars and 12,759 m of network: one car per 98 m. `TrafficSystem` counts the metres of
  centreline inside the cull radius every sweep and asks for that many, so downtown is busy and the
  edge of the map is empty without either being typed in. **This replaced a fixed count of 32, and
  the failure is the point:** 32 came from an estimate that a 160 m disc holds thirty streets'
  worth of road; measured, the disc around the starting lot holds 1,230 m, so 32 is one car per
  38 m — jam density at signalised junctions — and the city gridlocked in under a minute with 31 of
  32 cars stopped. At the derived count it runs indefinitely with nobody reaching the stuck escape.
  A guessed constant that happens to be wrong is indistinguishable from a broken algorithm until
  someone measures the thing it was a guess about.
- **2026-08-15** (U17) — **The street graph is build output, not load-time work.** `buildPath`
  raycasts the ground once per two metres of every path — 6,590 rays over this network — and the web
  build pays that before its first frame. `Assets/Traffic/Generated/TrafficNetwork.asset` holds the
  same numbers, so the runtime casts no ray for traffic ever. U19's police wants the same asset.
- **2026-08-15** (U17) — **⚠ `GroundY` could return a ROOF, and 230 samples were not enough to show
  it.** U16's rule was "the lowest hit that is not the ground plate", which is right wherever a
  district has street geometry — and downtown is one merged mesh with none under parts of its
  avenue, so the only non-plate hit there is the building overhead. At 230 crossings nothing landed
  on one; at 6,590 traffic samples, nine did, at 6.4–10.1 m. A street is never more than 2 m above
  the plate, so anything higher falls back to the plate, and single-sample spikes are flattened
  against their neighbours afterwards.
- **2026-08-15** (U17) — **⚠ The fast `Build World` was silently deleting the NavMesh bake.**
  `ComponentUtility.PasteComponentValues` does not reliably carry `NavMeshSurface.navMeshData`, and
  when it does not, everything looks fine: the surface is enabled and correctly configured, the
  asset is still on disk, and `NavMesh.CalculateTriangulation()` returns zero vertices. The only
  symptom is a city with no pedestrians in it and an empty console. Found by counting the crowd
  during U17's play-test, not by seeing it. The baked asset is loaded from disk on re-attach now.
  **The general lesson: a component copy is not a way to preserve a reference.**
- **2026-08-15** (U17) — **Cars stop behind the zebra; the original does not.** It stops a car
  `stopLineDist` (8 m) from the junction centre and paints the crossing at 10 m, and a car's
  position is its body centre — so the lead car of every queue parks its back half across the
  crossing. That is not a design decision there: `crossingSetback` exists so the crossing's kerb
  ends clear the light POLES, and the car was never measured against it. Scar tissue, not intent.
- **2026-08-15** (U17) — **Kinematic while driving, dynamic when rammed.** Thirty vehicles solving
  contacts was never on the table in Rapier, so the web build's traffic is kinematic full stop and
  the player bounces off it. Kinematic stays the default here for the same reason — a car following
  a baked lane costs one `MovePosition` — but the exception Unity can afford is per-car: a hit above
  an impulse threshold flips that one car to a real Rigidbody, and it stays a wreck the rest of the
  traffic queues behind until the slot recycles. Bounded by construction to cars the player actually
  hits, and switched off by one serialized bool if it ever misbehaves.
- **2026-08-15** (U17) — **The light pole uses the SHIPPED model, not the source asset.** A
  deliberate exception to port rule 3, and the reason is memory: `traffic_light__animation.glb` is
  16.5 MB carrying four 4096² textures for a 4.5 m pole placed 233 times, while the web build's
  dieted copy has the same four meshes at 512². Rule 3 exists to avoid a pointless second lossy
  pass; on a pole seen from 20 m that pass is invisible and the win is ~50×. It needed
  `tools/glb-webp-to-png.py` because the shipped file requires `EXT_texture_webp` — U13's trap,
  U13's tool.

- **2026-08-15** (U7b) — **The 32 units are not a complete inventory of the game.** Swimming is in
  `config.ts`, in `player.ts` and in the shipped build, and no unit owned it; it surfaced only
  because the user asked an unrelated question about animations. The sequence is a plan, not a
  spec — `config.ts` is the spec. Filed as `U7b` rather than renumbering, and the same audit has
  not been run against the rest of the config.
- **2026-08-15** (U7b) — **One collider, two answers: `excludeLayers` is Unity's `obstacleFilter`.**
  The shore wall must stop a car and pass a swimmer. The web build carries a predicate the character
  controller calls per candidate obstacle; Unity puts the same idea on the collider itself, and
  `WorldBuilder` had already parked that wall alone on Ignore Raycast for an unrelated reason (a
  downward probe was reading its top as ground). Excluding that layer on the player's
  `CharacterController` is the whole fix — no new layer, no marker component, nothing else on the
  layer to catch by accident. **If anything else is ever put on Ignore Raycast, this becomes wrong.**
- **2026-08-15** (U12 repair) — **`config.camera.far` is a three.js budget, not a design; the fog it
  came with is the design.** `far` 320 m, `fog` 70→280 m and `background` are ONE mechanism: the haze
  dissolves geometry into a sky painted the identical `#9FB8D4` long before the plane reaches it. The
  port took the plane and left the fog, so the clip ran naked and sliced the skyline in a hard arc.
  `config.streaming` (unload past 380 m) is the proof the distance was a budget. Unity draws to
  1500 m; `World.Atmosphere` owns that number AND the fog range together, and rescales the config's
  own near/far RATIOS onto it so the haze thickens at the same fraction of the view it always did.
  Never set one without the other.
- **2026-08-15** (U12 repair) — **The ground plate is not drawn where the sea is.** U12 kept the
  visual plane full-size because "the water is opaque and drawn above it"; the arithmetic says
  otherwise — the swells total 0.37 m of trough against a plate at −0.05, so every deep trough
  exposed green through the ocean in bands that read as a shader fault. The plate's mesh now has the
  sea's rectangle cut out of it. Moving either surface was rejected: the plate's collider is already
  trimmed at the shore and would float above a lowered plate, and the water line is gameplay.

- **2026-08-15** (U15) — **U15 is texture compression, not Addressables.** The row said "ONLY if
  the profiler says so"; the profiler said the problem is format, not streaming — 12.9 GB of the
  13.5 is raw RGB24 that no importer ever touched, and streaming it in chunks would still be
  12.9 GB. Addressables goes back on the shelf until something needs load-time sequencing, which
  nothing yet does. Chosen by the user 2026-08-15 over "record the numbers and skip to U16".
- **2026-08-15** (U15) — **Extracted textures' import settings are derived from the file NAME, in a
  postprocessor.** Editing the TextureImporter after writing the file imports everything twice and
  survives only in the .meta — a Library wipe or platform switch would silently restore defaults
  and put the 13 GB back. `TextureCompressor.AssetName` encodes size and linearity into the name;
  `GeneratedTextureImporter.OnPreprocessTexture` reads it, so the FIRST import is right, forever.
  The sRGB flag is copied from what glTFast itself resolved, never re-derived from material roles.
- **2026-08-15** (U15) — **An ambiguous texture stays uncompressed; the resolver never guesses.**
  Image names repeat inside one .glb, and binding a wall to another wall's normal map is a lighting
  bug that reads as anything but what it is. Name + pixel size + alpha channel narrows; what still
  matches two images is skipped and named in the report. 12 refusals (~110 MB) is the accepted cost.

- **2026-08-15** (U14) — **The map's base layer is a live camera, never a bake.** three.js could not
  afford a second camera, so it rendered the world once at boot and read the pixels back; a Unity
  camera into a RenderTexture costs one throttled pass and shows the world moving. Nothing in the
  port should reintroduce a baked map image.
- **2026-08-15** (U14) — **Runtime UI is UI Toolkit, and the HUD panel is a single shared one.**
  U14 created `Assets/UI/HudPanelSettings.asset` and the `HUD` object; U25, U26 and every later
  overlay extend that panel rather than adding their own `UIDocument` stack.
- **2026-08-15** (U14) — **The map is oriented by the camera, not by a hand-derived transform.** The
  overlay's world→panel maths is written against the map camera's real `transform.right`/`up`, so
  the vectors and the pixels underneath them cannot drift apart. Any new map layer reads the same
  two vectors instead of re-deriving the handedness.

- **2026-08-15** (U13) — **`AssetAliases` corrects real assets too, not just stand-ins.** An entry
  with no `File` keeps the config's own model and applies only the rotation/lift. The distinction is
  load-bearing rather than cosmetic: a stand-in must skip the config's `hideNodes` because those name
  another model's parts, and the real asset must obey them.
- **2026-08-15** (U13) — **Lot cars are GameObjects with per-car culling, not one InstancedMesh.**
  three's instancing is a single renderable with one bounding volume, so nothing culls; Unity
  GPU-instances identical mesh/material pairs by itself and culls each car on its own bounds, plus an
  `LODGroup` that drops them past 180 m. The web build's approach ports as a performance regression.
- **2026-08-15** (U13) — **Lot-car paint is a generated material per colour, never a
  `MaterialPropertyBlock`.** A property block would break the batch and give every car its own draw
  call, which is the opposite of what the web build's per-instance colour buys there. Eighteen
  material assets cover the whole lot, and they are swept like every other generated folder.
- **2026-08-15** (U13) — **A required glTF extension is transcoded, not worked around.**
  `tools/glb-webp-to-png.py` rewrites the embedded WebP and drops `EXT_texture_webp`, because
  glTFast rejects the entire file and the failure surfaces only as "missing". The lot car models have
  no source asset to re-export, which is what makes this the pipeline step rather than a Blender fix.
- **2026-08-15** (U13) — **The interior's lights stay on and the sun stays up.** The web build
  switches both because three's forward renderer charges every light against every fragment in the
  scene; URP culls per object and the room has a ceiling. Only fog and ambient are still swapped —
  those are global in both engines. Scar tissue, not design (port rule 5).
- **2026-08-15** (U13) — **The vehicle wins `E`.** A car parked outside the pizzeria puts the door
  and the driver's seat in range at once; the doorway asks
  `VehicleEnterExit.HasVehicleInReach` and stands down, rather than the two racing on Update order.

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
