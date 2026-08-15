using System.Collections.Generic;
using TheBlock.Core;
using TheBlock.Player;
using TheBlock.Traffic;
using TheBlock.Vehicles;
using UnityEngine;

namespace TheBlock.Npc
{
    /// <summary>
    /// The street crowd — U16b's population, as opposed to <see cref="Pedestrian"/>'s behaviour.
    ///
    /// <b>Placement is the original's, verbatim.</b> 33 painted rectangles at nine people each, 38
    /// sidewalk strips split into two opposing lanes, a per-district fallback, and two gated
    /// crossers per zebra — baked into <see cref="CrowdSeedTable"/> by <c>CrowdBuilder</c>. U16
    /// replaced all of that with a spawn ring that follows the player; this puts the authored
    /// pavements back, because they are where the game's streets actually look inhabited.
    ///
    /// <b>Bodies are a pool; people are a table.</b> The web keeps ~700 objects alive and freezes
    /// the far ones. Here the far ones do not exist: a seed is a struct, and the nearest
    /// <see cref="liveCap"/> of them inside <see cref="cullDistance"/> are handed a prefab instance.
    /// This is the unit's one deliberate deviation from "literal", and it is the deviation U16
    /// measured the case for — the crowd's steady cost was zero and the whole stutter was ninety
    /// <c>Instantiate</c> calls in a single frame. Nothing about a person is lost by it: the seed
    /// carries their position, target, pace, path position and face, so walking away and back finds
    /// them mid-stride where you left them.
    ///
    /// <c>C</c> toggles the whole crowd in Play, which is the instrument for judging its frame cost.
    /// </summary>
    public class CrowdSpawner : MonoBehaviour
    {
        [Header("Build products")]
        [Tooltip("Where everyone stands, from The Block → Bake Crowd Seeds.")]
        [SerializeField] private CrowdSeedTable seedTable;

        [Tooltip("Pedestrian prefabs from The Block → Build Pedestrians, in config.people order.")]
        [SerializeField] private List<GameObject> pedestrianPrefabs = new();

        [Header("From npc.config.ts")]
        [Tooltip("Beyond this from the player, a person is released back to the table. cullDistanceM.")]
        [SerializeField] private float cullDistance = 90f;

        [Tooltip("Seconds idling at each reached spot. pauseTime.")]
        [SerializeField] private float pauseTime = 1.2f;

        [Tooltip("How far the next wander target may hop. stepRadius.")]
        [SerializeField] private float stepRadius = 8f;

        [Tooltip("Sideways gap between a strip's two lanes. Crossers use half. laneOffset.")]
        [SerializeField] private float laneOffset = 1f;

        [Header("From pedestrian.ts / crowd.ts")]
        [Tooltip("Close enough to a target to count as arrived. ARRIVE.")]
        [SerializeField] private float arriveDistance = 0.3f;

        [Tooltip("Keeps wander targets off the very edge of their rectangle. ZONE_INSET.")]
        [SerializeField] private float zoneInset = 0.6f;

        [Tooltip("Seconds blocked by a car before a wanderer picks a new target. AVOID_REPICK.")]
        [SerializeField] private float avoidRepickSec = 0.6f;

        [Tooltip("Padding around a car's footprint that counts as blocked. CAR_AVOID_BUF = 0.3 + 0.3.")]
        [SerializeField] private float carAvoidBuffer = 0.6f;

        [Tooltip("Past this from BOTH its own kerbs, a crosser is on the road and traffic brakes for " +
                 "it. Below it, it is waiting and brakes for nobody. CURB_MARGIN.")]
        [SerializeField] private float curbMargin = 1f;

        [Header("Unity-only")]
        [Tooltip("Most bodies alive at once. Set this from the bake's measured peak-within-cull.")]
        [SerializeField] private int liveCap = 160;

        [Tooltip("Metres past the cull distance before a person is actually released, so somebody " +
                 "standing exactly on the boundary does not flicker in and out.")]
        [SerializeField] private float cullHysteresis = 5f;

        [Tooltip("Seconds between binder sweeps. Every frame is pointless — nobody walks 90 m in one.")]
        [SerializeField] private float sweepInterval = 0.25f;

