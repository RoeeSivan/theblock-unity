using System.Collections.Generic;
using System.Text;
using TheBlock.Core;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TheBlock.EditorTools
{
    /// <summary>
    /// Builds the police cruiser prefab — <b>The Block → Build Police Car</b>.
    ///
    /// <b>Why it is not just a seventeenth entry in <c>config.vehicle.cars</c>.</b> The web build
    /// does not list the cop car as a car either: <c>police.ts</c> hard-codes
    /// <c>const COP_URL = '/models/optimized/lod/police.glb'</c> and loads it outside the car list
    /// entirely, because it is never drivable, never parked, and never painted. Adding it to the
    /// config would mean editing the exporter in the other repo to describe something that repo's
    /// game does not have — so the spec is stated here, with its numbers measured rather than typed.
    ///
    /// It reuses <see cref="CarBuilder"/> wholesale: same shared origin (body centre in XZ, tyre
    /// contact patch in Y), same 1400 kg chassis, same suspension, same material cloning onto U15's
    /// compressed textures. A pose taken off any car prefab therefore drops straight into this one,
    /// which is what lets a cop be placed in a traffic lane with the same code that promotes a lot
    /// car (U17b's rule, and the reason it is worth reusing rather than hand-building).
    ///
    /// <b>Two things it deliberately does NOT do:</b>
    ///  - it never touches <c>CarSpawner.carPrefabs</c>. That list is what <c>Take()</c> resolves a
    ///    carjack against, and <c>CarBuilder.WireSpawner</c> rewrites it wholesale from the four
    ///    drivable cars every run — a police prefab in there would be both stealable and deleted.
    ///  - it writes its materials to its own folder, because
    ///    <see cref="VehicleMaterials.Sweep"/> deletes everything in a folder the current run did not
    ///    write, and the next <b>Build Drivable Cars</b> would take the police materials with it.
    /// </summary>
    public static class PoliceCarBuilder
    {
        /// <summary>The prefab, and the name <c>PoliceSystem</c> loads it by.</summary>
        public const string PrefabPath = "Assets/Prefabs/Vehicles/PoliceCar.prefab";

        private const string CarName = "PoliceCar";

        /// <summary>
        /// The raw Sketchfab source, not the web build's shipped copy.
        ///
        /// <c>CLAUDE.md</c> rule 3: the shipped 450 KB file is draco-compressed, webp, and decimated
        /// from 256.5k to 51.3k tris for a DOWNLOAD budget that does not exist here, and feeding it
        /// to Unity stacks a second lossy pass on top plus a Draco import dependency. This is the
        /// 8.4 MB original.
        /// </summary>
        private const string ModelUrl = "/models/police_car.glb";

        /// <summary>
        /// Scale to real size. <b>Measured, not inherited:</b> the source GLB's world AABB is
        /// 6.705 × 2.477 × 1.985 m, and 5.65 / 6.705 = 0.8427 — which lands the other two axes on
        /// 2.088 and 1.673, against the web build's independently measured 2.09 × 1.67 × 5.65. Three
        /// axes agreeing is what makes this a measurement rather than a coincidence.
        /// </summary>
        private const float ModelScale = 0.8428f;

        /// <summary>
        /// The CrownVic faces <c>+Z</c> natively, which is already Unity's forward — so the net
        /// rotation wanted is NONE, and π is what produces none:
        /// <c>RotFromRadians(π)</c> is −180° and <c>ModelFacing</c> is +180°.
        /// The web build reached the same π from the other side, for the opposite reason.
        /// </summary>
        private const float ModelYaw = Mathf.PI;

        /// <summary>Written and swept by this builder alone. See the class note.</summary>
        private const string MaterialFolder = "Assets/Materials/Police";

        /// <summary>
        /// What the body box must measure, metres, and how far off is tolerable.
        ///
        /// This is the import check, and it is here because this model is a case nothing else in the
        /// project has hit: its <c>Sketchfab_model</c> node carries an Rx(−90) with <b>no</b>
        /// cancelling <c>GLTF_SceneRootNode</c> twin, unlike the Mustang and the gas station (memory
        /// <c>sketchfab-root-matrices-swap-yz</c>). If glTFast resolves that differently, the car
        /// arrives on its side — and a 5.65 m TALL police car is a number, not a screenshot.
        /// </summary>
        private static readonly Vector3 ExpectedBody = new(2.09f, 1.67f, 5.65f);

        private const float BodyTolerance = 0.25f;

        /// <summary>
        /// Stands the model up. <b>Measured in the editor, not guessed.</b>
        ///
        /// This GLB's <c>Sketchfab_model</c> node carries an Rx(−90) with no cancelling
        /// <c>GLTF_SceneRootNode</c> twin — the Mustang and the gas station both have the pair, this
        /// one does not — and glTFast passes it straight through. The imported result was measured
        /// by instantiating the asset and reading it: length on <b>Y</b> (6.705), width on X, height
        /// on <b>Z</b>, wheels at z 0.42 with the roof lights at z 1.895 (so <b>+Z was up</b>), and
        /// the front wheels at y −1.868 against the rear at +1.724 (so <b>−Y was forward</b>).
        ///
        /// <c>Euler(-90, 0, 0)</c> sends +Z to +Y and −Y to +Z: up stays up and the nose comes round
        /// to Unity's forward. The first build without it produced a car 5.65 m TALL, a 1.36 m wheel
        /// radius and a 5.4 m chassis box — all of which the CHECK line caught as numbers rather
        /// than as a screenshot.
        /// </summary>
        private static readonly Quaternion StandUpright = Quaternion.Euler(-90f, 0f, 0f);

        [MenuItem("The Block/Build Police Car", priority = 25)]
        public static void BuildMenu() => Build();

        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                const string busy = "PoliceCarBuilder: stop Play mode first.";
                Debug.LogError(busy);
                return busy;
            }

            TheBlockConfig.Load(reload: true);
            var spec = new TheBlockConfig.CarSpec
            {
                Name = CarName,
                ModelUrl = ModelUrl,
                ModelScale = ModelScale,
                ModelYaw = ModelYaw,

                // No bodyColor: the livery is painted into the texture, and VehicleMaterials.IsPaint
                // matches only a `CarPrimaryColor`/`primary` slot, which this model has no such
                // material for. Nothing to repaint is the correct outcome here, not a miss.
                BodyColor = null,

                // No door: the CrownVic's body, glass and interior are separate meshes but there is
                // no door joint in the rig at all, so there is nothing to hinge. U19b's officer will
                // step out of a car whose door does not open, the same way the player already does.
                Door = null,
            };

            var driver = new TheBlockConfig.DriverSpec { Seats = OfficerSeat() };

            var written = new HashSet<string>(System.StringComparer.Ordinal);
            var log = new StringBuilder();

            var prefab = CarBuilder.BuildFromSpec(
                spec, driver, MaterialFolder, StandUpright, written, log);
            VehicleMaterials.Sweep(MaterialFolder, written, log);
            AssetDatabase.SaveAssets();

            if (prefab == null)
            {
                var missing = $"PoliceCarBuilder — FAILED\n{log}";
                Debug.LogError(missing);
                return missing;
            }

            MakeUnstealable(log);
            CheckImport(prefab, log);

            var report = $"PoliceCarBuilder — {PrefabPath}\n{log}";
            Debug.Log(report, prefab);
            return report;
        }

        /// <summary>
        /// The officer's seat block, stated here for the same reason the rest of this spec is: the
        /// web build has no cop car in <c>config.vehicle.cars</c>, so it has no seat for one either,
        /// and adding one would mean editing a config in the other repo to describe something that
        /// game does not have.
        ///
        /// <b>The numbers are the Avenger's, and that is a measurement rather than a shrug.</b> The
        /// seat block is not the cushion — it is where the entry clip's ORIGIN goes, a person
        /// standing beside the door at road level, and the clip's own hip travel carries the body
        /// into the car from there (memory <c>driver-seat-is-clip-origin</c>). Three of the web's
        /// four cars share <c>(-2.31, -0.84, -0.1)</c> because that is a saloon's door, not a
        /// particular saloon's door; the CrownVic measures 2.09 × 1.67 × 5.65 m against the
        /// Mustang's 1.95 × 1.40 × 4.72, so it is the widest of them and 2.31 m from the body centre
        /// still clears its flank. <c>y</c> cancels the body centre exactly — the anchor stands on
        /// the road — and the scale stays at 1 because the officer is 1.89 m native against Joe's
        /// 1.81 and a 4% difference in a seated pose is nothing to correct for.
        ///
        /// <c>CarBuilder</c> feeds <c>Raw</c> through <see cref="Convert.ModelOffset"/>, which flips
        /// Z alone, so <c>x = -2.31</c> stays on Unity's left — the driver's side, as in the web.
        /// </summary>
        private static Dictionary<string, TheBlockConfig.SeatSpec> OfficerSeat() =>
            new(System.StringComparer.Ordinal)
            {
                [ModelUrl] = new TheBlockConfig.SeatSpec
                {
                    X = -2.31f,
                    Y = -0.84f,
                    Z = -0.1f,
                    Yaw = -1.501f,
                    Scale = 1f,
                },
            };

        /// <summary>
        /// Clears <c>CarController.enterable</c> on the built prefab.
        ///
        /// <c>VehicleEnterExit.TryEnter</c> resolves <c>E</c> against <c>EnterableRegistry</c> and
        /// takes the nearest member, so without this the first thing a player does when a cop pulls
        /// alongside is press <c>E</c> and drive away in it. Done here rather than by hand because a
        /// rebuild would silently restore the default.
        /// </summary>
        private static void MakeUnstealable(StringBuilder log)
        {
            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                if (!contents.TryGetComponent<TheBlock.Vehicles.CarController>(out var car))
                {
                    log.AppendLine("        enter   NO CarController on the prefab — nothing to lock");
                    return;
                }

                var serialized = new SerializedObject(car);
                serialized.FindProperty("enterable").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                log.AppendLine("        enter   enterable=false — E cannot take a cop car");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// Measures the built prefab against <see cref="ExpectedBody"/> and says so either way.
        ///
        /// A silent pass is the point: the alternative is finding out at the checkpoint that the
        /// pursuit works perfectly and the cop is lying on its roof.
        /// </summary>
        private static void CheckImport(GameObject prefab, StringBuilder log)
        {
            if (!prefab.TryGetComponent<BoxCollider>(out var box))
            {
                log.AppendLine("        CHECK   no chassis box on the prefab — the build did not finish");
                Debug.LogWarning("PoliceCarBuilder: the built prefab has no BoxCollider.");
                return;
            }

            // The box floor is held clear of the road (CarBuilder.ChassisGroundGap), so its height is
            // shorter than the body by that gap. Only X and Z are compared directly; Y is compared
            // against the box's own top, which IS the body's height.
            float top = box.center.y + box.size.y * 0.5f;
            var measured = new Vector3(box.size.x, top, box.size.z);
            var error = new Vector3(
                Mathf.Abs(measured.x - ExpectedBody.x),
                Mathf.Abs(measured.y - ExpectedBody.y),
                Mathf.Abs(measured.z - ExpectedBody.z));

            bool ok = error.x <= BodyTolerance && error.y <= BodyTolerance && error.z <= BodyTolerance;
            log.AppendLine(
                $"        CHECK   body {measured.x:0.00} x {measured.y:0.00} x {measured.z:0.00} m " +
                $"against the web's {ExpectedBody.x:0.00} x {ExpectedBody.y:0.00} x {ExpectedBody.z:0.00} — " +
                (ok ? "MATCHES, so glTFast resolved the root rotation the same way" : "MISMATCH"));

            if (ok) return;

            log.AppendLine(
                "        CHECK   if the LENGTH came back as the height, the uncancelled Sketchfab " +
                "Rx(-90) survived import: pre-rotate the visual by Euler(-90, 0, 0), U13's pattern.");
            Debug.LogWarning(
                $"PoliceCarBuilder: body measured {measured} against expected {ExpectedBody}. " +
                "See the build log — this is the root-rotation case.");
        }
    }
}
