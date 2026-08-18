using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// <b>The Block → Texture Census</b> - names what is holding texture memory, instead of guessing.
    ///
    /// U30b's first memory finding was a number with no name on it: <c>Texture.nonStreamingTextureMemory</c>
    /// read 1,225 MB in the Editor while U15's extracted district textures - the only ones anyone had
    /// ever set streaming on - accounted for a fraction of it. This walks every loaded <c>Texture</c>,
    /// reads its runtime size off the Profiler, and groups the total by the folder the asset came
    /// from, so the next step is "fix these forty files" and not "textures are big".
    ///
    /// Run it IN PLAY MODE, after the world has loaded: the set of textures resident then is the set
    /// a Player holds, plus the Editor's own UI atlases, which are grouped under <c>(editor)</c> and
    /// ignored. Writes the full table to <c>Library/TheBlock/texture-census.txt</c> and a summary to
    /// the console.
    /// </summary>
    public static class TextureCensus
    {
        private const string OutputPath = "Library/TheBlock/texture-census.txt";
        private const int TopN = 60;

        [MenuItem("The Block/Texture Census", priority = 5)]
        public static void Run()
        {
            var rows = new List<Row>();
            foreach (var texture in Resources.FindObjectsOfTypeAll<Texture>())
            {
                if (texture == null) continue;
                var path = AssetDatabase.GetAssetPath(texture);
                rows.Add(new Row
                {
                    Name = texture.name,
                    Path = string.IsNullOrEmpty(path) ? "(runtime)" : path,
                    Kind = texture.GetType().Name,
                    Width = texture.width,
                    Height = texture.height,
                    Format = FormatOf(texture),
                    Mips = texture.mipmapCount,
                    Streaming = texture is Texture2D t2 && t2.streamingMipmaps,
                    Bytes = Profiler.GetRuntimeMemorySizeLong(texture),
                    Editor = IsEditorOwned(texture, path),
                });
            }

            rows.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));

            long total = rows.Sum(r => r.Bytes);
            long editor = rows.Where(r => r.Editor).Sum(r => r.Bytes);
            long game = total - editor;
            long streaming = rows.Where(r => !r.Editor && r.Streaming).Sum(r => r.Bytes);
            long nonStreaming = game - streaming;

            var report = new StringBuilder();
            report.AppendLine($"Texture census - {rows.Count} textures loaded, {Mb(total)} total, " +
                              $"{Mb(game)} game ({Mb(streaming)} streaming / {Mb(nonStreaming)} NON-streaming), " +
                              $"{Mb(editor)} editor-owned (ignored below).");
            report.AppendLine($"Unity says: current {Mb((long)Texture.currentTextureMemory)}, " +
                              $"desired {Mb((long)Texture.desiredTextureMemory)}, " +
                              $"nonStreaming {Mb((long)Texture.nonStreamingTextureMemory)}, " +
                              $"total {Mb((long)Texture.totalTextureMemory)}.");
            report.AppendLine();

            report.AppendLine("== By folder (game textures only) ==");
            var byFolder = rows.Where(r => !r.Editor)
                .GroupBy(r => FolderOf(r.Path))
                .Select(g => new
                {
                    Folder = g.Key,
                    Bytes = g.Sum(r => r.Bytes),
                    Count = g.Count(),
                    NonStreaming = g.Where(r => !r.Streaming).Sum(r => r.Bytes),
                })
                .OrderByDescending(g => g.Bytes);
            foreach (var g in byFolder)
                report.AppendLine($"  {Mb(g.Bytes),9}  ({Mb(g.NonStreaming),9} non-streaming)  {g.Count,4} tex  {g.Folder}");
            report.AppendLine();

            report.AppendLine($"== Top {TopN} (game textures only) ==");
            report.AppendLine($"  {"MB",8}  {"size",11}  {"format",-14}  {"mips",4}  {"strm",4}  {"kind",-14}  path");
            foreach (var r in rows.Where(r => !r.Editor).Take(TopN))
                report.AppendLine($"  {r.Bytes / 1048576f,8:0.0}  {r.Width + "x" + r.Height,11}  {r.Format,-14}  {r.Mips,4}  " +
                                  $"{(r.Streaming ? "yes" : "NO"),4}  {r.Kind,-14}  {r.Path}  [{r.Name}]");

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            File.WriteAllText(OutputPath, report.ToString());

            // The console gets the summary and the folder table; the full top-N is in the file.
            var summary = new StringBuilder();
            foreach (var line in report.ToString().Split('\n'))
            {
                if (line.StartsWith("== Top")) break;
                summary.AppendLine(line);
            }
            summary.AppendLine($"Full table: {OutputPath}");
            Debug.Log(summary.ToString());
        }

        private static string FormatOf(Texture texture) => texture switch
        {
            Texture2D t => t.format.ToString(),
            RenderTexture rt => rt.format.ToString(),
            Cubemap c => c.format.ToString(),
            Texture2DArray a => a.format.ToString(),
            _ => "-",
        };

        /// <summary>
        /// The first two path segments under <c>Assets/</c> - enough to separate the packs
        /// (<c>Assets/npc_casual_set_00/Textures</c>, <c>Assets/Textures/Generated</c>, ...) without
        /// listing every district folder on its own line.
        /// </summary>
        private static string FolderOf(string path)
        {
            if (path == "(runtime)") return path;
            var parts = path.Split('/');
            if (parts.Length <= 3) return string.Join("/", parts.Take(parts.Length - 1));
            return string.Join("/", parts.Take(3));
        }

        /// <summary>
        /// Editor UI atlases, icons and package-internal textures. Anything the Editor loads for
        /// its own windows has no asset path or lives outside <c>Assets/</c>; a Player never has it.
        /// </summary>
        private static bool IsEditorOwned(Texture texture, string path)
        {
            if ((texture.hideFlags & HideFlags.HideAndDontSave) == HideFlags.HideAndDontSave &&
                !(texture is RenderTexture)) return true;
            if (string.IsNullOrEmpty(path)) return false;                    // runtime-created: game
            if (path.StartsWith("Assets/")) return false;
            if (path.StartsWith("Packages/com.unity.render-pipelines")) return false; // URP's own LUTs
            return true;                                                      // Library/, Packages/ editor
        }

        private static string Mb(long bytes) => (bytes / 1048576f).ToString("0") + " MB";

        private struct Row
        {
            public string Name, Path, Kind, Format;
            public int Width, Height, Mips;
            public bool Streaming, Editor;
            public long Bytes;
        }
    }
}
