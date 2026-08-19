using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// <b>The Block → Geometry Census</b> - names what is holding the frame, instead of guessing.
    ///
    /// U30b round 2 established the headline: <b>the frame is bound by GEOMETRY, not pixels</b>. A
    /// resolution sweep from 7.31 MP to 0.46 MP - sixteen times fewer pixels - moved it by 11 ms out
    /// of 78. So every remaining lever is a triangle lever, and the campaign needs the same thing for
    /// geometry that <see cref="TextureCensus"/> gave it for memory: a ranked list of files, not an
    /// adjective.
    ///
    /// The jetski is the proof this is worth writing. It was found <i>by hand</i>, by someone opening
    /// one asset and looking: 1,190,600 triangles, twice in the scene, a quarter of the world's
    /// geometry. Nothing was wrong with any number anywhere - it was an asset nobody had looked at.
    /// This looks at all of them.
    ///
    /// <b>What it counts, and why it counts it that way.</b> The cost that matters is not a mesh's
    /// triangle count, it is <c>triangles × instances in the scene</c>: a 5 k prop placed 233 times
    /// outweighs a 100 k landmark placed once. Rows are ranked on that product. Shadow casters are
    /// tracked separately because a caster is drawn again per cascade - round 2 measured 21.9 M
    /// triangles drawn against 9.9 M present, and the gap is the cascades.
    ///
    /// <b>Runs in Edit mode, on the open scene, and touches nothing.</b> No Play mode, no build, no
    /// asset writes - unlike the texture census, everything here is knowable statically.
    /// <c>editor-cannot-measure-texture-memory</c> is the standing warning that Editor-side memory
    /// figures are fiction; triangle counts are not, so this stays strictly geometric and reports no
    /// byte figure at all.
    ///
    /// Writes <c>Library/TheBlock/geometry-census.txt</c> and a summary to the console.
    /// </summary>
    public static class GeometryCensus
    {
        private const string OutputPath = "Library/TheBlock/geometry-census.txt";
        private const int TopN = 80;

        [MenuItem("The Block/Geometry Census", priority = 6)]
        public static void Run()
        {
            var byMesh = new Dictionary<Mesh, Row>();
            int renderers = 0, skinned = 0, slots = 0, inactive = 0, missingMesh = 0;

            foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (renderer == null) continue;

                // Inactive geometry is real work for the builders and no work for the frame. Counted
                // on its own line so a big number here is not silently read as a frame cost.
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) { inactive++; continue; }

                Mesh mesh = MeshOf(renderer);
                if (mesh == null) { missingMesh++; continue; }

                renderers++;
                slots += renderer.sharedMaterials.Length;
                if (renderer is SkinnedMeshRenderer) skinned++;

                if (!byMesh.TryGetValue(mesh, out var row))
                {
                    row = new Row
                    {
                        Mesh = mesh,
                        Name = mesh.name,
                        Path = AssetDatabase.GetAssetPath(mesh),
                        Triangles = TrianglesOf(mesh),
                        SubMeshes = mesh.subMeshCount,
                    };
                }

                row.Instances++;
                if (renderer is SkinnedMeshRenderer) row.SkinnedInstances++;
                if (renderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)
                    row.CastingInstances++;
                if (renderer.GetComponentInParent<LODGroup>() != null) row.LodInstances++;
                if (row.Sample == null) row.Sample = Path(renderer.transform);

                byMesh[mesh] = row;
            }

            var rows = byMesh.Values.OrderByDescending(r => (long)r.Triangles * r.Instances).ToList();

            long present = rows.Sum(r => (long)r.Triangles * r.Instances);
            long casting = rows.Sum(r => (long)r.Triangles * r.CastingInstances);
            long unLodded = rows.Where(r => r.LodInstances == 0).Sum(r => (long)r.Triangles * r.Instances);

            var report = new StringBuilder();
            report.AppendLine($"Geometry census - scene '{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}', " +
                              $"{System.DateTime.Now:yyyy-MM-dd HH:mm}");
            report.AppendLine($"  {renderers} enabled renderers ({skinned} skinned), {slots} material slots, " +
                              $"{rows.Count} distinct meshes, {inactive} inactive renderers skipped, " +
                              $"{missingMesh} with no mesh.");
            report.AppendLine($"  {Millions(present)} triangles present, of which {Millions(casting)} cast shadows " +
                              $"and {Millions(unLodded)} sit under no LODGroup.");
            report.AppendLine();

            report.AppendLine("== By asset (the file to open) ==");
            foreach (var g in rows.GroupBy(r => r.Path)
                         .Select(g => new { Path = g.Key, Tris = g.Sum(r => (long)r.Triangles * r.Instances), Meshes = g.Count() })
                         .OrderByDescending(g => g.Tris)
                         .Take(30))
                report.AppendLine($"  {Millions(g.Tris),9}  {g.Meshes,4} mesh  {Label(g.Path)}");
            report.AppendLine();

            report.AppendLine($"== Top {TopN} meshes, ranked by triangles × instances ==");
            report.AppendLine($"  {"total",10}  {"tris",9}  {"n",4}  {"cast",4}  {"lod",4}  {"sub",3}  mesh / sample object");
            foreach (var r in rows.Take(TopN))
                report.AppendLine($"  {Millions((long)r.Triangles * r.Instances),10}  {r.Triangles,9:n0}  {r.Instances,4}  " +
                                  $"{r.CastingInstances,4}  {(r.LodInstances == r.Instances ? "all" : r.LodInstances.ToString()),4}  " +
                                  $"{r.SubMeshes,3}  {r.Name}  [{Label(r.Path)}]  {r.Sample}");
            report.AppendLine();

            report.AppendLine("== Worst offenders with NO LODGroup on any instance ==");
            report.AppendLine("   (the jetski class of fault: full detail at every distance)");
            foreach (var r in rows.Where(r => r.LodInstances == 0 && r.Triangles >= 20000).Take(30))
                report.AppendLine($"  {Millions((long)r.Triangles * r.Instances),10}  {r.Triangles,9:n0} × {r.Instances,-3}  " +
                                  $"{r.Name}  [{Label(r.Path)}]");

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(OutputPath));
            File.WriteAllText(OutputPath, report.ToString());

            var summary = new StringBuilder();
            foreach (var line in report.ToString().Split('\n'))
            {
                if (line.StartsWith("== Top")) break;
                summary.AppendLine(line);
            }
            summary.AppendLine($"Full table: {OutputPath}");
            Debug.Log(summary.ToString());
        }

        private static Mesh MeshOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer smr) return smr.sharedMesh;
            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        /// <summary>
        /// Triangles by index count, never <c>mesh.triangles.Length</c>.
        ///
        /// The array property allocates a managed copy of every index in the mesh - on a 1.19 M
        /// triangle asset that is 14 MB of garbage per call, and this walks the whole scene. The
        /// index count is metadata and costs nothing. Non-triangle topologies (the roads are quads
        /// nowhere, but a stray line-strip gizmo mesh would be) contribute zero rather than a wrong
        /// third of their index count.
        /// </summary>
        private static int TrianglesOf(Mesh mesh)
        {
            int total = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
                if (mesh.GetTopology(i) == MeshTopology.Triangles)
                    total += (int)(mesh.GetIndexCount(i) / 3);
            return total;
        }

        /// <summary>Assets built into the scene have no path; say so rather than printing empty.</summary>
        private static string Label(string path) => string.IsNullOrEmpty(path) ? "(scene mesh)" : path;

        private static string Path(Transform t)
        {
            var parts = new List<string>();
            for (var cur = t; cur != null && parts.Count < 4; cur = cur.parent) parts.Add(cur.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string Millions(long triangles) => (triangles / 1000000f).ToString("0.00") + "M";

        private class Row
        {
            public Mesh Mesh;
            public string Name, Path, Sample;
            public int Triangles, SubMeshes;
            public int Instances, SkinnedInstances, CastingInstances, LodInstances;
        }
    }
}
