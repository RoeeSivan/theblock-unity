# The Block — Unity Port

> **READ `PORT-STATUS.md` NEXT, BEFORE DOING ANYTHING.** It is the living ledger: what is done,
> what is half-built, and the single next action. This file holds the stable rules; that file holds
> the state. Conversation history is never a source of truth — the ledger is.

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

### Scope: the full game

Not a slice, not a demo — **the whole game rebuilt in Unity**: all 4 missions, the free-roam
systems, the world, the shell. Sequenced as **32 numbered units** in `PORT-STATUS.md`, ordered by
dependency. Multiplayer is deferred to the last unit (U32).

Target: **macOS desktop** first. iPad/iOS is a wanted bonus, never a constraint on design.

Settled decisions (full list in `PORT-STATUS.md` → Decisions log):

- **Unity-idiomatic, same game.** Same missions, same world, same feel — built the Unity way.
  Where Unity offers a better mechanism than the web version's workaround, **Unity wins**.
- **Autonomous units, one checkpoint each.** Build a unit fully → update `PORT-STATUS.md` →
  commit → report. The user play-tests at unit boundaries.
- **No deadline.** Resumability matters more than speed.

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

1. **Handedness — SOLVED 2026-08-12: negate X.** three.js is right-handed Y-up; Unity is
   **left-handed** Y-up. glTFast negates **X** on import and passes Y and Z through untouched:

   ```csharp
   Convert.Pos(p) => new Vector3(-p.x, p.y, p.z);
   Convert.Yaw(y) => -y;
   ```

   Verified on five district assets and a landmark gap measurement — not assumed. The importer
   flips mesh data, but every hand-authored number in `config.ts` — district offsets, spawn points,
   road polylines, mission waypoints, POIs — does not flip itself. Missing one mirrors that thing
   against everything else and it looks *almost* right.
   → **One static helper, one place. Never inline a sign flip.**
   → When a genuinely new *category* of coordinate appears, still cross-check it against a
   landmark before trusting it wholesale.
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

**See `PORT-STATUS.md`.** Its `RESUME HERE` block is the single source of truth for what to do
next; this file deliberately does not duplicate it, because two copies of a moving state is how
they drift apart.

**Closing a unit — all three, or it is not done:**

1. It play-tests correctly in the Editor, confirmed **by the user**.
2. `PORT-STATUS.md` updated — state, commit hash, and a rewritten `RESUME HERE`.
3. The commit lands.

If a unit cannot be finished, mark it `wip` and write exactly what is built, what is not, and the
next concrete action. **A `wip` unit with a vague note is the failure mode this system exists to
prevent.**

Record genuinely new gotchas as memory files in
`~/.claude/projects/-Users-roeesivan-TheBlockUnity/memory/` (this project starts with empty memory —
it is keyed by path).

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
