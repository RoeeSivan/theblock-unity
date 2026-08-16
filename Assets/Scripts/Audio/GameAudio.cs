using UnityEngine;
using UnityEngine.Audio;
using TheBlock.Core;
using TheBlock.Vehicles;
using TheBlock.World;

namespace TheBlock.Audio
{
    /// <summary>
    /// The game's audio host: one object in the scene that owns the mixer, the library and every
    /// system that makes a noise, and the only thing any of them has to be found through.
    ///
    /// <b>It is the port of <c>audio-context.ts</c>'s job, not its mechanism.</b> That file exists
    /// because browsers cap the number of <c>AudioContext</c>s and charge an audio render thread for
    /// each - the web build had grown SIX and was killing the tab on an iPad at the exact moment of
    /// getting into a car. Unity has one audio system and no such cap, so nothing here is
    /// rationing contexts. What it inherits is the OTHER half of that file's value: one place that
    /// every sound goes through, so the mix is a thing you can reason about rather than a set of
    /// call sites that each chose a gain.
    ///
    /// <b>The state machine drives snapshots, and it POLLS.</b> Four enum comparisons a frame is
    /// free, and an event would give the game two ways to say where the player is - the same
    /// argument <c>MissionFeedback</c> makes for the same reason. Driving, the interior and the
    /// dance each duck the Ambient bus by <c>config.ambientAudio.duck</c>, which is now one mixer
    /// parameter rather than a multiply at every ambient call site.
    ///
    /// <b>The engine is polled too, and that is what keeps <c>VehicleEnterExit</c> untouched.</b> A
    /// vehicle machine that had to remember to start a sound is a vehicle machine with an audio
    /// responsibility; watching its <see cref="VehicleEnterExit.Mode"/> costs nothing and cannot
    /// fall out of step with it.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameAudio : MonoBehaviour
    {
        /// <summary>The mixer groups, by name. <see cref="Bus.Radio"/> is declared and unused.</summary>
        public enum Bus
        {
            Master,
            Music,
            Voice,
            Sfx,
            Engine,
            Ambient,
            Radio,
        }

        private static GameAudio _instance;

        private static int _searchedFrame = -1;

        /// <summary>
        /// The one in the scene, or null. Null is a legitimate answer - an editor tool or a test
        /// scene has no audio host, and a missing sound must never be what stops a mission starting.
        ///
        /// <b>It re-finds itself, and that is not defensive padding.</b> A script recompile while the
        /// editor is in Play triggers a domain reload: every static field is wiped and
        /// <c>Awake</c> does NOT run again, so a plain <c>_instance</c> is null for the rest of the
        /// session and every <see cref="Cue"/> in the game silently stops making a sound. That was
        /// measured here mid-build - the whole mix went quiet with no error to explain it. The
        /// re-find costs one <c>FindAnyObjectByType</c> per FRAME at worst, guarded so a scene with
        /// genuinely no host does not search on every call.
        /// </summary>
        public static GameAudio Instance
        {
            get
            {
                if (_instance != null) return _instance;
                if (_searchedFrame == Time.frameCount) return null;
                _searchedFrame = Time.frameCount;
                _instance = FindAnyObjectByType<GameAudio>();
                return _instance;
            }
        }

        [Header("Assets")]
        [SerializeField] private AudioMixer mixer;
        [SerializeField] private AudioLibrary library;

        [Tooltip("The looping siren every cop car plays. Assets/Audio/Sfx/siren-loop.mp3.")]
        [SerializeField] private AudioClip sirenClip;

        [Header("Systems")]
        [SerializeField] private Sfx sfx;
        [SerializeField] private Ambient ambient;
        [SerializeField] private Screams screams;
        [SerializeField] private EngineSound engine;
        [SerializeField] private RotorSound rotor;

        [Header("Scene")]
        [SerializeField] private Transform listenerAnchor;
        [SerializeField] private VehicleEnterExit vehicles;
        [SerializeField] private Interior interior;

        [Header("Tuning - Unity-side, no web equivalent")]
        [Tooltip("Sirens allowed to sound at once. The police can only field one car per star.")]
        [SerializeField] private int maxSirens = 3;

        [Tooltip("Snapshot crossfade, seconds. Long enough not to click, short enough to feel caused.")]
        [SerializeField] private float snapshotBlend = 0.35f;

        [Tooltip("Closing speed a crash must reach before it is worth hearing, m/s.")]
        [SerializeField] private float crashFloor = 3f;

        [Tooltip("Closing speed at which the crash cue plays at full gain, m/s.")]
        [SerializeField] private float crashFull = 12f;

        [Tooltip("Shortest gap between two crash cues, seconds.")]
        [SerializeField] private float crashCooldown = 0.3f;

        private TheBlockConfig.Root _config;
        private float _lastCrash = -99f;
        private bool _engineRunning;
        private bool _rotorRunning;

        public Sfx Effects => sfx;

        public Screams Voices => screams;

        public Ambient Beds => ambient;

        public EngineSound Engine => engine;

        public RotorSound RotorAudio => rotor;

        public AudioLibrary Library => library;

        public AudioClip SirenClip => sirenClip;

        /// <summary>The mixer group for a bus, or null if the mixer is missing.</summary>
        public AudioMixerGroup Group(Bus bus)
        {
            if (mixer == null) return null;
            foreach (var group in mixer.FindMatchingGroups(string.Empty))
                if (group.name == bus.ToString())
                    return group;
            return null;
        }

        /// <summary>Plays a cue, or does nothing at all if there is no audio host. Call it freely.</summary>
        public static void Cue(SfxCue cue, float gain = 1f)
        {
            var host = Instance;
            if (host != null && host.sfx != null) host.sfx.Play(cue, gain);
        }

        /// <summary>
        /// U35b - a car explodes somewhere in the world.
        ///
        /// <b>The distance falloff is done here, by hand.</b> <see cref="Sfx"/>'s pool is deliberately
        /// 2D - a UI tick and a crash both belong at the mix - so a blast four streets away would
        /// otherwise arrive at exactly the volume of one under your window. Rolling it off against the
        /// listener is the smallest thing that fixes that, and it keeps the cue in the same pool as
        /// every other one rather than giving explosions their own spatial source to arbitrate.
        /// </summary>
        public static void Explosion(Vector3 at)
        {
            var host = Instance;
            if (host == null || host.sfx == null) return;

            float distance = Vector3.Distance(at, host.ListenerPosition());
            float gain = Mathf.Clamp01(1f - Mathf.InverseLerp(explosionNear, explosionFar, distance));
            if (gain <= 0.02f) return;

            host.sfx.Play(SfxCue.Explosion, 0.25f + 0.75f * gain);
        }

        /// <summary>Metres within which an explosion is at full volume.</summary>
        private const float explosionNear = 12f;

        /// <summary>Metres past which it is not worth a voice at all.</summary>
        private const float explosionFar = 150f;

        /// <summary>The run-over reaction's voice. Silent, never null-checked at the call site.</summary>
        public static void Scream(ScreamVoice voice)
        {
            var host = Instance;
            if (host != null && host.screams != null) host.screams.Play(voice);
        }

        private void Awake()
        {
            _instance = this;
            _config = TheBlockConfig.Load()?.Config;

            if (vehicles == null) vehicles = FindAnyObjectByType<VehicleEnterExit>();
            if (interior == null) interior = FindAnyObjectByType<Interior>();

            CrashSensor.Crashed += OnCrashed;
        }

        private void OnDestroy()
        {
            CrashSensor.Crashed -= OnCrashed;
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            PollMuteKey();

            float dt = Time.deltaTime;
            var listener = ListenerPosition();

            var snapshot = CurrentSnapshot(out float duck);
            TransitionTo(snapshot);

            if (ambient != null) ambient.Tick(dt, listener, duck);
            Siren.Arbitrate(listener, Mathf.Max(0, maxSirens));
            TickVehicle();
        }

        /// <summary>
        /// <c>N</c> - mute / unmute. It lives here because this is the one always-present audio
        /// object, and it is the one key in the game that does NOT stand down on
        /// <see cref="Pause.Frozen"/>: a paused game is exactly when someone reaches for the mute.
        /// </summary>
        private static void PollMuteKey()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.nKey.wasPressedThisFrame) Mute.Toggle();
        }

        private Vector3 ListenerPosition()
        {
            if (listenerAnchor != null) return listenerAnchor.position;
            var listener = FindAnyObjectByType<AudioListener>();
            if (listener != null)
            {
                listenerAnchor = listener.transform;
                return listenerAnchor.position;
            }

            return transform.position;
        }

        /// <summary>
        /// Which snapshot the world is in, and the linear gain that implies for the Ambient bus.
        ///
        /// The interior wins over the dance and the dance over driving, which is the web's own
        /// <c>duckFor</c> order - and it matters: you cannot be in a car inside the pizzeria, but a
        /// mode and an area are separate axes and one of them has to be asked first.
        /// </summary>
        private string CurrentSnapshot(out float duck)
        {
            var ducks = _config?.AmbientAudio?.Duck;

            if (interior != null && interior.Inside)
            {
                duck = ducks?.Interior ?? 0f;
                return Snapshots.Interior;
            }

            if (vehicles != null && vehicles.Mode == GameMode.Rhythm)
            {
                duck = ducks?.Rhythm ?? 0f;
                return Snapshots.Rhythm;
            }

            if (vehicles != null && vehicles.Mode == GameMode.Driving)
            {
                duck = ducks?.Driving ?? 0.35f;
                return Snapshots.Driving;
            }

            duck = 1f;
            return Snapshots.Default;
        }

        private string _snapshot;

        private void TransitionTo(string name)
        {
            if (mixer == null || name == _snapshot) return;
            var snapshot = mixer.FindSnapshot(name);
            if (snapshot == null) return;
            _snapshot = name;
            snapshot.TransitionTo(snapshotBlend);
        }

        /// <summary>
        /// Starts, tracks and stops the engine or the rotor from the vehicle machine's own state.
        ///
        /// A helicopter is the one vehicle with no engine LOOP: it gets <see cref="RotorSound"/>
        /// instead, which is synthesized, so the two are mutually exclusive rather than layered.
        /// </summary>
        private void TickVehicle()
        {
            var driving = vehicles != null && vehicles.Mode == GameMode.Driving
                ? vehicles.ActiveVehicle
                : null;

            if (driving == null)
            {
                if (_engineRunning && engine != null)
                {
                    engine.StopEngine();
                    _engineRunning = false;
                }

                if (_rotorRunning && rotor != null)
                {
                    rotor.StopRotor();
                    _rotorRunning = false;
                }

                return;
            }

            if (driving is HelicopterController)
            {
                if (engine != null && _engineRunning)
                {
                    engine.StopEngine();
                    _engineRunning = false;
                }

                if (rotor == null) return;
                if (!_rotorRunning)
                {
                    rotor.StartRotor();
                    _rotorRunning = true;
                }
                else
                {
                    // The blades already ramp their own throttle (Rotor.Throttle, which exists for
                    // exactly this) - the sound follows the geometry rather than re-deriving it.
                    var blades = driving.GetTransform().GetComponentInChildren<TheBlock.Vehicles.Rotor>();
                    if (blades != null) rotor.SetThrottle(blades.Throttle);
                }

                return;
            }

            string type = TypeOf(driving);
            if (type == null || engine == null) return;

            if (!_engineRunning || engine.Running != type)
            {
                engine.StartEngine(type);
                _engineRunning = true;
            }

            engine.SetSpeed(driving.ForwardSpeed, MaxSpeedFor(type));
        }

        private static string TypeOf(IEnterable vehicle) => vehicle switch
        {
            CarController => EngineSound.Car,
            MotorcycleController => EngineSound.Motorcycle,
            JetskiController => EngineSound.Jetski,
            _ => null,
        };

        private float MaxSpeedFor(string type)
        {
            var vehicle = _config?.Vehicle;
            if (vehicle == null) return 20f;
            return type switch
            {
                // The bike has no cap of its own - it shares the car's, which is what
                // MotorcycleController reads. Only the jetski carries one, on its handling block.
                EngineSound.Jetski => vehicle.Jetski?.Handling?.MaxSpeed ?? vehicle.MaxSpeed,
                _ => vehicle.MaxSpeed,
            };
        }

        /// <summary>
        /// The crash thump, from <see cref="CrashSensor"/>'s own event - the one U27 was promised in
        /// that class's comment.
        ///
        /// <b>Only the vehicle the player is in.</b> The sensor fires for every car in the city:
        /// thirty traffic cars and three cruisers, all colliding out of frame. The web can only ever
        /// crash the player because its detector is on the player's own vehicle, so this is parity
        /// and not a preference - a city that thumps whenever a cop clips a kerb is not the same
        /// game.
        ///
        /// The floor is 3 m/s rather than the sensor's own 0.5: that threshold exists to decide
        /// whether the police care, and a kerb worth no heat is also worth no sound.
        /// </summary>
        private void OnCrashed(CrashSensor.Impact impact)
        {
            if (impact.ClosingSpeed < crashFloor) return;
            if (Time.time - _lastCrash < crashCooldown) return;

            var driving = vehicles != null && vehicles.Mode == GameMode.Driving
                ? vehicles.ActiveVehicle
                : null;
            if (driving == null || impact.Sensor == null) return;
            if (impact.Sensor.transform.root != driving.GetTransform().root) return;

            _lastCrash = Time.time;
            float gain = Mathf.Clamp01(
                Mathf.InverseLerp(crashFloor, Mathf.Max(crashFloor + 0.1f, crashFull), impact.ClosingSpeed));
            Cue(SfxCue.Crash, 0.35f + 0.65f * gain);
        }

        /// <summary>Snapshot names, shared with the builder so a rename cannot half-land.</summary>
        public static class Snapshots
        {
            public const string Default = "Default";
            public const string Driving = "Driving";
            public const string Interior = "Interior";
            public const string Rhythm = "Rhythm";
        }

        /// <summary>Editor-side wiring, used by <c>The Block → Build Audio</c>.</summary>
        public void Configure(
            AudioMixer gameMixer, AudioLibrary clips, AudioClip siren,
            Sfx effects, Ambient beds, Screams voices, EngineSound engineSound, RotorSound rotorSound)
        {
            mixer = gameMixer;
            library = clips;
            sirenClip = siren;
            sfx = effects;
            ambient = beds;
            screams = voices;
            engine = engineSound;
            rotor = rotorSound;
        }
    }
}
