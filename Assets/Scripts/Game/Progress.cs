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
        private const string RadarKey = "theblock.radar";
        private const string DayNightKey = "theblock.daynight";
        private const string SoundKey = "theblock.sound";

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

        /// <summary>
        /// Settings → Display → Radar: is the corner minimap on? Default true, which is the state
        /// the user restored on 2026-08-16 after the same day's removal.
        ///
        /// A PREFERENCE, not progress, so like <see cref="CharacterId"/> it survives
        /// <see cref="Reset"/> — a New Game is a new campaign, not a new profile. It is also not the
        /// dance's <c>GameMap.Suppressed</c>: that is a temporary override a scene takes and gives
        /// back, and writing this from there would hand the radar back ON to someone who turned it
        /// off.
        /// </summary>
        public static bool RadarOn
        {
            get => PlayerPrefs.GetInt(RadarKey, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(RadarKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Settings → Display → Time of Day: does the sun move? <b>Default FALSE, and that default is
        /// load-bearing.</b> A moving sun is an addition to this port, not a port of anything — the
        /// web build's sky is one constant colour — so off means the world looks exactly as it did
        /// for every play-test from U11 to U27, and it costs exactly what it did too: with this false
        /// the grading pass is not scheduled at all.
        ///
        /// A PREFERENCE, like <see cref="RadarOn"/>, so it survives <see cref="Reset"/>.
        /// </summary>
        public static bool DayNightOn
        {
            get => PlayerPrefs.GetInt(DayNightKey, 0) != 0;
            set
            {
                PlayerPrefs.SetInt(DayNightKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Settings → Audio → Sound: is anything audible at all? Default true. Read and written
        /// through <c>Mute</c>, which is the only thing allowed to touch
        /// <c>AudioListener.volume</c> — this property is the storage, not the mechanism.
        ///
        /// A PREFERENCE, like <see cref="RadarOn"/>, so it survives <see cref="Reset"/>: a New Game
        /// must not start shouting at someone who muted the game.
        /// </summary>
        public static bool SoundOn
        {
            get => PlayerPrefs.GetInt(SoundKey, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(SoundKey, value ? 1 : 0);
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
