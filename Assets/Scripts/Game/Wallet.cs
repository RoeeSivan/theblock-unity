using UnityEngine;

namespace TheBlock.Game
{
    /// <summary>
    /// The player's cash — the port of <c>game/wallet.ts</c>.
    ///
    /// One number, persisted, floored at zero. <c>PlayerPrefs</c> is Unity's <c>localStorage</c>:
    /// same shape, same key, same "swallow the failure so play never breaks" contract, and the same
    /// write-through on every change rather than a save at quit.
    ///
    /// <b>U28 still owns the economy.</b> This exists now because a bust has to take something, and
    /// a fine that is only tallied is not a punishment you can feel. Missions paying into it, the
    /// 7-Eleven, and the New Game reset are all still that unit's.
    /// </summary>
    public class Wallet : MonoBehaviour
    {
        private const string Key = "theblock.cash";

        [Tooltip("Balance for a save that has never been written. 0, the web build's own opening " +
                 "balance — the campaign's $700 is the only income there is, and the 7-Eleven's " +
                 "prices are a share of exactly that number. U19 set it to 500 so a bust could be " +
                 "tested before anything could be earned; U28 gave it something to be true about. " +
                 "NOTE the scene stores its own copy, so changing this default alone changes " +
                 "nothing for a save that already exists — reach it through New Game.")]
        [SerializeField] private int startingBalance;

        [Tooltip("Wipes the stored balance back to startingBalance on Play. Debug — leave off.")]
        [SerializeField] private bool resetOnPlay;

        private int _balance;
        private bool _loaded;

        /// <summary>Current balance in whole dollars.</summary>
        public int Balance
        {
            get
            {
                Load();
                return _balance;
            }
        }

        /// <summary>Raised on every change, with the new balance.</summary>
        public event System.Action<int> BalanceChanged;

        private void Awake() => Load();

        private void Load()
        {
            if (_loaded) return;
            _loaded = true;

            _balance = resetOnPlay || !PlayerPrefs.HasKey(Key)
                ? Mathf.Max(0, startingBalance)
                : Mathf.Max(0, PlayerPrefs.GetInt(Key, startingBalance));

            if (resetOnPlay) Save();
        }

        private void Save()
        {
            PlayerPrefs.SetInt(Key, _balance);
            PlayerPrefs.Save();
        }

        /// <summary>A payout. Returns the new balance.</summary>
        public int Add(int amount)
        {
            Load();
            _balance += Mathf.Max(0, amount);
            Save();
            BalanceChanged?.Invoke(_balance);
            return _balance;
        }

        /// <summary>
        /// A penalty — the bust, and later the shop. Floors at zero, as the web build does, and
        /// returns <b>what was actually taken</b> rather than the new balance: a fine of 100 against
        /// a balance of 40 costs 40, and the caller is the only thing that can decide what happens to
        /// the other 60.
        /// </summary>
        public int Charge(int amount)
        {
            Load();
            int taken = Mathf.Clamp(Mathf.Max(0, amount), 0, _balance);
            if (taken == 0) return 0;

            _balance -= taken;
            Save();
            BalanceChanged?.Invoke(_balance);
            return taken;
        }

        /// <summary>New Game. U28's, but the storage lives here.</summary>
        public void Reset()
        {
            Load();
            _balance = Mathf.Max(0, startingBalance);
            Save();
            BalanceChanged?.Invoke(_balance);
        }
    }
}
