using TheBlock.Core;
using TheBlock.Player;
using TheBlock.UI.Menus;
using TheBlock.Vehicles;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheBlock.UI
{
    /// <summary>
    /// The two meters the player reads about themselves: how fast they are going, and how much sprint
    /// they have left — the port of <c>hud.ts</c>'s <c>setSpeed</c> and <c>setStamina</c>.
    ///
    /// Both were missed. U25 called the HUD done on the strength of what U14, U19 and U20 had already
    /// put on the panel, and these two are the only surfaces of <c>hud.ts</c> that nothing else
    /// happened to build — which is why <see cref="PlayerController.StaminaFraction"/> has carried
    /// the comment "for the U25 HUD" since U6 with no reader.
    ///
    /// <b>They are mutually exclusive by mode, exactly as the web build has them.</b> The bar is on
    /// foot only and the readout is for whatever you are driving, so the two are never both saying
    /// something about you at once.
    ///
    /// <b>The bar's slot is shared with <see cref="FuelGauge"/> on purpose.</b> `hud.css`'s comment
    /// is explicit about it: the fuel bar takes the SAME position because it is driving-only and this
    /// is on-foot-only, so "the bar above the radar is your meter" stays true in both modes. U28b
    /// landed and filled this slot rather than finding a new one; the position itself is
    /// <see cref="BarSlotBottomPx"/>, named here so the two cannot drift apart.
    ///
    /// <b>Hidden with <c>display</c>, never <c>visibility</c>.</b> <see cref="GameFlow"/> owns
    /// <c>visibility</c> for taking the whole gameplay HUD off behind a menu, and a second writer of
    /// that property is what silently broke the radar toggle once.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class PlayerMeters : MonoBehaviour
    {
        /// <summary>
        /// Radar edge and inset, from <see cref="GameMap"/> — the web's `#map { 200px; 12px }`. The
        /// bar sits a hair above it and matches its width, so the two read as one corner cluster.
        /// </summary>
        internal const float RadarSizePx = 200f;

        internal const float RadarInsetPx = 12f;
        internal const float BarGapPx = 8f;
        internal const float BarHeightPx = 8f;

        /// <summary>
        /// The bottom edge of the shared bar slot — this bar, and <see cref="FuelGauge"/>'s.
        ///
        /// <b>Named rather than duplicated</b> because it is the one number the two components must
        /// agree on and neither owns: they are mutually exclusive by mode, so a drift between them
        /// would never show up as an overlap, only as the bar quietly moving when you got into a car.
        /// </summary>
        internal const float BarSlotBottomPx = RadarInsetPx + RadarSizePx + BarGapPx;

        /// <summary>Below this the bar goes red, matching `hud-driver.ts`'s own threshold.</summary>
        private const float LowFraction = 0.25f;

        [Header("Scene — found automatically when left empty")]
        [SerializeField] private PlayerController player;
        [SerializeField] private VehicleEnterExit vehicles;

        [Tooltip("Show the km/h readout on foot as well as in a vehicle. The web build shows it only " +
                 "while driving; this is the port's own addition, because a character with no meter " +
                 "at all was the thing that read as missing. Off matches the web exactly.")]
        [SerializeField] private bool showSpeedOnFoot = true;

        [Tooltip("Hide the sprint bar while it is full and nothing is draining it. Off — the web " +
                 "build keeps it on screen the whole time you are on foot, and a meter that vanishes " +
                 "when healthy is a meter you cannot find when you need it.")]
        [SerializeField] private bool hideFullBar;

        private Label _speed;
        private VisualElement _barTrack;
        private VisualElement _barFill;

        /// <summary>Last drawn integer km/h, so a readout that has not changed is not rewritten.</summary>
        private int _lastKmh = int.MinValue;

        /// <summary>
        /// Nullable on purpose. A plain <c>bool</c> starts false, which is also what <c>low</c> is on
        /// a full bar — so the edge check below never fired on the first frame and the fill was left
        /// at its default TRANSPARENT. Measured: the bar drew as an empty track over a healthy
        /// player. Every "only write on a change" cache needs a value the first comparison cannot
        /// match.
        /// </summary>
        private bool? _lastLow;

        private float _lastFill = -1f;

        private void Start()
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (vehicles == null) vehicles = FindAnyObjectByType<VehicleEnterExit>();
            Build();
        }

        private void Build()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;

            _speed = new Label("0 km/h") { name = "speed" };
            _speed.style.position = Position.Absolute;
            _speed.style.bottom = RadarInsetPx;
            _speed.style.right = 24f;
            _speed.style.fontSize = 18f;
            _speed.style.unityFontStyleAndWeight = FontStyle.Bold;
            _speed.style.unityTextAlign = TextAnchor.MiddleRight;
            _speed.style.color = Color.white;
            _speed.style.paddingLeft = 10f;
            _speed.style.paddingRight = 10f;
            _speed.style.paddingTop = 6f;
            _speed.style.paddingBottom = 6f;
            _speed.style.backgroundColor = MenuStyle.Ui(0f, 0f, 0f, 0.45f);
            Round(_speed, 6f);
            _speed.style.display = DisplayStyle.None;
            root.Add(_speed);

            _barTrack = new VisualElement { name = "stamina" };
            _barTrack.style.position = Position.Absolute;
            _barTrack.style.left = RadarInsetPx;
            _barTrack.style.bottom = BarSlotBottomPx;
            _barTrack.style.width = RadarSizePx;
            _barTrack.style.height = BarHeightPx;
            _barTrack.style.backgroundColor = MenuStyle.Ui(0f, 0f, 0f, 0.45f);
            _barTrack.style.overflow = Overflow.Hidden;
            Round(_barTrack, BarHeightPx * 0.5f);
            _barTrack.style.display = DisplayStyle.None;
            root.Add(_barTrack);

            // Width, not scale: UI Toolkit has no transform-origin on a percentage scale, so a
            // scaleX would shrink the fill about its centre and leave a gap at both ends.
            _barFill = new VisualElement { name = "stamina-fill" };
            _barFill.style.height = Length.Percent(100f);
            _barFill.style.width = Length.Percent(100f);
            Round(_barFill, BarHeightPx * 0.5f);
            _barTrack.Add(_barFill);
        }

        private void Update()
        {
            if (_speed == null || Pause.Frozen) return;

            var driving = vehicles != null && vehicles.Mode != GameMode.OnFoot;
            var vehicle = driving ? vehicles.ActiveVehicle : null;

            DrawSpeed(driving, vehicle);
            DrawStamina(onFoot: !driving);
        }

        private void DrawSpeed(bool driving, IEnterable vehicle)
        {
            float? kmh = null;

            // `as`-cast interfaces do not carry Unity's fake-null through, so the anchor is what is
            // tested rather than the interface reference itself.
            if (driving && vehicle != null && vehicle.Anchor != null) kmh = vehicle.SpeedKmh;
            else if (!driving && showSpeedOnFoot && player != null) kmh = player.PlanarSpeed * 3.6f;

            if (kmh == null)
            {
                _speed.style.display = DisplayStyle.None;
                return;
            }

            _speed.style.display = DisplayStyle.Flex;

            var rounded = Mathf.RoundToInt(kmh.Value);
            if (rounded == _lastKmh) return;
            _lastKmh = rounded;
            _speed.text = rounded + " km/h";
        }

        private void DrawStamina(bool onFoot)
        {
            if (!onFoot || player == null)
            {
                _barTrack.style.display = DisplayStyle.None;
                return;
            }

            var fraction = Mathf.Clamp01(player.StaminaFraction);
            if (hideFullBar && fraction >= 0.999f && !player.Exhausted)
            {
                _barTrack.style.display = DisplayStyle.None;
                return;
            }

            _barTrack.style.display = DisplayStyle.Flex;

            // Red once it is spent or nearly spent — the same two conditions hud-driver.ts uses, so
            // the warning arrives at the same moment in both builds.
            var low = player.Exhausted || fraction < LowFraction;

            if (!Mathf.Approximately(fraction, _lastFill))
            {
                _lastFill = fraction;
                _barFill.style.width = Length.Percent(fraction * 100f);
            }

            if (low == _lastLow) return;
            _lastLow = low;
            _barFill.style.backgroundColor = low
                ? MenuStyle.Ui(0.82f, 0.29f, 0.29f)
                : MenuStyle.Ui(0.29f, 0.82f, 0.48f);
        }

        private static void Round(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }
    }
}
