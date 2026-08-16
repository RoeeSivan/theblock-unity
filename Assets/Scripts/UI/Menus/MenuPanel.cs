using UnityEngine;
using UnityEngine.UIElements;

namespace TheBlock.UI.Menus
{
    /// <summary>
    /// What every screen in this namespace has in common: one full-screen overlay on the shared HUD
    /// document, built once, shown and hidden by name.
    ///
    /// The web build says this five times over - each of <c>title-menu.ts</c>, <c>pause-menu.ts</c>,
    /// <c>controls-guide.ts</c>, <c>character-select.ts</c> and <c>shop-menu.ts</c> opens with the
    /// same paragraph about querying by id and toggling <c>.hidden</c>, and each re-implements the
    /// same three lines. There is no markup file here to hold the structure, so the shared part is a
    /// base class instead.
    ///
    /// <b>Built in Awake, and guarded.</b> Same reason <see cref="BriefingCard"/> is: another
    /// component's <c>Start</c> can ask a panel to open before this one's <c>Start</c> has run, and
    /// the result there was two of everything with the live one behind the dead one.
    ///
    /// <b>One <see cref="UIDocument"/> for the whole game.</b> U14 built it, U19 and U20 extended it,
    /// and the ledger's U25 row says why: a second document means a second <c>PanelSettings</c> and
    /// an argument about z-order. What that costs here is that <see cref="GameMap"/> adds its own
    /// children in <c>Start</c>, i.e. AFTER these, so every panel calls <c>BringToFront</c> on the
    /// way up rather than trusting the order it was added in.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public abstract class MenuPanel : MonoBehaviour
    {
        /// <summary>The overlay. Null until <see cref="Awake"/>.</summary>
        protected VisualElement Overlay { get; private set; }

        /// <summary>True while this panel is on screen.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>Element name, for the panel dump and for anyone reading the hierarchy.</summary>
        protected abstract string PanelName { get; }

        /// <summary>
        /// Does the backing hide the world (title, controls) or let it read through (pause)?
        /// </summary>
        protected virtual bool Opaque => false;

        /// <summary>Fill the overlay. Called once, from <see cref="Awake"/>.</summary>
        protected abstract void BuildContent(VisualElement overlay);

        protected virtual void Awake() => EnsureBuilt();

        protected void EnsureBuilt()
        {
            if (Overlay != null) return;

            var document = GetComponent<UIDocument>();
            var root = document != null ? document.rootVisualElement : null;
            if (root == null) return;

            Overlay = MenuStyle.Overlay(PanelName, Opaque);

            // Swallow clicks that miss a button. Without this a click on the empty part of the
            // title screen falls through to whatever U14 put underneath - the map panel registers a
            // ClickEvent of its own and would expand behind an opaque overlay.
            Overlay.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());

            BuildContent(Overlay);
            root.Add(Overlay);
        }

        public virtual void Show()
        {
            EnsureBuilt();
            if (Overlay == null) return;

            Overlay.style.display = DisplayStyle.Flex;
            Overlay.BringToFront();
            IsOpen = true;
        }

        public virtual void Hide()
        {
            if (Overlay == null) return;

            Overlay.style.display = DisplayStyle.None;
            IsOpen = false;
        }
    }
}
