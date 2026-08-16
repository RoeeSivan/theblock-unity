# The Block - Unity

**A Florentin joyride, rebuilt in Unity.** A small, genuinely playable GTA-style 3D open world set in
Florentin, Tel Aviv - walk it, swim it, drive it, ride it, fly it, and play a four-mission campaign
through to a win screen.

Solo final project for the course _From idea to app using AI_.

---

## The pivot - read this first

This project started as a **three.js + Rapier + TypeScript** browser game, and it shipped: 175
modules, ~26.8k LOC, live on Vercel. That repo is
**[RoeeSivan/Finalproject](https://github.com/RoeeSivan/Finalproject)**, and its history runs from
April 2026.

Mid-project I pivoted to **Unity 6** and rebuilt the whole game - not a slice, not a demo. Two
reasons: engine breadth, and because a stack of things the browser build had to work around are
things an engine simply *does*. This repo is the second half of that story, and the two histories
are meant to be read together.

**What the rebuild bought, concretely** - each of these is a place the web build settled and Unity
did not have to:

| | three.js build | Unity build |
| --- | --- | --- |
| **Police pursuit** | Cops drive **straight at you** - the road graph was five disconnected islands, so routing was deleted | Real **A\*** over the street graph; stitching T-junctions within 3 m makes 97.9% of the city one component |
| **Arrest** | The cruiser is the arrest | An **officer gets out and chases you on foot**; her seat is the entry animation's own origin |
| **Vehicles** | Kinematic capsules snapped to the road surface | **Rigidbodies on WheelColliders** - real suspension, real collisions, a bike that leans |
| **Collisions** | A rammed car is scenery | A rammed traffic car becomes a **Rigidbody wreck**; 101 parked fillers take a hit and shunt |
| **Crowd** | ~400 people seeded at boot and frozen | **1,147 authored people**, NavMesh-validated, 230 gated zebra crossings |
| **Roads** | Per-segment stretched tiles | **Splines** - 1,864 m of ribbon, markings continuous through the corners |
| **Minimap** | A snapshot baked at boot | A **live second camera** into a RenderTexture, capped at 12 fps |
| **Sirens** | One wail at constant gain (the build has no `AudioListener` at all) | **3D positional sirens** on the cars, nearest three |
| **Rhythm clock** | `audioElement.currentTime` - main thread, jitters against the frame | **`AudioSettings.dspTime`** - measured drift **0.02 ms** over a full song |
| **Textures** | A download optimisation: Draco + webp + 1024² | Per-platform block compression; **13,498 MB → 534 MB** resident, with no visual cost |

---

## Running it

**You need [Git LFS](https://git-lfs.com) installed _before_ you clone.** Every model, texture and
audio file here is stored in LFS. Clone without it and you get pointer files instead of assets, and
Unity will open an empty world.

```bash
git lfs install
git clone https://github.com/RoeeSivan/theblock-unity.git
```

Open the folder in **Unity 6000.5.8f1** (Apple Silicon) and press Play from
`Assets/Scenes/Boot.unity` - that is build index 0, and it is the loading screen and title menu.
Playing from `World.unity` directly skips the shell and starts you in the city.

> First open takes a while: Unity imports ~2 GB of source assets and bakes shader variants. That is
> once, not every launch.

## Controls

### On foot

| Key | Action |
| --- | --- |
| `W` `A` `S` `D` / arrows | Move |
| `Alt` _(hold)_ | Jog |
| `Shift` _(hold)_ | Sprint |
| `Space` | Jump |
| `E` | Enter a vehicle / a building / shop at the 7-Eleven |
| `T` | Talk - start a job |
| `F` | Deliver, act, or retry a failed mission |
| `1` - `4` | Use a power-up |

### Driving

| Key | Action |
| --- | --- |
| `W` `A` `S` `D` | Drive & steer |
| `Space` | Brake - or **hold at a gas pump to refuel** |
| `E` | Exit |
| `R` | Respawn / flip |
| `Space` / `Shift` | Helicopter up / down |

### General

| Key | Action |
| --- | --- |
| `M` | Map |
| `N` | Mute |
| `Esc` | Pause menu |
| `←` `↓` `↑` `→` | Dance - hit the arrows |

## The campaign

Four missions, in order, unlocked as you finish them. **Mission Select** on the title screen jumps to
any you have reached.

1. **The Block Pizza Run** - take a shift at the pizzeria, deliver five pizzas across the city
   against a clock.
2. **Dance Battle** - a rhythm minigame on a real audio clock, scored on a 50 ms judgment window.
3. **Rooftop Rescue** - fly the Huey, land on roofs, lift survivors off them. Roof landing spots are
   baked at build time from a downward cast, 46 across 8 districts.
4. **Jetski Chase** - nine buoy gates across the sea, then run the thief down on the sand.

Free-roam around all of it: a wanted system with real pursuit, carjacking, run-overs, a fuel tank
that limps rather than strands you, a 7-Eleven that sells four power-ups, and a day/night cycle
behind **Settings → Display**.

## Built with

- **Unity 6000.5.8f1**, Universal Render Pipeline 17.5
- **glTFast** 6.19 + **Draco** 5.4 - district and vehicle import
- **Splines** 2.9 - the road ribbons
- **AI Navigation** 2.0 - NavMesh, used as a *query surface* rather than as agents
- **Input System** 1.20, **UI Toolkit** - every menu, HUD and the map are UI Toolkit
- **TextCore** - Noto Color Emoji as a `COLOR`-mode `FontAsset`, which is what makes 🍕 render
- **AudioMixer** - 7 buses, authored by a reflection tool because Unity ships no public API for it

**The offline pipeline is the part with no counterpart in the web build.** The original's
`config.ts` is exported once by `scripts/export-config.mjs` into `theblock-config.json`, and an
Editor-time `WorldBuilder` turns that into the scene and into baked `ScriptableObject`s - the traffic
graph, the police route graph, roof spots, the NavMesh. **The runtime casts no rays for any of it.**

## Repo layout

| Path | What |
| --- | --- |
| `Assets/Scripts/` | All runtime code - 133 files, none of which reference `UnityEditor` |
| `Assets/Editor/` | The build toolchain - 42 files under **The Block →** in the menu bar |
| `Assets/Scenes/` | `Boot.unity` (index 0) and `World.unity` (index 1) |
| `tools/` | Offline asset scripts (glb webp→png, etc.) |
| `PORT-STATUS.md` | The living ledger: all 34 units, every decision, every gotcha |
| `CLAUDE.md` | The stable rules for working on it |

## Credits

Characters and animations from **Mixamo**. District and vehicle models from **Sketchfab** and the
Unity Asset Store. Everything else - code, world assembly, tuning - is mine.
