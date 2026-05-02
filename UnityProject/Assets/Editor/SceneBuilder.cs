#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

namespace DoodleClimb.Editor
{
    /// <summary>
    /// One-click scene builder for DoodleClimb.
    ///
    /// HOW TO USE — 5 steps:
    ///   1. New Unity 2022 LTS 2D project.
    ///   2. Window → Package Manager → TextMeshPro → Install,
    ///      then click "Import TMP Essentials" when prompted.
    ///   3. Drag the entire Assets/ folder into your project.
    ///   4. Wait for the green compile tick (bottom-right).
    ///   5. Menu: DoodleClimb → Build Scene.  Press Play to test.
    ///
    /// What is built automatically:
    ///   • Notebook-paper parallax background (BackgroundScroller)
    ///   • Player character with eyes, trail renderer, squash/stretch
    ///   • AI clone with eyes, antenna, squash/stretch
    ///   • Spring / Moving / Breakable / Temporary platforms (all pooled)
    ///   • Particle effects (land dust, spring burst, death burst, combo flash)
    ///   • Full UI: Start Menu, Mode Select, HUD (score + combo), Game Over
    ///   • Camera follow with dynamic zoom and screen shake
    ///
    /// Zero manual Inspector wiring required.
    /// </summary>
    public static class SceneBuilder
    {
        // ── Colour palette ────────────────────────────────────────────────────────
        private static readonly Color ColPaper    = new Color(0.97f, 0.96f, 0.91f, 1.00f);
        private static readonly Color ColPlayer   = new Color(0.18f, 0.80f, 0.44f, 1.00f); // green
        private static readonly Color ColAI       = new Color(0.90f, 0.32f, 0.18f, 1.00f); // red-orange
        private static readonly Color ColPlatform = new Color(0.20f, 0.78f, 0.40f, 1.00f); // match static
        private static readonly Color ColBtnNorm  = new Color(0.18f, 0.76f, 0.40f, 1.00f);
        private static readonly Color ColBtnVsAI  = new Color(0.90f, 0.32f, 0.18f, 1.00f);
        private static readonly Color ColBtnWatch = new Color(0.22f, 0.52f, 0.94f, 1.00f);
        private static readonly Color ColBtnGrey  = new Color(0.50f, 0.50f, 0.50f, 1.00f);
        private static readonly Color ColText     = new Color(0.12f, 0.12f, 0.15f, 1.00f);
        private static readonly Color ColWhite    = new Color(1.00f, 1.00f, 1.00f, 1.00f);
        private static readonly Color ColPanel    = new Color(0.97f, 0.96f, 0.91f, 0.97f);

