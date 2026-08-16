using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using TheBlock.Core;

namespace TheBlock.Audio
{
    /// <summary>
    /// The engine of whatever the player is driving — the port of <c>src/audio/engine-sound.ts</c>.
    /// One short looped recording per vehicle type, pitch and gain tracking throttle.
    ///
    /// <b>2D, and one instance, because the web is right about this one.</b> Only the vehicle you
    /// are sitting in gets an engine; the street's traffic is carried by the ambient bed. A spatial
    /// source on your own bumper buys nothing — the listener is two metres away and never moves
    /// relative to it — and thirty of them on the traffic would be a mix nobody has voiced.
    ///
    /// <b>THE LOOP SEAM IS THE WHOLE PROBLEM HERE, and it is not solved by a Unity feature.</b>
    /// Web Audio has <c>source.loopEnd</c>; the web pins it to the original ogg's duration because
    /// ffmpeg's Vorbis decoder emitted an overlap tail past it and the WAVs were cut from that.
    /// Unity's <see cref="AudioSource"/> has no <c>loopEnd</c> at all, so the honest answer was to
    /// MEASURE the three files against <c>config.engineAudio</c> and trim:
    ///
    /// <code>
    ///   car.wav         0.882358 s of file  vs  loopEndSec 0.864943  →  17.4 ms of tail
    ///   jetski.wav      0.766259 s          vs             0.759002  →   7.3 ms
    ///   motorcycle.wav  0.626939 s          vs             0.618231  →   8.7 ms
    /// </code>
    ///
    /// 17 ms on a 0.86 s loop is an audible tick <b>every cycle</b>, for as long as the car is
    /// running. So each clip is copied into a trimmed one at first use — which is also why the
    /// engine folder imports as uncompressed PCM: <see cref="AudioClip.GetData"/> on a Vorbis clip
    /// hands back nothing to trim.
    /// </summary>
    public class EngineSound : MonoBehaviour
    {
        /// <summary>The three keys in <c>config.engineAudio.types</c>.</summary>
        public const string Car = "car";
        public const string Motorcycle = "motorcycle";
        public const string Jetski = "jetski";

        [SerializeField] private AudioLibrary library;
        [SerializeField] private AudioMixerGroup output;
        [SerializeField] private AudioSource source;

        private TheBlockConfig.EngineAudioSpec _spec;
        private readonly Dictionary<string, AudioClip> _trimmed = new();

        private string _type;
        private bool _running;
        private float _targetPitch = 1f;
        private float _targetVolume;

        /// <summary>The loop that is running, or null. Read by the measurement pass.</summary>
        public string Running => _running ? _type : null;

        /// <summary>The trimmed clip for a type, so a test can compare its length to the config.</summary>
        public AudioClip TrimmedClip(string type) => Load(type);

        private void Awake()
        {
            _spec = TheBlockConfig.Load()?.Config?.EngineAudio;

            if (source == null)
            {
                var host = new GameObject("Engine Source");
                host.transform.SetParent(transform, false);
                source = host.AddComponent<AudioSource>();
            }

            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.outputAudioMixerGroup = output;
        }

        /// <summary>Editor-side wiring, used by <c>The Block → Build Audio</c>.</summary>
        public void Configure(AudioLibrary clips, AudioMixerGroup group)
        {
            library = clips;
            output = group;
        }

        /// <summary>Starts the idle loop for a vehicle type. Called as the rider sits down.</summary>
        public void StartEngine(string type)
        {
            if (_spec == null || string.IsNullOrEmpty(type)) return;
            if (!_spec.Types.TryGetValue(type, out var typeSpec) || typeSpec == null) return;

            var clip = Load(type);
            if (clip == null) return;

            _type = type;
            _running = true;
            source.clip = clip;
            source.pitch = typeSpec.IdleRate;
            source.volume = 0f;
            _targetPitch = typeSpec.IdleRate;
            _targetVolume = typeSpec.IdleVol;
            if (!source.isPlaying) source.Play();
        }

        /// <summary>Winds the engine down to silence. The source stops once the fade is inaudible.</summary>
        public void StopEngine()
        {
            _running = false;
            _targetVolume = 0f;
        }

        /// <summary>
        /// Tracks throttle from the current speed. Call every frame while driving; the smoothing is
        /// the config's own time-constant, so the sound leads the speedometer by the same amount the
        /// web's did.
        /// </summary>
        public void SetSpeed(float speedMs, float maxSpeedMs)
        {
            if (!_running || _spec == null || maxSpeedMs <= 0f) return;
            if (!_spec.Types.TryGetValue(_type ?? string.Empty, out var typeSpec) || typeSpec == null) return;

            float throttle = Mathf.Min(1f, Mathf.Abs(speedMs) / maxSpeedMs);
            _targetPitch = typeSpec.IdleRate + (typeSpec.MaxRate - typeSpec.IdleRate) * throttle;
            _targetVolume = typeSpec.IdleVol + (typeSpec.MaxVol - typeSpec.IdleVol) * throttle;
        }

        private void Update()
        {
            if (source == null || _spec == null) return;

            // Web Audio's setTargetAtTime is an exponential approach with a time constant; the same
            // curve per frame is k = 1 − exp(−dt / tau). Never a fixed lerp factor, which would make
            // the spin-up depend on the frame rate.
            float tau = _running ? _spec.Smooth : _spec.SpinDown;
            float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.0001f, tau));

            source.pitch = Mathf.Lerp(source.pitch, _targetPitch, k);
            source.volume = Mathf.Lerp(source.volume, _targetVolume, k);

            if (!_running && source.isPlaying && source.volume < 0.001f)
            {
                source.Stop();
                source.volume = 0f;
            }
        }

        /// <summary>
        /// The trimmed loop for a type, cut once and cached.
        ///
        /// Returns the untrimmed clip rather than nothing if the samples cannot be read — a tick
        /// once a second is a worse engine, but no engine at all is a broken one.
        /// </summary>
        private AudioClip Load(string type)
        {
            if (_trimmed.TryGetValue(type, out var cached)) return cached;
            if (_spec == null || library == null) return null;
            if (!_spec.Types.TryGetValue(type, out var typeSpec) || typeSpec == null) return null;

            var raw = library.Resolve(typeSpec.File);
            if (raw == null)
            {
                Debug.LogWarning($"EngineSound: no clip for '{typeSpec.File}' — {type} runs silent.");
                return null;
            }

            var clip = Trim(raw, typeSpec.LoopEndSec);
            _trimmed[type] = clip;
            return clip;
        }

        /// <summary>
        /// Copies the first <paramref name="seconds"/> of a clip into a new one. The tail past that
        /// is decoder overlap, not engine.
        /// </summary>
        public static AudioClip Trim(AudioClip raw, float seconds)
        {
            if (raw == null) return null;

            int wanted = Mathf.RoundToInt(seconds * raw.frequency);
            if (seconds <= 0f || wanted >= raw.samples) return raw; // nothing to cut

            var data = new float[raw.samples * raw.channels];
            if (!raw.GetData(data, 0))
            {
                Debug.LogWarning(
                    $"EngineSound: could not read '{raw.name}' — it must import as PCM with " +
                    "Decompress On Load (see GameAudioImporter). The loop keeps its seam.");
                return raw;
            }

            var cut = new float[wanted * raw.channels];
            System.Array.Copy(data, cut, cut.Length);

            var trimmed = AudioClip.Create(
                raw.name + "-loop", wanted, raw.channels, raw.frequency, false);
            trimmed.SetData(cut, 0);
            return trimmed;
        }
    }
}
