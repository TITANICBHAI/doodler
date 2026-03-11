using System.Collections.Generic;
using UnityEngine;

namespace DoodleClimb.AI
{
    // ── Data structures ───────────────────────────────────────────────────────────

    /// <summary>
    /// Statistical AI profile built from multiple runs.
    /// Updated every run using an exponential moving average so the AI
    /// gradually evolves to match (and eventually exceed) the player.
    /// </summary>
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

        [Header("Skill Metrics (0 = weak, 1 = expert)")]
        [Range(0f, 1f)]
        public float riskLevel = 0.5f;          // how often player attempts risky jumps
        [Range(0f, 1f)]
        public float jumpPrecision = 0.5f;      // consistency of jump timing
        [Range(0f, 1f)]
        public float movementSmoothness = 0.5f; // how rarely the player reverses direction
        [Range(0f, 1f)]
        public float landingAccuracy = 0.5f;    // how close to platform centre landings are
        [Range(0f, 1f)]
        public float distancePrecision = 0.5f;  // how well the player reads gap distances

        [Header("Evolution")]
        public int   totalRunsAnalyzed = 0;
        public float bestScoreEver     = 0f;

        /// <summary>
        /// The score the AI will set as its challenge target for the next vs-AI run.
        /// Starts at bestScoreEver + 5% — increases by 5% each time the AI beats the player.
        /// </summary>
        public float challengeTargetScore = 0f;

        /// <summary>Human-readable skill tier based on jumpPrecision + movementSmoothness.</summary>
        public string SkillTier
        {
            get
            {
                float skill = (jumpPrecision + movementSmoothness + landingAccuracy) / 3f;
                if (skill > 0.85f) return "Expert";
                if (skill > 0.65f) return "Advanced";
                if (skill > 0.45f) return "Intermediate";
                if (skill > 0.25f) return "Beginner";
                return "Novice";
            }
        }

