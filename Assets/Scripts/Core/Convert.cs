using UnityEngine;

namespace TheBlock.Core
{
    /// <summary>
    /// Converts hand-authored coordinates from the three.js build into Unity space.
    ///
    /// three.js is right-handed Y-up; Unity is left-handed Y-up. glTFast resolves that on import by
    /// negating X and passing Y and Z through untouched. Mesh data therefore arrives already
    /// converted - but every number typed by hand into <c>config.ts</c> does not convert itself:
    /// district offsets, spawn points, road polylines, mission waypoints, POIs, camera anchors.
    ///
    /// Miss one and that thing is mirrored against everything else, which looks *almost* right.
    /// This is the single place that flip is allowed to happen. Never inline a sign flip elsewhere.
    ///
    /// Verified empirically, not assumed (2026-08-12):
    ///  - procedural-city-2 submesh 1: glTF X[-33.38, 33.13] imported as Unity X[-33.13, 33.38],
    ///    with Z unchanged. Reconfirmed on cities 3-7.
    ///  - Landmark: config.ts places city 2 at x:-150 and notes it "leaves a ~14 m gap west of
    ///    downtown". Placed at Unity x:+150, the measured gap is 14.3 m.
    ///  - Landmark: config.ts places the 7-Eleven at x:37 yaw:-PI/2 and notes its forecourt's far
    ///    edge "lands at x16". Converted, Unity measures max.x = -16.0 - which confirms the yaw
    ///    conversion as well as the position one.
    /// </summary>
    public static class Convert
    {
        /// <summary>A three.js world position in Unity space.</summary>
        public static Vector3 Pos(Vector3 p) => new Vector3(-p.x, p.y, p.z);

        /// <summary>A three.js world position in Unity space, from loose components.</summary>
        public static Vector3 Pos(float x, float y, float z) => new Vector3(-x, y, z);

        /// <summary>
        /// A three.js direction or offset in Unity space. Same formula as <see cref="Pos"/> - the
        /// conversion is a change of basis, so it applies to directions unchanged - but call this
        /// when the vector is a direction so the intent reads at the call site.
        /// </summary>
        public static Vector3 Dir(Vector3 d) => new Vector3(-d.x, d.y, d.z);

        /// <summary>
        /// A three.js yaw in Unity space. Unit-agnostic: negating is correct for radians and
        /// degrees alike, because negating X reverses the sense of rotation about Y.
        /// </summary>
        public static float Yaw(float yaw) => -yaw;

        /// <summary>
        /// An offset expressed in a MODEL's own local frame - a camera boom, a seat anchor, a
        /// muzzle point. This is not the same conversion as <see cref="Pos"/>.
        ///
        /// ONLY Z FLIPS. Both engines put a model's right at local <c>+X</c> and its up at
        /// <c>+Y</c>; they disagree only about forward, which is <c>-Z</c> in three.js and
        /// <c>+Z</c> here. So the same physical spot behind a character is <c>z = +2.5</c> in the
        /// web build and <c>z = -2.5</c> in Unity, while left stays left. Feed a config offset
        /// through <see cref="Pos"/> by mistake and the camera ends up in the character's face.
        ///
        /// Z verified 2026-08-13 against Joe: the U2 checkpoint camera sits along his
        /// <c>transform.forward</c> and looks at his face, so his visual front is Unity's local
        /// <c>+Z</c>, and "behind" is local <c>-Z</c>.
        ///
        /// X NEGATION REMOVED 2026-08-13 (U9) - it was here on the assumption that this shared
        /// <see cref="Pos"/>'s mirroring, and the first non-zero X proved otherwise. A world
        /// position flips because the whole world is mirrored on import. A model-local offset gets
        /// that same flip from glTFast AND a second one from <see cref="ModelFacing"/>, which turns
        /// the model back around - two flips, so none. Measured, not reasoned: the Mustang's rig
        /// names its own corners, and <c>wheel_Front_L_0</c> at three.js-local
        /// <c>(-0.992, 0.479, -1.562)</c> (left of centre, ahead of the origin) has to land on
        /// Unity's left at <c>(-0.992, 0.479, +1.562)</c>, which is exactly where the imported
        /// bone sits once <see cref="ModelFacing"/> is applied. Negating X put it on the right.
        /// Cross-checked against <c>config.vehicle.driver.seats</c>, whose Mustang block places the
        /// driver at <c>x = -2.31</c> - beside the LEFT-hand door the rig calls <c>Door_L_6</c>.
        /// </summary>
        public static Vector3 ModelOffset(Vector3 offset) => new Vector3(offset.x, offset.y, -offset.z);

