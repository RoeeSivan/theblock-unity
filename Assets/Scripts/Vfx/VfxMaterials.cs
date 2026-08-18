using UnityEngine;

namespace TheBlock.Vfx
{
    /// <summary>Which of the four transparent looks a runtime effect wants.</summary>
    public enum VfxBlend
    {
        /// <summary>URP Particles/Unlit, alpha blended. Smoke, blood droplets.</summary>
        ParticleAlpha,

        /// <summary>URP Particles/Unlit, additive. Flame, sparks, the blast.</summary>
        ParticleAdditive,

        /// <summary>URP Unlit, alpha blended. Flat quads - the blood stain.</summary>
        UnlitAlpha,

        /// <summary>URP Unlit, additive. The mission beacon.</summary>
        UnlitAdditive,
    }

    /// <summary>
    /// The one place a runtime effect gets a transparent material, and the fix for the bug that made
    /// the built Player's smoke a swarm of black squares while the Editor looked right.
    ///
    /// <b>THE BUG, because it will happen again the moment somebody writes
    /// <c>new Material(Shader.Find(...))</c> and enables a keyword.</b> URP's transparency is chosen
    /// by the <c>_SURFACE_TYPE_TRANSPARENT</c> keyword, and in the shader that keyword is declared
    /// <c>#pragma shader_feature_local_fragment</c>. A <c>shader_feature</c> variant is compiled into
    /// a build ONLY if some MATERIAL ASSET in that build has the keyword on - unlike
    /// <c>multi_compile</c>, which always ships. In the Editor every variant is compiled on demand,
    /// so <c>EnableKeyword</c> at runtime works and the effect looks correct. In the Player the
    /// variant was never built, the shader silently falls back to its opaque variant, the fragment
    /// forces alpha to 1, and a soft round puff renders as a solid quad of its own tint - a black
    /// square for smoke authored dark grey. Nothing logs. <c>Shader.Find</c> succeeded; the shader is
    /// in Graphics Settings' Always Included list, which ships the SHADER, not its shader_feature
    /// variants.
    ///
    /// <b>The fix is the material assets in <c>Assets/Resources/Vfx/</c>.</b> They exist to be seen by
    /// the build's variant collector, and they are used as the templates here rather than left
    /// sitting unused, because an asset whose only job is to be scanned is an asset somebody deletes
    /// as dead weight. Four combinations, one per <see cref="VfxBlend"/>.
    ///
    /// <b>Every caller gets its own instance.</b> <c>new Material(template)</c> copies the properties
    /// AND the keyword state; the caller then binds its own generated texture and tint. The template
    /// itself is never written to - editing a Resources asset at runtime dirties it in the Editor and
    /// leaks the change into the repo.
    /// </summary>
    public static class VfxMaterials
    {
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        /// <summary>The keyword this whole file exists to keep alive.</summary>
        private const string TransparentKeyword = "_SURFACE_TYPE_TRANSPARENT";

        /// <summary>
        /// A material for one effect, cloned from the checked-in template.
        ///
        /// The caller owns the result and must <c>Destroy</c> it - these are instances, and Unity
        /// does not free a material just because the renderer using it went away.
        /// </summary>
        /// <param name="blend">Which template.</param>
        /// <param name="texture">Bound to <c>_BaseMap</c>. May be null.</param>
        /// <param name="name">For the profiler and the frame debugger.</param>
        public static Material Build(VfxBlend blend, Texture texture, string name)
        {
            string path = ResourcePath(blend);
            var template = Resources.Load<Material>(path);

            var material = template != null
                ? new Material(template) { name = name }
                : Fallback(blend, name);

            // The template exists to carry ONE keyword into the build, and a template that lost it -
            // a bad hand-edit, a URP upgrade that renamed it, Unity parking it in m_InvalidKeywords -
            // fails exactly the way the original bug did: right in the Editor, opaque squares in the
            // Player, nothing logged. Cheap to check, and it is the only symptom there is.
            if (!material.IsKeywordEnabled(TransparentKeyword))
                Debug.LogError($"VfxMaterials: '{name}' has no {TransparentKeyword}. It will render " +
                               $"opaque in a build. Check Assets/Resources/{path}.mat.");

            if (texture != null) material.SetTexture(BaseMapId, texture);
            return material;
        }

        private static string ResourcePath(VfxBlend blend) => blend switch
        {
            VfxBlend.ParticleAlpha => "Vfx/ParticleAlpha",
            VfxBlend.ParticleAdditive => "Vfx/ParticleAdditive",
            VfxBlend.UnlitAlpha => "Vfx/UnlitAlpha",
            _ => "Vfx/UnlitAdditive",
        };

        /// <summary>
        /// The old runtime keyword dance, kept for the one case the templates cannot cover: somebody
        /// deleted or renamed the asset. It is CORRECT IN THE EDITOR AND WRONG IN A BUILD, which is
        /// the whole point of this file, so it shouts rather than quietly carrying on.
        /// </summary>
        private static Material Fallback(VfxBlend blend, string name)
        {
            Debug.LogError($"VfxMaterials: Assets/Resources/{ResourcePath(blend)}.mat is missing. " +
                           "Falling back to a runtime material - this looks right in the Editor and " +
                           "renders as opaque squares in a built Player.");

            bool particles = blend is VfxBlend.ParticleAlpha or VfxBlend.ParticleAdditive;
            bool additive = blend is VfxBlend.ParticleAdditive or VfxBlend.UnlitAdditive;

            var shader = Shader.Find(particles
                ? "Universal Render Pipeline/Particles/Unlit"
                : "Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");

            var material = new Material(shader) { name = name };
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", additive ? 2f : 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)(additive
                ? UnityEngine.Rendering.BlendMode.One
                : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return material;
        }
    }
}
