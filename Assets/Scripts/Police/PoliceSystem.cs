using System.Collections.Generic;
using TheBlock.Core;
using TheBlock.Player;
using TheBlock.Traffic;
using TheBlock.Vehicles;
using UnityEngine;

namespace TheBlock.Police
{
    /// <summary>
    /// The pursuit director: how many cops there are, where they come from, what they know, and when
    /// one of them has caught you.
    ///
    /// It is deliberately shaped like <c>TrafficSystem</c> — a pool created once and reused, a focus
    /// point that is the driven vehicle or the player, a sweep that retires what has fallen out of
    /// range — because that shape has already been measured on this project twice: the cost is the
    /// <c>Instantiate</c> burst, never the population.
    ///
    /// <b>The bays are the pool.</b> Three cruisers sit parked at the station and those same three
    /// are the ones that come after you, which is the web build's own trick and it removes an entire
    /// class of bug: there is no second set of decorative parked cars to hide, and therefore no
    /// hidden car's collider left standing as an invisible wall in the forecourt.
    ///
    /// <b>Spawning out of sight is a rule here, not an accident.</b> The web samples eight headings
    /// behind the player and takes the first with a CLEAR line to them — which is exactly why its
    /// cops pop into existence in plain view at 70 m. This prefers a spot with NO line of sight, so
    /// a cop drives into view instead of appearing in it.
    /// </summary>
    public class PoliceSystem : MonoBehaviour
    {
        [Header("Wiring — found automatically when left empty")]
        [SerializeField] private Heat heat;
        [SerializeField] private RouteGraph routeGraph;
        [SerializeField] private GameObject copPrefab;

        [Tooltip("U19e's officer. Left empty, the cruisers drive themselves and arrest you " +
                 "themselves, exactly as U19 shipped.")]
        [SerializeField] private GameObject officerPrefab;
        [SerializeField] private PlayerController player;
        [SerializeField] private VehicleEnterExit vehicles;
        [SerializeField] private TrafficSystem traffic;
        [SerializeField] private BustSequence bust;
        [SerializeField] private Game.Wallet wallet;

        [Header("Station")]
        [Tooltip("Bay poses, in Unity space, placed by WorldBuilder.Police from config.policeStation.")]
        [SerializeField] private Vector3[] bayPositions = System.Array.Empty<Vector3>();

        [SerializeField] private float bayYaw;

        [Tooltip("Where a busted player is returned. The web's (-160, 0.3, -106), converted.")]
        [SerializeField] private Vector3 custodyPoint = new(160f, 0.3f, -106f);

        [Header("Debug")]
        [Tooltip("Draws each cop's route, aim point and sight line in the Scene view.")]
        [SerializeField] private bool drawGizmos = true;

        private readonly List<CopCar> _cops = new();
        private readonly List<TrafficSystem.Pursuer> _positions = new();
        private PoliceTuning _tuning;
        private float _sinceSpawn;
        private RaycastHit[] _hits;
        private float _laneGap = 3.4f;

        /// <summary>Live cops, for the map blips and the traffic's obstacle list.</summary>
        public IReadOnlyList<CopCar> Cops => _cops;

        /// <summary>Fines you have run up but nothing has charged. U28 owes a wallet.</summary>
        public int FinesOwed { get; private set; }

        public Heat Heat => heat;

        private void Awake() => Bind();

        private void Start()
        {
            Bind();
            FillPool();
        }

        private void Bind()
        {
            if (heat == null) heat = FindAnyObjectByType<Heat>();
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (vehicles == null) vehicles = FindAnyObjectByType<VehicleEnterExit>();
            if (traffic == null) traffic = FindAnyObjectByType<TrafficSystem>();
            if (bust == null) bust = FindAnyObjectByType<BustSequence>();
            if (wallet == null) wallet = FindAnyObjectByType<Game.Wallet>();

            _tuning = heat != null ? heat.Tuning : new PoliceTuning();
            _hits ??= new RaycastHit[8];

            var snapshot = TheBlockConfig.Load();
            if (snapshot?.Config?.Traffic != null) _laneGap = snapshot.Config.Traffic.LaneGap;
        }

