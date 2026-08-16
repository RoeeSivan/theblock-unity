using UnityEngine;

namespace TheBlock.World
{
    /// <summary>
    /// One instant of sky: everything <see cref="DayNightCycle"/> writes for a given hour, sampled
    /// as a unit.
    ///
    /// <b><see cref="Fog"/> and the sky are one field's worth of decision, not two.</b>
    /// <see cref="Atmosphere"/> opens with the post-mortem: the web build's fog colour and its
    /// background are byte-identical, and the far plane at 1500 m is invisible ONLY because the haze
    /// reaches full opacity in front of it. Porting the plane without the fog sliced towers
    /// mid-building against clear sky and survived four units unnoticed. A clock that darkens the fog
    /// on a different schedule from the sky puts that cut straight back at 21:00 - so both come out
    /// of one struct, sampled once, applied in the same frame.
    /// </summary>
    public readonly struct SkyStop
    {
        /// <summary>Hour of day this stop is authored at, 0-24.</summary>
        public readonly float Hour;

        /// <summary>
        /// The key light's colour - the SUN above the horizon and the MOON below it, because there
        /// is only one light. URP's main light is the brightest directional in the scene; a second
        /// one for the moon would be demoted to an additional light, which gets no shadows and is
        /// priced per pixel. So the one light swings a full 360°, is mirrored to the opposite side of
        /// the sky once it passes under, and simply changes colour.
        ///
        /// sRGB - <c>Light.color</c> is gamma space and Unity converts on upload, so these are the
        /// values you would type into the Inspector.
        /// </summary>
        public readonly Color Sun;

        /// <summary>Key light intensity. Roughly 0.1 at night, which is moonlight, not sunlight.
        /// <see cref="DayNightCycle"/> ramps it to zero either side of the horizon crossing so that
        /// the mirror is invisible, and drops shadows entirely below it - that is the refund.</summary>
        public readonly float SunIntensity;

        /// <summary>The three <c>AmbientMode.Trilight</c> colours. sRGB, same as above.</summary>
        public readonly Color AmbientSky, AmbientEquator, AmbientGround;

        /// <summary>Linear fog colour, and by construction the colour the sky reads as near the
        /// horizon. See the type summary.</summary>
        public readonly Color Fog;

        /// <summary><c>Skybox/Procedural</c> <c>_Exposure</c>. A float, deliberately: the procedural
        /// skybox's own Rayleigh/Mie model already reddens the horizon as the sun approaches it, so
        /// sunrise and sunset colour comes free from the rotation and only the overall level has to
        /// be driven. Colour properties on that shader are not <c>[Gamma]</c>-tagged and would need a
        /// visual check to write correctly (memory: <c>gltfast-basecolorfactor-gamma</c>); floats
        /// have no such question.</summary>
        public readonly float SkyExposure;

        /// <summary><c>Skybox/Procedural</c> <c>_AtmosphereThickness</c>. Thickens toward dusk, which
        /// is what turns a blue sky orange without touching a single colour value.</summary>
        public readonly float AtmosphereThickness;

        /// <summary>URP <c>ColorAdjustments.postExposure</c>, in EV.</summary>
        public readonly float PostExposure;

        /// <summary>URP <c>WhiteBalance.temperature</c> / <c>.tint</c>, −100..100.</summary>
        public readonly float Temperature, Tint;

        /// <summary>URP <c>ColorAdjustments.saturation</c>, −100..100. Night pulls it down; the eye
        /// desaturates in low light and a fully saturated midnight reads as a blue day.</summary>
        public readonly float Saturation;

        public SkyStop(
            float hour,
            Color sun, float sunIntensity,
            Color ambientSky, Color ambientEquator, Color ambientGround,
            Color fog, float skyExposure, float atmosphereThickness,
            float postExposure, float temperature, float tint, float saturation)
        {
            Hour = hour;
            Sun = sun;
            SunIntensity = sunIntensity;
            AmbientSky = ambientSky;
            AmbientEquator = ambientEquator;
            AmbientGround = ambientGround;
            Fog = fog;
            SkyExposure = skyExposure;
            AtmosphereThickness = atmosphereThickness;
            PostExposure = postExposure;
            Temperature = temperature;
            Tint = tint;
            Saturation = saturation;
        }

        /// <summary>Component-wise blend. <c>t</c> is not clamped by the caller - <see cref="SkyPalette"/>
        /// only ever passes 0..1.</summary>
        public static SkyStop Lerp(in SkyStop a, in SkyStop b, float t) => new(
            Mathf.Lerp(a.Hour, b.Hour, t),
            Color.Lerp(a.Sun, b.Sun, t),
            Mathf.Lerp(a.SunIntensity, b.SunIntensity, t),
            Color.Lerp(a.AmbientSky, b.AmbientSky, t),
            Color.Lerp(a.AmbientEquator, b.AmbientEquator, t),
            Color.Lerp(a.AmbientGround, b.AmbientGround, t),
            Color.Lerp(a.Fog, b.Fog, t),
            Mathf.Lerp(a.SkyExposure, b.SkyExposure, t),
            Mathf.Lerp(a.AtmosphereThickness, b.AtmosphereThickness, t),
            Mathf.Lerp(a.PostExposure, b.PostExposure, t),
            Mathf.Lerp(a.Temperature, b.Temperature, t),
            Mathf.Lerp(a.Tint, b.Tint, t),
            Mathf.Lerp(a.Saturation, b.Saturation, t));
    }

