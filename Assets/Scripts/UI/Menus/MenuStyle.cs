using TheBlock.Audio;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheBlock.UI.Menus
{
    /// <summary>
    /// The menus' shared look, in one place - the port of <c>src/styles/title.css</c> and
    /// <c>src/styles/loading.css</c>.
    ///
    /// Every colour here is read off those two files rather than re-picked, because the title
    /// screen, the loading bar and the pause overlay are deliberately the same product in the web
    /// build ("reuses the loading-screen visual language … so it reads as the same product") and
    /// three hand-tuned oranges would undo that.
    ///
    /// <b>Two things the CSS has and UI Toolkit does not, and both are deliberate flattenings:</b>
    /// the radial background gradient and the 90° button gradient. A runtime panel can only take a
    /// <c>background-image</c> texture, so reproducing them means generating and shipping two
    /// textures to carry a difference nobody can name at a glance. Each is flattened to the colour
    /// at its own midpoint instead.
    ///
    /// <see cref="Stretch"/> and <see cref="SetBorder"/> came out of <see cref="BriefingCard"/>,
    /// which is now the fourth caller.
    /// </summary>
    public static class MenuStyle
    {
        // ── the palette ───────────────────────────────────────────────────────────────────────
        //
        // EVERY colour goes through Ui(), and that is not decoration. This project renders in
        // LINEAR colour space (URP's default), and a runtime UI Toolkit panel hands the Color
        // straight to its shader as a vertex colour - so a value written as sRGB is drawn as if it
        // were already linear, which lightens it. Measured on the first title screen: a 0.90-alpha
        // near-black scrim came out as a pale blue-grey haze that the city read straight through,
        // and the #ff9440 buttons came out peach. Nothing logs; it just looks wrong in a way that
        // reads as "the designer picked bad colours" rather than as a bug.
        //
        // This is the same gamma trap as the gltfast-basecolorfactor-gamma memory, pointing the
        // other way: THERE the value had to stay sRGB, HERE it has to be converted.

        /// <summary>The dark backdrop, flattened out of the CSS radial (#1d2733 → #0b0f14).</summary>
        public static readonly Color Backdrop = Ui(0.078f, 0.106f, 0.141f);

        /// <summary>The pause scrim: rgba(11, 15, 20, 0.82), so the frozen world reads through.</summary>
        public static readonly Color Scrim = Ui(0.043f, 0.059f, 0.078f, 0.82f);

        /// <summary>The primary button, flattened out of #ffae42 → #ff7847.</summary>
        public static readonly Color Accent = Ui(1f, 0.580f, 0.278f);

        /// <summary>Ink on the orange: #1a1208.</summary>
        public static readonly Color AccentInk = Ui(0.102f, 0.071f, 0.031f);

        /// <summary>The secondary (Back) button: rgba(255, 255, 255, 0.14).</summary>
        public static readonly Color SecondaryFill = Ui(1f, 1f, 1f, 0.14f);

        /// <summary>Ink on a secondary button: #ffe9d2.</summary>
        public static readonly Color SecondaryInk = Ui(1f, 0.914f, 0.824f);

        /// <summary>A locked mission row: rgba(255, 255, 255, 0.10) with 50% white text.</summary>
        public static readonly Color Locked = Ui(1f, 1f, 1f, 0.10f);

        public static readonly Color LockedInk = Ui(1f, 1f, 1f, 0.5f);

        /// <summary>Body copy under a heading: rgba(255, 255, 255, 0.55).</summary>
        public static readonly Color Muted = Ui(1f, 1f, 1f, 0.55f);

        /// <summary>The card / panel backing the briefing already uses.</summary>
        public static readonly Color Panel = Ui(0.06f, 0.07f, 0.09f, 0.95f);

        public static readonly Color Rim = Ui(1f, 1f, 1f, 0.18f);

        /// <summary>
        /// An sRGB colour, as the runtime panel needs it. Alpha is untouched - only the three
        /// channels are a gamma quantity.
        ///
        /// <b>A compile-time define, not <c>QualitySettings.activeColorSpace</c>.</b> Every colour
        /// above is a <c>static readonly</c>, so its initialiser runs the first time the type is
        /// touched - which is inside a MonoBehaviour constructor, where Unity forbids that call
        /// outright: "get_activeColorSpace is not allowed to be called from a MonoBehaviour
        /// constructor". <c>UNITY_COLORSPACE_GAMMA</c> is the same answer with no call at all.
        /// </summary>
        public static Color Ui(float r, float g, float b, float a = 1f)
        {
            var color = new Color(r, g, b, a);
#if UNITY_COLORSPACE_GAMMA
            return color;
#else
            return color.linear;
#endif
        }

        /// <summary>The web's `.title-btn` width: min(340px, 78vw).</summary>
        private const float ButtonWidth = 340f;

        // ── layout helpers ────────────────────────────────────────────────────────────────────

        /// <summary>Pins an element to all four edges of its parent.</summary>
        public static void Stretch(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.top = 0f;
            element.style.bottom = 0f;
            element.style.left = 0f;
            element.style.right = 0f;
        }

        /// <summary>One border on all four sides - UI Toolkit has no shorthand at the style level.</summary>
        public static void SetBorder(VisualElement element, Color color, float width, float radius)
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

        /// <summary>
        /// A full-screen overlay, centred column, hidden. Every menu in this namespace is one of
        /// these; the only thing that differs is whether the backing is opaque (title) or a scrim
        /// the world reads through (pause).
        /// </summary>
        public static VisualElement Overlay(string name, bool opaque)
        {
            var overlay = new VisualElement { name = name };
            Stretch(overlay);
            overlay.style.backgroundColor = opaque ? Backdrop : Scrim;
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;
            overlay.style.display = DisplayStyle.None;
            return overlay;
        }

        /// <summary>A vertical stack of buttons - `#title-root`, `#pause-root`, the mission list.</summary>
        public static VisualElement Column(string name, float gap = 12f)
        {
            var column = new VisualElement { name = name };
            column.style.flexDirection = FlexDirection.Column;
            column.style.alignItems = Align.Center;
            column.style.display = DisplayStyle.None;
            // No `gap` in this UI Toolkit version; the children carry their own bottom margin.
            column.userData = gap;
            return column;
        }

        /// <summary>Adds a child to a <see cref="Column"/>, spacing it by the column's gap.</summary>
        public static T Stack<T>(VisualElement column, T child) where T : VisualElement
        {
            child.style.marginBottom = column.userData is float gap ? gap : 12f;
            column.Add(child);
            return child;
        }

        // ── text ──────────────────────────────────────────────────────────────────────────────

        /// <summary>The wordmark: `#title-name` at 64, `#pause-title` at 44, `#loading-title` at 44.</summary>
        public static Label Wordmark(string text, float size)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.color = Color.white;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.letterSpacing = size >= 60f ? 3f : 2f;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.marginBottom = 18f;
            return label;
        }

        /// <summary>A sub-panel heading - `#title-character-heading`, the controls-guide columns.</summary>
        public static Label Heading(string text, float size = 22f)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.color = new Color(1f, 1f, 1f, 0.75f);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.letterSpacing = 2f;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.marginBottom = 10f;
            return label;
        }

        /// <summary>Quiet body copy - the tagline, a status line, a blurb.</summary>
        public static Label Body(string text, float size = 15f)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.color = Muted;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            return label;
        }

        // ── buttons ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The orange primary. <b>The click sound is in the factory, not at the call sites</b> - the
        /// web calls <c>playUiClick()</c> by hand in all twenty of its handlers and one of them will
        /// eventually be the one that forgets.
        /// </summary>
        public static Button Primary(string text, System.Action onClick) =>
            Make(text, onClick, Accent, AccentInk, FontStyle.Bold, 18f);

        /// <summary>The dimmer treatment `#title-mission-back` and `.char-btn` use.</summary>
        public static Button Secondary(string text, System.Action onClick) =>
            Make(text, onClick, SecondaryFill, SecondaryInk, FontStyle.Normal, 18f);

        /// <summary>Repaints a live button between the two treatments - a picked character row.</summary>
        public static void Paint(Button button, bool primary)
        {
            button.style.backgroundColor = primary ? Accent : SecondaryFill;
            button.style.color = primary ? AccentInk : SecondaryInk;
            button.style.unityFontStyleAndWeight = primary ? FontStyle.Bold : FontStyle.Normal;
            button.SetEnabled(true);
        }

        /// <summary>
        /// Dims a row and makes it inert. <c>SetEnabled(false)</c> rather than a flag the click
        /// handler checks: a disabled element does not receive the event at all, so there is no
        /// second place for the lock state to be wrong.
        /// </summary>
        public static void PaintLocked(Button button)
        {
            button.style.backgroundColor = Locked;
            button.style.color = LockedInk;
            button.style.unityFontStyleAndWeight = FontStyle.Normal;
            button.SetEnabled(false);
        }

        private static Button Make(string text, System.Action onClick, Color background, Color ink,
                                   FontStyle weight, float size)
        {
            var button = new Button(() =>
            {
                GameAudio.Cue(SfxCue.UiClick);
                onClick?.Invoke();
            })
            {
                text = text,
            };

            // A FIXED width, not the CSS's `min(340px, 78vw)`. A percentage max-width resolves
            // against the parent's definite size, and these columns have none - their width comes
            // from their children - so the percentage came back undefined and the button collapsed
            // to its text (measured: 162 px instead of 340). The viewport clamp was a phone guard on
            // a build that had to run in a browser window; this one is desktop-first.
            button.style.width = ButtonWidth;
            button.style.flexShrink = 0f;
            button.style.paddingTop = 13f;
            button.style.paddingBottom = 13f;
            button.style.paddingLeft = 28f;
            button.style.paddingRight = 28f;
            button.style.marginLeft = 0f;
            button.style.marginRight = 0f;
            button.style.backgroundColor = background;
            button.style.color = ink;
            button.style.fontSize = size;
            button.style.letterSpacing = 1f;
            button.style.unityFontStyleAndWeight = weight;
            SetBorder(button, Color.clear, 0f, 8f);

            // The CSS hover is `transform: scale(1.04)` plus a glow. Scale is free here; the glow
            // would be a box-shadow, which a runtime panel does not have.
            button.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (button.enabledSelf) button.style.scale = new Scale(new Vector3(1.04f, 1.04f, 1f));
            });
            button.RegisterCallback<MouseLeaveEvent>(_ =>
                button.style.scale = new Scale(Vector3.one));

            return button;
        }
    }
}
