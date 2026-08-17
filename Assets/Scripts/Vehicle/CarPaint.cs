using UnityEngine;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// The body-paint slots of a drivable car, so its colour can be changed at runtime.
    ///
    /// It exists for one thing: a car you steal has to keep the colour it had. U17b spawns drivable
    /// cars in place of two other kinds of car - a parked filler from the lot and a stopped street
    /// car from the traffic pool - and both of those are already wearing a generated material asset
    /// from a palette. Handing that same asset to the drivable copy is exact by construction, costs
    /// no allocation, and reuses a material whose textures another builder has already pointed at
    /// U15's compressed copies. Anything else - a per-instance colour, a MaterialPropertyBlock, a
    /// runtime <c>new Material</c> - either mismatches the original by a shade or gives this one car
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

        [Tooltip("The body colour is baked into the base colour TEXTURE rather than a flat factor (the " +
                 "motorcycle: a red Vespa in one atlas). A repaint then recolours the atlas's pixels " +
                 "near Texture Body Color and leaves the rest - chrome, seat, tyres - alone.")]
        [SerializeField] private bool paintInTexture;
        [SerializeField] private Color textureBodyColor = new(0.75f, 0f, 0f);

        /// <summary>See <see cref="ConfigureBakedBody"/>.</summary>
        public bool PaintInTexture => paintInTexture;

        /// <summary>The atlas colour that IS the body, sRGB, when <see cref="PaintInTexture"/>.</summary>
        public Color TextureBodyColor => textureBodyColor;

        /// <summary>What this car is wearing now, or null if it has never been repainted.</summary>
        public Material Current { get; private set; }

        /// <summary>
        /// The prefab's own paint - what the first slot held when this instance woke, before any
        /// theft or repaint touched it. <c>PaintPalette</c> clones its colours from this, never from
        /// <see cref="Current"/>, so a car painted blue and then red is a clone of the original both
        /// times and every Mustang keys the same cache entry.
        /// </summary>
        public Material Source { get; private set; }

        /// <summary>
        /// The auto shop's colour on this car as a packed <c>0xRRGGBB</c>, or -1 if it wears the
        /// prefab's or a claimed car's paint. Set by <c>PaintPalette.Apply</c>.
        /// </summary>
        public int CurrentHex { get; set; } = -1;

        /// <summary>
        /// The name <c>PaintStore</c> keeps this vehicle's colour under, or null for a car with no
        /// identity across a reload (a promoted lot car, a hijacked street car). Set by the spawner.
        /// </summary>
        public string PersistKey { get; set; }

        /// <summary>Set by <c>CarBuilder</c>.</summary>
        public void Configure(Renderer[] renderers, int[] slots)
        {
            paintRenderers = renderers;
            paintSlots = slots;
        }

        /// <summary>
        /// Set by <c>MotorcycleBuilder</c>: this vehicle's paint is pixels in its atlas near
        /// <paramref name="bodyColor"/>, so a repaint recolours the texture instead of the factor.
        /// </summary>
        public void ConfigureBakedBody(Color bodyColor)
        {
            paintInTexture = true;
            textureBodyColor = bodyColor;
        }

        private void Awake()
        {
            if (paintRenderers.Length == 0 || paintRenderers[0] == null) return;
            var materials = paintRenderers[0].sharedMaterials;
            int slot = paintSlots.Length > 0 ? paintSlots[0] : 0;
            if (slot >= 0 && slot < materials.Length) Source = materials[slot];
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
