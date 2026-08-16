using UnityEngine;

namespace TheBlock.Police
{
    /// <summary>
    /// Every U19 number, in one place, each with where it came from.
    ///
    /// <b>The config has none of this.</b> <c>theblock-config.json</c> carries no <c>police</c>,
    /// <c>crime</c> or <c>wanted</c> section - the exporter never had one, because in the web build
    /// these live in <c>src/world/police.config.ts</c>, which is source and not data. So they are
    /// stated port-side, and the honest thing is to say per number whether it is DESIGN carried over
    /// (gameplay, still true), DERIVED (computed from something measured here) or a GUESS to tune.
    ///
    /// <b>Nothing physical was ported.</b> Port rule 2: Rapier is not PhysX. The web cop's speeds
    /// were applied directly to a kinematic body with no acceleration at all; this one has to ask an
    /// engine for them. Its top speed is therefore 2.5% over the player's rather than 15%, which is
    /// only sane because it also has to accelerate like the player's car.
    /// </summary>
    [System.Serializable]
    public class PoliceTuning
    {
        [Header("Wanted level - one crime, one star, one car")]
        [Tooltip("DESIGN. Three, as the web build and every game this borrows from.")]
        public int MaxStars = 3;

        [Tooltip("DESIGN. crime.pedCooldown. One star per run-over event however many go down at " +
                 "once, and no faster than this. It was 0.5 while heat was a meter with a per-event " +
                 "cap; under a counter that made one pass through a crowd worth three stars.")]
        public float RunOverCooldown = 3f;

        [Header("Crash - hard hits only, the fix for 'too sensitive'")]
        [Tooltip("GUESS, to be measured in Play. Closing speed at or above which an at-fault impact " +
                 "is a crime: a wall at about 22 km/h. Below it, nothing - which is what keeps a " +
                 "scrape free now that a crash is worth a whole star or nothing at all.")]
        public float CrashCrimeSpeed = 6f;

        [Tooltip("The same line for an impact against ANOTHER VEHICLE - a traffic car, a parked " +
                 "filler, a cruiser - and deliberately much lower than the wall figure. Ramming a " +
                 "car is a hit-and-run with a victim and a witness; scraping a bollard is not, and " +
                 "the web build could not tell the two apart at all. 2.5 m/s is 9 km/h: a real " +
                 "shunt, not a parking nudge.")]
        public float VehicleCrashCrimeSpeed = 2.5f;

        [Tooltip("DESIGN. crime.crashCooldown. Seconds before another impact can be a crime, so " +
                 "grinding a wall is not a star every frame.")]
        public float CrashCooldown = 3f;

        // GroundNormalY and CrashDeadzone used to sit here and were always dead: CrashSensor has its
        // own groundNormalY and minClosingSpeed and never read these. What reaches CrimeWatch has
        // already passed both, so the only crash number this file still owns is CrashCrimeSpeed.

        [Header("Escape - how a pursuit ends")]
        [Tooltip("DESIGN. escape.breakContact. Seconds of unbroken no-contact before stars start " +
                 "bleeding at all.")]
        public float BreakContact = 8f;

        [Tooltip("DESIGN. escape.shedStep, indexed by the star being shed, so the last one is the " +
                 "cheapest. From the contact break: 1* = 12 s, 2* = 17 s, 3* = 23 s.")]
        public float[] ShedStep = { 4f, 5f, 6f };

        [Tooltip("DESIGN. escape.giveUpAt. Seconds of ENGAGED pursuit since your last crime before a " +
                 "star drops anyway, whether or not they can still see you - what makes 'the cops " +
                 "eventually stop' true even if you never lose them. It does NOT run while they are " +
                 "still driving in: measured, a cop 127 m out lost the whole pursuit to this cap " +
                 "before it ever arrived. The inbound phase is bounded by InboundGrace instead.")]
        public float[] GiveUpAt = { 45f, 25f, 25f };

        [Tooltip("DERIVED, and raised from the web's 30. Heat cannot bleed until a cop has first " +
                 "reached SightRadius - that latch is what makes a station response possible at all. " +
                 "The web's cops appeared 70 m away; ours drive up to ~900 m of a 12.5 km network at " +
                 "an average well under top speed, so a 30 s bound would release the latch mid-drive " +
                 "and re-create the exact bug this replaced. It is a safety valve for an unreachable " +
                 "player, not a pacing dial.")]
        public float InboundGrace = 60f;

        [Header("Contact and sight")]
        [Tooltip("DESIGN. A cop inside this with a clear line is 'in contact', which stops decay.")]
        public float SightRadius = 70f;

