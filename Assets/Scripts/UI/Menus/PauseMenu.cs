using UnityEngine.UIElements;

namespace TheBlock.UI.Menus
{
    /// <summary>
    /// The in-game pause overlay - the port of <c>src/ui/pause-menu.ts</c>.
    ///
    /// The web's version is three buttons and nothing else, and its own comment says why: "the frame
    /// loop owns the paused flag and the freeze - this is purely the overlay + callbacks". The same
    /// division holds here, with <see cref="GameFlow"/> as the frame loop and
    /// <see cref="Core.Pause"/> as the freeze. A fourth button, Settings, is the only addition, and
    /// it is where the Radar toggle lives.
    ///
    /// <b>The scrim is translucent on purpose.</b> The title screen hides the world because there is
    /// no run behind it yet; a pause is a run you are coming back to, and seeing it frozen is the
    /// difference between "paused" and "quit".
    /// </summary>
    public class PauseMenu : MenuPanel
    {
        protected override string PanelName => "pause-menu";
        protected override bool Opaque => false;

        /// <summary>Wired by <see cref="GameFlow"/>, which owns all four of these decisions.</summary>
        public System.Action OnResume;
        public System.Action OnSettings;
        public System.Action OnHowToPlay;
        public System.Action OnQuit;

        protected override void BuildContent(VisualElement overlay)
        {
            overlay.Add(MenuStyle.Wordmark("Paused", 44f));

            var column = MenuStyle.Column("pause-root");
            column.style.display = DisplayStyle.Flex;
            overlay.Add(column);

            MenuStyle.Stack(column, MenuStyle.Primary("Resume", () => OnResume?.Invoke()));
            MenuStyle.Stack(column, MenuStyle.Primary("Settings", () => OnSettings?.Invoke()));
            MenuStyle.Stack(column, MenuStyle.Primary("How to Play", () => OnHowToPlay?.Invoke()));
            MenuStyle.Stack(column, MenuStyle.Secondary("Quit to Title", () => OnQuit?.Invoke()));
        }
    }
}
