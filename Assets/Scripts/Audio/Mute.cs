using TheBlock.Game;
using UnityEngine;

namespace TheBlock.Audio
{
    /// <summary>
    /// The mute. One flag, one owner — nothing else in the project may write
    /// <c>AudioListener.volume</c>.
    ///
    /// <b>It mutes at the listener, not at the mixer</b>, for the reason <see cref="TheBlock.Core.Pause"/>
    /// already states about <c>AudioListener.pause</c>: the mixer U27 shipped is built by reflection
    /// and nobody has balanced it yet, and <c>SetFloat</c> on <c>volMaster</c> would seize that
    /// parameter away from the snapshot system permanently (until a matching <c>ClearFloat</c>),
    /// which is a second owner of the mix. The listener is one line, is global, and catches the
    /// sources that never route through a group at all.
    ///
    /// <b>It is separate from the pause.</b> <c>AudioListener.pause</c> is a state the pause menu
    /// takes and gives back; this is a preference that outlives the session, and the two compose —
    /// muted-and-paused unpauses back to muted. This is also why the hotkey deliberately ignores
    /// <c>Pause.Frozen</c>: silencing the game from inside the pause menu is the one place a player
    /// is most likely to want it.
    /// </summary>
    public static class Mute
    {
        private static bool _applied;

        /// <summary>
        /// Is sound audible? Backed by <see cref="Progress.SoundOn"/>, so it survives a quit.
        /// Setting it writes the listener immediately.
        /// </summary>
        public static bool SoundOn
        {
            get => Progress.SoundOn;
            set
            {
                Progress.SoundOn = value;
                Apply();
            }
        }

        /// <summary>Flips it. What the settings row and the hotkey both call.</summary>
        public static void Toggle() => SoundOn = !SoundOn;

        /// <summary>Pushes the stored preference at the listener. Idempotent, cheap, safe any time.</summary>
        public static void Apply()
        {
            AudioListener.volume = Progress.SoundOn ? 1f : 0f;
            _applied = true;
        }

        /// <summary>
        /// Boot. <c>RuntimeInitializeOnLoadMethod</c> runs once per SESSION, not once per scene load
        /// — but <c>AudioListener.volume</c> is not a static of ours, it is engine state that Play
        /// mode leaves exactly as the last session set it, so a single apply at load is both
        /// necessary and enough. <see cref="_applied"/> exists only so a domain reload mid-Play can
        /// be told apart from a cold boot by anything that asks.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyOnPlay() => Apply();

        /// <summary>True once the preference has reached the listener this session.</summary>
        public static bool Applied => _applied;
    }
}
