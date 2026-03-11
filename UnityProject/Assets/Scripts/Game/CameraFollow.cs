using UnityEngine;

namespace DoodleClimb.Game
{
    /// <summary>
    /// Smoothly follows the player upward.
    /// The camera never moves downward — it only tracks upward progress.
    /// In "vs AI" mode it follows the highest of the two characters.
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Follow Targets")]
        public Transform playerTransform;
        public Transform aiPlayerTransform;   // optional, used in vs-AI mode

        [Header("Vertical Offset")]
        [Tooltip("How far above the target the camera sits (in world units).")]
        public float verticalOffset = 3f;

        [Header("Smoothing")]
        [Tooltip("Higher = snappier camera. Lower = smoother.")]
        [Range(1f, 20f)]
        public float followSpeed = 6f;

        [Header("Limits")]
        [Tooltip("The camera will never travel below this world-Y position.")]
        public float minCameraY = 0f;

        // ── Internal ──────────────────────────────────────────────────────────────
        private float _highestReachedY;

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void LateUpdate()
        {
            float targetY = GetTargetY();

            // Only move upward
            if (targetY > _highestReachedY)
                _highestReachedY = targetY;

            float desiredY = Mathf.Max(_highestReachedY + verticalOffset, minCameraY);

            // Smooth interpolation
            float newY = Mathf.Lerp(
                transform.position.y,
                desiredY,
                followSpeed * Time.deltaTime
            );

            transform.position = new Vector3(
                transform.position.x,
                newY,
                transform.position.z
            );
        }

        // ── Helpers ───────────────────────────────────────────────────────────────
        private float GetTargetY()
        {
            float playerY = playerTransform != null ? playerTransform.position.y : float.MinValue;
            float aiY = aiPlayerTransform != null ? aiPlayerTransform.position.y : float.MinValue;
            return Mathf.Max(playerY, aiY);
        }

        /// <summary>
        /// Resets the camera to a given position instantly (used at game start / restart).
        /// </summary>
        public void ResetTo(float worldY)
        {
            _highestReachedY = worldY;
            transform.position = new Vector3(
                transform.position.x,
                worldY + verticalOffset,
                transform.position.z
            );
        }

        /// <summary>Top of the visible area in world space.</summary>
        public float TopY => transform.position.y + Camera.main.orthographicSize;

        /// <summary>Bottom of the visible area in world space.</summary>
        public float BottomY => transform.position.y - Camera.main.orthographicSize;
    }
}
