using System.Collections.Generic;
using UnityEngine;

namespace TheBlock.Core
{
    /// <summary>
    /// Tier 5's half of the config model: the five mission modules the exporter gained for the
    /// port - <c>mission.config.ts</c>, <c>rescue.config.ts</c>, <c>chase.config.ts</c>,
    /// <c>campaign.config.ts</c> and <c>rhythm/rhythm.config.ts</c>.
    ///
    /// Same rules as the world half: still right-handed, nothing converted here, and every
    /// XZ/XYZ literal exposes a <c>Raw</c> that goes through <see cref="Convert"/> at the point of
    /// use. Split into its own file only for length - see the partial declaration in
    /// <c>TheBlockConfig.cs</c>.
    ///
    /// <b>Most of this is DATA, not tuning.</b> The 15 delivery spots, the 18-point flee route and
    /// the 3-point beach run were authored by hand in the original with its in-game position
    /// recorder. Re-typing them would be re-authoring the campaign, which is exactly what the
    /// exporter exists to prevent.
    /// </summary>
    public static partial class TheBlockConfig
    {
        // ── shared shapes ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The overhead "go here" beacon - <c>mission.config.ts</c>'s <c>beacon</c> block, shared by
        /// all three marker missions. Only the colour differs per mission (green deliveries, orange
        /// survivors, cyan gates), which is why the colour is passed in rather than read from here.
        /// </summary>
        public class BeaconSpec
        {
            /// <summary>Packed <c>0xRRGGBB</c>, the delivery run's green. See <see cref="ColorFromHex"/>.</summary>
            public int Color = 0x43c463;

            /// <summary>Metres above the ground spot the pin floats at.</summary>
            public float HeadHeight = 2.2f;

            public float ConeRadius = 0.35f;
            public float ConeHeight = 0.7f;
            public float BeamRadius = 0.12f;

            /// <summary>Tall thin pillar under the cone, so the pin reads from across the street.</summary>
            public float BeamHeight = 14f;

            public float BeamOpacity = 0.22f;
            public float BobAmplitude = 0.25f;
            public float BobSpeed = 2f;
            public float SpinSpeed = 1.6f;

            /// <summary>Seconds of scale-up + fade when the objective is met, before removal.</summary>
            public float PopTime = 0.3f;

            public float PopScale = 2.2f;
        }

        /// <summary>An XZ point with an optional standing yaw - a delivery drop-off.</summary>
        public struct DeliverySpotSpec
        {
            public float X;
            public float Z;

            /// <summary>Facing in RADIANS, right-handed. Absent on every spot in the original.</summary>
            public float Yaw;

            /// <summary>Unconverted. Feed it to <see cref="Convert.Pos(Vector3)"/>.</summary>
            public Vector3 Raw => new Vector3(X, 0f, Z);
        }

        // ── M1 - the pizza delivery run ───────────────────────────────────────────────────────

        /// <summary>
        /// <c>src/mission/mission.config.ts</c>. The shift: how many drops, how long you have, and
        /// the 15 hand-recorded pavement spots five of them are drawn from each run.
        /// </summary>
        public class MissionSpec
        {
            /// <summary>Targets spawned per run - a random subset of <see cref="CandidateSpots"/>.</summary>
            public int DeliveryCount = 5;

            /// <summary>Pizzas handed over at the counter. The HUD counts down from this.</summary>
            public int PizzasGiven = 5;

            /// <summary>Metres from a target at which the prompt shows and F delivers.</summary>
            public float DeliverRadius = 6f;

            /// <summary>Whole-shift countdown in seconds (~4 min).</summary>
            public float TimeLimitSec = 240f;

            /// <summary>Below this the HUD clock turns red and pulses.</summary>
            public float LowTimeSec = 30f;

            /// <summary>
            /// The five faces, cycled across the targets. In this port the MESHES come from the
            /// crowd's own imported prefabs (they are the same five people) - what is read here is
            /// the ORDER and each customer's thank-you line.
            /// </summary>
            public List<MissionNpcSpec> NpcSpecs = new();

            public BeaconSpec Beacon = new();

            /// <summary>
            /// The 15 authored drop-offs. Every one is the centre of a pavement rectangle the crowd
            /// already spawns in, so all of them are walkable by construction - the original's
            /// comment is explicit that this is why they were chosen that way.
            /// </summary>
            public List<DeliverySpotSpec> CandidateSpots = new();

            /// <summary>Read out on the briefing card when the cashier starts your shift.</summary>
            public List<string> BriefingLines = new();

            /// <summary>Hazel's spoken briefing. Web path; the port maps it to a clip by file name.</summary>
            public string BriefingVoiceUrl;
        }

