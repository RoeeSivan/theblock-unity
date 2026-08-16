using System.Collections.Generic;
using System.Linq;
using TheBlock.Core;
using TheBlock.Traffic;
using UnityEditor;
using UnityEngine;
using Convert = TheBlock.Core.Convert;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// U17's build pass: the baked street graph, the traffic lights, and the scene objects the
    /// runtime traffic hangs off.
    ///
    /// <b>It runs on EVERY build, including the fast one.</b> Nothing here bakes a NavMesh - the
    /// expensive step the ledger warns about - so there is no reason to hide it behind the slow
    /// menu item, and one very good reason not to: the crossings U16 places carry a node id and an
    /// edge id, and the lights answer to exactly those keys. Build the lights from a graph the
    /// crossings have never seen and every zebra silently stops working. So the graph is derived
    /// once, here, and <see cref="BuildNavigation"/> is handed the same object.
    ///
    /// <b>Where the numbers come from.</b> <c>traffic-graph.ts</c> and <c>npc/path.ts</c>: each
    /// authored polyline becomes a centripetal Catmull-Rom curve resampled every two metres, with
    /// the road height under each sample resolved by the same <see cref="GroundY"/> the zebras use.
    /// The web build does that at load, paying ~6,400 raycasts before the first frame; here it is
    /// build output, and the runtime never casts a ray for traffic at all.
    /// </summary>
    public static partial class WorldBuilder
    {
        /// <summary>Baked graph, lamp meshes and lamp materials. Swept by this pass and no other.</summary>
        private const string GeneratedTrafficFolder = "Assets/Traffic/Generated";

        /// <summary>Where <see cref="TrafficCarBuilder"/> leaves its prefabs.</summary>
        private const string TrafficPrefabFolder = "Assets/Prefabs/Traffic";

        /// <summary>The pole model, transcoded out of the web build's shipped copy. See the ledger.</summary>
        private const string TrafficLightModel = "Assets/Models/Props/traffic-light.glb";

        /// <summary>Samples per metre along a street's centreline. <c>buildPath</c>'s own default.</summary>
        private const float NetworkSamplesPerMetre = 0.5f;

        /// <summary>Subdivisions per authored segment when measuring the Catmull-Rom's arc length.</summary>
        private const int CurveSubdivisions = 20;

        /// <summary>Metres past which a light pole is not drawn. Well beyond where you can read one.</summary>
        private const float PoleCullDistance = 120f;

        /// <summary>
        /// How far in front of the housing's FRONT FACE our discs sit, in MODEL units (the model is
        /// ~78.6 units tall and scaled down to 4.5 m). Enough to clear the shell without z-fighting.
        ///
        /// <b>In front of the shell, not in front of the disc - and that distinction was the whole
        /// bug.</b> This used to be measured from <c>lampBox.max.z</c>, the front of the animated
        /// disc, on the assumption that the disc was the outermost thing at that height. It is not:
        /// the discs slide BEHIND a lens, and the shell's front face measures 9.675 against disc
        /// fronts at 6.883-7.163 - so the shell stands 2.51-2.79 units proud of them. A 0.3 epsilon
        /// off the wrong datum left every quad ~2.5 units INSIDE the housing, which at the pole's
        /// 0.06 world scale is 14 cm of solid model in front of the lamp.
        ///
        /// The lights were switching correctly the entire time; the city simply could not see them.
        /// </summary>
        private const float DiscFaceEpsilon = 0.3f;

        /// <summary>
        /// How far a lens bulges out of its rim, as a fraction of the lamp's radius. A real traffic
        /// lens is convex, and here that is not decoration: a FLAT lamp has no projected area at a
        /// grazing angle, which is exactly where a pedestrian standing at the kerb beside the pole
        /// sees it from. 0.35 of 3.45 model units is ~6.9 cm at the pole's 0.057 world scale.
        /// </summary>
        private const float DomeBulgeFrac = 0.35f;

        /// <summary>Fan segments around a lens. 14 is round enough at 40 cm across and costs 14 tris.</summary>
        private const int DomeSegments = 14;

        /// <summary>
        /// Emission multiplier on a lit lamp. The web build's 2.5, in URP terms.
        ///
        /// <b>Everything above 1.0 is currently dead.</b> The project renders with no
        /// post-processing at all - <c>m_RenderPostProcessing: 0</c> on every camera, no Volume in
        /// <c>World.unity</c>, LDR colour grading - so the HDR buffer simply clamps on output and a
        /// lit lamp reads as a flat saturated colour with no bloom halo. The number is kept because
        /// it is the right one the day a bloom volume appears; U33 left bloom out deliberately for
        /// its six-to-eight blur passes.
        /// </summary>
        private const float LampEmission = 2.5f;

        /// <summary>
        /// Builds the traffic world and returns the graph, so the navigation pass can share it.
        /// </summary>
        private static (List<NavNode> Nodes, List<NavEdge> Edges)? BuildTraffic(
            Transform root, TheBlockConfig.Root config, Options options, Report report)
        {
            var traffic = config.Traffic;
            if (traffic?.Network == null || traffic.Network.Count == 0)
            {
                report.Warnings.Add("traffic skipped - config has no `traffic.network`");
                return null;
            }

            // Colliders placed by the passes above are not in the physics scene until something
            // asks; the Y-baking raycasts below are that something. Without this every street bakes
            // at y 0 and the cars drive through the road in the raised districts.
            Physics.SyncTransforms();

            var (nodes, edges) = BuildGraph(traffic, report);
            if (edges.Count == 0)
            {
                report.Warnings.Add("traffic skipped - the network produced no usable streets");
                return null;
            }

            if (!options.Traffic)
            {
                report.Notes.Add("traffic: cars and lights skipped by build options (the graph is still built)");
                return (nodes, edges);
            }

            var generated = new HashSet<string>(System.StringComparer.Ordinal);
            var network = BakeNetwork(nodes, edges, traffic, generated, report);

            var group = NewGroup("Traffic", root);
            var lights = BuildTrafficLights(group, network, nodes, edges, traffic, generated, report);
            var system = BuildTrafficSystem(group, network, lights, traffic, report);

            SweepTrafficFolder(generated, report);

            report.Placed.Add(
                $"Traffic {network.Edges.Length} street(s) baked ({network.TotalLength:0} m), " +
                $"{lights.LitIntersections} lit intersection(s), " +
                $"{(system != null ? "car pool wired" : "NO CAR POOL")}");

            return (nodes, edges);
        }

        /// <summary>
        /// Rewrites <c>LampDiscs.asset</c> and the six lamp materials, so a lens can be reshaped or
        /// re-shaded without a world build.
        ///
        /// Every one of the 233 poles points at that one mesh, and nothing else about them changes
        /// when the lens does - same renderer, same three submeshes, same six materials - so this is
        /// the whole fix in a second, instead of a full <c>Build World</c> that re-places every pole
        /// and carries the <c>navMeshData</c> trap with it.
        ///
        /// <b>It deliberately does not sweep.</b> <see cref="SweepTrafficFolder"/> deletes anything
        /// in the generated folder this pass did not re-emit, which here would be the baked network
        /// and all six lamp materials.
        /// </summary>
        [MenuItem("The Block/Rebuild Traffic Lamps", priority = 9)]
        public static void RebuildTrafficLampsMenu()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(TrafficLightModel);
            if (model == null)
            {
                Debug.LogError($"Rebuild Traffic Lamps: no pole model at {TrafficLightModel}");
                return;
            }

            var report = new Report();
            var probe = new GameObject("LampProbe");
            probe.SetActive(false);
            try
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, probe.transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one;

                var lamps = new List<Bounds>();
                foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!renderer.name.StartsWith("lichtanimation", System.StringComparison.Ordinal)) continue;
                    lamps.Add(renderer.bounds);
                }

                if (lamps.Count != 3)
                {
                    Debug.LogError(
                        $"Rebuild Traffic Lamps: expected 3 lamp nodes in the model, found {lamps.Count}");
                    return;
                }

                lamps.Sort((a, b) => b.center.y.CompareTo(a.center.y)); // top to bottom: red, amber, green

                var generated = new HashSet<string>(System.StringComparer.Ordinal);
                var faces = MeasureLampFaces(visual, lamps, report);
                var mesh = BuildLampMesh(lamps, faces, generated, report);
                // The six lamp materials are rewritten in place at the same paths, so the poles keep
                // their references and a material-side fix (two-sided, emission) ships from here too.
                BuildLampMaterials(generated, report);
                AssetDatabase.SaveAssets();

                foreach (var warning in report.Warnings) Debug.LogWarning(warning);
                Debug.Log(
                    $"Rebuild Traffic Lamps: {mesh.vertexCount} vertices, " +
                    $"{mesh.triangles.Length / 3} triangles, {mesh.subMeshCount} submeshes\n" +
                    string.Join("\n", report.Notes), mesh);
            }
            finally
            {
                // Nothing in the scene was touched, so the scene does not need marking dirty - and
                // the probe must not survive to be saved into it.
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        // --- the baked network --------------------------------------------------------------------

        /// <summary>
        /// Turns the graph into the runtime asset: every street resampled by arc length with its
        /// road height resolved.
        ///
        /// The curve is a centripetal Catmull-Rom through the authored waypoints, which is what
        /// <c>buildPath</c> uses and what makes a bent street a curve rather than a hinge. Most
        /// streets here are two points and come out exactly straight; the handful with a bend are
        /// the reason this is not a straight-line resample.
        /// </summary>
        private static TrafficNetwork BakeNetwork(
            List<NavNode> nodes, List<NavEdge> edges, TheBlockConfig.TrafficSpec traffic,
            HashSet<string> generated, Report report)
        {
            var builtNodes = new TrafficNetwork.Node[nodes.Count];
            var nodeEdges = new List<int>();

            for (int i = 0; i < nodes.Count; i++)
            {
                builtNodes[i] = new TrafficNetwork.Node
                {
                    Position = nodes[i].Position,
                    EdgeStart = nodeEdges.Count,
                    EdgeCount = nodes[i].Edges.Count,
                };
                nodeEdges.AddRange(nodes[i].Edges);
            }

            var builtEdges = new TrafficNetwork.Edge[edges.Count];
            float total = 0f;
            int spikes = 0;

            for (int i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                var points = Resample(edge.Points, out float length);

                // The Y-bake is per SAMPLE and not per authored waypoint: a street that crosses from
                // one district onto another changes height in the middle, and interpolating between
                // the two ends would run the cars under the road for half its length.
                for (int p = 0; p < points.Length; p++)
                    points[p] = new Vector3(points[p].x, GroundY(points[p]), points[p].z);

                spikes += Despike(points);

                var bounds = new Bounds(points[0], Vector3.zero);
                foreach (var point in points) bounds.Encapsulate(point);

                builtEdges[i] = new TrafficNetwork.Edge
                {
                    A = edge.A,
                    B = edge.B,
                    Points = points,
                    Length = length,
                    Lanes = edge.Lanes,
                    LaneOffset = edge.LaneOffset,
                    HalfWidth = edge.HalfWidth,
                    Centre = bounds.center,
                    Radius = bounds.extents.magnitude,
                };

                total += length;
            }

            EnsureFolder(GeneratedTrafficFolder);
            var path = $"{GeneratedTrafficFolder}/TrafficNetwork.asset";
            generated.Add(path);
            report.Generated.Add(path);

            var asset = AssetDatabase.LoadAssetAtPath<TrafficNetwork>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<TrafficNetwork>();
                AssetDatabase.CreateAsset(asset, path);
            }

            asset.Fill(builtNodes, nodeEdges.ToArray(), builtEdges, total);
            EditorUtility.SetDirty(asset);

            report.Notes.Add(
                $"traffic network: {builtEdges.Length} street(s), {total:0} m of centreline, " +
                $"{builtEdges.Sum(e => e.Points.Length)} samples at {1f / NetworkSamplesPerMetre:0.#} m" +
                (spikes > 0 ? $", {spikes} height spike(s) flattened" : string.Empty));
            return asset;
        }

        /// <summary>
        /// Flattens single-sample height spikes, in place.
        ///
        /// <see cref="GroundY"/> now refuses a roof, but it still answers one ray at one point, and
        /// a street passing under a kerbstone, a doorstep or the lip of a raised forecourt gets one
        /// sample half a metre off its neighbours. Over two metres that is a ramp a car visibly hops.
        /// A sample that disagrees with the average of its neighbours by more than
        /// <see cref="SpikeTolerance"/> is replaced by that average - which leaves a genuine slope
        /// alone, because on a slope the neighbours are going the same way.
        /// </summary>
        private static int Despike(Vector3[] points)
        {
            int fixedUp = 0;

            for (int i = 1; i < points.Length - 1; i++)
            {
                float expected = (points[i - 1].y + points[i + 1].y) * 0.5f;
                if (Mathf.Abs(points[i].y - expected) <= SpikeTolerance) continue;
                points[i] = new Vector3(points[i].x, expected, points[i].z);
                fixedUp++;
            }

            return fixedUp;
        }

        /// <summary>Metres a sample may differ from the mean of its neighbours before it is a spike.</summary>
        private const float SpikeTolerance = 0.4f;

        /// <summary>
        /// Evenly spaced points along the centripetal Catmull-Rom through <paramref name="waypoints"/>.
        ///
        /// Two passes: subdivide finely to build an arc-length table, then walk that table at a
        /// constant step. Doing it in one pass by uniform curve parameter would bunch samples on the
        /// tight side of every bend, which is the thing arc-length parameterisation exists to stop.
        /// </summary>
        private static Vector3[] Resample(List<Vector3> waypoints, out float length)
        {
            var dense = new List<Vector3>();
            int count = waypoints.Count;

            for (int i = 0; i < count - 1; i++)
            {
                // three.js reflects the end points to get the outer control points, so the curve
                // starts and ends exactly on the authored waypoint rather than being pulled short.
                var p0 = i > 0 ? waypoints[i - 1] : waypoints[0] * 2f - waypoints[1];
                var p1 = waypoints[i];
                var p2 = waypoints[i + 1];
                var p3 = i + 2 < count ? waypoints[i + 2] : waypoints[count - 1] * 2f - waypoints[count - 2];

                for (int s = 0; s < CurveSubdivisions; s++)
                    dense.Add(CatmullRom(p0, p1, p2, p3, s / (float)CurveSubdivisions));
            }

            dense.Add(waypoints[count - 1]);

            var cumulative = new float[dense.Count];
            for (int i = 1; i < dense.Count; i++)
                cumulative[i] = cumulative[i - 1] + Vector3.Distance(dense[i - 1], dense[i]);

            length = cumulative[^1];
            int segments = Mathf.Max(2, Mathf.CeilToInt(length * NetworkSamplesPerMetre));
            var result = new Vector3[segments + 1];
            int cursor = 0;

            for (int i = 0; i <= segments; i++)
            {
                float target = length * i / segments;
                while (cursor < cumulative.Length - 2 && cumulative[cursor + 1] < target) cursor++;

                float span = cumulative[cursor + 1] - cumulative[cursor];
                float t = span > 1e-5f ? (target - cumulative[cursor]) / span : 0f;
                result[i] = Vector3.Lerp(dense[cursor], dense[cursor + 1], t);
            }

            return result;
        }

        /// <summary>
        /// Centripetal Catmull-Rom (alpha = 0.5) - the parameterisation three.js uses here, and the
        /// reason a tight street corner does not produce a cusp or a loop the way the uniform one
        /// does.
        /// </summary>
        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t0 = 0f;
            float t1 = t0 + Mathf.Pow(Vector3.Distance(p0, p1), 0.5f);
            float t2 = t1 + Mathf.Pow(Vector3.Distance(p1, p2), 0.5f);
            float t3 = t2 + Mathf.Pow(Vector3.Distance(p2, p3), 0.5f);

            // Coincident control points collapse a knot span to zero and divide by it. Falling back
            // to the straight segment is the right answer there - there is no curve to describe.
            if (t1 - t0 < 1e-5f || t2 - t1 < 1e-5f || t3 - t2 < 1e-5f) return Vector3.Lerp(p1, p2, t);

            float tt = Mathf.Lerp(t1, t2, t);
            var a1 = (t1 - tt) / (t1 - t0) * p0 + (tt - t0) / (t1 - t0) * p1;
            var a2 = (t2 - tt) / (t2 - t1) * p1 + (tt - t1) / (t2 - t1) * p2;
            var a3 = (t3 - tt) / (t3 - t2) * p2 + (tt - t2) / (t3 - t2) * p3;
            var b1 = (t2 - tt) / (t2 - t0) * a1 + (tt - t0) / (t2 - t0) * a2;
            var b2 = (t3 - tt) / (t3 - t1) * a2 + (tt - t1) / (t3 - t1) * a3;
            return (t2 - tt) / (t2 - t1) * b1 + (tt - t1) / (t2 - t1) * b2;
        }

        // --- the lights ---------------------------------------------------------------------------

        /// <summary>
        /// One pole per approach of every lit intersection, on the right of the road just before the
        /// stop line, head facing the oncoming cars - and one <see cref="TrafficLightSystem"/> that
        /// owns all of them.
        ///
        /// The phase group is the arrival AXIS, exactly as <c>traffic-lights.ts</c> classifies it.
        /// That test survives the handedness conversion untouched: negating X changes the sign of
        /// the tangent's x, never its magnitude, so "mostly-X" and "mostly-Z" partition the same
        /// approaches into the same two groups they do in the web build.
        /// </summary>
        private static TrafficLightSystem BuildTrafficLights(
            Transform parent, TrafficNetwork network, List<NavNode> nodes, List<NavEdge> edges,
            TheBlockConfig.TrafficSpec traffic, HashSet<string> generated, Report report)
        {
            var lightsCfg = traffic.Lights ?? new TheBlockConfig.LightsSpec();
            var group = NewGroup("Lights", parent);
            var system = parent.gameObject.AddComponent<TrafficLightSystem>();

            var template = BuildPoleTemplate(lightsCfg, generated, report);
            var controllers = new List<TrafficLightSystem.Controller>();
            var rng = new System.Random(20260817);

            float half = lightsCfg.GreenSec + lightsCfg.YellowSec + lightsCfg.AllRedSec + lightsCfg.RedYellowSec;
            float cycle = half * 2f;
            int poles = 0, unbalanced = 0;

            foreach (var node in nodes)
            {
                if (node.Edges.Count < 3) continue; // not lit - the same rule the crossings use

                var controller = new TrafficLightSystem.Controller
                {
                    NodeId = node.Id,
                    // Desynchronised, or the whole city blinks as one organism.
                    StartOffset = (float)rng.NextDouble() * cycle,
                };

                var counts = new int[2];

                foreach (int edgeId in node.Edges)
                {
                    var edge = edges[edgeId];
                    // Arriving AT this node: travelling b→a when the node is the path's start.
                    int dir = edge.A == node.Id ? -1 : 1;
                    float stop = dir == 1 ? edge.Length - traffic.StopLineDist : traffic.StopLineDist;

                    network.SampleLane(edgeId, stop, dir, traffic.LaneOffset, out var lane, out var tangent);

                    int phase = Mathf.Abs(tangent.x) > Mathf.Abs(tangent.z) ? 0 : 1;
                    counts[phase]++;

                    var pole = template != null
                        ? PlacePole(template, group, lane, tangent, traffic, lightsCfg, node.Id, edgeId)
                        : null;
                    if (pole != null) poles++;

                    controller.Approaches.Add(new TrafficLightSystem.Approach
                    {
                        EdgeId = edgeId,
                        Group = phase,
                        Pole = pole,
                    });
                }

                // If every approach landed in one axis group, the other group's whole half-cycle is
                // spent all-red for nobody and the junction looks deadlocked. The current network is
                // balanced; this catches a future street whose end tangent classifies oddly.
                if (counts[0] == 0 || counts[1] == 0) unbalanced++;

                controllers.Add(controller);
            }

            if (template != null) UnityEngine.Object.DestroyImmediate(template);

            system.Configure(
                lightsCfg.GreenSec, lightsCfg.YellowSec, lightsCfg.RedYellowSec, lightsCfg.AllRedSec,
                controllers);
            EditorUtility.SetDirty(system);

            report.Notes.Add(
                $"traffic lights: {controllers.Count} lit intersection(s), {poles} pole(s), " +
                $"{cycle:0.#} s cycle");
            if (unbalanced > 0)
                report.Warnings.Add(
                    $"traffic lights: {unbalanced} intersection(s) have all approaches in ONE phase " +
                    "group - half their cycle is all-red for nobody. Check those streets' end tangents");

            return system;
        }

        private static TrafficLightPole PlacePole(
            GameObject template, Transform parent, Vector3 lane, Vector3 tangent,
            TheBlockConfig.TrafficSpec traffic, TheBlockConfig.LightsSpec lightsCfg, int nodeId, int edgeId)
        {
            // Lane centre → kerb. The lane sample is already `laneOffset` right of the centreline,
            // so the pole only needs the remainder out to `sideOffset`.
            float extra = lightsCfg.SideOffset - traffic.LaneOffset;
            var right = Vector3.Cross(Vector3.up, tangent);
            var position = lane + right * extra;
            position.y = GroundY(position);

            var pole = (GameObject)UnityEngine.Object.Instantiate(template, parent);
            pole.name = $"Light_{nodeId:000}_{edgeId:000}";
            pole.SetActive(true);
            // The head faces back down the road at the cars arriving on it, which is −tangent.
            pole.transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(-tangent, Vector3.up) * Convert.RotFromRadians(lightsCfg.ModelYawTune));

            return pole.GetComponent<TrafficLightPole>();
        }

        /// <summary>
        /// One prepped pole, cloned 230-odd times.
        ///
        /// Three things are done to the imported model, and each of them is load-bearing:
        ///
        ///  1. <b>Its own lamp nodes are destroyed.</b> The GLB animates coloured discs sliding
        ///     behind a translucent lens; the web build hides them because three.js cannot sort that,
        ///     and they are no more usable here.
        ///  2. <b>The housing material is rebuilt opaque.</b> The whole model is ONE material and
        ///     the glTF declares it <c>BLEND</c>, so the import lands in the transparent queue -
        ///     230 poles' worth of sorted overdraw for geometry with no transparency in it. A
        ///     glTFast material's surface mode is fixed at import (U11), so this is a generated
        ///     URP/Lit asset, the same answer as the facade tint and the leaf cutouts.
        ///  3. <b>Three lamp quads become ONE mesh with three submeshes</b>, positioned from the
        ///     original lamps' own bounds rather than typed-in coordinates. One renderer per pole
        ///     instead of three, and switching a light is an assignment into a shared-material array.
        /// </summary>
        private static GameObject BuildPoleTemplate(
            TheBlockConfig.LightsSpec lightsCfg, HashSet<string> generated, Report report)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(TrafficLightModel);
            if (model == null)
            {
                report.Missing.Add(
                    $"traffic lights - no pole model at {TrafficLightModel}; the lights still run, " +
                    "but nothing is drawn");
                return null;
            }

            var root = new GameObject("PoleTemplate");
            root.SetActive(false);

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            visual.name = "Visual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            float scale = lightsCfg.ModelNativeHeight > 0.01f
                ? lightsCfg.PoleHeight / lightsCfg.ModelNativeHeight
                : 1f;
            visual.transform.localScale = Vector3.one * scale;

            foreach (var animator in visual.GetComponentsInChildren<Animator>(true))
                UnityEngine.Object.DestroyImmediate(animator);

            // Measure the three lamps before anything is destroyed, in the model's own units - the
            // probe is unrotated at the origin, so its world bounds ARE its local ones.
            visual.transform.localScale = Vector3.one;
            var lamps = new List<(string Name, Bounds Box, Renderer Renderer)>();
            foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.name.StartsWith("lichtanimation", System.StringComparison.Ordinal)) continue;
                lamps.Add((renderer.name, renderer.bounds, renderer));
            }

            if (lamps.Count != 3)
                report.Warnings.Add(
                    $"traffic lights: expected 3 lamp nodes in the model, found {lamps.Count} - " +
                    "the lamp discs may be misplaced");

            // Top to bottom is red, amber, green - read off the measured heights rather than off the
            // German node names, which arrive mojibaked ("grün") through the glTF's UTF-8.
            lamps.Sort((a, b) => b.Box.center.y.CompareTo(a.Box.center.y));

            // The two faces each lens has to clear, measured off the housing at that lamp's own
            // height. Taken while the probe is still unrotated and unscaled, so this is in model
            // units like everything else in BuildLampMesh, and taken BEFORE the lamp nodes are
            // destroyed.
            var boxes = lamps.Select(l => l.Box).ToList();
            var faces = MeasureLampFaces(visual, boxes, report);

            var discs = BuildLampMesh(boxes, faces, generated, report);
            foreach (var lamp in lamps) UnityEngine.Object.DestroyImmediate(lamp.Renderer.gameObject);

            var housing = BuildHousingMaterial(visual, generated, report);
            if (housing != null)
                foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
                    renderer.sharedMaterial = housing;

            var lampMaterials = BuildLampMaterials(generated, report);

            var lampObject = new GameObject("Lamps");
            lampObject.transform.SetParent(visual.transform, worldPositionStays: false);
            lampObject.AddComponent<MeshFilter>().sharedMesh = discs;
            var lampRenderer = lampObject.AddComponent<MeshRenderer>();
            lampRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lampRenderer.sharedMaterials = new[] { lampMaterials[1], lampMaterials[3], lampMaterials[5] };

            visual.transform.localScale = Vector3.one * scale;

            var pole = root.AddComponent<TrafficLightPole>();
            pole.Configure(
                lampRenderer,
                lampMaterials[0], lampMaterials[1],
                lampMaterials[2], lampMaterials[3],
                lampMaterials[4], lampMaterials[5]);

            // The post only. Nothing in the game reaches the head, and a capsule per pole is the
            // cheapest thing that stops a car driving through one.
            var collider = root.AddComponent<CapsuleCollider>();
            collider.radius = lightsCfg.PoleColliderRadius;
            collider.height = lightsCfg.PoleHeight;
            collider.center = new Vector3(0f, lightsCfg.PoleHeight * 0.5f, 0f);

            var lod = root.AddComponent<LODGroup>();
            float fov = Camera.main != null ? Camera.main.fieldOfView : 75f;
            float screenHeight = lightsCfg.PoleHeight /
                                 (2f * PoleCullDistance * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
            lod.SetLODs(new[] { new LOD(screenHeight, root.GetComponentsInChildren<Renderer>(true)) });
            lod.RecalculateBounds();

            return root;
        }

        /// <summary>
        /// Six domed lenses, three submeshes, one mesh - in MODEL units, so the whole thing scales
        /// with the pole. Each lens takes its X, Y and radius from its own lamp's box, and its Z
        /// plane from <see cref="MeasureLampFaces"/>, so it is the outermost thing at that height
        /// and nothing of the model's own can draw over it.
        ///
        /// <b>Two lenses per lamp, front AND rear, in the SAME submesh.</b> A pole is aimed at the
        /// cars on its own approach, and the player on foot stands at the kerb beside it or looks at
        /// it across the junction - both outside the head's forward hemisphere, where a single
        /// front-facing lens is backface-culled and hidden behind the opaque shell. A real traffic
        /// light is blank behind; this one is not, and that is the deliberate trade. Sharing the
        /// submesh keeps the whole thing at three materials and one draw call per state.
        ///
        /// <b>And they are domes, not flat discs.</b> A flat lens has no projected area at a grazing
        /// angle, which is exactly the angle a pedestrian beside the pole reads it from.
        /// </summary>
        private static Mesh BuildLampMesh(
            List<Bounds> lamps, List<LampFaces> faces, HashSet<string> generated, Report report)
        {
            var mesh = new Mesh { name = "LampDiscs", subMeshCount = Mathf.Max(1, lamps.Count) };
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var submeshes = new List<int[]>();

            for (int i = 0; i < lamps.Count; i++)
            {
                var box = lamps[i];
                float radius = Mathf.Max(box.size.x, box.size.y) * 0.5f * 0.9f;
                var centre = new Vector2(box.center.x, box.center.y);
                var triangles = new List<int>();

                AddLensCap(vertices, normals, uvs, triangles,
                    centre, radius, faces[i].Front + DiscFaceEpsilon, outwards: 1f);
                AddLensCap(vertices, normals, uvs, triangles,
                    centre, radius, faces[i].Rear - DiscFaceEpsilon, outwards: -1f);

                EnforceWinding(vertices, normals, triangles, i, report);
                submeshes.Add(triangles.ToArray());
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            for (int i = 0; i < submeshes.Count; i++) mesh.SetTriangles(submeshes[i], i);
            mesh.RecalculateBounds();

            EnsureFolder(GeneratedTrafficFolder);
            var path = $"{GeneratedTrafficFolder}/LampDiscs.asset";
            generated.Add(path);
            report.Generated.Add(path);

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            // Rewritten in place so the poles built last time keep pointing at the same asset - and
            // so the whole fix can ship by rewriting one mesh, without re-placing 233 poles.
            existing.Clear();
            existing.subMeshCount = mesh.subMeshCount;
            existing.SetVertices(vertices);
            existing.SetNormals(normals);
            existing.SetUVs(0, uvs);
            for (int i = 0; i < submeshes.Count; i++) existing.SetTriangles(submeshes[i], i);
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            return existing;
        }

        /// <summary>
        /// One convex lens: a triangle fan bulging <see cref="DomeBulgeFrac"/> of its radius out of
        /// the plane <paramref name="rimZ"/>, along <paramref name="outwards"/> (+1 front, −1 rear).
        /// Appends to the caller's buffers so both caps of a lamp land in one submesh.
        /// </summary>
        private static void AddLensCap(
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles,
            Vector2 centre, float radius, float rimZ, float outwards)
        {
            float bulge = radius * DomeBulgeFrac;
            float apexZ = rimZ + bulge * outwards;

            // The sphere this cap is a slice of: a cap of radius r and depth d belongs to a sphere of
            // radius (r² + d²) / 2d. Normals taken off its centre are what make the lens shade round
            // instead of flat, which is the whole point of not being a quad any more.
            float sphereRadius = (radius * radius + bulge * bulge) / (2f * bulge);
            var sphereCentre = new Vector3(centre.x, centre.y, apexZ - sphereRadius * outwards);

            int apex = vertices.Count;
            vertices.Add(new Vector3(centre.x, centre.y, apexZ));
            normals.Add(new Vector3(0f, 0f, outwards));
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int s = 0; s < DomeSegments; s++)
            {
                float angle = s * Mathf.PI * 2f / DomeSegments;
                float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                var rim = new Vector3(centre.x + cos * radius, centre.y + sin * radius, rimZ);
                vertices.Add(rim);
                normals.Add((rim - sphereCentre).normalized);
                uvs.Add(new Vector2(0.5f + cos * 0.5f, 0.5f + sin * 0.5f));
            }

            // <b>A +Z-facing triangle is COUNTER-clockwise in world XY.</b> Clockwise is the rule in
            // SCREEN space, and a camera looking at a +Z surface faces −Z, which puts its right at
            // world −X and mirrors the winding on the way. Worked through on the quad this replaced:
            // its order (v0, v2, v1) = (−1,−1), (−1,1), (1,−1) is clockwise in world XY, lands
            // counter-clockwise on screen, and is therefore a BACK face from +Z.
            //
            // Which means U17's lamp quads faced −Z - away from the lens - from the day they were
            // written. They were never visible from the road; the grey lamps were not a grazing-angle
            // problem at all. Position was measured twice and facing was never measured once.
            for (int s = 0; s < DomeSegments; s++)
            {
                int a = apex + 1 + s;
                int b = apex + 1 + (s + 1) % DomeSegments;
                triangles.Add(apex);
                triangles.Add(outwards > 0f ? a : b);
                triangles.Add(outwards > 0f ? b : a);
            }
        }

        /// <summary>
        /// Measures every triangle's facing against its own vertex normal and flips the ones that
        /// disagree.
        ///
        /// <b>Winding is measured here, never reasoned about.</b> The lamp quads faced −Z from U17
        /// until 2026-08-16, and the first dome build (17:58 that day) was inside-out too - decoded
        /// off the asset: front cap stored normal (0,0,+1), geometric normal (…,−0.94). Each time the
        /// argument in the comment was confident and wrong, and the symptom - grey from the front,
        /// colour from the side, where the far half of an inverted dome's rim happens to face you -
        /// was read as a position bug twice. Unity's front face is <c>Cross(b − a, c − a)</c>
        /// (the Mesh docs' own quad: <c>(0,2,1)</c> with normals <c>−forward</c>), so a triangle
        /// whose cross points against its stored normal is a back face, and the fix is a swap.
        /// </summary>
        private static void EnforceWinding(
            List<Vector3> vertices, List<Vector3> normals, List<int> triangles, int lamp, Report report)
        {
            int flipped = 0;
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                int ia = triangles[t], ib = triangles[t + 1], ic = triangles[t + 2];
                var a = vertices[ia];
                var geometric = Vector3.Cross(vertices[ib] - a, vertices[ic] - a);
                var stored = normals[ia] + normals[ib] + normals[ic];
                if (Vector3.Dot(geometric, stored) >= 0f) continue;

                triangles[t + 1] = ic;
                triangles[t + 2] = ib;
                flipped++;
            }

            if (flipped > 0)
                report.Warnings.Add(
                    $"traffic lights: lamp {lamp} had {flipped} triangle(s) wound against their normals - " +
                    "flipped at build. AddLensCap's winding order is wrong again; the mesh is right anyway.");
        }

        /// <summary>The housing's front and rear face at one lamp's own height, in model units.</summary>
        private readonly struct LampFaces
        {
            public readonly float Front;
            public readonly float Rear;

            public LampFaces(float front, float rear)
            {
                Front = front;
                Rear = rear;
            }
        }

        /// <summary>Below this many housing vertices in a lamp's window, the sample is not trusted.</summary>
        private const int MinFaceSamples = 8;

        /// <summary>
        /// Where each lens has to sit, front and back, measured off the housing's own vertices inside
        /// that lamp's X/Y window.
        ///
        /// <b>A whole-model min/max is the wrong datum for the back.</b> The front is safe either way
        /// - the visor lip is the frontmost thing on the model - but the entire pole is ONE mesh, so
        /// a global <c>bounds.min.z</c> is the back of the MAST. Measured: the model's global z runs
        /// −2.98…9.67, while inside a lamp's own window it runs <b>5.83…9.67</b>. The head is 3.84
        /// units deep (22 cm); −2.98 is the mast, 0.68 m further back, and a rear lens placed there
        /// would hang in mid-air behind the pole. Sampling the vertices around each lamp gives the
        /// shell at that lamp's own height, and behind the lamps there is nothing else to clear -
        /// the bracket sits in the seams BETWEEN the three heads, not behind them.
        ///
        /// The probe is unrotated and unscaled at the origin when this runs, so these are model units
        /// like everything else in <see cref="BuildLampMesh"/>, and it must run BEFORE the lamp nodes
        /// are destroyed.
        /// </summary>
        private static List<LampFaces> MeasureLampFaces(
            GameObject visual, List<Bounds> lamps, Report report)
        {
            var housing = new List<(Matrix4x4 ToProbe, Vector3[] Verts)>();
            float globalFront = float.MinValue, globalRear = float.MaxValue;

            foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.name.StartsWith("lichtanimation", System.StringComparison.Ordinal)) continue;
                globalFront = Mathf.Max(globalFront, renderer.bounds.max.z);
                globalRear = Mathf.Min(globalRear, renderer.bounds.min.z);

                if (!renderer.TryGetComponent<MeshFilter>(out var filter)) continue;
                if (filter.sharedMesh == null) continue;
                housing.Add((renderer.transform.localToWorldMatrix, filter.sharedMesh.vertices));
            }

            if (housing.Count == 0)
                report.Warnings.Add(
                    "traffic lights: no housing geometry found - the lenses are placed off the lamp " +
                    "discs themselves, which is right only when there is no shell to clear.");

            var faces = new List<LampFaces>(lamps.Count);
            for (int i = 0; i < lamps.Count; i++)
            {
                var box = lamps[i];
                float radius = Mathf.Max(box.size.x, box.size.y) * 0.5f * 0.9f;
                float front = float.MinValue, rear = float.MaxValue;
                int hits = 0;

                foreach (var (toProbe, verts) in housing)
                {
                    foreach (var local in verts)
                    {
                        var v = toProbe.MultiplyPoint3x4(local);
                        if (Mathf.Abs(v.x - box.center.x) > radius) continue;
                        if (Mathf.Abs(v.y - box.center.y) > radius) continue;
                        front = Mathf.Max(front, v.z);
                        rear = Mathf.Min(rear, v.z);
                        hits++;
                    }
                }

                if (hits < MinFaceSamples)
                {
                    if (housing.Count > 0)
                        report.Warnings.Add(
                            $"traffic lights: lamp {i} caught only {hits} housing vertices in its own " +
                            "window - its lenses fall back to the whole model's bounds");
                    front = housing.Count > 0 ? globalFront : box.max.z;
                    rear = housing.Count > 0 ? globalRear : box.min.z;
                }

                faces.Add(new LampFaces(front, rear));
            }

            report.Notes.Add(
                "traffic lights: lens planes in model units - " +
                string.Join(", ", faces.Select((f, i) => $"lamp{i} rear {f.Rear:0.00} / front {f.Front:0.00}")));

            return faces;
        }

        /// <summary>Six shared materials, in the order red on/off, amber on/off, green on/off.</summary>
        private static Material[] BuildLampMaterials(HashSet<string> generated, Report report)
        {
            var colors = new[]
            {
                ("Red", new Color(1f, 0.13f, 0.13f)),
                ("Amber", new Color(1f, 0.67f, 0f)),
                ("Green", new Color(0.13f, 0.87f, 0.27f)),
            };

            var materials = new Material[6];
            for (int i = 0; i < colors.Length; i++)
            {
                materials[i * 2] = LampMaterial($"{colors[i].Item1}_On", colors[i].Item2, true, generated, report);
                materials[i * 2 + 1] = LampMaterial($"{colors[i].Item1}_Off", colors[i].Item2, false, generated, report);
            }

            return materials;
        }

        private static Material LampMaterial(
            string name, Color color, bool on, HashSet<string> generated, Report report)
        {
            EnsureFolder(GeneratedTrafficFolder);
            var path = $"{GeneratedTrafficFolder}/Lamp_{name}.mat";
            generated.Add(path);
            report.Generated.Add(path);

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.name = $"Lamp_{name}";
            material.SetColor("_BaseColor", on ? color.linear : new Color(0.1f, 0.1f, 0.1f, 1f).linear);
            material.SetFloat("_Smoothness", 0.4f);
            material.SetFloat("_Metallic", 0f);

            // Two-sided. A lens is twelve triangles, so this costs nothing - and it means a lamp can
            // never again be hidden by its own winding, whatever EnforceWinding did or did not catch.
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);

            // Emission is what makes a lit lamp read as lit rather than as a coloured sticker - and
            // it is also what a future bloom pass would pick up. Off gets no emission at all rather
            // than a dim one: an unlit lamp is a dark lens.
            if (on)
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                material.SetColor("_EmissionColor", color.linear * LampEmission);
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                material.SetColor("_EmissionColor", Color.black);
            }

            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// The housing, rebuilt as an OPAQUE URP/Lit material carrying the model's base map.
        ///
        /// Only the base map crosses over. glTF packs metal-roughness into G and B of one texture
        /// and occlusion into R of another, and URP/Lit wants metallic in R with smoothness in A, so
        /// copying those would be silently wrong; the normal map is raw XYZ that URP's
        /// <c>UnpackNormal</c> would misread. On a 4.5 m pole none of them is the difference between
        /// reading as a traffic light and not.
        /// </summary>
        private static Material BuildHousingMaterial(
            GameObject visual, HashSet<string> generated, Report report)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                report.Warnings.Add("traffic lights: URP/Lit shader not found; the pole keeps its imported material");
                return null;
            }

            Material source = null;
            foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.sharedMaterial == null) continue;
                source = renderer.sharedMaterial;
                break;
            }

            if (source == null) return null;

            EnsureFolder(GeneratedTrafficFolder);
            var path = $"{GeneratedTrafficFolder}/TrafficLight.mat";
            generated.Add(path);
            report.Generated.Add(path);

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.name = "TrafficLight";

            if (source.HasProperty("baseColorTexture"))
                material.SetTexture("_BaseMap", source.GetTexture("baseColorTexture"));
            if (source.HasProperty("baseColorTexture_ST"))
                material.SetVector("_BaseMap_ST", UnflipV(source.GetVector("baseColorTexture_ST")));

            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.35f);
            material.SetFloat("_Surface", 0f); // opaque, which the glTF's BLEND mode is not
            material.SetFloat("_AlphaClip", 0f);
            BaseShaderGUI.SetupMaterialBlendMode(material);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        // --- the car pool -------------------------------------------------------------------------

        private static TrafficSystem BuildTrafficSystem(
            Transform parent, TrafficNetwork network, TrafficLightSystem lights,
            TheBlockConfig.TrafficSpec traffic, Report report)
        {
            var prefabs = AssetDatabase
                .FindAssets("t:Prefab", new[] { TrafficPrefabFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p, System.StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(p => p != null && p.GetComponent<TrafficCar>() != null)
                .ToList();

            if (prefabs.Count == 0)
            {
                report.Missing.Add(
                    $"traffic cars - no prefabs under {TrafficPrefabFolder}. Run " +
                    "The Block → Build Traffic Cars, then build the world again");
                return null;
            }

            var pumps = (traffic.GasStops?.Points ?? new List<TheBlockConfig.Vec3>())
                .Select(p => Convert.Pos(p.X, 0f, p.Z))
                .ToArray();
            for (int i = 0; i < pumps.Length; i++) pumps[i] = new Vector3(pumps[i].x, GroundY(pumps[i]), pumps[i].z);

            var system = parent.gameObject.AddComponent<TrafficSystem>();
            system.Configure(network, lights, pumps, traffic, prefabs);
            EditorUtility.SetDirty(system);

            report.Notes.Add(
                $"traffic cars: {prefabs.Count} model(s) pooled, {pumps.Length} fuel stop(s)");
            return system;
        }

        // --- housekeeping ---------------------------------------------------------------------------

        /// <summary>
        /// Deletes anything in <see cref="GeneratedTrafficFolder"/> this build did not write.
        ///
        /// Done HERE and not in <see cref="SweepGenerated"/> on purpose. That sweep runs on every
        /// build whether or not the traffic pass did, and the one time an equivalent sweep ran
        /// without the pass that fills it, it deleted the zebras' mesh and material out from under
        /// 230 crossings that still pointed at them.
        /// </summary>
        private static void SweepTrafficFolder(HashSet<string> generated, Report report)
        {
            if (!AssetDatabase.IsValidFolder(GeneratedTrafficFolder)) return;

            foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { GeneratedTrafficFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path) || generated.Contains(path)) continue;
                AssetDatabase.DeleteAsset(path);
                report.Notes.Add($"deleted stale traffic asset {path}");
            }
        }
    }
}