        /// <summary>
        /// Instantiates the whole pool at Start, parked and inactive-but-visible in the bays.
        ///
        /// All of them up front, deliberately: a WheelCollider car entering the physics scene is the
        /// expensive part, and doing it mid-chase is a hitch at exactly the moment nobody will
        /// forgive one.
        /// </summary>
        private void FillPool()
        {
            if (copPrefab == null || routeGraph == null) return;

            for (int i = 0; i < _tuning.MaxCops; i++)
            {
                var instance = Instantiate(copPrefab, transform);
                instance.name = $"Cop {i}";

                var cop = instance.GetComponent<CopCar>();
                if (cop == null) cop = instance.AddComponent<CopCar>();
                cop.Configure(_tuning, routeGraph, _laneGap);

                // U27's siren rides on the car itself, so it is a sound in the WORLD with real
                // distance and direction — the thing the web could not have, because that build has
                // no AudioListener and every sound in it plays at a constant gain. Added here rather
                // than on the prefab because the pool is built at runtime and there is no prefab step
                // to fill a field in. TryGetComponent, never `??` — see the memory file.
                if (!instance.TryGetComponent<TheBlock.Audio.Siren>(out _))
                    instance.AddComponent<TheBlock.Audio.Siren>();

                Seat(cop);

                cop.Bay = i < bayPositions.Length ? i : -1;
                cop.State = CopCar.Mode.Idle;
                cop.Car.Driven = false;

                Park(cop);
                _cops.Add(cop);
            }
        }

        /// <summary>
        /// Puts an officer in one cruiser's driver seat, if there is a prefab and a seat to put her
        /// in.
        ///
        /// <b>Both halves are optional and the failure is loud but not fatal.</b> No prefab means
        /// U19's cruisers, which arrest by themselves; a prefab with no <c>DriverAnchor</c> on the
        /// car means the seat was never built, which is a rebuild of the car away — and saying so
        /// once at Start beats three invisible officers standing at the world origin.
        /// </summary>
        private void Seat(CopCar cop)
        {
            if (officerPrefab == null) return;

            var seat = cop.Car != null ? cop.Car.DriverAnchor : null;
            if (seat == null)
            {
                Debug.LogWarning(
                    $"PoliceSystem: {cop.name} has no DriverAnchor — run The Block → Build Police Car. " +
                    "No officer will get out of it.", cop);
                return;
            }

            var officer = Instantiate(officerPrefab, seat).GetComponent<CopOfficer>();
            if (officer == null) return;

            officer.name = $"{cop.name} Officer";
            officer.BindSeat(seat);
            cop.Officer = officer;
        }

        private void Update()
        {
            if (_tuning == null) Bind();
            if (heat == null || routeGraph == null || _cops.Count == 0) return;

            var focus = Focus();
            float dt = Time.deltaTime;
            _sinceSpawn += dt;

            // Indoors the whole thing stands down: the heat is frozen by CrimeWatch and the cars go
            // back to their bays rather than circling a building the player is not behind.
            int wanted = heat.Frozen ? 0 : Mathf.Clamp(heat.Stars, 0, _tuning.MaxCops);

            Reconcile(wanted, focus);
            _positions.Clear();

            foreach (var cop in _cops)
            {
                // The siren, every frame, from the same State the driver reads — so it cannot say
                // one thing while the car does another. A parked, returning or wrecked cruiser is
                // silent; only a car actually coming for you sounds. Which of them are AUDIBLE is
                // Siren.Arbitrate's call, not this one's: "the three nearest" is not a fact any
                // single car knows.
                if (cop.TryGetComponent<TheBlock.Audio.Siren>(out var siren))
                {
                    siren.Wanted = cop.State == CopCar.Mode.Chasing ||
                                   cop.State == CopCar.Mode.Arresting;
                }

                if (cop.State == CopCar.Mode.Idle) continue;

                if (cop.State == CopCar.Mode.Wrecked)
                {
                    // A wrecked cop goes home the short way — it is on its roof or in the sea, so
                    // there is nothing to drive back.
                    if (Time.time - cop.WreckedAt > _tuning.ReplaceDelay) SendHome(cop, true);
                    continue;
                }

                // Handed to the traffic every step so ambient cars can pull over rather than stand
                // in the way — a traffic car is kinematic, so to a cop it is a wall, not a nudge.
                // A cop driving itself home is in the list too: it is still a car in the street and
                // still worth clearing, even though nobody is being chased.
                _positions.Add(new TrafficSystem.Pursuer(
                    cop.transform.position, cop.transform.forward, cop.Car.ForwardSpeed));

                if (cop.State == CopCar.Mode.Returning) StepReturn(cop, dt);
                else Step(cop, focus, dt);
            }

            if (traffic != null) traffic.SetPursuitObstacles(_positions);
        }

