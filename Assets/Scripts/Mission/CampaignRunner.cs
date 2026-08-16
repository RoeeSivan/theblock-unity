using System.Collections.Generic;
using TheBlock.Core;
using TheBlock.Game;
using TheBlock.Police;
using TheBlock.UI;
using TheBlock.Vehicles;
using TheBlock.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TheBlock.Missions
{
    /// <summary>
    /// The campaign's frame loop — the port of the mission block in the web build's <c>main.ts</c>.
    ///
    /// The web keeps this in one place on purpose, and the reason is written into its own comments:
    /// the payout, the card, the sting and the teardown all used to be scattered across each
    /// mission's exits, and they drifted. One reactor over <see cref="MissionFeedback"/>'s status
    /// edges is what makes "a bust and a clock time-out do the same thing" true by construction.
    ///
    /// It owns, in this order every frame:
    ///  1. the cursor (<see cref="CampaignDirector.Tick"/>) and the unlock record,
    ///  2. the police rules a mission changes,
    ///  3. the retry key,
    ///  4. the completion/failure reactor — teardown, payout, card,
    ///  5. the HUD's objective line, clock and counter.
    ///
    /// It owns no gameplay. A mission spawns its own actors and runs its own clock; this only ever
    /// asks it for a <see cref="MissionStatus"/>.
    /// </summary>
    public class CampaignRunner : MonoBehaviour
    {
        [Header("Scene — found automatically when left empty")]
        [SerializeField] private Campaign campaign;
        [SerializeField] private CampaignDirector director;
        [SerializeField] private MissionHud hud;
        [SerializeField] private BriefingCard card;
        [SerializeField] private Wallet wallet;
        [SerializeField] private Heat heat;
        [SerializeField] private BustSequence bust;
        [SerializeField] private VehicleEnterExit vehicles;
        [SerializeField] private Interior interior;

        [Header("Debug")]
        [Tooltip("Jump the cursor to this mission index on Play instead of starting at the first. " +
                 "−1 starts at mission 1. This is the port of the web build's ?mission= URL flag, " +
                 "and like it, it runs the REAL entry path — it selects the step, it does not fake " +
                 "completing the ones before it.")]
        [SerializeField] private int debugStartMission = -1;

        [Tooltip("Log every status edge, payout and cursor move. Off for a normal play-test.")]
        [SerializeField] private bool verbose;

        private readonly MissionFeedback _feedback = new();
        private TheBlockConfig.Snapshot _snapshot;

        // The queued card. Edges are rare — at most one mission resolves at a time — so one slot is
        // enough, which is the web build's own reasoning for a single pending slot.
        private struct Pending
        {
            public List<string> Lines;
            public bool Win;
            public bool Final;
        }

        private Pending? _pending;
        private bool _winShown;
        private bool _lastInMission;
        private int _lastSavedIndex = -1;

        /// <summary>Is this the step the player is on? Every in-world mission trigger asks this.</summary>
        public bool IsCurrent(string id) => director != null && director.IsCurrent(id);

        /// <summary>Has the cursor reached this step? What gates the heli and the jetski.</summary>
        public bool IsReached(string id) => director != null && director.IsReached(id);

        /// <summary>The campaign copy, for a mission that wants its own briefing lines.</summary>
        public TheBlockConfig.Snapshot Snapshot => _snapshot;

        /// <summary>The card, so a mission's entry sequence can brief before it spawns anything.</summary>
        public BriefingCard Card => card;

        private void Awake()
        {
            if (campaign == null) campaign = FindAnyObjectByType<Campaign>();
            if (director == null) director = FindAnyObjectByType<CampaignDirector>();
            if (hud == null) hud = FindAnyObjectByType<MissionHud>();
            if (card == null) card = FindAnyObjectByType<BriefingCard>();
            if (wallet == null) wallet = FindAnyObjectByType<Wallet>();
            if (heat == null) heat = FindAnyObjectByType<Heat>();
            if (bust == null) bust = FindAnyObjectByType<BustSequence>();
            if (vehicles == null) vehicles = FindAnyObjectByType<VehicleEnterExit>();
            if (interior == null) interior = FindAnyObjectByType<Interior>();
        }

        private void OnEnable()
        {
            if (bust != null) bust.Busted += OnBusted;
        }

        private void OnDisable()
        {
            if (bust != null) bust.Busted -= OnBusted;
        }

        private void Start()
        {
            _snapshot = TheBlockConfig.Load();
            if (_snapshot == null)
            {
                Debug.LogError("CampaignRunner: no config snapshot — the campaign cannot start.");
                enabled = false;
                return;
            }

            director?.BuildSteps(_snapshot);

            // THE CAMPAIGN ALWAYS OPENS ON MISSION 1, which is the web build's behaviour and not a
            // simplification of it: `createCampaign` sets `idx = 0` on every load and NOTHING there
            // reads the unlock index — `?mission=` is the only thing that moves the opening cursor.
            //
            // U20 shipped a resume instead ("Continue, with no title screen to press it on"), and
            // the play-test found what that actually feels like: a finished save opens on the FINAL
            // mission's objective — "Get to the jetski · chase the thief" over a fresh $0 wallet —
            // with no way back to the pizza run short of wiping PlayerPrefs. A Continue needs a menu
            // offering it, which is U26's; without one, every Play is a New Game.
            //
            // `Progress.UnlockedIndex` is still recorded below on every cursor move. It is what
            // U26's Mission Select will read, and it is monotonic for exactly that reason.
            var start = debugStartMission >= 0 ? debugStartMission : 0;
            campaign?.Select(start);
            _lastSavedIndex = campaign != null ? campaign.Index : 0;

            // Adopt the current statuses before the first poll, or a mission the scene already
            // reports complete fires its handoff card on frame one.
            _feedback.Prime(campaign?.Missions);

            director?.Refresh();

            // The intro card is once per profile, and it is also how a fresh player learns the keys
            // before U26 has a title screen to teach them on.
            if (start == 0 && Onboarding.FirstTime("intro") && _snapshot.Campaign?.IntroLines != null)
                card?.Show(_snapshot.Campaign.IntroLines);

            if (verbose)
                Debug.Log($"[campaign] start at {start} ({campaign?.Current?.Id}), " +
                          $"unlocked {Progress.UnlockedIndex}, cash ${wallet?.Balance}");
        }

        private void Update()
        {
            if (campaign == null) return;

            ApplyPoliceRules();
            HandleRetryKey();

            director?.Tick();

            if (campaign.Index != _lastSavedIndex)
            {
                _lastSavedIndex = campaign.Index;
                Progress.RecordReached(_lastSavedIndex);
                if (verbose) Debug.Log($"[campaign] cursor → {_lastSavedIndex} ({campaign.Current?.Id})");
            }

            ReactToEdges();
            PresentPendingCard();
            DriveHud();
        }

        // ── the police rules a mission changes ────────────────────────────────────────────────

        /// <summary>
        /// Two rules, both the web build's, and both about not letting free-roam heat decide a
        /// scripted run.
        ///
        /// <b>Crash heat is off inside a mission.</b> The only mission you drive through is the
        /// pizza run, on a motorcycle in Florentin's alleys, where clipping a wall is constant —
        /// minting stars for it punishes the route rather than the driving. Running someone over
        /// stays a crime everywhere, because that is always a choice.
        ///
        /// <b>Heat never carries INTO a mission.</b> The rising edge wipes it, so a run can never
        /// open already doomed by stars earned in free roam. It deliberately does carry OUT: finish
        /// the deliveries with a cop on your bumper and he is still there afterwards. A retry gets
        /// the same treatment for free — a failed mission is not active, so restarting it is itself
        /// a false→true edge.
        /// </summary>
        private void ApplyPoliceRules()
        {
            var inMission = campaign.Active != null;

            if (heat != null)
            {
                heat.SuppressCrash = inMission;
                if (inMission && !_lastInMission) heat.Clear();
            }

            _lastInMission = inMission;
        }

        private void OnBusted()
        {
            // The bust is a mission-failure edge and nothing more — it calls Fail(), which lands in
            // the same state the clock does, and the reactor below does the rest.
            var active = campaign != null ? campaign.Active : null;
            if (active == null) return;
            if (verbose) Debug.Log($"[campaign] busted during '{active.Id}' → Fail()");
            active.Fail();
        }

        // ── the retry key ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// F retries a failed mission, from anywhere, so a fail can never strand the player.
        ///
        /// <b>F, not R</b>, and that is not a preference: R is every vehicle's own respawn/flip, and
        /// they poll it themselves while driven — an R-retry fired twice, restarting the shift AND
        /// teleporting the bike out from under you. F is free, because a failed mission has no
        /// delivery target to hand a pizza to, so the two uses of the key cannot collide.
        /// </summary>
        private void HandleRetryKey()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.fKey.wasPressedThisFrame) return;
            if (card != null && card.IsOpen) return;

            var failed = campaign.Failed;
            if (failed == null) return;

            if (verbose) Debug.Log($"[campaign] retry '{failed.Id}'");
            failed.Retry();
        }

        // ── the reactor ───────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One place turns a status edge into a teardown, a payout and a card. Everything about the
        /// shape of this is the web build's, including the order: cleanup first, so a completed
        /// mission's actors are gone before the card that celebrates it goes up.
        /// </summary>
        private void ReactToEdges()
        {
            var edges = _feedback.Poll(campaign.Missions);
            if (edges.Count == 0) return;

            foreach (var edge in edges)
            {
                var text = TextFor(edge.Id);
                if (text == null) continue;

                // Teardown on BOTH exits. A player who walks away from a failed run instead of
                // retrying used to leave its beacons, map pins and colliders in the world for the
                // rest of the session. Idempotent, so a retry calling it first costs nothing.
                campaign.Find(edge.Id)?.Cleanup();

                // A completion whose payout is ALREADY BANKED is a replay of a cleared step, not a
                // first clear — U28 opened the dance to be danced again, and nothing else can reach
                // this twice. The campaign owes a replay no sting, no card and no cash; the mission's
                // own result panel is the feedback. It is read here, before the sting, because a
                // replay that flopped still reports Complete (a replay cannot un-clear a step) and
                // would otherwise chime a success over its own "Routine flopped".
                var replay = edge.Complete && Payouts.Has(edge.Id);

                // The sting goes HERE, on the one reactor that owns every terminal edge, for the same
                // reason the card and the payout do: a bust, a clock time-out and a completed run are
                // three ways into two outcomes, and each mission announcing its own would be four
                // places to forget one. The web build passes `sting: sfx.playComplete` INTO this
                // reactor as a callback for exactly this reason; a static call is the same shape with
                // nothing to wire.
                if (!replay)
                    TheBlock.Audio.GameAudio.Cue(edge.Complete
                        ? TheBlock.Audio.SfxCue.Complete
                        : TheBlock.Audio.SfxCue.Fail);

                if (!edge.Complete)
                {
                    if (verbose) Debug.Log($"[campaign] '{edge.Id}' FAILED");
                    _pending = new Pending { Lines = new List<string> { text.Fail }, Win = false };
                    continue;
                }

                // Pay once per run, and only ever announce a FIRST clear. The guard behind `replay`
                // has to be PERSISTED — the web build shipped it in memory beside a persisted wallet
                // and every mission paid again after a reload — and a handoff card is the other half
                // of the same rule: "now get to the helicopter", re-read every time someone dances
                // for fun, is the campaign talking over free roam. The replay stops here, having had
                // its teardown above, which is all it was owed.
                if (replay)
                {
                    if (verbose) Debug.Log($"[campaign] '{edge.Id}' replayed — no payout, no card");
                    continue;
                }

                Payouts.Mark(edge.Id);
                wallet?.Add(text.Reward);
                var payLine = $"{text.Done}  (+${text.Reward})";
                if (verbose) Debug.Log($"[campaign] '{edge.Id}' paid ${text.Reward} → ${wallet?.Balance}");

                _pending = campaign.IsComplete
                    ? WinCard(payLine)
                    : HandoffCard(payLine, edge.Id);
            }
        }

        /// <summary>The finale: this step's payout, the win line, and what the run banked.</summary>
        private Pending WinCard(string payLine)
        {
            var lines = new List<string> { payLine };
            if (_snapshot.Campaign?.WinLine != null) lines.Add(_snapshot.Campaign.WinLine);

            // `endingLine` is a function in campaign.config.ts, so the exporter cannot carry it —
            // this is the one string in the campaign's copy that is re-implemented rather than read.
            lines.Add($"💵 You banked ${wallet?.Balance ?? 0} driving for The Block. Roll credits.");
            return new Pending { Lines = lines, Win = true, Final = true };
        }

        /// <summary>Between missions: what you just earned, then the next job's beat and waypoint.</summary>
        private Pending HandoffCard(string payLine, string doneId)
        {
            var lines = new List<string> { payLine };
            var next = NextTextAfter(doneId);
            if (next != null)
            {
                if (!string.IsNullOrEmpty(next.Story)) lines.Add(next.Story);
                if (!string.IsNullOrEmpty(next.Objective)) lines.Add(next.Objective);
            }

            return new Pending { Lines = lines, Win = true };
        }

        /// <summary>
        /// Shows the queued card once nothing else owns the screen.
        ///
        /// The card does not pause the loop, so this is fine mid-drive — the gate only avoids
        /// clobbering another card that is still up.
        /// </summary>
        private void PresentPendingCard()
        {
            if (_pending == null || card == null || card.IsOpen) return;

            var pending = _pending.Value;
            _pending = null;

            // The win card is once-ever: it has to survive a Mission Select replay of the finale,
            // which U26 will make reachable.
            if (pending.Final)
            {
                if (_winShown) return;
                _winShown = true;
            }

            card.Flash(pending.Win);
            card.Show(pending.Lines);
        }

        // ── the HUD ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The objective line, the clock and the counter, driven from the running mission when there
        /// is one and the CURRENT step otherwise, so a finished mission stops masking the next
        /// objective. The web build's <c>hud-driver.ts</c> makes the same choice for the same reason;
        /// what differs is that each mission answers for its own
        /// line here (<see cref="MissionBehaviour.ObjectiveLine"/>) instead of the driver holding a
        /// chain of per-mission special cases.
        /// </summary>
        private void DriveHud()
        {
            if (hud == null) return;

            // Nothing campaign-shaped belongs on screen inside the storefront: the room has its own
            // prompts and the objective out on the street is not actionable from in there.
            if (interior != null && interior.Inside)
            {
                hud.SetObjective(null);
                hud.SetTimer(null);
                return;
            }

            // THE RUNNING MISSION WINS OVER THE CURSOR when the two differ. Normally they are the
            // same object, because a mission is entered from the step it belongs to — but a
            // REPLAYED step is not: U28 left the dance danceable after it is cleared, and by then
            // the cursor has moved to the helicopter. Reading the cursor there puts "Get to the
            // helicopter", or the win line on a finished save, over a routine the player is dancing
            // this second. Falls back to the cursor whenever nothing is running, which is every
            // other frame in the game.
            var current = campaign.Active ?? campaign.Current;

            if (campaign.Active == null && director != null && director.IsWon)
            {
                hud.SetObjective(_snapshot.Campaign?.WinLine);
                hud.SetTimer(null);
                return;
            }

            if (current != null && current.Status == MissionStatus.Failed)
            {
                var text = TextFor(current.Id);
                hud.SetObjective(text?.Fail ?? "Press F to retry");
                hud.SetTimer(0f, true); // frozen at 0:00, still red — the fail is legible at a glance
                return;
            }

            var line = current != null && current.Status == MissionStatus.Active
                ? current.ObjectiveLine
                : null;
            hud.SetObjective(line ?? director?.ObjectiveText);

            var left = current?.TimeLeft;
            hud.SetTimer(left, left.HasValue && left.Value <= LowTimeFor(current));
        }

        /// <summary>Each mission's own "the clock is red now" threshold, from its config block.</summary>
        private float LowTimeFor(MissionBehaviour mission)
        {
            if (mission == null || _snapshot == null) return 30f;
            switch (mission.Id)
            {
                case "pizza": return _snapshot.Mission?.LowTimeSec ?? 30f;
                case "heli": return _snapshot.Rescue?.LowTimeSec ?? 30f;
                case "jetski": return _snapshot.Chase?.LowTimeSec ?? 30f;
                default: return 30f;
            }
        }

        // ── copy lookup ───────────────────────────────────────────────────────────────────────

        /// <summary>This step's row in <c>campaign.config.ts</c>, or null.</summary>
        public TheBlockConfig.CampaignTextSpec TextFor(string id)
        {
            var rows = _snapshot?.Campaign?.CampaignText;
            if (rows == null || id == null) return null;
            foreach (var row in rows)
                if (row != null && row.Id == id)
                    return row;
            return null;
        }

        /// <summary>The copy for whatever comes after <paramref name="id"/> in campaign order.</summary>
        private TheBlockConfig.CampaignTextSpec NextTextAfter(string id)
        {
            var i = campaign.IndexOfId(id);
            if (i < 0 || i + 1 >= campaign.Missions.Count) return null;
            var next = campaign.Missions[i + 1];
            return next == null ? null : TextFor(next.Id);
        }
    }
}
