using System.Collections.Generic;
using UnityEngine;

namespace TheBlock.Game
{
    /// <summary>
    /// One-time contextual hints - the port of <c>game/onboarding.ts</c>. "Nudge once, never nag":
    /// each key fires the first time its situation comes up and is then recorded forever.
    ///
    /// Deliberately NOT cleared by <see cref="Reset"/> from a New Game, because a hint teaches the
    /// PLAYER something, not the save file - someone starting their third run does not need to be
    /// told what M does again. <see cref="Reset"/> exists for the editor menu, which is the only
    /// place that wants a genuinely first-time profile back.
    /// </summary>
    public static class Onboarding
    {
        private const string Key = "theblock.hints";
        private const char Separator = ',';

        private static HashSet<string> _seen;

        private static HashSet<string> Seen
        {
            get
            {
                if (_seen != null) return _seen;
                _seen = new HashSet<string>();
                var raw = PlayerPrefs.GetString(Key, string.Empty);
                if (string.IsNullOrEmpty(raw)) return _seen;
                foreach (var id in raw.Split(Separator))
                    if (!string.IsNullOrEmpty(id))
                        _seen.Add(id);
                return _seen;
            }
        }

        /// <summary>True the FIRST time this key is asked for, and false on every later ask.</summary>
        public static bool FirstTime(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (!Seen.Add(key)) return false;
            PlayerPrefs.SetString(Key, string.Join(Separator.ToString(), Seen));
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>Forgets every hint. The editor's Reset Campaign, not New Game.</summary>
        public static void Reset()
        {
            _seen = null;
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// The in-memory cache is static and Play mode does not clear statics by default - see the
        /// <c>recompile-during-play-nulls-fields</c> memory for the general trap. Dropping it on
        /// entering Play means an editor Reset between runs is actually seen.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => _seen = null;
    }
}
