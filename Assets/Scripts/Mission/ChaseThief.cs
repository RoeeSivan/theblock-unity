using System.Collections.Generic;
using TheBlock.Core;
using UnityEngine;

namespace TheBlock.Missions
{
    /// <summary>
    /// The fleeing thief - the port of <c>src/mission/chase-thief.ts</c>. One character, two bodies:
    /// a jetski he rides along the authored route, and a runner he becomes when he beaches.
    ///
    /// <b>He cannot be lost on distance, and that is the user's own call in the original.</b> Both
    /// phases rubber-band their speed against the chaser: ride his wake and he bolts at 17 m/s
    /// (against your 18, so a close chase still slowly reels him in), fall behind and he crawls at
    /// 8. Only the clock can fail the run. The web's config comment is explicit that this is a
    /// decision rather than a difficulty curve.
    ///
    /// <b>He has no body at all</b>, exactly as the web's does not: nothing ever collides with the
    /// thief, so physics would be cost with no purchase. He skims the buoys through
    /// <see cref="BuoyField"/> - the web's own radial push-out, which this port shares with the
    /// PLAYER's ski instead of writing twice. See that file for why the collider-based version the
    /// plan wanted does not work.
    /// </summary>
    public class ChaseThief : MonoBehaviour
    {
        public enum Phase
        {
            Idle,
            Riding,
            Running,
        }

        [SerializeField] private Transform ski;
        [SerializeField] private Transform runner;
        [SerializeField] private Animator runnerAnimator;

        private static readonly int SpeedParameter = Animator.StringToHash("Speed");

        private TheBlockConfig.ChaseSpec _spec;
        private float _seaLevel;
        private float _floatY;

        private Phase _phase = Phase.Idle;
        private bool _frozen;
        private int _waypoint;
        private int _runDirection = 1;
        private float _yaw;
        private float _bobT;
        private Vector3 _position;

        public Phase Current => _phase;
        public Vector3 Position => _position;

        /// <summary>Configured by the chase before the first frame.</summary>
        public void Setup(TheBlockConfig.ChaseSpec spec, float seaLevel, float hullHalfHeight)
        {
            _spec = spec;
            _seaLevel = seaLevel;
            _floatY = hullHalfHeight;
            PlaceIdle();
        }

        /// <summary>
        /// Back at the spawn, cruising his slow circle, VISIBLE from the moment the world loads.
        ///
        /// That visibility is the design: the player has to be able to see who they are about to
        /// chase before the mission starts, which is why the web places him at boot rather than on
        /// start().
        /// </summary>
        public void PlaceIdle()
        {
            if (_spec == null) return;

            _position = Convert.Pos(_spec.ThiefSpawn.Raw);
            _position.y = _seaLevel + _floatY;

            // Facing the first waypoint. Convert.Pos has already mirrored both points, so the angle
            // between them is a Unity angle - this must NOT re-apply the web's own atan2 sign trick.
            var first = Convert.Pos(_spec.Route[0].Raw);
            _yaw = Mathf.Atan2(first.x - _position.x, first.z - _position.z) * Mathf.Rad2Deg;

            if (ski != null)
            {
                ski.gameObject.SetActive(true);
                ski.SetPositionAndRotation(_position, Quaternion.Euler(0f, _yaw, 0f));
            }

            if (runner != null) runner.gameObject.SetActive(false);
        }

        public void Begin()
        {
            if (_phase == Phase.Idle) _phase = Phase.Riding;
        }

        /// <summary>Caught, or out of time. Stops him where he stands.</summary>
        public void Freeze()
        {
            _frozen = true;
            if (runnerAnimator != null) runnerAnimator.SetFloat(SpeedParameter, 0f);
        }

        public void ResetRun()
        {
            _phase = Phase.Idle;
            _frozen = false;
            _waypoint = 0;
            _runDirection = 1;
            _bobT = 0f;
            PlaceIdle();
        }

        /// <summary>Steps the active phase. <paramref name="chaser"/> is the ski, or the player on foot.</summary>
        public void Tick(float dt, Vector3 chaser)
        {
            if (_spec == null || _frozen) return;

            switch (_phase)
            {
                case Phase.Idle: TickIdle(dt); break;
                case Phase.Riding: TickRiding(dt, chaser); break;
                default: TickRunning(dt, chaser); break;
            }
        }

        /// <summary>
        /// The pre-chase loop: constant forward speed plus a constant yaw rate (speed / radius)
        /// traces a circle. He is cruising, not frozen - a thief idling dead still on open water
        /// reads as a prop.
        /// </summary>
        private void TickIdle(float dt)
        {
            var omega = _spec.IdleCircle.Speed / Mathf.Max(0.01f, _spec.IdleCircle.Radius);
            _yaw += omega * Mathf.Rad2Deg * dt;
            Advance(_spec.IdleCircle.Speed, dt);
            PoseSki();
        }

