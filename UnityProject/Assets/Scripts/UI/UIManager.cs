using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DoodleClimb.UI
{
    /// <summary>
    /// Manages all UI panels, score display, combo counter, and game-state transitions.
    ///
    /// Panels: startMenuPanel | modeSelectPanel | hudPanel | gameOverPanel
    ///
    /// Combo display:
    ///   UpdateCombo(int) — shows a punch-scale label when the player chains landings.
    ///   Appears from combo ≥ 3, scales up at milestone thresholds (5, 8, 10, 15),
    ///   fades out automatically after 2 s with no new landing.
    ///
    /// Watch AI mode:
    ///   "WATCHING AI" banner replaces score; game over shows "AI Run Complete!".
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        // ── Panels ────────────────────────────────────────────────────────────────
        [Header("Panels")]
        public CanvasGroup startMenuPanel;
        public CanvasGroup modeSelectPanel;
        public CanvasGroup hudPanel;
        public CanvasGroup gameOverPanel;

        // ── Start Menu ────────────────────────────────────────────────────────────
        [Header("Start Menu")]
        public TextMeshProUGUI bestScoreText;
        public TextMeshProUGUI skillTierText;
        public Button          playNormalButton;
        public Button          playVsAIButton;
        public Button          modeSelectButton;

        // ── Mode Select ───────────────────────────────────────────────────────────
        [Header("Mode Select")]
        public Button          normalModeButton;
        public Button          vsAIModeButton;
        public Button          watchAIButton;
        public Button          modeSelectBackButton;
        public TextMeshProUGUI aiProfileStatusText;

        // ── HUD ───────────────────────────────────────────────────────────────────
        [Header("HUD")]
        public TextMeshProUGUI playerScoreText;
        public TextMeshProUGUI aiScoreText;
        public GameObject      aiScoreContainer;
        public TextMeshProUGUI watchingAILabel;

        [Header("HUD — Combo")]
        public TextMeshProUGUI comboText;

        [Header("HUD — AI Challenge")]
        public GameObject      challengeContainer;
        public TextMeshProUGUI challengeTargetText;
        public TextMeshProUGUI challengeProgressText;

        // ── Game Over ─────────────────────────────────────────────────────────────
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

        // ── Combo state ───────────────────────────────────────────────────────────
        private float   _comboTimer;
        private int[]   _comboThresholds = { 3, 5, 8, 10, 15 };
        private bool    _comboVisible;

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

        private void Update()
        {
            if (_comboTimer <= 0f) return;
            _comboTimer -= Time.deltaTime;

            if (comboText != null)
            {
                // Restore punch scale smoothly
                comboText.transform.localScale = Vector3.Lerp(
                    comboText.transform.localScale, Vector3.one, 16f * Time.deltaTime);

                // Fade out in last 0.45 s
                if (_comboTimer < 0.45f && _comboVisible)
                {
                    Color c = comboText.color;
                    c.a             = _comboTimer / 0.45f;
                    comboText.color = c;
                }

                if (_comboTimer <= 0f)
                {
                    _comboVisible = false;
                    comboText.gameObject.SetActive(false);
                }
            }
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

            bool aiReady = _trainer != null && _trainer.IsProfileReady;
            if (watchAIButton  != null) watchAIButton.interactable  = aiReady;
            if (vsAIModeButton != null) vsAIModeButton.interactable = aiReady;
        }

        public void ShowHUD(bool showAIScore, bool isWatchAI, float challengeTarget)
        {
            SetPanel(startMenuPanel,  false);
            SetPanel(modeSelectPanel, false);
            SetPanel(hudPanel,        true);
            SetPanel(gameOverPanel,   false);

            _isVsAIMode    = showAIScore;
            _isWatchAIMode = isWatchAI;
            _challengeTarget = challengeTarget;

            // AI score container (vs-AI and Watch-AI modes)
            if (aiScoreContainer != null)
                aiScoreContainer.SetActive(showAIScore || isWatchAI);

            // "WATCHING AI" banner
            if (watchingAILabel != null)
                watchingAILabel.gameObject.SetActive(isWatchAI);

            // Player score text hidden in Watch AI mode
            if (playerScoreText != null)
                playerScoreText.gameObject.SetActive(!isWatchAI);

            // Combo (always hidden at start of run)
            HideComboImmediate();

            // Challenge bar (Normal mode, once profile exists)
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

            HideComboImmediate();

            bool vsAI = aiScore >= 0f && !isWatchAI;

            if (gameOverTitleText != null)
                gameOverTitleText.text = isWatchAI ? "AI Run Complete!" : "Game Over";

            if (playerFinalScoreText != null)
                playerFinalScoreText.text = isWatchAI
                    ? $"AI Score: {playerScore:0}"
                    : $"Your Score: {playerScore:0}";

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
                if (!isWatchAI && aiHasTrained && _trainer != null)
                    aiLearnedText.text =
                        $"AI trained from {_trainer.GetProfile().totalRunsAnalyzed} run(s).";
                else if (isWatchAI)
                    aiLearnedText.text = "Watch another run to see the AI improve!";
                else
                    aiLearnedText.text = "";
            }

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
                    float nt = _trainer.GetProfile().challengeTargetScore;
                    newTargetText.text = beat ? $"New target: {nt:0}" : "";
                }
            }

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

        // ── Combo display ─────────────────────────────────────────────────────────
        public void UpdateCombo(int combo)
        {
            if (comboText == null) return;

            if (combo >= 3)
            {
                // Determine label and colour
                string label;
                Color  col;
                if (combo >= 15)      { label = $"INSANE x{combo}!"; col = new Color(1.0f, 0.2f, 0.0f); }
                else if (combo >= 10) { label = $"ON FIRE x{combo}!";  col = new Color(1.0f, 0.5f, 0.0f); }
                else if (combo >= 5)  { label = $"COMBO x{combo}!";   col = new Color(1.0f, 0.90f, 0.1f); }
                else                  { label = $"x{combo}!";          col = new Color(0.5f, 1.0f, 0.8f); }

                // Check if this is a threshold hit — punch scale
                bool isThreshold = System.Array.IndexOf(_comboThresholds, combo) >= 0;

                col.a           = 1f;
                comboText.color = col;
                comboText.text  = label;

                if (!_comboVisible || isThreshold)
                {
                    comboText.gameObject.SetActive(true);
                    _comboVisible = true;
                }

                if (isThreshold)
                    comboText.transform.localScale = Vector3.one * 1.45f;

                _comboTimer = isThreshold ? 2.0f : Mathf.Max(_comboTimer, 1.6f);
            }
            else
            {
                // Combo below threshold — let existing display fade out naturally
                if (_comboTimer <= 0f)
                    HideComboImmediate();
            }
        }

        private void HideComboImmediate()
        {
            _comboVisible = false;
            _comboTimer   = 0f;
            if (comboText != null)
            {
                comboText.gameObject.SetActive(false);
                comboText.transform.localScale = Vector3.one;
            }
        }

        // ── HUD live update ───────────────────────────────────────────────────────
        public void UpdateScoreDisplay(
            float playerScore, float aiScore, float challengeTarget)
        {
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
        private void OnPlayNormal() =>
            _gameManager?.StartGame(Game.GameModeManager.GameMode.NormalPlay);

        private void OnPlayVsAI()
        {
            if (_trainer == null || !_trainer.IsProfileReady) { ShowNoAIDataMessage(); return; }
            _gameManager?.StartGame(Game.GameModeManager.GameMode.VsAI);
        }

        private void OnWatchAI()
        {
            if (_trainer == null || !_trainer.IsProfileReady) { ShowNoAIDataMessage(); return; }
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
