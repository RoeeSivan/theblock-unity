using System.Collections.Generic;
using TheBlock.Traffic;
using TheBlock.Vehicles;
using UnityEngine;

namespace TheBlock.Police
{
    /// <summary>
    /// One police car's state - the analogue of <c>TrafficCar</c>, and like it, a bag of state with
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

            /// <summary>
            /// Star lost, driving itself back to its bay on the same planner it chased you with.
            ///
            /// It exists because the alternative is worse than it sounds: a cruiser twenty metres
            /// off your bumper teleporting home the instant a star bleeds reads as a fresh bug, not
            /// as a stand-down. A returning cop is also still a car in the world - it can be rammed,
            /// and a new crime turns it round rather than making it finish the trip.
            /// </summary>
            Returning,

            /// <summary>On its roof, in the sea, or hopelessly wedged. Waiting to be replaced.</summary>
            Wrecked,
        }

        public CarController Car { get; private set; }
        public CopDriver Driver { get; private set; }
        public RoutePlanner Planner { get; private set; }

        /// <summary>
        /// The officer sitting in it, or null.
        ///
        /// Null is a supported state, not a broken one: without a built officer prefab every U19
        /// behaviour stands exactly as it was and the cruiser makes the arrest itself.
        /// </summary>
        public CopOfficer Officer { get; set; }

        public Mode State { get; set; } = Mode.Idle;

        /// <summary>Which station bay this car belongs to, or −1 for one that spawned in the field.</summary>
        public int Bay { get; set; } = -1;

        /// <summary>
        /// The waypoints, which live on the DRIVER and nowhere else.
        ///
        /// They were briefly held here as well, and the two lists cost an evening: the planner filled
        /// this one, the driver steered by that one, so every cop had a perfectly good 49-point route
        /// and an empty cursor - which reads as "aim straight at the player" and drove all three into
        /// the car-park wall within seconds. One owner.
        /// </summary>
        public List<Vector3> Route => Driver != null ? Driver.Route : null;

        /// <summary>Seconds until this cop replans. Staggered at spawn so three never land together.</summary>
        public float ReplanIn { get; set; }

        /// <summary>Seconds this cop has been within arrest range, bleeding when you leave.</summary>
        public float ArrestHold { get; set; }

        /// <summary>
        /// Seconds this cop has been within arrest range at ANY speed, bleeding when you leave.
        ///
        /// Kept apart from <see cref="ArrestHold"/> rather than folded into it because the two count
        /// different things: that one is the stationary arrest and clears the moment you are over
        /// <c>ArrestMaxSpeed</c>, this one is the pull-over and does not care how fast you are going.
        /// One meter serving both would reset the takedown every time you crossed 6 m/s, which on a
        /// chase is constantly.
        /// </summary>
        public float PulloverHold { get; set; }

        /// <summary>Time it went live, for the spawn grace.</summary>
        public float SpawnedAt { get; set; }

        /// <summary>Time it wrecked, for the replacement delay.</summary>
        public float WreckedAt { get; set; }

        /// <summary>
        /// Time it was last re-dispatched into the field ring, for the cooldown; also the deploy
        /// time, so a fresh cop is not moved again before it has had a chance to arrive.
        /// </summary>
        public float RelocatedAt { get; set; }

        /// <summary>
        /// Time this cop first went beyond <c>RelocateBeyond</c> out of sight, or 0 while it is
        /// within range or can see you. The distance trigger of the re-dispatch.
        /// </summary>
        public float FarSince { get; set; }

        /// <summary>How many times this cop has been re-dispatched this deployment. A probe number.</summary>
        public int Relocations { get; set; }

        /// <summary>
        /// How many times this cop has hit the unwedge limit since it was last placed. The first
        /// strike in view of you is answered with a fresh route; the second is a cop that has been
        /// visibly stuck for twenty seconds, and it is re-dispatched even though you can see it.
        /// </summary>
        public int WedgeStrikes { get; set; }

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
