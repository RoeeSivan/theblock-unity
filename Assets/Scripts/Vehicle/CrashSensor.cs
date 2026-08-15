using UnityEngine;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// Turns a vehicle collision into a severity, and tells anyone listening.
    ///
    /// <b>This component is the fix for "the police were too sensitive".</b> The web build detects a
    /// crash as <i>"collide-and-slide delivered less than 25% of the commanded motion while moving
    /// faster than 4 m/s"</i> (<c>vehicle.ts:309</c>, <c>config.ts blockedRatio 0.25</c>) — a probe
    /// that already existed for another purpose and was reused. In a dense city that fires on a wall
    /// scrape, a kerb, a bollard and a parked car, each worth a full star on a 3 s cooldown, which is
    /// how a clumsy ten seconds becomes three stars.
    ///
    /// Here the reading is <b>closing speed along the contact normal</b>:
    /// <code>vClose = |Dot(collision.relativeVelocity, contact.normal)|</code>
    /// A scrape carries the car's whole speed but its contact normal is perpendicular to travel, so
    /// vClose is near zero — the discrimination is geometric, not a threshold someone picked. A
    /// head-on gives back the car's speed. It is also mass-free, and it does not depend on which body
    /// the callback fired on, which <c>Collision.impulse</c>'s direction does (the same trap
    /// <c>TrafficCar</c> works around). The impulse is still read, and reported, as a cross-check.
    ///
    /// <b>OnCollisionEnter only, never Stay.</b> A scrape is a long sequence of Stay callbacks and a
    /// handful of Enters; listening to Enter alone is most of the immunity before a single number is
    /// tuned.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CrashSensor : MonoBehaviour
    {
        /// <summary>One impact, already filtered and measured.</summary>
        public readonly struct Impact
        {
            /// <summary>The vehicle that was hit INTO something.</summary>
            public readonly CrashSensor Sensor;

            /// <summary>Closing speed along the contact normal, m/s. The severity.</summary>
            public readonly float ClosingSpeed;

            /// <summary>Impulse magnitude over mass, m/s — the cross-check, logged not used.</summary>
            public readonly float ImpulseOverMass;

            /// <summary>What was hit, for weighting. Null when it was the static world.</summary>
            public readonly Rigidbody Other;

            public readonly Vector3 Point;
            public readonly Vector3 Normal;

            public Impact(CrashSensor sensor, float closing, float impulse, Rigidbody other, Vector3 point, Vector3 normal)
            {
                Sensor = sensor;
                ClosingSpeed = closing;
                ImpulseOverMass = impulse;
                Other = other;
                Point = point;
                Normal = normal;
            }
        }

        /// <summary>
        /// Raised for every impact that survives the filters. Static because the listener is a single
        /// system that does not know which cars exist — and because U27 wants this exact event for
        /// the crash thump, from the same frame.
        /// </summary>
        public static event System.Action<Impact> Crashed;

        [Tooltip("Contacts flatter than this are the road, a kerb or a landing — never a crash.")]
        [Range(0.1f, 0.99f)]
        [SerializeField] private float groundNormalY = 0.7f;

        [Tooltip("Closing speed below which nothing is reported at all, m/s.")]
        [SerializeField] private float minClosingSpeed = 0.5f;

        [Tooltip("Layer whose collisions belong to RunOverSystem, not here. Counting a body as a " +
                 "wall is the two-detector fight U18's decision log settled.")]
        [SerializeField] private string pedestrianLayer = "Pedestrian";

        private Rigidbody _body;
        private int _pedestrianLayer = -1;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _pedestrianLayer = LayerMask.NameToLayer(pedestrianLayer);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_body == null) _body = GetComponent<Rigidbody>();
            if (collision.contactCount == 0) return;
            if (collision.collider != null && collision.collider.gameObject.layer == _pedestrianLayer) return;

            // The most head-on contact of the set. A single collision reports several points and the
            // shallow ones are the car sliding along whatever it just touched.
            float best = 0f;
            var contact = collision.GetContact(0);
            for (int i = 0; i < collision.contactCount; i++)
            {
                var candidate = collision.GetContact(i);
                if (Mathf.Abs(candidate.normal.y) > groundNormalY) continue;

                float closing = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, candidate.normal));
                if (closing <= best) continue;

                best = closing;
                contact = candidate;
            }

            if (best < minClosingSpeed) return;

            float mass = _body != null && _body.mass > 0f ? _body.mass : 1f;
            Crashed?.Invoke(new Impact(
                this, best, collision.impulse.magnitude / mass, collision.rigidbody, contact.point, contact.normal));
        }

        /// <summary>
        /// Was this impact the car's own doing?
        ///
        /// True when the contact is roughly ahead of the nose, or when this car was the faster of the
        /// two. Without it, a cop ramming a stopped player mints the PLAYER heat, and the pursuit
        /// feeds itself: one cop hits you, you gain a star, another cop spawns.
        /// </summary>
        public bool AtFault(in Impact impact)
        {
            var toContact = impact.Point - transform.position;
            toContact.y = 0f;
            if (Vector3.Dot(toContact.normalized, transform.forward) > 0.34f) return true;

            float mine = _body != null ? _body.linearVelocity.magnitude : 0f;
            float theirs = impact.Other != null ? impact.Other.linearVelocity.magnitude : 0f;
            return mine > theirs;
        }
    }
}
