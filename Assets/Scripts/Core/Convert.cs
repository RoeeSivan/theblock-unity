using UnityEngine;

namespace TheBlock.Core
{
    /// <summary>
    /// Converts hand-authored coordinates from the three.js build into Unity space.
    ///
    /// three.js is right-handed Y-up; Unity is left-handed Y-up. glTFast resolves that on import by
    /// negating X and passing Y and Z through untouched. Mesh data therefore arrives already
    /// converted — but every number typed by hand into <c>config.ts</c> does not convert itself:
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
    ///    edge "lands at x16". Converted, Unity measures max.x = -16.0 — which confirms the yaw
    ///    conversion as well as the position one.
    /// </summary>
    public static class Convert
    {
        /// <summary>A three.js world position in Unity space.</summary>
        public static Vector3 Pos(Vector3 p) => new Vector3(-p.x, p.y, p.z);

        /// <summary>A three.js world position in Unity space, from loose components.</summary>
        public static Vector3 Pos(float x, float y, float z) => new Vector3(-x, y, z);

        /// <summary>
        /// A three.js direction or offset in Unity space. Same formula as <see cref="Pos"/> — the
        /// conversion is a change of basis, so it applies to directions unchanged — but call this
        /// when the vector is a direction so the intent reads at the call site.
        /// </summary>
        public static Vector3 Dir(Vector3 d) => new Vector3(-d.x, d.y, d.z);

        /// <summary>
        /// A three.js yaw in Unity space. Unit-agnostic: negating is correct for radians and
        /// degrees alike, because negating X reverses the sense of rotation about Y.
        /// </summary>
        public static float Yaw(float yaw) => -yaw;

        /// <summary>A Unity Y-rotation from a three.js yaw in radians (what config.ts stores).</summary>
        public static Quaternion RotFromRadians(float yawRadians) =>
            Quaternion.Euler(0f, -yawRadians * Mathf.Rad2Deg, 0f);

        /// <summary>A Unity Y-rotation from a three.js yaw already in degrees.</summary>
        public static Quaternion RotFromDegrees(float yawDegrees) =>
            Quaternion.Euler(0f, -yawDegrees, 0f);

        /// <summary>
        /// A three.js polyline in Unity space — road centrelines, patrol routes, mission paths.
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
