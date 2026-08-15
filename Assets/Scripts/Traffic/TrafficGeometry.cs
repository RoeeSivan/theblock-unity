using UnityEngine;

namespace TheBlock.Traffic
{
    /// <summary>
    /// Pure decision geometry for ambient traffic — the port of <c>traffic-ai.ts</c>, plus
    /// <c>buildCorner</c> from <c>traffic-graph.ts</c>.
    ///
    /// No <c>MonoBehaviour</c>, no scene access, no allocation: everything takes the caller's
    /// scratch and returns a number or writes into a buffer the caller owns. The web build split
    /// these out so they were unit-testable in isolation, and the same split is worth keeping —
    /// this is the part of the traffic sim that is easy to get subtly wrong and easy to check.
    /// </summary>
    public static class TrafficGeometry
    {
        /// <summary>An oriented bumper box in the XZ plane. Both a car and the player fit this shape.</summary>
        public struct Box
        {
            public float X;
            public float Z;

            /// <summary>Unit forward, XZ.</summary>
            public float Fx;

            public float Fz;

            /// <summary>Half extent along the length.</summary>
            public float HalfLen;

            /// <summary>Half extent across the width.</summary>
            public float HalfW;
        }

        /// <summary>
        /// Is a point inside this car's forward obstacle cone? <paramref name="otherHalf"/> is the
        /// obstacle's own half-size — another car's half-length, the player radius, a pedestrian
        /// radius. Mirrors the follow gate: ahead of the bumper, within a lane width laterally, and
        /// inside the follow distance.
        /// </summary>
        public static bool InFrontCone(
            in Box car, float px, float pz, float otherHalf, float gapLateral, float gapFollow)
        {
            float rx = px - car.X;
            float rz = pz - car.Z;
            float forward = rx * car.Fx + rz * car.Fz;
            if (forward <= 0f) return false;

            float lateral = rx * -car.Fz + rz * car.Fx;
            return Mathf.Abs(lateral) <= gapLateral + otherHalf * 0.3f && forward < gapFollow + otherHalf;
        }

        /// <summary>Signed bumper-to-bumper gap from this car to a point ahead of it.</summary>
        public static float BumperGap(in Box car, float px, float pz, float otherHalf) =>
            (px - car.X) * car.Fx + (pz - car.Z) * car.Fz - otherHalf - car.HalfLen;

        /// <summary>
        /// Do two oriented bumper boxes come within <paramref name="margin"/>? 2D SAT over the four
        /// box axes.
        ///
        /// Unlike the forward cone this fires from ANY angle, which is what catches a car turning
        /// across another one inside a junction — a case where neither is "ahead in lane" and both
        /// would otherwise drive straight through each other.
        /// </summary>
        public static bool BoxesOverlap(in Box a, in Box b, float margin)
        {
            float dx = b.X - a.X;
            float dz = b.Z - a.Z;
            return !(Separated(a, b, dx, dz, a.Fx, a.Fz, margin) ||
                     Separated(a, b, dx, dz, -a.Fz, a.Fx, margin) ||
                     Separated(a, b, dx, dz, b.Fx, b.Fz, margin) ||
                     Separated(a, b, dx, dz, -b.Fz, b.Fx, margin));
        }

        private static bool Separated(
            in Box a, in Box b, float dx, float dz, float ax, float az, float margin) =>
            Mathf.Abs(dx * ax + dz * az) > ProjectedRadius(a, ax, az) + ProjectedRadius(b, ax, az) + margin;

        private static float ProjectedRadius(in Box box, float ax, float az) =>
            box.HalfLen * Mathf.Abs(box.Fx * ax + box.Fz * az) +
            box.HalfW * Mathf.Abs(-box.Fz * ax + box.Fx * az);

        // --- intersection corners ----------------------------------------------------------------

        /// <summary>Points in a corner's lookup table. The web build's <c>CORNER_SEGMENTS</c> + 1.</summary>
        public const int CornerPoints = 9;

        /// <summary>
        /// Builds a one-shot smooth turn through an intersection into the caller's buffer: a
        /// quadratic bezier from the incoming lane point to the outgoing one, with the control point
        /// at the intersection of the two lane lines — so a right turn arcs tight and a left turn
        /// arcs wide, which is what a real one does.
        ///
        /// The buffer is the car's own, reused for every turn it ever makes, so a city's worth of
        /// cornering allocates nothing.
        ///
        /// <b>Y is interpolated, not re-probed.</b> The web build raycasts the ground at each LUT
        /// point; here the two lane samples already carry their baked road height and a junction is
        /// flat between them, so a lerp is the same answer without a raycast per turn.
        /// </summary>
        /// <returns>The curve's arc length.</returns>
        public static float BuildCorner(
            Vector3 from, Vector3 fromTangent, Vector3 to, Vector3 toTangent, Vector3 nodeFallback,
            Vector3[] buffer)
        {
            // Intersect the two lane lines in XZ.
            float denominator = fromTangent.x * toTangent.z - fromTangent.z * toTangent.x;
            float cx, cz;

            if (Mathf.Abs(denominator) > 1e-3f)
            {
                float t = ((to.x - from.x) * toTangent.z - (to.z - from.z) * toTangent.x) / denominator;
                cx = from.x + fromTangent.x * t;
                cz = from.z + fromTangent.z * t;

                // A wild intersection point — near-parallel lines — would loop the car off the map.
                float dx = cx - nodeFallback.x;
                float dz = cz - nodeFallback.z;
                if (t < 0f || dx * dx + dz * dz > 625f)
                {
                    cx = nodeFallback.x;
                    cz = nodeFallback.z;
                }
            }
            else
            {
                // Parallel: straight through, or a U-turn. Bow through the node centre.
                cx = nodeFallback.x;
                cz = nodeFallback.z;
            }

            var control = new Vector3(cx, (from.y + to.y) * 0.5f, cz);

            float length = 0f;
            for (int i = 0; i < CornerPoints; i++)
            {
                float t = i / (float)(CornerPoints - 1);
                float inv = 1f - t;
                buffer[i] = inv * inv * from + 2f * inv * t * control + t * t * to;
                if (i > 0) length += Vector3.Distance(buffer[i - 1], buffer[i]);
            }

            return Mathf.Max(length, 0.01f);
        }

        /// <summary>Reads a corner buffer at arc length <paramref name="s"/>, assuming even spacing.</summary>
        public static Vector3 CornerAt(Vector3[] buffer, float length, float s)
        {
            float t = Mathf.Clamp(s, 0f, length) / length * (CornerPoints - 1);
            int i = Mathf.Min((int)t, CornerPoints - 2);
            return Vector3.Lerp(buffer[i], buffer[i + 1], t - i);
        }
    }
}
