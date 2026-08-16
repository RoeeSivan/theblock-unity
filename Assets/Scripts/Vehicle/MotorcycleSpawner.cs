using TheBlock.Core;
using UnityEngine;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// Spawns the motorcycle at runtime from its prefab and its config spec.
    ///
    /// Unlike <c>CarSpawner</c>, this is a one-shot: there is only one motorcycle in the game, and
    /// it spawns once at startup. It finds ground the way the cars do - one raycast at spawn, then
    /// the suspension takes over - so the bike lands correctly on lot asphalt, a district street, or
    /// nothing at all.
    ///
    /// It does NOT put the bike on the enterable list; the controller does that itself in
    /// <c>OnEnable</c>. See <see cref="EnterableRegistry"/> for why that has to be the vehicle's job.
    /// </summary>
    public class MotorcycleSpawner : MonoBehaviour
    {
        [Tooltip("The Motorcycle prefab built by The Block → Build Motorcycle.")]
        [SerializeField] private GameObject motorcyclePrefab;

        [Tooltip("Drop height for the spawn probe. Must clear anything the bike could stand on.")]
        [SerializeField] private float probeHeight = 20f;

        [Tooltip("Gap left under the wheels so the bike settles onto its suspension instead of starting inside the road.")]
        [SerializeField] private float settleGap = 0.05f;

        private MotorcycleController _bike;

        public MotorcycleController Bike => _bike;

        private void Awake()
        {
            var snapshot = TheBlockConfig.Load();
            var spec = snapshot?.Config?.Vehicle?.Motorcycle;
            if (spec == null)
            {
                Debug.LogError("MotorcycleSpawner: config has no vehicle.motorcycle section.", this);
                return;
            }

            if (motorcyclePrefab == null)
            {
                Debug.LogError(
                    "MotorcycleSpawner: no motorcycle prefab assigned. Build one with " +
                    "The Block → Build Motorcycle and drag it onto this component.", this);
                return;
            }

            Spawn(spec);
        }

        private void Spawn(TheBlockConfig.MotorcycleSpec spec)
        {
            var ground = Convert.Pos(spec.Spawn.Raw);
            var y = spec.RoadSurfaceY;

            // Raycast for actual ground
            var from = new Vector3(ground.x, probeHeight, ground.z);
            if (Physics.Raycast(from, Vector3.down, out var hit, probeHeight * 2f))
            {
                y = hit.point.y;
            }
            else
            {
                Debug.LogWarning(
                    $"MotorcycleSpawner: nothing under motorcycle's spawn {Fmt(ground)} - " +
                    $"dropped at config roadSurfaceY {spec.RoadSurfaceY:0.##} instead.", this);
            }

            var position = new Vector3(ground.x, y + settleGap, ground.z);

            // config.vehicle.motorcycle has no spawn yaw, and the web build leaves the bike at 0 -
            // which is its -Z, and Unity's +Z, so identity is the converted equivalent, not a stub.
            var bike = Instantiate(motorcyclePrefab, position, Quaternion.identity, transform);
            bike.name = "Motorcycle";

            if (bike.TryGetComponent<MotorcycleController>(out var controller)) _bike = controller;
            else Debug.LogError(
                "MotorcycleSpawner: the assigned prefab has no MotorcycleController - this is almost " +
                "always the raw imported model rather than the one The Block → Build Motorcycle makes.",
                bike);
        }

        private static string Fmt(Vector3 v) => $"({v.x:0.#}, {v.z:0.#})";
    }
}
