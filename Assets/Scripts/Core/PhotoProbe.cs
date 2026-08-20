using System;
using System.Collections;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;
#endif

namespace TheBlock.Core
{
    /// <summary>
    /// Takes ONE composed photograph of the Player, unattended, and quits.
    ///
    /// <b>Why this is a sibling of <see cref="PerfProbe"/> rather than a flag on it.</b> PerfProbe's
    /// output is a defensible millisecond, and its <c>Settle</c>/<c>Sample</c> contract has already
    /// been re-tuned twice to protect that number. A camera that moves for aesthetic reasons is the
    /// exact kind of second master that would quietly invalidate it. So the pattern is reused - arm
    /// from argv, wait for the world, leave the title menu, pose, shoot, quit - and the measurement
    /// is not.
    ///
    /// <b>The Player, not the Editor</b>, for two reasons this project has already paid for. An
    /// edit-mode <c>Camera.Render()</c> does not consult baked occlusion or LOD
    /// (<c>editor-render-ignores-lod-and-occlusion</c>), and Animators do not tick outside Play, so
    /// every character renders in bind pose (<c>editor-render-cannot-diagnose-t-pose</c>). A press
    /// shot taken in the Editor is a picture of something that is not the game.
    ///
    /// <b>What it is for.</b> Marketing frames for the project write-up, and the same rig frames the
    /// submission video. Driven by <c>tools/press-shots.sh</c>.
    /// </summary>
    [DisallowMultipleComponent]
    public class PhotoProbe : MonoBehaviour
    {
        private const string Tag = "PHOTOPROBE";

        /// <summary>
        /// One composed frame. Deliberately data, not code: re-framing a shot has to be a number
        /// edit and one relaunch, because the loop that makes this work is shoot → look → nudge.
        /// </summary>
        private struct Shot
        {
            /// <summary>File name, without extension.</summary>
            public string Name;

            /// <summary>
            /// Scene path of what the camera looks at, e.g. <c>World/Places/Place_FalafelStand</c>.
            /// The focus point and the framing radius come from its renderer bounds, so this
            /// survives anything that moves the building. Empty falls back to <see cref="Stand"/>.
            /// </summary>
            public string Target;

            /// <summary>
            /// What the camera aims at when there is no <see cref="Target"/> to measure - an open
            /// street, or a patch of sea. Zero falls back to <see cref="Stand"/>, which is what a
            /// shot of "wherever the player happens to be" wants.
            /// </summary>
            public Vector3 Focus;

            /// <summary>
            /// Framing radius in metres when the subject has no bounds to read. Ignored whenever
            /// <see cref="Target"/> resolves, because a measured radius is always the better number.
            /// </summary>
            public float Radius;

            /// <summary>Where the player is put. The crowd and the traffic stream around the PLAYER,
            /// not the camera, so a shot whose player is left at spawn photographs an empty city.</summary>
            public Vector3 Stand;

            /// <summary>Player's yaw at <see cref="Stand"/>.</summary>
            public float StandYaw;

            /// <summary>Degrees around the focus. 0 looks from +Z toward -Z.</summary>
            public float Azimuth;

            /// <summary>Degrees above the horizon. Negative looks up at the subject.</summary>
            public float Elevation;

            /// <summary>How much of the frame height the subject fills, 0-1. Bigger is tighter.</summary>
            public float Fill;

            /// <summary>Vertical field of view. 75 is the game's own lens; wider is a choice.</summary>
            public float Fov;

            /// <summary>Metres the aim point is lifted off the bounds centre. Buildings look better
            /// aimed low; open ground looks better aimed high.</summary>
            public float AimLift;

            /// <summary>0-24. Negative leaves the built lighting exactly as the scene stores it.</summary>
            public float Hour;

            /// <summary>Keep the HUD, and shoot the back buffer instead of a free camera.</summary>
            public bool Hud;

            /// <summary><c>bike</c>, <c>police</c>, or empty.</summary>
            public string Stage;
        }

