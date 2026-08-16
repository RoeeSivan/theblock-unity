using System.Collections.Generic;
using UnityEngine;

namespace TheBlock.Audio
{
    /// <summary>
    /// The zero-asset sound effects of <c>src/audio/sfx.ts</c>, rendered to PCM.
    ///
    /// <b>This is U27's answer to the standing "can Unity do this better?" question, and the answer
    /// is bake it once.</b> The web builds a fresh <c>OscillatorNode</c> + <c>GainNode</c> graph on
    /// EVERY key press — three nodes per note, torn down 300 ms later — because Web Audio offers no
    /// way to keep the result. It has to: there is no file, and no place to put one. Unity has
    /// <see cref="AudioClip.Create"/>, so each cue is rendered to samples the first time it is asked
    /// for and is a plain clip from then on. Same envelope arithmetic, same waveforms, one
    /// allocation ever instead of one per press.
    ///
    /// <b>The oscillators are band-limited, and that is not a flourish.</b> Web Audio's
    /// <c>OscillatorNode</c> is band-limited by specification, so a 1046 Hz <c>square</c> there has
    /// no harmonics above Nyquist. Generating one naively here would alias every one of them back
    /// down as inharmonic grit, and the cue would be recognisably harsher than the shipped game's —
    /// a difference nobody would trace back to a missing PolyBLEP. So saw and square carry one.
    ///
    /// <b>The noise is seeded.</b> The web calls <c>Math.random()</c> per sample, so its crash thud
    /// is a different burst every time. A cue baked once is a fixed sound forever either way, so the
    /// only question is whether it is the SAME fixed sound on every machine and every rebuild — and
    /// a reproducible bake is what lets a test assert a peak level at all.
    /// </summary>
    public static class SfxSynth
    {
        public enum Wave
        {
            Sine,
            Triangle,
            Sawtooth,
            Square,
        }

        /// <summary>
        /// One enveloped tone inside a cue. <see cref="Start"/> is an offset from the cue's own zero,
        /// which is how the web sequences notes without a scheduler.
        /// </summary>
        public readonly struct Tone
        {
            public readonly float Frequency;
            public readonly float Start;
            public readonly float Duration;
            public readonly Wave Shape;
            public readonly float Peak;

            public Tone(float frequency, float start, float duration, Wave shape = Wave.Triangle,
                float peak = 0.25f)
            {
                Frequency = frequency;
                Start = start;
                Duration = duration;
                Shape = shape;
                Peak = peak;
            }
        }

        /// <summary>
        /// A short lowpassed white-noise burst — the BODY of an impact. A pure tone alone reads as a
        /// beep rather than a thunk, which is the web's own note and the reason this exists.
        /// </summary>
        public readonly struct Noise
        {
            public readonly float Start;
            public readonly float Duration;
            public readonly float Cutoff;
            public readonly float Peak;

            public Noise(float start, float duration, float cutoff, float peak)
            {
                Start = start;
                Duration = duration;
                Cutoff = cutoff;
                Peak = peak;
            }
        }

        /// <summary>Everything one named cue is made of.</summary>
        public class Cue
        {
            public readonly List<Tone> Tones = new();
            public readonly List<Noise> Noises = new();

            public Cue Add(float frequency, float start, float duration,
                Wave shape = Wave.Triangle, float peak = 0.25f)
            {
                Tones.Add(new Tone(frequency, start, duration, shape, peak));
                return this;
            }

            public Cue AddNoise(float start, float duration, float cutoff, float peak)
            {
                Noises.Add(new Noise(start, duration, cutoff, peak));
                return this;
            }

            /// <summary>Where the cue stops, before the release pad. The longest of its parts.</summary>
            public float Length
            {
                get
                {
                    float end = 0f;
                    foreach (var tone in Tones) end = Mathf.Max(end, tone.Start + tone.Duration);
                    foreach (var noise in Noises) end = Mathf.Max(end, noise.Start + noise.Duration);
                    return end;
                }
            }
        }

        /// <summary>The floor both of the web's exponential ramps start and end on.</summary>
        private const float Floor = 0.0001f;

        /// <summary>Attack, seconds. Fast enough to read as a clean strike, slow enough not to click.</summary>
        private const float Attack = 0.012f;

        /// <summary>The web stops each oscillator this long after its envelope lands. Kept as a pad.</summary>
        private const float Tail = 0.02f;

        /// <summary>Bakes one cue to a mono clip at the output sample rate.</summary>
        public static AudioClip Render(string name, Cue cue)
        {
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
            int samples = Mathf.Max(1, Mathf.CeilToInt((cue.Length + Tail) * rate));
            var buffer = new float[samples];

            foreach (var tone in cue.Tones) RenderTone(buffer, rate, tone);
            foreach (var noise in cue.Noises) RenderNoise(buffer, rate, noise);

            // Several notes of one cue can overlap by design (playComplete's arpeggio rings into
            // itself). Clamp rather than normalise: normalising would make a cue's loudness depend on
            // its own worst overlap, so two cues authored at the same peak would come out different.
            for (int i = 0; i < samples; i++) buffer[i] = Mathf.Clamp(buffer[i], -1f, 1f);

            var clip = AudioClip.Create(name, samples, 1, rate, false);
            clip.SetData(buffer, 0);
            return clip;
        }

