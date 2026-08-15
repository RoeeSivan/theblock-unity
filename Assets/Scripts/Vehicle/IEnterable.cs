using TheBlock.Core;
using UnityEngine;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// Something you can get into with <c>E</c> and drive. Implemented by <see cref="CarController"/>
    /// and <see cref="MotorcycleController"/>, and by U23's helicopter and U24's jetski next.
    ///
    /// The three defaulted members below are what let ONE enter/exit machine serve all of them
    /// without growing a third code path per vehicle: a car walks its driver up and opens a door, a
    /// bike is simply sat on. Everything else about getting in is identical, so it is stated once in
    /// <see cref="VehicleEnterExit"/> and parameterised here.
    /// </summary>
    public interface IEnterable : IChaseTarget
    {
        /// <summary>The vehicle's Transform component.</summary>
        Transform GetTransform();

        /// <summary>
        /// Where the rider ends up, in the vehicle's local space.
        ///
        /// Read it against <see cref="UsesEntryAnimation"/> before assuming what it means: on a car
        /// it is where the entry CLIP starts — the driver standing beside the door at road level,
        /// with the clip's own baked travel doing the sitting. On a bike it is a literal seat.
        /// </summary>
        Transform RiderAnchor { get; }

        /// <summary>The hinged door, or null on something door-less (bike, jetski).</summary>
        CarDoor Door { get; }

        /// <summary>
        /// True if this vehicle wants the walk-up-and-sit-down animation. False sits the rider
        /// straight down, which is what anything without a door does.
        /// </summary>
        bool UsesEntryAnimation => true;

        /// <summary>
        /// True if the rider stays VISIBLE on the quick mount. Only the quick mount asks: the entry
        /// animation always leaves him visible, because its held last frame IS the seated pose.
        ///
        /// A car hides him, which is the web build's behaviour for any car with no seat block — the
        /// cabin just looks empty from outside. A bike must not, because a bike driving itself down
        /// the street with nobody on it is the bug.
        /// </summary>
        bool ShowRiderOnQuickMount => false;

        /// <summary>The side you step out onto, as a world direction.</summary>
        Vector3 DriverSide => Vector3.left;

        /// <summary>True while somebody is driving it.</summary>
        bool Driven { get; set; }

        /// <summary>Try to get in. True if successful, false if refused.</summary>
        bool TryEnter();

        /// <summary>Called as the rider steps off.</summary>
        void Exit();

        /// <summary>Back to where it was put down, upright and stopped — the <c>R</c> key.</summary>
        void Respawn();
    }
}
