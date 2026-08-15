using System.Collections.Generic;
using TheBlock.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheBlock.UI
{
    /// <summary>
    /// The vector overlay on top of the map's live render: district outlines, POI dots, the player
    /// arrow — the drawing half of <c>src/ui/map.ts</c>, as a UI Toolkit element.
    ///
    /// Shapes are painted in <c>generateVisualContent</c> with Painter2D; labels are pooled child
    /// <see cref="Label"/>s because Painter2D has no text. The web build's greedy first-come label
    /// guard ports as-is: districts claim space before POI names, and a label that would overlap an
    /// earlier one is dropped rather than stacked.
    ///
    /// The world→panel transform mirrors <see cref="MapCamera"/>'s orientation exactly — screen
    /// right is world −X, screen down is world +Z — so the overlay lands on the pixels the camera
    /// rendered underneath it.
    /// </summary>
    public class MapView : VisualElement
    {
        private readonly TheBlockConfig.MapSpec _spec;

        private readonly Color _districtStroke;
        private readonly Color _labelColor;
        private readonly Color _labelText;
        private readonly Color _labelPill;
        private readonly Color _playerColor;
        private readonly Dictionary<MapPoiKind, Color> _poiColors;

        private readonly List<Label> _labelPool = new();

        private Vector2 _center;
        private float _orthoSize = 1f;
        private Vector2 _playerWorld;
        private Vector2 _playerScreenDir = new Vector2(0f, -1f);
        private bool _expanded;

        public MapView(TheBlockConfig.MapSpec spec)
        {
            _spec = spec;
            pickingMode = PickingMode.Ignore;

            _districtStroke = TheBlockConfig.ColorFromCss(spec?.DistrictStroke, new Color(0.82f, 0.89f, 0.71f, 0.85f));
            _labelColor = TheBlockConfig.ColorFromCss(spec?.LabelColor, new Color(1f, 1f, 1f, 0.92f));
            _labelText = TheBlockConfig.ColorFromCss(spec?.LabelText, new Color(0f, 0f, 0f, 0.95f));
            _labelPill = TheBlockConfig.ColorFromCss(spec?.LabelPill, new Color(1f, 1f, 1f, 0.90f));
            _playerColor = TheBlockConfig.ColorFromCss(spec?.Player, new Color(1f, 0.82f, 0.29f));

            Color Poi(string key, Color fallback) =>
                spec?.PoiColors != null && spec.PoiColors.TryGetValue(key, out var css)
                    ? TheBlockConfig.ColorFromCss(css, fallback)
                    : fallback;

            _poiColors = new Dictionary<MapPoiKind, Color>
            {
                { MapPoiKind.Pizza, Poi("pizza", new Color(0.91f, 0.31f, 0.31f)) },
                { MapPoiKind.Vehicle, Poi("vehicle", new Color(0.29f, 0.61f, 0.91f)) },
                { MapPoiKind.Spawn, Poi("spawn", new Color(0.60f, 0.63f, 0.65f)) },
                { MapPoiKind.Marker, Poi("marker", new Color(0.26f, 0.77f, 0.39f)) },
            };

            generateVisualContent += Draw;
        }

        /// <summary>Called once per map repaint with the camera's current framing.</summary>
        public void SetFrame(Vector2 worldCenter, float orthoSize, Transform active, bool expanded)
        {
            _center = worldCenter;
            _orthoSize = Mathf.Max(orthoSize, 0.01f);
            _playerWorld = new Vector2(active.position.x, active.position.z);

            // Heading: the anchor's +Z is its direction of travel. On screen, world −X is right and
            // world +Z is down, so forward (fx, fz) becomes screen (−fx, +fz).
            var f = active.forward;
            _playerScreenDir = new Vector2(-f.x, f.z);
            if (_playerScreenDir.sqrMagnitude < 1e-6f) _playerScreenDir = new Vector2(0f, -1f);

            _expanded = expanded;
            RefreshLabels();
            MarkDirtyRepaint();
        }

        private Vector2 ToPanel(float wx, float wz)
        {
            var size = Mathf.Min(resolvedStyle.width, resolvedStyle.height);
            var scale = size / (2f * _orthoSize); // px per metre
            return new Vector2(
                size / 2f - (wx - _center.x) * scale,
                size / 2f + (wz - _center.y) * scale);
        }

        private void Draw(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D;

            // District outlines. Outline-only (no fill): the live render underneath IS the district.
            p.strokeColor = _districtStroke;
            p.lineWidth = 1.5f;
            foreach (var d in MapRegistry.Districts)
            {
                var a = ToPanel(d.Bounds.min.x, d.Bounds.min.z);
                var b = ToPanel(d.Bounds.max.x, d.Bounds.max.z);
                p.BeginPath();
                p.MoveTo(new Vector2(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y)));
                p.LineTo(new Vector2(Mathf.Max(a.x, b.x), Mathf.Min(a.y, b.y)));
                p.LineTo(new Vector2(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)));
                p.LineTo(new Vector2(Mathf.Min(a.x, b.x), Mathf.Max(a.y, b.y)));
                p.ClosePath();
                p.Stroke();
            }

            // POI dots.
            foreach (var poi in MapRegistry.Pois)
            {
                var at = ToPanel(poi.Position.x, poi.Position.z);
                p.BeginPath();
                p.Arc(at, 4f, 0f, Angle.Degrees(360f));
                p.fillColor = _poiColors[poi.Kind];
                p.Fill();
                p.strokeColor = new Color(0f, 0f, 0f, 0.6f);
                p.lineWidth = 1.5f;
                p.Stroke();
            }

            // Player triangle, last so it wins. Centred on the minimap by construction (the camera
            // is framed around the player), at its world spot on the full map.
            var me = ToPanel(_playerWorld.x, _playerWorld.y);
            var dir = _playerScreenDir.normalized;
            var side = new Vector2(-dir.y, dir.x);
            const float sizePx = 7f;
            p.BeginPath();
            p.MoveTo(me + dir * sizePx);
            p.LineTo(me - dir * (sizePx * 0.7f) + side * (sizePx * 0.6f));
            p.LineTo(me - dir * (sizePx * 0.7f) - side * (sizePx * 0.6f));
            p.ClosePath();
            p.fillColor = _playerColor;
            p.Fill();
            p.strokeColor = new Color(0f, 0f, 0f, 0.7f);
            p.lineWidth = 1.5f;
            p.Stroke();
        }

        // --- labels ------------------------------------------------------------------------------

        /// <summary>
        /// Greedy first-come-first-served label placement, rebuilt per repaint — the web build's
        /// guard. Districts are placed before POI names, so a district pill always beats a POI label
        /// contesting the same pixels; among equals, earlier registration wins.
        /// </summary>
        private readonly List<Rect> _claimed = new();

        private bool Claim(Rect r)
        {
            foreach (var other in _claimed)
                if (r.Overlaps(other)) return false;
            _claimed.Add(r);
            return true;
        }

        private void RefreshLabels()
        {
            var used = 0;
            _claimed.Clear();

            if (_expanded)
            {
                // The web scales label text with the canvas; 740px is where scale = 1.
                var size = Mathf.Min(resolvedStyle.width, resolvedStyle.height);
                var scale = Mathf.Clamp(size / 740f, 0.8f, 1.4f);

                foreach (var d in MapRegistry.Districts)
                {
                    var a = ToPanel(d.Bounds.min.x, d.Bounds.min.z);
                    var b = ToPanel(d.Bounds.max.x, d.Bounds.max.z);
                    var top = Mathf.Min(a.y, b.y);
                    var cx = (a.x + b.x) / 2f;
                    PlacePill(ref used, d.Name, cx, top, scale);
                }

                foreach (var poi in MapRegistry.Pois)
                {
                    if (poi.Minor) continue;
                    var at = ToPanel(poi.Position.x, poi.Position.z);
                    PlaceName(ref used, poi.Name, at, scale);
                }
            }

            for (var i = used; i < _labelPool.Count; i++)
                _labelPool[i].style.display = DisplayStyle.None;
        }

        /// <summary>A pin with no name draws its dot and nothing else.</summary>
        private static bool Nameless(string text) => string.IsNullOrEmpty(text);

        private Label NextLabel(ref int used)
        {
            if (used == _labelPool.Count)
            {
                var label = new Label();
                label.pickingMode = PickingMode.Ignore;
                label.style.position = Position.Absolute;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                _labelPool.Add(label);
                Add(label);
            }

            var next = _labelPool[used++];
            next.style.display = DisplayStyle.Flex;
            return next;
        }

        /// <summary>Black-on-white district pill hugging the box's top edge.</summary>
        private void PlacePill(ref int used, string text, float cx, float top, float scale)
        {
            if (Nameless(text)) return;
            var fontPx = Mathf.Round(12f * scale);
            var padX = 6f * scale;
            var padY = 3f * scale;
            // Panel fonts are not monospace, so the width is an estimate; the guard only needs it
            // to be roughly right, and a few px of slack errs toward dropping rather than stacking.
            var w = text.Length * fontPx * 0.62f + padX * 2f;
            var h = fontPx + padY * 2f;
            var pillTop = Mathf.Max(top - 4f - h, 2f);
            var rect = new Rect(cx - w / 2f, pillTop, w, h);
            if (!Claim(rect)) return;

            var label = NextLabel(ref used);
            label.text = text;
            label.style.left = rect.x;
            label.style.top = rect.y;
            label.style.width = rect.width;
            label.style.height = rect.height;
            label.style.fontSize = fontPx;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = _labelText;
            label.style.backgroundColor = _labelPill;
            label.style.borderTopLeftRadius = label.style.borderTopRightRadius = 4f;
            label.style.borderBottomLeftRadius = label.style.borderBottomRightRadius = 4f;
        }

        /// <summary>Plain POI name to the right of its dot.</summary>
        private void PlaceName(ref int used, string text, Vector2 at, float scale)
        {
            if (Nameless(text)) return;
            var fontPx = Mathf.Round(11f * scale);
            var w = text.Length * fontPx * 0.62f;
            var rect = new Rect(at.x + 7f, at.y - fontPx / 2f - 2f, w, fontPx + 4f);
            if (!Claim(rect)) return;

            var label = NextLabel(ref used);
            label.text = text;
            label.style.left = rect.x;
            label.style.top = rect.y;
            label.style.width = StyleKeyword.Auto;
            label.style.height = rect.height;
            label.style.fontSize = fontPx;
            label.style.unityFontStyleAndWeight = FontStyle.Normal;
            label.style.color = _labelColor;
            label.style.backgroundColor = Color.clear;
        }
    }
}
