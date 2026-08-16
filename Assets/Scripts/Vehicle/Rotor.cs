using UnityEngine;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// Spinning rotors for the helicopter - the port of <c>src/vehicle/rotor.ts</c>.
    ///
    /// <b>The pivot wrapper is the whole trick, and it is a property of this model.</b> In the Huey
    /// GLB both rotor nodes have their origin at the MODEL centre, not at their own hub. Spin such a
    /// node directly and the blades orbit the fuselage like a fairground ride instead of turning in
    /// place. So each rotor gets a pivot placed at its own bounding-box centre, the node is
    /// re-parented under it preserving its world pose, and the PIVOT is what spins.
    ///
    /// The throttle ramps rather than snaps, using the same framerate-independent smoothing
    /// <see cref="CarDoor"/> uses - <c>k = 1 − exp(−rate·dt)</c>. A rotor that reaches full speed in
    /// one frame reads as a video starting, not as an engine.
    /// </summary>
    public class Rotor : MonoBehaviour
    {
        [System.Serializable]
        public struct Blade
        {
            [Tooltip("The pivot placed at the rotor's hub. The NODE hangs under it.")]
            public Transform Pivot;

            [Tooltip("Local axis to spin about.")]
            public Vector3 Axis;

            [Tooltip("Degrees per second at full throttle.")]
            public float Speed;
        }

        [SerializeField] private Blade[] blades = System.Array.Empty<Blade>();

        [Tooltip("How fast the throttle chases its target. Higher = a sharper spin-up.")]
        [SerializeField] private float rampRate = 0.6f;

        private float _throttle;
        private float _target;

        /// <summary>0 when parked, 1 while flown. Ramped, never assigned straight through.</summary>
        public void SetFlying(bool flying) => _target = flying ? 1f : 0f;

        /// <summary>Current spin fraction, for anything that wants to hear it (U27's rotor sound).</summary>
        public float Throttle => _throttle;

        private void Update()
        {
            var dt = Time.deltaTime;
            _throttle = Mathf.Lerp(_throttle, _target, 1f - Mathf.Exp(-rampRate * dt));
            if (_throttle < 0.001f) return;

            foreach (var blade in blades)
            {
                if (blade.Pivot == null) continue;
                blade.Pivot.Rotate(blade.Axis, blade.Speed * _throttle * dt, Space.Self);
            }
        }

        /// <summary>Editor-side wiring, used by <c>HelicopterBuilder</c>.</summary>
        public void SetBlades(Blade[] configured) => blades = configured ?? System.Array.Empty<Blade>();
    }
}
