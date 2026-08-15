using TheBlock.Core;
using TheBlock.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// Getting in and out of a vehicle — the port of the <c>onFoot → entering → driving → exiting</c>
    /// loop that lives in <c>src/main.ts</c> and <c>src/game/transitions.ts</c>.
    ///
    /// It replaces U8's <c>DebugVehicleSwitch</c>, and keeps the one thing that was worth keeping:
    /// the shape of the swap. Switch off the controller you are leaving, switch on the one you are
    /// taking, and point the camera at the new <see cref="IChaseTarget"/>. Everything else here is
    /// what that scaffolding faked — a proximity test, a door that swings, a driver who is actually
    /// visible in the seat, and two frozen states so nothing can fire mid-teleport.
    ///
    /// There are two ways in, and the web build has both. A vehicle that wants the ENTRY ANIMATION
    /// gets it: the driver walks up, opens the door and sits, and the clip's own progress drives the
    /// door. Everything else gets the QUICK mount, which is not a placeholder — it is what every
    /// untuned car and every door-less vehicle in the game uses. U10 added no third path; it added
    /// two flags on <see cref="IEnterable"/> so the quick mount can also seat a rider who stays
    /// visible, which is what a motorcycle needs.
    ///
    /// Unity-idiomatic difference worth knowing: the web build hides the walking player and mounts a
    /// SECOND skinned body into the vehicle, because three.js had no cheap way to hand one skeleton
    /// between two animation graphs. Here it is the same Joe throughout — parented to the vehicle's
    /// rider anchor, with his controller switched off. One body, one animator, and the character
    /// roster (U29) reaches the seat for free.
    /// </summary>
    public class VehicleEnterExit : MonoBehaviour
    {
        [Header("Scene — found automatically when left empty")]
        [SerializeField] private PlayerController player;
        [SerializeField] private PlayerAnimator playerAnimator;
        [SerializeField] private FollowCamera followCamera;

        [Header("Exit placement — Unity-side, not in config.ts")]
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
        // SCENE survives — so Joe would stay parented inside the car with his controller off while
        // this machine came back believing it was on foot. That is an unrecoverable soft lock.
        // Unity preserves serialized fields across the reload, so the machine wakes up where it was.

        [SerializeField, HideInInspector] private GameMode mode = GameMode.OnFoot;

        // As a MonoBehaviour, not as an IEnterable. Unity's serializer cannot write an interface
        // field at all — it silently stores nothing — so the guard above was quietly doing nothing
        // for the vehicle reference itself, which is the one piece of state that cannot be
        // recovered by looking at the scene.
        [SerializeField, HideInInspector] private MonoBehaviour activeVehicleObject;

        [SerializeField, HideInInspector] private float timer;
        [SerializeField, HideInInspector] private bool usingEntryClip;
        [SerializeField, HideInInspector] private bool riderSeated;

        private TheBlockConfig.VehicleSpec _spec;
        private CharacterController _capsule;
        private Renderer[] _driverRenderers;

        /// <summary>What the run is doing. U25's HUD and U20's mission framework both read this.</summary>
        public GameMode Mode => mode;

        /// <summary>The vehicle being entered, driven or left, or null while on foot.</summary>
        public IEnterable ActiveVehicle
        {
            get => activeVehicleObject as IEnterable;
            private set => activeVehicleObject = value as MonoBehaviour;
        }

        private void Awake() => Bind();

        /// <summary>
        /// Resolves the scene references and the config. Called from Awake, and again from Update if
        /// the spec has gone null after a mid-Play recompile — the same guard PlayerController and
        /// CarController carry.
        /// </summary>
        private void Bind()
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (followCamera == null) followCamera = FindAnyObjectByType<FollowCamera>();
            if (playerAnimator == null && player != null)
                playerAnimator = player.GetComponent<PlayerAnimator>();

            if (player == null)
            {
                Debug.LogError("VehicleEnterExit: no PlayerController in the scene.", this);
                enabled = false;
                return;
            }

            player.TryGetComponent(out _capsule);
            _driverRenderers = player.GetComponentsInChildren<Renderer>(true);

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
            if (_spec == null) Bind();
            if (_spec == null) return;

            var dt = Time.deltaTime;
            var keyboard = Keyboard.current;
            var pressedE = keyboard != null && keyboard.eKey.wasPressedThisFrame;
            var pressedR = keyboard != null && keyboard.rKey.wasPressedThisFrame;

            switch (mode)
            {
                case GameMode.OnFoot:
                    if (pressedE) TryEnter();
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
            }
        }

        // --- entering ----------------------------------------------------------------------------

        private void TryEnter()
        {
            var vehicle = Nearest();
            if (vehicle == null || !vehicle.TryEnter()) return;

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
                // The anchor is where the clip STARTS — beside the door, at road level. Its own
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
        /// animation, so nothing to wait on except the door — and a bike has no door, so it waits
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
        /// <c>worldPositionStays: false</c> is the whole point — the anchor carries the config's
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
            // out would bake that into Joe's own localScale — he would walk away permanently 5%
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
        /// skipped — otherwise the first thing the ray finds is the chassis box it just left.
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

        /// <summary>
        /// The nearest enterable vehicle within <c>enterRadius</c>, measured on the ground plane so
        /// standing on a kerb beside one still counts.
        ///
        /// The bike spawns 8 m from the Mustang, both on the lot, so this genuinely has to choose —
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
        private void SetDriverVisible(bool visible)
        {
            if (_driverRenderers == null) return;
            foreach (var renderer in _driverRenderers)
                if (renderer != null)
                    renderer.enabled = visible;
        }
    }
}
