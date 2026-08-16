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

        /// <summary>
        /// Signed speed along the vehicle's own forward, in m/s — negative while reversing.
        ///
        /// On the interface because U18's run-over asks every vehicle the same question and must not
        /// care which one it is holding. Both the car and the bike already answered it; the sign is
        /// what tells the knockback which way to throw a body when someone reverses over them.
        /// </summary>
        float ForwardSpeed { get; }

        /// <summary>
        /// Unsigned speed for the HUD readout, in km/h.
        ///
        /// On the interface so <see cref="UI.PlayerMeters"/> can ask whatever is being driven without
        /// four branches — and because it is NOT just <c>|ForwardSpeed| × 3.6</c> for all of them: a
        /// helicopter travels sideways and backwards perfectly happily, so it answers with its full
        /// velocity magnitude. All four already implemented this before it was declared here.
        /// </summary>
        float SpeedKmh { get; }

        /// <summary>True while somebody is driving it.</summary>
        bool Driven { get; set; }

        /// <summary>
        /// Null when <see cref="TryEnter"/> would let you in; otherwise the reason it will not, in
        /// the words the player should read.
        ///
        /// It exists so the PROMPT and the ACTION cannot disagree. Two of the four vehicles are
        /// gated by the campaign, and without this the doorstep of a locked Huey offered
        /// "Press E to enter" for a key that then did nothing — the exact failure the web build's
        /// cashier had, and the reason every prompt in this port is drawn from the same predicate
        /// its action tests.
        /// </summary>
        string EntryRefusal => null;

        /// <summary>Try to get in. True if successful, false if refused.</summary>
        bool TryEnter();

        /// <summary>Called as the rider steps off.</summary>
        void Exit();

        /// <summary>Back to where it was put down, upright and stopped — the <c>R</c> key.</summary>
        void Respawn();

        /// <summary>
        /// Puts this vehicle somewhere else, stopped. Unlike <see cref="Respawn"/> the caller chooses
        /// the pose — U21's retry parks your ride outside the pizzeria while you take the briefing.
        ///
        /// The default is the whole implementation for anything on a plain Rigidbody, in the order
        /// the physics requires: stop it, move it, then sync, so the next step does not resolve the
        /// old pose against the new one. <see cref="CarController"/> declares its own and therefore
        /// keeps it — a car also has to zero four <c>WheelCollider</c> steer angles and re-apply the
        /// brake, which nothing generic can know about.
        /// </summary>
        void Teleport(Vector3 position, Quaternion rotation)
        {
            var root = GetTransform();
            if (root.TryGetComponent<Rigidbody>(out var body))
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            root.SetPositionAndRotation(position, rotation);
            Physics.SyncTransforms();
        }
    }
}
