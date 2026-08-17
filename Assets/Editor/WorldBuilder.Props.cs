using System.Collections.Generic;
using System.Linq;
using TheBlock.Core;
using TheBlock.Traffic;
using TheBlock.World;
using UnityEditor;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// U35h - the street props: three prefabs from the prepped .glbs, and a layout baked into the
    /// scene's <see cref="PropSystem"/>.
    ///
    /// <b>Nothing here is a port.</b> The web build has no loose street furniture - what looks like a
    /// bench or a bin there is baked into the district meshes (memory: <c>breakable-props-census</c>)
    /// - so the three models are Sketchfab downloads the user chose, prepped once by
    /// <c>tools/prep-props.sh</c> (transforms baked, origin at the base, textures ≤ 1024², the bin
    /// decimated), and every position is DERIVED here rather than typed in:
    ///
    ///  * <b>bins</b> - one per lit intersection, beside the first pole of that junction, 1.2 m onto
    ///    the pavement and 0.8 m upstream, so 70 bins land where 70 poles already proved the kerb is;
    ///  * <b>cones</b> - a row of four along the kerb in front of the gas station and the auto shop,
    ///    on the road side of the pole line;
    ///  * <b>benches</b> - two per anchor place (7-Eleven, pizza place, gas station, auto shop), on
    ///    the pavement facing the road, either side of the place's own frontage.
    ///
    /// "In front of" is the nearest point on the traffic network's centreline to the place's
    /// footprint, and "the pavement" is <c>lights.sideOffset</c> from that centreline - the same
    /// number that put the poles at the kerb. Every candidate is snapped with <c>GroundY</c> and
    /// tested with an overlap box against the world; anything inside a building or off the plate is
    /// dropped and reported, never placed. The counts are the perf knob and the report prints them.
    ///
    /// Runs inside <b>Build World</b> (after traffic, because the bins need the poles and the
    /// compressed-texture material clones must be re-emitted or <c>SweepGenerated</c> deletes them)
    /// and on its own from <b>The Block → Build Props</b>.
    /// </summary>
    public static partial class WorldBuilder
    {
        private const string PropModelFolder = "Assets/Models/Props";
        private const string PropPrefabFolder = "Assets/Prefabs/Props";
        private const string PropsHostName = "Props";

        /// <summary>Metres from the kerb line onto the pavement for a bin beside its pole.</summary>
        private const float BinInset = 1.2f;

        /// <summary>Metres upstream (toward the arriving cars) from the pole for the bin.</summary>
        private const float BinUpstream = 0.8f;

        /// <summary>Cone row: spacing along the kerb, and how far onto the carriageway side of the kerb.</summary>
        private const float ConeSpacing = 1.3f;
        private const float ConeKerbOffset = -0.6f;

        /// <summary>Benches: metres onto the pavement past the kerb line, and either side of the frontage.</summary>
        private const float BenchInset = 1.6f;
        private const float BenchSpread = 5f;

        [MenuItem("The Block/Build Props", priority = 9)]
        public static void BuildPropsOnly()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Build Props: stop Play mode first - a build in Play is discarded on exit.");
                return;
            }

            var root = Object.FindAnyObjectByType<WorldRoot>();
            if (root == null)
            {
                Debug.LogError("Build Props: no WorldRoot in the scene. Run The Block → Build World.");
                return;
            }

            var snapshot = TheBlockConfig.Load(reload: false);
            if (snapshot?.Config == null)
            {
                Debug.LogError("Build Props: no config snapshot. Run tools/export-config.sh, then Build World.");
                return;
            }

            var report = new Report();
            var options = new Options();
            ResetTexturePass();

            BuildProps(root.transform, snapshot.Config, options, report);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            var text = new System.Text.StringBuilder(
                $"Build Props - {report.Warnings.Count} warning(s), {report.Missing.Count} missing\n");
            foreach (var line in report.Placed) text.AppendLine("  " + line);
            foreach (var line in report.Notes) text.AppendLine("  " + line);
            foreach (var line in report.Warnings) text.AppendLine("  ⚠ " + line);
            foreach (var line in report.Missing) text.AppendLine("  ✗ " + line);
            if (report.Missing.Count > 0) Debug.LogWarning(text.ToString(), root);
            else Debug.Log(text.ToString(), root);
        }

        /// <summary>The pass. Idempotent: the previous host goes first.</summary>
        private static void BuildProps(
            Transform root, TheBlockConfig.Root config, Options options, Report report)
        {
            int layer = NpcBuilder.EnsureLayer(Breakable.PropsLayerName);
            if (layer <= 0)
            {
                report.Missing.Add("props - could not create the Props layer; nothing placed");
                return;
            }

            var old = root.Find(PropsHostName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var cone = BuildPropPrefab(PropKind.Cone, "cone.glb", mass: 3f, comFraction: 0.25f,
                footprintScale: 0.7f, cullDistance: 60f, layer, options, report);
            var bin = BuildPropPrefab(PropKind.Bin, "trash-bin.glb", mass: 25f, comFraction: 0.35f,
                footprintScale: 1f, cullDistance: 80f, layer, options, report);
            var bench = BuildPropPrefab(PropKind.Bench, "bench.glb", mass: 60f, comFraction: 0.4f,
                footprintScale: 1f, cullDistance: 100f, layer, options, report);

            var lights = config.Traffic?.Lights ?? new TheBlockConfig.LightsSpec();
            var layout = BakeLayout(root, config, lights, report);

            var host = NewGroup(PropsHostName, root);
            var system = host.gameObject.AddComponent<PropSystem>();
            system.Configure(layout, cone, bin, bench);
            EditorUtility.SetDirty(system);

            int cones = layout.Count(e => e.Kind == PropKind.Cone);
            int bins = layout.Count(e => e.Kind == PropKind.Bin);
            int benches = layout.Count(e => e.Kind == PropKind.Bench);
            report.Placed.Add(
                $"props: {layout.Count} baked - {bins} bins, {cones} cones, {benches} benches " +
                $"(spawned at runtime by PropSystem while Street Props is on)");
        }

        // --- prefabs -----------------------------------------------------------------------------

        /// <summary>
        /// One prop prefab: the .glb visual under a root that carries the box, the body and the
        /// script. Saved over the same path each build so the GUID - and the scene's reference -
        /// survives.
        ///
        /// The root is at identity with the visual unrotated, so the renderer bounds ARE the local
        /// bounds and the box comes straight off them. The bodies are configured to SLEEP: low centre
        /// of mass so a nudged bin sits back down rather than going over, interpolation so a flying
        /// cone does not stutter at 50 Hz, and no continuous collision - nothing here is fast enough
        /// to tunnel and CCD on 95 bodies is a cost for nothing.
        /// </summary>
        private static GameObject BuildPropPrefab(
            PropKind kind, string file, float mass, float comFraction, float footprintScale,
            float cullDistance, int layer, Options options, Report report)
        {
            string modelPath = $"{PropModelFolder}/{file}";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                report.Missing.Add($"props - no model at {modelPath}; run tools/prep-props.sh");
                return null;
            }

            var root = new GameObject($"Prop_{kind}");
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            visual.name = "Visual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;

            foreach (var animator in visual.GetComponentsInChildren<Animator>(true))
                Object.DestroyImmediate(animator);

            // The .glb may bring its own colliders in future re-exports; ours is the only one.
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);

            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                report.Missing.Add($"props - {file} has no renderer");
                Object.DestroyImmediate(root);
                return null;
            }

            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            var box = root.AddComponent<BoxCollider>();
            var size = bounds.size;
            size.x *= footprintScale;
            size.z *= footprintScale;
            box.size = size;
            box.center = bounds.center;

            var body = root.AddComponent<Rigidbody>();
            body.mass = mass;
            body.linearDamping = 0.05f;
            body.angularDamping = 0.6f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.centerOfMass = new Vector3(bounds.center.x, bounds.size.y * comFraction, bounds.center.z);
            body.sleepThreshold = 0.01f;

            root.AddComponent<StreetProp>().Configure(kind);

            root.layer = layer;
            foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;

            // Cull like the poles: one LOD, gone past `cullDistance`. The formula is
            // BuildPoleTemplate's - relative screen height of the object at that distance.
            var lod = root.AddComponent<LODGroup>();
            float fov = Camera.main != null ? Camera.main.fieldOfView : 75f;
            float screenHeight = bounds.size.y / (2f * cullDistance * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
            lod.SetLODs(new[] { new LOD(screenHeight, renderers) });
            lod.RecalculateBounds();

            if (options.CompressedTextures) ApplyCompressedTextures(root, report);

            if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
            if (!AssetDatabase.IsValidFolder(PropPrefabFolder)) AssetDatabase.CreateFolder("Assets/Prefabs", "Props");
            string path = $"{PropPrefabFolder}/{kind}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            report.Notes.Add(
                $"props: {kind} prefab from {file} - {bounds.size.x:0.00} × {bounds.size.y:0.00} × " +
                $"{bounds.size.z:0.00} m, {mass:0} kg, culled past {cullDistance:0} m");
            return prefab;
        }

        // --- layout ------------------------------------------------------------------------------

        private static List<PropSystem.Entry> BakeLayout(
            Transform root, TheBlockConfig.Root config, TheBlockConfig.LightsSpec lights, Report report)
        {
            var layout = new List<PropSystem.Entry>();
            Physics.SyncTransforms();

            BakeBins(layout, lights, report);

            var network = AssetDatabase.LoadAssetAtPath<TrafficNetwork>($"{GeneratedTrafficFolder}/TrafficNetwork.asset");
            if (network == null || network.Edges == null || network.Edges.Length == 0)
            {
                report.Warnings.Add("props: no baked TrafficNetwork - cones and benches need the kerb line; run Build World");
                return layout;
            }

            var places = root.Find("Places");
            var anchors = new List<(string Name, Vector3 Centre, bool Cones, bool Benches)>();
            void Anchor(string objectName, bool cones, bool benches)
            {
                var place = places != null ? places.Find(objectName) : null;
                if (place == null)
                {
                    report.Warnings.Add($"props: no {objectName} in the scene - nothing placed there");
                    return;
                }
                anchors.Add((objectName, FootprintCentre(place), cones, benches));
            }

            // NOT the police station: its kerb is the cruisers' bays. The cops spawn there at
            // runtime - after this bake's overlap test - and the first Play sample found four cones
            // and two benches standing inside parked cruisers, awake from frame one. A cone in the
            // bay would also be a thing for a cop to wedge on leaving (memory:
            // cop-wedges-leaving-the-station-bay). And NOT the spawn car park: it is 80 m off the
            // graph, so "in front of" has no kerb to answer with.
            Anchor("Place_GasStation", cones: true, benches: true);
            Anchor(AutoShopSpec.ObjectName, cones: true, benches: true);
            Anchor("Place_SevenEleven", cones: false, benches: true);
            Anchor("Place_PizzaPlace", cones: false, benches: true);

            foreach (var anchor in anchors)
            {
                if (!NearestKerb(network, anchor.Centre, lights.SideOffset, out var kerb, out var along, out var outward))
                {
                    report.Warnings.Add($"props: {anchor.Name} is nowhere near a street - skipped");
                    continue;
                }

                // `outward` points from the road toward the place. A bench faces the road, so it
                // looks along -outward; a cone has no face.
                float faceYaw = Quaternion.LookRotation(-outward, Vector3.up).eulerAngles.y;

                if (anchor.Cones)
                {
                    int placed = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        float s = (i - 1.5f) * ConeSpacing;
                        var p = kerb + along * s + outward * ConeKerbOffset;
                        if (TryPlace(layout, PropKind.Cone, p, faceYaw + 37f * i, new Vector3(0.5f, 0.74f, 0.5f), report))
                            placed++;
                    }
                    report.Notes.Add($"props: {anchor.Name} - {placed}/4 cones on the kerb");
                }

                if (anchor.Benches)
                {
                    int placed = 0;
                    foreach (float s in new[] { -BenchSpread, BenchSpread })
                    {
                        var p = kerb + along * s + outward * BenchInset;
                        if (TryPlace(layout, PropKind.Bench, p, faceYaw, new Vector3(1.3f, 0.45f, 0.56f), report))
                            placed++;
                    }
                    report.Notes.Add($"props: {anchor.Name} - {placed}/2 benches on the pavement");
                }
            }

            return layout;
        }

        /// <summary>
        /// One bin per lit intersection, beside the first pole of the junction. The pole is at the
        /// kerb already; the pavement is <see cref="BinInset"/> further from the road, which is
        /// -right of the approach (the pole stands right of the lane and faces back down it).
        /// </summary>
        private static void BakeBins(List<PropSystem.Entry> layout, TheBlockConfig.LightsSpec lights, Report report)
        {
            var poles = Object.FindObjectsByType<TrafficLightPole>(FindObjectsSortMode.None);
            if (poles.Length == 0)
            {
                report.Warnings.Add("props: no traffic-light poles in the scene - no bins; run Build World");
                return;
            }

            var yawTune = Convert.RotFromRadians(lights.ModelYawTune);
            var seen = new HashSet<string>();
            int placed = 0, junctions = 0;

            foreach (var pole in poles.OrderBy(p => p.name, System.StringComparer.Ordinal))
            {
                // Light_{node:000}_{edge:000} - one bin per node.
                var parts = pole.name.Split('_');
                if (parts.Length < 3) continue;
                if (!seen.Add(parts[1])) continue;
                junctions++;

                var facing = pole.transform.rotation * Quaternion.Inverse(yawTune) * Vector3.forward; // = -tangent
                var tangent = -facing;
                var outward = Vector3.Cross(Vector3.up, tangent);   // right of the approach = away from the road
                var p = pole.transform.position + outward * BinInset + facing * BinUpstream;

                if (TryPlace(layout, PropKind.Bin, p, pole.transform.eulerAngles.y, new Vector3(0.4f, 0.8f, 0.4f), report))
                    placed++;
            }

            report.Notes.Add($"props: {placed}/{junctions} lit intersections got a bin");
        }

        /// <summary>
        /// Snaps a candidate to the ground and rejects it if the ground is missing or something
        /// solid already stands there. The overlap box is the prop's own footprint, shrunk and
        /// lifted clear of the pavement, so a building wall, a parked car or a pole is a hit and
        /// the ground never is.
        /// </summary>
        private static bool TryPlace(
            List<PropSystem.Entry> layout, PropKind kind, Vector3 at, float yaw, Vector3 size, Report report)
        {
            float y = GroundY(at);
            if (y < -1f || y > 3f)
            {
                report.Warnings.Add($"props: {kind} at {Fmt(at)} dropped - no plausible ground (y {y:0.00})");
                return false;
            }
            var position = new Vector3(at.x, y + 0.005f, at.z);
            var rotation = Quaternion.Euler(0f, yaw, 0f);

            var centre = position + rotation * new Vector3(0f, size.y * 0.55f, 0f);
            var half = new Vector3(size.x * 0.4f, size.y * 0.35f, size.z * 0.4f);
            if (Physics.CheckBox(centre, half, rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                report.Warnings.Add($"props: {kind} at {Fmt(position)} dropped - something solid is there");
                return false;
            }

            layout.Add(new PropSystem.Entry { Kind = kind, Position = position, Yaw = yaw });
            return true;
        }

        /// <summary>The XZ centre of a place's rendered footprint, at ground height.</summary>
        private static Vector3 FootprintCentre(Transform place)
        {
            var renderers = place.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return place.position;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return new Vector3(b.center.x, place.position.y, b.center.z);
        }

        /// <summary>
        /// The kerb point in front of <paramref name="from"/>: the nearest point on any street
        /// centreline, pushed <paramref name="sideOffset"/> toward the place. Also returns the
        /// street direction and the road→place normal, both horizontal unit vectors.
        /// </summary>
        private static bool NearestKerb(
            TrafficNetwork network, Vector3 from, float sideOffset,
            out Vector3 kerb, out Vector3 along, out Vector3 outward)
        {
            kerb = along = outward = Vector3.zero;
            float best = float.MaxValue;
            var flat = new Vector3(from.x, 0f, from.z);

            foreach (var edge in network.Edges)
            {
                if (edge?.Points == null || edge.Points.Length < 2) continue;
                for (int i = 0; i + 1 < edge.Points.Length; i++)
                {
                    var a = new Vector3(edge.Points[i].x, 0f, edge.Points[i].z);
                    var b = new Vector3(edge.Points[i + 1].x, 0f, edge.Points[i + 1].z);
                    var ab = b - a;
                    float len2 = ab.sqrMagnitude;
                    if (len2 < 1e-4f) continue;
                    float t = Mathf.Clamp01(Vector3.Dot(flat - a, ab) / len2);
                    var q = a + ab * t;
                    float d = (flat - q).sqrMagnitude;
                    if (d >= best) continue;

                    best = d;
                    along = ab.normalized;
                    var n = flat - q;
                    n.y = 0f;
                    outward = n.sqrMagnitude > 1e-4f ? n.normalized : Vector3.Cross(Vector3.up, along);
                    kerb = new Vector3(q.x, from.y, q.z) + outward * sideOffset;
                }
            }

            // A place more than 60 m from any street is not "in front of" anything.
            return best < 60f * 60f;
        }
    }
}
