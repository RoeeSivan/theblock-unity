using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace TheBlock.UI
{
    /// <summary>
    /// The one card the whole campaign talks through — the port of <c>src/ui/briefing.ts</c>.
    ///
    /// Four different things use it and they are deliberately the same thing: the mission briefing
    /// at the cashier, the handoff after a completion ("done, +$80 → here is the next job"), the
    /// fail line with its retry key, and the win card. One surface means one dismissal rule and one
    /// place that can be on screen at a time.
    ///
    /// <b>It does not pause the game</b>, and that is the web build's call carried over: the card
    /// reads while you sit in the vehicle, and the run itself begins when you dismiss it. Space or a
    /// click closes it.
    ///
    /// The screen flash is here too, because it is the same event — a green pulse on a win, red on a
    /// fail — and putting it anywhere else means two components racing to say one thing.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class BriefingCard : MonoBehaviour
    {
        private const float FlashSeconds = 0.6f;

        private VisualElement _overlay;
        private VisualElement _card;
        private Label _hintLabel;
        private VisualElement _flash;

        private readonly List<Label> _lines = new();
        private float _flashLeft;
        private Color _flashColor;

        /// <summary>True while a card is up. Every trigger in the game checks it before firing.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>
        /// <b>Awake, and guarded, and both halves are load-bearing.</b> Built in Start this raced
        /// <see cref="Missions.CampaignRunner"/>, whose own Start shows the intro card on a fresh
        /// profile: the runner went first, <see cref="Show"/> found no overlay and built one, then
        /// this Start built a SECOND set — leaving an empty dark panel on screen that nothing held a
        /// reference to and nothing could dismiss, with the real card hidden behind it. Measured, not
        /// guessed: the panel dump had two `feedback-flash` and two `briefing` elements.
        /// </summary>
        private void Awake() => Build();

        private void Build()
        {
            if (_overlay != null) return;

            var root = GetComponent<UIDocument>().rootVisualElement;

            // The flash goes in FIRST so it sits under the card — a green pulse over the words
            // would wash out the very line it is celebrating.
            _flash = new VisualElement { name = "feedback-flash" };
            Stretch(_flash);
            _flash.pickingMode = PickingMode.Ignore;
            _flash.style.display = DisplayStyle.None;
            root.Add(_flash);

            _overlay = new VisualElement { name = "briefing" };
            Stretch(_overlay);
            _overlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;
            _overlay.style.display = DisplayStyle.None;

            // The overlay itself takes the click, so anywhere on screen dismisses — the web card
            // does the same, and hunting for a button is not what a briefing is for.
            _overlay.RegisterCallback<PointerDownEvent>(_ => Dismiss());

            _card = new VisualElement { name = "briefing-card" };
            _card.style.backgroundColor = new Color(0.06f, 0.07f, 0.09f, 0.95f);
            _card.style.paddingTop = 28f;
            _card.style.paddingBottom = 24f;
            _card.style.paddingLeft = 36f;
            _card.style.paddingRight = 36f;
            _card.style.maxWidth = 720f;
            _card.style.alignItems = Align.Center;
            SetBorder(_card, new Color(1f, 1f, 1f, 0.18f), 1f, 10f);
            _overlay.Add(_card);

            _hintLabel = new Label("Press SPACE or click to continue");
            _hintLabel.style.fontSize = 15f;
            _hintLabel.style.color = new Color(1f, 1f, 1f, 0.5f);
            _hintLabel.style.marginTop = 18f;
            _hintLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _card.Add(_hintLabel);

            root.Add(_overlay);
        }

        private static void Stretch(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.top = 0f;
            element.style.bottom = 0f;
            element.style.left = 0f;
            element.style.right = 0f;
        }

        private static void SetBorder(VisualElement element, Color color, float width, float radius)
        {
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        /// <summary>Opens the card. Fire-and-forget; use <see cref="ShowAndWait"/> to sequence.</summary>
        public void Show(IReadOnlyList<string> lines)
        {
            if (_overlay == null) Build();
            if (lines == null || lines.Count == 0) return;

            // Rebuild the body each time. A card is a handful of labels shown at most a few dozen
            // times a run, so pooling would be a cache with nothing to gain.
            foreach (var line in _lines) line.RemoveFromHierarchy();
            _lines.Clear();

            for (var i = 0; i < lines.Count; i++)
            {
                // Emoji stripped at the point of drawing — see Glyphs, and U25 for the font that
                // ends it. The card is where most of the campaign's copy is read, so a line of blank
                // boxes here would be the most visible version of the problem.
                var label = new Label(Glyphs.Strip(lines[i]));
                label.style.fontSize = i == 0 ? 26f : 19f;
                label.style.color = i == 0 ? Color.white : new Color(0.87f, 0.9f, 0.94f);
                label.style.unityFontStyleAndWeight = i == 0 ? FontStyle.Bold : FontStyle.Normal;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.style.whiteSpace = WhiteSpace.Normal;
                label.style.marginBottom = 8f;
                _card.Insert(i, label); // before the "press space" hint, which stays last
                _lines.Add(label);
            }

            _overlay.style.display = DisplayStyle.Flex;
            _overlay.BringToFront(); // nothing on this document may cover a card
            IsOpen = true;
        }

        /// <summary>
        /// Opens the card and yields until it is dismissed. This is the web's
        /// <c>await briefing.show(...)</c>, and every mission's entry sequence is written against
        /// it — briefing, then fade, then spawn.
        /// </summary>
        ///
        /// <remarks>
        /// <b>It returns a wait instruction rather than being a coroutine, and that is the fix for a
        /// real hang.</b> Written as an <c>IEnumerator</c>, <c>yield return ShowAndWait(lines)</c>
        /// does NOT call <see cref="Show"/> until the scheduler advances the inner routine on the
        /// NEXT frame — so between a mission calling Enter and its card appearing there is a
        /// one-frame window. Anything that touched the card in that window left the mission parked
        /// forever: its entry latch stayed set, its status stayed Inactive, and no key could retry
        /// it because the mission believed it was already starting. Measured on the rescue, which
        /// sat at `_entering = true` with nothing on screen.
        ///
        /// As a method returning a yield instruction, the argument is evaluated where the caller
        /// writes it, so <see cref="Show"/> runs synchronously and the window does not exist.
        /// </remarks>
        public CustomYieldInstruction ShowAndWait(IReadOnlyList<string> lines)
        {
            Show(lines);
            return new WaitWhile(() => IsOpen);
        }

        /// <summary>Closes the card. Safe to call when nothing is open.</summary>
        public void Dismiss()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (_overlay != null) _overlay.style.display = DisplayStyle.None;

            // The only button this port has yet. U26's title, pause and character-select screens are
            // where the web spends the other twenty playUiClick() calls.
            TheBlock.Audio.GameAudio.Cue(TheBlock.Audio.SfxCue.UiClick);
        }

        /// <summary>A brief full-screen pulse: green on a win, red on a fail.</summary>
        public void Flash(bool win)
        {
            if (_flash == null) return;
            _flashColor = win ? new Color(0.26f, 0.85f, 0.35f) : new Color(0.9f, 0.2f, 0.2f);
            _flashLeft = FlashSeconds;
            _flash.style.display = DisplayStyle.Flex;
        }

        private void Update()
        {
            // Space dismisses, read through the Input System like every other key in the project so
            // it respects the physical-key mapping (the web build had to solve the same problem for
            // a Hebrew layout).
            if (IsOpen)
            {
                var keyboard = Keyboard.current;
                if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame) Dismiss();
            }

            if (_flashLeft <= 0f || _flash == null) return;

            // Unscaled: a card can be up while the world is paused or slowed, and the pulse is
            // feedback about the card, not about the simulation.
            _flashLeft -= Time.unscaledDeltaTime;
            var k = Mathf.Clamp01(_flashLeft / FlashSeconds);
            _flash.style.backgroundColor = new Color(_flashColor.r, _flashColor.g, _flashColor.b,
                Mathf.Sin(k * Mathf.PI) * 0.35f);
            if (_flashLeft <= 0f) _flash.style.display = DisplayStyle.None;
        }
    }
}
