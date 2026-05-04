using UnityEngine;

namespace DoodleClimb.Game
{
    /// <summary>
    /// Central coordinator — manages the game loop, death detection, score tracking,
    /// and bridges all major systems (Spawner, Camera, Recorder, Trainer).
    ///
    /// Game Modes:
    ///   NormalPlay — solo endless climb; AI records silently in the background
    ///   VsAI       — player races their trained AI clone on the same level
    ///   WatchAI    — spectator mode; player is hidden, camera follows AI only,
    ///                run ends when the AI falls
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────────
        public static GameManager Instance { get; private set; }

        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Characters")]
        public Player.PlayerController player;
        public AI.AIPlayerController   aiPlayer;

        [Header("Systems")]
        public Platforms.PlatformSpawner platformSpawner;
        public EnemySpawner              enemySpawner;
        public CameraFollow              cameraFollow;
        public UI.UIManager              uiManager;

        [Header("Spawn Points")]
        public Transform playerSpawnPoint;
        public Transform aiPlayerSpawnPoint;

        [Header("Death Zone")]
        [Tooltip("Extra units below camera bottom before death is triggered.")]
        public float deathYOffset = 2f;

        // ── Internal state ────────────────────────────────────────────────────────
        private GameModeManager _modeManager;
        private AI.AIRecorder   _recorder;
        private AI.AITrainer    _trainer;

        private float _playerScore;
        private float _aiScore;
        private float _playerMaxY;
        private float _aiMaxY;
        private bool  _gameRunning;

        // ── Stat tracking ─────────────────────────────────────────────────────────
        private int _coinsCollected;
        private int _gemsCollected;
        private int _bossKills;
        private int _enemiesDefeated;
        private int _batsKilled;
        private int _wormholesUsed;
        private int _nearMisses;

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _modeManager = GetComponent<GameModeManager>();
            _recorder    = GetComponent<AI.AIRecorder>();
            _trainer     = GetComponent<AI.AITrainer>();

