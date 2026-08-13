using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TheBlock.Core;
using TheBlock.Vehicles;
using UnityEditor;
using UnityEngine;
using Convert = TheBlock.Core.Convert;
using Debug = UnityEngine.Debug;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Builds a drivable car prefab from its imported GLB and its <c>config.vehicle.cars</c> entry.
    ///
    /// Generated rather than hand-assembled, for the reason <c>WorldBuilder</c> and
    /// <c>JoeAnimatorBuilder</c> are: four WheelColliders dragged into place by hand are invisible in
    /// review, impossible to reproduce, and quietly wrong the moment the model is re-exported. Here
    /// every number is either measured off the mesh or written down below with its reasoning, and
    /// re-running the menu item rebuilds the prefab in place so its GUID — and every scene reference
    /// to it — survives.
    ///
    /// Nothing here edits the .glb. The import stays exactly as downloaded; the corrections live in
    /// the prefab, the same rule <c>WorldBuilder.AssetAliases</c> follows.
    /// </summary>
    public static class CarBuilder
    {
        private const string ModelFolder = "Assets/Models/Vehicles";
        private const string PrefabFolder = "Assets/Prefabs/Vehicles";
        private const string MaterialFolder = "Assets/Materials/Vehicles";

        /// <summary>The material slot the web build recolours. Named the same in every rigged car.</summary>
        private const string PaintMaterialName = "CarPrimaryColor";

        // --- physics, all derived by feel for PhysX (port rule 2) ---------------------------------

        /// <summary>Kerb weight in kg. A real '65 Mustang is ~1300; this model is ~1.2x oversized.</summary>
        private const float Mass = 1400f;

        /// <summary>Total suspension travel in metres.</summary>
        private const float SuspensionDistance = 0.25f;

        /// <summary>Where along that travel the wheel rests. 0.5 leaves equal room to compress and droop.</summary>
        private const float SuspensionTarget = 0.5f;

        /// <summary>
        /// Spring rate per wheel, N/m. Sized so the car actually rests where
        /// <see cref="SuspensionTarget"/> says it does: a quarter of the weight is 3,434 N, and it
        /// has to be held at 0.125 m of deflection, so 3434 / 0.125 ≈ 27,500.
        /// </summary>
        private const float SuspensionSpring = 27500f;

        /// <summary>
        /// Damping, N·s/m. Critical for a 350 kg corner on that spring is 2·sqrt(k·m) ≈ 6,200;
        /// this is a little over half of it — settled in one bounce without the jitter an
        /// over-damped WheelCollider gets at low speed.
        /// </summary>
        private const float SuspensionDamper = 3500f;

        /// <summary>Chassis box floor, metres above the contact patch. Below the axle, clear of the road.</summary>
        private const float ChassisGroundGap = 0.25f;

        [MenuItem("The Block/Build Mustang", priority = 20)]
        public static void BuildMustangMenu() => Build("Mustang");

        /// <summary>Builds one car by its <c>config.vehicle.cars[].name</c>. Returns the report.</summary>
        public static string Build(string carName)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                const string message = "CarBuilder: stop Play mode first.";
                Debug.LogError(message);
                return message;
            }

            var snapshot = TheBlockConfig.Load(reload: true);
            var spec = snapshot?.Config?.Vehicle?.Cars?
                .FirstOrDefault(c => string.Equals(c.Name, carName, StringComparison.OrdinalIgnoreCase));
            if (spec == null)
                return Fail($"CarBuilder: no car named '{carName}' in config.vehicle.cars.");

            var model = LoadModel(spec.ModelUrl);
            if (model == null)
                return Fail($"CarBuilder: no asset for {spec.ModelUrl} under {ModelFolder}.");

            var log = new StringBuilder();
            var root = new GameObject(carName);

            try
            {
                var visual = BuildVisual(model, spec, root.transform, log, out var bounds, out var wheelBones);
                if (visual == null) return Fail(log.ToString());

                BuildChassis(root, bounds, log);
                var wheels = BuildWheels(root.transform, wheelBones, bounds, log);
                Wire(root, wheels, log);
                if (spec.BodyColor.HasValue) Paint(visual, carName, spec.BodyColor.Value, log);

                var path = SavePrefab(root, carName);
                log.AppendLine($"prefab  {path}");

                var report = $"CarBuilder — {carName}\n{log}";
                Debug.Log(report);
                return report;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // --- model -------------------------------------------------------------------------------

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

        /// <summary>
        /// Parents the model under the root, turns it to face Unity's +Z, and drops it so the root's
        /// origin lands on the tyre contact patch.
        ///
        /// That last part is what makes the config spawns usable as written: <c>spawn</c> carries no
        /// Y and <c>roadSurfaceY</c> is the road, so an origin at the contact patch means "put the
        /// car here" is literally the road height. The model's own origin sits a few centimetres
        /// below its tyres, which would otherwise bury or float it by exactly that much.
        /// </summary>
        private static GameObject BuildVisual(
            GameObject model, TheBlockConfig.CarSpec spec, Transform parent, StringBuilder log,
            out Bounds bounds, out Dictionary<Corner, Transform> wheelBones)
        {
            bounds = default;
            wheelBones = null;

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            visual.name = "Visual";
            visual.transform.localPosition = Vector3.zero;
            // Both rotations are about Y so they commute; see Convert.ModelFacing.
            visual.transform.localRotation = Convert.RotFromRadians(spec.ModelYaw) * Convert.ModelFacing;
            visual.transform.localScale = Vector3.one * (spec.ModelScale <= 0f ? 1f : spec.ModelScale);

            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                log.AppendLine("model has no renderers");
                return null;
            }

            var world = renderers[0].bounds;
            foreach (var r in renderers) world.Encapsulate(r.bounds);

            // Everything from here is measured with the root at the origin, so world == root-local.
            var drop = world.min.y;
            visual.transform.localPosition = new Vector3(0f, -drop, 0f);
            world.center -= new Vector3(0f, drop, 0f);
            bounds = world;

            log.AppendLine($"body    {Fmt(bounds.size)} m, centre {Fmt(bounds.center)}");
            log.AppendLine($"yaw     modelYaw {spec.ModelYaw:0.###} rad + 180° facing = " +
                           $"{visual.transform.localEulerAngles.y:0.#}°");
            log.AppendLine($"drop    visual lowered {drop:0.###} m so the root origin is the contact patch");

            wheelBones = FindWheelBones(visual.transform, log);
            return wheelBones == null ? null : visual;
        }

        // --- wheels ------------------------------------------------------------------------------

        public enum Corner { FrontLeft, FrontRight, RearLeft, RearRight }

        /// <summary>
        /// Finds the four wheel bones and sorts them into corners BY POSITION, not by name.
        ///
        /// The names cannot be trusted on their own: glTFast negates X on import, so the bone the
        /// rig calls <c>wheel_Front_L_0</c> arrives on Unity's +X side and only becomes left again
        /// after <see cref="Convert.ModelFacing"/>. Reading the sign off the transform sidesteps that
        /// entirely — and works on the next car, whose rigger may have used other names.
        ///
        /// The steering knuckles (<c>wheel_dir_*</c>) sit at the same spot as the front wheels and
        /// would tie for the corner, so they are filtered out by name first.
        /// </summary>
        private static Dictionary<Corner, Transform> FindWheelBones(Transform visual, StringBuilder log)
        {
            var candidates = visual.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.IndexOf("wheel", StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(t => t.name.IndexOf("dir", StringComparison.OrdinalIgnoreCase) < 0)
                .ToList();

            if (candidates.Count != 4)
            {
                log.AppendLine($"expected 4 wheel bones, found {candidates.Count}: " +
                               string.Join(", ", candidates.Select(c => c.name)));
                return null;
            }

            var map = new Dictionary<Corner, Transform>();
            foreach (var bone in candidates)
            {
                var p = bone.position;
                var corner = p.z > 0f
                    ? p.x < 0f ? Corner.FrontLeft : Corner.FrontRight
                    : p.x < 0f ? Corner.RearLeft : Corner.RearRight;

                if (map.ContainsKey(corner))
                {
                    log.AppendLine($"two bones landed on {corner}: {map[corner].name} and {bone.name}");
                    return null;
                }

                map[corner] = bone;
            }

            foreach (var pair in map.OrderBy(p => p.Key))
                log.AppendLine($"wheel   {pair.Key,-10} {pair.Value.name,-18} at {Fmt(pair.Value.position)}");

            return map;
        }

        /// <summary>
        /// Creates a WheelCollider per corner.
        ///
        /// Radius is measured, not typed: the wheel centre's height above the contact patch IS the
        /// rolling radius once the root origin sits on that patch. The collider is then raised by the
        /// resting suspension deflection, so the car settles back to the height the model was
        /// authored at instead of sagging by that much on the first frame.
        /// </summary>
        private static Dictionary<Corner, WheelCollider> BuildWheels(
            Transform root, Dictionary<Corner, Transform> bones, Bounds bounds, StringBuilder log)
        {
            var group = new GameObject("Wheels");
            group.transform.SetParent(root, false);

            var radius = bones.Values.Average(b => b.position.y);
            var rest = SuspensionDistance * SuspensionTarget;
            log.AppendLine($"radius  {radius:0.###} m (wheel centre height above the contact patch), " +
                           $"collider raised {rest:0.###} m for suspension rest");

            var wheels = new Dictionary<Corner, WheelCollider>();
            foreach (var pair in bones.OrderBy(p => p.Key))
            {
                var go = new GameObject(pair.Key.ToString());
                go.transform.SetParent(group.transform, false);
                go.transform.localPosition = pair.Value.position + Vector3.up * rest;

                var wheel = go.AddComponent<WheelCollider>();
                wheel.radius = radius;
                wheel.mass = 25f;
                wheel.wheelDampingRate = 0.25f;
                wheel.suspensionDistance = SuspensionDistance;
                wheel.forceAppPointDistance = 0f;

                wheel.suspensionSpring = new JointSpring
                {
                    spring = SuspensionSpring,
                    damper = SuspensionDamper,
                    targetPosition = SuspensionTarget,
                };

                // Grip curves. Stiffness above 1 is the arcade dial: real tyres let go long before
                // this, and a car that lets go at 40 km/h in a city is miserable to drive.
                wheel.forwardFriction = new WheelFrictionCurve
                {
                    extremumSlip = 0.4f, extremumValue = 1f,
                    asymptoteSlip = 0.8f, asymptoteValue = 0.5f,
                    stiffness = 2f,
                };
                wheel.sidewaysFriction = new WheelFrictionCurve
                {
                    extremumSlip = 0.25f, extremumValue = 1f,
                    asymptoteSlip = 0.5f, asymptoteValue = 0.75f,
                    stiffness = 2.2f,
                };

                var visual = go.AddComponent<CarWheel>();
                visual.BindBone(pair.Value);
                wheels[pair.Key] = wheel;
            }

            return wheels;
        }

        // --- chassis -----------------------------------------------------------------------------

        /// <summary>
        /// Rigidbody plus one box for the body.
        ///
        /// The box floor is held clear of the road on purpose. A collider that reaches the tarmac
        /// fights the wheels for ground contact and the car judders; lifting it above the contact
        /// patch leaves the suspension to do that job alone while the box still catches walls, kerbs
        /// and other cars. The web build arrived at the same shape from the Rapier side — which makes
        /// it design rather than scar tissue, so it carries over.
        /// </summary>
        private static void BuildChassis(GameObject root, Bounds bounds, StringBuilder log)
        {
            var body = root.AddComponent<Rigidbody>();
            body.mass = Mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            // A 20 m/s car against a thin building wall is exactly the tunnelling case discrete
            // collision misses.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearDamping = 0f;
            body.angularDamping = 0.05f;

            var top = bounds.max.y;
            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(bounds.size.x, Mathf.Max(0.1f, top - ChassisGroundGap), bounds.size.z);
            box.center = new Vector3(bounds.center.x, (top + ChassisGroundGap) * 0.5f, bounds.center.z);

            log.AppendLine($"chassis box {Fmt(box.size)} centred {Fmt(box.center)}, floor at {ChassisGroundGap:0.##} m");
            log.AppendLine($"mass    {Mass:0} kg");
        }

        private static void Wire(GameObject root, Dictionary<Corner, WheelCollider> wheels, StringBuilder log)
        {
            var car = root.AddComponent<CarController>();
            var serialized = new SerializedObject(car);
            serialized.FindProperty("frontLeft").objectReferenceValue = wheels[Corner.FrontLeft];
            serialized.FindProperty("frontRight").objectReferenceValue = wheels[Corner.FrontRight];
            serialized.FindProperty("rearLeft").objectReferenceValue = wheels[Corner.RearLeft];
            serialized.FindProperty("rearRight").objectReferenceValue = wheels[Corner.RearRight];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine("wired   CarController to all four wheels");
        }

        // --- paint -------------------------------------------------------------------------------

        /// <summary>
        /// Rebinds the paint slot to a material ASSET carrying the config colour.
        ///
        /// Same call as U1's facade tint: the web build recolours the material in code at load
        /// because three.js cannot author material assets, Unity can, and an asset costs nothing at
        /// runtime and is editable without a rebuild. The imported material inside the .glb is left
        /// untouched — it is shared by every copy of this model.
        /// </summary>
        private static void Paint(GameObject visual, string carName, int packed, StringBuilder log)
        {
            var source = visual.GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials)
                .FirstOrDefault(m => m != null && m.name == PaintMaterialName);

            if (source == null)
            {
                log.AppendLine($"paint   SKIPPED — no '{PaintMaterialName}' slot on this model");
                return;
            }

            EnsureFolder(MaterialFolder);
            var path = $"{MaterialFolder}/{carName}_Body.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = source.shader;
                material.CopyPropertiesFromMaterial(source);
            }

            var color = TheBlockConfig.ColorFromHex(packed);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            EditorUtility.SetDirty(material);

            var slots = 0;
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null || materials[i].name != PaintMaterialName) continue;
                    materials[i] = material;
                    changed = true;
                    slots++;
                }

                if (changed) renderer.sharedMaterials = materials;
            }

            log.AppendLine($"paint   #{packed:x6} on {slots} slot(s) via {path}");
        }

        // --- output ------------------------------------------------------------------------------

        private static string SavePrefab(GameObject root, string carName)
        {
            EnsureFolder(PrefabFolder);
            var path = $"{PrefabFolder}/{carName}.prefab";
            // SaveAsPrefabAsset over an existing path keeps the GUID, so scene references survive
            // a rebuild — the same reason JoeAnimatorBuilder writes its controller in place.
            PrefabUtility.SaveAsPrefabAsset(root, path);
            AssetDatabase.SaveAssets();
            return path;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parts = folder.Split('/');
            var built = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = $"{built}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(built, parts[i]);
                built = next;
            }
        }

        private static string Fail(string message)
        {
            Debug.LogError(message);
            return message;
        }

        private static string Fmt(Vector3 v) => $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";
    }
}
