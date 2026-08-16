using TheBlock.UI.Menus;
using TheBlock.Vehicles;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheBlock.UI
{
    /// <summary>
    /// The fuel bar - <c>hud.ts</c>'s <c>setFuel</c>, and the last of that file's surfaces to be
    /// built.
    ///
    /// <b>It takes <see cref="PlayerMeters"/>'s slot, and shares its constants.</b> The sprint bar is
    /// on foot only and this is driving only, so exactly one of them is ever on screen and "the bar
    /// above the radar is your meter" stays true in both modes. That is the web build's own
    /// arrangement, stated in a comment in <c>hud.css</c> - not a coincidence to be preserved by
    /// hand, which is why the position is <see cref="PlayerMeters.BarSlotBottomPx"/> rather than a
    /// second 220.
    ///
    /// <b>A separate component rather than a branch inside <see cref="PlayerMeters"/></b>: one
    /// concern per file, and this one has a pump, a pulse and three colours to think about while the
    /// sprint bar has none of them.
    ///
    /// <b>Hidden with <c>display</c>, never <c>visibility</c>.</b> <see cref="Menus.GameFlow"/> owns
    /// <c>visibility</c> for taking the whole gameplay HUD off behind a menu, and a second writer of
    /// that property is what silently broke the radar toggle once. The element's name is also kept
    /// out of that component's menu list, or it would stop being hidden behind menus at all.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class FuelGauge : MonoBehaviour
    {
        /// <summary>
        /// How far the ⛽ hangs off the left of the track. The web's own number, and it puts the
        /// glyph flush with the radar's left edge: 34 − 22 = 12, which is <c>RadarInsetPx</c>.
        /// </summary>
        private const float GlyphOffsetPx = 22f;

        /// <summary>Seconds per pulse while fuel is flowing - <c>hud.css</c>'s <c>fuel-pump</c>.</summary>
        private const float PulseSeconds = 0.7f;

        /// <summary>The dimmest the bar goes at the bottom of a pulse.</summary>
        private const float PulseFloor = 0.55f;

        [Header("Scene - found automatically when left empty")]
        [SerializeField] private FuelSystem fuel;

        private VisualElement _track;
        private VisualElement _fill;

        private float _lastFraction = -1f;

        /// <summary>
        /// Nullable for the reason <see cref="PlayerMeters"/>'s is: a plain <c>bool</c> starts false,
        /// which is also what "not low" is, so the first comparison would match and the fill would
        /// never get its colour at all.
        /// </summary>
        private bool? _lastLow;

        private bool? _lastFull;
        private bool _lastFilling;

        private void Start()
        {
            if (fuel == null) fuel = FindAnyObjectByType<FuelSystem>();
            Build();
        }

        private void Build()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;

            _track = new VisualElement { name = "fuel" };
            _track.style.position = Position.Absolute;
            _track.style.left = PlayerMeters.RadarInsetPx + GlyphOffsetPx;
            _track.style.bottom = PlayerMeters.BarSlotBottomPx;
            _track.style.width = PlayerMeters.RadarSizePx;
            _track.style.height = PlayerMeters.BarHeightPx;
            _track.style.backgroundColor = MenuStyle.Ui(0f, 0f, 0f, 0.45f);
            _track.pickingMode = PickingMode.Ignore;
            Round(_track, PlayerMeters.BarHeightPx * 0.5f);
            _track.style.display = DisplayStyle.None;
            root.Add(_track);

            // No `overflow: Hidden` here, unlike the sprint bar - the ⛽ is a CHILD hanging off the
            // left edge, and clipping the track would clip the glyph away with it.
            var glyph = new Label(Glyphs.Opaque("⛽")) { name = "fuel-glyph" };
            glyph.style.position = Position.Absolute;
            glyph.style.left = -GlyphOffsetPx;
            glyph.style.top = -6f;
            glyph.style.fontSize = 14f;
            glyph.pickingMode = PickingMode.Ignore;
            _track.Add(glyph);

            // Width, not scale: UI Toolkit has no transform-origin on a percentage scale, so a
            // scaleX would shrink the fill about its centre and leave a gap at both ends.
            _fill = new VisualElement { name = "fuel-fill" };
            _fill.style.height = Length.Percent(100f);
            _fill.style.width = Length.Percent(100f);
            Round(_fill, PlayerMeters.BarHeightPx * 0.5f);
            _track.Add(_fill);
        }

        private void Update()
        {
            if (_track == null) return;

            var tank = fuel != null ? fuel.ActiveTank : null;
            if (tank == null)
            {
                if (_track.style.display != DisplayStyle.None) _track.style.display = DisplayStyle.None;
                return;
            }

            _track.style.display = DisplayStyle.Flex;

            var fraction = Mathf.Clamp01(tank.Fraction);
            var full = fraction >= 0.999f;
            var low = !full && tank.IsLow;
            var filling = fuel.Filling;

            if (!Mathf.Approximately(fraction, _lastFraction))
            {
                _lastFraction = fraction;
                _fill.style.width = Length.Percent(fraction * 100f);
            }

            // Colours are flat and take each of the web's gradients by its LEFT stop, which is
            // already this project's convention - the sprint bar's two are literally these two.
            // UI Toolkit has no linear-gradient on a background colour, and through MenuStyle.Ui
            // because it takes a Color as LINEAR while these are written as sRGB.
            if (low != _lastLow || full != _lastFull)
            {
                _lastLow = low;
                _lastFull = full;
                _fill.style.backgroundColor = full
                    ? MenuStyle.Ui(0.29f, 0.82f, 0.48f)   // #4ad17a - green at 100% IS the refuel confirm
                    : low
                        ? MenuStyle.Ui(0.82f, 0.29f, 0.29f)   // #d14a4a - the sprint bar's own low red
                        : MenuStyle.Ui(0.878f, 0.631f, 0.247f); // #e0a13f
            }

            // The pump pulse. UI Toolkit has no CSS keyframes, so it is driven here - a cosine IS
            // ease-in-out, and on unscaled time so it keeps breathing if anything ever slows the
            // clock. Written only while filling, plus the one frame that turns it off.
            if (filling)
            {
                var t = 0.5f - 0.5f * Mathf.Cos(Time.unscaledTime * (2f * Mathf.PI / PulseSeconds));
                _fill.style.opacity = Mathf.Lerp(1f, PulseFloor, t);
            }
            else if (_lastFilling)
            {
                _fill.style.opacity = 1f;
            }

            _lastFilling = filling;
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
