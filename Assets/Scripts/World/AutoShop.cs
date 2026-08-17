using TheBlock.Vehicles;
using UnityEngine;

namespace TheBlock.World
{
    /// <summary>
    /// The auto shop - U35g. Pull up in front, the shutter rolls up, and the vehicle you are sitting
    /// in can be repainted.
    ///
    /// <b>The car does not drive in.</b> The user's spec: approach → the shutter opens (a real
    /// animation) → a colour menu → drive off in the new colour. The bay behind the door is
    /// set-dressing seen through it, which is the whole reason it was dressed.
    ///
    /// <b>The shutter opens for anyone</b> - on foot, on the bike, in a cop car - inside
    /// <see cref="AutoShopSpec.OpenRadius"/> of the service point. The paint needs more:
    /// <see cref="CanPaint"/> is <b>the one predicate behind both the prompt and the action</b>, the
    /// same rule the 7-Eleven counter and the fuel pump keep, so a prompt that says "Press C" cannot
    /// appear anywhere C does nothing. <c>GameFlow</c> owns C and the freeze; this component owns
    /// the door, the distance and the prompt.
    ///
    /// The distance is sampled four times a second (the cops' cadence), the shutter lerps only while
    /// it is moving, and the prompt is re-claimed each frame while it applies - <c>MissionHud</c>
    /// arbitrates prompts per frame and forgets them.
    /// </summary>
    public class AutoShop : MonoBehaviour
    {
        [Tooltip("The roll-up door. Its origin is its top edge, so Y scale 1 → 0 rolls it into the housing.")]
        [SerializeField] private Transform shutter;

        [SerializeField] private Player.PlayerController player;
        [SerializeField] private VehicleEnterExit vehicles;
        [SerializeField] private UI.MissionHud hud;

        private const float SampleInterval = 0.25f;

        private float _nextSample;
        private float _distance = float.PositiveInfinity;
        private float _open;          // 0 closed → 1 open
        private float _applied = -1f;

        /// <summary>The shutter's target: is anyone close enough to open it?</summary>
        public bool Near => _distance <= AutoShopSpec.OpenRadius;

        /// <summary>0 closed → 1 open, for anything that wants to watch the door.</summary>
        public float Open => _open;

        private void Awake()
        {
            if (player == null) player = FindAnyObjectByType<Player.PlayerController>();
            if (vehicles == null) vehicles = FindAnyObjectByType<VehicleEnterExit>();
            if (hud == null) hud = FindAnyObjectByType<UI.MissionHud>();
            if (shutter == null) shutter = transform.Find("Shutter");
            if (shutter != null) _closedScaleY = shutter.localScale.y;
        }

        /// <summary>The shutter's authored Y scale - the closed pose is measured, not assumed.</summary>
        private float _closedScaleY = 1f;

        // --- predicates -----------------------------------------------------------------------------

        /// <summary>
        /// The vehicle the player is sitting in, if it can be painted: driving, and the vehicle's
        /// root carries a <see cref="CarPaint"/>. Null on foot, on anything without a paint slot
        /// (the jetski, the helicopters, a cruiser), and mid door swing.
        /// </summary>
        public CarPaint ActivePaint()
        {
            if (vehicles == null || vehicles.Mode != Core.GameMode.Driving) return null;
            var active = vehicles.ActiveVehicle;
            if (active == null || active.Anchor == null) return null;
            var root = active.GetTransform();
            return root != null && root.TryGetComponent<CarPaint>(out var paint) ? paint : null;
        }

        /// <summary>
        /// Can the shop paint right now? In a paintable vehicle, stopped, at the service point.
        /// </summary>
        public bool CanPaint()
        {
            var paint = ActivePaint();
            if (paint == null) return false;
            if (_distance > AutoShopSpec.PaintRadius) return false;
            var active = vehicles.ActiveVehicle;
            return active != null && Mathf.Abs(active.ForwardSpeed) < AutoShopSpec.StoppedSpeed;
        }

        // --- the frame ------------------------------------------------------------------------------

        private void Update()
        {
            if (Core.Pause.Frozen) return;

            if (Time.time >= _nextSample)
            {
                _nextSample = Time.time + SampleInterval;
                var d = FocusPoint() - AutoShopSpec.ServicePoint;
                d.y = 0f;
                _distance = d.magnitude;
            }

            DriveShutter(Time.deltaTime);

            if (_distance > AutoShopSpec.PaintRadius) return;

            // Drawn from the same predicate GameFlow acts on. The second line is not an offer - it
            // says why there is none, in the door's own priority band so a real vehicle prompt wins.
            if (CanPaint()) hud?.SetPrompt("Press C to change the color", UI.MissionHud.PromptVehicle);
            else if (ActivePaint() == null) hud?.SetPrompt("Drive a car or the bike here to paint it", UI.MissionHud.PromptDoor);
        }

        /// <summary>
        /// One point stands for the player whether they are walking or driving, as the 7-Eleven's
        /// does - a car nosing up to the door has to open it too.
        /// </summary>
        private Vector3 FocusPoint()
        {
            if (vehicles != null && vehicles.Mode != Core.GameMode.OnFoot)
            {
                var active = vehicles.ActiveVehicle;
                // `as`-cast interfaces do not carry Unity's fake-null through, so the anchor is
                // checked rather than the interface reference.
                if (active != null && active.Anchor != null) return active.Anchor.position;
            }

            return player != null ? player.transform.position : transform.position;
        }

        /// <summary>
        /// The whole animation is one lerp of the shutter's Y scale, because the mesh was exported
        /// with its origin on its top edge. The MeshCollider on the same transform follows the scale,
        /// so an open door is also a passable one.
        /// </summary>
        private void DriveShutter(float dt)
        {
            if (shutter == null) return;

            float target = Near ? 1f : 0f;
            _open = Mathf.MoveTowards(_open, target, dt / AutoShopSpec.ShutterSeconds);
            if (Mathf.Approximately(_open, _applied)) return;
            _applied = _open;

            float eased = Mathf.SmoothStep(0f, 1f, _open);
            float y = Mathf.Lerp(_closedScaleY, _closedScaleY * AutoShopSpec.ShutterOpenScale, eased);
            var scale = shutter.localScale;
            scale.y = y;
            shutter.localScale = scale;
        }
    }
}
