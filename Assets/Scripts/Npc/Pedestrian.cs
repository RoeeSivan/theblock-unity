using UnityEngine;
using UnityEngine.AI;

namespace TheBlock.Npc
{
    /// <summary>
    /// One person walking the pavement — U16b's port of the original's <c>pedestrian.ts</c>.
    ///
    /// <b>No NavMeshAgent, deliberately, and it is a reversal of U16.</b> U16 gave every pedestrian
    /// an agent so that "stays on the pavement" was a property of the world rather than of a
    /// heuristic. It works, and it costs: an agent owns the transform, does its own avoidance and
    /// pathfinding, and has to be created on the mesh before it will do anything — which is what the
    /// "Failed to create agent because it is not close enough to the NavMesh" spam was. The original
    /// needs none of it, because it does not wander freely: it walks a hand-authored strip, or
    /// wanders inside a hand-painted rectangle, and both are pavement by construction.
    ///
    /// So the port is the web's own state machine — arrive, pause, re-pick; or walk the lane and
    /// turn around — and the NavMesh stays only as a QUERY SURFACE: <c>SamplePosition</c> answers
    /// "is this pavement" and <c>Raycast</c> answers "does this straight line stay on it". Those are
    /// the only two questions the web build's 4096² sidewalk mask ever answered, and the carve makes
    /// the answer better than the mask's (it let people onto kerbstones). <b>The agent is gone; the
    /// mesh is not.</b> Deleting the queries does not "finish the job", it removes the pavement.
    ///
    /// What this class also owns is the KERB. A crosser waits at the edge, asks its
    /// <see cref="Crossing"/> whether it may go, and walks across under its own power — the one
    /// moment the crowd is gated on something outside itself, and U17's traffic light is what
    /// answers.
    ///
    /// Ticked by <see cref="CrowdSpawner"/> rather than by its own <c>Update</c>: Unity's
    /// per-MonoBehaviour dispatch is a real cost across a live crowd, and the spawner has to walk
    /// the same list anyway.
    /// </summary>
    [DisallowMultipleComponent]
    public class Pedestrian : MonoBehaviour
    {
        /// <summary>Everything from <c>npc.config.ts</c>, handed over by the spawner each bind.</summary>
        public struct Tuning
        {
            public float StepRadius;
            public float PauseTime;
            public float ArriveDistance;
            public float ZoneInset;
            public float AvoidRepickSec;
            public float SameStoreyBand;
            public float CurbMargin;
            public int SampleAttempts;
            public float SampleRadius;
            public int GroundMask;
        }

        [Tooltip("Ground speed this character's walk clip was authored at, from the clip itself. " +
                 "The animator's Speed parameter is a RATIO against this, not a m/s — see " +
                 "NpcAnimatorBuilder.")]
        [SerializeField] private float walkClipSpeed = 1.35f;

        [Tooltip("The visual child. Its Animator is what gets the Speed parameter.")]
        [SerializeField] private Animator animator;

        private static readonly int SpeedParameter = Animator.StringToHash("Speed");

        private CrowdSpawner _owner;
        private CrowdSeedTable.LanePath _path;
        private Crossing _gate;
        private CrowdSeedTable.Rect _rect;
        private bool _hasRect;
        private System.Random _rng;

        private CrowdSeedTable.Seed _state;
        private float _pauseLeft;
        private float _blockedFor;
        private bool _blocked;
        private Vector3 _lastPosition;

        /// <summary>True between <see cref="Bind"/> and <see cref="Release"/>.</summary>
        public bool Live { get; private set; }

        /// <summary>Which seed this body currently is. −1 when pooled.</summary>
        public int SeedIndex { get; private set; } = -1;

        /// <summary>
        /// True while this person is actually ON a carriageway — not merely standing at the kerb
        /// waiting for the light.
        ///
        /// U17's traffic reads exactly this to decide whom to brake for, and the difference is what
        /// keeps the two systems from deadlocking: a pedestrian at the kerb gates on the LIGHT and
        /// never on cars, so a car and a pedestrian cannot end up each waiting on the other. Once
        /// they have stepped out they are a real obstacle, including when the light flips under them.
        ///
        /// Measured against this lane's OWN endpoints rather than the road centreline — the web's
        /// <c>refreshOnRoad</c>. A crosser is on the road when it is more than
        /// <c>curbMargin</c> from both of its own kerbs.
        /// </summary>
        public bool IsCrossing { get; private set; }

