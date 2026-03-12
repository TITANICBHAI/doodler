using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DoodleClimb.AI
{
    /// <summary>
    /// Controls the AI clone character using two complementary systems:
    ///
    ///   System A — Ghost Replay (when a best-run exists):
    ///     Plays back the player's best-run position timeline.
    ///     Naturally human because the data IS the player.
    ///
    ///   System B — Behaviour AI (statistical profile):
    ///     Looks ahead at the next 3 platforms, selects the best reachable one,
    ///     moves toward it with smooth lerp, applies learned jump timing and noise.
    ///     Falls back to the screen centre if no platforms are found.
    ///
    /// Platform reachability:
    ///     A platform is considered reachable if it's within the playfield width.
    ///     If the nearest platform is too far horizontally, the AI skips to the next.
    ///     Movement speed matches the trained player profile exactly.
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

        [Header("Randomness — human imprecision simulation")]
        [Range(0f, 1f)]
        public float noiseAmount = 0.15f;

        [Header("Physics")]
        public float jumpForce = 18f;
        public float fallGravityMultiplier = 2.5f;

        [Header("Platform Targeting")]
        [Tooltip("Maximum horizontal distance to a platform before it's skipped.")]
        public float maxReachableHorizontalDist = 7f;

        [Tooltip("Smoothing speed for horizontal movement target.")]
        [Range(1f, 15f)]
        public float targetSmoothSpeed = 6f;

        // ── References ────────────────────────────────────────────────────────────
        private AIProfile                 _profile;
        private AIRecorder                _recorder;
        private Platforms.PlatformSpawner _spawner;
        private Rigidbody2D               _rb;

        // ── Behaviour-AI state ────────────────────────────────────────────────────
        private float _targetX;         // smoothed target; AI lerps toward this
        private float _rawTargetX;      // unsmoothed desired X before lerp
        private float _lastLandTime;
        private bool  _wantsToJump;
        private bool  _isAlive;

        // ── Ghost-replay state ────────────────────────────────────────────────────
        private List<GhostFrame> _ghostFrames;
        private int              _ghostIndex;
        private float            _replayStartTime;
        private bool             _ghostActive;

        // ── Moving platform detection ─────────────────────────────────────────────
        private bool  _movingPlatformNearby;
        private float _movingPlatformDetectedTime;

        // ── Events ────────────────────────────────────────────────────────────────
        public System.Action OnDied;

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            _rb          = GetComponent<Rigidbody2D>();
            _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            _recorder    = FindObjectOfType<AIRecorder>();
            _spawner     = FindObjectOfType<Platforms.PlatformSpawner>();
        }

        private void Update()
        {
            if (!_isAlive) return;
            ApplyFallGravity();

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
                _rb.velocity = new Vector2(_rb.velocity.x, jumpForce);
            }
        }

        // ── Initialisation ────────────────────────────────────────────────────────
        public void Initialise(AIProfile profile, bool useGhost)
        {
            _profile      = profile;
            _isAlive      = true;
            _lastLandTime = Time.time;
            _wantsToJump  = false;
            _targetX      = 0f;
            _rawTargetX   = 0f;

            if (useGhost && _recorder != null && _recorder.HasBestRun)
            {
                _ghostFrames     = _recorder.GetBestRunFrames();
                _ghostIndex      = 0;
                _replayStartTime = Time.time;
                _ghostActive     = true;
                Debug.Log($"[AIPlayerController] Ghost replay: {_ghostFrames.Count} frames.");
            }
            else
            {
                _ghostActive = false;
                Debug.Log("[AIPlayerController] Behaviour-AI active.");
            }
        }

        // ── Ghost replay ──────────────────────────────────────────────────────────
        private void UpdateGhostReplay()
        {
            if (_ghostFrames == null || _ghostIndex >= _ghostFrames.Count)
            {
                // Ghost exhausted — fall back to behaviour AI for the rest of the run
                _ghostActive = false;
                return;
            }

            float elapsed = Time.time - _replayStartTime;

            // Seek forward in the timeline to match elapsed time
            while (_ghostIndex < _ghostFrames.Count - 1 &&
                   _ghostFrames[_ghostIndex + 1].time <= elapsed)
                _ghostIndex++;

            GhostFrame frame = _ghostFrames[_ghostIndex];

            if (aiMode == AIMode.Hybrid)
            {
                // Hybrid: drive X velocity toward ghost X while keeping physics Y
                _rb.velocity = new Vector2(
                    (frame.x - transform.position.x) / Time.fixedDeltaTime,
                    _rb.velocity.y);
            }
            else
            {
                // Pure replay: lerp directly to ghost position
                transform.position = Vector3.Lerp(
                    transform.position,
                    new Vector3(frame.x, frame.y, 0f),
                    20f * Time.deltaTime);
            }

            // Fire jump at the same frame the player did
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
                // No profile / spawner — drift toward centre so AI doesn't get stuck
                _rawTargetX = 0f;
                _targetX    = Mathf.Lerp(_targetX, _rawTargetX, Time.deltaTime * targetSmoothSpeed);
                return;
            }

            List<Platforms.Platform> upcoming =
                _spawner.GetPlatformsAbove(transform.position.y, 3);

            if (upcoming.Count == 0)
            {
                // No platforms found above — move to screen centre as fallback
                _rawTargetX = 0f;
                _targetX    = Mathf.Lerp(_targetX, _rawTargetX, Time.deltaTime * targetSmoothSpeed);
                return;
            }

            // Pick the best reachable platform from the list
            Platforms.Platform target = FindBestReachablePlatform(upcoming);

            if (target == null)
            {
                // All platforms are too far — use centre fallback
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

                // Only correct for moving platform after learned reaction delay
                if (Time.time - _movingPlatformDetectedTime > reactionTime)
                    _rawTargetX = PredictMovingPlatformX(target);
                // else keep current _rawTargetX (don't correct yet)
            }
            else
            {
                _movingPlatformNearby = false;
                // Add small noise to avoid perfectly centred movement every time
                _rawTargetX = target.transform.position.x
                              + Random.Range(-noiseAmount, noiseAmount);
            }

            // Blend in the player's directional tendency
            _rawTargetX += _profile.directionBias * noiseAmount;

            // Smooth the target so the AI doesn't snap direction instantly
            _targetX = Mathf.Lerp(_targetX, _rawTargetX, Time.deltaTime * targetSmoothSpeed);
        }

        /// <summary>
        /// Scans upcoming platforms and returns the best reachable one.
        /// Skips platforms that are too far horizontally (AI would miss).
        /// Falls back to the nearest platform if all are technically far (screen wrap).
        /// </summary>
        private Platforms.Platform FindBestReachablePlatform(
            List<Platforms.Platform> candidates)
        {
            // First pass: prefer a platform within maxReachableHorizontalDist
            foreach (Platforms.Platform p in candidates)
            {
                float hDist = Mathf.Abs(p.transform.position.x - transform.position.x);
                if (hDist <= maxReachableHorizontalDist)
                    return p;
            }

            // Second pass: all platforms are far — return the closest horizontally
            // (screen-wrap means the AI can always reach any platform eventually)
            Platforms.Platform best = null;
            float bestDist = float.MaxValue;
            foreach (Platforms.Platform p in candidates)
            {
                float hDist = Mathf.Abs(p.transform.position.x - transform.position.x);
                if (hDist < bestDist)
                {
                    bestDist = hDist;
                    best     = p;
                }
            }
            return best;
        }

        private void ApplyBehaviourMovement()
        {
            if (_profile == null) return;

            // Use trained speed — exactly matching how fast the player moved
            float speed = overrideMoveSpeed > 0f ? overrideMoveSpeed : _profile.avgMoveSpeed;
            float diff  = _targetX - transform.position.x;
            float dir   = Mathf.Abs(diff) < 0.05f ? 0f : Mathf.Sign(diff);
            float noise = Random.Range(-noiseAmount, noiseAmount) * 0.25f;

            _rb.velocity = new Vector2(
                dir * speed * (1f + noise),
                _rb.velocity.y);
        }

        // ── Predict moving platform position at arrival time ──────────────────────
        private float PredictMovingPlatformX(Platforms.Platform p)
        {
            float vy         = Mathf.Abs(_rb.velocity.y);
            float timeToReach = Mathf.Abs(p.transform.position.y - transform.position.y)
                              / Mathf.Max(1f, vy);
            return p.transform.position.x + p.HorizontalVelocity * timeToReach;
        }

        // ── Platform landing callback (called by Platform.cs) ─────────────────────
        public void OnLandedOnPlatform(string platformType, bool isMovingPlatform)
        {
            _lastLandTime         = Time.time;
            _movingPlatformNearby = false;

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
            OnDied?.Invoke();
        }

        public void Revive(Vector3 position)
        {
            transform.position = position;
            _rb.bodyType       = RigidbodyType2D.Dynamic;
            _rb.velocity       = Vector2.zero;
            _isAlive           = true;
            _targetX           = position.x;
            _rawTargetX        = position.x;
        }

        // ── Public getters ────────────────────────────────────────────────────────
        public float CurrentHeight => transform.position.y;
        public bool  IsAlive       => _isAlive;
    }
}
