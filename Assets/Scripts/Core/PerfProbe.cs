using System;
using System.Collections;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace TheBlock.Core
{
    /// <summary>
    /// Measures ONE thing on the Player, unattended, and quits.
    ///
    /// <b>Why this exists.</b> Eight units owe U30b a per-feature frame delta - U35a/b/c/g/h/i, U36,
    /// U37 and U38 - and every one of them has been waiting on "the user drives while somebody
    /// watches a counter". That is not a judgement call. A triangle count and a millisecond are
    /// numbers, and a number can be taken by a machine at 3am. What still needs a human is whether
    /// the crowd *reads* right; that is not what this measures and it must never be claimed as such.
    ///
    /// <b>The Player, not the Editor.</b> U30b's standing rule: the Editor lies. A Player is a
    /// different renderer path, a different memory ceiling and a different input stack, so the
    /// harness ships inside a Development Build and is compiled out of a release one, exactly like
    /// <see cref="FrameWatchdog"/>.
    ///
    /// <b>One launch per configuration, and this is the design decision the whole class turns on.</b>
    /// The obvious harness toggles a feature mid-session and re-samples. It would be wrong here:
    /// <c>Progress</c>' switches are <c>PlayerPrefs</c> read at <c>Awake</c> by systems all over the
    /// world, so flipping one mid-run leaves half the scene in the old state and produces a number
    /// that looks plausible and is not. <c>synthetic-play-test-decays</c> is the memory of exactly
    /// this class of test lying convincingly. So the process measures a single configuration and
    /// exits, and the sweep outside relaunches it.
    ///
    /// <b>Repeats are the finding, not padding.</b> The sweep runs each configuration twice and
    /// interleaves the arms. Two runs of the SAME configuration are the noise floor, and on a laptop
    /// whose thermal state nobody controls a 2 ms difference without one is not a measurement.
    ///
    /// Driven by <c>tools/perf-sweep.sh</c>; the lines it prints are parsed by
    /// <c>tools/perf-report.py</c>.
    /// </summary>
    [DisallowMultipleComponent]
    public class PerfProbe : MonoBehaviour
    {
        // --- the line the parser reads ------------------------------------------------------

        private const string Tag = "PERFPROBE";

        // --- what a run is ------------------------------------------------------------------

        private static bool _armed;
        private static string _feature = "baseline";
        private static bool _on = true;
        private static string _poseName = "lotcars";
        private static int _repeat = 0;
        private static bool _freeze;
        private static string _shotPath;

        [Tooltip("Frames discarded before sampling starts: streaming, the crowd's spawn ramp and " +
                 "the shadow cascades all settle inside this window.")]
        private const int SettleFrames = 180;

        [Tooltip("Frames actually measured. 300 at ~60 fps is five seconds of steady state.")]
        private const int SampleFrames = 300;

        /// <summary>
        /// Where the probe stands, and which way it looks.
        ///
        /// Baked as literals rather than resolved at runtime, so every run of every arm frames
        /// exactly the same geometry - a pose that drifted by a metre between the on and off arms
        /// would put a different number of buildings in the frustum and the delta would be that,
        /// not the feature. The heights came from <c>WorldBuilder.GroundY</c> at bake time; the
        /// crowd pose is the seed with the most neighbours inside the 90 m cull radius (138 of
        /// them), which is the same "densest pose in the game" U38 measured at.
        /// </summary>
        private static bool TryPose(string name, out Vector3 position, out float yaw)
        {
            switch (name)
            {
                // 81 parked cars inside 90 m - the densest geometry in the world.
                case "lotcars":  position = new Vector3(188.90f, 0.08f, -240.10f);  yaw = 0f;   return true;
                // 138 crowd seeds inside the cull radius.
                case "crowd":    position = new Vector3(227.30f, 0.115f, 263.40f);  yaw = 0f;   return true;
                // On the sand, looking out to sea: water, beach and both jetskis in frame.
                case "beach":    position = new Vector3(420.00f, 0.00f, -240.00f);  yaw = 90f;  return true;
                // 15 m short of the stand, facing it.
                case "falafel":  position = new Vector3(5.30f, 0.00f, -96.90f);     yaw = 90f;  return true;
                case "autoshop": position = new Vector3(-76.10f, 0.115f, 225.00f);  yaw = -45f; return true;
                case "station":  position = new Vector3(135.00f, 0.00f, -124.50f);  yaw = 90f;  return true;
                default:         position = Vector3.zero; yaw = 0f;                             return false;
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD

        // --- stage 1: before the world exists -----------------------------------------------

        /// <summary>
        /// Reads the command line and writes the <c>PlayerPrefs</c> half of the configuration.
        ///
        /// This has to happen before a single <c>Awake</c> runs, because <c>PropSystem</c>,
        /// <c>NpcBuilder</c>'s output and the damage call sites all read their switch at boot and
        /// never look again.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void Configure()
        {
            // A previous run that was killed mid-flight left the user's settings overwritten. Put
            // them back before doing anything else, whether or not this run is a probe run.
            RestoreStash();

            var argv = Environment.GetCommandLineArgs();
            for (int i = 0; i < argv.Length; i++)
            {
                switch (argv[i])
                {
                    case "-perfFeature": if (i + 1 < argv.Length) { _feature = argv[i + 1]; _armed = true; } break;
                    case "-perfState":   if (i + 1 < argv.Length) _on = argv[i + 1] == "on"; break;
                    case "-perfPose":    if (i + 1 < argv.Length) _poseName = argv[i + 1]; break;
                    case "-perfRepeat":  if (i + 1 < argv.Length) int.TryParse(argv[i + 1], out _repeat); break;
                    case "-perfFreeze":  if (i + 1 < argv.Length) _freeze = argv[i + 1] == "on"; break;
                    case "-perfShot":    if (i + 1 < argv.Length) _shotPath = argv[i + 1]; break;
                }
            }

            if (!_armed) return;

            Application.runInBackground = true;   // or the run stalls the moment focus moves away
            StashAndApplyPrefs();

            Debug.Log($"{Tag}: armed feature={_feature} state={(_on ? "on" : "off")} " +
                      $"pose={_poseName} repeat={_repeat}");
        }

        // --- stage 2: once the world is loaded ----------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!_armed) return;
            var go = new GameObject("__PerfProbe") { hideFlags = HideFlags.DontSave };
            go.AddComponent<PerfProbe>();
            DontDestroyOnLoad(go);
        }

        private void Start() => StartCoroutine(RunAndQuit());

        private IEnumerator RunAndQuit()
        {
            // The build boots to U26's title menu, which pins timeScale at 0. Without this every
            // sample reads identical and the run is silently worthless.
            yield return StartGame();

            // The cap exists so the shipped game does not run ragged; a capped frame cannot show a
            // regression, so measurement takes it off. Set here rather than beside the other
            // command-line work: DisplaySettings assigns it at BeforeSplashScreen too, and the
            // order of two methods with the same load type is undefined.
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;

            ApplySceneToggle();
            if (_freeze) FreezeWorld();

            if (TryPose(_poseName, out var position, out float yaw)) yield return MoveTo(position, yaw);
            else Fail($"unknown pose '{_poseName}'");

            yield return Settle();

            // A frame the Player actually drew, saved to disk, so a change that is supposed to be
            // invisible can be PROVED invisible instead of argued.
            //
            // It has to happen here rather than in the Editor: `Camera.Render()` on a hand-made
            // camera in edit mode does NOT consult the baked occlusion data - both arms of that
            // attempt reported the same 19,144,358 triangles, so the control failed and its pixel
            // diff would have read 0% whether occlusion worked or not. In a Player it does apply,
            // which the 17.96 M / 23.90 M pair at this pose already shows.
            if (!string.IsNullOrEmpty(_shotPath)) yield return Screenshot();

            yield return Sample();

            RestoreStash();
            Application.Quit();
        }

        /// <summary>
        /// Leaves the title menu the way a person does, by pressing Continue.
        ///
        /// <b>Continue, never New Game.</b> New Game wipes the wallet and the campaign, and a probe
        /// that quietly reset the user's save every time it ran would be a far worse bug than any
        /// it could measure.
        /// </summary>
        private IEnumerator StartGame()
        {
            // ⚠ POLL, NEVER A FIXED FRAME COUNT. The first version waited five frames, found no
            // GameFlow and carried on regardless - because the world does not exist that early:
            // BootLoader loads it with LoadSceneAsync and holds activation until ~4.1 s. The probe
            // then sampled the TITLE SCREEN and reported it under the parking-lot pose. That number
            // was real, wrong, and carried nothing in it to say so, which is the exact failure
            // synthetic tests are known for here. Wait for the object; never guess the machine's
            // timing.
            yield return WaitFor("TheBlock.UI.Menus.GameFlow", 40f);

            var flow = Find("TheBlock.UI.Menus.GameFlow");
            if (flow == null) { Fail("no GameFlow after 40 s"); yield break; }

            var method = flow.GetType().GetMethod("Continue",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (method == null) { Fail("GameFlow.Continue not found"); yield break; }

            method.Invoke(flow, null);

            // Continue unfreezes the world; the player is spawned as part of that, so wait for it
            // rather than for a frame count.
            yield return WaitFor("TheBlock.Player.PlayerController", 20f);

            Debug.Log($"{Tag}: started, timeScale={Time.timeScale}");
        }

        private IEnumerator MoveTo(Vector3 position, float yaw)
        {
            var player = Find("TheBlock.Player.PlayerController");
            if (player == null) { Fail("no PlayerController to place"); yield break; }

            var teleport = player.GetType().GetMethod("Teleport");
            if (teleport != null) teleport.Invoke(player, new object[] { position, yaw });
            else player.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            // The follow camera eases; give it time to arrive or the first frames sample a swing.
            for (int i = 0; i < 30; i++) yield return null;

            Debug.Log($"{Tag}: standing at {player.transform.position.ToString("F1")}");
        }

        /// <summary>
        /// Waits for the world to stop changing, rather than for a fixed number of frames.
        ///
        /// <b>A frame count is not a settle condition, and U30b's first sweep proved it.</b> The
        /// crowd pose was given 180 frames; one run sampled with 145 visible skinned meshes and its
        /// repeat sampled with 542. Those are two different worlds, so the pair was not an A/B of
        /// anything - and because the spread was enormous the noise floor swallowed it and it
        /// printed as "below the noise floor", which reads as "the crowd is free". It is the exact
        /// inversion of the truth, produced by a harness that was measuring before the thing it was
        /// measuring had finished arriving.
        ///
        /// So: watch the crowd actually stream in, and start only once it has stopped. The count of
        /// visible skinned meshes is the right signal because the crowd is the only thing in this
        /// world that streams, and <c>CrowdSpawner</c> is budgeted per frame by design.
        /// </summary>
        private IEnumerator Settle()
        {
            var skinned = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Visible Skinned Meshes Count");

            long last = -1;
            int stable = 0;
            int waited = 0;

            while (waited < MaxSettleFrames)
            {
                yield return null;
                waited++;

                long now = skinned.Valid ? skinned.LastValue : 0;
                if (now == last) stable++; else { stable = 0; last = now; }

                // Steady for a second AND past the minimum: the minimum is what covers the things
                // that settle without changing this counter - streaming, the shadow cascades, the
                // first-frame shader compiles.
                if (stable >= StableFrames && waited >= SettleFrames) break;
            }

            Debug.Log($"{Tag}: settled after {waited} frames, {last} skinned visible");
            skinned.Dispose();
        }

        /// <summary>Frames the counter must hold the same value before the world counts as still.</summary>
        private const int StableFrames = 60;

        /// <summary>Ceiling, so a world that never settles still produces a run rather than hanging.</summary>
        private const int MaxSettleFrames = 1800;

        /// <summary>Spins until the named component exists, or the timeout says it never will.</summary>
        private IEnumerator WaitFor(string typeName, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (Find(typeName) == null && Time.realtimeSinceStartup < deadline) yield return null;
        }

        private static MonoBehaviour Find(string typeName)
        {
            var type = FindType(typeName);
            return type == null ? null : FindAnyObjectByType(type) as MonoBehaviour;
        }

        /// <summary>
        /// A run that could not do what it was asked must say so IN the result, not only in a
        /// warning thirty lines above it. <c>tools/perf-report.py</c> refuses any run whose log
        /// carries this line, so a mis-posed sample can never be averaged into a delta.
        /// </summary>
        private void Fail(string why)
        {
            _failed = true;
            Debug.LogWarning($"{Tag} FAILED: {why}");
        }

        private static bool _failed;

        // --- the measurement ----------------------------------------------------------------

        /// <summary>
        /// The same recorder set <see cref="FrameWatchdog"/> already proved works on a Player.
        ///
        /// ⚠ There is no draw-call counter on Unity 6.5 - <c>Draw Calls Count</c> and
        /// <c>Batches Count</c> both report <c>Valid == false</c>, and
        /// <c>UnityEditor.UnityStats.drawCalls</c> is Editor-only. <c>SetPass Calls Count</c> plus
        /// the triangle count is the honest pair, so do not add one back.
        /// </summary>
        private IEnumerator Sample()
        {
            var setPass  = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            var tris     = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            var verts    = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
            var casters  = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
            var skinned  = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Visible Skinned Meshes Count");

            double frameSum = 0d;
            float worst = 0f;
            long setPassSum = 0, trisSum = 0, vertsSum = 0, castersSum = 0, skinnedSum = 0;
            int counted = 0;

            for (int i = 0; i < SampleFrames; i++)
            {
                yield return null;

                // Unscaled: a paused or slowed frame must still be reported at its real length.
                float dt = Time.unscaledDeltaTime;
                frameSum += dt;
                if (dt > worst) worst = dt;

                if (setPass.Valid) setPassSum += setPass.LastValue;
                if (tris.Valid)    trisSum    += tris.LastValue;
                if (verts.Valid)   vertsSum   += verts.LastValue;
                if (casters.Valid) castersSum += casters.LastValue;
                if (skinned.Valid) skinnedSum += skinned.LastValue;
                counted++;
            }

            var sb = new StringBuilder(256);
            sb.Append(Tag).Append(" RESULT")
              .Append(" feature=").Append(_feature)
              .Append(" state=").Append(_on ? "on" : "off")
              .Append(" pose=").Append(_poseName)
              .Append(" repeat=").Append(_repeat)
              .Append(" freeze=").Append(_freeze ? 1 : 0)
              .Append(" frames=").Append(counted)
              .Append(" frameMs=").Append((frameSum / Mathf.Max(1, counted) * 1000d).ToString("0.000"))
              .Append(" worstMs=").Append((worst * 1000f).ToString("0.0"))
              .Append(" setPass=").Append(Mean(setPassSum, counted))
              .Append(" tris=").Append(Mean(trisSum, counted))
              .Append(" verts=").Append(Mean(vertsSum, counted))
              .Append(" casters=").Append(Mean(castersSum, counted))
              .Append(" skinned=").Append(Mean(skinnedSum, counted))
              .Append(" texMb=").Append((Texture.currentTextureMemory / 1048576f).ToString("0"))
              .Append(" failed=").Append(_failed ? 1 : 0);
            Debug.Log(sb.ToString());

            setPass.Dispose(); tris.Dispose(); verts.Dispose(); casters.Dispose(); skinned.Dispose();
        }

        private static long Mean(long sum, int n) => n > 0 ? sum / n : 0;

        // --- applying the configuration -----------------------------------------------------

        /// <summary>
        /// The scene-side half: roots that are simply switched off to measure what they cost.
        ///
        /// Deliberately blunt. Measuring "what does the auto shop cost" by deactivating the auto
        /// shop is exact in a way that estimating from its triangle count is not, and it cannot
        /// drift out of step with the builder the way a hard-coded list of its parts would.
        /// </summary>
        private void ApplySceneToggle()
        {
            // U35c is the one feature whose "on" state is not the shipped world: the police
            // helicopter exists only during a 3★ chase, so measuring it means putting one in the
            // air rather than switching one off. Its off arm is the world as it stands.
            if (_feature == "heli")
            {
                if (_on) SpawnHelicopter();
                return;
            }

            // Occlusion culling is the one arm that is not a scene object. The baked data is either
            // consulted or ignored, and nothing else about the world differs between the arms - which
            // makes it the cleanest A/B here, since both arms run the same build off the same bake.
            if (_feature == "occlusion")
            {
                foreach (var cam in Camera.allCameras) cam.useOcclusionCulling = _on;
                Debug.Log($"{Tag}: occlusion culling {(_on ? "on" : "off")} on {Camera.allCameras.Length} cameras");
                return;
            }

            // Also a proposal rather than a feature. All 101 parked cars carry a SINGLE-level
            // LODGroup - which is a cull distance wearing an LODGroup's clothes, and at
            // screenRelativeTransitionHeight 0.0069 on a 5 m car it holds all 52,096 triangles out to
            // roughly 630 m. lodBias scales that distance for every LODGroup at once, so this arm
            // prices a tighter cull without editing 101 objects to find out it was not worth it.
            if (_feature == "lodbias")
            {
                if (_on) QualitySettings.lodBias = 0.4f;
                Debug.Log($"{Tag}: lodBias {QualitySettings.lodBias}");
                return;
            }

            if (_on) return;   // every other feature: "on" is the shipped world, nothing to do

            switch (_feature)
            {
                case "autoshop": Deactivate("World/Places/Place_AutoShop"); break;
                case "sea":      Deactivate("World/Sea"); break;
                case "falafel":
                    Deactivate("World/Places/Place_FalafelStand");
                    Deactivate("World/Places/Falafel_Vendor");
                    break;
                case "lotcars":  Deactivate("World/Places/LotCars"); break;
                case "jetski":
                    Deactivate("Jetski");
                    Deactivate("Thief Ski");
                    break;
                case "crowd":    DisableCrowd(); break;
            }
        }

        private void Deactivate(string path)
        {
            var go = GameObject.Find(path);
            if (go == null) { Debug.LogWarning($"{Tag}: nothing at '{path}' to switch off"); return; }
            go.SetActive(false);
            Debug.Log($"{Tag}: switched off {path}");
        }

        /// <summary>
        /// Puts one police H145 in the air where a 3★ chase would put it.
        ///
        /// Reflected off <c>PoliceSystem</c>'s own serialized field rather than loaded by path: a
        /// Player has no <c>AssetDatabase</c>, and a second reference to the prefab would be a
        /// second thing to keep in step with the builder. Placed at the slot the real thing holds -
        /// 18 m back, 34 m up - so what is measured is the view the player actually gets, spotlight,
        /// beam cone, shadow map and all.
        /// </summary>
        private void SpawnHelicopter()
        {
            var police = FindAnyObjectByType(FindType("TheBlock.Police.PoliceSystem")) as MonoBehaviour;
            if (police == null) { Debug.LogWarning($"{Tag}: no PoliceSystem"); return; }

            var field = police.GetType().GetField("heliPrefab",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var prefab = field?.GetValue(police) as GameObject;
            if (prefab == null) { Debug.LogWarning($"{Tag}: PoliceSystem has no heliPrefab"); return; }

            if (!TryPose(_poseName, out var stand, out float yaw)) stand = Vector3.zero;
            var forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var at = stand + forward * 18f + Vector3.up * 34f;

            Instantiate(prefab, at, Quaternion.Euler(0f, yaw, 0f));
            Debug.Log($"{Tag}: police helicopter spawned at {at}");
        }

        /// <summary>
        /// Empties the world of everything that moves, in BOTH arms of a comparison.
        ///
        /// <b>This is the instrument for anything whose delta is smaller than the crowd.</b> The
        /// first occlusion sweep put two identical `on` runs at 20.78 M and 24.40 M triangles - the
        /// exact counter, not a millisecond - because the crowd and the traffic had streamed to
        /// different states by the time each run sampled. A 1.8 M delta cannot be read off a pair
        /// that disagrees with itself by 3.6 M, and no number of repeats fixes a world that has no
        /// steady state to average towards (see the `live-crowd-has-no-steady-state` note).
        ///
        /// Switching the two systems off is enough to take their output with them: `CrowdSpawner`
        /// and `TrafficSystem` both `Instantiate(prefab, transform)`, so every pedestrian and every
        /// traffic car is a child of the thing being deactivated.
        ///
        /// What is measured afterwards is the STATIC world, which is where 9.9 M of the 9.95 M
        /// triangles live anyway - and it is deterministic, so two runs of one configuration should
        /// now agree to the triangle.
        /// </summary>
        /// <summary>
        /// Writes one PNG of the settled view. <c>CaptureScreenshot</c> is queued rather than
        /// immediate - it lands at the end of a later frame - so this waits for the file to appear
        /// instead of assuming, and gives up loudly rather than letting a run report a shot it
        /// never took.
        /// </summary>
        private IEnumerator Screenshot()
        {
            if (File.Exists(_shotPath)) File.Delete(_shotPath);
            ScreenCapture.CaptureScreenshot(_shotPath);

            for (int i = 0; i < 240 && !File.Exists(_shotPath); i++) yield return null;

            if (File.Exists(_shotPath)) Debug.Log($"{Tag}: shot {_shotPath}");
            else Fail($"screenshot never appeared at {_shotPath}");

            // The capture resizes the back buffer for a frame; sampling straight after would price
            // that instead of the view.
            for (int i = 0; i < 30; i++) yield return null;
        }

        private void FreezeWorld()
        {
            Deactivate2("TheBlock.Npc.CrowdSpawner");
            Deactivate2("TheBlock.Traffic.TrafficSystem");
        }

        private void Deactivate2(string typeName)
        {
            var system = FindAnyObjectByType(FindType(typeName)) as MonoBehaviour;
            if (system == null) { Debug.LogWarning($"{Tag}: no {typeName} to freeze"); return; }
            system.gameObject.SetActive(false);
            Debug.Log($"{Tag}: froze {typeName} ({system.gameObject.name})");
        }

        private void DisableCrowd()
        {
            var spawner = FindAnyObjectByType(FindType("TheBlock.Npc.CrowdSpawner")) as MonoBehaviour;
            if (spawner == null) { Debug.LogWarning($"{Tag}: no CrowdSpawner"); return; }
            spawner.gameObject.SetActive(false);
            Debug.Log($"{Tag}: crowd spawner off");
        }

        // --- the PlayerPrefs half, and putting it back ---------------------------------------

        private const string StashFlag = "theblock.perfprobe.stashed";
        private const string StashRagdolls = "theblock.perfprobe.ragdolls";
        private const string StashDamage = "theblock.perfprobe.vehicledamage";
        private const string StashProps = "theblock.perfprobe.streetprops";

        /// <summary>
        /// <b>The probe borrows the user's settings and must give them back.</b>
        ///
        /// These are the same <c>PlayerPrefs</c> keys the shipped Settings menu writes, and a
        /// Player shares them with every other Player of the same product. A sweep that left
        /// Vehicle Damage on because that was the last arm it measured would silently re-decide a
        /// user's setting - which has already happened once in this project, when the U35d-pre-2
        /// immunity test reset it and the ledger had to carry a warning about it.
        ///
        /// So the original values are stashed under separate keys before anything is written, and
        /// restored on quit. The stash flag means a run that is killed mid-flight is repaired by
        /// the NEXT run rather than lost.
        /// </summary>
        private static void StashAndApplyPrefs()
        {
            if (PlayerPrefs.GetInt(StashFlag, 0) == 0)
            {
                PlayerPrefs.SetInt(StashRagdolls, Game.Progress.RagdollsOn ? 1 : 0);
                PlayerPrefs.SetInt(StashDamage, (int)Game.Progress.VehicleDamage);
                PlayerPrefs.SetInt(StashProps, Game.Progress.BreakablePropsOn ? 1 : 0);
                PlayerPrefs.SetInt(StashFlag, 1);
                PlayerPrefs.Save();
            }

            // The baseline every delta subtracts from is round 1's: all three Tier 8 switches OFF.
            // Each arm then turns on exactly one of them.
            Game.Progress.RagdollsOn = _feature == "ragdolls" && _on;
            Game.Progress.VehicleDamage = _feature == "vehicledamage" && _on
                ? Game.VehicleDamageMode.Full
                : Game.VehicleDamageMode.Off;
            Game.Progress.BreakablePropsOn = _feature == "streetprops" && _on;
        }

        private static void RestoreStash()
        {
            if (PlayerPrefs.GetInt(StashFlag, 0) == 0) return;

            Game.Progress.RagdollsOn = PlayerPrefs.GetInt(StashRagdolls, 1) != 0;
            Game.Progress.VehicleDamage = (Game.VehicleDamageMode)PlayerPrefs.GetInt(StashDamage, 0);
            Game.Progress.BreakablePropsOn = PlayerPrefs.GetInt(StashProps, 1) != 0;

            PlayerPrefs.DeleteKey(StashFlag);
            PlayerPrefs.DeleteKey(StashRagdolls);
            PlayerPrefs.DeleteKey(StashDamage);
            PlayerPrefs.DeleteKey(StashProps);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Types are looked up by name rather than referenced, so this file can sit in Core without
        /// making Core depend on the UI, the player and the crowd all at once.
        /// </summary>
        private static Type FindType(string fullName) => Type.GetType(fullName + ", Assembly-CSharp");

#endif
    }
}
