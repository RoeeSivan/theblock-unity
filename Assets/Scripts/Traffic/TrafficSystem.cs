using System.Collections;
using System.Collections.Generic;
using TheBlock.Core;
using TheBlock.Npc;
using TheBlock.Player;
using TheBlock.Vehicles;
using UnityEngine;

namespace TheBlock.Traffic
{
    /// <summary>
    /// Ambient traffic — the port of <c>traffic-cars.ts</c>'s sim loop, on U16's graph.
    ///
    /// Each car drives the right-hand lane of its street, picks a random turn at every junction and
    /// eases its speed toward <c>min(cruise, follow, red light, corner)</c>, so queuing behind slower
    /// cars, stopping at reds and slowing into turns all fall out of one number rather than out of
    /// three behaviours that have to agree.
    ///
    /// <b>A pool that follows the player, not a population.</b> The web build spawns 130 cars at boot
    /// across all 12.7 km and freezes the ones past 160 m; that is the right shape for three.js,
    /// where a car is a handful of matrices, and the wrong one here, where it is a GameObject with a
    /// Rigidbody, a collider and a place in the physics scene. So the pool follows the player, and
    /// what is missing is the far half of the city, which was never on screen. Same argument and
    /// same conclusion as U16's crowd.
    ///
    /// <b>How many is not a setting — it is measured.</b> 130 cars over 12,759 m of network is one
    /// car per 98 m, and every sweep this counts the metres of centreline inside the cull radius and
    /// asks for that many. Downtown gets a busy street and the edge of the map gets three cars,
    /// without either being typed in. The alternative was a fixed count, and it is worth recording
    /// why that failed: 32 looked right on paper and turned out to be 1 car per 38 m around the
    /// starting lot, which is jam density at signalised junctions and gridlocked inside a minute.
    ///
    /// <b>Everything that can gridlock, the web build already learned about the hard way</b>, and all
    /// four of its answers are ported rather than simplified: id-ordered anti-overlap yielding (so a
    /// yield cycle cannot form), a right-of-way push through mutual standoffs, a per-car jittered
    /// stuck timer that nudges a pinned car free, and the three-zone stop-line logic that stops a car
    /// committing to a junction it is already halfway into.
    /// </summary>
    [DisallowMultipleComponent]
    public class TrafficSystem : MonoBehaviour
    {
        // --- wiring ---------------------------------------------------------------------------

        [Tooltip("The baked street graph. Written by The Block → Build World.")]
        [SerializeField] private TrafficNetwork network;

        [Tooltip("One prefab per traffic model, from The Block → Build Traffic Cars.")]
        [SerializeField] private List<GameObject> carPrefabs = new();

        [SerializeField] private TrafficLightSystem lights;
        [SerializeField] private PlayerController player;
        [SerializeField] private VehicleEnterExit vehicles;

        [Tooltip("So cars brake for people actually standing on a zebra. Optional.")]
        [SerializeField] private CrowdSpawner crowd;

        // --- population -----------------------------------------------------------------------

        [Tooltip("Ceiling on the pool. The live count is DERIVED from how much street is in range " +
                 "(see metresPerCar); this is the safety rail and the number of prefab instances made.")]
        [SerializeField] private int maxLive = 40;

        [Tooltip("Metres of centreline per car — the web build's own density, and the whole point of " +
                 "the pool. Set from config: 12,759 m of network / 130 cars = one car per 98 m.")]
        [SerializeField] private float metresPerCar = 98f;

        [Tooltip("Multiplier on that density. 1 is the shipped game. Raise it for a busier city, and " +
                 "watch the junctions — this is the number that decides whether the grid gridlocks.")]
        [SerializeField] private float densityScale = 1f;

        [Tooltip("Metres past which a car is recycled. `config.traffic.cullDistanceM`.")]
        [SerializeField] private float cullDistance = 160f;

        [Tooltip("Nothing is placed closer than this. A car appearing in front of you reads badly.")]
        [SerializeField] private float spawnMinRadius = 55f;

        [Tooltip("...nor further than this. Kept well inside the cull distance, or a car spawns at " +
                 "150 m, drives ten metres the wrong way and is recycled again.")]
        [SerializeField] private float spawnMaxRadius = 125f;

        [Tooltip("Metres of clear road a spawn point needs. Two cars in the same spot start a shunt.")]
        [SerializeField] private float spawnClearance = 12f;

        [Tooltip("Seconds between recycle sweeps. Cars do not travel 160 m in one frame.")]
        [SerializeField] private float sweepInterval = 0.4f;

        [Tooltip("Placements per sweep. U16 measured the stutter as the spawn BURST, not the crowd; " +
                 "the same applies here, and for cars each placement is an Instantiate plus a " +
                 "collider entering the physics scene.")]
        [SerializeField] private int placementsPerSweep = 3;

