using System.Collections.Generic;
using TheBlock.Npc;
using UnityEngine;

namespace TheBlock.Traffic
{
    /// <summary>
    /// Every traffic light in the city, on one timer each — the port of <c>traffic-lights.ts</c>.
    ///
    /// A node is LIT when three or more streets meet at it. Its approaches are split into two phase
    /// groups by the axis they arrive on (mostly-X against mostly-Z), and the controller alternates
    /// the groups through the Israeli cycle: red → red+yellow → green → yellow → red.
    ///
    /// <b>The phase timer is the single source of truth.</b> Cars ask <see cref="MayEnter"/>,
    /// pedestrians ask <see cref="PedMayCross"/> and the lamps are painted from the same state in
    /// the same pass, so what a driver sees and what the sim believes cannot drift apart. That is
    /// the web build's design and it is worth keeping verbatim: the alternative — lights animating
    /// on their own clock — is a bug you can only find by standing at a junction.
    ///
    /// <b>This is where U16's seam closes.</b> <c>Crossing.Gate</c> was left as a
    /// <c>Func&lt;bool&gt;</c> so this unit could fill it, and <see cref="AssignGates"/> is that one
    /// line: every crossing keyed by its node and edge now answers to its own light instead of to
    /// U16's "is anything driving over it" stand-in, which is deleted.
    ///
    /// <b>A pedestrian crosses on FULL red only.</b> Red+yellow is excluded on purpose — those cars
    /// are a second and a half from launching.
    /// </summary>
    [DisallowMultipleComponent]
    public class TrafficLightSystem : MonoBehaviour
    {
        /// <summary>One approach: a street arriving at a node, its phase group, and its pole.</summary>
        [System.Serializable]
        public class Approach
        {
            public int EdgeId;

            /// <summary>0 = mostly-X arrivals, 1 = mostly-Z. The two halves of the cycle.</summary>
            public int Group;

            public TrafficLightPole Pole;
        }

        /// <summary>One lit intersection.</summary>
        [System.Serializable]
        public class Controller
        {
            public int NodeId;

            /// <summary>Seconds into the cycle this junction starts at, so the city does not blink as one.</summary>
            public float StartOffset;

            public List<Approach> Approaches = new();

            [System.NonSerialized] public float Time;
            [System.NonSerialized] public LampState GroupA;
            [System.NonSerialized] public LampState GroupB;
        }

        [Tooltip("Seconds of green per phase group. `config.traffic.lights.greenSec`.")]
        [SerializeField] private float greenSec = 8f;

        [Tooltip("Seconds of amber before the light drops to red.")]
        [SerializeField] private float yellowSec = 2.5f;

        [Tooltip("Seconds of red+amber before the waiting group's green. The Israeli 'get ready'.")]
        [SerializeField] private float redYellowSec = 1.5f;

        [Tooltip("Seconds with both groups red, so the junction empties between phases.")]
        [SerializeField] private float allRedSec = 1f;

        [Tooltip("Every lit intersection, written by The Block → Build World.")]
        [SerializeField] private List<Controller> controllers = new();

        /// <summary>Half a cycle: one group's green, its amber, the all-red, then the other's red+amber.</summary>
        private float _half;
        private float _cycle;

        /// <summary>(node, edge) → phase group. 230-odd entries, built once.</summary>
        private readonly Dictionary<long, int> _groupOf = new();

        /// <summary>node → controller, so a car's query is a hash and not a scan.</summary>
        private readonly Dictionary<int, Controller> _byNode = new();

        public int LitIntersections => controllers.Count;

        /// <summary>Set by <c>WorldBuilder.Traffic</c>; the timings come from the config.</summary>
        public void Configure(float green, float yellow, float redYellow, float allRed, List<Controller> built)
        {
            greenSec = green;
            yellowSec = yellow;
            redYellowSec = redYellow;
            allRedSec = allRed;
            controllers = built;
        }

