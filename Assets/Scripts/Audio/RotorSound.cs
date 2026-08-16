using UnityEngine;
using UnityEngine.Audio;

namespace TheBlock.Audio
{
    /// <summary>
    /// The helicopter's rotor - the port of <c>src/audio/rotor-sound.ts</c>, and the one place in
    /// this project where Unity has a genuine one-for-one answer to the Web Audio API.
    ///
    /// The "whomp-whomp" blade slap is a low band of noise whose amplitude is chopped by an LFO at
    /// the blade-pass rate; a sawtooth engine hum and a turbine whine sit underneath. Volume, chop
    /// rate and both pitches scale with the rotor throttle, so the sound tracks the blades spinning
    /// up and down rather than fading a fixed recording in.
    ///
    /// <b><see cref="OnAudioFilterRead"/> is why this is a port and not an approximation.</b> The
    /// obvious Unity route - bake one loop and drive <c>AudioSource.pitch</c> - cannot reproduce it,
    /// because the three rates move by DIFFERENT factors as the throttle opens: the chop goes 7→17
    /// Hz (2.43×), the engine hum 0.7→1.2 (1.71×), the whine 0.6→1.3 (2.17×). One pitch knob
    /// collapses all three into one ratio and the spin-up stops sounding like a rotor. Unity's DSP
    /// callback runs on the audio thread at buffer rate, which is exactly what Web Audio's graph
    /// was, so the arithmetic below is the same arithmetic.
    ///
    /// <b>Rules of the audio thread, and they are not optional:</b> no allocation, no Unity API, no
    /// logging. Every buffer is filled from fields that were already there. The main thread writes
    /// only two floats, and a torn read of either is one buffer of very slightly wrong pitch -
    /// inaudible, and cheaper than a lock on a callback that runs hundreds of times a second.
    ///
    /// <b>Naive saw and triangle, deliberately</b>, unlike <see cref="SfxSynth"/>: the hum is 70 Hz
    /// into a 420 Hz lowpass and the whine is a 230 Hz triangle, so the aliased partials are 1/n and
    /// 1/n² terms around n ≈ 600 - sixty decibels under the blade noise they sit beneath. PolyBLEP
    /// here would be arithmetic nobody can hear.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class RotorSound : MonoBehaviour
    {
        // The web's CFG block, unchanged.
        private const float MaxVolume = 0.32f;
        private const float ChopHzMin = 7f;
        private const float ChopHzMax = 17f;
        private const float EngineHz = 70f;
        private const float WhineHz = 230f;
        private const float BandHz = 180f;
        private const float BandQ = 0.8f;
        private const float EngineCutoff = 420f;
        private const float EngineGain = 0.45f;
        private const float WhineGain = 0.05f;

        private const float SmoothTau = 0.12f; // per-frame tracking
        private const float SpinUpTau = 1.6f;
        private const float SpinDownTau = 1.2f;

        [SerializeField] private AudioMixerGroup output;

        private AudioSource _source;

        /// <summary>Written by the main thread, read by the audio thread. See the class note.</summary>
        private volatile float _target;

        private volatile float _tau = SmoothTau;

        // Audio-thread state. Never touched from anywhere else.
        private float _throttle;
        private double _chopPhase, _enginePhase, _whinePhase;
        private uint _noiseSeed = 0x9E3779B9;
        private Biquad _band;
        private Biquad _engineLow;
        private int _rate = 44100;
        private bool _built;

        /// <summary>Current smoothed throttle. Diagnostic - the measurement pass reads it.</summary>
        public float Throttle => _throttle;

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = true;
            _source.spatialBlend = 0f; // you are inside it; there is nothing to localise
            _source.outputAudioMixerGroup = output;

            // A filter only runs while its source is producing audio, so the source needs SOMETHING
            // to play. A second of silence, looped, is the whole trick - every sample of it is
            // overwritten in OnAudioFilterRead before anyone hears it.
            _rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
            var silence = AudioClip.Create("rotor-carrier", _rate, 1, _rate, false);
            silence.SetData(new float[_rate], 0);
            _source.clip = silence;

            _band = new Biquad(_rate, BandHz, BandQ, bandpass: true);
            _engineLow = new Biquad(_rate, EngineCutoff, 0.707f, bandpass: false);
            _built = true;
        }

