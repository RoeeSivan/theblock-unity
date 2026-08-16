using TheBlock.Core;
using UnityEngine;

namespace TheBlock.Minigame.Rhythm
{
    /// <summary>Which arrow a note wants. The four the web build uses, in its own letters.</summary>
    public enum Direction
    {
        Left,
        Right,
        Up,
        Down,
    }

    public enum Judgment
    {
        Perfect,
        Good,
        Miss,
    }

    /// <summary>One note: when it should be hit, in song-time, and which arrow it wants.</summary>
    public readonly struct Note
    {
        public readonly float Time;
        public readonly Direction Dir;

        public Note(float time, Direction dir)
        {
            Time = time;
            Dir = dir;
        }
    }

    /// <summary>The run's tally. Immutable - folded, never mutated, exactly as the web's is.</summary>
    public readonly struct Score
    {
        public readonly int Points;
        public readonly int Combo;
        public readonly int MaxCombo;
        public readonly int Perfect;
        public readonly int Good;
        public readonly int Miss;

        public Score(int points, int combo, int maxCombo, int perfect, int good, int miss)
        {
            Points = points;
            Combo = combo;
            MaxCombo = maxCombo;
            Perfect = perfect;
            Good = good;
            Miss = miss;
        }

        public int Judged => Perfect + Good + Miss;

        /// <summary>
        /// Weighted accuracy in 0..1 - perfect 1, good 0.5, miss 0. An empty run is 1, which is what
        /// stops a routine nobody played from reading as a failure.
        /// </summary>
        public float Accuracy => Judged == 0 ? 1f : (Perfect + Good * 0.5f) / Judged;
    }

    /// <summary>
    /// The pure half of the dance - the port of <c>src/minigame/rhythm/scoring.ts</c>.
    ///
    /// No UI, no audio, no scene. That separation is the web build's and it is worth keeping for the
    /// same reason: the timing rules are the one part of a rhythm game that has a right answer, and
    /// they should be checkable without a song playing.
    /// </summary>
    public static class RhythmScoring
    {
        /// <summary>
        /// Classifies a press against a note. <paramref name="delta"/> is press − note time; the sign
        /// is ignored, because early and late are equally wrong. Null means the press is outside the
        /// good window and does NOT belong to this note - it must not consume it.
        /// </summary>
        public static Judgment? Judge(float delta, TheBlockConfig.HitWindowsSpec windows)
        {
            var a = Mathf.Abs(delta);
            if (a <= windows.Perfect) return Judgment.Perfect;
            if (a <= windows.Good) return Judgment.Good;
            return null;
        }

        /// <summary>Folds one judgment in. Returns a new score; never mutates.</summary>
        public static Score Apply(Score s, Judgment j, TheBlockConfig.ScoreValuesSpec values)
        {
            var points = s.Points + (j switch
            {
                Judgment.Perfect => values.Perfect,
                Judgment.Good => values.Good,
                _ => values.Miss,
            });

            if (j == Judgment.Miss)
                return new Score(points, 0, s.MaxCombo, s.Perfect, s.Good, s.Miss + 1);

            var combo = s.Combo + 1;
            return new Score(
                points,
                combo,
                Mathf.Max(s.MaxCombo, combo),
                j == Judgment.Perfect ? s.Perfect + 1 : s.Perfect,
                j == Judgment.Good ? s.Good + 1 : s.Good,
                s.Miss);
        }

        /// <summary>The arrow glyphs the track draws. Unity's default font has all four.</summary>
        public static string Glyph(Direction dir) => dir switch
        {
            Direction.Left => "←",
            Direction.Right => "→",
            Direction.Up => "↑",
            _ => "↓",
        };
    }
}
