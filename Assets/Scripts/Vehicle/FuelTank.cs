using TheBlock.Core;
using UnityEngine;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// One vehicle's fuel tank: it burns by DISTANCE, and when it runs dry it does not stop the car
    /// — it eases the speed ceiling down to a quarter and wobbles it, so an empty tank is a limp
    /// home rather than a game over. Port of <c>src/vehicle/fuel.ts</c>.
    ///
    /// <b>Being a component IS the exemption mechanism.</b> The web expresses "this vehicle has no
    /// tank" as an optional field on its <c>Vehicle</c> type, with the comment that the exemption is
    /// "a type fact rather than a list of vehicle names". Here it is a component fact: the
    /// helicopter, the jetski and every police cruiser simply never receive one, and
    /// <c>FuelFactor</c> on a controller with no tank is 1. There is no list, no flag and nothing to
    /// keep in sync.
    ///
    /// <b>It is added at runtime, by <see cref="FuelSystem"/>, the first time you drive the vehicle
    /// — never baked into a prefab.</b> That is not laziness: <c>PoliceCarBuilder</c> reuses
    /// <c>CarBuilder</c> wholesale, so a tank baked by the car builder would land on
    /// <c>PoliceCar.prefab</c> too, and a cruiser limping mid-pursuit is U28's ☕ bug in a new
    /// currency. The known cost of runtime attachment — a component that misses the host's
    /// <c>Awake</c> cache — is paid explicitly: the tank PUSHES itself onto the controller in
    /// <see cref="Configure"/>, and each controller's <c>Bind</c> keeps a <c>TryGetComponent</c> as
    /// the belt to that push's braces.
    ///
    /// <b>A promoted or hijacked car gets a NEW tank at half full, every time.</b>
    /// <c>CarSpawner.Take</c> instantiates a fresh prefab, so there is nothing to carry over. The web
    /// build behaves identically (its <c>promoteLotCar</c> calls <c>createVehicle</c>, which calls
    /// <c>createFuelTank</c>), so stealing a car has always been a way to refill. Left as it is
    /// deliberately; the one-line fix, if it ever matters, is to seed a hijacked car's tank from the
    /// one you walked away from.
    /// </summary>
    [DisallowMultipleComponent]
    public class FuelTank : MonoBehaviour
    {
        /// <summary>
        /// The economy's master switch, the port of <c>setFuelEnabled</c>. Off means no drain, no
        /// cap easing and a <see cref="SpeedFactor"/> of exactly 1 — the web flips it for every
        /// multiplayer entry, since a match cannot ask half its players to find a petrol station.
        ///
        /// <b>It needs BOTH resets below, and <see cref="SpeedBoost"/> needs neither.</b> A
        /// <c>RuntimeInitializeOnLoadMethod</c> fires once per SESSION, not per scene load, so
        /// "Quit to Title" would carry a false into the next game. The boost survives that because
        /// <c>PowerUps.PushEffects</c> rewrites it every tick; nothing rewrites this.
        /// </summary>
        public static bool Enabled { get; private set; } = true;

        /// <summary>Turns the whole fuel economy on or off. Nothing calls it yet; U32 will.</summary>
        public static void SetEnabled(bool on) => Enabled = on;

        /// <summary>Called by <c>SessionReset</c> on every scene load. See <see cref="Enabled"/>.</summary>
        public static void ResetForSession() => Enabled = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetOnPlay() => Enabled = true;

        /// <summary>
        /// Litres in the tank. <b>Serialized so a mid-Play recompile does not refill it.</b> A domain
        /// reload wipes every non-serialized field and never re-runs Awake, so a plain float would
        /// hand a stranded player a full tank every time a script is saved.
        /// </summary>
        [SerializeField, HideInInspector] private float litres;

        /// <summary>
        /// The eased speed ceiling multiplier, 1 down to <c>LimpSpeedFrac</c>. Serialized alongside
        /// <see cref="litres"/> so a reload does not snap a limping car back to full speed.
        /// </summary>
        [SerializeField, HideInInspector] private float cap = 1f;

        /// <summary>Seconds accumulated, for the dry-tank sputter. Advances even at zero litres.</summary>
        [SerializeField, HideInInspector] private float phase;

        /// <summary>
        /// True once <see cref="Configure"/> has seeded the tank.
        ///
        /// <b>Not padding.</b> A domain reload keeps serialized fields, so re-seeding on every bind
        /// would refill a half-burned tank — that is what this guards. The initialiser on
        /// <see cref="cap"/> is the other half of the same problem from the other end: a brand-new
        /// component would otherwise come up at <c>cap = 0</c> and clamp the car to a dead stop.
        /// </summary>
        [SerializeField, HideInInspector] private bool configured;

        /// <summary>
        /// The vehicle this tank belongs to, held as the concrete MonoBehaviour.
        ///
        /// <b>Never as <c>IEnterable</c>.</b> Unity's serializer cannot write an interface field: it
        /// stores nothing, warns about nothing, and the reference is gone after the next reload.
        /// <c>VehicleEnterExit.activeVehicleObject</c> carries the same workaround.
        /// </summary>
        [SerializeField, HideInInspector] private MonoBehaviour vehicleObject;

        private TheBlockConfig.FuelSpec _spec;
        private float _litresPerMetre;
        private float _fillRate;

        /// <summary>How full, 0..1. This is what the HUD bar draws.</summary>
        public float Fraction => _spec == null || _spec.TankLitres <= 0f ? 0f : litres / _spec.TankLitres;

        /// <summary>At or below <c>LowFrac</c>: the bar goes red and the hint fires.</summary>
        public bool IsLow => Fraction <= (_spec?.LowFrac ?? 0.2f);

        /// <summary>Dry. Not stranded — see <see cref="SpeedFactor"/>.</summary>
        public bool IsEmpty => litres <= 0f;

        /// <summary>Full, and therefore nothing to do at a pump.</summary>
        public bool IsFull => _spec != null && litres >= _spec.TankLitres;

        /// <summary>Litres, for the measurement passes. Not gameplay.</summary>
        public float Litres => litres;

        /// <summary>The vehicle, or null if this tank was orphaned by a reload.</summary>
        public IEnterable Vehicle => vehicleObject as IEnterable;

        /// <summary>
        /// The factor this tank puts on its vehicle's speed ceiling.
        ///
        /// <b>It multiplies with ☕ rather than competing with it</b> — a boosted car on a dry tank
        /// still limps, because you cannot drink your way out of an empty tank. That is the exact
        /// requirement <c>SpeedBoost</c>'s own doc comment wrote down before this unit existed.
        ///
        /// A full tank returns exactly 1 through the <c>cap >= 0.999f</c> early-out, so ordinary
        /// driving never pays a float-drift cost on the clamp.
        /// </summary>
        public float SpeedFactor
        {
            get
            {
                if (!Enabled || _spec == null) return 1f;
                if (litres > 0f && cap >= 0.999f) return 1f;

                // The sputter only exists at zero. Above it the cap eases silently, so a tank that
                // is merely low drives exactly like a full one — the drama is all at the end.
                var wobble = litres <= 0f
                    ? 1f + _spec.SputterDepth * Mathf.Sin(phase * 2f * Mathf.PI * _spec.SputterHz)
                    : 1f;
                return cap * wobble;
            }
        }

        /// <summary>
        /// Seeds the tank and hands it to its controller. Safe to call again — a tank that is already
        /// <see cref="configured"/> keeps its litres, which is what makes it survive a recompile.
        /// </summary>
        /// <param name="spec">The config section. Never null; <see cref="FuelSystem"/> refuses to
        /// bind without it.</param>
        /// <param name="startFrac">Below zero means "the config's <c>StartFrac</c>". The port of
        /// the web's <c>?fuel=</c> dev override.</param>
        /// <param name="drainScale">Multiplies the DISTANCE burn only, never the idle trickle —
        /// same as the web's <c>?drain=</c>.</param>
        public void Configure(TheBlockConfig.FuelSpec spec, float startFrac, float drainScale, MonoBehaviour vehicle)
        {
            _spec = spec;
            _litresPerMetre = spec.LitresPerKm / 1000f * Mathf.Max(0.0001f, drainScale);
            _fillRate = spec.RefuelSecFromEmpty > 0f ? spec.TankLitres / spec.RefuelSecFromEmpty : spec.TankLitres;
            vehicleObject = vehicle;

            if (!configured)
            {
                litres = spec.TankLitres * (startFrac >= 0f ? Mathf.Clamp01(startFrac) : spec.StartFrac);
                cap = 1f;
                phase = 0f;
                configured = true;
            }

            switch (vehicle)
            {
                case CarController car: car.AttachTank(this); break;
                case MotorcycleController bike: bike.AttachTank(this); break;
            }
        }

        /// <summary>True once <see cref="Configure"/> has run in this session's domain.</summary>
        public bool IsBound => _spec != null;

        /// <summary>
        /// Adds fuel, and returns true on the single frame the tank REACHES full — the "done" edge
        /// the nozzle clunk and the hint hang off. It never latches: ask again next frame and it is
        /// false, because the tank is already full.
        /// </summary>
        public bool Fill(float dt)
        {
            if (_spec == null || litres >= _spec.TankLitres) return false;
            litres += _fillRate * dt;
            if (litres < _spec.TankLitres) return false;
            litres = _spec.TankLitres;
            return true;
        }

        /// <summary>
        /// Burns, then eases the cap. Runs on the physics tick, and only while the vehicle is being
        /// driven — <c>Driven</c> is false on every parked car and, explicitly, on every police
        /// cruiser, so nothing burns fuel off-screen.
        ///
        /// <c>timeScale = 0</c> stops FixedUpdate outright, so a paused game burns nothing and this
        /// needs no <c>Pause.Frozen</c> guard of its own: it reads no input, and that guard exists
        /// for scripts that do.
        /// </summary>
        private void FixedUpdate()
        {
            if (_spec == null || !Enabled) return;
            if (Vehicle is not { Driven: true }) return;

            var dt = Time.fixedDeltaTime;
            var speed = Mathf.Abs(Vehicle.ForwardSpeed);

            // Reverse burns too — it is |speed|, not speed. And the idle trickle is additive and
            // unconditional while there is fuel: sitting at a red light costs something.
            if (litres > 0f)
                litres = Mathf.Max(0f, litres - (speed * dt * _litresPerMetre + _spec.IdleLitresPerSec * dt));

            // Outside the guard above, on purpose. Zero litres is exactly when the cap has somewhere
            // to go, and the phase has to keep advancing or the sputter would stand still.
            cap = Mathf.MoveTowards(cap, litres > 0f ? 1f : _spec.LimpSpeedFrac, _spec.LimpRampPerSec * dt);
            phase += dt;
        }
    }
}
