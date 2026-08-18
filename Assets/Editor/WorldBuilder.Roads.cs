using System.Collections.Generic;
using TheBlock.Core;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using Convert = TheBlock.Core.Convert;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Roads, from <c>config.roads</c> - U12.
    ///
    /// The web build cloned one 8 m tile per A→B segment and rotated it: all three.js offered, and
    /// it shows at every bend, where two quads overlap in a hard V. Unity gets a spline through the
    /// same points and a ribbon generated along it, so a corner is a curve and the painted lines
    /// run through it unbroken.
    ///
    /// The <see cref="SplineContainer"/> is kept on each road object rather than thrown away after
    /// the mesh is built. It is the reason to use splines at all: U17's traffic wants a centreline
    /// it can sample at arbitrary distance with a tangent, U19's police want the same, and both
    /// would otherwise re-derive it from the raw polyline and disagree with the geometry at exactly
    /// the corners this unit smoothed.
    ///
    /// NO COLLIDER, matching the web build: roads sit 2 cm above the ground plate and flush with
    /// district pavement, and a wheel that met a 2 cm lip at 20 m/s would feel it. Cars drive on
    /// the ground plate and the district trimeshes; the road is a surface, not a solid.
    /// </summary>
    public static partial class WorldBuilder
    {
        /// <summary>Generated world materials/textures. Rebuilt every build, swept like the rest.</summary>
        private const string GeneratedWorldFolder = "Assets/Materials/World/Generated";

        /// <summary>Carriageway width. The web tile was 8 m across, and the lane markings assume it.</summary>
        private const float RoadWidth = 8f;

        /// <summary>Sampling step along the spline. Straights collapse to their endpoints anyway.</summary>
        private const float RoadStep = 2f;

        /// <summary>Metres of road per repeat of the surface texture, along the direction of travel.</summary>
        private const float RoadTileLength = 8f;

        private static void BuildRoads(Transform parent, TheBlockConfig.RoadsSpec roads, Report report)
        {
            if (roads?.Paths == null || roads.Paths.Count == 0)
            {
                report.Warnings.Add("roads skipped - config has no `roads.paths`");
                return;
            }

            var group = NewGroup("Roads", parent);
            var material = CreateRoadMaterial(report);

            int built = 0;
            float total = 0f;
            for (int i = 0; i < roads.Paths.Count; i++)
            {
                var points = ToUnityPath(roads.Paths[i], roads.Y);
                if (points.Count < 2)
                {
                    report.Warnings.Add($"road {i} skipped - fewer than 2 usable points");
                    continue;
                }

                var road = new GameObject($"Road_{i:00}");
                road.transform.SetParent(group, worldPositionStays: false);

                // The spline is authored in world space with the object at the origin, so a sampled
                // position needs no transform to become a world position. U17 reads it that way.
                var container = road.AddComponent<SplineContainer>();
                container.Spline = BuildSpline(points);

                var mesh = BuildRibbon(container.Spline, out float length);
                if (mesh == null)
                {
                    report.Warnings.Add($"road {i} skipped - spline came out shorter than one step");
                    Object.DestroyImmediate(road);
                    continue;
                }

                mesh.name = $"Road_{i:00}";
                SaveGeneratedMesh(mesh, $"{GeneratedMeshFolder}/{mesh.name}.asset", report);

                road.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = road.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // flat on the ground
                SetDistrictStaticFlags(road);

                built++;
                total += length;
            }

            report.Placed.Add($"Roads {built} spline(s), {total:0} m total, {RoadWidth:0} m wide @ y {roads.Y:0.##}");

            // U35g: the auto shop's street is not in config.roads - see WorldBuilder.AutoShop.cs.
            if (material != null) BuildAutoShopRoads(group, material, roads.Y, report);

            // U30b: three connectors the config never had - see ConnectorRoads.
            if (material != null) BuildConnectorRoads(group, material, roads.Y, report);
        }

        /// <summary>
        /// Streets the original never drew, because the original never had these places reachable.
        ///
        /// <b>Measured off the built scene, not guessed.</b> A top-down render of the district plus the
        /// scene's own road-spline endpoints says what the gap is: <c>Road_11</c> runs south down
        /// x≈296 and STOPS dead at z=−191, which is the parking lot's north edge; <c>Road_12</c>
        /// starts again at (289, −301), the lot's south edge; and the gas station's forecourt begins at
        /// x=306 with nothing but 6 m of grass between it and Road_11. So the network has two dead
        /// ends facing each other across a car park, and a fuel stop with no approach.
        ///
        /// Three ribbons fix it, and the routing is chosen around what is already standing:
        /// <list type="bullet">
        /// <item><b>The lot bypass</b> runs at x=303, four metres OUTSIDE the lot's own east edge
        /// (x max 299.4). Straight down x≈296 would have been shorter and would have painted asphalt
        /// over the lot's easternmost row of parked cars - visible in the render, and the reason this
        /// one bends.</item>
        /// <item><b>The gas apron</b> is a 12 m stub east off Road_11 at the forecourt's own centre
        /// line, z=−130.</item>
        /// </list>
        ///
        /// <b>A third connector was built, rendered and deleted, and that is worth keeping written
        /// down.</b> "Road_LotMouth" ran east-west across the lot to join the bypass to Road_05's stub.
        /// It was invisible in the render for a reason the ledger already knows in another form: roads
        /// are laid at <c>roads.y</c> = 0.02 and the parking-lot district's own surface reaches y=0.1,
        /// so the ribbon was under it. Raising it would have worked and would still have been wrong -
        /// it painted a carriageway straight through the marked bays. The lot is flat drivable asphalt
        /// with no kerb, so a car leaves the bypass onto it anywhere; it needs no painted road at all.
        ///
        /// Not in <c>config.ts</c> and deliberately not added to it: the original repo is the spec and
        /// is not to be edited (CLAUDE.md rule 4). Same shape as U35g's auto-shop street, which is why
        /// it uses the same spline/ribbon path rather than a second mesh generator.
        /// </summary>
        private static readonly (string Name, Vector3[] Path)[] ConnectorRoads =
        {
            ("Road_LotBypass", new[]
            {
                new Vector3(295.8f, 0f, -191.4f),   // Road_11's south end
                new Vector3(303f, 0f, -205f),
                new Vector3(303f, 0f, -288f),
                new Vector3(289.3f, 0f, -300.6f),   // Road_12's north end
            }),
            ("Road_GasApron", new[]
            {
                new Vector3(296.4f, 0f, -130f),     // on Road_11, at the forecourt's centre line
                new Vector3(308f, 0f, -130f),
            }),
        };

        /// <summary>Connectors built by an earlier run that are no longer in the table. Swept every build.</summary>
        private static readonly string[] RetiredConnectorRoads = { "Road_LotMouth" };

        /// <summary>The connectors, through the same spline/ribbon path as every config road.</summary>
        private static void BuildConnectorRoads(Transform roads, Material material, float y, Report report)
        {
            float total = 0f;
            int built = 0;

            foreach (var retired in RetiredConnectorRoads)
            {
                var gone = roads.Find(retired);
                if (gone != null) Object.DestroyImmediate(gone.gameObject);
                var mesh = $"{GeneratedMeshFolder}/{retired}.asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(mesh) != null) AssetDatabase.DeleteAsset(mesh);
            }

            foreach (var (name, path) in ConnectorRoads)
            {
                var stale = roads.Find(name);
                if (stale != null) Object.DestroyImmediate(stale.gameObject);

                var points = new List<Vector3>(path.Length);
                foreach (var p in path) points.Add(new Vector3(p.x, y, p.z));

                var road = new GameObject(name);
                road.transform.SetParent(roads, worldPositionStays: false);

                var container = road.AddComponent<SplineContainer>();
                container.Spline = BuildSpline(points);

                var mesh = BuildRibbon(container.Spline, out float length);
                if (mesh == null)
                {
                    report.Warnings.Add($"{name} skipped - shorter than one step");
                    Object.DestroyImmediate(road);
                    continue;
                }

                mesh.name = name;
                var assetPath = $"{GeneratedMeshFolder}/{mesh.name}.asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(assetPath) != null) AssetDatabase.DeleteAsset(assetPath);
                SaveGeneratedMesh(mesh, assetPath, report);

                road.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = road.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // flat on the ground
                SetDistrictStaticFlags(road);

                built++;
                total += length;
            }

            report.Placed.Add($"Connectors {built} spline(s), {total:0} m - the lot bypass and the gas apron");
        }

        /// <summary>Config polyline → Unity-space points, degenerate ones dropped.</summary>
        private static List<Vector3> ToUnityPath(List<TheBlockConfig.Vec3> path, float y)
        {
            var points = new List<Vector3>(path?.Count ?? 0);
            foreach (var p in path ?? new List<TheBlockConfig.Vec3>())
            {
                var world = Convert.Pos(p.X, y, p.Z);
                // The web build skips segments under 0.5 m as duplicate points; same rule, applied
                // to the knots instead, because a spline with a doubled knot kinks there.
                if (points.Count > 0 && Vector3.Distance(points[^1], world) < 0.5f) continue;
                points.Add(world);
            }

            return points;
        }

        /// <summary>
        /// A spline through the centreline points. <c>AutoSmooth</c> gives each interior knot a
        /// Catmull-Rom tangent, so a straight run stays straight and a corner rounds off; the end
        /// knots keep the polyline's own direction, so a road still starts and ends where config says.
        /// </summary>
        private static Spline BuildSpline(List<Vector3> points)
        {
            var spline = new Spline();
            foreach (var point in points) spline.Add(new BezierKnot(point), TangentMode.AutoSmooth);
            return spline;
        }

        /// <summary>
        /// Extrudes a flat ribbon along the spline: two vertices per station, offset left and right
        /// of the centreline by half the road width.
        ///
        /// U runs across the road (0 at the right edge, 1 at the left) so the material's painted
        /// lines land at fixed distances from the centreline; V runs along it in metres/tile, so the
        /// markings keep a constant pitch whatever the segment's length - the thing the web build's
        /// stretched tile could not do, since scaling it stretched the paint too.
        /// </summary>
        private static Mesh BuildRibbon(Spline spline, out float length)
        {
            length = spline.GetLength();
            if (length < RoadStep) return null;

            int stations = Mathf.Max(2, Mathf.CeilToInt(length / RoadStep) + 1);
            float half = RoadWidth * 0.5f;

            var vertices = new Vector3[stations * 2];
            var normals = new Vector3[stations * 2];
            var uvs = new Vector2[stations * 2];
            var triangles = new int[(stations - 1) * 6];

            for (int i = 0; i < stations; i++)
            {
                float t = (float)i / (stations - 1);
                var position = (Vector3)spline.EvaluatePosition(t);
                var tangent = (Vector3)spline.EvaluateTangent(t);
                tangent.y = 0f;
                if (tangent.sqrMagnitude < 1e-8f) tangent = Vector3.forward;
                tangent.Normalize();

                var right = Vector3.Cross(Vector3.up, tangent).normalized;
                float along = t * length / RoadTileLength;

                vertices[i * 2] = position + right * half;
                vertices[i * 2 + 1] = position - right * half;
                normals[i * 2] = Vector3.up;
                normals[i * 2 + 1] = Vector3.up;
                uvs[i * 2] = new Vector2(0f, along);
                uvs[i * 2 + 1] = new Vector2(1f, along);
            }

            for (int i = 0; i < stations - 1; i++)
            {
                int v = i * 2;
                int t = i * 6;
                // Wound so the face looks up: verified by the sign of (C-A)x(D-A), which is -Y for
                // the other order - a road you can only see from underneath.
                triangles[t] = v;
                triangles[t + 1] = v + 3;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v;
                triangles[t + 4] = v + 1;
                triangles[t + 5] = v + 3;
            }

            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Asphalt with a double-yellow centre line and white edge lines, generated rather than
        /// imported - the web build's <c>road-straight.glb</c> is a 6 KB tile whose paint is
        /// geometry, which does not survive being stretched along a curve.
        /// </summary>
        private static Material CreateRoadMaterial(Report report)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                report.Warnings.Add("road material skipped - URP/Lit shader not found");
                return null;
            }

            EnsureFolder(GeneratedWorldFolder);

            var texture = CreateRoadTexture();
            var texturePath = $"{GeneratedWorldFolder}/RoadSurface.asset";
            AssetDatabase.CreateAsset(texture, texturePath);
            report.Generated.Add(texturePath);

            var material = new Material(shader) { name = "Road" };
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Smoothness", 0.25f);
            var materialPath = $"{GeneratedWorldFolder}/Road.mat";
            AssetDatabase.CreateAsset(material, materialPath);
            report.Generated.Add(materialPath);
            return material;
        }

        /// <summary>
        /// The road cross-section as pixels. U is across the road, so every marking is a band in X
        /// and the texture is uniform in Y apart from grain - which is what lets it tile along a
        /// curve without the paint wobbling.
        /// </summary>
        private static Texture2D CreateRoadTexture()
        {
            const int across = 256;
            const int along = 64;
            var asphalt = new Color(0.16f, 0.16f, 0.17f);
            var paintWhite = new Color(0.86f, 0.86f, 0.83f);
            var paintYellow = new Color(0.78f, 0.66f, 0.16f);

            var texture = new Texture2D(across, along, TextureFormat.RGBA32, mipChain: true, linear: false)
            {
                name = "RoadSurface",
                wrapModeU = TextureWrapMode.Clamp, // across: never wrap, or the edge line bleeds
                wrapModeV = TextureWrapMode.Repeat, // along: tiles every RoadTileLength metres
                filterMode = FilterMode.Bilinear,
                anisoLevel = 8,
            };

            var pixels = new Color[across * along];
            var random = new System.Random(12345); // fixed seed: the build stays a pure function
            for (int y = 0; y < along; y++)
            {
                for (int x = 0; x < across; x++)
                {
                    float u = (x + 0.5f) / across; // 0 = one kerb, 1 = the other
                    float fromCentre = Mathf.Abs(u - 0.5f) * RoadWidth; // metres from the centreline
                    float fromEdge = (0.5f - Mathf.Abs(u - 0.5f)) * RoadWidth;

                    var color = asphalt;
                    float grain = (float)random.NextDouble() * 0.05f - 0.025f;
                    color = new Color(color.r + grain, color.g + grain, color.b + grain);

                    // Double yellow: two 0.12 m lines with a 0.18 m gap, centred on the centreline.
                    if (fromCentre > 0.09f && fromCentre < 0.21f) color = paintYellow;
                    // White edge line 0.35 m in from the kerb.
                    if (fromEdge > 0.35f && fromEdge < 0.47f) color = paintWhite;

                    pixels[y * across + x] = color;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>Writes a generated mesh to disk so it survives the scene being closed.</summary>
        private static void SaveGeneratedMesh(Mesh mesh, string path, Report report)
        {
            EnsureFolder(GeneratedMeshFolder);
            AssetDatabase.CreateAsset(mesh, path);
            report.Generated.Add(path);
        }
    }
}
