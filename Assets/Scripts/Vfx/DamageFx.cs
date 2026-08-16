using System.Collections.Generic;
using UnityEngine;

namespace TheBlock.Vfx
{
    /// <summary>How badly a car is burning. The two looks <see cref="DamageFx"/> can attach.</summary>
    public enum BurnLevel
    {
        /// <summary>Half health: grey engine smoke off the bonnet.</summary>
        Smoke,

        /// <summary>A fifth left: flame under the smoke, and it is about to be over.</summary>
        Fire,
    }

    /// <summary>
    /// U35b's smoke, fire and explosion - a small fixed pool of particle systems shared by every
    /// damaged vehicle in the world.
    ///
    /// <b>Built at runtime with generated textures, exactly like <see cref="Blood"/>.</b> That file
    /// makes the argument in full and every line of it applies here: two small procedural textures
    /// cost nothing, keep the shape tunable in code, stay out of LFS, and avoid a second set of
    /// authored assets that <c>WorldBuilder</c>'s compression pass would never see. The one thing
    /// this adds is that a puff and a flame are the SAME texture at two tints, so there is one to
    /// generate rather than three.
    ///
    /// <b>Three emitter slots, and the oldest is stolen.</b> The U35b row's budget is "one particle
    /// system per damaged car, at most 3 live". A cap that refuses the fourth car would leave the car
    /// you are looking at clean while an unseen one three streets away smokes; stealing the oldest
    /// slot spends the budget on the newest damage, which is the same argument
    /// <c>RagdollBudget</c> makes for freezing the oldest body.
    ///
    /// <b>The hosts are never parented to a car.</b> They hang off this object for their whole life
    /// and copy their anchor's position each frame. Parenting would be one less line per frame and
    /// would destroy a pooled emitter the moment the car it was following despawned - which is how a
    /// pool quietly becomes two slots, then one, with nothing logged.
    ///
    /// <b>Lazily created, and it heals itself.</b> There is no scene object to place and no builder to
    /// remember to run: the first caller makes one. The static is cleared by <c>SessionReset</c>
    /// because a scene reload leaves a destroyed object behind that still reads as non-null
    /// (memory: <c>unity-null-coalescing-fake-null</c>).
    /// </summary>
    public class DamageFx : MonoBehaviour
    {
        // --- smoke ---------------------------------------------------------------------------------

        [Tooltip("Puffs per second from a smoking car.")]
        [SerializeField] private float smokeRate = 14f;

        [Tooltip("Seconds a puff lives. Long enough to leave a trail behind a moving car.")]
        [SerializeField] private float smokeLife = 1.7f;

        [Tooltip("Metres across when it leaves the bonnet; it grows to three times this.")]
        [SerializeField] private float smokeSize = 0.45f;

        [Tooltip("Upward speed in m/s. Engine smoke drifts, it does not jet.")]
        [SerializeField] private float smokeSpeed = 1.3f;

        [Tooltip("Smoke colour. Dark grey with low alpha - a white puff reads as steam.")]
        [SerializeField] private Color smokeColor = new(0.24f, 0.24f, 0.26f, 0.5f);

        // --- fire ----------------------------------------------------------------------------------

        [Tooltip("Flames per second from a burning car.")]
        [SerializeField] private float fireRate = 34f;

        [Tooltip("Seconds a flame lives. Short - a flame that lingers looks like a flare.")]
        [SerializeField] private float fireLife = 0.55f;

        [Tooltip("Metres across at birth; a flame SHRINKS as it rises.")]
        [SerializeField] private float fireSize = 0.5f;

        [Tooltip("Upward speed in m/s.")]
        [SerializeField] private float fireSpeed = 2.6f;

        [Tooltip("Flame colour, additive. Authored hot because additive blending pales it.")]
        [SerializeField] private Color fireColor = new(1f, 0.5f, 0.12f, 0.9f);

        // --- the blast -----------------------------------------------------------------------------

        [Tooltip("Fireball particles in one explosion.")]
        [SerializeField] private int blastFlames = 48;

