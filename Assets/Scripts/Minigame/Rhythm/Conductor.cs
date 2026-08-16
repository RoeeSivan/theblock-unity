using UnityEngine;

namespace TheBlock.Minigame.Rhythm
{
    /// <summary>
    /// The music clock — the single source of truth for every timing decision the dance makes. The
    /// port of <c>src/minigame/rhythm/conductor.ts</c>, and <b>this unit's answer to the standing
    /// "can Unity do this better?" question.</b>
    ///
    /// The web reads <c>audioElement.currentTime</c>. That is the best a browser offers, and it is
    /// not very good: it updates on the main thread, it is quantised to the decode buffer, and it
    /// jitters against the frame. A rhythm game's whole job is deciding whether a press was within
    /// 50 ms of a beat, so clock noise is not a rendering detail — it is the scoring being wrong.
    ///
    /// Unity has <see cref="AudioSettings.dspTime"/>: a sample counter maintained on the AUDIO
    /// thread, monotonic and immune to frame hitches. This project has a measured 42 ms frame and
    /// intermittent ~800 ms stalls in its Deferred list; against <c>currentTime</c> a stall like
    /// that is a fistful of phantom misses, and against the DSP clock it is a visual hiccup that
    /// costs nothing. The song position is <c>dspTime − startDsp</c>, and the clip's own playhead is
    /// kept only as a DRIFT CHECK, never as the answer.
    ///
    /// The web's wall-clock fallback (for a missing MP3) has no equivalent and needs none: a missing
    /// AudioClip is a build error here, catchable before anyone plays, not a runtime condition.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class Conductor : MonoBehaviour
    {
        [SerializeField] private AudioSource source;
        [SerializeField] private AudioClip song;

        [Tooltip("The Music bus. Wired by The Block → Build Audio; null routes to the master output.")]
        [SerializeField] private UnityEngine.Audio.AudioMixerGroup output;

        /// <summary>Where the beatmap's t=0 sits inside the track, in seconds.</summary>
        private float _offset;

        private double _startDsp;
        private bool _running;

        /// <summary>The song has a clip and can actually be played.</summary>
        public bool Ready => Clip != null;

        public AudioClip Clip => song != null ? song : source != null ? source.clip : null;

        /// <summary>Seconds since the beatmap's t=0. Negative before the first note's reference.</summary>
        public float SongTime =>
            _running ? (float)(AudioSettings.dspTime - _startDsp) - _offset : -_offset;

        /// <summary>Track length, or 0 before a clip is set.</summary>
        public float Duration => Clip != null ? Clip.length : 0f;

        /// <summary>The track has run out. The routine ends on this or on its own note tail.</summary>
        public bool Ended => _running && Clip != null &&
                             AudioSettings.dspTime - _startDsp >= Clip.length;

        /// <summary>
        /// How far the DSP clock has drifted from the clip's own playhead, in seconds.
        ///
        /// Diagnostic only — nothing reads it to make a decision. It exists because "the dsp clock
        /// is better" is a claim, and a claim in this project gets a number next to it.
        /// </summary>
        public float Drift =>
            !_running || source == null || !source.isPlaying
                ? 0f
                : (float)(AudioSettings.dspTime - _startDsp) - source.time;

        private void Awake()
        {
            if (source == null && !TryGetComponent(out source))
                source = gameObject.AddComponent<AudioSource>();

            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f; // the soundtrack, not a speaker on the beach

            // U27 routed the song onto its own bus. It changes nothing about the CLOCK — the anchor
            // is still a dsp instant and the position is still dspTime − startDsp — but a mixer group
            // is inserted into this source's output chain, so the drift measurement is worth
            // repeating rather than assumed. It was 0.02 ms before.
            source.outputAudioMixerGroup = output;
        }

        /// <summary>Rewinds and starts the track. <paramref name="offset"/> is the config's.</summary>
        public void Play(float offset)
        {
            if (source == null) return;
            _offset = offset;

            if (song != null) source.clip = song;
            if (source.clip == null)
            {
                Debug.LogError("Conductor: no song clip — the routine has nothing to keep time to.");
                return;
            }

            source.time = 0f;

            // PlayScheduled against a dsp instant, rather than Play() and then asking when it
            // started. Play() begins somewhere inside the next audio buffer, so the anchor would be
            // off by up to a buffer's worth — which at 50 ms judgment windows is a real fraction of
            // a perfect. Scheduling names the instant, so the anchor is exact by construction.
            var start = AudioSettings.dspTime + 0.1;
            source.PlayScheduled(start);

            // …and then the anchor moves by the OUTPUT LATENCY, which is U27's doing and was caught
            // by re-running U22's own drift measurement after the song was put on a mixer bus.
            //
            // U22 measured 0.02 ms of drift with this source wired straight to the default output.
            // With an AudioMixerGroup in the chain the same measurement reads **21.3 ms**, dead
            // stable — and 21.3 ms is not noise, it is exactly 1024 / 48000, one DSP buffer. The
            // group is processed a buffer behind the source, so what reaches the speakers is a
            // buffer later than the instant we scheduled. The clock was never wrong; the SOUND
            // moved. Against a 50 ms Perfect window that is 43% of the window, biased one way, on
            // every note — the kind of fault a play-test reports as "the timing feels off" and
            // nobody traces to a routing change.
            //
            // So the beatmap's t=0 is placed where the music actually arrives. `Drift` goes back to
            // ~0, which is the invariant U22 established and measured.
            _startDsp = start + OutputLatency();
            _running = true;
        }

        /// <summary>
        /// Seconds between a scheduled sample and it leaving the speakers, for THIS routing.
        ///
        /// Zero with no mixer group: a source on the default output is already accounted for by
        /// <see cref="AudioSettings.dspTime"/>, which is what made U22's 0.02 ms possible.
        /// </summary>
        public double OutputLatency()
        {
            if (source == null || source.outputAudioMixerGroup == null) return 0.0;
            var config = AudioSettings.GetConfiguration();
            if (config.sampleRate <= 0 || config.dspBufferSize <= 0) return 0.0;
            return config.dspBufferSize / (double)config.sampleRate;
        }

        public void Stop()
        {
            if (source != null) source.Stop();
            _running = false;
        }

        /// <summary>Editor-side wiring, used by The Block → Build Campaign.</summary>
        public void SetSong(AudioClip clip) => song = clip;

        /// <summary>Editor-side wiring, used by The Block → Build Audio.</summary>
        public void SetOutput(UnityEngine.Audio.AudioMixerGroup group)
        {
            output = group;
            if (source != null) source.outputAudioMixerGroup = group;
        }
    }
}