        private void Awake()
        {
            _half = greenSec + yellowSec + allRedSec + redYellowSec;
            _cycle = _half * 2f;

            foreach (var controller in controllers)
            {
                if (controller == null) continue;
                _byNode[controller.NodeId] = controller;
                controller.Time = controller.StartOffset;
                controller.GroupA = LampState.Red;
                controller.GroupB = LampState.Red;
                foreach (var approach in controller.Approaches)
                    _groupOf[Key(controller.NodeId, approach.EdgeId)] = approach.Group;
            }
        }

        private void Start() => AssignGates();

        /// <summary>
        /// Hands every crossing its light. Done in <c>Start</c> rather than <c>Awake</c> because the
        /// crossings register themselves in <c>OnEnable</c>, and a scene's <c>Awake</c> order does
        /// not guarantee they have.
        /// </summary>
        private void AssignGates()
        {
            int gated = 0, orphaned = 0;

            foreach (var crossing in CrossingRegistry.All)
            {
                if (crossing == null) continue;

                int node = crossing.NodeId;
                int edge = crossing.EdgeId;
                if (!_groupOf.ContainsKey(Key(node, edge))) { orphaned++; continue; }

                crossing.Gate = () => PedMayCross(node, edge);
                gated++;
            }

            if (orphaned > 0)
            {
                // A crossing whose node and edge this system has never heard of means the graph the
                // crossings were built from is not the graph the lights were built from — which is
                // what happens if the Crossings group is a leftover from an older build. Say so
                // loudly: the symptom otherwise is a handful of zebras nobody ever waits at.
                Debug.LogWarning(
                    $"TrafficLightSystem: {orphaned} crossing(s) have no light for their (node, edge) — " +
                    "they are from an older graph. Re-run The Block → Build World + NavMesh (slow).",
                    this);
            }

            Debug.Log($"TrafficLightSystem: {controllers.Count} lit intersection(s), {gated} crossing(s) gated.");
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            foreach (var controller in controllers)
            {
                if (controller == null) continue;

                controller.Time = Mathf.Repeat(controller.Time + dt, _cycle);
                bool firstHalf = controller.Time < _half;
                float u = firstHalf ? controller.Time : controller.Time - _half;

                var active = ActiveState(u);
                var waiting = WaitingState(u);
                controller.GroupA = firstHalf ? active : waiting;
                controller.GroupB = firstHalf ? waiting : active;

                foreach (var approach in controller.Approaches)
                {
                    if (approach.Pole == null) continue;
                    approach.Pole.Apply(approach.Group == 0 ? controller.GroupA : controller.GroupB);
                }
            }
        }

        /// <summary>The group whose half-cycle this is: green, then amber, then red for the tail.</summary>
        private LampState ActiveState(float u)
        {
            if (u < greenSec) return LampState.Green;
            if (u < greenSec + yellowSec) return LampState.Yellow;
            return LampState.Red;
        }

        /// <summary>The other group: red, then red+amber just before its own green.</summary>
        private LampState WaitingState(float u) =>
            u >= _half - redYellowSec ? LampState.RedYellow : LampState.Red;

        /// <summary>Is this intersection controlled at all?</summary>
        public bool HasLight(int nodeId) => _byNode.ContainsKey(nodeId);

        /// <summary>May a car arriving on this edge enter this intersection right now?</summary>
        public bool MayEnter(int nodeId, int edgeId)
        {
            if (!_byNode.TryGetValue(nodeId, out var controller)) return true;
            if (!_groupOf.TryGetValue(Key(nodeId, edgeId), out int group)) return true;
            return (group == 0 ? controller.GroupA : controller.GroupB) == LampState.Green;
        }

        /// <summary>
        /// May a pedestrian cross this edge's road here right now? FULL red only — red+amber means
        /// the cars are about to go.
        /// </summary>
        public bool PedMayCross(int nodeId, int edgeId)
        {
            if (!_byNode.TryGetValue(nodeId, out var controller)) return true;
            if (!_groupOf.TryGetValue(Key(nodeId, edgeId), out int group)) return true;
            return (group == 0 ? controller.GroupA : controller.GroupB) == LampState.Red;
        }

        private static long Key(int nodeId, int edgeId) => ((long)nodeId << 32) | (uint)edgeId;
    }
}
