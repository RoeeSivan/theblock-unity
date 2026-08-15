using System.Collections.Generic;
using TheBlock.Audio;
using TheBlock.Core;
using TheBlock.Game;
using TheBlock.Minigame.Rhythm;
using TheBlock.Missions;
using TheBlock.Police;
using TheBlock.UI;
using TheBlock.Vehicles;
using TheBlock.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Wires the campaign into the scene — <b>The Block → Build Campaign</b> — and gives the port
    /// the New Game it has no menu for yet — <b>The Block → Reset Campaign</b>.
    ///
    /// Idempotent, like <see cref="WorldBuilder"/> and <see cref="HudBuilder"/>: it finds or creates
    /// the <c>Campaign</c> object, collects every <see cref="MissionBehaviour"/> in the scene,
    /// orders them by <c>campaign.config.ts</c>, and rebinds the references. Running it after
    /// building a new mission is how that mission joins the campaign — there is no list to hand-edit
    /// and forget.
    ///
    /// <b>It marks the scene dirty and does not save it</b>, the same contract every other builder
    /// here has.
    /// </summary>
    public static class MissionBuilder
    {
        private const string CampaignName = "Campaign";
        private const string VoiceFolder = "Assets/Audio/Voice";
        private const string MusicFolder = "Assets/Audio/Music";
        private const string PedFolder = "Assets/Prefabs/Npc";

        [MenuItem("The Block/Build Campaign", priority = 3)]
        public static void Build()
        {
            var snapshot = TheBlockConfig.Load(true);
            if (snapshot?.Campaign?.CampaignText == null)
            {
                Debug.LogError("MissionBuilder: the config snapshot has no campaignConfig. " +
                               "Re-run tools/export-config.sh.");
                return;
            }

            var root = GameObject.Find(CampaignName) ?? new GameObject(CampaignName);
            var campaign = Component(root, out Campaign _);
            var director = Component(root, out CampaignDirector _);
            var runner = Component(root, out CampaignRunner _);
            var voice = Component(root, out Voice _);
            var voiceCount = FillVoiceBank(voice);

            // Each mission is a component on the campaign root, created here so there is no scene
            // object anyone has to remember to add. Building a mission is writing its class; joining
            // the campaign is re-running this.
            BuildDelivery(root, snapshot);
            BuildDance(root, snapshot);
            BuildRescue(root, snapshot);
            BuildChase(root, snapshot);

            // Campaign order comes from the config, not from whatever order Unity happened to find
            // the components in. A mission with no row in campaignText is a mission with no copy,
            // which is a build error rather than something to silently append.
            var found = new Dictionary<string, MissionBehaviour>();
            foreach (var mission in Object.FindObjectsByType<MissionBehaviour>(
                         FindObjectsInactive.Include))
            {
                if (mission == null || string.IsNullOrEmpty(mission.Id)) continue;
                found[mission.Id] = mission;
            }

            var ordered = new List<MissionBehaviour>();
            var missing = new List<string>();
            foreach (var text in snapshot.Campaign.CampaignText)
            {
                if (text?.Id == null) continue;
                if (found.TryGetValue(text.Id, out var mission))
                {
                    ordered.Add(mission);
                    found.Remove(text.Id);
                }
                else
                {
                    missing.Add(text.Id);
                }
            }

            campaign.SetMissions(ordered);

            Bind(director, ("campaign", campaign));
            Bind(runner,
                ("campaign", campaign),
                ("director", director),
                ("hud", Object.FindAnyObjectByType<MissionHud>()),
                ("card", Object.FindAnyObjectByType<BriefingCard>()),
                ("wallet", Object.FindAnyObjectByType<Wallet>()),
                ("heat", Object.FindAnyObjectByType<Heat>()),
                ("bust", Object.FindAnyObjectByType<BustSequence>()),
                ("vehicles", Object.FindAnyObjectByType<VehicleEnterExit>()),
                ("interior", Object.FindAnyObjectByType<Interior>()));

            // Every mission that SPEAKS resolves its lines through the one Voice. Rebinding here
            // rather than in each mission's own builder means a mission cannot be built holding a
            // dangling reference to a bank that has not been filled yet. Two of the four have no
            // voice field at all — the rescue and the chase brief on a card and say nothing aloud —
            // so a missing field is expected here, not a warning.
            foreach (var mission in ordered) BindOptional(mission, "voice", voice);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            var report = $"MissionBuilder: campaign wired with {ordered.Count}/" +
                         $"{snapshot.Campaign.CampaignText.Count} missions " +
                         $"[{string.Join(", ", ordered.ConvertAll(m => m.Id))}], " +
                         $"{voiceCount} voice clips.";
            if (missing.Count > 0)
                report += $" NOT BUILT YET: {string.Join(", ", missing)}.";
            if (found.Count > 0)
                report += $" ⚠ In the scene with no campaignText row: {string.Join(", ", found.Keys)}.";

            if (Object.FindAnyObjectByType<MissionHud>() == null)
                report += " ⚠ No MissionHud — run The Block → Build Map HUD.";

            Debug.Log(report);
        }

        [MenuItem("The Block/Reset Campaign", priority = 4)]
        public static void ResetCampaign()
        {
            if (!EditorUtility.DisplayDialog(
                    "Reset Campaign",
                    "Wipes the saved campaign: unlocked missions back to the first, every mission " +
                    "pays again, the wallet back to its starting balance, and the one-time hints " +
                    "forgotten.\n\nThe picked character is kept — New Game restarts the campaign, " +
                    "not who you are.\n\nThis cannot be undone.",
                    "Reset", "Cancel"))
                return;

            // Fully qualified: `UnityEditor.Progress` is a real type and `using TheBlock.Game`
            // makes the bare name ambiguous inside an editor script.
            TheBlock.Game.Progress.Reset();
            Payouts.Reset();
            Onboarding.Reset();

            // The wallet's zero lives on the component, because its starting balance is serialized.
            // With no scene wallet — a fresh scene, or Play not yet run — clear the key directly so
            // a reset is a reset either way.
            var wallet = Object.FindAnyObjectByType<Wallet>();
            if (wallet != null) wallet.Reset();
            else PlayerPrefs.DeleteKey("theblock.cash");
            PlayerPrefs.Save();

            Debug.Log("MissionBuilder: campaign reset — mission 1, no payouts, wallet cleared, hints forgotten.");
        }

        /// <summary>
        /// M1. Its five customers are the crowd's own prefabs, resolved from the NAMES in
        /// <c>missionConfig.npcSpecs</c> — Sophie, Remy, Elizabeth, Chinese, Lewis — so the port
        /// cycles the same five faces in the same order the shipped game does, without a second
        /// character import. Peter is the sixth in the crowd and is not one of them, in both builds.
        /// </summary>
        private static void BuildDelivery(GameObject root, TheBlockConfig.Snapshot snapshot)
        {
            var mission = Component(root, out DeliveryMission _);

            var faces = new List<GameObject>();
            var missing = new List<string>();
            foreach (var face in snapshot.Mission?.NpcSpecs ?? new List<TheBlockConfig.MissionNpcSpec>())
            {
                if (string.IsNullOrEmpty(face?.Name)) continue;
                var path = $"{PedFolder}/Ped_{face.Name}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) missing.Add(face.Name);
                else faces.Add(prefab);
            }

            mission.SetFaces(faces);
            EditorUtility.SetDirty(mission);

            if (missing.Count > 0)
                Debug.LogWarning($"MissionBuilder: no Ped prefab for {string.Join(", ", missing)} — " +
                                 "run The Block → Build Pedestrians. Those faces will not appear.");

            Bind(mission,
                ("runner", root.GetComponent<CampaignRunner>()),
                ("interior", Object.FindAnyObjectByType<Interior>()),
                ("player", Object.FindAnyObjectByType<TheBlock.Player.PlayerController>()?.transform),
                ("vehicles", Object.FindAnyObjectByType<VehicleEnterExit>()),
                ("hud", Object.FindAnyObjectByType<MissionHud>()));
        }

        /// <summary>
        /// M2. Its two bodies and their shared controller come from <see cref="DanceBuilder"/>; what
        /// is here is the mission component, the conductor, and the song the conductor keeps time to.
        /// </summary>
        private static void BuildDance(GameObject root, TheBlockConfig.Snapshot snapshot)
        {
            var spec = snapshot.Rhythm;
            if (spec == null)
            {
                Debug.LogWarning("MissionBuilder: no rhythmConfig — the dance is not built.");
                return;
            }

            var log = new System.Text.StringBuilder();
            var (dancer, giver) = DanceBuilder.Build(root, spec, log);

            var mission = Component(root, out DanceMission _);
            mission.SetActors(dancer, giver);
            EditorUtility.SetDirty(mission);

            var conductor = Component(root, out Conductor _);
            var song = SongClip(spec.Song?.Url);
            conductor.SetSong(song);
            EditorUtility.SetDirty(conductor);

            log.AppendLine(song == null
                ? $"  ⚠ no song clip for '{spec.Song?.Url}' in {MusicFolder} — the routine has no clock"
                : $"  song: {song.name} {song.length:0.0}s @ {spec.Song.Bpm} BPM, offset {spec.Song.Offset}s");

            Bind(mission,
                ("runner", root.GetComponent<CampaignRunner>()),
                ("player", Object.FindAnyObjectByType<TheBlock.Player.PlayerController>()),
                ("followCamera", Object.FindAnyObjectByType<TheBlock.Player.FollowCamera>()),
                ("vehicles", Object.FindAnyObjectByType<VehicleEnterExit>()),
                ("hud", Object.FindAnyObjectByType<MissionHud>()),
                ("conductor", conductor),
                ("hudDocument", Object.FindAnyObjectByType<UnityEngine.UIElements.UIDocument>()));

            Debug.Log("MissionBuilder — dance\n" + log);
        }

        /// <summary>
        /// M3. Places the Huey at its config spawn, points the mission at the baked roof pool, and
        /// reuses the delivery run's faces as survivors — the ORIGINAL's own choice, and its
        /// <c>rescue.config.ts</c> says why: one source for those five assets.
        /// </summary>
        private static void BuildRescue(GameObject root, TheBlockConfig.Snapshot snapshot)
        {
            var spec = snapshot.Config?.Vehicle?.Helicopter;
            if (spec == null || snapshot.Rescue == null)
            {
                Debug.LogWarning("MissionBuilder: no helicopter or rescueConfig — M3 is not built.");
                return;
            }

            var heli = PlaceVehicle("Helicopter", spec.Spawn.X, spec.Spawn.Z, spec.ModelYaw, spec.PadY);
            var controller = heli != null ? heli.GetComponent<HelicopterController>() : null;

            var roofs = AssetDatabase.LoadAssetAtPath<RoofSpots>("Assets/Missions/Generated/RoofSpots.asset");

            var mission = Component(root, out RescueMission _);
            mission.SetContent(roofs, Faces(snapshot), controller);
            EditorUtility.SetDirty(mission);

            Bind(mission,
                ("runner", root.GetComponent<CampaignRunner>()),
                ("vehicles", Object.FindAnyObjectByType<VehicleEnterExit>()),
                ("hud", Object.FindAnyObjectByType<MissionHud>()));

            Debug.Log($"MissionBuilder — rescue: Huey at {(heli == null ? "MISSING" : heli.transform.position.ToString("F1"))}, " +
                      $"{(roofs == null ? "⚠ NO ROOF SPOTS — run The Block → Bake Roof Spots" : roofs.Count + " baked roof spots")}");
        }

        /// <summary>
        /// M4. Places the jetski past the shore wall where the player swims out to it, and builds
        /// the thief's two bodies.
        /// </summary>
        private static void BuildChase(GameObject root, TheBlockConfig.Snapshot snapshot)
        {
            var spec = snapshot.Config?.Vehicle?.Jetski;
            if (spec == null || snapshot.Chase == null)
            {
                Debug.LogWarning("MissionBuilder: no jetski or chaseConfig — M4 is not built.");
                return;
            }

            var seaLevel = snapshot.Config.Sea?.Level ?? 0f;
            var ski = PlaceVehicle("Jetski", spec.Spawn.X, spec.Spawn.Z, spec.ModelYaw, seaLevel);
            var controller = ski != null ? ski.GetComponent<JetskiController>() : null;

            var log = new System.Text.StringBuilder();
            var thief = ThiefBuilder.Build(root, snapshot.Chase, log);
            var buoy = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Vehicles/Buoy.prefab");

            var mission = Component(root, out JetskiChase _);
            mission.SetContent(controller, thief, buoy);
            EditorUtility.SetDirty(mission);

            Bind(mission,
                ("runner", root.GetComponent<CampaignRunner>()),
                ("vehicles", Object.FindAnyObjectByType<VehicleEnterExit>()),
                ("hud", Object.FindAnyObjectByType<MissionHud>()),
                ("player", Object.FindAnyObjectByType<TheBlock.Player.PlayerController>()));

            Debug.Log($"MissionBuilder — chase: jetski at {(ski == null ? "MISSING" : ski.transform.position.ToString("F1"))} " +
                      $"(shore is Unity x {TheBlock.World.SeaGeometry.ShoreX(snapshot.Config.Sea):F1}; the ski must be SEAWARD of it), " +
                      $"buoy prefab {(buoy == null ? "MISSING" : "ok")}\n{log}");
        }

        /// <summary>The five delivery faces, which the rescue's survivors reuse.</summary>
        private static List<GameObject> Faces(TheBlockConfig.Snapshot snapshot)
        {
            var faces = new List<GameObject>();
            foreach (var face in snapshot.Mission?.NpcSpecs ?? new List<TheBlockConfig.MissionNpcSpec>())
            {
                if (string.IsNullOrEmpty(face?.Name)) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PedFolder}/Ped_{face.Name}.prefab");
                if (prefab != null) faces.Add(prefab);
            }

            return faces;
        }

        /// <summary>
        /// Drops a mission vehicle at its config spawn, replacing whatever was there.
        ///
        /// Idempotent by destroy-and-rebuild, like every other builder here: re-running after a
        /// prefab change must not leave last run's copy standing beside this one's.
        /// </summary>
        private static GameObject PlaceVehicle(string name, float rawX, float rawZ, float modelYaw, float y)
        {
            var existing = GameObject.Find(name);
            if (existing != null && PrefabUtility.GetCorrespondingObjectFromSource(existing) != null)
                Object.DestroyImmediate(existing);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Vehicles/{name}.prefab");
            if (prefab == null)
            {
                Debug.LogWarning($"MissionBuilder: no {name}.prefab — run The Block → Build Mission Vehicles.");
                return null;
            }

            var at = Convert.Pos(new Vector3(rawX, 0f, rawZ));
            at.y = y;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = name;

            // modelYaw is already baked into the prefab's VISUAL child, so the body itself faces
            // Unity +Z and needs no rotation here. Applying it twice is how a vehicle ends up
            // driving backwards, which U17b spent a play-test finding.
            instance.transform.SetPositionAndRotation(at, Quaternion.identity);
            return instance;
        }

        /// <summary>Resolves the config's web URL to a clip in the music folder, by file name.</summary>
        private static AudioClip SongClip(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var key = System.IO.Path.GetFileNameWithoutExtension(url);
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { MusicFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == key)
                    return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }

            return null;
        }

        /// <summary>
        /// Fills the voice bank from <c>Assets/Audio/Voice</c>, keyed on file name — which is what
        /// the config's own web URLs resolve to, so nothing has to re-type a path the exporter is
        /// already carrying. Returns how many clips landed.
        /// </summary>
        private static int FillVoiceBank(Voice voice)
        {
            var entries = new List<Voice.Entry>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { VoiceFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;
                entries.Add(new Voice.Entry
                {
                    Key = System.IO.Path.GetFileNameWithoutExtension(path),
                    Clip = clip,
                });
            }

            entries.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            voice.SetClips(entries);
            EditorUtility.SetDirty(voice);
            return entries.Count;
        }

        private static T Component<T>(GameObject go, out T _) where T : Component
        {
            // TryGetComponent, never `GetComponent() ?? AddComponent()` — a missing component comes
            // back as Unity's fake-null, which `??` treats as a real object.
            if (!go.TryGetComponent<T>(out var component)) component = go.AddComponent<T>();
            _ = component;
            return component;
        }

        /// <summary>
        /// Writes serialized references through <see cref="SerializedObject"/> so the values land in
        /// the scene file rather than only in the live object.
        /// </summary>
        /// <summary>Binds a field only if the component has one. Silent when it does not.</summary>
        private static void BindOptional(Component target, string field, Object value)
        {
            if (target == null) return;
            var so = new SerializedObject(target);
            var property = so.FindProperty(field);
            if (property == null) return;
            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Bind(Component target, params (string field, Object value)[] refs)
        {
            if (target == null) return;
            var so = new SerializedObject(target);
            foreach (var (field, value) in refs)
            {
                var property = so.FindProperty(field);
                if (property == null)
                {
                    Debug.LogWarning($"MissionBuilder: {target.GetType().Name} has no field '{field}'.");
                    continue;
                }

                property.objectReferenceValue = value;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
