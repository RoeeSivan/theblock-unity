using UnityEngine;

namespace TheBlock.Police
{
    /// <summary>
    /// Every U19 number, in one place, each with where it came from.
    ///
    /// <b>The config has none of this.</b> <c>theblock-config.json</c> carries no <c>police</c>,
    /// <c>crime</c> or <c>wanted</c> section — the exporter never had one, because in the web build
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
        [Header("Wanted meter — stars, not events")]
        [Tooltip("DESIGN. Three, as the web build and every game this borrows from.")]
        public int MaxStars = 3;

        [Tooltip("DESIGN. One person under the wheels is one star, lit on the frame the blood lands. " +
                 "Just over 1.0 so the star is unambiguous rather than exactly at the threshold.")]
        public float RunOverFirst = 1.05f;

        [Tooltip("DESIGN. Each extra victim in the same physics step. A pavement is worth two stars, " +
                 "not five.")]
        public float RunOverExtra = 0.25f;

        [Tooltip("DESIGN. Cap on one run-over event however many were hit.")]
        public float RunOverCap = 2f;

        [Tooltip("GUESS. The web needed 3 s because its heat was a discrete +1 per event; a meter " +
                 "with a per-event cap needs far less.")]
        public float RunOverCooldown = 0.5f;

        [Header("Crash heat — the fix for 'too sensitive'")]
        [Tooltip("GUESS, to be measured in Play. Heat per m/s of closing speed above the deadzone. " +
                 "6 m/s (a wall at ~30 km/h) -> 0.45, half a star. 12 m/s -> 1.05, one star.")]
        public float CrashPerClosingSpeed = 0.10f;

        [Tooltip("GUESS, to be measured. Closing speed below this is a kerb hop or a car-park nudge.")]
        public float CrashDeadzone = 1.5f;

        [Tooltip("Multiplier for hitting a police car specifically. DESIGN — the web had no such " +
                 "case, and it should cost more than a wall.")]
        public float CrashCopWeight = 1.5f;

        [Tooltip("Cap on one impact, so a rebound chain cannot machine-gun the meter.")]
        public float CrashCap = 1.2f;

        [Tooltip("Seconds before another impact can add heat.")]
        public float CrashCooldown = 1f;

        [Tooltip("Contacts flatter than this are the ground, a kerb or a landing, never a wall. " +
                 "|normal.y| above it is dropped outright.")]
        [Range(0.1f, 0.99f)]
        public float GroundNormalY = 0.7f;

        [Header("Decay — always running")]
        [Tooltip("DERIVED from the web's hard give-up cap. Stars per second with a cop on your " +
                 "bumper: 1 star in ~33 s, 3 in ~100 s, against the web's 45/70/95 s worst case.")]
        public float DecayAlways = 0.030f;

        [Tooltip("DESIGN, softened. The web wanted 8 s of no contact before decay began; continuous " +
                 "decay feels longer than a stepped one, so 6.")]
        public float ContactGrace = 6f;

        [Tooltip("Seconds over which hidden decay ramps in, so the meter visibly starts draining.")]
        public float HiddenRamp = 2f;

        [Tooltip("DERIVED. Fully hidden: 1 star in ~4 s, 3 in ~12 s — 8 s and 18 s end to end, " +
                 "against the web's 12 s and 23 s.")]
        public float DecayHidden = 0.220f;

        [Tooltip("No decay at all for this long after a crime, so a star does not bleed as it lights.")]
        public float CrimeFreeze = 1.5f;

        [Header("Contact and sight")]
        [Tooltip("DESIGN. A cop inside this with a clear line is 'in contact', which stops decay.")]
        public float SightRadius = 70f;

        [Tooltip("DESIGN. Eye height on the cop and chest height on the target: a floor-to-floor ray " +
                 "skims kerbs and false-blocks.")]
        public float LosEyeHeight = 1.2f;

        public float LosTargetHeight = 1f;

        [Header("Pursuit driving")]
        [Tooltip("DESIGN, retuned. 20.5 against the player's 20 m/s — a 2.5% edge, not the web's " +
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

        [Tooltip("Metres of route ahead scanned for the tightest corner.")]
        public float CornerScan = 25f;

        [Tooltip("Pure-pursuit lookahead: base + per m/s of speed, clamped. GUESS — tune the per-speed " +
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

        [Tooltip("DESIGN. How far to the side of you the cop aims on that final run — the pull-in.")]
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

        [Tooltip("UNUSED since the cars were made to always deploy from the station. Kept because " +
                 "the web's 120 m is the number to compare against if a cross-city response ever " +
                 "feels too slow to be fun.")]
        public float StationDeployRange = 120f;

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

        [Tooltip("UNUSED. Cops now start at the station and are often legitimately hundreds of " +
                 "metres away while driving toward you; retiring one for being far retired it for " +
                 "doing its job. They go home when the stars do.")]
        public float RetireDistance = 220f;

        [Tooltip("Seconds before a wrecked or flipped cop is replaced. Ramming one into the sea buys " +
                 "you this much, not a permanent kill.")]
        public float ReplaceDelay = 6f;

        [Header("Routing")]
        [Tooltip("Seconds between A* replans, staggered across cops so three never land in one frame.")]
        public float ReplanInterval = 1f;

        [Tooltip("Metres off its own route before a cop replans immediately.")]
        public float CorridorWidth = 12f;

        [Tooltip("Beyond this from any street, the cop drives at the nearest route point rather than " +
                 "at the target — the only other place straight-line survives.")]
        public float OffGraphDistance = 15f;

        [Header("Bust")]
        [Tooltip("DESIGN. Seconds the overlay holds.")]
        public float BustHold = 2.2f;

        [Tooltip("DESIGN. There is no wallet in this port yet, so it is TALLIED and shown, and U28 " +
                 "bills it. The loss you actually feel is the freeze and where you were.")]
        public int BustFine = 100;
    }
}
