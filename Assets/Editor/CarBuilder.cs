using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TheBlock.Core;
using TheBlock.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Convert = TheBlock.Core.Convert;
using Debug = UnityEngine.Debug;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Builds the drivable car prefabs from their imported GLBs and their <c>config.vehicle.cars</c>
    /// entries - <b>The Block → Build Drivable Cars</b>.
    ///
    /// Generated rather than hand-assembled, for the reason <c>WorldBuilder</c> and
    /// <c>JoeAnimatorBuilder</c> are: four WheelColliders dragged into place by hand are invisible in
    /// review, impossible to reproduce, and quietly wrong the moment the model is re-exported. Here
    /// every number is either measured off the mesh or written down below with its reasoning, and
    /// re-running the menu item rebuilds each prefab in place so its GUID - and every scene reference
    /// to it - survives.
    ///
    /// <b>U17b generalised this past the Mustang, and the one thing that does not generalise is the
    /// wheels.</b> The Mustang is the only car in the game that kept its rig. The other three went
    /// through the web build's <c>blender/merge-car-meshes.py</c>, which welds every wheel into the
    /// body to cut three.js draw calls - so their GLBs contain no wheel node at all, and
    /// <see cref="FindWheelBones"/> has nothing to find. Their axles are therefore STATED from the
    /// measured body box, the way <c>MotorcycleBuilder</c> states the bike's, and nothing visible
    /// depends on getting them exactly right because there is no wheel mesh to spin either. The
    /// shipped web build never rotated a wheel on any car; the Mustang here is the only one that
    /// ever has.
    ///
    /// Nothing here edits the .glb. The import stays exactly as downloaded; the corrections live in
    /// the prefab, the same rule <c>WorldBuilder.AssetAliases</c> follows.
    /// </summary>
    public static class CarBuilder
    {
        private const string ModelFolder = "Assets/Models/Vehicles";
        private const string PrefabFolder = "Assets/Prefabs/Vehicles";

        /// <summary>
        /// Generated materials, one folder written and swept by this builder alone.
        ///
        /// Everything a car renders is cloned in here rather than used straight out of the .glb, for
        /// the reason <see cref="VehicleMaterials"/> spells out: a prefab instantiated at runtime is
        /// never seen by <c>WorldBuilder</c>'s compressed-texture pass, so it would quietly pull the
        /// raw RGB24 copies of U15's textures back into memory. That matters more since U17b than it
        /// did at U8, because these prefabs are now spawned mid-game by every carjack.
        /// </summary>
        private const string MaterialFolder = "Assets/Materials/Vehicles";

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
        /// this is a little over half of it - settled in one bounce without the jitter an
        /// over-damped WheelCollider gets at low speed.
        /// </summary>
        private const float SuspensionDamper = 3500f;

        /// <summary>Chassis box floor, metres above the contact patch. Below the axle, clear of the road.</summary>
        private const float ChassisGroundGap = 0.25f;

        // --- stated wheel geometry, for the three models with no wheel node -----------------------

        /// <summary>
        /// Wheel radius as a fraction of the body's total height. A saloon stands about four and a
        /// bit wheel-radii tall, and the Mustang - the one car that CAN be measured - is the check:
        /// its build log prints the measured radius beside what this rule would have produced.
        /// </summary>
        private const float WheelRadiusFraction = 0.24f;

        /// <summary>
        /// Wheelbase as a fraction of the body's total length. 0.6 is the ordinary road-car figure
        /// (a 4.7 m saloon on a 2.8 m wheelbase), so the axles sit at ±0.3 of the length.
        /// </summary>
        private const float WheelbaseFraction = 0.6f;

        /// <summary>
        /// Track as a fraction of the body's total width. Under 1 because the widest thing on a car
        /// is its mirrors, not its tyres, and both of these models carry theirs in the same mesh.
        /// </summary>
        private const float TrackFraction = 0.8f;

        [MenuItem("The Block/Build Drivable Cars", priority = 20)]
        public static void BuildAllMenu() => BuildAll();

        /// <summary>
        /// Builds one prefab per distinct model in <c>config.vehicle.cars</c>.
        ///
        /// Per MODEL, not per entry: the config lists sixteen cars, but twelve of them are colour
        /// variants that reuse one of four GLBs with a different <c>bodyColor</c> and a different
        /// parking space. A variant needs no prefab of its own - the paint is a material swap on the
        /// one already built - so the rule is "the first entry for each modelUrl", which produces
        /// exactly the four base cars without either the count or their names being typed in here.
        /// </summary>
        public static string BuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return Fail("CarBuilder: stop Play mode first.");

            var snapshot = TheBlockConfig.Load(reload: true);
            var cars = snapshot?.Config?.Vehicle?.Cars;
            if (cars == null || cars.Count == 0)
                return Fail("CarBuilder: config.vehicle.cars is empty.");

            var bases = cars
                .Where(c => !string.IsNullOrEmpty(c.ModelUrl))
                .GroupBy(c => c.ModelUrl, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var written = new HashSet<string>(StringComparer.Ordinal);
            var log = new StringBuilder();
            var built = new List<GameObject>();

            foreach (var spec in bases)
            {
                var prefab = BuildOne(spec, snapshot.Config.Vehicle.Driver, MaterialFolder, Quaternion.identity,written, log);
                if (prefab != null) built.Add(prefab);
                log.AppendLine();
            }

            VehicleMaterials.Sweep(MaterialFolder, written, log);
            WireSpawner(built, log);
            AssetDatabase.SaveAssets();

            var report = $"CarBuilder - {built.Count} of {bases.Count} drivable car(s)\n{log}";
            Debug.Log(report);
            return report;
        }

        /// <summary>Builds one car by its <c>config.vehicle.cars[].name</c>. Returns the report.</summary>
        public static string Build(string carName)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return Fail("CarBuilder: stop Play mode first.");

            var snapshot = TheBlockConfig.Load(reload: true);
            var spec = snapshot?.Config?.Vehicle?.Cars?
                .FirstOrDefault(c => string.Equals(c.Name, carName, StringComparison.OrdinalIgnoreCase));
            if (spec == null)
                return Fail($"CarBuilder: no car named '{carName}' in config.vehicle.cars.");

            var log = new StringBuilder();
            // No sweep on a single-car build: the folder holds every car's materials, and deleting
            // "everything this run did not write" would take the other three cars' with it.
            var prefab = BuildOne(spec, snapshot.Config.Vehicle.Driver, MaterialFolder, Quaternion.identity,new HashSet<string>(), log);
            AssetDatabase.SaveAssets();

            var report = $"CarBuilder - {carName}\n{log}";
            if (prefab == null) return Fail(report);

            Debug.Log(report);
            return report;
        }

        /// <summary>
        /// Builds one car from a spec that need not come from <c>config.vehicle.cars</c>.
        ///
        /// U19's police cruiser has no config entry - the web build hard-codes its model URL in
        /// <c>police.ts</c> and never lists it as a car - so its spec is stated port-side and handed
        /// in here. It takes its own <paramref name="materialFolder"/> for a reason that would
        /// otherwise bite silently: <see cref="VehicleMaterials.Sweep"/> deletes everything in a
        /// folder the run did not write, so police materials living beside the four drivable cars'
        /// would be deleted by the next <b>Build Drivable Cars</b>.
        /// </summary>
        internal static GameObject BuildFromSpec(
            TheBlockConfig.CarSpec spec, TheBlockConfig.DriverSpec driver, string materialFolder,
            Quaternion preRotation, HashSet<string> written, StringBuilder log) =>
            BuildOne(spec, driver, materialFolder, preRotation, written, log);

        private static GameObject BuildOne(
            TheBlockConfig.CarSpec spec, TheBlockConfig.DriverSpec driver, string materialFolder,
            Quaternion preRotation, HashSet<string> written, StringBuilder log)
        {
            log.AppendLine($"── {spec.Name}");

            var model = LoadModel(spec.ModelUrl);
            if (model == null)
            {
                log.AppendLine($"        MISSING - no asset for {spec.ModelUrl} under {ModelFolder}");
                return null;
            }

            var root = new GameObject(spec.Name);

            try
            {
                var visual = BuildVisual(model, spec, root.transform, preRotation, log, out var bounds, out var wheelBones);
                if (visual == null) return null;

                BuildChassis(root, bounds, log);
                var wheels = BuildWheels(root.transform, wheelBones, bounds, log);
                var door = BuildDoor(root, visual.transform, spec, log);
                var anchor = BuildDriverAnchor(root.transform, spec, bounds, driver, log);
                var paint = BuildMaterials(visual, spec, materialFolder, written, log);
                Wire(root, wheels, anchor, door, paint, log);
                BuildDamage(root, visual.transform, wheels, door, log);

                var path = SavePrefab(root, spec.Name);
                log.AppendLine($"        prefab  {path}");
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
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
        /// Parents the model under the root, turns it to face Unity's +Z, and moves it so the root's
        /// origin is the tyre contact patch in Y and the body centre in XZ.
        ///
        /// <b>Both halves of that origin earn their place.</b> The Y drop is what makes the config
        /// spawns usable as written: <c>spawn</c> carries no Y and <c>roadSurfaceY</c> is the road, so
        /// an origin at the contact patch means "put the car here" is literally the road height,
        /// rather than burying or floating it by however far the artist's pivot sits under the tyres.
        /// The XZ recentre is what makes a car SWAPPABLE, which is new at U17b: a hijacked street car
        /// and a promoted lot car are placed at the pose of the thing they replace, and both of those
        /// are built with a body-centred origin (<c>TrafficCarBuilder</c>) - so a drivable prefab
        /// pivoted somewhere off-centre would land half a car off the spot it was taken from.
        /// </summary>
        private static GameObject BuildVisual(
            GameObject model, TheBlockConfig.CarSpec spec, Transform parent, Quaternion preRotation,
            StringBuilder log, out Bounds bounds, out Dictionary<Corner, Transform> wheelBones)
        {
            bounds = default;
            wheelBones = null;

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            visual.name = "Visual";
            visual.transform.localPosition = Vector3.zero;
            // The yaw pair is about Y so those two commute (see Convert.ModelFacing); the
            // pre-rotation is NOT about Y and is applied first, in the model's own frame, because it
            // exists to correct axes the importer left standing on end before any facing convention
            // is meaningful. Identity for every car whose GLB imports Y-up.
            visual.transform.localRotation =
                Convert.RotFromRadians(spec.ModelYaw) * Convert.ModelFacing * preRotation;
            visual.transform.localScale = Vector3.one * (spec.ModelScale <= 0f ? 1f : spec.ModelScale);

            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                log.AppendLine("        model has no renderers");
                return null;
            }

            // Measured with the model turned but the root at the origin, so world == root-local, and
            // the box is the one the car will really occupy. Measuring before the rotation gives the
            // pre-turn box; measuring a car already yawed into a lane gives the bounding box of a
            // bounding box, which is the trap U13 hit on the lot.
            var world = renderers[0].bounds;
            foreach (var r in renderers) world.Encapsulate(r.bounds);

            var shift = new Vector3(world.center.x, world.min.y, world.center.z);
            visual.transform.localPosition = -shift;
            world.center -= shift;
            bounds = world;

            log.AppendLine($"        body    {Fmt(bounds.size)} m");
            log.AppendLine($"        yaw     modelYaw {spec.ModelYaw:0.###} rad + 180° facing = " +
                           $"{visual.transform.localEulerAngles.y:0.#}°, scale {visual.transform.localScale.x:0.##}" +
                           (preRotation == Quaternion.identity
                               ? string.Empty
                               : $", pre-rotated {Fmt(preRotation.eulerAngles)}"));
            log.AppendLine($"        origin  visual moved {Fmt(-shift)} - contact patch in Y, body centre in XZ");

            wheelBones = FindWheelBones(visual.transform, log);
            return visual;
        }

        // --- wheels ------------------------------------------------------------------------------

        public enum Corner { FrontLeft, FrontRight, RearLeft, RearRight }

        /// <summary>
        /// Finds the four wheel bones and sorts them into corners BY POSITION, not by name - or
        /// returns null, which is the ordinary case and not an error.
        ///
        /// The names cannot be trusted on their own: glTFast negates X on import, so the bone the
        /// rig calls <c>wheel_Front_L_0</c> arrives on Unity's +X side and only becomes left again
        /// after <see cref="Convert.ModelFacing"/>. Reading the sign off the transform sidesteps that
        /// entirely - and works on the next car, whose rigger may have used other names.
        ///
        /// The steering knuckles (<c>wheel_dir_*</c>) sit at the same spot as the front wheels and
        /// would tie for the corner, so they are filtered out by name first.
        ///
        /// <b>Null means "this model has no wheels to find", not "something went wrong".</b> Three of
        /// the four cars were flattened into a single merged mesh by the web build's Blender pass;
        /// the caller states their axles instead. Returning null for a model that ought to have wheels
        /// would be a real fault, which is why the count that was found is always logged.
        /// </summary>
        private static Dictionary<Corner, Transform> FindWheelBones(Transform visual, StringBuilder log)
        {
            var candidates = visual.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.IndexOf("wheel", StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(t => t.name.IndexOf("dir", StringComparison.OrdinalIgnoreCase) < 0)
                .Where(t => !IsRigControl(t.name))
                .ToList();

            // A wheel's own mesh child is not a second wheel. The CrownVic names its geometry
            // `CrownVic.Wheel.Ft.L_0` under a transform of the same name, and two transforms at one
            // position tie for the corner and throw the whole map away - which is how a model WITH
            // four perfectly good wheel nodes ended up on stated axles.
            candidates.RemoveAll(t => candidates.Any(other => other != t && t.IsChildOf(other)));

            if (candidates.Count != 4)
            {
                log.AppendLine($"        wheels  no rig - {candidates.Count} wheel node(s) in this model" +
                               (candidates.Count == 0
                                   ? " (merged into the body by the web build's Blender pass)"
                                   : $": {string.Join(", ", candidates.Select(c => c.name))}"));
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
                    log.AppendLine($"        wheels  two bones landed on {corner}: " +
                                   $"{map[corner].name} and {bone.name} - falling back to stated axles");
                    return null;
                }

                map[corner] = bone;
            }

            foreach (var pair in map.OrderBy(p => p.Key))
                log.AppendLine($"        wheel   {pair.Key,-10} {pair.Value.name,-18} at {Fmt(pair.Value.position)}");

            return map;
        }

        /// <summary>
        /// Rig plumbing that carries "wheel" in its name without being one.
        ///
        /// Blender car rigs ship a deform bone, a mechanism bone and a control shape per wheel, plus
        /// a brake, a damper and a rotation driver - the CrownVic has fifty such nodes against four
        /// real wheels. The prefixes are Rigify's own convention (<c>DEF-</c> deform, <c>MCH-</c>
        /// mechanism, <c>SHP-</c> shape) and the <c>_Car_Rig</c> suffix is the rig's namespace, so
        /// this reads as "not geometry" rather than as a list of names from one model.
        ///
        /// <c>_0</c> is deliberately NOT filtered: glTF numbers primitives that way and the
        /// Mustang's real wheels are called <c>wheel_Front_L_0</c>.
        /// </summary>
        private static bool IsRigControl(string name) =>
            name.StartsWith("DEF-", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("MCH-", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("SHP-", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_Car_Rig", StringComparison.OrdinalIgnoreCase) ||
            name.IndexOf("brake", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("damper", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("rotation", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Creates a WheelCollider per corner, from the rig where there is one and from the body box
        /// where there is not.
        ///
        /// <b>Rigged (the Mustang).</b> Radius is measured, not typed: the wheel centre's height above
        /// the contact patch IS the rolling radius once the root origin sits on that patch. The
        /// stated rule is printed beside it, which is the only cross-check either has - one measured
        /// car keeping the three stated ones honest.
        ///
        /// <b>Stated (Tesla, Audi, Avenger).</b> Radius, wheelbase and track come off the measured
        /// body box through the three fractions at the top of this file. They are ordinary road-car
        /// proportions, and nothing visible hangs on them: with the wheels welded into the body there
        /// is no tyre to line up with, so all these numbers decide is where the suspension pushes.
        ///
        /// Either way the collider is raised by the resting suspension deflection, so the car settles
        /// back to the height the model was authored at instead of sagging by that much on frame one.
        /// </summary>
        private static Dictionary<Corner, WheelCollider> BuildWheels(
            Transform root, Dictionary<Corner, Transform> bones, Bounds bounds, StringBuilder log)
        {
            var group = new GameObject("Wheels");
            group.transform.SetParent(root, false);

            var rest = SuspensionDistance * SuspensionTarget;
            float statedRadius = bounds.size.y * WheelRadiusFraction;
            float statedAxleZ = bounds.size.z * WheelbaseFraction * 0.5f;
            float statedAxleX = bounds.size.x * TrackFraction * 0.5f;

            var places = new Dictionary<Corner, Vector3>();
            float radius;

            if (bones != null)
            {
                radius = bones.Values.Average(b => b.position.y);
                foreach (var pair in bones) places[pair.Key] = pair.Value.position;

                float measuredAxleZ = bones.Values.Average(b => Mathf.Abs(b.position.z));
                float measuredAxleX = bones.Values.Average(b => Mathf.Abs(b.position.x));
                log.AppendLine(
                    $"        radius  {radius:0.###} m measured off the rig " +
                    $"(the stated rule would say {statedRadius:0.###})");
                log.AppendLine(
                    $"        axles   measured ±{measuredAxleX:0.###} x ±{measuredAxleZ:0.###} m " +
                    $"(stated ±{statedAxleX:0.###} x ±{statedAxleZ:0.###})");
            }
            else
            {
                radius = statedRadius;
                places[Corner.FrontLeft] = new Vector3(-statedAxleX, radius, statedAxleZ);
                places[Corner.FrontRight] = new Vector3(statedAxleX, radius, statedAxleZ);
                places[Corner.RearLeft] = new Vector3(-statedAxleX, radius, -statedAxleZ);
                places[Corner.RearRight] = new Vector3(statedAxleX, radius, -statedAxleZ);

                log.AppendLine(
                    $"        radius  {radius:0.###} m STATED ({WheelRadiusFraction:P0} of body height)");
                log.AppendLine(
                    $"        axles   STATED ±{statedAxleX:0.###} x ±{statedAxleZ:0.###} m " +
                    $"({WheelbaseFraction:P0} wheelbase, {TrackFraction:P0} track)");
            }

            log.AppendLine($"        rest    colliders raised {rest:0.###} m for suspension rest");

            var wheels = new Dictionary<Corner, WheelCollider>();
            foreach (var pair in places.OrderBy(p => p.Key))
            {
                var go = new GameObject(pair.Key.ToString());
                go.transform.SetParent(group.transform, false);
                go.transform.localPosition = pair.Value + Vector3.up * rest;

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

                // Only where there is a bone to drive. CarWheel with nothing bound disables itself
                // and says so, once per wheel per car - twelve console warnings that mean nothing.
                if (bones != null && bones.TryGetValue(pair.Key, out var bone))
                    go.AddComponent<CarWheel>().BindBone(bone);

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
        /// and other cars. The web build arrived at the same shape from the Rapier side - which makes
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

            log.AppendLine($"        chassis box {Fmt(box.size)} centred {Fmt(box.center)}, " +
                           $"floor at {ChassisGroundGap:0.##} m, mass {Mass:0} kg");
        }

        // --- cabin -------------------------------------------------------------------------------

        private static readonly Dictionary<string, Vector3> DoorAxes = new()
        {
            { "x", Vector3.right },
            { "y", Vector3.up },
            { "z", Vector3.forward },
        };

        /// <summary>
        /// Adds the driver's door, hinged on the armature joint the config names.
        ///
        /// The axis is the only part that needs thought. It is written in the JOINT's own local
        /// frame, which glTFast has already re-expressed on import, so it goes through
        /// <see cref="Convert.ModelAxis"/> - and the ANGLE is left exactly as authored, because
        /// reading an axis-angle out of a converted quaternion moves the sign onto the axis and not
        /// onto the angle. Getting that backwards swings the door into the cabin instead of out.
        /// </summary>
        private static CarDoor BuildDoor(
            GameObject root, Transform visual, TheBlockConfig.CarSpec spec, StringBuilder log)
        {
            if (spec.Door == null || string.IsNullOrEmpty(spec.Door.JointName))
            {
                log.AppendLine("        door    none in config - this car has no opening door");
                return null;
            }

            var joint = visual.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == spec.Door.JointName);
            if (joint == null)
            {
                log.AppendLine($"        door    SKIPPED - no joint named '{spec.Door.JointName}' in the rig");
                return null;
            }

            var key = (spec.Door.Axis ?? "z").ToLowerInvariant();
            if (!DoorAxes.TryGetValue(key, out var axis))
            {
                log.AppendLine($"        door    SKIPPED - axis '{spec.Door.Axis}' is not x, y or z");
                return null;
            }

            var unityAxis = Convert.ModelAxis(axis);
            var degrees = spec.Door.OpenAngle * Mathf.Rad2Deg;

            var component = root.AddComponent<CarDoor>();
            component.Configure(joint, unityAxis, degrees, spec.Door.LerpRate);

            log.AppendLine($"        door    {spec.Door.JointName} hinged on local {Fmt(unityAxis)} " +
                           $"at {degrees:0.#}° (config axis '{key}', {spec.Door.OpenAngle:0.###} rad)");
            return component;
        }

        /// <summary>
        /// Places the driver's entry anchor - where the seated-driver animation STARTS.
        ///
        /// Not the seat cushion. <c>config.vehicle.driver.seats</c> holds the transform of the entry
        /// clip's own origin, which is the driver standing beside the door at road level; the clip's
        /// baked hip travel carries him from there into the car. That is why the Mustang's x is
        /// 2.31 m, well outside a 2 m-wide body.
        ///
        /// Two corrections turn that block into a Unity local transform:
        ///
        ///  - The web build measures those offsets from the BODY CENTRE. This prefab's origin is the
        ///    body centre in XZ but the contact patch in Y, so the measured centre is added back -
        ///    which is now purely the half-height, the XZ terms having become zero when
        ///    <see cref="BuildVisual"/> recentred the model.
        ///  - <see cref="Convert.ModelOffset"/> for the offset and <see cref="Convert.RotFromRadians"/>
        ///    for the yaw. The web build adds a π to that yaw to turn a Mixamo body that faces +Z in
        ///    a -Z-forward engine; Unity's forward IS +Z, so that π is exactly what is not needed
        ///    here and a plain negation is the whole conversion.
        ///
        /// All four cars have a seat block, so all four get the walk-up-and-sit-down entry. The
        /// Tesla's is its own re-fit - a 1.44 m cabin against the Mustang's 1.61 - and the config
        /// says so at the entry.
        /// </summary>
        private static Transform BuildDriverAnchor(
            Transform root, TheBlockConfig.CarSpec spec, Bounds bounds,
            TheBlockConfig.DriverSpec driver, StringBuilder log)
        {
            TheBlockConfig.SeatSpec seat = null;
            var seats = driver?.Seats;
            if (seats != null && !string.IsNullOrEmpty(spec.ModelUrl))
                seats.TryGetValue(spec.ModelUrl, out seat);

            if (seat == null)
            {
                log.AppendLine($"        seat    none for {spec.ModelUrl} - this car uses the quick enter");
                return null;
            }

            var anchor = new GameObject("DriverAnchor");
            anchor.transform.SetParent(root, false);
            anchor.transform.localPosition = bounds.center + Convert.ModelOffset(seat.Raw);
            anchor.transform.localRotation = Convert.RotFromRadians(seat.Yaw);
            anchor.transform.localScale = Vector3.one * (seat.Scale <= 0f ? 1f : seat.Scale);

            var side = anchor.transform.localPosition.x < 0f ? "left" : "right";
            log.AppendLine(
                $"        seat    config ({seat.X:0.##}, {seat.Y:0.##}, {seat.Z:0.##}) about the body centre " +
                $"→ local {Fmt(anchor.transform.localPosition)}, the car's {side}, " +
                $"yaw {anchor.transform.localEulerAngles.y:0.#}°, scale {anchor.transform.localScale.x:0.##}");
            return anchor.transform;
        }

        // --- materials ---------------------------------------------------------------------------

        /// <summary>
        /// Clones every material out of the .glb, rebinds its textures onto U15's compressed copies,
        /// and paints the body.
        ///
        /// <b>The paint is the part that was quietly broken.</b> This used to write <c>_BaseColor</c>
        /// and <c>_Color</c> only - neither of which exists on the shader glTFast actually imports -
        /// so nothing was ever written and the Mustang has been wearing its model's native dark green
        /// since U8 instead of the config's <c>0xb5232a</c> red. <see cref="VehicleMaterials"/> now
        /// owns that choice, gamma and all, and returns which property it used so the log can show it.
        ///
        /// The returned component is what lets a hijacked or promoted car keep the colour of the car
        /// it replaced: the paint slot is a material assignment, so handing it the street palette's
        /// or the lot's material is exact, costs nothing, and reuses an asset whose textures another
        /// builder has already compressed.
        /// </summary>
        private static CarPaint BuildMaterials(
            GameObject visual, TheBlockConfig.CarSpec spec, string materialFolder,
            HashSet<string> written, StringBuilder log)
        {
            var clones = new Dictionary<Material, Material>();
            var slotOwners = new List<Renderer>();
            var slotIndices = new List<int>();
            Material paintSource = null;
            int rebound = 0, misses = 0;

            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (source == null) continue;

                    if (!clones.TryGetValue(source, out var clone))
                    {
                        clone = VehicleMaterials.Clone(source, materialFolder, spec.Name, written);
                        rebound += VehicleMaterials.RebindCompressed(clone, ref misses);
                        clones[source] = clone;
                    }

                    materials[i] = clone;
                    changed = true;

                    if (!VehicleMaterials.IsPaint(source)) continue;

                    paintSource ??= clone;
                    slotOwners.Add(renderer);
                    slotIndices.Add(i);
                }

                if (changed) renderer.sharedMaterials = materials;
            }

            log.AppendLine(
                $"        mats    {clones.Count} cloned, {rebound} texture slot(s) onto compressed copies" +
                (misses > 0 ? $", {misses} with no twin" : string.Empty));

            if (paintSource == null)
            {
                log.AppendLine(
                    $"        paint   none - no material named " +
                    $"{string.Join("/", VehicleMaterials.PaintMaterialNames)}; this model keeps its own");
                return null;
            }

            var component = visual.transform.parent.gameObject.AddComponent<CarPaint>();
            component.Configure(slotOwners.ToArray(), slotIndices.ToArray());

            if (!spec.BodyColor.HasValue)
            {
                log.AppendLine($"        paint   config sets no bodyColor - keeping the model's own, " +
                               $"on {slotOwners.Count} slot(s)");
                return component;
            }

            int hex = spec.BodyColor.Value;
            var painted = VehicleMaterials.PaintVariant(paintSource, materialFolder, spec.Name, hex, written);
            component.Apply(painted);

            var property = painted.HasProperty("baseColorFactor") ? "baseColorFactor" : "_BaseColor";
            log.AppendLine($"        paint   #{hex:x6} via {property} on {slotOwners.Count} slot(s)");
            return component;
        }

        private static void Wire(
            GameObject root, Dictionary<Corner, WheelCollider> wheels, Transform driverAnchor,
            CarDoor door, CarPaint paint, StringBuilder log)
        {
            var car = root.AddComponent<CarController>();
            var serialized = new SerializedObject(car);
            serialized.FindProperty("frontLeft").objectReferenceValue = wheels[Corner.FrontLeft];
            serialized.FindProperty("frontRight").objectReferenceValue = wheels[Corner.FrontRight];
            serialized.FindProperty("rearLeft").objectReferenceValue = wheels[Corner.RearLeft];
            serialized.FindProperty("rearRight").objectReferenceValue = wheels[Corner.RearRight];
            serialized.FindProperty("driverAnchor").objectReferenceValue = driverAnchor;
            serialized.FindProperty("door").objectReferenceValue = door;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine($"        wired   CarController to all four wheels" +
                           $"{(driverAnchor == null ? "" : ", the driver anchor")}" +
                           $"{(door == null ? "" : ", the door")}" +
                           $"{(paint == null ? "" : ", the paint")}");
        }

        // --- U35b: damage ------------------------------------------------------------------------

        /// <summary>
        /// Nodes that may be knocked off a car, by model.
        ///
        /// <b>Every name here is one that already exists in the .glb</b> - this table was read out of
        /// the files, not chosen. The three lot cars went through the web build's
        /// <c>merge-car-meshes.py</c>, which welded the shell into one mesh and left the door, the
        /// mirror and a window as their own nodes; the CrownVic keeps its light bar separate. Matching
        /// is case-insensitive and by exact name, so a model that does not have one simply gets fewer
        /// parts rather than a warning.
        ///
        /// <b>The Mustang is absent on purpose.</b> Its eighteen nodes are one per MATERIAL, each
        /// spanning the whole car - there is no bumper to detach without a Blender split, and that
        /// split was weighed and declined for this unit (the paint is rebound by the material NAME
        /// <c>CarPrimaryColor</c>, so a re-export that renames anything breaks the hero car in every
        /// screenshot). The build log says "parts none" for it rather than staying quiet.
        /// </summary>
        private static readonly string[] DetachableNodeNames =
        {
            "Door_R",           // audi, avenger - also CarDoor's own hinge
            "door_dside_f",     // tesla, likewise
            "Mirror_R",         // audi
            "mirror",           // avenger
            "Window_FR",        // audi, avenger
            "window_lf",        // tesla
            "Roof light bar_0", // the CrownVic cruiser
        };

        /// <summary>
        /// Wires U35b onto the prefab: which meshes dent, which nodes come off.
        ///
        /// <b>Only the two things that cannot be discovered at runtime are wired here.</b> The
        /// component itself is added by <c>CarController.Bind</c> at runtime, the same arrangement
        /// <c>CrashSensor</c> uses - a component dropped on a prefab is regenerated away by the next
        /// run of this menu item, which is the likeliest story of how the sensor was lost for fourteen
        /// units. A serialized ARRAY of scene references cannot be found at runtime though, so those
        /// two live on the prefab and the components are added here to hold them.
        ///
        /// <b>Wheels are excluded from the dent list</b> - a dented spinning wheel reads as a bug, not
        /// as damage - and so is anything under a wheel node, which on the CrownVic is four 16.5k-vert
        /// tyres and four brake discs.
        ///
        /// The readability count is logged because it is the single assumption layer ① rests on: a
        /// mesh whose Read/Write is off cannot be deformed, and the failure is otherwise silent.
        /// </summary>
        private static void BuildDamage(
            GameObject root, Transform visual, Dictionary<Corner, WheelCollider> wheels, CarDoor door,
            StringBuilder log)
        {
            var wheelRoots = wheels == null
                ? new List<Transform>()
                : wheels.Values.Where(w => w != null).Select(w => w.transform).ToList();

            var parts = new List<Transform>();
            foreach (var candidate in visual.GetComponentsInChildren<Transform>(true))
                if (DetachableNodeNames.Any(n => string.Equals(n, candidate.name, StringComparison.OrdinalIgnoreCase)))
                    parts.Add(candidate);

            var panels = new List<MeshFilter>();
            var skins = new List<SkinnedMeshRenderer>();
            int readable = 0, verts = 0, total = 0;

            // Both kinds, because the cars are both kinds: the three lot cars are plain MeshFilter
            // shells, and the Mustang is EIGHTEEN SkinnedMeshRenderers on one sixteen-bone rig - the
            // rig CarDoor and CarWheel already drive. A MeshFilter-only sweep found zero dentable
            // meshes on the hero car and said so in this log, which is how that was caught.
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                var skin = renderer as SkinnedMeshRenderer;
                var mesh = skin != null ? skin.sharedMesh : filter != null ? filter.sharedMesh : null;
                if (mesh == null) continue;

                // A wheel, or a brake disc under one. Both spin; neither dents. (On the Mustang the
                // wheels are bound to bones inside a shared mesh, so there is nothing to exclude -
                // and nothing to worry about either: a dent is a 0.9 m sphere at the contact point,
                // so a wheel only moves if you hit the wheel.)
                if (IsRigControl(renderer.name) ||
                    renderer.name.IndexOf("wheel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    wheelRoots.Any(w => renderer.transform == w || renderer.transform.IsChildOf(w))) continue;

                // A part that has come off carries its own dents nowhere - it is a separate rigid
                // body by then - so it is left out of the panel list rather than being deformed on a
                // car it is no longer attached to.
                if (parts.Any(p => renderer.transform == p || renderer.transform.IsChildOf(p))) continue;

                if (skin != null) skins.Add(skin);
                else if (filter != null) panels.Add(filter);
                else continue;

                total++;
                verts += mesh.vertexCount;
                if (mesh.isReadable) readable++;
            }

            var deform = root.AddComponent<DeformableBody>();
            deform.Configure(panels.ToArray(), skins.ToArray());

            var detachable = root.AddComponent<DetachableParts>();
            detachable.Configure(parts.ToArray(), door);

            var damage = root.AddComponent<VehicleDamage>();
            damage.Configure(deform, detachable);

            log.AppendLine(
                $"        damage  {total} dentable mesh(es) ({panels.Count} static, {skins.Count} skinned), " +
                $"{verts:n0} verts, {readable}/{total} readable");
            log.AppendLine(parts.Count == 0
                ? "        parts   none - this model has no node that is a PART (its .glb groups by material)"
                : $"        parts   {string.Join(", ", parts.Select(p => p.name))}");

            if (readable < total)
                log.AppendLine("        ⚠ some meshes are not readable - those panels cannot dent");
        }

        // --- output ------------------------------------------------------------------------------

        private static string SavePrefab(GameObject root, string carName)
        {
            VehicleMaterials.EnsureFolder(PrefabFolder);
            var path = $"{PrefabFolder}/{carName}.prefab";
            // SaveAsPrefabAsset over an existing path keeps the GUID, so scene references survive
            // a rebuild - the same reason JoeAnimatorBuilder writes its controller in place.
            PrefabUtility.SaveAsPrefabAsset(root, path);
            return path;
        }

        /// <summary>
        /// Hands the built prefabs to the open scene's <c>CarSpawner</c>.
        ///
        /// Done here rather than left to a drag, for the reason <c>WorldBuilder</c> wires the traffic
        /// pool itself: the builder is the only thing that knows the full set it just produced, and a
        /// prefab that exists but is in nobody's list is a car that silently never appears. It is also
        /// what U17b's carjack and lot promotion look the prefabs up in, so a missing entry is not
        /// just a missing parked car - it is a stolen car that cannot be spawned.
        ///
        /// The scene must be MARKED DIRTY by hand: assigning through SerializedObject leaves the scene
        /// looking clean, and Save then writes nothing and reports success (memory:
        /// <c>editor-created-objects-need-markscenedirty</c>).
        /// </summary>
        private static void WireSpawner(List<GameObject> prefabs, StringBuilder log)
        {
            if (prefabs.Count == 0) return;

            var spawners = UnityEngine.Object.FindObjectsByType<CarSpawner>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (spawners.Length == 0)
            {
                log.AppendLine("spawner    no CarSpawner in the open scene - nothing wired");
                return;
            }

            foreach (var spawner in spawners)
            {
                var serialized = new SerializedObject(spawner);
                var list = serialized.FindProperty("carPrefabs");
                list.arraySize = prefabs.Count;
                for (int i = 0; i < prefabs.Count; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = prefabs[i];
                serialized.ApplyModifiedPropertiesWithoutUndo();

                EditorUtility.SetDirty(spawner);
                EditorSceneManager.MarkSceneDirty(spawner.gameObject.scene);
                log.AppendLine(
                    $"spawner    {spawner.name} ← {string.Join(", ", prefabs.Select(p => p.name))} " +
                    "(scene marked dirty - save it)");
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
