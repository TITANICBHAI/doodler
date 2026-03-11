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
    /// AI Challenge Mode:
    ///   The HUD shows the player's current score AND the AI's challenge target.
    ///   "Your best: 1320 | Target: 1386"
    ///   This gives the player a concrete goal on every run.
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
        public TextMeshProUGUI skillTierText;     // shows player's AI skill tier
        public Button          playNormalButton;
        public Button          playVsAIButton;
        public Button          modeSelectButton;

        [Header("Mode Select Panel")]
        public Button          normalModeButton;
        public Button          vsAIModeButton;
        public Button          modeSelectBackButton;
        public TextMeshProUGUI aiProfileStatusText;

        [Header("HUD")]
        public TextMeshProUGUI playerScoreText;
        public TextMeshProUGUI aiScoreText;
        public GameObject      aiScoreContainer;       // shown only in vs-AI mode

        [Header("HUD — AI Challenge")]
        public GameObject      challengeContainer;     // shown in Normal mode when profile exists
        public TextMeshProUGUI challengeTargetText;    // "Target: 1386"
        public TextMeshProUGUI challengeProgressText;  // "1320 / 1386"

        [Header("Game Over")]
        public TextMeshProUGUI gameOverTitleText;
        public TextMeshProUGUI playerFinalScoreText;
        public TextMeshProUGUI aiFinalScoreText;
        public TextMeshProUGUI winnerText;
        public TextMeshProUGUI aiLearnedText;          // "AI trained from X runs"
        public GameObject      aiFinalScoreContainer;

        [Header("Game Over — AI Challenge")]
        public GameObject      challengeResultContainer;
        public TextMeshProUGUI challengeResultText;    // "Beat the target!" / "Try again!"
        public TextMeshProUGUI newTargetText;          // "New target: 1455"

        [Header("Game Over — Skill Breakdown")]
        public GameObject      skillBreakdownContainer;
        public TextMeshProUGUI skillTierResultText;
        public TextMeshProUGUI jumpPrecisionText;
        public TextMeshProUGUI movementSmoothnessText;
        public TextMeshProUGUI landingAccuracyText;
        public TextMeshProUGUI riskLevelText;

        [Header("Buttons")]
        public Button restartButton;
        public Button mainMenuButton;

        // ── References ────────────────────────────────────────────────────────────
        private Game.GameManager _gameManager;
        private AI.AITrainer     _trainer;
        private bool _isVsAIMode;
        private float _challengeTarget;

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            _gameManager = FindObjectOfType<Game.GameManager>();
            _trainer     = FindObjectOfType<AI.AITrainer>();
        }

        private void Start()
        {
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

            if (_trainer != null)
            {
                AI.AIProfile p = _trainer.GetProfile();

                if (bestScoreText != null)
                    bestScoreText.text = p.bestScoreEver > 0f
                        ? $"Best: {p.bestScoreEver:0}"
                        : "No runs yet";

                if (skillTierText != null)
                    skillTierText.text = p.totalRunsAnalyzed > 0
                        ? $"Skill: {p.SkillTier}"
                        : "";
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
                    aiProfileStatusText.text = "No AI data yet.\nPlay a Normal run first!";
                else
                    aiProfileStatusText.text =
                        $"AI trained on {p.totalRunsAnalyzed} run(s).\n" +
                        $"Skill tier: {p.SkillTier}  |  Best: {p.bestScoreEver:0}";
            }
        }

        /// <param name="showAIScore">True in vs-AI mode.</param>
        /// <param name="challengeTarget">Score the AI challenge targets (0 = no target yet).</param>
        public void ShowHUD(bool showAIScore, float challengeTarget)
        {
            SetPanel(startMenuPanel,  false);
            SetPanel(modeSelectPanel, false);
            SetPanel(hudPanel,        true);
            SetPanel(gameOverPanel,   false);

            _isVsAIMode      = showAIScore;
            _challengeTarget = challengeTarget;

            if (aiScoreContainer != null)
                aiScoreContainer.SetActive(showAIScore);

            // Show AI challenge bar in Normal mode when a target exists
            bool showChallenge = !showAIScore && challengeTarget > 0f;
            if (challengeContainer != null)
                challengeContainer.SetActive(showChallenge);

            if (showChallenge && challengeTargetText != null)
                challengeTargetText.text = $"Target: {challengeTarget:0}";
        }

        public void ShowGameOver(
            float  playerScore,
            float  aiScore,
            string winner,
            bool   aiHasTrained,
            float  challengeTarget)
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
                winnerText.text = vsAI
                    ? (winner == "You" ? "YOU WIN!" : "AI WINS!")
                    : "";

            if (aiLearnedText != null)
            {
                if (aiHasTrained && _trainer != null)
                {
                    int runs = _trainer.GetProfile().totalRunsAnalyzed;
                    aiLearnedText.text = $"AI trained from {runs} run(s).";
                }
                else
                {
                    aiLearnedText.text = "";
                }
            }

            // ── AI Challenge result ────────────────────────────────────────────────
            bool showChallengeResult = !vsAI && challengeTarget > 0f;
            if (challengeResultContainer != null)
                challengeResultContainer.SetActive(showChallengeResult);

            if (showChallengeResult)
            {
                bool beatTarget = playerScore >= challengeTarget;

                if (challengeResultText != null)
                    challengeResultText.text = beatTarget
                        ? "Challenge Beaten!"
                        : $"Target: {challengeTarget:0}  |  You: {playerScore:0}";

                if (newTargetText != null && _trainer != null)
                {
                    float newTarget = _trainer.GetProfile().challengeTargetScore;
                    newTargetText.text = beatTarget
                        ? $"New target: {newTarget:0}"
                        : "";
                }
            }

            // ── Skill breakdown ────────────────────────────────────────────────────
            bool showSkill = aiHasTrained && _trainer != null;
            if (skillBreakdownContainer != null)
                skillBreakdownContainer.SetActive(showSkill);

            if (showSkill)
            {
                AI.AIProfile p = _trainer.GetProfile();

                if (skillTierResultText    != null) skillTierResultText.text    = $"Skill: {p.SkillTier}";
                if (jumpPrecisionText      != null) jumpPrecisionText.text      = $"Jump Precision:  {p.jumpPrecision * 100f:0}%";
                if (movementSmoothnessText != null) movementSmoothnessText.text = $"Smoothness:      {p.movementSmoothness * 100f:0}%";
                if (landingAccuracyText    != null) landingAccuracyText.text    = $"Landing Accuracy:{p.landingAccuracy * 100f:0}%";
                if (riskLevelText          != null) riskLevelText.text          = $"Risk Level:      {p.riskLevel * 100f:0}%";
            }
        }

        // ── HUD live update ───────────────────────────────────────────────────────
        public void UpdateScoreDisplay(
            float playerScore, float aiScore, float challengeTarget)
        {
            if (playerScoreText != null)
                playerScoreText.text = $"{playerScore:0}";

            if (aiScoreText != null && aiScoreText.gameObject.activeInHierarchy)
                aiScoreText.text = $"{aiScore:0}";

            // Challenge progress bar text (Normal mode)
            if (!_isVsAIMode && challengeProgressText != null
                && challengeTarget > 0f
                && challengeProgressText.gameObject.activeInHierarchy)
            {
                challengeProgressText.text =
                    $"{playerScore:0} / {challengeTarget:0}";
            }
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
                ShowNoAIDataMessage();
                return;
            }
            _gameManager?.StartGame(Game.GameModeManager.GameMode.VsAI);
        }

        private void OnOpenModeSelect() => ShowModeSelect();
        private void OnRestart()        => _gameManager?.RestartGame();

        // ── Helpers ───────────────────────────────────────────────────────────────
        private void SetPanel(CanvasGroup group, bool visible)
        {
            if (group == null) return;
            group.alpha          = visible ? 1f : 0f;
            group.interactable   = visible;
            group.blocksRaycasts = visible;
        }

        private void ShowNoAIDataMessage()
        {
            if (aiProfileStatusText != null)
                aiProfileStatusText.text =
                    "Play at least one Normal run\nto train your AI clone first!";
            ShowModeSelect();
        }
    }
}
