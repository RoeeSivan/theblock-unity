using System.Collections;
using TheBlock.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheBlock.Boot
{
    /// <summary>
    /// The first scene. It clears the session's statics, loads the world asynchronously, and drives
    /// the bar off the load's own progress.
    ///
    /// It holds nothing else - no audio host, no campaign, no Joe. The title screen deliberately
    /// lives in the world scene (see <c>GameFlow</c>), so this scene's whole job is the two seconds
    /// before that one exists.
    ///
    /// <b><c>allowSceneActivation</c> is held off on purpose.</b> Unity stops a held load at
    /// <c>progress == 0.9</c> and waits, which is exactly the shape the web build's bar wanted: it
    /// caps itself at 99% and lets only the final step show a full bar, because a bar that reaches
    /// 100 and then keeps you waiting is the specific lie it was written to avoid. So 0 → 0.9 maps
    /// to 0 → 99%, then the bar is filled, and only then does the scene swap in.
    /// </summary>
    public class BootLoader : MonoBehaviour
    {
        [SerializeField] private LoadingScreen screen;

        [Tooltip("The scene to load. Must be in File → Build Settings - Build Menus puts it there.")]
        [SerializeField] private string worldScene = "World";

        [Tooltip("Seconds the finished bar is left on screen before the world appears. Without it " +
                 "a fast load never draws 100% at all, and the bar reads as having stopped short.")]
        [SerializeField] private float holdAtFull = 0.25f;

        private void Awake()
        {
            if (screen == null) screen = FindAnyObjectByType<LoadingScreen>();

            // Before the load, not after: a Quit to Title comes back through here with the previous
            // world's registries still populated, and MapRegistry in particular accumulates.
            SessionReset.Run();
        }

        /// <summary>
        /// One line per boot milestone, wall-clock since process start.
        ///
        /// <b>Why:</b> U30b round 1 measured a 2,154 ms hitch at t = 2.2 s that did not move between
        /// runs and fired while texture memory was still 6 MB - so it was neither the texture load
        /// nor anything the memory census could see. The two candidates were the async load's own
        /// work and the frame that activates the world (every <c>Awake</c> in the scene, in one
        /// frame), and nothing could tell them apart. Four timestamps could.
        ///
        /// <b>The answer, round 2, and it was the other one:</b>
        ///
        /// <code>
        ///   0 → 1.90 s   engine startup - Metal, Mono, PhysX, subsystems. Before any script runs.
        ///   2.17 s       LoadSceneAsync called, returns in 3 ms
        ///   1.90 → 4.06 s  THE HITCH. One blocked frame, and it is the load itself.
        ///   4.06 → 4.32 s  holdAtFull, 0.25 s, deliberate
        ///   4.32 → 4.57 s  activation + EVERY Awake in the world = 245 ms
        ///   → ~6.0 s     second hitch, 1.89 s: the texture upload (6 → 275 MB)
        ///   → ~6.3 s     third hitch, 332 ms: the first frame with real geometry
        /// </code>
        ///
        /// So the world's <c>Awake</c>s were never the problem - they are 4% of the boot - and the
        /// hitch is Unity deserializing one very large scene on the main thread.
        /// <c>Application.backgroundLoadingPriority = Low</c> was tried against it and moved nothing
        /// (2168/4064 against 2159/4046, inside run-to-run noise): it paces INTEGRATION, which is the
        /// 245 ms that was already cheap, not the deserialization that is not. It was reverted rather
        /// than left in as a no-op that would read like a fix. Splitting the world into additive
        /// scenes is the lever that would actually move it, and behind a loading screen this is not
        /// worth that.
        ///
        /// Development-only: this is diagnostic noise in a release <c>Player.log</c>.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private static void Mark(string what) =>
            Debug.Log($"BootLoader: {Time.realtimeSinceStartup * 1000f:0} ms - {what}");

        private IEnumerator Start()
        {
            screen?.SetProgress(0f, "Loading the city");

            Mark("calling LoadSceneAsync");
            var load = SceneManager.LoadSceneAsync(worldScene, LoadSceneMode.Single);
            Mark("LoadSceneAsync returned");
            if (load == null)
            {
                // The one failure this scene can actually have, and it is a setup mistake rather
                // than a runtime one: the world is not in Build Settings. Say that, rather than
                // sitting at 0% forever.
                screen?.Fail($"Could not load '{worldScene}'. Run The Block → Build Menus to add it " +
                             "to File → Build Settings.");
                yield break;
            }

            load.allowSceneActivation = false;

            // 0.9 is where Unity parks a held load, so it is the whole range there is to show.
            while (load.progress < 0.9f)
            {
                screen?.SetProgress(Mathf.Clamp01(load.progress / 0.9f) * 0.99f, "Loading the city");
                yield return null;
            }

            Mark("load parked at 0.9");
            screen?.SetProgress(1f, "Ready");

            // Unscaled, and long enough to paint: this scene runs before anything sets timeScale,
            // but the world it is about to hand over to opens frozen on the title screen.
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, holdAtFull));

            // Nothing may follow this line in the coroutine. A Single-mode activation unloads THIS
            // scene, which destroys this object and kills the coroutine with it - so the far side of
            // the activation has to be watched by something that outlives the scene. That is what
            // the static hook below is for.
            Mark("allowSceneActivation = true");
            WatchActivation();
            load.allowSceneActivation = true;
        }

        /// <summary>
        /// Times the activation frame from outside the scene that dies in it.
        ///
        /// <c>sceneLoaded</c> fires after every <c>Awake</c> in the world and before the first
        /// <c>Start</c>, so the gap between the mark above and this one IS the integration plus
        /// Awake cost - the half of the 2,154 ms that a coroutine in the dying scene can never see.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private static void WatchActivation()
        {
            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> handler = null;
            handler = (scene, mode) =>
            {
                Mark("scene '" + scene.name + "' activated - every Awake has run");
                SceneManager.sceneLoaded -= handler;
            };
            SceneManager.sceneLoaded += handler;
        }
    }
}
