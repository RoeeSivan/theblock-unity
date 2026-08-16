using System.Collections.Generic;
using System.Text;
using TheBlock.Audio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// <b>The Block → Build Audio</b> — the one step that makes the game audible.
    ///
    /// It builds the mixer if it is missing, fills the <see cref="AudioLibrary"/> from every clip
    /// under <c>Assets/Audio</c>, and assembles the <c>GameAudio</c> object with its five systems
    /// wired to the right buses. Idempotent: run it after adding a clip, after a Library wipe, or
    /// whenever something has been dragged out of the scene.
    ///
    /// <b>It is a build step and not a prefab</b> for the same reason the rest of this project's
    /// world is built rather than authored: a scene object that someone has to remember to keep in
    /// sync with the config drifts, and this one has to know which mixer group each source belongs
    /// to. Everything here is derivable, so nothing here is hand-placed.
    ///
    /// <b>It marks the scene dirty and does NOT save it</b> — the same contract Build Drivable Cars
    /// keeps, so a build can be inspected before it is committed to.
    /// </summary>
    public static class AudioBuilder
    {
        public const string AudioFolder = "Assets/Audio";
        public const string LibraryPath = AudioFolder + "/AudioLibrary.asset";

        /// <summary>The siren every cop car loops. Named once, here, rather than in three places.</summary>
        public const string SirenUrl = "/audio/sfx/siren-loop.mp3";

        [MenuItem("The Block/Build Audio")]
        public static void Build()
        {
            var report = new StringBuilder("AudioBuilder\n");

            AudioMixerBuilder.Build();
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AudioMixerBuilder.MixerPath);
            if (mixer == null)
            {
                Debug.LogError(
                    "AudioBuilder: no mixer, so nothing can be routed. See AudioMixerBuilder.");
                return;
            }

            var library = EnsureLibrary(out int clipCount, out var duplicates);
            report.AppendLine($"  library {clipCount} clip(s) → {LibraryPath}");
            foreach (var duplicate in duplicates)
                report.AppendLine($"  ⚠ duplicate key '{duplicate}' — one of them is unreachable");

            var host = EnsureHost();
            var sfx = Ensure<Sfx>(host, "Sfx");
            var ambient = Ensure<Ambient>(host, "Ambient");
            var screams = Ensure<Screams>(host, "Screams");
            var engine = Ensure<EngineSound>(host, "Engine");
            var rotor = Ensure<RotorSound>(host, "Rotor");

            sfx.SetOutput(Group(mixer, "Sfx"));
            ambient.Configure(library, Group(mixer, "Ambient"));
            screams.Configure(library, Group(mixer, "Sfx"), sfx);
            engine.Configure(library, Group(mixer, "Engine"));
            rotor.SetOutput(Group(mixer, "Engine"));

            var siren = library.Resolve(SirenUrl);
            if (siren == null) report.AppendLine($"  ⚠ no clip for {SirenUrl} — the police are silent");

            host.GetComponent<GameAudio>().Configure(
                mixer, library, siren, sfx, ambient, screams, engine, rotor);

            report.AppendLine(BindExisting(mixer, library));
            report.Append(AudioMixerBuilder.Validate());

            EditorUtility.SetDirty(host);
            // A GameObject created or re-parented in the editor leaves the scene CLEAN, so Save
            // writes nothing and reports success — the memory file this project already carries.
            EditorSceneManager.MarkSceneDirty(host.scene);

            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Loads the library, creating it if it does not exist, and refills it from disk.
        ///
        /// Public because <c>MissionBuilder</c> calls it: Build Campaign can legitimately run before
        /// Build Audio has ever been used, and a <c>Voice</c> pointed at nothing is a campaign that
        /// plays in silence.
        /// </summary>
        public static AudioLibrary EnsureLibrary() => EnsureLibrary(out _, out _);

        public static AudioLibrary EnsureLibrary(out int count, out List<string> duplicates)
        {
            var library = AssetDatabase.LoadAssetAtPath<AudioLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<AudioLibrary>();
                if (!AssetDatabase.IsValidFolder(AudioFolder))
                    AssetDatabase.CreateFolder("Assets", "Audio");
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            var entries = new List<AudioLibrary.Entry>();
            var seen = new HashSet<string>();
            duplicates = new List<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;

                var key = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!seen.Add(key))
                {
                    // Two clips with one name means one of them can never be resolved, and the
                    // symptom is a sound that is simply the wrong sound. Worth a line in the report.
                    duplicates.Add(key);
                    continue;
                }

                entries.Add(new AudioLibrary.Entry { Key = key, Clip = clip });
            }

            entries.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            library.Fill(entries);
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();

            count = entries.Count;
            return library;
        }

        /// <summary>
        /// Points the two components that already existed at the library and their buses.
        ///
        /// <c>Voice</c> and <c>Conductor</c> were built in U20 and U22 and have been playing through
        /// nothing but the default output ever since. Routing them is what makes a Voice bus and a
        /// Music bus mean anything — and it is also the change most likely to break the dance, so
        /// the measurement pass re-reads <c>Conductor.Drift</c> afterwards.
        /// </summary>
        private static string BindExisting(AudioMixer mixer, AudioLibrary library)
        {
            var log = new StringBuilder();

            foreach (var voice in Object.FindObjectsByType<Voice>(FindObjectsInactive.Include))
            {
                voice.Bind(library, Group(mixer, "Voice"));
                EditorUtility.SetDirty(voice);
                log.AppendLine($"  bound Voice on '{voice.gameObject.name}' → Voice bus");
            }

            foreach (var conductor in
                     Object.FindObjectsByType<TheBlock.Minigame.Rhythm.Conductor>(FindObjectsInactive.Include))
            {
                conductor.SetOutput(Group(mixer, "Music"));
                EditorUtility.SetDirty(conductor);
                log.AppendLine($"  bound Conductor on '{conductor.gameObject.name}' → Music bus");
            }

            return log.ToString();
        }

        private static AudioMixerGroup Group(AudioMixer mixer, string name)
        {
            foreach (var group in mixer.FindMatchingGroups(string.Empty))
                if (group.name == name)
                    return group;
            Debug.LogWarning($"AudioBuilder: the mixer has no '{name}' group.");
            return null;
        }

        private static GameObject EnsureHost()
        {
            var existing = Object.FindAnyObjectByType<GameAudio>();
            if (existing != null) return existing.gameObject;

            var host = GameObject.Find("GameAudio");
            if (host == null) host = new GameObject("GameAudio");
            if (!host.TryGetComponent<GameAudio>(out _)) host.AddComponent<GameAudio>();
            return host;
        }

        /// <summary>One child per system, so each has a place to hang its own pool of sources.</summary>
        private static T Ensure<T>(GameObject host, string childName) where T : Component
        {
            var child = host.transform.Find(childName);
            if (child == null)
            {
                var go = new GameObject(childName);
                go.transform.SetParent(host.transform, false);
                child = go.transform;
            }

            if (!child.TryGetComponent<T>(out var component))
                component = child.gameObject.AddComponent<T>();
            return component;
        }
    }
}
