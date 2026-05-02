using UnityEngine;

namespace DoodleClimb.Platforms
{
    /// <summary>
    /// Represents a single platform.
    ///
    /// Types:
    ///   Static    — classic solid platform, green
    ///   Moving    — oscillates horizontally, blue
    ///   Breakable — collapses after one landing, red-orange
    ///   Temporary — fades and vanishes after a few seconds, amber
    ///   Spring    — gives a massive double-jump boost, bright yellow, animated bounce
    ///
    /// Squash animation:
    ///   When a character lands, the platform squashes briefly in Y then springs back.
    ///
    /// Calls PlayerController.OnLanded / AIPlayerController.OnLandedOnPlatform
    /// with a jumpMultiplier (1f for most types, 2.5f for Spring).
    /// </summary>
    public class Platform : MonoBehaviour
    {
        // ── Types ─────────────────────────────────────────────────────────────────
        public enum PlatformType { Static, Moving, Breakable, Temporary, Spring }

        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Type")]
        public PlatformType platformType = PlatformType.Static;

        [Header("Moving Platform")]
        public float moveRange = 2f;
        public float moveSpeed = 2f;

        [Header("Breakable Platform")]
        [Tooltip("Seconds between first contact and collapse.")]
        public float breakDelay = 0.15f;

        [Header("Temporary Platform")]
        public float visibleDuration = 2f;
        public float fadeDuration    = 0.5f;

        [Header("Spring Platform")]
        [Tooltip("Jump force multiplier applied to any character that lands on this.")]
        public float springMultiplier = 2.5f;

        // ── Internal state ────────────────────────────────────────────────────────
        private Vector3        _startPosition;
        private float          _baseScaleX;
        private float          _baseScaleY;

        private bool           _broken    = false;
        private bool           _fadingOut = false;
        private float          _spawnTime;

        // Spring squash animation
        private float          _platformSquash       = 1f;   // current Y scale multiplier
        private const float    SquashAmount           = 0.65f;
        private const float    SquashRestoreSpeed     = 14f;

        private SpriteRenderer _spriteRenderer;
        private Collider2D     _collider;
        private PlatformSpawner _spawner;

        // Cached player reference
        private Player.PlayerController _cachedPlayer;

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            _collider       = GetComponent<Collider2D>();
            _spawner        = FindObjectOfType<PlatformSpawner>();
        }

        private void OnEnable()
        {
            _startPosition = transform.position;
            _baseScaleX    = transform.localScale.x;
            _baseScaleY    = transform.localScale.y;
            _broken        = false;
            _fadingOut     = false;
            _spawnTime     = Time.time;
            _platformSquash = 1f;

            if (_spriteRenderer != null)
            {
                Color c = _spriteRenderer.color;
                c.a = 1f;
                _spriteRenderer.color = c;
            }
            if (_collider != null) _collider.enabled = true;

            ApplyVisualStyle();
        }

        private void Start()
        {
            _cachedPlayer = FindObjectOfType<Player.PlayerController>();
        }

        private void Update()
        {
            switch (platformType)
            {
                case PlatformType.Moving:    UpdateMoving();    break;
                case PlatformType.Temporary: UpdateTemporary(); break;
                case PlatformType.Spring:    UpdateSpring();    break;
            }
            UpdateSquash();
        }

