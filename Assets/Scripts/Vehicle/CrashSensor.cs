using UnityEngine;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// Turns a vehicle collision into a severity, and tells anyone listening.
    ///
    /// <b>This component is the fix for "the police were too sensitive".</b> The web build detects a
    /// crash as <i>"collide-and-slide delivered less than 25% of the commanded motion while moving
    /// faster than 4 m/s"</i> (<c>vehicle.ts:309</c>, <c>config.ts blockedRatio 0.25</c>) - a probe
    /// that already existed for another purpose and was reused. In a dense city that fires on a wall
    /// scrape, a kerb, a bollard and a parked car, each worth a full star on a 3 s cooldown, which is
    /// how a clumsy ten seconds becomes three stars.
    ///
    /// Here the reading is <b>closing speed along the contact normal</b>:
    /// <code>vClose = |Dot(collision.relativeVelocity, contact.normal)|</code>
    /// A scrape carries the car's whole speed but its contact normal is perpendicular to travel, so
    /// vClose is near zero - the discrimination is geometric, not a threshold someone picked. A
    /// head-on gives back the car's speed. It is also mass-free, and it does not depend on which body
    /// the callback fired on, which <c>Collision.impulse</c>'s direction does (the same trap
    /// <c>TrafficCar</c> works around). The impulse is still read, and reported, as a cross-check.
    ///
    /// <b>OnCollisionEnter only, never Stay.</b> A scrape is a long sequence of Stay callbacks and a
    /// handful of Enters; listening to Enter alone is most of the immunity before a single number is
    /// tuned.
    ///
    /// <b>Attached at runtime, by the vehicle itself.</b> <see cref="CarController.Bind"/> and
    /// <see cref="MotorcycleController.Bind"/> both call <see cref="Ensure"/>. This class shipped in
    /// U19 and sat on NOTHING - no prefab, no scene object, no <c>AddComponent</c> anywhere - so
    /// both listeners were subscribed to an event that could never fire: a crash was worth no heat
    /// and made no sound, for every unit since. A prefab field would have been dropped again the
    /// next time <c>The Block → Build Drivable Cars</c> regenerated them, which is the likeliest
    /// story of how it was lost in the first place. Adding it from the controller's own bind is the
    /// same answer <c>PoliceSystem.FillPool</c> uses for <c>CopCar</c>, and it cannot be regenerated
    /// away.
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

            /// <summary>Impulse magnitude over mass, m/s - the cross-check, logged not used.</summary>
            public readonly float ImpulseOverMass;

            /// <summary>What was hit, for weighting. Null when it was the static world.</summary>
            public readonly Rigidbody Other;

            /// <summary>
            /// Was the thing hit another VEHICLE - a traffic car, a parked filler, a cruiser, a
            /// drivable car or a bike?
            ///
            /// <see cref="Other"/> cannot answer this and never could: a parked lot filler is a
            /// static collider with no Rigidbody at all, so it arrives here indistinguishable from a
            /// wall, and a traffic car promoted to a wreck inside this very callback has no body yet
            /// either. The answer is taken from the COLLIDER's own hierarchy instead, which is true
            /// in both cases.
            /// </summary>
            public readonly bool HitVehicle;

            public readonly Vector3 Point;
            public readonly Vector3 Normal;

            public Impact(CrashSensor sensor, float closing, float impulse, Rigidbody other, bool hitVehicle, Vector3 point, Vector3 normal)
            {
                Sensor = sensor;
                ClosingSpeed = closing;
                ImpulseOverMass = impulse;
                Other = other;
                HitVehicle = hitVehicle;
                Point = point;
                Normal = normal;
            }
        }

        /// <summary>
        /// Raised for every impact that survives the filters. Static because the listener is a single
        /// system that does not know which cars exist - and because U27 wants this exact event for
        /// the crash thump, from the same frame.
        /// </summary>
        public static event System.Action<Impact> Crashed;

        [Tooltip("Contacts flatter than this are the road, a kerb or a landing - never a crash.")]
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

        /// <summary>
        /// Gives <paramref name="vehicle"/> a sensor if it has not got one. Idempotent, and safe to
        /// call again after a mid-Play recompile.
        ///
        /// <c>TryGetComponent</c> rather than <c>GetComponent() ?? AddComponent()</c>: a missing
        /// component comes back as Unity's fake-null, which is not null to <c>??</c>, so the
        /// coalescing form silently hands back a dead reference and adds nothing.
        /// </summary>
        public static void Ensure(GameObject vehicle)
        {
            if (vehicle == null) return;
            if (!vehicle.TryGetComponent<CrashSensor>(out _)) vehicle.AddComponent<CrashSensor>();
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
                this, best, collision.impulse.magnitude / mass, collision.rigidbody,
                IsVehicle(collision.collider), contact.point, contact.normal));
        }

        /// <summary>
        /// Does this collider belong to a vehicle?
        ///
        /// Walked from the collider UPWARDS, because every kind of vehicle in this game puts its
        /// script somewhere different: a drivable car and a traffic car carry theirs on the same
        /// object as the collider, a parked filler carries <see cref="World.LotCar"/> on the root of
        /// a scaled model, and a bike's collider sits under its body. It is only ever run on the
        /// frames a vehicle actually strikes something, which is not a rate worth caching for.
        /// </summary>
        private static bool IsVehicle(Collider other)
        {
            if (other == null) return false;

            var probe = other.attachedRigidbody != null ? other.attachedRigidbody.transform : other.transform;

            // A CRUISER is not a civilian vehicle, and the exclusion is not politeness - it is the
            // feedback loop U19 already paid for once. Cops crowd you and touch you constantly, so a
            // low bar against police contact mints a crime every cooldown, which spawns another cop
            // and resets the give-up clock: a pursuit that can never end because it is happening.
            // Ramming one hard is still a crime; it is judged by the wall's line, like a wall.
            var car = probe.GetComponentInParent<CarController>();
            if (car != null) return !car.IsPolice;

            return probe.GetComponentInParent<MotorcycleController>() != null
                   || probe.GetComponentInParent<Traffic.TrafficCar>() != null
                   || probe.GetComponentInParent<World.LotCar>() != null;
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
