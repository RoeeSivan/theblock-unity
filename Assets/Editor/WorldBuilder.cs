using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using TheBlock.Core;
using TheBlock.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Convert = TheBlock.Core.Convert;
using Debug = UnityEngine.Debug;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Builds the world into the open scene from <c>theblock-config.json</c>.
    ///
    /// Re-runnable by design: it destroys its own root and rebuilds from scratch every time, so the
    /// scene is a pure function of the config plus the assets on disk. Nothing under the generated
    /// root should ever be hand-edited - the next build eats it.
    ///
    /// This is the ONLY place config positions become Unity transforms, and it does that through
    /// <see cref="Convert"/>. No sign flip is written inline here either.
    ///
    /// Not undoable. Re-running is the undo.
    /// </summary>
    public static partial class WorldBuilder
    {
        private const string RootName = "World";
        private const string FacadeMaterialPath = "Assets/Materials/City/Facade.mat";
        private const string GroundMaterialPath = "Assets/Materials/World/Ground.mat";

        /// <summary>Generated alpha-clipped stand-ins for imported blended materials. Rebuilt every build.</summary>
        private const string CutoutMaterialFolder = "Assets/Materials/City/Cutout";

        /// <summary>Generated meshes with hidden submeshes removed. Rebuilt every build, gitignored.</summary>
        private const string GeneratedMeshFolder = "Assets/Meshes/Generated";

        /// <summary>
        /// Imported materials whose alpha is a CUTOUT MASK - leaf cards, railings, grating - and not
        /// real translucency.
        ///
        /// glTF has one `alphaMode: BLEND` for both, and these assets use it for both, so glTFast
        /// imports the tree canopies as transparent surfaces with ZWrite off: hundreds of unsorted
        /// leaf quads blending over each other and over the buildings behind them, which reads as
        /// white shards rather than trees. Alpha CLIPPING is what these actually want - hard edges,
        /// depth written, sorted with the opaque geometry, and a shadow that has leaf-shaped holes
        /// in it, which the blended version cannot produce at all.
        ///
        /// This list is a port-side judgement, not config: the web build has one material path and
        /// never had to make the distinction. Anything NOT listed here stays blended, and every
        /// blended material left in the world is named in the build report so a wrong call is
        /// visible rather than silent.
        /// </summary>
        private static readonly string[] CutoutMaterialPatterns =
        {
            "foliage", "tree", "leaf", "leaves", "vegetation", "plant", "bush", "shrub",
            "street_assets", "firescape",
        };

        /// <summary>Scene roots the hand-built phase left behind, replaced by the generated world.</summary>
        private static readonly string[] LegacyRootPrefixes = { "District_", "Place_" };

        /// <summary>
        /// A per-asset correction: either a stand-in for a model the web build has and this project
        /// does not, or an orientation/height fix for the real asset, plus whatever it takes to make
        /// it sit where the web build's copy would have.
        ///
        /// The correction lives here rather than being baked into the asset on disk: the file stays
        /// exactly as it was downloaded, and the fix stays visible and re-runnable. Every use is
        /// called out in the build report, so a substitute never quietly passes for the real thing.
        /// </summary>
        private sealed class Substitute
        {
            /// <summary>
            /// File name as it exists under <c>Assets/Models</c>, when this entry swaps the asset out.
            /// <c>null</c> means the config's own file is used and only the corrections below apply -
            /// which is also what tells the build report and <c>hideNodes</c> apart: a stand-in has
            /// somebody else's node names, the real asset has the ones the config was written against.
            /// </summary>
            public string File;

            /// <summary>Rotation applied in the model's OWN frame, before the config's yaw.</summary>
            public Vector3 ExtraEuler;

            /// <summary>Lift above the config position, to rest the model on the ground it actually meets.</summary>
            public float ExtraY;

            public string Note;
        }

        /// <summary>Keyed by the file name <c>config.ts</c> asks for.</summary>
        private static readonly Dictionary<string, Substitute> AssetAliases = new()
        {
            // Pizza Lila is hand-modelled and has no distributable source; this is a low-poly
            // substitute the user sourced. config's scale 1.6 and yaw 0 were tuned for the original.
            ["pizza-lila.glb"] = new Substitute
            {
                File = "low_poly_pizza_restaurant.glb",
                // The GLB's node chain leaves the model lying on its back - its local Y and Z end up
                // swapped, so the lamp post runs along Z instead of standing up. Rx(-90) rights it and
                // lands the base exactly on the model's own zero.
                ExtraEuler = new Vector3(-90f, 0f, 0f),
                // config says y=0, which was correct for the original. The pavement here is at 0.15.
                ExtraY = 0.15f,
                Note = "lies on its back out of the box; righted and lifted onto the pavement",
            },

            // The real Paz station, not a stand-in - File is null. The Sketchfab export wraps the
            // model in Sketchfab_model (Rx-90) → GLTF_SceneRootNode (Rx+90), a pair that cancels in
            // three.js but not through glTFast: the imported model arrives with its local Y and Z
            // swapped, so it stands 24.5 m "tall" (that is its 61 m depth) and sinks 5.4 m below the
            // road. Rx(-90) puts the axes back: 13.1 m tall, 24.5 m deep, base on y 0.
            ["gas-station.glb"] = new Substitute
            {
                ExtraEuler = new Vector3(-90f, 0f, 0f),
                Note = "Sketchfab export imports with Y and Z swapped; righted",
            },
        };

        [MenuItem("The Block/Build World", priority = 0)]
        public static void BuildWorldMenu() => Build(new Options());

        [MenuItem("The Block/Build World (no colliders)", priority = 1)]
        public static void BuildWorldNoCollidersMenu() => Build(new Options { Colliders = false });

        /// <summary>
        /// The full build INCLUDING the NavMesh bake. Off the default item because the bake is the
        /// one step that freezes the editor for a minute or more with no progress bar, and a world
        /// rebuild should not cost that every time a material is touched. The scene keeps its last
        /// baked NavMesh across ordinary builds - see <see cref="Options.Navigation"/>.
        /// </summary>
        [MenuItem("The Block/Build World + NavMesh (slow)", priority = 2)]
        public static void BuildWorldWithNavigationMenu() => Build(new Options { Navigation = true });

        /// <summary>
        /// Just the fence, on the world already in the scene.
        ///
        /// A hole in the world edge is a one-object fix, and <c>Build World</c> is a minutes-long
        /// pass that re-imports every district and rewrites every material binding to correct it -
        /// which is a large diff and a large risk for four box colliders. This finds the existing
        /// <c>Ground</c>, throws its <c>World Edges</c> away and asks
        /// <see cref="BuildWorldEdges"/> for a new one from the same config numbers the full build
        /// would use, so the two cannot drift.
        /// </summary>
        [MenuItem("The Block/Rebuild World Bounds", priority = 3)]
        public static void RebuildWorldBoundsMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("WorldBuilder: stop Play mode first - edits made in play are discarded.");
                return;
            }

            var snapshot = TheBlockConfig.Load(reload: true);
            var ground = snapshot.Config?.Ground;
            if (ground == null || ground.Size <= 0f)
            {
                Debug.LogError("WorldBuilder: no `ground` section in the config - nothing to fence.");
                return;
            }

            var plane = GameObject.Find("Ground");
            if (plane == null)
            {
                Debug.LogError("WorldBuilder: no `Ground` object in the open scene. Build World first.");
                return;
            }

            var report = new Report();
            BuildGroundFloor(plane.transform, ground, snapshot.Config.Sea, report);
            BuildWorldEdgesPass(plane.transform.parent, snapshot.Config, report);

            // A scene edited from a menu item is not dirty by itself, so Save would write nothing and
            // report success (memory: editor-created-objects-need-markscenedirty).
            EditorSceneManager.MarkSceneDirty(plane.scene);
            foreach (var line in report.Warnings) Debug.LogWarning("WorldBuilder: " + line);

            foreach (var line in report.Placed) Debug.Log("WorldBuilder: " + line);
            Debug.Log($"WorldBuilder: world floor + edges rebuilt ({report.Colliders} colliders). SAVE THE SCENE.");
        }

        public class Options
        {
            /// <summary>
            /// Street fog + shadow distance. Off is what the world looked like before the fog was
            /// ported: a hard arc slicing the skyline wherever the far plane fell.
            /// </summary>
            public bool Atmosphere = true;

            public bool Ground = true;
            public bool Roads = true;
            public bool Sea = true;
            public bool Districts = true;
            public bool Places = true;
            public bool Colliders = true;

            /// <summary>
            /// U16's pedestrian world: the carriageway carve, the zebra crossings and the NavMesh
            /// bake. OFF BY DEFAULT - the bake is main-thread, minute-plus, and shows no progress,
            /// which twice read as a hang and got the editor force-quit. When off, the previously
            /// baked <c>NavMesh.asset</c> is kept, and the crossings and carve volumes from the last
            /// navigation build are left standing under the new root, so Play still has a crowd.
            /// Run <c>Build World + NavMesh</c> after anything that moves a district or a street.
            /// </summary>
            public bool Navigation = false;

            /// <summary>
            /// U17's traffic: the light poles and the car pool. ON by default and cheap - nothing in
            /// it bakes. Turning it off still builds the street graph, because U16's crossings key
            /// off that graph's node and edge numbering and the navigation pass shares it.
            /// </summary>
            public bool Traffic = true;

            /// <summary>
            /// U19's routing graph - the stitched view of the street graph the police plan over.
            /// ON by default and cheap: it copies no geometry, bakes no mesh and casts no ray, and
            /// it is the only pass that reports whether the network is connected at all.
            /// </summary>
            public bool Police = true;

            /// <summary>
            /// Rebind materials onto <see cref="TextureCompressor"/>'s compressed textures (U15).
            /// Off is the "what did this actually buy" comparison, not a mode anything should ship in.
            /// </summary>
            public bool CompressedTextures = true;
        }

        public static string Build(Options options)
        {
            options ??= new Options();

            // Play mode throws the scene away on exit, so a build there is silently discarded.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                const string message = "WorldBuilder: stop Play mode first - a world built in play is discarded on exit.";
                Debug.LogError(message);
                return message;
            }

            var snapshot = TheBlockConfig.Load(reload: true);
            if (snapshot == null) return "Build aborted - config could not be loaded.";

            var stopwatch = Stopwatch.StartNew();
            var report = new Report();
            var scene = EditorSceneManager.GetActiveScene();

            var root = ResetRoot(scene, report, keepNavigation: !options.Navigation, out var keptNavigation);
            root.SourceSha256 = snapshot.SourceSha256;
            ResetTexturePass();

            if (options.Atmosphere) BuildAtmosphere(snapshot.Config, report);
            if (options.Ground) BuildGround(root.transform, snapshot.Config.Ground, snapshot.Config.Sea, report);
            if (options.Roads) BuildRoads(root.transform, snapshot.Config.Roads, report);
            if (options.Sea)
                BuildSea(root.transform, snapshot.Config.Sea, snapshot.Config.Ground, options, report);

            Transform districts = null;
            if (options.Districts)
            {
                districts = NewGroup("Districts", root.transform);
                var city = snapshot.Config.City;
                if (city != null)
                {
                    BuildDistrict(
                        districts, city.Url, city.Name, city.Position, city.Scale,
                        hideMaterials: null, noCollidePatterns: city.NoCollidePatterns,
                        facadeMaterials: city.FacadeMaterials, options, report);
                }

                foreach (var district in snapshot.Config.Districts ?? new List<TheBlockConfig.DistrictSpec>())
                {
                    BuildDistrict(
                        districts, district.Url, district.Name, district.Position, district.Scale,
                        district.HideMaterials, district.NoCollidePatterns,
                        facadeMaterials: null, options, report);
                }
            }

            if (options.Places)
            {
                var places = NewGroup("Places", root.transform);
                var store = BuildPlace(places, snapshot.Config.SevenEleven, "Seven Eleven", options, report);
                BuildPlace(places, snapshot.Config.PizzaPlace, "Pizza Place", options, report);
                var gas = BuildPlace(places, snapshot.Config.GasStation, "Gas Station", options, report);
                BuildPlace(places, snapshot.Config.PoliceStation, "Police Station", options, report);
                BuildInterior(places, snapshot.Config.Interior, snapshot.Config.Player, options, report);
                BuildStore(store, snapshot.Config.SevenEleven, report);
                EnsureEconomy(root.transform, report, snapshot.PowerUps);
                BuildGasStation(gas, snapshot, report);
                EnsureFuel(root.transform, report);
                BuildLotCars(places, snapshot.Config.LotCars, options, report);
                BuildAutoShop(places, options, report); // U35g - Unity-authored, see WorldBuilder.AutoShop.cs
            }

            // Last, and it has to be: the traffic pass raycasts for the street surface under every
            // sample, the carve does the same under every crossing, and the bake reads the colliders
            // every pass above put there. The graph is derived ONCE, by the traffic pass, and handed
            // to navigation - two derivations of the same node numbering is how the crossings and
            // the lights would end up disagreeing about which junction is which.
            var trafficGraph = BuildTraffic(root.transform, snapshot.Config, options, report);

            // After traffic, because it reads the network traffic just baked - and it is the pass
            // that answers whether that network is one city or five islands.
            BuildPolice(root.transform, snapshot.Config, options, report);

            // U35h - after traffic, because the bins stand beside the poles it just placed and the
            // cones and benches read the network it just baked; and inside the build at all so the
            // prop prefabs' compressed-material clones are re-emitted before SweepGenerated runs.
            BuildProps(root.transform, snapshot.Config, snapshot.Npc, options, report);

            // Dead last of the geometry passes, because it MEASURES the floor rather than assuming
            // it: the fence follows the seaward edge of whatever is actually solid, and a district
            // built after it would be a stretch of road with a wall across the middle of it.
            if (options.Ground) BuildWorldEdgesPass(root.transform, snapshot.Config, report);

            if (options.Navigation)
                BuildNavigation(root.transform, districts, snapshot.Config, trafficGraph, options, report);
            else ReattachNavigation(root, districts, keptNavigation, report);

            SweepGenerated(report);

            stopwatch.Stop();
            var text = report.Compose(snapshot, stopwatch.Elapsed, options);
            root.LastBuildReport = text;

            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root.gameObject;

            if (report.Missing.Count > 0) Debug.LogWarning(text, root);
            else Debug.Log(text, root);
            return text;
        }

        // --- structure -------------------------------------------------------------------------

        /// <summary>
        /// Clears the previous build (and the hand-placed roots it supersedes), returns a fresh root.
        ///
        /// When <paramref name="keepNavigation"/> is set, the previous build's navigation objects -
        /// the Crossings group, the carve volumes and the NavMeshSurface - are lifted out before
        /// the root is destroyed and handed back for re-parenting, so a build that skips the bake
        /// does not also silently delete the last one. The bake is expensive; nothing else about
        /// the world build should be allowed to throw it away as a side effect.
        /// </summary>
        private static WorldRoot ResetRoot(
            UnityEngine.SceneManagement.Scene scene, Report report, bool keepNavigation,
            out List<GameObject> keptNavigation)
        {
            keptNavigation = new List<GameObject>();

            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.GetComponent<WorldRoot>() != null)
                {
                    if (keepNavigation) keptNavigation.AddRange(DetachNavigation(go.transform));
                    UnityEngine.Object.DestroyImmediate(go);
                    continue;
                }

                if (LegacyRootPrefixes.Any(prefix => go.name.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    report.RemovedLegacy.Add(go.name);
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            var root = new GameObject(RootName);
            return root.AddComponent<WorldRoot>();
        }

        /// <summary>
        /// Lifts the navigation objects out of an old root so they survive its destruction. The
        /// NavMeshSurface lives on the Districts group itself, so that whole group is what has to
        /// come out - carve volumes are its children and the surface only collects from children.
        /// </summary>
        private static List<GameObject> DetachNavigation(Transform oldRoot)
        {
            var kept = new List<GameObject>();
            foreach (var name in new[] { "Crossings" })
            {
                var t = oldRoot.Find(name);
                if (t == null) continue;
                t.SetParent(null, worldPositionStays: true);
                kept.Add(t.gameObject);
            }

            var districts = oldRoot.Find("Districts");
            if (districts != null && districts.TryGetComponent<Unity.AI.Navigation.NavMeshSurface>(out var surface))
            {
                // Only the surface's DATA and the carve volumes are worth keeping - the districts
                // themselves are about to be rebuilt. Move the surface onto a carrier along with the
                // Navigation group, and let the new Districts group re-adopt both.
                var carrier = new GameObject("__NavigationCarrier");
                var nav = districts.Find("Navigation");
                if (nav != null) nav.SetParent(carrier.transform, worldPositionStays: true);
                var newSurface = carrier.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
                UnityEditorInternal.ComponentUtility.CopyComponent(surface);
                UnityEditorInternal.ComponentUtility.PasteComponentValues(newSurface);
                kept.Add(carrier);
            }

            return kept;
        }

        /// <summary>Puts kept navigation back under the new root, mirroring where the bake put it.</summary>
        private static void ReattachNavigation(WorldRoot root, Transform districts, List<GameObject> kept, Report report)
        {
            int reattached = 0;
            foreach (var go in kept)
            {
                if (go == null) continue;

                if (go.name == "__NavigationCarrier")
                {
                    if (districts == null) { UnityEngine.Object.DestroyImmediate(go); continue; }
                    var nav = go.transform.Find("Navigation");
                    if (nav != null) { nav.SetParent(districts, worldPositionStays: true); reattached++; }
                    if (go.TryGetComponent<Unity.AI.Navigation.NavMeshSurface>(out var carried))
                    {
                        if (!districts.TryGetComponent<Unity.AI.Navigation.NavMeshSurface>(out var surface))
                            surface = districts.gameObject.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
                        UnityEditorInternal.ComponentUtility.CopyComponent(carried);
                        UnityEditorInternal.ComponentUtility.PasteComponentValues(surface);

                        // ⚠ The paste does NOT reliably bring `navMeshData` across, and when it does
                        // not the failure is silent and total: the surface looks correctly
                        // configured, the asset is still on disk, and `NavMesh.CalculateTriangulation`
                        // returns zero vertices - so every agent fails to spawn and the city is empty
                        // of people with nothing in the console. Found at U17's play-test by counting
                        // pedestrians. The asset the bake wrote is the authority, so bind it from
                        // disk rather than trusting a component copy to carry a reference.
                        var baked = AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(
                            $"{GeneratedNavigationFolder}/NavMesh.asset");
                        if (baked != null) surface.navMeshData = baked;
                        else report.Warnings.Add(
                            "navigation: no baked NavMesh.asset to re-attach - run Build World + NavMesh (slow)");

                        // Pasting values onto a live component does not re-run OnEnable, and
                        // OnEnable is where the surface registers its data with the NavMesh. Bounce
                        // it, or the asset is referenced and nothing is walkable until a reload.
                        surface.enabled = false;
                        surface.enabled = true;
                    }
                    UnityEngine.Object.DestroyImmediate(go);
                    continue;
                }

                go.transform.SetParent(root.transform, worldPositionStays: true);
                reattached++;
            }

            if (reattached > 0)
                report.Notes.Add($"navigation: kept the previous bake ({reattached} group(s) re-attached) - run Build World + NavMesh to redo it");
        }

        private static Transform NewGroup(string name, Transform parent)
        {
            var group = new GameObject(name);
            group.transform.SetParent(parent, worldPositionStays: false);
            return group.transform;
        }

        // --- ground ----------------------------------------------------------------------------

        /// <summary>
        /// The flat plate under everything, from <c>config.ground</c>.
        ///
        /// Belongs to U12 with the roads and the sea, and is built here early because U8's car needs
        /// somewhere to land: the districts are islands, and a car that leaves one had nothing under
        /// it at all - it drove off the edge and fell forever, which is not a thing a play-test can
        /// survive.
        ///
        /// It sits at <c>y = -0.05</c>, marginally below every district, so wherever the two overlap
        /// the district's own ground is what a wheel or a ground probe finds.
        ///
        /// U12 TRIMMED ITS COLLIDER AT THE SHORE, which the web build does too and for a reason that
        /// only shows up once there is a sea: the plate is solid at y -0.05 and the beach ramps down
        /// to -3, so an untrimmed plate holds the player up on an invisible sheet a few centimetres
        /// under the water and the whole beach becomes scenery. The visual plane keeps its full size
        /// - the water is opaque and drawn above it - but the solid part stops at the waterline, and
        /// past that the beach mesh is the only floor. Everything seaward of the shore is Unity
        /// <c>+x</c>; see <see cref="SeaGeometry"/> for why.
        /// </summary>
        private static void BuildGround(
            Transform parent, TheBlockConfig.GroundSpec ground, TheBlockConfig.SeaSpec sea, Report report)
        {
            if (ground == null)
            {
                report.Warnings.Add("ground skipped - config has no `ground` section");
                return;
            }

            var plane = new GameObject("Ground");
            plane.transform.SetParent(parent, worldPositionStays: false);
            plane.transform.position = new Vector3(0f, ground.Y, 0f);

            var mesh = BuildGroundMesh(ground, sea, report);
            plane.AddComponent<MeshFilter>().sharedMesh = mesh;
            var groundRenderer = plane.AddComponent<MeshRenderer>();

            var material = LoadOrCreateGroundMaterial(ground, report);
            if (material != null) groundRenderer.sharedMaterial = material;

            BuildGroundFloor(plane.transform, ground, sea, report);
            float far = -ground.Size * 0.5f;
            float near = sea != null ? SeaGeometry.ShoreX(sea) : ground.Size * 0.5f;

            // THE FENCE IS NOT BUILT HERE ANY MORE. It measures the floor it is fencing, and at this
            // point in the build the only floor that exists is this plate - no districts, no seabed.
            // `Build` runs it last; see BuildWorldEdgesPass.

            SetDistrictStaticFlags(plane);
            report.Placed.Add(
                $"Ground {ground.Size:0} x {ground.Size:0} m @ y {ground.Y:0.##}, " +
                $"solid over Unity x [{far:0}, {near:0}] (trimmed at the shore)");
        }

        /// <summary>
        /// A fence around the solid plate - four invisible boxes on its rim, so nothing can drive off
        /// the edge of the world.
        ///
        /// <b>Reported by the user, 2026-08-16:</b> *"יש אזור ליד הים שאם נוסעים בו אז פשוט נופלים
        /// והמכונית ממשיכה ליפול."* The measurement behind it: the plate's collider is solid over
        /// Unity x [−700, +430] and the full 1400 m of z, while the <b>shore wall covers only 600 m
        /// of that z</b> - it is `sea.Length`, because it was built to hold the waterline, and the
        /// water is 600 m of coast inside a 1400 m world. North and south of the water there is a
        /// 400 m stretch of plate edge at x = 430 with nothing on it at all, and past it no collider
        /// of any kind: the water surface has none. A car that reaches it falls forever.
        ///
        /// <b>Its seaward side moved out to the plate's rim on 2026-08-18</b>, with the sea wall, and
        /// for the reason written at the call site: on the waterline it was the wall a car rammed
        /// when the player aimed at the sea, and `CrashSensor` + `CrimeWatch` turned that into a
        /// wanted level. The three landward sides are unchanged.
        ///
        /// <b>⚠ 2026-08-18, and this is the SECOND report of the same fall.</b> Moving the seaward
        /// side out to the plate's rim (above) was right for the 600 m the sea covers and wrong for
        /// everything else, because <b>the FLOOR out there is the SEABED, and the seabed is the sea's
        /// own z-strip and nothing wider</b>. Probed: at z 0 and z 54 there is beach down to y −3 all
        /// the way out to x 690; at z 60, z −560 and z −650 the plate answers at x 420 and there is
        /// NOTHING from x 435 to the fence at x 698. That is a 270 m wide strip, ~640 m of it north
        /// of the water and ~150 m south, with a fence 270 m too far out to catch anything. The user
        /// drove up the beach past the end of the water and fell at (502.8, 65.4) - eleven metres
        /// north of where the seabed stops.
        ///
        /// So the seaward side is now THREE walls, not one: at the plate's rim across the sea's
        /// z-strip, where the seabed really does carry on, and <b>back at the waterline north and
        /// south of it</b>, where it does not. Nothing is rendered out there either, so the fence and
        /// the visible world end in the same place.
        ///
        /// <b>Ignore Raycast</b>, for the reason <see cref="BuildShoreWall"/> gives in full: a wall
        /// is not a floor, and a downward probe started inside one reads its top as ground and lifts
        /// the caller into the air.
        /// </summary>
        /// <param name="near">The plate's rim - where the seabed and the sea wall end.</param>
        /// <param name="waterline">Where the plate's own COLLIDER stops. The only floor past it is
        /// the seabed, and the seabed exists only across the sea's z-strip.</param>
        private static void BuildWorldEdges(
            Transform parent, float far, float near, float waterline, float size,
            TheBlockConfig.SeaSpec sea, Report report)
        {
            // The shore wall's own dimensions where the config has them, so the two agree by
            // construction rather than by two numbers that happen to match today.
            float height = sea?.Wall != null && sea.Wall.Height > 0f ? sea.Wall.Height : 8f;
            float thickness = sea?.Wall != null && sea.Wall.Thickness > 0f ? sea.Wall.Thickness : 3f;

            var edges = new GameObject("World Edges") { layer = LayerMask.NameToLayer("Ignore Raycast") };
            edges.transform.SetParent(parent, worldPositionStays: false);
            edges.transform.localPosition = Vector3.zero;

            float half = size * 0.5f;
            float midX = (near + far) * 0.5f;
            float spanX = Mathf.Abs(near - far);

            int segments = BuildSeawardFence(waterline, near, half, height, thickness, Edge);

            Edge("Landward", new Vector3(far, height * 0.5f, 0f), new Vector3(thickness, height, size));
            Edge("North", new Vector3(midX, height * 0.5f, half), new Vector3(spanX, height, thickness));
            Edge("South", new Vector3(midX, height * 0.5f, -half), new Vector3(spanX, height, thickness));

            report.Placed.Add(
                $"World edges: {height:0.#} m walls, landward x {far:0}, north/south z ±{half:0}, and " +
                $"a seaward fence of {segments} segment(s) swept along the real edge of the floor " +
                $"between x {waterline:0} and {near:0} (Ignore Raycast, colliders only)");

            void Edge(string name, Vector3 centre, Vector3 sizeOf)
            {
                var wall = new GameObject(name) { layer = edges.layer };
                wall.transform.SetParent(edges.transform, worldPositionStays: false);
                wall.transform.localPosition = centre;
                wall.AddComponent<BoxCollider>().size = sizeOf;
                report.Colliders++;
            }
        }

        // --- the plate's floor ----------------------------------------------------------------------

        /// <summary>
        /// The plate's solid part: <b>every square metre of green is walkable</b>, and only the
        /// water's own footprint is not.
        ///
        /// <b>The trim at the waterline was right for one reason and wrong everywhere else.</b> U12
        /// stopped the collider at the shore because the plate is solid at y −0.05 while the beach
        /// ramps down to −3: an untrimmed plate holds the player on an invisible sheet a few
        /// centimetres under the water and the whole beach becomes scenery. True - but only across
        /// the <b>sea's own 600 m of z</b>. North and south of it there is no water and no beach; the
        /// plate is plain green ground, drawn out to the rim, with nothing under it. The user, on the
        /// third report of the same fall: *"השטח הזה אמור להיות שטח ירוק רגיל… כל שטח ירוק הוא שטח
        /// שאפשר ללכת עליו"*.
        ///
        /// So the floor is three boxes: everything landward of the waterline, plus the seaward
        /// remainder north and south of the water. The hole is filled rather than fenced, which is
        /// also what stops the fence from having to cut across District 7's road.
        /// </summary>
        private static void BuildGroundFloor(
            Transform plane, TheBlockConfig.GroundSpec ground, TheBlockConfig.SeaSpec sea, Report report)
        {
            for (int i = plane.childCount - 1; i >= 0; i--)
            {
                var child = plane.GetChild(i);
                if (child.name.StartsWith("Ground Floor", StringComparison.Ordinal))
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            }

            float half = ground.Size * 0.5f;
            float far = -half;                                                    // landward, Unity -x
            float waterline = sea != null ? SeaGeometry.ShoreX(sea) : half;

            Slab("Ground Floor", (waterline + far) * 0.5f, 0f, Mathf.Abs(waterline - far), ground.Size);

            if (sea == null || sea.Length <= 0f) return;

            float stripMin = sea.CenterZ - sea.Length * 0.5f;
            float stripMax = sea.CenterZ + sea.Length * 0.5f;
            float seawardWidth = half - waterline;
            if (seawardWidth <= 0.1f) return;

            float north = half - stripMax;
            if (north > 0.1f)
                Slab("Ground Floor Seaward North", waterline + seawardWidth * 0.5f,
                    stripMax + north * 0.5f, seawardWidth, north);

            float south = stripMin + half;
            if (south > 0.1f)
                Slab("Ground Floor Seaward South", waterline + seawardWidth * 0.5f,
                    -half + south * 0.5f, seawardWidth, south);

            // The plate is a generated mesh at scale 1 (it was a 10 m Plane primitive scaled up), so
            // every box is already in metres and needs no scale correction.
            void Slab(string name, float centreX, float centreZ, float width, float depth)
            {
                var floor = new GameObject(name);
                floor.transform.SetParent(plane, worldPositionStays: false);
                floor.transform.localPosition = Vector3.zero;
                var box = floor.AddComponent<BoxCollider>();
                box.size = new Vector3(width, 0.2f, depth);
                box.center = new Vector3(centreX, -0.1f, centreZ);   // top face at the plate's y
                report.Colliders++;
            }
        }

        // --- the seaward fence, measured ------------------------------------------------------------

        /// <summary>Z resolution of the sweep. Fine enough to follow a street, coarse enough to be fast.</summary>
        private const float FenceSliceZ = 5f;

        /// <summary>X resolution of the edge search within one slice.</summary>
        private const float FenceStepX = 2f;

        /// <summary>Metres of clearance left between the last solid ground and the wall.</summary>
        private const float FenceMargin = 2.5f;

        /// <summary>
        /// The seaward wall, placed where the FLOOR ACTUALLY ENDS at each z rather than at one x.
        ///
        /// <b>Third report of the same fall, and the second wrong answer.</b> A single wall at the
        /// plate's rim (x 700) left ~640 m of open void north of the water; moving it back to the
        /// waterline (x 429) sealed that and <b>cut District 7's upper road in half</b> - the user:
        /// *"we should have the ability to drive through this whole road, the one that is
        /// perpendicular to the sea"*. Both are the same mistake: the seaward edge of the solid world
        /// is not a straight line. It is the plate to x 430 in most places, the seabed out to the rim
        /// across the sea's 600 m, and District 7's own mesh out past x 500 where that road runs.
        ///
        /// So it is measured. One downward probe per <see cref="FenceStepX"/> metres, walking seaward
        /// from inside the plate until the floor runs out, once per <see cref="FenceSliceZ"/> metre
        /// slice; the wall goes <see cref="FenceMargin"/> m past the last solid sample and adjacent
        /// slices that agree are merged into one box. ~24k raycasts, a second or two, and it cannot
        /// disagree with the world because it is reading the world.
        ///
        /// <b>Two consecutive misses end a slice, not one.</b> A 2 m seam between a district and the
        /// plate is a crack a fence should span, not stop at - stopping there would wall off the road
        /// behind it, which is exactly the bug this replaces.
        /// </summary>
        /// <returns>How many boxes it took.</returns>
        private static int BuildSeawardFence(
            float waterline, float rim, float half, float height, float thickness,
            Action<string, Vector3, Vector3> edge)
        {
            // Seated below the seabed: a wall whose foot is at y 0 leaves a 3 m gap under it out
            // where the beach has dropped to -3, and a sinking car slides straight out through it.
            const float Foot = -4f;
            float tall = height + Mathf.Abs(Foot);
            float centreY = Foot + tall * 0.5f;

            int slices = Mathf.Max(1, Mathf.CeilToInt(half * 2f / FenceSliceZ));
            var wallX = new float[slices];

            for (int i = 0; i < slices; i++)
            {
                float z = -half + (i + 0.5f) * FenceSliceZ;
                float solid = SeawardSolidEdge(z, waterline - 10f, rim);

                // Quantised, so a road's ragged mesh edge does not become fifty one-slice boxes.
                float x = Mathf.Clamp(solid + FenceMargin, waterline, rim);
                wallX[i] = Mathf.Ceil(x / FenceStepX) * FenceStepX;
            }

            int built = 0;
            int start = 0;
            for (int i = 1; i <= slices; i++)
            {
                if (i < slices && Mathf.Approximately(wallX[i], wallX[start])) continue;

                float z0 = -half + start * FenceSliceZ;
                float z1 = -half + i * FenceSliceZ;
                edge($"Seaward {built:00}",
                    new Vector3(wallX[start], centreY, (z0 + z1) * 0.5f),
                    new Vector3(thickness, tall, z1 - z0));
                built++;
                start = i;
            }

            return built;
        }

        /// <summary>
        /// The largest x at this z that still has something solid under it, searching seaward from
        /// <paramref name="from"/>. Ignore Raycast is excluded, so the fence never measures itself or
        /// the sea wall.
        /// </summary>
        private static float SeawardSolidEdge(float z, float from, float to)
        {
            float last = from;
            int misses = 0;

            for (float x = from; x <= to; x += FenceStepX)
            {
                bool hit = Physics.Raycast(
                    new Vector3(x, 80f, z), Vector3.down, out _, 200f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

                if (hit) { last = x; misses = 0; continue; }
                if (++misses >= 2) break;
            }

            return last;
        }

        /// <summary>
        /// The fence pass: throw the old one away and measure a new one against the world as it now
        /// stands. Shared by the full build and <c>Rebuild World Bounds</c> so the two cannot drift.
        /// </summary>
        private static void BuildWorldEdgesPass(Transform root, TheBlockConfig.Root config, Report report)
        {
            var ground = config?.Ground;
            if (ground == null || ground.Size <= 0f)
            {
                report.Warnings.Add("world edges skipped - config has no `ground` section");
                return;
            }

            var plane = root.Find("Ground");
            if (plane == null)
            {
                report.Warnings.Add("world edges skipped - no `Ground` object to fence");
                return;
            }

            var stale = plane.Find("World Edges");
            if (stale != null) UnityEngine.Object.DestroyImmediate(stale.gameObject);

            var sea = config.Sea;
            float far = -ground.Size * 0.5f;
            float waterline = sea != null ? SeaGeometry.ShoreX(sea) : ground.Size * 0.5f;

            BuildWorldEdges(plane, far, ground.Size * 0.5f, waterline, ground.Size, sea, report);
        }

        /// <summary>
        /// The plate as a flat mesh with the SEA'S FOOTPRINT CUT OUT of it.
        ///
        /// ⚠ THE WATER IS NOT RELIABLY ABOVE THE PLATE, which is what U12 assumed when it kept the
        /// visual plane at its full 1400 m ("the water is opaque and drawn above it"). It is not
        /// arithmetic that holds: `sea.surface.waves` carries amplitudes 0.18 + 0.12 + 0.07, so a
        /// trough reaches 0.37 m below the water line, while the plate sits at only −0.05. Every
        /// trough deeper than 5 cm exposes the green plate through the sea, in wide bands that
        /// follow the swell - it reads as a shader fault and is really two surfaces interpenetrating.
        ///
        /// The plate is never visible under opaque water, so the fix is to stop drawing it there
        /// rather than to move either surface: moving the plate down would leave its collider (which
        /// U12 already trimmed at the shore) floating above the visible ground between districts,
        /// and the water line is gameplay.
        ///
        /// The cut is a rectangle, so what is left is up to four rectangles. Land BEYOND the sea's
        /// z-strip is kept - the sea is only 600 m deep in z against the plate's 1400, and trimming
        /// the whole seaward half would put sky where there is currently ground.
        ///
        /// Predates U15's draw distance: the same bands are in a 320 m capture. Extending the view
        /// only made more of the sea visible at once.
        /// </summary>
        private static Mesh BuildGroundMesh(
            TheBlockConfig.GroundSpec ground, TheBlockConfig.SeaSpec sea, Report report)
        {
            float half = ground.Size * 0.5f;
            var rects = new List<Rect>();

            // Local space: the object already sits at ground.Y, so every vertex is y = 0.
            var cut = SeaFootprint(sea, half);
            if (cut.HasValue)
            {
                var c = cut.Value;
                if (c.xMin > -half) rects.Add(Rect.MinMaxRect(-half, -half, c.xMin, half));
                if (c.xMax < half) rects.Add(Rect.MinMaxRect(c.xMax, -half, half, half));
                if (c.yMin > -half) rects.Add(Rect.MinMaxRect(c.xMin, -half, c.xMax, c.yMin));
                if (c.yMax < half) rects.Add(Rect.MinMaxRect(c.xMin, c.yMax, c.xMax, half));
                report.Notes.Add(
                    $"Ground: sea footprint cut out - Unity x [{c.xMin:0}, {c.xMax:0}] z [{c.yMin:0}, {c.yMax:0}], " +
                    "so wave troughs cannot expose the plate");
            }
            else
            {
                rects.Add(Rect.MinMaxRect(-half, -half, half, half));
            }

            var vertices = new List<Vector3>(rects.Count * 4);
            var normals = new List<Vector3>(rects.Count * 4);
            var uvs = new List<Vector2>(rects.Count * 4);
            var triangles = new List<int>(rects.Count * 6);

            foreach (var r in rects)
            {
                int b = vertices.Count;
                // Rect's y is the world Z axis here.
                vertices.Add(new Vector3(r.xMin, 0f, r.yMin));
                vertices.Add(new Vector3(r.xMin, 0f, r.yMax));
                vertices.Add(new Vector3(r.xMax, 0f, r.yMax));
                vertices.Add(new Vector3(r.xMax, 0f, r.yMin));
                for (int i = 0; i < 4; i++) normals.Add(Vector3.up);
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(1f, 0f));
                triangles.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }

            var mesh = new Mesh { name = "GroundPlate" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            EnsureFolder(GeneratedWorldFolder);
            var assetPath = $"{GeneratedWorldFolder}/GroundPlate.asset";
            report.Generated.Add(assetPath);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing != null) AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(mesh, assetPath);
            return mesh;
        }

        /// <summary>
        /// The sea's rectangle in Unity's frame, clipped to the plate - or null when they do not
        /// overlap. <c>Rect.y</c> is world Z.
        /// </summary>
        private static Rect? SeaFootprint(TheBlockConfig.SeaSpec sea, float half)
        {
            if (sea == null || sea.Width <= 0f || sea.Length <= 0f) return null;

            // Converted the same way BuildWaterSurface places the plane, so the two cannot disagree
            // about where the water is. Never a hand-written sign flip - see Convert.
            var centre = Convert.Pos(sea.ShoreX - sea.Width * 0.5f, 0f, sea.CenterZ);
            float xMin = Mathf.Max(centre.x - sea.Width * 0.5f, -half);
            float xMax = Mathf.Min(centre.x + sea.Width * 0.5f, half);
            float zMin = Mathf.Max(centre.z - sea.Length * 0.5f, -half);
            float zMax = Mathf.Min(centre.z + sea.Length * 0.5f, half);

            if (xMin >= xMax || zMin >= zMax) return null;
            return Rect.MinMaxRect(xMin, zMin, xMax, zMax);
        }

        private static Material LoadOrCreateGroundMaterial(TheBlockConfig.GroundSpec ground, Report report)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
            if (existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                report.Warnings.Add("ground material skipped - URP/Lit shader not found");
                return null;
            }

            var material = new Material(shader) { name = "Ground" };
            // sRGB, not .linear - same trap as glTFast's baseColorFactor.
            material.SetColor("_BaseColor", TheBlockConfig.ColorFromHex(ground.Color));
            material.SetFloat("_Smoothness", 0f);

            var folder = System.IO.Path.GetDirectoryName(GroundMaterialPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Materials", "World");
            AssetDatabase.CreateAsset(material, GroundMaterialPath);
            report.Notes.Add($"created {GroundMaterialPath}");
            return material;
        }

        // --- placement -------------------------------------------------------------------------

        private static void BuildDistrict(
            Transform parent, string url, string name, TheBlockConfig.Vec3 position, float scale,
            List<string> hideMaterials, List<string> noCollidePatterns, List<string> facadeMaterials,
            Options options, Report report)
        {
            var instance = Instantiate(url, name, parent, report, out var substitute);
            if (instance == null) return;

            instance.name = $"District_{Sanitize(name)}";
            instance.transform.position = Convert.Pos(position.Raw) + Vector3.up * (substitute?.ExtraY ?? 0f);
            instance.transform.rotation = Quaternion.Euler(substitute?.ExtraEuler ?? Vector3.zero);
            instance.transform.localScale = Vector3.one * (scale <= 0f ? 1f : scale);
            SetDistrictStaticFlags(instance);

            if (facadeMaterials != null) ApplyFacadeMaterial(instance, facadeMaterials, report);
            if (hideMaterials != null) HideByMaterial(instance, hideMaterials, report);
            ApplyCutoutMaterials(instance, report);
            // Last of the material passes: it has to see the cutouts too, since those point at the
            // same uncompressed sub-asset textures.
            if (options.CompressedTextures) ApplyCompressedTextures(instance, report);
            // After the cutout pass, so anything reported is genuinely still blended.
            report.NoteTransparentMaterials(instance);
            if (options.Colliders) AddColliders(instance, noCollidePatterns, null, null, report);

            report.Placed.Add($"{instance.name} @ {Fmt(instance.transform.position)}");
        }

        /// <summary>
        /// Places one static prop. Returns the instance so a caller that needs to reach INTO the
        /// model can - U28's store binds five of its own nodes off the result.
        /// </summary>
        private static GameObject BuildPlace(
            Transform parent, TheBlockConfig.PlaceSpec place, string label, Options options, Report report)
        {
            if (place == null) return null;

            var instance = Instantiate(place.Url, label, parent, report, out var substitute);
            if (instance == null) return null;

            instance.name = $"Place_{Sanitize(label)}";
            instance.transform.position =
                Convert.Pos(place.Position.Raw) + Vector3.up * (substitute?.ExtraY ?? 0f);
            // The stand-in's correction goes on the right, so it stays in the model's own frame and
            // the config's yaw still means the same thing it means for the real asset.
            instance.transform.rotation =
                Convert.RotFromRadians(place.Yaw) * Quaternion.Euler(substitute?.ExtraEuler ?? Vector3.zero);
            instance.transform.localScale = Vector3.one * (place.Scale <= 0f ? 1f : place.Scale);

            HideCollisionProxies(instance, report);

            // hideNodes names parts of the ORIGINAL model. A stand-in that happens to share a node
            // name would lose a piece it needs - the pizza substitute's lamp post is called
            // PizzaLight, the same name the original build hides.
            if (substitute?.File != null)
                report.Notes.Add($"{instance.name}: hideNodes skipped - they name the original model's parts");
            else if (place.HideNodes != null)
                HideByNode(instance, place.HideNodes, report);

            ApplyCutoutMaterials(instance, report);
            if (options.CompressedTextures) ApplyCompressedTextures(instance, report);
            report.NoteTransparentMaterials(instance);

            if (options.Colliders)
                AddColliders(instance, null, place.NoCollideNodes, place.CollideMaxY, report);

            report.Placed.Add(
                $"{instance.name} @ {Fmt(instance.transform.position)} yaw {instance.transform.eulerAngles.y:0.#}°");

            return instance;
        }

        /// <summary>
        /// Resolves a web URL like <c>/models/optimized/split/procedural-city-4.glb</c> to an asset in
        /// this project by file name. The web build's folder layout (optimized/, split/) is a download
        /// concern that does not exist here, so only the base name is matched.
        /// </summary>
        private static GameObject Instantiate(
            string url, string label, Transform parent, Report report, out Substitute substitute)
        {
            substitute = null;
            if (string.IsNullOrEmpty(url))
            {
                report.Missing.Add($"{label} - config has no url");
                return null;
            }

            var fileName = url.Substring(url.LastIndexOf('/') + 1);
            if (AssetAliases.TryGetValue(fileName, out substitute))
            {
                if (substitute.File != null)
                {
                    report.Warnings.Add(
                        $"{label} - stand-in {substitute.File} for {fileName}: {substitute.Note}");
                    fileName = substitute.File;
                }
                else
                {
                    report.Warnings.Add($"{label} - {fileName} corrected on import: {substitute.Note}");
                }
            }

            var bareName = System.IO.Path.GetFileNameWithoutExtension(fileName);

            var assetPath = AssetDatabase
                .FindAssets($"{bareName} t:GameObject", new[] { "Assets/Models" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(path =>
                    string.Equals(System.IO.Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));

            if (assetPath == null)
            {
                report.Missing.Add($"{label} - no asset named {fileName} under Assets/Models");
                return null;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                report.Missing.Add($"{label} - {assetPath} did not load as a GameObject");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            return instance;
        }

        // --- material and node passes ----------------------------------------------------------

        /// <summary>
        /// Rebinds the downtown facade slots to <c>Facade.mat</c>.
        ///
        /// The web build recolours those materials in code at load because it cannot author material
        /// assets; Unity can, so the tint is an asset (decision U1). Matching is by the imported
        /// material's name against <c>city.facadeMaterials</c>, which is what the web build matches too.
        /// </summary>
        private static void ApplyFacadeMaterial(GameObject instance, List<string> facadeMaterials, Report report)
        {
            var facade = AssetDatabase.LoadAssetAtPath<Material>(FacadeMaterialPath);
            if (facade == null)
            {
                report.Warnings.Add($"facade tint skipped - {FacadeMaterialPath} not found");
                return;
            }

            var rebound = 0;
            foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null || !facadeMaterials.Contains(materials[i].name)) continue;
                    materials[i] = facade;
                    changed = true;
                    rebound++;
                }

                if (changed) renderer.sharedMaterials = materials;
            }

            if (rebound == 0)
                report.Warnings.Add(
                    $"facade tint matched nothing - looked for [{string.Join(", ", facadeMaterials)}]");
            else
                report.Notes.Add($"facade tint bound to {rebound} material slot(s)");
        }

        /// <summary>
        /// Removes the districts' baked-in parked cars.
        ///
        /// A renderer whose every slot is a hidden material is simply switched off. A renderer that
        /// MIXES them with real geometry - cities 2 and 3 each merge their cars into the same
        /// 300k-vertex mesh as their streets and buildings - gets the car SUBMESHES stripped out of
        /// a generated copy of its mesh instead, which is the thing the web build could not do:
        /// three.js had one material path per draw and no edit-time asset step, so it could only
        /// hide a whole object. Unity owns the mesh at build time, so the split is a build step and
        /// the .glb on disk is never touched.
        ///
        /// Stripping rather than tinting also takes the cars out of collision, since the collider
        /// pass reads the same (now stripped) mesh - an invisible but solid parked car is exactly
        /// the kind of thing U17's traffic would pile into.
        /// </summary>
        private static void HideByMaterial(GameObject instance, List<string> hideMaterials, Report report)
        {
            if (hideMaterials.Count == 0) return;

            int hidden = 0, stripped = 0, strippedSubmeshes = 0;
            foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                var materials = renderer.sharedMaterials;
                if (materials.Length == 0) continue;

                var matches = materials.Count(m => m != null && hideMaterials.Contains(m.name));
                if (matches == 0) continue;

                if (matches == materials.Length)
                {
                    renderer.gameObject.SetActive(false);
                    hidden++;
                    continue;
                }

                if (StripSubmeshes(instance, renderer, hideMaterials, report, out var removed))
                {
                    stripped++;
                    strippedSubmeshes += removed;
                }
            }

            if (hidden > 0) report.Notes.Add($"{instance.name}: hid {hidden} baked-car renderer(s)");
            if (stripped > 0)
                report.Notes.Add(
                    $"{instance.name}: stripped {strippedSubmeshes} baked-car submesh(es) out of {stripped} merged mesh(es)");
        }

        /// <summary>
        /// Rebuilds a renderer's mesh without the submeshes whose material is in
        /// <paramref name="hideMaterials"/>, and drops the matching material slots.
        ///
        /// The vertices are compacted, not just the indices dropped: in city 2 the parked cars are
        /// 186,186 of the mesh's 216,515 triangles, so leaving their vertices behind unreferenced
        /// would mean shipping a buffer that is 86% dead weight. Reindexing what survives is cheap
        /// by comparison - it is a few tens of thousands of triangles.
        ///
        /// The result is saved as an asset because a mesh created here and left unsaved is
        /// serialized INTO the scene file. Written under <see cref="GeneratedMeshFolder"/>,
        /// gitignored for the same reason the district .glbs are.
        /// </summary>
        private static bool StripSubmeshes(
            GameObject instance, MeshRenderer renderer, List<string> hideMaterials, Report report, out int removed)
        {
            removed = 0;
            if (!renderer.TryGetComponent<MeshFilter>(out var filter)) return false;

            var source = filter.sharedMesh;
            var materials = renderer.sharedMaterials;
            if (source == null || source.subMeshCount != materials.Length) return false;

            var keep = new List<int>();
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null && hideMaterials.Contains(materials[i].name)) removed++;
                else keep.Add(i);
            }

            if (removed == 0 || keep.Count == 0) return false;

            var indices = keep.Select(source.GetTriangles).ToList();
            var mesh = Compact(source, indices, $"{Sanitize(instance.name)}_{Sanitize(source.name)}_stripped");

            EnsureFolder(GeneratedMeshFolder);
            var assetPath = $"{GeneratedMeshFolder}/{mesh.name}.asset";
            report.Generated.Add(assetPath);
            AssetDatabase.CreateAsset(mesh, assetPath);

            filter.sharedMesh = mesh;
            renderer.sharedMaterials = keep.Select(i => materials[i]).ToArray();
            return true;
        }

        /// <summary>
        /// Builds a mesh holding only the vertices <paramref name="indices"/> still reach, with the
        /// index lists remapped onto them - one submesh per list, in the order given.
        ///
        /// Vertex order is first-use order, which keeps the surviving triangles' vertices roughly
        /// as locally coherent as they were.
        /// </summary>
        private static Mesh Compact(Mesh source, List<int[]> indices, string name)
        {
            var remap = new Dictionary<int, int>();
            var order = new List<int>();
            foreach (var submesh in indices)
            {
                for (int i = 0; i < submesh.Length; i++)
                {
                    if (remap.ContainsKey(submesh[i])) continue;
                    remap[submesh[i]] = order.Count;
                    order.Add(submesh[i]);
                }
            }

            var mesh = new Mesh
            {
                name = name,
                indexFormat = order.Count > ushort.MaxValue
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16,
            };

            mesh.SetVertices(Gather(source.vertices, order));
            if (source.normals.Length > 0) mesh.SetNormals(Gather(source.normals, order));
            if (source.tangents.Length > 0) mesh.SetTangents(Gather(source.tangents, order));
            if (source.colors.Length > 0) mesh.SetColors(Gather(source.colors, order));

            // glTF allows up to eight TEXCOORD sets and these districts use two; asking for all of
            // them costs nothing and means a re-export with a lightmap UV does not quietly lose it.
            var uvs = new List<Vector4>();
            for (int channel = 0; channel < 8; channel++)
            {
                source.GetUVs(channel, uvs);
                if (uvs.Count > 0) mesh.SetUVs(channel, Gather(uvs.ToArray(), order));
            }

            mesh.subMeshCount = indices.Count;
            for (int i = 0; i < indices.Count; i++)
            {
                var remapped = new int[indices[i].Length];
                for (int j = 0; j < remapped.Length; j++) remapped[j] = remap[indices[i][j]];
                mesh.SetTriangles(remapped, i, calculateBounds: false);
            }

            mesh.RecalculateBounds();
            return mesh;
        }

        private static List<T> Gather<T>(T[] source, List<int> order)
        {
            var gathered = new List<T>(order.Count);
            foreach (var index in order) gathered.Add(source[index]);
            return gathered;
        }

        /// <summary>
        /// Rebinds every imported material whose alpha is really a cutout mask (see
        /// <see cref="CutoutMaterialPatterns"/>) to a generated alpha-clipped URP/Lit material.
        ///
        /// glTFast's own Shader Graph will not do this: its surface mode is decided at import from
        /// the glTF's <c>alphaMode</c>, and <c>_AlphaClip</c> on the imported material is inert
        /// because the graph's keywords were baked for the blended variant. So the fix is a separate
        /// material asset, the same answer U1 reached for the facade tint - the imported material is
        /// read for its texture and factors and otherwise left exactly as imported.
        ///
        /// The generated material is rewritten in place on every build rather than reused as found,
        /// so it stays a pure function of the imported one; a re-export of a district changes the
        /// texture and the next build picks it up.
        /// </summary>
        private static void ApplyCutoutMaterials(GameObject instance, Report report)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                report.Warnings.Add("alpha-clip pass skipped - URP/Lit shader not found");
                return;
            }

            var converted = new Dictionary<Material, Material>();
            var names = new SortedSet<string>(StringComparer.Ordinal);
            var defaults = 0;

            foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];

                    // A submesh whose glTF primitive names no material. Unity draws an empty slot
                    // with the magenta error shader - small pink rectangles scattered over the
                    // pavement - where glTF says it is the spec's default PBR material.
                    if (source == null)
                    {
                        materials[i] = GltfDefaultMaterial(shader, report);
                        changed = true;
                        defaults++;
                        continue;
                    }

                    if (!IsBlended(source)) continue;
                    if (!ContainsAny(source.name, CutoutMaterialPatterns)) continue;
                    if (IsGeneratedCutout(source)) continue;

                    if (!converted.TryGetValue(source, out var cutout))
                    {
                        cutout = BuildCutoutMaterial(instance, source, shader, report);
                        converted[source] = cutout;
                        names.Add(source.name);
                    }

                    materials[i] = cutout;
                    changed = true;
                }

                if (changed) renderer.sharedMaterials = materials;
            }

            if (names.Count > 0)
                report.Notes.Add($"{instance.name}: alpha-clipped {string.Join(", ", names)}");
            if (defaults > 0)
                report.Notes.Add($"{instance.name}: filled {defaults} empty material slot(s) - were rendering magenta");
        }

        /// <summary>
        /// glTF's default material, as the spec states it: white, fully metallic, fully rough.
        ///
        /// Deliberately not something prettier. A submesh with no material is the asset saying
        /// nothing, and inventing a look for it would hide that; this is drab and correct, and it is
        /// what a glTF viewer shows.
        /// </summary>
        private static Material GltfDefaultMaterial(Shader shader, Report report)
        {
            EnsureFolder(CutoutMaterialFolder);
            var assetPath = $"{CutoutMaterialFolder}/GltfDefault.mat";
            report.Generated.Add(assetPath);

            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                material = new Material(shader) { name = "GltfDefault" };
                AssetDatabase.CreateAsset(material, assetPath);
            }

            material.shader = shader;
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 1f);
            material.SetFloat("_Smoothness", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// True when a material actually renders in the transparent queue - the precondition for the
        /// alpha-clip pass, and not merely a tidiness check.
        ///
        /// <see cref="CutoutMaterialPatterns"/> is matched as substrings, and "tree" is a substring
        /// of "CityGen_S<i>tree</i>ts": without this guard the pass converted every district's road
        /// surface, which is opaque and has nothing to clip. An alpha cutout only ever fixes
        /// something that is blended in the first place, so ask that first and the name match only
        /// has to choose among the blended ones.
        /// </summary>
        private static bool IsBlended(Material material) =>
            material.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent;

        private static bool IsGeneratedCutout(Material material) =>
            AssetDatabase.GetAssetPath(material)
                .StartsWith(CutoutMaterialFolder, StringComparison.Ordinal);

        /// <summary>
        /// Copies an imported glTF material onto a URP/Lit one with alpha clipping on.
        ///
        /// Only the base map, its tiling, the base colour and the metallic/roughness SCALARS carry
        /// over. glTF packs metal-roughness into G and B of one texture and occlusion into R of
        /// another; URP/Lit wants metallic in R and smoothness in A, so copying those maps across
        /// would be silently wrong. None of the materials this pass touches has one.
        /// </summary>
        private static Material BuildCutoutMaterial(
            GameObject instance, Material source, Shader shader, Report report)
        {
            EnsureFolder(CutoutMaterialFolder);
            var assetPath = $"{CutoutMaterialFolder}/{Sanitize(instance.name)}_{Sanitize(source.name)}.mat";
            report.Generated.Add(assetPath);

            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, assetPath);
            }
            else
            {
                material.shader = shader;
            }

            if (source.HasProperty("baseColorTexture"))
                material.SetTexture("_BaseMap", source.GetTexture("baseColorTexture"));
            if (source.HasProperty("baseColorTexture_ST"))
                material.SetVector("_BaseMap_ST", UnflipV(source.GetVector("baseColorTexture_ST")));

            // glTFast's `baseColorFactor` holds an sRGB value (memory: gltfast-basecolorfactor-gamma),
            // while URP/Lit's `_BaseColor` is an untagged colour property and so is read as linear.
            // Every material this pass touches is pure white, where the two agree - the conversion is
            // here to be right rather than to be visible.
            if (source.HasProperty("baseColorFactor"))
                material.SetColor("_BaseColor", source.GetColor("baseColorFactor").linear);
            if (source.HasProperty("metallicFactor"))
                material.SetFloat("_Metallic", source.GetFloat("metallicFactor"));
            if (source.HasProperty("roughnessFactor"))
                material.SetFloat("_Smoothness", 1f - source.GetFloat("roughnessFactor"));

            // Opaque surface + alpha clip. The imported `alphaCutoff` is 0 on a BLEND material -
            // glTF only defines it for MASK - so a real threshold has to be chosen here.
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.5f);
            // Leaf cards and railings are single-sided geometry seen from both sides.
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            material.doubleSidedGI = true;

            // Keywords, render queue, the TransparentCutout RenderType tag and _AlphaToMask all
            // follow from those four floats - and getting any of them wrong by hand is how a
            // material ends up clipping in the colour pass but not in the shadow pass.
            BaseShaderGUI.SetupMaterialBlendMode(material);

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Deletes anything in the generated folders that THIS build did not write.
        ///
        /// Without it the generated folders are append-only and a rename, a re-export or a corrected
        /// pattern list leaves a plausible-looking .mat behind that nothing references - which is
        /// the same "invisible and unreproducible" failure that keeps the world itself out of the
        /// scene file. The build is a pure function of the config and the assets; its output folders
        /// have to be too.
        /// </summary>
        private static void SweepGenerated(Report report)
        {
            foreach (var folder in new[]
                     {
                         CutoutMaterialFolder, CompressedMaterialFolder, GeneratedMeshFolder,
                         GeneratedWorldFolder, LotCarMaterialFolder,
                     })
            {
                if (!AssetDatabase.IsValidFolder(folder)) continue;

                foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { folder }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetDatabase.IsValidFolder(path) || report.Generated.Contains(path)) continue;
                    AssetDatabase.DeleteAsset(path);
                    report.Notes.Add($"deleted stale generated asset {path}");
                }
            }
        }

        /// <summary>
        /// Takes glTFast's tiling/offset back out of glTFast's own V convention.
        ///
        /// glTFast decides per TEXTURE whether the imported image ended up vertically flipped, and
        /// compensates by writing a negative Y scale into the material - an identity glTF transform
        /// comes out as <c>(1, -1, 0, 1)</c>. In these districts that decision is WRONG, and it is
        /// wrong inconsistently: <c>FoliageTrees.001</c> through <c>.004</c> all sample the same
        /// image through four different glTF texture entries, and only .001 came out unflipped.
        ///
        /// Which of them is right is measurable, not a matter of taste. The leaves occupy
        /// u [0, 0.25] × v [0, 0.25] of the imported Texture2D - the bottom-left sixteenth, the rest
        /// of the atlas being blank white - and the canopy meshes' UVs are in exactly that range.
        /// So the identity is correct and the flip is what sends three materials out of four into
        /// the white part of the atlas. THAT is the white shards, all along: not a blend-mode fault
        /// at all, which is why alpha-clipping alone left them white.
        ///
        /// Undoing the flip rather than forcing the identity keeps a genuine
        /// KHR_texture_transform - tiling, offset - intact if a future district ships one.
        /// </summary>
        private static Vector4 UnflipV(Vector4 scaleOffset) =>
            scaleOffset.y >= 0f
                ? scaleOffset
                : new Vector4(scaleOffset.x, -scaleOffset.y, scaleOffset.z, 1f - scaleOffset.w);

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            var parts = path.Split('/');
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        /// <summary>
        /// Hides the crude collision proxy that Sketchfab-sourced props ship alongside their real
        /// geometry - a node named <c>Collider</c> holding a coarse box, meant to be collided with
        /// and never drawn.
        ///
        /// Imported straight, it renders as a grey slab swallowing the prop, and it is the first
        /// thing a downward raycast hits, so ground probes read its roof instead of the pavement.
        /// Hiding it also drops it from the collider pass, which only looks at active meshes - and
        /// the real geometry makes a better collider than the box did.
        /// </summary>
        private static void HideCollisionProxies(GameObject instance, Report report)
        {
            var hidden = new List<string>();
            foreach (var child in instance.GetComponentsInChildren<Transform>(true))
            {
                if (!child.name.StartsWith("Collider", StringComparison.OrdinalIgnoreCase)) continue;
                child.gameObject.SetActive(false);
                hidden.Add(child.name);
            }

            if (hidden.Count > 0)
                report.Notes.Add($"{instance.name}: hid collision prox{(hidden.Count == 1 ? "y" : "ies")} {string.Join(", ", hidden)}");
        }

        private static void HideByNode(GameObject instance, List<string> hideNodes, Report report)
        {
            if (hideNodes.Count == 0) return;

            var hidden = new List<string>();
            foreach (var child in instance.GetComponentsInChildren<Transform>(true))
            {
                if (!hideNodes.Contains(child.name)) continue;
                child.gameObject.SetActive(false);
                hidden.Add(child.name);
            }

            if (hidden.Count > 0)
                report.Notes.Add($"{instance.name}: hid node(s) {string.Join(", ", hidden)}");
        }

        // --- colliders -------------------------------------------------------------------------

        /// <summary>
        /// Gives every visible mesh a non-convex MeshCollider, minus the exclusions the config asks
        /// for: foliage by name substring, specific nodes by exact name, and anything topping out
        /// above <paramref name="collideMaxY"/>.
        ///
        /// Note that a district whose GLB is one merged mesh (downtown) has no node names to match,
        /// so its foliage collides - the web build has the same hole. That is faithful, not a bug.
        /// </summary>
        private static void AddColliders(
            GameObject instance, List<string> noCollidePatterns, List<string> noCollideNodes,
            float? collideMaxY, Report report)
        {
            int added = 0, skipped = 0;

            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || !filter.gameObject.activeInHierarchy) continue;

                if (noCollideNodes != null && noCollideNodes.Contains(filter.name)) { skipped++; continue; }
                if (MatchesAny(filter, noCollidePatterns)) { skipped++; continue; }

                if (collideMaxY.HasValue)
                {
                    var renderer = filter.GetComponent<Renderer>();
                    if (renderer != null && renderer.bounds.max.y > collideMaxY.Value) { skipped++; continue; }
                }

                // Not `??` - UnityEngine.Object overloads `==` for destroyed objects, and `??`
                // bypasses that overload, so it hands back a fake-null and the next line throws.
                if (!filter.gameObject.TryGetComponent<MeshCollider>(out var collider))
                    collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.convex = false;
                added++;
            }

            report.Colliders += added;
            if (skipped > 0) report.Notes.Add($"{instance.name}: {added} collider(s), {skipped} skipped");
            else report.Notes.Add($"{instance.name}: {added} collider(s)");
        }

        /// <summary>
        /// True when this mesh is foliage and should carry no collider.
        ///
        /// The node name decides it outright. Material names only decide it when EVERY material on
        /// the renderer is foliage - the district GLBs are merged meshes carrying a dozen submeshes
        /// each, so "any material matches" would strip collision from a whole district because one
        /// of its submeshes is a tree. A mixed mesh collides, palms included; that is what downtown
        /// does and what the web build does.
        /// </summary>
        private static bool MatchesAny(MeshFilter filter, List<string> patterns)
        {
            if (patterns == null || patterns.Count == 0) return false;
            if (ContainsAny(filter.name, patterns)) return true;

            var renderer = filter.GetComponent<Renderer>();
            var materials = renderer == null ? null : renderer.sharedMaterials;
            if (materials == null || materials.Length == 0) return false;

            foreach (var material in materials)
            {
                if (material == null || !ContainsAny(material.name, patterns)) return false;
            }

            return true;
        }

        private static bool ContainsAny(string text, IEnumerable<string> patterns)
        {
            foreach (var pattern in patterns)
            {
                if (text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            return false;
        }

        // --- reporting -------------------------------------------------------------------------

        /// <summary>
        /// Marks a district static for occlusion, GI, navmesh and reflection probes - but NOT for
        /// batching.
        ///
        /// Static batching rewrites the MeshFilter to point at a "Combined Mesh (root: scene)" that
        /// Unity builds with a 16-bit index buffer. Downtown has 122,678 vertices, so every index
        /// past 65,535 wraps and its triangles reach across the mesh: the block renders as a nest
        /// of giant spikes, while its collider (which still uses the real asset mesh) stays correct,
        /// so the world feels right and looks shredded.
        ///
        /// There is nothing to win here anyway. Batching exists to merge many small draw calls, and
        /// a district is one to four huge meshes.
        /// </summary>
        private static void SetDistrictStaticFlags(GameObject go)
        {
            // Listed one by one rather than "everything except batching": passing an all-bits value
            // is normalised straight back to Everything, which quietly puts BatchingStatic back.
            const StaticEditorFlags flags =
                StaticEditorFlags.ContributeGI |
                StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.NavigationStatic |
                StaticEditorFlags.OffMeshLinkGeneration |
                StaticEditorFlags.ReflectionProbeStatic;

            foreach (var child in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, flags);
        }

        private static string Sanitize(string name) => name.Replace(" ", string.Empty);

        private static string Fmt(Vector3 v) => $"({v.x:0.##}, {v.y:0.##}, {v.z:0.##})";

        private class Report
        {
            public readonly List<string> Placed = new();
            public readonly List<string> Missing = new();
            public readonly List<string> Warnings = new();
            public readonly List<string> Notes = new();
            public readonly List<string> RemovedLegacy = new();
            public readonly SortedSet<string> StillBlended = new(StringComparer.Ordinal);

            /// <summary>Asset paths this build wrote under the generated folders. Anything else there is stale.</summary>
            public readonly HashSet<string> Generated = new(StringComparer.Ordinal);
            public int Colliders;

            /// <summary>
            /// Records every material still on a transparent surface after the alpha-clip pass.
            ///
            /// Deciding which blended materials are really cutouts is a judgement call made in
            /// <see cref="CutoutMaterialPatterns"/>, and the ones left over are exactly the
            /// candidates if something else in the world still renders as a pale smear. Naming them
            /// costs a line of report and turns "the trees look wrong" into a list to check.
            /// </summary>
            public void NoteTransparentMaterials(GameObject instance)
            {
                foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!renderer.gameObject.activeInHierarchy) continue;
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null) continue;
                        if (material.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent)
                            StillBlended.Add(material.name);
                    }
                }
            }

            public string Compose(TheBlockConfig.Snapshot snapshot, TimeSpan elapsed, Options options)
            {
                var sb = new StringBuilder();
                sb.AppendLine(
                    $"WorldBuilder - {Placed.Count} placed, {Missing.Count} missing, {Colliders} colliders, {elapsed.TotalSeconds:0.0}s");
                sb.AppendLine($"config.ts {snapshot.SourceSha256?[..12]}  " +
                              $"districts:{options.Districts} places:{options.Places} colliders:{options.Colliders}");

                Section(sb, "PLACED", Placed);
                Section(sb, "MISSING - asset not in the project yet", Missing);
                Section(sb, "WARNINGS", Warnings);
                Section(sb, "REPLACED hand-placed roots", RemovedLegacy);
                if (StillBlended.Count > 0)
                    Section(sb, "STILL BLENDED - deliberate; suspects if anything else renders pale",
                        new List<string> { string.Join(", ", StillBlended) });
                Section(sb, "NOTES", Notes);
                return sb.ToString().TrimEnd();
            }

            private static void Section(StringBuilder sb, string title, List<string> lines)
            {
                if (lines.Count == 0) return;
                sb.AppendLine();
                sb.AppendLine(title);
                foreach (var line in lines) sb.AppendLine($"  {line}");
            }
        }
    }
}
