using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Pulls the embedded images back out of the district .glb files and re-imports them as real
    /// project textures, so that Unity's per-platform texture compression runs on them at all.
    ///
    /// WHY THIS EXISTS. glTFast's <c>GltfImporter</c> is a <c>ScriptedImporter</c>: the textures it
    /// produces are SUB-ASSETS of the .glb, and a sub-asset has no <c>TextureImporter</c>. Nothing
    /// ever picks a compressed format for them, so every district texture sits in memory as raw
    /// RGB24 - measured on 2026-08-15, before this pass existed:
    ///
    /// <code>
    ///   RGB24    12,912 MB   <- glTFast sub-assets
    ///   ARGB32      547 MB
    ///   DXT5         32 MB   <- the handful of real .png assets in the project
    ///   DXT1          6 MB
    /// </code>
    ///
    /// Twenty-one of them are 16000x2000 at 284 MB each. That is 96% of the world's memory and it
    /// is the reason U15 is this pass and not Addressables - streaming 13.5 GB in chunks is still
    /// 13.5 GB, while compressing the same pixels is a ~12x cut with no resize and no visual loss.
    ///
    /// The image bytes are copied out VERBATIM - the PNG/JPEG buffer that is already inside the
    /// .glb, byte for byte. There is no decode and no re-encode here, so this step cannot lose
    /// quality; the only lossy stage is the block compression Unity then applies on import, which
    /// is the stage that was missing.
    ///
    /// This is a separate menu command rather than part of Build World on purpose. Compressing
    /// ~390 textures takes minutes, and Build World runs constantly. Run this once after a district
    /// .glb changes; it skips anything already newer than its source.
    ///
    /// The rebinding - pointing the districts' materials at these textures instead of at the
    /// sub-assets - is <see cref="WorldBuilder"/>'s job, in WorldBuilder.Textures.cs.
    /// </summary>
    public static class TextureCompressor
    {
        /// <summary>Extracted + compressed twins of the .glb sub-asset textures. Gitignored.</summary>
        public const string GeneratedTextureFolder = "Assets/Textures/Generated";

        /// <summary>Where the .glb files this pass reads live.</summary>
        private const string ModelFolder = "Assets/Models";

        /// <summary>
        /// Textures below this are left alone.
        ///
        /// The world's 393 textures are dominated by a handful of huge atlases: 21 at 16000x2000,
        /// 101 at 2048², 40 at 2000². Everything from 1 MP up is 96% of the memory, and the tail
        /// below it is ~200 MB in total - not worth a second copy on disk, an import round-trip, or
        /// the ambiguity risk below, since duplicate image names cluster in exactly that tail
        /// (there are seven images called "Untitled" in city 4 alone, all 512² or 1024²).
        /// </summary>
        public const int SkipBelowPixels = 1024 * 1024;

        /// <summary>
        /// glTFast appends this when one glTF image is sampled through more than one sampler, so the
        /// texture name is not always the image name. See <c>GltfImport.GetTextureName</c>.
        /// </summary>
        private static readonly Regex SamplerSuffix = new(@"_sampler\d+$", RegexOptions.Compiled);

        /// <summary>Unnamed glTF images are named by index - <c>GltfImport.GetTextureName</c> again.</summary>
        private static readonly Regex IndexedName = new(@"^image_(\d+)$", RegexOptions.Compiled);

        [MenuItem("The Block/Compress Textures", priority = 2)]
        public static void CompressTexturesMenu() => Compress(force: false, dryRun: false);

        [MenuItem("The Block/Compress Textures (force re-extract)", priority = 3)]
        public static void CompressTexturesForceMenu() => Compress(force: true, dryRun: false);

        /// <summary>
        /// Resolves every texture and reports what WOULD be written, without touching disk.
        ///
        /// Worth its own menu entry because the resolution step is the part that can go wrong - see
        /// <see cref="ResolveImageIndex"/> on why names alone are not enough - and the real run
        /// takes minutes of block compression before it would tell you.
        /// </summary>
        [MenuItem("The Block/Compress Textures (dry run)", priority = 4)]
        public static void CompressTexturesDryRunMenu() => Compress(force: false, dryRun: true);

        public static string Compress(bool force, bool dryRun)
        {
            var glbPaths = Directory
                .GetFiles(ModelFolder, "*.glb", SearchOption.AllDirectories)
                .Select(path => path.Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            if (glbPaths.Count == 0)
            {
                const string empty = "Compress Textures - no .glb files under Assets/Models.";
                Debug.LogWarning(empty);
                return empty;
            }

            var report = new StringBuilder();
            long before = 0, after = 0;
            int written = 0, skipped = 0, unchanged = 0;
            var warnings = new List<string>();

            try
            {
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < glbPaths.Count; i++)
                {
                    var glbPath = glbPaths[i];
                    EditorUtility.DisplayProgressBar(
                        "Compress Textures", Path.GetFileName(glbPath), (float)i / glbPaths.Count);

                    CompressOne(glbPath, force, dryRun, report, warnings,
                        ref before, ref after, ref written, ref skipped, ref unchanged);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            if (!dryRun)
            {
                // The reimports queued inside StartAssetEditing land here; sizes are read afterwards.
                AssetDatabase.Refresh();
                after = MeasureFolder();
            }

            var summary = new StringBuilder();
            summary.AppendLine(
                $"Compress Textures{(dryRun ? " (DRY RUN - nothing written)" : string.Empty)} - " +
                $"{written} {(dryRun ? "resolved" : "written")}, {unchanged} already current, " +
                $"{skipped} skipped (below {SkipBelowPixels / 1048576.0:0.#} MP, or unresolved)");
            summary.AppendLine(
                dryRun
                    ? $"  {Mb(before)} of .glb sub-asset textures would be extracted"
                    : $"  {Mb(before)} of .glb sub-asset textures  ->  {Mb(after)} compressed" +
                      (before > 0 ? $"   ({(double)before / Math.Max(after, 1):0.0}x)" : string.Empty));
            if (warnings.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine("WARNINGS");
                foreach (var warning in warnings) summary.AppendLine($"  {warning}");
            }

            summary.Append(report);

            var text = summary.ToString().TrimEnd();
            if (warnings.Count > 0) Debug.LogWarning(text);
            else Debug.Log(text);
            return text;
        }

        private static void CompressOne(
            string glbPath, bool force, bool dryRun, StringBuilder report, List<string> warnings,
            ref long before, ref long after, ref int written, ref int skipped, ref int unchanged)
        {
            var textures = AssetDatabase.LoadAllAssetsAtPath(glbPath).OfType<Texture2D>().ToList();
            if (textures.Count == 0) return;

            GlbImages images;
            try
            {
                images = ReadImages(glbPath);
            }
            catch (Exception exception)
            {
                warnings.Add($"{Path.GetFileName(glbPath)} - could not be read: {exception.Message}");
                return;
            }

            if (images.Count == 0)
            {
                warnings.Add($"{Path.GetFileName(glbPath)} - {textures.Count} imported texture(s) but no embedded images");
                return;
            }

            var stem = Path.GetFileNameWithoutExtension(glbPath);
            var folder = $"{GeneratedTextureFolder}/{Sanitize(stem)}";
            var sourceStamp = File.GetLastWriteTimeUtc(glbPath);
            var perFile = new List<string>();

            foreach (var texture in textures.OrderBy(t => t.name, StringComparer.Ordinal))
            {
                long size = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);
                if ((long)texture.width * texture.height < SkipBelowPixels)
                {
                    skipped++;
                    continue;
                }

                var index = ResolveImageIndex(texture, images, out var reason);
                if (index < 0)
                {
                    warnings.Add($"{stem} - {reason}");
                    skipped++;
                    continue;
                }

                before += size;

                var assetPath = $"{folder}/{AssetName(texture)}{images.Extension[index]}";
                var absolute = Path.GetFullPath(assetPath);

                if (dryRun)
                {
                    perFile.Add($"{texture.name} {texture.width}x{texture.height} {Mb(size)} -> {assetPath}");
                    written++;
                    continue;
                }

                if (!force && File.Exists(absolute) && File.GetLastWriteTimeUtc(absolute) >= sourceStamp)
                {
                    unchanged++;
                    continue;
                }

                if (texture.wrapMode != TextureWrapMode.Repeat || texture.filterMode != FilterMode.Bilinear)
                    warnings.Add(
                        $"{stem} - \"{texture.name}\" imports as {texture.wrapMode}/{texture.filterMode}, but the " +
                        "extracted copy is forced to Repeat/Bilinear; teach GeneratedTextureImporter about it");

                Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
                File.WriteAllBytes(absolute, images.Data[index]);

                written++;
                perFile.Add($"{texture.name} {texture.width}x{texture.height} {Mb(size)}");
            }

            if (perFile.Count > 0)
            {
                report.AppendLine();
                report.AppendLine($"{stem} - {perFile.Count} texture(s)");
                foreach (var line in perFile.Take(6)) report.AppendLine($"  {line}");
                if (perFile.Count > 6) report.AppendLine($"  … and {perFile.Count - 6} more");
            }
        }

        // --- GLB container ---------------------------------------------------------------------

        private sealed class GlbImages
        {
            public readonly List<byte[]> Data = new();
            public readonly List<string> Name = new();
            public readonly List<string> Extension = new();
            public readonly List<int> Width = new();
            public readonly List<int> Height = new();
            public readonly List<bool> HasAlpha = new();
            public int Count => Data.Count;

            public void Add(string name, byte[] data, string extension)
            {
                Name.Add(name);
                Data.Add(data);
                Extension.Add(extension);
                ReadImageHeader(data, out var w, out var h, out var alpha);
                Width.Add(w);
                Height.Add(h);
                HasAlpha.Add(alpha);
            }
        }

        /// <summary>
        /// Reads the embedded image buffers straight out of the GLB container.
        ///
        /// A GLB is a 12-byte header followed by length-prefixed chunks: one JSON chunk describing
        /// the glTF, one BIN chunk holding every buffer. An embedded image is a <c>bufferView</c>
        /// into that BIN chunk whose bytes are already a complete PNG or JPEG file, so extracting
        /// one is a slice, not a conversion.
        ///
        /// Draco compression, which these districts use, only ever touches MESH buffer views -
        /// images are untouched by it, which is why this works on a Draco file at all.
        /// </summary>
        private static GlbImages ReadImages(string glbPath)
        {
            var bytes = File.ReadAllBytes(glbPath);
            var result = new GlbImages();

            if (bytes.Length < 12) throw new InvalidDataException("shorter than a GLB header");
            if (BitConverter.ToUInt32(bytes, 0) != 0x46546C67u) throw new InvalidDataException("not a GLB (bad magic)");

            byte[] json = null, bin = null;
            int offset = 12;
            while (offset + 8 <= bytes.Length)
            {
                int length = (int)BitConverter.ToUInt32(bytes, offset);
                uint type = BitConverter.ToUInt32(bytes, offset + 4);
                int start = offset + 8;
                if (start + length > bytes.Length) throw new InvalidDataException("chunk runs past end of file");

                if (type == 0x4E4F534Au) json = Slice(bytes, start, length);       // 'JSON'
                else if (type == 0x004E4942u) bin = Slice(bytes, start, length);   // 'BIN\0'

                offset = start + ((length + 3) & ~3); // chunks are 4-byte aligned
            }

            if (json == null) throw new InvalidDataException("no JSON chunk");

            var root = JsonUtility.FromJson<GltfJson>(Encoding.UTF8.GetString(json));
            if (root?.images == null) return result;

            for (int i = 0; i < root.images.Length; i++)
            {
                var image = root.images[i];
                var name = string.IsNullOrEmpty(image.name) ? $"image_{i}" : image.name;

                if (!string.IsNullOrEmpty(image.uri) || bin == null ||
                    root.bufferViews == null || image.bufferView < 0 || image.bufferView >= root.bufferViews.Length)
                {
                    // An image stored outside the container, or one we cannot reach. Recorded so the
                    // indices stay aligned with the glTF's own, and reported as unmatched later.
                    result.Add(name, null, ".png");
                    continue;
                }

                var view = root.bufferViews[image.bufferView];
                var data = Slice(bin, view.byteOffset, view.byteLength);
                result.Add(name, data, ExtensionFor(image.mimeType, data));
            }

            return result;
        }

        /// <summary>
        /// Maps an imported texture back to the glTF image it came from - by name AND by shape, and
        /// refusing rather than guessing when that is not enough.
        ///
        /// ⚠ NAMES ARE NOT UNIQUE. glTFast names a texture after its glTF IMAGE
        /// (<c>GltfImport.GetTextureName</c>), and these districts were assembled in Blender from
        /// stock texture packs, so the image names repeat: city 4 alone has seven images called
        /// "Untitled", two called "Street_Mashup_texture_normal" and two called
        /// "TCom_ConcretePlates0211_1_seamless_L.png". Binding by name alone would quietly point a
        /// wall at another wall's normal map - the kind of fault that looks like a lighting bug.
        ///
        /// So the pixel dimensions and the presence of an alpha channel are read straight out of the
        /// embedded file's own header - a few bytes, no decode - and used to narrow the candidates.
        /// A texture that still matches zero or more than one image is SKIPPED and named in the
        /// report, because staying uncompressed is merely heavy while binding the wrong image is
        /// wrong.
        /// </summary>
        private static int ResolveImageIndex(Texture2D texture, GlbImages images, out string reason)
        {
            // The suffix appears when one image is sampled through several glTF samplers.
            var bare = SamplerSuffix.Replace(texture.name, string.Empty);

            var indexed = IndexedName.Match(bare);
            if (indexed.Success && int.TryParse(indexed.Groups[1].Value, out var parsed))
            {
                reason = parsed >= 0 && parsed < images.Count && images.Data[parsed] != null
                    ? null
                    : $"\"{texture.name}\" names image index {parsed}, which the container does not have";
                return reason == null ? parsed : -1;
            }

            var candidates = new List<int>();
            for (int i = 0; i < images.Count; i++)
            {
                if (images.Data[i] != null && string.Equals(images.Name[i], bare, StringComparison.Ordinal))
                    candidates.Add(i);
            }

            if (candidates.Count == 0)
            {
                reason = $"\"{texture.name}\" matches no embedded image";
                return -1;
            }

            if (candidates.Count > 1)
            {
                var bySize = candidates
                    .Where(i => images.Width[i] == texture.width && images.Height[i] == texture.height)
                    .ToList();
                if (bySize.Count > 0) candidates = bySize;
            }

            if (candidates.Count > 1)
            {
                bool wantsAlpha =
                    UnityEngine.Experimental.Rendering.GraphicsFormatUtility.HasAlphaChannel(texture.graphicsFormat);
                var byAlpha = candidates.Where(i => images.HasAlpha[i] == wantsAlpha).ToList();
                if (byAlpha.Count > 0) candidates = byAlpha;
            }

            if (candidates.Count > 1)
            {
                reason =
                    $"\"{texture.name}\" {texture.width}x{texture.height} matches {candidates.Count} embedded " +
                    "images that are alike in name, size and alpha - left uncompressed rather than guessed";
                return -1;
            }

            reason = null;
            return candidates[0];
        }

        /// <summary>
        /// Pixel size and alpha, straight from a PNG or JPEG header. No decode - this runs on every
        /// embedded image in every district and one of them is 16000x2000.
        ///
        /// Returns zeroes for anything unrecognised, which simply makes the size filter in
        /// <see cref="ResolveImageIndex"/> decline to narrow rather than narrow wrongly.
        /// </summary>
        private static void ReadImageHeader(byte[] data, out int width, out int height, out bool hasAlpha)
        {
            width = height = 0;
            hasAlpha = false;
            if (data == null) return;

            // PNG: 8-byte signature, then the IHDR chunk - length, type, width, height, bit depth,
            // colour type. Colour types 4 (grey+alpha) and 6 (RGBA) carry alpha.
            if (data.Length >= 26 && data[0] == 0x89 && data[1] == 0x50 &&
                data[12] == 'I' && data[13] == 'H' && data[14] == 'D' && data[15] == 'R')
            {
                width = BigEndian32(data, 16);
                height = BigEndian32(data, 20);
                hasAlpha = data[25] == 4 || data[25] == 6;
                return;
            }

            // JPEG: walk the marker segments to the start-of-frame, which is the only one carrying
            // the dimensions. JPEG has no alpha channel at all.
            if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
            {
                int offset = 2;
                while (offset + 9 < data.Length)
                {
                    if (data[offset] != 0xFF) { offset++; continue; }

                    byte marker = data[offset + 1];
                    if (marker == 0xFF) { offset++; continue; }
                    if (marker is 0xD8 or 0x01 || (marker >= 0xD0 && marker <= 0xD7)) { offset += 2; continue; }

                    int length = (data[offset + 2] << 8) | data[offset + 3];
                    bool isStartOfFrame =
                        marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);
                    if (isStartOfFrame)
                    {
                        height = (data[offset + 5] << 8) | data[offset + 6];
                        width = (data[offset + 7] << 8) | data[offset + 8];
                        return;
                    }

                    if (marker == 0xDA) return; // start of scan - no SOF found
                    offset += 2 + length;
                }
            }
        }

        private static int BigEndian32(byte[] data, int offset) =>
            (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

        private static string ExtensionFor(string mimeType, byte[] data)
        {
            if (mimeType == "image/png") return ".png";
            if (mimeType == "image/jpeg") return ".jpg";

            // No mimeType is legal for a bufferView image in practice, so sniff the magic instead of
            // guessing: a wrong extension imports as nothing at all.
            if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50) return ".png";
            if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8) return ".jpg";
            return ".png";
        }

        private static byte[] Slice(byte[] source, int offset, int length)
        {
            var slice = new byte[length];
            Buffer.BlockCopy(source, offset, slice, 0, length);
            return slice;
        }

        [Serializable]
        private class GltfJson
        {
            public GltfImage[] images;
            public GltfBufferView[] bufferViews;
        }

        [Serializable]
        private class GltfImage
        {
            public string name;
            public string mimeType;
            public string uri;
            public int bufferView = -1;
        }

        [Serializable]
        private class GltfBufferView
        {
            public int byteOffset;
            public int byteLength;
        }

        // --- helpers ----------------------------------------------------------------------------

        private static long MeasureFolder()
        {
            if (!AssetDatabase.IsValidFolder(GeneratedTextureFolder)) return 0;

            long total = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { GeneratedTextureFolder }))
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
                if (texture != null) total += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);
            }

            return total;
        }

        /// <summary>
        /// The extracted file's name, without extension. It is the ONLY record of how the texture
        /// must be imported, so it carries two things beyond the name.
        ///
        /// The pixel size, because the texture NAME is not unique inside one .glb - see
        /// <see cref="ResolveImageIndex"/>. Two same-named textures that both resolve (they resolve
        /// precisely because they differ in size) would otherwise land on one path and the second
        /// would silently overwrite the first.
        ///
        /// And <see cref="GeneratedTextureImporter.LinearSuffix"/> when the texture is data rather
        /// than colour, taken from the flag GLTFAST ITSELF RESOLVED. Re-deriving it from the glTF's
        /// material roles would risk disagreeing with the thing actually doing the sampling, and a
        /// roughness map read as sRGB is the same class of fault as memory
        /// <c>gltfast-basecolorfactor-gamma</c>, one stage further down the pipeline.
        ///
        /// <see cref="WorldBuilder"/> rebuilds this same name from the imported texture when it goes
        /// looking for the compressed twin, so the two must not drift apart.
        /// </summary>
        public static string AssetName(Texture2D texture)
        {
            bool sRgb = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat);
            var suffix = sRgb ? string.Empty : GeneratedTextureImporter.LinearSuffix;
            return $"{Sanitize(texture.name)}_{texture.width}x{texture.height}{suffix}";
        }

        private static string Sanitize(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var c in name) sb.Append(char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_');
            return sb.ToString();
        }

        private static string Mb(long bytes) => $"{bytes / 1048576.0:0.#} MB";
    }
}
