using System.Collections.Generic;
using UnityEngine;

namespace DoodleClimb.AI
{
    // ── Data structures ───────────────────────────────────────────────────────────

    /// <summary>Statistical AI profile built from multiple runs.</summary>
    [System.Serializable]
    public class AIProfile
    {
        [Header("Movement")]
        public float avgMoveSpeed = 5f;

        [Header("Jump Timing")]
        public float avgJumpDelay = 0.05f;

        [Header("Direction Tendency (-1 = left biased, 0 = neutral, +1 = right biased)")]
        [Range(-1f, 1f)]
        public float directionBias = 0f;

        [Header("Platform Reactions")]
        public float reactionTime = 0.3f;

        [Header("Skill Metrics")]
        [Range(0f, 1f)]
        public float riskLevel = 0.5f;      // how often player attempts difficult jumps
        [Range(0f, 1f)]
        public float jumpPrecision = 0.5f;  // how accurate jump timing is
        [Range(0f, 1f)]
        public float movementSmoothness = 0.5f; // how smoothly the player moves

        [Header("Evolution")]
        public int totalRunsAnalyzed = 0;
        public float bestScoreEver = 0f;

        /// <summary>Returns a human-readable summary of the profile.</summary>
        public override string ToString()
        {
            return $"AIProfile | Runs: {totalRunsAnalyzed} | Best: {bestScoreEver:0} | " +
                   $"Speed: {avgMoveSpeed:0.0} | Bias: {directionBias:+0.00;-0.00;0} | " +
                   $"Reaction: {reactionTime:0.000}s | Risk: {riskLevel:0.00} | " +
                   $"Precision: {jumpPrecision:0.00} | Smoothness: {movementSmoothness:0.00}";
        }
    }

    /// <summary>
    /// Analyses recorded PlayerActionData and ghost frames to produce or update an AIProfile.
    ///
    /// Key computations:
    ///   • avgMoveSpeed        — mean |velocityX| across all action samples
    ///   • avgJumpDelay        — mean time between landing and jumping
    ///   • directionBias       — net rightward tendency of horizontal velocity
    ///   • reactionTime        — mean recorded reaction to moving platforms
    ///   • riskLevel           — proportion of Breakable/Temporary platform landings
    ///   • jumpPrecision       — inverse of std-dev of jumpDelay (normalised)
    ///   • movementSmoothness  — inverse of mean direction-change rate (normalised)
    ///
    /// Profiles accumulate across runs using exponential moving averages so the AI
    /// gradually "evolves" to match the player rather than forgetting old runs.
    /// </summary>
    public class AITrainer : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Profile")]
        [Tooltip("The AI profile that is continuously updated.")]
        public AIProfile profile = new AIProfile();

        [Header("Evolution Blend")]
        [Tooltip("Weight given to new data vs existing profile (0=never update, 1=always replace).")]
        [Range(0.01f, 1f)]
        public float learningRate = 0.35f;

        [Header("Minimum samples")]
        [Tooltip("Minimum action records needed before a meaningful profile can be built.")]
        public int minSamplesRequired = 10;

