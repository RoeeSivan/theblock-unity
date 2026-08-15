using System;
using UnityEngine;

namespace TheBlock.Npc
{
    /// <summary>
    /// One zebra crossing: the only place a pedestrian may set foot on a carriageway.
    ///
    /// The web build has these too — <c>traffic.ts</c> derives one per approach of every lit
    /// intersection — but there they are only a line two dedicated "crosser" pedestrians walk back
    /// and forth along, pinned to that spot for the whole session. Everyone else was a wanderer on
    /// a sampled sidewalk grid that had no idea roads existed, which is why people drifted into the
    /// street in the first place.
    ///
    /// Here the crossing is a hole in the world's connectivity instead of a scripted walk. The
    /// carriageway is baked <c>Not Walkable</c> end to end (see <c>WorldBuilder.Navigation</c>), so
    /// a NavMesh path from one kerb to the other simply does not exist — except through the
    /// <see cref="UnityEngine.AI.NavMeshLink"/> this component sits on. Any pedestrian who wants
    /// the far side routes over a zebra because there is nothing else to route over, and no
    /// pedestrian is assigned to a crossing at all.
    ///
    /// <see cref="MayCross"/> is the gate. U16 answers it with "no vehicle is on the line right
    /// now"; U17 replaces <see cref="Gate"/> with the light controller and nothing else changes —
    /// the same seam the web build has as <c>CrossingSpec.mayCross</c>.
    /// </summary>
    [DisallowMultipleComponent]
    public class Crossing : MonoBehaviour
    {
        [Tooltip("Kerb endpoints in world space, kerb A → kerb B, across the carriageway.")]
        [SerializeField] private Vector3 kerbA;
        [SerializeField] private Vector3 kerbB;

        [Tooltip("Traffic-graph intersection this crossing belongs to. U17 keys its light off this.")]
        [SerializeField] private int nodeId = -1;

        [Tooltip("Traffic-graph street being crossed. U17 keys the light's phase off this.")]
        [SerializeField] private int edgeId = -1;

        [Tooltip("How far either side of the line still counts as a vehicle being on it.")]
        [SerializeField] private float clearanceRadius = 3.5f;

        /// <summary>Samples taken along the line when testing it for traffic. Endpoints included.</summary>
        private const int ClearanceSamples = 5;

        private static readonly Collider[] Overlap = new Collider[8];

        public Vector3 KerbA => kerbA;
        public Vector3 KerbB => kerbB;
        public int NodeId => nodeId;
        public int EdgeId => edgeId;

        /// <summary>
        /// The signal. Null means U16's fallback — the line is open whenever it is clear of traffic.
        /// U17 assigns the light controller here and the fallback stops being consulted.
        /// </summary>
        public Func<bool> Gate { get; set; }

        /// <summary>Set by <c>WorldBuilder.Navigation</c> at build time.</summary>
        public void Configure(Vector3 a, Vector3 b, int node, int edge)
        {
            kerbA = a;
            kerbB = b;
            nodeId = node;
            edgeId = edge;
        }

        private void OnEnable() => CrossingRegistry.Register(this);

        private void OnDisable() => CrossingRegistry.Unregister(this);

        /// <summary>May a pedestrian step off the kerb this frame?</summary>
        public bool MayCross() => Gate?.Invoke() ?? IsClearOfTraffic();

        /// <summary>
        /// True when nothing with a Rigidbody is sitting on the line.
        ///
        /// Deliberately crude, and deliberately not a prediction: with no traffic system yet the
        /// only things that can be here are the player's own car and the bike, and a pedestrian who
        /// waits for a stationary car to move would wait forever. U17's lights make the question a
        /// real one, and this method goes away when they do.
        /// </summary>
        private bool IsClearOfTraffic()
        {
            for (int i = 0; i < ClearanceSamples; i++)
            {
                var point = Vector3.Lerp(kerbA, kerbB, i / (float)(ClearanceSamples - 1));
                int hits = Physics.OverlapSphereNonAlloc(
                    point, clearanceRadius, Overlap, ~0, QueryTriggerInteraction.Ignore);

                for (int h = 0; h < hits; h++)
                {
                    // A Rigidbody is the cheap test for "something that drives": the world's static
                    // geometry has none, and pedestrians are CharacterControllers, which have none
                    // either. Without it every sample would hit the road it is painted on.
                    if (Overlap[h].attachedRigidbody != null) return false;
                }
            }

            return true;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(kerbA, kerbB);
            Gizmos.DrawWireSphere(kerbA, 0.4f);
            Gizmos.DrawWireSphere(kerbB, 0.4f);
        }
    }
}
