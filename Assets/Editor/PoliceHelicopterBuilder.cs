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
    /// <item>It carries no collider and no <see cref="HelicopterController"/>. This craft never
    /// lands, is never entered and never collides, so it needs no PhysX at all - and leaving
    /// <c>HelicopterController</c> off is also what makes it un-enterable, because that component
    /// is the only thing that calls <see cref="EnterableRegistry.Register"/>.</item>
    /// </list>
    /// </summary>
    public static class PoliceHelicopterBuilder
    {
        public const string PrefabPath = "Assets/Prefabs/Vehicles/PoliceHelicopter.prefab";

        private const string ModelPath = "Assets/Models/Vehicles/police_helicopter.glb";

        /// <summary>Main disc, °/s at full throttle. Matches the Huey's so the two read alike.</summary>
        private const float MainRotorSpeed = 900f;

        private const float FenestronSpeed = 1700f;

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
