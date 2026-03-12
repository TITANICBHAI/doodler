using UnityEngine;

namespace DoodleClimb.Game
{
    /// <summary>
    /// Tracks which game mode is active.
    ///
    /// Modes:
    ///   NormalPlay — classic endless climb, one character, AI records in background
    ///   VsAI       — player races their trained AI clone on the same level
    ///   WatchAI    — spectator mode; player is hidden, camera follows AI only,
    ///                run ends when AI falls
    /// </summary>
    public class GameModeManager : MonoBehaviour
    {
        public enum GameMode
        {
            NormalPlay,
            VsAI,
            WatchAI
        }

        private GameMode _currentMode = GameMode.NormalPlay;

        // ── Public API ────────────────────────────────────────────────────────────
        public void SetMode(GameMode mode)
        {
            _currentMode = mode;
            Debug.Log($"[GameModeManager] Mode → {mode}");
        }

        public GameMode CurrentMode  => _currentMode;
        public bool     IsNormalPlay => _currentMode == GameMode.NormalPlay;
        public bool     IsVsAI       => _currentMode == GameMode.VsAI;
        public bool     IsWatchAI    => _currentMode == GameMode.WatchAI;

        // ── Config helpers ────────────────────────────────────────────────────────
        public AI.AIPlayerController.AIMode RecommendedAIMode
        {
            get
            {
                switch (_currentMode)
                {
                    case GameMode.VsAI:
                    case GameMode.WatchAI:
                        return AI.AIPlayerController.AIMode.Hybrid;
                    default:
                        return AI.AIPlayerController.AIMode.BehaviourProfile;
                }
            }
        }

        public string ModeDisplayName
        {
            get
            {
                switch (_currentMode)
                {
                    case GameMode.NormalPlay: return "Normal Play";
                    case GameMode.VsAI:       return "vs AI Clone";
                    case GameMode.WatchAI:    return "Watch AI";
                    default: return "Unknown";
                }
            }
        }
    }
}