        [Tooltip("Attempts to find a lane point in the ring before this car waits for the next sweep.")]
        [SerializeField] private int sampleAttempts = 24;

        [Tooltip("Seeded, so a run is reproducible.")]
        [SerializeField] private int seed = 20260817;

        // --- driving, all from config.traffic ---------------------------------------------------

        [SerializeField] private float defaultSpeed = 12f;

        // `config.traffic.laneOffset` is NOT here: it is the default innermost-lane offset, and a
        // divided road overrides it per street, so the builder resolves the two into each edge's own
        // `LaneOffset` and the sim only ever needs the gap between lanes.
        [SerializeField] private float laneGap = 3.2f;
        [SerializeField] private float accel = 3f;
        [SerializeField] private float decel = 8f;
        [SerializeField] private float gapStop = 4f;
        [SerializeField] private float gapFollow = 14f;
        [SerializeField] private float gapLateral = 1.8f;
        [SerializeField] private float antiOverlapMargin = 0.8f;
        [SerializeField] private float playerRadius = 2.2f;
        [SerializeField] private float pedRadius = 0.8f;
        [SerializeField] private float cornerDist = 6f;
        [SerializeField] private float cornerSpeedFactor = 0.6f;
        [SerializeField] private float stopLineDist = 8f;

        [Tooltip("Extra metres kept back so a stopped car does not sit ON the zebra. The crossing is " +
                 "`crossingSetback` further from the junction than the stop line and 4 m long, so " +
                 "this is that setback plus half the zebra; the car's own half-length is added on top.")]
        [SerializeField] private float crossingClearance = 4f;

        [SerializeField] private float stopMargin = 0.4f;
        [SerializeField] private float lightCommitSpeed = 3f;
        [SerializeField] private float lightCommitDist = 2f;
        [SerializeField] private float stuckAfterSec = 20f;
        [SerializeField] private float stuckJitterSec = 6f;
        [SerializeField] private float stuckNudgeSec = 2f;
        [SerializeField] private float standoffPushSec = 2.5f;

        // --- carjacking, from config.traffic.hijack ------------------------------------------------

        [Tooltip("m/s below which a car counts as stopped, and so as stealable.")]
        [SerializeField] private float hijackStoppedSpeed = 0.5f;

        [Tooltip("Seconds a stopped car stands still once the on-foot player is in reach.")]
        [SerializeField] private float hijackHoldSec = 5f;

        // --- gas stops ---------------------------------------------------------------------------

        [Tooltip("The pumps, already converted to Unity space by the builder.")]
        [SerializeField] private Vector3[] gasPoints = System.Array.Empty<Vector3>();
        [SerializeField] private float gasTriggerRadius = 4f;
        [SerializeField] private float gasStopSeconds = 10f;
        [SerializeField] private float gasCooldownSec = 30f;

        // --- state ---------------------------------------------------------------------------

        private readonly List<TrafficCar> _cars = new();
        private readonly List<int> _nearbyEdges = new();
        private readonly List<Vector3> _crossers = new();
        private System.Random _rng;
        private float _sweepIn;
        private int _cursor;
        private bool _hidden;

        /// <summary>Metres of centreline inside the cull radius, refreshed every sweep.</summary>
        private float _streetInRange;

        /// <summary>What that length works out to in cars. Exposed for the play-test probe.</summary>
        public int TargetCount => Mathf.Clamp(
            Mathf.RoundToInt(_streetInRange * densityScale / Mathf.Max(1f, metresPerCar)), 0, _cars.Count);

        public float StreetInRange => _streetInRange;

        /// <summary>Every car currently in the world. U19's police and U18's run-over both want this.</summary>
        public IReadOnlyList<TrafficCar> Cars => _cars;

