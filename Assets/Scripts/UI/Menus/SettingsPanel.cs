using TheBlock.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheBlock.UI.Menus
{
    /// <summary>
    /// Settings → Display. <b>The web build has no settings screen at all</b>, so nothing here is a
    /// port — it exists because the user asked for a Radar toggle on 2026-08-15 and because the
    /// mechanism was already built: <see cref="GameMap.SetMinimapVisible"/> landed early, explicitly
    /// labelled as this menu's.
    ///
    /// It is deliberately one row. Volume sliders are the obvious next tenant — U27 exposed seven
    /// mixer parameters for exactly that — but the mix has not been balanced by ear yet, and a
    /// slider over an unbalanced mix hides the imbalance instead of reporting it.
    ///
    /// <b>The preference now outranks the scene.</b> <c>GameMap.showMinimap</c> is a serialized
    /// field, and a serialized value beats a C# default — but this reads
    /// <see cref="Progress.RadarOn"/> in <c>Start</c> and writes it into the map, so from U26 on the
    /// scene checkbox is only what a profile that has never touched this screen inherits.
    /// </summary>
    public class SettingsPanel : MenuPanel
    {
        protected override string PanelName => "settings";
        protected override bool Opaque => true;

        [SerializeField] private GameMap map;

        private Button _radarButton;
        private System.Action _onBack;

        protected override void Awake()
        {
            base.Awake();
            if (map == null) map = FindAnyObjectByType<GameMap>();
        }

        private void Start()
        {
            // Push the stored preference into the map on every boot. Safe in either Start order:
            // SetMinimapVisible tolerates being called before GameMap has built its panel.
            Apply(Progress.RadarOn);
        }

        protected override void BuildContent(VisualElement overlay)
        {
            overlay.Add(MenuStyle.Wordmark("Settings", 44f));
            overlay.Add(MenuStyle.Heading("Display"));

            _radarButton = MenuStyle.Primary("Radar", () => Apply(!Progress.RadarOn));
            _radarButton.style.marginBottom = 12f;
            overlay.Add(_radarButton);

            var note = MenuStyle.Body("The corner minimap. M still opens the full map with it off.");
            note.style.maxWidth = 420f;
            note.style.marginBottom = 22f;
            overlay.Add(note);

            overlay.Add(MenuStyle.Secondary("Back", Close));
        }

        private void Apply(bool radarOn)
        {
            Progress.RadarOn = radarOn;
            if (map != null) map.SetMinimapVisible(radarOn);
            if (_radarButton != null) _radarButton.text = radarOn ? "Radar:  On" : "Radar:  Off";
        }

        /// <summary>Opens over whatever raised it, remembering where Back goes.</summary>
        public void Open(System.Action onBack)
        {
            _onBack = onBack;
            if (_radarButton != null)
                _radarButton.text = Progress.RadarOn ? "Radar:  On" : "Radar:  Off";
            Show();
        }

        public void Close()
        {
            Hide();
            var back = _onBack;
            _onBack = null;
            back?.Invoke();
        }
    }
}
