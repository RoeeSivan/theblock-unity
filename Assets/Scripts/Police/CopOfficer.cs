using TheBlock.Npc;
using UnityEngine;
using UnityEngine.AI;

namespace TheBlock.Police
{
    /// <summary>
    /// The officer who drives the cruiser and finishes the arrest on foot.
    ///
    /// <b>She is one body in two places, not two bodies.</b> Seated, she is a child of the car's
    /// <c>DriverAnchor</c> with her agent, her capsule and her Animator all switched off - a pose
    /// held on a skinned mesh costs nothing per frame, and the car carries her because her
    /// Rigidbody is kinematic and a kinematic body follows the transform it hangs under. Deployed,
    /// she is unparented at that same anchor and becomes a NavMeshAgent. There is no second prefab
    /// to keep in sync and no swap to get wrong.
    ///
    /// <b>The anchor is why the exit needs no animation.</b> <c>DriverAnchor</c> is where the entry
    /// clip's ORIGIN goes - a person standing beside the door at road level - and the clip's own
    /// travel is what carries the body into the seat (see the memory file
    /// <c>driver-seat-is-clip-origin</c>, and <c>CarBuilder.BuildDriverAnchor</c>). So sitting is
    /// <c>Joe_EnterCar</c> held at normalized time 1, standing up again is the same clip at 0, and
    /// the transform never moves between the two. The cruiser's doors do not open - no car in this
    /// project has a door joint on the driver's side - which is the same compromise the player
    /// already lives with.
    ///
    /// <b>The NavMesh is a pursuit surface here and only here.</b> U16b deleted agents from the
    /// crowd for good reasons: 1,147 of them owned their own transforms, avoided each other, and
    /// walked authored strips that never needed a path. None of that applies to three officers who
    /// have to corner a building after somebody who is actively running away. The fallback below
    /// exists because the player can stand where the mesh does not reach - the car park they spawn
    /// in is 80 m from the nearest road - and an officer frozen at the kerb because
    /// <c>SetDestination</c> failed would read as a bug, not as a limit.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class CopOfficer : MonoBehaviour
    {
        public enum Mode
        {
            /// <summary>In the driver's seat, parented to the car, costing nothing.</summary>
            Seated,

            /// <summary>Out and running at the player.</summary>
            Chasing,

            /// <summary>Given up, running back to the car door to get back in.</summary>
            Returning,
        }

        [Header("Wiring - filled by CopOfficerBuilder")]
        [SerializeField] private Animator animator;
        [SerializeField] private NavMeshAgent agent;
        [SerializeField] private CapsuleCollider capsule;

        [Header("Movement")]
        [Tooltip("Metres per second on foot. The player sprints at 7 and jogs at 4.5, so this is " +
                 "deliberately between them: a fresh sprint gets away, a jog does not, and stamina " +
                 "is what decides which one you are doing by the time she reaches you.")]
        [SerializeField] private float runSpeed = 6.2f;

        [Tooltip("Seconds between path requests. A destination that moves every frame does not need " +
                 "a new A* every frame - the agent keeps following the old corridor meanwhile.")]
        [SerializeField] private float repathInterval = 0.25f;

        [Tooltip("Metres of NavMesh search around a destination that is off the mesh.")]
        [SerializeField] private float sampleRadius = 6f;

        [Tooltip("Metres from the car door at which she is back in the seat.")]
        [SerializeField] private float reseatRadius = 1.6f;

        private static readonly int SpeedId = Animator.StringToHash("Speed");

        private const string SitState = "Sit";
        private const string GaitState = "Gait";

        private Transform _seat;
        private Mode _mode = Mode.Seated;
        private float _repathAt;
        private float _reacquireAt;
        private Vector3 _lastPosition;
        private float _planarSpeed;
        private float _standoff = 1.1f;

        public Mode State => _mode;

        /// <summary>
        /// Metres she stops SHORT of the player, pushed in from <see cref="PoliceTuning.OfficerStandoff"/>
        /// at pool fill.
        ///
        /// <b>The player is not an obstacle to a NavMeshAgent</b> - a `CharacterController` is neither
        /// an agent nor a carve, so nothing in the navigation system knows it is standing there, and a
        /// destination set to the player's exact position is an instruction to occupy them. The first
        /// play-test saw exactly that: she ran up and kept going, into and through.
        ///
        /// The stopping distance is a QUARTER of it, not a match, because the destination is already
        /// the standoff point - see <see cref="StandoffPoint"/>. Setting both to the same number would
        /// stop her at two standoffs, outside her own grab radius, which is the arrest never firing
        /// again by a different route. What the quarter buys is a dead band: without one the agent
        /// re-solves for a point it is already standing on every <see cref="repathInterval"/> and
        /// shuffles in place.
        /// </summary>
        public float Standoff
        {
            get => _standoff;
            set
            {
                _standoff = Mathf.Max(0f, value);
                if (agent != null) agent.stoppingDistance = _standoff * 0.25f;
            }
        }

        /// <summary>True while she is out of the car and therefore the thing that arrests you.</summary>
        public bool Deployed => _mode != Mode.Seated;

        private void Awake()
        {
            if (agent == null) TryGetComponent(out agent);
            if (capsule == null) TryGetComponent(out capsule);
            if (animator == null) animator = GetComponentInChildren<Animator>(true);

            if (agent != null)
            {
                agent.speed = runSpeed;
                agent.enabled = false;
            }

            if (capsule != null) capsule.enabled = false;
            _lastPosition = transform.position;
        }

        /// <summary>
        /// Binds her to one cruiser's driver anchor and puts her in it. Called once, at pool fill.
        /// </summary>
        public void BindSeat(Transform seat)
        {
            _seat = seat;
            SeatNow();
        }

        /// <summary>
        /// Back in the car this instant, wherever she was.
        ///
        /// The instant version is the one the bust, a lost star and a wrecked cruiser all need: the
        /// star is gone, the street has to be clear now, and an officer left jogging back across it
        /// after the red screen has cleared is the last thing the player sees.
        /// </summary>
        public void SeatNow()
        {
            _mode = Mode.Seated;

            if (agent != null && agent.enabled) agent.enabled = false;
            if (capsule != null) capsule.enabled = false;

            if (_seat != null)
            {
                transform.SetParent(_seat, worldPositionStays: false);
                transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            }

            if (animator == null) return;

            // ⚠ AlwaysAnimate is what makes the next three lines do anything at all. A culled
            // Animator - and every cruiser parked at the station is culled - evaluates its state
            // machine and then skips WRITING the pose to the bones, so this whole call silently
            // left her standing in the bind pose beside the door. Asserted here as well as on the
            // prefab because it is the kind of field a later hand-edit puts back.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Straight to the last frame, never through the clip: a transition into Sit would play
            // the whole 5.47 s walk-up while she is already inside the car.
            animator.enabled = true;
            animator.Play(SitState, 0, 1f);
            animator.Update(0f);
            animator.enabled = false;
        }

        /// <summary>
        /// Out of the car, at the door, running.
        ///
        /// Returns false when there is nothing to step out of - no seat bound - which is the only
        /// way this can fail. Being off the NavMesh does not fail it: she walks in a straight line
        /// until the mesh is under her again.
        /// </summary>
        public bool Deploy()
        {
            if (_seat == null) return false;

            var door = _seat.position;
            var facing = _seat.rotation;

            transform.SetParent(null, worldPositionStays: false);
            transform.SetPositionAndRotation(door, Quaternion.Euler(0f, facing.eulerAngles.y, 0f));

            if (animator != null)
            {
                animator.enabled = true;
                animator.Play(GaitState, 0, 0f);
                animator.SetFloat(SpeedId, 1f);
            }

            if (capsule != null) capsule.enabled = true;
            _mode = Mode.Chasing;
            _repathAt = 0f;
            _lastPosition = transform.position;

            TryTakeTheMesh();
            return true;
        }

        /// <summary>Sends her back to the car under her own legs. She re-seats when she reaches it.</summary>
        public void Recall()
        {
            if (_mode == Mode.Chasing) _mode = Mode.Returning;
        }

        /// <summary>
        /// One step. <paramref name="target"/> is the player; it is ignored while returning, because
        /// what she is walking to then is the door she came out of.
        /// </summary>
        public void Step(Vector3 target, float dt)
        {
            if (_mode == Mode.Seated) return;

            // Returning walks to the door and has its own arrival test; chasing walks to a point
            // short of the player, never to the player.
            var destination = _mode == Mode.Returning && _seat != null
                ? _seat.position
                : StandoffPoint(target);

            if (_mode == Mode.Returning && _seat != null &&
                Planar(transform.position, _seat.position) <= reseatRadius)
            {
                SeatNow();
                return;
            }

            if (agent != null && agent.enabled && agent.isOnNavMesh) Drive(destination);
            else Walk(destination, dt);

            // Measured off the transform rather than read off the agent, because the fallback has no
            // agent to read. One number, both paths.
            _planarSpeed = dt > 0f ? Planar(transform.position, _lastPosition) / dt : 0f;
            _lastPosition = transform.position;

            if (_mode == Mode.Chasing) FaceWhenClose(target, dt);

            if (animator != null)
                animator.SetFloat(SpeedId, Mathf.Clamp01(_planarSpeed / Mathf.Max(0.01f, runSpeed)));
        }

        /// <summary>
        /// The point <see cref="Standoff"/> metres short of <paramref name="target"/>, on the line
        /// back towards her.
        ///
        /// It is done here rather than by <c>agent.stoppingDistance</c> alone because ONE mechanism
        /// has to serve both paths: <see cref="Walk"/> is a hand-rolled straight line with no agent
        /// in it at all, and the spawn car park - where a chase on foot is most likely to start - has
        /// no NavMesh within 10 m. A brake that only exists on the agent would let her walk through
        /// the player on precisely the ground the fallback covers.
        ///
        /// <b>The clamp is what stops her backing away.</b> Once she is nearer than the standoff the
        /// pull-back would otherwise put the destination BEHIND her, and she would retreat to arm's
        /// length every time the player closed in - a grab radius that pushes its own target out of
        /// itself. Clamped to her own distance, the point becomes where she is standing and she
        /// simply holds.
        /// </summary>
        private Vector3 StandoffPoint(Vector3 target)
        {
            var back = transform.position - target;
            back.y = 0f;

            float distance = back.magnitude;
            if (distance <= 0.01f) return target;

            return target + back * (Mathf.Min(_standoff, distance) / distance);
        }

        /// <summary>
        /// Turns her to the person she is arresting once she has stopped in front of them.
        ///
        /// Both movement paths aim her along her own travel, which is right while she is running and
        /// wrong the moment she arrives: a standoff means she stops a stride away, and whatever
        /// heading the last step left her on is the one she keeps. The band is deliberately wider
        /// than the standoff itself so the turn starts as she settles rather than snapping after it,
        /// and it is inside the agent's stopping distance, so this is not fighting the agent for the
        /// rotation - by here it has already given up steering.
        /// </summary>
        private void FaceWhenClose(Vector3 target, float dt)
        {
            var look = target - transform.position;
            look.y = 0f;

            if (look.sqrMagnitude < 0.01f) return;
            if (look.magnitude > _standoff * 1.5f) return;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, Quaternion.LookRotation(look, Vector3.up), 480f * dt);
        }

