using UnityEngine;

namespace DoodleClimb.Game
{
    /// <summary>
    /// Smoothly follows the highest active character upward.
    /// The camera NEVER moves downward.
    ///
    /// Dynamic Zoom:
    ///   When both a player and an AI character are active, the camera
    ///   automatically zooms out so both are always visible on screen.
    ///   The orthographic size lerps toward whatever is required to keep
    ///   the lower character in frame, clamped between min and max sizes.
    ///   When they're close together it zooms back in.
    ///
    /// Follow Mode:
    ///   Both      — follows the highest of player and AI (default vs-AI)
    ///   PlayerOnly — ignores AI transform (Normal mode)
    ///   AIOnly    — ignores player transform (Watch AI mode)
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        public enum FollowMode { Both, PlayerOnly, AIOnly }

        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Follow Targets")]
        public Transform playerTransform;
        public Transform aiPlayerTransform;

        [Header("Follow Mode")]
        public FollowMode followMode = FollowMode.PlayerOnly;

        [Header("Vertical Offset")]
        [Tooltip("How far above the target the camera sits (world units).")]
        public float verticalOffset = 3f;

        [Header("Smoothing")]
        [Range(1f, 20f)]
        public float followSpeed = 6f;

        [Header("Limits")]
        public float minCameraY = 0f;

        [Header("Dynamic Zoom")]
        [Tooltip("Zoom out automatically to keep both characters on screen.")]
        public bool enableDynamicZoom = true;

        [Tooltip("Smallest orthographic size (closest zoom).")]
        public float minOrthographicSize = 9f;

        [Tooltip("Largest orthographic size (furthest zoom).")]
        public float maxOrthographicSize = 16f;

        [Tooltip("Extra padding below the lower character when zooming out.")]
        public float zoomPadding = 2f;

        [Tooltip("How quickly the zoom lerps to the target value.")]
        [Range(1f, 10f)]
        public float zoomSpeed = 3f;

        // ── Internal ──────────────────────────────────────────────────────────────
        private float  _highestReachedY;
        private Camera _cam;

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            _cam = GetComponent<Camera>();
            if (_cam == null) _cam = Camera.main;
            if (_cam == null)
                Debug.LogWarning("[CameraFollow] No Camera found — " +
                                 "TopY / BottomY will return 0.");
        }

        private void LateUpdate()
        {
            if (_cam == null) return;

            float targetY = GetFocusY();

            // Ratchet: only ever move upward
            if (targetY > _highestReachedY)
                _highestReachedY = targetY;

            float desiredY = Mathf.Max(_highestReachedY + verticalOffset, minCameraY);

            float newY = Mathf.Lerp(
                transform.position.y,
                desiredY,
                followSpeed * Time.deltaTime);

            transform.position = new Vector3(
                transform.position.x,
                newY,
                transform.position.z);

            // Dynamic zoom — keep both characters visible
            if (enableDynamicZoom && followMode == FollowMode.Both
                && playerTransform    != null && playerTransform.gameObject.activeSelf
                && aiPlayerTransform  != null && aiPlayerTransform.gameObject.activeSelf)
            {
                UpdateDynamicZoom();
            }
            else
            {
                // Smoothly restore default zoom when not needed
                _cam.orthographicSize = Mathf.Lerp(
                    _cam.orthographicSize,
                    minOrthographicSize,
                    zoomSpeed * Time.deltaTime);
            }
        }

        // ── Dynamic zoom ──────────────────────────────────────────────────────────
        private void UpdateDynamicZoom()
        {
            float lowerY  = Mathf.Min(playerTransform.position.y,
                                      aiPlayerTransform.position.y);

            // Distance from camera centre down to the lower character
            float offset  = transform.position.y - lowerY;

            // Required half-height to show the lower character + padding
            float required = Mathf.Max(minOrthographicSize, offset + zoomPadding);
            float target   = Mathf.Clamp(required, minOrthographicSize, maxOrthographicSize);

            _cam.orthographicSize = Mathf.Lerp(
                _cam.orthographicSize,
                target,
                zoomSpeed * Time.deltaTime);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────
        private float GetFocusY()
        {
            switch (followMode)
            {
                case FollowMode.AIOnly:
                    return aiPlayerTransform != null
                        ? aiPlayerTransform.position.y : float.MinValue;

                case FollowMode.PlayerOnly:
                    return playerTransform != null
                        ? playerTransform.position.y : float.MinValue;

                default: // Both
                    float playerY = playerTransform    != null
                        ? playerTransform.position.y    : float.MinValue;
                    float aiY     = aiPlayerTransform  != null
                        ? aiPlayerTransform.position.y  : float.MinValue;
                    return Mathf.Max(playerY, aiY);
            }
        }

        // ── Public API ────────────────────────────────────────────────────────────
        /// <summary>
        /// Call at game start / restart to snap camera to the spawn point instantly.
        /// </summary>
        public void ResetTo(float worldY)
        {
            _highestReachedY = worldY;
            transform.position = new Vector3(
                transform.position.x,
                worldY + verticalOffset,
                transform.position.z);

            if (_cam != null) _cam.orthographicSize = minOrthographicSize;
        }

        public void SetFollowMode(FollowMode mode) => followMode = mode;

        /// <summary>Top of the visible area in world space.</summary>
        public float TopY => transform.position.y +
            (_cam != null ? _cam.orthographicSize : 0f);

        /// <summary>Bottom of the visible area in world space.</summary>
        public float BottomY => transform.position.y -
            (_cam != null ? _cam.orthographicSize : 0f);
    }
}
