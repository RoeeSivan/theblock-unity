using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace TheBlock.Core
{
    /// <summary>
    /// Catches the hitch, and the thing that caused it.
    ///
    /// <b>Why this exists, and why it is permanent.</b> This project has an intermittent fault the
    /// ledger has been carrying since U18: frames of ~800 ms, and at least once, green blocks tiled
    /// over the world with the Editor's own toolbar corrupted alongside. It survived one play-test
    /// as a screenshot and a shrug, because by the time anyone looks it is gone and a picture cannot
    /// say what the machine was holding at that moment.
    ///
    /// The toolbar detail is the tell: the Editor's own chrome is not drawn by this game, so
    /// corruption that reaches it is the graphics DEVICE, not game code. On a 16 GB unified-memory
    /// Mac that means one thing - the machine ran out of memory to give Metal, and a failed texture
    /// allocation renders as whatever was in that memory before. So the numbers worth having at the
    /// moment of the fault are the memory ones, and they must be recorded by something resident
    /// rather than reconstructed afterwards.
    ///
    /// <see cref="SkinWatchdog"/> made the same argument for the wedge and it worked: name it,
    /// measure it, pause on the frame. This is the same instrument pointed at memory. It is
    /// deliberately general - every unit after this one gets it for free.
    ///
    /// Auto-installs in the Editor and in a <b>Development Build</b>, and is compiled out of a
    /// release Player. U30b measures on the Player rather than the Editor (the ledger's rule), and
    /// the Player needs the same census lines in <c>Player.log</c> or there is nothing to read
    /// after a run. <c>SkinWatchdog</c> stays Editor-only: it pauses the Editor, which a Player
    /// cannot do.
    /// </summary>
    [DisallowMultipleComponent]
    public class FrameWatchdog : MonoBehaviour
    {
        [Tooltip("A frame longer than this is a hitch worth a full census, seconds. 0.3 s is five " +
                 "times a bad frame and a third of the ~800 ms the ledger recorded.")]
        [SerializeField] private float hitchSeconds = 0.3f;

        [Tooltip("Seconds between the quiet one-line samples, so a session leaves a trail even when " +
                 "nothing goes wrong. That trail is what turns 'it happened again' into a trend.")]
        [SerializeField] private float sampleInterval = 10f;

        [Tooltip("A jump in texture memory this large between samples is reported on its own, MB. " +
                 "Loading a district is the honest cause; anything else is worth knowing about.")]
        [SerializeField] private float textureJumpMb = 128f;

        [Tooltip("Pause the editor on a hitch. Off by default - a hitch is not a wedge, and pausing " +
                 "the first time the crowd streams in would be noise.")]
        [SerializeField] private bool pauseOnHitch;

        private float _nextSample;
        private float _worstFrame;
        private float _worstAt;
        private int _frames;
        private float _elapsed;
        private long _lastTextureBytes;
        private int _hitches;

        /// <summary>
        /// The render counters, read live rather than reconstructed.
        ///
        /// <b>Why these and not a time-based profile.</b> U30b round 1 fixed everything the texture
        /// census could see and the frame was still 44-77 ms in a chase. A census that reports only
        /// memory cannot say why, because the answer turned out not to be memory at all: at the
        /// parking lot the frame submits ~7,900 draw calls and ~21.9 M triangles, and a resolution
        /// sweep from 7.31 MP down to 0.46 MP moved it by 11 ms out of 78. That is a geometry bound,
        /// and geometry is exactly what these counters count.
        ///
        /// They are also what round 2 still owes: the six Tier 8 per-feature deltas are "how much
        /// does this feature add", and a triangle and draw-call delta is a far steadier answer than
        /// a millisecond one taken on a laptop whose thermal state nobody controls.
        ///
        /// <c>ProfilerRecorder</c> reads the counters the Player already maintains, so this costs a
        /// few reads a frame and nothing at all in a release build, where the class does not install.
        /// Every recorder is checked with <c>Valid</c> before it is printed - a counter that this
        /// platform does not publish is reported as absent rather than silently read as zero, which
        /// is the failure mode that would quietly turn a regression into "no change".
        ///
        /// ⚠ <b>There is no draw-call counter in a Player on Unity 6.5.</b> The obvious names
        /// (<c>Draw Calls Count</c>, <c>Batches Count</c>) return <c>Valid == false</c>; only the
        /// three <i>batched</i> draw-call counters exist, and they count what batching SAVED, not
        /// what was submitted. <c>UnityEditor.UnityStats.drawCalls</c> has the real figure and is
        /// Editor-only. So the honest per-frame number here is <c>SetPass Calls Count</c> plus the
        /// triangle count, and a draw-call census has to be taken in the Editor. This was found by
        /// probing rather than assumed - the first version of this class gated the whole block on
        /// <c>Draw Calls Count</c> being valid and printed "counters unavailable" on a Player where
        /// four of the six worked perfectly well.
        /// </summary>
        private ProfilerRecorder _setPassCalls, _triangles, _vertices, _shadowCasters, _skinned;

        private bool _recordersStarted;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Installs itself when Play starts. Nothing to add to a scene, nothing to forget.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var go = new GameObject("__FrameWatchdog") { hideFlags = HideFlags.DontSave };
            go.AddComponent<FrameWatchdog>();
            DontDestroyOnLoad(go);
        }
#endif

        private void Start()
        {
            StartRecorders();
            _lastTextureBytes = (long)Texture.currentTextureMemory;
            Debug.Log("FrameWatchdog: " + Census("start"));
        }

        private void StartRecorders()
        {
            _setPassCalls  = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _triangles     = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            _vertices      = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
            _shadowCasters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
            _skinned       = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Visible Skinned Meshes Count");
            _recordersStarted = true;
        }

        private void OnDestroy()
        {
            if (!_recordersStarted) return;

            // Every one of these holds a native handle. The watchdog is DontDestroyOnLoad, so in
            // normal play this runs once at quit - but a Quit to Title tears the object down with
            // the scene it was installed from, and a leaked recorder per session is a leak.
            _setPassCalls.Dispose();
            _triangles.Dispose();
            _vertices.Dispose();
            _shadowCasters.Dispose();
            _skinned.Dispose();
            _recordersStarted = false;
        }

        private void Update()
        {
            // Unscaled, deliberately: a synthetic test runs at timeScale 0.05 and a scaled delta
            // would report every one of those frames as tiny (memory: synthetic-play-test-decays).
            float dt = Time.unscaledDeltaTime;
            _frames++;
            _elapsed += dt;

            if (dt > _worstFrame)
            {
                _worstFrame = dt;
                _worstAt = Time.unscaledTime;
            }

            if (dt >= hitchSeconds)
            {
                _hitches++;
                Debug.LogWarning($"FrameWatchdog: HITCH {dt * 1000f:0} ms - {Census("hitch")}");

#if UNITY_EDITOR
                if (pauseOnHitch) UnityEditor.EditorApplication.isPaused = true;
#endif
            }

            if (Time.unscaledTime < _nextSample) return;
            _nextSample = Time.unscaledTime + sampleInterval;

            long textures = (long)Texture.currentTextureMemory;
            float jump = (textures - _lastTextureBytes) / 1048576f;
            _lastTextureBytes = textures;

            string note = Mathf.Abs(jump) >= textureJumpMb ? $" TEXTURE JUMP {jump:+0;-0} MB" : string.Empty;
            Debug.Log($"FrameWatchdog: {Census("sample")}{note}");

            _worstFrame = 0f;
            _frames = 0;
            _elapsed = 0f;
        }

        /// <summary>
        /// Everything worth knowing at one instant, on one line.
        ///
        /// <b>The texture triple is the point.</b> <c>current</c> is what is resident,
        /// <c>desired</c> is what the renderer would like if memory were free, and
        /// <c>nonStreaming</c> is the part that cannot be reduced at all. With streaming off,
        /// current == desired == everything, and the first two numbers are useless; with it on, the
        /// gap between them IS the headroom, and a <c>current</c> pinned at the budget while
        /// <c>desired</c> climbs is the machine telling you it is out of room before anything
        /// visibly breaks.
        /// </summary>
        private string Census(string reason)
        {
            var sb = new StringBuilder(220);
            float mean = _frames > 0 ? _elapsed / _frames * 1000f : 0f;

            sb.Append(reason).Append("  frame mean ").Append(mean.ToString("0.0"))
                .Append(" ms, worst ").Append((_worstFrame * 1000f).ToString("0"))
                .Append(" ms @").Append(_worstAt.ToString("0.0")).Append("s")
                .Append(", hitches ").Append(_hitches);

            sb.Append("  |  tex current ").Append(Mb(Texture.currentTextureMemory))
                .Append(" desired ").Append(Mb(Texture.desiredTextureMemory))
                .Append(" nonStreaming ").Append(Mb(Texture.nonStreamingTextureMemory))
                .Append(" total ").Append(Mb(Texture.totalTextureMemory));

            sb.Append("  |  unity alloc ").Append(Mb((ulong)Profiler.GetTotalAllocatedMemoryLong()))
                .Append(" reserved ").Append(Mb((ulong)Profiler.GetTotalReservedMemoryLong()))
                .Append(" mono ").Append(Mb((ulong)Profiler.GetMonoUsedSizeLong()));

            if (QualitySettings.streamingMipmapsActive)
                sb.Append("  |  streaming ON budget ").Append(QualitySettings.streamingMipmapsMemoryBudget.ToString("0"))
                    .Append(" MB, uploads ").Append(Texture.streamingMipmapUploadCount);
            else
                sb.Append("  |  streaming OFF");

            AppendRender(sb);
            return sb.ToString();
        }

        /// <summary>
        /// The render counters, each one printed only if this Player publishes it.
        ///
        /// Per-recorder rather than all-or-nothing on purpose: the counter set differs between Unity
        /// versions and platforms, and one missing name must not take the other four down with it.
        /// </summary>
        private void AppendRender(StringBuilder sb)
        {
            if (!_recordersStarted) return;

            sb.Append("  |  setPass ").Append(Count(_setPassCalls))
                .Append(" tris ").Append(Millions(_triangles))
                .Append(" verts ").Append(Millions(_vertices))
                .Append(" casters ").Append(Count(_shadowCasters))
                .Append(" skinned ").Append(Count(_skinned));
        }

        private static string Count(ProfilerRecorder recorder) =>
            recorder.Valid ? recorder.LastValue.ToString() : "n/a";

        private static string Millions(ProfilerRecorder recorder) =>
            recorder.Valid ? (recorder.LastValue / 1000000f).ToString("0.00") + "M" : "n/a";

        private static string Mb(ulong bytes) => (bytes / 1048576f).ToString("0") + " MB";
    }
}
