using TheBlock.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// The rescue Huey — the port of <c>src/vehicle/helicopter.ts</c>, and U23's answer to the
    /// standing "can Unity do this better?" question.
    ///
    /// <b>The web's craft is a kinematic Rapier character controller with gravity switched off
    /// forever.</b> It never falls, it never rests on anything, and "landing" means its
    /// collide-and-slide happened to stop. It also has to hand-write its own free-fall —
    /// <c>fallGravity</c> and <c>fallMaxSpeed</c> exist purely because a craft abandoned in mid-air
    /// had to be made to drop by arithmetic.
    ///
    /// Here it is a real <see cref="Rigidbody"/>. Flown, gravity is off and the intended velocity is
    /// written straight in, which keeps the arcade feel exactly — you do not fight a spring to
    /// hover. Vacated in the air, <c>useGravity</c> goes true and PhysX does the fall, so those two
    /// config numbers become a shape to check the result against rather than code. And a craft set
    /// down on a rooftop RESTS on it, under contact, which is what the rescue mission needs from
    /// every roof in the city.
    ///
    /// <b>The collider is the fuselage only.</b> The config's box excludes the ~10 m rotor disc,
    /// measured in Blender, so the Huey fits between buildings and sets down on its skids. A
    /// collider that included the disc would refuse to enter most of Florentin.
    ///
    /// <b>Yaw is an angular VELOCITY, written every step, and that is not a style choice.</b> The
    /// craft used to compose <c>MoveRotation</c> onto the body only while the stick was off centre,
    /// which left nothing at all in charge of rotation the rest of the time — so any yaw a contact
    /// imparted simply stayed. Measured: a 3 rad/s knock still reads <b>1.82 rad/s after ten
    /// seconds</b> under PhysX's default 0.05 angular damping, and <c>MoveRotation</c> composed on
    /// top does not clear it. With <c>_planar</c> pushed along a nose that is turning on its own,
    /// that is the spin the play-test found — and it is a spiral rather than a pirouette because
    /// held throttle keeps thrusting along the rotating forward.
    ///
    /// Controls: W/S or ↑/↓ fly · A/D or ←/→ turn · Space climb · Shift descend · R respawn · E out.
    /// Both key sets, because the car, the bike and walking all take both.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class HelicopterController : MonoBehaviour, IEnterable
    {
        [SerializeField] private Rigidbody body;
        [SerializeField] private Transform riderAnchor;
        [SerializeField] private Rotor rotor;

        [Tooltip("Written by HelicopterBuilder from config.vehicle.helicopter.")]
        [SerializeField] private float maxSpeed = 25f;
        [SerializeField] private float accel = 12f;
        [SerializeField] private float friction = 6f;
        [SerializeField] private float yawRate = 1.2f;
        [SerializeField] private float maxVertSpeed = 10f;
        [SerializeField] private float vertAccel = 14f;
        [SerializeField] private float vertFriction = 12f;

        [Header("Camera — config.vehicle.helicopter.camera")]
        [SerializeField] private Vector3 boom = new(0f, 6f, -16f);
        [SerializeField] private float lookYOffset = 2f;
        [SerializeField] private float followLerp = 0.08f;

        /// <summary>
        /// How fast an unflown craft's yaw bleeds off, in rad/s². Not a config number — the config
        /// has no such term, because the web's craft is kinematic and cannot be spun at all.
        /// </summary>
        private const float YawSettle = 6f;

        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private bool _spawnCaptured;

        /// <summary>Horizontal velocity we are steering. Vertical is tracked separately.</summary>
        private Vector3 _planar;
        private float _vertical;

        private void Awake()
        {
            if (body == null && !TryGetComponent(out body)) body = gameObject.AddComponent<Rigidbody>();

            // Interpolated, because a 25 m/s craft written at the physics rate and drawn at the
            // frame rate visibly stutters against a city that is not moving.
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // Upright, always. A helicopter that can be tipped by clipping a parapet is a
            // helicopter you cannot land, and the web's kinematic craft could not tip at all — that
            // part of its feel is design, not a Rapier limitation.
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            body.useGravity = false;
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

            if (!Driven)
            {
                // Nobody aboard. Gravity has it — this is the web's hand-written free-fall, done by
                // the engine — and once it is resting the velocity it kept from the last frame of
                // flight must not carry it off sideways.
                body.useGravity = true;
                _planar = Vector3.zero;
                _vertical = 0f;

                // A parked craft must not spin either. The default 0.05 angular damping keeps 61%
                // of a knock ten seconds later, so a Huey that landed hard would still be turning
                // on its pad when the player walked back to it. X and Z are frozen: this is yaw.
                body.angularVelocity =
                    Vector3.MoveTowards(body.angularVelocity, Vector3.zero, YawSettle * dt);
                return;
            }

            body.useGravity = false;

            var keyboard = Keyboard.current;
            var throttle = 0f;
            var steer = 0f;
            var collective = 0f;

            if (keyboard != null)
            {
                throttle = Held(keyboard.wKey, keyboard.upArrowKey) -
                           Held(keyboard.sKey, keyboard.downArrowKey);
                steer = Held(keyboard.dKey, keyboard.rightArrowKey) -
                        Held(keyboard.aKey, keyboard.leftArrowKey);
                if (keyboard.spaceKey.isPressed) collective += 1f;
                if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed) collective -= 1f;
            }

            // Yaw works while hovering, which is the one thing a helicopter does that a car cannot.
            //
            // Written as a velocity EVERY step, including the zero. That is what makes the stick
            // authoritative: a centred stick now means "not turning" rather than "not asking", so a
            // clipped parapet cannot leave the craft rotating. `yawRate` is rad/s in the config,
            // which is exactly what `angularVelocity` wants — the old Rad2Deg × dt was there only
            // to build a per-step Euler delta.
            body.angularVelocity = new Vector3(0f, steer * yawRate, 0f);

            // Horizontal: accelerate along the nose, or coast down when the stick is centred.
            if (Mathf.Abs(throttle) > 0.01f) _planar += transform.forward * (throttle * accel * dt);
            else _planar = Vector3.MoveTowards(_planar, Vector3.zero, friction * dt);
            _planar = Vector3.ClampMagnitude(_planar, maxSpeed);

            // Vertical: the collective. Releasing it decays toward zero rather than dropping the
            // craft, and THAT is the hover — the single most important number in the block.
            if (Mathf.Abs(collective) > 0.01f) _vertical += collective * vertAccel * dt;
            else _vertical = Mathf.MoveTowards(_vertical, 0f, vertFriction * dt);
            _vertical = Mathf.Clamp(_vertical, -maxVertSpeed, maxVertSpeed);

            body.linearVelocity = new Vector3(_planar.x, _vertical, _planar.z);
        }

        /// <summary>
        /// Keeps the steering state honest after a collision.
        ///
        /// Writing <c>linearVelocity</c> every step would otherwise drive the craft straight back
        /// into whatever it just hit, at full speed, forever — the wall wins the contact and the
        /// next FixedUpdate re-asserts the same velocity. Reading the body back means a wall
        /// actually stops it.
        /// </summary>
        private void OnCollisionStay(Collision collision)
        {
            if (!Driven) return;
            var v = body.linearVelocity;
            _planar = new Vector3(v.x, 0f, v.z);
            _vertical = v.y;

            // Deliberately NOT the same deal for rotation. A wall winning the contact is what stops
            // the craft; a wall winning the yaw is the spin. FixedUpdate re-asserts the stick.
        }

        private static float Held(KeyControl primary, KeyControl alternate) =>
            primary.isPressed || alternate.isPressed ? 1f : 0f;

        // --- IChaseTarget ------------------------------------------------------------------------

        public Transform Anchor => transform;

        /// <summary>
        /// Higher and further back than the car boom, to frame the craft AND its altitude. Already
        /// converted at build time — three.js hangs a chase camera at +Z, Unity at −Z, the same flip
        /// the car and the bike make.
        /// </summary>
        public Vector3 LocalBoom => boom;

        public Vector3 LookTarget => transform.position + Vector3.up * lookYOffset;

        public float FollowLerp => followLerp;

        // --- IEnterable --------------------------------------------------------------------------

        public Transform GetTransform() => transform;
        public Transform RiderAnchor => riderAnchor;

        /// <summary>Door-less, like the bike and the jetski.</summary>
        public CarDoor Door => null;

        /// <summary>No walk-up-and-sit clip: you climb into a Huey, you do not open its car door.</summary>
        public bool UsesEntryAnimation => false;

        /// <summary>
        /// Hidden, like a car's driver. The config carries no rider block for the helicopter — the
        /// jetski and the bike both do — so there is no seated pose authored for it, and an empty
        /// cockpit is better than a man floating in the wrong one.
        /// </summary>
        public bool ShowRiderOnQuickMount => false;

        /// <summary>You step out to the left, onto the skid.</summary>
        public Vector3 DriverSide => -transform.right;

        public float ForwardSpeed =>
            body == null ? 0f : Vector3.Dot(body.linearVelocity, transform.forward);

        public float SpeedKmh => body == null ? 0f : body.linearVelocity.magnitude * 3.6f;

        public bool Driven { get; set; }

        /// <summary>
        /// Locked until the campaign reaches its step — the port of <c>heliUnlocked</c>.
        ///
        /// This is the one place a vehicle refuses <c>E</c>, and it is why <see cref="IEnterable"/>
        /// has a <see cref="TryEnter"/> that can say no at all. Remy gates the chopper behind the
        /// dance, so before you have danced the keys are not yours.
        /// </summary>
        public System.Func<bool> Unlocked;

        /// <summary>The web build's own line for standing at a chopper you have not earned.</summary>
        public string EntryRefusal =>
            Unlocked == null || Unlocked() ? null : "Win the dance to earn the keys";

        public bool TryEnter()
        {
            if (Unlocked != null && !Unlocked()) return false;
            CaptureSpawn();
            return true;
        }

        private void OnEnable() => EnterableRegistry.Register(this);
        private void OnDisable() => EnterableRegistry.Unregister(this);

        public void Exit()
        {
            // Stopped dead as the pilot steps off. On the ground that is just parking; in the air it
            // is the start of the fall, and starting it from rest is what makes the drop read as the
            // craft losing its collective rather than being thrown.
            if (body == null) return;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            _planar = Vector3.zero;
            _vertical = 0f;
        }

        public void Respawn()
        {
            CaptureSpawn();
            if (body == null) return;

            // Stop the body BEFORE moving it and sync the transforms after — the four-step move
            // CarController.Teleport already documents. A craft moved while carrying 25 m/s arrives
            // at its pad still travelling.
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            _planar = Vector3.zero;
            _vertical = 0f;

            body.position = _spawnPosition;
            body.rotation = _spawnRotation;
            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            Physics.SyncTransforms();
        }

        /// <summary>Spins the blades up while flown. Called by the enter/exit machine.</summary>
        public void SetOccupied(bool occupied)
        {
            Driven = occupied;
            if (rotor != null) rotor.SetFlying(occupied);
        }

        /// <summary>Editor-side wiring, used by <c>HelicopterBuilder</c>.</summary>
        public void Configure(
            Transform seat, Rotor blades, TheBlockConfig.HelicopterSpec spec)
        {
            riderAnchor = seat;
            rotor = blades;

            var flight = spec.Flight;
            maxSpeed = flight.MaxSpeed;
            accel = flight.Accel;
            friction = flight.Friction;
            yawRate = flight.YawRate;
            maxVertSpeed = flight.MaxVertSpeed;
            vertAccel = flight.VertAccel;
            vertFriction = flight.VertFriction;

            boom = Convert.ModelOffset(spec.Camera.LocalOffset.Raw);
            lookYOffset = spec.Camera.LookYOffset;
            followLerp = spec.Camera.FollowLerp;
        }
    }
}
