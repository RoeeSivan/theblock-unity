using System.Collections.Generic;
using System.Linq;
using TheBlock.Core;
using TheBlock.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Convert = TheBlock.Core.Convert;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Builds the two mission vehicles and the buoy - <b>The Block → Build Mission Vehicles</b>.
    /// The helicopter U23's rescue flies and the jetski U24's chase rides, plus the gate marker the
    /// chase spawns nine of.
    ///
    /// <b>All three are Sketchfab exports and all three arrive lying on their nose.</b> That is the
    /// <c>sketchfab-root-matrices-swap-yz</c> memory, and it does not care whether the file has a
    /// cancelling Rx±90 pair: the jetski has one (Rx 90 at scale 0.01, Rx 270 at scale 100) and
    /// still comes in with its 3.79 m length on Y. So each gets <c>Euler(-90, 0, 0)</c> and then the
    /// result is MEASURED and logged, the way the police car's correction was - a stated axis is a
    /// guess until a bounding box agrees with it.
    ///
    /// Two of them also ship geometry that is not the model: the Huey sits on a black display disc
    /// (<c>pCylinder1</c>, 5.5 m across) and the buoy floats in its own 27 m sea plane
    /// (<c>Sea_1</c>). Both are destroyed at build time. Left in, the disc becomes a collider under
    /// the skids and the sea plane becomes a 27 m invisible slab in the middle of the real one.
    /// </summary>
    public static class MissionVehicleBuilder
    {
        private const string PrefabFolder = "Assets/Prefabs/Vehicles";
        private const string HueyPath = "Assets/Models/Vehicles/huey.glb";
        private const string JetskiPath = "Assets/Models/Vehicles/jetski.glb";
        private const string BuoyPath = "Assets/Models/Props/buoy.glb";

        /// <summary>The Sketchfab correction. Measured, not assumed - see the class comment.</summary>
        private static readonly Quaternion Upright = Quaternion.Euler(-90f, 0f, 0f);

        [MenuItem("The Block/Build Mission Vehicles", priority = 27)]
        public static void Build()
        {
            var snapshot = TheBlockConfig.Load(true);
            var vehicle = snapshot?.Config?.Vehicle;
            if (vehicle?.Helicopter == null || vehicle.Jetski == null)
            {
                Debug.LogError("MissionVehicleBuilder: config has no vehicle.helicopter / vehicle.jetski.");
                return;
            }

            System.IO.Directory.CreateDirectory(PrefabFolder);
            AssetDatabase.Refresh();

            var log = new System.Text.StringBuilder("MissionVehicleBuilder\n");
            BuildHelicopter(vehicle.Helicopter, log);
            BuildJetski(vehicle.Jetski, log);
            BuildBuoy(log);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log(log.ToString());
        }

        // ── the Huey ──────────────────────────────────────────────────────────────────────────

        private static void BuildHelicopter(TheBlockConfig.HelicopterSpec spec, System.Text.StringBuilder log)
        {
            var root = NewRoot("Helicopter", HueyPath, spec.ModelScale, spec.ModelYaw, log, out var visual);
            if (root == null) return;

            // The Sketchfab display base. Its 5.5 m disc would collide under the skids and skew
            // every measurement below.
            Kill(visual, "pCylinder1", log);

            var box = MeasureLocal(root);
            log.AppendLine($"  huey measured: {box.size:F2} m, centre {box.center:F2}, bottom {box.min.y:F2}");

            // Sit the model so its lowest point - the skids - is at the prefab origin. Every vehicle
            // prefab in this project shares that origin (body centre in XZ, contact patch in Y), and
            // it is what lets a spawner place one with no ride-height arithmetic.
            visual.localPosition -= new Vector3(box.center.x, box.min.y, box.center.z);

            var body = root.AddComponent<Rigidbody>();
            body.mass = 2200f;

            // FUSELAGE ONLY. The config's box excludes the ~10 m rotor disc, measured in Blender,
            // and the holder is recentred on fuselageCenterY so the box hugs the body with its
            // bottom at the skids. A collider that swallowed the disc could not enter Florentin.
            var collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(spec.Collider.X, spec.Collider.Y, spec.Collider.Z);
            collider.center = new Vector3(0f, spec.Collider.Y * 0.5f, 0f);
            log.AppendLine($"  fuselage collider {collider.size:F2} centred {collider.center:F2} " +
                           $"(rotor disc deliberately outside it)");

            var rotor = root.AddComponent<Rotor>();
            rotor.SetBlades(BuildRotorPivots(visual, log));

            // The seat. No rider is shown (the config carries no rider block for the helicopter), so
            // this only has to be somewhere sane inside the cabin for the machine to mount to.
            var seat = new GameObject("Seat").transform;
            seat.SetParent(root.transform, false);
            seat.localPosition = new Vector3(0f, spec.FuselageCenterY * 0.6f, 0.4f);

            var controller = root.AddComponent<HelicopterController>();
            controller.Configure(seat, rotor, spec);

            Save(root, "Helicopter", log);
        }

        /// <summary>
        /// Wraps each rotor node in a pivot at its own hub.
        ///
        /// <b>Both nodes have their origin at the MODEL centre, not at their hub</b> - measured, and
        /// it is what the web build's <c>rotor.ts</c> found too. Spin such a node directly and the
        /// blades orbit the fuselage. So the pivot goes at the node's bounding-box centre, the node
        /// is re-parented under it with its world pose preserved, and the pivot spins.
        /// </summary>
        private static Rotor.Blade[] BuildRotorPivots(Transform visual, System.Text.StringBuilder log)
        {
            var blades = new List<Rotor.Blade>();

            foreach (var (nodeName, axis, speed) in new[]
                     {
                         ("Mainrotor", Vector3.up, 900f),
                         ("TailRotor", Vector3.right, 1400f),
                     })
            {
                var node = Find(visual, nodeName);
                if (node == null)
                {
                    log.AppendLine($"  ⚠ no '{nodeName}' node - that rotor will not spin");
                    continue;
                }

                var hub = WorldBounds(node)?.center;
                if (hub == null) continue;

                var pivot = new GameObject($"{nodeName}_Pivot").transform;
                pivot.SetParent(node.parent, false);
                pivot.position = hub.Value;

                // ROOT-ALIGNED, not node-aligned. The spin axes are stated in the craft's own frame
                // - the main disc turns about its up, the tail about its right - and the visual
                // subtree carries the Sketchfab Rx(−90) plus the config's yaw flip, so a pivot
                // inheriting the NODE's rotation would spin the main rotor about the fuselage's
                // length. The root is unrotated at build time, so identity here IS the craft's frame,
                // and the pivot then rides the craft's rotation at runtime like any other child.
                pivot.rotation = Quaternion.identity;
                node.SetParent(pivot, true); // worldPositionStays: the blades do not move

                blades.Add(new Rotor.Blade { Pivot = pivot, Axis = axis, Speed = speed });
                log.AppendLine($"  rotor '{nodeName}': pivot at hub {hub.Value:F2}, {speed}°/s about {axis}");
            }

            return blades.ToArray();
        }

        // ── the jetski ────────────────────────────────────────────────────────────────────────

        private static void BuildJetski(TheBlockConfig.JetskiSpec spec, System.Text.StringBuilder log)
        {
            var root = NewRoot("Jetski", JetskiPath, spec.ModelScale, spec.ModelYaw, log, out var visual);
            if (root == null) return;

            var box = MeasureLocal(root);
            log.AppendLine($"  jetski measured: {box.size:F2} m, centre {box.center:F2}");

            // Centred in XZ and in Y, unlike the road vehicles: a hull floats AROUND the waterline
            // rather than resting on it, so the origin is the waterline and `floatY` nudges it.
            visual.localPosition -= box.center;

            var body = root.AddComponent<Rigidbody>();
            body.mass = 350f;

            var collider = root.AddComponent<CapsuleCollider>();
            collider.direction = 2; // along Z, the hull's length
            collider.height = box.size.z;
            collider.radius = Mathf.Max(box.size.x, box.size.y) * 0.5f;

            var seat = new GameObject("Seat").transform;
            seat.SetParent(root.transform, false);
            seat.localPosition = Convert.ModelOffset(spec.Rider.Seat.Raw);

            var controller = root.AddComponent<JetskiController>();
            controller.Configure(seat, spec);

            log.AppendLine($"  jetski hull capsule h{collider.height:F2} r{collider.radius:F2}, " +
                           $"seat {seat.localPosition:F2}");

            Save(root, "Jetski", log);
        }

        // ── the buoy ──────────────────────────────────────────────────────────────────────────

        private static void BuildBuoy(System.Text.StringBuilder log)
        {
            var root = NewRoot("Buoy", BuoyPath, 1f, 0f, log, out var visual);
            if (root == null) return;

            // The model floats in its own 27 m sea plane. Left in, every gate drops an invisible
            // slab of water into the real sea.
            Kill(visual, "Sea_1", log);

            var box = MeasureLocal(root);
            visual.localPosition -= new Vector3(box.center.x, box.min.y, box.center.z);
            log.AppendLine($"  buoy measured: {box.size:F2} m (the 27 m sea plane is gone)");

            // A fixed cylinder the player's ski steers around. The thief has one too - in the web he
            // needs a hand-written radial push-out because he has no physics body at all; here both
            // bodies just collide, and that code is never written.
            var collider = root.AddComponent<CapsuleCollider>();
            collider.direction = 1;
            collider.height = 4f;
            collider.radius = 1f;
            collider.center = new Vector3(0f, 1f, 0f);

            Save(root, "Buoy", log);
        }

        // ── shared ────────────────────────────────────────────────────────────────────────────

        private static GameObject NewRoot(
            string name, string modelPath, float scale, float modelYaw,
            System.Text.StringBuilder log, out Transform visual)
        {
            visual = null;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                log.AppendLine($"  {name}: {modelPath} is missing - skipped");
                return null;
            }

            var root = new GameObject(name);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "Visual";
            visual = instance.transform;

            // Three rotations, innermost first: stand the Sketchfab export up, turn its three.js
            // front to Unity's +Z, then the config's own yaw on top.
            //
            // <b>ModelFacing was missing until 2026-08-16 and both craft flew tail-first.</b> Every
            // other vehicle builder here composes `RotFromRadians(modelYaw) * ModelFacing`; this one
            // composed `RotFromRadians(modelYaw) * Upright` and the omission is invisible in a
            // bounding box - a Huey the right size and the right way up, with its nose at −Z.
            // Measured in the built prefab: tail rotor at z +5.25 against a cockpit at z −2.77, and
            // the ski's handlebars at −1.10 against its rear bar at +2.11. Both drive along +Z.
            visual.localRotation = Convert.RotFromRadians(modelYaw) * Convert.ModelFacing * Upright;
            visual.localScale = Vector3.one * scale;
            return root;
        }

        private static void Kill(Transform visual, string nodeName, System.Text.StringBuilder log)
        {
            var node = Find(visual, nodeName);
            if (node == null) return;
            Object.DestroyImmediate(node.gameObject);
            log.AppendLine($"  removed '{nodeName}' (not part of the model)");
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name)
                    return t;
            return null;
        }

        private static Bounds? WorldBounds(Transform node)
        {
            var renderers = node.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return null;
            var box = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) box.Encapsulate(renderers[i].bounds);
            return box;
        }

        /// <summary>Renderer bounds in the ROOT's local space, which is what the origin is set from.</summary>
        private static Bounds MeasureLocal(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var box = new Bounds();
            var any = false;
            foreach (var renderer in renderers)
            {
                var world = renderer.bounds;
                var local = new Bounds(
                    root.transform.InverseTransformPoint(world.center),
                    world.size); // axis-aligned already: the root is unrotated at build time
                if (!any) { box = local; any = true; }
                else box.Encapsulate(local);
            }

            return box;
        }

        private static void Save(GameObject root, string name, System.Text.StringBuilder log)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            var path = $"{PrefabFolder}/{name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            log.AppendLine($"  → {path}");
        }
    }
}
