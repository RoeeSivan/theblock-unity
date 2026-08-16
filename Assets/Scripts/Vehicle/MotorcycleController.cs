using TheBlock.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// A rideable motorcycle: a Rigidbody on two WheelColliders, held upright by a torque and
    /// leaning into its corners.
    ///
    /// This is NOT a port of <c>src/vehicle/motorcycle.ts</c>. That bike is kinematic — a scalar
    /// speed and a heading pushed through a Rapier character controller with a ray under it snapping
    /// the body to the road — for exactly the reason the car was (see <see cref="CarController"/>):
    /// Rapier's vehicle controller was unusable in that project, not because a two-wheeler wanted it.
    /// Port rule 5 says scar tissue does not carry, and the standing "can Unity do this better?"
    /// remark says to spend the difference. What that buys here, none of which the web build has:
    ///
    ///  - it is hit by, and hits, the cars and the world instead of sliding through them;
    ///  - it has suspension, so a Florentin kerb is a bump rather than a teleport;
    ///  - it LEANS, which is most of what makes a bike read as a bike rather than a narrow car;
    ///  - it keeps its momentum, which U18's run-over and U19's ramming inherit for free.
    ///
    /// The one thing a two-wheeler needs that a car does not is a reason to stay upright: two
    /// contact points give a Rigidbody no roll stability at all, and it falls over the moment it is
    /// spawned. <see cref="Stabilize"/> is that reason, and it runs whether or not anyone is riding —
    /// a parked bike with nobody on it has to stand there too, and this model has no kickstand.
    ///
    /// Gameplay numbers carry from config (the 20 m/s cap, the 7 m/s reverse, the ~34° lock);
    /// every PhysX number below is re-derived by feel, per port rule 2, and none of it should be
    /// compared against config.ts.
    ///
    /// Its forward is <c>+Z</c>. The imported GLB's is not; see <see cref="Convert.ModelFacing"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class MotorcycleController : MonoBehaviour, IEnterable
    {
        [Header("Wheels — front steers, rear drives. Wired by MotorcycleBuilder")]
        [SerializeField] private WheelCollider frontWheel;
        [SerializeField] private WheelCollider rearWheel;

        [Header("Cabin — wired by MotorcycleBuilder from config.vehicle.motorcycle.rider")]
        [Tooltip("Where the rider sits. Unlike the car's, this IS a seat: the bike has no entry " +
                 "animation, so the rider is put straight onto it holding the driving pose.")]
        [SerializeField] private Transform riderAnchor;

        [Tooltip("The visual model's parent. Rolled to lean the bike into corners WITHOUT touching " +
                 "the Rigidbody — a physical lean on two wheels is a fall with extra steps.")]
        [SerializeField] private Transform leanPivot;

        [Header("Drive — PhysX numbers, derived by feel (port rule 2)")]
        [Tooltip("Nm on the rear wheel. A bike is a seventh of the Mustang's mass, so far less " +
                 "torque gets far more acceleration — which is the point of taking the bike.")]
        [SerializeField] private float motorTorque = 950f;

        [Tooltip("Nm on the rear wheel when braking. The front gets `frontBrakeShare` of it.")]
        [SerializeField] private float brakeTorque = 1200f;

        [Tooltip("How much of the brake reaches the front wheel. Under 1 because a bike that " +
                 "brakes hardest at the front pitches over its bars.")]
        [Range(0f, 1f)]
        [SerializeField] private float frontBrakeShare = 0.55f;

        [Tooltip("Nm on both wheels with no key held. A WheelCollider has no rolling resistance, " +
                 "so without this it coasts forever. The web build called it `friction`.")]
        [SerializeField] private float coastBrake = 220f;

        [Header("Steering")]
        [Tooltip("Degrees per second the front wheel swings toward the held direction. Quicker " +
                 "than the car's 120 — the whole feel of a bike is that it changes direction now.")]
        [SerializeField] private float steerRate = 200f;

        [Tooltip("Steering lock left at top speed, as a fraction.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float steerAtTopSpeed = 0.30f;

        [Header("Stability — this is what stops a two-wheeler falling over")]
        [Tooltip("Centre of mass in local space, measured from the contact patch. Low, and back " +
                 "toward the driven wheel so the front stays light without lifting.")]
        [SerializeField] private Vector3 centerOfMass = new Vector3(0f, 0.28f, -0.05f);

        [Tooltip("Nm per radian of roll error. Too low and it lies down; too high and it snaps " +
                 "upright like a metronome.")]
        [SerializeField] private float uprightSpring = 9000f;

        [Tooltip("Seconds of roll velocity the stabiliser looks ahead. This is the damping term — " +
                 "it corrects where the bike is GOING to be, so it settles instead of oscillating.")]
        [SerializeField] private float uprightPredict = 0.35f;

        [Tooltip("Downward force at top speed, in multiples of the bike's weight.")]
        [SerializeField] private float downforce = 0.5f;

        [Header("Lean — visual only, and the difference between a bike and a narrow car")]
        [Tooltip("Hard limit on the roll, degrees. Real racers pass 50°; a delivery scooter does not.")]
        [SerializeField] private float maxLeanDegrees = 32f;

        [Tooltip("Degrees per second the lean chases its target.")]
        [SerializeField] private float leanRate = 180f;

        [Header("Skid — Space")]
        [Tooltip("Rear sideways grip multiplier while Space is down. Under 1 lets the back step out.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float skidGrip = 0.35f;

        [Header("Camera")]
        [Tooltip("Look point height above the body origin, if config.camera.lookYOffset is missing.")]
        [SerializeField] private float fallbackLookYOffset = 0.5f;

        private Rigidbody _body;
        private TheBlockConfig.VehicleSpec _spec;
        private TheBlockConfig.MotorcycleSpec _bikeSpec;
        private TheBlockConfig.CameraSpec _cameraSpec;

        private float _steerAngle;
        private float _maxSteerDegrees = 34.4f;
        private float _lean;
        private float _rearGripAtRest = 1f;

        /// <summary>Where it started, so R can put it back without re-reading the config every time.</summary>
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation = Quaternion.identity;
        private bool _spawnCaptured;

        /// <summary>False while nobody is on it: the bike stands there and ignores the keyboard.</summary>
        public bool Driven { get; set; }

        public Transform GetTransform() => transform;

        /// <summary>The seat. Never null on a built bike — the rider is always shown sitting on it.</summary>
        public Transform RiderAnchor => riderAnchor;

        /// <summary>A bike has no door, which is what routes it onto the quick mount.</summary>
        public CarDoor Door => null;

        /// <summary>
        /// No walk-up-and-open-the-door clip: you get on a bike, you do not enter it. The rider is
        /// placed on the seat holding the driving pose instead.
        /// </summary>
        public bool UsesEntryAnimation => false;

        /// <summary>The rider stays visible, sitting on the seat. An empty bike driving itself is the bug.</summary>
        public bool ShowRiderOnQuickMount => true;

        /// <summary>The bike's left, which is the side you step off onto.</summary>
        public Vector3 DriverSide => -transform.right;

        /// <summary>Signed speed along the bike's nose in m/s. Negative is reversing.</summary>
        public float ForwardSpeed => _body == null ? 0f : Vector3.Dot(_body.linearVelocity, transform.forward);

        /// <summary>For the U25 HUD.</summary>
        public float SpeedKmh => Mathf.Abs(ForwardSpeed) * 3.6f;

        /// <summary>
        /// U28b's tank, or null until the bike has been ridden. Serialized for the reason
        /// <c>CarController.fuelTank</c> is: a recompile mid-Play reloads the domain without
        /// re-running Awake, and a tank that silently detached would hand a dry bike its full speed
        /// back mid-limp.
        /// </summary>
        [SerializeField, HideInInspector] private FuelTank fuelTank;

        /// <summary>Called by <see cref="FuelTank.Configure"/> as the tank attaches itself.</summary>
        public void AttachTank(FuelTank tank) => fuelTank = tank;

        private float FuelFactor => fuelTank != null ? fuelTank.SpeedFactor : 1f;

        /// <summary>
        /// ☕ Nitro coffee scales the forward ceiling, and so does U28b's tank — as FACTORS of one
        /// product, so a boosted bike on a dry tank still limps. The bike has no police variant, so
        /// unlike <c>CarController</c> there is nothing to exclude from the boost here.
        /// </summary>
        private float MaxForwardSpeed => _spec.MaxSpeed * Powerup.SpeedBoost.Factor * FuelFactor;

        /// <summary>
        /// Reverse. <b>Fuel scales it; the boost does not</b> — the item says "+25% top speed on any
        /// ride", and nobody buys a power-up to back out of a driveway faster. An empty tank is
        /// empty in both directions.
        /// </summary>
        private float MaxReverseSpeed => _spec.ReverseMaxSpeed * FuelFactor;

        private void Awake() => Bind();

        private void OnEnable() => EnterableRegistry.Register(this);

        private void OnDisable() => EnterableRegistry.Unregister(this);

        /// <summary>
        /// Reads the config and caches what the ride loop needs. Called from Awake, and again from
        /// FixedUpdate if the spec has gone null — which is what a script recompile during Play does:
        /// the domain reloads, non-serialized fields clear, and Awake does not run again.
        /// </summary>
        private void Bind()
        {
            _body = GetComponent<Rigidbody>();
            _body.centerOfMass = centerOfMass;

            // Same as CarController.Bind: the bike reports its own impacts, or riding one is immune
            // to the police and silent on contact.
            CrashSensor.Ensure(gameObject);

            var snapshot = TheBlockConfig.Load();
            if (snapshot?.Config?.Vehicle == null)
            {
                Debug.LogError("MotorcycleController: config has no `vehicle` section.", this);
                enabled = false;
                return;
            }

            // The belt to FuelTank.Configure's braces — see CarController.Bind for the argument.
            if (fuelTank == null) TryGetComponent(out fuelTank);

            _spec = snapshot.Config.Vehicle;
            _bikeSpec = _spec.Motorcycle;
            _cameraSpec = snapshot.Config.Camera;
            _maxSteerDegrees = _spec.MaxWheelAngle * Mathf.Rad2Deg;

            if (rearWheel != null) _rearGripAtRest = rearWheel.sidewaysFriction.stiffness;

            // Sub-stepping keeps a WheelCollider honest at speed — without it the contact patch
            // skips over geometry and the bike climbs kerbs it should hit. Applied to one wheel,
            // it configures the whole vehicle.
            if (frontWheel != null) frontWheel.ConfigureVehicleSubsteps(5f, 12, 15);

            // Captured once, after the spawner has put the bike down. R goes back to here rather
            // than to the config spawn, so respawning on a slope lands on the slope.
            if (!_spawnCaptured)
            {
                _spawnPosition = transform.position;
                _spawnRotation = transform.rotation;
                _spawnCaptured = true;
            }
        }

        private void FixedUpdate()
        {
            if (_spec == null) Bind();
            if (_spec == null)
            {
                // Cut the drive BEFORE giving up. A WheelCollider holds the last torque it was
                // given, so a bare `return` here leaves full throttle latched on with nothing left
                // running to cap it — the exact failure U8 hit on the Mustang.
                Coast();
                return;
            }

            var keyboard = Keyboard.current;
            var throttle = 0f;
            var steer = 0f;
            var skid = false;

            if (Driven && keyboard != null)
            {
                throttle = Held(keyboard.wKey, keyboard.upArrowKey) - Held(keyboard.sKey, keyboard.downArrowKey);
                steer = Held(keyboard.dKey, keyboard.rightArrowKey) - Held(keyboard.aKey, keyboard.leftArrowKey);
                skid = keyboard.spaceKey.isPressed;
            }

            ApplySteering(steer, Time.fixedDeltaTime);
            ApplyDrive(throttle, skid);
            ApplyDownforce();
            Stabilize();
        }

        /// <summary>The lean is cosmetic, so it belongs on the render tick, not the physics one.</summary>
        private void Update() => ApplyLean(Time.deltaTime);

        /// <summary>
        /// Eases the front wheel toward the held direction, with less lock available the faster the
        /// bike is going. The tyres do the actual turning — a stationary wheel generates no side
        /// force — so the falloff exists only to stop full lock at 72 km/h from spinning it.
        /// </summary>
        private void ApplySteering(float steer, float dt)
        {
            var speedFraction = Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / Mathf.Max(1f, _spec.MaxSpeed));
            var available = _maxSteerDegrees * Mathf.Lerp(1f, steerAtTopSpeed, speedFraction);
            _steerAngle = Mathf.MoveTowards(_steerAngle, steer * available, steerRate * dt);

            if (frontWheel != null) frontWheel.steerAngle = _steerAngle;
        }

        private void ApplyDrive(float throttle, bool skid)
        {
            var speed = ForwardSpeed;

            // S brakes while rolling forward and reverses once stopped — a brake lever and a gear
            // selector on one key, which is the arcade convention the web build had.
            var braking = throttle < 0f && speed > 0.5f || throttle > 0f && speed < -0.5f;

            var atForwardLimit = speed >= MaxForwardSpeed;
            var atReverseLimit = -speed >= MaxReverseSpeed;
            var capped = throttle > 0f && atForwardLimit || throttle < 0f && atReverseLimit;

            var motor = braking || capped ? 0f : throttle * motorTorque;

            // `capped` gets the coast brake too, and that is not cosmetic: cutting the motor alone
            // leaves whatever overshoot the last physics step produced sitting there forever, since
            // a WheelCollider has no rolling resistance and there is no aero drag at this scale.
            // Measured in Play before this line existed: a 20 m/s cap holding at 22.6 m/s.
            var brake = braking ? brakeTorque
                : capped || Mathf.Approximately(throttle, 0f) ? coastBrake
                : 0f;

            SetTorque(rearWheel, motor, skid ? brakeTorque : brake);
            SetTorque(frontWheel, 0f, brake * frontBrakeShare);

            SetSidewaysStiffness(rearWheel, skid ? _rearGripAtRest * skidGrip : _rearGripAtRest);
        }

        /// <summary>Scales with speed squared, like real aero, so parking is unaffected.</summary>
        private void ApplyDownforce()
        {
            if (downforce <= 0f) return;
            var fraction = Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / Mathf.Max(1f, _spec.MaxSpeed));
            _body.AddForce(-transform.up * (downforce * fraction * fraction * _body.mass * -Physics.gravity.y));
        }

        /// <summary>
        /// Holds the bike upright. Without this a two-wheeled Rigidbody lies down within a second of
        /// being spawned, riderless or not.
        ///
        /// The torque is a spring toward world up, but measured against where the roll will BE in
        /// <see cref="uprightPredict"/> seconds rather than where it is now. That look-ahead is the
        /// damping term: correcting the current error alone makes a pendulum, and the bike wobbles
        /// side to side forever. It deliberately does not cancel yaw — turning is the one rotation
        /// the rider owns — so only the component that squares local up against world up is used.
        /// </summary>
        private void Stabilize()
        {
            var spin = _body.angularVelocity;
            var predictedUp = spin.sqrMagnitude < 1e-6f
                ? transform.up
                : Quaternion.AngleAxis(spin.magnitude * Mathf.Rad2Deg * uprightPredict, spin) * transform.up;

            // Cross(a, b) is the axis that rotates a onto b, scaled by the sine of the angle
            // between them — so it is zero when already upright and grows as the bike goes over.
            _body.AddTorque(Vector3.Cross(predictedUp, Vector3.up) * uprightSpring);
        }

        /// <summary>
        /// Rolls the MODEL into the corner, leaving the Rigidbody upright.
        ///
        /// A real bike leans until gravity and the corner's centripetal force line up along its
        /// wheels, which is <c>tan(lean) = v·ω / g</c> — so the target is read straight off the
        /// speed and the yaw rate the physics already produced, rather than off the steering key.
        /// Steering input alone would lean a stationary bike, and would not lean one sliding
        /// sideways off a handbrake turn.
        /// </summary>
        private void ApplyLean(float dt)
        {
            if (leanPivot == null) return;

            var target = 0f;
            if (_body != null)
            {
                var lateral = _body.angularVelocity.y * ForwardSpeed;
                target = -Mathf.Atan2(lateral, -Physics.gravity.y) * Mathf.Rad2Deg;
                target = Mathf.Clamp(target, -maxLeanDegrees, maxLeanDegrees);
            }

            _lean = Mathf.MoveTowards(_lean, target, leanRate * dt);
            leanPivot.localRotation = Quaternion.Euler(0f, 0f, _lean);
        }

        /// <summary>Motor off, gentle brake on — the safe state when there is nobody to ask.</summary>
        private void Coast()
        {
            SetTorque(frontWheel, 0f, coastBrake);
            SetTorque(rearWheel, 0f, coastBrake);
        }

        private static void SetTorque(WheelCollider wheel, float motor, float brake)
        {
            if (wheel == null) return;
            wheel.motorTorque = motor;
            wheel.brakeTorque = brake;
        }

        private static void SetSidewaysStiffness(WheelCollider wheel, float stiffness)
        {
            if (wheel == null) return;
            var friction = wheel.sidewaysFriction;
            if (Mathf.Approximately(friction.stiffness, stiffness)) return;
            friction.stiffness = stiffness;
            wheel.sidewaysFriction = friction;
        }

        private static float Held(KeyControl primary, KeyControl alternate) =>
            primary.isPressed || alternate.isPressed ? 1f : 0f;

        // --- IChaseTarget ------------------------------------------------------------------------

        public Transform Anchor => transform;

        /// <summary>
        /// <c>config.camera.localOffset</c>, the same boom the cars use — the web build hands the
        /// bike that identical offset, and a second camera feel for one vehicle is a worse game, not
        /// a better one. ModelOffset because three.js hangs a chase camera at <c>+Z</c>.
        /// </summary>
        public Vector3 LocalBoom => _cameraSpec == null
            ? new Vector3(0f, 2.5f, -6.5f)
            : Convert.ModelOffset(_cameraSpec.LocalOffset.Raw);

        public Vector3 LookTarget =>
            transform.position + Vector3.up * (_cameraSpec?.LookYOffset ?? fallbackLookYOffset);

        public float FollowLerp => _cameraSpec?.FollowLerp ?? 0.12f;

        // --- IEnterable --------------------------------------------------------------------------

        /// <summary>Getting on always succeeds; the sequencing lives in VehicleEnterExit.</summary>
        public bool TryEnter() => true;

        /// <summary>Stepping off. Held here so the bike does not roll away with nobody on it.</summary>
        public void Exit()
        {
            if (_body == null) return;
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            Coast();
        }

        /// <summary>
        /// Back to where it was put down, upright and stopped — the `R` key, and the web build's
        /// `resetToSpawn`. It falls back to the config spawn if Bind ran before the spawner did.
        /// </summary>
        public void Respawn()
        {
            if (!_spawnCaptured && _bikeSpec?.Spawn != null)
            {
                var ground = Convert.Pos(_bikeSpec.Spawn.Raw);
                _spawnPosition = new Vector3(ground.x, _bikeSpec.RoadSurfaceY, ground.z);
                _spawnRotation = Quaternion.identity;
                _spawnCaptured = true;
            }

            if (_body != null)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }

            transform.SetPositionAndRotation(_spawnPosition + Vector3.up * 0.1f, _spawnRotation);

            _steerAngle = 0f;
            _lean = 0f;
            if (frontWheel != null) frontWheel.steerAngle = 0f;
            Coast();
            if (leanPivot != null) leanPivot.localRotation = Quaternion.identity;
        }
    }
}
