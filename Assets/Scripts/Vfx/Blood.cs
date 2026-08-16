using UnityEngine;

namespace TheBlock.Vfx
{
    /// <summary>
    /// Blood for the run-over: a spray at the moment of impact, then a stain that spreads where the
    /// body comes to rest. The port of the original's <c>vfx/blood.ts</c> + <c>blood-textures.ts</c>.
    ///
    /// <b>The stain is what sells it.</b> The spray is over in well under a second while the pool is
    /// on screen for the whole time the body lies there - which is why the stain gets a pool, a
    /// caller-supplied lifetime and an ease-out spread, and the spray gets one particle system.
    ///
    /// <b>Both textures are drawn here rather than authored.</b> Two small procedural textures cost
    /// nothing to generate, keep the shape tunable in code, and stay out of LFS - the same call the
    /// web build made with its canvas. They are drawn WHITE and tinted by the material, so
    /// <see cref="color"/> is the only place the colour lives. Seeded, so the splat is the same shape
    /// every session; the variety comes from a random yaw per stamp, not from a new texture.
    ///
    /// <b>The stain is a lifted quad, not a URP Decal Projector.</b> A decal would conform to the
    /// road properly, and it costs a Decal renderer feature plus a depth prepass on a frame that is
    /// already the thing the user has flagged. A pavement is flat where people walk, so two
    /// centimetres of lift look the same for free - the web reached the same answer against
    /// <c>DecalGeometry</c> for a different reason (it walks every triangle of an 11M-tri city).
    /// </summary>
    public class Blood : MonoBehaviour
    {
        [Header("Spray - one particle system, emitted by hand")]
        [Tooltip("Droplets per impact. dropsPerBurst.")]
        [SerializeField] private int dropsPerBurst = 55;

        [Tooltip("Droplet size in metres. dropSize.")]
        [SerializeField] private float dropSize = 0.05f;

        [Tooltip("Metres per second along the throw direction. burstSpeed.")]
        [SerializeField] private float burstSpeed = 3.5f;

        [Tooltip("Initial lift, so the spray arcs instead of skidding. burstUp.")]
        [SerializeField] private float burstUp = 2.6f;

        [Tooltip("Sideways randomness across the throw, 0..1. burstSpread.")]
        [SerializeField] private float burstSpread = 0.55f;

        [Tooltip("Seconds until a droplet is gone. dropLife.")]
        [SerializeField] private float dropLife = 0.7f;

        [Header("Ground stain - one quad per pool")]
        [Tooltip("Concurrent stains; the oldest is recycled. maxPools.")]
        [SerializeField] private int maxPools = 8;

        [Tooltip("Final radius in metres. poolRadius.")]
        [SerializeField] private float poolRadius = 0.62f;

        [Tooltip("Seconds to spread to full size. poolGrow.")]
        [SerializeField] private float poolGrow = 1.1f;

        [Tooltip("Seconds at full opacity before fading, when no lifetime is supplied. poolHold.")]
        [SerializeField] private float poolHold = 1.8f;

        [Tooltip("Seconds to fade out. poolFade.")]
        [SerializeField] private float poolFade = 1.2f;

        [Tooltip("Peak opacity. poolOpacity.")]
        [SerializeField] private float poolOpacity = 0.85f;

        [Tooltip("Metres above the ground, to stay out of z-fighting with the road. groundLift.")]
        [SerializeField] private float groundLift = 0.02f;

        [Header("Colour")]
        [Tooltip("Blood is DARK - on asphalt it reads near-black maroon, and a bright red renders " +
                 "as paint. Authored darker than it looks in the swatch. bloodConfig.color 0x4E0A0A.")]
        [SerializeField] private Color color = new(0x4E / 255f, 0x0A / 255f, 0x0A / 255f, 1f);

        [Tooltip("Off kills every effect this system draws, which is the instrument for judging " +
                 "whether the blood is what costs the frame. bloodConfig.enabled.")]
        [SerializeField] private bool enable = true;

        // --- runtime ---------------------------------------------------------------------------

        /// <summary>The one child every effect object lives under, so the sweep can be exact.</summary>
        private const string RootName = "Blood FX";

        private Transform _root;
        private ParticleSystem _spray;
        private ParticleSystem.EmitParams _emit;
        private Stain[] _stains;
        private int _builtOnFrame = -1;

        /// <summary>Everything allocated on the GPU, so a rebuild can free what it replaces.</summary>
        private readonly System.Collections.Generic.List<Texture2D> _textures = new();
        private readonly System.Collections.Generic.List<Material> _materials = new();
        private int _nextStain;
        private MaterialPropertyBlock _block;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private sealed class Stain
        {
            public Transform Transform;
            public MeshRenderer Renderer;
            public float T = float.PositiveInfinity;
            public float Life;
        }

