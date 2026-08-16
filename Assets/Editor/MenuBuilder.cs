using System.IO;
using TheBlock.Boot;
using TheBlock.UI.Menus;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Builds U26's shell: the menu components on the existing HUD document, the character
    /// preview rig, and the Boot scene that shows the loading bar.
    ///
    /// Idempotent, like every other builder here - running it again tears the generated objects
    /// down and remakes them. It does NOT touch the HUD document itself, the map, or anything U14
    /// through U24 put in the scene; it adds components beside them.
    ///
    /// Run it with <c>World.unity</c> open.
    /// </summary>
    public static class MenuBuilder
    {
        private const string HudName = "HUD";
        private const string PreviewName = "Character Preview";
        private const string EventSystemName = "EventSystem";

        private const string WorldScenePath = "Assets/Scenes/World.unity";
        private const string BootScenePath = "Assets/Scenes/Boot.unity";
        private const string PanelSettingsPath = "Assets/UI/HudPanelSettings.asset";

        // The web's PREVIEW_HEIGHT_M normalised every body to 1.7 m so one camera framing fits all.
        // It has no equivalent here since U29: the roster bodies are already matched to Joe's height
        // at import time, so the framing below is right for every one of them by construction.

        /// <summary>Two kilometres under the map. The preview camera's far plane is the culling.</summary>
        private static readonly Vector3 PreviewOrigin = new(0f, -2000f, 0f);

        [MenuItem("The Block/Build Menus", priority = 6)]
        public static void Build()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.path.EndsWith("World.unity"))
            {
                EditorUtility.DisplayDialog(
                    "Build Menus",
                    "Open Assets/Scenes/World.unity first - the menus go on the HUD document that " +
                    "lives in it.",
                    "OK");
                return;
            }

            var hud = GameObject.Find(HudName);
            if (hud == null || hud.GetComponent<UIDocument>() == null)
            {
                EditorUtility.DisplayDialog(
                    "Build Menus",
                    "No HUD with a UIDocument in this scene. Run The Block → Build Map HUD first.",
                    "OK");
                return;
            }

            var preview = BuildCharacterPreview();
            EnsureEventSystem(scene);
            AddMenuComponents(hud, preview);
            BuildBootScene();
            RegisterScenes();

            // `new GameObject` does not dirty a scene, and an unmarked scene is not written by Save
            // - the whole shell would live in memory until the next domain reload discarded it.
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log("MenuBuilder: built the title/pause/settings/controls/character panels on " +
                      $"{HudName}, the preview rig at y {PreviewOrigin.y}, and {BootScenePath}. " +
                      "Save the scene, then Play from Boot.unity.");
        }

        // ── the menu components ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Every panel goes on the ONE UIDocument, beside U14's map and U20's card. Order of
        /// addition is the order they are drawn in, and it is deliberately menus-then-fade: a
        /// teleport that happens with a panel open still has to be hidden by the black. Each panel
        /// also calls BringToFront on the way up, because an add order is not a contract.
        /// </summary>
        private static void AddMenuComponents(GameObject hud, CharacterPreview preview)
        {
            var title = Replace<TitleMenu>(hud);
            Replace<PauseMenu>(hud);
            Replace<ControlsGuide>(hud);
            var settings = Replace<SettingsPanel>(hud);
            var character = Replace<CharacterPanel>(hud);

            // U28's counter. A panel like any other, so Esc layers over it and the freeze is the same
            // freeze - the only thing that differs is that E rather than a menu button opens it.
            Replace<ShopMenu>(hud);

            Replace<MissionLaunch>(hud);
            Replace<ScreenFade>(hud);

            // GameFlow last: its Awake caches the panels above, and a component added after it would
            // be found as null on the frame the title screen first tries to open.
            Replace<GameFlow>(hud);

            var characterSerialized = new SerializedObject(character);
            characterSerialized.FindProperty("preview").objectReferenceValue = preview;

            // The roster itself is NOT seeded here any more. U26 hand-wrote a list of one because
            // Joe was the only body; U29 gave the game a CharacterRoster component and the panel
            // reads that, so the names on these buttons and the bodies they apply can no longer
            // drift apart. The reference is resolved at runtime - a serialized one would go stale
            // every time Build Characters ran, which is a failure this project has already had.
            characterSerialized.ApplyModifiedPropertiesWithoutUndo();

            var map = Object.FindAnyObjectByType<TheBlock.UI.GameMap>();
            if (map != null)
            {
                var settingsSerialized = new SerializedObject(settings);
                settingsSerialized.FindProperty("map").objectReferenceValue = map;
                settingsSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            // Left unwired on purpose: TitleMenu finds the campaign itself, and the campaign object
            // is rebuilt by Build Campaign - a serialized reference here would go stale every time
            // that ran, which is the failure this project has already had once.
            if (title == null) Debug.LogWarning("MenuBuilder: TitleMenu did not attach.");
        }

        /// <summary>Removes any previous copy, then adds a fresh one. Never `?? AddComponent`.</summary>
        private static T Replace<T>(GameObject host) where T : Component
        {
            foreach (var existing in host.GetComponents<T>()) Object.DestroyImmediate(existing);
            return host.AddComponent<T>();
        }

        // ── the character preview ─────────────────────────────────────────────────────────────

        /// <summary>
        /// A camera and one Joe, two kilometres under the world.
        ///
        /// The body is NORMALISED to 1.7 m and stood on y = 0 of its own little space, which is the
        /// web build's own framing rule (<c>PREVIEW_HEIGHT_M</c>) and the reason the camera is one
        /// setting rather than one per character: whatever height a future roster entry imports at,
        /// it is 1.7 m here.
        /// </summary>
        private static CharacterPreview BuildCharacterPreview()
        {
            var old = GameObject.Find(PreviewName);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(PreviewName);
            root.transform.position = PreviewOrigin;

            // The web's CAM_POS / CAM_TARGET: (0, 1.1, 2.9) looking at (0, 0.95, 0), 38° vertical.
            var cameraGo = new GameObject("Preview Camera");
            cameraGo.transform.SetParent(root.transform, false);
            cameraGo.transform.localPosition = new Vector3(0f, 1.1f, 2.9f);
            cameraGo.transform.LookAt(root.transform.position + new Vector3(0f, 0.95f, 0f));

            var camera = cameraGo.AddComponent<Camera>();
            camera.fieldOfView = 38f;
            camera.nearClipPlane = 0.05f;

            // 20 m of far plane is the whole culling strategy - there is nothing else down here, so
            // no preview layer and no TagManager edit.
            camera.farClipPlane = 20f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.078f, 0.106f, 0.141f, 0f);
            camera.depth = -50f; // never competes with the game camera for the display

            var urp = cameraGo.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = false;
            urp.renderShadows = false;

            var turntable = new GameObject("Turntable");
            turntable.transform.SetParent(root.transform, false);

            var lights = BuildPreviewLights(root.transform);

            var preview = root.AddComponent<CharacterPreview>();
            preview.Bind(camera, turntable.transform, lights);

            SceneManager.MoveGameObjectToScene(root, EditorSceneManager.GetActiveScene());

            // U29: the turntable wears a roster body like every other host, so the body is not built
            // here any more - CharacterPrefabBuilder puts a CharacterBody on it with the Animator
            // settings a menu needs (AlwaysAnimate, and UnscaledTime because this screen is on
            // precisely when timeScale is 0). Called back into rather than ordered, so it does not
            // matter which of the two menu items was run last.
            Debug.Log($"MenuBuilder - {CharacterPrefabBuilder.DressTurntable()}");

            return preview;
        }

        /// <summary>
        /// The web preview's three lights, rebuilt under Unity's one real constraint.
        ///
        /// <c>character-select.ts</c> adds a <c>HemisphereLight(0xffffff, 0x333344, 2.2)</c>, a warm
        /// <c>DirectionalLight(0xffd7a8, 2.6)</c> at (2, 4, 3) and a cool
        /// <c>DirectionalLight(0x88bbff, 1.4)</c> at (−3, 2, −2). U26 ported the camera and the
        /// turntable and none of this, so the body was lit only by the world's sun - which is
        /// two kilometres above it and pointed wherever the day/night cycle left it.
        ///
        /// <b>Both directionals become POINT lights, and that is not a downgrade.</b> A directional
        /// light in Unity has no position: one added down here would light all 963 × 805 m of the
        /// city as a second sun, and URP only honours one main directional anyway. A point light
        /// with a 6 m range cannot reach anything but the body - the same "the far plane is the
        /// culling" trick the camera already uses, applied to light instead of geometry.
        ///
        /// <b>Shadows off on all three.</b> The preview camera does not render shadows, so they
        /// would cost an entry in the 2048² atlas the world is already overflowing and buy nothing.
        /// </summary>
        private static Transform BuildPreviewLights(Transform root)
        {
            var rig = new GameObject("Lights");
            rig.transform.SetParent(root, false);

            // Directions are the web's own light POSITIONS, which in three.js is what a directional
            // light's direction is expressed as. Placed at that direction, 2.5 m out, so the angles
            // on the face are the ones that screen was designed around.
            // Intensities match CharacterPreview's serialized defaults, which is where the
            // measurement behind them is written down. They must agree: the component re-pushes its
            // own values onto this rig every time the screen opens.
            MakePreviewLight(rig.transform, "Key", new Vector3(2f, 4f, 3f), new Color32(0xFF, 0xD7, 0xA8, 0xFF), 24f);
            MakePreviewLight(rig.transform, "Rim", new Vector3(-3f, 2f, -2f), new Color32(0x88, 0xBB, 0xFF, 0xFF), 14f);

            // The hemisphere has no local equivalent - ambient in URP is one global setting and this
            // rig may not touch it. A soft, slightly cool frontal light standing where the camera is
            // does the job it was there for: lifting the side the key does not reach.
            MakePreviewLight(rig.transform, "Fill", new Vector3(0f, 1.2f, 3f), new Color32(0xEA, 0xEE, 0xFF, 0xFF), 10f);

            return rig.transform;
        }

        private static void MakePreviewLight(
            Transform parent, string name, Vector3 direction, Color color, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = direction.normalized * 2.5f + new Vector3(0f, 0.95f, 0f);

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;

            // Six metres reaches every part of a 1.7 m body from 2.5 m away and nothing else. The
            // world starts two kilometres up.
            light.range = 6f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
        }

        // ── input plumbing ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A runtime UI Toolkit panel gets its pointer events through an <see cref="EventSystem"/>.
        /// Nothing in this project has needed one until now - every control the game has had is a
        /// key - so the scene does not contain one, and without it every button here is inert while
        /// looking perfectly fine.
        /// </summary>
        private static void EnsureEventSystem(Scene scene)
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;

            var go = new GameObject(EventSystemName);
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
            SceneManager.MoveGameObjectToScene(go, scene);
        }

        // ── the boot scene ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A camera, a panel and the loader. Everything else about the shell is in World.
        ///
        /// Built additively and saved without ever being opened, so running this does not disturb
        /// the world scene the user has open.
        /// </summary>
        private static void BuildBootScene()
        {
            var folder = Path.GetDirectoryName(BootScenePath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets", "Scenes");

            var boot = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            var cameraGo = new GameObject("Boot Camera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = MenuStyle.Backdrop;
            camera.cullingMask = 0; // there is nothing in this scene to draw; the UI is the screen
            cameraGo.AddComponent<AudioListener>();
            cameraGo.AddComponent<UniversalAdditionalCameraData>();
            SceneManager.MoveGameObjectToScene(cameraGo, boot);

            var uiGo = new GameObject("Loading");
            var document = uiGo.AddComponent<UIDocument>();
            document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            var screen = uiGo.AddComponent<LoadingScreen>();
            var loader = uiGo.AddComponent<BootLoader>();
            SceneManager.MoveGameObjectToScene(uiGo, boot);

            var loaderSerialized = new SerializedObject(loader);
            loaderSerialized.FindProperty("screen").objectReferenceValue = screen;
            loaderSerialized.ApplyModifiedPropertiesWithoutUndo();

            var eventSystem = new GameObject(EventSystemName);
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();
            SceneManager.MoveGameObjectToScene(eventSystem, boot);

            EditorSceneManager.MarkSceneDirty(boot);
            EditorSceneManager.SaveScene(boot, BootScenePath);
            EditorSceneManager.CloseScene(boot, true);
        }

        /// <summary>
        /// Boot first, World second. The order is the whole point: index 0 is what a built player
        /// opens on, and <c>SceneManager.LoadScene("Boot")</c> - Quit to Title - needs World listed
        /// at all or it cannot come back.
        /// </summary>
        private static void RegisterScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootScenePath, true),
                new EditorBuildSettingsScene(WorldScenePath, true),
            };
        }
    }
}
