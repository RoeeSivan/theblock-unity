using UnityEditor;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Import settings for every sound in the game, applied on FIRST import so they cannot be lost.
    ///
    /// <b>An importer, not a one-off menu pass</b>, for the reason
    /// <see cref="GeneratedTextureImporter"/> already documents: a Library wipe re-imports every
    /// asset from its <c>.meta</c>, and a setting only ever applied by a menu item someone
    /// remembered to run is a setting that quietly reverts to the default. Unity's default for an
    /// mp3 is Decompress On Load with the whole file preloaded, which for the 6.7 MB dance track is
    /// several megabytes of PCM resident from boot for a song that plays once.
    ///
    /// <b>It was <c>MissionAudioImporter</c> until U27.</b> Two folders were the whole of the game's
    /// audio while only the campaign made noise; there are five now, and the profiles below differ
    /// for reasons that are each a real constraint rather than a preference:
    ///
    ///  - <c>Assets/Audio/Voice</c> - short lines, a second or two. Decompressed and preloaded, so
    ///    a customer's thank-you fires on the frame the pizza lands rather than after a hitch.
    ///  - <c>Assets/Audio/Music</c> - the dance track. STREAMED, and that matters twice over: it
    ///    keeps the memory down, and a streamed clip advances its own DSP position, which is what
    ///    <c>Conductor</c>'s clock is read against.
    ///  - <c>Assets/Audio/Engine</c> - the three engine loops. <b>PCM, decompressed, NOT mono.</b>
    ///    <see cref="TheBlock.Audio.EngineSound"/> reads their samples with <c>GetData</c> to trim
    ///    the decoder tail off the loop point, and a compressed clip has no samples to read. They
    ///    total 400 KB; a lossy pass on a sub-second loop that plays for the whole game would be a
    ///    strange place to save it.
    ///  - <c>Assets/Audio/Ambient</c> - split by LENGTH, not by folder. The two beds are 30 s and
    ///    22 s of continuous murmur and stream; the one-shot honks and gulls must fire on the frame
    ///    they are rolled, so they preload like voice.
    ///  - <c>Assets/Audio/Sfx</c> - screams and the siren. Preloaded: the first person you run over
    ///    must not be the one who pays for the decode.
    /// </summary>
    public class GameAudioImporter : AssetPostprocessor
    {
        private const string Root = "Assets/Audio/";

        private const string VoiceFolder = Root + "Voice/";
        private const string MusicFolder = Root + "Music/";
        private const string EngineFolder = Root + "Engine/";
        private const string AmbientFolder = Root + "Ambient/";
        private const string SfxFolder = Root + "Sfx/";

        /// <summary>
        /// The two ambient BEDS, which stream. Everything else in that folder is a spot sound.
        /// Named rather than measured because <c>OnPreprocessAudio</c> runs before there is a clip
        /// to ask for a duration.
        /// </summary>
        private static readonly string[] AmbientBeds = { "street", "beach" };

        private void OnPreprocessAudio()
        {
            var importer = (AudioImporter)assetImporter;

            // Only on the FIRST import. After that the .meta is the truth and a hand-tuned setting
            // must survive - the same contract the generated-texture importer keeps.
            if (!string.IsNullOrEmpty(importer.userData)) return;

            var path = assetPath.Replace('\\', '/');
            if (!path.StartsWith(Root)) return;

            var settings = importer.defaultSampleSettings;
            string profile;

            if (path.StartsWith(MusicFolder))
            {
                profile = "music";
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.7f;
                settings.loadType = AudioClipLoadType.Streaming;
                settings.preloadAudioData = false;
                importer.loadInBackground = true;
                importer.forceToMono = false;
            }
            else if (path.StartsWith(VoiceFolder))
            {
                profile = "voice";
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.5f;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.preloadAudioData = true;
                importer.loadInBackground = false;
                // Voice is mono in every one of these files and narration is never positional, so
                // forcing mono costs nothing and halves each clip.
                importer.forceToMono = true;
            }
            else if (path.StartsWith(EngineFolder))
            {
                profile = "engine";
                // PCM, not Vorbis: EngineSound.GetData() needs real samples, and a Vorbis clip
                // decoded on demand hands back silence.
                settings.compressionFormat = AudioCompressionFormat.PCM;
                settings.quality = 1f;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.preloadAudioData = true;
                importer.loadInBackground = false;
                importer.forceToMono = false;
            }
            else if (path.StartsWith(AmbientFolder))
            {
                var isBed = System.Array.Exists(
                    AmbientBeds, bed => System.IO.Path.GetFileNameWithoutExtension(path) == bed);
                profile = isBed ? "ambient-bed" : "ambient-shot";
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = isBed ? 0.6f : 0.5f;
                settings.loadType = isBed
                    ? AudioClipLoadType.Streaming
                    : AudioClipLoadType.DecompressOnLoad;
                settings.preloadAudioData = !isBed;
                importer.loadInBackground = isBed;
                importer.forceToMono = false;
            }
            else if (path.StartsWith(SfxFolder))
            {
                profile = "sfx";
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.5f;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.preloadAudioData = true;
                importer.loadInBackground = false;
                // Screams are non-positional (the victim is under your own bumper) and the siren is
                // 3D, which Unity downmixes anyway. Mono either way.
                importer.forceToMono = true;
            }
            else
            {
                return;
            }

            importer.defaultSampleSettings = settings;
            importer.userData = "theblock-" + profile;

            Debug.Log($"GameAudioImporter: {path} → {profile}, {settings.loadType}, " +
                      $"{settings.compressionFormat} q{settings.quality:0.0}" +
                      (importer.forceToMono ? ", mono" : string.Empty));
        }
    }
}
