using System.Collections.Generic;
using TheBlock.Core;
using TheBlock.Player;
using TheBlock.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace TheBlock.UI
{
    /// <summary>
    /// The minimap and the full map — the port of <c>src/ui/map.ts</c>, drawn with UI Toolkit
    /// instead of a 2D canvas (the U25 decision, arriving early because the map IS ui).
    ///
    /// Structure is the web build's: a small always-on minimap centred on the active entity, and
    /// <c>M</c> (or a click) expands it into a whole-world map with district and POI labels. The
    /// base layer is <see cref="MapCamera"/>'s live render where the web build had a boot-time bake;
    /// the vector overlay — district outlines, POI dots, the player arrow — is painted by
    /// <see cref="MapView"/> on top, and labels are pooled <see cref="Label"/> elements because
    /// Painter2D draws shapes, not text.
    ///
    /// Ports that became unnecessary and are absent on purpose:
    /// <list type="bullet">
    /// <item><c>drawSea</c> — the blue rectangle stood in for water the bake did not show; the live
    /// camera sees the real water shader, so painting over it would hide the actual sea.</item>
    /// <item><c>districtFill</c> — filled the boxes only when the bake was missing; the live render
    /// is never missing.</item>
    /// <item>The dev zone-paint / road-draw tools — authoring tools for config.ts, which is
    /// authored in the original repo, not here.</item>
    /// </list>
    /// The cop-blip overlay (<c>drawCops</c>) belongs to U19 with the pursuit itself, and the rival
    /// arrow (<c>map-rival.ts</c>) to U32 with multiplayer.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class GameMap : MonoBehaviour
    {
        /// <summary>
        /// Repaint + re-render at ~12 fps, the web build's minimap cap. The RT keeps its last frame
        /// between renders, so a skipped frame holds the image — no flicker, and a second full-world
        /// camera pass every frame is exactly the cost worth not paying.
        ///
        /// The web build exempts its OPEN map from the cap so panning stays responsive; there is
        /// nothing to pan here (the open map is fixed on the whole world), so both states share it.
        /// </summary>
        private const float RedrawSec = 1f / 12f;

        /// <summary>
        /// Collapsed minimap edge and its inset from the screen corner, in panel px — the web
        /// build's `#map { width/height: 200px; bottom/left: 12px }` verbatim. The panel is
        /// ConstantPixelSize, so a panel px IS a screen px and the two builds match 1:1.
        /// </summary>
        private const float MiniSizePx = 200f;
        private const float MiniInsetPx = 12f;

        [SerializeField] private MapCamera mapCamera;
        [SerializeField] private PlayerController player;
        [SerializeField] private VehicleEnterExit vehicles;

        [Tooltip("Show the always-on minimap in the bottom-left corner. ON — the web build has it, " +
                 "and the port matches it (turned off 2026-08-16, restored the same day by the user: " +
                 "the radar belongs on screen). This doubles as the Settings → Display 'Radar' " +
                 "toggle U26 already owes, arriving early as a serialized field — U26 gives it a " +
                 "menu, it does not build the mechanism again.")]
        [SerializeField] private bool showMinimap = true;

        private TheBlockConfig.Root _config;
        private VisualElement _backdrop;
        private VisualElement _panel;
        private Image _base;
        private MapView _view;

        private bool _expanded;
        private bool _suppressed;
        private float _lastMiniDraw;

        public bool IsExpanded => _expanded;

        /// <summary>
        /// Borrows the screen back from the radar for a scene that owns the whole bottom edge. The
        /// dance's arrow lane runs full-width at 60 px up, and the 200 px corner widget sits square
        /// on top of the left-hand arrows — the player cannot see the note they are being asked to
        /// hit.
        ///
        /// <b>Deliberately not <see cref="SetMinimapVisible"/>.</b> That field is the player's own
        /// Radar preference (U26's Settings → Display, arriving early); a mission that wrote to it
        /// would hand it back turned ON to someone who had turned it off. This is a separate,
        /// temporary override that leaves the preference untouched, and whoever sets it owns
        /// clearing it on EVERY exit path — win, fail and retry alike.
        /// </summary>
        public bool Suppressed
        {
            get => _suppressed;
            set
            {
                if (_suppressed == value) return;
                _suppressed = value;

                // A suppressed map cannot stay open: the dance freezes the player, so an expanded
                // map over the routine is a screen nobody can dismiss by moving.
                SetExpanded(_expanded && !value);
            }
        }

        private void Start()
        {
            _config = TheBlockConfig.Load()?.Config;
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (vehicles == null) vehicles = FindAnyObjectByType<VehicleEnterExit>();
            if (mapCamera == null) mapCamera = FindAnyObjectByType<MapCamera>();

            BuildUi();
            SetExpanded(false);
        }

        private void BuildUi()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            root.style.position = Position.Absolute;
            root.style.top = 0; root.style.bottom = 0; root.style.left = 0; root.style.right = 0;
            root.pickingMode = PickingMode.Ignore;

            _backdrop = new VisualElement { name = "map-backdrop" };
            _backdrop.style.position = Position.Absolute;
            _backdrop.style.top = 0; _backdrop.style.bottom = 0;
            _backdrop.style.left = 0; _backdrop.style.right = 0;
            _backdrop.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            _backdrop.RegisterCallback<ClickEvent>(_ => SetExpanded(false));
            root.Add(_backdrop);

            _panel = new VisualElement { name = "map" };
            _panel.style.position = Position.Absolute;
            _panel.style.borderTopWidth = _panel.style.borderBottomWidth = 2f;
            _panel.style.borderLeftWidth = _panel.style.borderRightWidth = 2f;
            // The web's `#map` rim and backing: 2px rgba(255,255,255,0.25) over rgba(0,0,0,0.35).
            // The backing shows only in the corners the rounded rim cuts off the render texture.
            var rim = new Color(1f, 1f, 1f, 0.25f);
            _panel.style.borderTopColor = _panel.style.borderBottomColor = rim;
            _panel.style.borderLeftColor = _panel.style.borderRightColor = rim;
            _panel.style.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
            _panel.style.overflow = Overflow.Hidden;
            _panel.RegisterCallback<ClickEvent>(_ => SetExpanded(!_expanded));
            root.Add(_panel);

            _base = new Image { scaleMode = ScaleMode.StretchToFill };
            _base.style.position = Position.Absolute;
            _base.style.top = 0; _base.style.bottom = 0; _base.style.left = 0; _base.style.right = 0;
            if (mapCamera != null) _base.image = mapCamera.Target;
            _panel.Add(_base);

            _view = new MapView(_config?.Map);
            _view.style.position = Position.Absolute;
            _view.style.top = 0; _view.style.bottom = 0; _view.style.left = 0; _view.style.right = 0;
            _panel.Add(_view);
        }

        /// <summary>The radar is off: nothing is drawn while the map is closed.</summary>
        public bool Hidden => _suppressed || (!_expanded && !showMinimap);

        /// <summary>Turns the corner radar on and off. U26's Settings → Display will call this.</summary>
        public void SetMinimapVisible(bool visible)
        {
            showMinimap = visible;
            SetExpanded(_expanded);
        }

        public void SetExpanded(bool next)
        {
            // Suppression can be set before Start has built the panel — a mission that starts on
            // frame one would otherwise NRE here rather than in anything it can see.
            if (_panel == null) { _expanded = next; return; }

            _expanded = next;
            _backdrop.style.display = next ? DisplayStyle.Flex : DisplayStyle.None;
            _lastMiniDraw = 0f; // repaint immediately on either toggle direction

            // With the radar off the closed state draws nothing at all. Hiding the PANEL rather
            // than shrinking it is what makes the saving real: an invisible element still has the
            // map's RenderTexture bound to it, and the camera pass behind that is the actual cost.
            _panel.style.display = !_suppressed && (next || showMinimap)
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            // The web build rounds the open map harder than the corner widget: 10px against 6px.
            var radius = next ? 10f : 6f;
            _panel.style.borderTopLeftRadius = _panel.style.borderTopRightRadius = radius;
            _panel.style.borderBottomLeftRadius = _panel.style.borderBottomRightRadius = radius;

            if (next)
            {
                // Sized every LateUpdate against the live panel size; just switch anchoring here.
                _panel.style.left = StyleKeyword.Auto;
                _panel.style.bottom = StyleKeyword.Auto;
            }
            else
            {
                _panel.style.left = MiniInsetPx;
                _panel.style.bottom = MiniInsetPx;
                _panel.style.top = StyleKeyword.Auto;
                _panel.style.right = StyleKeyword.Auto;
                _panel.style.width = MiniSizePx;
                _panel.style.height = MiniSizePx;
            }
        }

        private void LateUpdate()
        {
            if (mapCamera == null || player == null) return;

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.mKey.wasPressedThisFrame && !_suppressed)
                SetExpanded(!_expanded);

            var active = ActiveAnchor();
            if (active == null) return;

            // Radar off and map closed: there is nothing on screen to keep up to date, so the whole
            // second-camera pass is skipped rather than rendered into a texture nobody is looking
            // at. U14's own note called the live camera the map's main cost; this is not drawing it.
            if (Hidden) return;

            if (Time.unscaledTime - _lastMiniDraw < RedrawSec) return;
            _lastMiniDraw = Time.unscaledTime;

            if (_expanded)
            {
                SizeExpandedPanel();
                mapCamera.FrameWorld(_config);
            }
            else
            {
                mapCamera.FrameAround(active.position, _config?.Map?.MinimapRangeM ?? 150f);
            }

            mapCamera.RenderNow();
            _view.SetFrame(mapCamera.Center, mapCamera.OrthoSize, active, _expanded);
        }

        /// <summary>
        /// The transform the map centres on: the vehicle while driving (and during the enter/exit
        /// swings, when the camera is already on it), the player on foot.
        /// </summary>
        private Transform ActiveAnchor()
        {
            var vehicle = vehicles != null ? vehicles.ActiveVehicle : null;
            if (vehicle != null && vehicles.Mode != GameMode.OnFoot) return vehicle.Anchor;
            return player.transform;
        }

        private void SizeExpandedPanel()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            var w = root.resolvedStyle.width;
            var h = root.resolvedStyle.height;
            if (float.IsNaN(w) || w <= 0f) return;

            // Square, 82% of the shorter screen edge — what the web css's min(82vw, 82vh) resolves to.
            var size = Mathf.Min(w, h) * 0.82f;
            _panel.style.width = size;
            _panel.style.height = size;
            _panel.style.left = (w - size) / 2f;
            _panel.style.top = (h - size) / 2f;
        }
    }
}
