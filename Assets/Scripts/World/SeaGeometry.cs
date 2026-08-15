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
    }
}
