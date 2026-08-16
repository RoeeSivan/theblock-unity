using UnityEngine;

namespace TheBlock.Core
{
    /// <summary>
    /// The freeze. One flag the whole game reads, set by the pause menu and by the title screen.
    ///
    /// <b>This exists because <c>Time.timeScale = 0</c> does not stop <c>Update</c>.</b> The web
    /// build pauses by skipping its own <c>stepSim</c> call - one branch in one frame loop, and
    /// nothing downstream of it runs. Unity has no such choke point: <c>timeScale</c> freezes
    /// physics, animators, NavMesh agents and every <c>Time.deltaTime</c> timer, but seventeen
    /// scripts in this project poll <c>Keyboard.current</c> in <c>Update</c> and every one of them
    /// keeps firing behind an open menu. <c>E</c> would get into a car, <c>M</c> would open the map
    /// under the pause overlay, <c>F</c> would retry a mission, <c>R</c> would respawn the vehicle
    /// out from under the player. So the scale change is only half of a pause; <see cref="Frozen"/>
    /// is the other half, and it is checked at the top of every one of those seventeen.
    ///
    /// <b>Audio pauses through the listener, not through the mixer.</b> A fifth
    /// <c>AudioMixerSnapshot</c> would mean re-running <c>AudioMixerBuilder</c>'s reflection-built
    /// mixer over an asset U27 shipped and nobody has balanced yet. <c>AudioListener.pause</c> is
    /// one line and Unity already provides the exception the menu needs -
    /// <c>AudioSource.ignoreListenerPause</c>, set on the sfx pool so the UI click still sounds.
    /// </summary>
    public static class Pause
    {
        /// <summary>True while a menu owns the screen. Gameplay input reads this and stands down.</summary>
        public static bool Frozen { get; private set; }

        /// <summary>Raised on every change, so a system with state to shed can shed it once.</summary>
        public static event System.Action<bool> Changed;

        /// <summary>The scale to go back to. Not assumed to be 1 - a synthetic Play test runs slow.</summary>
        private static float _resumeScale = 1f;

        public static void Set(bool frozen)
        {
            if (Frozen == frozen) return;

            if (frozen)
            {
                // Read it rather than assume it: PORT-STATUS's synthetic measurements run the editor
                // at timeScale 0.02, and a pause taken during one must not silently speed it up.
                _resumeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
            }
            else
            {
                Time.timeScale = _resumeScale;
            }

            AudioListener.pause = frozen;
            Frozen = frozen;
            Changed?.Invoke(frozen);
        }

        /// <summary>
        /// Entering Play with the statics left over from the last session - or from a domain reload -
        /// must never start the game already frozen. Clears the flag AND the scale, because the two
        /// live in different places and only one of them is a static field.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetOnPlay()
        {
            Frozen = false;
            _resumeScale = 1f;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            Changed = null;
        }
    }
}
