using TheBlock.Core;
using TheBlock.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// U8 SCAFFOLDING — delete this file at U9.
    ///
    /// Teleports between Joe and the nearest car on <c>V</c>, so U8's driving can be play-tested
    /// before the real enter/exit exists. There is no proximity check, no door, no walk-to-the-car
    /// and no seated driver: all of that IS U9, and faking it here would only mean writing it twice.
    ///
    /// The one thing it does share with U9 is the shape of the swap — disable the controller you are
    /// leaving, enable the one you are taking, point the camera at the new <see cref="IChaseTarget"/>.
    /// That much is the seam this unit was meant to leave behind.
    /// </summary>
    public class DebugVehicleSwitch : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private FollowCamera followCamera;
        [SerializeField] private CarSpawner spawner;

        [Tooltip("Where Joe is put down when he leaves the car, beside the driver's door.")]
        [SerializeField] private Vector3 exitOffset = new Vector3(-2f, 0f, 0f);

        private CarController _driving;

        private void Awake()
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (followCamera == null) followCamera = FindAnyObjectByType<FollowCamera>();
            if (spawner == null) spawner = FindAnyObjectByType<CarSpawner>();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.vKey.wasPressedThisFrame) return;

            if (_driving == null) Enter();
            else Exit();
        }

        private void Enter()
        {
            var car = Nearest();
            if (car == null)
            {
                Debug.LogWarning("DebugVehicleSwitch: no car in the scene to get into.", this);
                return;
            }

            _driving = car;
            car.Driven = true;

            if (player != null)
            {
                player.enabled = false;
                player.gameObject.SetActive(false);
            }

            if (followCamera != null) followCamera.Follow(car);
            Debug.Log($"[V] driving {car.name}. Press V again to get out.");
        }

        private void Exit()
        {
            var car = _driving;
            _driving = null;
            car.Driven = false;

            if (player != null)
            {
                // Sits Joe beside the car and leaves him there; the CharacterController has to be
                // off while its transform is written or it puts him straight back.
                var drop = car.transform.TransformPoint(exitOffset);
                if (player.TryGetComponent<CharacterController>(out var controller)) controller.enabled = false;
                player.transform.SetPositionAndRotation(drop, car.transform.rotation);
                if (controller != null) controller.enabled = true;

                player.gameObject.SetActive(true);
                player.enabled = true;
            }

            if (followCamera != null) followCamera.FollowPlayer();
            Debug.Log($"[V] out of {car.name}.");
        }

        private CarController Nearest()
        {
            if (spawner == null || spawner.Spawned.Count == 0) return null;
            if (player == null) return spawner.Spawned[0];

            CarController best = null;
            var bestDistance = float.MaxValue;
            foreach (var car in spawner.Spawned)
            {
                if (car == null) continue;
                var distance = (car.transform.position - player.transform.position).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = car;
            }

            return best;
        }
    }
}
