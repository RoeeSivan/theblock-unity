using System.Collections.Generic;
using UnityEngine;

namespace TheBlock.Traffic
{
    /// <summary>
    /// A* over <see cref="RouteGraph"/>: "drive me from here to there, on the streets".
    ///
    /// <b>This is the thing the web build gave up on</b>, and the reason it could be picked back up
    /// is measured rather than hoped: the network stitches from five components down to two, with
    /// 97.9% of the city — and the police station — inside the larger one. Routing failed there
    /// because it was asked to plan across islands; asked only within a component, it cannot.
    ///
    /// One instance per pursuer, reused. Nothing allocates after the first plan: the three A* arrays
    /// are sized to the graph once and cleared by a stamp rather than by refilling, and the open set
    /// is an int heap. At ~106 nodes a search is microseconds, which is what lets every cop replan
    /// once a second without anyone budgeting for it.
    ///
    /// The heuristic is straight-line XZ distance, which is admissible because a link's cost is its
    /// arc length and an arc is never shorter than its chord — so the first route found is the
    /// shortest one.
    /// </summary>
    public class RoutePlanner
    {
        /// <summary>Spacing of the waypoints handed to the driver, metres.</summary>
        private const float WaypointSpacing = 4f;

        /// <summary>
        /// What turning round is worth, in metres of route. A U-turn costs a cop a few seconds, so a
        /// route has to be at least this much shorter to be worth one — otherwise a cop dithers at
        /// every replan as the two options trade places.
        /// </summary>
        private const float UTurnPenalty = 25f;

        /// <summary>
        /// How far off-street a query point may be before it is not on the graph at all.
        ///
        /// <b>120 m, and the number is measured.</b> 60 seemed generous until the game was started:
        /// the Reichman car park where the player spawns is <b>80.2 m</b> from the nearest street and
        /// the Mustang beside them 77.2 m, so at 60 the very first pursuit of every session had no
        /// route, no field spawn and no cop. A car park set back from the road is an ordinary place
        /// to be, not an edge case.
        /// </summary>
        public const float SnapRadius = 120f;

        private readonly RouteGraph _graph;
        private readonly float _laneGap;

        private readonly float[] _gScore;
        private readonly int[] _cameFromLink;
        private readonly int[] _stamp;
        private int _visit;

        private readonly int[] _heap;
        private readonly float[] _heapKey;
        private int _heapCount;

        private readonly List<int> _path = new();

        public RoutePlanner(RouteGraph graph, float laneGap)
        {
            _graph = graph;
            _laneGap = laneGap;

            int nodes = graph != null ? graph.Nodes.Length : 0;
            _gScore = new float[nodes];
            _cameFromLink = new int[nodes];
            _stamp = new int[nodes];
            // Sized by LINKS, not nodes: without decrease-key a node is pushed again every time it
            // is relaxed, and the number of relaxations is bounded by the number of links, not by
            // the number of nodes. Sizing it to nodes overflows on a graph with any choice in it.
            int capacity = (graph != null ? graph.Links.Length : 0) + 2;
            _heap = new int[capacity];
            _heapKey = new float[capacity];
        }

        /// <summary>What a plan produced, for the log and for the driver's own decisions.</summary>
        public readonly struct Result
        {
            public readonly bool Found;
            public readonly int Hops;
            public readonly float RouteMetres;
            public readonly float StraightMetres;
            public readonly int Component;

            public Result(bool found, int hops, float route, float straight, int component)
            {
                Found = found;
                Hops = hops;
                RouteMetres = route;
                StraightMetres = straight;
                Component = component;
            }

            /// <summary>Route length over straight-line. 1.0 is a motorway; 2.0 is a detour.</summary>
            public float Detour => StraightMetres > 1f ? RouteMetres / StraightMetres : 1f;
        }

        /// <summary>
        /// Plans from a world position and heading to a world target, filling
        /// <paramref name="waypoints"/> with lane-centre points.
        ///
        /// <paramref name="heading"/> matters: standing on a street is not the same as travelling
        /// along it, and the span under a car has two directions. Taking the one the car already
        /// faces is what stops every replan from ordering an immediate U-turn.
        ///
        /// The target's own position is appended last, so the final few metres lead off the graph and
        /// onto whatever is actually being chased — the driver takes over there anyway.
        /// </summary>
        public Result Plan(Vector3 from, Vector3 heading, Vector3 to, List<Vector3> waypoints)
        {
            waypoints.Clear();
            float straight = Vector3.Distance(new Vector3(from.x, 0f, from.z), new Vector3(to.x, 0f, to.z));

            if (_graph == null || _graph.Nodes.Length == 0)
                return new Result(false, 0, 0f, straight, -1);

            if (!_graph.TryNearest(from, SnapRadius, -1, out var start))
                return new Result(false, 0, 0f, straight, -1);

            // The target must be reachable from where the cop is, not merely near a street. The
            // downtown avenue is 265 m of real road with no connection to anything, and a route onto
            // it is a cop driving at a kerb forever.
            if (!_graph.TryNearest(to, SnapRadius, start.Component, out var goal))
                return new Result(false, 0, 0f, straight, start.Component);

            var links = _graph.Links;
            int goalA = links[goal.Link].From;
            int goalB = links[goal.Link].To;

            // BOTH ends of the span under the car, not just the one it happens to face.
            //
            // Committing to finish the current span is what a car has to do — it cannot turn through
            // a building — but choosing that end because the nose points at it is how a cop drives
            // 500 m the wrong way: one of this city's edges is 1,364 m long, so "carry on to the end
            // and then route" can be most of a minute in the wrong direction. Measured: cops holding
            // a clean 100% on-road line while their distance to the player climbed from 81 m to 149.
            //
            // So both ends are costed, including the U-turn, and the cheaper one wins.
            int facing = Facing(start.Link, heading);
            int back = links[facing].Twin;

            int startLink = facing;
            float bestTotal = float.MaxValue;
            int reached = -1;

            foreach (int candidate in back >= 0 ? new[] { facing, back } : new[] { facing })
            {
                if (!Search(links[candidate].To, goalA, goalB, out int end)) continue;

                float total = Mathf.Abs(links[candidate].S1 - start.S)
                              + _gScore[end]
                              + (candidate == facing ? 0f : UTurnPenalty);

                if (total >= bestTotal) continue;

                bestTotal = total;
                startLink = candidate;
                reached = end;
            }

            if (reached < 0) return new Result(false, 0, 0f, straight, start.Component);

            // Re-run the winner: the search arrays hold whichever ran last, and the parents have to
            // be the ones being walked back.
            Search(links[startLink].To, goalA, goalB, out reached);
            int first = links[startLink].To;

            // Walk the parents back to `first`, then lay the route out forwards.
            _path.Clear();
            for (int node = reached; node != first; )
            {
                int link = _cameFromLink[node];
                _path.Add(link);
                node = links[link].From;
            }

            _path.Reverse();

            // The remainder of the span the cop is already on, from where it stands to the far end.
            float routeMetres = SampleSpan(startLink, start.S, links[startLink].S1, waypoints);
            foreach (int link in _path)
                routeMetres += SampleSpan(link, links[link].S0, links[link].S1, waypoints);

            waypoints.Add(to);
            return new Result(true, _path.Count, routeMetres, straight, start.Component);
        }

