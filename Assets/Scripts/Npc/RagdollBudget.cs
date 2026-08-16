using System.Collections.Generic;
using UnityEngine;

namespace TheBlock.Npc
{
    /// <summary>
    /// How many bodies may be simulated at once - U35a's answer to the perf-and-quality rule.
    ///
    /// <b>A cap, and the oldest freezes.</b> Eleven bodies and ten joints is a real solver cost, and
    /// the number of people a car can plough through has no upper bound: the bumper box downs everyone
    /// inside it on one frame, and a pavement queue at a zebra is a dozen. Without a cap the worst
    /// case is not the common case being slow, it is one moment being a stall.
    ///
    /// <b>Freeze rather than refuse.</b> The alternative - "over the cap, play the old clip instead" -
    /// looks worse than it sounds: two people struck by the same bumper on the same frame would react
    /// in two different ways beside each other. Freezing the OLDEST keeps every new hit physical and
    /// spends the budget on the body the player is currently looking at, which is always the newest.
    /// A frozen body keeps its pose, keeps its own fade clock, and costs nothing.
    ///
    /// A static with no per-frame writer to heal it, so it is cleared by <c>SessionReset</c> - the
    /// same reason <c>FuelTank</c> is on that list (memory: <c>runtimeinitialize-is-once-per-session</c>).
    /// </summary>
    public static class RagdollBudget
    {
        /// <summary>
        /// Most bodies simulated at once. Four is the U35a row's own starting number and it is a
        /// starting number - U30b measures it on the Player, and this is the knob that answers.
        /// </summary>
        public static int Cap { get; set; } = 4;

        private static readonly List<Ragdoll> Live = new();

        /// <summary>How many are simulating right now, frozen ones excluded.</summary>
        public static int Count
        {
            get
            {
                int live = 0;
                for (int i = 0; i < Live.Count; i++)
                    if (Live[i] != null && !Live[i].Frozen) live++;
                return live;
            }
        }

        /// <summary>
        /// Take a slot. Always granted - what the cap decides is who gets frozen to pay for it, not
        /// whether the new body may move.
        /// </summary>
        public static void Take(Ragdoll body)
        {
            if (body == null) return;

            Live.RemoveAll(r => r == null);
            Live.Remove(body);
            Live.Add(body);

            // Freeze from the front: the list is in the order bodies were hit, so the front is the
            // oldest still moving.
            for (int i = 0; i < Live.Count && Count > Mathf.Max(1, Cap); i++)
            {
                var old = Live[i];
                if (old == null || old == body || old.Frozen) continue;
                old.Freeze();
            }
        }

        /// <summary>Hand the slot back. Called from <see cref="Ragdoll.Rest"/>, so it cannot be missed.</summary>
        public static void Give(Ragdoll body)
        {
            Live.Remove(body);
            Live.RemoveAll(r => r == null);
        }

        /// <summary>Drop everything - a scene reload leaves destroyed bodies in this list otherwise.</summary>
        public static void Clear() => Live.Clear();
    }
}