        // ── Menu item ─────────────────────────────────────────────────────────────
        [MenuItem("DoodleClimb/Build Scene %#b")]
        public static void BuildScene()
        {
            if (!EditorUtility.DisplayDialog(
                "Build DoodleClimb Scene",
                "This will clear the current scene and build the full DoodleClimb game.\n\nContinue?",
                "Build", "Cancel"))
                return;

            // Clear scene
            foreach (var go in Object.FindObjectsOfType<GameObject>())
                if (go != null) Object.DestroyImmediate(go);

            // ── Camera ────────────────────────────────────────────────────────────
            Camera mainCam = CreateCamera();

            // ── Platform prefab ───────────────────────────────────────────────────
            GameObject platformPrefab = CreatePlatformPrefab();

            // ── Background ────────────────────────────────────────────────────────
            GameObject bgGO = new GameObject("BackgroundScroller");
            bgGO.AddComponent<Game.BackgroundScroller>();

            // ── Spawn points ──────────────────────────────────────────────────────
            Transform playerSpawn = CreateMarker("PlayerSpawn", new Vector3( 0.0f, 1f, 0f));
            Transform aiSpawn     = CreateMarker("AISpawn",     new Vector3( 0.8f, 1f, 0f));

            // ── Characters ────────────────────────────────────────────────────────
            GameObject playerGO   = CreateCharacter("Player",   ColPlayer, 0.0f, isAI: false);
            GameObject aiPlayerGO = CreateCharacter("AIPlayer", ColAI,     0.8f, isAI: true);
            aiPlayerGO.SetActive(false);

            // ── Systems (all on one GameManager GO) ───────────────────────────────
            GameObject sysGO = new GameObject("GameManager");
            sysGO.AddComponent<Game.GameModeManager>();
            Game.GameManager  gameManager = sysGO.AddComponent<Game.GameManager>();
            sysGO.AddComponent<AI.AIRecorder>();
            sysGO.AddComponent<AI.AITrainer>();
            sysGO.AddComponent<Game.VisualEffects>(); // ← particle systems

            // ── Platform spawner ──────────────────────────────────────────────────
            GameObject spawnerGO = new GameObject("PlatformSpawner");
            Platforms.PlatformSpawner spawner =
                spawnerGO.AddComponent<Platforms.PlatformSpawner>();
            spawner.platformPrefab = platformPrefab;

            // ── Camera follow ─────────────────────────────────────────────────────
            Game.CameraFollow camFollow =
                mainCam.gameObject.AddComponent<Game.CameraFollow>();
            camFollow.playerTransform     = playerGO.transform;
            camFollow.aiPlayerTransform   = null;
            camFollow.enableDynamicZoom   = true;
            camFollow.minOrthographicSize = 9f;
            camFollow.maxOrthographicSize = 16f;

            // ── UI ────────────────────────────────────────────────────────────────
            UI.UIManager uiManager = BuildCanvas();

            // ── Wire GameManager ──────────────────────────────────────────────────
            gameManager.player             = playerGO.GetComponent<Player.PlayerController>();
            gameManager.aiPlayer           = aiPlayerGO.GetComponent<AI.AIPlayerController>();
            gameManager.platformSpawner    = spawner;
            gameManager.cameraFollow       = camFollow;
            gameManager.uiManager          = uiManager;
            gameManager.playerSpawnPoint   = playerSpawn;
            gameManager.aiPlayerSpawnPoint = aiSpawn;

            // ── Physics ───────────────────────────────────────────────────────────
            Physics2D.gravity = new Vector2(0f, -20f);

            EditorUtility.SetDirty(sysGO);
            AssetDatabase.SaveAssets();

            Debug.Log("[SceneBuilder] Done — press Play to test.");
            EditorUtility.DisplayDialog(
                "Scene Built!",
                "DoodleClimb is ready.\n\n" +
                "▶  Press Play to test.\n" +
                "▶  File → Build Settings → Android → Build and Run for the APK.",
                "Got it!");
        }

        // ── Camera ────────────────────────────────────────────────────────────────
        private static Camera CreateCamera()
        {
            var go  = new GameObject("Main Camera");
            go.tag  = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.orthographic     = true;
            cam.orthographicSize = 9f;
            cam.clearFlags       = CameraClearFlags.SolidColor;
            cam.backgroundColor  = ColPaper;
            go.transform.position = new Vector3(0f, 0f, -10f);
            go.AddComponent<AudioListener>();
            return cam;
        }

        // ── Platform prefab ───────────────────────────────────────────────────────
        private static GameObject CreatePlatformPrefab()
        {
            var go = new GameObject("Platform");
            go.transform.localScale = new Vector3(2.2f, 0.25f, 1f);

            var sr    = go.AddComponent<SpriteRenderer>();
            sr.sprite = BuiltinSprite();
            sr.color  = ColPlatform;

            var col   = go.AddComponent<BoxCollider2D>();
            col.size  = new Vector2(1f, 1f);

            var rb          = go.AddComponent<Rigidbody2D>();
            rb.bodyType     = RigidbodyType2D.Kinematic;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            go.AddComponent<Platforms.Platform>();

            string dir = "Assets/Prefabs";
            System.IO.Directory.CreateDirectory(dir);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, dir + "/Platform.prefab");
            Object.DestroyImmediate(go);
            AssetDatabase.Refresh();
            return prefab;
        }

