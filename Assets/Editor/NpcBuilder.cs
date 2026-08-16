using System.Linq;
using System.Text;
using TheBlock.Core;
using TheBlock.Npc;
using UnityEditor;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Builds the six pedestrian prefabs — <b>The Block → Build Pedestrians</b>.
    ///
    /// One per character imported by <see cref="PeopleImporter"/>: the body, its own animator
    /// override, the capsule <c>npc.config.ts</c> specifies, and nothing else.
    ///
    /// <b>What this file no longer does is the whole point of U16b.</b> The vendor pack it used to
    /// build from shipped six body parts at five LOD levels — 33 SkinnedMeshRenderers per person —
    /// and every one of them was posed every frame whether or not it was drawn. Stripping that back
    /// to two levels was U16's perf fix, and the leftover mechanism was still the "exploding
    /// pedestrian": a renderer that had never been posed, swapped in by a LOD change, drawing its
    /// first frame at bind pose against a skeleton that had walked off — a fan of black triangles
    /// reaching for the horizon. <b>These rigs have no LODGroup and one or two renderers each, so
    /// that cannot happen.</b> Do not add LODs back without reading this paragraph twice.
    ///
    /// Three more things it deliberately does not do:
    ///  - <b>No material work.</b> Mixamo FBX imported by Unity's own importer produce URP/Lit
    ///    materials with their base and normal maps already bound (verified on all six). The
    ///    <c>MaterialsUPR</c> rebinding dance is a property of Asset Store PREFABS, not of this
    ///    pipeline.
    ///  - <b>No appearance randomiser.</b> The vendor pack had one head mesh and swappable faces;
    ///    these are six different people, which is what the original ships.
    ///  - <b>No scale on the root.</b> A character that did not import at 1.70 m is corrected on the
    ///    VISUAL CHILD, because scaling the root scales the physics capsule with it — and the capsule
    ///    is the one dimension the config actually specifies.
    /// </summary>
    public static class NpcBuilder
    {
        private const string PrefabFolder = "Assets/Prefabs/Npc";

        /// <summary>Where a pedestrian's collider lives, so the ground probe can ignore it.</summary>
        internal const string PedestrianLayer = "Pedestrian";

        [MenuItem("The Block/Build Pedestrians", priority = 22)]
        public static void BuildMenu() => Build();

        public static string Build()
        {
            var snapshot = TheBlockConfig.Load(reload: true);
            var npc = snapshot?.Npc;
            if (npc == null)
            {
                const string message =
                    "NpcBuilder: the config snapshot has no npcConfig section. Run tools/export-config.sh.";
                Debug.LogError(message);
                return message;
            }

            int layer = EnsureLayer(PedestrianLayer);
            if (!AssetDatabase.IsValidFolder(PrefabFolder))
                AssetDatabase.CreateFolder("Assets/Prefabs", "Npc");

            var log = new StringBuilder();
            int built = 0;

            foreach (var name in PeopleImporter.Names)
            {
                if (BuildOne(name, npc, layer, log)) built++;
            }

            AssetDatabase.SaveAssets();

            var report =
                $"NpcBuilder — {built}/{PeopleImporter.Names.Length} pedestrian prefab(s)\n{log}" +
                $"  capsule r {npc.Collider.Radius:0.##} h {2f * (npc.Collider.HalfHeight + npc.Collider.Radius):0.##} " +
                $"on layer '{PedestrianLayer}' ({layer})\n" +
                "  Drag these onto the Crowd object's Pedestrian Prefabs list, or run Bake Crowd Seeds";
            Debug.Log(report);
            return report;
        }

        private static bool BuildOne(string name, TheBlockConfig.NpcSpec npc, int layer, StringBuilder log)
        {
            var modelPath = PeopleImporter.IdlePath(name);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                log.AppendLine($"{name,-11} SKIPPED — no {modelPath}. Run Import People (slow) first.");
                return false;
            }

            var over = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                NpcAnimatorBuilder.OverridePath(name));
            if (over == null)
            {
                log.AppendLine($"{name,-11} SKIPPED — no override controller. Run Build NPC Animator first.");
                return false;
            }

            var root = new GameObject($"Ped_{name}");

            try
            {
                root.layer = layer;

                var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                visual.name = "Visual";
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;

                // Mixamo characters already face +Z in both engines, and these are FBX read by
                // Unity's own importer rather than glTFast — so no ModelFacing and no X negation.
                // The handedness rule governs hand-authored config numbers and .glb mesh data.
                float measured = PeopleImporter.MeasureHeight(modelPath);
                float scale = PeopleImporter.HeightScale(measured);
                visual.transform.localScale = Vector3.one * scale;

                foreach (var child in visual.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;

                var animator = visual.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    log.AppendLine($"{name,-11} SKIPPED — the imported model has no Animator");
                    return false;
                }

                animator.runtimeAnimatorController = over;

                // The crowd script owns movement, so the clip's travel is discarded — the same
                // arrangement PlayerAnimator uses. Baked into the pose it would fight the script.
                animator.applyRootMotion = false;

                // Off-screen, stop posing entirely. The pedestrian keeps walking because its
                // transform is script-driven, so it is in the right place when it comes back into
                // view; only the limbs pause. U16 tried AlwaysAnimate as a fix for the exploding
                // pedestrian, doubled the cost and fixed nothing — the fault was elsewhere.
                animator.cullingMode = AnimatorCullingMode.CullCompletely;

                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.radius = npc.Collider.Radius;
                capsule.height = 2f * (npc.Collider.HalfHeight + npc.Collider.Radius);
                capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);

                // The port of the web's kinematicPositionBased body. A moving collider with NO
                // Rigidbody is a moving STATIC collider, and PhysX rebuilds its static tree for every
                // one of them every frame — across a live crowd that is the most expensive thing in
                // the unit. (U16's comment forbidding a Rigidbody protected Crossing.IsClearOfTraffic,
                // which U17 deleted.)
                var body = root.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                body.interpolation = RigidbodyInterpolation.None;
                body.collisionDetectionMode = CollisionDetectionMode.Discrete;

                var walkClip = AssetDatabase.LoadAllAssetsAtPath(PeopleImporter.WalkPath(name))
                    .OfType<AnimationClip>()
                    .FirstOrDefault(c => c.name == PeopleImporter.WalkClip(name));

                float clipSpeed = NpcAnimatorBuilder.WalkSpeed(walkClip);

                var pedestrian = root.AddComponent<Pedestrian>();
                pedestrian.Configure(animator, clipSpeed);

                // npcConfig's `gender` reads to exactly one thing in the original — which pool this
                // face screams from when a car hits it — and this is where it gets baked in. The six
                // names in PeopleImporter.Names and the six in npcConfig.people are the same six, so
                // the match is by name and a miss is worth saying out loud.
                var person = npc.People?.FirstOrDefault(p => p != null && p.Name == name);
                if (person == null)
                    log.AppendLine($"{name,-11} note — no npcConfig.people entry, so it screams male");
                pedestrian.SetVoice(person != null && person.Gender == "f"
                    ? TheBlock.Audio.ScreamVoice.Female
                    : TheBlock.Audio.ScreamVoice.Male);

                var path = $"{PrefabFolder}/Ped_{name}.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path);

                log.AppendLine(
                    $"{name,-11} {measured:0.00} m × {scale:0.###} → 1.70 m | " +
                    $"{visual.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length} skinned mesh(es), " +
                    $"no LODGroup | walk clip {clipSpeed:0.00} m/s → {path}");
                return true;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Finds or creates a project layer by name.
        ///
        /// The crowd needs one so its ground probe can ignore other pedestrians — without it people
        /// sample each other's capsules and stack up.
        /// </summary>
        private static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;

            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset").FirstOrDefault();
            if (asset == null)
            {
                Debug.LogWarning($"NpcBuilder: could not open TagManager to add the '{name}' layer.");
                return 0;
            }

            var tagManager = new SerializedObject(asset);
            var layers = tagManager.FindProperty("layers");

            // 0-7 are Unity's own. 8 upwards is the user range.
            for (int i = 8; i < layers.arraySize; i++)
            {
                var element = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(element.stringValue)) continue;

                element.stringValue = name;
                tagManager.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                return i;
            }

            Debug.LogWarning($"NpcBuilder: no free layer slot for '{name}'. Pedestrians stay on Default.");
            return 0;
        }
    }
}
