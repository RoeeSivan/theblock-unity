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
    /// automatically, so the draw-call win survives — but each car is culled on its own bounds, and
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
        /// 180 m clears the whole 165 m lot from its far corner, so a car is never missing from a
        /// view that contains the lot — it only stops being drawn from across the city.
        /// </summary>
        private const float CullDistance = 180f;

        private static void BuildLotCars(
            Transform parent, TheBlockConfig.LotCarsSpec cfg, Options options, Report report)
        {
            if (cfg?.Models == null || cfg.Models.Count == 0 || cfg.LotBounds == null) return;

            var group = NewGroup("LotCars", parent);
            var stalls = LayOutStalls(cfg);
            if (stalls.Count == 0)
            {
                report.Warnings.Add("lot cars — the stall grid came out empty; check bounds/rowDepth");
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
                // view at the same time — see LotCar.DriveRotation.
                var drivable = TheBlockConfig.Load()?.Config?.Vehicle?.Cars?
                    .FirstOrDefault(c => string.Equals(
                        c.Name, spec.Name, System.StringComparison.OrdinalIgnoreCase));
                if (drivable == null)
                    report.Warnings.Add(
                        $"lot cars — no config.vehicle.cars entry named '{spec.Name}', so these " +
                        "fillers cannot be promoted on E");

                // Measured once off the prefab — every instance of a model is the same box, and the
                // config states no size of its own. The probe carries the model's scale, so `body` is
                // in metres of world space, which is what both the placement and the cull threshold
                // want; the collider divides it back out.
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
                    // bottom — not its centre — is what gets placed. The web build recentres the body
                    // to the origin and adds half the height, which assumes a centred pivot; this
                    // does not need to assume anything.
                    car.transform.position =
                        Convert.Pos(stall.X, cfg.Y - body.min.y, stall.Z);

                    PaintCar(car, spec, stall.Paint, paints, report);
                    if (options.Colliders) AddLotCarCollider(car, body, scale, report);
                    AddCullGroup(car, body);
                    SetDistrictStaticFlags(car);

                    // U17b: what E needs to promote this filler into the drivable car of the same
                    // model and colour. The paint is looked up rather than rebuilt — PaintCar has
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
                report.Missing.Add($"lot cars — no model asset for {string.Join(", ", skipped)}");

            report.Placed.Add($"LotCars — {placed} parked cars in {stalls.Count} filled stalls");
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
        /// repeated to the far edge — row A noses -Z, row B noses +Z.
        /// </summary>
        private static List<LotStall> LayOutStalls(TheBlockConfig.LotCarsSpec cfg)
        {
            var rand = Mulberry32(cfg.Seed);
            var totalWeight = cfg.Palette?.Sum(p => p.Weight) ?? 0f;

            int PickPaint(int modelIndex)
            {
                // A per-model palette is a uniform pick, and it consumes exactly ONE rand() like the
                // weighted path does — otherwise the shared stream desynchronises and the whole lot
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
        /// unsigned shift — which is what <c>uint</c> arithmetic is here, so no reinterpretation is
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
                    $"lot cars — {path} did not load as a GameObject (a required glTF extension glTFast " +
                    "cannot read imports as a DefaultAsset; see tools/glb-webp-to-png.py)");
            return prefab;
        }

        /// <summary>
        /// Gives one car its paint, by rebinding the body-paint slot to a generated material.
        ///
        /// The web build clones that material white and drives the colour per instance, because
        /// three's InstancedMesh has nowhere else to put it. Here the colour is a material asset,
        /// which is the same call U1 made for the facade tint and U11 for the leaf cutouts — and it
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
                report.Warnings.Add($"lot cars — {spec.Name}'s paint material has no base colour to set");

            material.enableInstancing = true;
            return material;
        }

        /// <summary>
        /// One box per car, sized off the model — not a MeshCollider.
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
        /// Culls the car past <see cref="CullDistance"/> with a single-LOD <c>LODGroup</c>.
        ///
        /// LOD screen height is relative, so the threshold is derived from the car's own size: at
        /// distance d a body of height h covers roughly <c>h / (2 d tan(fov/2))</c> of the screen.
        /// Stating it in metres and converting keeps the intent readable — "not drawn from more than
        /// 180 m away" — instead of a bare 0.01.
        /// </summary>
        private static void AddCullGroup(GameObject car, Bounds body)
        {
            var fov = Camera.main != null ? Camera.main.fieldOfView : 75f;
            var screenHeight = body.size.y /
                               (2f * CullDistance * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));

            var group = car.AddComponent<LODGroup>();
            group.SetLODs(new[]
            {
                new LOD(screenHeight, car.GetComponentsInChildren<Renderer>(true)),
            });
            group.RecalculateBounds();
        }

        /// <summary>
        /// The car's body box in metres, measured off a probe standing at the origin unrotated.
        ///
        /// Renderer bounds are world-space and axis-aligned, which is only the body's real box while
        /// the probe is unrotated — hence the caller placing it at identity before asking. Measuring
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
