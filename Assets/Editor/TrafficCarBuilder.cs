using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TheBlock.Core;
using TheBlock.Traffic;
using UnityEditor;
using UnityEngine;
using Convert = TheBlock.Core.Convert;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Generates the ambient traffic prefabs — <b>The Block → Build Traffic Cars</b>.
    ///
    /// One prefab per entry in <c>config.traffic.models</c>, each carrying its measured bumper box,
    /// a kinematic Rigidbody, a cull group and the whole street palette as material assets so a
    /// recycled car can be repainted without a per-instance colour.
    ///
    /// <b>The prefab's origin is the body centre in XZ and the contact patch in Y</b>, which is what
    /// makes a lane sample directly usable as a position: the baked network's Y is the road surface,
    /// so "put the car here" needs no ride-height arithmetic at runtime and no assumption about
    /// where the artist put the model's pivot. The web build does the same thing with a
    /// <c>recentre</c> matrix and then adds half the body height back every frame; doing it once, at
    /// build time, in the transform, is the same answer without the per-frame term.
    ///
    /// <b>Materials are cloned onto U15's compressed textures.</b> A .glb's textures are sub-assets
    /// no importer ever compresses (memory: <c>gltfast-textures-never-compressed</c>), and
    /// <c>WorldBuilder</c>'s rebinding pass only ever sees objects in the SCENE — these are prefabs
    /// instantiated at runtime, so they would have quietly pulled the raw copies back in: 110 MB of
    /// RGB24 across the three car models, for cars whose compressed twins are already on disk.
    /// The clones land in <see cref="MaterialFolder"/>, which only this builder writes and only this
    /// builder sweeps, so a world build can never delete them out from under a prefab.
    /// </summary>
    public static class TrafficCarBuilder
    {
        private const string ModelFolder = "Assets/Models";
        private const string PrefabFolder = "Assets/Prefabs/Traffic";
        private const string MaterialFolder = "Assets/Materials/Traffic";

        /// <summary>Mass once a wrecked car becomes a real body. Same order as the Mustang's.</summary>
        private const float Mass = 1400f;

        [MenuItem("The Block/Build Traffic Cars", priority = 22)]
        public static void BuildMenu() => Build();

        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                const string message = "TrafficCarBuilder: stop Play mode first.";
                Debug.LogError(message);
                return message;
            }

            var snapshot = TheBlockConfig.Load(reload: true);
            var traffic = snapshot?.Config?.Traffic;
            if (traffic?.Models == null || traffic.Models.Count == 0)
                return Fail("TrafficCarBuilder: config.traffic.models is empty.");

            var log = new StringBuilder();
            var written = new HashSet<string>(StringComparer.Ordinal);
            var built = new List<string>();

            foreach (var spec in traffic.Models)
            {
                var name = ModelName(spec.Url);
                var model = LoadModel(spec.Url);
                if (model == null)
                {
                    log.AppendLine($"{name,-10} MISSING — no asset for {spec.Url} under {ModelFolder}");
                    continue;
                }

                var path = BuildOne(model, spec, traffic, DrivableOffset(snapshot, name, log), name, written, log);
                if (path != null) built.Add(path);
            }

            VehicleMaterials.Sweep(MaterialFolder, written, log);
            AssetDatabase.SaveAssets();

            var report = $"TrafficCarBuilder — {built.Count} prefab(s)\n{log}";
            Debug.Log(report);
            return report;
        }

        // --- one car ------------------------------------------------------------------------------

        /// <summary>
        /// The rotation that turns this model's TRAFFIC facing into its DRIVABLE facing, baked into
        /// the prefab so a carjack can apply it without doing the algebra at runtime.
        ///
        /// The two conventions are π apart per model and in opposite directions
        /// (<see cref="TheBlockConfig.TrafficModelSpec.ModelYaw"/> explains why), so this is not a
        /// constant and it is not something to eyeball: it is
        /// <c>trafficYaw · (carYaw · facing)⁻¹</c>, resolved here where both numbers are in view.
        /// Get it wrong and every stolen car drives away pointing backwards.
        /// </summary>
        private static Quaternion DrivableOffset(
            TheBlockConfig.Snapshot snapshot, string name, StringBuilder log)
        {
            var spec = snapshot?.Config?.Traffic?.Models?
                .FirstOrDefault(m => string.Equals(ModelName(m.Url), name, StringComparison.OrdinalIgnoreCase));
            var car = snapshot?.Config?.Vehicle?.Cars?
                .FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

            if (spec == null || car == null)
            {
                log.AppendLine(
                    $"{name,-10} NOTE — no config.vehicle.cars entry, so this model cannot be carjacked");
                return Quaternion.identity;
            }

            return Convert.RotFromRadians(spec.ModelYaw) *
                   Quaternion.Inverse(Convert.RotFromRadians(car.ModelYaw) * Convert.ModelFacing);
        }

        private static string BuildOne(
            GameObject model, TheBlockConfig.TrafficModelSpec spec, TheBlockConfig.TrafficSpec traffic,
            Quaternion drivableOffset, string name, HashSet<string> written, StringBuilder log)
        {
            var root = new GameObject($"TrafficCar_{name}");

            try
            {
                float scale = spec.Scale <= 0f ? 1f : spec.Scale;

                var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                visual.name = "Visual";
                visual.transform.localPosition = Vector3.zero;
                // NOT `* Convert.ModelFacing`, unlike every other vehicle builder in this project.
                // `config.traffic.models[].modelYaw` is authored in the OPPOSITE convention to
                // `vehicle.cars` and `lotCars` (see TrafficModelSpec.ModelYaw): it already turns the
                // nose to the travel direction, which the web build's heading math puts at +Z — and
                // Z survives the X-negation untouched, so +Z there is +Z here. Composing the
                // -Z→+Z flip on top of numbers that contain it drives every ambient car in reverse.
                visual.transform.localRotation = Convert.RotFromRadians(spec.ModelYaw);
                visual.transform.localScale = Vector3.one * scale;

                var renderers = visual.GetComponentsInChildren<MeshRenderer>(true);
                if (renderers.Length == 0)
                {
                    log.AppendLine($"{name,-10} SKIPPED — the model has no MeshRenderers");
                    return null;
                }

                // Measured with the root at the origin and the model already turned, so this AABB is
                // the box the car will actually occupy. Measuring before the rotation would give the
                // pre-turn box, and measuring a car already yawed into a lane would give the bounding
                // box of a bounding box — the trap U13 hit on the lot.
                var body = renderers[0].bounds;
                foreach (var renderer in renderers) body.Encapsulate(renderer.bounds);

                var shift = new Vector3(body.center.x, body.min.y, body.center.z);
                visual.transform.localPosition = -shift;
                body.center -= shift;

                // The GLB ships a lamp-slide animation on some of these models and glTFast adds an
                // Animator for it. Nothing here animates; an Animator on 32 cars is 32 avatarless
                // update calls a frame for no output.
                foreach (var animator in visual.GetComponentsInChildren<Animator>(true))
                    UnityEngine.Object.DestroyImmediate(animator);

                var paints = BuildMaterials(
                    visual, spec, traffic, name, written, log,
                    out var paintRenderers, out var paintSlots);

                AddCollider(root, body);
                AddBody(root);
                AddCullGroup(root, body, traffic.CullDistanceM);

                var car = root.AddComponent<TrafficCar>();
                float halfLength = Mathf.Max(body.size.x, body.size.z) * 0.5f;
                float halfWidth = Mathf.Min(body.size.x, body.size.z) * 0.5f;
                car.Configure(name, drivableOffset, halfLength, halfWidth, body.size.y, paintRenderers, paintSlots, paints);

                var path = SavePrefab(root, $"TrafficCar_{name}");
                log.AppendLine(
                    $"{name,-10} {body.size.x:0.00} x {body.size.y:0.00} x {body.size.z:0.00} m, " +
                    $"scale {scale:0.##}, {paints.Length} paint(s), {paintRenderers.Length} paint slot(s) → {path}");
                return path;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// One box per car, not a MeshCollider.
        ///
        /// The web build uses a Rapier cuboid for the same reason U13's lot cars use a box: a convex
        /// hull of tens of thousands of triangles buys nothing against another box, a wheel and a
        /// capsule, and would cost it thirty-two times over. The root's scale is 1 — the model's
        /// scale lives on the Visual child — so unlike the lot cars this size needs no dividing back
        /// out, which is the mistake that made the 37.4× Avenger's collider a kilometre wide.
        /// </summary>
        private static void AddCollider(GameObject root, Bounds body)
        {
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, body.size.y * 0.5f, 0f);
            box.size = body.size;
        }

        /// <summary>
        /// Kinematic, interpolated, continuous.
        ///
        /// <c>Interpolate</c> is what lets the sim run in <c>FixedUpdate</c> at 50 Hz and still look
        /// smooth at any frame rate — without it a car driven by <c>MovePosition</c> visibly steps.
        /// <c>ContinuousSpeculative</c> is the mode a kinematic body is allowed to have, and it is
        /// worth having: a car crossing a junction at 12 m/s moves a quarter of its own length
        /// between steps, which is enough for a thin collider to be missed.
        /// </summary>
        private static void AddBody(GameObject root)
        {
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.mass = Mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        /// <summary>
        /// Culls the car past the config's own cull distance with a single-LOD <c>LODGroup</c>.
        ///
        /// Identical to the lot cars': LOD screen height is relative, so the threshold is derived
        /// from the car's own height rather than typed in as a bare 0.01. The sim keeps running past
        /// this — a car is recycled by distance, not by visibility — so all this decides is whether
        /// the renderer is submitted.
        /// </summary>
        private static void AddCullGroup(GameObject root, Bounds body, float cullDistance)
        {
            float fov = Camera.main != null ? Camera.main.fieldOfView : 75f;
            float screenHeight = body.size.y /
                                 (2f * Mathf.Max(cullDistance, 1f) * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));

            var group = root.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(screenHeight, root.GetComponentsInChildren<Renderer>(true)) });
            group.RecalculateBounds();
        }

        // --- materials ----------------------------------------------------------------------------

        /// <summary>
        /// Clones every material this model uses into <see cref="MaterialFolder"/>, rebinding each
        /// texture that lives inside a .glb onto its compressed twin, and expands the body-paint
        /// material into one asset per street colour.
        ///
        /// <b>Why materials and not per-instance colour.</b> The web build clones the paint material
        /// white and drives the colour through <c>InstancedMesh.setColorAt</c>, because that is the
        /// only place three.js has to put it. Here a <c>MaterialPropertyBlock</c> would be the
        /// obvious reach and is the wrong one: it gives every car its own draw call, while twelve
        /// shared material assets give twelve batches for the whole city. Same call U1 made for the
        /// facade tint and U13 for the lot.
        /// </summary>
        private static Material[] BuildMaterials(
            GameObject visual, TheBlockConfig.TrafficModelSpec spec, TheBlockConfig.TrafficSpec traffic,
            string name, HashSet<string> written, StringBuilder log,
            out MeshRenderer[] paintRenderers, out int[] paintSlots)
        {
            var palette = spec.Palette != null && spec.Palette.Count > 0 ? spec.Palette : traffic.Palette;
            if (palette == null || palette.Count == 0) palette = new List<int> { 0xFFFFFF };

            var clones = new Dictionary<Material, Material>();
            var slotOwners = new List<MeshRenderer>();
            var slotIndices = new List<int>();
            Material paintSource = null;
            int rebound = 0, misses = 0;

            foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (source == null) continue;

                    if (!clones.TryGetValue(source, out var clone))
                    {
                        clone = VehicleMaterials.Clone(source, MaterialFolder, name, written);
                        rebound += VehicleMaterials.RebindCompressed(clone, ref misses);
                        clones[source] = clone;
                    }

                    materials[i] = clone;
                    changed = true;

                    if (!VehicleMaterials.IsPaint(source)) continue;

                    if (paintSource == null) paintSource = clone;
                    else if (paintSource != clone)
                        log.AppendLine(
                            $"{name,-10} NOTE — a second distinct paint material ('{source.name}'); " +
                            "the whole car takes the first one's palette");

                    slotOwners.Add(renderer);
                    slotIndices.Add(i);
                }

                if (changed) renderer.sharedMaterials = materials;
            }

            paintRenderers = slotOwners.ToArray();
            paintSlots = slotIndices.ToArray();

            if (paintSource == null)
            {
                log.AppendLine(
                    $"{name,-10} NOTE — no material named {string.Join("/", VehicleMaterials.PaintMaterialNames)}; " +
                    "this model keeps its shipped colour");
                if (misses > 0) log.AppendLine($"{name,-10} {misses} texture(s) have no compressed twin");
                return Array.Empty<Material>();
            }

            var paints = new Material[palette.Count];
            for (int i = 0; i < palette.Count; i++)
                paints[i] = VehicleMaterials.PaintVariant(paintSource, MaterialFolder, name, palette[i], written);

            // The prefab is saved showing the first colour; every instance is repainted on spawn.
            foreach (var renderer in paintRenderers.Distinct())
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] == paintSource) materials[i] = paints[0];
                renderer.sharedMaterials = materials;
            }

            log.AppendLine(
                $"{name,-10} materials {clones.Count} cloned, {rebound} texture slot(s) onto compressed copies" +
                (misses > 0 ? $", {misses} with no twin (run The Block → Compress Textures)" : string.Empty));
            return paints;
        }

        // --- plumbing -----------------------------------------------------------------------------

        /// <summary>Resolves a config URL to an asset by base file name, as WorldBuilder does.</summary>
        private static GameObject LoadModel(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var fileName = url.Substring(url.LastIndexOf('/') + 1);
            var bare = System.IO.Path.GetFileNameWithoutExtension(fileName);

            var path = AssetDatabase
                .FindAssets($"{bare} t:GameObject", new[] { ModelFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p =>
                    string.Equals(System.IO.Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase));

            return path == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>"/models/optimized/lod/tesla.glb" → "Tesla". The config names these nowhere else.</summary>
        private static string ModelName(string url)
        {
            if (string.IsNullOrEmpty(url)) return "Unnamed";
            var bare = System.IO.Path.GetFileNameWithoutExtension(url);
            return bare.Length == 0 ? "Unnamed" : char.ToUpperInvariant(bare[0]) + bare.Substring(1);
        }

        private static string SavePrefab(GameObject root, string prefabName)
        {
            VehicleMaterials.EnsureFolder(PrefabFolder);
            var path = $"{PrefabFolder}/{prefabName}.prefab";
            // SaveAsPrefabAsset over an existing path keeps the GUID, so the scene's TrafficSystem
            // keeps its reference across a rebuild.
            PrefabUtility.SaveAsPrefabAsset(root, path);
            return path;
        }

        private static string Fail(string message)
        {
            Debug.LogError(message);
            return message;
        }
    }
}