        [Tooltip("DESIGN. Eye height on the cop and chest height on the target: a floor-to-floor ray " +
                 "skims kerbs and false-blocks.")]
        public float LosEyeHeight = 1.2f;

        public float LosTargetHeight = 1f;

        [Header("Pursuit driving")]
        [Tooltip("DESIGN, retuned. 20.5 against the player's 20 m/s - a 2.5% edge, not the web's " +
                 "15%, because this cop also has to accelerate for it.")]
        public float MaxSpeed = 20.5f;

        [Tooltip("Speed at the near end of the rubber band, m/s.")]
        public float MinSpeed = 8f;

        [Tooltip("DESIGN. Rubber band range: full pressure beyond BandFar, easing off inside BandNear.")]
        public float BandNear = 12f;

        public float BandFar = 75f;

        [Tooltip("GUESS. Lateral acceleration a cop will ask of its tyres, m/s^2. THE dial for " +
                 "whether corners are a real escape.")]
        public float LateralGrip = 6.5f;

        [Header("The run to the scene - urgency without an unbeatable chase")]
        [Tooltip("Top speed while RESPONDING: beyond BandFar with no line of sight, which is the " +
                 "drive in from the station and nothing else. Above the chase's MaxSpeed on purpose " +
                 "- a car with its lights on covering ground is not the same thing as a car on your " +
                 "bumper, and only the second one has to stay beatable.")]
        public float ResponseSpeed = 29f;

        [Tooltip("Lateral grip while responding, m/s^2. The measured drive was CORNER-limited, not " +
                 "top-speed limited - 13.7 m/s against a 20.5 ceiling - so this is the number that " +
                 "actually shortens the wait. It does not apply once a cop can see you, so bends are " +
                 "still where a pursuit is lost.")]
        public float ResponseGrip = 11f;

        [Tooltip("Seconds a RESPONDING cop tolerates being stopped while asking to move before it " +
                 "pulls out to pass. MEASURED: a queue at a red light cost one cruiser 12 s in a " +
                 "single junction, and no amount of yielding fixes that - stationary traffic has " +
                 "nowhere to go, so the cop is what has to move.")]
        public float OvertakeAfter = 1.5f;

        [Tooltip("Metres to the side the aim point swings while passing a queue - into the oncoming " +
                 "side, which is what a real car with its lights on does.")]
        public float OvertakeShift = 3.5f;

        [Tooltip("Seconds one pull-out lasts before the cop tucks back into its lane and re-checks.")]
        public float OvertakeTime = 3f;

        [Tooltip("Metres of route ahead scanned for the tightest corner.")]
        public float CornerScan = 25f;

        [Tooltip("Pure-pursuit lookahead: base + per m/s of speed, clamped. GUESS - tune the per-speed " +
                 "term first if it weaves.")]
        public float LookaheadBase = 5f;

        public float LookaheadPerSpeed = 0.6f;
        public float LookaheadMin = 6f;
        public float LookaheadMax = 22f;

        [Tooltip("A route point closer than this is behind us. Half a car length.")]
        public float WaypointReach = 3f;

        [Tooltip("MEASURED off the built prefab: the cruiser's axles are ±1.514 m, so 3.03 m.")]
        public float Wheelbase = 3.03f;

        [Tooltip("The car's own steering lock, degrees. config.vehicle.maxWheelAngle.")]
        public float MaxSteerDegrees = 34.4f;

        [Header("Arrival and arrest")]
        [Tooltip("DESIGN. Where the route is abandoned for a straight run at your near flank, with " +
                 "hysteresis so it cannot chatter at the boundary.")]
        public float FinalApproachEnter = 40f;

        public float FinalApproachExit = 48f;

        [Tooltip("DESIGN. How far to the side of you the cop aims on that final run - the pull-in.")]
        public float SideGap = 3f;

        [Tooltip("Brake to ArriveSpeed inside this.")]
        public float ArriveDistance = 8f;

        public float ArriveSpeed = 3f;

        [Tooltip("DESIGN. XZ metres for the arrest.")]
        public float ArrestRadius = 4f;

        [Tooltip("DESIGN. Seconds held inside that radius. Bleeds at twice the rate when you leave.")]
        public float ArrestHold = 1.5f;

        [Tooltip("PORT ADDITION. The web's cop stopped to arrest, implicitly; a physics cop can sit " +
                 "on your bumper at 20 m/s, and being arrested at speed reads as a bug. Both cars " +
                 "must be under this, m/s.")]
        public float ArrestMaxSpeed = 6f;

