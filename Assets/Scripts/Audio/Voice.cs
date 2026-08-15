using System.Collections.Generic;
using UnityEngine;

namespace TheBlock.Audio
{
    /// <summary>
    /// Spoken lines — the port of <c>src/audio/voice.ts</c>. The briefings, the customers' thank-yous
    /// and Remy's dance hype.
    ///
    /// <b>U27 owns audio, and this is not that unit.</b> It exists because the dance minigame's clock
    /// IS its music and a briefing that reads in silence is a different scene from one that is read
    /// to you — those were the user's call to pull forward. Engine, ambient, radio and the sfx pool
    /// are all still U27's, and this is the seam they extend rather than replace.
    ///
    /// <b>Non-positional, one voice at a time.</b> A line is narration, not a sound in the world: it
    /// belongs at the same volume wherever the player is standing, and a second line starting while
    /// the first is still going is two people talking over each other. Both are the web build's
    /// behaviour — its <c>voice.ts</c> is one reused <c>HTMLAudioElement</c> for exactly this reason.
    ///
    /// Clips are addressed by the web URL the config already holds ("/audio/thanks-remy.mp3"), keyed
    /// on the FILE NAME. That way nothing has to re-type a path that the exporter is already
    /// carrying, and a renamed clip fails loudly at build time rather than silently at runtime.
    /// </summary>
    public class Voice : MonoBehaviour
    {
        [System.Serializable]
        public struct Entry
        {
            [Tooltip("File name with no extension, e.g. 'thanks-remy'.")]
            public string Key;

            public AudioClip Clip;
        }

        [Tooltip("Filled by The Block → Build Campaign from Assets/Audio/Voice.")]
        [SerializeField] private List<Entry> clips = new();

        [SerializeField] private AudioSource source;

        [Range(0f, 1f)]
        [SerializeField] private float volume = 1f;

        private readonly Dictionary<string, AudioClip> _byKey = new();
        private bool _indexed;

        private void Awake()
        {
            if (source == null && !TryGetComponent(out source))
            {
                source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f; // 2D: narration, not a sound in the world
            }

            Index();
        }

        private void Index()
        {
            if (_indexed) return;
            _indexed = true;
            _byKey.Clear();
            foreach (var entry in clips)
                if (!string.IsNullOrEmpty(entry.Key) && entry.Clip != null)
                    _byKey[entry.Key] = entry.Clip;
        }

        /// <summary>True while a line is being spoken.</summary>
        public bool IsPlaying => source != null && source.isPlaying;

        /// <summary>
        /// Speaks the line at this web URL, cutting off whatever was playing. A URL with no clip is
        /// silent and says so once — U27 will fill the gaps, and a missing line must never be the
        /// thing that stops a mission starting.
        /// </summary>
        public void Play(string url, float gain = 1f)
        {
            if (string.IsNullOrEmpty(url) || source == null) return;
            Index();

            var clip = Resolve(url);
            if (clip == null)
            {
                Debug.LogWarning($"Voice: no clip for '{url}' — the line plays silently.");
                return;
            }

            source.Stop();
            source.clip = clip;

            // The web build pushes Remy's cheers past unity gain (1.6) so he is louder than the
            // other lines. Unity clamps an AudioSource at 1, so the loud one is the baseline and
            // everything else sits under it — same relationship, legal numbers.
            source.volume = Mathf.Clamp01(volume * gain);
            source.Play();
        }

        /// <summary>One of these lines at random. Remy's hype, which must not be the same clip twice.</summary>
        public void PlayRandom(IReadOnlyList<string> urls, float gain = 1f)
        {
            if (urls == null || urls.Count == 0) return;
            Play(urls[Random.Range(0, urls.Count)], gain);
        }

        /// <summary>Cuts the line off — what happens when a player dismisses a briefing early.</summary>
        public void Stop()
        {
            if (source != null) source.Stop();
        }

        private AudioClip Resolve(string url)
        {
            var key = url;
            var slash = key.LastIndexOf('/');
            if (slash >= 0) key = key.Substring(slash + 1);
            var dot = key.LastIndexOf('.');
            if (dot >= 0) key = key.Substring(0, dot);
            return _byKey.TryGetValue(key, out var clip) ? clip : null;
        }

        /// <summary>Editor-side wiring, used by The Block → Build Campaign.</summary>
        public void SetClips(List<Entry> entries)
        {
            clips = entries ?? new List<Entry>();
            _indexed = false;
        }
    }
}
