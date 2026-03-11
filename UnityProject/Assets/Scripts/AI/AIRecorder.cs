using System.Collections.Generic;
using UnityEngine;

namespace DoodleClimb.AI
{
    // ── Data structures ───────────────────────────────────────────────────────────

    /// <summary>Outcome of an individual jump attempt.</summary>
    public enum JumpOutcome
    {
        SuccessfulLanding,
        MissedJump,   // player jumped but landed on nothing and kept falling
        Fall          // player fell off the screen entirely
    }

    /// <summary>
    /// Snapshot of the player's behaviour AND the surrounding environment at
    /// the moment of a jump.  Storing both together lets the AI understand
    /// context ("I was close to a moving platform and jumped early") rather than
    /// just copying raw actions.
    /// </summary>
    [System.Serializable]
    public class PlayerActionData
    {
        // ── Player state ──────────────────────────────────────────────────────────
        public float time;
        public float playerX;
        public float playerY;
        public float velocityX;
        public float velocityY;
        public float jumpDelay;          // seconds between landing and next jump
        public string platformType;      // type of platform the player just left

        // ── Environment state (next 3 platforms at time of jump) ─────────────────
        public float nextPlatform1X;
        public float nextPlatform1Y;
        public string nextPlatform1Type;
        public float nextPlatform1Speed; // horizontal speed (0 if not moving)

        public float nextPlatform2X;
        public float nextPlatform2Y;
        public string nextPlatform2Type;

        public float nextPlatform3X;
        public float nextPlatform3Y;
        public string nextPlatform3Type;

        // ── Outcome ───────────────────────────────────────────────────────────────
        public JumpOutcome outcome;      // filled in after the jump resolves
        public float distanceToPlatform; // distance to next platform at jump time
        public float landingError;       // |playerX - platform.x| on landing (0 = perfect)
    }

    /// <summary>A single timestamped frame captured for ghost replay.</summary>
    [System.Serializable]
    public class GhostFrame
    {
        public float time;
        public float x;
        public float y;
        public float velocityX;
        public bool jumpEvent;
    }

    /// <summary>
    /// Records the human player's gameplay in two ways:
    ///
    ///   1. Behaviour + environment samples
    ///      Records player state, surrounding platform layout, and jump outcomes.
    ///      Fed to AITrainer to build a statistical AIProfile.
    ///
    ///   2. Ghost frames (full position/velocity timeline)
    ///      Used for exact ghost replay by AIPlayerController.
    ///
    /// Both buffers are capped at 2,000 entries to stay mobile-friendly.
    /// Outcomes are filled in retroactively after each jump resolves.
    /// </summary>
    public class AIRecorder : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Limits")]
        [Tooltip("Maximum action entries kept in memory.")]
        public int maxActionEntries = 2000;

        [Tooltip("Maximum ghost frames kept per run.")]
        public int maxGhostFrames = 2000;

        [Tooltip("Minimum seconds between frame samples (performance throttle).")]
        public float frameSampleInterval = 0.05f;

        // ── Internal state ────────────────────────────────────────────────────────
        private List<PlayerActionData> _actionHistory = new List<PlayerActionData>();
        private List<GhostFrame> _currentGhostFrames = new List<GhostFrame>();

        // Stores the best run ghost (highest score achieved)
        private List<GhostFrame> _bestRunGhostFrames = new List<GhostFrame>();
        private float _bestRunScore = 0f;

        private float _lastFrameSampleTime;
        private bool _isRecording = false;
        private float _runStartTime;

        // Latest reaction time for the trainer
        private float _latestReactionTime = 0f;

        // The last recorded jump — we fill in the outcome retroactively
        private PlayerActionData _pendingJump = null;

        // ── Public API ────────────────────────────────────────────────────────────
        public void StartRecording()
        {
            _currentGhostFrames.Clear();
            _pendingJump = null;
            _runStartTime = Time.time;
            _lastFrameSampleTime = 0f;
            _isRecording = true;
            Debug.Log("[AIRecorder] Recording started.");
        }

        public void StopRecording(float runScore)
        {
            _isRecording = false;

            // If there's still an unresolved jump, mark it as a fall
            if (_pendingJump != null)
            {
                _pendingJump.outcome = JumpOutcome.Fall;
                _pendingJump = null;
            }

            // Keep the best run for ghost replay
            if (runScore > _bestRunScore)
            {
                _bestRunScore = runScore;
                _bestRunGhostFrames = new List<GhostFrame>(_currentGhostFrames);
                Debug.Log($"[AIRecorder] New best run saved. Score: {runScore}. " +
                          $"Frames: {_bestRunGhostFrames.Count}");
            }

            Debug.Log($"[AIRecorder] Recording stopped. " +
                      $"Action history: {_actionHistory.Count} entries.");
        }

