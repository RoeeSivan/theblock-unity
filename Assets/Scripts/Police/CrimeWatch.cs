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

        private float _sinceRunOver = 999f;
        private float _sinceCrash = 999f;
        private bool _hooked;

        /// <summary>Last impact's heat, for the probe's crash table. Zero when it was filtered out.</summary>
        public float LastCrashHeat { get; private set; }

        /// <summary>Raised for every impact the sensor reported, filtered or not, so the probe can log it.</summary>
        public event System.Action<CrashSensor.Impact, float> Judged;

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
        }

        /// <summary>Is the player behind a wheel? Everything here is gated on it.</summary>
        private bool Driving => vehicles != null && vehicles.Mode == GameMode.Driving;

        private void OnRanOver(int downed)
        {
            if (heat == null || downed <= 0) return;
            if (!Driving || heat.Frozen) return;
            if (_sinceRunOver < heat.Tuning.RunOverCooldown) return;

            _sinceRunOver = 0f;

            // One person is one star. Everyone after the first is worth much less, capped — a
            // pavement full of people is worth two stars, not five.
            float gain = heat.Tuning.RunOverFirst + heat.Tuning.RunOverExtra * (downed - 1);
            heat.Add(Mathf.Min(gain, heat.Tuning.RunOverCap));
        }

        /// <summary>
        /// Weighs one impact.
        ///
        /// The order of the filters is the whole point, and the first two are what makes a scrape
        /// free: only the car the player is driving can commit a crime, and a contact whose normal is
        /// near-vertical is the ground rather than a wall. The severity itself is closing speed above
        /// a deadzone, so the relationship between "how hard did I hit that" and "how much trouble am
        /// I in" is continuous instead of a step at 14 km/h.
        /// </summary>
        private void OnCrashed(CrashSensor.Impact impact)
        {
            LastCrashHeat = 0f;
            if (heat == null || impact.Sensor == null) return;

            bool mine = Driving && vehicles.ActiveVehicle != null &&
                        vehicles.ActiveVehicle.GetTransform() == impact.Sensor.transform;

            if (!mine || heat.Frozen || heat.SuppressCrash)
            {
                Judged?.Invoke(impact, 0f);
                return;
            }

            if (_sinceCrash < heat.Tuning.CrashCooldown || !impact.Sensor.AtFault(impact))
            {
                Judged?.Invoke(impact, 0f);
                return;
            }

            float over = Mathf.Max(0f, impact.ClosingSpeed - heat.Tuning.CrashDeadzone);
            float weight = impact.Other != null && impact.Other.GetComponent<CopCar>() != null
                ? heat.Tuning.CrashCopWeight
                : 1f;

            float gain = Mathf.Min(over * heat.Tuning.CrashPerClosingSpeed * weight, heat.Tuning.CrashCap);
            LastCrashHeat = gain;
            Judged?.Invoke(impact, gain);

            if (gain <= 0f) return;

            _sinceCrash = 0f;
            heat.Add(gain);
        }
    }
}
