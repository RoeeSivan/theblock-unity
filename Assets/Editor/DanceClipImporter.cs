using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Imports the eight dance clips plus Remy's partner loop - <b>The Block → Import Dance Clips</b>
    /// - and then <b>deletes the FBX they came from</b>.
    ///
    /// <b>That deletion is the whole point, and it is not tidiness.</b> These are Mixamo
    /// with-skin downloads: nine files, ~50 MB each, 450 MB in total, each carrying its own copy of
    /// the same body that nothing ever renders. This project's Git LFS store is already at GitHub's
    /// 1 GiB free ceiling, shared with the original repo. Committing them would end that.
    ///
    /// Only the ANIMATION is wanted, and an extracted humanoid <c>AnimationClip</c> is a few hundred
    /// kilobytes of muscle curves that retargets onto any Humanoid avatar - so the clips are copied
    /// out into <c>Assets/Animation/Dance</c> as standalone assets and the sources go. <b>The web
    /// build solved the identical problem the identical way</b>: its
    /// <c>blender/anim-clip.py --strip-mesh</c> pass took the same eight from 6.5 MB to 1.0 MB.
    ///
    /// Re-running is safe: an already-extracted clip whose FBX is gone is skipped. To re-derive one,
    /// drop its FBX back in and run again.
    ///
    /// The recipe is <see cref="JoeClipImporter"/>'s, with one difference - <b>Bake Into Pose on all
    /// three axes</b>. A dance is performed on the spot, so whatever travel Mixamo baked into the
    /// hips has to stay in the POSE rather than become root motion. The web build does exactly this
    /// by hand, zeroing the hips' X and Z tracks and keeping Y for the vertical bob, and its comment
    /// says why: a routine that sways 40 cm per loop wanders off the stage over 500 frames.
    /// </summary>
    public static class DanceClipImporter
    {
        private const string SourceFolder = "Assets/Models/Dance";
        private const string ClipFolder = "Assets/Animation/Dance";
        private const string AvatarSource = "Assets/Models/Characters/Joe.fbx";

        /// <summary>
        /// The nine, keyed by the clip name the runtime asks for. The first eight are the dancer's
        /// own set - one groove, four flourishes, and three reactions. The ninth is Remy's: he
        /// grooves beside you for the whole routine, which is what makes it a duet rather than a
        /// man dancing at a static NPC.
        /// </summary>
        private static readonly (string File, string Clip, bool Loop)[] Clips =
        {
            ("Dance_HipHop", "Dance_Idle", true),        // the base groove, between notes
            ("Dance_Snake", "Dance_Hit1", false),
            ("Dance_Wave", "Dance_Hit2", false),
            ("Dance_Shuffling", "Dance_Hit3", false),
            ("Dance_Robot", "Dance_Hit4", false),
            ("Dance_SadIdle", "Dance_Miss", false),
            ("Dance_Victory", "Dance_Win", false),       // terminal: it clamps and holds
            ("Dance_Defeated", "Dance_Fail", false),     // terminal
            ("Dance_Bboy", "Dance_Partner", true),       // Remy's loop
        };

        [MenuItem("The Block/Import Dance Clips", priority = 26)]
        public static void Import()
        {
            // Joe is not the retarget SOURCE - each file builds its own avatar, see Apply - but he is
            // what these clips will be played on, so his absence is still the thing worth failing on.
            var avatar = AssetDatabase.LoadAllAssetsAtPath(AvatarSource).OfType<Avatar>().FirstOrDefault();
            if (avatar == null)
            {
                Debug.LogError($"DanceClipImporter: no Avatar in {AvatarSource}. Import Joe Humanoid first.");
                return;
            }

            Directory.CreateDirectory(ClipFolder);
            AssetDatabase.Refresh();

            var log = new System.Text.StringBuilder(
                $"DanceClipImporter - own avatar per file, retargeted onto {avatar.name} at play time\n");
            var extracted = new List<string>();
            var deleted = 0L;

            foreach (var (file, clipName, loop) in Clips)
            {
                var fbx = $"{SourceFolder}/{file}.fbx";
                var outPath = $"{ClipFolder}/{clipName}.anim";

                if (AssetImporter.GetAtPath(fbx) is not ModelImporter importer)
                {
                    var already = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
                    log.AppendLine(already != null
                        ? $"  {clipName,-14} already extracted ({already.length:0.00}s) - source gone, as intended"
                        : $"  {clipName,-14} MISSING: no {fbx} and no extracted clip");
                    continue;
                }

                Apply(importer, clipName, loop);
                importer.SaveAndReimport();

                var source = AssetDatabase.LoadAllAssetsAtPath(fbx)
                    .OfType<AnimationClip>()
                    .FirstOrDefault(c => c.name == clipName);

                if (source == null)
                {
                    log.AppendLine($"  {clipName,-14} reimported but produced no clip - FBX kept for inspection");
                    continue;
                }

                // A duplicated humanoid clip is standalone: its muscle curves are avatar-relative,
                // so it plays on any Humanoid rig with no reference back to the file it came from.
                // That independence is what makes deleting the source safe rather than reckless.
                var copy = Object.Instantiate(source);
                copy.name = clipName;
                AssetDatabase.DeleteAsset(outPath);
                AssetDatabase.CreateAsset(copy, outPath);

                var bytes = new FileInfo(Path.GetFullPath(fbx)).Length;
                AssetDatabase.DeleteAsset(fbx);
                deleted += bytes;

                extracted.Add(clipName);
                log.AppendLine(
                    $"  {clipName,-14} {copy.length:0.00}s {(loop ? "looping" : "one-shot")} " +
                    $"→ {outPath}   (source {bytes / (1024f * 1024f):0.#} MB deleted)");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            log.AppendLine($"\n{extracted.Count}/{Clips.Length} extracted, " +
                           $"{deleted / (1024f * 1024f):0.#} MB of source FBX removed from the project.");
            Debug.Log(log.ToString());
        }

        private static void Apply(ModelImporter importer, string clipName, bool loop)
        {
            importer.animationType = ModelImporterAnimationType.Human;

            // CREATE FROM THIS MODEL, not Copy From Other - and this is the one place this importer
            // must differ from JoeClipImporter. Mixamo stamps a per-upload counter into its bone
            // namespace, so Joe's rig is `mixamorig7:Hips` and these nine downloads are not. Copying
            // his avatar onto them fails outright with "Transform 'mixamorig7:Hips' for human bone
            // 'Hips' not found" - which is exactly what happened on the first run of this.
            //
            // Building each file its own avatar costs nothing here, because a HUMANOID clip is
            // stored as avatar-relative muscle curves: it retargets onto Joe at PLAY time regardless
            // of what it was authored on. That is the whole reason Humanoid exists, and it is why
            // the web build's 195 hand-renamed tracks have no equivalent in this port.
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;
            importer.importAnimation = true;

            // Nothing here needs a mesh, a material or a texture - only curves. Skipping them makes
            // the reimport of a 55 MB file dramatically cheaper and leaves nothing behind to delete.
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importBlendShapes = false;
            importer.isReadable = false;

            var takes = importer.defaultClipAnimations;
            if (takes.Length == 0)
            {
                Debug.LogWarning($"DanceClipImporter: {clipName} has no animation takes.");
                return;
            }

            var clip = takes[0];
            clip.name = clipName;
            clip.loopTime = loop;

            // Bake Into Pose on all three, Based Upon: Original. The dance happens on the spot.
            clip.lockRootRotation = true;
            clip.lockRootHeightY = true;
            clip.lockRootPositionXZ = true;
            clip.keepOriginalOrientation = true;
            clip.keepOriginalPositionY = true;
            clip.keepOriginalPositionXZ = true;
            clip.heightFromFeet = false;

            importer.clipAnimations = new[] { clip };
        }
    }
}