        /// <summary>
        /// Set by <c>WorldBuilder.Traffic</c> at build time — one call rather than twenty setters,
        /// so a field added to <c>config.traffic</c> lands here and nowhere else.
        ///
        /// The values are then serialized on the scene object and editable in the Inspector, which
        /// is the point: re-deriving them from the JSON at runtime would cost a parse on load and
        /// make every one of them un-tunable during Play.
        /// </summary>
        public void Configure(
            TrafficNetwork built, TrafficLightSystem lightSystem, Vector3[] pumps,
            TheBlockConfig.TrafficSpec spec, List<GameObject> prefabs)
        {
            network = built;
            lights = lightSystem;
            gasPoints = pumps;
            carPrefabs = prefabs;

            if (spec == null) return;

            // The density the shipped game actually has, read off its own two numbers rather than
            // guessed: 130 cars spread over the whole network. Everything about the pool follows
            // from this — see the class note.
            if (built != null && spec.CarCount > 0 && built.TotalLength > 0f)
                metresPerCar = built.TotalLength / spec.CarCount;

            cullDistance = spec.CullDistanceM;
            defaultSpeed = spec.DefaultSpeed;
            laneGap = spec.LaneGap;
            accel = spec.Accel;
            decel = spec.Decel;
            gapStop = spec.GapStop;
            gapFollow = spec.GapFollow;
            gapLateral = spec.GapLateral;
            antiOverlapMargin = spec.AntiOverlapMargin;
            playerRadius = spec.PlayerRadius;
            pedRadius = spec.PedRadius;
            cornerDist = spec.CornerDist;
            cornerSpeedFactor = spec.CornerSpeedFactor;
            stopLineDist = spec.StopLineDist;
            stopMargin = spec.StopMargin;
            lightCommitSpeed = spec.LightCommitSpeed;
            lightCommitDist = spec.LightCommitDist;
            stuckAfterSec = spec.StuckAfterSec;
            stuckJitterSec = spec.StuckJitterSec;
            stuckNudgeSec = spec.StuckNudgeSec;
            standoffPushSec = spec.StandoffPushSec;

            if (spec.Hijack != null)
            {
                hijackStoppedSpeed = spec.Hijack.StoppedSpeed;
                hijackHoldSec = spec.Hijack.HoldSec;
            }

            if (spec.GasStops == null) return;
            gasTriggerRadius = spec.GasStops.TriggerRadius;
            gasStopSeconds = spec.GasStops.StopSeconds;
            gasCooldownSec = spec.GasStops.CooldownSec;
        }

        private void Awake()
        {
            _rng = new System.Random(seed);

            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (vehicles == null) vehicles = FindAnyObjectByType<VehicleEnterExit>();
            if (crowd == null) crowd = FindAnyObjectByType<CrowdSpawner>();
            if (lights == null) lights = FindAnyObjectByType<TrafficLightSystem>();

            carPrefabs.RemoveAll(p => p == null);

            if (network == null || network.Edges.Length == 0)
            {
                Debug.LogError(
                    "TrafficSystem: no baked TrafficNetwork, so nothing can drive. Run " +
                    "The Block → Build World.", this);
                enabled = false;
                return;
            }

            if (carPrefabs.Count == 0)
            {
                Debug.LogError(
                    "TrafficSystem: no car prefabs assigned. Run The Block → Build Traffic Cars, " +
                    "then The Block → Build World.", this);
                enabled = false;
                return;
            }

            StartCoroutine(FillPool());
        }

        /// <summary>
        /// Creates the pool over several frames.
        ///
        /// The same lesson as U16's, and it is worth repeating because it was measured and not
        /// guessed: what stuttered there was not the crowd's steady cost — that came out at zero —
        /// it was ninety instantiations landing in one frame. A car is heavier than a pedestrian
        /// prefab in one respect that matters here: it brings a collider into the physics scene.
        /// </summary>
        private IEnumerator FillPool()
        {
            for (int i = 0; i < maxLive; i++)
            {
                var prefab = carPrefabs[_rng.Next(carPrefabs.Count)];
                var instance = Instantiate(prefab, transform);
                instance.name = $"TrafficCar_{i:00}";
                instance.SetActive(false);

                if (instance.TryGetComponent<TrafficCar>(out var car))
                {
                    car.Index = i;
                    _cars.Add(car);
                }
                else
                {
                    Debug.LogError($"TrafficSystem: {prefab.name} has no TrafficCar component.", instance);
                }

                if (i % placementsPerSweep == placementsPerSweep - 1) yield return null;
            }
        }

        private void Update()
        {
            // Debug only, exactly like U16's `C`: it answers "is the stutter the traffic?" without a
            // rebuild, and it goes once the unit is confirmed. Not a game control.
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.tKey.wasPressedThisFrame) ToggleTraffic();
        }

        private void ToggleTraffic()
        {
            _hidden = !_hidden;
            if (_hidden)
                foreach (var car in _cars)
                {
                    if (car != null) car.Retire();
                }

            Debug.Log($"TrafficSystem: traffic {(_hidden ? "HIDDEN" : "shown")} (T)");
        }

        private void FixedUpdate()
        {
            if (_hidden || _cars.Count == 0) return;

            var focus = Focus();
            if (focus == null) return;

            var centre = focus.position;
            float dt = Time.fixedDeltaTime;

            GatherCrossers();
            Sweep(centre, dt);

            foreach (var car in _cars)
            {
                if (car == null || car.Mode == TrafficCar.State.Idle) continue;

                if (car.Mode == TrafficCar.State.Wrecked)
                {
                    // Not driven any more, but still an obstacle — so its box has to keep up with
                    // wherever PhysX has taken it, or the traffic behind will queue at a ghost.
                    var wreckForward = car.transform.forward;
                    car.UpdateBox(car.transform.position, wreckForward);
                    continue;
                }

                Simulate(car, centre, dt);
            }
        }

