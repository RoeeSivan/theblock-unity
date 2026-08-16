namespace TheBlock.Core
{
    /// <summary>
    /// What the run is doing right now - the port of <c>src/game/modes.ts</c>.
    ///
    /// The web build's enum has six labels and this now has five of them. <c>Transition</c> is still
    /// absent, and deliberately: it exists in the web to freeze input behind a fade-to-black, and
    /// U13's interior teleport here is instant with no fade to hide behind (the fade itself is U25's
    /// row). Adding a label nothing switches on is how a state machine quietly grows dead branches,
    /// so it arrives with the fade or not at all.
    ///
    /// <see cref="Entering"/> and <see cref="Exiting"/> exist to freeze input while the door swings,
    /// so nothing can fire mid-teleport: no jump, no second <c>E</c>, no trigger. They are not
    /// cosmetic.
    /// </summary>
    public enum GameMode
    {
        /// <summary>Walking. The only mode that reads on-foot input.</summary>
        OnFoot,

        /// <summary>Getting in - the entry animation or the timed door swing. Input is frozen.</summary>
        Entering,

        /// <summary>At the wheel. The only mode that reads vehicle input.</summary>
        Driving,

        /// <summary>Getting out - stood beside the car while the door shuts. Input is frozen.</summary>
        Exiting,

        /// <summary>
        /// The beach dance (U22). A modal takeover: the player's body is hidden, a dancer stands on
        /// the stage in their place, the camera frames it, and the only live input is the four
        /// arrows. Every other system - the police, the doorway, the vehicle machine - reads this
        /// and stands down, because none of them has anything to act on while nobody is on the map.
        /// </summary>
        Rhythm,
    }
}
