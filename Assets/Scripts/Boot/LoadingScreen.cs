using TheBlock.UI.Menus;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheBlock.Boot
{
    /// <summary>
    /// The boot bar — the port of <c>src/ui/loading-screen.ts</c> and <c>styles/loading.css</c>.
    ///
    /// <b>The bar finally measures something.</b> That file's long comment is an apology: three.js's
    /// <c>DefaultLoadingManager</c> reports <c>itemsLoaded / itemsTotal</c>, the web build loads
    /// sequentially, so the first file to finish reports 1/1 and the bar sits pinned at 100% for the
    /// entire remaining download — "the worst possible lie to tell someone waiting". Its answer is a
    /// hand-counted list of milestones that must be kept in sync with <c>main.ts</c> by hand. Unity
    /// hands us <c>AsyncOperation.progress</c> over the whole scene, which is the number that file
    /// wanted and could not have.
    ///
    /// <b>There is no Play button.</b> <c>waitForPlay()</c> exists to harvest a user gesture,
    /// because a browser will not start an <c>AudioContext</c> without one — its own comment says
    /// so at the click handler ("first user gesture — also primes the shared AudioContext"). Unity
    /// has no autoplay policy, so the button is scar tissue and the bar simply finishes.
    ///
    /// <see cref="Fail"/> survives, unchanged in spirit: a boot that dies has to read as an error
    /// rather than hang at "Loading…".
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class LoadingScreen : MonoBehaviour
    {
        private VisualElement _fill;
        private Label _status;
        private bool _failed;

        private void Awake() => Build();

        private void Build()
        {
            if (_fill != null) return;

            var root = GetComponent<UIDocument>()?.rootVisualElement;
            if (root == null) return;

            var screen = MenuStyle.Overlay("loading-screen", opaque: true);
            screen.style.display = DisplayStyle.Flex;
            root.Add(screen);

            screen.Add(MenuStyle.Wordmark("The Block", 44f));

            // #loading-bar: min(420px, 70vw) x 10, r5, over rgba(255,255,255,0.12).
            var track = new VisualElement { name = "loading-bar" };
            track.style.width = 420f;
            track.style.maxWidth = Length.Percent(70f);
            track.style.height = 10f;
            track.style.marginBottom = 24f;
            track.style.backgroundColor = MenuStyle.Ui(1f, 1f, 1f, 0.12f);
            track.style.overflow = Overflow.Hidden;
            MenuStyle.SetBorder(track, Color.clear, 0f, 5f);
            screen.Add(track);

            _fill = new VisualElement { name = "loading-bar-fill" };
            _fill.style.width = Length.Percent(0f);
            _fill.style.height = Length.Percent(100f);
            _fill.style.backgroundColor = MenuStyle.Accent;
            MenuStyle.SetBorder(_fill, Color.clear, 0f, 5f);
            track.Add(_fill);

            _status = MenuStyle.Body("Loading…", 14f);
            screen.Add(_status);
        }

        /// <summary>Paints the bar. <paramref name="fraction"/> is 0–1; the label sits under it.</summary>
        public void SetProgress(float fraction, string label)
        {
            if (_failed) return; // a dead boot must not keep animating as if it were fine
            Build();

            var percent = Mathf.RoundToInt(Mathf.Clamp01(fraction) * 100f);
            if (_fill != null) _fill.style.width = Length.Percent(percent);
            if (_status != null) _status.text = $"{percent}% · {label}";
        }

        /// <summary>
        /// Boot died. The bar goes red and full and the reason replaces the percentage — the web's
        /// <c>#loading-screen.failed</c>, and the first error wins so a cascade cannot overwrite the
        /// cause.
        /// </summary>
        public void Fail(string message)
        {
            if (_failed) return;
            _failed = true;
            Build();

            if (_fill != null)
            {
                _fill.style.width = Length.Percent(100f);
                _fill.style.backgroundColor = MenuStyle.Ui(0.84f, 0.27f, 0.27f);
            }

            if (_status != null)
            {
                _status.text = message;
                _status.style.color = MenuStyle.Ui(1f, 0.71f, 0.71f);
            }

            Debug.LogError($"LoadingScreen: {message}");
        }
    }
}
