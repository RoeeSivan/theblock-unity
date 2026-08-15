using System.Linq;
using TheBlock.Core;
using UnityEngine;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// A drivable car: a Rigidbody on four WheelColliders.
    ///
    /// This is NOT a port of <c>src/vehicle/vehicle.ts</c>. That car is kinematic — a scalar speed
    /// and a heading, pushed through a Rapier character controller, with a ray under it snapping the
    /// body to the road every frame. It is a workaround for Rapier's vehicle controller being
    /// unusable in that project, and CLAUDE.md port rule 5 says scar tissue does not carry over.
    /// PhysX gives real suspension, real tyre friction and real momentum, which is what U17's
    /// traffic, U18's run-over and U19's ramming all want to inherit.
    ///
    /// What DOES carry over is gameplay: the 20 m/s cap, the 7 m/s reverse, the ~34° steering lock,
    /// and tank-free steering that only bites while the car is rolling. Everything else on this
    /// component is a PhysX number derived by feel, per port rule 2 — none of it came from config.ts,
    /// and none of it should be compared against config.ts.
    ///
    /// Its forward is <c>+Z</c>. The imported GLB's is not; see <see cref="Convert.ModelFacing"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CarController : MonoBehaviour, IEnterable
    {
        [Header("Wheels — front pair steers, rear pair drives")]
        [SerializeField] private WheelCollider frontLeft;
        [SerializeField] private WheelCollider frontRight;
        [SerializeField] private WheelCollider rearLeft;
        [SerializeField] private WheelCollider rearRight;

        [Header("Drive — PhysX numbers, derived by feel (port rule 2)")]
        [Tooltip("Nm per driven wheel. 1600 over a 0.38 m wheel is ~8.4 kN of thrust, ~6 m/s^2 " +
                 "on a 1400 kg car — 0-72 km/h in about 3.5 s.")]
        [SerializeField] private float motorTorque = 1600f;

        [Tooltip("Nm per wheel when braking. Well above motor torque so S beats W.")]
        [SerializeField] private float brakeTorque = 3000f;

        [Tooltip("Nm on the rear wheels for the handbrake, paired with the grip cut below.")]
        [SerializeField] private float handbrakeTorque = 4500f;

        [Tooltip("Rear sideways grip multiplier while the handbrake is down. Under 1 lets the " +
                 "back step out; 1 makes the handbrake a plain rear brake.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float handbrakeGrip = 0.45f;

        [Tooltip("Nm on every wheel with no key held. A WheelCollider has no rolling resistance and " +
                 "PhysX applies no drag worth the name, so without this the car coasts forever. " +
                 "Stands in for engine braking and tyre losses; the web build called it `friction`.")]
        [SerializeField] private float coastBrake = 450f;

        [Header("Steering")]
        [Tooltip("Degrees per second the front wheels swing toward the held direction.")]
        [SerializeField] private float steerRate = 120f;

        [Tooltip("Steering lock left at top speed, as a fraction. Full lock at 72 km/h spins the " +
                 "car; this is the speed-sensitive falloff that stops it.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float steerAtTopSpeed = 0.35f;

        [Header("Stability")]
        [Tooltip("Centre of mass, in the car's local frame, measured from the body origin. Low and " +
                 "slightly back. This is the single biggest anti-roll knob on a WheelCollider car.")]
        [SerializeField] private Vector3 centerOfMass = new Vector3(0f, 0.35f, -0.1f);

        [Tooltip("Downward force at top speed, in multiples of the car's weight. Keeps the wheels " +
                 "loaded over crests so it does not take off on a curb.")]
        [SerializeField] private float downforce = 0.6f;

        [Header("Cabin — wired by CarBuilder from config.vehicle.driver / .cars[].door")]
        [Tooltip("Where the driver's entry animation starts: beside the door, at road level. The " +
                 "clip's own travel carries him from here into the seat.")]
        [SerializeField] private Transform driverAnchor;

        [SerializeField] private CarDoor door;

        [Header("Who may take the wheel")]
        [Tooltip("Whether E can enter this car. Off for police cruisers — see OnEnable.")]
        [SerializeField] private bool enterable = true;

        [Header("Camera")]
        [Tooltip("Look point height above the body origin, if config.camera.lookYOffset is missing.")]
        [SerializeField] private float fallbackLookYOffset = 0.5f;

        private Rigidbody _body;
        private TheBlockConfig.VehicleSpec _spec;
        private TheBlockConfig.CameraSpec _cameraSpec;
        private float _steerAngle;
        private float _maxSteerDegrees = 34.4f;

        /// <summary>Rest sideways stiffness of the rear tyres, so the handbrake can put it back.</summary>
        private float _rearGripAtRest = 1f;

        /// <summary>False while nobody is at the wheel: the car sits there and ignores the keyboard.</summary>
        public bool Driven { get; set; }

        /// <summary>
        /// Raises this car's forward speed ceiling above <c>config.vehicle.maxSpeed</c>. Zero, the
        /// default, means the config's own limit — which is what every player-driven car uses.
        ///
        /// <b>It exists for one caller.</b> A police car answering a call is allowed to cover ground
        /// faster than traffic; a police car ON you is not, because "a cop cannot corner or accelerate
        /// in a way your car could not" is the whole reason the AI writes <see cref="CarInput"/>
        /// instead of moving a transform. <c>CopDriver</c> raises it only while responding — beyond
        /// the rubber band with no line of sight — and drops it the moment it can see you.
        ///
        /// Worth knowing before touching this: the config cap is <b>20 m/s for every car in the
        /// game</b>, so <c>PoliceTuning.MaxSpeed</c>'s documented "20.5, a 2.5% edge over the player"
        /// was never reachable. The torque was being cut at 20 the whole time.
        /// </summary>
        public float SpeedLimitOverride { get; set; }

        private float MaxForwardSpeed =>
            SpeedLimitOverride > 0f ? Mathf.Max(SpeedLimitOverride, _spec.MaxSpeed) : _spec.MaxSpeed;

        /// <summary>
        /// How long a <see cref="SetInput"/> call stays in force, seconds. Long enough to survive a
        /// dropped frame, far shorter than the time it takes a coasting car to become a problem.
        /// </summary>
        private const float ExternalInputTimeout = 0.5f;

        private CarInput _input;
        private float _sinceInput = float.MaxValue;

        /// <summary>
        /// Drive this car from something other than the keyboard — U19's <c>CopDriver</c>, and
        /// anything else that ever needs to. Call it every FixedUpdate: it expires (see
        /// <see cref="ExternalInputTimeout"/>) so a driver that goes away leaves a coasting car and
        /// not a runaway one.
        /// </summary>
        public void SetInput(in CarInput input)
        {
            _input = input;
            _sinceInput = 0f;
        }

        /// <summary>The chassis body, for the teleports and velocity resets a pursuit does.</summary>
        public Rigidbody Body => _body;

        /// <summary>IEnterable interface: get the Transform.</summary>
        public Transform GetTransform() => transform;

        /// <summary>
        /// Where the driver is parented on the way in, or null on a car with no seat block in
        /// <c>config.vehicle.driver.seats</c> — which is the signal to use the quick enter instead.
        /// </summary>
        public Transform DriverAnchor => driverAnchor;

        /// <summary>IEnterable interface: same as DriverAnchor.</summary>
        public Transform RiderAnchor => driverAnchor;

        /// <summary>The driver's door, or null if this model's rig has no door joint.</summary>
        public CarDoor Door => door;

        /// <summary>The car's left, which is the side the driver's door is on.</summary>
        public Vector3 DriverSide => -transform.right;

        /// <summary>Signed speed along the car's nose in m/s. Negative is reversing.</summary>
        public float ForwardSpeed => Vector3.Dot(_body.linearVelocity, transform.forward);

        /// <summary>For the U25 HUD, and for U19's "is this a getaway" test.</summary>
        public float SpeedKmh => Mathf.Abs(ForwardSpeed) * 3.6f;

        private void Awake() => Bind();

        /// <summary>
        /// The car puts itself on the enterable list rather than being put there by its spawner: a
        /// spawner has no idea when its car is destroyed, and a stale entry means `E` aims at a
        /// corpse. See <see cref="EnterableRegistry"/>.
        ///
        /// Off on a police cruiser: <c>VehicleEnterExit.TryEnter</c> walks that registry and takes
        /// the nearest thing on it, so a cop car that registers is a cop car you can steal by
        /// standing next to it. Stealing one is a fine idea and belongs to a later unit; getting it
        /// by accident, mid-arrest, is not.
        /// </summary>
        private void OnEnable()
        {
            if (enterable) EnterableRegistry.Register(this);
        }

        private void OnDisable() => EnterableRegistry.Unregister(this);

        /// <summary>
        /// Reads the config and caches what the drive loop needs.
        ///
        /// Called from Awake, and again from FixedUpdate if the spec has gone null — which happens
        /// when scripts recompile while the Editor is in Play mode: the domain reloads, every field
        /// that is not serializable comes back null, and Awake does NOT run again. PlayerController
        /// carries the same guard for the same reason.
        /// </summary>
        private void Bind()
        {
            _body = GetComponent<Rigidbody>();
            _body.centerOfMass = centerOfMass;

            // Non-serialized, so a mid-Play recompile brings it back as 0 — which would read as
            // "an AI wrote input this instant" and lock out the keyboard for half a second.
            _sinceInput = float.MaxValue;

            var snapshot = TheBlockConfig.Load();
            if (snapshot?.Config?.Vehicle == null)
            {
                Debug.LogError("CarController: config has no `vehicle` section.", this);
                enabled = false;
                return;
            }

            _spec = snapshot.Config.Vehicle;
            _cameraSpec = snapshot.Config.Camera;
            _maxSteerDegrees = _spec.MaxWheelAngle * Mathf.Rad2Deg;

            if (rearLeft != null) _rearGripAtRest = rearLeft.sidewaysFriction.stiffness;

            // Sub-stepping is what keeps a WheelCollider honest at speed: with the default step
            // count the contact patch skips over geometry and the car climbs kerbs it should hit.
            // Applied to any one wheel, it configures the whole vehicle.
            if (frontLeft != null) frontLeft.ConfigureVehicleSubsteps(5f, 12, 15);
        }

        private void FixedUpdate()
        {
            if (_spec == null) Bind();
            if (_spec == null)
            {
                // Cut the drive before giving up. A WheelCollider HOLDS the last torque it was
                // given, so simply returning here leaves full throttle latched on and the car
                // accelerates away with nothing left running to cap it — which is exactly what a
                // mid-Play recompile produced: a 161 km/h Mustang against a 72 km/h limit.
                Coast();
                return;
            }

            // Whoever wrote last, wins — but only while their input is FRESH. An AI driver that
            // stops writing (its component destroyed, the cop despawned mid-corner) falls back to
            // CarInput.None, which is the coast brake, rather than leaving the last throttle latched
            // in the WheelColliders. That failure is not hypothetical: it is the 161 km/h Mustang
            // above, arrived at from the other direction.
            _sinceInput += Time.fixedDeltaTime;
            var input = _sinceInput <= ExternalInputTimeout ? _input
                : Driven ? PlayerCarInput.Read()
                : CarInput.None;

            ApplySteering(input.Steer, Time.fixedDeltaTime);
            ApplyDrive(input.Throttle, input.Handbrake);
            ApplyDownforce();
        }

        /// <summary>
        /// Eases the front wheels toward the held direction, with less lock available the faster the
        /// car is going.
        ///
        /// The web build reached the same place from the other end: it turned the heading by
        /// <c>distance travelled * wheel angle</c>, so standing still it could not turn at all. Here
        /// the tyres do that part on their own — a stationary wheel generates no side force — and
        /// the falloff exists only to stop full lock at 72 km/h from spinning the car.
        /// </summary>
        private void ApplySteering(float steer, float dt)
        {
            var speedFraction = Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / Mathf.Max(1f, _spec.MaxSpeed));
            var available = _maxSteerDegrees * Mathf.Lerp(1f, steerAtTopSpeed, speedFraction);
            _steerAngle = Mathf.MoveTowards(_steerAngle, steer * available, steerRate * dt);

            if (frontLeft != null) frontLeft.steerAngle = _steerAngle;
            if (frontRight != null) frontRight.steerAngle = _steerAngle;
        }

        private void ApplyDrive(float throttle, bool handbrake)
        {
            var speed = ForwardSpeed;

            // S means brake while rolling forward and reverse once stopped — one key doing what a
            // brake pedal and a gear selector do, which is the arcade convention the web build had.
            var braking = throttle < 0f && speed > 0.5f || throttle > 0f && speed < -0.5f;

            var atForwardLimit = speed >= MaxForwardSpeed;
            var atReverseLimit = -speed >= _spec.ReverseMaxSpeed;
            var capped = throttle > 0f && atForwardLimit || throttle < 0f && atReverseLimit;

            var motor = braking || capped ? 0f : throttle * motorTorque;
            var brake = braking ? brakeTorque
                : Mathf.Approximately(throttle, 0f) ? coastBrake
                : 0f;

            // Rear-wheel drive, like the car it is.
            SetTorque(rearLeft, motor, handbrake ? handbrakeTorque : brake);
            SetTorque(rearRight, motor, handbrake ? handbrakeTorque : brake);
            SetTorque(frontLeft, 0f, brake);
            SetTorque(frontRight, 0f, brake);

            SetRearGrip(handbrake ? _rearGripAtRest * handbrakeGrip : _rearGripAtRest);
        }

        /// <summary>Scales with speed squared, like real aero, so parking is unaffected.</summary>
        private void ApplyDownforce()
        {
            if (downforce <= 0f) return;
            var fraction = Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / Mathf.Max(1f, _spec.MaxSpeed));
            _body.AddForce(-transform.up * (downforce * fraction * fraction * _body.mass * -Physics.gravity.y));
        }

        /// <summary>Motor off, gentle brake on — the safe state when there is nobody to ask.</summary>
        private void Coast()
        {
            SetTorque(frontLeft, 0f, coastBrake);
            SetTorque(frontRight, 0f, coastBrake);
            SetTorque(rearLeft, 0f, coastBrake);
            SetTorque(rearRight, 0f, coastBrake);
        }

        private static void SetTorque(WheelCollider wheel, float motor, float brake)
        {
            if (wheel == null) return;
            wheel.motorTorque = motor;
            wheel.brakeTorque = brake;
        }

        private void SetRearGrip(float stiffness)
        {
            SetSidewaysStiffness(rearLeft, stiffness);
            SetSidewaysStiffness(rearRight, stiffness);
        }

        private static void SetSidewaysStiffness(WheelCollider wheel, float stiffness)
        {
            if (wheel == null) return;
            var friction = wheel.sidewaysFriction;
            if (Mathf.Approximately(friction.stiffness, stiffness)) return;
            friction.stiffness = stiffness;
            wheel.sidewaysFriction = friction;
        }

        // --- IChaseTarget ----------------------------------------------------------------------

        public Transform Anchor => transform;

        /// <summary>
        /// <c>config.camera.localOffset</c> — the VEHICLE boom, not the on-foot one, which lives
        /// under <c>config.player.camera</c>. ModelOffset for the usual reason: three.js hangs a
        /// chase camera at <c>+Z</c> because it drives down <c>-Z</c>.
        /// </summary>
        public Vector3 LocalBoom => _cameraSpec == null
            ? new Vector3(0f, 2.5f, -6.5f)
            : Convert.ModelOffset(_cameraSpec.LocalOffset.Raw);

        public Vector3 LookTarget =>
            transform.position + Vector3.up * (_cameraSpec?.LookYOffset ?? fallbackLookYOffset);

        public float FollowLerp => _cameraSpec?.FollowLerp ?? 0.12f;

        // --- IEnterable implementation -------------------------------------------------------

        /// <summary>Try to enter the car (always succeeds; entry logic is in VehicleEnterExit).</summary>
        public bool TryEnter() => true;

        /// <summary>Exit the car (handled by VehicleEnterExit; this is a pass-through).</summary>
        public void Exit() { }

        /// <summary>
        /// Where <see cref="Respawn"/> puts this car back. Set by <see cref="CarSpawner"/> at spawn,
        /// which is the only place that knows which config entry this instance came from.
        ///
        /// Serialized and hidden so it survives a mid-Play recompile: a plain private field comes
        /// back zeroed after the domain reload, and R would then teleport the car to the origin.
        /// </summary>
        [SerializeField, HideInInspector] private bool hasHome;
        [SerializeField, HideInInspector] private Vector3 homePosition;
        [SerializeField, HideInInspector] private Quaternion homeRotation = Quaternion.identity;

        /// <summary>Called by <see cref="CarSpawner"/> immediately after Instantiate.</summary>
        public void BindHome(string specName, Vector3 position, Quaternion rotation)
        {
            name = specName;
            homePosition = position;
            homeRotation = rotation;
            hasHome = true;
        }

        /// <summary>
        /// Teleport back to this car's own spawn, upright and stopped.
        ///
        /// <b>Its own.</b> This used to take <c>cars.FirstOrDefault()</c> — the first entry in the
        /// config, whichever car pressed R — and to teleport to the raw config point, which carries
        /// no Y, dropping the car through the road to y = 0. The spawn position is now the same
        /// ground-probed answer <see cref="CarSpawner"/> computed, handed over at spawn.
        ///
        /// The order below is the whole point:
        ///  1. stop the body BEFORE moving it, or the old velocity is still applied on the next step;
        ///  2. <c>Physics.SyncTransforms</c> — without it the WheelColliders keep their pre-teleport
        ///     world poses for a frame, and <see cref="CarWheel"/> then poses four bones at the place
        ///     the car just left while the body is here. On one skin that draws as a wedge stretched
        ///     between the two positions, which is exactly the artefact R was reported for;
        ///  3. brake all four, not just zero the motor — a WheelCollider LATCHES the last brake
        ///     torque it was given, so zeroing the motor alone can leave the car held or rolling;
        ///  4. re-pose the wheels now that the colliders agree with the body.
        /// </summary>
        public void Respawn()
        {
            var position = homePosition;
            var rotation = homeRotation;

            if (!hasHome)
            {
                // Never spawned by CarSpawner (dropped into the scene by hand, or a test). Ask the
                // config for the entry that matches this object's name — never "the first one".
                var cars = TheBlockConfig.Load()?.Config?.Vehicle?.Cars;
                var spec = cars?.FirstOrDefault(c =>
                    string.Equals(c.Name, name, System.StringComparison.OrdinalIgnoreCase));

                if (spec?.Spawn == null)
                {
                    // Nothing to go home to: at least stand it up where it is, which is what the
                    // key is for after a roll.
                    position = transform.position;
                    rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                }
                else
                {
                    position = CarSpawner.Settle(spec, 20f, 0.05f, this);
                    rotation = Convert.RotFromRadians(spec.SpawnYaw ?? 0f);
                }
            }

            Teleport(position, rotation);
        }

        /// <summary>
        /// Puts this car somewhere else, stopped and upright — the four steps <see cref="Respawn"/>
        /// documents above, which U19's bust needs as well and must not re-derive.
        /// </summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;

            transform.SetPositionAndRotation(position, rotation);
            Physics.SyncTransforms();

            _steerAngle = 0f;
            foreach (var wheel in new[] { frontLeft, frontRight, rearLeft, rearRight })
            {
                if (wheel == null) continue;
                wheel.steerAngle = 0f;
                SetTorque(wheel, 0f, brakeTorque);
            }

            foreach (var wheel in GetComponentsInChildren<CarWheel>(true)) wheel.Pose();
        }
    }
}
