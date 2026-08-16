using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Imports the playable bodies - <b>The Block → Import Characters (slow)</b>.
    ///
    /// The roster the shipped game has: Joe, Jody and David, named by
    /// <c>src/player/characters.config.ts</c>. Joe arrived at U2 and is not touched here; this
    /// brings in the other two from the FBX they were exported from rather than from the shipped
    /// GLBs (port rule 3), which is ~100 MB of source and takes minutes.
    ///
    /// <b>A playable body is imported like Joe, not like a pedestrian.</b> The two importers look
    /// alike and differ in three settings that matter:
    ///  - <b>Optimize Game Objects OFF.</b> <see cref="PeopleImporter"/> turns it on because nothing
    ///    ever reads a pedestrian's bones. A playable body is the one U19e measures a driver's seat
    ///    against and the one every future unit will hang something off, exactly as Joe's is.
    ///  - <b>No animation at all.</b> These are Mixamo T-Pose downloads: mesh only. Every clip the
    ///    player uses is Joe's shelf, retargeted through Humanoid at play time - which is the whole
    ///    reason the roster costs one file per character and no animation work. The web build says
    ///    the same thing in its own words at the top of <c>characters.config.ts</c>.
    ///  - <b>Height is matched to JOE, not to 1.70 m.</b> <c>referenceCharacterId</c> is
    ///    <c>'joe'</c>, and the point of that is that adding a roster changed nothing about how the
    ///    character the game shipped with looks. So Joe's own measurement is the target and his
    ///    scale is exactly 1, by construction rather than by luck.
    ///
    /// Shared with the crowd, because they are the same problems: textures are EXTRACTED (an
    /// embedded texture is a sub-asset, a sub-asset has no <c>TextureImporter</c>, so it is never
    /// compressed - memory <c>gltfast-textures-never-compressed</c>, and the npot/mips trap with
    /// it), and the height is measured by instantiating into a preview scene rather than read off
    /// <c>mesh.bounds</c>, which reports FILE units and ignores import scale.
    ///
    /// <b>The scale lands on the prefab's visual child</b> (<see cref="CharacterPrefabBuilder"/>),
    /// never on the importer and never on a root: scaling the importer makes the avatar's bone
    /// lengths disagree with the clip's and Unity reports a rig mis-match on every leg bone; scaling
    /// the player's root would scale his <c>CharacterController</c> capsule with it.
    /// </summary>
    public static class CharacterImporter
    {
        internal const string CharactersPath = "Assets/Models/Characters";

        /// <summary>Whose proportions everyone else is matched to - <c>referenceCharacterId</c>.</summary>
        internal const string ReferenceFile = "Joe.fbx";

        /// <summary>Past this much error the visual child is scaled. Joe himself is always 1.</summary>
        private const float HeightTolerance = 0.02f;

        /// <summary>A playable body IS examined up close, unlike a pedestrian's 1024².</summary>
        private const int MaxTextureSize = 2048;

        /// <summary>
        /// The two files this unit adds. Joe is deliberately absent: he is the reference, he was
        /// imported at U2 with these same settings, and re-importing him would rewrite the avatar
        /// every clip in the project is retargeted onto.
        /// </summary>
        internal static readonly string[] Names = { "Jody", "David" };

        internal static string BodyPath(string name) => $"{CharactersPath}/{name}.fbx";

        internal static string TextureFolder(string name) => $"{CharactersPath}/{name}Textures";

        [MenuItem("The Block/Import Characters (slow)", priority = 22)]
        public static void ImportMenu() => Import();

        public static string Import()
        {
            var log = new StringBuilder();
            int done = 0;

            float reference = PeopleImporter.MeasureHeight($"{CharactersPath}/{ReferenceFile}");
            if (reference <= 0.01f)
            {
                var missing = $"CharacterImporter - {CharactersPath}/{ReferenceFile} did not measure " +
                              "(is it in the project?). Everyone is scaled to Joe, so there is " +
                              "nothing to scale to. Nothing imported.";
                Debug.LogError(missing);
                return missing;
            }

            log.AppendLine($"reference  {ReferenceFile} = {reference:0.000} m - everyone is matched to this");

            try
            {
                for (int i = 0; i < Names.Length; i++)
                {
                    EditorUtility.DisplayProgressBar(
                        "Import Characters", $"{Names[i]} ({i + 1}/{Names.Length})", i / (float)Names.Length);

                    if (ImportOne(Names[i], reference, log)) done++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();

            var report = $"CharacterImporter - {done}/{Names.Length} body/bodies\n{log}";
            Debug.Log(report);
            return report;
        }

        private static bool ImportOne(string name, float reference, StringBuilder log)
        {
            var path = BodyPath(name);

            if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
            {
                log.AppendLine(
                    $"{name,-11} MISSING {path} - copy it out of the game repo's " +
                    "source-assets/models/ first");
                return false;
            }

            Apply(importer);
            importer.SaveAndReimport();

            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                log.AppendLine(
                    $"{name,-11} FAILED - {Path.GetFileName(path)} produced " +
                    (avatar == null ? "no avatar" : $"an avatar that is valid={avatar.isValid} human={avatar.isHuman}") +
                    ". Every clip retargets onto it, so this body is unusable.");
                return false;
            }

            int textures = ExtractTextures(name, path, log);

            float height = PeopleImporter.MeasureHeight(path);
            float scale = HeightScale(height, reference);

            log.AppendLine(
                $"{name,-11} avatar ok | {height:0.000} m" +
                (Mathf.Approximately(scale, 1f) ? " (matches Joe)" : $" → visual scale {scale:0.###}") +
                $" | {textures} texture(s)");

            return true;
        }

        /// <summary>
        /// What a body's visual child must be scaled by to stand as tall as Joe. 1 inside
        /// <see cref="HeightTolerance"/>, and 1 for Joe himself whatever he measures.
        /// </summary>
        internal static float HeightScale(float measured, float reference)
        {
            if (measured <= 0.001f || reference <= 0.001f) return 1f;
            if (Mathf.Abs(measured - reference) <= reference * HeightTolerance) return 1f;
            return reference / measured;
        }

        /// <summary>Joe's own settings, applied to a body that must behave exactly like his.</summary>
        private static void Apply(ModelImporter importer)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;

            // A T-Pose download has no take worth keeping, and importing one would put a second
            // idle in the project that nothing plays.
            importer.importAnimation = false;

            // These FBX are in CENTIMETRES and Unity reads that from the file header. Never scale
            // here - see the class doc for what a scaled importer does to a Humanoid rig.
            importer.useFileScale = true;
            importer.globalScale = 1f;

            // OFF, unlike the crowd's. A playable body's bones are hunted (the driver seat, U19e's
            // hip measurement), and the whole roster must behave the same way Joe does.
            importer.optimizeGameObjects = false;
            importer.extraExposedTransformPaths = System.Array.Empty<string>();

            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
        }

        /// <summary>
        /// Pulls the FBX's embedded textures onto disk, where a <c>TextureImporter</c> can reach
        /// them - and where Unity's own material remap can bind them, which is what stops a Mixamo
        /// body rendering white.
        /// </summary>
        private static int ExtractTextures(string name, string modelPath, StringBuilder log)
        {
            var folder = TextureFolder(name);
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(CharactersPath, $"{name}Textures");

            if (AssetImporter.GetAtPath(modelPath) is not ModelImporter importer) return 0;

            importer.ExtractTextures(folder);
            AssetDatabase.Refresh();

            var textures = AssetDatabase.FindAssets("t:Texture2D", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .ToList();

            foreach (var texture in textures) PeopleImporter.ConfigureTexture(texture, MaxTextureSize);
            AssetDatabase.Refresh();

            if (textures.Count == 0)
                log.AppendLine(
                    $"{name,-11} ⚠ no textures came out of the FBX - the body will render white. " +
                    $"Check {folder}");

            return textures.Count;
        }
    }
}
