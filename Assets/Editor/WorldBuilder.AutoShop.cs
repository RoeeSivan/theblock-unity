using System.Collections.Generic;
using TheBlock.Core;
using TheBlock.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// U35g's place: the auto shop, and the street that reaches it.
    ///
    /// <b>This is the first place in the port that the web build never had</b>, so its numbers are
    /// authored HERE, in Unity space, and not in <c>config.ts</c> - the original repo takes no change
    /// beyond the exporter, and there is nothing there to export. Every other place goes through
    /// <c>Convert.Pos</c>; this one does not, because it was never right-handed to begin with.
    ///
    /// <b>Where.</b> The corridor between Procedural City 4 (x −88..88, z 157..392) and Procedural
    /// City 6 (x −367..−119, z 120..317) is 31 m of bare ground plate with a road at each end -
    /// <c>Road_06</c> along z = 165 and <c>Road_08</c> along z = 326 - and nothing between them. The
    /// user picked it. The lot faces a new north-south street at x = −110 that joins the two.
    ///
    /// <b>The two new ribbons TILE the old ones flush rather than crossing them:</b> the roads have no
    /// collider and sit coplanar at <c>roads.y</c>, so an overlap is a z-fight. <c>Road_06</c>'s
    /// ribbon spans z 161..169, so the street starts at 169; <c>Road_08</c> ends at x = −77 and spans
    /// z 322..330, so the street runs to 330 and the stub runs from −77 to −106, where the street's
    /// own east edge (x −114..−106) takes over. Corner and T both close with no gap and no overlap.
    ///
    /// Built from the full <b>Build World</b> (roads pass and places pass, so the meshes are in the
    /// generated report and <c>SweepGenerated</c> keeps them) and on its own from
    /// <b>The Block → Build Auto Shop</b>, which is what a play-test uses.
    /// </summary>
    public static partial class WorldBuilder
    {
        // Position and yaw live in the runtime AutoShopSpec, because the map pins the same point.

        /// <summary>The model file, under Assets/Models.</summary>
        private const string AutoShopFile = "auto_shop.glb";

        /// <summary>Nodes that are paint, not walls - no collider.</summary>
        private static readonly List<string> AutoShopNoCollide = new()
        {
            "Static_Stain", "Static_YellowLine", "Static_Bulb", "Static_Tube", "Sign_AutoShop",
            "Mechanic_Lewis", // gets a capsule instead of a 14k-triangle mesh collider
        };

        /// <summary>The new street and its stub, Unity space, y filled from <c>roads.y</c>.</summary>
        private static readonly Vector3[][] AutoShopRoadPaths =
        {
            new[] { new Vector3(-110f, 0f, 169f), new Vector3(-110f, 0f, 330f) }, // street, S→N
            new[] { new Vector3(-77f, 0f, 326f), new Vector3(-106f, 0f, 326f) },  // stub to Road_08
        };

        private static readonly string[] AutoShopRoadNames = { "Road_AutoShop_Street", "Road_AutoShop_Stub" };

        [MenuItem("The Block/Build Auto Shop", priority = 9)]
        public static void BuildAutoShopOnly()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Build Auto Shop: stop Play mode first - a build in Play is discarded on exit.");
                return;
            }

            var root = Object.FindAnyObjectByType<WorldRoot>();
            if (root == null)
            {
                Debug.LogError("Build Auto Shop: no WorldRoot in the scene. Run The Block → Build World.");
                return;
            }

            var places = root.transform.Find("Places");
            var roads = root.transform.Find("Roads");
            if (places == null || roads == null)
            {
                Debug.LogError("Build Auto Shop: the world has no Places/Roads group. Run The Block → Build World.");
                return;
            }

            var snapshot = TheBlock.Core.TheBlockConfig.Load(reload: false);
            float roadY = snapshot?.Config?.Roads?.Y ?? 0.02f;

            var material = AssetDatabase.LoadAssetAtPath<Material>($"{GeneratedWorldFolder}/Road.mat");
            if (material == null)
            {
                Debug.LogError($"Build Auto Shop: {GeneratedWorldFolder}/Road.mat is missing. Run The Block → Build World.");
                return;
            }

            var report = new Report();
            var options = new Options();
            // The compressed-texture lookup is cached per build; without this a run before Compress
            // Textures remembers its misses and the next run reports them again.
            ResetTexturePass();

            // Idempotent: the previous run's objects go first, so the menu can be run after every
            // re-export without leaving a second lot or a second street behind.
            var old = places.Find(AutoShopSpec.ObjectName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            foreach (var name in AutoShopRoadNames)
            {
                var stale = roads.Find(name);
                if (stale != null) Object.DestroyImmediate(stale.gameObject);
            }

            BuildAutoShop(places, options, report);
            BuildAutoShopRoads(roads, material, roadY, report);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            var text = new System.Text.StringBuilder(
                $"Build Auto Shop - {report.Warnings.Count} warning(s), {report.Missing.Count} missing\n");
            foreach (var line in report.Placed) text.AppendLine("  " + line);
            foreach (var line in report.Notes) text.AppendLine("  " + line);
            foreach (var line in report.Warnings) text.AppendLine("  WARNING " + line);
            foreach (var line in report.Missing) text.AppendLine("  MISSING " + line);

            if (report.Warnings.Count > 0 || report.Missing.Count > 0) Debug.LogWarning(text.ToString().TrimEnd());
            else Debug.Log(text.ToString().TrimEnd());
        }

        /// <summary>Instantiates the lot. Called from the places pass and from the menu.</summary>
        private static GameObject BuildAutoShop(Transform places, Options options, Report report)
        {
            var instance = Instantiate($"/models/{AutoShopFile}", "Auto Shop", places, report, out _);
            if (instance == null) return null;

            instance.name = AutoShopSpec.ObjectName;
            instance.transform.SetPositionAndRotation(AutoShopSpec.Position, Quaternion.Euler(0f, AutoShopSpec.YawDeg, 0f));
            instance.transform.localScale = Vector3.one;

            ApplyCutoutMaterials(instance, report);
            if (options.CompressedTextures) ApplyCompressedTextures(instance, report);
            report.NoteTransparentMaterials(instance);

            if (options.Colliders)
            {
                AddColliders(instance, null, AutoShopNoCollide, null, report);

                var lewis = instance.transform.Find("Mechanic_Lewis");
                if (lewis != null && !lewis.TryGetComponent<CapsuleCollider>(out _))
                {
                    var capsule = lewis.gameObject.AddComponent<CapsuleCollider>();
                    capsule.center = new Vector3(0f, 0.9f, 0f);
                    capsule.radius = 0.3f;
                    capsule.height = 1.8f;
                }
            }

            // Static for lighting/occlusion, but the shutter animates and the mechanic may one day,
            // so neither is flagged. Batching stays off, as it does for every district.
            foreach (Transform child in instance.transform)
            {
                if (child.name == "Shutter" || child.name == "Mechanic_Lewis") continue;
                SetDistrictStaticFlags(child.gameObject);
            }

            report.Placed.Add(
                $"{instance.name} @ {Fmt(instance.transform.position)} yaw {instance.transform.eulerAngles.y:0.#}° " +
                $"({instance.GetComponentsInChildren<MeshFilter>(true).Length} meshes)");
            return instance;
        }

        /// <summary>The street and the stub, through the same spline/ribbon path as every config road.</summary>
        private static void BuildAutoShopRoads(Transform roads, Material material, float y, Report report)
        {
            float total = 0f;
            for (int i = 0; i < AutoShopRoadPaths.Length; i++)
            {
                var points = new List<Vector3>();
                foreach (var p in AutoShopRoadPaths[i]) points.Add(new Vector3(p.x, y, p.z));

                var road = new GameObject(AutoShopRoadNames[i]);
                road.transform.SetParent(roads, worldPositionStays: false);

                var container = road.AddComponent<SplineContainer>();
                container.Spline = BuildSpline(points);

                var mesh = BuildRibbon(container.Spline, out float length);
                if (mesh == null)
                {
                    report.Warnings.Add($"{AutoShopRoadNames[i]} skipped - shorter than one step");
                    Object.DestroyImmediate(road);
                    continue;
                }

                mesh.name = AutoShopRoadNames[i];
                var path = $"{GeneratedMeshFolder}/{mesh.name}.asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
                SaveGeneratedMesh(mesh, path, report);

                road.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = road.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                SetDistrictStaticFlags(road);
                total += length;
            }

            report.Placed.Add($"Auto shop street {AutoShopRoadPaths.Length} spline(s), {total:0} m, flush with Road_06 and Road_08");
        }
    }
}