        /// <summary>One delivery customer: which crowd face, and their own thank-you voice line.</summary>
        public class MissionNpcSpec
        {
            public string Name;

            /// <summary><c>'m'</c> or <c>'f'</c>. U27's scream pools want it; nothing here does.</summary>
            public string Gender;

            public string IdleUrl;
            public string WalkUrl;
            public string ThankUrl;
        }

        // ── M3 - the rooftop rescue ───────────────────────────────────────────────────────────

        /// <summary>
        /// <c>src/mission/rescue.config.ts</c>. Note what is NOT here: the survivor faces, because
        /// the original deliberately reuses the delivery run's five so there is one source of them.
        /// </summary>
        public class RescueSpec
        {
            public int SurvivorCount = 4;
            public float TimeLimitSec = 180f;
            public float LowTimeSec = 30f;

            /// <summary>
            /// 3D metres. Descend within this of a survivor and they are scooped up - generous, so
            /// no pixel-perfect landing is needed, but you must drop to the roof rather than
            /// overfly it at altitude.
            /// </summary>
            public float RescueRadius = 10f;

            /// <summary>A sampled surface must be at least this high to count as a roof, not street.</summary>
            public float RoofMinHeight = 8f;

            /// <summary>
            /// Cap on random samples per placement pass in the original, which casts at RUNTIME.
            /// This port bakes the candidates at build time instead, so the number bounds the BAKE.
            /// </summary>
            public int SampleAttempts = 400;

            /// <summary>Metres between survivors, so they are not clustered on one block.</summary>
            public float MinSeparation = 20f;

            /// <summary>Packed <c>0xRRGGBB</c>. Orange, to read as "rescue" against delivery green.</summary>
            public int BeaconColor = 0xff6b3d;

            public List<string> BriefingLines = new();
        }

        // ── M4 - the jetski sea chase ─────────────────────────────────────────────────────────

        /// <summary>A point on the thief's flee route. <see cref="Gate"/> points get a buoy.</summary>
        public struct ChasePointSpec
        {
            public float X;
            public float Z;

            /// <summary>This point carries a buoy, a beacon and a map pin. 9 of the 18 do.</summary>
            public bool Gate;

            /// <summary>Unconverted. Feed it to <see cref="Convert.Pos(Vector3)"/>.</summary>
            public Vector3 Raw => new Vector3(X, 0f, Z);
        }

        /// <summary>
        /// <c>src/mission/chase.config.ts</c>. Every coordinate here lives in the water west of
        /// <c>sea.shoreX</c> - which is Unity's EAST, since X negates. The run path is on dry sand.
        /// </summary>
        public class ChaseSpec
        {
            public float TimeLimitSec = 300f;
            public float LowTimeSec = 30f;

            /// <summary>Horizontal metres at which a gate counts as passed. Gates never fail you.</summary>
            public float GateRadius = 12f;

            /// <summary>On-foot catch distance in the beach finale.</summary>
            public float CatchRadius = 2.5f;

            /// <summary>Packed <c>0xRRGGBB</c>. Cyan - "water checkpoint".</summary>
            public int BeaconColor = 0x22ccee;

            public float BuoyScale = 1.2f;

            /// <summary>Where the thief idles before the chase, just ahead of the player's ski.</summary>
            public ChasePointSpec ThiefSpawn;

            /// <summary>The 18-point flee route, in order. The LAST point is the beach landing.</summary>
            public List<ChasePointSpec> Route = new();

            public RubberBandSpec RubberBand = new();

            /// <summary>rad/s yaw ease toward the next waypoint. Lower = wider arcs.</summary>
            public float TurnRate = 1.8f;

            public float WaypointRadius = 6f;

            /// <summary>Pre-chase idle: a slow constant-radius cruise, so he looks alive, not frozen.</summary>
            public IdleCircleSpec IdleCircle = new();

            /// <summary>Beach jog, then a ping-pong between the last two points until caught.</summary>
            public List<ChasePointSpec> RunPath = new();

            public RunSpeedSpec Run = new();

            /// <summary>Packed <c>0xRRGGBB</c>. Dark red hull paint, so the two skis read apart.</summary>
            public int HullTint = 0x8a1f1f;

            public string ThiefRiderUrl;

            public List<string> BriefingLines = new();
        }

        /// <summary>
        /// The chase cannot be lost on distance - the user's own call in the original. Thief speed
        /// maps linearly from <see cref="MaxSpeed"/> when you are inside <see cref="NearDist"/> down
        /// to <see cref="MinSpeed"/> at <see cref="FarDist"/>: ride his wake and he bolts, fall
        /// behind and he crawls. Player max is 18 m/s against his 17, so a close chase still reels
        /// him in. Only the clock can fail the run.
        /// </summary>
        public class RubberBandSpec
        {
            public float NearDist = 22f;
            public float FarDist = 85f;
            public float MinSpeed = 8f;
            public float MaxSpeed = 17f;
        }