        // ── Called every physics frame (by PlayerController) ─────────────────────
        public void RecordFrame(float x, float y, float vx, float vy)
        {
            if (!_isRecording) return;

            float now = Time.time - _runStartTime;
            if (now - _lastFrameSampleTime < frameSampleInterval) return;
            _lastFrameSampleTime = now;

            GhostFrame gf = new GhostFrame
            {
                time  = now,
                x     = x,
                y     = y,
                velocityX = vx,
                jumpEvent = false
            };

            _currentGhostFrames.Add(gf);

            // Trim oldest frames if over limit
            if (_currentGhostFrames.Count > maxGhostFrames)
                _currentGhostFrames.RemoveAt(0);
        }

        // ── Called on each jump (by PlayerController) ─────────────────────────────
        /// <summary>
        /// Records a jump event, including the state of the next 3 platforms visible
        /// at the time of the jump (passed in from PlatformSpawner via GameManager).
        /// </summary>
        public void RecordJump(
            float x, float y,
            float velocityX, float jumpDelay,
            string platformType,
            List<Platforms.Platform> nextPlatforms)
        {
            if (!_isRecording) return;

            // Mark jump event on the last ghost frame
            if (_currentGhostFrames.Count > 0)
                _currentGhostFrames[_currentGhostFrames.Count - 1].jumpEvent = true;

            // Resolve any still-pending jump as a missed jump (no landing between jumps)
            if (_pendingJump != null)
            {
                _pendingJump.outcome = JumpOutcome.MissedJump;
                _pendingJump = null;
            }

            PlayerActionData entry = new PlayerActionData
            {
                time         = Time.time - _runStartTime,
                playerX      = x,
                playerY      = y,
                velocityX    = velocityX,
                velocityY    = 0f,
                jumpDelay    = jumpDelay,
                platformType = platformType,
                outcome      = JumpOutcome.MissedJump // default; overwritten on landing
            };

            // Fill in next-platform environment state
            if (nextPlatforms != null)
            {
                if (nextPlatforms.Count > 0)
                {
                    var p1 = nextPlatforms[0];
                    entry.nextPlatform1X    = p1.transform.position.x;
                    entry.nextPlatform1Y    = p1.transform.position.y;
                    entry.nextPlatform1Type = p1.Type.ToString();
                    entry.nextPlatform1Speed = p1.HorizontalVelocity;
                    entry.distanceToPlatform = Vector2.Distance(
                        new Vector2(x, y),
                        new Vector2(p1.transform.position.x, p1.transform.position.y)
                    );
                }
                if (nextPlatforms.Count > 1)
                {
                    var p2 = nextPlatforms[1];
                    entry.nextPlatform2X    = p2.transform.position.x;
                    entry.nextPlatform2Y    = p2.transform.position.y;
                    entry.nextPlatform2Type = p2.Type.ToString();
                }
                if (nextPlatforms.Count > 2)
                {
                    var p3 = nextPlatforms[2];
                    entry.nextPlatform3X    = p3.transform.position.x;
                    entry.nextPlatform3Y    = p3.transform.position.y;
                    entry.nextPlatform3Type = p3.Type.ToString();
                }
            }

            _actionHistory.Add(entry);
            _pendingJump = entry;

            // Rolling window — drop oldest if over limit
            if (_actionHistory.Count > maxActionEntries)
                _actionHistory.RemoveAt(0);
        }

        // ── Called when the player successfully lands (by PlayerController) ────────
        /// <summary>
        /// Resolves the pending jump as a SuccessfulLanding and records landing error
        /// (how far horizontally from the platform centre the player actually landed).
        /// </summary>
        public void RecordLanding(float playerX, float platformCentreX)
        {
            if (_pendingJump == null) return;

            _pendingJump.outcome      = JumpOutcome.SuccessfulLanding;
            _pendingJump.landingError = Mathf.Abs(playerX - platformCentreX);
            _pendingJump = null;
        }

        // ── Called when we measure a platform reaction time ───────────────────────
        public void RecordReactionTime(float reactionTime)
        {
            _latestReactionTime = reactionTime;
        }

        // ── Getters (for AITrainer) ───────────────────────────────────────────────
        public List<PlayerActionData> GetActionHistory() => _actionHistory;
        public List<GhostFrame> GetBestRunFrames()       => _bestRunGhostFrames;
        public float LatestReactionTime                  => _latestReactionTime;
        public bool  HasBestRun                          => _bestRunGhostFrames.Count > 0;
        public float BestRunScore                        => _bestRunScore;

        /// <summary>
        /// Clears ALL recorded data (use when player wants a completely fresh AI).
        /// </summary>
        public void ClearAll()
        {
            _actionHistory.Clear();
            _currentGhostFrames.Clear();
            _bestRunGhostFrames.Clear();
            _bestRunScore = 0f;
            _pendingJump  = null;
            Debug.Log("[AIRecorder] All data cleared.");
        }
    }
}
