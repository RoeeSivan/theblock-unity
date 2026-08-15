using UnityEngine;
using UnityEngine.UIElements;

namespace TheBlock.UI
{
    /// <summary>
    /// Everything the campaign writes on screen while you are playing: the objective line, the
    /// mission clock, the carried-pizzas counter, the contextual prompt, and the one-time hints.
    /// The port of the <c>#hud-mission</c> / <c>#hud-timer</c> / <c>#hud-pizzas</c> /
    /// <c>#enter-prompt</c> elements in the web build's <c>index.html</c>, driven by
    /// <c>ui/hud-driver.ts</c>.
    ///
    /// <b>One UIDocument, always.</b> This goes on the panel U14 built and U19 already extended —
    /// the U25 row is explicit that a second panel means a second PanelSettings, a second scale mode
    /// and an argument about z-order that nobody wins.
    ///
    /// <b>Functional, not designed.</b> Legibility is the bar here; typography, an emoji-capable
    /// font and the layout pass are U25's, and the missions have to exist before anyone can lay out
    /// a HUD for them.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class MissionHud : MonoBehaviour
    {
        private const float HintSeconds = 4f;

        private Label _objective;
        private Label _timer;
        private Label _counter;
        private Label _prompt;
        private Label _hint;

        private float _hintLeft;
        private bool _urgent;

        /// <summary>
        /// Awake, and guarded, for the reason written out on <see cref="BriefingCard"/>: a HUD built
        /// in Start races every Start that writes to it, and the loser builds a second copy.
        /// </summary>
        private void Awake() => Build();

        private void Build()
        {
            if (_objective != null) return;

            var root = GetComponent<UIDocument>().rootVisualElement;

            // Top-centre stack: the objective line with the clock under it. Absolute and
            // pointer-transparent, like every other HUD layer on this document — a HUD that eats
            // clicks is a HUD that breaks the map.
            var top = new VisualElement { name = "mission-top" };
            top.style.position = Position.Absolute;
            top.style.top = 14f;
            top.style.left = 0f;
            top.style.right = 0f;
            top.style.alignItems = Align.Center;
            top.pickingMode = PickingMode.Ignore;

            _objective = MakeLabel(22f, Color.white, FontStyle.Bold);
            _objective.style.display = DisplayStyle.None;
            top.Add(_objective);

            _timer = MakeLabel(30f, Color.white, FontStyle.Bold);
            _timer.style.display = DisplayStyle.None;
            top.Add(_timer);

            root.Add(top);

            // Carried pizzas, under the cash WantedHud already owns at the top right.
            _counter = MakeLabel(20f, Color.white, FontStyle.Bold);
            _counter.style.position = Position.Absolute;
            _counter.style.top = 86f;
            _counter.style.right = 24f;
            _counter.style.unityTextAlign = TextAnchor.MiddleRight;
            _counter.style.display = DisplayStyle.None;
            root.Add(_counter);

            // The contextual prompt sits low-centre, where the web build puts it: it belongs to the
            // thing you are standing next to, not to the mission.
            _prompt = MakeLabel(21f, Color.white, FontStyle.Bold);
            _prompt.style.position = Position.Absolute;
            _prompt.style.bottom = 90f;
            _prompt.style.left = 0f;
            _prompt.style.right = 0f;
            _prompt.style.unityTextAlign = TextAnchor.MiddleCenter;
            _prompt.style.display = DisplayStyle.None;
            root.Add(_prompt);

            // A hint fades itself out and never repeats — see Game/Onboarding.
            _hint = MakeLabel(19f, new Color(1f, 0.93f, 0.6f), FontStyle.Normal);
            _hint.style.position = Position.Absolute;
            _hint.style.bottom = 140f;
            _hint.style.left = 0f;
            _hint.style.right = 0f;
            _hint.style.unityTextAlign = TextAnchor.MiddleCenter;
            _hint.style.display = DisplayStyle.None;
            root.Add(_hint);
        }

        private static Label MakeLabel(float size, Color color, FontStyle weight)
        {
            var label = new Label(string.Empty);
            label.style.fontSize = size;
            label.style.color = color;
            label.style.unityFontStyleAndWeight = weight;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.pickingMode = PickingMode.Ignore;

            // A pure text shadow instead of a plate behind the words. The web build uses a CSS
            // text-shadow for the same reason: the HUD has to stay readable over a bright sky AND
            // over dark asphalt without ever boxing off a chunk of the screen.
            label.style.textShadow = new TextShadow
            {
                offset = new Vector2(0f, 2f),
                blurRadius = 6f,
                color = new Color(0f, 0f, 0f, 0.85f),
            };
            return label;
        }

        /// <summary>The objective line. Null hides it.</summary>
        public void SetObjective(string text) => SetText(_objective, text);

        /// <summary>
        /// The mission clock. Null hides it; <paramref name="urgent"/> turns it red and pulses,
        /// which is what <c>lowTimeSec</c> buys.
        /// </summary>
        public void SetTimer(float? seconds, bool urgent = false)
        {
            if (_timer == null) return;
            if (!seconds.HasValue)
            {
                _timer.style.display = DisplayStyle.None;
                _urgent = false;
                return;
            }

            var whole = Mathf.Max(0, Mathf.CeilToInt(seconds.Value));
            _timer.text = $"{whole / 60}:{whole % 60:00}";
            _timer.style.display = DisplayStyle.Flex;
            _urgent = urgent;
            if (!urgent) _timer.style.color = Color.white;
        }

        /// <summary>The carried-pizzas readout, or any per-mission tally. Null hides it.</summary>
        public void SetCounter(string text) => SetText(_counter, text);

        /// <summary>The contextual "Press E to …" line. Null hides it.</summary>
        public void SetPrompt(string text) => SetText(_prompt, text);

        /// <summary>Flashes a one-time nudge that fades itself out.</summary>
        public void ShowHint(string text)
        {
            if (_hint == null || string.IsNullOrEmpty(text)) return;
            _hint.text = text;
            _hint.style.display = DisplayStyle.Flex;
            _hintLeft = HintSeconds;
        }

        private static void SetText(Label label, string text)
        {
            if (label == null) return;

            // Emoji come out until U25 ships a font that has any — see Glyphs. Stripped HERE, at the
            // point of drawing, so the copy itself is never edited and restoring them is a deletion.
            var drawn = Glyphs.Strip(text);
            if (string.IsNullOrEmpty(drawn))
            {
                label.style.display = DisplayStyle.None;
                return;
            }

            label.text = drawn;
            label.style.display = DisplayStyle.Flex;
        }

        private void Update()
        {
            if (_urgent && _timer != null)
            {
                // 2 Hz between red and white. Unscaled, because a pause must not freeze the one
                // element whose whole job is to say "hurry".
                var pulse = Mathf.PingPong(Time.unscaledTime * 4f, 1f);
                _timer.style.color = Color.Lerp(new Color(1f, 0.27f, 0.27f), Color.white, pulse * 0.4f);
            }

            if (_hintLeft <= 0f || _hint == null) return;
            _hintLeft -= Time.unscaledDeltaTime;
            _hint.style.opacity = Mathf.Clamp01(_hintLeft);
            if (_hintLeft <= 0f) _hint.style.display = DisplayStyle.None;
        }
    }
}
