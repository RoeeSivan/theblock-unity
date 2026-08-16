using System.Collections.Generic;
using TheBlock.Core;
using TheBlock.Powerup;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheBlock.UI
{
    /// <summary>
    /// The power-up strip: what you are carrying, what is running, and how long it has left.
    ///
    /// <b>On the one shared document</b>, like everything else here — U14 built it, and the ledger's
    /// U25 row is explicit that a second <c>UIDocument</c> means a second <c>PanelSettings</c> and an
    /// argument about z-order.
    ///
    /// It sits under the cash readout on the right, because the two are the same subject: what you
    /// have, and what you spent it on. A chip appears when you own the item and lights when it is
    /// running; 🎒 reads ARMED rather than counting down, because it has no clock — it is spent by
    /// the next mission that pays.
    ///
    /// <b>The empty strip hides with <c>display</c>, and that is the opposite of what it looks like
    /// it should be.</b> <see cref="Menus.GameFlow.SetGameplayHudVisible"/> takes the whole gameplay
    /// HUD off the screen behind a menu using <c>visibility</c>, chosen precisely because no gameplay
    /// component writes that property — the radar toggle was silently broken once by a component that
    /// did. So <c>visibility</c> belongs to the menu layer and <c>display</c> belongs to the owner,
    /// and the two compose: a strip with nothing in it stays down behind a menu and after it.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class PowerUpChips : MonoBehaviour
    {
        [SerializeField] private PowerUps powerups;

        private VisualElement _strip;

        private readonly List<Chip> _chips = new();

        private struct Chip
        {
            public string Id;
            public bool Timed;
            public VisualElement Root;
            public Label Text;
        }

        private void Start()
        {
            if (powerups == null) powerups = FindAnyObjectByType<PowerUps>();
            Build();
            Repaint();

            if (powerups != null) powerups.Changed += Repaint;
        }

        private void OnDestroy()
        {
            if (powerups != null) powerups.Changed -= Repaint;
        }

        /// <summary>
        /// The countdowns need a frame; the counts do not. Stock changes arrive on
        /// <see cref="PowerUps.Changed"/>, so this only ever refreshes text that is already visible.
        /// </summary>
        private void Update()
        {
            if (powerups == null || _strip == null) return;
            if (_strip.style.display == DisplayStyle.None) return;

            foreach (var chip in _chips)
            {
                if (!chip.Timed || chip.Root.style.display == DisplayStyle.None) continue;
                var left = powerups.Remaining(chip.Id);
                if (left > 0f) chip.Text.text = Mathf.CeilToInt(left).ToString() + "s";
            }
        }

        private void Build()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;

            _strip = new VisualElement { name = "powerup-chips" };
            _strip.style.position = Position.Absolute;
            _strip.style.top = 88f;
            _strip.style.right = 24f;
            _strip.style.flexDirection = FlexDirection.Column;
            _strip.style.alignItems = Align.FlexEnd;
            root.Add(_strip);

            var catalogue = TheBlockConfig.Load()?.PowerUps;
            foreach (var item in catalogue?.Items ?? new List<TheBlockConfig.PowerUpSpec>())
                _strip.Add(BuildChip(item));
        }

        private VisualElement BuildChip(TheBlockConfig.PowerUpSpec item)
        {
            var chip = new VisualElement { name = $"chip-{item.Id}" };
            chip.style.flexDirection = FlexDirection.Row;
            chip.style.alignItems = Align.Center;
            chip.style.marginBottom = 4f;
            chip.style.paddingLeft = 8f;
            chip.style.paddingRight = 8f;
            chip.style.paddingTop = 3f;
            chip.style.paddingBottom = 3f;
            chip.style.display = DisplayStyle.None;
            Round(chip, 6f);

            var glyph = new Label(Glyphs.Opaque($"{item.Hotkey} {item.Emoji}"));
            glyph.style.fontSize = 15f;
            glyph.style.color = Color.white;
            chip.Add(glyph);

            var text = new Label(string.Empty);
            text.style.fontSize = 13f;
            text.style.marginLeft = 6f;
            text.style.minWidth = 34f;
            text.style.unityTextAlign = TextAnchor.MiddleRight;
            text.style.color = Color.white;
            chip.Add(text);

            _chips.Add(new Chip
            {
                Id = item.Id,
                Timed = item.DurationSec > 0f,
                Root = chip,
                Text = text,
            });

            return chip;
        }

        private void Repaint()
        {
            if (powerups == null) return;

            var anyVisible = false;
            foreach (var chip in _chips)
            {
                var owned = powerups.Count(chip.Id);
                var active = powerups.IsActive(chip.Id);
                var show = owned > 0 || active;

                chip.Root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) continue;
                anyVisible = true;

                // Through MenuStyle.Ui, because UI Toolkit takes a Color as LINEAR and these are
                // written as sRGB — the same conversion every colour in the menus goes through.
                chip.Root.style.backgroundColor = active
                    ? Menus.MenuStyle.Ui(1f, 0.58f, 0.28f, 0.85f)
                    : Menus.MenuStyle.Ui(0f, 0f, 0f, 0.45f);

                chip.Text.text = active
                    ? chip.Timed ? Mathf.CeilToInt(powerups.Remaining(chip.Id)) + "s" : "ARMED"
                    : "×" + owned;
            }

            _strip.style.display = anyVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void Round(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }
    }
}