        public override string ToString()
        {
            return $"AIProfile | Runs:{totalRunsAnalyzed} | Best:{bestScoreEver:0} | " +
                   $"Speed:{avgMoveSpeed:0.0} | Bias:{directionBias:+0.00;-0.00;0} | " +
                   $"Reaction:{reactionTime:0.000}s | Skill:{SkillTier}";
        }
    }

    /// <summary>
    /// Analyses recorded PlayerActionData (including environment state and outcomes)
    /// to produce or update an AIProfile.
    ///
    /// Key computations:
    ///   avgMoveSpeed       — mean |velocityX| across all action samples
    ///   avgJumpDelay       — mean time between landing and jumping
    ///   directionBias      — net rightward tendency of horizontal velocity
    ///   reactionTime       — mean recorded reaction to moving platforms
    ///   riskLevel          — proportion of Breakable/Temporary platform attempts
    ///   jumpPrecision      — inverse of std-dev of jumpDelay (normalised)
    ///   movementSmoothness — inverse of mean direction-change rate (normalised)
    ///   landingAccuracy    — inverse of mean landing error (how close to platform centre)
    ///   distancePrecision  — how accurately the player gauges gap distances (jump success rate)
    ///
    /// Uses exponential moving average blending so each run shifts the profile
    /// gradually — the AI gets stronger over many runs rather than resetting.
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

        [Header("AI Challenge")]
        [Tooltip("How much above the player's best score the AI challenge target is set, as a multiplier.")]
        public float challengeScoreMultiplier = 1.05f;  // default: AI target = best × 1.05

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
        /// Call at the end of each player run to update the AI profile.
        /// runScore is the player's height score this run.
        /// aiWon is true if the AI clone beat the player in a vs-AI run.
        /// </summary>
        public void TrainFromLatestRun(float runScore, bool aiWon = false)
        {
            List<PlayerActionData> actions = _recorder.GetActionHistory();

            if (actions.Count < minSamplesRequired)
            {
                Debug.Log($"[AITrainer] Not enough samples ({actions.Count}). Skipping training.");
                return;
            }

            AIProfile newData = AnalyseActions(actions);
            BlendIntoProfile(newData, runScore, aiWon);

            Debug.Log($"[AITrainer] Profile updated. {profile}");
        }

        // ── Analysis ──────────────────────────────────────────────────────────────
        private AIProfile AnalyseActions(List<PlayerActionData> actions)
        {
            AIProfile result = new AIProfile();

            double sumSpeed          = 0;
            double sumJumpDelay      = 0;
            double sumDirectionX     = 0;
            int    riskCount         = 0;
            int    successCount      = 0;
            double sumLandingError   = 0;
            int    landingCount      = 0;
            double sumDistancePrecision = 0;
            int    distanceCount     = 0;

            List<float> jumpDelays = new List<float>();
            float prevVX = 0f;
            int   directionChanges = 0;

            foreach (PlayerActionData a in actions)
            {
                sumSpeed      += Mathf.Abs(a.velocityX);
                sumJumpDelay  += a.jumpDelay;
                sumDirectionX += a.velocityX;
                jumpDelays.Add(a.jumpDelay);

                if (a.platformType == "Breakable" || a.platformType == "Temporary")
                    riskCount++;

                if (a.velocityX != 0f && prevVX != 0f &&
                    Mathf.Sign(a.velocityX) != Mathf.Sign(prevVX))
                    directionChanges++;

                // Outcomes
                if (a.outcome == JumpOutcome.SuccessfulLanding)
                {
                    successCount++;
                    sumLandingError += a.landingError;
                    landingCount++;
                }

                // Distance precision: was the jump distance close to the actual gap?
                if (a.distanceToPlatform > 0f)
                {
                    // Normalise: perfect = 1.0, terrible = 0.0
                    float precision = a.outcome == JumpOutcome.SuccessfulLanding ? 1f : 0f;
                    sumDistancePrecision += precision;
                    distanceCount++;
                }

                prevVX = a.velocityX;
            }

            int n = actions.Count;

            result.avgMoveSpeed  = Mathf.Max(0.1f, (float)(sumSpeed / n));
            result.avgJumpDelay  = Mathf.Max(0f, (float)(sumJumpDelay / n));
            result.directionBias = Mathf.Clamp(
                (float)(sumDirectionX / n) / result.avgMoveSpeed, -1f, 1f);

            result.reactionTime = _recorder.LatestReactionTime > 0f
                ? _recorder.LatestReactionTime
                : profile.reactionTime;

            result.riskLevel = Mathf.Clamp01((float)riskCount / n);

            // Jump precision: tight std-dev → high precision
            float meanDelay = result.avgJumpDelay;
            double variance  = 0;
            foreach (float d in jumpDelays)
                variance += (d - meanDelay) * (d - meanDelay);
            float stdDev = jumpDelays.Count > 1
                ? Mathf.Sqrt((float)(variance / jumpDelays.Count)) : 0f;
            result.jumpPrecision =
                Mathf.Clamp01(1f - Mathf.InverseLerp(0f, 0.5f, stdDev));

            // Movement smoothness: few direction changes → smooth
            float changeRate = (float)directionChanges / n;
            result.movementSmoothness =
                Mathf.Clamp01(1f - Mathf.InverseLerp(0f, 0.5f, changeRate));

            // Landing accuracy: small error → high accuracy (max perfect error ~0.2 units)
            result.landingAccuracy = landingCount > 0
                ? Mathf.Clamp01(1f - Mathf.InverseLerp(0f, 0.5f,
                    (float)(sumLandingError / landingCount)))
                : 0.5f;

            // Distance precision: fraction of jumps that land successfully
            result.distancePrecision = distanceCount > 0
                ? Mathf.Clamp01((float)(sumDistancePrecision / distanceCount))
                : 0.5f;

            return result;
        }

        // ── Blending (evolution) ──────────────────────────────────────────────────
        private void BlendIntoProfile(AIProfile newData, float runScore, bool aiWon)
        {
            float lr = learningRate;
            profile.avgMoveSpeed       = Lerp(profile.avgMoveSpeed,       newData.avgMoveSpeed,       lr);
            profile.avgJumpDelay       = Lerp(profile.avgJumpDelay,       newData.avgJumpDelay,       lr);
            profile.directionBias      = Lerp(profile.directionBias,      newData.directionBias,      lr);
            profile.reactionTime       = Lerp(profile.reactionTime,       newData.reactionTime,       lr);
            profile.riskLevel          = Lerp(profile.riskLevel,          newData.riskLevel,          lr);
            profile.jumpPrecision      = Lerp(profile.jumpPrecision,      newData.jumpPrecision,      lr);
            profile.movementSmoothness = Lerp(profile.movementSmoothness, newData.movementSmoothness, lr);
            profile.landingAccuracy    = Lerp(profile.landingAccuracy,    newData.landingAccuracy,    lr);
            profile.distancePrecision  = Lerp(profile.distancePrecision,  newData.distancePrecision,  lr);

            profile.totalRunsAnalyzed++;

            if (runScore > profile.bestScoreEver)
                profile.bestScoreEver = runScore;

            // Update the AI Challenge target
            // If the AI won, raise the challenge target further (AI keeps improving)
            float multiplier = aiWon
                ? challengeScoreMultiplier * 1.05f
                : challengeScoreMultiplier;

            profile.challengeTargetScore =
                Mathf.Max(profile.bestScoreEver * multiplier, profile.bestScoreEver + 10f);
        }

        private float Lerp(float current, float target, float t) =>
            current + (target - current) * t;

        // ── Getters ───────────────────────────────────────────────────────────────
        public AIProfile GetProfile()      => profile;
        public bool      IsProfileReady    => profile.totalRunsAnalyzed > 0;

        public void ResetProfile()
        {
            profile = new AIProfile();
            Debug.Log("[AITrainer] Profile reset.");
        }
    }
}
