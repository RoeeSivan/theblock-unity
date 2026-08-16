using TheBlock.Vfx;

namespace TheBlock.Npc
{
    /// <summary>
    /// What a downed pedestrian is doing until they are handed back to the pool.
    ///
    /// Two implementations, and U35a is why there are two rather than one being replaced:
    /// <see cref="RunOverReaction"/> is the authored clip plus a coded arc, and
    /// <see cref="RagdollReaction"/> is PhysX. The clip is not dead code kept out of sentiment - it is
    /// the OFF state of the Settings toggle and the fallback for a body whose prefab never went
    /// through <c>RagdollBuilder</c>, so it has to stay reachable and has to stay correct.
    ///
    /// The interface is small on purpose: <c>Pedestrian</c> owns the body, the materials and the
    /// lifecycle, and asks a reaction only three things.
    /// </summary>
    public interface IRunOverReaction
    {
        /// <summary>One step, from <c>CrowdSpawner</c>'s LateUpdate.</summary>
        void Tick(float dt, Blood blood);

        /// <summary>0..1, applied to the body's materials by <c>Pedestrian</c>.</summary>
        float Opacity { get; }

        /// <summary>True once the body has faded out and the person can be stood back up.</summary>
        bool Done { get; }

        /// <summary>
        /// True when this reaction moves the pedestrian's own transform and therefore needs the
        /// animator's root motion harvested onto it first. The clip does; a ragdoll does not - its
        /// bones are simulated in world space and the root transform stays where the victim was
        /// standing.
        /// </summary>
        bool DrivesTransform { get; }
    }
}