        /// <summary>Editor-side wiring, used by <c>The Block → Build Audio</c>.</summary>
        public void SetOutput(AudioMixerGroup group) => output = group;

        /// <summary>Spins the sound up. Called as the pilot sits down.</summary>
        public void StartRotor()
        {
            _tau = SpinUpTau;
            _target = 1f;
            if (_source != null && !_source.isPlaying) _source.Play();
        }

        /// <summary>Spins it down to silence and stops the carrier once it is inaudible.</summary>
        public void StopRotor()
        {
            _tau = SpinDownTau;
            _target = 0f;
        }

        /// <summary>Tracks the live rotor throttle each frame while flying.</summary>
        public void SetThrottle(float t)
        {
            _tau = SmoothTau;
            _target = Mathf.Clamp01(t);
        }

        private void Update()
        {
            // Stopping the carrier is a main-thread job; the audio thread must not call into Unity.
            if (_source != null && _source.isPlaying && _target <= 0f && _throttle < 0.001f)
                _source.Stop();
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_built) return;

            float target = _target;
            float k = 1f - Mathf.Exp(-1f / Mathf.Max(0.0001f, _tau * _rate));

            for (int i = 0; i < data.Length; i += channels)
            {
                _throttle += (target - _throttle) * k;
                float t = _throttle;

                // --- blade slap: white noise → bandpass → LFO-chopped gain ---
                float white = NextFloat(ref _noiseSeed) * 2f - 1f;
                float band = _band.Process(white);
                float chop = 0.5f + 0.5f * Mathf.Sin((float)(_chopPhase * 2.0 * Mathf.PI));
                float blade = band * chop;

                // --- engine hum: low sawtooth, lowpassed ---
                float saw = 2f * (float)_enginePhase - 1f;
                float hum = _engineLow.Process(saw) * EngineGain;

                // --- turbine whine: faint high triangle ---
                float whine = (2f * Mathf.Abs(2f * (float)_whinePhase - 1f) - 1f) * WhineGain;

                float sample = (blade + hum + whine) * Mathf.Max(0.0001f, t * MaxVolume);
                sample = Mathf.Clamp(sample, -1f, 1f);

                for (int c = 0; c < channels; c++) data[i + c] = sample;

                _chopPhase = Wrap(_chopPhase + (ChopHzMin + t * (ChopHzMax - ChopHzMin)) / _rate);
                _enginePhase = Wrap(_enginePhase + EngineHz * (0.7f + 0.5f * t) / _rate);
                _whinePhase = Wrap(_whinePhase + WhineHz * (0.6f + 0.7f * t) / _rate);
            }
        }

        private static double Wrap(double phase) => phase >= 1.0 ? phase - 1.0 : phase;

        /// <summary>Mulberry32, inlined so the audio thread allocates nothing.</summary>
        private static float NextFloat(ref uint state)
        {
            state += 0x6D2B79F5u;
            uint z = state;
            z = (z ^ (z >> 15)) * (z | 1u);
            z ^= z + (z ^ (z >> 7)) * (z | 61u);
            return ((z ^ (z >> 14)) & 0x00FFFFFFu) / (float)0x01000000;
        }

        /// <summary>RBJ bandpass (constant 0 dB peak) or lowpass, whichever the caller asked for.</summary>
        private struct Biquad
        {
            private readonly float _b0, _b1, _b2, _a1, _a2;
            private float _x1, _x2, _y1, _y2;

            public Biquad(int rate, float frequency, float q, bool bandpass)
            {
                float w0 = 2f * Mathf.PI * Mathf.Clamp(frequency, 20f, rate * 0.45f) / rate;
                float cos = Mathf.Cos(w0);
                float alpha = Mathf.Sin(w0) / (2f * Mathf.Max(0.05f, q));
                float a0 = 1f + alpha;

                if (bandpass)
                {
                    _b0 = alpha / a0;
                    _b1 = 0f;
                    _b2 = -alpha / a0;
                }
                else
                {
                    _b0 = (1f - cos) / 2f / a0;
                    _b1 = (1f - cos) / a0;
                    _b2 = _b0;
                }

                _a1 = -2f * cos / a0;
                _a2 = (1f - alpha) / a0;
                _x1 = _x2 = _y1 = _y2 = 0f;
            }

            public float Process(float x)
            {
                float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1;
                _x1 = x;
                _y2 = _y1;
                _y1 = y;
                return y;
            }
        }
    }
}
