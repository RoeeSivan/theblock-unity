using UnityEngine;

namespace TheBlock.Missions
{
    /// <summary>Where a mission sits in its own lifecycle - the port of <c>MissionStatus</c>.</summary>
    public enum MissionStatus
    {
        Inactive,
        Active,
        Complete,
        Failed,
    }

    /// <summary>
    /// The campaign contract: the small lifecycle surface every mission exposes so
    /// <see cref="Campaign"/> can track order and completion. The port of
    /// <c>src/mission/mission.ts</c>.
    ///
    /// <b>Deliberately thin.</b> Missions are allowed to differ in shape - the pizza run is an
    /// ambient objective layered on free-roam, the beach dance is a modal takeover with its own
    /// camera - so this covers ONLY lifecycle, identity and entry. Never the per-frame update, the
    /// HUD or the input, which stay specific to each mission. Keeping the shared part this small is
    /// what lets two dissimilar missions coexist without a leaky "one true Update()".
    ///
    /// <b>An abstract MonoBehaviour, not an interface, and that is not a style preference.</b>
    /// <c>[SerializeField]</c> on an interface stores nothing in Unity - no value, no warning (see
    /// the <c>serializefield-interface-stores-nothing</c> memory). The campaign has to hold its
    /// four missions in a serialized, hand-ordered list, so the thing in that list must be a
    /// concrete Object type. It also buys what the web build's closures cannot have: every
    /// mission's live state is visible in the Inspector during Play.
    /// </summary>
    public abstract class MissionBehaviour : MonoBehaviour
    {
        /// <summary>Stable key. Matches a <c>campaignText</c> row; never shown to players.</summary>
        public abstract string Id { get; }

        /// <summary>Player-facing name - the Mission Select rows U26 will draw.</summary>
        public abstract string Title { get; }

        /// <summary>Where this mission currently sits in its own lifecycle.</summary>
        public abstract MissionStatus Status { get; }

        /// <summary>
        /// Set the world up and begin: teleport, spawn targets, show the briefing - whatever "start
        /// playing this now" means here.
        ///
        /// <b>Both the in-world trigger AND the dev jump call this</b>, so testing a mission in
        /// isolation runs the exact path real play does, never a stub. Missions that need to fade,
        /// load or wait start a coroutine and return; the campaign does not await anything.
        /// </summary>
        public abstract void Enter();

        /// <summary>
        /// Tear down whatever this mission spawned - targets, survivors, gates, and their beacons,
        /// map pins and colliders - with no restart.
        ///
        /// Called from the completion AND the failure edge, because a player who walks away from a
        /// failed run instead of retrying used to leave its actors in the world for the whole
        /// session. Must be idempotent: a retry calls it first and then respawns. The default does
        /// nothing, because not every mission spawns anything (the dance, for one).
        /// </summary>
        public virtual void Cleanup() { }

        /// <summary>
        /// End this run NOW as a loss, from outside - today only the bust does it.
        ///
        /// It has to land in the exact same <see cref="MissionStatus.Failed"/> state as running out
        /// of clock, so the fail card, the sting and the retry key all work unchanged: they hang off
        /// the status EDGE, never off the cause. The default does nothing, for a mission that cannot
        /// be interrupted (again, the dance - you cannot be arrested mid-routine).
        /// </summary>
        public virtual void Fail() { }

        /// <summary>
        /// Restart from scratch after a failure. The default does nothing; a timed mission overrides
        /// it to clear its actors, reset the clock and respawn.
        /// </summary>
        public virtual void Retry() { }

        /// <summary>Seconds left on this mission's clock, or null if it has none running.</summary>
        public virtual float? TimeLeft => null;

        /// <summary>
        /// The HUD's objective line while this mission is ACTIVE - "Deliveries 2/5", "Gates 4/9".
        /// Null hands the line back to the campaign's own "go here next" text.
        /// </summary>
        public virtual string ObjectiveLine => null;
    }
}
