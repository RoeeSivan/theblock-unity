using UnityEngine;
using UnityEngine.UIElements;

namespace TheBlock.UI.Menus
{
    /// <summary>
    /// The shared "How to Play" reference — the port of <c>src/ui/controls-guide.ts</c> and the
    /// markup it drives (<c>index.html</c> #controls-guide).
    ///
    /// One overlay, two entry points: the title screen and the pause menu both open it, which is
    /// exactly why it is its own component and not a sub-panel of either.
    ///
    /// <b>One row of the web's list is deliberately missing.</b> The radio keys (<c>[</c>, <c>]</c>,
    /// <c>\</c>) describe a system U27 deferred. A controls screen that lists a key which does
    /// nothing is worse than one that is short: it is the one screen a stuck player reads, and it
    /// has to be true. It comes back with its unit. U28 added the shop and the power-up hotkeys on
    /// exactly that rule, and U28b's refuel row landed the same way, the moment it was true.
    /// </summary>
    public class ControlsGuide : MenuPanel
    {
        protected override string PanelName => "controls-guide";
        protected override bool Opaque => true;

        /// <summary>Where Back returns to — set by whoever opened it.</summary>
        private System.Action _onBack;

        private static readonly (string keys, string what)[] OnFoot =
        {
            ("W A S D", "Move (or arrow keys)"),
            ("Alt", "Hold to jog"),
            ("Shift", "Hold to sprint"),
            ("Space", "Jump"),
            ("E", "Enter vehicle / building"),
            ("T", "Talk / start a job"),
            ("F", "Deliver / action"),
            ("F", "Retry a failed mission"),
            ("E", "Shop at the 7-Eleven counter"),
            ("1 – 4", "Use a power-up"),
        };

        private static readonly (string keys, string what)[] Driving =
        {
            ("W A S D", "Drive & steer"),
            ("Space", "Brake"),
            ("Space", "Hold at a gas pump — refuel"),
            ("E", "Exit vehicle"),
            ("R", "Respawn / flip vehicle"),
            ("Space / Shift", "Helicopter up / down"),
        };

        private static readonly (string keys, string what)[] General =
        {
            ("M", "Map"),
            ("Esc", "Pause / menu"),
            ("< v ^ >", "Dance — hit the arrows"),
        };

        protected override void BuildContent(VisualElement overlay)
        {
            overlay.Add(MenuStyle.Wordmark("How to Play", 44f));

            var groups = new VisualElement { name = "controls-guide-groups" };
            groups.style.flexDirection = FlexDirection.Row;
            groups.style.alignItems = Align.FlexStart;
            groups.style.justifyContent = Justify.Center;
            groups.style.flexWrap = Wrap.Wrap;
            groups.style.marginBottom = 24f;
            overlay.Add(groups);

            groups.Add(Group("On Foot", OnFoot));
            groups.Add(Group("Driving", Driving));
            groups.Add(Group("General", General));

            var back = MenuStyle.Secondary("Back", () => Close());
            back.style.marginTop = 8f;
            overlay.Add(back);
        }

        private static VisualElement Group(string heading, (string keys, string what)[] rows)
        {
            var group = new VisualElement();
            group.style.flexDirection = FlexDirection.Column;
            group.style.marginLeft = 22f;
            group.style.marginRight = 22f;
            group.style.minWidth = 260f;

            group.Add(MenuStyle.Heading(heading, 18f));

            foreach (var (keys, what) in rows)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 7f;

                var chip = new Label(keys);
                chip.style.width = 108f;
                chip.style.fontSize = 14f;
                chip.style.color = MenuStyle.SecondaryInk;
                chip.style.unityFontStyleAndWeight = FontStyle.Bold;
                chip.style.unityTextAlign = TextAnchor.MiddleCenter;
                chip.style.paddingTop = 4f;
                chip.style.paddingBottom = 4f;
                chip.style.backgroundColor = MenuStyle.SecondaryFill;
                MenuStyle.SetBorder(chip, MenuStyle.Rim, 1f, 5f);
                row.Add(chip);

                var text = new Label(what);
                text.style.marginLeft = 12f;
                text.style.fontSize = 15f;
                text.style.color = MenuStyle.Ui(0.87f, 0.9f, 0.94f);
                row.Add(text);

                group.Add(row);
            }

            return group;
        }

        /// <summary>Opens the guide over whatever raised it, remembering where Back goes.</summary>
        public void Open(System.Action onBack)
        {
            _onBack = onBack;
            Show();
        }

        /// <summary>Back / Esc: close and hand the screen back.</summary>
        public void Close()
        {
            Hide();
            var back = _onBack;
            _onBack = null;
            back?.Invoke();
        }
    }
}
