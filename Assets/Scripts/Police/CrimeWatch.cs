using TheBlock.Core;
using TheBlock.Npc;
using TheBlock.Vehicles;
using TheBlock.World;
using UnityEngine;

namespace TheBlock.Police
{
    /// <summary>
    /// The port of <c>crime.ts</c>: decides what counts as a crime, and how much.
    ///
    /// Two sources, and deliberately only two — the same pair the shipped game has:
    ///  - <see cref="RunOverSystem.RanOver"/>, which U18 already owns. There is no second pedestrian
    ///    detector to add: the web's <c>crime.ts pedHit</c> radius scan is dead upstream and the
    ///    decisions log says not to re-split them.
    ///  - <see cref="CrashSensor.Crashed"/>, which is new, and is where the "too sensitive" problem
    ///    was actually fixed.
    ///
    /// <b>Gates.</b> No crime exists on foot — that is the web's rule and it survives, because a
    /// pedestrian who bumps a wall is not a police matter. Nothing happens indoors. A mission gate is
    /// exposed for U20 (<see cref="Heat.SuppressCrash"/>) and deliberately not wired to anything yet:
    /// the web suppresses crashes but never run-overs inside a mission, because its one driving
    /// mission is a bike in Florentin's alleys where clipping a wall is constant.
    /// </summary>
    public class CrimeWatch : MonoBehaviour
    {
        [SerializeField] private Heat heat;
        [SerializeField] private RunOverSystem runOver;
        [SerializeField] private VehicleEnterExit vehicles;
        [SerializeField] private Interior interior;

        [Header("Debug")]
        [Tooltip("P adds one star, so the escalation can be play-tested without hunting three " +
                 "separate crowds. Debug-only, like T for the traffic and C for the crowd, and it " +
                 "goes when U19 is confirmed.")]
        [SerializeField] private bool debugStarKey = true;

        private float _sinceRunOver = 999f;
        private float _sinceCrash = 999f;
        private bool _hooked;

        /// <summary>Did the last impact the sensor reported count as a crime? For the probe's table.</summary>
        public bool LastCrashWasCrime { get; private set; }

        /// <summary>Raised for every impact the sensor reported, filtered or not, so the probe can log it.</summary>
        public event System.Action<CrashSensor.Impact, bool> Judged;

        private void Awake() => Bind();

        private void OnEnable()
        {
            Bind();
            Hook();
        }

        private void OnDisable()
        {
            if (runOver != null) runOver.RanOver -= OnRanOver;
            CrashSensor.Crashed -= OnCrashed;
            _hooked = false;
        }

        private void Bind()
        {
            if (heat == null) heat = FindAnyObjectByType<Heat>();
            if (runOver == null) runOver = FindAnyObjectByType<RunOverSystem>();
            if (vehicles == null) vehicles = FindAnyObjectByType<VehicleEnterExit>();
            if (interior == null) interior = FindAnyObjectByType<Interior>();
        }

        /// <summary>
        /// Subscribes once, and can be called again after a mid-Play recompile has thrown the
        /// delegates away without re-running Awake.
        /// </summary>
        private void Hook()
        {
            if (_hooked || heat == null) return;

            if (runOver != null) runOver.RanOver += OnRanOver;
            CrashSensor.Crashed += OnCrashed;
            _hooked = true;
        }

        private void Update()
        {
            if (heat == null) Bind();
            if (!_hooked) Hook();

            _sinceRunOver += Time.deltaTime;
            _sinceCrash += Time.deltaTime;

            if (heat != null && interior != null) heat.Frozen = interior.Inside;

            if (debugStarKey && heat != null && !Core.Pause.Frozen &&
                UnityEngine.InputSystem.Keyboard.current?.pKey.wasPressedThisFrame == true)
                heat.Bump();
        }

        /// <summary>Is the player behind a wheel? Everything here is gated on it.</summary>
        private bool Driving => vehicles != null && vehicles.Mode == GameMode.Driving;

        private void OnRanOver(int downed)
        {
            if (heat == null || downed <= 0) return;
            if (!Driving || heat.Frozen) return;
            if (_sinceRunOver < heat.Tuning.RunOverCooldown) return;

            _sinceRunOver = 0f;

            // One event, one star, however many went down — the web's rule. A pavement full of
            // people is not five cars, and the cooldown is what stops a rampage being one either.
            heat.Bump();
        }

        /// <summary>
        /// Weighs one impact.
        ///
        /// The order of the filters is the whole point, and the first two are what makes a scrape
        /// free: only the car the player is driving can commit a crime, and a contact whose normal is
        /// near-vertical is the ground rather than a wall.
        ///
        /// <b>The last filter is new, and it is what a counter forces.</b> Heat is whole stars again,
        /// so a crash is worth one star or nothing at all and "how hard did I hit that" has to become
        /// a threshold rather than a multiplier. <see cref="PoliceTuning.CrashCrimeSpeed"/> is that
        /// line, set where a wall at about 22 km/h is a crime and a kerb hop is not — and
        /// <see cref="PoliceTuning.VehicleCrashCrimeSpeed"/> is the far lower one an impact against
        /// another car is judged by, because that is a hit-and-run and the wall figure exists to
        /// forgive geometry, not victims.
        /// </summary>
        private void OnCrashed(CrashSensor.Impact impact)
        {
            LastCrashWasCrime = false;
            if (heat == null || impact.Sensor == null) return;

            bool mine = Driving && vehicles.ActiveVehicle != null &&
                        vehicles.ActiveVehicle.GetTransform() == impact.Sensor.transform;

            if (!mine || heat.Frozen || heat.SuppressCrash)
            {
                Judged?.Invoke(impact, false);
                return;
            }

            if (_sinceCrash < heat.Tuning.CrashCooldown || !impact.Sensor.AtFault(impact))
            {
                Judged?.Invoke(impact, false);
                return;
            }

            // Two lines, not one: what you hit decides which. A wall wants the high bar that made
            // scrapes free; another CAR is a hit-and-run and is a crime at little more than walking
            // pace. `HitVehicle` is read off the collider rather than off `Other`, because a parked
            // filler has no Rigidbody to be.
            float line = impact.HitVehicle
                ? heat.Tuning.VehicleCrashCrimeSpeed
                : heat.Tuning.CrashCrimeSpeed;

            if (impact.ClosingSpeed < line)
            {
                Judged?.Invoke(impact, false);
                return;
            }

            LastCrashWasCrime = true;
            Judged?.Invoke(impact, true);

            _sinceCrash = 0f;
            heat.Bump();
        }
    }
}
