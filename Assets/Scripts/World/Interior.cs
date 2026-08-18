using TheBlock.Core;
using TheBlock.Player;
using TheBlock.UI;
using TheBlock.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TheBlock.World
{
    /// <summary>
    /// The pizzeria's interior cell and the doorway that gets you in and out of it - the port of
    /// <c>src/world/interior.ts</c> plus <c>transitions.ts</c>'s <c>enterPlace</c>/<c>leavePlace</c>.
    ///
    /// The room is a real room a kilometre from the city (<c>config.interior.offset</c>), not a
    /// separate scene: entering teleports the player into it. That is the web build's design and it
    /// carries - a second scene would mean the street stops simulating the moment you buy a pizza,
    /// which U21's delivery timer and U19's wanted level both care about.
    ///
    /// <b>Can Unity do this better?</b> Two of the web build's three chores here are gone.
    /// Its room lights are switched OFF while you are on the street, because three's forward renderer
    /// costs every light on every shaded fragment CITY-WIDE; URP culls lights per object, so three
    /// point lights a kilometre away cost nothing and simply stay on. And the sun does not have to be
    /// dimmed to keep daylight out of the room - the room has a ceiling and URP shadows it. What is
    /// left is the palette: fog and ambient are global render settings in both engines, so those are
    /// still swapped, and swapping them is what makes the inside feel like an inside.
    /// </summary>
    public class Interior : MonoBehaviour
    {
        [Header("Scene - found automatically when left empty")]
        [SerializeField] private PlayerController player;
        [SerializeField] private FollowCamera followCamera;
        [SerializeField] private VehicleEnterExit vehicles;

        [Tooltip("Where the doorway's own prompts are drawn. Optional; without it E still works.")]
        [SerializeField] private MissionHud hud;

        [Header("Doorway - written by WorldBuilder from config.interior")]
        [Tooltip("The circle on the street that takes you in. World space, already converted.")]
        [SerializeField] private Vector3 streetDoor;
        [SerializeField] private float streetDoorRadius = 2.5f;

        [Tooltip("Where the player lands inside, and the yaw they land facing.")]
        [SerializeField] private Vector3 spawnPoint;
        [SerializeField] private float spawnYaw;

        [Tooltip("Stand here and press E to leave.")]
        [SerializeField] private Vector3 exitPad;
        [SerializeField] private float exitPadRadius = 1.3f;

        [Tooltip("Height the player is put back at on the street - config.player.spawn.y.")]
        [SerializeField] private float streetY = 1f;

        [Header("The counter - config.interior.npc, U21's mechanics")]
        [Tooltip("Where the cashier stands, world space. WorldBuilder writes it from the config.")]
        [SerializeField] private Vector3 counterNpc;

        [SerializeField] private float counterTalkRadius = 3.5f;

        [Header("Interior palette - config.interior.palette")]
        [SerializeField] private Color fogColor = Color.black;
        [SerializeField] private float fogNear = 5f;
        [SerializeField] private float fogFar = 26f;
        [SerializeField] private Color ambientColor = Color.white;
        [SerializeField] private float ambientIntensity = 0.45f;

        // --- run state --------------------------------------------------------------------------
        // Serialized for the same reason VehicleEnterExit's is: a recompile during Play reloads the
        // domain but keeps the scene, and a machine that forgets it is indoors would put the street
        // palette back with the player still standing in the room - or teleport them to the
        // storefront from the middle of the city on the next E.

        [SerializeField, HideInInspector] private bool inside;

        // The street palette as WorldBuilder baked it, captured once in Awake before anything has
        // written those fields. It is the FALLBACK for Leave() when no DayNightCycle is in the scene
        // - with no cycle nothing else moves the sky, so the as-built value cannot go stale. Do not
        // mistake this for the per-Enter snapshot U33 deleted; see PaintInterior for why that one was
        // wrong and this one is not. Serialized because a recompile during Play reloads the domain
        // without re-running Awake.
        [SerializeField, HideInInspector] private bool streetCaptured;
        [SerializeField, HideInInspector] private bool streetFogEnabled;
        [SerializeField, HideInInspector] private Color streetFogColor;
        [SerializeField, HideInInspector] private float streetFogNear;
        [SerializeField, HideInInspector] private float streetFogFar;
        [SerializeField, HideInInspector] private Color streetAmbientColor;
        [SerializeField, HideInInspector] private float streetAmbientIntensity;
        [SerializeField, HideInInspector] private Color streetAmbientSky;
        [SerializeField, HideInInspector] private Color streetAmbientEquator;
        [SerializeField, HideInInspector] private Color streetAmbientGround;

        /// <summary>True while the player is in the room. U19's police and U21's mission both read it.</summary>
        public bool Inside => inside;

        /// <summary>Where the player stands inside, in world space. U21's counter NPC needs it.</summary>
        public Vector3 SpawnPoint => spawnPoint;

        /// <summary>
        /// The doorway on the street, in world space - where you are put down when you step out.
        /// U21's retry parks your ride against it.
        /// </summary>
        public Vector3 StreetDoor => streetDoor;

        /// <summary>
        /// Standing at the counter, close enough to talk. The SAME predicate behind both the prompt
        /// and the action, so the two cannot drift - the arrangement the web build settled on after
        /// its cashier offered "Press T to start your shift" for a key that did nothing.
        /// </summary>
        public bool NearCounter => inside && player != null &&
                                   WithinXZ(player.transform.position, counterNpc, counterTalkRadius);

        /// <summary>On the mat by the door, where E leaves. Read by the prompt.</summary>
        public bool AtExitPad => inside && player != null &&
                                 WithinXZ(player.transform.position, exitPad, exitPadRadius);

        private void Awake()
        {
            CaptureStreet();
            Bind();
            _lamps = GetComponentsInChildren<Light>(true);
            SetLampShadows(inside);
        }

        /// <summary>
        /// The room's three point lights - see <c>WorldBuilder.BuildInteriorLights</c>.
        ///
        /// <b>Their SHADOWS are switched with the door, and that is U30b's first measured saving.</b>
        /// The builder's note that "URP culls per object, so three point lights nobody can see cost
        /// nothing" is true of the lighting and false of the shadows: a shadow-casting point light is
        /// six shadow-map faces rendered every frame the light is inside the shadow distance, whether
        /// or not any lit surface is on screen. The first macOS Player logged it on every frame -
        /// "Reduced additional punctual light shadows resolution by 4 to make 18 shadow maps fit in the
        /// 2048x2048 shadow atlas" - 18 being exactly these three lamps × 6 faces, drawn over a district
        /// from a room the player was not in. Soft shadows inside, none outside; nothing visible changes.
        /// </summary>
        private Light[] _lamps = System.Array.Empty<Light>();

        private void SetLampShadows(bool on)
        {
            foreach (var lamp in _lamps)
            {
                if (lamp == null) continue;
                lamp.shadows = on ? LightShadows.Soft : LightShadows.None;
            }
        }

        /// <summary>
        /// Reads the street palette once, at boot. Skipped when <c>inside</c> is already true - that
        /// is a recompile mid-Play with the player standing in the room, where <c>RenderSettings</c>
        /// holds the INTERIOR palette and the serialized copy from before the reload is the good one.
        /// </summary>
        private void CaptureStreet()
        {
            if (streetCaptured || inside) return;

            streetFogEnabled = RenderSettings.fog;
            streetFogColor = RenderSettings.fogColor;
            streetFogNear = RenderSettings.fogStartDistance;
            streetFogFar = RenderSettings.fogEndDistance;
            streetAmbientColor = RenderSettings.ambientLight;
            streetAmbientIntensity = RenderSettings.ambientIntensity;
            streetAmbientSky = RenderSettings.ambientSkyColor;
            streetAmbientEquator = RenderSettings.ambientEquatorColor;
            streetAmbientGround = RenderSettings.ambientGroundColor;
            streetCaptured = true;
        }

        private void Bind()
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (followCamera == null) followCamera = FindAnyObjectByType<FollowCamera>();
            if (vehicles == null) vehicles = FindAnyObjectByType<VehicleEnterExit>();
            if (hud == null) hud = FindAnyObjectByType<MissionHud>();

            if (player == null)
            {
                Debug.LogError("Interior: no PlayerController in the scene.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            if (Core.Pause.Frozen) return; // no E through a menu - see Core.Pause

            if (player == null) Bind();
            if (player == null) return;

            // Only on foot. Driving through the storefront window should not teleport a car into a
            // room with no floor for it, and the enter/exit machine owns E while it is running.
            if (vehicles != null && vehicles.Mode != GameMode.OnFoot) return;

            // E is shared with getting into a vehicle. A car parked on the pavement outside the
            // pizzeria would otherwise win or lose the key at random, so the doorway defers: it only
            // acts when the enter/exit machine found nothing to get into. The PROMPT defers on the
            // same test, so what is offered and what E does can never disagree.
            var here = player.transform.position;
            var atDoor = !inside && WithinXZ(here, streetDoor, streetDoorRadius) &&
                         (vehicles == null || !vehicles.HasVehicleInReach);
            var atPad = inside && WithinXZ(here, exitPad, exitPadRadius);

            if (atDoor) hud?.SetPrompt("Press E to go inside", MissionHud.PromptDoor);
            else if (atPad) hud?.SetPrompt("Press E to leave", MissionHud.PromptDoor);

            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.eKey.wasPressedThisFrame) return;

            if (atDoor) Enter();
            else if (atPad) Leave();
        }

        /// <summary>
        /// Steps out onto the street. Public because the delivery mission's entry sequence ends with
        /// it: the briefing is read at the counter and the shift begins outside, so the mission has
        /// to be able to say "leave" without learning how the teleport or the palette swap work.
        /// </summary>
        public void LeaveNow()
        {
            if (inside) Leave();
        }

        /// <summary>
        /// Steps in from anywhere in the city, without standing on the doormat first - the twin of
        /// <see cref="LeaveNow"/>, and what U21's retry uses to put a failed rider back at the shop.
        ///
        /// <b>On foot only.</b> The room is a cell a kilometre away with no floor for a vehicle,
        /// which is the same reason <see cref="Update"/> refuses the doorway while driving. A caller
        /// that wants this from the saddle has to get off first.
        /// </summary>
        public void EnterNow()
        {
            if (inside) return;
            if (vehicles != null && vehicles.Mode != GameMode.OnFoot) return;
            Enter();
        }

        /// <summary>
        /// Paints the room's palette and hands the street's back.
        ///
        /// <b>This used to snapshot six <c>RenderSettings</c> fields on the way in and replay them on
        /// the way out, and U33 deleted that.</b> It is the same fault U26 paid for with the Radar
        /// toggle: remembering shared state and restoring it looks careful and is exactly wrong the
        /// moment a SECOND writer owns the same fields. Against <see cref="DayNightCycle"/> it failed
        /// twice over - the cycle overwrote the room's warm fog on the very next frame, and the
        /// restore on the way out handed the street back a colour from whatever hour you walked in
        /// at. One owner: the cycle stands down while you are inside, and re-derives the street from
        /// the clock when you step out. There is nothing to remember, so there is nothing to fight.
        /// </summary>
        private void PaintInterior()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogNear;
            RenderSettings.fogEndDistance = fogFar;

            // Written as BOTH a flat colour and a Trilight triple, because which one renders depends
            // on the cycle's setting - and until U33 neither did. The scene is AmbientMode.Skybox,
            // where ambientLight and ambientIntensity are ignored, so the room's {1, 0.808, 0.584}
            // at 0.45 had never actually rendered since U13.
            RenderSettings.ambientLight = ambientColor;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.ambientSkyColor = ambientColor * ambientIntensity;
            RenderSettings.ambientEquatorColor = ambientColor * (ambientIntensity * 0.7f);
            RenderSettings.ambientGroundColor = ambientColor * (ambientIntensity * 0.35f);
        }

        /// <summary>The as-built street, for a scene with no <see cref="DayNightCycle"/> in it.</summary>
        private void PaintStreet()
        {
            if (!streetCaptured) return;

            RenderSettings.fog = streetFogEnabled;
            RenderSettings.fogColor = streetFogColor;
            RenderSettings.fogStartDistance = streetFogNear;
            RenderSettings.fogEndDistance = streetFogFar;
            RenderSettings.ambientLight = streetAmbientColor;
            RenderSettings.ambientIntensity = streetAmbientIntensity;
            RenderSettings.ambientSkyColor = streetAmbientSky;
            RenderSettings.ambientEquatorColor = streetAmbientEquator;
            RenderSettings.ambientGroundColor = streetAmbientGround;
        }

        private void Enter()
        {
            DayNightCycle.SuspendedForInterior = true;
            PaintInterior();

            inside = true;
            SetLampShadows(true);
            Teleport(spawnPoint, spawnYaw);
        }

        private void Leave()
        {
            DayNightCycle.SuspendedForInterior = false;
            if (DayNightCycle.Instance != null) DayNightCycle.Instance.ReassertNow();
            else PaintStreet();

            inside = false;
            SetLampShadows(false);
            // Back out onto the pavement facing away from the storefront, as the web build does.
            Teleport(new Vector3(streetDoor.x, streetY, streetDoor.z), 90f);
        }

        /// <summary>
        /// Moves the player and snaps the camera behind them.
        ///
        /// The CharacterController has to be switched off across the write: it caches its own
        /// position and will happily sweep the capsule from the pizzeria back to the city if it is
        /// left enabled, which reads as the player being dragged through every building on the way.
        /// The camera snap is the same reason the web build has one - without it the boom lerps
        /// across a kilometre of city while the player stands in a lit room.
        /// </summary>
        private void Teleport(Vector3 position, float yawDegrees)
        {
            // The capsule dance itself now lives on PlayerController, because U19's bust does the
            // same move and a second copy is how one of them loses the guard.
            player.Teleport(position, yawDegrees);
            if (followCamera != null) followCamera.SnapToTarget();

            // U25's fade, owed since U13 shipped this teleport as a hard cut. Raised in the SAME
            // frame as the move, so the first frame of the destination is already black - see
            // ScreenFade for why it covers instead of bracketing.
            if (fade == null) fade = FindAnyObjectByType<UI.Menus.ScreenFade>();
            if (fade != null) fade.Cover();
        }

        private UI.Menus.ScreenFade fade;

        /// <summary>Distance on the ground plane - a doorway does not care how high you are standing.</summary>
        private static bool WithinXZ(Vector3 a, Vector3 b, float radius)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return dx * dx + dz * dz <= radius * radius;
        }

        /// <summary>Draws the two circles in the Scene view - they are invisible otherwise.</summary>
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
