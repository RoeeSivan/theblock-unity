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
    /// </summary>
    public class CharacterPreview : MonoBehaviour
    {
        [Header("Built by The Block → Build Menus")]
        [SerializeField] private Camera previewCamera;
        [SerializeField] private Transform turntable;

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
        }

        private void LateUpdate()
        {
            if (turntable == null || !turntable.gameObject.activeInHierarchy) return;
            turntable.Rotate(0f, spinDegreesPerSecond * Time.unscaledDeltaTime, 0f, Space.Self);
        }

        /// <summary>Editor wiring, so the builder does not need SerializedObject for three fields.</summary>
        public void Bind(Camera camera, Transform rigTurntable)
        {
            previewCamera = camera;
            turntable = rigTurntable;
        }
    }
}
