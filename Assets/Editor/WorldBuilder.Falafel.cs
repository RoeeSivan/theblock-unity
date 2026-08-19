using System.Collections.Generic;
using TheBlock.Missions;
using TheBlock.World;
using UnityEditor;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// פלאפל הפעמונים - the falafel stand on the east pavement, and the vendor who hands out
    /// rounds from it.
    ///
    /// <b>The second place the web build never had</b>, after the auto shop, so it follows that
    /// one's shape exactly: the numbers live in <see cref="FalafelSpec"/> in Unity space and never
    /// go through <c>Convert.Pos</c>, because the builder, the map pin and the runtime all measure
    /// from the same field and three copies of a coordinate is how they drift.
    ///
    /// <b>The model is authored, not downloaded</b> - <c>tools/build-falafel.py</c> builds it from
    /// scratch in Blender against two photographs of the real shop on Herzl, joins it into six
    /// meshes and ships three empty marker nodes with it. Reading a marker is exact and free;
    /// re-deriving the same points as C# constants means choosing correctly between three handedness
    /// rules for a model-local coordinate, which is the trap variant rather than the famous one
    /// (memory: <c>glb-marker-nodes-beat-config-conversion</c>).
    ///
    /// Built from the full <b>Build World</b> (places pass) and on its own from
    /// <b>The Block → Build Falafel Stand</b>, which is what a play-test uses.
    /// </summary>
    public static partial class WorldBuilder
    {
        private const string FalafelFile = "falafel-stand.glb";

        /// <summary>
        /// Nodes that are not walls.
        ///
        /// The <b>glass</b> is the vitrine and the shopfront - a mesh collider on it would seal the
        /// open bay and put an invisible wall across the counter (memory:
        /// <c>an-invisible-wall-reads-as-a-game-rule</c>). The <b>food</b> is 2,884 triangles of
        /// chips and salad inside a case nobody can reach. The <b>sign</b> is 16,808 triangles of
        /// lettering three metres in the air, and its fascia box is part of the canopy, which does
        /// collide.
        ///
        /// The <b>awning</b> joined the list in U37b, and it is the only entry here that is about
        /// playability rather than geometry. The striped valance used to hang at 2.00 m over a
        /// pavement the player walks at 0.157 - four centimetres of headroom on a 1.80 m capsule.
        /// It has been raised to 2.26, and it is ALSO uncollidable now, because "raise it far
        /// enough" is a fix that the next tweak to the canopy height could quietly undo. The canopy
        /// POSTS are unaffected - they land in <c>_Shell</c>, not <c>_Canopy</c>, and still collide.
        /// </summary>
        private static readonly List<string> FalafelNoCollide = new()
        {
            "FalafelStand_Glass", "FalafelStand_Food", "FalafelStand_Sign", "FalafelStand_Awning",
        };

        /// <summary>The vendor object's name under Places - destroyed and rebuilt with the stand.</summary>
        private const string FalafelVendorName = "Falafel_Vendor";

        /// <summary>
        /// The customers a round is delivered to.
        ///
        /// <b>Deliberately not the pizza run's five.</b> Sophie, Remy, Elizabeth, Chinese and Lewis
        /// are mission 1's named faces; using them again would make the two jobs look like the same
        /// job with a different sign. These are four of the crowd's twelve unnamed people, already
        /// imported and already height-normalised, so it costs nothing.
        /// </summary>
        private static readonly string[] FalafelFaces =
        {
            "Ped_01f_02", "Ped_01m_03", "Ped_02f_01", "Ped_02m_03",
        };

        [MenuItem("The Block/Build Falafel Stand", priority = 9)]
        public static void BuildFalafelStandOnly()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError(
                    "Build Falafel Stand: stop Play mode first - a build in Play is discarded on exit.");
                return;
            }

            var root = Object.FindAnyObjectByType<TheBlock.Core.WorldRoot>();
            if (root == null)
            {
                Debug.LogError("Build Falafel Stand: no WorldRoot in the scene. Run The Block → Build World.");
                return;
            }

            var places = root.transform.Find("Places");
            if (places == null)
            {
                Debug.LogError("Build Falafel Stand: the world has no Places group. Run The Block → Build World.");
                return;
            }

            var report = new Report();
            var options = new Options();
            ResetTexturePass();

            BuildFalafelStand(places, options, report);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            var text = new System.Text.StringBuilder(
                $"Build Falafel Stand - {report.Warnings.Count} warning(s), {report.Missing.Count} missing\n");
            foreach (var line in report.Placed) text.AppendLine("  " + line);
            foreach (var line in report.Notes) text.AppendLine("  " + line);
            foreach (var line in report.Warnings) text.AppendLine("  WARNING " + line);
            foreach (var line in report.Missing) text.AppendLine("  MISSING " + line);

            if (report.Warnings.Count > 0 || report.Missing.Count > 0)
                Debug.LogWarning(text.ToString().TrimEnd());
            else Debug.Log(text.ToString().TrimEnd());
        }

        /// <summary>
        /// Instantiates the stand, its vendor and the job on it. Called from the places pass and
        /// from the menu; idempotent, so it can be re-run after every re-export.
        /// </summary>
        private static GameObject BuildFalafelStand(Transform places, Options options, Report report)
        {
            SweepStaleFalafel(places);

            var instance = Instantiate(FalafelSpec.ModelUrl, "Falafel Stand", places, report, out _);
            if (instance == null) return null;

            instance.name = FalafelSpec.ObjectName;
            instance.transform.SetPositionAndRotation(
                FalafelSpec.Position, Quaternion.Euler(0f, FalafelSpec.YawDeg, 0f));
            instance.transform.localScale = Vector3.one;

            ApplyCutoutMaterials(instance, report);
            if (options.CompressedTextures) ApplyCompressedTextures(instance, report);
            report.NoteTransparentMaterials(instance);

            if (options.Colliders) AddColliders(instance, null, FalafelNoCollide, null, report);

            // The marker nodes. Read AFTER the transform is set, so what is reported is where the
            // points actually are in the world rather than where they are in the file.
            var nodes = NodeIndex(instance);
            var talk = FalafelNode(nodes, FalafelSpec.TalkNode, report);
            var counter = FalafelNode(nodes, FalafelSpec.CounterNode, report);
            var vendorAt = FalafelNode(nodes, FalafelSpec.VendorNode, report);

            foreach (Transform child in instance.transform) SetDistrictStaticFlags(child.gameObject);

            BuildFalafelVendor(places, vendorAt, report);
            BindFalafelRun(instance, counter, talk, report);

            report.Placed.Add(
                $"{instance.name} @ {Fmt(instance.transform.position)} yaw {instance.transform.eulerAngles.y:0.#}° " +
                $"({instance.GetComponentsInChildren<MeshFilter>(true).Length} meshes, FalafelRun on it, " +
                $"counter {Fmt(counter != null ? counter.position : Vector3.zero)} r{FalafelSpec.CounterRadius:0.#}, " +
                $"nudge {Fmt(talk != null ? talk.position : Vector3.zero)} r{FalafelSpec.TalkRadius:0.#})");
            return instance;
        }

        /// <summary>
        /// Removes every previous stand and vendor under Places, <b>including one left under the
        /// model file's own name</b>.
        ///
        /// ⚠ THIS IS WIDER THAN IT LOOKS AND THE REASON IS A BUG THAT SHIPPED. The sweep used to be
        /// two <c>places.Find</c> calls, for <c>Place_FalafelStand</c> and <c>Falafel_Vendor</c>.
        /// U37b (`154954e`) left a second, complete stand in the scene named <c>falafel-stand</c> -
        /// the raw asset name, unrotated, at the world ORIGIN, 41,098 triangles standing in the
        /// middle of downtown - and because it did not carry either of those two names, every
        /// subsequent build swept right past it. It survived three commits and was found by a
        /// footprint query, not by anyone looking at it; the user saw it in a play-test and
        /// reasonably concluded the shop had moved.
        ///
        /// A name-keyed sweep only cleans up the names it already knows, so this one also matches
        /// the model file's stem. Anything the builder can create, the builder must be able to
        /// destroy.
        /// </summary>
        private static void SweepStaleFalafel(Transform places)
        {
            var modelStem = System.IO.Path.GetFileNameWithoutExtension(FalafelSpec.ModelUrl);
            var doomed = new List<GameObject>();

            foreach (Transform child in places)
                if (child.name == FalafelSpec.ObjectName ||
                    child.name == FalafelVendorName ||
                    child.name == modelStem)
                    doomed.Add(child.gameObject);

            foreach (var go in doomed) Object.DestroyImmediate(go);
        }

        private static Transform FalafelNode(
            Dictionary<string, Transform> nodes, string name, Report report)
        {
            if (nodes.TryGetValue(name, out var t)) return t;
            report.Warnings.Add(
                $"falafel stand - no node named '{name}' in the imported model. " +
                "Re-export it: tools/blend-to-glb.sh source-assets/falafel_hapaamonim.blend " +
                "Assets/Models/Places/falafel-stand.glb");
            return null;
        }

        /// <summary>
        /// The man behind the counter.
        ///
        /// One of the crowd's twelve unnamed faces, for the reason
        /// <c>WorldBuilder.Interior.BuildCounterNpc</c> gives about the pizzeria's cashier: he is
        /// already imported, already URP-bound and already height-normalised, and a dedicated model
        /// is tens of megabytes against an LFS store that is at GitHub's free ceiling.
        ///
        /// He stands, and that is free - a <c>Pedestrian</c> never bound to a seed never ticks, so
        /// its blend tree sits at Speed 0. No capsule either: he is behind a counter the player
        /// cannot reach round, and a collider there only ever means getting stuck on him.
        /// </summary>
        private static void BuildFalafelVendor(Transform places, Transform at, Report report)
        {
            if (at == null) return;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FalafelSpec.VendorPrefab);
            if (prefab == null)
            {
                report.Warnings.Add(
                    $"falafel vendor skipped - {FalafelSpec.VendorPrefab} is missing. Run Build Pedestrians.");
                return;
            }

            var vendor = (GameObject)PrefabUtility.InstantiatePrefab(prefab, places);
            vendor.name = FalafelVendorName;
            vendor.transform.SetPositionAndRotation(at.position, at.rotation);

            if (vendor.TryGetComponent<TheBlock.Npc.Pedestrian>(out var pedestrian))
                pedestrian.enabled = false;
            if (vendor.TryGetComponent<CapsuleCollider>(out var capsule)) capsule.enabled = false;

            // ALWAYS ANIMATE, and this is the one line here that is not boilerplate.
            //
            // The pack's prefabs arrive at `CullCompletely`: with no renderer on screen the Animator
            // does not run at all, so the rig sits in its BIND pose - and this pack binds in a T,
            // which is why the vendor reads as a shop mannequin the instant you look at him. It is
            // the same trap U19e's seated officer hit from the other side (memory:
            // <c>culled-animator-skips-pose-write</c>): every check says the state machine is fine
            // and no bone has moved.
            //
            // One stationary skinned NPC animating off-screen is nothing - the crowd runs ninety of
            // them - and it buys a vendor who is already mid-idle before the player is close enough
            // to read him, instead of one who snaps out of a T-pose on first sight.
            //
            // <b>In CHILDREN, and U38 is why.</b> The pack prefab used to carry its Animator on its
            // own root, so a TryGetComponent here found it. PackPedBuilder now wraps the pack body in
            // a `Visual` child - it has to, because the height correction belongs on the visual and
            // never on the root that owns the physics capsule - and the Animator went down with it.
            // A root-only lookup silently found nothing, left the vendor at CullCompletely, and put
            // him straight back in the T-pose this line exists to prevent.
            var animator = vendor.GetComponentInChildren<Animator>(true);
            if (animator != null) animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            report.Notes.Add(
                $"{FalafelSpec.ObjectName}: vendor at {Fmt(at.position)} " +
                $"facing {at.eulerAngles.y:0.#}° ({System.IO.Path.GetFileNameWithoutExtension(FalafelSpec.VendorPrefab)})");
        }

        /// <summary>
        /// The job itself, on the stand. Everything else it needs - the player, the vehicles, the
        /// HUD, the wallet, the bust - it finds in its own Awake; the talk point is the one fact
        /// that belongs to this model, so it is the only thing handed over.
        /// </summary>
        private static void BindFalafelRun(
            GameObject instance, Transform counter, Transform talk, Report report)
        {
            var run = instance.AddComponent<FalafelRun>();
            var serialized = new SerializedObject(run);
            serialized.FindProperty("counterPoint").objectReferenceValue = counter;
            serialized.FindProperty("talkPoint").objectReferenceValue = talk;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var faces = new List<GameObject>();
            var missing = new List<string>();
            foreach (var name in FalafelFaces)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Npc/{name}.prefab");
                if (prefab == null) missing.Add(name);
                else faces.Add(prefab);
            }

            run.SetFaces(faces);
            EditorUtility.SetDirty(run);

            if (missing.Count > 0)
                report.Warnings.Add(
                    $"falafel customers - no prefab for {string.Join(", ", missing)}. " +
                    "Run The Block → Build Pedestrians; those drops will be a beacon with nobody under it.");

            if (counter == null)
                report.Warnings.Add(
                    $"{FalafelSpec.ObjectName}: no counter point - rounds cannot be ordered at all");
            if (talk == null)
                report.Warnings.Add(
                    $"{FalafelSpec.ObjectName}: no talk point - the stand gives no sign it is open");
        }
    }
}