        /// <summary>The driven vehicle if there is one, else the player — as the crowd and map do.</summary>
        private Transform Focus()
        {
            if (vehicles != null && vehicles.Mode == GameMode.Driving && vehicles.ActiveVehicle != null)
                return vehicles.ActiveVehicle.GetTransform();
            return player != null ? player.transform : transform;
        }

        // --- population ---------------------------------------------------------------------------

        /// <summary>
        /// Brings the number of live cops to the number of stars, one per second.
        ///
        /// <b>A cop driving home is not live and is not idle either.</b> It does not count toward the
        /// star, so a fresh crime deploys again immediately — and the car it deploys is preferably
        /// that same one, already out on the road, rather than a cold start from the bays.
        /// </summary>
        private void Reconcile(int wanted, Transform focus)
        {
            int live = 0;
            foreach (var cop in _cops)
                if (cop.State == CopCar.Mode.Chasing || cop.State == CopCar.Mode.Arresting) live++;

            // Losing a star sends home the cop FURTHEST from you, never the last one in the list.
            // The list order is the bay order, which has nothing to do with the chase, and retiring
            // by it regularly stood down the one car that was actually on your bumper.
            while (live > wanted)
            {
                var furthest = Furthest(focus);
                if (furthest == null) break;

                SendHome(furthest, false);
                live--;
            }

            if (live >= wanted || _sinceSpawn < _tuning.SpawnInterval) return;

            // Returning first: it is already on a street and pointed at the city.
            if (TryDeployFrom(CopCar.Mode.Returning, focus)) return;
            TryDeployFrom(CopCar.Mode.Idle, focus);
        }

        private bool TryDeployFrom(CopCar.Mode from, Transform focus)
        {
            foreach (var cop in _cops)
            {
                if (cop.State != from) continue;
                if (!Deploy(cop, focus)) continue;

                _sinceSpawn = 0f;
                return true;
            }

            return false;
        }

        /// <summary>The chasing cop furthest from the focus, which is the one a lost star costs.</summary>
        private CopCar Furthest(Transform focus)
        {
            CopCar worst = null;
            float best = -1f;

            foreach (var cop in _cops)
            {
                if (cop.State != CopCar.Mode.Chasing && cop.State != CopCar.Mode.Arresting) continue;

                float distance = (cop.transform.position - focus.position).sqrMagnitude;
                if (distance <= best) continue;

                best = distance;
                worst = cop;
            }

            return worst;
        }

        /// <summary>
        /// Sends one cop after the player: out of its bay, off the road it was driving home on, or —
        /// only for a car with no bay of its own — placed on a street you cannot see.
        /// </summary>
        private bool Deploy(CopCar cop, Transform focus)
        {
            var at = focus.position;

            // ALWAYS out of a bay when this car has one, however far away the crime was.
            //
            // The web deploys from the station only within 120 m and otherwise places a cop on a
            // street near the player, because its cops could not reliably drive anywhere. Ours can:
            // 97.9% of the city is one connected component and the station is inside it. So the
            // cars sit at the station until a crime, then drive — which is what a police car does,
            // and it also means the response has a TRAVEL TIME. Getting away before they arrive is
            // a real thing you can do, and none of it needed a new mechanism.
            bool fromBay = cop.Bay >= 0 && cop.Bay < bayPositions.Length;

            if (!fromBay)
            {
                if (!TryFieldSpawn(at, out var position, out var rotation)) return false;

                cop.gameObject.SetActive(true);
                cop.transform.SetPositionAndRotation(position, rotation);
                Physics.SyncTransforms();
                Stop(cop);
            }

            cop.State = CopCar.Mode.Chasing;
            cop.SpawnedAt = Time.time;
            cop.ArrestHold = 0f;
            cop.LastKnown = at;
            cop.ReplanIn = 0f;
            cop.Driver.Halt = false;
            cop.Driver.Target = at;
            cop.Driver.ClearRoute();
            cop.Driver.ResetWedges();
            cop.OnGraphSamples = cop.TotalSamples = 0;
            cop.WorstOffRoute = 0f;

            // The blip follows the car rather than being rewritten every frame — see MapPoi.Follow.
            UI.MapRegistry.AddPoi(new UI.MapPoi
            {
                Name = cop.name,
                Kind = UI.MapPoiKind.Cop,
                Follow = cop.transform,
                Minor = true,
            });

            return true;
        }

