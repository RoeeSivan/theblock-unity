using System.Collections.Generic;
using TheBlock.Core;
using TheBlock.Vfx;
using UnityEngine;

namespace TheBlock.Missions
{
    /// <summary>
    /// The reusable overhead waypoint pin - the port of <c>src/mission/marker.ts</c>. A downward
    /// cone over a tall thin translucent beam, bobbing and spinning so it reads from across the
    /// street, and popping when its objective is met.
    ///
    /// All three marker missions use it and differ only in colour: green drops, orange survivors,
    /// cyan gates. Unlit and additive, exactly as the web's <c>MeshBasicMaterial</c> is - a beacon
    /// must not be shaded by the sun or it goes dark at the wrong moment.
    ///
    /// <b>Can Unity do this better?</b> The web clones a material per beacon, so nine gates are nine
    /// materials and nine draw calls. Here the two meshes are built ONCE as statics and the
    /// materials are cached per colour, so every beacon of a mission shares one pair and the SRP
    /// batcher draws them together. A beacon only leaves that batch while it is mid-pop, because the
    /// pop fades an instance and nothing else - and at most a handful pop at a time.
    ///
    /// <b>The beam hangs DOWNWARD from the cone, which is what the original does</b>, so most of its
    /// 14 m is under the ground (or under the roof) and occluded. That is the shipped look and it is
    /// ported as-is; <see cref="beamUpward"/> is the one-click experiment if the pins ever read too
    /// weakly at a play-test.
    /// </summary>
    public class Beacon : MonoBehaviour
    {
        private static Mesh _coneMesh;
        private static Mesh _beamMesh;
        private static readonly Dictionary<int, Material> _materials = new();
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private bool beamUpward;

        private TheBlockConfig.BeaconSpec _spec;
        private Transform _cone;
        private Transform _beam;
        private Renderer _coneRenderer;
        private Renderer _beamRenderer;
        private MaterialPropertyBlock _block;

        private Vector3 _base; // spot + headHeight; the bob oscillates around this
        private Color _color;
        private float _t;
        private bool _popping;
        private float _popT;

        /// <summary>True once the pop has fully played out and the pin can be destroyed.</summary>
        public bool PopDone => _popping && _popT >= _spec.PopTime;

        /// <summary>Builds one pin. <paramref name="color"/> is the mission's, not the spec's.</summary>
        public static Beacon Create(Transform parent, TheBlockConfig.BeaconSpec spec, Color color, string label)
        {
            var go = new GameObject(label);
            go.transform.SetParent(parent, false);
            var beacon = go.AddComponent<Beacon>();
            beacon.Build(spec, color);
            return beacon;
        }

        private void Build(TheBlockConfig.BeaconSpec spec, Color color)
        {
            _spec = spec ?? new TheBlockConfig.BeaconSpec();
            _color = color;
            _block = new MaterialPropertyBlock();

            EnsureMeshes(_spec);
            var material = MaterialFor(color);

            // The cone's tip points at the target, so it is built apex-down and needs no rotation.
            _cone = NewPart("Cone", _coneMesh, material, out _coneRenderer);

            // The beam grows away from the cone. Its mesh is centred, so the transform carries the
            // half-height that puts one end at the cone.
            _beam = NewPart("Beam", _beamMesh, material, out _beamRenderer);
            var half = _spec.BeamHeight * 0.5f;
            _beam.localPosition = new Vector3(0f, beamUpward ? half : -half, 0f);

            ApplyAlpha(1f);
        }

        private Transform NewPart(string name, Mesh mesh, Material material, out Renderer renderer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            renderer = mr;
            return go.transform;
        }

        /// <summary>Drops the pin over a ground spot; it floats <c>headHeight</c> above it.</summary>
        public void SetGround(Vector3 groundPoint)
        {
            _base = groundPoint + Vector3.up * _spec.HeadHeight;
            transform.position = _base;
        }

        /// <summary>Starts the scale-up and fade. Returns the seconds until it should be removed.</summary>
        public float Pop()
        {
            if (_popping) return _spec.PopTime;
            _popping = true;
            _popT = 0f;
            return _spec.PopTime;
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            _t += dt;

            transform.rotation = Quaternion.Euler(0f, _t * _spec.SpinSpeed * Mathf.Rad2Deg, 0f);
            transform.position = new Vector3(
                _base.x,
                _base.y + Mathf.Sin(_t * _spec.BobSpeed) * _spec.BobAmplitude,
                _base.z);

            if (!_popping) return;

            _popT += dt;
            var k = Mathf.Clamp01(_spec.PopTime <= 0f ? 1f : _popT / _spec.PopTime);
            transform.localScale = Vector3.one * (1f + (_spec.PopScale - 1f) * k);
            ApplyAlpha(1f - k);
        }

        /// <summary>
        /// Fades both parts. The beam keeps its own <c>beamOpacity</c> as a ceiling, so the pop
        /// scales it down from 0.22 rather than up to 1.
        /// </summary>
        private void ApplyAlpha(float k)
        {
            SetPartColor(_coneRenderer, new Color(_color.r, _color.g, _color.b, k));
            SetPartColor(_beamRenderer,
                new Color(_color.r, _color.g, _color.b, _spec.BeamOpacity * k));
        }

