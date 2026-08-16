using TheBlock.Core;
using TheBlock.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheBlock.UI
{
    /// <summary>
    /// The live top-down camera behind the map — Unity's replacement for the web build's one-shot
    /// bake (<c>map-bake.ts</c>).
    ///
    /// The bake existed because three.js could not afford a second live camera: rendering the whole
    /// world once at boot compiled every shader in the same frame, which is why touch skipped it
    /// outright. Unity renders a second camera like any other, so the base layer is simply this
    /// camera into a RenderTexture — always current, no readback, no boot spike. It stays disabled
    /// and is rendered manually by <see cref="GameMap"/>, which throttles the collapsed minimap to
    /// ~12 fps the same way the web build throttles its canvas repaints.
    ///
    /// Orientation: rotation (90, 180, 0) looks straight down with screen-up = world −Z and
    /// screen-right = world −X. That is exactly the web map's frame (its +x is Unity's −x), so the
    /// sea sits on the left and north is up, matching the original without any UV flipping.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class MapCamera : MonoBehaviour
    {
        /// <summary>How high above the world the camera sits. Anything taller is clipped.</summary>
        private const float Height = 250f;

        private Camera _camera;
        private RenderTexture _target;

        public RenderTexture Target => _target;

        /// <summary>Half-height of the framed world rectangle, in metres.</summary>
        public float OrthoSize => _camera != null ? _camera.orthographicSize : 1f;

        /// <summary>World XZ centre of the framed rectangle.</summary>
        public Vector2 Center => new Vector2(transform.position.x, transform.position.z);

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _camera.orthographic = true;
            _camera.transform.rotation = Quaternion.Euler(90f, 180f, 0f);
            _camera.nearClipPlane = 1f;
            _camera.farClipPlane = Height + 50f; // down to y −50, below the seabed
            _camera.enabled = false; // rendered manually — GameMap owns the cadence
            _camera.allowMSAA = false;

            // 24-bit depth, not 16: Metal reports "Ignoring depth surface load action as it is
            // memoryless" for a 16-bit depth target here, once per session, as an ERROR.
            _target = new RenderTexture(1024, 1024, 24)
            {
                name = "MapCameraRT",
                filterMode = FilterMode.Bilinear,
            };
            _camera.targetTexture = _target;

            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }

        private void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            if (_target != null) _target.Release();
        }

        /// <summary>Frame a square window of half-extent <paramref name="rangeM"/> around a point.</summary>
        public void FrameAround(Vector3 worldCenter, float rangeM)
        {
            transform.position = new Vector3(worldCenter.x, Height, worldCenter.z);
            _camera.orthographicSize = rangeM;
            _camera.aspect = 1f;
        }

        /// <summary>
        /// Frame the whole world: the registry's bounds pulled east (Unity +X) to the coastal band so
        /// the sea strip is on-canvas — the same framing as the web full map. The window stays square
        /// because the RT and the panel element are square.
        /// </summary>
        public void FrameWorld(TheBlockConfig.Root config)
        {
            if (!MapRegistry.TryWorldBounds(out var bounds))
            {
                FrameAround(Vector3.zero, 300f);
                return;
            }

            var minX = bounds.min.x;
            var maxX = bounds.max.x;
            if (config?.Sea != null && config.Map != null)
            {
                // Web: minX = min(minX, shoreX − seaBandM). Unity's shore is at −shoreX and the sea
                // runs to +x, so the pulled edge is the MAX side.
                maxX = Mathf.Max(maxX, -config.Sea.ShoreX + config.Map.SeaBandM);
            }

            var center = new Vector3((minX + maxX) / 2f, 0f, bounds.center.z);
            var span = Mathf.Max(maxX - minX, bounds.size.z, 1f);
            FrameAround(center, span / 2f * 1.05f); // 5% ≈ the web's `padding` px inset
        }

        /// <summary>Render one frame into the RT.</summary>
        public void RenderNow() => _camera.Render();

        // The scene's fog is tuned for a street-level camera; from 250 m up it greys out the whole
        // city. Off for this camera's render only, restored immediately after.
        //
        // U33 added the same treatment for ambient. This camera renders with m_RenderShadows off and
        // no key light of its own, so once the sun sets the map goes as black as the city does — and
        // a minimap you cannot read at 02:00 is a broken minimap, not atmosphere. It is lit at a flat
        // daylight level whatever the hour.
        //
        // Save-and-restore is safe HERE, unlike in Interior, and the difference is worth naming: this
        // pair brackets one camera's render inside a single frame, and nothing else runs between the
        // two callbacks. There is no second writer to race.
        private bool _fogWas;
        private AmbientMode _ambientModeWas;
        private Color _ambientSkyWas, _ambientEquatorWas, _ambientGroundWas;

        private static readonly Color MapSky = new(0.42f, 0.45f, 0.50f, 1f);
        private static readonly Color MapEquator = new(0.34f, 0.35f, 0.37f, 1f);
        private static readonly Color MapGround = new(0.20f, 0.19f, 0.17f, 1f);

        private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != _camera) return;

            _fogWas = RenderSettings.fog;
            RenderSettings.fog = false;

            if (!DayNightCycle.Running) return;

            _ambientModeWas = RenderSettings.ambientMode;
            _ambientSkyWas = RenderSettings.ambientSkyColor;
            _ambientEquatorWas = RenderSettings.ambientEquatorColor;
            _ambientGroundWas = RenderSettings.ambientGroundColor;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = MapSky;
            RenderSettings.ambientEquatorColor = MapEquator;
            RenderSettings.ambientGroundColor = MapGround;
        }

        private void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != _camera) return;

            RenderSettings.fog = _fogWas;

            if (!DayNightCycle.Running) return;

            RenderSettings.ambientMode = _ambientModeWas;
            RenderSettings.ambientSkyColor = _ambientSkyWas;
            RenderSettings.ambientEquatorColor = _ambientEquatorWas;
            RenderSettings.ambientGroundColor = _ambientGroundWas;
        }
    }
}
