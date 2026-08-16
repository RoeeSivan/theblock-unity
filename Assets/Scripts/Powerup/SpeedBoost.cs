using UnityEngine;

namespace TheBlock.Powerup
{
    /// <summary>
    /// ☕ Nitro coffee's multiplier on every drivable speed clamp — the port of
    /// <c>src/powerup/boost.ts</c>.
    ///
    /// <b>A static, and not a field on <see cref="PowerUps"/>, for the reason the web build gives:</b>
    /// the sixteen cars, the motorcycle and the jetski are all built during boot, long before a shop
    /// exists, so their clamps have nothing to hold a reference to. One writer, many readers, and the
    /// vehicles stay unaware that a shop was ever added.
    ///
    /// <b>It multiplies; it does not win.</b> U28b's fuel tank will put its own factor on the same
    /// clamp, and a dry tank at full boost must still limp — so both are factors of one product
    /// rather than two candidates for a <c>Max</c>. This is also why it is NOT
    /// <c>CarController.SpeedLimitOverride</c>: that seam is absolute, cop-owned, and exists to raise
    /// a cruiser's ceiling ABOVE <c>config.vehicle.maxSpeed</c> during a pursuit (U19d). Routing the
    /// boost through it would have a power-up and a chase quietly overwrite each other.
    /// </summary>
    public static class SpeedBoost
    {
        /// <summary>Live multiplier on the player's speed clamps. 1 = no boost.</summary>
        public static float Factor { get; private set; } = 1f;

        /// <summary>
        /// Sets the multiplier. Written by <see cref="PowerUps"/> on every state change AND every
        /// tick, so no path can leave the world boosted by forgetting to clear it.
        /// </summary>
        public static void Set(float multiplier) => Factor = Mathf.Max(0.01f, multiplier);

        /// <summary>
        /// Entering Play with the static left over from the last session must not start the game
        /// boosted. <c>RuntimeInitializeOnLoadMethod</c> runs once per session rather than per scene
        /// load, which is exactly the guarantee needed here — see <c>Core.Pause</c>, which resets for
        /// the same reason.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetOnPlay() => Factor = 1f;
    }
}
