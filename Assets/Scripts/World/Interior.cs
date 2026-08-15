using TheBlock.Core;
using TheBlock.Player;
using TheBlock.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TheBlock.World
{
    /// <summary>
    /// The pizzeria's interior cell and the doorway that gets you in and out of it — the port of
    /// <c>src/world/interior.ts</c> plus <c>transitions.ts</c>'s <c>enterPlace</c>/<c>leavePlace</c>.
    ///
    /// The room is a real room a kilometre from the city (<c>config.interior.offset</c>), not a
    /// separate scene: entering teleports the player into it. That is the web build's design and it
    /// carries — a second scene would mean the street stops simulating the moment you buy a pizza,
    /// which U21's delivery timer and U19's wanted level both care about.
    ///
    /// <b>Can Unity do this better?</b> Two of the web build's three chores here are gone.
    /// Its room lights are switched OFF while you are on the street, because three's forward renderer
    /// costs every light on every shaded fragment CITY-WIDE; URP culls lights per object, so three
    /// point lights a kilometre away cost nothing and simply stay on. And the sun does not have to be
    /// dimmed to keep daylight out of the room — the room has a ceiling and URP shadows it. What is
    /// left is the palette: fog and ambient are global render settings in both engines, so those are
    /// still swapped, and swapping them is what makes the inside feel like an inside.
    /// </summary>
    public class Interior : MonoBehaviour
    {
        [Header("Scene — found automatically when left empty")]
        [SerializeField] private PlayerController player;
        [SerializeField] private FollowCamera followCamera;
        [SerializeField] private VehicleEnterExit vehicles;

        [Header("Doorway — written by WorldBuilder from config.interior")]
        [Tooltip("The circle on the street that takes you in. World space, already converted.")]
        [SerializeField] private Vector3 streetDoor;
        [SerializeField] private float streetDoorRadius = 2.5f;

        [Tooltip("Where the player lands inside, and the yaw they land facing.")]
        [SerializeField] private Vector3 spawnPoint;
        [SerializeField] private float spawnYaw;

        [Tooltip("Stand here and press E to leave.")]
        [SerializeField] private Vector3 exitPad;
        [SerializeField] private float exitPadRadius = 1.3f;

        [Tooltip("Height the player is put back at on the street — config.player.spawn.y.")]
        [SerializeField] private float streetY = 1f;

        [Header("Interior palette — config.interior.palette")]
        [SerializeField] private Color fogColor = Color.black;
        [SerializeField] private float fogNear = 5f;
        [SerializeField] private float fogFar = 26f;
        [SerializeField] private Color ambientColor = Color.white;
        [SerializeField] private float ambientIntensity = 0.45f;

        // --- run state --------------------------------------------------------------------------
        // Serialized for the same reason VehicleEnterExit's is: a recompile during Play reloads the
        // domain but keeps the scene, and a machine that forgets it is indoors would put the street
        // palette back with the player still standing in the room — or teleport them to the
        // storefront from the middle of the city on the next E.

        [SerializeField, HideInInspector] private bool inside;
        [SerializeField, HideInInspector] private Color streetFogColor;
        [SerializeField, HideInInspector] private float streetFogNear;
        [SerializeField, HideInInspector] private float streetFogFar;
        [SerializeField, HideInInspector] private bool streetFogEnabled;
        [SerializeField, HideInInspector] private Color streetAmbientColor;
        [SerializeField, HideInInspector] private float streetAmbientIntensity;

        /// <summary>True while the player is in the room. U19's police and U21's mission both read it.</summary>
        public bool Inside => inside;

        /// <summary>Where the player stands inside, in world space. U21's counter NPC needs it.</summary>
        public Vector3 SpawnPoint => spawnPoint;

        private void Awake() => Bind();

        private void Bind()
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (followCamera == null) followCamera = FindAnyObjectByType<FollowCamera>();
            if (vehicles == null) vehicles = FindAnyObjectByType<VehicleEnterExit>();

            if (player == null)
            {
                Debug.LogError("Interior: no PlayerController in the scene.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            if (player == null) Bind();
            if (player == null) return;

            // Only on foot. Driving through the storefront window should not teleport a car into a
            // room with no floor for it, and the enter/exit machine owns E while it is running.
            if (vehicles != null && vehicles.Mode != GameMode.OnFoot) return;

            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.eKey.wasPressedThisFrame) return;

            // E is shared with getting into a vehicle. A car parked on the pavement outside the
            // pizzeria would otherwise win or lose the key at random, so the doorway defers: it only
            // acts when the enter/exit machine found nothing to get into.
            var here = player.transform.position;
            if (!inside)
            {
                if (vehicles != null && vehicles.HasVehicleInReach) return;
                if (WithinXZ(here, streetDoor, streetDoorRadius)) Enter();
            }
            else if (WithinXZ(here, exitPad, exitPadRadius))
            {
                Leave();
            }
        }

        private void Enter()
        {
            streetFogEnabled = RenderSettings.fog;
            streetFogColor = RenderSettings.fogColor;
            streetFogNear = RenderSettings.fogStartDistance;
            streetFogFar = RenderSettings.fogEndDistance;
            streetAmbientColor = RenderSettings.ambientLight;
            streetAmbientIntensity = RenderSettings.ambientIntensity;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogNear;
            RenderSettings.fogEndDistance = fogFar;
            RenderSettings.ambientLight = ambientColor;
            RenderSettings.ambientIntensity = ambientIntensity;

            inside = true;
            Teleport(spawnPoint, spawnYaw);
        }

        private void Leave()
        {
            RenderSettings.fog = streetFogEnabled;
            RenderSettings.fogColor = streetFogColor;
            RenderSettings.fogStartDistance = streetFogNear;
            RenderSettings.fogEndDistance = streetFogFar;
            RenderSettings.ambientLight = streetAmbientColor;
            RenderSettings.ambientIntensity = streetAmbientIntensity;

            inside = false;
            // Back out onto the pavement facing away from the storefront, as the web build does.
            Teleport(new Vector3(streetDoor.x, streetY, streetDoor.z), 90f);
        }

        /// <summary>
        /// Moves the player and snaps the camera behind them.
        ///
        /// The CharacterController has to be switched off across the write: it caches its own
        /// position and will happily sweep the capsule from the pizzeria back to the city if it is
        /// left enabled, which reads as the player being dragged through every building on the way.
        /// The camera snap is the same reason the web build has one — without it the boom lerps
        /// across a kilometre of city while the player stands in a lit room.
        /// </summary>
        private void Teleport(Vector3 position, float yawDegrees)
        {
            // The capsule dance itself now lives on PlayerController, because U19's bust does the
            // same move and a second copy is how one of them loses the guard.
            player.Teleport(position, yawDegrees);
            if (followCamera != null) followCamera.SnapToTarget();
        }

        /// <summary>Distance on the ground plane — a doorway does not care how high you are standing.</summary>
        private static bool WithinXZ(Vector3 a, Vector3 b, float radius)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return dx * dx + dz * dz <= radius * radius;
        }

        /// <summary>Draws the two circles in the Scene view — they are invisible otherwise.</summary>
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.7f, 0.2f, 0.9f);
            DrawCircle(streetDoor, streetDoorRadius);
            DrawCircle(exitPad, exitPadRadius);
            Gizmos.DrawSphere(spawnPoint, 0.15f);
        }

        private static void DrawCircle(Vector3 centre, float radius)
        {
            const int steps = 32;
            var previous = centre + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= steps; i++)
            {
                var angle = i / (float)steps * Mathf.PI * 2f;
                var next = centre + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
