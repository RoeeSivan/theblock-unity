using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using TheBlock.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Convert = TheBlock.Core.Convert;
using Debug = UnityEngine.Debug;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Builds the world into the open scene from <c>theblock-config.json</c>.
    ///
    /// Re-runnable by design: it destroys its own root and rebuilds from scratch every time, so the
    /// scene is a pure function of the config plus the assets on disk. Nothing under the generated
    /// root should ever be hand-edited — the next build eats it.
    ///
    /// This is the ONLY place config positions become Unity transforms, and it does that through
    /// <see cref="Convert"/>. No sign flip is written inline here either.
    ///
    /// Not undoable. Re-running is the undo.
    /// </summary>
    public static class WorldBuilder
    {
        private const string RootName = "World";
        private const string FacadeMaterialPath = "Assets/Materials/City/Facade.mat";

        /// <summary>Scene roots the hand-built phase left behind, replaced by the generated world.</summary>
        private static readonly string[] LegacyRootPrefixes = { "District_", "Place_" };

        /// <summary>
        /// A stand-in for a model the web build has and this project does not, plus whatever it takes
        /// to make that stand-in sit where the original would have.
        ///
        /// The correction lives here rather than being baked into the asset on disk: the file stays
        /// exactly as it was downloaded, and the fix stays visible and re-runnable. Every use is
        /// called out in the build report, so a substitute never quietly passes for the real thing.
        /// </summary>
        private sealed class Substitute
        {
            /// <summary>File name as it exists under <c>Assets/Models</c>.</summary>
            public string File;

            /// <summary>Rotation applied in the model's OWN frame, before the config's yaw.</summary>
            public Vector3 ExtraEuler;

            /// <summary>Lift above the config position, to rest the model on the ground it actually meets.</summary>
            public float ExtraY;

            public string Note;
        }

        /// <summary>Keyed by the file name <c>config.ts</c> asks for.</summary>
        private static readonly Dictionary<string, Substitute> AssetAliases = new()
        {
            // Pizza Lila is hand-modelled and has no distributable source; this is a low-poly
            // substitute the user sourced. config's scale 1.6 and yaw 0 were tuned for the original.
            ["pizza-lila.glb"] = new Substitute
            {
                File = "low_poly_pizza_restaurant.glb",
                // The GLB's node chain leaves the model lying on its back — its local Y and Z end up
                // swapped, so the lamp post runs along Z instead of standing up. Rx(-90) rights it and
                // lands the base exactly on the model's own zero.
                ExtraEuler = new Vector3(-90f, 0f, 0f),
                // config says y=0, which was correct for the original. The pavement here is at 0.15.
                ExtraY = 0.15f,
                Note = "lies on its back out of the box; righted and lifted onto the pavement",
            },
        };

        [MenuItem("The Block/Build World", priority = 0)]
        public static void BuildWorldMenu() => Build(new Options());

        [MenuItem("The Block/Build World (no colliders)", priority = 1)]
        public static void BuildWorldNoCollidersMenu() => Build(new Options { Colliders = false });

        public class Options
        {
            public bool Districts = true;
            public bool Places = true;
            public bool Colliders = true;
        }

        public static string Build(Options options)
        {
            options ??= new Options();

            // Play mode throws the scene away on exit, so a build there is silently discarded.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                const string message = "WorldBuilder: stop Play mode first — a world built in play is discarded on exit.";
                Debug.LogError(message);
                return message;
            }

            var snapshot = TheBlockConfig.Load(reload: true);
            if (snapshot == null) return "Build aborted — config could not be loaded.";

            var stopwatch = Stopwatch.StartNew();
            var report = new Report();
            var scene = EditorSceneManager.GetActiveScene();

            var root = ResetRoot(scene, report);
            root.SourceSha256 = snapshot.SourceSha256;

            if (options.Districts)
            {
                var districts = NewGroup("Districts", root.transform);
                var city = snapshot.Config.City;
                if (city != null)
                {
                    BuildDistrict(
                        districts, city.Url, city.Name, city.Position, city.Scale,
                        hideMaterials: null, noCollidePatterns: city.NoCollidePatterns,
                        facadeMaterials: city.FacadeMaterials, options, report);
                }

                foreach (var district in snapshot.Config.Districts ?? new List<TheBlockConfig.DistrictSpec>())
                {
                    BuildDistrict(
                        districts, district.Url, district.Name, district.Position, district.Scale,
                        district.HideMaterials, district.NoCollidePatterns,
                        facadeMaterials: null, options, report);
                }
            }

            if (options.Places)
            {
                var places = NewGroup("Places", root.transform);
                BuildPlace(places, snapshot.Config.SevenEleven, "Seven Eleven", options, report);
                BuildPlace(places, snapshot.Config.PizzaPlace, "Pizza Place", options, report);
                BuildPlace(places, snapshot.Config.GasStation, "Gas Station", options, report);
                BuildPlace(places, snapshot.Config.PoliceStation, "Police Station", options, report);
            }

            stopwatch.Stop();
            var text = report.Compose(snapshot, stopwatch.Elapsed, options);
            root.LastBuildReport = text;

            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root.gameObject;

            if (report.Missing.Count > 0) Debug.LogWarning(text, root);
            else Debug.Log(text, root);
            return text;
        }

        // --- structure -------------------------------------------------------------------------

        /// <summary>Clears the previous build (and the hand-placed roots it supersedes), returns a fresh root.</summary>
        private static WorldRoot ResetRoot(UnityEngine.SceneManagement.Scene scene, Report report)
        {
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.GetComponent<WorldRoot>() != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                    continue;
                }

                if (LegacyRootPrefixes.Any(prefix => go.name.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    report.RemovedLegacy.Add(go.name);
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            var root = new GameObject(RootName);
            return root.AddComponent<WorldRoot>();
        }

        private static Transform NewGroup(string name, Transform parent)
        {
            var group = new GameObject(name);
            group.transform.SetParent(parent, worldPositionStays: false);
            return group.transform;
        }

        // --- placement -------------------------------------------------------------------------

        private static void BuildDistrict(
            Transform parent, string url, string name, TheBlockConfig.Vec3 position, float scale,
            List<string> hideMaterials, List<string> noCollidePatterns, List<string> facadeMaterials,
            Options options, Report report)
        {
            var instance = Instantiate(url, name, parent, report, out var substitute);
            if (instance == null) return;

            instance.name = $"District_{Sanitize(name)}";
            instance.transform.position = Convert.Pos(position.Raw) + Vector3.up * (substitute?.ExtraY ?? 0f);
            instance.transform.rotation = Quaternion.Euler(substitute?.ExtraEuler ?? Vector3.zero);
            instance.transform.localScale = Vector3.one * (scale <= 0f ? 1f : scale);
            SetDistrictStaticFlags(instance);

            if (facadeMaterials != null) ApplyFacadeMaterial(instance, facadeMaterials, report);
            if (hideMaterials != null) HideByMaterial(instance, hideMaterials, report);
            if (options.Colliders) AddColliders(instance, noCollidePatterns, null, null, report);

            report.Placed.Add($"{instance.name} @ {Fmt(instance.transform.position)}");
        }

        private static void BuildPlace(
            Transform parent, TheBlockConfig.PlaceSpec place, string label, Options options, Report report)
        {
            if (place == null) return;

            var instance = Instantiate(place.Url, label, parent, report, out var substitute);
            if (instance == null) return;

            instance.name = $"Place_{Sanitize(label)}";
            instance.transform.position =
                Convert.Pos(place.Position.Raw) + Vector3.up * (substitute?.ExtraY ?? 0f);
            // The stand-in's correction goes on the right, so it stays in the model's own frame and
            // the config's yaw still means the same thing it means for the real asset.
            instance.transform.rotation =
                Convert.RotFromRadians(place.Yaw) * Quaternion.Euler(substitute?.ExtraEuler ?? Vector3.zero);
            instance.transform.localScale = Vector3.one * (place.Scale <= 0f ? 1f : place.Scale);

            HideCollisionProxies(instance, report);

            // hideNodes names parts of the ORIGINAL model. A stand-in that happens to share a node
            // name would lose a piece it needs — the pizza substitute's lamp post is called
            // PizzaLight, the same name the original build hides.
            if (substitute != null)
                report.Notes.Add($"{instance.name}: hideNodes skipped — they name the original model's parts");
            else if (place.HideNodes != null)
                HideByNode(instance, place.HideNodes, report);
            if (options.Colliders)
                AddColliders(instance, null, place.NoCollideNodes, place.CollideMaxY, report);

            report.Placed.Add(
                $"{instance.name} @ {Fmt(instance.transform.position)} yaw {instance.transform.eulerAngles.y:0.#}°");
        }

        /// <summary>
        /// Resolves a web URL like <c>/models/optimized/split/procedural-city-4.glb</c> to an asset in
        /// this project by file name. The web build's folder layout (optimized/, split/) is a download
        /// concern that does not exist here, so only the base name is matched.
        /// </summary>
        private static GameObject Instantiate(
            string url, string label, Transform parent, Report report, out Substitute substitute)
        {
            substitute = null;
            if (string.IsNullOrEmpty(url))
            {
                report.Missing.Add($"{label} — config has no url");
                return null;
            }

            var fileName = url.Substring(url.LastIndexOf('/') + 1);
            if (AssetAliases.TryGetValue(fileName, out substitute))
            {
                report.Warnings.Add(
                    $"{label} — stand-in {substitute.File} for {fileName}: {substitute.Note}");
                fileName = substitute.File;
            }

            var bareName = System.IO.Path.GetFileNameWithoutExtension(fileName);

            var assetPath = AssetDatabase
                .FindAssets($"{bareName} t:GameObject", new[] { "Assets/Models" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(path =>
                    string.Equals(System.IO.Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));

            if (assetPath == null)
            {
                report.Missing.Add($"{label} — no asset named {fileName} under Assets/Models");
                return null;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                report.Missing.Add($"{label} — {assetPath} did not load as a GameObject");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            return instance;
        }

        // --- material and node passes ----------------------------------------------------------

        /// <summary>
        /// Rebinds the downtown facade slots to <c>Facade.mat</c>.
        ///
        /// The web build recolours those materials in code at load because it cannot author material
        /// assets; Unity can, so the tint is an asset (decision U1). Matching is by the imported
        /// material's name against <c>city.facadeMaterials</c>, which is what the web build matches too.
        /// </summary>
        private static void ApplyFacadeMaterial(GameObject instance, List<string> facadeMaterials, Report report)
        {
            var facade = AssetDatabase.LoadAssetAtPath<Material>(FacadeMaterialPath);
            if (facade == null)
            {
                report.Warnings.Add($"facade tint skipped — {FacadeMaterialPath} not found");
                return;
            }

            var rebound = 0;
            foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null || !facadeMaterials.Contains(materials[i].name)) continue;
                    materials[i] = facade;
                    changed = true;
                    rebound++;
                }

                if (changed) renderer.sharedMaterials = materials;
            }

            if (rebound == 0)
                report.Warnings.Add(
                    $"facade tint matched nothing — looked for [{string.Join(", ", facadeMaterials)}]");
            else
                report.Notes.Add($"facade tint bound to {rebound} material slot(s)");
        }

        /// <summary>
        /// Hides the districts' baked-in parked cars. A renderer is only disabled when EVERY slot on
        /// it is a hidden material — a partial match would take real geometry with it, so those are
        /// reported instead of guessed at.
        /// </summary>
        private static void HideByMaterial(GameObject instance, List<string> hideMaterials, Report report)
        {
            if (hideMaterials.Count == 0) return;

            int hidden = 0, partial = 0;
            foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                var materials = renderer.sharedMaterials;
                if (materials.Length == 0) continue;

                var matches = materials.Count(m => m != null && hideMaterials.Contains(m.name));
                if (matches == 0) continue;

                if (matches == materials.Length)
                {
                    renderer.gameObject.SetActive(false);
                    hidden++;
                }
                else
                {
                    partial++;
                }
            }

            if (hidden > 0) report.Notes.Add($"{instance.name}: hid {hidden} baked-car renderer(s)");
            if (partial > 0)
                report.Warnings.Add(
                    $"{instance.name}: {partial} renderer(s) mix baked cars with real geometry — left visible, needs a submesh split");
        }

        /// <summary>
        /// Hides the crude collision proxy that Sketchfab-sourced props ship alongside their real
        /// geometry — a node named <c>Collider</c> holding a coarse box, meant to be collided with
        /// and never drawn.
        ///
        /// Imported straight, it renders as a grey slab swallowing the prop, and it is the first
        /// thing a downward raycast hits, so ground probes read its roof instead of the pavement.
        /// Hiding it also drops it from the collider pass, which only looks at active meshes — and
        /// the real geometry makes a better collider than the box did.
        /// </summary>
        private static void HideCollisionProxies(GameObject instance, Report report)
        {
            var hidden = new List<string>();
            foreach (var child in instance.GetComponentsInChildren<Transform>(true))
            {
                if (!child.name.StartsWith("Collider", StringComparison.OrdinalIgnoreCase)) continue;
                child.gameObject.SetActive(false);
                hidden.Add(child.name);
            }

            if (hidden.Count > 0)
                report.Notes.Add($"{instance.name}: hid collision prox{(hidden.Count == 1 ? "y" : "ies")} {string.Join(", ", hidden)}");
        }

        private static void HideByNode(GameObject instance, List<string> hideNodes, Report report)
        {
            if (hideNodes.Count == 0) return;

            var hidden = new List<string>();
            foreach (var child in instance.GetComponentsInChildren<Transform>(true))
            {
                if (!hideNodes.Contains(child.name)) continue;
                child.gameObject.SetActive(false);
                hidden.Add(child.name);
            }

            if (hidden.Count > 0)
                report.Notes.Add($"{instance.name}: hid node(s) {string.Join(", ", hidden)}");
        }

        // --- colliders -------------------------------------------------------------------------

        /// <summary>
        /// Gives every visible mesh a non-convex MeshCollider, minus the exclusions the config asks
        /// for: foliage by name substring, specific nodes by exact name, and anything topping out
        /// above <paramref name="collideMaxY"/>.
        ///
        /// Note that a district whose GLB is one merged mesh (downtown) has no node names to match,
        /// so its foliage collides — the web build has the same hole. That is faithful, not a bug.
        /// </summary>
        private static void AddColliders(
            GameObject instance, List<string> noCollidePatterns, List<string> noCollideNodes,
            float? collideMaxY, Report report)
        {
            int added = 0, skipped = 0;

            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || !filter.gameObject.activeInHierarchy) continue;

                if (noCollideNodes != null && noCollideNodes.Contains(filter.name)) { skipped++; continue; }
                if (MatchesAny(filter, noCollidePatterns)) { skipped++; continue; }

                if (collideMaxY.HasValue)
                {
                    var renderer = filter.GetComponent<Renderer>();
                    if (renderer != null && renderer.bounds.max.y > collideMaxY.Value) { skipped++; continue; }
                }

                // Not `??` — UnityEngine.Object overloads `==` for destroyed objects, and `??`
                // bypasses that overload, so it hands back a fake-null and the next line throws.
                if (!filter.gameObject.TryGetComponent<MeshCollider>(out var collider))
                    collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.convex = false;
                added++;
            }

            report.Colliders += added;
            if (skipped > 0) report.Notes.Add($"{instance.name}: {added} collider(s), {skipped} skipped");
            else report.Notes.Add($"{instance.name}: {added} collider(s)");
        }

        /// <summary>
        /// True when this mesh is foliage and should carry no collider.
        ///
        /// The node name decides it outright. Material names only decide it when EVERY material on
        /// the renderer is foliage — the district GLBs are merged meshes carrying a dozen submeshes
        /// each, so "any material matches" would strip collision from a whole district because one
        /// of its submeshes is a tree. A mixed mesh collides, palms included; that is what downtown
        /// does and what the web build does.
        /// </summary>
        private static bool MatchesAny(MeshFilter filter, List<string> patterns)
        {
            if (patterns == null || patterns.Count == 0) return false;
            if (ContainsAny(filter.name, patterns)) return true;

            var renderer = filter.GetComponent<Renderer>();
            var materials = renderer == null ? null : renderer.sharedMaterials;
            if (materials == null || materials.Length == 0) return false;

            foreach (var material in materials)
            {
                if (material == null || !ContainsAny(material.name, patterns)) return false;
            }

            return true;
        }

        private static bool ContainsAny(string text, List<string> patterns)
        {
            foreach (var pattern in patterns)
            {
                if (text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            return false;
        }

        // --- reporting -------------------------------------------------------------------------

        /// <summary>
        /// Marks a district static for occlusion, GI, navmesh and reflection probes — but NOT for
        /// batching.
        ///
        /// Static batching rewrites the MeshFilter to point at a "Combined Mesh (root: scene)" that
        /// Unity builds with a 16-bit index buffer. Downtown has 122,678 vertices, so every index
        /// past 65,535 wraps and its triangles reach across the mesh: the block renders as a nest
        /// of giant spikes, while its collider (which still uses the real asset mesh) stays correct,
        /// so the world feels right and looks shredded.
        ///
        /// There is nothing to win here anyway. Batching exists to merge many small draw calls, and
        /// a district is one to four huge meshes.
        /// </summary>
        private static void SetDistrictStaticFlags(GameObject go)
        {
            // Listed one by one rather than "everything except batching": passing an all-bits value
            // is normalised straight back to Everything, which quietly puts BatchingStatic back.
            const StaticEditorFlags flags =
                StaticEditorFlags.ContributeGI |
                StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.NavigationStatic |
                StaticEditorFlags.OffMeshLinkGeneration |
                StaticEditorFlags.ReflectionProbeStatic;

            foreach (var child in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, flags);
        }

        private static string Sanitize(string name) => name.Replace(" ", string.Empty);

        private static string Fmt(Vector3 v) => $"({v.x:0.##}, {v.y:0.##}, {v.z:0.##})";

        private class Report
        {
            public readonly List<string> Placed = new();
            public readonly List<string> Missing = new();
            public readonly List<string> Warnings = new();
            public readonly List<string> Notes = new();
            public readonly List<string> RemovedLegacy = new();
            public int Colliders;

            public string Compose(TheBlockConfig.Snapshot snapshot, TimeSpan elapsed, Options options)
            {
                var sb = new StringBuilder();
                sb.AppendLine(
                    $"WorldBuilder — {Placed.Count} placed, {Missing.Count} missing, {Colliders} colliders, {elapsed.TotalSeconds:0.0}s");
                sb.AppendLine($"config.ts {snapshot.SourceSha256?[..12]}  " +
                              $"districts:{options.Districts} places:{options.Places} colliders:{options.Colliders}");

                Section(sb, "PLACED", Placed);
                Section(sb, "MISSING — asset not in the project yet", Missing);
                Section(sb, "WARNINGS", Warnings);
                Section(sb, "REPLACED hand-placed roots", RemovedLegacy);
                Section(sb, "NOTES", Notes);
                return sb.ToString().TrimEnd();
            }

            private static void Section(StringBuilder sb, string title, List<string> lines)
            {
                if (lines.Count == 0) return;
                sb.AppendLine();
                sb.AppendLine(title);
                foreach (var line in lines) sb.AppendLine($"  {line}");
            }
        }
    }
}
