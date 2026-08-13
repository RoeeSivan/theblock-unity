using System.Linq;
using UnityEngine;

namespace TheBlock.Player
{
    /// <summary>
    /// Drives Joe's Animator from <see cref="PlayerController"/>.
    ///
    /// Kept apart from the controller on purpose: movement decides what the body does, this decides
    /// what it looks like doing it. When the character roster lands (U29) the body underneath
    /// changes and the controller must not care.
    ///
    /// Root motion stays OFF. The controller owns position; the clips only supply pose. The sprint
    /// clip does carry root motion, which the Animator ignores — the builder corrects its playback
    /// rate instead so the cadence matches the speed the controller actually moves at.
    ///
    /// Build the graph this expects with **The Block → Build Joe Animator**.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class PlayerAnimator : MonoBehaviour
    {
        [SerializeField] private PlayerController controller;

        [Tooltip("How fast the blended gait catches up to the real speed, in m/s per second. " +
                 "Without it, tapping Shift snaps the legs from walk to sprint in one frame.")]
        [SerializeField] private float speedBlendRate = 12f;

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int JumpId = Animator.StringToHash("Jump");
        private static readonly int EnterCarId = Animator.StringToHash("EnterCar");

        /// <summary>Clip name the builder gives the entry animation, and the length probe looks for.</summary>
        private const string EnterCarClip = "Joe_EnterCar";

        private Animator _animator;
        private float _blendedSpeed;
        private bool _inCar;

        /// <summary>
        /// How long the entry animation runs, in seconds, or 0 if the clip has not been imported.
        ///
        /// Zero is meaningful, not an error: it is what tells the enter/exit machine to fall back to
        /// the config's quick-enter timings, exactly as the web build does for a car with no seat
        /// block. So the unit works before the FBX lands, and better once it has.
        /// </summary>
        public float EnterCarSeconds { get; private set; }

        private void Awake() => Bind();

        /// <summary>
        /// Caches the Animator and measures the entry clip. Called from Awake, and again from Update
        /// if the Animator has gone null — which is what a script recompile during Play does: the
        /// domain reloads, non-serialized fields clear, and Awake does not run again. The other
        /// components carry the same guard; without it this one throws once a frame forever.
        /// </summary>
        private void Bind()
        {
            _animator = GetComponent<Animator>();
            _animator.applyRootMotion = false;

            // The clip's hip travel is baked into its pose, not extracted as root motion (see
            // JoeClipImporter), so the driver walks himself into the car while his transform stays
            // pinned to the seat anchor. That is the web build's arrangement, where the mixer moves
            // the body inside a driver object that never moves.
            EnterCarSeconds = _animator.runtimeAnimatorController == null ? 0f
                : _animator.runtimeAnimatorController.animationClips
                    .FirstOrDefault(c => c != null && c.name == EnterCarClip)?.length ?? 0f;

            if (controller == null) controller = GetComponent<PlayerController>();
            if (controller == null)
            {
                Debug.LogError("PlayerAnimator: no PlayerController to read.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (controller != null) controller.Jumped += OnJumped;
        }

        private void OnDisable()
        {
            if (controller != null) controller.Jumped -= OnJumped;
        }

        private void Update()
        {
            if (_animator == null) Bind();
            if (_animator == null) return;

            // In the car the controller is switched off, so PlanarSpeed is frozen at whatever Joe
            // was doing when he pressed E. Bleeding the blend to zero instead means he steps out
            // idle rather than mid-stride.
            var wanted = _inCar ? 0f : controller.PlanarSpeed;
            _blendedSpeed = Mathf.MoveTowards(_blendedSpeed, wanted, speedBlendRate * Time.deltaTime);

            _animator.SetFloat(SpeedId, _blendedSpeed);
            _animator.SetBool(GroundedId, !_inCar && controller.IsGrounded);
        }

        private void OnJumped() => _animator.SetTrigger(JumpId);

        /// <summary>
        /// Starts the entry animation. It plays once and holds its last frame — the seated pose —
        /// for as long as the flag stays set, which is the whole drive.
        /// </summary>
        public void SeatIn() => Seat(true);

        /// <summary>Back to the gait blend. Called when stepping out.</summary>
        public void SeatOut() => Seat(false);

        private void Seat(bool inCar)
        {
            // Both of these are called from another component's Update, which may well run before
            // this one's has had a chance to rebind after a recompile.
            if (_animator == null) Bind();
            if (_animator == null) return;

            _inCar = inCar;
            _animator.SetBool(EnterCarId, inCar);
        }
    }
}
