using TheBlock.Vfx;
using UnityEngine;

namespace TheBlock.Npc
{
    /// <summary>
    /// One pedestrian's run-over, simulated: launched by the car, tumbles, settles, lies there, fades
    /// out. The ragdoll twin of <see cref="RunOverReaction"/>, and deliberately the same shape - same
    /// interface, same phases, same fade - so that turning ragdolls off in Settings restores the old
    /// behaviour rather than routing it through new code (U35's off-switch rule).
    ///
    /// <b>What is different is where the clock comes from.</b> The clip's phases hang off the clip's
    /// own length, which is known the moment the hit lands. A ragdoll's do not: how long a body takes
    /// to stop depends on what it hit, so the settle is MEASURED (mean bone speed under a threshold,
    /// held for a moment) and bounded by a timeout. A body wedged against a bollard, still twitching
    /// against its own joint limits, must not lie there for the rest of the session.
    ///
    /// <b>The stain is stamped where the pelvis stops</b>, exactly as the clip version stamps it where
    /// the body stops - but the pelvis is metres from the pedestrian's own transform by then, which is
    /// the one place the two implementations cannot share a line.
    /// </summary>
    public sealed class RagdollReaction : IRunOverReaction
    {
        /// <summary>Everything tunable, serialized once on <see cref="RunOverSystem"/>.</summary>
        [System.Serializable]
        public struct Tuning
        {
            [Tooltip("Fraction of the vehicle's speed the body leaves with. Not 1: a person struck by " +
                     "a car does not keep up with it, and a body launched at the full 20 m/s of a " +
                     "speeding Mustang clears the pavement and lands on a roof. launchShare.")]
            public float LaunchShare;

            [Tooltip("Ceiling on that launch, m/s. The difference between a hit at 60 km/h and one at " +
                     "120 is not worth twice the distance.")]
            public float LaunchMax;

            [Tooltip("Upward m/s added on top, so the body is scooped by the bumper rather than " +
                     "shoved along the tarmac. A car's nose is a wedge and this is that wedge.")]
            public float Lift;

            [Tooltip("Impulse in newton-seconds applied at the contact point, which is what makes the " +
                     "body spin instead of flying like a plank. Newton-seconds because the bodies " +
                     "carry real masses - about 70 kg all told.")]
            public float SpinImpulse;

            [Tooltip("Mean bone speed below which the body counts as still, m/s.")]
            public float SettleSpeed;

            [Tooltip("Seconds it must stay below that before the body is called settled. Without a " +
                     "hold, one frame of a bounce apex reads as stillness.")]
            public float SettleHold;

            [Tooltip("Seconds after which the body is settled whatever it is doing - a limb twitching " +
                     "against a joint limit never gets under the threshold on its own.")]
            public float SettleTimeout;

            [Tooltip("Seconds lying still before the fade starts, after the settle. lieTime.")]
            public float LieTime;

            [Tooltip("Seconds to fade the body out. fadeTime.")]
            public float FadeTime;

            public static Tuning Default => new()
            {
                LaunchShare = 0.55f,
                LaunchMax = 11f,
                Lift = 2.4f,
                SpinImpulse = 45f,
                SettleSpeed = 0.35f,
                SettleHold = 0.4f,
                SettleTimeout = 5f,
                LieTime = 2f,
                FadeTime = 0.8f,
            };
        }

        private Tuning _tuning;
        private Ragdoll _rig;
        private float _groundY;
        private float _t;
        private float _stillFor;
        private float _lieUntil;
        private float _fadeUntil;

        /// <summary>True once the body has faded out and the person can be handed back.</summary>
        public bool Done { get; private set; }

        /// <summary>True from the step the body comes to rest. The stain hangs off this.</summary>
        public bool Landed { get; private set; }

        /// <summary>0..1, applied to the body's materials by <see cref="Pedestrian"/>.</summary>
        public float Opacity { get; private set; } = 1f;

        /// <summary>PhysX owns the bones; the pedestrian's transform is not to be touched.</summary>
        public bool DrivesTransform => false;

        /// <summary>Where the body is now - the pelvis, for the stain and for the log.</summary>
        public Vector3 Position => _rig != null ? _rig.Position : Vector3.zero;

        public void Begin(
            Ragdoll rig, Vector3 throwDirection, float speedMs, Vector3 hitPoint, float groundY,
            in Tuning tuning, Blood blood)
        {
            _tuning = tuning;
            _rig = rig;
            _groundY = groundY;
            _t = 0f;
            _stillFor = 0f;
            _lieUntil = 0f;
            _fadeUntil = 0f;
            Opacity = 1f;
            Done = false;
            Landed = false;

            var flat = new Vector3(throwDirection.x, 0f, throwDirection.z).normalized;
            float launch = Mathf.Min(Mathf.Abs(speedMs) * _tuning.LaunchShare, _tuning.LaunchMax);
            var velocity = flat * launch + Vector3.up * _tuning.Lift;

            // Off-centre, so the impulse is a torque about the pelvis as well as a shove. Dead centre
            // it would only add to the velocity every bone already has.
            var spin = (flat * 0.7f + Vector3.up * 0.7f).normalized * _tuning.SpinImpulse;

            RagdollBudget.Take(rig);
            rig.Activate(velocity, hitPoint, spin);

            // Spray on the impact frame, thrown the way the body is - the clip version's line, and it
            // has to stay identical or the two paths look different at the one moment they overlap.
            if (blood != null) blood.Splash(hitPoint, flat);
        }

        public void Tick(float dt, Blood blood)
        {
            if (Done || _rig == null) return;

            _t += dt;

            if (!Landed)
            {
                // A frozen body is settled by definition: nothing is going to move it again.
                bool still = _rig.Frozen || _rig.Speed <= _tuning.SettleSpeed;
                _stillFor = still ? _stillFor + dt : 0f;

                if (_stillFor < _tuning.SettleHold && _t < _tuning.SettleTimeout) return;

                Landed = true;
                _lieUntil = _t + _tuning.LieTime;
                _fadeUntil = _lieUntil + _tuning.FadeTime;

                // The stain belongs where the body STOPS, and where it stopped is the pelvis - the
                // pedestrian's own transform never left the spot the bumper found them.
                if (blood != null)
                {
                    var at = _rig.Position;
                    blood.Pool(at, Mathf.Min(_groundY, at.y), _fadeUntil - _t);
                }

                return;
            }

            if (_t <= _lieUntil) return;

            Opacity = Mathf.Clamp01((_fadeUntil - _t) / Mathf.Max(0.01f, _tuning.FadeTime));
            if (_t >= _fadeUntil) Done = true;
        }
    }
}
