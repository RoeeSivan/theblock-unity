using System.Collections.Generic;
using UnityEngine;

namespace TheBlock.Missions
{
    /// <summary>
    /// Keeps a hull out of the buoys — the port of <c>chase-thief.ts</c>'s radial push-out, shared
    /// by BOTH skis.
    ///
    /// <b>This exists because the obvious Unity answer does not work, and the ledger's rule is that
    /// "can Unity do this better?" has to be allowed to answer NO.</b> The plan for this unit said
    /// the web needs two separate avoidance mechanisms — a Rapier cylinder for the player and a
    /// hand-written push-out for its body-less thief — and that Unity would delete both because the
    /// buoys are colliders. That was wrong, and measuring it is what showed why: both skis are
    /// KINEMATIC rigidbodies (their motion is scripted onto a water plane, because U12 built the sea
    /// as a shader surface with no volume to be buoyant in), and a kinematic body does not collide
    /// with a static collider. PhysX generates no response in that pair, so nine buoys would have
    /// been nine pieces of scenery you drive straight through.
    ///
    /// The honest fix is the web's own, and it turns out to be BETTER than the web's arrangement
    /// rather than worse: one mechanism instead of two, applied identically to the player and the
    /// thief, so they cannot disagree about where a buoy is.
    ///
    /// It is a position correction, not a force: after a step lands inside a buoy's clearance
    /// circle, the hull is pushed radially back out to the rim. Cheap, exact, and it can never
    /// tunnel, because it is checked against the position that was actually reached.
    /// </summary>
    public static class BuoyField
    {
        /// <summary>
        /// Buoy collider radius (1.0) plus half a ski hull plus a margin — the web's own number,
        /// and it holds here because the hull is the same 1.47 m wide model.
        /// </summary>
        public const float Clearance = 2.6f;

        private static readonly List<Vector3> Buoys = new();

        /// <summary>Replaces the field. Called by the chase when it spawns or clears its gates.</summary>
        public static void Set(IEnumerable<Vector3> positions)
        {
            Buoys.Clear();
            if (positions == null) return;
            foreach (var position in positions) Buoys.Add(position);
        }

        public static void Clear() => Buoys.Clear();

        public static int Count => Buoys.Count;

        /// <summary>
        /// Pushes <paramref name="position"/> out of any buoy it has landed inside. Y is untouched:
        /// the waterline is the caller's business and a buoy is not something you go over.
        /// </summary>
        public static Vector3 PushOut(Vector3 position)
        {
            for (var i = 0; i < Buoys.Count; i++)
            {
                var dx = position.x - Buoys[i].x;
                var dz = position.z - Buoys[i].z;
                var sqr = dx * dx + dz * dz;
                if (sqr >= Clearance * Clearance) continue;

                // Dead centre has no direction to push along. It cannot happen from a moving hull,
                // but a spawn could land there, so pick one rather than divide by zero.
                var d = Mathf.Sqrt(sqr);
                if (d < 0.01f)
                {
                    position.x = Buoys[i].x + Clearance;
                    continue;
                }

                position.x = Buoys[i].x + dx / d * Clearance;
                position.z = Buoys[i].z + dz / d * Clearance;
            }

            return position;
        }

        /// <summary>
        /// Static state and Play mode do not mix by default — the same guard
        /// <see cref="UI.MapRegistry"/> keeps. A field left over from a previous session would have
        /// the player bouncing off buoys that are not there.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Buoys.Clear();
    }
}
