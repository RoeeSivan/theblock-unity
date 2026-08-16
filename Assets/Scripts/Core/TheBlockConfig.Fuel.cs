using Newtonsoft.Json;

namespace TheBlock.Core
{
    /// <summary>
    /// U28b's half of the config: the fuel tank, limp mode and the pump.
    ///
    /// Split out of <c>TheBlockConfig.cs</c> for the reason every partial here is — one source
    /// module, one file — and it is the tenth and last source in the payload.
    ///
    /// <b>Unlike <see cref="StoreSpec"/>, none of this is a cross-check.</b> The 7-Eleven's numbers
    /// are also nodes inside its .glb, so the builder can assert the two agree. <c>gas-station.glb</c>
    /// ships no marker empties at all — 119 nodes, every one of them geometry — so these values have
    /// nowhere else to come from and the pump ANCHORS are measured off the pump meshes instead
    /// (<c>WorldBuilder.Fuel.cs</c>).
    /// </summary>
    public static partial class TheBlockConfig
    {
        /// <summary>
        /// <c>src/vehicle/fuel.config.ts</c> — data only, so a gameplay tweak never means code-diving.
        ///
        /// <b>The range arithmetic, so these can be re-derived instead of guessed:</b>
        /// 50 L / 5.2 L·km⁻¹ = <b>9.6 km</b> of range, which at <c>config.vehicle.maxSpeed</c>
        /// (20 m/s, the same cap every car in the game has) is 480 s = 8 minutes flat out. A half
        /// tank — the start state — is 4.8 km. The pizza shift's clock is 240 s, which even flat out
        /// covers only 4.8 km, so <b>a half tank clears mission 1 exactly</b> and the shift needs no
        /// fuel special-case.
        ///
        /// <b>There is no price. Refuelling is free</b>, in the web build and here: no key in the
        /// source module, and no wallet call anywhere in <c>fuel.ts</c>, <c>refuel.ts</c> or
        /// <c>gas-station.ts</c>. The two ways to lose money remain the shop and the bust.
        ///
        /// Every field carries the web's value as its C# default. That is the existing convention
        /// (<see cref="PowerUpCatalogSpec.SpeedBoost"/>) and it covers a field the exporter has not
        /// learned yet — but it deliberately does NOT cover a missing SECTION. If
        /// <c>fuelConfig</c> is absent, <see cref="Vehicles.FuelSystem"/> refuses to bind and says
        /// so, because a silent fallback is how a port's numbers drift away from their source.
        /// </summary>
        public class FuelSpec
        {
            /// <summary>A whole tank. The HUD bar reads <c>litres / TankLitres</c>.</summary>
            public float TankLitres = 50f;

            /// <summary>Every vehicle spawns at half a tank — filling up is the first errand.</summary>
            public float StartFrac = 0.5f;

            /// <summary>Distance-based burn: a tank means RANGE, not session time.</summary>
            public float LitresPerKm = 5.2f;

            /// <summary>Burn with the engine on but stopped (~83 min idle; a rounding cost).</summary>
            public float IdleLitresPerSec = 0.01f;

            /// <summary>Empty → full hold time, so the half-tank start fills in ~5 s.</summary>
            public float RefuelSecFromEmpty = 10f;

            /// <summary>Seconds between pump-counter ticks while fuelling.</summary>
            public float TickSec = 0.5f;

            /// <summary>At or below this the bar turns red and the low-fuel hint fires.</summary>
            public float LowFrac = 0.2f;

            /// <summary>
            /// At or below this, nudge the player toward the station. <b>Deliberately equal to
            /// <see cref="StartFrac"/></b>, so a fresh vehicle reminds you the moment you drive it —
            /// that IS the opening errand.
            /// </summary>
            public float RemindFrac = 0.5f;

            /// <summary>Seconds in the seat before either reminder may fire.</summary>
            public float RemindDelaySec = 3f;

            /// <summary>
            /// Max-speed multiplier on a dry tank. <b>NEVER 0</b> — you must never be stranded, which
            /// is the whole design of the empty state: it is a limp home, not a game over.
            /// </summary>
            public float LimpSpeedFrac = 0.25f;

            /// <summary>How fast the cap eases 1 → <see cref="LimpSpeedFrac"/> (1.5 s): it dies, not snaps.</summary>
            public float LimpRampPerSec = 0.5f;

            /// <summary>
            /// Wobble rate on a dry tank. The sputter is one number: it wobbles the speed CAP, and
            /// <c>EngineSound.SetSpeed</c> is fed that same clamped speed — so the engine note
            /// lurches with the car for free, with no audio work at all.
            /// </summary>
            public float SputterHz = 3f;

            /// <summary>± share the cap wobbles by.</summary>
            public float SputterDepth = 0.15f;

            /// <summary>
            /// Metres from the station centre that count as "at a pump" — the web build's ONE circle.
            /// The port keeps it, and unions three tighter circles at the pump meshes on top, so the
            /// eligible area is a superset by construction. See <see cref="World.GasStation"/>.
            /// </summary>
            public float PumpRadius = 9f;

            /// <summary>Must be stopped (or near enough) to fuel.</summary>
            public float PumpMaxKmh = 2f;
        }
    }
}
