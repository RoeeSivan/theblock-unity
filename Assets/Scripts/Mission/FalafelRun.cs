using System.Collections.Generic;
using TheBlock.Core;
using TheBlock.Game;
using TheBlock.Npc;
using TheBlock.Police;
using TheBlock.UI;
using TheBlock.Vehicles;
using TheBlock.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TheBlock.Missions
{
    /// <summary>
    /// A delivery round for פלאפל הפעמונים - the game's repeatable street job.
    ///
    /// <b>Why it is not a campaign mission, and must never become one.</b> Every step in
    /// <c>Campaign.Missions</c> pays exactly once per run: <see cref="CampaignRunner.ReactToEdges"/>
    /// checks <c>Payouts.Has</c> and a replay gets a teardown and nothing else. That is right for a
    /// story and wrong for a job - and with the campaign cleared it leaves the player with a wallet
    /// that can only ever go down, against a power-up shop, a paint shop and a fuel pump that all
    /// keep charging. This pays every single time you finish it.
    ///
    /// It is structurally safe from being adopted: <c>MissionBuilder</c> orders the campaign from
    /// <c>campaignConfig.campaignText</c> and drops any mission whose id has no row there, and
    /// <c>"falafel"</c> has none. <b>Do not add one.</b>
    ///
    /// <b>Everything else is the pizza run</b>, deliberately - the user asked for the same mechanism,
    /// and the parts are already built and already play-tested: <see cref="Beacon"/> over each
    /// customer, a minor guide pin on the map, <c>F</c> to hand one over measured from the vehicle
    /// while driving and the player on foot, a clock. What is new is the loop around it:
    ///
    ///  * <b>T at the stand</b> starts a round, from the saddle or on foot. No briefing card and no
    ///    teleport - a job you run twenty times cannot open with a modal.
    ///  * <b>Finishing pays and escalates.</b> Round 1 is 3 drops in 150 s for $40; each round
    ///    finished back to back is one drop longer and $15 better, to a ceiling of 6 and $100.
    ///  * <b>Losing resets the streak to round 1.</b> The clock running out and getting busted are
    ///    the same edge, as they are in the campaign.
    ///
    /// <b>Free-roam police rules apply</b>, unlike a mission: crashes still mint stars and the cops
    /// still come. <see cref="CampaignRunner.ApplyPoliceRules"/> is deliberately untouched, because
    /// its rising edge CLEARS heat - extending it to cover this job would hand the player a wanted
    /// level launderer at the price of walking up to a falafel counter.
    /// </summary>
    public class FalafelRun : MissionBehaviour
    {
        [Header("The stand - written by Build Falafel Stand")]
        [Tooltip("fh_talk, the marker node on the pavement in front of the open bay.")]
        [SerializeField] private Transform talkPoint;

        [Tooltip("Customer faces, cycled per drop. Deliberately NOT the pizza run's five.")]
        [SerializeField] private List<GameObject> facePrefabs = new();

        [Tooltip("Layers the drop-off ground ray may hit. Districts and roads; never the player.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Debug")]
        [SerializeField] private bool verbose;

        [Header("Live state - read-only, shown so a round can be watched in the Inspector")]
        [SerializeField, HideInInspector] private int level = 1;

        private Campaign _campaign;
        private CampaignRunner _runner;
        private Transform _player;
        private VehicleEnterExit _vehicles;
        private MissionHud _hud;
        private Wallet _wallet;
        private BustSequence _bust;
        private Interior _interior;

        private TheBlockConfig.MissionSpec _spec;
        private MissionStatus _status = MissionStatus.Inactive;
        private readonly List<Target> _targets = new();
        private readonly List<Beacon> _fading = new();
        private readonly List<TheBlockConfig.DeliverySpotSpec> _pool = new();
        private Transform _root;
        private int _delivered;
        private int _chosen;
        private float _timeLeft;

        private class Target
        {
            public string Id;
            public Vector3 Position;
            public GameObject Body;
            public Beacon Beacon;
            public bool Delivered;
        }

        public override string Id => "falafel";
        public override string Title => "Falafel HaPaamonim Round";
        public override MissionStatus Status => _status;

        public override float? TimeLeft =>
            _status == MissionStatus.Active ? _timeLeft : (float?)null;

        public override string ObjectiveLine =>
            _status == MissionStatus.Active ? $"Falafel {_delivered}/{_chosen}" : null;

        /// <summary>Orders still in the bag. The HUD's own readout, separate from the objective.</summary>
        public int Carried => Mathf.Max(0, _chosen - _delivered);

        /// <summary>Which round the streak is on. 1 is a first round, or one after a loss.</summary>
        public int Level => level;

        /// <summary>What finishing the round in progress - or the next one - is worth.</summary>
        public int Pay => FalafelSpec.PayFor(level);

        private void Awake()
        {
            _campaign = FindAnyObjectByType<Campaign>();
            _runner = FindAnyObjectByType<CampaignRunner>();
            _vehicles = FindAnyObjectByType<VehicleEnterExit>();
            _hud = FindAnyObjectByType<MissionHud>();
            _wallet = FindAnyObjectByType<Wallet>();
            _bust = FindAnyObjectByType<BustSequence>();
            _interior = FindAnyObjectByType<Interior>();

            var pc = FindAnyObjectByType<TheBlock.Player.PlayerController>();
            if (pc != null) _player = pc.transform;
        }

        private void OnEnable()
        {
            // Its own subscription, not a hand-off from the campaign runner: a bust has to end this
            // round wherever it happens, and the runner only ever calls Fail() on a CAMPAIGN mission.
            if (_bust != null) _bust.Busted += OnBusted;
        }

        private void OnDisable()
        {
            if (_bust != null) _bust.Busted -= OnBusted;
            if (ReferenceEquals(FreeRoamJob.Active, this)) FreeRoamJob.Clear();
        }

        private void Start()
        {
            _spec = TheBlockConfig.Load()?.Mission;
            if (_spec == null)
            {
                Debug.LogError("FalafelRun: no missionConfig in the snapshot - no spots to deliver to.");
                enabled = false;
                return;
            }

            BuildPool();
        }

        /// <summary>
        /// The spots this shop delivers to: the nearest <see cref="FalafelSpec.NearestPool"/> of the
        /// fifteen the pizza run already uses.
        ///
        /// <b>Reused rather than authored, and that is the whole reason they are trustworthy.</b>
        /// Every one is the centre of a pavement rectangle the crowd already seeds, so all of them
        /// are walkable by construction - there is no reachability test to run, which is the same
        /// argument <c>DeliveryMission.PickSpots</c> makes. Cutting to the nearest ten is what makes
        /// a round read as this shop's own quarter instead of a tour of the city, and ten against a
        /// six-drop ceiling is what stops every capped round visiting the same six doors.
        /// </summary>
        private void BuildPool()
        {
            _pool.Clear();
            var all = _spec.CandidateSpots;
            if (all == null || all.Count == 0) return;

            var here = transform.position;
            var sorted = new List<TheBlockConfig.DeliverySpotSpec>(all);
            sorted.Sort((a, b) =>
                Flat(Convert.Pos(a.Raw) - here).sqrMagnitude
                    .CompareTo(Flat(Convert.Pos(b.Raw) - here).sqrMagnitude));

            var take = Mathf.Min(FalafelSpec.NearestPool, sorted.Count);
            for (var i = 0; i < take; i++) _pool.Add(sorted[i]);

            if (verbose)
                Debug.Log($"[falafel] pool: {_pool.Count} of {all.Count} spots, nearest first");
        }

        // ── the stand ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Close enough to the counter to be served.
        ///
        /// <b>The same predicate behind both the prompt and the action</b>, the rule
        /// <c>Interior.NearCounter</c> and <c>AutoShop.CanPaint</c> both keep - a prompt that offers
        /// a key the key does not answer is the failure this arrangement exists to prevent.
        /// </summary>
        public bool AtStand =>
            talkPoint != null &&
            Flat(MeasureFrom() - talkPoint.position).sqrMagnitude <
                FalafelSpec.TalkRadius * FalafelSpec.TalkRadius;

        /// <summary>
        /// Can a round be started right now? Not while one is running, and <b>not while a campaign
        /// mission is</b>: the story owns the HUD and the police rules while it is on, and two clocks
        /// on one screen is the confusion, not the feature.
        /// </summary>
        public bool CanStart =>
            _status == MissionStatus.Inactive && _pool.Count > 0 &&
            (_campaign == null || _campaign.Active == null);

        // ── the round ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Takes the order and starts the clock. No card, no voice-over, no teleport - see the class
        /// note. Public and overriding <see cref="MissionBehaviour.Enter"/> so a test can start a
        /// round without a synthetic key press, which in this project has its own memory file about
        /// how it decays.
        /// </summary>
        public override void Enter()
        {
            if (!CanStart) return;

            _root = new GameObject("Falafel Round").transform;
            _delivered = 0;
            _chosen = Mathf.Min(FalafelSpec.DropsFor(level), _pool.Count);
            _timeLeft = FalafelSpec.SecondsFor(level);

            foreach (var spot in PickSpots(_chosen)) _targets.Add(Spawn(spot, _targets.Count));

            _status = MissionStatus.Active;
            FreeRoamJob.Active = this;

            _hud?.SetCounter($"🧆 {Carried}");
            _hud?.ShowHint($"Round {level} - {_chosen} orders, ${Pay}");

            if (verbose)
                Debug.Log($"[falafel] round {level} started: {_chosen} drops, {_timeLeft:0} s, ${Pay}");
        }

        /// <summary>A random subset of the pool, shuffled so two rounds in a row are not the same run.</summary>
        private List<TheBlockConfig.DeliverySpotSpec> PickSpots(int count)
        {
            var bag = new List<TheBlockConfig.DeliverySpotSpec>(_pool);
            for (var i = bag.Count - 1; i > 0; i--)
            {
                var j = Random.Range(0, i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }

            return bag.GetRange(0, Mathf.Min(count, bag.Count));
        }

        private Target Spawn(TheBlockConfig.DeliverySpotSpec spot, int index)
        {
            var at = Convert.Pos(spot.Raw);
            at.y = GroundAt(at);

            var target = new Target
            {
                Id = $"Falafel #{index + 1}",
                Position = at,
                Delivered = false,
            };

            var prefab = facePrefabs.Count == 0 ? null : facePrefabs[index % facePrefabs.Count];
            if (prefab != null)
            {
                target.Body = Instantiate(prefab, at, Convert.RotFromRadians(spot.Yaw), _root);
                target.Body.name = target.Id;

                // Never bound to a seed, so Pedestrian.Tick never runs and the blend tree sits at
                // Speed 0 - a stander. It also keeps the customer out of CrowdSpawner.Crowd, which
                // is the only list RunOverSystem reads, so a round cannot be made unwinnable by
                // flattening the person you were sent to feed.
                if (target.Body.TryGetComponent<Pedestrian>(out var pedestrian)) pedestrian.enabled = false;
            }

            target.Beacon = Beacon.Create(
                _root, _spec.Beacon, TheBlockConfig.ColorFromHex(FalafelSpec.BeaconColor),
                $"{target.Id} Beacon");
            target.Beacon.SetGround(at);

            MapRegistry.AddPoi(new MapPoi
            {
                Name = target.Id,
                Position = at,
                Kind = MapPoiKind.Marker,
                Minor = true,
                Guide = true,
            });

            return target;
        }

        /// <summary>
        /// Ground height at a drop-off. Casts DOWN from above and takes the FIRST hit, which on a
        /// pavement is the pavement - never the lowest, which is the ground plate at −0.05 (memory:
        /// <c>lowest-raycast-hit-is-the-ground-plate</c>).
        /// </summary>
        private float GroundAt(Vector3 at)
        {
            var from = at + Vector3.up * 50f;
            return Physics.Raycast(from, Vector3.down, out var hit, 200f, groundMask,
                QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : 0f;
        }

        private void Update()
        {
            if (Core.Pause.Frozen) return; // no T, no F, and no clock behind a menu - see Core.Pause

            // Beacons keep animating through a pop after the round ends, so this runs regardless of
            // state - otherwise the last drop's pin freezes mid-flourish.
            for (var i = _fading.Count - 1; i >= 0; i--)
            {
                var beacon = _fading[i];
                if (beacon == null) { _fading.RemoveAt(i); continue; }
                if (!beacon.PopDone) continue;
                Destroy(beacon.gameObject);
                _fading.RemoveAt(i);
            }

            // A card is a modal the player is reading; it must not eat a T or an F behind it.
            if (_runner?.Card != null && _runner.Card.IsOpen) return;

            if (_status == MissionStatus.Active) TickRound();
            else TickStand();
        }

        private void TickRound()
        {
            // The story takes precedence if one somehow starts under a running round - it owns the
            // HUD and the police rules, and CanStart cannot see a mission entered after this began.
            if (_campaign != null && _campaign.Active != null)
            {
                if (verbose) Debug.Log("[falafel] a campaign mission started - the round is abandoned");
                Abandon();
                return;
            }

            _timeLeft -= Time.deltaTime;
            if (_timeLeft <= 0f)
            {
                _timeLeft = 0f;
                Lose("out of time");
                return;
            }

            // Inside the pizzeria the round is still on, but nothing about it is actionable and its
            // beacons are a kilometre away. The room owns the screen; see CampaignRunner.DriveHud.
            if (_interior != null && _interior.Inside) return;

            _hud?.SetObjective(ObjectiveLine);
            _hud?.SetTimer(_timeLeft, _timeLeft <= FalafelSpec.LowTimeSec);
            _hud?.SetCounter($"🧆 {Carried}");

            var near = Nearest(MeasureFrom());
            if (near != null) _hud?.SetPrompt("Press F to hand over the order");

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.fKey.wasPressedThisFrame) TryDeliver();
        }

        private void TickStand()
        {
            if (!AtStand || !CanStart) return;

            _hud?.SetPrompt($"Press T for a falafel round - {FalafelSpec.DropsFor(level)} orders, ${Pay}");

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.tKey.wasPressedThisFrame) Enter();
        }

        /// <summary>
        /// Hands over whatever is in reach. True if one was delivered.
        ///
        /// Public because it is the ACTION and a key press is only one way to ask for it - the same
        /// reason <c>DeliveryMission.TryDeliver</c> and <c>VehicleEnterExit.TryEnter</c> are public.
        /// </summary>
        public bool TryDeliver()
        {
            if (_status != MissionStatus.Active) return false;
            var target = Nearest(MeasureFrom());
            if (target == null) return false;
            Deliver(target);
            return true;
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

            TheBlock.Audio.GameAudio.Cue(TheBlock.Audio.SfxCue.Delivery);

            if (_delivered >= _chosen) Win();
            else if (verbose) Debug.Log($"[falafel] {_delivered}/{_chosen}");
        }

        // ── the exits ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Paid, and the streak goes up.
        ///
        /// <b>The payout lands here rather than on a status edge</b>, which is the one place this
        /// deliberately parts company with the campaign. <see cref="CampaignRunner"/>'s reactor is
        /// built around paying once and never again - it consults <c>Payouts</c>, marks the id, and
        /// treats a second completion as a replay owed nothing. There is no version of that which
        /// pays every time, so this owns its own exit and never touches <c>Payouts</c> at all.
        ///
        /// <b>The thermal bag is deliberately not consumed.</b> 🎒 doubles a MISSION payout and is
        /// spent by it; letting a $40 round burn an item bought for the $300 finale would be a trap
        /// dressed as a feature.
        /// </summary>
        private void Win()
        {
            var pay = Pay;
            _wallet?.Add(pay);
            TheBlock.Audio.GameAudio.Cue(TheBlock.Audio.SfxCue.Complete);

            var next = Mathf.Min(level + 1, MaxLevel);
            _hud?.ShowHint(level < MaxLevel
                ? $"🧆 Round done  +${pay}   -   next round: {FalafelSpec.DropsFor(next)} orders, ${FalafelSpec.PayFor(next)}"
                : $"🧆 Round done  +${pay}   -   top rate");

            if (verbose)
                Debug.Log($"[falafel] round {level} complete, paid ${pay} → ${_wallet?.Balance}");

            level = next;
            Finish();
        }

        /// <summary>The clock, or the cuffs. Both drop the streak back to the first round.</summary>
        private void Lose(string why)
        {
            TheBlock.Audio.GameAudio.Cue(TheBlock.Audio.SfxCue.Fail);
            _hud?.ShowHint($"🧆 Round lost - {why}. Back to the stand for another.");
            if (verbose) Debug.Log($"[falafel] round {level} lost: {why}");

            level = 1;
            Finish();
        }

        /// <summary>Ended by something that is not a win or a loss - no sting, no streak change.</summary>
        private void Abandon() => Finish();

        /// <summary>
        /// Back to Inactive, which is the only resting state this job has.
        ///
        /// <b>It never sits in Complete or Failed</b>, and that is not sloppiness: those states exist
        /// so <c>MissionFeedback</c> can see an EDGE and <see cref="CampaignRunner"/> can react to
        /// it. Nothing polls this one - it is not in <c>Campaign.Missions</c> - so the edge is the
        /// method that just ran, and a round that stayed Failed would simply be a job you could
        /// never start again.
        /// </summary>
        private void Finish()
        {
            Cleanup();
            _status = MissionStatus.Inactive;
            if (ReferenceEquals(FreeRoamJob.Active, this)) FreeRoamJob.Clear();

            // Handed back deliberately: the campaign's DriveHud stands down while a job is running,
            // so whatever it wants on screen only returns once these are cleared.
            _hud?.SetObjective(null);
            _hud?.SetTimer(null);
            _hud?.SetCounter(null);
        }

        /// <summary>The bust. A round lost to the cops is a round lost, exactly like the clock.</summary>
        private void OnBusted()
        {
            if (_status != MissionStatus.Active) return;
            Lose("busted");
        }

        /// <summary>Same contract as a mission's: end this run NOW as a loss, from outside.</summary>
        public override void Fail()
        {
            if (_status != MissionStatus.Active) return;
            Lose("called off");
        }

        /// <summary>
        /// Removes every customer, beacon and pin. Idempotent - <see cref="Finish"/> calls it on
        /// every exit, and only what this component made is destroyed.
        /// </summary>
        public override void Cleanup()
        {
            foreach (var target in _targets)
            {
                if (!target.Delivered) MapRegistry.RemovePoi(target.Id);
                if (target.Body != null) Destroy(target.Body);
                if (!target.Delivered && target.Beacon != null) Destroy(target.Beacon.gameObject);
            }

            _targets.Clear();

            foreach (var beacon in _fading)
                if (beacon != null)
                    Destroy(beacon.gameObject);
            _fading.Clear();

            if (_root != null) Destroy(_root.gameObject);
            _root = null;
        }

        // ── helpers ───────────────────────────────────────────────────────────────────────────

        /// <summary>The level at which both the drop count and the pay have stopped moving.</summary>
        private static int MaxLevel =>
            Mathf.Max(
                FalafelSpec.MaxDrops - FalafelSpec.BaseDrops,
                (FalafelSpec.MaxPay - FalafelSpec.BasePay) / FalafelSpec.PayStep) + 1;

        /// <summary>
        /// Where a delivery is measured from: the vehicle while driving, the player on foot. The
        /// bike is the intended ride and measuring from the man inside it would be a metre out.
        /// </summary>
        private Vector3 MeasureFrom()
        {
            if (_vehicles != null && _vehicles.Mode == GameMode.Driving)
            {
                var vehicle = _vehicles.ActiveVehicle;
                if (vehicle != null) return vehicle.GetTransform().position;
            }

            return _player != null ? _player.position : transform.position;
        }

        private Target Nearest(Vector3 from)
        {
            Target best = null;
            var bestDistance = _spec?.DeliverRadius ?? 6f;
            foreach (var target in _targets)
            {
                if (target.Delivered) continue;
                var d = Flat(target.Position - from).magnitude;
                if (d > bestDistance) continue;
                bestDistance = d;
                best = target;
            }

            return best;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        /// <summary>Editor-side wiring, used by The Block → Build Falafel Stand.</summary>
        public void SetFaces(List<GameObject> prefabs) => facePrefabs = prefabs ?? new List<GameObject>();
    }
}
