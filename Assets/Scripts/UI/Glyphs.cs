using System.Text;

namespace TheBlock.UI
{
    /// <summary>
    /// Strips emoji out of HUD text — a stop-gap with a known end date.
    ///
    /// The campaign's copy leads with emoji ("🍕 Drive to the pizzeria", "🏆 Campaign complete!")
    /// because the web build renders it in a browser, where the system emoji font is always there.
    /// Unity's default runtime font has no emoji at all, so every one of them draws as a blank box.
    /// U14 already hit this from the other side and dropped the map's ⛽/🚓/🏪 pin glyphs for the
    /// same reason; its note says an emoji-capable font is U25's to add.
    ///
    /// So the words are kept and the boxes are dropped. <b>The DATA is untouched</b> — the config
    /// still carries the emoji, the map POIs still carry theirs, and when U25 ships a font with
    /// emoji coverage this whole file is deleted and one call site each stops using it. Stripping at
    /// the point of DRAWING rather than at the point of loading is what makes that a deletion rather
    /// than a re-import.
    /// </summary>
    public static class Glyphs
    {
        /// <summary>
        /// Drops emoji and the space that followed them. Ordinary punctuation, accented Latin and
        /// the copy's em-dashes and arrows all survive — only the pictographic blocks go.
        /// </summary>
        public static string Strip(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var builder = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                // Emoji above the BMP arrive as a surrogate pair. Test the pair's real codepoint,
                // then skip both halves — writing one half alone produces a lone surrogate, which is
                // a different kind of broken glyph.
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    var codepoint = char.ConvertToUtf32(c, text[i + 1]);
                    i++;
                    if (IsPictographic(codepoint)) { SkipFollowingSpace(text, ref i, builder); continue; }
                    builder.Append(c).Append(text[i]);
                    continue;
                }

                if (IsPictographic(c)) { SkipFollowingSpace(text, ref i, builder); continue; }
                builder.Append(c);
            }

            return builder.ToString().Trim();
        }

        /// <summary>
        /// Eats the single space an emoji was separated by, so "🍕 Drive" becomes "Drive" rather
        /// than " Drive" — a leading space is visible in a centred label.
        /// </summary>
        private static void SkipFollowingSpace(string text, ref int i, StringBuilder builder)
        {
            if (i + 1 < text.Length && text[i + 1] == ' ' && builder.Length == 0) i++;
        }

        /// <summary>
        /// The blocks that hold emoji and the pictographic dingbats.
        ///
        /// Deliberately a coarse test: this only ever runs over the campaign's own copy, so a false
        /// positive is impossible to hit and precision would be cost with no benefit.
        ///
        /// <b>Arrows (U+2190–21FF) are deliberately absent from this list.</b> The dance track draws
        /// ← ↓ ↑ → as its actual note glyphs, Unity's default font has them, and stripping them
        /// would leave the rhythm game with four blank lanes.
        /// </summary>
        private static bool IsPictographic(int c) =>
            c == 0x20E3 ||                      // combining keycap
            (c >= 0x2300 && c <= 0x23FF) ||     // misc technical — this is where ⏰ lives, and it
                                                // was the one the first pass missed: every fail line
                                                // in the campaign opens with it
            (c >= 0x2600 && c <= 0x27BF) ||     // misc symbols + dingbats (✅ ⛽ 🕺)
            (c >= 0x2B00 && c <= 0x2BFF) ||     // extra arrows and stars
            (c >= 0x1F000 && c <= 0x1FAFF) ||   // the emoji planes proper
            (c >= 0xFE00 && c <= 0xFE0F);       // variation selectors, the "render as emoji" marks
    }
}
