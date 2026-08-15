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

        /// <summary>
        /// Playback rate for the swim cycle. A first guess, not a measurement: unlike the sprint
        /// clip there is nothing to match it against, because the controller moves the swimmer at
        /// 3 m/s regardless and water gives the eye no stride length to check the cadence against.
        /// Tune it by watching, and only if the stroke reads as too frantic or too slow.
        /// </summary>
        private const float SwimClipSpeed = 1f;

        /// <summary>
        /// Water is entered by walking, not by a key press, so this blend is the whole transition
        /// from upright to prone. Longer than the gait crossfade, or Joe snaps flat in a fifth of a
        /// second the moment the seabed drops past the wade depth.
        /// </summary>
        private const float SwimCrossfadeSec = 0.35f;

        [MenuItem("The Block/Build Joe Animator", priority = 20)]
        public static void Build()
        {
            var idle = FindClip("Joe.fbx", "Joe_Idle");
            var walk = FindClip("Joe_Walking.fbx", "Joe_Walk");
            var sprint = FindClip("Joe_Sprint.fbx", "Joe_Sprint");
            var jump = FindClip("Joe_Jumping.fbx", "Joe_Jump");
            if (idle == null || walk == null || sprint == null || jump == null) return;

            // Optional: without it the enter/exit machine falls back to config's quick-enter
            // timings, which is a real path in the game rather than a degraded one.
            var enterCar = FindClip("Joe_EnterCar.fbx", "Joe_EnterCar", optional: true);

            // Optional too, and its absence is more visible: without it the bike is ridden by a
            // standing Joe. The bike still works, which is why this is a warning and not an error.
            var ride = FindClip("Joe_Driving.fbx", "Joe_Ride", optional: true);

            // Optional as well: without it deep water is swum in a running pose, which is silly but
            // not broken — the buoyancy and the speed change are the controller's, not the clip's.
            var swim = FindClip("Joe_Swim.fbx", "Joe_Swim", optional: true);

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                             ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            Wipe(controller);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("EnterCar", AnimatorControllerParameterType.Bool);
            // Declared whether or not the clip exists: PlayerAnimator sets it on every mount, and
            // SetBool against a parameter the graph does not have warns once per call forever.
            controller.AddParameter("Ride", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Swim", AnimatorControllerParameterType.Bool);

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
            // ...but never out of a vehicle. A trigger left set from the frame before E was pressed
            // would otherwise have the driver hop in his seat.
            toJump.AddCondition(AnimatorConditionMode.IfNot, 0f, "EnterCar");
            toJump.AddCondition(AnimatorConditionMode.IfNot, 0f, "Ride");
            // ...nor in the water. The controller makes Space inert while swimming, so this only
            // catches a trigger set on the last dry frame — but that is exactly the frame you wade
            // in on, and a hop mid-stroke would read as a bug.
            toJump.AddCondition(AnimatorConditionMode.IfNot, 0f, "Swim");
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

            // Getting into a car. One state, played once, holding its last frame — which IS the
            // seated pose, so the whole drive is the tail of the entry animation. That is the web
            // build's arrangement too (`clampWhenFinished`), and it means no separate driving clip.
            if (enterCar != null)
            {
                var enterState = stateMachine.AddState("EnterCar");
                enterState.motion = enterCar;

                var toEnter = stateMachine.AddAnyStateTransition(enterState);
                toEnter.AddCondition(AnimatorConditionMode.If, 0f, "EnterCar");
                toEnter.hasExitTime = false;
                toEnter.hasFixedDuration = true;
                // Short, so the clip's first frame — standing at the door — is reached before the
                // door starts to swing at 25% progress.
                toEnter.duration = 0.1f;
                toEnter.canTransitionToSelf = false;

                var outOfCar = enterState.AddTransition(locomotion);
                outOfCar.AddCondition(AnimatorConditionMode.IfNot, 0f, "EnterCar");
                // No exit time: leaving is a key press part-way through a held pose, not the clip
                // running out. It never runs out — that is the point of holding the last frame.
                outOfCar.hasExitTime = false;
                outOfCar.hasFixedDuration = true;
                outOfCar.duration = CrossfadeSec;
            }

            // Riding. No walk-up clip and nothing to play through: the pose is simply held, looping,
            // for as long as the rider is on the bike. That is the difference between a car and a
            // motorcycle stated in one state — and the jetski (U24) reuses this exact one.
            if (ride != null)
            {
                var rideState = stateMachine.AddState("Ride");
                rideState.motion = ride;

                var toRide = stateMachine.AddAnyStateTransition(rideState);
                toRide.AddCondition(AnimatorConditionMode.If, 0f, "Ride");
                toRide.hasExitTime = false;
                toRide.hasFixedDuration = true;
                toRide.duration = CrossfadeSec;
                toRide.canTransitionToSelf = false;

                var offBike = rideState.AddTransition(locomotion);
                offBike.AddCondition(AnimatorConditionMode.IfNot, 0f, "Ride");
                offBike.hasExitTime = false;
                offBike.hasFixedDuration = true;
                offBike.duration = CrossfadeSec;
            }

            // Swimming. A looping cycle held for as long as the water is deep, same shape as Ride —
            // but entered by where the body IS rather than by a key press, so the crossfade is what
            // sells wading out into it. Longer than the gait crossfade on purpose: standing up out
            // of the water is a slower change of posture than walk → jog.
            if (swim != null)
            {
                var swimState = stateMachine.AddState("Swim");
                swimState.motion = swim;
                swimState.speed = SwimClipSpeed;

                var toSwim = stateMachine.AddAnyStateTransition(swimState);
                toSwim.AddCondition(AnimatorConditionMode.If, 0f, "Swim");
                toSwim.hasExitTime = false;
                toSwim.hasFixedDuration = true;
                toSwim.duration = SwimCrossfadeSec;
                toSwim.canTransitionToSelf = false;

                var ashore = swimState.AddTransition(locomotion);
                ashore.AddCondition(AnimatorConditionMode.IfNot, 0f, "Swim");
                ashore.hasExitTime = false;
                ashore.hasFixedDuration = true;
                ashore.duration = SwimCrossfadeSec;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"JoeAnimatorBuilder — rebuilt {ControllerPath}\n" +
                $"  Locomotion blend: idle 0 / walk {WalkSpeed} / sprint {SprintSpeed} m/s " +
                $"(sprint plays at {SprintSpeed / SprintClipSpeed:0.00}x so its cadence matches)\n" +
                "  Jump: Any State on the Jump trigger, back to Locomotion on Grounded\n" +
                (enterCar == null
                    ? "  EnterCar: NO CLIP — enter/exit falls back to config's quick-enter timings\n"
                    : $"  EnterCar: {enterCar.length:0.00}s, Any State on the EnterCar bool, holds " +
                      "its last frame as the seated pose\n") +
                (ride == null
                    ? "  Ride: NO CLIP — the bike gets ridden by a standing Joe\n"
                    : $"  Ride: {ride.length:0.00}s looping, Any State on the Ride bool\n") +
                (swim == null
                    ? "  Swim: NO CLIP — deep water is swum in a running pose\n"
                    : $"  Swim: {swim.length:0.00}s looping at {SwimClipSpeed:0.00}x, Any State on " +
                      $"the Swim bool, {SwimCrossfadeSec:0.00}s both ways\n") +
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

        /// <summary>
        /// Pulls a named clip out of an FBX's sub-assets, skipping Unity's preview clips.
        ///
        /// <paramref name="optional"/> downgrades a miss to a warning: some clips have a working
        /// fallback and their absence is a state of the project, not a mistake.
        /// </summary>
        private static AnimationClip FindClip(string fileName, string clipName, bool optional = false)
        {
            var path = $"{CharactersPath}/{fileName}";
            var clip = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == clipName);

            if (clip != null) return clip;

            var message = $"JoeAnimatorBuilder: no clip '{clipName}' in {path}.";
            if (optional) Debug.LogWarning($"{message} Skipping the states that need it.");
            else Debug.LogError($"{message} Import it first.");

            return null;
        }
    }
}
