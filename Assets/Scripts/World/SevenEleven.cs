using TheBlock.Audio;
using UnityEngine;

namespace TheBlock.World
{
    /// <summary>
    /// The 7-Eleven: automatic sliding doors, a sales floor you walk onto, and the counter that
    /// opens the shop - the port of <c>src/world/seven-eleven.ts</c>.
    ///
    /// <b>This is not <see cref="Interior"/>.</b> The pizzeria is a room a kilometre from the city
    /// that you teleport into behind a fade; the 7-Eleven is one model whose storefront and sales
    /// floor are the same geometry, so going in is an ordinary walk through a door. Nothing here
    /// fades, teleports, or swaps the fog and ambient palette - borrowing that would darken the
    /// whole street the moment you stepped over the threshold.
    ///
    /// <b>The geometry comes off the model, not out of the config.</b> <c>seven-eleven-lot.glb</c>
    /// carries an empty node for every point this needs - <c>se_door_trigger</c>,
    /// <c>se_cashier_stand</c>, the two door leaves, the ten <c>pu_slot_*</c> fixtures - each holding
    /// the same number <c>config.sevenEleven</c> holds, and glTFast has already converted the whole
    /// hierarchy into Unity's frame on import. Reading a node is exact and free; converting the
    /// config's copy means choosing correctly between three handedness rules for a MODEL-LOCAL
    /// coordinate, which is the trap variant rather than the famous one. <c>WorldBuilder</c> binds
    /// the nodes and asserts they agree with the config to within a centimetre.
    /// </summary>
    public class SevenEleven : MonoBehaviour
    {
        [Header("Model nodes - bound by WorldBuilder")]
        [Tooltip("SevenEleven_DoorL. Note the NAME is from the glTF: after import it is on the right.")]
        [SerializeField] private Transform doorLeft;

        [Tooltip("SevenEleven_DoorR.")]
        [SerializeField] private Transform doorRight;

        [Tooltip("se_door_trigger - the doorway centre the doors open on.")]
        [SerializeField] private Transform entrance;

        [Tooltip("se_cashier_stand - where the shopkeeper stands and where E opens the shop.")]
        [SerializeField] private Transform cashier;

        [Header("Doors - config.sevenEleven.door")]
        [Tooltip("How far each leaf travels apart, metres. MAGNITUDE only - the direction is measured.")]
        [SerializeField] private float slideDistance = 1.14f;

        [Tooltip("The small shared Z shift both leaves take as they part. glTFast leaves Z alone, so " +
                 "this carries with its config sign intact.")]
        [SerializeField] private float slideZ = -0.09f;

        [SerializeField] private float triggerRadius = 3.8f;
        [SerializeField] private float openSec = 0.5f;
        [SerializeField] private float holdSec = 1.2f;

        [Header("Sales floor - config.sevenEleven.floor, already converted to model-local Unity space")]
        [SerializeField] private float floorMinX = -6.78f;
        [SerializeField] private float floorMaxX = 6.78f;
        [SerializeField] private float floorMinZ = -5.28f;
        [SerializeField] private float floorMaxZ = 5.3f;

        [Tooltip("Keeps the roof from counting as inside - the shelves are all under 3.4 m.")]
        [SerializeField] private float ceilingY = 3.4f;

        [SerializeField] private float floorY = -0.5f;

        [Header("Counter")]
        [Tooltip("Metres from the cashier's stand that count as at the counter.")]
        [SerializeField] private float talkRadius = 2.8f;

        [Header("Scene - found automatically when left empty")]
        [SerializeField] private Player.PlayerController player;
        [SerializeField] private Vehicles.VehicleEnterExit vehicles;

        [Tooltip("Where the counter prompt is drawn. Optional; without it E still opens the shop.")]
        [SerializeField] private UI.MissionHud hud;

        // --- run state ------------------------------------------------------------------------------
        // Serialized for the reason VehicleEnterExit's and Interior's are: a recompile during Play
        // reloads the domain but keeps the scene, and doors that forgot their closed pose would
        // re-cache it from wherever they happened to be standing and never shut again.

        [SerializeField, HideInInspector] private Vector3 closedLeft;
        [SerializeField, HideInInspector] private Vector3 closedRight;
        [SerializeField, HideInInspector] private bool posesCached;

        /// <summary>+1 or −1: which way <see cref="doorLeft"/> travels to open. Measured, never assumed.</summary>
        private float _partingSign = 1f;

        private float _open;   // 0 closed → 1 fully open
        private float _hold;   // seconds of "stay open" left after the trigger clears
        private float _applied = -1f;
        private bool _wasInside;

        /// <summary>True while the focus point is on the sales floor.</summary>
        public bool Inside { get; private set; }

        private void Awake()
        {
            if (player == null) player = FindAnyObjectByType<Player.PlayerController>();
            if (vehicles == null) vehicles = FindAnyObjectByType<Vehicles.VehicleEnterExit>();
            if (hud == null) hud = FindAnyObjectByType<UI.MissionHud>();
            CachePoses();
        }

        /// <summary>
        /// Whatever pose the model was authored in IS the closed pose, so it is measured rather than
        /// configured - re-exporting the store with the leaves moved needs no config change.
        ///
        /// The parting direction is measured at the same time, and that is not fussiness: glTFast
        /// negates X on import, so the leaf the glTF calls <c>DoorL</c> arrives on the RIGHT. Taking
        /// the sign from <c>config.door.slide.x</c> would drive both leaves into each other, and the
        /// symptom - a door that shuts harder when you approach - reads as a physics bug.
        /// </summary>
        private void CachePoses()
        {
            if (doorLeft == null || doorRight == null) return;

            if (!posesCached)
            {
                closedLeft = doorLeft.localPosition;
                closedRight = doorRight.localPosition;
                posesCached = true;
            }

            var centreX = (closedLeft.x + closedRight.x) * 0.5f;
            var offset = closedLeft.x - centreX;
            _partingSign = Mathf.Approximately(offset, 0f) ? 1f : Mathf.Sign(offset);
        }