        [Tooltip("Seeds bound per sweep. Trickled for the reason U16 measured: the burst IS the hitch.")]
        [SerializeField] private int bindsPerSweep = 8;

        [Tooltip("New bodies instantiated per frame. The other half of the same lesson.")]
        [SerializeField] private int instantiatesPerFrame = 2;

        [Tooltip("Rooftops bake walkable, so a re-target more than this far up or down is rejected.")]
        [SerializeField] private float sameStoreyBand = 2.5f;

        [Tooltip("Tries per re-target before a person keeps its current target and waits a beat.")]
        [SerializeField] private int sampleAttempts = 6;

        [Tooltip("How far a NavMesh sample may snap. SMALL on purpose: a generous radius stops the " +
                 "sample being a test of the point at all and starts being a search.")]
        [SerializeField] private float sampleRadius = 0.75f;

        [Tooltip("What counts as ground. Leave the Pedestrian layer OUT or people stand on each other.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("Seeded, so a run is reproducible — the same call WorldBuilder makes.")]
        [SerializeField] private int seed = 20260816;

        [Header("Crossings")]
        [Tooltip("Off makes a crosser step off the kerb as soon as its light is red, whether or not a " +
                 "car is standing on the zebra. The original checks; U16 deleted its version of this " +
                 "check because it could not tell a stopped car from a moving one.")]
        [SerializeField] private bool requireClearLine = true;

        [Tooltip("Seconds a crosser will wait for the zebra to clear before stepping off anyway. Safe " +
                 "by construction: a metre from the kerb it is IsCrossing and the traffic brakes.")]
        [SerializeField] private float clearLineWaitCap = 6f;

        [Tooltip("Samples along the zebra when testing whether it is clear.")]
        [SerializeField] private int clearLineSamples = 6;

        [Header("Wiring")]
        [SerializeField] private PlayerController player;
        [SerializeField] private VehicleEnterExit vehicles;
        [SerializeField] private TrafficSystem traffic;

        // --- runtime ---------------------------------------------------------------------------

        private CrowdSeedTable.Seed[] _seeds;
        private CrowdSeedTable.Rect[] _rects;
        private readonly List<CrowdSeedTable.LanePath> _paths = new();
        private Crossing[] _gates;
        private Pedestrian[] _bound;
        private float[] _waitingSince;

        private readonly List<Pedestrian> _live = new();
        private readonly List<List<Pedestrian>> _pool = new();
        private readonly List<int> _candidates = new();

        private System.Random _rng;
        private float _sweepIn;
        private bool _hidden;
        private int _spawnedThisFrame;

        /// <summary>Everyone with a body right now. U18's run-over pass and U19's police want this.</summary>
        public IReadOnlyList<Pedestrian> Crowd => _live;

        public int LiveCount => _live.Count;
        public int SeedCount => _seeds?.Length ?? 0;
        public int SeedsInRange { get; private set; }
        public int PoolSize { get; private set; }

        private void Awake() => Bind();

        /// <summary>
        /// Builds the runtime copy of the table.
        ///
        /// Called from Awake and again from Update if a mid-Play recompile wiped it — the domain
        /// reloads, non-serialized fields come back null, and Awake does not run again. Without the
        /// guard the crowd silently stops existing for the rest of the session.
        /// </summary>
        private void Bind()
        {
            _rng = new System.Random(seed);
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (vehicles == null) vehicles = FindAnyObjectByType<VehicleEnterExit>();
            if (traffic == null) traffic = FindAnyObjectByType<TrafficSystem>();

            pedestrianPrefabs.RemoveAll(p => p == null);
            _pool.Clear();
            _live.Clear();
            for (int i = 0; i < pedestrianPrefabs.Count; i++) _pool.Add(new List<Pedestrian>());
            PoolSize = 0;

            // Anything left over from a previous bind — after a domain reload the children are still
            // in the scene but every reference to them is gone.
            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);

            if (seedTable == null || seedTable.Seeds.Length == 0)
            {
                Debug.LogError(
                    "CrowdSpawner: no seed table, so the street will be empty. Run " +
                    "The Block → Bake Crowd Seeds.", this);
                _seeds = System.Array.Empty<CrowdSeedTable.Seed>();
                return;
            }

            if (pedestrianPrefabs.Count == 0)
            {
                Debug.LogError(
                    "CrowdSpawner: no pedestrian prefabs assigned. Run The Block → Build Pedestrians.", this);
                _seeds = System.Array.Empty<CrowdSeedTable.Seed>();
                return;
            }

            _rects = seedTable.Rects;
            _paths.Clear();
            _paths.AddRange(seedTable.Paths);

            var baked = seedTable.Seeds;
            _seeds = new CrowdSeedTable.Seed[baked.Length];
            System.Array.Copy(baked, _seeds, baked.Length);

            _gates = new Crossing[_seeds.Length];
            _bound = new Pedestrian[_seeds.Length];
            _waitingSince = new float[_seeds.Length];
        }

        /// <summary>
        /// Crossers are built here rather than baked: they hold a reference to the live
        /// <see cref="Crossing"/> whose <c>Gate</c> <c>TrafficLightSystem</c> fills in at ITS Start.
        /// Ordering between the two does not matter — <see cref="Crossing.MayCross"/> reads open
        /// while the gate is null, so the worst case is one frame of a green light.
        /// </summary>
        private void Start() => AddCrossers();

        private void AddCrossers()
        {
            if (_seeds == null || _seeds.Length == 0) return;

            var crossings = CrossingRegistry.All;
            if (crossings.Count == 0) return;

            var seeds = new List<CrowdSeedTable.Seed>(_seeds);
            var gates = new List<Crossing>(_gates);

            float half = laneOffset * 0.5f;
            int added = 0;

            foreach (var crossing in crossings)
            {
                var a = crossing.KerbA;
                var b = crossing.KerbB;

                var across = b - a;
                across.y = 0f;
                if (across.sqrMagnitude < 0.01f) continue;

                // Along the street, so the two crossers walk parallel lines about a metre apart
                // instead of through each other.
                var along = Vector3.Cross(Vector3.up, across.normalized);

                for (int side = 0; side < 2; side++)
                {
                    float sign = side == 0 ? 1f : -1f;
                    var path = Lane(a + along * (half * sign), b + along * (half * sign));

                    _paths.Add(path);
                    seeds.Add(new CrowdSeedTable.Seed
                    {
                        Position = side == 0 ? path.At(0f) : path.At(path.Length),
                        Target = Vector3.zero,
                        S = side == 0 ? 0f : path.Length,
                        Dir = (sbyte)(side == 0 ? 1 : -1),
                        Face = (byte)(added % Mathf.Max(1, pedestrianPrefabs.Count)),
                        Speed = Mathf.Lerp(1f, 1.5f, (float)_rng.NextDouble()),
                        Mode = CrowdSeedTable.Mode.Crosser,
                        RectId = -1,
                        PathId = _paths.Count - 1,
                    });
                    gates.Add(crossing);
                    added++;
                }
            }

            _seeds = seeds.ToArray();
            _gates = gates.ToArray();
            _bound = new Pedestrian[_seeds.Length];
            _waitingSince = new float[_seeds.Length];
        }

        /// <summary>A two-point lane, resampled so each sample can carry its own ground height.</summary>
        private static CrowdSeedTable.LanePath Lane(Vector3 a, Vector3 b)
        {
            float length = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z));
            int samples = Mathf.Max(2, Mathf.CeilToInt(length / 2f) + 1);