        /// <summary>Constant forward speed + constant yaw rate (speed/radius) traces a circle.</summary>
        public class IdleCircleSpec
        {
            public float Radius = 9f;
            public float Speed = 4f;
        }

        /// <summary>On-foot rubber band: he slows to a walk when you are far, so he stays catchable.</summary>
        public class RunSpeedSpec
        {
            public float BaseSpeed = 3f;
            public float SlowSpeed = 1.2f;
            public float SlowDist = 12f;
        }

        // ── the campaign's copy ───────────────────────────────────────────────────────────────

        /// <summary>
        /// <c>src/mission/campaign.config.ts</c> - words and money, no coordinates. The waypoint a
        /// step points at is resolved from the live world config, so a location stays single-sourced.
        /// </summary>
        public class CampaignSpec
        {
            /// <summary>One row per mission, keyed by id. Order here is informational.</summary>
            public List<CampaignTextSpec> CampaignText = new();

            public string WinLine = "🏆 Campaign complete!";

            /// <summary>Shown once on a fresh profile, before the world goes live.</summary>
            public List<string> IntroLines = new();
        }

        /// <summary>One campaign step's player-facing copy and its flat payout.</summary>
        public class CampaignTextSpec
        {
            /// <summary>Stable key - matches the mission's own id. Never shown to players.</summary>
            public string Id;

            /// <summary>
            /// Map glyph, drawn in place of the objective's dot. Idle until the emoji font landed in
            /// U28 - this comment used to say the map draws a dot instead, and it does not any more.
            /// </summary>
            public string Emoji;

            /// <summary>"Go here next" line, on the HUD and on the previous step's handoff card.</summary>
            public string Objective;

            /// <summary>This step's completion flavour, at the top of the handoff/win card.</summary>
            public string Done;

            /// <summary>Fail card line, including the retry key for that mission.</summary>
            public string Fail;

            /// <summary>One-line narrative beat, shown when this step becomes the next objective.</summary>
            public string Story;

            /// <summary>Flat cash paid when this step completes - once per run, never on a retry.</summary>
            public int Reward;
        }

        // ── M2 - the beach dance ──────────────────────────────────────────────────────────────

        /// <summary><c>src/minigame/rhythm/rhythm.config.ts</c>. Timing, scoring, clips and placement.</summary>
        public class RhythmSpec
        {
            public RhythmSongSpec Song = new();

            /// <summary>Seconds a note is visible: travel time from the spawn edge to the ring.</summary>
            public float ScrollSeconds = 2.2f;

            public RhythmUiSpec Ui = new();
            public HitWindowsSpec HitWindows = new();
            public ScoreValuesSpec ScoreValues = new();
            public RhythmDancerSpec Dancer = new();

            /// <summary>
            /// Pass gate: the run is a WIN only if weighted accuracy (perfect 1, good 0.5, miss 0)
            /// reaches this. Null means it can never fail - the original ships 0.5.
            /// </summary>
            public float? FailBelowAccuracy = 0.5f;

            public RhythmBeatmapSpec Beatmap = new();
            public RhythmCheerSpec Cheer = new();

            /// <summary>Seconds after the last note before the run is declared over.</summary>
            public float EndTailSec = 1.5f;

            /// <summary>Seconds the result card holds before fading itself out.</summary>
            public float ResultHoldSec = 6f;

            public List<string> Instructions = new();
            public string IntroVoiceUrl;
            public RhythmCountdownSpec Countdown = new();
            public RhythmWorldSpec World = new();
        }

        /// <summary>
        /// The track. <see cref="Bpm"/> was measured with librosa, not guessed, and
        /// <see cref="Offset"/> places beatmap t=0 far enough in that the first note lands on a real
        /// beat. In this port the clock is <c>AudioSettings.dspTime</c>, not the clip's own
        /// playhead - see <c>Minigame/Rhythm/Conductor.cs</c>.
        /// </summary>
        public class RhythmSongSpec
        {
            public string Url;
            public float Bpm = 112.35f;
            public float Offset = 0.25f;
        }

        /// <summary>Where the hit ring sits, as a percentage of track width from the left.</summary>
        public class RhythmUiSpec
        {
            public float RingPct = 10f;
        }

        /// <summary>Judgment windows in seconds - absolute |press − note.time|.</summary>
        public class HitWindowsSpec
        {
            public float Perfect = 0.05f;
            public float Good = 0.13f;
        }

