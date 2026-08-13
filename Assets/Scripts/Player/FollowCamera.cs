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
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class FollowCamera : MonoBehaviour
    {
        [SerializeField] private PlayerController target;

        private Vector3 _localOffset;
        private float _followLerp;
        private Camera _camera;

        private void Awake()
        {
            _camera = GetComponent<Camera>();

            var snapshot = TheBlockConfig.Load();
            if (snapshot == null)
            {
                enabled = false;
                return;
            }

            var camera = snapshot.Config.Camera;
            _camera.fieldOfView = camera.Fov;
            _camera.nearClipPlane = camera.Near;
            _camera.farClipPlane = camera.Far;

            var boom = snapshot.Config.Player.Camera;
            // ModelOffset, not Pos: this is an offset in the player's own frame, and three.js
            // faces -Z where Unity faces +Z. Through Pos() the camera would sit in Joe's face.
            _localOffset = Convert.ModelOffset(boom.Offset.Raw);
            _followLerp = boom.FollowLerp;

            if (target == null) target = FindFirstObjectByType<PlayerController>();
            if (target == null)
            {
                Debug.LogError("FollowCamera: no PlayerController in the scene.", this);
                enabled = false;
                return;
            }

            SnapToTarget();
        }

        private void LateUpdate()
        {
            if (target == null) return;

            var desired = target.transform.TransformPoint(_localOffset);

            // The web build lerps by a fixed fraction per frame, which ties the feel to the frame
            // rate. This reproduces that fraction exactly at 60 fps and stays stable off it.
            var t = 1f - Mathf.Pow(1f - _followLerp, Time.deltaTime * 60f);
            transform.position = Vector3.Lerp(transform.position, desired, t);
            transform.LookAt(target.LookTarget);
        }

        /// <summary>Jumps the camera onto its mark with no smoothing — spawn, teleport, cut.</summary>
        public void SnapToTarget()
        {
            if (target == null) return;
            transform.position = target.transform.TransformPoint(_localOffset);
            transform.LookAt(target.LookTarget);
        }
    }
}
