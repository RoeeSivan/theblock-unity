using TheBlock.Audio;
using TheBlock.Game;
using TheBlock.World;
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
    /// It is three rows, in two sections. Per-bus volume sliders are still the obvious next tenant —
    /// U27 exposed seven mixer parameters for exactly that — but the mix has not been balanced by ear
    /// yet, and a slider over an unbalanced mix hides the imbalance instead of reporting it. A mute
    /// needs no balance to be correct, which is why it could land first.
    ///
    /// U33 added <b>Time of Day</b>, and it belongs here rather than being always-on for the reason
    /// stated in <see cref="Progress.DayNightOn"/>: a moving sun is an addition to this port, and the
    /// off state has to remain provably the look and the cost the rest of the game was built against.
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
        private Button _dayNightButton;
        private Button _soundButton;
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

            _dayNightButton = MenuStyle.Primary("Time of Day", () => ApplyDayNight(!Progress.DayNightOn));
            _dayNightButton.style.marginBottom = 12f;
            overlay.Add(_dayNightButton);

            var dayNightNote = MenuStyle.Body(
                "Cycle moves the sun through a 24-minute day — dawn, noon, dusk, night. " +
                "Fixed is the daylight the game was built in, and costs nothing.");
            dayNightNote.style.maxWidth = 420f;
            dayNightNote.style.marginBottom = 22f;
            overlay.Add(dayNightNote);

            overlay.Add(MenuStyle.Heading("Audio"));

            _soundButton = MenuStyle.Primary("Sound", () => ApplySound(!Mute.SoundOn));
            _soundButton.style.marginBottom = 12f;
            overlay.Add(_soundButton);

            var soundNote = MenuStyle.Body("Silences everything — engines, sirens, music, voices. N toggles it in play.");
            soundNote.style.maxWidth = 420f;
            soundNote.style.marginBottom = 22f;
            overlay.Add(soundNote);

            overlay.Add(MenuStyle.Secondary("Back", Close));
        }

        private void Apply(bool radarOn)
        {
            Progress.RadarOn = radarOn;
            if (map != null) map.SetMinimapVisible(radarOn);
            if (_radarButton != null) _radarButton.text = radarOn ? "Radar:  On" : "Radar:  Off";
        }

        /// <summary>
        /// Settings → Display → Time of Day.
        ///
        /// <b>Unlike the radar row, this one is not pushed on boot from <c>Start</c>.</b>
        /// <see cref="DayNightCycle"/> reads <see cref="Progress.DayNightOn"/> in its own
        /// <c>Start</c>, and it has to: it owns a snapshot of the as-built sky that must be taken
        /// before anything writes it. Two boot-time writers of the same preference is how they drift,
        /// so this method only ever runs off a button press.
        /// </summary>
        private void ApplyDayNight(bool on)
        {
            Progress.DayNightOn = on;
            if (DayNightCycle.Instance != null) DayNightCycle.Instance.SetEnabled(on);
            if (_dayNightButton != null)
                _dayNightButton.text = on ? "Time of Day:  Cycle" : "Time of Day:  Fixed";
        }

        /// <summary>
        /// Settings → Audio → Sound.
        ///
        /// <b>No boot-time push from <c>Start</c>, and for the opposite reason to the day/night
        /// row:</b> <see cref="Mute"/> applies the preference itself at
        /// <c>RuntimeInitializeOnLoadMethod</c> time, before any scene object exists — this method
        /// only relabels the button after a press, and the label is also refreshed in
        /// <see cref="Open"/> because <c>N</c> can flip the flag while the panel is closed.
        /// </summary>
        private void ApplySound(bool on)
        {
            Mute.SoundOn = on;
            if (_soundButton != null) _soundButton.text = on ? "Sound:  On" : "Sound:  Muted";
        }

        /// <summary>Opens over whatever raised it, remembering where Back goes.</summary>
        public void Open(System.Action onBack)
        {
            _onBack = onBack;
            if (_radarButton != null)
                _radarButton.text = Progress.RadarOn ? "Radar:  On" : "Radar:  Off";
            if (_dayNightButton != null)
                _dayNightButton.text = Progress.DayNightOn ? "Time of Day:  Cycle" : "Time of Day:  Fixed";
            if (_soundButton != null)
                _soundButton.text = Mute.SoundOn ? "Sound:  On" : "Sound:  Muted";
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