        /// <summary>
        /// What the traffic is arranged around: the player on foot, or the vehicle they are driving.
        ///
        /// The same resolution the crowd and the map use, and it has to be — the moment the player
        /// gets into a car, arranging traffic around the body they left behind empties the road
        /// ahead of them.
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

        // --- population ---------------------------------------------------------------------------

        /// <summary>
        /// Recycles what has fallen out of range and brings the live count to whatever the street
        /// around the player is worth.
        ///
        /// <b>The count is derived, not configured</b>, and that is the correction that made this
        /// work at all. A fixed 32 was the plan, from an estimate that a 160 m disc holds thirty
        /// streets' worth of road; measured, the disc around the starting lot holds 1,230 m, which
        /// at the web build's own density is twelve cars. Thirty-two of them in that space is 1 car
        /// per 38 m — near jam density at signalised junctions — and it gridlocked in under a
        /// minute. Reading the length in range every sweep gets the shipped game's density
        /// everywhere by construction instead of by guessing, and it thins out automatically at the
        /// edge of the map where there is barely any road.
        /// </summary>
        private void Sweep(Vector3 centre, float dt)
        {
            _sweepIn -= dt;
            if (_sweepIn > 0f) return;
            _sweepIn = sweepInterval;

            GatherNearbyEdges(centre);

            float cullSqr = cullDistance * cullDistance;
            int live = 0;

            // Anything out of range or fallen through the world goes now, un-budgeted: switching a
            // GameObject off is not what costs, placing one is.
            foreach (var car in _cars)
            {
                if (car == null || car.Mode == TrafficCar.State.Idle) continue;

                var position = car.transform.position;
                bool tooFar = (position - centre).sqrMagnitude > cullSqr;
                bool sunk = position.y < -20f; // a wreck that found a hole in the world
                if (tooFar || sunk) { car.Retire(); continue; }
                live++;
            }

            int target = TargetCount;
            if (_nearbyEdges.Count == 0) return;

            // Trickled, for U16's reason: what stuttered there was the burst, not the population.
            int budget = placementsPerSweep;

            // Round-robin from where the last sweep stopped, so a capped sweep cannot starve the
            // tail of the pool — the same three cars would otherwise be re-placed forever.
            for (int n = 0; n < _cars.Count && budget > 0 && live < target; n++)
            {
                _cursor = (_cursor + 1) % _cars.Count;
                var car = _cars[_cursor];
                if (car == null || car.Mode != TrafficCar.State.Idle) continue;

                budget--;
                if (Place(car, centre)) live++;
            }

            // Walked from a dense grid into somewhere empty: retire the surplus, farthest first, so
            // the cars that go are the ones least likely to be on screen.
            while (live > target)
            {
                TrafficCar farthest = null;
                float worst = -1f;
                foreach (var car in _cars)
                {
                    if (car == null || car.Mode == TrafficCar.State.Idle) continue;
                    float sqr = (car.transform.position - centre).sqrMagnitude;
                    if (sqr <= worst) continue;
                    worst = sqr;
                    farthest = car;
                }

                if (farthest == null) break;
                farthest.Retire();
                live--;
            }
        }

        /// <summary>
        /// Streets within the cull radius, and how many metres of centreline that comes to.
        ///
        /// The edge filter is a bounding-sphere test — 142 of them, twice a second. The length is
        /// then accumulated per SEGMENT over the survivors only, because a street's bounding sphere
        /// says nothing about how much of it is actually in range, and the density the whole pool
        /// is sized from would inherit that error.
        /// </summary>
        private void GatherNearbyEdges(Vector3 centre)
        {
            _nearbyEdges.Clear();
            _streetInRange = 0f;

            var edges = network.Edges;
            float cullSqr = cullDistance * cullDistance;

            for (int i = 0; i < edges.Length; i++)
            {
                var edge = edges[i];

                float dx = edge.Centre.x - centre.x;
                float dz = edge.Centre.z - centre.z;
                if (Mathf.Sqrt(dx * dx + dz * dz) - edge.Radius > cullDistance) continue;

                var points = edge.Points;
                for (int p = 1; p < points.Length; p++)
                {
                    float mx = (points[p].x + points[p - 1].x) * 0.5f - centre.x;
                    float mz = (points[p].z + points[p - 1].z) * 0.5f - centre.z;
                    if (mx * mx + mz * mz > cullSqr) continue;
                    _streetInRange += Vector3.Distance(points[p - 1], points[p]);
                }

                // Too short to hold a car clear of the corner windows at both ends — counts towards
                // the density, cannot be spawned on.
                if (edge.Length > (cornerDist + 1f) * 2f) _nearbyEdges.Add(i);
            }
        }

