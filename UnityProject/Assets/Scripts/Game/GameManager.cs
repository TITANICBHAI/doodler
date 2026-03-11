using UnityEngine;

namespace DoodleClimb.Game
{
    /// <summary>
    /// Central coordinator for the game.
    /// Manages the game loop, death detection, score tracking,
    /// and bridges all major systems (Spawner, Camera, Recorder, Trainer).
    ///
    /// Works in two modes:
    ///   NormalPlay — single player, infinite climbing
    ///   VsAI       — player races their AI clone on the same generated level
    ///
    /// Also runs the AI Challenge system:
    ///   After training, a challengeTargetScore is set (player's best × 1.05).
    ///   The HUD shows the target so the player always has something to beat.
    ///   If the AI wins a vs-AI run, the target rises further.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────────
        public static GameManager Instance { get; private set; }

        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("References")]
        public Player.PlayerController      player;
        public AI.AIPlayerController        aiPlayer;
        public Platforms.PlatformSpawner    platformSpawner;
        public CameraFollow                 cameraFollow;
        public UI.UIManager                 uiManager;

        [Header("Player Spawn")]
        public Transform playerSpawnPoint;
        public Transform aiPlayerSpawnPoint;

        [Header("Death Zone")]
        [Tooltip("How far below the camera bottom the player must fall before dying.")]
        public float deathYOffset = 2f;

        // ── Internal state ────────────────────────────────────────────────────────
        private GameModeManager   _modeManager;
        private AI.AIRecorder     _recorder;
        private AI.AITrainer      _trainer;

        private float _playerScore = 0f;
        private float _aiScore     = 0f;
        private float _playerMaxY  = 0f;
        private float _aiMaxY      = 0f;

        private bool _gameRunning = false;
        private int  _levelSeed;

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _modeManager = GetComponent<GameModeManager>();
            _recorder    = FindObjectOfType<AI.AIRecorder>();
            _trainer     = FindObjectOfType<AI.AITrainer>();
        }

        private void Start()
        {
            if (player != null)   player.OnDied   += OnPlayerDied;
            if (aiPlayer != null) aiPlayer.OnDied += OnAIDied;

            uiManager?.ShowStartMenu();
        }

        private void Update()
        {
            if (!_gameRunning) return;

            UpdateScores();
            CheckDeathZone();

            float maxHeight = Mathf.Max(_playerMaxY, _aiMaxY);
            platformSpawner?.UpdateSpawner(cameraFollow.TopY, maxHeight);

            // Pass AI challenge target to HUD every frame
            float challengeTarget = _trainer != null
                ? _trainer.GetProfile().challengeTargetScore : 0f;

            uiManager?.UpdateScoreDisplay(_playerScore, _aiScore, challengeTarget);
        }

        // ── Game flow ─────────────────────────────────────────────────────────────
        public void StartGame(GameModeManager.GameMode mode)
        {
            _modeManager.SetMode(mode);

            _levelSeed    = System.Environment.TickCount;
            _playerScore  = 0f;
            _aiScore      = 0f;
            _playerMaxY   = 0f;
            _aiMaxY       = 0f;

            // Same seed = same platforms for both player and AI clone
            platformSpawner?.InitLevel(_levelSeed);

            // Apply Platform Personality after a profile exists
            if (_trainer != null && _trainer.IsProfileReady)
                platformSpawner?.ApplySkillProfile(_trainer.GetProfile());

            cameraFollow?.ResetTo(playerSpawnPoint.position.y);
            player?.Revive(playerSpawnPoint.position);

            bool vsAI = mode == GameModeManager.GameMode.VsAI;
            aiPlayer?.gameObject.SetActive(vsAI);

            if (vsAI && aiPlayer != null && _trainer != null)
            {
                bool useGhost = _recorder != null && _recorder.HasBestRun;
                aiPlayer.Initialise(_trainer.GetProfile(), useGhost);
                aiPlayer.Revive(aiPlayerSpawnPoint != null
                    ? aiPlayerSpawnPoint.position
                    : playerSpawnPoint.position + Vector3.right * 0.5f);

                if (cameraFollow != null)
                    cameraFollow.aiPlayerTransform = aiPlayer.transform;
            }
            else if (cameraFollow != null)
            {
                cameraFollow.aiPlayerTransform = null;
            }

            _recorder?.StartRecording();
            _gameRunning = true;

            float challengeTarget = _trainer != null
                ? _trainer.GetProfile().challengeTargetScore : 0f;

            uiManager?.ShowHUD(vsAI, challengeTarget);

            Debug.Log($"[GameManager] Game started. Mode:{mode} Seed:{_levelSeed}");
        }

        public void RestartGame()
        {
            StartGame(_modeManager.CurrentMode);
        }

        // ── Score tracking ────────────────────────────────────────────────────────
        private void UpdateScores()
        {
            if (player != null && player.IsAlive)
            {
                float h = player.CurrentHeight;
                if (h > _playerMaxY)
                {
                    _playerMaxY  = h;
                    _playerScore = Mathf.Max(0f, _playerMaxY - playerSpawnPoint.position.y);
                }
            }

            if (aiPlayer != null && aiPlayer.IsAlive)
            {
                float h = aiPlayer.CurrentHeight;
                if (h > _aiMaxY)
                {
                    _aiMaxY  = h;
                    _aiScore = Mathf.Max(0f, _aiMaxY - playerSpawnPoint.position.y);
                }
            }
        }

        // ── Death zone ────────────────────────────────────────────────────────────
        private void CheckDeathZone()
        {
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
            _gameRunning = false;

            bool vsAI  = _modeManager.CurrentMode == GameModeManager.GameMode.VsAI;
            bool aiWon = vsAI && aiPlayer != null && aiPlayer.IsAlive;

            _recorder?.StopRecording(_playerScore);
            _trainer?.TrainFromLatestRun(_playerScore, aiWon);

            // Refresh Platform Personality after training
            if (_trainer != null && _trainer.IsProfileReady)
                platformSpawner?.ApplySkillProfile(_trainer.GetProfile());

            string winner = aiWon ? "AI Clone" : "You";

            float challengeTarget = _trainer != null
                ? _trainer.GetProfile().challengeTargetScore : 0f;

            uiManager?.ShowGameOver(
                _playerScore,
                vsAI ? _aiScore : -1f,
                winner,
                _trainer?.IsProfileReady ?? false,
                challengeTarget
            );

            Debug.Log($"[GameManager] Player died. " +
                      $"Score:{_playerScore:0} AI:{_aiScore:0} Winner:{winner}");
        }

        private void OnAIDied()
        {
            // AI dying alone doesn't end the run — the player keeps climbing
            Debug.Log($"[GameManager] AI died. AI Score:{_aiScore:0}");
        }

        // ── Getters ───────────────────────────────────────────────────────────────
        public float PlayerScore => _playerScore;
        public float AIScore     => _aiScore;
        public bool  IsRunning   => _gameRunning;

        /// <summary>
        /// Called by PlayerController when a jump happens so we can pass platform
        /// context to the recorder.
        /// </summary>
        public void NotifyPlayerJumped(
            float x, float y, float vx, float jumpDelay, string platformType)
        {
            if (_recorder == null || platformSpawner == null) return;

            var nextPlatforms = platformSpawner.GetPlatformsAbove(y, 3);
            _recorder.RecordJump(x, y, vx, jumpDelay, platformType, nextPlatforms);
        }

        /// <summary>Called by PlayerController on each successful landing.</summary>
        public void NotifyPlayerLanded(float playerX, float platformCentreX)
        {
            _recorder?.RecordLanding(playerX, platformCentreX);
        }
    }
}