            var points = new Vector3[samples];
            for (int i = 0; i < samples; i++) points[i] = Vector3.Lerp(a, b, i / (float)(samples - 1));

            return new CrowdSeedTable.LanePath { Points = points, Length = Mathf.Max(length, 0.01f) };
        }

        // --- the frame -------------------------------------------------------------------------

        private void Update()
        {
            if (_seeds == null) Bind();
            if (_seeds == null || _seeds.Length == 0) return;

            if (UnityEngine.InputSystem.Keyboard.current?.cKey.wasPressedThisFrame == true) Toggle();
            if (_hidden) return;

            _spawnedThisFrame = 0;

            var focus = Focus();
            if (focus != null)
            {
                _sweepIn -= Time.deltaTime;
                if (_sweepIn <= 0f)
                {
                    _sweepIn = sweepInterval;
                    Sweep(focus.position);
                }
            }

            var tuning = CurrentTuning();
            float dt = Time.deltaTime;
            for (int i = 0; i < _live.Count; i++) _live[i].Tick(dt, tuning);
        }

        private Pedestrian.Tuning CurrentTuning() => new()
        {
            StepRadius = stepRadius,
            PauseTime = pauseTime,
            ArriveDistance = arriveDistance,
            ZoneInset = zoneInset,
            AvoidRepickSec = avoidRepickSec,
            SameStoreyBand = sameStoreyBand,
            CurbMargin = curbMargin,
            SampleAttempts = sampleAttempts,
            SampleRadius = sampleRadius,
            GroundMask = groundMask.value,
        };

