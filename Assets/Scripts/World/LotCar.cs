using System.Collections.Generic;
using UnityEngine;

namespace TheBlock.World
{
    /// <summary>
    /// One parked filler car in the Reichman lot, and everything needed to turn it into a real one.
    ///
    /// U13 put 101 of these in the world as pure scenery. U17b makes them GTA-style promotable: walk
    /// up to any of them, press <c>E</c>, and the filler is replaced by the drivable prefab of the
    /// same model wearing the same paint, in the same stall, facing the same way. The web build does
    /// exactly this (<c>lot-cars.ts</c> <c>claim</c> → <c>transitions.ts promoteLotCar</c>), and it
    /// is the same mechanism as the carjack - the difference is only where the car came from.
    ///
    /// <b>And it takes a hit.</b> U13 gave these a box collider and nothing else, which made 101 of
    /// them immovable walls in the middle of the one place in the city you are most likely to be
    /// driving badly. Ramming one now hands that single car to PhysX, exactly as
    /// <c>TrafficCar.Wreck</c> does for a street car and for the same reason: only cars the player
    /// actually hits are ever dynamic, so the usual number is zero and the worst case is a handful.
    ///
    /// <b>Two fields, because two is all that cannot be recovered from the GameObject.</b> The pose
    /// is on the transform and the shape is in the mesh; what a filler cannot tell you by looking is
    /// which config model it is (its name is decoration and its mesh is shared) and which palette
    /// material it was painted with (the renderer knows, but only if you also know which slot). Both
    /// are written by <c>WorldBuilder.BuildLotCars</c>, which is the only place that knows.
    /// </summary>
    [DisallowMultipleComponent]
    public class LotCar : MonoBehaviour
    {
        [Tooltip("The config.vehicle.cars name of the drivable twin of this model - Tesla, Audi, Avenger.")]
        [SerializeField] private string modelName;

        [Tooltip("The generated palette material this car was painted with, so the promoted copy keeps it.")]
        [SerializeField] private Material paint;

        [Tooltip("World rotation for the drivable copy, baked at build time - see the property.")]
        [SerializeField] private Quaternion driveRotation = Quaternion.identity;

        [Header("Getting hit")]
        [Tooltip("Off makes this filler an immovable wall for good - U13's contract.")]
        [SerializeField] private bool wreckOnImpact = true;

        [Tooltip("Relative speed, m/s, below which a bump is just a bump. The same 3 m/s a street " +
                 "car uses, so the lot and the road do not disagree about what a shunt is.")]
        [SerializeField] private float wreckSpeed = 3f;

        [Tooltip("Mass once it is a real body. Matches the drivable cars and TrafficCar's wreck.")]
        [SerializeField] private float wreckMass = 1400f;

        [Tooltip("Fraction of the impact speed the wreck leaves with.")]
        [SerializeField] private float wreckMomentumShare = 0.9f;

        [Tooltip("Ceiling on that, m/s.")]
        [SerializeField] private float wreckMaxSpeed = 20f;

        [Tooltip("How hard an off-centre hit slews the car round.")]
        [SerializeField] private float wreckSpin = 0.45f;

        public string ModelName => modelName;
        public Material Paint => paint;

        /// <summary>
        /// True once this filler has been hit hard enough to become a real body. It is still an
        /// obstacle; it is no longer a car anyone is walking up to and driving away, which is
        /// <c>TrafficSystem.NearestStopped</c>'s rule for a street wreck and has to be this one's
        /// too - promoting a wreck would swap a shunted, spinning car for a pristine one standing
        /// neatly in its stall.
        /// </summary>
        public bool Wrecked { get; private set; }

        /// <summary>
        /// The rotation the drivable prefab's ROOT must take to leave the car pointing exactly where
        /// this filler points.
        ///
        /// Not simply this transform's rotation, and the difference is a facing correction that lives
        /// in two different config blocks. A filler is turned by <c>lotCars.models[].modelYaw</c> and
        /// a drivable car by <c>vehicle.cars[].modelYaw</c>; those happen to agree for all three
        /// promotable models today, so copying the rotation across would work and would keep working
        /// right up until somebody re-tunes one of them. Baked here at build time, where both numbers
        /// are visible at once, it cannot drift.
        /// </summary>
        public Quaternion DriveRotation => driveRotation;

        /// <summary>Set by <c>WorldBuilder.BuildLotCars</c>.</summary>
        public void Configure(string carName, Material paintMaterial, Quaternion rotationForDrivable)
        {
            modelName = carName;
            paint = paintMaterial;
            driveRotation = rotationForDrivable;
        }

        /// <summary>
        /// Where the drivable copy's origin goes: the body centre in XZ, the ground under the tyres
        /// in Y, which is the origin every car prefab in this project is built around.
        ///
        /// Measured off the renderers rather than read off the transform, because a filler's origin
        /// is the artist's pivot and nothing has moved it - <c>WorldBuilder</c> places these by
        /// subtracting the model's own <c>min.y</c> instead of re-pivoting them. Asking the meshes
        /// where the car actually is needs no assumption about where that pivot ended up.
        /// </summary>
        public Vector3 ContactPatch()
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return transform.position;

            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);

