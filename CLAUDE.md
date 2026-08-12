# The Block — Unity Port

Loaded every session. This project is a **rebuild of an existing, finished game** in Unity. Most
of the knowledge you need is not here — it is in the original project, and this file tells you
where.

---

## 1 — What this is

`The Block` is a shipped browser game: an Israeli GTA-style 3D mini open world set in Florentin,
Tel Aviv. three.js + Rapier + Vite + TypeScript, 175 modules / ~26.8k LOC, 4-mission campaign,
live on Vercel.

This repo rebuilds it in **Unity 6** (6000.5.8f1, URP). Motivation is career breadth — engine
experience — not a product need. It is a **side project with no deadline**.

**The original ships and must keep shipping.** Its `main` stays submittable (course deadline
1 Oct 2026). The only change ever made to that repo for this port is one additive script,
`scripts/export-config.mjs`. Never refactor it, never "improve" it, never touch its runtime.

### Current scope: vertical slice, NOT the full campaign

One district (**First One**, downtown) + the motorcycle + the **pizza delivery mission**, playable
end to end. Deliberately excluded until the slice is done and judged: police pursuit, run-over,
traffic, crowd, day/night, fuel, district streaming, the other 3 missions, multiplayer.

Target: **macOS desktop build** first. iPad/iOS is a wanted bonus, not a constraint on the slice.

Plan file: `~/.claude/plans/i-want-to-consult-indexed-willow.md`

---

## 2 — Where the real knowledge lives

The original project is at:

```
/Users/roeesivan/Desktop/Year B/Semester B/From idea to app using AI/Final project
```

**Add it to the session** so its source is readable:

```
/add-dir "/Users/roeesivan/Desktop/Year B/Semester B/From idea to app using AI/Final project"
```

Then read from it directly. Nothing here summarizes it — the TypeScript is the spec.

| You need | Read |
| --- | --- |
| Whole-project overview, settled decisions | its `CLAUDE.md` (self-contained, ~760 lines) |
| Every constant: offsets, spawns, roads, tuning | `src/config.ts` (1726 lines) + `src/**/*.config.ts` |
| Mission logic | `src/mission/`, `src/game/mission-feedback.ts` |
| Vehicle behavior | `src/vehicle/` |
| Diagrams, tool/API order | `docs/architecture.md` |

**Memory does not carry across projects** — it is keyed by project path, so this project starts
with empty memory. The original's memory files are at:

```
~/.claude/projects/-Users-roeesivan-Desktop-Year-B-Semester-B-From-idea-to-app-using-AI-Final-project/memory/
```

Most relevant here: `run_over_mechanic`, `police_pursuit`, `mixamo_bone_namespace`,
`fbx_to_glb_conversion`, `character_roster`, `perf-budget`, `asset-safety`,
`feedback_user_checks_manually`, `feedback_visual_checkpoints`.

---

## 3 — Port rules

**These are specific to this repo and override habits from the original.**

1. **Handedness.** three.js is right-handed Y-up; Unity is **left-handed** Y-up. glTF import flips
   mesh data, but every hand-authored number in `config.ts` — district offsets, spawn points, road
   polylines, mission waypoints, POIs — does not flip itself. Missing this mirrors the city against
   the mission locations and it looks *almost* right.
   → **One static `Convert.Pos()` / `Convert.Yaw()` helper, one place. Never inline a sign flip.**
   → Confirm the convention **empirically** against a known landmark. Do not assume.
2. **Physics numbers are void.** Rapier ≠ PhysX. Every mass, friction, suspension and impulse value
   from the original is meaningless here. Re-derive by feel. Do not port them and do not cite them
   as a starting point — they will mislead.
