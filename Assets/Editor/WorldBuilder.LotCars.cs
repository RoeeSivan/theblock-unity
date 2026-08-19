using System.Collections.Generic;
using System.Linq;
using TheBlock.Core;
using TheBlock.World;
using UnityEditor;
using UnityEngine;
using Convert = TheBlock.Core.Convert;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// The parked-car filler for the big lot (U13), ported from <c>src/world/lot-cars.ts</c>.
    ///
    /// <b>Can Unity do this better?</b> The web build draws these as one
    /// <c>THREE.InstancedMesh</c> per source mesh per model, because a few hundred cloned cars would
    /// be thousands of draw calls in three's forward renderer. That is the right answer there and the
    /// wrong one here: an InstancedMesh is a SINGLE renderable with one bounding sphere over the
    /// whole lot, so every instance is drawn whenever any part of the lot is on screen, and none of
    /// them can be culled, lit or shadowed individually.
    ///
    /// Unity gets real GameObjects instead. Identical mesh + material pairs are GPU-instanced
    /// automatically, so the draw-call win survives - but each car is culled on its own bounds, and
    /// each carries an <c>LODGroup</c> that culls it past <see cref="CullDistance"/>. Standing in the
    /// lot, the visible set is what the camera can see rather than every car in the world.
    ///
    /// The layout itself is ported exactly, PRNG included: same seed, same stall grid, same rolls, so
    /// the lot has the same cars in the same places as the shipped web build.
    /// </summary>
    public static partial class WorldBuilder
    {
        /// <summary>Generated paint variants, one material per model per colour. Swept every build.</summary>
        private const string LotCarMaterialFolder = "Assets/Materials/LotCars";

        /// <summary>
        /// Names the source models give their body-paint material. Same convention the web build
        /// matches on in <c>lot-cars.ts</c>, <c>vehicle.ts</c> and <c>traffic-cars.ts</c>; a model
        /// that names neither simply keeps its shipped colour, which is visible in the report.
        /// </summary>
        private static readonly string[] PaintMaterialNames = { "CarPrimaryColor", "primary" };

        /// <summary>
        /// Metres past which a filler car is not drawn at all.
        ///
        /// Nothing in <c>config.ts</c> to port: the web build has no per-car culling to configure.
        ///
        /// <b>Was 180 m, and 180 never actually happened</b> - see <see cref="AddCullGroup"/> for the
        /// 6.9× conversion error that made it ~1,250 m. Raised to 350 rather than restored to 180,
        /// because 180 was chosen when it was decorative: cutting straight to it would make cars
        /// wink out 150 m BEFORE <c>m_LinearFogStart</c> (328 m), which is the thing that is supposed
        /// to hide them. At 350 the cull lands where the fog has already taken them, and it is still
        /// 3.6× tighter than what was shipping.
        /// </summary>
        private const float CullDistance = 350f;

        /// <summary>
        /// Metres past which a filler car drops to its 5×-lighter twin.
        ///
        /// Far enough out that the swap is not something a player standing in the lot can be looking
        /// at - the gate on this number is a pixel diff either side of it, not an opinion.
        /// </summary>
        private const float Lod1Distance = 45f;

        /// <summary>Suffix on the decimated twin beside each lot model, and the child it becomes.</summary>
        private const string Lod1Suffix = "_lod1";
        private const string Lod1ChildName = "LOD1";

        /// <summary>
        /// Rebuilds the 101 parked cars and nothing else.
        ///
        /// <b>Why this exists rather than "just run Build World".</b> The lot is the heaviest thing
        /// in the scene and its LOD wiring is the one part likely to be tuned twice - but a full
        /// world build regenerates every district's geometry, which silently invalidates the baked
        /// occlusion data (`occlusion-bake-is-a-build-artifact`) and costs minutes. This touches only
        /// <c>Places/LotCars</c>, which no occluder depends on.
        /// </summary>
        [MenuItem("The Block/Build Lot Cars", priority = 9)]
        public static void BuildLotCarsOnly()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError(
                    "Build Lot Cars: stop Play mode first - a build in Play is discarded on exit.");
                return;
            }

            var root = Object.FindAnyObjectByType<TheBlock.Core.WorldRoot>();
            var places = root != null ? root.transform.Find("Places") : null;
            if (places == null)
            {
                Debug.LogError("Build Lot Cars: no Places group. Run The Block → Build World first.");
                return;
            }

            var cfg = TheBlockConfig.Load(reload: true)?.Config?.LotCars;
            if (cfg == null)
            {
                Debug.LogError("Build Lot Cars: config has no lotCars section.");
                return;
            }

            // ⚠ `NewGroup` CREATES, IT DOES NOT REPLACE. That is correct inside a full world build,
            // where Places is fresh - and it means a standalone rebuild would leave the old lot
            // sitting under a second `LotCars` group, which is precisely the fault that put a spare
            // falafel stand in the middle of downtown for three commits. Sweep first.
            var stale = places.Find("LotCars");
            if (stale != null) Object.DestroyImmediate(stale.gameObject);

            var report = new Report();
            ResetTexturePass();
            BuildLotCars(places, cfg, new Options(), report);

            // The paint variants are regenerated by name, so a rebuild overwrites rather than
            // accumulates - but a colour the PRNG no longer rolls would linger. The full build's
            // folder sweep covers it; this one leaves them, which costs a few kilobytes and never
            // shows up in the world.

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            var text = new System.Text.StringBuilder(
                $"Build Lot Cars - {report.Warnings.Count} warning(s)\n");
            foreach (var line in report.Placed) text.AppendLine("  " + line);
            foreach (var line in report.Notes) text.AppendLine("  " + line);
            foreach (var line in report.Warnings) text.AppendLine("  WARNING " + line);

            if (report.Warnings.Count > 0) Debug.LogWarning(text.ToString().TrimEnd());
            else Debug.Log(text.ToString().TrimEnd());
        }

        private static void BuildLotCars(
            Transform parent, TheBlockConfig.LotCarsSpec cfg, Options options, Report report)
        {
            if (cfg?.Models == null || cfg.Models.Count == 0 || cfg.LotBounds == null) return;

            var group = NewGroup("LotCars", parent);
            var stalls = LayOutStalls(cfg);
            if (stalls.Count == 0)
            {
                report.Warnings.Add("lot cars - the stall grid came out empty; check bounds/rowDepth");
                return;
            }

            var placed = 0;
            var skipped = new List<string>();

            for (int m = 0; m < cfg.Models.Count; m++)
            {
                var spec = cfg.Models[m];
                var mine = stalls.Where(s => s.Model == m).ToList();
                if (mine.Count == 0) continue;

                var prefab = LoadLotCarModel(spec, report);
                if (prefab == null)
                {
                    skipped.Add($"{spec.Name} ({mine.Count} stalls)");
                    continue;
                }

                var scale = spec.Scale <= 0f ? 1f : spec.Scale;

                // U17b: the drivable twin's own facing correction, so a promoted car can be turned to
                // match the filler it replaces. Resolved once per model, here, because this is the
                // only place both `lotCars.models[].modelYaw` and `vehicle.cars[].modelYaw` are in
                // view at the same time - see LotCar.DriveRotation.
                var drivable = TheBlockConfig.Load()?.Config?.Vehicle?.Cars?
                    .FirstOrDefault(c => string.Equals(
                        c.Name, spec.Name, System.StringComparison.OrdinalIgnoreCase));
                if (drivable == null)
                    report.Warnings.Add(
                        $"lot cars - no config.vehicle.cars entry named '{spec.Name}', so these " +
                        "fillers cannot be promoted on E");

                // Measured once off the prefab - every instance of a model is the same box, and the
                // config states no size of its own. The probe carries the model's scale, so `body` is
                // in metres of world space, which is what both the placement and the cull threshold
                // want; the collider divides it back out.
                // The decimated twin, resolved once per model rather than once per stall.
                var lod1 = LoadLotCarLod1(spec, report);

                var probe = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
                probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                probe.transform.localScale = Vector3.one * scale;
                var body = MeasureBody(probe);
                Object.DestroyImmediate(probe);

                var paints = new Dictionary<int, Material>();
                foreach (var stall in mine)
                {
                    var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);

                    car.name = $"LotCar_{spec.Name}_{placed:000}";
                    car.transform.localScale = Vector3.one * scale;
                    // The stall's heading and the model's own facing correction are both three.js
                    // yaws about +Y, so they add before the single conversion. ModelFacing then turns
                    // the nose down Unity's +Z, exactly as CarBuilder does for the Mustang.
                    car.transform.rotation =
                        Convert.RotFromRadians(stall.Yaw + spec.ModelYaw) * Convert.ModelFacing;
                    // Config's `y` is the lot surface and the wheels rest on it, so the model's own
                    // bottom - not its centre - is what gets placed. The web build recentres the body
                    // to the origin and adds half the height, which assumes a centred pivot; this
                    // does not need to assume anything.
                    car.transform.position =
                        Convert.Pos(stall.X, cfg.Y - body.min.y, stall.Z);

                    PaintCar(car, spec, stall.Paint, paints, report);
                    if (options.Colliders) AddLotCarCollider(car, body, scale, report);
                    // After PaintCar, deliberately: the twin takes LOD0's materials, and LOD0's
                    // materials are only this car's colour once PaintCar has swapped them in.
                    AddCullGroup(car, body, lod1);
                    SetDistrictStaticFlags(car);

                    // U17b: what E needs to promote this filler into the drivable car of the same
                    // model and colour. The paint is looked up rather than rebuilt - PaintCar has
                    // just put the material for this hex in the cache, and handing the promoted car
                    // the SAME asset is what makes the swap invisible. The rotation is the stall's
                    // heading carried into the drivable car's own facing convention.
                    paints.TryGetValue(stall.Paint, out var painted);
                    car.AddComponent<LotCar>().Configure(
                        spec.Name, painted,
                        Convert.RotFromRadians(stall.Yaw + spec.ModelYaw - (drivable?.ModelYaw ?? spec.ModelYaw)));

                    placed++;
                }

                foreach (var material in paints.Values) EditorUtility.SetDirty(material);
            }

            // Once over the whole group rather than per car: the 101 instances share three models'
            // worth of materials, and the pass dedupes by material, so this is three clones and not
            // a hundred and one.
            if (options.CompressedTextures) ApplyCompressedTextures(group.gameObject, report);

            if (skipped.Count > 0)
                report.Missing.Add($"lot cars - no model asset for {string.Join(", ", skipped)}");

            report.Placed.Add($"LotCars - {placed} parked cars in {stalls.Count} filled stalls");
        }

        /// <summary>One filled parking stall, still in the web build's right-handed frame.</summary>
        private struct LotStall
        {
            public float X;
            public float Z;

            /// <summary>Parking heading in radians, EXCLUDING the model's own facing correction.</summary>
            public float Yaw;

            public int Model;
            public int Paint;
        }

        /// <summary>
        /// Generates the stall grid and rolls occupancy, in the web build's own coordinates.
        ///
        /// Kept in three.js space on purpose: the layout reads <c>bounds</c> and <c>keepClear</c> as
        /// min/max rectangles, and converting those first would swap their X ends and quietly invert
        /// every comparison. One conversion per car happens at placement instead.
        ///
        /// Rows run along X. Two rows sit back to back (rears touching), then a driving aisle,
        /// repeated to the far edge - row A noses -Z, row B noses +Z.
        /// </summary>
        private static List<LotStall> LayOutStalls(TheBlockConfig.LotCarsSpec cfg)
        {
            var rand = Mulberry32(cfg.Seed);
            var totalWeight = cfg.Palette?.Sum(p => p.Weight) ?? 0f;

            int PickPaint(int modelIndex)
            {
                // A per-model palette is a uniform pick, and it consumes exactly ONE rand() like the
                // weighted path does - otherwise the shared stream desynchronises and the whole lot
                // reshuffles behind it.
                var own = cfg.Models[modelIndex].Palette;
                if (own != null && own.Count > 0) return own[Mathf.FloorToInt(rand() * own.Count)];

                var roll = rand() * totalWeight;
                foreach (var paint in cfg.Palette)
                {
                    roll -= paint.Weight;
                    if (roll <= 0f) return paint.Hex;
                }

                return cfg.Palette != null && cfg.Palette.Count > 0 ? cfg.Palette[0].Hex : 0xFFFFFF;
            }

            bool InKeepClear(float x, float z) =>
                cfg.KeepClear != null &&
                cfg.KeepClear.Any(k => x >= k.MinX && x <= k.MaxX && z >= k.MinZ && z <= k.MaxZ);

            var stalls = new List<LotStall>();
            var bounds = cfg.LotBounds;
            var zA = bounds.MinZ + cfg.RowDepth / 2f;

            while (zA + cfg.RowDepth + cfg.RowDepth / 2f <= bounds.MaxZ)
            {
                var zB = zA + cfg.RowDepth;
                foreach (var (z, yaw) in new[] { (zA, 0f), (zB, Mathf.PI) })
                {
                    for (var x = bounds.MinX + cfg.StallPitchX / 2f; x <= bounds.MaxX; x += cfg.StallPitchX)
                    {
                        // Every roll happens BEFORE the keep-clear test, so clearing a zone empties
                        // those stalls without shifting what lands in all the others.
                        var roll = rand();
                        var model = Mathf.FloorToInt(rand() * cfg.Models.Count);
                        var paint = PickPaint(model);
                        var jitterX = (rand() - 0.5f) * 0.3f;
                        var jitterZ = (rand() - 0.5f) * 0.5f;
                        var jitterYaw = (rand() - 0.5f) * 0.06f; // slightly sloppy human parking
                        if (roll > cfg.Occupancy || InKeepClear(x, z)) continue;

                        stalls.Add(new LotStall
                        {
                            X = x + jitterX,
                            Z = z + jitterZ,
                            Yaw = yaw + jitterYaw,
                            Model = model,
                            Paint = paint,
                        });
                    }
                }

                zA = zB + cfg.RowDepth + cfg.AisleGap;
            }

            return stalls;
        }

        /// <summary>
        /// The web build's <c>mulberry32</c>, bit for bit, so the seed reproduces the shipped lot.
        ///
        /// JavaScript's <c>Math.imul</c> is a wrapping 32-bit multiply and <c>&gt;&gt;&gt;</c> is an
        /// unsigned shift - which is what <c>uint</c> arithmetic is here, so no reinterpretation is
        /// needed as long as nothing becomes <c>int</c> on the way.
        /// </summary>
        private static System.Func<float> Mulberry32(int seed)
        {
            var state = unchecked((uint)seed);
            return () =>
            {
                unchecked
                {
                    state += 0x6D2B79F5u;
                    var t = (state ^ (state >> 15)) * (1u | state);
                    t = ((t + (t ^ (t >> 7)) * (61u | t)) ^ t);
                    return (t ^ (t >> 14)) / 4294967296f;
                }
            };
        }

        /// <summary>
        /// The lighter twin beside a lot model - <c>tesla.glb</c> → <c>tesla_lod1.glb</c>.
        ///
        /// Absent is not an error, and as of U39 absent is the NORMAL case: no twin exists for any of
        /// the three lot models, so they keep a single-level group and are drawn in full out to
        /// <see cref="CullDistance"/>. The wiring stays because it is correct and costs nothing; what
        /// is missing is a mesh good enough to switch to.
        ///
        /// ⚠ <b>DO NOT MAKE ONE WITH DECIMATE. BOTH MODES WERE MEASURED AND BOTH FAIL.</b>
        ///
        /// <list type="bullet">
        /// <item><b>COLLAPSE</b> destroys these bodies. Rendered side-on, 80% of the triangles is
        /// clean, 60% opens the panel gaps, 45% is visibly torn and the 20% this unit first shipped
        /// had the shell ripped open with the interior showing through - it reached the Player and
        /// only the pixel gate caught it. 80% is the most it survives, and 1.25× is not an LOD.</item>
        /// <item><b>PLANAR</b> does nothing here: 52,096 → 48,123 triangles even at a 20° angle,
        /// measured in U30b round 4.</item>
        /// <item>Clearing the glTF's custom split normals first was tried, on the theory that the
        /// damage was shading rather than shape. It is not - the torn geometry renders identically
        /// with and without them. <c>tools/decimate-glb.py</c> keeps the fix anyway; it is correct on
        /// its own terms and cost nothing.</item>
        /// <item>Dropping the interior for the far level does not work either: these .glbs group
        /// geometry BY MATERIAL, so there is no interior to drop - <c>bodyshell</c> is 46,224 of the
        /// 52,096 triangles under names like <c>bonnet.005</c> and <c>chassis.008</c>.</item>
        /// </list>
        ///
        /// A real LOD1 for these needs an authored low-poly body, not a decimated one.
        /// </summary>
        private static GameObject LoadLotCarLod1(TheBlockConfig.LotCarModelSpec spec, Report report)
        {
            if (string.IsNullOrEmpty(spec.Url)) return null;

            var stem = System.IO.Path.GetFileNameWithoutExtension(
                spec.Url.Substring(spec.Url.LastIndexOf('/') + 1));
            var twin = LoadLotCarModel(
                new TheBlockConfig.LotCarModelSpec { Url = $"/{stem}{Lod1Suffix}.glb" }, report);

            // Deliberately silent when absent. It used to warn, telling the reader to run
            // decimate-glb.sh COLLAPSE - advice that produced a torn car and had to be thrown away.
            // A warning that fires 101 times per build and recommends a known-bad fix is worse than
            // no warning; the docstring above carries what was learned instead.
            return twin;
        }

        private static GameObject LoadLotCarModel(TheBlockConfig.LotCarModelSpec spec, Report report)
        {
            if (string.IsNullOrEmpty(spec.Url)) return null;

            var fileName = spec.Url.Substring(spec.Url.LastIndexOf('/') + 1);
            var bareName = System.IO.Path.GetFileNameWithoutExtension(fileName);
            var path = AssetDatabase
                .FindAssets($"{bareName} t:GameObject", new[] { "Assets/Models" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => string.Equals(System.IO.Path.GetFileName(p), fileName,
                    System.StringComparison.OrdinalIgnoreCase));

            if (path == null) return null;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                report.Warnings.Add(
                    $"lot cars - {path} did not load as a GameObject (a required glTF extension glTFast " +
                    "cannot read imports as a DefaultAsset; see tools/glb-webp-to-png.py)");
            return prefab;
        }

        /// <summary>
        /// Gives one car its paint, by rebinding the body-paint slot to a generated material.
        ///
        /// The web build clones that material white and drives the colour per instance, because
        /// three's InstancedMesh has nowhere else to put it. Here the colour is a material asset,
        /// which is the same call U1 made for the facade tint and U11 for the leaf cutouts - and it
        /// is also what KEEPS the instancing: a per-renderer MaterialPropertyBlock would give every
        /// car its own draw call, while nine shared materials give nine batches for the whole lot.
        /// </summary>
        private static void PaintCar(
            GameObject car, TheBlockConfig.LotCarModelSpec spec, int hex,
            Dictionary<int, Material> paints, Report report)
        {
            foreach (var renderer in car.GetComponentsInChildren<MeshRenderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (source == null || !PaintMaterialNames.Contains(source.name)) continue;

                    if (!paints.TryGetValue(hex, out var painted))
                    {
                        painted = BuildPaintMaterial(spec, source, hex, report);
                        paints[hex] = painted;
                    }

                    materials[i] = painted;
                    changed = true;
                }

                if (changed) renderer.sharedMaterials = materials;
            }
        }

        private static Material BuildPaintMaterial(
            TheBlockConfig.LotCarModelSpec spec, Material source, int hex, Report report)
        {
            EnsureFolder(LotCarMaterialFolder);
            var assetPath = $"{LotCarMaterialFolder}/{Sanitize(spec.Name)}_{hex:X6}.mat";
            report.Generated.Add(assetPath);

            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                // A copy of the imported material, so the paint keeps whatever map, roughness and
                // metallic the model shipped and only the base colour changes.
                material = new Material(source);
                AssetDatabase.CreateAsset(material, assetPath);
            }

            material.shader = source.shader;
            // glTFast's `baseColorFactor` is a gamma-tagged property: the sRGB value goes in as
            // written, and calling .linear on it here renders the car near-black (memory:
            // gltfast-basecolorfactor-gamma).
            if (material.HasProperty("baseColorFactor"))
                material.SetColor("baseColorFactor", TheBlockConfig.ColorFromHex(hex));
            else if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", TheBlockConfig.ColorFromHex(hex).linear);
            else
                report.Warnings.Add($"lot cars - {spec.Name}'s paint material has no base colour to set");

            material.enableInstancing = true;
            return material;
        }

        /// <summary>
        /// One box per car, sized off the model - not a MeshCollider.
        ///
        /// The web build uses a Rapier cuboid for the same reason: these never move, never get
        /// driven, and are hit by cars and by a capsule. A convex hull of 40k triangles would buy
        /// nothing a box does not already give and would cost every one of the hundred cars.
        /// </summary>
        private static void AddLotCarCollider(GameObject car, Bounds body, float scale, Report report)
        {
            var box = car.AddComponent<BoxCollider>();
            // `body` is in metres; a BoxCollider is stated in the object's own unscaled space and the
            // transform scales it back up. The avenger is scale 37.4, so skipping this makes its
            // collider a kilometre wide and nothing can enter the lot at all.
            box.center = body.center / scale;
            box.size = body.size / scale;
            report.Colliders++;
        }

        /// <summary>
        /// Gives the car two real levels - full mesh up close, a 5×-lighter twin in the middle
        /// distance, nothing past <see cref="CullDistance"/>.
        ///
        /// <b>⚠ THE OLD ARITHMETIC WAS WRONG BY 6.9×, AND IT LOOKED RIGHT.</b> This used to read
        /// <c>body.size.y / (2 * CullDistance * tan(fov/2))</c> - the car's HEIGHT, about 1.45 m.
        /// Unity does not test against the height. It tests against <c>LODGroup.size</c>, the
        /// bounding-sphere diameter <see cref="LODGroup.RecalculateBounds"/> computes, which for a
        /// parked Tesla is <b>5.03 m</b> - so every distance came out 3.47× too far. Then
        /// <c>QualitySettings.lodBias</c> is <b>2</b> in this project, which doubled it again. The
        /// docstring's honest intent, "not drawn from more than 180 m away", was actually happening
        /// at roughly <b>1,250 m</b>, and the scene's linear fog does not even begin until 328 m.
        /// U30b round 4 measured what that cost: at a city pose the parked cars and the lamp posts
        /// were 7.77 M submitted triangles and 2,168 shadow casters.
        ///
        /// <b>⚠ THE REPLACEMENT WAS ALSO WRONG, BY 4×, AND ITS ASSERT ALSO PASSED.</b> Two further
        /// faults, both invisible to any check written from the same arithmetic:
        ///
        /// <list type="bullet">
        /// <item><c>lodBias</c> was DIVIDED where Unity multiplies - see <see cref="ScreenHeightAt"/> -
        /// so every distance came out <c>lodBias²</c> = 4× too far.</item>
        /// <item>The lens came from <c>Camera.main</c>, which is 60° in the open scene; the game runs
        /// 75° from <c>TheBlockConfig.Camera.Fov</c> - see <see cref="GameplayFov"/>.</item>
        /// </list>
        ///
        /// Together they put <see cref="Lod1Distance"/>'s 45 m at <b>136 m</b>, which a Player sweep
        /// measured directly (<c>PerfProbe.MeasureSwitchDistance</c>) after the pixel diff's control
        /// arm refused to show any difference between LOD1 and "let the group choose".
        /// </summary>
        private static void AddCullGroup(GameObject car, Bounds body, GameObject lod1Source)
        {
            var group = car.AddComponent<LODGroup>();

            var near = car.GetComponentsInChildren<Renderer>(true);
            Renderer[] far = null;

            if (lod1Source != null)
            {
                var lod1 = (GameObject)PrefabUtility.InstantiatePrefab(lod1Source, car.transform);
                lod1.name = Lod1ChildName;
                lod1.transform.localPosition = Vector3.zero;
                lod1.transform.localRotation = Quaternion.identity;
                lod1.transform.localScale = Vector3.one;

                far = lod1.GetComponentsInChildren<Renderer>(true);
                // LOD0's renderers were collected BEFORE the twin existed, so `near` is still only
                // the full-detail meshes and the two sets do not overlap.
                CopyLodMaterials(near, far);
            }

            // ⚠ MEASURE THE CAR, DO NOT ASK THE GROUP. `LODGroup.size` is stated in the object's own
            // LOCAL space and Unity multiplies it by the transform's scale at test time. These
            // models are imported at wildly different scales - the avenger is 37.4 - so its
            // `group.size` reads 0.15 for a 5.64 m car. Building a threshold from that number sent
            // the avenger's LOD0 to 1,685 m and the audi's to 64.6 m while the Tesla, which happens
            // to import at scale 1, looked perfectly correct. World-space renderer bounds are the
            // same quantity Unity ends up comparing against, and they do not depend on the scale.
            float fov = GameplayFov();
            float worldSize = WorldSize(near);

            group.SetLODs(Levels(
                ScreenHeightAt(worldSize, Lod1Distance, fov),
                ScreenHeightAt(worldSize, CullDistance, fov),
                near, far));

            // After the levels, because it measures the renderers they reference.
            group.RecalculateBounds();
            AssertLodDistances(car, group, fov);
        }

        /// <summary>
        /// The longest world-space edge of a set of renderers - the quantity Unity's LOD test ends
        /// up comparing a car against, and the one <see cref="LODGroup.size"/> is NOT.
        /// </summary>
        private static float WorldSize(Renderer[] renderers)
        {
            var bounds = new Bounds();
            var first = true;

            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }

            return first ? 0f : Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        }

        /// <summary>One or two levels, depending on whether a decimated twin was found.</summary>
        private static LOD[] Levels(float nearHeight, float farHeight, Renderer[] near, Renderer[] far) =>
            far == null
                ? new[] { new LOD(farHeight, near) }
                : new[] { new LOD(nearHeight, near), new LOD(farHeight, far) };

        /// <summary>
        /// The lens the GAME runs, which is not the one the Editor is holding.
        ///
        /// ⚠ <b><c>Camera.main.fieldOfView</c> IS THE WRONG SOURCE AND READS 60 HERE.</b> The scene's
        /// Main Camera carries whatever was last saved on it; at runtime
        /// <see cref="TheBlock.Player.FollowCamera"/> overwrites it from
        /// <c>TheBlockConfig.Camera.Fov</c>, which is <b>75</b>. Solving the thresholds for 60° and
        /// then rendering at 75° moves every switch by <c>tan(30°)/tan(37.5°)</c> - a quarter of the
        /// distance, silently. Read the same number the game reads.
        /// </summary>
        private static float GameplayFov()
        {
            var snapshot = TheBlockConfig.Load();
            return snapshot != null ? snapshot.Config.Camera.Fov : 75f;
        }

        /// <summary>
        /// The screen-height threshold that puts a switch at <paramref name="distance"/> metres for a
        /// body <paramref name="size"/> metres across.
        ///
        /// <b>⚠ <c>lodBias</c> MULTIPLIES. THIS DIVIDED BY IT, AND THE ERROR IS <c>lodBias²</c>.</b>
        /// Unity scales the measured screen height by <c>QualitySettings.lodBias</c> before comparing
        /// it to the threshold - that is what "LOD Bias 2 means LODs switch at twice the distance"
        /// means. Dividing here instead of multiplying therefore misses by a factor of 4 with the
        /// bias of 2 this project ships, and it misses in the direction that keeps the expensive mesh
        /// alive: <see cref="Lod1Distance"/>'s 45 m was landing at <b>136 m</b>.
        ///
        /// That was not caught by argument. A Player rendered one car twice from one position - once
        /// pinned to LOD0, once left to choose - across 10..400 m, and the frames first disagreed at
        /// 140 m (<c>PerfProbe.MeasureSwitchDistance</c>). <b>That sweep, not this line, is the
        /// authority on where a switch happens</b>, because an assert written from this formula can
        /// only ever agree with it.
        /// </summary>
        private static float ScreenHeightAt(float size, float distance, float fov) =>
            size * QualitySettings.lodBias / (2f * distance * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));

        /// <summary>
        /// Reads the thresholds back off the finished group and complains if they do not mean the
        /// metres this file asked for.
        ///
        /// ⚠ <b>AN ASSERT THAT INVERTS ITS OWN INPUTS CANNOT FAIL, AND THE FIRST ONE HERE DID NOT.</b>
        /// It recomputed the distance from the same <c>size</c> variable the threshold had been
        /// built from, so it agreed with itself perfectly while every car switched LOD at 228 m
        /// instead of 45. It now re-derives the size from the renderers Unity will actually measure,
        /// so a wrong <c>group.size</c> - the exact bug it missed - shows up as a mismatch.
        ///
        /// ⚠ <b>AND IT STILL CANNOT CHECK THE FORMULA ITSELF.</b> It shares
        /// <see cref="ScreenHeightAt"/>'s reading of <c>lodBias</c> and of the lens, so when that
        /// reading was inverted this reported a contented 45.0 m on all three models while the Player
        /// switched at 136. What this catches is a wrong SIZE - a scaled import, an empty renderer
        /// set. What only <c>PerfProbe.MeasureSwitchDistance</c> catches is a wrong RELATION. Run it
        /// after changing anything here.
        /// </summary>
        private static void AssertLodDistances(GameObject car, LODGroup group, float fov)
        {
            var lods = group.GetLODs();

            float size = WorldSize(lods[0].renderers);
            if (size <= 0f) return;

            var want = lods.Length == 1
                ? new[] { CullDistance }
                : new[] { Lod1Distance, CullDistance };

            for (int i = 0; i < lods.Length; i++)
            {
                float got = size * QualitySettings.lodBias /
                            (2f * lods[i].screenRelativeTransitionHeight *
                             Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
                // Proportional: 2 m matters at 45 and is rounding at 350.
                if (Mathf.Abs(got - want[i]) <= Mathf.Max(2f, want[i] * 0.05f)) continue;

                Debug.LogError(
                    $"lot cars - {car.name} LOD{i} switches at {got:0.#} m, not the {want[i]:0.#} m " +
                    $"this file asks for (group.size {group.size:0.##}, measured {size:0.##}). " +
                    "The screen-height conversion and Unity's own test have drifted apart again.",
                    car);
                return;
            }
        }

        /// <summary>
        /// Points the LOD1 twin at LOD0's materials, matching renderer by name.
        ///
        /// Two reasons, and the second is the one that would have bitten. <b>Memory:</b> the LOD1
        /// .glb carries its own copy of every texture, so leaving its materials in place would load
        /// the lot's whole texture set twice for meshes seen from 45 m. <b>Colour:</b>
        /// <see cref="PaintCar"/> recolours by matching a material NAME, and it runs once - a twin
        /// on its own materials would be the model's shipped colour while the car it belongs to is
        /// whatever the lot's PRNG rolled, so a car would change colour as you walked towards it.
        /// </summary>
        private static void CopyLodMaterials(Renderer[] near, Renderer[] far)
        {
            var byName = new Dictionary<string, Renderer>();
            foreach (var renderer in near) byName[renderer.name] = renderer;

            foreach (var renderer in far)
                if (byName.TryGetValue(renderer.name, out var source))
                    renderer.sharedMaterials = source.sharedMaterials;
        }

        /// <summary>
        /// The car's body box in metres, measured off a probe standing at the origin unrotated.
        ///
        /// Renderer bounds are world-space and axis-aligned, which is only the body's real box while
        /// the probe is unrotated - hence the caller placing it at identity before asking. Measuring
        /// a rotated car would give the bounding box of a bounding box and grow with the yaw.
        /// </summary>
        private static Bounds MeasureBody(GameObject probe)
        {
            var bounds = new Bounds();
            var first = true;

            foreach (var renderer in probe.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (first)
                {
                    bounds = renderer.bounds;
                    first = false;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return bounds;
        }
    }
}