        /// <summary>
        /// The shot list. Five of these are the ones asked for; the rest exist because the first
        /// image has to carry scale, one frame should show a SYSTEM rather than a place, and one
        /// should show that this is a finished game with a shell rather than a tech demo.
        ///
        /// The stand points for <c>crowd</c>, <c>beach</c>, <c>falafel</c>, <c>autoshop</c>,
        /// <c>station</c> and <c>lotcars</c> are <see cref="PerfProbe"/>'s own poses, which were
        /// picked against <c>WorldBuilder.GroundY</c> at bake time and are known to stand on ground
        /// rather than inside it.
        /// </summary>
        private static readonly Shot[] Shots =
        {
            // The downtown boulevard, looking up the avenue. The only street in the game with
            // pavements, benches, storefronts and a median, so it is the one frame that can carry
            // "this is a city" on its own.
            new Shot
            {
                Name = "01-hero-boulevard", Target = "",
                Stand = new Vector3(18f, 0.16f, 8f), StandYaw = 0f,
                Focus = new Vector3(2f, 8f, 40f), Radius = 16f,
                Azimuth = 0f, Elevation = 22f, Fill = 0.42f, Fov = 62f,
                Hour = -1f,
            },
            new Shot
            {
                Name = "02-motorcycle", Target = "",
                Stand = new Vector3(9f, 0.4f, -60f), StandYaw = 0f,
                Radius = 3.2f,
                Azimuth = 200f, Elevation = 4f, Fill = 0.9f, Fov = 55f, AimLift = 0.8f,
                Hour = -1f, Stage = "bike",
            },
            new Shot
            {
                Name = "03-police-night", Target = "",
                Stand = new Vector3(18f, 0.16f, -20f), StandYaw = 0f,
                Radius = 12f,
                Azimuth = 250f, Elevation = 13f, Fill = 0.7f, Fov = 65f, AimLift = 3f,
                Hour = -1f, Stage = "police",
            },
            // fh_talk, the pavement nudge point 2.5 m off the kerb - the stand itself is at
            // (20.25, 0, -96.9) and the pavement here sits at y 0.16, not 0.
            new Shot
            {
                Name = "04-falafel-stand", Target = "World/Places/Place_FalafelStand",
                Stand = new Vector3(16.75f, 0.16f, -96.75f), StandYaw = 90f,
                Azimuth = 120f, Elevation = 3f, Fill = 1.9f, Fov = 60f, AimLift = 0.4f,
                Hour = -1f,
            },
            // se_exit_to_street. The storefront faces +X, so the camera stands out on the street.
            new Shot
            {
                Name = "05-seven-eleven", Target = "World/Places/Place_SevenEleven",
                Stand = new Vector3(-27.5f, 0.34f, -15f), StandYaw = 270f,
                Azimuth = 90f, Elevation = 5f, Fill = 2.6f, Fov = 60f, AimLift = 1f,
                Hour = -1f,
            },
            // The signage face is the -Z side, 23.7 m up: Reichman_SignHeb at world z = -185.
            new Shot
            {
                Name = "06-reichman-university", Target = "World/Districts/District_ReichmanUniversity",
                Stand = new Vector3(216.9f, 0.1f, -196f), StandYaw = 0f,
                Azimuth = 0f, Elevation = 8f, Fill = 0.78f, Fov = 60f, AimLift = 7f,
                Hour = -1f,
            },
            new Shot
            {
                Name = "07-reichman-lot", Target = "World/Places/LotCars",
                Stand = new Vector3(209f, 0.25f, -230f), StandYaw = 0f,
                Azimuth = 25f, Elevation = 15f, Fill = 0.44f, Fov = 60f, AimLift = 14f,
                Hour = -1f,
            },
            // The waterline is x = 430 and the jetski sits at (442, 0, -246); azimuth 90 puts the
            // camera on the sand looking out, which is the only direction with water in it.
            new Shot
            {
                Name = "08-beach-sea", Target = "",
                Stand = new Vector3(422f, 0f, -244f), StandYaw = 90f,
                Focus = new Vector3(448f, 2f, -246f), Radius = 14f,
                Azimuth = 90f, Elevation = 15f, Fill = 0.8f, Fov = 65f,
                Hour = -1f,
            },
            // ServicePoint, where a car is pulled up to be painted. The shutter faces -X.
            new Shot
            {
                Name = "09-auto-shop", Target = "World/Places/Place_AutoShop",
                Stand = new Vector3(-104f, 0.1f, 246.5f), StandYaw = 90f,
                Azimuth = 90f, Elevation = 5f, Fill = 1.5f, Fov = 60f, AimLift = 2f,
                Hour = -1f,
            },
            // The station's door and its Hebrew sign face -Z, so the facade is shot from the south.
            new Shot
            {
                Name = "10-police-station", Target = "World/Places/Place_PoliceStation",
                Stand = new Vector3(160f, 0.05f, -132f), StandYaw = 0f,
                Azimuth = 0f, Elevation = 6f, Fill = 1.25f, Fov = 60f, AimLift = 2f,
                Hour = -1f,
            },
            // Coordinates from the config: the helicopter spawns at (428, 0.1, -228) and the jetski
            // at (442, 0, -246), both by the shore. The waterline is x = 430, so a camera looking
            // west from over the water gets the city behind the subject instead of empty sea.
            new Shot
            {
                Name = "12-helicopter", Target = "",
                Stand = new Vector3(424f, 0.2f, -228f), StandYaw = 270f,
                Radius = 9f,
                Azimuth = 300f, Elevation = 6f, Fill = 0.55f, Fov = 60f, AimLift = 0f,
                Hour = -1f, Stage = "heli",
            },
            new Shot
            {
                // NOT repositioned - the jetski's spawn is already on the water and Stand would put
                // it on the sand. Stand here only says where the crowd streams from.
                Name = "13-jetski", Target = "",
                Stand = new Vector3(424f, 0f, -246f), StandYaw = 90f,
                Radius = 4.5f,
                Azimuth = 300f, Elevation = 8f, Fill = 0.7f, Fov = 60f, AimLift = 0.5f,
                Hour = -1f, Stage = "jetski",
            },
            // The one frame that keeps the HUD, and the only one shot off the back buffer. Left at
            // the built lighting on purpose: this is what a player actually sees on boot.
            new Shot
            {
                Name = "11-gameplay-hud", Target = "",
                Stand = new Vector3(9f, 0.4f, -60f), StandYaw = 0f,
                Hour = -1f, Hud = true, Stage = "bike",
            },
        };

