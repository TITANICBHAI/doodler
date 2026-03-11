using UnityEngine;

namespace DoodleClimb.Platforms
{
    /// <summary>
    /// Represents a single platform in the level.
    /// Handles static, moving, breakable, and temporary behaviours.
    /// Calls PlayerController.OnLanded when the player touches it.
    /// </summary>
    public class Platform : MonoBehaviour
    {
        // ── Platform types ────────────────────────────────────────────────────────
        public enum PlatformType
        {
            Static,
            Moving,
            Breakable,
            Temporary
        }

        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Type")]
        public PlatformType platformType = PlatformType.Static;

        [Header("Moving Platform")]
        [Tooltip("Horizontal move distance from spawn point.")]
        public float moveRange = 2f;
        public float moveSpeed = 2f;

        [Header("Breakable Platform")]
        [Tooltip("Frames the platform survives after first contact before breaking.")]
        public float breakDelay = 0.15f;

        [Header("Temporary Platform")]
        [Tooltip("Seconds the temporary platform stays visible before fading.")]
        public float visibleDuration = 2f;
        public float fadeDuration = 0.5f;

        // ── Internal state ────────────────────────────────────────────────────────
        private Vector3 _startPosition;
        private int _moveDirection = 1;
        private bool _broken = false;
        private bool _fadingOut = false;
        private float _spawnTime;
        private SpriteRenderer _spriteRenderer;
        private Collider2D _collider;

        // ── References ────────────────────────────────────────────────────────────
        private PlatformSpawner _spawner;

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            _collider = GetComponent<Collider2D>();
            _spawner = FindObjectOfType<PlatformSpawner>();
        }

        private void OnEnable()
        {
            _startPosition = transform.position;
            _broken = false;
            _fadingOut = false;
            _spawnTime = Time.time;

            if (_spriteRenderer != null)
            {
                Color c = _spriteRenderer.color;
                c.a = 1f;
                _spriteRenderer.color = c;
            }

            if (_collider != null)
                _collider.enabled = true;

            ApplyVisualStyle();
        }

        private void Update()
        {
            switch (platformType)
            {
                case PlatformType.Moving:
                    UpdateMoving();
                    break;
                case PlatformType.Temporary:
                    UpdateTemporary();
                    break;
            }
        }

        // ── Behaviour updates ─────────────────────────────────────────────────────
        private void UpdateMoving()
        {
            float newX = Mathf.PingPong(
                (Time.time - _spawnTime) * moveSpeed,
                moveRange * 2f
            ) - moveRange + _startPosition.x;

            transform.position = new Vector3(newX, transform.position.y, 0f);
        }

        private void UpdateTemporary()
        {
            float age = Time.time - _spawnTime;

            if (!_fadingOut && age >= visibleDuration)
            {
                _fadingOut = true;
                if (_collider != null) _collider.enabled = false;
            }

            if (_fadingOut)
            {
                float t = (age - visibleDuration) / fadeDuration;
                if (_spriteRenderer != null)
                {
                    Color c = _spriteRenderer.color;
                    c.a = Mathf.Clamp01(1f - t);
                    _spriteRenderer.color = c;
                }

                if (t >= 1f)
                    Recycle();
            }
        }

        // ── Collision ─────────────────────────────────────────────────────────────
        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (_broken) return;

            // Only trigger when landing on the top surface
            if (collision.relativeVelocity.y < 0f) return;

            // Check for player or AI player
            Player.PlayerController player = collision.gameObject.GetComponent<Player.PlayerController>();
            if (player != null)
            {
                HandlePlayerLanding(player);
                return;
            }

            AI.AIPlayerController aiPlayer = collision.gameObject.GetComponent<AI.AIPlayerController>();
            if (aiPlayer != null)
            {
                HandleAILanding(aiPlayer);
            }
        }

        private void HandlePlayerLanding(Player.PlayerController player)
        {
            bool isMoving = platformType == PlatformType.Moving;
            player.OnLanded(platformType.ToString(), isMoving);

            if (platformType == PlatformType.Breakable)
                Invoke(nameof(BreakPlatform), breakDelay);
        }

        private void HandleAILanding(AI.AIPlayerController aiPlayer)
        {
            bool isMoving = platformType == PlatformType.Moving;
            aiPlayer.OnLandedOnPlatform(platformType.ToString(), isMoving);

            if (platformType == PlatformType.Breakable)
                Invoke(nameof(BreakPlatform), breakDelay);
        }

        // ── Break logic ───────────────────────────────────────────────────────────
        private void BreakPlatform()
        {
            _broken = true;
            if (_collider != null) _collider.enabled = false;

            // Brief visual shake before recycling
            if (_spriteRenderer != null)
                StartCoroutine(ShakeAndRecycle());
            else
                Recycle();
        }

        private System.Collections.IEnumerator ShakeAndRecycle()
        {
            float t = 0f;
            Vector3 origin = transform.position;
            while (t < 0.25f)
            {
                t += Time.deltaTime;
                transform.position = origin + (Vector3)Random.insideUnitCircle * 0.08f;
                yield return null;
            }
            Recycle();
        }

        // ── Recycling ─────────────────────────────────────────────────────────────
        private void Recycle()
        {
            if (_spawner != null)
                _spawner.RecyclePlatform(this);
            else
                gameObject.SetActive(false);
        }

        // ── Visual theming ────────────────────────────────────────────────────────
        private void ApplyVisualStyle()
        {
            if (_spriteRenderer == null) return;

            switch (platformType)
            {
                case PlatformType.Static:
                    _spriteRenderer.color = new Color(0.22f, 0.76f, 0.38f); // green
                    break;
                case PlatformType.Moving:
                    _spriteRenderer.color = new Color(0.20f, 0.55f, 0.90f); // blue
                    break;
                case PlatformType.Breakable:
                    _spriteRenderer.color = new Color(0.85f, 0.35f, 0.20f); // orange-red
                    break;
                case PlatformType.Temporary:
                    _spriteRenderer.color = new Color(0.80f, 0.60f, 0.10f); // yellow
                    break;
            }
        }

        // ── Public helpers ────────────────────────────────────────────────────────
        public PlatformType Type => platformType;
        public bool IsBroken => _broken;

        /// <summary>Returns the horizontal velocity of this platform (0 for non-moving).</summary>
        public float HorizontalVelocity
        {
            get
            {
                if (platformType != PlatformType.Moving) return 0f;
                float age = Time.time - _spawnTime;
                float phase = Mathf.PingPong(age * moveSpeed, moveRange * 2f);
                return (phase < moveRange) ? moveSpeed : -moveSpeed;
            }
        }
    }
}