        // ── Character ─────────────────────────────────────────────────────────────
        /// <summary>
        /// Creates a character with separate root (physics) and Visual child (rendering).
        /// The Visual child is what PlayerController/AIPlayerController squash/stretches.
        ///
        /// Player: green body, human eyes, trail renderer
        /// AI    : red-orange body, glowing cyan robot eyes, antenna
        /// </summary>
        private static GameObject CreateCharacter(
            string name, Color bodyColor, float spawnX, bool isAI)
        {
            // Root — physics only (no SpriteRenderer)
            var root = new GameObject(name);
            root.transform.position = new Vector3(spawnX, 1f, 0f);

            var rb = root.AddComponent<Rigidbody2D>();
            rb.gravityScale           = 1f;
            rb.constraints            = RigidbodyConstraints2D.FreezeRotation;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            var col  = root.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.60f, 0.70f); // tight — matches visual footprint

            // ── Visual child ───────────────────────────────────────────────────────
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            // This is the squash/stretch target; normalScale is read in Awake()
            visual.transform.localScale = new Vector3(0.60f, 0.75f, 1f);

            // Body sprite
            var bodySR     = visual.AddComponent<SpriteRenderer>();
            bodySR.sprite  = BuiltinSprite();
            bodySR.color   = bodyColor;
            bodySR.sortingOrder = 2;

            // Eyes
            AddEye(visual, new Vector3(-0.24f, 0.15f, 0f), isAI, isLeft: true);
            AddEye(visual, new Vector3( 0.24f, 0.15f, 0f), isAI, isLeft: false);

            if (isAI)
                AddAntenna(visual, bodyColor);
            else
                AddTrailRenderer(visual, bodyColor);

            // Controller script on root
            if (!isAI) root.AddComponent<Player.PlayerController>();
            else        root.AddComponent<AI.AIPlayerController>();

