using System;
using System.Collections.Generic;
using TheBlock.Game;
using TheBlock.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheBlock.UI.Menus
{
    /// <summary>
    /// "Choose your character" — the port of the panel half of <c>src/ui/character-select.ts</c>
    /// (the render half is <see cref="CharacterPreview"/>).
    ///
    /// <b>The rows come from <see cref="CharacterRoster"/>, not from a list of its own.</b> U26
    /// shipped this screen with a hand-seeded roster of one because there was only Joe; U29 gave the
    /// game a real roster component, and a screen that keeps a second copy of a table is a screen
    /// that will disagree with it. The panel now reads what the builder built.
    ///
    /// The pick is applied immediately and written to <see cref="Progress.CharacterId"/>, which is
    /// where the web puts it too (<c>saveCharacterId</c>) and which already survives a New Game on
    /// purpose: a new campaign is not a new person. Applying it is one call —
    /// <see cref="CharacterRoster.Apply"/> — and that call is the port of <c>main.ts</c>'s
    /// <c>applyCharacter</c> fan-out, including the body on the dance stage.
    /// </summary>
    public class CharacterPanel : MenuPanel
    {
        protected override string PanelName => "character";
        protected override bool Opaque => true;

        [SerializeField] private CharacterPreview preview;
        [SerializeField] private CharacterRoster roster;

        private readonly List<Button> _rows = new();
        private VisualElement _list;
        private Image _image;
        private string _picked = string.Empty;
        private Action _onBack;

        protected override void Awake()
        {
            base.Awake();
            if (preview == null) preview = FindAnyObjectByType<CharacterPreview>(FindObjectsInactive.Include);
            if (roster == null) roster = CharacterRoster.Find();
        }

        protected override void BuildContent(VisualElement overlay)
        {
            overlay.Add(MenuStyle.Heading("Choose your character"));

            // The web's #character-preview box, at its own CSS size.
            _image = new Image { scaleMode = ScaleMode.ScaleToFit };
            _image.style.width = 300f;
            _image.style.height = 360f;
            _image.style.marginBottom = 18f;
            MenuStyle.SetBorder(_image, MenuStyle.Rim, 1f, 12f);
            overlay.Add(_image);

            // A roster reads as a ROW, not as another vertical menu — the web's own note on
            // #title-character-list, and the reason this one panel breaks the column pattern.
            _list = new VisualElement { name = "character-list" };
            _list.style.flexDirection = FlexDirection.Row;
            _list.style.flexWrap = Wrap.Wrap;
            _list.style.justifyContent = Justify.Center;
            _list.style.marginBottom = 22f;
            overlay.Add(_list);

            overlay.Add(MenuStyle.Secondary("Back", Close));

            Populate();
        }

        /// <summary>
        /// Fills the row of buttons from the roster. Called from <see cref="BuildContent"/> and
        /// again on every <see cref="Open"/>: the panel is built in <c>Awake</c> and a roster
        /// rebuilt by the editor between two opens would otherwise show yesterday's names.
        /// </summary>
        private void Populate()
        {
            if (_list == null) return;
            if (roster == null) roster = CharacterRoster.Find();

            var entries = roster != null ? roster.Entries : null;
            if (entries == null || entries.Count == _rows.Count) return;

            _list.Clear();
            _rows.Clear();

            foreach (var entry in entries)
            {
                var id = entry.Id;
                var row = MenuStyle.Secondary(entry.Name, () => Pick(id));
                row.style.width = StyleKeyword.Auto;
                row.style.minWidth = 128f;
                row.style.marginLeft = 6f;
                row.style.marginRight = 6f;
                _list.Add(row);
                _rows.Add(row);
            }
        }

        public void Open(Action onBack)
        {
            _onBack = onBack;

            Populate();

            // Normalise: a stored id from a roster that has since changed resolves to the default
            // rather than leaving nothing highlighted.
            _picked = Resolve(Progress.CharacterId);

            Show();
            Paint();

            if (preview != null)
            {
                preview.SetActive(true);
                if (_image != null) _image.image = preview.Target;
            }
        }

        public void Close()
        {
            preview?.SetActive(false);
            Hide();

            var back = _onBack;
            _onBack = null;
            back?.Invoke();
        }

        public override void Hide()
        {
            // Belt and braces, the same one the web takes: never leave the preview rendering behind
            // a screen nobody is looking at. Closing by Esc goes through Close, but a CloseMenus
            // sweep from GameFlow lands here directly.
            preview?.SetActive(false);
            base.Hide();
        }

        private void Pick(string id)
        {
            if (string.IsNullOrEmpty(id) || id == _picked) return;

            _picked = id;

            // One call dresses the player, the stage dancer and the turntable the player is looking
            // at while they press this. The preview updating in place is the point of doing it here
            // rather than on Close.
            if (roster == null) roster = CharacterRoster.Find();
            if (roster != null) roster.Apply(id);
            else Progress.CharacterId = id;

            Paint();
        }

        private void Paint()
        {
            if (roster == null) return;

            var entries = roster.Entries;
            for (var i = 0; i < _rows.Count && i < entries.Count; i++)
                MenuStyle.Paint(_rows[i], entries[i].Id == _picked);
        }

        private string Resolve(string id)
        {
            if (roster != null && roster.TryResolve(id, out var entry)) return entry.Id;
            return string.IsNullOrEmpty(id) ? CharacterRoster.DefaultId : id;
        }
    }
}