        [Tooltip("Smoke particles in one explosion, thrown slower and living longer.")]
        [SerializeField] private int blastSmoke = 26;

        [Tooltip("Metres per second the fireball expands.")]
        [SerializeField] private float blastSpeed = 9f;

        [Tooltip("Peak intensity of the one-shot point light.")]
        [SerializeField] private float flashIntensity = 900f;

        [Tooltip("Metres the flash reaches. A point light, not a second directional - URP has room " +
                 "for exactly one main directional and this is not competing for it.")]
        [SerializeField] private float flashRange = 22f;

        [Tooltip("Seconds the flash takes to fall to nothing.")]
        [SerializeField] private float flashFade = 0.3f;

        // --- the pool ------------------------------------------------------------------------------

        /// <summary>Everything this system owns lives under one child, so the sweep can be exact.</summary>
        private const string RootName = "Damage FX";

        /// <summary>The U35b row's budget: at most three cars smoking at once.</summary>
        private const int Slots = 3;

        private static DamageFx _instance;

        /// <summary>
        /// The one in the scene, made on demand.
        ///
        /// <c>==</c> rather than <c>??</c> throughout: a destroyed object is not null to the
        /// coalescing operator, so after a scene reload the lazy getter would hand back a corpse
        /// (memory: <c>unity-null-coalescing-fake-null</c>).
        /// </summary>
        public static DamageFx Instance
        {
            get
            {
                if (_instance != null) return _instance;

                _instance = FindAnyObjectByType<DamageFx>();
                if (_instance != null) return _instance;

                var host = new GameObject("Damage FX");
                _instance = host.AddComponent<DamageFx>();
                return _instance;
            }
        }

        /// <summary>
        /// The one in the scene, or null - <b>never a new one</b>.
        ///
        /// <see cref="Instance"/> cannot be used from an <c>OnDestroy</c>: creating a GameObject while
        /// the scene is being torn down is an error, and "detach my plume on the way out" is exactly
        /// the call that arrives at that moment.
        /// </summary>
        public static DamageFx Existing => _instance;

        /// <summary>Called by <c>SessionReset</c>: the cached instance belongs to the old scene.</summary>
        public static void ResetCaches() => _instance = null;

        private sealed class Slot
        {
            public Transform Host;
            public ParticleSystem Smoke;
            public ParticleSystem Fire;

            /// <summary>Who asked for it. Compared by reference, so two cars cannot share a slot.</summary>
            public UnityEngine.Object Owner;

            public Transform Anchor;
            public Vector3 LocalOffset;

            /// <summary>Seconds left, or negative for "until the owner lets go".</summary>
            public float Remaining = -1f;
        }

        private Transform _root;
        private Slot[] _slots;
        private ParticleSystem _blastFire;
        private ParticleSystem _blastSmoke;
        private ParticleSystem.EmitParams _emit;
        private Light _flash;
        private float _flashT = float.PositiveInfinity;
        private int _nextSlot;
        private int _builtOnFrame = -1;

        private readonly List<Texture2D> _textures = new();
        private readonly List<Material> _materials = new();

        private void Awake() => Build();

        private void OnDestroy()
        {
            ReleaseGpuResources();
            if (_instance == this) _instance = null;
        }

