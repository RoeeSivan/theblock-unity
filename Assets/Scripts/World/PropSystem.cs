using System.Collections.Generic;
using TheBlock.Game;
using TheBlock.Traffic;
using UnityEngine;

namespace TheBlock.World
{
    /// <summary>
    /// U35h - the street props: where they go, whether they exist, and when they go back.
    ///
    /// <b>The layout is baked into this component, not into the scene as objects and not into an
    /// asset.</b> <c>WorldBuilder.Props</c> writes ~95 <see cref="Entry"/> rows here (the
    /// <c>TrafficLightSystem.controllers</c> pattern - a serialized list on the scene component), and
    /// the three prefabs are instantiated from it at <c>Start</c> if the switch is on. So Off means
    /// zero objects exist, On/Off flips at runtime, and a rebuild regenerates one host object rather
    /// than 95 scene entries. The traffic-light poles are not in the list: they already stand where
    /// <c>WorldBuilder.Traffic</c> put them, and this system only hands each one a
    /// <see cref="BreakablePole"/> at runtime.
    ///
    /// <b>The tick puts the city back.</b> Twice a second it walks every breakable: one that has come
    /// to rest and is either far away or out of the camera's frustum, with its home spot free, is
    /// restored - a prop teleports back asleep, a pole stands up and lights again. That is the GTA
    /// answer to a world that would otherwise wear every fender-bender for the rest of the session,
    /// and it is also the perf guard: nothing that has been knocked over is ever more than a few
    /// minutes from being static again. An awake cap sleeps the oldest movers if a pile-up ever gets
    /// large.
    ///
    /// The frustum test is <c>GeometryUtility.TestPlanesAABB</c> against <c>Camera.main</c>, NOT
    /// <c>Renderer.isVisible</c> - the map camera renders the world too and would keep every prop
    /// "visible" while the map is up.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PropSystem : MonoBehaviour
    {
        /// <summary>One placed prop. World position (the base) and yaw in degrees.</summary>
        [System.Serializable]
        public struct Entry
        {
            public PropKind Kind;
            public Vector3 Position;
            public float Yaw;
        }

        [Tooltip("Baked by The Block → Build Props. Do not edit by hand; re-run the build.")]
        [SerializeField] private List<Entry> entries = new();

        [Header("Prefabs (built by The Block → Build Props)")]
        [SerializeField] private GameObject conePrefab;
        [SerializeField] private GameObject binPrefab;
        [SerializeField] private GameObject benchPrefab;

        [Header("Putting things back")]
        [Tooltip("Seconds between passes over the breakables.")]
        [SerializeField] private float tickSeconds = 0.5f;

        [Tooltip("Seconds a knocked-over thing must lie still before it may be restored.")]
        [SerializeField] private float settleSeconds = 6f;

        [Tooltip("Beyond this many metres from the camera a settled thing is restored regardless of view.")]
        [SerializeField] private float farDistance = 120f;

        [Tooltip("Inside this many metres nothing is restored even out of view - it could be heard, or walked back to in a step.")]
        [SerializeField] private float nearFloor = 15f;

        [Tooltip("At most this many props awake at once; the oldest movers beyond it are put to sleep.")]
        [SerializeField] private int maxAwake = 16;

        [Tooltip("Anything below this Y has fallen out of the world and is restored on the spot.")]
        [SerializeField] private float killY = -20f;

        public static PropSystem Instance { get; private set; }

        /// <summary>The rows the last build baked. Read by the builder's report and the probe.</summary>
        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>Is the switch currently applied - are the props in the world?</summary>
        public bool Enabled { get; private set; }

        private readonly List<GameObject> _spawned = new();
        private readonly List<StreetProp> _awake = new();
        private readonly Plane[] _planes = new Plane[6];
        private float _nextTick;
        private bool _polesEnsured;

        /// <summary>Editor: the builder writes the layout and the prefabs.</summary>
        public void Configure(List<Entry> layout, GameObject cone, GameObject bin, GameObject bench)
        {
            entries = layout ?? new List<Entry>();
            conePrefab = cone;
            binPrefab = bin;
            benchPrefab = bench;
        }

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start() => SetEnabled(Progress.BreakablePropsOn);

        /// <summary>
        /// Applies the switch. On spawns the layout and arms the poles; Off restores everything
        /// where it stands and despawns the props, so the world is exactly what it was before U35h.
        /// </summary>
        public void SetEnabled(bool on)
        {
            if (on == Enabled) return;

            if (on)
            {
                if (Breakable.PropsLayer < 0)
                {
                    // Without the layer a cone would be a crash, a crime and a dent. Refuse loudly.
                    Debug.LogError(
                        $"PropSystem: no '{Breakable.PropsLayerName}' layer in this project - run " +
                        "The Block → Build Props (or Build World) once, then re-enable Street Props.");
                    return;
                }

                int pedestrian = LayerMask.NameToLayer("Pedestrian");
                if (pedestrian >= 0) Physics.IgnoreLayerCollision(Breakable.PropsLayer, pedestrian, true);

                Spawn();
                EnsurePoles();
                Enabled = true;
                return;
            }

            // Off: poles up and lit, props gone.
            for (int i = Breakable.All.Count - 1; i >= 0; i--)
            {
                var b = Breakable.All[i];
                if (b is BreakablePole pole && pole.Disturbed) pole.Restore();
            }
            foreach (var go in _spawned) if (go != null) Destroy(go);
            _spawned.Clear();
            Enabled = false;
        }

        private void Spawn()
        {
            foreach (var entry in entries)
            {
                var prefab = PrefabFor(entry.Kind);
                if (prefab == null) continue;

                var go = Instantiate(prefab, entry.Position, Quaternion.Euler(0f, entry.Yaw, 0f), transform);
                go.name = $"{entry.Kind}_{_spawned.Count:000}";
                _spawned.Add(go);
            }
        }

        private GameObject PrefabFor(PropKind kind) => kind switch
        {
            PropKind.Cone => conePrefab,
            PropKind.Bin => binPrefab,
            PropKind.Bench => benchPrefab,
            _ => null,
        };

        /// <summary>
        /// Every pole in the scene gets its <see cref="BreakablePole"/> once. Idempotent, and
        /// <c>TryGetComponent</c> rather than <c>?? AddComponent</c> - the fake-null trap.
        /// </summary>
        private void EnsurePoles()
        {
            if (_polesEnsured) return;
            _polesEnsured = true;

            foreach (var pole in FindObjectsByType<TrafficLightPole>(FindObjectsSortMode.None))
            {
                if (pole.TryGetComponent<BreakablePole>(out _)) continue;
                pole.gameObject.AddComponent<BreakablePole>();
            }
        }

        // --- the tick ---------------------------------------------------------------------------

        private void Update()
        {
            if (!Enabled) return;
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + tickSeconds;

            var camera = Camera.main;
            var eye = camera != null ? camera.transform.position : transform.position;
            bool havePlanes = camera != null;
            if (havePlanes) GeometryUtility.CalculateFrustumPlanes(camera, _planes);

            _awake.Clear();
            var all = Breakable.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var b = all[i];
                if (b == null) continue;

                // A prop awake by any route - a nudge, a blast, a rolling neighbour - is disturbed.
                if (b is StreetProp prop && prop.Body != null && !prop.Body.IsSleeping())
                {
                    prop.NoteAwake();
                    _awake.Add(prop);
                }

                if (!b.Disturbed) continue;

                if (b.IsSettled)
                {
                    if (b.SettledSince < 0f) b.SettledSince = Time.time;
                }
                else
                {
                    b.SettledSince = -1f;
                }

                bool fell = b.transform.position.y < killY;
                if (!fell)
                {
                    if (b.SettledSince < 0f || Time.time - b.SettledSince < settleSeconds) continue;

                    float distance = Vector3.Distance(b.transform.position, eye);
                    bool far = distance > farDistance;
                    bool unseen = distance > nearFloor && havePlanes && !GeometryUtility.TestPlanesAABB(_planes, b.WorldBounds);
                    if (!far && !unseen) continue;
                }

                if (!b.HomeIsClear()) continue;
                b.Restore();
                b.SettledSince = -1f;
            }

            CapAwake();
        }

        /// <summary>
        /// A pile-up bigger than <see cref="maxAwake"/> puts its oldest movers to sleep. They stay
        /// where they are (asleep is not restored) and the tick restores them later like any other.
        /// </summary>
        private void CapAwake()
        {
            if (_awake.Count <= maxAwake) return;
            _awake.Sort((a, b) => a.DisturbedAt.CompareTo(b.DisturbedAt));
            int excess = _awake.Count - maxAwake;
            for (int i = 0; i < _awake.Count && excess > 0; i++)
            {
                var prop = _awake[i];
                if (Time.time - prop.DisturbedAt < 3f) break;   // still fresh; the rest are newer still
                prop.Body.linearVelocity = Vector3.zero;
                prop.Body.angularVelocity = Vector3.zero;
                prop.Body.Sleep();
                excess--;
            }
        }
    }
}
