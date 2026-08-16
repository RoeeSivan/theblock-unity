using System.IO;
using TheBlock.Missions;
using TheBlock.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Builds U33's day/night rig into <c>World.unity</c>: the clock on the scene's directional
    /// light, a global grading Volume, and the post-processing data the Main Camera has never had.
    ///
    /// <b>A menu item rather than a hand edit, for the same reason everything else here is one.</b>
    /// A Unity scene does not merge, so three components placed by hand are three things a second
    /// session can silently overwrite and nobody can diff. Run this again and the rig comes back
    /// identically. Idempotent: it replaces its own output and touches nothing else.
    ///
    /// <b>It deliberately leaves the scene looking exactly as it did.</b> The Volume is created with
    /// neutral overrides and <c>renderPostProcessing</c> is written FALSE, so a freshly built scene
    /// is byte-for-byte the daylight every unit from U11 to U27 was tested against. The look only
    /// changes when a player turns Settings → Display → Time of Day to Cycle.
    ///
    /// Run it with <c>World.unity</c> open.
    /// </summary>
    public static class DayNightBuilder
    {
        private const string VolumeRootName = "Day-Night Volume";
        private const string ProfilePath = "Assets/Settings/DayNightProfile.asset";

        /// <summary>
        /// Real seconds per in-game day. 2880 = 48 minutes, GTA V's own pace and the user's call on
        /// 2026-08-16 after play-testing the 24-minute version.
        ///
        /// <b>Written here rather than left to the C# field initialiser</b>, because the scene has
        /// already serialized a value for that field and a serialized value beats the default —
        /// re-tuning the initialiser would change nothing and report nothing
        /// (memory: <c>scene-serialized-value-beats-cs-default</c>).
        /// </summary>
        private const float DayLengthSeconds = 2880f;

        /// <summary>
        /// Builds the rig ready to SHIP: cycle off, 24-minute day, no banner. This is what the scene
        /// must be committed in.
        /// </summary>
        [MenuItem("The Block/Build Day-Night", priority = 7)]
        public static void Build() => Build(false);

        /// <summary>
        /// Builds the rig ready to LOOK AT: cycle forced on, a full day in two minutes, the clock
        /// running behind the title screen, and a banner in the corner saying so. Press Play and the
        /// sky moves — nothing to click, no Settings, no Boot scene.
        ///
        /// Run <b>Build Day-Night</b> again to put it back before committing. The banner exists so
        /// that "left in a test setting" cannot happen silently.
        /// </summary>
        [MenuItem("The Block/Build Day-Night (Test Mode)", priority = 8)]
        public static void BuildTest() => Build(true);

        private static void Build(bool testMode)
        {
            // Unity discards scene and component edits made during Play, silently and on Stop
            // (the same class of trap as PlayerSettings edits during Play). Building from inside a
            // Play session would report success, look right for as long as the session lasted, and
            // revert the moment it ended — which is the worst way for this to fail, because the log
            // line says it worked.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    "Build Day-Night",
                    "Stop Play first. Unity throws away scene edits made during a Play session, so " +
                    "this would report success and then quietly revert on Stop.",
                    "OK");
                return;
            }

            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.path.EndsWith("World.unity"))
            {
                EditorUtility.DisplayDialog(
                    "Build Day-Night",
                    "Open Assets/Scenes/World.unity first — the clock goes on that scene's " +
                    "directional light.",
                    "OK");
                return;
            }

            var sun = FindSun();
            if (sun == null)
            {
                EditorUtility.DisplayDialog(
                    "Build Day-Night",
                    "No directional light in this scene. The cycle has nothing to rotate.",
                    "OK");
                return;
            }

            var profile = BuildProfile();
            var volume = BuildVolume(profile);
            var camera = Camera.main;
            var campaign = Object.FindAnyObjectByType<Campaign>();

            // Never `?? AddComponent` — a destroyed component reads as non-null
            // (memory: unity-null-coalescing-fake-null).
            foreach (var existing in sun.GetComponents<DayNightCycle>()) Object.DestroyImmediate(existing);
            var cycle = sun.gameObject.AddComponent<DayNightCycle>();

            var serialized = new SerializedObject(cycle);
            serialized.FindProperty("sun").objectReferenceValue = sun;
            serialized.FindProperty("grade").objectReferenceValue = volume;
            serialized.FindProperty("view").objectReferenceValue = camera;
            serialized.FindProperty("campaign").objectReferenceValue = campaign;
            serialized.FindProperty("dayLengthSeconds").floatValue = DayLengthSeconds;
            serialized.FindProperty("testMode").boolValue = testMode;
            serialized.FindProperty("scrub").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Test Mode turns post-processing on from Start, but the SAVED scene must still be the
            // shipped state — see BuildCameraData.
            var cameraNote = BuildCameraData(camera);

            // `new GameObject` and AddComponent do not dirty a scene, and an unmarked scene is not
            // written by Save (memory: editor-created-objects-need-markscenedirty).
            EditorSceneManager.MarkSceneDirty(scene);

            var tail = testMode
                ? "TEST MODE: press Play from World.unity and the sky moves behind the title — " +
                  "a full day in 2 minutes, [ and ] step an hour, \\ holds the clock. " +
                  "Run The Block → Build Day-Night to put it back before committing."
                : "The setting defaults to Fixed, so nothing changes until a player turns it on.";

            Debug.Log(
                $"DayNightBuilder: clock on '{sun.name}', volume '{VolumeRootName}' → {ProfilePath}, " +
                $"{cameraNote} Campaign {(campaign == null ? "NOT found — the mission hold is off" : "wired")}. " +
                $"Save the scene. {tail}");
        }

        /// <summary>
        /// The scene's key light. Found by TYPE rather than by name — the object is called
        /// "Directional Light" today, but a rename would silently produce a cycle with nothing to
        /// rotate, and that failure looks like "the setting does nothing".
        /// </summary>
        private static Light FindSun()
        {
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (light.type == LightType.Directional)
                    return light;
            return null;
        }

        /// <summary>
        /// Tonemapping, ColorAdjustments, WhiteBalance. <b>No Bloom, and that is the perf decision:</b>
        /// bloom is six to eight downsample/upsample passes against a frame already measuring 20.7 ms,
        /// where the whole of this grading stack is one LUT and one fullscreen blit.
        ///
        /// Rebuilt from scratch each run so the shipped values cannot drift from this file.
        /// </summary>
        private static VolumeProfile BuildProfile()
        {
            var dir = Path.GetDirectoryName(ProfilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            AssetDatabase.DeleteAsset(ProfilePath);

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);

            // Neutral, not ACES: ACES crushes the shadows and shifts the whole palette, and this
            // scene's colours were approved under no tonemapper at all. Neutral is the smallest step
            // away from that which still keeps a low sun from clipping to white.
            var tonemapping = Add<Tonemapping>(profile);
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.Neutral;

            var color = Add<ColorAdjustments>(profile);
            color.postExposure.overrideState = true;
            color.postExposure.value = 0f;
            color.saturation.overrideState = true;
            color.saturation.value = 0f;

            var balance = Add<WhiteBalance>(profile);
            balance.temperature.overrideState = true;
            balance.temperature.value = 0f;
            balance.tint.overrideState = true;
            balance.tint.value = 0f;

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        /// <summary>
        /// <c>VolumeProfile.Add</c> builds the component but does not make it a sub-asset — that is
        /// the profile inspector's job, and without it the overrides are lost on the next import.
        /// </summary>
        private static T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var component = profile.Add<T>();
            component.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        /// <summary>
        /// A global Volume at the scene root — root because everything under <c>World</c> is
        /// WorldBuilder's output and is destroyed on every build.
        /// </summary>
        private static Volume BuildVolume(VolumeProfile profile)
        {
            var existing = GameObject.Find(VolumeRootName);
            if (existing != null) Object.DestroyImmediate(existing);

            var go = new GameObject(VolumeRootName);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.weight = 1f;
            volume.sharedProfile = profile;

            // Off in the saved scene. DayNightCycle.SetEnabled turns it on with the setting, so a
            // Fixed profile never evaluates a volume stack it is not using.
            volume.enabled = false;
            return volume;
        }

        /// <summary>
        /// The Main Camera has no <c>UniversalAdditionalCameraData</c> in the scene at all, so there
        /// is no post-processing switch to flip at runtime. Adds one, with the switch OFF.
        /// </summary>
        private static string BuildCameraData(Camera camera)
        {
            if (camera == null) return "no Main Camera found — grading will not render;";

            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            EditorUtility.SetDirty(data);
            return $"camera data on '{camera.name}' (post off);";
        }
    }
}