        /// <summary>
        /// Rebuilt from <c>Update</c> as well as <c>Awake</c>, for the reason <see cref="Blood"/> is:
        /// a mid-Play recompile reloads the domain, non-serialized fields come back null and
        /// <c>Awake</c> never runs again (memory: <c>recompile-during-play-nulls-fields</c>). It
        /// destroys what it replaces and refuses to run twice in one frame - a rebuild path that
        /// leaks textures is a rebuild path that can eat memory with nothing logged.
        /// </summary>
        private void Build()
        {
            if (_builtOnFrame == Time.frameCount && _root != null) return;
            _builtOnFrame = Time.frameCount;

            ReleaseGpuResources();

            var existing = transform.Find(RootName);
            if (existing != null) Destroy(existing.gameObject);

            _root = new GameObject(RootName).transform;
            _root.SetParent(transform, false);

            var puff = PuffTexture();
            var smokeMaterial = BuildMaterial(puff, "Damage Smoke", additive: false);
            var fireMaterial = BuildMaterial(puff, "Damage Fire", additive: true);

            _slots = new Slot[Slots];
            for (int i = 0; i < Slots; i++)
            {
                var host = new GameObject($"Burn {i}");
                host.transform.SetParent(_root, false);

                _slots[i] = new Slot
                {
                    Host = host.transform,
                    Smoke = BuildStream(host.transform, "Smoke", smokeMaterial, smokeColor, smokeRate,
                        smokeLife, smokeSize, smokeSpeed, grow: true),
                    Fire = BuildStream(host.transform, "Fire", fireMaterial, fireColor, fireRate,
                        fireLife, fireSize, fireSpeed, grow: false),
                };
            }

            _blastFire = BuildBurst(_root, "Blast Fire", fireMaterial, 0.9f);
            _blastSmoke = BuildBurst(_root, "Blast Smoke", smokeMaterial, 2.4f);

            var flashHost = new GameObject("Blast Flash");
            flashHost.transform.SetParent(_root, false);
            _flash = flashHost.AddComponent<Light>();
            _flash.type = LightType.Point;
            _flash.color = new Color(1f, 0.72f, 0.4f);
            _flash.range = flashRange;
            _flash.shadows = LightShadows.None;   // one flash, no shadow map - it lasts a third of a second
            _flash.enabled = false;
        }

        private void ReleaseGpuResources()
        {
            foreach (var texture in _textures) if (texture != null) Destroy(texture);
            foreach (var material in _materials) if (material != null) Destroy(material);
            _textures.Clear();
            _materials.Clear();
        }

        // --- what a damaged car asks for ------------------------------------------------------------

        /// <summary>
        /// Start (or update) the plume on one vehicle.
        ///
        /// Idempotent per owner: calling it every frame with the same level is free, which is what
        /// lets <c>VehicleDamage</c> simply state its condition rather than track transitions.
        /// </summary>
        /// <param name="owner">The component that owns this plume - its slot key.</param>
        /// <param name="anchor">Followed each frame. Usually the car's own transform.</param>
        /// <param name="localOffset">Where on it the smoke comes from, in the anchor's local frame.</param>
        /// <param name="level">Smoke, or smoke plus flame.</param>
        /// <param name="seconds">Positive for a timed plume (a traffic wreck); negative to burn until
        /// <see cref="Detach"/>.</param>
        public void Attach(UnityEngine.Object owner, Transform anchor, Vector3 localOffset, BurnLevel level, float seconds = -1f)
        {
            if (owner == null || anchor == null) return;
            if (_slots == null || _root == null) Build();
            if (_slots == null) return;

            var slot = Find(owner);
            if (slot == null)
            {
                // Steal the oldest. Round-robin IS oldest-first here, because a slot is only ever
                // taken at the front of this cursor.
                slot = _slots[_nextSlot];
                _nextSlot = (_nextSlot + 1) % _slots.Length;
                Release(slot);
                slot.Owner = owner;
            }

            slot.Anchor = anchor;
            slot.LocalOffset = localOffset;
            slot.Remaining = seconds;

            Play(slot.Smoke, true);
            Play(slot.Fire, level == BurnLevel.Fire);

            slot.Host.position = anchor.TransformPoint(localOffset);
        }

        /// <summary>Stop this owner's plume. Safe to call when it never had one.</summary>
        public void Detach(UnityEngine.Object owner)
        {
            if (_slots == null) return;
            var slot = Find(owner);
            if (slot != null) Release(slot);
        }

