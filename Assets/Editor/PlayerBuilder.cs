using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// U30a - builds the macOS Player, and fixes the settings the Player needs but the Editor never
    /// does.
    ///
    /// <b>A menu item rather than the Build Profiles window, for one reason: the window cannot fix
    /// anything.</b> Pressing Build there produces a Player carrying whatever settings happen to be
    /// current, and two of those settings are wrong in a way that only shows up after the build -
    /// see <see cref="FixPlayerSettings"/>. Running the fix and the build from one place means the
    /// pair cannot drift, and re-running is free because the fix is idempotent.
    ///
    /// <b>The architecture is deliberately NOT set here.</b>
    /// <c>UnityEditor.OSXStandalone.Extensions.dll</c> carries the string
    /// <i>"MacOSArchitecture has been obsoleted, please use UnityEditor.Build.OSArchitecture"</i>,
    /// so touching it from a script is a bet on which side of that rename this editor sits. It is
    /// one dropdown in File → Build Profiles → macOS, it persists, and a human setting it can see
    /// what they set. <see cref="Preflight"/> cannot check it for the same reason, so the build log
    /// says what to verify instead: <c>lipo -archs</c> on the built binary.
    /// </summary>
    public static class PlayerBuilder
    {
        /// <summary>Where the Player lands. Covered by <c>.gitignore</c>'s <c>/[Bb]uilds/</c>.</summary>
        private const string OutputDirectory = "Builds/macOS";
        private const string AppName = "The Block.app";

        /// <summary>
        /// Replaces the URP template's leftover <c>com.Unity-Technologies.com.unity.template.urp-blank</c>.
        /// This is the identifier the player's Mac registers the app under.
        /// </summary>
        private const string BundleIdentifier = "com.roeesivan.theblock";

        /// <summary>
        /// Shaders resolved by NAME at runtime, which means they are invisible to the build's
        /// dependency walk.
        ///
        /// A shader ships only if a material in a build scene uses it, or it sits in this list, or
        /// it lives in a <c>Resources/</c> folder. Measured 2026-08-16: <b>zero of the project's 591
        /// materials reference either of these</b>, the Always Included list held only Unity's
        /// built-ins, and the project has no <c>Resources/</c> folder at all. The Editor keeps every
        /// shader resident, so <c>Shader.Find</c> has worked here for 34 units and would have
        /// returned null the first time it ran in a Player - <c>new Material(null)</c>, and the
        /// mission beacons render magenta.
        ///
        /// Call sites: <c>Mission/Beacon.cs</c> (the waypoint markers) and <c>Vfx/Blood.cs</c>
        /// (drops and stains).
        /// </summary>
        private static readonly string[] RuntimeShaders =
        {
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/Particles/Unlit",
        };

        [MenuItem("The Block/Fix Player Settings for Build", priority = 40)]
        public static void FixPlayerSettingsMenu()
        {
            var changes = FixPlayerSettings();
            EditorUtility.DisplayDialog(
                "Player settings",
                changes.Count == 0
                    ? "Already correct - nothing to change."
                    : "Applied:\n\n• " + string.Join("\n• ", changes),
                "OK");
        }

        [MenuItem("The Block/Build macOS Player", priority = 41)]
        public static void BuildRelease() => RunBuild(development: false);

        [MenuItem("The Block/Build macOS Player (Development)", priority = 42)]
        public static void BuildDevelopment() => RunBuild(development: true);

        /// <summary>
        /// The two settings a Player needs and the Editor does not, applied idempotently.
        ///
        /// <b>It deliberately does not touch <c>companyName</c> or <c>productName</c>.</b> On macOS
        /// PlayerPrefs live at <c>~/Library/Preferences/unity.[companyName].[productName].plist</c>,
        /// so renaming either field does not migrate the save - it makes the existing one
        /// unreachable, campaign progress and wallet included. The Editor and the built Player share
        /// that one file, which is also why the build opens on the current test save rather than as
        /// a clean install. Renaming is a U30c decision, to be taken after the video is recorded and
        /// alongside <c>Wallet.startingBalance</c>.
        /// </summary>
        /// <returns>Human-readable descriptions of what actually changed; empty when already correct.</returns>
        public static List<string> FixPlayerSettings()
        {
            var changes = new List<string>();

            var graphicsSettings = GraphicsSettings.GetGraphicsSettings();
            var serialized = new SerializedObject(graphicsSettings);
            var included = serialized.FindProperty("m_AlwaysIncludedShaders");

            var present = new HashSet<Shader>();
            for (int i = 0; i < included.arraySize; i++)
            {
                if (included.GetArrayElementAtIndex(i).objectReferenceValue is Shader shader)
                    present.Add(shader);
            }

            foreach (var name in RuntimeShaders)
            {
                var shader = Shader.Find(name);
                if (shader == null)
                {
                    Debug.LogWarning($"PlayerBuilder: no shader named '{name}' - skipped.");
                    continue;
                }

                if (present.Contains(shader)) continue;

                included.InsertArrayElementAtIndex(included.arraySize);
                included.GetArrayElementAtIndex(included.arraySize - 1).objectReferenceValue = shader;
                present.Add(shader);
                changes.Add($"Always Included Shaders += {name}");
            }

            if (changes.Count > 0) serialized.ApplyModifiedProperties();

            var target = UnityEditor.Build.NamedBuildTarget.Standalone;
            if (PlayerSettings.GetApplicationIdentifier(target) != BundleIdentifier)
            {
                PlayerSettings.SetApplicationIdentifier(target, BundleIdentifier);
                changes.Add($"Bundle Identifier = {BundleIdentifier}");
            }

            if (changes.Count > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log("PlayerBuilder - settings applied:\n  " + string.Join("\n  ", changes));
            }

            return changes;
        }

        /// <summary>
        /// Refuses rather than producing a Player that misleads. Every one of these has bitten this
        /// project or is a documented Unity trap: a build started in Play mode is discarded on Stop
        /// (memory: <c>editor-builds-during-play-are-discarded</c>), a build mid-compile ships the
        /// previous assemblies, and building while the active target is not macOS silently produces
        /// something else.
        /// </summary>
        /// <returns>Null when clear to build; otherwise the reason to show the user.</returns>
        private static string Preflight()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "Play mode is running. Stop it first - a build started in Play mode is discarded on Stop.";

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                // The same pair Unity's own Build Profiles window guards on, worded the same way:
                // "Cannot build player while editor is importing assets or compiling scripts."
                // isCompiling alone misses the import half, which is the half a freshly added
                // script triggers.
                return "The editor is still importing assets or compiling scripts.\n\n" +
                       "Wait for the spinner in the bottom-right corner to clear, then run this again.";
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneOSX)
            {
                return $"The active build target is {EditorUserBuildSettings.activeBuildTarget}, not macOS.\n\n" +
                       "Open File → Build Profiles, select macOS, and press Switch Platform. " +
                       "That reimports assets and takes a while, so it is not done silently here.";
            }

            return null;
        }

        private static void RunBuild(bool development)
        {
            var blocked = Preflight();
            if (blocked != null)
            {
                EditorUtility.DisplayDialog("Cannot build", blocked, "OK");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            FixPlayerSettings();

            // From EditorBuildSettings rather than a hardcoded pair, so this cannot drift from
            // Boot (index 0) / World (index 1) if the scene list is ever reordered.
            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "Cannot build",
                    "No scenes are enabled in File → Build Profiles → Scene List.",
                    "OK");
                return;
            }

            var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            var outputPath = Path.Combine(projectRoot, OutputDirectory, AppName);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.StandaloneOSX,
                targetGroup = BuildTargetGroup.Standalone,
                options = development
                    ? BuildOptions.Development | BuildOptions.ConnectWithProfiler | BuildOptions.AllowDebugging
                    : BuildOptions.None,
            };

            Debug.Log(
                $"PlayerBuilder - building {(development ? "DEVELOPMENT" : "release")} macOS Player\n" +
                $"  scenes: {string.Join(", ", scenes)}\n" +
                $"  output: {outputPath}");

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError(
                    $"PlayerBuilder - build {summary.result}. " +
                    $"{summary.totalErrors} error(s) after {summary.totalTime:mm\\:ss}.");

                EditorUtility.DisplayDialog(
                    "Build failed",
                    $"Result: {summary.result}\nErrors: {summary.totalErrors}\n" +
                    $"Time: {summary.totalTime:mm\\:ss}\n\nSee the Console for the first error.",
                    "OK");
                return;
            }

            double megabytes = summary.totalSize / (1024.0 * 1024.0);
            var binary = Path.Combine(outputPath, "Contents/MacOS");

            Debug.Log(
                $"PlayerBuilder - build SUCCEEDED in {summary.totalTime:mm\\:ss}, " +
                $"{megabytes:N0} MB at {outputPath}\n" +
                $"  Verify the architecture - the one setting this script does not control:\n" +
                $"    lipo -archs \"{binary}/\"*\n" +
                $"  Expect: x86_64 arm64");

            EditorUtility.DisplayDialog(
                "Build succeeded",
                $"{megabytes:N0} MB in {summary.totalTime:mm\\:ss}\n\n{outputPath}\n\n" +
                "Next: quit Unity and launch it from Finder, then check a mission beacon is " +
                "coloured rather than magenta.",
                "OK");

            EditorUtility.RevealInFinder(outputPath);
        }
    }
}
