using TheBlock.Core;
using TheBlock.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// A drivable motorcycle: kinematic speed + heading, like the web build.
    ///
    /// The original <c>src/vehicle/motorcycle.ts</c> runs the same arcade model as the cars:
    /// scalar speed and heading, ground-following ray, collide-and-slide. A two-wheeler is not
    /// a 4-WheelCollider car with 2 wheels deleted (that would tip constantly). The web build's
    /// choice to use kinematic for motorcycles is not scar tissue — it is physics sense.
    ///
    /// Door-less: the rider sits on frame 0 of the Driving clip, parented to the bike
    /// (src/vehicle/seated-rider.ts). Unlike the car, there is no entry animation; E gets in
    /// instantly via the quick-enter fallback in VehicleEnterExit.
    ///
    /// Its forward is <c>+Z</c>. The imported GLB's is not; see <see cref="Convert.ModelFacing"/>.
    /// </summary>
    public class MotorcycleController : MonoBehaviour, IEnterable
    {
        [Header("Cabin — wired by MotorcycleBuilder from config.vehicle.motorcycle.rider")]
        [Tooltip("Where the rider sits. Parented to the bike so it rides along.")]
        [SerializeField] private Transform riderAnchor;

        [Header("Drive — tuned by feel, not ported from config.ts")]
        [SerializeField] private float motorTorque = 1200f;
        [SerializeField] private float brakeTorque = 2500f;
        [SerializeField] private float coastBrake = 400f;

        [Header("Steering")]
        [SerializeField] private float steerRate = 140f; // deg/s, slightly twitchier than a car
        [SerializeField] private float steerAtTopSpeed = 0.40f;

        [Header("Suspension / Ground follow")]
        [Tooltip("Height of the bike's body origin above the road surface it rests on.")]
        [SerializeField] private float rideHeight = 0.5f;

        [Tooltip("Max vertical velocity (m/s) to prevent popping up on obstacles.")]
        [SerializeField] private float maxClimbRate = 3f;

        [Header("Reference")]
        [SerializeField] private Rigidbody rb;
        [SerializeField] private CharacterController characterController;

        // Specs loaded from config at runtime
        private TheBlockConfig.MotorcycleSpec _spec;
        private float _maxSpeed;
        private float _reverseMaxSpeed;
        private float _maxWheelAngle;
        private float _wheelReturn;
        private float _accel;
        private float _brakeDecel;
        private float _friction;
        private float _steerRatio;

        // Drive state
        private float _speed;
        private float _wheel;
        private float _yaw;
        private Vector3 _position;
        private Quaternion _rotation;

        // Ground detection
        private float _groundY;
        private Vector3 _desiredMove;
        private readonly RaycastHit[] _raycastHits = new RaycastHit[1];

        /// <summary>Mounted rider position in holder-local space.</summary>
        public Transform RiderAnchor => riderAnchor;

        /// <summary>Motorcycles have no door.</summary>
        public CarDoor Door => null;

        /// <summary>True while Joe is driving the bike.</summary>
        private bool _driven;
        public bool Driven
        {
            get => _driven;
            set => _driven = value;
        }

        /// <summary>IEnterable interface: get the Transform.</summary>
        public Transform GetTransform() => transform;

        /// <summary>Current heading in radians (0 → facing -Z).</summary>
        public float CurrentYaw => _yaw;

        /// <summary>Current speed in m/s (signed; negative = reverse).</summary>
        public float CurrentSpeed => _speed;

        // --- IChaseTarget implementation ----
        
        /// <summary>The motorcycle's transform — where the camera boom attaches.</summary>
        public Transform Anchor => transform;

        /// <summary>Camera boom offset from the motorcycle.</summary>
        public Vector3 LocalBoom => new Vector3(0f, 1.2f, -8f);

        /// <summary>Point the camera aims at — roughly the rider's head.</summary>
        public Vector3 LookTarget => transform.position + Vector3.up * 1.5f;

        /// <summary>Camera follow smoothing: fraction of remaining distance closed per frame.</summary>
        public float FollowLerp => 0.12f;

        private void Awake()
        {
            rb ??= GetComponent<Rigidbody>();
            characterController ??= GetComponent<CharacterController>();
        }

        private void Start()
        {
            if (!Bind()) return;

            _position = transform.position;
            _yaw = transform.eulerAngles.y * Mathf.Deg2Rad;
            _speed = 0f;
            _wheel = 0f;
            _groundY = _position.y;
        }

        /// <summary>Bind the motorcycle to its config spec. Called at Start and re-called on recompile.</summary>
        private bool Bind()
        {
            var snapshot = TheBlockConfig.Load();
            _spec = snapshot?.Config?.Vehicle?.Motorcycle;
            if (_spec == null)
            {
                Debug.LogError("MotorcycleController: config has no vehicle.motorcycle section.", this);
                return false;
            }

            // Load shared vehicle tuning
            var vehicleSpec = snapshot.Config.Vehicle;
            _maxSpeed = vehicleSpec.MaxSpeed;
            _reverseMaxSpeed = vehicleSpec.ReverseMaxSpeed;
            _maxWheelAngle = vehicleSpec.MaxWheelAngle;
            _wheelReturn = vehicleSpec.WheelReturn;
            _accel = vehicleSpec.Accel;
            _brakeDecel = vehicleSpec.BrakeDecel;
            _friction = vehicleSpec.Friction;
            _steerRatio = vehicleSpec.SteerRatio;

            return true;
        }

        private void Update()
        {
            // Recompile guard: if config went null, rebind
            if (_spec == null && !Bind()) return;

            // Only accept input while being driven
            if (_driven)
            {
                HandleInput();
                UpdatePhysics(Time.deltaTime);
                UpdateTransform();
            }
        }

        private void HandleInput()
        {
            var input = InputSystem.GetDevice<Gamepad>() ?? (InputDevice)Keyboard.current;
            if (input == null) return;

            var fwd = input.IsPressed(Key.W) || input.IsPressed(Key.UpArrow);
            var back = input.IsPressed(Key.S) || input.IsPressed(Key.DownArrow);
            var braking = input.IsPressed(Key.Space);
            var left = input.IsPressed(Key.A) || input.IsPressed(Key.LeftArrow);
            var right = input.IsPressed(Key.D) || input.IsPressed(Key.RightArrow);

            // Throttle/brake
            if (fwd)
                _speed += _accel * Time.deltaTime;
            else if (back)
                _speed -= _accel * Time.deltaTime;
            else
                _speed = Decay(_speed, _friction * Time.deltaTime);

            if (braking)
                _speed = Decay(_speed, _brakeDecel * Time.deltaTime);

            _speed = Mathf.Clamp(_speed, -_reverseMaxSpeed, _maxSpeed);

            // Steering
            var steer = (left ? 1f : 0f) - (right ? 1f : 0f);
            var targetWheel = steer * _maxWheelAngle;
            _wheel += (targetWheel - _wheel) * Mathf.Min(1f, _wheelReturn * Time.deltaTime);
        }

        private void UpdatePhysics(float dt)
        {
            // Ground follow via raycast
            var rayOrigin = _position + Vector3.up * rideHeight * 2f;
            var rayDistance = rideHeight * 3f;

            Physics.RaycastNonAlloc(
                rayOrigin,
                Vector3.down,
                _raycastHits,
                rayDistance,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore
            );

            if (_raycastHits[0].collider != null)
            {
                _groundY = _raycastHits[0].point.y + rideHeight;
            }
            else
            {
                // No surface found; clamp to config ground plane
                _groundY = Mathf.Max(_position.y, TheBlockConfig.Load().Config.Ground.Y + rideHeight);
            }

            // Heading update
            var forwardDelta = _speed * dt;
            _yaw += forwardDelta * _steerRatio * _wheel;

            // Movement in world space
            var dx = -Mathf.Sin(_yaw) * forwardDelta;
            var dz = -Mathf.Cos(_yaw) * forwardDelta;
            _desiredMove = new Vector3(dx, 0, dz);

            // Simple collision sweep (placeholder; real implementation would use CharacterController.computeColliderMovement)
            // For now, just move directly
            _position += _desiredMove;

            // Vertical follow with clamp
            var targetY = _groundY;
            var maxStep = maxClimbRate * dt;
            _position.y += Mathf.Clamp(targetY - _position.y, -maxStep, maxStep);

            _rotation = Quaternion.AngleAxis(_yaw * Mathf.Rad2Deg, Vector3.up);
        }

        private void UpdateTransform()
        {
            transform.position = _position;
            transform.rotation = _rotation;
        }

        /// <summary>Decay a value by amount, clamping to zero if close enough.</summary>
        private static float Decay(float value, float amount)
        {
            if (Mathf.Abs(value) <= amount)
                return 0f;
            return value - Mathf.Sign(value) * amount;
        }

        /// <summary>Called by VehicleEnterExit when E is pressed.</summary>
        public bool TryEnter()
        {
            return true; // Door-less; always allow entry via the quick-enter path
        }

        /// <summary>Called by VehicleEnterExit when E is pressed while driving.</summary>
        public void Exit()
        {
            _speed = 0f;
        }

        /// <summary>Respawn to configured spawn (R key).</summary>
        public void Respawn()
        {
            var spawnPos = Convert.Pos(_spec.Spawn.Raw);
            _position = new Vector3(spawnPos.x, _spec.RoadSurfaceY + rideHeight, spawnPos.z);
            _speed = 0f;
            _yaw = 0f;
            _wheel = 0f;
            _groundY = _position.y;
        }
    }
}
