using UnityEngine;

namespace DoodleClimb.Player
{
    /// <summary>
    /// Controls the human player character.
    /// Handles left/right input (touch or tilt), auto-jump on platform landing,
    /// gravity, screen wrapping, and notifies the AIRecorder each frame.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────────────────────────
        [Header("Movement")]
        [Tooltip("Horizontal move speed in units/second.")]
        public float moveSpeed = 6f;

        [Tooltip("Velocity applied upward on each jump.")]
        public float jumpForce = 18f;

        [Tooltip("Extra gravity multiplier applied while falling (makes falls feel snappy).")]
        public float fallGravityMultiplier = 2.5f;

        [Tooltip("Use device tilt instead of screen touch drag.")]
        public bool useTiltInput = false;

        [Tooltip("Sensitivity when using tilt input.")]
        public float tiltSensitivity = 3f;

        [Header("Screen Wrap")]
        [Tooltip("When the player exits one side of the screen they appear on the other.")]
        public bool screenWrap = true;

        // ── Internal state ────────────────────────────────────────────────────────
        private Rigidbody2D _rb;
        private float _horizontalInput;
        private bool _isAlive = true;

        // Tracked for AI recording
        private float _lastJumpTime;
        private float _lastLandTime;
        private string _lastPlatformType = "none";
        private float _movingPlatformReactionTime;
        private float _movingPlatformEncounteredTime;
        private bool _encounteredMovingPlatform;

        // Screen boundary cache
        private float _halfScreenWidth;

        // ── References ────────────────────────────────────────────────────────────
        private AI.AIRecorder _recorder;

        // ── Events ────────────────────────────────────────────────────────────────
        public System.Action<string> OnLandedOnPlatform;
        public System.Action OnDied;

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 1f;
            _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            _recorder = FindObjectOfType<AI.AIRecorder>();
        }

        private void Start()
        {
            _halfScreenWidth = Camera.main.orthographicSize * Camera.main.aspect;
            _lastJumpTime = Time.time;
        }

        private void Update()
        {
            if (!_isAlive) return;

            ReadInput();
            ApplyFallGravity();
            WrapScreen();
            RecordFrame();
        }

        private void FixedUpdate()
        {
            if (!_isAlive) return;

            // Apply horizontal velocity; preserve vertical velocity from physics
            _rb.linearVelocity = new Vector2(_horizontalInput * moveSpeed, _rb.linearVelocity.y);
        }

        // ── Input ─────────────────────────────────────────────────────────────────
        private void ReadInput()
        {
            if (useTiltInput)
            {
                // Tilt input via accelerometer (portrait orientation)
                _horizontalInput = Mathf.Clamp(Input.acceleration.x * tiltSensitivity, -1f, 1f);
            }
            else
            {
                // Touch drag: any active touch moves the character
                if (Input.touchCount > 0)
                {
                    Touch t = Input.GetTouch(0);
                    float screenMid = Screen.width * 0.5f;
                    _horizontalInput = t.position.x < screenMid ? -1f : 1f;
                }
                else if (Input.touchCount == 0)
                {
                    // Keyboard fallback for editor testing
                    _horizontalInput = Input.GetAxisRaw("Horizontal");
                }
            }
        }

        // ── Physics helpers ───────────────────────────────────────────────────────
        private void ApplyFallGravity()
        {
            // Extra gravity when falling so the arc feels punchy
            if (_rb.linearVelocity.y < 0f)
            {
                _rb.linearVelocity += Vector2.up
                    * Physics2D.gravity.y
                    * (fallGravityMultiplier - 1f)
                    * Time.deltaTime;
            }
        }

        private void WrapScreen()
        {
            if (!screenWrap) return;

            Vector3 pos = transform.position;
            if (pos.x > _halfScreenWidth + 0.5f)
                pos.x = -_halfScreenWidth - 0.5f;
            else if (pos.x < -_halfScreenWidth - 0.5f)
                pos.x = _halfScreenWidth + 0.5f;
            transform.position = pos;
        }

        // ── Jump ──────────────────────────────────────────────────────────────────
        public void Jump()
        {
            float jumpDelay = Time.time - _lastLandTime;
            _lastJumpTime = Time.time;

            _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, jumpForce);

            // Notify recorder
            if (_recorder != null)
            {
                _recorder.RecordJump(
                    transform.position.x,
                    transform.position.y,
                    _rb.linearVelocity.x,
                    jumpDelay,
                    _lastPlatformType
                );
            }
        }

        // ── Landing callback (called by Platform.cs) ─────────────────────────────
        public void OnLanded(string platformType, bool isMovingPlatform)
        {
            _lastLandTime = Time.time;
            _lastPlatformType = platformType;

            if (isMovingPlatform && _encounteredMovingPlatform)
            {
                _movingPlatformReactionTime = Time.time - _movingPlatformEncounteredTime;
                _encounteredMovingPlatform = false;

                if (_recorder != null)
                    _recorder.RecordReactionTime(_movingPlatformReactionTime);
            }

            OnLandedOnPlatform?.Invoke(platformType);
            Jump(); // Auto-jump on landing
        }

        // Called when a moving platform first enters the player's vicinity
        public void NotifyMovingPlatformNearby()
        {
            if (!_encounteredMovingPlatform)
            {
                _encounteredMovingPlatform = true;
                _movingPlatformEncounteredTime = Time.time;
            }
        }

        // ── Death ─────────────────────────────────────────────────────────────────
        public void Die()
        {
            if (!_isAlive) return;
            _isAlive = false;
            _rb.linearVelocity = Vector2.zero;
            _rb.bodyType = RigidbodyType2D.Kinematic;
            OnDied?.Invoke();
        }

        public void Revive(Vector3 spawnPosition)
        {
            transform.position = spawnPosition;
            _rb.bodyType = RigidbodyType2D.Dynamic;
            _rb.linearVelocity = Vector2.zero;
            _isAlive = true;
            _lastJumpTime = Time.time;
            _lastLandTime = Time.time;
        }

        // ── AI recording helper ───────────────────────────────────────────────────
        private void RecordFrame()
        {
            if (_recorder == null) return;
            _recorder.RecordFrame(
                transform.position.x,
                transform.position.y,
                _rb.linearVelocity.x,
                _rb.linearVelocity.y
            );
        }

        // ── Public getters ────────────────────────────────────────────────────────
        public float HorizontalInput => _horizontalInput;
        public bool IsAlive => _isAlive;
        public Rigidbody2D Rigidbody => _rb;
        public float CurrentHeight => transform.position.y;
    }
}