        /// <summary>
        /// Picks a street point in the ring behind the action that the player cannot see.
        ///
        /// Blocked line of sight is what is WANTED here — it is the difference between a cop arriving
        /// and a cop appearing. The component test matters just as much: a spawn on an unreachable
        /// street is a cop that will drive at a kerb forever, and 265 m of this city is exactly that.
        /// </summary>
        private bool TryFieldSpawn(Vector3 at, out Vector3 position, out Quaternion rotation)
        {
            position = at;
            rotation = Quaternion.identity;

            int component = ReferenceComponent(at);
            var camera = Camera.main;
            bool best = false;
            float bestScore = float.MaxValue;

            for (int i = 0; i < _tuning.FieldSpawnTries; i++)
            {
                float angle = Random.value * Mathf.PI * 2f;
                float range = Random.Range(_tuning.FieldSpawnMin, _tuning.FieldSpawnMax);
                var probe = at + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * range;

                if (!routeGraph.TryNearest(probe, 25f, component, out var hit)) continue;

                float distance = Vector3.Distance(hit.Position, at);
                if (distance < _tuning.FieldSpawnMin * 0.5f) continue;
                if (TooCloseToTraffic(hit.Position)) continue;
                if (TooCloseToCops(hit.Position)) continue;

                // Out of sight is preferred, not required — in an open plaza there may be no such
                // point, and a late cop is worse than a visible one.
                bool visible = HasClearLine(hit.Position + Vector3.up * _tuning.LosEyeHeight,
                    at + Vector3.up * _tuning.LosTargetHeight, null);

                bool inView = camera != null &&
                              Vector3.Dot((hit.Position - at).normalized, camera.transform.forward) > 0.2f;

                float score = distance + (visible ? 500f : 0f) + (inView ? 250f : 0f);
                if (score >= bestScore) continue;

                var link = routeGraph.Links[hit.Link];
                routeGraph.SampleLink(link, hit.S, _laneGap, out var lane, out var tangent);

                bestScore = score;
                best = true;
                position = lane + Vector3.up * 0.2f;
                rotation = Quaternion.LookRotation(tangent, Vector3.up);
            }

            return best;
        }

        /// <summary>
        /// Which part of the street network a point belongs to, for deciding where a cop may spawn.
        ///
        /// <b>Being off-street is not an error.</b> The player spawns in a car park 80 m from the
        /// nearest road, and requiring THEM to be on the graph is what stopped the first cop of every
        /// session from ever deploying. So the lookup is generous, and when even that finds nothing —
        /// deep inside a plaza, or on the beach — it falls back to the city, which is component 0 by
        /// construction: <c>WorldBuilder.Police</c> renumbers components largest-first.
        /// </summary>
        private int ReferenceComponent(Vector3 at) =>
            routeGraph.TryNearest(at, RoutePlanner.SnapRadius * 2f, -1, out var hit) ? hit.Component : 0;

        /// <summary>
        /// Keeps cops apart at spawn.
        ///
        /// Three pursuers run the same solo pursuit — the web build's do too — so two placed near
        /// each other take the same route to the same person and spend the chase shoving one another
        /// instead of driving. Measured: spawned 5 m apart, both hit the unwedge limit within
        /// seconds and retired themselves.
        /// </summary>
        private bool TooCloseToCops(Vector3 point)
        {
            float separation = _tuning.CopSeparation * _tuning.CopSeparation;

            foreach (var cop in _cops)
            {
                if (cop == null || cop.State == CopCar.Mode.Idle) continue;
                if ((cop.transform.position - point).sqrMagnitude < separation) return true;
            }

            return false;
        }

        private bool TooCloseToTraffic(Vector3 point)
        {
            if (traffic == null) return false;
            float clearance = _tuning.FieldSpawnClearance * _tuning.FieldSpawnClearance;

            foreach (var car in traffic.Cars)
            {
                if (car == null || !car.gameObject.activeSelf) continue;
                if ((car.transform.position - point).sqrMagnitude < clearance) return true;
            }

            return false;
        }

