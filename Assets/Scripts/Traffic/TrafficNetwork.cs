using UnityEngine;

namespace TheBlock.Traffic
{
    /// <summary>
    /// The street graph, baked. U17's port of <c>traffic-graph.ts</c> plus <c>npc/path.ts</c>'s
    /// <c>buildPath</c> — one edge per authored polyline, endpoints within <c>snapDist</c> merged
    /// into shared nodes, each edge resampled to an even-arc-length lookup table with its road
    /// height already resolved.
    ///
    /// <b>Why an asset and not a runtime build.</b> The web build constructs this at load and pays
    /// for it there: <c>buildPath</c> raycasts the ground once per LUT sample, which over 12.7 km at
    /// a sample every 2 m is ~6,400 downward rays on the main thread before the first frame. That
    /// cost is a load-time spike in three.js and would be the same spike here, for numbers that
    /// cannot change unless a district moves. So the whole graph is baked by
    /// <c>WorldBuilder.Traffic</c> — same polylines, same snapping, same Catmull-Rom, same
    /// <c>GroundY</c> the zebras use — and the runtime does zero raycasts for traffic, ever.
    ///
    /// <b>Everything in here is already in Unity space.</b> The conversion happens once, in the
    /// builder, and every tangent, lane offset and corner downstream is computed on converted
    /// geometry (port rule 1). In particular the lane side is <c>Cross(up, tangent)</c> — the right
    /// of travel in a LEFT-handed frame — which is the same physical side as the web build's
    /// <c>(-tz, tx)</c> in a right-handed one, and is not the same arithmetic.
    ///
    /// U19's police pursuit wants this asset too: it is the only description of where a car may be.
    /// </summary>
    public class TrafficNetwork : ScriptableObject
    {
        /// <summary>An intersection: where two or more authored streets share an endpoint.</summary>
        [System.Serializable]
        public struct Node
        {
            public Vector3 Position;

            /// <summary>Slice of <see cref="NodeEdges"/> holding the edge ids that touch this node.</summary>
            public int EdgeStart;

            public int EdgeCount;
        }

        /// <summary>One street, resampled and Y-baked.</summary>
        [System.Serializable]
        public class Edge
        {
            /// <summary>Node at s = 0.</summary>
            public int A;

            /// <summary>Node at s = <see cref="Length"/>.</summary>
            public int B;

            /// <summary>Evenly spaced by arc length, so index → s is a multiply, not a search.</summary>
            public Vector3[] Points;

            public float Length;

            /// <summary>Parallel lanes per direction. 1 for an ordinary street.</summary>
            public int Lanes = 1;

            /// <summary>Innermost lane's offset from the centreline, metres.</summary>
            public float LaneOffset;

            /// <summary>Centreline to kerb. Sets how wide the carve and the zebra are.</summary>
            public float HalfWidth;

            /// <summary>Midpoint and radius of the polyline, for cheap "is this street near the player".</summary>
            public Vector3 Centre;

            public float Radius;
        }

        [SerializeField] private Node[] nodes = System.Array.Empty<Node>();
        [SerializeField] private int[] nodeEdges = System.Array.Empty<int>();
        [SerializeField] private Edge[] edges = System.Array.Empty<Edge>();

        /// <summary>Total centreline length, metres. Reported at build so a bad bake is visible.</summary>
        [SerializeField] private float totalLength;

        public Node[] Nodes => nodes;
        public int[] NodeEdges => nodeEdges;
        public Edge[] Edges => edges;
        public float TotalLength => totalLength;

        /// <summary>Set once, by the builder.</summary>
        public void Fill(Node[] newNodes, int[] newNodeEdges, Edge[] newEdges, float length)
        {
            nodes = newNodes;
            nodeEdges = newNodeEdges;
            edges = newEdges;
            totalLength = length;
        }

        /// <summary>The edge ids touching a node, without allocating.</summary>
        public int EdgeAt(in Node node, int index) => nodeEdges[node.EdgeStart + index];

        /// <summary>A node is lit when three or more streets meet at it — <c>traffic-lights.ts</c>'s rule.</summary>
        public bool IsLit(int nodeId) => nodes[nodeId].EdgeCount >= 3;

        // --- sampling -------------------------------------------------------------------------

        /// <summary>Position on an edge's centreline at arc length <paramref name="s"/>.</summary>
        public Vector3 PointAt(int edgeId, float s)
        {
            var edge = edges[edgeId];
            int segments = edge.Points.Length - 1;
            if (segments < 1) return edge.Points.Length > 0 ? edge.Points[0] : Vector3.zero;

            float t = Mathf.Clamp(s, 0f, edge.Length) / edge.Length * segments;
            int i = Mathf.Min((int)t, segments - 1);
            return Vector3.Lerp(edge.Points[i], edge.Points[i + 1], t - i);
        }

        /// <summary>
        /// A directed lane sample: the centreline at <paramref name="s"/>, pushed
        /// <paramref name="off"/> metres to the RIGHT of travel, with the travel tangent.
        ///
        /// <paramref name="dir"/> is +1 for a→b and −1 for b→a; <paramref name="s"/> is always in
        /// the edge's own a→b frame, exactly as <c>sampleLane</c> defines it.
        /// </summary>
        public void SampleLane(int edgeId, float s, int dir, float off, out Vector3 position, out Vector3 tangent)
        {
            var edge = edges[edgeId];

            // Tangent from two samples half a metre apart, pointing the way this car travels. Near
            // an end, step backwards instead, so the pair never collapses onto one point.
            float s0 = s;
            float s1 = s + dir * 0.5f;
            if (s1 < 0f || s1 > edge.Length)
            {
                s1 = s;
                s0 = s - dir * 0.5f;
            }

            var a = PointAt(edgeId, s0);
            var b = PointAt(edgeId, s1);
            tangent = new Vector3(b.x - a.x, 0f, b.z - a.z);
            float magnitude = tangent.magnitude;
            tangent = magnitude > 1e-5f ? tangent / magnitude : Vector3.forward;

            // Right of travel. In Unity's LEFT-handed frame that is Cross(up, forward) — the web
            // build's (-tz, tx) is the same side written for a right-handed one, and transcribing
            // it literally would put every car in the oncoming lane.
            var right = Vector3.Cross(Vector3.up, tangent);
            position = PointAt(edgeId, s) + right * off;
        }

        /// <summary>Lateral offset of a lane's centre from the authored centreline.</summary>
        public float LaneOffsetFor(int edgeId, int lane, float laneGap) =>
            edges[edgeId].LaneOffset + lane * laneGap;
    }
}
