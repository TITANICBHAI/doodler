using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DoodleClimb.AI
{
    /// <summary>
    /// Controls the AI clone using Ghost Replay and / or Behaviour AI.
    ///
    /// Feel improvements (matching PlayerController):
    ///   - Squash on landing, stretch on jump (Visual child transform)
    ///   - Landing particle effects via VisualEffects singleton
    ///   - Spring platform jump-force multiplier support
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class AIPlayerController : MonoBehaviour
    {
        // ── Mode selection ────────────────────────────────────────────────────────
        public enum AIMode { BehaviourProfile, GhostReplay, Hybrid }

        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("AI Mode")]
        public AIMode aiMode = AIMode.Hybrid;

        [Header("Profile Overrides (0 = use trained value)")]
        public float overrideMoveSpeed = 0f;
        public float overrideJumpDelay = 0f;

        [Header("Randomness")]
        [Range(0f, 1f)]
        public float noiseAmount = 0.15f;

        [Header("Physics")]
        public float jumpForce              = 18f;
        public float fallGravityMultiplier  = 2.5f;

        [Header("Platform Targeting")]
        public float maxReachableHorizontalDist = 7f;
        [Range(1f, 15f)]
        public float targetSmoothSpeed          = 6f;

        [Header("Squash & Stretch")]
        [Range(5f, 20f)]
        public float squashRestoreSpeed = 12f;

        // ── References ────────────────────────────────────────────────────────────
        private AIProfile                 _profile;
        private AIRecorder                _recorder;
        private Platforms.PlatformSpawner _spawner;
        private Rigidbody2D               _rb;

        // ── Visual ────────────────────────────────────────────────────────────────
        private Transform      _visual;
        private SpriteRenderer _visualSR;
        private Vector3        _normalScale;
        private Vector3        _squashScale = Vector3.one;

        // ── Behaviour state ───────────────────────────────────────────────────────
        private float _targetX;
        private float _rawTargetX;
        private float _lastLandTime;
        private bool  _wantsToJump;
        private bool  _isAlive;
        private float _pendingJumpMultiplier = 1f;

        // ── Ghost-replay state ────────────────────────────────────────────────────
        private List<GhostFrame> _ghostFrames;
        private int              _ghostIndex;
        private float            _replayStartTime;
        private bool             _ghostActive;

        // ── Moving platform ───────────────────────────────────────────────────────
        private bool  _movingPlatformNearby;
        private float _movingPlatformDetectedTime;

        // ── Events ────────────────────────────────────────────────────────────────
        public System.Action OnDied;

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            _rb             = GetComponent<Rigidbody2D>();
            _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            _recorder       = FindObjectOfType<AIRecorder>();
            _spawner        = FindObjectOfType<Platforms.PlatformSpawner>();

            Transform v = transform.Find("Visual");
            _visual      = v != null ? v : transform;
            _visualSR    = _visual.GetComponent<SpriteRenderer>();
            _normalScale = _visual.localScale;
            _squashScale = Vector3.one;
        }

        private void Update()
        {
            if (!_isAlive) return;
            ApplyFallGravity();
            UpdateSquash();

            if (_ghostActive && (aiMode == AIMode.GhostReplay || aiMode == AIMode.Hybrid))
                UpdateGhostReplay();
            else
                UpdateBehaviourAI();
        }

        private void FixedUpdate()
        {
            if (!_isAlive) return;

            if (!_ghostActive || aiMode == AIMode.BehaviourProfile)
                ApplyBehaviourMovement();

            if (_wantsToJump)
            {
                _wantsToJump = false;
                ApplySquash(0.72f, 1.32f);
                _rb.velocity = new Vector2(_rb.velocity.x, jumpForce * _pendingJumpMultiplier);
                _pendingJumpMultiplier = 1f;

                Game.VisualEffects.Instance?.PlayJumpDust(
                    transform.position, CharacterColor);
            }
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

        // ── Initialisation ────────────────────────────────────────────────────────
        public void Initialise(AIProfile profile, bool useGhost)
        {
            _profile               = profile;
            _isAlive               = true;
            _lastLandTime          = Time.time;
            _wantsToJump           = false;
            _targetX               = 0f;
            _rawTargetX            = 0f;
            _pendingJumpMultiplier = 1f;
            _squashScale           = Vector3.one;

            if (useGhost && _recorder != null && _recorder.HasBestRun)
            {
                _ghostFrames     = _recorder.GetBestRunFrames();
                _ghostIndex      = 0;
                _replayStartTime = Time.time;
                _ghostActive     = true;
                Debug.Log($"[AI] Ghost replay: {_ghostFrames.Count} frames.");
            }
            else
            {
                _ghostActive = false;
                Debug.Log("[AI] Behaviour-AI active.");
            }
        }

        // ── Ghost replay ──────────────────────────────────────────────────────────
        private void UpdateGhostReplay()
        {
            if (_ghostFrames == null || _ghostIndex >= _ghostFrames.Count)
            {
                _ghostActive = false;
                return;
            }

            float elapsed = Time.time - _replayStartTime;

            while (_ghostIndex < _ghostFrames.Count - 1 &&
                   _ghostFrames[_ghostIndex + 1].time <= elapsed)
                _ghostIndex++;

            GhostFrame frame = _ghostFrames[_ghostIndex];

            if (aiMode == AIMode.Hybrid)
            {
                _rb.velocity = new Vector2(
                    (frame.x - transform.position.x) / Time.fixedDeltaTime,
                    _rb.velocity.y);
            }
            else
            {
                transform.position = Vector3.Lerp(
                    transform.position,
                    new Vector3(frame.x, frame.y, 0f),
                    20f * Time.deltaTime);
            }

            if (frame.jumpEvent &&
                _ghostIndex > 0 &&
                !_ghostFrames[_ghostIndex - 1].jumpEvent)
            {
                _rb.velocity = new Vector2(_rb.velocity.x, jumpForce);
            }
        }

        // ── Behaviour AI ──────────────────────────────────────────────────────────
        private void UpdateBehaviourAI()
        {
            if (_profile == null || _spawner == null)
            {
                _rawTargetX = 0f;
                _targetX    = Mathf.Lerp(_targetX, _rawTargetX, Time.deltaTime * targetSmoothSpeed);
                return;
            }

            List<Platforms.Platform> upcoming =
                _spawner.GetPlatformsAbove(transform.position.y, 3);

            if (upcoming.Count == 0)
            {
                _rawTargetX = 0f;
                _targetX    = Mathf.Lerp(_targetX, _rawTargetX, Time.deltaTime * targetSmoothSpeed);
                return;
            }

            Platforms.Platform target = FindBestReachablePlatform(upcoming);

            if (target == null)
            {
                _rawTargetX = 0f;
            }
            else if (target.Type == Platforms.Platform.PlatformType.Moving)
            {
                if (!_movingPlatformNearby)
                {
                    _movingPlatformNearby       = true;
                    _movingPlatformDetectedTime = Time.time;
                }
                float reactionTime = _profile.reactionTime
                    + Random.Range(-noiseAmount * 0.2f, noiseAmount * 0.2f);

                if (Time.time - _movingPlatformDetectedTime > reactionTime)
                    _rawTargetX = PredictMovingPlatformX(target);
            }
            else
            {
                _movingPlatformNearby = false;
                _rawTargetX = target.transform.position.x
                              + Random.Range(-noiseAmount, noiseAmount);
            }

            _rawTargetX += _profile.directionBias * noiseAmount;
            _targetX     = Mathf.Lerp(_targetX, _rawTargetX, Time.deltaTime * targetSmoothSpeed);
        }

        private Platforms.Platform FindBestReachablePlatform(
            List<Platforms.Platform> candidates)
        {
            foreach (Platforms.Platform p in candidates)
            {
                float hDist = Mathf.Abs(p.transform.position.x - transform.position.x);
                if (hDist <= maxReachableHorizontalDist) return p;
            }

            Platforms.Platform best = null;
            float bestDist = float.MaxValue;
            foreach (Platforms.Platform p in candidates)
            {
                float hDist = Mathf.Abs(p.transform.position.x - transform.position.x);
                if (hDist < bestDist) { bestDist = hDist; best = p; }
            }
            return best;
        }

        private void ApplyBehaviourMovement()
        {
            if (_profile == null) return;
            float speed = overrideMoveSpeed > 0f ? overrideMoveSpeed : _profile.avgMoveSpeed;
            float diff  = _targetX - transform.position.x;
            float dir   = Mathf.Abs(diff) < 0.05f ? 0f : Mathf.Sign(diff);
            float noise = Random.Range(-noiseAmount, noiseAmount) * 0.25f;
            _rb.velocity = new Vector2(dir * speed * (1f + noise), _rb.velocity.y);
        }

        private float PredictMovingPlatformX(Platforms.Platform p)
        {
            float vy = Mathf.Abs(_rb.velocity.y);
            float t  = Mathf.Abs(p.transform.position.y - transform.position.y)
                       / Mathf.Max(1f, vy);
            return p.transform.position.x + p.HorizontalVelocity * t;
        }

        // ── Platform landing callback (called by Platform.cs) ─────────────────────
        public void OnLandedOnPlatform(
            string platformType, bool isMovingPlatform, float jumpMultiplier = 1f)
        {
            _lastLandTime         = Time.time;
            _movingPlatformNearby = false;

            ApplySquash(1.38f, platformType == "Spring" ? 0.55f : 0.65f);

            Game.VisualEffects.Instance?.PlayLandDust(
                transform.position, CharacterColor);

            _pendingJumpMultiplier = jumpMultiplier;

            float delay = overrideJumpDelay > 0f
                ? overrideJumpDelay
                : (_profile != null ? _profile.avgJumpDelay : 0.05f);

            delay += Random.Range(-noiseAmount * 0.05f, noiseAmount * 0.05f);
            delay  = Mathf.Max(0f, delay);

            StartCoroutine(JumpAfterDelay(delay));
        }

        private IEnumerator JumpAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            _wantsToJump = true;
        }

        // ── Physics helper ────────────────────────────────────────────────────────
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

        // ── Death / Revive ────────────────────────────────────────────────────────
        public void Die()
        {
            if (!_isAlive) return;
            _isAlive     = false;
            _rb.velocity = Vector2.zero;
            _rb.bodyType = RigidbodyType2D.Kinematic;

            Game.VisualEffects.Instance?.PlayDeathBurst(
                transform.position, CharacterColor);

            OnDied?.Invoke();
        }

        public void Revive(Vector3 position)
        {
            transform.position     = position;
            _rb.bodyType           = RigidbodyType2D.Dynamic;
            _rb.velocity           = Vector2.zero;
            _isAlive               = true;
            _targetX               = position.x;
            _rawTargetX            = position.x;
            _ghostActive           = false;
            _ghostIndex            = 0;
            _pendingJumpMultiplier = 1f;
            _squashScale           = Vector3.one;
            if (_visual != null) _visual.localScale = _normalScale;
        }

        // ── Public getters ────────────────────────────────────────────────────────
        public float CurrentHeight => transform.position.y;
        public bool  IsAlive       => _isAlive;
        public Color CharacterColor =>
            _visualSR != null ? _visualSR.color : new Color(0.95f, 0.38f, 0.27f);
    }
}