        public float WalkClipSpeed => walkClipSpeed;

        /// <summary>Set by <c>NpcBuilder</c> at build time.</summary>
        public void Configure(Animator characterAnimator, float measuredWalkClipSpeed)
        {
            animator = characterAnimator;
            walkClipSpeed = measuredWalkClipSpeed;
        }

        // --- lifecycle --------------------------------------------------------------------------

        /// <summary>Puts this body on a seed. Everything the seed remembers is restored.</summary>
        public void Bind(
            CrowdSpawner owner, int seedIndex, in CrowdSeedTable.Seed state,
            CrowdSeedTable.LanePath path, Crossing gate, CrowdSeedTable.Rect rect, bool hasRect,
            System.Random rng)
        {
            _owner = owner;
            _path = path;
            _gate = gate;
            _rect = rect;
            _hasRect = hasRect;
            _rng = rng;
            _state = state;

            SeedIndex = seedIndex;
            Live = true;
            _pauseLeft = 0f;
            _blockedFor = 0f;
            _blocked = false;
            IsCrossing = false;

            var position = _state.Mode == CrowdSeedTable.Mode.Wander || _path == null
                ? _state.Position
                : _path.At(_state.S);

            transform.position = position;
            _lastPosition = position;
            if (animator != null) animator.SetFloat(SpeedParameter, 0f);
        }

        /// <summary>Hands the seed back with whatever this body did to it.</summary>
        public CrowdSeedTable.Seed Release()
        {
            _state.Position = transform.position;
            Live = false;
            SeedIndex = -1;
            _owner = null;
            _path = null;
            _gate = null;
            IsCrossing = false;
            return _state;
        }

        // --- the tick ---------------------------------------------------------------------------

        /// <summary>One step. Called by <see cref="CrowdSpawner"/>, not by Unity.</summary>
        public void Tick(float dt, in Tuning tuning)
        {
            if (!Live) return;

            float animSpeed;
            if (_state.Mode == CrowdSeedTable.Mode.Wander) animSpeed = Wander(dt, tuning);
            else animSpeed = Walk(dt, tuning);

            if (animator != null)
                animator.SetFloat(SpeedParameter, walkClipSpeed > 0.01f ? animSpeed / walkClipSpeed : 0f);
        }

        /// <summary>
        /// Walk to the target, stand a moment, pick another. A line-for-line port of the web's
        /// wander mode — including the two behaviours that are easy to drop and change how a street
        /// reads: the pause at each arrival (without it the crowd is a conveyor belt), and re-picking
        /// after being blocked by a car for <c>AvoidRepickSec</c> so people step AROUND it instead of
        /// standing in the road until it moves.
        /// </summary>
        private float Wander(float dt, in Tuning tuning)
        {
            var position = transform.position;
            var flat = new Vector3(_state.Target.x - position.x, 0f, _state.Target.z - position.z);
            float distance = flat.magnitude;

            if (distance < tuning.ArriveDistance)
            {
                _blocked = false;
                _pauseLeft += dt;
                if (_pauseLeft >= tuning.PauseTime)
                {
                    _pauseLeft = 0f;
                    PickTarget(tuning);
                }

                return 0f;
            }

            var direction = flat / distance;
            float step = Mathf.Min(_state.Speed * dt, distance);
            var next = position + direction * step;

            if (_owner != null && _owner.BlockedByCar(next))
            {
                _blocked = true;
                _blockedFor += dt;
                if (_blockedFor >= tuning.AvoidRepickSec)
                {
                    _blockedFor = 0f;
                    PickTarget(tuning);
                }

                return 0f;
            }

            _blocked = false;
            _blockedFor = 0f;

            // "Sidewalks are flat" — the web snaps to the target's height rather than probing every
            // step, and the target's height came from a real ground sample when it was picked.
            next.y = _state.Target.y;
            transform.SetPositionAndRotation(next, Quaternion.LookRotation(direction, Vector3.up));
            _state.Position = next;
            return _state.Speed;
        }

