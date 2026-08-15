using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Applies the import settings a borrowed Mixamo clip needs before it will play on Joe.
    ///
    /// Scripted for the reason every other builder here is: the settings are six checkboxes buried
    /// in two Inspector tabs, they are invisible in review, and a clip imported with the wrong ones
    /// fails in a way that looks like an animation bug rather than an import mistake — a T-pose, or
    /// a driver who slides out of the car because his travel was extracted as root motion.
    ///
    /// The recipe, for each clip:
    ///  - <b>Humanoid, Copy From Other Avatar → JoeAvatar.</b> Retargeting by avatar is what makes
    ///    the <c>mixamorig7:</c> bone-prefix problem moot — Mixamo stamps a per-upload counter into
    ///    its namespace, so a clip and a body downloaded on different days disagree about what the
    ///    skeleton is called. The web build had to rename 195 tracks by hand to work around it.
    ///  - <b>Bake Into Pose, based upon Original</b>, on all three of rotation, position Y and
    ///    position XZ, for a clip whose travel is the point. Baked into the pose, the body walks
    ///    itself while its transform stays exactly where it was put — which is what lets the car's
    ///    driver anchor be a fixed spot beside the door. Extracted as root motion instead, the
    ///    travel would fight whatever else owns the transform.
    ///
    /// To add a clip: drop the FBX in <c>Assets/Models/Characters</c>, add a row to
    /// <see cref="Clips"/>, run this, then re-run <b>The Block → Build Joe Animator</b>.
    /// </summary>
    public static class JoeClipImporter
    {
        private const string CharactersPath = "Assets/Models/Characters";

        /// <summary>The FBX that defines the avatar every other clip is retargeted onto.</summary>
        private const string AvatarSource = "Joe.fbx";

        /// <summary>
        /// One borrowed clip. <c>BakeRoot</c> keeps the clip's travel in the pose instead of handing
        /// it to the Animator as root motion — true for anything that moves the body through a fixed
        /// space, false for a locomotion cycle the controller drives.
        /// </summary>
        private readonly struct ClipSpec
        {
            public readonly string FileName;
            public readonly string ClipName;
            public readonly bool Loop;
            public readonly bool BakeRoot;

            public ClipSpec(string fileName, string clipName, bool loop, bool bakeRoot)
            {
                FileName = fileName;
                ClipName = clipName;
                Loop = loop;
                BakeRoot = bakeRoot;
            }
        }

        private static readonly ClipSpec[] Clips =
        {
            new("Joe_EnterCar.fbx", "Joe_EnterCar", loop: false, bakeRoot: true),

            // The seated riding pose, shared by the motorcycle (U10) and the jetski (U24) — which is
            // why the web build has it as a file of its own rather than inside either vehicle.
            // BakeRoot for the same reason as the entry clip: the rider anchor is a fixed child of
            // the bike and the body must not drift off it, so whatever travel the clip carries stays
            // in the pose. Looping, because unlike the entry clip this one never ends.
            //
            // This FBX ships a body (55 MB, Mixamo's with-skin download) exactly as Joe_Sprint and
            // Joe_Jumping do. Only its animation is imported; the mesh inside it is never used.
            new("Joe_Driving.fbx", "Joe_Ride", loop: true, bakeRoot: true),

            // Swimming. BakeRoot FALSE, unlike the two above: this is a locomotion cycle the
            // controller drives, so the clip's forward travel must be extracted (and then discarded,
            // because PlayerAnimator keeps root motion off) rather than left in the pose. Baked in,
            // Joe would visibly crawl away from his own capsule and snap back every time the loop
            // wrapped. Same arrangement as walk and sprint.
            new("Joe_Swim.fbx", "Joe_Swim", loop: true, bakeRoot: false),
        };

        [MenuItem("The Block/Import Joe Clips", priority = 21)]
        public static void Import()
        {
            var avatar = AssetDatabase.LoadAllAssetsAtPath($"{CharactersPath}/{AvatarSource}")
                .OfType<Avatar>()
                .FirstOrDefault();

            if (avatar == null)
            {
                Debug.LogError(
                    $"JoeClipImporter: no Avatar in {CharactersPath}/{AvatarSource}. That file is " +
                    "what every borrowed clip is retargeted onto — import it Humanoid first.");
                return;
            }

            var log = new System.Text.StringBuilder($"JoeClipImporter — avatar {avatar.name}\n");

            foreach (var clip in Clips)
            {
                var path = $"{CharactersPath}/{clip.FileName}";
                if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
                {
                    log.AppendLine($"  {clip.FileName,-22} not in the project — skipped");
                    continue;
                }

                Apply(importer, avatar, clip);
                importer.SaveAndReimport();

                var imported = AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<AnimationClip>()
                    .FirstOrDefault(c => c.name == clip.ClipName);

                log.AppendLine(imported == null
                    ? $"  {clip.FileName,-22} reimported but produced no clip named '{clip.ClipName}'"
                    : $"  {clip.FileName,-22} → {clip.ClipName}, {imported.length:0.00}s, " +
                      $"{(clip.Loop ? "looping" : "one-shot")}, " +
                      $"root {(clip.BakeRoot ? "baked into pose" : "extracted")}");
            }

            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString());
        }

        private static void Apply(ModelImporter importer, Avatar avatar, ClipSpec spec)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = avatar;
            importer.importAnimation = true;

            // Start from the takes the file actually contains rather than from whatever a previous
            // run left behind, so re-running this is idempotent and a re-exported FBX is picked up.
            var takes = importer.defaultClipAnimations;
            if (takes.Length == 0)
            {
                Debug.LogWarning($"JoeClipImporter: {spec.FileName} has no animation takes.");
                return;
            }

            var clip = takes[0];
            clip.name = spec.ClipName;
            clip.loopTime = spec.Loop;

            // The three "Bake Into Pose" toggles, whose serialized names say loopBlend for
            // historical reasons, plus "Based Upon: Original" for each. Original, not Feet or
            // Centre of Mass: the config's seat block was authored against the clip's own origin,
            // so re-basing it here would move the driver by however much Unity decided to re-base.
            clip.lockRootRotation = spec.BakeRoot;
            clip.lockRootHeightY = spec.BakeRoot;
            clip.lockRootPositionXZ = spec.BakeRoot;
            clip.keepOriginalOrientation = true;
            clip.keepOriginalPositionY = true;
            clip.keepOriginalPositionXZ = true;
            clip.heightFromFeet = false;

            importer.clipAnimations = new[] { clip };
        }
    }
}
