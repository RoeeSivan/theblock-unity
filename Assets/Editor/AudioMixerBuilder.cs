using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using TheBlock.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Builds <c>Assets/Audio/GameMixer.mixer</c> - seven groups, seven exposed volume parameters
    /// and four snapshots - from a menu item instead of from twenty minutes of clicking.
    ///
    /// <b>Every call in here is reflection, and that is not a preference.</b> Unity ships no public
    /// API for AUTHORING an AudioMixer: <c>AudioMixer</c> is read-only at runtime and the editing
    /// surface (<c>AudioMixerController</c>, <c>AudioMixerGroupController</c>,
    /// <c>AudioGroupParameterPath</c>, <c>ExposedAudioParameter</c>) is <c>internal</c> to
    /// <c>UnityEditor.dll</c>. The alternative was a numbered list of menu clicks in
    /// <c>PORT-STATUS.md</c> for a project whose whole rule is that a setting nobody re-runs is a
    /// setting that reverts. Each call below was probed against 6000.5.8f1 through the MCP bridge
    /// before it was written down, so this is measured rather than hoped for.
    ///
    /// <b>It will not overwrite an existing mixer.</b> Volumes are exactly the kind of thing that
    /// gets tuned by hand with the game running, and a build step that silently discards that would
    /// be worse than no build step. Run it against an existing asset and it VALIDATES instead,
    /// naming precisely what is missing.
    ///
    /// If a future Unity moves these internals, the failure is loud and lands here - and the fallback
    /// is the same asset built by hand, which the log tells you how to check.
    /// </summary>
    public static class AudioMixerBuilder
    {
        public const string MixerPath = "Assets/Audio/GameMixer.mixer";

        /// <summary>The buses. Master is implicit - every mixer has one.</summary>
        public static readonly string[] Groups =
            { "Music", "Voice", "Sfx", "Engine", "Ambient", "Radio" };

        /// <summary>
        /// The snapshots, and what they are for. The web multiplies <c>ambientAudio.duck</c> into
        /// each bed's gain by hand at every call site; here the duck IS the Ambient bus's volume in
        /// a snapshot, so one transition moves the whole bus and U26's settings slider can sit on
        /// the same parameter without fighting it.
        /// </summary>
        public const string SnapshotDefault = "Default";
        public const string SnapshotDriving = "Driving";
        public const string SnapshotInterior = "Interior";
        public const string SnapshotRhythm = "Rhythm";

        public static readonly string[] Snapshots =
            { SnapshotDefault, SnapshotDriving, SnapshotInterior, SnapshotRhythm };

        /// <summary>Exposed parameter name for a group's volume - <c>volMaster</c>, <c>volSfx</c>, …</summary>
        public static string VolumeParam(string group) => "vol" + group;

        private static readonly Assembly Ed = typeof(Editor).Assembly;
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static Type T(string name) =>
            Ed.GetType(name) ?? throw new InvalidOperationException(
                $"AudioMixerBuilder: this Unity has no {name}. The mixer must be built by hand - " +
                "see the class comment.");

        [MenuItem("The Block/Build Audio Mixer")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath) != null)
            {
                Debug.Log(Validate());
                return;
            }

            var ctrlType = T("UnityEditor.Audio.AudioMixerController");
            var groupType = T("UnityEditor.Audio.AudioMixerGroupController");
            var pathType = T("UnityEditor.Audio.AudioGroupParameterPath");
            var exposedType = T("UnityEditor.Audio.ExposedAudioParameter");

            System.IO.Directory.CreateDirectory(
                System.IO.Path.GetDirectoryName(MixerPath) ?? "Assets/Audio");

            var controller = ctrlType
                .GetMethod("CreateMixerControllerAtPath", BindingFlags.Public | BindingFlags.Static)
                !.Invoke(null, new object[] { MixerPath });

            var master = ctrlType.GetProperty("masterGroup", Any)!.GetValue(controller);

            // --- groups, parented under Master by assigning its children array outright ---
            var createGroup = ctrlType.GetMethod("CreateNewGroup", Any)!;
            var children = Array.CreateInstance(groupType, Groups.Length);
            for (int i = 0; i < Groups.Length; i++)
                children.SetValue(createGroup.Invoke(controller, new object[] { Groups[i], false }), i);
            groupType.GetProperty("children", Any)!.SetValue(master, children);

            // --- expose every volume, then rename the lot to volMaster / volMusic / … ---
            var buses = new List<object> { master };
            var labels = new List<string> { "Master" };
            foreach (var child in children) buses.Add(child);
            labels.AddRange(Groups);

            var guidForVolume = groupType.GetMethod("GetGUIDForVolume", Any)!;
            var addExposed = ctrlType.GetMethod("AddExposedParameter", Any)!;
            var pathCtor = pathType.GetConstructors()[0];
            foreach (var bus in buses)
            {
                addExposed.Invoke(controller, new[]
                {
                    pathCtor.Invoke(new[] { bus, guidForVolume.Invoke(bus, null) })
                });
            }

            var exposedProp = ctrlType.GetProperty("exposedParameters", Any)!;
            var exposed = (Array)exposedProp.GetValue(controller);
            var nameField = exposedType.GetField("name")!;
            var guidField = exposedType.GetField("guid")!;
            for (int e = 0; e < exposed.Length; e++)
            {
                // ExposedAudioParameter is a STRUCT: mutate the box, then write it back into the
                // array. Editing it in place through the array does nothing.
                var box = exposed.GetValue(e);
                for (int i = 0; i < buses.Count; i++)
                {
                    if (guidField.GetValue(box)!.Equals(guidForVolume.Invoke(buses[i], null)))
                        nameField.SetValue(box, VolumeParam(labels[i]));
                }

                exposed.SetValue(box, e);
            }

            exposedProp.SetValue(controller, exposed);

            // --- snapshots: rename the default, then clone the other three off it ---
            var snapshotsProp = ctrlType.GetProperty("snapshots", Any)!;
            var targetProp = ctrlType.GetProperty("TargetSnapshot", Any)!;
            var clone = ctrlType.GetMethod("CloneNewSnapshotFromTarget", Any)!;

            var first = ((Array)snapshotsProp.GetValue(controller)).GetValue(0);
            ((UnityEngine.Object)first).name = SnapshotDefault;
            for (int i = 1; i < Snapshots.Length; i++)
            {
                // Clone from Default every time, not from whatever was cloned last - otherwise
                // Rhythm inherits Interior's overrides and the three stop being independent.
                targetProp.SetValue(controller, first);
                clone.Invoke(controller, new object[] { false });
                ((UnityEngine.Object)targetProp.GetValue(controller)).name = Snapshots[i];
            }

            targetProp.SetValue(controller, first);
            ctrlType.GetProperty("startSnapshot", Any)!.SetValue(controller, first);

            ApplyDuck(controller, ctrlType, groupType, snapshotsProp);

            // Two things that are only visible when a human opens the Audio Mixer window, and both
            // were wrong on the first build:
            //  - SetValueForVolume moves the EDITING target to whatever snapshot it just wrote, so
            //    the mixer opened showing Rhythm - an Ambient bus at −80 dB, which reads as a broken
            //    build rather than as a snapshot doing its job. m_StartSnapshot was always correct.
            //  - a mixer built through the API has an EMPTY view list, so the window has no strips to
            //    show. Measured rather than assumed: `GetCurrentViewGroupList()` threw
            //    "Index was outside the bounds of the array" on the first build, and Unity's own
            //    `SanitizeGroupViews()` does NOT repair an empty list - it only tidies a populated
            //    one. One view holding every group has to be built by hand.
            targetProp.SetValue(controller, first);
            BuildDefaultView(controller, ctrlType, groupType);

            EditorUtility.SetDirty((UnityEngine.Object)controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(MixerPath);

            Debug.Log($"AudioMixerBuilder - built {MixerPath}\n{Validate()}");
        }

        /// <summary>
        /// Gives the mixer the single "View" holding every group that the Audio Mixer window needs
        /// to draw anything. Without it the window's own group list throws on an empty array.
        /// </summary>
        private static void BuildDefaultView(object controller, Type ctrlType, Type groupType)
        {
            var viewType = Ed.GetType("UnityEditor.Audio.MixerGroupView");
            var viewsProp = ctrlType.GetProperty("views", Any);
            if (viewType == null || viewsProp == null) return;

            var mixer = (AudioMixer)controller;
            var groups = mixer.FindMatchingGroups(string.Empty);
            var idProp = groupType.GetProperty("groupID", Any);
            if (idProp == null) return;

            var ids = Array.CreateInstance(idProp.PropertyType, groups.Length);
            for (int i = 0; i < groups.Length; i++) ids.SetValue(idProp.GetValue(groups[i]), i);

            var view = Activator.CreateInstance(viewType);
            viewType.GetField("guids")!.SetValue(view, ids);
            viewType.GetField("name")!.SetValue(view, "View");

            var views = Array.CreateInstance(viewType, 1);
            views.SetValue(view, 0);
            viewsProp.SetValue(controller, views);
            ctrlType.GetProperty("currentViewIndex", Any)?.SetValue(controller, 0);
        }

        /// <summary>
        /// Writes <c>ambientAudio.duck</c> into the three non-default snapshots.
        ///
        /// Read from the config rather than typed in, so the numbers stay diffable against
        /// <c>config.ts</c> - the same rule the rest of the port follows. Only the Ambient bus is
        /// touched: the web ducks nothing else, and a snapshot that quietly moves the music too
        /// would be a design change smuggled in as plumbing.
        /// </summary>
        private static void ApplyDuck(
            object controller, Type ctrlType, Type groupType, PropertyInfo snapshotsProp)
        {
            var duck = TheBlockConfig.Load()?.Config?.AmbientAudio?.Duck;
            if (duck == null)
            {
                Debug.LogWarning("AudioMixerBuilder: no ambientAudio.duck in the config - the " +
                                 "snapshots are built but every bus sits at 0 dB.");
                return;
            }

            var mixer = (AudioMixer)controller;
            object ambient = null;
            foreach (var group in mixer.FindMatchingGroups(string.Empty))
                if (group.name == "Ambient")
                    ambient = group;
            if (ambient == null) return;

            var snapshots = (Array)snapshotsProp.GetValue(controller);
            var setVolume = groupType.GetMethod("SetValueForVolume", Any)!;

            foreach (var snapshot in snapshots)
            {
                var name = ((UnityEngine.Object)snapshot).name;
                float linear = name switch
                {
                    SnapshotDriving => duck.Driving,
                    SnapshotInterior => duck.Interior,
                    SnapshotRhythm => duck.Rhythm,
                    _ => 1f,
                };

                setVolume.Invoke(ambient, new[] { controller, snapshot, (object)LinearToDb(linear) });
            }
        }

        /// <summary>
        /// A gain multiplier as decibels. Unity's floor is −80 dB, which is where a duck of 0 has to
        /// land - <c>log10(0)</c> is negative infinity and a mixer will not take it.
        /// </summary>
        public static float LinearToDb(float linear) =>
            linear <= 0.0001f ? -80f : Mathf.Max(-80f, 20f * Mathf.Log10(linear));

        /// <summary>
        /// Names what the mixer at <see cref="MixerPath"/> has and has not got. Cheap enough to run
        /// from <c>Build Audio</c>, which does.
        /// </summary>
        public static string Validate()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (mixer == null) return $"AudioMixerBuilder: {MixerPath} does not exist.";

            var report = new StringBuilder($"AudioMixerBuilder - {MixerPath}\n");
            var missing = 0;

            foreach (var name in Groups)
            {
                var found = mixer.FindMatchingGroups(name);
                bool ok = false;
                foreach (var group in found) ok |= group.name == name;
                if (!ok) missing++;
                report.AppendLine($"  group {name,-8} {(ok ? "ok" : "MISSING")}");
            }

            var labels = new List<string> { "Master" };
            labels.AddRange(Groups);
            foreach (var label in labels)
            {
                var param = VolumeParam(label);
                bool ok = mixer.GetFloat(param, out _);
                if (!ok) missing++;
                report.AppendLine($"  param {param,-11} {(ok ? "ok" : "MISSING")}");
            }

            foreach (var name in Snapshots)
            {
                bool ok = mixer.FindSnapshot(name) != null;
                if (!ok) missing++;
                report.AppendLine($"  snapshot {name,-8} {(ok ? "ok" : "MISSING")}");
            }

            report.Append(missing == 0
                ? "  → complete."
                : $"  → {missing} missing. Delete the asset and run Build Audio Mixer to rebuild it.");
            return report.ToString();
        }
    }
}
