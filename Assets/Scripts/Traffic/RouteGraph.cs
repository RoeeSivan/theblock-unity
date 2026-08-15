using UnityEngine;

namespace TheBlock.Traffic
{
    /// <summary>
    /// The street graph as something a car can be ROUTED over, baked.
    ///
    /// <b>Why this exists at all.</b> <see cref="TrafficNetwork"/> describes where a car may be, and
    /// U17's traffic drives it by picking a uniformly random outgoing edge at every junction — a
    /// random walk never needs the graph to be connected, so U17 could not have noticed that it is
    /// not. Measured on the baked asset: <b>97 nodes / 142 edges in FIVE connected components</b>
    /// (49, 18, 16, 12 and 2 nodes). That is the same fault that killed routed pursuit in the web
    /// build — its own config comment says routing was deleted because the network "is authored
    /// district by district and is not fully connected, so 'drive the roads to the player' was
    /// regularly an impossible instruction and the pursuit silently did nothing."
    ///
    /// The cause is mundane and fixable: <c>BuildGraph</c> mints nodes only where polyline ENDPOINTS
    /// meet, so a side street that <b>T</b>s into the middle of another street produces no junction.
    /// Four of the five islands touch one 1,364 m polyline, at gaps of 0.65 m, 1.13 m, 2.20 m and
    /// 2.73 m. Stitching those — plus the handful of true segment crossings that share no node —
    /// leaves <b>two</b> components, the larger holding 97.9% of the network.
    ///
    /// <b>This is a VIEW, never a mutation.</b> Splitting <see cref="TrafficNetwork"/>'s own edges
    /// would change <c>EdgeCount</c>, which changes <see cref="TrafficNetwork.IsLit"/>
    /// (<c>EdgeCount >= 3</c>), which changes the 70 lit intersections, the 230 crossings and the 233
    /// poles U16 and U17 verified. So no geometry is copied and no edge is edited: a
    /// <see cref="Link"/> is an ARC-LENGTH SPAN over an existing edge, and every sample still goes
    /// through <see cref="TrafficNetwork.SampleLane"/> — same lane offsets, same baked road height,
    /// same handedness. A cop drives the right-hand lane on the same side as the traffic, and the
    /// runtime casts no rays for any of it.
    ///
    /// The remaining island is not stitched, it is AVOIDED: <see cref="TryNearest"/> takes a
    /// required component, so a route is never planned onto a street the planner cannot reach.
    /// </summary>
    public class RouteGraph : ScriptableObject
    {
        /// <summary>A junction: an original street endpoint, or a stitch point cut into an edge.</summary>
        [System.Serializable]
        public struct Node
        {
            public Vector3 Position;

            /// <summary>Slice of <see cref="Links"/> leaving this node. Links are sorted by their origin.</summary>
            public int LinkStart;

            public int LinkCount;

            /// <summary>Which connected component this node belongs to. 0 is the largest.</summary>
            public int Component;
        }

        /// <summary>
        /// One directed hop: travel <see cref="Edge"/> from arc length <see cref="S0"/> to
        /// <see cref="S1"/>, arriving at node <see cref="To"/>.
        ///
        /// <c>S1 &lt; S0</c> means travelling the edge backwards (b→a), which is exactly the
        /// <c>dir = -1</c> <see cref="TrafficNetwork.SampleLane"/> already understands — so driving
        /// the wrong way up an authored polyline still puts the car in the correct lane.
        /// </summary>
        [System.Serializable]
        public struct Link
        {
            public int Edge;
            public float S0;
            public float S1;

            /// <summary>Node at <see cref="S0"/>. Redundant with the owning slice, and worth it.</summary>
            public int From;

            public int To;

            /// <summary>The same span travelled the other way. Always valid.</summary>
            public int Twin;

            /// <summary>Metres. Always positive.</summary>
            public float Cost;
        }

        /// <summary>What <see cref="TryNearest"/> found: a point on a link, in the edge's own frame.</summary>
        public readonly struct Hit
        {
            public readonly int Link;

            /// <summary>Arc length in the EDGE's a→b frame, clamped to the link's span.</summary>
            public readonly float S;

            /// <summary>Horizontal distance from the query point, metres.</summary>
            public readonly float Distance;

            public readonly Vector3 Position;
            public readonly int Component;

            public Hit(int link, float s, float distance, Vector3 position, int component)
            {
                Link = link;
                S = s;
                Distance = distance;
                Position = position;
                Component = component;
            }
        }

        [SerializeField] private TrafficNetwork network;
        [SerializeField] private Node[] nodes = System.Array.Empty<Node>();
        [SerializeField] private Link[] links = System.Array.Empty<Link>();
        [SerializeField] private int componentCount;

        /// <summary>Centreline metres per component, indexed by component id. Largest first.</summary>
        [SerializeField] private float[] componentLength = System.Array.Empty<float>();

