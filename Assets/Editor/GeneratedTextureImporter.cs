using System;
using UnityEditor;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Applies the import settings for everything <see cref="TextureCompressor"/> extracts, at
    /// import time and derived entirely from the file's own name.
    ///
    /// WHY A POSTPROCESSOR AND NOT A SETTER. Writing the file and then editing its
    /// <c>TextureImporter</c> means importing twice — once with Unity's defaults, which for a
    /// 16000x2000 atlas is a full downscale-to-2048-and-compress that is then thrown away. There
    /// are 241 of these. Worse, the settings would live only in the .meta, so anything that forces
    /// a reimport later (a Library wipe, a platform switch, a fresh clone) would silently restore
    /// the defaults and put the memory back.
    ///
    /// Encoding the two decisions that cannot be inferred INTO THE FILE NAME fixes both: the first
    /// import is already correct, and it stays correct forever because the name is the record.
    /// <see cref="TextureCompressor.AssetName"/> writes that name; this reads it.
    /// </summary>
    public class GeneratedTextureImporter : AssetPostprocessor
    {
        /// <summary>
        /// Cap on the longest edge. 16384 is Unity's maximum, i.e. no resize at all — the
        /// 16000-wide atlases pass through untouched and this pass is purely a format change.
        ///
        /// This is the knob U30's perf pass and U31's iPad build will want, and halving it is worth
        /// another 4x. It is deliberately NOT halved here, because a resize is a visual change and
        /// U15 is a memory unit: the point was to prove the format alone was the fault.
        /// </summary>
        private const int MaxTextureSize = 16384;

        /// <summary>
        /// Marks a texture that must be sampled linearly — a roughness, metallic, occlusion or
        /// normal map. Written by <see cref="TextureCompressor.AssetName"/> from the flag glTFast
        /// itself resolved, rather than re-derived from the glTF's material roles, because
        /// disagreeing with glTFast is how a roughness map ends up gamma-decoded.
        /// </summary>
        public const string LinearSuffix = "_lin";

        /// <summary>
        /// Names that mean "this is a tangent-space normal map".
        ///
        /// They get BC7 rather than BC1. A normal map is three correlated channels with no tolerance
        /// for the 5:6:5 endpoints BC1 quantises to — BC1 normals band into visible facets across a
        /// flat wall, which is exactly the surface these districts are made of. Colour and roughness
        /// maps do not care. A false positive here costs size, never correctness.
        /// </summary>
        private static readonly string[] NormalMapPatterns = { "normal", "_nrm", "_norm" };

        /// <summary>
        /// Force-reimports everything in the generated folder so a change to the rules above is
        /// actually applied — <b>The Block → Reimport Generated Textures</b>.
        ///
        /// Needed because these settings live in this file rather than in the .meta (see the class
        /// note): editing a rule changes what a FUTURE import would produce and does nothing to the
        /// 241 textures already sitting in the Library. The alternative is
        /// <b>Compress Textures (force re-extract)</b>, which re-extracts every source image out of
        /// the district .glb files and is minutes of work to change one importer flag.
        ///
        /// Slow — it is BC7 over 16384-wide atlases — and it deliberately reports what it did.
        /// </summary>
        [MenuItem("The Block/Reimport Generated Textures", priority = 5)]
        public static void ReimportGeneratedMenu()
        {
            var paths = new System.Collections.Generic.List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TextureCompressor.GeneratedTextureFolder }))
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));

            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Reimport Generated Textures",
                            $"{i + 1} / {paths.Count}  {System.IO.Path.GetFileName(paths[i])}",
                            (i + 1) / (float)paths.Count))
                        break;

                    AssetDatabase.ImportAsset(
                        paths[i], ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            watch.Stop();
            Debug.Log($"GeneratedTextureImporter: reimported {paths.Count} texture(s) in {watch.Elapsed.TotalSeconds:0}s");
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(TextureCompressor.GeneratedTextureFolder + "/", StringComparison.Ordinal))
                return;

            var importer = (TextureImporter)assetImporter;
            var name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            bool isLinear = name.EndsWith(LinearSuffix, StringComparison.Ordinal);
            bool isNormal = ContainsAny(name, NormalMapPatterns);

            // ⚠ NOT TextureImporterType.NormalMap, deliberately. That swizzles into Unity's DXT5nm
            // layout, which only makes sense for a shader calling UnpackNormal. glTF normal maps are
            // raw XYZ in RGB and glTFast's Shader Graph samples them raw, so the swizzle would feed
            // it garbage — a world lit as though every wall were dented.
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = !isLinear;

            // glTF's sampler defaults, which is what every texture in these districts uses; the
            // compressor reports any source texture that disagrees rather than encoding it here.
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 1;

            importer.mipmapEnabled = true;
            importer.maxTextureSize = MaxTextureSize;

            // MIP STREAMING. U15 proved the format was the fault and left 2.19 GB of texture memory
            // resident, every byte of it non-streaming — on a 16 GB unified-memory Mac that is most
            // of the reason the editor intermittently ran out of memory to give Metal and painted
            // green blocks over its own toolbar.
            //
            // This costs NO visual quality by construction, which is why it is the fix rather than
            // halving MaxTextureSize above: streaming never resizes a texture, it declines to keep
            // resident a mip finer than the screen can resolve. A district 400 m away does not need
            // its 16384-wide mip 0, and that is the byte being saved. `maxLevelReduction` in the
            // quality settings bounds how far it may go if memory gets tight anyway.
            //
            // The system must ALSO be switched on in QualitySettings; this flag is the per-texture
            // opt-in, and without it the budget applies to nothing.
            importer.streamingMipmaps = true;

            // Priority 0 is the default band. The atlases are all the same kind of thing — city
            // facades — so there is no honest reason to rank one above another, and inventing one
            // here would just be a number nobody could defend later.
            importer.streamingMipmapsPriority = 0;

            // ⚠ THIS LINE IS THE WHOLE UNIT. A non-power-of-two texture with mipmaps is NOT block
            // compressed by Unity — the importer picks DXT1, reports DXT1 from GetAutomaticFormat,
            // agrees the platform supports it, and then hands back RGB24 anyway, silently. With
            // npotScale None the first run compressed only the 166 power-of-two textures and left
            // all 75 NPOT ones raw, which is 8.9 GB of the 12.8 GB — the fix looked like it had
            // barely worked. Multiple-of-4 is not enough; it has to be a power of two.
            //
            // ToLarger rather than ToNearest or ToSmaller, because this unit promised a format
            // change and not a resize: scaling UP cannot lose source detail. 16000x2000 becomes
            // 16384x2048 and 3900x3000 becomes 4096x4096 — slightly more pixels, a tenth of the
            // memory, and nothing resampled away. UVs are normalised, so nothing shifts.
            importer.npotScale = TextureImporterNPOTScale.ToLarger;

            // FromInput keeps an alpha channel when the source image has one and none when it does
            // not — the same thing glTFast does. alphaIsTransparency stays OFF: it runs Unity's
            // colour dilation into transparent texels, which glTFast never did, so turning it on
            // would silently change the look of the foliage atlas U11 spent a unit getting right.
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;

            importer.textureCompression = isNormal
                ? TextureImporterCompression.CompressedHQ   // BC7 — see NormalMapPatterns
                : TextureImporterCompression.Compressed;    // BC1/BC3
            importer.compressionQuality = (int)TextureCompressionQuality.Normal;
            importer.crunchedCompression = false;
        }

        private static bool ContainsAny(string text, string[] patterns)
        {
            foreach (var pattern in patterns)
            {
                if (text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            return false;
        }
    }
}
