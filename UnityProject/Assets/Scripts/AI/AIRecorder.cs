using System.Collections.Generic;
using UnityEngine;

namespace DoodleClimb.AI
{
    // ── Data structures ───────────────────────────────────────────────────────────

    /// <summary>Snapshot of the player's behaviour at a single moment.</summary>
    [System.Serializable]
    public class PlayerActionData
    {
        public float time;
        public float playerX;
        public float playerY;
        public float velocityX;
        public float velocityY;
        public float jumpDelay;
        public string platformType;
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
    ///   1. Behaviour samples (jump timing, speed, platform type, reaction time)
    ///      — fed to AITrainer to build a statistical AIProfile.
    ///   2. Ghost frames (full position/velocity timeline)
    ///      — used for exact ghost replay.
    ///
    /// Both buffers are capped at 2,000 entries to stay mobile-friendly.
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

        // ── Public API ────────────────────────────────────────────────────────────
        public void StartRecording()
        {
            _currentGhostFrames.Clear();
            _runStartTime = Time.time;
            _lastFrameSampleTime = 0f;
            _isRecording = true;
            Debug.Log("[AIRecorder] Recording started.");
        }

        public void StopRecording(float runScore)
        {
            _isRecording = false;

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
                time = now,
                x = x,
                y = y,
                velocityX = vx,
                jumpEvent = false
            };

            _currentGhostFrames.Add(gf);

            // Trim oldest frames if over limit
            if (_currentGhostFrames.Count > maxGhostFrames)
                _currentGhostFrames.RemoveAt(0);
        }

        // ── Called on each jump (by PlayerController) ─────────────────────────────
        public void RecordJump(
            float x, float y,
            float velocityX,
            float jumpDelay,
            string platformType)
        {
            if (!_isRecording) return;

            // Mark jump event on the last ghost frame
            if (_currentGhostFrames.Count > 0)
                _currentGhostFrames[_currentGhostFrames.Count - 1].jumpEvent = true;

            PlayerActionData entry = new PlayerActionData
            {
                time = Time.time - _runStartTime,
                playerX = x,
                playerY = y,
                velocityX = velocityX,
                velocityY = 0f,
                jumpDelay = jumpDelay,
                platformType = platformType
            };

            _actionHistory.Add(entry);

            // Rolling window — drop oldest if over limit
            if (_actionHistory.Count > maxActionEntries)
                _actionHistory.RemoveAt(0);
        }

        // ── Called when we measure a platform reaction time ───────────────────────
        public void RecordReactionTime(float reactionTime)
        {
            _latestReactionTime = reactionTime;
        }

        // ── Getters (for AITrainer) ───────────────────────────────────────────────
        public List<PlayerActionData> GetActionHistory() => _actionHistory;
        public List<GhostFrame> GetBestRunFrames() => _bestRunGhostFrames;
        public float LatestReactionTime => _latestReactionTime;
        public bool HasBestRun => _bestRunGhostFrames.Count > 0;
        public float BestRunScore => _bestRunScore;

        /// <summary>
        /// Clears ALL recorded data (used when starting a completely fresh session).
        /// </summary>
        public void ClearAll()
        {
            _actionHistory.Clear();
            _currentGhostFrames.Clear();
            _bestRunGhostFrames.Clear();
            _bestRunScore = 0f;
            Debug.Log("[AIRecorder] All data cleared.");
        }
    }
}
