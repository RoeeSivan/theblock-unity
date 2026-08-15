using System.Collections.Generic;
using UnityEngine;

namespace TheBlock.Npc
{
    /// <summary>
    /// Every <see cref="Crossing"/> in the world, so a pedestrian standing on a NavMeshLink can ask
    /// which crossing it is standing on without a component lookup per step.
    ///
    /// Same shape and same reasoning as <c>EnterableRegistry</c>: crossings put themselves in and
    /// take themselves out, because the thing that spawned them cannot know when they die. U17 also
    /// walks this list once at startup to hand every crossing its light.
    /// </summary>
    public static class CrossingRegistry
    {
        private static readonly List<Crossing> Crossings = new();

        public static void Register(Crossing crossing)
        {
            if (crossing != null && !Crossings.Contains(crossing)) Crossings.Add(crossing);
        }

        public static void Unregister(Crossing crossing) => Crossings.Remove(crossing);

        /// <summary>Every live crossing. Dead entries swept on the way out — the list is static.</summary>
        public static IReadOnlyList<Crossing> All
        {
            get
            {
                Crossings.RemoveAll(c => c == null);
                return Crossings;
            }
        }

        /// <summary>
        /// The crossing whose kerb-to-kerb line passes closest to <paramref name="point"/>, within
        /// <paramref name="maxDistance"/>. Null when the point is nowhere near one.
        ///
        /// Called only when a pedestrian is about to traverse a link, not per frame per agent.
        /// </summary>
        public static Crossing Nearest(Vector3 point, float maxDistance)
        {
            Crossing best = null;
            float bestSqr = maxDistance * maxDistance;

            foreach (var crossing in All)
            {
                float sqr = SqrDistanceToSegment(point, crossing.KerbA, crossing.KerbB);
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = crossing;
            }

            return best;
        }

        private static float SqrDistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float lengthSqr = ab.sqrMagnitude;
            if (lengthSqr < 1e-6f) return (p - a).sqrMagnitude;

            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / lengthSqr);
            return (p - (a + ab * t)).sqrMagnitude;
        }

        /// <summary>Drops everything. For tests and scene resets.</summary>
        public static void Clear() => Crossings.Clear();
    }
}
