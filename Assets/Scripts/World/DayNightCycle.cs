using TheBlock.Core;
using TheBlock.Missions;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TheBlock.World
{
    /// <summary>
    /// The clock, and the one thing in this project allowed to write the sky.
    ///
    /// <b>This is an addition, not a port.</b> The web build has no time of day: its sky is a
    /// constant <c>config.background</c> and its sun never moves. Everything here is new, which is
    /// why it ships behind a Setting that defaults to OFF — Fixed mode is not "close to" the look
    /// every unit from U11 to U27 was play-tested against, it is that look, reproduced field for
    /// field. See <see cref="SkyPalette.AnchorHour"/> for how.
    ///
    /// <b>Cost, because that was the requirement.</b> Rotating a directional light is free — URP
    /// re-renders all four cascades every frame whatever the angle — and there is no baked GI in this
    /// project to invalidate (no lightmaps, every light Realtime, every renderer non-static). The
    /// two things that WOULD cost are both refused: <c>DynamicGI.UpdateEnvironment</c> (1–3 ms a
    /// call, re-convolving the skybox into spherical harmonics) is replaced by
    /// <c>AmbientMode.Trilight</c> and three lerped colours; and Bloom, which is six to eight blur
    /// passes, is not in the profile. What is left is one fullscreen grading pass, and when the
    /// setting is Fixed even that is not scheduled — <see cref="SetEnabled"/> turns
    /// <c>renderPostProcessing</c> off, so the default configuration costs literally nothing.
    /// Night costs LESS than day: below the horizon the light's shadows are switched off and the
    /// 4×2048 cascade render goes with them.
    ///
    /// <b>On the snapshot in this class, given that Interior's snapshot is being deleted in the same
    /// change.</b> They are not the same thing. Interior's fault was saving state a SECOND live
    /// writer also owns, so its restore handed back a value that had moved on underneath it — the
    /// same shape as U26's Radar/<c>display</c> bug. This snapshot is taken in <c>Awake</c>, before
    /// anything has written those fields, and is replayed only when the setting is switched off.
    /// While the cycle is running it is the sole owner of every field it touches; nothing else has a
    /// copy to hand back.
    /// </summary>
    [DisallowMultipleComponent]
    public class DayNightCycle : MonoBehaviour
    {
        [Header("Clock")]
        [Tooltip("Real seconds for one full 24-hour day. 2880 = 48 real minutes, GTA V's own pace.")]
        [SerializeField] private float dayLengthSeconds = 2880f;

        [Tooltip("Hour the world starts at, and the hour Quit to Title comes back to.")]
        [SerializeField, Range(0f, 24f)] private float startHour = SkyPalette.AnchorHour;

        [Tooltip("Compass direction of the sun's arc. The scene's stock light sits at −30°.")]
        [SerializeField, Range(-180f, 180f)] private float azimuth = -30f;

        [Header("Scene — found automatically when left empty")]
        [SerializeField] private Light sun;
        [SerializeField] private Volume grade;
        [SerializeField] private Camera view;
        [SerializeField] private Campaign campaign;

        [Header("Testing — a scene left in these is a trap. The on-screen banner is the guard.")]
        [Tooltip("Turn the cycle on regardless of the Settings preference, and run the clock behind " +
                 "the title screen. Press Play and the sky moves; nothing to click.")]
        [SerializeField] private bool testMode;

        [Tooltip("Seconds per full day while Test Mode is on. 120 = a whole day in two minutes.")]
        [SerializeField] private float testDayLengthSeconds = 120f;

        [Tooltip("Hold the clock at Scrub Hour instead of running it. Test Mode only.")]
        [SerializeField] private bool scrub;

        [SerializeField, Range(0f, 24f)] private float scrubHour = 12f;

        // --- the world's clock ----------------------------------------------------------------

        /// <summary>Hour of day, 0.0–24.0. Anything that wants to know what time it is reads this.</summary>
        public static float TimeOfDay { get; private set; }

        /// <summary>Is the cycle actually advancing? False on a Fixed profile, which is the default.</summary>
        public static bool Running { get; private set; }

        /// <summary>True while the sun is below the horizon. The night emissive pass reads it.</summary>
        public static bool IsNight { get; private set; }

        /// <summary>
        /// Raised by <see cref="Interior"/> while the player is in the room. The cycle keeps running
        /// the clock and the sun, but stops writing fog and ambient — the interior owns those for as
        /// long as you are inside, and it hands them back by calling <see cref="ReassertNow"/> rather
        /// than by restoring a copy it took on the way in.
        /// </summary>
        public static bool SuspendedForInterior { get; set; }

        /// <summary>The live cycle, so Settings and Interior can reach it without a search each time.</summary>
        public static DayNightCycle Instance { get; private set; }

        // --- as-built state, replayed when the setting goes back to Fixed -----------------------

        private AmbientMode _builtAmbientMode;
        private Color _builtAmbientSky, _builtAmbientEquator, _builtAmbientGround, _builtAmbientLight;
        private float _builtAmbientIntensity;
        private Material _builtSkybox;
        private bool _builtFog;
        private Color _builtFogColor;
        private float _builtFogStart, _builtFogEnd;
        private Quaternion _builtSunRotation;
        private Color _builtSunColor;
        private float _builtSunIntensity, _builtShadowStrength;
        private LightShadows _builtShadows;

        private Material _skyInstance;
        private ColorAdjustments _colorAdjustments;
        private WhiteBalance _whiteBalance;
        private UniversalAdditionalCameraData _viewData;

        private void Awake()
        {
            Instance = this;

            if (sun == null) sun = GetComponent<Light>();
            if (grade == null) grade = FindAnyObjectByType<Volume>();
            if (view == null) view = Camera.main;
            if (campaign == null) campaign = FindAnyObjectByType<Campaign>();

            Snapshot();
            ResolveGrading();

            TimeOfDay = Mathf.Repeat(startHour, 24f);
            IsNight = false;
            Running = false;
            SuspendedForInterior = false;
        }

        private void Start()
        {
            // The stored preference outranks the scene, the same way Settings → Radar does: a
            // serialized default is only what a profile that has never opened the menu inherits.
            // Test Mode outranks BOTH, and says so on screen for as long as it is on.
            SetEnabled(testMode || Game.Progress.DayNightOn);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_skyInstance != null) Destroy(_skyInstance);
        }

        private void Update()
        {
            if (!Running) return;

            if (testMode) ReadTestKeys();

            if (testMode && scrub)
            {
                TimeOfDay = scrubHour;
            }
            else if (testMode || !Holding())
            {
                var length = testMode ? testDayLengthSeconds : dayLengthSeconds;
                var hoursPerSecond = length <= 0f ? 0f : 24f / length;

                // Unscaled in Test Mode, because the whole point is that the sky moves behind the
                // title screen — and the title screen sets Time.timeScale to 0, which would leave
                // deltaTime at zero and the sun exactly where it started.
                var dt = testMode ? Time.unscaledDeltaTime : Time.deltaTime;
                TimeOfDay = Mathf.Repeat(TimeOfDay + dt * hoursPerSecond, 24f);
            }

            Apply(TimeOfDay);
        }

        /// <summary>
        /// The test keys. Brackets step an hour, backslash holds the clock where it is.
        ///
        /// <c>[</c> <c>]</c> <c>\</c> are chosen because nothing else in this project reads them —
        /// seventeen scripts poll <see cref="Keyboard"/> every frame and a collision here would look
        /// like the cycle breaking the game rather than like a key clash. Live only in Test Mode, so
        /// a shipped build has no bindings to collide with at all.
        /// </summary>
        private void ReadTestKeys()
        {
            var keys = Keyboard.current;
            if (keys == null) return;

            if (keys.backslashKey.wasPressedThisFrame)
            {
                scrub = !scrub;
                scrubHour = TimeOfDay;
            }

            var step = keys.leftBracketKey.wasPressedThisFrame ? -1f
                : keys.rightBracketKey.wasPressedThisFrame ? 1f
                : 0f;
            if (step == 0f) return;

            // Stepping implies you want to look at that hour, so it also takes the clock.
            scrub = true;
            scrubHour = Mathf.Repeat(scrubHour + step, 24f);
        }

        /// <summary>
        /// Is the clock held? Three reasons, and none of them stops the cycle from RENDERING — the
        /// sky still has to be written every frame, it just must not advance.
        ///
        /// The mission hold is the user's call: a mission runs four to fifteen minutes against a
        /// 24-minute day, so a sea chase begun at dusk would finish in the dark, and U23 and U24 were
        /// tuned and confirmed in daylight. It is a poll of <see cref="Campaign.Current"/> rather
        /// than a hook, deliberately — nothing under <c>Assets/Scripts/Mission/</c> is touched by this
        /// unit, so a mission cannot forget to release a latch it never took.
        /// </summary>
        private bool Holding()
        {
            if (Pause.Frozen) return true;
            if (SuspendedForInterior) return true;
            return campaign != null
                   && campaign.Current != null
                   && campaign.Current.Status == MissionStatus.Active;
        }

        /// <summary>
        /// Turns the cycle on or off. Called by Settings → Display, and once on boot.
        ///
        /// Off is not "neutral values through the same path" — it is the path switched out. Ambient
        /// goes back to Skybox mode, the built-in sky material goes back, the light returns to the
        /// angle the scene stores, and <c>renderPostProcessing</c> goes false so URP schedules no
        /// post pass at all. That is what makes the default cost zero rather than nearly zero.
        /// </summary>
        public void SetEnabled(bool on)
        {
            Running = on;

            if (on)
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientIntensity = 1f;
                RenderSettings.sun = sun;
                if (_skyInstance != null) RenderSettings.skybox = _skyInstance;
                if (_viewData != null) _viewData.renderPostProcessing = true;
                if (grade != null) grade.enabled = true;
                Apply(TimeOfDay);
            }
            else
            {
                RestoreBuilt();
                if (_viewData != null) _viewData.renderPostProcessing = false;
                if (grade != null) grade.enabled = false;
                IsNight = false;

                // Fixed mode is defined by the SCENE, not by the palette — RestoreBuilt replays the
                // light and sky exactly as WorldBuilder left them, which is the strongest possible
                // guarantee that off is a no-op. TimeOfDay still has to answer, though: anything
                // asking what time it is gets the hour that scene corresponds to.
                TimeOfDay = SkyPalette.AnchorHour;
            }
        }

        /// <summary>
        /// Re-writes fog and ambient at the current hour. <see cref="Interior"/> calls this on the way
        /// out instead of restoring a snapshot it took on the way in — the difference between one
        /// owner and two, and the whole reason that snapshot is being deleted.
        /// </summary>
        public void ReassertNow()
        {
            if (Running) Apply(TimeOfDay);
            else RestoreBuilt();
        }

        /// <summary>Puts the clock back to <c>startHour</c>. Called from <see cref="SessionReset"/>.</summary>
        public static void ResetClock()
        {
            TimeOfDay = 0f;
            Running = false;
            IsNight = false;
            SuspendedForInterior = false;
            Instance = null;
        }

        // --- the frame's work -------------------------------------------------------------------

        private void Apply(float hour)
        {
            var stop = SkyPalette.Sample(hour);

            ApplySun(hour, stop);
            ApplySky(stop);
            if (!SuspendedForInterior) ApplyAtmosphere(stop);
            ApplyGrading(stop);
        }

        private void ApplySun(float hour, in SkyStop stop)
        {
            if (sun == null) return;

            var solar = Quaternion.Euler(SkyPalette.SunPitch(hour), azimuth, 0f);
            var toGround = solar * Vector3.forward;

            // Elevation from the direction vector rather than from the pitch, so it stays right if
            // the azimuth is ever animated or the rig is ever parented. The light points DOWN from
            // the sun, so a negative y is a sun above the horizon.
            var elevation = -Mathf.Asin(Mathf.Clamp(toGround.y, -1f, 1f)) * Mathf.Rad2Deg;
            IsNight = elevation <= 0f;

            // Below the horizon the same light becomes the moon, mirrored to the opposite side of the
            // sky. One light, because URP has exactly one main directional and a second would be an
            // additional light with no shadows. See SkyStop.Sun.
            sun.transform.rotation = IsNight ? Quaternion.LookRotation(-toGround) : solar;

            sun.color = stop.Sun;

            // Fade through the crossing so the mirror is invisible — see SkyPalette.HorizonFadeBand.
            var horizon = Mathf.InverseLerp(0f, SkyPalette.HorizonFadeBand, Mathf.Abs(elevation));
            sun.intensity = stop.SunIntensity * horizon;

            if (IsNight)
            {
                // The refund, and the reason night runs cheaper than day: no main-light shadows means
                // URP skips the four 2048² cascades, which are the most expensive thing this camera
                // draws. The light itself stays ENABLED — the procedural skybox reads its direction,
                // and a disabled key light would leave the night sky lit from nowhere.
                sun.shadows = LightShadows.None;
                return;
            }

            sun.shadows = LightShadows.Soft;

            // Fade shadows in off the horizon. At 3° the cascades stretch far enough that this
            // project's 0.1 depth / 0.5 normal bias — tuned against a 50° sun — shows acne and
            // peter-panning. The ramp hides it under the dimming that is happening anyway.
            sun.shadowStrength = Mathf.InverseLerp(0f, SkyPalette.ShadowFadeElevation, elevation);
        }

        private void ApplySky(in SkyStop stop)
        {
            if (_skyInstance == null) return;
            _skyInstance.SetFloat(SkyExposureId, stop.SkyExposure);
            _skyInstance.SetFloat(AtmosphereThicknessId, stop.AtmosphereThickness);
        }

        private void ApplyAtmosphere(in SkyStop stop)
        {
            RenderSettings.ambientSkyColor = stop.AmbientSky;
            RenderSettings.ambientEquatorColor = stop.AmbientEquator;
            RenderSettings.ambientGroundColor = stop.AmbientGround;

            // Fog RANGE never moves — it is Atmosphere's ratio against the 1500 m draw distance and
            // has nothing to do with the hour. Only the colour tracks the sky, and it must, or the
            // far plane reappears as a hard arc. See SkyStop.
            RenderSettings.fogColor = stop.Fog;
        }

        private void ApplyGrading(in SkyStop stop)
        {
            if (_colorAdjustments != null)
            {
                _colorAdjustments.postExposure.value = stop.PostExposure;
                _colorAdjustments.saturation.value = stop.Saturation;
            }

            if (_whiteBalance != null)
            {
                _whiteBalance.temperature.value = stop.Temperature;
                _whiteBalance.tint.value = stop.Tint;
            }
        }

        // --- setup and teardown -----------------------------------------------------------------

        private static readonly int SkyExposureId = Shader.PropertyToID("_Exposure");
        private static readonly int AtmosphereThicknessId = Shader.PropertyToID("_AtmosphereThickness");

        private void Snapshot()
        {
            _builtAmbientMode = RenderSettings.ambientMode;
            _builtAmbientSky = RenderSettings.ambientSkyColor;
            _builtAmbientEquator = RenderSettings.ambientEquatorColor;
            _builtAmbientGround = RenderSettings.ambientGroundColor;
            _builtAmbientLight = RenderSettings.ambientLight;
            _builtAmbientIntensity = RenderSettings.ambientIntensity;
            _builtSkybox = RenderSettings.skybox;
            _builtFog = RenderSettings.fog;
            _builtFogColor = RenderSettings.fogColor;
            _builtFogStart = RenderSettings.fogStartDistance;
            _builtFogEnd = RenderSettings.fogEndDistance;

            if (sun != null)
            {
                _builtSunRotation = sun.transform.rotation;
                _builtSunColor = sun.color;
                _builtSunIntensity = sun.intensity;
                _builtShadows = sun.shadows;
                _builtShadowStrength = sun.shadowStrength;
            }

            // RenderSettings.skybox points at the BUILT-IN Default-Skybox asset. Writing properties
            // on it would mutate a shared built-in for every scene in the project, so the cycle
            // drives a runtime copy and hands the original back when it is switched off.
            if (_builtSkybox != null) _skyInstance = new Material(_builtSkybox) { name = "Sky (day/night)" };
        }

        private void RestoreBuilt()
        {
            RenderSettings.ambientMode = _builtAmbientMode;
            RenderSettings.ambientSkyColor = _builtAmbientSky;
            RenderSettings.ambientEquatorColor = _builtAmbientEquator;
            RenderSettings.ambientGroundColor = _builtAmbientGround;
            RenderSettings.ambientLight = _builtAmbientLight;
            RenderSettings.ambientIntensity = _builtAmbientIntensity;
            RenderSettings.skybox = _builtSkybox;
            RenderSettings.fog = _builtFog;
            RenderSettings.fogColor = _builtFogColor;
            RenderSettings.fogStartDistance = _builtFogStart;
            RenderSettings.fogEndDistance = _builtFogEnd;

            if (sun == null) return;
            sun.enabled = true;
            sun.transform.rotation = _builtSunRotation;
            sun.color = _builtSunColor;
            sun.intensity = _builtSunIntensity;
            sun.shadows = _builtShadows;
            sun.shadowStrength = _builtShadowStrength;
        }

        private void ResolveGrading()
        {
            // The Main Camera has no UniversalAdditionalCameraData in the scene at all — URP adds one
            // on demand, and this is the demand. Done in Awake so it lands at runtime and never
            // dirties the saved scene.
            if (view != null) _viewData = view.GetUniversalAdditionalCameraData();

            if (grade == null) return;

            // `profile`, not `sharedProfile`: the getter hands back a runtime clone, so writing the
            // overrides every frame cannot dirty the asset on disk.
            var profile = grade.profile;
            if (profile == null) return;

            profile.TryGet(out _colorAdjustments);
            profile.TryGet(out _whiteBalance);
        }

#if UNITY_EDITOR
        /// <summary>
        /// The clock, on screen, whenever Test Mode is on.
        ///
        /// <b>This is the guard, not a convenience.</b> The ledger's standing warning is that a scene
        /// left in a test setting is a trap — and Test Mode overrides the player's own preference, so
        /// it is exactly that kind of setting. It cannot be left on quietly: it says so, in the
        /// corner, for as long as it is on. Editor only; a player build has no banner because it has
        /// no Test Mode to announce.
        /// </summary>
        private void OnGUI()
        {
            if (!testMode) return;

            var hour = Mathf.FloorToInt(TimeOfDay);
            var minute = Mathf.FloorToInt((TimeOfDay - hour) * 60f);
            var phase = IsNight ? "night" : "day";
            var clock = scrub ? "HELD" : "running";

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            };

            GUI.Box(new Rect(10f, 10f, 430f, 62f), GUIContent.none);
            GUI.Label(new Rect(20f, 14f, 420f, 26f),
                $"DAY/NIGHT TEST MODE — {hour:00}:{minute:00} · {phase} · {clock}", style);
            GUI.Label(new Rect(20f, 40f, 420f, 24f),
                "[ and ]  step an hour     \\  hold / release the clock");
        }
#endif
    }
}
