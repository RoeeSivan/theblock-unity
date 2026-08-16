using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Imports the one clip U18 needs - <b>The Block → Import Hit Clip</b>.
    ///
    /// <c>Hit_By_Car.fbx</c> is the original game's own knockdown animation, taken from its
    /// <c>source-assets/</c> rather than from the 169 KB GLB it ships (port rule 3). One file serves
    /// all six characters: a Humanoid clip retargets onto any Humanoid avatar, so unlike the idle and
    /// the walk - which are each authored on their own body and must stay with it - the hit is
    /// imported ONCE and played on everyone.
    ///
    /// <b>It creates its own avatar, and that is NOT what <see cref="JoeClipImporter"/> does.</b>
    /// Joe's borrowed clips all copy Joe's avatar, which works because they came out of one Mixamo
    /// upload and therefore share his bone namespace. This file did not: its skeleton is
    /// <c>mixamorig:Hips</c> while the six characters are <c>mixamorigN:Hips</c>, and Copy From Other
    /// matches bones by NAME - so copying any character's avatar fails outright with
    /// <i>"Copied Avatar Rig Configuration mis-match. Transform 'mixamorig:Hips' for human bone
    /// 'Hips' not found"</i>. Create From This Model builds an avatar from the file's own skeleton,
    /// and Humanoid retargeting then plays the clip on any body regardless of what its bones are
    /// called. That is the same namespace problem the web build had to rename tracks by hand to work
    /// around, arriving from the other direction.
    ///
    /// <b>The three Bake Into Pose switches are the whole file, and they are not the settings the
    /// other clips use.</b>
    ///
    ///  - <b>Position XZ: OFF (extracted).</b> This is the knockback. The clip carries about two
    ///    metres of authored travel and that travel IS the throw - <see cref="TheBlock.Npc.Pedestrian"/>
    ///    turns root motion on for the reaction and off again afterwards. Everything else on a
    ///    pedestrian is script-driven, so this clip is the single exception in the project.
    ///  - <b>Position Y: ON (baked into the pose).</b> The clip's own vertical is flat - it is a
    ///    stagger-and-collapse, not a launch - and the code supplies the arc instead. Extracting Y
    ///    would also sink the transform to hip height as the body lies down, and then the arc and the
    ///    pose would each be moving the same body by a different clock.
    ///  - <b>Rotation: ON (baked into the pose).</b> The victim's yaw is set ONCE at impact so the
    ///    clip's authored (sideways) throw travels down the road. Extracting the rotation would spin
    ///    the transform under that aim mid-flight and the body would curve.
    ///
    /// <b>The throw direction is measured here, not ported.</b> The web build carries a hand-tuned
    /// <c>clipYawOffset</c> of −85.8°, derived by eye against three.js's handedness. Rather than
    /// transcribe a number whose sign is exactly the thing this project keeps getting wrong,
    /// <see cref="ThrowYaw"/> reads the clip's own root-motion direction - and the runtime aims the
    /// body by subtracting it, so the authored travel lands along the car's forward whatever the
    /// clip does.
    /// </summary>
    public static class HitClipImporter
    {
        /// <summary>The FBX, copied out of the original's <c>source-assets/</c>.</summary>
        internal const string ClipPath = "Assets/Models/Characters/People/Hit_By_Car.fbx";

        /// <summary>
        /// What the take is renamed to. The name is <see cref="TheBlock.Npc.Pedestrian"/>'s, because
        /// the runtime is what looks the clip up by it - this file only has to agree.
        /// </summary>
        internal const string ClipName = TheBlock.Npc.Pedestrian.HitClipName;

        [MenuItem("The Block/Import Hit Clip", priority = 24)]
        public static void ImportMenu() => Import();

        public static string Import()
        {
            if (AssetImporter.GetAtPath(ClipPath) is not ModelImporter importer)
            {
                var missing =
                    $"HitClipImporter: no {ClipPath}. Copy it from the original project's " +
                    "source-assets/Hit_By_Car.fbx.";
                Debug.LogError(missing);
                return missing;
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.importAnimation = true;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;

            // Same reasoning as the walk files: this FBX's own mesh is never drawn, so its materials
            // are clutter that would fight the six characters' over shared texture names.
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.animationCompression = ModelImporterAnimationCompression.KeyframeReductionAndCompression;

            // Reimported BEFORE the takes are read, unlike PeopleImporter's one-pass version, and it
            // is not optional. `defaultClipAnimations` reports what the LAST import produced, and the
            // last import of this file may well have failed - the first attempt here died on the
            // bone-namespace mismatch above and came back with no takes at all, which reads as "the
            // FBX has no animation" rather than "your rig settings were wrong". The untrimmed import
            // is also what the window below is measured against.
            importer.clipAnimations = System.Array.Empty<ModelImporterClipAnimation>();
            importer.SaveAndReimport();

            var takes = importer.defaultClipAnimations;
            if (takes.Length == 0)
            {
                const string noTake = "HitClipImporter: Hit_By_Car.fbx has no animation takes.";
                Debug.LogError(noTake);
                return noTake;
            }

            var take = takes[0];
            take.name = ClipName;

            var whole = AssetDatabase.LoadAllAssetsAtPath(ClipPath).OfType<AnimationClip>().FirstOrDefault();
            var window = Window(whole, out string windowLog);
            take.firstFrame = window.x;
            take.lastFrame = window.y;

            // Not a cycle. Looping it would stand the body back up and knock it down forever; held on
            // its last frame, the clip IS the lying pose the reaction fades out.
            take.loopTime = false;

            take.lockRootRotation = true;      // rotation → pose
            take.keepOriginalOrientation = true;
            take.lockRootHeightY = true;       // vertical → pose
            take.keepOriginalPositionY = true;
            take.heightFromFeet = false;
            take.lockRootPositionXZ = false;   // horizontal → ROOT MOTION, the knockback itself
            take.keepOriginalPositionXZ = true;

            importer.clipAnimations = new[] { take };
            importer.SaveAndReimport();

            var clip = AssetDatabase.LoadAllAssetsAtPath(ClipPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == ClipName);

            if (clip == null)
            {
                var noClip = $"HitClipImporter: the reimport produced no clip named {ClipName}.";
                Debug.LogError(noClip);
                return noClip;
            }

            var avatar = AssetDatabase.LoadAllAssetsAtPath(ClipPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isHuman)
            {
                var noAvatar =
                    "HitClipImporter: the FBX produced no human avatar, so the clip is not a Humanoid " +
                    "clip and will not retarget onto anybody.";
                Debug.LogError(noAvatar);
                return noAvatar;
            }

            var log = new StringBuilder();
            var travel = clip.averageSpeed * clip.length;
            float flat = new Vector2(travel.x, travel.z).magnitude;

            log.Append(windowLog);
            log.AppendLine($"  {ClipName}  {clip.length:0.00} s @ {clip.frameRate:0} fps, loop {clip.isLooping}");
            log.AppendLine(
                $"  root motion travel {flat:0.00} m in the clip's own frame " +
                $"(x {travel.x:0.00}, y {travel.y:0.00}, z {travel.z:0.00})");
            log.AppendLine(
                $"  throw yaw {ThrowYaw(clip):0.0}° off the body's forward - the runtime subtracts this " +
                "so the travel lands along the car's forward");

            if (flat < 0.2f)
                log.AppendLine(
                    "  ⚠ the clip barely travels. Bake Into Pose is probably ON for XZ, which leaves " +
                    "nothing for root motion to apply and makes the knockback a stagger on the spot.");

            var report = $"HitClipImporter - {ClipPath}\n{log}";
            Debug.Log(report, clip);
            return report;
        }

        /// <summary>
        /// The frames the knockdown actually occupies, found by watching the root move.
        ///
        /// <b>Mixamo pads a one-shot clip with seconds of idle before the action starts</b>, and this
        /// file is mostly padding: 145 frames at 30 fps, of which the body simply stands there for
        /// the first eighty. Imported whole, the reaction would wait 2.7 seconds after impact before
        /// the victim so much as flinched - and the phases hang off the clip's LENGTH, so the lie and
        /// the fade would be pushed out behind it too. The original trims the same clip in Blender on
        /// the way to its GLB and its comment names the same fault.
        ///
        /// <b>Measured, not typed in.</b> The threshold is a fraction of the clip's OWN peak frame
        /// speed rather than an absolute, so it does not care what units or what character the file
        /// was authored on: standing drift is under 4% of the throw's peak here, which is not a close
        /// call. A tail of stillness is kept deliberately - the last frame is the pose the reaction
        /// lies in while it fades, so it has to be the body settled, not the body still sliding.
        /// </summary>
        private static Vector2Int Window(AnimationClip clip, out string log)
        {
            log = string.Empty;
            if (clip == null) return new Vector2Int(0, 0);

            int frames = Mathf.RoundToInt(clip.length * clip.frameRate);
            var bindings = AnimationUtility.GetCurveBindings(clip);
            AnimationCurve x = null, z = null;

            foreach (var binding in bindings)
            {
                if (binding.propertyName == "RootT.x") x = AnimationUtility.GetEditorCurve(clip, binding);
                if (binding.propertyName == "RootT.z") z = AnimationUtility.GetEditorCurve(clip, binding);
            }

            if (x == null || z == null || frames < 4)
            {
                log = "  ⚠ no RootT curves to measure - importing the clip whole, padding and all.\n";
                return new Vector2Int(0, frames);
            }

            var step = new float[frames + 1];
            float peak = 0f;
            for (int f = 1; f <= frames; f++)
            {
                float t = f / clip.frameRate;
                float previous = (f - 1) / clip.frameRate;
                float dx = x.Evaluate(t) - x.Evaluate(previous);
                float dz = z.Evaluate(t) - z.Evaluate(previous);
                step[f] = Mathf.Sqrt(dx * dx + dz * dz);
                peak = Mathf.Max(peak, step[f]);
            }

            if (peak < 1e-4f)
            {
                log = "  ⚠ the root never moves - importing the clip whole.\n";
                return new Vector2Int(0, frames);
            }

            int first = 0;
            while (first < frames && step[first + 1] < peak * 0.15f) first++;

            int last = frames;
            while (last > first && step[last] < peak * 0.10f) last--;

            // ~0.4 s of the settled pose, which is what the body lies in while the stain spreads.
            last = Mathf.Min(frames, last + Mathf.RoundToInt(clip.frameRate * 0.4f));

            log =
                $"  window f{first}..f{last} of {frames} - the body stands still for the first " +
                $"{first} frames ({first / clip.frameRate:0.0} s of Mixamo padding)\n";

            return new Vector2Int(first, last);
        }

        /// <summary>
        /// Which way the clip throws the body, in degrees about Y, measured off its own root motion.
        ///
        /// The same function the runtime aims with - <see cref="TheBlock.Npc.Pedestrian.ThrowYaw"/> -
        /// so what this logs at import is exactly what the game will use, and the two cannot drift.
        /// </summary>
        internal static float ThrowYaw(AnimationClip clip) => TheBlock.Npc.Pedestrian.ThrowYaw(clip);

        /// <summary>The imported clip, or null if it has not been imported yet.</summary>
        internal static AnimationClip Load() =>
            AssetDatabase.LoadAllAssetsAtPath(ClipPath).OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == ClipName);
    }
}