            if (_recorder == null) _recorder = FindObjectOfType<AI.AIRecorder>();
            if (_trainer  == null) _trainer  = FindObjectOfType<AI.AITrainer>();
        }

        private void Start()
        {
            // Wire death events once — never inside StartGame (prevents duplicates)
            if (player   != null) player.OnDied   += OnPlayerDied;
            if (aiPlayer != null) aiPlayer.OnDied += OnAIDied;

            uiManager?.ShowStartMenu();
        }

        private void Update()
        {
            if (!_gameRunning) return;

            UpdateScores();
            CheckDeathZone();

            if (cameraFollow != null)
            {
                float maxHeight = Mathf.Max(_playerMaxY, _aiMaxY);
                platformSpawner?.UpdateSpawner(cameraFollow.TopY, maxHeight);
                enemySpawner?.UpdateSpawner(cameraFollow.TopY, maxHeight);
            }

            float challengeTarget = _trainer != null
                ? _trainer.GetProfile().challengeTargetScore : 0f;

            uiManager?.UpdateScoreDisplay(_playerScore, _aiScore, challengeTarget);

            // Combo display
            int combo = (player != null && player.gameObject.activeSelf) ? player.Combo : 0;
            uiManager?.UpdateCombo(combo);
        }

        // ── Game flow ─────────────────────────────────────────────────────────────
        public void StartGame(GameModeManager.GameMode mode)
        {
            _modeManager?.SetMode(mode);

            _playerScore    = 0f;
            _aiScore        = 0f;
            _playerMaxY     = 0f;
            _aiMaxY         = 0f;
            _coinsCollected = 0;
            _gemsCollected  = 0;
            _bossKills      = 0;
            _enemiesDefeated = 0;
            _batsKilled     = 0;
            _wormholesUsed  = 0;
            _nearMisses     = 0;

            int levelSeed = System.Environment.TickCount;

            if (platformSpawner != null)
            {
                platformSpawner.InitLevel(levelSeed);
                if (_trainer != null && _trainer.IsProfileReady)
                    platformSpawner.ApplySkillProfile(_trainer.GetProfile());
            }

            Vector3 spawnPos = playerSpawnPoint != null
                ? playerSpawnPoint.position : Vector3.up;

            if (cameraFollow != null)
                cameraFollow.ResetTo(spawnPos.y);

            bool isWatchAI = mode == GameModeManager.GameMode.WatchAI;
            bool isVsAI    = mode == GameModeManager.GameMode.VsAI;
            bool needsAI   = isVsAI || isWatchAI;

            // ── Player setup ──────────────────────────────────────────────────────
            if (player != null)
            {
                if (isWatchAI)
                {
                    // Spectator mode: freeze player at spawn, hide it
                    player.Revive(spawnPos);
                    player.SetInputEnabled(false);
                    player.gameObject.SetActive(false);
                }
                else
                {
                    player.gameObject.SetActive(true);
                    player.Revive(spawnPos);
                }
            }

            // ── AI setup ──────────────────────────────────────────────────────────
            if (aiPlayer != null)
            {
                aiPlayer.gameObject.SetActive(needsAI);

                if (needsAI && _trainer != null)
                {
                    bool useGhost = _recorder != null && _recorder.HasBestRun;
                    aiPlayer.Initialise(_trainer.GetProfile(), useGhost);

                    Vector3 aiSpawn = aiPlayerSpawnPoint != null
                        ? aiPlayerSpawnPoint.position
                        : spawnPos + Vector3.right * 0.6f;

                    aiPlayer.Revive(aiSpawn);
                }
            }

            // ── Camera follow mode ────────────────────────────────────────────────
            if (cameraFollow != null)
            {
                cameraFollow.aiPlayerTransform = needsAI && aiPlayer != null
                    ? aiPlayer.transform : null;

                if (isWatchAI)
                    cameraFollow.SetFollowMode(CameraFollow.FollowMode.AIOnly);
                else if (isVsAI)
                    cameraFollow.SetFollowMode(CameraFollow.FollowMode.Both);
                else
                    cameraFollow.SetFollowMode(CameraFollow.FollowMode.PlayerOnly);
            }

            // ── Recording (not needed in Watch AI — we're observing, not training) ─
            if (!isWatchAI)
                _recorder?.StartRecording();

            _gameRunning = true;

            float challengeTarget = _trainer != null
                ? _trainer.GetProfile().challengeTargetScore : 0f;

            uiManager?.ShowHUD(isVsAI, isWatchAI, challengeTarget);

            Debug.Log($"[GameManager] Started — Mode:{mode} Seed:{levelSeed}");
        }

        public void RestartGame()
        {
            StartGame(_modeManager != null
                ? _modeManager.CurrentMode
                : GameModeManager.GameMode.NormalPlay);
        }

        // ── Score tracking ────────────────────────────────────────────────────────
        private void UpdateScores()
        {
            float spawnY = playerSpawnPoint != null ? playerSpawnPoint.position.y : 0f;

            if (player != null && player.IsAlive && player.gameObject.activeSelf)
            {
                float h = player.CurrentHeight;
                if (h > _playerMaxY)
                {
                    _playerMaxY  = h;
                    _playerScore = Mathf.Max(0f, h - spawnY);
                }
            }

            if (aiPlayer != null && aiPlayer.IsAlive)
            {
                float h = aiPlayer.CurrentHeight;
                if (h > _aiMaxY)
                {
                    _aiMaxY  = h;
                    _aiScore = Mathf.Max(0f, h - spawnY);
                }
            }
        }

        // ── Death zone ────────────────────────────────────────────────────────────
        private void CheckDeathZone()
        {
            if (cameraFollow == null) return;
            float deathLine = cameraFollow.BottomY - deathYOffset;

            bool isWatchAI = _modeManager != null && _modeManager.IsWatchAI;

            // In Watch AI mode the player is disabled — skip its death check
            if (!isWatchAI && player != null && player.IsAlive
                && player.transform.position.y < deathLine)
                player.Die();

            if (aiPlayer != null && aiPlayer.IsAlive
                && aiPlayer.transform.position.y < deathLine)
                aiPlayer.Die();
        }

        // ── Death callbacks ───────────────────────────────────────────────────────
        private void OnPlayerDied()
        {
            if (!_gameRunning) return;
            _gameRunning = false;

            bool vsAI  = _modeManager != null && _modeManager.IsVsAI;
            bool aiWon = vsAI && aiPlayer != null && aiPlayer.IsAlive;

            cameraFollow?.ShakeCamera(0.55f, 0.45f);
            _recorder?.StopRecording(_playerScore);

            if (_trainer != null)
                _trainer.TrainFromLatestRun(_playerScore, aiWon);

            if (_trainer != null && _trainer.IsProfileReady && platformSpawner != null)
                platformSpawner.ApplySkillProfile(_trainer.GetProfile());

            string winner = aiWon ? "AI Clone" : "You";

            float challengeTarget = _trainer != null
                ? _trainer.GetProfile().challengeTargetScore : 0f;

            // Save daily best
            bool newDailyBest = DailyBestTracker.TrySaveDailyBest((int)_playerScore);
            int  dailyBest    = DailyBestTracker.GetDailyBest();

            uiManager?.ShowGameOver(
                playerScore:    _playerScore,
                aiScore:        vsAI ? _aiScore : -1f,
                winner:         winner,
                aiHasTrained:   _trainer != null && _trainer.IsProfileReady,
                challengeTarget: challengeTarget,
                isWatchAI:      false,
                coinsCollected: _coinsCollected,
                gemsCollected:  _gemsCollected,
                bossKills:      _bossKills,
                dailyBest:      dailyBest,
                newDailyBest:   newDailyBest);

            Debug.Log($"[GameManager] Player died. Score:{_playerScore:0} " +
                      $"AI:{_aiScore:0} Winner:{winner}");
        }

        private void OnAIDied()
        {
            bool isWatchAI = _modeManager != null && _modeManager.IsWatchAI;

            if (isWatchAI && _gameRunning)
            {
                // Watch AI mode — AI falling ends the run
                _gameRunning = false;

                uiManager?.ShowGameOver(
                    playerScore:    _aiScore,   // AI's score is the headline number
                    aiScore:        -1f,
                    winner:         "",
                    aiHasTrained:   false,
                    challengeTarget: 0f,
                    isWatchAI:      true);

                Debug.Log($"[GameManager] AI died in Watch mode. AI Score:{_aiScore:0}");
                return;
            }

            // vs-AI mode — AI dying doesn't end the run; player keeps climbing
            Debug.Log($"[GameManager] AI died in vs-AI mode at score {_aiScore:0}.");
        }

        // ── Bridge: PlayerController → AIRecorder ─────────────────────────────────
        public void NotifyPlayerJumped(
            float x, float y, float vx, float jumpDelay, string platformType)
        {
            if (_recorder == null || platformSpawner == null) return;
            var nextPlatforms = platformSpawner.GetPlatformsAbove(y, 3);
            _recorder.RecordJump(x, y, vx, jumpDelay, platformType, nextPlatforms);
        }

        public void NotifyPlayerLanded(float playerX, float platformCentreX)
        {
            _recorder?.RecordLanding(playerX, platformCentreX);
        }

        // ── Stat notification callbacks ───────────────────────────────────────────
        public void AddScore(int pts)          { _playerScore += pts; }
        public void NotifyCoinCollected()     { _coinsCollected++; }
        public void NotifyGemCollected(int pts){ _gemsCollected++; _playerScore += pts; }
        public void NotifyBossKilled(int pts) { _bossKills++; _enemiesDefeated++; _playerScore += pts; }
        public void NotifyEnemyDefeated()     { _enemiesDefeated++; }
        public void NotifyBatKilled()         { _batsKilled++; _enemiesDefeated++; }
        public void NotifyWormholeUsed()      { _wormholesUsed++; }
        public void NotifyNearMiss()
        {
            _nearMisses++;
            _recorder?.RecordNearMiss();
        }

        // ── Public getters ────────────────────────────────────────────────────────
        public float PlayerScore    => _playerScore;
        public float AIScore        => _aiScore;
        public bool  IsRunning      => _gameRunning;
        public int   CoinsCollected => _coinsCollected;
        public int   GemsCollected  => _gemsCollected;
        public int   BossKills      => _bossKills;
        public int   BatsKilled     => _batsKilled;
        public int   WormholesUsed  => _wormholesUsed;
        public int   NearMisses     => _nearMisses;
    }
}