        // --- the nearest-link index ---------------------------------------------------------------
        // A uniform grid over XZ. `TrafficSystem.GatherNearbyEdges` gets away with a linear scan of
        // all 142 edges because it runs twice a second; a pursuit asks "which street am I on" for
        // every cop every replan and again whenever one is knocked off its route, so it is binned.

        [SerializeField] private Vector3 gridOrigin;
        [SerializeField] private float cellSize = 32f;
        [SerializeField] private int cellsX;
        [SerializeField] private int cellsZ;

        /// <summary>Start of each cell's slice of <see cref="cellItems"/>; length is cells + 1.</summary>
        [SerializeField] private int[] cellStart = System.Array.Empty<int>();

        [SerializeField] private int[] cellItems = System.Array.Empty<int>();

        public TrafficNetwork Network => network;
        public Node[] Nodes => nodes;
        public Link[] Links => links;
        public int ComponentCount => componentCount;
        public float[] ComponentLength => componentLength;

        /// <summary>Set once, by the builder.</summary>
        public void Fill(TrafficNetwork source, Node[] newNodes, Link[] newLinks, int components, float[] lengths)
        {
            network = source;
            nodes = newNodes;
            links = newLinks;
            componentCount = components;
            componentLength = lengths;
        }

        /// <summary>+1 when the link travels the edge a→b, −1 when b→a.</summary>
        public static int DirectionOf(in Link link) => link.S1 >= link.S0 ? 1 : -1;

        /// <summary>
        /// A drivable point on a link: the lane centre at arc length <paramref name="s"/>, on the
        /// right of travel, at the baked road height.
        /// </summary>
        public void SampleLink(in Link link, float s, float laneGap, out Vector3 position, out Vector3 tangent)
        {
            float offset = network.LaneOffsetFor(link.Edge, 0, laneGap);
            network.SampleLane(link.Edge, s, DirectionOf(link), offset, out position, out tangent);
        }

        // --- queries ------------------------------------------------------------------------------

