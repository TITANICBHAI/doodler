using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DoodleClimb.AI
{
    /// <summary>
    /// Controls the AI clone character using a hybrid of two systems:
    ///
    ///   System A — Ghost Replay (if a best-run exists):
    ///     Plays back the exact positions and jumps from the player's best run.
    ///     Feels naturally human because it IS the player.
    ///
    ///   System B — Behaviour AI (statistical profile):
    ///     Looks at the next 3 platforms, moves toward them, jumps with learned
    ///     timing, and adds small random noise to feel alive.
    ///
    /// The active system is determined by GameModeManager / can be toggled at runtime.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class AIPlayerController : MonoBehaviour
    {
        // ── Mode selection ────────────────────────────────────────────────────────
        public enum AIMode { BehaviourProfile, GhostReplay, Hybrid }

        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("AI Mode")]
        public AIMode aiMode = AIMode.Hybrid;

        [Header("Profile Overrides (leave 0 to use trained profile)")]
        public float overrideMoveSpeed = 0f;
        public float overrideJumpDelay = 0f;

        [Header("Randomness")]
        [Tooltip("Small noise added to movements to simulate human imprecision.")]
        [Range(0f, 1f)]
        public float noiseAmount = 0.15f;

        [Header("Physics")]
        public float jumpForce = 18f;
        public float fallGravityMultiplier = 2.5f;

        // ── References ────────────────────────────────────────────────────────────
        private AIProfile _profile;
        private AIRecorder _recorder;
        private Platforms.PlatformSpawner _spawner;
        private Rigidbody2D _rb;

        // ── Behaviour-AI state ────────────────────────────────────────────────────
        private float _targetX;
        private float _lastLandTime;
        private float _jumpDelayTimer;
        private bool _wantsToJump;
        private bool _isAlive;

        // ── Ghost-replay state ────────────────────────────────────────────────────
        private List<GhostFrame> _ghostFrames;
        private int _ghostIndex = 0;
        private float _replayStartTime;
        private bool _ghostActive = false;

        // ── Moving platform detection ─────────────────────────────────────────────
        private bool _movingPlatformNearby;
        private float _movingPlatformDetectedTime;

        // ── Events ────────────────────────────────────────────────────────────────
        public System.Action OnDied;

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            _recorder = FindObjectOfType<AIRecorder>();
            _spawner = FindObjectOfType<Platforms.PlatformSpawner>();
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
                _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, jumpForce);
            }
        }

        // ── Initialisation ────────────────────────────────────────────────────────
        public void Initialise(AIProfile profile, bool useGhost)
        {
            _profile = profile;
            _isAlive = true;
            _lastLandTime = Time.time;
            _jumpDelayTimer = 0f;

            if (useGhost && _recorder != null && _recorder.HasBestRun)
            {
                _ghostFrames = _recorder.GetBestRunFrames();
                _ghostIndex = 0;
                _replayStartTime = Time.time;
                _ghostActive = true;
                Debug.Log($"[AIPlayerController] Ghost replay active. " +
                          $"Frames: {_ghostFrames.Count}");
            }
            else
            {
                _ghostActive = false;
                Debug.Log("[AIPlayerController] Behaviour-AI mode active.");
            }
        }

        // ── Ghost replay ──────────────────────────────────────────────────────────
        private void UpdateGhostReplay()
        {
            if (_ghostFrames == null || _ghostIndex >= _ghostFrames.Count)
            {
                // Ghost finished — fall back to behaviour AI for the rest of the run
                _ghostActive = false;
                return;
            }

            float replayTime = Time.time - _replayStartTime;

            // Advance ghost index to match current replay time
            while (_ghostIndex < _ghostFrames.Count - 1 &&
                   _ghostFrames[_ghostIndex + 1].time <= replayTime)
            {
                _ghostIndex++;
            }

            GhostFrame current = _ghostFrames[_ghostIndex];

            // Interpolate position towards ghost position (smooth tracking)
            Vector3 targetPos = new Vector3(current.x, current.y, 0f);

            // In Hybrid mode, blend ghost position with physics
            if (aiMode == AIMode.Hybrid)
            {
                _rb.linearVelocity = new Vector2(
                    (current.x - transform.position.x) / Time.fixedDeltaTime,
                    _rb.linearVelocity.y
                );
            }
            else
            {
                // Pure replay: move directly
                transform.position = Vector3.Lerp(
                    transform.position, targetPos, 20f * Time.deltaTime);
            }

            // Replay jump events
            if (current.jumpEvent &&
                _ghostIndex > 0 &&
                !_ghostFrames[_ghostIndex - 1].jumpEvent)
            {
                _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, jumpForce);
            }
        }

        // ── Behaviour AI ──────────────────────────────────────────────────────────
        private void UpdateBehaviourAI()
        {
            if (_profile == null || _spawner == null) return;

            // Get next 3 platforms above the AI
            List<Platforms.Platform> upcomingPlatforms =
                _spawner.GetPlatformsAbove(transform.position.y, 3);

            if (upcomingPlatforms.Count == 0) return;

            Platforms.Platform targetPlatform = upcomingPlatforms[0];

            // React to moving platforms with learned reaction time
            if (targetPlatform.Type == Platforms.Platform.PlatformType.Moving)
            {
                if (!_movingPlatformNearby)
                {
                    _movingPlatformNearby = true;
                    _movingPlatformDetectedTime = Time.time;
                }

                float reactionTime = _profile.reactionTime
                    + Random.Range(-noiseAmount * 0.2f, noiseAmount * 0.2f);

                if (Time.time - _movingPlatformDetectedTime > reactionTime)
                    _targetX = PredictMovingPlatformX(targetPlatform);
            }
            else
            {
                _movingPlatformNearby = false;
                _targetX = targetPlatform.transform.position.x
                    + Random.Range(-noiseAmount, noiseAmount);  // noise to feel human
            }

            // Account for direction bias from profile
            _targetX += _profile.directionBias * noiseAmount;
        }

        private void ApplyBehaviourMovement()
        {
            if (_profile == null) return;

            float speed = overrideMoveSpeed > 0f ? overrideMoveSpeed : _profile.avgMoveSpeed;
            float dir = Mathf.Sign(_targetX - transform.position.x);
            float noise = Random.Range(-noiseAmount, noiseAmount) * 0.3f;

            _rb.linearVelocity = new Vector2(
                dir * speed * (1f + noise),
                _rb.linearVelocity.y
            );
        }

        // ── Predict where a moving platform will be when the AI arrives ───────────
        private float PredictMovingPlatformX(Platforms.Platform p)
        {
            float timeToReach = Mathf.Abs(p.transform.position.y - transform.position.y)
                / Mathf.Max(1f, Mathf.Abs(_rb.linearVelocity.y));
            return p.transform.position.x + p.HorizontalVelocity * timeToReach;
        }

        // ── Platform landing callback ─────────────────────────────────────────────
        public void OnLandedOnPlatform(string platformType, bool isMovingPlatform)
        {
            _lastLandTime = Time.time;

            float delay = overrideJumpDelay > 0f ? overrideJumpDelay : _profile?.avgJumpDelay ?? 0.05f;
            delay += Random.Range(-noiseAmount * 0.05f, noiseAmount * 0.05f);
            delay = Mathf.Max(0f, delay);

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
            if (_rb.linearVelocity.y < 0f)
            {
                _rb.linearVelocity += Vector2.up
                    * Physics2D.gravity.y
                    * (fallGravityMultiplier - 1f)
                    * Time.deltaTime;
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

        public void Revive(Vector3 position)
        {
            transform.position = position;
            _rb.bodyType = RigidbodyType2D.Dynamic;
            _rb.linearVelocity = Vector2.zero;
            _isAlive = true;
        }

        public float CurrentHeight => transform.position.y;
        public bool IsAlive => _isAlive;
    }
}