        /// <summary>Planar metres between her and a point - the arrest test's input.</summary>
        public float DistanceTo(Vector3 point) => Planar(transform.position, point);

        // --- movement ------------------------------------------------------------------------------

        private void Drive(Vector3 destination)
        {
            if (Time.time < _repathAt) return;
            _repathAt = Time.time + repathInterval;

            // The destination is sampled, never handed over raw: a player standing on the beach, on
            // a lot or inside a building is metres off the mesh, and SetDestination to a point with
            // no polygon under it fails outright and leaves her standing.
            agent.SetDestination(
                NavMesh.SamplePosition(destination, out var hit, sampleRadius, NavMesh.AllAreas)
                    ? hit.position
                    : destination);
        }

        /// <summary>
        /// Off the mesh: straight at them, on whatever ground the crowd's own probe can find.
        ///
        /// This is not a pathfinder and is not trying to be - it is the twenty metres of car park or
        /// sand between the mesh and the player, walked in a straight line, with the agent taken
        /// back the moment there is a polygon under her feet again.
        /// </summary>
        private void Walk(Vector3 destination, float dt)
        {
            var step = destination - transform.position;
            step.y = 0f;

            if (step.sqrMagnitude > 0.01f)
            {
                var direction = step.normalized;
                transform.position += direction * (runSpeed * dt);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, Quaternion.LookRotation(direction, Vector3.up), 480f * dt);
            }

