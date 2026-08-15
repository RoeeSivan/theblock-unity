using TheBlock.Core;
using UnityEngine;

namespace TheBlock.World
{
    /// <summary>
    /// The shape of the shoreline, in Unity space — one source of truth for the sand MESH, the
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
        /// <summary>Waterline X in Unity space.</summary>
        public static float ShoreX(TheBlockConfig.SeaSpec sea) => Convert.Pos(sea.ShoreX, 0f, 0f).x;

        /// <summary>
        /// Seabed height (world Y) at a Unity world X. Flat at 0 landward of the shore — dry sand,
        /// flush with the city ground — then a linear ramp down to <c>beach.deepY</c> over
        /// <c>beach.wadeRun</c> metres, flat again past the ramp's foot.
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
        /// Is this point inside the sea's rectangle? A region test, not a raycast — the water has no
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
        /// level — which is what the web build does, and it matters: with the config's numbers the
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
