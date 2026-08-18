using UnityEngine;

namespace TheBlock.World
{
    /// <summary>
    /// Where פלאפל הפעמונים is, and what a round of deliveries for it is worth.
    ///
    /// The second place in the port the web build never had (after the auto shop), so - like
    /// <see cref="AutoShopSpec"/> - its numbers are authored HERE, in Unity space, and never go
    /// through <c>Convert.Pos</c>: they were never right-handed to begin with. The builder places
    /// the model with them, the map pins it with them, and <c>FalafelRun</c> measures against them,
    /// so the three cannot disagree.
    ///
    /// <b>Why it exists at all.</b> Every money event in the campaign pays exactly once
    /// (<see cref="Game.Payouts"/>), and everything that spends - power-ups, a coat of paint, fuel -
    /// is repeatable. Clear the four missions and the wallet is a closed tap with three shops behind
    /// it. This is the faucet: a street job that pays every time you finish it.
    /// </summary>
    public static class FalafelSpec
    {
        // --- the place ------------------------------------------------------------------------------

        /// <summary>
        /// The east pavement of the downtown avenue, directly across the road from the pizzeria at
        /// (−20, 0, −100) - the user's placement, and it is measured rather than eyeballed: the
        /// stand sits in the 20 m gap between <c>Bench_075</c> (z −106.93) and <c>Bench_076</c>
        /// (z −86.93), 6.4 m clear of each.
        /// </summary>
        public static readonly Vector3 Position = new(20.25f, 0f, -96.9f);

        /// <summary>
        /// The model is authored front-on-+Z and side-on-+X (Blender −Y / −X; glTFast negates only
        /// X), so −90° turns the sign facade west onto the avenue - facing the pizzeria - and leaves
        /// the second glazed facade looking north up the pavement.
        /// </summary>
        public const float YawDeg = -90f;

        /// <summary>The scene object the builder creates.</summary>
        public const string ObjectName = "Place_FalafelStand";

        /// <summary>
        /// Map label and pin. <b>Transliterated on purpose</b> - the sign in the world is Hebrew,
        /// but the HUD's font stack is Latin plus a colour-emoji fallback, and Hebrew there draws as
        /// boxes. The shop reads as itself from the street; the map only has to name it.
        /// </summary>
        public const string MapLabel = "Falafel HaPaamonim";
        public const string MapIcon = "🧆";

        /// <summary>The model file, and the marker nodes authored inside it.</summary>
        public const string ModelUrl = "/models/falafel-stand.glb";
        public const string VendorNode = "fh_vendor";
        public const string CounterNode = "fh_counter";
        public const string TalkNode = "fh_talk";
        public const string PinNode = "fh_pin";

        /// <summary>The vendor: a generic crowd man. Every named face in the roster already has a job.</summary>
        public const string VendorPrefab = "Assets/Prefabs/Npc/Ped_02m_01.prefab";

        /// <summary>
        /// Metres from <c>fh_talk</c>, out under the canopy, within which the stand nudges you to
        /// <b>go in</b>.
        ///
        /// <b>This is a hint, not the trigger any more.</b> U37 shipped taking the order from the
        /// saddle at this radius; the user's call after playing it is that the shift starts
        /// <b>inside, at the counter</b> - see <see cref="CounterRadius"/>. Without a nudge out here
        /// a rider pulling up sees nothing at all and has no reason to dismount, so the outside
        /// prompt stays and only its wording changes.
        ///
        /// <b>Measured, not guessed.</b> Sampling the ground west from the talk point puts the
        /// pavement at y 0.16 for 2 m and the carriageway at 0.00 from 3 m, so the <b>kerb is 2.5 m
        /// out</b>. Six reaches 3.5 m past it, which is a bike stopped in the near lane.
        /// </summary>
        public const float TalkRadius = 6f;

        /// <summary>
        /// Metres from <c>fh_counter</c> - the spot on the customer's side of the counter - that
        /// count as being served. Both the prompt and the action read this same predicate, so a
        /// prompt offering T can never appear where T does nothing.
        /// </summary>
        public const float CounterRadius = 2.2f;

        /// <summary>
        /// The shop floor, in the model's own local space, as a half-open box the player has to be
        /// standing in to count as inside.
        ///
        /// <b>A radius alone is not enough and the geometry says why.</b> The whole front of the
        /// stand is an open bay, so a player on the pavement can be within
        /// <see cref="CounterRadius"/> of the counter while still standing in the street - which
        /// would make "he needs to be inside" a lie the first time someone leaned on the glass.
        /// The box is the same answer <see cref="SevenEleven"/> reached for its sales floor.
        ///
        /// Local, because the stand is yawed: <c>transform.InverseTransformPoint</c> puts the player
        /// in these axes and nothing has to be re-derived if the stand is ever moved or turned.
        /// The numbers are the walls as modelled - Blender x ±3.45 and y −2.6..2.45 - carried through
        /// the importer's axis change (Unity local X = −Blender x, Z = −Blender y).
        /// </summary>
        public static readonly Bounds Interior = new(
            new Vector3(0f, 1.25f, 0.075f),      // centre
            new Vector3(7.0f, 3.5f, 5.05f));     // size: x ±3.5, z −2.45..2.6

        /// <summary>Height above the shop floor that still counts - a walking player, never a roof.</summary>
        public const float InteriorHeadroom = 3f;

        // --- a round --------------------------------------------------------------------------------

        /// <summary>
        /// Drops on the first round, and one more for every round finished back to back.
        /// </summary>
        public const int BaseDrops = 3;
        public const int MaxDrops = 6;

        /// <summary>Seconds on the first round's clock, and the step and ceiling as it escalates.</summary>
        public const float BaseSeconds = 150f;
        public const float SecondsStep = 20f;
        public const float MaxSeconds = 210f;

        /// <summary>
        /// Dollars for the first round, the step, and the ceiling.
        ///
        /// <b>Scaled against the campaign, not invented.</b> Mission 1 pays $80 once for five drops
        /// in four minutes; a first falafel round is three drops in two and a half and pays $40, so
        /// the story is always the better hour's work and this is the floor you can always come back
        /// to. At the cap a round is a power-up and a bit, which is the rate that makes the 7-Eleven
        /// and the paint shop reachable without making them free.
        /// </summary>
        public const int BasePay = 40;
        public const int PayStep = 15;
        public const int MaxPay = 100;

        /// <summary>
        /// The clock turns red here, matching <c>missionConfig.lowTimeSec</c>.
        /// </summary>
        public const float LowTimeSec = 30f;

        /// <summary>
        /// The beacon over a waiting customer: amber, the colour of the chips in the vitrine, so a
        /// falafel drop is never confused with the delivery run's green at a glance.
        /// </summary>
        public const int BeaconColor = 0xE0A93A;

        /// <summary>
        /// How many of the fifteen authored spots are drawn from when picking a round's drops.
        /// The pool is sorted by distance from the stand and cut here, so a round reads as this
        /// shop's own quarter rather than as a tour of the city - but it is wider than
        /// <see cref="MaxDrops"/>, or every round at the cap would visit the same six doors.
        /// </summary>
        public const int NearestPool = 10;

        /// <summary>Level n's drops, seconds and pay. Level 1 is a first round or one after a loss.</summary>
        public static int DropsFor(int level) => Mathf.Min(BaseDrops + level - 1, MaxDrops);

        public static float SecondsFor(int level) =>
            Mathf.Min(BaseSeconds + (level - 1) * SecondsStep, MaxSeconds);

        public static int PayFor(int level) => Mathf.Min(BasePay + (level - 1) * PayStep, MaxPay);
    }
}
