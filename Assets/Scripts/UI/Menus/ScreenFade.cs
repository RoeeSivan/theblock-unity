using UnityEngine;
using UnityEngine.UIElements;

namespace TheBlock.UI.Menus
{
    /// <summary>
    /// The black that hides a teleport — U25's row, and the port of <c>src/ui/fade.ts</c>.
    ///
    /// <b>It covers rather than brackets, and that is a deliberate difference from the web.</b>
    /// <c>fade.ts</c> exposes <c>to(black)</c> and the caller awaits a fade-OUT, teleports, then
    /// awaits a fade-in. Both halves of that are async, and here both call sites are not:
    /// <see cref="World.Interior"/>'s <c>Enter</c>/<c>Leave</c> flip <c>inside</c> and move the
    /// capsule in the same statement, and <see cref="Missions.DeliveryMission"/> reads
    /// <c>interior.Inside</c> on the very next line. Making them awaitable to buy a fade-out would
    /// reach into the mission's entry sequence for a quarter of a second of black.
    ///
    /// So the black goes up in the SAME frame as the teleport and fades off. Nothing is lost: the
    /// move is instantaneous either way, and UI Toolkit composites after the scene, so the first
    /// frame the player is shown of the destination is already covered. What the fade is actually
    /// for — never cutting hard between a street and a lit room — is unchanged.
    ///
    /// Unscaled throughout: a fade can outlive a pause taken on top of it, and a fade stuck at full
    /// black is a black screen with no way out.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ScreenFade : MonoBehaviour
    {
        [Tooltip("Seconds from full black back to clear. The web's CSS transition is 0.35 s.")]
        [SerializeField] private float seconds = 0.35f;

        private VisualElement _sheet;
        private float _alpha;

        private void Awake()
        {
            var document = GetComponent<UIDocument>();
            var root = document != null ? document.rootVisualElement : null;
            if (root == null) return;

            _sheet = new VisualElement { name = "fade" };
            MenuStyle.Stretch(_sheet);
            _sheet.style.backgroundColor = Color.black;

            // It must never eat a click on its way out, and it must never be picked at all.
            _sheet.pickingMode = PickingMode.Ignore;
            _sheet.style.display = DisplayStyle.None;

            root.Add(_sheet);
        }

        /// <summary>
        /// Snap to black, then fade off. Call it in the same frame as the thing being hidden.
        /// </summary>
        public void Cover()
        {
            if (_sheet == null) return;

            _alpha = 1f;
            _sheet.style.display = DisplayStyle.Flex;
            _sheet.BringToFront(); // above every menu — a teleport under an open panel still hides
            _sheet.style.backgroundColor = Color.black;
        }

        private void Update()
        {
            if (_sheet == null || _alpha <= 0f) return;

            _alpha -= Time.unscaledDeltaTime / Mathf.Max(0.01f, seconds);

            if (_alpha <= 0f)
            {
                _alpha = 0f;
                _sheet.style.display = DisplayStyle.None;
                return;
            }

            _sheet.style.backgroundColor = new Color(0f, 0f, 0f, _alpha);
        }
    }
}
