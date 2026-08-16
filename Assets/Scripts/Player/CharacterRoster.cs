using System;
using System.Collections.Generic;
using TheBlock.Game;
using UnityEngine;

namespace TheBlock.Player
{
    /// <summary>
    /// Who you can play as, and the one call that dresses everybody — the port of
    /// <c>src/player/characters.config.ts</c> plus <c>main.ts</c>'s <c>applyCharacter()</c> fan-out.
    ///
    /// <b>The fan-out is two bodies here and five in the web build, and that is U9's dividend.</b>
    /// Its comment lists four rigs that wear the player's body — the walking capsule, the seated car
    /// driver, and the bike and jetski riders — because each is a separately built skinned mesh
    /// there, so picking a character has to reach all of them "or you'd change clothes on getting
    /// into a vehicle". This port reparents ONE player into every seat (U9), so all four collapse
    /// into a single <see cref="CharacterBody"/>. What is left is that body and the one on the dance
    /// stage, which is a second body in both projects for the same reason: the routine is a fixed
    /// shot of a performer, not of the player's own rig.
    ///
    /// <b>Nothing here is exported from the web config, deliberately.</b> Every other ported table
    /// in this project comes through <c>export-config.mjs</c> because it is full of hand-tuned
    /// numbers that must not be re-typed. This one is three ids, three names and two optional
    /// tuning fields that are unset for all three characters — the rest of that file is GLB URLs
    /// that mean nothing to Unity. There is no number here to get wrong.
    ///
    /// The <c>scale</c> and <c>seat</c> nudges the web's <c>PlayableCharacter</c> carries are also
    /// absent, and absent on purpose: both are unset for all three characters there, and the height
    /// match they exist to correct is done at import time here (<c>CharacterImporter</c>), on the
    /// visual child, against Joe.
    /// </summary>
    public class CharacterRoster : MonoBehaviour
    {
        /// <summary>
        /// <c>referenceCharacterId</c> AND <c>defaultCharacterId</c> — the web build has them as the
        /// same character on purpose: keeping the body the game shipped with as the reference means
        /// the roster changed nothing about how he looks.
        /// </summary>
        public const string DefaultId = "joe";

        [Serializable]
        public struct Entry
        {
            [Tooltip("Stored in PlayerPrefs and matched to characters.config.ts. Never renumber one.")]
            public string Id;

            [Tooltip("What the character screen's button says.")]
            public string Name;

            [Tooltip("The body. Its root carries the height match — see CharacterPrefabBuilder.")]
            public GameObject Prefab;
        }

        [Header("Built by The Block → Build Characters")]
        [SerializeField] private List<Entry> entries = new();

        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>
        /// The roster in the open scene, or null. Not cached in a static: this survives a scene load
        /// only by being found again, and U26's <c>SessionReset</c> exists because a static that
        /// latches across Quit to Title is this project's own recurring bug.
        /// </summary>
        public static CharacterRoster Find() => FindAnyObjectByType<CharacterRoster>(FindObjectsInactive.Include);

        /// <summary>
        /// Dresses every body in the scene and remembers the pick — the whole of
        /// <c>applyCharacter</c>. Safe to call with an id the roster no longer has: a stale
        /// PlayerPrefs value from a roster that has since changed must not brick the boot, which is
        /// what <c>characterById</c>'s fallback is for in the web build.
        /// </summary>
        public void Apply(string id, bool remember = true)
        {
            if (!TryResolve(id, out var entry)) return;

            foreach (var body in FindObjectsByType<CharacterBody>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                body.Wear(entry);
            }

            if (remember) Progress.CharacterId = entry.Id;
        }

        /// <summary>The stored pick, resolved — what the game boots wearing.</summary>
        public void ApplySaved() => Apply(Progress.CharacterId, remember: false);

        /// <summary>
        /// The web's <c>applyCharacter(player.characterId())</c> at the bottom of <c>main.ts</c> —
        /// "fan the saved pick out to the vehicles built after it". In <c>Start</c> rather than
        /// <c>Awake</c> so every <see cref="CharacterBody"/> has cached its own body first; and free
        /// for the common case, because the builder bakes the default in and
        /// <see cref="CharacterBody.Wear"/> early-outs on a body that is already worn.
        /// </summary>
        private void Start() => ApplySaved();

        /// <summary>
        /// The web's <c>characterById</c>: the asked-for entry, else the default, else the first
        /// row. False only when the roster is empty, which means the builder has not been run.
        /// </summary>
        public bool TryResolve(string id, out Entry entry)
        {
            entry = default;
            if (entries.Count == 0) return false;

            foreach (var candidate in entries)
                if (candidate.Id == id)
                {
                    entry = candidate;
                    return true;
                }

            foreach (var candidate in entries)
                if (candidate.Id == DefaultId)
                {
                    entry = candidate;
                    return true;
                }

            entry = entries[0];
            return true;
        }

        /// <summary>Editor-side wiring, used by The Block → Build Characters.</summary>
        public void Configure(List<Entry> built) => entries = built;
    }
}