        public class ScoreValuesSpec
        {
            public int Perfect = 100;
            public int Good = 50;
            public int Miss;
        }

        /// <summary>
        /// The dance clips. In the web these are CLIP-ONLY GLBs so the dancer can wear whichever
        /// roster character was picked; here they are Humanoid <c>AnimationClip</c>s, which is the
        /// same idea with the engine doing the retarget. The URLs are read for their FILE NAMES,
        /// which is how the port maps a clip to its imported asset.
        /// </summary>
        public class RhythmDancerSpec
        {
            /// <summary>Base groove, looping.</summary>
            public string IdleUrl;

            /// <summary>Four flourishes, one picked at random per successful hit.</summary>
            public List<string> HitUrls = new();

            public string MissUrl;
            public string WinUrl;
            public string FailUrl;

            /// <summary>Absorbs the start/end-pose mismatch between arbitrary Mixamo clips.</summary>
            public float CrossFadeSec = 0.2f;

            /// <summary>Metres. The body is normalised to this, so export units do not matter.</summary>
            public float TargetHeight = 1.7f;
        }

        /// <summary>
        /// The beatmap is GENERATED at load from a BPM and a difficulty ramp - there is no authored
        /// note file in either build. Density ramps; note travel speed never does.
        /// </summary>
        public class RhythmBeatmapSpec
        {
            /// <summary>First note lands here, so the song's intro plays before anything falls.</summary>
            public float StartSec = 10f;

            /// <summary>Generate up to here. The track itself runs ~280 s.</summary>
            public float EndSec = 110f;

            public List<RhythmPhaseSpec> Phases = new();
        }

        /// <summary>From <see cref="FromSec"/> on, place a note every <see cref="BeatsPerNote"/> beats.</summary>
        public class RhythmPhaseSpec
        {
            public float FromSec;

            /// <summary>Bigger = sparser = easier. 2 → half-time, 1 → one per beat.</summary>
            public float BeatsPerNote = 2f;

            /// <summary>Chance of an extra eighth-note burst. Unset everywhere in the original.</summary>
            public float DoubleChance;
        }

        /// <summary>Remy's random hype on a good hit - occasional by design, not on every note.</summary>
        public class RhythmCheerSpec
        {
            public List<string> Urls = new();
            public float Chance = 0.25f;
            public float CooldownSec = 6f;

            /// <summary>Web gain, &gt;1 to push past unity. Unity clamps volume at 1 - see the port.</summary>
            public float Gain = 1.6f;
        }

        public class RhythmCountdownSpec
        {
            public List<string> Steps = new();

            /// <summary>Seconds each step holds on screen.</summary>
            public float StepSec = 0.7f;
        }

        /// <summary>Where the dance happens in the open world: the giver, the stage, the camera.</summary>
        public class RhythmWorldSpec
        {
            public RhythmNpcSpec Npc = new();
            public RhythmStageSpec Stage = new();
            public RhythmCameraSpec Camera = new();
        }

        /// <summary>
        /// The mission-giver on the dry sand. He is Remy, and he grooves through the whole run -
        /// the GTA-style partner you dance to, not a static quest marker.
        /// </summary>
        public class RhythmNpcSpec
        {
            public string IdleUrl;
            public string WalkUrl;

            /// <summary>His looping dance clip, played for the duration of the routine.</summary>
            public string DanceUrl;

            /// <summary>
            /// A three.js-only correction for a glTF clip on an FBX skeleton. 0 in the original now,
            /// and irrelevant here - Humanoid retargeting has no such mismatch.
            /// </summary>
            public float DanceRotationX;

            public float X;
            public float Z;

            /// <summary>Facing in RADIANS, right-handed. Negate it - see <see cref="Convert.Yaw"/>.</summary>
            public float Yaw;

            /// <summary>Press-to-start range in metres.</summary>
            public float TalkRadius = 4.5f;
        }

        /// <summary>Where the player-dancer performs - a couple of metres in front of the giver.</summary>
        public struct RhythmStageSpec
        {
            public float X;
            public float Y;
            public float Z;
            public float Yaw;

            /// <summary>Unconverted. Feed it to <see cref="Convert.Pos(Vector3)"/>.</summary>
            public Vector3 Raw => new Vector3(X, Y, Z);
        }

        /// <summary>
        /// Behind/over-the-shoulder framing. The offset is rotated by the dancer's own facing, so
        /// −z stays behind their back whichever way the stage points.
        /// </summary>
        public class RhythmCameraSpec
        {
            public Vec3 Offset;
            public float LookY = 1.3f;
        }
    }
}
