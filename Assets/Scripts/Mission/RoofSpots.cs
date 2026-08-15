using System.Collections.Generic;
using UnityEngine;

namespace TheBlock.Missions
{
    /// <summary>
    /// Every rooftop the rescue may strand someone on, found once at build time.
    ///
    /// <b>This is U23's answer to the standing question.</b> The web samples rooftops at RUNTIME:
    /// up to 400 random points inside the district boxes, a downward raycast each, keep the ones
    /// whose surface is high enough, and relax the height threshold once if it could not place
    /// enough. That is a lot of casting on the frame a mission starts, it can quietly place three
    /// survivors instead of four, and nobody can look at the result before playing.
    ///
    /// Unity bakes it, the way U17 bakes the traffic graph and U19 bakes the police route graph. The
    /// candidates are found once by <c>WorldBuilder.Rescue</c>, written here, and drawn as gizmos —
    /// so "are the roofs sensible?" is a question with an answer before anyone flies anywhere, and
    /// the runtime casts nothing at all.
    /// </summary>
    [CreateAssetMenu(menuName = "The Block/Roof Spots", fileName = "RoofSpots")]
    public class RoofSpots : ScriptableObject
    {
        [System.Serializable]
        public struct Spot
        {
            /// <summary>World position on the roof surface, already converted.</summary>
            public Vector3 Position;

            /// <summary>Which district it was found in, for the bake report.</summary>
            public string District;
        }

        [SerializeField] private List<Spot> spots = new();

        public IReadOnlyList<Spot> Spots => spots;
        public int Count => spots.Count;

        /// <summary>Editor-side, used by the bake.</summary>
        public void SetSpots(List<Spot> baked) => spots = baked ?? new List<Spot>();
    }
}
