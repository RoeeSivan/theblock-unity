using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheBlock.Minigame.Rhythm
{
    /// <summary>
    /// The falling-arrow track - the port of <c>src/minigame/rhythm/notes-ui.ts</c>, from a DOM
    /// overlay onto the HUD's existing UIDocument.
    ///
    /// <b>One horizontal lane, not four.</b> That is the web build's design and it is what makes the
    /// game readable: arrows scroll in from the right, the ring sits at 10% from the left, and the
    /// note AT the ring is whichever one is nearest in time - so the player watches one place and
    /// presses the arrow they see, rather than tracking four columns at once.
    ///
    /// Notes are pooled. A three-phase chart is ~150 arrows over 100 seconds and only a handful are
    /// on screen at a time, so allocating a <see cref="Label"/> per note would churn the UI's layout
    /// for no reason.
    /// </summary>
    public class NotesTrack
    {
        private const float NoteSize = 64f;

        private readonly VisualElement _root;
        private readonly VisualElement _track;
        private readonly VisualElement _ring;
        private readonly Label _score;
        private readonly Label _combo;
        private readonly Label _judgment;
        private readonly Label _count;

        private readonly Dictionary<int, Label> _live = new();
        private readonly Stack<Label> _pool = new();

        private float _ringPct;
        private float _judgmentLeft;
        private float _ringFlash;

        public NotesTrack(VisualElement parent, float ringPct)
        {
            _ringPct = ringPct;

            _root = new VisualElement { name = "rhythm-ui" };
            _root.style.position = Position.Absolute;
            _root.style.left = 0f;
            _root.style.right = 0f;
            _root.style.bottom = 0f;
            _root.style.top = 0f;
            _root.style.display = DisplayStyle.None;
            _root.pickingMode = PickingMode.Ignore;

            // The lane. Low on the screen so it never covers the dancer, who is the actual show.
            _track = new VisualElement { name = "rhythm-track" };
            _track.style.position = Position.Absolute;
            _track.style.left = 0f;
            _track.style.right = 0f;
            _track.style.bottom = 60f;
            _track.style.height = 96f;
            _track.style.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
            _track.pickingMode = PickingMode.Ignore;
            _root.Add(_track);

            // The hit zone. A note is on the beat when it is inside this.
            _ring = new VisualElement { name = "rhythm-ring" };
            _ring.style.position = Position.Absolute;
            _ring.style.width = NoteSize + 16f;
            _ring.style.height = NoteSize + 16f;
            _ring.style.top = 8f;
            _ring.style.left = new Length(_ringPct, LengthUnit.Percent);
            _ring.style.translate = new Translate(new Length(-50f, LengthUnit.Percent), 0f);
            Round(_ring, (NoteSize + 16f) * 0.5f);
            Border(_ring, new Color(1f, 1f, 1f, 0.75f), 3f);
            _ring.pickingMode = PickingMode.Ignore;
            _track.Add(_ring);

            _score = Text(28f, Color.white, TextAnchor.MiddleLeft);
            _score.style.position = Position.Absolute;
            _score.style.left = 24f;
            _score.style.top = 96f;
            _score.text = "0";
            _root.Add(_score);

            _combo = Text(22f, new Color(1f, 0.85f, 0.35f), TextAnchor.MiddleLeft);
            _combo.style.position = Position.Absolute;
            _combo.style.left = 24f;
            _combo.style.top = 130f;
            _root.Add(_combo);

            // The judgment word pops at the ring, not in a corner - feedback belongs where the eye
            // already is.
            _judgment = Text(34f, Color.white, TextAnchor.MiddleCenter);
            _judgment.style.position = Position.Absolute;
            _judgment.style.left = new Length(_ringPct, LengthUnit.Percent);
            _judgment.style.bottom = 170f;
            _judgment.style.translate = new Translate(new Length(-50f, LengthUnit.Percent), 0f);
            _judgment.style.display = DisplayStyle.None;
            _root.Add(_judgment);

            // The countdown, and then the result card, share this one big centred label.
            _count = Text(64f, Color.white, TextAnchor.MiddleCenter);
            _count.style.position = Position.Absolute;
            _count.style.left = 0f;
            _count.style.right = 0f;
            _count.style.top = new Length(38f, LengthUnit.Percent);
            _count.style.display = DisplayStyle.None;
            _root.Add(_count);

            parent.Add(_root);
        }

        private static Label Text(float size, Color color, TextAnchor align)
        {
            var label = new Label(string.Empty);
            label.style.fontSize = size;
            label.style.color = color;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.unityTextAlign = align;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.pickingMode = PickingMode.Ignore;
            label.style.textShadow = new TextShadow
            {
                offset = new Vector2(0f, 2f),
                blurRadius = 6f,
                color = new Color(0f, 0f, 0f, 0.9f),
            };
            return label;
        }

        private static void Round(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r;
            e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r;
            e.style.borderBottomRightRadius = r;
        }

        private static void Border(VisualElement e, Color c, float w)
        {
            e.style.borderTopColor = c;
            e.style.borderBottomColor = c;
            e.style.borderLeftColor = c;
            e.style.borderRightColor = c;
            e.style.borderTopWidth = w;
            e.style.borderBottomWidth = w;
            e.style.borderLeftWidth = w;
            e.style.borderRightWidth = w;
        }

        public void Show() => _root.style.display = DisplayStyle.Flex;

        public void Hide()
        {
            _root.style.display = DisplayStyle.None;
            HideCount();
        }

        /// <summary>Clears every live note and zeroes the readouts. A fresh run starts here.</summary>
        public void Reset()
        {
            foreach (var pair in _live) Recycle(pair.Value);
            _live.Clear();
            SetScore(0);
            SetCombo(0);
            _judgment.style.display = DisplayStyle.None;
        }

        public void AddNote(int id, Direction dir)
        {
            if (_live.ContainsKey(id)) return;

            var note = _pool.Count > 0 ? _pool.Pop() : NewNote();
            note.text = RhythmScoring.Glyph(dir);
            note.style.display = DisplayStyle.Flex;
            _live[id] = note;
        }

        private Label NewNote()
        {
            var note = Text(40f, Color.white, TextAnchor.MiddleCenter);
            note.style.position = Position.Absolute;
            note.style.width = NoteSize;
            note.style.height = NoteSize;
            note.style.top = 16f;
            note.style.backgroundColor = new Color(0.13f, 0.15f, 0.2f, 0.9f);
            Round(note, NoteSize * 0.5f);
            Border(note, new Color(1f, 1f, 1f, 0.35f), 2f);
            _track.Add(note);
            return note;
        }

        /// <summary>
        /// Positions a note. <paramref name="fraction"/> is 1 at the right spawn edge and 0 at the
        /// ring, so the arrow travels the same distance whatever the tempo - the ramp changes
        /// density, never speed, and this is where that promise is kept.
        /// </summary>
        public void MoveNote(int id, float fraction)
        {
            if (!_live.TryGetValue(id, out var note)) return;
            var pct = Mathf.Lerp(_ringPct, 100f, Mathf.Clamp01(fraction));
            note.style.left = new Length(pct, LengthUnit.Percent);
            note.style.translate = new Translate(new Length(-50f, LengthUnit.Percent), 0f);
        }

        public void RemoveNote(int id)
        {
            if (!_live.TryGetValue(id, out var note)) return;
            _live.Remove(id);
            Recycle(note);
        }

        private void Recycle(Label note)
        {
            note.style.display = DisplayStyle.None;
            _pool.Push(note);
        }

        public void SetScore(int points) => _score.text = points.ToString();

        public void SetCombo(int combo)
        {
            _combo.text = combo >= 2 ? $"{combo}x" : string.Empty;
            _combo.style.display = combo >= 2 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void ShowJudgment(Judgment j)
        {
            _judgment.text = j switch
            {
                Judgment.Perfect => "PERFECT",
                Judgment.Good => "GOOD",
                _ => "MISS",
            };
            _judgment.style.color = j switch
            {
                Judgment.Perfect => new Color(0.4f, 1f, 0.6f),
                Judgment.Good => new Color(1f, 0.9f, 0.4f),
                _ => new Color(1f, 0.4f, 0.4f),
            };
            _judgment.style.display = DisplayStyle.Flex;
            _judgmentLeft = 0.45f;
        }

        public void FlashRing() => _ringFlash = 0.15f;

        /// <summary>"3 · 2 · 1 · GO!", and afterwards the result card, on the same centred label.</summary>
        public void ShowCount(string text, bool emphasis)
        {
            _count.text = text;
            _count.style.fontSize = emphasis ? 84f : 64f;
            _count.style.color = emphasis ? new Color(0.4f, 1f, 0.6f) : Color.white;
            _count.style.display = DisplayStyle.Flex;
        }

        public void ShowResult(string text)
        {
            _count.text = text;
            _count.style.fontSize = 34f;
            _count.style.color = Color.white;
            _count.style.display = DisplayStyle.Flex;
        }

        public void HideCount() => _count.style.display = DisplayStyle.None;

        /// <summary>
        /// The two self-expiring flourishes. Driven from the mission's own Update on UNSCALED time,
        /// because they are about the interface, not the simulation.
        /// </summary>
        public void Tick(float unscaledDt)
        {
            if (_judgmentLeft > 0f)
            {
                _judgmentLeft -= unscaledDt;
                if (_judgmentLeft <= 0f) _judgment.style.display = DisplayStyle.None;
            }

            if (_ringFlash <= 0f) return;
            _ringFlash -= unscaledDt;
            var k = Mathf.Clamp01(_ringFlash / 0.15f);
            var white = new Color(1f, 1f, 1f, 0.75f);
            var hot = new Color(0.4f, 1f, 0.6f, 1f);
            Border(_ring, Color.Lerp(white, hot, k), 3f + k * 3f);
        }
    }
}
