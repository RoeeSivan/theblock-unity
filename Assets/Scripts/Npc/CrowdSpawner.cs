using System.Collections.Generic;
using TheBlock.Core;
using TheBlock.Player;
using TheBlock.Vehicles;
using UnityEngine;
using UnityEngine.AI;

namespace TheBlock.Npc
{
    /// <summary>
    /// The street crowd — U16's population, as opposed to <see cref="Pedestrian"/>'s behaviour.
    ///
    /// THE WEB BUILD SEEDS THE WHOLE WORLD AT BOOT: nine people per district on the auto-detected
    /// grid, eight per hand-recorded strip across forty strips, two per painted rectangle across
    /// forty rectangles, plus two dedicated crossers per zebra — several hundred characters, every
    /// one of them created before the first frame and then frozen individually whenever the player
    /// is more than 90 m away (`cullDistanceM`, which the comment there notes was 220 until "nearly
    /// the whole crowd" was live every frame).
    ///
    /// That arrangement exists because a three.js pedestrian is cheap to hold and expensive to
    /// create. A NavMeshAgent is the other way round, and freezing one is not free — it stays in the
    /// avoidance solver's grid. So the crowd here is a POOL that follows the player: a fixed number
    /// of agents, recycled out of the far side of the world and back in ahead of you, each rerolling
    /// its face and its shirt on the way (<see cref="NpcAppearance"/>). Forty live agents give a
    /// denser-looking street than four hundred frozen ones did, at a fraction of the cost, and the
    /// number is a budget rather than a consequence of how the world was authored.
    ///
    /// What it deliberately does NOT do is place anybody. Where people can stand is the NavMesh's
    /// answer, and the NavMesh is the districts' own pavements — so the eighty hand-recorded
    /// rectangles and strips in <c>npc.config.ts</c> have no port and need none.
    /// </summary>
    public class CrowdSpawner : MonoBehaviour
    {
        [Tooltip("Pedestrian prefabs from The Block → Build Pedestrians.")]
        [SerializeField] private List<GameObject> pedestrianPrefabs = new();

        [Tooltip("Agents alive at once. A budget, not a world population.")]
        [SerializeField] private int liveCount = 40;

        [Tooltip("Nobody is spawned closer than this — a person appearing in front of you reads badly.")]
        [SerializeField] private float spawnMinRadius = 25f;

        [Tooltip("...nor further than this, which must stay under the cull distance.")]
        [SerializeField] private float spawnMaxRadius = 70f;

        [Tooltip("Beyond this from the player, an agent is recycled. `npc.config.ts` cullDistanceM.")]
        [SerializeField] private float cullDistance = 90f;

        [Tooltip("Walking speed range, randomised per person. `npc.config.ts` speed.")]
        [SerializeField] private Vector2 speedRange = new(1.0f, 1.5f);

        [Tooltip("Metres of height difference that still counts as street level, so nobody spawns on a roof.")]
        [SerializeField] private float sameStoreyBand = 3f;

        [Tooltip("Seeded, so a run is reproducible — the same call WorldBuilder makes.")]
        [SerializeField] private int seed = 20260816;

        [Tooltip("Attempts per spawn before this agent waits for the next tick.")]
        [SerializeField] private int sampleAttempts = 12;

        [Tooltip("Seconds between recycle sweeps. Every frame is pointless — people do not walk 90 m in one.")]
        [SerializeField] private float sweepInterval = 0.5f;

        [SerializeField] private PlayerController player;
        [SerializeField] private VehicleEnterExit vehicles;

        private readonly List<Pedestrian> _crowd = new();
        private System.Random _rng;
        private float _sweepIn;

        /// <summary>Everyone currently walking. U18's run-over pass and U19's police both want this.</summary>
        public IReadOnlyList<Pedestrian> Crowd => _crowd;

