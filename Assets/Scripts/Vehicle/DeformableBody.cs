using UnityEngine;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// U35b layer ①: the bodywork dents where you hit it.
    ///
    /// <b>Why this can exist at all.</b> glTFast leaves imported meshes readable - both its normal
    /// and its Draco path finish with <c>UploadMeshData(false)</c> - so the vertices of a .glb are
    /// reachable at runtime with no import setting to tick and no baked copy to check in. Verified on
    /// all five car models before a line of this was written: 18/18 meshes on the Mustang, 4/4 on each
    /// Draco-compressed lot car, 13/13 on the cruiser.
    ///
    /// <b>Every mesh under the car, not "the body mesh".</b> This is the one thing the models forced.
    /// The Mustang's .glb has eighteen nodes and each one is a MATERIAL covering the whole 2.28 × 5.63 m
    /// car - ShinySilver, MatteWhite, CarPrimaryColor, tyer_rubber - so denting "the body" and leaving
    /// the chrome behind tears the car in half. The lot cars are the opposite shape (one 64-105k vert
    /// shell plus a door and a mirror) and the same rule covers them for free.
    ///
    /// <b>Wheels are excluded.</b> A dented, spinning wheel does not read as damage, it reads as a
    /// bug. <c>CarBuilder</c> wires the list and leaves out anything <see cref="CarWheel"/> poses.
    ///
    /// <b>Cloned lazily, per mesh, and the original is the repair.</b> A mesh is only copied the first
    /// time a dent actually reaches it, and the pristine data is never kept twice - the asset in the
    /// .glb IS the pristine copy, so <see cref="Restore"/> is a <c>sharedMesh</c> assignment rather
    /// than a re-upload of a vertex array. That is also what makes eviction under
    /// <see cref="DamageBudget"/> cheap enough to be the answer.
    ///
    /// <b>Nothing here touches a collider.</b> The chassis is one box built by <c>CarBuilder</c>;
    /// a deformed shell is a look, not a shape you can catch a wheel on. Rebuilding a convex hull per
    /// impact is the version of this feature that costs the frame.
    /// </summary>
    [DisallowMultipleComponent]
    public class DeformableBody : MonoBehaviour
    {
        [Tooltip("Static meshes that may dent - the three lot cars' shells. Wired by CarBuilder.")]
        [SerializeField] private MeshFilter[] panels = System.Array.Empty<MeshFilter>();

        [Tooltip("Skinned meshes that may dent - the Mustang's eighteen and the cruiser's body. " +
                 "Wired by CarBuilder; wheels and their brake discs are deliberately absent.")]
        [SerializeField] private SkinnedMeshRenderer[] skinnedPanels = System.Array.Empty<SkinnedMeshRenderer>();

        [Tooltip("Metres of bodywork affected around the contact point.")]
        [SerializeField] private float radius = 0.9f;

        [Tooltip("Metres a vertex may move on ONE impact, at full closing speed.")]
        [SerializeField] private float strength = 0.16f;

        [Tooltip("Metres a vertex may EVER move from where the artist put it, however many times " +
                 "this car is hit. The clamp that stops a car turning inside-out.")]
        [SerializeField] private float maxDeform = 0.28f;

        [Tooltip("Fraction of the push applied sideways, per vertex, so a dent is not a smooth " +
                 "sphere pressed into the panel.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float jitter = 0.18f;

        [Tooltip("Seconds between deforms on this car. An impact is several contacts and a scrape " +
                 "is a burst of them; without this the same crash writes the buffer five times.")]
        [SerializeField] private float minInterval = 0.12f;

        [Tooltip("Normals are recalculated only on meshes smaller than this. Above it the dent " +
                 "shades a little flat and the frame is safe - the Tesla's shell alone is 105k.")]
        [SerializeField] private int recalculateNormalsUnderVerts = 25000;

        /// <summary>
        /// One dentable mesh, static or skinned. <see cref="Clone"/> is null until a dent actually
        /// reaches this panel.
        ///
        /// <b>Both kinds are here because the cars are both kinds.</b> The three lot cars are plain
        /// <c>MeshFilter</c> shells; the Mustang is eighteen <c>SkinnedMeshRenderer</c>s sharing a
        /// sixteen-bone rig, and so is the cruiser. That is not decoration on the Mustang - its door
        /// and its four wheels are driven through that rig by <see cref="CarDoor"/> and
        /// <see cref="CarWheel"/>.
        /// </summary>
        private sealed class Panel
        {
            public MeshFilter Filter;
            public SkinnedMeshRenderer Skin;
            public Renderer Renderer;
            public Mesh Original;
            public Mesh Clone;
            public Vector3[] Pristine;
            public Vector3[] Work;

            /// <summary>Skinned only: which bone owns each vertex. Null on a static panel.</summary>
            public BoneWeight[] Weights;

            /// <summary>Skinned only, one per bone, rebuilt each dent: skin space to world.</summary>
            public Matrix4x4[] ToWorld;
            public Matrix4x4[] ToSkin;
            public float[] Scale;

            public bool Alive => Filter != null || Skin != null;

            public Mesh Mesh
            {
                get => Filter != null ? Filter.sharedMesh : Skin != null ? Skin.sharedMesh : null;
                set
                {
                    if (Filter != null) Filter.sharedMesh = value;
                    else if (Skin != null)
                    {
                        // Assigning a mesh to a SkinnedMeshRenderer resets its local bounds to the
                        // new mesh's - which for a skinned mesh are BIND-space bounds, five metres
                        // tall on a car whose rig stands the shell on end. Putting the renderer's own
                        // bounds back is what keeps the culling honest.
                        var bounds = Skin.localBounds;
                        Skin.sharedMesh = value;
                        Skin.localBounds = bounds;
                    }
                }
            }
        }

        private Panel[] _panels;
        private CarController _car;
        private float _lastDeform = -999f;

        /// <summary>True once anything on this car has moved.</summary>
        public bool Dented { get; private set; }

        /// <summary>
        /// Exempt from eviction while the player is driving it. Eviction restores a car to pristine,
        /// and a car un-denting is only acceptable when it is not the one filling the screen.
        /// </summary>
        public bool Protected => _car != null && _car.Driven;

        private void Awake() => TryGetComponent(out _car);

        private void OnDestroy() => DamageBudget.GiveDeform(this);

        /// <summary>Editor-side wiring, used by <c>CarBuilder</c>.</summary>
        public void Configure(MeshFilter[] staticPanels, SkinnedMeshRenderer[] skinned)
        {
            panels = staticPanels ?? System.Array.Empty<MeshFilter>();
            skinnedPanels = skinned ?? System.Array.Empty<SkinnedMeshRenderer>();
        }

        /// <summary>How many panels this car could dent. Read by the build log and by tests.</summary>
        public int PanelCount =>
            (panels != null ? panels.Length : 0) + (skinnedPanels != null ? skinnedPanels.Length : 0);

        /// <summary>
        /// Push the bodywork in around <paramref name="worldPoint"/>.
        /// </summary>
        /// <param name="worldPoint">The contact point, from <c>CrashSensor.Impact.Point</c>.</param>
        /// <param name="worldNormal">The contact normal. Unity points it from the OTHER collider
        /// toward the body whose callback fired - which is this car - so a wall struck head-on reports
        /// a normal pointing backwards down the car, and the dent goes ALONG it. It was written as the
        /// negation first and the nose bulged outward by 0.136 m; the sign is now measured, not
        /// argued. Taking the car's velocity instead would be wrong on the one case that matters
        /// most: being rammed while stationary.</param>
        /// <param name="severity">0-1. The closing speed, already normalised by the caller.</param>
        public void Dent(Vector3 worldPoint, Vector3 worldNormal, float severity)
        {
            if (PanelCount == 0 || severity <= 0f) return;
            if (Time.time - _lastDeform < minInterval) return;
            _lastDeform = Time.time;

            EnsurePanels();

            float push = strength * Mathf.Clamp01(severity);
            var direction = worldNormal.normalized;
            bool touchedAny = false;

            foreach (var panel in _panels)
            {
                if (!panel.Alive) continue;
                if (DentOne(panel, worldPoint, direction, push)) touchedAny = true;
            }

            if (!touchedAny) return;

            if (!Dented)
            {
                Dented = true;
                DamageBudget.TakeDeform(this);
            }
        }

        /// <summary>
        /// One panel. Returns whether any vertex actually moved, so a car whose dent landed entirely
        /// on empty air does not claim a budget slot.
        ///
        /// <b>The distance test is done in WORLD space, and on a skinned panel it has to be.</b> A
        /// skinned mesh's vertices are stored in BIND space, which on the Mustang is a frame where the
        /// car stands on its nose - measured, not assumed: the shell's vertices run to 5.57 m in Y
        /// through the renderer's own transform and to 5.57 m in Z through the skinning. Testing bind
        /// coordinates against a world contact point would dent a car-shaped region of empty air above
        /// the road. So each vertex is carried to world through the matrix its own bone supplies, and
        /// the push is carried back the same way.
        ///
        /// The cost is one matrix multiply per vertex, which is what a static panel's cheaper
        /// "transform the point once" trick was buying - and it is paid at most eight times a second
        /// per car (see <see cref="minInterval"/>), never per frame.
        /// </summary>
        private bool DentOne(Panel panel, Vector3 worldPoint, Vector3 worldDirection, float push)
        {
            // The early-out that makes "every panel on the car" affordable: a mirror four metres from
            // the impact is rejected on one sphere-against-AABB test. The renderer's bounds are
            // already in world space for both kinds of panel, which is the other reason to work there.
            if (panel.Renderer == null) return false;
            if (panel.Renderer.bounds.SqrDistance(worldPoint) > radius * radius) return false;

            EnsureClone(panel);
            if (panel.Work == null) return false;

            if (!BuildMatrices(panel)) return false;

            float radiusSquared = radius * radius;
            bool moved = false;

            var work = panel.Work;
            var pristine = panel.Pristine;
            var weights = panel.Weights;
            var toWorld = panel.ToWorld;
            var toSkin = panel.ToSkin;
            var scale = panel.Scale;

            for (int i = 0; i < work.Length; i++)
            {
                int bone = weights != null ? weights[i].boneIndex0 : 0;
                if (bone < 0 || bone >= toWorld.Length) bone = 0;

                var at = toWorld[bone].MultiplyPoint3x4(work[i]);

                float dx = at.x - worldPoint.x;
                float dy = at.y - worldPoint.y;
                float dz = at.z - worldPoint.z;
                float distanceSquared = dx * dx + dy * dy + dz * dz;
                if (distanceSquared > radiusSquared) continue;

                // (1 - d/r)^2: full depth at the contact, nothing at the rim, and a shoulder in
                // between. Linear falloff leaves a visible crease at exactly the radius.
                float falloff = 1f - Mathf.Sqrt(distanceSquared) / radius;
                falloff *= falloff;

                // Deterministic per-vertex noise. Not UnityEngine.Random: this runs a hundred
                // thousand times inside one frame and must not consume the stream the gameplay
                // shares, and the same car hit the same way should dent the same way.
                float noise = Hash(i) * jitter;

                var step = toSkin[bone].MultiplyVector(worldDirection * (push * falloff * (1f + noise)));
                var candidate = work[i] + step;

                // Clamped against where the ARTIST put the vertex, never against last frame's answer -
                // a per-impact clamp accumulates and the car turns inside-out on the tenth crash. The
                // limit is in metres, so it is divided by this bone's own scale to reach mesh units.
                float limit = maxDeform / Mathf.Max(0.0001f, scale[bone]);
                var fromPristine = candidate - pristine[i];
                float travel = fromPristine.magnitude;
                if (travel > limit) candidate = pristine[i] + fromPristine * (limit / travel);

                if ((candidate - work[i]).sqrMagnitude < 1e-10f) continue;
                work[i] = candidate;
                moved = true;
            }

            if (!moved) return false;

            panel.Clone.SetVertices(work);
            if (work.Length < recalculateNormalsUnderVerts) panel.Clone.RecalculateNormals();
            panel.Clone.RecalculateBounds();
            return true;
        }

        /// <summary>
        /// The vertex-to-world matrices for this panel, rebuilt on every dent because the rig moves:
        /// the wheels spin and the door swings, so a matrix cached at bind time would put those
        /// vertices wherever they were when the car spawned.
        ///
        /// One entry per bone on a skinned panel, one entry full stop on a static one. Only
        /// <c>boneIndex0</c> is consulted per vertex - the dominant weight - because the alternative
        /// is four matrix multiplies per vertex to answer a question whose resolution is a 0.9 m
        /// sphere.
        /// </summary>
        private static bool BuildMatrices(Panel panel)
        {
            if (panel.Skin == null)
            {
                panel.ToWorld ??= new Matrix4x4[1];
                panel.ToSkin ??= new Matrix4x4[1];
                panel.Scale ??= new float[1];

                var host = panel.Filter.transform;
                panel.ToWorld[0] = host.localToWorldMatrix;
                panel.ToSkin[0] = host.worldToLocalMatrix;
                panel.Scale[0] = host.lossyScale.x;
                return true;
            }

            var bones = panel.Skin.bones;
            var bind = panel.Original != null ? panel.Original.bindposes : null;
            if (bones == null || bind == null || bones.Length == 0 || bind.Length < bones.Length) return false;

            if (panel.ToWorld == null || panel.ToWorld.Length != bones.Length)
            {
                panel.ToWorld = new Matrix4x4[bones.Length];
                panel.ToSkin = new Matrix4x4[bones.Length];
                panel.Scale = new float[bones.Length];
            }

            for (int b = 0; b < bones.Length; b++)
            {
                if (bones[b] == null)
                {
                    panel.ToWorld[b] = Matrix4x4.identity;
                    panel.ToSkin[b] = Matrix4x4.identity;
                    panel.Scale[b] = 1f;
                    continue;
                }

                panel.ToWorld[b] = bones[b].localToWorldMatrix * bind[b];
                panel.ToSkin[b] = panel.ToWorld[b].inverse;
                panel.Scale[b] = panel.ToWorld[b].MultiplyVector(Vector3.right).magnitude;
            }

            return true;
        }

        /// <summary>
        /// A cheap deterministic ±1 from a vertex index. Bit-mixed rather than sin-based so
        /// neighbouring vertices do not share a value, which would flatten the noise into a stripe.
        /// </summary>
        private static float Hash(int i)
        {
            unchecked
            {
                uint h = (uint)i * 2654435761u;
                h ^= h >> 13;
                h *= 2246822519u;
                h ^= h >> 16;
                return (h & 0xFFFF) / 32767.5f - 1f;
            }
        }

        /// <summary>
        /// The runtime mirror of the serialized list, built once.
        ///
        /// Rebuilt only when it is null, never when the lengths disagree: a rebuild throws away the
        /// references to the clones currently assigned to the renderers, and those are then
        /// unrestorable. The one thing that can null it mid-session is a recompile during Play
        /// (memory: <c>recompile-during-play-nulls-fields</c>), and that leaves the car wearing its
        /// dents with no way back until it respawns - which is the honest outcome, not a leak worth
        /// building machinery for.
        /// </summary>
        private void EnsurePanels()
        {
            if (_panels != null) return;

            var list = new System.Collections.Generic.List<Panel>(PanelCount);

            foreach (var filter in panels)
            {
                if (filter == null) continue;
                list.Add(new Panel
                {
                    Filter = filter,
                    Renderer = filter.GetComponent<Renderer>(),
                    Original = filter.sharedMesh,
                });
            }

            foreach (var skin in skinnedPanels)
            {
                if (skin == null) continue;
                list.Add(new Panel
                {
                    Skin = skin,
                    Renderer = skin,
                    Original = skin.sharedMesh,
                    Weights = skin.sharedMesh != null ? skin.sharedMesh.boneWeights : null,
                });
            }

            _panels = list.ToArray();
        }

        /// <summary>
        /// Copy this panel's mesh, once, the first time a dent reaches it.
        ///
        /// <b>The clone is what makes a car dent instead of a MODEL denting.</b> <c>sharedMesh</c> is
        /// the asset every instance of that .glb draws - the four parked Audis, the one you are
        /// driving and the one the police are - so writing vertices into it would dent every Audi in
        /// Florentin at once, permanently, including inside the Editor's own asset.
        /// </summary>
        private void EnsureClone(Panel panel)
        {
            if (panel.Clone != null) return;

            var source = panel.Original != null ? panel.Original : panel.Mesh;
            if (source == null) return;

            if (!source.isReadable)
            {
                // Cannot happen with the five models this game ships - all verified readable, both the
                // Draco-compressed lot cars and the skinned Mustang - but a model added later could
                // arrive with Read/Write off, and the failure is otherwise a silent "nothing dents".
                Debug.LogWarning($"DeformableBody: '{source.name}' on {name} is not readable - no dents on this panel.");
                panel.Filter = null;
                panel.Skin = null;
                return;
            }

            panel.Original = source;
            panel.Clone = Instantiate(source);
            panel.Clone.name = source.name + " (dented)";
            panel.Clone.MarkDynamic();          // this buffer is rewritten several times per crash
            panel.Pristine = source.vertices;
            panel.Work = source.vertices;       // a second, independent copy - `vertices` allocates
            panel.Weights ??= source.boneWeights.Length > 0 ? source.boneWeights : null;
            panel.Mesh = panel.Clone;
        }

        /// <summary>
        /// Put every panel back to the asset and release the clones.
        ///
        /// Called by <c>CarController.Respawn</c> (the R key repairs), by <see cref="DamageBudget"/>
        /// when this car is the oldest dented one and the cap is reached, and by
        /// <see cref="VehicleDamage"/> when the setting is switched off mid-session.
        /// </summary>
        public void Restore()
        {
            if (_panels != null)
            {
                foreach (var panel in _panels)
                {
                    if (panel.Clone != null)
                    {
                        if (panel.Alive && panel.Original != null) panel.Mesh = panel.Original;
                        Destroy(panel.Clone);
                    }

                    panel.Clone = null;
                    panel.Pristine = null;
                    panel.Work = null;
                }
            }

            Dented = false;
            DamageBudget.GiveDeform(this);
        }
    }
}
