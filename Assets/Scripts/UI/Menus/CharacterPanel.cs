using System;
using System.Collections.Generic;
using TheBlock.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheBlock.UI.Menus
{
    /// <summary>
    /// "Choose your character" — the port of the panel half of <c>src/ui/character-select.ts</c>
    /// (the render half is <see cref="CharacterPreview"/>).
    ///
    /// <b>The roster is one row today, and that is the point of shipping it now.</b> Only Joe exists
    /// in this port; Jody and David arrive with U29, which is a config entry and an FBX import each.
    /// Building the screen here means U29 never has to reopen the menu — it adds two
    /// <see cref="Entry"/> rows and two rigs under the turntable.
    ///
    /// The pick is written straight to <see cref="Progress.CharacterId"/>, which is where the web
    /// puts it too (<c>saveCharacterId</c>) and which already survives a New Game on purpose: a new
    /// campaign is not a new person. <b>Fanning it out to the four rigs that wear it — the walking
    /// body, the seated driver, the bike and jetski riders — is U29's job</b>, because until there
    /// is a second body there is nothing for a swap to change.
    /// </summary>
    public class CharacterPanel : MenuPanel
    {
        protected override string PanelName => "character";
        protected override bool Opaque => true;

        [Serializable]
        public struct Entry
        {
            [Tooltip("Stored in PlayerPrefs. Matches characters.config.ts's id when U29 lands.")]
            public string Id;

            [Tooltip("What the button says.")]
            public string Name;
        }

        [Header("Roster — U29 adds Jody and David")]
        [SerializeField] private List<Entry> roster = new();

        [SerializeField] private CharacterPreview preview;

        private readonly List<Button> _rows = new();
        private Image _image;
        private string _picked = string.Empty;
        private Action _onBack;

        protected override void Awake()
        {
            base.Awake();
            if (preview == null) preview = FindAnyObjectByType<CharacterPreview>();
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
            var list = new VisualElement { name = "character-list" };
            list.style.flexDirection = FlexDirection.Row;
            list.style.flexWrap = Wrap.Wrap;
            list.style.justifyContent = Justify.Center;
            list.style.marginBottom = 22f;
            overlay.Add(list);

            foreach (var entry in roster)
            {
                var id = entry.Id;
                var row = MenuStyle.Secondary(entry.Name, () => Pick(id));
                row.style.width = StyleKeyword.Auto;
                row.style.minWidth = 128f;
                row.style.marginLeft = 6f;
                row.style.marginRight = 6f;
                list.Add(row);
                _rows.Add(row);
            }

            overlay.Add(MenuStyle.Secondary("Back", Close));
        }

        public void Open(Action onBack)
        {
            _onBack = onBack;

            // Normalise: a stored id from a roster that has since changed resolves to the first
            // entry rather than leaving nothing highlighted.
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
            Progress.CharacterId = id;
            Paint();
        }

        private void Paint()
        {
            for (var i = 0; i < _rows.Count && i < roster.Count; i++)
                MenuStyle.Paint(_rows[i], roster[i].Id == _picked);
        }

        private string Resolve(string id)
        {
            foreach (var entry in roster)
                if (entry.Id == id)
                    return id;
            return roster.Count > 0 ? roster[0].Id : string.Empty;
        }
    }
}
