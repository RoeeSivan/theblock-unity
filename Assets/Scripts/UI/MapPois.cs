using TheBlock.Core;
using TheBlock.World;
using UnityEngine;

namespace TheBlock.UI
{
    /// <summary>
    /// Fills <see cref="MapRegistry"/> at scene start - the port of <c>src/world/map-pois.ts</c>
    /// plus the district half of <c>src/world/sandbox.ts</c>'s registration.
    ///
    /// The web build registers each district as its model finishes loading; here the world is baked
    /// into the scene by WorldBuilder, so one component walks the baked hierarchy once. Coordinates
    /// come from the same config the builder used and go through <see cref="Convert"/> exactly once,
    /// on the way into the registry - everything the map reads back out is already Unity space.
    ///
    /// Deliberately absent from the web build's list, each because its system is not built yet:
    /// "Beach Dance" reads <c>rhythmConfig</c> (U22, not exported), and the 7-Eleven pin exists but
    /// the power-up shop behind it is U24's.
    /// </summary>
    public class MapPois : MonoBehaviour
    {
        private void Start()
        {
            var snapshot = TheBlockConfig.Load();
            if (snapshot == null) return;
            var config = snapshot.Config;

            RegisterDistricts(config);
            RegisterStaticPois(config);
        }

        private void RegisterDistricts(TheBlockConfig.Root config)
        {
            var root = FindAnyObjectByType<WorldRoot>();
            var group = root != null ? root.transform.Find("Districts") : null;
            if (group == null)
            {
                Debug.LogWarning("MapPois: no Districts group under WorldRoot - build the world first.");
                return;
            }

            if (config.City != null) RegisterDistrict(group, config.City.Name);
            if (config.Districts != null)
                foreach (var district in config.Districts)
                    RegisterDistrict(group, district.Name);
        }

        private static void RegisterDistrict(Transform group, string name)
        {
            if (string.IsNullOrEmpty(name)) return;

            // WorldBuilder names instances District_{name minus spaces}.
            var child = group.Find($"District_{name.Replace(" ", string.Empty)}");
            if (child == null) return;

            var renderers = child.GetComponentsInChildren<Renderer>(includeInactive: false);
            if (renderers.Length == 0) return;

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            MapRegistry.AddDistrict(name, bounds);
        }

        private void RegisterStaticPois(TheBlockConfig.Root config)
        {
            // The names are literals, not `place.Name`: a PlaceSpec in config.ts has no name field -
            // the pin's label is typed into map-pois.ts. Reading the missing field back gave every
            // landmark a null label, which is a crash and not a blank pin.
            // The emoji column is the web's, POI for POI (map-pois.ts): the three services that are
            // otherwise indistinguishable green Marker dots get a picture, and the places that
            // already read as themselves - the pizzeria's red dot, the parking lot, spawn - keep
            // theirs. The pizzeria in particular must NOT be 🍕 here: that glyph is mission 1's
            // objective marker, and two pizzas on one map is the confusion, not the fix.
            void Place(TheBlockConfig.PlaceSpec place, string label, MapPoiKind kind, string icon = null)
            {
                if (place == null) return;
                MapRegistry.AddPoi(new MapPoi
                {
                    Name = label,
                    Position = Convert.Pos(place.Position.Raw),
                    Kind = kind,
                    Icon = icon,
                });
            }

            Place(config.PizzaPlace, "The Block Pizza", MapPoiKind.Pizza);
            Place(config.PoliceStation, "Police Station", MapPoiKind.Marker, "🚓");
            Place(config.GasStation, "Gas Station", MapPoiKind.Marker, "⛽");
            Place(config.SevenEleven, "7-Eleven", MapPoiKind.Marker, "🏪");

            // U35g - not a config place; the web build never had it. Same point the builder uses.
            MapRegistry.AddPoi(new MapPoi
            {
                Name = AutoShopSpec.MapLabel,
                Position = AutoShopSpec.Position,
                Kind = MapPoiKind.Marker,
                Icon = AutoShopSpec.MapIcon,
            });

            // Not a config place either - the falafel stand is the game's repeatable street job, and
            // the pin reads the same field the builder placed the model with.
            MapRegistry.AddPoi(new MapPoi
            {
                Name = FalafelSpec.MapLabel,
                Position = FalafelSpec.Position,
                Kind = MapPoiKind.Marker,
                Icon = FalafelSpec.MapIcon,
            });

            // One pin for the whole lot - the web build found per-car POIs stacked unreadably. The
            // point is hand-authored in map-pois.ts, not derived from lotCars.bounds, so it is
            // hand-copied here the same way.
            MapRegistry.AddPoi(new MapPoi
            {
                Name = "Parking",
                Position = Convert.Pos(-220f, 0f, -250f),
                Kind = MapPoiKind.Vehicle,
            });

            if (config.Vehicle?.Motorcycle != null)
                MapRegistry.AddPoi(new MapPoi
                {
                    Name = "Motorcycle",
                    Position = Convert.Pos(config.Vehicle.Motorcycle.Spawn.Raw),
                    Kind = MapPoiKind.Vehicle,
                });

            if (config.Player != null)
                MapRegistry.AddPoi(new MapPoi
                {
                    Name = "Start",
                    Position = Convert.Pos(config.Player.Spawn.Raw),
                    Kind = MapPoiKind.Spawn,
                });

            if (config.Sea != null)
                MapRegistry.AddPoi(new MapPoi
                {
                    Name = "Beach",
                    Position = Convert.Pos(
                        config.Sea.ShoreX + (config.Sea.Beach?.DryWidth ?? 25f) / 2f,
                        0f,
                        config.Sea.CenterZ),
                    Kind = MapPoiKind.Marker,
                });
        }
    }
}
