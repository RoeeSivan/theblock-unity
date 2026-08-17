using System.Collections;
using System.Collections.Generic;
using TheBlock.Core;
using TheBlock.UI;
using TheBlock.Vehicles;
using TheBlock.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TheBlock.Missions
{
    /// <summary>
    /// M4, the jetski sea chase - the port of <c>src/mission/jetski-chase.ts</c>, and the finale.
    ///
    /// The same lifecycle as the rescue, with a different failure surface: the buoy GATES are
    /// passed by proximity and <b>never fail you</b> - they mark the route, they do not gate it -
    /// and the win condition is catching the thief on foot after he beaches. Only the clock loses
    /// the run.
    ///
    /// Completing it completes the campaign, so this is the mission whose status edge produces the
    /// win card. Everything about that lives in <see cref="CampaignRunner"/>; all this does is stop
    /// being Active.
    /// </summary>
    public class JetskiChase : MissionBehaviour
    {
        [Header("Scene - found automatically when left empty")]
        [SerializeField] private CampaignRunner runner;
        [SerializeField] private VehicleEnterExit vehicles;
        [SerializeField] private MissionHud hud;
        [SerializeField] private TheBlock.Player.PlayerController player;

        [Header("Content - written by Build Campaign")]
        [SerializeField] private JetskiController jetski;
        [SerializeField] private ChaseThief thief;
        [SerializeField] private GameObject buoyPrefab;

        [Header("Debug")]
        [SerializeField] private bool verbose;

        private TheBlockConfig.ChaseSpec _spec;
        private TheBlockConfig.BeaconSpec _beaconSpec;
        private MissionStatus _status = MissionStatus.Inactive;
        private readonly List<Gate> _gates = new();
        private readonly List<Beacon> _fading = new();
        private Transform _root;
        private float _timeLeft;
        private int _passed;
        private int _total;
        private float _seaLevel;
        private bool _entering;

        private class Gate
        {
            public string Id;
            public Vector3 Position;
            public GameObject Buoy;
            public Beacon Beacon;
            public bool Passed;
        }

        public override string Id => "jetski";
        public override string Title => "Jetski Chase";
        public override MissionStatus Status => _status;

        public override float? TimeLeft =>
            _status == MissionStatus.Active ? _timeLeft : (float?)null;

        public override string ObjectiveLine
        {
            get
            {
                if (_status != MissionStatus.Active) return null;
                return thief != null && thief.Current == ChaseThief.Phase.Running
                    ? "Catch the thief on foot!"
                    : $"Gates {_passed}/{_total}";
            }
        }

        private void Awake()
        {
            if (runner == null) runner = FindAnyObjectByType<CampaignRunner>();
            if (vehicles == null) vehicles = FindAnyObjectByType<VehicleEnterExit>();
            if (hud == null) hud = FindAnyObjectByType<MissionHud>();
            if (player == null) player = FindAnyObjectByType<TheBlock.Player.PlayerController>();
            if (jetski == null) jetski = FindAnyObjectByType<JetskiController>();
            if (thief == null) thief = FindAnyObjectByType<ChaseThief>();
        }

        private void Start()
        {
            var snapshot = TheBlockConfig.Load();
            _spec = snapshot?.Chase;
            _beaconSpec = snapshot?.Mission?.Beacon;
            _seaLevel = snapshot?.Config?.Sea?.Level ?? 0f;

            if (_spec == null)
            {
                Debug.LogError("JetskiChase: no chaseConfig in the snapshot.");
                enabled = false;
                return;
            }

            _total = 0;
            foreach (var point in _spec.Route)
                if (point.Gate)
                    _total++;

            // Locked until the cursor reaches this step, the same gate the chopper has.
            if (jetski != null) jetski.Unlocked = () => runner == null || runner.IsReached(Id);

            // He idles at his spawn from the moment the world loads, cruising his slow circle. The
            // player has to be able to see who they are about to chase.
            if (thief != null) thief.Setup(_spec, _seaLevel, 0.6f);
        }

        // ── entry ─────────────────────────────────────────────────────────────────────────────

        public override void Enter()
        {
            if (_status != MissionStatus.Inactive || _entering) return;
            StartCoroutine(EnterRoutine());
        }

        private IEnumerator EnterRoutine()
        {
            _entering = true;

            // Released in a finally - see the same guard on RescueMission for why an entry latch
            // that survives a throw is unrecoverable.
            try
            {
                if (runner?.Card != null) yield return runner.Card.ShowAndWait(_spec.BriefingLines);
                Begin();
            }
            finally
            {
                _entering = false;
            }
        }

        private void Begin()
        {
            if (_status == MissionStatus.Active) return;

            _root = new GameObject("Chase Gates").transform;
            _passed = 0;
            _timeLeft = _spec.TimeLimitSec;

            var index = 0;
            foreach (var point in _spec.Route)
            {
                if (!point.Gate) continue;
                _gates.Add(SpawnGate(point, index));
                index++;
            }

            // Publish the field BEFORE anything moves. Both skis push out of it every step, and a
            // gate that exists visually but not in the field is a buoy you sail through.
            var positions = new List<Vector3>(_gates.Count);
            foreach (var gate in _gates) positions.Add(gate.Position);
            BuoyField.Set(positions);

            thief?.Begin();
            _status = MissionStatus.Active;

            if (verbose) Debug.Log($"[chase] {_gates.Count} gates, {_timeLeft:0} s, field {BuoyField.Count}");
        }

        private Gate SpawnGate(TheBlockConfig.ChasePointSpec point, int index)
        {
            var at = Convert.Pos(point.Raw);
            at.y = _seaLevel;

            var gate = new Gate { Id = $"Gate {index + 1}", Position = at, Passed = false };

            if (buoyPrefab != null)
            {
                gate.Buoy = Instantiate(buoyPrefab, at, Quaternion.identity, _root);
                gate.Buoy.name = gate.Id;
                gate.Buoy.transform.localScale = Vector3.one * _spec.BuoyScale;
            }

            gate.Beacon = Beacon.Create(
                _root, _beaconSpec, TheBlockConfig.ColorFromHex(_spec.BeaconColor), $"{gate.Id} Beacon");

            // Above the buoy's mast, not at the waterline - the pin has to clear the thing it marks.
            gate.Beacon.SetGround(at + Vector3.up * 4f);

            MapRegistry.AddPoi(new MapPoi
            {
                Name = gate.Id,
                Position = at,
                Kind = MapPoiKind.Marker,
                Minor = true,

                // Marked as somewhere the player is being sent, like the rest. Nothing reads it
                // since the GPS route was cut (2026-08-17); see MapPoi.Guide.
                Guide = true,
            });

            return gate;
        }

        // ── the run ───────────────────────────────────────────────────────────────────────────

        private void Update()
        {
            if (Core.Pause.Frozen) return; // see Core.Pause

            var dt = Time.deltaTime;

            for (var i = _fading.Count - 1; i >= 0; i--)
            {
                var beacon = _fading[i];
                if (beacon == null) { _fading.RemoveAt(i); continue; }
                if (!beacon.PopDone) continue;
                Destroy(beacon.gameObject);
                _fading.RemoveAt(i);
            }

            if (_entering || (runner?.Card != null && runner.Card.IsOpen)) return;

            var riding = vehicles != null && vehicles.Mode == GameMode.Driving &&
                         jetski != null && ReferenceEquals(vehicles.ActiveVehicle, jetski);

            // The chase point: the ski while riding it, otherwise the player. On the beach the thief
            // is chased on foot, so this HAS to follow the player out of the saddle.
            var chaser = riding
                ? jetski.transform.position
                : player != null ? player.transform.position : Vector3.zero;

            // He cruises his idle circle whether or not the mission is running.
            thief?.Tick(dt, chaser);

            if (_status != MissionStatus.Active)
            {
                TickStartPrompt(riding);
                return;
            }

            _timeLeft -= dt;
            if (_timeLeft <= 0f)
            {
                _timeLeft = 0f;
                _status = MissionStatus.Failed;
                thief?.Freeze(); // he "got away" - stopping makes the fail read
                hud?.SetPrompt(null);
                if (verbose) Debug.Log("[chase] out of time");
                return;
            }

            TickGates(dt, chaser);

            // The finale. Proximity, no key press - the same feel the rescue pickup has.
            if (thief != null && thief.Current == ChaseThief.Phase.Running)
            {
                hud?.SetPrompt("Catch the thief!");
                TryCatch(chaser);
            }
        }

        private void TickGates(float dt, Vector3 chaser)
        {
            foreach (var gate in _gates)
            {
                // THE BUOY RIDES THE REAL SURFACE, not a sine around the mean level.
                //
                // The web bobs its buoys by hand because its sea is flat, and this port copied that
                // - against a sea U12 gave three summed swells to, up to 0.37 m of crest. A hull
                // whose base sits at the MEAN is under water for half of every wave, which is what
                // the play-test saw. SeaSurface is the CPU's copy of the shader's own displacement,
                // read off the same material, so the base sits on the water wherever the water is.
                // The swell IS the bob; the extra sine would now fight it.
                if (gate.Buoy != null)
                    gate.Buoy.transform.position = new Vector3(
                        gate.Position.x,
                        SeaSurface.Height(gate.Position.x, gate.Position.z),
                        gate.Position.z);

                if (gate.Passed) continue;

                var dx = chaser.x - gate.Position.x;
                var dz = chaser.z - gate.Position.z;
                if (dx * dx + dz * dz > _spec.GateRadius * _spec.GateRadius) continue;

                gate.Passed = true;
                _passed++;
                MapRegistry.RemovePoi(gate.Id);
                if (gate.Beacon != null)
                {
                    gate.Beacon.Pop();
                    _fading.Add(gate.Beacon);
                }

                if (verbose) Debug.Log($"[chase] gate {_passed}/{_total}");
            }
        }

        /// <summary>F on the ski starts the chase, or retries a timed-out one.</summary>
        private void TickStartPrompt(bool riding)
        {
            if (!riding) return;

            if (_status == MissionStatus.Failed) hud?.SetPrompt("Press F to retry chase");
            else if (_status == MissionStatus.Inactive) hud?.SetPrompt("Press F to start chase");
            else return;

            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.fKey.wasPressedThisFrame) return;

            if (_status == MissionStatus.Failed) Retry();
            else if (runner == null || runner.IsCurrent(Id)) Enter();
        }

        /// <summary>Grabs him. True once, and it completes the campaign.</summary>
        public bool TryCatch(Vector3 from)
        {
            if (_status != MissionStatus.Active) return false;
            if (thief == null || thief.Current != ChaseThief.Phase.Running) return false;

            var target = thief.Position;
            var dx = from.x - target.x;
            var dz = from.z - target.z;
            if (dx * dx + dz * dz > _spec.CatchRadius * _spec.CatchRadius) return false;

            thief.Freeze();
            _status = MissionStatus.Complete;
            hud?.SetPrompt(null);
            if (verbose) Debug.Log("[chase] caught");
            return true;
        }

        // ── exits ─────────────────────────────────────────────────────────────────────────────

        public override void Fail()
        {
            if (_status != MissionStatus.Active) return;
            _status = MissionStatus.Failed;
            thief?.Freeze();
            hud?.SetPrompt(null);
        }

        public override void Retry()
        {
            if (_status != MissionStatus.Failed) return;
            Cleanup();
            _status = MissionStatus.Inactive;
            thief?.ResetRun();
            jetski?.Respawn();
            Begin();
        }

        /// <summary>
        /// Removes the buoys, their colliders and their pins.
        ///
        /// The colliders are why this matters more here than anywhere else: nine invisible cylinders
        /// left floating in the sea is the exact thing the web build's own cleanup comment was
        /// written about.
        /// </summary>
        public override void Cleanup()
        {
            foreach (var gate in _gates)
            {
                if (!gate.Passed) MapRegistry.RemovePoi(gate.Id);
                if (gate.Buoy != null) Destroy(gate.Buoy);
                if (!gate.Passed && gate.Beacon != null) Destroy(gate.Beacon.gameObject);
            }

            _gates.Clear();

            // The field goes with them. A buoy the player still bounces off after the mission ended
            // is the invisible-collider bug in its purest form - which is the very thing the web
            // build's own cleanup comment was written about.
            BuoyField.Clear();

            foreach (var beacon in _fading)
                if (beacon != null)
                    Destroy(beacon.gameObject);
            _fading.Clear();

            if (_root != null) Destroy(_root.gameObject);
            _root = null;
        }

        /// <summary>Editor-side wiring, used by The Block → Build Campaign.</summary>
        public void SetContent(JetskiController ski, ChaseThief fleeing, GameObject buoy)
        {
            jetski = ski;
            thief = fleeing;
            buoyPrefab = buoy;
        }
    }
}
