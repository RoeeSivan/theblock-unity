using TheBlock.Core;
using TheBlock.World;
using UnityEngine;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// What happens when you drive into the sea - the GTA behaviour, ported to this game's pieces.
    ///
    /// <b>Why there was nothing here before.</b> Until 2026-08-18 the sea could not be reached at all:
    /// an 8 m invisible wall stood on the waterline (<c>WorldBuilder.BuildShoreWall</c>) and a second
    /// one behind it (<c>BuildWorldEdges</c>), so aiming a car at the water was ramming a wall, which
    /// <c>CrashSensor</c> measured and <c>CrimeWatch</c> turned into a wanted level. That was the
    /// user's "driving into the water ignites the police" - not a water rule, a wall. Both walls moved
    /// out to the plate's rim; this component is what fills the 270 m they opened up.
    ///
    /// <b>The reference behaviour</b> (GTA V, checked rather than remembered): any land vehicle can be
    /// driven in and it sinks; the engine shuts down as the water reaches it; a bike throws its rider
    /// off; the player swims out; and water does <b>not</b> raise a wanted level - submerging is one of
    /// the ways to LOSE one, because it breaks line of sight. All four are here.
    ///
    /// <b>Four stages, and the interesting one is <see cref="Stage.Wading"/>.</b> A game where the
    /// first centimetre of water is fatal is worse than one with a wall: the beach becomes a place you
    /// cannot go near. So shallow water only drags - you can splash along the tide line and reverse
    /// out - and it is sustained depth over the engine, not contact with the sea, that kills it.
    ///
    /// <b>It floats on <see cref="SeaSurface"/>, never on <c>sea.Level</c></b> (memory:
    /// <c>vertex-displaced-water-has-no-cpu-height</c>). The swell reaches 0.37 m and a hull floated on
    /// the mean is under water for half of every wave, which reads as a car that sinks and resurfaces.
    ///
    /// <b>Attached at runtime by the controller's own Bind</b>, exactly like <see cref="CrashSensor"/>
    /// and <see cref="VehicleDamage"/> - a component put on the prefab by hand is regenerated away the
    /// next time <c>The Block → Build Drivable Cars</c> runs, which is how CrashSensor came to be
    /// attached to nothing for fourteen units.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class WaterEntry : MonoBehaviour
    {
        /// <summary>How wet this vehicle is, as a state rather than as a number.</summary>
        public enum Stage
        {
            /// <summary>Out of the water, or barely in it.</summary>
            Dry,

            /// <summary>In the shallows. Heavy drag, engine still alive, you can reverse out.</summary>
            Wading,

            /// <summary>Engine drowned. It is taking on water and its buoyancy is bleeding away.</summary>
            Flooding,

            /// <summary>On the bottom. Empty of its driver, and on its way back to where it lives.</summary>
            Sunk,
        }

        [Header("Depth thresholds - fractions of the hull's height that are under the surface")]
        [Tooltip("Below this the vehicle is dry. Small, so a wheel through a puddle at the tide line " +
                 "is not an event.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float wadeAt = 0.06f;

        [Tooltip("Sustained submersion at which the engine drowns. About a third of the hull is the " +
                 "bonnet line on these bodies - water over the wheels is wading, water over the " +
                 "engine is not.")]
        [Range(0.1f, 0.9f)]
        [SerializeField] private float drownAt = 0.34f;

        [Tooltip("Submersion at which the driver is thrown clear. The cabin is under by here, and a " +
                 "player who is still holding W is not going to reverse out of it.")]
        [Range(0.3f, 1f)]
        [SerializeField] private float bailOutAt = 0.80f;

        [Header("Timings")]
        [Tooltip("Seconds past `drownAt` before a CAR's engine cuts. Not instant: a fast enough " +
                 "entry throws a bow wave over the bonnet and should still let you climb back out.")]
        [SerializeField] private float carDrownSeconds = 1.6f;

        [Tooltip("Seconds past `drownAt` before a BIKE's engine cuts. Near-instant - the engine is " +
                 "between your knees and there is no cabin to keep the water out.")]
        [SerializeField] private float bikeDrownSeconds = 0.35f;

        [Tooltip("Seconds from the engine dying to zero buoyancy. This is the float-then-sink the " +
                 "whole effect rests on: it has to be long enough to swim clear of and short enough " +
                 "that the car does not become a boat.")]
        [SerializeField] private float sinkSeconds = 7f;

        [Tooltip("Seconds resting on the bottom, with nobody aboard, before it is put back where it " +
                 "lives. The car you drowned is gone - `IEnterable.Respawn` returns it to its lot.")]
        [SerializeField] private float retireSeconds = 6f;

        [Header("Water physics - PhysX numbers, derived by feel (port rule 2)")]
        [Tooltip("Upthrust at full submersion, in multiples of the vehicle's weight, before it " +
                 "floods. Over 1 so it bobs rather than hovering; well under 2 so it sits low.")]
        [SerializeField] private float buoyancy = 1.4f;

        [Tooltip("Linear drag in the water, as a force rather than as Rigidbody damping. A FORCE on " +
                 "purpose: damping is a stored field, and restoring it correctly across a mid-Play " +
                 "recompile - which brings back the cached original as whatever the water had just " +
                 "written - is a bug waiting to be found. A force leaves no state to restore.")]
        [SerializeField] private float waterDrag = 2.4f;

        [Tooltip("Rotational drag in the water. High, or the hull yaws and rolls for ever with " +
                 "nothing to stop it.")]
        [SerializeField] private float waterAngularDrag = 4f;

        [Tooltip("How far the four float points are pushed out toward the hull's corners, as a " +
                 "fraction of its half-extents. Wide enough to right the body, inside the hull so " +
                 "a rolling wave cannot lever it over.")]
        [Range(0.3f, 1f)]
        [SerializeField] private float floatSpread = 0.8f;

        // --- run state ---------------------------------------------------------------------------

        private Rigidbody _body;
        private TheBlockConfig.SeaSpec _sea;
        private CarController _car;
        private MotorcycleController _bike;
        private VehicleEnterExit _vehicles;

        private Vector3 _hullCenter;
        private Vector3 _hullExtents;

        private float _sinceDeep;
        private float _flood;
        private float _sinceSunk;

        /// <summary>How much of the hull's height is under the water, 0 to 1.</summary>
        public float Submersion { get; private set; }

        /// <summary>Where this vehicle is in the sequence.</summary>
        public Stage State { get; private set; } = Stage.Dry;

        /// <summary>Touching the water at all. What <c>CrimeWatch</c> asks before judging an impact.</summary>
        public bool InWater => Submersion > wadeAt;

        /// <summary>The engine has taken water and will not restart. Read by <c>GameAudio</c>.</summary>
        public bool EngineDrowned => State == Stage.Flooding || State == Stage.Sunk;

        /// <summary>
        /// Gives <paramref name="vehicle"/> the component if it has not got one. Idempotent, and safe
        /// after a mid-Play recompile - <c>TryGetComponent</c> rather than <c>??</c>, because a
        /// missing component is Unity's fake-null and the coalescing form silently adds nothing.
        /// </summary>
        public static void Ensure(GameObject vehicle)
        {
            if (vehicle == null) return;
            if (!vehicle.TryGetComponent<WaterEntry>(out _)) vehicle.AddComponent<WaterEntry>();
        }

        /// <summary>
        /// Is this vehicle in the water right now? The question <c>CrimeWatch</c> and the police both
        /// ask, phrased so neither has to know the component exists on a given body.
        /// </summary>
        public static bool IsInWater(Component vehicle) =>
            vehicle != null &&
            vehicle.TryGetComponent<WaterEntry>(out var water) &&
            water.InWater;

        private void Awake() => Bind();

        /// <summary>
        /// Puts it back on dry land, in state as well as in position. Called by both controllers'
        /// <c>Respawn</c> and <c>Teleport</c>: a car recovered from the seabed that kept its flood
        /// timer would sink again on the tarmac, with a dead engine and no water in sight.
        /// </summary>
        public void RestoreFromWater()
        {
            State = Stage.Dry;
            Submersion = 0f;
            _flood = 0f;
            _sinceDeep = 0f;
            _sinceSunk = 0f;

            SetDrowned(false);
        }

        private void Bind()
        {
            _body = GetComponent<Rigidbody>();
            TryGetComponent(out _car);
            TryGetComponent(out _bike);

            var snapshot = TheBlockConfig.Load();
            _sea = snapshot?.Config?.Sea;

            MeasureHull();
        }

        /// <summary>
        /// The hull, in this body's own local space.
        ///
        /// Both builders give the root a single <see cref="BoxCollider"/> (<c>CarBuilder</c>:531,
        /// <c>MotorcycleBuilder</c>:382), so the hull IS that box and no estimation is needed. The
        /// fallback exists for anything that arrives here without one, and it deliberately reads
        /// renderer bounds rather than collider bounds - a WheelCollider reports none.
        /// </summary>
        private void MeasureHull()
        {
            if (TryGetComponent<BoxCollider>(out var box))
            {
                _hullCenter = box.center;
                _hullExtents = box.size * 0.5f;
                return;
            }

            var bounds = new Bounds(transform.position, Vector3.zero);
            bool any = false;
            foreach (var renderer in GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            if (!any) { _hullCenter = Vector3.zero; _hullExtents = Vector3.one; return; }

            _hullCenter = transform.InverseTransformPoint(bounds.center);
            _hullExtents = bounds.extents;
        }

        private void FixedUpdate()
        {
            if (_sea?.Beach == null) { Bind(); return; }
            if (_body == null) return;

            Submersion = Measure();

            // Only WADING is reversible. A vehicle that has already drowned and been washed onto the
            // sand is not dry again - it is a wreck standing in the shallows, and it has to keep
            // ticking or its retire timer never runs and it sits there for the rest of the session
            // with a dead engine and no way to get rid of it.
            if (State == Stage.Dry)
            {
                if (Submersion <= wadeAt) return;
                State = Stage.Wading;
            }
            else if (State == Stage.Wading && Submersion <= wadeAt)
            {
                LeaveWater();
                return;
            }

            if (Submersion > 0f) ApplyWaterPhysics();
            Advance(Time.fixedDeltaTime);
        }

        /// <summary>
        /// How much of the hull is under the drawn water surface, 0 to 1.
        ///
        /// Zero outside the sea's own rectangle: the water plane is 2400 m wide but only 600 m of
        /// coast, and a car in the car park is not floating. <see cref="SeaGeometry.InSeaRegion"/> is
        /// the one place that test - and its handedness - lives.
        /// </summary>
        private float Measure()
        {
            var p = transform.TransformPoint(_hullCenter);
            if (!SeaGeometry.InSeaRegion(_sea, p.x, p.z)) return 0f;

            float draft = Mathf.Max(0.05f, _hullExtents.y * 2f);
            float bottom = p.y - _hullExtents.y;

            // Sampled at the hull's centre for the depth reading, which is what the stages key off;
            // the four corners are for the FORCE, below, where their spread is what rights the body.
            float surface = SeaSurface.Height(p.x, p.z);
            return Mathf.Clamp01((surface - bottom) / draft);
        }

        /// <summary>
        /// Upthrust at four points, plus the drag that makes water feel like water.
        ///
        /// Four points rather than one because a single central force cannot right a hull: a car that
        /// goes in nose-first would stay nose-first all the way down. Spread across the footprint, the
        /// deeper corners push harder and the body levels itself, which is also what makes a flooding
        /// car settle heavy-end-first instead of sinking flat.
        /// </summary>
        private void ApplyWaterPhysics()
        {
            float lift = buoyancy * Mathf.Max(0f, 1f - _flood) * _body.mass * -Physics.gravity.y * 0.25f;

            for (int i = 0; i < 4; i++)
            {
                var local = _hullCenter + new Vector3(
                    _hullExtents.x * floatSpread * ((i & 1) == 0 ? -1f : 1f),
                    -_hullExtents.y,
                    _hullExtents.z * floatSpread * ((i & 2) == 0 ? -1f : 1f));

                var point = transform.TransformPoint(local);
                float depth = SeaSurface.Height(point.x, point.z) - point.y;
                if (depth <= 0f) continue;

                float share = Mathf.Clamp01(depth / Mathf.Max(0.05f, _hullExtents.y * 2f));
                _body.AddForceAtPosition(Vector3.up * (lift * share), point, ForceMode.Force);
            }

            // Drag, as a force scaled by how much of the hull is actually in the water. Acceleration
            // mode, so it is mass-free and the Mustang and the scooter slow at the same rate - which
            // is what a swimmer would expect and what an impulse tuned on one of them would not give.
            _body.AddForce(-_body.linearVelocity * (waterDrag * Submersion), ForceMode.Acceleration);
            _body.AddTorque(-_body.angularVelocity * (waterAngularDrag * Submersion),
                ForceMode.Acceleration);
        }

        /// <summary>The stage machine. One direction only - nothing here dries a drowned engine out.</summary>
        private void Advance(float dt)
        {
            switch (State)
            {
                case Stage.Wading:
                    // Sustained depth, not contact. A bow wave over the bonnet at 80 km/h is not the
                    // same event as sitting in two metres of water, and the timer is what tells them
                    // apart without a second sensor.
                    _sinceDeep = Submersion >= drownAt ? _sinceDeep + dt : 0f;
                    if (_sinceDeep >= (_bike != null ? bikeDrownSeconds : carDrownSeconds)) Drown();
                    break;

                case Stage.Flooding:
                    _flood = Mathf.Clamp01(_flood + dt / Mathf.Max(0.01f, sinkSeconds));
                    if (Submersion >= bailOutAt) BailOutDriver();
                    if (_flood >= 1f && _body.linearVelocity.sqrMagnitude < 0.25f) State = Stage.Sunk;
                    break;

                case Stage.Sunk:
                    // Only once it is nobody's ride: retiring a car with the player still parented
                    // into its seat teleports him across the map still attached to it. Deep water
                    // throws him out; a car finished in knee-deep water is left for him to step out
                    // of, because forcing a bail-out there reads as the game taking the wheel.
                    if (Driven)
                    {
                        if (Submersion >= bailOutAt) BailOutDriver();
                        _sinceSunk = 0f;
                        break;
                    }

                    _sinceSunk += dt;
                    if (_sinceSunk >= retireSeconds) Retire();
                    break;
            }
        }

        private bool Driven =>
            (_car != null && _car.Driven) || (_bike != null && _bike.Driven);

        /// <summary>
        /// The engine takes water. Steering and rolling survive - the vehicle is not switched off,
        /// its throttle is - for the reason <c>CarController.EngineDead</c> spells out: a disabled
        /// controller stops applying the coast brake too, and a WheelCollider LATCHES the last torque
        /// it was handed (memory: <c>wheelcollider-latches-last-torque</c>).
        ///
        /// A bike throws its rider at the same instant, which is GTA's own rule and is also the only
        /// honest outcome: there is no cabin to sit in and nothing to steer.
        /// </summary>
        private void Drown()
        {
            State = Stage.Flooding;
            SetDrowned(true);
            if (_bike != null) BailOutDriver();
        }

        /// <summary>
        /// Out of the seat and into the water. <see cref="VehicleEnterExit.LeaveVehicleNow"/> is the
        /// scripted exit U21 already needed, so this adds no second way out of a vehicle - which is
        /// the kind of thing that ends with the player parented to a car he is not in.
        ///
        /// <see cref="Player.PlayerController"/> takes it from here: it decides the swim state by
        /// POSITION every frame, and its capsule already ignores the sea's wall layer.
        /// </summary>
        private void BailOutDriver()
        {
            if (!Driven) return;
            if (_vehicles == null) _vehicles = FindAnyObjectByType<VehicleEnterExit>();
            if (_vehicles == null || _vehicles.Mode != GameMode.Driving) return;
            if (_vehicles.ActiveVehicle?.GetTransform() != transform) return;

            _vehicles.LeaveVehicleNow();
        }

        /// <summary>
        /// The car you drowned is gone. <c>IEnterable.Respawn</c> puts it back where it lives - the
        /// lot for a player's car, the station bay for a cruiser - which is GTA's answer too and is
        /// also the only one that does not leave the seabed filling up with wrecks nobody can reach.
        /// </summary>
        private void Retire()
        {
            RestoreFromWater();

            // GetComponent rather than TryGetComponent: this is an INTERFACE, and `!= null` on an
            // interface reference does not go through Unity's overloaded operator. Nothing here is
            // destroyed, so a plain reference check is the honest one.
            var enterable = GetComponent<IEnterable>();
            enterable?.Respawn();
        }

        /// <summary>Back on dry land under its own power - it only ever waded.</summary>
        private void LeaveWater()
        {
            State = Stage.Dry;
            _sinceDeep = 0f;
        }

        /// <summary>
        /// A flag of its own rather than a second writer on <c>EngineDead</c>.
        ///
        /// <c>VehicleDamage</c> assigns <c>EngineDead</c> from the vehicle's condition, so a second
        /// owner would be overwritten the next time it wrote - the exact failure memory
        /// <c>one-flag-one-owner-heat-frozen</c> records, where the burner phone did nothing outside
        /// the pizzeria. One flag, one owner; the controllers read both.
        /// </summary>
        private void SetDrowned(bool drowned)
        {
            if (_car != null) _car.Drowned = drowned;
            if (_bike != null) _bike.Drowned = drowned;
        }
    }
}
