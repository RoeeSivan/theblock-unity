using UnityEngine;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// Swings one rigged joint of a car - the driver's door.
    ///
    /// The GLB ships a real armature whose joints already sit at the correct hinge positions, so
    /// this rotates the bone directly and the skinned mesh follows. There is no pivot wrapper and
    /// the joint is never detached from the skeleton, which is the same arrangement
    /// <see cref="CarWheel"/> uses for the wheels. The rest pose is captured at Awake and every
    /// target is relative to it, so "shut" returns exactly to the rig's own zero.
    ///
    /// Generic on purpose: the axis and angle are serialized, so the other door, the hood or the
    /// boot is the same component with different numbers. <c>CarBuilder</c> fills them in from
    /// <c>config.vehicle.cars[].door</c>, converted once through <see cref="Core.Convert.ModelAxis"/>.
    /// </summary>
    public class CarDoor : MonoBehaviour
    {
        [Tooltip("The armature joint to swing. Named by config.vehicle.cars[].door.jointName.")]
        [SerializeField] private Transform hinge;

        [Tooltip("Hinge axis in the JOINT's own local frame, already in Unity space.")]
        [SerializeField] private Vector3 openAxis = Vector3.forward;

        [Tooltip("How far it swings, in degrees, from the rig's rest pose.")]
        [SerializeField] private float openAngle = -65f;

        [Tooltip("Exponential smoothing rate - higher is snappier. Frame-rate independent.")]
        [SerializeField] private float lerpRate = 8f;

        private Quaternion _rest;
        private Quaternion _open;
        private bool _bound;

        /// <summary>True while the door is being held open. Says nothing about how far it has swung.</summary>
        public bool IsOpen { get; private set; }

        private void Awake() => Bind();

        /// <summary>
        /// Captures the rest pose and precomputes the open pose.
        ///
        /// Called from Awake and again from Update if a mid-Play recompile wiped the cached
        /// quaternions - the same guard <c>CarController</c> and <c>PlayerController</c> carry, for
        /// the same reason: the domain reloads, non-serialized fields come back empty, and Awake
        /// does not run again. Here that would leave the door lerping toward an identity rotation
        /// and tearing the door off the car.
        /// </summary>
        private void Bind()
        {
            if (hinge == null)
            {
                Debug.LogError("CarDoor: no hinge joint assigned. Rebuild the prefab with " +
                               "The Block → Build Mustang.", this);
                enabled = false;
                return;
            }

            _rest = hinge.localRotation;
            _open = _rest * Quaternion.AngleAxis(openAngle, openAxis.normalized);
            _bound = true;
        }

        private void Update()
        {
            if (!_bound) Bind();
            if (!_bound) return;

            // Framerate-independent smoothing: the fraction closed per second is fixed, not the
            // fraction closed per frame.
            var k = 1f - Mathf.Exp(-lerpRate * Time.deltaTime);
            hinge.localRotation = Quaternion.Slerp(hinge.localRotation, IsOpen ? _open : _rest, k);
        }

        public void SetOpen(bool open) => IsOpen = open;

        public void Open() => IsOpen = true;

        public void Close() => IsOpen = false;

        /// <summary>
        /// Is <paramref name="joint"/> the transform this door swings? Asked by
        /// <see cref="DetachableParts"/> before it knocks a part off, because on all three lot cars
        /// the detachable door node and this hinge are the same object - and a door lying in the road
        /// must stop being animated.
        /// </summary>
        public bool IsJoint(Transform joint) => joint != null && joint == hinge;

        /// <summary>Editor-time wiring. Called by <c>CarBuilder</c>; nothing at runtime uses it.</summary>
        public void Configure(Transform joint, Vector3 axis, float degrees, float rate)
        {
            hinge = joint;
            openAxis = axis;
            openAngle = degrees;
            lerpRate = rate;
        }
    }
}