        /// <summary>
        /// What the crowd is arranged around: the player on foot, or the vehicle they are driving.
        ///
        /// Same resolution as the map's (U14) — and it has to be, or the crowd empties out around the
        /// player's abandoned body the moment they get into a car.
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

        /// <summary>
        /// Release whoever has walked out of range, then bind the nearest unbound seeds that are in
        /// it. Linear over the whole table — about 1,600 distance compares, which is ~0.01 ms and
        /// needs no spatial index. If the table ever grows past a few thousand, a uniform bucket grid
        /// belongs in the ASSET, not here.
        /// </summary>
        private void Sweep(Vector3 focus)
        {
            float releaseSqr = (cullDistance + cullHysteresis) * (cullDistance + cullHysteresis);
            float bindSqr = cullDistance * cullDistance;

            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var body = _live[i];
                if ((body.transform.position - focus).sqrMagnitude <= releaseSqr) continue;
                Release(body);
            }

            _candidates.Clear();
            int inRange = 0;

            for (int i = 0; i < _seeds.Length; i++)
            {
                float sqr = (_seeds[i].Position - focus).sqrMagnitude;
                if (sqr > bindSqr) continue;

                inRange++;
                if (_bound[i] == null) _candidates.Add(i);
            }

            SeedsInRange = inRange;
            if (_candidates.Count == 0) return;

            // Crossers first at equal distance: an unbound crosser is a dead zebra, and U17's traffic
            // has nobody to brake for.
            _candidates.Sort((a, b) =>
            {
                bool crosserA = _seeds[a].Mode == CrowdSeedTable.Mode.Crosser;
                bool crosserB = _seeds[b].Mode == CrowdSeedTable.Mode.Crosser;
                if (crosserA != crosserB) return crosserA ? -1 : 1;

                float da = (_seeds[a].Position - focus).sqrMagnitude;
                float db = (_seeds[b].Position - focus).sqrMagnitude;
                return da.CompareTo(db);
            });