        /// <summary>
        /// Stands a cop down.
        ///
        /// <b>It DRIVES home.</b> The blip goes now, because the blip belongs to the star, but the
        /// car is still a car and it routes back to its own bay on the planner it was chasing you
        /// with. Teleporting instead is what the first version did, and a cruiser vanishing twenty
        /// metres off your bumper the moment a star bleeds reads as a bug rather than as the police
        /// giving up on you.
        ///
        /// <paramref name="immediate"/> is the two cases where there is nothing to drive: a car on
        /// its roof or in the sea, and a car with no bay to drive to.
        /// </summary>
        private void SendHome(CopCar cop, bool immediate)
        {
            cop.ArrestHold = 0f;
            UI.MapRegistry.RemovePoi(cop.name);

            // Straight back into the seat, never a walk. This is the bust, a lost star or a wrecked
            // cruiser: the street has to be clear NOW, and a cruiser about to be parked in its bay
            // 900 m away cannot wait for its driver to jog there.
            if (cop.Officer != null) cop.Officer.SeatNow();

            bool hasBay = cop.Bay >= 0 && cop.Bay < bayPositions.Length;
            if (immediate || !hasBay)
            {
                cop.State = CopCar.Mode.Idle;
                cop.Driver.Halt = true;
                cop.Driver.ClearRoute();
                Park(cop);
                return;
            }

            cop.State = CopCar.Mode.Returning;
            cop.Driver.Halt = false;
            cop.Driver.HasLineOfSight = false;
            cop.Driver.Target = bayPositions[cop.Bay];
            cop.Driver.ClearRoute();
            cop.Driver.ResetWedges();
            cop.ReplanIn = 0f;
        }

        /// <summary>
        /// One step of a cop driving itself home. No line of sight, no arrest, no contact report —
        /// it is not chasing anybody, and reporting contact from here would hold the player's heat up
        /// with a car that is leaving.
        /// </summary>
        private void StepReturn(CopCar cop, float dt)
        {
            if (cop.LooksWrecked(-5f))
            {
                cop.State = CopCar.Mode.Wrecked;
                cop.WreckedAt = Time.time;
                cop.Driver.Halt = true;

                // A car in the sea takes its driver with it. Leaving her running about on the
                // surface after her cruiser has gone under is not a mechanic anyone asked for.
                if (cop.Officer != null) cop.Officer.SeatNow();
                return;
            }

            var bay = bayPositions[cop.Bay];

            // Close enough: park it properly, which also snaps it square in the slot rather than
            // leaving it at whatever angle it rolled up at.
            if ((cop.transform.position - bay).sqrMagnitude <= _tuning.BayArrive * _tuning.BayArrive)
            {
                cop.State = CopCar.Mode.Idle;
                cop.Driver.Halt = true;
                cop.Driver.ClearRoute();
                Park(cop);
                return;
            }

            // A cop that cannot get home is not worth a rescue mechanism — it is already off duty.
            if (cop.Driver.Unwedges >= _tuning.UnwedgeLimit)
            {
                cop.State = CopCar.Mode.Idle;
                cop.Driver.Halt = true;
                cop.Driver.ClearRoute();
                Park(cop);
                return;
            }

            cop.Driver.Target = bay;
            Replan(cop, dt);
        }

        /// <summary>
        /// Puts a cruiser back in its bay with the handbrake on.
        ///
        /// <b>The hold is not decoration.</b> A parked car still runs its driver every physics step,
        /// and a driver with no route aims at whatever its target says — which at startup is
        /// <c>Vector3.zero</c>, the middle of the map. Without this the three cruisers quietly drove
        /// out of the station before the player had committed any crime at all.
        /// </summary>
        private void Park(CopCar cop)
        {
            cop.Driver.Halt = true;
            cop.Driver.ClearRoute();
            cop.Driver.Target = cop.transform.position;

            if (cop.Bay < 0 || cop.Bay >= bayPositions.Length)
            {
                cop.gameObject.SetActive(false);
                return;
            }

            cop.gameObject.SetActive(true);
            cop.transform.SetPositionAndRotation(
                bayPositions[cop.Bay], Quaternion.Euler(0f, bayYaw, 0f));
            Physics.SyncTransforms();
            Stop(cop);
        }

