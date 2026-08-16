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
    /// It holds nothing else — no audio host, no campaign, no Joe. The title screen deliberately
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

        [Tooltip("The scene to load. Must be in File → Build Settings — Build Menus puts it there.")]
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

        private IEnumerator Start()
        {
            screen?.SetProgress(0f, "Loading the city");

            var load = SceneManager.LoadSceneAsync(worldScene, LoadSceneMode.Single);
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

            screen?.SetProgress(1f, "Ready");

            // Unscaled, and long enough to paint: this scene runs before anything sets timeScale,
            // but the world it is about to hand over to opens frozen on the title screen.
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, holdAtFull));

            load.allowSceneActivation = true;
        }
    }
}
