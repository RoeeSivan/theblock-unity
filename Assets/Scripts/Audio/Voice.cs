using System.Collections.Generic;
using UnityEngine;

namespace TheBlock.Audio
{
    /// <summary>
    /// Spoken lines - the port of <c>src/audio/voice.ts</c>. The briefings, the customers' thank-yous
    /// and Remy's dance hype.
    ///
    /// <b>It arrived early, in U20/U22</b>, because the dance minigame's clock IS its music and a
    /// briefing that reads in silence is a different scene from one that is read to you. U27 has now
    /// built the rest around it and changed exactly one thing: the clips come from
    /// <see cref="AudioLibrary"/> rather than from a list serialized into this component, so the
    /// project has ONE resolver instead of this one plus whatever the other four folders grew.
    ///
    /// <b>Non-positional, one voice at a time.</b> A line is narration, not a sound in the world: it
    /// belongs at the same volume wherever the player is standing, and a second line starting while
    /// the first is still going is two people talking over each other. Both are the web build's
    /// behaviour - its <c>voice.ts</c> is one reused <c>HTMLAudioElement</c> for exactly this reason.
    ///
    /// Clips are addressed by the web URL the config already holds ("/audio/thanks-remy.mp3"), keyed
    /// on the FILE NAME. That way nothing has to re-type a path that the exporter is already
    /// carrying, and a renamed clip fails loudly at build time rather than silently at runtime.
    /// </summary>
    public class Voice : MonoBehaviour
    {
        [Tooltip("The project's one clip resolver. Filled by The Block → Build Audio.")]
        [SerializeField] private AudioLibrary library;

        [SerializeField] private AudioSource source;

        [SerializeField] private UnityEngine.Audio.AudioMixerGroup output;

        [Range(0f, 1f)]
        [SerializeField] private float volume = 1f;

        private void Awake()
        {
            // A source of its OWN, on a child, and deliberately never TryGetComponent.
            //
            // Voice and Conductor both live on the Campaign object, and Conductor is
            // [RequireComponent(typeof(AudioSource))] - so that object has exactly one AudioSource
            // and both components used to grab it. Every line therefore played THROUGH the song:
            // Play() calls Stop() first, so Remy's cheer silenced the dance's own soundtrack and the
            // routine ran on in silence with the DSP clock still counting.
            //
            // One AudioSource is one voice, which is the right model for narration and the wrong one
            // for narration-over-music. Two sources is the whole fix; nothing else is shared.
            if (source == null)
            {
                var host = new GameObject("Voice Source");
                host.transform.SetParent(transform, false);
                source = host.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f; // 2D: narration, not a sound in the world
            }

            source.outputAudioMixerGroup = output;
        }

        /// <summary>True while a line is being spoken.</summary>
        public bool IsPlaying => source != null && source.isPlaying;

        /// <summary>
        /// Speaks the line at this web URL, cutting off whatever was playing. A URL with no clip is
        /// silent and says so once - U27 will fill the gaps, and a missing line must never be the
        /// thing that stops a mission starting.
        /// </summary>
        public void Play(string url, float gain = 1f)
        {
            if (string.IsNullOrEmpty(url) || source == null) return;

            var clip = library != null ? library.Resolve(url) : null;
            if (clip == null)
            {
                Debug.LogWarning($"Voice: no clip for '{url}' - the line plays silently.");
                return;
            }

            source.Stop();
            source.clip = clip;

            // The web build pushes Remy's cheers past unity gain (1.6) so he is louder than the
            // other lines. Unity clamps an AudioSource at 1, so the loud one is the baseline and
            // everything else sits under it - same relationship, legal numbers.
            source.volume = Mathf.Clamp01(volume * gain);
            source.Play();
        }

        /// <summary>One of these lines at random. Remy's hype, which must not be the same clip twice.</summary>
        public void PlayRandom(IReadOnlyList<string> urls, float gain = 1f)
        {
            if (urls == null || urls.Count == 0) return;
            Play(urls[Random.Range(0, urls.Count)], gain);
        }

        /// <summary>Cuts the line off - what happens when a player dismisses a briefing early.</summary>
        public void Stop()
        {
            if (source != null) source.Stop();
        }

        /// <summary>Editor-side wiring, used by The Block → Build Audio and Build Campaign.</summary>
        public void Bind(AudioLibrary clips, UnityEngine.Audio.AudioMixerGroup group)
        {
            library = clips;
            output = group;
            if (source != null) source.outputAudioMixerGroup = group;
        }
    }
}