        /// <summary>
        /// Walks the lane. Ungated ends reflect (a strip walker turns around); gated ends CLAMP and
        /// wait, which is the difference between a pavement stroller and someone at a kerb.
        /// </summary>
        private float Walk(float dt, in Tuning tuning)
        {
            if (_path == null || _path.Points.Length < 2) return 0f;

            bool gated = _gate != null;

            if (gated && (_state.S <= 0f || _state.S >= _path.Length))
            {
                IsCrossing = false;
                if (!_owner.MayCross(_gate, _path)) return 0f;

                // Whichever kerb we are on, set off towards the other one.
                _state.Dir = (sbyte)(_state.S <= 0f ? 1 : -1);
            }

            float previous = _state.S;
            _state.S += _state.Dir * _state.Speed * dt;

            if (gated)
            {
                _state.S = Mathf.Clamp(_state.S, 0f, _path.Length);
            }
            else if (_state.S > _path.Length)
            {
                _state.S = _path.Length - (_state.S - _path.Length);
                _state.Dir = -1;
            }
            else if (_state.S < 0f)
            {
                _state.S = -_state.S;
                _state.Dir = 1;
            }

            var position = _path.At(_state.S);

            // A strip walker holds for a car in its way; a crosser does NOT, and that asymmetry is
            // load-bearing. Mid-road the crosser keeps going and the traffic yields to it, which is
            // what keeps the pedestrian↔car wait graph acyclic.
            if (!gated && _owner != null && _owner.BlockedByCar(position))
            {
                _state.S = previous;
                _blocked = true;
                return 0f;
            }

            _blocked = false;

            var delta = position - _lastPosition;
            delta.y = 0f;
            if (delta.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);

            transform.position = position;
            _lastPosition = position;
            _state.Position = position;

            if (gated)
            {
                var a = _path.At(0f);
                var b = _path.At(_path.Length);
                float margin = tuning.CurbMargin;
                IsCrossing = Flat(position, a) > margin && Flat(position, b) > margin;
            }

            return _state.Speed;
        }

        private static float Flat(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Somewhere else to be, within <c>stepRadius</c> — the port of the web's
        /// <c>sampleNearReachable</c>, and the one place the 4096² walkable mask is replaced rather
        /// than dropped.
        ///
        /// The mask answers two questions and the NavMesh answers both with no memory and no
        /// readback: <c>SamplePosition</c> is <c>isWalkable</c>, and <c>Raycast</c> is
        /// <c>segmentWalkable</c> — which is what stops a wanderer cutting through a building corner
        /// or across a carriageway. The sample radius is deliberately SMALL: a generous one snaps
        /// the candidate somewhere else entirely and stops being a test of the point at all.
        ///
        /// Finding nothing is not an error. The web's answer is "stay put and try again next time",
        /// and so is this one — the target is left alone.
        /// </summary>
        private void PickTarget(in Tuning tuning)
        {
            var here = transform.position;

            for (int attempt = 0; attempt < tuning.SampleAttempts; attempt++)
            {
                var offset = InsideUnitCircle() * tuning.StepRadius;
                var candidate = here + new Vector3(offset.x, 0f, offset.y);

                if (!NavMesh.SamplePosition(candidate, out var hit, tuning.SampleRadius, NavMesh.AllAreas))
                    continue;

                // The bake cannot tell a flat roof from a pavement — both are horizontal geometry a
                // Humanoid agent fits on — so reject anything a storey off where this person is.
                if (Mathf.Abs(hit.position.y - here.y) > tuning.SameStoreyBand) continue;

                if (_hasRect && !Inside(hit.position, tuning.ZoneInset)) continue;

                if (NavMesh.Raycast(here, hit.position, out _, NavMesh.AllAreas)) continue;

                var target = hit.position;
                if (CrowdGround.TrySample(target, tuning.GroundMask, 1.5f, 4f, out float y)) target.y = y;

                _state.Target = target;
                return;
            }
        }

        /// <summary>Inside this person's own rectangle, kept off its very edge.</summary>
        private bool Inside(Vector3 point, float inset) =>
            point.x >= _rect.MinX + inset && point.x <= _rect.MaxX - inset &&
            point.z >= _rect.MinZ + inset && point.z <= _rect.MaxZ - inset;

        /// <summary>Uniform in the disc, from this pedestrian's own stream so the crowd stays seeded.</summary>
        private Vector2 InsideUnitCircle()
        {
            double angle = _rng.NextDouble() * System.Math.PI * 2.0;
            double radius = System.Math.Sqrt(_rng.NextDouble());
            return new Vector2((float)(System.Math.Cos(angle) * radius), (float)(System.Math.Sin(angle) * radius));
        }

        /// <summary>Debug read-out for the crowd's gizmos and the MCP scans.</summary>
        public bool BlockedByTraffic => _blocked;
    }
}
