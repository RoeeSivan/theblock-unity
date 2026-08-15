using TheBlock.Core;
using TheBlock.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace TheBlock.Player
{
    /// <summary>The locomotion state the animator reads. Ordered walk → jog → sprint by speed.</summary>
    public enum Gait
    {
        Idle,
        Walk,
        Jog,
        Sprint,

        /// <summary>Stamina hit zero — panting, locked out of sprint until it recovers.</summary>
        Exhausted,
    }

    /// <summary>The transient poses that outrank locomotion, highest priority first.</summary>
    public enum Pose
    {
        Locomotion,
        Jump,
        Falling,

        /// <summary>In deep water. Outranks every other pose — jump and falling are inert in the sea.</summary>
        Swim,
    }

    /// <summary>
    /// On-foot movement, ported from the web build's <c>src/player/player.ts</c>.
    ///
    /// Tank controls, deliberately: A/D turn the body and W/S drive along whatever it faces. That
    /// is the original's design, not a workaround for anything three.js could not do, so it carries
    /// over unchanged. The camera follows the body rather than steering it.
    ///
    /// Unity's CharacterController replaces the web build's kinematic Rapier capsule plus
    /// hand-rolled collide-and-slide — same behaviour, one component. Speeds, stamina and the gait
    /// tiers are gameplay tuning and port straight across; nothing here is a Rapier physics number.
    ///
    /// Animation is U7's job. This component only publishes what it is doing.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour, IChaseTarget
    {
        [Header("Spawn")]
        [Tooltip("Spawn at config.player.spawn — the parking lot. Leave OFF until that district " +
                 "is ingested (U11) and there is ground under it, or the player falls forever.")]
        [SerializeField] private bool useConfigSpawn;

        [Header("Feel — not in config.ts, Unity-side additions")]
        [Tooltip("How high a step the controller walks up without jumping. Florentin curbs are ~0.15 m.")]
        [SerializeField] private float stepOffset = 0.35f;

        [SerializeField] private float slopeLimit = 50f;

        [Tooltip("Downward speed held while grounded so the controller keeps hugging the ground.")]
        [SerializeField] private float groundedStickSpeed = -2f;

        private CharacterController _controller;
        private TheBlockConfig.PlayerSpec _spec;
        private TheBlockConfig.SeaSpec _sea;
        private float _gravity;

        private float _stamina;
        private bool _exhausted;
        private float _verticalSpeed;
        private bool _jumping;
        private bool _swimming;

        /// <summary>
        /// Where the capsule's centre sits above its origin. The config's swim target is a CENTRE
        /// height and Unity's transform is at the feet, so this is the difference between the two.
        /// </summary>
        private float _capsuleCenterY;

        /// <summary>Locomotion tier this frame. U7's animator reads this.</summary>
        public Gait CurrentGait { get; private set; } = Gait.Idle;

        /// <summary>Transient pose this frame; outranks <see cref="CurrentGait"/> when not Locomotion.</summary>
        public Pose CurrentPose { get; private set; } = Pose.Locomotion;

        /// <summary>
        /// Horizontal speed in m/s that the player is ASKING for this frame, not what it achieved.
        /// Intent is the right input for the animator: pressed against a wall the achieved speed
        /// drops to zero, and blending back to idle there would look like a bug, not a collision.
        /// </summary>
        public float PlanarSpeed { get; private set; }

        /// <summary>Fires on the frame a jump leaves the ground. The animator listens for this.</summary>
        public event System.Action Jumped;

        /// <summary>Sprint budget in [0,1], for the U25 HUD.</summary>
        public float StaminaFraction => _spec == null ? 1f : _stamina / _spec.Stamina.Max;

        /// <summary>True while stamina is spent and sprint is locked out.</summary>
        public bool Exhausted => _exhausted;

        public bool IsGrounded => _controller != null && _controller.isGrounded;

        /// <summary>True while the water is deep enough to swim in. The animator reads this.</summary>
        public bool IsSwimming => _swimming;

        /// <summary>Where the follow camera should look: roughly the player's head.</summary>
        public Vector3 LookTarget =>
            transform.position + Vector3.up * (_spec?.Camera?.LookYOffset ?? 1.8f);

        // --- IChaseTarget ----------------------------------------------------------------------

        public Transform Anchor => transform;

        /// <summary>
        /// ModelOffset, not Pos: this is an offset in the player's own frame, and three.js faces
        /// -Z where Unity faces +Z. Through Pos() the camera would sit in Joe's face.
        /// </summary>
        public Vector3 LocalBoom =>
            _spec?.Camera == null ? new Vector3(0f, 2f, -2.5f) : Convert.ModelOffset(_spec.Camera.Offset.Raw);

        public float FollowLerp => _spec?.Camera?.FollowLerp ?? 0.25f;

        private void Awake() => Bind();

        /// <summary>
        /// Reads the config and shapes the capsule from it.
        ///
        /// Called from Awake, and again from Update if the spec has gone null. That happens when
        /// scripts recompile while the Editor is in Play mode: the domain reloads, every field that
        /// is not serializable comes back null, and Awake does NOT run again. Without this the
        /// controller throws once per frame for the rest of the session.
        /// </summary>
        private void Bind()
        {
            _controller = GetComponent<CharacterController>();

            var snapshot = TheBlockConfig.Load();
            if (snapshot?.Config?.Player == null)
            {
                Debug.LogError("PlayerController: config has no `player` section.", this);
                enabled = false;
                return;
            }

            _spec = snapshot.Config.Player;
            _sea = snapshot.Config.Sea;
            _gravity = snapshot.Config.Gravity.Y;
            _stamina = _spec.Stamina.Max;

            // The capsule's origin is at the feet, so its centre sits half a body up.
            _capsuleCenterY = _spec.Collider.HalfHeight + _spec.Collider.Radius;
            _controller.radius = _spec.Collider.Radius;
            _controller.height = _capsuleCenterY * 2f;
            _controller.center = new Vector3(0f, _capsuleCenterY, 0f);
            _controller.skinWidth = _spec.CharacterOffset;
            _controller.stepOffset = stepOffset;
            _controller.slopeLimit = slopeLimit;

            // The shore wall stops cars driving out to sea, and the swimmer has to be the one thing
            // that walks through it — which is exactly what the web build's `obstacleFilter` does.
            // WorldBuilder puts that wall, and only that wall, on Ignore Raycast; excluding the layer
            // here drops it out of this capsule's collisions while leaving everything else's alone.
            _controller.excludeLayers = 1 << LayerMask.NameToLayer("Ignore Raycast");

            if (useConfigSpawn)
            {
                _controller.enabled = false;
                transform.position = Convert.Pos(_spec.Spawn.Raw);
                _controller.enabled = true;
            }
        }

        private void Update()
        {
            if (_spec == null) Bind();
            if (_spec == null) return;

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            var dt = Time.deltaTime;

            // Swim state is decided by POSITION, before anything reads it. Entering cancels whatever
            // vertical momentum the walk down the seabed built up, so hitting deep water does not
            // punch the body under and let the spring lob it back out.
            var wasSwimming = _swimming;
            _swimming = SeaGeometry.IsSwimming(_sea, transform.position);
            if (_swimming && !wasSwimming)
            {
                _verticalSpeed = 0f;
                _jumping = false;
            }

            // Turning is pure yaw; it steers the forward vector rather than the velocity. Positive
            // is clockwise seen from above, so D turns right. The three.js source writes the same
            // intent with the opposite sign because +Y there is counter-clockwise.
            var turn = Held(keyboard.dKey, keyboard.rightArrowKey) - Held(keyboard.aKey, keyboard.leftArrowKey);
            if (turn != 0f)
                transform.Rotate(Vector3.up, turn * _spec.TurnSpeed * Mathf.Rad2Deg * dt, Space.World);

            var moveDir = Held(keyboard.wKey, keyboard.upArrowKey) - Held(keyboard.sKey, keyboard.downArrowKey);
            var moving = !Mathf.Approximately(moveDir, 0f);

            var wantSprint = keyboard.shiftKey.isPressed && moving && !_exhausted && _stamina > 0f;
            var wantJog = keyboard.altKey.isPressed && moving;
            UpdateStamina(wantSprint, wantJog, dt);

            // One speed in the water. Gait tiers do not apply — there is no sprinting a crawl stroke,
            // and stamina keeps ticking back up while you swim.
            var speed = _swimming ? _sea.Swim.Speed
                : wantSprint ? _spec.Movement.SprintSpeed
                : wantJog ? _spec.Movement.JogSpeed
                : _spec.Movement.WalkSpeed;

            // Grounded check happens before Move(), so it describes last frame — which is exactly
            // what the jump wants: you may jump on the frame you land, not the frame you leave.
            // Both of these are land-only: in water the seabed is 3 m down and grounded is false
            // anyway, but the stick speed would fight the buoyancy spring on the way out.
            var grounded = !_swimming && _controller.isGrounded;
            if (grounded && _verticalSpeed <= 0f)
            {
                // A small constant downward push, not zero: CharacterController only reports
                // grounded while it is actually being driven into the floor.
                _verticalSpeed = groundedStickSpeed;
                _jumping = false;
            }

            if (grounded && keyboard.spaceKey.wasPressedThisFrame)
            {
                _verticalSpeed = _spec.JumpForce;
                _jumping = true;
                Jumped?.Invoke();
            }

            if (_swimming) Float(dt);
            else _verticalSpeed += _gravity * dt;

            PlanarSpeed = Mathf.Abs(moveDir) * speed;

            var velocity = transform.forward * (moveDir * speed);
            velocity.y = _verticalSpeed;
            _controller.Move(velocity * dt);

            UpdatePose(moving, wantSprint, wantJog);
        }

        /// <summary>
        /// Buoyancy instead of gravity: a spring that pulls the capsule's centre to the surface, and
        /// a damper so it settles there rather than bobbing forever.
        ///
        /// <c>swim.surfaceY</c> is a CENTRE height, so the feet target is that much lower — miss the
        /// offset and Joe floats with the waterline at his knees.
        ///
        /// The web build multiplies by <c>damping</c> once per frame, which silently ties how fast
        /// the bob settles to the frame rate. Raising it to the frame time is the same curve at 60
        /// fps and the same curve everywhere else too.
        /// </summary>
        private void Float(float dt)
        {
            var swim = _sea.Swim;
            var targetFeetY = swim.SurfaceY - _capsuleCenterY;
            _verticalSpeed += (targetFeetY - transform.position.y) * swim.Buoyancy * dt;
            _verticalSpeed *= Mathf.Pow(swim.Damping, dt * 60f);
        }

        private void UpdateStamina(bool sprinting, bool jogging, float dt)
        {
            var stamina = _spec.Stamina;
            if (sprinting)
            {
                _stamina = Mathf.Max(0f, _stamina - stamina.DrainPerSec * dt);
                if (_stamina <= 0f) _exhausted = true;
                return;
            }

            var regen = jogging ? stamina.JogRegenPerSec : stamina.RegenPerSec;
            _stamina = Mathf.Min(stamina.Max, _stamina + regen * dt);
            if (_exhausted && _stamina >= stamina.ResumeThreshold) _exhausted = false;
        }

        /// <summary>
        /// Pose priority: swimming beats falling beats jumping beats locomotion. Falling only wins
        /// once the drop is faster than a hop ever reaches, so it fires off a ledge and never on a
        /// jump — and never in water, where the body is never grounded and would otherwise read as
        /// a permanent fall.
        /// </summary>
        private void UpdatePose(bool moving, bool sprinting, bool jogging)
        {
            if (_swimming)
                CurrentPose = Pose.Swim;
            else if (!_controller.isGrounded && _verticalSpeed < -_spec.FallSpeedThreshold)
                CurrentPose = Pose.Falling;
            else if (_jumping)
                CurrentPose = Pose.Jump;
            else
                CurrentPose = Pose.Locomotion;

            CurrentGait = sprinting ? Gait.Sprint
                : jogging ? Gait.Jog
                : moving ? Gait.Walk
                : _exhausted ? Gait.Exhausted
                : Gait.Idle;
        }

        private static float Held(KeyControl primary, KeyControl alternate) =>
            primary.isPressed || alternate.isPressed ? 1f : 0f;
    }
}
