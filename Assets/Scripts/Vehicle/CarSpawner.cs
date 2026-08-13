using System.Collections.Generic;
using System.Linq;
using TheBlock.Core;
using UnityEngine;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// Puts cars in the world at the spawns <c>config.vehicle.cars</c> names.
    ///
    /// At runtime rather than baked into the scene, and that is deliberate: cars are the one part of
    /// the world that moves, and a hand-placed transform would immediately disagree with the config
    /// it was copied from. WorldBuilder owns the static world; this owns the things that drive away
    /// from where they started. U13's parking-lot cars and U17's traffic both grow from here.
    ///
    /// A car's config spawn carries no Y — the web build ray-cast for the road every frame. Here it
    /// is a one-off probe at spawn and the suspension takes over, so a car lands correctly whether
    /// it is standing on lot asphalt, a district street, or nothing at all.
    /// </summary>
    public class CarSpawner : MonoBehaviour
    {
        [Tooltip("Car prefabs from The Block → Build Mustang. Each is matched to the config entry " +
                 "whose `name` equals the prefab's name.")]
        [SerializeField] private List<GameObject> carPrefabs = new();

        [Tooltip("Drop height for the spawn probe. Must clear anything the car could stand on.")]
        [SerializeField] private float probeHeight = 20f;

        [Tooltip("Gap left under the wheels so the car settles onto its springs instead of starting " +
                 "inside the road.")]
        [SerializeField] private float settleGap = 0.05f;

        private readonly List<CarController> _spawned = new();

        /// <summary>Every car currently in the world, in spawn order.</summary>
        public IReadOnlyList<CarController> Spawned => _spawned;

        private void Awake()
        {
            var snapshot = TheBlockConfig.Load();
            var cars = snapshot?.Config?.Vehicle?.Cars;
            if (cars == null)
            {
                Debug.LogError("CarSpawner: config has no vehicle.cars list.", this);
                return;
            }

            if (carPrefabs.Count == 0 || carPrefabs.TrueForAll(p => p == null))
            {
                Debug.LogError(
                    "CarSpawner: no car prefabs assigned, so no cars will exist. Build one with " +
                    "The Block → Build Mustang and drag it onto this component's Car Prefabs list.", this);
                return;
            }

            foreach (var prefab in carPrefabs)
            {
                if (prefab == null) continue;

                var spec = cars.FirstOrDefault(c =>
                    string.Equals(c.Name, prefab.name, System.StringComparison.OrdinalIgnoreCase));
                if (spec == null)
                {
                    Debug.LogWarning($"CarSpawner: no config.vehicle.cars entry named '{prefab.name}'.", this);
                    continue;
                }

                Spawn(prefab, spec);
            }
        }

        private void Spawn(GameObject prefab, TheBlockConfig.CarSpec spec)
        {
            var ground = Convert.Pos(spec.Spawn.Raw);
            var y = spec.RoadSurfaceY;

            // staticOnly is not a thing for a single raycast, so this can in principle hit another
            // car — but spawns are metres apart and this runs before any of them has moved.
            var from = new Vector3(ground.x, probeHeight, ground.z);
            if (Physics.Raycast(from, Vector3.down, out var hit, probeHeight * 2f))
            {
                y = hit.point.y;
            }
            else
            {
                Debug.LogWarning(
                    $"CarSpawner: nothing under {spec.Name}'s spawn {Fmt(ground)} — " +
                    $"dropped at config roadSurfaceY {spec.RoadSurfaceY:0.##} instead.", this);
            }

            var position = new Vector3(ground.x, y + settleGap, ground.z);
            var rotation = Convert.RotFromRadians(spec.SpawnYaw ?? 0f);

            var car = Instantiate(prefab, position, rotation, transform);
            car.name = spec.Name;

            if (car.TryGetComponent<CarController>(out var controller)) _spawned.Add(controller);
            else Debug.LogWarning($"CarSpawner: {spec.Name} has no CarController.", car);
        }

        private static string Fmt(Vector3 v) => $"({v.x:0.#}, {v.z:0.#})";
    }
}
