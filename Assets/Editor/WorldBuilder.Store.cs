using System.Collections.Generic;
using TheBlock.Core;
using TheBlock.Npc;
using TheBlock.Powerup;
using TheBlock.World;
using UnityEditor;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// U28's build pass: wires the 7-Eleven's own nodes into <see cref="SevenEleven"/>, stands a
    /// shopkeeper behind the till, and makes sure the economy has a component to live on.
    ///
    /// <b>The nodes are the source, and the config is the check.</b> <c>seven-eleven-lot.glb</c> ships
    /// an empty for every point this needs - <c>se_door_trigger</c>, <c>se_cashier_stand</c>,
    /// <c>se_register</c>, ten <c>pu_slot_*</c> - each holding the same number
    /// <c>config.sevenEleven</c> holds, and glTFast converted the whole hierarchy on import. So the
    /// component binds transforms and this pass REPORTS the node-versus-config delta instead of
    /// converting anything. That matters more here than anywhere else in the project: every one of
    /// these coordinates is MODEL-LOCAL, which is the handedness rule people get wrong, and a silent
    /// mirror would put the counter on the wrong side of a symmetrical room where it looks fine.
    ///
    /// Measured once, so it does not have to be re-derived: glTFast negates X on a node's local
    /// position and passes Y and Z through. Carried through the place's own +90° yaw and its
    /// <c>Convert.Pos</c> origin, <c>se_cashier_stand</c> lands at <c>(−33.70, 0.23, −20.62)</c>,
    /// which is <c>Convert.Pos</c> of where three.js puts the same point. The hierarchy agrees with
    /// the config; nothing has to be flipped by hand.
    /// </summary>
    public static partial class WorldBuilder
    {
        private const string PedPrefabFolder = "Assets/Prefabs/Npc";

        /// <summary>The face behind the till. One of the six the crowd already ships.</summary>
        private const string ClerkFace = "Sophie";

        /// <summary>
        /// Re-wires just the shop, against the world already in the scene - <b>The Block → Build
        /// Store</b>.
        ///
        /// A full <b>Build World</b> does this too, and would also re-instantiate nine districts, the
        /// roads, the traffic graph and the NavMesh bake to change two components. The store is the
        /// only thing in this unit that lives in the scene, so it gets a door of its own.
        /// </summary>
        [MenuItem("The Block/Build Store", priority = 7)]
        public static void BuildStoreOnly()
        {
            var snapshot = TheBlockConfig.Load(reload: true);
            if (snapshot?.Config?.SevenEleven == null)
            {
                Debug.LogError("Build Store: the snapshot has no `sevenEleven` section.");
                return;
            }

            var instance = GameObject.Find("Place_SevenEleven");
            if (instance == null)
            {
                Debug.LogError("Build Store: no Place_SevenEleven in the scene. Run The Block → Build World.");
                return;
            }

            var root = Object.FindAnyObjectByType<WorldRoot>();
            if (root == null)
            {
                Debug.LogError("Build Store: no WorldRoot in the scene. Run The Block → Build World.");
                return;
            }

            var report = new Report();
            BuildStore(instance, snapshot.Config.SevenEleven, report);
            EnsureEconomy(root.transform, report, snapshot.PowerUps);

            // `new GameObject` and a component add do not dirty the scene by themselves, and an
            // unmarked scene is not written by Save - the shop would live in memory until the next
            // domain reload threw it away.
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            var text = new System.Text.StringBuilder("Build Store\n");
            foreach (var line in report.Placed) text.AppendLine("  " + line);
            foreach (var line in report.Notes) text.AppendLine("  note: " + line);
            foreach (var line in report.Warnings) text.AppendLine("  WARNING: " + line);

            if (report.Warnings.Count > 0) Debug.LogWarning(text.ToString());
            else Debug.Log(text.ToString() + "  Save the scene.");
        }

        private static void BuildStore(GameObject instance, TheBlockConfig.StoreSpec spec, Report report)
        {
            if (instance == null || spec == null) return;

            var nodes = NodeIndex(instance);

            var left = Node(nodes, spec.Door?.LeftNode, report);
            var right = Node(nodes, spec.Door?.RightNode, report);
            var entrance = Node(nodes, "se_door_trigger", report);
            var stand = Node(nodes, "se_cashier_stand", report);
            var register = Node(nodes, "se_register", report);

            if (left == null || right == null || entrance == null || stand == null)
            {
                report.Warnings.Add(
                    "7-Eleven - one of its marker nodes is missing, so the shop is not wired. " +
                    "Expected SevenEleven_DoorL/R, se_door_trigger, se_cashier_stand.");
                return;
            }

            // TryGetComponent, never `GetComponent() ?? AddComponent()` - a missing component comes
            // back as Unity's fake-null and `??` treats it as a real object.
            if (!instance.TryGetComponent<SevenEleven>(out var store))
                store = instance.AddComponent<SevenEleven>();

            var (minX, maxX, minZ, maxZ) = FloorRect(spec.Floor);
            store.Configure(left, right, entrance, stand, spec, minX, maxX, minZ, maxZ);
            EditorUtility.SetDirty(store);

            BuildClerk(instance.transform, stand, register, spec, report);

            report.Placed.Add(
                $"7-Eleven - doors {left.name}/{right.name} part {Mathf.Abs(spec.Door?.Slide.X ?? 0f):0.00} m, " +
                $"floor x[{minX:0.00},{maxX:0.00}] z[{minZ:0.00},{maxZ:0.00}] model-local, " +
                $"counter r{spec.Clerk?.TalkRadius ?? 0f:0.0} m");

            CheckAgainstConfig(nodes, spec, report);
        }

        /// <summary>
        /// The sales floor rectangle - the one part of the store with no node to read, and therefore
        /// the one part that must be converted by hand.
        ///
        /// glTFast negates X, so a min becomes a max: the rectangle is not just mirrored, its corners
        /// SWAP. Z passes through untouched. This is deliberately not <c>Convert.ModelOffset</c> -
        /// that rule (pass X, negate Z) governs offsets in a model whose facing has been corrected by
        /// <c>Convert.ModelFacing</c>, which a placed prop never gets. Two different conversions for
        /// two different frames; reaching for the more famous one is the whole trap.
        /// </summary>
        private static (float MinX, float MaxX, float MinZ, float MaxZ) FloorRect(
            TheBlockConfig.StoreFloorSpec floor)
        {
            if (floor == null) return (-6.78f, 6.78f, -5.3f, 5.28f);

            var x0 = -floor.MaxX;
            var x1 = -floor.MinX;
            var z0 = floor.MinZ;
            var z1 = floor.MaxZ;

            return (Mathf.Min(x0, x1), Mathf.Max(x0, x1), Mathf.Min(z0, z1), Mathf.Max(z0, z1));
        }

        /// <summary>
        /// Stands a shopkeeper behind the till.
        ///
        /// <b>A crowd prefab that is never bound to a seed</b>, which is U21's delivery-customer
        /// pattern and works for the same three reasons: <c>Pedestrian.Tick</c> is never called so the
        /// blend tree sits at Speed 0 and she idles for free; there is no second character import; and
        /// <see cref="RunOverSystem"/> only ever reads <c>CrowdSpawner.Crowd</c>, so she is invisible
        /// to it and cannot be killed. A shop you can permanently close by driving into it is not a
        /// shop.
        ///
        /// Her FACING is measured, not configured: <c>se_register</c> is 1.57 m in front of
        /// <c>se_cashier_stand</c>, so looking from one to the other is the counter's own answer. The
        /// config's <c>yaw + clerk.yaw</c> is computed anyway and reported as a delta, because two
        /// derivations that agree are a check and one that does not is worth knowing about.
        /// </summary>
        private static void BuildClerk(
            Transform storeRoot, Transform stand, Transform register,
            TheBlockConfig.StoreSpec spec, Report report)
        {
            const string ClerkName = "Store_Clerk";

            // A prefab change must not leave last run's copy standing beside this one's.
            var existing = storeRoot.Find(ClerkName);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var path = $"{PedPrefabFolder}/Ped_{ClerkFace}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                report.Warnings.Add(
                    $"7-Eleven - no {path}, so the counter is unstaffed. " +
                    "Run The Block → Build Pedestrians, then rebuild.");
                return;
            }

            var clerk = (GameObject)PrefabUtility.InstantiatePrefab(prefab, storeRoot);
            clerk.name = ClerkName;
            clerk.transform.position = stand.position;

            var facing = register != null
                ? Flat(register.position - stand.position)
                : Vector3.zero;

            var fromConfig = Convert.RotFromRadians(spec.Yaw + (spec.Clerk?.Yaw ?? 0f));
            if (facing.sqrMagnitude > 0.0001f)
            {
                clerk.transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
                var delta = Quaternion.Angle(clerk.transform.rotation, fromConfig);
                report.Notes.Add(
                    $"7-Eleven clerk - faces se_register; config's yaw sum disagrees by {delta:0.#}°");
            }
            else
            {
                clerk.transform.rotation = fromConfig;
                report.Notes.Add("7-Eleven clerk - no se_register node, so the facing came from the config");
            }

            if (spec.Clerk != null && spec.Clerk.Scale > 0f && !Mathf.Approximately(spec.Clerk.Scale, 1f))
                clerk.transform.localScale = Vector3.one * spec.Clerk.Scale;

            // Never bound to a seed - see the method note. Disabled rather than destroyed so the
            // prefab link survives and a future unit can hand her a line to say.
            if (clerk.TryGetComponent<Pedestrian>(out var pedestrian)) pedestrian.enabled = false;

            report.Placed.Add($"7-Eleven clerk - {ClerkFace} @ {Fmt(clerk.transform.position)}");
        }

        /// <summary>
        /// Asserts the model and the config still describe the same building.
        ///
        /// Every point below exists twice, so drift is detectable - and it is the ONLY thing that
        /// would catch a re-export of the store with a shelf moved, which would otherwise show up as
        /// a power-up floating a metre off a counter that nobody looks at.
        /// </summary>
        private static void CheckAgainstConfig(
            Dictionary<string, Transform> nodes, TheBlockConfig.StoreSpec spec, Report report)
        {
            var worst = 0f;
            var worstName = string.Empty;
            var checks = 0;

            void Check(string node, TheBlockConfig.Vec3 fromConfig)
            {
                if (!nodes.TryGetValue(node, out var t) || t == null) return;
                checks++;

                // The node's imported local position against the config's own, with glTFast's X
                // negation applied to the config side. Y is ignored: the glb parks its empties a
                // centimetre off the floor and the config rounds that to zero.
                var expected = new Vector3(-fromConfig.X, t.localPosition.y, fromConfig.Z);
                var d = Vector3.Distance(t.localPosition, expected);
                if (d <= worst) return;
                worst = d;
                worstName = node;
            }

            Check("se_door_trigger", spec.Entrance);
            Check("se_cashier_stand", spec.Cashier);
            foreach (var slot in spec.Slots ?? new Dictionary<string, TheBlockConfig.Vec3>())
                Check($"pu_slot_{slot.Key}", slot.Value);

            if (checks == 0)
            {
                report.Warnings.Add("7-Eleven - no marker node matched the config; nothing was cross-checked");
                return;
            }

            var line = $"7-Eleven - {checks} marker node(s) checked against the config, worst delta " +
                       $"{worst * 100f:0.0} cm ({worstName})";
            if (worst > 0.01f) report.Warnings.Add(line + " - over 1 cm, the model and the config have drifted");
            else report.Notes.Add(line);
        }

        /// <summary>
        /// The economy's own component. Placed in a <c>Game</c> group rather than beside the store,
        /// because power-ups outlive the building you bought them in - 1-4 have to work on foot, at
        /// the wheel and mid-mission.
        ///
        /// <see cref="TheBlock.Game.Wallet"/> is deliberately LEFT where U19 put it, on the Police
        /// group. Moving a component that owns persisted state, in a unit that already touches ten
        /// files, buys tidiness and risks a save. Every consumer resolves it with
        /// <c>FindAnyObjectByType</c>, so where it sits has never mattered.
        /// </summary>
        private static void EnsureEconomy(Transform root, Report report, TheBlockConfig.PowerUpCatalogSpec catalogue)
        {
            NormaliseWallet(report, catalogue);

            var existing = Object.FindAnyObjectByType<PowerUps>();
            if (existing != null)
            {
                report.Notes.Add($"economy - PowerUps already on '{existing.gameObject.name}'");
                return;
            }

            var group = NewGroup("Game", root);
            group.gameObject.AddComponent<PowerUps>();
            report.Placed.Add("economy - PowerUps on Game");
        }

        /// <summary>
        /// Writes the opening balance from the catalogue: the price of the FIRST power-up, so a new
        /// player can buy exactly one thing and nothing else.
        ///
        /// <b>Derived rather than typed, because it is not really a number - it is a relationship.</b>
        /// "You start with enough for one energy drink" survives a price change in
        /// <c>powerup.config.ts</c>; a hardcoded 40 quietly stops being true and nothing says so.
        /// Build time rather than runtime, so <see cref="TheBlock.Game.Wallet"/> keeps knowing
        /// nothing about power-ups.
        ///
        /// It also has to be written HERE at all, and not just as a C# default, because a field the
        /// scene has already serialized beats its initializer - U19's debug 500 sat in this scene
        /// through two units for exactly that reason.
        ///
        /// It does not touch the SAVE. <c>PlayerPrefs</c> still holds whatever the last run banked;
        /// this is only what a profile that has never played opens on, which is also what New Game
        /// resets to.
        /// </summary>
        private static void NormaliseWallet(Report report, TheBlockConfig.PowerUpCatalogSpec catalogue)
        {
            var wallet = Object.FindAnyObjectByType<TheBlock.Game.Wallet>();
            if (wallet == null) return;

            var first = catalogue?.Items != null && catalogue.Items.Count > 0 ? catalogue.Items[0] : null;
            var want = first?.Price ?? TheBlock.Game.Wallet.DefaultStartingBalance;
            var from = first != null
                ? $"powerUpConfig.items[0] ({first.Emoji} {first.Label})"
                : "Wallet.DefaultStartingBalance - the catalogue is missing";

            var serialized = new SerializedObject(wallet);
            var starting = serialized.FindProperty("startingBalance");
            if (starting == null) return;

            var was = starting.intValue;
            if (was == want)
            {
                report.Notes.Add($"economy - Wallet.startingBalance already ${want}, from {from}");
                return;
            }

            starting.intValue = want;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(wallet);
            report.Notes.Add($"economy - Wallet.startingBalance ${was} → ${want}, from {from}");
        }

        // --- node lookup ------------------------------------------------------------------------

        /// <summary>
        /// Every transform under the instance, by name. Built once: the store has 95 nodes and this
        /// pass asks for a dozen of them.
        ///
        /// Duplicate names keep the FIRST hit, which is what <c>getObjectByName</c> does in three.js
        /// and therefore what the web build's numbers were authored against.
        /// </summary>
        private static Dictionary<string, Transform> NodeIndex(GameObject instance)
        {
            var index = new Dictionary<string, Transform>(System.StringComparer.Ordinal);
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                if (!index.ContainsKey(t.name)) index[t.name] = t;
            return index;
        }

        private static Transform Node(Dictionary<string, Transform> nodes, string name, Report report)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (nodes.TryGetValue(name, out var t)) return t;
            report.Warnings.Add($"7-Eleven - no node named '{name}' in the imported model");
            return null;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
