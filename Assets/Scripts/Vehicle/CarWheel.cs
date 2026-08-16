using UnityEngine;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// Drives one visible wheel from its <see cref="WheelCollider"/>.
    ///
    /// This is the whole reason U8 uses the Mustang. Every other car in the web build went through
    /// <c>blender/merge-car-meshes.py</c>, which welds the wheels into the body to cut three.js draw
    /// calls - a fix for a problem Unity does not have, and one that leaves those models unable to
    /// show a wheel turning ever again. The Mustang kept its rig, so its wheels can finally move:
    /// the shipped web build never rotated a wheel at all.
    ///
    /// The bone is posed in WORLD space from <c>GetWorldPose</c>, which already carries suspension
    /// travel, steering and spin. Its parent knuckle bone is deliberately left alone - writing a
    /// world pose onto a child makes the parent irrelevant, and driving both would apply the steer
    /// angle twice.
    ///
    /// <b>Everything below the rest capture is about one failure: a bad quaternion on a bone.</b>
    /// The Mustang's body, doors and wheels are ONE skin over sixteen bones, so a wheel bone holding
    /// a degenerate rotation does not just tilt a tyre - every vertex weighted to it is thrown at
    /// the horizon and the car draws as a black wedge across the sky. Unity never complains: a
    /// zero or NaN quaternion is written, converted to a matrix, and rendered.
    /// </summary>
    public class CarWheel : MonoBehaviour
    {
        [Tooltip("The rigged bone this wheel's mesh is skinned to, e.g. wheel_Front_L_0.")]
        [SerializeField] private Transform bone;

        private WheelCollider _collider;
        private bool _bound;
        private bool _hasStepped;
        private bool _warned;

        /// <summary>
        /// The bone's rest orientation relative to this wheel object's, captured once.
        ///
        /// A Blender bone almost never points down the axis Unity would pick, so the pose coming out
        /// of the WheelCollider cannot be written onto it raw - the tyre would sit cocked at some
        /// arbitrary angle. Holding the difference and re-applying it keeps the wheel where the
        /// artist left it while the collider still owns the motion.
        ///
        /// <b>Measured against the transform, not against <c>GetWorldPose</c>.</b> The pose is only
        /// meaningful after PhysX has simulated the collider, and this is captured in
        /// <see cref="Bind"/> - which runs from <c>Awake</c>, i.e. during scene load, guaranteed
        /// before the first <c>FixedUpdate</c>. At rest the two are the same answer anyway: steer
        /// and spin are both zero, so the pose rotation IS the collider transform's rotation. The
        /// transform is always a valid unit quaternion; the un-simulated pose is not guaranteed to
        /// be, and <c>Quaternion.Inverse</c> of a zero quaternion is NaN - which then rode into the
        /// bone on every LateUpdate for the rest of the session.
        /// </summary>
        private Quaternion _boneRestOffset = Quaternion.identity;

        private void Awake() => Bind();

        /// <summary>
        /// Captures the rest offset. Called from Awake and again from LateUpdate if a mid-Play
        /// recompile wiped it - the same guard <see cref="CarDoor"/> carries, for the same reason.
        ///
        /// Worse here than on the door, and worth spelling out: <see cref="_boneRestOffset"/> is a
        /// non-serialized struct, so a domain reload brings it back as <c>(0,0,0,0)</c> - the ZERO
        /// quaternion, not identity. Multiplying a pose by it produces a degenerate rotation, and
        /// this component then writes that into a bone sixty times a second.
        /// </summary>
        private void Bind()
        {
            if (_collider == null) _collider = GetComponent<WheelCollider>();
            if (_collider == null)
            {
                Debug.LogError("CarWheel: no WheelCollider on this object.", this);
                enabled = false;
                return;
            }

            if (bone == null)
            {
                Debug.LogWarning($"CarWheel on {name}: no bone assigned, wheel will not turn.", this);
                enabled = false;
                return;
            }

            var offset = Quaternion.Inverse(transform.rotation) * bone.rotation;
            _boneRestOffset = IsUsable(offset) ? offset : Quaternion.identity;
            _bound = true;
        }

        /// <summary>PhysX has simulated this collider at least once, so its pose means something.</summary>
        private void FixedUpdate() => _hasStepped = true;

        /// <summary>
        /// LateUpdate, not Update: the Rigidbody has moved by then, so the wheel is posed against
        /// the body's final position for the frame instead of trailing it by one.
        /// </summary>
        private void LateUpdate()
        {
            if (!_bound) Bind();
            if (!_bound || !_hasStepped) return;
            Pose();
        }

        /// <summary>
        /// Puts the bone where the collider is, right now.
        ///
        /// Public because a teleport has to call it: <c>GetWorldPose</c> reports the pose PhysX last
        /// simulated, so on the frame a car is moved the wheels are still at the old world position
        /// while the body is at the new one - the skin stretches between the two and draws a wedge
        /// the length of the teleport. <see cref="CarController.Respawn"/> syncs the transforms and
        /// then calls this.
        /// </summary>
        public void Pose()
        {
            _collider.GetWorldPose(out var position, out var rotation);

            // The guard is on the WRITE, not on the read. A NaN that reaches a bone never leaves it,
            // and nothing downstream will tell you where it came from.
            if (!IsUsable(rotation) || !IsUsable(position))
            {
                Complain($"a degenerate pose (pos {position}, rot {rotation})");
                return;
            }

            // And a pose can be a perfectly valid unit quaternion at a perfectly finite position that
            // is still nowhere near this car. THAT is what the validity check above cannot see, and
            // it is the one that tears the skin: the body draws here, one wheel bone draws there, and
            // the vertices in between stretch into a wedge between the two.
            //
            // The bound is not a taste number. A WheelCollider's pose is its own transform slid along
            // the suspension axis, so it can never leave a sphere of `suspensionDistance` around the
            // anchor - that is the entire travel the spring has. Anything further did not come from
            // the spring; it came from a transform PhysX has not simulated yet (a teleport, a respawn,
            // a wreck promotion), and the honest answer is to leave the bone on the skeleton for one
            // frame rather than to draw the car across the sky.
            float slack = _collider.suspensionDistance + PoseSlack;
            float strayed = Vector3.Distance(position, transform.position);
            if (strayed > slack)
            {
                Complain($"a pose {strayed:0.##} m from its own anchor, which is more than the " +
                         $"{slack:0.##} m the suspension can travel");
                return;
            }

            bone.SetPositionAndRotation(position, rotation * _boneRestOffset);
        }

        /// <summary>
        /// Room on top of the suspension travel before a pose counts as impossible. Wide enough that
        /// no legitimate compression trips it, far narrower than any smear worth seeing.
        /// </summary>
        private const float PoseSlack = 0.25f;

        /// <summary>Says it once. A pose that goes wrong goes wrong every frame, and 60 Hz of the same
        /// error buries whatever else the console was trying to tell you.</summary>
        private void Complain(string what)
        {
            if (_warned) return;
            _warned = true;
            Debug.LogError(
                $"CarWheel on {name}: WheelCollider.GetWorldPose returned {what} - the bone is being " +
                "left alone. Written, this is what draws the car as a wedge across the sky.", this);
        }

        /// <summary>Finite, and a unit quaternion to within a percent.</summary>
        private static bool IsUsable(Quaternion q)
        {
            float square = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            return !float.IsNaN(square) && !float.IsInfinity(square) && Mathf.Abs(square - 1f) < 0.01f;
        }

        private static bool IsUsable(Vector3 v)
        {
            float square = v.sqrMagnitude;
            return !float.IsNaN(square) && !float.IsInfinity(square);
        }

#if UNITY_EDITOR
        /// <summary>Wired by <c>CarBuilder</c>, which knows which bone belongs to which corner.</summary>
        public void BindBone(Transform wheelBone) => bone = wheelBone;
#endif
    }
}
