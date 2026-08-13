using System.Collections.Generic;
using TheBlock.Core;
using UnityEngine;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// Global registry of all enterable vehicles (cars, bike, helicopter, jetski).
    /// Lets VehicleEnterExit find the nearest one via Nearest() instead of only searching CarSpawner's cars.
    /// </summary>
    public static class EnterableRegistry
    {
        private static readonly List<IEnterable> Vehicles = new();

        /// <summary>Register a vehicle when it spawns.</summary>
        public static void Register(IEnterable vehicle)
        {
            if (!Vehicles.Contains(vehicle))
                Vehicles.Add(vehicle);
        }

        /// <summary>Unregister a vehicle when it despawns.</summary>
        public static void Unregister(IEnterable vehicle)
        {
            Vehicles.Remove(vehicle);
        }

        /// <summary>Get all currently registered enterable vehicles.</summary>
        public static IReadOnlyList<IEnterable> All => Vehicles;

        /// <summary>Clear all registrations (for testing / scene resets).</summary>
        public static void Clear() => Vehicles.Clear();
    }
}