        // --- what a run is ------------------------------------------------------------------

        private static bool _armed;
        private static string _shotName;
        private static string _outDir;
        private static string _suffix = "";

        /// <summary>
        /// Every framing number is overridable from the command line, and that is not a convenience.
        /// The shot table is compiled into the Player, so without these a one-degree change to an
        /// azimuth costs a full rebuild - twelve minutes, and a cold shader cache if the build target
        /// moved. The loop this rig depends on is shoot → look → nudge → shoot, and a twelve-minute
        /// nudge is not a loop. The table holds what was settled; the flags are how it gets settled.
        /// </summary>
        private static float _hourOverride = float.NaN;
        private static float _azimuthOverride = float.NaN;
        private static float _elevationOverride = float.NaN;
        private static float _fillOverride = float.NaN;
        private static float _fovOverride = float.NaN;
        private static float _aimLiftOverride = float.NaN;
        private static int _hudOverride = -1;   // -1 leave, 0 off, 1 on

        /// <summary>New Game instead of Continue. Resets every paint to the authored atlas -
        /// and wipes the save doing it, so it is opt-in per launch.</summary>
        private static bool _fresh;

        /// <summary>4K, 16:9 - LinkedIn and YouTube both display this aspect, and downscaling a
        /// too-large frame is free where upscaling a too-small one is not.</summary>
        private const int Width = 3840;
        private const int Height = 2160;

#if UNITY_EDITOR || DEVELOPMENT_BUILD

        // --- stage 1: before the world exists -----------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void Configure()
        {
            var argv = Environment.GetCommandLineArgs();
            for (int i = 0; i < argv.Length; i++)
            {
                switch (argv[i])
                {
                    case "-photoShot":   if (i + 1 < argv.Length) { _shotName = argv[i + 1]; _armed = true; } break;
                    case "-photoOut":    if (i + 1 < argv.Length) _outDir = argv[i + 1]; break;
                    case "-photoSuffix": if (i + 1 < argv.Length) _suffix = argv[i + 1]; break;
                    case "-photoHud":    if (i + 1 < argv.Length) _hudOverride = argv[i + 1] == "on" ? 1 : 0; break;
                    case "-photoFresh":  if (i + 1 < argv.Length) _fresh = argv[i + 1] == "on"; break;
                    case "-photoHour":      _hourOverride      = Number(argv, i); break;
                    case "-photoAzimuth":   _azimuthOverride   = Number(argv, i); break;
                    case "-photoElevation": _elevationOverride = Number(argv, i); break;
                    case "-photoFill":      _fillOverride      = Number(argv, i); break;
                    case "-photoFov":       _fovOverride       = Number(argv, i); break;
                    case "-photoAimLift":   _aimLiftOverride   = Number(argv, i); break;
                }
            }

            if (!_armed) return;

            // Without this the window loses focus the moment the shell moves on and the Player
            // stops rendering, so the shutter fires on a frame that was never drawn.
            Application.runInBackground = true;
            Debug.Log($"{Tag}: armed shot={_shotName} out={_outDir}");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!_armed) return;
            var go = new GameObject("__PhotoProbe") { hideFlags = HideFlags.DontSave };
            go.AddComponent<PhotoProbe>();
            DontDestroyOnLoad(go);
        }