    /// <summary>
    /// The look of every hour, as a keyed table.
    ///
    /// <b>Static and code-only, and that is the important decision in this file.</b> The obvious
    /// shape is a <c>[SerializeField] SkyStop[]</c> on <see cref="DayNightCycle"/> so the colours are
    /// Inspector-editable - and it is a trap this project has already been bitten by: a serialized
    /// field's value is written into <c>World.unity</c> the first time the scene is saved, and from
    /// then on the C# initialiser is dead. Re-tuning it changes nothing and reports nothing
    /// (memory: <c>scene-serialized-value-beats-cs-default</c>). Only the four numbers a player or a
    /// tester turns - day length, start hour, fixed hour, enabled - are serialized. The palette lives
    /// here, where editing it always takes effect.
    ///
    /// <b>Hour 9.33 is the anchor and is not free to move.</b> The scene's stock directional light
    /// sits at <c>{50, −30, 0}</c>, and this cycle's rotation is <c>x = (hour − 6) × 15</c> - so 50°
    /// is 09:20. The stop at that hour reproduces today's lighting field for field: the light's own
    /// <c>#FFF4D6</c> at intensity 1, the scene's stored ambient triple, and <c>#9FB8D4</c> fog,
    /// which is <c>config.background</c>. That is what lets the setting default to Fixed and be
    /// provably a no-op rather than merely a nearly-identical one.
    /// </summary>
    public static class SkyPalette
    {
        /// <summary>The hour the scene's own lighting corresponds to. See the type summary.</summary>
        public const float AnchorHour = 9.3333f;

        /// <summary>Degrees of sun rotation per hour. 360 / 24 - sunrise at 06:00, zenith at 12:00,
        /// sunset at 18:00, and the light under the map at midnight.</summary>
        public const float DegreesPerHour = 15f;

        /// <summary>Sun elevation, in degrees, at which shadows have faded fully in. Below this the
        /// cascades stretch far enough that the project's 0.1/0.5 depth and normal bias - tuned
        /// against a 50° sun - produce acne and peter-panning.</summary>
        public const float ShadowFadeElevation = 12f;

        /// <summary>
        /// Degrees either side of the horizon over which the key light fades to nothing.
        ///
        /// This is what hides the moon mirror. At the crossing the sun points horizontally one way
        /// and the moon horizontally the other, so flipping between them is a 180° change in light
        /// direction - plainly visible on every vertical wall in the city unless the light is
        /// contributing nothing at the moment it happens. Two degrees is about five seconds of a
        /// 24-minute day, and the sky and ambient carry the look across it.
        /// </summary>
        public const float HorizonFadeBand = 2f;

        private static Color Rgb(int hex) => new(
            ((hex >> 16) & 0xFF) / 255f,
            ((hex >> 8) & 0xFF) / 255f,
            (hex & 0xFF) / 255f,
            1f);

        // The scene's own ambient triple, read out of World.unity. Hour 9.33 must return these
        // three unchanged or Fixed mode is not a no-op.
        private static readonly Color NoonSky = new(0.212f, 0.227f, 0.259f, 1f);
        private static readonly Color NoonEquator = new(0.114f, 0.125f, 0.133f, 1f);
        private static readonly Color NoonGround = new(0.047f, 0.043f, 0.035f, 1f);

