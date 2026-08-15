using System.Collections.Generic;
using TheBlock.Core;
using TheBlock.Npc;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Convert = TheBlock.Core.Convert;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// The pedestrian world — U16. Where a person may walk, and the only places they may cross.
    ///
    /// THE WEB BUILD'S VERSION, AND WHY NONE OF IT SURVIVES. `walkable.ts` renders the districts
    /// from directly above into a 4096² offscreen mask, keeping only surfaces whose MATERIAL is
    /// named like a pavement (`CityGenside_walks`, `Concrete_Flooring_*`), reads that back on the
    /// GPU — 67 MB at peak, plus a boolean grid held for the whole session — and then rejection-
    /// samples points out of it. A pedestrian picks a point within 8 m and walks to it IN A
    /// STRAIGHT LINE. There is no path, no corridor, no notion that a road is a thing. When that
    /// proved not to be enough, forty rectangles and forty strips were recorded BY HAND into
    /// `npc.config.ts` to pin people down — and a strip is a straight pingpong line beside the
    /// pavement rather than on it. That is the real reason people never looked like they were
    /// walking on the pavement: for the most part they were not on one, they were on a line
    /// somebody typed in next to one.
    ///
    /// Unity's answer is not a better sampler, it is a different question. The NavMesh is baked
    /// from the districts' real geometry, and an agent moves inside a corridor of its triangles —
    /// off it is not "unlikely", it is unrepresentable. So:
    ///
    ///  1. Every carriageway in `config.traffic.network` — 12.7 km of it — is carved
    ///     <c>Not Walkable</c> end to end. Not "expensive": absent. A pedestrian cannot be in the
    ///     road, and no per-frame check enforces that, the bake does.
    ///  2. Which leaves the two pavements of a street disconnected, so the ONLY way across is a
    ///     <see cref="NavMeshLink"/> at a zebra crossing, one per approach of every lit
    ///     intersection — the same 233 crossings `traffic.ts` derives, from the same graph and the
    ///     same setbacks. A pedestrian crosses at a zebra because there is nowhere else to cross,
    ///     which is a stronger statement than the web build's dedicated crossers could make: there,
    ///     two people were assigned to walk each zebra forever and everyone else ignored roads.
    ///  3. The link is gated (<see cref="Crossing.MayCross"/>), so stepping off the kerb is a
    ///     decision rather than a certainty. U16 answers the gate with "nothing is driving over it";
    ///     U17 hands it the traffic light and deletes that fallback.
    ///
    /// Kerb heights make no difference to any of this, which is the point — with the carriageway
    /// carved there is no surface to step down onto, so the default agent's 0.75 m climb cannot
    /// quietly reconnect the two sides the way it would if the road were merely costly.
    /// </summary>
    public static partial class WorldBuilder
    {
        /// <summary>Baked NavMesh data. Swept and rewritten every build like every other generated asset.</summary>
        private const string GeneratedNavigationFolder = "Assets/Navigation/Generated";

        /// <summary>Metres of carriageway along the direction of travel that the zebra covers.</summary>
        private const float ZebraLength = 4f;

        /// <summary>
        /// How far past the kerb a link's endpoint is planted. The kerb itself is exactly where the
        /// carve stops, and a link endpoint landing on that boundary is a coin toss between the
        /// pavement's NavMesh and nothing at all — so it goes a metre into the pavement instead.
        /// </summary>
        private const float LinkKerbInset = 1.2f;

        /// <summary>Vertical extent of a carve box: enough to cover any street, not enough to reach a roof.</summary>
        private const float CarveHeight = 5f;

        /// <summary>
        /// Voxel size for the bake, overriding the agent-derived default of radius/3 ≈ 0.17 m.
        ///
        /// The districts span roughly 950 × 800 m. At the default that is 5,700 × 4,800 columns and
        /// the bake stops being something you re-run casually; 0.25 m is a quarter of the cost and
        /// still four times finer than the narrowest pavement in the world.
        /// </summary>
        private const float NavVoxelSize = 0.25f;

        /// <summary>Square metres below which a baked island is dropped — awnings, ledges, kerb tops.</summary>
        private const float NavMinRegionArea = 4f;

        /// <summary>Height above the street the zebra is painted, matching the road ribbon's own lift.</summary>
        private const float ZebraLift = 0.03f;

        // --- graph -------------------------------------------------------------------------------

        /// <summary>An intersection: where two or more authored streets share an endpoint.</summary>
        private sealed class NavNode
        {
            public int Id;
            public Vector3 Position;
            public readonly List<int> Edges = new();
        }

        /// <summary>One authored street, already in Unity space.</summary>
        private sealed class NavEdge
        {
            public int Id;
            public int A;
            public int B;
            public List<Vector3> Points;
            public float Length;
            public float HalfWidth;
        }

        private static void BuildNavigation(
            Transform root, Transform districts, TheBlockConfig.Root config, Options options, Report report)
        {
            if (districts == null)
            {
                report.Warnings.Add("navigation skipped — districts were not built, so there is nothing to bake");
                return;
            }

            var traffic = config.Traffic;
            if (traffic?.Network == null || traffic.Network.Count == 0)
            {
                report.Warnings.Add("navigation skipped — config has no `traffic.network`");
                return;
            }

            // Colliders were added by the passes above through the editor API, which does not push
            // them into the physics scene until something asks. The Y-baking raycasts below are that
            // something, and without this they all miss and every zebra lands at y 0.
            Physics.SyncTransforms();

            var (nodes, edges) = BuildGraph(traffic, report);
            if (edges.Count == 0)
            {
                report.Warnings.Add("navigation skipped — the traffic network produced no usable streets");
                return;
            }

            var carveGroup = NewGroup("Navigation", districts);
            int carved = CarveCarriageways(carveGroup, edges, report);

            var crossingGroup = NewGroup("Crossings", root);
            int crossings = BuildCrossings(crossingGroup, nodes, edges, traffic, report);

            BakeNavMesh(districts, report);

            report.Placed.Add(
                $"Navigation {edges.Count} street(s) carved into {carved} volume(s), " +
                $"{crossings} zebra crossing(s), NavMesh baked over the districts");
        }

        /// <summary>
        /// The traffic graph, ported from <c>traffic-graph.ts</c>: one edge per authored polyline,
        /// endpoints within <c>snapDist</c> merged into a shared node.
        ///
        /// Positions go through <see cref="Convert"/> here and nowhere downstream, so every
        /// perpendicular, tangent and box rotation below is computed in Unity space against Unity
        /// geometry. Deriving a kerb offset in config space and converting the result afterwards
        /// would mirror the crossing about the street it belongs to — which reads as "the zebra is
        /// slightly off" rather than as a handedness fault, and is exactly the trap port rule 1 is
        /// about.
        /// </summary>
        private static (List<NavNode>, List<NavEdge>) BuildGraph(TheBlockConfig.TrafficSpec traffic, Report report)
        {
            var nodes = new List<NavNode>();
            var edges = new List<NavEdge>();
            float snapSqr = traffic.SnapDist * traffic.SnapDist;

            int NodeFor(Vector3 p)
            {
                foreach (var node in nodes)
                {
                    var d = node.Position - p;
                    if (d.x * d.x + d.z * d.z <= snapSqr) return node.Id;
                }

                nodes.Add(new NavNode { Id = nodes.Count, Position = p });
                return nodes.Count - 1;
            }

            foreach (var street in traffic.Network)
            {
                if (street?.Pts == null || street.Pts.Count < 2) continue;

                var points = new List<Vector3>(street.Pts.Count);
                foreach (var p in street.Pts) points.Add(Convert.Pos(p.X, 0f, p.Z));

                int a = NodeFor(points[0]);
                int b = NodeFor(points[^1]);

                // Snap the endpoints onto the node positions, so streets meeting at an intersection
                // meet exactly and a crossing measured from the node is measured from the same spot
                // whichever street it belongs to.
                points[0] = nodes[a].Position;
                points[^1] = nodes[b].Position;

                float length = 0f;
                for (int i = 0; i < points.Count - 1; i++) length += Horizontal(points[i + 1] - points[i]).magnitude;
                if (length < 1f) continue;

                var edge = new NavEdge
                {
                    Id = edges.Count,
                    A = a,
                    B = b,
                    Points = points,
                    Length = length,
                    HalfWidth = HalfWidth(street, traffic),
                };

                edges.Add(edge);
                nodes[a].Edges.Add(edge.Id);
                if (b != a) nodes[b].Edges.Add(edge.Id);
            }

            report.Notes.Add($"traffic graph: {nodes.Count} node(s), {edges.Count} street(s)");
            return (nodes, edges);
        }

        /// <summary>
        /// Centreline to kerb. <c>lights.sideOffset</c> is the ordinary street's half-width; a
        /// divided road pushes its kerb out by one lane gap per extra lane, so the one 3-lane road
        /// in the network is carved 10.9 m wide rather than 4.5 and its zebra spans the same.
        /// </summary>
        private static float HalfWidth(TheBlockConfig.StreetSpec street, TheBlockConfig.TrafficSpec traffic)
        {
            float baseHalf = traffic.Lights?.SideOffset ?? 4.5f;
            int extraLanes = Mathf.Max(0, street.Lanes - 1);
            return baseHalf + extraLanes * traffic.LaneGap;
        }

        // --- carving -----------------------------------------------------------------------------

        /// <summary>
        /// One <c>Not Walkable</c> box per straight run of every street.
        ///
        /// Per SEGMENT rather than per street, because a box is an oriented rectangle and a street
        /// with a bend in it is not one — 142 streets come to 172 segments, which is few enough that
        /// the bake does not care and exact enough that a corner does not carve away the pavement
        /// outside it.
        ///
        /// The boxes overlap at every intersection, deliberately: they run the full length of their
        /// segment including the node at each end, so the middle of a junction — which belongs to no
        /// approach in particular — comes out carved rather than left as an island of road NavMesh
        /// people could stand on.
        /// </summary>
        private static int CarveCarriageways(Transform parent, List<NavEdge> edges, Report report)
        {
            int built = 0;

            foreach (var edge in edges)
            {
                for (int i = 0; i < edge.Points.Count - 1; i++)
                {
                    var from = edge.Points[i];
                    var to = edge.Points[i + 1];
                    var along = Horizontal(to - from);
                    float length = along.magnitude;
                    if (length < 0.5f) continue;

                    var centre = (from + to) * 0.5f;
                    centre.y = GroundY(centre);

                    var volume = new GameObject($"Carve_{edge.Id:000}_{i:00}");
                    volume.transform.SetParent(parent, worldPositionStays: false);
                    volume.transform.SetPositionAndRotation(
                        centre, Quaternion.LookRotation(along / length, Vector3.up));

                    var modifier = volume.AddComponent<NavMeshModifierVolume>();
                    modifier.size = new Vector3(edge.HalfWidth * 2f, CarveHeight, length);
                    // Centred a little above the street: the box has to swallow the road surface and
                    // the kerb beside it, and nothing at first-floor height.
                    modifier.center = new Vector3(0f, CarveHeight * 0.5f - 1f, 0f);
                    modifier.area = 1; // Not Walkable — Unity's built-in area 1, fixed by the engine
                    built++;
                }
            }

            report.Notes.Add($"carriageways: {built} Not Walkable volume(s) over {edges.Count} street(s)");
            return built;
        }

        // --- crossings ---------------------------------------------------------------------------

        /// <summary>
        /// One zebra per approach of every LIT intersection, matching <c>traffic.ts</c> exactly: a
        /// node is lit when three or more streets meet at it, and the crossing sits
        /// <c>stopLineDist + crossingSetback</c> back from the node so it clears where U17's poles
        /// and stop lines will go.
        /// </summary>
        private static int BuildCrossings(
            Transform parent, List<NavNode> nodes, List<NavEdge> edges,
            TheBlockConfig.TrafficSpec traffic, Report report)
        {
            float fromNode = traffic.StopLineDist + traffic.CrossingSetback;
            var material = CreateZebraMaterial(report);
            var quad = CreateZebraMesh(report);

            int built = 0, tooShort = 0, lit = 0;

            foreach (var node in nodes)
            {
                if (node.Edges.Count < 3) continue; // not lit — same rule as traffic-lights.ts
                lit++;

                foreach (int edgeId in node.Edges)
                {
                    var edge = edges[edgeId];

                    // A street shorter than two setbacks would put the two ends' crossings on top of
                    // each other. The web build drops those too.
                    if (edge.Length < fromNode * 2f) { tooShort++; continue; }

                    // Walk into the street FROM this node, so the tangent points away from the
                    // junction and the perpendicular is the direction pedestrians travel.
                    bool fromStart = edge.A == node.Id;
                    var centre = PointAlong(edge.Points, fromNode, fromStart, out var tangent);
                    var across = Vector3.Cross(Vector3.up, tangent).normalized;

                    centre.y = GroundY(centre);

                    var crossing = new GameObject($"Crossing_{node.Id:000}_{edgeId:000}");
                    crossing.transform.SetParent(parent, worldPositionStays: false);
                    crossing.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(tangent, Vector3.up));

                    var kerbA = centre + across * edge.HalfWidth;
                    var kerbB = centre - across * edge.HalfWidth;
                    crossing.AddComponent<Crossing>().Configure(kerbA, kerbB, node.Id, edgeId);

                    BuildZebraVisual(crossing.transform, edge.HalfWidth, quad, material);
                    BuildCrossingLink(crossing.transform, edge.HalfWidth);

                    built++;
                }
            }

            report.Notes.Add(
                $"crossings: {built} on {lit} lit intersection(s)" +
                (tooShort > 0 ? $", {tooShort} approach(es) dropped — street shorter than {fromNode * 2f:0} m" : ""));
            return built;
        }

        /// <summary>
        /// The link that IS the crossing, as far as pathfinding is concerned.
        ///
        /// Its endpoints sit past the kerb on both sides so they land on pavement NavMesh rather
        /// than on the boundary of the carve, and its width is the zebra's own so an agent traverses
        /// somewhere along the painted band instead of down a single line.
        ///
        /// <c>costModifier</c> is left at -1 (no override) on purpose: a crossing should not be
        /// discouraged, it is the only way over, and inflating its cost would only make a pedestrian
        /// walk further along the same pavement before using the same zebra anyway.
        /// </summary>
        private static void BuildCrossingLink(Transform crossing, float halfWidth)
        {
            var link = crossing.gameObject.AddComponent<NavMeshLink>();
            float reach = halfWidth + LinkKerbInset;

            // Local space: the crossing's +Z is the street's direction of travel, so the walk across
            // it is ±X. Local endpoints keep the link correct if the object is ever nudged.
            link.startPoint = new Vector3(reach, 0f, 0f);
            link.endPoint = new Vector3(-reach, 0f, 0f);
            link.width = ZebraLength;
            link.bidirectional = true;
            link.area = 0; // Walkable
        }

        private static void BuildZebraVisual(Transform crossing, float halfWidth, Mesh quad, Material material)
        {
            if (quad == null || material == null) return;

            var paint = new GameObject("Zebra");
            paint.transform.SetParent(crossing, worldPositionStays: false);
            paint.transform.localPosition = new Vector3(0f, ZebraLift, 0f);
            // The shared quad is 1 x 1 in XZ with +Z along the street, so scaling puts the stripes
            // across the carriageway at whatever width this street happens to be.
            paint.transform.localScale = new Vector3(halfWidth * 2f, 1f, ZebraLength);

            paint.AddComponent<MeshFilter>().sharedMesh = quad;
            var renderer = paint.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // flat on the road
        }

        /// <summary>A 1 x 1 quad in XZ, facing up. Shared by every zebra and scaled per street.</summary>
        private static Mesh CreateZebraMesh(Report report)
        {
            var mesh = new Mesh { name = "Zebra" };
            mesh.SetVertices(new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f),
            });
            mesh.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            // U runs across the carriageway, so the stripe pattern is a function of U alone and
            // stays put whatever the street's width — the same trick as the road ribbon's texture.
            mesh.SetUVs(0, new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f),
            });
            mesh.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0);
            mesh.RecalculateBounds();

            SaveGeneratedMesh(mesh, $"{GeneratedMeshFolder}/Zebra.asset", report);
            return mesh;
        }

        /// <summary>
        /// The zebra's paint, generated for the same reason the road's is: the pattern has to hold
        /// its metre pitch across a 9 m street and a 21.8 m one, which a fixed image cannot do.
        /// </summary>
        private static Material CreateZebraMaterial(Report report)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                report.Warnings.Add("zebra material skipped — URP/Lit shader not found");
                return null;
            }

            EnsureFolder(GeneratedWorldFolder);

            const int across = 256;
            var texture = new Texture2D(across, 4, TextureFormat.RGBA32, mipChain: true, linear: false)
            {
                name = "Zebra",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 8,
            };

            // Nine bars over a 9 m street: 0.5 m of paint, 0.5 m of gap. Expressed as a fraction of
            // the width rather than in metres, so the widest road gets wider bars rather than more
            // of them — which is what a real crossing does.
            var pixels = new Color[across * 4];
            for (int x = 0; x < across; x++)
            {
                float u = (x + 0.5f) / across;
                bool bar = Mathf.Repeat(u * 9f, 1f) < 0.55f;
                var color = bar ? new Color(0.88f, 0.88f, 0.85f) : new Color(0f, 0f, 0f, 0f);
                for (int y = 0; y < 4; y++) pixels[y * across + x] = color;
            }

            texture.SetPixels(pixels);
            texture.Apply();

            var texturePath = $"{GeneratedWorldFolder}/Zebra.asset";
            AssetDatabase.CreateAsset(texture, texturePath);
            report.Generated.Add(texturePath);

            var material = new Material(shader) { name = "Zebra" };
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Smoothness", 0.2f);
            // The gaps between bars are transparent, so the asphalt underneath shows through rather
            // than the crossing being a grey slab with white on it. Alpha CLIP, not blend: the paint
            // is opaque or absent, and clipping keeps it sorted with the road it lies on.
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.5f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;

            var materialPath = $"{GeneratedWorldFolder}/Zebra.mat";
            AssetDatabase.CreateAsset(material, materialPath);
            report.Generated.Add(materialPath);
            return material;
        }

        // --- baking ------------------------------------------------------------------------------

        /// <summary>
        /// Bakes the districts, and only the districts.
        ///
        /// <c>CollectObjects.Children</c> on the Districts group rather than a world volume, because
        /// the alternative bakes the 1400 m ground plate — an unbroken, perfectly walkable sheet
        /// under the entire map, on which a pedestrian could stroll from downtown to the sea. The
        /// districts carry every pavement in the game, so restricting the bake to them is also what
        /// makes the carve meaningful: outside a district there is no NavMesh to carve.
        ///
        /// It also means the carve volumes have to live UNDER the Districts group, since that
        /// collection mode only gathers modifiers from the surface's own hierarchy.
        ///
        /// Geometry comes from PHYSICS COLLIDERS, not render meshes. They are the same meshes here —
        /// U11 gave every district a non-convex MeshCollider — except where they deliberately are
        /// not: foliage carries no collider, so a tree canopy does not become a first floor.
        /// </summary>
        private static void BakeNavMesh(Transform districts, Report report)
        {
            if (!districts.TryGetComponent<NavMeshSurface>(out var surface))
                surface = districts.gameObject.AddComponent<NavMeshSurface>();

            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~0;
            surface.defaultArea = 0; // Walkable
            surface.ignoreNavMeshAgent = true;
            surface.overrideVoxelSize = true;
            surface.voxelSize = NavVoxelSize;
            surface.minRegionArea = NavMinRegionArea;

            surface.BuildNavMesh();

            if (surface.navMeshData == null)
            {
                report.Warnings.Add("NavMesh bake produced no data — nothing walkable was collected");
                return;
            }

            // BuildNavMesh leaves the data in memory only. Without an asset behind it the NavMesh is
            // gone the moment the scene is closed, and Play mode then runs with agents that cannot
            // path anywhere — which looks like the crowd being broken rather than the bake missing.
            EnsureFolder(GeneratedNavigationFolder);
            var path = $"{GeneratedNavigationFolder}/NavMesh.asset";
            AssetDatabase.CreateAsset(surface.navMeshData, path);
            report.Generated.Add(path);

            var bounds = surface.navMeshData.sourceBounds;
            report.Notes.Add(
                $"NavMesh baked over {bounds.size.x:0} x {bounds.size.z:0} m at {NavVoxelSize:0.00} m voxels → {path}");
        }

        // --- geometry helpers ---------------------------------------------------------------------

        /// <summary>A point <paramref name="distance"/> along the polyline from one end, with the tangent there.</summary>
        private static Vector3 PointAlong(List<Vector3> points, float distance, bool fromStart, out Vector3 tangent)
        {
            int count = points.Count;
            float remaining = distance;

            for (int step = 0; step < count - 1; step++)
            {
                int i = fromStart ? step : count - 1 - step;
                int j = fromStart ? step + 1 : count - 2 - step;

                var from = points[i];
                var to = points[j];
                var along = Horizontal(to - from);
                float length = along.magnitude;
                if (length < 1e-4f) continue;

                tangent = along / length;
                if (remaining <= length) return from + along * (remaining / length);
                remaining -= length;
            }

            // Past the far end: clamp to it. Callers have already rejected streets too short to
            // carry a crossing, so this is the degenerate-polyline case rather than a real one.
            tangent = Horizontal(points[^1] - points[0]).normalized;
            if (tangent.sqrMagnitude < 1e-6f) tangent = Vector3.forward;
            return fromStart ? points[^1] : points[0];
        }

        private static Vector3 Horizontal(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>
        /// The street surface under a point, by raycast — the port of the web build's <c>bakeY</c>.
        ///
        /// LOWEST hit, not the first: downtown's canopies and the districts' overhangs sit above the
        /// road, and taking the first would paint a zebra on an awning. Falls back to a hair above
        /// zero, which is where the ground plate is, if nothing is under the point at all.
        /// </summary>
        private static float GroundY(Vector3 point)
        {
            var hits = Physics.RaycastAll(
                new Vector3(point.x, 50f, point.z), Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore);

            float lowest = float.MaxValue;
            foreach (var hit in hits) lowest = Mathf.Min(lowest, hit.point.y);
            return lowest == float.MaxValue ? 0f : lowest;
        }
    }
}