        [Tooltip("Seconds a freshly spawned cop cannot arrest, so a spawn-on-top is not an instant bust.")]
        public float SpawnGrace = 2f;

        [Header("The officer on foot - U19e")]
        [Tooltip("PORT ADDITION. Off, the cruiser arrests you itself at ArrestRadius, which is what " +
                 "U19 shipped. On, a player ON FOOT is arrested by the officer who gets out of it; " +
                 "a player in a vehicle is arrested by the car either way, because nobody on legs " +
                 "catches a car.")]
        public bool OfficerChase = true;

        [Tooltip("DESIGN. Metres at which the cruiser stops and she gets out. Wider than the car's " +
                 "own ArrestRadius on purpose: the last stretch is supposed to be the chase, not a " +
                 "formality after one.")]
        public float OfficerDeployRadius = 18f;

        [Tooltip("DESIGN. Her speed, m/s. Between the player's jog (4.5) and sprint (7), so a fresh " +
                 "sprint escapes and an exhausted one does not.")]
        public float OfficerSpeed = 6.2f;

        [Tooltip("DESIGN. Metres she stops SHORT of the player. A NavMeshAgent has no idea the " +
                 "player is standing there - a CharacterController neither agents nor carves - so " +
                 "without this her destination IS the player and she walks into and through them. " +
                 "Must stay well inside OfficerGrabRadius or she stands outside her own reach and " +
                 "the arrest never fires.")]
        public float OfficerStandoff = 1.1f;

        [Tooltip("DESIGN. Metres for the grab - arm's length, not a car's length.")]
        public float OfficerGrabRadius = 1.6f;

        [Tooltip("DESIGN. Seconds held inside that radius. Short: she has hold of you.")]
        public float OfficerGrabHold = 0.6f;

        [Tooltip("DESIGN. Metres of daylight at which she gives up, walks back and gets in. The " +
                 "cruiser resumes the chase from there - the pursuit is not over, only her run is.")]
        public float OfficerGiveUp = 60f;

        [Header("Anti-stuck")]
        [Tooltip("Seconds of travel measured before deciding a cop is wedged.")]
        public float StuckWindow = 2f;

        [Tooltip("Metres it must cover in that window while it is asking to move.")]
        public float StuckTravel = 1.5f;

        [Tooltip("Seconds of mirrored reverse.")]
        public float UnwedgeTime = 1.2f;

        [Tooltip("Three unwedges inside this window and the cop is retired and replaced elsewhere.")]
        public float UnwedgeWindow = 12f;

        public int UnwedgeLimit = 3;

        [Header("Spawning")]
        [Tooltip("DESIGN. One cop per star.")]
        public int MaxCops = 3;

        [Tooltip("Seconds between spawns, so 0 -> 3 stars never lands three cars in one frame. " +
                 "U16 and U17 both measured that the burst is the cost, not the population.")]
        public float SpawnInterval = 1f;

        [Tooltip("Field spawn ring, metres. Only reachable by a cop with no bay of its own.")]
        public float FieldSpawnMin = 60f;

        public float FieldSpawnMax = 110f;

        [Tooltip("Candidates sampled per field spawn.")]
        public int FieldSpawnTries = 24;

        [Tooltip("Clear space required around a field spawn, metres.")]
        public float FieldSpawnClearance = 12f;

        [Tooltip("MEASURED THE HARD WAY. Two cops spawned 5 m apart drive the same route to the same " +
                 "target, shove each other, and both hit the unwedge limit within seconds. They need " +
                 "real distance between them, not just clearance from the traffic.")]
        public float CopSeparation = 30f;

        [Tooltip("Seconds before a wrecked or flipped cop is replaced. Ramming one into the sea buys " +
                 "you this much, not a permanent kill.")]
        public float ReplaceDelay = 6f;

        [Tooltip("Metres from its bay at which a cop driving home is parked and goes Idle. A cop " +
                 "that loses its star drives back rather than vanishing in front of you.")]
        public float BayArrive = 6f;

        [Header("Routing")]
        [Tooltip("Seconds between A* replans, staggered across cops so three never land in one frame.")]
        public float ReplanInterval = 1f;

        [Tooltip("Metres off its own route before a cop replans immediately.")]
        public float CorridorWidth = 12f;

        [Header("Bust")]
        [Tooltip("DESIGN. Seconds the overlay holds.")]
        public float BustHold = 2.2f;

        [Tooltip("DESIGN. There is no wallet in this port yet, so it is TALLIED and shown, and U28 " +
                 "bills it. The loss you actually feel is the freeze and where you were.")]
        public int BustFine = 100;
    }
}