        // ── References ────────────────────────────────────────────────────────────
        private AIRecorder _recorder;

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            _recorder = FindObjectOfType<AIRecorder>();
        }

        // ── Public entry point ────────────────────────────────────────────────────

        /// <summary>
        /// Call this at the end of each player run to update the AI profile.
        /// Pass the score so we can track evolution.
        /// </summary>
        public void TrainFromLatestRun(float runScore)
        {
            List<PlayerActionData> actions = _recorder.GetActionHistory();

            if (actions.Count < minSamplesRequired)
            {
                Debug.Log($"[AITrainer] Not enough samples ({actions.Count}). Skipping training.");
                return;
            }

            AIProfile newData = AnalyseActions(actions);
            BlendIntoProfile(newData, runScore);

            Debug.Log($"[AITrainer] Profile updated. {profile}");
        }

        // ── Analysis ──────────────────────────────────────────────────────────────
        private AIProfile AnalyseActions(List<PlayerActionData> actions)
        {
            AIProfile result = new AIProfile();

            // Accumulators
            double sumSpeed = 0;
            double sumJumpDelay = 0;
            double sumDirectionX = 0;
            double sumReaction = 0;
            int reactionCount = 0;
            int riskCount = 0;

            // For precision (std-dev of jumpDelay)
            List<float> jumpDelays = new List<float>();
            // For smoothness (direction changes)
            float prevVX = 0f;
            int directionChanges = 0;

            foreach (PlayerActionData a in actions)
            {
                sumSpeed += Mathf.Abs(a.velocityX);
                sumJumpDelay += a.jumpDelay;
                sumDirectionX += a.velocityX;
                jumpDelays.Add(a.jumpDelay);

                if (a.platformType == "Breakable" || a.platformType == "Temporary")
                    riskCount++;

                if (a.velocityX != 0f && prevVX != 0f &&
                    Mathf.Sign(a.velocityX) != Mathf.Sign(prevVX))
                    directionChanges++;

                prevVX = a.velocityX;
            }

            int n = actions.Count;
            result.avgMoveSpeed = Mathf.Max(0.1f, (float)(sumSpeed / n));
            result.avgJumpDelay = Mathf.Max(0f, (float)(sumJumpDelay / n));
            result.directionBias = Mathf.Clamp((float)(sumDirectionX / n) / result.avgMoveSpeed, -1f, 1f);

            // Reaction time from recorder
            result.reactionTime = _recorder.LatestReactionTime > 0f
                ? _recorder.LatestReactionTime
                : profile.reactionTime;

            // Risk level
            result.riskLevel = Mathf.Clamp01((float)riskCount / n);

            // Jump precision: small std-dev → high precision
            float meanDelay = result.avgJumpDelay;
            double variance = 0;
            foreach (float d in jumpDelays)
                variance += (d - meanDelay) * (d - meanDelay);
            float stdDev = Mathf.Sqrt((float)(variance / jumpDelays.Count));
            result.jumpPrecision = Mathf.Clamp01(1f - Mathf.InverseLerp(0f, 0.5f, stdDev));

            // Movement smoothness: few direction changes → smooth
            float changeRate = (float)directionChanges / n;
            result.movementSmoothness = Mathf.Clamp01(1f - Mathf.InverseLerp(0f, 0.5f, changeRate));

            return result;
        }

        // ── Blending (evolution) ──────────────────────────────────────────────────
        private void BlendIntoProfile(AIProfile newData, float runScore)
        {
            float lr = learningRate;
            profile.avgMoveSpeed    = Lerp(profile.avgMoveSpeed,    newData.avgMoveSpeed,    lr);
            profile.avgJumpDelay    = Lerp(profile.avgJumpDelay,    newData.avgJumpDelay,    lr);
            profile.directionBias   = Lerp(profile.directionBias,   newData.directionBias,   lr);
            profile.reactionTime    = Lerp(profile.reactionTime,    newData.reactionTime,    lr);
            profile.riskLevel       = Lerp(profile.riskLevel,       newData.riskLevel,       lr);
            profile.jumpPrecision   = Lerp(profile.jumpPrecision,   newData.jumpPrecision,   lr);
            profile.movementSmoothness = Lerp(
                profile.movementSmoothness, newData.movementSmoothness, lr);

            profile.totalRunsAnalyzed++;
            if (runScore > profile.bestScoreEver)
                profile.bestScoreEver = runScore;
        }

        private float Lerp(float current, float target, float t) =>
            current + (target - current) * t;

        // ── Getters ───────────────────────────────────────────────────────────────
        public AIProfile GetProfile() => profile;
        public bool IsProfileReady => profile.totalRunsAnalyzed > 0;

        /// <summary>Resets profile to defaults (use when player wants a fresh AI).</summary>
        public void ResetProfile()
        {
            profile = new AIProfile();
            Debug.Log("[AITrainer] Profile reset.");
        }
    }
}
