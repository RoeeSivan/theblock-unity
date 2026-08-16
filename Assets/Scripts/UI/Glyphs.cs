using System.Text;

namespace TheBlock.UI
{
    /// <summary>
    /// Keeps emoji opaque and untinted inside coloured text.
    ///
    /// <b>This file used to do the opposite.</b> Until U28 it was <c>Glyphs.Strip</c>, deleting emoji
    /// because Unity's default font had none. The font landed, the stripping went, and the glyphs
    /// came back — <b>washed out</b>. That is the second half of the same problem, and it is not a
    /// font bug.
    ///
    /// <b>Why they look transparent.</b> A colour-emoji glyph is a little RGBA bitmap drawn through
    /// <c>Hidden/TextCore/Sprite</c>, and UI Toolkit hands that shader the ELEMENT's colour as vertex
    /// colour, which it multiplies. Every emoji-bearing surface in this game sits in a label whose
    /// colour is deliberately not opaque white:
    /// <list type="bullet">
    ///   <item><c>MenuStyle.Muted</c> — alpha <b>0.55</b> (the shop blurbs)</item>
    ///   <item><c>MenuStyle.Heading</c> — alpha <b>0.75</b> (the shop's 💵 balance)</item>
    ///   <item><c>MenuStyle.LockedInk</c> — alpha <b>0.5</b> (a row you cannot afford)</item>
    ///   <item><c>MenuStyle.SecondaryInk</c> — opaque, but PEACH, so 🍕 comes out peach</item>
    ///   <item><c>MenuStyle.AccentInk</c> — near-black; an emoji on a primary button all but vanishes</item>
    /// </list>
    /// So the glyphs are exactly as transparent as the text around them, which is correct behaviour
    /// for letters and wrong for pictures.
    ///
    /// <b>The fix is rich text, not a per-call-site colour.</b> Wrapping each pictographic run in
    /// <c>&lt;color=#FFFFFFFF&gt;</c> resets the vertex colour for those characters only, so the
    /// surrounding copy keeps its muting and the picture keeps its own colours — and it works on a
    /// MIXED string like <c>"1.  🍕 The Block Pizza Run"</c>, which splitting into two labels does
    /// not. UI Toolkit parses these tags whenever <c>TextElement.enableRichText</c> is on, which is
    /// its default.
    ///
    /// <b>The data is still untouched.</b> Applied at the point of DRAWING, exactly as the stripping
    /// was, and for the same reason: the config keeps its emoji, and undoing this is a deletion.
    /// </summary>
    public static class Glyphs
    {
        /// <summary>Opaque white, in the form TextCore's rich-text parser wants.</summary>
        private const string Open = "<color=#FFFFFFFF>";

        private const string Close = "</color>";

        /// <summary>
        /// Wraps every run of pictographic characters so it draws at full opacity in its own colours.
        ///
        /// Runs, not characters: a flag or a keycap is several codepoints, and one tag per codepoint
        /// would be correct but three times the string. Returns the input unchanged when there is
        /// nothing pictographic in it, so the common case allocates nothing.
        /// </summary>
        public static string Opaque(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var builder = (StringBuilder)null;
            var inRun = false;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                var wide = char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
                var codepoint = wide ? char.ConvertToUtf32(c, text[i + 1]) : c;
                var pictographic = IsPictographic(codepoint);

                if (pictographic && builder == null)
                {
                    // First hit: copy what came before it and start building.
                    builder = new StringBuilder(text.Length + 32);
                    builder.Append(text, 0, i);
                }

                if (builder != null && pictographic != inRun)
                {
                    builder.Append(pictographic ? Open : Close);
                    inRun = pictographic;
                }

                if (builder != null)
                {
                    builder.Append(c);
                    if (wide) builder.Append(text[i + 1]);
                }

                if (wide) i++;
            }

            if (builder == null) return text;
            if (inRun) builder.Append(Close);
            return builder.ToString();
        }

        /// <summary>
        /// The blocks that hold emoji and the pictographic dingbats — inherited verbatim from the
        /// stripping pass, where the same ranges decided what to delete.
        ///
        /// <b>Arrows (U+2190–21FF) are deliberately absent.</b> The dance track draws ← ↓ ↑ → as its
        /// actual note glyphs and they are meant to take the lane's colour, not fight it.
        /// </summary>
        private static bool IsPictographic(int c) =>
            c == 0x20E3 ||                      // combining keycap
            (c >= 0x2300 && c <= 0x23FF) ||     // misc technical — ⏰ lives here
            (c >= 0x2600 && c <= 0x27BF) ||     // misc symbols + dingbats (✅ ⛽ 🕺)
            (c >= 0x2B00 && c <= 0x2BFF) ||     // extra arrows and stars
            (c >= 0x1F000 && c <= 0x1FAFF) ||   // the emoji planes proper
            (c >= 0xFE00 && c <= 0xFE0F);       // variation selectors, the "render as emoji" marks
    }
}
