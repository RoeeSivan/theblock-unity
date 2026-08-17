using TheBlock.Audio;
using TheBlock.Game;
using TheBlock.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheBlock.UI.Menus
{
    /// <summary>
    /// Settings → Display. <b>The web build has no settings screen at all</b>, so nothing here is a
    /// port - it exists because the user asked for a Radar toggle on 2026-08-15 and because the
    /// mechanism was already built: <see cref="GameMap.SetMinimapVisible"/> landed early, explicitly
    /// labelled as this menu's.
    ///
    /// It is three rows, in two sections. Per-bus volume sliders are still the obvious next tenant -
    /// U27 exposed seven mixer parameters for exactly that - but the mix has not been balanced by ear
    /// yet, and a slider over an unbalanced mix hides the imbalance instead of reporting it. A mute
    /// needs no balance to be correct, which is why it could land first.
    ///
    /// U33 added <b>Time of Day</b>, and it belongs here rather than being always-on for the reason
    /// stated in <see cref="Progress.DayNightOn"/>: a moving sun is an addition to this port, and the
    /// off state has to remain provably the look and the cost the rest of the game was built against.
    ///
    /// <b>The preference now outranks the scene.</b> <c>GameMap.showMinimap</c> is a serialized
    /// field, and a serialized value beats a C# default - but this reads
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
        private Button _ragdollButton;
        private Button _damageButton;
        private Button _soundButton;
        private Button _gpsButton;
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

            _gpsButton = MenuStyle.Primary("GPS Route", () => ApplyGps(!Progress.GpsRouteOn));
            _gpsButton.style.marginBottom = 12f;
            overlay.Add(_gpsButton);

            var gpsNote = MenuStyle.Body(
                "Draws the way to your objective along the streets, on the radar and the map. " +
                "Off leaves just the pin.");
            gpsNote.style.maxWidth = 420f;
            gpsNote.style.marginBottom = 22f;
            overlay.Add(gpsNote);

            _dayNightButton = MenuStyle.Primary("Time of Day", () => ApplyDayNight(!Progress.DayNightOn));
            _dayNightButton.style.marginBottom = 12f;
            overlay.Add(_dayNightButton);

            var dayNightNote = MenuStyle.Body(
                "Cycle moves the sun through a 24-minute day - dawn, noon, dusk, night. " +
                "Fixed is the daylight the game was built in, and costs nothing.");
            dayNightNote.style.maxWidth = 420f;
            dayNightNote.style.marginBottom = 22f;
            overlay.Add(dayNightNote);

            overlay.Add(MenuStyle.Heading("Gameplay"));

            _ragdollButton = MenuStyle.Primary("Ragdolls", () => ApplyRagdolls(!Progress.RagdollsOn));
            _ragdollButton.style.marginBottom = 12f;
            overlay.Add(_ragdollButton);

            var ragdollNote = MenuStyle.Body(
                "Physics takes over a body that is hit by a vehicle. Off plays the animation the " +
                "web build used instead.");
            ragdollNote.style.maxWidth = 420f;
            ragdollNote.style.marginBottom = 22f;
            overlay.Add(ragdollNote);

            _damageButton = MenuStyle.Primary("Vehicle Damage", () => ApplyDamage(Next(Progress.VehicleDamage)));
            _damageButton.style.marginBottom = 12f;
            overlay.Add(_damageButton);

            var damageNote = MenuStyle.Body(
                "Visual dents the bodywork, smokes and burns, and knocks parts off. Full lets a car " +
                "die and explode. Off is the game as it was before.");
            damageNote.style.maxWidth = 420f;
            damageNote.style.marginBottom = 22f;
            overlay.Add(damageNote);

            overlay.Add(MenuStyle.Heading("Audio"));

            _soundButton = MenuStyle.Primary("Sound", () => ApplySound(!Mute.SoundOn));
            _soundButton.style.marginBottom = 12f;
            overlay.Add(_soundButton);

            var soundNote = MenuStyle.Body("Silences everything - engines, sirens, music, voices. N toggles it in play.");
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
        /// Settings → Display → GPS Route (U35c).
        ///
        /// <b>Storage IS the mechanism here</b>, like Ragdolls and unlike Radar: there is nothing to
        /// push to, because <c>GpsRoute.Tick</c> reads <see cref="Progress.GpsRouteOn"/> at the top
        /// of its own tick and returns early. One reader means the preference and the drawing cannot
        /// drift, and turning it off stops the A* rather than merely hiding its result.
        /// </summary>
        private void ApplyGps(bool on)
        {
            Progress.GpsRouteOn = on;
            if (_gpsButton != null) _gpsButton.text = on ? "GPS Route:  On" : "GPS Route:  Off";
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
        /// Settings → Gameplay → Ragdolls (U35a).
        ///
        /// <b>No boot-time push, and nothing to apply.</b> Unlike the radar, which owns a live object
        /// that has to be told, this preference is read at the moment a body is hit and nowhere else -
        /// so the toggle is the storage AND the mechanism, and a body already mid-fall keeps whatever
        /// it started as rather than half-swapping mechanism under the player's eyes.
        /// </summary>
        private void ApplyRagdolls(bool on)
        {
            Progress.RagdollsOn = on;
            if (_ragdollButton != null) _ragdollButton.text = on ? "Ragdolls:  On" : "Ragdolls:  Off";
        }

        /// <summary>
        /// Settings → Gameplay → Vehicle Damage (U35b). Three states on one button, cycling
        /// Off → Visual → Full, because a row of three radio buttons is a new widget and this menu
        /// has exactly one.
        ///
        /// <b>No boot-time push, like the ragdoll row</b> - the preference is read at the moment a car
        /// is struck. Turning it OFF mid-session does have something to undo though, and it is not
        /// this method's job: every damaged car heals itself on the next frame it notices the mode
        /// changed, which keeps the knowledge of what "damaged" means inside the component that owns
        /// it rather than in the menu.
        /// </summary>
        private void ApplyDamage(VehicleDamageMode mode)
        {
            Progress.VehicleDamage = mode;
            if (_damageButton != null) _damageButton.text = DamageLabel(mode);
        }

        private static VehicleDamageMode Next(VehicleDamageMode mode) => mode switch
        {
            VehicleDamageMode.Off => VehicleDamageMode.Visual,
            VehicleDamageMode.Visual => VehicleDamageMode.Full,
            _ => VehicleDamageMode.Off,
        };

        private static string DamageLabel(VehicleDamageMode mode) => mode switch
        {
            VehicleDamageMode.Visual => "Vehicle Damage:  Visual",
            VehicleDamageMode.Full => "Vehicle Damage:  Full",
            _ => "Vehicle Damage:  Off",
        };

        /// <summary>
        /// Settings → Audio → Sound.
        ///
        /// <b>No boot-time push from <c>Start</c>, and for the opposite reason to the day/night
        /// row:</b> <see cref="Mute"/> applies the preference itself at
        /// <c>RuntimeInitializeOnLoadMethod</c> time, before any scene object exists - this method
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
            if (_gpsButton != null)
                _gpsButton.text = Progress.GpsRouteOn ? "GPS Route:  On" : "GPS Route:  Off";
            if (_dayNightButton != null)
                _dayNightButton.text = Progress.DayNightOn ? "Time of Day:  Cycle" : "Time of Day:  Fixed";
            if (_ragdollButton != null)
                _ragdollButton.text = Progress.RagdollsOn ? "Ragdolls:  On" : "Ragdolls:  Off";
            if (_damageButton != null)
                _damageButton.text = DamageLabel(Progress.VehicleDamage);
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