        private bool Place(TrafficCar car, Vector3 centre)
        {
            float minSqr = spawnMinRadius * spawnMinRadius;
            float maxSqr = spawnMaxRadius * spawnMaxRadius;
            var view = Camera.main != null ? Camera.main.transform.forward : Vector3.forward;

            for (int attempt = 0; attempt < sampleAttempts; attempt++)
            {
                int edgeId = _nearbyEdges[_rng.Next(_nearbyEdges.Count)];
                var edge = network.Edges[edgeId];

                int dir = _rng.Next(2) == 0 ? 1 : -1;
                int lane = _rng.Next(Mathf.Max(1, edge.Lanes));
                float margin = cornerDist + 1f;
                float s = margin + (float)_rng.NextDouble() * (edge.Length - margin * 2f);

                network.SampleLane(edgeId, s, dir, network.LaneOffsetFor(edgeId, lane, laneGap),
                    out var position, out var tangent);

                var offset = position - centre;
                offset.y = 0f;
                float sqr = offset.sqrMagnitude;
                if (sqr < minSqr || sqr > maxSqr) continue;

                // For most of the attempts, insist the point is out of the view cone. A car fading
                // in at 80 m is only invisible if you are not looking at it, and in a grid city most
                // of the network is behind a building from wherever you stand — so this succeeds
                // often, and the last few attempts drop the requirement rather than leave a hole.
                if (attempt < sampleAttempts * 2 / 3 &&
                    Vector3.Dot(offset.normalized, view) > 0.3f) continue;

                if (Occupied(position, car)) continue;

                car.EdgeId = edgeId;
                car.Dir = dir;
                car.Lane = lane;
                car.S = s;
                car.Speed = defaultSpeed;
                car.Cruise = defaultSpeed;
                car.StuckLimit = stuckAfterSec + (float)_rng.NextDouble() * stuckJitterSec;
                car.GasCool = gasCooldownSec; // no diving into a pump the moment it arrives
                car.Stuck = 0f;

                if (!car.gameObject.activeSelf) car.gameObject.SetActive(true);
                if (car.PaintCount > 0) car.SetPaint(_rng.Next(car.PaintCount));
                car.Place(position, tangent);
                return true;
            }

            return false;
        }

        private bool Occupied(Vector3 point, TrafficCar self)
        {
            float radiusSqr = spawnClearance * spawnClearance;
            foreach (var other in _cars)
            {
                if (other == null || other == self || other.Mode == TrafficCar.State.Idle) continue;
                if ((other.transform.position - point).sqrMagnitude < radiusSqr) return true;
            }

            return false;
        }

        // --- carjack ------------------------------------------------------------------------------

        /// <summary>
        /// Everything the drivable copy of a stolen car needs, snapshotted the moment it is taken.
        ///
        /// There is no surface height in here and no heading in radians, unlike the web build's
        /// <c>HijackSpec</c>. Both prefabs put their origin on the tyre contact patch, so the traffic
        /// car's own transform IS where the drivable one goes — no ride-height arithmetic, no frame
        /// conversion, and nothing that can be off by half a car.
        /// </summary>
        public readonly struct Claimed
        {
            public Claimed(string modelName, Material paint, Vector3 position, Quaternion rotation)
            {
                ModelName = modelName;
                Paint = paint;
                Position = position;
                Rotation = rotation;
            }

            /// <summary>The <c>config.vehicle.cars</c> name to spawn — "Tesla", "Audi", "Avenger".</summary>
            public string ModelName { get; }

            /// <summary>The exact street-palette material it was wearing, so the theft keeps its colour.</summary>
            public Material Paint { get; }

            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
        }

        /// <summary>
        /// The nearest STOPPED live car within <paramref name="radius"/> of a point, or null.
        ///
        /// A car mid-turn through a junction is never offered, which is the web build's rule and a
        /// good one: stealing there leaves you parked diagonally across the intersection. A wreck is
        /// not offered either — it is stopped, but it is not a car anyone is driving away.
        /// </summary>
        public TrafficCar NearestStopped(Vector3 point, float radius)
        {
            TrafficCar best = null;
            float bestSqr = radius * radius;

            foreach (var car in _cars)
            {
                if (car == null || car.Mode != TrafficCar.State.Driving) continue;
                if (car.InCorner || car.Speed >= hijackStoppedSpeed) continue;

                var offset = car.transform.position - point;
                offset.y = 0f;
                float sqr = offset.sqrMagnitude;
                if (sqr > bestSqr) continue;

                bestSqr = sqr;
                best = car;
            }

            return best;
        }

