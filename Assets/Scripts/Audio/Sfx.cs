using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace TheBlock.Audio
{
    /// <summary>Every synthesized cue in the game. The port of <c>sfx.ts</c>'s exported surface.</summary>
    public enum SfxCue
    {
        /// <summary>Menu button tick. Short, soft, high.</summary>
        UiClick,

        /// <summary>The dull body impact under a run-over scream. Darker and softer than a crash.</summary>
        BodyThud,

        /// <summary>Two-note ding on a successful delivery — or a rooftop survivor reached.</summary>
        Delivery,

        /// <summary>Ascending arpeggio fanfare: a mission is complete.</summary>
        Complete,

        /// <summary>Descending dissonant sting: a mission failed.</summary>
        Fail,

        /// <summary>Rhythm hit, bright — a Perfect.</summary>
        RhythmPerfect,

        /// <summary>Rhythm hit, duller — a Good.</summary>
        RhythmGood,

        /// <summary>Rhythm miss: a low thunk.</summary>
        RhythmMiss,

        /// <summary>Faint metronome click on each beat.</summary>
        Beat,

        /// <summary>Countdown: one beep per number.</summary>
        CountTick,

        /// <summary>Countdown: the brighter beep on GO.</summary>
        CountGo,

        /// <summary>The vehicle rammed something solid at speed.</summary>
        Crash,

        /// <summary>A cop got you. Lower and longer than <see cref="Fail"/>.</summary>
        Busted,

        /// <summary>U28 — the pump counter ticking over.</summary>
        FuelTick,

        /// <summary>U28 — nozzle clunk plus a rising confirm: the tank is full.</summary>
        FuelDone,

        /// <summary>U28 — the convenience-store door chime.</summary>
        StoreChime,

        /// <summary>U28 — the till: a power-up was bought.</summary>
        Purchase,

        /// <summary>U28 — a power-up ignited.</summary>
        PowerUp,

        /// <summary>U28 — its timer ran out. The same figure, falling.</summary>
        PowerDown,

        /// <summary>U28 — the press did nothing. Deliberately quiet.</summary>
        Deny,
    }

    /// <summary>
    /// The game's sound effects: twenty cues with no asset behind any of them, baked to clips by
    /// <see cref="SfxSynth"/> and played through a small pool of 2D sources.
    ///
    /// <b>The note data below is the web's, number for number.</b> Every frequency, offset, duration
    /// and peak is copied out of <c>src/audio/sfx.ts</c> rather than re-voiced, because these were
    /// tuned against each other — <c>Fail</c> is sour specifically next to <c>Complete</c>,
    /// <c>BodyThud</c> is deliberately darker and shorter than <c>Crash</c>, and <c>PowerDown</c> is
    /// the same three notes as <c>PowerUp</c> falling so the pair reads as one effect starting and
    /// ending. Re-deriving them by feel would quietly dissolve all of that.
    ///
    /// <b>Five cues are built and wired to nothing.</b> Fuel, the store chime, the till, the
    /// power-up pair and the deny tick belong to U28's economy, which has no call sites yet. They
    /// are note data, they cost nothing until something asks for one, and building them here means
    /// U28 does no audio work at all. Nothing dead is switched on: an unplayed cue is never baked.
    ///
    /// <b>Lazy, then cached.</b> A cue renders the first time it is asked for. Twenty short mono
    /// clips is well under a megabyte in total, but most runs never reach half of them.
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        [Tooltip("Voices that can overlap. A rhythm run and a pile-up are the two that need several.")]
        [SerializeField] private int voices = 8;

        [Range(0f, 1f)]
        [Tooltip("Master trim on top of the Sfx mixer group.")]
        [SerializeField] private float volume = 1f;

        [SerializeField] private AudioMixerGroup output;

        private readonly Dictionary<SfxCue, AudioClip> _baked = new();
        private AudioSource[] _pool;
        private int _next;

        /// <summary>Cues rendered so far. Read by the measurement pass.</summary>
        public int BakedCount => _baked.Count;

        private void Awake()
        {
            _pool = new AudioSource[Mathf.Max(1, voices)];
            for (int i = 0; i < _pool.Length; i++)
            {
                var host = new GameObject($"Sfx Voice {i}");
                host.transform.SetParent(transform, false);
                var source = host.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f; // 2D: a UI tick and a crash both belong at the mix, not in the world
                source.outputAudioMixerGroup = output;
                _pool[i] = source;
            }
        }

        /// <summary>Editor-side wiring, used by <c>The Block → Build Audio</c>.</summary>
        public void SetOutput(AudioMixerGroup group) => output = group;

        /// <summary>Fires a cue. Safe to call every frame — the caller owns its own throttling.</summary>
        public void Play(SfxCue cue, float gain = 1f)
        {
            if (_pool == null || _pool.Length == 0) return;

            if (!_baked.TryGetValue(cue, out var clip))
            {
                clip = SfxSynth.Render($"sfx-{cue}", SpecFor(cue));
                _baked[cue] = clip;
            }

            // Round-robin, oldest first. Stealing a voice is correct here: a cue is 50–900 ms and
            // eight of them sounding at once is already a mush nobody authored.
            var source = _pool[_next];
            _next = (_next + 1) % _pool.Length;
            source.Stop();
            source.clip = clip;
            source.volume = Mathf.Clamp01(volume * gain);
            source.Play();
        }

        /// <summary>The web's <c>playRhythmHit(judgment)</c>: brighter for a Perfect than a Good.</summary>
        public void PlayRhythmHit(bool perfect) =>
            Play(perfect ? SfxCue.RhythmPerfect : SfxCue.RhythmGood);

        /// <summary>Renders every cue up front. Used by the measurement pass, not by the game.</summary>
        public void BakeAll()
        {
            foreach (SfxCue cue in System.Enum.GetValues(typeof(SfxCue)))
                if (!_baked.ContainsKey(cue))
                    _baked[cue] = SfxSynth.Render($"sfx-{cue}", SpecFor(cue));
        }

        /// <summary>The baked clip for a cue, rendering it if it has not been asked for yet.</summary>
        public AudioClip ClipFor(SfxCue cue)
        {
            if (!_baked.TryGetValue(cue, out var clip))
            {
                clip = SfxSynth.Render($"sfx-{cue}", SpecFor(cue));
                _baked[cue] = clip;
            }

            return clip;
        }

        // Equal-temperament note frequencies, named as sfx.ts names them.
        private const float C5 = 523.25f;
        private const float E5 = 659.25f;
        private const float G5 = 783.99f;
        private const float B5 = 987.77f;
        private const float C6 = 1046.5f;
        private const float A4 = 440.0f;

        /// <summary>
        /// What each cue is made of. One switch, so the whole voicing of the game is readable in one
        /// screen — which is how it stays comparable to <c>sfx.ts</c>.
        /// </summary>
        public static SfxSynth.Cue SpecFor(SfxCue cue)
        {
            var c = new SfxSynth.Cue();
            switch (cue)
            {
                case SfxCue.UiClick:
                    return c.Add(C6, 0f, 0.05f, SfxSynth.Wave.Triangle, 0.14f);

                case SfxCue.BodyThud:
                    // Heavily lowpassed: a flat slap with no metallic edge, and a sine rather than a
                    // sawtooth underneath — weight without the engine-y buzz.
                    return c.AddNoise(0f, 0.12f, 380f, 0.22f)
                        .Add(58f, 0f, 0.13f, SfxSynth.Wave.Sine, 0.18f);

                case SfxCue.Delivery:
                    return c.Add(G5, 0f, 0.12f).Add(C6, 0.09f, 0.18f);

                case SfxCue.Complete:
                    return c.Add(C5, 0f, 0.16f)
                        .Add(E5, 0.12f, 0.16f)
                        .Add(G5, 0.24f, 0.16f)
                        .Add(C6, 0.36f, 0.42f, SfxSynth.Wave.Triangle, 0.3f);

                case SfxCue.Fail:
                    // Two-note descending sawtooth: clearly sour next to Complete.
                    return c.Add(E5, 0f, 0.16f, SfxSynth.Wave.Sawtooth, 0.22f)
                        .Add(A4 * 0.5f, 0.13f, 0.34f, SfxSynth.Wave.Sawtooth, 0.2f);

                case SfxCue.RhythmPerfect:
                    return c.Add(C6, 0f, 0.1f, SfxSynth.Wave.Triangle, 0.28f);

                case SfxCue.RhythmGood:
                    return c.Add(G5, 0f, 0.1f, SfxSynth.Wave.Triangle, 0.2f);

                case SfxCue.RhythmMiss:
                    return c.Add(A4 / 3f, 0f, 0.16f, SfxSynth.Wave.Sawtooth, 0.18f);

                case SfxCue.Beat:
                    return c.Add(A4, 0f, 0.03f, SfxSynth.Wave.Square, 0.05f);

                case SfxCue.CountTick:
                    return c.Add(E5, 0f, 0.12f, SfxSynth.Wave.Triangle, 0.22f);

                case SfxCue.CountGo:
                    return c.Add(C6, 0f, 0.22f, SfxSynth.Wave.Triangle, 0.3f);

                case SfxCue.Crash:
                    // Sheet metal, not a person: brighter noise and a sawtooth for weight.
                    return c.AddNoise(0f, 0.18f, 900f, 0.34f)
                        .Add(70f, 0f, 0.16f, SfxSynth.Wave.Sawtooth, 0.26f);

                case SfxCue.Busted:
                    return c.Add(A4 * 0.5f, 0f, 0.28f, SfxSynth.Wave.Sawtooth, 0.26f) // 220 Hz
                        .Add(A4 * 0.375f, 0.22f, 0.5f, SfxSynth.Wave.Sawtooth, 0.24f) // 165 Hz
                        .AddNoise(0f, 0.5f, 480f, 0.12f);

                case SfxCue.FuelTick:
                    // As faint as Beat — this fires ~10 times per fill and must not become a drill.
                    return c.Add(A4, 0f, 0.035f, SfxSynth.Wave.Square, 0.05f);

                case SfxCue.FuelDone:
                    return c.AddNoise(0f, 0.09f, 500f, 0.16f)
                        .Add(E5, 0.06f, 0.14f)
                        .Add(C6, 0.17f, 0.26f, SfxSynth.Wave.Triangle, 0.28f);

                case SfxCue.StoreChime:
                    // Sine, not the triangle every other cue uses — that is what makes it read as a
                    // doorbell rather than another game beep.
                    return c.Add(B5, 0f, 0.35f, SfxSynth.Wave.Sine, 0.2f)
                        .Add(E5, 0.18f, 0.9f, SfxSynth.Wave.Sine, 0.22f);

                case SfxCue.Purchase:
                    return c.AddNoise(0f, 0.05f, 2600f, 0.1f)
                        .Add(E5, 0.04f, 0.09f, SfxSynth.Wave.Square, 0.14f)
                        .Add(C6, 0.12f, 0.16f, SfxSynth.Wave.Triangle, 0.2f);

                case SfxCue.PowerUp:
                    return c.Add(C5, 0f, 0.09f, SfxSynth.Wave.Triangle, 0.22f)
                        .Add(G5, 0.07f, 0.12f, SfxSynth.Wave.Triangle, 0.24f)
                        .Add(C6, 0.15f, 0.26f, SfxSynth.Wave.Triangle, 0.26f);

                case SfxCue.PowerDown:
                    return c.Add(C6, 0f, 0.09f, SfxSynth.Wave.Triangle, 0.18f)
                        .Add(G5, 0.07f, 0.12f, SfxSynth.Wave.Triangle, 0.16f)
                        .Add(C5, 0.15f, 0.3f, SfxSynth.Wave.Triangle, 0.16f);

                case SfxCue.Deny:
                    return c.Add(A4 * 0.5f, 0f, 0.07f, SfxSynth.Wave.Square, 0.09f);

                default:
                    return c.Add(A4, 0f, 0.05f);
            }
        }
    }
}
