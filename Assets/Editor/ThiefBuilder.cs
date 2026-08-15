using System.Linq;
using TheBlock.Core;
using TheBlock.Missions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Convert = TheBlock.Core.Convert;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Builds M4's thief: one character with two bodies, a tinted jetski he rides and a runner he
    /// becomes when he beaches.
    ///
    /// <b>He is Peter, and that is a deliberate substitution.</b> The web ships two dedicated 52 MB
    /// Mixamo downloads for him — <c>driving-thief.fbx</c> and <c>Running.fbx</c> — 104 MB of body
    /// for one man who appears in one mission, against an LFS store already at GitHub's 1 GiB free
    /// ceiling. Peter is the sixth crowd character and the ONE the delivery run does not use as a
    /// customer, so he is already imported, already URP-bound, already height-normalised, and he is
    /// nobody else in the campaign. Swapping the prefab below is a one-line change if the exact
    /// model is ever wanted.
    ///
    /// His ski is a tinted clone of the player's, exactly as the web tints its own: same hull, dark
    /// red paint, so the two read apart at speed on open water. It carries no body and no collider
    /// at all — nothing ever hits the thief, and the buoys he skims are handled by
    /// <see cref="Missions.BuoyField"/>, which that file explains.
    /// </summary>
    public static class ThiefBuilder
    {
        private const string ThiefPrefab = "Assets/Prefabs/Npc/Ped_Peter.prefab";
        private const string SkiPrefab = "Assets/Prefabs/Vehicles/Jetski.prefab";
        private const string RideControllerPath = "Assets/Animation/Dance/ThiefRide.controller";
        private const string JoeRideClip = "Joe_Ride";
        private const string JoePath = "Assets/Models/Characters/Joe_Driving.fbx";

        public static ChaseThief Build(
            GameObject root, TheBlockConfig.ChaseSpec spec, System.Text.StringBuilder log)
        {
            const string name = "Chase Thief";
            var existing = root.transform.Find(name);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var holder = new GameObject(name);
            holder.transform.SetParent(root.transform, false);
            var thief = holder.AddComponent<ChaseThief>();

            var ski = BuildSki(holder.transform, spec, log);
            var (runner, animator) = BuildRunner(holder.transform, log);

            thief.SetBodies(ski, runner, animator);
            EditorUtility.SetDirty(thief);
            return thief;
        }

        // ── the ridden body ───────────────────────────────────────────────────────────────────

        private static Transform BuildSki(
            Transform parent, TheBlockConfig.ChaseSpec spec, System.Text.StringBuilder log)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SkiPrefab);
            if (prefab == null)
            {
                log.AppendLine("  thief: no Jetski.prefab — run The Block → Build Mission Vehicles.");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "Thief Ski";

            // Strip it down to visuals. The CONTROLLER goes first and on its own: it carries
            // [RequireComponent(typeof(Rigidbody))], so removing the body while it is still attached
            // fails silently ("Can't remove Rigidbody because JetskiController depends on it") and
            // leaves a half-stripped object behind — which is exactly what the first run of this did.
            if (instance.TryGetComponent<TheBlock.Vehicles.JetskiController>(out var controller))
                Object.DestroyImmediate(controller);

            foreach (var collider in instance.GetComponents<Collider>()) Object.DestroyImmediate(collider);
            if (instance.TryGetComponent<Rigidbody>(out var stale)) Object.DestroyImmediate(stale);

            var tinted = Tint(instance, spec.HullTint, log);
            var rider = MountRider(instance.transform, log);

            log.AppendLine($"  thief ski: {tinted} paint slot(s) tinted #{spec.HullTint:x6}" +
                           (rider ? ", rider mounted" : ", NO rider"));
            return instance.transform;
        }

        /// <summary>
        /// Repaints the hull so the two skis read apart. Clones the material first, because it is
        /// shared with the player's ski through the prefab — writing the shared asset would paint
        /// BOTH of them dark red, which is the version of this bug that looks like it works until
        /// the chase starts.
        /// </summary>
        private static int Tint(GameObject instance, int packed, System.Text.StringBuilder log)
        {
            var color = TheBlockConfig.ColorFromHex(packed);
            var painted = 0;

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;

                for (var i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (material == null) continue;
                    if (!material.name.ToLowerInvariant().Contains("carpaint")) continue;

                    var clone = new Material(material) { name = $"{material.name}_Thief" };

                    // baseColorFactor first: glTFast's imported shader has neither _BaseColor nor
                    // _Color, and writing a property a shader lacks is a SILENT no-op — the fault
                    // that left the Mustang the wrong colour for four units.
                    var wrote = false;
                    foreach (var property in new[] { "baseColorFactor", "_BaseColor", "_Color" })
                    {
                        if (!clone.HasProperty(property)) continue;
                        clone.SetColor(property, color);
                        wrote = true;
                    }

                    if (!wrote)
                        log.AppendLine($"  ⚠ '{material.name}' has no colour property — hull not tinted");

                    materials[i] = clone;
                    changed = true;
                    painted++;
                }

                if (changed) renderer.sharedMaterials = materials;
            }

            return painted;
        }

        /// <summary>
        /// Sits Peter on the ski in the shared riding pose.
        ///
        /// <c>Joe_Ride</c> is the clip the motorcycle and the jetski both use — the web keeps it in
        /// a file of its own for exactly that reason — and it is Humanoid, so it retargets onto
        /// Peter without a second import. A driverless ski fleeing across the bay is the bug the
        /// jetski's own config comment names.
        /// </summary>
        private static bool MountRider(Transform ski, System.Text.StringBuilder log)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ThiefPrefab);
            var clip = AssetDatabase.LoadAllAssetsAtPath(JoePath)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == JoeRideClip);

            if (prefab == null || clip == null)
            {
                log.AppendLine($"  ⚠ thief rider: prefab={prefab != null} rideClip={clip != null}");
                return false;
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(RideControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(RideControllerPath);
                var state = controller.layers[0].stateMachine.AddState("Ride");
                state.motion = clip;
                state.writeDefaultValues = false;
                controller.layers[0].stateMachine.defaultState = state;
                AssetDatabase.SaveAssets();
            }

            var seat = ski.Find("Seat");
            var rider = (GameObject)PrefabUtility.InstantiatePrefab(prefab, ski);
            rider.name = "Thief Rider";
            rider.transform.localPosition = seat != null ? seat.localPosition : new Vector3(0f, 0.4f, 0f);
            rider.transform.localRotation = Quaternion.identity;

            if (rider.TryGetComponent<TheBlock.Npc.Pedestrian>(out var pedestrian)) pedestrian.enabled = false;
            if (rider.TryGetComponent<CapsuleCollider>(out var capsule)) capsule.enabled = false;
            if (rider.TryGetComponent<Rigidbody>(out var body)) Object.DestroyImmediate(body);

            var animator = rider.GetComponentInChildren<Animator>(true);
            if (animator == null) return false;

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            // He is on open water with nothing near him, so the crowd's CullCompletely would freeze
            // him into a T-pose the moment the camera looked past him.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return true;
        }

        // ── the running body ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// The beach jog. Peter again, on the crowd's OWN locomotion blend tree — the same graph
        /// every pedestrian walks on, driven by hand at a jog rather than by <c>Pedestrian.Tick</c>.
        /// The web needs a dedicated run GLB here because its crowd characters have no such graph.
        /// </summary>
        private static (Transform, Animator) BuildRunner(Transform parent, System.Text.StringBuilder log)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ThiefPrefab);
            if (prefab == null)
            {
                log.AppendLine($"  thief: {ThiefPrefab} is missing — run Build Pedestrians. No runner.");
                return (null, null);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = "Thief Runner";
            instance.SetActive(false); // revealed when he beaches

            if (instance.TryGetComponent<TheBlock.Npc.Pedestrian>(out var pedestrian)) pedestrian.enabled = false;

            var animator = instance.GetComponentInChildren<Animator>(true);
            if (animator != null) animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            log.AppendLine("  thief runner: Peter on the crowd's locomotion tree (no extra import)");
            return (instance.transform, animator);
        }
    }
}
