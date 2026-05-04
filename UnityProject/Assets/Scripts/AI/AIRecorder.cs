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
        Fall          // player fell off the bottom of the screen
    }

    /// <summary>
    /// Snapshot of the player's behaviour AND surrounding environment at the
    /// moment of a jump.  Storing both together lets the AI understand context
    /// (e.g. "I was close to a moving platform and jumped early") rather than
    /// just copying raw actions.
    /// </summary>
    [System.Serializable]
    public class PlayerActionData
    {
        // Player state
        public float  time;
        public float  playerX;
        public float  playerY;
        public float  velocityX;
        public float  velocityY;
        public float  jumpDelay;       // seconds between landing and this jump
        public string platformType;    // type of platform just left

        // Environment state — next 3 platforms at time of jump
        public float  nextPlatform1X;
        public float  nextPlatform1Y;
        public string nextPlatform1Type;
        public float  nextPlatform1Speed; // horizontal speed (0 if not moving)

        public float  nextPlatform2X;
        public float  nextPlatform2Y;
        public string nextPlatform2Type;

        public float  nextPlatform3X;
        public float  nextPlatform3Y;
        public string nextPlatform3Type;

        // Outcome — filled in after the jump resolves
        public JumpOutcome outcome;
        public float       distanceToPlatform; // distance to next platform at jump time
        public float       landingError;       // |playerX - platform.x| on landing
    }

    /// <summary>A single timestamped position frame captured for ghost replay.</summary>
    [System.Serializable]
    public class GhostFrame
    {
        public float time;
        public float x;
        public float y;
        public float velocityX;
        public bool  jumpEvent;
    }

    /// <summary>
    /// Records the human player's gameplay in two parallel streams:
    ///
    ///   1. Action samples (jump + environment + outcome)
    ///      Fed to AITrainer to build a statistical AIProfile that grows smarter
    ///      across runs.
    ///
    ///   2. Ghost frames (position / velocity timeline, sampled every 0.2 s)
    ///      Used by AIPlayerController for exact ghost replay in vs-AI mode.
    ///
    /// Both buffers are capped to stay mobile-friendly.
    /// Jump outcomes are written retroactively — on the next landing or on death.
    /// </summary>
    public class AIRecorder : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Buffer Limits")]
        [Tooltip("Maximum action entries kept in the rolling history.")]
        public int maxActionEntries = 2000;

        [Tooltip("Maximum ghost frames stored per run.")]
        public int maxGhostFrames = 2000;

        [Header("Performance")]
        [Tooltip("Seconds between ghost frame samples. 0.2 s = 5 samples/sec (mobile-friendly).")]
        public float frameSampleInterval = 0.2f;

        // ── Internal state ────────────────────────────────────────────────────────
        private List<PlayerActionData> _actionHistory    = new List<PlayerActionData>();
        private List<GhostFrame>       _currentRun       = new List<GhostFrame>();
        private List<GhostFrame>       _bestRunGhost     = new List<GhostFrame>();
        private float                  _bestRunScore     = 0f;

        private float _lastSampleTime;
        private bool  _isRecording;
        private float _runStartTime;
        private float _latestReactionTime;

        // Last jump waiting for its outcome to be resolved
        private PlayerActionData _pendingJump;

        // ── Public API ────────────────────────────────────────────────────────────
        public void StartRecording()
        {
            _currentRun.Clear();
            _pendingJump     = null;
            _runStartTime    = Time.time;
            _lastSampleTime  = 0f;
            _isRecording     = true;
            Debug.Log("[AIRecorder] Recording started.");
        }

        public void StopRecording(float runScore)
        {
            if (!_isRecording) return;
            _isRecording = false;

            // Any unresolved jump at death is a Fall
            if (_pendingJump != null)
            {
                _pendingJump.outcome = JumpOutcome.Fall;
                _pendingJump = null;
            }

            // Promote this run to best-run ghost if it scored higher
            if (runScore > _bestRunScore && _currentRun.Count > 0)
            {
                _bestRunScore = runScore;
                _bestRunGhost = new List<GhostFrame>(_currentRun);
                Debug.Log($"[AIRecorder] New best run: score={runScore:0}  " +
                          $"frames={_bestRunGhost.Count}");
            }

            Debug.Log($"[AIRecorder] Stopped. Action history: {_actionHistory.Count}");
        }

        // ── Frame sampling (called by PlayerController.RecordFrame) ───────────────
        public void RecordFrame(float x, float y, float vx, float vy)
        {
            if (!_isRecording) return;

            float elapsed = Time.time - _runStartTime;
            if (elapsed - _lastSampleTime < frameSampleInterval) return;
            _lastSampleTime = elapsed;

            _currentRun.Add(new GhostFrame
            {
                time      = elapsed,
                x         = x,
                y         = y,
                velocityX = vx,
                jumpEvent = false
            });

            // Rolling cap — remove oldest entry
            if (_currentRun.Count > maxGhostFrames)
                _currentRun.RemoveAt(0);
        }

        // ── Jump recording (called via GameManager from PlayerController) ─────────
        public void RecordJump(
            float x, float y,
            float velocityX, float jumpDelay,
            string platformType,
            List<Platforms.Platform> nextPlatforms)
        {
            if (!_isRecording) return;

            // Tag the last ghost frame with a jump event
            if (_currentRun.Count > 0)
                _currentRun[_currentRun.Count - 1].jumpEvent = true;

            // Previous unresolved jump with no landing between them = missed
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
                outcome      = JumpOutcome.MissedJump // overwritten on landing
            };

            // Fill environment state from the next 3 platforms
            if (nextPlatforms != null)
            {
                if (nextPlatforms.Count > 0)
                {
                    var p1 = nextPlatforms[0];
                    entry.nextPlatform1X     = p1.transform.position.x;
                    entry.nextPlatform1Y     = p1.transform.position.y;
                    entry.nextPlatform1Type  = p1.Type.ToString();
                    entry.nextPlatform1Speed = p1.HorizontalVelocity;
                    entry.distanceToPlatform = Vector2.Distance(
                        new Vector2(x, y),
                        new Vector2(p1.transform.position.x, p1.transform.position.y));
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

            if (_actionHistory.Count > maxActionEntries)
                _actionHistory.RemoveAt(0);
        }

        // ── Landing resolution (called via GameManager from PlayerController) ─────
        public void RecordLanding(float playerX, float platformCentreX)
        {
            if (_pendingJump == null) return;
            _pendingJump.outcome      = JumpOutcome.SuccessfulLanding;
            _pendingJump.landingError = Mathf.Abs(playerX - platformCentreX);
            _pendingJump = null;
        }

        // ── Reaction time (called by PlayerController) ────────────────────────────
        public void RecordReactionTime(float reactionTime)
        {
            _latestReactionTime = reactionTime;
        }

        // ── Near-miss event (called by GameManager) ────────────────────────────────
        /// <summary>
        /// Called when the player avoids a hazard by a narrow margin (e.g. UFO shot).
        /// Useful for tracking reaction accuracy in the AI profile.
        /// </summary>
        public void RecordNearMiss()
        {
            // Bump the near-miss count on the most recent action if available
            if (_currentRun.Count > 0)
            {
                var last = _currentRun[^1];
                last.nearMissFlag = true;
            }
        }

        // ── Full reset ────────────────────────────────────────────────────────────
        public void ClearAll()
        {
            _actionHistory.Clear();
            _currentRun.Clear();
            _bestRunGhost.Clear();
            _bestRunScore    = 0f;
            _pendingJump     = null;
            _isRecording     = false;
            Debug.Log("[AIRecorder] All data cleared.");
        }

        // ── Getters ───────────────────────────────────────────────────────────────
        public List<PlayerActionData> GetActionHistory()  => _actionHistory;
        public List<GhostFrame>       GetBestRunFrames()  => _bestRunGhost;
        public float                  LatestReactionTime  => _latestReactionTime;
        public bool                   HasBestRun          => _bestRunGhost.Count > 0;
        public float                  BestRunScore        => _bestRunScore;
    }
}