        /// <summary>Zero the body before and after a teleport, in the order CarController documents.</summary>
        private static void Stop(CopCar cop)
        {
            var body = cop.Car.Body;
            if (body == null) return;

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        // --- one cop, one step --------------------------------------------------------------------

        /// <summary>
        /// One step of one chasing cop, and the two facts it owes <see cref="Heat"/>.
        ///
        /// <b>Those two facts are different and the difference is the whole fix.</b> ARRIVING is
        /// being inside <see cref="PoliceTuning.SightRadius"/> at all, which latches the meter and
        /// lets it start bleeding; CONTACT is being inside it with a clear line, which holds the
        /// meter still. Only the second existed before, so nothing could ever latch — which meant
        /// heat bled from the moment of the crime while the cars were still hundreds of metres away
        /// driving in, and the stars were gone before anyone arrived.
        /// </summary>
        private void Step(CopCar cop, Transform focus, float dt)
        {
            var target = focus.position;

            // Only a car that is actually on its roof or in the sea is out of the chase.
            if (cop.LooksWrecked(-5f))
            {
                cop.State = CopCar.Mode.Wrecked;
                cop.WreckedAt = Time.time;
                cop.Driver.Halt = true;

                // A car in the sea takes its driver with it. Leaving her running about on the
                // surface after her cruiser has gone under is not a mechanic anyone asked for.
                if (cop.Officer != null) cop.Officer.SeatNow();
                return;
            }

            // Repeatedly wedged is not the same as wrecked, and treating it as such is how a cop
            // that met a fence around a car park retired itself, was replaced, and the replacement
            // met the same fence. Back off, throw the route away and plan a fresh one instead —
            // which reads as a cop looking for another way in, because that is what it is.
            if (cop.Driver.Unwedges >= _tuning.UnwedgeLimit)
            {
                cop.Driver.ResetWedges();
                cop.Driver.ClearRoute();
                cop.ReplanIn = 0f;
            }

            float distance = Vector3.Distance(cop.transform.position, target);

            // No distance retire. It was the traffic pool's discipline, and it is wrong here now
            // that every cop starts at the station: a chase on the far side of the map begins with
            // a car that is 900 m away and closing, and retiring it for being far is retiring it for
            // doing its job. Cops go home when the stars do.

            // One ray per cop per step. An occluder is anything with no Rigidbody attached, which
            // excludes every cop, every traffic car, every wreck and the player's own car in a single
            // test and needs no new layer.
            bool inRange = distance <= _tuning.SightRadius;
            cop.HasLos = inRange && HasClearLine(
                cop.transform.position + Vector3.up * _tuning.LosEyeHeight,
                target + Vector3.up * _tuning.LosTargetHeight,
                cop.transform);

            if (inRange) heat.ReportInSight();
            if (cop.HasLos)
            {
                cop.LastKnown = target;
                heat.ReportContact();
            }

            cop.Driver.Target = cop.HasLos ? target : cop.LastKnown;
            cop.Driver.HasLineOfSight = cop.HasLos;

            Replan(cop, dt);
            Arrest(cop, target, distance, dt);
            Sample(cop);
        }

        /// <summary>
        /// Replans on a timer, or immediately when the route has run out or been left behind.
        /// </summary>
        private void Replan(CopCar cop, float dt)
        {
            cop.ReplanIn -= dt;

            bool exhausted = cop.Driver.Cursor >= cop.Driver.Route.Count;
            bool strayed = cop.Driver.OffRoute > _tuning.CorridorWidth;
            if (cop.ReplanIn > 0f && !exhausted && !strayed) return;

            cop.ReplanIn = _tuning.ReplanInterval;

            var heading = cop.transform.forward;
            var plan = cop.Planner.Plan(cop.transform.position, heading, cop.Driver.Target, cop.Driver.Route);
            cop.LastPlan = plan;
            cop.Driver.RouteRefreshed();

            // No route at all: off the graph, or the target is somewhere no street reaches. The
            // driver then steers straight, which is the ONLY other place straight-line survives.
            if (!plan.Found) cop.Driver.ClearRoute();
        }

        /// <summary>
        /// The capture test: close, slow, stopped, and held.
        ///
        /// The speed condition is a port addition and it earns its place — the web's cop halted to
        /// arrest as a side effect of being kinematic, while a physics cop can genuinely sit on your
        /// bumper at 70 km/h, and being arrested at that speed reads as a bug rather than as an
        /// arrest.
        /// </summary>
        private void Arrest(CopCar cop, Vector3 target, float distance, float dt)
        {
            bool close = distance <= _tuning.ArrestRadius;
            bool slow = Mathf.Abs(cop.Car.ForwardSpeed) <= _tuning.ArrestMaxSpeed && TargetSpeed() <= _tuning.ArrestMaxSpeed;
            bool grace = Time.time - cop.SpawnedAt < _tuning.SpawnGrace;

            // ON FOOT, the officer is the arrest. IN A VEHICLE, the car is — she would never catch
            // you, and a cruiser pulling up to a stopped getaway car is the arrest the web build has.
            bool onFoot = vehicles == null || vehicles.Mode != GameMode.Driving;
            if (_tuning.OfficerChase && cop.Officer != null && onFoot)
            {
                FootArrest(cop, target, distance, dt, grace);
                return;
            }

            // Back in a car while she was out of hers: she walks back, and the cruiser takes the
            // arrest over from exactly where U19 left it — but not until she is IN it. A cruiser
            // that drives off leaving its own driver on the pavement is the version of this bug
            // that looks fine right up until you watch the car.
            if (cop.Officer != null && cop.Officer.Deployed)
            {
                cop.Officer.Recall();
                if (Step(cop, target, dt)) return;
            }

            if (close && slow && !grace)
            {
                cop.State = CopCar.Mode.Arresting;
                cop.Driver.Halt = true;
                cop.ArrestHold += dt;

                // Face the person being arrested while it holds them.
                var look = target - cop.transform.position;
                look.y = 0f;
                if (look.sqrMagnitude > 0.5f)
                    cop.transform.rotation = Quaternion.RotateTowards(
                        cop.transform.rotation, Quaternion.LookRotation(look, Vector3.up), 90f * dt);

                if (cop.ArrestHold >= _tuning.ArrestHold) Bust();
                return;
            }

            if (cop.State == CopCar.Mode.Arresting)
            {
                cop.State = CopCar.Mode.Chasing;
                cop.Driver.Halt = false;
            }

            // Bleeds at twice the rate: stepping out of range for a moment should cost more than it
            // gained, or a car rocking on the arrest boundary is never quite caught.
            cop.ArrestHold = Mathf.Max(0f, cop.ArrestHold - dt * 2f);
        }

        /// <summary>
        /// The arrest a person makes: the cruiser stops short, the officer gets out, and the last
        /// stretch is a foot race.
        ///
        /// <b>Why the car stops at all.</b> Two metres of pavement is not a chase, so the cruiser
        /// gives up its own arrest radius and halts at <see cref="PoliceTuning.OfficerDeployRadius"/>
        /// — far enough that the run is the interesting part, close enough that she is a threat
        /// rather than a jogger in the distance. It waits there with its lights on until she is
        /// back in, which is also what makes giving up legible: the cruiser is still parked where
        /// you last saw it.
        ///
        /// <b>Why she can lose you.</b> She runs at 6.2 m/s against a 7 m/s sprint, so a player
        /// with stamina left outruns her and a player without does not — and at 60 m she stops,
        /// walks back and the CAR takes over again. The pursuit does not end; only her run does.
        /// </summary>
        private void FootArrest(CopCar cop, Vector3 target, float distance, float dt, bool grace)
        {
            var officer = cop.Officer;

            if (!officer.Deployed)
            {
                bool close = distance <= _tuning.OfficerDeployRadius;
                bool slow = Mathf.Abs(cop.Car.ForwardSpeed) <= _tuning.ArrestMaxSpeed;

                if (!close || !slow || grace)
                {
                    if (cop.State != CopCar.Mode.Arresting) return;
                    cop.State = CopCar.Mode.Chasing;
                    cop.Driver.Halt = false;
                    return;
                }

                cop.State = CopCar.Mode.Arresting;
                cop.Driver.Halt = true;
                cop.ArrestHold = 0f;
                officer.Deploy();
                return;
            }

            // She is out, so the car is parked for the duration — including while she walks back.
            cop.State = CopCar.Mode.Arresting;
            cop.Driver.Halt = true;

            float reach = officer.DistanceTo(target);
            if (reach > _tuning.OfficerGiveUp) officer.Recall();

            // Step her FIRST, then decide. The order matters and the inverted version of this cost
            // a play-test: an early return keyed on "is she still out" put the grab test on the one
            // path that never runs during a chase, so she reached the player, stood there, and
            // nothing happened.
            if (!Step(cop, target, dt)) return;
            if (officer.State != CopOfficer.Mode.Chasing) return;

            // Same bleed as the car's own hold, and for the same reason: stepping out of arm's
            // reach for a moment has to cost more than it gained.
            if (reach <= _tuning.OfficerGrabRadius) cop.ArrestHold += dt;
            else cop.ArrestHold = Mathf.Max(0f, cop.ArrestHold - dt * 2f);

            if (cop.ArrestHold >= _tuning.OfficerGrabHold) Bust();
        }

        /// <summary>
        /// Steps a deployed officer, and hands the car back to its driver the moment she is in it.
        /// </summary>
        /// <returns>True while she is still out, which is the only state that has an arrest in it.</returns>
        private bool Step(CopCar cop, Vector3 target, float dt)
        {
            var officer = cop.Officer;
            officer.Step(target, dt);
            if (officer.Deployed) return true;

            cop.State = CopCar.Mode.Chasing;
            cop.Driver.Halt = false;
            cop.ArrestHold = 0f;
            return false;
        }

        private float TargetSpeed()
        {
            if (vehicles != null && vehicles.Mode == GameMode.Driving && vehicles.ActiveVehicle != null)
                return Mathf.Abs(vehicles.ActiveVehicle.ForwardSpeed);
            return player != null ? player.PlanarSpeed : 0f;
        }

        /// <summary>
        /// Caught. The fine is CHARGED now, and whatever the wallet could not cover stays owed.
        ///
        /// The web build has no such split — its wallet floors at zero and the shortfall simply
        /// evaporates — but this port already had a <see cref="FinesOwed"/> tally waiting for a
        /// wallet that did not exist, and a debt is the honest thing for it to become. It also means
        /// being broke is not a free pass: the third bust with $0 still costs you the street you
        /// were on and puts another $100 on the slate.
        /// </summary>
        private void Bust()
        {
            int taken = wallet != null ? wallet.Charge(_tuning.BustFine) : 0;
            FinesOwed += _tuning.BustFine - taken;

            // Everyone home the short way. A busted street should be clear at once, and on foot the
            // player stays where they are — so a cruiser driving off would be the last thing they
            // watch instead of the arrest.
            foreach (var cop in _cops)
                if (cop.State != CopCar.Mode.Idle) SendHome(cop, true);

            heat.Clear();
            if (bust != null) bust.Begin(custodyPoint, _tuning.BustHold, taken, FinesOwed);
        }

        // --- measurement ---------------------------------------------------------------------------

        /// <summary>
        /// Samples whether this cop is actually on a street, which is the road-route proof: the point
        /// of routing at all is that a pursuit looks like driving rather than like a diagonal across
        /// a plaza, and that is a number, not a screenshot.
        /// </summary>
        private void Sample(CopCar cop)
        {
            cop.TotalSamples++;
            if (!routeGraph.TryNearest(cop.transform.position, 40f, -1, out var hit)) return;

            float halfLane = _laneGap * 1.5f;
            if (hit.Distance <= halfLane) cop.OnGraphSamples++;
            if (cop.Driver.OffRoute > cop.WorstOffRoute) cop.WorstOffRoute = cop.Driver.OffRoute;
        }

        // --- helpers --------------------------------------------------------------------------------

        /// <summary>
        /// Line of sight between two world points, ignoring anything with a Rigidbody.
        ///
        /// The web has to filter cop colliders by hand and notes that its traffic and crowd are
        /// instanced visuals with no colliders at all, so only buildings occlude. Here they are real
        /// bodies, and "an occluder is a collider with no attached Rigidbody" gets the same answer
        /// from the physics itself: buildings, walls, the station and the ground block; cars, cops,
        /// wrecks and people do not.
        /// </summary>
        private bool HasClearLine(Vector3 from, Vector3 to, Transform ignore)
        {
            var delta = to - from;
            float length = delta.magnitude;
            if (length < 0.01f) return true;

            int count = Physics.RaycastNonAlloc(
                from, delta / length, _hits, length, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (hit.collider == null || hit.collider.attachedRigidbody != null) continue;
                if (ignore != null && hit.collider.transform.IsChildOf(ignore)) continue;
                if (hit.distance < length * 0.9f) return false;
            }

            return true;
        }

        /// <summary>Bay poses, set by <c>WorldBuilder.Police</c> so the conversion happens once.</summary>
        public void ConfigureStation(Vector3[] bays, float yawDegrees, Vector3 custody)
        {
            bayPositions = bays;
            bayYaw = yawDegrees;
            custodyPoint = custody;
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmos || _cops.Count == 0) return;

            foreach (var cop in _cops)
            {
                if (cop == null || cop.State == CopCar.Mode.Idle) continue;

                Gizmos.color = cop.HasLos ? Color.red : new Color(1f, 0.6f, 0f);
                Gizmos.DrawLine(cop.transform.position + Vector3.up, cop.Driver.Target + Vector3.up);

                Gizmos.color = Color.cyan;
                for (int i = cop.Driver.Cursor; i < cop.Route.Count - 1; i++)
                    Gizmos.DrawLine(cop.Route[i] + Vector3.up * 0.3f, cop.Route[i + 1] + Vector3.up * 0.3f);

                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(cop.Driver.Aim + Vector3.up * 0.3f, 0.6f);
            }
        }
    }
}
