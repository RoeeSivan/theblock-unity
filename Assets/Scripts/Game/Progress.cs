using UnityEngine;

namespace TheBlock.Game
{
    /// <summary>
    /// Campaign unlock progress that survives a quit — the port of <c>game/progress.ts</c>.
    ///
    /// <see cref="Campaign"/> only tracks a cursor for the current session; this is the one number
    /// that outlives it. It is MONOTONIC on purpose: completing a mission raises it, and nothing
    /// ever lowers it, so a Mission Select jump (U26) cannot cost a returning player a mission they
    /// already earned.
    ///
    /// Static, because it is asked for before any scene object is guaranteed to exist and it holds
    /// no state of its own — <c>PlayerPrefs</c> IS the state. That is Unity's <c>localStorage</c>:
    /// same keys as the web build, same write-on-every-change, and the same refusal to let a storage
    /// failure break play. <c>PlayerPrefs</c> never throws where <c>localStorage</c> can, so the
    /// web's try/catch has nothing to guard here — the tolerance is in the DEFAULTS instead.
    /// </summary>
    public static class Progress
    {
        private const string UnlockedKey = "theblock.unlocked";
        private const string CharacterKey = "theblock.character";

        /// <summary>
        /// Furthest mission index unlocked. 0 = only the first, which is also what a fresh profile
        /// reads, so "never played" and "played the first mission" are the same state — exactly as
        /// in the web build.
        /// </summary>
        public static int UnlockedIndex => Mathf.Max(0, PlayerPrefs.GetInt(UnlockedKey, 0));

        /// <summary>Records the furthest mission reached. Never lowers the stored value.</summary>
        public static void RecordReached(int index)
        {
            if (index <= UnlockedIndex) return;
            PlayerPrefs.SetInt(UnlockedKey, index);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Which body the player wears. Deliberately NOT touched by <see cref="Reset"/>: New Game
        /// restarts the campaign, not who you are. Empty means "never picked" and the caller
        /// resolves it to the roster default — the roster is U29's, so today it is always empty.
        /// </summary>
        public static string CharacterId
        {
            get => PlayerPrefs.GetString(CharacterKey, string.Empty);
            set
            {
                PlayerPrefs.SetString(CharacterKey, value ?? string.Empty);
                PlayerPrefs.Save();
            }
        }

        /// <summary>New Game: back to only the first mission unlocked.</summary>
        public static void Reset()
        {
            PlayerPrefs.SetInt(UnlockedKey, 0);
            PlayerPrefs.Save();
        }
    }
}
