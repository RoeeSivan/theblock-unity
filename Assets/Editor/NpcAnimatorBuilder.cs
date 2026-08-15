using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Builds <c>Assets/Animation/Npc.controller</c> — the whole animation state of a pedestrian,
    /// which is idle, walking, and the blend between them.
    ///
    /// It reuses JOE'S clips rather than shipping its own. Both sides are Humanoid — Joe's FBXs
    /// import at <c>animationType: 3</c> and every part of <c>npc_casual_set_00</c> shares the
    /// <c>npc_hmn_01mAvatar</c>/<c>01fAvatar</c> pair — so a clip authored on Joe's skeleton
    /// retargets onto a pedestrian's without a bone in common being named the same. That is the
    /// reason the pack's "Animation: none" in its readme does not matter.
    ///
    /// Same idiom and same reasoning as <see cref="JoeAnimatorBuilder"/>: a graph built by hand in
    /// the Animator window cannot be reviewed or reproduced.
    /// </summary>
    public static class NpcAnimatorBuilder
    {
        private const string ControllerPath = "Assets/Animation/Npc.controller";
        private const string CharactersPath = "Assets/Models/Characters";

        /// <summary>
        /// Ground speed <c>Joe_Walking</c> was authored at. <see cref="JoeAnimatorBuilder"/> puts
        /// that clip in the player's gait ladder at a 2 m/s threshold with no time scaling, which is
        /// the same statement made from the other side.
        /// </summary>
        private const float WalkClipSpeed = 2f;

        /// <summary>
        /// The crowd's own pace, from <c>npc.config.ts</c>: <c>speed { min: 1.0, max: 1.5 }</c>.
        ///
        /// A pedestrian moves at roughly half the speed the walk clip was authored for, so playing
        /// it straight would slide their feet across the pavement — the same fault
        /// <see cref="JoeAnimatorBuilder"/> corrects at the top of the ladder for the sprint. The
        /// low anchor is set below the crowd's minimum so the SLOWEST pedestrian is still fully
        /// walking rather than a blend of walking and standing still.
        /// </summary>
        private const float StrollSpeed = 0.9f;

        [MenuItem("The Block/Build NPC Animator", priority = 21)]
        public static void Build()
        {
            var idle = FindClip("Joe.fbx", "Joe_Idle");
            var walk = FindClip("Joe_Walking.fbx", "Joe_Walk");
            if (idle == null || walk == null) return;

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                             ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            Wipe(controller);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

            var locomotion = controller.CreateBlendTreeInController("Locomotion", out var gait);
            gait.name = "Gait";
            gait.blendType = BlendTreeType.Simple1D;
            gait.blendParameter = "Speed";
            gait.useAutomaticThresholds = false;
            // The walk clip appears twice on purpose: once slowed to the crowd's stroll and once at
            // its authored pace. Between them the blend is walk-to-walk, so a pedestrian anywhere in
            // the 1.0–1.5 m/s band is fully walking and only the cadence changes.
            gait.children = new[]
            {
                Child(idle, 0f, 1f),
                Child(walk, StrollSpeed, StrollSpeed / WalkClipSpeed),
                Child(walk, WalkClipSpeed, 1f),
            };

            controller.layers[0].stateMachine.defaultState = locomotion;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"NpcAnimatorBuilder — rebuilt {ControllerPath}\n" +
                $"  Locomotion blend: idle 0 / stroll {StrollSpeed} (clip at " +
                $"{StrollSpeed / WalkClipSpeed:0.00}x) / walk {WalkClipSpeed} m/s\n" +
                "  Joe's Humanoid clips, retargeted — the NPC pack ships no animation of its own\n" +
                "  No run, no hit, no panic: U18 owns the run-over reaction, U19 the fleeing",
                controller);
        }

        private static void Wipe(AnimatorController controller)
        {
            var stateMachine = controller.layers[0].stateMachine;

            foreach (var transition in stateMachine.anyStateTransitions)
                stateMachine.RemoveAnyStateTransition(transition);
            foreach (var state in stateMachine.states)
                stateMachine.RemoveState(state.state);
            while (controller.parameters.Length > 0)
                controller.RemoveParameter(0);

            // RemoveState drops the state but leaves its BlendTree as an orphaned sub-asset.
            foreach (var orphan in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller))
                         .OfType<BlendTree>())
            {
                Object.DestroyImmediate(orphan, true);
            }
        }

        private static ChildMotion Child(Motion motion, float threshold, float timeScale) => new ChildMotion
        {
            motion = motion,
            threshold = threshold,
            timeScale = timeScale,
            directBlendParameter = "Speed",
        };

        private static AnimationClip FindClip(string fileName, string clipName)
        {
            var path = $"{CharactersPath}/{fileName}";
            var clip = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == clipName);

            if (clip == null) Debug.LogError($"NpcAnimatorBuilder: no clip '{clipName}' in {path}.");
            return clip;
        }
    }
}
