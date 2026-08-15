using System.Collections.Generic;
using System.Linq;
using TheBlock.Npc;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Turns Chepatack's <c>npc_casual_set_00</c> characters into pedestrians — U16.
    ///
    /// Same idiom as <see cref="CarBuilder"/> and <see cref="MotorcycleBuilder"/>: the prefab is a
    /// build product, generated from a vendor asset plus a list of decisions written down here,
    /// rather than a thing assembled by hand in the Inspector and impossible to review.
    ///
    /// THE PACK IS WELL BUILT AND TOO HEAVY, both at once. Each of the twelve character prefabs
    /// carries a real 5-level <c>LODGroup</c> and an Animator already bound to
    /// <c>npc_hmn_01mAvatar</c>, so there is no bone rebinding to do — but that LODGroup sits over
    /// thirty-odd SkinnedMeshRenderers (six parts × five LODs), and a LODGroup only decides what
    /// the camera draws; every one of those is still skinned each frame. See <see cref="KeepLods"/>
    /// for what that cost and what is done about it. This builder adds the game's own components,
    /// strips the ladder to two rungs, and fixes one real fault.
    ///
    /// THAT FAULT: the prefabs reference <c>npc_casual_set_00/Materials</c>, which is the BUILT-IN
    /// pipeline's Standard shader. This project is URP. The pack ships the URP set beside it as
    /// <c>MaterialsUPR</c> — same 54 names, <c>Universal Render Pipeline/Lit</c> — and nothing
    /// points at it, so dropped in as they are, every pedestrian renders magenta. The rebind below
    /// is by NAME, one folder to the other.
    /// </summary>
    public static class NpcBuilder
    {
        private const string PackRoot = "Assets/npc_casual_set_00";
        private const string PrefabFolder = "Assets/Prefabs/Npc";
        private const string GeneratedMaterialFolder = "Assets/Materials/Npc/Generated";
        private const string ControllerPath = "Assets/Animation/Npc.controller";

        /// <summary>Capsule from <c>npc.config.ts</c>: total height 2 * (halfHeight + radius) = 1.6 m.</summary>
        private const float ColliderRadius = 0.3f;
        private const float ColliderHalfHeight = 0.5f;

        /// <summary>
        /// Agent radius, narrower than the default Humanoid 0.5.
        ///
        /// Pavements in these districts run about 2 m wide, and two agents at 0.5 cannot pass on
        /// one — the avoidance solver puts one of them into the building. This is the AGENT's
        /// radius, not the bake's, which stays at the shared Humanoid type's.
        /// </summary>
        private const float AgentRadius = 0.3f;

        private const float AgentHeight = 1.7f;

        /// <summary>Shirt tints. Deliberately drab: this is Florentin on a weekday, not a parade.</summary>
        private static readonly Color[] TopTints =
        {
            new(1.00f, 1.00f, 1.00f), // untouched — the vendor's own colourway
            new(0.62f, 0.66f, 0.72f),
            new(0.78f, 0.72f, 0.58f),
            new(0.55f, 0.60f, 0.55f),
            new(0.72f, 0.55f, 0.52f),
            new(0.45f, 0.48f, 0.58f),
        };

        /// <summary>Node-name fragments that identify the head, whose material carries the face.</summary>
        private static readonly string[] HeadParts = { "_head", "_face" };

        /// <summary>Node-name fragments that identify the garment worn on the torso.</summary>
        private static readonly string[] TopParts = { "tshirt", "shirtopenrolled" };

        [MenuItem("The Block/Build Pedestrians", priority = 22)]
        public static void Build()
        {
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            if (controller == null)
            {
                Debug.LogError(
                    $"NpcBuilder: {ControllerPath} does not exist. Run The Block → Build NPC Animator first.");
                return;
            }

            var sources = AssetDatabase.FindAssets("t:Prefab", new[] { $"{PackRoot}/Prefabs" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => System.IO.Path.GetFileNameWithoutExtension(p).StartsWith("npc_csl_00_character_"))
                .OrderBy(p => p)
                .ToList();

            if (sources.Count == 0)
            {
                Debug.LogError(
                    $"NpcBuilder: no character prefabs under {PackRoot}/Prefabs. Is the pack imported?");
                return;
            }

            EnsureFolder(PrefabFolder);
            EnsureFolder(GeneratedMaterialFolder);

            var urpMaterials = LoadUrpMaterials();
            var faceMaterials = LoadFaceMaterials(urpMaterials);
            var tintCache = new Dictionary<string, Material[]>();
            var built = new List<string>();
            int rebound = 0, missingUrp = 0, smrsBefore = 0, smrsAfter = 0;

            foreach (var source in sources)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(source);
                if (prefab == null) continue;

                var instance = Object.Instantiate(prefab);
                instance.name = $"Ped_{prefab.name.Replace("npc_csl_00_character_", string.Empty)}";

                int smrBefore = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
                RebindToUrp(instance, urpMaterials, ref rebound, ref missingUrp);
                WireAgent(instance, controller);
                WireAppearance(instance, prefab.name, faceMaterials, tintCache);
                smrsBefore += smrBefore;
                smrsAfter += instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;

                var name = instance.name;
                PrefabUtility.SaveAsPrefabAsset(instance, $"{PrefabFolder}/{name}.prefab");
                Object.DestroyImmediate(instance);
                built.Add(name);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"NpcBuilder — {built.Count} pedestrian prefab(s) in {PrefabFolder}\n" +
                $"  {string.Join(", ", built)}\n" +
                $"  Materials rebound to MaterialsUPR: {rebound}" +
                (missingUrp > 0
                    ? $" — {missingUrp} had NO URP twin and stay on the built-in shader (they will render magenta)\n"
                    : " (the pack's prefabs all point at the BUILT-IN set, which is magenta under URP)\n") +
                $"  Skinned meshes: {smrsBefore} → {smrsAfter} across the set (LODs kept: " +
                $"{string.Join("/", KeepLods)}; the rest destroyed, not disabled)\n" +
                $"  NavMeshAgent r={AgentRadius} h={AgentHeight}, auto off-mesh traversal OFF so " +
                "Pedestrian owns the kerb\n" +
                "  Appearance: face material × shirt tint, both as shared assets so the SRP Batcher survives\n" +
                "  Drag these onto a CrowdSpawner's Pedestrian Prefabs list");
        }

        // --- materials ---------------------------------------------------------------------------

        private static Dictionary<string, Material> LoadUrpMaterials()
        {
            var map = new Dictionary<string, Material>(System.StringComparer.Ordinal);
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { $"{PackRoot}/MaterialsUPR" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null) map[material.name] = material;
            }

            return map;
        }

        /// <summary>The pack's ten face materials, split by the gender their name carries.</summary>
        private static Dictionary<char, Material[]> LoadFaceMaterials(Dictionary<string, Material> urp)
        {
            var byGender = new Dictionary<char, List<Material>> { ['m'] = new(), ['f'] = new() };

            foreach (var pair in urp)
            {
                // mtl_npc_hmn_face_01m_03_01 — the character letter after "face_01" is the gender.
                const string marker = "_face_01";
                int at = pair.Key.IndexOf(marker, System.StringComparison.Ordinal);
                if (at < 0 || at + marker.Length >= pair.Key.Length) continue;

                char gender = pair.Key[at + marker.Length];
                if (byGender.TryGetValue(gender, out var list)) list.Add(pair.Value);
            }

            return byGender.ToDictionary(p => p.Key, p => p.Value.OrderBy(m => m.name).ToArray());
        }

        /// <summary>
        /// Points every renderer at the URP twin of the material it currently uses.
        ///
        /// By name, because that is the only thing the two folders share — the GUIDs are unrelated
        /// and the vendor prefab hard-references the built-in ones. A material with no twin is left
        /// alone and counted, so a silently magenta pedestrian is a number in the log rather than a
        /// thing to discover in the Game view.
        /// </summary>
        private static void RebindToUrp(
            GameObject instance, Dictionary<string, Material> urp, ref int rebound, ref int missing)
        {
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    if (urp.TryGetValue(materials[i].name, out var replacement))
                    {
                        if (materials[i] == replacement) continue;
                        materials[i] = replacement;
                        changed = true;
                        rebound++;
                    }
                    else
                    {
                        missing++;
                    }
                }

                if (changed) renderer.sharedMaterials = materials;
            }
        }

        /// <summary>
        /// Tinted clones of one shirt material, generated once and shared by every pedestrian
        /// wearing it — the same call U13 made for the car park's eighteen paints, and for the same
        /// reason: a <see cref="MaterialPropertyBlock"/> would tint the shirt and drop the wearer
        /// out of the SRP Batcher.
        /// </summary>
        private static Material[] TintsFor(Material source, Dictionary<string, Material[]> cache)
        {
            if (source == null) return System.Array.Empty<Material>();
            if (cache.TryGetValue(source.name, out var cached)) return cached;

            var variants = new Material[TopTints.Length];
            for (int i = 0; i < TopTints.Length; i++)
            {
                var path = $"{GeneratedMaterialFolder}/{source.name}_t{i}.mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing != null)
                {
                    variants[i] = existing;
                    continue;
                }

                var variant = new Material(source) { name = $"{source.name}_t{i}" };
                variant.SetColor("_BaseColor", TopTints[i]);
                AssetDatabase.CreateAsset(variant, path);
                variants[i] = variant;
            }

            cache[source.name] = variants;
            return variants;
        }

        // --- components --------------------------------------------------------------------------

        /// <summary>
        /// LOD levels kept, by index into the vendor's five. Everything else is deleted.
        ///
        /// The pack's LODGroup is real, but a LODGroup only decides which renderers the CAMERA
        /// draws — every SkinnedMeshRenderer under it is still a live skinned mesh whose vertices
        /// are re-posed each frame whether or not it is shown. Five LODs × six parts is 30–33 of
        /// them per person, so a 90-person crowd was 2,960 skinned meshes being posed for 747 that
        /// were visible, and the frame time went with it. It also produced the "exploding
        /// pedestrian": a renderer that had sat unposed since spawn, swapped in by a LOD change,
        /// draws its first frame at bind pose against a skeleton that has long since walked off —
        /// which is a fan of black triangles reaching for the horizon.
        ///
        /// Two levels is the whole ladder a street crowd needs: full detail up close, and the ~800
        /// tri LOD2 for everything past that. Nothing here is ever examined at LOD1 distance for
        /// long enough to notice, and LOD3/4 are for a hundred metres out, where the animator is
        /// culled entirely anyway.
        /// </summary>
        private static readonly int[] KeepLods = { 0, 2 };

        /// <summary>Screen-height thresholds for the kept levels, then cull. Vendor's were 0.7/0.4/0.2/0.05/0.</summary>
        private static readonly float[] KeepLodHeights = { 0.25f, 0.02f };

        private static void WireAgent(GameObject instance, RuntimeAnimatorController controller)
        {
            StripLods(instance);

            if (instance.TryGetComponent<Animator>(out var animator))
            {
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false; // the agent drives movement, not the clip
                // Cull completely off-screen. The agent still moves the root — that is a
                // NavMeshAgent, not the animator — so a culled pedestrian keeps walking and is in
                // the right place when it comes back into view; only its limbs stop being posed
                // while nobody can see them. This was misread as the cause of the exploding
                // pedestrian in an earlier pass and switched to AlwaysAnimate, which doubled the
                // frame cost and fixed nothing: the real cause was renderers that had never been
                // posed at all (see <see cref="KeepLods"/>), and StripLods removes them.
                animator.cullingMode = AnimatorCullingMode.CullCompletely;
            }

            var agent = instance.GetComponent<NavMeshAgent>();
            if (agent == null) agent = instance.AddComponent<NavMeshAgent>();
            agent.radius = AgentRadius;
            agent.height = AgentHeight;
            agent.baseOffset = 0f;
            agent.acceleration = 8f;
            agent.angularSpeed = 360f;
            agent.stoppingDistance = 0.4f;
            agent.autoBraking = true;
            agent.autoRepath = true;
            // Good quality costs more than a crowd of forty is worth, and None lets people merge
            // into each other on a narrow pavement, which is the thing you actually notice.
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

            var capsule = instance.GetComponent<CapsuleCollider>();
            if (capsule == null) capsule = instance.AddComponent<CapsuleCollider>();
            capsule.radius = ColliderRadius;
            capsule.height = 2f * (ColliderHalfHeight + ColliderRadius);
            capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);
            // NO RIGIDBODY, and that is load-bearing: Crossing.IsClearOfTraffic decides whether the
            // road is busy by looking for attached Rigidbodies, so a pedestrian with one would make
            // every other pedestrian wait for it.

            if (!instance.TryGetComponent<Pedestrian>(out _)) instance.AddComponent<Pedestrian>();
        }

        /// <summary>
        /// Rebuilds the LODGroup with only <see cref="KeepLods"/> and DESTROYS the renderers of every
        /// other level. Destroyed, not disabled: a disabled SkinnedMeshRenderer is still a skinned
        /// mesh the animator owns, and the cost is the ownership.
        /// </summary>
        private static void StripLods(GameObject instance)
        {
            var group = instance.GetComponentInChildren<LODGroup>();
            if (group == null) return;

            var lods = group.GetLODs();
            var kept = new List<LOD>();
            var doomed = new List<Renderer>();

            for (int i = 0; i < lods.Length; i++)
            {
                int keepIndex = System.Array.IndexOf(KeepLods, i);
                if (keepIndex < 0)
                {
                    doomed.AddRange(lods[i].renderers);
                    continue;
                }

                var lod = lods[i];
                lod.screenRelativeTransitionHeight = KeepLodHeights[keepIndex];
                kept.Add(lod);
            }

            group.SetLODs(kept.ToArray());
            group.RecalculateBounds();

            foreach (var renderer in doomed)
            {
                if (renderer == null) continue;
                // The vendor puts each LOD renderer on its own child object with nothing else on
                // it, so the object goes with it. Anything else — a shared bone, a container — is
                // left standing and only the renderer is removed.
                var go = renderer.gameObject;
                bool bare = go.transform.childCount == 0 && go.GetComponents<Component>().Length <= 2;
                if (bare) Object.DestroyImmediate(go);
                else Object.DestroyImmediate(renderer);
            }
        }

        private static void WireAppearance(
            GameObject instance, string sourceName,
            Dictionary<char, Material[]> faces, Dictionary<string, Material[]> tintCache)
        {
            // npc_csl_00_character_01f_02 — the letter after the body-type digits is the gender.
            char gender = sourceName.Contains("f_", System.StringComparison.Ordinal) ? 'f' : 'm';
            faces.TryGetValue(gender, out var faceVariants);

            var heads = new List<Renderer>();
            var headSlots = new List<int>();
            var tops = new List<Renderer>();
            Material topSource = null;

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var name = renderer.name.ToLowerInvariant();

                if (HeadParts.Any(p => name.Contains(p)))
                {
                    int slot = System.Array.FindIndex(
                        renderer.sharedMaterials,
                        m => m != null && m.name.Contains("_face_", System.StringComparison.Ordinal));
                    if (slot >= 0)
                    {
                        heads.Add(renderer);
                        headSlots.Add(slot);
                    }
                }

                if (TopParts.Any(p => name.Contains(p)))
                {
                    tops.Add(renderer);
                    topSource ??= renderer.sharedMaterial;
                }
            }

            var appearance = instance.GetComponent<NpcAppearance>();
            if (appearance == null) appearance = instance.AddComponent<NpcAppearance>();
            appearance.Configure(
                faceVariants ?? System.Array.Empty<Material>(),
                heads.ToArray(), headSlots.ToArray(),
                TintsFor(topSource, tintCache), tops.ToArray());
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            var parts = path.Split('/');
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
