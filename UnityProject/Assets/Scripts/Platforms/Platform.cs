using UnityEngine;

namespace DoodleClimb.Platforms
{
    /// <summary>
    /// Represents a single platform in the level.
    /// Handles static, moving, breakable, and temporary behaviours.
    /// Calls PlayerController.OnLanded (or AIPlayerController equivalent) when
    /// the player touches the top surface.  Passes the platform's centre X so
    /// the AIRecorder can compute landing accuracy.
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
        private Vector3         _startPosition;
        private int             _moveDirection = 1;
        private bool            _broken        = false;
        private bool            _fadingOut     = false;
        private float           _spawnTime;
        private SpriteRenderer  _spriteRenderer;
        private Collider2D      _collider;
        private PlatformSpawner _spawner;

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

            // Notify any nearby player that a moving platform is close
            Player.PlayerController nearby = FindNearbyPlayer();
            if (nearby != null)
                nearby.NotifyMovingPlatformNearby();
        }

        private Player.PlayerController FindNearbyPlayer()
        {
            // Simple distance check — cheaper than Physics2D.OverlapCircle every frame
            Player.PlayerController p = FindObjectOfType<Player.PlayerController>();
            if (p == null) return null;
            float dist = Mathf.Abs(p.transform.position.y - transform.position.y);
            return dist < 4f ? p : null;
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

            // Only trigger when the character is falling onto the top surface
            foreach (ContactPoint2D contact in collision.contacts)
            {
                if (contact.normal.y < 0.5f) return; // not landing on top
            }

            float centreX = transform.position.x;

            Player.PlayerController player =
                collision.gameObject.GetComponent<Player.PlayerController>();
            if (player != null)
            {
                player.OnLanded(platformType.ToString(), platformType == PlatformType.Moving, centreX);
                if (platformType == PlatformType.Breakable)
                    Invoke(nameof(BreakPlatform), breakDelay);
                return;
            }

            AI.AIPlayerController aiPlayer =
                collision.gameObject.GetComponent<AI.AIPlayerController>();
            if (aiPlayer != null)
            {
                aiPlayer.OnLandedOnPlatform(
                    platformType.ToString(), platformType == PlatformType.Moving);
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
            float t      = 0f;
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
        public PlatformType Type    => platformType;
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