        private void TickRiding(float dt, Vector3 chaser)
        {
            var target = Convert.Pos(_spec.Route[_waypoint].Raw);
            var flatDistance = Distance2D(_position, target);

            if (flatDistance <= _spec.WaypointRadius)
            {
                _waypoint++;
                if (_waypoint < _spec.Route.Count) return;

                // The last route point IS the beach landing. Swap bodies and start the jog.
                _phase = Phase.Running;
                _waypoint = 0;
                if (ski != null) ski.gameObject.SetActive(false);
                if (runner != null)
                {
                    runner.gameObject.SetActive(true);
                    runner.position = new Vector3(_position.x, 0f, _position.z);
                }

                return;
            }

            var wanted = Mathf.Atan2(target.x - _position.x, target.z - _position.z) * Mathf.Rad2Deg;
            _yaw = Mathf.MoveTowardsAngle(_yaw, wanted, _spec.TurnRate * Mathf.Rad2Deg * dt);
            Advance(RideSpeed(chaser), dt);
            PoseSki();
        }

        /// <summary>The rubber band: close chaser → fast thief, far chaser → a crawl.</summary>
        private float RideSpeed(Vector3 chaser)
        {
            var band = _spec.RubberBand;
            var d = Distance2D(chaser, _position);
            var t = Mathf.Clamp01((band.FarDist - d) / Mathf.Max(0.01f, band.FarDist - band.NearDist));
            return Mathf.Lerp(band.MinSpeed, band.MaxSpeed, t);
        }

        private void TickRunning(float dt, Vector3 chaser)
        {
            if (_spec.RunPath.Count == 0) return;

            // Clamped on the way IN, not only on the way out. The two phases share `_waypoint` and
            // they index different lists - an 18-point route and a 3-point sand path - so any state
            // that survives the hand-off indexes the short one with a long one's cursor and throws
            // every frame. Found exactly that way: a forced phase change during a measurement left
            // the cursor at 17 and the mission threw from Update until Play stopped.
            _waypoint = Mathf.Clamp(_waypoint, 0, _spec.RunPath.Count - 1);

            var target = Convert.Pos(_spec.RunPath[_waypoint].Raw);
            var flat = Distance2D(_position, target);

            if (flat <= 0.8f)
            {
                // Past the end he ping-pongs between the last two sand points. The TIMER is the real
                // fail, so he must never simply stop and become uncatchable-by-standing-still.
                if (_runDirection > 0 && _waypoint >= _spec.RunPath.Count - 1) _runDirection = -1;
                else if (_runDirection < 0 && _waypoint <= 0) _runDirection = 1;
                _waypoint = Mathf.Clamp(_waypoint + _runDirection, 0, _spec.RunPath.Count - 1);
                return;
            }

            var speed = Distance2D(chaser, _position) > _spec.Run.SlowDist
                ? _spec.Run.SlowSpeed
                : _spec.Run.BaseSpeed;

            var step = (target - _position);
            step.y = 0f;
            _position += step.normalized * (speed * dt);
            _position.y = 0f;

            if (runner == null) return;
            runner.position = _position;

            var wanted = Quaternion.LookRotation(new Vector3(step.x, 0f, step.z).normalized, Vector3.up);
            runner.rotation = Quaternion.RotateTowards(runner.rotation, wanted, 480f * dt);

            // He is on Joe's sprint now, not the crowd's walk, and its stride is already retimed to
            // `run.baseSpeed` by ThiefBuilder. So the parameter only has to answer "is he running",
            // measured against his SLOW speed rather than his base one: at 1.2 m/s and above he is
            // fleeing and gets the whole clip, and only a genuine stop settles him back to idle.
            // Normalising by baseSpeed instead - which is what the crowd tree wanted - would blend
            // 60% idle into his eased approach and leave him wading up the beach.
            if (runnerAnimator != null)
                runnerAnimator.SetFloat(
                    SpeedParameter, Mathf.Clamp01(speed / Mathf.Max(0.01f, _spec.Run.SlowSpeed)));
        }

        private void Advance(float speed, float dt)
        {
            var forward = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            _position += forward * (speed * dt);

            // Skim, never through. His route runs him past his own gates by design.
            _position = BuoyField.PushOut(_position);

            _bobT += dt * 1.6f;
            var bobScale = 1f - Mathf.Min(1f, speed / Mathf.Max(1f, _spec.RubberBand.MaxSpeed * 0.4f));
            _position.y = _seaLevel + _floatY + Mathf.Sin(_bobT) * 0.06f * bobScale;
        }

        private void PoseSki()
        {
            if (ski == null) return;
            ski.SetPositionAndRotation(_position, Quaternion.Euler(0f, _yaw, 0f));
        }

        private static float Distance2D(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Editor-side wiring, used by The Block → Build Campaign.</summary>
        public void SetBodies(Transform skiBody, Transform runBody, Animator runAnimator)
        {
            ski = skiBody;
            runner = runBody;
            runnerAnimator = runAnimator;
        }
    }
}
