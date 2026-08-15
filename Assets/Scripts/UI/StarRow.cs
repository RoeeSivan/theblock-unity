using UnityEngine;
using UnityEngine.UIElements;

namespace TheBlock.UI
{
    /// <summary>
    /// The wanted stars, drawn with Painter2D.
    ///
    /// <b>The partial star is the point.</b> A row of lit/unlit stars would show the same thing the
    /// web build shows, and it would hide the whole change U19 makes: heat here is continuous, so a
    /// wall scrape moves the meter by a few hundredths and a body under the wheels moves it by a
    /// star. Drawing the fraction is what lets a player see that scraping a wall costs nothing —
    /// which is the actual fix for "the police are too sensitive", made visible rather than merely
    /// true.
    ///
    /// Painter2D rather than a font, for U14's reason: there is no emoji font in the project and the
    /// glyph pass is U25's.
    /// </summary>
    public class StarRow : VisualElement
    {
        private const float StarSize = 26f;
        private const float Gap = 6f;

        private static readonly Color Lit = new(1f, 0.92f, 0.10f);
        private static readonly Color Empty = new(1f, 1f, 1f, 0.18f);
        private static readonly Color Rim = new(0f, 0f, 0f, 0.55f);
        private static readonly Color Cooling = new(0.35f, 0.75f, 1f);

        private int _max = 3;
        private int _stars;
        private float _fraction;
        private bool _cooling;

        public StarRow()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        /// <summary>
        /// Repaints only when the picture would change — the fraction is compared at 1/100th of a
        /// star, which is finer than a pixel of fill and far coarser than every frame.
        /// </summary>
        public void Set(int max, int stars, float fraction, bool cooling)
        {
            if (_max == max && _stars == stars && _cooling == cooling &&
                Mathf.Abs(_fraction - fraction) < 0.01f) return;

            _max = max;
            _stars = stars;
            _fraction = fraction;
            _cooling = cooling;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            var painter = context.painter2D;

            for (int i = 0; i < _max; i++)
            {
                float x = i * (StarSize + Gap) + StarSize * 0.5f;
                var centre = new Vector2(x, StarSize * 0.5f + 2f);

                bool lit = i < _stars;
                float fill = lit ? 1f : i == _stars ? _fraction : 0f;

                // The dim star always, then the lit one INSIDE it at a fraction of the size.
                //
                // Growing rather than filling from the bottom, because Painter2D has no clip: a
                // rectangle and a star in one path can only be combined by winding rule, and every
                // rule gives the union or the difference — never the intersection, which is the one
                // wanted. A star that grows reads as a meter just as well and is honest about what
                // it draws.
                Trace(painter, centre, StarSize * 0.5f);
                painter.fillColor = Empty;
                painter.Fill();

                if (fill > 0.02f)
                {
                    Trace(painter, centre, StarSize * 0.5f * fill);
                    painter.fillColor = _cooling && !lit ? Cooling : Lit;
                    painter.Fill();
                }

                Trace(painter, centre, StarSize * 0.5f);
                painter.strokeColor = Rim;
                painter.lineWidth = 1.5f;
                painter.Stroke();
            }
        }

        /// <summary>A five-pointed star as a path, points up.</summary>
        private static void Trace(Painter2D painter, Vector2 centre, float radius)
        {
            painter.BeginPath();
            for (int i = 0; i < 10; i++)
            {
                float r = i % 2 == 0 ? radius : radius * 0.42f;
                float angle = -Mathf.PI * 0.5f + i * Mathf.PI / 5f;
                var point = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
                if (i == 0) painter.MoveTo(point);
                else painter.LineTo(point);
            }

            painter.ClosePath();
        }
    }
}
