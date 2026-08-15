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
    /// is the same mechanism as the carjack — the difference is only where the car came from.
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
        [Tooltip("The config.vehicle.cars name of the drivable twin of this model — Tesla, Audi, Avenger.")]
        [SerializeField] private string modelName;

        [Tooltip("The generated palette material this car was painted with, so the promoted copy keeps it.")]
        [SerializeField] private Material paint;

        [Tooltip("World rotation for the drivable copy, baked at build time — see the property.")]
        [SerializeField] private Quaternion driveRotation = Quaternion.identity;

        public string ModelName => modelName;
        public Material Paint => paint;

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
        /// is the artist's pivot and nothing has moved it — <c>WorldBuilder</c> places these by
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

        // --- the registry -------------------------------------------------------------------------

        private static readonly List<LotCar> Parked = new();

        private void OnEnable() => Parked.Add(this);

        private void OnDisable() => Parked.Remove(this);

        /// <summary>Every filler still standing.</summary>
        public static IReadOnlyList<LotCar> All => Parked;

        /// <summary>
        /// The nearest filler within <paramref name="radius"/> of a point, measured on the ground
        /// plane so standing on a kerb beside one still counts — the same test
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
                if (car == null) continue;

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
