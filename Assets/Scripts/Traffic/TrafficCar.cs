using UnityEngine;

namespace TheBlock.Traffic
{
    /// <summary>
    /// One ambient street car. The state is here; the decisions are in <see cref="TrafficSystem"/>.
    ///
    /// <b>Kinematic while it drives, dynamic when it is hit — and that split is this unit's answer to
    /// the standing "can Unity do this better?" question.</b> The web build's traffic is kinematic
    /// full stop (<c>traffic-cars.ts</c>: "the player collides with them but can never push them off
    /// the road"), because thirty Rapier vehicles solving contacts every frame was never on the
    /// table. Kinematic is still the right default here for exactly that reason — a car following a
    /// baked lane costs a <c>MovePosition</c> and nothing else. But Unity can afford the exception
    /// the web build could not: hit one hard enough and <em>that one car</em> becomes a real
    /// Rigidbody, takes the blow, and stays a wreck the rest of the traffic queues behind until you
    /// drive away and its slot recycles.
    ///
    /// The cost is bounded by construction: only cars the player actually rams are ever dynamic, so
    /// the usual number is zero and the worst case is a handful. <see cref="wreckOnImpact"/> turns
    /// the whole thing off if it ever misbehaves, and nothing else depends on it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class TrafficCar : MonoBehaviour
    {
        public enum State
        {
            /// <summary>Pooled and hidden — not in the world.</summary>
            Idle,

            /// <summary>Following its lane under script control.</summary>
            Driving,

            /// <summary>A real Rigidbody, on its own. Still an obstacle; no longer a driver.</summary>
            Wrecked,
        }

        // --- built by TrafficCarBuilder ------------------------------------------------------

        [Tooltip("Half the body length, metres. Bumper-gap arithmetic.")]
        [SerializeField] private float halfLength = 2.2f;

        [Tooltip("Half the body width, metres. Lateral extent for the any-angle overlap test.")]
        [SerializeField] private float halfWidth = 0.9f;

        [Tooltip("Body height, metres. Only used to place the collider and the LOD threshold.")]
        [SerializeField] private float bodyHeight = 1.5f;

        [Tooltip("Renderers holding the body-paint slot, and which slot it is.")]
        [SerializeField] private MeshRenderer[] paintRenderers = System.Array.Empty<MeshRenderer>();
        [SerializeField] private int[] paintSlots = System.Array.Empty<int>();

        [Tooltip("One generated material per street colour, in the config palette's own order.")]
        [SerializeField] private Material[] paints = System.Array.Empty<Material>();

        // --- tuning ---------------------------------------------------------------------------

        [Tooltip("Off makes a traffic car an immovable wall for good — the web build's contract.")]
        [SerializeField] private bool wreckOnImpact = true;

        [Tooltip("Relative speed, m/s, below which a bump is just a bump. Kerb-speed nudges must not wreck.")]
        [SerializeField] private float wreckSpeed = 6f;

        [Tooltip("Mass once it is a real body. A saloon, not a skip.")]
        [SerializeField] private float wreckMass = 1400f;

        [Tooltip("Fraction of the impact speed the wreck leaves with. 1 would be a perfectly elastic hit.")]
        [SerializeField] private float wreckMomentumShare = 0.55f;

        [Tooltip("Ceiling on that, m/s, so nothing is ever launched across the city.")]
        [SerializeField] private float wreckMaxSpeed = 14f;

        // --- sim state, owned by TrafficSystem -------------------------------------------------

        [System.NonSerialized] public State Mode = State.Idle;

        /// <summary>
        /// Position in the pool, and the right-of-way tie-break. Every standoff between two cars is
        /// broken by "lower index wins", which is what makes the resolution acyclic: a ring of cars
        /// each yielding to the next cannot form, because one of them has the lowest index.
        /// </summary>
        [System.NonSerialized] public int Index;

        [System.NonSerialized] public int EdgeId;
        [System.NonSerialized] public int Dir = 1;
        [System.NonSerialized] public int Lane;
        [System.NonSerialized] public float S;
        [System.NonSerialized] public float Speed;
        [System.NonSerialized] public float Cruise;

        [System.NonSerialized] public bool InCorner;
        [System.NonSerialized] public float CornerS;
        [System.NonSerialized] public float CornerLength;
        [System.NonSerialized] public int NextEdgeId;
        [System.NonSerialized] public int NextDir = 1;
        [System.NonSerialized] public int NextLane;
        [System.NonSerialized] public float NextS;

        /// <summary>The bezier through the junction. Allocated once, refilled on every turn.</summary>
        [System.NonSerialized] public readonly Vector3[] Corner = new Vector3[TrafficGeometry.CornerPoints];

        [System.NonSerialized] public float Stuck;
        [System.NonSerialized] public float StuckLimit;
        [System.NonSerialized] public float Push;
        [System.NonSerialized] public float Gas;
        [System.NonSerialized] public float GasCool;

        /// <summary>Last pose, in the flat form every other car's obstacle scan reads.</summary>
        [System.NonSerialized] public TrafficGeometry.Box Box;

        private Rigidbody _body;
        private Material[][] _slotScratch;

        public float HalfLength => halfLength;
        public float HalfWidth => halfWidth;
        public float BodyHeight => bodyHeight;
        public int PaintCount => paints.Length;
        public Rigidbody Body => _body;

        /// <summary>Set by <c>TrafficCarBuilder</c>.</summary>
        public void Configure(
            float measuredHalfLength, float measuredHalfWidth, float measuredHeight,
            MeshRenderer[] renderers, int[] slots, Material[] palette)
        {
            halfLength = measuredHalfLength;
            halfWidth = measuredHalfWidth;
            bodyHeight = measuredHeight;
            paintRenderers = renderers;
            paintSlots = slots;
            paints = palette;
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _body.isKinematic = true;
            _body.interpolation = RigidbodyInterpolation.Interpolate;

            // One materials array per paint renderer, allocated once. `sharedMaterials` returns a
            // fresh array on every read, so re-painting through the property directly would allocate
            // twice per renderer per recycle — sixty times a minute, forever.
            _slotScratch = new Material[paintRenderers.Length][];
            for (int i = 0; i < paintRenderers.Length; i++)
                _slotScratch[i] = paintRenderers[i] != null
                    ? paintRenderers[i].sharedMaterials
                    : System.Array.Empty<Material>();
        }

        /// <summary>Repaints the body. Index into the generated palette, not a colour.</summary>
        public void SetPaint(int paintIndex)
        {
            if (paints.Length == 0) return;
            var material = paints[Mathf.Clamp(paintIndex, 0, paints.Length - 1)];

            for (int i = 0; i < paintRenderers.Length; i++)
            {
                var renderer = paintRenderers[i];
                if (renderer == null) continue;

                int slot = i < paintSlots.Length ? paintSlots[i] : 0;
                var slots = _slotScratch[i];
                if (slot < 0 || slot >= slots.Length) continue;
                if (slots[slot] == material) continue;

                slots[slot] = material;
                renderer.sharedMaterials = slots;
            }
        }

        /// <summary>Puts the car back on a lane, kinematic and driving. A HARD teleport, deliberately.</summary>
        public void Place(Vector3 position, Vector3 forward)
        {
            // Not MovePosition: a kinematic body interpolates towards its target, so a recycle across
            // the city would sweep the collider through every building on the way and leave the car
            // smeared across a frame. The transform write is the teleport; MovePosition resumes next
            // step from wherever this put it.
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));

