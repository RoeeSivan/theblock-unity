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
    /// Idempotent, like every other builder here — running it again tears the generated objects
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

        private const string JoeModelPath = "Assets/Models/Characters/Joe.fbx";
        private const string JoeControllerPath = "Assets/Animation/Joe.controller";

        /// <summary>The web's PREVIEW_HEIGHT_M: every body is normalised to this so one camera framing fits all.</summary>
        private const float PreviewHeight = 1.7f;

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
                    "Open Assets/Scenes/World.unity first — the menus go on the HUD document that " +
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
            // — the whole shell would live in memory until the next domain reload discarded it.
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
            // freeze — the only thing that differs is that E rather than a menu button opens it.
            Replace<ShopMenu>(hud);

            Replace<MissionLaunch>(hud);
            Replace<ScreenFade>(hud);

            // GameFlow last: its Awake caches the panels above, and a component added after it would
            // be found as null on the frame the title screen first tries to open.
            Replace<GameFlow>(hud);

            var characterSerialized = new SerializedObject(character);
            characterSerialized.FindProperty("preview").objectReferenceValue = preview;

            // The roster, seeded with the only body this port has. U29 adds Jody and David here and
            // two more rigs under the turntable; nothing else about the screen changes.
            var roster = characterSerialized.FindProperty("roster");
            roster.arraySize = 1;
            var joe = roster.GetArrayElementAtIndex(0);
            joe.FindPropertyRelative("Id").stringValue = "joe";
            joe.FindPropertyRelative("Name").stringValue = "Joe";
            characterSerialized.ApplyModifiedPropertiesWithoutUndo();

            var map = Object.FindAnyObjectByType<TheBlock.UI.GameMap>();
            if (map != null)
            {
                var settingsSerialized = new SerializedObject(settings);
                settingsSerialized.FindProperty("map").objectReferenceValue = map;
                settingsSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            // Left unwired on purpose: TitleMenu finds the campaign itself, and the campaign object
            // is rebuilt by Build Campaign — a serialized reference here would go stale every time
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

            // 20 m of far plane is the whole culling strategy — there is nothing else down here, so
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

            var body = LoadJoe();
            if (body != null)
            {
                body.transform.SetParent(turntable.transform, false);
                Normalise(body);

                if (!body.TryGetComponent<Animator>(out var animator))
                    animator = body.AddComponent<Animator>();

                animator.runtimeAnimatorController =
                    AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(JoeControllerPath);

                // The menu is on screen exactly when timeScale is 0. A Normal animator would stand
                // in the bind pose for as long as the player looks at it.
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            else
            {
                Debug.LogWarning($"MenuBuilder: {JoeModelPath} not found — the character panel will " +
                                 "show an empty frame.");
            }

            var preview = root.AddComponent<CharacterPreview>();
            preview.Bind(camera, turntable.transform);

            SceneManager.MoveGameObjectToScene(root, EditorSceneManager.GetActiveScene());
            return preview;
        }

        private static GameObject LoadJoe()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(JoeModelPath);
            if (source == null) return null;

            var body = (GameObject)PrefabUtility.InstantiatePrefab(source);
            body.name = "Joe";
            PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            return body;
        }

        /// <summary>Scales the body to <see cref="PreviewHeight"/> and stands it on its own floor.</summary>
        private static void Normalise(GameObject body)
        {
            var renderers = body.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            if (bounds.size.y <= 0.0001f) return;

            var scale = PreviewHeight / bounds.size.y;
            body.transform.localScale = Vector3.one * scale;

            // Re-read after scaling: the bounds moved with it.
            bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            var parentY = body.transform.parent != null ? body.transform.parent.position.y : 0f;
            body.transform.localPosition = new Vector3(
                body.transform.localPosition.x - (bounds.center.x - body.transform.position.x),
                body.transform.localPosition.y + (parentY - bounds.min.y),
                body.transform.localPosition.z);
        }

        // ── input plumbing ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A runtime UI Toolkit panel gets its pointer events through an <see cref="EventSystem"/>.
        /// Nothing in this project has needed one until now — every control the game has had is a
        /// key — so the scene does not contain one, and without it every button here is inert while
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
        /// opens on, and <c>SceneManager.LoadScene("Boot")</c> — Quit to Title — needs World listed
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