            int budget = bindsPerSweep;
            foreach (int index in _candidates)
            {
                if (budget <= 0 || _live.Count >= liveCap) break;
                if (TryBind(index)) budget--;
            }
        }

        private bool TryBind(int index)
        {
            ref var seed = ref _seeds[index];
            int face = seed.Face % pedestrianPrefabs.Count;

            var body = Take(face);
            if (body == null) return false;

            var path = seed.PathId >= 0 && seed.PathId < _paths.Count ? _paths[seed.PathId] : null;
            var gate = _gates[index];
            bool hasRect = seed.RectId >= 0 && _rects != null && seed.RectId < _rects.Length;
            var rect = hasRect ? _rects[seed.RectId] : default;

            body.gameObject.SetActive(true);
            body.Bind(this, index, seed, path, gate, rect, hasRect, _rng);

            _bound[index] = body;
            _waitingSince[index] = 0f;
            _live.Add(body);
            return true;
        }

        private void Release(Pedestrian body)
        {
            int index = body.SeedIndex;
            if (index >= 0 && index < _seeds.Length)
            {
                _seeds[index] = body.Release();
                _bound[index] = null;
            }
            else body.Release();

            _live.Remove(body);
            body.gameObject.SetActive(false);
        }

        /// <summary>An idle body of this face, or a new one if the frame's budget allows.</summary>
        private Pedestrian Take(int face)
        {
            var pool = _pool[face];
            for (int i = 0; i < pool.Count; i++)
                if (!pool[i].Live) return pool[i];

            if (_spawnedThisFrame >= instantiatesPerFrame) return null;
            _spawnedThisFrame++;

            var instance = Instantiate(pedestrianPrefabs[face], transform);
            instance.name = $"{pedestrianPrefabs[face].name}_{pool.Count:00}";
            instance.SetActive(false);

            if (!instance.TryGetComponent<Pedestrian>(out var pedestrian))
            {
                Debug.LogError($"CrowdSpawner: {instance.name} has no Pedestrian component.", instance);
                Destroy(instance);
                return null;
            }

            pool.Add(pedestrian);
            PoolSize++;
            return pedestrian;
        }

        // --- what a pedestrian asks -------------------------------------------------------------

        /// <summary>
        /// Is a moving car sitting on this point? The port of <c>crowd.ts</c>'s <c>avoidCar</c>.
        ///
        /// Against the traffic's own box list, never a physics overlap: an overlap also finds the
        /// player's parked car and every wreck, and then people refuse to walk past a car that has
        /// been standing there all session.
        /// </summary>
        public bool BlockedByCar(Vector3 point)
        {
            if (traffic == null) return false;

            var cars = traffic.Cars;
            for (int i = 0; i < cars.Count; i++)
            {
                var car = cars[i];
                if (car == null || car.Mode == TrafficCar.State.Idle) continue;
                if (TrafficGeometry.PointInBox(car.Box, point.x, point.z, carAvoidBuffer)) return true;
            }

            return false;
        }

        /// <summary>
        /// May a crosser step off the kerb? The light, and then the zebra itself.
        ///
        /// The second half is the awkward one and it is why <see cref="requireClearLine"/> and
        /// <see cref="clearLineWaitCap"/> are both serialized and live in Play. The original gates on
        /// <c>mayCross() &amp;&amp; crossingClear()</c>, but the geometry makes those two fight: the
        /// zebra is 10 m from the junction and cars stop at 8 m, so a legitimately queued car covers
        /// the crossing exactly when the light is red for it. U16 already deleted a version of this
        /// check for a related reason — it could not tell a stopped car from a parked one. The cap is
        /// the compromise: wait, then go anyway, which is safe because a metre from the kerb the
        /// crosser is <see cref="Pedestrian.IsCrossing"/> and U17's traffic brakes for it.
        /// </summary>
        public bool MayCross(Crossing crossing, CrowdSeedTable.LanePath path)
        {
            if (crossing == null) return true;
            if (!crossing.MayCross()) return false;

            if (!requireClearLine || traffic == null) return true;

            int index = System.Array.IndexOf(_gates, crossing);
            if (ClearLine(path))
            {
                if (index >= 0) _waitingSince[index] = 0f;
                return true;
            }

            if (index >= 0)
            {
                _waitingSince[index] += Time.deltaTime;
                if (_waitingSince[index] >= clearLineWaitCap) return true;
            }

            return false;
        }

        private bool ClearLine(CrowdSeedTable.LanePath path)
        {
            if (path == null) return true;

            for (int i = 0; i <= clearLineSamples; i++)
            {
                var point = path.At(path.Length * i / clearLineSamples);
                if (BlockedByCar(point)) return false;
            }

            return true;
        }

        // --- debug ------------------------------------------------------------------------------

        private void Toggle()
        {
            _hidden = !_hidden;
            if (_hidden)
            {
                for (int i = _live.Count - 1; i >= 0; i--) Release(_live[i]);
            }

            Debug.Log($"CrowdSpawner: crowd {(_hidden ? "HIDDEN" : "shown")} (C)");
        }

        private void OnDrawGizmosSelected()
        {
            if (seedTable == null) return;

            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
            foreach (var path in seedTable.Paths)
            {
                if (path?.Points == null || path.Points.Length < 2) continue;
                for (int i = 1; i < path.Points.Length; i++) Gizmos.DrawLine(path.Points[i - 1], path.Points[i]);
            }

            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.7f);
            foreach (var seed in seedTable.Seeds)
                if (seed.Mode == CrowdSeedTable.Mode.Wander)
                    Gizmos.DrawWireSphere(seed.Position, 0.3f);
        }
    }
}
