using TheBlock.Core;
using UnityEngine;
using Convert = TheBlock.Core.Convert;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Builds the pizzeria's interior cell (U13) - the room, its lamps, and the
    /// <see cref="World.Interior"/> component that owns the doorway.
    ///
    /// U13 built the place and left its MISSION mechanics open, by the user's call. U21 settles them
    /// and they are here, because they are geometry: the cashier behind the counter, and the
    /// <see cref="World.Interior.NearCounter"/> circle that is the shift's start button.
    ///
    /// <b>The pizza-box stack is deliberately NOT built.</b> It is set dressing with no mechanic -
    /// the pizzas you carry are a HUD count, and no version of this game ever picks a box up. Its
    /// raw asset is 23 MB for a 30 cm prop (a 14.7 MB normal map alone), and the shipped 417 KB copy
    /// needs Draco, which this project has no importer for. Wiring it properly means extending U15's
    /// texture pass to props, and it buys three boxes on a counter you walk past. Named here so it
    /// is a decision rather than an oversight.
    /// </summary>
    public static partial class WorldBuilder
    {
        /// <summary>
        /// Metres of three.js point-light "intensity" per Unity candela.
        ///
        /// A pure re-derivation, like every other number that crosses a renderer boundary (port rule
        /// 2): three's intensity means something different in every version and none of those
        /// meanings is URP's. The config's 40 and 30 become 4 and 3, which is a warm ceiling lamp in
        /// a 3.6 m room - the shape of the lighting (one bright lamp over the counter, two dimmer
        /// ones flanking it) is what carries, not the scalar.
        /// </summary>
        private const float InteriorLightScale = 0.1f;

        private static void BuildInterior(
            Transform parent, TheBlockConfig.InteriorSpec cfg, TheBlockConfig.PlayerSpec player,
            Options options, Report report)
        {
            if (cfg == null) return;

            var offset = Convert.Pos(cfg.Offset.Raw);
            var instance = Instantiate(cfg.AssetUrl, "Pizza Interior", parent, report, out var substitute);
            if (instance == null) return;

            instance.name = "Place_PizzaInterior";
            instance.transform.position = offset + Vector3.up * (substitute?.ExtraY ?? 0f);
            instance.transform.rotation = Quaternion.Euler(substitute?.ExtraEuler ?? Vector3.zero);

            HideCollisionProxies(instance, report);
            ApplyCutoutMaterials(instance, report);
            report.NoteTransparentMaterials(instance);

            if (options.Colliders) AddColliders(instance, null, null, null, report);

            BuildInteriorLights(instance.transform, cfg, offset, report);

            // The cashier hangs off the PLACES group, not off the room. See BuildCounterNpc.
            BuildCounterNpc(parent, cfg, offset, report);
            BindInteriorComponent(instance, cfg, player, offset, report);

            report.Placed.Add($"{instance.name} @ {Fmt(instance.transform.position)}");
        }

        /// <summary>
        /// The cashier behind the counter - the person you press T on to start a shift.
        ///
        /// <b>She is one of the crowd's six, not a seventh import.</b> The web build loads a
        /// dedicated <c>idle-woman.glb</c> for her; that FBX is 52 MB, this project's LFS store is
        /// already at GitHub's 1 GiB free ceiling, and Elizabeth is a woman in the roster who is
        /// already imported, already URP-bound and already height-normalised. Swapping the prefab
        /// below is a one-line change if the exact model is ever wanted.
        ///
        /// She stands, and that is free: a <c>Pedestrian</c> that is never bound to a seed never
        /// ticks, so its blend tree sits at Speed 0. Disabled outright here so nothing can wake her.
        ///
        /// <b><paramref name="parent"/> is the Places group and NOT the room</b>, which is the whole
        /// reason it is a parameter. <c>pizza-interior.glb</c>'s root node carries a scale of
        /// <c>(5, 0.025, 4)</c> - the room is a scaled box - and a character parented under it
        /// inherits that: measured, she rendered 3.5 m wide and <b>2 cm tall</b>, a smear on the
        /// floor that reads as "there is no cashier". A skinned body has to hang off something with
        /// an honest scale. She is placed in world space either way, so nothing else changes.
        /// </summary>
        private static void BuildCounterNpc(
            Transform parent, TheBlockConfig.InteriorSpec cfg, Vector3 offset, Report report)
        {
            var npc = cfg?.Npc;
            if (npc == null)
            {
                report.Warnings.Add("interior cashier skipped - config has no `interior.npc`");
                return;
            }

            const string prefabPath = "Assets/Prefabs/Npc/Ped_Elizabeth.prefab";
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                report.Warnings.Add(
                    $"interior cashier skipped - {prefabPath} is missing. Run Build Pedestrians.");
                return;
            }

            var at = offset + Convert.Pos(new Vector3(npc.X, 0f, npc.Z));
            var instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = "Interior_Cashier";
            instance.transform.SetPositionAndRotation(at, Convert.RotFromRadians(npc.Yaw));

            if (instance.TryGetComponent<TheBlock.Npc.Pedestrian>(out var pedestrian))
                pedestrian.enabled = false;

            // No capsule either. She is behind a counter the player cannot reach around, and a
            // collider there only ever means getting stuck on her.
            if (instance.TryGetComponent<CapsuleCollider>(out var capsule)) capsule.enabled = false;

            report.Notes.Add(
                $"Place_PizzaInterior: cashier (Elizabeth) at {Fmt(at)} " +
                $"yaw {Convert.Yaw(npc.Yaw) * Mathf.Rad2Deg:0.#}°, talk r{npc.TalkRadius:0.#}");
        }

        /// <summary>
        /// The room's warm lamps.
        ///
        /// They stay ON, unlike the web build's, which switch off the moment the player steps out -
        /// three's forward renderer charges every light against every shaded fragment in the scene,
        /// so a lamp in a room a kilometre away still costs the whole city. URP culls per object, so
        /// three point lights nobody can see cost nothing and the switching is dead code here.
        /// </summary>
        private static void BuildInteriorLights(
            Transform parent, TheBlockConfig.InteriorSpec cfg, Vector3 offset, Report report)
        {
            if (cfg.Lights == null || cfg.Lights.Count == 0) return;

            var group = NewGroup("Lights", parent);
            for (int i = 0; i < cfg.Lights.Count; i++)
            {
                var spec = cfg.Lights[i];
                var go = new GameObject($"Lamp_{i}");
                go.transform.SetParent(group, false);
                // A light position inside the room is a world position in the same right-handed
                // frame as the offset - it is stated relative to the room, but it is not a
                // model-local offset, so it takes the world conversion and not ModelOffset.
                go.transform.position = offset + Convert.Pos(spec.Raw);

                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = TheBlockConfig.ColorFromHex(spec.Color);
                light.intensity = spec.Intensity * InteriorLightScale;
                light.range = spec.Distance;
                // No shadows AS BUILT. Interior.cs turns them Soft on Enter and None on Leave: the
                // lighting is culled per object, the shadow maps are not - three shadowed point lights
                // are 18 shadow faces a frame from a room the player is not in (U30b, first Player log).
                light.shadows = LightShadows.None;
            }

            report.Notes.Add($"Place_PizzaInterior: {cfg.Lights.Count} lamps, left on (URP culls them); shadows only while inside");
        }

        private static void BindInteriorComponent(
            GameObject instance, TheBlockConfig.InteriorSpec cfg, TheBlockConfig.PlayerSpec player,
            Vector3 offset, Report report)
        {
            var interior = instance.AddComponent<World.Interior>();
            var palette = cfg.Palette;

            // Written through SerializedObject rather than public fields: the component's inputs are
            // [SerializeField] private, which is what stops anything at runtime from moving the
            // doorway, and this is a build-time write of the same kind CarBuilder does.
            var so = new UnityEditor.SerializedObject(interior);
            so.FindProperty("streetDoor").vector3Value =
                Convert.Pos(cfg.StreetDoorTrigger.Raw);
            so.FindProperty("streetDoorRadius").floatValue = cfg.StreetDoorTrigger.Radius;
            so.FindProperty("spawnPoint").vector3Value = offset + Convert.Pos(cfg.Spawn.Raw);
            so.FindProperty("spawnYaw").floatValue = Convert.Yaw(cfg.Spawn.Yaw) * Mathf.Rad2Deg;
            so.FindProperty("exitPad").vector3Value = offset + Convert.Pos(cfg.ExitPad.Raw);
            so.FindProperty("exitPadRadius").floatValue = cfg.ExitPad.Radius;

            // The counter circle: U21's shift trigger, and the same numbers the cashier is placed on.
            if (cfg.Npc != null)
            {
                so.FindProperty("counterNpc").vector3Value =
                    offset + Convert.Pos(new Vector3(cfg.Npc.X, 0f, cfg.Npc.Z));
                so.FindProperty("counterTalkRadius").floatValue = cfg.Npc.TalkRadius;
            }
            // Stepping back out lands at the player's own spawn height, which is what the web build
            // uses too - the doorway's config carries no y of its own.
            if (player != null) so.FindProperty("streetY").floatValue = player.Spawn.Y;

            if (palette?.Fog != null)
            {
                so.FindProperty("fogColor").colorValue = TheBlockConfig.ColorFromHex(palette.Fog.Color);
                so.FindProperty("fogNear").floatValue = palette.Fog.Near;
                so.FindProperty("fogFar").floatValue = palette.Fog.Far;
            }

            if (palette?.Ambient != null)
            {
                so.FindProperty("ambientColor").colorValue =
                    TheBlockConfig.ColorFromHex(palette.Ambient.Color);
                so.FindProperty("ambientIntensity").floatValue = palette.Ambient.Intensity;
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            report.Notes.Add(
                $"Place_PizzaInterior: door at {Fmt(Convert.Pos(cfg.StreetDoorTrigger.Raw))} " +
                $"r{cfg.StreetDoorTrigger.Radius:0.#}, spawn {Fmt(offset + Convert.Pos(cfg.Spawn.Raw))}");
        }
    }
}
