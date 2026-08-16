using TheBlock.Core;
using UnityEngine;

namespace TheBlock.World
{
    /// <summary>
    /// How far the world is drawn, and the haze that hides the end of it.
    ///
    /// THE FAULT THIS FIXES. `config.camera.far` is 320 m and `config.fog` runs 70 m → 280 m in a
    /// colour (`#9FB8D4`) that is byte-identical to `config.background`. That is not three
    /// unrelated numbers - it is one mechanism. Geometry is fully faded into a sky painted the
    /// same shade well before the far plane reaches it, so the plane never shows. The port carried
    /// the far plane and NOT the fog, which left the clip naked: distant towers were sliced
    /// mid-building against a clear sky, in a hard arc that moved with the player. It read as
    /// missing geometry rather than as a clip, which is why it survived U11 through U14.
    ///
    /// WHAT UNITY CHANGES. A 320 m draw distance is a browser concession, not a design intent -
    /// `config.streaming` is the proof, unloading whole districts past 380 m because three.js
    /// could not afford to keep them. Port rule 5 says scar tissue does not carry, so the distance
    /// is a port-side decision (<see cref="DrawDistance"/>) while the fog's SHAPE stays the web
    /// build's: <see cref="FogRange"/> rescales the config's own near/far ratios onto whatever
    /// draw distance we pick, so the haze thickens at the same fraction of the view it always did.
    ///
    /// One place, deliberately. The camera sets its far plane from here at runtime and
    /// `WorldBuilder` writes `RenderSettings` from here at build time; a fog end that disagreed
    /// with the far plane would put the hard cut straight back.
    /// </summary>
    public static class Atmosphere
    {
        /// <summary>
        /// How far the camera draws, in metres. The one knob - everything else is derived.
        ///
        /// Chosen so the whole skyline is visible from anywhere in it: the world's districts span
        /// roughly 1000 m corner to corner and city 7's building mesh alone is 431 m wide, so 320 m
        /// could not show one district's far side, let alone the city. Raise it and the fog follows;
        /// U30's perf pass is where it gets measured rather than reasoned about.
        /// </summary>
        public const float DrawDistance = 1500f;

        /// <summary>Shadow distance. Well short of <see cref="DrawDistance"/> - cascades over a
        /// kilometre would be a shadow map with nothing usable in it - but far enough that a
        /// building casts one at street scale instead of stopping 40 m out.</summary>
        public const float ShadowDistance = 150f;

        /// <summary>The web build's own far plane, and the denominator its fog ratios are against.</summary>
        private const float WebDrawDistance = 320f;

        /// <summary>
        /// The config's fog band, rescaled from the web build's 320 m view onto ours.
        ///
        /// The RATIOS are what carry, not the metres. 70/320 and 280/320 say "haze starts at a
        /// fifth of the view and is total by seven eighths" - that is the look, and it holds at any
        /// draw distance. Copying 70 and 280 verbatim onto a 1500 m view would instead bury the
        /// city in soup at walking distance.
        /// </summary>
        public static (float Start, float End) FogRange(TheBlockConfig.FogSpec fog)
        {
            if (fog == null) return (DrawDistance * 0.22f, DrawDistance * 0.875f);
            return (DrawDistance * fog.Near / WebDrawDistance, DrawDistance * fog.Far / WebDrawDistance);
        }

        /// <summary>
        /// The haze colour. Falls back to the config's <c>background</c>, because the two are the
        /// same value in <c>config.ts</c> and their being the same is the point: fog that does not
        /// match what is behind it draws a visible band instead of hiding one.
        /// </summary>
        public static Color FogColor(TheBlockConfig.Root config)
        {
            if (config?.Fog != null) return TheBlockConfig.ColorFromHex(config.Fog.Color);
            return TheBlockConfig.ColorFromHex(config?.Background ?? 0x9FB8D4);
        }
    }
}
