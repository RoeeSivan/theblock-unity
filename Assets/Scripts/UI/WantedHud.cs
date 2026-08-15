using TheBlock.Police;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheBlock.UI
{
    /// <summary>
    /// The wanted stars and the BUSTED screen, on the panel that already exists.
    ///
    /// <b>One UIDocument, always.</b> U14 built the HUD panel for the map and the ledger's U25 row
    /// says to extend it rather than add a second — two panels means two PanelSettings, two scale
    /// modes and a z-order argument nobody wins.
    ///
    /// This is the minimum U19 needs to be play-testable, and no more: the typography, the emoji POI
    /// glyphs U14 still owes, the cash readout and the radar toggle are all U25's.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class WantedHud : MonoBehaviour
    {
        [SerializeField] private Heat heat;
        [SerializeField] private TheBlock.Game.Wallet wallet;

        private StarRow _stars;
        private VisualElement _bustOverlay;
        private Label _bustLabel;
        private Label _bustDetail;
        private Label _cash;

        private void Start()
        {
            if (heat == null) heat = FindAnyObjectByType<Heat>();
            if (wallet == null) wallet = FindAnyObjectByType<TheBlock.Game.Wallet>();
            Build();

            if (wallet == null) return;
            wallet.BalanceChanged += ShowCash;
            ShowCash(wallet.Balance);
        }

        private void OnDestroy()
        {
            if (wallet != null) wallet.BalanceChanged -= ShowCash;
        }

        private void ShowCash(int balance)
        {
            if (_cash != null) _cash.text = "$" + balance;
        }

        private void Build()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;

            _stars = new StarRow { name = "wanted-stars" };
            _stars.style.position = Position.Absolute;
            _stars.style.top = 18f;
            _stars.style.right = 24f;
            _stars.style.width = 110f;
            _stars.style.height = 34f;
            root.Add(_stars);

            // The cash readout sits under the stars. U25 owns the typography; what it owes here is
            // legibility, not a design — a fine that is charged and never shown is a fine nobody
            // can tell happened.
            _cash = new Label("$0") { name = "cash" };
            _cash.style.position = Position.Absolute;
            _cash.style.top = 56f;
            _cash.style.right = 24f;
            _cash.style.fontSize = 22f;
            _cash.style.unityFontStyleAndWeight = FontStyle.Bold;
            _cash.style.unityTextAlign = TextAnchor.MiddleRight;
            _cash.style.width = 110f;
            _cash.style.color = new Color(0.55f, 0.92f, 0.55f);
            root.Add(_cash);

            _bustOverlay = new VisualElement { name = "busted" };
            _bustOverlay.style.position = Position.Absolute;
            _bustOverlay.style.top = 0;
            _bustOverlay.style.bottom = 0;
            _bustOverlay.style.left = 0;
            _bustOverlay.style.right = 0;
            _bustOverlay.style.backgroundColor = new Color(0.47f, 0f, 0f, 0.55f);
            _bustOverlay.style.alignItems = Align.Center;
            _bustOverlay.style.justifyContent = Justify.Center;
            _bustOverlay.style.display = DisplayStyle.None;
            _bustOverlay.pickingMode = PickingMode.Ignore;

            _bustLabel = new Label("BUSTED");
            _bustLabel.style.color = new Color(1f, 0.27f, 0.27f);
            _bustLabel.style.fontSize = 64f;
            _bustLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _bustOverlay.Add(_bustLabel);

            _bustDetail = new Label(string.Empty);
            _bustDetail.style.color = Color.white;
            _bustDetail.style.fontSize = 24f;
            _bustDetail.style.unityTextAlign = TextAnchor.MiddleCenter;
            _bustDetail.style.whiteSpace = WhiteSpace.Normal;
            _bustOverlay.Add(_bustDetail);

            root.Add(_bustOverlay);
        }

        private void Update()
        {
            if (_stars == null || heat == null) return;

            // Cooling is the "you're losing them" readout, and the partial star is the web's evade
            // arc: before it, the fraction fills as you stay out of sight; after it, as the top star
            // drains. Heat itself is whole stars, so this is the only continuous thing left to draw
            // and it is the one worth drawing.
            _stars.Set(heat.Tuning.MaxStars, heat.Stars, heat.Fraction, heat.Cooling);
        }

        /// <summary>
        /// The BUSTED screen. It names what actually happened rather than a flat fine, because the
        /// two outcomes now differ: in a vehicle you and it are impounded at the station, on foot you
        /// are cuffed where you stand. Both cost money, and <paramref name="owed"/> is whatever the
        /// wallet could not cover.
        /// </summary>
        public void ShowBusted(int taken, int owed, bool impounded)
        {
            if (_bustOverlay == null) return;

            _bustLabel.text = "BUSTED";

            var line = taken > 0 ? $"−${taken}" : "nothing left to take";
            if (owed > 0) line += $"   ·   ${owed} owed";
            line += impounded ? "\nvehicle impounded — station" : "\ncuffed on the spot";

            _bustDetail.text = line;
            _bustOverlay.style.display = DisplayStyle.Flex;
        }

        public void HideBusted()
        {
            if (_bustOverlay == null) return;
            _bustOverlay.style.display = DisplayStyle.None;
        }
    }
}
