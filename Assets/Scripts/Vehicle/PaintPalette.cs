using System.Collections.Generic;
using UnityEngine;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// The auto shop's paint - U35g. Runtime materials for the colours a player picks.
    ///
    /// <b>One material per (source paint, colour), never one per car.</b> The lot and the traffic pool
    /// batch because every car wearing a colour shares one material asset; a repainted car has to keep
    /// that rule or it takes a draw call of its own. So the shop clones the vehicle's OWN paint material
    /// once per colour and caches the clone - two Mustangs painted blue share one material, and a
    /// Mustang painted blue twice allocates nothing the second time. A <c>MaterialPropertyBlock</c>
    /// would be simpler and is wrong twice over: it breaks instancing, and <c>VehicleDamage.Char</c>
    /// already owns the block on these renderers.
    ///
    /// The clone is taken from <see cref="CarPaint.Source"/> - the prefab's own paint, which the
    /// builder already pointed at U15's compressed textures and which carries the right shader
    /// (clearcoat on the Audi) - so a repaint differs from the original by its colour and nothing else.
    ///
    /// The cache is static and survives a scene reload; an entry that Unity has destroyed under it
    /// (a domain reload) is fake-null and is rebuilt on the next request.
    /// </summary>
    public static class PaintPalette
    {
        private static readonly Dictionary<(Material, int), Material> Cache = new();

        /// <summary>The shared material for this source paint in this colour, made on first use.</summary>
        public static Material For(Material source, int hex)
        {
            if (source == null) return null;

            var key = (source, hex);
            if (Cache.TryGetValue(key, out var material) && material != null) return material;

            material = new Material(source)
            {
                name = $"{source.name}_Paint_{hex:X6}",
                enableInstancing = true,
            };
            SetBaseColor(material, hex);
            Cache[key] = material;
            return material;
        }

        /// <summary>Repaints one vehicle. A vehicle with no paint slot is left alone.</summary>
        public static bool Apply(CarPaint paint, int hex)
        {
            if (paint == null) return false;
            var material = paint.PaintInTexture
                ? ForBakedBody(paint.Source, paint.TextureBodyColor, hex)
                : For(paint.Source, hex);
            if (material == null) return false;

            paint.Apply(material);
            paint.CurrentHex = hex;
            return true;
        }

        // --- paint that lives in the texture ------------------------------------------------------

        private static readonly Dictionary<(Texture, int), Texture2D> Atlases = new();

        private static readonly int BaseColorTexture = Shader.PropertyToID("baseColorTexture");
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        /// <summary>Hue tolerance either side of the body colour, degrees; and the saturation and value floors under which a pixel is chrome, seat or shadow, not body.</summary>
        private const float BodyHueTolerance = 30f;
        private const float BodyMinSaturation = 0.45f;
        private const float BodyMinValue = 0.06f;

        /// <summary>
        /// The motorcycle's case: its Vespa is red because its ATLAS is red, so multiplying the
        /// factor would give red × blue = black. Instead the atlas is copied once per colour with
        /// every body pixel - hue within <see cref="BodyHueTolerance"/> of <paramref name="bodyColor"/>,
        /// saturated, not black - re-hued to the target at its own shading, and everything else
        /// (chrome, seat, tyres, the teal box on its own material) untouched. One texture and one
        /// material per (atlas, colour), cached like the factor path.
        /// </summary>
        public static Material ForBakedBody(Material source, Color bodyColor, int hex)
        {
            if (source == null) return null;

            var key = (source, hex);
            if (Cache.TryGetValue(key, out var material) && material != null) return material;

            int property = source.HasProperty(BaseColorTexture) ? BaseColorTexture
                         : source.HasProperty(BaseMap) ? BaseMap : 0;
            var atlas = property != 0 ? source.GetTexture(property) as Texture2D : null;
            if (atlas == null) return For(source, hex); // no atlas after all - the factor path

            if (!Atlases.TryGetValue((atlas, hex), out var recolored) || recolored == null)
            {
                recolored = RecolorBody(atlas, bodyColor, hex);
                Atlases[(atlas, hex)] = recolored;
            }

            material = new Material(source)
            {
                name = $"{source.name}_Paint_{hex:X6}",
                enableInstancing = true,
            };
            material.SetTexture(property, recolored);
            Cache[key] = material;
            return material;
        }

        /// <summary>
        /// A copy of <paramref name="atlas"/> with its body pixels re-hued. Read back through a
        /// RenderTexture because glTFast's textures are not CPU-readable; the values are sRGB in and
        /// sRGB out, and the target colour is applied at the pixel's own value relative to the body's
        /// nominal value, so shading and highlights survive the recolour.
        /// </summary>
        private static Texture2D RecolorBody(Texture2D atlas, Color bodyColor, int hex)
        {
            int w = atlas.width, h = atlas.height;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Graphics.Blit(atlas, rt);
            RenderTexture.active = rt;
            var pixels = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            pixels.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            var target = Core.TheBlockConfig.ColorFromHex(hex);
            Color.RGBToHSV(bodyColor, out float bodyHue, out _, out float bodyValue);
            if (bodyValue <= 0f) bodyValue = 1f;

            var data = pixels.GetPixels32();
            for (int i = 0; i < data.Length; i++)
            {
                Color c = data[i];
                Color.RGBToHSV(c, out float hue, out float sat, out float val);
                if (sat < BodyMinSaturation || val < BodyMinValue) continue;
                if (Mathf.Abs(Mathf.DeltaAngle(hue * 360f, bodyHue * 360f)) > BodyHueTolerance) continue;

                float shade = val / bodyValue;
                var o = new Color(
                    Mathf.Clamp01(target.r * shade),
                    Mathf.Clamp01(target.g * shade),
                    Mathf.Clamp01(target.b * shade),
                    c.a);
                data[i] = o;
            }

            var result = new Texture2D(w, h, TextureFormat.RGBA32, true, false)
            {
                name = $"{atlas.name}_Paint_{hex:X6}",
                wrapMode = atlas.wrapMode,
                filterMode = atlas.filterMode,
                anisoLevel = atlas.anisoLevel,
            };
            result.SetPixels32(data);
            result.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            if (Application.isPlaying) Object.Destroy(pixels); else Object.DestroyImmediate(pixels);
            return result;
        }

        /// <summary>
        /// Writes a packed <c>0xRRGGBB</c> as this material's base colour, whichever shader it is on.
        /// Returns the property it used, or null if the shader has no base colour at all.
        ///
        /// <b>The two branches are not interchangeable.</b> glTFast's <c>baseColorFactor</c> is
        /// declared gamma-tagged, so the sRGB value goes in exactly as written and <c>.linear</c>
        /// renders the car near-black. URP/Lit's <c>_BaseColor</c> is untagged and is read as linear,
        /// so it needs the conversion. A builder that only knows the URP name writes nothing at all
        /// on a glTFast material - silently, with no warning - which is how the Mustang shipped in
        /// its model's native paint instead of the config's red for the whole of U8.
        ///
        /// This is the one copy of the rule; the editor's <c>VehicleMaterials.SetBaseColor</c> calls it.
        /// </summary>
        public static string SetBaseColor(Material material, int hex)
        {
            var color = Core.TheBlockConfig.ColorFromHex(hex);

            if (material.HasProperty("baseColorFactor"))
            {
                material.SetColor("baseColorFactor", color);
                return "baseColorFactor";
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color.linear);
                return "_BaseColor";
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color.linear);
                return "_Color";
            }

            return null;
        }
    }
}