            return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }

        // --- getting hit ---------------------------------------------------------------------------

        /// <summary>
        /// A static collider still receives collision callbacks, which is what makes this possible
        /// without giving 101 parked cars a Rigidbody they would never otherwise need.
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            if (!wreckOnImpact || Wrecked) return;
            if (collision.rigidbody == null) return;      // a pedestrian capsule, or nothing at all
            if (collision.rigidbody.isKinematic) return;  // a traffic car under script control
            if (collision.relativeVelocity.magnitude < wreckSpeed) return;

            Wreck(collision);
        }

        /// <summary>
        /// Hands this one filler over to PhysX.
        ///
        /// The push direction is taken from the CONTACT POINT, not from <c>Collision.impulse</c> or
        /// <c>relativeVelocity</c> - both of those carry a sign that depends on which body the
        /// callback fired on, and getting it backwards drives the car through whatever hit it.
        /// "Away from where I was struck" cannot be backwards. This is <c>TrafficCar.Wreck</c>'s
        /// argument and its arithmetic; what differs is only that there is no body here yet and no
        /// lane speed to carry over.
        /// </summary>
        private void Wreck(Collision collision)
        {
            var contact = collision.GetContact(0);
            var away = transform.position - contact.point;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) away = transform.forward;
            away.Normalize();

            float speed = Mathf.Min(collision.relativeVelocity.magnitude * wreckMomentumShare, wreckMaxSpeed);
            float offCentre = Vector3.Dot(contact.point - transform.position, transform.right);

            Promote(away, speed, offCentre);
        }

        /// <summary>
        /// U35b: an explosion next door promotes this filler too.
        ///
        /// <b>It needs its own entry point and cannot borrow <c>OnCollisionEnter</c>'s.</b> A parked
        /// filler is a static collider with no Rigidbody, which is the whole trick that lets 101 of
        /// them stand in the lot for free - and it also means <c>Physics.OverlapSphere</c> hands the
        /// blast a collider whose <c>attachedRigidbody</c> is null, so <c>AddExplosionForce</c> has
        /// nothing to push. The arithmetic below is <see cref="Wreck"/>'s, with the blast's centre
        /// standing in for the contact point.
        /// </summary>
        /// <param name="origin">Where the car went up.</param>
        /// <param name="force">Newtons at this distance, already faded by the caller.</param>
        public void Blast(Vector3 origin, float force)
        {
            if (Wrecked) return;

            var away = transform.position - origin;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) away = transform.forward;
            away.Normalize();

            // Force to speed, through the mass this filler is about to be given. A blast that shunts a
            // parked car three metres is right; one that launches it over the buildings is a physics
            // number nobody derived.
            float speed = Mathf.Min(force / Mathf.Max(1f, wreckMass), wreckMaxSpeed);
            float offCentre = Vector3.Dot(origin - transform.position, transform.right);

            Promote(away, speed, -offCentre);
        }

        /// <summary>
        /// Hands this filler to PhysX: the body, the mass, the dropped centre of mass, the shove and
        /// the spin. Shared by the collision path and the blast path so the two cannot drift.
        /// </summary>
        private void Promote(Vector3 away, float speed, float offCentre)
        {
            Wrecked = true;

            var body = gameObject.AddComponent<Rigidbody>();
            body.mass = wreckMass;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            // A box the size of a car pivots about its own centre and goes over on its roof at the
            // first kerb. Dropping the centre of mass into the sills is the same single knob
            // CarController leans on, and it is worth more here than any amount of damping.
            if (TryGetComponent<BoxCollider>(out var box))
                body.centerOfMass = box.center - Vector3.up * (box.size.y * 0.35f);

            body.linearVelocity = away * speed;

            // A little spin, scaled by how far off-centre the hit was, so a clipped wing slews the car
            // round instead of shunting it squarely out of its stall.
            body.angularVelocity = new Vector3(0f, -offCentre * speed * wreckSpin, 0f);
        }

        // --- the registry -------------------------------------------------------------------------

        private static readonly List<LotCar> Parked = new();

        private void OnEnable() => Parked.Add(this);

        private void OnDisable() => Parked.Remove(this);

        /// <summary>Every filler still standing.</summary>
        public static IReadOnlyList<LotCar> All => Parked;

        /// <summary>
        /// The nearest filler within <paramref name="radius"/> of a point, measured on the ground
        /// plane so standing on a kerb beside one still counts - the same test
        /// <c>VehicleEnterExit.Nearest</c> uses for real vehicles, and it has to be, because the two
        /// answers are compared against each other when <c>E</c> is pressed.
        ///
        /// A linear scan over 101 cars, only on the frames <c>E</c> is pressed. A spatial index for
        /// that would be a hundred lines to save a hundred subtractions.
        /// </summary>
        public static LotCar Nearest(Vector3 point, float radius)
        {
            LotCar best = null;
            float bestSqr = radius * radius;

            foreach (var car in Parked)
            {
                if (car == null || car.Wrecked) continue;

                var offset = car.transform.position - point;
                offset.y = 0f;
                float sqr = offset.sqrMagnitude;
                if (sqr > bestSqr) continue;

                bestSqr = sqr;
                best = car;
            }

            return best;
        }
    }
}
