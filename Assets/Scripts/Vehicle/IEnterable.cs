using TheBlock.Core;
using UnityEngine;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// Something that can be entered and exited with E, and driven by Joe.
    /// Implemented by: CarController, MotorcycleController, and later Helicopter, Jetski.
    ///
    /// This lets VehicleEnterExit.Nearest() search all enterable vehicles at once rather than
    /// only CarSpawner's cars. A vehicle that is not a car (e.g. the bike, or U23's helicopter)
    /// registers itself at startup; Nearest() walks a global registry.
    /// </summary>
    public interface IEnterable : IChaseTarget
    {
        /// <summary>The vehicle's Transform component.</summary>
        Transform GetTransform();

        /// <summary>Where the driver/rider sits, in the vehicle's local space.</summary>
        Transform RiderAnchor { get; }

        /// <summary>The hinged door, if this vehicle has one. Null for door-less vehicles (motorcycle, jetski).</summary>
        CarDoor Door { get; }

        /// <summary>The side the exit will use (+X for right, -X for left). Defaults to -X (driver's door on left).</summary>
        Vector3 DriverSide => Vector3.left;

        /// <summary>True if the vehicle is currently being driven.</summary>
        bool Driven { get; set; }

        /// <summary>Try to get in. True if successful, false if refused (e.g. already driving).</summary>
        bool TryEnter();

        /// <summary>Exit the vehicle and return to foot.</summary>
        void Exit();

        /// <summary>Respawn to the configured spawn point (R key).</summary>
        void Respawn();
    }
}