        private void SetPartColor(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            renderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            renderer.SetPropertyBlock(_block);
        }

        private void OnDestroy()
        {
            // The meshes and materials are shared statics - destroying them here would take out
            // every other live beacon. Only the GameObjects this pin made are its to remove, and
            // Unity does that with the hierarchy.
        }

        // ── shared assets ─────────────────────────────────────────────────────────────────────

        private static void EnsureMeshes(TheBlockConfig.BeaconSpec spec)
        {
            if (_coneMesh == null)
                _coneMesh = BuildCone(spec.ConeRadius, spec.ConeHeight, 16);
            if (_beamMesh == null)
                _beamMesh = BuildCylinder(spec.BeamRadius, spec.BeamHeight, 12);
        }

        private static Material MaterialFor(Color color)
        {
            var key = ((Color32)color).GetHashCode();
            if (_materials.TryGetValue(key, out var cached) && cached != null) return cached;

            // Additive transparent, cloned from the checked-in template. Doing the keyword dance
            // here instead - which is what this did - leaves a material that is transparent in the
            // Editor and OPAQUE in a built Player, because the variant the keyword selects is only
            // compiled for keywords that some material ASSET in the build has on. See
            // TheBlock.Vfx.VfxMaterials.
            var material = VfxMaterials.Build(
                VfxBlend.UnlitAdditive, null, $"Beacon_{ColorUtility.ToHtmlStringRGB(color)}");

            // ONE/ONE, not the template's SrcAlpha/One. URP's own "Additive" scales the source by
            // alpha, and this pin drives alpha for its pop and its BeamOpacity - so taking URP's
            // factors would quietly dim a beacon that has been signed off. Blend factors are plain
            // material floats and survive into a build untouched; it is only the KEYWORD that has to
            // come from the asset, and that came with the clone.
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);

            // A SetColor on a property the shader does not have is a silent no-op - the fault that
            // left the Mustang the wrong colour for four units. Say so rather than render black.
            if (!material.HasProperty(BaseColorId))
                Debug.LogError("Beacon: URP/Unlit has no _BaseColor - the pin will not take a colour.");
            material.SetColor(BaseColorId, color);

            _materials[key] = material;
            return material;
        }

        /// <summary>A cone with its APEX DOWN, centred on the origin - the pin tip points at the spot.</summary>
        private static Mesh BuildCone(float radius, float height, int segments)
        {
            var half = height * 0.5f;
            var vertices = new Vector3[segments * 3];
            var triangles = new int[segments * 3];

            for (var i = 0; i < segments; i++)
            {
                var a0 = (float)i / segments * Mathf.PI * 2f;
                var a1 = (float)(i + 1) / segments * Mathf.PI * 2f;
                var v = i * 3;
                vertices[v] = new Vector3(0f, -half, 0f); // apex, pointing down
                vertices[v + 1] = new Vector3(Mathf.Cos(a1) * radius, half, Mathf.Sin(a1) * radius);
                vertices[v + 2] = new Vector3(Mathf.Cos(a0) * radius, half, Mathf.Sin(a0) * radius);
                triangles[v] = v;
                triangles[v + 1] = v + 1;
                triangles[v + 2] = v + 2;
            }

            var mesh = new Mesh { name = "BeaconCone" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>An open-ended tube centred on the origin. No caps - nothing ever sees them.</summary>
        private static Mesh BuildCylinder(float radius, float height, int segments)
        {
            var half = height * 0.5f;
            var vertices = new Vector3[(segments + 1) * 2];
            var triangles = new int[segments * 6];

            for (var i = 0; i <= segments; i++)
            {
                var a = (float)i / segments * Mathf.PI * 2f;
                var x = Mathf.Cos(a) * radius;
                var z = Mathf.Sin(a) * radius;
                vertices[i * 2] = new Vector3(x, -half, z);
                vertices[i * 2 + 1] = new Vector3(x, half, z);
            }

            for (var i = 0; i < segments; i++)
            {
                var v = i * 2;
                var t = i * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 2;
                triangles[t + 4] = v + 1;
                triangles[t + 5] = v + 3;
            }

            var mesh = new Mesh { name = "BeaconBeam" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// The mesh and material caches are statics, and Play mode does not clear statics by
        /// default. A stale <see cref="Mesh"/> from a previous session is a destroyed object that
        /// reads as non-null, so it has to be dropped explicitly - the same trap
        /// <see cref="UI.MapRegistry"/> guards against.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => ResetCaches();

        /// <summary>
        /// The same drop, callable. <c>RuntimeInitializeOnLoadMethod</c> fires once per Play
        /// SESSION, not once per scene load, so U26's Quit to Title - which unloads the world and
        /// rebuilds it inside one session - has to ask for this itself. See
        /// <see cref="Core.SessionReset"/>.
        /// </summary>
        public static void ResetCaches()
        {
            _coneMesh = null;
            _beamMesh = null;
            _materials.Clear();
        }
    }
}