        /// <summary>
        /// The closest link to a world point, optionally restricted to one component.
        ///
        /// <paramref name="requiredComponent"/> of −1 means "any". Passing the player's component is
        /// what stops a cop planning onto the downtown avenue, which is 265 m of real street with no
        /// node within 24 m of anything else and is therefore unreachable by road, however close it
        /// happens to be to where you are standing.
        /// </summary>
        public bool TryNearest(Vector3 point, float maxDistance, int requiredComponent, out Hit hit)
        {
            hit = default;
            if (network == null || links.Length == 0 || cellStart.Length == 0) return false;

            float best = maxDistance;
            bool found = false;

            // Rings outward from the query cell, so a point on a street is usually settled by the
            // first ring. The loop still has to finish a ring after the first hit: the nearest link
            // by cell is not the nearest by distance when the query sits near a cell corner.
            int maxRing = Mathf.Max(1, Mathf.CeilToInt(maxDistance / cellSize) + 1);
            int cx = Mathf.FloorToInt((point.x - gridOrigin.x) / cellSize);
            int cz = Mathf.FloorToInt((point.z - gridOrigin.z) / cellSize);

            for (int ring = 0; ring <= maxRing; ring++)
            {
                // Once a hit is closer than this ring's inner edge, no further ring can beat it.
                if (found && (ring - 1) * cellSize > best) break;

                for (int dz = -ring; dz <= ring; dz++)
                {
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        // Only the ring's perimeter — the inside was covered by earlier rings.
                        if (ring > 0 && Mathf.Abs(dx) != ring && Mathf.Abs(dz) != ring) continue;

                        int x = cx + dx;
                        int z = cz + dz;
                        if (x < 0 || z < 0 || x >= cellsX || z >= cellsZ) continue;

                        int cell = z * cellsX + x;
                        for (int i = cellStart[cell]; i < cellStart[cell + 1]; i++)
                        {
                            int linkId = cellItems[i];
                            ref readonly var link = ref links[linkId];

                            int component = nodes[link.To].Component;
                            if (requiredComponent >= 0 && component != requiredComponent) continue;

                            if (!ClosestOnLink(link, point, out float s, out var position, out float distance))
                                continue;
                            if (distance >= best) continue;

                            best = distance;
                            found = true;
                            hit = new Hit(linkId, s, distance, position, component);
                        }
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Closest point on one link's span, measured in XZ.
        ///
        /// Horizontal on purpose: a car on the raised downtown avenue is 6–10 m above a street that
        /// passes under it, and a 3D distance would have it "on" the wrong one. The height comes back
        /// from the baked sample, which is right by construction.
        /// </summary>
        private bool ClosestOnLink(in Link link, Vector3 point, out float s, out Vector3 position, out float distance)
        {
            s = 0f;
            position = default;
            distance = float.MaxValue;

            var edge = network.Edges[link.Edge];
            int segments = edge.Points.Length - 1;
            if (segments < 1 || edge.Length <= 0f) return false;

            float per = edge.Length / segments;
            float lo = Mathf.Min(link.S0, link.S1);
            float hi = Mathf.Max(link.S0, link.S1);

            int first = Mathf.Clamp(Mathf.FloorToInt(lo / per), 0, segments - 1);
            int last = Mathf.Clamp(Mathf.CeilToInt(hi / per) - 1, first, segments - 1);

            for (int i = first; i <= last; i++)
            {
                var a = edge.Points[i];
                var b = edge.Points[i + 1];

                float abx = b.x - a.x;
                float abz = b.z - a.z;
                float lengthSq = abx * abx + abz * abz;
                if (lengthSq < 1e-6f) continue;

                float t = ((point.x - a.x) * abx + (point.z - a.z) * abz) / lengthSq;
                t = Mathf.Clamp01(t);

                float candidate = Mathf.Clamp((i + t) * per, lo, hi);
                var at = network.PointAt(link.Edge, candidate);

                float dx = point.x - at.x;
                float dz = point.z - at.z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d >= distance) continue;

                distance = d;
                s = candidate;
                position = at;
            }

            return distance < float.MaxValue;
        }

        // --- index build --------------------------------------------------------------------------

        /// <summary>
        /// Bins every link into a uniform grid. Called by the builder after <see cref="Fill"/>; it
        /// lives here rather than in the editor pass so the binning and the query that reads it
        /// cannot drift apart.
        /// </summary>
        public void BuildIndex(float cell)
        {
            cellSize = Mathf.Max(4f, cell);

            if (network == null || links.Length == 0)
            {
                cellsX = cellsZ = 0;
                cellStart = System.Array.Empty<int>();
                cellItems = System.Array.Empty<int>();
                return;
            }

            var bounds = new Bounds(network.PointAt(links[0].Edge, links[0].S0), Vector3.zero);
            foreach (var edge in network.Edges)
                foreach (var point in edge.Points)
                    bounds.Encapsulate(point);

            gridOrigin = new Vector3(bounds.min.x - cellSize, 0f, bounds.min.z - cellSize);
            cellsX = Mathf.Max(1, Mathf.CeilToInt((bounds.size.x + cellSize * 2f) / cellSize));
            cellsZ = Mathf.Max(1, Mathf.CeilToInt((bounds.size.z + cellSize * 2f) / cellSize));

            int cells = cellsX * cellsZ;
            var counts = new int[cells];

            // Two passes, so the flat slice arrays are sized exactly: count, then place.
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1)
                {
                    cellStart = new int[cells + 1];
                    int running = 0;
                    for (int i = 0; i < cells; i++)
                    {
                        cellStart[i] = running;
                        running += counts[i];
                        counts[i] = 0;
                    }

                    cellStart[cells] = running;
                    cellItems = new int[running];
                }

                for (int linkId = 0; linkId < links.Length; linkId++)
                {
                    SpanBounds(links[linkId], out var min, out var max);

                    int x0 = Mathf.Clamp(Mathf.FloorToInt((min.x - gridOrigin.x) / cellSize), 0, cellsX - 1);
                    int x1 = Mathf.Clamp(Mathf.FloorToInt((max.x - gridOrigin.x) / cellSize), 0, cellsX - 1);
                    int z0 = Mathf.Clamp(Mathf.FloorToInt((min.z - gridOrigin.z) / cellSize), 0, cellsZ - 1);
                    int z1 = Mathf.Clamp(Mathf.FloorToInt((max.z - gridOrigin.z) / cellSize), 0, cellsZ - 1);

                    for (int z = z0; z <= z1; z++)
                    {
                        for (int x = x0; x <= x1; x++)
                        {
                            int bin = z * cellsX + x;
                            if (pass == 0) counts[bin]++;
                            else cellItems[cellStart[bin] + counts[bin]++] = linkId;
                        }
                    }
                }
            }
        }

        /// <summary>XZ bounds of one link's span, from the edge samples it actually covers.</summary>
        private void SpanBounds(in Link link, out Vector3 min, out Vector3 max)
        {
            var edge = network.Edges[link.Edge];
            int segments = Mathf.Max(1, edge.Points.Length - 1);
            float per = edge.Length / segments;
            float lo = Mathf.Min(link.S0, link.S1);
            float hi = Mathf.Max(link.S0, link.S1);

            int first = Mathf.Clamp(Mathf.FloorToInt(lo / per), 0, segments);
            int last = Mathf.Clamp(Mathf.CeilToInt(hi / per), first, segments);

            var a = network.PointAt(link.Edge, lo);
            var b = network.PointAt(link.Edge, hi);
            min = Vector3.Min(a, b);
            max = Vector3.Max(a, b);

            for (int i = first; i <= last; i++)
            {
                var point = edge.Points[i];
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
            }
        }
    }
}
