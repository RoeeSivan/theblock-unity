using System.Collections.Generic;
using System.Linq;
using System.Text;
using TheBlock.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Turns the three imported bodies into swappable prefabs and wires the scene to wear them -
    /// <b>The Block → Build Characters</b>.
    ///
    /// <b>A roster body is a PREFAB whose root is the visual child.</b> Its transform carries the
    /// height match against Joe and nothing else, so <see cref="CharacterBody"/> can instantiate it
    /// under any host with <c>worldPositionStays: false</c> and get a correctly sized body standing
    /// on the host's own origin. Joe's own scale is 1 by construction - he is the reference.
    ///
    /// <b>The player is restructured here, and it is the only invasive thing this unit does.</b>
    /// Until now <c>Player_Joe</c> carried the Animator, nine skinned meshes and the whole skeleton
    /// on the same transform as its <c>CharacterController</c>. A second body needs a scale, and a
    /// scale there resizes the physics capsule - so the body moves into a <c>Visual</c> child,
    /// which is how the crowd and the stage dancer were already built. Idempotent: run it twice and
    /// the second run finds the child already there.
    ///
    /// The three hosts and why their Animator settings differ:
    ///  - <b>Player</b> - the game's own controller, ordinary culling. He is always on camera.
    ///  - <b>Stage dancer</b> - the Dance controller, <c>AlwaysAnimate</c>: the routine is framed by
    ///    its own camera and a culled Animator never writes the pose at all (memory:
    ///    <c>culled-animator-skips-pose-write</c>).
    ///  - <b>Character screen turntable</b> - <c>AlwaysAnimate</c> AND <c>UnscaledTime</c>, because
    ///    that menu is on screen precisely when <c>Time.timeScale</c> is 0.
    /// </summary>
    public static class CharacterPrefabBuilder
    {
        private const string PrefabFolder = "Assets/Prefabs/Characters";
        private const string PlayerControllerPath = "Assets/Animation/Joe.controller";
        private const string DanceControllerPath = "Assets/Animation/Dance/Dance.controller";
        private const string VisualName = "Visual";

        /// <summary>The web's <c>PREVIEW_HEIGHT_M</c>: what the character screen's body stands at.</summary>
        private const float PreviewHeight = 1.7f;

        /// <summary>
        /// The roster, in <c>characters.config.ts</c>'s own order - which decides which button is
        /// first on the character screen. Joe leads because he is the default.
        /// </summary>
        private readonly struct Spec
        {
            public readonly string Id;
            public readonly string Name;
            public readonly string ModelPath;

            public Spec(string id, string name, string modelPath)
            {
                Id = id;
                Name = name;
                ModelPath = modelPath;
            }
        }

        private static readonly Spec[] Specs =
        {
            new("joe", "Joe", $"{CharacterImporter.CharactersPath}/Joe.fbx"),
            new("jody", "Jody", $"{CharacterImporter.CharactersPath}/Jody.fbx"),
            new("david", "David", $"{CharacterImporter.CharactersPath}/David.fbx"),
        };

        /// <summary>
        /// Joe's FBX ships two WHITE materials - <c>Ch33_body</c> and <c>Ch33_hair</c> carry no map
        /// at all. The textured URP twins were bound by hand onto the scene's player at U2, which is
        /// why he looks right there and a freshly instantiated Joe does not. The other two bodies
        /// need none of this: they are imported with their textures extracted, and Unity's own
        /// material remap binds those (see <see cref="CharacterImporter"/>).
        ///
        /// This table used to live in <c>DanceBuilder</c>, which is where the same fault was found
        /// first. It is here now because a Joe prefab is a Joe prefab wherever it is instantiated.
        /// </summary>
        private static readonly (string Slot, string Material)[] JoeMaterials =
        {
            ("Ch33_body", $"{CharacterImporter.CharactersPath}/Materials/Ch33_1001_Diffuse.mat"),
            ("Ch33_hair", $"{CharacterImporter.CharactersPath}/Materials/Ch33_1002_Diffuse.mat"),
        };

        [MenuItem("The Block/Build Characters", priority = 23)]
        public static void BuildMenu() => Build();

        public static string Build()
        {
            var log = new StringBuilder();

            float reference = PeopleImporter.MeasureHeight(
                $"{CharacterImporter.CharactersPath}/{CharacterImporter.ReferenceFile}");

            if (reference <= 0.01f)
            {
                var missing = $"CharacterPrefabBuilder - {CharacterImporter.ReferenceFile} did not " +
                              "measure. Everyone is scaled to Joe, so nothing was built.";
                Debug.LogError(missing);
                return missing;
            }

            EnsureFolder();

            var entries = new List<CharacterRoster.Entry>();
            foreach (var spec in Specs)
            {
                var prefab = BuildOne(spec, reference, log);
                if (prefab != null)
                    entries.Add(new CharacterRoster.Entry { Id = spec.Id, Name = spec.Name, Prefab = prefab });
            }

            if (entries.Count == 0)
            {
                var nothing = "CharacterPrefabBuilder - no body built, so the scene was left alone.\n" + log;
                Debug.LogError(nothing);
                return nothing;
            }

            // U35a's ragdoll rig lives inside the bodies this method has just rewritten, and it is
            // rebuilt before anything is wired to them - a roster entry pointing at a prefab with no
            // rig is a player who cannot be thrown off a bike. See NpcBuilder for the same call and
            // the U34 lesson behind both.
            log.AppendLine(RagdollBuilder.Build());

            RigRoster(entries, log);
            RigPlayer(entries, log);
            RigStageDancer(entries, log);
            RigTurntable(entries, log);

            // A scene changed by script is not dirty until it is said to be, and Save then writes
            // nothing while reporting success (memory: editor-created-objects-need-markscenedirty).
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();

            var report = $"CharacterPrefabBuilder - {entries.Count}/{Specs.Length} body/bodies\n{log}";
            Debug.Log(report);
            return report;
        }

        // ── prefabs ───────────────────────────────────────────────────────────────────────────

        private static GameObject BuildOne(Spec spec, float reference, StringBuilder log)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath);
            if (model == null)
            {
                log.AppendLine(
                    $"{spec.Name,-7} MISSING {spec.ModelPath} - run The Block → Import Characters " +
                    "first, and copy the FBX in before that");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = VisualName;

            float height = PeopleImporter.MeasureHeight(spec.ModelPath);
            float scale = CharacterImporter.HeightScale(height, reference);
            instance.transform.localScale = Vector3.one * scale;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            int rebound = spec.Id == "joe" ? BindJoeMaterials(instance.transform) : 0;
            rebound += DressUntexturedSlots(spec, instance.transform, log);
            int blank = CountUntexturedSlots(instance.transform);

            if (!instance.TryGetComponent<Animator>(out var animator))
                animator = instance.AddComponent<Animator>();

            var avatar = AssetDatabase.LoadAllAssetsAtPath(spec.ModelPath).OfType<Avatar>().FirstOrDefault();
            if (avatar != null) animator.avatar = avatar;

            // Left unset on the prefab on purpose: the host decides. The same body dances under the
            // Dance controller and walks under Joe's, which is the whole reason one prefab serves
            // three hosts.
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;

            var path = $"{PrefabFolder}/{spec.Name}.prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance);

            log.AppendLine(
                $"{spec.Name,-7} {height:0.000} m × {scale:0.###} → {reference:0.000} m | " +
                $"avatar {(avatar == null ? "MISSING" : avatar.name)}" +
                (rebound > 0 ? $" | {rebound} material(s) rebound" : "") +
                (blank > 0 ? $" | ⚠ {blank} slot(s) with no base map - this body will render white" : "") +
                $" → {path}");

            return saved;
        }

        private static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(PrefabFolder)) return;
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
            AssetDatabase.CreateFolder("Assets/Prefabs", "Characters");
        }

        /// <summary>Swaps Joe's white FBX materials for the textured ones. See <see cref="JoeMaterials"/>.</summary>
        private static int BindJoeMaterials(Transform root)
        {
            int bound = 0;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;

                    foreach (var (slot, path) in JoeMaterials)
                    {
                        if (materials[i].name != slot) continue;

                        var replacement = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (replacement == null) continue;

                        materials[i] = replacement;
                        changed = true;
                        bound++;
                    }
                }

                if (changed) renderer.sharedMaterials = materials;
            }

            return bound;
        }

        /// <summary>
        /// Writes a URP/Lit material for every slot the FBX left untextured, and binds it.
        ///
        /// <b>This is the U16b claim failing on a new file, and the failure is worth naming.</b> The
        /// crowd's importer says Mixamo FBX "come out of Unity's own importer as URP/Lit with base +
        /// normal already bound", and Joe on this machine agrees - but Jody and David came out with
        /// seven and six slots holding a white URP/Lit material and no <c>_BaseMap</c> at all, with
        /// their textures sitting extracted right beside them. The remap is an importer STATE, not a
        /// guarantee, and it lives in a <c>.meta</c> - which for Joe is gitignored, so his working
        /// remap is a local patch nobody else has (memory: <c>gitignored-meta-hides-importer-fixes</c>).
        /// Writing the materials as assets is code, and code is what survives a clone.
        ///
        /// <b>Which texture belongs to which slot is Mixamo's own numbering, not the names.</b>
        /// Jody's body material is called <c>Ch38_body</c> while every one of her textures is
        /// <c>Ch37_*</c> - matching by prefix finds nothing. The pairing that does hold across all
        /// three characters is the set number: <c>_body</c> takes 1001 and <c>_hair</c> takes 1002,
        /// which is exactly the table Joe's two hand-made materials encode. David has no 1002 at
        /// all, so his hair falls back to 1001 rather than staying white.
        /// </summary>
        private static int DressUntexturedSlots(Spec spec, Transform root, StringBuilder log)
        {
            var sets = TextureSets(spec.Name);
            if (sets.Count == 0) return 0;

            var cache = new Dictionary<string, Material>();
            int bound = 0;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (source != null && source.HasProperty("_BaseMap") &&
                        source.GetTexture("_BaseMap") != null) continue;

                    var wanted = source != null && source.name.EndsWith("_hair") ? "1002" : "1001";
                    var key = sets.ContainsKey(wanted) ? wanted : sets.Keys.OrderBy(k => k).First();

                    if (!cache.TryGetValue(key, out var material))
                    {
                        material = WriteMaterial(spec.Name, key, sets[key]);
                        cache[key] = material;
                    }

                    if (material == null) continue;

                    materials[i] = material;
                    changed = true;
                    bound++;
                }

                if (changed) renderer.sharedMaterials = materials;
            }

            if (bound > 0)
                log.AppendLine(
                    $"{spec.Name,-7} wrote {cache.Count} material(s) from {sets.Count} texture set(s) " +
                    $"- the FBX's own were white");

            return bound;
        }

        /// <summary>The extracted textures, grouped by Mixamo's set number (1001, 1002, …).</summary>
        private static Dictionary<string, (string Base, string Normal)> TextureSets(string name)
        {
            var sets = new Dictionary<string, (string Base, string Normal)>();
            var folder = CharacterImporter.TextureFolder(name);
            if (!AssetDatabase.IsValidFolder(folder)) return sets;

            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var stem = System.IO.Path.GetFileNameWithoutExtension(path);

                var key = stem.Split('_').FirstOrDefault(part => part.Length == 4 && part.All(char.IsDigit));
                if (key == null) continue;

                sets.TryGetValue(key, out var entry);

                if (PeopleImporter.IsBaseMap(path)) entry.Base = path;
                else if (PeopleImporter.IsNormalMap(path)) entry.Normal = path;

                sets[key] = entry;
            }

            // A set with no diffuse is a specular or gloss map on its own - nothing to build from.
            foreach (var empty in sets.Where(s => s.Value.Base == null).Select(s => s.Key).ToArray())
                sets.Remove(empty);

            return sets;
        }

        /// <summary>
        /// Joe's own two materials, written by script: URP/Lit, opaque, base + normal, everything
        /// else at its default. Read off <c>Ch33_1001_Diffuse.mat</c> rather than invented, so the
        /// roster is lit the same way the character the game shipped with is.
        /// </summary>
        private static Material WriteMaterial(string name, string set, (string Base, string Normal) textures)
        {
            var folder = $"{CharacterImporter.CharactersPath}/Materials";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(CharacterImporter.CharactersPath, "Materials");

            var path = $"{folder}/{name}_{set}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool fresh = material == null;

            if (fresh)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    Debug.LogError("CharacterPrefabBuilder: URP/Lit shader not found.");
                    return null;
                }

                material = new Material(shader);
            }

            var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(textures.Base);
            if (baseMap != null) material.SetTexture("_BaseMap", baseMap);

            if (textures.Normal != null)
            {
                var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(textures.Normal);
                if (normal != null)
                {
                    material.SetTexture("_BumpMap", normal);

                    // The keyword is the whole difference between a bound normal map and an ignored
                    // one - assigning the texture alone changes nothing that is drawn.
                    material.EnableKeyword("_NORMALMAP");
                }
            }

            if (fresh) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);

            return material;
        }

        /// <summary>
        /// How many material slots would draw untextured. A Mixamo body whose textures were never
        /// extracted renders white and it looks like a lighting bug, so it is counted and named in
        /// the log rather than left to be discovered in Play.
        /// </summary>
        private static int CountUntexturedSlots(Transform root)
        {
            int blank = 0;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null) { blank++; continue; }
                    if (!material.HasProperty("_BaseMap")) continue;
                    if (material.GetTexture("_BaseMap") == null) blank++;
                }

            return blank;
        }

        // ── entry points for the other builders ───────────────────────────────────────────────
        //
        // The Block → Build Menus and → Build Campaign each rebuild a host that wears a body, and
        // either can be run after this one. Rather than make the menu order a thing to remember,
        // they call back in here and dress what they just built. Both are no-ops with a clear line
        // in their own log when the prefabs do not exist yet.

        /// <summary>Dresses the character screen's turntable. Called by <c>MenuBuilder</c>.</summary>
        public static string DressTurntable()
        {
            var log = new StringBuilder();
            if (TryLoadEntries(out var entries)) RigTurntable(entries, log);
            else log.AppendLine("preview  no character prefabs yet - run The Block → Build Characters");
            return log.ToString().TrimEnd();
        }

        /// <summary>Dresses the dance stage's body. Called by <c>DanceBuilder</c>.</summary>
        public static string DressStageDancer()
        {
            var log = new StringBuilder();
            if (TryLoadEntries(out var entries)) RigStageDancer(entries, log);
            else log.AppendLine("dancer   no character prefabs yet - run The Block → Build Characters");
            return log.ToString().TrimEnd();
        }

        /// <summary>The prefabs this builder last wrote, in roster order. False when none exist.</summary>
        private static bool TryLoadEntries(out List<CharacterRoster.Entry> entries)
        {
            entries = new List<CharacterRoster.Entry>();

            foreach (var spec in Specs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{spec.Name}.prefab");
                if (prefab != null)
                    entries.Add(new CharacterRoster.Entry { Id = spec.Id, Name = spec.Name, Prefab = prefab });
            }

            return entries.Count > 0;
        }

        // ── the scene ─────────────────────────────────────────────────────────────────────────

        private static void RigRoster(List<CharacterRoster.Entry> entries, StringBuilder log)
        {
            var roster = Object.FindAnyObjectByType<CharacterRoster>(FindObjectsInactive.Include);
            if (roster == null)
            {
                var host = new GameObject("Characters");
                SceneManagerMove(host);
                roster = host.AddComponent<CharacterRoster>();
            }

            roster.Configure(entries);
            EditorUtility.SetDirty(roster);

            log.AppendLine($"roster   {entries.Count} entr(ies) on '{roster.gameObject.name}': " +
                           string.Join(", ", entries.Select(e => e.Id)));
        }

        /// <summary>
        /// Moves the player's body off the root and onto a <c>Visual</c> child, then gives him a
        /// <see cref="CharacterBody"/>. Everything except the four components that ARE the player
        /// goes down a level: the Animator, the skinned meshes and the skeleton root.
        /// </summary>
        private static void RigPlayer(List<CharacterRoster.Entry> entries, StringBuilder log)
        {
            var player = Object.FindAnyObjectByType<TheBlock.Player.PlayerController>(FindObjectsInactive.Include);
            if (player == null)
            {
                log.AppendLine("player   no PlayerController in the scene - skipped");
                return;
            }

            var host = player.gameObject;
            var joe = entries.FirstOrDefault(e => e.Id == CharacterRoster.DefaultId);
            if (joe.Prefab == null) joe = entries[0];

            // The old body, whatever shape it is in: the pre-U29 arrangement (nine skinned meshes
            // and a `mixamorig7:` skeleton root sitting directly on the player) or a Visual child
            // from an earlier run of this. Both are the body and both go.
            //
            // Anything else under the player is left alone and named in the log. Nothing hangs off
            // him today, but "delete every child" is the kind of line that silently eats a marker a
            // later unit parents there.
            int removed = StripOldBody(host.transform, out var kept);

            // The Animator moves onto the body with the skeleton. Left here it would be a second,
            // avatar-less Animator on the same object PlayerAnimator resolves through.
            if (host.TryGetComponent<Animator>(out var rootAnimator)) Object.DestroyImmediate(rootAnimator);

            var body = Ensure<CharacterBody>(host);
            body.Configure(
                joe.Id,
                AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerControllerPath),
                AnimatorCullingMode.CullUpdateTransforms,
                AnimatorUpdateMode.Normal);

            body.Wear(joe);
            EditorUtility.SetDirty(host);

            log.AppendLine(
                $"player   {host.name} → CharacterBody wearing '{joe.Id}' under {VisualName} " +
                $"({removed} old body object(s) removed" +
                (kept.Count == 0 ? "" : $", kept: {string.Join(", ", kept)}") + ")");
        }

        private static void RigStageDancer(List<CharacterRoster.Entry> entries, StringBuilder log)
        {
            var dancer = Object.FindAnyObjectByType<TheBlock.Minigame.Rhythm.Dancer>(FindObjectsInactive.Include);
            if (dancer == null)
            {
                log.AppendLine("dancer   no Dancer in the scene - run The Block → Build Campaign first");
                return;
            }

            var joe = entries.FirstOrDefault(e => e.Id == CharacterRoster.DefaultId);
            if (joe.Prefab == null) joe = entries[0];

            StripOldBody(dancer.transform, out _);

            var body = Ensure<CharacterBody>(dancer.gameObject);
            body.Configure(
                joe.Id,
                AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(DanceControllerPath),
                AnimatorCullingMode.AlwaysAnimate,
                AnimatorUpdateMode.Normal);

            body.Wear(joe);
            EditorUtility.SetDirty(dancer.gameObject);

            log.AppendLine($"dancer   stage body → CharacterBody wearing '{joe.Id}'");
        }

        private static void RigTurntable(List<CharacterRoster.Entry> entries, StringBuilder log)
        {
            var preview = Object.FindAnyObjectByType<TheBlock.UI.Menus.CharacterPreview>(FindObjectsInactive.Include);
            if (preview == null)
            {
                log.AppendLine("preview  no CharacterPreview - run The Block → Build Menus first");
                return;
            }

            var turntable = preview.transform.Find("Turntable");
            if (turntable == null)
            {
                log.AppendLine("preview  the rig has no Turntable child - skipped");
                return;
            }

            var joe = entries.FirstOrDefault(e => e.Id == CharacterRoster.DefaultId);
            if (joe.Prefab == null) joe = entries[0];

            // U26 baked a body under its own name here, so the strip has two shapes to catch.
            StripOldBody(turntable, out _);

            var body = Ensure<CharacterBody>(turntable.gameObject);
            body.Configure(
                joe.Id,
                AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerControllerPath),
                AnimatorCullingMode.AlwaysAnimate,
                AnimatorUpdateMode.UnscaledTime);

            body.Wear(joe);
            NormalisePreview(turntable);
            EditorUtility.SetDirty(turntable.gameObject);

            log.AppendLine(
                $"preview  turntable → CharacterBody wearing '{joe.Id}', " +
                $"normalised to {PreviewHeight} m at scale {turntable.localScale.x:0.###}");
        }

        /// <summary>
        /// Removes whatever body a host is already carrying, in any of the three shapes this port
        /// has produced: a <c>Visual</c> child, the pre-U29 player's loose skinned meshes and
        /// <c>mixamorig7:</c> skeleton root, and U26's turntable body baked under its own name.
        ///
        /// Anything that is not a body is left alone and named by the caller's log. Nothing hangs
        /// off these three hosts today, but "delete every child" is the kind of line that silently
        /// eats a marker a later unit parents there.
        /// </summary>
        private static int StripOldBody(Transform host, out List<string> kept)
        {
            kept = new List<string>();
            int removed = 0;

            foreach (var child in host.Cast<Transform>().ToArray())
            {
                bool isBody = child.name == VisualName ||
                              child.GetComponentInChildren<SkinnedMeshRenderer>(true) != null ||
                              child.name.Contains(":");

                if (!isBody) { kept.Add(child.name); continue; }

                Object.DestroyImmediate(child.gameObject);
                removed++;
            }

            return removed;
        }

        /// <summary>
        /// Stands the turntable's body 1.7 m tall on the rig's own floor - the web's
        /// <c>PREVIEW_HEIGHT_M</c>, and the framing the U26 camera was approved against.
        ///
        /// <b>On the TURNTABLE, not on the body.</b> U26 scaled the body it baked; a roster body's
        /// own transform now carries the height match against Joe, so scaling it here would fight
        /// that. And because every roster body is already the same height, one number on the
        /// turntable frames all three - which is what U26's comment wanted and could not have.
        /// </summary>
        private static void NormalisePreview(Transform turntable)
        {
            turntable.localScale = Vector3.one;
            turntable.localPosition = Vector3.zero;

            var renderers = turntable.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            if (bounds.size.y <= 0.0001f) return;

            turntable.localScale = Vector3.one * (PreviewHeight / bounds.size.y);

            // Re-read after scaling: the bounds moved with it.
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            var floorY = turntable.parent != null ? turntable.parent.position.y : 0f;
            turntable.localPosition = new Vector3(
                turntable.localPosition.x - (bounds.center.x - turntable.position.x),
                turntable.localPosition.y + (floorY - bounds.min.y),
                turntable.localPosition.z);
        }

        /// <summary>Never <c>GetComponent() ?? AddComponent()</c> - memory: unity-null-coalescing-fake-null.</summary>
        private static T Ensure<T>(GameObject host) where T : Component =>
            host.TryGetComponent<T>(out var existing) ? existing : host.AddComponent<T>();

        private static void SceneManagerMove(GameObject go) =>
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(
                go, EditorSceneManager.GetActiveScene());
    }
}
