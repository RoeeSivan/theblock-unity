using TheBlock.Audio;
using TheBlock.Core;
using TheBlock.Game;
using TheBlock.Npc;
using TheBlock.Police;
using TheBlock.Vfx;
using TheBlock.World;
using UnityEngine;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// U35b layer ②, and the unit's one gameplay component: a car has a condition, and crashing costs
    /// it.
    ///
    /// <b>The argument for the whole unit.</b> The web build's cars are kinematic - a crash there is a
    /// number that fires a sound and, since U34, a star. Nothing about the car changes, because
    /// nothing about the car CAN change: its shell is a merged mesh drawn by a renderer that has no
    /// idea it was hit. Here the shell dents (<see cref="DeformableBody"/>), pieces come off
    /// (<see cref="DetachableParts"/>) and the car itself can stop being a car.
    ///
    /// <b>Every car reports its own impacts.</b> Both bodies in a collision get their own
    /// <c>OnCollisionEnter</c>, so a T-bone raises <c>CrashSensor.Crashed</c> twice - once per car,
    /// each with its own closing speed and its own contact - and this component simply ignores every
    /// impact that is not its own. There is deliberately no code anywhere that damages the OTHER car
    /// in a collision; that would be a second, disagreeing account of the same crash.
    ///
    /// <b>Attached at runtime by the car itself</b>, the same arrangement <c>CrashSensor</c> uses and
    /// for the same reason: a prefab field is regenerated away the next time
    /// <c>The Block → Build Drivable Cars</c> runs. The serialized references below are the two things
    /// that CANNOT be found at runtime - which panels may dent and which nodes may come off - so those
    /// are wired by the builder, on the prefab, where the model is visible.
    ///
    /// <b>The fuse is the user's call, made 2026-08-16.</b> A car that reaches zero with the player in
    /// it burns for three seconds and then goes up; you get out with E, or you are put down beside it
    /// unharmed at the last moment. It is deliberately NOT a ragdoll: U35a settled that a car does not
    /// eject you, and a showcase feature does not get to quietly reverse a decision made about how the
    /// game feels.
    /// </summary>
    [DisallowMultipleComponent]
    public class VehicleDamage : MonoBehaviour
    {
        [Header("Taking damage")]
        [Tooltip("Closing speed below which a crash costs nothing, m/s. GameAudio's crash floor is " +
                 "3 m/s - a kerb worth no sound is worth no damage either.")]
        [SerializeField] private float minDamageSpeed = 5f;

        [Tooltip("Metres per second of closing speed that would cost a whole car, if one impact " +
                 "could. It cannot - see maxPerImpact.")]
        [SerializeField] private float speedForFullDamage = 45f;

        [Tooltip("Most condition one impact may take. Three real crashes kill a car; one cannot.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float maxPerImpact = 0.4f;

        [Tooltip("Hitting another vehicle hurts more than hitting a wall - there is a second bumper " +
                 "coming the other way.")]
        [SerializeField] private float vehicleMultiplier = 1.4f;

        [Tooltip("Closing speed, m/s, at which a dent is at full depth. Not the same number as " +
                 "speedForFullDamage: the look saturates long before the health does.")]
        [SerializeField] private float speedForFullDent = 16f;

        [Tooltip("Dent severity above which a part may be knocked off.")]
        [Range(0f, 1f)]
        [SerializeField] private float partThreshold = 0.45f;

        [Header("Condition")]
        [Tooltip("Condition at or below which the engine smokes.")]
        [Range(0f, 1f)]
        [SerializeField] private float smokeAt = 0.5f;

        [Tooltip("Condition at or below which it burns.")]
        [Range(0f, 1f)]
        [SerializeField] private float fireAt = 0.2f;

        [Tooltip("Seconds between reaching zero and the explosion. The window to get out.")]
        [SerializeField] private float fuseSeconds = 3f;

        [Tooltip("Seconds before the blast at which a player still in the seat is put down beside " +
                 "the car. Enough to be clear of it, short enough to feel like the last moment.")]
        [SerializeField] private float bailOutAt = 0.4f;

        [Tooltip("Seconds the burnt husk stays before the car respawns whole.")]
        [SerializeField] private float huskSeconds = 20f;

        [Header("The blast")]
        [Tooltip("Metres. Everything with a Rigidbody inside this is thrown.")]
        [SerializeField] private float blastRadius = 8f;

        [Tooltip("Newtons at the centre, falling to nothing at the rim.")]
        [SerializeField] private float blastForce = 9000f;

        [Tooltip("How much of the force lifts rather than shoves. AddExplosionForce's own parameter.")]
        [SerializeField] private float blastLift = 0.5f;

        [Tooltip("Condition taken off other cars caught in the blast. Under 1 on purpose: a chain " +
                 "reaction should need cars that were ALREADY damaged, not a single spark.")]
        [Range(0f, 1f)]
        [SerializeField] private float chainDamage = 0.5f;

        [Tooltip("Metres per second handed to a pedestrian at the centre of the blast, falling off " +
                 "to the rim. Read by RunOverSystem exactly as a bumper's speed is.")]
        [SerializeField] private float blastThrowSpeed = 11f;

        [Header("Wiring - set by CarBuilder")]
        [SerializeField] private DeformableBody deform;
        [SerializeField] private DetachableParts parts;

        [Header("Debug - stripped by U30c")]
        [Tooltip("J takes a quarter off the vehicle you are driving. Four presses is an explosion.")]
        [SerializeField] private bool debugKey = true;

        // --- runtime -------------------------------------------------------------------------------

        private CarController _car;
        private Rigidbody _body;
        private CrashSensor _sensor;
        private BoxCollider _chassis;
        private VehicleEnterExit _vehicles;
        private Heat _heat;
        private RunOverSystem _runOver;
        private MaterialPropertyBlock _block;
        private Renderer[] _renderers;
        private UI.MissionHud _hud;

        private float _health = 1f;
        private float _fuse = -1f;
        private float _husk = -1f;
        private bool _dead;
        private bool _blown;
        private bool _playerWasDriving;
        private BurnLevel _burn;
        private bool _burning;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>1 is showroom, 0 is a fireball. Read by nothing yet; the smoke is the readout.</summary>
        public float Health => _health;

        /// <summary>True from the moment the condition hits zero - engine dead, fuse lit.</summary>
        public bool Dead => _dead;

        /// <summary>
        /// True once it has actually gone up: a burnt husk, not a car. Read by
        /// <see cref="CarController.EntryRefusal"/>, which is what tells the player why E does
        /// nothing here.
        /// </summary>
        public bool Wrecked => _blown;

        /// <summary>
        /// Gives <paramref name="vehicle"/> a damage model if it has not got one. Idempotent, and
        /// safe after a mid-Play recompile - <c>TryGetComponent</c> rather than <c>??</c>, because a
        /// missing component comes back as Unity's fake-null (memory:
        /// <c>unity-null-coalescing-fake-null</c>).
        /// </summary>
        public static void Ensure(GameObject vehicle)
        {
            if (vehicle == null) return;
            if (!vehicle.TryGetComponent<VehicleDamage>(out _)) vehicle.AddComponent<VehicleDamage>();
        }

        /// <summary>Editor-side wiring, used by <c>CarBuilder</c>.</summary>
        public void Configure(DeformableBody body, DetachableParts detachable)
        {
            deform = body;
            parts = detachable;
        }

        private void Awake() => Bind();

        private void OnEnable() => CrashSensor.Crashed += OnCrashed;

        private void OnDisable() => CrashSensor.Crashed -= OnCrashed;

        private void Bind()
        {
            TryGetComponent(out _car);
            TryGetComponent(out _body);
            TryGetComponent(out _chassis);
            if (deform == null) TryGetComponent(out deform);
            if (parts == null) TryGetComponent(out parts);
            _block ??= new MaterialPropertyBlock();
        }

        // --- taking a hit ---------------------------------------------------------------------------

        private void OnCrashed(CrashSensor.Impact impact)
        {
            if (!Progress.VehicleDamageOn) return;
            if (_dead) return;

            // The sensor is added at runtime by CarController.Bind, which may not have run when this
            // component first enabled - so it is resolved on use rather than cached at Awake.
            if (_sensor == null && !TryGetComponent(out _sensor)) return;
            if (impact.Sensor != _sensor) return;

            float over = impact.ClosingSpeed - minDamageSpeed;
            if (over <= 0f) return;

            float dentSeverity = Mathf.Clamp01(over / Mathf.Max(0.1f, speedForFullDent));

            if (deform != null) deform.Dent(impact.Point, impact.Normal, dentSeverity);
            if (parts != null && dentSeverity >= partThreshold) parts.Shed(impact.Point, impact.Normal, dentSeverity);

            float amount = Mathf.Min(maxPerImpact,
                over / Mathf.Max(0.1f, speedForFullDamage) * (impact.HitVehicle ? vehicleMultiplier : 1f));

            Hurt(amount);
        }

        /// <summary>
        /// Take condition off this car. Public because a blast next door is the other way a car gets
        /// hurt, and that one has no contact point to report.
        /// </summary>
        public void Hurt(float amount)
        {
            if (_dead || amount <= 0f || !Progress.VehicleDamageOn) return;

            // Visual mode floors the condition just above zero: the car dents, smokes and burns and
            // is still a car you can drive home. That is the whole difference between the two modes.
            float floor = Progress.VehicleDamageLethal ? 0f : 0.05f;
            _health = Mathf.Max(floor, _health - amount);

            if (_health <= 0f) Die();
        }

        // --- the frame ------------------------------------------------------------------------------

        private void Update()
        {
            if (_car == null && _body == null) Bind();

            // The setting can be turned off with a smoking wreck on screen. Nothing else is watching
            // for that, so the car heals itself here rather than the menu reaching into every car in
            // the world to undo what it did.
            if (!Progress.VehicleDamageOn)
            {
                if (_health < 1f || _dead) Repair();
                PollDebugKey();
                return;
            }

            PollDebugKey();
            TickBurn();
            TickFuse(Time.deltaTime);
        }

        /// <summary>
        /// Says what this car looks like, every frame, rather than tracking transitions.
        ///
        /// <see cref="DamageFx.Attach"/> is idempotent per owner, so restating the condition costs one
        /// comparison - and it means a slot stolen by another car is quietly reclaimed on the next
        /// frame instead of being lost until the next impact.
        /// </summary>
        private void TickBurn()
        {
            BurnLevel? wanted = _health <= fireAt || _dead ? BurnLevel.Fire
                : _health <= smokeAt ? BurnLevel.Smoke
                : null;

            if (wanted == null)
            {
                if (!_burning) return;
                _burning = false;
                DamageFx.Instance.Detach(this);
                return;
            }

            if (_burning && wanted.Value == _burn) return;
            _burn = wanted.Value;
            _burning = true;
            DamageFx.Instance.Attach(this, transform, BonnetOffset(), _burn);
        }

        /// <summary>
        /// Where the smoke comes from: the top of the bonnet, in the car's own frame.
        ///
        /// Measured off the chassis box that <c>CarBuilder</c> built, so it is right on a Mustang and
        /// on a Tesla without either being named here. The fallback covers a vehicle with no box -
        /// nothing in the game today, but a bike would arrive that way.
        /// </summary>
        private Vector3 BonnetOffset()
        {
            if (_chassis == null) return Vector3.up * 0.9f;
            return _chassis.center
                   + Vector3.forward * (_chassis.size.z * 0.34f)
                   + Vector3.up * (_chassis.size.y * 0.5f);
        }

        private void TickFuse(float dt)
        {
            if (_fuse > 0f)
            {
                _fuse -= dt;

                // Put the player down beside the car just before it goes. Not a ragdoll: U35a's rule.
                if (_fuse <= bailOutAt && _car != null && _car.Driven)
                {
                    if (_vehicles == null) _vehicles = FindAnyObjectByType<VehicleEnterExit>();
                    if (_vehicles != null) _vehicles.LeaveVehicleNow();
                }

                if (_fuse <= 0f) Explode();
                return;
            }

            if (_husk <= 0f) return;

            _husk -= dt;
            if (_husk > 0f) return;

            // The husk's time is up. Repair first, THEN respawn: repair puts the shed door back and
            // hands the dented meshes in, and respawn is the existing R-key path that stands the car
            // up at its own spawn with its wheels re-posed.
            Repair();
            if (_car != null) _car.Respawn();
        }

        // --- dying -----------------------------------------------------------------------------------

        private void Die()
        {
            if (_dead) return;
            _dead = true;
            _playerWasDriving = _car != null && _car.Driven;

            if (_car != null) _car.EngineDead = true;
            _fuse = Mathf.Max(0.1f, fuseSeconds);
        }

        /// <summary>
        /// The blast. Everything in it is gameplay, which is why none of it lives in
        /// <see cref="DamageFx"/> - that file draws, this one decides.
        /// </summary>
        private void Explode()
        {
            if (_blown) return;
            _blown = true;
            _fuse = -1f;
            _husk = huskSeconds;

            var at = _chassis != null ? transform.TransformPoint(_chassis.center) : transform.position;

            DamageFx.Instance.Burst(at);
            GameAudio.Explosion(at);

            // Throw everything with a body. The car itself is included deliberately - a wreck that
            // does not move is a car that was switched off, not one that exploded.
            var caught = Physics.OverlapSphere(at, blastRadius, ~0, QueryTriggerInteraction.Ignore);
            foreach (var hit in caught)
            {
                var other = hit.attachedRigidbody;
                if (other == null || other.isKinematic) continue;
                other.AddExplosionForce(blastForce, at, blastRadius, blastLift, ForceMode.Force);
            }

            // Parked fillers are STATIC colliders with no body at all, so the sweep above cannot see
            // them. U34 gave them a promotion path off a collision; this is the same promotion with
            // no collision behind it.
            foreach (var filler in LotCar.All)
            {
                if (filler == null || filler.Wrecked) continue;
                float distance = Vector3.Distance(filler.transform.position, at);
                if (distance > blastRadius) continue;
                filler.Blast(at, blastForce * (1f - distance / blastRadius));
            }

            // Other cars in range take condition, which is what makes a row of parked cars go up one
            // after another - but only if they were already hurt. chainDamage is under 1 for exactly
            // that reason.
            foreach (var hit in caught)
            {
                var neighbour = hit.attachedRigidbody != null
                    ? hit.attachedRigidbody.GetComponent<VehicleDamage>()
                    : null;
                if (neighbour == null || neighbour == this) continue;

                float distance = Vector3.Distance(neighbour.transform.position, at);
                neighbour.Hurt(chainDamage * Mathf.Clamp01(1f - distance / blastRadius));
            }

            if (_runOver == null) _runOver = FindAnyObjectByType<RunOverSystem>();
            if (_runOver != null) _runOver.Blast(at, blastRadius, blastThrowSpeed);

            Wanted(at);
            Announce();
            Char();
        }

        /// <summary>
        /// Tell the player their car is gone - U35b, the user's call on 2026-08-16.
        ///
        /// <b>Only for the car they were driving.</b> The fireball, the flash and the blast are the
        /// feedback for everything else that goes up in this city, and a line on the HUD every time a
        /// cruiser cooks off in a pursuit would be noise on top of the one moment it should mark. The
        /// hint fades itself after four seconds, which is <c>MissionHud</c>'s own clock.
        ///
        /// The player is already standing beside it by now: the fuse put them out at t-0.4 s.
        /// </summary>
        private void Announce()
        {
            if (!_playerWasDriving) return;

            if (_hud == null) _hud = FindAnyObjectByType<UI.MissionHud>();
            if (_hud == null) return;

            _hud.ShowHint("Your car exploded. Find another one.");
        }

        /// <summary>
        /// A car going up in the street is a crime - when it is YOUR car, or when you are standing
        /// close enough to have done it.
        ///
        /// <c>Heat.Bump</c> is the existing hook and it already refuses while <c>Frozen</c> or
        /// <c>Immune</c>, so this adds no second writer of either (memory:
        /// <c>one-flag-one-owner-heat-frozen</c>). A cruiser exploding on its own does not pay a star:
        /// the police blowing themselves up is not the player's doing.
        /// </summary>
        private void Wanted(Vector3 at)
        {
            if (_car != null && _car.IsPolice && !_playerWasDriving) return;

            if (_heat == null) _heat = FindAnyObjectByType<Heat>();
            if (_heat == null) return;

            bool mine = _playerWasDriving;
            if (!mine)
            {
                var player = _vehicles != null ? _vehicles : FindAnyObjectByType<VehicleEnterExit>();
                _vehicles = player;
                mine = player != null && Vector3.Distance(player.transform.position, at) <= blastRadius + 6f;
            }

            if (mine) _heat.Bump();
        }

        /// <summary>
        /// Burn the paint. A <c>MaterialPropertyBlock</c>, never a material edit: the paint on this car
        /// is a generated ASSET shared with every other car wearing that colour (see
        /// <see cref="CarPaint"/>), so darkening the material would char the whole lot - and the ones
        /// still parked in the Editor's own project folder.
        /// </summary>
        private void Char()
        {
            _renderers ??= GetComponentsInChildren<Renderer>(true);
            _block ??= new MaterialPropertyBlock();

            foreach (var renderer in _renderers)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, new Color(0.09f, 0.08f, 0.08f, 1f));
                renderer.SetPropertyBlock(_block);
            }
        }

        // --- repair -----------------------------------------------------------------------------------

        /// <summary>
        /// Whole again: condition, dents, parts, paint and the engine.
        ///
        /// Called by <c>CarController.Respawn</c> (R repairs, which is the play-test's own undo), by
        /// the husk clock, and by <see cref="Update"/> when the setting is switched off with a wreck
        /// on screen.
        /// </summary>
        public void Repair()
        {
            _health = 1f;
            _dead = false;
            _blown = false;
            _fuse = -1f;
            _husk = -1f;
            _playerWasDriving = false;

            if (_car != null) _car.EngineDead = false;
            if (deform != null) deform.Restore();
            if (parts != null) parts.Repair();

            if (_burning)
            {
                _burning = false;
                DamageFx.Instance.Detach(this);
            }

            if (_renderers == null) return;

            // Clearing the block rather than writing white back: white is not "no tint", it is a
            // white base colour, and a car whose paint was never charred must not gain one.
            foreach (var renderer in _renderers)
                if (renderer != null) renderer.SetPropertyBlock(null);
        }

        /// <summary>
        /// A carjacked filler is destroyed and replaced mid-session, so a smoking car can leave with
        /// its slot still held. <see cref="DamageFx.Existing"/> rather than <c>Instance</c>: this runs
        /// during teardown, where creating a GameObject is an error.
        /// </summary>
        private void OnDestroy()
        {
            var fx = DamageFx.Existing;
            if (_burning && fx != null) fx.Detach(this);
        }

        // --- the debug key --------------------------------------------------------------------------

        /// <summary>
        /// <c>J</c> - a quarter of the car's condition, on whatever the player is driving. Four
        /// presses reach the fuse, which is how the whole chain is reachable in a play-test without
        /// having to find a wall four times.
        ///
        /// Only on the car the player is IN, so holding it down does not quietly detonate the traffic.
        /// U30c strips it with the rest of the debug keys.
        /// </summary>
        private void PollDebugKey()
        {
            if (!debugKey || Pause.Frozen) return;
            if (_car == null || !_car.Driven) return;
            if (!Progress.VehicleDamageOn) return;

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null || !keyboard.jKey.wasPressedThisFrame) return;

            Hurt(0.25f);
        }
    }
}