            return root;
        }

        private static void AddEye(
            GameObject parent, Vector3 localPos, bool isRobot, bool isLeft)
        {
            // ── White / iris ──────────────────────────────────────────────────────
            var eyeGO = new GameObject(isLeft ? "EyeL" : "EyeR");
            eyeGO.transform.SetParent(parent.transform, false);
            eyeGO.transform.localPosition = localPos;
            eyeGO.transform.localScale    = isRobot
                ? new Vector3(0.24f, 0.18f, 1f)  // rectangular robot eye
                : new Vector3(0.22f, 0.22f, 1f);  // round human eye

            var eyeSR     = eyeGO.AddComponent<SpriteRenderer>();
            eyeSR.sprite  = BuiltinSprite();
            eyeSR.color   = Color.white;
            eyeSR.sortingOrder = 3;

            // ── Pupil ─────────────────────────────────────────────────────────────
            var pupilGO = new GameObject("Pupil");
            pupilGO.transform.SetParent(eyeGO.transform, false);
            // Offset slightly toward centre and down for "looking down" expression
            pupilGO.transform.localPosition = new Vector3(isLeft ? 0.06f : -0.06f, -0.08f, 0f);
            pupilGO.transform.localScale    = new Vector3(0.44f, 0.44f, 1f);

            var pupilSR    = pupilGO.AddComponent<SpriteRenderer>();
            pupilSR.sprite = BuiltinSprite();
            // Robot: glowing cyan pupil; Human: dark pupil
            pupilSR.color  = isRobot
                ? new Color(0.10f, 0.90f, 1.00f) // cyan
                : new Color(0.10f, 0.10f, 0.18f); // near-black
            pupilSR.sortingOrder = 4;

            // Robot gets an extra inner highlight
            if (isRobot)
            {
                var glowGO = new GameObject("Glow");
                glowGO.transform.SetParent(pupilGO.transform, false);
                glowGO.transform.localPosition = new Vector3(-0.2f, 0.2f, 0f);
                glowGO.transform.localScale    = new Vector3(0.35f, 0.35f, 1f);
                var glowSR    = glowGO.AddComponent<SpriteRenderer>();
                glowSR.sprite = BuiltinSprite();
                glowSR.color  = new Color(1f, 1f, 1f, 0.9f);
                glowSR.sortingOrder = 5;
            }
        }

        private static void AddAntenna(GameObject parent, Color bodyColor)
        {
            // Antenna stem
            var stem = new GameObject("Antenna");
            stem.transform.SetParent(parent.transform, false);
            stem.transform.localPosition = new Vector3(0f, 0.60f, 0f);
            stem.transform.localScale    = new Vector3(0.07f, 0.35f, 1f);

            var stemSR    = stem.AddComponent<SpriteRenderer>();
            stemSR.sprite = BuiltinSprite();
            stemSR.color  = Color.Lerp(bodyColor, Color.white, 0.45f);
            stemSR.sortingOrder = 3;

            // Antenna tip (yellow ball)
            var tip = new GameObject("AntennaTip");
            tip.transform.SetParent(stem.transform, false);
            tip.transform.localPosition = new Vector3(0f, 0.60f, 0f);
            tip.transform.localScale    = new Vector3(2.20f, 0.22f, 1f);

            var tipSR    = tip.AddComponent<SpriteRenderer>();
            tipSR.sprite = BuiltinSprite();
            tipSR.color  = new Color(1.0f, 0.88f, 0.10f); // bright yellow
            tipSR.sortingOrder = 4;
        }

        private static void AddTrailRenderer(GameObject visual, Color bodyColor)
        {
            var trail = visual.AddComponent<TrailRenderer>();
            trail.time        = 0.13f;
            trail.startWidth  = 0.55f;
            trail.endWidth    = 0f;
            trail.numCapVertices      = 2;
            trail.numCornerVertices   = 2;
            trail.generateLightingData = false;
            trail.shadowCastingMode   = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.sortingOrder        = 1;

            Material mat       = new Material(Shader.Find("Sprites/Default"));
            trail.material     = mat;
            trail.startColor   = new Color(bodyColor.r, bodyColor.g, bodyColor.b, 0.50f);
            trail.endColor     = new Color(bodyColor.r, bodyColor.g, bodyColor.b, 0.00f);
        }

        // ── Canvas ────────────────────────────────────────────────────────────────
        private static UI.UIManager BuildCanvas()
        {
            // EventSystem
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();

            // Canvas root
            var canvasGO = new GameObject("Canvas");
            var canvas   = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler         = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            UI.UIManager ui = canvasGO.AddComponent<UI.UIManager>();

            // ──────────────────────────────────────────────────────────────────────
            // START MENU
            // ──────────────────────────────────────────────────────────────────────
            var startPanel = MakePanel(canvasGO, "StartMenuPanel", ColPanel);
            var startCG    = startPanel.GetComponent<CanvasGroup>();

            MakeTMP(startPanel, "TitleText", "DOODLE CLIMB",
                V(0, 550), V(900, 170), 88, ColText, TMPro.FontStyles.Bold);

            var bestScoreTxt = MakeTMP(startPanel, "BestScoreText", "No runs yet",
                V(0, 390), V(700, 70), 42, ColText);

            var skillTierTxt = MakeTMP(startPanel, "SkillTierText", "",
                V(0, 320), V(700, 60), 36, ColText);

            var btnPlayNormal = MakeButton(startPanel, "PlayNormalBtn", "PLAY",
                V(0, 150), V(520, 120), ColBtnNorm);

            var btnPlayVsAI = MakeButton(startPanel, "PlayVsAIBtn", "vs AI CLONE",
                V(0,   0), V(520, 120), ColBtnVsAI);

            var btnModeSelect = MakeButton(startPanel, "ModeSelectBtn", "MORE MODES",
                V(0, -155), V(520, 85), ColBtnGrey);

            // ──────────────────────────────────────────────────────────────────────
            // MODE SELECT
            // ──────────────────────────────────────────────────────────────────────
            var modePanel = MakePanel(canvasGO, "ModeSelectPanel", ColPanel);
            var modeCG    = modePanel.GetComponent<CanvasGroup>();
            HideGroup(modeCG);

            MakeTMP(modePanel, "ModeTitleText", "CHOOSE MODE",
                V(0, 700), V(800, 130), 68, ColText, TMPro.FontStyles.Bold);

            var aiStatusTxt = MakeTMP(modePanel, "AIProfileStatusText",
                "No AI data yet.\nPlay a Normal run first!",
                V(0, 570), V(800, 130), 34, ColText);

            var btnNormal = MakeButton(modePanel, "NormalModeBtn", "NORMAL PLAY",
                V(0, 390), V(620, 120), ColBtnNorm);

            var btnVsAI2 = MakeButton(modePanel, "VsAIModeBtn", "vs AI CLONE",
                V(0, 240), V(620, 120), ColBtnVsAI);

            var btnWatchAI = MakeButton(modePanel, "WatchAIBtn", "WATCH AI PLAY",
                V(0,  90), V(620, 120), ColBtnWatch);

            var btnBack = MakeButton(modePanel, "ModeSelectBackBtn", "BACK",
                V(0, -80), V(320, 85), ColBtnGrey);

            // ──────────────────────────────────────────────────────────────────────
            // HUD
            // ──────────────────────────────────────────────────────────────────────
            var hudPanel = MakePanel(canvasGO, "HUDPanel", new Color(0f, 0f, 0f, 0f));
            var hudCG    = hudPanel.GetComponent<CanvasGroup>();
            HideGroup(hudCG);

            // Player score — top-left
            var playerScoreTxt = MakeTMP(hudPanel, "PlayerScoreText", "0",
                V(-360, 860), V(400, 90), 64, ColText, TMPro.FontStyles.Bold);
            playerScoreTxt.alignment = TextAlignmentOptions.Left;

            // AI score container — top-right (vs-AI + Watch-AI)
            var aiScoreContainer = MakeRTChild(hudPanel, "AIScoreContainer",
                V(360, 860), V(420, 90));
            aiScoreContainer.SetActive(false);

            var aiScoreTxt = MakeTMP(aiScoreContainer, "AIScoreText", "AI: 0",
                V(0, 0), V(420, 90), 60, ColAI, TMPro.FontStyles.Bold);
            aiScoreTxt.alignment = TextAlignmentOptions.Right;

            // WATCHING AI banner — top centre
            var watchingAILbl = MakeTMP(hudPanel, "WatchingAILabel", "WATCHING AI",
                V(0, 860), V(700, 90), 52,
                new Color(0.22f, 0.52f, 0.94f), TMPro.FontStyles.Bold);
            watchingAILbl.gameObject.SetActive(false);

            // Combo display — centre of screen, punchy
            var comboTxt = MakeTMP(hudPanel, "ComboText", "x3!",
                V(0, 450), V(780, 120), 72,
                new Color(0.5f, 1f, 0.8f), TMPro.FontStyles.Bold);
            comboTxt.gameObject.SetActive(false);

            // Challenge bar (Normal mode)
            var challengeContainer = MakeRTChild(hudPanel, "ChallengeContainer",
                V(0, 770), V(800, 60));
            challengeContainer.SetActive(false);

            var challengeTargetTxt = MakeTMP(challengeContainer, "ChallengeTargetText",
                "Target: 0", V(-200, 0), V(380, 56), 38, ColText);

            var challengeProgressTxt = MakeTMP(challengeContainer, "ChallengeProgressText",
                "0 / 0", V(200, 0), V(380, 56), 38, ColText);

            // ──────────────────────────────────────────────────────────────────────
            // GAME OVER
            // ──────────────────────────────────────────────────────────────────────
            var gameOverPanel = MakePanel(canvasGO, "GameOverPanel", ColPanel);
            var gameOverCG    = gameOverPanel.GetComponent<CanvasGroup>();
            HideGroup(gameOverCG);

            var gameOverTitleTxt = MakeTMP(gameOverPanel, "GameOverTitleText",
                "Game Over",
                V(0, 700), V(800, 140), 84, ColText, TMPro.FontStyles.Bold);

            var playerFinalTxt = MakeTMP(gameOverPanel, "PlayerFinalScoreText",
                "Your Score: 0",
                V(0, 550), V(700, 85), 56, ColText);

            var aiFinalContainer = MakeRTChild(gameOverPanel,
                "AIFinalScoreContainer", V(0, 450), V(700, 80));
            aiFinalContainer.SetActive(false);

            var aiFinalTxt = MakeTMP(aiFinalContainer, "AIFinalScoreText",
                "AI Score: 0", V(0, 0), V(700, 80), 52, ColAI);

            var winnerTxt = MakeTMP(gameOverPanel, "WinnerText", "",
                V(0, 360), V(700, 95), 64,
                new Color(0.85f, 0.28f, 0.08f), TMPro.FontStyles.Bold);

            var aiLearnedTxt = MakeTMP(gameOverPanel, "AILearnedText", "",
                V(0, 270), V(700, 60), 34, ColText);

            var challengeResultContainer =
                MakeRTChild(gameOverPanel, "ChallengeResultContainer",
                    V(0, 185), V(700, 105));
            challengeResultContainer.SetActive(false);

            var challengeResultTxt = MakeTMP(challengeResultContainer,
                "ChallengeResultText", "", V(0, 30), V(700, 60), 42, ColText);

            var newTargetTxt = MakeTMP(challengeResultContainer, "NewTargetText",
                "", V(0, -32), V(700, 50), 32, ColText);

            var skillBreakdown = MakeRTChild(gameOverPanel,
                "SkillBreakdownContainer", V(0, -65), V(700, 290));
            skillBreakdown.SetActive(false);

            MakeTMP(skillBreakdown, "SkillBreakdownTitle", "── Your AI Profile ──",
                V(0, 125), V(700, 50), 32, ColText, TMPro.FontStyles.Bold);

            var skillTierResult  = MakeTMP(skillBreakdown, "SkillTierResultText",
                "Skill: Novice",       V(0,  75), V(700, 44), 30, ColText);
            var jumpPrecTxt      = MakeTMP(skillBreakdown, "JumpPrecisionText",
                "Jump Precision:  0%", V(0,  30), V(700, 44), 28, ColText);
            var moveSmoothTxt    = MakeTMP(skillBreakdown, "MoveSmoothText",
                "Smoothness:      0%", V(0, -14), V(700, 44), 28, ColText);
            var landingAccTxt    = MakeTMP(skillBreakdown, "LandingAccText",
                "Landing Accuracy:0%", V(0, -58), V(700, 44), 28, ColText);
            var riskTxt          = MakeTMP(skillBreakdown, "RiskLevelText",
                "Risk Level:      0%", V(0,-102), V(700, 44), 28, ColText);

            var btnRestart  = MakeButton(gameOverPanel, "RestartButton",
                "PLAY AGAIN", V(0, -490), V(520, 120), ColBtnNorm);

            var btnMainMenu = MakeButton(gameOverPanel, "MainMenuButton",
                "MAIN MENU",  V(0, -640), V(520, 85), ColBtnGrey);

            // ──────────────────────────────────────────────────────────────────────
            // WIRE UIManager
            // ──────────────────────────────────────────────────────────────────────
            ui.startMenuPanel  = startCG;
            ui.modeSelectPanel = modeCG;
            ui.hudPanel        = hudCG;
            ui.gameOverPanel   = gameOverCG;

            ui.bestScoreText    = bestScoreTxt;
            ui.skillTierText    = skillTierTxt;
            ui.playNormalButton = btnPlayNormal;
            ui.playVsAIButton   = btnPlayVsAI;
            ui.modeSelectButton = btnModeSelect;

            ui.normalModeButton     = btnNormal;
            ui.vsAIModeButton       = btnVsAI2;
            ui.watchAIButton        = btnWatchAI;
            ui.modeSelectBackButton = btnBack;
            ui.aiProfileStatusText  = aiStatusTxt;

            ui.playerScoreText       = playerScoreTxt;
            ui.aiScoreText           = aiScoreTxt;
            ui.aiScoreContainer      = aiScoreContainer;
            ui.watchingAILabel       = watchingAILbl;
            ui.comboText             = comboTxt;        // ← NEW
            ui.challengeContainer    = challengeContainer;
            ui.challengeTargetText   = challengeTargetTxt;
            ui.challengeProgressText = challengeProgressTxt;

            ui.gameOverTitleText        = gameOverTitleTxt;
            ui.playerFinalScoreText     = playerFinalTxt;
            ui.aiFinalScoreText         = aiFinalTxt;
            ui.winnerText               = winnerTxt;
            ui.aiLearnedText            = aiLearnedTxt;
            ui.aiFinalScoreContainer    = aiFinalContainer;
            ui.challengeResultContainer = challengeResultContainer;
            ui.challengeResultText      = challengeResultTxt;
            ui.newTargetText            = newTargetTxt;
            ui.skillBreakdownContainer  = skillBreakdown;
            ui.skillTierResultText      = skillTierResult;
            ui.jumpPrecisionText        = jumpPrecTxt;
            ui.movementSmoothnessText   = moveSmoothTxt;
            ui.landingAccuracyText      = landingAccTxt;
            ui.riskLevelText            = riskTxt;
            ui.restartButton            = btnRestart;
            ui.mainMenuButton           = btnMainMenu;

            return ui;
        }

        // ── UI factory helpers ────────────────────────────────────────────────────
        private static GameObject MakePanel(GameObject parent, string name, Color bg)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);

            var rt       = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img   = go.AddComponent<Image>();
            img.color = bg;

            var cg             = go.AddComponent<CanvasGroup>();
            cg.alpha           = 1f;
            cg.interactable    = true;
            cg.blocksRaycasts  = true;
            return go;
        }

        private static GameObject MakeRTChild(
            GameObject parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var rt              = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta        = size;
            return go;
        }

        private static TextMeshProUGUI MakeTMP(
            GameObject parent, string name, string text,
            Vector2 pos, Vector2 size, float fontSize, Color color,
            TMPro.FontStyles style = TMPro.FontStyles.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);

            var rt              = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta        = size;

            var tmp                = go.AddComponent<TextMeshProUGUI>();
            tmp.text               = text;
            tmp.fontSize           = fontSize;
            tmp.color              = color;
            tmp.alignment          = TextAlignmentOptions.Center;
            tmp.fontStyle          = style;
            tmp.enableWordWrapping = true;
            return tmp;
        }

        private static Button MakeButton(
            GameObject parent, string name, string label,
            Vector2 pos, Vector2 size, Color bgColor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);

            var rt              = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta        = size;

            var img   = go.AddComponent<Image>();
            img.color  = bgColor;
            img.sprite = BuiltinSprite();

            var btn = go.AddComponent<Button>();
            var cb  = btn.colors;
            cb.normalColor      = bgColor;
            cb.highlightedColor = bgColor * 1.15f;
            cb.pressedColor     = bgColor * 0.80f;
            cb.selectedColor    = bgColor;
            btn.colors          = cb;

            // Label
            var lblGO = new GameObject("Label", typeof(RectTransform));
            lblGO.transform.SetParent(go.transform, false);
            var lblRT       = (RectTransform)lblGO.transform;
            lblRT.anchorMin = Vector2.zero;
            lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = new Vector2(12f, 0f);
            lblRT.offsetMax = new Vector2(-12f, 0f);

            var tmp       = lblGO.AddComponent<TextMeshProUGUI>();
            tmp.text      = label;
            tmp.fontSize  = Mathf.Clamp(size.y * 0.38f, 22f, 56f);
            tmp.color     = ColWhite;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = TMPro.FontStyles.Bold;
            return btn;
        }

        // ── Low-level helpers ─────────────────────────────────────────────────────
        private static Transform CreateMarker(string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            return go.transform;
        }

        private static void HideGroup(CanvasGroup cg)
        {
            cg.alpha          = 0f;
            cg.interactable   = false;
            cg.blocksRaycasts = false;
        }

        private static Vector2 V(float x, float y) => new Vector2(x, y);

        private static Sprite BuiltinSprite() =>
            AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
    }
}
#endif
