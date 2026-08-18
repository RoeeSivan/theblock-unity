using TheBlock.Core;
using UnityEngine;

namespace TheBlock.World
{
    /// <summary>
    /// The shape of the shoreline, in Unity space - one source of truth for the sand MESH, the
    /// water shader's depth ramp, and (later) the swim state's "how deep am I" test.
    ///
    /// The web build has the same function in <c>world/sea.ts</c> and reuses it the same way; the
    /// drift it prevents is water that tints as if it were deep over sand that is visibly shallow.
    ///
    /// Handedness: <c>config.sea.shoreX</c> is -430 in three.js, so the shore is at Unity
    /// <c>x = +430</c> and the SEA is everything EAST of it (larger x). Landward is smaller x.
    /// That single flip is taken here, through <see cref="Convert"/>, and nowhere else.
    /// </summary>
    public static class SeaGeometry
    {
        /// <summary>
        /// How wide the wet tide line is, in metres landward of the waterline.
        ///
        /// <b>Why the sand needs this at all.</b> Both beach shaders - the web's and this port's -
        /// used to key their wet band off <c>depth = level - seabed</c>. Dry sand is flat at 0 and sea
        /// level is 0, so <c>depth</c> is exactly 0 across every metre of it and
        /// <c>smoothstep(-wetBandDry, wetBandSea, 0)</c> is <b>0.54</b>: the whole beach renders 54%
        /// blended into the dark wet tone, uniformly, with no tide line anywhere, and the gold
        /// <c>beach.look.dryColor</c> the config carries is never drawn on a single pixel.
        ///
        /// A seabed height cannot express "how far from the water am I" on flat ground, so the shader
        /// measures that distance directly instead. Everything at or past the waterline is soaked;
        /// the sand dries out over this run. 5 m against a 25 m beach leaves most of it dry gold with
        /// a band of damp sand at the water, which is what a beach looks like.
        ///
        /// ⚠ A geometric BERM was tried first (2026-08-18) and reverted the same hour. It worked, and
        /// it cost more than it was worth: raising the sand 1.2 m buried everything any builder had
        /// placed on the beach at y 0 - the U27 Beach Dancer measured 1.12 m under the surface - and
        /// every one of those would have needed its own fix. The dark sand turned out not to be the
        /// wet band's fault anyway; see the header of <c>Beach.shader</c>.
        /// </summary>
        public const float TideRun = 5f;

        /// <summary>Waterline X in Unity space.</summary>
        public static float ShoreX(TheBlockConfig.SeaSpec sea) => Convert.Pos(sea.ShoreX, 0f, 0f).x;

        /// <summary>
        /// Seabed height (world Y) at a Unity world X. Flat at 0 landward of the shore - dry sand,
        /// flush with the city ground, and flush with everything placed on it - then a linear ramp
        /// down to <c>beach.deepY</c> over <c>beach.wadeRun</c> metres, flat again past the ramp's
        /// foot.
        /// </summary>
        public static float SeabedHeight(TheBlockConfig.SeaSpec sea, float unityX)
        {
            var shore = ShoreX(sea);
            if (unityX <= shore) return 0f; // landward: dry sand at the waterline height
            var t = Mathf.Min((unityX - shore) / sea.Beach.WadeRun, 1f);
            return sea.Beach.DeepY * t;
        }

        /// <summary>Water depth at a Unity world X. Negative on dry sand.</summary>
        public static float Depth(TheBlockConfig.SeaSpec sea, float unityX) =>
            sea.Level - SeabedHeight(sea, unityX);

        /// <summary>
        /// Is this point inside the sea's rectangle? A region test, not a raycast - the water has no
        /// collider to hit, by design (see <c>WorldBuilder.Sea</c>).
        ///
        /// The web build writes <c>x &lt; shoreX</c>; here it is <c>x &gt; ShoreX</c>, because X is
        /// negated and the sea therefore lies at LARGER x. Z is not flipped, so the span test is the
        /// web's verbatim. This is the one place that sign lives for the swim state.
        /// </summary>
        public static bool InSeaRegion(TheBlockConfig.SeaSpec sea, float unityX, float unityZ) =>
            unityX > ShoreX(sea) && Mathf.Abs(unityZ - sea.CenterZ) < sea.Length * 0.5f;

        /// <summary>
        /// Is the water here deep enough to swim in? Inside the region and deeper than
        /// <c>swim.wadeDepth</c>, measured from the swimmer's float height rather than from sea
        /// level - which is what the web build does, and it matters: with the config's numbers the
        /// swim starts about 6.4 m past the waterline instead of 11.7 m.
        ///
        /// Shallower than that is wading, and wading needs no state of its own: the seabed is a real
        /// MeshCollider, so the controller walks down it and gravity keeps the feet on it.
        /// </summary>
        public static bool IsSwimming(TheBlockConfig.SeaSpec sea, Vector3 unityPos)
        {
            if (sea?.Swim == null) return false;
            return InSeaRegion(sea, unityPos.x, unityPos.z)
                   && sea.Swim.SurfaceY - SeabedHeight(sea, unityPos.x) > sea.Swim.WadeDepth;
        }
    }
}
