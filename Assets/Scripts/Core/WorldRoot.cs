using UnityEngine;

namespace TheBlock.Core
{
    /// <summary>
    /// Marks the GameObject that WorldBuilder owns. Everything under it is generated and will be
    /// destroyed and rebuilt on the next build — so never hand-edit anything below this object,
    /// change <c>config.ts</c> or the builder instead.
    ///
    /// It exists so the builder can find its own output by component rather than by name, which
    /// survives someone renaming the root.
    /// </summary>
    [DisallowMultipleComponent]
    public class WorldRoot : MonoBehaviour
    {
        /// <summary>Which <c>config.ts</c> produced this world — the exporter's `$sourceSha256`.</summary>
        public string SourceSha256;

        /// <summary>Human-readable summary of the last build, mirrored from the console.</summary>
        [TextArea(4, 20)]
        public string LastBuildReport;
    }
}