        /// <summary>
        /// One explosion at a point: a fireball, a slower smoke ball, and a single point light.
        ///
        /// Visual only - the impulse, the star and the sound belong to <c>VehicleDamage</c>, because
        /// those are gameplay and this file is not allowed to know about gameplay.
        /// </summary>
        public void Burst(Vector3 at)
        {
            if (_blastFire == null || _root == null) Build();
            if (_blastFire == null) return;

            Throw(_blastFire, at, blastFlames, blastSpeed, fireSize * 3.2f, 0.55f, fireColor);
            Throw(_blastSmoke, at, blastSmoke, blastSpeed * 0.45f, smokeSize * 4f, 2.2f,
                new Color(smokeColor.r, smokeColor.g, smokeColor.b, 0.75f));

            if (_flash == null) return;
            _flash.transform.position = at + Vector3.up * 0.6f;
            _flash.enabled = true;
            _flashT = 0f;
        }

        private void Throw(ParticleSystem system, Vector3 at, int count, float speed, float size,
            float life, Color color)
        {
            for (int i = 0; i < count; i++)
            {
                var direction = Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y) * 0.8f + 0.2f;   // a blast goes UP, not into the road

                _emit.position = at + Random.insideUnitSphere * 0.4f;
                _emit.velocity = direction * (speed * (0.4f + Random.value));
                _emit.startLifetime = life * (0.6f + Random.value * 0.6f);
                _emit.startSize = size * (0.6f + Random.value * 0.8f);
                _emit.startColor = color;
                system.Emit(_emit, 1);
            }
        }

        // --- the frame -------------------------------------------------------------------------------

        private void Update()
        {
            if (_slots == null || _root == null) { Build(); return; }

            float dt = Time.deltaTime;

            foreach (var slot in _slots)
            {
                if (slot.Owner == null && slot.Anchor == null) continue;

                // The owner or the car it was following was destroyed under us - a carjacked filler,
                // a despawned wreck, a scene change. Nothing logs that, so it is checked rather than
                // trusted.
                if (slot.Owner == null || slot.Anchor == null) { Release(slot); continue; }

                if (slot.Remaining > 0f)
                {
                    slot.Remaining -= dt;
                    if (slot.Remaining <= 0f) { Release(slot); continue; }
                }

                slot.Host.position = slot.Anchor.TransformPoint(slot.LocalOffset);
            }

            if (_flashT < flashFade)
            {
                _flashT += dt;
                float k = 1f - Mathf.Clamp01(_flashT / flashFade);
                _flash.intensity = flashIntensity * k * k;
                if (_flashT >= flashFade) _flash.enabled = false;
            }
        }

        // --- slots -----------------------------------------------------------------------------------

        private Slot Find(UnityEngine.Object owner)
        {
            foreach (var slot in _slots)
                if (slot.Owner == owner) return slot;
            return null;
        }

        private static void Release(Slot slot)
        {
            slot.Owner = null;
            slot.Anchor = null;
            slot.Remaining = -1f;
            Play(slot.Smoke, false);
            Play(slot.Fire, false);
        }

        /// <summary>
        /// Emission on or off, leaving the system itself simulating.
        ///
        /// <c>Stop</c> would clear the puffs already in the air, so a car that stops smoking would
        /// take its whole plume with it in one frame. Turning the EMISSION off lets the last puffs
        /// live out their lifetime, which is what stopping looks like.
        /// </summary>
        private static void Play(ParticleSystem system, bool on)
        {
            if (system == null) return;
            var emission = system.emission;
            emission.enabled = on;
        }

        // --- construction ------------------------------------------------------------------------------

