using System.Collections.Generic;
using UnityEngine;

namespace TheBlock.Missions
{
    /// <summary>
    /// The campaign runner: the ordered list of missions and a cursor at the current one. The port
    /// of <c>src/mission/campaign.ts</c>.
    ///
    /// Deliberately tiny and engine-light - it owns SEQUENCE, not gameplay. Reorder
    /// <see cref="missions"/> in the Inspector to reorder the game; everything here keys off list
    /// position or mission id.
    /// </summary>
    public class Campaign : MonoBehaviour
    {
        [Tooltip("The campaign, in play order. Written by The Block → Build Campaign; hand-editable.")]
        [SerializeField] private List<MissionBehaviour> missions = new();

        // Serialized for the reason VehicleEnterExit's and Interior's run state is: a recompile
        // during Play reloads the domain but keeps the scene, and a campaign that forgot its cursor
        // would silently put the player back on mission 1 with mission 3's actors in the world.
        [SerializeField, HideInInspector] private int index;

        public IReadOnlyList<MissionBehaviour> Missions => missions;

        /// <summary>Index of the mission the campaign is pointed at.</summary>
        public int Index => index;

        public MissionBehaviour Current =>
            missions.Count == 0 ? null : missions[Mathf.Clamp(index, 0, missions.Count - 1)];

        /// <summary>Is the currently-selected mission the one with this id? The trigger gate.</summary>
        public bool IsCurrent(string id) => Current != null && Current.Id == id;

        /// <summary>Index of a mission by id, or −1.</summary>
        public int IndexOfId(string id)
        {
            for (var i = 0; i < missions.Count; i++)
                if (missions[i] != null && missions[i].Id == id)
                    return i;
            return -1;
        }

        /// <summary>Points at mission <paramref name="i"/> (clamped). Does NOT start it.</summary>
        public void Select(int i)
        {
            if (missions.Count == 0) return;
            index = Mathf.Clamp(i, 0, missions.Count - 1);
        }

        /// <summary>Begins the currently-selected mission.</summary>
        public void EnterCurrent() => Current?.Enter();

        /// <summary>
        /// If the current mission reports complete and a later one exists, advance the cursor.
        /// Returns true if it moved. Points the HUD at what is next without forcing an auto-start -
        /// whether missions chain or drop you back into free roam stays a design choice, and in this
        /// game it is free roam.
        /// </summary>
        public bool AdvanceIfComplete()
        {
            if (Current == null) return false;
            if (Current.Status != MissionStatus.Complete) return false;
            if (index >= missions.Count - 1) return false;
            index++;
            return true;
        }

        /// <summary>
        /// The campaign is over - i.e. the LAST mission reports complete.
        ///
        /// <b>Deliberately not "every mission is complete"</b>, and the web build's own comment
        /// explains what that cost it: Mission Select and Continue both jump the cursor forward,
        /// which leaves the skipped missions inactive forever. An all-complete test made the win
        /// state permanently unreachable for anyone who did not play one straight run - no win card,
        /// the objective line pinned forever, the final marker never cleared. Keying off the last
        /// mission means "you finished the finale" however you got there.
        /// </summary>
        public bool IsComplete =>
            missions.Count > 0 &&
            missions[missions.Count - 1] != null &&
            missions[missions.Count - 1].Status == MissionStatus.Complete;

        /// <summary>The mission currently being played, or null when nothing is running.</summary>
        public MissionBehaviour Active
        {
            get
            {
                foreach (var m in missions)
                    if (m != null && m.Status == MissionStatus.Active)
                        return m;
                return null;
            }
        }

        /// <summary>The mission that has failed and is waiting on a retry, or null.</summary>
        public MissionBehaviour Failed
        {
            get
            {
                foreach (var m in missions)
                    if (m != null && m.Status == MissionStatus.Failed)
                        return m;
                return null;
            }
        }

        /// <summary>Look one up by id. Null when nothing matches.</summary>
        public MissionBehaviour Find(string id)
        {
            var i = IndexOfId(id);
            return i < 0 ? null : missions[i];
        }

        /// <summary>Editor-side wiring, used by The Block → Build Campaign.</summary>
        public void SetMissions(List<MissionBehaviour> ordered)
        {
            missions = ordered ?? new List<MissionBehaviour>();
            index = 0;
        }
    }
}
