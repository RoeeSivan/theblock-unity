using UnityEngine;

namespace TheBlock.Core
{
    /// <summary>
    /// Something the chase camera can sit behind: the player on foot, a car, later a bike or a
    /// helicopter.
    ///
    /// The camera owns the smoothing and the lens; the target owns where the boom hangs and what to
    /// look at. That split is what lets U9 swap targets on enter/exit without the camera knowing
    /// what a vehicle is - and it is why the boom is handed over already in Unity space. Each
    /// implementer runs its own config offset through <see cref="Convert.ModelOffset"/>, because
    /// that conversion belongs next to the config field it converts, not in the camera.
    /// </summary>
    public interface IChaseTarget
    {
        /// <summary>The transform the boom is relative to. Its <c>+Z</c> is the direction of travel.</summary>
        Transform Anchor { get; }

        /// <summary>Boom offset in <see cref="Anchor"/>'s local frame, already converted.</summary>
        Vector3 LocalBoom { get; }

        /// <summary>World point the camera aims at - roughly a head, or a car's roofline.</summary>
        Vector3 LookTarget { get; }

        /// <summary>Fraction of the remaining distance closed per frame at 60 fps.</summary>
        float FollowLerp { get; }
    }
}
