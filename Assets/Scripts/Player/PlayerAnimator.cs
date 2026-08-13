using UnityEngine;

namespace TheBlock.Player
{
    /// <summary>
    /// Drives Joe's Animator from <see cref="PlayerController"/>.
    ///
    /// Kept apart from the controller on purpose: movement decides what the body does, this decides
    /// what it looks like doing it. When the character roster lands (U29) the body underneath
    /// changes and the controller must not care.
    ///
    /// Root motion stays OFF. The controller owns position; the clips only supply pose. The sprint
    /// clip does carry root motion, which the Animator ignores — the builder corrects its playback
    /// rate instead so the cadence matches the speed the controller actually moves at.
    ///
    /// Build the graph this expects with **The Block → Build Joe Animator**.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class PlayerAnimator : MonoBehaviour
    {
        [SerializeField] private PlayerController controller;

        [Tooltip("How fast the blended gait catches up to the real speed, in m/s per second. " +
                 "Without it, tapping Shift snaps the legs from walk to sprint in one frame.")]
        [SerializeField] private float speedBlendRate = 12f;

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int JumpId = Animator.StringToHash("Jump");

        private Animator _animator;
        private float _blendedSpeed;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _animator.applyRootMotion = false;

            if (controller == null) controller = GetComponent<PlayerController>();
            if (controller == null)
            {
                Debug.LogError("PlayerAnimator: no PlayerController to read.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (controller != null) controller.Jumped += OnJumped;
        }

        private void OnDisable()
        {
            if (controller != null) controller.Jumped -= OnJumped;
        }

        private void Update()
        {
            _blendedSpeed = Mathf.MoveTowards(
                _blendedSpeed, controller.PlanarSpeed, speedBlendRate * Time.deltaTime);

            _animator.SetFloat(SpeedId, _blendedSpeed);
            _animator.SetBool(GroundedId, controller.IsGrounded);
        }

        private void OnJumped() => _animator.SetTrigger(JumpId);
    }
}