        /// <summary>The shortest a stain can be told to live: it must still spread and still fade.</summary>
        private float MinimumLife => poolGrow + poolFade;

        private void Awake() => Build();

        /// <summary>
        /// Rebuilt from Update as well as Awake, for the same reason the crowd rebinds: a mid-Play
        /// recompile reloads the domain, non-serialized fields come back null and Awake never runs
        /// again (memory: <c>recompile-during-play-nulls-fields</c>).
        ///
        /// <b>It destroys the textures and materials it replaces, and it refuses to run twice in a
        /// frame.</b> Both matter more than they look. This is the only code in the project that
        /// allocates a Texture2D at runtime, and a rebuild path that leaks them is a rebuild path
        /// that can eat texture memory without ever logging anything - in a scene whose textures are
        /// already 3.2 GB, and where the visible symptom of running out is garbage sampled over the
        /// whole world rather than an error. `GameObject.Destroy` does not touch a material's or a
        /// texture's memory just because the renderer using it went away.
        /// </summary>
        private void Build()
        {
            if (_builtOnFrame == Time.frameCount && _root != null) return;
            _builtOnFrame = Time.frameCount;

            _block = new MaterialPropertyBlock();
            ReleaseGpuResources();

            // Everything this system owns hangs off ONE child, and only that child is swept. Reaching
            // wider - "destroy every child of this object" - is exactly how the crowd on this same
            // GameObject deleted the stain pool out from under a running reaction.
            var existing = transform.Find(RootName);
            if (existing != null) Destroy(existing.gameObject);

            _root = new GameObject(RootName).transform;
            _root.SetParent(transform, false);

            BuildSpray();
            BuildStains();
        }

        /// <summary>Everything this component allocated on the GPU, freed explicitly.</summary>
        private void ReleaseGpuResources()
        {
            foreach (var texture in _textures) if (texture != null) Destroy(texture);
            foreach (var material in _materials) if (material != null) Destroy(material);
            _textures.Clear();
            _materials.Clear();
        }

        private void OnDestroy() => ReleaseGpuResources();