        /// <summary>Freezes a stopped car for <c>hijack.holdSec</c> while the player walks over.</summary>
        public void Hold(TrafficCar car)
        {
            if (car != null) car.Hold = hijackHoldSec;
        }

        /// <summary>
        /// Takes the car: snapshot what the drivable copy needs, then hand the slot back to the pool.
        ///
        /// <b>The recycle is a retire</b>, and that is the whole of it. The web build has to teleport
        /// the stolen car to a far-away lane picked over up to thirty tries, because its pool is a
        /// fixed set of instanced-mesh slots allocated at boot and a car can never stop existing. This
        /// pool already retires and re-places cars twice a second, from a ring 55–125 m out and
        /// preferentially outside the view cone — so "recycle it somewhere you will not see it arrive"
        /// is not a thing to build here, it is the thing that was already running.
        /// </summary>
        public Claimed Claim(TrafficCar car)
        {
            var snapshot = new Claimed(
                car.ModelName, car.CurrentPaint, car.transform.position, car.transform.rotation);

            car.Retire();
            return snapshot;
        }

        /// <summary>
        /// The people cars have to brake for: those actually standing on a carriageway.
        ///
        /// Kerb-waiters are deliberately excluded, and that exclusion is the whole reason there is no
        /// deadlock. A pedestrian waiting at a kerb gates on the LIGHT and never on traffic; a
        /// pedestrian already crossing makes cars stop. So nobody ever waits on somebody who is
        /// waiting on them — the same argument <c>traffic-cars.ts</c> spells out, and it stops being
        /// true the moment a queued pedestrian is added to this list.
        /// </summary>
        private void GatherCrossers()
        {
            _crossers.Clear();
            if (crowd == null) return;

            foreach (var pedestrian in crowd.Crowd)
            {
                if (pedestrian == null || !pedestrian.IsCrossing) continue;
                _crossers.Add(pedestrian.transform.position);
            }
        }

        // --- the drive loop -------------------------------------------------------------------

