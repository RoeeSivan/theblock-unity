using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Gives the HUD a font that can draw emoji — <b>The Block → Build Emoji Font</b>.
    ///
    /// <b>The fault this closes.</b> The campaign's copy, the map's POI pins and the whole 7-Eleven
    /// catalogue lead with emoji, because the web build renders in a browser where the system emoji
    /// font is always present. Unity's default runtime font has none of them, so every one drew as a
    /// blank box, and U14 dropped the map's ⛽ 🚓 🏪 rather than ship boxes. <c>UI.Glyphs.Strip</c>
    /// was the stop-gap: keep the words, drop the pictures, at the point of DRAWING so that the day a
    /// font arrived the fix would be a deletion rather than a re-import. This is that day.
    ///
    /// <b>How UI Toolkit finds a fallback.</b> Not through <c>TMP_Settings</c> — that is uGUI's, and
    /// setting it does nothing here. A <see cref="PanelSettings"/> carries a
    /// <see cref="PanelTextSettings"/>, whose <c>fallbackFontAssets</c> list is what TextCore walks
    /// when the primary font has no glyph for a character. So the chain is: font file → dynamic
    /// <see cref="FontAsset"/> → the panel's text settings → <c>HudPanelSettings</c>.
    ///
    /// <b><see cref="GlyphRenderMode.COLOR"/> is the whole question.</b> NotoColorEmoji is a CBDT/CBLC
    /// bitmap font: its glyphs are little PNGs, not outlines, so every SDF mode has nothing to trace
    /// and rasterises empty. COLOR asks FreeType for the bitmap and gives the atlas an RGBA texture.
    /// If a future Unity stops supporting it the fallback is the MONOCHROME Noto Emoji at
    /// <see cref="GlyphRenderMode.SMOOTH"/> — a grey 🍕 beats a blank box — and the only change is the
    /// file this points at.
    /// </summary>
    public static class EmojiFontBuilder
    {
        private const string FontPath = "Assets/UI/Fonts/NotoColorEmoji.ttf";
        private const string FontAssetPath = "Assets/UI/Fonts/NotoColorEmoji FontAsset.asset";
        private const string TextSettingsPath = "Assets/UI/HudTextSettings.asset";
        private const string PanelSettingsPath = "Assets/UI/HudPanelSettings.asset";

        /// <summary>
        /// Sampling size and atlas. 96 px is comfortably above the biggest emoji the HUD draws (the
        /// shop's 20 px rows and the 40 px wordmark), and a CBDT strike is authored at 128 so asking
        /// for more would only upscale a bitmap.
        /// </summary>
        private const int SamplingPointSize = 96;

        private const int AtlasPadding = 2;
        private const int AtlasSize = 1024;

        /// <summary>The glyphs the game actually asks for. Rendered as a smoke test, not baked.</summary>
        private static readonly string[] Probe = { "🍕", "⛽", "🚓", "🏪", "🥤", "☕", "📱", "🎒", "💵", "🏆", "✅" };

        [MenuItem("The Block/Build Emoji Font")]
        public static void Build()
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null)
            {
                Debug.LogError($"EmojiFontBuilder: no font at {FontPath}. " +
                               "Download NotoColorEmoji.ttf (OFL) into Assets/UI/Fonts first.");
                return;
            }

            var fontAsset = BuildFontAsset(font);
            if (fontAsset == null) return;

            var textSettings = BuildTextSettings(fontAsset);
            AttachToPanel(textSettings);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"EmojiFontBuilder: {fontAsset.name} @ {SamplingPointSize} px " +
                      $"{fontAsset.atlasRenderMode}, {AtlasSize}² atlas → {TextSettingsPath} → " +
                      $"{PanelSettingsPath}.\n{Report(fontAsset)}");
        }

        /// <summary>
        /// A DYNAMIC font asset: glyphs are rasterised into the atlas the first time something asks
        /// for them, so the 3,600-glyph font costs one texture page and the dozen characters this
        /// game uses, not 3,600. The .ttf still ships — it is the source the atlas is populated from
        /// at runtime.
        /// </summary>
        private static FontAsset BuildFontAsset(Font font)
        {
            var fontAsset = FontAsset.CreateFontAsset(
                font, SamplingPointSize, AtlasPadding, GlyphRenderMode.COLOR,
                AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: true);

            if (fontAsset == null)
            {
                Debug.LogError("EmojiFontBuilder: CreateFontAsset returned null — the .ttf did not load.");
                return null;
            }

            fontAsset.name = "NotoColorEmoji FontAsset";

            var existing = AssetDatabase.LoadAssetAtPath<FontAsset>(FontAssetPath);
            if (existing != null) AssetDatabase.DeleteAsset(FontAssetPath);

            EnsureFolder("Assets/UI/Fonts");
            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);

            // The atlas texture and the material are sub-assets of the font asset, not files of their
            // own. Without this they are created in memory and thrown away on the next domain reload,
            // and the font comes back on the following Play with an atlas of nothing.
            if (fontAsset.atlasTextures != null)
                foreach (var texture in fontAsset.atlasTextures)
                {
                    if (texture == null) continue;
                    texture.name = "Atlas";
                    AssetDatabase.AddObjectToAsset(texture, fontAsset);
                }

            if (fontAsset.material != null)
            {
                fontAsset.material.name = "Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            EditorUtility.SetDirty(fontAsset);
            return fontAsset;
        }

        /// <summary>
        /// The panel's text settings, with the emoji font in the FALLBACK list rather than as the
        /// default — the HUD's Latin copy must keep coming from Unity's own font, and only the
        /// characters it has no glyph for should walk the chain.
        /// </summary>
        private static PanelTextSettings BuildTextSettings(FontAsset fontAsset)
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelTextSettings>();
                settings.name = "HudTextSettings";
                EnsureFolder("Assets/UI");
                AssetDatabase.CreateAsset(settings, TextSettingsPath);
            }

            settings.fallbackFontAssets ??= new List<FontAsset>();
            settings.fallbackFontAssets.RemoveAll(f => f == null || f.name == fontAsset.name);
            settings.fallbackFontAssets.Insert(0, fontAsset);

            // Also the emoji-specific chain. TextCore consults this first for characters in the emoji
            // ranges, and leaving it empty makes a variation selector (the "draw this as emoji" mark
            // that follows ⛽ and ✅) miss even when the fallback above has the glyph.
            // Fully qualified: `TextAsset` is ambiguous here. TextCore has its own — the base class of
            // FontAsset and SpriteAsset — and `UnityEngine.TextAsset` is the .txt importer's type.
            settings.emojiFallbackTextAssets ??= new List<UnityEngine.TextCore.Text.TextAsset>();
            settings.emojiFallbackTextAssets.RemoveAll(a => a == null || a == fontAsset);
            settings.emojiFallbackTextAssets.Insert(0, fontAsset);

            EditorUtility.SetDirty(settings);
            return settings;
        }

        private static void AttachToPanel(PanelTextSettings textSettings)
        {
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panel == null)
            {
                Debug.LogWarning($"EmojiFontBuilder: no {PanelSettingsPath} — run The Block → Build HUD " +
                                 "first, then this again. The font asset is built either way.");
                return;
            }

            panel.textSettings = textSettings;
            EditorUtility.SetDirty(panel);
        }

        /// <summary>
        /// Asks the font for the characters the game actually draws. A font asset that builds cleanly
        /// and has no glyph for 🍕 is the failure this whole file exists to prevent, and it is
        /// invisible until someone looks at the HUD.
        /// </summary>
        private static string Report(FontAsset fontAsset)
        {
            var have = new List<string>();
            var miss = new List<string>();

            foreach (var glyph in Probe)
            {
                var code = (uint)char.ConvertToUtf32(glyph, 0);
                if (fontAsset.HasCharacter(code, searchFallbacks: false, tryAddCharacter: true)) have.Add(glyph);
                else miss.Add(glyph);
            }

            var line = $"  glyphs: {have.Count}/{Probe.Length} present";
            if (miss.Count > 0)
                line += $" — MISSING {string.Join(" ", miss)}. If they are all missing, the render mode " +
                        "cannot read this font's bitmaps: switch to monochrome Noto Emoji at SMOOTH.";
            return line;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            var leaf = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