        private void BuildSpray()
        {
            var host = new GameObject("Blood Spray");
            host.transform.SetParent(_root, false);

            _spray = host.AddComponent<ParticleSystem>();

            // A ParticleSystem added at runtime starts playing immediately, and several of the
            // settings below cannot be written to a playing system - duration asserts outright.
            _spray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _spray.main;
            main.loop = true;                 // never emits on its own - this only keeps it simulating
            main.duration = 1f;
            main.playOnAwake = true;
            main.startLifetime = dropLife;
            main.startSize = dropSize;
            main.startSpeed = 0f;             // every droplet's velocity is supplied by Splash
            main.gravityModifier = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(64, dropsPerBurst * 6);
            main.startColor = color;

            var emission = _spray.emission;
            emission.enabled = false;

            var shape = _spray.shape;
            shape.enabled = false;

            // Hold full opacity in the air, then fade over the last third - the web's `left * 3`.
            var overLifetime = _spray.colorOverLifetime;
            overLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.67f),
                    new GradientAlphaKey(0f, 1f),
                });
            overLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = BuildMaterial(
                "Universal Render Pipeline/Particles/Unlit", DropTexture(), "Blood Drop");
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _spray.Play();
        }

        private void BuildStains()
        {
            var mesh = QuadMesh();
            var material = BuildMaterial("Universal Render Pipeline/Unlit", SplatTexture(), "Blood Stain");

            _stains = new Stain[Mathf.Max(1, maxPools)];
            for (int i = 0; i < _stains.Length; i++)
            {
                var host = new GameObject($"Blood Stain {i:00}");
                host.transform.SetParent(_root, false);
                host.SetActive(false);

                host.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = host.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                _stains[i] = new Stain { Transform = host.transform, Renderer = renderer };
            }
        }

        // --- what a run-over asks for -----------------------------------------------------------

        /// <summary>Spray from an impact at <paramref name="at"/>, thrown along <paramref name="dir"/>.</summary>
        public void Splash(Vector3 at, Vector3 dir)
        {
            if (!enable || _spray == null) return;

            var forward = new Vector3(dir.x, 0f, dir.z);
            forward = forward.sqrMagnitude < 1e-4f ? Vector3.forward : forward.normalized;
            var side = Vector3.Cross(Vector3.up, forward);

            for (int i = 0; i < dropsPerBurst; i++)
            {
                // Start inside the body rather than at one point - a pinpoint origin reads as a
                // firework. 0.7 m up is chest height on a 1.7 m character.
                _emit.position = at + new Vector3(
                    Random.Range(-0.125f, 0.125f),
                    0.7f + Random.Range(-0.25f, 0.25f),
                    Random.Range(-0.125f, 0.125f));

                float speed = burstSpeed * (0.35f + Random.value);
                float spread = (Random.value - 0.5f) * 2f * burstSpread;
                _emit.velocity = (forward + side * spread) * speed + Vector3.up * (burstUp * (0.3f + Random.value));
                _emit.startLifetime = dropLife;
                _emit.startSize = dropSize;
                _emit.startColor = color;

                _spray.Emit(_emit, 1);
            }
        }

        /// <summary>
        /// Start a stain spreading on the ground.
        ///
        /// <paramref name="life"/> is the TOTAL seconds it may exist, and the caller passes it
        /// because the stain must not outlive the body: a victim gets back up and walks away, so a
        /// stain still on the road then is somebody strolling past their own blood. A short budget
        /// squeezes the HOLD and never the spread or the fade - those two are what the effect reads
        /// as, so "less time" has to mean "linger less", not "snap".
        /// </summary>
        public void Pool(Vector3 at, float groundY, float life = 0f)
        {
            if (!enable || _stains == null || _stains.Length == 0) return;

            var stain = _stains[_nextStain];
            _nextStain = (_nextStain + 1) % _stains.Length;
            if (stain.Transform == null) return;

            stain.Transform.SetPositionAndRotation(
                new Vector3(at.x, groundY + groundLift, at.z),
                Quaternion.Euler(90f, Random.value * 360f, 0f));   // never stamp the same shape twice
            stain.Transform.localScale = Vector3.one * 0.001f;
            stain.T = 0f;
            stain.Life = Mathf.Max(life <= 0f ? poolGrow + poolHold + poolFade : life, MinimumLife);
            stain.Transform.gameObject.SetActive(true);
        }

        // --- the frame ---------------------------------------------------------------------------

        private void Update()
        {
            if (_stains == null || _root == null) { Build(); return; }

            float dt = Time.deltaTime;

            foreach (var stain in _stains)
            {
                if (stain.T >= stain.Life || stain.Transform == null) continue;

                stain.T += dt;
                if (stain.T >= stain.Life)
                {
                    stain.Transform.gameObject.SetActive(false);
                    continue;
                }

                // 1-(1-k)³ is an ease-out: spread fast, then creep. That is how a pool actually grows.
                float k = Mathf.Clamp01(stain.T / poolGrow);
                stain.Transform.localScale = Vector3.one * (poolRadius * 2f * (1f - (1f - k) * (1f - k) * (1f - k)));

                // Anchored to the END of this stain's own budget, not to the configured hold - that
                // is what makes a shortened stain finish exactly when it was told to.
                float fadeStart = stain.Life - poolFade;
                float alpha = stain.T <= fadeStart
                    ? poolOpacity
                    : poolOpacity * (1f - (stain.T - fadeStart) / poolFade);

                stain.Renderer.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, new Color(color.r, color.g, color.b, alpha));
                stain.Renderer.SetPropertyBlock(_block);
            }
        }

        // --- the two textures, drawn rather than authored ----------------------------------------

        /// <summary>Soft round droplet with a real edge - a droplet is not smoke.</summary>
        private Texture2D DropTexture()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "Blood Drop",
                // Clamp, not the Repeat default: the quad's UVs sit on the texture edge, and a
                // repeating alpha wraps the soft rim back onto the opposite side as a hard seam.
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[size * size];
            const float mid = size / 2f;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = Mathf.Sqrt((x + 0.5f - mid) * (x + 0.5f - mid) + (y + 0.5f - mid) * (y + 0.5f - mid)) / mid;
                float a = r >= 1f ? 0f : r <= 0.55f ? Mathf.Lerp(1f, 0.95f, r / 0.55f) : 0.95f * (1f - (r - 0.55f) / 0.45f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            _textures.Add(texture);
            return texture;
        }

        /// <summary>
        /// The stain: overlapping off-centre lobes plus a scatter of satellites.
        ///
        /// The point is the IRREGULAR outline. A clean circle reads instantly as "a quad with a
        /// texture on it", which is the single thing most likely to make this look cheap.
        /// </summary>
        private Texture2D SplatTexture()
        {
            // 1024, up from 256 on 2026-08-16 after the first macOS Player was play-tested: the
            // stain is a GROUND decal, so it is the one texture here a player stands over and reads
            // at arm's length, and 256 px across a metre of pavement is visibly blocky at native
            // resolution. The Editor's smaller Game view had been hiding it. Every blob below is
            // expressed in units of `mid`, so the shape is resolution-independent - this is a
            // sharper print of the same splat, not a different one. Cost: 4 MB + mips, once.
            const int size = 1024;
            const float mid = size / 2f;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "Blood Splat",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var alpha = new float[size * size];

            // Seeded and restored, so the splat is identical every session without disturbing the
            // gameplay streams that share UnityEngine.Random.
            var state = Random.state;
            Random.InitState(20260818);

            for (int i = 0; i < 6; i++)
            {
                float angle = i / 6f * Mathf.PI * 2f + Random.value * 0.8f;
                float distance = mid * 0.16f * Random.value;
                float radius = mid * (0.44f + Random.value * 0.3f);
                Blob(alpha, size,
                    mid + Mathf.Cos(angle) * distance, mid + Mathf.Sin(angle) * distance,
                    radius, radius * (0.7f + Random.value * 0.5f), angle, 1f);
            }

            for (int i = 0; i < 14; i++)
            {
                float angle = Random.value * Mathf.PI * 2f;
                float distance = mid * (0.55f + Random.value * 0.4f);
                float radius = mid * (0.03f + Random.value * 0.09f);
                Blob(alpha, size,
                    mid + Mathf.Cos(angle) * distance, mid + Mathf.Sin(angle) * distance,
                    radius, radius * (0.5f + Random.value), angle, 0.55f + Random.value * 0.45f);
            }

            Random.state = state;

            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha[i]) * 255f));

            texture.SetPixels32(pixels);
            texture.Apply();
            _textures.Add(texture);
            return texture;
        }

        /// <summary>One squashed, rotated blob with a soft edge, accumulated into the alpha buffer.</summary>
        private static void Blob(
            float[] alpha, int size, float cx, float cy, float rx, float ry, float rotation, float peak)
        {
            float cos = Mathf.Cos(-rotation);
            float sin = Mathf.Sin(-rotation);
            float reach = Mathf.Max(rx, ry) + 1f;

            int minX = Mathf.Max(0, Mathf.FloorToInt(cx - reach));
            int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(cx + reach));
            int minY = Mathf.Max(0, Mathf.FloorToInt(cy - reach));
            int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(cy + reach));

            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x + 0.5f - cx;
                float dy = y + 0.5f - cy;
                float lx = (dx * cos - dy * sin) / rx;
                float ly = (dx * sin + dy * cos) / ry;
                float r = Mathf.Sqrt(lx * lx + ly * ly);
                if (r >= 1f) continue;

                // Solid core to 0.55, a shoulder to 0.8, then out to nothing - the web's gradient.
                float a = r <= 0.55f ? peak : r <= 0.8f ? Mathf.Lerp(peak, peak * 0.9f, (r - 0.55f) / 0.25f)
                    : peak * 0.9f * (1f - (r - 0.8f) / 0.2f);

                int index = y * size + x;
                if (a > alpha[index]) alpha[index] = a;
            }
        }

        // --- the two materials -------------------------------------------------------------------

        /// <summary>
        /// A transparent, unlit, tinted material built at runtime.
        ///
        /// Runtime rather than an asset a builder writes, unlike every material in <c>WorldBuilder</c>:
        /// these two have no authored content at all - their textures are generated in this file - so
        /// an asset would be a checked-in copy of code that already exists. The keyword and blend
        /// dance is what URP's shader GUI does when you flip Surface Type to Transparent; setting the
        /// float alone changes nothing, because the variant is chosen by the keyword.
        /// </summary>
        private Material BuildMaterial(string shaderName, Texture2D texture, string name)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"Blood: no shader '{shaderName}'. Falling back to URP/Unlit.");
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            var material = new Material(shader) { name = name };
            _materials.Add(material);
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Surface", 1f);   // transparent
            material.SetFloat("_Blend", 0f);     // alpha
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return material;
        }

        /// <summary>
        /// A 1×1 quad in the XY plane, so a 90° X rotation lays it flat on the road.
        ///
        /// Wound BOTH ways on purpose. It is unlit and it is two triangles, so the second pair costs
        /// nothing measurable - and it removes the one way this can fail silently: a stain that is
        /// perfectly correct but back-face culled looks exactly like a stain that never spawned.
        /// </summary>
        private static Mesh QuadMesh()
        {
            var mesh = new Mesh { name = "Blood Quad" };
            mesh.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            });
            mesh.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            mesh.SetNormals(new[] { -Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
