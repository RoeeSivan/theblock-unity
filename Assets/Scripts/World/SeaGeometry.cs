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
        /// Crest height of the dry-sand BERM, metres above the city ground.
        ///
        /// <b>This is a Unity-side addition, and it is a bug fix rather than a flourish.</b> The web
        /// build returns a flat 0 for every metre of dry sand, and both beach shaders key their wet
        /// band off <c>depth = level - seabed</c>. Flat sand therefore sits at <c>depth = 0</c> from
        /// the waterline to the city, and <c>smoothstep(-wetBandDry, wetBandSea, 0)</c> is <b>0.54</b>
        /// - so every pixel of "dry" sand renders 54% blended into the dark wet tone, uniformly, with
        /// no tide line anywhere. The gold <c>beach.look.dryColor</c> the config carries can never be
        /// drawn at all. That is why the strip reads dull and dark instead of sandy.
        ///
        /// Giving the sand a berm - it rises off the waterline, crests, and comes back down to meet
        /// the city ground - makes <c>depth</c> genuinely negative across most of the beach, which is
        /// what the wet band was written to expect. It also happens to be what a real beach looks
        /// like, and the tide line lands where the water is instead of covering everything.
        ///
        /// Sized against <c>beach.look.wetBandDry</c> (0.6 m): the crest has to clear it by enough
        /// that the middle of the beach is fully dry. At 1.2 m over a 25 m run that is the inner ~67%
        /// of the sand, with a ~4 m tide line at the water - and an 11% grade, which a car climbs
        /// without noticing.
        /// </summary>
        public const float BermHeight = 1.2f;

        /// <summary>Waterline X in Unity space.</summary>
        public static float ShoreX(TheBlockConfig.SeaSpec sea) => Convert.Pos(sea.ShoreX, 0f, 0f).x;

        /// <summary>
        /// Seabed height (world Y) at a Unity world X. Landward of the shore the dry sand rises to a
        /// berm and falls back to 0 at the city ground; seaward it is a linear ramp down to
        /// <c>beach.deepY</c> over <c>beach.wadeRun</c> metres, flat again past the ramp's foot.
        ///
        /// A half sine is the berm's profile because BOTH ends have to be pinned: 0 at the waterline
        /// (or the tide line starts on a step) and 0 at the landward edge (or the beach meets the
        /// city as a 1.2 m cliff). Anything that only ramps up leaves one of those two.
        /// </summary>
        public static float SeabedHeight(TheBlockConfig.SeaSpec sea, float unityX)
        {
            var shore = ShoreX(sea);
            if (unityX <= shore)
            {
                // Landward: the dry-sand berm. `dryWidth` is the whole run from the waterline to the
                // city ground, and it is the same number BuildBeach cuts the mesh's landward edge on.
                var run = Mathf.Max(0.0001f, sea.Beach.DryWidth);
                var rise = Mathf.Clamp01((shore - unityX) / run);
                return BermHeight * Mathf.Sin(rise * Mathf.PI);
            }

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