        private void Start() => StartCoroutine(RunAndQuit());

        private IEnumerator RunAndQuit()
        {
            if (!TryShot(_shotName, out var shot))
            {
                Fail($"unknown shot '{_shotName}'");
                Application.Quit();
                yield break;
            }

            if (_hudOverride >= 0) shot.Hud = _hudOverride == 1;
            if (!float.IsNaN(_hourOverride))      shot.Hour      = _hourOverride;
            if (!float.IsNaN(_azimuthOverride))   shot.Azimuth   = _azimuthOverride;
            if (!float.IsNaN(_elevationOverride)) shot.Elevation = _elevationOverride;
            if (!float.IsNaN(_fillOverride))      shot.Fill      = _fillOverride;
            if (!float.IsNaN(_fovOverride))       shot.Fov       = _fovOverride;
            if (!float.IsNaN(_aimLiftOverride))   shot.AimLift   = _aimLiftOverride;

            yield return StartGame();

            // A capped frame is not wrong here the way it is for a measurement, but an uncapped one
            // reaches the settled state sooner and the run is shorter for it.
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;

            ApplyHour(shot.Hour);

            yield return Place(shot.Stand, shot.StandYaw);
            yield return Stage(shot);

            // The crowd streams around the player and the shadow cascades take a moment to resolve.
            // This is not PerfProbe's Settle - nothing here is being measured, so a generous fixed
            // wait is honest where a stability test would only be theatre.
            yield return Wait(150);

            if (!shot.Hud) HideHud();

            yield return Capture(shot);

            Application.Quit();
        }

        // --- leaving the title menu -----------------------------------------------------------

