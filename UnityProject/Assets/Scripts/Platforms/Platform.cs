using UnityEngine;

namespace DoodleClimb.Platforms
{
    /// <summary>
    /// Represents a single platform in the level.
    /// Handles static, moving, breakable, and temporary behaviours.
    /// Calls PlayerController.OnLanded when the player lands on the top surface.
    /// Passes the platform's centre X so AIRecorder can compute landing accuracy.
    /// </summary>
    public class Platform : MonoBehaviour
    {
        // ── Platform types ────────────────────────────────────────────────────────
        public enum PlatformType { Static, Moving, Breakable, Temporary }

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

        // ── Internal state ────────────────────────────────────────────────────────
        private Vector3        _startPosition;
        private bool           _broken        = false;
        private bool           _fadingOut     = false;
        private float          _spawnTime;
        private SpriteRenderer _spriteRenderer;
        private Collider2D     _collider;
        private PlatformSpawner _spawner;

        // Cached player reference — found once and reused to avoid per-frame FindObjectOfType
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
            _broken        = false;
            _fadingOut     = false;
            _spawnTime     = Time.time;

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
            // Cache the player once at scene start (cheaper than FindObjectOfType every frame)
            _cachedPlayer = FindObjectOfType<Player.PlayerController>();
        }

        private void Update()
        {
            switch (platformType)
            {
                case PlatformType.Moving:    UpdateMoving();    break;
                case PlatformType.Temporary: UpdateTemporary(); break;
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

            // Notify nearby player that a moving platform is in range
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

        // ── Collision ─────────────────────────────────────────────────────────────
        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (_broken) return;

            // Only trigger when something lands on the TOP surface.
            // Scan all contact points; at least one must have an upward-facing normal
            // (normal.y > 0.5 means within ~60 degrees of straight up).
            // FIX: do NOT return inside the loop — check all contacts first.
            bool topContact = false;
            foreach (ContactPoint2D contact in collision.contacts)
            {
                if (contact.normal.y > 0.5f)
                {
                    topContact = true;
                    break;
                }
            }
            if (!topContact) return;

            float centreX = transform.position.x;

            // Check for human player
            Player.PlayerController player =
                collision.gameObject.GetComponent<Player.PlayerController>();
            if (player != null)
            {
                player.OnLanded(
                    platformType.ToString(),
                    platformType == PlatformType.Moving,
                    centreX);

                if (platformType == PlatformType.Breakable)
                    Invoke(nameof(BreakPlatform), breakDelay);
                return;
            }

            // Check for AI player
            AI.AIPlayerController aiPlayer =
                collision.gameObject.GetComponent<AI.AIPlayerController>();
            if (aiPlayer != null)
            {
                aiPlayer.OnLandedOnPlatform(
                    platformType.ToString(),
                    platformType == PlatformType.Moving);

                if (platformType == PlatformType.Breakable)
                    Invoke(nameof(BreakPlatform), breakDelay);
            }
        }

        // ── Break logic ───────────────────────────────────────────────────────────
        private void BreakPlatform()
        {
            _broken = true;
            if (_collider != null) _collider.enabled = false;

            if (_spriteRenderer != null)
                StartCoroutine(ShakeAndRecycle());
            else
                Recycle();
        }

        private System.Collections.IEnumerator ShakeAndRecycle()
        {
            float   t    = 0f;
            Vector3 orig = transform.position;
            while (t < 0.25f)
            {
                t += Time.deltaTime;
                transform.position = orig + (Vector3)(Random.insideUnitCircle * 0.08f);
                yield return null;
            }
            Recycle();
        }

        // ── Recycling ─────────────────────────────────────────────────────────────
        private void Recycle()
        {
            if (_spawner != null) _spawner.RecyclePlatform(this);
            else gameObject.SetActive(false);
        }

        // ── Visual theming ────────────────────────────────────────────────────────
        private void ApplyVisualStyle()
        {
            if (_spriteRenderer == null) return;
            switch (platformType)
            {
                case PlatformType.Static:    _spriteRenderer.color = new Color(0.22f, 0.76f, 0.38f); break; // green
                case PlatformType.Moving:    _spriteRenderer.color = new Color(0.20f, 0.55f, 0.90f); break; // blue
                case PlatformType.Breakable: _spriteRenderer.color = new Color(0.85f, 0.35f, 0.20f); break; // red
                case PlatformType.Temporary: _spriteRenderer.color = new Color(0.80f, 0.60f, 0.10f); break; // yellow
            }
        }

        // ── Public helpers ────────────────────────────────────────────────────────
        public PlatformType Type     => platformType;
        public bool         IsBroken => _broken;

        /// <summary>Current horizontal velocity (0 for non-moving platforms).</summary>
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