        // --- predicates -----------------------------------------------------------------------------

        /// <summary>
        /// On the sales floor. Model-local, so turning the building turns the room with it and
        /// <c>config.sevenEleven.yaw</c> stays the only place the orientation is stated.
        /// </summary>
        public bool IsInside(Vector3 worldPos)
        {
            var local = transform.InverseTransformPoint(worldPos);
            return local.x >= floorMinX && local.x <= floorMaxX &&
                   local.z >= floorMinZ && local.z <= floorMaxZ &&
                   local.y > floorY && local.y < ceilingY;
        }

        /// <summary>
        /// At the counter. XZ only - the till is at floor level and there is nothing above it.
        ///
        /// <b>The one predicate behind both the prompt and the action</b>, so a prompt that says
        /// "Press E" cannot appear anywhere E does nothing. The web build calls this out as the wart
        /// it deliberately avoided repeating from the vehicle prompt.
        /// </summary>
        public bool NearClerk(Vector3 worldPos)
        {
            if (cashier == null) return false;
            var d = worldPos - cashier.position;
            d.y = 0f;
            return d.sqrMagnitude <= talkRadius * talkRadius;
        }

        /// <summary>On the threshold, either side - what the doors trigger on.</summary>
        public bool NearEntrance(Vector3 worldPos)
        {
            if (entrance == null) return false;
            var d = worldPos - entrance.position;
            d.y = 0f;
            return d.sqrMagnitude <= triggerRadius * triggerRadius;
        }

        /// <summary>
        /// Can the player shop right now? On foot, on the floor, at the counter. Driving in opens the
        /// doors and counts as inside - it does not let you shop through a windscreen.
        /// </summary>
        public bool CanShop()
        {
            if (player == null) return false;
            if (vehicles != null && vehicles.Mode != Core.GameMode.OnFoot) return false;
            var at = player.transform.position;
            return IsInside(at) && NearClerk(at);
        }

        // --- the frame ------------------------------------------------------------------------------

        private void Update()
        {
            if (Core.Pause.Frozen) return;

            var focus = FocusPoint();

            // The threshold edge, tested on the same focus point the doors use, so the chime fires
            // once whether you walk in or drive in and re-arms only after you have left.
            Inside = IsInside(focus);
            if (Inside && !_wasInside) GameAudio.Cue(SfxCue.StoreChime);
            _wasInside = Inside;

            DriveDoors(focus, Time.deltaTime);

            // Drawn from the same predicate GameFlow acts on, at the doorway's own priority - a
            // prompt that offers a key which does nothing is the fault this shares its priority
            // band with Interior's to avoid.
            if (CanShop()) hud?.SetPrompt("Press E to shop", UI.MissionHud.PromptDoor);
        }

        /// <summary>
        /// One point stands for the player whether they are walking or driving, exactly as the web
        /// build's <c>(fx, fz)</c> does - a car nosing onto the forecourt has to open the doors too.
        /// </summary>
        private Vector3 FocusPoint()
        {
            if (vehicles != null && vehicles.Mode != Core.GameMode.OnFoot)
            {
                var active = vehicles.ActiveVehicle;
                // `as`-cast interfaces do not carry Unity's fake-null through, so the anchor is
                // checked rather than the interface reference.
                if (active != null && active.Anchor != null) return active.Anchor.position;
            }

            return player != null ? player.transform.position : transform.position;
        }

        private void DriveDoors(Vector3 focus, float dt)
        {
            if (doorLeft == null || doorRight == null) return;
            if (!posesCached) CachePoses();

            _hold = NearEntrance(focus) ? holdSec : Mathf.Max(0f, _hold - dt);

            var step = dt / Mathf.Max(0.01f, openSec);
            _open = _hold > 0f ? Mathf.Min(1f, _open + step) : Mathf.Max(0f, _open - step);

            // Smoothstep, so the leaves ease off both ends instead of starting and stopping dead.
            var e = _open * _open * (3f - 2f * _open);
            if (Mathf.Abs(e - _applied) < 0.001f) return; // idle: do not dirty two transforms a frame
            _applied = e;

            var travel = slideDistance * e * _partingSign;
            doorLeft.localPosition = closedLeft + new Vector3(travel, 0f, slideZ * e);
            doorRight.localPosition = closedRight + new Vector3(-travel, 0f, slideZ * e);
        }

        /// <summary>Writes everything <c>WorldBuilder</c> resolves off the model and the config.</summary>
        public void Configure(
            Transform left, Transform right, Transform doorTrigger, Transform stand,
            Core.TheBlockConfig.StoreSpec spec, float minX, float maxX, float minZ, float maxZ)
        {
            doorLeft = left;
            doorRight = right;
            entrance = doorTrigger;
            cashier = stand;

            floorMinX = minX;
            floorMaxX = maxX;
            floorMinZ = minZ;
            floorMaxZ = maxZ;

            if (spec?.Door != null)
            {
                slideDistance = Mathf.Abs(spec.Door.Slide.X);
                slideZ = spec.Door.Slide.Z;
                triggerRadius = spec.Door.TriggerRadius;
                openSec = spec.Door.OpenSec;
                holdSec = spec.Door.HoldSec;
            }

            if (spec?.Clerk != null) talkRadius = spec.Clerk.TalkRadius;

            posesCached = false;
            CachePoses();
        }
    }
}
