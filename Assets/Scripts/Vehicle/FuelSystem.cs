using TheBlock.Audio;
using TheBlock.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// The pump, the hold, and the two reminders. Port of the fuel block in <c>src/main.ts</c> plus
    /// <c>src/vehicle/refuel.ts</c>.
    ///
    /// It owns three things the tank itself deliberately does not: WHICH vehicle currently has a
    /// tank, the hold-to-fill loop with its tick clock, and the per-tank reminder state. The tank is
    /// arithmetic; this is the interaction.
    ///
    /// <b>One predicate behind the prompt and the action</b> — <see cref="CanFuel"/> — which is this
    /// project's most-repeated invariant and the reason a locked Huey never offered "Press E".
    /// </summary>
    [DisallowMultipleComponent]
    public class FuelSystem : MonoBehaviour
    {
        [Header("Debug — the ports of the web's ?fuel= and ?drain=")]
        [Tooltip("Starting tank fraction. Below zero means the config's StartFrac (0.5). Debug — leave at -1.")]
        [SerializeField] private float debugStartFrac = -1f;

        [Tooltip("Multiplies the DISTANCE burn only. 20 turns an 8-minute range into 24 seconds, " +
                 "which is what makes the burn measurable in one Play session. Debug — leave at 1.")]
        [SerializeField] private float debugDrainScale = 1f;

        [Header("Scene — found automatically when left empty")]
        [SerializeField] private VehicleEnterExit vehicles;
        [SerializeField] private World.GasStation station;
        [SerializeField] private UI.MissionHud hud;
        [SerializeField] private UI.BriefingCard briefing;

        private TheBlockConfig.FuelSpec _spec;
        private bool _bound;

        /// <summary>Seconds this tank has been under the player. Gates both reminders.</summary>
        private float _tankTime;

        private FuelTank _lastTank;
        private bool _lowWarned;
        private bool _halfWarned;
        private float _tickClock;

        /// <summary>The tank of whatever is being driven, or null. The HUD bar reads this.</summary>
        public FuelTank ActiveTank { get; private set; }

        /// <summary>True on the frames fuel is actually flowing. Drives the bar's pulse.</summary>
        public bool Filling { get; private set; }

        private void Awake() => Bind();

        private void Bind()
        {
            if (_bound) return;

            var snapshot = TheBlockConfig.Load();
            _spec = snapshot?.Fuel;
            if (_spec == null)
            {
                // No hard-coded fallback, on purpose. Per-FIELD defaults cover a key the exporter
                // has not learned yet; a missing SECTION means the JSON is stale, and quietly
                // driving on invented numbers is how a port's tuning drifts away from its source.
                // Unbound means no tank is ever attached, every clamp multiplies by 1, and the game
                // drives exactly as it did before this unit: loud in the console, invisible in play.
                Debug.LogError("FuelSystem: the snapshot has no `fuelConfig`. Re-run tools/export-config.sh.");
                return;
            }

            _bound = true;

            if (vehicles == null) vehicles = FindAnyObjectByType<VehicleEnterExit>();
            if (station == null) station = FindAnyObjectByType<World.GasStation>();
            if (hud == null) hud = FindAnyObjectByType<UI.MissionHud>();
            if (briefing == null) briefing = FindAnyObjectByType<UI.BriefingCard>();

            if (station == null)
                Debug.LogWarning("FuelSystem: no GasStation in the scene — nothing to refuel at. " +
                                 "Run The Block → Build Gas Station.", this);

            if (debugStartFrac >= 0f) Debug.Log($"[fuel] debugStartFrac {debugStartFrac:0.##}");
            if (!Mathf.Approximately(debugDrainScale, 1f)) Debug.Log($"[fuel] debugDrainScale {debugDrainScale:0.##}×");
        }

        private void Update()
        {
            if (Pause.Frozen) return;
            if (!_bound) Bind();
            if (!_bound) return;

            var dt = Time.deltaTime;

            var vehicle = vehicles != null && vehicles.Mode == GameMode.Driving ? vehicles.ActiveVehicle : null;
            var tank = TankFor(vehicle);
            ActiveTank = tank;

            // A change of vehicle re-arms both reminders and restarts the delay: getting into any
            // car on half a tank should nudge you toward the pump again, because it is a different
            // tank with its own errand.
            if (!ReferenceEquals(tank, _lastTank))
            {
                _lastTank = tank;
                _tankTime = 0f;
                _lowWarned = false;
                _halfWarned = false;
                _tickClock = 0f;
            }

            _tankTime += dt;

            var here = vehicle?.Anchor != null ? vehicle.Anchor.position : transform.position;
            var eligible = CanFuel(vehicle, tank, here);

            // isPressed, not wasPressedThisFrame: this is a HELD key. Space is also the brake, which
            // the web build calls helpful rather than a conflict — it pins you on the forecourt, and
            // you have to be under 2 km/h to fuel anyway.
            //
            // The briefing clause is load-bearing: the card reads Space as its own dismiss key and
            // is open WITHOUT Pause.Frozen, so without it a card on screen would secretly refuel.
            var keyboard = Keyboard.current;
            var holding = keyboard != null && keyboard.spaceKey.isPressed && !(briefing != null && briefing.IsOpen);

            Filling = false;
            if (eligible && holding)
            {
                Filling = true;

                if (tank.Fill(dt))
                {
                    GameAudio.Cue(SfxCue.FuelDone);
                    hud?.ShowHint("⛽ Tank full — go!");
                    _lowWarned = false;
                    _halfWarned = false;
                }

                // Own the cadence: there is no loop helper in Sfx, and the cue's own note says it
                // fires ~10 times a fill and must not become a drill.
                _tickClock += dt;
                if (_tickClock >= _spec.TickSec)
                {
                    _tickClock -= _spec.TickSec;
                    GameAudio.Cue(SfxCue.FuelTick);
                }
            }
            else
            {
                _tickClock = 0f;
            }

            // The prompt, from the predicate the action just used, claimed every frame. PromptVehicle
            // rather than a new tier: measured, the nearest of the 15 mission delivery spots is
            // 206.6 m from the station against a 6 m radius, the retry prompt needs you inside the
            // pizzeria, and the rescue and chase prompts need the heli or the ski. Nothing can
            // contend at a pump, so the existing three-tier chain is enough.
            if (eligible)
                hud?.SetPrompt(
                    Filling ? $"⛽ Fuelling… {Mathf.RoundToInt(tank.Fraction * 100f)}%" : "⛽ Hold SPACE to refuel",
                    UI.MissionHud.PromptVehicle);

            // Per TANK, and NOT through Onboarding.FirstTime — that is once-ever and PlayerPrefs
            // backed, which would spend both of these on the first car of the first session. These
            // re-arm on a change of vehicle and on a completed fill, exactly as the web's do.
            if (tank != null && _tankTime >= _spec.RemindDelaySec)
            {
                if (tank.IsLow && !_lowWarned)
                {
                    _lowWarned = true;

                    // Low supersedes half: firing it marks half as warned too, so one tank is worth
                    // at most one hint.
                    _halfWarned = true;
                    hud?.ShowHint("⛽ Low fuel — the ⛽ pin on the map (M) is the gas station");
                }
                else if (!_halfWarned && tank.Fraction <= _spec.RemindFrac)
                {
                    _halfWarned = true;
                    hud?.ShowHint("⛽ Half a tank — fill up at the Paz station (⛽ on the map)");
                }
            }
        }

        /// <summary>
        /// The one predicate. <b><c>IsFull</c> is in it, and the web build's is not</b> — over there
        /// the prompt is drawn from <c>canFuel</c> alone while the fill bails separately on full, so
        /// parking with a brimmed tank offers "Hold SPACE to refuel" for a key that does nothing.
        /// That is precisely the disagreement this project states three times that it will not have,
        /// so the port folds it in: the prompt disappears on the same frame "Tank full" appears.
        /// </summary>
        private bool CanFuel(IEnterable vehicle, FuelTank tank, Vector3 worldPos)
        {
            if (tank == null || station == null) return false;
            if (tank.IsFull) return false;
            if (vehicle.SpeedKmh > _spec.PumpMaxKmh) return false;
            return station.AtPump(worldPos);
        }

        /// <summary>
        /// The tank of whatever is being driven, attaching one on first need.
        ///
        /// <b>This is where the helicopter, the jetski and every cruiser fall out</b>, and they fall
        /// out by never being handed a component rather than by appearing on a list. The
        /// <c>IsPolice</c> clause is a second, redundant lock — a cruiser is not enterable, so
        /// <see cref="VehicleEnterExit"/> can never hand one over — and redundancy is cheap where
        /// U28 already paid once for an exemption that did not latch.
        /// </summary>
        private FuelTank TankFor(IEnterable vehicle)
        {
            // Test the Transform, not the interface: an as-cast interface reference does not carry
            // Unity's fake-null, so a destroyed vehicle compares non-null through it.
            if (vehicle?.Anchor == null) return null;

            var wants = vehicle switch
            {
                CarController car => !car.IsPolice,
                MotorcycleController => true,
                _ => false,
            };
            if (!wants) return null;

            var host = (MonoBehaviour)vehicle;
            if (!host.TryGetComponent<FuelTank>(out var tank)) tank = host.gameObject.AddComponent<FuelTank>();
            if (!tank.IsBound) tank.Configure(_spec, debugStartFrac, debugDrainScale, host);
            return tank;
        }
    }
}
