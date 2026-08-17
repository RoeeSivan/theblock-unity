using System.Collections;
using TheBlock.Core;
using TheBlock.Player;
using TheBlock.UI;
using TheBlock.Vehicles;
using UnityEngine;

namespace TheBlock.Police
{
    /// <summary>
    /// Being caught: freeze, show it, move you to the station, hand control back.
    ///
    /// <b>It invents no new way to take control.</b> The project already has exactly three, and this
    /// uses two of them: <c>VehicleEnterExit</c>'s "disable the controller and the capsule" while on
    /// foot, and <c>CarController.Teleport</c>'s four-step body move while driving - stop the body
    /// before moving it, sync the transforms, brake all four wheels (a WheelCollider latches its last
    /// torque), re-pose. A cutscene controller for a 2.2 s red screen would be a whole mechanism
    /// nothing else wants.
    ///
    /// <b>What you lose depends on whether you were driving, and that is the user's call.</b> Caught
    /// in a vehicle, the car is impounded and you are both moved to the station - you lose where you
    /// were, which in a city this size is the real cost. Caught on foot, you are cuffed where you
    /// stand and lose only the money and the time: there is no car to impound, and hauling a
    /// pedestrian across town for a hit-and-run they committed on foot is a punishment with no
    /// mechanism behind it. Money goes either way.
    /// </summary>
    public class BustSequence : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private VehicleEnterExit vehicles;
        [SerializeField] private FollowCamera followCamera;
        [SerializeField] private WantedHud hud;
        [SerializeField] private PlayerRagdoll ragdoll;

        /// <summary>Raised once the freeze begins. U20 hangs its mission failure here.</summary>
        public event System.Action Busted;

        public bool Running { get; private set; }

        private void Awake() => Bind();

        private void Bind()
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (vehicles == null) vehicles = FindAnyObjectByType<VehicleEnterExit>();
            if (followCamera == null) followCamera = FindAnyObjectByType<FollowCamera>();
            if (hud == null) hud = FindAnyObjectByType<WantedHud>();
            if (ragdoll == null) ragdoll = FindAnyObjectByType<PlayerRagdoll>();
        }

        /// <summary>Starts the sequence. Ignored if one is already running.</summary>
        public void Begin(Vector3 custody, float hold, int taken, int owed)
        {
            if (Running) return;
            if (player == null) Bind();
            if (player == null) return;

            StartCoroutine(Run(custody, hold, taken, owed));
        }

        private IEnumerator Run(Vector3 custody, float hold, int taken, int owed)
        {
            Running = true;
            Busted?.Invoke();

            // The heavy "you're caught" sting. The sirens need no explicit cut here: clearing the
            // stars sends every cop home, `Siren.Wanted` goes false with the mode, and the
            // arbitration stops them on the same frame - which is the web's
            // `pursuitAudio.setChaseActive(false)` arriving as a consequence rather than a call.
            TheBlock.Audio.GameAudio.Cue(TheBlock.Audio.SfxCue.Busted);

            var driving = vehicles != null && vehicles.Mode == GameMode.Driving
                ? vehicles.ActiveVehicle
                : null;

            // Freeze. On foot that is the controller and the capsule; driving it is the car's own
            // input gate plus a full stop, because a 20 m/s car left rolling under a red screen
            // arrives at the station already crashed.
            bool frozeOnFoot = false;
            if (driving is CarController car)
            {
                car.Driven = false;
                car.SetInput(CarInput.None);
                if (car.Body != null)
                {
                    car.Body.linearVelocity = Vector3.zero;
                    car.Body.angularVelocity = Vector3.zero;
                }
            }
            else if (driving != null)
            {
                driving.Driven = false;
            }
            else
            {
                // A body that is already DOWN is not this sequence's to freeze, and above all not
                // its to hand back. <see cref="PlayerRagdoll"/> owns the controller from the throw
                // until it stands the player up, and switching it on under a crumpled skeleton gives
                // the player a walking capsule while the visible body is still a heap on the road -
                // which is a normal way for a pursuit to end, not an exotic one: come off the bike
                // hard, and the officer arrests you where you land.
                frozeOnFoot = ragdoll == null || !ragdoll.Down;
                if (frozeOnFoot) player.enabled = false;
            }

            if (hud != null) hud.ShowBusted(taken, owed, driving != null);
            yield return new WaitForSeconds(hold);

            // IMPOUNDED, and only when there was something to impound. A driven vehicle goes to the
            // station with you; on foot you are cuffed where you stand, so nothing moves and the
            // camera does not need snapping either.
            if (driving is CarController driven)
            {
                driven.Teleport(custody, Quaternion.Euler(0f, 90f, 0f));
                driven.Driven = true;
                if (followCamera != null) followCamera.SnapToTarget();
            }
            else if (driving != null)
            {
                // The bike has no Teleport of its own, so it rides along by hand - in the order
                // CarController documents, because the reason for that order is the physics and not
                // the class: stop the body, move it, then sync.
                var root = driving.GetTransform();
                if (root.TryGetComponent<Rigidbody>(out var body))
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                root.SetPositionAndRotation(custody, Quaternion.Euler(0f, 90f, 0f));
                Physics.SyncTransforms();
                driving.Driven = true;
                if (followCamera != null) followCamera.SnapToTarget();
            }

            // Checked again rather than trusted from before the wait: the ragdoll can settle and
            // stand up during the hold, and it re-enables the controller itself when it does.
            if (frozeOnFoot && (ragdoll == null || !ragdoll.Down)) player.enabled = true;
            if (hud != null) hud.HideBusted();

            Running = false;
        }
    }
}