        private void Awake()
        {
            _rng = new System.Random(seed);

            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (vehicles == null) vehicles = FindAnyObjectByType<VehicleEnterExit>();

            pedestrianPrefabs.RemoveAll(p => p == null);
            if (pedestrianPrefabs.Count == 0)
            {
                Debug.LogError(
                    "CrowdSpawner: no pedestrian prefabs assigned, so the street will be empty. Build " +
                    "them with The Block → Build Pedestrians and drag them onto this component.", this);
                enabled = false;
                return;
            }

            for (int i = 0; i < liveCount; i++) _crowd.Add(CreateAgent(i));
        }

        private void Update()
        {
            var focus = Focus();
            if (focus == null) return;

            _sweepIn -= Time.deltaTime;
            if (_sweepIn > 0f) return;
            _sweepIn = sweepInterval;

            float cullSqr = cullDistance * cullDistance;
            var centre = focus.position;

            foreach (var pedestrian in _crowd)
            {
                if (pedestrian == null) continue;

                bool tooFar = (pedestrian.transform.position - centre).sqrMagnitude > cullSqr;
                if (pedestrian.Active && !tooFar) continue;

                Place(pedestrian, centre);
            }
        }

        /// <summary>
        /// What the crowd walks around: the player on foot, or the vehicle they are driving.
        ///
        /// Same resolution as the map's (U14) — and it has to be, or the crowd empties out around
        /// the player's abandoned body the moment they get into a car.
        /// </summary>
        private Transform Focus()
        {
            if (vehicles != null && vehicles.Mode != GameMode.OnFoot)
            {
                var vehicle = vehicles.ActiveVehicle;
                if (vehicle != null) return vehicle.Anchor;
            }

            return player != null ? player.transform : null;
        }

        private Pedestrian CreateAgent(int index)
        {
            var prefab = pedestrianPrefabs[_rng.Next(pedestrianPrefabs.Count)];
            var instance = Instantiate(prefab, transform);
            instance.name = $"Pedestrian_{index:00}";
            instance.SetActive(false);

            if (instance.TryGetComponent<Pedestrian>(out var pedestrian)) return pedestrian;

            Debug.LogError($"CrowdSpawner: {prefab.name} has no Pedestrian component.", instance);
            return null;
        }

        /// <summary>
        /// Drops a pedestrian back into the world somewhere in the ring around the focus.
        ///
        /// The height band is the same guard <see cref="Pedestrian"/> applies when it re-targets, and
        /// for the same reason: the bake cannot tell a flat roof from a pavement, so without it a share of
        /// the crowd spawns three storeys up and strolls around a rooftop in plain view.
        /// </summary>
        private void Place(Pedestrian pedestrian, Vector3 centre)
        {
            for (int attempt = 0; attempt < sampleAttempts; attempt++)
            {
                float angle = (float)_rng.NextDouble() * Mathf.PI * 2f;
                float radius = _rng.Range(spawnMinRadius, spawnMaxRadius);
                var candidate = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

                // A generous sample radius, because the ring almost never lands on pavement first
                // try — most of a city block is building, and all of the street is carved away.
                if (!NavMesh.SamplePosition(candidate, out var hit, 12f, NavMesh.AllAreas)) continue;
                if (Mathf.Abs(hit.position.y - centre.y) > sameStoreyBand) continue;
                if ((hit.position - centre).sqrMagnitude < spawnMinRadius * spawnMinRadius) continue;

                if (!pedestrian.gameObject.activeSelf) pedestrian.gameObject.SetActive(true);
                if (pedestrian.TryGetComponent<NpcAppearance>(out var appearance)) appearance.Randomize(_rng);
                pedestrian.Begin(hit.position, _rng.Range(speedRange.x, speedRange.y), _rng);
                return;
            }

            // Nowhere to put this one — the player is off the NavMesh, on the beach or out at sea.
            // Park it and try again on the next sweep rather than dropping it into the water.
            pedestrian.End();
            pedestrian.gameObject.SetActive(false);
        }
    }
}
