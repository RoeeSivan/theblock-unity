using System.Collections.Generic;
using TheBlock.Traffic;
using TheBlock.Vehicles;
using UnityEngine;

namespace TheBlock.Police
{
    /// <summary>
    /// One police car's state — the analogue of <c>TrafficCar</c>, and like it, a bag of state with
    /// no opinions. <see cref="PoliceSystem"/> decides; <see cref="CopDriver"/> drives.
    ///
    /// It also carries this cop's own <see cref="RoutePlanner"/>. One planner per car rather than one
    /// shared: the planner's working arrays are its scratch space, and sharing them would mean either
    /// clearing them per call or three cops overwriting each other's search.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    [RequireComponent(typeof(CopDriver))]
    public class CopCar : MonoBehaviour
    {
        public enum Mode
        {
            /// <summary>Parked in a station bay, engine off. The pool's resting state.</summary>
            Idle,

            /// <summary>Out and looking for you.</summary>
            Chasing,

            /// <summary>Stopped beside you, running the arrest clock.</summary>
            Arresting,

            /// <summary>On its roof, in the sea, or hopelessly wedged. Waiting to be replaced.</summary>
            Wrecked,
        }

        public CarController Car { get; private set; }
        public CopDriver Driver { get; private set; }
        public RoutePlanner Planner { get; private set; }

        public Mode State { get; set; } = Mode.Idle;

        /// <summary>Which station bay this car belongs to, or −1 for one that spawned in the field.</summary>
        public int Bay { get; set; } = -1;

        /// <summary>
        /// The waypoints, which live on the DRIVER and nowhere else.
        ///
        /// They were briefly held here as well, and the two lists cost an evening: the planner filled
        /// this one, the driver steered by that one, so every cop had a perfectly good 49-point route
        /// and an empty cursor — which reads as "aim straight at the player" and drove all three into
        /// the car-park wall within seconds. One owner.
        /// </summary>
        public List<Vector3> Route => Driver != null ? Driver.Route : null;

        /// <summary>Seconds until this cop replans. Staggered at spawn so three never land together.</summary>
        public float ReplanIn { get; set; }

        /// <summary>Seconds this cop has been within arrest range, bleeding when you leave.</summary>
        public float ArrestHold { get; set; }

        /// <summary>Time it went live, for the spawn grace.</summary>
        public float SpawnedAt { get; set; }

        /// <summary>Time it wrecked, for the replacement delay.</summary>
        public float WreckedAt { get; set; }

        /// <summary>Clear line to the target this step.</summary>
        public bool HasLos { get; set; }

        /// <summary>Where the target was last seen. What it drives at when it cannot see you.</summary>
        public Vector3 LastKnown { get; set; }

        /// <summary>Last A* result, for the probe's route table.</summary>
        public RoutePlanner.Result LastPlan { get; set; }

        /// <summary>Fraction of samples this cop spent within a lane of a street. The road-route proof.</summary>
        public int OnGraphSamples { get; set; }

        public int TotalSamples { get; set; }

        public float WorstOffRoute { get; set; }

        public void Configure(PoliceTuning tuning, RouteGraph graph, float laneGap)
        {
            Car = GetComponent<CarController>();
            Driver = GetComponent<CopDriver>();
            Driver.Configure(tuning);
            Planner = new RoutePlanner(graph, laneGap);
        }

        /// <summary>Upside down, underwater, or otherwise not coming back.</summary>
        public bool LooksWrecked(float belowY) =>
            Vector3.Dot(transform.up, Vector3.up) < 0.3f || transform.position.y < belowY;
    }
}
