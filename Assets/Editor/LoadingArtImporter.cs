using System;
using UnityEditor;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Import settings for the boot screen's key art - <c>Assets/Resources/UI/LoadingArt.*</c>.
    ///
    /// A postprocessor for the same reason <see cref="GeneratedTextureImporter"/> is one: settings
    /// typed into the inspector live only in the <c>.meta</c>, and this project has already been
    /// bitten by a fix that existed only there ([[gitignored-meta-hides-importer-fixes]]). The rules
    /// live in code, so a Library wipe or a fresh clone re-derives them.
    ///
    /// Three of them are not the defaults, and each has a reason:
    ///
    /// <list type="bullet">
    /// <item><b>No mipmaps.</b> This is drawn at roughly 1:1 on a flat quad and never minified, so a
    /// mip chain is a third more memory for nothing - and worse, a non-power-of-two texture WITH
    /// mipmaps is silently not block compressed at all (that is the whole of U15's lesson, in
    /// <see cref="GeneratedTextureImporter"/>'s npotScale note). Mips off is what lets the cover
    /// keep its aspect ratio and still be compressed.</item>
    /// <item><b><c>npotScale = None</c>.</b> The cover is 2.36:1. Scaling it to a power of two -
    /// which is what fixes the case above for the district atlases - would stretch it to 4096x2048
    /// and distort the artwork. UVs there are normalised so nothing notices; a piece of framed art
    /// is exactly the case where it does.</item>
    /// <item><b>BC7, not BC1.</b> The sky is one long orange gradient across 3168 px, which is the
    /// worst possible input for BC1's 5:6:5 endpoints - it bands into visible steps. BC7 is 8 bpp
    /// against BC1's 4, so this costs about 2 MB on one texture that is only resident during boot.
    /// </item>
    /// </list>
    /// </summary>
    public class LoadingArtImporter : AssetPostprocessor
    {
        /// <summary>The asset, without its extension - .png and .jpg are both fine.</summary>
        public const string ArtPathPrefix = "Assets/Resources/UI/LoadingArt";

        private static bool IsLoadingArt(string path) =>
            path.StartsWith(ArtPathPrefix, StringComparison.Ordinal);

        private void OnPreprocessTexture()
        {
            if (!IsLoadingArt(assetPath)) return;

            var importer = (TextureImporter)assetImporter;

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 1;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 4096;

            // The art is opaque. Dropping the alpha channel does not shrink BC7, but it does stop a
            // stray alpha in a re-exported source from making the cover translucent over the
            // backdrop, which would read as a bug in the fade rather than in the file.
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;

            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.compressionQuality = (int)TextureCompressionQuality.Best;
            importer.crunchedCompression = false;

            var standalone = importer.GetPlatformTextureSettings("Standalone");
            standalone.overridden = true;
            standalone.maxTextureSize = 4096;
            standalone.format = TextureImporterFormat.BC7;
            standalone.compressionQuality = (int)TextureCompressionQuality.Best;
            importer.SetPlatformTextureSettings(standalone);
        }

        /// <summary>
        /// Reports what actually came out, because the thing being asked for here fails silently.
        ///
        /// Block compression needs both edges to be a multiple of 4. Ask for BC7 on, say, a
        /// 3167-wide image and Unity hands back RGBA32 - four times the memory, no warning, no error,
        /// and a texture that looks perfect. The only way to know is to read the format back.
        ///
        /// <b>Here and not in <c>OnPostprocessTexture</c>, which is the obvious place and the wrong
        /// one.</b> The <c>Texture2D</c> that callback is handed is the decoded source, BEFORE
        /// compression - it reports RGBA32 for an import that ends up BC7, so the check written
        /// there warns on every healthy import. Measured on the first run of this file: "3168x1344
        /// RGBA32, ~16.2 MB - not block compressed", about an asset that loads as BC7 at 4.1 MB.
        /// By <c>OnPostprocessAllAssets</c> the asset is final and the format read is the real one.
        /// </summary>
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted,
                                                   string[] moved, string[] movedFrom)
        {
            foreach (var path in imported)
            {
                if (!IsLoadingArt(path)) continue;

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null) continue;

                var mb = (texture.width * (long)texture.height * BitsPerPixel(texture.format))
                         / 8f / (1024f * 1024f);
                var line = $"LoadingArtImporter: {texture.width}x{texture.height} {texture.format}, ~{mb:0.0} MB";

                if (texture.format == TextureFormat.BC7 || texture.format == TextureFormat.DXT1)
                {
                    Debug.Log(line);
                    continue;
                }

                Debug.LogWarning(
                    $"{line} - not block compressed. Both edges must be a multiple of 4 " +
                    $"({texture.width % 4} / {texture.height % 4} over). Crop the source and re-drop it.");
            }
        }

        private static int BitsPerPixel(TextureFormat format)
        {
            switch (format)
            {
                case TextureFormat.DXT1: return 4;
                case TextureFormat.BC7:
                case TextureFormat.DXT5: return 8;
                case TextureFormat.RGB24: return 24;
                default: return 32;
            }
        }
    }
}