        /// <summary>
        /// Leaves the title menu by pressing <b>New Game</b>.
        ///
        /// ⚠ <b>THIS IS THE OPPOSITE OF <see cref="PerfProbe"/>'S RULE, AND IT IS DELIBERATE.</b>
        /// That file says "Continue, never New Game", and memory
        /// <c>new-game-wipes-the-test-balance</c> says the same, because a probe that reset the
        /// user's save on every run would be a far worse bug than any number it could take. Both
        /// still stand - for a measurement.
        ///
        /// A photograph needs something a measurement does not: the vehicles in their AUTHORED
        /// colours. <c>MotorcycleSpawner</c> re-applies whatever the auto shop last painted
        /// (<c>MotorcycleSpawner.cs:79-82</c>), so <c>Continue</c> photographed a bike painted
        /// orange - <c>theblock.paint.Motorcycle</c> - when the asset's own atlas is red.
        /// <c>GameFlow.NewGame</c> calls <c>PaintStore.Reset()</c>, which is the whole reason it is
        /// used here. <b>The user was told it wipes the save and chose it anyway.</b>
        ///
        /// So: do not "fix" this back to Continue. If a future run must not wipe the save, the
        /// alternative is to stash and clear <c>PaintStore.MotorcycleKey</c> around the run.
        ///
        /// ⚠ POLL, NEVER A FIXED FRAME COUNT. <c>BootLoader</c> holds scene activation until ~4.1 s;
        /// PerfProbe's first version waited five frames, found no GameFlow, carried on, and
        /// photographed the title screen while reporting a world pose.
        /// </summary>
        private IEnumerator StartGame()
        {
            yield return WaitFor("TheBlock.UI.Menus.GameFlow", 40f);

            var flow = Find("TheBlock.UI.Menus.GameFlow");
            if (flow == null) { Fail("no GameFlow after 40 s"); yield break; }

            // ⚠ A FLAG, NOT A CONSTANT, and the reason is worth keeping. New Game is the blunt way
            // to reach the authored paint, and it costs the user their save. Whether it is NEEDED
            // depends on whether `theblock.paint.Motorcycle` happens to be set, which is runtime
            // state nobody can read from here. Hard-coding either answer bakes a guess into a
            // twelve-minute build; a flag lets one Player test the cheap path first and fall back.
            var entry = _fresh ? "NewGame" : "Continue";
            var method = flow.GetType().GetMethod(entry,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (method == null) { Fail($"GameFlow.{entry} not found"); yield break; }

            method.Invoke(flow, null);

            yield return WaitFor("TheBlock.Player.PlayerController", 20f);
            Debug.Log($"{Tag}: started via {entry}, timeScale={Time.timeScale}");

            if (_fresh) yield return DismissBriefing();
        }

        /// <summary>
        /// Closes the intro card New Game raises.
        ///
        /// <c>NewGame</c> ends in <c>launch.Launch(0, fresh: true)</c>, and <c>fresh</c> means a
        /// briefing card that a person dismisses with SPACE. Nothing presses it here.
        ///
        /// The nine free-camera frames would never have seen it - UI Toolkit composites to the
        /// screen, not into a camera's target texture, and <see cref="HideHud"/> hides it anyway.
        /// <b>Shot 11 is the one that matters</b>: it is the back buffer, so an undismissed card is
        /// simply what the photograph would be of.
        ///
        /// <c>Dismiss()</c> is public, so this calls it rather than synthesising a keypress - one
        /// less moving part than <see cref="QueueKeys"/>, whose queued state decays.
        /// </summary>
        private IEnumerator DismissBriefing()
        {
            // The card is raised by the launch routine, not by NewGame itself, so it may not exist
            // for a frame or two yet.
            for (int i = 0; i < 120; i++)
            {
                var card = Find("TheBlock.UI.BriefingCard");
                var open = card?.GetType().GetProperty("IsOpen")?.GetValue(card) as bool?;
                if (open == true)
                {
                    card.GetType().GetMethod("Dismiss")?.Invoke(card, null);
                    Debug.Log($"{Tag}: dismissed the briefing card");
                    yield break;
                }
                yield return null;
            }

            Debug.Log($"{Tag}: no briefing card appeared");
        }

        // --- posing -----------------------------------------------------------------------------

        private IEnumerator Place(Vector3 position, float yaw)
        {
            if (position == Vector3.zero) yield break;

            var player = Find("TheBlock.Player.PlayerController");
            if (player == null) { Fail("no PlayerController to place"); yield break; }

            var teleport = player.GetType().GetMethod("Teleport");
            if (teleport != null) teleport.Invoke(player, new object[] { position, yaw });
            else player.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            Physics.SyncTransforms();
            yield return Wait(30);
            Debug.Log($"{Tag}: player at {player.transform.position:F1}");
        }

        /// <summary>
        /// Pins the sun at an hour.
        ///
        /// <c>scrubHour</c> is only consulted inside Test Mode (<c>DayNightCycle.Update</c>), so all
        /// three fields have to be written together. Test Mode's on-screen banner is Editor-only, so
        /// a Player shows nothing - but this is still a per-launch override and never a change to
        /// the built default, which ships <b>Fixed / off</b> and is what every screenshot approved in
        /// U11-U27 reproduces under.
        /// </summary>
        private void ApplyHour(float hour)
        {
            if (hour < 0f) { Debug.Log($"{Tag}: leaving the built lighting alone"); return; }

            var cycle = Find("TheBlock.World.DayNightCycle");
            if (cycle == null) { Debug.LogWarning($"{Tag}: no DayNightCycle"); return; }

            SetPrivate(cycle, "testMode", true);
            SetPrivate(cycle, "scrub", true);
            SetPrivate(cycle, "scrubHour", Mathf.Repeat(hour, 24f));

            var setEnabled = cycle.GetType().GetMethod("SetEnabled");
            setEnabled?.Invoke(cycle, new object[] { true });

            Debug.Log($"{Tag}: hour pinned at {hour:0.00}");
        }

        // --- staging ------------------------------------------------------------------------

        private IEnumerator Stage(Shot shot)
        {
            switch (shot.Stage)
            {
                // W only, and straight. An early bike shot added D for a banked look; it promptly
                // steered off the carriageway and photographed an empty plaza.
                case "bike":
                    yield return StageVehicle(shot, "Motorcycle", Key.W, Key.None, 150, 0.3f);
                    break;

                // SPACE is the collective - it is what gets the helicopter off the pad - and W tips
                // it forward. Held together for longer than the others because altitude is the shot:
                // a helicopter photographed at head height is a prop, not an aircraft.
                case "heli":
                    yield return StageVehicle(shot, "Helicopter", Key.Space, Key.W, 320, 0.5f);
                    break;

                // The jetski's own spawn is already on the water, so it is NOT repositioned - Stand
                // is left alone and only the throttle runs. Moving it would be moving it onto land.
                case "jetski":
                    yield return StageVehicle(shot, "Jetski", Key.W, Key.None, 200, float.NaN);
                    break;

                case "police":
                    yield return StagePolice(shot);
                    break;
            }
        }

        /// <summary>
        /// Puts the player on a vehicle and drives it, because a parked vehicle and a moving one are
        /// the same photograph otherwise.
        ///
        /// All three drivable subjects here - motorcycle, helicopter, jetski - are
        /// <c>IEnterable</c> and all three read <c>Keyboard.current</c> directly in their own
        /// FixedUpdate, so one routine covers them and only the keys and the durations differ.
        ///
        /// ⚠ A queued key state DECAYS, so it is re-queued every frame, and
        /// <c>InputSystem.Update()</c> is never called by hand - memory
        /// <c>synthetic-play-test-decays</c> is the record of that test lying convincingly.
        /// </summary>
        /// <param name="lift">Metres above <see cref="Shot.Stand"/> to reposition the vehicle before
        /// boarding. <c>NaN</c> leaves it wherever it spawned, which is what anything that lives on
        /// water needs - the jetski's spawn IS the shot, and Stand would put it on the sand.</param>
        private IEnumerator StageVehicle(Shot shot, string typeFragment,
                                         Key primary, Key secondary, int frames, float lift)
        {
            var vehicle = FindEnterable(typeFragment);
            if (vehicle == null) { Fail($"no {typeFragment} in EnterableRegistry"); yield break; }

            var vehicles = Find("TheBlock.Vehicles.VehicleEnterExit");
            if (vehicles == null) { Fail("no VehicleEnterExit"); yield break; }

            // ⚠ The helicopter is the ONE vehicle that refuses E. `HelicopterController.Unlocked` is
            // a Func<bool> wired to campaign progress, and this save has `theblock.unlocked = 0`, so
            // Board would be turned away and the shot would be of an empty pad. Overriding the
            // predicate for the run is a photograph's licence, not a gameplay change - nothing is
            // written to the save.
            var unlocked = vehicle.GetType().GetField("Unlocked");
            if (unlocked != null && unlocked.FieldType == typeof(Func<bool>))
            {
                unlocked.SetValue(vehicle, (Func<bool>)(() => true));
                Debug.Log($"{Tag}: unlocked the {typeFragment} for this frame only");
            }

            var craft = vehicle.GetType().GetMethod("GetTransform")?.Invoke(vehicle, null) as Transform;

            // The bike spawns in the car park, which is a photograph of tarmac; move it to the shot
            // instead of moving the shot to it. The velocity has to be cleared by hand or PhysX
            // carries the old one into the new position.
            if (craft != null && !float.IsNaN(lift))
            {
                if (craft.TryGetComponent<Rigidbody>(out var body))
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                craft.SetPositionAndRotation(shot.Stand + Vector3.up * lift,
                                             Quaternion.Euler(0f, shot.StandYaw, 0f));
                Physics.SyncTransforms();
                yield return Wait(10);
            }

            var player = Find("TheBlock.Player.PlayerController");
            if (craft != null && player != null)
            {
                // Board() has no walk-over step, so the player has to already be at the vehicle.
                var teleport = player.GetType().GetMethod("Teleport");
                teleport?.Invoke(player, new object[] { craft.position, craft.eulerAngles.y });
                Physics.SyncTransforms();
                yield return Wait(5);
            }

            var board = vehicles.GetType().GetMethod("Board");
            if (board == null) { Fail("VehicleEnterExit.Board not found"); yield break; }
            var boarded = board.Invoke(vehicles, new object[] { vehicle }) as bool?;
            if (boarded != true) { Fail($"Board refused the {typeFragment}"); yield break; }

            Debug.Log($"{Tag}: boarded the {typeFragment}");

            for (int i = 0; i < frames; i++)
            {
                QueueKeys(primary, secondary);
                yield return null;
            }

            float kmh = 0f;
            var speed = vehicle.GetType().GetProperty("SpeedKmh");
            if (speed != null) kmh = (float)speed.GetValue(vehicle);

            // ⚠ THE TRANSFORM, NOT ITS POSITION, and the first sweep is why. Capturing the position
            // here framed the spot the bike occupied at the END OF STAGING - but RunAndQuit still
            // waits 150 frames for the world to settle before the shutter, and a bike doing 39 km/h
            // covers eighty metres in that time. The frame came back with the subject a speck near
            // the horizon. A moving subject has to be read at the moment of exposure.
            if (craft != null) _focusTransform = craft;

            Debug.Log($"{Tag}: {typeFragment} at {kmh:0} km/h, now at {craft?.position:F1}");
        }

        /// <summary>Set by staging that MOVES its subject; read at exposure, not when set.</summary>
        private static Transform _focusTransform;

        /// <summary>
        /// Raises the wanted level and puts the helicopter overhead, so one frame shows a system
        /// working rather than a place standing still.
        /// </summary>
        private IEnumerator StagePolice(Shot shot)
        {
            var heat = Find("TheBlock.Police.Heat");
            if (heat != null)
            {
                var bump = heat.GetType().GetMethod("Bump");
                for (int i = 0; i < 3; i++) bump?.Invoke(heat, null);
                Debug.Log($"{Tag}: wanted level raised");
            }
            else Debug.LogWarning($"{Tag}: no Heat to raise");

            var police = Find("TheBlock.Police.PoliceSystem");
            if (police != null)
            {
                var field = police.GetType().GetField("heliPrefab",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field?.GetValue(police) is GameObject prefab)
                {
                    var forward = Quaternion.Euler(0f, shot.StandYaw, 0f) * Vector3.forward;
                    var at = shot.Stand + forward * 22f + Vector3.up * 30f;
                    Instantiate(prefab, at, Quaternion.Euler(0f, shot.StandYaw, 0f));
                    Debug.Log($"{Tag}: helicopter at {at}");
                }
            }

            // The cruisers have to actually drive to the player before the frame is worth taking.
            yield return Wait(240);
        }

        // --- the shutter ----------------------------------------------------------------------

        private IEnumerator Capture(Shot shot)
        {
            Directory.CreateDirectory(_outDir);
            var path = Path.Combine(_outDir, shot.Name + _suffix + ".png");
            if (File.Exists(path)) File.Delete(path);

            if (shot.Hud) yield return CaptureBackBuffer(path);
            else yield return CaptureFreeCamera(shot, path);

            if (File.Exists(path))
                Debug.Log($"{Tag}: shot {path} ({new FileInfo(path).Length / 1024} KB)");
            else
                Fail($"no file appeared at {path}");
        }

        /// <summary>
        /// The HUD path. UI Toolkit composites to the screen, not into a camera's target texture, so
        /// a frame that must contain the HUD has to come off the back buffer.
        /// </summary>
        private IEnumerator CaptureBackBuffer(string path)
        {
            ScreenCapture.CaptureScreenshot(path);
            for (int i = 0; i < 240 && !File.Exists(path); i++) yield return null;
        }

        /// <summary>
        /// The composed path: a clone of the game camera, moved somewhere a follow rig would never
        /// put it, rendered into a 4K target.
        ///
        /// <b>Cloned, not built.</b> A bare <c>new GameObject().AddComponent&lt;Camera&gt;()</c> has
        /// no <c>UniversalAdditionalCameraData</c>, so URP renders it with defaults and the grade,
        /// the fog and the post stack all silently differ from the game. Cloning carries the whole
        /// rig across; the components that would MOVE it are then destroyed.
        ///
        /// Depth is 24. A 16-bit request logs a Metal error about a memoryless depth surface -
        /// memory <c>rendertexture-16bit-depth-metal-error</c>.
        /// </summary>
        private IEnumerator CaptureFreeCamera(Shot shot, string path)
        {
            var source = Camera.main;
            if (source == null) { Fail("no Camera.main to clone"); yield break; }

            var clone = Instantiate(source.gameObject);
            clone.name = "__PhotoCam";
            foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>()) Destroy(behaviour);
            foreach (var listener in clone.GetComponentsInChildren<AudioListener>()) Destroy(listener);

            var cam = clone.GetComponent<Camera>();
            if (cam == null) { Fail("the clone has no Camera"); Destroy(clone); yield break; }

            Frame(cam, shot);

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.Default);
            rt.Create();
            cam.targetTexture = rt;
            cam.enabled = false;

