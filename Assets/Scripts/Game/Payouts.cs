using System.Collections.Generic;
using UnityEngine;

namespace TheBlock.Game
{
    /// <summary>
    /// Which missions have already paid out this run — the port of <c>game/payouts.ts</c>.
    ///
    /// Each mission pays its flat reward exactly once per New Game. Retrying a failed run or
    /// replaying a finished one must not mint cash again, and <b>this has to be persisted, not held
    /// in memory</b>: the web build learned that the hard way. Its payout set was a plain in-memory
    /// Set while <see cref="Wallet"/> persisted, so the pair disagreed across a reload — quit, hit
    /// Continue, and every mission paid a second time into a balance that had survived.
    ///
    /// Stored as one delimited string rather than a JSON array. Four ids that are all lowercase
    /// ASCII do not need a parser, and the failure mode of a hand-edited value is a duplicate
    /// payout at worst, which is the same bar the web build set for itself.
    /// </summary>
    public static class Payouts
    {
        private const string Key = "theblock.paid";
        private const char Separator = ',';

        private static HashSet<string> Load()
        {
            var set = new HashSet<string>();
            var raw = PlayerPrefs.GetString(Key, string.Empty);
            if (string.IsNullOrEmpty(raw)) return set;
            foreach (var id in raw.Split(Separator))
                if (!string.IsNullOrEmpty(id))
                    set.Add(id);
            return set;
        }

        /// <summary>Has this mission already paid out this run?</summary>
        public static bool Has(string id) => !string.IsNullOrEmpty(id) && Load().Contains(id);

        /// <summary>Records a payout so it cannot land twice.</summary>
        public static void Mark(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            var set = Load();
            if (!set.Add(id)) return;
            PlayerPrefs.SetString(Key, string.Join(Separator.ToString(), set));
            PlayerPrefs.Save();
        }

        /// <summary>New Game: every mission pays again.</summary>
        public static void Reset()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
