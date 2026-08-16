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

        /// <summary>A mission's own F/T action — the most specific thing you can do here.</summary>
        public const int PromptMission = 30;

        /// <summary>E gets you into the vehicle you are standing next to.</summary>
        public const int PromptVehicle = 20;

        /// <summary>E gets you through the pizzeria door. Loses to a vehicle, as the E action does.</summary>
        public const int PromptDoor = 10;

        private Label _objective;
        private Label _timer;
        private Label _counter;
        private Label _prompt;
        private Label _hint;

        private float _hintLeft;
        private bool _urgent;

        private string _promptText;
        private int _promptPriority;

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

        /// <summary>
        /// The contextual "Press E to …" line. <b>Immediate mode: claim it every frame you want it
        /// shown, and stop claiming to hide it.</b> Null or empty is not a claim at all.
        ///
        /// It is arbitrated rather than last-writer-wins because three unrelated things now compete
        /// for one line — a mission's F/T action, the vehicle you are standing beside, and the
        /// pizzeria door — and with a latched setter the winner would be whichever component Unity
        /// happened to call last that frame. The web build has the same three and resolves them in
        /// one `if/else` chain in <c>hud-driver.ts</c>; the priorities here ARE that chain's order,
        /// written where each claim is made instead of in a driver that has to know about all of them.
        /// </summary>
        public void SetPrompt(string text, int priority = PromptMission)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (_promptText != null && priority <= _promptPriority) return;
            _promptText = text;
            _promptPriority = priority;
        }

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

            // U28 gave the panel an emoji-capable fallback font (EmojiFontBuilder), so the copy is
            // drawn as written. Until then this ran through `Glyphs.Strip`, at the point of DRAWING
            // rather than of loading — which is exactly what made restoring the emoji a deletion.
            var drawn = text;
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

        /// <summary>
        /// Draws whichever claim won this frame, then forgets it. LateUpdate, so every Update has
        /// already had its say — that is the whole reason the arbitration can be order-independent.
        /// </summary>
        private void LateUpdate()
        {
            SetText(_prompt, _promptText);
            _promptText = null;
            _promptPriority = 0;
        }
    }
}
