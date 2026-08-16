using System.Collections.Generic;
using TheBlock.Audio;
using TheBlock.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheBlock.UI.Menus
{
    /// <summary>
    /// The 7-Eleven counter - the port of <c>src/ui/shop-menu.ts</c>.
    ///
    /// Four rows, a balance, and a Done button. Rows are built ONCE from the catalogue and repainted
    /// on every open, which is the same idiom the mission list and the character roster use here: no
    /// element is ever created after <c>Awake</c>, so there is no path where a repaint leaks a row.
    ///
    /// <b>The scrim is translucent, like the pause menu's.</b> You are standing in the shop, and
    /// seeing the shop behind the counter is the difference between "shopping" and "left the game".
    ///
    /// <b>It owns no money rules.</b> The buy handler returns a bool and this asks for it - the
    /// wallet, the price and the stock all live outside, which is the division the web build made so
    /// a row could stay inert without the overlay knowing what an item costs.
    /// </summary>
    public class ShopMenu : MenuPanel
    {
        protected override string PanelName => "shop-menu";
        protected override bool Opaque => false;

        /// <summary>Wired by <see cref="GameFlow"/>, which owns the wallet and the stock.</summary>
        public System.Func<int> Balance;

        /// <summary>How many of this item the player already owns.</summary>
        public System.Func<string, int> Count;

        /// <summary>
        /// Buy one. Returns false when it did not happen, so the row can refuse without this class
        /// having to know the price rules.
        /// </summary>
        public System.Func<string, bool> OnBuy;

        public System.Action OnClose;

        private Label _balanceLabel;

        private readonly List<Row> _rows = new();

        private struct Row
        {
            public string Id;
            public int Price;
            public Button Button;
            public Label Owned;
        }

        protected override void BuildContent(VisualElement overlay)
        {
            overlay.Add(MenuStyle.Wordmark("7-Eleven", 40f));

            var column = MenuStyle.Column("shop-root", 10f);
            column.style.display = DisplayStyle.Flex;
            overlay.Add(column);

            _balanceLabel = MenuStyle.Stack(column, MenuStyle.Heading("💵 $0", 24f));

            var catalogue = TheBlockConfig.Load()?.PowerUps;
            if (catalogue?.Items == null || catalogue.Items.Count == 0)
            {
                MenuStyle.Stack(column, MenuStyle.Body(
                    "The catalogue is missing - re-run tools/export-config.sh."));
                MenuStyle.Stack(column, MenuStyle.Secondary("Done", Close));
                return;
            }

            foreach (var item in catalogue.Items) MenuStyle.Stack(column, BuildRow(item));

            MenuStyle.Stack(column, MenuStyle.Body(
                "1 - 4 uses what you bought, anywhere in the city.", 13f));
            MenuStyle.Stack(column, MenuStyle.Secondary("Done", Close));
        }

        /// <summary>
        /// One shelf. The button carries the whole row so a click lands anywhere on it - the web's
        /// rows are <c>&lt;button&gt;</c>s for exactly this reason.
        /// </summary>
        private VisualElement BuildRow(TheBlockConfig.PowerUpSpec item)
        {
            var button = MenuStyle.Secondary(string.Empty, () => Buy(item.Id));
            button.style.width = 460f;
            button.style.height = StyleKeyword.Auto;
            button.style.paddingTop = 10f;
            button.style.paddingBottom = 10f;
            button.style.paddingLeft = 14f;
            button.style.paddingRight = 14f;

            // A definite width above, because these children are laid out as a row and a percentage
            // against a content-sized parent resolves to nothing.
            var line = new VisualElement();
            line.style.flexDirection = FlexDirection.Row;
            line.style.alignItems = Align.Center;
            button.Add(line);

            // The row's ink is peach when affordable and half-transparent white when not, and both
            // would land on the item's picture. Opaque keeps the hotkey digit in the row's colour
            // and the emoji in its own.
            var emoji = new Label(Glyphs.Opaque($"{item.Hotkey}  {item.Emoji}"));
            emoji.style.fontSize = 20f;
            emoji.style.width = 74f;
            emoji.style.unityTextAlign = TextAnchor.MiddleLeft;
            line.Add(emoji);

            var text = new VisualElement();
            text.style.flexGrow = 1f;
            text.style.flexDirection = FlexDirection.Column;
            line.Add(text);

            var label = new Label(item.Label);
            label.style.fontSize = 17f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            text.Add(label);

            var blurb = new Label(item.Blurb);
            blurb.style.fontSize = 13f;
            blurb.style.color = MenuStyle.Muted;
            blurb.style.whiteSpace = WhiteSpace.Normal;
            blurb.style.unityTextAlign = TextAnchor.MiddleLeft;
            text.Add(blurb);

            var right = new VisualElement();
            right.style.width = 96f;
            right.style.flexDirection = FlexDirection.Column;
            line.Add(right);

            var price = new Label($"${item.Price}");
            price.style.fontSize = 18f;
            price.style.unityFontStyleAndWeight = FontStyle.Bold;
            price.style.unityTextAlign = TextAnchor.MiddleRight;
            right.Add(price);

            var owned = new Label(string.Empty);
            owned.style.fontSize = 12f;
            owned.style.color = MenuStyle.Muted;
            owned.style.unityTextAlign = TextAnchor.MiddleRight;
            right.Add(owned);

            _rows.Add(new Row { Id = item.Id, Price = item.Price, Button = button, Owned = owned });
            return button;
        }

        private void Buy(string id)
        {
            if (OnBuy == null || !OnBuy(id)) return;
            GameAudio.Cue(SfxCue.Purchase);
            Paint();
        }

        /// <summary>Repaints from live state. Called on open and after every purchase.</summary>
        private void Paint()
        {
            var cash = Balance != null ? Balance() : 0;
            // Heading ink is white at alpha 0.75, which would draw a 💵 three-quarters faded.
            if (_balanceLabel != null) _balanceLabel.text = Glyphs.Opaque($"💵 ${cash}");

            foreach (var row in _rows)
            {
                var owned = Count != null ? Count(row.Id) : 0;
                if (row.Owned != null) row.Owned.text = owned > 0 ? $"owned {owned}" : string.Empty;

                // SetEnabled rather than a flag the handler checks: a disabled element never receives
                // the click, so there is no second place for the lock state to be wrong.
                if (cash < row.Price) MenuStyle.PaintLocked(row.Button);
                else MenuStyle.Paint(row.Button, primary: false);
            }
        }

        public void Open()
        {
            EnsureBuilt();
            Paint();
            Show();
        }

        public void Close()
        {
            Hide();
            OnClose?.Invoke();
        }
    }
}
