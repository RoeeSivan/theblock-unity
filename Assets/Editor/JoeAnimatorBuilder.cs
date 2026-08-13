using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Rebuilds <c>Assets/Animation/Joe.controller</c> from code.
    ///
    /// Same reasoning as WorldBuilder: a graph built by hand in the Animator window is invisible in
    /// review and impossible to reproduce, while this file states the whole machine in twenty lines
    /// and can be re-run after any clip lands.
    ///
    /// It rebuilds the existing asset in place rather than recreating it, so the GUID survives and
    /// <c>Player_Joe</c>'s Animator keeps its reference.
    /// </summary>
    public static class JoeAnimatorBuilder
    {
        private const string ControllerPath = "Assets/Animation/Joe.controller";
        private const string CharactersPath = "Assets/Models/Characters";

        /// <summary>Gait blend thresholds, in gameplay m/s — the same numbers PlayerController moves at.</summary>
        private const float WalkSpeed = 2.0f;
        private const float SprintSpeed = 7.0f;

        /// <summary>
        /// Root-motion speed the sprint clip was authored at. Root motion is off — the controller
        /// drives movement — so the clip's own travel is ignored and only its cadence matters. Played
        /// at 1.0 the feet would skate, because the legs cycle for 5.58 m/s while the body covers 7.
        /// </summary>
        private const float SprintClipSpeed = 5.58f;

        /// <summary>Gait blend time from <c>config.player.animCrossfadeSec</c>.</summary>
        private const float CrossfadeSec = 0.18f;

        [MenuItem("The Block/Build Joe Animator", priority = 20)]
        public static void Build()
        {
            var idle = FindClip("Joe.fbx", "Joe_Idle");
            var walk = FindClip("Joe_Walking.fbx", "Joe_Walk");
            var sprint = FindClip("Joe_Sprint.fbx", "Joe_Sprint");
            var jump = FindClip("Joe_Jumping.fbx", "Joe_Jump");
            if (idle == null || walk == null || sprint == null || jump == null) return;

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                             ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            Wipe(controller);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);

            // One 1-D tree covers the whole gait ladder. Jog (4.5 m/s) needs no clip and no state of
            // its own: it is simply where the blend sits between walk and sprint.
            var locomotion = controller.CreateBlendTreeInController("Locomotion", out var gait);
            gait.name = "Gait";
            gait.blendType = BlendTreeType.Simple1D;
            gait.blendParameter = "Speed";
            gait.useAutomaticThresholds = false;
            gait.children = new[]
            {
                Child(idle, 0f, 1f),
                Child(walk, WalkSpeed, 1f),
                Child(sprint, SprintSpeed, SprintSpeed / SprintClipSpeed),
            };

            var stateMachine = controller.layers[0].stateMachine;
            stateMachine.defaultState = locomotion;

            var jumpState = stateMachine.AddState("Jump");
            jumpState.motion = jump;

            // Any State, so a jump reads instantly from any gait rather than waiting out a cycle.
            var toJump = stateMachine.AddAnyStateTransition(jumpState);
            toJump.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
            toJump.hasExitTime = false;
            // Fixed duration, or `duration` is read as a fraction of the clip: 0.18 of the 1.9 s jump
            // is a third of a second, and Joe keeps jogging for a beat after he lands.
            toJump.hasFixedDuration = true;
            toJump.duration = 0.05f;
            toJump.canTransitionToSelf = false;

            // Landing is the signal to leave, not the clip running out: a long fall outlasts the clip.
            var toLocomotion = jumpState.AddTransition(locomotion);
            toLocomotion.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            toLocomotion.hasExitTime = false;
            toLocomotion.hasFixedDuration = true;
            toLocomotion.duration = CrossfadeSec;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"JoeAnimatorBuilder — rebuilt {ControllerPath}\n" +
                $"  Locomotion blend: idle 0 / walk {WalkSpeed} / sprint {SprintSpeed} m/s " +
                $"(sprint plays at {SprintSpeed / SprintClipSpeed:0.00}x so its cadence matches)\n" +
                "  Jump: Any State on the Jump trigger, back to Locomotion on Grounded\n" +
                "  No clip yet for exhausted or falling — both fall through to the states above",
                controller);
        }

        /// <summary>Clears states, parameters and any blend trees left embedded from a previous run.</summary>
        private static void Wipe(AnimatorController controller)
        {
            var stateMachine = controller.layers[0].stateMachine;

            foreach (var transition in stateMachine.anyStateTransitions)
                stateMachine.RemoveAnyStateTransition(transition);
            foreach (var state in stateMachine.states)
                stateMachine.RemoveState(state.state);
            foreach (var child in stateMachine.stateMachines)
                stateMachine.RemoveStateMachine(child.stateMachine);
            while (controller.parameters.Length > 0)
                controller.RemoveParameter(0);

            // RemoveState drops the state but leaves its BlendTree behind as an orphaned sub-asset.
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

        /// <summary>Pulls a named clip out of an FBX's sub-assets, skipping Unity's preview clips.</summary>
        private static AnimationClip FindClip(string fileName, string clipName)
        {
            var path = $"{CharactersPath}/{fileName}";
            var clip = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == clipName);

            if (clip == null)
                Debug.LogError($"JoeAnimatorBuilder: no clip '{clipName}' in {path}. Import it first.");

            return clip;
        }
    }
}
