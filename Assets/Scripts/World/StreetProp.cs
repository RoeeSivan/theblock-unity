using TheBlock.Audio;
using UnityEngine;

namespace TheBlock.World
{
    /// <summary>The three street props U35h places. Serialized on the prefab and in the layout.</summary>
    public enum PropKind
    {
        Cone,
        Bin,
        Bench,
    }

    /// <summary>
    /// U35h - a cone, a bin or a bench at the kerb: a real dynamic Rigidbody from the moment it
    /// spawns, put to sleep on its first frame and never woken by anything but contact.
    ///
    /// <b>Why a sleeping body and not the <see cref="LotCar"/> promote pattern.</b> A parked filler
    /// is a static collider until it is hit because 101 of them would otherwise be 101 bodies, and
    /// because a car hitting one SHOULD stop - it weighs 1,400 kg. A cone weighs three. If it were
    /// static, a car would hit an immovable post for one physics step and jolt, and a kinematic
    /// traffic car would drive straight through it (kinematic-static pairs are neither resolved nor
    /// reported). A sleeping dynamic body costs nothing to PhysX - a sleeping actor is not
    /// integrated, only touched - and 3-60 kg against a car is a 0-4% dip in its speed, which is
    /// what a cone under the bumper feels like. Explosions find it too, for free.
    ///
    /// <b>Boxes, not capsules.</b> A capsule standing on its end has one contact point and topples
    /// on any pavement slope the moment it is woken; a box sits.
    ///
    /// It lives on the <see cref="Breakable.PropsLayerName"/> layer, which is what keeps it out of
    /// <c>CrashSensor</c> (no crime, no dent, no crash thump), out of <c>TrafficCar</c>'s and
    /// <c>LotCar</c>'s wreck tests, and out of pedestrians' way (<see cref="PropSystem"/> sets the
    /// layer pair to ignore each other, because the crowd's bodies are kinematic and a walking
    /// pedestrian would otherwise shove every bench they pass).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class StreetProp : Breakable
    {
        [SerializeField] private PropKind kind;

        [Tooltip("Relative speed, m/s, above which a touch is worth a clatter.")]
        [SerializeField] private float clatterSpeed = 1.5f;

        [Tooltip("Relative speed at which the clatter is at full volume.")]
        [SerializeField] private float clatterLoudSpeed = 12f;

        private BoxCollider _box;

        public PropKind Kind => kind;

        /// <summary>Set by the prefab builder.</summary>
        public void Configure(PropKind propKind) => kind = propKind;

        private void Awake()
        {
            Body = GetComponent<Rigidbody>();
            _box = GetComponent<BoxCollider>();

            // `Instantiate(prefab, position, rotation)` runs Awake with the transform already placed,
            // and Awake runs BEFORE any physics step - Start does not: a FixedUpdate can fall between
            // the spawn and Start, and a bin on a pavement's slight slope was found tilted by then,
            // with the tilt captured as home. So home is taken here, and the body goes to sleep here,
            // before PhysX has integrated it once.
            SetHome();
            if (Body != null) Body.Sleep();
        }

        private void Start()
        {
            if (Body != null && !Disturbed) Body.Sleep();
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Bumped by something that moves - a car, a bike, another prop already flying. The
            // static world is not a disturbance; it only gets a clatter once the prop is already
            // in the air and comes down on it.
            if (collision.rigidbody == null && !Disturbed) return;

            float speed = collision.relativeVelocity.magnitude;
            if (speed < clatterSpeed) return;

            MarkDisturbed();
            GameAudio.Clatter(collision.GetContact(0).point, Mathf.InverseLerp(clatterSpeed, clatterLoudSpeed, speed));
        }

        /// <summary>Awake by any route - a nudge, a blast, a rolling neighbour - counts as disturbed.</summary>
        public void NoteAwake() => MarkDisturbed();

        public override bool HomeIsClear()
        {
            if (_box == null) _box = GetComponent<BoxCollider>();
            if (_box == null) return true;

            // Shrunken and lifted: the bottom fifth of the box is left out so the pavement it sits
            // on is never the hit, and anything on the Props layer (including itself) is masked off.
            var size = Vector3.Scale(_box.size, transform.lossyScale);
            var half = size * 0.3f;
            var centre = HomePosition + HomeRotation * Vector3.Scale(_box.center, transform.lossyScale);
            int mask = PropsLayer >= 0 ? ~(1 << PropsLayer) : ~0;
            return !Physics.CheckBox(centre, half, HomeRotation, mask, QueryTriggerInteraction.Ignore);
        }

        public override void Restore()
        {
            if (Body == null) Body = GetComponent<Rigidbody>();

            // Order matters, and it is the trap `PropSystem`'s note names: a pose write wakes the
            // body, so the pose goes first, PhysX is told about it, the velocities are zeroed, and
            // Sleep is LAST.
            transform.SetPositionAndRotation(HomePosition, HomeRotation);
            Physics.SyncTransforms();
            if (Body != null)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
                Body.Sleep();
            }
            ClearDisturbed();
        }
    }
}