        private ParticleSystem BuildStream(Transform parent, string name, Material material, Color color,
            float rate, float life, float size, float speed, bool grow)
        {
            var host = new GameObject(name);
            host.transform.SetParent(parent, false);

            var system = host.AddComponent<ParticleSystem>();

            // A ParticleSystem added at runtime starts playing immediately, and several of the
            // settings below cannot be written to a playing system - duration asserts outright.
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.loop = true;
            main.duration = 1f;
            main.playOnAwake = true;
            main.startLifetime = life;
            main.startSize = size;
            main.startSpeed = speed;
            main.startColor = color;
            main.gravityModifier = grow ? -0.04f : -0.12f;   // both rise; flame rises faster
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.CeilToInt(rate * life) + 16;

            var emission = system.emission;
            emission.enabled = false;                        // nothing burns until a car asks
            emission.rateOverTime = rate;

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 18f;
            shape.radius = 0.18f;
            shape.rotation = new Vector3(-90f, 0f, 0f);      // the cone points up, not down the +Z axis

            var sizeOverLifetime = system.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, grow
                ? AnimationCurve.EaseInOut(0f, 0.45f, 1f, 3f)     // smoke billows out as it cools
                : AnimationCurve.EaseInOut(0f, 1f, 1f, 0.15f));   // a flame narrows to nothing

            var colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(FadeOut(grow ? 0.35f : 0.15f));

            Dress(system, material);
            system.Play();
            return system;
        }

        private ParticleSystem BuildBurst(Transform parent, string name, Material material, float life)
        {
            var host = new GameObject(name);
            host.transform.SetParent(parent, false);

            var system = host.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.loop = true;
            main.duration = 1f;
            main.playOnAwake = true;
            main.startLifetime = life;
            main.startSpeed = 0f;                 // every particle's velocity is supplied by Throw
            main.startSize = 1f;
            main.gravityModifier = -0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 256;

            var emission = system.emission;
            emission.enabled = false;             // emitted by hand, like Blood's spray

            var shape = system.shape;
            shape.enabled = false;

            var sizeOverLifetime = system.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.6f));

            var colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(FadeOut(0.25f));

            Dress(system, material);
            system.Play();
            return system;
        }

        private static void Dress(ParticleSystem system, Material material)
        {
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>Hold, then fade over the tail. <paramref name="hold"/> is the fraction held.</summary>
        private static Gradient FadeOut(float hold)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, Mathf.Clamp01(hold)),
                    new GradientAlphaKey(0f, 1f),
                });
            return gradient;
        }

        // --- the one texture ------------------------------------------------------------------------

        /// <summary>
        /// A soft puff: opaque nowhere, thickest at a third of the radius, gone at the rim, with a
        /// little noise so a hundred of them do not read as a hundred copies of one circle.
        ///
        /// The same texture serves smoke and flame. What separates them is the tint, the blend mode
        /// and the size curve - which is also true of the real thing.
        /// </summary>
        private Texture2D PuffTexture()
        {
            const int size = 128;
            const float mid = size / 2f;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "Damage Puff",
                // Clamp, never Repeat: the quad's UVs sit on the texture edge and a repeating alpha
                // wraps the soft rim onto the opposite side as a hard seam - Blood's lesson.
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color32[size * size];
            var state = Random.state;
            Random.InitState(20260816);   // seeded: the same puff every session, like Blood's splat

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - mid) / mid;
                float dy = (y + 0.5f - mid) / mid;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = r >= 1f ? 0f : Mathf.Cos(r * Mathf.PI * 0.5f);
                a *= a;                                   // squared: a soft shoulder rather than a disc
                a *= 0.85f + Random.value * 0.15f;        // break up the perfect circle
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }

            Random.state = state;

            texture.SetPixels32(pixels);
            texture.Apply();
            _textures.Add(texture);
            return texture;
        }

        /// <summary>
        /// A transparent particle material built at runtime - <see cref="Blood"/>'s keyword dance,
        /// with an additive variant for the flame.
        ///
        /// Setting the blend floats alone is not enough: URP picks the variant off the KEYWORD, so a
        /// material with the right numbers and no <c>_SURFACE_TYPE_TRANSPARENT</c> renders opaque.
        /// </summary>
        private Material BuildMaterial(Texture2D texture, string name, bool additive)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogWarning("DamageFx: no URP particle shader. Falling back to URP/Unlit.");
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            var material = new Material(shader) { name = name };
            _materials.Add(material);

            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", additive ? 1f : 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)(additive
                ? UnityEngine.Rendering.BlendMode.One
                : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return material;
        }
    }
}
