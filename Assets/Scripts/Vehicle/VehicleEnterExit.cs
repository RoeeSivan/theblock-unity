using TheBlock.Core;
using TheBlock.Player;
using TheBlock.Traffic;
using TheBlock.UI;
using TheBlock.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// Getting in and out of a vehicle - the port of the <c>onFoot → entering → driving → exiting</c>
    /// loop that lives in <c>src/main.ts</c> and <c>src/game/transitions.ts</c>.
    ///
    /// It replaces U8's <c>DebugVehicleSwitch</c>, and keeps the one thing that was worth keeping:
    /// the shape of the swap. Switch off the controller you are leaving, switch on the one you are
    /// taking, and point the camera at the new <see cref="IChaseTarget"/>. Everything else here is
    /// what that scaffolding faked - a proximity test, a door that swings, a driver who is actually
    /// visible in the seat, and two frozen states so nothing can fire mid-teleport.
    ///
    /// There are two ways in, and the web build has both. A vehicle that wants the ENTRY ANIMATION
    /// gets it: the driver walks up, opens the door and sits, and the clip's own progress drives the
    /// door. Everything else gets the QUICK mount, which is not a placeholder - it is what every
    /// untuned car and every door-less vehicle in the game uses. U10 added no third path; it added
    /// two flags on <see cref="IEnterable"/> so the quick mount can also seat a rider who stays
    /// visible, which is what a motorcycle needs.
    ///
    /// Unity-idiomatic difference worth knowing: the web build hides the walking player and mounts a
    /// SECOND skinned body into the vehicle, because three.js had no cheap way to hand one skeleton
    /// between two animation graphs. Here it is the same Joe throughout - parented to the vehicle's
    /// rider anchor, with his controller switched off. One body, one animator, and the character
    /// roster (U29) reaches the seat for free.
    /// </summary>
    public class VehicleEnterExit : MonoBehaviour
    {
        [Header("Scene - found automatically when left empty")]
        [SerializeField] private PlayerController player;
        [SerializeField] private PlayerAnimator playerAnimator;
        [SerializeField] private FollowCamera followCamera;

        [Tooltip("Where a promoted lot car or a stolen street car comes from. Optional: without it, " +
                 "E still enters real vehicles and simply cannot take anything else.")]
        [SerializeField] private CarSpawner cars;

        [Tooltip("The ambient traffic, for the carjack. Optional in the same way.")]
        [SerializeField] private TrafficSystem traffic;

        [Tooltip("Where 'Press E to enter' is drawn. Optional: without it E still works, silently.")]
        [SerializeField] private MissionHud hud;

        [Tooltip("U35a's ragdoll, so E is ignored while the player is on the tarmac. Optional.")]
        [SerializeField] private PlayerRagdoll ragdoll;

        [Header("Exit placement - Unity-side, not in config.ts")]
        [Tooltip("How far above the vehicle the ground probe starts. Must clear the roof.")]
        [SerializeField] private float exitProbeHeight = 5f;

        [Tooltip("How far down the probe looks from there. A rooftop heli exit (U23) needs the reach.")]
        [SerializeField] private float exitProbeDepth = 25f;

        [Header("Quick mount")]
        [Tooltip("Seconds of frozen input to get onto something with no door. config's " +
                 "enterDoorOpenTime + enterDoorCloseDelay is 1.05 s of waiting for a door to swing, " +
                 "which a motorcycle does not have and should not pay for.")]
        [SerializeField] private float doorlessMountSeconds = 0.35f;

        // --- run state -----------------------------------------------------------------------
        // Serialized on purpose, and NOT because anyone edits it. A script recompile while the
        // Editor is in Play mode reloads the domain and clears every non-serialized field, but the
        // SCENE survives - so Joe would stay parented inside the car with his controller off while
        // this machine came back believing it was on foot. That is an unrecoverable soft lock.
        // Unity preserves serialized fields across the reload, so the machine wakes up where it was.

        [SerializeField, HideInInspector] private GameMode mode = GameMode.OnFoot;

        // As a MonoBehaviour, not as an IEnterable. Unity's serializer cannot write an interface
        // field at all - it silently stores nothing - so the guard above was quietly doing nothing
        // for the vehicle reference itself, which is the one piece of state that cannot be
        // recovered by looking at the scene.
        [SerializeField, HideInInspector] private MonoBehaviour activeVehicleObject;

        [SerializeField, HideInInspector] private float timer;
        [SerializeField, HideInInspector] private bool usingEntryClip;
        [SerializeField, HideInInspector] private bool riderSeated;

        [SerializeField] private TheBlock.Player.CharacterBody playerBody;

        private TheBlockConfig.VehicleSpec _spec;
        private CharacterController _capsule;
        private Renderer[] _driverRenderers;

        /// <summary>What the run is doing. U25's HUD and U20's mission framework both read this.</summary>
        public GameMode Mode => mode;

        /// <summary>
        /// True when pressing E right now would get the player into something - a real vehicle, a
        /// promotable parked filler, or a stealable street car.
        ///
        /// E is shared with the pizzeria doorway, and a car parked outside the storefront puts both
        /// in range at once. This is how the doorway defers rather than the two racing on Update
        /// order - the vehicle wins, which is the web build's precedence too, and all three car
        /// sources outrank the door there for the same reason.
        /// </summary>
        public bool HasVehicleInReach => InReach(NearestStopped());

        /// <summary>
        /// The same test against a stopped car the caller has ALREADY found. Update has one in hand
        /// every frame (it has to, to hold it), and asking the traffic system for it a second time
        /// to draw a prompt is a sweep of the live pool for an answer we are holding.
        /// </summary>
        private bool InReach(TrafficCar stopped) =>
            _spec != null && mode == GameMode.OnFoot &&
            (Nearest() != null || NearestFiller() != null || stopped != null);

        /// <summary>The vehicle being entered, driven or left, or null while on foot.</summary>
        public IEnterable ActiveVehicle
        {
            get => activeVehicleObject as IEnterable;
            private set => activeVehicleObject = value as MonoBehaviour;
        }

        private void Awake() => Bind();

        private void OnDestroy()
        {
            if (playerBody != null) playerBody.Swapped -= OnDriverBodySwapped;
        }

        /// <summary>
        /// Resolves the scene references and the config. Called from Awake, and again from Update if
        /// the spec has gone null after a mid-Play recompile - the same guard PlayerController and
        /// CarController carry.
        /// </summary>
        private void Bind()
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (followCamera == null) followCamera = FindAnyObjectByType<FollowCamera>();
            if (playerAnimator == null && player != null)
                playerAnimator = player.GetComponent<PlayerAnimator>();
            if (cars == null) cars = FindAnyObjectByType<CarSpawner>();
            if (traffic == null) traffic = FindAnyObjectByType<TrafficSystem>();
            if (hud == null) hud = FindAnyObjectByType<MissionHud>();
            if (ragdoll == null) ragdoll = FindAnyObjectByType<PlayerRagdoll>();

            if (player == null)
            {
                Debug.LogError("VehicleEnterExit: no PlayerController in the scene.", this);
                enabled = false;
                return;
            }

            player.TryGetComponent(out _capsule);

            // U29: the driver's renderers belong to a body a menu can replace mid-drive, so the
            // cache has to be invalidated rather than taken once. A stale array leaves the OLD
            // body's renderers being switched - which reads as a driver visible through the
            // windscreen of a car that is meant to look empty, and the new body never hidden.
            if (playerBody == null && player != null)
                playerBody = player.GetComponent<TheBlock.Player.CharacterBody>();
            if (playerBody != null)
            {
                playerBody.Swapped -= OnDriverBodySwapped;
                playerBody.Swapped += OnDriverBodySwapped;
            }

            CacheDriverRenderers();

            var snapshot = TheBlockConfig.Load();
            if (snapshot?.Config?.Vehicle == null)
            {
                Debug.LogError("VehicleEnterExit: config has no `vehicle` section.", this);
                enabled = false;
                return;
            }

            _spec = snapshot.Config.Vehicle;
        }

        private void Update()
        {
            // Frozen: no E, and the door timer does not advance either - a pause taken mid-swing
            // resumes where it left off rather than finishing behind the menu. See Core.Pause.
            if (Core.Pause.Frozen) return;

            if (_spec == null) Bind();
            if (_spec == null) return;

            var dt = Time.deltaTime;
            var keyboard = Keyboard.current;
            var pressedE = keyboard != null && keyboard.eKey.wasPressedThisFrame;
            var pressedR = keyboard != null && keyboard.rKey.wasPressedThisFrame;

            switch (mode)
            {
                case GameMode.OnFoot:
                    // Down and being simulated is on-foot as far as this machine is concerned, and it
                    // must not be: the player's root transform is parked where the fall STARTED while
                    // the body slides somewhere else entirely, so a proximity test here answers about
                    // a place nobody is standing. Boarding from it would parent a corpse to a seat.
                    if (ragdoll != null && ragdoll.Down) break;

                    // Every frame, not only on the frame E is pressed. A stopped car within reach
                    // is frozen for `hijack.holdSec` so it waits while you walk over - otherwise its
                    // light goes green mid-approach and the car you were heading for drives off.
                    var stopped = NearestStopped();
                    if (stopped != null) traffic.Hold(stopped);

                    // One predicate behind both the prompt and the action, which is the arrangement
                    // the web build settled on after its cashier offered a key that did nothing.
                    // A vehicle you can see the prompt for is one E will get you into - and one that
                    // would refuse says WHY instead, in the same line, from its own EntryRefusal.
                    var refusal = Nearest()?.EntryRefusal;
                    if (refusal != null) hud?.SetPrompt(refusal, MissionHud.PromptVehicle);
                    else if (InReach(stopped)) hud?.SetPrompt("Press E to enter", MissionHud.PromptVehicle);

                    if (pressedE) TryEnter(stopped);
                    break;

                case GameMode.Entering:
                    timer += dt;
                    if (usingEntryClip) TickEnterClip();
                    else TickQuickMount();
                    break;

                case GameMode.Driving:
                    // R first: pressing both in one frame should put the vehicle back, not step off
                    // it and leave it wherever it got stuck.
                    if (pressedR) ActiveVehicle?.Respawn();
                    else if (pressedE) BeginExit();
                    break;

                case GameMode.Exiting:
                    timer += dt;
                    if (timer >= _spec.ExitDoorCloseTime) FinishExit();
                    break;

                case GameMode.Rhythm:
                    // A modal minigame owns the screen and the keyboard. Nothing here runs - no E,
                    // no R, and no stopped-car hold, which would otherwise keep freezing traffic
                    // beside a player who is not standing there any more.
                    break;
            }
        }

        /// <summary>
        /// Parks this machine while a modal minigame owns the player (U22's dance).
        ///
        /// <b>Only from <see cref="GameMode.OnFoot"/></b>, and that guard is the point: a dance
        /// cannot start out of a car door swing, and suspending mid-<see cref="GameMode.Entering"/>
        /// would strand a driver half-seated with the clip still running.
        /// </summary>
        public bool EnterModal()
        {
            if (mode != GameMode.OnFoot) return false;
            mode = GameMode.Rhythm;
            return true;
        }

        /// <summary>Hands control back. Idempotent, so an exit path may call it more than once.</summary>
        public void ExitModal()
        {
            if (mode == GameMode.Rhythm) mode = GameMode.OnFoot;
        }

        /// <summary>
        /// Steps off immediately, skipping the door-close wait - the same two halves the E exit runs,
        /// back to back.
        ///
        /// For a scripted exit rather than a player's: U21's retry has to have the rider on foot
        /// before it can put him in the pizzeria, and teleporting a player who is still parented to
        /// a seat leaves him being dragged around by a vehicle he is no longer driving.
        /// </summary>
        public void LeaveVehicleNow()
        {
            if (mode != GameMode.Driving) return;
            BeginExit();
            FinishExit();
        }

        // --- entering ----------------------------------------------------------------------------

        /// <summary>
        /// What E does on foot, in the web build's own order: get into a real vehicle, else promote
        /// the parked filler you are standing beside, else steal the stopped street car.
        ///
        /// <b>The order is the design, not an implementation detail.</b> A real vehicle always wins,
        /// so parking your own car among the fillers never leaves you promoting a stranger's; and the
        /// lot beats the street because the two can only ever be in reach together at the lot's edge,
        /// where the parked car is what you are obviously standing next to. <c>main.ts</c> resolves
        /// it identically, one line at a time, and the pizzeria door comes after all three.
        /// </summary>
        private void TryEnter(TrafficCar stopped)
        {
            // Written out rather than chained with `??`, deliberately: these are Unity objects, and
            // `??` does not see the overloaded null (memory: unity-null-coalescing-fake-null).
            var vehicle = Nearest();
            if (vehicle == null) vehicle = PromoteFiller();
            if (vehicle == null) vehicle = Hijack(stopped);

            Board(vehicle);
        }

        /// <summary>
        /// Gets the player onto a vehicle the CALLER has chosen, skipping the proximity search - the
        /// mirror of <see cref="LeaveVehicleNow"/>, and everything <see cref="TryEnter"/> does once it
        /// has decided what you are getting into.
        ///
        /// U35a's remount is what needs it: a rider thrown off the bike stands up with their own bike
        /// laid at their feet, and the one thing that must not choose what they get back onto is
        /// whatever else happens to be parked inside <c>enterRadius</c> - which on the lot is a
        /// Mustang eight metres away.
        /// </summary>
        /// <returns>False if the mode is wrong or the vehicle refused.</returns>
        public bool Board(IEnterable vehicle)
        {
            if (vehicle == null || mode != GameMode.OnFoot) return false;
            if (!vehicle.TryEnter()) return false;

            ActiveVehicle = vehicle;
            mode = GameMode.Entering;
            timer = 0f;

            // The capsule stops being driven the moment the controller goes off, so leaving it
            // solid would park an invisible obstacle wherever E was pressed. Harmless on an empty
            // lot; a permanent phantom in a traffic lane once U17 lands.
            player.enabled = false;
            if (_capsule != null) _capsule.enabled = false;

            var seconds = playerAnimator == null ? 0f : playerAnimator.EnterCarSeconds;
            usingEntryClip = vehicle.UsesEntryAnimation && vehicle.RiderAnchor != null && seconds > 0f;

            if (usingEntryClip)
            {
                // The anchor is where the clip STARTS - beside the door, at road level. Its own
                // baked hip travel is what carries Joe from there into the seat, so his transform
                // never has to move again until he gets out.
                SetDriverVisible(true);
                Mount(vehicle.RiderAnchor);
                playerAnimator.SeatIn();
            }
            else
            {
                riderSeated = false;
                if (vehicle.Door != null) vehicle.Door.Open();
            }

            followCamera?.Follow(vehicle, snap: false);
            return true;
        }

        /// <summary>
        /// The tuned path. Clip progress drives the door: it swings open a quarter of the way in and
        /// starts shutting at seven tenths, both from <c>config.vehicle.driver</c>, so the swing is
        /// tied to what the animation is doing rather than to a stopwatch that would drift out of
        /// step the moment the clip is re-exported.
        /// </summary>
        private void TickEnterClip()
        {
            var vehicle = ActiveVehicle;
            var driver = _spec.Driver;
            var progress = Mathf.Clamp01(timer / playerAnimator.EnterCarSeconds);

            if (vehicle.Door != null)
            {
                var openAt = driver?.DoorOpenAt ?? 0.25f;
                var closeAt = driver?.DoorCloseAt ?? 0.7f;
                vehicle.Door.SetOpen(progress >= openAt && progress < closeAt);
            }

            if (progress >= 1f) BeginDriving();
        }

        /// <summary>
        /// The other path, and what every untuned car and every door-less vehicle uses: no
        /// animation, so nothing to wait on except the door - and a bike has no door, so it waits
        /// only <see cref="doorlessMountSeconds"/> rather than the 1.05 s a door swing costs.
        ///
        /// The rider is either hidden (a car: the cabin looks empty from outside, as the web build's
        /// untuned cars do) or left visible on the seat holding the driving pose (a bike).
        /// </summary>
        private void TickQuickMount()
        {
            var vehicle = ActiveVehicle;
            var hasDoor = vehicle.Door != null;
            var seatAt = hasDoor ? _spec.EnterDoorOpenTime : 0f;
            var doneAt = seatAt + (hasDoor ? _spec.EnterDoorCloseDelay : doorlessMountSeconds);

            if (!riderSeated && timer >= seatAt)
            {
                riderSeated = true;
                Mount(vehicle.RiderAnchor != null ? vehicle.RiderAnchor : vehicle.GetTransform());

                if (vehicle.ShowRiderOnQuickMount)
                {
                    SetDriverVisible(true);
                    playerAnimator?.RideOn();
                }
                else SetDriverVisible(false);
            }

            if (timer >= doneAt) BeginDriving();
        }

        /// <summary>
        /// Parents Joe to a seat and drops him onto it exactly.
        ///
        /// <c>worldPositionStays: false</c> is the whole point - the anchor carries the config's
        /// rider scale and yaw, and preserving the world transform would throw both away.
        /// </summary>
        private void Mount(Transform seat)
        {
            player.transform.SetParent(seat, worldPositionStays: false);
            player.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        private void BeginDriving()
        {
            mode = GameMode.Driving;
            timer = 0f;
            if (ActiveVehicle.Door != null) ActiveVehicle.Door.Close();
            ActiveVehicle.Driven = true;
        }

        // --- exiting -----------------------------------------------------------------------------

        /// <summary>
        /// Steps out immediately and freezes for <c>exitDoorCloseTime</c> while the door shuts. The
        /// player is put down before the wait, not after, so the door is seen closing behind a body
        /// that is already standing there.
        /// </summary>
        private void BeginExit()
        {
            mode = GameMode.Exiting;
            timer = 0f;

            var vehicle = ActiveVehicle;
            vehicle.Driven = false;
            vehicle.Exit();
            if (vehicle.Door != null) vehicle.Door.Open();

            // worldPositionStays MUST be false. The seat anchor carries the config's rider scale
            // (0.95 on the Mustang, 1.1 on the bike), and preserving the world transform on the way
            // out would bake that into Joe's own localScale - he would walk away permanently 5%
            // shorter, a little more so with every vehicle he got out of.
            player.transform.SetParent(null, worldPositionStays: false);
            player.transform.SetPositionAndRotation(ExitSpot(vehicle), vehicle.GetTransform().rotation);

            SetDriverVisible(true);
            riderSeated = false;
            playerAnimator?.SeatOut();
            followCamera?.FollowPlayer(snap: false);
        }

        /// <summary>
        /// Beside the driver's door, on whatever surface is actually under that spot.
        ///
        /// The probe matters more than it looks: hard-coding the road height drops anyone stepping
        /// out of a car parked on lot asphalt through the tarmac, and would put U23's helicopter
        /// pilot at street level the moment he lands on a roof. The vehicle's own colliders are
        /// skipped - otherwise the first thing the ray finds is the chassis box it just left.
        /// </summary>
        private Vector3 ExitSpot(IEnterable vehicle)
        {
            var anchor = vehicle.GetTransform();
            var beside = anchor.position + vehicle.DriverSide * _spec.ExitSideOffset;
            var from = new Vector3(beside.x, anchor.position.y + exitProbeHeight, beside.z);

            var best = float.NegativeInfinity;
            foreach (var hit in Physics.RaycastAll(
                         from, Vector3.down, exitProbeDepth, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(anchor)) continue;
                if (hit.point.y > best) best = hit.point.y;
            }

            return new Vector3(
                beside.x,
                float.IsNegativeInfinity(best) ? anchor.position.y : best,
                beside.z);
        }

        private void FinishExit()
        {
            var vehicle = ActiveVehicle;
            if (vehicle?.Door != null) vehicle.Door.Close();

            ActiveVehicle = null;
            mode = GameMode.OnFoot;
            timer = 0f;

            if (_capsule != null) _capsule.enabled = true;
            player.enabled = true;
        }

        // --- helpers -----------------------------------------------------------------------------

        // --- promotion: turning something that is not a vehicle into one -------------------------

        /// <summary>
        /// Swaps the parked filler you are standing beside for a real drivable car - U13's deferred
        /// lot-car promotion, and the port of <c>transitions.ts promoteLotCar</c>.
        ///
        /// Same model, same colour, same stall, same heading, and then you get in. The filler is
        /// destroyed only once the replacement exists: a failed spawn must leave the lot as it was
        /// rather than eating the car you pressed E at.
        /// </summary>
        private IEnterable PromoteFiller()
        {
            var filler = NearestFiller();
            if (filler == null) return null;

            var taken = cars.Take(
                filler.ModelName, filler.ContactPatch(), filler.DriveRotation, filler.Paint);
            if (taken == null) return null;

            Destroy(filler.gameObject);
            return taken;
        }

        /// <summary>
        /// Steals the stopped street car - the port of <c>transitions.ts hijackTrafficCar</c>.
        ///
        /// The claim comes first and it retires the sim car, so for one instant the lane is empty;
        /// the drivable copy is then put at the pose the claim recorded. Doing it the other way round
        /// would spawn a car inside the one it replaces and hand PhysX two overlapping boxes to
        /// resolve - which it does by throwing them apart.
        /// </summary>
        private IEnterable Hijack(TrafficCar stopped)
        {
            if (stopped == null || traffic == null || cars == null) return null;
            if (cars.PrefabFor(stopped.ModelName) == null)
            {
                // Checked BEFORE the claim. Claiming first would retire a car that then cannot be
                // replaced, and the street would simply lose it.
                Debug.LogWarning(
                    $"VehicleEnterExit: no drivable prefab for '{stopped.ModelName}', so this car " +
                    "cannot be taken. Run The Block → Build Drivable Cars.", this);
                return null;
            }

            var claim = traffic.Claim(stopped);
            return cars.Take(claim.ModelName, claim.Position, claim.Rotation, claim.Paint);
        }

        /// <summary>The parked filler in reach, or null. Null whenever there is nothing to spawn with.</summary>
        private LotCar NearestFiller() =>
            cars == null ? null : LotCar.Nearest(player.transform.position, _spec.EnterRadius);

        /// <summary>The stealable street car in reach, or null.</summary>
        private TrafficCar NearestStopped() =>
            traffic == null || cars == null
                ? null
                : traffic.NearestStopped(player.transform.position, _spec.EnterRadius);

        /// <summary>
        /// The nearest enterable vehicle within <c>enterRadius</c>, measured on the ground plane so
        /// standing on a kerb beside one still counts.
        ///
        /// The bike spawns 8 m from the Mustang, both on the lot, so this genuinely has to choose -
        /// which is why it walks <see cref="EnterableRegistry"/> rather than the car spawner's list.
        /// </summary>
        private IEnterable Nearest()
        {
            IEnterable best = null;
            var bestDistance = _spec.EnterRadius * _spec.EnterRadius;
            var here = player.transform.position;

            foreach (var vehicle in EnterableRegistry.All)
            {
                var offset = vehicle.GetTransform().position - here;
                var distance = offset.x * offset.x + offset.z * offset.z;
                if (distance > bestDistance) continue;
                bestDistance = distance;
                best = vehicle;
            }

            return best;
        }

        /// <summary>
        /// Hides the body without deactivating it. The GameObject has to stay alive: its Animator is
        /// what plays the entry clip, and a disabled object animates nothing.
        /// </summary>
        private void CacheDriverRenderers() =>
            _driverRenderers = player == null ? null : player.GetComponentsInChildren<Renderer>(true);

        /// <summary>
        /// A fresh body starts visible, so the hidden-driver state has to be re-imposed on it. The
        /// only mode where the driver is hidden is a quick mount into something that does not show
        /// its rider - a car - which is exactly the case the stale cache used to break.
        /// </summary>
        private void OnDriverBodySwapped()
        {
            CacheDriverRenderers();

            bool hidden = riderSeated && mode != GameMode.OnFoot &&
                          ActiveVehicle != null && !ActiveVehicle.ShowRiderOnQuickMount;
            SetDriverVisible(!hidden);
        }

        private void SetDriverVisible(bool visible)
        {
            if (_driverRenderers == null) return;
            foreach (var renderer in _driverRenderers)
                if (renderer != null)
                    renderer.enabled = visible;
        }
    }
}
