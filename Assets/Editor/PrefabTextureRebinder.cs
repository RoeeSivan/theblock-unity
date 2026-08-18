using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// The Block → Rebind Compressed Textures (prefabs + materials) - U30b.
    ///
    /// <b>The pass that fixes what is already built, without rebuilding it.</b> <c>TextureCompressor</c>
    /// extracts a .glb's textures into <c>Assets/Textures/Generated</c> so Unity can compress them, but
    /// extraction on its own changes nothing on screen: every material has to be pointed at the twin,
    /// and that rebind was done by <c>WorldBuilder</c> for the scene and by the car builders for their
    /// prefabs - and by nothing else. The first macOS Player's texture census found the gap:
    ///
    /// <list type="bullet">
    /// <item><c>Helicopter.prefab</c> drew from <c>huey.glb</c>'s own materials - 7 × 4096² ARGB32,
    /// <b>1,195 MB</b> resident from boot.</item>
    /// <item><c>PoliceCar_*.mat</c> were cloned but built before Compress Textures had reached
    /// <c>police_car.glb</c>, so their eight slots still pointed into the .glb - 88 MB.</item>
    /// <item><c>Jetski.prefab</c>, one 4096×2048 - 85 MB.</item>
    /// </list>
    ///
    /// Three sweeps: every <c>.mat</c> under <c>Assets/Materials</c> is rebound in place, then every
    /// prefab under <c>Assets/Prefabs</c> has its imported (.glb) materials cloned into
    /// <see cref="CloneFolder"/> and rebound, and is saved, then the open scene gets the same
    /// treatment into <see cref="SceneCloneFolder"/>. All three use
    /// <see cref="VehicleMaterials.RebindHierarchy"/> / <see cref="VehicleMaterials.RebindCompressed"/>,
    /// which is what the builders call, so a later rebuild produces the same result this pass does.
    /// Idempotent: a second run finds nothing to change.
    ///
    /// <b>The scene sweep is U30b round 2, and it is the third blind spot of the same fault.</b> The
    /// two sweeps above cover assets on disk, and the whole premise was that everything in the world
    /// either is a prefab or is written by a builder that calls the rebind. <c>Chase Thief</c>'s
    /// <c>Thief Ski</c> is neither - a plain scene object, no prefab, no builder - so it kept
    /// <c>jetski.glb</c>'s own <c>texMain</c> and with it one 4096×2048 ARGB32, <b>85.3 MB resident
    /// from boot</b>, while the <c>Jetski.prefab</c> six metres away had been fixed. The same shape as
    /// the Huey: not a wrong number anywhere, an object nobody's sweep reached. Memory
    /// <c>compressed-rebind-is-per-builder</c>.
    ///
    /// Prefab instances are deliberately skipped by the scene sweep - sweep 2 owns them, and writing
    /// a material into an instance would store a prefab override that outlives the next rebuild.
    ///
    /// Run it AFTER <c>The Block → Compress Textures</c>; a texture with no twin is reported as a miss.
    /// </summary>
    public static class PrefabTextureRebinder
    {
        private const string MaterialRoot = "Assets/Materials";
        private const string PrefabRoot = "Assets/Prefabs";
        private const string CloneFolder = "Assets/Materials/Prefabs/Compressed";
        private const string SceneCloneFolder = "Assets/Materials/Scene/Compressed";

        [MenuItem("The Block/Rebind Compressed Textures (prefabs + materials)", priority = 5)]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Rebind Compressed Textures: stop Play mode first.");
                return;
            }

            var log = new StringBuilder("Rebind Compressed Textures\n");
            int matSlots = 0, matTouched = 0, matMisses = 0;
            int prefabSlots = 0, prefabTouched = 0, prefabMisses = 0, prefabCloned = 0;
            int sceneSlots = 0, sceneMisses = 0, sceneCloned = 0;

            try
            {
                // ── 1. materials that are already assets of ours ─────────────────────────────
                var matGuids = AssetDatabase.FindAssets("t:Material", new[] { MaterialRoot });
                for (int i = 0; i < matGuids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(matGuids[i]);
                    EditorUtility.DisplayProgressBar("Rebind Compressed Textures", path, 0.5f * i / matGuids.Length);

                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null) continue;

                    int misses = 0;
                    int n = VehicleMaterials.RebindCompressed(material, ref misses);
                    matMisses += misses;
                    if (n == 0) continue;

                    EditorUtility.SetDirty(material);
                    matSlots += n;
                    matTouched++;
                    log.AppendLine($"  {path}: {n} slot(s)");
                }

                // ── 2. prefabs still drawing from .glb sub-asset materials ────────────────────
                var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot });
                for (int i = 0; i < prefabGuids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                    EditorUtility.DisplayProgressBar("Rebind Compressed Textures", path, 0.5f + 0.5f * i / prefabGuids.Length);

                    var root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        var prefix = System.IO.Path.GetFileNameWithoutExtension(path);
                        var written = new HashSet<string>(System.StringComparer.Ordinal);
                        int misses = 0, cloned = 0;

                        // Materials that are assets were handled in sweep 1; this pass only ever
                        // changes the prefab when a .glb material had to be replaced by a clone.
                        int n = VehicleMaterials.RebindHierarchy(root, CloneFolder, prefix, written, ref misses, ref cloned, log);
                        prefabMisses += misses;
                        if (cloned == 0) continue;

                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        prefabSlots += n;
                        prefabCloned += cloned;
                        prefabTouched++;
                        log.AppendLine($"  {path}: {cloned} material(s) cloned, {n} slot(s)");
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                }

                // ── 3. the open scene: objects that are neither a prefab nor a builder's output ──
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (scene.IsValid() && scene.isLoaded)
                {
                    var written = new HashSet<string>(System.StringComparer.Ordinal);
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        EditorUtility.DisplayProgressBar("Rebind Compressed Textures", scene.name + "/" + root.name, 1f);

                        // Sweep 2 owns prefab instances. Rebinding one here would write a prefab
                        // override, which survives the next Build World and then disagrees with it.
                        if (PrefabUtility.IsPartOfPrefabInstance(root)) continue;

                        int misses = 0, cloned = 0;
                        int n = VehicleMaterials.RebindHierarchy(
                            root, SceneCloneFolder, VehicleMaterials.Sanitize(root.name), written, ref misses, ref cloned, log);

                        sceneMisses += misses;
                        if (n == 0) continue;

                        sceneSlots += n;
                        sceneCloned += cloned;
                        log.AppendLine($"  scene/{root.name}: {cloned} material(s) cloned, {n} slot(s)");
                    }

                    if (sceneSlots > 0) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();

            var summary =
                $"Rebind Compressed Textures - materials: {matTouched} rebound ({matSlots} slots, {matMisses} misses); " +
                $"prefabs: {prefabTouched} rewritten ({prefabCloned} clones, {prefabSlots} slots, {prefabMisses} misses); " +
                $"scene: {sceneCloned} clones, {sceneSlots} slots, {sceneMisses} misses" +
                (sceneSlots > 0 ? " - SCENE IS DIRTY, save it" : string.Empty) + "\n";
            var text = summary + log;
            if (matMisses + prefabMisses + sceneMisses > 0)
                Debug.LogWarning(text + "  Misses have no compressed twin - run The Block → Compress Textures, then this again.");
            else
                Debug.Log(text);
        }
    }
}
