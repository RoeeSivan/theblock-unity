using UnityEngine;
using UnityEngine.AI;

namespace TheBlock.Npc
{
    /// <summary>
    /// One person walking the pavement — U16.
    ///
    /// The web build's pedestrian picks a point out of a sampled grid and walks to it IN A STRAIGHT
    /// LINE, with no path and no idea that a road exists (`pedestrian.ts`: "No physics body, no
    /// collision avoidance"). This one asks the NavMesh for a destination and lets the agent find
    /// the corridor, so "stays on the pavement" is a property of the world rather than of a
    /// heuristic that mostly holds. Everything that used to enforce it — the 4096² walkable mask,
    /// the 67 MB readback, the forty hand-recorded rectangles, the forty hand-recorded strips — has
    /// no equivalent here and no replacement.
    ///
    /// What this class does own is the KERB. Automatic off-mesh link traversal is switched off, so
    /// arriving at a zebra hands control back here: the pedestrian stops at the edge, asks the
    /// <see cref="Crossing"/> whether it may go, and walks across under its own power when the
    /// answer is yes. That is the one moment the crowd is not simply "the agent knows best".
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class Pedestrian : MonoBehaviour
    {
        [Tooltip("How far the next wander target may be. `npc.config.ts` stepRadius.")]
        [SerializeField] private float stepRadius = 8f;

        [Tooltip("Seconds standing at each reached spot. `npc.config.ts` pauseTime.")]
        [SerializeField] private float pauseTime = 1.2f;

        [Tooltip("Metres of height difference that still counts as the same storey. Keeps targets " +
                 "off the rooftops the bake cannot tell from pavement.")]
        [SerializeField] private float sameStoreyBand = 2.5f;

        [Tooltip("Tries per re-target before the pedestrian gives up and waits a beat.")]
        [SerializeField] private int sampleAttempts = 6;

        [Tooltip("Chance, per re-target, of heading for the far side of the nearest zebra instead of " +
                 "wandering. With 8 m hops almost nobody would otherwise ever want the other pavement.")]
        [Range(0f, 1f)]
        [SerializeField] private float crossingBias = 0.25f;

        [Tooltip("How far a pedestrian will look for a zebra to be drawn to.")]
        [SerializeField] private float crossingSearchRadius = 45f;

        private NavMeshAgent _agent;
        private Animator _animator;
        private System.Random _rng;

        private float _pauseLeft;

        /// <summary>Set while stepping across a zebra under manual control.</summary>
        private bool _crossing;
        private Vector3 _crossFrom;
        private Vector3 _crossTo;
        private Crossing _gate;

        private static readonly int SpeedParameter = Animator.StringToHash("Speed");

        /// <summary>True once the agent is on the NavMesh and has somewhere to be.</summary>
        public bool Active { get; private set; }

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _animator = GetComponentInChildren<Animator>();

            // The whole point: arriving at a crossing must hand control back to this script rather
            // than teleport-sliding the agent over the road regardless of the traffic.
            _agent.autoTraverseOffMeshLink = false;
        }

        /// <summary>
        /// Wakes this pedestrian up at <paramref name="position"/>. Called by
        /// <see cref="CrowdSpawner"/> on spawn and on every recycle.
        /// </summary>
        public void Begin(Vector3 position, float speed, System.Random rng)
        {
            _rng = rng;
            _crossing = false;
            _gate = null;
            _pauseLeft = 0f;

            _agent.speed = speed;
            // Warp, not `transform.position`: an agent moved by its transform keeps the internal
            // position it had, and then walks back to it the moment it is enabled.
            _agent.Warp(position);
            Active = _agent.isOnNavMesh;
            if (Active) Retarget();
        }

        public void End()
        {
            Active = false;
            _crossing = false;
            if (_agent.isOnNavMesh) _agent.ResetPath();
        }

        private void Update()
        {
            if (!Active) return;

            if (_crossing) StepAcross();
            else if (_agent.isOnOffMeshLink) BeginCrossing();
            else Wander();

            if (_animator != null)
            {
                float speed = _crossing ? (_gate == null || _gate.MayCross() ? _agent.speed : 0f)
                                        : _agent.velocity.magnitude;
                _animator.SetFloat(SpeedParameter, speed);
            }
        }

        // --- wandering ---------------------------------------------------------------------------

        private void Wander()
        {
            if (_agent.pathPending) return;

            if (_agent.remainingDistance > _agent.stoppingDistance)
            {
                _pauseLeft = pauseTime;
                return;
            }

            // Arrived. Stand for a moment before choosing again — without it the crowd never stops
            // moving, which reads as a conveyor belt rather than a street.
            _pauseLeft -= Time.deltaTime;
            if (_pauseLeft > 0f) return;

            Retarget();
        }

