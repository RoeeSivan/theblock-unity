using TheBlock.Core;
using UnityEngine;

namespace TheBlock.Minigame.Rhythm
{
    /// <summary>
    /// The body on stage — the port of <c>src/minigame/rhythm/dancer.ts</c>.
    ///
    /// One skeleton, one Animator, eight clips cross-faded between: a base groove, four flourishes
    /// cycled one per successful hit, and three reactions. The cross-fade is doing real work, not
    /// polish — these are arbitrary stock Mixamo clips whose start and end poses have nothing to do
    /// with each other, and 0.2 s of blend is what turns a snap into a move.
    ///
    /// <b>Two of the web's three hard problems here simply do not exist in Unity.</b> Its dancer has
    /// to strip the hips' X/Z tracks by hand so the routine does not wander off the stage, and it
    /// has to rebind every clip onto the chosen body by bone name because Mixamo stamps a per-upload
    /// counter into its namespace. Here Bake Into Pose does the first at import time and Humanoid
    /// retargeting does the second for free — see <c>DanceClipImporter</c>. What is left is the
    /// state machine, which is this file.
    ///
    /// <b>Win and Fail are terminal.</b> They clamp on their last frame and must not slide back to
    /// the groove: the routine is over, and a dancer who shrugs off a defeat and starts grooving
    /// again is the wrong ending.
    /// </summary>
    public class Dancer : MonoBehaviour, IChaseTarget
    {
        public const string IdleState = "Dance_Idle";
        public const string MissState = "Dance_Miss";
        public const string WinState = "Dance_Win";
        public const string FailState = "Dance_Fail";

        private static readonly string[] HitStates =
        {
            "Dance_Hit1", "Dance_Hit2", "Dance_Hit3", "Dance_Hit4",
        };

        [SerializeField] private Animator animator;

        [Tooltip("Whose body is on the stage. U29: the dancer IS the player, so it wears the roster " +
                 "pick — and this is the one place the swap had to reach beyond the player himself.")]
        [SerializeField] private TheBlock.Player.CharacterBody body;

        [Tooltip("Seconds of blend between clips — rhythmConfig.dancer.crossFadeSec.")]
        [SerializeField] private float crossFade = 0.2f;

        private int _hitIndex;

        private void Awake() => Bind();

        private void OnEnable()
        {
            if (body == null) body = GetComponent<TheBlock.Player.CharacterBody>();
            if (body != null) body.Swapped += OnBodySwapped;

            // Re-bound on every enable, not only in Awake. The stage stands DISABLED between
            // routines, so a character picked from the menu swaps a body this component was not
            // listening for — OnDisable had already unsubscribed. Taking the animator again on the
            // way in covers exactly that window.
            Bind();
        }

        private void OnDisable()
        {
            if (body != null) body.Swapped -= OnBodySwapped;
        }

        /// <summary>
        /// A swap destroys the skeleton this was holding, so the state the routine was in goes with
        /// it. Back to the groove is the only safe answer and also the right one: the character
        /// screen cannot be opened during the routine (<c>GameFlow.CanPause</c> refuses in
        /// <c>GameMode.Rhythm</c>), so this only ever runs between runs.
        /// </summary>
        private void OnBodySwapped()
        {
            Bind();
            ResetRun();
        }

        private void Bind()
        {
            if (body == null) body = GetComponent<TheBlock.Player.CharacterBody>();
            if (body != null)
            {
                if (body.Animator == null) body.Rebind();
                animator = body.Animator;
            }

            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            if (animator == null) return;

            // The dance is performed on the spot and the clips were baked into pose for it, so there
            // is no root motion to apply. Belt and braces: the import setting alone already means
            // there is nothing to apply, and this makes it impossible to reintroduce by accident.
            animator.applyRootMotion = false;

            // The stage is framed by its own camera and the dancer IS the shot, so it must keep
            // being posed whatever the culling volumes think. The crowd's CullCompletely is right
            // for a pedestrian and exactly wrong here.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        /// <summary>The base groove, looping. Where the body sits between notes.</summary>
        public void PlayIdle() => Fade(IdleState);

        /// <summary>
        /// The next flourish, cycled rather than random — so four hits in a row are four different
        /// moves, which random selection would not guarantee. Each returns to the groove on its own.
        /// </summary>
        public void PlayHit()
        {
            Fade(HitStates[_hitIndex % HitStates.Length]);
            _hitIndex++;
        }

        public void PlayMiss() => Fade(MissState);
        public void PlayWin() => Fade(WinState);
        public void PlayFail() => Fade(FailState);

        /// <summary>Back to the top for a fresh run.</summary>
        public void ResetRun()
        {
            _hitIndex = 0;
            PlayIdle();
        }

        /// <summary>
        /// Cross-fades in FIXED time, not normalised time.
        ///
        /// Normalised would scale the blend by the length of the clip being left, so leaving a 16 s
        /// flourish would blend for seconds and leaving a 2.8 s reaction would snap. The blend
        /// belongs to the transition, not to whichever clip happens to be playing.
        /// </summary>
        private void Fade(string state)
        {
            if (animator == null) return;
            animator.CrossFadeInFixedTime(state, crossFade, 0);
        }

        // ── IChaseTarget ──────────────────────────────────────────────────────────────────────
        //
        // The dancer IS a chase target, which is what lets the existing FollowCamera frame the
        // routine with no dance-specific camera code at all — `Follow(dancer)` to take the stage and
        // `FollowPlayer()` to leave it. U9 built that swap for getting into a car and the split it
        // chose (the camera owns the smoothing, the target owns where the boom hangs) is exactly
        // what makes a third kind of target free.

        [SerializeField] private Vector3 boom = new(0f, 2.4f, -5f);
        [SerializeField] private float lookY = 1.3f;

        public Transform Anchor => transform;

        /// <summary>
        /// Already in Unity's frame. The config's offset is authored in the DANCER's local space, so
        /// it goes through <see cref="Convert.ModelOffset"/> at build time — the same rule the
        /// player's own boom follows, and for the same reason: through <c>Pos</c> the camera ends up
        /// in the dancer's face.
        /// </summary>
        public Vector3 LocalBoom => boom;

        public Vector3 LookTarget => transform.position + Vector3.up * lookY;

        /// <summary>
        /// Snappier than the player's. The routine is a fixed shot of a body that does not travel,
        /// so there is nothing to smooth out and a lazy boom just reads as the camera settling for
        /// the first two seconds of the song.
        /// </summary>
        public float FollowLerp => 0.35f;

        /// <summary>Editor-side wiring, used by The Block → Build Campaign.</summary>
        public void Configure(Animator bodyAnimator, float crossFadeSec, Vector3 localBoom, float lookYOffset)
        {
            animator = bodyAnimator;
            crossFade = crossFadeSec;
            boom = localBoom;
            lookY = lookYOffset;
        }
    }
}
