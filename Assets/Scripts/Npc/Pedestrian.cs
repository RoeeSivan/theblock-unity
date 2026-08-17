using System.Collections.Generic;
using TheBlock.Game;
using TheBlock.Vfx;
using UnityEngine;
using UnityEngine.AI;

namespace TheBlock.Npc
{
    /// <summary>
    /// One person walking the pavement - U16b's port of the original's <c>pedestrian.ts</c>.
    ///
    /// <b>No NavMeshAgent, deliberately, and it is a reversal of U16.</b> U16 gave every pedestrian
    /// an agent so that "stays on the pavement" was a property of the world rather than of a
    /// heuristic. It works, and it costs: an agent owns the transform, does its own avoidance and
    /// pathfinding, and has to be created on the mesh before it will do anything - which is what the
    /// "Failed to create agent because it is not close enough to the NavMesh" spam was. The original
    /// needs none of it, because it does not wander freely: it walks a hand-authored strip, or
    /// wanders inside a hand-painted rectangle, and both are pavement by construction.
    ///
    /// So the port is the web's own state machine - arrive, pause, re-pick; or walk the lane and
    /// turn around - and the NavMesh stays only as a QUERY SURFACE: <c>SamplePosition</c> answers
    /// "is this pavement" and <c>Raycast</c> answers "does this straight line stay on it". Those are
    /// the only two questions the web build's 4096² sidewalk mask ever answered, and the carve makes
    /// the answer better than the mask's (it let people onto kerbstones). <b>The agent is gone; the
    /// mesh is not.</b> Deleting the queries does not "finish the job", it removes the pavement.
    ///
    /// What this class also owns is the KERB. A crosser waits at the edge, asks its
    /// <see cref="Crossing"/> whether it may go, and walks across under its own power - the one
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
                 "The animator's Speed parameter is a RATIO against this, not a m/s - see " +
                 "NpcAnimatorBuilder.")]
        [SerializeField] private float walkClipSpeed = 1.35f;

        [Tooltip("Which pool this face screams from when it is run over. Baked by NpcBuilder from " +
                 "npcConfig.people[].gender - it is the only thing that field has ever read to.")]
        [SerializeField] private TheBlock.Audio.ScreamVoice voice = TheBlock.Audio.ScreamVoice.Male;

        [Tooltip("The visual child. Its Animator is what gets the Speed parameter.")]
        [SerializeField] private Animator animator;

        private static readonly int SpeedParameter = Animator.StringToHash("Speed");

        /// <summary>
        /// The animator states this class drives by name. Declared on the runtime side rather than in
        /// <c>NpcAnimatorBuilder</c> because the builder is editor-only and this is the consumer - the
        /// builder references these, not the other way round.
        /// </summary>
        public const string LocomotionState = "Locomotion";

        public const string HitState = "Hit";

        /// <summary>The knockdown clip, shared by all six characters. See <c>HitClipImporter</c>.</summary>
        public const string HitClipName = "Ped_HitByCar";

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

        // --- U18's run-over ---------------------------------------------------------------------
        private IRunOverReaction _reaction;
        private Ragdoll _ragdoll;
        private Blood _blood;
        private Vector3 _standingPosition;      // where this person was when the bumper arrived
        private Quaternion _standingRotation;
        private Collider _collider;
        private Renderer[] _renderers;
        private Material[][] _solidMaterials;   // the prefab's own, restored when the fade is over
        private Material[][] _fadeMaterials;    // transparent clones, built on the first knockdown
        private float _appliedOpacity = 1f;
        private bool _logHit;
        private float _logSpeed;

        /// <summary>True between <see cref="Bind"/> and <see cref="Release"/>.</summary>
        public bool Live { get; private set; }

        /// <summary>Which seed this body currently is. −1 when pooled.</summary>
        public int SeedIndex { get; private set; } = -1;

        /// <summary>
        /// True while this person is actually ON a carriageway - not merely standing at the kerb
        /// waiting for the light.
        ///
        /// U17's traffic reads exactly this to decide whom to brake for, and the difference is what
        /// keeps the two systems from deadlocking: a pedestrian at the kerb gates on the LIGHT and
        /// never on cars, so a car and a pedestrian cannot end up each waiting on the other. Once
        /// they have stepped out they are a real obstacle, including when the light flips under them.
        ///
        /// Measured against this lane's OWN endpoints rather than the road centreline - the web's
        /// <c>refreshOnRoad</c>. A crosser is on the road when it is more than
        /// <c>curbMargin</c> from both of its own kerbs.
        /// </summary>
        public bool IsCrossing { get; private set; }

        public float WalkClipSpeed => walkClipSpeed;

        /// <summary>
        /// True from the frame a vehicle hits this person until the body has faded out.
        ///
        /// A downed person is not a pedestrian in any sense the rest of the game cares about: they do
        /// not walk, they are not a braking obstacle, they have no capsule, and they cannot be hit
        /// again. Every one of those is a separate early-out below rather than one flag doing four
        /// jobs, because they come back at different moments.
        /// </summary>
        public bool Downed => _reaction != null;

        /// <summary>
        /// This person's own capsule radius, read off the prefab rather than configured a second
        /// time. It is what widens a vehicle's hit box from "the fender touched their origin" to "the
        /// fender touched them" - testing the bare origin means a corner clip with the capsule
        /// visibly inside the metal does nothing at all.
        /// </summary>
        public float BodyRadius
        {
            get
            {
                if (_collider == null) TryGetComponent(out _collider);
                return _collider is CapsuleCollider capsule ? capsule.radius : 0.3f;
            }
        }

        /// <summary>Set by <c>NpcBuilder</c> at build time.</summary>
        /// <summary>Editor-side wiring, used by <c>NpcBuilder</c>.</summary>
        public void SetVoice(TheBlock.Audio.ScreamVoice pool) => voice = pool;

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

            // A body coming out of the pool may have been someone's corpse a moment ago. Every one of
            // these is already undone by Recover; they are repeated here because a pooled object that
            // wakes up half-dead is the classic pooling bug, and the cost is four assignments.
            _reaction = null;
            _logHit = false;
            SetOpacity(1f);
            if (_collider != null) _collider.enabled = true;
            if (_ragdoll != null && _ragdoll.Active) _ragdoll.Rest();

            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                animator.SetFloat(SpeedParameter, 0f);
            }
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
            if (!Live || Downed) return;

            float animSpeed;
            if (_state.Mode == CrowdSeedTable.Mode.Wander) animSpeed = Wander(dt, tuning);
            else animSpeed = Walk(dt, tuning);

            if (animator != null)
            {
                // Cadence against the stride as DRAWN, not as the clip measures it.
                // <see cref="walkClipSpeed"/> is in the avatar's own units and the body is drawn at
                // the visual's scale - the same multiply, for the same reason, as
                // <see cref="HarvestRootMotion"/>, and Remy is again the one who proves it: his
                // avatar is 4.20 m tall, so his clip travels 2.5x too far in local units before it
                // is drawn 0.45x. Divide by the unscaled figure and a shrunk body skates.
                float drawn = walkClipSpeed * animator.transform.localScale.z;
                animator.SetFloat(SpeedParameter, drawn > 0.01f ? animSpeed / drawn : 0f);
            }
        }

        /// <summary>
        /// Walk to the target, stand a moment, pick another. A line-for-line port of the web's
        /// wander mode - including the two behaviours that are easy to drop and change how a street
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

            // "Sidewalks are flat" - the web snaps to the target's height rather than probing every
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
        /// Somewhere else to be, within <c>stepRadius</c> - the port of the web's
        /// <c>sampleNearReachable</c>, and the one place the 4096² walkable mask is replaced rather
        /// than dropped.
        ///
        /// The mask answers two questions and the NavMesh answers both with no memory and no
        /// readback: <c>SamplePosition</c> is <c>isWalkable</c>, and <c>Raycast</c> is
        /// <c>segmentWalkable</c> - which is what stops a wanderer cutting through a building corner
        /// or across a carriageway. The sample radius is deliberately SMALL: a generous one snaps
        /// the candidate somewhere else entirely and stops being a test of the point at all.
        ///
        /// Finding nothing is not an error. The web's answer is "stay put and try again next time",
        /// and so is this one - the target is left alone.
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

                // The bake cannot tell a flat roof from a pavement - both are horizontal geometry a
                // Humanoid agent fits on - so reject anything a storey off where this person is.
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

        // --- U18: run over ------------------------------------------------------------------------

        /// <summary>
        /// Hit by a vehicle. <see cref="RunOverSystem"/> decides who and when; this puts the body into
        /// the reaction and takes it back out again.
        ///
        /// <b>U35a made this a fork, and left the old branch fully alive.</b> With ragdolls on, PhysX
        /// gets the body and the clip is never loaded; with them off - or on a body whose prefab has
        /// never been through <c>RagdollBuilder</c> - it is U18's clip, unchanged, down to the
        /// materials it fades. That is the off-switch rule: the off state IS the old behaviour, not a
        /// neutral pass through new code.
        ///
        /// <b>Root motion goes ON here and OFF in <see cref="Recover"/>, and the clip path is the only
        /// place in the project where it is on.</b> Every other clip a pedestrian plays is a
        /// locomotion cycle whose travel is discarded because the script owns the position - but the
        /// knockback IS the clip's travel, and reproducing it in code would be two clocks for one body.
        ///
        /// <b>The aim is derived from the clip, never from a constant.</b> The animation is authored
        /// as "hit from the side while crossing", so its travel leaves the body at roughly a right
        /// angle to where it was facing. Reading that angle off the clip's own root motion and
        /// subtracting it puts the authored throw along the vehicle's forward, whatever the clip does
        /// - and it means nobody has to decide whether the web build's −85.8° survives a change of
        /// handedness. <b>The ragdoll needs none of that</b>, and that is the point of it: it does not
        /// have an authored direction to correct, so there is nothing to get wrong.
        /// </summary>
        public void KnockDown(
            Vector3 throwDirection, float speedMs, Vector3 hitPoint,
            in RunOverReaction.Tuning tuning, in RagdollReaction.Tuning ragdollTuning, Blood blood)
        {
            if (!Live || Downed || animator == null) return;

            if (_ragdoll == null) TryGetComponent(out _ragdoll);
            bool simulated = Progress.RagdollsOn && _ragdoll != null && _ragdoll.IsBuilt;

            var clip = simulated ? null : HitClip();
            if (!simulated && clip == null) return;   // no clip, no reaction - RunOverSystem says so, loudly

            _standingPosition = transform.position;
            _standingRotation = transform.rotation;
            _blood = blood;
            IsCrossing = false;
            _blocked = false;

            var flat = new Vector3(throwDirection.x, 0f, throwDirection.z);
            if (flat.sqrMagnitude < 1e-4f) flat = transform.forward;
            flat.Normalize();

            // A body flying through the air is not something the player's car should also be pushing,
            // and a corpse is not a wall to drive into.
            if (_collider == null) TryGetComponent(out _collider);
            if (_collider != null) _collider.enabled = false;

            float groundY = transform.position.y;
            if (CrowdGround.TrySample(transform.position, ~0, 1.5f, 3f, out float sampled))
                groundY = sampled;

            if (simulated)
            {
                var ragdoll = new RagdollReaction();
                ragdoll.Begin(_ragdoll, flat, speedMs, hitPoint, groundY, ragdollTuning, blood);
                _reaction = ragdoll;
            }
            else
            {
                float aim = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg - ThrowYaw(clip);
                transform.rotation = Quaternion.Euler(0f, aim, 0f);

                animator.applyRootMotion = true;

                // The reaction runs on its own clock, so a culled animator would leave the pose frozen
                // mid-flight while the body still travelled, landed and faded. Costly for a crowd,
                // free for the two or three people actually under a car.
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.CrossFadeInFixedTime(HitState, 0.05f, 0, 0f);

                var clipped = new RunOverReaction();
                clipped.Begin(transform, flat, speedMs, groundY, clip.length, tuning, blood);
                _reaction = clipped;
            }

            // The voice and the thud, on the impact frame, beside the blood - U27's debt to U18, and
            // the seam that row named. It sits HERE rather than inside the reaction because this is
            // the object that knows whose face this is; the reaction only knows a transform.
            // Self-throttling: a bumper box downs everyone inside it in this one frame.
            TheBlock.Audio.GameAudio.Scream(voice);
        }

        /// <summary>
        /// The downed body's step. Driven from <see cref="CrowdSpawner"/>'s <c>LateUpdate</c> so it
        /// runs AFTER the animator - the harvest below has to see this frame's root motion, and the
        /// arc has to be applied on top of it rather than under it.
        /// </summary>
        public void TickDown(float dt)
        {
            if (!Downed) return;

            // Only the clip moves this transform. A ragdoll's bones are simulated in world space and
            // the harvest would drag the whole rig by whatever the disabled Animator last left in the
            // visual's localPosition.
            if (_reaction.DrivesTransform) HarvestRootMotion();

            _reaction.Tick(dt, _blood);
            SetOpacity(_reaction.Opacity);

            // Reported when the body comes to REST, not when it touches down. Those are ~0.9 m apart:
            // the clip keeps sliding for about 0.6 s after its own vertical settles, which is a body
            // skidding to a stop and is the animation being right rather than the reaction being
            // wrong. Measuring at touchdown would report the throw as roughly half what it is.
            if (_logHit && _reaction.Done)
            {
                _logHit = false;

                // Where the BODY ended up, which after a ragdoll is nowhere near this transform.
                var rest = _reaction.DrivesTransform ? transform.position : _ragdoll.Position;
                var travel = rest - _standingPosition;
                travel.y = 0f;
                Debug.Log(
                    $"Run-over: {name} at {_logSpeed * 3.6f:0.0} km/h → thrown {travel.magnitude:0.00} m " +
                    $"({(_reaction.DrivesTransform ? "clip root motion + push" : "ragdoll")}), " +
                    $"came to rest at y {rest.y:0.00}", this);
            }

            if (_reaction.Done) Recover();
        }

        /// <summary>Ask for this hit to report what it actually did, once, on the landing frame.</summary>
        public void LogHit(float speedMs)
        {
            _logHit = true;
            _logSpeed = speedMs;
        }

        /// <summary>
        /// Moves the clip's root motion from the visual child up onto the pedestrian's own transform.
        ///
        /// <b>Why it is not simply <c>applyRootMotion</c> and done.</b> The Animator is on the VISUAL
        /// child, because that is where the model is - and a character that did not import at 1.70 m
        /// is scaled there too (see <c>NpcBuilder</c>). Left alone, root motion slides the visual out
        /// from under its own collider, its culling and its seed. Harvesting it puts the body and the
        /// logical person back in one place every frame.
        ///
        /// <b>Scaled by the visual's own scale, and that is the part worth reading twice.</b>
        /// Humanoid retargeting produces root motion in the TARGET avatar's units, and Remy's avatar
        /// really is 4.20 m tall - his knockback comes out 2.5× too long in local units, and is then
        /// drawn 0.405× because that is what makes him 1.70 m on screen. Multiplying by the visual's
        /// scale is what makes the transform travel exactly as far as the body appears to. For anyone
        /// who imported at 1.70 m the factor is 1 and this line does nothing.
        ///
        /// Y is dropped on purpose: the clip's vertical is baked into its pose and the arc owns the
        /// height.
        /// </summary>
        private void HarvestRootMotion()
        {
            var visual = animator.transform;
            if (visual == transform) return;

            var local = visual.localPosition;
            if (local.x == 0f && local.z == 0f) return;

            var scale = visual.localScale;
            transform.position += transform.TransformVector(new Vector3(local.x * scale.x, 0f, local.z * scale.z));
            visual.localPosition = Vector3.zero;
        }

        /// <summary>
        /// Back on their feet and back on their route, at the spot they were standing when they were
        /// hit - which is the original's "recovery is free" call, and the reason the crowd never
        /// depletes. The seed was never touched, so the person resumes their own walk rather than
        /// being replaced by a new one.
        /// </summary>
        private void Recover()
        {
            _reaction = null;
            _blood = null;

            // Before the Animator is told anything: Rest hands the budget slot back and switches the
            // Animator on, and the crossfade below has to be the last word on what this skeleton is
            // doing. The other order leaves a body playing Locomotion for one frame and then having
            // its pose overwritten by eleven kinematic bones still standing where they fell.
            if (_ragdoll != null && _ragdoll.Active) _ragdoll.Rest();

            SetOpacity(1f);

            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullCompletely;
                animator.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                animator.CrossFadeInFixedTime(LocomotionState, 0.15f, 0, 0f);
            }

            if (_collider != null) _collider.enabled = true;

            transform.SetPositionAndRotation(_standingPosition, _standingRotation);
            _lastPosition = _standingPosition;
            _state.Position = _standingPosition;
        }

        /// <summary>
        /// Cut the reaction short and stand the person back up wherever they are - used when the body
        /// is culled mid-flight because the player drove off. It is behind the camera by then, so the
        /// alternative is holding a body alive for four seconds to animate a fade nobody sees.
        /// </summary>
        public void AbortRunOver()
        {
            if (Downed) Recover();
        }

        /// <summary>
        /// Fade the body out, by swapping to transparent clones of its own materials.
        ///
        /// A clone rather than a shared material, because two people can be fading at once and they
        /// are at different opacities. The clones are kept for the session and swapped back OUT at
        /// full opacity: a transparent material on a walking pedestrian would cost sorting and
        /// overdraw for the rest of the run to solve a problem that lasts 0.8 seconds.
        /// </summary>
        private void SetOpacity(float opacity)
        {
            if (Mathf.Approximately(opacity, _appliedOpacity)) return;

            if (_renderers == null)
            {
                _renderers = GetComponentsInChildren<Renderer>(true);
                _solidMaterials = new Material[_renderers.Length][];
                _fadeMaterials = new Material[_renderers.Length][];

                for (int i = 0; i < _renderers.Length; i++)
                {
                    _solidMaterials[i] = _renderers[i].sharedMaterials;
                    var clones = new Material[_solidMaterials[i].Length];
                    for (int m = 0; m < clones.Length; m++)
                    {
                        if (_solidMaterials[i][m] == null) continue;
                        clones[m] = MakeTransparent(new Material(_solidMaterials[i][m]));
                    }

                    _fadeMaterials[i] = clones;
                }
            }

            bool solid = opacity >= 0.999f;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;

                if (solid)
                {
                    if (_appliedOpacity < 0.999f) _renderers[i].sharedMaterials = _solidMaterials[i];
                    continue;
                }

                if (_appliedOpacity >= 0.999f) _renderers[i].sharedMaterials = _fadeMaterials[i];
                foreach (var material in _fadeMaterials[i])
                {
                    if (material == null) continue;
                    var colour = material.GetColor(BaseColorId);
                    colour.a = opacity;
                    material.SetColor(BaseColorId, colour);
                }
            }

            _appliedOpacity = opacity;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

        /// <summary>
        /// Flips a URP/Lit material to alpha-blended transparency.
        ///
        /// The keyword is the load-bearing line - URP picks the variant by keyword, so setting
        /// <c>_Surface</c> alone changes the inspector and nothing else.
        /// </summary>
        private static Material MakeTransparent(Material material)
        {
            material.SetFloat(SurfaceId, 1f);
            material.SetFloat(ZWriteId, 0f);
            material.SetFloat(SrcBlendId, (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat(DstBlendId, (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return material;
        }

        // --- the clip ----------------------------------------------------------------------------

        private static readonly Dictionary<RuntimeAnimatorController, AnimationClip> HitClips = new();

        /// <summary>
        /// The knockdown clip out of this character's own controller.
        ///
        /// Looked up rather than serialized so there is one source of truth - the graph
        /// <c>NpcAnimatorBuilder</c> writes - and cached per controller because six characters share
        /// six override controllers and the lookup is a linear scan of every clip in each.
        /// </summary>
        private AnimationClip HitClip()
        {
            var controller = animator.runtimeAnimatorController;
            if (controller == null) return null;
            if (HitClips.TryGetValue(controller, out var cached)) return cached;

            AnimationClip found = null;
            foreach (var clip in controller.animationClips)
            {
                if (clip == null || clip.name != HitClipName) continue;
                found = clip;
                break;
            }

            HitClips[controller] = found;
            return found;
        }

        /// <summary>
        /// Which way this clip throws the body, in degrees about Y, measured off its own root motion.
        /// Zero would mean "straight ahead of where the victim was facing"; this one is authored
        /// sideways, so it is near a right angle.
        /// </summary>
        public static float ThrowYaw(AnimationClip clip)
        {
            if (clip == null) return 0f;
            var travel = clip.averageSpeed;
            if (travel.x * travel.x + travel.z * travel.z < 1e-6f) return 0f;
            return Mathf.Atan2(travel.x, travel.z) * Mathf.Rad2Deg;
        }
    }
}
