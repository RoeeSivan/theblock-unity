using UnityEngine;

namespace TheBlock.World
{
    /// <summary>
    /// The Paz forecourt: it answers one question — <see cref="AtPump"/> — and
    /// <see cref="Vehicles.FuelSystem"/> asks it for both the prompt and the action, so the two can
    /// never disagree. That is the same invariant <see cref="SevenEleven"/> holds for the till.
    ///
    /// <b>The web build has one flat 9 m circle at the station's origin, and this is where Unity can
    /// do better.</b> three.js had nothing to ask about the model's contents, so a single generous
    /// radius was the only shape available. <c>gas-station.glb</c> contains three separately named
    /// and positioned pump meshes, and the Editor pass bakes their world positions into
    /// <see cref="pumps"/> — so the prompt is anchored at the pump you actually parked at.
    ///
    /// <b>It is a UNION, and that is not belt-and-braces.</b> The pumps sit 7.9 m apart in a line, so
    /// per-pump circles ALONE would be stricter than the web across the middle of the forecourt,
    /// where the station's own 9 m reaches further in Z than a 6 m pump circle does. Keeping the
    /// station circle in the test makes the eligible area a superset by construction: nothing that
    /// worked in the web build stops working, and the lip of that circle — where the outer two pumps
    /// sit, 7.8 and 8.2 m out — gains the 6 m of forecourt it was missing.
    ///
    /// <b>This is NOT the 7-Eleven's "read the geometry off the model" pattern.</b>
    /// <c>seven-eleven-lot.glb</c> ships purpose-built marker empties carrying the config's own
    /// numbers. This model ships none — 119 nodes, every one of them geometry — so the anchors are
    /// render-mesh pivots, wherever the Sketchfab author left them. The builder therefore MEASURES
    /// and reports them rather than trusting them.
    /// </summary>
    [DisallowMultipleComponent]
    public class GasStation : MonoBehaviour
    {
        [Header("Pumps — baked by The Block → Build Gas Station")]
        [Tooltip("The three `gas pump` meshes, left to right along the forecourt.")]
        [SerializeField] private Transform[] pumps;

        [Header("Radii, metres")]
        [Tooltip("Around each pump. Port-side: the web build has no per-pump test at all.")]
        [SerializeField] private float pumpRadius = 6f;

        [Tooltip("Around the station centre — fuelConfig.pumpRadius, the web's whole trigger.")]
        [SerializeField] private float stationRadius = 9f;

        /// <summary>The station centre, in world space. The web's circle is centred here.</summary>
        public Vector3 Centre => transform.position;

        /// <summary>The baked pump anchors. Empty until the Editor pass has run.</summary>
        public Transform[] Pumps => pumps;

        /// <summary>Applies the config's radius. Called by the builder; the per-pump one is authored.</summary>
        public void Configure(Transform[] found, float configPumpRadius)
        {
            pumps = found;
            stationRadius = configPumpRadius;
        }

        private void Awake()
        {
            if (pumps == null || pumps.Length == 0)
                Debug.LogWarning(
                    "GasStation: no pump anchors. Run The Block → Build Gas Station. Refuelling " +
                    "still works on the station circle alone — that is exactly the web build.", this);
        }

        /// <summary>
        /// Is this world position at a pump? XZ only: the canopy's height is irrelevant, and a car
        /// on the forecourt and a car on a bridge over it are not a case this game can produce.
        /// </summary>
        public bool AtPump(Vector3 worldPos)
        {
            if (FlatSqrDistance(worldPos, transform.position) <= stationRadius * stationRadius) return true;
            return NearestPump(worldPos, out var sqr) != null && sqr <= pumpRadius * pumpRadius;
        }

        /// <summary>
        /// The closest pump and its squared XZ distance, or null when none is baked. Used to anchor
        /// the feedback at the pump you parked at rather than at the station's origin.
        /// </summary>
        public Transform NearestPump(Vector3 worldPos, out float sqrDistance)
        {
            sqrDistance = float.MaxValue;
            if (pumps == null) return null;

            Transform best = null;
            foreach (var pump in pumps)
            {
                if (pump == null) continue;
                var sqr = FlatSqrDistance(worldPos, pump.position);
                if (sqr >= sqrDistance) continue;
                sqrDistance = sqr;
                best = pump;
            }

            return best;
        }

        private static float FlatSqrDistance(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
