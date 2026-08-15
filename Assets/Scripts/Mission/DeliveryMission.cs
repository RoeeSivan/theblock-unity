using System.Collections;
using System.Collections.Generic;
using TheBlock.Audio;
using TheBlock.Core;
using TheBlock.Npc;
using TheBlock.UI;
using TheBlock.Vehicles;
using TheBlock.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TheBlock.Missions
{
    /// <summary>
    /// M1, the pizza delivery run — the port of <c>src/mission/delivery-mission.ts</c>.
    ///
    /// Five customers stand on five of the fifteen authored pavement spots, each shown twice: an
    /// overhead beacon in the world and a minor pin on the map. Ride to one, press F, and it pops.
    /// Deliver all five inside four minutes or the shift is a loss.
    ///
    /// <b>Can Unity do this better?</b> The web loads five more character FBX at boot for these
    /// standers, on top of the crowd's own copies of the same five people. Here a target IS a crowd
    /// prefab — <c>Ped_Sophie</c>, <c>Ped_Remy</c> and the rest, already imported, already
    /// URP-bound, already height-normalised — instantiated and simply never bound to a seed, so
    /// <c>Pedestrian.Tick</c> never runs and it stands there idling. No second character pipeline,
    /// and no extra megabyte.
    ///
    /// That also settles a question the web has to answer with a separate list: a target is NOT in
    /// <c>CrowdSpawner.Crowd</c>, which is the only thing <see cref="RunOverSystem"/> looks at, so a
    /// customer cannot be run over and the mission cannot be made unwinnable by bad parking.
    /// </summary>
    public class DeliveryMission : MissionBehaviour
    {
        [Header("Scene — found automatically when left empty")]
        [SerializeField] private CampaignRunner runner;
        [SerializeField] private Interior interior;
        [SerializeField] private Transform player;
        [SerializeField] private VehicleEnterExit vehicles;
        [SerializeField] private MissionHud hud;
        [SerializeField] private Voice voice;

        [Header("Targets")]
        [Tooltip("One per face in missionConfig.npcSpecs, in that order. Written by Build Campaign.")]
        [SerializeField] private List<GameObject> facePrefabs = new();

        [Tooltip("Layers the drop-off ground ray may hit. Districts and roads; never the player.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Debug")]
        [SerializeField] private bool verbose;

        private TheBlockConfig.MissionSpec _spec;
        private MissionStatus _status = MissionStatus.Inactive;
        private readonly List<Target> _targets = new();
        private readonly List<Beacon> _fading = new();
        private Transform _root;
        private int _delivered;
        private int _chosen;
        private float _timeLeft;
        private bool _entering;

        private class Target
        {
            public string Id;
            public Vector3 Position;
            public GameObject Body;
            public Beacon Beacon;
            public string ThankUrl;
            public bool Delivered;
        }

        public override string Id => "pizza";
        public override string Title => "The Block Pizza Run";
        public override MissionStatus Status => _status;

        public override float? TimeLeft =>
            _status == MissionStatus.Active ? _timeLeft : (float?)null;

        public override string ObjectiveLine =>
            _status == MissionStatus.Active ? $"Deliveries {_delivered}/{_chosen}" : null;

        /// <summary>Pizzas still on the bike. The HUD's own readout, separate from the objective.</summary>
        public int Carried => Mathf.Max(0, (_spec?.PizzasGiven ?? 5) - _delivered);

        /// <summary>True while the shift can be started at the counter — the cashier's prompt gate.</summary>
        public bool CanStart => _status == MissionStatus.Inactive;

        private void Awake()
        {
            if (runner == null) runner = FindAnyObjectByType<CampaignRunner>();
            if (interior == null) interior = FindAnyObjectByType<Interior>();
            if (vehicles == null) vehicles = FindAnyObjectByType<VehicleEnterExit>();
            if (hud == null) hud = FindAnyObjectByType<MissionHud>();
            if (voice == null) voice = FindAnyObjectByType<Voice>();
            if (player == null)
            {
                var pc = FindAnyObjectByType<TheBlock.Player.PlayerController>();
                if (pc != null) player = pc.transform;
            }
        }

        private void Start()
        {
            _spec = TheBlockConfig.Load()?.Mission;
            if (_spec == null)
            {
                Debug.LogError("DeliveryMission: no missionConfig in the snapshot.");
                enabled = false;
            }
        }

        // ── entry ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The shift hand-off: the cashier's briefing, then out to the street, then the targets.
        ///
        /// Runs as a coroutine because the card has to be READ before the world changes under the
        /// player — the web awaits its promise for the same reason. The <see cref="_entering"/> latch
        /// is what stops a second T during the card from starting two shifts.
        /// </summary>
        public override void Enter()
        {
            if (_status != MissionStatus.Inactive || _entering) return;
            StartCoroutine(EnterRoutine());
        }

        private IEnumerator EnterRoutine()
        {
            _entering = true;

            // Released in a finally — an entry latch that survives a throw is unrecoverable,
            // because Enter() refuses to run again while it is set.
            try
            {
                // Hazel reads the briefing aloud while the card is up, and is cut off if the player
                // dismisses early — the web's exact arrangement.
                voice?.Play(_spec.BriefingVoiceUrl);
                if (runner?.Card != null) yield return runner.Card.ShowAndWait(_spec.BriefingLines);
                voice?.Stop();

                // Out of the storefront. The interior owns the teleport and the palette swap; the
                // mission must not learn how either works.
                if (interior != null && interior.Inside)
                {
                    interior.LeaveNow();
                    yield return null; // let the teleport land before anything reads the position
                }

                Begin();
            }
            finally
            {
                _entering = false;
            }
        }

        /// <summary>Picks the spots and spawns the round. Split out so the retry can reuse it.</summary>
        private void Begin()
        {
            if (_status == MissionStatus.Active) return;

            _root = new GameObject("Delivery Targets").transform;
            _delivered = 0;
            _timeLeft = _spec.TimeLimitSec;

            var spots = PickSpots();
            _chosen = spots.Count;
            for (var i = 0; i < spots.Count; i++) _targets.Add(Spawn(spots[i], i));

            _status = MissionStatus.Active;
            hud?.SetCounter($"🍕 {Carried}");

            if (verbose)
                Debug.Log($"[pizza] shift started: {_chosen} drops, {_timeLeft:0} s");
        }

        /// <summary>
        /// A random subset of the authored spots. Every one of the fifteen is the centre of a
        /// pavement rectangle the crowd already seeds, so all of them are walkable by construction —
        /// there is no reachability test to run and the original says why.
        /// </summary>
        private List<TheBlockConfig.DeliverySpotSpec> PickSpots()
        {
            var pool = new List<TheBlockConfig.DeliverySpotSpec>(_spec.CandidateSpots);
            for (var i = pool.Count - 1; i > 0; i--)
            {
                var j = Random.Range(0, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            var take = Mathf.Min(_spec.DeliveryCount, pool.Count);
            return pool.GetRange(0, take);
        }

        private Target Spawn(TheBlockConfig.DeliverySpotSpec spot, int index)
        {
            var at = Convert.Pos(spot.Raw);
            at.y = GroundAt(at);

            var target = new Target
            {
                Id = $"Delivery #{index + 1}",
                Position = at,
                ThankUrl = ThankUrlFor(index),
                Delivered = false,
            };

            var prefab = FaceFor(index);
            if (prefab != null)
            {
                target.Body = Instantiate(prefab, at, Convert.RotFromRadians(spot.Yaw), _root);
                target.Body.name = target.Id;

                // Never bound to a seed, so Pedestrian.Tick is never called and the blend tree sits
                // at Speed 0 — a stander, which is exactly the web's `mode: 'stand'`.
                if (target.Body.TryGetComponent<Pedestrian>(out var pedestrian)) pedestrian.enabled = false;
            }

            target.Beacon = Beacon.Create(
                _root, _spec.Beacon, TheBlockConfig.ColorFromHex(_spec.Beacon.Color), $"{target.Id} Beacon");
            target.Beacon.SetGround(at);

            // `Minor`: the dot without the name. Five "Delivery #n" labels at once is most of what
            // makes an open map unreadable, and the number says nothing the pin does not.
            MapRegistry.AddPoi(new MapPoi
            {
                Name = target.Id,
                Position = at,
                Kind = MapPoiKind.Marker,
                Minor = true,
            });

            return target;
        }

        private GameObject FaceFor(int index) =>
            facePrefabs.Count == 0 ? null : facePrefabs[index % facePrefabs.Count];

        private string ThankUrlFor(int index)
        {
            var faces = _spec.NpcSpecs;
            if (faces == null || faces.Count == 0) return null;
            return faces[index % faces.Count].ThankUrl;
        }

        /// <summary>
        /// Ground height at a drop-off. Casts DOWN from above and takes the FIRST hit, which on a
        /// pavement is the pavement — not the lowest, which is the ground plate at −0.05 and the
        /// fault behind U16's zebras z-fighting up through the street.
        /// </summary>
        private float GroundAt(Vector3 at)
        {
            var from = at + Vector3.up * 50f;
            return Physics.Raycast(from, Vector3.down, out var hit, 200f, groundMask,
                QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : 0f;
        }

        // ── the run ───────────────────────────────────────────────────────────────────────────

        private void Update()
        {
            // Beacons keep animating through a pop even after the shift ends, so this runs
            // regardless of state — otherwise the last delivery's pin freezes mid-flourish.
            for (var i = _fading.Count - 1; i >= 0; i--)
            {
                var beacon = _fading[i];
                if (beacon == null) { _fading.RemoveAt(i); continue; }
                if (!beacon.PopDone) continue;
                Destroy(beacon.gameObject);
                _fading.RemoveAt(i);
            }

            if (_entering || (runner != null && runner.Card != null && runner.Card.IsOpen)) return;

            // Indoors is the counter's business, not the street's: the shift is STARTED here and the
            // objective out on the pavement is not actionable from in the room.
            if (interior != null && interior.Inside)
            {
                TickCounter();
                return;
            }

            if (_status != MissionStatus.Active) return;

            _timeLeft -= Time.deltaTime;
            if (_timeLeft <= 0f)
            {
                _timeLeft = 0f;
                _status = MissionStatus.Failed; // out of time; the runner offers the retry
                hud?.SetCounter(null);
                if (verbose) Debug.Log("[pizza] out of time");
                return;
            }

            var near = Nearest(DeliveryPoint());
            hud?.SetPrompt(near != null ? "Press F to deliver" : null);

            // F hands over a pizza. The same key retries a FAILED shift, and the two cannot collide
            // because TryDeliver only ever finds a target while the shift is active.
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.fKey.wasPressedThisFrame) TryDeliver();
        }

        /// <summary>
        /// Hands a pizza to whoever is in reach. True if one was delivered.
        ///
        /// Public because it is the ACTION, and the key press is only one way to ask for it — the
        /// same reason <c>VehicleEnterExit.TryEnter</c> is public. It is also the only honest way to
        /// measure a delivery without a synthetic key press, and synthetic keys in this project have
        /// their own memory file about how they decay.
        /// </summary>
        public bool TryDeliver()
        {
            if (_status != MissionStatus.Active) return false;
            var target = Nearest(DeliveryPoint());
            if (target == null) return false;
            Deliver(target);
            return true;
        }

        /// <summary>
        /// The counter, which is where a shift begins and where a failed one is restarted.
        ///
        /// <b>T talks, not E</b>, and the split is the web build's: E is already the doorway and
        /// every vehicle, so overloading it at a counter inside a room you entered with E is how one
        /// press does two things. F retries, the same key it is everywhere else.
        ///
        /// The <see cref="CanStart"/> test is load-bearing rather than tidy. <see cref="Enter"/>
        /// no-ops on anything but Inactive, so without it a failed shift still played the whole
        /// briefing, the voiceover and the teleport and then silently spawned nothing.
        /// </summary>
        private void TickCounter()
        {
            var atCounter = interior.NearCounter;
            var mine = runner == null || runner.IsCurrent(Id);
            var keyboard = Keyboard.current;

            if (atCounter && _status == MissionStatus.Failed)
            {
                hud?.SetPrompt("Press F to retry your shift");
            }
            else if (atCounter && mine && CanStart)
            {
                hud?.SetPrompt("Press T to start your shift");
                if (keyboard != null && keyboard.tKey.wasPressedThisFrame) Enter();
            }
            else if (interior.AtExitPad)
            {
                hud?.SetPrompt("Press E to leave");
            }
            else
            {
                hud?.SetPrompt(null);
            }
        }

        /// <summary>
        /// Where the delivery is measured from: the vehicle while driving, the player on foot. The
        /// bike is the intended ride, and measuring from the man inside it would be a metre out.
        /// </summary>
        private Vector3 DeliveryPoint()
        {
            if (vehicles != null && vehicles.Mode == GameMode.Driving)
            {
                var vehicle = vehicles.ActiveVehicle;
                if (vehicle != null) return vehicle.GetTransform().position;
            }

            return player != null ? player.position : Vector3.zero;
        }

        private Target Nearest(Vector3 from)
        {
            Target best = null;
            var bestDistance = _spec.DeliverRadius;
            foreach (var target in _targets)
            {
                if (target.Delivered) continue;
                var d = Vector2.Distance(
                    new Vector2(from.x, from.z), new Vector2(target.Position.x, target.Position.z));
                if (d > bestDistance) continue;
                bestDistance = d;
                best = target;
            }

            return best;
        }

        private void Deliver(Target target)
        {
            if (target.Delivered) return;
            target.Delivered = true;
            _delivered++;

            MapRegistry.RemovePoi(target.Id);
            if (target.Beacon != null)
            {
                target.Beacon.Pop();
                _fading.Add(target.Beacon);
            }

            voice?.Play(target.ThankUrl); // this customer's own thank-you line

            if (_delivered >= _chosen)
            {
                _status = MissionStatus.Complete;
                hud?.SetCounter(null);
                hud?.SetPrompt(null);
                if (verbose) Debug.Log("[pizza] shift complete");
                return;
            }

            hud?.SetCounter($"🍕 {Carried}");
            if (verbose) Debug.Log($"[pizza] delivered {_delivered}/{_chosen}");
        }

        // ── exits ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The bust. Guarded on Active so a bust landing on the same frame as the last delivery
        /// cannot turn a completed shift back into a loss.
        /// </summary>
        public override void Fail()
        {
            if (_status != MissionStatus.Active) return;
            _status = MissionStatus.Failed;
            hud?.SetCounter(null);
            hud?.SetPrompt(null);
        }

        /// <summary>Restarts in place — on foot, indoors, or from the saddle. No briefing twice.</summary>
        public override void Retry()
        {
            if (_status != MissionStatus.Failed) return;
            Cleanup();
            _status = MissionStatus.Inactive;
            Begin();
        }

        /// <summary>
        /// Removes every target, beacon and pin. Idempotent, because the retry calls it first and
        /// the runner calls it again on the status edge.
        /// </summary>
        public override void Cleanup()
        {
            foreach (var target in _targets)
            {
                if (!target.Delivered) MapRegistry.RemovePoi(target.Id);
                if (target.Body != null) Destroy(target.Body);

                // A delivered beacon is mid-pop and lives in _fading, which owns its destruction.
                if (!target.Delivered && target.Beacon != null) Destroy(target.Beacon.gameObject);
            }

            _targets.Clear();

            foreach (var beacon in _fading)
                if (beacon != null)
                    Destroy(beacon.gameObject);
            _fading.Clear();

            // Only what this component made. A component may only destroy what it made — the fault
            // U18 found when CrowdSpawner.Bind deleted the blood pool off a shared object.
            if (_root != null) Destroy(_root.gameObject);
            _root = null;

            hud?.SetCounter(null);
        }

        /// <summary>Editor-side wiring, used by The Block → Build Campaign.</summary>
        public void SetFaces(List<GameObject> prefabs) => facePrefabs = prefabs ?? new List<GameObject>();
    }
}
