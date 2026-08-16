using TheBlock.Core;
using TheBlock.Game;
using TheBlock.Missions;
using TheBlock.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace TheBlock.UI.Menus
{
    /// <summary>
    /// Who is on screen, and what Esc does about it — the port of the boot tail of the web build's
    /// <c>main.ts</c> (the <c>for(;;) await titleMenu.showMainMenu()</c> loop) plus its
    /// <c>setPaused</c> and its <c>canPause</c>.
    ///
    /// Every panel in this namespace is dumb: it draws itself and raises a callback.
    /// <b>All the decisions are here</b>, which is the same division the web makes and for the same
    /// reason — <c>pause-menu.ts</c>'s own comment, "the frame loop owns the paused flag and the
    /// freeze; this is purely the overlay + callbacks".
    ///
    /// <b>The title screen is not a scene.</b> The loading bar is (see <c>BootLoader</c>), but the
    /// title itself sits on the HUD document over the loaded, frozen city — the same shape as the
    /// web's <c>#title-menu</c>, a z-101 overlay over a canvas that has already finished loading. A
    /// title in the Boot scene would need its own copy of the audio host, the Joe rig and the
    /// campaign, all so it could show them against a black background.
    /// </summary>
    public class GameFlow : MonoBehaviour
    {
        [Header("Panels — found automatically when left empty")]
        [SerializeField] private TitleMenu title;
        [SerializeField] private PauseMenu pause;
        [SerializeField] private ControlsGuide guide;
        [SerializeField] private SettingsPanel settings;
        [SerializeField] private CharacterPanel character;
        [SerializeField] private ShopMenu shop;

        [Header("Scene — found automatically when left empty")]
        [SerializeField] private MissionLaunch launch;
        [SerializeField] private CampaignRunner runner;
        [SerializeField] private GameMap map;
        [SerializeField] private VehicleEnterExit vehicles;
        [SerializeField] private BriefingCard card;
        [SerializeField] private Wallet wallet;
        [SerializeField] private TheBlock.Powerup.PowerUps powerups;
        [SerializeField] private TheBlock.World.SevenEleven store;

        [Header("Flow")]
        [Tooltip("Skip the title screen and drop straight into free roam at mission 1. For a " +
                 "play-test of something that is not the menus; the shipped game never sets it.")]
        [SerializeField] private bool skipTitle;

        [Tooltip("The scene Quit to Title goes back to. It re-shows the loading bar and rebuilds " +
                 "the world, which is the honest teardown — the web reloads the page here.")]
        [SerializeField] private string bootScene = "Boot";

        private void Awake()
        {
            if (title == null) title = GetComponent<TitleMenu>();
            if (pause == null) pause = GetComponent<PauseMenu>();
            if (guide == null) guide = GetComponent<ControlsGuide>();
            if (settings == null) settings = GetComponent<SettingsPanel>();
            if (character == null) character = GetComponent<CharacterPanel>();
            if (shop == null) shop = GetComponent<ShopMenu>();
            if (launch == null) launch = GetComponent<MissionLaunch>();

            if (runner == null) runner = FindAnyObjectByType<CampaignRunner>();
            if (map == null) map = FindAnyObjectByType<GameMap>();
            if (vehicles == null) vehicles = FindAnyObjectByType<VehicleEnterExit>();
            if (card == null) card = FindAnyObjectByType<BriefingCard>();
            if (wallet == null) wallet = FindAnyObjectByType<Wallet>();
            if (powerups == null) powerups = FindAnyObjectByType<TheBlock.Powerup.PowerUps>();
            if (store == null) store = FindAnyObjectByType<TheBlock.World.SevenEleven>();

            Wire();
        }

        private void Wire()
        {
            if (title != null)
            {
                title.OnNewGame = NewGame;
                title.OnContinue = Continue;
                title.OnMission = SelectMission;
                title.OnCharacter = () => Swap(title, () => character?.Open(BackToTitle));
                title.OnSettings = () => Swap(title, () => settings?.Open(BackToTitle));
                title.OnHowToPlay = () => Swap(title, () => guide?.Open(BackToTitle));
            }

            if (pause != null)
            {
                pause.OnResume = Resume;
                pause.OnSettings = () => Swap(pause, () => settings?.Open(BackToPause));
                pause.OnHowToPlay = () => Swap(pause, () => guide?.Open(BackToPause));
                pause.OnQuit = QuitToTitle;
            }

            if (shop != null)
            {
                shop.Balance = () => wallet != null ? wallet.Balance : 0;
                shop.Count = id => powerups != null ? powerups.Count(id) : 0;
                shop.OnBuy = Buy;
                shop.OnClose = CloseShop;
            }
        }

        // ── the 7-Eleven ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The only thing in the game that SPENDS cash, so the charge lives here beside the wallet
        /// rather than inside the overlay — the same placement <c>main.ts</c> gives it.
        ///
        /// Charge first, then stock. <see cref="Wallet.Charge"/> returns what it actually took, and
        /// the balance was checked a line earlier, so a partial take is impossible; taking the money
        /// before handing over the goods is simply the order that cannot leave you holding an item
        /// you did not pay for.
        /// </summary>
        private bool Buy(string id)
        {
            if (wallet == null || powerups == null) return false;

            var price = PriceOf(id);
            if (price <= 0 || wallet.Balance < price) return false;

            wallet.Charge(price);
            powerups.Add(id);
            return true;
        }

        private int PriceOf(string id)
        {
            foreach (var item in TheBlockConfig.Load()?.PowerUps?.Items
                                 ?? new System.Collections.Generic.List<TheBlockConfig.PowerUpSpec>())
                if (item != null && item.Id == id) return item.Price;
            return 0;
        }

        private void OpenShop()
        {
            Pause.Set(true);
            shop?.Open();
        }

        private void CloseShop()
        {
            shop?.Hide();
            Pause.Set(false);
        }

        private void Start()
        {
            // A debug jump has already chosen its mission in CampaignRunner.Start; offering the
            // choice again on top of it would undo the one thing the flag is for.
            if (runner != null && runner.DebugStartActive) return;

            if (skipTitle)
            {
                runner?.BeginRun(0, fresh: false);
                return;
            }

            Pause.Set(true);
            title?.Show();
        }

        // ── Esc ───────────────────────────────────────────────────────────────────────────────

        private void Update()
        {
            // A script recompile during Play wipes Core.Pause's statics without re-running Awake
            // (memory: recompile-during-play-nulls-fields), which would leave the overlay on screen
            // over a world that had quietly started moving again. Re-assert rather than trust it.
            var menuUp = Frozen();
            if (menuUp && !Pause.Frozen) Pause.Set(true);

            // Driven from the same state, every frame, rather than paired with each Show/Hide call.
            // There are seven ways into a menu and eight ways out, and a pairing that is wrong in
            // one of them leaves the wanted stars over the title screen with nothing to blame.
            SetGameplayHudVisible(!menuUp);

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // E at the 7-Eleven counter. Handled here rather than in SevenEleven for the reason every
            // other decision in this file is: the panels are dumb, and opening one means taking the
            // freeze, which is this component's job. The predicate is the store's own, so the prompt
            // MissionHud draws and the action taken here can never disagree.
            if (!Pause.Frozen && keyboard.eKey.wasPressedThisFrame &&
                store != null && store.CanShop() && shop != null && !shop.IsOpen)
            {
                OpenShop();
                return;
            }

            if (!keyboard.escapeKey.wasPressedThisFrame) return;

            // Order matters, and it is the web's (main.ts): the deepest thing on screen goes first,
            // then the map, then the pause toggle. Esc is the only key in the game that means "back"
            // rather than a verb, so it must never skip a layer.
            if (guide != null && guide.IsOpen) { guide.Close(); return; }
            if (settings != null && settings.IsOpen) { settings.Close(); return; }
            if (character != null && character.IsOpen) { character.Close(); return; }
            if (shop != null && shop.IsOpen) { CloseShop(); return; }

            // The title has nothing behind it to go back to. Esc there is not "resume", it is
            // "resume WHAT" — the campaign has not been told which mission to open on yet.
            if (title != null && title.IsOpen) return;

            if (map != null && map.IsExpanded) { map.SetExpanded(false); return; }
            if (pause != null && pause.IsOpen) { Resume(); return; }
            if (CanPause()) OpenPause();
        }

        /// <summary>Is one of this component's own panels holding the screen?</summary>
        private bool Frozen() =>
            (title != null && title.IsOpen) ||
            (pause != null && pause.IsOpen) ||
            (guide != null && guide.IsOpen) ||
            (settings != null && settings.IsOpen) ||
            (character != null && character.IsOpen) ||
            (shop != null && shop.IsOpen);

        /// <summary>
        /// The web's <c>canPause()</c>, minus its multiplayer clause. Free play only: on foot or
        /// driving, and not over a briefing card.
        ///
        /// <b>The dance is the clause that matters.</b> <see cref="Minigame.Rhythm.Conductor"/> is
        /// anchored to <c>AudioSettings.dspTime</c>, which <c>Time.timeScale</c> cannot stop — pause
        /// a routine and the arrows freeze while the song plays on, then resume against an anchor
        /// that is wrong by however long the menu was up. U27 already paid for one 21.3 ms shift in
        /// that anchor; this would be a shift of seconds.
        ///
        /// <see cref="GameMode.Entering"/> and <see cref="GameMode.Exiting"/> are refused for a
        /// smaller reason: they are a timed door swing that <see cref="VehicleEnterExit"/> drives on
        /// its own clock, and its Update is one of the things the freeze stops.
        /// </summary>
        private bool CanPause()
        {
            if (Pause.Frozen) return false;
            if (card != null && card.IsOpen) return false;
            if (vehicles == null) return true;
            return vehicles.Mode == GameMode.OnFoot || vehicles.Mode == GameMode.Driving;
        }

        // ── the title's choices ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Everything the web's New Game branch resets, in its order: unlocks, cash, the per-mission
        /// paid flags, and the bag — which had to go too, because it was bought with cash that no
        /// longer exists.
        ///
        /// <b>The freeze lifts BEFORE the run begins</b>, and that is not cosmetic ordering: the
        /// intro card is dismissed with SPACE, and the space key is one of the things
        /// <see cref="Pause"/> gates. Raised while still frozen it would be mouse-only.
        /// </summary>
        private void NewGame()
        {
            CloseMenus();
            Progress.Reset();
            wallet?.Reset();
            Payouts.Reset();
            powerups?.Reset();
            launch?.Launch(0, fresh: true);
        }

        /// <summary>Resume at the furthest mission reached, wallet and payouts untouched.</summary>
        private void Continue()
        {
            CloseMenus();
            launch?.Launch(Progress.UnlockedIndex, fresh: false);
        }

        private void SelectMission(int index)
        {
            CloseMenus();
            launch?.Launch(index, fresh: false);
        }

        // ── pause ─────────────────────────────────────────────────────────────────────────────

        private void OpenPause()
        {
            Pause.Set(true);
            pause?.Show();
        }

        private void Resume()
        {
            pause?.Hide();
            Pause.Set(false);
        }

        // ── the gameplay HUD behind a menu ────────────────────────────────────────────────────

        /// <summary>
        /// Element names this component owns. Everything else on the shared document belongs to
        /// gameplay and comes off the screen while a menu is up.
        /// </summary>
        private static readonly string[] MenuElements =
        {
            "title-menu", "pause-menu", "controls-guide", "settings", "character", "shop-menu",
            "fade", "briefing", "feedback-flash",
        };

        private bool _hudHidden;

        /// <summary>
        /// Takes the wanted stars, the cash counter, the radar and the objective line off the screen
        /// while a menu owns it.
        ///
        /// <b>By exclusion, not by a list of what to hide.</b> Six components draw onto this one
        /// document and more will; a list of gameplay elements would be a list somebody has to
        /// remember to append to, and the failure — three wanted stars floating over the title
        /// screen — is silent. The menus know their own names, so everything else is gameplay.
        ///
        /// <b>It hides with <c>visibility</c>, and the first version hid with <c>display</c>.</b>
        /// That version remembered each element's previous <c>display</c> and restored it on close,
        /// which looks careful and is exactly wrong: <see cref="GameMap.SetMinimapVisible"/> writes
        /// <c>display</c> on the radar panel, so Settings → Radar changed it WHILE the menu was up
        /// and the restore then put the old value straight back. The toggle did nothing, and the
        /// component that broke it was this one.
        ///
        /// <c>visibility</c> is a separate property no owner in this project writes, so there is
        /// nothing to remember and nothing to clobber: an element the player turned off stays off
        /// because its own <c>display</c> is still None, and one they turned on comes back.
        /// </summary>
        private void SetGameplayHudVisible(bool visible)
        {
            if (visible == !_hudHidden) return;
            _hudHidden = !visible;

            var document = GetComponent<UIDocument>();
            var root = document != null ? document.rootVisualElement : null;
            if (root == null) return;

            foreach (var child in root.Children())
            {
                if (System.Array.IndexOf(MenuElements, child.name) >= 0) continue;
                child.style.visibility = visible ? Visibility.Visible : Visibility.Hidden;
            }
        }

        /// <summary>
        /// Back to the loading bar and a fresh world. <b>Unfreeze first</b> — <c>Time.timeScale</c>
        /// is not scene state, and loading a scene with it still at 0 gives you a Boot screen whose
        /// progress bar never animates.
        /// </summary>
        private void QuitToTitle()
        {
            CloseMenus();
            SceneManager.LoadScene(bootScene);
        }

        // ── panel arbitration ─────────────────────────────────────────────────────────────────

        /// <summary>Exactly one panel visible at a time — the web's `showPanel` rule.</summary>
        private void Swap(MenuPanel from, System.Action open)
        {
            from?.Hide();
            open?.Invoke();
        }

        private void BackToTitle() => title?.Show();

        private void BackToPause() => pause?.Show();

        private void CloseMenus()
        {
            title?.Hide();
            pause?.Hide();
            guide?.Hide();
            settings?.Hide();
            character?.Hide();
            Pause.Set(false);
        }
    }
}