        /// <summary>Whichever direction of a span points the way this car is already going.</summary>
        private int Facing(int link, Vector3 heading)
        {
            var links = _graph.Links;
            int twin = links[link].Twin;
            if (twin < 0) return link;

            var a = _graph.Nodes[links[link].To].Position - _graph.Nodes[links[link].From].Position;
            float along = a.x * heading.x + a.z * heading.z;
            return along >= 0f ? link : twin;
        }

        /// <summary>Standard A*, stopping at either end of the goal span.</summary>
        private bool Search(int start, int goalA, int goalB, out int reached)
        {
            reached = start;
            _visit++;
            _heapCount = 0;

            var nodes = _graph.Nodes;
            var links = _graph.Links;

            _stamp[start] = _visit;
            _gScore[start] = 0f;
            _cameFromLink[start] = -1;
            Push(start, Heuristic(start, goalA, goalB));

            while (_heapCount > 0)
            {
                int current = Pop();
                if (current == goalA || current == goalB)
                {
                    reached = current;
                    return true;
                }

                ref readonly var node = ref nodes[current];
                for (int i = 0; i < node.LinkCount; i++)
                {
                    int linkId = node.LinkStart + i;
                    ref readonly var link = ref links[linkId];

                    float tentative = _gScore[current] + link.Cost;
                    if (_stamp[link.To] == _visit && tentative >= _gScore[link.To]) continue;

                    _stamp[link.To] = _visit;
                    _gScore[link.To] = tentative;
                    _cameFromLink[link.To] = linkId;
                    Push(link.To, tentative + Heuristic(link.To, goalA, goalB));
                }
            }

            return false;
        }

        private float Heuristic(int node, int goalA, int goalB)
        {
            var p = _graph.Nodes[node].Position;
            var a = _graph.Nodes[goalA].Position;
            var b = _graph.Nodes[goalB].Position;
            float da = Flat(p, a);
            float db = Flat(p, b);
            return Mathf.Min(da, db);
        }

        private static float Flat(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Lays lane-centre points along one span and returns the metres covered.
        ///
        /// Through <see cref="RouteGraph.SampleLink"/> rather than the raw polyline, so the route is
        /// the RIGHT-HAND LANE — the same offset, the same side and the same baked road height the
        /// traffic uses. A cop steering down the centreline drives through the oncoming cars.
        /// </summary>
        private float SampleSpan(int link, float s0, float s1, List<Vector3> waypoints)
        {
            var spec = _graph.Links[link];
            float span = Mathf.Abs(s1 - s0);
            if (span < 0.01f) return 0f;

            int steps = Mathf.Max(1, Mathf.CeilToInt(span / WaypointSpacing));
            for (int i = 1; i <= steps; i++)
            {
                float s = Mathf.Lerp(s0, s1, i / (float)steps);
                _graph.SampleLink(spec, s, _laneGap, out var position, out _);
                waypoints.Add(position);
            }

            return span;
        }

        // --- a small binary heap of node ids ---------------------------------------------------

        private void Push(int node, float key)
        {
            int i = ++_heapCount;
            if (i >= _heap.Length) return;

            _heap[i] = node;
            _heapKey[i] = key;

            while (i > 1 && _heapKey[i >> 1] > _heapKey[i])
            {
                Swap(i, i >> 1);
                i >>= 1;
            }
        }

        private int Pop()
        {
            int top = _heap[1];
            _heap[1] = _heap[_heapCount];
            _heapKey[1] = _heapKey[_heapCount--];

            int i = 1;
            while (true)
            {
                int left = i << 1;
                int right = left + 1;
                int smallest = i;

                if (left <= _heapCount && _heapKey[left] < _heapKey[smallest]) smallest = left;
                if (right <= _heapCount && _heapKey[right] < _heapKey[smallest]) smallest = right;
                if (smallest == i) break;

                Swap(i, smallest);
                i = smallest;
            }

            return top;
        }

        private void Swap(int a, int b)
        {
            (_heap[a], _heap[b]) = (_heap[b], _heap[a]);
            (_heapKey[a], _heapKey[b]) = (_heapKey[b], _heapKey[a]);
        }
    }
}
