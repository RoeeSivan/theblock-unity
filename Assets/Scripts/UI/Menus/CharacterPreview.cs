using UnityEngine;

namespace TheBlock.UI.Menus
{
    /// <summary>
    /// The slowly rotating body on the Character screen — the port of the render half of
    /// <c>src/ui/character-select.ts</c>.
    ///
    /// <b>The web spends four paragraphs on this and Unity deletes all of them.</b> That file needs
    /// a SECOND <c>WebGLRenderer</c>, and its comments are an argument about whether the browser
    /// will evict the game's own context under memory pressure — "this is exactly the assumption
    /// that did not hold on an iPad". Here it is a second Camera into a RenderTexture, which is the
    /// ordinary way to do it and has no such failure mode.
    ///
    /// <b>The rig stands at y = −2000, and that is the culling.</b> No preview layer, no TagManager
    /// edit: the camera's 20 m far plane simply has nothing else in range, because the world is two
    /// kilometres above it. A layer would have been the textbook answer and it would also have been
    /// a project-settings change every future scene inherits.
    ///
    /// <b>Everything here runs on unscaled time</b> — the menu is on screen precisely when
    /// <c>Time.timeScale</c> is 0, so the spin and the idle clip both have to be told to ignore it.
    /// The Animator's <c>UpdateMode.UnscaledTime</c> is set by the builder; the spin is below.
    ///
    /// <b>It has its own three-light studio rig, and U26 shipped without one.</b> The web build's
    /// preview scene adds a hemisphere, a warm key and a cool rim before it adds the body; U26
    /// ported the camera and the turntable and left the lighting to whatever the world's sun
    /// happened to be doing two kilometres overhead — which is a body lit from the wrong side, in a
    /// dark box, with no fill at all. The rig below is that block, rebuilt with the one constraint
    /// Unity adds: <b>a directional light has no position and would light the whole city</b>
    /// (memory: <c>urp-has-one-main-directional</c>), so the key and the rim are range-limited
    /// POINT lights and the hemisphere — which is a global ambient here, not something a scene can
    /// have two of — becomes a third soft fill from the front.
    ///
    /// The intensities are serialized rather than baked into the builder because this is the one
    /// thing in the unit that is pure taste: they are meant to be dragged in the Inspector while
    /// looking at the screen.
    /// </summary>
    public class CharacterPreview : MonoBehaviour
    {
        [Header("Built by The Block → Build Menus")]
        [SerializeField] private Camera previewCamera;
        [SerializeField] private Transform turntable;

        [Tooltip("The studio rig. Switched with the camera — three lights nobody is looking at are " +
                 "three lights being culled and shaded for nothing.")]
        [SerializeField] private Transform lights;

        // The three numbers were measured, not guessed. Rendering the rig at 0×, 1×, 2×, 4×, 8× and
        // 16× and averaging the luminance of the BODY pixels only (the background is most of the
        // frame and swamps a whole-image mean) gives 0.154 unlit — what U26 shipped — then 0.218,
        // 0.262, 0.326, 0.411, 0.521. The brightest pixel hits 1.000 at 4×, so anything from there
        // up is buying mean brightness by blowing the specular highlights out. 2× is the last stop
        // before the clip: 0.959 peak, and 70% brighter than the screen the user called too dark.
        [Header("Lighting — tune these while the screen is open")]
        [Tooltip("Warm key from the front-upper-right. The web's DirectionalLight(0xffd7a8, 2.6).")]
        [SerializeField] private float keyIntensity = 24f;

        [Tooltip("Cool rim from behind-left, which is what separates a dark body from a dark box. " +
                 "The web's DirectionalLight(0x88bbff, 1.4).")]
        [SerializeField] private float rimIntensity = 14f;

        [Tooltip("Soft frontal fill standing in for the web's HemisphereLight(0xffffff, 0x333344, " +
                 "2.2) — URP ambient is global, so a preview cannot have one of its own.")]
        [SerializeField] private float fillIntensity = 10f;

        [Tooltip("Radians per second in the web build (0.6). Degrees here.")]
        [SerializeField] private float spinDegreesPerSecond = 34f;

        [Tooltip("The web's #character-preview is 300 x 360 CSS px. This is that, at 1 device pixel.")]
        [SerializeField] private Vector2Int resolution = new(512, 640);

        private RenderTexture _target;

        /// <summary>What the panel binds its Image to. Null until Awake has run.</summary>
        public RenderTexture Target => _target;

        private void Awake()
        {
            if (previewCamera == null) return;

            // 24-bit depth, not 16: Metal logs "Ignoring depth surface load action as it is
            // memoryless" as an ERROR for a 16-bit depth target, once per session, with nothing to
            // trace it to. U14's map camera learned the same thing.
            _target = new RenderTexture(Mathf.Max(64, resolution.x), Mathf.Max(64, resolution.y), 24)
            {
                name = "Character Preview",
                antiAliasing = 2,
            };

            previewCamera.targetTexture = _target;
            SetActive(false);
        }

        private void OnDestroy()
        {
            if (_target == null) return;
            if (previewCamera != null) previewCamera.targetTexture = null;
            _target.Release();
            Destroy(_target);
        }

        /// <summary>
        /// The camera and the rig are both switched, not just the camera. A skinned mesh under a
        /// live Animator is posed every frame whether anything draws it or not (memory:
        /// lodgroup-does-not-stop-skinning) — and this one is posed on UNSCALED time, so it would
        /// keep costing that during the very freeze the menu exists to hold.
        /// </summary>
        public void SetActive(bool active)
        {
            if (previewCamera != null) previewCamera.enabled = active;
            if (turntable != null) turntable.gameObject.SetActive(active);

            if (lights == null) return;
            lights.gameObject.SetActive(active);
            if (active) ApplyIntensities();
        }

        /// <summary>
        /// Pushes the three serialized intensities onto the rig. Called on every open so a value
        /// dragged in the Inspector takes effect on the next open rather than on the next build —
        /// which is the whole reason they are fields and not constants in the builder.
        /// </summary>
        private void ApplyIntensities()
        {
            var rig = lights.GetComponentsInChildren<Light>(true);
            foreach (var light in rig)
            {
                if (light.name.StartsWith("Key")) light.intensity = keyIntensity;
                else if (light.name.StartsWith("Rim")) light.intensity = rimIntensity;
                else if (light.name.StartsWith("Fill")) light.intensity = fillIntensity;
            }
        }

        private void LateUpdate()
        {
            if (turntable == null || !turntable.gameObject.activeInHierarchy) return;
            turntable.Rotate(0f, spinDegreesPerSecond * Time.unscaledDeltaTime, 0f, Space.Self);
        }

        /// <summary>Editor wiring, so the builder does not need SerializedObject for three fields.</summary>
        public void Bind(Camera camera, Transform rigTurntable, Transform rigLights = null)
        {
            previewCamera = camera;
            turntable = rigTurntable;
            if (rigLights != null) lights = rigLights;
        }
    }
}
