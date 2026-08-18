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
    /// Two sweeps: every <c>.mat</c> under <c>Assets/Materials</c> is rebound in place, then every
    /// prefab under <c>Assets/Prefabs</c> has its imported (.glb) materials cloned into
    /// <see cref="CloneFolder"/> and rebound, and is saved. Both use
    /// <see cref="VehicleMaterials.RebindHierarchy"/> / <see cref="VehicleMaterials.RebindCompressed"/>,
    /// which is what the builders call, so a later rebuild produces the same result this pass does.
    /// Idempotent: a second run finds nothing to change.
    ///
    /// Run it AFTER <c>The Block → Compress Textures</c>; a texture with no twin is reported as a miss.
    /// </summary>
    public static class PrefabTextureRebinder
    {
        private const string MaterialRoot = "Assets/Materials";
        private const string PrefabRoot = "Assets/Prefabs";
        private const string CloneFolder = "Assets/Materials/Prefabs/Compressed";

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
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();

            var summary =
                $"Rebind Compressed Textures - materials: {matTouched} rebound ({matSlots} slots, {matMisses} misses); " +
                $"prefabs: {prefabTouched} rewritten ({prefabCloned} clones, {prefabSlots} slots, {prefabMisses} misses)\n";
            var text = summary + log;
            if (matMisses + prefabMisses > 0)
                Debug.LogWarning(text + "  Misses have no compressed twin - run The Block → Compress Textures, then this again.");
            else
                Debug.Log(text);
        }
    }
}
