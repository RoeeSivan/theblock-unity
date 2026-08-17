using TheBlock.Vehicles;
using UnityEngine;

namespace TheBlock.Police
{
    /// <summary>
    /// The police H145 that answers the third star: it lifts off the station pad, holds a slot
    /// above and behind you, and pins you with a real shadowed spotlight until the star bleeds.
    ///
    /// <b>It is deliberately NOT a <see cref="CopCar"/>, and deliberately NOT driven by
    /// <see cref="HelicopterController"/>.</b> Both would have been the obvious reuse and both are
    /// wrong here:
    ///
    /// <list type="bullet">
    /// <item><c>CopCar</c> requires a <c>CarController</c> and a <c>CopDriver</c>, and about twenty
    /// sites in <see cref="PoliceSystem"/> reach through them into wheels, lane snapping and bay
    /// parking. <c>CopCar.LooksWrecked</c> alone would end this unit: it declares a car wrecked
    /// once its up-vector tips past 0.3, which any banking aircraft does.</item>
    /// <item><c>HelicopterController</c> reads <c>Keyboard.current</c> inside its own
    /// <c>FixedUpdate</c> and has no input seam to drive from. Adding one would mean touching the
    /// player's flyable Huey - which is user-confirmed and working - to gain nothing, because a
    /// craft that never lands, never collides and is never entered does not need a flight model.
    /// It needs to LOOK right.</item>
    /// </list>
    ///
    /// So this is transform-driven with <c>SmoothDamp</c>, and it carries no Rigidbody and no
    /// collider at all. That also gets `enterable = false` for free: the prefab simply never has
    /// <c>HelicopterController</c> on it, and that component is the only thing that ever calls
    /// <see cref="EnterableRegistry.Register"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class PoliceHelicopter : MonoBehaviour
    {
        public enum Mode
        {
            /// <summary>On the pad, rotor stopped, light off. The resting state.</summary>
            Parked,

            /// <summary>Rotor spinning up on the pad, running the scramble delay.</summary>
            Scrambling,

            /// <summary>Climbing out to altitude.</summary>
            Climbing,

            /// <summary>Holding station on the target and lighting it.</summary>
            Hunting,

            /// <summary>Star lost. Flying back to the pad and landing.</summary>
            Returning,
        }

        [Tooltip("Filled by PoliceHelicopterBuilder. The rotors, so they spin while airborne.")]
        [SerializeField] private Rotor rotor;

        [Tooltip("The searchlight under the FLIR ball. Enabled only while airborne.")]
        [SerializeField] private Light searchlight;

        [Tooltip("The translucent beam cone. Without it the spot is nearly invisible in daylight, " +
                 "and daylight is this game's default sky.")]
        [SerializeField] private Transform beam;

        [Tooltip("Metres at which the rotor is inaudible. Beyond the hover slot, so a helicopter " +
                 "arriving is heard before it is seen.")]
        [SerializeField] private float audibleRange = 130f;

        private TheBlock.Audio.RotorSound _audio;
        private PoliceTuning _tuning;
        private Transform _target;
        private Vector3 _pad;
        private Quaternion _padRotation;

        private Vector3 _velocity;          // SmoothDamp's own state, not a physics velocity
        private float _sinceLaunch;
        private float _groundY;
        private float _roofY;
        private float _sinceGroundProbe;

        public Mode State { get; private set; } = Mode.Parked;

        /// <summary>Airborne in any sense - what the map blip and the perf note both care about.</summary>
        public bool Airborne => State == Mode.Climbing || State == Mode.Hunting ||
                                State == Mode.Returning;

        public void Configure(PoliceTuning tuning, Vector3 pad, Quaternion padRotation)
        {
            _tuning = tuning;
            _pad = pad;
            _padRotation = padRotation;
            transform.SetPositionAndRotation(pad, padRotation);
            SetLightsOn(false);
            if (rotor != null) rotor.SetFlying(false);

            // Its own rotor voice. GameAudio's single RotorSound is the PLAYER's cockpit sound and
            // is only fed while the driven vehicle is a HelicopterController - a second aircraft in
            // the world has no path through it at all.
            if (!TryGetComponent(out _audio))
                _audio = gameObject.AddComponent<TheBlock.Audio.RotorSound>();

            var host = TheBlock.Audio.GameAudio.Instance;
            if (host != null) _audio.SetOutput(host.Group(TheBlock.Audio.GameAudio.Bus.Sfx));
            _audio.SetPlacement(0f, 0f);
        }

        /// <summary>Wired by the builder so the fields survive without a prefab editing step.</summary>
        public void Bind(Rotor blades, Light light, Transform cone)
        {
            rotor = blades;
            searchlight = light;
            beam = cone;
        }

        // --- orders ---------------------------------------------------------------------------

        public void Launch(Transform target)
        {
            _target = target;
            if (State == Mode.Parked)
            {
                State = Mode.Scrambling;
                _sinceLaunch = 0f;
                if (rotor != null) rotor.SetFlying(true);
                if (_audio != null) _audio.StartRotor();
                return;
            }

            // Already up, or on its way home when a fresh crime lands: turn it round rather than
            // making it finish the trip. Same argument CopCar.Returning is built on.
            if (State == Mode.Returning) State = Mode.Hunting;
        }

        public void Recall()
        {
            if (State == Mode.Parked || State == Mode.Returning) return;
            State = Mode.Returning;
            SetLightsOn(false);
        }

        // --- flight ---------------------------------------------------------------------------

        private void Update()
        {
            if (_tuning == null) return;
            float dt = Time.deltaTime;

            switch (State)
            {
                case Mode.Parked:
                    return;

                case Mode.Scrambling:
                    _sinceLaunch += dt;
                    if (_sinceLaunch >= _tuning.HeliLaunchDelay)
                    {
                        State = Mode.Climbing;
                        SetLightsOn(true);
                    }

                    return;

                case Mode.Climbing:
                    FlyTo(Slot(), dt);
                    if (transform.position.y >= _pad.y + _tuning.HeliAltitude * 0.75f)
                        State = Mode.Hunting;
                    break;

                case Mode.Hunting:
                    FlyTo(Slot(), dt);
                    break;

                case Mode.Returning:
                    // Cross to the pad at altitude first, then sink onto it. Descending on the way
                    // means flying through whatever is between here and the station.
                    bool overPad = Vector2.Distance(
                        new Vector2(transform.position.x, transform.position.z),
                        new Vector2(_pad.x, _pad.z)) < 6f;
                    var goal = overPad ? _pad : new Vector3(_pad.x, _pad.y + _tuning.HeliAltitude, _pad.z);
                    FlyTo(goal, dt);

                    if (overPad && Vector3.Distance(transform.position, _pad) < 0.6f)
                    {
                        State = Mode.Parked;
                        transform.SetPositionAndRotation(_pad, _padRotation);
                        _velocity = Vector3.zero;
                        if (rotor != null) rotor.SetFlying(false);
                        if (_audio != null) _audio.StopRotor();
                        SetLightsOn(false);
                    }

                    break;
            }

            Probe(dt);
            AimLight();
            PlaceSound();
        }

        /// <summary>
        /// The two raycasts this aircraft needs, four times a second between them.
        ///
        /// Per-frame they would be exactly the sort of cost this unit promised not to add, and
        /// neither answer changes fast: the ground under a car and the roofline it is driving past
        /// are both slow next to a 60 Hz frame.
        /// </summary>
        private void Probe(float dt)
        {
            _sinceGroundProbe += dt;
            if (_sinceGroundProbe < 0.25f || _target == null) return;
            _sinceGroundProbe = 0f;

            // Ground under the target, for the searchlight to aim at.
            var from = _target.position + Vector3.up * 3f;
            _groundY = Physics.Raycast(from, Vector3.down, out var down, 80f,
                                       ~0, QueryTriggerInteraction.Ignore)
                ? down.point.y
                : _target.position.y;

            // The tallest thing under the hover slot, so the slot can be lifted over it.
            var behind = _target.forward;
            behind.y = 0f;
            if (behind.sqrMagnitude < 1e-4f) behind = Vector3.forward;
            var above = _target.position - behind.normalized * _tuning.HeliOffsetBack
                        + Vector3.up * 300f;

            _roofY = Physics.Raycast(above, Vector3.down, out var roof, 320f,
                                     ~0, QueryTriggerInteraction.Ignore)
                ? roof.point.y
                : _target.position.y;
        }

        /// <summary>
        /// Distance and direction into the rotor synth, once a frame.
        ///
        /// This is hand-rolled because it has to be: <see cref="TheBlock.Audio.RotorSound"/>
        /// generates its output in <c>OnAudioFilterRead</c> and OVERWRITES the buffer, so Unity's
        /// own 3D panning is computed and then discarded. See <c>RotorSound.SetPlacement</c>.
        /// </summary>
        private void PlaceSound()
        {
            if (_audio == null) return;

            // The synth's pitch and chop rate ride the disc's own spin-up, so the sound winds up
            // with the blades instead of snapping to full note at lift-off.
            if (rotor != null) _audio.SetThrottle(rotor.Throttle);

            var listener = Object.FindAnyObjectByType<AudioListener>();
            if (listener == null) return;

            var ear = listener.transform;
            var delta = transform.position - ear.position;
            float distance = delta.magnitude;

            // Linear rolloff, squared so it falls away the way a real one does rather than fading
            // out in a straight line the whole way in.
            float t = 1f - Mathf.Clamp01(distance / Mathf.Max(1f, audibleRange));
            _audio.SetPlacement(t * t, Vector3.Dot(delta.normalized, ear.right));
        }

        /// <summary>Metres of air kept between the skids and the highest thing under the slot.</summary>
        private const float RoofClearance = 12f;

        /// <summary>
        /// The hover slot: above the target's ground, back along its own heading, and never inside
        /// a building.
        ///
        /// <b>The roof check is not optional.</b> This aircraft has no collider by design - it never
        /// lands, is never entered and is never rammed - which also means nothing stops it flying
        /// straight through the downtown towers. Those are well over the 34 m hover altitude, so a
        /// chase that ends up downtown would put the helicopter inside a building. One downward ray,
        /// on the same quarter-second clock as the searchlight's ground probe.
        /// </summary>
        private Vector3 Slot()
        {
            if (_target == null) return new Vector3(_pad.x, _pad.y + _tuning.HeliAltitude, _pad.z);

            var behind = _target.forward;
            behind.y = 0f;
            if (behind.sqrMagnitude < 1e-4f) behind = Vector3.forward;
            behind.Normalize();

            var slot = _target.position - behind * _tuning.HeliOffsetBack;
            float wanted = _target.position.y + _tuning.HeliAltitude;
            slot.y = Mathf.Max(wanted, _roofY + RoofClearance);
            return slot;
        }

        private void FlyTo(Vector3 goal, float dt)
        {
            transform.position = Vector3.SmoothDamp(transform.position, goal, ref _velocity,
                                                    _tuning.HeliDamping, _tuning.HeliSpeed, dt);

            // Nose at the target, yaw only. There is no physics to fight, and a level airframe is
            // what keeps the light cone honest - a banking one sweeps its spot off the street.
            var look = (_target != null ? _target.position : _pad) - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude < 1e-4f) return;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, Quaternion.LookRotation(look, Vector3.up), 90f * dt);
        }

        // --- the searchlight ------------------------------------------------------------------

        /// <summary>Swings the beam onto the ground under the target. Aim is per frame; the ground
        /// it aims at comes from <see cref="Probe"/>'s quarter-second clock.</summary>
        private void AimLight()
        {
            if (searchlight == null || !searchlight.enabled || _target == null) return;

            var aim = new Vector3(_target.position.x, _groundY, _target.position.z);
            var dir = aim - searchlight.transform.position;
            if (dir.sqrMagnitude < 1e-4f) return;

            searchlight.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

            if (beam != null)
            {
                // The cone is authored one unit long pointing +Z, so its length IS its Z scale.
                float reach = Mathf.Min(dir.magnitude, _tuning.HeliSpotRange);
                float radius = reach * Mathf.Tan(_tuning.HeliSpotAngle * 0.5f * Mathf.Deg2Rad);
                beam.localScale = new Vector3(radius, radius, reach);
            }
        }

        private void SetLightsOn(bool on)
        {
            if (searchlight != null)
            {
                searchlight.enabled = on;
                if (on)
                {
                    searchlight.spotAngle = _tuning != null ? _tuning.HeliSpotAngle : 24f;
                    searchlight.range = _tuning != null ? _tuning.HeliSpotRange : 70f;
                }
            }

            if (beam != null) beam.gameObject.SetActive(on);
        }
    }
}
