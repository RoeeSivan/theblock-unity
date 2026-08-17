using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Imports the six street characters - <b>The Block → Import People (slow)</b>.
    ///
    /// These are the original game's own crowd: Sophie, Remy, Elizabeth, Chinese, Peter and Lewis,
    /// the same six Mixamo people <c>npc.config.ts</c> names, brought in from the FBX they were
    /// exported from rather than from the shipped GLBs (port rule 3). ~576 MB of source, so this
    /// takes minutes and the MCP bridge will drop and reconnect while it runs.
    ///
    /// <b>This is <see cref="JoeClipImporter"/>'s recipe widened from one avatar to six.</b> Joe is
    /// one body plus a shelf of borrowed clips, so every clip copies HIS avatar. Here each character
    /// is a different Mixamo body with its own skeleton proportions, so each idle FBX CREATES an
    /// avatar and only that character's walk copies it. Copying one shared avatar across six
    /// different bodies is the mistake that produces a crowd of subtly broken skeletons.
    ///
    /// Per character:
    ///  - <b>Idle FBX carries the mesh.</b> Humanoid, Create From This Model. Humanoid is also what
    ///    makes Mixamo's per-upload <c>mixamorigN:</c> bone namespace a non-problem - the web build
    ///    had to rename tracks by hand to work around it.
    ///  - <b>Walk FBX is used for its clip only.</b> Humanoid, Copy From Other → that character's own
    ///    avatar. Its mesh is imported and never referenced, exactly as <c>Joe_Driving.fbx</c>'s is.
    ///  - <b>Bake Into Pose OFF on both.</b> These are locomotion cycles the crowd script drives, so
    ///    the travel is extracted and then discarded (<c>applyRootMotion = false</c>) - the
    ///    <c>Joe_Swim</c> arrangement, not the <c>Joe_EnterCar</c> one. It also matters mechanically:
    ///    with the root baked, <c>AnimationClip.averageSpeed</c> reads ~0 and the animator builder
    ///    has no cadence to calibrate against.
    ///  - <b>Optimize Game Objects OFF - reversed by U35a, and the old note called its own shot.</b>
    ///    It used to be ON, with the reasoning "Joe has it off because gameplay hunts his bones (the
    ///    driver anchor, the seat); nothing hunts a pedestrian's bones until U18". Ragdolls are what
    ///    finally hunt them: eleven Rigidbodies need eleven bone TRANSFORMS, and Optimize Game
    ///    Objects is precisely the option that deletes them. See the reversal at the setting itself
    ///    for why exposing a few bones instead does not work.
    ///  - <b>Textures extracted, then bound by hand.</b> Unity does neither for a Mixamo FBX and the
    ///    character renders white; extraction is also the only way the textures get a
    ///    <c>TextureImporter</c> at all, which is what compresses them (the same fault U15 found
    ///    from the glTFast side).
    ///  - <b>Height normalised to 1.70 m</b> on the IMPORTER, never on the prefab root - a scaled
    ///    root scales the physics capsule with it. The web build auto-normalises for the same
    ///    reason, and its comment names Remy specifically as the odd export.
    /// </summary>
    public static class PeopleImporter
    {
        private const string PeoplePath = "Assets/Models/Characters/People";

        /// <summary>What the web build normalises every character to (<c>TARGET_HEIGHT_M</c>).</summary>
        private const float TargetHeight = 1.70f;

        /// <summary>Past this much error the scale is corrected and the file reimported.</summary>
        private const float HeightTolerance = 0.02f;

        /// <summary>A pedestrian is never examined up close. 1024² keeps the whole set near 34 MB.</summary>
        private const int MaxTextureSize = 1024;

        /// <summary>The six faces, in <c>npc.config.ts</c>'s own order - which decides who is who.</summary>
        internal static readonly string[] Names =
            { "Sophie", "Remy", "Elizabeth", "Chinese", "Peter", "Lewis" };

        internal static string IdlePath(string name) => $"{PeoplePath}/{name}_Idle.fbx";

        internal static string WalkPath(string name) => $"{PeoplePath}/{name}_Walk.fbx";

        internal static string IdleClip(string name) => $"{name}_Idle";

        internal static string WalkClip(string name) => $"{name}_Walk";

        [MenuItem("The Block/Import People (slow)", priority = 20)]
        public static void ImportMenu() => Import();

        public static string Import()
        {
            var log = new StringBuilder();
            int done = 0;

            try
            {
                for (int i = 0; i < Names.Length; i++)
                {
                    var name = Names[i];
                    EditorUtility.DisplayProgressBar(
                        "Import People", $"{name} ({i + 1}/{Names.Length})", i / (float)Names.Length);

                    if (ImportOne(name, log)) done++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();

            var report = $"PeopleImporter - {done}/{Names.Length} character(s)\n{log}";
            Debug.Log(report);
            return report;
        }

        private static bool ImportOne(string name, StringBuilder log)
        {
            var idlePath = IdlePath(name);
            var walkPath = WalkPath(name);

            if (AssetImporter.GetAtPath(idlePath) is not ModelImporter idle)
            {
                log.AppendLine($"{name,-11} MISSING {idlePath}");
                return false;
            }

            // --- body + idle clip ---------------------------------------------------------------

            ApplyCommon(idle);
            idle.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            idle.sourceAvatar = null;
            SetSingleClip(idle, IdleClip(name), log, name);
            idle.SaveAndReimport();

            var avatar = AssetDatabase.LoadAllAssetsAtPath(idlePath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                log.AppendLine(
                    $"{name,-11} FAILED - {Path.GetFileName(idlePath)} produced " +
                    (avatar == null ? "no avatar" : $"an avatar that is valid={avatar.isValid} human={avatar.isHuman}") +
                    ". Everything downstream retargets onto it, so this character is skipped.");
                return false;
            }

            int textures = ExtractTextures(name, idlePath, log);
            float height = MeasureHeight(idlePath);

            // --- walk clip ----------------------------------------------------------------------

            float walkSpeed = 0f;
            if (AssetImporter.GetAtPath(walkPath) is not ModelImporter walk)
            {
                log.AppendLine($"{name,-11} no {Path.GetFileName(walkPath)} - this character will only idle");
            }
            else
            {
                ApplyCommon(walk);
                walk.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                walk.sourceAvatar = avatar;

                // The walk file's own mesh is never used, so its materials are pure clutter - and a
                // second set of Mixamo materials would fight the body's over the shared texture names.
                walk.materialImportMode = ModelImporterMaterialImportMode.None;

                SetSingleClip(walk, WalkClip(name), log, name);
                walk.SaveAndReimport();

                var clip = AssetDatabase.LoadAllAssetsAtPath(walkPath)
                    .OfType<AnimationClip>()
                    .FirstOrDefault(c => c.name == WalkClip(name));

                if (clip == null) log.AppendLine($"{name,-11} walk FBX produced no clip named {WalkClip(name)}");
                else walkSpeed = clip.averageSpeed.magnitude;
            }

            float scale = HeightScale(height);
            log.AppendLine(
                $"{name,-11} avatar ok | {height:0.00} m" +
                (Mathf.Approximately(scale, 1f) ? "" : $" → visual scale {scale:0.###}") +
                $" | walk {walkSpeed:0.00} m/s | {textures} texture(s)");

            // A Mixamo "in place" export, or a baked root. The animator builder falls back, but the
            // cadence will be guessed rather than measured - say so where it can be seen.
            if (walkSpeed > 0f && walkSpeed < 0.2f)
                log.AppendLine(
                    $"{name,-11} ⚠ walk clip barely travels ({walkSpeed:0.00} m/s) - it is probably an " +
                    "\"in place\" Mixamo export. Build NPC Animator will fall back to a nominal speed.");

            return true;
        }

        /// <summary>Settings both files of a character share.</summary>
        private static void ApplyCommon(ModelImporter importer)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.importAnimation = true;

            // Unity's own unit conversion, explicitly - these FBX are in CENTIMETRES, and Unity
            // reads that from the file header and divides by a hundred for you.
            //
            // An earlier pass here normalised the height by scaling the IMPORTER instead, and it
            // broke the rig: the avatar is built from the body file's bone lengths while the clip
            // carries the walk file's, so scaling one of them apart from the other made Unity report
            // "Avatar Rig Configuration mis-match … position error = 43757 mm" on every leg bone. A
            // character who is not 1.70 m is scaled on the prefab's VISUAL CHILD instead, by
            // NpcBuilder - the root keeps scale 1 so the physics capsule stays the size the config
            // asked for.
            importer.useFileScale = true;
            importer.globalScale = 1f;

            // ⚠ U35a REVERSED THIS, and the reversal is not a preference - it is forced.
            //
            // It used to read "nothing in a street crowd reads a bone, so the whole transform
            // hierarchy can go", with optimizeGameObjects = true collapsing ~68 transforms per
            // instance into the Animator's internal skeleton. A RAGDOLL is eleven Rigidbodies bolted
            // to eleven bone transforms, and under Optimize Game Objects those transforms do not
            // exist: the six pedestrians all failed RagdollBuilder with "the avatar has no Hips bone"
            // while the three player bodies, which never had it on, rigged first time.
            //
            // extraExposedTransformPaths is NOT the way out, and it is worth writing down because it
            // looks exactly like the feature for this: an exposed transform is an OUTPUT the Animator
            // writes, while the SkinnedMeshRenderer is skinned from the internal skeleton it was not
            // given. Physics moving an exposed bone would move nothing anyone can see.
            //
            // The cost is real and is U30b's to measure: ~68 transforms per live body, and the crowd
            // holds up to 160. What keeps it affordable is that it is off-screen work that does not
            // happen - NpcBuilder sets cullingMode = CullCompletely, so an unseen pedestrian poses
            // nothing at all - plus the spawner's existing trickle, which is what bounds the extra
            // Instantiate cost per frame.
            importer.optimizeGameObjects = false;
            importer.extraExposedTransformPaths = System.Array.Empty<string>();

            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.animationCompression = ModelImporterAnimationCompression.KeyframeReductionAndCompression;
            importer.meshCompression = ModelImporterMeshCompression.Off;
        }

        /// <summary>
        /// Renames the file's first take and makes it a looping, root-extracted locomotion clip.
        ///
        /// Rebuilt from <c>defaultClipAnimations</c> every run rather than edited in place, so
        /// re-running is idempotent and a re-exported FBX is picked up - the same reason
        /// <see cref="JoeClipImporter"/> does it that way.
        /// </summary>
        private static void SetSingleClip(ModelImporter importer, string clipName, StringBuilder log, string name)
        {
            var takes = importer.defaultClipAnimations;
            if (takes.Length == 0)
            {
                log.AppendLine($"{name,-11} {Path.GetFileName(importer.assetPath)} has no animation takes");
                return;
            }

            var clip = takes[0];
            clip.name = clipName;
            clip.loopTime = true;

            // Bake Into Pose OFF on all three, "Based Upon: Original" - see the class doc.
            clip.lockRootRotation = false;
            clip.lockRootHeightY = false;
            clip.lockRootPositionXZ = false;
            clip.keepOriginalOrientation = true;
            clip.keepOriginalPositionY = true;
            clip.keepOriginalPositionXZ = true;
            clip.heightFromFeet = false;

            importer.clipAnimations = new[] { clip };
        }

        // --- textures -------------------------------------------------------------------------

        private static readonly string[] BaseTokens = { "diffuse", "basecolor", "albedo", "base_color" };
        private static readonly string[] NormalTokens = { "normal", "_nrm", "_norm", "bump" };

        /// <summary>
        /// Pulls the FBX's embedded textures onto disk, where an importer can reach them.
        ///
        /// An embedded texture is a sub-asset and a sub-asset has no <c>TextureImporter</c>, so it
        /// is never compressed and never resized - the same fault U15 found on the districts from
        /// the glTFast side (memory: <c>gltfast-textures-never-compressed</c>). Extraction is what
        /// gives them one.
        ///
        /// <b>Binding them is NpcBuilder's job, not this file's.</b> Unity 6 removed External
        /// material location ("no longer supported"), so the generated materials stay embedded and
        /// read-only; the prefab builder writes its own URP/Lit materials against these textures
        /// instead. Same answer TrafficCarBuilder reached for the same reason.
        /// </summary>
        private static int ExtractTextures(string name, string modelPath, StringBuilder log)
        {
            var folder = TextureFolder(name);
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(PeoplePath, $"{name}Textures");

            if (AssetImporter.GetAtPath(modelPath) is not ModelImporter importer) return 0;

            importer.ExtractTextures(folder);
            AssetDatabase.Refresh();

            var textures = AssetDatabase.FindAssets("t:Texture2D", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .ToList();

            foreach (var path in textures) ConfigureTexture(path, MaxTextureSize);
            AssetDatabase.Refresh();

            if (textures.Count == 0)
                log.AppendLine(
                    $"{name,-11} ⚠ no textures came out of the FBX - the character will render white. " +
                    $"Check {folder}");

            return textures.Count;
        }

        internal static string TextureFolder(string name) => $"{PeoplePath}/{name}Textures";

        internal static bool IsNormalMap(string path) =>
            NormalTokens.Any(t => Path.GetFileNameWithoutExtension(path).ToLowerInvariant().Contains(t));

        internal static bool IsBaseMap(string path)
        {
            var stem = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            return BaseTokens.Any(t => stem.Contains(t));
        }

        /// <summary>
        /// Shared with <see cref="CharacterImporter"/>, which imports the playable bodies at a
        /// larger <paramref name="maxSize"/> - a body the camera sits three metres behind is not a
        /// pedestrian glimpsed across a street. Everything else about the recipe is the same, and
        /// the npot trap below is the reason this is one function rather than two.
        /// </summary>
        internal static void ConfigureTexture(string path, int maxSize)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;

            var stem = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            bool isNormal = NormalTokens.Any(t => stem.Contains(t));

            importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !isNormal;
            importer.maxTextureSize = maxSize;
            importer.mipmapEnabled = true;

            // A non-power-of-two texture with mipmaps silently refuses block compression and ships
            // as raw RGB24 - the importer still claims DXT. ToLarger is the fix (memory:
            // npot-mips-skip-block-compression).
            importer.npotScale = TextureImporterNPOTScale.ToLarger;
            importer.textureCompression = TextureImporterCompression.Compressed;

            importer.SaveAndReimport();
        }

        // --- height ---------------------------------------------------------------------------

        /// <summary>
        /// The character's standing height in metres, measured by actually instantiating it and
        /// baking the skin.
        ///
        /// Instantiated rather than read off <c>mesh.bounds</c>, because that reports the mesh in
        /// FILE units and ignores the import scale entirely - which is how an earlier pass here
        /// concluded every character was "170 m tall", scaled the importer to fix it, and broke the
        /// rigs. A real instance in a preview scene is the only reading that includes everything.
        ///
        /// <b>BAKED VERTICES, never <c>renderer.bounds</c>, and that reversal is what fixed the
        /// crowd.</b> A SkinnedMeshRenderer's bounds are the volume the whole CLIP sweeps, padded -
        /// not the body's height. Measured on this project's own files: Joe.fbx bounds 1.968 m
        /// against a 1.769 m body, Chinese_Idle 2.037 against 1.739, and Joe_Jumping - the same rig
        /// again - 2.061, because the jump is in it. Feeding that overshoot into
        /// <see cref="HeightScale"/> shrank every pedestrian by its own padding, so the crowd was
        /// built 1.45-1.55 m tall and stood knee-high beside a 1.77 m player. Baking the skin and
        /// reading the vertices is a measurement of the body and nothing else.
        ///
        /// The preview scene is not the open one: instantiating into the active scene would leave it
        /// dirty, and a builder that quietly dirties the world scene is how an unrelated change ends
        /// up in a commit.
        /// </summary>
        internal static float MeasureHeight(string path)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) return 0f;

            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = Object.Instantiate(model);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(instance, scene);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                float height = SkinnedHeight(instance);

                // Nothing skinned is not an error - it is a prop, or a clip-only FBX with no mesh at
                // all - and for a static mesh the bounds ARE the geometry, so the old reading stands.
                if (height <= 0f)
                {
                    var renderers = instance.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length > 0)
                    {
                        var bounds = renderers[0].bounds;
                        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                        height = bounds.size.y;
                    }
                }

                Object.DestroyImmediate(instance);
                return height;
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>
        /// Vertical extent of every skinned vertex in the pose the rig imported in, or 0 if there is
        /// no skin.
        ///
        /// <b><c>useScale: true</c> AND then the renderer's matrix, and the pairing is the opposite of
        /// what the flag name suggests.</b> Measured on <c>Ped_Remy</c>, whose visual scale is 0.454,
        /// against a body that is drawn 1.700 m: <c>useScale: true</c> hands back 3.748 - the rig with
        /// its world scale DIVIDED OUT - so the matrix puts it back and the answer is 1.700.
        /// <c>useScale: false</c> hands back 1.700 already, and the matrix then squares the scale into
        /// 0.771. On an unscaled hierarchy all four combinations agree, which is exactly why the wrong
        /// pairing survives being tested on an FBX and only lies about a built prefab.
        /// </summary>
        private static float SkinnedHeight(GameObject instance)
        {
            float low = float.PositiveInfinity;
            float high = float.NegativeInfinity;

            foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var baked = new Mesh();
                try
                {
                    skin.BakeMesh(baked, useScale: true);
                    var matrix = skin.transform.localToWorldMatrix;

                    foreach (var vertex in baked.vertices)
                    {
                        float y = matrix.MultiplyPoint3x4(vertex).y;
                        if (y < low) low = y;
                        if (y > high) high = y;
                    }
                }
                finally
                {
                    Object.DestroyImmediate(baked);
                }
            }

            return high > low ? high - low : 0f;
        }

        /// <summary>
        /// What the prefab's visual child must be scaled by to stand <see cref="TargetHeight"/> tall.
        /// 1 when the import already landed within <see cref="HeightTolerance"/>.
        /// </summary>
        internal static float HeightScale(float measured)
        {
            if (measured <= 0.001f) return 1f;
            if (Mathf.Abs(measured - TargetHeight) <= TargetHeight * HeightTolerance) return 1f;
            return TargetHeight / measured;
        }
    }
}
