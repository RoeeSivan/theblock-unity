using System.Text;
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
            _lastTextureBytes = (long)Texture.currentTextureMemory;
            Debug.Log("FrameWatchdog: " + Census("start"));
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

            return sb.ToString();
        }

        private static string Mb(ulong bytes) => (bytes / 1048576f).ToString("0") + " MB";
    }
}
