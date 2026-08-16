using TheBlock.Core;
using TheBlock.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Writes the street haze into the scene's render settings, and the shadow distance into the
    /// URP asset.
    ///
    /// This is the half of <c>config.camera.far</c> that was never ported. See
    /// <see cref="Atmosphere"/> for why the far plane on its own is a visible knife through the
    /// skyline, and why the fog's ratios rather than its metres are what carry across.
    ///
    /// It runs at BUILD time rather than in a MonoBehaviour because fog is scene state and the
    /// scene is WorldBuilder's output.
    ///
    /// <b>U33 made what this writes the reference for a second thing.</b> With Settings → Display →
    /// Time of Day on Fixed — the default — <see cref="DayNightCycle"/> does not write the sky at
    /// all; it replays the values found here at <c>Awake</c>. So this function defines what "off"
    /// looks like, and the palette's <see cref="SkyPalette.AnchorHour"/> stop is authored to match
    /// it. Change the fog colour here and that stop has to move with it, or switching the setting on
    /// will visibly jump.
    /// </summary>
    public static partial class WorldBuilder
    {
        /// <summary>
        /// Linear fog in the config's own colour, over <see cref="Atmosphere"/>'s rescaled band.
        ///
        /// Linear and not exponential on purpose: the web build's <c>THREE.Fog</c> is linear, and
        /// its near/far are the two numbers the look was authored against. URP's exponential modes
        /// take a density instead, which would mean inventing a value and losing the relationship
        /// to the far plane that keeps the clip hidden.
        /// </summary>
        private static void BuildAtmosphere(TheBlockConfig.Root config, Report report)
        {
            var color = Atmosphere.FogColor(config);
            var (start, end) = Atmosphere.FogRange(config.Fog);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = color;
            RenderSettings.fogStartDistance = start;
            RenderSettings.fogEndDistance = end;

            if (config.Fog == null)
                report.Warnings.Add("fog — config has no `fog` section; fell back to the background colour");

            report.Placed.Add(
                $"Atmosphere — linear fog {ColorUtility.ToHtmlStringRGB(color)} over {start:0}–{end:0} m, " +
                $"camera draws to {Atmosphere.DrawDistance:0} m " +
                $"(config says {config.Camera?.Far ?? 0:0} m — three.js budget, not design)");

            ApplyShadowDistance(report);
        }

        /// <summary>
        /// Shadow distance lives on the URP asset, not on <c>QualitySettings</c>, whenever URP is
        /// the active pipeline — setting the latter is silently ignored.
        ///
        /// It is raised here rather than left to U30 because it is part of the same fault: at 40 m
        /// nothing past the end of the street casts a shadow, which was invisible while the world
        /// was clipped at 320 m and is not once it reaches 1500.
        /// </summary>
        private static void ApplyShadowDistance(Report report)
        {
            var asset = UniversalRenderPipeline.asset;
            if (asset == null)
            {
                report.Warnings.Add("shadow distance skipped — URP is not the active render pipeline");
                return;
            }

            if (Mathf.Approximately(asset.shadowDistance, Atmosphere.ShadowDistance)) return;

            var was = asset.shadowDistance;
            asset.shadowDistance = Atmosphere.ShadowDistance;
            EditorUtility.SetDirty(asset);
            report.Notes.Add(
                $"{AssetDatabase.GetAssetPath(asset)}: shadow distance {was:0} → {Atmosphere.ShadowDistance:0} m");
        }
    }
}
