using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Builds <c>Assets/Animation/Npc.controller</c> and one override controller per character -
    /// the whole animation state of a pedestrian, which is idle, walking, and the blend between.
    ///
    /// <b>Six faces, six pairs of clips, ONE graph.</b> Each of the original's characters carries its
    /// own Mixamo idle and walk (see <see cref="PeopleImporter"/>), so the naive arrangement is six
    /// controllers. The objection to overrides is real - an <c>AnimatorOverrideController</c> can
    /// swap the CLIPS in a blend tree but not the thresholds or time scales, which live in the base
    /// asset - and six characters whose walks were authored at 0.85 to 1.65 m/s would each need
    /// their own numbers.
    ///
    /// <b>The fix is to make the blend parameter dimensionless.</b> <c>Speed</c> is not metres per
    /// second here; it is <c>moveSpeed / walkClipSpeed</c>, a RATIO. One set of thresholds is then
    /// correct for every character, overrides become viable, and U18's hit reaction lands as one new
    /// state in one graph instead of six.
    ///
    /// Same idiom and same reasoning as <see cref="JoeAnimatorBuilder"/>: a graph built by hand in
    /// the Animator window cannot be reviewed or reproduced.
    /// </summary>
    public static class NpcAnimatorBuilder
    {
        private const string ControllerPath = "Assets/Animation/Npc.controller";
        private const string OverrideFolder = "Assets/Animation/Npc";

        /// <summary>
        /// The knockdown state's name, which is what <c>Pedestrian</c> cross-fades to by string - so
        /// the name lives there and this file agrees with it.
        ///
        /// <b>The state is deliberately not overridden per character.</b> Idle and walk are, because
        /// each of the six was authored on its own body; the hit is one Humanoid clip that retargets
        /// onto all of them, so there is one state holding one clip and the override controllers say
        /// nothing about it.
        /// </summary>
        private const string HitState = TheBlock.Npc.Pedestrian.HitState;

        /// <summary>The state everyone returns to once the reaction is over.</summary>
        private const string LocomotionState = TheBlock.Npc.Pedestrian.LocomotionState;

        /// <summary>The character whose clips fill the base graph. The others override them.</summary>
        private const string BaseCharacter = "Sophie";

        /// <summary>
        /// What a walk clip is assumed to travel at when its own reading is useless - a Mixamo "in
        /// place" export, or one whose root got baked. Roughly the middle of the crowd's band.
        /// </summary>
        internal const float FallbackWalkSpeed = 1.35f;

        /// <summary>Below this the clip is not really travelling and the reading is not trusted.</summary>
        internal const float MinTrustedWalkSpeed = 0.2f;

        /// <summary>
        /// The dimensionless gait ladder: ratio of ground speed to the clip's own authored speed.
        ///
        /// Three walk children, not one. The crowd walks at 1.0-1.5 m/s against clips authored at
        /// 0.85-1.65, so the ratio lands anywhere in roughly 0.6-1.4 - and every point of that range
        /// blends walk-to-walk, never walk-to-idle, which is the foot-sliding fault the old
        /// <c>StrollSpeed</c> anchor existed to avoid. The 1.4 child is there because a 1-D tree
        /// clamps past its last threshold: without it the fastest walker on the slowest clip skates.
        /// </summary>
        private static readonly float[] WalkRatios = { 0.6f, 1.0f, 1.4f };

        [MenuItem("The Block/Build NPC Animator", priority = 21)]
        public static void BuildMenu() => Build();

        public static string Build()
        {
            var log = new StringBuilder();

            var baseIdle = FindClip(PeopleImporter.IdlePath(BaseCharacter), PeopleImporter.IdleClip(BaseCharacter));
            var baseWalk = FindClip(PeopleImporter.WalkPath(BaseCharacter), PeopleImporter.WalkClip(BaseCharacter));
            if (baseIdle == null || baseWalk == null)
            {
                const string message =
                    "NpcAnimatorBuilder: the base character's clips are missing. Run " +
                    "The Block → Import People (slow) first.";
                Debug.LogError(message);
                return message;
            }

            // Rebuilt IN PLACE so the GUID survives: the scene's pedestrian prefabs reference this
            // asset, and a fresh one would leave them pointing at nothing.
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                             ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            Wipe(controller);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

            var locomotion = controller.CreateBlendTreeInController(LocomotionState, out var gait);
            gait.name = "Gait";
            gait.blendType = BlendTreeType.Simple1D;
            gait.blendParameter = "Speed";
            gait.useAutomaticThresholds = false;

            var children = new List<ChildMotion> { Child(baseIdle, 0f, 1f) };
            foreach (var ratio in WalkRatios) children.Add(Child(baseWalk, ratio, ratio));
            gait.children = children.ToArray();

            controller.layers[0].stateMachine.defaultState = locomotion;

            log.AppendLine(
                $"  base {ControllerPath}: idle 0 / walk " +
                string.Join(" / ", WalkRatios.Select(r => $"{r:0.#}×")) + "  (Speed is a RATIO, not m/s)");

            // --- U18's knockdown ------------------------------------------------------------------
            //
            // No transitions in or out, and no trigger parameter. Pedestrian cross-fades to this
            // state at impact and back to Locomotion when the body is recycled, because the reaction
            // has a clock of its own - the arc, the stain and the fade all hang off it - and a
            // transition graph would give the same reaction a second, disagreeing one.
            var hit = HitClipImporter.Load();
            if (hit == null)
            {
                log.AppendLine("  ⚠ no hit clip - run The Block → Import Hit Clip. The crowd is unhittable.");
            }
            else
            {
                var hitState = controller.layers[0].stateMachine.AddState(HitState);
                hitState.motion = hit;
                hitState.writeDefaultValues = false;
                log.AppendLine(
                    $"  {HitState,-11} {hit.name} {hit.length:0.00} s, root motion ON at runtime only, " +
                    "shared by all six");
            }

            EditorUtility.SetDirty(controller);

            // --- one override per character -----------------------------------------------------

            if (!AssetDatabase.IsValidFolder(OverrideFolder))
                AssetDatabase.CreateFolder("Assets/Animation", "Npc");

            int built = 0;
            foreach (var name in PeopleImporter.Names)
            {
                var idle = FindClip(PeopleImporter.IdlePath(name), PeopleImporter.IdleClip(name));
                var walk = FindClip(PeopleImporter.WalkPath(name), PeopleImporter.WalkClip(name));
                if (idle == null)
                {
                    log.AppendLine($"  {name,-11} SKIPPED - no idle clip");
                    continue;
                }

                var path = OverridePath(name);
                var over = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
                if (over == null)
                {
                    over = new AnimatorOverrideController();
                    AssetDatabase.CreateAsset(over, path);
                }

                over.runtimeAnimatorController = controller;

                // Sophie's own override is an identity map, kept so NpcBuilder has ONE rule to
                // follow rather than a rule plus an exception.
                var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>
                {
                    new(baseIdle, idle),
                    new(baseWalk, walk ?? baseWalk),
                };
                over.ApplyOverrides(overrides);
                EditorUtility.SetDirty(over);

                float speed = WalkSpeed(walk);
                log.AppendLine(
                    $"  {name,-11} → {System.IO.Path.GetFileName(path)}  walk {speed:0.00} m/s" +
                    (walk == null ? "  ⚠ NO WALK CLIP, idles only"
                        : speed <= MinTrustedWalkSpeed + 0.001f ? "  ⚠ measured ~0, using the fallback" : ""));
                built++;
            }

            AssetDatabase.SaveAssets();

            var report =
                $"NpcAnimatorBuilder - {built}/{PeopleImporter.Names.Length} override(s)\n{log}" +
                "  No run and no panic: U19 owns the fleeing";
            Debug.Log(report, controller);
            return report;
        }

        internal static string OverridePath(string name) => $"{OverrideFolder}/Npc_{name}.overrideController";

        /// <summary>
        /// The ground speed a character's walk clip was authored at - the denominator of the
        /// dimensionless <c>Speed</c> parameter. <see cref="NpcBuilder"/> bakes it onto the prefab.
        /// </summary>
        internal static float WalkSpeed(AnimationClip walk)
        {
            if (walk == null) return FallbackWalkSpeed;
            float speed = walk.averageSpeed.magnitude;
            return speed >= MinTrustedWalkSpeed ? speed : FallbackWalkSpeed;
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

        private static AnimationClip FindClip(string path, string clipName)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == clipName);

            if (clip == null) Debug.LogWarning($"NpcAnimatorBuilder: no clip '{clipName}' in {path}.");
            return clip;
        }
    }
}