        /// <summary>
        /// Picks somewhere else to be, within <see cref="stepRadius"/>.
        ///
        /// The height band is the one thing here that is not simply "ask the NavMesh". The bake
        /// cannot tell a flat roof from a pavement — both are horizontal geometry a Humanoid agent
        /// fits on — so a district's rooftops are walkable surface as far as pathfinding is
        /// concerned. Rejecting samples more than a storey off this pedestrian's own height keeps
        /// the crowd on the ground without a second bake or a hand-painted mask.
        /// </summary>
        private void Retarget()
        {
            // Some of the time, want the other side of the street. Left to pure wandering the
            // crowd never crosses — not because it cannot, but because an 8 m hop from a pavement
            // almost never lands on the opposite one, so the path that goes over a zebra is never
            // the path that gets asked for. This is the ONLY steering the crowd has, and it steers
            // towards a place, not along a route: how to get there is still the NavMesh's answer.
            if (_rng.NextDouble() < crossingBias && TryTargetAcrossCrossing()) return;

            for (int attempt = 0; attempt < sampleAttempts; attempt++)
            {
                var offset = _rng.InsideUnitCircle() * stepRadius;
                var candidate = transform.position + new Vector3(offset.x, 0f, offset.y);

                if (!NavMesh.SamplePosition(candidate, out var hit, stepRadius, NavMesh.AllAreas)) continue;
                if (Mathf.Abs(hit.position.y - transform.position.y) > sameStoreyBand) continue;

                _agent.SetDestination(hit.position);
                _pauseLeft = pauseTime;
                return;
            }

            // Boxed in — try again next second rather than every frame.
            _pauseLeft = 1f;
        }

        /// <summary>
        /// Aims for the far kerb of the nearest zebra. False if there is none in range or the far
        /// side is not on the NavMesh, in which case the caller wanders as usual.
        /// </summary>
        private bool TryTargetAcrossCrossing()
        {
            var crossing = CrossingRegistry.Nearest(transform.position, crossingSearchRadius);
            if (crossing == null) return false;

            // Whichever kerb is further from here is the one on the other side. A little past it,
            // onto the pavement proper, so the destination is not the boundary of the carve.
            var a = crossing.KerbA;
            var b = crossing.KerbB;
            bool aIsFar = (a - transform.position).sqrMagnitude > (b - transform.position).sqrMagnitude;
            var far = aIsFar ? a : b;
            var near = aIsFar ? b : a;
            var beyond = far + (far - near).normalized * 2f;

            if (!NavMesh.SamplePosition(beyond, out var hit, 3f, NavMesh.AllAreas)) return false;
            if (Mathf.Abs(hit.position.y - transform.position.y) > sameStoreyBand) return false;

            _agent.SetDestination(hit.position);
            _pauseLeft = pauseTime;
            return true;
        }

        // --- crossing ----------------------------------------------------------------------------

        private void BeginCrossing()
        {
            var link = _agent.currentOffMeshLinkData;
            _crossFrom = transform.position;
            _crossTo = link.endPos;
            _crossing = true;

            // Which zebra this is. Looked up once per traversal rather than held on the link, so a
            // crossing whose geometry U17 moves does not need the links rebuilt to match.
            _gate = CrossingRegistry.Nearest(transform.position, 12f);
        }

        private void StepAcross()
        {
            // Waiting on the kerb. The gate is the only thing in the crowd that says "not yet" —
            // U16 answers it with the road being clear, U17 with the light.
            if (_gate != null && !_gate.MayCross())
            {
                FaceAlong(_crossTo - _crossFrom);
                return;
            }

            transform.position = Vector3.MoveTowards(
                transform.position, _crossTo, _agent.speed * Time.deltaTime);
            FaceAlong(_crossTo - _crossFrom);

            if ((transform.position - _crossTo).sqrMagnitude > 0.01f) return;

            _crossing = false;
            _gate = null;
            _agent.CompleteOffMeshLink();
        }

        private void FaceAlong(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-4f) return;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(direction, Vector3.up),
                _agent.angularSpeed * Time.deltaTime);
        }
    }

    /// <summary>A point in the unit circle from a seeded RNG — <c>Random.insideUnitCircle</c> is global.</summary>
    internal static class RandomExtensions
    {
        public static Vector2 InsideUnitCircle(this System.Random rng)
        {
            float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
            float radius = Mathf.Sqrt((float)rng.NextDouble()); // sqrt, or every point clusters at the centre
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        public static float Range(this System.Random rng, float min, float max) =>
            min + (float)rng.NextDouble() * (max - min);
    }
}
