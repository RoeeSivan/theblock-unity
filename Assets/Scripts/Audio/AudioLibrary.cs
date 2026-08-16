using System.Collections.Generic;
using UnityEngine;

namespace TheBlock.Audio
{
    /// <summary>
    /// Every clip in the game, addressed by the web URL the config already holds
    /// (<c>/audio/ambient/honk.mp3</c>, <c>/audio/engine/car.wav</c>).
    ///
    /// <b>One resolver, and an asset rather than a scene list.</b> Before U27 there were two ways to
    /// find a clip - <c>Voice</c>'s private <c>List&lt;Entry&gt;</c>, filled by Build Campaign, and
    /// nothing at all for the other four folders. A second mechanism is how the two disagree later,
    /// and a list serialized into the scene is lost the next time something rebuilds that object.
    ///
    /// <b>Keyed on the FILE NAME, not the path.</b> That way nothing re-types a path the exporter is
    /// already carrying, and moving <c>Assets/Audio/Sfx</c> somewhere else does not break every
    /// lookup. All 26 clips have distinct names, and <see cref="Fill"/> refuses to build a library
    /// where two do not.
    ///
    /// A missing clip is reported at BUILD time by <c>The Block → Build Audio</c>, not discovered at
    /// runtime by a sound that never plays.
    /// </summary>
    [CreateAssetMenu(fileName = "AudioLibrary", menuName = "The Block/Audio Library")]
    public class AudioLibrary : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            [Tooltip("File name with no extension, e.g. 'thanks-remy'.")]
            public string Key;

            public AudioClip Clip;
        }

        [Tooltip("Filled by The Block → Build Audio from everything under Assets/Audio.")]
        [SerializeField] private List<Entry> clips = new();

        private Dictionary<string, AudioClip> _byKey;

        /// <summary>How many clips this library holds. Read by the build report and the tests.</summary>
        public int Count => clips.Count;

        /// <summary>
        /// The clip for a web URL, or null. Null is a real answer - a warning belongs to the ONE
        /// caller that knows whether silence there is a degraded mix or a broken feature.
        /// </summary>
        public AudioClip Resolve(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            Index();
            return _byKey.TryGetValue(KeyOf(url), out var clip) ? clip : null;
        }

        /// <summary>
        /// Every clip for a list of URLs, in order, with the missing ones dropped. What the scream
        /// pools and the ambient one-shot zones both want: a half-filled pool is a degraded mix, not
        /// a crash, exactly as the web has it.
        /// </summary>
        public AudioClip[] ResolveAll(IReadOnlyList<string> urls)
        {
            if (urls == null || urls.Count == 0) return System.Array.Empty<AudioClip>();
            var found = new List<AudioClip>(urls.Count);
            foreach (var url in urls)
            {
                var clip = Resolve(url);
                if (clip != null) found.Add(clip);
            }

            return found.ToArray();
        }

        /// <summary>The file name, with no folder and no extension. The key.</summary>
        public static string KeyOf(string url)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;
            var key = url;
            var slash = key.LastIndexOf('/');
            if (slash >= 0) key = key.Substring(slash + 1);
            var dot = key.LastIndexOf('.');
            return dot >= 0 ? key.Substring(0, dot) : key;
        }

        private void Index()
        {
            if (_byKey != null) return;
            _byKey = new Dictionary<string, AudioClip>(clips.Count);
            foreach (var entry in clips)
                if (!string.IsNullOrEmpty(entry.Key) && entry.Clip != null)
                    _byKey[entry.Key] = entry.Clip;
        }

        /// <summary>Editor-side wiring, used by <c>The Block → Build Audio</c>.</summary>
        public void Fill(List<Entry> entries)
        {
            clips = entries ?? new List<Entry>();
            _byKey = null;
        }
    }
}
