# PORT-STATUS - The Block, Unity port

**This is the living ledger. Read it immediately after `CLAUDE.md`, before doing anything else.**
It is the only thing that survives a lost session. Conversation history is not a source of truth;
this file is.

---

## Standing remark - every unit asks "can Unity do this better?"

**This is a rebuild, not a transcription.** Before building any `U`, ask the question explicitly and
write the answer down in that unit's notes: *what did the web build settle for here because three.js
or Rapier could not do better, and what does Unity give us instead?*

The game must stay the same game - same missions, same world, same feel. But the mechanism
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
a real Rigidbody wreck, which thirty Rapier vehicles could never have afforded (U17); and the police
route real A\* over the street graph instead of driving straight at you, which the web could not do
because its graph was five disconnected islands (U19); and the sirens are 3D sounds on the cars
rather than one wail at a constant gain, because the web build has no `AudioListener` in it at all
(U27); and the loading bar reads `AsyncOperation.progress` instead of the hand-counted milestones
the web had to fake, while the character preview is a second camera into a RenderTexture rather than
the second WebGL context that "did not hold on an iPad" (U26); and the day/night cycle turns a
dev-only URL flag into a player-facing setting, shipped switched off so every approved screenshot
still reproduces (U33 - ⚠ this line used to claim day/night was the first thing the port ADDS rather
than ports; the original has one at `enabled: false`, see the row). U25's last owing, an
emoji-capable font, landed with U28 and that row is closed.

**U29 is the rule paying a dividend on a unit built four months earlier, and that is a shape worth
recognising.** The web build's roster has to reach FIVE bodies, and its own comment says why: four
separately built skinned meshes wear the player, "so picking one has to reach all of them or you'd
change clothes on getting into a vehicle". U9 had already replaced all four with one reparented
player, so the fan-out here is two - the player and the stage dancer - and nobody had to design that.
**Answering the standing question well does not only improve the unit it is asked in; it deletes work
from units that have not been written yet.** The counterweight is U29's own scar: the same reparenting
means `VehicleEnterExit` caches the player's renderers, and a cache is exactly what a swappable body
invalidates.

**U27 also adds the sharpest instance yet of the rule's cost, and it is not a Unity feature failing
- it is a Unity feature having a price nobody quoted.** Putting the dance's song on a mixer bus is
plainly right: it is what a Music volume slider will attach to. It also inserted one DSP buffer of
latency between the beatmap's anchor and the speakers, and moved every note 21.3 ms - 43% of a
Perfect window - in one direction. Nothing was broken and nothing logged; the sound simply arrived
late. It was found because U22 had written its drift number down and the number was re-measured
after the change. **The lesson is the one U19 already paid for in a different currency: when a unit
routes something through a new mechanism, re-run the measurement the old mechanism was accepted on.**

**U19 is also the sharpest warning the rule carries, and it cost two extra rows.** "Unity can do
this better" produced a genuinely better wanted meter - continuous, so a scrape costs less than a
body - and it shipped in the same unit as "the cruisers park at the station", which gave the
response a 15-60 s travel time. Each was right. Together they were a pursuit that could not happen,
because the star died in six seconds. **A better mechanism is only better against the rest of the
unit**, and the check is arithmetic: when something gains a duration, put every clock next to it.

**U17 adds a second kind of answer, and it is not a Unity feature at all: measure the original.**
Its population is not a number anyone picked - it is 130 cars over 12,759 m of network, read off the
web build's own config and applied per metre of street in range. The version with a chosen constant
gridlocked; the version that asks the original what its density was does not. Where the shipped game
already encodes a decision, porting the DECISION beats porting the number.

**U15 is also the rule's counter-example, and the more useful one.** Its planned answer was
Addressables, and the measurement said no: streaming 13.5 GB in chunks is still 13.5 GB, and the
real fault was a format nothing had ever set. "Can Unity do this better?" has to be allowed to
answer *not like that* - the question earns its place by being measured, not by producing a Unity
feature every time.

The counterweight is port rule 5 in `CLAUDE.md`: **design intent carries, scar tissue does not** -
and telling them apart is the actual work. Tank controls stayed (U6) because they are the design.
Kinematic vehicles went (U8, U10) because they were a Rapier limitation. When it is genuinely
unclear, re-test before inheriting.

---

## RESUME HERE

> **THE GAME IS BUILT, AND AS OF 2026-08-17 SO IS TIER 8.** U0-U29 plus U33, U34 and every U35
> sub-unit that survived the user's cuts are `done` or `built - user-confirmed`. **There is no
> gameplay work scheduled anywhere in this ledger.** What remains is Tier 7 and the submission.
>
> **⚠ THE PROJECT PIVOTED, 2026-08-16. The Unity build is what gets submitted, and it has a deadline:
> 1 Oct 2026.** See `CLAUDE.md` §1 and the **Submission** § below. This is not a change of plan, it is
> a change of what "done" means - the graded artifacts are a video, a repo, a kanban board and a zip,
> and only one of them is the game.
>
> ### 🚩 NEXT ACTION: **the user play-tests U35h's props** - the poles, cones and bins, recipe in its section below; its BENCH half and all of U35i are already user-confirmed. Then **U30a - the macOS build.**
>
> ---
>
> **What the user settled on 2026-08-17, in one message. Five decisions, and four of them are cuts:**
>
> - ✅ **U35d-pre-3 is USER-CONFIRMED** - the in-vehicle arrest. Its section is below; `ArrestRadius`
>   was a distance between CENTRES and two 5.6 m cars nose to tail never satisfied it, so in a vehicle
>   the arrest measures the **gap between bodies** (`VehicleArrestGap = 2.5`).
> - ✅ **U35c is USER-CONFIRMED** - the police H145 at 3★, modelled in Blender from two reference
>   photographs. **Its GPS-route half is CUT** - see the next line.
> - ✂ **THE GPS ROUTE IS REMOVED FROM THE GAME** (`cfbd4eb`) - *"תוריד את הקו התכלת מהמפה, לא צריך את הפיצר
>   הזה."* The cyan polyline on the radar and the full map is gone, and it was **deleted rather than
>   defaulted off**: `Assets/Scripts/UI/GpsRoute.cs` is gone, and with it `MapView.SetRoute` /
>   `DrawRoute`, `GameMap._gps`, `Progress.GpsRouteOn` + its `theblock.gpsroute` key, the
>   `Settings → Display → GPS Route` row, and `MapRegistry.NearestGuide`. **What deliberately stays:**
>   `RoutePlanner` and `RouteGraph` (the police have A*-ed on them since U19 and still do,
>   `PoliceSystem.Graph` included), and the `MapPoi.Guide` bool that four missions set - nothing reads
>   it now, and its comment says so. **So U35c ships as the helicopter alone.**
> - ✂ **U35e IS DROPPED** - stunt jumps and Cinemachine. *"we decieded we do not need that so do not
>   mention it again."* Nothing was ever built for it, so the cut costs no work. **With it Tier 8 has
>   no scheduled unit left at all.**
> - ✂ **THE RADIO IS DROPPED, not held** - *"רדיו - גם תוריד, לא כזה חשוב."* It was ⏸ on hold since
>   2026-08-16 and it is now closed. The measured finding in **Deferred** stays on the page as
>   research, not as pending work: `GetAudioClip(streamAudio)` is dead on a live stream and the answer
>   would have been a pure-C# MP3 decoder. **The `Radio` mixer group, `GameAudio.Bus.Radio` and
>   `config.radio` remain idle in the build and are NOT to be ripped out** - they cost nothing and
>   removing them is work spent making a retry harder.
> - ✂ **U35d-pre needs no play-test and is CLOSED as superseded** - *"u35d-pre גם אפשר להוריד אמרנו
>   שיורדים מהרמפה."* Read as: it comes off the pending list, **not** as "delete the arrival ramp from
>   the code" - U35d-pre-3, which the user just confirmed, is a rewrite of that very ramp into
>   relative terms and rests on the pull-over mechanism U35d-pre built. Removing it would break the
>   feature that was approved in the same message. **If a literal code removal was meant, say so and
>   it is a separate job.**
>
> ---
>
> **Tier 8's final state, so nobody re-derives it:** U35a (ragdolls) ✅, U35b (vehicle damage) ✅,
> U35c (police helicopter) ✅, U35g (the auto shop) ✅ - all four `built - user-confirmed, awaiting
> U30b`, which is rule 3(a)'s frame measurement on a Player that does not exist yet and is the ONLY
> thing any of them still owes. Dropped by the user: U35d (weather), U35e (stunt jumps), U35f (side
> jobs). **U35h (breakable street props) is BUILT 2026-08-17 and awaits the user's play-test** - both
> halves: the 233 traffic-light poles topple, AND three Sketchfab props the user supplied (bench, bin,
> cone) stand at the kerb as sleeping rigidbodies. Section below.
>
> **The order from here, and it has not changed except by getting shorter:**
>
> **U30a** (build) → **U30b** (baseline on the Player with every switch off, then a per-feature delta
> for U35a/b/c/g; anything over budget is tuned or cut) → **record the video** → **U30c** (strip the
> debug keys, LAST, because `P`, `T`, `C`, `debugStock` and Mission Select are how a five-minute
> recording reaches every feature in the game).
>
> **Why the old "build → baseline → features" trap does not bite here:** every U35 addition ships
> behind a switch whose off state IS today's game (Tier 8, rule 2). So U30b's baseline is taken with
> everything off - identical to what it would have measured before U35 - and each feature is then
> toggled on alone for its delta.
>
> **U30a's own note:** U30 was split into a/b/c because a build is a correctness job, a perf pass is a
> measurement job, and stripping debug keys is a shipping job. Nothing in this port has ever left the
> Editor - a Player is a different renderer path, a different memory ceiling and a different input
> stack, and it is the first place any of that is testable. U15 is the standing warning that the
> obvious answer (streaming, LODs) can be the wrong one. **The one open choice is the scripting
> backend, Mono or IL2CPP** - see the row.
>
> **Tier 7 is TWO units:** U30 and U32 (multiplayer, deferred by decision to last). U31 (iOS/iPad) is
> **dropped by the user, 2026-08-16** - *"זה לא רלוונטי להגשה."* The iOS module stays installed and
> unused; nothing is to be ripped out.
>
> ---
>
> ⚠ **`Settings → Gameplay → Vehicle Damage` was reset to Off by the U35d-pre-2 immunity test** (it
> is a `PlayerPrefs` value and the test had to write it). If it was on before, turn it back on.
>
> ⚠ **THE BLENDER MCP IS REGISTERED FOR THIS PROJECT** (`claude mcp add blender -- uvx blender-mcp`,
> done by the user 2026-08-17) - but a session that starts before Blender's addon is listening has no
> Blender tools. **The addon speaks plain JSON over TCP 127.0.0.1:9876 and can be driven directly**,
> which is how U35c's model was built without restarting anything. The same is true of Unity's own MCP
> on `http://127.0.0.1:8080/mcp` (JSON-RPC, 48 tools). Worth knowing before anyone burns a session
> restarting for tools that were reachable all along.
>
> ⚠ **THE BATCH PLAY-TEST IS OFF - the user reversed it, 2026-08-16:** *"ברור, אנחנו צריכים לבדוק
> אחרי כל פיצ'ר כן."* Every unit is play-tested at its own boundary, exactly as `CLAUDE.md`'s
> "autonomous units, one checkpoint each" always said. The one-batch-at-the-end reordering is dead and
> is recorded in the decisions log rather than deleted, so it cannot be rediscovered as the plan. The
> reason it was reversed is the reason it should have been: **a ragdoll is judged with eyes and I have
> none.**
>
> **What is deliberately NOT in the port:** the radio (dropped 2026-08-17, above) and the dance's
> tappable arrows (dropped 2026-08-16 - and with U31 gone its last trigger went too). Both are in
> **Deferred** with their history.
>
> *Ledger audited 2026-08-16: the U28b and U33 scene-rig debts are closed, a duplicated section and
> four malformed table rows are fixed. Open-work census re-cut 2026-08-17 by the five decisions above.*

### U35i, 2026-08-18 - the police helicopter is a solid object, and hitting a police vehicle is a crime - BUILT and USER-CONFIRMED - `a2e3438`

> ✅ **USER-CONFIRMED 2026-08-18, same day as the build** - *"looking good."* Not `done` only because
> rule 3(a)'s frame measurement is on a Player that does not exist yet. **The 1.4× knock-on was
> flagged to the user at hand-over and not objected to**: ramming a cruiser now costs the player's
> own car the vehicle damage multiplier instead of a wall's, because cop cars finally count as
> vehicles. If that ever reads wrong, the fix is one branch in `VehicleDamage.OnCrashed`, not a
> revert of `HitPolice`.

**The user's ask, in one message:** *"בוא נוסיף פיזיקה לאובייקט של המסוק המשטרתי, שלא נוכל פשוט ליסוע
דרכו. אם מכונית מתנגשת במסוק אז שהוא יזוז גם כמובן (בנוסף התנגשות במסוק משטרתי או במכונית משטרתית.
כמובן גם קוראת למשטרה, עלייה בכוכב)."* Three things, and they are separable: **be solid**, **be
shovable**, and **be a crime to hit** - the last one covering the cruiser as well as the helicopter.

**Why it was hollow in the first place, and it was a decision rather than an oversight.** U35c's own
class comment argues the craft *"never lands, is never entered and never collides, so it needs no
PhysX at all"*. The first two halves are still true; the third was simply wrong about the parked
state, because the H145 sits on a pad in the open world for the whole game and the user drove
through it. The comment has been rewritten rather than deleted - what it was right about (no
`CopCar`, no `HelicopterController`, no flight model) still holds.

**① The hull.** `PoliceHelicopterBuilder` now writes a Rigidbody (**2200 kg**, the Huey's figure) and
**four BoxColliders** on the root - skids, cabin, boom, fin - measured off the glb's own accessors
and stated in prefab space, with **the rotor disc deliberately outside all of them** for the same
reason the Huey's collider excludes its own: a 10.4 m disc collider would sweep every façade the
craft hovers past. The build re-measures the airframe and warns if a re-export moves a box outside
it. Centre of mass is pulled down to **(0, 0.9, 0.8)** - PhysX's own figure from these boxes sits at
1.5 m, and a 2.74 m skid track under a 1.5 m centre tips on a hard side hit.

**② Two regimes, switched on the state.** **Grounded** (`Parked`, `Scrambling`) the body is DYNAMIC
and asleep on its skids: a car shoves it, it slides, settles and sleeps again where it stopped, and
it launches from wherever that is. **Airborne** (`Climbing`, `Hunting`, `Returning`) it is
KINEMATIC and the transform is written by `SmoothDamp` exactly as before - kinematic is what makes
it a wall to the player's Huey without needing a flight model to fall out of the sky with. The flip
follows memory `physx-pose-stale-on-activate` exactly: pose written to the body **after**
`isKinematic = false`, velocities zeroed, `SyncTransforms`, then `Sleep()` (memory
`sleeping-props-need-awake-time-sleep` - a resting body that stays awake integrates once on any pad
that is not dead level and wakes itself tilted).

**③ The probes had to learn to skip their owner.** The roof probe fires from 300 m above the hover
slot, and the hover slot is where the helicopter IS - the moment it had a hull, a plain `Raycast`
would have returned its own engine deck as "the tallest thing under the slot", lifted the slot 12 m
above that, and climbed 12 m every quarter second for ever. `GroundUnder` takes the nearest hit that
is not a child of this transform, and both probes go through it.

**④ Hitting a police vehicle is a crime, and it is a THIRD line, not the wall's.** `CrashSensor`
used to answer "not a vehicle" for anything police - not politeness, but the feedback loop U19 paid
for once: cops crowd you constantly, and a low bar against police contact mints a crime every
cooldown, which spawns another cop and resets the give-up clock. It now reports `HitVehicle` **and**
a new `HitPolice` (a `CarController.IsPolice`, or a `PoliceHelicopter` in the parents), and
`CrimeWatch` judges police contact by **`PoliceTuning.PoliceCrashCrimeSpeed = 3.5 m/s`** - measured,
not picked: sampled in Play, three cruisers crowding a stopped car came in at **2.15, 2.30 and
2.45 m/s**, and a deliberate ram read **6.93**. `AtFault` still applies on top and is what clears a
cop that shunts you from behind (two of those three read `atFault=False` by themselves).

⚠ **One knock-on, and it is wanted:** `VehicleDamage` is the only other reader of `HitVehicle`, so
ramming a cruiser now costs the player's own car the 1.4× vehicle multiplier instead of a wall's 1×.
The cruiser itself is still immune (`VehicleDamage.Immune` on `IsPolice`).

**⑤ Skid friction is a measured number.** On Unity's default 0.6/0.6 a 1400 kg car at 15 m/s moved
the parked aircraft **0.82 m** - a nudge that reads as "bolted down" rather than as the shove that
was asked for. `HeliSkids.asset` (dynamic **0.30**, static **0.45**, `Minimum` combine) gives
**1.38 m and a 3° swing** on the identical hit. It is also the physically right direction: skids are
a low-friction contact on purpose, which is why real ground handling puts wheels under them.
(Saved as `.asset`, not `.physicsMaterial` - `AssetDatabase.CreateAsset` answers that extension with
*"should not be used to create a file of type 'physicsMaterial'… will in a future release be changed
to an exception"*.)

**Also fixed in passing:** the pad pose came from `WorldBuilder.Police` at the cruisers' **ride
height**, 15 cm up - clearance for a WheelCollider and nothing to a skid. A dynamic body left there
drops and thumps at start. `Configure` now probes the ground once, **before** moving the craft there
so it cannot read its own hull, and rests the skids on it (pad y −0.05, flush).

**Measured in Play, from the built prefab (2026-08-18):**

| what | reading |
| --- | --- |
| parked | dynamic, **asleep**, 0.0° tilt, flush at y −0.05 |
| ram at 15 m/s (54 km/h) | moved **1.38 m**, yaw **3.0°**, tilt 0.0°, back asleep, **+1★** |
| the judgement | `crime=True closing=5.14 police=True other=Police Helicopter atFault=True` |
| cruisers crowding a stopped car | 2.15 / 2.30 / 2.45 m/s → **not** a crime, every time |
| a deliberate ram of Cop 0 | `crime=True closing=6.93` → **+1★**, cops responded, arrest, bust |
| 3★ launch from a SHOVED pose | `Scrambling → Climbing → Hunting`, kinematic, 34.0 m over the target, 0.0° tilt |
| recall | `Returning → Parked`, snapped to the pad, dynamic, asleep, 0.0° tilt |

**One measured edge, left as physics rather than special-cased:** land the helicopter on a car parked
on its pad and the 2200 kg body ejects itself sideways - sampled once at 5.4 m and 21° of yaw. It
self-heals, because `_pad` is fixed and the next sortie's landing snaps it home.

#### The recipe - what to try

1. **Drive into the parked H145** at the station pad (it is ~18 m east and 12 m south of the cruiser
   bays). It should stop you like a wall, slide a metre or so, and **give you a star**. A slow
   nudge under ~13 km/h should cost nothing.
2. **Ram a cruiser** - also a star now. Then let the cops crowd you while stopped and check you are
   NOT gaining stars from them touching you; that is the whole point of the 3.5 line.
3. **Get to 3★** and watch the helicopter launch **from wherever you shoved it**, level itself on the
   climb, and hold station. Escape, and watch it come home and settle on the pad.
4. **Fly the Huey into it** while it hovers - it should be solid there too, and unmoved.

### U35h, 2026-08-17 - breakable street props - BUILT, benches re-sited 2026-08-18, awaiting the user's play-test

**2026-08-18 - the user's first play-test:** *"הפונקציונליות של הספסלים טובה, רק צריך לשנות את המיקום
שלהם - בוא נשים אותם על השדרה ולא על הכביש, כלומר על הרצועה של איפה שנמצאת הפיצרייה, וגם באופן מקביל
בצד השני."* **The benches were on the road**, and the cause is a real one: the downtown avenue is a
DIVIDED road whose one network centreline runs down the median, so `lights.sideOffset` (4.5 m) from
it is the inner lane, not the kerb - the pizza-place benches stood at x −6, in the carriageway
(kerb measured at ±15, pavement 0.15 m up, x ±16 outward). **Fix:** benches no longer take their
line from the network at all. They follow the two long crowd strips of `npc.config.ts` (`east`, the
pizzeria's pavement, and `west`, the one across the avenue - ~280 m each), one every 20 m,
2 m to the building side of the walkers' line (the two crowd lanes are ±1 m of it, so the crowd
passes between bench and kerb), facing the road, 14 m in from either end, skipping any place's
footprint + 2.5 m (the 7-Eleven and the pizza place both stand on the east pavement). **24 benches**
- west 13/13, east 11/13 (two skipped at the storefronts) - at x ±18–23, y 0.16, and NO benches at
the gas station / auto shop / 7-Eleven any more (the user asked for the boulevard; those come back on
request as one `Anchor` line). Cones unchanged (8). **98 props total.** Sampled in Play: 24/24
benches asleep, 0° tilt, `Disturbed=false` at t = 4.5 s. Bake reads `snapshot.Npc.Strips`, so
`BuildProps` takes the Npc spec; the scene is re-saved. **The bench siting is USER-CONFIRMED the same
morning** - *"הספסלים במקום טוב"* - and the reason it went wrong is memory
`avenue-sideoffset-is-the-inner-lane`.

**Same test, second finding - "the cones were light blue for a moment" (`הקונוסים היו בצבע תכלת
לרגע`):** the Editor's async shader compilation placeholder. Of the 120 compressed glTFast materials
in the game, the cone's is the ONLY one with the `_OCCLUSION` keyword (Sketchfab packed AO into its
metallic-roughness texture; glTFast wires it up) - a shader variant nothing else ever compiles, so
the first cone drawn shows cyan until it does. Editor-only (a Player has no placeholder), but a
variant to build and load for nothing. **Fix:** `WorldBuilder.Props.DropOcclusionVariant` strips the
keyword (and the occlusion slot) from the writable Compressed clone every build - AO on a 0.7 m
cone is invisible - so the props share the keyword-less variant 97 district materials already
have. `Build Props` now also `SaveAssets` so the clone edit lands.

**Two halves, both in.** The user approved the vision and supplied three Sketchfab GLBs
(`modern_bench_1`, `public_trash_bin_1`, `traffic_cone_game_ready`) - *"נוכל להשתמש ב-assets האלה
לדריסה… כמובן בלי להוריד ביצועים."*

**What it is in play:** `Settings → Gameplay → Street Props` (default **On**; Off is today's game -
zero props exist and every pole is rigid). **98 props** stand on the pavements - **66 bins** (one per lit
intersection, 1.2 m onto the pavement beside the junction's first pole), **8 cones** (rows of four
in front of the gas station and the auto shop) and **24 benches** (a row down each pavement of the
downtown avenue - the boulevard - every 20 m, facing the road; re-sited 2026-08-18, above). A car sends them flying with a light
clatter and **no star, no dent, no crash thump**. Ram a **traffic-light pole** above 7 m/s and it
**topples away from you**, its lamps go dark, sparks fly, the crash itself is what it always was (a
star ≥ 6 m/s, a dent, the thump), and the rest of the junction keeps cycling. Everything that was
knocked over is **put back GTA-style** once it has lain still 6 s and is either > 120 m away or out
of the camera's frustum (> 15 m) - poles stand up and light again. On foot you kick cones and nudge
bins instead of being stopped by them.

**The performance answer, measured in the Editor:** at rest **0 of 82 bodies awake, 0 disturbed,
0° tilt** after 6 s of play (0 of 98 after the bench re-site) - the props are dynamic Rigidbodies that are `Sleep()`ed in `Awake`
before PhysX has integrated them once, and a sleeping actor costs nothing until touched. Poles cost
nothing until hit (static capsule → `AddComponent<Rigidbody>` on impact, the `LotCar` promote
pattern). Awake cap 16 (oldest movers are put to sleep), single-LOD cull groups at 60/80/100 m,
four materials, no per-object instances. Sampled: pole `Light_010_007` rammed at 15 m/s → `Down`,
body, layer `Props`, lying along +z (away from a ram from −z); four cones rammed → flung, **0
`CrashSensor.Crashed` events**; ~35 s later every one of them and the pole were back home,
`Disturbed=false`, the pole repainted (amber lit).

**Assets:** the raw downloads (35 MB, one bin texture alone 4096²) are NOT in the repo - they live in
`~/TheBlockSource/props/` like the district sources - and `tools/prep-props.sh` (headless Blender
5.1) bakes the Sketchfab root matrices, puts the origin at the base, scales the cone (the file is
29 m tall - a Sketchfab ×100 root with no ×0.01), decimates the bin 8,940 → 2,450 tris and downsizes
every texture to ≤ 1024², writing `Assets/Models/Props/{bench,trash-bin,cone}.glb` (4.0 / 1.1 /
1.6 MB, LFS). Then `Compress Textures` (9 new textures written).

**Files:** `Scripts/World/{Breakable,StreetProp,BreakablePole,PropSystem}.cs`,
`Editor/WorldBuilder.Props.cs` (**The Block → Build Props**, also inside Build World after the police
pass), `TrafficLightPole.Down`, `Progress.BreakablePropsOn`, the Settings row, `SfxCue.Clatter` +
`GameAudio.Clatter`, `DamageFx.Sparks`, `PlayerController.OnControllerColliderHit`, a new **`Props`
layer** (TagManager slot 9) with early-outs in `CrashSensor`, `TrafficCar` and `LotCar`,
`Physics.IgnoreLayerCollision(Props, Pedestrian)`. Prefabs in the gitignored `Assets/Prefabs/Props/`
(they reference Compressed material clones); the layout is a serialized list of 98 rows on the
scene's `PropSystem`, spawned at runtime.

**Two things found by the first Play sample and fixed:** the police station's kerb is the cruisers'
bays - four cones and two benches spawned inside parked cop cars, awake from frame one - so the
station is not an anchor; and a bin on a pavement slope was tilted before `Start` ran (a physics
step fits between the spawn and `Start`), so home is captured and the body slept in `Awake`.

**Play-test recipe (one thing at a time):** ① drive to any lit junction - a bin beside a pole; ram
the pole hard: topple away from you, dark lamps, sparks, one star as today, others cycle; drive
150 m and back - upright and lit. ② gas station forecourt: four cones - drive through: they scatter,
clatter, **no star, no dent**. ③ the avenue by the pizza place: benches down both pavements, on the
pizzeria's strip and the one across, facing the road, none in the pizzeria's or the 7-Eleven's
doorway; a fast hit slides one. ④ on foot, walk into a cone: it goes over. ⑤ `Settings → Gameplay →
Street Props: Off`: everything vanishes, poles rigid.

**U29 IS DONE AND USER-CONFIRMED (2026-08-16).** *"looking good."* The roster is Joe, Jody and
David; the pick dresses the player AND the stage dancer, and the character screen got the studio
lighting U26 never ported. Section below. Nothing about it is open.

**U28 IS DONE AND USER-CONFIRMED (2026-08-16).** The money loop is closed: the 7-Eleven's doors open
as you walk at them, the counter sells four power-ups, all four effects fire, and U25's emoji font
landed with it. Play-tested in three rounds - *"דלתות אוטומיות עובדות טוב"*, then the two faults
those rounds found, then *"עובד טוב"*.

**U28b IS DONE AND USER-CONFIRMED (2026-08-16).** The tank is real: distance-based burn, a limp mode
at empty that never strands you, hold-`Space` refuelling at the Paz forecourt, and the fuel bar in
the slot the sprint bar shares. *"looking good."*

**Its scene rig landed in `a269a6b` and the debt is CLOSED** (verified 2026-08-16: working tree
clean, `World.unity` holds `GasStation` on the `Place_GasStation` prefab instance with its three
pumps wired, `FuelSystem`, `FuelGauge`). It was deliberately left out of `4f46f70` because a
parallel session was mid-unit on the police officer and the `N` mute key, so `World.unity` and the
police prefabs carried both units' changes at once - only U28b's own hunks were staged, and the rig
rode in with U19e's scene commit exactly as planned. `The Block → Build Gas Station` still rebuilds
the whole rig in one click if it is ever lost.

**U19e IS DONE AND USER-CONFIRMED (2026-08-16)** - *"U19e נבדק וגם גמור."* The officer who drives the
cruiser and gets out to arrest you on foot: three officers sit in three cruisers, one gets out at
18 m and runs at you. **All three faults the play-tests found are closed**, and the block below keeps
the mechanism and the measurements because every one of them was a lesson:

1. ~~**No bust when she catches you.**~~ **FIXED AND USER-CONFIRMED 2026-08-16** - *"המעצר הרגלי
   עובד טוב."* A logic inversion in `PoliceSystem.FootArrest` had put the grab test on a path that
   never runs during a chase; `Step` is called before the decision now, and the arrest fires.
2. ~~**She walks into the player rather than stopping beside them.**~~ **FIXED AND USER-CONFIRMED
   2026-08-16.** `PoliceTuning.OfficerStandoff` = 1.1 m.
   **The cause is that the player is invisible to the navigation system**: a `CharacterController`
   is neither a `NavMeshAgent` nor a carve, so nothing knows a body is standing there and a
   destination set to their exact position is an instruction to occupy them. Implemented as a
   **pulled-back destination rather than `agent.stoppingDistance` alone**, because one mechanism has
   to serve both movement paths - `CopOfficer.Walk` is a hand-rolled straight line with no agent in
   it, and the spawn car park, where a foot chase is most likely to start, has no NavMesh within
   10 m. `stoppingDistance` is set to a QUARTER of the standoff, not to a match: the destination is
   already the standoff point, so matching them would halt her at two standoffs - outside her own
   1.6 m grab radius, i.e. the arrest silently never firing again. The quarter is a dead band so she
   does not re-solve for a point she is standing on every 0.25 s and shuffle. Two traps handled:
   the pull-back is **clamped to her own distance** or, once she is nearer than 1.1 m, the
   destination lands behind her and she retreats to arm's length every time you close in - a grab
   radius that pushes its own target out of itself; and both paths aim her along her travel, which
   is right running and wrong on arrival, so `FaceWhenClose` turns her to you inside 1.5 standoffs.
3. ~~**She sits 21.4 cm through the cruiser's roof.**~~ **FIXED AND USER-CONFIRMED 2026-08-16** -
   *"שוטר בתוך מכונית נראה טוב."* It took TWO numbers, not the one the first fix assumed: the rider
   scale, and then the seat's X, because **a rider scale is not only a height**. See the block below.

**How to test it, which is not obvious:** there is no on-foot crime in this game - `CrimeWatch` gates
every crime on `Driving` - so a wanted level while on foot is only reachable through the debug key.
Drive out of the spawn car park (it has **no NavMesh within 10 m**, so she falls back to running in
straight lines there and it is the wrong place to judge her), get out on a street, press **`P`** for
one star, and wait ~20-30 s for the cruiser to drive over from the station.

### U35d-pre, 2026-08-17 - the police can catch you in a vehicle - CLOSED AS SUPERSEDED, no play-test of its own - `33420c8`

> **Taken off the pending list by the user, 2026-08-17** - *"u35d-pre גם אפשר להוריד אמרנו שיורדים
> מהרמפה."* It is not a code removal: **U35d-pre-3 is a rewrite of this ramp into relative terms and
> rests on the pull-over mechanism this unit built**, and the user confirmed pre-3 in the same
> message. Kept below because the reasoning is the reasoning pre-3 inherited.

**The user's report:** *"אנחנו צריכים לוודא שהמשטרה תופסת גם אם אני בתוך הרכב… כלומר שתפיסה תהיה גם אם
אני בתוך אופנוע / רכב."* It is not a tuning complaint - **an in-vehicle arrest was unreachable**, and
two independent things made it so. Both are fixed; the second was the user's own design call.

**① THE BUG: the arrival ramp is written for a target that is standing still.** `CopDriver.ChooseSpeed`
had two clamps that assume a stationary quarry, and against a moving car they are a brake applied to
a cop that has not caught anybody:

```csharp
if (distance < ArriveDistance)  wanted = Min(wanted, ArriveSpeed);          //  8 m → 3 m/s
if (straightRun)                wanted = Min(wanted, distance - ArrestRadius);
```

So a cruiser that got inside **8 m** of the player's car braked to **3 m/s** while the player drove
away at 20. The 4 m arrest radius was **not reachable on a moving vehicle at all** - which is exactly
why it worked on foot, where the number it is handed is genuinely zero.

Both are relative now, through a new `CopDriver.QuarrySpeed` that `PoliceSystem.Step` writes each
step from `TargetSpeed()`:

```csharp
float arrive = Max(ArriveSpeed, QuarrySpeed + ClosingSpeed);   // ClosingSpeed = 2 m/s
if (distance < ArriveDistance)  wanted = Min(wanted, arrive);
if (straightRun)                wanted = Min(wanted, Max(arrive, distance - ArrestRadius + QuarrySpeed));
```

**On foot `QuarrySpeed` is zero and both lines reduce to the old ones character for character**, so
every U19e number stands and nothing that was play-tested has moved. At 20 m/s it asks for 22, which
the rubber band's own `MaxSpeed` caps back to 20.5 - it can only ever raise a floor, never a ceiling.

**② THE DESIGN: `ArrestMaxSpeed` was a precondition nobody ever met.** The web build busts you at 4 m
after 1.5 s **at any speed** (`police.ts:454` - there is no speed test in it). The port added
`ArrestMaxSpeed = 6` because a BUSTED card over a car still doing 70 km/h reads as a bug. That reason
is right and the side effect was fatal: a player who simply kept driving was never caught.

**It is an ESCALATION now, not a gate, and the shape is the user's call** (asked, and answered
*"עצירה כפויה ואז מעצר"*):

| | Condition | Outcome |
| --- | --- | --- |
| Stationary arrest, unchanged | 4 m **and** both under 6 m/s, 1.5 s | BUSTED, exactly as U19 shipped |
| **Pull-over, new** | 4 m at **any** speed, `PulloverHold` = 2.5 s | 🚨 hint → forced braking → BUSTED once stopped, or after `PulloverStop` = 3 s |

**Three mechanisms in it are worth keeping, and each one is a trap this project has already paid for:**

1. **`HoldStill(seconds)` is a DEADLINE, not a bool.** The police run on `Update` and the vehicles on
   `FixedUpdate`; a flag one sets and the other clears drops out on every frame the two ticks do not
   coincide, and a car that is braked on half its frames shudders instead of stopping. The caller
   re-arms a 0.25 s lease each frame and **letting it lapse IS the release** - the same idiom
   `CarController.ExternalInputTimeout` already uses for AI steering input.
2. **One flag, one owner.** `_pulloverCop` lives on `PoliceSystem`, not on `CopCar`, because the thing
   being written is the *player's* throttle and there is one of those. Three cruisers each holding an
   opinion is the `Heat.Frozen` failure again (memory: `one-flag-one-owner-heat-frozen`).
3. **`Stabilize()` still runs on a braked bike.** Hard braking is precisely when a two-wheeler lies
   down, and an arrest that begins by throwing you off is U35a's mechanic firing on the wrong trigger.

The steering stays yours throughout - only the throttle and brakes are taken, which is what being
pulled over is. **Off switch: `PulloverHold = 0` restores U19's behaviour exactly.**

**③ A SEPARATE BUG FOUND ON THE WAY, and it predates this work.** `PoliceSystem.Bust()` charged the
wallet and cleared the heat **outside** `BustSequence`'s own `Running` guard, so two cruisers crossing
their thresholds on the same frame took the $100 fine twice and the second `Begin` silently no-opped.
Guarded now, and `Arrest` returns early for every cop while a pull-over is live rather than racing it
to the same `Bust()`.

**Compiled clean and verified by reflection against the live domain**, not assumed: `ClosingSpeed 2`,
`PulloverHold 2.5`, `PulloverStop 3`, `PulloverSpeed 2` all present on the **scene's** `PoliceTuning`
with U35c's own serialized values (`ResponseSpeed 34`, `HeliStars 3`) untouched - the new fields took
their C# defaults exactly as memory `scene-serialized-value-beats-cs-default` predicts, so **no scene
edit and no hand wiring were needed**. `MissionHud` and `BustSequence` are both in the scene for
`Bind()` to find.

**HOW TO PLAY-TEST IT - and it doubles as U35c's own test, because it is the same drive:**

1. `Continue` (never `New Game` - memory `new-game-wipes-the-test-balance`), take a car, earn a star.
2. **Keep driving at full speed.** The cruiser should now stay glued instead of falling back at 8 m.
   After ~2.5 s alongside: the hint, then your car is braked out of your hands, then BUSTED.
3. Repeat **on the motorcycle** - it must stop upright, not throw you.
4. Then U35c on the same run: 3★ for the helicopter, downtown for its tower guard, `M` for the GPS line.

**What to watch for and report, because it is what the numbers could get wrong:** cruisers *ramming*
rather than pulling alongside. Nothing brakes them at 8 m any more, and `SideGap = 3` is the only
thing aiming them at your flank instead of your bumper.

### U35d-pre-2, 2026-08-17 - the police response, rebuilt on the web's model - DONE, USER-CONFIRMED - `98470b2`

**The user's report, the third on this feature and the bluntest:** *"הפיצר של המשטרה פשוט גרוע והוא
לא עובד. אני מאוד לא מרוצה ממנו… מכוניות של משטרה יש להן את ההתנהגות שהן יכולות להפגע, בוא נוריד את
זה… המשטרות פשוט לא באות אליי… ב three js בגרסא שם זה דווקא עבד טוב."*

**The diagnosis is a design decision, not a number.** The two builds were read side by side:

| | three.js `police.ts` | Unity, before this |
| --- | --- | --- |
| Where a cop spawns | station bay only if you are ≤ 120 m from the station, **else a street 70 m behind you**, same frame | **always the station bays** (`Deploy`: *"however far away the crime was"*) - up to ~900 m of A\* through traffic, kerbs and wedges |
| Crime → first contact | 4-6 s anywhere | 36-45 s, traffic-dependent, after two tuning rounds |
| A cop that cannot get to you | impossible by construction (kinematic force-through) | a WheelCollider car that reverses out three times, then only replans - and one that dented itself to `EngineDead` coasted in `Chasing` for ever, unread by `PoliceSystem` |

`TryFieldSpawn` - the 60-110 m road-graph ring, out of sight preferred - **had existed since U19 and
was never reached**, because all three cruisers have bays. U19 turned the web's near-spawn off on
purpose: *"the response has a TRAVEL TIME. Getting away before they arrive is a real thing you can
do."* That was design rather than scar tissue, and it was the bug: the OPEN section this replaces
had already found that *"the time was never being spent driving"* and warned against raising speeds.
It was right. The answer was not to drive the 900 m.

**What changed - four things, three files of logic:**

1. **`VehicleDamage.Immune`** - a `CarController.IsPolice` car takes nothing: no dent, no shed part,
   no condition, no smoke, no fire, no fuse. Guarded in `OnCrashed` (before `Dent`/`Shed`) and in
   `Hurt` (the explosion's chain damage). The prefab is untouched - `CarController.Bind` re-adds the
   model at runtime anyway, so a guard in the model is the only fix that holds. Verified in Play:
   `Hurt(0.9)` on Cop 0 → health 1 → 1; the same on a civilian Audi → 1 → 0.7.
2. **`PoliceSystem.Deploy` is the web's rule.** Within `DeployInPlaceRange` (120 m) of you a car
   deploys from where it is - a bay it rolls out of (`PrependBayEgress` still applies), or the
   street it was driving home on. Beyond it the same car is **placed** on a street in the field
   ring, 50-90 m off (scene values changed from 60/110), out of your sight and out of the camera
   when there is such a spot. Its bay stands empty while it is out - the pool trick the file's own
   header describes. A bay car that finds no street still drives from the station: late beats never.
3. **`PoliceSystem.Relocate` - re-dispatch.** The web's *"a pursuer that can enter an unrecoverable
   state is a bug whatever the geometry"*, kept by the honest Unity means: an out-of-sight cop is put
   back in the field ring. Triggers: **fallen behind** (`RelocateBeyond` 120 m out of sight for
   `RelocateAfter` 6 s - sized against the shed clock, since a star is gone 12 s after contact is
   lost) and **wedged out** (the unwedge limit; first strike in view is a fresh route as before, the
   **second strike re-dispatches even in view** - twenty seconds rocking on a kerb in front of you
   is worse than a cruiser leaving and another arriving from round a corner). `RelocateCooldown`
   8 s per car; the officer on foot blocks it. **`RelocateAfter = 0` is the off switch** and
   restores U19's behaviour exactly.
4. **Speeds, grip, corners, arrest, pull-over: untouched.** Every U35c/U35d-pre number stands.

**Measured, in Play, from the spawn car park (176 m from the station, "the hardest case"):**

| | Before (ledger) | Now |
| --- | --- | --- |
| Crime → cop in sight | 30-45 s | **0.3 s** (placed 41 m off, in view - the car park is an open plaza, and out-of-sight is preferred not required) |
| Crime → `Arresting` (officer out) | - | **4.9 s** |
| Crime → BUSTED, 1★ | ~36-45 s | **~6.5 s** |
| Crime → BUSTED, 3★ | - | **~8 s** - two cars placed, the third found no separated street and drove from the station, as designed |
| Player jumps 176 m away mid-chase | cop lost | re-dispatched **6.4 s** later, 64 m off, out of sight; LOS regained 5 s after that; the cooling meter reset |

**Known residual, and it is the OPEN section's own item 1:** in the jump test the re-dispatched
cruiser then wedged in view at (117, −106) on the way into the station forecourt for ~5 s before
reversing free - the kerb geometry near the station is climbable and the car beaches on it. The
second-strike rule bounds that at ~20 s now; it does not remove it. Nothing else of the OPEN
section survives - its three "measure first" items are moot when the drive is 60 m rather than 900.

**HOW TO PLAY-TEST IT - it is the same drive as U35c and U35d-pre, so one run covers all three:**

1. `Continue` (never `New Game`). Take a car, drive to the **far side of the map** from the station
   (the beach, or downtown), earn a star. **A cruiser should be on you within ~10 s** and it should
   *drive into view*, never appear in front of you. Report if one pops in.
2. **Keep driving flat out** - the U35d-pre pull-over: hint, forced braking, BUSTED. Then break line
   of sight and run for a corner - a re-dispatched car should come at you from a *different*
   street, not the one you left it on.
3. Set `Settings → Gameplay → Vehicle Damage = Full` and ram a cruiser into a wall, or let it ram
   you: **no smoke, no fire, no dents on the police car** - and your own car still dents.
4. Repeat 2 on the motorcycle - it must stop upright, not throw you.
5. Then U35c: 3★ for the H145 (three cars now, all near you), `M` for the GPS line.

### U35g, 2026-08-17 - the auto shop: BUILT and USER-CONFIRMED (*"cool. mark this feature as done"*), awaiting U30b's frame measurement - `df0d9fc` + `d5e2da8`

**④ BUILT, 2026-08-17 - what shipped, and where it differs from the nine steps below.** The plan
below was written before the user's own brief for the feature (*"drive with every car / also the
motorcycle to the auto shop, user will see a writing that says something like: click C in order to
change the color of the car, than there is a palette (lets go for 10 colors), user picks one, car
gets painted and thats it"*), and three answers given in the same session; where the two disagree
the user's brief wins and the plan text stands as history:

- **The key is C, not E** - E is exit-vehicle while driving. C was a leftover, ungated debug toggle
  that hid the whole crowd (`CrowdSpawner`); it is now behind `debugToggleKey`, default off, like
  J and P. `ControlsGuide` → Driving lists C.
- **$30 a coat** (`AutoShopSpec.PaintPrice`), charged by `GameFlow.PickPaint` beside `Buy` - the
  second thing in the game that spends cash. Under $30 every swatch but the one you wear locks
  (`SetEnabled(false)`, dimmed - the fill IS the swatch, so no recolour); the colour already on the
  car is free and a no-op. `SfxCue.Purchase` on a pick, `Deny` on a refusal.
- **A pick closes the menu** - click → painted → gone; C again for another. Not a live-preview-plus-Done.
- **The motorcycle IS in** - the user's brief names it. And it was the one thing the census got
  wrong in a way that only a play-test could show: `MotorcycleBuilder` first gave the bike a paint
  slot on `Wolt_Teal`, and the user's first look was *"it changed only the box. the box needs to
  stay in the wolt color… and the red is changing"*. The GLB is two nodes - `WoltBox` (flat teal /
  white / black) and `Bike`, the whole scooter on ONE textured material whose atlas is painted red
  (measured: 60 % of its pixels bin at rgb(192,0,0)/(160,0,0)/(128,0,0)). A `baseColorFactor` tint
  on that is red × blue = black. So `CarPaint` grew a second mode - `ConfigureBakedBody(Color)`,
  "the paint is pixels, keyed on this colour" - and `PaintPalette.ForBakedBody` copies the atlas
  once per colour, re-hues every pixel within 30° of the key (saturated, not black) to the target at
  its own shading, and leaves chrome, seat, tyres and the box alone. Measured: red bins → blue
  bins at the same proportions, greys/blacks unchanged, `WoltBox` still `Motorcycle_Wolt_Teal`,
  41 ms one-off per colour, cached. The four cars keep the factor path.
- **Copy says "color"** - the user: *"we write colors, not colour"* - in every player-facing string
  (prompt, menu line, controls row). Code comments stay British.

**What it is, file by file.** `Scripts/World/AutoShop.cs` on `Place_AutoShop` (attached by
`Build Auto Shop`, `shutter` serialized by name): the distance from the 7-Eleven's `FocusPoint`
(vehicle anchor when driving) to `AutoShopSpec.ServicePoint` (−104, 0, 246.5 - 9 m out from the
shutter's centre, on the kerb), sampled every 0.25 s; inside `OpenRadius` 14 m the shutter rolls
up (`localScale.y` from its authored value to ×0.04 over 1.5 s, SmoothStep, MeshCollider follows;
the mesh's origin is its top edge, so that IS the animation), for anyone; `CanPaint()` = Driving +
the root has a `CarPaint` + inside `PaintRadius` 8 m + `|ForwardSpeed| < 1` - **the one predicate
behind both the prompt and the key**, drawn at `PromptVehicle` (`Press C to change the color`),
and on foot / on a non-paintable vehicle `Drive a car or the bike here to paint it` at `PromptDoor`
(so `Press E to enter` beside a bike wins, correctly). `Scripts/UI/Menus/PaintMenu.cs : MenuPanel`
(`paint-menu`, translucent) - wordmark, balance, 2 × 5 swatch buttons whose fill is the colour
through `MenuStyle.Ui`, price line, Done; the `ShopMenu` idiom, owns no money rules.
`Scripts/Vehicle/PaintPalette.cs` (static) - **the trap in the row, designed against:** ONE
`Material` per (source paint, hex), cloned from `CarPaint.Source` (the prefab's own paint - captured
in `Awake`, never a palette clone, so clones never compound and every Mustang keys the same entry),
coloured through `SetBaseColor` - **which moved here from `VehicleMaterials`** so the gamma rule
(`baseColorFactor` takes sRGB as written, `_BaseColor` needs `.linear`) has one copy and the editor
calls it; `enableInstancing` on; a fake-null cache entry (domain reload) is rebuilt. Two blue Mustangs
share one material and one draw call; measured: `paintMatsOnCar = 1`, `For(src, hex)` twice →
`ReferenceEquals`. **Not a `MaterialPropertyBlock`** - that is `VehicleDamage.Char`'s, and it wins
over the material while live: measured char → block on, `Repair()` → block off, the blue still
there. `Scripts/Vehicle/PaintStore.cs` (static) - `PlayerPrefs` `theblock.paint.<name>` int hex,
**keyed per config spawn name** (`CarSpawner.Spawn` sets `CarPaint.PersistKey = spec.Name` and
re-applies on spawn; `MotorcycleSpawner` the same under `Motorcycle`); a promoted / hijacked car
(`Take`) has no key and keeps its paint for the session only - it did not exist at boot and will
not at the next; `Reset()` walks the config's car names + the bike, called from `GameFlow.NewGame`
after the wallet's. Measured: paint blue → Stop → Play → `Continue` → the Mustang spawns blue and
the bike purple; keys then cleared and the $30 refunded so the user's save is as it was.
`AutoShopSpec` carries every number - `ServicePoint`, `OpenRadius`, `PaintRadius`, `StoppedSpeed`,
`ShutterSeconds`, `ShutterOpenScale`, `PaintPrice`, and `Palette` = White F5F6F7 · Black 14151A ·
Silver 9A9B9D · Red B31218 · Orange D96716 · Yellow D9A514 · Green 2E6B34 · Blue 1F4F9E · Navy
16263F · Purple 5B2A86 (the first nine are traffic/lot palette values - the city's own colours -
purple is the one the streets never wear). `GameFlow` - `PaintMenu paint`, `AutoShop autoShop`,
`Wire()`, `OpenPaint/ClosePaint` (the freeze), the C branch beside the E-at-counter branch, the Esc
chain, `Frozen()`, `MenuElements` + `paint-menu`, `PickPaint`. `MenuBuilder` installs `PaintMenu`
before `GameFlow`. `MotorcycleBuilder.BuildMaterials` - clones the bike's four materials into
`Assets/Materials/Motorcycle` (its renderers had pointed at the .glb's embedded sub-assets, which a
runtime write would have edited), paint slot = the `Bike` node's one material, `ConfigureBakedBody
(0.75, 0, 0)`.

**Off state / rule 2:** it is a place - nothing changes anywhere unless you drive to it and press C.
No settings switch. **Perf:** one distance every 0.25 s, the lerp only while the shutter moves, one
shared material per colour in use, one 512² RGBA32 atlas per colour used on the bike (768 KB each,
at most ten); U30b measures it like every place. **Rebuilds run:** `Build Auto Shop`, `Build
Motorcycle`, `Build Menus`; scene saved; `Boot.unity` came back as a pure re-serialisation and was
reverted.

**Not in this unit:** buying cars (`U35g-b`), a shutter sound cue, a mechanic idle, rims/spoilers,
the police car / jetski / helicopters (no paint slot - correctly).

**HOW TO PLAY-TEST IT:** `Continue` (never New Game - memory `new-game-wipes-the-test-balance`) →
any car → west along z = 165, north up the new street to the 🚗 pin → the shutter rolls up as you
arrive → stop on the street in front → `Press C to change the color` → C → click a colour → the
car is that colour, the menu is gone, $30 gone → drive off, get out, get in, quit to title,
`Continue` → still that colour. Then the motorcycle: its red body changes, the Wolt box stays teal.
Then on foot: shutter opens, no paint prompt. Same colour again is free; under $30, swatches lock.

**⑤ THE PLAN AS WRITTEN BEFORE THE BUILD - kept as history; where it says E, "live preview",
"not the motorcycle" or "colour", ④ above overrides it.**

**The user's spec, given while the asset was being modelled and it changes the row above:** *"ברגע
שאנחנו מתקרבים אז התריס הזה ייפתח, כלומר ממש תהיה אנימציה שזה נפתח… והמכונית לא בעצם תכנס לשם,
כלומר ברגע שאנחנו ליד המוסך אז האנימציה תהיה, ואז יהיה כבר תפריט של שינויי צבעים."* So: **the car
does NOT drive into the bay.** Pull up in front → the shutter rolls up (a real animation) → a colour
menu → the car you are sitting in is repainted → drive off. The interior is set-dressing seen through
the open door, and that is the whole reason it was dressed.

**① The asset - DONE, user-confirmed** (*"asset looks good"*), commit `df0d9fc`. Modelled in Blender
through the MCP from the user's reference photo (a Unity 4.6-era "My Auto Repair" corner lot):
10 × 8 m cinderblock shop, 4.5 m tall, a 4 × 3.4 m roll-up shutter, side door, louvre, rooftop
stair box, caged ladder, downpipe, electrical box, AC unit, pilasters, string course; a chain-link
yard with drums, a tyre stack, a red dumpster, planks, pipes, a pallet, an old fuel pump; an 18 × 13 m
slab with oil stains, a manhole, a painted kerb; two fence banners; and a **hollow bay** behind the
shutter - workbench, tool board, six paint cans, a three-shelf rack of cans, a tyre rack, tool chest,
compressor, hose reel, three fluorescent tubes, a yellow stop box on the floor. **The sign is
"AUTO SHOP"** on a dark board with a white border, red emissive text, per the user (*"a stronger
red"*). **Lewis from the U16b crowd stands outside by the ladder as the mechanic**, in blue overalls -
his Mixamo atlas recoloured by luminance-preserving tint (shirt + trousers → workman blue), the idle
clip baked at frame 40, decimated 25k → 14k faces, a wrench in his right hand. A red coverall and a
cap were tried at the user's request and **reverted by the user** (*"we are getting carried away…
lets just stick to what we had originally with the blue"*, then *"remove the hat"*).

Numbers: **25 nodes, 20.5k faces, 8.5 MB glb**, four generated tileable 1024² textures (cinderblock
light/dark, concrete, corrugated steel - written by a Python script, world-scale box-projected at
2 m / 4 m / 3 m per tile) plus Lewis's atlas at 1024². All five pass the U15 gate: `The Block →
Compress Textures` wrote block-compressed twins and the builder rebinds them (**"rebound 6 texture
slot(s)… 0 warning(s)"**). Source: `source-assets/auto_shop.blend`, saved BEFORE the join-by-material
so every prop is still its own object there. **Three nodes deliberately stay separate in the glb:**
`Shutter` (origin at its TOP edge, so `localScale.y` 1 → 0 rolls it up into the housing - the whole
animation is one lerp), `Mechanic_Lewis`, `Sign_AutoShop`. Everything else is `Static_<Material>`.

**② Placement - DONE, user-confirmed.** *"lets put it between procedural city 4 and procedural city 6…
connect roads to it."* The corridor between PC4 (x −88..88) and PC6 (x −119..−367), z 157..317, was
31 m of bare ground plate with `Road_06` along its south mouth (z = 165) and `Road_08` ending at
x = −77 along its north (z = 326). **`AutoShopSpec`** (runtime, `Scripts/World`) holds the numbers -
lot origin **(−96.1, 0, 245)**, yaw **−90°** so the model's +Z front faces the new street - and both
the builder and the map read it, so they cannot disagree. **Two new roads** through the same
spline-ribbon path as every config road: `Road_AutoShop_Street` x = −110, z 169 → 330, and
`Road_AutoShop_Stub` z = 326, x −77 → −106. **They TILE the old ribbons flush rather than cross
them** - the roads have no collider and sit coplanar at `roads.y`, so an overlap is a z-fight; the
street starts at 169 because `Road_06`'s ribbon ends there, runs to 330 because `Road_08`'s does, and
the stub stops at −106 where the street's own east edge begins. Kerb at x ≈ −103, three metres of
verge off the tarmac. Measured: lot bounds x −103..−89, z 236..254; `Physics.OverlapBox` over it hits
**nothing** outside the lot. **The map has a 🚗 "Auto Shop" pin** (`MapPois`, `MapPoiKind.Marker`).

**This is the first place the port has that the web build never had**, so its numbers are authored
in Unity space in `AutoShopSpec` and never go through `Convert.Pos` - there is nothing in `config.ts`
to export and the original repo takes no change. **`The Block → Build Auto Shop`** rebuilds it alone
(idempotent - deletes the previous lot and roads first) and **`Build World` carries it** in both the
roads pass and the places pass, so `SweepGenerated` keeps its meshes. Caught on the way: the
compressed-texture lookup is a static per-build cache that a standalone menu must `ResetTexturePass()`
or a run before `Compress Textures` remembers its misses forever; and `bpy.ops.transform_apply`
baked one object's location into its mesh, so `location.z = 4.4` put the sign board at 8.8 m.

**③ The census the user asked for** - *"if we have some car that we cannot implement this feature
for, let me know"* - measured on the prefabs, not guessed:

| vehicle | `CarPaint` | verdict |
| --- | --- | --- |
| Mustang, Audi, Tesla, Avenger | ✅ 1-2 paint slots each (`*_Paint_*` / `Tesla_primary`) | **paintable**, and so is every lot/street car promoted from them - same prefabs |
| Motorcycle | ✗ | it is a Wolt scooter in `Wolt_Teal/White/Black` livery; paintable only by giving `Wolt_Teal` a paint slot in `MotorcycleBuilder` - **not in this unit**, the user said "car" |
| Police car | ✗ | deliberately - a cruiser is not yours to paint |
| Jetski, Huey, H145 | ✗ | no body-paint slot; not a car |

**④ THE FEATURE - the plan, one unit, in build order.** Everything below sits on plumbing that
exists; the new code is one world component, one menu, one runtime palette and a store.

1. **`AutoShop : MonoBehaviour`** (`Scripts/World`), added to `Place_AutoShop` by the builder,
   handed the `Shutter` transform by name. On a 0.25 s cadence (the cops' cadence): the player's
   distance to a **service point** ~9 m in front of the shutter. Inside **`OpenRadius` = 14 m** the
   shutter rolls up (`localScale.y` 1 → 0.04 over 1.5 s, `SmoothStep`, its `MeshCollider` follows the
   scale); outside it rolls down. **It opens for anyone who approaches** - on foot, on the bike - the
   user's words were "when we approach". `CanPaint()` = `VehicleEnterExit.ActiveVehicle` is a car
   with `CarPaint`, inside **`PaintRadius` = 8 m**, and stopped (< 1 m/s - the same "stand still"
   the 7-Eleven counter asks for). While `CanPaint()`, `hud.SetPrompt("Press E to paint",
   PromptDoor)`; near but on foot / on the bike, `"Drive a car here to paint it"`.
2. **`PaintMenu : MenuPanel`** (`UI/Menus`), the `ShopMenu` idiom exactly - built once in `Awake`,
   repainted on open, translucent scrim so the shop and the car stay visible: a wordmark, a balance
   line, **a swatch grid of 10 colours** (white, black, silver, red, orange, yellow, green, blue,
   navy, purple - hand-picked, `AutoShopSpec.Palette`), the price under it, `Done`. **Picking a
   swatch repaints the car LIVE behind the scrim** - the menu is the preview - and charges once per
   pick; picking the colour it already wears is free and does nothing.
3. **`GameFlow`** owns E and the freeze, as it does for the counter: `!Pause.Frozen && E &&
   autoShop.CanPaint() && !paint.IsOpen` → `Pause.Set(true)`, `paint.Open()`; Esc order gets one
   more line above the map. On pick: `wallet.Charge(price)` (deny tick + refuse when broke - the
   U28 path), then `PaintPalette.Apply(carPaint, hex)`.
4. **`PaintPalette`** (static, `Scripts/Vehicle`) - **the trap in the row above, designed against:**
   ONE `Material` per (source paint material, hex), cached in a dictionary, cloned once from
   `CarPaint.Current` (which is the builder's `*_Paint_*` clone, so it keeps the compressed textures
   and the right shader - clearcoat on the Audi) and coloured through `VehicleMaterials.SetBaseColor`'s
   rule: **`baseColorFactor` takes the sRGB value as written** (memory
   `gltfast-basecolorfactor-gamma`). Two Mustangs painted blue share one material and one draw
   call; no car ever gets an instance of its own. `CarPaint.Apply` does the rest, and
   `VehicleDamage`'s repair path already restores `CarPaint.Current`, so a repaint survives a repair.
5. **Persistence - `PaintStore`** on `PlayerPrefs` beside `Progress`, **keyed per vehicle**:
   config-spawned cars by their spawn name (`config.vehicle.cars[i].name`), which is stable across
   reloads because `CarSpawner` spawns them in config order; a promoted lot/street car keeps its
   paint for the session (the material stays on its renderers) and is **not** persisted - it is
   recreated from the seeded layout on reload and there is no stable identity to key on. Applied by
   `CarSpawner` right after spawn. **Test through `Continue`**, never New Game (memory
   `new-game-wipes-the-test-balance`) - and decide whether New Game clears paint (it should: it
   clears the wallet that paid for it).
6. **Price**: `AutoShopSpec.PaintPrice = 30` - between a power-up and a tank of fuel; a Unity-side
   number, no `config.ts` change.
7. **Audio**: the shutter reuses U28's door cue if one exists (`AudioBuilder` bakes procedural
   cues; a 1.5 s ratchet is ~20 lines of note data - only if cheap); the pick uses the shop's
   till/deny pair.
8. **Off state / rule 2:** it is a place - nothing changes anywhere unless you drive to it and press
   E. No settings switch. **Perf:** one distance check every 0.25 s, the lerp only while the shutter
   moves, one shared material per colour in use; the asset is 20.5k tris behind U15's textures - U30b
   gets its delta like every other place.
9. **Not in this unit:** buying cars (the row's second half - `U35g-b`, its own checkpoint), the
   motorcycle, a mechanic idle animation (Lewis is a static bake), rims/spoilers.

**Play-test recipe, when it is built:** `Continue` → drive any car to the 🚗 pin (west along z = 165,
north up the new street) → the shutter should roll up as you arrive → stop in front → `Press E to
paint` → pick blue → the car goes blue behind the menu → Done → drive off, get out, get back in,
quit to title, `Continue` → still blue. Then approach on foot: shutter opens, prompt says drive a
car here.

### U35d-pre-3, 2026-08-17 - the in-vehicle arrest was measured against the wrong thing - DONE, USER-CONFIRMED - `d6cc611`

**The user's report, the fourth on this feature:** *"שמתי לב שאם אני יוצא מהרכב זה תופס ישר, בוא
נעשה שיש busted גם אם אני בתוך האוטו / על האופנוע."* Get out and it busts at once; stay in and it
never does. U35d-pre fixed the arrival ramp and the speed gate and **still left the in-vehicle
arrest unreachable**, because the thing being measured was never the right thing.

**THE CAUSE IS ONE NUMBER READ AGAINST THE WRONG GEOMETRY.** `ArrestRadius = 4` is a distance
between *centres*, and it is a fine number for a person, who is a point. Every car in this game is
**5.6 m long, the cruiser included** (`BoxCollider` bounds read live: Audi 5.64, Tesla 5.03,
Avenger 5.63, cop 5.65). Nose to tail their centres are 5.6 m apart. So a cop glued to your bumper
for the whole chase was at ~6 m and **never once counted as close**; 4 m was reachable door-to-door,
alongside, and from nowhere else - which is not where a chasing car is. On foot the same 4 m is
trivially reachable, and the officer's 18 m deploy radius makes it more so - hence *"if I get out it
catches me immediately"*. That contrast is the whole diagnosis.

**What changed - three things, one principle:** in a vehicle, every distance the arrest reasons
about is now relative to the *vehicle*, not to a point.

1. **`PoliceTuning.VehicleArrestGap = 2.5`**, new: in a vehicle, `close` is the **gap between the
   two bodies**, box to box, XZ, not the centre distance. Bumper-to-bumper is 0. `ArrestRadius`
   keeps its meaning on foot exactly. The gap is `PoliceSystem.VehicleGap` - three rounds of
   alternating `Collider.ClosestPoint` between the two root `BoxCollider`s (every vehicle carries
   exactly one; the wheels are `WheelCollider`s, which have no surface to ask), which lands within
   centimetres and is far inside the tolerance of a 2.5 m threshold.
2. **`CopDriver.ArrestDistance`**, new, written by `PoliceSystem.Step` beside `QuarrySpeed`: the
   centre distance the arrival ramp *aims at*. On foot it is `ArrestRadius`; against a vehicle it is
   the centre distance at which the gap is **half** the threshold - inside it rather than on it,
   because a ramp converges on its target from outside and one aimed at the threshold itself hovers a
   hair above it for ever. And the ramp's floor is now the **quarry's speed** rather than
   `QuarrySpeed + ClosingSpeed`: at the reach the cop sits on the bumper matching pace instead of
   shoving into it at +2 m/s for the whole hold. On foot both floors are `ArriveSpeed` and the line
   is U19e's.
3. **The rubber band's floor is relative too.** `MinSpeed = 8` is a number for a target on foot;
   **measured**: against the Audi at 9.0 m/s a cop 18.8 m back asked for 9.35 and closed at a third
   of a metre a second - thirty seconds to cover eleven metres, on a chase that expires in fifteen.
   Inside the band it now wants at least `QuarrySpeed + ClosingSpeed`, capped by `MaxSpeed` (the
   user's own 2.5% ceiling is untouched, so flat-out is still an escape). On foot it is `MinSpeed`.

**MEASURED IN PLAY, driven over MCP - two runs, both BUSTED, on the same code:**

| Run | Setup | Result |
| --- | --- | --- |
| Stationary | Audi parked on link 2 of the road graph, Cop 0 dropped 30 m behind, 1★ | BUSTED inside 11 s: Audi at the custody point `(160, 0.05, −106)`, stars 0, fine charged |
| **Moving** | Audi lane-following at **9 m/s** via `SetInput` from an `EditorApplication.update` lambda, Cop 0 dropped 14 m behind, 1★ | **BUSTED ~10 s after the star**, 60 m down the road, `BustSequence.Running = true`, stars 0 - the car was never stopped by hand |

The first two attempts of the moving run are worth recording as **test traps, not code faults**: the
Audi was steered straight with no lane-following, ran 400 m south of the lot into the world's
`South` wall (rear wheels slipping at 800 N·m against it, speed 0), and the corridor there is off the
road graph, so both cops planned to nowhere. Put a synthetic drive ON the graph and steer it along
the lane, or every number is against a car pressed into a wall.

**⚠ Both runs charged the user's real save**, because `Continue` was used (memory:
`new-game-wipes-the-test-balance`) - the balance read `$0` afterwards with `FinesOwed = 100`. If the
wallet was not empty before, that is where it went.

**HOW TO PLAY-TEST IT - the same drive as U35d-pre and U35d-pre-2, and it replaces their step 2:**

1. `Continue`, take a car, earn a star, and **keep driving at a normal pace** - not flat out, the
   2.5% ceiling still lets a full-throttle car hold a cruiser off. The cop should close, sit on your
   bumper, and within ~2.5 s of contact: 🚨 hint, brakes taken, BUSTED.
2. Then **stop the car and wait** with a star. The cruiser pulls up behind and busts you in ~1.5 s
   without you leaving the seat.
3. Repeat 1 **on the motorcycle** - the gap is measured against its 0.5 × 1.65 m box, so it must
   work there too, and it must stop upright.

### ~~⚠ OPEN - police pursuit, consider improving further~~ - CLOSED BY U35d-pre-2 (kept as history)

**Left open by the user 2026-08-17** - *"תרשום לך בצעד מרדף משטרתי - לשקול לשפר בהמשך… יכול לסגור את
הצעד הזה ואנחנו נטפל בזה בהמשך."* Closed the same day by U35d-pre-2 above; the section stays because
its measurements are the "before" column and its warning about speeds was correct.

**Where it stood, measured in Play rather than estimated.** The user's report was that the police
often never arrive. Three things were found and two were fixed:

| | Before | After |
| --- | --- | --- |
| Wedged outside the station | **18 s** | **~5 s** |
| Commanded speed on the run in (mean / worst) | 25.8 / 3.8 m/s | 28.1 / 9.6 m/s |
| Crime → arrest, whole run | ~43 s | **~36-45 s, traffic-dependent** |

So the station stall is down by roughly two thirds and the approach is no longer throttled, but the
**total is not reliably better**, because the time now goes somewhere else: in the last sampled run
a single ambient car blocked the cruiser for ~3 s mid-route, and the wedge counter was non-zero for
most of the drive.

**What a next attempt should measure FIRST, before touching a number:**

1. **Why the car still ends up 1.5 m north of a lane it was aimed at.** Two egress waypoints
   reduced it and did not remove it. Suspect the kerb geometry at (155, −100) is climbable and the
   car beaches on it - the forward box-cast reads "nothing ahead" while the car sits at v = 0.
2. **Whether ambient traffic should yield harder to a siren.** `SetPursuitObstacles` already hands
   the traffic every live cop, but a kinematic car is a wall to a cop, and the sample caught one
   holding a cruiser for 3 s at (54.7, −161.8).
3. **The `Unwedges` counter as a metric.** It was non-zero for most of a 40 s drive. A pursuit that
   never wedges is the target; the count is the cheapest way to know.

⚠ **Do not start by raising speeds.** `ResponseSpeed` was already raised 29 → 34 and it changed
almost nothing - the time was never being spent driving. See the memory files
`cop-wedges-leaving-the-station-bay` and `corner-limiter-throttles-the-approach`, and note that one
plausible fix (capping egress speed) made the stall **five times worse** and was reverted.

### U35c, 2026-08-17 - police helicopter + a police-response fix - DONE, USER-CONFIRMED (the GPS half CUT)

> ✅ **USER-CONFIRMED 2026-08-17**, and **halved in the same message**: the road route is deleted
> from the game - *"תוריד את הקו התכלת מהמפה, לא צריך את הפיצר הזה."* Everything below about the
> H145 stands; everything below about the GPS line is history, not behaviour. What was removed and
> what deliberately stayed is in RESUME HERE.

Three things landed, and the third was not in the plan - the user reported it mid-build.

**① The police H145, modelled in Blender.** The plan said "a cop-coloured `CarPaint` twin" of the
Huey. The user rejected that on sight of the first render and was right to: the player flies the
Huey, so a repaint reads as the same helicopter in a different colour. Five free CC-BY Sketchfab
models were offered and also rejected in favour of authoring one from a reference photograph.

- `source-assets/police_helicopter.blend` → `Assets/Models/Vehicles/police_helicopter.glb`
  (1.5 MB against the Huey's 18.7). **5,592 triangles**, 52 objects, **zero textures** - the entire
  livery is geometry and eight materials.
- **What makes it not-a-Huey, in the order it reads from 34 m:** a shrouded **fenestron** against an
  open two-blade tail rotor; four main blades against two; a thin boom against a military fuselage.
- Livery from the user's second reference (Israeli Police, white over navy): a swept boundary,
  `POLICE` in navy on the white, **`משטרת ישראל`** large in white on the navy (the user asked for it
  to be the biggest marking on the aircraft), `5X-BMD` on the boom, and a Star-of-David roundel on
  both flanks and the fin.
- Approved by the user at three checkpoints - silhouette, livery, and in-world scale beside the
  Huey. Measured in Unity at **10.4 × 3.9 × 10.9 m** against the Huey's 9.8 × 2.2 × 3.8.

**② The unit at 3★.** `PoliceHelicopter` is transform-driven with `SmoothDamp`, holds a slot
18 m behind and 34 m above whatever `PoliceSystem.Focus()` returns, and pins it with a URP spot at a
**512 shadow map** plus a translucent beam cone. `PoliceTuning.HeliStars` (ships 3, 0 = never) is the
off switch. Rotor and fenestron spin; the rotor has its own synthesised voice placed by distance.

**③ THE POLICE-RESPONSE FIX, reported by the user during the build:** *"הרבה פעמים שיש פשעים אז
המשטרה לא מגיעה או מתעכבת… אני רוצה שהמכוניות של המשטרה יגיעו במהירות רבה יותר"*, and the principle
*"כמו מסלול הכי קצר כזה, ממש שהן ידחפו להגיע לפושע."*

The cause was **not** the pursuit tuning, which is measured and was left alone. Cruisers *drive* out
of their bays - deliberately, so the response has a real travel time - and the one street the station
opens onto is ambient road like any other. A queue standing on it is a wall, and `CopDriver`'s
overtake cannot help a car that has not got moving yet. So:

- `TrafficSystem.SetKeepClear` - a **26 m apron** around the station that ambient traffic will not
  spawn into and is retired from. Refusing to spawn was not enough on its own: a car placed
  legitimately down the road drives onto the apron seconds later.
- Urgency dials, **written into the scene** and not into the C# initialisers, because
  `PoliceTuning` is a plain `[Serializable]` on `Heat` and the scene already held every pre-existing
  value: `ResponseSpeed` 29 → **34**, `ResponseGrip` 11 → **14** (the drive was corner-limited, so
  this is the number that shortens the wait), `OvertakeAfter` 1.5 → **0.9 s**.
- **The chase numbers are untouched.** `MaxSpeed` 20.5 and the rubber band are what keep a pursuit
  beatable once a cop can see you, and the complaint was about arrival, not about escape.

**④ The GPS route - ✂ REMOVED FROM THE GAME 2026-08-17 at the user's request** (*"תוריד את הקו
התכלת מהמפה"*). The paragraph is kept as the record of what was built and deleted; **none of it is in
the build.** `GpsRoute` ran the U19 A\* once for the map, at the cops' own 0.25 s cadence,
replanning only when the objective drifts >5 m or the player leaves the corridor by >15 m. Drawn on
`MapView` as a two-stroke Painter2D polyline under every pin. `Settings → Display → GPS Route`,
default **on**, pull-only. Mission pins opt in through a new `MapPoi.Guide`, because kind alone
cannot separate an objective from the 7-Eleven - both are `Marker`.

⚠ **The jetski gates are the deliberate blank.** They sit on open water with no street inside the
planner's 120 m snap, so `Plan` returns `Found = false` and the map draws **nothing**. A straight
line over the sea would be worse than no line: it would claim a road.

**Verified in the Editor, 21/21 checks:** no Rigidbody, no collider and no `HelicopterController` on
the prefab (that last one is what makes it un-enterable - it is the only thing that ever calls
`EnterableRegistry.Register`); the spot starts disabled; the URP shadow tier is really 1; 54 material
slots and none empty; A\* finds a 32-point route whose every point is within **2.0 m** of a street;
and a sea target returns not-found.

**Five findings worth keeping, all of which cost a render or a log line to discover:**

1. **`Light.shadowResolution` is a silent no-op in URP** - it takes the assignment and Unity says
   *"compatible only with the Built-In Render Pipeline"*. URP keeps its own copy on
   `UniversalAdditionalLightData`, both fields are private, and the tier is only read when
   `m_UsePipelineSettings` is false, so setting one without the other does nothing either.
2. **`RotorSound` cannot use Unity's 3D panning** - `OnAudioFilterRead` *overwrites* the buffer, so
   whatever the source spatialised is thrown away. The synth is the last word, so distance and
   balance are applied inside it (`SetPlacement`).
3. **Blender does no bidi shaping** - a Hebrew string comes out in logical order, which is visually
   reversed. It is handed over reversed on purpose and verified in a close-up render, never argued.
4. **A face-painted boundary cannot be finer than the faces carrying it** - the livery's navy/white
   split rendered as a staircase twice before it became its own skin with the edge solved per vertex.
5. **A flat decal on a curved hull sinks at its ends** - `5X-BMD` lost three characters into the
   boom, which narrows 0.2 m across the width of the word. Every decal vertex is projected now.

**The helicopter has no collider by design, so nothing stops it flying through a tower.** The guard
is a downward ray on the same quarter-second clock as the searchlight's ground probe: the hover slot
is lifted to 12 m over whatever is under it. Downtown is where to check that.

### U35b, 2026-08-16 - vehicle damage - BUILT and USER-CONFIRMED, awaiting U30b's frame measurement

The web build's cars are kinematic, so a crash there is a number: U34 made it cost a star and a
thump. This makes it cost the car. **Three layers, one switch, and the switch's Off state is exactly
today's game** - which is what keeps U30b's baseline valid (Tier 8, rule 2).

**What is in it**

- `Assets/Scripts/Vehicle/{VehicleDamage,DeformableBody,DetachableParts,DetachedPart,DamageBudget}.cs`
  and `Assets/Scripts/Vfx/DamageFx.cs`. The panels and the part nodes are wired by a new
  `BuildDamage` step in `Assets/Editor/CarBuilder.cs`, so **Build Drivable Cars** and
  **Build Police Car** both produce them; the component itself is added at runtime by
  `CarController.Bind`, beside `CrashSensor.Ensure`, because a component dropped on a prefab is
  regenerated away by the next build (the U19-to-U34 `CrashSensor` scar, paid forward).
- `Settings → Gameplay → Vehicle Damage`, `Progress.VehicleDamage`, **Off / Visual / Full,
  default Off**. Visual floors the condition at 0.05 - the car dents, smokes, burns and loses a door
  and still drives home; only Full lets it die.
- **Condition**, per car, no HUD readout - the smoke IS the readout, which is what GTA does and what
  the shared bar slot above the radar (`PlayerMeters` / `FuelGauge`, mutually exclusive by mode)
  leaves no room for. Under 5 m/s costs nothing; above, `(v−5)/45`, ×1.4 into another vehicle, capped
  at 0.4 per impact. **0.5 → smoke, 0.2 → fire, 0 → dead.** Three real crashes kill a car; one cannot.
- **The fuse, and it is the user's call (2026-08-16):** at zero the engine dies and the car burns for
  3 s. `E` gets you out; do nothing and you are put down beside it at t−0.4 s, on foot and unharmed.
  **Not a ragdoll** - U35a settled that a car does not eject you, and a showcase feature does not get
  to quietly reverse a decision about how the game feels.
- **The blast:** 8 m sphere, `AddExplosionForce` on every body, `LotCar.Blast` promotes parked
  fillers (static colliders the sweep cannot see, so U34's promotion needed a second door with no
  collision behind it), `RunOverSystem.Blast` downs the crowd through U35a's own path, one pooled
  point light for 0.3 s, and `SfxCue.Explosion` - the one cue with no line in `sfx.ts` to copy,
  voiced against `Crash` deliberately since the two are heard seconds apart. A star through
  `Heat.Bump`, and only when it is the player's doing.
- **A wreck cannot be used, and says so** (the user's addition, same day). `CarController.TryEnter`
  refuses once the engine is dead and `EntryRefusal` supplies the line - the U28 socket that exists
  precisely so a prompt and a key cannot disagree: *"Get back - this car is about to blow"* while the
  fuse burns, *"Wrecked - this car is not going anywhere"* after. The explosion also flashes
  *"Your car exploded. Find another one."* through `MissionHud.ShowHint`, **only for the car the
  player was driving** - a line per cruiser cooking off in a pursuit would be noise.
- **Repair is `Teleport`**, so `R` and U19's bust both hand back a whole car. The husk lasts 20 s and
  then the existing `Respawn` puts it home, repaired - which is also what stops four blown cars from
  stranding the player.
- **Caps:** 4 cars holding cloned meshes (the oldest is RESTORED, never the player's), 3 smoke/fire
  emitters (oldest stolen), 8 shed parts, 1 blast light. Off allocates nothing at all.

**Three findings, and two of them changed the design.**

1. **The Mustang is eighteen SkinnedMeshRenderers, and a `MeshFilter` sweep found ZERO dentable
   meshes on it** - reported by the build log, which is the only reason it was caught before the
   play-test. Its vertices live in **bind space**: through the renderer's transform the shell measures
   5.57 m in Y, a car standing on its nose. The deform core was rewritten to carry every vertex
   through `bones[i].localToWorld * bindposes[i]`, rebuilt per dent because the rig moves (wheels
   spin, door swings). Memory: `skinned-verts-live-in-bind-space`.
2. **The contact normal points the other way from the guess.** Written as `-normal` first, from
   reasoning, and the nose **bulged outward by 0.136 m**. Unity points `ContactPoint.normal` from the
   other collider INTO the body whose callback fired, so bodywork caves ALONG it - measured, then
   fixed: Mustang nose z 2.825 → 2.751, Audi 2.820 → 2.787. A shed part still flies against it.
   Memory: `contact-normal-points-into-the-body`.
3. **Layer ③ needs no Blender, and the .glb files are why.** Read out of the glTF JSON directly: the
   Mustang's eighteen nodes are one per MATERIAL, each spanning the whole car - no bumper node exists
   to detach - while the three lot cars kept `Door_R` / `Mirror_R` / `Window_FR` / `door_dside_f` from
   the web build's merge pass, and the cruiser kept `Roof light bar_0`. So doors, mirrors and the
   light bar come off for free and the Mustang sheds nothing; the build log says
   *"parts none"* for it rather than staying quiet. A Blender re-split of the hero car was weighed
   and **declined by the user**: `CarBuilder` rebinds paint by the material NAME `CarPrimaryColor`,
   so a re-export that renames anything breaks the car in every screenshot. Memory:
   `car-glbs-group-by-material`.

**Measured before the hand-over, not assumed.** All five models' meshes are readable (18/18 Mustang,
4/4 per Draco lot car, 13/13 cruiser - glTFast ends both its paths with `UploadMeshData(false)`;
memory `gltfast-meshes-stay-readable`). A dent moved 4,500 of 34,598 verts with the furthest 0.89 m
from the contact against a 0.9 m radius. Shedding and repair were exercised on all three part-bearing
cars. And the full chain ran in Play on the Audi: smoke → fire → fuse → explosion → charred husk →
20 s → repaired to `Circle`, not `Circle (dented)`, with the paint block cleared and the budget back
to zero. **No Editor errors in any of it.** Frame cost is deliberately NOT claimed - that is U30b's,
on the Player, and this is the row most likely to want a smaller `DeformCap`.

**The one number to look at again** is `maxDeform`, 0.28 m per vertex for the life of the car. It is
a look, and looks are the user's to judge; `DeformableBody` exposes it, the dent radius, the strength
and the jitter as serialized fields.

### The U35a play-test also turned up two unrelated faults, 2026-08-16 - both fixed

Neither is U35a's; both are recorded because a bug found and fixed in passing is a bug that comes
back if only the conversation remembers it.

1. **"המפה נעלמה לנו" - the map was gone.** Not a fault at all: `theblock.radar` was **0**. `GameMap`
   and the Map Camera were both present and enabled, and the preference is what hides the minimap
   (U26's Settings → Display → Radar). Set back to 1. **Worth knowing for every future report of this
   shape:** the radar, the day/night cycle, the sound and now the ragdolls are all `PlayerPrefs` that
   survive a New Game by design, so "feature X disappeared" is worth checking against
   `Progress` before anything is debugged.
2. **"אזור ליד הים שאם נוסעים בו פשוט נופלים והמכונית ממשיכה ליפול" - a real hole, and the
   measurement names it exactly.** The plate's collider is solid over Unity x [−700, +430] across the
   full **1400 m** of z, while the shore wall that seals its seaward edge is **600 m** long - it is
   `sea.Length`, because it was built to hold the *waterline*, and the water is 600 m of coast inside
   a 1400 m world. That leaves ~400 m of open plate edge north of the water and ~150 m south of it,
   with no collider of any kind past it: the water surface has none. A car that reached it fell for
   the rest of the session.

   **Fixed in two layers, on purpose.** `WorldBuilder.BuildWorldEdges` fences all four sides of the
   solid plate with 8 m invisible boxes on **Ignore Raycast** - verified by probe: the seaward edge
   now answers at z 300, 650 and −650 where it was open, and at z 0 and −200 the **shore wall** still
   answers first, which is the proof the fence takes nothing away where the beach and the swim are.
   And `Assets/Scripts/World/FallGuard.cs` catches anything below y −25 and returns it to the last
   spot it stood on solid ground, because a fence answers "the edge of the map" and not "a gap
   between two districts", and the failure mode is unrecoverable without Quit to Title. Verified:
   the player dropped to y −80 was returned to the exact metre they left. It logs a warning every
   time it fires - **a FallGuard warning in the console is a hole worth finding.**

### U35a, 2026-08-16 - ragdolls - BUILT and USER-CONFIRMED (*"עובד טוב"*), awaiting U30b's frame measurement

The web build's run-over is a canned Mixamo clip because Rapier on the main thread has no budget for
a 15-body articulated rig per victim. PhysX does. **Both halves of the row are built: the crowd and
the player.**

**What is in it**

- `Assets/Scripts/Npc/{Ragdoll,RagdollBudget,RagdollReaction,IRunOverReaction}.cs` and
  `Assets/Scripts/Player/PlayerRagdoll.cs`; the rig itself is written by
  `Assets/Editor/RagdollBuilder.cs` - **The Block → Build Ragdolls**.
- **Eleven bodies, ten joints, ~64 kg**, on all six pedestrian faces and all three player characters.
  Every dimension is MEASURED off the rig (a shell's length is the distance to the next bone) because
  nine Mixamo uploads have nine different skeletons; only the masses are typed, because a person
  weighs what a person weighs.
- **Not the cop and not the thief** - neither can be run over, so neither pays for a rig.
- `Settings → Gameplay → Ragdolls`, `Progress.RagdollsOn`, **default ON** - the one U35 addition that
  ships switched on, argued in the row: it replaces a REACTION, not a look, so it cannot re-open a
  visual judgement. Off is U18's clip, unchanged and still reachable, which is why
  `IRunOverReaction` exists rather than the clip path being deleted.
- **The cap is 4 and the oldest FREEZES**, keeping its pose and its own fade clock. Refusing the
  fifth would have put two people struck by the same bumper into two different mechanisms side by
  side.
- Player triggers, the user's pair: **a bike crash over 8 m/s closing**, and **a fall over 5 m**. A
  car does not eject you. ~~`K` throws the player on the spot~~ - **the debug key was removed the same
  day, at the user's request** (*"כן בוא נוריד את ה-K הזה עכשיו כבר לא צריך את זה"*), which is U30c's
  job done early and safe to do early for one reason: unlike `P`, `T` and `C`, this feature's REAL
  triggers are five seconds of gameplay each, so the video loses nothing by reaching it through a
  bike crash. `PlayerRagdoll.Launch` stays public - it is what the triggers call, and what a test
  can still fire.
- The stand-up is a **bone blend, not a clip**: control returns at the START of the 0.35 s blend. The
  Mixamo `Getting Up` FBX is not in the project and the seam for it is left open.

**Three faults were found by building it, and all three are the same shape - a physics system whose
state is not the state the scene appears to be in.**

1. **The crowd could not be rigged at all.** All six pedestrians failed with *"the avatar has no Hips
   bone"* while the three player bodies rigged first time. `PeopleImporter` sets
   **Optimize Game Objects ON** - U16b's own optimisation, whose comment said *"nothing hunts a
   pedestrian's bones until U18"* - and that option deletes the bone GameObjects. **It is now OFF for
   the six**, and `extraExposedTransformPaths` is NOT the way out: an exposed transform is an output
   the Animator writes, while the SkinnedMeshRenderer is skinned from the internal skeleton, so
   physics moving an exposed bone moves nothing anyone can see. ⚠ **This is a real perf debt and it
   is U30b's**: ~68 transforms per live body against a crowd cap of 160. What bounds it is that
   `CullCompletely` means an unseen pedestrian poses nothing, and the spawner already trickles.
2. **Every pedestrian's skeleton was being dragged to the world origin, downed or not.** Measured: a
   pedestrian standing at (10.8, 0, −159.4) with their hips transform reading (0, 0.83, 0). The cause
   is `RigidbodyInterpolation.Interpolate` on a KINEMATIC body: interpolation writes PhysX → Transform
   every frame, and PhysX's pose for a body no step has ever moved is the one saved in the prefab. The
   bones are now built `None` and switched to `Interpolate` only while simulating.
3. **A ragdoll appeared at the origin instead of where the victim stood.** `Physics.autoSyncTransforms`
   is off, so a kinematic body driven by a script has told PhysX nothing; going dynamic hands the
   solver the prefab's pose. Fixed with `Physics.SyncTransforms()` plus a per-bone pose write - and
   the write has to come **after** the kinematic flip, because a write to a kinematic body is a move
   target that going dynamic throws away. The first attempt had it in the other order and changed
   nothing, which is the more useful half of the lesson.

**Measured in the Editor, not assumed:** a victim hit at 14 m/s is thrown ~8 m and settles; the whole
cycle (launch → settle → lie → fade → recover → back on their route, animator re-enabled, budget
released) completes and the body is reusable. The player thrown by `Launch` travelled 4.25 m, stood
back up at the new spot, and control and camera both came back. **No Editor errors in any of it.**
Frame cost is deliberately NOT claimed here - that is U30b's, on the Player.

**One thing to look at in the play-test:** the knee and elbow hinges are derived rather than
hand-checked (a positive rotation about the character's right takes the shin backwards, and the
elbows' axis is the cross of the arm with the character's forward, which mirrors itself). If a corpse
folds a leg forwards, that sign is where it lives - `RagdollBuilder.Configure`.

⚠ **`Assets/Prefabs/Npc/` is gitignored**, so the crowd's rigs are not in the repo - they are rebuilt
by **Build Pedestrians**, which now calls **Build Ragdolls** at its tail. `Build Characters` does the
same. That hook is the U34 lesson paid forward: a rig written into a prefab that a builder
regenerates is a rig with a countdown on it.

**How to play-test it** - the scene is already built and saved, nothing needs rebuilding:

1. Play → **Continue** (not New Game - `new-game-wipes-the-test-balance`).
2. **The crowd.** Drive at ~50 km/h into someone on the pavement. Expect: the body is thrown,
   tumbles, lands, lies, fades - and the same person gets up and walks on. Watch for a knee or an
   elbow folding the wrong way, limbs buzzing against their stops, or a body sinking into the road.
3. **The player.** Crash the motorcycle into a wall above ~29 km/h; and separately, walk off
   something over 5 m.
4. **The switch.** Pause → Settings → **Gameplay → Ragdolls → Off**, then run someone over again:
   U18's clip should come back exactly as it was.

### U35a follow-up, 2026-08-16 - coming off the bike was only half a state change

The user played it and found the half that was missing: *"כרגע ההתנהגות הזאת לא מוגדרת וזה נראה
מוזר"* - after the bike throws you, nothing puts you back. **The fix is that a throw now DISMOUNTS and
a stand-up REMOUNTS**, so a crash is an interruption of the ride rather than the end of it.

**What "undefined" actually was.** `PlayerRagdoll.Launch` switched off the `PlayerController` and left
everything else exactly as it was, so for the whole ragdoll and for good afterwards:

- `VehicleEnterExit.Mode` stayed `Driving` and `bike.Driven` stayed `true` - **the keyboard was still
  driving the bike** while its rider lay in the road.
- The player's root was **still parented to the seat**, so the body was dragged along by the bike it
  had just come off, and the stand-up's world-space `Teleport` fought that parent every frame.
- Standing back up therefore re-enabled `PlayerController` *inside* a vehicle: two controllers reading
  WASD, and a player wearing the seat anchor's 1.1× rider scale. Only pressing `E` could unpick it.

**The four changes:**

- `PlayerRagdoll.Dismount` - `LeaveVehicleNow()` on the frame of the throw, then the rider is put back
  at the SEAT pose it captured first. Left alone, `LeaveVehicleNow` stands you beside the bike at road
  level, which is right for stepping off and wrong for being thrown: the ragdoll would start a metre
  down and a metre sideways of where the rider was.
- `PlayerRagdoll.Remount` - stands the bike up at the spot the body settled, on the heading the fall
  began with, and boards it. **The bike comes to the rider**, because the alternative has no answer for
  a bike that ended up in the canal or fifty metres down the street, and both are normal outcomes of a
  crash hard enough to fire this.
- `VehicleEnterExit.Board(IEnterable)` - public, extracted from `TryEnter`'s tail, so the remount is
  the *ordinary* door-less mount and not a second way of getting onto a bike. It also fixes a real bug
  of its own: the OnFoot branch now refuses `E` while `PlayerRagdoll.Down`, which until now would
  happily board a vehicle from a proximity test taken at the spot the fall STARTED.
- `MotorcycleController.Teleport` - declared rather than inherited from `IEnterable`, for the reason
  `CarController` declares its own. The generic one stops the rigidbody; a bike also holds a steer
  angle on the front `WheelCollider` and a visual lean on `leanPivot`, so a bike that went down
  mid-corner was set upright by the transform write and leaned straight back over on the next render
  tick. `Respawn` is now this plus a spawn pose, which is all it ever was - and `FallGuard`'s bike
  rescue gets the same fix for free.

✅ **PLAYED AND USER-CONFIRMED, 2026-08-16** - *"U35a עובד טוב אתה יכול לסמן כן"*. The recovery reads
as built: crash → thrown → settle (≤4 s) → lie 0.9 s → the bike stands up under you and you are riding
again 0.35 s later. **The heading was the one thing flagged to watch** - the bike keeps the yaw the
crash began with, so a head-on into a wall stands you back up facing that wall - and it was not
raised, so it stays as built. One line in `PlayerRagdoll.Remount` if it ever reads badly.

### Everything else that is open, audited 2026-08-16, re-cut 2026-08-17

This list was re-derived from the whole ledger in one pass, so it is the census - if something is not
here or in **Deferred**, it is not open. **As of 2026-08-16 every unit on it is struck through: what
remains is Tier 7 and the two systems deliberately not ported.**

- ~~**U29 - the character roster.**~~ **DONE AND USER-CONFIRMED 2026-08-16** - *"looking good"*.
  Joe, Jody and David; the swap reaches the player and the stage dancer, and the character screen
  finally has the three-light rig the web build always had. Its own section is below.
- ~~**U19e - the officer.**~~ **DONE AND USER-CONFIRMED 2026-08-16** - *"U19e נבדק וגם גמור."*
  Arrest, standoff and seat all closed.
- ~~**U19d - the police blue-light run.**~~ **DONE AND USER-CONFIRMED 2026-08-16** - *"u19d התנהגות
  רדיפת השוטרים גם טוב."* It needed no correction; it had only ever needed driving. **With it there
  is no open unit below Tier 7 at all.**
- ~~**The jetski floats on the sea's MEAN level**, not on the swell.~~ **CLOSED BY THE USER,
  2026-08-16** - *"ה-jetski גם נראה טוב אתה יכול לסמן את זה כגמור."* Looked at on the water and
  judged fine, so the buoys' `SeaSurface` fix is deliberately NOT extended to the ski. The reason it
  reads right where the buoys read wrong is size: a buoy is a small floating object whose whole body
  the swell swallows, and the ski is a long hull with a rider on it that the eye reads as planing
  across chop rather than bobbing in it. This is a judgment, not a measurement - if it ever looks
  wrong at a different sea state, `SeaSurface.Height` is already built and it is a one-line move.
- ~~**The radio.**~~ ✂ **DROPPED BY THE USER, 2026-08-17** - *"רדיו - גם תוריד, לא כזה חשוב."* It
  had been ⏸ on hold since 2026-08-16 (*"בינתיים נכניס את הסעיף הזה על hold"*) and it is now closed:
  **not pending work, and not to be re-proposed.** The measured research below stays on the page
  because it cost a session to get and it is the honest answer to "why is there no radio", but
  nothing waits on it. **The idle half stays in the build and is NOT to be ripped out** - the `Radio`
  mixer group, `GameAudio.Bus.Radio` and `config.radio` cost nothing and removing them only makes a
  private retry harder. **Original entry, as history:** It was
  reopened, measured in the live Editor, and found **buildable**: Unity's own
  `GetAudioClip(streamAudio)` is proven dead on a live stream (FMOD needs a size a stream never
  has), and the answer is a pure-C# MP3 decoder into an `AudioClip` PCM callback with a local-clip
  fallback. **The full measured finding - including the User-Agent trap that makes SomaFM look
  offline - is in Deferred.** Held behind U30a and the video, not dropped. Deferred by the user
  inside U27 as the only system with a network dependency - recorded here because a deferral that
  lives in one unit's prose is a system that quietly vanishes from the port. Twelve of the web's
  thirteen audio modules shipped; this is the thirteenth.
- ~~**The dance's arrows are keyboard-only.**~~ **DROPPED BY THE USER, 2026-08-16** - *"חצי ריקוד -
  לא רלוונטי לדעתי יכול להוריד."* Not a regression (the original is keyboard-only too), and the port
  matches the game it is porting. **U31 was dropped the same day, so this has no trigger left at
  all** - it would come back only if a device build ever happens privately, where M2 is unplayable
  without it - it is ~20 lines of tappable lanes on the existing panel. Recorded as a
  decision rather than deleted, so it cannot be rediscovered as a bug.
- **Tier 8 is re-scoped again, 2026-08-17, by the user: ~~U35d - weather~~ is DROPPED** -
  *"הפיצר של המזג אוויר תוריד אותו הוא לא מעניין אותי כבר יותר. לא נממש אותו."* Nothing was ever
  built for it, so the cut costs no work and removes the largest un-measured frame risk left in
  Tier 8 (the row's own perf note named it). `U35d-pre` - the in-vehicle arrest, `33420c8` - keeps
  its name and is untouched; it was named for its slot, not for weather. ~~**Tier 8's remaining
  scheduled work is now U35e alone.**~~ **Overtaken 2026-08-17 - see the next bullet.**
- **TIER 8 IS CLOSED, 2026-08-17.** Four decisions in one message from the user: **U35c and
  U35d-pre-3 are user-confirmed**, **U35e is DROPPED** (*"we decieded we do not need that so do not
  mention it again"*), **U35d-pre needs no play-test of its own** (superseded by pre-3, which is a
  rewrite of its ramp - it is off the list, not out of the code), and **the GPS route is REMOVED from
  the game** (*"תוריד את הקו התכלת מהמפה, לא צריך את הפיצר הזה"*), taking U35c down to the helicopter
  alone. **What is left in Tier 8 is U35h in backlog and four confirmed units awaiting only U30b's
  frame measurement.** No Tier 8 unit is scheduled.
- **Tier 8 is re-scoped, 2026-08-16, by the user.** ~~Open: **U35c**, ~~**U35d**~~ (dropped a day
  later, see above), **U35e** (stunt jumps +
  Cinemachine - **skid marks cut**)~~ - **all resolved by the 2026-08-17 bullet above**, then backlog **U35g** (the garage, now with its own design) and
  **U35h** (breakables). ~~**U35f - side jobs**~~ is **DROPPED** - *"עבודות צד גם לא מעניין תוריד"*.
  It was already the weakest row on the "the web could not have done it" test and the ledger said so
  when it was written, so this cut agrees with the selection rule rather than fighting it. Recorded
  rather than deleted, so it cannot be rediscovered as pending work.
- **Tier 7 is untouched, and it is now the whole job**: **U30a** (the macOS build), **U30b** (the
  perf pass on the Player, which owns every entry in Deferred), **U30c** (ship hardening, after the
  video) and U32 (multiplayer, deferred by decision to last). ~~U31 (iOS/iPad)~~ is **dropped by the
  user, 2026-08-16** - see its row and the decisions log. **Nothing in Tiers 0-6 or Tier 8 is
  open** - that sentence has never been true before 2026-08-16.
- **The submission itself is now work, and it is not in any tier.** See the § below - the graded
  artifacts are a video, a repo, a kanban board and a zip, and only one of them is the game.

### Submission - 1 Oct 2026, and only one of its four artifacts is the game

**Added 2026-08-16, on the pivot** (see `CLAUDE.md` §1). Source: `Final project-From Idea To Reality
- App Using AI - 2026.pdf`. **A requirement recorded only in a conversation is a requirement that
vanishes - that is what this ledger exists to stop, so it lives here.**

| # | Required | State, 2026-08-16 |
| --- | --- | --- |
| 1 | Private GitHub repo, **instructor invited**, committed + pushed throughout | ⚠ repo is pushed and current; **the invite is unverified** |
| 2 | Trello / kanban board, used as discussed, **final screenshot** | ⚠ **must be brought up to date with the port** - see below |
| 3 | 5-min video: idea + one-liner | ✗ |
| 4 | …the list of major features implemented | ✗ |
| 5 | …**a diagram of the APIs / tools / libraries + the flow**, what is called when | ✗ - **and Unity's is a different diagram, not the web one relabelled** |
| 6 | …a recording of the whole project running, all features, good resolution | ✗ - needs U30a |
| 7 | Video on Google Drive, anyone-with-link | ✗ |
| 8 | Moodle: a PDF holding video link + repo link + kanban screenshot | ✗ |
| 9 | Moodle: source as `finalproject.zip` | ✅ **solved - 898 KB** |

**Measured, so nobody re-derives it:**

- **LFS is NOT a blocker.** 150 objects / **1.30 GiB**, and `git lfs push --dry-run origin main`
  reports **nothing missing** - it is all already on the remote. The "1 GiB free tier" worry that has
  been in `CLAUDE.md` since U0 never materialised for storage. **What remains is BANDWIDTH**: 1 GiB
  per month, per account, shared with `Finalproject`. One clone by the instructor pulls ~1.3 GiB and
  can exhaust the month for **both** repos.
- **The zip is solved and it is not the obvious answer.** A whole-tree zip is 1.66 GiB across 1,262
  tracked files - no Moodle takes that. But `Assets/Scripts` + `Assets/Editor` + `ProjectSettings` +
  `Packages` + `tools` + the two `.md` files is **898 KB / 427 files**, and that IS the source: the
  2.2 GB in `Assets/Models` is third-party art. Measured 2026-08-16.
- `theblock-unity` is 101 commits over 2026-08-12→16; `Finalproject` is 110 from 2026-04-17. **The
  user's call: invite the instructor to both.** The four-day history is not hidden, it is the second
  half of a pivot whose first half is in the other repo.

**The kanban board is a real task, not a formality.** Requirement #2 is graded on the board *showing
activity*, and right now the board reflects the three.js phase. The port's 34 units are the activity
of the last week and none of them are on it. It also has to be screenshotted at the end, so it is
work with a deadline attached to it twice.

**The diagram (#5) is its own piece of work and the web build's diagram will not do.** Unity's
inventory is URP · glTFast · Draco · Splines · AI Navigation · Input System · UI Toolkit ·
TextCore emoji `FontAsset` · AudioMixer · PlayerPrefs - plus the arrow this project should be
proudest of, which has no counterpart in the original: the **offline pipeline**, `config.ts` →
`export-config.mjs` → `theblock-config.json` → `WorldBuilder` → baked `ScriptableObject`s (traffic
graph, route graph, roof spots, NavMesh). Runtime casts no rays for any of it.

**Order, and the one sequencing trap:** ~~U30a (build) → U30b (perf baseline) → Tier 8 showcase
features~~ **REORDERED by the user 2026-08-16: the Tier 8 features first, THEN U30a → U30b (baseline
with all switches off, then per-feature deltas)** → record → U30c (strip the debug keys).
**As of 2026-08-17 the Tier 8 half of that is finished** - U35a/b/c/g are confirmed and U35d/e/f are
dropped - so what is left of this order is exactly **U30a → U30b → record → U30c**. ⚠ The same day's
second reorder - ~~"one batch play-test at the end of the five"~~ - was **reversed by the user within
the hour**: each sub-unit is played at its own boundary. Kept struck through rather than deleted,
because a plan that was live for an hour is a plan somebody can rediscover as current.
**Something to say in the video, and it is only true because the list is written down:** the
additions were chosen against a rule - "the web build could not have done it" - not collected. **U30c
is last on purpose**:
`P`, `T`, `C`, `debugStock` and Mission Select are how a five-minute recording reaches every feature
in the game. Strip them before recording and the video cannot be made.

### Testing the economy - `Continue`, never `New Game`

This cost a round of play-testing, so it is written down rather than re-learned. **`New Game` resets
four things**: `Progress`, the `Wallet`, `Payouts` and the power-up stock. A balance set by hand for
testing is wiped by it, and the symptom is "the money did not update".

Two debug knobs, and **both must be set outside Play** - a scene edit during Play reverts on Stop:

- `Police → Wallet → Starting Balance` and `Reset On Play`. ⚠ `Starting Balance` is **rewritten by
  every world build** from `powerUpConfig.items[0].price`, so a number typed there for testing is
  overwritten the next time `Build Store` or `Build World` runs. For a one-off, set the live wallet
  in Play instead.
- `Game → PowerUps → Debug Stock` - N of every item on Play, the port of the web's `?stock=`.

**Landed alongside U28, in a parallel session: U33, the day/night cycle** - the first addition to
this port rather than a port of something. It is `done` and user-confirmed. It changes nothing until
a player opens **Settings → Display → Time of Day**, because Fixed replays the scene as built and
schedules no post pass; see the Tier 8 row. **Its rig landed in `a269a6b` too and that debt is
CLOSED** - `DayNightCycle` is on the `Directional Light` in the committed scene. `The Block → Build
Day-Night` rebuilds it in one click if it is ever lost.

### How to reach any mission - Mission Select, not the debug field

`Campaign → CampaignRunner → Debug → Debug Start Mission` is at **−1**, the shipped setting, and it
no longer has to be moved to test a mission: **U26's title screen has Mission Select**, which does
the same job through the same entry path and also puts the player at the mission's start.
0 pizza · 1 dance · 2 heli · 3 jetski, locked above `Progress.UnlockedIndex`.

Play from **`Assets/Scenes/Boot.unity`** → bar → title → `Mission Select`. A row above the unlock is
dim and inert; `Continue` resumes at the furthest mission reached. On the profile that played these
through, `theblock.unlocked` is **3**, so every row is live.

### U19e, 2026-08-16 - the officer gets out of the car - DONE, user-confirmed

The user's own design, and it is the first thing in this port that is neither a port nor a
workaround-removal: *"the character I bought for free should be the one driving the police car, and
on the capture she gets out and chases us on foot."* Everything below is built, compiles and runs;
the first play-test found two faults and the session stopped there to commit.

**The mechanism, and why almost nothing new had to exist.**

- **She is one body in two places.** Seated, she is a child of the cruiser's `DriverAnchor` with her
  `NavMeshAgent`, capsule and `Animator` all switched off - a held pose on a skinned mesh costs
  nothing, and her Rigidbody is kinematic so the car carries her. Deployed, she is unparented at that
  same anchor and becomes an agent. No second prefab, no swap.
- **The anchor is why the exit needs no animation.** `DriverAnchor` is where the entry clip's ORIGIN
  goes - a person standing beside the door at road level - and the clip's own hip travel does the
  sitting (memory `driver-seat-is-clip-origin`). So sitting is `Joe_EnterCar` held at normalized time
  **1**, standing up is the same clip at **0**, and the transform never moves between them. Measured
  on the built prefab: hips at car-local **(−0.38, 0.79, 0.08)** against a cabin of x ∈ [−1.04, 1.04]
  - in the driver's seat, head at 1.48 m under a 1.67 m roof.
- **The cruiser had no seat until now.** `config.vehicle.driver.seats` is keyed by `modelUrl` and the
  web build has no cop car in it at all, so `PoliceCarBuilder` states one port-side, exactly as it
  states the rest of that car: the Avenger's `(−2.31, −0.84, −0.1)`, which is a saloon's door rather
  than a particular saloon's.
- **Two clips, both borrowed.** She is Humanoid with her own valid avatar, so Joe's clips retarget
  onto her for nothing - the same trick U16b used to put `Joe_Sprint` on Peter. `Joe_Sprint` is
  retimed 5.58 → 6.2 m/s by the builder, as `ThiefBuilder` retimes it for the thief.
- **The NavMesh is a pursuit surface here and only here.** U16b deleted agents from the crowd for
  good reasons that do not apply to three officers who have to corner a building after somebody
  running away. It is measured as patchy: the station has mesh 2.2 m away, but **the player's own
  spawn car park has none within 10 m**, so `CopOfficer.Walk` is a straight-line fallback with
  `CrowdGround` under it that takes the agent back the moment a polygon appears.
- **The split is on foot vs in a vehicle**, and it is not a preference: nobody on legs catches a car.
  On foot the officer is the arrest; driving, the cruiser is, exactly as U19 shipped. `OfficerChase`
  turns the whole thing off.

**The asset problem, and how it was taken out of LFS.** The free Asset Store officer is 459 MB of
2048² `.tga` and `*.tga` is LFS-tracked, against a free 1 GiB shared with the original repo - which
is why `.gitignore` has held `Assets/Police_officer/` since U19b. Nothing points at that folder now:
`sips` wrote 1024² PNG twins and the 2.3 MB FBX was copied out, so
**`Assets/Models/Characters/Officer/` is 24 MB and committed**. The 459 MB original stays ignored and
can be deleted whenever the user wants.

⚠ **THE ONE WORTH REMEMBERING: a culled Animator never writes the pose.** `SeatNow` poses her with
`Play("Sit", 0, 1f)` + `Update(0f)` and is first called from `FillPool`, where all three cruisers are
parked at the station **off-camera**. Under the FBX's default `CullUpdateTransforms` the state
machine runs and the bones are never written, so the sit silently did nothing: hips read car-local
**−2.30**, standing beside the door. `Rebind()`, a second `Update`, a non-zero delta - none of them
change it; `AlwaysAnimate` fixes it on the same call. It is asserted both on the prefab and in
`CopOfficer` because it is exactly the field a later hand-edit resets. **The offline check cannot
catch this**: `AnimationMode.SampleAnimationClip` in a preview scene writes the pose correctly, which
is how the seat was verified as right hours before it was found to be wrong in Play. Memory file
`culled-animator-skips-pose-write`.

**The seat's rider scale, 2026-08-16 - she sat 21.4 cm through the roof, and the check that
cleared her measured the wrong thing.**

The row above used to end *"the scale stays at 1 because the officer is 1.89 m native against Joe's
1.81 and a 4% difference in a seated pose is nothing to correct for."* That reasoning is wrong twice
over, and the play-test saw it immediately from outside the car.

- **Standing height is the wrong measurement for a seat.** What has to fit under a roof is the body
  ABOVE the hips, and hers is **26 cm** longer than Joe's. Sampled in the same seat: his head top
  lands at **1.627**, hers at **1.887**, against a cruiser roof at **1.673**.
- **The earlier "head at 1.48 m under a 1.67 m roof" was the head BONE.** The bone sits at the base
  of the skull; there is another 40 cm of head and cap above it. That is why the number looked like
  clearance and the eye disagreed - and it is the same shape of error as
  `skinned-bounds-ignore-thrown-bones`: the measurement was of a proxy, not of what draws.
- **No cabin in this game leaves the rider scale at 1**, which is the part that should have caught
  it without any of the above: the config gives the Mustang **0.95**, the Audi **0.97** and the
  Tesla **0.82**, and only the Avenger - a 2.17 m roof - seats a driver unscaled. Even Joe only
  clears the cruiser by 0.046 m at scale 1. A hand-stated seat with the scale left at its default
  was never going to fit.

**The fix is `PoliceCarBuilder.RiderScale = 0.833f`, and it is solved for rather than eyeballed.**
The target is the headroom the config already gives Joe - Mustang 0.096 m, Audi 0.103, Tesla 0.134 -
so `1.673 − 1.887 s = 0.10` gives `s = 0.833`. Measured at that value: head top **1.571**,
clearance **+0.101 m**, hips **0.661**, between the Tesla's 0.656 and the Mustang's 0.759.

It goes on the SEAT and not on her prefab **on purpose**: she is within 4 cm of Joe standing, so her
size on foot is right, and this is a cabin too small for her rather than a body too big for the
world. Scaling the prefab would shrink the officer who chases you on foot to fix a car she sits in.

**Then the scale moved her sideways, and THAT is the lesson of this row - a rider scale is not only
a height.** Rebuilt at 0.833 and looked at, she was under the roof and jammed against the driver's
door. The seat is the clip's ORIGIN, so the body reaches the cushion by *travelling* from a point
beside the car - and a uniform scale on the anchor shortens that trip on **every axis**, not the
vertical one people are thinking about when they set it.

| | scale 1 | × 0.833 |
| --- | --- | --- |
| trip from anchor to hips | `(+1.93, +0.79, −0.02)` | `(+1.608, +0.658, −0.017)` |
| hips, car-local x | **−0.38** - mid-way across the driver's half | **−0.702** - 0.34 m off a wall at −1.04 |
| hips, car-local y | 0.79 | **0.661**, measured |

**The vertical axis is what proves it.** 0.79 × 0.833 = 0.658 against 0.661 measured - the model is
right, so it is right on the lateral axis too, and nobody had to see the car a second time to know
by how much. The seat moves out by exactly what the scale took: `−0.38 − 0.833 × 1.93 = −1.988`,
which is `PoliceCarBuilder`'s `X` now. The anchor is still 1.99 m from the body centre against a
1.045 m half-width, so it clears the flank - and that matters past the pose, because
`CopOfficer.Deploy` stands her up at this same point when she gets out.

**Generalised: any rider scale in this project is also a lateral and longitudinal move**, for every
seat whose anchor is an animation origin rather than a cushion - which is all of them
(`driver-seat-is-clip-origin`). The config's own scales (Mustang 0.95, Audi 0.97, Tesla 0.82) carry
the same displacement, and they were authored with it, so nothing there needs revisiting; what does
is any scale changed by hand from here on.

*Both numbers landed by `The Block → Build Police Car`; the cruisers are `Instantiate`d from
`PoliceCar.prefab` at `Start`, so rebuilding the prefab was the whole fix and no `World.unity`
change was involved.*

**The three faults the play-tests found, all mine, all closed.**

1. **No bust when she reaches you - a plain logic inversion.** `PoliceSystem.FootArrest` called a
   helper that returns "is she still out" and returned early on it - which is true on every frame of
   a chase, so the grab test was on a path that never runs. The helper is now `Step`, it is called
   before the decision rather than as it, and the grab follows. User-confirmed - *"המעצר הרגלי עובד
   טוב."*
2. **She walked INTO the player instead of stopping beside them.** Her destination was the player's
   exact position, and **the player is invisible to the navigation system** - a `CharacterController`
   is neither an agent nor a carve, so nothing knows a body is standing there and that destination is
   an instruction to occupy them (`kinematic-bodies-ignore-static-colliders`, wearing a third face).
   `PoliceTuning.OfficerStandoff` = 1.1 m - her radius 0.32 plus the player's capsule is ~0.72 m, so
   1.1 m is shoulder to shoulder and the 1.6 m grab still fires. **Implemented as a pulled-back
   destination, NOT as `agent.stoppingDistance`**, because one mechanism has to serve both movement
   paths: `Walk` is a hand-rolled straight line with no agent in it, and the spawn car park - where a
   foot chase is most likely to start - has no NavMesh within 10 m. `stoppingDistance` is a QUARTER of
   the standoff, because the destination is already the standoff point and matching them would halt
   her at two standoffs, outside her own grab radius: the arrest silently never firing again, by a
   second route. The quarter is a dead band so she does not re-solve for a point she is standing on
   and shuffle. **The pull-back is clamped to her own distance** or, once she is nearer than 1.1 m,
   the destination lands behind her and she retreats every time you close in - a grab radius that
   pushes its own target out of itself. `FaceWhenClose` turns her to you inside 1.5 standoffs, because
   both paths aim her along her travel, which is right running and wrong on arrival.
3. **She sat 21.4 cm through the roof.** Two numbers, not one - the rider scale and then the seat's
   X. Its own block is above, and it is the unit's real lesson.

**The scene commit that was owed since U28b landed here**, on the user's call once fuel was confirmed
closed: `World.unity`'s 81 added lines are U28b's `GasStation`, `FuelSystem` and `FuelGauge` plus this
unit's one `officerPrefab` reference.

### U29, 2026-08-16 - the character roster - DONE, user-confirmed

*"looking good."* Three bodies, one swap, and the interesting part of the unit is how few places had
to learn about it.

**The fan-out is two, and the web build's is five.** `main.ts`'s `applyCharacter` is four calls plus a
dancer, and its comment says why: *"Four separate rigs wear the player's body - the walking capsule,
the seated car driver, and the bike + jetski riders - so picking one has to reach all of them or
you'd change clothes on getting into a vehicle."* Each of those is a separately built skinned mesh
there. **U9 already deleted that problem**: this port reparents ONE player into every seat, so all
four are the same body and the fan-out is the player plus the stage dancer. This is the standing
question answered by a unit that was built four months earlier - the dividend, not a new idea.

**The dance WAS the gap, and the user named it before a line was written.** `DanceBuilder`
instantiated `Joe.fbx` straight onto the stage at build time, so picking Jody would have left Joe up
there. That is not a hypothetical: `dancer.ts`'s own header records it as a bug the web build had and
fixed - *"picking the female character still put joe on stage"*. The stage wears a roster prefab now
and `DanceBuilder` lost its white-material rebind with it, because a Joe prefab is a Joe prefab
wherever it is instantiated.

**The gap nobody had named is a cache.** `VehicleEnterExit` takes `player.GetComponentsInChildren
<Renderer>()` once in `Bind` and hides the driver with it - correct for four units, and wrong the
moment a body can be replaced: swap while driving and the array holds the DEAD body's renderers, so
the new one is never hidden and the cabin that is supposed to look empty has somebody in it.
`CharacterBody.Swapped` is an event for exactly this, and it has three subscribers: that cache,
`PlayerAnimator`'s Animator, and `Dancer`'s. `PlayerAnimator` also re-pushes its mount flags on a
swap - a fresh Animator starts in its controller's entry state, so without it a character picked
mid-drive stands up in the driver's seat.

**`Player_Joe` was restructured, and it is the only invasive thing here.** He carried the Animator,
nine skinned meshes and the whole `mixamorig7:` skeleton on the same transform as his
`CharacterController`. A second body needs a height match, a height match is a scale, and a scale
there resizes the physics capsule - the same rule `NpcBuilder` follows and the reason the crowd and
the stage dancer were already built with a `Visual` child. He has one now.

**Heights are matched to JOE, not to 1.70 m.** `referenceCharacterId` is `'joe'` and the point of
that is that adding a roster changed nothing about how the character the game shipped with looks. So
Joe's own measurement is the target and his scale is 1 by construction rather than by luck: Joe
1.968 m × 1, Jody 1.899 m × 1.037, David 1.934 m × 1 (inside the 2% tolerance). The crowd's
`PeopleImporter` normalises to a constant instead, which is right for a pedestrian and would have
been wrong here.

**⚠ U16b's material claim is importer STATE, not a guarantee - and Joe's copy of it is gitignored.**
`PeopleImporter`'s doc says Mixamo FBX "come out of Unity's own importer as URP/Lit with base +
normal already bound". Jody and David came out with **7 and 6 slots holding a white URP/Lit material
and no `_BaseMap`**, their textures extracted right beside them. Joe looks right on this machine
because his `.meta` carries a hand-made remap - and `Joe.fbx.meta` is in `.gitignore`, so that fix is
a local patch no clone has ever had (memory: `gitignored-meta-hides-importer-fixes`). The builder
writes URP/Lit materials as assets now, which is code, and code survives a clone.

**⚠ And the texture↔slot pairing is Mixamo's SET NUMBER, not the names.** Jody's body material is
called `Ch38_body` while every one of her textures is `Ch37_*` - matching by prefix finds nothing.
What holds across all three characters is the number: `_body` takes 1001, `_hair` takes 1002, which
is exactly the table Joe's two hand-made materials already encoded. David has no 1002 at all, so his
hair falls back to 1001 rather than staying white.

**The character screen had no lighting, and this was the user's report.** *"חסר לי קצת יותר אור."*
`character-select.ts` adds three lights to its preview scene before it adds a body - a
`HemisphereLight(0xffffff, 0x333344, 2.2)`, a warm `DirectionalLight(0xffd7a8, 2.6)` at (2, 4, 3) and
a cool `DirectionalLight(0x88bbff, 1.4)` at (−3, 2, −2). U26 ported the camera and the turntable and
none of that, so the body was lit only by the world's sun, two kilometres overhead at whatever angle
the day/night cycle left it. **Both directionals become range-limited POINT lights**: a directional
has no position and one down here would light all 963 × 805 m of the city as a second sun, and URP
honours one main directional anyway (memory: `urp-has-one-main-directional`). A 6 m range cannot
reach anything but the body - the camera's "20 m far plane is the culling" trick, applied to light.
The hemisphere becomes a frontal fill, because ambient in URP is one global setting a preview may not
touch. Shadows off on all three: the preview camera does not render shadows, so they would only take
space in the 2048² atlas the world already overflows.

**The intensities were measured, because "brighter" is not a number.** Rendering the rig at 0×, 1×,
2×, 4×, 8× and 16× and averaging the luminance of the **body** pixels only - the background is most
of the frame and swamps a whole-image mean - gives **0.154** unlit (what U26 shipped), then 0.218,
0.262, 0.326, 0.411, 0.521. The brightest pixel reaches **1.000 at 4×**, so everything above that is
buying mean brightness by blowing the specular out. **2× is the last stop before the clip** - 0.959
peak, 70% brighter than the screen that was called too dark - and that is what shipped: key 24,
rim 14, fill 10. They are `[SerializeField]` on `CharacterPreview` and re-pushed onto the rig on every
open, so they can be dragged in the Inspector with the screen up; the builder's numbers must be kept
equal to the component's defaults or the component wins at runtime.

**⚠ What is committed and what is not, because the two new bodies are NOT like Joe.** `Jody.fbx` and
`David.fbx` are in the repo (52 + 50 MB, LFS), with their extracted textures, their materials and
their prefabs - so those two are complete on a fresh clone. **`Joe.fbx` is still gitignored** (a U2
decision, and `Joe_Jumping.fbx` / `Joe_Sprint.fbx` with it), so `Assets/Prefabs/Characters/Joe.prefab`
points at a mesh and an avatar that a clone does not have. That is not new - the scene's `Player_Joe`
has always pointed at the same missing file - but it is now written down. Un-ignoring the three is
+150 MB of LFS against a free 1 GiB shared with the original repo, and it is a decision for whoever
does U30's build pass, not a thing to slip into a roster unit.

**Two new menu items, and no build order to remember.** **The Block → Import Characters (slow)**
(~100 MB of FBX, Humanoid, textures extracted) and **→ Build Characters** (prefabs + the roster + all
three hosts). `Build Menus` and `Build Campaign` each call back into the second to dress what they
just rebuilt, so it does not matter which was run last.

**The roster table is hand-written, and it is the only ported table in this project that is.**
Everything else comes through `export-config.mjs` because it is full of hand-tuned numbers that must
not be re-typed. `characters.config.ts` is three ids, three names, and `scale`/`seat` nudges that are
unset for all three characters; the rest of the file is GLB URLs that mean nothing to Unity. There is
no number here to get wrong, and an eleventh exporter source would have been ceremony.

### U28 round 2, 2026-08-16 - what the play-test found

Three rounds, and the two faults are worth keeping because neither was in the logic.

**The emoji drew washed out, and it was not the font.** A colour-emoji glyph is an RGBA bitmap drawn
through `Hidden/TextCore/Sprite`, and UI Toolkit hands that shader the ELEMENT's colour as vertex
colour to multiply. Every surface that draws one sits in a deliberately-not-white label:
`MenuStyle.Muted` is alpha **0.55**, `Heading` **0.75**, `LockedInk` **0.5**, `SecondaryInk` is peach,
`AccentInk` is near-black. So the pictures were exactly as faded as the text around them - correct
for letters, wrong for pictures. **`Glyphs.cs` is back, doing the opposite of what it did**: where it
once deleted emoji for want of a font, it now wraps each pictographic RUN in `<color=#FFFFFFFF>`.
Rich text rather than a colour on the label, because it has to work on a MIXED string like
`1.  🍕 The Block Pizza Run` that splitting into two labels cannot. Still applied at the point of
DRAWING, so the data stays clean. The dance's ← ↓ ↑ → stay excluded - they are meant to take their
lane's colour.

**The HUD had no speed readout and no sprint bar**, and neither is new work the port invented -
they are the only two surfaces of `hud.ts` that nothing else happened to build, which is why
`PlayerController.StaminaFraction` has carried the comment "for the U25 HUD" since U6 with no reader.
`PlayerMeters` adds both. They are mutually exclusive by mode, as in the web build, and **the bar
takes the slot U28b's fuel gauge will share** - `hud.css`'s own comment says why: the fuel bar is
driving-only and this is on-foot-only, so "the bar above the radar is your meter" stays true in both
modes. `IEnterable` gained `SpeedKmh`, which all four vehicles already implemented.
⚠ The readout **also shows on foot**, which the web build does not do. That is the port's own call -
"a character with no meter at all" is what read as missing - and `showSpeedOnFoot` turns it off.

⚠ **Caught while measuring the bar: the fill rendered TRANSPARENT.** `_lastLow` started `false`, and
`low` is also `false` on a full bar, so the write-on-change never fired and the colour was left at
its default. Every such cache needs a value the first comparison cannot match; it is a `bool?` now.

**The opening balance is DERIVED, not typed.** `Wallet.startingBalance` is written by every world
build from `powerUpConfig.items[0].price` - **$40, the energy drink**. The web build opens at $0,
which makes the shop a place you cannot use until the first mission pays, and a shop you have never
been inside is a shop you do not know exists. One item's worth makes the 7-Eleven reachable on the
walk to the first job. It is deliberately the CHEAPEST item, so it buys exactly one thing and the
campaign still pays for everything after it. Derived rather than hardcoded because it is not really a
number, it is a relationship: "enough for one energy drink" survives a price change, a literal 40
quietly stops being true.

### U28b, 2026-08-16 - the tank

A tank means **range**, not session time: 50 L at 5.2 L/km is **9.6 km**, and you start on half of
it. At zero the car does not stop - the ceiling eases to a quarter over 1.5 s and wobbles ±15 % at
3 Hz, so an empty tank is a limp home. Refuelling is **free**, in the web build and here; the two
ways to lose money are still the shop and the bust.

**The line the whole unit hung on was one the store unit could never have found.**
`MotorcycleController` has carried a coast brake on `capped` since U10, with a measurement in its
comment: a 20 m/s cap holding at 22.6, because a `WheelCollider` has no rolling resistance and there
is no aero at this scale. `CarController` has never had it, and never needed it - **☕ only ever
RAISES a ceiling, so the car was only ever asked to accelerate INTO its cap, never to fall to a new
one.** Limp mode is the first thing in this project that collapses a ceiling under a car already at
the old one, and without that line the car coasts at 20 m/s with the motor cut and the limp is
invisible. It is the U19 lesson in a third currency: *when something gains a new direction of
travel, re-run the measurement the old direction was accepted on.*

**Measured in Play, all of it** (`FuelSystem → Debug Drain Scale` is what makes an 8-minute range
testable in one session):

| | measured | expected |
| --- | --- | --- |
| Burn at 20 m/s | 0.1140 L/s | 0.1140 |
| Burn per 100 m | 0.5698 L | 0.5700 |
| Limp ramp, cap | 0.99 → **0.25 at 1.5 s**, linear at 0.5/s | 1.5 s |
| Ordinary top speed, **after** the brake line | **19.99 m/s** | 20.00 - unchanged |
| Dry top speed, full throttle | **5.02 m/s** | 5.00 ± the sputter |
| Dry + ☕ | exactly **1.25×** the dry cap | multiplies, does not win |
| Cruiser ceiling while the player limps | 20.00 on all 3, **no tank on any** | untouched |
| Fill from empty | 10.017 s, 20 ticks | 10.0 s, 20 |
| Fuel bar vs sprint bar | never both `Flex`; bar at bottom 220 / left 34 / w 200 | the web's own numbers |

**The pumps are measured, not read - and that is the opposite of U28.** `seven-eleven-lot.glb` ships
purpose-built marker empties holding the config's own numbers, which is why the store binds nodes and
checks them at 0.0 cm. `gas-station.glb` ships **none**: 119 nodes, every one geometry. So the three
`gas pump*` meshes are render-mesh pivots, and the builder reports rather than trusts:
`(313.085, −122.881)`, `(321.019, −122.881)`, `(329.040, −122.881)` - **7.83 / 0.44 / 8.15 m** from
the station centre, each pivot within **0.24 m** of its own geometry. Matched by **prefix**, because
the `_7` / `_11` / `_15` suffixes are glTF node indices and renumber on any re-export.

**"Can Unity do this better?" - yes, and the shape of the answer is a union.** The web has one 9 m
circle at the station origin because three.js had nothing to ask about the model. The outer two pumps
sit at 7.8 and 8.2 m, i.e. **at the lip of that circle** - park 4 m off the pump line and you are
outside it with the nozzle in front of you. But per-pump circles *alone* would be **stricter** across
the forecourt's middle, so `AtPump` is the station circle **∪** three 6 m pump circles: a superset by
construction, machine-checked at **576/576** over the disc with the point past the rim refused.

**The first superset check reported 52/64 and the predicate was fine.** The samples were generated on
the rim as `centre + (cos θ, sin θ)·9` and came back at squared distance `81.000160` against a
threshold of `81` - `Mathf.Cos`/`Sin` rounding puts a point microscopically outside the very circle it
was built on. A boundary sample is a coin toss; the check now sweeps the area. **And the run that
"failed" printed no WARNING line at all**, which is why the report's header now carries the warning
COUNT: there was no way to tell a silent list from an empty one by reading the log.

**Found while wiring the gauge, and it is a live landmine for every future unit:**
**`The Block → Build Map HUD` deletes the entire U26 menu shell.** It does `Find("HUD")` →
`DestroyImmediate`, and `MenuBuilder` puts `TitleMenu`, `PauseMenu`, `ControlsGuide`, `SettingsPanel`,
`CharacterPanel`, `ShopMenu`, `MissionLaunch`, `ScreenFade` and `GameFlow` on that same object.
Recoverable with `Build Menus`, but nothing said so. Now documented on the class, and
**`Build Gas Station` installs `FuelGauge` itself** so the door never has to be opened.

Two other decisions worth keeping: the prompt folds `IsFull` into the shared predicate, which the web
does **not** - over there a brimmed tank at a pump still offers "Hold SPACE" for a key that does
nothing, and this project states three times that it will not have that. And the reminders are
per-tank rather than through `Onboarding.FirstTime`, which is once-ever and would spend both hints on
the first car of the first session.

### U28, 2026-08-16 - the money has somewhere to go

The game has earned money since U19 gave the bust something to take, and had nothing to spend it on.
This is the other half: a shop you walk into through automatic doors, a clerk behind the till, four
power-ups, and the HUD strip that shows what you are carrying. **Fuel is NOT here** - the user split
it out to `U28b` so the store could be one checkpoint. **U25's emoji font is here**, because the shop
is the most icon-dependent screen in the game and shipping it through `Glyphs.Strip` would have meant
four rows with no icons.

**The finding that shaped the whole unit: `seven-eleven-lot.glb` ships its own markers.** 95 nodes,
and the interesting ones - `se_door_trigger`, `se_cashier_stand`, `se_register`, ten `pu_slot_*`,
the two door leaves - each hold the same number `config.sevenEleven` holds, and glTFast already
converted the hierarchy on import. So `SevenEleven` binds transforms and the builder REPORTS the
node-versus-config delta instead of converting anything. That matters more here than anywhere else
in the project, because every one of these coordinates is MODEL-LOCAL - the handedness rule people
get wrong - and a silent mirror would put the counter on the wrong side of a symmetrical room, where
it looks fine. **Measured: 12 marker nodes, worst delta 0.0 cm.**

**The handedness chain is now confirmed by arithmetic AND by measurement, and they agree exactly.**
glTFast negates X on a node's local position and passes Y and Z through. Carried through the place's
own +90° yaw and its `Convert.Pos` origin, `se_cashier_stand` should land at `(−33.70, 0.23, −20.62)`
- computed by hand from the three.js side - and the clerk was placed at **`(−33.70, 0.23, −20.62)`**.
The clerk's facing is derived twice, from `se_register` and from `config.yaw + clerk.yaw`, and the
two **disagree by 0°**.

⚠ **`Convert.ModelOffset` is the WRONG conversion for a placed prop, and reaching for it is the
trap.** That rule (pass X, negate Z) governs offsets in a model whose facing has been corrected by
`Convert.ModelFacing`, which `BuildPlace` never applies. The sales floor is the only part of the
store with no node to read, so it is the only part converted by hand - and it is a min/max rectangle,
so **negating X swaps min and max**. `WorldBuilder.Store.FloorRect` re-sorts after converting.

⚠ **The door leaves swap sides on import, and the config's sign would drive them into each other.**
`SevenEleven_DoorL` is at glTF-local x −0.63 and arrives in Unity at **+0.63** - on the right. So the
parting direction is MEASURED from the two leaves' imported positions (slide each away from the door
centre) rather than taken from `config.door.slide.x`. Self-correcting under any handedness, and the
symptom it avoids - a door that shuts harder as you approach - would have read as a physics bug.
**Measured: gap 1.260 m closed → 3.540 m open, each leaf travelling 1.140 m, Z shift −0.090 m,
back to 1.262 m after the hold.**

⚠ **THE ONE THAT MATTERS MOST: the cop exclusion never latched, and only a measurement found it.**
☕ Nitro coffee multiplies the forward speed clamp, and every car in the game shares `CarController`
- cruisers included. So the boost was written with a `_isCop` flag cached in `Bind`, and the flag was
**false on every police car in the game**: `PoliceCar.prefab` carries no `CopDriver`, because
`PoliceSystem.FillPool` adds `CopCar` at runtime and its `[RequireComponent]` brings the driver with
it, *after* `CarController.Awake` has already run. Drinking coffee to escape a pursuit would have
made the pursuit 25% faster. The fix is `CarController.MarkAsPolice()`, called by `CopDriver` as it
binds, and the flag is **serialized** - a mid-Play recompile reloads the domain without re-running
anyone's `Awake` and would have handed the boost back to the police mid-session.
**Measured after the fix: 3 cruisers exempt at 20.00 m/s, player car at 25.00 = 20 × 1.25.**

⚠ **`Heat.Frozen` already had an owner, so 📱 got its own flag.** `CrimeWatch` assigns
`heat.Frozen = interior.Inside` on EVERY frame; a second writer would have been overwritten within
one frame, and the burner phone would have done nothing anywhere except inside the pizzeria.
`Heat.Immune` is 📱's line, and `Bump()` refuses on either. The clear is an EVENT at ignition and the
immunity is the state - doing the clear inside the per-frame push would make the 90 s window
unloseable.

**Measured in Play, so do not re-derive:**

- **The purchase arithmetic is the config's, exactly.** One of each = **$265** against a $700
  lifetime income, through the same `OnBuy` the shop rows call. Balance $700 → $435.
- **All four effects fire and all four clear.** ☕ boost 1 → 1.25 → 1; 🥤 `InfiniteStamina` true →
  false; 📱 cleared 2 stars, then `Bump()` while immune left stars at **0**; 🎒 armed, consumed once,
  refused the second time.
- **A mashed key eats nothing.** Re-activating a running item returns false with stock unchanged;
  activating an empty slot returns false with stock unchanged.
- **Stock persists, timers do not** - one `PlayerPrefs` int per id under `theblock.powerups.<id>`,
  and no clock is ever written.
- **The predicates hold.** `se_cashier_stand` and `se_register` are inside and at the counter;
  `se_door_trigger` and `se_entry_outside` are at the entrance and NOT inside (the threshold sits
  outside the floor rect, so the chime fires as you step through); 30 m away, neither.
- **The five dead sfx cues are alive.** StoreChime 1.100 s, Purchase 0.300 s, PowerUp 0.430 s,
  PowerDown 0.470 s, Deny 0.090 s - baked on first play, as U27 designed.
- 0 errors in the console across the whole session.

**U25 is closed: the emoji font works, and `GlyphRenderMode.COLOR` is why.** NotoColorEmoji (OFL,
10.6 MB) is a CBDT/CBLC **bitmap** font - its glyphs are little PNGs, not outlines, so every SDF mode
has nothing to trace and rasterises empty. COLOR asks FreeType for the bitmap and gives the atlas an
RGBA texture. **Measured: 11/11 probe glyphs present, 1024² RGBA32 atlas.** The chain is font file →
dynamic `FontAsset` → `HudTextSettings` (a `PanelTextSettings`, in BOTH `fallbackFontAssets` and
`emojiFallbackTextAssets`) → `HudPanelSettings.textSettings`. **Not `TMP_Settings`** - that is uGUI's
and setting it does nothing for UI Toolkit. `Glyphs.cs` is deleted and its three call sites draw the
copy as written.

⚠ **`Assets/StreamingAssets/theblock-config.json` had been hand-edited and nobody recorded it.**
Every source hash matched, yet the shipped snapshot had ASCII hyphens where `campaign.config.ts` has
em-dashes. The file is gitignored, so there is no history to blame. Regenerating restored the
faithful dump; the em-dashes now reach the HUD, and Unity's default font has U+2014.

**The exporter grew its ninth source.** `powerup.config.ts` is a sibling module, not a section of
`config.ts`, so it was simply not in the payload. One entry appended to `SOURCES` in
`scripts/export-config.mjs` - **the only file the original repo ever accepts a change to** - and
appended rather than slotted in, so every key already in the snapshot keeps its position and a re-run
diffs clean.

**The Wallet did NOT move.** `WorldBuilder.Police`'s comment invites U28 to take it off the Police
group, and it stays where it is: moving a component that owns persisted state, in a unit that already
touches a dozen files, buys tidiness and risks a save. Every consumer resolves it with
`FindAnyObjectByType`, so where it sits has never mattered.

**New menu item: The Block → Build Store.** A full Build World would re-instantiate nine districts,
the roads, the traffic graph and the NavMesh bake to change two components. The store is the only
thing in this unit that lives in the scene, so it got a door of its own. `Build World` still calls
the same two methods.

### U26, 2026-08-16 - the game has a shell - DONE, user-confirmed

Boot scene with a real loading bar → title screen over the frozen city → `Esc` → Settings, How to
Play, Character, Mission Select, Quit to Title. Seven of the web's `src/ui/` modules, plus U25's
interior fade. `Assets/Scripts/UI/Menus/`, `Assets/Scripts/Boot/`, `Core/Pause.cs`,
`Core/SessionReset.cs`, built into the scene by **The Block → Build Menus**.

**What Unity actually gave us, and one thing it charged for:**

- **The loading bar finally measures something.** `loading-screen.ts` opens with an apology:
  `THREE.DefaultLoadingManager` reports `itemsLoaded / itemsTotal` against a sequential loader, so
  the first file to finish reports 1/1 and the bar sits pinned at 100% for the whole remaining
  download - "the worst possible lie to tell someone waiting". Its answer is a hand-counted list of
  milestones kept in sync with `main.ts` by hand. `AsyncOperation.progress` is the number that file
  wanted. Held at `allowSceneActivation = false`, which parks the load at 0.9 - exactly the 99% cap
  the web imposed on itself.
- **The Play button is scar tissue and is gone.** `waitForPlay()` exists to harvest a user gesture
  so a browser will start an `AudioContext`; its own comment says so at the click handler. Unity has
  no autoplay policy.
- **The character preview is a second Camera into a RenderTexture.** `character-select.ts` spends
  four paragraphs justifying a second `WebGLRenderer` against the browser's LRU context eviction -
  "exactly the assumption that did not hold on an iPad". None of that exists here. The rig stands at
  **y = −2000** and the camera's 20 m far plane is the entire culling strategy: no preview layer, no
  TagManager edit, nothing a future scene inherits.
- **Mission Select reads the objective pin's own table.** `campaign-launch.ts` keeps a second,
  hand-written coordinate block beside `stepCoords`; they agree only because nothing has moved.
  `CampaignDirector.TryStepPosition` is the one dictionary, read twice.
- **⚠ AND THE PRICE: `Time.timeScale = 0` does not stop `Update`.** The web pauses by skipping its
  own `stepSim` call - one branch, and nothing downstream runs. Unity has no such choke point.
  Fourteen scripts poll `Keyboard.current` every frame and every one of them keeps firing behind an
  open menu: `E` gets into a car, `M` opens the map under the overlay, `F` retries a mission, `R`
  respawns the vehicle out from under you. `Core.Pause.Frozen` is the other half of a pause, and it
  is a guard line in each of the fourteen. `FixedUpdate` needs none - `timeScale` really does stop
  that - but `PlayerCarInput.Read` returns `CarInput.None` anyway, because a WheelCollider latches
  the last torque it was given and "in practice nothing calls it" is the wrong standard there.

**The dance is deliberately unpausable, and this is the U19 arithmetic again.** `Conductor` is
anchored to `AudioSettings.dspTime`, which `timeScale` cannot touch: pause a routine and the arrows
freeze while the song keeps playing, then resume against an anchor wrong by however long the menu
was up. `GameFlow.CanPause` refuses in `GameMode.Rhythm` - the web's own `canPause` clause, ported
verbatim - and `DanceMission`'s guard is written so the routine survives even if that rule is ever
broken. U27 paid 21.3 ms for a shift in that anchor; this would have been seconds.

**Audio pauses through `AudioListener.pause`, not a fifth mixer snapshot.** A snapshot means
re-running `AudioMixerBuilder`'s reflection tool over an asset U27 shipped and nobody has balanced
by ear yet. Unity already provides the exception a menu needs: `ignoreListenerPause` on the Sfx
voice pool, so the click of the button you press to un-pause still sounds.

**Three faults found by measurement rather than by looking, and all three were invisible:**

- **UI Toolkit hands `Color` to its shader as LINEAR.** This project renders in linear space, so
  every sRGB value in the palette was drawn as if already linear and came out lighter: a 0.90-alpha
  near-black scrim rendered as a pale blue-grey haze the city read straight through, and the
  `#ff9440` buttons rendered peach. Nothing logs. `MenuStyle.Ui()` converts, through the
  `UNITY_COLORSPACE_GAMMA` define rather than `QualitySettings.activeColorSpace` - that call is
  forbidden from a static field initialiser, which is where every colour here is built.
- **A percentage `max-width` against an indefinite parent collapses the element.** The CSS is
  `min(340px, 78vw)`; ported literally, the buttons measured **162 px** - their text width - because
  these columns are sized BY their children, so the percentage resolved to nothing and took the
  340 with it. Desktop-first: the viewport clamp is gone.
- **Hiding the gameplay HUD by `display` clobbered the Radar toggle, and this was the one the user
  caught.** Behind a menu, `GameFlow` takes the wanted stars, the cash and the radar off the shared
  document. The first version remembered each element's `display` and restored it on close - which
  looks careful and is exactly wrong, because `GameMap.SetMinimapVisible` writes `display` on that
  same element, so Settings → Radar changed it while the menu was up and the restore put the old
  value straight back. It hides with **`visibility`** now: a separate property no owner in this
  project writes, so there is nothing to remember and nothing to fight. Measured one frame after
  closing: `wanted-stars` and `cash` back to `Flex`, `map` still `None`, `GameMap.Hidden` true.

**`[RuntimeInitializeOnLoadMethod]` fires once per Play SESSION, not per scene load** - and until
Quit to Title there was no scene load, so the distinction had never mattered. Six statics reset
themselves that way and each comment is right about the trap it guards; none of them can fire again.
`Core.SessionReset` is the callable version, run by `BootLoader` before the load. Two of the six are
why it is not optional: `MapRegistry` ACCUMULATES, so a second pass through the world draws every
district twice over itself; and `SeaSurface` LATCHES - `_searched` goes true once, after which the
cached material is a destroyed object that still reads as non-null and nothing will look for the
new one.

**Deliberately not built:**

- **No Multiplayer button.** The web has one; U32 is deferred, and a dead control is worst on the
  screen a player presses first.
- **Mission Select teleports, it does not mount.** The web mounts the heli and the jetski because
  otherwise you are left swimming beside one. `VehicleEnterExit` has no public programmatic entry,
  and opening one reaches into U8/U23/U24 while all three await their play-test. Six metres of walk
  is not a cost.
- **Settings is one row.** Volume sliders are the obvious next tenant and U27 exposed seven mixer
  parameters for them - but a slider over an unbalanced mix hides the imbalance instead of
  reporting it.
- **The roster is one row (Joe).** The panel, the turntable and the persistence all ship; **U29 adds
  two `Entry` rows and two rigs under the turntable** and does not reopen this menu.
  **⚠ CLOSED at U29, and that last clause was wrong.** The menu was reopened, twice and for good
  reasons: the panel's hand-seeded roster became a read of the `CharacterRoster` component (two
  copies of one table is two tables that drift), and the turntable turned out to have **no lighting
  at all** - U26 ported this screen's camera and its body and left the web's three-light rig behind,
  which is what the user saw as "too dark". A prediction that a later unit will not touch a screen is
  a prediction about faults nobody has found yet.

**⚠ U25's emoji font is NOT done, and it is the only thing that unit still owes.** The fade shipped
here - `ScreenFade`, which COVERS rather than brackets, because both `Interior` call sites are
synchronous and `DeliveryMission` reads `interior.Inside` on the next line. `Glyphs.Strip` is still
in place, so Mission Select reads `1.  The Block Pizza Run` with the 🍕 gone and the map still draws
dots. Next attempt: Noto Color Emoji (OFL) as a fallback `FontAsset` on `HudPanelSettings`' theme,
falling back to monochrome Noto Emoji if TextCore will not rasterise CBDT/COLR.

### U23c / U24b, 2026-08-16 - the roofs are fine, the buoys were drowning, the thief was strolling

Three reports from the M3 play-test, and they needed three different answers. **Committed as
`6b38c6e`** by the U26 session, which found this work sitting uncommitted in the shared tree; the
design is this section's, the commit only carried it.

- **The survivors are on the roofs, and this one needed no fix - it needed a measurement.** All 46
  baked spots have a surface within 5 cm of their baked Y (0 exceptions), and the check that
  matters is the POSED mesh, not the renderer bounds: Survivor #1's baked sole sits **0.001 m**
  from the roof. The bounds say −0.15, which is the `skinned-bounds-ignore-thrown-bones` memory
  showing up as a false alarm - a skinned bound is conservative and never follows the pose. Verified
  in a rendered frame as well: shoes on the gravel.
- **The buoys were under water, and the cause was the sea getting better.** U12 gave the water three
  summed swells displaced in the VERTEX stage - up to **0.37 m of crest** - and nothing on the CPU
  ever knew: `SeaGeometry` answers `sea.Level`, which is the water's MEAN. The buoy was placed at
  the mean with a hand-rolled ±0.12 sine on top, so measured at gate 1 the water swings
  **−0.175 … +0.248 m** around a hull whose base sat at 0. Half of every wave went over it.
  `Assets/Scripts/World/SeaSurface.cs` is the CPU's copy of that displacement, **read off
  `Water.mat` itself** rather than from a second table of amplitudes - if the two ever disagree the
  shader is what the player sees, so the shader's own inputs are the source. The buoys ride it now
  and the fake sine is gone, because the swell IS the bob. Measured: 9/9 buoys sit at
  **|buoyY − surfaceY| = 0.0000 m**.
  ⚠ **The jetski still floats on the mean** (`level + floatY + its own bob`) and so does the thief's.
  Nobody has complained, but it is the same class of fault and `SeaSurface` is now sitting there.
- **The thief walked because he was on the crowd's tree, and every motion in it is a walk.**
  `Npc.controller`'s Gait is `Sophie_Walk` at 0.6 / 1.0 / 1.4 - a pedestrian has never had anywhere
  to be - and `ChaseThief` fed it `speed / baseSpeed`, so a man fleeing a chase strolled up the
  beach. He is on **`Joe_Sprint`** now, which retargets onto Peter for free because both are
  Humanoid: no import, no LFS. **The stride is matched to his ground speed rather than typed** -
  the clip carries **5.58 m/s** of root motion against his `run.baseSpeed` of 3, so the sprint
  child's `timeScale` is that ratio (**× 0.538 = 3.00 m/s**), computed in `ThiefBuilder` the same
  way the crowd's own tree retimes its walk. Playing it at 1.0 would have skated his feet by
  2.6 m/s. `Speed` is now normalised by his SLOW speed, so anything from 1.2 m/s up is a full run
  and only a genuine stop settles him to idle. Verified in Play: `Joe_Sprint` at weight 1.00 on a
  Humanoid avatar, and a rendered frame of him mid-stride.
  **`ThiefRun.controller` is rebuilt by Build Campaign every time**, so a retuned `baseSpeed` lands
  in the stride instead of drifting away from it.

⚠ **Two sessions edited this repo at once, and the tree is now clean.** The M3/M4 work above and
U26 were written into the same working tree; both are committed, M3/M4 first (`6b38c6e`) so that
history attributes it. `JetskiChase.cs` carries both sets of edits - the sea-surface gate heights
from here, one `Pause.Frozen` guard from U26 - and `World.unity` carries a Build Campaign run from
here plus a Build Menus run from U26.

**One thing genuinely collided and it went U26's way:** `Debug Start Mission` was left at **2** by
this section to reach the Huey, and U26 set it back to **−1**, which is the shipped value. That is
not a loss - Mission Select reaches the Huey through the same entry path, and the ledger's own rule
is that a scene left in a test setting is a trap. See the box at the top of RESUME HERE.

### U23b, 2026-08-16 - the Huey answers both key sets, and it cannot spin any more - `d485bae`

Two reports, one flight model, and the second one had a mechanism nobody had looked for.

- **Arrows work now, everywhere.** `W/S · A/D` were the only keys the helicopter and the **jetski**
  read, while the car, the bike and walking have taken arrows all along through the same two-key
  `Held(primary, alternate)`. Both craft use it now. The dance's arrows are unaffected - a mission
  with a dancer on screen has no vehicle under the player.
- **The endless spin is fixed, and its cause was an ownership gap rather than a bad number.** Yaw
  used to be composed with `MoveRotation` **only while the stick was off centre**, which left
  nothing at all in charge of rotation the rest of the time. So any yaw a contact imparted just
  stayed. Measured on a matching Rigidbody: a 3 rad/s knock still reads **1.82 rad/s ten seconds
  later** under PhysX's default 0.05 angular damping, and `MoveRotation` composed on top does not
  clear it - it adds to it. With `_planar` being pushed along a nose that is turning on its own,
  that is a spiral rather than a pirouette, which is exactly what the play-test saw.
  **Yaw is an angular VELOCITY now, written every step including the zero**, so a centred stick
  means *not turning* rather than *not asking*. An unflown craft bleeds any knock off at 6 rad/s²,
  because "parked" has to mean **still** - X and Z were already frozen, so this is yaw only.
- **Measured in Play, on the real Huey:** parked, a 3 rad/s knock is gone with **4.0° of yaw**
  travelled; flown with the stick centred, the same knock is **0.0000 rad/s on the next FixedUpdate
  and 0.00° of yaw**. At rest it sits at euler `(0, 0.1, 0)` with `v = 0, w = 0`, main rotor at
  world y **3.30** - upright, blades up, still.

⚠ **A scar from the same session, and the memory file it produced.** The first attempt to measure
the spin ran `Physics.Simulate` from an **edit-mode** probe. That steps the whole open scene, not
the probe - and in edit mode no `Awake` has run, so none of the runtime constraints exist. It left
the Huey rolled 90° onto its side and the jetski 261 m under the sea, and a `SaveScene` for an
unrelated setting wrote both to disk. The user reported it as "the helicopter is lying on the wrong
side", which is precisely what it looked like. Both were restored from the committed values
(`428, 0.1, −228` and `442, 0, −246`, rotations identity) and the scene diff is back to the one
intended line. **Measure physics in Play, where the controllers own their bodies.**

**U27 is DONE, user-confirmed 2026-08-16** (*"sound - mark it as done"*). The game has sound: engines,
ambient beds, 3D sirens, the run-over screams, every mission sting, the rotor. Its block is below and
what a future session needs from the top is only this - **an `AudioMixerGroup` costs one DSP buffer**,
which moved the dance's music 21.3 ms off its own beatmap until `Conductor.OutputLatency()`
compensated for it. Anything else that gets scored against `AudioSettings.dspTime` inherits that trap.

Everything is rebuilt and saved in `World.unity`: **Build Mission Vehicles**, **Build World**,
**Build Campaign**, then **Build Audio** and **Build Pedestrians** (U27 added the first and needed
the second - the scream pool is baked onto each prefab). **The save is wiped**: Play opens on
mission 1 with $0 and every mission pays again.

### What to play, in this order

The save was deliberately wiped, so Play starts a fresh campaign at mission 1 with $0.

| # | do this | expect |
| --- | --- | --- |
| 1 | Drive to the pizzeria (objective line points at it), `E` at the door, walk to the counter, **`T`** | Briefing card + Hazel's voiceover → out to the street → 5 customers with green pins |
| 2 | Ride to each, **`F`** within 6 m | A thank-you line, the beacon pops, `Deliveries n/5`, 4-minute clock. Done → +$80 and a handoff card |
| 3 | Go to the beach (Remy at Unity ≈ `414, −239`), **`T`** within 4.5 m | Instructions card → 3·2·1 → the song, arrows scrolling right-to-left into the ring. **← ↓ ↑ →** to hit |
| 4 | Win it (≥50% accuracy) | Result card, +$120, and the Huey unlocks |
| 5 | Walk to the Huey (≈ `428, −228`), `E`, then **`F`** | 4 survivors on rooftops, orange pins. `W/S` or `↑/↓` fly · `A/D` or `←/→` turn · `Space` up · `Shift` down. Descend within 10 m of each |
| 6 | All four → +$200. Then swim out to the jetski (≈ `442, −246`, past the shore wall), `E`, **`F`** | 9 buoys, the thief flees, gates tick up. He beaches; get off and walk within 2.5 m |
| 7 | Catch him | +$300, the win card with the total, campaign complete |

`F` retries any failed mission from anywhere. `M` opens the map. `R` respawns a vehicle.

### What to LISTEN for, on top of that

| when | expect |
| --- | --- |
| standing anywhere in the city | a street bed under everything; it crossfades to surf over the last 90 m before the shore |
| every 5-13 s outdoors | a honk or a dog in town, gulls on the sand - and nothing at all indoors or during the dance |
| any car / the bike / the jetski | an engine that pitches and swells with speed, and **no tick once a second** (that tick was 18 ms of decoder tail on the loop) |
| the Huey | a rotor that spins UP rather than fading in - the chop, the hum and the whine all move at different rates |
| a delivery, a survivor, a win, a fail | the web's own dings and stings, with the customers still talking over them |
| **the dance** | judge the timing hardest. The song sits on a mixer bus now and that cost 21.3 ms until it was compensated. If a Perfect feels early or late, this is the first thing to suspect |
| **a wanted star** | a siren you can hear coming from ~250 m away and *locate*. The biggest single change from the web build, and the one most likely to be too much rather than too little |
| running someone over | a scream from a male or female pool with a body thud under it, at most two voices at once |
| dismissing any card | a soft tick - the only UI click this port has until U26 |

⚠ **Nobody has heard the balance.** Every level is the web's own number, but the web mixed against a
browser's output, not Unity's. Report which BUS is wrong rather than which sound: `volMaster`,
`volMusic`, `volVoice`, `volSfx`, `volEngine`, `volAmbient` are exposed on `Assets/Audio/GameMixer`,
and one slider moving a whole family is the entire point of having built it.

**The corner minimap is back on** (user, 2026-08-16, reversing the same day's removal) at the web
build's own 200 px / 12 px inset, bottom-left. `M` still opens the full-world map over it. If the
radar is not on screen, the scene's `HUD → GameMap → Show Minimap` is the switch - the field is
serialized, so the scene wins over the C# default.

### U27, 2026-08-16 - the game has sound - DONE, user-confirmed

Twelve of the web's thirteen audio modules, ~1.2 MB of clips, and one mixer. The radio is deferred
by the user's call: five live SomaFM streams are the only system in the unit with a network
dependency, its own HUD panel, and failure modes nothing else here has.

**What Unity actually gave us, and it is not the same answer four times:**

- **The 20 synth cues are BAKED.** `sfx.ts` builds a fresh oscillator + gain graph on every single
  key press because Web Audio offers no way to keep the result. `SfxSynth` renders each cue's PCM
  once into an `AudioClip`; from then on it is a clip. Same envelope arithmetic, one allocation ever.
  ⚠ The oscillators carry **PolyBLEP** on saw and square, and that is not a flourish: Web Audio's
  `OscillatorNode` is band-limited *by specification*, so a naive 1046 Hz square here would fold
  every harmonic above Nyquist back as inharmonic grit and the cue would be recognisably harsher
  than the shipped game's - with nothing to trace it to.
- **The rotor is a literal port, through `OnAudioFilterRead`.** The obvious Unity route - bake one
  loop, drive `AudioSource.pitch` - provably cannot reproduce it: the three rates move by DIFFERENT
  factors as the throttle opens (chop 7→17 Hz = 2.43×, hum 0.7→1.2 = 1.71×, whine 0.6→1.3 = 2.17×),
  and one pitch knob collapses all three into one ratio. Unity's DSP callback *is* what Web Audio's
  graph was, so the arithmetic is the same arithmetic. Allocation-free: **0 B of managed heap over
  0.6 s of callbacks**, measured.
- **Sirens are 3D and on the cars.** `pursuit-audio.ts` fires ONE wail on the chase edge and its own
  comment says why - "a continuous loop was unbearable". It was unbearable *because* that build has
  **no `AudioListener` at all**, so every sound in it plays at a constant gain however far away it
  is. With a listener and rolloff the loop becomes the opposite: the thing that tells you the
  response is coming and from where. U19 gave the police a 15-60 s drive from the station and called
  it "a mechanic rather than a cost"; this is what makes that drive perceptible.
- **The mixer replaces six `AudioContext`s and a gain multiply at every call site.** Seven buses,
  seven exposed volume params, four snapshots. `ambientAudio.duck` is now the Ambient bus's volume in
  a snapshot rather than a number multiplied in by hand at each ambient call - so it also catches a
  one-shot already in the air, which the web's version cannot.
- **And the counter-example, which is the more useful half: the engine loops.** The web pins
  `source.loopEnd` to the original ogg duration because the WAVs carry a decoder overlap tail past
  it. Unity's `AudioSource` **has no `loopEnd`**. The answer was not a Unity feature - it was
  measuring the three files and trimming.

**⚠ THE ONE THAT MATTERS MOST: an `AudioMixerGroup` costs one DSP buffer, and it moved the dance
21.3 ms off its own beatmap.** U22 measured 0.02 ms of drift with `Conductor`'s source wired to the
default output. Re-running that identical measurement after U27 put the song on the Music bus read
**21.3 ms, dead stable** - and 21.3 ms is not noise, it is exactly `1024 / 48000`. The group is
processed a buffer behind the source, so what reaches the speakers is a buffer later than the
instant the beatmap was anchored to. **The clock was never wrong; the SOUND moved.** Against a 50 ms
Perfect window that is 43% of the window, biased the same way on every note - the kind of fault a
play-test reports as "the timing feels off" and nobody traces to a routing change. `Conductor.Play`
now moves its anchor by `OutputLatency()` (the buffer, read from `AudioSettings.GetConfiguration`,
and **0 when there is no group** - so a source on the bare output is untouched).
**Re-measured: mean |drift| 0.014 ms, worst 0.023 ms over 2 s, with a voice line and six cues
playing under the song.** That is U22's number back.

**Measured in Play, so do not re-derive:**

- **The loop seam was real on all three engines.** `car.wav` is 38912 samples / 0.882358 s against a
  `loopEndSec` of 0.864943 → **18.4 ms of tail**, trimmed to 38144 = `round(0.864943 × 44100)`
  exactly. Jetski 7.3 ms, motorcycle 8.9 ms. 18 ms on a 0.86 s loop is a tick *every cycle*, forever.
- **All 20 cues bake clean.** Every clip's length is exactly its authored `start + duration` plus the
  0.02 s tail; peaks 0.05-0.29, so nothing clips and nothing is silent.
- **Beachness is exactly the web's.** 0 at 90 m inland → 0.250 / 0.500 / 0.750 at 67.5 / 45 / 22.5 m
  → 1.000 at the shore and clamped seaward; the Z gate is full to 176 m and feathers to 0 by 220.
  It runs in the WEB's frame on purpose - the player's position is converted BACK
  (`Convert.Pos` is its own inverse) so an inequality against `shoreX = −430` cannot be silently
  mirrored.
- **The scream throttle behaves, and the web's own numbers are why.** Sixteen people downed in ONE
  frame - U18's own measurement - start **one** voice, not sixteen and not two, because `minGapSec`
  0.18 catches the burst before `maxConcurrent` does. Two overlap at a 0.213 s gap; a third while
  both are busy is refused; never the same clip twice in a row.
- **The siren cap picks the nearest.** Five sirens all wanting to sound → 3 sound, the 250 m and
  300 m ones denied. A 3D siren at 21 m reads L 0.173 / R 0.139 rms against a 0.163 reference with
  the source on top of the listener - i.e. essentially undiminished, which is what Linear rolloff
  with `minDistance` 12 predicts.
- 0 errors in the console across the whole session.

**Gender comes from `npcConfig`, by name.** `PeopleImporter.Names` and `npcConfig.people[].name` are
the same six, so `NpcBuilder` looks the person up and bakes the pool onto the prefab. Verified:
Sophie F, Remy M, Elizabeth F, Chinese M, Peter M, Lewis M - the config, exactly.

**Five cues are built and wired to nothing, on purpose:** fuel tick, fuel done, the store chime, the
till, the power-up/down pair and the deny tick. They are ~30 lines of note data belonging to U28's
economy, which has no call sites yet - so U28 does no audio work, and nothing dead is switched on
(an unplayed cue is never even baked).

**A sixth is built and can never fire, and that is the correct answer.** `SfxCue.Beat` is the
metronome, and the web plays it only when `conductor.isFallback()` is true - i.e. when the MP3 failed
to load and it is counting on the wall clock. This port has no fallback: a missing song is a build
error, not a runtime state. The cue exists; the condition does not.

**Three things a future session should not re-derive:**

- **Unity ships no public API for AUTHORING an AudioMixer.** `AudioMixerController`,
  `AudioMixerGroupController`, `AudioGroupParameterPath` and `ExposedAudioParameter` are all
  `internal` to `UnityEditor.dll`. `AudioMixerBuilder` drives them by reflection, every call probed
  against 6000.5.8f1 before it was written down. Two things were wrong on the first build and both
  are only visible to a human opening the window: `SetValueForVolume` moves the EDITING target to
  whatever it last wrote (so the mixer opened showing Rhythm, Ambient at −80 dB, which reads as a
  broken build), and a mixer built through the API has an **empty view list** - `GetCurrentViewGroupList()`
  threw, and Unity's own `SanitizeGroupViews()` does not repair an empty one, only a populated one.
- **`GameAudio.Instance` re-finds itself.** A script recompile while the editor is in Play triggers a
  domain reload: statics are wiped and `Awake` does not run again, so a plain static instance is null
  for the rest of the session and every cue in the game silently stops. That happened here mid-build
  - the entire mix went quiet with no error to explain it.
- **Measuring audio through the MCP bridge has two traps.** `AudioSource.GetOutputData` returns
  SILENCE for a source routed to a mixer group, so read `AudioListener.GetOutputData` instead; and
  that is **per channel**, so a source panned hard to one side reads zero on channel 0 and looks
  broken. Both cost real time here.

### Play-test round 1, 2026-08-16 - nine reports, nine causes, all fixed

Reported in one pass over the campaign. **Only the last one was a design call** - five were a frame
or a rotation being composed wrongly, two were a resource being shared or missing, one was a cursor.
Each is written with what it actually was, because in every case the symptom named a different
thing.

1. **No "E to enter" anywhere.** There was no prompt SOURCE, only mission prompts. `MissionHud`'s
   prompt line is now an **arbitrated, immediate-mode channel** - claim it every frame you want it,
   highest priority wins, `LateUpdate` draws and forgets. Priorities are the web's own `if/else`
   chain in `hud-driver.ts`: mission F/T (30) → vehicle E (20) → doorway E (10). `VehicleEnterExit`
   claims it from **the same predicate `E` tests**, sharing the stopped-car it already holds.
   ⚠ Consequence to know: a prompt that is not re-claimed every frame disappears. `SetPrompt(null)`
   is now a no-op, and every existing caller was already per-frame.
2. **The cashier.** She was built, placed and rendering - and **2 cm tall**. `pizza-interior.glb`'s
   root carries a scale of `(5, 0.025, 4)`, and `BuildCounterNpc` parented her to it. She hangs off
   the `Places` group now: measured 1.70 m, standing at `(−1000, 0, 996.4)`.
3. **The pizzeria door said nothing.** `Interior` claims both its own prompts now - "Press E to go
   inside" outside, "Press E to leave" on the mat. The exit line used to be drawn by
   `DeliveryMission`, which meant the way OUT of the room only existed while that mission was the
   one running.
4. **Remy's cheers stopped the music.** `Voice` and `Conductor` are both components on `Campaign`,
   `Conductor` is `[RequireComponent(typeof(AudioSource))]`, and `Voice` resolved its source with
   `TryGetComponent` - **one AudioSource, measured**. So every cheer's `Stop()` killed the song
   while the DSP clock counted on. `Voice` builds its own child source now. Verified in Play: song
   at `t = 7.62 s`, drift `0.0 ms`, with a line played through it.
5. **The white dancer.** `Joe.fbx`'s own materials are `Ch33_body` / `Ch33_hair` with no map. Its
   importer remap named `Ch33_1001_Diffuse` / `Ch33_1002_Diffuse` - **the names of the target
   materials, not of the FBX's slots**, so it matched nothing and did nothing, silently. The scene's
   `Player_Joe` had been bound by hand, which is why only the dancer was white. Remapped on the
   correct keys, so every future instantiation of Joe is textured.
6. **The Huey flew tail-first.** `MissionVehicleBuilder` composed `RotFromRadians(modelYaw) *
   Upright` and left out **`Convert.ModelFacing`**, which every other vehicle builder applies. A
   bounding box cannot see this: the craft was the right size and the right way up with its nose at
   −Z. Measured before: tail rotor `z +5.25`, cockpit `z −2.77`. After: `−5.25` and `+2.77`.
7. **The jetski lay on its face.** Not the spawn - `JetskiController`'s lean wrote
   `Euler(0, y, roll)` straight onto `Visual`, **throwing away the Sketchfab `Rx(−90)` on the first
   FixedUpdate**, driven or not. The lean is composed on top of a captured rest rotation now. The
   ski was ALSO backwards, by fault 6 (handlebars `z −1.10` → `+1.10`).

**One thing found on the way and fixed with them:** a locked Huey would have offered "Press E to
enter" for a key that refuses. `IEnterable.EntryRefusal` is the reason-or-null a vehicle gives, so
the prompt and the action come from one place - the helicopter's line is the web's own
("Win the dance to earn the keys"); the jetski's is written to match, because the web has none.

**9. Retrying the pizza shift restarted it in place.** The user's call: F should put you back at the
shop, because that is where a shift begins. It does now - into the pizzeria, Hazel's briefing again,
then `EnterRoutine`'s own `LeaveNow` steps you onto the pavement and the clock starts there. The
briefing repeating is deliberate: it makes the restart read as being handed the job again, and it is
skippable with the key that dismisses it the first time.

**The part that is easy to miss is the ride.** Teleporting the rider out of the saddle and leaving
the bike where the shift died restarts a 4-minute run across Florentin ON FOOT - a retry that is
worse than the loss. So whatever you were driving comes back with you and is parked 3 m in front of
the door, facing the way you will be facing when you step out (the ground there is flat at y 0.15
for at least 5 m, measured). Three small mechanisms carry it, each of which had no owner before:
`VehicleEnterExit.LeaveVehicleNow` (the E exit's two halves back to back, for a scripted dismount),
`Interior.EnterNow` (the twin of `LeaveNow`, on-foot only for the same reason the doorway is), and
`IEnterable.Teleport` with a default implementation - stop the body, move it, sync - which
`CarController` already overrides with its own wheel-aware version. `BustSequence` hand-rolls that
same default for the bike and could now use it; deliberately not touched.

**Measured in Play:** retry from the starting lot `(209, 0.25, −230)` put the player at the interior
spawn `(−1000, 0.30, 1002.8)` with `Inside` true and the room's palette applied (ambient 0.45, fog
far 26), status back to Inactive, briefing card open with `briefing-1` playing; the bike landed at
`(−25.00, 0.15, −100.00)` yaw 90 against the door at `(−28, 0, −100)`.

**8. Play opened on "Get to the jetski · chase the thief" instead of the pizza run.** Reported as
copy; it was the cursor. The save read `unlocked = 3`, `paid = pizza,dance,heli,jetski` - a
finished campaign - and U20's `CampaignRunner` **resumed the furthest mission reached**. Checked
against the original before changing it: `createCampaign` sets `idx = 0` on every load and
**nothing in the web reads `unlockedIndex` at all** - `?mission=` is the only thing that moves the
opening cursor. So the resume was invented here, and what it feels like is a finished save opening
on the finale's objective over a fresh $0 wallet with no way back. **Every Play is a New Game now**,
which is web parity; `Progress.UnlockedIndex` is still recorded on every cursor move because it is
what U26's Mission Select will read. The stale save was wiped with it (progress, payouts, cash -
the character and the seen hints kept), so the four missions pay again. Verified in Play: cursor 0,
`pizza`, objective **"Drive to the pizzeria"**.

**Left alone deliberately, worth a look while playing M4:** the jetski's rider seat comes from
`config.vehicle.jetski.rider.seat` at `y −0.31` against a hull centred on its origin, and its
`rider.scale` of 1.1 is not applied at all. Nobody has ridden it yet. If Joe sits inside the hull,
that is where to start. `JetskiController` also no longer adds a hull-half-height to the waterline:
that value was written into a non-serialized field at build time and was **0 at runtime**, so the
term never did anything - the origin IS the waterline, which is what the code now says.

### What I could not verify, and what to watch

- **The FEEL is still unplayed.** Round 1 answered the geometry questions and none of the others:
  whether the dance is fun, whether the Huey feels heavy now that it points the right way, whether
  four minutes is enough for five deliveries. Those are what round 2 is for.
- **The dance is the one to judge hardest.** Its clock is provably right (0.02 ms of drift) but the
  *feel* - note density, whether the ring reads at speed, whether 2.2 s of travel is enough warning -
  is untested and is exactly the kind of thing a rhythm game lives or dies on.
- **The heli's flight model has never been flown.** It is a Rigidbody with velocity written in; it
  has been proven to rest on a roof, not to be pleasant to land.
- ~~**U19d is still un-play-tested.**~~ **DONE AND USER-CONFIRMED 2026-08-16**, and it landed as its
  own commit `86502ac` after all - the worry recorded here, that a `git add -A` had swept its two
  files into `51e8037`, did not survive checking.
- **Arrows are keyboard only.** The user asked whether clicking works: it does not, in this port or
  the original. Four tappable lanes would be ~20 lines and matter for U31's iPad.

### Three things deliberately not built, so they are decisions and not oversights

- **The pizza-box stack on the counter.** Set dressing with no mechanic - the pizzas you carry are a
  HUD count and no version of this game picks a box up. The raw asset is 23 MB for a 30 cm prop (a
  14.7 MB normal map alone) and the shipped 417 KB copy needs Draco, which this project has no
  importer for.
- **The cashier is Elizabeth and the thief is Peter**, rather than the web's three dedicated Mixamo
  downloads (~155 MB). Both are already-imported crowd characters, and Peter is the one the delivery
  run does not use as a customer. Swapping either is a one-line change in its builder.
- **`GameMode.Transition`.** It exists in the web to freeze input behind a fade; the port has no fade
  yet and U25 owns it. A label nothing switches on is a dead branch.

### The rebuild order gained five steps

**Import Dance Clips** (once, then never again - it deletes its own sources), **Build Mission
Vehicles**, **Bake Roof Spots** (needs Build World to have run - it reads the placed city),
**Build Campaign** (it collects every mission and wires the lot), and now **Build Audio** - which
goes LAST, because it binds the `Voice` and `Conductor` that Build Campaign puts in the scene, and
running it before them leaves both on the default output. **Reset Campaign** is the New Game button
until U26 has a menu.

**Build Audio** is idempotent and safe to re-run: it creates the mixer only if it is missing (an
existing one is validated instead, because volumes are exactly the thing someone tunes by hand),
refills `AudioLibrary.asset` from every clip under `Assets/Audio`, and rebuilds the `GameAudio`
object and its five children. It marks the scene dirty and does not save it.

⚠ **Build Pedestrians must be re-run after any change to `npcConfig`'s genders** - the scream pool is
baked onto each `Ped_*.prefab`, so a prefab built before U27 screams male whoever it is.

---

### U19d, 2026-08-15 - "I want the police to arrive a bit faster" - DONE, user-confirmed 2026-08-16

**Play-tested and confirmed 2026-08-16** - *"u19d התנהגות רדיפת השוטרים גם טוב."* Written on
2026-08-15 and left un-driven for a day; it needed no correction when it finally was. **Tier 5's
whole police stack (U19 · U19b · U19c · U19d · U19e) is now closed.**

**What actually limited the response was neither of the obvious things.** Measured on the drive in:
the cop asked for its full 20.5 m/s and delivered **13.7** - so top speed was never the constraint,
`CornerSpeed` was. And worse, a single red-light queue cost one cruiser **12 seconds in one
junction**: six traffic cars around it all at 0.0 m/s, one of them yielding its entire shift and
still nose-to-nose with it.

Three changes, and the boundary between them is the point:

1. **A blue-light run.** Past `BandFar` with no line of sight a cop is not chasing anyone, it is
   answering a call - so it gets `ResponseSpeed` (29) and `ResponseGrip` (11) instead of the chase's
   20.5 and 6.5. **Neither applies once it can see you**, so the chase and the escape are exactly
   what the play-test already accepted, and corners are still where a pursuit is lost.
2. **A cop does not queue.** Blocked for `OvertakeAfter` (1.5 s) while asking to move, it swings its
   aim `OvertakeShift` (3.5 m) into the oncoming side for `OvertakeTime` (3 s), then tucks back and
   re-checks. Time-boxed rather than latched, so it cannot drive the city on the wrong side. This one
   applies **during a chase too** - the user's rule is *"cops do not listen to traffic lights, they
   just get to their target"*, and being stuck behind stopped traffic is the only way they ever did.
   It deliberately does **not** touch the final approach, where a swerve would wreck the pull-in.
3. **`copYieldShift` 2.0 → 3.0 m.** The old value was arithmetically too tight and the measurement
   proved it: cruiser half-width 1.05 + traffic car half-width 0.9 = 1.95, so a 2 m shift left
   **five centimetres**. Three metres leaves about a metre.

⚠ **`config.vehicle.maxSpeed` is 20 m/s and `ApplyDrive` cuts the torque there, for every car in the
game.** So `PoliceTuning.MaxSpeed`'s documented *"20.5 - a 2.5% edge over the player"* **was never
reachable**; both cars were pinned at exactly 20 the whole time. `CarController.SpeedLimitOverride`
is the seam that lets one car past that cap, and `CopDriver` is its only caller - set while
responding, cleared the instant there is line of sight or the car halts. Do not hand it to anything
the player drives.

**U19 is DONE, user-confirmed 2026-08-15** (*"mark police chase as done … maybe we will have minor
improvements in the future but for now its solid"*). Three rows closed together: U19 the pursuit,
U19b the fix that made it arrive, U19c the yield and the bust. The detail is below and in the rows;
what a future session needs from the top is only this:

- **Heat is a whole-star counter and the `engaged` latch is what makes a station response possible.**
  Nothing bleeds until a cop first reaches `SightRadius`. Do not "simplify" that back into a
  continuous meter without re-reading the U19b block - it was tried, and it deleted the pursuit.
- **Traffic yields to a pursuing cop rather than the cop shoving through**, because a `TrafficCar` is
  kinematic and therefore a wall. This is the mechanism to check first if cops ever stop arriving.
- **U20 inherits three hooks that already exist and are wired to nothing:** `Heat.SuppressCrash` (the
  web suppresses crash heat inside a mission and never run-over heat), `BustSequence.Busted` (the
  mission-failure edge), and `Wallet.Add` (payouts). None of them needs building.

**Carried forward, small, deliberately not done** - the user's "minor improvements in the future":

- The debug keys are still live: `P` adds a star (`CrimeWatch.debugStarKey`), alongside U17's `T`
  and U16's `C`. All three can go whenever someone is tidying.
- `Wallet.startingBalance` is **500** so there is something to lose before U20 pays for anything.
  The web opens at 0. `resetOnPlay` is off, so the balance persists.
- `PoliceProbe` was scoped in U19 and never written. The measurements in these blocks were taken
  through the MCP bridge instead, which is why they exist as prose rather than as a repeatable tool.
- A cop was once seen holding 94% on-road at full speed while its distance to the player GREW from
  241 m to 296 m. Never explained. It did not survive to the play-test, and the user's verdict is
  that the pursuit is solid - so it is a curiosity, not an open bug. If cops ever seem to wander,
  suspect the A\* route going the long way round a block and start at `RoutePlanner`.

### U19c, 2026-08-15 - the bust, the wallet, and why traffic was the wall

**The user's second report: "police cars are not getting to me because they were blocked by other
cars."** Correct, and the cause is structural: a `TrafficCar` is a **kinematic** Rigidbody
(`TrafficCar.cs`, `_body.isKinematic = true`), so to the cop's 1400 kg dynamic body it is not a car
to nudge past, it is a wall. The cruiser wedged, reversed, and tried again - which is the
`wedges=2, v=0.00` in U19b's own measurements, read at the time as an approach problem.

**The web build cannot hit this and its config says why:** its cops are kinematic character
controllers, so `police.config.ts` notes they "collide-and-slide … around stopped cars, which reads
as aggressive shoving". Shoving is free there and impossible here. **So traffic gets out of the way
instead**, which is the real-world behaviour and looks better than shoving anyway: a car inside a
pursuing cop's corridor eases 2 m outward onto the kerb side and caps at 6 m/s. It **never stops** -
a stopped car in the lane is the wall this exists to remove. The shift rides on the lane-offset term
the sampler already takes, so it is one added number rather than a second positioning path.

**Measured in Play** (isolated with `timeScale = 0.02`, because a static synthetic pursuer falls
behind a 12 m/s car between two MCP calls - the first attempt read 0 for exactly that reason):
detection at 12 m behind, ease-in **0 → 2.000 m** against a 2.00 target, speed **12.0 → 6.00** against
a 6.00 cap, and a clean ease-out when the pursuer is removed. Two cars in one corridor both yielded.

**Getting caught now has two outcomes, the user's call.** In a vehicle, you and it are impounded at
the station - you lose where you were, which in a city this size is the cost. On foot you are cuffed
where you stand: there is nothing to impound, and hauling a pedestrian across town has no mechanism
behind it. **Money goes either way**, and that needed a wallet, because there was none -
`FinesOwed` was a tally nothing ever spent. `Assets/Scripts/Game/Wallet.cs` is the port of
`game/wallet.ts` on `PlayerPrefs` (Unity's `localStorage`), floor-at-zero included. `Charge` returns
**what it actually took**, so a $100 fine against $40 costs $40 and the rest becomes debt on
`FinesOwed` - being broke is not a free pass. U28 still owns the economy.

**Measured:** on-foot bust moved the player **0.04 m** (gravity settling, nothing else), cash
**$500 → $400**, control returned, stars cleared, all cops sent home, 0 errors. **The in-vehicle
bust is NOT verified** - synthetic `E` would not take (memory `synthetic-play-test-decays`), so
nobody has watched a car get impounded.

`Wallet.startingBalance` is **500** and `resetOnPlay` is **off**, so the balance persists. The web
opens at 0 and its missions pay in; U20 can set it back once it does.

### Why the police never came, and it was not the plumbing - U19b, 2026-08-15

Two U19 decisions were individually defensible and jointly fatal.

1. Heat became a **continuous meter with unconditional decay**, deliberately deleting the web's
   `engaged` latch on the grounds that "three stars and nothing on screen, forever" must not be
   possible.
2. Every cruiser was then **moved to the station bays** (the user's own call, same day), so a
   response gained a real travel time of 15-60 s.

The arithmetic settles it without a screenshot: a run-over gave `1.05`, decay began 1.5 s later at
`0.030/s` ramping to `0.250/s`, and the star went out at `0.90` - **a star lifetime of about 6 s
against a drive of 15-60**. `Reconcile` then saw `wanted = 0` and teleported the car back to its bay.
**The cop could not arrive.** The top star of any level was worse: gains land exactly on the cap, so
the third star died ~4.8 s after the crime *with a cop on your bumper*.

**Heat is a counter again - one crime, one star, one car - and the `engaged` latch is back.** Nothing
bleeds until a cop has first reached `SightRadius`; `InboundGrace` (60 s, up from the web's 30
because our cops drive up to ~900 m rather than appearing at 70) bounds that so an unreachable player
still cools off.

**Measured in Play, so do not re-derive:**

- **The star now survives the drive.** Player 185 m from the station: cop deployed, held **1 star for
  the whole run**, `engaged` false until it crossed 70 m, **180.5 m → 62.9 m in 10.6 s** at 13.7 m/s,
  **88% on-road**. Under the old meter the star was gone at ~6 s.
- **Escalation is exactly 1:1.** Three crimes → **3 stars, 3 cars Chasing, 3 map blips**, on-graph
  97 / 94 / 78%.
- **The stand-down drives home.** Star shed → `Mode.Returning` → routes back → parks at
  **(164.00, 0.10, −111.00), yaw 0.0, v = 0.00**, blip removed. It does not teleport in front of you.
- 0 errors in the console across the whole session.

**A second bug fell out of the first and would have read the same way.** The hard give-up cap
(`GiveUpAt`, 45 s since the last crime) was also running during the drive-in: measured, a cruiser
127 m out lost the entire pursuit to it **while still 112 m away**. The cap now counts only while
`engaged` - "the cops eventually stop even if you never lose them" presupposes they reached you, and
the inbound phase already has `InboundGrace`.

**And a third, in the arrest that has never fired.** `ChooseAim` recomputed which flank to pull in
on every single step, from "which side is the cop already turning toward" - so the instant its nose
swung past you the sign flipped and the aim point jumped 6 m across to the other flank. That is a
limit cycle, and it was measured as one: a cruiser sat between **10.6 and 11.1 m** of a stationary
player and never reached the 4 m arrest radius. The flank is now latched for the duration of a final
approach. Alongside it, a dead band between `ArriveDistance` (8 m) and `BandNear` (12 m) left the
rubber band's own floor as the answer, so a cop 11 m out asked for **8 m/s** and overshot; there is
an arrival ramp now, one m/s per metre remaining. **Neither is confirmed - the arrest still has not
been seen, because the only spot it was tested from turned out to be inside the station building.**

### The white rays out of the Mustang - FIXED 2026-08-15, `gpuSkinning = false`

Reported as "קרניים לבנות מהמכונית". **It was never our code, and it is worth knowing why every
check missed it.** The renderer is `Object_11` on the Mustang (mesh `Object_4`, material
`Mustang_Light` - emissive `1.0, 0.887, 0.783`, which is the rays' colour). Every CPU-side reading
said the mesh was perfect: 772 vertices, UInt32 indices with **zero** out of range, one bone per
vertex at weight 1.0, 16 bones against 16 bindposes, and `bone.localToWorldMatrix * bindpose * v` -
the exact arithmetic the GPU runs - putting the farthest vertex **2.87 m** from the car. `BakeMesh`
agreed. `bounds` under `updateWhenOffscreen` reported **0.40 m** of height. Only the drawn pixels
disagreed, with blades about ten metres long.

So **`SkinWatchdog` could not have caught this**: there is no thrown bone and no thrown vertex to
find. Its threshold was separately wrong too and is fixed - `maxBoneStray = 15f` was a constant
**2.6× the Mustang's entire length**, so it is now `max(3 m, baked diagonal × 1)`, which is 6.6 m
for the car against a worst honest bone of 2.9 m, and 3 m for a pedestrian instead of 15.

Proven two ways before changing anything: baking the same mesh into `body_9`'s space and drawing it
as a plain MeshRenderer removed the rays, and then `PlayerSettings.gpuSkinning = false` removed them
outright - **337 white sky pixels → 4**, verified in a rendered frame, car intact.

⚠ **The trap that follows:** a `PlayerSettings` write made **while in Play mode reverts on Stop**,
and `SaveAssets` + `File → Save Project` both report success while writing nothing. The fix looked
applied, then silently was not. It is set with Play stopped now and `ProjectSettings.asset` reads
`gpuSkinning: 0` on disk. Both gotchas are memory files.

**If CPU skinning ever costs too much** (386 SkinnedMeshRenderers live, mostly crowd), the targeted
alternative is already scoped: `Object_11`, `Object_17`, `Object_18`, `Object_19` and `Object_22`
are **rigidly** bound - every vertex on one bone at weight 1.0 - so `CarBuilder` can emit them as
plain MeshRenderers parented to that bone. Visually identical, and it removes skinning work rather
than adding it.

**Do not re-derive these - they were measured today:**

- **The street graph is not connected, and it is repairable.** 97 nodes / 142 edges in **5
  components** `[6621, 2890, 1665, 1319, 265 m]`. Stitching nodes within 3 m of an edge INTERIOR
  (7 T-junctions) plus true crossings (8) gives **2 components: 12,494 m (97.9%) + one orphan**.
  The orphan is the 3-lane downtown avenue, 265 m, nearest neighbour 24.7 m - not joinable, and it
  is avoided rather than stitched. Verified twice, independently: a Python model of the same
  algorithm run against the baked asset, and the Unity bake's own report line.
- **The starting lot is 80.2 m from the nearest street** (the Mustang 77.2 m). That killed the
  first version of both the field spawn and the planner, which gave up at 60 m. `SnapRadius` is
  120 m for that reason and the number is not arbitrary.
- **The police station is in the big component, 21.4 m from it; the custody point is 2.9 m from a
  lane**, so a car put there can drive straight off.
- **`police_car.glb` imports LYING ON ITS NOSE.** Its `Sketchfab_model` node has an Rx(−90) with no
  cancelling twin - the Mustang and the gas station both have the pair. The first build produced a
  car 5.65 m TALL with a 1.36 m wheel radius. `Euler(-90, 0, 0)` fixes it, and the direction was
  measured (wheels at z 0.42 with the roof lights at 1.895 → +Z was up; front wheels at y −1.868 →
  −Y was forward), not guessed. Scale **0.8428** puts it at 2.09 × 1.67 × 5.65 m, all three axes
  agreeing with the web build's independent measurement.

**Three bugs found by measuring rather than by watching**, each of which would have read as "the
pursuit is just bad" in a play-test:

1. **Two route lists.** The planner filled `CopCar.Route`, the driver steered by `CopDriver._route`.
   Every cop had a perfect 49-point route and an empty cursor, which reads as "drive straight at the
   player" - all three drove into the car-park wall. One owner now.
2. **Cops field-spawned 5 m apart**, took the same route to the same person, shoved each other, and
   both retired themselves as wrecked within seconds. There is a `CopSeparation` of 30 m now.
3. **A plan always finished the span the car was on**, choosing the end the nose happened to face.
   One of this city's edges is 1,364 m long, so cops held a clean 100% on-road line while their
   distance to the player climbed from 81 m to 149. Both ends are costed now, with a 25 m U-turn
   penalty.

**The user's call, 2026-08-15: the cruisers PARK AT THE STATION and only a crime moves them.** No
field spawn while a cop has a bay of its own, whatever the distance - the web deploys from the
station only within 120 m and teleports a cop next to you otherwise, because its cops could not
reliably drive anywhere. Ours can: 97.9% of the city is one component and the station is inside it.
Verified in Play: three parked at `(164/156/160, 0.10, −111)` at 0.00 m/s with no stars, then **one**
star put **one** of them on the road and left the other two parked. The response now has a travel
time, which is a mechanic rather than a cost.

Two things that fell out of that and are fixed: a parked cop still runs its driver every step, and a
driver with no route aims at its target - which at startup is `Vector3.zero`, so all three quietly
drove out of the station before any crime existed (`Park` now holds the handbrake). And the distance
retire is gone: a cop starting at the station is legitimately hundreds of metres away while doing
exactly its job. **Being wedged no longer means wrecked either** - a cop that met the fence around
the car park retired itself as wrecked, was replaced, and the replacement met the same fence; now it
backs off, throws the route away and plans a fresh one.

**U17b is done, user-confirmed 2026-08-15** (*"עובד טוב"*). `E` resolves three ways in `main.ts`'s
own order - real vehicle, else the parked filler beside you, else the stopped street car, which
waits 5 s for you. All four cars are drivable, not just the Mustang. **It is the first unit since
U12 to come back from a play-test with nothing wrong**, and the reason is worth keeping: both swaps
were measured before it was ever played, so the things that usually surface at the checkpoint -
half a car of offset, a car facing backwards - could not have survived to it.

**Measured in Play, so do not re-derive:**

- **The carjack lands EXACTLY.** Body-centre delta **0.000 m**, visual rotation delta **0.00°**, and
  the same paint material asset carried across. The stolen car's sim slot went straight back to the
  pool (live 11 → 10, idle 29 → 30) and the sweep refilled it.
- **The lot promotion lands within 2.9 cm** and **0.00°**, bottom 0.100 → 0.100 m on a lot surface of
  0.10. The 2.9 cm is not error in the swap: it is the difference between a rotated car's AABB centre
  and the unrotated centre the prefab is pivoted on, for a body with a mirror on one side.
- **Seven cars resting flat**, 4/4 wheels grounded on every one, tilt 0.0°, zero velocity - including
  the three whose axles are STATED rather than measured.
- **0 errors, 0 warnings** beyond the shadow-atlas line U16 already flagged.
- The hold works: a car held at 7.2 m/s braked to a stop and drove on when the 5 s expired.

**The one number worth keeping: the Mustang's rig validates the stated-wheel rule.** It is the only
car that can be measured, and the rule the other three are built from gets it right -
radius **0.379 m measured against 0.387 stated**, wheelbase **±1.688 m against ±1.695**, track
**±0.992 m against ±0.953**. Track is the loosest at 4%, and that is the one to change if a car
feels tippy.

**⚠ Found on the way and fixed: the Mustang had been the wrong colour since U8.** `CarBuilder.Paint`
wrote `_BaseColor` and `_Color`, and glTFast's imported shader has neither - it has
`baseColorFactor`. So nothing was written, silently, and the car wore its model's native dark green
instead of the config's `0xb5232a` red for four units. **It is red now, and the user has seen it.**

**Not verified by anyone, and cheap to check if a car ever feels off:** the seated driver's pose in
the three new cars (the Tesla's seat block is its own re-fit for a 1.44 m cabin), and the door-swing
sign on the Audi and the Avenger - `config.ts` itself says *"tune sign in-game"* on both, and nobody
has. They passed the play-test, so they are not wrong; they were simply never the thing being looked
at.

**U18 is done, user-confirmed 2026-08-15** (*"הדריסה נראית טוב"*). Run people over above 12 km/h and
they are launched, land, lie, fade, and walk back onto their pavement.

**The one number to keep from it: the throw angle is +85.1°, measured off the clip's own root
motion, against the web build's hand-tuned −85.8°.** Same physical angle, opposite sign. That is the
handedness rule confirmed from a direction nothing else had tested, and it is derived at runtime by
`Pedestrian.ThrowYaw`, so it is not a constant anyone has to maintain.

**Measured in Play, so do not re-derive:** a 54 km/h pass downed **2 people**, thrown **2.14 m and
2.28 m**, both resting at y 0.00, both recovering and walking on; 16 knocked down in a single frame
cost **+7 materials and 0 MB of texture memory**; 0 errors. The clip is **1.13 s of a 4.83 s file**
(Mixamo pads a one-shot - the body stands still for 79 of 145 frames) and its own root reaches the
ground at frame 15 of 34, which is where `flightTime` 0.5 s comes from.

**Root motion is ON for this clip and nowhere else in the project**, and it is harvested off the
visual child onto the pedestrian's transform every LateUpdate, multiplied by that child's scale.
That multiply is not cosmetic - see the decisions log and memory `root-motion-on-a-scaled-child`.

**One fault found on the way, and it was not U18's mechanism:** `CrowdSpawner.Bind` destroyed
**every** child of the Crowd object, which silently deleted the stain pool `Blood` builds on that
same object. Pedestrians only now. **A component may only destroy what it made.**

**⚠ Open, deliberately deferred by the user: ~800 ms frame hitches, and they are NOT U18's.**
Measured max frame with nobody run over **818 ms**, across a full run-over **839 ms** - the run-over
adds noise, not cost. Same session showed green blocks tiled over the world with the Editor's own
toolbar corrupted alongside. Both are in Deferred with what has and has not been measured. The user
played again afterwards with no hitches at all, so it is intermittent. **Do not start a perf hunt by
suspecting the newest feature** - that was tested and came back clean.

**U17, U16b and the vehicle hardening are all done, user-confirmed 2026-08-15**, and everything is
committed and pushed (`origin/main` is a real remote now: `RoeeSivan/theblock-unity`). The
play-test found two faults; both are fixed and both were found by MEASURING, not by looking.

1. **The traffic lights never appeared to switch - the quads were inside the housing.** The
   mechanism was never broken: sampled live, the 70 controllers were cycling and the 233 poles held
   genuinely different materials (125 red / 79 green / 20 amber / 9 red+amber in one frame). What was
   wrong was 14 cm of geometry. `BuildLampMesh` placed each quad at `lampDisc.max.z + 0.3` **model**
   units, measured off the animated disc on the assumption it was the outermost thing at that height.
   It is not - the discs slide BEHIND a lens. Measured on the model: the shell's front face is at
   **9.675**, the disc fronts at **6.883-7.163**, so the shell stands **2.51-2.79 units proud of
   them**, and a 0.3 epsilon off that datum buried every quad in solid model. The Z now comes from
   the housing's own front face, shared by all three quads. Verified: 233/233 poles now sit 1.7 cm
   proud of the shell. **The generic lesson: an epsilon is only as good as its datum, and "the thing
   I am offsetting from" is worth measuring rather than assuming.**
2. **The black wedge was the car, exactly as this block predicted** - and the hardening that shipped
   with U16b was not enough on its own. `CarWheel` validated the pose's *quaternion* but not its
   *position*, so a perfectly valid unit rotation at a position nowhere near the car passed straight
   through and tore the skin. There is now a plausibility bound with a real derivation behind it: a
   `WheelCollider`'s pose is its own transform slid along the suspension axis, so it can never leave
   a sphere of `suspensionDistance` around the anchor. Anything further did not come from the spring.
   Measured live: wheels sit 0.126 m out against a 0.5 m limit - 4× headroom, no false trips.

**`SkinWatchdog` exists now so this class of bug is never a screenshot again**
(`Assets/Scripts/Core/SkinWatchdog.cs`, auto-installs on Play, editor-only). It names the renderer,
the offending bone and its distance, then pauses the editor on that frame. **It reads BONES, not
`renderer.bounds`** - and that is the whole point: a `SkinnedMeshRenderer`'s bounds are baked and do
NOT grow when a bone is thrown. Proved it by throwing a bone 500 m and watching the bounds report
5.65 m, unchanged. A bounds-based watchdog is not a weak test, it is a test that can never fire.

**CLOSED 2026-08-16 - the lamps were wound INSIDE-OUT, and had been since U17.** *"סוף סוף עובד."*
This was the deferred "standing next to a pole, its lights do not appear to change", and the final
symptom was the reverse of the report: **grey head-on, coloured from the side.** Three passes chased
the wrong quantity - the quads' Z was measured twice and their FACING never once. The proof is not an
argument, it is the asset: decoding `LampDiscs.asset`'s index and vertex buffers gave a front lens
with stored normal `(0,0,+1)` and a geometric normal of `(−0.33, −0.08, −0.94)`. Unity culls that,
so head-on you see the shell; from the side the far half of an inverted dome's rim happens to face
you, which is the colour that made this look like a grazing-angle problem. Details in the U17 row.

**The starting lot is quiet on purpose.** The original's 33 painted rectangles are downtown and
west; the Reichman lot gets only its 9-person district share. Drive into the city before judging
density.

**`T` toggles all traffic off and on in Play**, the same debug affordance `C` gives the crowd. Both
are debug-only and both can go once U17 is confirmed.

**Already measured, so do not re-derive:** 97 nodes / 142 streets / 70 lit / 230 crossings - the same
numbers U16 had, because it is now literally the same graph object. 12,759 m baked at 2 m samples,
233 poles. Sim cost **0.029 ms per physics step**, lights **0.012 ms per frame**. Over 3½ minutes:
13 live against a target of 13, no gridlock, nobody reaching the stuck escape, no car more than
0.25 m off its lane centreline, every car's Y inside the road band, 230/230 crossings gated.

**U16b, measured the same way:** 687 seeds baked (297 painted + 72 district + 318 strip) from 33
rectangles and 76 lanes over 7,082 m, plus 460 crossers built at Start = **1,147 people**. Peak
within the 90 m cull radius is **139**, p95 is **79**, so `liveCap` is 155. In Play: 0 exploded
skinned meshes, 0 on a carriageway, 0 on a rooftop, 230/230 gates live, all six faces present,
16/16 bound people actually walking. **Frame time with the crowd on 42.39 ms, off 42.31 ms - a
delta of 0.09 ms.** The crowd is free; whatever the frame costs, it is not this.

**If it needs another pass, the knobs are all serialized on `TrafficSystem`** (select
`World/Traffic` during Play): `densityScale`, `cullDistance`, the spawn ring, and every number from
`config.traffic`. Nothing needs a rebuild to try.

**Rebuild order:** The Block → **Import People (slow)** → **Build NPC Animator** → **Build
Pedestrians** → **Build Drivable Cars** → **Build Traffic Cars** → **Build World + NavMesh (slow)** →
**Bake Crowd Seeds**. Drivable Cars comes before Traffic Cars and the world for a U17b reason: it is
what fills the scene's `CarSpawner`, and both the carjack and the lot promotion look their prefabs up
in that list - a missing entry is not a missing parked car, it is a stolen car that cannot spawn.
**It marks the scene dirty and does not save it.**
The bake is last because it asks the NavMesh what is pavement; Import People is first and only ever
needs re-running if a character FBX changes (it is ~576 MB and several minutes, and the MCP bridge
drops while it runs). Plain **Build World** is the fast path and KEEPS the last
bake - it lifts the `Crossings` group, the carve volumes and the `NavMeshSurface` out of the old root
and re-attaches them, re-binding `NavMesh.asset` from disk (see the U17 decision: the component copy
alone silently dropped it), and it never sweeps `Assets/Navigation/Generated/`. Run the slow one
after anything that moves a district or a street. In practice "slow" is ~3 s at 0.4 m voxels; the
name is a warning that the bake is main-thread with no progress bar. **The traffic pass runs on both
paths** - nothing in it bakes, and the lights must come from the same graph the crossings did.

**U16's performance note is now U16b's answer** (user's call, 2026-08-15: *"flag this step as low
performance, we will try to make it better later"*, then 2026-08-15: *"return to the NPC's we had in
three js version, and same placement"*). Measured both times, and the measurement said the same
thing twice: **the crowd is not what costs.** U16 measured 0 delta with 60 agents; U16b measures
0.09 ms with 139. What stuttered at U16 was the spawn burst and the vendor's 33-SMR five-LOD rigs.
What is left, if the frame is still short, is elsewhere - start with the 18 shadow-casting punctual
lights the console complains about (`Reduced additional punctual light shadows resolution by 4 to
make 18 shadow maps fit in the 2048×2048 atlas`), which is the traffic lights and the headlights.
- The 111 build warnings (`Main Object Name … does not match filename`) are U15's compressed
  material clones keeping their source name inside a district-prefixed file. Cosmetic, from URP's
  material upgrader. One line in `WorldBuilder.Textures.cs` (`material.name = fileName`) silences
  them; not done yet.

**U7b is done** - swimming, user-confirmed 2026-08-15. It was **never a row in the 32**: the web
build has the state, the sequence forgot it, and the port would have shipped a sea that drowns you.
Filed under U7 because it is one more pose on that state machine. See *What U7b built* below for the
three things that were nearly wrong - the capsule-centre offset, the per-frame damping, and the
shore wall that has to block cars while letting a swimmer through.

**Worth a look while planning the rest:** the same "is it in config.ts but not in the 32?" question
has not been asked systematically. Swimming was found by accident, from a question about animations.

### What U17b built

| file | is |
| --- | --- |
| `Assets/Editor/CarBuilder.cs` | **The Block → Build Drivable Cars** - one prefab per distinct `modelUrl`, so 4 from 16 config entries |
| `Assets/Editor/VehicleMaterials.cs` | the clone / compressed-rebind / paint / sweep pass all three car builders share |
| `Assets/Scripts/Vehicle/CarPaint.cs` | the body-paint slots of a drivable car, so a theft keeps its colour |
| `Assets/Scripts/World/LotCar.cs` | a filler's identity, its paint, its drivable rotation, and the registry `E` searches |
| `TrafficSystem.NearestStopped / Hold / Claim` | the carjack API, and `Claimed` - what the drivable copy needs |
| `VehicleEnterExit.TryEnter` | the three-way precedence: real vehicle → parked filler → stopped street car |

**Every car prefab in the project now has the same origin: the body centre in XZ, the contact patch
in Y.** That is what makes a car swappable, and it is the whole trick behind both mechanisms. A
promoted or stolen car is placed at the pose of the thing it replaces with no ride-height arithmetic
and no frame conversion - which is why the hijack measured a 0.000 m delta rather than "close
enough". `TrafficCarBuilder` already did this; U17b gave `CarBuilder` the XZ half of it.

**The recycle is a retire, and that is a whole mechanism the port does not need.** `traffic-cars.ts`
spends `recycleMargin` + `recycleTries` teleporting a stolen car to a lane far enough away that you
never see it arrive, because its pool is a fixed set of InstancedMesh slots allocated at boot and a
car can never stop existing. Here `Claim` calls `Retire()` and the ordinary sweep - already running
twice a second, already placing cars 55-125 m out and preferentially outside the view cone - does
the rest. Those two config numbers are deliberately **not** declared in `TheBlockConfig`; declaring
them would imply a mechanism that is not there.

**Two facing conventions had to be reconciled, and both corrections are BAKED at build time rather
than computed at runtime.** A filler is turned by `lotCars.models[].modelYaw`, a drivable car by
`vehicle.cars[].modelYaw`, and a traffic car by `traffic.models[].modelYaw` - which is the opposite
convention to the other two. `LotCar.DriveRotation` and `TrafficCar.DriveRotation` each hold the
correction, resolved in the builder where both numbers are visible at once. The traffic one comes out
as the **identity for all three models**, and that is the point: it is identity *because* the two
conventions are exactly π apart everywhere, which is a fact that was derived rather than assumed and
is now enforced by construction if anyone re-tunes one of them.

**Wheels on the other three cars are STATED, not measured, and cannot ever be otherwise.** The web
build's `blender/merge-car-meshes.py` welds every wheel into the body to cut three.js draw calls, so
tesla/audi/avenger.glb contain **0 wheel nodes** - verified by reading the glTF node lists, not
inferred. Their axles come off the measured body box at 24% of height for the radius, 60% wheelbase,
80% track, the way `MotorcycleBuilder` states the bike's. Nothing visible depends on them: with no
wheel mesh there is nothing to spin, and the shipped web build never rotated a wheel on any car
anyway. The Mustang is the only rigged car in the game and it is the check on the rule - see the
numbers in RESUME HERE.

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
| `Assets/Editor/TrafficCarBuilder.cs` | **The Block → Build Traffic Cars** - 3 prefabs + paints |
| `Assets/Models/Props/traffic-light.glb` | the shipped 65 KB pole, transcoded (see the decision log) |

**The lamps are one renderer, not three.** The model animates coloured discs sliding behind a
translucent lens - unusable in three.js and no better here, plus the whole model is ONE `BLEND`
material, so 233 poles would have sat in the transparent queue. Those nodes are destroyed at build,
the housing is rebuilt as an opaque URP/Lit asset, and **six domed lenses - front and rear per lamp -
become one mesh with three submeshes**. Switching a light is an assignment into a shared-material
array, so every pole showing the same state still batches.

**Each lens takes its X and Y from its own lamp's box - that is what puts red above amber above
green - but its Z from the HOUSING's own face at that lamp's height.** Taking Z from the lamp box is
what shipped first and it made the whole system invisible: the discs sit behind the lens, so an
epsilon measured off a disc lands inside the shell.

**Winding is MEASURED at build, never reasoned about** (`EnforceWinding`), and the six lamp materials
are **two-sided** (`_Cull Off`). Both exist because the same class of fault shipped twice: U17's quads
and the first dome build were each wound against their own normals, and each time the comment above
them argued the case confidently and wrongly. Unity's front face is `Cross(b − a, c − a)`; a triangle
whose cross disagrees with its stored normal is now flipped at build and the warning names the lamp.
**The generic lesson, and it is the second one this feature taught: geometry that is invisible has a
POSITION and a FACING, and only one of them was ever measured.**

**`The Block → Rebuild Traffic Lamps` rewrites the lens mesh and the six materials alone**, in place
at the same paths, so all 233 poles pick the change up without a `Build World` - no re-placing, no
`navMeshData` trap. It deliberately does not sweep the generated folder.

**Handedness: the lane offset is `Cross(up, tangent)`, not the web's `(-tz, tx)`.** Those are the
same physical side written for opposite handednesses, and transcribing the arithmetic literally puts
every car in the oncoming lane. Cross-checked against the web build's own expression on the 3-lane
avenue: both land the inner lane at Unity x +5.30.

**Not ported, on purpose:** `carCount` is read but is not the pool size - it is one half of the
density the pool is sized from. (`config.traffic.hijack` was U17b's, and is now ported except for its
two recycle numbers - see *What U17b built* for why those two are absent by design.)

**U15 is done** - the user confirmed on 2026-08-15. The measurement its row demanded came back loud
and rejected Addressables: 13.5 GB of scene memory, 96% textures, because glTFast's .glb textures
are sub-assets no `TextureImporter` ever compresses. The unit became the compression pass instead
(see its row and the decisions log). **13,498 → 3,204 MB.** The pipeline is **The Block → Compress
Textures** once after any district .glb changes (~4 min, writes `Assets/Textures/Generated/`), then
every **Build World** rebinds automatically. Both are run; the scene is current.

**Two U12-era faults surfaced by that play-test are fixed and confirmed** (2026-08-15), neither
caused by U15 - both in the decisions log:

1. **`config.fog` was never ported**, so the 320 m far plane sliced the skyline. The world draws to
   1500 m with the config's haze rescaled onto it (328-1313 m, `#9FB8D4`); shadows 50 → 150 m.
   `Assets/Scripts/World/Atmosphere.cs` owns the far plane and the fog band **together**.
2. **The ground plate showed through the sea's wave troughs** - 0.37 m of swell against a plate at
   −0.05 m. `WorldBuilder.BuildGroundMesh` cuts the sea's rectangle out of the plate.

**What U16 built** (all of it re-runnable; the numbers below are from the build that is in the scene
now - **22 placed, 0 missing, 288 colliders**, 22 because Navigation reports itself as a placed item):

- `Assets/Editor/WorldBuilder.Navigation.cs` - the traffic graph (97 nodes / 142 streets, ported
  from `traffic-graph.ts`), **172 `Not Walkable` volumes** carving all 12.7 km of carriageway,
  **230 zebra crossings** on **70 lit intersections** (3 approaches dropped - street under 20 m),
  and the NavMesh bake: **963 × 805 m @ 0.25 m voxels**, districts only, from PhysicsColliders.
- `Assets/Scripts/Npc/` - `Crossing` + `CrossingRegistry` (the gate), `Pedestrian` (agent + manual
  kerb control), `CrowdSpawner` (pool of 40 following the player), `NpcAppearance` (face × shirt).
- `Assets/Editor/NpcAnimatorBuilder.cs` → `Npc.controller`; `Assets/Editor/NpcBuilder.cs` → 12
  `Assets/Prefabs/Npc/Ped_*.prefab`.
- `TheBlockConfig` gained `TrafficSpec` / `StreetSpec` (+ a `JsonConverter`, because the exporter
  emits a street as either a bare point array or an object with lane metadata) and `LightsSpec`.
- Scene: one `Crowd` root holding `CrowdSpawner` with all 12 prefabs.

**Two things the plan had wrong, corrected here so they are not re-derived:**

1. ~~"the vendor prefabs carry all 5 LODs as ~30 always-on SkinnedMeshRenderers with NO LODGroup"~~
   - **false.** Each character prefab has a real 5-level `LODGroup` (6 renderers per level, screen
   heights 0.7/0.4/0.2/0.05/0) and an Animator already bound to `npc_hmn_01mAvatar`. There was no
   perf problem to solve and no bone rebinding to do. `NpcBuilder` exists for a different reason -
   see (2) - and for adding the agent, the capsule and the appearance table.
2. ~~"the web build has no crosswalks"~~ - **false, and it was my claim, from grepping `crosswalk`
   when the code says `crossing`.** `traffic.ts:99-124` derives one zebra per approach of every lit
   intersection and `crowd.ts:43` walks two dedicated crossers over each. What the web build has
   NOT got is any connection between those crossings and the rest of the crowd.

**The real U16 gotcha:** the pack's 12 prefabs reference `npc_casual_set_00/Materials`, which is the
**built-in Standard** shader, while the URP twins sit unused in `MaterialsUPR` beside them - same 54
names, unrelated GUIDs. Dropped in as-is every pedestrian renders magenta. `NpcBuilder.RebindToUrp`
rebinds by name: **455 slots**. Memory: `asset-store-prefabs-ship-built-in-materials`.

**Known and deliberate:** rooftops bake walkable - the bake cannot tell a flat roof from a pavement,
and downtown is one mesh so there is nothing to mark. Both the spawner and the re-target reject
samples more than a storey off the current height. If anyone is ever seen on a roof, that band is
the thing to tighten, not the bake. **The car park is excluded outright** (`UnwalkableDistricts` in
`WorldBuilder.Navigation.cs`) - one open slab swallowed the whole spawn ring, and the web build never
seeded people there either.

**U14 is done** - the user confirmed on 2026-08-15 that the minimap and the `M` map read right.

**U13 is done** - the user confirmed on 2026-08-15 that the station, the lot and the interior all
read right. Current build: **21 placed, 0 missing, 288 colliders** - 21 because U15's atmosphere
pass reports itself as a placed item; it was 20 through U14.

**One thing carried forward, deliberately, into U21:** the interior *looks* right but its
**mission mechanics are not settled** - the user's words on accepting it. Nothing is broken; what is
missing is the shape the delivery mission wants from the room (where the counter hand-off happens,
what the exit pad means once you are carrying pizzas, whether stepping out should be the thing that
starts the shift). U21 owns that, and it is expected to change `Assets/Scripts/World/Interior.cs`
rather than build beside it. Do not treat the current doorway behaviour as settled design.

**U12 is done** - the user confirmed on 2026-08-15 that the roads, the water and the beach all read
right.

### What U14 built

**The base layer is a live camera, and that is this unit's answer to the standing question.** The
web build bakes the world top-down once at boot into a 2048² render target, reads it back into a
canvas and draws that image under everything - and skips the bake outright on touch, because the
cost is not the resolution, it is that rendering the whole world once compiles every shader and
uploads every texture in the same frame. Unity renders a second camera like any other, so
`Assets/Scripts/UI/MapCamera.cs` is an orthographic camera pointed straight down into a 1024²
RenderTexture: no readback, no boot spike, and the map shows the world as it *is* - parked cars,
and later U17's traffic and U19's police cars, moving on it. `config.map.bakeRes` and
`districtFill` are therefore not ported, and `TheBlockConfig.MapSpec` says why in place.

**Both states redraw at 12 fps, which is one step past the web build.** It caps only the collapsed
minimap and lets the open map repaint every frame so panning stays responsive; there is nothing to
pan here - the open map is fixed on the whole world - so the cap covers both, and the thing being
skipped is a full second camera pass over the city rather than a canvas repaint.

**The overlay is UI Toolkit, arriving eleven units before U25 said it would.** The map *is* UI, so
the choice could not be deferred: `MapView` paints district outlines, POI dots and the player arrow
in `generateVisualContent` with Painter2D - near enough a 1:1 port of the canvas code - and labels
are pooled `Label` children, because Painter2D draws shapes and has no text. The web build's greedy
first-come label guard ports exactly: districts claim their rectangle before POI names, and a label
that would overlap an earlier one is dropped rather than stacked. `HudBuilder.cs`
(**The Block → Build Map HUD**) creates the `PanelSettings` and theme asset a fresh URP project does
not have, plus the HUD and Map Camera objects; it is idempotent, like WorldBuilder.

**Orientation is verified, not assumed.** The map camera sits at `(90, 180, 0)`, which puts screen
right on world **−X** and screen down on world **+Z** - the web map's own frame, since its `+x` is
Unity's `−x`. So the sea is on the left in both, and the overlay's world→panel transform is written
against the camera's actual `transform.right`/`up` rather than a guess. Measured in Play: with the
player facing world `+Z`, the arrow draws tip-down.

**`MapRegistry` is the flexibility hook, and missions are its real customer.** It is the port of
`world/registry.ts` - static, so a district's outline outlives the meshes it was measured from
(U15's streaming needs exactly that), and cleared on entering Play rather than trusted to be empty.
`AddPoi`/`RemovePoi` by name is what U20's campaign director and U21's delivery will hang their
objective markers on.

**⚠ A `PlaceSpec` in `config.ts` has no `name`** - the pin's label is typed into `map-pois.ts`, not
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
Sketchfab export wraps the model in `Sketchfab_model` (Rx −90) → `GLTF_SceneRootNode` (Rx +90) - a
pair that cancels in three.js and does not survive glTFast, so the model arrived with its local Y and
Z swapped: 24.5 m "tall" (that was its depth), 13.1 m "deep", and its base 5.36 m below the road. It
now measures 27.6 × 13.1 × 24.5 m with its base on y 0. The fix is `Rx(-90)` in
`WorldBuilder.AssetAliases`, and the entry has **no `File`** - that is the new part: the table now
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
build's own coordinates and each car is converted at placement - converting `bounds` and `keepClear`
first would swap their X ends and invert every comparison in the loop.

**Paint is a generated material per model per colour, not a per-instance colour.** The web build
clones the body material white and drives the colour per instance because InstancedMesh has nowhere
else to put it; here it is a material asset (same call as U1's facade tint and U11's cutouts) and
that is also what KEEPS the instancing - a `MaterialPropertyBlock` would give every car its own draw
call. 18 materials for the whole lot. The paint slot is found by material name (`CarPrimaryColor`, or
`primary` on the Tesla), the same convention the web build matches on, and the colour goes into
glTFast's `baseColorFactor` as sRGB (memory: `gltfast-basecolorfactor-gamma`).

**⚠ `tesla.glb` and `avenger.glb` would not import at all: required WebP.** Both name
`EXT_texture_webp` in `extensionsRequired`, which glTFast cannot read, so Unity imported them as
`DefaultAsset`s and WorldBuilder could only say "missing" - the same trap U8 hit from the Blender
side and solved with `export_image_webp_fallback`. These have no source asset anywhere and cannot be
re-exported, so `tools/glb-webp-to-png.py` transcodes the embedded images (JPEG where there is no
alpha, PNG where there is), flattens the extension's texture indirection and drops it from
`extensionsRequired`. Geometry is untouched - Draco stays compressed. Run it once per file; the
result is what is committed.

**The car's box is measured off an UNROTATED probe.** Renderer bounds are world-space and
axis-aligned, so measuring a car already turned into its stall gives the bounding box of a bounding
box, which grows with the yaw. The probe is also where the ride height comes from: the car is placed
by its own underside against `lotCars.y`, not by the web build's "recentre the body and add half the
height", which assumes a centred pivot. And the `BoxCollider` divides the measurement back out by
the model scale - **the Avenger is scale 37.4**, so skipping that makes its collider a kilometre wide
and nothing can get into the lot at all.

**The interior is a real room a kilometre away, entered by teleport** - the web build's design, and
it carries: a second Unity scene would stop the street simulating the moment you walk in, which
U21's delivery timer and U19's wanted level both care about. `Assets/Scripts/World/Interior.cs` owns
the doorway; WorldBuilder writes its fields through `SerializedObject` at build time.

**Two of the web build's three interior chores turned out to be three.js tax.** Its room lights are
switched off while you are on the street because three's forward renderer charges every light against
every shaded fragment city-wide; URP culls per object, so three lamps a kilometre away cost nothing
and simply stay on. Its sun is dimmed on entry to keep daylight out of the room; the room has a
ceiling and URP shadows it. What is left is fog and ambient, which are global render settings in both
engines - so those are still saved on the way in and put back on the way out, and that swap is what
makes the inside feel like an inside.

**`E` is shared with getting into a vehicle, and the doorway defers.** A car parked outside the
storefront puts both in range at once, so `VehicleEnterExit.HasVehicleInReach` decides it rather than
Update order - the vehicle wins, which is the web build's precedence too.

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
is a curve - **1864 m of spline against 1859.5 m of raw polyline**, the 0.24% being exactly the
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
markings hold a constant pitch through a bend whatever the segment length - the thing the stretched
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
with a MeshCollider - the player walks DOWN it into the water rather than looking at a picture of a
beach.

**⚠ The ground plate's collider had to be trimmed at the shore, and this was not obvious.** The
plate is solid at y −0.05 while the beach ramps to −3, so an untrimmed plate holds the player up on
an invisible sheet a few centimetres under the water and the entire beach becomes scenery. The
visual plane keeps its full 1400 m - the water is opaque and drawn above it - but the solid part now
stops at the waterline, and seaward of that the beach mesh is the only floor. The web build does the
same thing in `physics.addGround` and the comment there is the only reason it was caught.

**The shore wall is on the `Ignore Raycast` layer**, which is Unity's answer to the web's
`markNonGround`. A wall is not a floor: a downward probe started inside it - the side probe on the
exit-a-vehicle path does exactly that - reads its top as ground and lifts the caller 8 m. That layer
is excluded from the default raycast mask, so probes miss it while collision is untouched.

**One source of truth for the waterline: `Assets/Scripts/World/SeaGeometry.cs`.** The sand mesh, the
water shader and the sand shader all key off the same ramp, and a mismatch is a tide line that does
not sit on the water. It also owns the handedness: `config.sea.shoreX` is −430 and the web's sea
runs to more negative x, so **in Unity the sea is EAST, at larger x**, and every derived edge here is
produced by converting the config's own expression rather than re-deriving it with a flipped sign.

**⚠ "Kerbs" were phantom scope.** The ledger said "roads, kerbs and the sea" for months; grepping
the original shows no kerb system exists at all - kerbs are baked into the district meshes and
appear only in comments. U12 is roads + sea. Nothing was skipped.

**⚠ `com.unity.splines` 2.8.x does not compile on Unity 6000.5** - `SplineInstantiate.cs` calls
`Object.GetInstanceID()`, which is obsolete-as-*error* there (`CS0619`, not a warning). **2.9.0 is
the minimum**; it guards the call behind `UNITY_6000_4_OR_NEWER`. And editing `manifest.json` by
hand did nothing: `packages-lock.json` keeps pinning the old version and a refresh never
re-resolves. Install through Package Manager. See memory `package-version-needs-package-manager`.

### What U11 built

**All 9 districts were already placed by U5's WorldBuilder** - the unit's real content was three
rendering faults, and the first one was not what it looked like.

**⚠ The white shards were never a blending problem.** They were the wrong CORNER of the atlas.
`assets_Foliage` is a 512² image whose leaves occupy only u [0, 0.25] × v [0, 0.25] - the
bottom-left sixteenth - and the rest is blank white. glTFast decides per TEXTURE whether the
imported image came out vertically flipped and compensates with a negative Y scale in the material's
`_ST`; on these districts that decision is wrong, and wrong INCONSISTENTLY: `FoliageTrees.001`
through `.004` all sample the same image through four different glTF texture entries and only `.001`
came out unflipped. The other three sampled v ∈ [0.75, 1] - pure white. `WorldBuilder.UnflipV`
takes the flip back out. See memory `gltfast-spurious-v-flip`.

The diagnosis in the old note - `alphaMode: BLEND` with ZWrite off - was real but was the *second*
fault, and fixing only it left the trees exactly as white as before. Alpha clipping went in anyway
and is what makes them read as leaves rather than as translucent smears: hard edges, depth written,
sorted with the opaque geometry, and a shadow with leaf-shaped holes, which a blended canopy cannot
cast at all.

**⚠ `_AlphaClip` on an imported glTFast material does nothing**, which is what the old note was
reaching for. The surface mode is baked at import from the glTF's `alphaMode`. So the alpha-clip
pass builds a separate URP/Lit material asset per district per material and rebinds the slot - the
same answer U1 reached for the facade tint, and the imported material is only ever read.

**⚠ A pattern list matched by substring will surprise you: "tree" is inside "CityGen_S`tree`ts".**
The first build alpha-clipped every district's road surface. The guard is not a better pattern, it
is asking the right question FIRST - `IsBlended()`, because an alpha cutout only ever fixes
something that is blended to begin with, and the name match then only has to choose among those.

**Cities 2 and 3 got a submesh split, in Unity, not in Blender.** Their parked cars are merged into
the same 300k-vertex mesh as the streets and buildings, so `hideMaterials` could not disable the
renderer without taking the district with it. WorldBuilder now rebuilds the mesh without those
submeshes. **The cars were 86% of the geometry** - 186,186 of city 2's 216,515 triangles - so the
surviving vertices are compacted rather than left in place, taking the mesh from 304,797 vertices to
39,121 and the asset to 5.8 MB. They leave collision with the geometry, which matters: an invisible
but solid parked car is exactly what U17's traffic would pile into.

**Empty material slots were rendering magenta.** A glTF primitive that names no material leaves the
Unity slot null and Unity draws the error shader - the small pink rectangles on the pavement in
every procedural district. They now get the glTF spec's default material: white, metallic, rough.
Deliberately drab; inventing a look for it would hide the fact that the asset says nothing there.

**The generated folders are swept every build, and are gitignored.** `Assets/Materials/City/Cutout/`
and `Assets/Meshes/Generated/` are output, so anything in them this build did not write is deleted -
otherwise a corrected pattern list leaves a plausible-looking `.mat` behind that nothing references.
That is how the six stale `CityGen_Streets` cutouts got cleaned up rather than lingering. Both
folders derive entirely from the gitignored district GLBs, so a fresh clone rebuilds them along with
everything else under `World`.

**Foliage still collides - left open on purpose, low priority.** See "Deferred" below.

**MSAA is off** (`PC_RPAsset`, `antiAliasing = 0`), so the `_AlphaToMask` the cutout materials carry
is inert. Turning MSAA on would soften the leaf edges via alpha-to-coverage - a real improvement,
and a global render-quality change with a cost, so it belongs to U30's perf pass and not here.

### U10 tuning knobs, if the bike ever needs re-feeling

**All serialized on `MotorcycleController`** - select the spawned `Motorcycle`
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

Suspension, tyre grip, mass, wheel radius and the chassis box are NOT here - they are baked into the
prefab by `MotorcycleBuilder` and live as constants at the top of `Assets/Editor/MotorcycleBuilder.cs`.
Change them there and re-run **The Block → Build Motorcycle**, which rebuilds the prefab in place so
the scene keeps its reference.

Controls while riding: `W`/`S` throttle and brake-then-reverse, `A`/`D` steer, `Space` rear-brake
skid, `R` back to the spawn. `E` gets on and off - the bike sits 8 m west of the Mustang on the lot,
and `E` picks whichever is nearer.

### What U10 built

**U10 is done** - the user confirmed on 2026-08-15 that riding the motorcycle feels right.

**The bike is a Rigidbody on two WheelColliders, not a port of `motorcycle.ts`.** That file is
kinematic - scalar speed and heading through a Rapier character controller with a ray snapping it to
the road - for exactly the reason the car was, and U8 already ruled that scar tissue. What the swap
buys, none of which the web build has: it collides with the world and the cars instead of sliding
through them, it has suspension so a kerb is a bump rather than a teleport, it keeps its momentum
(U18's run-over and U19's ramming inherit that), and **it leans**.

| file | is |
| --- | --- |
| `Assets/Scripts/Vehicle/MotorcycleController.cs` | drive, steer, the upright stabiliser, the lean |
| `Assets/Scripts/Vehicle/MotorcycleSpawner.cs` | one-shot spawn + ground probe, on the `Vehicles` root |
| `Assets/Editor/MotorcycleBuilder.cs` | **The Block → Build Motorcycle** - generates the prefab |
| `Assets/Scripts/Vehicle/IEnterable.cs` | + `UsesEntryAnimation`, `ShowRiderOnQuickMount` |
| `Assets/Models/Characters/Joe_Driving.fbx` | the seated riding pose, imported as `Joe_Ride` |

**A two-wheeled Rigidbody has no roll stability and falls over on frame one.** `Stabilize()` is what
holds it up, and it runs whether or not anyone is riding - a parked bike has to stand there too, and
this model has no kickstand. The torque is a spring toward world up measured against where the roll
will BE in `uprightPredict` seconds rather than where it is now; that look-ahead IS the damping term.
Correcting only the current error makes a pendulum and the bike wobbles forever. As shipped it is
about 3.6× over-damped, which is the safe side of the choice - it will feel firmly held rather than
floaty, and `uprightPredict` is the knob if that reads as stiff.

**The lean is on a separate `Lean` node, and the Rigidbody never rolls.** Rolling a two-wheeler's
body is not a lean, it is a fall. The pivot sits between the prefab root and `Visual`, and the rider
anchor hangs off it too so Joe leans with the bike instead of staying bolt upright on top of it. The
target angle is read off the physics - `tan(lean) = v·ω / g`, the angle at which gravity and the
corner's centripetal force line up - not off the steering key, so a stationary bike does not lean and
a bike sliding sideways out of a `Space` skid still does.

**Wheel geometry is stated, not measured, and that is a property of this asset.** The Mustang's rig
names its own corners; `pizza_delivery_bike_wolt.glb` is two nodes - `Bike` and `WoltBox` - each one
merged mesh with no wheel to find. So the radius is `WheelRadiusFraction` (0.22 of body height →
0.268 m) and the axles go one radius in from each end of the bounding box, which is a fact about
bikes rather than a guess about this model. Nothing visible depends on it: there is no wheel mesh to
spin either, so `CarWheel` has no counterpart here and the shipped web build never rotated one.

**The chassis box's WIDTH is overridden.** Measured bounds are 1.037 m across because they span the
mirrors and the bars; colliding as a metre-wide brick makes the bike handle like a car in traffic.
`ChassisWidth` forces 0.5 m - bike plus rider. Length and height stay measured.

**The rider seat block IS a seat, unlike the car's.** `{x: 0.01, y: -0.49, z: 0.23}` is measured from
the body centre and lands at prefab-local **(0.01, 0.238, -0.23)** once the centre is added back -
the same correction `CarBuilder` applies, and the arithmetic reproduces the web build's rider height
exactly (`surface + 0.728 − 0.49`), which is the cross-check that the centre-add-back is right.
`Convert.ModelOffset` for the offset, `Convert.RotFromRadians` for the yaw with **no** extra π: the
web build adds one to turn a Mixamo body that faces `+Z` in a `-Z`-forward engine, and Unity's
forward already is `+Z`.

**`Nearest()` now walks `EnterableRegistry`, and vehicles register THEMSELVES.** Registration moved
out of the spawners in `OnEnable`/`OnDisable`, because a spawner cannot know when its vehicle is
destroyed and a stale entry means `E` aims at a corpse. `EnterableRegistry.All` also sweeps dead
entries on the way out - a destroyed MonoBehaviour reached through an *interface* reference does not
compare equal to null, since the overloaded operator lives on `Object` and an interface does not
carry it, so the sweep goes through the concrete type to ask the question at all.

**⚠ `VehicleEnterExit.activeVehicle` was `[SerializeField]` on an interface type, which Unity cannot
serialize.** It silently stored nothing, so the whole mid-Play-recompile guard that field exists for
was doing nothing for the one piece of state the scene cannot rebuild. It is now stored as a
`MonoBehaviour` and cast back through a property.

**No third enter path was added** - the two flags on `IEnterable` parameterise the quick mount
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
throttle gives ~11 m/s² and tracks dead straight - 153 m with `x` unchanged to 2 decimal places.
**One bug caught that way and fixed**: at the 20 m/s cap the motor cut but nothing bled the
overshoot, so it held 22.6 m/s. `capped` now takes the coast brake as well.

**Two things the user did by hand that were quietly wrong, both corrected:** the scene's
`MotorcycleSpawner.motorcyclePrefab` pointed at `pizza_delivery_bike_wolt copy.prefab` - the raw
imported model, which has no `MotorcycleController` - and the GLB itself was named
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
| `Assets/Editor/JoeClipImporter.cs` | **The Block → Import Joe Clips** - the borrowed-clip import recipe, scripted |

**There are two ways in, and both are the web build's.** A car with a seat block in
`config.vehicle.driver.seats` plays the entry ANIMATION and its progress drives the door
(`doorOpenAt` 0.25 → `doorCloseAt` 0.7). Anything else - the bike, the jetski, the heli, an untuned
car, or the Mustang if the clip is ever missing - gets the QUICK enter off `enterDoorOpenTime` 0.55
and `enterDoorCloseDelay` 0.5, with the rider simply hidden. **U10 needs the second path and it is
already written**; do not add a third.

**⚠ `Convert.ModelOffset` was wrong and is now fixed: X passes through, only Z flips.**
`(x, y, -z)`. It had an X negation from U6 that no unit had ever exercised, because every offset
ported until now had `x = 0`. Both engines put a model's right at local `+X`; only forward differs.
The measurement is in the method's own doc comment. Nothing else moved - both camera booms are
`x = 0` - but **anything that trusted the old shape is suspect**. `Convert.ModelAxis` is new and is a
third conversion again: a rotation axis negates Y and Z and leaves the ANGLE alone.

**⚠ The seat block is not a seat.** `{ x: -2.31, y: -0.84, z: -0.1, yaw: -π/2, scale: 0.95 }` is
where the entry clip's ORIGIN goes - Joe standing beside the door at road level - and the clip's
~1.9 m of baked hip travel does the sitting. Read as a cushion those numbers are absurd: 2.31 m
sideways is outside a 2.38 m-wide car. `CarBuilder` adds the measured body centre back (the web
build recentres each car in a holder; this prefab's origin is the tyre contact patch) and lands the
anchor at car-local **(-2.31, -0.035, 0.048)** - the car's left, 3.5 cm off the road. The height
falling out at ~0 is the cross-check: `y: -0.84` is half the measured 1.611 m body height.

The clip is `Assets/Models/Characters/Joe_EnterCar.fbx`, the Mixamo source FBX (754 KB,
animation-only). **Its travel must stay in the pose, not become root motion** - Bake Into Pose on
rotation, position Y and position XZ, all Based Upon Original. `JoeClipImporter` sets that; add a
row to its `Clips` table for the next one rather than clicking through the Inspector.

### U10 - motorcycle

`config.vehicle.motorcycle`, whole thing:

```
modelUrl: /models/pizza_delivery_bike_wolt.glb   modelScale 0.66   modelYaw π
spawn (x -198, z -236)   roadSurfaceY 0.1   groundClearance 0.12
rider: { scale 1.1, yaw 0, seat { x 0.01, y -0.49, z 0.23 } }
```

Note the spawn is 8 m from the Mustang's, so both are in the parking lot and `enterRadius` will
have to choose between them - which is what `VehicleEnterExit.Nearest()` already does, except that
it only walks `CarSpawner.Spawned`. **The bike is not a `CarController`**, so U10's real design
question is what `Nearest()` iterates over: extract an interface both implement, or a registry that
anything enterable adds itself to. U16's pedestrians and U17's traffic do not enter it; U23's
helicopter and U24's jetski do.

Its rider IS a seat, unlike the car's: `src/vehicle/seated-rider.ts` freezes **frame 0** of a
sitting clip and parents it to the bike, so the offsets are a real seated position. Source clip is
`source-assets/models/Driving.fbx` (55 MB, ships a body - import animation only). U24's jetski
reuses the identical rig, which is why that file exists at all.

**Feel is re-derived, not ported** (port rule 2) - and a two-wheeler is not a `CarController` with
two wheels deleted. Budget real play-testing time. Whether PhysX WheelColliders can carry a bike at
all, or whether it wants a leaning Rigidbody with raycast wheels, is the first thing to settle.

### U8 reference - tuning knobs

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

Suspension and tyre-grip numbers are NOT here - they are baked into the prefab by `CarBuilder` and
live as constants at the top of `Assets/Editor/CarBuilder.cs`. Change them there and re-run
**The Block → Build Mustang**, which rebuilds the prefab in place so the scene keeps its reference.

Controls while driving: `W`/`S` throttle and brake-then-reverse, `A`/`D` steer, `Space` handbrake.
`E` gets in and out.

Measured in Play with synthetic input, if any of it ever looks wrong later: spawns on the lot with
four wheels grounded, caps at 20.10 m/s and −7.03 m/s, brakes through zero, steers right on `D`,
tracks straight to 0.045 m over 176 m, holds upright 1.0000 through a 72° turn at speed, and stops
dead against a building.

### What U7b built - swimming

**The sequence never had a row for swimming.** The web build has it (`config.sea.swim`, and
`player.ts` carries the state), the 32 units did not, and nothing downstream would have noticed -
the port would simply have shipped a sea you drown in. It is filed as `U7b` because it is U7's
state machine plus one pose, but it could not be built until U12 put water in the world.

Four pieces, no new systems:

- **`SeaGeometry.IsSwimming`** - a region test, not a raycast, because the water deliberately has no
  collider. The web writes `x < shoreX`; here it is `x > ShoreX`, and that sign lives in this one
  method. Depth is measured from the swimmer's float height, not from sea level, which is what the
  web does and is not a rounding detail: it starts the swim **6.4 m** past the waterline instead of
  11.7 m.
- **`PlayerController.Float`** - the buoyancy spring, replacing gravity outright rather than adding
  to it. Two traps: `swim.surfaceY` is a **capsule-centre** height while Unity's transform is at the
  feet, so it is used as `surfaceY − capsuleCenterY` (miss it and Joe floats waist-deep in his own
  shins); and the web damps per **frame**, which quietly ties the settle to the frame rate - raised
  to `Mathf.Pow(damping, dt * 60)` here, same curve at 60 fps and the same curve everywhere else.
- **The shore wall had to stop blocking the player.** It is one collider serving two purposes: cars
  must not drive out to sea, the swimmer must walk straight through. The web build solves it with a
  per-obstacle `obstacleFilter` predicate; Unity has the mechanism built in -
  `CharacterController.excludeLayers`, aimed at the Ignore Raycast layer that `WorldBuilder` already
  puts the wall (and nothing else) on. One line, no new layer, no new marker component.
- **`Joe_Swim`** - the animator gets a `Swim` bool and one looping state on an Any State transition,
  same shape as `Ride`. Crossfade is 0.35 s rather than the gait 0.18: water is entered by walking
  into it, so that blend IS the transition from upright to prone, and at 0.18 Joe snaps flat.

Wading needs no state and does not have one - the seabed is a real MeshCollider, so under 6.4 m out
the controller simply walks down it and gravity holds the feet on it.

**U7 is done** - the user confirmed walk, sprint and jump all read right on 2026-08-13.

Its blend was verified programmatically too: `Joe_Idle` at 0 m/s, `Joe_Walk` at 2, a 50/50
walk-sprint blend at 4.5 and `Joe_Sprint` at 7, with the jump transition entering and returning on
landing. **If sprint ever comes up again**, the two candidates, in order:

1. `PlayerAnimator.speedBlendRate` (12 m/s per second) means a standing start takes ~0.6 s for the
   blend to climb 0 → 7, while the controller is already at full speed on frame one. A short burst
   therefore never reaches the sprint clip. Raising the rate, or making it asymmetric so it speeds
   up faster than it slows down, is the first thing to try.
2. `JoeAnimatorBuilder.SprintClipSpeed` (5.58) sets the 1.25× playback correction. Movement speed
   itself is `config.player.movement.sprintSpeed` = 7.0 and was never touched by U7.

Rebuild the graph any time with **The Block → Build Joe Animator** - `Joe.controller` is generated
from `Assets/Editor/JoeAnimatorBuilder.cs`, not hand-authored, so re-run it after any new clip
lands rather than editing the graph in the Animator window.

**Clips still missing.** None of these block anything; each just falls through:

| clip | Mixamo name | falls back to |
| --- | --- | --- |
| jog | Jog Forward | the 50/50 walk-sprint blend at 4.5 m/s |
| falling | Falling Idle | holds the jump pose |
| exhausted | Standing Idle 02 Exhausted | idle |

When one arrives: drop the FBX in `Assets/Models/Characters` as `Joe_<Thing>.fbx`, add a row to
`JoeClipImporter.Clips` (U9 scripted the import settings - do not click through the Inspector), run
**The Block → Import Joe Clips**, then re-run the animator builder. `bakeRoot` is `false` for all
three of these: they are locomotion cycles the controller drives, not clips that move the body
through a fixed space.

`Joe_Swim` (U7b) is the worked example of exactly that path - 55 MB with-skin FBX out of the
original's `source-assets/`, one row in `JoeClipImporter.Clips` with `bakeRoot: false`, two menu
items, done. **The original's `source-assets/models/` is worth reading before assuming a clip is
missing**; it holds the raw Mixamo download for everything the web build animates.

**U6 is done** - the user confirmed the controls feel right. Controls:

| key | does |
| --- | --- |
| `W` / `S` | forward / back along whatever Joe faces |
| `A` / `D` | turn Joe left / right (tank controls - the camera follows the body, it does not steer it) |
| `Shift` | sprint, 7.0 m/s, drains stamina |
| `Alt` | jog, 4.5 m/s |
| nothing | walk, 2.0 m/s |
| `Space` | jump |

**Downtown was rendering as a nest of grey spikes and is fixed** (2026-08-13). Unity's static
batching had replaced its 122,678-vertex mesh with a `Combined Mesh (root: scene)` built on a 16-bit
index buffer, so every index past 65,535 wrapped. The collider kept using the real asset mesh, which
is why the world felt right and looked shredded - and why it survived the U1 checkpoint. See memory
`static-batching-shreds-big-meshes`.

**The world is generated, not hand-placed.** `World.unity` holds four roots:
`Main Camera`, `Directional Light`, `Player_Joe`, and `World` - everything under `World` is
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
   → tools/export-config.sh               (this repo - holds the port-specific paths)
   → Assets/StreamingAssets/theblock-config.json   (gitignored, 61 KB, whole config)
   → TheBlockConfig.Load()                (Assets/Scripts/Core/TheBlockConfig.cs)
   → The Block → Build World              (Assets/Editor/WorldBuilder.cs, applies Convert)
```

**The ground plate** is a 1400 × 1400 m plane at y −0.05 from `config.ground`, pulled forward into U8
because the districts are islands: a car that left one had nothing under it and fell forever. It
sits marginally below every district so district ground always wins a ground probe. **Its collider
stops at the shore** (U12) - see the U12 notes for why an untrimmed one deletes the beach.

Last build: **18 placed, 0 missing, 177 colliders** - the plate, the roads, the water, the beach,
the shore wall, 9 districts and 4 places.

**Every config asset is now ingested.** The gas station and police station landed 2026-08-15; the
gas station's *placement* is wrong and is U13's first job.

**The parking lot and Reichman are in** (2026-08-13). The user re-modelled both in Blender rather
than falling back on the shipped GLBs, and both reproduce `config.ts`'s stated geometry exactly:

- **Parking lot** - 165 × 116 m, asphalt top at y 0.08, stall lines 0.09-0.11. Spans Unity
  X[134.4, 299.4] / Z[−304, −188], the mirror of the web build's X[−299, −134].
- **Reichman** - 36.1 × 31.6 m, 31 m tall. Its south edge lands at z −185.08 against the
  `config.ts` note "the school's south edge (z~-185.1)", clearing the lot's near edge by 2.92 m
  against its "~3 m", with both centred at x 216.90 against its "aligned in X". Three independent
  landmarks, so the export orientation is confirmed rather than assumed.

Sources are `blender/parkinglot.blend` and `blender/reichman.blend` **in the game repo**, exported
by `tools/blend-to-glb.sh` here. That script only ever READS the .blend (Blender runs `-b` and never
saves), so port rule 4 holds.

**Hebrew text is NOT mirrored by the X negation** - checked by eye on Reichman's sign, which reads
`אוניברסיטת רייכמן` correctly. Worth knowing before someone "fixes" it; see memory
`x-negation-does-not-mirror-text`.

**Pizza place is a stand-in, and it needed three fixes** - all of them in
`WorldBuilder.AssetAliases` rather than baked into the file, so the download stays as downloaded and
the correction stays visible in the build report. User-confirmed 2026-08-13.

- It shipped a **collision proxy**: a `Collider` node holding a coarse box at 100× non-uniform
  scale, meant for physics and never to be drawn. It rendered as a grey slab over the shop and was
  the first thing a downward raycast hit, so ground probes read its roof. `HideCollisionProxies`
  now disables `Collider*` nodes on every place. Expect this on any Sketchfab prop - see memory
  `sketchfab-collider-proxy-node`.
- It **lay on its back**: the GLB's node chain leaves local Y and Z swapped, so the lamp post ran
  3.28 m along Z instead of standing up. Corrected with `ExtraEuler = (-90, 0, 0)`.
- Its **pivot is at the model's centre**, not its base, so half of it was underground.
  `ExtraY = 0.15` rests it on the pavement.

Stand-ins also **skip the config's `hideNodes`**: those name parts of the original model, and this
one happens to share the name `PizzaLight` - which is its lamp post, not the original's light.

**Known issues - all three were U11's.** The white foliage and the mixed car renderers are fixed;
see "What U11 built" above. The merged-mesh colliders are not fixed and not forgotten - they moved
to the **Deferred** section, with the trigger that would make them worth doing.

**District GLBs are gitignored** (40-85 MB each; free LFS is 1 GiB and shared with the original
repo). Working copies live in `Assets/Models/City/`, zips in `~/TheBlockSource/cities/zips/`. A
fresh clone opens `World.unity` with the districts missing until those are restored - deliberate.
`first-one.glb` is the exception: 240 KB and the only copy anywhere, so it is committed.

**Requires:** a session with cwd `~/TheBlockUnity` (the MCP server is scoped to that path) and the
game repo added via `/add-dir`. See `CLAUDE.md` §2.

---

## Units

State: `todo` · `wip` (half-built - the notes column MUST say exactly what and what's next) · `done`

### Tier 0 - Pipeline
| id | unit | state | commit | notes |
| --- | --- | --- | --- | --- |
| U0 | Project setup - Unity, MCP, git, LFS, docs | done | `dacca07` | Unity 6000.5.8f1 URP; MCP v10.1.2 HTTP Local :8080; remote pushed |
| U1 | glTF import path - glTFast + Draco, downtown solid | done | `5a0b58f` | glTFast 6.19.0 + Draco 5.4.3; `World.unity` is build scene 0; asset needed zero fixup |
| U2 | Character import - Mixamo FBX as Humanoid, walk clip | done | `13cea9f` | `JoeAvatar` isHuman, 52 bones; clips `Joe_Idle`/`Joe_Walk` loop. Bones were `mixamorig7:` - suffix varies per export, Humanoid makes it moot |
| U3 | `Convert` handedness helper | done | `16fe0ee` | Negate X. `Assets/Scripts/Core/Convert.cs`; verified 8/8 against the placed scene objects |
| U4 | `export-config.mjs` → `theblock-config.json` | done | `62d917a` | Whole config, not a subset - the game repo gets one change ever, so a subset would force re-editing it at U12/U13/U17. 61 KB, byte-identical across runs |
| U5 | `WorldBuilder` Editor script | done | `62d917a` | Menu **The Block → Build World**. User-confirmed 2026-08-13: their run reproduced the report line for line - 9 placed, 4 missing, 96 colliders |

### Tier 1 - Traversal
| id | unit | state | commit | notes |
| --- | --- | --- | --- | --- |
| U6 | Character controller + camera follow | done | `1905f94` | `Assets/Scripts/Player/{PlayerController,FollowCamera}.cs` on `Player_Joe` / `Main Camera`. User-confirmed 2026-08-13: controls feel right |
| U7 | Animator state machine (idle/walk/run/jump) | done | `2525c3b` | Graph generated by **The Block → Build Joe Animator**; `PlayerAnimator.cs` drives it. User-confirmed 2026-08-13: walk, sprint and jump all read right. Missing jog/falling/exhausted clips all fall through cleanly - see the clip table below |
| U7b | Swimming | done | `3190b43` | **Not in the original 32 - the sequence never had a row for it and the port would have silently lost it.** Belongs to U7's state machine, but needed U12's sea to exist, so it lands here. `Pose.Swim` outranks every other pose; buoyancy spring replaces gravity outright (`PlayerController.Float`); `SeaGeometry.IsSwimming` owns the region + depth test and its X sign. Clip is `Swimming.fbx` from the original's `source-assets/`, imported as `Joe_Swim`, `bakeRoot: false` - a locomotion cycle, not a fixed-space clip. **The player had to be let THROUGH the shore wall it shares with the cars** - `excludeLayers` on the CharacterController, which is Unity's answer to the web's `obstacleFilter`. User-confirmed 2026-08-15 |

### Tier 2 - Vehicles
| id | unit | state | commit | notes |
| --- | --- | --- | --- | --- |
| U8 | Vehicle base + one drivable car | done | `b789c5a` | Rigidbody + 4 WheelColliders, NOT a port of the kinematic `vehicle.ts`. `Assets/Scripts/Vehicle/{CarController,CarWheel,CarSpawner}.cs`; prefab generated by `Assets/Editor/CarBuilder.cs`, which **U17b generalised to all four cars** (**The Block → Build Drivable Cars**, replacing Build Mustang). User-confirmed 2026-08-13: it drives and feels right. ⚠ **U17b found that this unit's paint never applied** - it wrote `_BaseColor` on a glTFast material whose property is `baseColorFactor`, so the Mustang was its model's native dark green, not the config's red, from U8 until then. Tuning table in RESUME HERE |
| U9 | Enter/exit state machine + seated driver | done | `a86df20` | `E` and a real door. `Assets/Scripts/{Core/GameMode,Vehicle/VehicleEnterExit,Vehicle/CarDoor}.cs`; `DebugVehicleSwitch.cs` deleted. Both of the web build's enter paths - the 5.47 s entry clip for a car with a seat block, the timed door swing for everything else. **Caught and fixed a wrong X in `Convert.ModelOffset`.** User-confirmed 2026-08-13 |
| U10 | Motorcycle | done | `80f7fa4` | Rigidbody + 2 WheelColliders + an always-on upright stabiliser + a visual lean, NOT the original's kinematic model. `Assets/Scripts/Vehicle/{MotorcycleController,MotorcycleSpawner}.cs`, `Assets/Editor/MotorcycleBuilder.cs`. `IEnterable` gained `UsesEntryAnimation` + `ShowRiderOnQuickMount` so one enter/exit machine still serves both; vehicles now self-register with `EnterableRegistry`. Rider is `Joe_Driving.fbx` → `Joe_Ride`, a real looping state, parented to the bike's seat. **Caught and fixed: an interface `[SerializeField]` Unity was never serializing, and a speed cap that held 22.6 m/s against 20.** User-confirmed 2026-08-15: riding feels right |

### Tier 3 - World
| id | unit | state | commit | notes |
| --- | --- | --- | --- | --- |
| U11 | All 9 districts via WorldBuilder | done | `21857c3` | Placement and colliders shipped in U5; U11 is the three rendering faults that survived it. Foliage: the white shards were a spurious V flip in glTFast's `_ST`, NOT the blend mode - `WorldBuilder.UnflipV`, plus a real alpha-clip pass that rebinds to generated URP/Lit materials because `_AlphaClip` on an imported glTFast material is inert. Cities 2/3: baked cars stripped at the SUBMESH level in Unity - 86% of the mesh - instead of a Blender split, out of collision as well as sight. Empty material slots were drawing magenta and now get glTF's default material. **Caught and fixed: a substring pattern list that alpha-clipped every road, because "tree" is inside "CityGen_Streets".** Foliage colliders left open on purpose - see Deferred. User-confirmed 2026-08-15 |
| U12 | Roads, ground, sea | done | `7dc8208` (+ fixes 2026-08-15) | **Two faults found at U15's play-test, both fixed - see the decision log: `config.fog` was never ported, so the 320 m far plane sliced the skyline; and the ground plate showed through the sea's wave troughs (0.37 m of swell vs a plate at −0.05 m), now cut out of the plate mesh.** Roads are `com.unity.splines` + a generated ribbon, NOT the web's per-segment stretched tile: 1864 m of spline vs 1859.5 m of polyline, corners curved, markings continuous through them. The `SplineContainer`s are kept as U17/U19's centreline. Road surface texture is generated because the web tile's paint is geometry. Sea is a port of `sea-surface.ts` into `Assets/Shaders/{Water,Beach}.shader` (URP has no built-in water) - unlit on purpose, since the original does its own lighting. Beach is a displaced MeshCollider you walk down. `Assets/Scripts/World/SeaGeometry.cs` owns the waterline and its handedness - the sea is Unity **+x**. **Caught and fixed: the ground plate's collider held the player up over the whole beach; it now stops at the shore. "Kerbs" were phantom scope - no such system exists in the original.** Splines needs ≥2.9.0 on Unity 6.5. User-confirmed 2026-08-15 |
| U13 | Places - pizza + interior, gas, police station, lot cars | done | `211abc2` | User-confirmed 2026-08-15. Gas station was Y/Z swapped by the Sketchfab export's cancelling root matrices; `Rx(-90)` in `AssetAliases`, whose entries can now correct the REAL asset (`File = null`) instead of only swapping in a stand-in. Lot cars are 101 real GameObjects with per-car culling and `LODGroup`s, NOT an InstancedMesh - same seeded layout as the web build (`Mulberry32` in `uint`), paint as 18 generated materials so the instancing survives. Interior is a teleport cell with the fog/ambient swap; its lights stay on and the sun stays up, both of which the web build only fights because of three's forward renderer. **Caught and fixed: `tesla.glb`/`avenger.glb` require `EXT_texture_webp` and glTFast rejects the whole file - `tools/glb-webp-to-png.py`; and a BoxCollider that ignores the model scale is a kilometre wide on the 37.4× Avenger.** NPC + pizza pickups deferred to U21, the fade to U25 - by the user's call. **Lot-car promotion, deferred to U17 and then to U17b, is DONE there**: every filler carries a `LotCar` and `E` swaps it for the drivable car of the same model, colour, stall and heading. **The interior's MISSION mechanics are explicitly unsettled and belong to U21** - the room is right, what the delivery does inside it is not |
| U14 | Map + minimap | done | `8ea9fc4` | User-confirmed 2026-08-15. The base layer is a LIVE second camera into a 1024² RenderTexture (`Assets/Scripts/UI/MapCamera.cs`), not the web's boot-time bake - no readback, no shader-compile spike, and moving cars show. UI Toolkit, eleven units before U25: `MapView` paints outlines/dots/arrow with Painter2D and pools `Label`s for text, `GameMap` owns the panel and the `M` toggle, `MapRegistry` is the port of `world/registry.ts` and the hook missions add objective pins to. Both states capped at 12 fps - the web caps only the minimap. Camera at `(90, 180, 0)` puts screen right on world −X, matching the web map's frame; verified against `transform.right`. **Caught and fixed: `PlaceSpec` has no `name` in config.ts - the pin labels live in `map-pois.ts`, and reading the absent field crashed the label pass; and a 16-bit RT depth that made Metal log "memoryless depth surface" as an error.** Emoji pin glyphs deferred to U25 (no emoji font), cop blips to U19, rival/arena to U32 |
| U15 | World memory - texture compression (was: Addressables) | done | `4b7a93d` | User-confirmed 2026-08-15. The measurement the row demanded REJECTED Addressables: 13.5 GB of scene memory, 96% textures, and streaming 13.5 GB in chunks is still 13.5 GB. Real cause: glTFast textures are .glb SUB-ASSETS with no TextureImporter, so nothing ever compressed them - 12.9 GB raw RGB24. **The Block → Compress Textures** (`TextureCompressor.cs`) slices the embedded PNG/JPEGs verbatim out of the GLB container into `Assets/Textures/Generated/`; `GeneratedTextureImporter.cs` makes the first import BC1/BC7 with settings derived from the file NAME (so a Library wipe cannot lose them); `WorldBuilder.Textures.cs` clones .glb materials and rebinds - 688 slots. **Scene texture memory 13,498 → 3,204 MB (4.2×).** Caught: texture names are NOT unique in a .glb (seven "Untitled" in city 4) - resolver matches name+size+alpha and refuses to guess, 12 refusals reported; and NPOT+mips silently skips block compression while claiming DXT1 - `npotScale ToLarger`, which was 8.9 GB of the win. Memories: `gltfast-textures-never-compressed`, `npot-mips-skip-block-compression` |

### Tier 4 - Living world
| id | unit | state | commit | notes |
| --- | --- | --- | --- | --- |
| U16 | Pedestrian crowd (NavMesh agents) + zebra crossings | done | `0dc4398` + `27058ae` | User-confirmed 2026-08-15 - flagged **low performance, revisit later** by the user (see RESUME HERE). The pavement is not enforced, it is the only thing that exists: `WorldBuilder.Navigation.cs` carves all 12.7 km of `config.traffic.network` **Not Walkable** (172 volumes over 142 streets), which disconnects the two sides of every road, so the only route across is a gated `NavMeshLink` at one of **230 zebras** on 70 lit intersections - derived from the same graph and the same `stopLineDist + crossingSetback` as `traffic.ts`. NavMesh baked 963 × 805 m @ 0.4 m voxels over the DISTRICTS only (car park excluded) (`CollectObjects.Children`, PhysicsColliders) → `Assets/Navigation/Generated/NavMesh.asset`. `config.traffic` ported (`TrafficSpec`, `StreetSpec` + its union converter). Crowd is a **pool of 60 that follows the player**, trickled in 6 per sweep, not the web build's ~400 seeded-at-boot-and-frozen - `CrowdSpawner`/`Pedestrian`/`NpcAppearance`. Zero of the 80 hand-recorded rectangles and strips in `npc.config.ts` are ported and none are needed. **Caught: the pack's prefabs reference the BUILT-IN Standard materials while the URP twins sit unused beside them - 455 slots rebound, or every pedestrian is magenta. Then, at play-test: zebras 2 cm UNDER the street (GroundY took the lowest hit - the ground plate - z-fighting up as orange); the vendor's five LODs are 33 skinned meshes per person, all posed every frame whether drawn or not, and an unposed one swapping in by LOD change draws at bind pose against a walked-off skeleton - the 'exploding pedestrian'; and 90 agents spawned in one frame was the stutter, not the crowd's steady cost, which measured as zero.** LODs 0+2 only now (395 → 158 SMRs), spawn trickled, car park excluded. ⚠ Rooftops bake walkable and are filtered at spawn/re-target by a height band, not by the bake |
| U16b | Crowd rebuilt on the ORIGINAL's six people + authored placement | done | `31f5767` | User-confirmed 2026-08-15, play-tested together with U17. **The user's call after U16's play-test: stop patching the vendor pack, port the crowd the shipped game actually has.** Six Mixamo characters (Sophie/Remy/Elizabeth/Chinese/Peter/Lewis) imported from the original's `source-assets` FBX - 576 MB, Humanoid, one avatar CREATED per character and only that character's walk copying it (a shared avatar across six different bodies is how you get six subtly broken skeletons), `optimizeGameObjects` on, textures extracted so they can be compressed at all. Placement is `npc.config.ts` verbatim, now EXPORTED rather than re-typed: `export-config.mjs` gained a second source (`$npcSource`, `$npcSourceSha256`, `npcConfig` as a sibling of `config`) - 33 painted rectangles × 9, 38 strips × 8 split into two opposing lanes, a 9-per-district fallback, 2 gated crossers per zebra = **687 baked + 460 runtime = 1,147 people**. **NavMeshAgent is GONE from the crowd** and that reverses U16: the agent owned the transform, did its own avoidance and had to be created on the mesh first (the 'Failed to create agent' spam), and the original needs none of it because it walks authored strips and rectangles. The NavMesh STAYS as a query surface - `SamplePosition` is the web's `isWalkable`, `Raycast` is its `segmentWalkable`, which is the whole job of the 4096² mask with no readback and no 67 MB grid. **No LODGroup, one or two renderers per person: the 'exploding pedestrian' mechanism cannot occur.** Measured: peak 139 within 90 m (p95 79) so `liveCap` 155; frame time crowd-on 42.39 ms vs crowd-off 42.31 ms - **delta 0.09 ms**; 0 exploded, 0 on a carriageway, 0 on a rooftop, 230/230 gated. **Caught: `mesh.bounds` reports FILE units and ignores import scale, so an earlier pass 'measured' every character at 170 m, scaled the importer to fix it, and broke every rig (`Avatar Rig Configuration mis-match … position error = 43757 mm`) - height is now measured by instantiating into a preview scene and corrected on the prefab's VISUAL CHILD, never the importer and never the root (that would scale the physics capsule). Remy really is 4.20 m native, exactly as the web build's comment says.** Unity 6 removed External material location; not needed - Mixamo FBX come out of Unity's own importer as URP/Lit with base+normal already bound. Deliberate deviation, and the only one: the 1,147 are structs and only those in range own a GameObject, because U16 measured that the cost was the `Instantiate` burst, not the population |
| - | Vehicle hardening, folded into U16b | done | `31f5767` | User-confirmed 2026-08-15 (*"i notice that it is fixed"*). **The wedge came back once after the first hardening, and the ledger's own one-variable prediction held: it was the car.** The first pass validated only that the pose was a finite unit quaternion, which a stale-but-valid pose passes. `CarWheel.Pose` now also enforces the geometric bound - a `WheelCollider` pose is its own transform slid along the suspension axis, so it can never leave a sphere of `suspensionDistance` around the anchor; further than that did not come from the spring and the bone is left on the skeleton for a frame. Live: 0.126 m out against a 0.5 m limit. `Assets/Scripts/Core/SkinWatchdog.cs` added so a next occurrence names its own bone - and it reads BONES, because baked `renderer.bounds` do not grow when one is thrown (verified by throwing one 500 m). Original notes: `CarWheel` took its bone rest offset from `WheelCollider.GetWorldPose` in `Awake` - before the first physics step, where the pose is not guaranteed to be a unit quaternion, and `Quaternion.Inverse` of a zero quaternion is NaN. It also had no rebind guard, so a mid-Play recompile left `_boneRestOffset` deserialized as `(0,0,0,0)` and every LateUpdate wrote a degenerate rotation into a wheel bone - on a car whose body, doors and wheels are ONE skin over 16 bones, that is a black wedge across the sky. Now: offset from `transform.rotation`, `Bind()` guard like `CarDoor`, validation on the WRITE, and nothing posed before the first `FixedUpdate`. `CarController.Respawn` also rewritten - it used `cars.FirstOrDefault()` (whichever car pressed R), teleported to the raw config spawn which carries no Y (dropping the car to 0, under the road), and moved the Rigidbody with no `Physics.SyncTransforms`, so for one frame the wheel bones were posed where the car used to be |
| U17 | Traffic - graph, cars, lights | done | `2ea3c54` + `31f5767` | User-confirmed 2026-08-15. **Play-test fault: the lights looked frozen because the lamp quads were built 14 cm INSIDE the housing** - the epsilon was measured off the animated disc, which sits behind the lens, instead of off the shell's front face (shell at 9.675, discs at 6.883-7.163, so the shell stands 2.51-2.79 model units proud). The state machine was correct throughout: sampled live it held 125 red / 79 green / 20 amber / 9 red+amber across the 233 poles. Fixed in `WorldBuilder.Traffic.cs`; 233/233 now 1.7 cm proud of the shell. **The SECOND lamp fault is closed too, 2026-08-16 - user-confirmed, *"סוף סוף עובד."*** Deferred since 2026-08-15 as "beside a pole the lights do not change", it turned out to be **grey head-on and coloured from the side**, and the cause was not position at all: **the lenses were wound inside-out and had been since U17.** Proved off the asset rather than argued - decoding `LampDiscs.asset` gave a front lens with stored normal `(0,0,+1)` against a geometric normal of `(−0.33, −0.08, −0.94)`, so Unity culled it from the road while the far half of an inverted dome's rim leaked colour from the kerb. Three passes had chased the Z and never once measured the facing. **Four things shipped, two of them the actual fix:** lenses are now **domes** (front AND rear per lamp, same submesh, still three materials); `EnforceWinding` measures every triangle against its own normal at build and flips the disagreements (`Cross(b − a, c − a)` is Unity's front face); the six lamp materials are **two-sided** (`_Cull Off`), so winding can never hide a lamp again; and **`The Block → Rebuild Traffic Lamps`** rewrites the mesh and the materials in place - no `Build World`, no re-placing 233 poles, no `navMeshData` trap. Commit `d167647`. Memory: `winding-hides-geometry-not-position` and `deferred-hypothesis-needs-falsifier`. ⚠ `Assets/Traffic/` is gitignored, so a fresh clone has no lens mesh until **Rebuild Traffic Lamps** (or `Build World`) is run once. Cars, lights and phases on U16's graph, which is now derived ONCE by the traffic pass and handed to the navigation pass - the crossings and the lights key off the same node numbering by construction. `Crossing.IsClearOfTraffic` deleted; `TrafficLightSystem` fills `Crossing.Gate` for all 230. **The population is DERIVED, not configured**: 130 cars over 12,759 m is one car per 98 m, so the live count is the metres of centreline in range divided by that - a fixed 32 was the plan and it gridlocked the city in under a minute, because the disc around the starting lot holds 1,230 m and 32 there is jam density. The graph is BAKED to a ScriptableObject at build time (6,590 Y-samples), so the runtime casts no rays for traffic at all. Kinematic while driving, a real Rigidbody wreck when rammed. **Caught and fixed, both by measuring rather than looking: `GroundY` could return a ROOF (downtown's avenue baked at 6-10 m) and the fast `Build World` was silently losing the whole NavMesh - `PasteComponentValues` does not carry `navMeshData`, so the crowd failed to spawn with nothing in the console.** Cars stop BEHIND the zebra, which the original does not. Carjacking split out to U17b |
| U17b | Carjack + `CarBuilder` past the Mustang | done | `26be56d` | User-confirmed 2026-08-15 (*"עובד טוב"*) - clean, with no play-test faults, which is the first unit since U12 that can be said of. `CarBuilder` builds all four drivable cars (one prefab per distinct `modelUrl`, so 4 out of 16 config entries - the other twelve are colour variants) and wires them into the scene's `CarSpawner` itself. `E` now resolves three ways in `main.ts`'s own order: real vehicle → **parked filler** (U13's deferred promotion, 101 of them) → **stopped street car**, which waits 5 s for you. **Both swaps were measured rather than eyeballed: the carjack lands at 0.000 m / 0.00°, the lot promotion at 0.029 m / 0.00°, paint material carried in both.** The enabling change is that every car prefab now shares one origin - body centre in XZ, contact patch in Y - so a pose taken off one prefab drops straight into another. `hijack.recycleMargin`/`recycleTries` are deliberately NOT ported: `Claim` retires the slot and the sweep that already runs twice a second re-places it out of the view cone. **Caught: the Mustang has been the wrong colour since U8** - the paint write named `_BaseColor`, glTFast's shader has `baseColorFactor`, so nothing was ever written and the car wore its model's native green. Tesla/Audi/Avenger have **0 wheel nodes** (verified in the glTF, not assumed), so their axles are stated off the body box; the Mustang's rig is the check and the rule matches it to within 4%. Split out of U17 by the user, 2026-08-15, to keep U17 to one checkpoint |
| U18 | Run-over + blood VFX | done | `781117d` + `fe081b8` | User-confirmed 2026-08-15. Root Motion ON, and this is the only place in the project where it is: the clip's own 1.74 m of travel IS the knockback, harvested off the visual child onto the pedestrian's transform each LateUpdate (and multiplied by that child's scale, because Humanoid retargeting produces root motion in the TARGET avatar's units and Remy's really is 4.20 m). Code adds only what the clip lacks - a 1.1 m arc and a speed-scaled push. **The debt to U16 in this row's old note is void:** U16b deleted `NavMeshAgent` from the crowd, so there is no agent to disable and no `Warp` to do. **The throw angle is MEASURED, not ported** - `clip.averageSpeed` gives 85.1°, which is the mirror of the web's hand-tuned −85.8° and is the cleanest handedness cross-check in the project so far. **Caught: Mixamo pads a one-shot clip with idle** (the body stands still for 79 of 145 frames), so `HitClipImporter` finds the action's own window by watching the root move rather than trimming to a typed-in number; and **`CrowdSpawner.Bind` destroyed every child of the Crowd object**, which deleted the `Blood` stain pool built on that same object - now Pedestrians only. New: `HitClipImporter`, `RunOverReaction`, `RunOverSystem`, `Vfx/Blood`, a `Hit` state on `Npc.controller`, `IEnterable.ForwardSpeed`. **Audio is U27's**: the original's scream pool and body thud fire from this exact impact frame, so `RunOverReaction.Begin` is where they go |
| U19 | Police pursuit + wanted level | done | `7993e19` (+ U19b/U19c) | **User-confirmed 2026-08-15** - *"maybe we will have minor improvements in the future but for now its solid"*. It took two follow-up rows to get there; both are below and both were the same class of fault. See RESUME HERE for what a future session actually needs. **Routing is real A\* over a stitched view of U17's graph** (`RouteGraph` + `RoutePlanner`, baked by `WorldBuilder.Police.cs` into `Assets/Police/Generated/`) - the web's "cops drive straight at you" was scar tissue from a graph split into 5 islands, and stitching T-junctions within 3 m makes 97.9% of the city one component. Straight-line survives in exactly two places: the last 40 m with line of sight, and the rejoin when a cop is off the graph. **The cop is a real WheelCollider car** built by the existing `CarBuilder` through a new `preRotation` seam (`PoliceCarBuilder`, own material folder, `enterable=false` so `E` cannot steal one), and it is driven by writing `CarInput` into the same `ApplySteering`/`ApplyDrive` the player uses - so it cannot corner in a way your car could not. ~~**Heat is a continuous meter, not +1 per crime**~~ - **REVERSED at U19b, see below.** **Not done yet:** the arrest and `BustSequence` have still never fired in a test, `PoliceProbe` is not written, and the approach is slow and sometimes indirect from the starting lot (which is 80 m off-graph - the hardest case in the map, and where the game begins). Original notes: real NavMesh; do NOT inherit the straight-line hack untested. **The run-over's heat hooks into `RunOverSystem`** - `Victims` and the `RanOver` event - and there is deliberately no second detector to add: the original's `crime.ts pedHit` radius scan is dead upstream (see the decisions log). One run-over event is one star however many go down, on a 3 s cooldown, and it applies during missions too |
| U19b | Police pursuit - the fix | done | `5771951` | **The user played U19 and the police never arrived; the cause, the fix and the measurements are in RESUME HERE.** Heat is a **whole-star counter** again - 1 crime = 1 star = 1 car, the web's own escalation - and the web's **`engaged` latch is back**, which is the actual fix: nothing bleeds until a cop has first reached `SightRadius`, so a station response with a 15-60 s travel time is possible at all. The continuous meter was not wrong about scrapes, it was **incompatible with the travel time added on the same day**: star lifetime ~6 s against a drive of 15-60. A crash is now a whole star above `CrashCrimeSpeed` (6 m/s closing, the user's call - "hard crashes only") or nothing, which keeps U19's "a scrape is free" fix without a severity curve. `GiveUpAt` counts only while `engaged`; `InboundGrace` (60 s) bounds the inbound phase. New `CopCar.Mode.Returning`: a cop that loses its star **drives back to its bay** on the same planner instead of teleporting out of shot. `Reconcile` now stands down the cop **furthest** from you, never the last in the bay order. Two arrest-approach faults fixed and NOT yet confirmed - the pull-in flank was recomputed every step and orbited (measured: stuck at 10.6-11.1 m, never reaching the 4 m radius), and an 8-12 m dead band left the rubber band's floor as the answer. Dead tuning fields deleted (`StationDeployRange`, `RetireDistance`, `OffGraphDistance`, and `GroundNormalY`/`CrashDeadzone`, which duplicated `CrashSensor`'s own and were never read). ⚠ **`RunOverCooldown` and `CrashCooldown` had to be fixed in the SCENE, not just in code** - see the decisions log |
| U19c | Pursuit - traffic yields, and the bust | done | `6fea7db` | **Second report: "police cars are not getting to me because they were blocked by other cars", and it was structural.** A `TrafficCar` is a **kinematic** Rigidbody, so to the cop's 1400 kg dynamic body it is a wall, not a car to squeeze past - it wedged, reversed, retried. The web build cannot hit this and its own config says why: its cops are kinematic character controllers that collide-and-slide around stopped cars, so shoving is free there and impossible here. **So traffic gets out of the way instead** - a car inside a pursuing cop's corridor eases 2 m outward and caps at 6 m/s, and NEVER stops, because a stopped car in the lane is the wall this removes. It rides on the lane-offset term the sampler already takes. Measured (isolated at `timeScale = 0.02`, because a static synthetic pursuer falls behind a 12 m/s car between two MCP calls and the first attempt read 0 for exactly that): ease-in **0 → 2.000 m**, speed **12.0 → 6.00**, clean ease-out. **The bust has two outcomes, the user's call:** in a vehicle you and it are impounded at the station; on foot you are cuffed where you stand. Money either way, which needed `Assets/Scripts/Game/Wallet.cs` - the port of `game/wallet.ts` onto `PlayerPrefs` - because `FinesOwed` was a tally nothing ever spent. `Charge` returns **what it actually took**, so a $100 fine against $40 costs $40 and the rest becomes debt: being broke is not a free pass. Measured: on-foot bust moved the player **0.04 m**, cash **$500 → $400**, cops all sent home, 0 errors. `WantedHud` gained a `$` readout and a BUSTED line that names which outcome happened. **U20 inherits `Heat.SuppressCrash`, `BustSequence.Busted` and `Wallet.Add`, all built and wired to nothing** |

| U19d | Pursuit - urgency on the run in | **done - user-confirmed 2026-08-16** | `86502ac` | ✅ *"u19d התנהגות רדיפת השוטרים גם טוב."* Written 2026-08-15 and driven a day later; it needed no correction. **It is also the one row in this ledger whose commit column was blank because the note feared its files had been swept into `51e8037` by a `git add -A` - they had not, and the hash is now recorded.** Asked for: *"the police should arrive a bit faster, so the user feels more urgency."* The constraint was neither top speed nor the star: the cop asked for 20.5 and delivered **13.7 m/s** because `CornerSpeed` bound it, and a red-light queue cost one cruiser **12 s in a single junction**. So (1) a **blue-light run** - `ResponseSpeed` 29 and `ResponseGrip` 11 apply only past `BandFar` with NO line of sight, so the chase you can still win is untouched; (2) **a cop does not queue** - blocked 1.5 s while asking to move, it swings 3.5 m into the oncoming side for 3 s, time-boxed, and this one applies during a chase as well, per the user's *"cops do not listen to traffic lights, they just get to their target"*; it does not touch the final approach; (3) `copYieldShift` **2.0 → 3.0 m**, because 2.0 left five centimetres between a 2.09 m cruiser and a 1.8 m car and the measurement showed exactly that. ⚠ New seam `CarController.SpeedLimitOverride`, whose only caller is `CopDriver` - needed because `config.vehicle.maxSpeed` is 20 for every car and `ApplyDrive` cuts the torque there, which means `PoliceTuning.MaxSpeed`'s "20.5, a 2.5% edge over the player" **was never reachable**. ⚠ `copYieldShift` had to be fixed in the SCENE as well as in code - the same trap as U19b's cooldowns |
| U19e | The officer who drives the cruiser, and arrests you on foot | **done - user-confirmed 2026-08-16** | `a269a6b` + `3a104a9` + `e02253b` | ✅ *"U19e נבדק וגם גמור."* **The user's own design, not a port** - *"the character I bought for free should drive the police car, and on the capture she gets out and chases us."* **All three play-test faults are closed: the foot arrest fires, she stops a stride short and turns to face you, and she sits under the roof instead of through it.** ⚠ **The seat took two numbers, and the second is the row's real lesson: a rider scale is not only a height.** `RiderScale = 0.833` put her under the roof and hard against the driver's door, because the anchor is the entry clip's ORIGIN and a uniform scale shortens the whole trip to the cushion - the hips went from car-local x −0.38 to −0.702 against a cabin wall at −1.04. Confirmed on the axis measured twice (y 0.79 → 0.661 vs 0.79 × 0.833 = 0.658), so the lateral figure needed no second look, and the seat's `X` moves −2.31 → **−1.988** to pay it back. ⚠ **The standoff is a pulled-back destination, not `agent.stoppingDistance`** - `Walk` is a hand-rolled straight line with no agent in it and the spawn car park has no mesh - and the stopping distance is a QUARTER of the standoff, because matching them would halt her at two standoffs, outside her own 1.6 m grab radius. One body in two places: seated she is a child of `CarController.DriverAnchor` with agent, capsule and Animator OFF and a kinematic body so the car carries her; deployed she is unparented at that same anchor. **The anchor is the exit animation** - it is where `Joe_EnterCar`'s ORIGIN goes, so sitting is that clip held at normalized 1 and standing is the same clip at 0, measured to put her hips at car-local (−0.38, 0.79, 0.08) inside a cabin of x ∈ [−1.04, 1.04]. `PoliceCarBuilder` states the CrownVic's seat port-side (the web has no cop car in `config.vehicle.driver.seats`); `CopOfficerBuilder` writes 7 URP/Lit materials, the controller and the prefab. **She is Humanoid with her own valid avatar, so Joe's clips retarget for nothing**, `Joe_Sprint` retimed 5.58 → 6.2 m/s. **On foot she is the arrest; in a vehicle the cruiser still is** - nobody on legs catches a car - and `OfficerChase` turns it all off. The NavMesh is a pursuit surface here and only here, with a straight-line fallback because the spawn car park has no mesh within 10 m. **The 459 MB Asset Store `.tga` set is OUT of the repo**: `Assets/Models/Characters/Officer/` is a 24 MB slim twin (2.3 MB FBX + 1024² PNGs) and `Assets/Police_officer/` stays gitignored and unreferenced. ⚠ **A culled Animator never writes the pose** - the whole sit was a silent no-op until `AlwaysAnimate`; memory `culled-animator-skips-pose-write` |

### Tier 5 - Missions
| id | unit | state | commit | notes |
| --- | --- | --- | --- | --- |
| U20 | Mission framework + campaign director + persistence | **done - carried by all four missions, 2026-08-16** | `51e8037` | ✅ Not marked on its own by the user and not promoted on inference either: **every mission it carries is now user-confirmed end to end** (U21, U22, U23, U24), which is the framework's own test - one reactor owning teardown, payout and cards across four completions, four failures and the retry key, plus a payout set that survived restarts. If a campaign-level fault ever appears, this row is where it belongs and it goes back to `wip`. **`MissionBehaviour` is an abstract MonoBehaviour, not an interface**, because `[SerializeField]` on an interface stores nothing in Unity and the campaign holds a hand-ordered serialized list. `Campaign`/`CampaignDirector`/`MissionFeedback`/`CampaignRunner` port `campaign.ts`, `campaign-director.ts`, `mission-feedback.ts` and main.ts's mission block; ONE reactor over status edges owns teardown, payout and cards, which is what makes "a bust and a clock time-out are the same edge" structural rather than a convention. `Progress`/`Payouts`/`Onboarding` on PlayerPrefs - **payouts MUST persist**: the web shipped that set in memory beside a persisted wallet and every mission paid again after a reload. `Beacon` ports `marker.ts` with shared meshes and per-colour cached materials. `MissionHud` + `BriefingCard` on the EXISTING UIDocument per the U25 row. **The three hooks U19c left dangling are wired**: `Heat.SuppressCrash`, `BustSequence.Busted` (its first-ever firing), `Wallet.Add`. Exporter extended from 2 sources to 7, table-driven; `config` and `npcConfig` come out byte-identical. **Caught: `BriefingCard` built in Start raced `CampaignRunner`'s Start and built a SECOND overlay - an undismissable dark panel over the screen with the real card behind it. Both Awake now, and guarded. And Unity's default font has no emoji, so every objective line drew blank boxes - `Glyphs.Strip` removes them at the point of DRAWING, so the copy is untouched and U25's font deletes one file.** Measured: $0→$80 paid exactly once and still marked paid after a Play restart; teardown left 0 POIs on BOTH the complete and the fail edge |
| U21 | M1 pizza delivery | **done - user-confirmed 2026-08-16** | `51e8037` | ✅ *"pizza mission and dance mission - mark as done."* Played end to end after the round-1 fixes: the shift starts at the counter, the five customers take their pizzas, the clock and the retry-from-the-shop both behave. **A delivery target IS a crowd prefab never bound to a seed**, so it stands and idles for free - no second character pipeline, and it is invisible to `RunOverSystem` (which only reads `CrowdSpawner.Crowd`), so a customer cannot be run over and the shift cannot be made unwinnable. The web loads five more FBX at boot for these. **Owns the interior's mission mechanics, which U13 left open**: the cashier behind the counter, `T` to start, briefing + voiceover, then out to the street. `Interior` gained `NearCounter`/`AtExitPad`/`LeaveNow`. Measured: 5 targets on the pavement at y 0.12-0.16 (not the plate at −0.05), the five faces in config order, a forced clock fail froze the HUD at 0:00 and retried to a fresh 240 s with no briefing replay |
| U22 | M2 rhythm / dance minigame | **done - user-confirmed 2026-08-16** | `90d24c6` | ✅ *"pizza mission and dance mission - mark as done."* Danced through with the song on the Music bus and the 21.3 ms compensation in place - the timing reads right to a human, which is the half no measurement could answer. **The clock is `AudioSettings.dspTime`, and that is the biggest feel win in Tier 5.** The web reads `audioElement.currentTime`: main thread, quantised to the decode buffer, jittering against the frame. Against 50 ms judgment windows on a project with a 42 ms frame and ~800 ms stalls in Deferred, that is scoring the frame rate instead of the player. **Measured drift: 0.02 ms** over a full run; `PlayScheduled` anchors the start to a named dsp instant because `Play()` begins somewhere inside the next buffer. **450 MB → 34 MB**: nine Mixamo with-skin FBX imported for their clips only, then DELETED - the same move the web's `anim-clip.py --strip-mesh` makes, and necessary because LFS is already at GitHub's 1 GiB free tier. One controller drives Joe and Remy (Humanoid clips are avatar-relative); default `Dance_Stand` so a giver just stands; Win/Fail terminal, every other one-shot self-returns on exit time. The dancer is an `IChaseTarget`, so U9's camera swap frames it with no dance-specific camera code. **Caught: the `copy-avatar-needs-same-bone-names` memory, verbatim - Copy From Other failed on `mixamorig7:Hips`, so each file Creates From This Model. And a handedness trap with no precedent: the web uses OPPOSITE Z signs for its two camera booms (player +2.5, dancer −5.0) because its player model is π-rotated in a holder and its dancer is a raw Mixamo body. `ModelOffset` is right for one and wrong for the other; applied here it put the camera in the dancer's face. The boom passes through RAW. A conversion belongs to a coordinate's PROVENANCE, not its shape.** Measured: 125 notes at 2.00/1.51/1.01 beats against the authored ramp, 0 repeats; boundaries exact at 49/51/130/140 ms; the 0.5 gate passes 100-good and fails 100-miss; the exit returns the player 3.00 m from Remy with camera and vehicle machine restored |
| U23 | Helicopter + M3 rooftop rescue | **done - user-confirmed 2026-08-16** | `f0388c5`, `d485bae`, `6b38c6e` | ✅ *"helicopter - mission looking good"* → *"mark as complete"*. Flown by a human at last, and it took two rounds: the Huey answered only WASD and could be left spinning (U23b, `d485bae`), and the survivors were questioned and cleared by measurement rather than changed - posed sole **0.001 m** off the roof, 46/46 baked spots within 5 cm of their surface, the −0.15 m the renderer bounds claimed being the `skinned-bounds-ignore-thrown-bones` trap wearing a new face (U23c, `6b38c6e`). **U23b, from the first flight:** both key sets are read now (`W/S · A/D` **and** the arrows, through the same `Held` the bike uses), and the endless spin is gone - yaw was composed with `MoveRotation` only while the stick was off centre, so nothing owned rotation the rest of the time and a knock simply stayed (measured: 3 rad/s still **1.82 rad/s ten seconds later** at PhysX's default 0.05 damping). Yaw is an angular velocity written every step now, zero included, and an unflown craft bleeds a knock off at 6 rad/s². Measured in Play: parked knock gone in 4.0° of yaw, flown knock **0.0000 rad/s on the next FixedUpdate**, at rest euler `(0, 0.1, 0)` with the rotor at world y 3.30. **A real Rigidbody, not the web's kinematic controller with gravity off forever.** Flown, gravity off and velocity written in, so the arcade hover is unchanged; vacated in the air, `useGravity` goes true and PhysX does the fall - the web's hand-written `fallGravity`/`fallMaxSpeed` become numbers to check against. A craft set down on a roof RESTS on it, which is what the mission needs from every roof in the city. Fuselage-only collider per the config's Blender measurement. **Roof spots are BAKED** (`WorldBuilder.Rescue` → `RoofSpots.asset`) the way U17 bakes the traffic graph: the runtime casts nothing and the result is inspectable before anyone flies. The cast takes the FIRST hit from 400 m up - the topmost surface, the opposite of both raycast memories - and rejects >30° slopes, which the web has no way to test for. **Caught: a global spot cap starved four districts of eight, so every rescue would have sent the player to the same corner. The quota is per district now.** Measured: Huey 5.40 × 4.70 × 12.49 m with skids at the pad height; 46 spots across all 8 districts, survivors 27-94 m up, closest pair 104.6 m, 4/4 on the topmost surface at Δ 0.00 m and 0° slope; pickup ignores a 15 m hover and takes at 8 m. **Never flown by a human** |
| U24 | Jetski + M4 chase | **done - user-confirmed 2026-08-16** | `f0388c5`, `6b38c6e` | ✅ *"mark as complete"*. Two faults found on the water and both were a number that stopped being true when something else got better: the buoys sat at the sea's MEAN while U12's shader displaces the surface by up to **0.37 m** in the vertex stage, so half of every wave went over them - `SeaSurface` reads the swell off `Water.mat` itself and they ride it (9/9 at **0.0000 m**); and the thief STROLLED because the crowd's blend tree is `Sophie_Walk` at every threshold. He is on `Joe_Sprint` now, retargeted free through Humanoid, with its **5.58 m/s** of root motion retimed **× 0.538** onto his own 3 m/s so nothing skates (U24b, `6b38c6e`). The finale: 9 buoy gates that pass on proximity and never fail you, only the clock loses it, and catching the beached thief on foot completes the campaign. **⚠ THE PLAN WAS WRONG ABOUT THE BUOYS, and this is the unit's real lesson.** It said Unity would delete the web's two avoidance mechanisms because a collider is a collider. Both skis are KINEMATIC - their motion is scripted onto a water plane, because U12 built the sea as a shader surface with no volume to be buoyant in - and **a kinematic body gets no collision response against a static one**, so all nine buoys would have been scenery you sail through. `BuoyField` is the web's own radial push-out, now shared by the player AND the thief: one mechanism where the web has two. A smaller win than claimed, and a real one. Measured: a step 0.30 m from a centre lands at 2.60 m. **The thief is Peter** - the one crowd character the delivery run does not use - instead of the web's two dedicated 52 MB downloads; his ski is a tinted clone with the material CLONED first, or both skis go dark red. **Caught: `yield return card.ShowAndWait(lines)` deferred `Show()` by a frame, and anything touching the card in that window parked the mission forever with its entry latch set and no key able to retry. `ShowAndWait` returns a `WaitWhile` now and all four entry routines release their latch in a `finally`. And `ChaseThief` indexed the 3-point sand path with the 18-point route's cursor.** Measured: jetski at Unity x 442 against a shore at 430 so the player swims out as designed; both vehicles refuse `E` until the cursor reaches their step; 9 gates / 9 beacons / 9 pins / 0 on the land side; the beach hand-off swaps the bodies; the catch refuses at 6.0 m and takes at 1.5 m against a 2.5 m radius |
| - | Minimap removed, then **restored** - both by the user, 2026-08-16 | done | `f0388c5`, restored below | Removed on *"remove the map from the left side… you will only see the map when pressing M."*, and put back the same day on *"let's bring the map back for real, the way it happens in three.js"*. **`GameMap.showMinimap` is ON again, in the C# default AND in `World.unity` - the scene's stored value beats the initializer, so flipping one alone changes nothing.** The removal built the mechanism the restore keeps: with the radar off and the map closed the whole second-camera pass is SKIPPED rather than rendered into a texture nobody sees, exposed as `Hidden` + `SetMinimapVisible`. That is still the Settings → Display radar toggle U26 owes, arriving early - U26 gives it a menu, it does not build it again. The restore also pulled the widget's geometry onto the web's `#map` css exactly, where U14 had eyeballed it: **200 px square** (was 220), **12 px inset** (was 16), rim `rgba(255,255,255,0.25)` (was 0.65), backing `rgba(0,0,0,0.35)`, corner radius 6 collapsed / 10 open. `HudPanelSettings` is ConstantPixelSize, so a panel px IS a screen px and the two builds line up 1:1. Behaviour needed no change - U14 already ported it: north-up, centred on the active entity, `minimapRangeM` 150 half-extent, 12 fps cap, same overlay |

### Tier 6 - Shell
| id | unit | state | commit | notes |
| --- | --- | --- | --- | --- |
| U25 | HUD + in-game UI (UI Toolkit) | **done - the font landed with U28, 2026-08-16** | `dd6fbb8` (the fade) | **CLOSED BY U28.** `EmojiFontBuilder` builds NotoColorEmoji (OFL) as a dynamic `FontAsset` at `GlyphRenderMode.COLOR` - the render mode is the whole question, because CBDT/CBLC glyphs are bitmaps and every SDF mode rasterises them empty. It goes in a `PanelTextSettings` (`Assets/UI/HudTextSettings.asset`), in both `fallbackFontAssets` and `emojiFallbackTextAssets`, assigned to `HudPanelSettings.textSettings`. **Not `TMP_Settings`** - that is uGUI's and does nothing here. Measured 11/11 probe glyphs into a 1024² RGBA32 atlas. `Glyphs.cs` deleted, its three call sites now draw the copy as written. The original row said: - This row was only ever two owings, and U26 paid one of them. **DONE: the fade** behind U13's interior teleport - `Assets/Scripts/UI/Menus/ScreenFade.cs`, on the shared document, unscaled. It COVERS rather than brackets (black up in the same frame as the teleport, then fades off) because `fade.ts`'s `await to(true)` has nowhere to go here: `Interior.Enter`/`Leave` flip `inside` and move the capsule in one statement and `DeliveryMission` reads `interior.Inside` on the next line. Nothing is lost - the move is instantaneous either way and UI Toolkit composites after the scene, so the first frame of the destination is already black. **NOT DONE: the emoji-capable font.** `Glyphs.Strip` is still the stop-gap, so the map draws dots instead of `⛽`/`🚓`/`🏪` and Mission Select reads `1.  The Block Pizza Run`. Plan: Noto Color Emoji (OFL) as a fallback `FontAsset` on `HudPanelSettings`' theme; if Unity 6's TextCore will not rasterise CBDT/COLR, fall back to monochrome **Noto Emoji** - a grey 🍕 beats a blank box - and only then delete `Glyphs.cs`. Everything else the row wanted was already built by U14/U19/U20 on the one panel |
| U26 | Menus - title, character select, briefing, controls, pause | done | `dd6fbb8` | **User-confirmed 2026-08-16** (*"works"*, after the radar toggle was fixed; *"all other buttons work good"*). A Boot scene whose bar reads `AsyncOperation.progress` (the number `loading-screen.ts` wished for and faked with hand-counted milestones), then a title screen on the HUD document over the frozen city - New Game · Continue · Character · Mission Select · Settings · How to Play - plus `Esc` → Resume/Settings/How to Play/Quit to Title. Built by **The Block → Build Menus**; `Boot` is build index 0, `World` is 1. **⚠ THE UNIT'S REAL LESSON: `Time.timeScale = 0` does not stop `Update`.** The web pauses by skipping one `stepSim` call; here fourteen scripts poll `Keyboard.current` every frame and kept firing behind the overlay, so `Core.Pause.Frozen` is a guard line in each of them. **The dance is unpausable by rule** - `Conductor` runs on `dspTime`, which no freeze touches. Three faults found only by measuring: UI Toolkit takes `Color` as LINEAR (the scrim rendered as a pale haze, the buttons peach); a percentage `max-width` against an indefinite parent collapsed every button to 162 px; and hiding the HUD by `display` **clobbered the Radar toggle**, which writes `display` on the same element - it hides with `visibility` now. `SessionReset` exists because `[RuntimeInitializeOnLoadMethod]` fires once per Play session, not per scene load, and Quit to Title is this port's first scene load. Deliberately absent: no Multiplayer button (U32), Mission Select teleports rather than mounts, Settings is one row, ~~roster is Joe alone~~ (**U29 added Jody and David, and had to reopen this screen twice: the panel's own roster list became a read of `CharacterRoster`, and the turntable had NO lighting - the web's three-light preview rig was never ported**) |
| U27 | Audio - sfx, engine, ambient, sirens | done | `bf0bdd9` | **User-confirmed 2026-08-16** (*"sound - mark it as done"*). Twelve of the web's thirteen audio modules, ~1.2 MB of clips, one `AudioMixer` (7 buses / 7 exposed params / 4 snapshots) built by a REFLECTION tool because Unity ships no public API for authoring one. The 20 synth cues of `sfx.ts` are **baked to `AudioClip`s once** instead of rebuilding an oscillator graph per press, with PolyBLEP on saw/square because Web Audio's oscillators are band-limited by spec and a naive one would alias audibly. The rotor is a literal port through **`OnAudioFilterRead`** - the three rates move by different factors (chop 2.43×, hum 1.71×, whine 2.17×) so one `pitch` knob cannot reproduce it. Sirens are **3D, on the cars**, capped at the nearest 3: the web's one-shot wail exists only because that build has no `AudioListener` at all. **Caught, and it is the important one: an `AudioMixerGroup` costs one DSP buffer, and it moved the dance's music 21.3 ms off its own beatmap** (§ below). Also caught: the engine WAVs' 7-18 ms decoder tail, which Unity has no `loopEnd` to ignore. **Radio deferred** by the user - the only system with a network dependency |
| U28 | Economy - the 7-Eleven + power-ups | **done - user-confirmed 2026-08-16** | `0044863` + `cd276a4` | Fuel was split out to **U28b** by the user, 2026-08-16, so the store could be one checkpoint. Everything else is built and in the scene: `SevenEleven` (automatic bi-parting doors, sales floor, counter), a clerk from a crowd prefab, `ShopMenu`, `PowerUps` + `SpeedBoost`, `PowerUpChips`, and the four effects at their own call sites. **The store's geometry is READ OFF THE MODEL, not converted** - the glb ships a marker node for every point the config states, and 12 of them check out to 0.0 cm. **Caught by measurement, not by reading: the ☕ boost applied to police cars** - `PoliceCar.prefab` has no `CopDriver` (it arrives at runtime with `CopCar`, after `CarController.Awake`), so the cop exclusion never latched and drinking coffee to escape would have sped up the chase. `MarkAsPolice()` + a serialized flag. **Also: the door leaves swap sides on import**, so the parting direction is measured, never taken from `config.door.slide.x`'s sign. `Heat` gained `Immune` because `CrimeWatch` already writes `Frozen` every frame. Wallet deliberately left on the Police group. New menu item **The Block → Build Store** |
| U28b | Fuel - tank, limp mode, the pump | **done - user-confirmed 2026-08-16** | `4f46f70` | Built 2026-08-16. **The scene rig is not in this commit - it landed in `a269a6b` and the debt is closed** (verified 2026-08-16); a parallel session shared the tree, so only U28b's own hunks were staged here. `The Block → Build Gas Station` rebuilds it in one click if it is ever lost. `fuelConfig` is the **tenth exporter source**, appended so the JSON diffed clean. `FuelTank` is a component the vehicle owns, and **being a component IS the exemption** - the heli, the ski and every cruiser are excluded by never receiving one, so there is no second flag to forget. Both ceilings take the factor (**fuel scales reverse, ☕ does not**), and they multiply: measured, dry+coffee is exactly 1.25× the dry cap. **The line the whole unit hung on: `CarController.ApplyDrive` had no coast brake on `capped`** - the bike's had one since U10 and the car never needed it, because a boost only ever RAISES a ceiling. Limp mode collapses it 20→5 under a car doing 20, and without the brake the car coasts at 20 and the limp is invisible. Measured before and after: ordinary top speed **19.99 m/s**, unchanged. **Per-pump trigger, and it is a UNION with the web's circle, not a replacement** - pumps alone would be STRICTER across the forecourt's middle. Machine-checked 576/576 over the 9 m disc. New menu item **The Block → Build Gas Station** (which also installs the HUD gauge, so the destructive `Build Map HUD` never has to be run). Old row, still the plan of record: Split off U28, 2026-08-16. `vehicle/fuel.ts` + `fuel.config.ts` + `refuel.ts` + `world/gas-station.ts`. Tank per car and bike (never the heli or ski - `Vehicle.fuel` is optional for exactly that reason), distance-based burn so a tank means RANGE, limp at 0.25× that eases in over 1.5 s and NEVER strands you, hold-to-refuel on the Paz forecourt, the bar and the two hints. **Two cues are already baked and waiting**: `FuelTick`, `FuelDone`. **The speed hook already exists** - `SpeedBoost.Factor` multiplies the same clamp, so the tank's factor multiplies too and a dry tank still limps at full boost. ~~`fuelConfig` still needs adding to `export-config.mjs`'s `SOURCES`~~ and ~~put the refuel row back in `ControlsGuide` when it is true~~ - **both done, verified 2026-08-16**: the exporter carries `fuelConfig` and `ControlsGuide` line 45 reads `("Space", "Hold at a gas pump - refuel")` |
| U29 | Character roster | **done - user-confirmed 2026-08-16** | `3b83090` | ✅ *"looking good"*. Joe, Jody and David, from `characters.config.ts` - **the only ported table in this project that is hand-written rather than exported, and deliberately**: it is three ids, three names, and two optional tuning fields that are unset for all three; the rest of that file is GLB URLs. There is no number here to get wrong. **The fan-out is TWO bodies here and five in the web build, and that is U9's dividend** - `main.ts`'s `applyCharacter` reaches four separately-built rigs (walking capsule, seated driver, bike and jetski riders) because each is its own skinned mesh there; this port reparents ONE player into every seat, so all four collapse into a single `CharacterBody`. What is left is the player and the stage dancer. **The dance was a real gap and the user called it before a line was written** - `DanceBuilder` baked `Joe.fbx` into the stage at build time, so picking Jody would have left Joe up there, which is the exact fault `dancer.ts`'s own header records as fixed in the web build. **A second gap nobody had named: `VehicleEnterExit` caches `_driverRenderers` once**, so a swap mid-drive would switch the dead body's renderers and never hide the new one. `Swapped` is an event with three subscribers for that reason. **`Player_Joe` was restructured** - the Animator, nine skinned meshes and the skeleton moved off the root onto a `Visual` child, because the height match is a scale and a scale on the root resizes the `CharacterController` capsule. Heights are matched to **Joe**, not to 1.70 m: he is `referenceCharacterId` and the point of that is that adding a roster changed nothing about him. Jody 1.899 m × 1.037, David 1.934 m × 1 (inside tolerance), Joe × 1 by construction. **Two things measured rather than assumed: U16b's "Mixamo FBX come out with base+normal bound" is importer STATE, not a guarantee** - Jody and David arrived with 7 and 6 white slots and their textures extracted right beside them, and the reason Joe is fine is a remap in a **gitignored** `.meta`, i.e. a local patch no clone has; the builder writes URP/Lit materials in code now. And **the texture↔slot pairing is Mixamo's set number, not the names** - Jody's body material is `Ch38_body` while every texture of hers is `Ch37_*`, so `_body`→1001 / `_hair`→1002, with David falling back to 1001 because he has no 1002. New menu items: **The Block → Import Characters (slow)** and **→ Build Characters**; `Build Menus` and `Build Campaign` call back into the latter so no build order has to be remembered |

### Tier 7 - Ship
| id | unit | state | commit | notes |
| --- | --- | --- | --- | --- |
| U30a | macOS build - the game leaves the Editor | todo | | **Split out 2026-08-16, and the split is the point: a build is a correctness job, a perf pass is a measurement job, and stripping debug keys is a shipping job. Three checkpoints, not one.** Build Profiles → macOS → Apple Silicon → a `.app`. **Nothing in this port has ever run outside the Editor**, so this is the first moment anything Player-only can surface - stripping, shader variants, a different memory ceiling, a different input stack. Two risks already checked and CLEARED: build scenes are set (`Boot` 0, `World` 1), and **zero scripts under `Assets/Scripts` reference `UnityEditor`** (the whole world-building toolchain lives in `Assets/Editor`, which a Player build excludes by construction). `/[Bb]uild/` is already gitignored, so output cannot bloat the repo. **The one open choice is the scripting backend**: Mono is the current default (`scriptingBackend` is empty in `ProjectSettings.asset`) - fast builds, but `Contents/Resources/Data/Managed/Assembly-CSharp.dll` is readable by anyone with ILSpy; IL2CPP is AOT, faster at runtime, slower to build, and much harder to read. **Done when the `.app` launches from Finder with Unity closed and one full mission completes inside it.** |
| U30b | Perf pass - on the Player, not the Editor | todo | | **Order settled by the user 2026-08-16: build first, then profile.** The reason is specific - the top suspect for the ~800 ms hitches is **synchronous shader-variant compilation, which exists only in the Editor**; a Player prebuilds its variants. Profiling first risks spending the budget on a ghost the build deletes for free. Same for the green blocks, diagnosed as Metal under memory pressure: the Editor holds a second copy of half the project, so "is there memory pressure" is not answerable from inside it. **Start with the one measurement that transfers either way: 1,513 ms at t≈6.1 s and four hitches inside the first 15 s**, which is world + crowd load - code structure, not renderer path. Needs a **Development Build** so the Profiler can attach; `FrameWatchdog` is `#if UNITY_EDITOR` and correctly does not ship, so the Player pass uses the Profiler instead. This unit owns every entry in **Deferred**. Old row's note, still true: watch texture memory - it killed web mobile |
| U30c | Ship hardening - debug keys and shipping defaults | todo | | **LAST, and deliberately after the submission video is recorded**, because the debug keys are how the video reaches every feature in five minutes. What comes out or gets gated: `CrimeWatch.debugStarKey` (`P`, currently `true` and serialized in the scene, i.e. **it ships today**), U17's `T`, U16's `C`, `PowerUps.debugStock`, `CampaignRunner.debugStartMission` (−1 today, so inert but present). One judgement call, not a deletion: `Wallet.startingBalance` is **500** here against the web's **0** - it was set so there was something to lose before U20 paid anything, and it is rewritten by every `Build Store` / `Build World` run, so "fix it in the scene" is not a fix |
| U31 | iOS / iPad | **dropped - the user's call, 2026-08-16** | | *"ipad אנחנו כנראה נראה מזה… זה לא רלוונטי להגשה."* **Out of the port's scope, not failed and not deferred.** `CLAUDE.md` always called iPad "a wanted bonus, never a constraint on design", and this is that sentence being cashed: the port ships to macOS. **The user may still try a build personally, for the engine experience** - that is a private experiment, not a unit, and nothing in this ledger waits on it. **What this closes elsewhere:** the dance's tappable arrows (Deferred) lose their only remaining trigger, and every "U31 inherits this" note in the ledger is now inert. **What it does NOT license:** ripping out iOS support. The module is installed, `PlayerSettings` has an iOS section, and touch input costs nothing while unused - deleting any of it would be work spent to make a future retry harder. Old row: free 7-day Xcode provisioning; $99 only for distribution |
| U32 | Multiplayer | todo | | DEFERRED by decision - revisit only here |

### Tier 8 - Additions (not ports)

**This tier is the submission video's central argument.** The video has to answer *"why pivot engines
at all"*, and a faithful port answers it badly - "the same game, again" is not a reason. Tier 8 is
the reason: things the web build could not have. Which is why U35 below is scheduled INSIDE the
submission run rather than after it.

**The U35 list was chosen 2026-08-16** - eight additions, proposed in one session and accepted by the
user in full: *"בוא נוסיף את כל מה שאמרת לתוכנית. נממש את הדברים לאט לאט."* They are recorded here as
sub-units, ranked, so that none of them lives only in a conversation. **Three rules govern every one
of them, and the user set the third in the same breath:**

1. **The selection rule (from the original U35 row):** each addition is something the web build
   *could not* have done, not something it merely did not do. Every row below names its argument.
2. **The off-switch rule (U33's):** each ships **switched off** or gated behind a Setting, and its off
   state replays the old behaviour rather than routing neutral values through new code. Five
   always-on additions would re-open every visual judgement made in U11-U27, days before a recording.
3. **The perf-and-quality rule (the user's, 2026-08-16):** *"חשוב שלא נדפוק את הביצועים שלנו… ה-assets
   צריכים להיראות טוב, ואנחנו לא רוצים שהמשחק ייתקע לנו."* Concretely: **(a)** every sub-unit is
   measured on the **Player** against the U30b baseline, before and after, and a feature that costs
   more than its budget is tuned or cut, not shipped; **(b)** every new asset - Blender or bought -
   goes through the U15 gate (block-compressed textures, POT sizes, a triangle count written in the
   row) and is looked at in the Game view by the user before it is wired to anything; **(c)** no
   hitches: nothing spawns, instantiates or bakes in bulk on one frame - pool it, trickle it, or
   preload it in `Boot` (U16's 90-agents-in-one-frame stutter is the precedent). A sub-unit that
   passes play-test but fails (a) is `wip`, not `done`.

**Order: U35a → U35b → U35c first** (the most GTA per hour, and all three ride on systems that already
exist), ~~**then U35d** (the best-looking on video and the riskiest for the frame)~~ **- U35d is
DROPPED 2026-08-17**, ~~**then U35e**~~ **- U35e is DROPPED 2026-08-17 too**. **~~U35f~~ is DROPPED, U35g
is BUILT and confirmed, and U35h is the only backlog row left** (re-scoped by the user 2026-08-16 - see the rows) - after the
five, only if the frame and the calendar allow, and never before the video is recorded. **Slot: BEFORE
U30a/U30b, and the user play-tests EACH one at its own boundary** - they reordered it to one batch at
the end on 2026-08-16 and reversed that the same day (*"ברור, אנחנו צריכים לבדוק אחרי כל פיצ'ר כן"*),
so the standard checkpoint rule stands. Rule 2 is what keeps building before the baseline safe: U30b
takes it with every switch off, then each feature alone for its delta, so a feature built first cannot
pollute it. A sub-unit the user has played is **`built - user-confirmed, awaiting U30b`** - never
`done`, because rule 3(a)'s measurement is on a Player that does not exist yet.

| id | unit | state | commit | notes |
| --- | --- | --- | --- | --- |
| U35 | The showcase additions - parent row | **planned 2026-08-16, list chosen** | | **The user's own idea and their framing:** *"סשן של 5 פיצ'רים מגניבים, לראות מה אני יכול עוד להוציא מ-Unity."* The list is now the eight rows below; this row is their parent and carries the rules above. ⚠ **The sequencing trap, and it is the same one U19b paid for:** a feature added after the perf baseline invalidates it, so the frame gets re-checked between the last landed sub-unit and the recording. Precedent for what a good row looks like is already in this tier and in the standing remark: real A\* pursuit against the web's five disconnected graph islands, Rigidbody wrecks against a 30-vehicle Rapier budget, `dspTime` against `audioElement.currentTime` |
| U35a | Ragdolls - pedestrians and the player | **built - user-confirmed, awaiting U30b** | `6b856ab` + `abd69fe` | ✅ **USER-CONFIRMED 2026-08-16** - *"עובד טוב"*, **and its bike-dismount follow-up (`abd69fe`) was played and confirmed the same day** - *"U35a עובד טוב אתה יכול לסמן כן"*, so the throw→dismount→stand-up→remount loop is closed and the heading concern flagged for the play-test was not raised. Not `done` only because rule 3(a)'s frame measurement is on a Player that does not exist yet. **BUILT 2026-08-16. Its own section is above** - what is in it, the three faults building it found (Optimize Game Objects deletes the bones a ragdoll needs; `Interpolate` on a kinematic body drags every bone to the prefab pose; a kinematic body's PhysX pose is stale when it goes dynamic), the measurements, and the one hinge sign to look at in the play-test. `Build Ragdolls` writes 11 bodies / 10 joints / ~64 kg into six pedestrians and three player characters; `Settings → Gameplay → Ragdolls` default **on**; cap 4 with the oldest freezing; player thrown by a bike crash over 8 m/s or a fall over 5 m (the `K` debug key was removed the same day at the user's request - see the section), stand-up is a bone blend rather than a clip. ⚠ Perf debt for U30b: the six pedestrian FBX lost Optimize Game Objects, which is ~68 transforms per live body. **The original plan follows, unchanged, because every line of it survived contact:** **The argument:** the web's run-over is a canned Mixamo clip (`Hit_By_Car`, root motion harvested - memories `mixamo-pads-one-shot-clips`, `root-motion-on-a-scaled-child`) because Rapier on the main thread has no budget for a 15-body articulated rig per victim; PhysX does. **Mechanism:** the crowd prefabs are Humanoid, so Unity's **Ragdoll Wizard** (`GameObject → 3D Object → Ragdoll…`) builds the capsule/joint chain once per body type; at `RunOverSystem`'s hit, `RunOverReaction` disables the Animator, enables the rigidbodies and injects the car's velocity into the pelvis and the struck limb, then after N s the body settles and is recycled exactly as the clip's victims are today. **The player too:** thrown from the bike / a car door at speed, or a fall from a roof past a threshold, → ragdoll → `Getting_Up` (Mixamo, one more clip through the U29 importer) → control returns. **Off state:** a `Settings → Gameplay → Ragdolls` toggle, default **on** is the one exception argued for here - it replaces a reaction rather than adding a look, and it is the single most GTA thing on the list; if it does not read right the toggle restores the clip. **Perf budget:** a hard cap on simultaneous ragdolls (start at 4, oldest one freezes to a static pose), joints on `Solver Iterations` default, no ragdoll on the LOD-2 body (U16's `LODGroup` note applies - the ragdoll rig lives on ONE mesh). **Blender:** none. **Physics numbers are re-derived by feel (port rule 2)** - nothing to port anyway. Reuses: `RunOverSystem`, `RunOverReaction`, `Screams`, `Blood`, `CrashSensor` for the player's ejection |
| U35b | Vehicle damage - deform, smoke, fire, parts that come off | **built - user-confirmed, awaiting U30b** | `bb51c29` | ✅ **USER-CONFIRMED 2026-08-16.** Not `done` only because rule 3(a)'s frame measurement is on a Player that does not exist yet. **BUILT 2026-08-16. Its own section is above** - the three layers, the switch, the fuse, and the three findings (the Mustang is eighteen SKINNED meshes whose vertices live in bind space; the contact normal points INTO the struck body, so the first dent bulged the nose outward by 0.136 m; the .glbs group by material, which is why layer ③ needed no Blender and why the Mustang alone sheds nothing). `Settings → Gameplay → Vehicle Damage` = Off / Visual / **Off by default**. Two additions the user asked for during the play-test, both landed: **a wrecked car cannot be entered** (`CarController.TryEnter` refuses, `EntryRefusal` says why on the same HUD line that offers the key - U28's socket) and **the explosion announces itself** through `MissionHud.ShowHint`, for the player's own car only. ⚠ Perf debt for U30b: up to 4 cars holding cloned meshes (~2.8 MB for a Tesla shell), 3 emitters, 8 shed parts - `DamageBudget` is the knob. **The original plan follows, unchanged where it survived contact:** **The argument:** the web's cars are kinematic and a crash is a number; U34 already made collisions cost a star and a thump. This makes them cost the car. **Mechanism, three layers, each independently switchable:** ① **vertex deformation** on the body mesh around the contact point (`CrashSensor.Impact` already carries the point, the closing speed and `HitVehicle` - U34) - a radius/strength curve, mesh readable at import, capped total deform so a car never turns inside-out; ② **health** per car → engine smoke (URP particles, pooled, ONE emitter per damaged car) at 50 %, fire at 20 %, and at 0 an explosion: radial impulse to everything within R, the U34 `LotCar` promotion path already handles static neighbours waking up, and the wanted level pays a star through the existing crime hooks; ③ **detachable parts - this is the Blender work:** split the Mustang's (then each car's) front/rear bumper, bonnet and doors into separate objects in Blender, re-export, and give each a `FixedJoint` with a `breakForce` - a hard hit sheds the bumper as its own rigidbody that despawns after 20 s. **Off state:** `Settings → Gameplay → Vehicle Damage` (Off / Visual / Full), default **Off**; Off touches no mesh and spawns no emitter. **Perf budget:** deform writes only the struck car's mesh and only on impact (never per frame); one particle system per damaged car, at most 3 live; detached parts are pooled and capped at 8. Texture/tri budget for the re-exported cars must not exceed today's - the split is topology, not detail. **Also on the list here:** the cop cruiser is a car built by the same `CarBuilder`, so it inherits all three for free, and traffic wrecks (`TrafficCar.Wrecked`) get smoke as a byproduct. **Careful:** the `preRotation` seam and every seat/rider scale (memory `every-seat-carries-a-rider-scale`) survive a re-export only if the object origins do not move in Blender - export from the same file, split in place |
| U35c | Police helicopter at 3★ (~~+ GPS route on the map~~ CUT) | **built - user-confirmed, awaiting U30b** | `5ec82a8` + `cfbd4eb` (the cut) | ✅ **USER-CONFIRMED 2026-08-17, and the GPS half was CUT by the user in the same message** - *"תוריד את הקו התכלת מהמפה"*. `GpsRoute.cs`, `MapView.SetRoute`/`DrawRoute`, `Progress.GpsRouteOn`, the Settings row and `MapRegistry.NearestGuide` are deleted; `RoutePlanner`/`RouteGraph` stay because the police route on them. **So the unit ships as the helicopter alone**, and not `done` only because rule 3(a)'s frame measurement is on a Player that does not exist yet. **BUILT 2026-08-17. Its own section is above** - the three findings that cost a render each, the police-response fix the user asked for mid-build, and the play-test recipe. **The plan said a cop-coloured `CarPaint` twin of the Huey and the user rejected that** - *"המסוק של המשטרה צריך להראות כמו מסוק של משטרה"* - so the H145 was MODELLED IN BLENDER from two reference photographs, 5.6k tris, zero textures, approved at three checkpoints. `PoliceTuning.HeliStars` = 3 (0 = never); ~~`Settings → Display → GPS Route` default on~~ - removed with the GPS half. ⚠ Perf debt for U30b: ONE extra shadow-casting light at 512, only while airborne - the only new light in the port, and it gets its own delta. **Original row follows:** **Two arguments in one unit, both riding on things that exist.** ① **The heli:** at three stars a police Huey (the U21 model, `HelicopterController`'s flight, a cop-coloured `CarPaint` twin) lifts off from the station, holds a hover slot above and behind you, and pins you with a **real `Spotlight`** - URP spot with a **cookie** and **shadows** - that tracks the player on the ground; the rotor sound already exists (`RotorSound`), so does the siren bus. Three.js in a browser does not do a moving shadowed spotlight over a city at frame rate; URP does it as one additional light. Reconcile through `PoliceSystem` like a fourth car (Returning mode when the star drops), and it never lands: no seat, `enterable=false`, no arrest of its own - it exists to make the third star feel like the third star. ② **The GPS line:** the objective on the minimap and the full map draws as a **route along the roads**, not a straight line - `RoutePlanner` + `RouteGraph` are the U19 A\* the cops already drive on, and the web build's traffic graph was five islands, so it *could not* have drawn this. Re-planned only when the player leaves the current path by > 15 m or the objective moves; drawn on `MapView` as a polyline (UI Toolkit `generateVisualContent`, one mesh). **Off state:** the heli is gated by star count and by `PoliceTuning.HeliStars` (0 = never, ships **3**); the GPS line is `Settings → Display → GPS Route` default **on** - it is HUD, it changes no visual judgement of the world. **Perf budget:** ONE extra shadow-casting light, at a 512 shadow map, only while the heli is airborne - measure it against the U30b baseline explicitly, it is the only new light in the port; the route replan is off the hot path (0.25 s cadence, same as the cops). **Blender:** none - unless a searchlight housing under the Huey's nose is wanted, which is a five-minute mesh. Reuses: `HelicopterController`, `Rotor`, `RotorSound`, `Siren`, `PoliceSystem`, `Heat`, `RoutePlanner`, `MapView`/`GameMap` |
| ~~U35d~~ | ~~Weather - rain, wet roads, lightning, and grip that answers~~ | **DROPPED by the user 2026-08-17** | | ✗ *"הפיצר של המזג אוויר תוריד אותו הוא לא מעניין אותי כבר יותר. לא נממש אותו."* **Never started - no `Weather.cs`, no emitter, no scene object, nothing to rip out**; the row was `todo` and is now closed. It was also the row this ledger itself called *"the one most likely to fail rule 3"* against a 20.7 ms frame, so the cut removes the largest un-measured perf risk left in Tier 8 - it agrees with the selection rule rather than fighting it. **Naming note: `U35d-pre` (the in-vehicle arrest, `33420c8`) keeps its name and is unaffected** - it was named for the slot it landed in, not for weather. The plan is struck through, not deleted, so nobody rebuilds it. ~~**The argument:** rain that changes how the car drives. Visuals alone the web could fake; a `WheelFrictionCurve` whose stiffness drops with wetness is a physics engine doing the work. **Mechanism:** a `Weather` component beside `DayNightCycle` on the same object, with a `Wetness` 0-1 that ramps in over ~30 s: **rain** = one URP particle system parented to the camera (pooled, ~600 drops, soft-particle off, no collision - the drops die at a fixed height), splashes as a second cheap emitter under the camera's ground point; **wet roads** = the road/pavement materials get their `_Smoothness` lerped up and `_BaseColor` darkened by `Wetness` via a `MaterialPropertyBlock` per district renderer (no material duplication - U15's texture memory lesson stands), which gives sky and neon reflections for free under URP; **lightning** = a 2-frame flash on the main light's intensity + a `Thunder` clip on the ambient bus with a distance delay; **grip** = every `CarWheel`'s forward/sideways stiffness × `(1 - 0.35 × Wetness)`, the bike more; ties into U33: rain darkens `SkyPalette`'s current stop by a fixed factor rather than adding a fourth palette. **Off state:** `Settings → Display → Weather` = Off / Rain / Random, default **Off**; Off never instantiates the emitter and writes no property block. **Perf budget - this is the row most likely to fail rule 3:** U33 already cut Bloom against a 20.7 ms frame; rain particles + darker sky must be measured on the Player, and the emitter has a `maxParticles` that is a tuning field, not a constant. If reflections on wet roads need a reflection probe or SSR, **they are cut** - the smoothness lerp alone reads as wet. **Blender:** none. **Note:** the sea (`SeaSurface`) and the ski get no rain treatment; the sea already moves. **Reuses:** `DayNightCycle`, `SkyPalette`, `Ambient`, `CarWheel`, `MotorcycleController`~~ |
| U35e | ~~Stunt jumps + a Cinemachine camera~~ | **dropped - the user's call, 2026-08-17** | | ✂ **DROPPED** - *"we decieded we do not need that so do not mention it again."* Nothing was built for it, so the cut costs no work, and with it **Tier 8 has no scheduled unit left**. Not to be re-proposed. **Old row follows as HISTORY ONLY - do not act on any of it:** **RE-SCOPED BY THE USER 2026-08-16: the skid marks are CUT** - *"סימני צמיגים נמחק זה לא מענין אותי"*. What that removes is layer ① below and nothing else; **the handbrake goes with it** (it existed to make the marks worth drawing), so the unit is now two layers, not three, and its budget loses the 32-segments-per-wheel trail cap entirely. Struck through rather than deleted so nobody rebuilds it from the plan. **The argument, for what is left:** `FollowCamera.cs`'s own header says *"Cinemachine earns its place when the mission…"* - this is where it earns it, and the recording benefits from every shot after. **Mechanism:** ~~① **skid marks + tyre smoke** - `TrailRenderer` per wheel, emitting only when `WheelHit.sidewaysSlip`/`forwardSlip` cross a threshold, pooled and length-capped; a small smoke emitter per wheel on hard slip; drift = handbrake (a new key, `Shift` while driving, sideways stiffness on the rear halved while held).~~ **CUT.** ② **stunt jumps** - **the Blender work: 3-4 ramps** in a Florentin register (a plank-and-scaffold ramp, a rubble ramp, a container-and-plate ramp), each < 2k tris, one 1024² atlas, placed by a `StuntJumpBuilder` menu item at hand-chosen spots off the road graph; a trigger volume at the lip fires the jump: `Time.timeScale → 0.3`, a **Cinemachine** orbital camera takes over for the airtime, and a clean landing (all four wheels down within N s, no wreck) pays the `Wallet` and stamps the jump found on the map. ③ **Cinemachine** proper - install `com.unity.cinemachine` 3.x, keep `FollowCamera` as the default (it is user-confirmed and fifteen lines), and add virtual cameras only for: jumps, the bust, mission-start reveals, and a **cinematic camera key** (`V`, GTA's) that cycles a few shots for the recording - which is what makes it *the* video unit. **Off state:** ~~skids/smoke `Settings → Display → Tyre Effects`~~ cut with layer ①; ramps are world objects, present or not by build; the `V` camera is a key. **Perf budget:** ~~trails capped at 32 live segments per wheel and 4 cars, smoke emitters pooled~~ - gone with the cut; Cinemachine adds one Brain and costs nothing while a vcam is inactive, so what is left to measure is the ramps' geometry and the `timeScale` dip. **Reuses:** `CarWheel`, `CarController`, `Wallet`, `MapPois`, `BustSequence`, `CampaignDirector` |
| ~~U35f~~ | ~~Side jobs - taxi and deliveries in free roam~~ | **DROPPED by the user 2026-08-16** | | ✗ *"עבודות צד גם לא מעניין תוריד"*. **The cut agrees with the ledger's own assessment of the row** - it was written down as the weakest against the selection rule, and it is the one row here whose argument was "design, not engine". The wallet's free-roam earning gap it was going to close stays open, and that is now a known and accepted state, not an oversight. Kept struck through so it is not rediscovered as pending. **The original row follows, unbuilt:** Weakest on the "web could not" test - it is design, not engine - but the strongest on the game: the wallet today has four mission payouts as its only source and the 7-Eleven as its only sink, so free roam earns nothing. A hail-a-fare loop: a pedestrian on the pavement with a marker (they are NavMesh agents already, `CrowdSpawner`), stop beside them, they get in (the `VehicleEnterExit` seat rig, second seat), a destination pin from the POI table, a timer, a payout scaled by A\* distance (`RoutePlanner` gives the number, so the fare is honest), tip if no crash on the way (`CrashSensor`). Deliveries reuse `DeliveryMission`'s pizza flow with a random shop→door pair. Ships behind a `Settings → Gameplay → Side Jobs` toggle; whether it defaults on is argued at build time. Blender: a taxi roof sign, optional. Reuses: `CrowdSpawner`, `Pedestrian`, `VehicleEnterExit`, `MapPois`, `Payouts`, `Wallet`, `RoutePlanner` |
| U35g | Auto shop - paint the vehicle you drove up in (and buy cars, later) | **built - user-confirmed 2026-08-17, awaiting U30b** | `df0d9fc` (asset) + `d5e2da8` (feature) | ✅ **USER-CONFIRMED 2026-08-17** - *"cool. mark this feature as done"*; not `done` only because rule 3(a)'s frame measurement is on a Player that does not exist yet. **BUILT the same day - section "④ BUILT" above: C at the service point, ten swatches, $30, click → painted → closed, persisted per config vehicle; the motorcycle IS in (its red body recolours via an atlas re-hue, the Wolt box stays teal); "color" in every string.** Spec changed by the user mid-model: the car does NOT drive in - approach → shutter animation → colour menu. **Original row follows.** ✅ **The user's verdict 2026-08-16: *"קניית רכבים - מגניב"*, plus a design of their own that changes the unit's centre of gravity** - *"תסמן לעצמך שניצור asset ב-blender של מוסך פתוח, ששם היוזר יוכל לצבוע את הרכב שאיתו הוא מגיע, כלומר לצבוע ואז הרכב באמת יצא עם הצבע שהיוזר בחר."* **Three things follow from that sentence and none of them are the old row.** ① **The subject is the car you ARRIVED in**, not a showroom turntable - so the flow is drive in → the garage detects the vehicle you are sitting in → pick a colour → **drive out in it**. That deletes the teleport/interior pattern from the plan: it is an **OPEN garage** (the user's word - *מוסך פתוח*), a world structure you drive into, not a `U13 Interior` scene swap. ② **The paint must PERSIST on that specific vehicle** and survive leaving the garage, re-entering the car, and a reload - `CarPaint` writes `baseColorFactor` (memory `gltfast-basecolorfactor-gamma`) and the store is `PlayerPrefs` beside `Progress`, keyed **per vehicle**, not one global colour. ⚠ **The trap to design against before writing a line:** U13's lot cars deliberately share paint materials so they batch (see `TrafficLightPole`'s note on the same mistake) - a per-car colour must not hand every car in the world its own material instance. ③ **Blender is now REQUIRED, and it is this unit's asset**: an open garage/workshop in the Florentin register - roller shutter up, a bay, a sign - through the U15 gate (POT block-compressed textures, a tri count written here before it is wired, eyeballed in the Game view by the user first). **Buying cars stays in the row** as the second half and the money sink; the painting is the half the user asked for, so it is the half that ships first if only one does. **Reuses:** `CarPaint`, `CarSpawner`, `VehicleEnterExit` (to know what you drove in), `Wallet`, `Progress`, `MapPois` for the pin. **Original row, kept because parts of it survive:** Money sink number two, and a showcase of the config's own paint plumbing, so a colour picker is a UI over a system that exists. ~~A garage interior (the `Interior` teleport pattern from U13) with the four cars on turntables lit by U29's three-light preview rig~~ - superseded by the open-garage design above; the U29 studio rig can still light the bay. Blender: rims / a spoiler as optional bolt-ons, only if U35b's split export already exists (same file, same origins) |
| U35h | Breakable street props | **built 2026-08-17, benches re-sited 2026-08-18 - awaiting the user's play-test** | `1b8eccd` + `a0f7df7` | ✅ **BUILT, both halves - see the U35h section in RESUME HERE for what is in play, the measurements and the recipe.** The user reversed the 'poles only' answer by supplying three Sketchfab props on 2026-08-17; those went through a headless-Blender prep (`tools/prep-props.sh`) rather than a hand pass. **History follows:** **The user asked the right question first** - *"צריך לוודא אם יש אופציה לקחת assets קיימים במשחק כמו הרמזור או ספסל וכדומה ולעשות שהם יעפו יהיו שבירים"* - and it was measured against the tree rather than guessed. **The answer, and it is half good news:** ✅ **The traffic light IS available and it is a free win.** `Assets/Models/Props/traffic-light.glb` is the port's only standalone prop asset, and `WorldBuilder.Traffic` places **233 poles**, each its own GameObject under its own probe, each already carrying a `CapsuleCollider` sized by `lightsCfg.PoleColliderRadius`. That is exactly the shape `Breakable` needs - a static collider that already receives the U34 `LotCar` callback - so a pole that gets rammed can fall with no new asset at all. ⚠ **One thing to handle that a bin would not have:** a pole is a live `TrafficLightPole` driven by `TrafficLightSystem`, and its lamps are a three-submesh quad on SHARED materials so 233 poles batch. A knocked-over pole must go dark and drop out of its intersection's phase group (`TrafficLightSystem` counts poles per axis) - a felled light that keeps cycling green is worse than no feature - and it must not be given its own material instance on the way down. ✗ **Benches, bins, bollards, café chairs, bus shelters DO NOT EXIST as separate objects.** Grepped: no such node name appears anywhere in `Assets/Editor` or `Assets/Scripts`, and `Assets/Models` holds only `City/` (the eight district .glbs), `Places/`, `Vehicles/`, `Characters/` and a one-file `Props/`. Any street furniture in this game is **baked into the district meshes** - downtown is literally one mesh (memory `downtown-is-one-mesh`), so there is nothing to name, detach or knock over. **So the unit is now two clearly-priced halves:** ① **the poles, cheap, no Blender, do this one first**; ② **everything else is new Blender work**, not a reuse - and it also has to be PLACED, which is a `StuntJumpBuilder`-style menu item and hand-chosen spots. **Blender (half ② only):** split each prop into 2-5 pieces with sane origins, < 500 tris each, one shared 1024² atlas; the Sketchfab props' hidden `Collider` node (memory `sketchfab-collider-proxy-node`) must be stripped in the same pass. Ships as world objects; the number placed is the perf knob (start at 40, measure). Cheap, adds life, and the wanted level should NOT count it - a bin is not a crime, and neither is a traffic light |
| U35i | The police helicopter is a solid object, and hitting a police vehicle is a crime | **built - user-confirmed 2026-08-18, awaiting U30b** | `a2e3438` | ✅ **USER-CONFIRMED 2026-08-18** - *"looking good."* Not `done` only because rule 3(a)'s frame measurement is on a Player that does not exist yet. **The user's ask:** *"בוא נוסיף פיזיקה לאובייקט של המסוק המשטרתי, שלא נוכל פשוט ליסוע דרכו. אם מכונית מתנגשת במסוק אז שהוא יזוז גם כמובן (בנוסף התנגשות במסוק משטרתי או במכונית משטרתית. כמובן גם קוראת למשטרה, עלייה בכוכב)."* **Its own section is at the top of RESUME HERE** - the hull, the two regimes, the third crime line and the measurements. In one line each: a **2200 kg Rigidbody + four boxes** (skids/cabin/boom/fin, rotor disc deliberately outside them) on the U35c prefab; **dynamic and asleep on the ground, kinematic aloft**, flipped by state per memory `physx-pose-stale-on-activate`; both raycast probes now skip their own hull, without which the roof probe would read the craft's own engine deck and climb 12 m every quarter second for ever; `CrashSensor` gains **`HitPolice`** and `CrimeWatch` a **third line, `PoliceCrashCrimeSpeed = 3.5 m/s`** - measured, because cruisers crowding a stopped car come in at 2.15-2.45 and a deliberate ram at 6.93, so the U19 feedback loop (cop touches you → star → another cop) stays shut; skid friction **0.30/0.45** because Unity's default moved the aircraft 0.82 m at 54 km/h and this moves it 1.38 m. ⚠ Knock-on, and wanted: `VehicleDamage` reads the same `HitVehicle`, so ramming a cruiser now costs YOUR car the 1.4× vehicle multiplier. Not a planned U35 row - it came out of the U35h play-test session |
| U33 | Day/night cycle | done | `4ec2978` | **User-confirmed 2026-08-16** (*"i like the lighting… feature good"*). ⚠ **CORRECTED 2026-08-16: this row used to open "the first thing in this repo that is not a port of anything", and that is FALSE.** The original has a day/night cycle - `src/world/day-night.ts` + `day-night-state.ts`, committed there 2026-06-17, 13 keyframe stops, a sun arc and a moon fill light - sitting at `enabled: false` in `config.ts`, frozen at noon. What is true is only the narrower claim: **in the SHIPPED web build the sun never moves.** So U33 ports a default-off feature and independently arrives at the same default. The sharpest evidence that nobody read the original before building it: that config's own comment reads *"GTA-like pace = 2880 (48 min/day)"* - **the exact number this unit landed on after the user asked for half speed, and recorded as the user's call.** The IMPLEMENTATION is genuinely independent and the mechanism notes below all stand; it is the provenance that was wrong. **Why the correction matters beyond tidiness: the submission video states which features were invented and which were ported, and this row was the source for that claim.** It ships behind **Settings → Display → Time of Day**, default **Fixed**, and Fixed is not "close to" the old look: `DayNightCycle.RestoreBuilt` replays the scene as WorldBuilder left it, `renderPostProcessing` goes false so URP schedules **no post pass at all**, and ambient stays `Skybox`. Off costs 0 ms and every screenshot approved in U11-U27 still reproduces. **One light does the sun AND the moon** - URP's main light is the brightest directional and a second would demote to an additional light with no shadows, so it swings a full 360° (`pitch = (hour − 6) × 15`) and mirrors to the far side of the sky below the horizon; intensity ramps to 0 across ±2° of the crossing so the 180° flip is invisible. **Ambient is `Trilight`, never `DynamicGI.UpdateEnvironment`** - three lerped colours instead of a 1-3 ms skybox re-convolution - and that fixes a dead line from U13 as a side effect: `Interior` wrote `ambientLight`, which `AmbientMode.Skybox` ignores, so the pizzeria's warm ambient had never once rendered. **Night is CHEAPER than day**: below the horizon `shadows = None` drops the four 2048² cascades. Grading is Tonemapping·ColorAdjustments·WhiteBalance only - **Bloom was cut on cost**, 6-8 blur passes against a 20.7 ms frame. `SkyPalette` is 13 stops, **static and code-only on purpose** (a `[SerializeField]` palette is dead the moment the scene saves it). `Assets/Scripts/World/{DayNightCycle,SkyPalette}.cs`, built into the scene by **The Block → Build Day-Night** (`Assets/Editor/DayNightBuilder.cs`), which also has a **(Test Mode)** twin: cycle forced on, a full day in 2 min, `[` `]` step an hour, `\` holds the clock, and a corner banner so it cannot be left on silently. ⚠ **`Interior`'s per-Enter `RenderSettings` snapshot was DELETED** - it was U26's Radar/`display` bug again, two owners of one field. Day length **2880 s = 48 min**, GTA V's pace, the user's call. ⚠ **The scene rig is not in this unit's commit - it landed in `a269a6b` and the debt is closed** (verified 2026-08-16: `DayNightCycle` is on the `Directional Light` in the committed scene); `World.unity` was carrying U28's unsaved work at the time, so the rig rode in with the next scene commit. The menu item rebuilds it in one click if it is ever lost |
| U34 | Collisions have consequences | **done - user-confirmed 2026-08-16** | `9bd360c` | ✅ *"לגבי ריסוק מחדש אתה יכול לסמן את זה כגמור זה מתנהג כמו שצריך."* **The unit exists because of one discovery: `CrashSensor` had been attached to NOTHING since U19** - no prefab, no scene object, no `AddComponent` anywhere - so `CrimeWatch.OnCrashed` and the crash thump were both subscribed to an event that could not fire, and every collision in this game had been silent and free for fourteen units. It was found by grepping the `.meta` guid rather than the class name, which is the only search that distinguishes "referenced" from "merely compiled"; memory `static-event-with-no-publisher`. The fix is `CrashSensor.Ensure(gameObject)` from `CarController.Bind` and `MotorcycleController.Bind` - **not a prefab field, on purpose**, because `Build Drivable Cars` regenerates those prefabs and that is the likeliest story of how it was lost to begin with. **Three things followed from having impacts at all.** ① **A wall and a car are different crimes.** `PoliceTuning.VehicleCrashCrimeSpeed = 2.5f` (9 km/h) against the wall's `CrashCrimeSpeed = 6f` (22 km/h): ramming a car is a hit-and-run with a victim, scraping a bollard is geometry being forgiven, and the web build could not tell them apart at all. The test is `Impact.HitVehicle`, read off the **collider's own hierarchy** and never off `Impact.Other` - a parked filler is a static collider with no Rigidbody and arrives indistinguishable from a wall, and a traffic car promoted inside the same callback has no body yet either. ⚠ **A cruiser is excluded and that is a feedback loop, not politeness**: cops crowd you and touch you constantly, so a low bar against police contact mints a crime every cooldown, which spawns another cop and resets the give-up clock - a pursuit that cannot end because it is happening. Hitting one hard is still a crime, judged by the wall's line. ② **`TrafficCar` retuned** - `wreckSpeed` 6→3 (6 m/s is faster than most of a queue ever moves, so every collision inside a jam was a car hitting a wall), `wreckMomentumShare` 0.55→0.9 (PhysX has already spent most of the energy on the contact by the time this runs, so a share under ~0.8 reads as hitting a parked skip), `wreckMaxSpeed` 14→20, and the hard-coded 0.25 spin became `wreckSpin = 0.45`. ③ **`LotCar` takes a hit** - U13 gave 101 parked fillers a box collider and nothing else, making them immovable walls in the one place you are most likely to be driving badly. **A static collider still receives collision callbacks**, which is what lets a filler stay static until the frame it is struck and only then take a Rigidbody, so the usual dynamic count is zero and the worst case is a handful. The push direction comes from the CONTACT POINT, never from `Collision.impulse` or `relativeVelocity` - both carry a sign that depends on which body the callback fired on, and "away from where I was struck" cannot be backwards. Centre of mass drops into the sills or a car-sized box goes over on its roof at the first kerb. `Wrecked` bars promotion, per `TrafficSystem.NearestStopped`'s rule: promoting a wreck would swap a shunted, spinning car for a pristine one standing neatly in its stall |

---

## How to close a unit

A unit is **not done** until all three are true:

1. It play-tests correctly in the Editor, confirmed **by the user** (I cannot see the Game view).
2. This file is updated - state → `done`, commit hash filled in, `RESUME HERE` rewritten to the
   next action.
3. The commit lands.

If a unit **cannot** be finished, set it to `wip` and write in the notes exactly what is built,
what is not, and the next concrete action. Then update `RESUME HERE` to point at it. A `wip` unit
with a vague note is the one failure mode this whole system exists to prevent.

---

## Deferred - known, low priority, fix if it ever becomes worth it

**Not** the decisions log: these are open, and picking one up needs no permission. Each says what
would trigger it. A `wip` unit is work half-done; this is work deliberately not started.

- ✂ **THE RADIO IS DROPPED BY THE USER, 2026-08-17** - *"רדיו - גם תוריד, לא כזה חשוב."* **Everything
  below is research, not a plan.** It is kept because it cost a session to measure and it is the
  answer to "why is there no radio", but it is not pending work and is not to be re-proposed. The
  idle `Radio` mixer group, `GameAudio.Bus.Radio` and `config.radio` stay in the build. **Original
  entry follows.**
- **The radio is the one web system with no port and no row - and the TRIGGER FIRED, 2026-08-16.**
  The user asked for it directly: *"אני רוצה שכן נבדוק אם יש אופציה לממש את הרדיו."* It was deferred
  during U27 as the only system carrying a network dependency; twelve of the web's thirteen audio
  modules shipped and this is the thirteenth. **The deferral was never a claim that it cannot be
  built** - that impression needed correcting - it was a demo-risk call, and a submission recording
  makes that risk concrete rather than theoretical.

  **⏸ ON HOLD BY THE USER, 2026-08-16, AFTER the feasibility was measured** - *"בינתיים נכניס את
  הסעיף הזה על hold. אולי נממש בהמשך."* Not dropped and not scheduled. The measurements below were
  taken in the live Editor before the hold, so a later pass starts from evidence rather than from
  this paragraph's old guess. **Trigger to pick it up: U30a (the macOS build) and the video are
  done and there is time left before 1 Oct 2026.** Never before them - a feature is not worth a day
  taken from a build that has to exist.

  **Feasibility, MEASURED 2026-08-16 (it was inferred before; now it is not).** Three findings, in
  the order they bite:

  1. **The streams are alive.** `curl` against `ice1/ice2/ice4.somafm.com` → `HTTP 200`,
     `Server: Icecast 2.4.0-kh22-Soma1.7`, `Content-Type: audio/mpeg`, `icy-br: 128`.
  2. **SomaFM rejects Unity's default User-Agent.** The first Editor attempt died in 0.9 s with
     `result=ConnectionError err="Received no data in response" bytes=0` - identical to `curl`'s
     exit 52 with no UA. `SetRequestHeader("User-Agent", "Mozilla/5.0 …")` fixes it. **Without this
     line the next investigation concludes "the streams are blocked" and is wrong.**
  3. **With that header the bytes flow and Unity still cannot play them - the wall is real.**
     A 21-second poll of `UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG)` with
     `streamAudio = true`: `bytes` climbed 344 816 → 584 307 (≈128 kbps, i.e. real time), `result`
     stayed `InProgress`, `error` stayed `none`, and **`downloadHandler.audioClip` was `null` at
     every single sample**. The console says why:
     `Playback of audio clip not yet possible; headers are done, 489012/? (76.16%) bytes downloaded
     but size is still not known` followed by `Cannot create FMOD::Sound instance for clip ""
     (FMOD error: The HTTP request timed out.)`. **The `?` is the whole finding**: FMOD will not
     start without a denominator, and a live stream has none. This is a loader policy, not a codec
     limit - MP3 frames are independently decodable and endless input is fine for the format.

  **The shape that works, and why each piece is there:** an HTTP reader that never waits for
  `Content-Length` → **NLayer** (MIT, pure C#, on NuGet - v2.0.1 verified to exist) decoding frame
  by frame → a **lock-free ring buffer** → `AudioClip.Create(..., stream: true)` with a
  `PCMReaderCallback` → an `AudioSource` on the Radio bus. NLayer's virtue is not better decoding;
  it is that **it never asks how big the file is**. `RadioUnityStream` on GitHub is a working
  reference of this architecture but its licence is personal/non-commercial - take NLayer directly,
  not the repo. Paid assets (AudioStreamIce, Radio PRO) are **out**: no money, ever.

  **Half of it is already built and idle.** The `Radio` mixer group and its exposed `volRadio`
  exist (U27), `GameAudio.Bus.Radio` is declared, `config.radio` - five stations, volumes,
  `[` `]` `\`, the 8000 ms timeout - is **already in `theblock-config.json`**, and `GameAudio`
  already polls `VehicleEnterExit.Mode`, so the driving-only rule costs nothing. Missing: a
  `RadioSpec` in the config model (deliberately not declared - see `TheBlockConfig.Audio.cs`), the
  stream reader, and a HUD panel in the `PowerUpChips` mould on the one shared `UIDocument`.

  **Two free de-risks found in passing.** The response carried **no `icy-metaint`** because the
  request never sent `Icy-MetaData: 1` - so the body is raw MP3 frames with nothing interleaved and
  no de-muxing is needed (ask for it only if a "now playing" title is wanted). And the residual risk
  is **sound quality, not existence**: the `PCMReaderCallback` runs on the audio thread, so a lock
  there clicks, and an under-fed ring buffer drops out. The bad outcome is a radio that stutters.

  **⚠ Key clash, and it is silent.** `DayNightCycle.ReadTestKeys` owns `[` `]` `\` - exactly the
  radio's three keys - and carries a comment asserting *"nothing else in this project reads them"*.
  Test Mode only, so a normal scene is clear, but that comment stops being true the day the radio
  lands.

  **Recommendation: build what the web build already behaves like, local-first.** `radio.ts` ships a
  graceful "unavailable" path, so: **local clips as the guaranteed content, live streams as an
  upgrade that degrades to local on any failure.** Same five stations, same keys, same driving-only
  rule. The recording then demos a radio that works with the network unplugged. **The open content
  question is licensing, not code:** the repo has no music it may ship except `rhythm-song.mp3`
  (the dance's own track), so a local station means CC0/CC-BY tracks with attribution, or a
  procedural station off `SfxSynth`, or no fallback at all and a panel that says "unavailable".

- ~~**The dance's arrows are keyboard-only, and U31 has no keyboard.**~~ **DROPPED BY THE USER,
  2026-08-16** - *"חצי ריקוד - לא רלוונטי לדעתי יכול להוריד."* True in the original too, so it was
  never a regression, and the desktop target this port ships to has a keyboard. Four tappable lanes
  are still ~20 lines on the existing UI Toolkit panel if it is ever wanted. ~~**The only trigger
  left is U31 actually shipping to a device.**~~ **U31 was dropped the same day, so this entry has no
  trigger at all** - it survives only as the note that would matter if the user ever does try a
  device build privately: on a tablet this is not polish, it is M2 being unplayable.

- **Foliage collides.** `noCollidePatterns` matches node or material names and a merged district has
  neither, so each district takes 2-4 whole-mesh colliders with the palms inside them - the same
  hole the web build has. **The fix is now cheap**: U11's `Compact()` already builds a mesh from a
  chosen subset of submeshes, so a foliage-free COLLIDER mesh is that call again with the foliage
  submeshes dropped, assigned to the `MeshCollider` instead of the `MeshFilter`. The cost is what
  holds it back - a second full copy of every district's geometry in memory, for canopies that start
  above head height and that neither Joe nor a vehicle can reach today. **Trigger:** anything that
  gets a player INTO a canopy (U23's helicopter is the obvious one), or a U30 profiler pass that
  makes it a memory question rather than a gameplay one.

- **~800 ms frame hitches, intermittent, and NOT U18's.** User-flagged 2026-08-15 during U18's
  play-test, their explicit call to defer and treat properly later. **Measured: max frame with
  nobody run over 818 ms, max frame across a full run-over 839 ms** - the reaction adds noise, not
  cost, so the newest feature is ruled out and should not be the starting point. It sits on top of
  the 42 ms steady frame the user already flagged at U16, and the user played again straight
  afterwards with no hitches at all, so it is intermittent rather than constant. Almost certainly
  the same event as the green blocks below - a second-long stall and a GPU under pressure are one
  symptom, not two. **Untested hypotheses, in order and none of them checked:** runtime shader
  variant compilation (synchronous in the editor, and every material seen for the first time pays -
  a fresh district or a fresh character face would fit the "sometimes" exactly); a GC spike against
  a 1,157 MB mono heap; district or crowd instantiation. **Trigger:** it becoming reproducible, or
  U30's perf pass, which owns this properly. **First step:** get the user to say WHEN it hits
  (driving into a new district? crowd loading? first run-over of a session?), then run the Profiler
  over that window - the answer wanted is a function name, not another guess.

- **RESOLVED 2026-08-15 (pending a play-test): resident texture memory cut from 2,190 MB to
  534 MB.** The two entries below are the same event, and the guess written into the second of them
  - *"Mipmap Streaming with a budget is the Unity mechanism U15 did not need to reach for"* - was
  right. **Measured in Play, before and after:**

  | | before | after |
  | --- | --- | --- |
  | `Texture.currentTextureMemory` | 2,190 MB | **534 MB** |
  | `nonStreamingTextureMemory` | 2,190 MB | **453 MB** |
  | `Profiler.GetTotalAllocatedMemoryLong` | 3,146 MB | **2,685 MB** |

  **It costs no visual quality, and that is measured rather than argued: `desired` == `current` ==
  534 MB.** Unity is being handed every mip the renderer asked for and is not touching the 1,024 MB
  budget, so nothing anywhere is being reduced - the 1,656 MB saved is mip levels finer than the
  screen can resolve, which were resident only because nothing had ever told Unity it could drop
  them. `maxLevelReduction 2` bounds the worst case if memory ever does get tight.

  What was done: `streamingMipmaps` in `GeneratedTextureImporter` (in the importer, not the .meta,
  for the reason that file already documents - a Library wipe would otherwise restore the defaults
  and put the memory back), `QualitySettings` streaming on at 1,024 MB with `addAllCameras` so U14's
  map RenderTexture camera votes on mip density too, and a new **The Block → Reimport Generated
  Textures** because changing a rule in that importer does nothing to the 241 textures already in
  the Library. **Halving `MaxTextureSize` to 8192 was considered and NOT done** - that one really
  does cost facade sharpness, and after this it is not needed.

  **The remaining suspect is now startup, not memory.** `FrameWatchdog` (new, below) caught **1,513
  ms at t=6.1 s and four hitches inside the first 15 s**, then a steady state of **20.7 ms mean /
  65 ms worst** with texture memory flat at 535 MB throughout. So the hitches cluster where the
  world and the crowd load, and they are bigger than the 800 ms this entry recorded. Next step is
  the Profiler over the first 15 seconds specifically - not a memory hunt.

- **`Assets/Scripts/Core/FrameWatchdog.cs` exists now, and it is permanent.** Auto-installs on Play
  like `SkinWatchdog`, editor-only. One quiet line every 10 s, a full census on any frame over
  300 ms: frame mean/worst, the texture triple (`current` / `desired` / `nonStreaming`), Unity's
  allocated and reserved, and the streaming budget. **The triple is the instrument**: with streaming
  off all three are identical and say nothing; with it on, `current` pinned at the budget while
  `desired` climbs is the machine reporting it is out of room *before* anything breaks on screen.
  The green blocks were never going to be caught by a screenshot.

- **Green blocks tiled over the whole world, and the Editor's own toolbar corrupted with it.**
  User-flagged 2026-08-15 during U18's play-test; cleared on an editor restart. **Measured, and it is
  not an allocation this project makes:** texture memory sat flat at **1,634 MB** across 45 s of Play
  and across 16 run-overs in one frame (Texture2D count 1,346 → 1,346, texture memory 1,634 → 1,634
  MB; only material COUNT moved, 756 → 851, which is the fade's per-body clones and carries no
  texture memory). The decisive evidence is in the screenshot rather than the numbers: **the Game
  view's own toolbar icons were drawn as coloured blocks too**, and the minimap RenderTexture came
  back magenta. Game draw calls cannot reach the editor's IMGUI atlas - only a GPU-level failure
  can, which makes this Metal under memory pressure on a 16 GB M3, not a leak. **Trigger:** it
  recurring, or U30's perf pass. **First step:** ask whether it predates U18 - if it does, this is a
  standing environment ceiling and the answer is to cut resident texture memory again (Mipmap
  Streaming with a budget is the Unity mechanism U15 did not need to reach for), plus the shadow
  atlas the console complains about every session ("18 shadow maps in a 2048×2048 atlas"). **Do not
  start by suspecting the newest feature** - that was tested here and came back clean.

- ~~**On foot beside a pole, its lights do not appear to change.**~~ **FIXED AND USER-CONFIRMED,
  2026-08-16** - *"סוף סוף עובד."* Deferred 2026-08-15; the trigger that fired was the user simply
  asking again. **The hypothesis this entry left behind was half right and that half cost three
  passes.** It guessed one-sided culling - correct - but blamed the VIEWING ANGLE, so the two fixes
  it suggested (a rear lens set, then domes for grazing angles) were both built and neither worked,
  because the lens was culled from *every* angle: it was wound inside-out, facing into the housing.
  The final symptom was the opposite of the report - **grey head-on, coloured from the side** - and
  that flip is what finally identified it. Both suggested fixes are shipped anyway (rear lenses,
  domes) plus the two that actually mattered: `EnforceWinding` and `_Cull Off`. See the U17 row.
  **The lesson for the next deferred entry: a hypothesis written in the ledger is read as a head
  start, so it must name the ONE measurement that would falsify it.** This one named
  `TrafficLightPole._shown`, which was never the doubtful part.

---

## Decisions log

Dated one-liners. These are settled - do not re-litigate them without the user reopening.

- **2026-08-17** - **TIER 8 IS CLOSED: two confirmations and four cuts, in one message.** ✅ **U35c**
  (police H145) and ✅ **U35d-pre-3** (the in-vehicle arrest) are user-confirmed. ✂ **The GPS route
  is REMOVED from the game** - *"תוריד את הקו התכלת מהמפה, לא צריך את הפיצר הזה"* - deleted rather
  than defaulted off (`GpsRoute.cs`, `MapView.SetRoute`/`DrawRoute`, `Progress.GpsRouteOn`, the
  Settings row, `MapRegistry.NearestGuide`); `RoutePlanner`/`RouteGraph` stay, the police drive on
  them. ✂ **U35e (stunt jumps + Cinemachine) is DROPPED** - *"we decieded we do not need that so do
  not mention it again"* - so **no Tier 8 unit is scheduled at all**. ✂ **The radio is DROPPED**, not
  held - *"רדיו - גם תוריד, לא כזה חשוב."* ✂ **U35d-pre needs no play-test**, superseded by pre-3;
  that is a list removal, not a code removal - pre-3 is a rewrite of its ramp. **Next action is
  U30a.**

- **2026-08-17** - **Police deploy NEAR the player, the web's way; U19's "always from the station"
  is reversed.** Third user report on the same feature (*"הפיצר של המשטרה פשוט גרוע… המשטרות פשוט לא
  באות אליי"*). Bays are used only within 120 m of you; otherwise the same car is placed on a
  hidden street 50-90 m off, and a cop that loses you is re-dispatched (`RelocateAfter = 0` turns
  that off). Crime → BUSTED measured ~6.5 s from ~36-45 s. **Cruisers are damage-immune** on the
  user's own ask (*"לא יוצא אש ממכוניות של משטרה"*). Chase numbers untouched. U35d-pre-2.
- **2026-08-16** - **THE PIVOT: the Unity build is the submission, and the port has a deadline.**
  The user's framing: the project began in three.js and moved to Unity mid-way, to learn a second
  engine and to use Unity's advantages. This **reverses** two things this ledger and `CLAUDE.md` had
  said since 2026-08-12 - *"a side project with no deadline"* and *"the original's `main` stays
  submittable"*. What changes: every gap against the web build is now a graded decision rather than a
  nice-to-have, and the deliverable stops being only the game - a video, a repo, a kanban board and a
  zip are the graded artifacts. What does NOT change: the original repo is still never touched beyond
  `export-config.mjs`, and the instructor is invited to both repos so the pivot is presented rather
  than hidden.
- **2026-08-16** (U31) - **iPad is dropped from the port.** *"ipad אנחנו כנראה נראה מזה… זה לא
  רלוונטי להגשה."* The target is macOS, full stop. `CLAUDE.md` had always ranked iOS as "a wanted
  bonus, never a constraint on design", so this changes no design decision retroactively - it only
  stops future units from paying a tax for a platform nobody will run. **The iOS module, the
  `PlayerSettings` section and unused touch handling all STAY**: they cost nothing idle, and removing
  them would be effort spent making a retry harder. The user may still build to a device privately
  for the engine experience; that is not a unit and nothing waits on it.
- **2026-08-16** (U35b) - **No Blender split of the Mustang, and a wreck has to say why it refuses.**
  Two calls the user made while U35b was being built. ① The hero car's `.glb` groups by MATERIAL, so
  layer ③ would have needed a Blender re-split to find a bumper - **declined**, because `CarBuilder`
  rebinds paint by the material name `CarPrimaryColor` and a re-export that renames anything breaks
  the car in every screenshot. The three lot cars and the cruiser shed the parts they already have as
  nodes; the Mustang sheds nothing and the build log says so. The split stays available as its own
  sub-unit if the video ever wants it. ② A car that has exploded **cannot be entered, and the player
  is told** - *"אם הוא ינסה להכנס אליה הוא לא יוכל ויהיה כיתוב שהוא לא יכול להכנס למכונית כי היא
  מפוצצת"*. It landed in U28's existing `IEnterable.EntryRefusal` socket rather than a new mechanism,
  which is the same rule that put the locked Huey's line there: **one predicate feeds both the prompt
  and the key**. ③ The player is put down beside the burning car rather than thrown from it - the U35a
  decision that a car does not eject you was left standing, deliberately, rather than reversed by a
  feature added afterwards.
- **2026-08-16** (U35, the same day, LATER - this supersedes the entry below it) - **Tier 8 is cut to
  seven rows and one of them is redesigned.** After confirming U35a's bike follow-up (*"U35a עובד טוב
  אתה יכול לסמן כן"*) the user went through the remaining list and made four calls in one message:
  **① the skid marks come out of U35e** (*"סימני צמיגים נמחק זה לא מענין אותי"*) - the handbrake goes
  with them, since it existed to make them worth drawing; **② U35f side jobs is dropped outright**
  (*"עבודות צד גם לא מעניין תוריד"*) - which the ledger had already called the weakest row against the
  selection rule, so the cut agrees with the rule rather than overriding it; **③ U35g the garage is
  wanted** (*"קניית רכבים - מגניב"*) **and re-centred on a design of the user's own** - a Blender-built
  **open** garage you drive INTO, where you paint **the car you arrived in** and drive out in that
  colour, persisted per vehicle. That deletes U13's interior-teleport pattern from the plan and makes
  Blender a requirement of the unit rather than an optional bolt-on; **④ U35h must reuse what exists
  before modelling anything** (*"צריך לוודא אם יש אופציה לקחת assets קיימים… כמו הרמזור או ספסל"*).
  **Measured the same day, and the answer is one prop:** the 233 traffic-light poles are separate
  GameObjects with their own colliders and are a free win; benches, bins and bollards do not exist as
  objects at all - they are baked into the district meshes - so half ② of that unit is new Blender
  work, not reuse. **The shape worth keeping:** three of these four are the user pruning a list they
  accepted whole two entries ago, and the pruning tracks the selection rule almost exactly.
- **2026-08-16** (U35) - **The showcase list is chosen: eight additions, five scheduled, three
  backlog.** ⚠ **Amended by the entry above** - U35f is dropped, U35e loses its skids, U35g is
  redesigned. Proposed in one session, accepted whole by the user - *"בוא נוסיף את כל מה שאמרת
  לתוכנית. נממש את הדברים לאט לאט."* Scheduled, in order: **U35a** ragdolls · **U35b** vehicle damage ·
  **U35c** police heli at 3★ + GPS route · ~~**U35d** weather~~ (dropped 2026-08-17) · **U35e** stunt jumps + skids +
  Cinemachine. Backlog: **U35f** side jobs · **U35g** garage · **U35h** breakable props. **The user
  attached a third rule to Tier 8 in the same message and it governs every one of them**: *"חשוב שלא
  נדפוק את הביצועים… ה-assets צריכים להיראות טוב… שהמשחק לא ייתקע"* - measured on the Player against
  the U30b baseline, new assets through the U15 gate and eyeballed before wiring, nothing spawned in
  bulk on one frame. The full mechanism, off-switch, perf budget and Blender note per row are in
  Tier 8. **Slot unchanged: after U30a/U30b, before the recording.**
- **2026-08-16** (U33) - **The port may now GAIN things, and a gain ships switched off.** ⚠ **The
  example was wrong and the rule is not** - this entry opened *"a day/night cycle is in no version of
  the web build"*, and the web build has one at `enabled: false` (corrected 2026-08-16, see the U33
  row). The rule stands on its own and U19e is now the cleaner example of it. It is allowed in
  because "Unity-idiomatic, same game" was
  never a ban on additions - but an addition that is always on silently re-opens every visual
  judgement made in U11 through U27, because the screenshots the user approved stop reproducing. So
  the rule this unit sets for whatever comes next: **an addition defaults to off, and its off state is
  the old behaviour reproduced by replaying the scene, not by feeding neutral values through the new
  code path.** `DayNightCycle` does not write a neutral sky when it is off - it does not write at all,
  and URP schedules no post pass. That is what makes "costs nothing" a fact rather than a hope.
- **2026-08-16** (U33) - **Day length is 48 real minutes**, GTA V's own pace. The user played the
  24-minute version first and asked for half speed. One constant, `DayNightBuilder.DayLengthSeconds`
  - written through `SerializedObject` rather than left to the C# field initialiser, because the
  scene had already serialized the old value and would have ignored a re-tune.
- **2026-08-15** (U19b) - **A mechanism and its pacing are one decision, and U19 made them
  separately.** "Heat decays unconditionally so three stars with an empty screen is impossible" is a
  good rule. "Cruisers park at the station, so a response has a travel time" is a good rule. Together
  they are a pursuit that cannot happen, and neither reads as wrong on its own - the bug is only
  visible when the star's lifetime (~6 s) is put next to the drive (15-60 s). **Whenever a unit adds
  a duration to something, re-check every clock that was tuned before it existed.** Both faults found
  this way - the latch and the give-up cap - were arithmetic, not screenshots.
- **2026-08-15** (U19b) - **Changing a C# default does NOT change a value already serialized in the
  scene.** `RunOverCooldown` was raised 0.5 → 3 in `PoliceTuning.cs` and the live component kept
  reading 0.5, because Unity constructs the object and then overwrites it from the scene YAML - new
  fields take their initializers, existing ones do not. Silent, and it would have shipped one pass
  through a crowd as three stars. Fields added in the same edit (`BreakContact`, `ShedStep`, …) came
  through correctly, which is what makes it easy to miss. **Read the value back off the live
  component after retuning anything already in a scene**, and write the fix through `SerializedObject`
  + `MarkSceneDirty` + `SaveScene`.
- **2026-08-15** (U17b) - **One origin for every car prefab: body centre in XZ, contact patch in Y.**
  A car that can be swapped for another has to be placed at the pose of the thing it replaces, and
  three builders were pivoting three different ways - `TrafficCarBuilder` on the body centre,
  `CarBuilder` on the artist's pivot, the lot on the model's own bottom. Agreeing on one origin turns
  every swap into an assignment, which is why the hijack measures 0.000 m rather than "about right",
  and it removes the ride-height term the web build re-adds every frame.
- **2026-08-15** (U17b) - **A stolen car is RETIRED, not teleported.** The web build hunts up to
  thirty random lane points for somewhere far enough away to hide the recycled car, because its pool
  is fixed InstancedMesh slots and a car can never stop existing. Unity's pool already retires and
  re-places on a sweep, from a ring outside the view cone, so `Claim` hands the slot back and the
  mechanism that was already running does the work. `hijack.recycleMargin`/`recycleTries` are left
  undeclared on purpose: a config field that nothing reads is a claim about a mechanism that is not
  there.
- **2026-08-15** (U17b) - **Facing corrections between two config conventions are baked at build
  time, never computed at runtime.** `lotCars`, `vehicle.cars` and `traffic.models` each carry their
  own `modelYaw`, and the traffic one is the opposite convention to the other two. The corrections
  live on `LotCar.DriveRotation` and `TrafficCar.DriveRotation`, resolved where both numbers are in
  view. They currently come out as the identity - which is exactly the trap: copying a rotation
  across works today and silently breaks the day someone re-tunes one yaw.
- **2026-08-15** (U17b) - **An unwritable material property fails SILENTLY, so the write has to say
  which property it used.** `CarBuilder` set `_BaseColor`/`_Color`; glTFast imports a shader with
  `baseColorFactor`; `Material.SetColor` on a property that does not exist is a no-op with no warning,
  and the Mustang wore the wrong paint for four units without anyone being told. `VehicleMaterials`
  now owns the branch (and its gamma, which differs between the two names), returns the property it
  wrote, and the build log prints it.
- **2026-08-15** (U17b) - **The one rigged car is the check on the three stated ones.** Tesla, Audi
  and Avenger have no wheel nodes at all - the web build's Blender pass welded them into the body -
  so their axles are stated from the body box. That would be unfalsifiable on its own, so the
  Mustang's build log prints what the stated rule WOULD have produced beside what its rig actually
  measures: radius 0.387 against 0.379, wheelbase ±1.695 against ±1.688, track ±0.953 against ±0.992.
  A stated number with a measurement standing next to it is a different thing from a guess.
- **2026-08-15** (U18) - **The clip's root motion IS the knockback, and it is the only root motion
  in the project.** Every other clip a character plays has its travel discarded because a script
  owns the position - U7 settled that for Joe and U16b for the crowd. The hit is the deliberate
  opposite: the limbs, the tumble and the landing are frame-exact by construction, and code supplies
  only the two things the clip has no opinion about (a 1.1 m vertical arc, since the clip's own
  vertical is flat, and a speed-scaled push). Reproducing the throw in code would be two clocks for
  one body, which is exactly what the web build rejected.
- **2026-08-15** (U18) - **Root motion is HARVESTED onto the pedestrian's transform, scaled by the
  visual child's scale.** The Animator sits on the visual child because that is where the model is,
  and a character that did not import at 1.70 m is scaled there too - so plain `applyRootMotion`
  slides the body out from under its own collider, its culling and its seed. The scale factor is not
  cosmetic: Humanoid retargeting produces root motion in the TARGET avatar's units and Remy's avatar
  really is 4.20 m tall, so his knockback arrives 2.5× too long in local units and is then drawn
  0.405×. Multiplying by the child's scale is what makes the transform travel as far as the body
  appears to; for anyone who imported at 1.70 m the factor is 1.
- **2026-08-15** (U18) - **The throw angle is measured off the clip, never ported.** The web carries
  a hand-tuned `clipYawOffset` of −85.8°; `clip.averageSpeed` reads **+85.1°** here. Same physical
  angle, opposite sign - a clean confirmation of the handedness rule from a direction nothing else
  has tested, and a number nobody has to decide the sign of ever again. `Pedestrian.ThrowYaw` is the
  one implementation and the importer logs it through the same function, so the two cannot drift.
- **2026-08-15** (U18) - **The victim's window is found by watching the root move.** Mixamo pads a
  one-shot clip with seconds of idle - `Hit_By_Car.fbx` is 145 frames and the body stands still for
  79 of them - and the reaction's phases hang off the clip's LENGTH, so importing it whole would
  push the lie and the fade out behind 2.6 s of nothing. The threshold is a fraction of the clip's
  own peak frame speed rather than an absolute, so it survives a re-export. The original trims the
  same clip in Blender for the same reason.
- **2026-08-15** (U18) - **ONE detector, and it is the bumper box.** The original shipped two - the
  box, and a separate radius scan in `crime.ts` that decided whether to call the police - and they
  fought, because the radius scan skipped anyone already yielding to the car and wanted the victim
  within 1.8 m of the vehicle CENTRE while the box downs them at ~3.2 m. Blood on the road, usually
  no stars. That call is dead upstream and is not ported back: U19's wanted level reads
  `RunOverSystem.Victims`.
- **2026-08-15** (U18) - **The hit fires a physics step BEFORE contact, and the gate is what keeps a
  crawl honest.** A person's capsule is solid, so a car at 20 m/s would hit a wall for one step
  before anything downed them; the box is padded by the victim's own capsule radius plus the
  distance the vehicle covers before the next step. Below 12 km/h none of that happens and the
  capsule stays solid, so nudging someone bumps into them rather than gliding through. The web
  needed Rapier interaction groups to reach the same place.
- **2026-08-15** (U18) - **A component may only destroy what it made.** `CrowdSpawner.Bind` cleared
  **every** child of the Crowd object to sweep stale bodies after a domain reload, and quietly
  deleted the stain pool `Blood` builds on that same object - surfacing as a
  `MissingReferenceException` three seconds into a run-over, nowhere near the cause. It now destroys
  only children carrying a `Pedestrian`, and `Blood` keeps everything under one child it sweeps by
  name.
- **2026-08-15** (U18) - **A borrowed clip whose bone namespace differs must CREATE its own avatar.**
  `JoeClipImporter` copies Joe's avatar into every clip he borrows, which works because they came out
  of one Mixamo upload. `Hit_By_Car.fbx` did not: it is `mixamorig:Hips` against the crowd's
  `mixamorigN:Hips`, Copy From Other matches by NAME, and it fails outright. Create From This Model
  plus Humanoid retargeting plays one clip on all six bodies regardless of what their bones are
  called - the same namespace trap the web build renamed tracks by hand to escape.
- **2026-08-15** (U18) - **The stain is a lifted quad, not a URP Decal Projector.** A decal would
  conform to the road properly and costs a Decal renderer feature plus the depth it needs, on a
  frame the user has already flagged. Pavement is flat where people walk, so 2 cm of lift looks the
  same for free. Both blood textures are drawn in code rather than authored: two small procedural
  textures, no LFS, and the shape stays tunable - the same call the web made with its canvas.
- **2026-08-15** (U16) - **The pavement is not enforced, it is the only thing that exists.** The web
  build's pedestrians drift into the road because nothing there knows a road is a thing: a 4096²
  top-down material mask, a 67 MB GPU readback, a session-long boolean grid, straight-line movement
  between sampled points, and - when that was not enough - eighty rectangles and strips recorded by
  hand beside the pavements rather than on them. **None of it is ported and none of it is replaced.**
  All 12.7 km of `config.traffic.network` is carved `Not Walkable`, which disconnects the two sides
  of every street, so being in the road is not unlikely, it is unrepresentable. This is the answer
  to the standing remark for U16, and it is the strong form of it: the mechanism is not a better
  version of the web build's, there is no equivalent of the web build's at all.
- **2026-08-15** (U16) - **A crossing is a hole in connectivity, not a scripted walk.** With the
  carriageway carved, the only route to the far pavement is a `NavMeshLink` at a zebra, so an
  ordinary wanderer crosses at a zebra because there is nowhere else - no pedestrian is assigned to
  a crossing at all. The web build's crossings are real (`traffic.ts`) but serve two dedicated
  pingpong walkers each while the rest of the crowd ignores roads entirely. `autoTraverseOffMeshLink`
  is OFF so `Pedestrian` owns the kerb, and `Crossing.Gate` is the seam U17 hands the light to -
  the same shape as `CrossingSpec.mayCross`.
- **2026-08-15** (U16) - **The crowd is a pool that follows the player, not a population.** The web
  build creates several hundred pedestrians at boot and freezes them individually past 90 m, because
  a three.js pedestrian is cheap to hold and dear to create. A NavMeshAgent is the reverse, and a
  frozen one still sits in the avoidance solver. 40 live agents that recycle from behind you to
  ahead of you - rerolling face and shirt each time - read denser than 400 frozen ones and cost a
  fraction. It also means `npc.config.ts`'s `paintedZones`, `strips` and `zones` have no port: where
  people can stand is the NavMesh's answer now.
- **2026-08-15** (U16 play-test) - **The stutter was the spawn burst, not the crowd. Measured:
  frame time with 60 agents on = frame time with them off = 20.0 ms.** So "too many people" was
  never the fault; 90 `Instantiate`+`Warp`+`SetDestination` in one `Awake` was, and the vendor's
  five LODs multiplied it (33 skinned meshes per person, all posed every frame regardless of what
  the LODGroup draws - 2,960 SMRs for 90 people, 747 visible). Spawn is trickled 6 per sweep,
  LODs 1/3/4 are DESTROYED at build (not disabled - a disabled SMR is still owned by the animator),
  and the animator culls completely off-screen. `AlwaysAnimate` was tried in between and was
  wrong: it doubled the cost and fixed nothing, because the "exploding pedestrian" was an SMR that
  had never been posed drawing at bind pose on LOD swap, and removing those SMRs is the fix. The
  user flagged the unit low-performance for later; the number to beat is density, not frame time.
- **2026-08-15** (U16 play-test) - **`Build World` no longer bakes; `Build World + NavMesh (slow)`
  does.** The 0.25 m bake froze the editor long enough, twice, that the user force-quit it, and a
  main-thread freeze with no progress bar is indistinguishable from a crash. At 0.4 m the whole
  bake is ~3 s, and the split is kept anyway: the fast build lifts the previous navigation out of
  the old root and re-attaches it, and never sweeps `Assets/Navigation/Generated/` - which it did
  once, deleting the zebras' mesh and material out from under 230 kept crossings.
- **2026-08-15** (U16 play-test) - **`GroundY` is "lowest hit that is not the ground plate."** The
  first version took the lowest hit outright, which under every district is the plate at −0.05,
  2 cm below the street at 0 - and a zebra painted there z-fights up through the district mesh
  as bars of that mesh's OWN texture. Orange stripes, in this case. It reads as a material fault
  and is a 5 cm height fault; check the height before the shader.
- **2026-08-15** (U16) - **U17 inherits U16's traffic graph; it must not build a second one.**
  `config.traffic` is ported in full (`TrafficSpec`, `StreetSpec` + a union `JsonConverter`,
  `LightsSpec`) and `WorldBuilder.Navigation.cs` already builds the 97-node graph, finds the 70 lit
  intersections and places the 230 crossings. U17 adds cars, lights and phases on top and replaces
  `Crossing.IsClearOfTraffic` with the controller.
  **Settled harder at U17:** the graph is not merely shared, it is derived by the traffic pass -
  which runs on EVERY build - and passed into `BuildNavigation`. The navigation pass no longer
  builds one at all.

- **2026-08-15** (U17) - **How many cars is measured, not chosen.** The web build's own two numbers
  are 130 cars and 12,759 m of network: one car per 98 m. `TrafficSystem` counts the metres of
  centreline inside the cull radius every sweep and asks for that many, so downtown is busy and the
  edge of the map is empty without either being typed in. **This replaced a fixed count of 32, and
  the failure is the point:** 32 came from an estimate that a 160 m disc holds thirty streets'
  worth of road; measured, the disc around the starting lot holds 1,230 m, so 32 is one car per
  38 m - jam density at signalised junctions - and the city gridlocked in under a minute with 31 of
  32 cars stopped. At the derived count it runs indefinitely with nobody reaching the stuck escape.
  A guessed constant that happens to be wrong is indistinguishable from a broken algorithm until
  someone measures the thing it was a guess about.
- **2026-08-15** (U17) - **The street graph is build output, not load-time work.** `buildPath`
  raycasts the ground once per two metres of every path - 6,590 rays over this network - and the web
  build pays that before its first frame. `Assets/Traffic/Generated/TrafficNetwork.asset` holds the
  same numbers, so the runtime casts no ray for traffic ever. U19's police wants the same asset.
- **2026-08-15** (U17) - **⚠ `GroundY` could return a ROOF, and 230 samples were not enough to show
  it.** U16's rule was "the lowest hit that is not the ground plate", which is right wherever a
  district has street geometry - and downtown is one merged mesh with none under parts of its
  avenue, so the only non-plate hit there is the building overhead. At 230 crossings nothing landed
  on one; at 6,590 traffic samples, nine did, at 6.4-10.1 m. A street is never more than 2 m above
  the plate, so anything higher falls back to the plate, and single-sample spikes are flattened
  against their neighbours afterwards.
- **2026-08-15** (U17) - **⚠ The fast `Build World` was silently deleting the NavMesh bake.**
  `ComponentUtility.PasteComponentValues` does not reliably carry `NavMeshSurface.navMeshData`, and
  when it does not, everything looks fine: the surface is enabled and correctly configured, the
  asset is still on disk, and `NavMesh.CalculateTriangulation()` returns zero vertices. The only
  symptom is a city with no pedestrians in it and an empty console. Found by counting the crowd
  during U17's play-test, not by seeing it. The baked asset is loaded from disk on re-attach now.
  **The general lesson: a component copy is not a way to preserve a reference.**
- **2026-08-15** (U17) - **Cars stop behind the zebra; the original does not.** It stops a car
  `stopLineDist` (8 m) from the junction centre and paints the crossing at 10 m, and a car's
  position is its body centre - so the lead car of every queue parks its back half across the
  crossing. That is not a design decision there: `crossingSetback` exists so the crossing's kerb
  ends clear the light POLES, and the car was never measured against it. Scar tissue, not intent.
- **2026-08-15** (U17) - **Kinematic while driving, dynamic when rammed.** Thirty vehicles solving
  contacts was never on the table in Rapier, so the web build's traffic is kinematic full stop and
  the player bounces off it. Kinematic stays the default here for the same reason - a car following
  a baked lane costs one `MovePosition` - but the exception Unity can afford is per-car: a hit above
  an impulse threshold flips that one car to a real Rigidbody, and it stays a wreck the rest of the
  traffic queues behind until the slot recycles. Bounded by construction to cars the player actually
  hits, and switched off by one serialized bool if it ever misbehaves.
- **2026-08-15** (U17) - **The light pole uses the SHIPPED model, not the source asset.** A
  deliberate exception to port rule 3, and the reason is memory: `traffic_light__animation.glb` is
  16.5 MB carrying four 4096² textures for a 4.5 m pole placed 233 times, while the web build's
  dieted copy has the same four meshes at 512². Rule 3 exists to avoid a pointless second lossy
  pass; on a pole seen from 20 m that pass is invisible and the win is ~50×. It needed
  `tools/glb-webp-to-png.py` because the shipped file requires `EXT_texture_webp` - U13's trap,
  U13's tool.

- **2026-08-15** (U7b) - **The 32 units are not a complete inventory of the game.** Swimming is in
  `config.ts`, in `player.ts` and in the shipped build, and no unit owned it; it surfaced only
  because the user asked an unrelated question about animations. The sequence is a plan, not a
  spec - `config.ts` is the spec. Filed as `U7b` rather than renumbering, and the same audit has
  not been run against the rest of the config.
- **2026-08-15** (U7b) - **One collider, two answers: `excludeLayers` is Unity's `obstacleFilter`.**
  The shore wall must stop a car and pass a swimmer. The web build carries a predicate the character
  controller calls per candidate obstacle; Unity puts the same idea on the collider itself, and
  `WorldBuilder` had already parked that wall alone on Ignore Raycast for an unrelated reason (a
  downward probe was reading its top as ground). Excluding that layer on the player's
  `CharacterController` is the whole fix - no new layer, no marker component, nothing else on the
  layer to catch by accident. **If anything else is ever put on Ignore Raycast, this becomes wrong.**
- **2026-08-15** (U12 repair) - **`config.camera.far` is a three.js budget, not a design; the fog it
  came with is the design.** `far` 320 m, `fog` 70→280 m and `background` are ONE mechanism: the haze
  dissolves geometry into a sky painted the identical `#9FB8D4` long before the plane reaches it. The
  port took the plane and left the fog, so the clip ran naked and sliced the skyline in a hard arc.
  `config.streaming` (unload past 380 m) is the proof the distance was a budget. Unity draws to
  1500 m; `World.Atmosphere` owns that number AND the fog range together, and rescales the config's
  own near/far RATIOS onto it so the haze thickens at the same fraction of the view it always did.
  Never set one without the other.
- **2026-08-15** (U12 repair) - **The ground plate is not drawn where the sea is.** U12 kept the
  visual plane full-size because "the water is opaque and drawn above it"; the arithmetic says
  otherwise - the swells total 0.37 m of trough against a plate at −0.05, so every deep trough
  exposed green through the ocean in bands that read as a shader fault. The plate's mesh now has the
  sea's rectangle cut out of it. Moving either surface was rejected: the plate's collider is already
  trimmed at the shore and would float above a lowered plate, and the water line is gameplay.

- **2026-08-15** (U15) - **U15 is texture compression, not Addressables.** The row said "ONLY if
  the profiler says so"; the profiler said the problem is format, not streaming - 12.9 GB of the
  13.5 is raw RGB24 that no importer ever touched, and streaming it in chunks would still be
  12.9 GB. Addressables goes back on the shelf until something needs load-time sequencing, which
  nothing yet does. Chosen by the user 2026-08-15 over "record the numbers and skip to U16".
- **2026-08-15** (U15) - **Extracted textures' import settings are derived from the file NAME, in a
  postprocessor.** Editing the TextureImporter after writing the file imports everything twice and
  survives only in the .meta - a Library wipe or platform switch would silently restore defaults
  and put the 13 GB back. `TextureCompressor.AssetName` encodes size and linearity into the name;
  `GeneratedTextureImporter.OnPreprocessTexture` reads it, so the FIRST import is right, forever.
  The sRGB flag is copied from what glTFast itself resolved, never re-derived from material roles.
- **2026-08-15** (U15) - **An ambiguous texture stays uncompressed; the resolver never guesses.**
  Image names repeat inside one .glb, and binding a wall to another wall's normal map is a lighting
  bug that reads as anything but what it is. Name + pixel size + alpha channel narrows; what still
  matches two images is skipped and named in the report. 12 refusals (~110 MB) is the accepted cost.

- **2026-08-15** (U14) - **The map's base layer is a live camera, never a bake.** three.js could not
  afford a second camera, so it rendered the world once at boot and read the pixels back; a Unity
  camera into a RenderTexture costs one throttled pass and shows the world moving. Nothing in the
  port should reintroduce a baked map image.
- **2026-08-15** (U14) - **Runtime UI is UI Toolkit, and the HUD panel is a single shared one.**
  U14 created `Assets/UI/HudPanelSettings.asset` and the `HUD` object; U25, U26 and every later
  overlay extend that panel rather than adding their own `UIDocument` stack.
- **2026-08-15** (U14) - **The map is oriented by the camera, not by a hand-derived transform.** The
  overlay's world→panel maths is written against the map camera's real `transform.right`/`up`, so
  the vectors and the pixels underneath them cannot drift apart. Any new map layer reads the same
  two vectors instead of re-deriving the handedness.

- **2026-08-15** (U13) - **`AssetAliases` corrects real assets too, not just stand-ins.** An entry
  with no `File` keeps the config's own model and applies only the rotation/lift. The distinction is
  load-bearing rather than cosmetic: a stand-in must skip the config's `hideNodes` because those name
  another model's parts, and the real asset must obey them.
- **2026-08-15** (U13) - **Lot cars are GameObjects with per-car culling, not one InstancedMesh.**
  three's instancing is a single renderable with one bounding volume, so nothing culls; Unity
  GPU-instances identical mesh/material pairs by itself and culls each car on its own bounds, plus an
  `LODGroup` that drops them past 180 m. The web build's approach ports as a performance regression.
- **2026-08-15** (U13) - **Lot-car paint is a generated material per colour, never a
  `MaterialPropertyBlock`.** A property block would break the batch and give every car its own draw
  call, which is the opposite of what the web build's per-instance colour buys there. Eighteen
  material assets cover the whole lot, and they are swept like every other generated folder.
- **2026-08-15** (U13) - **A required glTF extension is transcoded, not worked around.**
  `tools/glb-webp-to-png.py` rewrites the embedded WebP and drops `EXT_texture_webp`, because
  glTFast rejects the entire file and the failure surfaces only as "missing". The lot car models have
  no source asset to re-export, which is what makes this the pipeline step rather than a Blender fix.
- **2026-08-15** (U13) - **The interior's lights stay on and the sun stays up.** The web build
  switches both because three's forward renderer charges every light against every fragment in the
  scene; URP culls per object and the room has a ceiling. Only fog and ambient are still swapped -
  those are global in both engines. Scar tissue, not design (port rule 5).
- **2026-08-15** (U13) - **The vehicle wins `E`.** A car parked outside the pizzeria puts the door
  and the driver's seat in range at once; the doorway asks
  `VehicleEnterExit.HasVehicleInReach` and stands down, rather than the two racing on Update order.

- **2026-08-15** - **Every unit opens with "can Unity do this better?"** and closes with the answer
  written into its notes. Not a new decision so much as the 2026-08-12 "Unity-idiomatic, same game"
  call promoted to a per-unit checklist item, because it kept getting remembered only after the fact.
  Same game, better mechanism, better feel. See the standing remark at the top of this file.
- **2026-08-12** - Scope is the **full game**, not a slice. No deadline; resumability matters more
  than speed.
- **2026-08-12** - **Unity-idiomatic, same game.** Where Unity offers a better mechanism than the
  web version's workaround, Unity wins (NavMesh police, Addressables streaming). Same missions,
  same world, same feel.
- **2026-08-12** - **Multiplayer deferred to U32.** `src/mp` + `src/net` (2,263 lines) rides on
  Supabase Realtime; none of that transport carries to Unity.
- **2026-08-12** - **Autonomous units with a checkpoint each.** Build a unit fully, update this
  ledger, commit, report. User play-tests at unit boundaries.
- **2026-08-12** - **Desktop (macOS) is the priority target**; iPad is a wanted bonus, never a
  constraint on design. **SUPERSEDED 2026-08-16: the bonus was declined and U31 is dropped** - macOS
  is the only target. See the 2026-08-16 entry at the top.
- **2026-08-12** - **No money spent, ever.** Unity Personal only. No Unity Cloud, no Unity AI
  (it bills credits), no Asset Store, no paid LFS.
- **2026-08-12** - Transport for MCP is **HTTP Local**; the remote option requires a Coplay API key
  and is off the table for the same reason.
- **2026-08-12** (U1) - **The facade tint is a material asset, not code.** The web build recolours
  `facade_5` in code at load because it cannot author materials; Unity can. `Facade.mat` costs
  nothing at runtime and is editable without a rebuild. Unity wins, per the rule above.
- **2026-08-12** - **Handedness is X negation.** `Convert.Pos = (-x, y, z)`, `Convert.Yaw = -y`.
  Established empirically on five district assets plus a landmark gap measurement, not assumed.
  Closes the biggest open risk in the port. See memory `handedness-negate-x`.
- **2026-08-12** - **Districts are built from raw Sketchfab originals, not the shipped GLBs.**
  The raw downloads share the exact coordinate frame `config.ts` documents, so they need no Blender
  normalize pass - which removes the only real cost objection. See memory
  `district-sources-match-config`.
- **2026-08-12** - **District GLBs stay out of git.** 40-85 MB each; free LFS is 1 GiB and shared
  with the original repo. Working copies in `Assets/Models/City/` are gitignored, zips archived in
  `~/TheBlockSource/cities/zips/`. `first-one.glb` is the exception - 240 KB and the only copy in
  existence, so it is committed.
- **2026-08-13** (U4) - **The exporter dumps the WHOLE config, not the subset U5 needs.** The game
  repo is permitted exactly one added file, so a subset would force re-editing it at U12, U13, U17
  and U20. The whole thing is 61 KB and `TheBlockConfig` ignores unknown fields, so the C# model can
  stay a subset and grow per unit while the exporter never changes again.
- **2026-08-13** (U4) - **No timestamp in the export; a `$sourceSha256` instead.** A timestamp would
  break byte-identical re-runs, which is what makes a stale export detectable at all.
- **2026-08-13** (U5) - **The scene is a pure function of the config plus the assets on disk.**
  WorldBuilder destroys its own root and rebuilds every run, so nothing under `World` may be
  hand-edited. Placement, the facade rebind, car hiding and colliders all live in the builder - not
  in the scene file, where they would be invisible and unreproducible.
- **2026-08-13** (U5) - **Foliage is excluded from collision only when the WHOLE renderer is
  foliage.** The district GLBs are merged meshes; "any material matches" stripped collision from
  entire districts. A mixed mesh collides, palms included - the same hole the web build has.
- **2026-08-13** (U5) - **Substitute models go in `WorldBuilder.AssetAliases`, never renamed or
  re-authored on disk.** A rename hides the substitution and an edited file hides the fix; the alias
  table carries the file name plus whatever rotation and lift that stand-in needs, and warns on
  every build. First entry is the pizza place, which needed all three.
- **2026-08-13** (U5) - **A stand-in ignores the config's `hideNodes`.** Those names describe the
  original model's parts, and a shared name means the wrong thing: the pizza substitute's `PizzaLight`
  is its lamp post, not the light the web build hides.
- **2026-08-13** (U6) - **Model-local offsets need `Convert.ModelOffset`, not `Convert.Pos`.** A
  world position only crosses the handedness change; an offset in a model's own frame also crosses a
  convention change, because three.js faces `-Z` and Unity faces `+Z`. Through `Pos` the chase
  camera lands in the character's face. Z verified against Joe; X is still unverified, since every
  offset ported so far has `x = 0`.
- **2026-08-13** (U6) - **Tank controls carry over.** A/D turn the body, W/S drive along its facing,
  and the camera trails rather than steers. This is the original's design, not a three.js
  limitation, so rule 5 says it stays.
- **2026-08-13** (U6) - **Unity's `CharacterController` replaces the Rapier kinematic capsule plus
  hand-rolled collide-and-slide.** Same behaviour, one component, and it brings `stepOffset` - which
  the web build had no equivalent for and which is what gets Joe up a Florentin curb.
- **2026-08-13** (U5) - **Districts are never `BatchingStatic`.** Batching rebuilds a >65k-vertex
  mesh on a 16-bit index buffer and shreds it, while the collider keeps using the real asset mesh -
  so the world feels right and looks wrong, which is how it survived a checkpoint. Nothing to win
  either way: a district is one to four huge meshes and batching exists to merge small draws. The
  flags are listed one by one in `SetDistrictStaticFlags`, because passing "everything except
  batching" as an all-bits value is normalised back to Everything. See memory
  `static-batching-shreds-big-meshes`.
- **2026-08-13** (U7) - **`Joe.controller` is generated, not hand-authored.** Same reasoning as
  WorldBuilder: a graph built in the Animator window is invisible in review and impossible to
  reproduce. `JoeAnimatorBuilder` rebuilds the asset in place so the GUID survives and the scene
  keeps its reference.
- **2026-08-13** (U7) - **One 1-D blend tree covers the whole gait ladder.** Jog gets no state and
  no clip: at 4.5 m/s it is simply where the blend sits between walk and sprint. A jog clip can drop
  in later as a third threshold without touching anything else.
- **2026-08-13** (U7) - **Root motion stays off; clip cadence is corrected instead.** `Joe_Sprint`
  carries real root motion authored at 5.58 m/s while the controller moves at 7.0, so the blend tree
  plays it at 1.25× rather than letting the clip drive position. The controller owns position
  everywhere on foot. U18's run-over is the deliberate exception.
- **2026-08-13** (U6) - **No Cinemachine yet.** The chase camera is fifteen lines with a specific
  feel to reproduce; a camera framework earns its place at U23's helicopter and U26's menus, not
  here.
- **2026-08-13** (U8) - **The car is a Rigidbody on four WheelColliders, not a port of `vehicle.ts`.**
  The web build's car is kinematic - a scalar speed and heading pushed through a Rapier character
  controller with a ray snapping it to the road - because Rapier's vehicle controller was unusable
  there. That is scar tissue under port rule 5, and PhysX gives real suspension, momentum and
  collisions that U17's traffic, U18's run-over and U19's ramming all inherit for free. Gameplay
  numbers carry (20 m/s cap, 7 m/s reverse, ~34° lock); every physics number is re-derived.
  Chosen by the user over a raycast-suspension middle path and a 1:1 kinematic port.
- **2026-08-13** (U8) - **`config.vehicle`'s physics fields are deliberately NOT in the C# model.**
  `accel`, `brakeDecel`, `friction`, `steerRatio`, `wheelReturn`, `colliderHeight`,
  `colliderBottomGap`, `blockedRatio`, `blockBleedMinSpeed`, `maxClimbRate` and `characterOffset`
  all describe the kinematic car. Under PhysX they are outputs of mass, suspension and tyre
  friction, not inputs. Declaring them would invite someone to wire them up and be wrong, so their
  absence is the statement. Replacements are serialized on `CarController` where they can be tuned
  against the real thing.
- **2026-08-13** (U8) - **`Convert.ModelFacing` is the rotational twin of `ModelOffset`.** three.js
  drives an object down `-Z`, Unity down `+Z`, so a model with a FRONT needs a 180° yaw that a
  district never does. The Mustang proves the two flips compose into exactly that one rotation: its
  `wheel_Front_L_0` bone imports at `(0.992, 0.479, -1.562)` and lands at `(-0.992, 0.479, 1.562)`
  - front and left, which is what the bone calls itself. The same 180° that points the nose down
  `+Z` also puts the L/R names back on Unity's hands.
- **2026-08-13** (U8) - **A car prefab is generated by `CarBuilder`, never assembled by hand.** Same
  reasoning as WorldBuilder and JoeAnimatorBuilder: four WheelColliders dragged into place are
  invisible in review and silently wrong after a re-export. Wheel radius and corner assignment are
  MEASURED off the rig - corners by the sign of the bone's position, never by its name, because the
  X negation makes `_L_` arrive on Unity's right until the facing rotation is applied.
- **2026-08-13** (U8) - **The prefab root's origin is the tyre contact patch.** The model's own
  origin sits 0.1 m below its tyres, so anchoring there makes `config`'s Y-less `spawn` plus
  `roadSurfaceY` directly usable as "put the car here" instead of burying or floating it.
- **2026-08-13** (U8) - **`x-negation-does-not-mirror-text`.** Checked by eye rather than reasoned:
  Reichman's Hebrew sign reads `אוניברסיטת רייכמן` correctly after import. The negate-X convention
  is a change of basis, not a visual mirror, so signage needs no compensation.
- **2026-08-13** (U8) - **Blender exports get `export_image_webp_fallback=True`.** A texture stored
  as .webp in a .blend exports as one, which writes `EXT_texture_webp` into extensions**Required**;
  glTFast cannot read it and rejects the entire file, importing it as a `DefaultAsset` so
  WorldBuilder just says "missing" while the real error hides in the Inspector. The fallback demotes
  it to extensionsUsed. Forcing JPEG would be smaller but drops alpha, and Reichman's flag is an
  alpha decal.
- **2026-08-13** (U9) - **`Convert.ModelOffset` negates Z only; X passes through.** The negation it
  carried since U6 was inherited from `Pos` on the assumption that both mirror, and no unit had ever
  exercised it because every offset ported until now had `x = 0`. Both engines put a model's right
  at local `+X` and its up at `+Y`; they disagree only about forward. Equivalently, glTFast's X
  negation and `ModelFacing`'s 180° cancel. Measured against the Mustang's own rig, whose
  `wheel_Front_L_0` has to stay on the left. A world position and a model-local offset are
  permanently different conversions - see memory `model-offset-x-passes-through`.
- **2026-08-13** (U9) - **One Joe, reparented - not a second body in the seat.** The web build hides
  the walking player and mounts a separate skinned driver, because three.js had no cheap way to hand
  one skeleton between two animation graphs; Unity does, so the same GameObject is parented to the
  car's driver anchor with its controller switched off. One body, one Animator, and U29's character
  roster reaches the seat for free instead of needing a second swap path. Unity wins, per the
  standing rule.
- **2026-08-13** (U9) - **The entry clip's travel is baked into its pose, never root motion.** The
  seat anchor is a fixed child of the car prefab and the driver must not move relative to it, so the
  clip has to carry its own travel visually - Bake Into Pose on rotation, position Y and position
  XZ, all Based Upon Original. That is also what makes `config.vehicle.driver.seats` usable as
  written, since it was authored against the clip's own origin. U18's run-over is the deliberate
  opposite and is the only place root motion goes on.
- **2026-08-13** (U9) - **Borrowed Mixamo clips are imported by a script, not by hand.** Same
  reasoning as every other builder here: the settings are six checkboxes across two Inspector tabs,
  invisible in review, and wrong ones fail as a T-pose or a driver sliding out of the car - which
  reads as an animation bug, not an import mistake. `JoeClipImporter` states them once; a new clip
  is one table row.
- **2026-08-13** (U9) - **A state machine's run state is serialized, its cached config is not.** A
  recompile during Play reloads the domain but the SCENE survives, so a machine that forgets its
  mode wakes up disagreeing with the world - Joe parented inside a car while the machine believes he
  is on foot, which no `Bind()` guard recovers. `[SerializeField, HideInInspector]` on the state
  fields, and the existing null-check rebind for everything derived from config.
- **2026-08-15** (U10) - **The bike is a Rigidbody on two WheelColliders, not a port of
  `motorcycle.ts`.** Same call as U8's car and for the same reason: the web build's kinematic
  speed-and-heading model is a Rapier workaround, not a statement about two-wheelers. It buys real
  collisions, suspension, momentum and a lean. Gameplay numbers carry (20 m/s cap, 7 m/s reverse,
  ~34° lock); every physics number is re-derived.
- **2026-08-15** (U10) - **The lean is visual, on its own pivot; the Rigidbody stays upright.**
  Rolling the body of a two-wheeler is not a lean, it is a fall. The rider anchor hangs off the same
  pivot so Joe leans with it. The angle is read off `v·ω / g` rather than off the steering key, so it
  is right during a skid and absent when parked.
- **2026-08-15** (U10) - **A two-wheeler needs an active upright torque, always on.** Two contact
  points give a Rigidbody no roll stability whatsoever, riderless or not, and this model has no
  kickstand. The damping term is a look-ahead on angular velocity, not a `-kω` - correcting only the
  present error makes a pendulum.
- **2026-08-15** (U10) - **Enterable vehicles register themselves, in `OnEnable`/`OnDisable`.** A
  spawner cannot know when its vehicle dies, and a stale registry entry is `E` aimed at a corpse.
  The registry also sweeps dead entries itself, because a destroyed MonoBehaviour reached through an
  INTERFACE reference does not compare equal to null - the operator is on `Object`, and an interface
  does not carry it.
- **2026-08-15** (U10) - **The quick mount is parameterised, not duplicated.** Two defaulted members
  on `IEnterable` (`UsesEntryAnimation`, `ShowRiderOnQuickMount`) cover the difference between
  getting into a car and getting onto a bike; a door-less vehicle also skips the door timings rather
  than waiting 1.05 s for a swing it does not have. U23's helicopter and U24's jetski are meant to
  land as two more flag values, not a third code path.
- **2026-08-15** (U10) - **A `[SerializeField]` on an interface type serializes NOTHING.** Unity
  writes no value and gives no warning, so `VehicleEnterExit`'s mid-Play-recompile guard was silently
  not guarding the one field it most needed to. Store the concrete `MonoBehaviour` and cast back.
- **2026-08-15** (U11) - **Cutout foliage is a generated URP/Lit material, not a setting on the
  imported one.** glTFast bakes the surface mode into its Shader Graph material at import from the
  glTF's `alphaMode`, so `_AlphaClip` on it is inert - the fix has to be a separate material asset,
  which is the same call U1 made for the facade tint and for the same reason. The imported material
  is read for its texture and factors and never written. Its metal-roughness and occlusion MAPS are
  deliberately not copied: glTF packs those channels differently from URP/Lit, so carrying them
  across would be silently wrong. None of the materials this touches has one.
- **2026-08-15** (U11) - **Which blended materials are really cutouts is a port-side judgement, and
  the leftovers get named in the build report.** The web build had one material path and never made
  the distinction, so there is nothing in `config.ts` to port. `CutoutMaterialPatterns` decides, and
  every material still transparent after the pass is listed under STILL BLENDED - so a wrong call
  shows up as a list to check rather than as a mystery.
- **2026-08-15** (U11) - **Ask `IsBlended()` before matching the name.** Patterns are substrings and
  "tree" is inside "CityGen_S`tree`ts", which alpha-clipped every road surface on the first build.
  A tighter pattern is not the fix; the precondition is, because a cutout only ever repairs
  something that is blended to begin with.
- **2026-08-15** (U11) - **Baked-in parked cars are stripped at the submesh level in Unity, not
  split in Blender.** WorldBuilder owns the mesh at build time, so the split is a build step and the
  .glb on disk stays as downloaded - the same principle as `AssetAliases`. The vertices are
  compacted, not just the indices dropped: the cars are 86% of city 2's triangles. Stripping also
  takes them out of collision, which tinting or hiding would not have.
- **2026-08-15** (U11) - **Generated asset folders are swept every build.** `Cutout/` and
  `Meshes/Generated/` are build OUTPUT, so anything in them the current build did not write is
  deleted. Without the sweep they are append-only and a corrected pattern list leaves behind a
  plausible-looking material that nothing references - the same invisible-and-unreproducible failure
  that keeps the world out of the scene file.
- **2026-08-12** (U1) - **Downtown gets one collider over the whole mesh.** `city.noCollidePatterns`
  matches node *or* material names; `first-one.glb` has no per-object nodes and its only foliage
  material (`AM113_072_Washingtonia_filifera`) matches no pattern - so the shipped web build
  collides with its palms too. This is faithful, not a shortcut. Build the noCollide filtering when
  the first multi-node district lands, not before.
