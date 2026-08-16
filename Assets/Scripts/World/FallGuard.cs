using TheBlock.Core;
using TheBlock.Player;
using TheBlock.Vehicles;
using UnityEngine;

namespace TheBlock.World
{
    /// <summary>
    /// Catches whatever has fallen out of the world and puts it back where it last stood.
    ///
    /// <b>Why this exists on top of the fence.</b> `WorldBuilder.BuildWorldEdges` seals the plate's
    /// rim, which is where the user found the hole - but a fence answers "the edge of the map" and
    /// not "a gap between two districts", and it cannot answer a hole nobody has found yet. The
    /// failure mode is what makes it worth a component: a car that goes through the floor does not
    /// land, it falls for the rest of the session. The player's only way out is Quit to Title, and
    /// during a mission that is the mission gone.
    ///
    /// <b>It puts you back where you were, not at a spawn.</b> A respawn is already on `R`
    /// (<see cref="IEnterable.Respawn"/>), and it teleports a car to the lot it was parked in, which
    /// after ten minutes of driving is a punishment for the game's own bug. This remembers the last
    /// place the thing you are controlling was standing on real ground, twice a second, and returns
    /// you there facing the way you were.
    ///
    /// <b>Both modes, one mechanism.</b> The focus is the vehicle while driving and the player on
    /// foot - the same resolution `CrowdSpawner` and the map already use, and it has to be: a car
    /// falls with you inside it, and the thing that has to be teleported is the car.
    /// </summary>
    public class FallGuard : MonoBehaviour
    {
        [Tooltip("Below this Y, whatever is being controlled has left the world. The plate sits at " +
                 "−0.05 and the seabed ramps to about −3, so −25 is far below anything the game has " +
                 "a floor for and far above the point where a fall has been going long enough to " +
                 "notice.")]
        [SerializeField] private float floorY = -25f;

        [Tooltip("Seconds between safe-spot samples. Twice a second is 2 raycasts, and a car at " +
                 "100 km/h moves 14 m between them - close enough to where you fell.")]
        [SerializeField] private float sampleInterval = 0.5f;

        [Tooltip("How far below the focus a downward ray still counts as standing on something.")]
        [SerializeField] private float groundReach = 4f;

        [Tooltip("Metres above the remembered ground to drop you back in, so the recovery does not " +
                 "start inside the road.")]
        [SerializeField] private float dropHeight = 0.6f;

        [Tooltip("What counts as ground for the sample. The walls are on Ignore Raycast and are " +
                 "excluded by that, which is exactly why they are on it.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Wiring")]
        [SerializeField] private PlayerController player;
        [SerializeField] private VehicleEnterExit vehicles;

        private Vector3 _safePosition;
        private float _safeYaw;
        private bool _hasSafe;
        private float _since;

        /// <summary>How many times this session has caught something. Read by nothing; logged.</summary>
        public int Catches { get; private set; }

        private void Awake() => Bind();

        private void Bind()
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (vehicles == null) vehicles = FindAnyObjectByType<VehicleEnterExit>();
        }

        private void Update()
        {
            if (player == null || vehicles == null) { Bind(); return; }
            if (Pause.Frozen) return;

            var focus = Focus();
            if (focus == null) return;

            if (focus.position.y < floorY)
            {
                Recover(focus);
                return;
            }

            _since -= Time.deltaTime;
            if (_since > 0f) return;
            _since = sampleInterval;

            Remember(focus);
        }

        /// <summary>What the player is currently riding in, or the player.</summary>
        private Transform Focus()
        {
            if (vehicles.Mode != GameMode.OnFoot)
            {
                var vehicle = vehicles.ActiveVehicle;
                if (vehicle != null) return vehicle.GetTransform();
            }

            return player.transform;
        }

        /// <summary>
        /// Records the spot only when there is something solid under it.
        ///
        /// The test is a short downward ray rather than "is it grounded", because a car mid-jump over
        /// a kerb is not grounded and is still over perfectly good road - and, more to the point, a
        /// car that has just left the world through a hole is not grounded either. Remembering a spot
        /// with nothing under it would hand the recovery a place to fall from again.
        /// </summary>
        private void Remember(Transform focus)
        {
            var from = focus.position + Vector3.up * 1f;
            if (!Physics.Raycast(from, Vector3.down, out var hit, groundReach + 1f, groundMask,
                    QueryTriggerInteraction.Ignore))
                return;

            _safePosition = hit.point;
            _safeYaw = focus.eulerAngles.y;
            _hasSafe = true;
        }

        /// <summary>
        /// Put it back. A vehicle is teleported through its own interface, which is what zeroes the
        /// body and re-poses four WheelColliders; the player goes through
        /// <see cref="PlayerController.Teleport"/>, which owns the CharacterController guard.
        /// </summary>
        private void Recover(Transform focus)
        {
            Catches++;
            var target = _safePosition + Vector3.up * dropHeight;

            if (!_hasSafe)
            {
                // Nothing was ever sampled - the fall began before the first sample, which on this
                // map means it started at the spawn. The vehicle's own home is the only answer left,
                // and on foot there is nothing to do but lift the player to the plate.
                if (vehicles.Mode != GameMode.OnFoot && vehicles.ActiveVehicle != null)
                {
                    vehicles.ActiveVehicle.Respawn();
                    Debug.LogWarning($"FallGuard: caught {focus.name} below y {floorY:0} with no safe " +
                                     "spot recorded - respawned it at its home instead.", this);
                    return;
                }

                target = new Vector3(focus.position.x, 1f, focus.position.z);
            }

            if (vehicles.Mode != GameMode.OnFoot && vehicles.ActiveVehicle != null)
                vehicles.ActiveVehicle.Teleport(target, Quaternion.Euler(0f, _safeYaw, 0f));
            else
                player.Teleport(target, _safeYaw);

            Debug.LogWarning(
                $"FallGuard: {focus.name} fell below y {floorY:0} and was put back at " +
                $"{target} (catch #{Catches}). That is a hole in the world - worth finding.", this);
        }
    }
}
