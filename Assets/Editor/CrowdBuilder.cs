using System.Collections.Generic;
using System.Linq;
using System.Text;
using TheBlock.Core;
using TheBlock.Npc;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Bakes where the crowd stands - <b>The Block → Bake Crowd Seeds</b>.
    ///
    /// The port of the original's three additive seeding passes (<c>crowd.ts</c>), run once at build
    /// time instead of once per boot:
    ///  1. <b>33 painted rectangles</b> × <c>perDistrict</c> 9 - hand-captured pavement, the bulk of
    ///     the crowd.
    ///  2. <b>38 sidewalk strips</b>, each split into two lanes <c>laneOffset</c> apart walked in
    ///     opposite directions, which is what makes a pavement read as two-way foot traffic.
    ///  3. <b>A per-district fallback</b> of 9 more, for the districts no rectangle covers.
    /// Zebra crossers are NOT baked - they hold a reference to a live <c>Crossing</c> and are built
    /// by <see cref="CrowdSpawner"/> at Start.
    ///
    /// <b>Run this last.</b> It queries the NavMesh to decide what is pavement, so it needs
    /// <c>Build World + NavMesh (slow)</c> to have run first.
    ///
    /// <b>Handedness, once, through <see cref="Convert"/>.</b> Every number in <c>npc.config.ts</c>
    /// is right-handed. Negating X swaps a rectangle's two X bounds - <c>xMin</c> becomes the Unity
    /// <c>xMax</c> - so both corners go through <see cref="Convert.Pos(Vector3)"/> and min/max are
    /// re-derived from the results. Never negate a field in place (port rule 1).
    ///
    /// The lane's sideways offset is <c>Cross(up, tangent)</c> on the CONVERTED endpoints. That is
    /// the same physical side as the web's <c>(-tz, tx)</c>: with web tangent <c>t</c>, converting
    /// its result gives <c>(t.z, 0, t.x)</c>, and <c>Cross(up, Convert(t))</c> gives <c>(t.z, 0,
    /// t.x)</c> too. The trap U17 hit is re-typing the web's FORMULA onto Unity vectors, which lands
    /// on the opposite side; converting its ANSWER is right.
    /// </summary>
    public static class CrowdBuilder
    {
        private const string AssetFolder = "Assets/Npc/Generated";
        private const string AssetPath = AssetFolder + "/CrowdSeeds.asset";

        /// <summary>Lane resampling. Fine enough that a sloped pavement is followed, coarse enough to be free.</summary>
        private const float LaneSampleM = 2f;

        /// <summary>How far a seed candidate may snap onto the mesh before it stops being that point.</summary>
        private const float SnapRadius = 0.75f;

        /// <summary>Rejection-sampling budget per person, from <c>walkable.ts</c>'s own 200.</summary>
        private const int SampleAttempts = 200;

        /// <summary>A candidate this far off the ground probe's answer is a roof, not a pavement.</summary>
        private const float RooftopBand = 1.5f;

        /// <summary>Points used to measure how many seeds are ever within the cull radius at once.</summary>
        private const int DensitySamples = 4000;

        [MenuItem("The Block/Bake Crowd Seeds", priority = 23)]
        public static void BuildMenu() => Build();

        public static string Build()
        {
            var snapshot = TheBlockConfig.Load(reload: true);
            var npc = snapshot?.Npc;
            if (npc == null)
            {
                const string message =
                    "CrowdBuilder: the config snapshot has no npcConfig. Run tools/export-config.sh.";
                Debug.LogError(message);
                return message;
            }

            var triangulation = NavMesh.CalculateTriangulation();
            if (triangulation.vertices == null || triangulation.vertices.Length == 0)
            {
                const string message =
                    "CrowdBuilder: there is no NavMesh in this scene, so there is no pavement to seed " +
                    "onto. Run The Block → Build World + NavMesh (slow) first.";
                Debug.LogError(message);
                return message;
            }

            var log = new StringBuilder();
            var rng = new System.Random(20260816);

            var rects = new List<CrowdSeedTable.Rect>();
            var paths = new List<CrowdSeedTable.LanePath>();
            var seeds = new List<CrowdSeedTable.Seed>();
            int faceCount = Mathf.Max(1, npc.People?.Count ?? 1);
            int face = 0;

            // --- 1. painted rectangles ----------------------------------------------------------

            int wanted = 0;
            int placed = 0;

            foreach (var painted in npc.PaintedZones)
            {
                var rect = ToUnity(painted);
                int rectId = rects.Count;
                rects.Add(rect);

                int count = npc.PerDistrict;
                wanted += count;

                for (int i = 0; i < count; i++)
                {
                    if (!TrySampleIn(rect, rng, out var point)) break;

                    seeds.Add(Wander(point, rectId, face++ % faceCount, Speed(npc, rng)));
                    placed++;
                }
            }

            log.AppendLine($"  painted zones  {npc.PaintedZones.Count} rect(s) → {placed}/{wanted} seeded");

            // Manual mode: a non-empty `zones` replaces the district fallback entirely, which is the
            // switch the web build's dev paint tool flips. Empty in the original, kept so it works.
            int manual = 0;
            foreach (var zone in npc.Zones)
            {
                var rect = ToUnity(zone);
                int rectId = rects.Count;
                rects.Add(rect);

                int count = zone.Count ?? npc.DefaultPerZone;
                for (int i = 0; i < count; i++)
                {
                    if (!TrySampleIn(rect, rng, out var point)) break;
                    seeds.Add(Wander(point, rectId, face++ % faceCount, Speed(npc, rng)));
                    manual++;
                }
            }

            if (npc.Zones.Count > 0)
                log.AppendLine($"  manual zones   {npc.Zones.Count} rect(s) → {manual} seeded (district pass skipped)");

            // --- 2. per-district fallback -------------------------------------------------------

            int districts = 0;
            int districtSeeds = 0;

            if (npc.Zones.Count == 0)
            {
                foreach (var bounds in DistrictBounds())
                {
                    districts++;
                    var rect = new CrowdSeedTable.Rect
                    {
                        MinX = bounds.min.x,
                        MaxX = bounds.max.x,
                        MinZ = bounds.min.z,
                        MaxZ = bounds.max.z,
                    };

                    for (int i = 0; i < npc.PerDistrict; i++)
                    {
                        if (!TrySampleIn(rect, rng, out var point)) break;

                        // RectId −1: these are the only truly free-roaming people, exactly as in the
                        // web build's auto branch.
                        seeds.Add(Wander(point, -1, face++ % faceCount, Speed(npc, rng)));
                        districtSeeds++;
                    }
                }

                log.AppendLine($"  districts      {districts} → {districtSeeds} seeded (free-roaming)");
            }

            // --- 3. sidewalk strips -------------------------------------------------------------

            int walkers = 0;
            float laneMetres = 0f;

            foreach (var strip in npc.Strips)
            {
                var a = Convert.Pos(strip.A.Raw);
                var b = Convert.Pos(strip.B.Raw);

                var tangent = b - a;
                tangent.y = 0f;
                if (tangent.sqrMagnitude < 0.01f) continue;

                var side = Vector3.Cross(Vector3.up, tangent.normalized) * npc.LaneOffset;

                int count = strip.Count ?? npc.PerStrip;
                int half = Mathf.CeilToInt(count / 2f);

                for (int lane = 0; lane < 2; lane++)
                {
                    float sign = lane == 0 ? 1f : -1f;
                    var path = Resample(a + side * sign, b + side * sign);
                    int pathId = paths.Count;
                    paths.Add(path);
                    laneMetres += path.Length;

                    int on = lane == 0 ? half : count - half;
                    for (int i = 0; i < on; i++)
                    {
                        // Spread along the lane with a little jitter, so a strip is a stream of
                        // people rather than a queue that sets off together.
                        float s = (i + (float)rng.NextDouble() * 0.5f) / Mathf.Max(1, on) * path.Length;

                        seeds.Add(new CrowdSeedTable.Seed
                        {
                            Position = path.At(s),
                            S = s,
                            Dir = (sbyte)(lane == 0 ? 1 : -1),
                            Face = (byte)(face++ % faceCount),
                            Speed = Speed(npc, rng),
                            Mode = CrowdSeedTable.Mode.Strip,
                            RectId = -1,
                            PathId = pathId,
                        });
                        walkers++;
                    }
                }
            }

            log.AppendLine(
                $"  strips         {npc.Strips.Count} → {paths.Count} lane(s), {laneMetres:0} m, " +
                $"{paths.Sum(p => p.Points.Length)} LUT point(s), {walkers} walker(s)");

            // --- density ------------------------------------------------------------------------

            var (peak, p95) = MeasureDensity(seeds, npc.CullDistanceM, rng);
            log.AppendLine(
                $"  within {npc.CullDistanceM:0} m    peak {peak}, p95 {p95} " +
                "- set the Crowd object's Live Cap from this");

            // --- write --------------------------------------------------------------------------

            if (!AssetDatabase.IsValidFolder("Assets/Npc")) AssetDatabase.CreateFolder("Assets", "Npc");
            if (!AssetDatabase.IsValidFolder(AssetFolder)) AssetDatabase.CreateFolder("Assets/Npc", "Generated");

            var table = AssetDatabase.LoadAssetAtPath<CrowdSeedTable>(AssetPath);
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<CrowdSeedTable>();
                AssetDatabase.CreateAsset(table, AssetPath);
            }

            table.Fill(rects.ToArray(), paths.ToArray(), seeds.ToArray(), snapshot.NpcSourceSha256, peak, p95);
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();

            string wiring = WireScene(table, peak);

            var report =
                $"CrowdBuilder - {seeds.Count} seed(s), {rects.Count} rect(s), {paths.Count} lane(s)\n" +
                log + wiring +
                "\n  crossers are NOT baked: they hold a live Crossing and are built at Start (2 per zebra)";
            Debug.Log(report, table);
            return report;
        }

        // --- helpers ---------------------------------------------------------------------------

        private static CrowdSeedTable.Seed Wander(Vector3 point, int rectId, int face, float speed) =>
            new()
            {
                Position = point,
                Target = point,
                S = 0f,
                Dir = 1,
                Face = (byte)face,
                Speed = speed,
                Mode = CrowdSeedTable.Mode.Wander,
                RectId = rectId,
                PathId = -1,
            };

        private static float Speed(TheBlockConfig.NpcSpec npc, System.Random rng) =>
            Mathf.Lerp(npc.Speed.Min, npc.Speed.Max, (float)rng.NextDouble());

        /// <summary>
        /// Both corners converted, then min/max re-derived. Worked example, because getting this
        /// wrong mirrors the crowd against the city: web <c>{xMin −177.4, xMax −129.8}</c> becomes
        /// Unity <c>{xMin 129.8, xMax 177.4}</c>.
        /// </summary>
        private static CrowdSeedTable.Rect ToUnity(TheBlockConfig.NpcRectSpec rect)
        {
            var a = Convert.Pos(new Vector3(rect.XMin, 0f, rect.ZMin));
            var b = Convert.Pos(new Vector3(rect.XMax, 0f, rect.ZMax));

            return new CrowdSeedTable.Rect
            {
                MinX = Mathf.Min(a.x, b.x),
                MaxX = Mathf.Max(a.x, b.x),
                MinZ = Mathf.Min(a.z, b.z),
                MaxZ = Mathf.Max(a.z, b.z),
            };
        }

        /// <summary>
        /// A point on pavement inside the rectangle - the port of <c>walkable.ts</c>'s
        /// <c>sampleInBox</c>, with the mask replaced by the NavMesh and one extra guard the web
        /// build did not need: rooftops bake walkable, so a hit far off the ground probe's answer is
        /// a roof and is rejected (memory: <c>navmesh-bakes-rooftops</c>).
        /// </summary>
        private static bool TrySampleIn(CrowdSeedTable.Rect rect, System.Random rng, out Vector3 point)
        {
            for (int attempt = 0; attempt < SampleAttempts; attempt++)
            {
                var candidate = new Vector3(
                    Mathf.Lerp(rect.MinX, rect.MaxX, (float)rng.NextDouble()),
                    0f,
                    Mathf.Lerp(rect.MinZ, rect.MaxZ, (float)rng.NextDouble()));

                if (!NavMesh.SamplePosition(candidate, out var hit, SnapRadius, NavMesh.AllAreas)) continue;

                float ground = WorldBuilder.GroundY(hit.position);
                if (Mathf.Abs(hit.position.y - ground) > RooftopBand) continue;

                point = new Vector3(hit.position.x, ground, hit.position.z);
                return true;
            }

            point = Vector3.zero;
            return false;
        }

        /// <summary>A straight lane, resampled so every sample carries its own baked ground height.</summary>
        private static CrowdSeedTable.LanePath Resample(Vector3 a, Vector3 b)
        {
            float length = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z));
            int samples = Mathf.Max(2, Mathf.CeilToInt(length / LaneSampleM) + 1);

            var points = new Vector3[samples];
            for (int i = 0; i < samples; i++)
            {
                var flat = Vector3.Lerp(a, b, i / (float)(samples - 1));
                points[i] = new Vector3(flat.x, WorldBuilder.GroundY(flat), flat.z);
            }

            return new CrowdSeedTable.LanePath { Points = points, Length = Mathf.Max(length, 0.01f) };
        }

        /// <summary>Every district's world bounds - the port of <c>worldMap.districts()</c>.</summary>
        private static IEnumerable<Bounds> DistrictBounds()
        {
            var root = GameObject.Find("World/Districts");
            if (root == null) yield break;

            foreach (Transform district in root.transform)
            {
                var renderers = district.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) continue;

                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                yield return bounds;
            }
        }

        /// <summary>
        /// How many people are ever inside the cull radius at once. This is what <c>liveCap</c> has
        /// to cover, and knowing it BEFORE the first Play is the difference between tuning the pool
        /// by measurement and tuning it by feel.
        /// </summary>
        private static (int Peak, int P95) MeasureDensity(
            List<CrowdSeedTable.Seed> seeds, float cull, System.Random rng)
        {
            if (seeds.Count == 0) return (0, 0);

            float minX = seeds.Min(s => s.Position.x);
            float maxX = seeds.Max(s => s.Position.x);
            float minZ = seeds.Min(s => s.Position.z);
            float maxZ = seeds.Max(s => s.Position.z);
            float cullSqr = cull * cull;

            var counts = new List<int>(DensitySamples);
            for (int i = 0; i < DensitySamples; i++)
            {
                var at = new Vector3(
                    Mathf.Lerp(minX, maxX, (float)rng.NextDouble()), 0f,
                    Mathf.Lerp(minZ, maxZ, (float)rng.NextDouble()));

                int near = 0;
                foreach (var seed in seeds)
                {
                    float dx = seed.Position.x - at.x;
                    float dz = seed.Position.z - at.z;
                    if (dx * dx + dz * dz <= cullSqr) near++;
                }

                counts.Add(near);
            }

            counts.Sort();
            return (counts[^1], counts[Mathf.Clamp((int)(counts.Count * 0.95f), 0, counts.Count - 1)]);
        }

        /// <summary>
        /// Points the scene's Crowd object at what was just built.
        ///
        /// Ends on <c>MarkSceneDirty</c>: a script that changes a scene object without it leaves the
        /// scene looking clean, so Save writes nothing and reports success (memory:
        /// <c>editor-created-objects-need-markscenedirty</c>).
        /// </summary>
        private static string WireScene(CrowdSeedTable table, int peak)
        {
            var crowd = GameObject.Find("Crowd");
            if (crowd == null || !crowd.TryGetComponent<CrowdSpawner>(out var spawner))
                return "\n  ⚠ no Crowd object with a CrowdSpawner in this scene - wire it by hand";

            var prefabs = PeopleImporter.Names
                .Select(n => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Npc/Ped_{n}.prefab"))
                .Where(p => p != null)
                .ToList();

            var serialized = new SerializedObject(spawner);
            serialized.FindProperty("seedTable").objectReferenceValue = table;

            var list = serialized.FindProperty("pedestrianPrefabs");
            list.arraySize = prefabs.Count;
            for (int i = 0; i < prefabs.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = prefabs[i];

            // Cover the measured peak with a little headroom, then let the user tune it down.
            serialized.FindProperty("liveCap").intValue = Mathf.Clamp(peak + 16, 32, 400);

            // Everything except the pedestrians themselves is ground.
            serialized.FindProperty("groundMask").intValue = ~(1 << LayerMask.NameToLayer(NpcBuilder.PedestrianLayer));

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(spawner);
            EditorSceneManager.MarkSceneDirty(crowd.scene);

            return $"\n  wired Crowd: {prefabs.Count} prefab(s), liveCap {serialized.FindProperty("liveCap").intValue}";
        }
    }
}
