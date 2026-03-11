using UnityEngine;

namespace DoodleClimb.Game
{
    /// <summary>
    /// Tracks which game mode is active and exposes helpers that other
    /// systems query to configure themselves appropriately.
    ///
    /// Modes:
    ///   NormalPlay — classic endless climb, one character, no AI
    ///   VsAI       — player races their trained AI clone on the same level
    /// </summary>
    public class GameModeManager : MonoBehaviour
    {
        public enum GameMode
        {
            NormalPlay,
            VsAI
        }

        // ── State ─────────────────────────────────────────────────────────────────
        private GameMode _currentMode = GameMode.NormalPlay;

        // ── Public API ────────────────────────────────────────────────────────────
        public void SetMode(GameMode mode)
        {
            _currentMode = mode;
            Debug.Log($"[GameModeManager] Mode set to: {mode}");
        }

        public GameMode CurrentMode => _currentMode;
        public bool IsVsAI => _currentMode == GameMode.VsAI;
        public bool IsNormalPlay => _currentMode == GameMode.NormalPlay;

        // ── Config helpers ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the AI controller mode that should be used for this game mode.
        /// VsAI defaults to Hybrid (ghost + behaviour).
        /// </summary>
        public AI.AIPlayerController.AIMode RecommendedAIMode
        {
            get
            {
                switch (_currentMode)
                {
                    case GameMode.VsAI:
                        return AI.AIPlayerController.AIMode.Hybrid;
                    default:
                        return AI.AIPlayerController.AIMode.BehaviourProfile;
                }
            }
        }

        /// <summary>
        /// Human-readable label for the current mode, shown in the UI.
        /// </summary>
        public string ModeDisplayName
        {
            get
            {
                switch (_currentMode)
                {
                    case GameMode.NormalPlay: return "Normal Play";
                    case GameMode.VsAI:       return "vs AI Clone";
                    default: return "Unknown";
                }
            }
        }
    }
}
