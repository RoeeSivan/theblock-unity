using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace TheBlock.Audio
{
    /// <summary>
    /// A police siren, on the car, in the world.
    ///
    /// <b>This is the clearest "Unity wins" in U27, and the web's own comment is the evidence.</b>
    /// <c>pursuit-audio.ts</c> fires ONE wail on the false→true edge of the chase and says why:
    /// "a continuous loop was unbearable". It was unbearable because it could not be anything else -
    /// three.js has no <c>AudioListener</c> in that build, so every sound in the game plays at a
    /// constant gain regardless of where it is. A siren that is exactly as loud when the cruiser is
    /// 300 m away as when it is on your bumper is not a siren, it is an alarm.
    ///
    /// With a listener and rolloff it becomes the opposite: the thing that tells you the response is
    /// coming, from which direction, and how close it has got. U19 gave the police a real 15-60 s
    /// drive from the station and called that "a mechanic rather than a cost" - this is what makes
    /// that drive perceptible instead of a number in the ledger.
    ///
    /// <b>Linear rolloff, not Unity's logarithmic default.</b> Logarithmic falls off so steeply that
    /// a siren is inaudible by 60 m, which would delete the whole point. Linear from
    /// <see cref="MinDistance"/> to <see cref="MaxDistance"/> keeps it audible across the block it
    /// is driving down. There is no web number to port here: the web has no distance at all.
    ///
    /// <b>Doppler is left ON</b> (Unity's default of 1). A cruiser passing at 20 m/s is exactly the
    /// case the effect exists for, and it costs nothing.
    ///
    /// The concurrency cap lives in <see cref="Arbitrate"/> rather than on the cop, because "the
    /// three nearest" is not a fact any one car knows.
    /// </summary>
    public class Siren : MonoBehaviour
    {
        /// <summary>Full volume inside this radius, metres.</summary>
        public const float MinDistance = 12f;

        /// <summary>Silent past this, metres. About two long blocks.</summary>
        public const float MaxDistance = 260f;

        private static readonly List<Siren> Live = new();

        [SerializeField] private AudioClip clip;
        [SerializeField] private AudioMixerGroup output;

        [Range(0f, 1f)]
        [SerializeField] private float volume = 0.55f;

        private AudioSource _source;

        /// <summary>Set every frame by <c>PoliceSystem</c>: this car is answering or chasing.</summary>
        public bool Wanted { get; set; }

        /// <summary>True while this siren actually holds one of the concurrency slots.</summary>
        public bool Sounding => _source != null && _source.isPlaying;

        /// <summary>Sirens sounding right now. The measurement pass asserts on this.</summary>
        public static int SoundingCount
        {
            get
            {
                int n = 0;
                foreach (var siren in Live)
                    if (siren != null && siren.Sounding)
                        n++;
                return n;
            }
        }

        private void OnEnable() => Live.Add(this);

        private void OnDisable()
        {
            Live.Remove(this);
            if (_source != null) _source.Stop();
        }

        private void Awake()
        {
            if (!TryGetComponent(out _source))
                _source = gameObject.AddComponent<AudioSource>();

            _source.playOnAwake = false;
            _source.loop = true;
            _source.spatialBlend = 1f; // fully 3D - the entire reason this component exists
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = MinDistance;
            _source.maxDistance = MaxDistance;
            _source.volume = volume;
        }

        private void Start()
        {
            // Pulled from the game's audio host rather than wired per car: cops are built at runtime
            // by PoliceSystem and there is no prefab step to fill a field in.
            var audio = GameAudio.Instance;
            if (audio == null) return;
            if (clip == null) clip = audio.SirenClip;
            if (output == null) output = audio.Group(GameAudio.Bus.Sfx);
            _source.clip = clip;
            _source.outputAudioMixerGroup = output;
        }

        /// <summary>
        /// Turns on the <paramref name="cap"/> nearest sirens that want to sound, and turns every
        /// other one off. Called once a frame by <see cref="GameAudio"/>.
        ///
        /// The city can only field one cruiser per star, so three is already the ceiling the police
        /// system imposes - this is the belt to that pair of braces, and it is also what makes the
        /// mix predictable if a future unit ever deploys more.
        /// </summary>
        public static void Arbitrate(Vector3 listener, int cap)
        {
            // Cheap insertion pass rather than a sort: the list is never longer than the police
            // force, which is three.
            for (int round = 0; round < cap; round++)
            {
                Siren best = null;
                float bestSqr = float.MaxValue;
                foreach (var siren in Live)
                {
                    if (siren == null || !siren.Wanted || siren._granted) continue;
                    float sqr = (siren.transform.position - listener).sqrMagnitude;
                    if (sqr >= bestSqr) continue;
                    best = siren;
                    bestSqr = sqr;
                }

                if (best == null) break;
                best._granted = true;
            }

            foreach (var siren in Live)
            {
                if (siren == null) continue;
                bool on = siren._granted;
                siren._granted = false; // consumed; re-decided next frame

                if (siren._source == null) continue;
                if (on && !siren._source.isPlaying && siren._source.clip != null) siren._source.Play();
                else if (!on && siren._source.isPlaying) siren._source.Stop();
            }
        }

        private bool _granted;
    }
}
