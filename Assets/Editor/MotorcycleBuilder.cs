using System;
using System.Linq;
using System.Text;
using TheBlock.Core;
using TheBlock.Vehicles;
using UnityEditor;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Builds a rideable motorcycle prefab from its imported GLB and its <c>config.vehicle.motorcycle</c> entry.
    ///
    /// Simple compared to <c>CarBuilder</c>: no WheelColliders, no door rigging, no suspension tuning.
    /// Just the visual model, a MotorcycleController, and a rider anchor. The motorcycle stays kinematic
    /// (scalar speed + heading, like the web build) because a two-wheeler with PhysX WheelColliders
    /// tips constantly and is not worth the engineering time to stabilize.
    /// </summary>
    public static class MotorcycleBuilder
    {
        private const string ModelFolder = "Assets/Models/Vehicles";
        private const string PrefabFolder = "Assets/Prefabs/Vehicles";

        [MenuItem("The Block/Build Motorcycle", priority = 21)]
        public static void BuildMenu() => Build();

        /// <summary>Builds the motorcycle prefab. Returns the report.</summary>
        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                const string message = "MotorcycleBuilder: stop Play mode first.";
                Debug.LogError(message);
                return message;
            }

            var snapshot = TheBlockConfig.Load(reload: true);
            var spec = snapshot?.Config?.Vehicle?.Motorcycle;
            if (spec == null)
                return Fail("MotorcycleBuilder: config has no vehicle.motorcycle section.");

            var model = LoadModel(spec.ModelUrl);
            if (model == null)
                return Fail($"MotorcycleBuilder: no asset for {spec.ModelUrl} under {ModelFolder}.");

            var log = new StringBuilder();
            var root = new GameObject("Motorcycle");

            try
            {
                // Build the visual model
                var visual = BuildVisual(model, spec, root.transform, log, out var bounds);
                if (visual == null) return Fail(log.ToString());

                // Add Rigidbody (kinematic)
                var rb = root.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;

                // Add CharacterController for collision detection
                var cc = root.AddComponent<CharacterController>();
                cc.center = bounds.center;
                cc.height = bounds.size.y;
                cc.radius = bounds.size.x / 2f;

                // Add MotorcycleController
                var controller = root.AddComponent<MotorcycleController>();

                // Create rider anchor (where the rider gets parented during driving)
                var riderAnchor = new GameObject("RiderAnchor");
                riderAnchor.transform.SetParent(root.transform, false);
                riderAnchor.transform.localPosition = Vector3.zero;
                riderAnchor.transform.localRotation = Quaternion.identity;
                SerializedObject soController = new SerializedObject(controller);
                soController.FindProperty("riderAnchor").objectReferenceValue = riderAnchor.transform;
                soController.ApplyModifiedProperties();

                // Save as prefab
                var prefabPath = $"{PrefabFolder}/Motorcycle.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                UnityEngine.Object.DestroyImmediate(root);

                var report = $"✓ Motorcycle built at {prefabPath}";
                Debug.Log(report);
                log.AppendLine(report);
                return log.ToString();
            }
            catch (Exception e)
            {
                return Fail($"MotorcycleBuilder exception: {e.Message}\n{e.StackTrace}");
            }
        }

        /// <summary>Load and clone the motorcycle model from the assets folder.</summary>
        private static GameObject LoadModel(string url)
        {
            var filename = System.IO.Path.GetFileNameWithoutExtension(url);
            var asset = AssetDatabase.FindAssets(filename)
                .FirstOrDefault(guid => AssetDatabase.GUIDToAssetPath(guid).StartsWith(ModelFolder));
            if (asset == null) return null;

            var path = AssetDatabase.GUIDToAssetPath(asset);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return prefab != null ? UnityEngine.Object.Instantiate(prefab) : null;
        }

        /// <summary>Build the visual model with scale and rotation applied.</summary>
        private static GameObject BuildVisual(
            GameObject model,
            TheBlockConfig.MotorcycleSpec spec,
            Transform parent,
            StringBuilder log,
            out Bounds bounds)
        {
            model.transform.SetParent(parent, false);
            model.transform.localScale = Vector3.one * spec.ModelScale;
            model.transform.localRotation = Quaternion.AngleAxis(spec.ModelYaw * Mathf.Rad2Deg, Vector3.up);

            // Measure bounds
            var meshes = model.GetComponentsInChildren<MeshFilter>();
            bounds = new Bounds();
            if (meshes.Length > 0)
            {
                bounds = meshes[0].mesh.bounds;
                foreach (var mf in meshes)
                {
                    bounds.Encapsulate(mf.mesh.bounds);
                }
            }

            log.AppendLine($"  Model: {spec.ModelUrl} at scale {spec.ModelScale:0.##}, yaw {spec.ModelYaw:0.##} rad");
            log.AppendLine($"  Bounds: {bounds.min:0.##} to {bounds.max:0.##}");

            return model;
        }

        private static string Fail(string message)
        {
            Debug.LogError(message);
            return message;
        }
    }
}
