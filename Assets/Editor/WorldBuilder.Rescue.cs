using System.Collections.Generic;
using TheBlock.Core;
using TheBlock.Missions;
using TheBlock.UI;
using UnityEditor;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Bakes the rooftops U23's rescue strands people on — <b>The Block → Bake Roof Spots</b>.
    ///
    /// The web finds them at runtime with up to 400 random raycasts per run; this finds them once
    /// and writes a ScriptableObject, the same move U17 made for the traffic graph and U19 for the
    /// police route graph. The runtime then casts nothing.
    ///
    /// <b>Getting the cast right is the whole job, and this project has two memories about getting
    /// it wrong.</b> <c>lowest-raycast-hit-is-the-ground-plate</c>: there is a plate at −0.05 under
    /// every street, so the LOWEST hit is never what you want. <c>groundy-lowest-hit-can-be-a-roof</c>:
    /// where a district has no street mesh, the building overhead is the lowest non-plate hit. Here
    /// the answer is the opposite of both — cast down from above the city and take the FIRST hit,
    /// which is by definition the topmost surface, then keep it only if it is high enough to be a
    /// roof rather than a street.
    /// </summary>
    public static partial class WorldBuilder
    {
        private const string RoofAssetPath = "Assets/Missions/Generated/RoofSpots.asset";

        /// <summary>Above anything in the city, so the first hit is always the top of something.</summary>
        private const float CastFrom = 400f;

        [MenuItem("The Block/Bake Roof Spots", priority = 28)]
        public static void BakeRoofSpots()
        {
            var snapshot = TheBlockConfig.Load(true);
            var rescue = snapshot?.Rescue;
            if (rescue == null)
            {
                Debug.LogError("BakeRoofSpots: config has no rescueConfig.");
                return;
            }

            // Read the districts out of the SCENE, not out of MapRegistry: the registry is filled
            // at runtime and is empty in the editor, and the scene objects are the very geometry the
            // raycasts below will hit — so this measures the thing it is about to sample.
            var districts = SceneDistricts();
            if (districts.Count == 0)
            {
                Debug.LogError(
                    "BakeRoofSpots: no District_* objects under World/Districts. Run " +
                    "The Block → Build World first — this bake reads the placed city.");
                return;
            }

            var spots = new List<RoofSpots.Spot>();
            var rng = new System.Random(20260816);
            var report = new System.Text.StringBuilder("BakeRoofSpots\n");

            // Far more attempts than the four survivors a run needs. A bake is allowed to be
            // thorough where a per-frame sampler is not, and a fat pool is what lets the run pick a
            // DIFFERENT four each time without ever coming up short.
            //
            // The quota is PER DISTRICT, not global, and that is not a detail. A global cap is what
            // the first bake used, and it filled up on the first four districts it walked and left
            // Procedural City 5, 6, 7 and Reichman with zero — so every rescue would have sent the
            // player to the same corner of the map. A rescue that never crosses the city is not the
            // mission.
            var attempts = rescue.SampleAttempts * 8;
            var perDistrict = Mathf.Max(2, Mathf.CeilToInt(rescue.SurvivorCount * 12f / districts.Count));
            var minHeight = rescue.RoofMinHeight;
            var separation = rescue.MinSeparation;

            foreach (var district in districts)
            {
                var found = 0;
                for (var i = 0; i < attempts && found < perDistrict; i++)
                {
                    var x = Mathf.Lerp(district.Bounds.min.x, district.Bounds.max.x, (float)rng.NextDouble());
                    var z = Mathf.Lerp(district.Bounds.min.z, district.Bounds.max.z, (float)rng.NextDouble());

                    // FIRST hit from above = the topmost surface. Never the lowest, which is the
                    // ground plate, and never a "nearest to the ground" heuristic, which is the roof
                    // a street is missing under.
                    if (!Physics.Raycast(new Vector3(x, CastFrom, z), Vector3.down, out var hit,
                            CastFrom * 2f, ~0, QueryTriggerInteraction.Ignore))
                        continue;

                    if (hit.point.y < minHeight) continue; // street, or a low shed

                    // A roof you can set a helicopter down on has to be roughly level. A 30° cap
                    // rejects the sloped faces the districts are full of, which the web build has no
                    // way to test for and which is where a survivor ends up sliding off.
                    if (Vector3.Angle(hit.normal, Vector3.up) > 30f) continue;

                    if (TooClose(spots, x, z, separation)) continue;

                    spots.Add(new RoofSpots.Spot
                    {
                        Position = new Vector3(x, hit.point.y, z),
                        District = district.Name,
                    });
                    found++;
                }

                report.AppendLine(
                    $"  {district.Name,-28} {found,2} roof spots" +
                    (found == 0 ? "   ⚠ nothing high enough or level enough here" : string.Empty));
            }

            if (spots.Count < rescue.SurvivorCount)
            {
                Debug.LogError(
                    $"BakeRoofSpots: only {spots.Count} spots for {rescue.SurvivorCount} survivors. " +
                    $"Lower rescueConfig.roofMinHeight ({minHeight} m) or minSeparation ({separation} m).");
            }

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(RoofAssetPath)!);
            var asset = AssetDatabase.LoadAssetAtPath<RoofSpots>(RoofAssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<RoofSpots>();
                AssetDatabase.CreateAsset(asset, RoofAssetPath);
            }

            asset.SetSpots(spots);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            var min = float.MaxValue;
            var max = float.MinValue;
            foreach (var spot in spots)
            {
                min = Mathf.Min(min, spot.Position.y);
                max = Mathf.Max(max, spot.Position.y);
            }

            report.AppendLine(
                $"  TOTAL {spots.Count} spots, height {min:0.0}–{max:0.0} m, " +
                $"≥{minHeight} m and ≥{separation} m apart, ≤30° slope → {RoofAssetPath}");
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Every placed district and its renderer bounds.
        ///
        /// The car park is skipped for the same reason <c>WorldBuilder.Navigation</c> excludes it
        /// from the NavMesh: it is one open slab at ground level with nothing on it that could be a
        /// roof, so every sample there is a rejection.
        /// </summary>
        private static List<MapDistrict> SceneDistricts()
        {
            var found = new List<MapDistrict>();
            var root = GameObject.Find("World");
            var group = root != null ? root.transform.Find("Districts") : null;
            if (group == null) return found;

            foreach (Transform district in group)
            {
                if (!district.name.StartsWith("District_")) continue;
                if (district.name.Contains("ParkingLot")) continue;

                var renderers = district.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) continue;

                var bounds = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                found.Add(new MapDistrict { Name = district.name, Bounds = bounds });
            }

            return found;
        }

        private static bool TooClose(List<RoofSpots.Spot> spots, float x, float z, float separation)
        {
            var sqr = separation * separation;
            foreach (var spot in spots)
            {
                var dx = spot.Position.x - x;
                var dz = spot.Position.z - z;
                if (dx * dx + dz * dz < sqr) return true;
            }

            return false;
        }
    }
}
