using System.Collections.Generic;
using UnityEngine;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// How much of U35b may be live at once - the same shape as <c>RagdollBudget</c>, and for the
    /// same reason: the worst case is not the common case being slow, it is one moment being a stall.
    ///
    /// Two resources are rationed here, and they are rationed differently.
    ///
    ///  - <b>Deformed bodies</b> cost MEMORY. A dented car holds a cloned mesh plus a pristine copy of
    ///    its vertices, and the Tesla's shell alone is 117k of them. Over the cap the oldest car is
    ///    RESTORED rather than sealed, because the pristine data lives in the .glb asset for free -
    ///    handing its <c>sharedMesh</c> back is exact, instant and releases everything. The cost is
    ///    that a car can visibly un-dent; the player's own car is exempt from eviction, so it is
    ///    always a car someone else was driving.
    ///  - <b>Shed parts</b> cost draw calls and bodies. Over the cap the oldest is despawned early,
    ///    which is invisible - a bumper lying in the road two streets back.
    ///
    /// Statics with no per-frame writer to heal them, so <c>SessionReset</c> clears them: after a
    /// scene reload these lists hold destroyed objects that still read as non-null (memory:
    /// <c>unity-null-coalescing-fake-null</c>), and the budget would be spent on the previous world.
    /// </summary>
    public static class DamageBudget
    {
        /// <summary>
        /// Most cars holding cloned, dented meshes at once. Four is a starting number; U30b measures
        /// it on the Player and this is the knob that answers.
        /// </summary>
        public static int DeformCap { get; set; } = 4;

        /// <summary>Most shed parts lying around at once. The U35b row's own number.</summary>
        public static int PartCap { get; set; } = 8;

        private static readonly List<DeformableBody> Deformed = new();
        private static readonly List<DetachedPart> Parts = new();

        /// <summary>How many cars are currently holding a cloned mesh.</summary>
        public static int DeformCount
        {
            get
            {
                Deformed.RemoveAll(b => b == null);
                return Deformed.Count;
            }
        }

        /// <summary>How many shed parts are lying in the world.</summary>
        public static int PartCount
        {
            get
            {
                Parts.RemoveAll(p => p == null);
                return Parts.Count;
            }
        }

        /// <summary>
        /// Take a deform slot. Always granted - what the cap decides is who pays, not whether this car
        /// may dent.
        /// </summary>
        public static void TakeDeform(DeformableBody body)
        {
            if (body == null) return;

            Deformed.RemoveAll(b => b == null);
            Deformed.Remove(body);
            Deformed.Add(body);

            // Evict from the front: the list is in the order cars were first damaged, so the front is
            // the one that has been dented the longest.
            for (int i = 0; i < Deformed.Count && Deformed.Count > Mathf.Max(1, DeformCap); i++)
            {
                var old = Deformed[i];
                if (old == null || old == body || old.Protected) continue;

                old.Restore();      // calls GiveDeform, which removes it from this list
                i--;
            }
        }

        /// <summary>Hand a deform slot back. Called from <see cref="DeformableBody.Restore"/>.</summary>
        public static void GiveDeform(DeformableBody body)
        {
            Deformed.Remove(body);
            Deformed.RemoveAll(b => b == null);
        }

        /// <summary>Register a shed part, despawning the oldest if the world is now littered.</summary>
        public static void TakePart(DetachedPart part)
        {
            if (part == null) return;

            Parts.RemoveAll(p => p == null);
            Parts.Add(part);

            while (Parts.Count > Mathf.Max(1, PartCap))
            {
                var old = Parts[0];
                Parts.RemoveAt(0);
                if (old != null) old.Expire();
            }
        }

        /// <summary>Called by a part as it despawns on its own clock.</summary>
        public static void GivePart(DetachedPart part)
        {
            Parts.Remove(part);
            Parts.RemoveAll(p => p == null);
        }

        /// <summary>Drop everything - a scene reload leaves destroyed objects in both lists.</summary>
        public static void Clear()
        {
            Deformed.Clear();
            Parts.Clear();
        }
    }
}
