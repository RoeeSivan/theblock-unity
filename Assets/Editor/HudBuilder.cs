using System.IO;
using TheBlock.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Builds the HUD scaffolding for U14: the UI Toolkit panel with <see cref="GameMap"/> on it,
    /// and the top-down <see cref="MapCamera"/> it reads from. One menu item, idempotent — running
    /// it again tears down and rebuilds both objects, like WorldBuilder does for the world.
    ///
    /// UI Toolkit needs a <see cref="PanelSettings"/> asset with a theme; neither exists in a fresh
    /// URP project until the first .uxml is created by hand, so this creates both: a minimal .tss
    /// that imports Unity's default runtime theme, and a PanelSettings asset pointing at it.
    ///
    /// <b>⚠ THIS ITEM DELETES THE WHOLE MENU SHELL, and nothing else says so.</b> It destroys the
    /// <c>HUD</c> GameObject before rebuilding it, and U26's <c>MenuBuilder</c> puts every menu
    /// component on that same object — <c>TitleMenu</c>, <c>PauseMenu</c>, <c>ControlsGuide</c>,
    /// <c>SettingsPanel</c>, <c>CharacterPanel</c>, <c>ShopMenu</c>, <c>MissionLaunch</c>,
    /// <c>ScreenFade</c>, <c>GameFlow</c> — along with their serialized wiring to the preview rig
    /// and the map. Running this on a built scene therefore ships a game that boots to nothing.
    /// It is recoverable: run <b>The Block → Build Menus</b> straight after. Better still, do not
    /// run it at all — every later unit that adds a HUD element also installs it idempotently from
    /// its own menu item, exactly so this door can stay shut.
    /// </summary>
    public static class HudBuilder
    {
        private const string HudName = "HUD";
        private const string CameraName = "Map Camera";
        private const string ThemePath = "Assets/UI/DefaultRuntimeTheme.tss";
        private const string PanelSettingsPath = "Assets/UI/HudPanelSettings.asset";

        [MenuItem("The Block/Build Map HUD", priority = 2)]
        public static void Build()
        {
            var panelSettings = LoadOrCreatePanelSettings();

            var oldHud = GameObject.Find(HudName);
            if (oldHud != null) Object.DestroyImmediate(oldHud);
            var oldCam = GameObject.Find(CameraName);
            if (oldCam != null) Object.DestroyImmediate(oldCam);

            // The top-down camera. Disabled component-wise is wrong — MapCamera.Awake needs to run —
            // so the Camera itself is left enabled here and MapCamera turns it off in Awake.
            var camGo = new GameObject(CameraName);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            var urp = camGo.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = false;
            urp.renderShadows = false;
            var mapCamera = camGo.AddComponent<MapCamera>();

            var hud = new GameObject(HudName);
            var document = hud.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            hud.AddComponent<MapPois>();
            var map = hud.AddComponent<GameMap>();

            // U19's stars and BUSTED overlay go on THIS document, beside the map. The ledger's U25
            // row is explicit that the panel built here is the one every later HUD element extends —
            // a second UIDocument means a second PanelSettings and an argument about z-order.
            hud.AddComponent<WantedHud>();

            // U20's mission surfaces, on the same document for the same reason. ORDER MATTERS: UI
            // Toolkit draws later children on top, and each of these appends to the shared root in
            // its own Start. The card is added last so it covers everything — and it calls
            // BringToFront on open as well, because a Start order is not a contract.
            hud.AddComponent<MissionHud>();

            // U28's power-up strip, under the cash readout WantedHud draws. Before the card for the
            // same reason everything is: the card has to cover it.
            hud.AddComponent<PowerUpChips>();

            // The km/h readout and the sprint bar — the last two surfaces of hud.ts that nothing had
            // built. The bar deliberately takes the slot U28b's fuel gauge will share.
            hud.AddComponent<PlayerMeters>();

            // U28b's fuel bar, which takes the sprint bar's slot — the two are mutually exclusive by
            // mode. It is ALSO installed idempotently by The Block → Build Gas Station, and that is
            // the door to use: see the warning on this class.
            hud.AddComponent<FuelGauge>();

            hud.AddComponent<BriefingCard>();

            var so = new SerializedObject(map);
            so.FindProperty("mapCamera").objectReferenceValue = mapCamera;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Objects made with `new GameObject` do not dirty the scene by themselves, and an
            // unmarked scene is not written by Save — the HUD would live in memory until the next
            // domain reload threw it away. Same call WorldBuilder ends on.
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log("HudBuilder: built HUD (map, wanted, mission, briefing) + Map Camera. " +
                      "Enter Play and press M to expand the map.");
        }

        private static PanelSettings LoadOrCreatePanelSettings()
        {
            var existing = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (existing != null) return existing;

            var folder = Path.GetDirectoryName(PanelSettingsPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "UI");

            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme == null)
            {
                File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(ThemePath);
                theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            }

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "HudPanelSettings";
            settings.themeStyleSheet = theme;
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            AssetDatabase.SaveAssets();
            return settings;
        }
    }
}