        private static void RenderTone(float[] buffer, int rate, in Tone tone)
        {
            int start = Mathf.RoundToInt(tone.Start * rate);
            int count = Mathf.RoundToInt(tone.Duration * rate);
            if (count <= 0) return;

            float peak = Mathf.Max(Floor, tone.Peak);
            float attack = Mathf.Min(Attack, tone.Duration * 0.5f);
            float decay = Mathf.Max(0.0001f, tone.Duration - attack);

            double phase = 0.0;
            double step = tone.Frequency / rate;

            for (int i = 0; i < count; i++)
            {
                int at = start + i;
                if (at < 0 || at >= buffer.Length) break;

                float t = (float)i / rate;
                // Two exponential ramps, exactly as Web Audio interpolates them: geometric between
                // the two endpoint values, which is why neither end may be zero.
                float gain = t < attack
                    ? Floor * Mathf.Pow(peak / Floor, t / attack)
                    : peak * Mathf.Pow(Floor / peak, (t - attack) / decay);

                buffer[at] += Sample(tone.Shape, phase, step) * gain;
                phase += step;
                if (phase >= 1.0) phase -= 1.0;
            }
        }

        private static void RenderNoise(float[] buffer, int rate, in Noise noise)
        {
            int start = Mathf.RoundToInt(noise.Start * rate);
            int count = Mathf.RoundToInt(noise.Duration * rate);
            if (count <= 0) return;

            // A fixed seed per burst position/length, so the same cue renders identically anywhere.
            uint seed = (uint)(0x9E3779B9 ^ (start * 73856093) ^ (count * 19349663));
            var lowpass = new Biquad(rate, noise.Cutoff);
            float peak = Mathf.Max(Floor, noise.Peak);

            for (int i = 0; i < count; i++)
            {
                int at = start + i;
                if (at < 0 || at >= buffer.Length) break;

                float white = NextFloat(ref seed) * 2f - 1f;
                float t = (float)i / rate;
                float gain = peak * Mathf.Pow(Floor / peak, t / noise.Duration);
                buffer[at] += lowpass.Process(white) * gain;
            }
        }

        /// <summary>One band-limited oscillator sample. <paramref name="step"/> is cycles per sample.</summary>
        private static float Sample(Wave shape, double phase, double step)
        {
            float t = (float)phase;
            float dt = (float)step;

            switch (shape)
            {
                case Wave.Sine:
                    return Mathf.Sin(2f * Mathf.PI * t);

                case Wave.Triangle:
                    // 1/n² harmonics: the aliasing a naive triangle produces is 30 dB below a saw's
                    // and inaudible under these envelopes, so it needs no correction.
                    return 2f * Mathf.Abs(2f * t - 1f) - 1f;

                case Wave.Sawtooth:
                    return (2f * t - 1f) - PolyBlep(t, dt);

                case Wave.Square:
                    float square = t < 0.5f ? 1f : -1f;
                    return square + PolyBlep(t, dt) - PolyBlep(Frac(t + 0.5f), dt);

                default:
                    return 0f;
            }
        }

        /// <summary>
        /// The polynomial band-limited step: a two-sample correction across a waveform's
        /// discontinuity that removes most of the aliasing a naive saw or square would fold back.
        /// </summary>
        private static float PolyBlep(float t, float dt)
        {
            if (dt <= 0f) return 0f;
            if (t < dt)
            {
                t /= dt;
                return t + t - t * t - 1f;
            }

            if (t > 1f - dt)
            {
                t = (t - 1f) / dt;
                return t * t + t + t + 1f;
            }

            return 0f;
        }

        private static float Frac(float v) => v - Mathf.Floor(v);

        /// <summary>Mulberry32 — the same generator the world bake uses, so the project has one.</summary>
        private static float NextFloat(ref uint state)
        {
            state += 0x6D2B79F5u;
            uint z = state;
            z = (z ^ (z >> 15)) * (z | 1u);
            z ^= z + (z ^ (z >> 7)) * (z | 61u);
            return ((z ^ (z >> 14)) & 0x00FFFFFFu) / (float)0x01000000;
        }

        /// <summary>
        /// An RBJ lowpass, Q = 1 — the default a Web Audio <c>BiquadFilterNode</c> runs at, which is
        /// what the web's noise bursts were voiced against.
        /// </summary>
        private struct Biquad
        {
            private readonly float _b0, _b1, _b2, _a1, _a2;
            private float _x1, _x2, _y1, _y2;

            public Biquad(int rate, float cutoff)
            {
                float w0 = 2f * Mathf.PI * Mathf.Clamp(cutoff, 20f, rate * 0.45f) / rate;
                float cos = Mathf.Cos(w0);
                float alpha = Mathf.Sin(w0) / 2f; // Q = 1 → alpha = sin(w0) / (2Q)
                float a0 = 1f + alpha;

                _b0 = (1f - cos) / 2f / a0;
                _b1 = (1f - cos) / a0;
                _b2 = _b0;
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