        private void Simulate(TrafficCar car, Vector3 focus, float dt)
        {
            var edge = network.Edges[car.EdgeId];

            if (car.Push > 0f) car.Push -= dt;

            // -- target speed: the minimum of cruise, following, the light, and the corner --------
            float target = car.Cruise;
            if (car.InCorner) target = Mathf.Min(target, car.Cruise * cornerSpeedFactor);

            float gap = NearestGap(car, focus);
            if (gap < gapFollow && car.Stuck < car.StuckLimit)
            {
                float t = Mathf.Clamp01((gap - gapStop) / (gapFollow - gapStop));
                target = Mathf.Min(target, car.Cruise * t);
            }

            // Any-angle backstop: the higher-index car yields to any nearby lower-index one. Ordered
            // by index and therefore acyclic, so a ring of cars can never freeze one another for
            // good — which a "yield to whoever is on your right" rule absolutely can.
            bool overlapYield = false;
            if (car.Stuck < car.StuckLimit)
            {
                foreach (var other in _cars)
                {
                    if (other == null || other.Index >= car.Index || other.Mode == TrafficCar.State.Idle) continue;
                    if (!TrafficGeometry.BoxesOverlap(car.Box, other.Box, antiOverlapMargin)) continue;
                    overlapYield = true;
                    break;
                }
            }

            if (overlapYield) target = 0f;

            // Waiting to be stolen. Checked before the light, because it outranks it: the point of
            // the hold is that a light going green mid-approach must not drive the car out from
            // under the player walking towards it.
            bool held = false;
            if (car.Hold > 0f)
            {
                car.Hold -= dt;
                target = 0f;
                held = true;
            }

            if (!car.InCorner && lights != null)
            {
                int nodeId = car.Dir == 1 ? edge.B : edge.A;
                float remaining = car.Dir == 1 ? edge.Length - car.S : car.S;

                if (lights.HasLight(nodeId) && !lights.MayEnter(nodeId, car.EdgeId))
                {
                    // ⚠ WHERE THE STOP LINE ACTUALLY IS. The web build stops a car `stopLineDist`
                    // (8 m) from the junction centre and paints the zebra at 10 m — so the queue's
                    // lead car, whose origin is its own body centre, straddles 5.5–10.5 m and parks
                    // its back half across the crossing. That is not a design decision there: the
                    // setback exists so the crossing's kerb ends clear the light POLES, and the car
                    // was never measured against it. Here the stop point clears the whole zebra plus
                    // the car's own length, which is what a stop line is for. The pole then stands
                    // ahead of the stopped car, at the junction, which is also where a real one is.
                    float stopAt = stopLineDist + crossingClearance + car.HalfLength;

                    // Three zones around it. Before: brake so the target reaches a hard zero
                    // `stopMargin` short, rather than creeping asymptotically over it. Just past it
                    // and slow: hold, because a car that crept over must not treat that as having
                    // committed. Deep inside the junction, or past the line at speed because the
                    // light changed on approach: clear it.
                    float toStop = remaining - stopAt;
                    if (toStop > 0f)
                    {
                        float brake = Mathf.Sqrt(2f * decel * Mathf.Max(toStop - stopMargin, 0f));
                        if (brake < target)
                        {
                            target = brake;
                            held = brake <= 0f;
                        }
                    }
                    else if (toStop > -lightCommitDist && car.Speed < lightCommitSpeed)
                    {
                        target = 0f;
                        held = true;
                    }
                }
            }

            target = GasStop(car, target, dt);

            // Gridlock escape: pinned past its own threshold, a car ignores the follow limit for
            // `stuckNudgeSec` and creeps out. Lights are still obeyed — only the obstacle gate is
            // waived — and waiting at a red is explicitly not "stuck", because the longest red here
            // is ~11.5 s and `stuckAfterSec` is 20. The threshold is jittered per car so a blocked
            // ring unfreezes one car at a time instead of all at once.
            if (car.Speed < 0.5f && (gap < gapFollow || overlapYield) && !held)
            {
                car.Stuck += dt;
                if (car.Stuck > car.StuckLimit + stuckNudgeSec) car.Stuck = 0f;
            }
            else if (car.Speed > 1f)
            {
                car.Stuck = 0f;
            }

            // -- ease the speed, advance along the lane -------------------------------------------
            float rate = target > car.Speed ? accel : decel;
            car.Speed += Mathf.Clamp(target - car.Speed, -rate * dt, rate * dt);

            if (car.InCorner)
            {
                car.CornerS += car.Speed * dt;
                if (car.CornerS >= car.CornerLength)
                {
                    car.EdgeId = car.NextEdgeId;
                    car.Dir = car.NextDir;
                    car.Lane = car.NextLane;
                    car.S = car.NextS;
                    car.InCorner = false;
                }
            }
            else
            {
                car.S += car.Dir * car.Speed * dt;
                int nodeId = car.Dir == 1 ? edge.B : edge.A;
                float remaining = car.Dir == 1 ? edge.Length - car.S : car.S;
                if (remaining <= cornerDist) StartCorner(car, nodeId);
            }

            Vector3 position, forward;
            if (car.InCorner)
            {
                position = TrafficGeometry.CornerAt(car.Corner, car.CornerLength, car.CornerS);
                var ahead = TrafficGeometry.CornerAt(
                    car.Corner, car.CornerLength, Mathf.Min(car.CornerS + 0.5f, car.CornerLength));
                forward = new Vector3(ahead.x - position.x, 0f, ahead.z - position.z);
                // At the very end of the curve the two samples coincide; keep the heading rather
                // than snapping the car to +Z for one step.
                forward = forward.sqrMagnitude > 1e-6f
                    ? forward.normalized
                    : new Vector3(car.Box.Fx, 0f, car.Box.Fz);
            }
            else
            {
                network.SampleLane(
                    car.EdgeId, car.S, car.Dir, network.LaneOffsetFor(car.EdgeId, car.Lane, laneGap),
                    out position, out forward);
            }

            car.Drive(position, forward);
        }

