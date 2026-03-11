using UnityEngine;

namespace DoodleClimb.Game
{
    /// <summary>
    /// Central coordinator for the game.
    /// Manages the game loop, death detection, score tracking,
    /// and bridges all major systems (Spawner, Camera, Recorder, Trainer).
    ///
    /// Works in two modes:
    ///   NormalPlay   — single player, infinite climbing
    ///   VsAI         — player races their AI clone
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────────
        public static GameManager Instance { get; private set; }

        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("References")]
        public Player.PlayerController player;
        public AI.AIPlayerController aiPlayer;
        public Platforms.PlatformSpawner platformSpawner;
        public CameraFollow cameraFollow;
        public UI.UIManager uiManager;

        [Header("Player Spawn")]
        public Transform playerSpawnPoint;
        public Transform aiPlayerSpawnPoint;

        [Header("Death Zone")]
        [Tooltip("How far below the camera bottom the player must fall before dying.")]
        public float deathYOffset = 2f;

        // ── Internal state ────────────────────────────────────────────────────────
        private GameModeManager _modeManager;
        private AI.AIRecorder _recorder;
        private AI.AITrainer _trainer;

        private float _playerScore = 0f;
        private float _aiScore = 0f;
        private float _playerMaxY = 0f;
        private float _aiMaxY = 0f;

        private bool _gameRunning = false;
        private int _levelSeed;

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
            _recorder = FindObjectOfType<AI.AIRecorder>();
            _trainer = FindObjectOfType<AI.AITrainer>();
        }

        private void Start()
        {
            // Subscribe to player events
            if (player != null)
                player.OnDied += OnPlayerDied;

            if (aiPlayer != null)
                aiPlayer.OnDied += OnAIDied;

            // Show start menu
            uiManager?.ShowStartMenu();
        }

        private void Update()
        {
            if (!_gameRunning) return;

            UpdateScores();
            CheckDeathZone();

            // Feed the spawner with current camera top and highest player position
            float maxHeight = Mathf.Max(_playerMaxY, _aiMaxY);
            platformSpawner?.UpdateSpawner(cameraFollow.TopY, maxHeight);

            uiManager?.UpdateScoreDisplay(_playerScore, _aiScore);
        }

        // ── Game flow ─────────────────────────────────────────────────────────────
        public void StartGame(GameModeManager.GameMode mode)
        {
            _modeManager.SetMode(mode);

            _levelSeed = System.Environment.TickCount;
            _playerScore = 0f;
            _aiScore = 0f;
            _playerMaxY = 0f;
            _aiMaxY = 0f;

            // Reset spawner with shared seed so both characters face the same level
            platformSpawner?.InitLevel(_levelSeed);

            // Reset camera
            cameraFollow?.ResetTo(playerSpawnPoint.position.y);

            // Revive player
            player?.Revive(playerSpawnPoint.position);

            // Setup AI if needed
            bool vsAI = mode == GameModeManager.GameMode.VsAI;
            aiPlayer?.gameObject.SetActive(vsAI);

            if (vsAI && aiPlayer != null && _trainer != null)
            {
                bool useGhost = _recorder != null && _recorder.HasBestRun;
                aiPlayer.Initialise(_trainer.GetProfile(), useGhost);
                aiPlayer.Revive(aiPlayerSpawnPoint != null
                    ? aiPlayerSpawnPoint.position
                    : playerSpawnPoint.position + Vector3.right * 0.5f);

                // Point camera at both
                if (cameraFollow != null)
                    cameraFollow.aiPlayerTransform = aiPlayer.transform;
            }
            else if (cameraFollow != null)
            {
                cameraFollow.aiPlayerTransform = null;
            }

            // Start recording
            _recorder?.StartRecording();

            _gameRunning = true;
            uiManager?.ShowHUD(vsAI);

            Debug.Log($"[GameManager] Game started. Mode: {mode}. Seed: {_levelSeed}");
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
                    _playerMaxY = h;
                    _playerScore = Mathf.Max(0f, _playerMaxY - playerSpawnPoint.position.y);
                }
            }

            if (aiPlayer != null && aiPlayer.IsAlive)
            {
                float h = aiPlayer.CurrentHeight;
                if (h > _aiMaxY)
                {
                    _aiMaxY = h;
                    _aiScore = Mathf.Max(0f, _aiMaxY);
                }
            }
        }

        // ── Death zone ────────────────────────────────────────────────────────────
        private void CheckDeathZone()
        {
            float deathLine = cameraFollow.BottomY - deathYOffset;

            if (player != null && player.IsAlive &&
                player.transform.position.y < deathLine)
            {
                player.Die();
            }

            if (aiPlayer != null && aiPlayer.IsAlive &&
                aiPlayer.transform.position.y < deathLine)
            {
                aiPlayer.Die();
            }
        }

        // ── Death callbacks ───────────────────────────────────────────────────────
        private void OnPlayerDied()
        {
            _gameRunning = false;
            _recorder?.StopRecording(_playerScore);
            _trainer?.TrainFromLatestRun(_playerScore);

            bool vsAI = _modeManager.CurrentMode == GameModeManager.GameMode.VsAI;
            string winner = vsAI && aiPlayer != null && aiPlayer.IsAlive ? "AI Clone" : "You";

            uiManager?.ShowGameOver(
                _playerScore,
                vsAI ? _aiScore : -1f,
                winner,
                _trainer?.IsProfileReady ?? false
            );

            Debug.Log($"[GameManager] Player died. Score: {_playerScore:0}. " +
                      $"AI Score: {_aiScore:0}.");
        }

        private void OnAIDied()
        {
            Debug.Log($"[GameManager] AI died. AI Score: {_aiScore:0}.");
            // AI dying alone doesn't end the game — player keeps going
        }

        // ── Getters ───────────────────────────────────────────────────────────────
        public float PlayerScore => _playerScore;
        public float AIScore => _aiScore;
        public bool IsRunning => _gameRunning;
    }
}
