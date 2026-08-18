using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// The material work every car builder has to do, in one place.
    ///
    /// Three builders now put a car in the world from the same three .glb files -
    /// <see cref="TrafficCarBuilder"/> for the ambient traffic, <see cref="CarBuilder"/> for the
    /// drivable copies, and <c>WorldBuilder.LotCars</c> for the parked fillers - and each of them
    /// needs the same two non-obvious things done to a .glb's materials. Both were learned the hard
    /// way and neither is guessable from the API:
    ///
    ///  1. <b>A .glb's textures are sub-assets no importer ever compresses</b> (memory
    ///     <c>gltfast-textures-never-compressed</c>), and <c>WorldBuilder</c>'s rebinding pass only
    ///     ever sees objects in the SCENE. A prefab instantiated at runtime therefore quietly pulls
    ///     the raw RGB24 copies back into memory unless its materials are cloned out of the .glb and
    ///     pointed at <see cref="TextureCompressor"/>'s output.
    ///  2. <b>glTFast's <c>baseColorFactor</c> is gamma-tagged and URP/Lit's <c>_BaseColor</c> is
    ///     not</b> (memory <c>gltfast-basecolorfactor-gamma</c>), so the same colour has to be
    ///     written two different ways depending on which shader survived the import. Get it backwards
    ///     and the paint renders near-black, or does not apply at all.
    ///
    /// Kept as plain static helpers over a shared base class: the three builders assemble very
    /// different objects and share only this.
    /// </summary>
    public static class VehicleMaterials
    {
        /// <summary>
        /// Names the car models give their body-paint material. The web build's own convention,
        /// matched identically in <c>vehicle.ts</c>, <c>lot-cars.ts</c> and <c>traffic-cars.ts</c>.
        /// The Mustang and the Audi/Avenger use the first; the Tesla uses the second.
        /// </summary>
        public static readonly string[] PaintMaterialNames = { "CarPrimaryColor", "primary" };

        public static bool IsPaint(Material material) =>
            material != null && Array.IndexOf(PaintMaterialNames, material.name) >= 0;

        /// <summary>
        /// Copies a material out of its .glb into an editable asset.
        ///
        /// <paramref name="written"/> is the run's ledger: it both de-duplicates names within a
        /// build and tells <see cref="Sweep"/> what is still current, so a model that stops using a
        /// material does not leave the old .mat behind forever.
        /// </summary>
        public static Material Clone(
            Material source, string folder, string prefix, HashSet<string> written)
        {
            EnsureFolder(folder);

            var assetPath = $"{folder}/{prefix}_{Sanitize(source.name)}.mat";
            for (int i = 2; written.Contains(assetPath); i++)
                assetPath = $"{folder}/{prefix}_{Sanitize(source.name)}_{i}.mat";
            written.Add(assetPath);

            var clone = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (clone == null)
            {
                clone = new Material(source);
                AssetDatabase.CreateAsset(clone, assetPath);
            }
            else
            {
                // Rewritten from the source every run, so a re-exported model carries through rather
                // than leaving a stale clone that looks right until someone changes the asset.
                clone.shader = source.shader;
                clone.CopyPropertiesFromMaterial(source);
            }

            // Named after the FILE, not after the source material. A .mat whose object name differs
            // from its file name makes URP's material upgrader log "Main Object Name … does not
            // match filename" on every import, and there is no reason to add to that noise.
            clone.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            clone.renderQueue = source.renderQueue;
            clone.doubleSidedGI = source.doubleSidedGI;
            clone.enableInstancing = true;
            EditorUtility.SetDirty(clone);
            return clone;
        }

        /// <summary>One colour of a car's paint, as its own asset. See the class note on the gamma.</summary>
        public static Material PaintVariant(
            Material source, string folder, string prefix, int hex, HashSet<string> written)
        {
            EnsureFolder(folder);

            var assetPath = $"{folder}/{prefix}_Paint_{hex:X6}.mat";
            written.Add(assetPath);

            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                material = new Material(source);
                AssetDatabase.CreateAsset(material, assetPath);
            }
            else
            {
                material.shader = source.shader;
                material.CopyPropertiesFromMaterial(source);
            }

            SetBaseColor(material, hex);
            material.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Writes a packed <c>0xRRGGBB</c> as this material's base colour, whichever shader it is on.
        /// Returns the property it used, or null if the shader has no base colour at all.
        ///
        /// The rule - glTFast's gamma-tagged <c>baseColorFactor</c> takes sRGB as written, URP's
        /// <c>_BaseColor</c> needs <c>.linear</c> - lives in the runtime
        /// <see cref="TheBlock.Vehicles.PaintPalette.SetBaseColor"/> since U35g, because the auto
        /// shop applies it in Play. One copy; this is the editor's door to it.
        /// </summary>
        public static string SetBaseColor(Material material, int hex) =>
            TheBlock.Vehicles.PaintPalette.SetBaseColor(material, hex);

        /// <summary>
        /// Points every .glb-sourced texture on a material at the copy <see cref="TextureCompressor"/>
        /// extracted, if there is one. Returns how many slots were rebound.
        ///
        /// The lookup contract is <c>TextureCompressor</c>'s own - folder from the .glb's stem, file
        /// name from <see cref="TextureCompressor.AssetName"/> - so the two sides cannot disagree
        /// about where a texture landed. A miss is counted rather than silently accepted: a
        /// half-compressed car costs the disk of the new copy and keeps the old one resident anyway.
        /// Textures under <see cref="TextureCompressor.SkipBelowPixels"/> were never extracted and
        /// are not misses; they are skipped here on the same test.
        /// </summary>
        public static int RebindCompressed(Material material, ref int misses)
        {
            var shader = material.shader;
            if (shader == null) return 0;

            int rebound = 0;

            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;

                var property = shader.GetPropertyName(i);
                if (material.GetTexture(property) is not Texture2D texture) continue;

                var sourcePath = AssetDatabase.GetAssetPath(texture);
                if (!sourcePath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) continue;
                if ((long)texture.width * texture.height < TextureCompressor.SkipBelowPixels) continue;

                var stem = Sanitize(System.IO.Path.GetFileNameWithoutExtension(sourcePath));
                var folder = $"{TextureCompressor.GeneratedTextureFolder}/{stem}";
                var assetName = TextureCompressor.AssetName(texture);

                Texture2D compressed = null;
                foreach (var extension in new[] { ".png", ".jpg" })
                {
                    compressed = AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}/{assetName}{extension}");
                    if (compressed != null) break;
                }

                if (compressed == null) { misses++; continue; }

                material.SetTexture(property, compressed);
                rebound++;
            }

            return rebound;
        }

        /// <summary>
        /// The whole rebind for one built object: every renderer under <paramref name="root"/> whose
        /// material is still a read-only .glb sub-asset gets a clone in <paramref name="cloneFolder"/>
        /// pointed at the compressed twins, and every material that is already an asset of ours is
        /// rebound in place. Returns the number of texture slots rebound.
        ///
        /// <b>Why this exists, U30b.</b> <see cref="Clone"/> + <see cref="RebindCompressed"/> were
        /// wired into the car builders and nowhere else, so the helicopter, the jetski and the police
        /// car (built before Compress Textures had extracted their .glbs) shipped with their raw
        /// sub-asset textures. The first Player census put a number on it: the Huey alone was seven
        /// 4096² ARGB32 textures, 1,195 MB resident from boot for a mission the player may never
        /// start, and 1,134 MB of the 1,024 MB streaming budget was "non-streaming" - i.e. the
        /// districts were being starved of mips by a helicopter in a hangar. One helper, called by
        /// every builder that saves a prefab AND by the menu pass that fixes what is already built.
        ///
        /// A material is only cloned if the clone would actually change something - a probe copy is
        /// rebound first and thrown away if nothing on it points at a twin - so a model whose textures
        /// are all below <see cref="TextureCompressor.SkipBelowPixels"/> stays exactly as imported.
        /// </summary>
        public static int RebindHierarchy(
            GameObject root, string cloneFolder, string prefix, HashSet<string> written,
            ref int misses, ref int cloned, StringBuilder log = null)
        {
            var clones = new Dictionary<Material, Material>();
            int rebound = 0;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (material == null) continue;

                    var path = AssetDatabase.GetAssetPath(material);
                    bool imported = path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);

                    if (!imported)
                    {
                        // Ours already (a builder clone, a hand-made .mat): rebind in place.
                        if (string.IsNullOrEmpty(path)) continue; // scene-only instance, nothing to save
                        int n = RebindCompressed(material, ref misses);
                        if (n > 0) { rebound += n; EditorUtility.SetDirty(material); }
                        continue;
                    }

                    if (!clones.TryGetValue(material, out var clone))
                    {
                        // Probe first: is there anything to gain?
                        var probe = new Material(material);
                        int probeMisses = 0;
                        int gain = RebindCompressed(probe, ref probeMisses);
                        UnityEngine.Object.DestroyImmediate(probe);
                        misses += probeMisses;

                        clone = gain > 0 ? Clone(material, cloneFolder, prefix, written) : null;
                        clones[material] = clone;
                        if (clone != null)
                        {
                            cloned++;
                            int m = 0;
                            rebound += RebindCompressed(clone, ref m);
                            log?.AppendLine($"  {prefix}: {material.name} → {clone.name} ({gain} texture slot(s) compressed)");
                        }
                    }

                    if (clone == null) continue;
                    materials[i] = clone;
                    changed = true;
                }

                if (changed) renderer.sharedMaterials = materials;
            }

            return rebound;
        }

        /// <summary>
        /// Deletes anything in <paramref name="folder"/> this run did not write, and says so.
        ///
        /// A builder's material folder is build output that only that builder writes - which is also
        /// why these folders are not in <c>WorldBuilder.SweepGenerated</c>'s list. Putting them there
        /// would let an ordinary world build delete the materials a prefab points at, which is the
        /// same shape of fault that once blanked U16's zebras.
        /// </summary>
        public static void Sweep(string folder, HashSet<string> written, StringBuilder log)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return;

            int deleted = 0;
            foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path) || written.Contains(path)) continue;
                AssetDatabase.DeleteAsset(path);
                deleted++;
            }

            if (deleted > 0) log?.AppendLine($"swept      {deleted} stale material(s) from {folder}");
        }

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            var parts = folder.Split('/');
            var built = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = $"{built}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(built, parts[i]);
                built = next;
            }
        }

        /// <summary>The same rule <see cref="TextureCompressor"/> uses, so both agree on a file name.</summary>
        public static string Sanitize(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var c in name) sb.Append(char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_');
            return sb.ToString();
        }
    }
}
