using System.Collections.Generic;
using System.Linq;
using TheBlock.Core;
using TheBlock.UI;
using TheBlock.Vehicles;
using TheBlock.World;
using UnityEditor;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// U28b's build pass: measures the Paz station's three pump meshes and hands them to
    /// <see cref="GasStation"/>, then makes sure <see cref="FuelSystem"/> and
    /// <see cref="FuelGauge"/> exist in the scene.
    ///
    /// <b>The opposite of U28's store pass, and the difference is the whole reason this file has to
    /// report numbers.</b> <c>seven-eleven-lot.glb</c> ships purpose-built marker empties carrying
    /// the config's own coordinates, so that pass can assert node-versus-config and get 0.0 cm.
    /// <c>gas-station.glb</c> ships none: 119 nodes and every one of them is geometry. The anchors
    /// here are RENDER-MESH PIVOTS — wherever the Sketchfab author left them — so there is nothing
    /// to check them against except each other, the station origin, and the two forecourt points
    /// <c>config.traffic.gasStops</c> already exports for the traffic AI. This pass prints all
    /// three comparisons, and machine-checks the one property that actually matters: that the new
    /// per-pump trigger is a SUPERSET of the web build's single 9 m circle.
    /// </summary>
    public static partial class WorldBuilder
    {
        /// <summary>The prefix every pump mesh shares. See <see cref="FindPumps"/> for why not the full name.</summary>
        private const string PumpNodePrefix = "gas pump";

        /// <summary>How many points on the web's circle the superset check samples.</summary>
        private const int SupersetSamples = 64;

        /// <summary>
        /// Re-wires just the gas station, against the world already in the scene — <b>The Block →
        /// Build Gas Station</b>.
        ///
        /// A full <b>Build World</b> does this too, and would also re-instantiate nine districts, the
        /// roads, the traffic graph and the NavMesh bake to configure three components — the same
        /// argument U28's <b>Build Store</b> made.
        ///
        /// <b>It also installs the HUD gauge, and that is not tidiness — it is a safety rail.</b>
        /// <see cref="HudBuilder"/> destroys the <c>HUD</c> GameObject before rebuilding it, and
        /// U26's <c>MenuBuilder</c> puts every menu component on that same object. So running
        /// <b>Build Map HUD</b> to pick up one new HUD element would delete the entire title/pause/
        /// settings shell. Installing the gauge from here means nobody has to.
        /// </summary>
        [MenuItem("The Block/Build Gas Station", priority = 8)]
        public static void BuildGasStationOnly()
        {
            var snapshot = TheBlockConfig.Load(reload: true);
            if (snapshot?.Fuel == null)
            {
                Debug.LogError("Build Gas Station: the snapshot has no `fuelConfig`. Re-run tools/export-config.sh.");
                return;
            }

            var instance = GameObject.Find("Place_GasStation");
            if (instance == null)
            {
                Debug.LogError("Build Gas Station: no Place_GasStation in the scene. Run The Block → Build World.");
                return;
            }

            var root = Object.FindAnyObjectByType<WorldRoot>();
            if (root == null)
            {
                Debug.LogError("Build Gas Station: no WorldRoot in the scene. Run The Block → Build World.");
                return;
            }

            var report = new Report();
            BuildGasStation(instance, snapshot, report);
            EnsureFuel(root.transform, report);

            // A component add does not dirty the scene by itself, and an unmarked scene is not
            // written by Save — the station would live in memory until the next domain reload.
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            // The count is in the HEADER, not just implied by the lines below it. The first run of
            // this pass reported a failing superset check and printed no WARNING line at all, and
            // there was no way to tell a silent warning list from an empty one by reading the log.
            var text = new System.Text.StringBuilder(
                $"Build Gas Station — {report.Warnings.Count} warning(s)\n");
            foreach (var line in report.Placed) text.AppendLine("  " + line);
            foreach (var line in report.Notes) text.AppendLine("  " + line);
            foreach (var line in report.Warnings) text.AppendLine("  WARNING " + line);

            if (report.Warnings.Count > 0) Debug.LogWarning(text.ToString().TrimEnd());
            else Debug.Log(text.ToString().TrimEnd());
        }

        /// <summary>
        /// Finds the pumps, measures them, and configures the component. Called from the menu item
        /// above and from the full <b>Build World</b> pass.
        /// </summary>
        private static void BuildGasStation(GameObject instance, TheBlockConfig.Snapshot snapshot, Report report)
        {
            if (instance == null) return;

            if (!instance.TryGetComponent<GasStation>(out var station))
                station = instance.AddComponent<GasStation>();

            var pumps = FindPumps(instance);
            station.Configure(pumps, snapshot.Fuel.PumpRadius);
            EditorUtility.SetDirty(station);

            var centre = instance.transform.position;
            report.Placed.Add($"gas station — GasStation on '{instance.name}' at " +
                              $"({centre.x:0.00}, {centre.y:0.00}, {centre.z:0.00}), " +
                              $"stationRadius {snapshot.Fuel.PumpRadius:0.0} m, {pumps.Length} pump(s)");

            if (pumps.Length != 3)
                report.Warnings.Add($"gas station — expected 3 '{PumpNodePrefix}*' nodes, found {pumps.Length}. " +
                                    "A re-export may have renamed them; the station circle still works alone.");

            MeasurePumps(pumps, centre, report);
            CrossCheckTrafficStops(station, snapshot, report);
            AssertSuperset(station, centre, snapshot.Fuel.PumpRadius, report);
        }

        /// <summary>
        /// The three pump meshes, ordered left to right along world X so the report is stable run to
        /// run.
        ///
        /// <b>Matched by PREFIX, never by full name.</b> The nodes import as <c>gas pump_7</c>,
        /// <c>gas pump.001_11</c> and <c>gas pump.002_15</c>, and those trailing numbers are glTF
        /// NODE INDICES — a re-export that adds or removes a single node renumbers them all. The
        /// prefix is stable and unambiguous here: the pumps' own children are <c>Object_NN</c> and
        /// the nozzle groups are <c>gas_handles*</c>, with an underscore, so neither can match.
        /// </summary>
        private static Transform[] FindPumps(GameObject instance)
        {
            return instance.GetComponentsInChildren<Transform>(includeInactive: true)
                .Where(t => t.name.StartsWith(PumpNodePrefix, System.StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.position.x)
                .ToArray();
        }

        /// <summary>
        /// Prints each pump's position, its XZ distance from the station centre, and — the part that
        /// catches a re-export — how far its own origin sits from the centre of the geometry hanging
        /// off it. A pivot far from its mesh means the anchor is not where the nozzle is.
        /// </summary>
        private static void MeasurePumps(IReadOnlyList<Transform> pumps, Vector3 centre, Report report)
        {
            for (var i = 0; i < pumps.Count; i++)
            {
                var pump = pumps[i];
                var flat = FlatDistance(pump.position, centre);

                var pivotDelta = -1f;
                var renderers = pump.GetComponentsInChildren<Renderer>(includeInactive: true);
                if (renderers.Length > 0)
                {
                    var bounds = renderers[0].bounds;
                    for (var r = 1; r < renderers.Length; r++) bounds.Encapsulate(renderers[r].bounds);
                    pivotDelta = FlatDistance(pump.position, bounds.center);
                }

                report.Notes.Add($"gas station — pump {i} '{pump.name}' at " +
                                 $"({pump.position.x:0.000}, {pump.position.y:0.000}, {pump.position.z:0.000}), " +
                                 $"{flat:0.00} m from centre, pivot↔bounds {pivotDelta:0.00} m");

                if (pivotDelta > 1.5f)
                    report.Warnings.Add($"gas station — pump {i} pivot is {pivotDelta:0.00} m from its own " +
                                        "geometry. A re-export may have moved it; check the anchor by eye.");
            }
        }

        /// <summary>
        /// A free sanity check on the anchors: <c>config.traffic.gasStops.points</c> are where the
        /// TRAFFIC AI pulls in, already exported and already in the right frame. They are not the
        /// player's trigger — the web build's own source calls their 4 m radius "far too tight" for
        /// a person — but they are two independently-authored points on this forecourt, so both must
        /// fall inside the predicate.
        /// </summary>
        private static void CrossCheckTrafficStops(GasStation station, TheBlockConfig.Snapshot snapshot, Report report)
        {
            var stops = snapshot.Config?.Traffic?.GasStops?.Points;
            if (stops == null || stops.Count == 0)
            {
                report.Notes.Add("gas station — no traffic.gasStops to cross-check against");
                return;
            }

            for (var i = 0; i < stops.Count; i++)
            {
                var point = Convert.Pos(stops[i].X, station.Centre.y, stops[i].Z);
                var inside = station.AtPump(point);
                var line = $"gas station — traffic stop {i} at ({point.x:0.0}, {point.z:0.0}) " +
                           $"{(inside ? "inside" : "OUTSIDE")} the refuel area";
                if (inside) report.Notes.Add(line);
                else report.Warnings.Add(line);
            }
        }

        /// <summary>
        /// <b>The assertion this unit exists to keep honest.</b> The per-pump circles are an
        /// improvement only if they never take anything away, so every point the web build's single
        /// circle accepted must still be accepted here. Swept as a solid disc rather than a rim:
        /// "superset" is a claim about the whole area, and a rim-only check would pass a predicate
        /// that had somehow lost its middle.
        ///
        /// <b>Sampled at 0.999 of the radius, and that 0.001 is not a fudge.</b> Measured: a point
        /// built as <c>centre + (cos θ, sin θ) · 9</c> comes back at squared distance
        /// <c>81.000160</c> against a threshold of <c>81</c> — <c>Mathf.Cos</c>/<c>Sin</c> rounding
        /// puts 12 of 64 rim samples microscopically OUTSIDE the very circle they were generated on,
        /// and the first run of this check reported 52/64 for it. The boundary itself is measure-zero
        /// and both builds compare it the same way; the area is what the claim is about.
        ///
        /// The negative sample is the other half: a point past the rim, and away from every pump,
        /// must be refused, or the predicate has stopped being a predicate.
        /// </summary>
        private static void AssertSuperset(GasStation station, Vector3 centre, float stationRadius, Report report)
        {
            const int rings = 9;
            var accepted = 0;
            var total = 0;
            for (var ring = 1; ring <= rings; ring++)
            {
                var radius = stationRadius * (ring / (float)rings) * 0.999f;
                for (var i = 0; i < SupersetSamples; i++)
                {
                    var a = i / (float)SupersetSamples * 2f * Mathf.PI;
                    var p = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                    total++;
                    if (station.AtPump(p)) accepted++;
                }
            }

            // Straight out in +Z: the pumps are in a line along X, so this direction leaves every
            // pump circle at the same time it leaves the station's.
            var outside = centre + new Vector3(0f, 0f, stationRadius + 0.1f);
            var refused = !station.AtPump(outside);

            report.Notes.Add($"gas station — superset check {accepted}/{total} across the " +
                             $"{stationRadius:0.0} m disc, and the point past it is {(refused ? "refused" : "ACCEPTED")}");

            if (accepted < total)
                report.Warnings.Add($"gas station — the per-pump area is NOT a superset of the web's circle " +
                                    $"({accepted}/{total}). The station clause must stay in AtPump.");
            if (!refused)
                report.Warnings.Add("gas station — a point outside every circle was accepted. AtPump is wrong.");
        }

        /// <summary>
        /// Makes sure the fuel economy has a component to live on, and the HUD has its gauge.
        ///
        /// <see cref="FuelSystem"/> goes wherever <c>PowerUps</c> already is — they are the same
        /// concern, both outlive the building you are standing in, and U28 already argued the case
        /// for that group. <b>Only calls <c>NewGroup</c> as a last resort</b>: it always creates a
        /// fresh object, so calling it after <c>EnsureEconomy</c> has just made <c>Game</c> would
        /// leave two of them.
        /// </summary>
        private static void EnsureFuel(Transform root, Report report)
        {
            var existing = Object.FindAnyObjectByType<FuelSystem>();
            if (existing != null)
            {
                report.Notes.Add($"fuel — FuelSystem already on '{existing.gameObject.name}'");
            }
            else
            {
                var host = Object.FindAnyObjectByType<Powerup.PowerUps>()?.transform
                           ?? root.Find("Game")
                           ?? NewGroup("Game", root);
                host.gameObject.AddComponent<FuelSystem>();
                report.Placed.Add($"fuel — FuelSystem on {host.name}");
            }

            EnsureFuelGauge(report);
        }

        /// <summary>
        /// Adds the gauge to the shared HUD document, idempotently.
        ///
        /// <b>This exists so nobody has to run The Block → Build Map HUD.</b> That item calls
        /// <c>DestroyImmediate</c> on the <c>HUD</c> GameObject, and every U26 menu component lives
        /// on it — running it to pick up one new element would take the title screen with it.
        /// </summary>
        private static void EnsureFuelGauge(Report report)
        {
            var hud = GameObject.Find("HUD");
            if (hud == null)
            {
                report.Warnings.Add("fuel — no HUD object in the scene; the fuel bar has nowhere to go. " +
                                    "Run The Block → Build Map HUD (and then Build Menus, which it deletes).");
                return;
            }

            if (hud.GetComponent<FuelGauge>() != null)
            {
                report.Notes.Add("fuel — FuelGauge already on HUD");
                return;
            }

            hud.AddComponent<FuelGauge>();
            EditorUtility.SetDirty(hud);
            report.Placed.Add("fuel — FuelGauge on HUD");
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
