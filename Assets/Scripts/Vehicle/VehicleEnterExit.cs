using TheBlock.Core;
using TheBlock.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// Getting in and out of a car — the port of the <c>onFoot → entering → driving → exiting</c>
    /// loop that lives in <c>src/main.ts</c> and <c>src/game/transitions.ts</c>.
    ///
    /// It replaces U8's <c>DebugVehicleSwitch</c>, and keeps the one thing that was worth keeping:
    /// the shape of the swap. Switch off the controller you are leaving, switch on the one you are
    /// taking, and point the camera at the new <see cref="IChaseTarget"/>. Everything else here is
    /// what that scaffolding faked — a proximity test, a door that swings, a driver who is actually
    /// visible in the seat, and two frozen states so nothing can fire mid-teleport.
    ///
    /// There are two ways in, and the web build has both. A car with a seat block in
    /// <c>config.vehicle.driver.seats</c> plays the entry ANIMATION: the driver walks up, opens the
    /// door and sits, and the clip's own progress drives the door. Any other car — and this one, if
    /// the clip has not been imported — gets the QUICK enter: the door swings for
    /// <c>enterDoorOpenTime</c>, the driver vanishes, and <c>enterDoorCloseDelay</c> later it shuts.
    /// The fallback is not a placeholder; it is the path every untuned car in the game uses.
    ///
    /// Unity-idiomatic difference worth knowing: the web build hides the walking player and mounts a
    /// SECOND skinned body into the car, because three.js had no cheap way to hand one skeleton
    /// between two animation graphs. Here it is the same Joe throughout — parented to the car's
    /// driver anchor, with his controller switched off. One body, one animator, and the character
    /// roster (U29) reaches the seat for free.
    /// </summary>
    public class VehicleEnterExit : MonoBehaviour
    {
        [Header("Scene — found automatically when left empty")]
        [SerializeField] private PlayerController player;
        [SerializeField] private PlayerAnimator playerAnimator;
        [SerializeField] private FollowCamera followCamera;
        [SerializeField] private CarSpawner spawner;

        [Header("Exit placement — Unity-side, not in config.ts")]
        [Tooltip("How far above the car the ground probe starts. Must clear the roof.")]
        [SerializeField] private float exitProbeHeight = 5f;

        [Tooltip("How far down the probe looks from there. A rooftop heli exit (U23) needs the reach.")]
        [SerializeField] private float exitProbeDepth = 25f;

        // --- run state -----------------------------------------------------------------------
        // Serialized on purpose, and NOT because anyone edits it. A script recompile while the
        // Editor is in Play mode reloads the domain and clears every non-serialized field, but the
        // SCENE survives — so Joe would stay parented inside the car with his controller off while
        // this machine came back believing it was on foot. That is an unrecoverable soft lock.
        // Unity preserves serialized fields across the reload, so the machine wakes up where it was.

        [SerializeField, HideInInspector] private GameMode mode = GameMode.OnFoot;
        [SerializeField, HideInInspector] private CarController activeVehicle;
        [SerializeField, HideInInspector] private float timer;
        [SerializeField, HideInInspector] private bool usingEntryClip;
        [SerializeField, HideInInspector] private bool driverHidden;

        private TheBlockConfig.VehicleSpec _spec;
        private CharacterController _capsule;
        private Renderer[] _driverRenderers;

        /// <summary>What the run is doing. U25's HUD and U20's mission framework both read this.</summary>
        public GameMode Mode => mode;

        /// <summary>The car being entered, driven or left, or null while on foot.</summary>
        public CarController ActiveVehicle => activeVehicle;

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
            if (spawner == null) spawner = FindAnyObjectByType<CarSpawner>();
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
            var pressedE = Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;

            switch (mode)
            {
                case GameMode.OnFoot:
                    if (pressedE) TryEnter();
                    break;

                case GameMode.Entering:
                    timer += dt;
                    if (usingEntryClip) TickEnterClip();
                    else TickEnterQuick();
                    break;

                case GameMode.Driving:
                    if (pressedE) BeginExit();
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
            var car = Nearest();
            if (car == null) return;

            activeVehicle = car;
            mode = GameMode.Entering;
            timer = 0f;

            // The capsule stops being driven the moment the controller goes off, so leaving it
            // solid would park an invisible obstacle wherever E was pressed. Harmless on an empty
            // lot; a permanent phantom in a traffic lane once U17 lands.
            player.enabled = false;
            if (_capsule != null) _capsule.enabled = false;

            var seconds = playerAnimator == null ? 0f : playerAnimator.EnterCarSeconds;
            usingEntryClip = car.DriverAnchor != null && seconds > 0f;

            if (usingEntryClip)
            {
                // The anchor is where the clip STARTS — beside the door, at road level. Its own
                // baked hip travel is what carries Joe from there into the seat, so his transform
                // never has to move again until he gets out.
                SetDriverVisible(true);
                player.transform.SetParent(car.DriverAnchor, false);
                player.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                playerAnimator.SeatIn();
            }
            else
            {
                driverHidden = false;
                if (car.Door != null) car.Door.Open();
            }

            followCamera?.Follow(car, snap: false);
        }

        /// <summary>
        /// The tuned path. Clip progress drives the door: it swings open a quarter of the way in and
        /// starts shutting at seven tenths, both from <c>config.vehicle.driver</c>, so the swing is
        /// tied to what the animation is doing rather than to a stopwatch that would drift out of
        /// step the moment the clip is re-exported.
        /// </summary>
        private void TickEnterClip()
        {
            var driver = _spec.Driver;
            var progress = Mathf.Clamp01(timer / playerAnimator.EnterCarSeconds);

            if (activeVehicle.Door != null)
            {
                var openAt = driver?.DoorOpenAt ?? 0.25f;
                var closeAt = driver?.DoorCloseAt ?? 0.7f;
                activeVehicle.Door.SetOpen(progress >= openAt && progress < closeAt);
            }

            if (progress >= 1f) BeginDriving();
        }

        /// <summary>
        /// The fallback path, and what every untuned car in the game uses: a timed door swing with
        /// the driver simply hidden. No animation, so nothing to wait on and nothing to look at.
        /// </summary>
        private void TickEnterQuick()
        {
            if (!driverHidden && timer >= _spec.EnterDoorOpenTime)
            {
                driverHidden = true;
                SetDriverVisible(false);
                var seat = activeVehicle.DriverAnchor != null
                    ? activeVehicle.DriverAnchor
                    : activeVehicle.transform;
                player.transform.SetParent(seat, false);
                player.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            }

            if (timer >= _spec.EnterDoorOpenTime + _spec.EnterDoorCloseDelay) BeginDriving();
        }

        private void BeginDriving()
        {
            mode = GameMode.Driving;
            timer = 0f;
            if (activeVehicle.Door != null) activeVehicle.Door.Close();
            activeVehicle.Driven = true;
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

            var car = activeVehicle;
            car.Driven = false;
            if (car.Door != null) car.Door.Open();

            // worldPositionStays MUST be false. The seat anchor carries the config's driver scale
            // (0.95 on the Mustang), and preserving the world transform on the way out would bake
            // that into Joe's own localScale — he would walk away permanently 5% shorter, a little
            // more so with every car he got out of.
            player.transform.SetParent(null, worldPositionStays: false);
            var spot = ExitSpot(car);
            player.transform.SetPositionAndRotation(spot, car.transform.rotation);

            SetDriverVisible(true);
            driverHidden = false;
            playerAnimator?.SeatOut();
            followCamera?.FollowPlayer(snap: false);
        }

        /// <summary>
        /// Beside the driver's door, on whatever surface is actually under that spot.
        ///
        /// The probe matters more than it looks: hard-coding the road height drops anyone stepping
        /// out of a car parked on lot asphalt through the tarmac, and would put U23's helicopter
        /// pilot at street level the moment he lands on a roof. The car's own colliders are skipped
        /// — otherwise the first thing the ray finds is the chassis box it just left.
        /// </summary>
        private Vector3 ExitSpot(CarController car)
        {
            var beside = car.transform.position + car.DriverSide * _spec.ExitSideOffset;
            var from = new Vector3(beside.x, car.transform.position.y + exitProbeHeight, beside.z);

            var best = float.NegativeInfinity;
            foreach (var hit in Physics.RaycastAll(
                         from, Vector3.down, exitProbeDepth, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(car.transform)) continue;
                if (hit.point.y > best) best = hit.point.y;
            }

            return new Vector3(
                beside.x,
                float.IsNegativeInfinity(best) ? car.transform.position.y : best,
                beside.z);
        }

        private void FinishExit()
        {
            if (activeVehicle != null && activeVehicle.Door != null) activeVehicle.Door.Close();
            activeVehicle = null;
            mode = GameMode.OnFoot;
            timer = 0f;

            if (_capsule != null) _capsule.enabled = true;
            player.enabled = true;
        }

        // --- helpers -----------------------------------------------------------------------------

        /// <summary>
        /// The nearest car within <c>enterRadius</c>, measured on the ground plane so standing on a
        /// kerb beside one still counts.
        /// </summary>
        private CarController Nearest()
        {
            if (spawner == null) return null;

            CarController best = null;
            var bestDistance = _spec.EnterRadius * _spec.EnterRadius;
            var here = player.transform.position;

            foreach (var car in spawner.Spawned)
            {
                if (car == null) continue;
                var offset = car.transform.position - here;
                var distance = offset.x * offset.x + offset.z * offset.z;
                if (distance > bestDistance) continue;
                bestDistance = distance;
                best = car;
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
