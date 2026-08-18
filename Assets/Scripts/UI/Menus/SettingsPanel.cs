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
        private Button _propsButton;
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

        /// <summary>
        /// Two columns, not one. Six rows of button + blurb stack to ~1,050 reference px in a single
        /// column, and the panel's reference frame is 1200×800 - so on the first macOS Player
        /// (16:10) the Audio section and Back sat below the bottom edge, unreachable. It fit in the
        /// Editor only because the Game view there was taller. Display + Audio on the left, Gameplay
        /// on the right, and the tallest column is ~530 px.
        /// </summary>
        protected override void BuildContent(VisualElement overlay)
        {
            overlay.Add(MenuStyle.Wordmark("Settings", 44f));

            var columns = new VisualElement { name = "settings-columns" };
            columns.style.flexDirection = FlexDirection.Row;
            columns.style.alignItems = Align.FlexStart;
            columns.style.justifyContent = Justify.Center;
            columns.style.marginBottom = 8f;
            overlay.Add(columns);

            var left = SettingsColumn("settings-left");
            var right = SettingsColumn("settings-right");
            columns.Add(left);
            columns.Add(right);

            left.Add(MenuStyle.Heading("Display"));

            _radarButton = MenuStyle.Primary("Radar", () => Apply(!Progress.RadarOn));
            Row(left, _radarButton, "The corner minimap. M still opens the full map with it off.");

            _dayNightButton = MenuStyle.Primary("Time of Day", () => ApplyDayNight(!Progress.DayNightOn));
            Row(left, _dayNightButton,
                "Cycle moves the sun through a 24-minute day - dawn, noon, dusk, night. " +
                "Fixed is the daylight the game was built in, and costs nothing.");

            left.Add(MenuStyle.Heading("Audio"));

            _soundButton = MenuStyle.Primary("Sound", () => ApplySound(!Mute.SoundOn));
            Row(left, _soundButton, "Silences everything - engines, sirens, music, voices. N toggles it in play.");

            right.Add(MenuStyle.Heading("Gameplay"));

            _ragdollButton = MenuStyle.Primary("Ragdolls", () => ApplyRagdolls(!Progress.RagdollsOn));
            Row(right, _ragdollButton,
                "Physics takes over a body that is hit by a vehicle. Off plays the animation the " +
                "web build used instead.");

            _damageButton = MenuStyle.Primary("Vehicle Damage", () => ApplyDamage(Next(Progress.VehicleDamage)));
            Row(right, _damageButton,
                "Visual dents the bodywork, smokes and burns, and knocks parts off. Full lets a car " +
                "die and explode. Off is the game as it was before.");

            _propsButton = MenuStyle.Primary("Street Props", () => ApplyProps(!Progress.BreakablePropsOn));
            Row(right, _propsButton,
                "Cones, bins and benches at the kerb that cars send flying, and traffic lights that " +
                "topple when rammed. Off is the game as it was.");

            overlay.Add(MenuStyle.Secondary("Back", Close));
        }

        private static VisualElement SettingsColumn(string name)
        {
            var column = new VisualElement { name = name };
            column.style.flexDirection = FlexDirection.Column;
            column.style.alignItems = Align.Center;
            column.style.width = 420f;
            column.style.marginLeft = 24f;
            column.style.marginRight = 24f;
            return column;
        }

        /// <summary>One setting: the toggle button and the blurb under it, spaced like every row was.</summary>
        private static void Row(VisualElement column, Button button, string blurb)
        {
            button.style.marginBottom = 12f;
            column.Add(button);

            var note = MenuStyle.Body(blurb);
            note.style.maxWidth = 420f;
            note.style.marginBottom = 22f;
            column.Add(note);
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

        /// <summary>
        /// Settings → Gameplay → Street Props (U35h). <b>Pushed, like the radar row</b> - unlike a
        /// ragdoll, a prop is a standing object, so switching Off has something to undo right now:
        /// <see cref="PropSystem.SetEnabled"/> restores and despawns every prop and makes the poles
        /// rigid again, and On spawns them back. The boot-time read is <c>PropSystem</c>'s own
        /// <c>Start</c>, not this panel's, for the day/night reason: one boot-time writer.
        /// </summary>
        private void ApplyProps(bool on)
        {
            Progress.BreakablePropsOn = on;
            if (PropSystem.Instance != null) PropSystem.Instance.SetEnabled(on);
            if (_propsButton != null) _propsButton.text = on ? "Street Props:  On" : "Street Props:  Off";
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
            if (_dayNightButton != null)
                _dayNightButton.text = Progress.DayNightOn ? "Time of Day:  Cycle" : "Time of Day:  Fixed";
            if (_ragdollButton != null)
                _ragdollButton.text = Progress.RagdollsOn ? "Ragdolls:  On" : "Ragdolls:  Off";
            if (_damageButton != null)
                _damageButton.text = DamageLabel(Progress.VehicleDamage);
            if (_propsButton != null)
                _propsButton.text = Progress.BreakablePropsOn ? "Street Props:  On" : "Street Props:  Off";
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
