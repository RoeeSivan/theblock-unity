using TheBlock.Core;
using TheBlock.Game;
using TheBlock.Npc;
using TheBlock.Vehicles;
using UnityEngine;

namespace TheBlock.Player
{
    /// <summary>
    /// The player comes off the bike, or off a roof - U35a's second half.
    ///
    /// <b>Two triggers, and no third.</b> A hard crash on the motorcycle throws the rider, and a fall
    /// past <see cref="fallLimit"/> lands as a body rather than as an idle. A car does NOT eject you:
    /// it is the more spectacular option and the less believable one, and the user's call was the
    /// believable pair.
    ///
    /// <b>The recovery is a pose blend, not a clip.</b> Mixamo has a Getting Up animation and this
    /// project does not own it; rather than block the unit on a download, the stand-up interpolates
    /// every bone from where the ragdoll left it back to where the Animator wants it, over
    /// <see cref="blendSeconds"/>. Control returns at the START of that blend, so the recovery costs
    /// the player a third of a second of looking odd rather than a second of being unable to move -
    /// which is the trade a get-up clip actually makes, and the wrong side of it for a game where the
    /// police are driving towards you. <b>The seam for the clip is left open</b>: if the FBX ever
    /// lands, it plays here and the blend becomes its cross-fade.
    ///
    /// <b>The camera is handed a stable anchor, and the root transform never chases the hips.</b> That
    /// is the one design decision in this file worth reading twice. The obvious implementation - move
    /// the player object to the pelvis every frame so everything that follows the player still works -
    /// fights PhysX: the bones are simulated in world space and write their own transforms after the
    /// parent has been moved, so the visual lands a frame behind its own skeleton and jitters. Instead
    /// the player object stays exactly where the fall started, and only the CAMERA is re-pointed, at a
    /// dedicated anchor that tracks the pelvis with a fixed rotation. A spinning hips bone as the
    /// camera's Anchor would roll the horizon.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerRagdoll : MonoBehaviour, IChaseTarget
    {
        private enum Phase
        {
            None,
            Simulating,
            Settled,
            Standing,
        }

        [Header("Triggers")]
        [Tooltip("Closing speed, m/s, at which a crash on the motorcycle throws the rider. 8 m/s is " +
                 "about 29 km/h into a wall - hard enough that staying seated is the odd-looking one.")]
        [SerializeField] private float ejectSpeed = 8f;

        [Tooltip("Fraction of the bike's velocity the rider leaves with. Under 1 because the bike " +
                 "stops against whatever it hit and the rider does not.")]
        [SerializeField] private float ejectShare = 0.9f;

        [Tooltip("Metres the rider is thrown upward on ejection, on top of that.")]
        [SerializeField] private float ejectLift = 2.5f;

        [Tooltip("Metres of fall past which landing is a ragdoll rather than an idle. Well above the " +
                 "jump, which is about a metre, and above every kerb and step in Florentin.")]
        [SerializeField] private float fallLimit = 5f;

        [Header("Recovery")]
        [Tooltip("Mean bone speed below which the body counts as settled, m/s.")]
        [SerializeField] private float settleSpeed = 0.35f;

        [Tooltip("Seconds it must hold below that before the body is called settled.")]
        [SerializeField] private float settleHold = 0.35f;

        [Tooltip("Seconds after which the body is settled whatever it is doing - a limb against a " +
                 "joint limit never gets under the threshold on its own, and a player who cannot " +
                 "stand up is a player who has to quit to the title.")]
        [SerializeField] private float settleTimeout = 4f;

        [Tooltip("Seconds lying still before getting up. Long enough to read as a fall, short enough " +
                 "that the police catching up is a risk rather than a certainty.")]
        [SerializeField] private float lieSeconds = 0.9f;

        [Tooltip("Seconds to blend the crumpled pose back into the animation. Control returns at the " +
                 "start of it, not the end.")]
        [SerializeField] private float blendSeconds = 0.35f;

        [Header("Wiring")]
        [SerializeField] private PlayerController player;
        [SerializeField] private CharacterBody body;
        [SerializeField] private FollowCamera followCamera;
        [SerializeField] private VehicleEnterExit vehicles;

        private CharacterController _controller;
        private Ragdoll _rig;
        private Transform _camAnchor;
        private bool _hooked;

        private Phase _phase = Phase.None;
        private float _stillFor;
        private float _since;
        private float _blend;

        private Quaternion[] _pose;         // the crumple, captured the frame control comes back
        private Vector3 _poseHipsLocal;
        private Transform[] _bones;
        private Transform _hips;

        private float _peakY;
        private bool _airborne;
        private Vector3 _pending;           // a launch asked for from inside a physics callback
        private bool _hasPending;

        /// <summary>True from the throw until the player is standing again and back in control.</summary>
        public bool Down => _phase != Phase.None;

        private void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (body == null && player != null) body = player.GetComponent<CharacterBody>();
            if (body == null) body = FindAnyObjectByType<CharacterBody>();
            if (followCamera == null) followCamera = FindAnyObjectByType<FollowCamera>();
            if (vehicles == null) vehicles = FindAnyObjectByType<VehicleEnterExit>();
            if (player != null) player.TryGetComponent(out _controller);

            var anchor = new GameObject("RagdollCameraAnchor");
            anchor.transform.SetParent(transform, false);
            _camAnchor = anchor.transform;

            Rebind();
        }

