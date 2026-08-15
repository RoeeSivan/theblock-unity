using TheBlock.Vfx;
using UnityEngine;

namespace TheBlock.Npc
{
    /// <summary>
    /// One pedestrian's run-over: launched, lands, lies there, fades out. The port of the original's
    /// <c>npc/run-over.ts</c>, and the same split of responsibilities — <see cref="Pedestrian"/> owns
    /// the body, the animator and the lifecycle; this owns the MOTION and the CLOCK, and touches
    /// nothing it was not handed.
    ///
    /// <b>The motion is split between the clip and the code, and the split is the design.</b> The
    /// clip supplies the pose and its own authored horizontal travel — as real Unity root motion, see
    /// <c>HitClipImporter</c> — so the limbs and the landing are frame-exact by construction and
    /// there is no code-side pose clock to drift against. The code supplies only the two things the
    /// clip lacks: a vertical arc (the clip's own vertical is flat, measured at +3 mm in the web
    /// build) and a speed-scaled extra shove, so a hit at 100 km/h throws further than one at 15.
    ///
    /// <b>The phases hang off the clip's own length</b>, not off hand-picked durations, so re-trimming
    /// the FBX cannot desync them.
    /// </summary>
    public sealed class RunOverReaction
    {
        /// <summary>Everything tunable, serialized once on <see cref="RunOverSystem"/>.</summary>
        [System.Serializable]
        public struct Tuning
        {
            [Tooltip("Seconds from impact to touchdown. NOT a feel number: measured off this clip's " +
                     "own RootT.y, which reaches its final lying height at frame 15 of 34 @ 30 fps. " +
                     "The arc therefore returns to zero on exactly the frame the animation lands, " +
                     "which is what stops the body touching down twice. flightTime.")]
            public float FlightTime;

            [Tooltip("Metres at the apex of the arc the code adds on top of the clip. arcHeight.")]
            public float ArcHeight;

            [Tooltip("Extra world-space push along the car's forward, in metres per m/s of vehicle " +
                     "speed, on top of the clip's own travel. pushPerSpeed.")]
            public float PushPerSpeed;

            [Tooltip("Ceiling on that push, metres. pushMax.")]
            public float PushMax;

            [Tooltip("Seconds face-down before the fade starts. The clip clamps on its final frame, " +
                     "so this is the body lying still. lieTime.")]
            public float LieTime;

            [Tooltip("Seconds to fade the body out. fadeTime.")]
            public float FadeTime;

            public static Tuning Default => new()
            {
                FlightTime = 0.5f,
                ArcHeight = 1.1f,
                PushPerSpeed = 0.06f,
                PushMax = 4f,
                LieTime = 2f,
                FadeTime = 0.8f,
            };
        }

        private Tuning _tuning;
        private Transform _body;
        private Vector3 _throw;      // unit, horizontal — the direction the vehicle was travelling
        private float _groundY;      // the street under the victim at the moment of impact
        private float _push;         // total extra metres this hit is worth
        private float _pushed;       // how much of it has been applied
        private float _clipTime;
        private float _lieUntil;
        private float _fadeUntil;
        private float _t;
        private bool _stained;

        /// <summary>True once the body has faded out and the person can be handed back.</summary>
        public bool Done { get; private set; }

        /// <summary>True from the step the body touches down. The measurement hangs off this.</summary>
        public bool Landed { get; private set; }

        /// <summary>0..1, applied to the body's materials by <see cref="Pedestrian"/>.</summary>
        public float Opacity { get; private set; } = 1f;

        /// <summary>How long this reaction lasts in total — the stain's budget, and the cull check's.</summary>
        public float Duration => _fadeUntil;

        public void Begin(
            Transform body, Vector3 throwDirection, float speedMs, float groundY, float clipLength,
            in Tuning tuning, Blood blood)
        {
            _tuning = tuning;
            _body = body;
            _groundY = groundY;
            _clipTime = Mathf.Max(0.1f, clipLength);
            _lieUntil = _clipTime + _tuning.LieTime;
            _fadeUntil = _lieUntil + _tuning.FadeTime;
            _push = Mathf.Min(Mathf.Abs(speedMs) * _tuning.PushPerSpeed, _tuning.PushMax);
            _pushed = 0f;
            _t = 0f;
            _stained = false;
            _throw = new Vector3(throwDirection.x, 0f, throwDirection.z).normalized;
            Opacity = 1f;
            Done = false;
            Landed = false;

            // Spray on the impact frame, thrown the way the body is. Fire and forget: it cannot
            // outlive the reaction, and the reaction does not wait on it.
            if (blood != null) blood.Splash(body.position, _throw);
        }

        /// <summary>
        /// One step, called from <see cref="CrowdSpawner"/>'s LateUpdate — <b>after</b> the animator
        /// has run and after <see cref="Pedestrian"/> has harvested the clip's root motion onto the
        /// transform. The order matters: this adds to where the clip has already put the body.
        /// </summary>
        public void Tick(float dt, Blood blood)
        {
            if (Done || _body == null) return;

            _t += dt;

            var position = _body.position;

            if (_t < _tuning.FlightTime)
            {
                // Push is applied as the DIFFERENCE from where it should be by now, so it composes
                // with the root motion the clip has already contributed this frame instead of
                // overwriting it.
                float k = _t / _tuning.FlightTime;
                float wanted = _push * k;
                position += _throw * (wanted - _pushed);
                _pushed = wanted;

                // 4k(1-k) is a unit parabola: 0 at launch, 1 at the apex, 0 on touchdown.
                position.y = _groundY + _tuning.ArcHeight * 4f * k * (1f - k);
            }
            else
            {
                position += _throw * (_push - _pushed);
                _pushed = _push;
                position.y = _groundY;   // settle exactly on the road so the lying pose cannot float
            }

            _body.position = position;

            // The stain belongs where the body STOPS, not where the car hit it — those are metres
            // apart. Its budget is exactly the time left before this person gets up and walks away,
            // so it can never still be on the road while its own victim strolls past it.
            if (!_stained && _t >= _tuning.FlightTime)
            {
                _stained = true;
                Landed = true;
                if (blood != null) blood.Pool(position, _groundY, _fadeUntil - _t);
            }

            if (_t > _lieUntil)
            {
                Opacity = Mathf.Clamp01((_fadeUntil - _t) / Mathf.Max(0.01f, _tuning.FadeTime));
                if (_t >= _fadeUntil) Done = true;
            }
        }
    }
}
