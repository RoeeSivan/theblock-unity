using UnityEditor;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Import settings for the campaign's audio, applied on FIRST import so they cannot be lost.
    ///
    /// <b>An importer, not a one-off menu pass</b>, for the reason
    /// <see cref="GeneratedTextureImporter"/> already documents: a Library wipe re-imports every
    /// asset from its <c>.meta</c>, and a setting only ever applied by a menu item someone
    /// remembered to run is a setting that quietly reverts to the default. Unity's default for an
    /// mp3 is Decompress On Load with the whole file preloaded, which for the 6.7 MB dance track is
    /// several megabytes of PCM resident from boot for a song that plays once.
    ///
    /// Two profiles, split by folder:
    ///  - <c>Assets/Audio/Voice</c> — short lines, a second or two. Decompressed and preloaded, so
    ///    a customer's thank-you fires on the frame the pizza lands rather than after a hitch.
    ///  - <c>Assets/Audio/Music</c> — the dance track. STREAMED, and that matters twice over: it
    ///    keeps the memory down, and a streamed clip advances its own DSP position, which is what
    ///    <c>Conductor</c>'s clock is read against.
    /// </summary>
    public class MissionAudioImporter : AssetPostprocessor
    {
        private const string VoiceFolder = "Assets/Audio/Voice/";
        private const string MusicFolder = "Assets/Audio/Music/";

        private void OnPreprocessAudio()
        {
            var importer = (AudioImporter)assetImporter;

            // Only on the FIRST import. After that the .meta is the truth and a hand-tuned setting
            // must survive — the same contract the generated-texture importer keeps.
            if (!string.IsNullOrEmpty(importer.userData)) return;

            var path = assetPath.Replace('\\', '/');
            var isVoice = path.StartsWith(VoiceFolder);
            var isMusic = path.StartsWith(MusicFolder);
            if (!isVoice && !isMusic) return;

            var settings = importer.defaultSampleSettings;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = isMusic ? 0.7f : 0.5f;

            // Voice is mono in every one of these files and narration is never positional, so
            // forcing mono costs nothing and halves each clip.
            importer.forceToMono = isVoice;
            importer.loadInBackground = isMusic;

            settings.loadType = isMusic
                ? AudioClipLoadType.Streaming
                : AudioClipLoadType.DecompressOnLoad;

            // Preloading moved onto the per-platform sample settings; the importer-level property is
            // obsolete and errors on Unity 6.
            settings.preloadAudioData = isVoice;

            importer.defaultSampleSettings = settings;
            importer.userData = isMusic ? "theblock-music" : "theblock-voice";

            Debug.Log($"MissionAudioImporter: {path} → {settings.loadType}, " +
                      $"{settings.compressionFormat} q{settings.quality:0.0}" +
                      (importer.forceToMono ? ", mono" : string.Empty));
        }
    }
}
