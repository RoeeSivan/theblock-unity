using TheBlock.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

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
    public class CarController : MonoBehaviour, IChaseTarget
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

        /// <summary>Signed speed along the car's nose in m/s. Negative is reversing.</summary>
        public float ForwardSpeed => Vector3.Dot(_body.linearVelocity, transform.forward);

        /// <summary>For the U25 HUD, and for U19's "is this a getaway" test.</summary>
        public float SpeedKmh => Mathf.Abs(ForwardSpeed) * 3.6f;

        private void Awake() => Bind();

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

            var keyboard = Keyboard.current;
            var throttle = 0f;
            var steer = 0f;
            var handbrake = false;

            if (Driven && keyboard != null)
            {
                throttle = Held(keyboard.wKey, keyboard.upArrowKey) - Held(keyboard.sKey, keyboard.downArrowKey);
                steer = Held(keyboard.dKey, keyboard.rightArrowKey) - Held(keyboard.aKey, keyboard.leftArrowKey);
                handbrake = keyboard.spaceKey.isPressed;
            }

            ApplySteering(steer, Time.fixedDeltaTime);
            ApplyDrive(throttle, handbrake);
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

            var atForwardLimit = speed >= _spec.MaxSpeed;
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

        private static float Held(KeyControl primary, KeyControl alternate) =>
            primary.isPressed || alternate.isPressed ? 1f : 0f;

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
    }
}
