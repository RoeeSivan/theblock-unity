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
            _startDsp = AudioSettings.dspTime + 0.1;
            source.PlayScheduled(_startDsp);
            _running = true;
        }

        public void Stop()
        {
            if (source != null) source.Stop();
            _running = false;
        }

        /// <summary>Editor-side wiring, used by The Block → Build Campaign.</summary>
        public void SetSong(AudioClip clip) => song = clip;
    }
}