3. **Prefer source assets over shipped ones.** The original's `public/models/optimized/` is
   Draco-compressed, webp, 1024² — a *download* optimization. Unity does its own per-platform
   compression (ASTC on iOS, BC7 on desktop), so feeding it those stacks a second lossy pass and
   adds a Draco import dependency. Use `source-assets/` (2.0 GB of raw FBX/GLB) where an original
   exists.
   **Known exception:** `first-one.glb` (downtown, the slice district) has **no original** — only
   the 240 KB shipped file. It is near-textureless, so this is cheap. Note its facade color is
   applied *in code* at load (`config.ts` `facadeColor`/`facadeMaterials`), not baked in the asset —
   that tint must be re-implemented, it will not import.
4. **Never modify the original repo** beyond `scripts/export-config.mjs`.
5. **Separate decisions from scar tissue.** Many "settled" calls in the original's CLAUDE.md §13 are
   workarounds for three.js/Rapier limits, not design. Example: "cops drive straight at you, all
   routing deleted" was forced by a broken lane collider — Unity's NavMesh likely makes real pursuit
   work. Re-test before inheriting a workaround. Design intent carries; scar tissue does not.

---

## 4 — Setup state (done)

- Unity **6000.5.8f1** (Apple Silicon), modules: MacStandalone, iOS, WebGL.
- Project created from **Universal 3D (URP)** template at `~/TheBlockUnity`.
- **No Unity Cloud, no Unity AI Assistant, no Unity Version Control.** The user will not pay Unity
  anything — Unity Personal only. Unity AI runs on paid credits; stay off it. Do not propose Unity
  Cloud / Build Automation / DevOps.
- git + **Git LFS** local, no remote yet. GitHub free LFS is 1 GiB storage + 1 GiB/mo bandwidth
  **per account**, already shared with the original repo — so the full 2 GB asset set cannot be
  pushed there. Slice assets are small enough to be fine.
- `com.coplaydev.unity-mcp` installed (MCP for Unity v10.1.2), Transport **HTTP Local**,
  server on `http://127.0.0.1:8080`. Registered for Claude Code, scoped to this project path.
- `com.unity.ai.navigation` (NavMesh) present from the template — relevant much later, for police.

**MCP only works from a session whose cwd is `~/TheBlockUnity`.** Start the local server from
`Window → MCP for Unity` → **Start Server** if it is not listening.

---

## 5 — Next steps

1. **Phase 1 — import spike.** Get `first-one.glb` and one Mixamo character standing in a scene,
   correct scale and orientation, with a borrowed walk clip playing. Needs **glTFast**
   (`com.unity.cloud.gltfast`) + the **Draco** package, since the only downtown file is Draco'd.
   Import characters as **Animation Type = Humanoid** — Unity retargets by bone *role*, which
   deletes the original's entire `mixamorig:` namespace bug class.
   **Gate: screenshot confirming it before anything else starts.**
2. **Phase 2 — config as data.** `scripts/export-config.mjs` in the original emits
   `theblock-config.json`; a C# `WorldBuilder` **Editor script** here reads it and builds the scene.
   The export is a faithful mirror — **coordinate conversion happens in C#, not in the exporter.**
   Tuning values become **ScriptableObjects** so they stay Inspector-editable.
3. **Phase 3 — the slice.** Character controller → motorcycle → enter/exit state machine → pizza
   mission → UI Toolkit HUD → audio.
4. **Phase 4 — decision gate.** Play it, then decide whether to continue. **Stopping is a
   legitimate outcome** and still leaves a real Unity portfolio piece.

---

## 6 — Working style

- **The user is new to Unity.** Give the single next physical step, not a wall of phases. Name exact
  menus, buttons and field labels — and read the package source to get them right rather than
  trusting a README, which has already been stale once.
- **Verification is manual and belongs to the user.** I cannot see the Game view or judge whether
  the bike feels good. Ask for a screenshot; one change → screenshot → confirm → next.
- **Do not spend money.** Not Unity, not Asset Store, not cloud.
- Boring conventional choices over clever ones. C# conventions: `PascalCase` methods/types,
  `camelCase` locals, one concern per file.
