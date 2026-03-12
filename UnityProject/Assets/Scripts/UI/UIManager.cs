using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DoodleClimb.UI
{
    /// <summary>
    /// Manages all UI panels and text displays.
    ///
    /// Panels:
    ///   startMenuPanel  — shown before first game
    ///   modeSelectPanel — Normal / vs AI / Watch AI mode buttons
    ///   hudPanel        — in-game heads-up display
    ///   gameOverPanel   — end-of-run results screen
    ///
    /// Watch AI Mode:
    ///   A "WATCHING AI" label appears in the HUD so it's always clear
    ///   you're in spectator mode. The game over screen shows "AI Run Complete!"
    ///   and the AI's score as the headline number.
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
        public TextMeshProUGUI bestScoreText;
        public TextMeshProUGUI skillTierText;
        public Button          playNormalButton;
        public Button          playVsAIButton;
        public Button          modeSelectButton;

        [Header("Mode Select Panel")]
        public Button          normalModeButton;
        public Button          vsAIModeButton;
        public Button          watchAIButton;       // NEW — spectate the AI
        public Button          modeSelectBackButton;
        public TextMeshProUGUI aiProfileStatusText;

        [Header("HUD")]
        public TextMeshProUGUI playerScoreText;
        public TextMeshProUGUI aiScoreText;
        public GameObject      aiScoreContainer;    // shown in vs-AI mode
        public TextMeshProUGUI watchingAILabel;     // NEW — "WATCHING AI" banner

        [Header("HUD — AI Challenge")]
        public GameObject      challengeContainer;
        public TextMeshProUGUI challengeTargetText;
        public TextMeshProUGUI challengeProgressText;

        [Header("Game Over")]
        public TextMeshProUGUI gameOverTitleText;
        public TextMeshProUGUI playerFinalScoreText;
        public TextMeshProUGUI aiFinalScoreText;
        public TextMeshProUGUI winnerText;
        public TextMeshProUGUI aiLearnedText;
        public GameObject      aiFinalScoreContainer;

        [Header("Game Over — AI Challenge")]
        public GameObject      challengeResultContainer;
        public TextMeshProUGUI challengeResultText;
        public TextMeshProUGUI newTargetText;

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
        private bool             _isVsAIMode;
        private bool             _isWatchAIMode;
        private float            _challengeTarget;

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
            watchAIButton?.onClick.AddListener(OnWatchAI);
            modeSelectButton?.onClick.AddListener(OnOpenModeSelect);
            normalModeButton?.onClick.AddListener(OnPlayNormal);
            vsAIModeButton?.onClick.AddListener(OnPlayVsAI);
            modeSelectBackButton?.onClick.AddListener(ShowStartMenu);
            restartButton?.onClick.AddListener(OnRestart);
            mainMenuButton?.onClick.AddListener(ShowStartMenu);
        }

        // ── Panel show / hide ─────────────────────────────────────────────────────
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
                aiProfileStatusText.text = p.totalRunsAnalyzed == 0
                    ? "No AI data yet.\nPlay a Normal run first!"
                    : $"AI trained on {p.totalRunsAnalyzed} run(s).\n" +
                      $"Skill: {p.SkillTier}  |  Best: {p.bestScoreEver:0}";
            }

            // Watch AI and vs-AI buttons only usable once AI is trained
            bool aiReady = _trainer != null && _trainer.IsProfileReady;
            if (watchAIButton != null) watchAIButton.interactable = aiReady;
            if (vsAIModeButton != null) vsAIModeButton.interactable = aiReady;
        }

        /// <summary>
        /// Show the HUD.
        /// showAIScore — true in vs-AI mode (shows AI score alongside player score).
        /// isWatchAI   — true in Watch AI mode (shows "WATCHING AI" banner).
        /// challengeTarget — challenge score target, 0 = none.
        /// </summary>
        public void ShowHUD(bool showAIScore, bool isWatchAI, float challengeTarget)
        {
            SetPanel(startMenuPanel,  false);
            SetPanel(modeSelectPanel, false);
            SetPanel(hudPanel,        true);
            SetPanel(gameOverPanel,   false);

            _isVsAIMode    = showAIScore;
            _isWatchAIMode = isWatchAI;
            _challengeTarget = challengeTarget;

            // AI score bar (vs-AI mode only)
            if (aiScoreContainer != null)
                aiScoreContainer.SetActive(showAIScore);

            // "WATCHING AI" banner
            if (watchingAILabel != null)
                watchingAILabel.gameObject.SetActive(isWatchAI);

            // Rename player score label in watch mode
            if (playerScoreText != null)
                playerScoreText.gameObject.SetActive(!isWatchAI);

            // AI challenge bar (Normal mode only, once profile exists)
            bool showChallenge = !showAIScore && !isWatchAI && challengeTarget > 0f;
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
            float  challengeTarget,
            bool   isWatchAI = false)
        {
            SetPanel(startMenuPanel,  false);
            SetPanel(modeSelectPanel, false);
            SetPanel(hudPanel,        false);
            SetPanel(gameOverPanel,   true);

            bool vsAI = aiScore >= 0f && !isWatchAI;

            // ── Title ──────────────────────────────────────────────────────────────
            if (gameOverTitleText != null)
                gameOverTitleText.text = isWatchAI ? "AI Run Complete!" : "Game Over";

            // ── Scores ────────────────────────────────────────────────────────────
            if (playerFinalScoreText != null)
                playerFinalScoreText.text = isWatchAI
                    ? $"AI Score: {playerScore:0}"   // playerScore holds AI score here
                    : $"Your Score: {playerScore:0}";

            if (aiFinalScoreContainer != null)
                aiFinalScoreContainer.SetActive(vsAI);
            if (vsAI && aiFinalScoreText != null)
                aiFinalScoreText.text = $"AI Score: {aiScore:0}";

            // ── Winner ────────────────────────────────────────────────────────────
            if (winnerText != null)
                winnerText.text = vsAI
                    ? (winner == "You" ? "YOU WIN!" : "AI WINS!")
                    : "";

            // ── AI learning feedback ───────────────────────────────────────────────
            if (aiLearnedText != null)
            {
                if (!isWatchAI && aiHasTrained && _trainer != null)
                {
                    int runs = _trainer.GetProfile().totalRunsAnalyzed;
                    aiLearnedText.text = $"AI trained from {runs} run(s).";
                }
                else if (isWatchAI)
                    aiLearnedText.text = "Watch another run to see the AI improve!";
                else
                    aiLearnedText.text = "";
            }

            // ── Challenge result ───────────────────────────────────────────────────
            bool showChallengeResult = !vsAI && !isWatchAI && challengeTarget > 0f;
            if (challengeResultContainer != null)
                challengeResultContainer.SetActive(showChallengeResult);

            if (showChallengeResult)
            {
                bool beat = playerScore >= challengeTarget;
                if (challengeResultText != null)
                    challengeResultText.text = beat
                        ? "Challenge Beaten!"
                        : $"Target: {challengeTarget:0}  |  You: {playerScore:0}";

                if (newTargetText != null && _trainer != null)
                {
                    float newTarget = _trainer.GetProfile().challengeTargetScore;
                    newTargetText.text = beat ? $"New target: {newTarget:0}" : "";
                }
            }

            // ── Skill breakdown ────────────────────────────────────────────────────
            bool showSkill = !isWatchAI && aiHasTrained && _trainer != null;
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
            // In Watch AI mode show AI's score in the AI score label
            if (_isWatchAIMode)
            {
                if (aiScoreText != null)
                    aiScoreText.text = $"AI: {aiScore:0}";
                return;
            }

            if (playerScoreText != null)
                playerScoreText.text = $"{playerScore:0}";

            if (_isVsAIMode && aiScoreText != null
                && aiScoreText.gameObject.activeInHierarchy)
                aiScoreText.text = $"{aiScore:0}";

            if (!_isVsAIMode && challengeProgressText != null
                && challengeTarget > 0f
                && challengeProgressText.gameObject.activeInHierarchy)
                challengeProgressText.text = $"{playerScore:0} / {challengeTarget:0}";
        }

        // ── Button handlers ───────────────────────────────────────────────────────
        private void OnPlayNormal()
        {
            _gameManager?.StartGame(Game.GameModeManager.GameMode.NormalPlay);
        }

        private void OnPlayVsAI()
        {
            if (_trainer == null || !_trainer.IsProfileReady)
            { ShowNoAIDataMessage(); return; }
            _gameManager?.StartGame(Game.GameModeManager.GameMode.VsAI);
        }

        private void OnWatchAI()
        {
            if (_trainer == null || !_trainer.IsProfileReady)
            { ShowNoAIDataMessage(); return; }
            _gameManager?.StartGame(Game.GameModeManager.GameMode.WatchAI);
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
