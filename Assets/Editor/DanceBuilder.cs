using System.Linq;
using TheBlock.Core;
using TheBlock.Minigame.Rhythm;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Builds U22's two bodies and the one controller they share: the player-dancer on the stage and
    /// Remy grooving beside him on the sand.
    ///
    /// <b>One controller for both.</b> A Humanoid clip is stored as avatar-relative muscle curves,
    /// so the same eight states drive Joe's rig and Remy's without either knowing about the other -
    /// which is what makes the duet cost one asset instead of two. Its DEFAULT state is
    /// <c>Dance_Stand</c> (Remy's own idle), so a giver dropped in the world just stands there and
    /// only cross-fades into the groove when a routine starts.
    ///
    /// <b>Win and Fail have no exit transition, on purpose.</b> They are terminal: the routine is
    /// over, they clamp on their last frame and hold it. Every other one-shot has an exit back to
    /// the groove, so a flourish plays and the body carries on dancing.
    ///
    /// Called by <see cref="MissionBuilder"/>; not its own menu item, because a dancer with no
    /// campaign to belong to is not a thing anyone wants to build.
    /// </summary>
    public static class DanceBuilder
    {
        private const string ClipFolder = "Assets/Animation/Dance";
        private const string ControllerPath = "Assets/Animation/Dance/Dance.controller";
        private const string RemyPrefab = "Assets/Prefabs/Npc/Ped_Remy.prefab";

        // Joe's two white FBX materials used to be rebound here, because the stage dancer was a
        // fresh instantiation of Joe.fbx and rendered pure white. U29 moved that table into
        // CharacterPrefabBuilder along with the instantiation itself: the stage wears a roster
        // prefab now, and that prefab is already textured whoever is in it.

        /// <summary>Remy's own standing idle, so the giver is not a man b-boying at nobody.</summary>
        private const string StandState = "Dance_Stand";

        private static readonly string[] OneShots =
        {
            "Dance_Hit1", "Dance_Hit2", "Dance_Hit3", "Dance_Hit4", "Dance_Miss",
        };

        private static readonly string[] Terminal = { "Dance_Win", "Dance_Fail" };

        public static (Dancer dancer, Animator giver) Build(
            GameObject root, TheBlockConfig.RhythmSpec spec, System.Text.StringBuilder log)
        {
            var controller = BuildController(log);
            if (controller == null) return (null, null);

            var dancer = BuildDancer(root, spec, controller, log);
            var giver = BuildGiver(spec, controller, log);
            return (dancer, giver);
        }

        // ── the controller ────────────────────────────────────────────────────────────────────

        private static AnimatorController BuildController(System.Text.StringBuilder log)
        {
            var idle = Clip("Dance_Idle");
            if (idle == null)
            {
                log.AppendLine("  dance: no Dance_Idle clip - run The Block → Import Dance Clips first.");
                return null;
            }

            // Rebuilt from scratch every run rather than patched: a controller half-edited across
            // two versions of this file is the kind of asset nobody can review.
            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var machine = controller.layers[0].stateMachine;

            var stand = machine.AddState(StandState);
            stand.motion = StandClip();
            stand.writeDefaultValues = false;
            machine.defaultState = stand;

            var groove = machine.AddState("Dance_Idle");
            groove.motion = idle;
            groove.writeDefaultValues = false;

            var partner = machine.AddState("Dance_Partner");
            partner.motion = Clip("Dance_Partner");
            partner.writeDefaultValues = false;

            foreach (var name in OneShots)
            {
                var state = machine.AddState(name);
                state.motion = Clip(name);
                state.writeDefaultValues = false;

                // Back to the groove when the flourish runs out. No condition - exit time IS the
                // condition, which is what makes a one-shot self-returning with no parameters at all.
                var exit = state.AddTransition(groove);
                exit.hasExitTime = true;
                exit.exitTime = 1f;
                exit.duration = 0.2f;
                exit.hasFixedDuration = true;
            }

            foreach (var name in Terminal)
            {
                var state = machine.AddState(name);
                state.motion = Clip(name);
                state.writeDefaultValues = false;
            }

            AssetDatabase.SaveAssets();
            log.AppendLine(
                $"  dance controller: {machine.states.Length} states, default {StandState}, " +
                $"{OneShots.Length} self-returning, {Terminal.Length} terminal → {ControllerPath}");
            return controller;
        }

        private static AnimationClip Clip(string name) =>
            AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ClipFolder}/{name}.anim");

        /// <summary>
        /// Remy's imported idle, borrowed as the standing pose. Humanoid, so it plays on Joe too -
        /// which matters because the dancer sits in this state until a routine begins.
        /// </summary>
        private static AnimationClip StandClip() =>
            AssetDatabase.LoadAllAssetsAtPath(PeopleImporter.IdlePath("Remy"))
                .OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == PeopleImporter.IdleClip("Remy"));

        // ── the two bodies ────────────────────────────────────────────────────────────────────

        private static Dancer BuildDancer(
            GameObject root, TheBlockConfig.RhythmSpec spec, AnimatorController controller,
            System.Text.StringBuilder log)
        {
            const string name = "Stage Dancer";
            var existing = root.transform.Find(name);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);

            var dancer = go.AddComponent<Dancer>();

            // ── the body is the ROSTER's, and this is the fault U29 came here to fix ──────────
            //
            // This used to instantiate Joe.fbx directly, which meant picking Jody on the character
            // screen left Joe on the stage. The web build carries the same fix and names it in
            // dancer.ts's own header: "picking the female character still put joe on stage".
            //
            // CharacterPrefabBuilder does the instantiating now, so the white-materials rebind that
            // lived here went with it - a Joe prefab is a Joe prefab wherever it is put.
            log.AppendLine($"  {CharacterPrefabBuilder.DressStageDancer()}");

            var animator = go.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                log.AppendLine(
                    "  ⚠ dance: the stage dancer has no body - run The Block → Build Characters, " +
                    "then this again. The routine will play against an empty stage until then.");
            }
            else
            {
                animator.runtimeAnimatorController = controller;
            }

            // ── the boom goes through UNCONVERTED, and that is not an oversight ────────────────
            //
            // The player's boom uses Convert.ModelOffset. This one must not, and the two are
            // reconciled by the ORIGINAL's own numbers: its player camera offset is z = +2.5
            // ("GTA-style third-person behind view") while its dance camera offset is z = −5.0
            // ("−z sits behind their back"). Opposite signs for the same intent, in one codebase.
            //
            // The reason is what each offset is relative to. The web's player model is π-rotated
            // inside its holder, so "behind" in that holder's frame is +z; the dancer is a raw
            // Mixamo body, which faces +z in three.js AND in Unity, so "behind" is −z in both.
            // ModelOffset's job is to undo that π - apply it to a body that never had one and the
            // camera lands in the dancer's face with Remy behind the lens.
            //
            // The general rule this is an instance of: a conversion belongs to a coordinate's
            // PROVENANCE, not to its shape. Two Vector3s that look identical can need different
            // treatment, which is the trap the `model-offset-x-passes-through` memory names.
            var offset = spec.World.Camera.Offset;
            var boom = new Vector3(offset.X, offset.Y, offset.Z);

            dancer.Configure(animator, spec.Dancer.CrossFadeSec, boom, spec.World.Camera.LookY);

            go.SetActive(false); // revealed when the routine takes the stage

            log.AppendLine(
                $"  stage dancer: roster body + {controller.name}, boom {boom} (raw - see the " +
                $"comment), lookY {spec.World.Camera.LookY}, crossfade {spec.Dancer.CrossFadeSec}s");
            return dancer;
        }

        private static Animator BuildGiver(
            TheBlockConfig.RhythmSpec spec, AnimatorController controller, System.Text.StringBuilder log)
        {
            const string name = "Beach Dancer";
            var existing = GameObject.Find(name);
            if (existing != null) Object.DestroyImmediate(existing);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RemyPrefab);
            if (prefab == null)
            {
                log.AppendLine($"  dance: {RemyPrefab} is missing - run Build Pedestrians. No giver.");
                return null;
            }

            var npc = spec.World.Npc;
            var at = Convert.Pos(new Vector3(npc.X, 0f, npc.Z));

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = name;
            instance.transform.SetPositionAndRotation(at, Convert.RotFromRadians(npc.Yaw));

            // Never bound to a seed, so the crowd's own script never ticks him - the same trick the
            // delivery targets and the cashier use.
            if (instance.TryGetComponent<TheBlock.Npc.Pedestrian>(out var pedestrian))
                pedestrian.enabled = false;

            var animator = instance.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                log.AppendLine("  dance: Ped_Remy has no Animator - no giver.");
                return null;
            }

            animator.runtimeAnimatorController = controller;

            // He is the shot's second figure for the whole routine, so he must keep being posed
            // whatever the culling volumes think - the crowd's CullCompletely would freeze him
            // mid-groove the moment the stage camera framed him from behind.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            log.AppendLine(
                $"  beach dancer: Remy at {at:F1}, yaw {Convert.Yaw(npc.Yaw) * Mathf.Rad2Deg:0.#}°, " +
                $"talk r{npc.TalkRadius:0.#}");
            return animator;
        }
    }
}
