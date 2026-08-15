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
    /// foot, and <c>CarController.Teleport</c>'s four-step body move while driving — stop the body
    /// before moving it, sync the transforms, brake all four wheels (a WheelCollider latches its last
    /// torque), re-pose. A cutscene controller for a 2.2 s red screen would be a whole mechanism
    /// nothing else wants.
    ///
    /// <b>What you lose.</b> There is no wallet in this port yet, so the fine is tallied and shown
    /// rather than charged — U28 gets a real one and can bill it, the way U18 left its two sounds to
    /// U27. What you actually lose is where you were, which in a city this size is the point.
    /// </summary>
    public class BustSequence : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private VehicleEnterExit vehicles;
        [SerializeField] private FollowCamera followCamera;
        [SerializeField] private WantedHud hud;

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
        }

        /// <summary>Starts the sequence. Ignored if one is already running.</summary>
        public void Begin(Vector3 custody, float hold, int finesOwed, int fine)
        {
            if (Running) return;
            if (player == null) Bind();
            if (player == null) return;

            StartCoroutine(Run(custody, hold, finesOwed, fine));
        }

        private IEnumerator Run(Vector3 custody, float hold, int finesOwed, int fine)
        {
            Running = true;
            Busted?.Invoke();

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
                player.enabled = false;
                frozeOnFoot = true;
            }

            if (hud != null) hud.ShowBusted(fine);
            yield return new WaitForSeconds(hold);

            // Move. The car comes with you — the web keeps yours too, and walking back across the
            // city for a wall scrape would be a punishment out of all proportion to the crime.
            float yaw = 90f;
            if (driving is CarController driven)
            {
                driven.Teleport(custody, Quaternion.Euler(0f, yaw, 0f));
                driven.Driven = true;
            }
            else
            {
                player.Teleport(custody, yaw);
            }

            if (frozeOnFoot) player.enabled = true;
            if (followCamera != null) followCamera.SnapToTarget();
            if (hud != null) hud.HideBusted();

            Running = false;
        }
    }
}
