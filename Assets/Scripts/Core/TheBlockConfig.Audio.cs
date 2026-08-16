using System.Collections.Generic;

namespace TheBlock.Core
{
    /// <summary>
    /// U27's half of the config model: the two audio blocks inside <c>config.ts</c>
    /// (<c>engineAudio</c>, <c>ambientAudio</c>) plus the eighth exported source,
    /// <c>src/audio/scream.config.ts</c>.
    ///
    /// Split into its own file only for length — see the partial declaration in
    /// <c>TheBlockConfig.cs</c>. Same rules as the rest: nothing is converted here.
    ///
    /// <b><c>config.radio</c> is deliberately NOT declared.</b> The car radio streams five live
    /// SomaFM stations and is deferred to its own pass; a spec class for a system nothing reads is
    /// the same dead branch <c>GameMode.Transition</c> was.
    /// </summary>
    public static partial class TheBlockConfig
    {
        /// <summary>
        /// <c>config.engineAudio</c> — one looped recording per vehicle type, pitch and gain
        /// following throttle.
        /// </summary>
        public class EngineAudioSpec
        {
            /// <summary>
            /// Web Audio's <c>setTargetAtTime</c> time-constant for pitch/volume tracking, seconds.
            /// Unity has no equivalent parameter ramp, so <c>EngineSound</c> applies the same
            /// exponential per frame: <c>k = 1 − exp(−dt / smooth)</c>.
            /// </summary>
            public float Smooth = 0.08f;

            /// <summary>Fade-out time-constant when the rider steps off, seconds.</summary>
            public float SpinDown = 0.4f;

            /// <summary>Keyed <c>car</c> / <c>motorcycle</c> / <c>jetski</c>.</summary>
            public Dictionary<string, EngineTypeSpec> Types = new();
        }

        /// <summary>One vehicle type's loop and its throttle curve.</summary>
        public class EngineTypeSpec
        {
            /// <summary>Web URL, e.g. <c>/audio/engine/car.wav</c>. The key into the audio library.</summary>
            public string File;

            /// <summary>
            /// Where the loop must restart, seconds — the ORIGINAL ogg duration.
            ///
            /// The shipped WAVs carry 7–17 ms of decoder overlap tail past this. Web Audio has
            /// <c>source.loopEnd</c> to ignore it; Unity's <c>AudioSource</c> does not, so
            /// <see cref="TheBlock.Audio.EngineSound"/> trims the clip to this length at load.
            /// Measured: car 0.882358 s of file against 0.864943 here.
            /// </summary>
            public float LoopEndSec;

            /// <summary>Playback rate at a standstill, and at max speed. Unity's <c>pitch</c>.</summary>
            public float IdleRate = 1f;

            public float MaxRate = 1f;

            /// <summary>Gain at a standstill, and at max speed.</summary>
            public float IdleVol;

            public float MaxVol;
        }

        /// <summary>
        /// <c>config.ambientAudio</c> — the city murmur and the ocean bed, crossfaded by where the
        /// player is standing, with a sparse spot-sound layer over the top.
        /// </summary>
        public class AmbientAudioSpec
        {
            /// <summary>Crossfade time-constant, seconds. Slow enough to be unnoticeable.</summary>
            public float Smooth = 0.5f;

            public AmbientBedsSpec Beds;

            public AmbientBlendSpec Blend;

            public AmbientDuckSpec Duck;

            public AmbientOneShotsSpec OneShots;
        }

        public class AmbientBedsSpec
        {
            public AmbientClipSpec Street;
            public AmbientClipSpec Beach;
        }

        public class AmbientClipSpec
        {
            public string File;
            public float Volume;
        }

        /// <summary>
        /// Crossfade geometry, derived off <c>config.sea</c>. <c>beachness</c> ramps 0→1 over
        /// <see cref="WidthX"/> metres east of the shore, and only inside a Z band
        /// <see cref="ZHalf"/> either side of the beach centre.
        /// </summary>
        public class AmbientBlendSpec
        {
            public float WidthX = 90f;
            public float ZHalf = 220f;
        }

        /// <summary>
        /// Per-state gain multipliers on the ambient mix. In the web these multiply into every bed
        /// gain by hand; here they are the volume of the <c>Ambient</c> mixer group, so one number
        /// moves the whole bus and U26's slider can sit on the same parameter.
        /// </summary>
        public class AmbientDuckSpec
        {
            public float Driving = 0.35f;
            public float Interior;
            public float Rhythm;
        }

        public class AmbientOneShotsSpec
        {
            /// <summary>Shortest and longest wait before the next spot-sound roll, seconds.</summary>
            public float MinGapSec = 5f;

            public float MaxGapSec = 13f;

            public AmbientZoneSpec Street;
            public AmbientZoneSpec Beach;
        }

        /// <summary>One zone's spot-sound pool. The dominant bed picks which one is rolled.</summary>
        public class AmbientZoneSpec
        {
            public List<string> Urls = new();

            /// <summary>Probability the roll actually fires, 0..1.</summary>
            public float Chance = 0.7f;

            public float Gain = 0.5f;

            /// <summary>
            /// Fades so a spot sound swells instead of snapping on. The street stays near-instant
            /// (a honk should punch); the gulls get a slow swell, because their hard onset was
            /// startling.
            /// </summary>
            public float AttackSec = 0.02f;

            public float ReleaseSec = 0.05f;
        }

        /// <summary>
        /// <c>src/audio/scream.config.ts</c> — the run-over voice pools and the throttle that keeps
        /// a plowed pavement from becoming one loud mush.
        /// </summary>
        public class ScreamSpec
        {
            public bool Enabled = true;

            /// <summary>Playback gain for the voice clip. The thud's level is baked into the synth.</summary>
            public float Volume = 0.7f;

            /// <summary>Layer the synthesized body-impact under the voice.</summary>
            public bool Thud = true;

            /// <summary>
            /// Voices allowed to overlap. A bumper box downs every person inside it in ONE frame, so
            /// plowing a busy pavement offers ~5 screams at once. Two still says "I hit a group".
            /// </summary>
            public int MaxConcurrent = 2;

            /// <summary>Hard floor between two scream starts, seconds — the other half of that defence.</summary>
            public float MinGapSec = 0.18f;

            public List<string> Male = new();
            public List<string> Female = new();
        }
    }
}
