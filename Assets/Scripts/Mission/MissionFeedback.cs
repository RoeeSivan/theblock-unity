using System.Collections.Generic;

namespace TheBlock.Missions
{
    /// <summary>One mission crossing into a terminal state, since the previous poll.</summary>
    public readonly struct MissionEdge
    {
        public readonly string Id;
        public readonly bool Complete;

        public MissionEdge(string id, bool complete)
        {
            Id = id;
            Complete = complete;
        }
    }

    /// <summary>
    /// Detects when a mission's status NEWLY becomes complete or failed - the port of
    /// <c>src/game/mission-feedback.ts</c>.
    ///
    /// The point is that one place fires one card, one sting and one payout per event, instead of
    /// every mission remembering to do it at each of its own exits. It only diffs status snapshots:
    /// no UI, no audio, no side effects, which is what keeps the rule "a bust and a clock time-out
    /// are the same edge" true by construction rather than by discipline.
    ///
    /// <b>A poll, not an event, on purpose.</b> Four enum comparisons a frame is free, and an event
    /// would give a mission two ways to announce the same thing - the status property and the
    /// notification - which is exactly how the two disagree later.
    /// </summary>
    public class MissionFeedback
    {
        private readonly Dictionary<string, MissionStatus> _seen = new();
        private readonly List<MissionEdge> _edges = new();

        /// <summary>
        /// Edges seen since the previous poll. Empty on a steady frame. The returned list is reused,
        /// so read it before calling again.
        /// </summary>
        public List<MissionEdge> Poll(IReadOnlyList<MissionBehaviour> missions)
        {
            _edges.Clear();
            if (missions == null) return _edges;

            foreach (var m in missions)
            {
                if (m == null) continue;
                var now = m.Status;
                var known = _seen.TryGetValue(m.Id, out var was);
                if ((!known || now != was) &&
                    (now == MissionStatus.Complete || now == MissionStatus.Failed))
                {
                    _edges.Add(new MissionEdge(m.Id, now == MissionStatus.Complete));
                }

                _seen[m.Id] = now;
            }

            return _edges;
        }

        /// <summary>
        /// Adopts the current statuses without reporting anything.
        ///
        /// Called once at Start, so a mission the SAVE says is already complete does not fire its
        /// card the instant the game loads. Without it, resuming a run replays the last handoff.
        /// </summary>
        public void Prime(IReadOnlyList<MissionBehaviour> missions)
        {
            if (missions == null) return;
            foreach (var m in missions)
                if (m != null)
                    _seen[m.Id] = m.Status;
        }
    }
}
