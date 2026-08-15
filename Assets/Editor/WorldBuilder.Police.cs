using System.Collections.Generic;
using System.Linq;
using TheBlock.Core;
using TheBlock.Police;
using TheBlock.Traffic;
using UnityEditor;
using UnityEngine;
using Convert = TheBlock.Core.Convert;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// U19's build pass: the routing graph the police drive.
    ///
    /// <b>It exists because the street graph is not connected.</b> Measured on the baked asset before
    /// a line of pursuit code was written: 97 nodes and 142 edges in <b>five</b> components. U17's
    /// traffic could not have noticed — it picks a uniformly random outgoing edge at every junction,
    /// and a random walk never asks whether two streets are reachable from each other. A pursuit asks
    /// that question every second.
    ///
    /// This is the same fault that killed routed pursuit in the web build, and the original's config
    /// says so outright: routing was deleted because the network "is authored district by district
    /// and is not fully connected, so 'drive the roads to the player' was regularly an impossible
    /// instruction and the pursuit silently did nothing". <c>CLAUDE.md</c> rule 5 says re-test a
    /// workaround before inheriting it. Re-tested: the cause is that <see cref="BuildGraph"/> mints
    /// nodes only where polyline ENDPOINTS meet, so a side street that <b>T</b>s into the middle of
    /// another produces no junction at all. Four of the five islands touch one 1,364 m polyline, at
    /// gaps under 3 m.
    ///
    /// So this pass stitches — <b>into a separate asset, never into
    /// <see cref="TrafficNetwork"/></b>. Editing that would change <c>EdgeCount</c>, which decides
    /// <see cref="TrafficNetwork.IsLit"/>, which decides the 70 lit intersections, the 230 crossings
    /// and the 233 poles U16 and U17 verified. Nothing here copies geometry: a
    /// <see cref="RouteGraph.Link"/> is an arc-length span over an edge that already exists.
    ///
    /// The result is REPORTED, with the component sizes on both sides of the stitch, because a graph
    /// that silently loses a district is exactly the failure the web build spent six bugs on.
    /// </summary>
    public static partial class WorldBuilder
    {
        /// <summary>The baked route graph. Swept by this pass and no other.</summary>
        private const string GeneratedPoliceFolder = "Assets/Police/Generated";

        /// <summary>
        /// How close a node must come to the middle of another street to be treated as a junction
        /// with it, metres.
        ///
        /// 3.0 because the measured gaps that matter are 0.65, 1.13, 2.20 and 2.73 m and the next
        /// one up is 4.85 m — so any value in (2.73, 4.85) picks the same four, and 3.0 sits in that
        /// gap without pretending to more precision than the data has. Half a lane is also about the
        /// most that can be called "the same junction" with a straight face.
        /// </summary>
        private const float StitchDistance = 3.0f;

        /// <summary>
        /// A stitch this close to an edge's own end is ignored, metres. The endpoint node is already
        /// there and the graph already joins through it; cutting beside it only makes a stub link
        /// shorter than a car.
        /// </summary>
        private const float EndpointGuard = 1.0f;

        /// <summary>Two cuts on the same edge closer than this are the same junction, metres.</summary>
        private const float CutMerge = 1.5f;

        /// <summary>
        /// Vertical tolerance on a stitch, metres. Downtown's avenue is baked 6–10 m above the
        /// streets it passes over, and joining those would let a cop drive off a viaduct.
        /// </summary>
        private const float StitchHeightBand = 3.0f;

        /// <summary>Nearest-link index cell, metres. The median edge is ~65 m, so a cell holds a few.</summary>
        private const float RouteCellSize = 32f;

        /// <summary>Below this share of total centreline in one component, the pursuit is unreliable.</summary>
        private const float ReachableWarnFraction = 0.95f;

        /// <summary>
        /// The three station bays, in the web build's own frame. Yaw 0 — they face the street.
        ///
        /// These are the parked cruisers AND the pursuers, which is the web's trick: with the pool
        /// parked in the bays there is no second set of decorative cars to hide, and therefore no
        /// hidden car's collider left standing in the forecourt as an invisible wall.
        /// </summary>
        private static readonly Vector3[] StationBays =
        {
            new(-164f, 0f, -111f),
            new(-160f, 0f, -111f),
            new(-156f, 0f, -111f),
        };

        /// <summary>Where a busted player is returned, in the web's frame. Its own custody point.</summary>
        private static readonly Vector3 CustodyPoint = new(-160f, 0.3f, -106f);

        private static void BuildPolice(Transform root, TheBlockConfig.Root config, Options options, Report report)
        {
            if (!options.Police) return;

            var network = AssetDatabase.LoadAssetAtPath<TrafficNetwork>($"{GeneratedTrafficFolder}/TrafficNetwork.asset");
            if (network == null || network.Edges.Length == 0)
            {
                report.Warnings.Add("police: no baked traffic network — route graph skipped");
                return;
            }

            var generated = new HashSet<string>(System.StringComparer.Ordinal);
            var graph = BakeRouteGraph(network, config, generated, report);
            SweepPoliceFolder(generated, report);
            BuildPoliceScene(root, graph, report);
        }

        /// <summary>
        /// Places the <c>Police</c> group and hands it everything that needed converting.
        ///
        /// The bay and custody coordinates pass through <see cref="Convert"/> exactly once, here —
        /// port rule 1. Their height is probed with the same <see cref="GroundY"/> the streets and
        /// the zebras use, rather than taken from the config, because the config has no Y for either
        /// and the station forecourt is a raised slab.
        /// </summary>
        private static void BuildPoliceScene(Transform root, RouteGraph graph, Report report)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PoliceCarBuilder.PrefabPath);
            if (prefab == null)
            {
                report.Warnings.Add(
                    $"police: no cruiser prefab at {PoliceCarBuilder.PrefabPath} — " +
                    "run The Block → Build Police Car, then rebuild");
                return;
            }

            var group = NewGroup("Police", root);
            var system = group.gameObject.AddComponent<PoliceSystem>();
            group.gameObject.AddComponent<Heat>();
            group.gameObject.AddComponent<CrimeWatch>();
            group.gameObject.AddComponent<BustSequence>();

            var bays = new Vector3[StationBays.Length];
            for (int i = 0; i < StationBays.Length; i++)
            {
                var at = Convert.Pos(StationBays[i]);
                bays[i] = new Vector3(at.x, GroundY(at) + BayRideHeight, at.z);
            }

            var custody = Convert.Pos(CustodyPoint);
            custody = new Vector3(custody.x, GroundY(custody) + BayRideHeight, custody.z);

            system.ConfigureStation(bays, Convert.Yaw(0f), custody);

            var serialized = new SerializedObject(system);
            serialized.FindProperty("copPrefab").objectReferenceValue = prefab;
            serialized.FindProperty("routeGraph").objectReferenceValue = graph;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // How far the custody point is from a street decides whether you can drive away from a
            // bust or have to reverse off a kerb first — worth a report line, not a guess.
            string custodyNote = graph != null && graph.TryNearest(custody, 60f, -1, out var hit)
                ? $"custody point {hit.Distance:0.0} m from component {hit.Component}"
                : "custody point is not near any street";

            report.Placed.Add(
                $"Police {bays.Length} bay(s) at the station, pool of cruisers wired, {custodyNote}");
        }

        /// <summary>Clearance above the probed ground when a car is placed, metres.</summary>
        private const float BayRideHeight = 0.15f;

        // --- the route graph ------------------------------------------------------------------------

        /// <summary>One cut in an edge: a junction at arc length <see cref="S"/>.</summary>
        private struct RouteCut
        {
            public float S;
            public int Node;
        }

        private static RouteGraph BakeRouteGraph(
            TrafficNetwork network, TheBlockConfig.Root config, HashSet<string> generated, Report report)
        {
            var edges = network.Edges;

            // Working nodes: the network's own, plus one per true crossing found below.
            var positions = new List<Vector3>(network.Nodes.Length + 16);
            foreach (var node in network.Nodes) positions.Add(node.Position);

            var cuts = new List<RouteCut>[edges.Length];
            for (int i = 0; i < cuts.Length; i++) cuts[i] = new List<RouteCut>();

            int tJunctions = StitchTJunctions(network, cuts);
            int crossings = StitchCrossings(network, positions, cuts);

            var (nodes, links, linkOwners) = ChainLinks(network, positions, cuts);
            int components = LabelComponents(nodes, links, linkOwners, out var componentLength);

            var asset = SaveRouteGraph(network, nodes, links, components, componentLength, generated, report);
            ReportRouteGraph(network, config, asset, tJunctions, crossings, report);
            return asset;
        }

        /// <summary>
        /// Pass A — a node sitting in the middle of another street is a junction with it.
        ///
        /// This is the one that matters: it is where the four islands are joined.
        /// </summary>
        private static int StitchTJunctions(TrafficNetwork network, List<RouteCut>[] cuts)
        {
            int stitched = 0;
            var edges = network.Edges;

            for (int nodeId = 0; nodeId < network.Nodes.Length; nodeId++)
            {
                var point = network.Nodes[nodeId].Position;

                for (int edgeId = 0; edgeId < edges.Length; edgeId++)
                {
                    var edge = edges[edgeId];
                    if (edge.A == nodeId || edge.B == nodeId) continue;

                    // Cheap reject on the edge's own bounding sphere, which the bake already stores.
                    if ((point - edge.Centre).sqrMagnitude > (edge.Radius + StitchDistance) * (edge.Radius + StitchDistance))
                        continue;

                    if (!ClosestOnEdge(network, edgeId, point, out float s, out var at, out float distance)) continue;
                    if (distance > StitchDistance) continue;
                    if (Mathf.Abs(at.y - point.y) > StitchHeightBand) continue;
                    if (s < EndpointGuard || s > edge.Length - EndpointGuard) continue;
                    if (HasCutNear(cuts[edgeId], s)) continue;

                    cuts[edgeId].Add(new RouteCut { S = s, Node = nodeId });
                    stitched++;
                }
            }

            return stitched;
        }

        /// <summary>
        /// Pass B — two streets whose polylines genuinely cross with no shared node.
        ///
        /// Rarer than the T case and worth less, but a crossroads that is not a node is a place the
        /// planner would route the long way round for no reason a player could see.
        /// </summary>
        private static int StitchCrossings(TrafficNetwork network, List<Vector3> positions, List<RouteCut>[] cuts)
        {
            int stitched = 0;
            var edges = network.Edges;

            for (int a = 0; a < edges.Length; a++)
            {
                var ea = edges[a];
                for (int b = a + 1; b < edges.Length; b++)
                {
                    var eb = edges[b];
                    if (ea.A == eb.A || ea.A == eb.B || ea.B == eb.A || ea.B == eb.B) continue;

                    float reach = ea.Radius + eb.Radius;
                    if ((ea.Centre - eb.Centre).sqrMagnitude > reach * reach) continue;

                    if (!SegmentsCross(ea, eb, out float sa, out float sb, out var at)) continue;
                    if (sa < EndpointGuard || sa > ea.Length - EndpointGuard) continue;
                    if (sb < EndpointGuard || sb > eb.Length - EndpointGuard) continue;

                    var pa = network.PointAt(a, sa);
                    var pb = network.PointAt(b, sb);
                    if (Mathf.Abs(pa.y - pb.y) > StitchHeightBand) continue;

                    if (HasCutNear(cuts[a], sa) || HasCutNear(cuts[b], sb)) continue;

                    int node = positions.Count;
                    positions.Add(new Vector3(at.x, (pa.y + pb.y) * 0.5f, at.z));
                    cuts[a].Add(new RouteCut { S = sa, Node = node });
                    cuts[b].Add(new RouteCut { S = sb, Node = node });
                    stitched++;
                }
            }

            return stitched;
        }

        /// <summary>
        /// Turns the cuts into directed links: every edge becomes a chain of spans, each traversable
        /// both ways.
        /// </summary>
        private static (RouteGraph.Node[] Nodes, RouteGraph.Link[] Links, int[] Owners) ChainLinks(
            TrafficNetwork network, List<Vector3> positions, List<RouteCut>[] cuts)
        {
            var built = new List<(int From, RouteGraph.Link Link)>(network.Edges.Length * 2);

            for (int edgeId = 0; edgeId < network.Edges.Length; edgeId++)
            {
                var edge = network.Edges[edgeId];
                var sorted = new List<RouteCut>(cuts[edgeId].Count + 2) { new RouteCut { S = 0f, Node = edge.A } };
                sorted.AddRange(cuts[edgeId]);
                sorted.Add(new RouteCut { S = edge.Length, Node = edge.B });
                sorted.Sort((x, y) => x.S.CompareTo(y.S));

                // Drop a cut that landed on top of its predecessor rather than dropping the LINK it
                // would have made: skipping the link instead breaks the chain, and the node before
                // the gap silently loses its way onto the rest of the street.
                var chain = new List<RouteCut>(sorted.Count);
                foreach (var cut in sorted)
                {
                    bool last = cut.S >= edge.Length - 1e-3f;
                    if (chain.Count > 0 && !last && cut.S - chain[^1].S < CutMerge) continue;
                    chain.Add(cut);
                }

                // The far endpoint must be the last link's destination whatever the merge did.
                if (chain.Count > 1 && chain[^1].Node != edge.B) chain[^1] = new RouteCut { S = edge.Length, Node = edge.B };

                for (int i = 0; i < chain.Count - 1; i++)
                {
                    var from = chain[i];
                    var to = chain[i + 1];
                    float cost = to.S - from.S;

                    built.Add((from.Node, new RouteGraph.Link
                    {
                        Edge = edgeId, S0 = from.S, S1 = to.S,
                        From = from.Node, To = to.Node, Cost = cost,
                    }));
                    built.Add((to.Node, new RouteGraph.Link
                    {
                        Edge = edgeId, S0 = to.S, S1 = from.S,
                        From = to.Node, To = from.Node, Cost = cost,
                    }));
                }
            }

            // Links are sorted by origin so a node's outgoing set is a slice, with no indirection
            // array to keep in step.
            built.Sort((x, y) => x.From.CompareTo(y.From));

            var nodes = new RouteGraph.Node[positions.Count];
            var links = new RouteGraph.Link[built.Count];
            var owners = new int[built.Count];

            for (int i = 0; i < built.Count; i++)
            {
                links[i] = built[i].Link;
                owners[i] = built[i].From;
            }

            for (int i = 0; i < nodes.Length; i++)
                nodes[i] = new RouteGraph.Node { Position = positions[i], LinkStart = 0, LinkCount = 0 };

            for (int i = 0; i < owners.Length; i++)
            {
                int owner = owners[i];
                if (nodes[owner].LinkCount == 0) nodes[owner].LinkStart = i;
                nodes[owner].LinkCount++;
            }

            // Twins, after the sort has separated the pairs. A cop that finds itself on a span needs
            // the direction it is actually FACING, and searching for that at runtime would be a scan
            // of the whole link array once per replan.
            for (int i = 0; i < links.Length; i++)
            {
                links[i].Twin = -1;
                for (int j = 0; j < links.Length; j++)
                {
                    if (j == i || links[j].Edge != links[i].Edge) continue;
                    if (links[j].From != links[i].To || links[j].To != links[i].From) continue;
                    if (Mathf.Abs(links[j].S0 - links[i].S1) > 1e-3f) continue;

                    links[i].Twin = j;
                    break;
                }
            }

            return (nodes, links, owners);
        }

        /// <summary>
        /// Union-find over the stitched graph, components renumbered largest-first by centreline
        /// metres — so component 0 is always the city and the orphans sort after it.
        /// </summary>
        private static int LabelComponents(
            RouteGraph.Node[] nodes, RouteGraph.Link[] links, int[] owners, out float[] componentLength)
        {
            var parent = new int[nodes.Length];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }

                return x;
            }

            for (int i = 0; i < links.Length; i++)
            {
                int a = Find(owners[i]);
                int b = Find(links[i].To);
                if (a != b) parent[a] = b;
            }

            // Metres per root. Each span is stored twice (both directions), so halve it.
            var metres = new Dictionary<int, float>();
            for (int i = 0; i < links.Length; i++)
            {
                int root = Find(owners[i]);
                metres.TryGetValue(root, out float running);
                metres[root] = running + links[i].Cost * 0.5f;
            }

            foreach (var root in Enumerable.Range(0, nodes.Length).Select(Find).Distinct())
                if (!metres.ContainsKey(root)) metres[root] = 0f;

            var order = metres.OrderByDescending(pair => pair.Value).Select(pair => pair.Key).ToList();
            var index = new Dictionary<int, int>();
            for (int i = 0; i < order.Count; i++) index[order[i]] = i;

            for (int i = 0; i < nodes.Length; i++) nodes[i].Component = index[Find(i)];

            componentLength = order.Select(root => metres[root]).ToArray();
            return order.Count;
        }

        private static RouteGraph SaveRouteGraph(
            TrafficNetwork network, RouteGraph.Node[] nodes, RouteGraph.Link[] links,
            int components, float[] componentLength, HashSet<string> generated, Report report)
        {
            EnsureFolder(GeneratedPoliceFolder);
            var path = $"{GeneratedPoliceFolder}/RouteGraph.asset";
            generated.Add(path);
            report.Generated.Add(path);

            var asset = AssetDatabase.LoadAssetAtPath<RouteGraph>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<RouteGraph>();
                AssetDatabase.CreateAsset(asset, path);
            }

            asset.Fill(network, nodes, links, components, componentLength);
            asset.BuildIndex(RouteCellSize);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        /// <summary>
        /// The line that has to be read, not skimmed: what the graph looked like before the stitch,
        /// after it, and whether the police station is inside the part a cop can actually drive.
        /// </summary>
        private static void ReportRouteGraph(
            TrafficNetwork network, TheBlockConfig.Root config, RouteGraph graph,
            int tJunctions, int crossings, Report report)
        {
            var before = RawComponents(network);
            float total = network.TotalLength;
            float largest = graph.ComponentLength.Length > 0 ? graph.ComponentLength[0] : 0f;
            float share = total > 0f ? largest / total : 0f;

            string Sizes(IEnumerable<float> lengths) => string.Join(", ", lengths.Select(metres => $"{metres:0}m"));

            report.Notes.Add(
                $"route graph: {network.Nodes.Length} node(s) / {network.Edges.Length} edge(s), " +
                $"{before.Count} component(s) [{Sizes(before)}] before stitching");
            report.Notes.Add(
                $"route graph: {tJunctions} T-junction(s) + {crossings} crossing(s) stitched at <= {StitchDistance:0.#} m " +
                $"-> {graph.ComponentCount} component(s) [{Sizes(graph.ComponentLength)}], " +
                $"largest holds {share:P1} of {total:0} m");

            var station = config?.PoliceStation;
            if (station == null) return;

            var at = Convert.Pos(station.Position.Raw);
            if (!graph.TryNearest(at, 200f, -1, out var hit))
            {
                report.Warnings.Add("route graph: the police station is not near any street — cops cannot deploy");
                return;
            }

            report.Notes.Add(
                $"route graph: police station {hit.Distance:0.0} m from component {hit.Component}");

            if (hit.Component != 0)
                report.Warnings.Add(
                    $"route graph: the police station is in component {hit.Component}, not the largest — " +
                    "deploying cops will strand them");
            else if (share < ReachableWarnFraction)
                report.Warnings.Add(
                    $"route graph: only {share:P1} of the network is reachable from the police station " +
                    $"({ReachableWarnFraction:P0} expected) — check the stitch distance");
        }

        /// <summary>Component sizes of the UNSTITCHED network, in metres, largest first.</summary>
        private static List<float> RawComponents(TrafficNetwork network)
        {
            var parent = new int[network.Nodes.Length];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }

                return x;
            }

            foreach (var edge in network.Edges)
            {
                int a = Find(edge.A);
                int b = Find(edge.B);
                if (a != b) parent[a] = b;
            }

            var metres = new Dictionary<int, float>();
            foreach (var edge in network.Edges)
            {
                int root = Find(edge.A);
                metres.TryGetValue(root, out float running);
                metres[root] = running + edge.Length;
            }

            return metres.Values.OrderByDescending(value => value).ToList();
        }

        // --- geometry ------------------------------------------------------------------------------

        /// <summary>Closest point on a whole edge, measured in XZ. Arc length comes back in the edge's frame.</summary>
        private static bool ClosestOnEdge(
            TrafficNetwork network, int edgeId, Vector3 point, out float s, out Vector3 at, out float distance)
        {
            s = 0f;
            at = default;
            distance = float.MaxValue;

            var edge = network.Edges[edgeId];
            int segments = edge.Points.Length - 1;
            if (segments < 1 || edge.Length <= 0f) return false;

            float per = edge.Length / segments;

            for (int i = 0; i < segments; i++)
            {
                var a = edge.Points[i];
                var b = edge.Points[i + 1];

                float abx = b.x - a.x;
                float abz = b.z - a.z;
                float lengthSq = abx * abx + abz * abz;
                if (lengthSq < 1e-6f) continue;

                float t = Mathf.Clamp01(((point.x - a.x) * abx + (point.z - a.z) * abz) / lengthSq);
                float candidate = (i + t) * per;
                var sample = network.PointAt(edgeId, candidate);

                float dx = point.x - sample.x;
                float dz = point.z - sample.z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d >= distance) continue;

                distance = d;
                s = candidate;
                at = sample;
            }

            return distance < float.MaxValue;
        }

        /// <summary>
        /// First true XZ intersection between two edges' polylines, with the arc length on each.
        /// </summary>
        private static bool SegmentsCross(
            TrafficNetwork.Edge a, TrafficNetwork.Edge b, out float sa, out float sb, out Vector3 at)
        {
            sa = sb = 0f;
            at = default;

            int segmentsA = a.Points.Length - 1;
            int segmentsB = b.Points.Length - 1;
            if (segmentsA < 1 || segmentsB < 1) return false;

            float perA = a.Length / segmentsA;
            float perB = b.Length / segmentsB;

            for (int i = 0; i < segmentsA; i++)
            {
                var p1 = a.Points[i];
                var p2 = a.Points[i + 1];

                for (int j = 0; j < segmentsB; j++)
                {
                    var p3 = b.Points[j];
                    var p4 = b.Points[j + 1];

                    float d1x = p2.x - p1.x, d1z = p2.z - p1.z;
                    float d2x = p4.x - p3.x, d2z = p4.z - p3.z;

                    float denominator = d1x * d2z - d1z * d2x;
                    if (Mathf.Abs(denominator) < 1e-6f) continue;

                    float t = ((p3.x - p1.x) * d2z - (p3.z - p1.z) * d2x) / denominator;
                    float u = ((p3.x - p1.x) * d1z - (p3.z - p1.z) * d1x) / denominator;
                    if (t < 0f || t > 1f || u < 0f || u > 1f) continue;

                    sa = (i + t) * perA;
                    sb = (j + u) * perB;
                    at = new Vector3(p1.x + d1x * t, p1.y, p1.z + d1z * t);
                    return true;
                }
            }

            return false;
        }

        private static bool HasCutNear(List<RouteCut> cuts, float s)
        {
            foreach (var cut in cuts)
                if (Mathf.Abs(cut.S - s) < CutMerge) return true;
            return false;
        }

        // --- housekeeping ---------------------------------------------------------------------------

        /// <summary>Deletes anything in <see cref="GeneratedPoliceFolder"/> this build did not write.</summary>
        private static void SweepPoliceFolder(HashSet<string> generated, Report report)
        {
            if (!AssetDatabase.IsValidFolder(GeneratedPoliceFolder)) return;

            foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { GeneratedPoliceFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path) || generated.Contains(path)) continue;

                AssetDatabase.DeleteAsset(path);
                report.Notes.Add($"deleted stale police asset {path}");
            }
        }
    }
}
