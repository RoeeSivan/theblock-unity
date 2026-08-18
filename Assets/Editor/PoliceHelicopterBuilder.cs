using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TheBlock.Police;
using TheBlock.Vehicles;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Builds the police Airbus H145 prefab from <c>police_helicopter.glb</c>.
    ///
    /// <b>Why this is not <see cref="MissionVehicleBuilder"/>'s Huey with a coat of paint.</b> The
    /// player already flies the Huey, and a repaint of the machine parked by the sea reads as the
    /// same helicopter in a different colour rather than as a police unit. The user's call, and
    /// the model was authored in Blender from a photograph of an Israeli Police H145 instead - a
    /// fenestron tail against the Huey's open two-blade rotor, four blades against two, a thin
    /// boom against a military fuselage. Different at any distance, before any colour is read.
    ///
    /// <b>Being authored rather than downloaded changes three things here:</b>
    ///
    /// <list type="number">
    /// <item>NO ORIENTATION CORRECTION IS ASSUMED. The Sketchfab imports each needed a measured
    /// <c>Euler(-90,0,0)</c> plus a facing flip, and the Huey flew tail-first for four units
    /// because the facing half was missed - invisible in a bounding box. This model was authored
    /// nose-down-Blender-−Y precisely so the glTF axis conversion lands it on Unity's +Z, and the
    /// build still MEASURES that and says so in the log. Port rule 1: a stated axis is a guess
    /// until a box agrees with it.</item>
    /// <item>The rotor nodes already have their pivots AT THEIR HUBS, so
    /// <c>MissionVehicleBuilder.BuildRotorPivots</c>'s wrap-in-a-pivot dance is not needed - that
    /// exists because Sketchfab exports put every node's origin at the model centre.</item>
    /// <item>It carries no <see cref="HelicopterController"/> - that component is the only thing
    /// that calls <see cref="EnterableRegistry.Register"/>, so leaving it off is what makes the
    /// craft un-enterable. It DOES carry a Rigidbody and a hull (below) since 2026-08-18; the
    /// first build had neither, and the user drove through it.</item>
    /// </list>
    ///
    /// <b>The hull</b> is four boxes on the root - skids, cabin, tail boom, fin - and NOT the rotor
    /// disc, for the same reason the Huey's collider excludes its own: a 10.4 m disc collider
    /// would sweep every façade the craft hovers past. The numbers are measured off the glb's own
    /// accessors (2026-08-18) and stated in PREFAB space, i.e. after the visual is recentred so the
    /// skids sit at the origin; <see cref="BuildHull"/> checks each box against the measured
    /// bounds and logs a warning if a re-export has moved the airframe out from under them.
    /// </summary>
    public static class PoliceHelicopterBuilder
    {
        public const string PrefabPath = "Assets/Prefabs/Vehicles/PoliceHelicopter.prefab";

        private const string ModelPath = "Assets/Models/Vehicles/police_helicopter.glb";

        /// <summary>Main disc, °/s at full throttle. Matches the Huey's so the two read alike.</summary>
        private const float MainRotorSpeed = 900f;

        private const float FenestronSpeed = 1700f;

        /// <summary>
        /// The Huey's figure, so a car meets the same wall either way. An H145 is 1.8 t empty and
        /// 3.7 t at gross; a 1.4 t car at 40 km/h shoves either a metre or two, which is the look
        /// asked for - it moves, it does not skitter.
        /// </summary>
        private const float Mass = 2200f;

        /// <summary>
        /// The hull, prefab space (skid bottoms at y = 0, body centred in XZ, nose at +Z), from the
        /// glb's node bounds with the visual's recentring offset (0, −0.03, +0.28) applied:
        /// skids ±1.37 × 0.03–0.94 × −1.04…3.15; fuselage ±1.04 × 0.83–2.48 × −5.20…4.52 with the
        /// engine deck to 2.82 and the boom narrowing behind z ≈ −1.95; fin ±0.16 × 1.63–3.88 ×
        /// −5.74…−4.44. The cabin box's front corners overhang the pointed nose by ~0.4 m, and the
        /// stabiliser's ±1.18 m span is left out - neither is a thing a car meets.
        /// </summary>
        private static readonly (string name, Vector3 centre, Vector3 size)[] Hull =
        {
            ("Skids", new Vector3(0f, 0.45f, 1.33f), new Vector3(2.74f, 0.90f, 4.20f)),
            ("Cabin", new Vector3(0f, 1.80f, 1.55f), new Vector3(2.10f, 2.00f, 6.50f)),
            ("Boom",  new Vector3(0f, 1.90f, -3.55f), new Vector3(1.00f, 1.60f, 3.70f)),
            ("Fin",   new Vector3(0f, 2.72f, -4.81f), new Vector3(0.40f, 2.25f, 1.30f)),
        };

        [MenuItem("The Block/Build Police Helicopter", priority = 28)]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"PoliceHelicopterBuilder: no model at {ModelPath}. Export it from " +
                               "source-assets/police_helicopter.blend first.");
                return;
            }

            var log = new System.Text.StringBuilder("PoliceHelicopterBuilder\n");

            var root = new GameObject("PoliceHelicopter");
            var visual = ((GameObject)PrefabUtility.InstantiatePrefab(model, root.transform)).transform;
            PrefabUtility.UnpackPrefabInstance(visual.gameObject, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);
            visual.name = "Visual";

            // Deliberately identity. See the class comment - the assertion below is what proves it.
            visual.localRotation = Quaternion.identity;
            visual.localScale = Vector3.one;

            var box = Measure(root);
            log.AppendLine($"  measured: {box.size:F2} m, centre {box.center:F2}, bottom {box.min.y:F2}");

            AssertFacing(visual, log);

            // Skids to the prefab origin, body centred in XZ - the origin every vehicle prefab in
            // this project shares, and what lets a spawner place one with no ride-height sums.
            visual.localPosition -= new Vector3(box.center.x, box.min.y, box.center.z);

            var rotor = BuildRotor(root, visual, log);
            var light = BuildSearchlight(root, visual, log);
            var beam = BuildBeam(light.transform, log);
            BuildHull(root, log);

            // Bind here rather than leaving [SerializeField]s for a human to drag: the prefab is
            // generated, so there is no inspector step in the pipeline to fill them in, and a null
            // searchlight is a helicopter that flies with no light and says nothing about it.
            root.AddComponent<PoliceHelicopter>().Bind(rotor, light, beam);

            Save(root, log);
            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString());
        }

        /// <summary>
        /// Proves the model came in nose-forward and right-way-up, instead of trusting it.
        ///
        /// Three named nodes are enough: the fenestron is at the TAIL so it must sit at −Z, the
        /// rotor hub is on TOP so it must be the highest of the three, and the FLIR ball is on the
        /// port skid so it must sit at −X once glTFast has negated the axis.
        /// </summary>
        private static void AssertFacing(Transform visual, System.Text.StringBuilder log)
        {
            var tail = Find(visual, "Fenestron");
            var hub = Find(visual, "Mainrotor");
            var flir = Find(visual, "FLIR");

            if (tail == null || hub == null || flir == null)
            {
                log.AppendLine("  ⚠ named nodes missing - cannot verify orientation. Re-export the glb.");
                return;
            }

            log.AppendLine($"  fenestron {tail.localPosition:F2} · hub {hub.localPosition:F2} · " +
                           $"FLIR {flir.localPosition:F2}");

            if (tail.localPosition.z > 0f)
                log.AppendLine("  ⚠ TAIL IS AT +Z - the craft would fly backwards. Check the export axes.");
            if (hub.localPosition.y < 2f)
                log.AppendLine("  ⚠ rotor hub is low - the model may be lying on its side.");
            if (flir.localPosition.x > 0f)
                log.AppendLine("  · FLIR is on the starboard side (reference has it to port) - cosmetic.");
        }

        /// <summary>
        /// The Rigidbody and the four hull boxes, on the root.
        ///
        /// Built KINEMATIC and asleep: <see cref="PoliceHelicopter.Configure"/> is what decides the
        /// regime at runtime (dynamic on the skids, kinematic aloft), and a prefab that is dynamic
        /// at rest would start falling in the Editor's prefab stage and in any scene it is dropped
        /// into by hand. Interpolation off - the runtime asserts it too - because while airborne the
        /// transform is written by <c>SmoothDamp</c> and interpolation would fight it.
        ///
        /// The centre of mass is pulled down to the cabin floor. PhysX's own figure from these boxes
        /// sits at 1.5 m, and a 2.7 m skid track under a 1.5 m centre tips on a hard side hit; at
        /// 0.9 m it slides instead, which is what a rammed helicopter should do in a game whose
        /// helicopter has no way to be righted.
        /// </summary>
        private static void BuildHull(GameObject root, System.Text.StringBuilder log)
        {
            var body = root.AddComponent<Rigidbody>();
            body.mass = Mass;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.linearDamping = 0.05f;
            body.angularDamping = 1f;

            // Kept in a local for the log: the getter reads back zero on an Editor-created body
            // whose physics representation has not been built yet, though the value serialises
            // (m_CenterOfMass, with m_ImplicitCom 0). Verified in the prefab YAML, not assumed.
            var centreOfMass = new Vector3(0f, 0.9f, 0.8f);
            body.centerOfMass = centreOfMass;

            var skids = SkidMaterial();
            var bounds = Measure(root);
            foreach (var (name, centre, size) in Hull)
            {
                var box = root.AddComponent<BoxCollider>();
                box.center = centre;
                box.size = size;
                box.sharedMaterial = skids;

                // Each box must lie inside the airframe's measured extents (with a little slack for
                // the nose overhang) - the check that catches a re-export moving the model.
                var min = centre - size * 0.5f;
                var max = centre + size * 0.5f;
                bool inside = min.x >= bounds.min.x - 0.1f && max.x <= bounds.max.x + 0.1f &&
                              min.y >= bounds.min.y - 0.1f && max.y <= bounds.max.y + 0.1f &&
                              min.z >= bounds.min.z - 0.5f && max.z <= bounds.max.z + 0.5f;
                log.AppendLine($"  hull '{name}': centre {centre:F2} size {size:F2}" +
                               (inside ? "" : "  ⚠ OUTSIDE the measured airframe - re-measure the glb"));
            }

            log.AppendLine($"  rigidbody {Mass} kg, kinematic at rest, CoM {centreOfMass:F2}, " +
                           $"{Hull.Length} boxes on '{skids.name}', rotor disc outside all of them");
        }

        /// <summary>
        /// Skids on tarmac, and the number is MEASURED against how far a rammed helicopter slides.
        ///
        /// On Unity's default friction (0.6/0.6) a 1400 kg car at 15 m/s moved the parked aircraft
        /// 0.82 m - a nudge that reads as "it is bolted down" rather than as the shove the user
        /// asked for. At 0.30/0.45 the same hit gives 1.38 m and a 3° swing, which is a helicopter
        /// being shoved. It is also the physically right direction: skids are a low-friction contact
        /// on purpose, which is why real ground handling puts wheels under them.
        ///
        /// <c>Minimum</c> combine, so the ground's own material cannot pull the figure back up.
        ///
        /// Saved as <c>.asset</c>, not <c>.physicsMaterial</c>: <c>AssetDatabase.CreateAsset</c>
        /// answers a physics-material extension with <i>"CreateAsset() should not be used to create
        /// a file of type 'physicsMaterial'… this error will in a future release be changed to an
        /// exception"</i>. The extension is cosmetic; the asset type is not.
        /// </summary>
        private static PhysicsMaterial SkidMaterial()
        {
            const string path = "Assets/Materials/PoliceHeli/HeliSkids.asset";
            var existing = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (existing != null) return existing;

            System.IO.Directory.CreateDirectory("Assets/Materials/PoliceHeli");
            var material = new PhysicsMaterial("HeliSkids")
            {
                dynamicFriction = 0.30f,
                staticFriction = 0.45f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Rotor BuildRotor(GameObject root, Transform visual, System.Text.StringBuilder log)
        {
            var blades = new System.Collections.Generic.List<Rotor.Blade>();

            // Axes are in the CRAFT's own frame. The main disc turns about its up; the fenestron is
            // a lateral fan, so it turns about the craft's right.
            foreach (var (node, axis, speed) in new[]
                     {
                         ("Mainrotor", Vector3.up, MainRotorSpeed),
                         ("Fenestron", Vector3.right, FenestronSpeed),
                     })
            {
                var pivot = Find(visual, node);
                if (pivot == null)
                {
                    log.AppendLine($"  ⚠ no '{node}' node - that rotor will not spin");
                    continue;
                }

                blades.Add(new Rotor.Blade { Pivot = pivot, Axis = axis, Speed = speed });
                log.AppendLine($"  rotor '{node}': {speed}°/s about {axis}, " +
                               $"{pivot.childCount} blades, pivot already at the hub");
            }

            var rotor = root.AddComponent<Rotor>();
            rotor.SetBlades(blades.ToArray());
            return rotor;
        }

        /// <summary>
        /// The visible beam: a translucent cone from the lamp to the ground.
        ///
        /// <b>It is not decoration and the unit fails without it.</b> This game's default sky is
        /// fixed daylight (<c>Progress.DayNightOn</c> ships false), and a 24° spot thrown from 34 m
        /// onto an already-lit street contributes almost nothing a player would notice. The cone is
        /// what makes the third star read as being pinned. Authored ONE unit long down +Z with a
        /// unit radius, so <see cref="PoliceHelicopter"/> can drive its length and spread straight
        /// off the aim distance and the cone angle.
        /// </summary>
        private static Transform BuildBeam(Transform parent, System.Text.StringBuilder log)
        {
            const int sides = 20;
            var vertices = new Vector3[sides + 2];
            var triangles = new int[sides * 3 * 2];

            vertices[0] = Vector3.zero;                       // apex, at the lamp
            for (int i = 0; i < sides; i++)
            {
                float a = 2f * Mathf.PI * i / sides;
                vertices[i + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 1f);
            }

            vertices[sides + 1] = new Vector3(0f, 0f, 1f);    // centre of the far cap

            for (int i = 0; i < sides; i++)
            {
                int a = 1 + i;
                int b = 1 + (i + 1) % sides;
                // Side wall, and the far cap so the beam reads as a solid shaft from underneath.
                triangles[i * 6 + 0] = 0;
                triangles[i * 6 + 1] = b;
                triangles[i * 6 + 2] = a;
                triangles[i * 6 + 3] = sides + 1;
                triangles[i * 6 + 4] = a;
                triangles[i * 6 + 5] = b;
            }

            var mesh = new Mesh { name = "SearchlightBeam" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Beam");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = BeamMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(false);

            log.AppendLine($"  beam cone: {sides} sides, additive, no shadows, starts disabled");
            return go.transform;
        }

        private static Material BeamMaterial()
        {
            const string path = "Assets/Materials/PoliceHeli/SearchlightBeam.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            // Its own folder, NOT Assets/Materials/Police - PoliceCarBuilder sweeps that one clean
            // of anything its own run did not write, so a beam material parked there would vanish
            // the next time the cruiser was rebuilt.
            System.IO.Directory.CreateDirectory("Assets/Materials/PoliceHeli");

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var material = new Material(shader) { name = "SearchlightBeam" };
            material.SetColor("_BaseColor", new Color(0.85f, 0.88f, 1f, 0.10f));
            material.SetFloat("_Surface", 1f);   // transparent
            material.SetFloat("_Blend", 1f);     // additive
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", 0f);      // both faces: you see it from inside as well
            material.renderQueue = 3000;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHAPREMULTIPLY_ON");

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// The searchlight, parented to the FLIR ball because that is where the reference photo
        /// puts the camera - a beam that leaves from anywhere else looks bolted on.
        ///
        /// It is created DISABLED. The light only exists while the aircraft is airborne, which is
        /// the whole of its perf budget: one extra shadow-casting light, at 512.
        /// </summary>
        private static Light BuildSearchlight(GameObject root, Transform visual,
                                              System.Text.StringBuilder log)
        {
            var flir = Find(visual, "FLIR");
            var anchor = new GameObject("Searchlight").transform;
            anchor.SetParent(flir != null ? flir : root.transform, false);

            var light = anchor.gameObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(0.98f, 0.97f, 0.90f);
            light.intensity = 120f;
            light.range = 70f;
            light.spotAngle = 24f;
            light.innerSpotAngle = 10f;
            light.shadows = LightShadows.Soft;
            light.enabled = false;

            SetUrpShadowResolution(light, log);

            // Points straight down at rest; PoliceHelicopter aims it at the target's ground point.
            anchor.localRotation = Quaternion.Euler(90f, 0f, 0f);

            log.AppendLine($"  searchlight on '{(flir != null ? flir.name : "root")}': spot, " +
                           $"{light.spotAngle}°, range {light.range}, 512 shadow map, starts disabled");
            return light;
        }

        /// <summary>
        /// Pins the searchlight to the 512 shadow map the unit's perf budget promises.
        ///
        /// <b><c>Light.shadowResolution</c> does nothing here and says so.</b> The first build of
        /// this prefab set it and Unity answered
        /// <i>"Light.shadowResolution is compatible only with the Built-In Render Pipeline"</i> -
        /// the property exists, takes the assignment, and is then ignored, so the light would have
        /// quietly drawn at whatever tier the pipeline asset defaults to. URP keeps its own copy on
        /// <c>UniversalAdditionalLightData</c>, and both fields are private: the tier is only read
        /// when <c>m_UsePipelineSettings</c> is false, so setting one without the other is a no-op
        /// as well. Property names were read off a live SerializedObject rather than guessed.
        ///
        /// Tier 1 = Medium = 512 against this project's <c>PC_RPAsset</c> (256 / 512 / 1024).
        /// </summary>
        private static void SetUrpShadowResolution(Light light, System.Text.StringBuilder log)
        {
            var data = light.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
            if (data == null)
                data = light.gameObject.AddComponent<
                    UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();

            var so = new SerializedObject(data);
            var usePipeline = so.FindProperty("m_UsePipelineSettings");
            var tier = so.FindProperty("m_AdditionalLightsShadowResolutionTier");
            if (usePipeline == null || tier == null)
            {
                log.AppendLine("  ⚠ URP light data fields not found - shadow map left at the " +
                               "pipeline default. Check UniversalAdditionalLightData's fields.");
                return;
            }

            usePipeline.boolValue = false;
            tier.intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine("  searchlight shadow map pinned to tier 1 (512) via URP light data");
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name)
                    return t;
            return null;
        }

        private static Bounds Measure(GameObject root)
        {
            var box = new Bounds();
            var any = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var local = new Bounds(
                    root.transform.InverseTransformPoint(renderer.bounds.center),
                    renderer.bounds.size);
                if (!any) { box = local; any = true; }
                else box.Encapsulate(local);
            }

            return box;
        }

        private static void Save(GameObject root, System.Text.StringBuilder log)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            System.IO.Directory.CreateDirectory("Assets/Prefabs/Vehicles");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            log.AppendLine($"  → {PrefabPath}");
        }
    }
}
