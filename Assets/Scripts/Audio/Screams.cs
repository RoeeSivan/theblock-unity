using UnityEngine;
using UnityEngine.Audio;
using TheBlock.Core;

namespace TheBlock.Audio
{
    /// <summary>Which pool a face screams from when it is run over. The web's <c>gender: 'm' | 'f'</c>.</summary>
    public enum ScreamVoice
    {
        Male,
        Female,
    }

    /// <summary>
    /// The voice of a pedestrian being run over — the port of <c>src/audio/scream.ts</c>. A short
    /// scream from the male or female pool with the synthesized body thud under it, fired once on
    /// the impact frame by <c>RunOverReaction.Begin</c>.
    ///
    /// <b>No spatial audio, on purpose.</b> The victim is by definition a couple of metres under the
    /// player's own bumper, so a constant gain is the correct mix. This is the one sound in U27 that
    /// had a real claim on 3D and does not get it.
    ///
    /// <b>The throttle is the feature, not an optimisation.</b> A bumper box downs EVERY person
    /// inside it in a single frame — U18 measured sixteen at once — so plowing a pavement offers up
    /// five screams simultaneously, which reads as one loud mush instead of a reaction.
    /// <c>maxConcurrent</c> 2 still says "I hit a group", and <c>minGapSec</c> 0.18 is the other half
    /// of the same defence: it spaces out a fast run along a LINE of people, where the hits are
    /// frames rather than metres apart.
    ///
    /// <b>One thing the web had to work around is simply gone.</b> Its concurrency counter is
    /// decremented by <c>onended</c>, which a suspended context can swallow — leaving the count
    /// wedged at the cap and every later hit silent forever, a dead feature with no error. It needs
    /// a <c>quietBy</c> horizon to detect and drop a stale count. Here the count is
    /// <see cref="AudioSource.isPlaying"/> over the pool: the question is asked of the thing that
    /// knows the answer, so there is no counter to go stale.
    ///
    /// <b>The clock is <see cref="AudioSettings.dspTime"/>, not <c>Time.time</c></b>, matching the
    /// web's use of the audio context clock — and it matters here for a reason particular to this
    /// project: measurements in <c>PORT-STATUS.md</c> are taken with <c>timeScale</c> at 0.02, and a
    /// scaled clock would make the 0.18 s gap read as 9 seconds of wall time.
    /// </summary>
    public class Screams : MonoBehaviour
    {
        [SerializeField] private AudioLibrary library;
        [SerializeField] private AudioMixerGroup output;
        [SerializeField] private Sfx sfx;

        private TheBlockConfig.ScreamSpec _spec;
        private AudioClip[] _male = System.Array.Empty<AudioClip>();
        private AudioClip[] _female = System.Array.Empty<AudioClip>();

        private AudioSource[] _pool;
        private double _lastStart = double.NegativeInfinity;
        private AudioClip _lastClip;

        /// <summary>Voices sounding right now. The measurement pass asserts on this.</summary>
        public int Live
        {
            get
            {
                if (_pool == null) return 0;
                int live = 0;
                foreach (var source in _pool)
                    if (source != null && source.isPlaying)
                        live++;
                return live;
            }
        }

        /// <summary>dsp time of the last accepted scream. Diagnostic.</summary>
        public double LastStart => _lastStart;

        /// <summary>Editor-side wiring, used by <c>The Block → Build Audio</c>.</summary>
        public void Configure(AudioLibrary clips, AudioMixerGroup group, Sfx effects)
        {
            library = clips;
            output = group;
            sfx = effects;
        }

        private void Awake()
        {
            _spec = TheBlockConfig.Load()?.Scream;
            if (_spec == null)
            {
                Debug.LogWarning("Screams: no screamConfig in the snapshot — run tools/export-config.sh.");
                _spec = new TheBlockConfig.ScreamSpec();
            }

            if (library != null)
            {
                _male = library.ResolveAll(_spec.Male);
                _female = library.ResolveAll(_spec.Female);
            }

            // One source per allowed voice. The cap IS the pool, so the throttle cannot be violated
            // by a caller that forgets to ask.
            int voices = Mathf.Max(1, _spec.MaxConcurrent);
            _pool = new AudioSource[voices];
            for (int i = 0; i < voices; i++)
            {
                var host = new GameObject($"Scream Voice {i}");
                host.transform.SetParent(transform, false);
                var source = host.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.outputAudioMixerGroup = output;
                _pool[i] = source;
            }
        }

        /// <summary>
        /// Fires the reaction for one victim. Self-throttling — safe to call once per person knocked
        /// down, including several in the same frame.
        /// </summary>
        public void Play(ScreamVoice voice)
        {
            if (_spec == null || !_spec.Enabled || _pool == null) return;

            double now = AudioSettings.dspTime;
            if (now - _lastStart < _spec.MinGapSec) return;

            var free = FreeSource();
            if (free == null) return; // every voice is busy: that is maxConcurrent, enforced

            // Thud first and unconditionally: it is the impact itself, so it must still be heard on a
            // face whose voice clip is missing. It sits INSIDE the throttle so it cannot machine-gun.
            if (_spec.Thud && sfx != null) sfx.Play(SfxCue.BodyThud);

            _lastStart = now;

            var pool = voice == ScreamVoice.Female ? _female : _male;
            var clip = Pick(pool);
            if (clip == null) return; // pool empty → the thud alone, which is the web's behaviour

            _lastClip = clip;
            free.clip = clip;
            free.volume = _spec.Volume;
            free.Play();
        }

        private AudioSource FreeSource()
        {
            foreach (var source in _pool)
                if (source != null && !source.isPlaying)
                    return source;
            return null;
        }

        /// <summary>A clip from the pool, never the same one twice in a row.</summary>
        private AudioClip Pick(AudioClip[] pool)
        {
            if (pool == null || pool.Length == 0) return null;
            if (pool.Length == 1) return pool[0];

            for (int attempt = 0; attempt < 8; attempt++)
            {
                var clip = pool[Random.Range(0, pool.Length)];
                if (clip != _lastClip) return clip;
            }

            return pool[0];
        }
    }
}
