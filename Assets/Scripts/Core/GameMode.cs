namespace TheBlock.Core
{
    /// <summary>
    /// What the run is doing right now — the port of <c>src/game/modes.ts</c>.
    ///
    /// The web build's enum has six labels; these are the four that exist today. The other two are
    /// declared by the unit that needs them rather than sitting here unhandled:
    /// <c>transition</c> is the interior fade (U13) and <c>rhythm</c> is the dance minigame (U22).
    /// Adding a label nothing switches on is how a state machine quietly grows dead branches.
    ///
    /// <see cref="Entering"/> and <see cref="Exiting"/> exist to freeze input while the door swings,
    /// so nothing can fire mid-teleport: no jump, no second <c>E</c>, no trigger. They are not
    /// cosmetic.
    /// </summary>
    public enum GameMode
    {
        /// <summary>Walking. The only mode that reads on-foot input.</summary>
        OnFoot,

        /// <summary>Getting in — the entry animation or the timed door swing. Input is frozen.</summary>
        Entering,

        /// <summary>At the wheel. The only mode that reads vehicle input.</summary>
        Driving,

        /// <summary>Getting out — stood beside the car while the door shuts. Input is frozen.</summary>
        Exiting,
    }
}
