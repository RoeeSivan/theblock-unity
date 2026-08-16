using UnityEngine;

namespace TheBlock.Npc
{
    /// <summary>
    /// Where the crowd stands, baked - U16b's port of <c>npc.config.ts</c>'s placement tables.
    ///
    /// <b>This asset is the crowd.</b> The original seeds people from three additive sources: 33
    /// hand-painted pavement rectangles (9 each), 38 sidewalk strips split into two opposing lanes
    /// (8 each, three of them overridden), and a per-district fallback. Those rectangles and strips
    /// were captured by hand with the web build's in-game recorder - they are DATA, not tuning, and
    /// re-deriving them would be re-authoring the crowd. <c>CrowdBuilder</c> converts them into
    /// Unity space once, at build time, and resolves a ground height per sample.
    ///
    /// <b>Baked rather than seeded at boot, for the reason U16 measured.</b> The web build creates
    /// ~700 pedestrian objects at load and merely freezes those out of range; in Unity the steady
    /// cost of the crowd measured at zero and the whole stutter was the INSTANTIATION BURST. So the
    /// people live here as plain structs and only those inside the cull radius are ever given a
    /// body - see <see cref="CrowdSpawner"/>. Nothing about their behaviour is approximated: each
    /// seed carries its own position, target, path position, pace and face, and gets them back when
    /// it is bound again.
    ///
    /// U18's run-over wants this asset too: a downed person is a seed with a different mode.
    /// </summary>
    public class CrowdSeedTable : ScriptableObject
    {
        /// <summary>How one person decides where to go next.</summary>
        public enum Mode : byte
        {
            /// <summary>Picks a spot within <c>stepRadius</c> and walks to it. Painted zones and districts.</summary>
            Wander = 0,

            /// <summary>Walks a lane end to end and turns around. Sidewalk strips.</summary>
            Strip = 1,

            /// <summary>A strip walker whose ends are gated by a traffic light. Zebra crossers.</summary>
            Crosser = 2,
        }

        /// <summary>An axis-aligned pavement rectangle, already converted to Unity space.</summary>
        [System.Serializable]
        public struct Rect
        {
            public float MinX;
            public float MaxX;
            public float MinZ;
            public float MaxZ;

            public bool Contains(Vector3 point) =>
                point.x >= MinX && point.x <= MaxX && point.z >= MinZ && point.z <= MaxZ;
        }

        /// <summary>
        /// One walking lane, resampled by arc length with its ground height already resolved.
        ///
        /// The web calls <c>buildPath</c> here, but only ever with TWO waypoints - and a centripetal
        /// Catmull-Rom through two points is a straight line. So there is no curve to port: the
        /// resampling exists solely to bake a Y per sample, which is what lets a strip follow a
        /// pavement that changes height without a raycast per step at runtime.
        /// </summary>
        [System.Serializable]
        public class LanePath
        {
            public Vector3[] Points = System.Array.Empty<Vector3>();
            public float Length;

            /// <summary>Position at arc length <paramref name="s"/>. Even spacing, so no search.</summary>
            public Vector3 At(float s)
            {
                int segments = Points.Length - 1;
                if (segments < 1) return Points.Length > 0 ? Points[0] : Vector3.zero;

                float t = Mathf.Clamp(s, 0f, Length) / Length * segments;
                int i = Mathf.Min((int)t, segments - 1);
                return Vector3.Lerp(Points[i], Points[i + 1], t - i);
            }
        }

        /// <summary>
        /// One person's entire state. Baked as the starting state, then owned by the spawner's
        /// runtime copy - a bound pedestrian writes this back when it is released, so walking out of
        /// range and back does not reset anybody.
        /// </summary>
        [System.Serializable]
        public struct Seed
        {
            public Vector3 Position;

            /// <summary>Wander only. Equal to <see cref="Position"/> when nothing is picked yet.</summary>
            public Vector3 Target;

            /// <summary>Strip and crosser only: arc length along <see cref="PathId"/>.</summary>
            public float S;

            /// <summary>Strip and crosser only: +1 towards the far end, −1 back.</summary>
            public sbyte Dir;

            /// <summary>Index into <c>npc.config.ts</c>'s people list. Baked, so a person keeps a face.</summary>
            public byte Face;

            /// <summary>Rolled once at bake from <c>speed { min, max }</c>, and never again.</summary>
            public float Speed;

            public Mode Mode;

            /// <summary>Wander only: the rectangle this person may not leave, or −1 for free roaming.</summary>
            public int RectId;

            /// <summary>Strip and crosser only.</summary>
            public int PathId;
        }

        [SerializeField] private Rect[] rects = System.Array.Empty<Rect>();
        [SerializeField] private LanePath[] paths = System.Array.Empty<LanePath>();
        [SerializeField] private Seed[] seeds = System.Array.Empty<Seed>();

        [Tooltip("Which npc.config.ts produced this. Compared against the loaded config's hash.")]
        [SerializeField] private string npcSourceSha256;

        [Tooltip("Most seeds found within the cull radius anywhere in the world, measured at bake. " +
                 "This is what liveCap has to cover.")]
        [SerializeField] private int peakWithinCull;

        [Tooltip("95th percentile of the same measurement - the number that matters in practice.")]
        [SerializeField] private int p95WithinCull;

        public Rect[] Rects => rects;
        public LanePath[] Paths => paths;
        public Seed[] Seeds => seeds;
        public string NpcSourceSha256 => npcSourceSha256;
        public int PeakWithinCull => peakWithinCull;
        public int P95WithinCull => p95WithinCull;

        /// <summary>Set once, by <c>CrowdBuilder</c>.</summary>
        public void Fill(Rect[] newRects, LanePath[] newPaths, Seed[] newSeeds, string sha, int peak, int p95)
        {
            rects = newRects;
            paths = newPaths;
            seeds = newSeeds;
            npcSourceSha256 = sha;
            peakWithinCull = peak;
            p95WithinCull = p95;
        }
    }
}
