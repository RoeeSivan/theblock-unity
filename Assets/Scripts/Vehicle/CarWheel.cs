using UnityEngine;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// Drives one visible wheel from its <see cref="WheelCollider"/>.
    ///
    /// This is the whole reason U8 uses the Mustang. Every other car in the web build went through
    /// <c>blender/merge-car-meshes.py</c>, which welds the wheels into the body to cut three.js draw
    /// calls — a fix for a problem Unity does not have, and one that leaves those models unable to
    /// show a wheel turning ever again. The Mustang kept its rig, so its wheels can finally move:
    /// the shipped web build never rotated a wheel at all.
    ///
    /// The bone is posed in WORLD space from <c>GetWorldPose</c>, which already carries suspension
    /// travel, steering and spin. Its parent knuckle bone is deliberately left alone — writing a
    /// world pose onto a child makes the parent irrelevant, and driving both would apply the steer
    /// angle twice.
    /// </summary>
    public class CarWheel : MonoBehaviour
    {
        [Tooltip("The rigged bone this wheel's mesh is skinned to, e.g. wheel_Front_L_0.")]
        [SerializeField] private Transform bone;

        private WheelCollider _collider;

        /// <summary>
        /// The bone's rest orientation relative to the collider's, captured once.
        ///
        /// A Blender bone almost never points down the axis Unity would pick, so the pose coming out
        /// of the WheelCollider cannot be written onto it raw — the tyre would sit cocked at some
        /// arbitrary angle. Holding the difference and re-applying it keeps the wheel where the
        /// artist left it while the collider still owns the motion.
        /// </summary>
        private Quaternion _boneRestOffset = Quaternion.identity;

        private void Awake()
        {
            _collider = GetComponent<WheelCollider>();
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

            _collider.GetWorldPose(out _, out var colliderRest);
            _boneRestOffset = Quaternion.Inverse(colliderRest) * bone.rotation;
        }

        /// <summary>
        /// LateUpdate, not Update: the Rigidbody has moved by then, so the wheel is posed against
        /// the body's final position for the frame instead of trailing it by one.
        /// </summary>
        private void LateUpdate()
        {
            _collider.GetWorldPose(out var position, out var rotation);
            bone.SetPositionAndRotation(position, rotation * _boneRestOffset);
        }

#if UNITY_EDITOR
        /// <summary>Wired by <c>CarBuilder</c>, which knows which bone belongs to which corner.</summary>
        public void BindBone(Transform wheelBone) => bone = wheelBone;
#endif
    }
}
