using UnityEngine;

namespace TheBlock.Npc
{
    /// <summary>
    /// The runtime twin of <c>WorldBuilder.Navigation</c>'s <c>GroundY</c>: what height is the
    /// pavement at this XZ, right now.
    ///
    /// The crowd needs this because U16b took the NavMeshAgent away — nothing puts a pedestrian on
    /// the floor for us any more. It carries the same two scars the build-time version earned, and
    /// they are not obvious:
    ///
    ///  1. <b>Take the LOWEST hit, not the first.</b> A flat plate sits at y −0.05 under every
    ///     street, and every district's own geometry sits on top of it. The first hit going down is
    ///     whatever awning, balcony or sign happens to be overhead (memory:
    ///     <c>lowest-raycast-hit-is-the-ground-plate</c>).
    ///  2. <b>The lowest hit can still be a roof.</b> Downtown is one merged mesh with no street
    ///     surface modelled under parts of its avenue, so the only thing below is the building's own
    ///     roof several metres up. Accepting a hit only when it is within a storey of the plate is
    ///     what catches those; U17 found nine in 6,590 samples.
    ///
    /// Plus one the build-time version never needed: <b>ignore anything with a Rigidbody</b>. At
    /// build time nothing moves, but at runtime a pedestrian would otherwise be placed on the roof
    /// of a passing car — the web build's <c>staticOnly: true</c>, in the one place it matters.
    /// </summary>
    public static class CrowdGround
    {
        /// <summary>Metres above the plate a surface may be and still be a street. Matches the bake.</summary>
        public const float StreetHeightBand = 2f;

        /// <summary>The plate's collider name, from <c>WorldBuilder</c>. Recognised, not excluded.</summary>
        private const string PlateName = "Ground Floor";

        /// <summary>Allocated once. Eight is deeper than any stack of geometry in this world.</summary>
        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        /// <summary>
        /// Ground height at <paramref name="point"/>'s XZ, searching a band around its own Y.
        ///
        /// A local search rather than the build-time "from 50 m down 200 m", because a walking
        /// person already knows roughly what floor they are on and a full-height ray would happily
        /// find the pavement three storeys below a rooftop.
        /// </summary>
        /// <returns>False when nothing was under the point at all — the caller keeps its old Y.</returns>
        public static bool TrySample(Vector3 point, int layerMask, float up, float down, out float y)
        {
            int count = Physics.RaycastNonAlloc(
                new Vector3(point.x, point.y + up, point.z), Vector3.down, Hits, up + down,
                layerMask, QueryTriggerInteraction.Ignore);

            float lowest = float.MaxValue;
            float plate = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var hit = Hits[i];

                // Anything that can move is not the floor. A car, a bike, another pedestrian's
                // capsule — all of them would otherwise be walked on.
                if (hit.collider.attachedRigidbody != null) continue;

                if (hit.collider.name == PlateName) { plate = Mathf.Min(plate, hit.point.y); continue; }
                lowest = Mathf.Min(lowest, hit.point.y);
            }

            bool plausible = lowest != float.MaxValue &&
                             (plate == float.MaxValue || lowest - plate <= StreetHeightBand);

            if (plausible) { y = lowest; return true; }
            if (plate != float.MaxValue) { y = plate; return true; }
            if (lowest != float.MaxValue) { y = lowest; return true; }

            y = point.y;
            return false;
        }
    }
}
