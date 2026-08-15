using TheBlock.Core;
using UnityEngine;

namespace TheBlock.Player
{
    /// <summary>
    /// Third-person chase camera, ported from <c>player.ts</c>'s <c>follow()</c>.
    ///
    /// It has no orbit and no mouse input by design: the player turns with A/D and the camera
    /// trails the body. Deliberately hand-written rather than Cinemachine — this is fifteen lines
    /// of behaviour with a specific feel to reproduce, and pulling in a whole camera framework to
    /// get it would be the tail wagging the dog. Cinemachine earns its place when the mission
    /// cameras land (U23's helicopter, U26's menus), not here.
    ///
    /// It follows an <see cref="IChaseTarget"/> rather than the player specifically, so the boom
    /// numbers and the look point come from whatever is being driven. U8 added that seam; U9's
    /// enter/exit is what actually swaps it mid-game via <see cref="Follow"/>.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class FollowCamera : MonoBehaviour
    {
        [Tooltip("Target held while on foot. The camera returns to it whenever a vehicle is left.")]
        [SerializeField] private PlayerController player;

        private IChaseTarget _target;
        private Camera _camera;

        /// <summary>Whatever the camera is behind right now.</summary>
        public IChaseTarget Target => _target;

        private void Awake()
        {
            _camera = GetComponent<Camera>();

            var snapshot = TheBlockConfig.Load();
            if (snapshot == null)
            {
                enabled = false;
                return;
            }

            var lens = snapshot.Config.Camera;
            _camera.fieldOfView = lens.Fov;
            _camera.nearClipPlane = lens.Near;
            // NOT lens.Far. The config's 320 m is a three.js draw budget that only ever worked
            // because the fog it ships with dissolves everything before the plane reaches it, and
            // because config.streaming unloads districts past 380 m anyway. See World.Atmosphere —
            // it owns this number and the fog range together, because a fog end that disagreed with
            // the far plane is exactly the hard slice this replaced.
            _camera.farClipPlane = World.Atmosphere.DrawDistance;

            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (player == null)
            {
                Debug.LogError("FollowCamera: no PlayerController in the scene.", this);
                enabled = false;
            }
        }

        /// <summary>
        /// Snapping happens in Start, not Awake: the boom comes off the target now, and a target
        /// reads it out of the config in its own Awake. Start runs after every Awake has, so the
        /// numbers are there whichever order the two components happened to wake up in.
        /// </summary>
        private void Start()
        {
            _target = player;
            SnapToTarget();
        }

        private void LateUpdate()
        {
            if (_target == null || _target.Anchor == null) return;

            var desired = _target.Anchor.TransformPoint(_target.LocalBoom);

            // The web build lerps by a fixed fraction per frame, which ties the feel to the frame
            // rate. This reproduces that fraction exactly at 60 fps and stays stable off it.
            var t = 1f - Mathf.Pow(1f - _target.FollowLerp, Time.deltaTime * 60f);
            transform.position = Vector3.Lerp(transform.position, desired, t);
            transform.LookAt(_target.LookTarget);
        }

        /// <summary>
        /// Points the camera at a new target. Null falls back to the player.
        ///
        /// <paramref name="snap"/> cuts to the new boom; without it the camera eases across on its
        /// usual lerp. Enter and exit pass <c>false</c>: the two booms are metres apart and a cut
        /// there reads as a glitch, where a sweep reads as the camera pulling back off the car.
        /// A teleport across the city is what <see cref="SnapToTarget"/> is for.
        /// </summary>
        public void Follow(IChaseTarget target, bool snap = true)
        {
            _target = target ?? player;
            if (snap) SnapToTarget();
        }

        /// <summary>Back to the on-foot boom — what leaving a vehicle calls.</summary>
        public void FollowPlayer(bool snap = true) => Follow(player, snap);

        /// <summary>Jumps the camera onto its mark with no smoothing — spawn, teleport, cut.</summary>
        public void SnapToTarget()
        {
            if (_target == null || _target.Anchor == null) return;
            transform.position = _target.Anchor.TransformPoint(_target.LocalBoom);
            transform.LookAt(_target.LookTarget);
        }
    }
}
