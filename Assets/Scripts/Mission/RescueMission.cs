using System.Collections;
using System.Collections.Generic;
using TheBlock.Audio;
using TheBlock.Core;
using TheBlock.Npc;
using TheBlock.UI;
using TheBlock.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TheBlock.Missions
{
    /// <summary>
    /// M3, the rooftop rescue — the port of <c>src/mission/rescue-mission.ts</c>.
    ///
    /// A near-twin of the delivery run by design: the same lifecycle, the same overhead beacon, the
    /// same map pin. What differs is that the targets are on ROOFS and "deliver" becomes "descend
    /// the Huey close enough and they climb aboard" — no key press, the same feel as the web's.
    ///
    /// The survivors reuse the delivery run's five faces, which is the ORIGINAL's own choice: its
    /// <c>rescue.config.ts</c> deliberately lists no faces of its own so there is a single source of
    /// them. Here that costs nothing extra again, because they are the crowd's prefabs either way.
    ///
    /// <b>The roofs are baked</b> (<see cref="RoofSpots"/>), not sampled at runtime. See that file
    /// for why, and <c>WorldBuilder.Rescue</c> for the cast that finds them.
    /// </summary>
    public class RescueMission : MissionBehaviour
    {
        [Header("Scene — found automatically when left empty")]
        [SerializeField] private CampaignRunner runner;
        [SerializeField] private VehicleEnterExit vehicles;
        [SerializeField] private MissionHud hud;
        [SerializeField] private Voice voice;

        [Header("Content — written by Build Campaign")]
        [SerializeField] private RoofSpots roofs;
        [SerializeField] private List<GameObject> facePrefabs = new();
        [SerializeField] private HelicopterController helicopter;

        [Header("Debug")]
        [SerializeField] private bool verbose;

        private TheBlockConfig.RescueSpec _spec;
        private TheBlockConfig.BeaconSpec _beaconSpec;
        private MissionStatus _status = MissionStatus.Inactive;
        private readonly List<Survivor> _survivors = new();
        private readonly List<Beacon> _fading = new();
        private Transform _root;
        private int _rescued;
        private int _chosen;
        private float _timeLeft;
        private bool _entering;

        private class Survivor
        {
            public string Id;
            public Vector3 Position; // 3D: the pickup test is a sphere, not a ground circle
            public GameObject Body;
            public Beacon Beacon;
            public bool Rescued;
        }

        public override string Id => "heli";
        public override string Title => "Helicopter Rescue";
        public override MissionStatus Status => _status;

        public override float? TimeLeft =>
            _status == MissionStatus.Active ? _timeLeft : (float?)null;

        public override string ObjectiveLine =>
            _status == MissionStatus.Active ? $"Survivors {_rescued}/{_chosen}" : null;

        private void Awake()
        {
            if (runner == null) runner = FindAnyObjectByType<CampaignRunner>();
            if (vehicles == null) vehicles = FindAnyObjectByType<VehicleEnterExit>();
            if (hud == null) hud = FindAnyObjectByType<MissionHud>();
            if (voice == null) voice = FindAnyObjectByType<Voice>();
            if (helicopter == null) helicopter = FindAnyObjectByType<HelicopterController>();
        }

        private void Start()
        {
            var snapshot = TheBlockConfig.Load();
            _spec = snapshot?.Rescue;
            _beaconSpec = snapshot?.Mission?.Beacon; // the shared pin; only the colour differs
            if (_spec == null)
            {
                Debug.LogError("RescueMission: no rescueConfig in the snapshot.");
                enabled = false;
                return;
            }

            // The chopper is Remy's to give: locked until the dance is won, which is the cursor
            // reaching this step. The web calls the same flag heliUnlocked.
            if (helicopter != null) helicopter.Unlocked = () => runner == null || runner.IsReached(Id);
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

            // The latch is released in a finally, so a throw anywhere inside cannot leave the
            // mission believing it is still starting — which is unrecoverable, because Enter()
            // refuses to run again while it is set.
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

            var spots = PickSpots();
            if (spots.Count == 0)
            {
                // The web build's own guard, and its comment says exactly why: its completion check
                // lives INSIDE the loop over survivors, so with none placed the run could never be
                // won and the player flew until the clock hard-failed a mission that was impossible.
                // Treat an empty bake as already finished rather than as a guaranteed loss.
                Debug.LogError("RescueMission: no roof spots baked — run The Block → Bake Roof Spots.");
                _chosen = 0;
                _rescued = 0;
                _status = MissionStatus.Complete;
                return;
            }

            _root = new GameObject("Rescue Survivors").transform;
            _rescued = 0;
            _timeLeft = _spec.TimeLimitSec;
            _chosen = spots.Count;

            for (var i = 0; i < spots.Count; i++) _survivors.Add(Spawn(spots[i], i));

            _status = MissionStatus.Active;
            if (verbose) Debug.Log($"[rescue] {_chosen} survivors, {_timeLeft:0} s");
        }

        /// <summary>
        /// A random subset of the baked pool, re-checked for separation.
        ///
        /// The bake already spaced its whole pool, but a SUBSET of a spaced set is still spaced, so
        /// this only has to shuffle and take — the expensive part happened at build time.
        /// </summary>
        private List<Vector3> PickSpots()
        {
            var picked = new List<Vector3>();
            if (roofs == null || roofs.Count == 0) return picked;

            var pool = new List<RoofSpots.Spot>(roofs.Spots);
            for (var i = pool.Count - 1; i > 0; i--)
            {
                var j = Random.Range(0, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            foreach (var spot in pool)
            {
                if (picked.Count >= _spec.SurvivorCount) break;
                picked.Add(spot.Position);
            }

            return picked;
        }

        private Survivor Spawn(Vector3 at, int index)
        {
            var survivor = new Survivor
            {
                Id = $"Survivor #{index + 1}",
                Position = at,
                Rescued = false,
            };

            var prefab = facePrefabs.Count == 0 ? null : facePrefabs[index % facePrefabs.Count];
            if (prefab != null)
            {
                // A random facing, as the web does: four people all looking the same way down a
                // skyline reads as a spawn, not as people who got stuck up there.
                var yaw = Random.Range(0f, 360f);
                survivor.Body = Instantiate(prefab, at, Quaternion.Euler(0f, yaw, 0f), _root);
                survivor.Body.name = survivor.Id;
                if (survivor.Body.TryGetComponent<Pedestrian>(out var pedestrian)) pedestrian.enabled = false;
            }

            survivor.Beacon = Beacon.Create(
                _root, _beaconSpec, TheBlockConfig.ColorFromHex(_spec.BeaconColor), $"{survivor.Id} Beacon");
            survivor.Beacon.SetGround(at);

            MapRegistry.AddPoi(new MapPoi
            {
                Name = survivor.Id,
                Position = at,
                Kind = MapPoiKind.Marker,
                Minor = true,
            });

            return survivor;
        }

        // ── the run ───────────────────────────────────────────────────────────────────────────

        private void Update()
        {
            for (var i = _fading.Count - 1; i >= 0; i--)
            {
                var beacon = _fading[i];
                if (beacon == null) { _fading.RemoveAt(i); continue; }
                if (!beacon.PopDone) continue;
                Destroy(beacon.gameObject);
                _fading.RemoveAt(i);
            }

            if (_entering || (runner?.Card != null && runner.Card.IsOpen)) return;

            var flying = vehicles != null && vehicles.Mode == GameMode.Driving &&
                         helicopter != null && ReferenceEquals(vehicles.ActiveVehicle, helicopter);

            if (_status != MissionStatus.Active)
            {
                TickHeliPrompt(flying);
                return;
            }

            _timeLeft -= Time.deltaTime;
            if (_timeLeft <= 0f)
            {
                _timeLeft = 0f;
                _status = MissionStatus.Failed;
                hud?.SetPrompt(null);
                if (verbose) Debug.Log("[rescue] out of time");
                return;
            }

            // Auto-pickup: no key press, the same feel the web gives it. Only from the CHOPPER —
            // walking onto a roof and collecting someone would make the aircraft optional.
            if (flying) RescueNear(helicopter.transform.position);
        }

        /// <summary>F in the chopper starts the run, or retries a timed-out one.</summary>
        private void TickHeliPrompt(bool flying)
        {
            if (!flying) return;

            if (_status == MissionStatus.Failed) hud?.SetPrompt("Press F to retry rescue");
            else if (_status == MissionStatus.Inactive) hud?.SetPrompt("Press F to start rescue");
            else return;

            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.fKey.wasPressedThisFrame) return;

            if (_status == MissionStatus.Failed) Retry();
            else if (runner == null || runner.IsCurrent(Id)) Enter();
        }

        /// <summary>
        /// Scoops up everyone inside <c>rescueRadius</c> in 3D. Generous, so no pixel-perfect
        /// landing is wanted — but it IS 3D, so overflying at altitude collects nobody.
        /// </summary>
        public bool RescueNear(Vector3 from)
        {
            if (_status != MissionStatus.Active) return false;

            var any = false;
            foreach (var survivor in _survivors)
            {
                if (survivor.Rescued) continue;
                if (Vector3.Distance(from, survivor.Position) > _spec.RescueRadius) continue;

                survivor.Rescued = true;
                _rescued++;
                any = true;

                MapRegistry.RemovePoi(survivor.Id);
                if (survivor.Body != null) survivor.Body.SetActive(false); // aboard now
                if (survivor.Beacon != null)
                {
                    survivor.Beacon.Pop();
                    _fading.Add(survivor.Beacon);
                }

                if (verbose) Debug.Log($"[rescue] {_rescued}/{_chosen}");

                if (_rescued < _chosen) continue;
                _status = MissionStatus.Complete;
                hud?.SetPrompt(null);
                break;
            }

            return any;
        }

        // ── exits ─────────────────────────────────────────────────────────────────────────────

        public override void Fail()
        {
            if (_status != MissionStatus.Active) return;
            _status = MissionStatus.Failed;
            hud?.SetPrompt(null);
        }

        /// <summary>
        /// Restarts, and puts the chopper back beside the player.
        ///
        /// Without that snap the fresh run opens with a long walk: after a fail the Huey is wherever
        /// it was abandoned, which for a rescue is usually a rooftop halfway across the city, and the
        /// clock starts again immediately.
        /// </summary>
        public override void Retry()
        {
            if (_status != MissionStatus.Failed) return;
            Cleanup();
            _status = MissionStatus.Inactive;
            helicopter?.Respawn();
            Begin();
        }

        public override void Cleanup()
        {
            foreach (var survivor in _survivors)
            {
                if (!survivor.Rescued) MapRegistry.RemovePoi(survivor.Id);
                if (survivor.Body != null) Destroy(survivor.Body);
                if (!survivor.Rescued && survivor.Beacon != null) Destroy(survivor.Beacon.gameObject);
            }

            _survivors.Clear();

            foreach (var beacon in _fading)
                if (beacon != null)
                    Destroy(beacon.gameObject);
            _fading.Clear();

            if (_root != null) Destroy(_root.gameObject);
            _root = null;
        }

        /// <summary>Editor-side wiring, used by The Block → Build Campaign.</summary>
        public void SetContent(RoofSpots baked, List<GameObject> faces, HelicopterController craft)
        {
            roofs = baked;
            facePrefabs = faces ?? new List<GameObject>();
            helicopter = craft;
        }
    }
}
