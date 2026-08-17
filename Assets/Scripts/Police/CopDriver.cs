using System.Collections.Generic;
using TheBlock.Traffic;
using TheBlock.Vehicles;
using UnityEngine;

namespace TheBlock.Police
{
    /// <summary>
    /// Drives a police cruiser along a route - the only file in the project that knows steering
    /// geometry.
    ///
    /// <b>It drives the same car the player does.</b> Everything here ends in
    /// <see cref="CarController.SetInput"/>: the AI produces a throttle and a steer between −1 and 1,
    /// and the identical <c>ApplySteering</c> / <c>ApplyDrive</c> turn those into torque. Same
    /// 120°/s rate limit, same speed-sensitive lock, same rear-wheel drive, same 1400 kg. A cop
    /// therefore cannot corner in a way the player's car could not, which is the whole reason the
    /// input seam exists - and it is what makes "good driving wins" true by construction rather than
    /// by tuning.
    ///
    /// <b>Pure pursuit</b> for the steering: aim at a point a speed-dependent distance ahead on the
    /// route and turn the wheels toward it. It is the standard method for exactly this problem and
    /// it degrades gracefully - a route that is stale by a second still produces sane steering,
    /// where a waypoint-chasing controller snaps.
    ///
    /// The web build's pursuit had no acceleration model at all: its commanded speed WAS its speed,
    /// applied straight to the body each frame, 15% above the player's top speed with an intercept
    /// burst 35% above. Here the cop asks for a speed and the engine has to deliver it, which is why
    /// its top speed can sit within 3% of yours and the chase still works.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class CopDriver : MonoBehaviour
    {
        private CarController _car;
        private PoliceTuning _tuning;

        private readonly List<Vector3> _route = new();
        private int _cursor;

        /// <summary>Which flank this cop committed to for the current final approach. See ChooseAim.</summary>
        private float _flankSign = 1f;

        // --- passing a queue ------------------------------------------------------------------------

        private float _blockedFor;
        private float _overtakeUntil;

        /// <summary>True while this cop is out of its lane getting past stopped traffic.</summary>
        public bool Overtaking => Time.time < _overtakeUntil;

        /// <summary>Where the driver is aiming this step. Read by the probe and the gizmos.</summary>
        public Vector3 Aim { get; private set; }

        /// <summary>Metres from the car to its own route, this step. The road-route proof reads this.</summary>
        public float OffRoute { get; private set; }

        /// <summary>Speed the driver asked for this step, m/s.</summary>
        public float TargetSpeed { get; private set; }

        /// <summary>True while the route has been abandoned for a straight run at the target.</summary>
        public bool FinalApproach { get; private set; }

        /// <summary>Route waypoints, live. The planner writes into this list.</summary>
        public List<Vector3> Route => _route;

        public int Cursor => _cursor;

        /// <summary>Where this cop is trying to get to. Set by <see cref="PoliceSystem"/> each step.</summary>
        public Vector3 Target { get; set; }

        /// <summary>Whether the target is visible, which is what allows the straight final run.</summary>
        public bool HasLineOfSight { get; set; }

        /// <summary>Stops the car dead and holds it there - the arrest, and the spawn grace.</summary>
        public bool Halt { get; set; }

        /// <summary>
        /// TRIED AND REVERTED, 2026-08-17, kept only as this note.
        ///
        /// The idea was a speed ceiling while a cruiser manoeuvres out of its bay - a car leaving a
        /// parking space does not do 12 m/s, and the measured overshoot past the lane centre was
        /// 1.5 m. <b>Capping it at 6 m/s made the stall roughly five times worse</b>: 3 s became
        /// 17 s of oscillation, because what the car was actually doing was climbing off a kerb it
        /// had beached on, and at 6 m/s it no longer had the momentum to. The forward box-cast said
        /// "nothing ahead" the whole time, which is what gave it away.
        ///
        /// The egress WAYPOINT is what fixed the station stall; the cap is not needed and is not
        /// wanted. Recorded rather than deleted so it is not rediscovered as an idea.
        /// </summary>
        public float EgressCap { get; set; } = float.MaxValue;

        // --- anti-stuck ---------------------------------------------------------------------------

        private Vector3 _stuckFrom;
        private float _stuckTimer;
        private float _unwedgeUntil;
        private int _unwedgeCount;
        private float _unwedgeWindow;
        private float _unwedgeSteer;

        /// <summary>How many times this cop has had to reverse out of something. Three is a despawn.</summary>
        public int Unwedges => _unwedgeCount;

        public void Configure(PoliceTuning tuning)
        {
            _tuning = tuning;
            _car = GetComponent<CarController>();
            _stuckFrom = transform.position;

            // U28: the ☕ boost is the PLAYER's. This component is added at runtime, after the car's
            // own Awake has already run, so the car cannot work out on its own that it is a cruiser -
            // and a police force that speeds up when you drink coffee to escape it is the opposite of
            // what the item is sold as.
            if (_car != null) _car.MarkAsPolice();
        }

        /// <summary>Throws the current route away. The planner refills it.</summary>
        public void ClearRoute()
        {
            _route.Clear();
            _cursor = 0;
        }

        /// <summary>
        /// Fresh start for a car being sent out again.
        ///
        /// Without this the unwedge count is a running total for the life of the object, so a cop
        /// that got briefly stuck three times over five minutes of separate pursuits retires itself
        /// as wrecked while sitting upright on an empty road.
        /// </summary>
        public void ResetWedges()
        {
            _unwedgeCount = 0;
            _unwedgeWindow = 0f;
            _unwedgeUntil = 0f;
            _stuckFrom = transform.position;
            _stuckTimer = 0f;
            _blockedFor = 0f;
            _overtakeUntil = 0f;
        }

        /// <summary>Called after the planner has refilled <see cref="Route"/>.</summary>
        public void RouteRefreshed() => _cursor = 0;

        private void FixedUpdate()
        {
            if (_tuning == null || _car == null) return;

            float dt = Time.fixedDeltaTime;
            var position = transform.position;
            var forward = transform.forward;
            float speed = _car.ForwardSpeed;

            if (Halt)
            {
                // Brake toward a stop, and hold it there. The handbrake below 0.5 m/s is what keeps
                // a parked cruiser parked on the station's sloped apron instead of creeping.
                _car.SpeedLimitOverride = 0f;
                _car.SetInput(new CarInput(Mathf.Clamp(-speed, -1f, 1f), 0f, Mathf.Abs(speed) < 0.5f));
                _stuckFrom = position;
                _stuckTimer = 0f;
                return;
            }

            if (Unwedging(dt, position, speed)) return;

            AdvanceCursor(position, forward);
            var aim = ChooseAim(position, forward, speed, out bool straightRun);
            FinalApproach = straightRun;
            Aim = aim;

            float steer = SteerToward(aim, position);
            TargetSpeed = ChooseSpeed(position, speed, straightRun);

            // The blue-light allowance, handed to the car itself: asking for 29 m/s means nothing
            // while ApplyDrive is cutting the torque at the config's 20. Dropped to 0 - the config
            // cap - the moment this stops being a run to the scene, so the chase you can still win
            // is fought on exactly the numbers the play-test signed off.
            _car.SpeedLimitOverride = Responding(position) ? _tuning.ResponseSpeed : 0f;

            // Proportional on the speed error: 3 m/s off target is full pedal either way. Negative
            // throttle is the brake while rolling forward and becomes reverse once stopped, which is
            // the arcade convention the player's car already has - and doubles as the way out of a
            // nose-in.
            float throttle = Mathf.Clamp((TargetSpeed - speed) / 3f, -1f, 1f);
            _car.SetInput(new CarInput(throttle, steer));

            TrackBlocked(dt, speed);
            TrackStuck(dt, position);
        }

        // --- steering -----------------------------------------------------------------------------

        /// <summary>
        /// Drops route points the car has already passed.
        ///
        /// Both tests matter: within <see cref="PoliceTuning.WaypointReach"/> covers a point driven
        /// straight over, and "behind me" covers a point cut off on the inside of a corner, which is
        /// the ordinary case and the one that makes a cop otherwise circle back for a kerb it
        /// clipped.
        /// </summary>
        private void AdvanceCursor(Vector3 position, Vector3 forward)
        {
            while (_cursor < _route.Count)
            {
                var to = _route[_cursor] - position;
                to.y = 0f;

                bool reached = to.sqrMagnitude < _tuning.WaypointReach * _tuning.WaypointReach;
                bool behind = Vector3.Dot(to, forward) < 0f && to.sqrMagnitude < 400f;
                if (!reached && !behind) break;

                _cursor++;
            }

            OffRoute = _cursor < _route.Count
                ? Mathf.Sqrt(FlatSqr(_route[_cursor], position))
                : 0f;
        }

        /// <summary>
        /// The point to steer at: a lookahead down the route, or the target's near flank once close
        /// enough to see it.
        ///
        /// The flank offset is what makes the end of a chase read as being pulled over rather than
        /// rammed - the cop comes alongside instead of aiming at your boot. It is ported design, and
        /// it is also the only reason a cop stops beside you at all.
        /// </summary>
        private Vector3 ChooseAim(Vector3 position, Vector3 forward, float speed, out bool straightRun)
        {
            float toTarget = Mathf.Sqrt(FlatSqr(Target, position));
            straightRun = HasLineOfSight && toTarget <= (FinalApproach
                ? _tuning.FinalApproachExit
                : _tuning.FinalApproachEnter);

            if (straightRun)
            {
                var side = Vector3.Cross(Vector3.up, (Target - position).normalized);

                // WHICH flank is chosen once, when the final approach begins, and then held.
                //
                // Recomputing it every step is a limit cycle: the test is which side the cop is
                // already turning toward, so the moment its nose swings past the target the sign
                // flips and the aim point jumps twice SideGap across to the other flank. The car
                // turns after it, swings past again, and orbits. Measured: a cruiser sat between
                // 10.6 and 11.1 m of a stationary player for the rest of the pursuit and never
                // reached the 4 m arrest radius.
                if (!FinalApproach) _flankSign = Vector3.Dot(side, forward) >= 0f ? 1f : -1f;

                return Target + side * (_flankSign * _tuning.SideGap);
            }

            if (_cursor >= _route.Count) return Overtake(Target, position);

            // Walk forward along the route until the lookahead is used up. Measuring along the route
            // rather than as a radius keeps the aim on the road through a corner instead of cutting
            // across the inside of it.
            float lookahead = Mathf.Clamp(
                _tuning.LookaheadBase + _tuning.LookaheadPerSpeed * Mathf.Abs(speed),
                _tuning.LookaheadMin, _tuning.LookaheadMax);

            var at = position;
            for (int i = _cursor; i < _route.Count; i++)
            {
                float step = Mathf.Sqrt(FlatSqr(_route[i], at));
                if (step >= lookahead)
                    return Overtake(Vector3.Lerp(at, _route[i], lookahead / Mathf.Max(step, 0.01f)), position);

                lookahead -= step;
                at = _route[i];
            }

            return Overtake(at, position);
        }

        /// <summary>
        /// Swings the aim point out of the lane so the cop passes whatever is standing in it.
        ///
        /// <b>A police car does not queue.</b> Traffic pulls over for one, but a queue stopped at a
        /// red light has nowhere to pull TO - measured, six cars around a cruiser all at 0.0 m/s, one
        /// of them yielding its full shift and still nose-to-nose with it, and the junction cost 12
        /// seconds of a response. When the traffic cannot move, the cop is what has to.
        ///
        /// It aims into the oncoming side, which is what a real car with its lights on does, and it
        /// is time-boxed rather than latched: after <see cref="PoliceTuning.OvertakeTime"/> it tucks
        /// back into the lane and only pulls out again if it is still stuck. That keeps it from
        /// driving the whole city on the wrong side.
        /// </summary>
        private Vector3 Overtake(Vector3 aim, Vector3 position)
        {
            if (!Overtaking) return aim;

            var along = aim - position;
            along.y = 0f;
            if (along.sqrMagnitude < 0.01f) return aim;

            // Left is the oncoming side on this city's right-hand lanes.
            var side = Vector3.Cross(Vector3.up, along.normalized);
            return aim - side * _tuning.OvertakeShift;
        }

        /// <summary>
        /// Decides when the cop has been standing still long enough to pull out and pass.
        ///
        /// The test is "asking to move and not moving", which is deliberately the same shape as the
        /// wedge detector - but this one fires in a second and a half rather than two, and answers
        /// sideways instead of in reverse, because a queue is not a wall you back off from.
        /// </summary>
        private void TrackBlocked(float dt, float speed)
        {
            if (Overtaking) return;

            bool stuck = TargetSpeed > 2f && Mathf.Abs(speed) < 1.5f;
            _blockedFor = stuck ? _blockedFor + dt : 0f;

            if (_blockedFor < _tuning.OvertakeAfter) return;

            _blockedFor = 0f;
            _overtakeUntil = Time.time + _tuning.OvertakeTime;
        }

        /// <summary>
        /// Pure pursuit: the curvature of the arc through the aim point, as a wheel angle.
        ///
        /// <c>k = 2x / L²</c> is the standard result for the circle through the car that passes
        /// through a point at lateral offset <c>x</c> and range <c>L</c>; the wheel angle for a
        /// curvature is <c>atan(wheelbase · k)</c>. Then it is normalised against the car's own lock
        /// and handed over as an ordinary steering input, so the rate limit and the speed falloff
        /// apply to the cop exactly as they do to the player.
        /// </summary>
        private float SteerToward(Vector3 aim, Vector3 position)
        {
            var local = transform.InverseTransformPoint(new Vector3(aim.x, position.y, aim.z));
            float range = new Vector2(local.x, local.z).magnitude;
            if (range < 0.5f) return 0f;

            float curvature = 2f * local.x / (range * range);
            float degrees = Mathf.Atan(_tuning.Wheelbase * curvature) * Mathf.Rad2Deg;

            // Behind the car, the geometry gives a tiny angle for a point it cannot reach forwards.
            // Full lock is the honest answer there; the reverse comes from the anti-stuck rule.
            if (local.z < 0f) degrees = Mathf.Sign(local.x) * _tuning.MaxSteerDegrees;

            return Mathf.Clamp(degrees / _tuning.MaxSteerDegrees, -1f, 1f);
        }

        // --- speed --------------------------------------------------------------------------------

        /// <summary>
        /// The slowest of what the chase wants, what the corner allows, and what the arrival needs.
        ///
        /// The rubber band is ported design - far away it presses, close in it settles - but the top
        /// of the band is <see cref="PoliceTuning.MaxSpeed"/>, which is 2.5% over the player's rather
        /// than the web build's 15%. Cornering is where a cop is actually beaten.
        ///
        /// <b>Except on the way TO you.</b> A cop past <see cref="PoliceTuning.BandFar"/> with no
        /// line of sight is not chasing anybody yet - it is answering a call across the city, and
        /// holding it to chase numbers is what made the response feel slack. Measured on the drive
        /// in: it asked for the full 20.5 and delivered **13.7 m/s**, so the binding constraint was
        /// never the top speed, it was <see cref="CornerSpeed"/>. Both are raised for that phase and
        /// neither applies once it can see you, so the chase and the escape are byte-for-byte what
        /// the play-test already accepted.
        /// </summary>
        /// <summary>
        /// Running to the scene rather than chasing anyone: past the rubber band, with no eyes on the
        /// target. The one condition the whole urgency allowance hangs on.
        /// </summary>
        private bool Responding(Vector3 position) =>
            !HasLineOfSight && FlatSqr(Target, position) > _tuning.BandFar * _tuning.BandFar;

        private float ChooseSpeed(Vector3 position, float speed, bool straightRun)
        {
            float distance = Mathf.Sqrt(FlatSqr(Target, position));
            bool responding = Responding(position);

            float band = Mathf.InverseLerp(_tuning.BandNear, _tuning.BandFar, distance);
            float wanted = responding
                ? _tuning.ResponseSpeed
                : Mathf.Lerp(_tuning.MinSpeed, _tuning.MaxSpeed, band);

            float corner = CornerSpeed(position, responding ? _tuning.ResponseGrip : _tuning.LateralGrip);
            wanted = Mathf.Min(wanted, corner);

            if (distance < _tuning.ArriveDistance)
                wanted = Mathf.Min(wanted, _tuning.ArriveSpeed);

            // An arrival ramp on the final approach, one metre per second per metre left.
            //
            // Without it there is a dead band between ArriveDistance and BandNear where the rubber
            // band's own floor, MinSpeed, is the answer - so a cop eleven metres from a stationary
            // player asks for 8 m/s, overshoots the flank it is aiming at, and has to come round for
            // another pass. Measured at exactly that: 8 m/s commanded, 1.5 m/s delivered, and the
            // 4 m arrest radius never reached before the pursuit expired.
            if (straightRun)
                wanted = Mathf.Min(wanted, Mathf.Max(_tuning.ArriveSpeed, distance - _tuning.ArrestRadius));

            return Mathf.Min(wanted, EgressCap);
        }

        /// <summary>
        /// How fast this cop may be going RIGHT NOW given the corners ahead of it.
        ///
        /// <c>v = sqrt(a / k)</c> is the speed at which a curvature <c>k</c> costs a lateral
        /// acceleration <c>a</c>. The grip is the dial: raise it and cops take corners flat, lower it
        /// and a bend is a real place to lose one. It is a parameter rather than a field read because
        /// the run to the scene and the chase itself want different answers - see
        /// <see cref="ChooseSpeed"/>.
        ///
        /// <b>MEASURED AND FIXED 2026-08-17, on a user report that the police simply do not arrive.</b>
        /// This method used to return the tightest corner in the next 25 m and apply it immediately,
        /// which is not how a car works: a hairpin twenty metres away does not stop you driving at
        /// it, it makes you brake before you get there. Sampled live, a real plan from the station
        /// to the player contained a <b>143° turn across 2.5 m</b> - a lane-width U-turn the planner
        /// had chosen, rendered as a hairpin - and it capped the cruiser at <b>3.8 m/s for the whole
        /// 25 m approach</b>. That is the whole of the "the cop never arrives" complaint: the run in
        /// was being throttled by a corner nobody had reached.
        ///
        /// So the limit is now a BRAKING one. For a corner at distance <c>d</c> whose own limit is
        /// <c>v_c</c>, the speed permitted here is <c>sqrt(v_c² + 2·b·d)</c> - the classic
        /// stop-in-time relation. Right at the corner it reduces to <c>v_c</c> exactly, so corners
        /// are still taken at the same speed as before and nothing about the chase gets easier; it
        /// is only the approach that stops being crawled. That same 3.8 m/s hairpin, 20 m out at
        /// <c>b</c> = 6.5, now permits <b>16.3 m/s</b>.
        /// </summary>
        private float CornerSpeed(Vector3 position, float grip)
        {
            float best = Mathf.Max(_tuning.MaxSpeed, _tuning.ResponseSpeed);
            float scanned = 0f;
            var previous = position;

            for (int i = _cursor; i < _route.Count - 1 && scanned < _tuning.CornerScan; i++)
            {
                var a = _route[i] - previous;
                var b = _route[i + 1] - _route[i];
                a.y = b.y = 0f;

                float lengthA = a.magnitude;
                float lengthB = b.magnitude;
                float reach = scanned;      // arc length from HERE to this corner
                scanned += lengthB;
                previous = _route[i];
                if (lengthA < 0.1f || lengthB < 0.1f) continue;

                float turn = Vector3.Angle(a, b) * Mathf.Deg2Rad;
                if (turn < 0.02f) continue;

                // Curvature of the arc that turns by `turn` over the length of the second leg.
                float curvature = turn / lengthB;
                float corner = Mathf.Sqrt(grip / Mathf.Max(curvature, 1e-4f));

                // What that corner allows HERE, given there is room to brake into it.
                float allowed = Mathf.Sqrt(corner * corner + 2f * _tuning.BrakeDecel * reach);
                if (allowed < best) best = allowed;
            }

            return Mathf.Max(best, _tuning.MinSpeed * 0.5f);
        }

        // --- anti-stuck ---------------------------------------------------------------------------

        /// <summary>
        /// Reverses out of whatever the cop has buried itself in.
        ///
        /// <b>Not the web build's fix.</b> That one stepped the kinematic body straight through the
        /// geometry when collide-and-slide gave up, on the grounds that a pursuer which can enter an
        /// unrecoverable state is worse than one which clips a corner. A dynamic Rigidbody has an
        /// honest way out - reverse, with the steering mirrored so the nose swings off whatever it is
        /// against - so the clipping does not carry over. Three of these in a short window means this
        /// cop is not getting out, and <see cref="PoliceSystem"/> retires it.
        /// </summary>
        private bool Unwedging(float dt, Vector3 position, float speed)
        {
            if (Time.time >= _unwedgeUntil) return false;

            _car.SetInput(new CarInput(-1f, _unwedgeSteer));
            _stuckFrom = position;
            _stuckTimer = 0f;
            return true;
        }

        private void TrackStuck(float dt, Vector3 position)
        {
            if (_unwedgeWindow > 0f)
            {
                _unwedgeWindow -= dt;
                if (_unwedgeWindow <= 0f) _unwedgeCount = 0;
            }

            _stuckTimer += dt;
            if (_stuckTimer < _tuning.StuckWindow) return;

            float travelled = Mathf.Sqrt(FlatSqr(position, _stuckFrom));
            _stuckFrom = position;
            _stuckTimer = 0f;

            if (travelled >= _tuning.StuckTravel || TargetSpeed <= 2f) return;

            _unwedgeUntil = Time.time + _tuning.UnwedgeTime;
            _unwedgeSteer = Random.value < 0.5f ? -1f : 1f;
            _unwedgeCount++;
            _unwedgeWindow = _tuning.UnwedgeWindow;
        }

        private static float FlatSqr(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
