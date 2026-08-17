using UnityEngine;

namespace TheBlock.World
{
    /// <summary>
    /// Where the auto shop is - U35g.
    ///
    /// The first place in the port that the web build never had, so there is nothing in
    /// <c>config.ts</c> to export and no <c>Convert.Pos</c> to go through: these are Unity-space
    /// numbers, authored here. The editor builder places the model with them and the map pins it
    /// with them, so the two cannot disagree.
    /// </summary>
    public static class AutoShopSpec
    {
        /// <summary>Lot origin - the slab's ground-level centre. Between Procedural City 4 and 6.</summary>
        public static readonly Vector3 Position = new(-96.1f, 0f, 245f);

        /// <summary>
        /// The model's front is +Z (Blender −Y → glTF +Z; glTFast negates only X). −90° turns it to
        /// face −X, onto the new north-south street at x = −110.
        /// </summary>
        public const float YawDeg = -90f;

        /// <summary>The scene object the builder creates.</summary>
        public const string ObjectName = "Place_AutoShop";

        /// <summary>Map label and pin.</summary>
        public const string MapLabel = "Auto Shop";
        public const string MapIcon = "🚗";
    }
}
