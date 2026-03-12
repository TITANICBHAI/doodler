using UnityEngine;

namespace DoodleClimb.Game
{
    /// <summary>
    /// Central coordinator for the game.
    /// Manages the game loop, death detection, score tracking,
    /// and bridges all major systems (Spawner, Camera, Recorder, Trainer).
    ///
    /// Modes:
    ///   NormalPlay  — single player, infinite climbing
    ///   VsAI        — player races their AI clone on the same generated level
    ///
    /// Singleton pattern prevents duplicate instances if the scene is rebuilt
    /// without clearing the old one first.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────────
        public static GameManager Instance { get; private set; }

        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("References")]
        public Player.PlayerController   player;
        public AI.AIPlayerController     aiPlayer;
        public Platforms.PlatformSpawner platformSpawner;
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

        private bool _gameRunning;

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            // Singleton guard — destroy duplicate instances
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _modeManager = GetComponent<GameModeManager>();
            _recorder    = GetComponent<AI.AIRecorder>();
            _trainer     = GetComponent<AI.AITrainer>();

            // Fallback: search scene if not on same GameObject
            if (_recorder == null) _recorder = FindObjectOfType<AI.AIRecorder>();
            if (_trainer  == null) _trainer  = FindObjectOfType<AI.AITrainer>();
        }

        private void Start()
        {
            // Subscribe to death events once — never in StartGame (avoids duplicate listeners)
            if (player   != null) player.OnDied   += OnPlayerDied;
            if (aiPlayer != null) aiPlayer.OnDied += OnAIDied;

            uiManager?.ShowStartMenu();
        }

        private void Update()
        {
            if (!_gameRunning) return;

            UpdateScores();
            CheckDeathZone();

            if (platformSpawner != null && cameraFollow != null)
            {
                float maxHeight = Mathf.Max(_playerMaxY, _aiMaxY);
                platformSpawner.UpdateSpawner(cameraFollow.TopY, maxHeight);
            }

            float challengeTarget = _trainer != null
                ? _trainer.GetProfile().challengeTargetScore : 0f;

            uiManager?.UpdateScoreDisplay(_playerScore, _aiScore, challengeTarget);
        }

        // ── Game flow ─────────────────────────────────────────────────────────────
        public void StartGame(GameModeManager.GameMode mode)
        {
            if (_modeManager != null)
                _modeManager.SetMode(mode);

            // Reset all per-run state
            _playerScore = 0f;
            _aiScore     = 0f;
            _playerMaxY  = 0f;
            _aiMaxY      = 0f;

            int levelSeed = System.Environment.TickCount;

            if (platformSpawner != null)
            {
                platformSpawner.InitLevel(levelSeed);

                if (_trainer != null && _trainer.IsProfileReady)
                    platformSpawner.ApplySkillProfile(_trainer.GetProfile());
            }

            if (cameraFollow != null && playerSpawnPoint != null)
                cameraFollow.ResetTo(playerSpawnPoint.position.y);

            if (player != null && playerSpawnPoint != null)
                player.Revive(playerSpawnPoint.position);

            bool vsAI = mode == GameModeManager.GameMode.VsAI;

            if (aiPlayer != null)
            {
                aiPlayer.gameObject.SetActive(vsAI);

                if (vsAI && _trainer != null)
                {
                    bool useGhost = _recorder != null && _recorder.HasBestRun;
                    aiPlayer.Initialise(_trainer.GetProfile(), useGhost);

                    Vector3 aiSpawnPos = aiPlayerSpawnPoint != null
                        ? aiPlayerSpawnPoint.position
                        : (playerSpawnPoint != null
                            ? playerSpawnPoint.position + Vector3.right * 0.6f
                            : Vector3.up);

                    aiPlayer.Revive(aiSpawnPos);
                }
            }

            if (cameraFollow != null)
                cameraFollow.aiPlayerTransform = vsAI && aiPlayer != null
                    ? aiPlayer.transform : null;

            _recorder?.StartRecording();
            _gameRunning = true;

            float challengeTarget = _trainer != null
                ? _trainer.GetProfile().challengeTargetScore : 0f;

            uiManager?.ShowHUD(vsAI, challengeTarget);

            Debug.Log($"[GameManager] Game started — Mode:{mode} Seed:{levelSeed}");
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
            if (playerSpawnPoint == null) return;
            float spawnY = playerSpawnPoint.position.y;

            if (player != null && player.IsAlive)
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

            if (player != null && player.IsAlive &&
                player.transform.position.y < deathLine)
                player.Die();

            if (aiPlayer != null && aiPlayer.IsAlive &&
                aiPlayer.transform.position.y < deathLine)
                aiPlayer.Die();
        }

        // ── Death callbacks ───────────────────────────────────────────────────────
        private void OnPlayerDied()
        {
            if (!_gameRunning) return; // guard against double-fire
            _gameRunning = false;

            bool vsAI  = _modeManager != null &&
                         _modeManager.CurrentMode == GameModeManager.GameMode.VsAI;
            bool aiWon = vsAI && aiPlayer != null && aiPlayer.IsAlive;

            // Stop recording FIRST so the last jump outcome is resolved as a Fall
            _recorder?.StopRecording(_playerScore);

            // Train the profile from this run
            if (_trainer != null)
                _trainer.TrainFromLatestRun(_playerScore, aiWon);

            // Refresh Platform Personality for next run
            if (_trainer != null && _trainer.IsProfileReady && platformSpawner != null)
                platformSpawner.ApplySkillProfile(_trainer.GetProfile());

            string winner = aiWon ? "AI Clone" : "You";

            float challengeTarget = _trainer != null
                ? _trainer.GetProfile().challengeTargetScore : 0f;

            uiManager?.ShowGameOver(
                _playerScore,
                vsAI ? _aiScore : -1f,
                winner,
                _trainer != null && _trainer.IsProfileReady,
                challengeTarget
            );

            Debug.Log($"[GameManager] Player died. " +
                      $"Score:{_playerScore:0}  AI:{_aiScore:0}  Winner:{winner}");
        }

        private void OnAIDied()
        {
            // AI dying on its own does not end the run — player keeps climbing
            Debug.Log($"[GameManager] AI died at score {_aiScore:0}.");
        }

        // ── Bridge: PlayerController → AIRecorder ─────────────────────────────────

        /// <summary>
        /// Called by PlayerController on each jump so the AIRecorder can capture
        /// both player state and the current platform layout together.
        /// </summary>
        public void NotifyPlayerJumped(
            float x, float y, float vx, float jumpDelay, string platformType)
        {
            if (_recorder == null || platformSpawner == null) return;

            var nextPlatforms = platformSpawner.GetPlatformsAbove(y, 3);
            _recorder.RecordJump(x, y, vx, jumpDelay, platformType, nextPlatforms);
        }

        /// <summary>
        /// Called by PlayerController on each successful landing so the recorder
        /// can measure landing accuracy retroactively.
        /// </summary>
        public void NotifyPlayerLanded(float playerX, float platformCentreX)
        {
            _recorder?.RecordLanding(playerX, platformCentreX);
        }

        // ── Public getters ────────────────────────────────────────────────────────
        public float PlayerScore => _playerScore;
        public float AIScore     => _aiScore;
        public bool  IsRunning   => _gameRunning;
    }
}
