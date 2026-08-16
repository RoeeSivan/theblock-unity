using System.Collections.Generic;
using Newtonsoft.Json;

namespace TheBlock.Core
{
    /// <summary>
    /// U28's half of the config: the 7-Eleven's own geometry, and the power-up catalogue.
    ///
    /// Split out of <c>TheBlockConfig.cs</c> for the same reason Tier 5's mission specs were - the
    /// store has nothing to say about the world, and the main file is already the longest in the
    /// project.
    ///
    /// <b>The numbers here are a cross-check, not the source.</b> Everything in
    /// <see cref="StoreSpec"/> that names a POINT - the entrance, the cashier's stand, the ten
    /// power-up slots - is also an empty node inside <c>seven-eleven-lot.glb</c>, carrying the same
    /// value, and glTFast has already converted those nodes into Unity's frame on import. Reading
    /// the node is therefore exact and free, while converting the config's copy means picking the
    /// right one of three handedness rules for a MODEL-LOCAL coordinate - the trap variant, not the
    /// famous one. <see cref="World.SevenEleven"/> reads the nodes; the builder asserts the two
    /// agree and reports the delta. What is left here is what has no node: distances, durations, and
    /// the floor rectangle.
    /// </summary>
    public static partial class TheBlockConfig
    {
        /// <summary>
        /// <c>config.sevenEleven</c> - a <see cref="PlaceSpec"/> plus the sales floor.
        ///
        /// Subclasses rather than replaces, so <c>WorldBuilder.BuildPlace</c> keeps placing it as
        /// the ordinary static prop it also is.
        /// </summary>
        public class StoreSpec : PlaceSpec
        {
            /// <summary>The bi-parting automatic doors.</summary>
            public StoreDoorSpec Door;

            /// <summary>Doorway centre, MODEL-LOCAL. Node <c>se_door_trigger</c>.</summary>
            public Vec3 Entrance;

            /// <summary>
            /// The sales floor, MODEL-LOCAL, as min/max in X and Z.
            ///
            /// The one part of this block with no node to read, so it is the one part that must be
            /// converted by hand - and a rectangle is where that bites: <see cref="Convert"/>'s
            /// model-local rule negates Z, which turns a min into a max. Re-sort after converting.
            /// </summary>
            public StoreFloorSpec Floor;

            /// <summary>Where the shopkeeper stands, MODEL-LOCAL. Node <c>se_cashier_stand</c>.</summary>
            public Vec3 Cashier;

            /// <summary>
            /// The ten fixtures a power-up can be parked on, MODEL-LOCAL, keyed by fixture name.
            /// Nodes <c>pu_slot_*</c>. Nothing consumes these - the web build's own comment says the
            /// same - but they are the only record of where the shelves are.
            /// </summary>
            public Dictionary<string, Vec3> Slots;

            /// <summary>The shopkeeper herself.</summary>
            public StoreClerkSpec Clerk;
        }

        /// <summary>
        /// The sliding leaves. <see cref="Slide"/> is a DISPLACEMENT in the model's own frame, and
        /// its sign is not portable: glTFast negates X on import, so the leaf named
        /// <c>SevenEleven_DoorL</c> arrives on the RIGHT. Only the magnitude carries - the direction
        /// is measured from the two leaves' imported positions.
        /// </summary>
        public class StoreDoorSpec
        {
            public string LeftNode;
            public string RightNode;

            /// <summary>How far each leaf travels, model-local. Magnitude only - see the class note.</summary>
            public Vec3 Slide;

            /// <summary>Metres from <see cref="StoreSpec.Entrance"/> that open the doors.</summary>
            public float TriggerRadius = 3.8f;

            /// <summary>Seconds for a full closed → open sweep.</summary>
            public float OpenSec = 0.5f;

            /// <summary>Seconds the doors stay open after the trigger clears.</summary>
            public float HoldSec = 1.2f;
        }

        /// <summary>The sales floor rectangle, model-local. See <see cref="StoreSpec.Floor"/>.</summary>
        public class StoreFloorSpec
        {
            public float MinX;
            public float MaxX;
            public float MinZ;
            public float MaxZ;
        }

        /// <summary>
        /// The shopkeeper. <see cref="Url"/> is the web build's dedicated <c>idle-woman.glb</c>; this
        /// port stands a crowd prefab there instead, which idles for free and - because it is never
        /// bound to a seed - is invisible to <c>RunOverSystem</c>, so she cannot be killed and the
        /// shop cannot be made unreachable. U21 proved the pattern on the delivery customers.
        /// </summary>
        public class StoreClerkSpec
        {
            public string Url;

            /// <summary>Facing, radians, ADDED to the building's own yaw.</summary>
            public float Yaw;

            public float Scale = 1f;

            /// <summary>Metres from the stand that count as "at the counter".</summary>
            public float TalkRadius = 2.8f;
        }

        /// <summary>
        /// <c>src/powerup/powerup.config.ts</c> - the 7-Eleven catalogue.
        ///
        /// <b>The price arithmetic is closed and must stay closed.</b> The campaign pays
        /// 80 + 120 + 200 + 300 = $700, once per New Game, and a bust docks $100. One of each item is
        /// $265, so a playthrough affords roughly two full sets. Re-pricing anything here, or adding
        /// a second income source, dissolves the only scarcity the game has.
        /// </summary>
        public class PowerUpCatalogSpec
        {
            /// <summary>Shop and HUD order. The array IS the order; the hotkey is its 1-based index.</summary>
            public List<PowerUpSpec> Items;

            /// <summary>☕'s multiplier on the vehicle speed clamp. Cars, bike, jetski - never the heli.</summary>
            public float SpeedBoost = 1.25f;

            /// <summary>The web build's <c>?stock=N</c> ceiling. Kept for the debug filler.</summary>
            public int DevStockMax = 99;
        }

        /// <summary>One item. <see cref="Id"/> is also its <c>PlayerPrefs</c> key suffix.</summary>
        public class PowerUpSpec
        {
            public string Id;

            /// <summary>The number key that ignites it. Also the label on the HUD chip.</summary>
            public string Hotkey;

            public string Emoji;
            public string Label;

            /// <summary>What it does, in the player's words. One line in the shop row.</summary>
            public string Blurb;

            public int Price;

            /// <summary>
            /// Seconds the effect runs. <b>0 means it is not timed</b> - the bag ARMS instead, and is
            /// spent by the next mission that completes. Every "is this a clock?" test in the port
            /// gates on <c>DurationSec &gt; 0</c>, exactly as the web build's does.
            /// </summary>
            public float DurationSec;
        }
    }
}