            var position = transform.position;
            if (CrowdGround.TrySample(position, ~0, 3f, 6f, out var y))
                transform.position = new Vector3(position.x, y, position.z);

            if (Time.time < _reacquireAt) return;
            _reacquireAt = Time.time + 0.5f;
            TryTakeTheMesh();
        }

        /// <summary>
        /// Puts the agent back on the NavMesh if there is one within reach.
        ///
        /// <c>Warp</c> before <c>enabled</c> is not optional: an agent enabled off the mesh logs
        /// "Failed to create agent because it is not close enough to the NavMesh" once per frame,
        /// which is the spam U16 hit across a whole crowd.
        /// </summary>
        private void TryTakeTheMesh()
        {
            if (agent == null || agent.enabled) return;
            if (!NavMesh.SamplePosition(transform.position, out var hit, 2f, NavMesh.AllAreas)) return;

            transform.position = hit.position;
            agent.enabled = true;
            agent.speed = runSpeed;
            agent.Warp(hit.position);
            _repathAt = 0f;
        }

        private static float Planar(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Editor-side wiring, used by <c>CopOfficerBuilder</c>.</summary>
        public void SetParts(Animator officerAnimator, NavMeshAgent officerAgent,
            CapsuleCollider officerCapsule, float speed)
        {
            animator = officerAnimator;
            agent = officerAgent;
            capsule = officerCapsule;
            runSpeed = speed;
        }
    }
}
