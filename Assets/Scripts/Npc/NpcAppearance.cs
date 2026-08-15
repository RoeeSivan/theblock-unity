using UnityEngine;

namespace TheBlock.Npc
{
    /// <summary>
    /// Makes one pedestrian prefab look like several people.
    ///
    /// Twelve finished characters is not many for a street, and the pack underneath them is
    /// modular — 9 tops and 4 pairs of trousers per gender, 8 haircuts, 5 face materials, three
    /// body blendshapes — so the combination space is six figures. Assembling parts at build time
    /// would reach all of it, and costs a bone-rebinding pass across six parts times five LODs;
    /// swapping two MATERIALS reaches 30 looks per prefab, 360 across the twelve, for nothing.
    ///
    /// Both swaps assign SHARED material assets, generated once by <c>NpcBuilder</c>. That is the
    /// same call U13 made for the car park's paint: a <see cref="MaterialPropertyBlock"/> would be
    /// the obvious way to tint a shirt and it silently opts every pedestrian wearing it out of the
    /// SRP Batcher, which is the whole reason a crowd renders cheaply.
    /// </summary>
    public class NpcAppearance : MonoBehaviour
    {
        [Tooltip("Face materials for this character's gender. One is picked per spawn.")]
        [SerializeField] private Material[] faceVariants;

        [Tooltip("Every LOD of the head, in the order their face material slot is listed below.")]
        [SerializeField] private Renderer[] faceRenderers;

        [Tooltip("Which material slot on each head renderer is the face.")]
        [SerializeField] private int[] faceSlots;

        [Tooltip("Tinted clones of this character's top. One is picked per spawn.")]
        [SerializeField] private Material[] topVariants;

        [Tooltip("Every LOD of the top.")]
        [SerializeField] private Renderer[] topRenderers;

        /// <summary>Set by <c>NpcBuilder</c>; kept so the appearance pass is reproducible at edit time.</summary>
        public void Configure(
            Material[] faces, Renderer[] heads, int[] slots, Material[] tops, Renderer[] shirts)
        {
            faceVariants = faces;
            faceRenderers = heads;
            faceSlots = slots;
            topVariants = tops;
            topRenderers = shirts;
        }

        /// <summary>
        /// Rolls this pedestrian's face and shirt. Called by <see cref="CrowdSpawner"/> every time an
        /// agent is recycled, so the same pooled body is a different person each time it walks back
        /// into view — which is what keeps a pool of forty from reading as forty.
        /// </summary>
        public void Randomize(System.Random rng)
        {
            if (faceVariants is { Length: > 0 } && faceRenderers != null)
            {
                var face = faceVariants[rng.Next(faceVariants.Length)];
                for (int i = 0; i < faceRenderers.Length; i++)
                {
                    var renderer = faceRenderers[i];
                    if (renderer == null) continue;

                    int slot = faceSlots != null && i < faceSlots.Length ? faceSlots[i] : 0;
                    var materials = renderer.sharedMaterials;
                    if (slot < 0 || slot >= materials.Length) continue;

                    materials[slot] = face;
                    renderer.sharedMaterials = materials;
                }
            }

            if (topVariants is { Length: > 0 } && topRenderers != null)
            {
                var top = topVariants[rng.Next(topVariants.Length)];
                foreach (var renderer in topRenderers)
                {
                    if (renderer != null) renderer.sharedMaterial = top;
                }
            }
        }
    }
}
