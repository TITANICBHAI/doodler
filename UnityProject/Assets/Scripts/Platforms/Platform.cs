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
    ///   Ice       — slippery: multiplies horizontal velocity on landing, light blue
    ///   Conveyor  — pushes the player horizontally on landing, orange; arrow shows direction
    ///   Bomb      — launches player upward at high speed then destroys itself, red
    ///   Rocket    — super-launch, fiery red-orange
    ///   Golden    — score bonus on landing, gold
    ///   Crumble   — crumbles quickly after landing, dark orange
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
        public enum PlatformType
        {
            Static, Moving, Breakable, Temporary, Spring,
            Ice, Conveyor, Bomb, Rocket, Golden, Crumble
        }

        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Type")]
        public PlatformType platformType = PlatformType.Static;

        [Header("Moving Platform")]
        public float moveRange = 2f;
        public float moveSpeed = 2f;

        [Header("Breakable / Crumble Platform")]
        [Tooltip("Seconds between first contact and collapse.")]
        public float breakDelay = 0.15f;

        [Header("Temporary Platform")]
        public float visibleDuration = 2f;
        public float fadeDuration    = 0.5f;

        [Header("Spring Platform")]
        [Tooltip("Jump force multiplier applied to any character that lands on this.")]
        public float springMultiplier = 2.5f;

        [Header("Ice Platform")]
        [Tooltip("Horizontal velocity multiplier applied on landing.")]
        public float iceSlipMultiplier = 1.65f;

        [Header("Conveyor Platform")]
        [Tooltip("Direction to push the player: +1 = right, -1 = left.")]
        public float conveyorDir = 1f;
        [Tooltip("Speed added to the player's horizontal velocity on landing.")]
        public float conveyorPushSpeed = 8f;
        [Tooltip("Duration the push effect lingers (seconds).")]
        public float conveyorPushDuration = 0.5f;

        [Header("Rocket / Bomb Platform")]
        public float rocketMultiplier = 3.5f;
        public float bombMultiplier   = 2.8f;

        [Header("Golden Platform")]
        public int goldenScoreBonus = 25;

        // ── Internal state ────────────────────────────────────────────────────────
        private Vector3        _startPosition;
        private float          _baseScaleX;
        private float          _baseScaleY;

        private bool           _broken    = false;
        private bool           _fadingOut = false;
        private float          _spawnTime;

        // Spring squash animation
        private float          _platformSquash       = 1f;
        private const float    SquashAmount           = 0.65f;
        private const float    SquashRestoreSpeed     = 14f;

        private SpriteRenderer _spriteRenderer;
        private Collider2D     _collider;
        private PlatformSpawner _spawner;

        // Arrow renderer for Conveyor visual
        private GameObject     _conveyorArrow;

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
            SetupConveyorArrow();
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
                case PlatformType.Conveyor:  UpdateConveyor();  break;
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
            float bob = 1f + Mathf.Sin(Time.time * 4f + _spawnTime) * 0.06f;
            SetScaleY(_baseScaleY * bob * _platformSquash);
        }

        private void UpdateConveyor()
        {
            // Animate the arrow by pulsing its alpha
            if (_conveyorArrow != null)
            {
                var sr = _conveyorArrow.GetComponent<SpriteRenderer>();
                if (sr != null)
                    sr.color = new Color(1f, 1f, 1f, 0.55f + Mathf.Sin(Time.time * 4f) * 0.35f);
            }
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

            _platformSquash = SquashAmount;

            float centreX  = transform.position.x;
            float jumpMult = GetJumpMultiplier();
            bool  isMoving = platformType == PlatformType.Moving;

            if (platformType == PlatformType.Spring)
                Game.VisualEffects.Instance?.PlaySpringBounce(
                    transform.position + Vector3.up * (_baseScaleY * 0.5f));

            // ── Human player ──────────────────────────────────────────────────────
            Player.PlayerController player =
                collision.gameObject.GetComponent<Player.PlayerController>();
            if (player != null)
            {
                player.OnLanded(platformType.ToString(), isMoving, centreX, jumpMult);
                ApplySpecialEffect(player, null);
                HandleBreak();
                return;
            }

            // ── AI player ─────────────────────────────────────────────────────────
            AI.AIPlayerController aiPlayer =
                collision.gameObject.GetComponent<AI.AIPlayerController>();
            if (aiPlayer != null)
            {
                aiPlayer.OnLandedOnPlatform(platformType.ToString(), isMoving, jumpMult);
                ApplySpecialEffect(null, aiPlayer);
                HandleBreak();
            }
        }

        // ── Special effects on landing ────────────────────────────────────────────
        private float GetJumpMultiplier()
        {
            return platformType switch
            {
                PlatformType.Spring => springMultiplier,
                PlatformType.Rocket => rocketMultiplier,
                PlatformType.Bomb   => bombMultiplier,
                _                   => 1f
            };
        }

        private void ApplySpecialEffect(Player.PlayerController player, AI.AIPlayerController ai)
        {
            switch (platformType)
            {
                case PlatformType.Ice:
                    if (player != null) player.ApplyIceSlip(iceSlipMultiplier);
                    if (ai     != null) ai.ApplyIceSlip(iceSlipMultiplier);
                    break;

                case PlatformType.Conveyor:
                    if (player != null)
                        player.ApplyConveyorPush(conveyorDir * conveyorPushSpeed, conveyorPushDuration);
                    if (ai != null)
                        ai.ApplyConveyorPush(conveyorDir * conveyorPushSpeed, conveyorPushDuration);
                    break;

                case PlatformType.Golden:
                    Game.GameManager.Instance?.AddScore(goldenScoreBonus);
                    Game.VisualEffects.Instance?.PlayGoldBurst(transform.position);
                    break;

                case PlatformType.Bomb:
                case PlatformType.Crumble:
                    // Break with a short delay
                    break;
            }
        }

        private void HandleBreak()
        {
            if (platformType == PlatformType.Breakable ||
                platformType == PlatformType.Bomb      ||
                platformType == PlatformType.Crumble)
            {
                float delay = (platformType == PlatformType.Crumble) ? breakDelay * 2f : breakDelay;
                Invoke(nameof(BreakPlatform), delay);
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

        // ── Conveyor arrow setup ──────────────────────────────────────────────────
        private void SetupConveyorArrow()
        {
            if (_conveyorArrow != null) Destroy(_conveyorArrow);
            if (platformType != PlatformType.Conveyor) return;

            _conveyorArrow = new GameObject("ConveyorArrow");
            _conveyorArrow.transform.SetParent(transform, false);
            _conveyorArrow.transform.localPosition = Vector3.zero;

            var sr = _conveyorArrow.AddComponent<SpriteRenderer>();
            // Flip based on direction
            _conveyorArrow.transform.localScale = new Vector3(conveyorDir, 1f, 1f);
            sr.sortingOrder = _spriteRenderer != null ? _spriteRenderer.sortingOrder + 1 : 1;
        }

        // ── Recycling ─────────────────────────────────────────────────────────────
        private void Recycle()
        {
            if (_conveyorArrow != null) { Destroy(_conveyorArrow); _conveyorArrow = null; }
            if (_spawner != null) _spawner.RecyclePlatform(this);
            else                  gameObject.SetActive(false);
        }

        // ── Visual theming ────────────────────────────────────────────────────────
        private void ApplyVisualStyle()
        {
            if (_spriteRenderer == null) return;
            _spriteRenderer.color = platformType switch
            {
                PlatformType.Static    => new Color(0.20f, 0.78f, 0.40f),
                PlatformType.Moving    => new Color(0.22f, 0.62f, 0.95f),
                PlatformType.Breakable => new Color(0.90f, 0.32f, 0.18f),
                PlatformType.Temporary => new Color(0.92f, 0.70f, 0.08f),
                PlatformType.Spring    => new Color(0.95f, 0.88f, 0.10f),
                PlatformType.Ice       => new Color(0.66f, 0.93f, 1.00f),
                PlatformType.Conveyor  => new Color(1.00f, 0.50f, 0.25f),
                PlatformType.Bomb      => new Color(1.00f, 0.20f, 0.20f),
                PlatformType.Rocket    => new Color(1.00f, 0.27f, 0.13f),
                PlatformType.Golden    => new Color(1.00f, 0.84f, 0.00f),
                PlatformType.Crumble   => new Color(1.00f, 0.42f, 0.13f),
                _                      => Color.white
            };
        }

        // ── Public helpers ────────────────────────────────────────────────────────
        public PlatformType Type     => platformType;
        public bool         IsBroken => _broken;

        public float HorizontalVelocity
        {
            get
            {
                if (platformType == PlatformType.Moving)
                {
                    float phase = Mathf.PingPong(
                        (Time.time - _spawnTime) * moveSpeed, moveRange * 2f);
                    return phase < moveRange ? moveSpeed : -moveSpeed;
                }
                if (platformType == PlatformType.Conveyor)
                    return conveyorDir * conveyorPushSpeed;
                return 0f;
            }
        }
    }
}