        /// <summary>
        /// Sorted by hour, wrapping at 24. The first and last stops are both midnight and MUST hold
        /// identical values - <see cref="Sample"/> blends across the wrap between them, and a
        /// mismatch would show as a hard snap at 00:00.
        /// </summary>
        private static readonly SkyStop[] Stops =
        {
            //          hour   key colour      int    ambient sky   ambient equator  ambient ground   fog             skyExp  thick   postEV  temp   tint   sat
            new(  0.00f, Rgb(0x8496C8), 0.10f, Rgb(0x1A2233), Rgb(0x141A28), Rgb(0x0A0D14), Rgb(0x0E1420), 0.35f,  1.15f,  0.10f, -22f,   4f,  -26f),
            new(  4.30f, Rgb(0x8496C8), 0.10f, Rgb(0x1E2740), Rgb(0x171E30), Rgb(0x0C1018), Rgb(0x14203A), 0.45f,  1.35f,  0.10f, -18f,   4f,  -22f),
            new(  5.45f, Rgb(0x8496C8), 0.08f, Rgb(0x36405E), Rgb(0x3A3446), Rgb(0x1A1A20), Rgb(0x3E4468), 0.70f,  1.70f,  0.05f,  -6f,   6f,  -12f),
            new(  6.30f, Rgb(0xFF9E5E), 0.45f, Rgb(0x6E7EA0), Rgb(0x8A7A72), Rgb(0x342E2A), Rgb(0x9A8A96), 1.05f,  2.05f, -0.05f,  16f,   6f,    6f),
            new(  7.45f, Rgb(0xFFD2A6), 0.85f, Rgb(0x8C9AB8), Rgb(0x9E9A94), Rgb(0x3E3A34), Rgb(0xB3B6C6), 1.25f,  1.35f, -0.05f,  10f,   2f,    4f),
            new(  9.3333f, Rgb(0xFFF4D6), 1.00f, NoonSky,     NoonEquator,   NoonGround,    Rgb(0x9FB8D4), 1.30f,  1.00f,  0.00f,   0f,   0f,    0f),
            new( 12.00f, Rgb(0xFFF8E4), 1.05f, Rgb(0x384358), Rgb(0x1E2128), Rgb(0x0C0B09), Rgb(0xA8C0DC), 1.35f,  0.90f, -0.05f,  -4f,   0f,   -2f),
            new( 15.30f, Rgb(0xFFF0CC), 0.95f, NoonSky,       NoonEquator,   NoonGround,    Rgb(0x9FB8D4), 1.30f,  1.00f,  0.00f,   2f,   0f,    0f),
            new( 17.30f, Rgb(0xFF9E5E), 0.45f, Rgb(0x8A8CA4), Rgb(0x9E8A78), Rgb(0x3C3228), Rgb(0xC3B4B0), 1.20f,  1.55f, -0.05f,  18f,   4f,    8f),
            new( 18.45f, Rgb(0x8496C8), 0.06f, Rgb(0x5E6288), Rgb(0x7A5E56), Rgb(0x2A2226), Rgb(0x8E6E78), 0.90f,  2.20f,  0.00f,  26f,   8f,   10f),
            new( 19.45f, Rgb(0x8496C8), 0.10f, Rgb(0x33395A), Rgb(0x2E2C42), Rgb(0x14161E), Rgb(0x3A3A5C), 0.60f,  1.80f,  0.10f,  -4f,   8f,  -10f),
            new( 21.00f, Rgb(0x8496C8), 0.10f, Rgb(0x1E2740), Rgb(0x171E30), Rgb(0x0C1018), Rgb(0x16203A), 0.40f,  1.25f,  0.10f, -20f,   4f,  -24f),
            new( 24.00f, Rgb(0x8496C8), 0.10f, Rgb(0x1A2233), Rgb(0x141A28), Rgb(0x0A0D14), Rgb(0x0E1420), 0.35f,  1.15f,  0.10f, -22f,   4f,  -26f),
        };

        /// <summary>
        /// The look at <paramref name="hour"/>, blended between the two stops bracketing it. Hours
        /// outside 0-24 wrap, so a caller never has to normalise before asking.
        /// </summary>
        public static SkyStop Sample(float hour)
        {
            hour = Mathf.Repeat(hour, 24f);

            // Stops are few and sorted; a linear scan is cheaper than the branch to avoid it, and
            // this runs once a frame.
            for (var i = 0; i < Stops.Length - 1; i++)
            {
                var a = Stops[i];
                var b = Stops[i + 1];
                if (hour < a.Hour || hour > b.Hour) continue;

                var span = b.Hour - a.Hour;
                var t = span <= 0f ? 0f : (hour - a.Hour) / span;
                return SkyStop.Lerp(a, b, SmoothStep(t));
            }

            return Stops[0];
        }

        /// <summary>Ease in and out of every stop. Linear blending between hand-placed keys puts a
        /// visible crease at each one - the rate of change jumps, and on a sky that is exactly what
        /// the eye catches.</summary>
        private static float SmoothStep(float t) => t * t * (3f - 2f * t);

        /// <summary>
        /// The sun's Euler X for an hour: 0° at 06:00 (on the horizon), 90° at 12:00 (overhead),
        /// 180° at 18:00, 270° at midnight. Azimuth is the caller's, held at the scene's −30°.
        /// </summary>
        public static float SunPitch(float hour) => (Mathf.Repeat(hour, 24f) - 6f) * DegreesPerHour;
    }
}
