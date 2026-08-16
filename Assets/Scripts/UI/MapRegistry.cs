using System.Collections.Generic;
using UnityEngine;

namespace TheBlock.UI
{
    /// <summary>What a POI looks like on the map - the port of <c>PoiKind</c> in the web build.</summary>
    public enum MapPoiKind
    {
        Pizza,
        Vehicle,
        Spawn,

        /// <summary>Everything else, and the hook missions hang their objectives on.</summary>
        Marker,

        /// <summary>A live police car. Drawn last, pulsing, and only while a pursuit is running.</summary>
        Cop,
    }

    /// <summary>A pin on the map. <see cref="Position"/> is a UNITY world position, already converted.</summary>
    public class MapPoi
    {
        public string Name;
        public Vector3 Position;
        public MapPoiKind Kind = MapPoiKind.Marker;

        /// <summary>
        /// An emoji drawn INSTEAD of the coloured dot - 🍕 for the pizzeria, ⛽ for the pumps.
        ///
        /// Standing places take one; mission pins deliberately do not. Every fixed landmark and
        /// every objective used to be the same green <see cref="MapPoiKind.Marker"/> dot, so during
        /// a mission the map showed a row of identical circles and the one that mattered was
        /// indistinguishable from the police station. Giving the permanent places a picture leaves
        /// the plain dot meaning exactly one thing: go here.
        ///
        /// Empty (the default) means draw the dot, so nothing that does not opt in changes.
        /// </summary>
        public string Icon;

        /// <summary>
        /// Something to track, for a pin that moves.
        ///
        /// <see cref="Position"/> is a snapshot, which is right for a building and useless for a
        /// police car. Rather than have the pursuit rewrite its pins every frame, a POI can hold the
        /// transform and the map reads it at draw time - six lines that also give U20's objective on
        /// a walking NPC and U32's rival arrow the thing they will both want.
        /// </summary>
        public Transform Follow;

        /// <summary>Where to draw this pin: the followed thing if there is one, else the snapshot.</summary>
        public Vector3 At => Follow != null ? Follow.position : Position;

        /// <summary>
        /// Draw the dot but never the name. For the numbered sets a mission spawns in bulk -
        /// "Delivery #3", "Gate 7" - where the label says nothing the dot doesn't and ten at once is
        /// most of what makes the open map unreadable. The name still has to be unique:
        /// <see cref="MapRegistry.RemovePoi"/> finds by name.
        /// </summary>
        public bool Minor;
    }

    /// <summary>A district's world footprint and the name shown over it.</summary>
    public class MapDistrict
    {
        public string Name;
        public Bounds Bounds;
    }

    /// <summary>
    /// Runtime index of everything the map draws - the port of <c>src/world/registry.ts</c>.
    ///
    /// Subsystems register into it as they load, so adding content never touches the map code. It is
    /// static because it outlives any one object: a district's outline and label keep being drawn
    /// whether or not its meshes are currently resident (a city map does not blank out the part of
    /// town you cannot see), which is also what U15's streaming will need.
    ///
    /// Static state and Play mode do not mix by default - see the <c>recompile-during-play-nulls-fields</c>
    /// memory - so the lists are cleared on every entry into Play rather than trusted to be empty.
    /// </summary>
    public static class MapRegistry
    {
        private static readonly List<MapDistrict> _districts = new();
        private static readonly List<MapPoi> _pois = new();

        public static IReadOnlyList<MapDistrict> Districts => _districts;
        public static IReadOnlyList<MapPoi> Pois => _pois;

        public static void AddDistrict(string name, Bounds bounds) =>
            _districts.Add(new MapDistrict { Name = name, Bounds = bounds });

        public static void AddPoi(MapPoi poi)
        {
            if (poi == null) return;
            _pois.Add(poi);
        }

        /// <summary>Removes a POI by name - for dynamic markers, e.g. a drop-off that is done.</summary>
        public static void RemovePoi(string name)
        {
            var i = _pois.FindIndex(p => p.Name == name);
            if (i >= 0) _pois.RemoveAt(i);
        }

        public static void Clear()
        {
            _districts.Clear();
            _pois.Clear();
        }

        /// <summary>
        /// World bounds of every district and POI, in metres. This is what frames the full map.
        /// Returns false when nothing has registered yet.
        /// </summary>
        public static bool TryWorldBounds(out Bounds bounds)
        {
            var any = false;
            bounds = default;

            foreach (var d in _districts)
            {
                if (!any) { bounds = d.Bounds; any = true; }
                else bounds.Encapsulate(d.Bounds);
            }

            foreach (var p in _pois)
            {
                if (!any) { bounds = new Bounds(p.Position, Vector3.zero); any = true; }
                else bounds.Encapsulate(p.Position);
            }

            return any;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Clear();
    }
}
