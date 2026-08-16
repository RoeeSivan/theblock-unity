using UnityEngine;

namespace TheBlock.Game
{
    /// <summary>
    /// The player's cash - the port of <c>game/wallet.ts</c>.
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

        [Tooltip("Balance a fresh profile opens on, and what New Game resets to. It is the price of " +
                 "the FIRST power-up in the catalogue - one purchase, on the house - and " +
                 "WorldBuilder writes it from powerUpConfig so the two can never drift. NOTE the " +
                 "scene stores its own copy, so editing the C# default alone changes nothing for a " +
                 "save that already exists - reach it through New Game.")]
        [SerializeField] private int startingBalance = DefaultStartingBalance;

        /// <summary>
        /// The opening balance when nothing has written one: <c>powerUpConfig.items[0].price</c>, the
        /// energy drink at $40.
        ///
        /// <b>Not zero, and not a round number.</b> The web build opens at $0, which means the shop
        /// is a place you cannot use until the first mission pays - and a shop you have never been
        /// inside is a shop you do not know exists. One item's worth of cash makes the 7-Eleven
        /// reachable on the walk to the first job, which is the only cheap way to teach that it is
        /// there. It is deliberately the CHEAPEST item, so it buys exactly one thing and the campaign
        /// still has to pay for everything after it.
        ///
        /// <see cref="EditorTools.WorldBuilder"/> re-derives it from the catalogue on every build, so
        /// a price change in <c>powerup.config.ts</c> carries here rather than leaving a stale
        /// constant behind.
        /// </summary>
        public const int DefaultStartingBalance = 40;

        [Tooltip("Wipes the stored balance back to startingBalance on Play. Debug - leave off.")]
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
        /// A penalty - the bust, and later the shop. Floors at zero, as the web build does, and
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
