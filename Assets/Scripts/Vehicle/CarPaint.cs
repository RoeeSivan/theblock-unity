using UnityEngine;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// The body-paint slots of a drivable car, so its colour can be changed at runtime.
    ///
    /// It exists for one thing: a car you steal has to keep the colour it had. U17b spawns drivable
    /// cars in place of two other kinds of car — a parked filler from the lot and a stopped street
    /// car from the traffic pool — and both of those are already wearing a generated material asset
    /// from a palette. Handing that same asset to the drivable copy is exact by construction, costs
    /// no allocation, and reuses a material whose textures another builder has already pointed at
    /// U15's compressed copies. Anything else — a per-instance colour, a MaterialPropertyBlock, a
    /// runtime <c>new Material</c> — either mismatches the original by a shade or gives this one car
    /// its own draw call.
    ///
    /// Wired by <c>CarBuilder</c>, which is the only place that knows which slot on which renderer
    /// held the model's <c>CarPrimaryColor</c> / <c>primary</c> material.
    /// </summary>
    [DisallowMultipleComponent]
    public class CarPaint : MonoBehaviour
    {
        [Tooltip("Renderers holding the body-paint slot, and which slot it is on each.")]
        [SerializeField] private Renderer[] paintRenderers = System.Array.Empty<Renderer>();
        [SerializeField] private int[] paintSlots = System.Array.Empty<int>();

        /// <summary>What this car is wearing now, or null if it has never been repainted.</summary>
        public Material Current { get; private set; }

        /// <summary>Set by <c>CarBuilder</c>.</summary>
        public void Configure(Renderer[] renderers, int[] slots)
        {
            paintRenderers = renderers;
            paintSlots = slots;
        }

        /// <summary>
        /// Puts one material into every paint slot. A null is ignored rather than blanking the car:
        /// a claimed car with no recorded paint should keep the prefab's own, not turn magenta.
        /// </summary>
        public void Apply(Material material)
        {
            if (material == null) return;

            for (int i = 0; i < paintRenderers.Length; i++)
            {
                var renderer = paintRenderers[i];
                if (renderer == null) continue;

                int slot = i < paintSlots.Length ? paintSlots[i] : 0;
                var materials = renderer.sharedMaterials;
                if (slot < 0 || slot >= materials.Length) continue;
                if (materials[slot] == material) continue;

                materials[slot] = material;
                renderer.sharedMaterials = materials;
            }

            Current = material;
        }
    }
}