        /// <summary>
        /// Nearest obstacle ahead in this car's lane, as a bumper gap in metres: other cars, the
        /// player, and anyone on a zebra. O(n²) over 32 cars is ~1,000 cone tests a step — the same
        /// arithmetic the web build does over more cars, and no spatial index earns its keep at this
        /// size.
        /// </summary>
        private float NearestGap(TrafficCar car, Vector3 focus)
        {
            float nearest = float.PositiveInfinity;

            foreach (var other in _cars)
            {
                if (other == null || other == car || other.Mode == TrafficCar.State.Idle) continue;
                if (!TrafficGeometry.InFrontCone(
                        car.Box, other.Box.X, other.Box.Z, other.HalfLength, gapLateral, gapFollow))
                    continue;

                // Two cars stopped nose to nose inside a junction are each in the other's cone and
                // would hold each other there forever. When that mutual block is seen, the LOWER
                // index takes right of way and drives through it. A red-light queue is immune by
                // construction: the leader does not have the follower in ITS cone, so the block is
                // never mutual.
                bool mutual =
                    other.Mode == TrafficCar.State.Driving &&
                    other.Speed < 0.5f &&
                    TrafficGeometry.InFrontCone(
                        other.Box, car.Box.X, car.Box.Z, car.HalfLength, gapLateral, gapFollow);

                if (mutual && car.Speed < 0.5f && car.Index < other.Index) car.Push = standoffPushSec;
                if (car.Push > 0f && mutual) continue;

                float gap = TrafficGeometry.BumperGap(car.Box, other.Box.X, other.Box.Z, other.HalfLength);
                if (gap < nearest) nearest = gap;
            }

            // The player is never ignored, on foot or driving. This is the one obstacle with no
            // escape hatch, because the alternative is being run over by scenery.
            if (TrafficGeometry.InFrontCone(car.Box, focus.x, focus.z, playerRadius, gapLateral, gapFollow))
            {
                float gap = TrafficGeometry.BumperGap(car.Box, focus.x, focus.z, playerRadius);
                if (gap < nearest) nearest = gap;
            }

            foreach (var crosser in _crossers)
            {
                if (!TrafficGeometry.InFrontCone(
                        car.Box, crosser.x, crosser.z, pedRadius, gapLateral, gapFollow)) continue;

                float gap = TrafficGeometry.BumperGap(car.Box, crosser.x, crosser.z, pedRadius);
                if (gap < nearest) nearest = gap;
            }

            return nearest;
        }

        /// <summary>
        /// The pumps beside the Paz station: a car driving a straight stretch within range latches a
        /// stop, idles, and then cannot re-trigger for a cooldown so it does not loop on the spot.
        /// The only reason ambient traffic ever stops for something that is not in front of it.
        /// </summary>
        private float GasStop(TrafficCar car, float target, float dt)
        {
            if (car.GasCool > 0f) car.GasCool -= dt;
            if (car.InCorner || gasPoints.Length == 0) return target;

            if (car.Gas > 0f)
            {
                if (car.Speed < 0.5f)
                {
                    car.Gas -= dt;
                    if (car.Gas <= 0f) car.GasCool = gasCooldownSec;
                }

                return 0f;
            }

            if (car.GasCool > 0f) return target;

            float triggerSqr = gasTriggerRadius * gasTriggerRadius;
            foreach (var pump in gasPoints)
            {
                float dx = pump.x - car.Box.X;
                float dz = pump.z - car.Box.Z;
                if (dx * dx + dz * dz > triggerSqr) continue;
                car.Gas = gasStopSeconds;
                return 0f;
            }

            return target;
        }

        /// <summary>
        /// Begins the bezier through a junction: this lane's exit point to the chosen next lane's
        /// entry point. The turn is picked at random, excluding the street the car arrived on unless
        /// the node is a dead end — no instant U-turns.
        /// </summary>
        private void StartCorner(TrafficCar car, int nodeId)
        {
            var node = network.Nodes[nodeId];
            var edge = network.Edges[car.EdgeId];

            int nextId = PickNext(nodeId, car.EdgeId);
            var next = network.Edges[nextId];
            int nextDir = next.A == nodeId ? 1 : -1;
            int nextLane = _rng.Next(Mathf.Max(1, next.Lanes));

            float sIn = car.Dir == 1 ? edge.Length - cornerDist : cornerDist;
            float sOut = nextDir == 1 ? cornerDist : next.Length - cornerDist;

            network.SampleLane(car.EdgeId, sIn, car.Dir, network.LaneOffsetFor(car.EdgeId, car.Lane, laneGap),
                out var inPosition, out var inTangent);
            network.SampleLane(nextId, sOut, nextDir, network.LaneOffsetFor(nextId, nextLane, laneGap),
                out var outPosition, out var outTangent);

            car.CornerLength = TrafficGeometry.BuildCorner(
                inPosition, inTangent, outPosition, outTangent, node.Position, car.Corner);
            car.CornerS = 0f;
            car.InCorner = true;
            car.NextEdgeId = nextId;
            car.NextDir = nextDir;
            car.NextLane = nextLane;
            car.NextS = sOut;
        }

        private int PickNext(int nodeId, int fromEdge)
        {
            var node = network.Nodes[nodeId];
            if (node.EdgeCount <= 1) return fromEdge;

            // Reservoir-free uniform pick over the alternatives: count them, then take the nth.
            int alternatives = 0;
            for (int i = 0; i < node.EdgeCount; i++)
                if (network.EdgeAt(node, i) != fromEdge) alternatives++;

            if (alternatives == 0) return fromEdge;

            int wanted = _rng.Next(alternatives);
            for (int i = 0; i < node.EdgeCount; i++)
            {
                int candidate = network.EdgeAt(node, i);
                if (candidate == fromEdge) continue;
                if (wanted-- == 0) return candidate;
            }

            return fromEdge;
        }
    }
}
