using System.Collections.Generic;
using System.Linq;
using System.Text;
using TheBlock.Core;
using TheBlock.Npc;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Bakes where the crowd stands - <b>The Block → Bake Crowd Seeds</b>.
    ///
    /// The port of the original's three additive seeding passes (<c>crowd.ts</c>), run once at build
    /// time instead of once per boot:
    ///  1. <b>33 painted rectangles</b> × <c>perDistrict</c> 9 - hand-captured pavement, the bulk of
    ///     the crowd.
    ///  2. <b>38 sidewalk strips</b>, each split into two lanes <c>laneOffset</c> apart walked in
    ///     opposite directions, which is what makes a pavement read as two-way foot traffic.
    ///  3. <b>A per-district fallback</b> of 9 more, for the districts no rectangle covers.
    /// Zebra crossers are NOT baked - they hold a reference to a live <c>Crossing</c> and are built
    /// by <see cref="CrowdSpawner"/> at Start.
    ///
    /// <b>Run this last.</b> It queries the NavMesh to decide what is pavement, so it needs
    /// <c>Build World + NavMesh (slow)</c> to have run first.
    ///
    /// <b>Handedness, once, through <see cref="Convert"/>.</b> Every number in <c>npc.config.ts</c>
    /// is right-handed. Negating X swaps a rectangle's two X bounds - <c>xMin</c> becomes the Unity
    /// <c>xMax</c> - so both corners go through <see cref="Convert.Pos(Vector3)"/> and min/max are
    /// re-derived from the results. Never negate a field in place (port rule 1).
    ///
    /// The lane's sideways offset is <c>Cross(up, tangent)</c> on the CONVERTED endpoints. That is
    /// the same physical side as the web's <c>(-tz, tx)</c>: with web tangent <c>t</c>, converting
    /// its result gives <c>(t.z, 0, t.x)</c>, and <c>Cross(up, Convert(t))</c> gives <c>(t.z, 0,
    /// t.x)</c> too. The trap U17 hit is re-typing the web's FORMULA onto Unity vectors, which lands
    /// on the opposite side; converting its ANSWER is right.
    /// </summary>
    public static class CrowdBuilder
    {
        private const string AssetFolder = "Assets/Npc/Generated";
        private const string AssetPath = AssetFolder + "/CrowdSeeds.asset";

        /// <summary>Lane resampling. Fine enough that a sloped pavement is followed, coarse enough to be free.</summary>
        private const float LaneSampleM = 2f;

        /// <summary>How far a seed candidate may snap onto the mesh before it stops being that point.</summary>
        private const float SnapRadius = 0.75f;

        /// <summary>Rejection-sampling budget per person, from <c>walkable.ts</c>'s own 200.</summary>
        private const int SampleAttempts = 200;

        /// <summary>A candidate this far off the ground probe's answer is a roof, not a pavement.</summary>
        private const float RooftopBand = 1.5f;

        /// <summary>Points used to measure how many seeds are ever within the cull radius at once.</summary>
        private const int DensitySamples = 4000;

        /// <summary>How far sideways a lane sample may be pushed to get out of a building, and in
        /// what increments.
        ///
        /// <b>Eight, and four was measured to be too few.</b> The east-pavement lane runs 5.4 m
        /// inside פלאפל הפעמונים's 7 m footprint, and the near side - east, 2.1 m away - is the
        /// building the stand backs onto, so <c>SamplePosition</c> rightly refuses it. The only way
        /// out is 5.8 m west, onto the strip of pavement between the stand and a kerb that is 6.75 m
        /// away. At 4 m both directions failed and four lanes stayed inside a wall.
        ///
        /// Raising the cap cannot make a good detour worse: the loop tries every push in ascending
        /// order and takes the first that works, so the smallest one still wins.</summary>
        private const float DetourStepM = 0.25f;
        private const float DetourMaxM = 8f;

        /// <summary>Metres of daylight a detoured sample must keep between itself and the wall -
        /// roughly a pedestrian's shoulder, so nobody walks with an arm inside the glazing.</summary>
        private const float DetourClearanceM = 0.4f;

        /// <summary>Samples of run-up and run-out either side of a blocked stretch, so the diagonal
        /// into and out of a detour happens clear of the building rather than across its corner.
        /// Two samples is 4 m at <see cref="LaneSampleM"/>.</summary>
        private const int DetourRunPad = 2;

        /// <summary>Renderer bounds are generous - a canopy overhangs the pavement it does not
        /// block - so a footprint is pulled in this far on each side before it counts as a wall.</summary>
        private const float FootprintInsetM = 0.35f;

        [MenuItem("The Block/Bake Crowd Seeds", priority = 23)]
        public static void BuildMenu() => Build();

        public static string Build()
        {
            var snapshot = TheBlockConfig.Load(reload: true);
            var npc = snapshot?.Npc;
            if (npc == null)
            {
                const string message =
                    "CrowdBuilder: the config snapshot has no npcConfig. Run tools/export-config.sh.";
                Debug.LogError(message);
                return message;
            }

            var triangulation = NavMesh.CalculateTriangulation();
            if (triangulation.vertices == null || triangulation.vertices.Length == 0)
            {
                const string message =
                    "CrowdBuilder: there is no NavMesh in this scene, so there is no pavement to seed " +
                    "onto. Run The Block → Build World + NavMesh (slow) first.";
                Debug.LogError(message);
                return message;
            }

            var log = new StringBuilder();
            var rng = new System.Random(20260816);

            var rects = new List<CrowdSeedTable.Rect>();
            var paths = new List<CrowdSeedTable.LanePath>();
            var seeds = new List<CrowdSeedTable.Seed>();

            // THE PREFAB LIST IS BUILT HERE, at the top, and the bake counts faces from it.
            //
            // It used to count from `npcConfig.people` - six entries, because the web build had six
            // people - and <see cref="CrowdSpawner.TryBind"/> resolves a body as
            // `seed.Face % pedestrianPrefabs.Count`. With six baked face values and a longer list,
            // `0..5 % 12` is still `0..5`: the extra prefabs are wired, look correct in the
            // inspector, and never appear on the street. Counting from the list that will actually be
            // assigned is the only arrangement where the two cannot disagree.
            var prefabs = CrowdPrefabs();
            int faceCount = Mathf.Max(1, prefabs.Count);
            int face = 0;

            // --- 1. painted rectangles ----------------------------------------------------------

            int wanted = 0;
            int placed = 0;

            foreach (var painted in npc.PaintedZones)
            {
                var rect = ToUnity(painted);
                int rectId = rects.Count;
                rects.Add(rect);

                int count = npc.PerDistrict;
                wanted += count;

                for (int i = 0; i < count; i++)
                {
                    if (!TrySampleIn(rect, rng, out var point)) break;

                    seeds.Add(Wander(point, rectId, face++ % faceCount, Speed(npc, rng)));
                    placed++;
                }
            }

            log.AppendLine($"  painted zones  {npc.PaintedZones.Count} rect(s) → {placed}/{wanted} seeded");

            // Manual mode: a non-empty `zones` replaces the district fallback entirely, which is the
            // switch the web build's dev paint tool flips. Empty in the original, kept so it works.
            int manual = 0;
            foreach (var zone in npc.Zones)
            {
                var rect = ToUnity(zone);
                int rectId = rects.Count;
                rects.Add(rect);

                int count = zone.Count ?? npc.DefaultPerZone;
                for (int i = 0; i < count; i++)
                {
                    if (!TrySampleIn(rect, rng, out var point)) break;
                    seeds.Add(Wander(point, rectId, face++ % faceCount, Speed(npc, rng)));
                    manual++;
                }
            }

            if (npc.Zones.Count > 0)
                log.AppendLine($"  manual zones   {npc.Zones.Count} rect(s) → {manual} seeded (district pass skipped)");

            // --- 2. per-district fallback -------------------------------------------------------

            int districts = 0;
            int districtSeeds = 0;

            if (npc.Zones.Count == 0)
            {
                foreach (var bounds in DistrictBounds())
                {
                    districts++;
                    var rect = new CrowdSeedTable.Rect
                    {
                        MinX = bounds.min.x,
                        MaxX = bounds.max.x,
                        MinZ = bounds.min.z,
                        MaxZ = bounds.max.z,
                    };

                    for (int i = 0; i < npc.PerDistrict; i++)
                    {
                        if (!TrySampleIn(rect, rng, out var point)) break;

                        // RectId −1: these are the only truly free-roaming people, exactly as in the
                        // web build's auto branch.
                        seeds.Add(Wander(point, -1, face++ % faceCount, Speed(npc, rng)));
                        districtSeeds++;
                    }
                }

                log.AppendLine($"  districts      {districts} → {districtSeeds} seeded (free-roaming)");
            }

            // --- 3. sidewalk strips -------------------------------------------------------------

            int walkers = 0;
            float laneMetres = 0f;

            // The buildings a lane may not walk through. Read once - they do not move between
            // strips, and each one costs a GetComponentsInChildren.
            var footprints = PlaceFootprints();
            int bent = 0;

            foreach (var strip in npc.Strips)
            {
                var a = Convert.Pos(strip.A.Raw);
                var b = Convert.Pos(strip.B.Raw);

                var tangent = b - a;
                tangent.y = 0f;
                if (tangent.sqrMagnitude < 0.01f) continue;

                var side = Vector3.Cross(Vector3.up, tangent.normalized) * npc.LaneOffset;

                int count = strip.Count ?? npc.PerStrip;
                int half = Mathf.CeilToInt(count / 2f);

                for (int lane = 0; lane < 2; lane++)
                {
                    float sign = lane == 0 ? 1f : -1f;
                    var path = Resample(a + side * sign, b + side * sign, footprints);
                    foreach (var point in path.Points)
                        if (Blocked(point, footprints)) { bent++; break; }
                    int pathId = paths.Count;
                    paths.Add(path);
                    laneMetres += path.Length;

                    int on = lane == 0 ? half : count - half;
                    for (int i = 0; i < on; i++)
                    {
                        // Spread along the lane with a little jitter, so a strip is a stream of
                        // people rather than a queue that sets off together.
                        float s = (i + (float)rng.NextDouble() * 0.5f) / Mathf.Max(1, on) * path.Length;

                        seeds.Add(new CrowdSeedTable.Seed
                        {
                            Position = path.At(s),
                            S = s,
                            Dir = (sbyte)(lane == 0 ? 1 : -1),
                            Face = (byte)(face++ % faceCount),
                            Speed = Speed(npc, rng),
                            Mode = CrowdSeedTable.Mode.Strip,
                            RectId = -1,
                            PathId = pathId,
                        });
                        walkers++;
                    }
                }
            }

            log.AppendLine(
                $"  strips         {npc.Strips.Count} → {paths.Count} lane(s), {laneMetres:0} m, " +
                $"{paths.Sum(p => p.Points.Length)} LUT point(s), {walkers} walker(s)");
            log.AppendLine(
                $"  footprints     {footprints.Count} place(s) routed around, " +
                $"{bent} lane(s) STILL crossing one");

            // A lane that could not be routed out of a building in DetourMaxM is a lane authored
            // straight through the middle of something, and the honest answer is to say so rather
            // than shove somebody into the carriageway. Loud, because the fault it replaces went
            // unnoticed until a player walked through a falafel stand.
            if (bent > 0)
                Debug.LogWarning(
                    $"CrowdBuilder: {bent} lane(s) still pass through a Place after a {DetourMaxM:0.#} m " +
                    "detour - widen DetourMaxM or move the strip in config.ts");

            // --- density ------------------------------------------------------------------------

            var (peak, p95) = MeasureDensity(seeds, npc.CullDistanceM, rng);
            log.AppendLine(
                $"  within {npc.CullDistanceM:0} m    peak {peak}, p95 {p95} " +
                "- set the Crowd object's Live Cap from this");

            // --- write --------------------------------------------------------------------------

            if (!AssetDatabase.IsValidFolder("Assets/Npc")) AssetDatabase.CreateFolder("Assets", "Npc");
            if (!AssetDatabase.IsValidFolder(AssetFolder)) AssetDatabase.CreateFolder("Assets/Npc", "Generated");

            var table = AssetDatabase.LoadAssetAtPath<CrowdSeedTable>(AssetPath);
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<CrowdSeedTable>();
                AssetDatabase.CreateAsset(table, AssetPath);
            }

            table.Fill(rects.ToArray(), paths.ToArray(), seeds.ToArray(), snapshot.NpcSourceSha256, peak, p95);
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();

            string wiring = WireScene(table, peak, prefabs);

            var report =
                $"CrowdBuilder - {seeds.Count} seed(s), {rects.Count} rect(s), {paths.Count} lane(s)\n" +
                log + wiring +
                "\n  crossers are NOT baked: they hold a live Crossing and are built at Start (2 per zebra)";
            Debug.Log(report, table);
            return report;
        }

        // --- helpers ---------------------------------------------------------------------------

        /// <summary>
        /// The bodies the street draws from - U38's twelve strangers from
        /// <see cref="PackPedBuilder"/>.
        ///
        /// <b>The six Mixamo faces are deliberately NOT here any more.</b> Five of them have a job in
        /// the campaign - Elizabeth cashiers the pizzeria, Remy gives the dance, Sophie, Chinese and
        /// Lewis take the pizza deliveries - and a named character who is also every third extra on
        /// the pavement stops reading as a character. They are still built, still rigged and still
        /// placed by the mission builders; they have just stopped being the crowd.
        ///
        /// Falling back to them if the pack is absent is on purpose and not a courtesy: the pack
        /// folder is gitignored (505 MB against a shared 1 GiB LFS ceiling), so a fresh clone has no
        /// pack at all, and an empty street would look like a broken bake rather than a missing
        /// import.
        /// </summary>
        private static List<GameObject> CrowdPrefabs()
        {
            var pack = PackPedBuilder.Names
                .Select(code => AssetDatabase.LoadAssetAtPath<GameObject>(PackPedBuilder.PrefabPath(code)))
                .Where(p => p != null)
                .ToList();

            if (pack.Count > 0) return pack;

            Debug.LogWarning(
                "CrowdBuilder: none of PackPedBuilder's twelve prefabs exist, so the crowd falls back " +
                "to the six Mixamo faces. Re-import Assets/npc_casual_set_00 (gitignored) and run " +
                "The Block → Build Pack Pedestrians.");

            return PeopleImporter.Names
                .Select(n => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Npc/Ped_{n}.prefab"))
                .Where(p => p != null)
                .ToList();
        }

        private static CrowdSeedTable.Seed Wander(Vector3 point, int rectId, int face, float speed) =>
            new()
            {
                Position = point,
                Target = point,
                S = 0f,
                Dir = 1,
                Face = (byte)face,
                Speed = speed,
                Mode = CrowdSeedTable.Mode.Wander,
                RectId = rectId,
                PathId = -1,
            };

        private static float Speed(TheBlockConfig.NpcSpec npc, System.Random rng) =>
            Mathf.Lerp(npc.Speed.Min, npc.Speed.Max, (float)rng.NextDouble());

        /// <summary>
        /// Both corners converted, then min/max re-derived. Worked example, because getting this
        /// wrong mirrors the crowd against the city: web <c>{xMin −177.4, xMax −129.8}</c> becomes
        /// Unity <c>{xMin 129.8, xMax 177.4}</c>.
        /// </summary>
        private static CrowdSeedTable.Rect ToUnity(TheBlockConfig.NpcRectSpec rect)
        {
            var a = Convert.Pos(new Vector3(rect.XMin, 0f, rect.ZMin));
            var b = Convert.Pos(new Vector3(rect.XMax, 0f, rect.ZMax));

            return new CrowdSeedTable.Rect
            {
                MinX = Mathf.Min(a.x, b.x),
                MaxX = Mathf.Max(a.x, b.x),
                MinZ = Mathf.Min(a.z, b.z),
                MaxZ = Mathf.Max(a.z, b.z),
            };
        }

        /// <summary>
        /// A point on pavement inside the rectangle - the port of <c>walkable.ts</c>'s
        /// <c>sampleInBox</c>, with the mask replaced by the NavMesh and one extra guard the web
        /// build did not need: rooftops bake walkable, so a hit far off the ground probe's answer is
        /// a roof and is rejected (memory: <c>navmesh-bakes-rooftops</c>).
        /// </summary>
        private static bool TrySampleIn(CrowdSeedTable.Rect rect, System.Random rng, out Vector3 point)
        {
            for (int attempt = 0; attempt < SampleAttempts; attempt++)
            {
                var candidate = new Vector3(
                    Mathf.Lerp(rect.MinX, rect.MaxX, (float)rng.NextDouble()),
                    0f,
                    Mathf.Lerp(rect.MinZ, rect.MaxZ, (float)rng.NextDouble()));

                if (!NavMesh.SamplePosition(candidate, out var hit, SnapRadius, NavMesh.AllAreas)) continue;

                float ground = WorldBuilder.GroundY(hit.position);
                if (Mathf.Abs(hit.position.y - ground) > RooftopBand) continue;

                point = new Vector3(hit.position.x, ground, hit.position.z);
                return true;
            }

            point = Vector3.zero;
            return false;
        }

        /// <summary>
        /// A lane, resampled so every sample carries its own baked ground height - and bent around
        /// anything standing on the pavement.
        ///
        /// <b>Why a straight line was not enough.</b> The strips in <c>config.ts</c> are pairs of
        /// endpoints and this used to interpolate straight between them, asking only how high the
        /// ground was. Nothing ever asked whether a sample landed INSIDE a building, and on
        /// 2026-08-19 the user found the answer: pedestrians walked clean through פלאפל הפעמונים.
        /// A footprint query then found lane 0 - the 141-point strip that runs the whole east
        /// pavement, z −140.9 to +138.5 - passing through the stand at (19.2, 0.2, −100.3), and
        /// <c>Place_SevenEleven</c> with exactly the same fault on lane 2.
        ///
        /// <b>Carving the NavMesh would not have fixed it.</b> Pedestrians have no
        /// <c>NavMeshAgent</c> - a deliberate U16 reversal - so a lane-follower reads
        /// <c>_path.At(_state.S)</c> and walks wherever the polyline goes, whatever the NavMesh
        /// says. The polyline is the thing that has to move.
        /// </summary>
        private static CrowdSeedTable.LanePath Resample(Vector3 a, Vector3 b, List<Bounds> blockers)
        {
            float straight = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z));
            int samples = Mathf.Max(2, Mathf.CeilToInt(straight / LaneSampleM) + 1);

            var tangent = b - a;
            tangent.y = 0f;
            var side = Vector3.Cross(Vector3.up, tangent.normalized);

            var flat = new Vector3[samples];
            for (int i = 0; i < samples; i++) flat[i] = Vector3.Lerp(a, b, i / (float)(samples - 1));

            DetourRuns(flat, side, blockers);

            var points = new Vector3[samples];
            for (int i = 0; i < samples; i++)
                points[i] = new Vector3(flat[i].x, WorldBuilder.GroundY(flat[i]), flat[i].z);

            // ⚠ RE-RESAMPLE, AND THIS IS NOT TIDINESS. `LanePath.At(s)` maps arc length to a point as
            // `s / Length * segments` - it ASSUMES the samples are evenly spaced and does no search.
            // A detour displaces some of them sideways, so leaving the polyline as-is would make
            // every walker visibly speed up and slow down through the bend.
            return EvenlySpaced(points);
        }

        /// <summary>
        /// Moves each blocked stretch of a lane sideways <b>as a whole run</b>, in place.
        ///
        /// ⚠ <b>MOVING ONLY THE BLOCKED SAMPLES DOES NOT WORK, AND IT LOOKS LIKE IT SHOULD.</b> That
        /// was the first version: push each sample that lands inside a building out to the nearest
        /// clear pavement. Every push individually succeeded - a probe at (21.0, −100.0) had a clean
        /// answer 2.5 m east - and the bake still reported four lanes inside a wall, because a
        /// polyline is its SEGMENTS, not its points. A sample jogged 2.5 m sideways while its
        /// neighbour 2 m along stayed put leaves a diagonal that cuts straight back through the
        /// corner, and <see cref="EvenlySpaced"/> then samples points along it.
        ///
        /// So a blocked stretch is widened by <see cref="DetourRunPad"/> samples at each end and the
        /// whole run is offset by ONE vector: the lane steps out before the building, runs parallel
        /// past it, and steps back after - and both diagonals happen in clear air.
        /// </summary>
        private static void DetourRuns(Vector3[] flat, Vector3 side, List<Bounds> blockers)
        {
            var blocked = new bool[flat.Length];
            var any = false;
            for (int i = 0; i < flat.Length; i++)
                if (blocked[i] = Blocked(flat[i], blockers)) any = true;

            if (!any) return;

            for (int i = 0; i < flat.Length; i++)
            {
                if (!blocked[i]) continue;

                int end = i;
                while (end + 1 < flat.Length && blocked[end + 1]) end++;

                int from = Mathf.Max(0, i - DetourRunPad);
                int to = Mathf.Min(flat.Length - 1, end + DetourRunPad);

                var offset = RunOffset(flat, from, to, side, blockers);
                if (offset != Vector3.zero)
                    for (int k = from; k <= to; k++) flat[k] += offset;

                i = end;
            }
        }

        /// <summary>
        /// The single smallest sideways offset that gets every sample of a run onto clear, walkable
        /// pavement - or <c>Vector3.zero</c> if neither side has one inside
        /// <see cref="DetourMaxM"/>.
        ///
        /// Both directions are priced in full and the cheaper wins, so a lane clips the near side of
        /// a building rather than swinging around the far side of it.
        /// </summary>
        private static Vector3 RunOffset(
            Vector3[] flat, int from, int to, Vector3 side, List<Bounds> blockers)
        {
            float best = float.MaxValue;
            var chosen = Vector3.zero;

            for (int sign = 0; sign < 2; sign++)
            {
                var direction = sign == 0 ? side : -side;

                for (float push = DetourStepM; push <= DetourMaxM; push += DetourStepM)
                {
                    if (push >= best) break;

                    var ok = true;
                    for (int k = from; k <= to && ok; k++)
                    {
                        var candidate = flat[k] + direction * push;

                        // Clear by a shoulder, not merely outside the box - a sample sitting exactly
                        // on the wall puts a 0.6 m capsule half inside it.
                        if (Blocked(candidate, blockers, DetourClearanceM)) { ok = false; break; }

                        // Clear of the building is only half of it. The other side of a narrow
                        // pavement is the carriageway, and a lane of pedestrians strolling up the
                        // middle of the road is a worse bug than the one being fixed.
                        ok = NavMesh.SamplePosition(
                            candidate, out _, SnapRadius, NavMesh.AllAreas);
                    }

                    if (!ok) continue;

                    best = push;
                    chosen = direction * push;
                    break;
                }
            }

            return chosen;
        }

        private static bool Blocked(Vector3 point, List<Bounds> blockers, float margin = 0f)
        {
            for (int i = 0; i < blockers.Count; i++)
            {
                var b = blockers[i];
                if (point.x > b.min.x - margin && point.x < b.max.x + margin &&
                    point.z > b.min.z - margin && point.z < b.max.z + margin)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Rebuilds a polyline at a constant arc length, which is the spacing
        /// <see cref="CrowdSeedTable.LanePath.At"/> assumes.
        /// </summary>
        private static CrowdSeedTable.LanePath EvenlySpaced(Vector3[] raw)
        {
            var cumulative = new float[raw.Length];
            for (int i = 1; i < raw.Length; i++)
            {
                var step = raw[i] - raw[i - 1];
                step.y = 0f;
                cumulative[i] = cumulative[i - 1] + step.magnitude;
            }

            float total = cumulative[raw.Length - 1];
            if (total < 0.01f)
                return new CrowdSeedTable.LanePath { Points = raw, Length = 0.01f };

            int count = Mathf.Max(2, Mathf.CeilToInt(total / LaneSampleM) + 1);
            var points = new Vector3[count];
            int cursor = 0;

            for (int i = 0; i < count; i++)
            {
                float want = total * i / (count - 1);
                while (cursor < raw.Length - 2 && cumulative[cursor + 1] < want) cursor++;

                float span = cumulative[cursor + 1] - cumulative[cursor];
                float t = span < 1e-4f ? 0f : (want - cumulative[cursor]) / span;
                points[i] = Vector3.Lerp(raw[cursor], raw[cursor + 1], t);
            }

            return new CrowdSeedTable.LanePath { Points = points, Length = total };
        }

        /// <summary>
        /// The XZ footprints a lane may not pass through: everything under <c>World/Places</c> that
        /// has geometry and is not a car, an NPC or the off-world interior set.
        ///
        /// Shrunk by <see cref="FootprintInsetM"/> because these are renderer bounds, and a falafel
        /// stand's canopy overhangs the pavement by most of a metre. Walking under a canopy is fine;
        /// walking through the counter is not.
        /// </summary>
        private static List<Bounds> PlaceFootprints()
        {
            var footprints = new List<Bounds>();
            var places = GameObject.Find("World/Places");
            if (places == null) return footprints;

            foreach (Transform child in places.transform)
            {
                if (!child.gameObject.activeInHierarchy) continue;
                if (child.name == "LotCars") continue;                       // parked cars, not buildings
                if (child.name.StartsWith("Falafel_Vendor")) continue;       // a person, and he moves
                if (child.position.x < -500f) continue;                      // the interior set at x −1000

                var renderers = child.GetComponentsInChildren<Renderer>(false);
                if (renderers.Length == 0) continue;

                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);

                footprints.Add(new Bounds(
                    new Vector3(bounds.center.x, 0f, bounds.center.z),
                    new Vector3(
                        Mathf.Max(0.1f, bounds.size.x - FootprintInsetM * 2f),
                        1f,
                        Mathf.Max(0.1f, bounds.size.z - FootprintInsetM * 2f))));
            }

            return footprints;
        }

        /// <summary>Every district's world bounds - the port of <c>worldMap.districts()</c>.</summary>
        private static IEnumerable<Bounds> DistrictBounds()
        {
            var root = GameObject.Find("World/Districts");
            if (root == null) yield break;

            foreach (Transform district in root.transform)
            {
                var renderers = district.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) continue;

                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                yield return bounds;
            }
        }

        /// <summary>
        /// How many people are ever inside the cull radius at once. This is what <c>liveCap</c> has
        /// to cover, and knowing it BEFORE the first Play is the difference between tuning the pool
        /// by measurement and tuning it by feel.
        /// </summary>
        private static (int Peak, int P95) MeasureDensity(
            List<CrowdSeedTable.Seed> seeds, float cull, System.Random rng)
        {
            if (seeds.Count == 0) return (0, 0);

            float minX = seeds.Min(s => s.Position.x);
            float maxX = seeds.Max(s => s.Position.x);
            float minZ = seeds.Min(s => s.Position.z);
            float maxZ = seeds.Max(s => s.Position.z);
            float cullSqr = cull * cull;

            var counts = new List<int>(DensitySamples);
            for (int i = 0; i < DensitySamples; i++)
            {
                var at = new Vector3(
                    Mathf.Lerp(minX, maxX, (float)rng.NextDouble()), 0f,
                    Mathf.Lerp(minZ, maxZ, (float)rng.NextDouble()));

                int near = 0;
                foreach (var seed in seeds)
                {
                    float dx = seed.Position.x - at.x;
                    float dz = seed.Position.z - at.z;
                    if (dx * dx + dz * dz <= cullSqr) near++;
                }

                counts.Add(near);
            }

            counts.Sort();
            return (counts[^1], counts[Mathf.Clamp((int)(counts.Count * 0.95f), 0, counts.Count - 1)]);
        }

        /// <summary>
        /// Points the scene's Crowd object at what was just built.
        ///
        /// Ends on <c>MarkSceneDirty</c>: a script that changes a scene object without it leaves the
        /// scene looking clean, so Save writes nothing and reports success (memory:
        /// <c>editor-created-objects-need-markscenedirty</c>).
        /// </summary>
        private static string WireScene(CrowdSeedTable table, int peak, List<GameObject> prefabs)
        {
            var crowd = GameObject.Find("Crowd");
            if (crowd == null || !crowd.TryGetComponent<CrowdSpawner>(out var spawner))
                return "\n  ⚠ no Crowd object with a CrowdSpawner in this scene - wire it by hand";

            var serialized = new SerializedObject(spawner);
            serialized.FindProperty("seedTable").objectReferenceValue = table;

            var list = serialized.FindProperty("pedestrianPrefabs");
            list.arraySize = prefabs.Count;
            for (int i = 0; i < prefabs.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = prefabs[i];

            // Cover the measured peak with a little headroom, then let the user tune it down.
            serialized.FindProperty("liveCap").intValue = Mathf.Clamp(peak + 16, 32, 400);

            // Everything except the pedestrians themselves is ground.
            serialized.FindProperty("groundMask").intValue = ~(1 << LayerMask.NameToLayer(NpcBuilder.PedestrianLayer));

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(spawner);
            EditorSceneManager.MarkSceneDirty(crowd.scene);

            return $"\n  wired Crowd: {prefabs.Count} prefab(s), liveCap {serialized.FindProperty("liveCap").intValue}";
        }
    }
}
