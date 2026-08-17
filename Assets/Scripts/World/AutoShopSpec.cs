using UnityEngine;

namespace TheBlock.World
{
    /// <summary>
    /// Where the auto shop is, and what it charges - U35g.
    ///
    /// The first place in the port that the web build never had, so there is nothing in
    /// <c>config.ts</c> to export and no <c>Convert.Pos</c> to go through: these are Unity-space
    /// numbers, authored here. The editor builder places the model with them, the map pins it
    /// with them, and <see cref="AutoShop"/> measures against them, so the three cannot disagree.
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

        // --- the service point --------------------------------------------------------------------

        /// <summary>
        /// Where you pull up. The shutter's centre is glb (−1.5, _, 1.06) → world (−95.0, _, 246.5)
        /// with the yaw above, and the lot faces −X; nine metres out from it lands on the kerb at
        /// x ≈ −103, so a car stopped on the street or on the apron is inside <see cref="PaintRadius"/>.
        /// </summary>
        public static readonly Vector3 ServicePoint = new(-104f, 0f, 246.5f);

        /// <summary>Within this of the service point the shutter rolls up - for anyone, on anything.</summary>
        public const float OpenRadius = 14f;

        /// <summary>Within this of the service point, stopped, in a paintable vehicle, the shop will paint.</summary>
        public const float PaintRadius = 8f;

        /// <summary>"Stopped": below this forward speed, m/s - the 7-Eleven counter's stand-still.</summary>
        public const float StoppedSpeed = 1f;

        /// <summary>The shutter roll, seconds, one way.</summary>
        public const float ShutterSeconds = 1.5f;

        /// <summary>The shutter's Y scale when open - not 0, so its collider keeps a shape.</summary>
        public const float ShutterOpenScale = 0.04f;

        // --- the paint ----------------------------------------------------------------------------

        /// <summary>One coat. Between a power-up and a tank of fuel.</summary>
        public const int PaintPrice = 30;

        /// <summary>
        /// The ten swatches, packed <c>0xRRGGBB</c>. The first nine are the city's own - they are the
        /// traffic and lot palettes' values, so a repainted car reads as one that could have been
        /// parked there - and purple is the one colour the streets never wear.
        /// </summary>
        public static readonly (int Hex, string Name)[] Palette =
        {
            (0xF5F6F7, "White"),
            (0x14151A, "Black"),
            (0x9A9B9D, "Silver"),
            (0xB31218, "Red"),
            (0xD96716, "Orange"),
            (0xD9A514, "Yellow"),
            (0x2E6B34, "Green"),
            (0x1F4F9E, "Blue"),
            (0x16263F, "Navy"),
            (0x5B2A86, "Purple"),
        };
    }
}
