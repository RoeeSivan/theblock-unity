using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// U15 - points the world's materials at <see cref="TextureCompressor"/>'s compressed textures
    /// instead of at the raw RGB24 sub-assets glTFast produced.
    ///
    /// This is the cheap half of the unit. The expensive half - extracting the images and letting
    /// Unity compress them - is its own menu command, because it takes minutes and Build World runs
    /// constantly. If that command has never been run, every lookup misses, this pass reports how
    /// many and changes nothing: the world still builds, just heavy.
    ///
    /// It runs LAST of the material passes, after the alpha-clip pass, so it sees the final set of
    /// materials - including the URP/Lit cutouts U11 generates, which point at the same sub-asset
    /// textures and would otherwise have kept the uncompressed copy alive on their own.
    /// </summary>
    public static partial class WorldBuilder
    {
        /// <summary>Clones of imported glTF materials, rebound to compressed textures. Rebuilt every build.</summary>
        private const string CompressedMaterialFolder = "Assets/Materials/City/Compressed";

        /// <summary>
        /// Resolved <c>.glb</c> sub-asset texture -> compressed twin, for the whole build.
        ///
        /// Keyed by the texture instance rather than by name because a name is only unique within
        /// one .glb, and cleared at the start of every build so a re-run after a Compress Textures
        /// pass does not keep handing back last build's misses.
        /// </summary>
        private static readonly Dictionary<Texture, Texture> CompressedTextures = new();

        private static void ResetTexturePass() => CompressedTextures.Clear();

        /// <summary>
        /// Rebinds every material on <paramref name="instance"/> whose textures live inside a .glb.
        ///
        /// A material we generated ourselves (the alpha-clip cutouts) is edited in place - it is
        /// build output either way. An IMPORTED glTFast material cannot be: it is a sub-asset of the
        /// .glb and read-only, so it is cloned into <see cref="CompressedMaterialFolder"/> first.
        ///
        /// The clone is a full property copy onto the SAME shader, not a translation to URP/Lit.
        /// That distinction matters here in a way it did not for U11's cutouts: glTF packs
        /// metal-roughness and occlusion differently from URP/Lit, so the only safe way to swap a
        /// texture on a district's wall material is to keep glTFast's own Shader Graph underneath it
        /// and change nothing but the texture references.
        /// </summary>
        private static void ApplyCompressedTextures(GameObject instance, Report report)
        {
            var cloned = new Dictionary<Material, Material>();
            var usedPaths = new HashSet<string>(StringComparer.Ordinal);
            int rebound = 0, misses = 0;
            var missNames = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;

                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (material == null) continue;

                    var swaps = FindCompressedSwaps(material, missNames, ref misses);
                    if (swaps.Count == 0) continue;

                    if (!cloned.TryGetValue(material, out var target))
                    {
                        target = IsGeneratedMaterial(material)
                            ? material
                            : CloneForCompression(instance, material, usedPaths, report);
                        cloned[material] = target;
                    }

                    foreach (var swap in swaps) target.SetTexture(swap.Key, swap.Value);
                    EditorUtility.SetDirty(target);
                    rebound += swaps.Count;

                    if (target == material) continue;
                    materials[i] = target;
                    changed = true;
                }

                if (changed) renderer.sharedMaterials = materials;
            }

            if (rebound > 0)
                report.Notes.Add(
                    $"{instance.name}: rebound {rebound} texture slot(s) onto compressed copies " +
                    $"across {cloned.Count} material(s)");

            if (misses > 0)
                report.Warnings.Add(
                    $"{instance.name}: {misses} texture(s) have no compressed copy and stay RGB24 - " +
                    $"run The Block → Compress Textures ({string.Join(", ", missNames.Take(4))}" +
                    $"{(missNames.Count > 4 ? ", …" : string.Empty)})");
        }

        /// <summary>
        /// Every texture property on <paramref name="material"/> that currently points inside a .glb
        /// and has a compressed twin on disk, as property-name -> replacement.
        ///
        /// Anything that points inside a .glb and has NO twin is counted as a miss rather than left
        /// silent: a partly-compressed district is the worst outcome - it costs the disk of the new
        /// copy and keeps the old one resident anyway - so it has to show up in the build report.
        /// </summary>
        private static Dictionary<string, Texture> FindCompressedSwaps(
            Material material, SortedSet<string> missNames, ref int misses)
        {
            var swaps = new Dictionary<string, Texture>(StringComparer.Ordinal);
            var shader = material.shader;
            if (shader == null) return swaps;

            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;

                var property = shader.GetPropertyName(i);
                var texture = material.GetTexture(property);
                if (texture == null) continue;

                var sourcePath = AssetDatabase.GetAssetPath(texture);
                if (!sourcePath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) continue;

                // Below the compressor's threshold on purpose - a miss here is the design, not a
                // missing run, so it must not be reported as one.
                if ((long)texture.width * texture.height < TextureCompressor.SkipBelowPixels) continue;

                if (!CompressedTextures.TryGetValue(texture, out var compressed))
                {
                    compressed = LoadCompressed(sourcePath, texture as Texture2D);
                    CompressedTextures[texture] = compressed;
                }

                if (compressed == null)
                {
                    misses++;
                    missNames.Add(texture.name);
                    continue;
                }

                swaps[property] = compressed;
            }

            return swaps;
        }

        /// <summary>
        /// Finds the extracted twin of a .glb sub-asset texture.
        ///
        /// The folder is the source .glb's stem and the file name comes from
        /// <see cref="TextureCompressor.AssetName"/>, so the two sides cannot disagree about where a
        /// texture landed. The extension follows the embedded image's own format, so both are tried
        /// rather than recorded in a side table that could go stale against the folder.
        /// </summary>
        private static Texture LoadCompressed(string glbPath, Texture2D source)
        {
            if (source == null) return null;

            var stem = SanitizeAsset(System.IO.Path.GetFileNameWithoutExtension(glbPath));
            var folder = $"{TextureCompressor.GeneratedTextureFolder}/{stem}";
            var name = TextureCompressor.AssetName(source);

            foreach (var extension in new[] { ".png", ".jpg" })
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}/{name}{extension}");
                if (texture != null) return texture;
            }

            return null;
        }

        /// <summary>
        /// A read-write copy of an imported glTFast material, on the same shader.
        ///
        /// Rewritten from the source on every build rather than reused as found, for the same reason
        /// the cutout materials are: it has to stay a pure function of the imported material, so a
        /// re-exported district changes the source and the next build carries the change through.
        /// The render queue is set explicitly afterwards because the alpha-clip pass reads it back
        /// to decide what is still blended, and a clone that reports the wrong queue would make the
        /// build report lie.
        /// </summary>
        private static Material CloneForCompression(
            GameObject instance, Material source, HashSet<string> usedPaths, Report report)
        {
            EnsureFolder(CompressedMaterialFolder);

            var baseName = $"{Sanitize(instance.name)}_{SanitizeAsset(source.name)}";
            var assetPath = $"{CompressedMaterialFolder}/{baseName}.mat";
            // Two distinct materials in one district can share a name; the second gets a suffix
            // rather than silently overwriting the first.
            for (int i = 2; usedPaths.Contains(assetPath); i++)
                assetPath = $"{CompressedMaterialFolder}/{baseName}_{i}.mat";
            usedPaths.Add(assetPath);
            report.Generated.Add(assetPath);

            var clone = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (clone == null)
            {
                clone = new Material(source);
                AssetDatabase.CreateAsset(clone, assetPath);
            }
            else
            {
                clone.shader = source.shader;
                clone.CopyPropertiesFromMaterial(source);
            }

            clone.name = source.name;
            clone.renderQueue = source.renderQueue;
            clone.doubleSidedGI = source.doubleSidedGI;
            clone.enableInstancing = source.enableInstancing;
            return clone;
        }

        private static bool IsGeneratedMaterial(Material material)
        {
            var path = AssetDatabase.GetAssetPath(material);
            return path.StartsWith(CutoutMaterialFolder, StringComparison.Ordinal)
                   || path.StartsWith(CompressedMaterialFolder, StringComparison.Ordinal)
                   // U13's lot-car paint. Build output like the rest, so it is edited rather than
                   // cloned - cloning it would also break the instancing that made it a material
                   // asset in the first place.
                   || path.StartsWith(LotCarMaterialFolder, StringComparison.Ordinal);
        }

        /// <summary>
        /// Same rule <see cref="TextureCompressor"/> uses for file names, so the two agree on where a
        /// texture landed. <see cref="Sanitize"/> only strips spaces and is for object names.
        /// </summary>
        private static string SanitizeAsset(string name)
        {
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var c in name) sb.Append(char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_');
            return sb.ToString();
        }
    }
}
