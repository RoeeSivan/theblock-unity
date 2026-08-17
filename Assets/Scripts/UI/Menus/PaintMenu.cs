using System.Collections.Generic;
using TheBlock.Audio;
using TheBlock.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheBlock.UI.Menus
{
    /// <summary>
    /// The auto shop's colour menu - U35g. Ten swatches, a balance, the price, Done.
    ///
    /// The <see cref="ShopMenu"/> idiom exactly: swatches are built ONCE from
    /// <see cref="AutoShopSpec.Palette"/> and repainted on every open, the scrim is translucent so the
    /// shop and the car you are about to paint stay on screen, and <b>it owns no money rules</b> -
    /// the pick handler returns a bool and this asks for it. The wallet, the price and the car live
    /// in <see cref="GameFlow"/>.
    ///
    /// <b>A pick closes the menu.</b> The user's flow: press C, click a colour, the car is painted,
    /// that is it - C again for another. The swatch you already wear is outlined and never locked,
    /// so a broke player can still see what they have; every other swatch locks when the balance is
    /// under the price, by <c>SetEnabled(false)</c> so the click never arrives.
    /// </summary>
    public class PaintMenu : MenuPanel
    {
        protected override string PanelName => "paint-menu";
        protected override bool Opaque => false;

        /// <summary>Wired by <see cref="GameFlow"/>, which owns the wallet and the vehicle.</summary>
        public System.Func<int> Balance;

        /// <summary>The colour the vehicle wears now, packed <c>0xRRGGBB</c>, or -1 if not a shop colour.</summary>
        public System.Func<int> CurrentHex;

        /// <summary>Paint this colour. Returns false when it did not happen, so the menu stays up.</summary>
        public System.Func<int, bool> OnPick;

        public System.Action OnClose;

        private Label _balanceLabel;
        private readonly List<Swatch> _swatches = new();

        private struct Swatch
        {
            public int Hex;
            public Button Button;
        }

        private const float SwatchWidth = 92f;
        private const float SwatchHeight = 60f;
        private const int PerRow = 5;

        protected override void BuildContent(VisualElement overlay)
        {
            overlay.Add(MenuStyle.Wordmark("Auto Shop", 40f));

            var column = MenuStyle.Column("paint-root", 12f);
            column.style.display = DisplayStyle.Flex;
            overlay.Add(column);

            _balanceLabel = MenuStyle.Stack(column, MenuStyle.Heading("💵 $0", 24f));

            var palette = AutoShopSpec.Palette;
            VisualElement row = null;
            for (int i = 0; i < palette.Length; i++)
            {
                if (i % PerRow == 0)
                {
                    row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.justifyContent = Justify.Center;
                    MenuStyle.Stack(column, row);
                }

                row.Add(BuildSwatch(palette[i].Hex, palette[i].Name));
            }

            MenuStyle.Stack(column, MenuStyle.Body(
                $"${AutoShopSpec.PaintPrice} a coat. Pick a color and drive off in it.", 13f));
            MenuStyle.Stack(column, MenuStyle.Secondary("Done", Close));
        }

        /// <summary>
        /// One swatch: a button whose fill IS the colour. The name sits on it in whichever ink reads
        /// against that fill, and the fill goes through <see cref="MenuStyle.Ui"/> because UI Toolkit
        /// takes its colours as linear.
        /// </summary>
        private Button BuildSwatch(int hex, string label)
        {
            var srgb = Core.TheBlockConfig.ColorFromHex(hex);
            var fill = MenuStyle.Ui(srgb.r, srgb.g, srgb.b);
            float luma = 0.2126f * srgb.r + 0.7152f * srgb.g + 0.0722f * srgb.b;
            var ink = luma > 0.6f ? MenuStyle.Ui(0.08f, 0.08f, 0.1f) : MenuStyle.Ui(1f, 1f, 1f);

            var button = MenuStyle.Secondary(label, () => Pick(hex));
            button.style.width = SwatchWidth;
            button.style.height = SwatchHeight;
            button.style.marginLeft = 5f;
            button.style.marginRight = 5f;
            button.style.paddingLeft = 4f;
            button.style.paddingRight = 4f;
            button.style.paddingTop = 0f;
            button.style.paddingBottom = 0f;
            button.style.fontSize = 14f;
            button.style.unityTextAlign = TextAnchor.LowerCenter;
            button.style.backgroundColor = fill;
            button.style.color = ink;

            _swatches.Add(new Swatch { Hex = hex, Button = button });
            return button;
        }

        private void Pick(int hex)
        {
            if (OnPick == null || !OnPick(hex))
            {
                GameAudio.Cue(SfxCue.Deny);
                return;
            }

            GameAudio.Cue(SfxCue.Purchase);
            Close();
        }

        /// <summary>Repaints from live state. Called on open.</summary>
        private void Paint()
        {
            var cash = Balance != null ? Balance() : 0;
            var current = CurrentHex != null ? CurrentHex() : -1;
            if (_balanceLabel != null) _balanceLabel.text = Glyphs.Opaque($"💵 ${cash}");

            foreach (var swatch in _swatches)
            {
                bool wearing = swatch.Hex == current;
                bool locked = !wearing && cash < AutoShopSpec.PaintPrice;

                // The outline is the "you are here"; the lock is a dim plus SetEnabled(false), never a
                // recolour - the swatch's fill is the whole point of it.
                MenuStyle.SetBorder(swatch.Button, wearing ? MenuStyle.Ui(1f, 1f, 1f) : Color.clear, wearing ? 3f : 0f, 8f);
                swatch.Button.style.opacity = locked ? 0.35f : 1f;
                swatch.Button.SetEnabled(!locked);
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
