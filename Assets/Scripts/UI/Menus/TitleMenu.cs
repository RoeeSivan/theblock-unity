using System.Collections.Generic;
using TheBlock.Game;
using TheBlock.Missions;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheBlock.UI.Menus
{
    /// <summary>
    /// The cold-launch title screen — the port of <c>src/ui/title-menu.ts</c>.
    ///
    /// New Game · Continue · Character · Mission Select · Settings · How to Play, over the city
    /// standing frozen behind it. <see cref="GameFlow"/> decides what each one DOES; this owns which
    /// panel is on screen and what the rows say.
    ///
    /// <b>No Multiplayer button.</b> The web has one; U32 is deferred by decision, and a button that
    /// cannot do anything is worse on a title screen than anywhere else — it is the first thing a
    /// player presses.
    ///
    /// <b>Continue is hidden, not disabled, until there is something to continue.</b> The web's own
    /// gate, and its reasoning holds: <see cref="Progress.UnlockedIndex"/> above 0 means a mission
    /// beyond the first has been reached, which is the only definition of "resumable" this game has.
    ///
    /// <b>Locked rows are inert by being disabled</b>, not by a flag the handler re-checks. A
    /// disabled element never receives the click, so the lock state cannot be right in the paint and
    /// wrong in the callback.
    /// </summary>
    public class TitleMenu : MenuPanel
    {
        protected override string PanelName => "title-menu";
        protected override bool Opaque => false;

        [SerializeField] private Campaign campaign;
        [SerializeField] private CampaignRunner runner;

        /// <summary>Wired by <see cref="GameFlow"/>.</summary>
        public System.Action OnNewGame;
        public System.Action OnContinue;
        public System.Action<int> OnMission;
        public System.Action OnCharacter;
        public System.Action OnSettings;
        public System.Action OnHowToPlay;

        private VisualElement _root;
        private VisualElement _missionPanel;
        private Button _continueButton;
        private readonly List<Button> _missionRows = new();

        /// <summary>
        /// The scrim is heavier than the pause menu's: this covers a world the player has not asked
        /// to see yet, and the six buttons have to read against a city, not a sky.
        /// </summary>
        private static readonly Color TitleScrim = MenuStyle.Ui(0.043f, 0.059f, 0.078f, 0.90f);

        protected override void Awake()
        {
            base.Awake();
            if (campaign == null) campaign = FindAnyObjectByType<Campaign>();
            if (runner == null) runner = FindAnyObjectByType<CampaignRunner>();
        }

        protected override void BuildContent(VisualElement overlay)
        {
            overlay.style.backgroundColor = TitleScrim;

            overlay.Add(MenuStyle.Wordmark("The Block", 64f));

            var tagline = MenuStyle.Body("Florentin, Tel Aviv");
            tagline.style.marginBottom = 18f;
            overlay.Add(tagline);

            // ── root ──────────────────────────────────────────────────────────────────────────
            _root = MenuStyle.Column("title-root");
            overlay.Add(_root);

            _continueButton = MenuStyle.Stack(_root, MenuStyle.Primary("Continue", () => OnContinue?.Invoke()));
            MenuStyle.Stack(_root, MenuStyle.Primary("New Game", () => OnNewGame?.Invoke()));
            MenuStyle.Stack(_root, MenuStyle.Primary("Character", () => OnCharacter?.Invoke()));
            MenuStyle.Stack(_root, MenuStyle.Primary("Mission Select", () => ShowPanel(Panel.Mission)));
            MenuStyle.Stack(_root, MenuStyle.Primary("Settings", () => OnSettings?.Invoke()));
            MenuStyle.Stack(_root, MenuStyle.Primary("How to Play", () => OnHowToPlay?.Invoke()));

            // ── mission select ────────────────────────────────────────────────────────────────
            _missionPanel = MenuStyle.Column("title-mission-panel");
            overlay.Add(_missionPanel);

            MenuStyle.Stack(_missionPanel, MenuStyle.Heading("Mission Select"));

            // Four rows, built empty and repainted on every show — the web's own static-markup
            // idiom, for the same reason: the labels are config and the lock state is a save file,
            // and neither is known at build time.
            var count = campaign != null ? campaign.Missions.Count : 4;
            for (var i = 0; i < count; i++)
            {
                var index = i; // capture per row, not the loop variable
                var row = MenuStyle.Stack(_missionPanel, MenuStyle.Primary("", () => OnMission?.Invoke(index)));
                _missionRows.Add(row);
            }

            var back = MenuStyle.Stack(_missionPanel, MenuStyle.Secondary("Back", () => ShowPanel(Panel.Root)));
            back.style.marginTop = 8f;
        }

        private enum Panel { Root, Mission }

        private void ShowPanel(Panel which)
        {
            if (_root != null)
                _root.style.display = which == Panel.Root ? DisplayStyle.Flex : DisplayStyle.None;
            if (_missionPanel != null)
                _missionPanel.style.display = which == Panel.Mission ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public override void Show()
        {
            base.Show();
            Paint();
            ShowPanel(Panel.Root);
        }

        /// <summary>
        /// Fresh labels and a fresh lock state on every show. Cheap, and it means a Quit to Title
        /// after finishing a mission comes back with that mission's successor already unlocked.
        /// </summary>
        private void Paint()
        {
            if (_continueButton != null)
                _continueButton.style.display = Progress.UnlockedIndex > 0
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;

            var unlocked = Progress.UnlockedIndex;
            for (var i = 0; i < _missionRows.Count; i++)
            {
                var row = _missionRows[i];
                var mission = campaign != null && i < campaign.Missions.Count ? campaign.Missions[i] : null;
                var title = mission != null ? mission.Title : $"Mission {i + 1}";

                if (i <= unlocked)
                {
                    // Opaque, because a row's ink is peach (or near-black once picked) and it would
                    // otherwise tint the mission's own 🍕 / 🕺 / 🚁 / 🛟 with it.
                    row.text = Glyphs.Opaque($"{i + 1}.  {Prefix(mission)}{title}");
                    MenuStyle.Paint(row, primary: true);
                }
                else
                {
                    row.text = $"{i + 1}.  {title}   —   Locked";
                    MenuStyle.PaintLocked(row);
                }
            }
        }

        /// <summary>The step's glyph from <c>campaign.config.ts</c>, if the copy carries one.</summary>
        private string Prefix(MissionBehaviour mission)
        {
            if (mission == null || runner == null) return string.Empty;
            var emoji = runner.TextFor(mission.Id)?.Emoji;
            return string.IsNullOrEmpty(emoji) ? string.Empty : emoji + " ";
        }
    }
}