        /// <summary>
        /// A rotation axis expressed in a MODEL's own local frame, in Unity space - a door hinge, a
        /// rotor mast, a turret pin. Pair it with the config angle in degrees, unchanged.
        ///
        /// This is not <see cref="ModelOffset"/>: an axis is a pseudo-vector and does not transform
        /// like a point. glTFast converts a node's local rotation quaternion
        /// <c>(x, y, z, w)</c> → <c>(x, -y, -z, w)</c>, which is conjugation by <c>i</c> - an
        /// automorphism, so it applies to a hinge delta exactly as it applies to the bone's rest
        /// pose, and the two still compose. Reading the axis out of
        /// <c>(a·sin(θ/2), cos(θ/2))</c> leaves the ANGLE alone and negates Y and Z of the axis.
        ///
        /// So <c>config.vehicle.cars[].door.axis = 'z'</c> with <c>openAngle = -1.134</c> rad
        /// becomes Unity's local <c>(0, 0, -1)</c> at <c>-64.97°</c> - the same swing, read in a
        /// left-handed frame.
        /// </summary>
        public static Vector3 ModelAxis(Vector3 axis) => new Vector3(axis.x, -axis.y, -axis.z);

        /// <summary>
        /// Turns a three.js-authored model so the thing it calls its front faces Unity's <c>+Z</c>.
        ///
        /// This is the rotational twin of <see cref="ModelOffset"/> and it exists for the same
        /// reason: three.js drives an object along <c>-Z</c>, Unity along <c>+Z</c>. A model with no
        /// front - a district, a building - never needs it. A model that drives, walks or aims does.
        ///
        /// The two flips compose into exactly one 180° yaw, which is worth seeing spelled out.
        /// Verified 2026-08-13 on the Mustang, whose rig names its own corners:
        /// <c>wheel_Front_L_0</c> imports at <c>(0.992, 0.479, -1.562)</c> - the +X side, and behind
        /// the origin. Rotated by this, it lands at <c>(-0.992, 0.479, +1.562)</c>: in front (z &gt; 0)
        /// and on the left (x &lt; 0), which is what the bone calls itself. So the same 180° that
        /// points the nose down +Z also puts the L/R names back on Unity's hands. If a model ever
        /// needs one but not the other, this assumption has broken - re-measure before working round it.
        ///
        /// A per-model <c>modelYaw</c> from config composes on top: both are rotations about Y, so
        /// <c>RotFromRadians(modelYaw) * ModelFacing</c> is order-independent.
        /// </summary>
        public static readonly Quaternion ModelFacing = Quaternion.Euler(0f, 180f, 0f);

        /// <summary>A Unity Y-rotation from a three.js yaw in radians (what config.ts stores).</summary>
        public static Quaternion RotFromRadians(float yawRadians) =>
            Quaternion.Euler(0f, -yawRadians * Mathf.Rad2Deg, 0f);

        /// <summary>A Unity Y-rotation from a three.js yaw already in degrees.</summary>
        public static Quaternion RotFromDegrees(float yawDegrees) =>
            Quaternion.Euler(0f, -yawDegrees, 0f);

        /// <summary>
        /// A three.js polyline in Unity space - road centrelines, patrol routes, mission paths.
        /// Returns a new array; the input is left alone.
        /// </summary>
        public static Vector3[] Path(Vector3[] points)
        {
            if (points == null) return null;
            var result = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++) result[i] = Pos(points[i]);
            return result;
        }
    }
}