            if (_body != null)
            {
                // Zero a WRECK's motion before it goes kinematic again, never after: a kinematic
                // body refuses a velocity write and logs a warning for every one, and the transition
                // clears both anyway. Writing them in the other order is three console lines per
                // recycle for no effect.
                ClearMotion();
                _body.isKinematic = true;
                _body.position = position;
                _body.rotation = transform.rotation;
            }

            Mode = State.Driving;
            InCorner = false;
            Stuck = 0f;
            Push = 0f;
            Gas = 0f;
            UpdateBox(position, forward);
        }

        /// <summary>Drives one physics step. Kinematic: PhysX derives the velocity from the move.</summary>
        public void Drive(Vector3 position, Vector3 forward)
        {
            if (_body == null || _body.isKinematic == false) return;
            _body.MovePosition(position);
            _body.MoveRotation(Quaternion.LookRotation(forward, Vector3.up));
            UpdateBox(position, forward);
        }

        /// <summary>Refreshes the flat pose other cars scan. A wreck calls this from its transform.</summary>
        public void UpdateBox(Vector3 position, Vector3 forward)
        {
            Box.X = position.x;
            Box.Z = position.z;
            Box.Fx = forward.x;
            Box.Fz = forward.z;
            Box.HalfLen = halfLength;
            Box.HalfW = halfWidth;
        }

        public void Retire()
        {
            Mode = State.Idle;
            if (_body != null)
            {
                ClearMotion();
                _body.isKinematic = true;
            }

            gameObject.SetActive(false);
        }

        /// <summary>Stops a wreck dead. A no-op on a car that is already kinematic — deliberately.</summary>
        private void ClearMotion()
        {
            if (_body.isKinematic) return;
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!wreckOnImpact || Mode != State.Driving) return;
            if (collision.rigidbody == null) return;      // static geometry cannot wreck a car
            if (collision.rigidbody.isKinematic) return;  // another traffic car; the sim handles those
            if (collision.relativeVelocity.magnitude < wreckSpeed) return;

            Wreck(collision);
        }

        /// <summary>
        /// Hands this one car over to PhysX.
        ///
        /// The push direction is taken from the CONTACT POINT rather than from
        /// <c>Collision.impulse</c> or <c>relativeVelocity</c>. Both of those carry a sign whose
        /// meaning depends on which body the callback fired on, and getting it backwards sends the
        /// car through the thing that hit it. "Away from where I was struck" cannot be backwards: a
        /// hit on the rear pushes it forward, a hit on the flank slides it sideways.
        /// </summary>
        private void Wreck(Collision collision)
        {
            Mode = State.Wrecked;

            var contact = collision.GetContact(0);
            var away = transform.position - contact.point;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) away = transform.forward;
            away.Normalize();

            float speed = Mathf.Min(collision.relativeVelocity.magnitude * wreckMomentumShare, wreckMaxSpeed);

            _body.isKinematic = false;
            _body.mass = wreckMass;
            _body.linearVelocity = away * speed + transform.forward * Speed;
            // A little spin, scaled by how far off-centre the hit was, so a clipped wing slews the
            // car round instead of shunting it squarely down the road.
            float offCentre = Vector3.Dot(contact.point - transform.position, transform.right);
            _body.angularVelocity = new Vector3(0f, -offCentre * speed * 0.25f, 0f);

            Speed = 0f;
            InCorner = false;
        }
    }
}
