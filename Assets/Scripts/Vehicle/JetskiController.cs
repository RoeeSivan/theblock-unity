using TheBlock.Core;
using TheBlock.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// M4's ride — the port of <c>src/vehicle/jetski.ts</c>.
    ///
    /// Handling is the bike's speed-and-heading model made drifty: a fifth of the road's friction so
    /// it coasts a long way, no handbrake, steering that only bites with forward travel, and a
    /// visual roll into the turn. All of that is feel and it ports; the physics NUMBERS behind it do
    /// not (port rule 2), so what carries over is the shape.
    ///
    /// <b>The hull is pinned to the water plane rather than floating on it.</b> Buoyancy would be a
    /// whole system to tune for a sea that is a shader with no volume — U12 built the water as a
    /// surface, not as a body. So Y is written to the waterline plus an idle bob, and the body is
    /// kinematic.
    ///
    /// <b>Which means the buoys are NOT free, and this unit's plan was wrong about that.</b> It said
    /// Unity would delete the web's two separate avoidance mechanisms because a collider is a
    /// collider. A kinematic rigidbody gets no collision response against a static one, so both skis
    /// would have driven straight through all nine. <see cref="TheBlock.Missions.BuoyField"/> is the
    /// web's own radial push-out, shared by the player and the thief — one mechanism where the web
    /// has two, which is a smaller win than the plan claimed and a real one.
    ///
    /// Controls: W/S throttle · A/D steer · Space brake · R respawn · E off · F starts the chase.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class JetskiController : MonoBehaviour, IEnterable
    {
        [SerializeField] private Rigidbody body;
        [SerializeField] private Transform riderAnchor;

        [Header("Handling — written from config.vehicle.jetski.handling")]
        [SerializeField] private float maxSpeed = 18f;
        [SerializeField] private float reverseMaxSpeed = 3f;
        [SerializeField] private float accel = 7f;
        [SerializeField] private float waterFriction = 1.5f;
        [SerializeField] private float brakeDecel = 6f;
        [SerializeField] private float maxWheelAngle = 0.4f;
        [SerializeField] private float steerRatio = 0.45f;
        [SerializeField] private float wheelReturn = 4f;
        [SerializeField] private float leanMax = 0.25f;
        [SerializeField] private float bobAmp = 0.06f;
        [SerializeField] private float bobFreq = 1.6f;
        [SerializeField] private float floatY;

        [Header("Camera — the shared chase boom, as the bike uses")]
        [SerializeField] private Vector3 boom = new(0f, 2.5f, -6.5f);
        [SerializeField] private float lookYOffset = 1.2f;
        [SerializeField] private float followLerp = 0.12f;

        private TheBlockConfig.SeaSpec _sea;
        private Transform _visual;

        /// <summary>
        /// The Visual's built rotation — the Sketchfab Rx(−90) plus the model's facing flip.
        ///
        /// The lean is composed ON TOP of this rather than replacing it. Writing a bare
        /// <c>Euler(0, y, roll)</c> threw the −90° away on the first FixedUpdate, which laid the
        /// whole ski on its nose in the water, parked or not: the roll is a rotation of the CRAFT
        /// and the base is a correction of the MODEL, and the two do not live in the same frame.
        /// </summary>
        private Quaternion _visualRest = Quaternion.identity;

        private float _speed;
        private float _steer;
        private float _bobT;

        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private bool _spawnCaptured;

        private void Awake()
        {
            if (body == null && !TryGetComponent(out body)) body = gameObject.AddComponent<Rigidbody>();

            // Kinematic: the hull's motion is scripted onto the water plane, and the body exists so
            // the buoys are real collisions rather than arithmetic. A dynamic hull would need
            // buoyancy for a sea that has no volume to be buoyant in.
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            _sea = TheBlockConfig.Load()?.Config?.Sea;
            _visual = transform.Find("Visual");
            if (_visual != null) _visualRest = _visual.localRotation;
            CaptureSpawn();
        }

        private void CaptureSpawn()
        {
            if (_spawnCaptured) return;
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            _spawnCaptured = true;
        }

        private void FixedUpdate()
        {
            var dt = Time.fixedDeltaTime;
            var throttle = 0f;
            var steerInput = 0f;
            var braking = false;

            if (Driven)
            {
                var keyboard = Keyboard.current;
                if (keyboard != null)
                {
                    // Arrows as well as WASD, like every other thing the player steers here.
                    throttle = Held(keyboard.wKey, keyboard.upArrowKey) -
                               Held(keyboard.sKey, keyboard.downArrowKey);
                    steerInput = Held(keyboard.dKey, keyboard.rightArrowKey) -
                                 Held(keyboard.aKey, keyboard.leftArrowKey);
                    braking = keyboard.spaceKey.isPressed;
                }
            }

            // Speed. Coast decay is deliberately tiny — the long glide IS the jetski.
            if (braking) _speed = Mathf.MoveTowards(_speed, 0f, brakeDecel * dt);
            else if (Mathf.Abs(throttle) > 0.01f) _speed += throttle * accel * dt;
            else _speed = Mathf.MoveTowards(_speed, 0f, waterFriction * dt);
            _speed = Mathf.Clamp(_speed, -reverseMaxSpeed, maxSpeed);

            // Steering centres itself slowly, which is most of the drifty feel.
            if (Mathf.Abs(steerInput) > 0.01f)
                _steer = Mathf.Clamp(_steer + steerInput * maxWheelAngle * 4f * dt, -maxWheelAngle, maxWheelAngle);
            else
                _steer = Mathf.MoveTowards(_steer, 0f, wheelReturn * maxWheelAngle * dt);

            // Yaw gained per metre travelled — so the nose does nothing while stopped, exactly as
            // the bike's does. This is the whole reason the config calls it a ratio, not a rate.
            var yawDelta = _steer * steerRatio * _speed * dt;
            var rotation = body.rotation * Quaternion.Euler(0f, yawDelta * Mathf.Rad2Deg, 0f);

            var next = body.position + rotation * Vector3.forward * (_speed * dt);

            // The buoys. A KINEMATIC body gets no collision response against a static collider, so
            // the nine gate markers would otherwise be scenery you drive through — see BuoyField,
            // which is where that finding is written down.
            next = TheBlock.Missions.BuoyField.PushOut(next);

            // Pinned to the water. `SeaGeometry` owns the waterline and its handedness — the sea is
            // Unity +X, which is the mirror of the web's −X, and this must never re-derive that.
            //
            // The ORIGIN is the waterline, which is why there is no hull-height term: the prefab is
            // centred in Y (unlike the road vehicles, which sit on their contact patch), so a hull
            // floats around y=0 with about 0.66 m of it wet. `floatY` is the nudge on top.
            _bobT += dt * bobFreq;
            var bobScale = 1f - Mathf.Min(1f, Mathf.Abs(_speed) / Mathf.Max(1f, maxSpeed * 0.4f));
            var level = _sea?.Level ?? 0f;
            next.y = level + floatY + Mathf.Sin(_bobT) * bobAmp * bobScale;

            body.MovePosition(next);
            body.MoveRotation(rotation);

            // The lean is visual only, on the child, so the collider never tips with it.
            if (_visual == null) return;
            var lean = Mathf.Clamp(_steer / Mathf.Max(0.01f, maxWheelAngle), -1f, 1f) *
                       leanMax * Mathf.Clamp01(Mathf.Abs(_speed) / Mathf.Max(1f, maxSpeed * 0.5f));

            // PRE-multiplied: a roll about the craft's own forward, applied after the model
            // correction rather than instead of it. See _visualRest.
            _visual.localRotation =
                Quaternion.AngleAxis(-lean * Mathf.Rad2Deg, Vector3.forward) * _visualRest;
        }

        private static float Held(KeyControl primary, KeyControl alternate) =>
            primary.isPressed || alternate.isPressed ? 1f : 0f;

        // --- IChaseTarget ------------------------------------------------------------------------

        public Transform Anchor => transform;
        public Vector3 LocalBoom => boom;
        public Vector3 LookTarget => transform.position + Vector3.up * lookYOffset;
        public float FollowLerp => followLerp;

        // --- IEnterable --------------------------------------------------------------------------

        public Transform GetTransform() => transform;
        public Transform RiderAnchor => riderAnchor;
        public CarDoor Door => null;

        /// <summary>You climb on; there is no door to walk up to.</summary>
        public bool UsesEntryAnimation => false;

        /// <summary>
        /// The rider STAYS visible, like the bike's and unlike a car's. A jetski crossing the bay
        /// with nobody on it is the bug, and the config carries a rider seat block for exactly this.
        /// </summary>
        public bool ShowRiderOnQuickMount => true;

        public Vector3 DriverSide => -transform.right;
        public float ForwardSpeed => _speed;
        public float SpeedKmh => Mathf.Abs(_speed) * 3.6f;
        public bool Driven { get; set; }

        /// <summary>Locked until the campaign reaches its step — the port of <c>jetskiUnlocked</c>.</summary>
        public System.Func<bool> Unlocked;

        /// <summary>
        /// The web has no line for this one — its jetski is simply silent when locked — so this is
        /// written to match the helicopter's, which the web does have. Swimming out to a ski that
        /// says nothing is the same dead-end the chopper's line exists to prevent.
        /// </summary>
        public string EntryRefusal =>
            Unlocked == null || Unlocked() ? null : "Finish the rescue to earn the keys";

        public bool TryEnter()
        {
            if (Unlocked != null && !Unlocked()) return false;
            CaptureSpawn();
            return true;
        }

        public void Exit() => _speed = 0f;

        public void Respawn()
        {
            CaptureSpawn();
            _speed = 0f;
            _steer = 0f;
            body.position = _spawnPosition;
            body.rotation = _spawnRotation;
            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            Physics.SyncTransforms();
        }

        public void SetOccupied(bool occupied) => Driven = occupied;

        private void OnEnable() => EnterableRegistry.Register(this);
        private void OnDisable() => EnterableRegistry.Unregister(this);

        /// <summary>Editor-side wiring, used by <c>MissionVehicleBuilder</c>.</summary>
        public void Configure(Transform seat, TheBlockConfig.JetskiSpec spec)
        {
            riderAnchor = seat;

            var h = spec.Handling;
            maxSpeed = h.MaxSpeed;
            reverseMaxSpeed = h.ReverseMaxSpeed;
            accel = h.Accel;
            waterFriction = h.WaterFriction;
            brakeDecel = h.BrakeDecel;
            maxWheelAngle = h.MaxWheelAngle;
            steerRatio = h.SteerRatio;
            wheelReturn = h.WheelReturn;
            leanMax = h.LeanMax;
            bobAmp = h.BobAmp;
            bobFreq = h.BobFreq;
            floatY = h.FloatY;

            // The shared chase boom, as the bike takes — the web hands the jetski the same one, and
            // a second camera feel for one vehicle is a worse game rather than a better one.
            var camera = TheBlockConfig.Load()?.Config?.Camera;
            if (camera != null)
            {
                boom = Convert.ModelOffset(camera.LocalOffset.Raw);
                lookYOffset = camera.LookYOffset;
                followLerp = camera.FollowLerp;
            }
        }
    }
}