        private void OnEnable()
        {
            CrashSensor.Crashed += OnCrashed;
            if (body != null) body.Swapped += Rebind;
            _hooked = true;
        }

        private void OnDisable()
        {
            CrashSensor.Crashed -= OnCrashed;
            if (body != null) body.Swapped -= Rebind;
            _hooked = false;
        }

        /// <summary>
        /// Re-find the rig on the body that is worn right now.
        ///
        /// <b>Subscribed to <see cref="CharacterBody.Swapped"/>, and that is not optional</b> - U29's
        /// scar is exactly this: the roster throws the visual child away and instantiates another, so
        /// every cache taken off the body points at a destroyed skeleton the moment the player picks
        /// Jody (memory: <c>caches-outlive-a-swapped-body</c>).
        /// </summary>
        private void Rebind()
        {
            _rig = null;
            _bones = null;
            _hips = null;

            if (body == null) return;
            if (body.Animator == null) body.Rebind();
            if (body.Animator == null) return;

            _rig = body.Animator.GetComponent<Ragdoll>();
            if (_rig == null) _rig = body.Animator.GetComponentInParent<Ragdoll>();
            if (_rig == null || !_rig.IsBuilt) return;

            _hips = _rig.Bones[0] != null ? _rig.Bones[0].transform : null;
            _bones = body.Animator.GetComponentsInChildren<Transform>(true);
        }

        private void Update()
        {
            // A recompile mid-Play drops both delegates and never runs OnEnable again.
            if (!_hooked) OnEnable();
            if (_rig == null || _bones == null) Rebind();

            if (_hasPending)
            {
                _hasPending = false;
                Launch(_pending);
            }

            switch (_phase)
            {
                case Phase.None:
                    WatchTheFall();
                    break;

                case Phase.Simulating:
                    Simulate();
                    break;

                case Phase.Settled:
                    _since += Time.deltaTime;
                    if (_since >= lieSeconds) StandUp();
                    break;
            }
        }

