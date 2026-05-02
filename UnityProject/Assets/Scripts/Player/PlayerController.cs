using UnityEngine;

namespace DoodleClimb.Player
{
    /// <summary>
    /// Controls the human player character.
    ///
    /// Gameplay:
    ///   - Touch / tilt / keyboard input → horizontal movement
    ///   - Auto-jump on platform landing; jumpMultiplier supports Spring platforms
    ///   - Fall gravity multiplier for snappy arc
    ///   - Screen wrap
    ///
    /// Feel:
    ///   - Squash on landing, stretch on jump (applied to "Visual" child transform)
    ///   - Landing / death particle effects via VisualEffects singleton
    ///   - Combo counter — increments per landing, resets on death
    ///
    /// AI recording:
    ///   - Notifies GameManager of jumps and landings for AIRecorder capture
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Movement")]
        public float moveSpeed          = 6f;
        public float jumpForce          = 18f;
        public float fallGravityMultiplier = 2.5f;
        public bool  useTiltInput       = false;
        public float tiltSensitivity    = 3f;

        [Header("Screen Wrap")]
        public bool screenWrap = true;

        [Header("Squash & Stretch")]
        [Range(5f, 20f)]
        public float squashRestoreSpeed = 12f;

        // ── Internal — physics ────────────────────────────────────────────────────
        private Rigidbody2D _rb;
        private float _horizontalInput;
        private bool  _isAlive       = true;
        private bool  _inputEnabled  = true;
        private float _halfScreenWidth;

        // ── Internal — visual ─────────────────────────────────────────────────────
        private Transform      _visual;
        private SpriteRenderer _visualSR;
        private Vector3        _normalScale;    // visual's scale at scene start
        private Vector3        _squashScale = Vector3.one;

        // ── Internal — combo ──────────────────────────────────────────────────────
        private int   _combo;
        private int[] _comboThresholds = { 3, 5, 8, 10, 15 };

        // ── Internal — recording ──────────────────────────────────────────────────
        private float  _lastLandTime;
        private string _lastPlatformType = "none";
        private bool   _encounteredMovingPlatform;
        private float  _movingPlatformEncounteredTime;

        // ── References ────────────────────────────────────────────────────────────
        private AI.AIRecorder    _recorder;
        private Game.GameManager _gameManager;

        // ── Events ────────────────────────────────────────────────────────────────
        public System.Action<string> OnLandedOnPlatform;
        public System.Action<int>    OnComboChanged;
        public System.Action         OnDied;

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            _rb              = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 1f;
            _rb.constraints  = RigidbodyConstraints2D.FreezeRotation;
            _recorder        = FindObjectOfType<AI.AIRecorder>();
            _gameManager     = FindObjectOfType<Game.GameManager>();

            // Visual child for squash / stretch
            Transform v = transform.Find("Visual");
            _visual      = v != null ? v : transform;
            _visualSR    = _visual.GetComponent<SpriteRenderer>();
            _normalScale = _visual.localScale;
            _squashScale = Vector3.one;
        }

        private void Start()
        {
            Camera cam = Camera.main;
            _halfScreenWidth = cam != null
                ? cam.orthographicSize * cam.aspect
                : 5f;
            _lastLandTime = Time.time;
        }

        private void Update()
        {
            if (!_isAlive) return;
            if (_inputEnabled) ReadInput();
            ApplyFallGravity();
            WrapScreen();
            UpdateSquash();
            RecordFrame();
        }

        private void FixedUpdate()
        {
            if (!_isAlive || !_inputEnabled) return;
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
                    _horizontalInput = Input.GetTouch(0).position.x < Screen.width * 0.5f
                        ? -1f : 1f;
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

        // ── Squash & Stretch ──────────────────────────────────────────────────────
        private void UpdateSquash()
        {
            _squashScale = Vector3.Lerp(_squashScale, Vector3.one,
                                        squashRestoreSpeed * Time.deltaTime);
            if (_visual != null)
                _visual.localScale = new Vector3(
                    _normalScale.x * _squashScale.x,
                    _normalScale.y * _squashScale.y,
                    1f);
        }

        private void ApplySquash(float xMul, float yMul)
        {
            _squashScale = new Vector3(xMul, yMul, 1f);
        }

        // ── Jump ──────────────────────────────────────────────────────────────────
        private void Jump(float multiplier = 1f)
        {
            ApplySquash(0.72f, 1.32f); // stretch upward
            float jumpDelay = Time.time - _lastLandTime;
            _rb.velocity    = new Vector2(_rb.velocity.x, jumpForce * multiplier);

            Game.VisualEffects.Instance?.PlayJumpDust(
                transform.position,
                CharacterColor);

            _gameManager?.NotifyPlayerJumped(
                transform.position.x,
                transform.position.y,
                _rb.velocity.x,
                jumpDelay,
                _lastPlatformType);
        }

        // ── Landing callback (called by Platform.cs) ─────────────────────────────
        public void OnLanded(
            string platformType, bool isMovingPlatform,
            float platformCentreX, float jumpMultiplier = 1f)
        {
            _lastLandTime     = Time.time;
            _lastPlatformType = platformType;

            // Squash — harder for Spring
            float squashY = platformType == "Spring" ? 0.55f : 0.65f;
            ApplySquash(1.38f, squashY);

            // Landing particles
            Game.VisualEffects.Instance?.PlayLandDust(
                transform.position, CharacterColor);

            if (isMovingPlatform && _encounteredMovingPlatform)
            {
                float reactionTime = Time.time - _movingPlatformEncounteredTime;
                _encounteredMovingPlatform = false;
                _recorder?.RecordReactionTime(reactionTime);
            }

            // Combo
            _combo++;
            CheckComboThreshold();
            OnComboChanged?.Invoke(_combo);

            _gameManager?.NotifyPlayerLanded(transform.position.x, platformCentreX);
            OnLandedOnPlatform?.Invoke(platformType);
            Jump(jumpMultiplier);
        }

        private void CheckComboThreshold()
        {
            foreach (int threshold in _comboThresholds)
            {
                if (_combo == threshold)
                {
                    Game.VisualEffects.Instance?.PlayComboFlash(
                        transform.position,
                        _combo >= 10 ? Color.red
                      : _combo >=  5 ? Color.yellow
                      :                Color.cyan);
                    break;
                }
            }
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
            _combo       = 0;
            OnComboChanged?.Invoke(0);

            Game.VisualEffects.Instance?.PlayDeathBurst(
                transform.position, CharacterColor);

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
            _combo             = 0;
            _squashScale       = Vector3.one;
            if (_visual != null) _visual.localScale = _normalScale;
        }

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
        public int         Combo           => _combo;
        public Color CharacterColor =>
            _visualSR != null ? _visualSR.color : new Color(0.18f, 0.80f, 0.44f);
    }
}
