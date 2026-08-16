using System.Linq;
using UnityEngine;

namespace TheBlock.Player
{
    /// <summary>
    /// Drives Joe's Animator from <see cref="PlayerController"/>.
    ///
    /// Kept apart from the controller on purpose: movement decides what the body does, this decides
    /// what it looks like doing it. U29 is what that separation was for: the body underneath is now
    /// a <see cref="CharacterBody"/>'s visual child that a menu can replace mid-game, and the
    /// controller never learned about it.
    ///
    /// <b>The Animator is on the CHILD, not here.</b> Until U29 it sat on the player root beside the
    /// capsule; a roster needs the body to carry a height-match scale, and on the root that scale
    /// would resize the <c>CharacterController</c>. So this resolves through the
    /// <see cref="CharacterBody"/> and re-resolves whenever it swaps.
    ///
    /// Root motion stays OFF. The controller owns position; the clips only supply pose. The sprint
    /// clip does carry root motion, which the Animator ignores - the builder corrects its playback
    /// rate instead so the cadence matches the speed the controller actually moves at.
    ///
    /// Build the graph this expects with **The Block → Build Joe Animator**.
    /// </summary>
    public class PlayerAnimator : MonoBehaviour
    {
        [SerializeField] private PlayerController controller;
        [SerializeField] private CharacterBody body;

        [Tooltip("How fast the blended gait catches up to the real speed, in m/s per second. " +
                 "Without it, tapping Shift snaps the legs from walk to sprint in one frame.")]
        [SerializeField] private float speedBlendRate = 12f;

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int JumpId = Animator.StringToHash("Jump");
        private static readonly int EnterCarId = Animator.StringToHash("EnterCar");
        private static readonly int RideId = Animator.StringToHash("Ride");
        private static readonly int SwimId = Animator.StringToHash("Swim");

        /// <summary>Clip name the builder gives the entry animation, and the length probe looks for.</summary>
        private const string EnterCarClip = "Joe_EnterCar";

        private Animator _animator;
        private float _blendedSpeed;

        /// <summary>Mounted in anything - a car or a bike. Owns the gait blend's freeze.</summary>
        private bool _inCar;

        // The two raw flags behind _inCar, kept apart from it so a body swap can re-push exactly
        // what the old Animator was holding. Without them a character picked mid-drive stands up in
        // the driver's seat, because a fresh Animator starts in its controller's entry state.
        private bool _seatedInCar;
        private bool _riding;

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
        /// if the Animator has gone null - which is what a script recompile during Play does: the
        /// domain reloads, non-serialized fields clear, and Awake does not run again. The other
        /// components carry the same guard; without it this one throws once a frame forever.
        /// </summary>
        private void Bind()
        {
            if (controller == null) controller = GetComponent<PlayerController>();
            if (controller == null)
            {
                Debug.LogError("PlayerAnimator: no PlayerController to read.", this);
                enabled = false;
                return;
            }

            if (body == null) body = GetComponent<CharacterBody>();
            if (body != null && body.Animator == null) body.Rebind();

            _animator = body != null ? body.Animator : GetComponentInChildren<Animator>(true);
            if (_animator == null)
            {
                // Not worth an error: a swap destroys the old body before the new one is parented,
                // and anything polling in that one window sees exactly this. Update rebinds.
                EnterCarSeconds = 0f;
                return;
            }

            _animator.applyRootMotion = false;

            // The clip's hip travel is baked into its pose, not extracted as root motion (see
            // JoeClipImporter), so the driver walks himself into the car while his transform stays
            // pinned to the seat anchor. That is the web build's arrangement, where the mixer moves
            // the body inside a driver object that never moves.
            EnterCarSeconds = _animator.runtimeAnimatorController == null ? 0f
                : _animator.runtimeAnimatorController.animationClips
                    .FirstOrDefault(c => c != null && c.name == EnterCarClip)?.length ?? 0f;
        }

        private void OnEnable()
        {
            if (controller != null) controller.Jumped += OnJumped;
            if (body == null) body = GetComponent<CharacterBody>();
            if (body != null) body.Swapped += OnBodySwapped;
        }

        private void OnDisable()
        {
            if (controller != null) controller.Jumped -= OnJumped;
            if (body != null) body.Swapped -= OnBodySwapped;
        }

        /// <summary>
        /// A new body means a new Animator, and it comes up in whatever state its controller starts
        /// in. The mount flags are re-pushed rather than assumed: swap character while sitting in a
        /// car and the fresh Animator would otherwise stand up in the driver's seat.
        /// </summary>
        private void OnBodySwapped()
        {
            Bind();
            if (_animator == null) return;

            _animator.SetBool(EnterCarId, _seatedInCar);
            _animator.SetBool(RideId, _riding);
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

            // Not while mounted: the jetski (U24) floats in the same water the swim test reads, and
            // its rider must stay in the riding pose rather than start doing the crawl on the seat.
            _animator.SetBool(SwimId, !_inCar && controller.IsSwimming);
        }

        private void OnJumped() => _animator.SetTrigger(JumpId);

        /// <summary>
        /// Starts the entry animation. It plays once and holds its last frame - the seated pose -
        /// for as long as the flag stays set, which is the whole drive.
        /// </summary>
        public void SeatIn() => Seat(inCar: true, riding: false);

        /// <summary>
        /// Straight into the riding pose, with no walk-up. This is the bike: you do not enter a
        /// motorcycle, you sit on it, so there is no clip to play through first - the pose is
        /// simply held for the whole ride.
        /// </summary>
        public void RideOn() => Seat(inCar: false, riding: true);

        /// <summary>Back to the gait blend. Called when stepping out of anything.</summary>
        public void SeatOut() => Seat(inCar: false, riding: false);

        private void Seat(bool inCar, bool riding)
        {
            // Recorded before the guard, not after: these three are what a later swap re-pushes, so
            // losing them because the Animator happened to be mid-rebind would put the new body on
            // its feet inside a moving car.
            _inCar = inCar || riding;
            _seatedInCar = inCar;
            _riding = riding;

            // These are called from another component's Update, which may well run before this
            // one's has had a chance to rebind after a recompile.
            if (_animator == null) Bind();
            if (_animator == null) return;

            _animator.SetBool(EnterCarId, inCar);
            _animator.SetBool(RideId, riding);
        }
    }
}
