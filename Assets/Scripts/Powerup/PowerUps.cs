using System.Collections.Generic;
using TheBlock.Audio;
using TheBlock.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TheBlock.Powerup
{
    /// <summary>
    /// What you own and what is currently running — the port of <c>src/powerup/powerups.ts</c>.
    ///
    /// <b>Two halves with deliberately different lifetimes, and getting that wrong is the whole
    /// bug surface of this file.</b>
    /// <list type="bullet">
    ///   <item>STOCK persists. You paid cash for it, and losing a purchase to a restart reads as a
    ///   bug rather than as a rule.</item>
    ///   <item>TIMERS do not. A reload starts everything idle — the same rule the fuel tank rests
    ///   on. Persisting a countdown would mean a 4-minute effect that survives a quit, which is
    ///   either free or stolen depending on which side of the clock you land.</item>
    /// </list>
    ///
    /// <b>A MonoBehaviour, unlike the web's pure model,</b> because the timers need a frame and the
    /// hotkeys need polling — and because Unity's answer to "who owns per-frame state" is a
    /// component in the scene, not a closure. The effects it drives are still owned by the systems
    /// they belong to: this pushes four values outward every frame and reads none of them back.
    ///
    /// <b>The catalogue is the config's, not this file's.</b> Prices and durations come from
    /// <c>powerup.config.ts</c> through the snapshot, and the arithmetic behind them is closed: the
    /// campaign pays a fixed $700 once per New Game, one of each item is $265, and there is no other
    /// income. See <see cref="TheBlockConfig.PowerUpCatalogSpec"/>.
    /// </summary>
    public class PowerUps : MonoBehaviour
    {
        /// <summary>
        /// One <c>PlayerPrefs</c> int per item rather than the web's one JSON blob.
        ///
        /// <c>localStorage</c> only stores strings, so <c>powerups.ts</c> had to serialize a record
        /// and hand-tolerate a corrupt parse; <c>PlayerPrefs</c> stores ints natively and cannot
        /// throw. Four keys with a typed default is the same state with none of the parsing — the
        /// same simplification <see cref="Game.Payouts"/> made for the same reason.
        /// </summary>
        private const string KeyPrefix = "theblock.powerups.";

        [Tooltip("Fills every slot on Play so the effects can be tested without shopping for them. " +
                 "The port of the web's ?stock= flag. Debug — leave at 0.")]
        [SerializeField] private int debugStock;

        [Header("Effect targets — found automatically when left empty")]
        [SerializeField] private Player.PlayerController player;
        [SerializeField] private Police.Heat heat;

        private TheBlockConfig.PowerUpCatalogSpec _catalogue;

        /// <summary>Seconds left on each timed effect, parallel to <see cref="Items"/>. 0 = idle.</summary>
        private float[] _timers;

        /// <summary>🎒 has no clock — this is its whole state.</summary>
        private bool _armed;

        private bool _enabled = true;
        private bool _bound;

        /// <summary>The catalogue, in shop and HUD order. Empty until <see cref="Bind"/> has run.</summary>
        public IReadOnlyList<TheBlockConfig.PowerUpSpec> Items =>
            _catalogue?.Items ?? (IReadOnlyList<TheBlockConfig.PowerUpSpec>)System.Array.Empty<TheBlockConfig.PowerUpSpec>();

        /// <summary>Raised whenever stock or a timer changes, so the HUD repaints on an edge.</summary>
        public event System.Action Changed;

        private void Awake() => Bind();

        private void Bind()
        {
            if (_bound) return;

            var snapshot = TheBlockConfig.Load();
            _catalogue = snapshot?.PowerUps;
            if (_catalogue?.Items == null || _catalogue.Items.Count == 0)
            {
                Debug.LogError("PowerUps: the snapshot has no `powerUpConfig`. Re-run tools/export-config.sh.");
                return;
            }

            _bound = true;
            _timers = new float[_catalogue.Items.Count];

            if (player == null) player = FindAnyObjectByType<Player.PlayerController>();
            if (heat == null) heat = FindAnyObjectByType<Police.Heat>();

            if (debugStock > 0)
            {
                int n = Mathf.Min(debugStock, _catalogue.DevStockMax);
                foreach (var item in _catalogue.Items) SetCount(item.Id, n);
                Debug.Log($"[powerups] debugStock filled every slot to {n}");
            }

            PushEffects();
        }

        // --- stock ----------------------------------------------------------------------------------

        /// <summary>How many of this item are in the bag.</summary>
        public int Count(string id) => Mathf.Max(0, PlayerPrefs.GetInt(KeyPrefix + id, 0));

        /// <summary>Any stock at all? The HUD strip hides itself on false.</summary>
        public bool AnyOwned()
        {
            foreach (var item in Items)
                if (Count(item.Id) > 0) return true;
            return false;
        }

        /// <summary>
        /// A purchase landed. <b>This only holds stock</b> — the wallet is charged by the caller,
        /// which is the one place that knows the price rules, exactly as in the web build.
        /// </summary>
        public void Add(string id, int n = 1)
        {
            if (n <= 0) return;
            SetCount(id, Count(id) + n);
            Changed?.Invoke();
        }

        private void SetCount(string id, int n)
        {
            PlayerPrefs.SetInt(KeyPrefix + id, Mathf.Max(0, n));
            PlayerPrefs.Save();
        }

        // --- running state --------------------------------------------------------------------------

        /// <summary>Running now? For 🎒 this means ARMED — it has no clock.</summary>
        public bool IsActive(string id)
        {
            int i = IndexOf(id);
            if (i < 0) return false;
            return Timed(i) ? _timers[i] > 0f : _armed;
        }

        /// <summary>Seconds left, 0 when idle or untimed. The HUD chip's countdown.</summary>
        public float Remaining(string id)
        {
            int i = IndexOf(id);
            return i >= 0 && Timed(i) ? _timers[i] : 0f;
        }

        /// <summary>🥤 — sprint costs no stamina.</summary>
        public bool SprintFree => IsActive("energy");

        /// <summary>📱 — crimes raise no stars.</summary>
        public bool HeatImmune => IsActive("phone");

        /// <summary>🎒 — the next completed mission pays double. Read-only; the chip reads this.</summary>
        public bool DoublePayArmed => _armed;

        /// <summary>
        /// Spends 🎒's arming. <b>Call only where a payout actually lands</b>, so a failed run or a
        /// replay of a mission that has already paid cannot eat it.
        /// </summary>
        public bool ConsumeDoublePay()
        {
            if (!_armed) return false;
            _armed = false;
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Spends one and starts it.
        ///
        /// Returns false — and <b>consumes nothing</b> — when there is none left, when it is already
        /// running, or when the system is off. A mashed key can therefore never eat stock, which is
        /// the invariant the web build calls out and the reason the refusal has its own quiet cue.
        /// </summary>
        public bool Activate(string id)
        {
            Bind();
            int i = IndexOf(id);
            if (!_enabled || i < 0 || Count(id) <= 0 || IsActive(id)) return false;

            if (Timed(i)) _timers[i] = _catalogue.Items[i].DurationSec;
            else _armed = true;

            SetCount(id, Count(id) - 1);

            // 📱 clears what you have already run up, then holds the line for its duration. The
            // clear is an EVENT at ignition; the immunity is the state. Doing the clear inside
            // PushEffects would re-wipe every frame and make the 90 s window unloseable.
            if (id == "phone" && heat != null) heat.Clear();

            PushEffects();
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Off for a multiplayer round or any other seam that resets the world but not its clocks —
        /// the same switch the web build gives the fuel economy. Off also clears whatever is live.
        /// </summary>
        public void SetEnabled(bool on)
        {
            _enabled = on;
            if (!on) ClearAll();
        }

        /// <summary>New Game: no stock, nothing running. Bought with cash that no longer exists.</summary>
        public void Reset()
        {
            Bind();
            foreach (var item in Items) PlayerPrefs.DeleteKey(KeyPrefix + item.Id);
            PlayerPrefs.Save();
            ClearAll();
            Changed?.Invoke();
        }

        // --- the frame ------------------------------------------------------------------------------

        private void Update()
        {
            if (!_bound) Bind();
            if (!_bound) return;

            // A pause is not a slow clock — see Core.Pause. timeScale 0 already makes deltaTime 0,
            // but the hotkeys below poll the keyboard directly and would fire through a menu.
            if (Core.Pause.Frozen) return;

            ReadHotkeys();
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// Advances every timer and pushes the result outward.
        ///
        /// Expiry is an EDGE and must never latch: the power-down figure plays on the single frame a
        /// clock reaches zero, not on every frame after it.
        /// </summary>
        private void Tick(float dt)
        {
            bool expired = false;
            for (int i = 0; i < _timers.Length; i++)
            {
                if (_timers[i] <= 0f) continue;
                _timers[i] = Mathf.Max(0f, _timers[i] - dt);
                if (_timers[i] == 0f) expired = true;
            }

            // Pushed on every tick and not only on a change, so no path can leave the world boosted
            // or the player immune by forgetting to clear it — the web build's own reasoning.
            PushEffects();

            if (!expired) return;
            GameAudio.Cue(SfxCue.PowerDown);
            Changed?.Invoke();
        }

        private void ReadHotkeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // 1–4 have to work on foot AND at the wheel, so this sits outside every mode switch —
            // the same placement main.ts gives it.
            for (int i = 0; i < Items.Count && i < 4; i++)
            {
                var key = keyboard[Key.Digit1 + i];
                if (key == null || !key.wasPressedThisFrame) continue;

                if (Activate(Items[i].Id)) GameAudio.Cue(SfxCue.PowerUp);
                else GameAudio.Cue(SfxCue.Deny);
                return; // one press, one item
            }
        }

        /// <summary>
        /// The only place the four effects reach the systems that own them. One writer each.
        /// </summary>
        private void PushEffects()
        {
            SpeedBoost.Set(IsActive("coffee") ? _catalogue.SpeedBoost : 1f);
            if (player != null) player.InfiniteStamina = SprintFree;

            // NOT `heat.Frozen`: CrimeWatch writes that every frame from `interior.Inside`, so a
            // second owner would be overwritten within one frame and 📱 would silently do nothing
            // outside the pizzeria. `Immune` is 📱's own line.
            if (heat != null) heat.Immune = HeatImmune;
        }

        private void ClearAll()
        {
            if (_timers != null)
                for (int i = 0; i < _timers.Length; i++) _timers[i] = 0f;
            _armed = false;
            PushEffects();
        }

        private bool Timed(int i) => _catalogue.Items[i].DurationSec > 0f;

        private int IndexOf(string id)
        {
            var items = Items;
            for (int i = 0; i < items.Count; i++)
                if (items[i].Id == id) return i;
            return -1;
        }
    }
}