        /// <summary>
        /// The pose blend, in LateUpdate because the Animator has to have written this frame's pose
        /// before there is anything to blend TOWARDS. In Update it would read the previous frame's,
        /// which is the crumple it is trying to leave.
        /// </summary>
        private void LateUpdate()
        {
            if (_phase != Phase.Standing || _bones == null) return;

            _blend += Time.deltaTime / Mathf.Max(0.01f, blendSeconds);
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_blend));

            for (int i = 0; i < _bones.Length; i++)
            {
                if (_bones[i] == null || i >= _pose.Length) continue;
                _bones[i].localRotation = Quaternion.Slerp(_pose[i], _bones[i].localRotation, t);
            }

            if (_hips != null) _hips.localPosition = Vector3.Lerp(_poseHipsLocal, _hips.localPosition, t);

            if (_blend >= 1f) _phase = Phase.None;
        }

        // --- triggers ---------------------------------------------------------------------------

        /// <summary>
        /// Off the bike. <b>Deferred to the next Update rather than done here</b>, because this runs
        /// inside <c>OnCollisionEnter</c>: <see cref="VehicleEnterExit.LeaveVehicleNow"/> disables
        /// colliders, re-parents the rider and teleports a CharacterController, and doing any of that
        /// from inside a physics callback is how a frame ends up half-simulated.
        /// </summary>
        private void OnCrashed(CrashSensor.Impact impact)
        {
            if (Down || _hasPending || !Progress.RagdollsOn) return;
            if (vehicles == null || vehicles.Mode != GameMode.Driving) return;
            if (impact.ClosingSpeed < ejectSpeed) return;

            // A bike only. The rider of a car is behind a windscreen, and this is the trigger pair the
            // user chose.
            if (vehicles.ActiveVehicle is not MotorcycleController bike) return;

            // The sensor that fired must be the one on the bike UNDER the player - every vehicle in
            // the world carries a CrashSensor, and a taxi hitting a wall two streets away is not the
            // player coming off anything.
            if (impact.Sensor == null || impact.Sensor.gameObject != bike.gameObject) return;

            var travel = bike.GetTransform().forward * bike.ForwardSpeed;
            _pending = travel * ejectShare + Vector3.up * ejectLift;
            _hasPending = true;
        }

        /// <summary>
        /// The fall. Measured from the highest point of the airborne stretch, so walking off a roof
        /// and jumping off one are the same measurement, and a jump is nowhere near the limit.
        ///
        /// Swimming is exempt: the sea is where a long drop is SUPPOSED to end well, and U7b's swim
        /// pose owns the body there.
        /// </summary>
        private void WatchTheFall()
        {
            if (player == null || !Progress.RagdollsOn) return;

            if (vehicles != null && vehicles.Mode != GameMode.OnFoot)
            {
                _airborne = false;
                return;
            }

            float y = player.transform.position.y;

            if (!player.IsGrounded)
            {
                if (!_airborne)
                {
                    _airborne = true;
                    _peakY = y;
                }

                if (y > _peakY) _peakY = y;
                return;
            }

            if (!_airborne) return;
            _airborne = false;

            float drop = _peakY - y;
            if (drop < fallLimit || player.IsSwimming) return;

            // What the body was doing when it arrived: its own planar travel, plus the speed the drop
            // is worth. Free fall, so v = √(2gh) - and the numbers this game falls at (5-20 m) put it
            // between 10 and 20 m/s, which is why a landing looks like an impact rather than a lie-down.
            float impact = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * drop);
            Launch(player.transform.forward * player.PlanarSpeed + Vector3.down * impact);
        }

        // --- the state machine ------------------------------------------------------------------

        /// <summary>Hand the body to PhysX and take control away.</summary>
        public void Launch(Vector3 velocity)
        {
            if (Down || _rig == null || !_rig.IsBuilt || player == null) return;

            RagdollBudget.Take(_rig);
            _rig.Activate(velocity, _rig.Position, Vector3.zero);

            player.enabled = false;
            if (_controller != null) _controller.enabled = false;

            if (followCamera != null) followCamera.Follow(this, snap: false);

            _phase = Phase.Simulating;
            _stillFor = 0f;
            _since = 0f;
        }

        private void Simulate()
        {
            _since += Time.deltaTime;
            TrackCamera();

            bool still = _rig.Frozen || _rig.Speed <= settleSpeed;
            _stillFor = still ? _stillFor + Time.deltaTime : 0f;

            if (_stillFor < settleHold && _since < settleTimeout) return;

            _phase = Phase.Settled;
            _since = 0f;
        }

        /// <summary>
        /// Stand the player back up where the body ended up.
        ///
        /// The order is the whole method: capture the crumple BEFORE the Animator is switched back on
        /// (one frame later it is gone), then move the player object under the pelvis with the
        /// CharacterController's own guard - <see cref="PlayerController.Teleport"/> owns that,
        /// because a controller that is not switched off across a position write sweeps its capsule
        /// through every building between the two points.
        ///
        /// <b>The yaw is the one the player fell with, not the pelvis's.</b> A pelvis face-down on the
        /// tarmac points at the sky, and reading a heading off it stands the player up facing a
        /// direction they never chose.
        /// </summary>
        private void StandUp()
        {
            CapturePose();

            var at = _rig.Position;
            float groundY = at.y;

            // Down from just above the pelvis, first hit wins, and NOT through the corpse: the rig's
            // own shells are on the Pedestrian layer (see Ragdoll.ShellLayer), so an unmasked ray
            // stands the player on their own thigh.
            int mask = ~0;
            int shells = LayerMask.NameToLayer(Ragdoll.ShellLayer);
            if (shells >= 0) mask &= ~(1 << shells);

            if (Physics.Raycast(at + Vector3.up * 2f, Vector3.down, out var hit, 8f, mask,
                    QueryTriggerInteraction.Ignore))
                groundY = hit.point.y;

            _rig.Rest();

            player.enabled = true;
            if (_controller != null) _controller.enabled = true;
            player.Teleport(new Vector3(at.x, groundY, at.z), player.transform.eulerAngles.y);

            if (followCamera != null) followCamera.FollowPlayer(snap: false);

            _phase = Phase.Standing;
            _blend = 0f;
        }

        /// <summary>Every bone's local rotation as physics left it - what the blend starts from.</summary>
        private void CapturePose()
        {
            if (_bones == null) Rebind();
            if (_bones == null) return;

            if (_pose == null || _pose.Length != _bones.Length) _pose = new Quaternion[_bones.Length];

            for (int i = 0; i < _bones.Length; i++)
                _pose[i] = _bones[i] != null ? _bones[i].localRotation : Quaternion.identity;

            if (_hips != null) _poseHipsLocal = _hips.localPosition;
        }

        /// <summary>
        /// Keeps the camera's anchor on the pelvis without inheriting its tumble. Yaw is held at
        /// whatever the player was facing, so the horizon does not roll while the body does.
        /// </summary>
        private void TrackCamera()
        {
            if (_camAnchor == null || _rig == null) return;
            _camAnchor.SetPositionAndRotation(
                _rig.Position, Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f));
        }

        // --- IChaseTarget ---------------------------------------------------------------------------

        public Transform Anchor => _camAnchor != null ? _camAnchor : transform;

        /// <summary>The player's own boom, so the shot does not change size when the body goes down.</summary>
        public Vector3 LocalBoom => player != null ? player.LocalBoom : new Vector3(0f, 2.5f, -5f);

        public Vector3 LookTarget => _rig != null ? _rig.Position + Vector3.up * 0.4f : transform.position;

        /// <summary>Slower than the player's: a body that is tumbling wants a camera that is not.</summary>
        public float FollowLerp => 0.12f;
    }
}
