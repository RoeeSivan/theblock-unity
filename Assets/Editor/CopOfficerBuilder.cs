using System.Collections.Generic;
using System.Linq;
using System.Text;
using TheBlock.Police;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;
using Debug = UnityEngine.Debug;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Builds the officer who drives the cruiser — <b>The Block → Build Cop Officer</b>.
    ///
    /// <b>The asset is a free Asset Store character and it arrives in three pieces of trouble.</b>
    /// Its 459 MB of 2048² .tga is LFS-bound in a repo whose free quota is already shared with the
    /// original project (see the note in <c>.gitignore</c>), its materials are built-in Standard and
    /// therefore magenta under URP (memory <c>asset-store-prefabs-ship-built-in-materials</c>), and
    /// it ships no animation at all. So this builder reads a SLIM copy — the 2.3 MB FBX and 1024²
    /// PNG twins of its textures, both committable — rebinds every material onto URP/Lit, and
    /// borrows Joe's clips, which retarget because both rigs are Humanoid. Nothing is downloaded and
    /// nothing new goes into LFS.
    ///
    /// <b>Two clips do the whole job.</b> <c>Joe_EnterCar</c> held at its last frame IS the seated
    /// pose — the same trick the player's own driving uses, and the reason no car in this project
    /// needs a driving clip — and <c>Joe_Sprint</c>, retimed to her ground speed exactly as
    /// <see cref="ThiefBuilder"/> retimes it for the thief, is the run.
    /// </summary>
    public static class CopOfficerBuilder
    {
        public const string PrefabPath = "Assets/Prefabs/Police/CopOfficer.prefab";

        private const string ModelPath = "Assets/Models/Characters/Officer/police_officer.fbx";
        private const string TextureFolder = "Assets/Models/Characters/Officer/Textures";
        private const string MaterialFolder = "Assets/Materials/CopOfficer";
        private const string ControllerPath = "Assets/Animation/CopOfficer.controller";
        private const string PrefabFolder = "Assets/Prefabs/Police";

        // Joe's clips. Humanoid, so they retarget onto her avatar with nothing imported.
        private const string JoeIdlePath = "Assets/Models/Characters/Joe.fbx";
        private const string JoeIdleClip = "Joe_Idle";
        private const string JoeSprintPath = "Assets/Models/Characters/Joe_Sprint.fbx";
        private const string JoeSprintClip = "Joe_Sprint";
        private const string JoeEnterPath = "Assets/Models/Characters/Joe_EnterCar.fbx";
        private const string JoeEnterClip = "Joe_EnterCar";

        /// <summary>Her ground speed. Must match <c>PoliceTuning.OfficerSpeed</c> — the stride is cut to it.</summary>
        private const float RunSpeed = 6.2f;

        /// <summary>Body capsule. A person, not a barrel: the crowd's own is 0.3 m at 1.7 m tall.</summary>
        private const float CapsuleRadius = 0.32f;

        private const float CapsuleHeight = 1.85f;

        /// <summary>
        /// One material's three maps, by the file names in the pack.
        ///
        /// Written out rather than derived because the pack's own naming cannot be derived from:
        /// the torso's normal is <c>T_policeofficerl_torso_Normal</c> with an extra l, its albedo is
        /// <c>T_policeofficer_torso_AlbedoTransparency</c> without, and the pistol's three are
        /// prefixed <c>M_</c>. A rule that covered all seven would be longer than the table and
        /// would fail silently on the eighth.
        /// </summary>
        private readonly struct Skin
        {
            public readonly string Albedo;
            public readonly string Normal;
            public readonly string Mask;
            public readonly bool TwoSided;
            public readonly bool Cutout;

            public Skin(string albedo, string normal, string mask, bool twoSided, bool cutout = false)
            {
                Albedo = albedo;
                Normal = normal;
                Mask = mask;
                TwoSided = twoSided;
                Cutout = cutout;
            }
        }

        private static readonly Dictionary<string, Skin> Skins = new(System.StringComparer.Ordinal)
        {
            ["M_policeofficer_cap"] = new Skin(
                "T_policeofficer_cap_AlbedoTransparency", "T_policeofficerl_cap_Normal",
                "T_policeofficer_cap_MetallicSmoothness", true),

            // The one cutout. Hair is planes with alpha, and opaque planes are a helmet.
            ["M_policeofficer_hair"] = new Skin(
                "T_policeofficer_hair_AlbedoTransparency", "T_policeofficer_hair_Normal",
                "T_policeofficerl_hair_MetallicSmoothness", true, cutout: true),

            ["M_policeofficer_head"] = new Skin(
                "T_policeofficer_head_AlbedoTransparency", "T_policeofficer_head_Normal",
                "T_policeofficer_head_MetallicSmoothness", true),

            ["M_policeofficer_leg"] = new Skin(
                "T_policeofficer_leg_AlbedoTransparency", "T_policeofficerl_leg_Normal",
                "T_policeofficer_leg_MetallicSmoothness", false),

            ["M_policeofficer_torso"] = new Skin(
                "T_policeofficer_torso_AlbedoTransparency", "T_policeofficerl_torso_Normal",
                "T_policeofficerl_torso_MetallicSmoothness", true),

            // The pack ships the eye as Fade at queue 3000. It is a ball inside a head that never
            // shows its back — transparent buys nothing and sorts against the hair every frame.
            ["M_policeofficerl_Eye"] = new Skin(
                "T_policeofficer_Eye_AlbedoTransparency", "T_policeofficer_Eye_Normal", null, false),

            ["M_policeofficerl_pistolet"] = new Skin(
                "M_policeofficerl_pistolet_AlbedoTransparency", "M_policeofficer_pistolet_Normal",
                "M_policeofficer_pistolet_MetallicSmoothness", false),
        };

        [MenuItem("The Block/Build Cop Officer", priority = 26)]
        public static void BuildMenu() => Build();

        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                const string busy = "CopOfficerBuilder: stop Play mode first.";
                Debug.LogError(busy);
                return busy;
            }

            var log = new StringBuilder();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                var missing = $"CopOfficerBuilder — FAILED: no model at {ModelPath}.";
                Debug.LogError(missing);
                return missing;
            }

            ImportModel(log);
            ImportTextures(log);
            var materials = BuildMaterials(log);
            var controller = BuildController(log);
            var prefab = BuildPrefab(model, materials, controller, log);

            AssetDatabase.SaveAssets();

            var report = $"CopOfficerBuilder — {(prefab == null ? "FAILED" : PrefabPath)}\n{log}";
            if (prefab == null) Debug.LogError(report);
            else Debug.Log(report, prefab);
            return report;
        }

        // --- import ----------------------------------------------------------------------------

        /// <summary>
        /// Humanoid, its own avatar, and no materials of its own.
        ///
        /// <b>Its own avatar, never a copy of Joe's.</b> Copy From Other needs identical bone names
        /// and hers are not Mixamo's (memory <c>copy-avatar-needs-same-bone-names</c>) — but two
        /// valid Humanoid avatars retarget to each other regardless, which is the whole reason
        /// Joe's clips work on her. <c>materialImportMode = None</c> because the pack's are built-in
        /// Standard; the URP twins below are bound by name onto the renderers instead.
        /// </summary>
        private static void ImportModel(StringBuilder log)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            bool dirty = false;

            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                dirty = true;
            }

            if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                dirty = true;
            }

            if (importer.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                dirty = true;
            }

            if (importer.importAnimation)
            {
                importer.importAnimation = false;
                dirty = true;
            }

            if (dirty)
            {
                importer.SaveAndReimport();
                log.AppendLine("        model   re-imported Humanoid, own avatar, no materials");
            }
            else
            {
                log.AppendLine("        model   already Humanoid with its own avatar");
            }

            var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            log.AppendLine(avatar == null
                ? "        CHECK   NO AVATAR — Joe's clips cannot retarget onto her"
                : $"        CHECK   avatar '{avatar.name}' valid={avatar.isValid} human={avatar.isHuman}");
        }

        /// <summary>
        /// Sets the texture type on each map, which is the difference between a normal map and a
        /// blue photograph.
        ///
        /// The masks go LINEAR, and that is not cosmetic: a metallic/smoothness map read as sRGB is
        /// gamma-decoded on sample, so every rough surface reads glossier than it is.
        /// </summary>
        private static void ImportTextures(StringBuilder log)
        {
            int normals = 0;
            int linear = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TextureFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                bool isNormal = path.EndsWith("_Normal.png", System.StringComparison.Ordinal);
                bool isMask = path.EndsWith("_MetallicSmoothness.png", System.StringComparison.Ordinal);
                bool dirty = false;

                var wanted = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (importer.textureType != wanted)
                {
                    importer.textureType = wanted;
                    dirty = true;
                }

                if (!isNormal && importer.sRGBTexture == isMask)
                {
                    importer.sRGBTexture = !isMask;
                    dirty = true;
                }

                if (importer.maxTextureSize > 1024)
                {
                    importer.maxTextureSize = 1024;
                    dirty = true;
                }

                if (!dirty) continue;

                importer.SaveAndReimport();
                if (isNormal) normals++;
                if (isMask) linear++;
            }

            log.AppendLine($"        texture {normals} re-imported as normal maps, {linear} as linear masks");
        }

        // --- materials -------------------------------------------------------------------------

        private static Dictionary<string, Material> BuildMaterials(StringBuilder log)
        {
            VehicleMaterials.EnsureFolder(MaterialFolder);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var built = new Dictionary<string, Material>(System.StringComparer.Ordinal);

            if (lit == null)
            {
                log.AppendLine("        MISSING URP/Lit shader — nothing rebound, she will be magenta");
                return built;
            }

            foreach (var pair in Skins)
            {
                var path = $"{MaterialFolder}/{pair.Key}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (material == null)
                {
                    material = new Material(lit);
                    AssetDatabase.CreateAsset(material, path);
                }

                material.shader = lit;
                Bind(material, pair.Value, log, pair.Key);
                EditorUtility.SetDirty(material);
                built[pair.Key] = material;
            }

            log.AppendLine($"        material {built.Count} URP/Lit written to {MaterialFolder}");
            return built;
        }

        private static void Bind(Material material, Skin skin, StringBuilder log, string name)
        {
            material.SetTexture("_BaseMap", Texture(skin.Albedo, log, name));

            var normal = Texture(skin.Normal, log, name);
            material.SetTexture("_BumpMap", normal);
            if (normal != null) material.EnableKeyword("_NORMALMAP");

            var mask = skin.Mask == null ? null : Texture(skin.Mask, log, name);
            material.SetTexture("_MetallicGlossMap", mask);

            if (mask != null)
            {
                // Metallic in R, smoothness in A — the Standard packing the pack authored to, and
                // URP/Lit reads the same one as long as the channel is left on metallic alpha.
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                material.SetFloat("_SmoothnessTextureChannel", 0f);
                material.SetFloat("_Metallic", 1f);
                material.SetFloat("_Smoothness", 1f);
            }
            else
            {
                material.SetFloat("_Metallic", 0f);
                material.SetFloat("_Smoothness", 0.3f);
            }

            // Cull Off is a MATERIAL property in URP, not a shader variant: the pack's own
            // Standard(2sided) is a hand-edited copy of Standard whose only change is this.
            material.SetFloat("_Cull", skin.TwoSided ? 0f : 2f);
            material.doubleSidedGI = skin.TwoSided;

            if (skin.Cutout)
            {
                material.SetFloat("_AlphaClip", 1f);
                material.SetFloat("_Cutoff", 0.5f);
                material.EnableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }
            else
            {
                material.SetFloat("_AlphaClip", 0f);
                material.DisableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            }
        }

        private static Texture2D Texture(string fileName, StringBuilder log, string owner)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/{fileName}.png");
            if (texture == null) log.AppendLine($"        MISSING {owner}: no texture {fileName}.png");
            return texture;
        }

        // --- animation -------------------------------------------------------------------------

        /// <summary>
        /// Two states and one float. <c>Sit</c> is the entry clip, which <see cref="CopOfficer"/>
        /// plays at normalized time 1 and never transitions into; <c>Gait</c> is idle-to-sprint on
        /// <c>Speed</c>.
        ///
        /// <b>The stride is computed, not typed</b>, exactly as the thief's is: <c>Joe_Sprint</c>
        /// carries 5.58 m/s of root motion and she covers <see cref="RunSpeed"/>, so the child's
        /// <c>timeScale</c> is that ratio and her feet stay on the ground she is actually crossing.
        /// </summary>
        private static AnimatorController BuildController(StringBuilder log)
        {
            var idle = Clip(JoeIdlePath, JoeIdleClip);
            var sprint = Clip(JoeSprintPath, JoeSprintClip);
            var enter = Clip(JoeEnterPath, JoeEnterClip);

            if (idle == null || sprint == null || enter == null)
            {
                log.AppendLine($"        MISSING clips: idle={idle != null} sprint={sprint != null} " +
                               $"enterCar={enter != null} — no controller written");
                return null;
            }

            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

            var machine = controller.layers[0].stateMachine;

            var sit = machine.AddState("Sit");
            sit.motion = enter;
            sit.writeDefaultValues = false;
            machine.defaultState = sit;

            var tree = new BlendTree
            {
                name = "Gait",
                blendParameter = "Speed",
                blendType = BlendTreeType.Simple1D,
                useAutomaticThresholds = false,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.AddChild(idle, 0f);
            tree.AddChild(sprint, 1f);

            float clipSpeed = sprint.averageSpeed.magnitude;
            float stride = clipSpeed > 0.01f ? RunSpeed / clipSpeed : 1f;
            var children = tree.children;
            children[1].timeScale = stride;
            tree.children = children;

            var gait = machine.AddState("Gait");
            gait.motion = tree;
            gait.writeDefaultValues = false;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            log.AppendLine($"        anim    Sit = {enter.name} ({enter.length:0.00} s, held at 1.0), " +
                           $"Gait = {idle.name}→{sprint.name} at {clipSpeed:0.00} m/s × {stride:0.000} " +
                           $"= {RunSpeed:0.0} m/s");
            return controller;
        }

        private static AnimationClip Clip(string path, string name) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);

        // --- prefab ----------------------------------------------------------------------------

        private static GameObject BuildPrefab(
            GameObject model, Dictionary<string, Material> materials, AnimatorController controller,
            StringBuilder log)
        {
            VehicleMaterials.EnsureFolder(PrefabFolder);
            var root = new GameObject("CopOfficer");

            try
            {
                int layer = LayerMask.NameToLayer("Pedestrian");
                if (layer < 0) layer = 0;
                root.layer = layer;

                var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                visual.name = "Visual";
                visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                foreach (var child in visual.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = layer;

                int rebound = Rebind(visual, materials, log);

                var animator = visual.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    log.AppendLine("        FAILED  the imported model has no Animator");
                    return null;
                }

                animator.runtimeAnimatorController = controller;

                // The agent owns her position; root motion on top would fight it, exactly as it
                // would for the crowd and for the thief.
                animator.applyRootMotion = false;

                // ⚠ MEASURED, and it cost the first build. Under the FBX's own default
                // (CullUpdateTransforms) an Animator whose renderers are INVISIBLE evaluates but
                // does not write the pose to the bones — and the cruisers are parked at the station,
                // off-camera, at the exact moment PoliceSystem seats her. So the sit pose silently
                // did nothing: her hips read car-local x -2.30, standing beside the door, when the
                // seat is at -0.38. Under AlwaysAnimate the same call writes it. It costs nothing
                // here, because a seated officer's Animator is switched OFF the instant the pose is
                // written and only a deployed one runs at all.
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                var agent = root.AddComponent<NavMeshAgent>();
                agent.speed = RunSpeed;
                agent.angularSpeed = 720f;
                agent.acceleration = 16f;
                agent.radius = CapsuleRadius;
                agent.height = CapsuleHeight;
                agent.stoppingDistance = 0.8f;
                agent.autoBraking = false;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
                agent.avoidancePriority = 45;
                agent.enabled = false;

                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.radius = CapsuleRadius;
                capsule.height = CapsuleHeight;
                capsule.center = new Vector3(0f, CapsuleHeight * 0.5f, 0f);
                capsule.enabled = false;

                // The crowd's own note applies word for word: a moving collider with NO Rigidbody
                // is a moving STATIC collider, and PhysX rebuilds its static tree for every one of
                // them every frame. Kinematic also means she rides the car she is parented to.
                var body = root.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                body.interpolation = RigidbodyInterpolation.None;
                body.collisionDetectionMode = CollisionDetectionMode.Discrete;

                var officer = root.AddComponent<CopOfficer>();
                officer.SetParts(animator, agent, capsule, RunSpeed);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);

                log.AppendLine(
                    $"        prefab  {visual.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length} " +
                    $"skinned mesh(es), {rebound} material(s) rebound, layer '{LayerMask.LayerToName(layer)}', " +
                    $"capsule {CapsuleRadius:0.00} × {CapsuleHeight:0.00} m");
                return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Points every renderer at its URP twin, matched on the pack's own material names.
        ///
        /// With <c>materialImportMode = None</c> the imported slots are NULL, and a null slot draws
        /// magenta with no error of any kind — the same silence the districts' empty glTF slots
        /// produce (memory <c>gltf-empty-material-slot-magenta</c>). So a miss is counted and said
        /// out loud rather than left to the play-test.
        /// </summary>
        private static int Rebind(GameObject visual, Dictionary<string, Material> materials, StringBuilder log)
        {
            int bound = 0;

            foreach (var renderer in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var slots = renderer.sharedMaterials;

                for (int i = 0; i < slots.Length; i++)
                {
                    // The slot itself is null, so the name has to come from somewhere else: the
                    // renderer's own. Sk_Police_girl_torso wants _torso, and the sub-mesh order
                    // within a renderer is the pack's own material order.
                    var wanted = Match(renderer.name, i);
                    if (wanted == null || !materials.TryGetValue(wanted, out var material))
                    {
                        log.AppendLine($"        MISS    {renderer.name}[{i}] — no material matched");
                        continue;
                    }

                    slots[i] = material;
                    bound++;
                }

                renderer.sharedMaterials = slots;
            }

            return bound;
        }

        /// <summary>
        /// Which of the seven materials a renderer's slot wants.
        ///
        /// The head is the only renderer with two, and its second is the eye — that is the whole
        /// special case, and it is stated rather than guessed at.
        /// </summary>
        private static string Match(string rendererName, int slot)
        {
            var lower = rendererName.ToLowerInvariant();

            if (lower.EndsWith("head")) return slot == 0 ? "M_policeofficer_head" : "M_policeofficerl_Eye";
            if (lower.EndsWith("cap")) return "M_policeofficer_cap";
            if (lower.EndsWith("hair")) return "M_policeofficer_hair";
            if (lower.EndsWith("leg")) return "M_policeofficer_leg";
            if (lower.EndsWith("torso")) return "M_policeofficer_torso";
            if (lower.EndsWith("pistolet")) return "M_policeofficerl_pistolet";

            return null;
        }
    }
}
