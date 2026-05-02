using UnityEngine;

namespace DoodleClimb.Game
{
    /// <summary>
    /// Smoothly follows the highest active character upward (camera never moves down).
    ///
    /// Features:
    ///   • Dynamic zoom — zooms out ONLY when the PLAYER falls below the camera edge
    ///   • Screen shake — ShakeCamera(intensity, duration) for punchy death / spring hits
    ///   • FollowMode — Both | PlayerOnly | AIOnly
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
        [Tooltip("Zoom out automatically to keep the PLAYER on screen in vs-AI mode.")]
        public bool enableDynamicZoom = true;

        [Tooltip("Smallest orthographic size (closest zoom).")]
        public float minOrthographicSize = 9f;

        [Tooltip("Largest orthographic size (furthest zoom).")]
        public float maxOrthographicSize = 16f;

        [Tooltip("Extra bottom padding when computing required zoom.")]
        public float zoomPadding = 2f;

        [Tooltip("How quickly the zoom lerps to its target.")]
        [Range(1f, 10f)]
        public float zoomSpeed = 3f;

        [Header("Screen Shake")]
        [Range(0f, 1f)]
        public float shakeDecay = 8f;

        // ── Internal ──────────────────────────────────────────────────────────────
        private float  _highestReachedY;
        private Camera _cam;

        // Shake state
        private float   _shakeIntensity;
        private float   _shakeDuration;
        private float   _shakeTimer;
        private Vector3 _shakeOffset;

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

            // ── Follow ─────────────────────────────────────────────────────────────
            float targetY = GetFocusY();

            if (targetY > _highestReachedY)
                _highestReachedY = targetY;

            float desiredY = Mathf.Max(_highestReachedY + verticalOffset, minCameraY);

            float newY = Mathf.Lerp(
                transform.position.y,
                desiredY,
                followSpeed * Time.deltaTime);

            // ── Screen shake ───────────────────────────────────────────────────────
            UpdateShake();

            transform.position = new Vector3(
                transform.position.x + _shakeOffset.x,
                newY                 + _shakeOffset.y,
                transform.position.z);

            // ── Dynamic zoom ───────────────────────────────────────────────────────
            if (enableDynamicZoom
                && followMode == FollowMode.Both
                && playerTransform    != null && playerTransform.gameObject.activeSelf
                && aiPlayerTransform  != null && aiPlayerTransform.gameObject.activeSelf)
            {
                UpdateDynamicZoom();
            }
            else
            {
                _cam.orthographicSize = Mathf.Lerp(
                    _cam.orthographicSize,
                    minOrthographicSize,
                    zoomSpeed * Time.deltaTime);
            }
        }

        // ── Screen shake ──────────────────────────────────────────────────────────
        /// <summary>
        /// Trigger a camera shake.
        /// intensity: maximum pixel offset in world units.
        /// duration: seconds the shake runs before dying out.
        /// </summary>
        public void ShakeCamera(float intensity, float duration)
        {
            _shakeIntensity = Mathf.Max(_shakeIntensity, intensity);
            _shakeDuration  = duration;
            _shakeTimer     = duration;
        }

        private void UpdateShake()
        {
            if (_shakeTimer <= 0f)
            {
                _shakeOffset    = Vector3.zero;
                _shakeIntensity = 0f;
                return;
            }

            _shakeTimer -= Time.deltaTime;

            // Intensity decays over the duration
            float t       = _shakeTimer / Mathf.Max(0.001f, _shakeDuration);
            float current = _shakeIntensity * t;

            _shakeOffset = (Vector3)Random.insideUnitCircle * current;

            // Also decay stored intensity
            _shakeIntensity = Mathf.Lerp(_shakeIntensity, 0f, shakeDecay * Time.deltaTime);
        }

        // ── Dynamic zoom ──────────────────────────────────────────────────────────
        private void UpdateDynamicZoom()
        {
            // Zoom out ONLY to keep the PLAYER in view.
            // AI falling behind does not trigger zoom.
            float playerY   = playerTransform.position.y;
            float camBottom = transform.position.y - _cam.orthographicSize;

            if (playerY < camBottom + zoomPadding)
            {
                float offset   = transform.position.y - playerY;
                float required = Mathf.Max(minOrthographicSize, offset + zoomPadding);
                float target   = Mathf.Clamp(required, minOrthographicSize, maxOrthographicSize);

                _cam.orthographicSize = Mathf.Lerp(
                    _cam.orthographicSize, target, zoomSpeed * Time.deltaTime);
            }
            else
            {
                _cam.orthographicSize = Mathf.Lerp(
                    _cam.orthographicSize, minOrthographicSize, zoomSpeed * Time.deltaTime);
            }
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
        public void ResetTo(float worldY)
        {
            _highestReachedY = worldY;
            _shakeTimer      = 0f;
            _shakeIntensity  = 0f;
            _shakeOffset     = Vector3.zero;
            transform.position = new Vector3(
                transform.position.x,
                worldY + verticalOffset,
                transform.position.z);
            if (_cam != null) _cam.orthographicSize = minOrthographicSize;
        }

        public void SetFollowMode(FollowMode mode) => followMode = mode;

        public float TopY    => transform.position.y + (_cam != null ? _cam.orthographicSize : 0f);
        public float BottomY => transform.position.y - (_cam != null ? _cam.orthographicSize : 0f);
    }
}
