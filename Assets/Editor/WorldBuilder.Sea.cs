using TheBlock.Core;
using TheBlock.World;
using UnityEditor;
using UnityEngine;
using Convert = TheBlock.Core.Convert;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// The sea, from <c>config.sea</c> — U12. Three parts, in the order they matter:
    ///
    ///  1. the water surface: one subdivided plane running the whole 2400 m out from the shore,
    ///     drawn by <c>TheBlock/Water</c>, which does the swells as a vertex displacement;
    ///  2. the sand: a plane displaced to <see cref="SeaGeometry.SeabedHeight"/> with a MeshCollider,
    ///     so the beach is a real floor a player walks DOWN into the water rather than a picture;
    ///  3. the shore wall: an invisible box at the waterline, the world's boundary on that side.
    ///
    /// HANDEDNESS. <c>config.sea.shoreX</c> is -430 and its water extends to more negative x — in
    /// three.js the sea is WEST. Under <see cref="Convert"/> that whole span mirrors: the shore is
    /// Unity <c>x +430</c> and the sea runs to LARGER x. Every derived edge here is produced by
    /// converting the config's own expression rather than by re-deriving it with a sign flipped by
    /// hand, which is what stops the beach and the water disagreeing about which side is dry.
    /// </summary>
    public static partial class WorldBuilder
    {
        private const string WaterShaderName = "TheBlock/Water";
        private const string BeachShaderName = "TheBlock/Beach";
        private const string WaterNormalMapPath = "Assets/Textures/waternormals.jpg";

        /// <summary>Flat seabed carried past the foot of the wade ramp, so near water has a floor.</summary>
        private const float BeachDeepRun = 40f;

        private static void BuildSea(Transform parent, TheBlockConfig.SeaSpec sea, Options options, Report report)
        {
            if (sea?.Beach == null || sea.Surface == null)
            {
                report.Warnings.Add("sea skipped — config has no `sea` section");
                return;
            }

            var group = NewGroup("Sea", parent);
            BuildWaterSurface(group, sea, report);
            BuildBeach(group, sea, options, report);
            BuildShoreWall(group, sea, report);
        }

        // --- water -------------------------------------------------------------------------------

        private static void BuildWaterSurface(Transform parent, TheBlockConfig.SeaSpec sea, Report report)
        {
            var shader = Shader.Find(WaterShaderName);
            if (shader == null)
            {
                report.Warnings.Add($"water skipped — shader `{WaterShaderName}` not found");
                return;
            }

            if (sea.Surface.Waves == null || sea.Surface.Waves.Count != 3)
            {
                report.Warnings.Add(
                    $"water: config has {sea.Surface.Waves?.Count ?? 0} waves, the shader takes exactly 3 — " +
                    "extras ignored, missing ones flat");
            }

            // The web build centres the plane at shoreX - width/2, i.e. half its width out to sea.
            var centre = Convert.Pos(sea.ShoreX - sea.Width * 0.5f, sea.Level, sea.CenterZ);

            var mesh = BuildGrid(
                "SeaSurface", centre, sea.Width, sea.Length, sea.Surface.SegX, sea.Surface.SegZ,
                heightAt: null);
            SaveGeneratedMesh(mesh, $"{GeneratedMeshFolder}/{mesh.name}.asset", report);

            var water = new GameObject("Water");
            water.transform.SetParent(parent, worldPositionStays: false);
            water.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = water.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateWaterMaterial(shader, sea, report);
            // The swells are a VERTEX displacement the shadow pass does not run, so a cast shadow
            // would be the shape of the flat plane and would not move. Off, as in the web build.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // No collider: the water is a surface to look at. Whether it holds a swimmer up is the
            // swim state's job, and it reads SeaGeometry rather than a collider (config.sea.swim).
            report.Placed.Add(
                $"Water {sea.Width:0} x {sea.Length:0} m @ Unity x [{SeaGeometry.ShoreX(sea):0}, " +
                $"{SeaGeometry.ShoreX(sea) + sea.Width:0}], level {sea.Level:0.##}");
        }

        private static Material CreateWaterMaterial(Shader shader, TheBlockConfig.SeaSpec sea, Report report)
        {
            EnsureFolder(GeneratedWorldFolder);
            var s = sea.Surface;
            var material = new Material(shader) { name = "Water" };

            var normalMap = AssetDatabase.LoadAssetAtPath<Texture2D>(WaterNormalMapPath);
            if (normalMap == null) report.Warnings.Add($"water: {WaterNormalMapPath} missing — ripple flat");
            else material.SetTexture("_NormalMap", normalMap);

            // sRGB, never .linear — the same trap as glTFast's baseColorFactor.
            material.SetColor("_DeepColor", TheBlockConfig.ColorFromHex(s.DeepColor));
            material.SetColor("_ShallowColor", TheBlockConfig.ColorFromHex(s.ShallowColor));
            material.SetColor("_SkyColor", TheBlockConfig.ColorFromHex(s.SkyColor));
            material.SetColor("_FoamColor", TheBlockConfig.ColorFromHex(s.FoamColor));
            material.SetFloat("_FresnelCeil", s.FresnelCeil);
            material.SetFloat("_FoamStrength", s.FoamStrength);
            material.SetFloat("_RippleStrength", s.RippleStrength);
            material.SetFloat("_SpecPower", s.SpecPower);
            material.SetFloat("_SwellFadeDepth", s.SwellFadeDepth);
            ApplyShoreRamp(material, sea);

            // Wave DIRECTIONS are three.js vectors in the XZ plane, so they mirror like any other
            // direction: Convert.Dir, not a hand-written negation.
            var speeds = Vector4.zero;
            for (int i = 0; i < 3; i++)
            {
                var wave = s.Waves != null && i < s.Waves.Count ? s.Waves[i] : null;
                if (wave == null)
                {
                    material.SetVector($"_Wave{i}", new Vector4(1f, 0f, 0f, 10f)); // zero amplitude
                    continue;
                }

                var direction = Convert.Dir(new Vector3(wave.DirX, 0f, wave.DirZ));
                material.SetVector($"_Wave{i}", new Vector4(direction.x, direction.z, wave.Amplitude, wave.Wavelength));
                speeds[i] = wave.Speed;
            }

            material.SetVector("_WaveSpeeds", speeds);

            var path = $"{GeneratedWorldFolder}/Water.mat";
            AssetDatabase.CreateAsset(material, path);
            report.Generated.Add(path);
            return material;
        }

        // --- beach -------------------------------------------------------------------------------

        /// <summary>
        /// The sand: a grid displaced to the seabed ramp, and the floor the player descends into the
        /// water on. Solid — a MeshCollider — because the alternative is walking on the sea.
        /// </summary>
        private static void BuildBeach(Transform parent, TheBlockConfig.SeaSpec sea, Options options, Report report)
        {
            var b = sea.Beach;

            // Landward edge meets the city ground; seaward edge clears the ramp's foot by
            // BeachDeepRun so there is flat floor under the near water. Both expressed in the
            // config's own frame and converted, so neither picks up a hand-written flip.
            var landward = Convert.Pos(sea.ShoreX + b.DryWidth, 0f, 0f).x;
            var seaward = Convert.Pos(sea.ShoreX - b.WadeRun - BeachDeepRun, 0f, 0f).x;
            float width = Mathf.Abs(seaward - landward);
            var centre = new Vector3((landward + seaward) * 0.5f, 0f, sea.CenterZ);

            var mesh = BuildGrid(
                "SeaBeach", centre, width, sea.Length, b.SegX, b.SegZ,
                heightAt: x => SeaGeometry.SeabedHeight(sea, x));
            SaveGeneratedMesh(mesh, $"{GeneratedMeshFolder}/{mesh.name}.asset", report);

            var beach = new GameObject("Beach");
            beach.transform.SetParent(parent, worldPositionStays: false);
            beach.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = beach.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateBeachMaterial(sea, report);

            if (options.Colliders)
            {
                beach.AddComponent<MeshCollider>().sharedMesh = mesh;
                report.Colliders++;
            }

            SetDistrictStaticFlags(beach);
            report.Placed.Add(
                $"Beach {width:0} x {sea.Length:0} m, Unity x [{Mathf.Min(landward, seaward):0}, " +
                $"{Mathf.Max(landward, seaward):0}], ramps to y {b.DeepY:0.##}");
        }

        private static Material CreateBeachMaterial(TheBlockConfig.SeaSpec sea, Report report)
        {
            EnsureFolder(GeneratedWorldFolder);
            var shader = Shader.Find(BeachShaderName);
            if (shader == null)
            {
                report.Warnings.Add($"beach: shader `{BeachShaderName}` not found — falling back to flat sand");
                shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) return null;
                var flat = new Material(shader) { name = "Beach" };
                flat.SetColor("_BaseColor", TheBlockConfig.ColorFromHex(sea.Beach.Color));
                return flat;
            }

            var k = sea.Beach.Look;
            var material = new Material(shader) { name = "Beach" };
            if (k != null)
            {
                material.SetColor("_DryColor", TheBlockConfig.ColorFromHex(k.DryColor));
                material.SetColor("_DryShadowColor", TheBlockConfig.ColorFromHex(k.DryShadowColor));
                material.SetColor("_WetColor", TheBlockConfig.ColorFromHex(k.WetColor));
                material.SetFloat("_GrainScale", k.GrainScale);
                material.SetFloat("_GrainStrength", k.GrainStrength);
                material.SetFloat("_BlotchScale", k.BlotchScale);
                material.SetFloat("_BlotchStrength", k.BlotchStrength);
                material.SetFloat("_WetBandDry", k.WetBandDry);
                material.SetFloat("_WetBandSea", k.WetBandSea);
                material.SetFloat("_DryRoughness", k.DryRoughness);
                material.SetFloat("_WetRoughness", k.WetRoughness);
            }

            ApplyShoreRamp(material, sea);

            var path = $"{GeneratedWorldFolder}/Beach.mat";
            AssetDatabase.CreateAsset(material, path);
            report.Generated.Add(path);
            return material;
        }

        // --- shore wall --------------------------------------------------------------------------

        /// <summary>
        /// The world's boundary at the waterline: a collider, no mesh. A car that reaches the sand
        /// stops here instead of driving out to sea.
        ///
        /// It sits on <b>Ignore Raycast</b>, which is Unity's answer to the web build's
        /// <c>markNonGround</c>. A wall is not a floor, and a downward probe started inside it — the
        /// side probe in the exit-a-vehicle path does exactly that — reads its top as ground and
        /// lifts the caller 8 m into the air. The layer is excluded from the default raycast mask,
        /// so probes miss it while collision is untouched.
        /// </summary>
        private static void BuildShoreWall(Transform parent, TheBlockConfig.SeaSpec sea, Report report)
        {
            var wallSpec = sea.Wall;
            if (wallSpec == null)
            {
                report.Warnings.Add("shore wall skipped — config has no `sea.wall`");
                return;
            }

            var wall = new GameObject("Shore Wall") { layer = LayerMask.NameToLayer("Ignore Raycast") };
            wall.transform.SetParent(parent, worldPositionStays: false);
            wall.transform.position = new Vector3(
                SeaGeometry.ShoreX(sea), wallSpec.Height * 0.5f, sea.CenterZ);

            var collider = wall.AddComponent<BoxCollider>();
            collider.size = new Vector3(wallSpec.Thickness, wallSpec.Height, sea.Length);
            report.Colliders++;

            report.Placed.Add(
                $"Shore wall {wallSpec.Thickness:0.#} x {wallSpec.Height:0.#} x {sea.Length:0} m " +
                $"@ Unity x {SeaGeometry.ShoreX(sea):0} (Ignore Raycast, collider only)");
        }

        // --- shared ------------------------------------------------------------------------------

        /// <summary>
        /// Writes the shoreline ramp onto a material. The water and the sand both shade from it and
        /// the sand mesh is built from it, so all three have to be given the same four numbers — a
        /// mismatch is a tide line that does not sit on the waterline.
        /// </summary>
        private static void ApplyShoreRamp(Material material, TheBlockConfig.SeaSpec sea)
        {
            material.SetFloat("_ShoreX", SeaGeometry.ShoreX(sea));
            material.SetFloat("_WadeRun", sea.Beach.WadeRun);
            material.SetFloat("_DeepY", sea.Beach.DeepY);
            material.SetFloat("_Level", sea.Level);
        }

        /// <summary>
        /// A flat XZ grid centred on <paramref name="centre"/>, optionally displaced in Y by a
        /// function of world X — which is all the seabed ever depends on.
        /// </summary>
        private static Mesh BuildGrid(
            string name, Vector3 centre, float width, float length, int segX, int segZ,
            System.Func<float, float> heightAt)
        {
            segX = Mathf.Max(1, segX);
            segZ = Mathf.Max(1, segZ);

            int countX = segX + 1;
            int countZ = segZ + 1;
            var vertices = new Vector3[countX * countZ];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[segX * segZ * 6];

            for (int z = 0; z < countZ; z++)
            {
                float tz = (float)z / segZ;
                float worldZ = centre.z + (tz - 0.5f) * length;
                for (int x = 0; x < countX; x++)
                {
                    float tx = (float)x / segX;
                    float worldX = centre.x + (tx - 0.5f) * width;
                    float y = heightAt?.Invoke(worldX) ?? centre.y;
                    int i = z * countX + x;
                    vertices[i] = new Vector3(worldX, y, worldZ);
                    uvs[i] = new Vector2(tx, tz);
                }
            }

            int t = 0;
            for (int z = 0; z < segZ; z++)
            {
                for (int x = 0; x < segX; x++)
                {
                    int i = z * countX + x;
                    // Wound to face +Y. Sign checked the same way the road ribbon's was.
                    triangles[t++] = i;
                    triangles[t++] = i + countX;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + countX;
                    triangles[t++] = i + countX + 1;
                }
            }

            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
