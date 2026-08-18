using System.Collections.Generic;
using System.Linq;
using System.Text;
using TheBlock.Core;
using TheBlock.Npc;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Builds the twelve strangers - <b>The Block → Build Pack Pedestrians</b>. U38.
    ///
    /// <b>Why this is a second builder and not a loop inside <see cref="NpcBuilder"/>.</b> The two
    /// pipelines have almost nothing in common past the capsule. A Mixamo person is one FBX per clip
    /// with its own upload, its own avatar and its own override controller; a pack person is an
    /// assembled prefab of six-to-eight garment meshes sharing one 54-bone skeleton, wearing built-in
    /// <c>Standard</c> materials, under a five-level LODGroup, and it drives the SHARED controller
    /// because it has no clips of its own. Folding that into <c>NpcBuilder</c> would make every line
    /// of it an if.
    ///
    /// <b>The twelve prefabs this overwrites were already there, and every one of them was broken.</b>
    /// They date from U16, before three separate things existed, and none of the three announces its
    /// absence:
    ///  - <b>No kinematic Rigidbody.</b> A moving <see cref="CapsuleCollider"/> with no body is a
    ///    moving STATIC collider, and PhysX rebuilds its static tree for every one of them every
    ///    frame. <see cref="NpcBuilder"/>'s comment calls this the most expensive thing in the unit.
    ///  - <b>No ragdoll</b>, so U35a's run-over would silently fall back to U18's clip reaction.
    ///  - <b><see cref="Pedestrian"/>'s three fields unset</b> - <c>animator</c>, <c>walkClipSpeed</c>,
    ///    <c>voice</c> - while still carrying six serialized fields (<c>stepRadius</c>,
    ///    <c>crossingBias</c>, …) that moved to <see cref="CrowdSpawner"/> units ago. Unity keeps
    ///    orphaned YAML silently, so a stale prefab looks fine in the inspector.
    /// That is why these are REBUILT rather than wired up.
    ///
    /// <b>LOD0 only, and the LODGroup deleted.</b> The pack ships 40 SkinnedMeshRenderers per person -
    /// eight parts at five levels - and per memory <c>lodgroup-does-not-stop-skinning</c> every one of
    /// them is posed every frame whatever the LODGroup is showing. Keeping the <c>_lod0</c> set takes
    /// a body to 6-8 renderers and ~4,700 triangles, which is nothing, and it restores
    /// <see cref="NpcBuilder"/>'s stated invariant that a pedestrian has no LODGroup - the mechanism
    /// behind U16's "exploding pedestrian".
    ///
    /// It also fixes a bug in the pack: <c>02m_01</c>'s LOD0 lists six renderers where LOD1 lists
    /// eight, so at the closest range - the only range you can actually see him - that character has
    /// no hair and no beard, and they pop in as you walk away. Deleting the group gives him his head
    /// back.
    ///
    /// <b>Materials are rebound to <c>MaterialsUPR/</c></b>, the memory
    /// <c>asset-store-prefabs-ship-built-in-materials</c> exactly: the pack's prefabs point at
    /// <c>Materials/</c>, which is <c>Standard</c> - a built-in shader that renders wrong under URP -
    /// and the URP set sits unused beside it under the same file names.
    /// </summary>
    public static class PackPedBuilder
    {
        private const string PackFolder = "Assets/npc_casual_set_00/Prefabs";
        private const string UprFolder = "Assets/npc_casual_set_00/MaterialsUPR";

        /// <summary>Where <c>NpcAppearance</c>'s six tints per garment live, one folder for all twelve.</summary>
        private const string TintFolder = "Assets/Materials/Npc/Generated";

        private const string PrefabFolder = "Assets/Prefabs/Npc";

        /// <summary>
        /// The shared controller. These are not Mixamo uploads, so unlike the six named faces there
        /// is no per-character override to build - they all drive the base directly, and Unity's
        /// Humanoid retargeting is what makes one person's clip fit another person's skeleton.
        /// </summary>
        private const string ControllerPath = "Assets/Animation/Npc.controller";

        /// <summary>
        /// The twelve assembled characters in <c>npc_casual_set_00</c>, by the pack's own code:
        /// body type <c>01</c>/<c>02</c>, gender, outfit. <c>01</c> wears a plain t-shirt, <c>02</c>
        /// an open shirt over one.
        /// </summary>
        public static readonly string[] Names =
        {
            "01f_01", "01f_02", "01f_03", "01m_01", "01m_02", "01m_03",
            "02f_01", "02f_02", "02f_03", "02m_01", "02m_02", "02m_03",
        };

        public static string PrefabPath(string code) => $"{PrefabFolder}/Ped_{code}.prefab";

        [MenuItem("The Block/Build Pack Pedestrians", priority = 22)]
        public static void BuildMenu() => Build();

        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                const string playing =
                    "PackPedBuilder: stop Play mode first - prefabs written during Play are discarded " +
                    "on exit (memory: editor-builds-during-play-are-discarded).";
                Debug.LogError(playing);
                return playing;
            }

            var snapshot = TheBlockConfig.Load(reload: true);
            var npc = snapshot?.Npc;
            if (npc == null)
            {
                const string message =
                    "PackPedBuilder: the config snapshot has no npcConfig section. Run tools/export-config.sh.";
                Debug.LogError(message);
                return message;
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                var message = $"PackPedBuilder: no {ControllerPath}. Run The Block → Build NPC Animator first.";
                Debug.LogError(message);
                return message;
            }

            if (!TryDrawnWalkSpeed(controller, out float drawnWalk, out string walkNote))
            {
                Debug.LogError("PackPedBuilder: " + walkNote);
                return walkNote;
            }

            int layer = NpcBuilder.EnsureLayer(NpcBuilder.PedestrianLayer);
            if (!AssetDatabase.IsValidFolder(PrefabFolder)) AssetDatabase.CreateFolder("Assets/Prefabs", "Npc");

            var log = new StringBuilder();
            int built = 0;

            foreach (var code in Names)
            {
                if (BuildOne(code, npc, controller, drawnWalk, layer, log)) built++;
            }

            AssetDatabase.SaveAssets();

            // U35a's rig is written INTO the prefabs this method has just replaced, so it is rebuilt
            // here rather than left to be remembered - the U34 lesson (memory:
            // static-event-with-no-publisher), where CrashSensor went fourteen units attached to
            // nothing because a builder regenerated a prefab and did not put back what another
            // builder had written into it.
            if (built > 0) log.AppendLine().Append(RagdollBuilder.Build());

            var report =
                $"PackPedBuilder - {built}/{Names.Length} pedestrian prefab(s)\n{log}\n" +
                $"  {walkNote}\n" +
                $"  capsule r {npc.Collider.Radius:0.##} h {2f * (npc.Collider.HalfHeight + npc.Collider.Radius):0.##} " +
                $"on layer '{NpcBuilder.PedestrianLayer}' ({layer})\n" +
                "  The scene's crowd list is NOT touched by this - that is Bake Crowd Seeds' job.";

            if (built < Names.Length) Debug.LogWarning(report);
            else Debug.Log(report);
            return report;
        }

        // --- one body ----------------------------------------------------------------------------

        private static bool BuildOne(
            string code, TheBlockConfig.NpcSpec npc, AnimatorController controller, float drawnWalk,
            int layer, StringBuilder log)
        {
            var sourcePath = $"{PackFolder}/npc_csl_00_character_{code}.prefab";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null)
            {
                log.AppendLine(
                    $"{code,-8} SKIPPED - no {sourcePath}. The pack is gitignored (505 MB against a " +
                    "shared 1 GiB LFS budget); re-import it from the Asset Store to build from a clone.");
                return false;
            }

            // Measured on the BAKED SKIN, not on renderer bounds - PeopleImporter.MeasureHeight is
            // already the corrected one, and memory skinned-bounds-are-the-clips-sweep is what
            // happens with the other: bounds are the clip's whole sweep, which built the crowd 15%
            // short once already. Every pack character measures 1.87-1.90 m, so all twelve scale.
            float measured = PeopleImporter.MeasureHeight(sourcePath);
            float scale = PeopleImporter.HeightScale(measured);

            var root = new GameObject($"Ped_{code}");

            try
            {
                root.layer = layer;

                var visual = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
                visual.name = "Visual";
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;

                // UNPACKED, and it has to be. Everything below - deleting four LOD levels, rebinding
                // nine materials, rescaling - would otherwise be stored as prefab-instance overrides
                // against an asset in an ignored folder, which is both fragile and unreadable in a
                // diff. Unpacked, what is saved is what is there.
                PrefabUtility.UnpackPrefabInstance(
                    visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

                int stripped = StripToLod0(visual);
                int rebound = RebindToUrp(visual);

                visual.transform.localScale = Vector3.one * scale;
                foreach (var child in visual.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = layer;

                var animator = visual.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    log.AppendLine($"{code,-8} SKIPPED - the pack prefab has no Animator");
                    return false;
                }

                animator.runtimeAnimatorController = controller;

                // The crowd script owns movement, so the clip's travel is discarded - NpcBuilder's
                // arrangement, for its reason: baked into the pose it would fight the script.
                animator.applyRootMotion = false;

                // Off-screen, stop posing entirely. Correct for a walker (its transform is
                // script-driven, so it is in the right place when it comes back) and WRONG for a
                // stationary one - the falafel vendor is the same prefab forced to AlwaysAnimate,
                // because a culled Animator leaves the rig in its bind pose, which for this pack is a
                // T (memory: culled-animator-skips-pose-write).
                animator.cullingMode = AnimatorCullingMode.CullCompletely;

                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.radius = npc.Collider.Radius;
                capsule.height = 2f * (npc.Collider.HalfHeight + npc.Collider.Radius);
                capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);

                var body = root.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                body.interpolation = RigidbodyInterpolation.None;
                body.collisionDetectionMode = CollisionDetectionMode.Discrete;

                var pedestrian = root.AddComponent<Pedestrian>();
                pedestrian.Configure(animator, drawnWalk / Mathf.Max(scale, 0.0001f));
                pedestrian.SetVoice(IsFemale(code)
                    ? TheBlock.Audio.ScreamVoice.Female
                    : TheBlock.Audio.ScreamVoice.Male);

                var appearance = root.AddComponent<NpcAppearance>();
                string look = Dress(appearance, visual, code);

                var path = PrefabPath(code);
                PrefabUtility.SaveAsPrefabAsset(root, path);

                log.AppendLine(
                    $"{code,-8} {measured:0.00} m × {scale:0.###} → 1.70 m | " +
                    $"{visual.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length} skinned mesh(es) " +
                    $"(−{stripped} LOD copies, no LODGroup) | {rebound} material(s) → URP | " +
                    $"{(IsFemale(code) ? "female" : "male")} | {look}");
                return true;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static bool IsFemale(string code) => code.Contains("f");

        // --- the three passes ---------------------------------------------------------------------

        /// <summary>
        /// Deletes every renderer that is not <c>_lod0</c>, and the LODGroup with them. Returns how
        /// many went.
        /// </summary>
        private static int StripToLod0(GameObject visual)
        {
            int removed = 0;

            foreach (var group in visual.GetComponentsInChildren<LODGroup>(true))
                Object.DestroyImmediate(group);

            foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin == null || skin.name.EndsWith("_lod0")) continue;
                Object.DestroyImmediate(skin.gameObject);
                removed++;
            }

            return removed;
        }

        /// <summary>
        /// Swaps every <c>Materials/</c> material for the <c>MaterialsUPR/</c> one of the same name.
        /// A miss is left alone rather than nulled - a null slot renders magenta, and a Standard
        /// material at least renders.
        /// </summary>
        private static int RebindToUrp(GameObject visual)
        {
            int rebound = 0;

            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < materials.Length; i++)
                {
                    var current = materials[i];
                    if (current == null) continue;

                    var urp = AssetDatabase.LoadAssetAtPath<Material>($"{UprFolder}/{current.name}.mat");
                    if (urp == null || urp == current) continue;

                    materials[i] = urp;
                    changed = true;
                    rebound++;
                }

                if (changed) renderer.sharedMaterials = materials;
            }

            return rebound;
        }

        /// <summary>
        /// Wires <see cref="NpcAppearance"/>: the five faces of this character's gender, and the six
        /// tinted copies of whatever they are wearing on top. Thirty looks per body, 360 across the
        /// twelve, for two shared-material assignments per spawn.
        ///
        /// <b>The face is always slot 1 of <c>*_head2_lod0</c></b> - slot 0 is the body skin, which is
        /// shared with the arms and must not be touched. Verified on all twelve.
        ///
        /// <b>The top is the garment with tints on disk, preferring the outer one.</b> Two characters
        /// (<c>02f_03</c>, <c>02m_02</c>) have a tint family for BOTH the open shirt and the t-shirt
        /// under it; recolouring the under-layer would repaint a triangle of chest nobody reads as a
        /// shirt. Excluding <c>frontal</c> - the pack's own name for that under-layer - picks the
        /// outer garment on both.
        /// </summary>
        private static string Dress(NpcAppearance appearance, GameObject visual, string code)
        {
            var skins = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            var heads = skins.Where(s => s.name.Contains("_head2")).ToArray();
            var slots = heads.Select(_ => 1).ToArray();

            string gender = IsFemale(code) ? "01f" : "01m";
            var faces = new List<Material>();
            for (int i = 1; i <= 5; i++)
            {
                var face = AssetDatabase.LoadAssetAtPath<Material>(
                    $"{UprFolder}/mtl_npc_hmn_face_{gender}_0{i}_01.mat");
                if (face != null) faces.Add(face);
            }

            SkinnedMeshRenderer top = null;
            var tints = new List<Material>();

            foreach (var skin in skins)
            {
                if (skin.name.Contains("frontal")) continue;

                var material = skin.sharedMaterial;
                if (material == null) continue;

                var family = Tints(material.name);
                if (family.Count == 0) continue;

                top = skin;
                tints = family;
                break;
            }

            appearance.Configure(
                faces.ToArray(), heads, slots, tints.ToArray(),
                top != null ? new Renderer[] { top } : System.Array.Empty<Renderer>());

            return $"{faces.Count} face(s) × {tints.Count} tint(s) = {faces.Count * Mathf.Max(tints.Count, 1)} looks" +
                   (top != null ? $" on {top.name}" : "  ⚠ no tinted garment found");
        }

        /// <summary>The six <c>_t0</c>…<c>_t5</c> copies of a garment material, or empty if it has none.</summary>
        private static List<Material> Tints(string materialName)
        {
            var found = new List<Material>();
            for (int i = 0; i < 6; i++)
            {
                var tint = AssetDatabase.LoadAssetAtPath<Material>($"{TintFolder}/{materialName}_t{i}.mat");
                if (tint == null) break;
                found.Add(tint);
            }

            return found.Count == 6 ? found : new List<Material>();
        }

        // --- the walk ------------------------------------------------------------------------------

        /// <summary>
        /// How fast the shared controller's walk clip travels <b>as drawn at 1.70 m</b>, in m/s.
        ///
        /// <see cref="Pedestrian"/> sets its Speed parameter as <c>animSpeed / (walkClipSpeed ×
        /// visual scale)</c>, and <c>walkClipSpeed</c> is in the avatar's OWN units - which is why a
        /// number measured on the clip's original owner cannot simply be copied onto a body of a
        /// different height. Measuring the owner's drawn speed once and dividing by each pack
        /// character's own scale gives every body the same ground speed per stride, which is the
        /// thing that stops feet sliding.
        ///
        /// The owner is read off the controller rather than named here, so re-pointing the base
        /// controller at a different walk does not silently leave this measuring the old one.
        /// </summary>
        private static bool TryDrawnWalkSpeed(AnimatorController controller, out float drawn, out string note)
        {
            drawn = 0f;

            var walk = controller.animationClips?.FirstOrDefault(c => c != null && c.name.EndsWith("_Walk"));
            if (walk == null)
            {
                note = $"{ControllerPath} has no *_Walk clip, so there is no cadence to measure.";
                return false;
            }

            string owner = walk.name.Substring(0, walk.name.Length - "_Walk".Length);
            float ownerHeight = PeopleImporter.MeasureHeight(PeopleImporter.IdlePath(owner));
            float ownerScale = PeopleImporter.HeightScale(ownerHeight);
            float clipSpeed = NpcAnimatorBuilder.WalkSpeed(walk);

            drawn = clipSpeed * ownerScale;
            note =
                $"walk cadence from '{walk.name}' ({owner} {ownerHeight:0.00} m × {ownerScale:0.###}): " +
                $"{clipSpeed:0.00} clip units/s → {drawn:0.00} m/s drawn";
            return true;
        }
    }
}
