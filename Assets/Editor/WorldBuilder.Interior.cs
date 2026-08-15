using TheBlock.Core;
using UnityEngine;
using Convert = TheBlock.Core.Convert;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Builds the pizzeria's interior cell (U13) — the room, its lamps, and the
    /// <see cref="World.Interior"/> component that owns the doorway.
    ///
    /// The room's contents are NOT here: the counter NPC and the pizza-box stack belong to U21's
    /// delivery mission, which is what consumes them. This unit builds the place.
    /// </summary>
    public static partial class WorldBuilder
    {
        /// <summary>
        /// Metres of three.js point-light "intensity" per Unity candela.
        ///
        /// A pure re-derivation, like every other number that crosses a renderer boundary (port rule
        /// 2): three's intensity means something different in every version and none of those
        /// meanings is URP's. The config's 40 and 30 become 4 and 3, which is a warm ceiling lamp in
        /// a 3.6 m room — the shape of the lighting (one bright lamp over the counter, two dimmer
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
            BindInteriorComponent(instance, cfg, player, offset, report);

            report.Placed.Add($"{instance.name} @ {Fmt(instance.transform.position)}");
        }

        /// <summary>
        /// The room's warm lamps.
        ///
        /// They stay ON, unlike the web build's, which switch off the moment the player steps out —
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
                // frame as the offset — it is stated relative to the room, but it is not a
                // model-local offset, so it takes the world conversion and not ModelOffset.
                go.transform.position = offset + Convert.Pos(spec.Raw);

                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = TheBlockConfig.ColorFromHex(spec.Color);
                light.intensity = spec.Intensity * InteriorLightScale;
                light.range = spec.Distance;
                light.shadows = LightShadows.Soft;
            }

            report.Notes.Add($"Place_PizzaInterior: {cfg.Lights.Count} lamps, left on (URP culls them)");
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
            // Stepping back out lands at the player's own spawn height, which is what the web build
            // uses too — the doorway's config carries no y of its own.
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
