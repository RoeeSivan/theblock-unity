using System.Collections;
using System.Collections.Generic;
using TheBlock.Audio;
using TheBlock.Core;
using TheBlock.Minigame.Rhythm;
using TheBlock.Player;
using TheBlock.UI;
using TheBlock.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace TheBlock.Missions
{
    /// <summary>
    /// M2, the beach dance — the port of <c>rhythm-mission.ts</c>, <c>countdown.ts</c> and the
    /// <c>startDance</c>/<c>endDance</c> transitions in <c>game/transitions.ts</c>.
    ///
    /// <b>It is a modal takeover, and that is the whole reason the mission contract is as thin as it
    /// is.</b> The pizza run is an objective layered over free-roam; this hides the player, stands a
    /// dancer on the sand, points a different camera at them and takes over the keyboard. Two
    /// missions this dissimilar can only share a lifecycle, never an update loop — which is exactly
    /// what <c>mission.ts</c>'s own comment says.
    ///
    /// Remy grooves through the whole thing on his own looping clip. He is the partner you dance to,
    /// not a quest marker who stands still while you perform at him.
    ///
    /// <b>Winning unlocks the helicopter</b>, which is the campaign cursor's doing rather than
    /// anything here: completing this advances it to <c>heli</c>, and
    /// <see cref="CampaignDirector.IsReached"/> is what the Huey checks.
    /// </summary>
    public class DanceMission : MissionBehaviour
    {
        [Header("Scene — found automatically when left empty")]
        [SerializeField] private CampaignRunner runner;
        [SerializeField] private PlayerController player;
        [SerializeField] private FollowCamera followCamera;
        [SerializeField] private VehicleEnterExit vehicles;
        [SerializeField] private MissionHud hud;
        [SerializeField] private Voice voice;
        [SerializeField] private Conductor conductor;
        [SerializeField] private UIDocument hudDocument;

        [Header("Actors — written by Build Campaign")]
        [Tooltip("The player-dancer on the stage. Hidden until the routine starts.")]
        [SerializeField] private Dancer dancer;

        [Tooltip("Remy on the sand. His Animator holds the same dance controller.")]
        [SerializeField] private Animator giver;

        [Header("Debug")]
        [SerializeField] private bool verbose;

        private TheBlockConfig.RhythmSpec _spec;
        private MissionStatus _status = MissionStatus.Inactive;
        private NotesTrack _track;

        private readonly List<Note> _notes = new();
        private readonly HashSet<int> _judged = new();
        private readonly HashSet<int> _spawned = new();

        private Score _score;
        private float _lastNoteTime;
        private bool _running;
        private bool _entering;
        private float _cheerCooldown;
        private Vector3 _stage;
        private float _stageYaw;

        public override string Id => "dance";
        public override string Title => "Beach Dance";
        public override MissionStatus Status => _status;

        /// <summary>No clock: the SONG is the clock, and it is not a countdown you can lose to.</summary>
        public override float? TimeLeft => null;

        public override string ObjectiveLine =>
            _running ? "Dance!  ←  ↓  ↑  →" : null;

        /// <summary>How close you must stand to Remy for T to start it.</summary>
        public bool NearGiver
        {
            get
            {
                if (_spec?.World?.Npc == null || player == null) return false;
                var npc = Convert.Pos(new Vector3(_spec.World.Npc.X, 0f, _spec.World.Npc.Z));
                var here = player.transform.position;
                var dx = here.x - npc.x;
                var dz = here.z - npc.z;
                return dx * dx + dz * dz <= _spec.World.Npc.TalkRadius * _spec.World.Npc.TalkRadius;
            }
        }

        private void Awake()
        {
            if (runner == null) runner = FindAnyObjectByType<CampaignRunner>();
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (followCamera == null) followCamera = FindAnyObjectByType<FollowCamera>();
            if (vehicles == null) vehicles = FindAnyObjectByType<VehicleEnterExit>();
            if (hud == null) hud = FindAnyObjectByType<MissionHud>();
            if (voice == null) voice = FindAnyObjectByType<Voice>();
            if (conductor == null) conductor = FindAnyObjectByType<Conductor>();
            if (hudDocument == null)
            {
                var found = GameObject.Find("HUD");
                if (found != null) found.TryGetComponent(out hudDocument);
            }
        }

        private void Start()
        {
            _spec = TheBlockConfig.Load()?.Rhythm;
            if (_spec == null)
            {
                Debug.LogError("DanceMission: no rhythmConfig in the snapshot.");
                enabled = false;
                return;
            }

            _stage = Convert.Pos(_spec.World.Stage.Raw);
            _stageYaw = Convert.Yaw(_spec.World.Stage.Yaw) * Mathf.Rad2Deg;

            if (hudDocument != null)
                _track = new NotesTrack(hudDocument.rootVisualElement, _spec.Ui.RingPct);

            if (dancer != null) dancer.gameObject.SetActive(false);
        }

        // ── entry ─────────────────────────────────────────────────────────────────────────────

        public override void Enter()
        {
            if (_status != MissionStatus.Inactive || _entering) return;
            if (!Ready()) return;
            StartCoroutine(EnterRoutine());
        }

        private bool Ready()
        {
            if (dancer != null && conductor != null && conductor.Ready) return true;
            Debug.LogError("DanceMission: no dancer or no song — run The Block → Build Campaign.");
            return false;
        }

        private IEnumerator EnterRoutine()
        {
            _entering = true;

            // Take the stage BEFORE the card goes up, so both figures are already grooving behind
            // it. The web does the same, and it is what makes the instructions read as Remy talking
            // to you rather than as a modal dialog.
            TakeStage();

            voice?.Play(_spec.IntroVoiceUrl);
            if (runner?.Card != null) yield return runner.Card.ShowAndWait(_spec.Instructions);
            voice?.Stop();

            _track?.Show();
            yield return Countdown();

            BeginRoutine();
            _entering = false;
        }

        /// <summary>
        /// Hides the player, reveals the dancer, and points the camera over their shoulder.
        ///
        /// The camera offset is rotated by the dancer's OWN facing, so −z stays behind their back
        /// whichever way the stage points — the same arithmetic the web build does, and the reason
        /// the offset is authored in local space rather than as a world position.
        /// </summary>
        private void TakeStage()
        {
            if (vehicles != null) vehicles.EnterModal();

            if (player != null)
            {
                player.SetVisible(false);

                // Frozen the way the bust freezes an on-foot player — disable the component, leave
                // the capsule alone. Two ways to stop the player moving is how one of them drifts.
                player.enabled = false;
            }

            dancer.transform.SetPositionAndRotation(_stage, Quaternion.Euler(0f, _stageYaw, 0f));
            dancer.gameObject.SetActive(true);
            dancer.ResetRun();

            if (giver != null) giver.CrossFadeInFixedTime("Dance_Partner", 0.25f, 0);

            // The dancer is an IChaseTarget, so the existing camera frames it with no dance-specific
            // camera code — the same swap U9 built for getting into a car.
            if (followCamera != null) followCamera.Follow(dancer);
        }

        private IEnumerator Countdown()
        {
            var steps = _spec.Countdown.Steps;
            if (steps == null || steps.Count == 0) yield break;

            for (var i = 0; i < steps.Count; i++)
            {
                _track?.ShowCount(steps[i], i == steps.Count - 1);
                yield return new WaitForSecondsRealtime(_spec.Countdown.StepSec);
            }

            _track?.HideCount();
        }

        private void BeginRoutine()
        {
            _notes.Clear();
            _judged.Clear();
            _spawned.Clear();
            _score = new Score();
            _cheerCooldown = 0f;

            // Seeded off the frame count so two runs in one session differ, but a run can be
            // reproduced from its seed if a chart ever needs arguing about.
            var seed = Random.Range(int.MinValue, int.MaxValue);
            _notes.AddRange(Beatmap.Generate(_spec.Song, _spec.Beatmap, new System.Random(seed)));
            _lastNoteTime = _notes.Count > 0 ? _notes[_notes.Count - 1].Time : 0f;

            _track?.Reset();
            _track?.Show();
            dancer.PlayIdle();

            conductor.Play(_spec.Song.Offset);
            _running = true;
            _status = MissionStatus.Active;

            if (verbose)
                Debug.Log($"[dance] {_notes.Count} notes, last at {_lastNoteTime:0.0}s, seed {seed}");
        }

        // ── the routine ───────────────────────────────────────────────────────────────────────

        private void Update()
        {
            _track?.Tick(Time.unscaledDeltaTime);
            if (_cheerCooldown > 0f) _cheerCooldown -= Time.unscaledDeltaTime;

            if (!_running)
            {
                TickGiverPrompt();
                return;
            }

            var t = conductor.SongTime;

            for (var i = 0; i < _notes.Count; i++)
            {
                if (_judged.Contains(i)) continue;

                var lead = _notes[i].Time - t; // seconds until it should be hit
                if (!_spawned.Contains(i) && lead <= _spec.ScrollSeconds)
                {
                    _track?.AddNote(i, _notes[i].Dir);
                    _spawned.Add(i);
                }

                if (!_spawned.Contains(i)) continue;

                _track?.MoveNote(i, Mathf.Max(0f, lead / _spec.ScrollSeconds));

                // Dropped past the late edge of the good window without being hit.
                if (lead >= -_spec.HitWindows.Good) continue;
                _judged.Add(i);
                _score = RhythmScoring.Apply(_score, Judgment.Miss, _spec.ScoreValues);
                _track?.RemoveNote(i);
                _track?.ShowJudgment(Judgment.Miss);
                Readout();
                dancer.PlayMiss();
            }

            ReadPresses(t);

            if (t > _lastNoteTime + _spec.EndTailSec || conductor.Ended) Finish();
        }

        /// <summary>T near Remy starts it. The same key the counter uses, for the same reason.</summary>
        private void TickGiverPrompt()
        {
            if (_status != MissionStatus.Inactive || _entering) return;
            if (runner != null && !runner.IsCurrent(Id)) return;
            if (runner?.Card != null && runner.Card.IsOpen) return;
            if (vehicles != null && vehicles.Mode != GameMode.OnFoot) return;
            if (!NearGiver) return;

            hud?.SetPrompt("Press T to dance");
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.tKey.wasPressedThisFrame) Enter();
        }

        private void ReadPresses(float t)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.leftArrowKey.wasPressedThisFrame) Hit(Direction.Left, t);
            if (keyboard.rightArrowKey.wasPressedThisFrame) Hit(Direction.Right, t);
            if (keyboard.upArrowKey.wasPressedThisFrame) Hit(Direction.Up, t);
            if (keyboard.downArrowKey.wasPressedThisFrame) Hit(Direction.Down, t);
        }

        /// <summary>
        /// Judges a press against the note AT THE RING — the nearest unjudged, spawned one, whatever
        /// its direction.
        ///
        /// Two refusals, both the web's and both deliberate: a press outside the good window is
        /// ignored rather than counted as a miss, and the RIGHT key on the WRONG note is ignored
        /// too. Mashing costs you nothing directly; it costs you the notes you were not watching.
        /// </summary>
        public void Hit(Direction dir, float t)
        {
            if (!_running) return;

            var best = -1;
            var bestAbs = float.MaxValue;
            for (var i = 0; i < _notes.Count; i++)
            {
                if (_judged.Contains(i) || !_spawned.Contains(i)) continue;
                var abs = Mathf.Abs(_notes[i].Time - t);
                if (abs >= bestAbs) continue;
                bestAbs = abs;
                best = i;
            }

            if (best < 0) return;

            var judgment = RhythmScoring.Judge(_notes[best].Time - t, _spec.HitWindows);
            if (judgment == null) return;          // not near enough to belong to this note
            if (_notes[best].Dir != dir) return;   // wrong arrow for the note at the ring

            _judged.Add(best);
            _score = RhythmScoring.Apply(_score, judgment.Value, _spec.ScoreValues);
            _track?.RemoveNote(best);
            _track?.FlashRing();
            _track?.ShowJudgment(judgment.Value);
            Readout();
            dancer.PlayHit();

            // Remy's hype: occasional by design, not on every hit — his own chance and cooldown.
            if (_cheerCooldown > 0f || _spec.Cheer?.Urls == null) return;
            if (Random.value > _spec.Cheer.Chance) return;
            voice?.PlayRandom(_spec.Cheer.Urls);
            _cheerCooldown = _spec.Cheer.CooldownSec;
        }

        private void Readout()
        {
            _track?.SetScore(_score.Points);
            _track?.SetCombo(_score.Combo);
        }

        // ── exits ─────────────────────────────────────────────────────────────────────────────

        private void Finish()
        {
            _running = false;
            conductor.Stop();

            var accuracy = _score.Accuracy;
            var failed = _spec.FailBelowAccuracy.HasValue && accuracy < _spec.FailBelowAccuracy.Value;
            _status = failed ? MissionStatus.Failed : MissionStatus.Complete;

            if (failed) dancer.PlayFail();
            else dancer.PlayWin();

            _track?.ShowResult(
                $"{(failed ? "Routine flopped" : "Nailed it!")}\n" +
                $"{_score.Points} pts   ·   {accuracy * 100f:0}%   ·   best combo {_score.MaxCombo}\n" +
                $"perfect {_score.Perfect}   good {_score.Good}   miss {_score.Miss}");

            if (verbose)
                Debug.Log($"[dance] {(failed ? "FAILED" : "PASSED")} {_score.Points} pts, " +
                          $"{accuracy * 100f:0.0}% ({_score.Perfect}/{_score.Good}/{_score.Miss}), " +
                          $"dsp drift {conductor.Drift * 1000f:0.0} ms");

            StartCoroutine(LeaveStage());
        }

        /// <summary>
        /// Holds the result long enough to read, then puts the player back on the sand beside Remy.
        ///
        /// <b>Unscaled</b>, because a card is about the interface and the world's clock has nothing
        /// to do with how fast someone reads.
        /// </summary>
        private IEnumerator LeaveStage()
        {
            yield return new WaitForSecondsRealtime(_spec.ResultHoldSec);

            _track?.Hide();
            if (dancer != null) dancer.gameObject.SetActive(false);
            if (giver != null) giver.CrossFadeInFixedTime("Dance_Idle", 0.25f, 0);

            if (player != null)
            {
                // Beside him, facing him — the web puts the player back at npc.x + 3 for the same
                // reason: stepping out of a cinematic into the exact spot the dancer occupied reads
                // as a teleport, and standing next to the person you were dancing with does not.
                // The sign is flipped because X negates: the web's +3 is Unity's −3.
                var npc = Convert.Pos(new Vector3(_spec.World.Npc.X, 0f, _spec.World.Npc.Z));
                player.Teleport(npc + new Vector3(-3f, 0f, 0f), 90f);
                player.SetVisible(true);
                player.enabled = true;
            }

            if (vehicles != null) vehicles.ExitModal();
            if (followCamera != null) followCamera.FollowPlayer();
            hud?.SetPrompt(null);
        }

        /// <summary>Another go. Same entry path, briefing and all — the routine IS the mission.</summary>
        public override void Retry()
        {
            if (_status != MissionStatus.Failed) return;
            _status = MissionStatus.Inactive;
            Enter();
        }

        /// <summary>
        /// Nothing to tear down: the dance spawns no world state, which is why
        /// <see cref="MissionBehaviour.Cleanup"/> is virtual rather than abstract. The stage is left
        /// by <see cref="LeaveStage"/> on every exit path, win or lose.
        /// </summary>
        public override void Cleanup() { }

        /// <summary>Editor-side wiring, used by The Block → Build Campaign.</summary>
        public void SetActors(Dancer stageDancer, Animator partner)
        {
            dancer = stageDancer;
            giver = partner;
        }
    }
}
