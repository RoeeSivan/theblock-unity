using System.Collections.Generic;
using TheBlock.Core;
using UnityEngine;

namespace TheBlock.Minigame.Rhythm
{
    /// <summary>
    /// Builds the note chart from a BPM and a difficulty ramp — the port of
    /// <c>src/minigame/rhythm/beatmap.ts</c>.
    ///
    /// <b>There is no authored chart in either build, and that is a design decision, not a shortcut.</b>
    /// The ramp is density only: notes get closer together (half-time → 1.5 beats → a brief one-per-beat
    /// climax) while their travel speed never changes, so the track reads the same the whole way
    /// through and only the workload rises. The web build's config comment records that eighth-note
    /// bursts were tried and cut for spiking the difficulty too hard; the phase table it settled on
    /// is what the exporter now carries.
    /// </summary>
    public static class Beatmap
    {
        private static readonly Direction[] Dirs =
        {
            Direction.Left, Direction.Right, Direction.Up, Direction.Down,
        };

        /// <summary>
        /// Generates the run's notes, in time order.
        ///
        /// <paramref name="rng"/> is passed in rather than using <see cref="Random"/> so a chart can
        /// be regenerated identically — which is the only way to compare two runs of a test.
        /// </summary>
        public static List<Note> Generate(
            TheBlockConfig.RhythmSongSpec song, TheBlockConfig.RhythmBeatmapSpec map, System.Random rng)
        {
            var notes = new List<Note>();
            if (song == null || map == null || song.Bpm <= 0f) return notes;

            var beat = 60f / song.Bpm;
            var previous = -1;
            var t = map.StartSec;

            // A guard, not a limit: `beatsPerNote` comes out of a config file, and a zero there would
            // spin here forever rather than fail. 8,192 notes is ~70 minutes at the densest phase.
            const int cap = 8192;

            while (t < map.EndSec && notes.Count < cap)
            {
                var phase = PhaseAt(map.Phases, t);
                var step = Mathf.Max(0.05f, phase.BeatsPerNote) * beat;

                previous = NextDir(previous, rng);
                notes.Add(new Note(t, Dirs[previous]));

                // An extra eighth between beats, for short bursts. Unset everywhere in the shipped
                // config — carried because the field is real and someone may want it back.
                if (phase.DoubleChance > 0f && rng.NextDouble() < phase.DoubleChance)
                {
                    previous = NextDir(previous, rng);
                    notes.Add(new Note(t + step * 0.5f, Dirs[previous]));
                }

                t += step;
            }

            notes.Sort((a, b) => a.Time.CompareTo(b.Time));
            return notes;
        }

        /// <summary>The last phase whose <c>fromSec</c> has been reached.</summary>
        private static TheBlockConfig.RhythmPhaseSpec PhaseAt(
            List<TheBlockConfig.RhythmPhaseSpec> phases, float t)
        {
            if (phases == null || phases.Count == 0)
                return new TheBlockConfig.RhythmPhaseSpec { BeatsPerNote = 2f };

            var active = phases[0];
            foreach (var phase in phases)
            {
                if (t < phase.FromSec) break;
                active = phase;
            }

            return active;
        }

        /// <summary>A random arrow that is never the one before it — the web's own variety rule.</summary>
        private static int NextDir(int previous, System.Random rng)
        {
            if (previous < 0) return rng.Next(Dirs.Length);

            // Pick from the other three directly instead of rejection-sampling: same distribution,
            // and it cannot loop.
            var offset = 1 + rng.Next(Dirs.Length - 1);
            return (previous + offset) % Dirs.Length;
        }
    }
}
