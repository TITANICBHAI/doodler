using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DoodleClimb.UI
{
    /// <summary>
    /// Manages all UI panels and text displays.
    ///
    /// Panels (assign in Inspector):
    ///   startMenuPanel    — shown before first game
    ///   modeSelectPanel   — lets player choose Normal or vs-AI
    ///   hudPanel          — in-game heads-up display
    ///   gameOverPanel     — end-of-run results screen
    ///
    /// All panels have CanvasGroup components so they can be faded in/out.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Panels")]
        public CanvasGroup startMenuPanel;
        public CanvasGroup modeSelectPanel;
        public CanvasGroup hudPanel;
        public CanvasGroup gameOverPanel;

        [Header("Start Menu")]
        public TextMeshProUGUI appTitleText;
        public TextMeshProUGUI bestScoreText;
        public Button playNormalButton;
        public Button playVsAIButton;
        public Button modeSelectButton;

        [Header("Mode Select Panel")]
        public Button normalModeButton;
        public Button vsAIModeButton;
        public Button modeSelectBackButton;
        public TextMeshProUGUI aiProfileStatusText;

        [Header("HUD")]
        public TextMeshProUGUI playerScoreText;
        public TextMeshProUGUI aiScoreText;
        public TextMeshProUGUI playerScoreLabel;
        public TextMeshProUGUI aiScoreLabel;
        public GameObject aiScoreContainer;     // enable/disable for vs-AI mode

        [Header("Game Over")]
        public TextMeshProUGUI gameOverTitleText;
        public TextMeshProUGUI playerFinalScoreText;
        public TextMeshProUGUI aiFinalScoreText;
        public TextMeshProUGUI winnerText;
        public TextMeshProUGUI aiLearnedText;   // "AI has trained from X runs"
        public Button restartButton;
        public Button mainMenuButton;
        public GameObject aiFinalScoreContainer;

        // ── References ────────────────────────────────────────────────────────────
        private Game.GameManager _gameManager;
        private AI.AITrainer _trainer;

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            _gameManager = FindObjectOfType<Game.GameManager>();
            _trainer = FindObjectOfType<AI.AITrainer>();
        }

        private void Start()
        {
            // Wire buttons
            playNormalButton?.onClick.AddListener(OnPlayNormal);
            playVsAIButton?.onClick.AddListener(OnPlayVsAI);
            modeSelectButton?.onClick.AddListener(OnOpenModeSelect);
            normalModeButton?.onClick.AddListener(OnPlayNormal);
            vsAIModeButton?.onClick.AddListener(OnPlayVsAI);
            modeSelectBackButton?.onClick.AddListener(ShowStartMenu);
            restartButton?.onClick.AddListener(OnRestart);
            mainMenuButton?.onClick.AddListener(ShowStartMenu);
        }

        // ── Panel show/hide ───────────────────────────────────────────────────────
        public void ShowStartMenu()
        {
            SetPanel(startMenuPanel,  true);
            SetPanel(modeSelectPanel, false);
            SetPanel(hudPanel,        false);
            SetPanel(gameOverPanel,   false);

            // Update best score
            if (bestScoreText != null && _trainer != null)
            {
                float best = _trainer.GetProfile().bestScoreEver;
                bestScoreText.text = best > 0f
                    ? $"Best: {best:0}"
                    : "No runs yet";
            }
        }

        public void ShowModeSelect()
        {
            SetPanel(startMenuPanel,  false);
            SetPanel(modeSelectPanel, true);
            SetPanel(hudPanel,        false);
            SetPanel(gameOverPanel,   false);

            if (aiProfileStatusText != null && _trainer != null)
            {
                AI.AIProfile p = _trainer.GetProfile();
                if (p.totalRunsAnalyzed == 0)
                    aiProfileStatusText.text = "No AI data yet.\nPlay a run first!";
                else
                    aiProfileStatusText.text =
                        $"AI trained on {p.totalRunsAnalyzed} run(s).\n" +
                        $"Best score: {p.bestScoreEver:0}";
            }
        }

        public void ShowHUD(bool showAIScore)
        {
            SetPanel(startMenuPanel,  false);
            SetPanel(modeSelectPanel, false);
            SetPanel(hudPanel,        true);
            SetPanel(gameOverPanel,   false);

            if (aiScoreContainer != null)
                aiScoreContainer.SetActive(showAIScore);
        }

        public void ShowGameOver(
            float playerScore,
            float aiScore,
            string winner,
            bool aiHasTrained)
        {
            SetPanel(startMenuPanel,  false);
            SetPanel(modeSelectPanel, false);
            SetPanel(hudPanel,        false);
            SetPanel(gameOverPanel,   true);

            bool vsAI = aiScore >= 0f;

            if (gameOverTitleText != null)
                gameOverTitleText.text = "Game Over";

            if (playerFinalScoreText != null)
                playerFinalScoreText.text = $"Your Score: {playerScore:0}";

            if (aiFinalScoreContainer != null)
                aiFinalScoreContainer.SetActive(vsAI);

            if (vsAI && aiFinalScoreText != null)
                aiFinalScoreText.text = $"AI Score: {aiScore:0}";

            if (winnerText != null)
            {
                if (vsAI)
                    winnerText.text = winner == "You" ? "YOU WIN!" : "AI WINS!";
                else
                    winnerText.text = "";
            }

            if (aiLearnedText != null)
            {
                if (aiHasTrained && _trainer != null)
                {
                    int runs = _trainer.GetProfile().totalRunsAnalyzed;
                    aiLearnedText.text = $"AI has trained from {runs} run(s).";
                }
                else
                {
                    aiLearnedText.text = "";
                }
            }
        }

        // ── HUD live update ───────────────────────────────────────────────────────
        public void UpdateScoreDisplay(float playerScore, float aiScore)
        {
            if (playerScoreText != null)
                playerScoreText.text = $"{playerScore:0}";

            if (aiScoreText != null && aiScoreText.gameObject.activeInHierarchy)
                aiScoreText.text = $"{aiScore:0}";
        }

        // ── Button handlers ───────────────────────────────────────────────────────
        private void OnPlayNormal()
        {
            _gameManager?.StartGame(Game.GameModeManager.GameMode.NormalPlay);
        }

        private void OnPlayVsAI()
        {
            if (_trainer == null || !_trainer.IsProfileReady)
            {
                // Not enough data — show feedback and play normal first
                Debug.Log("[UIManager] No AI profile yet. Redirecting to normal play.");
                ShowNoAIDataMessage();
                return;
            }
            _gameManager?.StartGame(Game.GameModeManager.GameMode.VsAI);
        }

        private void OnOpenModeSelect()
        {
            ShowModeSelect();
        }

        private void OnRestart()
        {
            _gameManager?.RestartGame();
        }

        // ── Helpers ───────────────────────────────────────────────────────────────
        private void SetPanel(CanvasGroup group, bool visible)
        {
            if (group == null) return;
            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }

        private void ShowNoAIDataMessage()
        {
            // Temporarily override the AI profile status text
            if (aiProfileStatusText != null)
            {
                aiProfileStatusText.text =
                    "Play at least one normal run\nto train your AI clone first!";
            }
            ShowModeSelect();
        }
    }
}
