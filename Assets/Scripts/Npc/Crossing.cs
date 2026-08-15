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
    /// <see cref="MayCross"/> is the gate, and as of U17 it is the traffic light.
    /// <c>TrafficLightSystem.AssignGates</c> hands every crossing a closure over its own
    /// <see cref="NodeId"/> and <see cref="EdgeId"/> at startup, which is the same seam the web
    /// build has as <c>CrossingSpec.mayCross</c>. U16's stand-in — an overlap sphere asking whether
    /// anything with a Rigidbody was sitting on the line — is gone: it could not tell a stopped car
    /// from a moving one, so a pedestrian would have waited on a parked car forever, and with real
    /// traffic the question it was approximating is now answerable exactly.
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

        public Vector3 KerbA => kerbA;
        public Vector3 KerbB => kerbB;
        public int NodeId => nodeId;
        public int EdgeId => edgeId;

        /// <summary>
        /// The signal, assigned by <c>TrafficLightSystem</c> at startup. Null means no light system
        /// is running at all — a scene with the crowd but no traffic — and then the line is simply
        /// open, which is what it was before U17 built anything that could drive over it.
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
        public bool MayCross() => Gate?.Invoke() ?? true;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(kerbA, kerbB);
            Gizmos.DrawWireSphere(kerbA, 0.4f);
            Gizmos.DrawWireSphere(kerbB, 0.4f);
        }
    }
}