            // One frame with the camera in place before rendering, so anything that reacts to a new
            // view - reflection probes, the water - has been given a chance to.
            yield return null;
            cam.Render();

            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;

            File.WriteAllBytes(path, tex.EncodeToPNG());

            Destroy(tex);
            cam.targetTexture = null;
            rt.Release();
            Destroy(clone);
        }

        /// <summary>
        /// Puts the camera on an arc around the subject and derives the distance from its size, so a
        /// shot is authored as "from this side, this high, this tight" rather than as a coordinate
        /// that has to be re-hunted whenever a building moves.
        /// </summary>
        private void Frame(Camera cam, Shot shot)
        {
            var focus = _focusTransform != null
                ? _focusTransform.position
                : (shot.Focus == Vector3.zero ? shot.Stand : shot.Focus);
            float radius = shot.Radius > 0f ? shot.Radius : 12f;

            var target = string.IsNullOrEmpty(shot.Target) ? null : GameObject.Find(shot.Target);
            if (target != null && TryBounds(target, out var bounds))
            {
                focus = bounds.center;
                radius = Mathf.Max(bounds.extents.magnitude, 2f);
                Debug.Log($"{Tag}: framing {shot.Target} centre={bounds.center:F1} radius={radius:0.0}");
            }
            else if (!string.IsNullOrEmpty(shot.Target))
            {
                Debug.LogWarning($"{Tag}: no renderers under '{shot.Target}', framing the stand point");
            }

            focus += Vector3.up * shot.AimLift;

            float fov = shot.Fov <= 0f ? 75f : shot.Fov;

            // ⚠ Fill goes ABOVE 1, and the first shot proved why. `radius` is the bounds' 3D
            // diagonal half-length, so for the falafel stand it read 7.0 m against a building barely
            // 3 m tall - the sign pylon and the width are both in that number. The frame is also
            // 16:9, so the horizontal field is far wider than the vertical one this formula sizes
            // against. Both push the subject smaller than the fraction says: Fill 0.6 rendered at
            // roughly a quarter of the frame. Treat it as a tightness dial, not a percentage.
            float fill = Mathf.Clamp(shot.Fill <= 0f ? 0.6f : shot.Fill, 0.05f, 6f);
            float distance = radius / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / fill;

            var direction = Quaternion.Euler(shot.Elevation, shot.Azimuth, 0f) * Vector3.back;
            var eye = focus + direction * distance;

            cam.fieldOfView = fov;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));

            Debug.Log($"{Tag}: camera at {eye:F1} fov={fov:0} distance={distance:0.0}");
        }

        private static bool TryBounds(GameObject root, out Bounds bounds)
        {
            bounds = default;
            var renderers = root.GetComponentsInChildren<Renderer>();
            bool any = false;
            foreach (var r in renderers)
            {
                if (!r.enabled) continue;
                if (any) bounds.Encapsulate(r.bounds);
                else { bounds = r.bounds; any = true; }
            }
            return any;
        }

        // --- the HUD ----------------------------------------------------------------------------

        /// <summary>
        /// <c>visibility</c>, never <c>display</c>. Several components share these documents and set
        /// <c>display</c> themselves; save-and-restore of it reverts whatever the owner changed in
        /// the meantime - memory <c>hide-shared-hud-with-visibility</c>. Nothing is restored here
        /// because the process quits seconds later.
        /// </summary>
        private void HideHud()
        {
            int hidden = 0;
            foreach (var doc in FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var root = doc.rootVisualElement;
                if (root == null) continue;
                root.style.visibility = Visibility.Hidden;
                hidden++;
            }
            Debug.Log($"{Tag}: hid {hidden} UI documents");
        }

        // --- odds and ends ----------------------------------------------------------------------

        /// <summary>
        /// ⚠ Re-queued every frame by the caller. A queued key state decays, and never call
        /// <c>InputSystem.Update()</c> by hand.
        /// </summary>
        private static void QueueKeys(Key first, Key second)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            var state = new KeyboardState();
            if (first != Key.None) state.Press(first);
            if (second != Key.None) state.Press(second);
            InputSystem.QueueStateEvent(keyboard, state);
        }

        private static object FindEnterable(string typeNameFragment)
        {
            var registry = FindType("TheBlock.Vehicles.EnterableRegistry");
            var all = registry?.GetProperty("All",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (all?.GetValue(null) is not System.Collections.IEnumerable vehicles) return null;

            foreach (var vehicle in vehicles)
                if (vehicle != null && vehicle.GetType().Name.Contains(typeNameFragment)) return vehicle;
            return null;
        }

        private static void SetPrivate(object target, string field, object value)
        {
            var info = target.GetType().GetField(field,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (info == null) { Debug.LogWarning($"{Tag}: no field '{field}'"); return; }
            info.SetValue(target, value);
        }

        /// <summary>Reads the value after <c>argv[i]</c>, or NaN, which every override treats as
        /// "leave the table's number alone".</summary>
        private static float Number(string[] argv, int i)
            => i + 1 < argv.Length && float.TryParse(argv[i + 1], out float value) ? value : float.NaN;

        private static bool TryShot(string name, out Shot shot)
        {
            foreach (var candidate in Shots)
                if (candidate.Name == name) { shot = candidate; return true; }
            shot = default;
            return false;
        }

        private static IEnumerator Wait(int frames)
        {
            for (int i = 0; i < frames; i++) yield return null;
        }

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

        /// <summary>Looked up by name so Core does not take a dependency on the UI, the player, the
        /// police and the vehicles all at once - the same reason <see cref="PerfProbe"/> does it.</summary>
        private static Type FindType(string fullName) => Type.GetType(fullName + ", Assembly-CSharp");

        /// <summary>A run that could not do what it was asked must say so in the log, loudly enough
        /// that <c>tools/press-shots.sh</c> can refuse it.</summary>
        private void Fail(string why) => Debug.LogWarning($"{Tag} FAILED: {why}");

#endif
    }
}
