using System;
using UnityEngine;

namespace TheBlock.Player
{
    /// <summary>
    /// A host that wears a roster body - the player, the dance stage, the character screen's
    /// turntable. One component, three very different owners, because the swap itself is the same
    /// operation every time: throw the visual child away and instantiate another one.
    ///
    /// <b>The body is a CHILD and never the host.</b> That is not tidiness, it is the reason a
    /// second character can exist at all: a body that did not import at Joe's height is corrected by
    /// a scale, and on the player that scale would land on the same transform as his
    /// <c>CharacterController</c> - a shorter character would get a shorter capsule and start
    /// falling through steps. The crowd (<c>NpcBuilder</c>) and the stage dancer were already built
    /// this way; U29 is what finally made the player match them.
    ///
    /// <b>Every consumer that caches something off the body must listen to <see cref="Swapped"/>.</b>
    /// There are three, and each cached for a good reason before there was anything to invalidate:
    /// <c>PlayerAnimator</c> holds the Animator, <c>Dancer</c> holds it too, and
    /// <c>VehicleEnterExit</c> holds the renderer array it hides the driver with - a stale one of
    /// those leaves a body visible inside a car that is supposed to look empty.
    /// </summary>
    public class CharacterBody : MonoBehaviour
    {
        private const string VisualName = "Visual";

        [Header("Built by The Block → Build Characters")]
        [Tooltip("Whose body is under this host right now. Baked in by the builder so booting as " +
                 "the default character costs no swap at all.")]
        [SerializeField] private string wornId = CharacterRoster.DefaultId;

        [Tooltip("What the body's Animator plays. The player's graph and the dance's are different " +
                 "controllers over the same skeleton, which is what makes one prefab serve both.")]
        [SerializeField] private RuntimeAnimatorController controller;

        [Tooltip("CullUpdateTransforms for a body that can leave the frame; AlwaysAnimate for one " +
                 "that is posed off-camera and must still be right when it is looked at - the " +
                 "trap U19e paid for (memory: culled-animator-skips-pose-write).")]
        [SerializeField] private AnimatorCullingMode culling = AnimatorCullingMode.CullUpdateTransforms;

        [Tooltip("UnscaledTime for the character screen, which is on precisely when timeScale is 0.")]
        [SerializeField] private AnimatorUpdateMode updateMode = AnimatorUpdateMode.Normal;

        /// <summary>Raised after the new body is in place and its Animator is configured.</summary>
        public event Action Swapped;

        /// <summary>The live body's Animator, or null before <see cref="Awake"/>.</summary>
        public Animator Animator { get; private set; }

        /// <summary>Which roster id is on this host.</summary>
        public string WornId => wornId;

        private void Awake() => Rebind();

        /// <summary>
        /// Re-finds the Animator under the visual child. Public because a script recompile during
        /// Play clears it and never runs Awake again (memory: recompile-during-play-nulls-fields) -
        /// every cache in this project carries the same guard.
        /// </summary>
        public void Rebind()
        {
            var visual = transform.Find(VisualName);
            Animator = visual != null
                ? visual.GetComponent<Animator>()
                : GetComponentInChildren<Animator>(true);
        }

        /// <summary>
        /// Puts <paramref name="entry"/>'s body on this host. A no-op when it is already worn, which
        /// is what makes booting as the default free: the builder bakes Joe in, so the common case
        /// destroys nothing.
        /// </summary>
        public void Wear(CharacterRoster.Entry entry)
        {
            if (entry.Prefab == null)
            {
                Debug.LogWarning($"CharacterBody: roster entry '{entry.Id}' has no prefab.", this);
                return;
            }

            if (entry.Id == wornId && Animator != null) return;

            var old = transform.Find(VisualName);
            if (old != null)
            {
                // Detached BEFORE it is destroyed. Destroy is deferred to the end of the frame, so
                // an un-parented old body would still answer a GetComponentInChildren from anything
                // rebinding on this same frame - including the Swapped handlers below.
                old.SetParent(null, worldPositionStays: false);
                if (Application.isPlaying) Destroy(old.gameObject);
                else DestroyImmediate(old.gameObject);
            }

            // worldPositionStays: false - the prefab root carries the height match against Joe, and
            // preserving the world transform would throw that scale away. Same rule, and the same
            // trap, as VehicleEnterExit's seat mounting.
            var visual = Instantiate(entry.Prefab, transform, worldPositionStays: false);
            visual.name = VisualName;

            var animator = visual.GetComponent<Animator>();
            if (animator == null) animator = visual.AddComponent<Animator>();

            animator.runtimeAnimatorController = controller;
            animator.cullingMode = culling;
            animator.updateMode = updateMode;

            // The host owns the transform in all three cases - the controller moves the player, the
            // stage is a fixed mark, the turntable spins. Nothing here may be driven by a clip.
            animator.applyRootMotion = false;

            Animator = animator;
            wornId = entry.Id;

            Swapped?.Invoke();
        }

        /// <summary>Editor wiring, so the builder does not need SerializedObject for four fields.</summary>
        public void Configure(string id, RuntimeAnimatorController animatorController,
                              AnimatorCullingMode cullingMode, AnimatorUpdateMode mode)
        {
            wornId = id;
            controller = animatorController;
            culling = cullingMode;
            updateMode = mode;
        }
    }
}