        // ── Behaviour updates ─────────────────────────────────────────────────────
        private void UpdateMoving()
        {
            float newX = Mathf.PingPong(
                (Time.time - _spawnTime) * moveSpeed,
                moveRange * 2f
            ) - moveRange + _startPosition.x;

            transform.position = new Vector3(newX, transform.position.y, 0f);

            if (_cachedPlayer != null)
            {
                float vertDist = Mathf.Abs(_cachedPlayer.transform.position.y
                                           - transform.position.y);
                if (vertDist < 4f)
                    _cachedPlayer.NotifyMovingPlatformNearby();
            }
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
                if (t >= 1f) Recycle();
            }
        }

        private void UpdateSpring()
        {
            // Gentle breathing bob — indicates this platform is "alive"
            float bob = 1f + Mathf.Sin(Time.time * 4f + _spawnTime) * 0.06f;
            SetScaleY(_baseScaleY * bob * _platformSquash);
        }

        private void UpdateSquash()
        {
            if (Mathf.Abs(_platformSquash - 1f) < 0.001f) return;
            _platformSquash = Mathf.Lerp(_platformSquash, 1f, SquashRestoreSpeed * Time.deltaTime);
            if (platformType != PlatformType.Spring)
                SetScaleY(_baseScaleY * _platformSquash);
        }

        private void SetScaleY(float y)
        {
            transform.localScale = new Vector3(_baseScaleX, y, 1f);
        }

        // ── Collision ─────────────────────────────────────────────────────────────
        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (_broken) return;

            bool topContact = false;
            foreach (ContactPoint2D contact in collision.contacts)
            {
                if (contact.normal.y > 0.5f) { topContact = true; break; }
            }
            if (!topContact) return;

            // Squash the platform on impact
            _platformSquash = SquashAmount;

            float centreX      = transform.position.x;
            float jumpMult     = platformType == PlatformType.Spring ? springMultiplier : 1f;
            bool  isMoving     = platformType == PlatformType.Moving;

            // Spring effect
            if (platformType == PlatformType.Spring)
                Game.VisualEffects.Instance?.PlaySpringBounce(
                    transform.position + Vector3.up * (_baseScaleY * 0.5f));

            // ── Human player ──────────────────────────────────────────────────────
            Player.PlayerController player =
                collision.gameObject.GetComponent<Player.PlayerController>();
            if (player != null)
            {
                player.OnLanded(platformType.ToString(), isMoving, centreX, jumpMult);

                if (platformType == PlatformType.Breakable)
                    Invoke(nameof(BreakPlatform), breakDelay);
                return;
            }

            // ── AI player ─────────────────────────────────────────────────────────
            AI.AIPlayerController aiPlayer =
                collision.gameObject.GetComponent<AI.AIPlayerController>();
            if (aiPlayer != null)
            {
                aiPlayer.OnLandedOnPlatform(platformType.ToString(), isMoving, jumpMult);

                if (platformType == PlatformType.Breakable)
                    Invoke(nameof(BreakPlatform), breakDelay);
            }
        }

        // ── Break logic ───────────────────────────────────────────────────────────
        private void BreakPlatform()
        {
            _broken = true;
            if (_collider != null) _collider.enabled = false;

            if (_spriteRenderer != null) StartCoroutine(ShakeAndRecycle());
            else                         Recycle();
        }

        private System.Collections.IEnumerator ShakeAndRecycle()
        {
            float   t    = 0f;
            Vector3 orig = transform.position;
            while (t < 0.25f)
            {
                t += Time.deltaTime;
                transform.position = orig + (Vector3)(Random.insideUnitCircle * 0.09f);
                yield return null;
            }
            Recycle();
        }

        // ── Recycling ─────────────────────────────────────────────────────────────
        private void Recycle()
        {
            if (_spawner != null) _spawner.RecyclePlatform(this);
            else                  gameObject.SetActive(false);
        }

        // ── Visual theming ────────────────────────────────────────────────────────
        private void ApplyVisualStyle()
        {
            if (_spriteRenderer == null) return;
            switch (platformType)
            {
                case PlatformType.Static:
                    _spriteRenderer.color = new Color(0.20f, 0.78f, 0.40f); break; // vibrant green
                case PlatformType.Moving:
                    _spriteRenderer.color = new Color(0.22f, 0.62f, 0.95f); break; // sky blue
                case PlatformType.Breakable:
                    _spriteRenderer.color = new Color(0.90f, 0.32f, 0.18f); break; // orange-red
                case PlatformType.Temporary:
                    _spriteRenderer.color = new Color(0.92f, 0.70f, 0.08f); break; // amber
                case PlatformType.Spring:
                    _spriteRenderer.color = new Color(0.95f, 0.88f, 0.10f); break; // bright yellow
            }
        }

        // ── Public helpers ────────────────────────────────────────────────────────
        public PlatformType Type     => platformType;
        public bool         IsBroken => _broken;

        public float HorizontalVelocity
        {
            get
            {
                if (platformType != PlatformType.Moving) return 0f;
                float phase = Mathf.PingPong(
                    (Time.time - _spawnTime) * moveSpeed, moveRange * 2f);
                return phase < moveRange ? moveSpeed : -moveSpeed;
            }
        }
    }
}
