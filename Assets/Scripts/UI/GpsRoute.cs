using System.Collections.Generic;
using TheBlock.Game;
using TheBlock.Police;
using TheBlock.Traffic;
using UnityEngine;

namespace TheBlock.UI
{
    /// <summary>
    /// The route to the objective, along the roads, for the minimap and the full map.
    ///
    /// <b>This is the half of U35c that the web build could not have shipped at all</b>, and not
    /// for want of trying: its traffic network was five disconnected components, so "drive from
    /// here to there" had no answer for most pairs of points and the map drew a straight line to
    /// the pin instead. The port's baked <see cref="RouteGraph"/> is two components with 97.9% of
    /// the city in the larger one, and the police have been A*-ing across it since U19. Drawing the
    /// player a route is that same search, run once for the map.
    ///
    /// <b>A plain C# class, not a MonoBehaviour.</b> <see cref="GameMap"/> owns one and ticks it.
    /// A component would need a GameObject, and the only sensible host is the HUD object that
    /// <c>HudBuilder</c> destroys and rebuilds - which is how U26's menus were lost once already.
    /// </summary>
    public class GpsRoute
    {
        /// <summary>Seconds between considering a replan. The cops' own repath cadence.</summary>
        private const float TickInterval = 0.25f;

        /// <summary>Metres off the drawn line before it is replanned from where you actually are.</summary>
        private const float CorridorMetres = 15f;

        /// <summary>Metres the objective may drift before the route is stale.</summary>
        private const float TargetDrift = 5f;

        private readonly RoutePlanner _planner;
        private readonly List<Vector3> _points = new();

        private float _sinceTick;
        private Vector3 _plannedTo;
        private bool _hasRoute;

        /// <summary>The polyline, in world space. Empty when there is nothing to draw.</summary>
        public IReadOnlyList<Vector3> Points => _points;

        public GpsRoute(RouteGraph graph, float laneGap)
        {
            // Its OWN planner. The planner's scratch arrays are its search state, and CopCar's
            // comment spells out what sharing them costs: three cops overwriting each other's
            // search. A fourth consumer would be the fourth to do it.
            if (graph != null) _planner = new RoutePlanner(graph, laneGap);
        }

        /// <summary>Builds one against whatever the police are already routing on, or null.</summary>
        public static GpsRoute Create()
        {
            var police = Object.FindAnyObjectByType<PoliceSystem>();
            if (police == null || police.Graph == null) return null;

            float laneGap = 3.4f;
            var snapshot = Core.TheBlockConfig.Load();
            if (snapshot?.Config?.Traffic != null) laneGap = snapshot.Config.Traffic.LaneGap;

            return new GpsRoute(police.Graph, laneGap);
        }

        public void Tick(Vector3 from, Vector3 heading, float dt)
        {
            // Pull-only, and read FIRST. Turning the setting off costs an A* search rather than
            // merely hiding one - the Ragdolls pattern, not the Radar one, so there is exactly one
            // reader of the preference and nothing to drift out of step with it.
            if (_planner == null || !Progress.GpsRouteOn)
            {
                Clear();
                return;
            }

            _sinceTick += dt;
            if (_sinceTick < TickInterval) return;
            _sinceTick = 0f;

            var guide = MapRegistry.NearestGuide(from);
            if (guide == null)
            {
                Clear();
                return;
            }

            var to = guide.At;
            if (_hasRoute && Flat(to, _plannedTo) < TargetDrift && OffRoute(from) < CorridorMetres)
                return;

            var result = _planner.Plan(from, heading, to, _points);

            // NOT FOUND IS A REAL ANSWER, and it has to draw nothing. The jetski mission's gates sit
            // on open water with no street inside the planner's 120 m snap radius, and a straight
            // line to them across the sea would be worse than no line: it would claim a road.
            if (!result.Found)
            {
                Clear();
                return;
            }

            _plannedTo = to;
            _hasRoute = true;
        }

        private void Clear()
        {
            _points.Clear();
            _hasRoute = false;
        }

        /// <summary>Shortest distance from the player to the drawn polyline, flat.</summary>
        private float OffRoute(Vector3 from)
        {
            if (_points.Count < 2) return float.MaxValue;

            float best = float.MaxValue;
            for (int i = 0; i < _points.Count - 1; i++)
            {
                float d = PointToSegment(from, _points[i], _points[i + 1]);
                if (d < best) best = d;
            }

            return best;
        }

        private static float PointToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            p.y = a.y = b.y = 0f;
            var ab = b - a;
            float lengthSqr = ab.sqrMagnitude;
            if (lengthSqr < 1e-6f) return Vector3.Distance(p, a);

            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / lengthSqr);
            return Vector3.Distance(p, a + ab * t);
        }

        private static float Flat(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
