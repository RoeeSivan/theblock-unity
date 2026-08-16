using System;
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
    /// Builds the rideable motorcycle prefab from its imported GLB and <c>config.vehicle.motorcycle</c>.
    ///
    /// Generated rather than hand-assembled, for the reason every other builder here is: two
    /// WheelColliders and a seat dragged into place are invisible in review and quietly wrong after
    /// a re-export. Re-running the menu item rebuilds the prefab in place so its GUID - and the
    /// scene's reference to it - survives.
    ///
    /// The one thing this cannot measure, and <c>CarBuilder</c> can, is the wheels. The Mustang's
    /// rig names its own corners; this GLB is two nodes, <c>Bike</c> and <c>WoltBox</c>, each a
    /// single merged mesh with no wheel to find. So the axles are placed from the body's bounding
    /// box and the radius is a stated constant - see <see cref="WheelRadiusFraction"/>. Nothing
    /// visible depends on getting them exactly right, because there is no wheel mesh to spin either;
    /// what depends on it is where the suspension pushes and how the bike pitches.
    /// </summary>
    public static class MotorcycleBuilder
    {
        private const string ModelFolder = "Assets/Models/Vehicles";
        private const string PrefabFolder = "Assets/Prefabs/Vehicles";
        private const string PrefabName = "Motorcycle";

        // --- physics, all derived by feel for PhysX (port rule 2) ---------------------------------

        /// <summary>Kerb weight in kg, rider included. A loaded delivery scooter, not a superbike.</summary>
        private const float Mass = 190f;

        /// <summary>
        /// Wheel radius as a fraction of the body's total height. The model has no wheel node to
        /// measure, so this is the one number here that is eyeballed rather than derived: a scooter
        /// stands about four and a half wheel-radii tall.
        /// </summary>
        private const float WheelRadiusFraction = 0.22f;

        /// <summary>Total suspension travel in metres. Shorter than the car's - a bike has less.</summary>
        private const float SuspensionDistance = 0.16f;

        /// <summary>Where along that travel the wheel rests. Half, so it can compress and droop equally.</summary>
        private const float SuspensionTarget = 0.5f;

        /// <summary>
        /// Spring rate per wheel, N/m. Sized so the bike rests where <see cref="SuspensionTarget"/>
        /// says: half the weight is 932 N held at 0.08 m of deflection, so 932 / 0.08 ≈ 11,650.
        /// </summary>
        private const float SuspensionSpring = 11650f;

        /// <summary>
        /// Damping, N·s/m. Critical for a 95 kg corner on that spring is 2·sqrt(k·m) ≈ 2,100; this
        /// is about two thirds, which settles in one bounce without the low-speed jitter an
        /// over-damped WheelCollider gets.
        /// </summary>
        private const float SuspensionDamper = 1400f;

        /// <summary>
        /// Chassis capsule width. The measured bounding box is a metre across because it spans the
        /// mirrors and the bars; colliding as a metre-wide brick makes the bike handle like a car in
        /// traffic. This is roughly the width of the bike plus its rider.
        /// </summary>
        private const float ChassisWidth = 0.5f;

        /// <summary>Chassis box floor, metres above the contact patch. Clear of the road so it does
        /// not fight the wheels for ground contact.</summary>
        private const float ChassisGroundGap = 0.3f;

        [MenuItem("The Block/Build Motorcycle", priority = 22)]
        public static void BuildMenu() => Build();

        /// <summary>Builds the motorcycle prefab. Returns the report.</summary>
        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return Fail("MotorcycleBuilder: stop Play mode first.");

            var snapshot = TheBlockConfig.Load(reload: true);
            var spec = snapshot?.Config?.Vehicle?.Motorcycle;
            if (spec == null)
                return Fail("MotorcycleBuilder: config has no vehicle.motorcycle section.");

            var model = LoadModel(spec.ModelUrl);
            if (model == null)
                return Fail($"MotorcycleBuilder: no asset named {FileName(spec.ModelUrl)} under {ModelFolder}.");

            var log = new StringBuilder();
            var root = new GameObject(PrefabName);

            try
            {
                var lean = BuildVisual(model, spec, root.transform, log, out var bounds);
                if (lean == null) return Fail(log.ToString());

                BuildChassis(root, bounds, log);
                var wheels = BuildWheels(root.transform, bounds, log);
                // Under the lean pivot, not under the root: a rider who stays bolt upright while
                // the bike he is sitting on rolls 30° into a corner is worse than no lean at all.
                var seat = BuildRiderAnchor(lean, spec, bounds, log);
                Wire(root, wheels.front, wheels.rear, seat, lean, log);

                var path = SavePrefab(root);
                log.AppendLine($"prefab  {path}");

                var report = $"MotorcycleBuilder\n{log}";
                Debug.Log(report);
                return report;
            }
            catch (Exception e)
            {
                return Fail($"MotorcycleBuilder exception: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // --- model -------------------------------------------------------------------------------

        private static string FileName(string url) =>
            string.IsNullOrEmpty(url) ? null : url.Substring(url.LastIndexOf('/') + 1);

        /// <summary>Resolves a config URL to an asset by exact file name, as CarBuilder does.</summary>
        private static GameObject LoadModel(string url)
        {
            var fileName = FileName(url);
            if (fileName == null) return null;
            var bare = System.IO.Path.GetFileNameWithoutExtension(fileName);

            var path = AssetDatabase
                .FindAssets($"{bare} t:GameObject", new[] { ModelFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p =>
                    string.Equals(System.IO.Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase));

            return path == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>
        /// Parents the model under a lean pivot, turns it to face Unity's +Z, and drops it so the
        /// root's origin lands on the tyre contact patch - the same convention the car uses, so
        /// <c>spawn</c> plus <c>roadSurfaceY</c> means "put the bike here" without a correction.
        ///
        /// The extra node in the middle is the whole reason a bike reads as a bike. The controller
        /// rolls THAT to lean into corners, leaving the Rigidbody upright: rolling the body itself
        /// on two wheels is not a lean, it is a fall.
        ///
        /// <c>groundClearance</c> is honoured as the web build honours it - the model carries a
        /// little geometry below its tyres, and without the lift the visible wheels sink into the
        /// asphalt by exactly that much.
        /// </summary>
        private static Transform BuildVisual(
            GameObject model, TheBlockConfig.MotorcycleSpec spec, Transform parent,
            StringBuilder log, out Bounds bounds)
        {
            bounds = default;

            var lean = new GameObject("Lean");
            lean.transform.SetParent(parent, false);

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, lean.transform);
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
            var lift = spec.GroundClearance - world.min.y;
            visual.transform.localPosition = new Vector3(0f, lift, 0f);
            world.center += new Vector3(0f, lift, 0f);
            bounds = world;

            log.AppendLine($"body    {Fmt(bounds.size)} m, centre {Fmt(bounds.center)}");
            log.AppendLine($"yaw     modelYaw {spec.ModelYaw:0.###} rad + 180° facing = " +
                           $"{visual.transform.localEulerAngles.y:0.#}°");
            log.AppendLine($"lift    visual raised {lift:0.###} m - root origin is the contact patch, " +
                           $"wheels held {spec.GroundClearance:0.###} m clear per config groundClearance");
            log.AppendLine("lean    pivot 'Lean' between root and Visual; the controller rolls it, " +
                           "the Rigidbody stays upright");

            return lean.transform;
        }

        // --- wheels ------------------------------------------------------------------------------

        /// <summary>
        /// Two WheelColliders, one radius in from each end of the body.
        ///
        /// That placement is not a guess about this model so much as a fact about bikes: the
        /// bounding box of a motorcycle is bounded front and back by its own tyres, so an axle sits
        /// exactly one radius inside each edge. Both are centred on the body's X - a bike's wheels
        /// are in line, which is the entire reason it needs a stabiliser.
        /// </summary>
        private static (WheelCollider front, WheelCollider rear) BuildWheels(
            Transform root, Bounds bounds, StringBuilder log)
        {
            var group = new GameObject("Wheels");
            group.transform.SetParent(root, false);

            var radius = bounds.size.y * WheelRadiusFraction;
            var rest = SuspensionDistance * SuspensionTarget;
            var axleZ = Mathf.Max(0.15f, bounds.size.z * 0.5f - radius);

            log.AppendLine($"wheels  radius {radius:0.###} m ({WheelRadiusFraction:P0} of body height, " +
                           "stated not measured - this GLB has no wheel node), axles at " +
                           $"z ±{axleZ:0.###} ({axleZ * 2f:0.###} m wheelbase), raised {rest:0.###} m " +
                           "for suspension rest");

            var front = MakeWheel(group.transform, "Front", new Vector3(0f, radius + rest, axleZ), radius);
            var rear = MakeWheel(group.transform, "Rear", new Vector3(0f, radius + rest, -axleZ), radius);
            return (front, rear);
        }

        private static WheelCollider MakeWheel(Transform parent, string name, Vector3 local, float radius)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;

            var wheel = go.AddComponent<WheelCollider>();
            wheel.radius = radius;
            wheel.mass = 12f;
            wheel.wheelDampingRate = 0.25f;
            wheel.suspensionDistance = SuspensionDistance;
            wheel.forceAppPointDistance = 0f;

            wheel.suspensionSpring = new JointSpring
            {
                spring = SuspensionSpring,
                damper = SuspensionDamper,
                targetPosition = SuspensionTarget,
            };

            // Grip curves. Grippier than the car's on purpose: a bike has half the contact patch and
            // an arcade one that lets go in a Florentin side street is miserable rather than exciting.
            wheel.forwardFriction = new WheelFrictionCurve
            {
                extremumSlip = 0.4f, extremumValue = 1f,
                asymptoteSlip = 0.8f, asymptoteValue = 0.55f,
                stiffness = 2.4f,
            };
            wheel.sidewaysFriction = new WheelFrictionCurve
            {
                extremumSlip = 0.22f, extremumValue = 1f,
                asymptoteSlip = 0.5f, asymptoteValue = 0.75f,
                stiffness = 2.6f,
            };

            return wheel;
        }

        // --- chassis -----------------------------------------------------------------------------

        /// <summary>
        /// Rigidbody plus one narrow box for the body.
        ///
        /// The box floor is held clear of the road for the same reason the car's is: a collider that
        /// reaches the tarmac fights the wheels for ground contact and the vehicle judders. Its WIDTH
        /// is overridden rather than measured - see <see cref="ChassisWidth"/>. Angular damping is
        /// higher than the car's so the upright stabiliser has something to settle against instead of
        /// ringing.
        /// </summary>
        private static void BuildChassis(GameObject root, Bounds bounds, StringBuilder log)
        {
            var body = root.AddComponent<Rigidbody>();
            body.mass = Mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearDamping = 0f;
            // Low, and deliberately: this damps YAW as well as roll, and yaw is how the bike turns.
            // Roll is already heavily over-damped by the stabiliser's look-ahead term, so it needs
            // no help here - help here would only make the steering feel like treacle.
            body.angularDamping = 0.15f;

            var top = bounds.max.y;
            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(ChassisWidth, Mathf.Max(0.1f, top - ChassisGroundGap), bounds.size.z);
            box.center = new Vector3(0f, (top + ChassisGroundGap) * 0.5f, bounds.center.z);

            log.AppendLine($"chassis box {Fmt(box.size)} centred {Fmt(box.center)}, floor at " +
                           $"{ChassisGroundGap:0.##} m - width forced to {ChassisWidth:0.##} m, the " +
                           $"measured {bounds.size.x:0.##} m spans the mirrors and bars");
            log.AppendLine($"mass    {Mass:0} kg, angular damping {body.angularDamping:0.##}");
        }

        // --- cabin -------------------------------------------------------------------------------

        /// <summary>
        /// Places the seat.
        ///
        /// Unlike the car's driver anchor, this one IS a seat - <c>config.vehicle.motorcycle.rider</c>
        /// was tuned against a rider frozen on frame 0 of a sitting clip, not against an entry
        /// animation's origin. The two corrections are the same ones <c>CarBuilder</c> applies:
        ///
        ///  - The web build recentres the bike inside a holder, so its numbers are measured from the
        ///    BODY CENTRE. This prefab's origin is the tyre contact patch, so the centre is added back.
        ///  - <see cref="Convert.ModelOffset"/> for the offset and <see cref="Convert.RotFromRadians"/>
        ///    for the yaw. The web build adds a π to that yaw to turn a Mixamo body that faces +Z in
        ///    a -Z-forward engine; Unity's forward IS +Z, so the π is exactly what is not needed here.
        /// </summary>
        private static Transform BuildRiderAnchor(
            Transform root, TheBlockConfig.MotorcycleSpec spec, Bounds bounds, StringBuilder log)
        {
            var rider = spec.Rider;
            if (rider?.Seat == null)
            {
                log.AppendLine("seat    none in config.vehicle.motorcycle.rider - no rider anchor built");
                return null;
            }

            var anchor = new GameObject("RiderAnchor");
            anchor.transform.SetParent(root, false);
            anchor.transform.localPosition = bounds.center + Convert.ModelOffset(rider.Seat.Raw);
            anchor.transform.localRotation = Convert.RotFromRadians(rider.Yaw);
            anchor.transform.localScale = Vector3.one * (rider.Scale <= 0f ? 1f : rider.Scale);

            log.AppendLine(
                $"seat    config ({rider.Seat.X:0.##}, {rider.Seat.Y:0.##}, {rider.Seat.Z:0.##}) about " +
                $"the body centre → local {Fmt(anchor.transform.localPosition)}");
            log.AppendLine(
                $"        yaw {rider.Yaw:0.###} rad → {anchor.transform.localEulerAngles.y:0.#}°, " +
                $"scale {anchor.transform.localScale.x:0.##}");
            return anchor.transform;
        }

        private static void Wire(
            GameObject root, WheelCollider front, WheelCollider rear, Transform seat, Transform lean,
            StringBuilder log)
        {
            var bike = root.AddComponent<MotorcycleController>();
            var serialized = new SerializedObject(bike);
            serialized.FindProperty("frontWheel").objectReferenceValue = front;
            serialized.FindProperty("rearWheel").objectReferenceValue = rear;
            serialized.FindProperty("riderAnchor").objectReferenceValue = seat;
            serialized.FindProperty("leanPivot").objectReferenceValue = lean;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine("wired   MotorcycleController to both wheels, the lean pivot" +
                           $"{(seat == null ? "" : " and the seat")}");
        }

        // --- output ------------------------------------------------------------------------------

        private static string SavePrefab(GameObject root)
        {
            EnsureFolder(PrefabFolder);
            var path = $"{PrefabFolder}/{PrefabName}.prefab";
            // SaveAsPrefabAsset over an existing path keeps the GUID, so the scene's reference
            // survives a rebuild.
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
