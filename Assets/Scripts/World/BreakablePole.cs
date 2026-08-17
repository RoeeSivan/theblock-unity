using System.Collections;
using TheBlock.Game;
using TheBlock.Traffic;
using TheBlock.Vfx;
using UnityEngine;

namespace TheBlock.World
{
    /// <summary>
    /// U35h - a traffic-light pole that goes over when a car rams it.
    ///
    /// <b>Added at RUNTIME to every <see cref="TrafficLightPole"/> root by <see cref="PropSystem"/></b>,
    /// the <c>CrashSensor.Ensure</c> way, so the 233 poles that <c>WorldBuilder.Traffic</c> already
    /// placed need no rebuild and a future Build World cannot forget them.
    ///
    /// <b>The <see cref="LotCar"/> promote pattern, on purpose.</b> A pole is a static capsule until
    /// it is hit - 233 sleeping bodies would be free too, but a pole is not a cone: hitting one at
    /// speed SHOULD be a crash. It stays on the Default layer while it stands, so <c>CrashSensor</c>
    /// sees the impact exactly as it did before this unit (a star above 6 m/s, a dent, the thump),
    /// and only once it is down does it move to the Props layer, where a lying pole is debris that
    /// wrecks no traffic car and mints no second star. That move happens ONE FRAME after the hit,
    /// because the car's own collision callback may run after this one in the same step and would
    /// otherwise find the pole already excused.
    ///
    /// <b>No joint.</b> A free body with its centre of mass 1.6 m up and a spin about the axis
    /// perpendicular to the blow reads as a topple: friction pins the foot for the first half
    /// second, then it slides. A hinge at the base with a break force would look the same at speed
    /// and cost create/break plumbing per pole.
    ///
    /// The lamps go dark through <see cref="TrafficLightPole.Down"/> - the shared OFF materials,
    /// never an instance - and <c>TrafficLightSystem</c> keeps painting the approach none the wiser.
    /// </summary>
    [RequireComponent(typeof(TrafficLightPole))]
    public sealed class BreakablePole : Breakable
    {
        [Tooltip("Relative speed, m/s, below which a bump against a pole is a bump.")]
        [SerializeField] private float breakSpeed = 7f;

        [Tooltip("Mass once it is a real body. Steel post plus head.")]
        [SerializeField] private float mass = 150f;

        [Tooltip("Height of the centre of mass, m. High enough to topple, low enough not to cartwheel.")]
        [SerializeField] private float centreOfMassHeight = 1.6f;

        [Tooltip("Initial tip rate, rad/s, about the base away from the blow.")]
        [SerializeField] private float tipSpin = 2f;

        [Tooltip("Share of the impact speed the whole pole leaves with, and its ceiling in m/s.")]
        [SerializeField] private float shoveShare = 0.25f;
        [SerializeField] private float shoveMax = 4f;

        private TrafficLightPole _pole;
        private CapsuleCollider _capsule;
        private int _homeLayer;

        private void Awake()
        {
            _pole = GetComponent<TrafficLightPole>();
            _capsule = GetComponent<CapsuleCollider>();
            _homeLayer = gameObject.layer;
            SetHome();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (Body != null || _pole == null) return;                 // already down
            if (!Progress.BreakablePropsOn) return;                     // Off = today's rigid post
            if (collision.rigidbody == null) return;                    // pedestrian capsule, or nothing
            if (collision.rigidbody.isKinematic) return;                // a traffic car under script control
            if (collision.collider != null && collision.collider.gameObject.layer == PropsLayer) return; // a flung bin
            if (collision.relativeVelocity.magnitude < breakSpeed) return;

            Break(collision);
        }

        /// <summary>
        /// Hands the pole to PhysX. Direction from the CONTACT POINT, not from the impulse or the
        /// relative velocity - <see cref="LotCar"/>'s argument: "away from where I was struck" cannot
        /// be backwards.
        /// </summary>
        private void Break(Collision collision)
        {
            var contact = collision.GetContact(0);
            var away = transform.position - contact.point;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) away = transform.forward;
            away.Normalize();

            var body = gameObject.AddComponent<Rigidbody>();
            body.mass = mass;
            body.angularDamping = 0.8f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.centerOfMass = Vector3.up * centreOfMassHeight;

            // ω = up × away tips the TOP toward `away`: (up × away) × up = away.
            body.angularVelocity = Vector3.Cross(Vector3.up, away) * tipSpin;
            body.linearVelocity = away * Mathf.Min(collision.relativeVelocity.magnitude * shoveShare, shoveMax);
            Body = body;

            _pole.Down = true;
            MarkDisturbed();

            var fx = DamageFx.Instance;
            if (fx != null) fx.Sparks(contact.point);

            StartCoroutine(FlipLayerNextFrame());
        }

        private IEnumerator FlipLayerNextFrame()
        {
            yield return null;
            // Root only: the collider lives here, and `CrashSensor` reads the collider's layer.
            if (Body != null && PropsLayer >= 0) gameObject.layer = PropsLayer;
        }

        public override bool HomeIsClear()
        {
            float height = _capsule != null ? _capsule.height : 4.5f;
            float radius = _capsule != null ? _capsule.radius * 0.8f : 0.15f;
            var up = HomeRotation * Vector3.up;
            var a = HomePosition + up * 0.3f;
            var b = HomePosition + up * (height - 0.3f);
            int mask = PropsLayer >= 0 ? ~(1 << PropsLayer) : ~0;
            return !Physics.CheckCapsule(a, b, radius, mask, QueryTriggerInteraction.Ignore);
        }

        public override void Restore()
        {
            if (Body != null)
            {
                // Deferred, never DestroyImmediate: this runs from a tick, not from a physics
                // callback, and the body is gone by the end of the frame with no step in between.
                Destroy(Body);
                Body = null;
            }

            gameObject.layer = _homeLayer;
            transform.SetPositionAndRotation(HomePosition, HomeRotation);
            Physics.SyncTransforms();

            if (_pole != null) _pole.Down = false;
            ClearDisturbed();
        }
    }
}
