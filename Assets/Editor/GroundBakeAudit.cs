using System.Collections.Generic;
using System.IO;
using System.Text;
using TheBlock.Npc;
using TheBlock.Traffic;
using UnityEditor;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// <b>The Block → Audit Baked Ground</b> - re-derives every height that was baked with
    /// <see cref="WorldBuilder.GroundY"/> and reports the ones that no longer agree.
    ///
    /// <b>Why this exists.</b> U38 found that <c>GroundY</c> recognised the ground plate by an
    /// equality test on the name <c>Ground Floor</c>, and the plate had quietly become three objects
    /// when the sea was added - <c>Ground Floor Seaward North</c> and <c>… South</c>. In those two
    /// regions the plate was therefore read as a STREET, and everything baked there came back at
    /// −0.05 instead of the district surface. The test is a prefix test now and the crowd was
    /// re-baked, but the ledger recorded a WARNING rather than a measurement: *"zebras, lanes and
    /// anything else baked against those regions still carry the same error"*. That sentence is a
    /// hypothesis, and an inherited hypothesis is exactly the thing this project has been bitten by
    /// before.
    ///
    /// So this settles it by arithmetic instead of by memory. It is READ-ONLY: it changes nothing,
    /// re-bakes nothing, and can be run at any time to prove the world's baked heights are still
    /// what a fresh raycast would produce.
    ///
    /// <b>It audits the whole world, not only the seaward strip.</b> Scoping it to the region that
    /// caused the bug would only ever confirm the bug you already know about - and the first run of
    /// this audit found its worst offender, a traffic light hanging 9.9 m in the air, by looking
    /// everywhere.
    ///
    /// Writes <c>Library/TheBlock/ground-audit.txt</c> and a summary to the console.
    /// </summary>
    public static class GroundBakeAudit
    {
        private const string OutputPath = "Library/TheBlock/ground-audit.txt";

        /// <summary>
        /// How far a baked height may sit from a fresh probe before it is worth reporting, metres.
        ///
        /// One centimetre. The errors this is looking for are 12 cm (a person's ankles in the
        /// pavement), 16 cm (U38's own finding) and 9.9 m (a street light in the sky); anything
        /// under a centimetre is float noise in a raycast against a mesh.
        /// </summary>
        private const float Tolerance = 0.01f;

        [MenuItem("The Block/Audit Baked Ground", priority = 7)]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("GroundBakeAudit: stop Play mode first - a raycast during Play hits " +
                               "moving cars and pedestrians, which are not the floor.");
                return;
            }

            var report = new StringBuilder();
            report.AppendLine($"Ground bake audit - {System.DateTime.Now:yyyy-MM-dd HH:mm}, " +
                              $"tolerance {Tolerance:0.00} m");

            var seaward = SeawardRegions(report);
            report.AppendLine();

            int total = 0;

            total += AuditPoints(report, "crowd seeds", CrowdSeedPoints(), seaward);
            total += AuditPoints(report, "crowd lane points", CrowdLanePoints(), seaward);
            total += AuditPoints(report, "traffic edge points", TrafficEdgePoints(), seaward);
            total += AuditPoints(report, "traffic nodes", TrafficNodePoints(), seaward);
            total += AuditPoints(report, "zebra crossings", ScenePoints("Crossing_"), seaward);
            total += AuditPoints(report, "traffic lights", ScenePoints("Light_"), seaward);
            total += AuditPoints(report, "road carves", ScenePoints("Carve_"), seaward);

            report.AppendLine();
            report.AppendLine(total == 0
                ? "CLEAN - every baked height agrees with a fresh GroundY."
                : $"{total} baked height(s) disagree with a fresh GroundY. Each is listed above with " +
                  "its position, what is stored, and what a fresh probe returns.");

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            File.WriteAllText(OutputPath, report.ToString());
            Debug.Log(report + $"\nFull report: {OutputPath}");
        }

        /// <summary>
        /// The two footprints the bug lived in, read off the colliders rather than hard-coded.
        ///
        /// Hard-coding them would be a fourth copy of a number that has already moved once. Points
        /// inside these are flagged in the output, because a defect there has a known cause and a
        /// defect outside them is something new.
        /// </summary>
        private static List<Bounds> SeawardRegions(StringBuilder report)
        {
            var regions = new List<Bounds>();
            foreach (var collider in Object.FindObjectsByType<Collider>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (collider == null || !collider.name.StartsWith("Ground Floor Seaward")) continue;
                regions.Add(collider.bounds);
                var b = collider.bounds;
                report.AppendLine($"  seaward region '{collider.name}': " +
                                  $"x [{b.min.x:0}, {b.max.x:0}]  z [{b.min.z:0}, {b.max.z:0}]");
            }
            if (regions.Count == 0) report.AppendLine("  no 'Ground Floor Seaward *' colliders found");
            return regions;
        }

        private static bool Inside(List<Bounds> regions, Vector3 point)
        {
            foreach (var b in regions)
                if (point.x >= b.min.x && point.x <= b.max.x && point.z >= b.min.z && point.z <= b.max.z)
                    return true;
            return false;
        }

        /// <summary>Re-probes every point and reports the disagreements. Returns how many.</summary>
        private static int AuditPoints(StringBuilder report, string label,
                                       List<(string Name, Vector3 Point)> points, List<Bounds> seaward)
        {
            int wrong = 0, inBand = 0;
            float worst = 0f;

            foreach (var (name, point) in points)
            {
                bool band = Inside(seaward, point);
                if (band) inBand++;

                float fresh = WorldBuilder.GroundY(point);
                float delta = fresh - point.y;
                if (Mathf.Abs(delta) <= Tolerance) continue;

                wrong++;
                if (Mathf.Abs(delta) > Mathf.Abs(worst)) worst = delta;
                report.AppendLine($"    {name} at ({point.x:0.0}, {point.z:0.0}) " +
                                  $"stored y {point.y:0.000}, fresh {fresh:0.000}, " +
                                  $"off by {delta:+0.000;-0.000} m{(band ? "  [seaward]" : string.Empty)}");
            }

            report.AppendLine($"  {label}: {points.Count} baked, {inBand} in the seaward band, " +
                              (wrong == 0 ? "ALL AGREE" : $"{wrong} DISAGREE (worst {worst:+0.000;-0.000} m)"));
            return wrong;
        }

        // --- the baked sources ----------------------------------------------------------------

        private static CrowdSeedTable Crowd() =>
            AssetDatabase.LoadAssetAtPath<CrowdSeedTable>("Assets/Npc/Generated/CrowdSeeds.asset");

        private static TrafficNetwork Traffic() =>
            AssetDatabase.LoadAssetAtPath<TrafficNetwork>("Assets/Traffic/Generated/TrafficNetwork.asset");

        private static List<(string, Vector3)> CrowdSeedPoints()
        {
            var list = new List<(string, Vector3)>();
            var table = Crowd();
            if (table == null) return list;
            for (int i = 0; i < table.Seeds.Length; i++) list.Add(($"seed {i}", table.Seeds[i].Position));
            return list;
        }

        private static List<(string, Vector3)> CrowdLanePoints()
        {
            var list = new List<(string, Vector3)>();
            var table = Crowd();
            if (table == null) return list;
            for (int p = 0; p < table.Paths.Length; p++)
                for (int i = 0; i < table.Paths[p].Points.Length; i++)
                    list.Add(($"lane {p} point {i}", table.Paths[p].Points[i]));
            return list;
        }

        private static List<(string, Vector3)> TrafficEdgePoints()
        {
            var list = new List<(string, Vector3)>();
            var net = Traffic();
            if (net == null) return list;
            for (int e = 0; e < net.Edges.Length; e++)
                for (int i = 0; i < net.Edges[e].Points.Length; i++)
                    list.Add(($"edge {e} point {i}", net.Edges[e].Points[i]));
            return list;
        }

        private static List<(string, Vector3)> TrafficNodePoints()
        {
            var list = new List<(string, Vector3)>();
            var net = Traffic();
            if (net == null) return list;
            for (int n = 0; n < net.Nodes.Length; n++) list.Add(($"node {n}", net.Nodes[n].Position));
            return list;
        }

        /// <summary>
        /// Scene objects whose transform Y came from a bake, found by name prefix.
        ///
        /// By prefix and not by component, because these are plain <c>GameObject</c>s the builders
        /// position - there is no <c>BakedHeight</c> marker to look for, and adding one now would be
        /// a change to the world in a tool whose whole value is that it changes nothing.
        /// </summary>
        private static List<(string, Vector3)> ScenePoints(string prefix)
        {
            var list = new List<(string, Vector3)>();
            foreach (var t in Object.FindObjectsByType<Transform>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t != null && t.name.StartsWith(prefix)) list.Add((t.name, t.position));
            return list;
        }
    }
}
