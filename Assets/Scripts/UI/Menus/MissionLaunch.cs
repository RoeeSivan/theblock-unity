using TheBlock.Core;
using TheBlock.Missions;
using TheBlock.Npc;
using UnityEngine;

namespace TheBlock.UI.Menus
{
    /// <summary>
    /// "Jump straight to mission N" — the port of <c>src/ui/campaign-launch.ts</c>.
    ///
    /// Move the cursor, then put the player where that mission begins. Both halves matter: selecting
    /// the step alone leaves someone standing in a car park with an objective 600 m away and no idea
    /// they chose it.
    ///
    /// <b>It teleports; it does not mount.</b> The web build mounts the helicopter and the jetski
    /// because otherwise you are dropped beside — or swimming next to — a vehicle whose mission is
    /// armed by <c>F</c> while riding. Here the player is put down at the same waypoint and presses
    /// <c>E</c> themselves, for two reasons. <see cref="Vehicles.VehicleEnterExit"/> has no public
    /// programmatic entry (its <c>EnterModal</c> pair belongs to the dance, not to vehicles), so
    /// mounting means opening up the enter/exit machine — and U8, U23 and U24 are all still awaiting
    /// their play-test. And the walk of six metres to the aircraft is not a cost: it is the same
    /// three keys the mission teaches anyway.
    ///
    /// <b>Every coordinate comes from <see cref="CampaignDirector.TryStepPosition"/></b>, which is
    /// the table the objective pin already draws from. The web has a second, hand-written one in
    /// this file; two tables that agree today are two tables that disagree after the first move.
    /// </summary>
    public class MissionLaunch : MonoBehaviour
    {
        [Header("Scene — found automatically when left empty")]
        [SerializeField] private Campaign campaign;
        [SerializeField] private CampaignDirector director;
        [SerializeField] private CampaignRunner runner;
        [SerializeField] private Player.PlayerController player;

        [Tooltip("Metres east of the waypoint the player is set down — the web's own `spawn.x + 4`. " +
                 "Far enough not to stand inside the giver or the aircraft, near enough to see it.")]
        [SerializeField] private float standOff = 4f;

        [Tooltip("Layers the ground search may hit. Default is everything.")]
        [SerializeField] private LayerMask groundMask = ~0;

        private TheBlockConfig.Snapshot _snapshot;

        private void Awake()
        {
            if (campaign == null) campaign = FindAnyObjectByType<Campaign>();
            if (director == null) director = FindAnyObjectByType<CampaignDirector>();
            if (runner == null) runner = FindAnyObjectByType<CampaignRunner>();
            if (player == null) player = FindAnyObjectByType<Player.PlayerController>();
        }

        private void Start() => _snapshot = TheBlockConfig.Load();

        /// <summary>
        /// Select mission <paramref name="index"/> and stand the player at its start.
        /// <paramref name="fresh"/> is New Game — it is the only path that shows the intro card.
        /// </summary>
        public void Launch(int index, bool fresh)
        {
            runner?.BeginRun(index, fresh);

            // New Game is not a jump: mission 1 begins where the player already is, and moving them
            // would take away the drive to the pizzeria that the first objective is asking for.
            if (fresh) return;

            var id = campaign != null && index >= 0 && index < campaign.Missions.Count
                ? campaign.Missions[index]?.Id
                : null;
            if (id == null || player == null || director == null) return;
            if (!director.TryStepPosition(id, out var waypoint)) return;

            var at = new Vector3(waypoint.x + standOff, FloorFor(id, waypoint), waypoint.z);

            // Face the thing they came for, so the first frame after the jump shows it.
            var toWaypoint = new Vector3(waypoint.x - at.x, 0f, waypoint.z - at.z);
            var yaw = toWaypoint.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(toWaypoint).eulerAngles.y
                : player.transform.eulerAngles.y;

            player.Teleport(at, yaw);
            director.Refresh();
        }

        /// <summary>
        /// What Y to stand at. The ground is SAMPLED first — the same runtime probe the crowd uses,
        /// which already knows that the lowest hit is the plate and that the lowest hit can still be
        /// a roof — and the config's own number is the fallback when nothing is under the point.
        ///
        /// The jetski's fallback is the sea, not the street, which is the web build's distinction
        /// too: <c>config.sea.level</c> there, <c>config.player.spawn.y</c> everywhere else. Land
        /// beside it in the water and swim the last few metres, exactly as the play-test script says.
        /// </summary>
        private float FloorFor(string id, Vector3 waypoint)
        {
            if (id != "jetski" &&
                CrowdGround.TrySample(waypoint, groundMask, up: 60f, down: 120f, out var y))
                return y;

            if (id == "jetski") return _snapshot?.Config?.Sea?.Level ?? 0f;

            var playerSpec = _snapshot?.Config?.Player;
            return playerSpec != null ? playerSpec.Spawn.Y : waypoint.y;
        }
    }
}
