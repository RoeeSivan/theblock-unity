using System.Collections.Generic;
using UnityEngine;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// Every vehicle currently in the world that <c>E</c> can get you into.
    ///
    /// It exists because <c>VehicleEnterExit.Nearest()</c> used to walk <c>CarSpawner.Spawned</c>,
    /// which works right up to the moment something enterable is not a car. The bike was the first;
    /// U23's helicopter and U24's jetski are the next two.
    ///
    /// Vehicles put themselves in and take themselves out, in <c>OnEnable</c>/<c>OnDisable</c>,
    /// rather than being registered by whatever spawned them — a spawner cannot know when its
    /// vehicle is destroyed, and a stale entry here is a soft lock: <c>E</c> pointed at a corpse.
    /// </summary>
    public static class EnterableRegistry
    {
        private static readonly List<IEnterable> Vehicles = new();

        /// <summary>Called from a vehicle's own <c>OnEnable</c>.</summary>
        public static void Register(IEnterable vehicle)
        {
            if (vehicle != null && !Vehicles.Contains(vehicle)) Vehicles.Add(vehicle);
        }

        /// <summary>Called from a vehicle's own <c>OnDisable</c>.</summary>
        public static void Unregister(IEnterable vehicle) => Vehicles.Remove(vehicle);

        /// <summary>
        /// Every live vehicle. Dead ones are swept on the way out, which is not belt-and-braces:
        /// this list is static, so it survives a domain reload with "Reload Domain" turned off, and
        /// a destroyed MonoBehaviour reached through an INTERFACE reference does not compare equal
        /// to null — the overloaded operator is on <see cref="Object"/>, and an interface does not
        /// carry it. So the sweep has to go through the concrete type to ask the question at all.
        /// </summary>
        public static IReadOnlyList<IEnterable> All
        {
            get
            {
                Vehicles.RemoveAll(IsDead);
                return Vehicles;
            }
        }

        private static bool IsDead(IEnterable vehicle) =>
            vehicle is not MonoBehaviour behaviour || behaviour == null;

        /// <summary>Drops everything. For tests and scene resets.</summary>
        public static void Clear() => Vehicles.Clear();
    }
}
