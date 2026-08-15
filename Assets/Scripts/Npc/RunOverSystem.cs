using TheBlock.Core;
using TheBlock.Traffic;
using TheBlock.Vehicles;
using TheBlock.Vfx;
using UnityEngine;

namespace TheBlock.Npc
{
    /// <summary>
    /// Who the player just ran over. The port of <c>crowd.runOver()</c> plus the frame-loop gate in
    /// the original's <c>main.ts</c>.
    ///
    /// <b>ONE detector, and it is the bumper box.</b> The original shipped two — this test, and a
    /// separate radius scan in <c>crime.ts</c> that decided whether to call the police — and they
    /// fought: the radius scan skipped anyone already yielding to the car bearing down on them, and
    /// wanted the victim within 1.8 m of the vehicle CENTRE while the box downs them at about 3.2 m.
    /// The result was blood on the road and, usually, no stars. That call is dead and is not being
    /// ported back: U19's wanted level will read <see cref="Victims"/> from here.
    ///
    /// <b>In FixedUpdate, and padded by a step's travel.</b> A person's capsule is solid, so a car
    /// arriving at 20 m/s would hit a wall for one physics step before anything downed them. Firing
    /// on the step BEFORE contact is what makes the hit feel like a hit rather than a stop — the
    /// bumper box is padded by the victim's own body radius (a person is a capsule, not a point,
    /// which is what stopped a fender-corner clip doing nothing in the web build), plus how far the
    /// vehicle will move before the next step. Below <see cref="minSpeedKmh"/> none of that happens
    /// and the capsule stays solid, so a crawl still bumps into people instead of gliding through
    /// them.
    /// </summary>
    public class RunOverSystem : MonoBehaviour
    {
        [Header("From run-over.config.ts")]
        [Tooltip("Below this the vehicle is nudging, not running someone over. The single gate for " +
                 "both the reaction and (at U19) the wanted star. minSpeedKmh.")]
        [SerializeField] private float minSpeedKmh = 12f;

        [Tooltip("Padding in metres around the bumper box, on top of the victim's body radius. boxPad.")]
        [SerializeField] private float boxPad = 0.35f;

        [SerializeField] private RunOverReaction.Tuning reaction = RunOverReaction.Tuning.Default;

        [Header("Unity-only")]
        [Tooltip("Metres of height difference past which a vehicle and a person are simply not at the " +
                 "same place. The web build never needed this because its city is one storey.")]
        [SerializeField] private float sameStoreyBand = 3f;

        [Tooltip("Logs every hit with the numbers behind it — victim, speed, and how far the body was " +
                 "actually thrown. This is how the knockback was measured; leave it on until it is.")]
        [SerializeField] private bool logHits = true;

        [Header("Wiring")]
        [SerializeField] private CrowdSpawner crowd;
        [SerializeField] private VehicleEnterExit vehicles;
        [SerializeField] private Blood blood;

        private bool _warnedNoClip;

        /// <summary>How many people this session has run over. U19's wanted level reads it.</summary>
        public int Victims { get; private set; }

        /// <summary>Fired with the number downed on a step, on the steps where that is not zero.</summary>
        public event System.Action<int> RanOver;

        private void Awake() => Bind();

        private void Bind()
        {
            if (crowd == null) crowd = GetComponent<CrowdSpawner>();
            if (crowd == null) crowd = FindAnyObjectByType<CrowdSpawner>();
            if (vehicles == null) vehicles = FindAnyObjectByType<VehicleEnterExit>();
            if (blood == null) blood = GetComponent<Blood>();
            if (blood == null) blood = FindAnyObjectByType<Blood>();
        }

        private void FixedUpdate()
        {
            if (crowd == null || vehicles == null) { Bind(); return; }
            if (vehicles.Mode == GameMode.OnFoot) return;

            var vehicle = vehicles.ActiveVehicle;
            if (vehicle == null) return;

            float speedMs = vehicle.ForwardSpeed;
            if (Mathf.Abs(speedMs) * 3.6f < minSpeedKmh) return;

            var anchor = vehicle.GetTransform();
            if (!TryBox(anchor, out var box)) return;

            // Reversing over someone counts, and the box is symmetric, so the throw follows the
            // direction of TRAVEL rather than the nose.
            var travel = speedMs >= 0f ? anchor.forward : -anchor.forward;
            float lead = Mathf.Abs(speedMs) * Time.fixedDeltaTime;

            int downed = 0;
            var people = crowd.Crowd;

            for (int i = 0; i < people.Count; i++)
            {
                var person = people[i];
                if (person == null || person.Downed) continue;

                var at = person.transform.position;
                if (Mathf.Abs(at.y - anchor.position.y) > sameStoreyBand) continue;
                if (!TrafficGeometry.PointInBox(box, at.x, at.z, boxPad + person.BodyRadius + lead)) continue;

                person.KnockDown(travel, speedMs, reaction, blood);
                if (!person.Downed)
                {
                    WarnNoClip();
                    continue;
                }

                downed++;
                if (logHits) person.LogHit(speedMs);
            }

            if (downed == 0) return;

            Victims += downed;
            RanOver?.Invoke(downed);
        }

        /// <summary>
        /// The vehicle's own bumper box, straight off its chassis collider.
        ///
        /// Measured rather than configured, and it has to be: the car's box is the Mustang's measured
        /// bounds while the bike's WIDTH is deliberately overridden to 0.5 m (bike plus rider, not the
        /// span of the mirrors) — so a bike that hit as wide as a car would mow down the pavement on
        /// both sides of it.
        /// </summary>
        private static bool TryBox(Transform vehicle, out TrafficGeometry.Box box)
        {
            box = default;
            if (vehicle == null || !vehicle.TryGetComponent<BoxCollider>(out var collider)) return false;

            var scale = vehicle.lossyScale;
            var centre = vehicle.TransformPoint(collider.center);
            var forward = vehicle.forward;

            box.X = centre.x;
            box.Z = centre.z;
            box.Fx = forward.x;
            box.Fz = forward.z;
            box.HalfLen = collider.size.z * 0.5f * Mathf.Abs(scale.z);
            box.HalfW = collider.size.x * 0.5f * Mathf.Abs(scale.x);

            // The forward the box projects onto must be a unit vector in the XZ plane, or every
            // distance it measures is short by the cosine of the vehicle's pitch.
            float length = Mathf.Sqrt(box.Fx * box.Fx + box.Fz * box.Fz);
            if (length < 1e-4f) return false;
            box.Fx /= length;
            box.Fz /= length;
            return true;
        }

        private void WarnNoClip()
        {
            if (_warnedNoClip) return;
            _warnedNoClip = true;
            Debug.LogError(
                "RunOverSystem: a pedestrian was hit but has no knockdown clip, so nothing happened. " +
                "Run The Block → Import Hit Clip, then The Block → Build NPC Animator.", this);
        }
    }
}
