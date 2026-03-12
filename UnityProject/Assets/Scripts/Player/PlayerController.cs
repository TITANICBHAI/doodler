using UnityEngine;

namespace DoodleClimb.Player
{
    /// <summary>
    /// Controls the human player character.
    ///
    /// Responsibilities:
    ///   - Touch drag / tilt input → horizontal movement
    ///   - Auto-jump the instant the player lands on a platform
    ///   - Fall gravity multiplier for a satisfying arc
    ///   - Screen wrapping (exit right → enter left, and vice-versa)
    ///   - Notifies GameManager of jumps and landings so AIRecorder
    ///     can capture both behaviour and environment state together
    ///
    /// Watch AI mode:
    ///   Call SetInputEnabled(false) to freeze input and physics.
    ///   The player remains at its spawn position while the AI plays.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Movement")]
        [Tooltip("Horizontal move speed in units/second.")]
        public float moveSpeed = 6f;

        [Tooltip("Velocity applied upward on each jump.")]
        public float jumpForce = 18f;

        [Tooltip("Extra gravity multiplier while falling — makes falls feel punchy.")]
        public float fallGravityMultiplier = 2.5f;

        [Tooltip("Use device tilt instead of screen touch drag.")]
        public bool useTiltInput = false;

        [Tooltip("Sensitivity when using tilt input.")]
        public float tiltSensitivity = 3f;

        [Header("Screen Wrap")]
        public bool screenWrap = true;

        // ── Internal state ────────────────────────────────────────────────────────
        private Rigidbody2D _rb;
        private float _horizontalInput;
        private bool  _isAlive        = true;
        private bool  _inputEnabled   = true;   // false in Watch AI mode

        // Tracked for AI recording
        private float  _lastLandTime;
        private string _lastPlatformType = "none";

        // Moving platform reaction tracking
        private bool  _encounteredMovingPlatform;
        private float _movingPlatformEncounteredTime;

        private float _halfScreenWidth;

        // ── References ────────────────────────────────────────────────────────────
        private AI.AIRecorder    _recorder;
        private Game.GameManager _gameManager;

        // ── Events ────────────────────────────────────────────────────────────────
        public System.Action<string> OnLandedOnPlatform;
        public System.Action         OnDied;

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            _rb              = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 1f;
            _rb.constraints  = RigidbodyConstraints2D.FreezeRotation;
            _recorder        = FindObjectOfType<AI.AIRecorder>();
            _gameManager     = FindObjectOfType<Game.GameManager>();
        }

        private void Start()
        {
            Camera cam = Camera.main;
            if (cam != null)
                _halfScreenWidth = cam.orthographicSize * cam.aspect;
            else
            {
                _halfScreenWidth = 5f;
                Debug.LogWarning("[PlayerController] Camera.main is null in Start(). " +
                                 "Using default half-screen width 5.");
            }
            _lastLandTime = Time.time;
        }

        private void Update()
        {
            if (!_isAlive) return;
            if (_inputEnabled) ReadInput();
            ApplyFallGravity();
            WrapScreen();
            RecordFrame();
        }

        private void FixedUpdate()
        {
            if (!_isAlive)        return;
            if (!_inputEnabled)   return;
            _rb.velocity = new Vector2(_horizontalInput * moveSpeed, _rb.velocity.y);
        }

        // ── Input ─────────────────────────────────────────────────────────────────
        private void ReadInput()
        {
            if (useTiltInput)
            {
                _horizontalInput = Mathf.Clamp(
                    Input.acceleration.x * tiltSensitivity, -1f, 1f);
            }
            else
            {
                if (Input.touchCount > 0)
                {
                    Touch t = Input.GetTouch(0);
                    _horizontalInput = t.position.x < Screen.width * 0.5f ? -1f : 1f;
                }
                else
                {
                    _horizontalInput = Input.GetAxisRaw("Horizontal");
                }
            }
        }

        // ── Physics helpers ───────────────────────────────────────────────────────
        private void ApplyFallGravity()
        {
            if (_rb.velocity.y < 0f)
            {
                _rb.velocity += Vector2.up
                    * Physics2D.gravity.y
                    * (fallGravityMultiplier - 1f)
                    * Time.deltaTime;
            }
        }

        private void WrapScreen()
        {
            if (!screenWrap) return;
            Vector3 pos = transform.position;
            if      (pos.x >  _halfScreenWidth + 0.5f) pos.x = -_halfScreenWidth - 0.5f;
            else if (pos.x < -_halfScreenWidth - 0.5f) pos.x =  _halfScreenWidth + 0.5f;
            transform.position = pos;
        }

        // ── Jump ──────────────────────────────────────────────────────────────────
        private void Jump()
        {
            float jumpDelay = Time.time - _lastLandTime;
            _rb.velocity    = new Vector2(_rb.velocity.x, jumpForce);

            _gameManager?.NotifyPlayerJumped(
                transform.position.x,
                transform.position.y,
                _rb.velocity.x,
                jumpDelay,
                _lastPlatformType);
        }

        // ── Landing callback (called by Platform.cs) ─────────────────────────────
        public void OnLanded(string platformType, bool isMovingPlatform, float platformCentreX)
        {
            _lastLandTime     = Time.time;
            _lastPlatformType = platformType;

            if (isMovingPlatform && _encounteredMovingPlatform)
            {
                float reactionTime = Time.time - _movingPlatformEncounteredTime;
                _encounteredMovingPlatform = false;
                _recorder?.RecordReactionTime(reactionTime);
            }

            _gameManager?.NotifyPlayerLanded(transform.position.x, platformCentreX);

            OnLandedOnPlatform?.Invoke(platformType);
            Jump();
        }

        public void NotifyMovingPlatformNearby()
        {
            if (!_encounteredMovingPlatform)
            {
                _encounteredMovingPlatform     = true;
                _movingPlatformEncounteredTime = Time.time;
            }
        }

        // ── Death / Revive ────────────────────────────────────────────────────────
        public void Die()
        {
            if (!_isAlive) return;
            _isAlive     = false;
            _rb.velocity = Vector2.zero;
            _rb.bodyType = RigidbodyType2D.Kinematic;
            OnDied?.Invoke();
        }

        public void Revive(Vector3 spawnPosition)
        {
            transform.position = spawnPosition;
            _rb.bodyType       = RigidbodyType2D.Dynamic;
            _rb.velocity       = Vector2.zero;
            _isAlive           = true;
            _inputEnabled      = true;
            _lastLandTime      = Time.time;
        }

        /// <summary>
        /// Enable or disable player input and physics.
        /// Pass false in Watch AI mode to freeze the player at the spawn point.
        /// </summary>
        public void SetInputEnabled(bool enabled)
        {
            _inputEnabled = enabled;
            if (!enabled)
            {
                _rb.bodyType = RigidbodyType2D.Kinematic;
                _rb.velocity = Vector2.zero;
            }
            else
            {
                if (_isAlive) _rb.bodyType = RigidbodyType2D.Dynamic;
            }
        }

        // ── Per-frame recording ───────────────────────────────────────────────────
        private void RecordFrame()
        {
            _recorder?.RecordFrame(
                transform.position.x,
                transform.position.y,
                _rb.velocity.x,
                _rb.velocity.y);
        }

        // ── Public getters ────────────────────────────────────────────────────────
        public float       HorizontalInput => _horizontalInput;
        public bool        IsAlive         => _isAlive;
        public bool        InputEnabled    => _inputEnabled;
        public Rigidbody2D Rigidbody       => _rb;
        public float       CurrentHeight   => transform.position.y;
    }
}
