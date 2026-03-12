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
    /// HOW TO USE — 5 steps only:
    ///   1. Create a new 2D project in Unity (2022 LTS or later).
    ///   2. Window → Package Manager → TextMeshPro → Install.
    ///      When prompted, click "Import TMP Essentials".
    ///   3. Drag the entire Assets/ folder from this project into your Unity project.
    ///   4. Wait for the green tick (compilation done).
    ///   5. Top menu bar: DoodleClimb → Build Scene.
    ///      Press Play to test.  File → Build Settings → Android → Build and Run for APK.
    ///
    /// Zero manual Inspector wiring required.
    ///
    /// FIXES applied versus earlier version:
    ///   - FindObjectsOfType (compatible with Unity 2022+) instead of FindObjectsByType
    ///   - RectTransform created with new GameObject(name, typeof(RectTransform))
    ///     instead of AddComponent<RectTransform>() which throws an error on fresh GOs
    ///   - Font style parameter changed to TMPro.FontStyles (correct type)
    ///   - gameOverTitleText captured directly from AddTMPText return value
    /// </summary>
    public static class SceneBuilder
    {
        // ── Colours ───────────────────────────────────────────────────────────────
        private static readonly Color ColBg        = new Color(0.96f, 0.94f, 0.88f);
        private static readonly Color ColPlayer    = new Color(0.18f, 0.80f, 0.44f);
        private static readonly Color ColAI        = new Color(0.95f, 0.38f, 0.27f);
        private static readonly Color ColPlatform  = new Color(0.22f, 0.76f, 0.38f);
        private static readonly Color ColBtnNorm   = new Color(0.22f, 0.76f, 0.38f);
        private static readonly Color ColBtnVsAI   = new Color(0.95f, 0.38f, 0.27f);
        private static readonly Color ColBtnGrey   = new Color(0.52f, 0.52f, 0.52f);
        private static readonly Color ColText      = new Color(0.12f, 0.12f, 0.12f);
        private static readonly Color ColTextLight = new Color(1f, 1f, 1f);
        private static readonly Color ColPanel     = new Color(0.96f, 0.94f, 0.88f, 0.97f);

        // ── Menu item ─────────────────────────────────────────────────────────────
        [MenuItem("DoodleClimb/Build Scene %#b")]
        public static void BuildScene()
        {
            if (!EditorUtility.DisplayDialog(
                "Build DoodleClimb Scene",
                "This will clear the current scene and build the full DoodleClimb game.\n\nContinue?",
                "Build", "Cancel"))
                return;

            // Destroy every existing root GameObject
            GameObject[] all = Object.FindObjectsOfType<GameObject>();
            foreach (GameObject go in all)
                if (go != null) Object.DestroyImmediate(go);

            // ── Camera ────────────────────────────────────────────────────────────
            Camera mainCam = CreateCamera();

            // ── Platform prefab ───────────────────────────────────────────────────
            GameObject platformPrefab = CreatePlatformPrefab();

            // ── Spawn points ──────────────────────────────────────────────────────
            Transform playerSpawn = CreateMarker("PlayerSpawn", new Vector3(0f,    1f, 0f));
            Transform aiSpawn     = CreateMarker("AISpawn",     new Vector3(0.8f,  1f, 0f));

            // ── Characters ───────────────────────────────────────────────────────
            GameObject playerGO   = CreateCharacter("Player",   ColPlayer, 0f);
            GameObject aiPlayerGO = CreateCharacter("AIPlayer", ColAI,     0.8f);
            aiPlayerGO.SetActive(false); // only active in vs-AI mode

            // ── Systems (all on one GameObject for simplicity) ────────────────────
            GameObject sysGO = new GameObject("GameManager");
            sysGO.AddComponent<Game.GameModeManager>();
            Game.GameManager   gameManager = sysGO.AddComponent<Game.GameManager>();
            AI.AIRecorder      recorder    = sysGO.AddComponent<AI.AIRecorder>();
            AI.AITrainer       trainer     = sysGO.AddComponent<AI.AITrainer>();

            // ── Platform spawner ──────────────────────────────────────────────────
            GameObject spawnerGO = new GameObject("PlatformSpawner");
            Platforms.PlatformSpawner spawner =
                spawnerGO.AddComponent<Platforms.PlatformSpawner>();
            spawner.platformPrefab = platformPrefab;

            // ── Camera follow ─────────────────────────────────────────────────────
            Game.CameraFollow camFollow = mainCam.gameObject.AddComponent<Game.CameraFollow>();
            camFollow.playerTransform   = playerGO.transform;
            camFollow.aiPlayerTransform = null;

            // ── UI (Canvas + all panels) ──────────────────────────────────────────
            UI.UIManager uiManager = BuildCanvas();

            // ── Wire up GameManager references ────────────────────────────────────
            gameManager.player           = playerGO.GetComponent<Player.PlayerController>();
            gameManager.aiPlayer         = aiPlayerGO.GetComponent<AI.AIPlayerController>();
            gameManager.platformSpawner  = spawner;
            gameManager.cameraFollow     = camFollow;
            gameManager.uiManager        = uiManager;
            gameManager.playerSpawnPoint = playerSpawn;
            gameManager.aiPlayerSpawnPoint = aiSpawn;

            // ── Physics ───────────────────────────────────────────────────────────
            Physics2D.gravity = new Vector2(0f, -20f);

            EditorUtility.SetDirty(sysGO);
            AssetDatabase.SaveAssets();

            Debug.Log("[SceneBuilder] Scene built. Press Play to test.");
            EditorUtility.DisplayDialog(
                "Scene Built!",
                "DoodleClimb is ready.\n\n" +
                "▶  Press Play to test in the editor.\n" +
                "▶  File → Build Settings → Android → Build and Run for APK.",
                "Got it!");
        }

        // ── Camera ────────────────────────────────────────────────────────────────
        private static Camera CreateCamera()
        {
            GameObject go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            Camera cam = go.AddComponent<Camera>();
            cam.orthographic     = true;
            cam.orthographicSize = 9f;
            cam.clearFlags       = CameraClearFlags.SolidColor;
            cam.backgroundColor  = ColBg;
            go.transform.position = new Vector3(0f, 0f, -10f);
            go.AddComponent<AudioListener>();
            return cam;
        }

        // ── Platform prefab ───────────────────────────────────────────────────────
        private static GameObject CreatePlatformPrefab()
        {
            GameObject go = new GameObject("Platform");

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = BuiltinSprite();
            sr.color  = ColPlatform;
            go.transform.localScale = new Vector3(2.2f, 0.25f, 1f);

            BoxCollider2D col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(1f, 1f);

            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType               = RigidbodyType2D.Kinematic;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            go.AddComponent<Platforms.Platform>();

            string dir = "Assets/Prefabs";
            System.IO.Directory.CreateDirectory(dir);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, dir + "/Platform.prefab");
            Object.DestroyImmediate(go);
            AssetDatabase.Refresh();
            return prefab;
        }

        // ── Character (Player / AI) ───────────────────────────────────────────────
        private static GameObject CreateCharacter(string name, Color colour, float spawnX)
        {
            GameObject go = new GameObject(name);
            go.transform.position = new Vector3(spawnX, 1f, 0f);

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = BuiltinSprite();
            sr.color  = colour;
            go.transform.localScale = new Vector3(0.6f, 0.75f, 1f);

            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale           = 1f;
            rb.constraints            = RigidbodyConstraints2D.FreezeRotation;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            BoxCollider2D col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.9f, 0.95f);

            if (name == "Player")
                go.AddComponent<Player.PlayerController>();
            else
                go.AddComponent<AI.AIPlayerController>();

            return go;
        }

        // ── Canvas + all UI panels ────────────────────────────────────────────────
        private static UI.UIManager BuildCanvas()
        {
            // EventSystem
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();

            // Canvas root
            GameObject canvasGO = new GameObject("Canvas");
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            UI.UIManager ui = canvasGO.AddComponent<UI.UIManager>();

            // ── START MENU ────────────────────────────────────────────────────────
            GameObject startPanel = MakePanel(canvasGO, "StartMenuPanel", ColPanel);
            CanvasGroup startCG   = startPanel.GetComponent<CanvasGroup>();

            MakeTMP(startPanel, "TitleText", "DOODLE CLIMB",
                Vec(0, 500), Vec(900, 150), 80, ColText, TMPro.FontStyles.Bold);

            var bestScoreTxt = MakeTMP(startPanel, "BestScoreText", "No runs yet",
                Vec(0, 360), Vec(700, 70), 40, ColText);

            var skillTierTxt = MakeTMP(startPanel, "SkillTierText", "",
                Vec(0, 290), Vec(700, 60), 36, ColText);

            var btnPlayNormal  = MakeButton(startPanel, "PlayNormalBtn",  "PLAY",
                Vec(0, 140), Vec(500, 110), ColBtnNorm);

            var btnPlayVsAI    = MakeButton(startPanel, "PlayVsAIBtn",   "vs AI CLONE",
                Vec(0, 0),   Vec(500, 110), ColBtnVsAI);

            var btnModeSelect  = MakeButton(startPanel, "ModeSelectBtn", "SELECT MODE",
                Vec(0, -150), Vec(500, 80), ColBtnGrey);

            // ── MODE SELECT ───────────────────────────────────────────────────────
            GameObject modePanel = MakePanel(canvasGO, "ModeSelectPanel", ColPanel);
            CanvasGroup modeCG   = modePanel.GetComponent<CanvasGroup>();
            HideGroup(modeCG);

            MakeTMP(modePanel, "ModeTitleText", "CHOOSE MODE",
                Vec(0, 600), Vec(800, 120), 64, ColText, TMPro.FontStyles.Bold);

            var aiStatusTxt = MakeTMP(modePanel, "AIProfileStatusText",
                "No AI data yet.\nPlay a Normal run first!",
                Vec(0, 450), Vec(800, 120), 34, ColText);

            var btnNormal    = MakeButton(modePanel, "NormalModeBtn", "NORMAL PLAY",
                Vec(0, 280), Vec(600, 110), ColBtnNorm);

            var btnVsAI2     = MakeButton(modePanel, "VsAIModeBtn", "vs AI CLONE",
                Vec(0, 130), Vec(600, 110), ColBtnVsAI);

            var btnBack      = MakeButton(modePanel, "ModeSelectBackBtn", "BACK",
                Vec(0, -50), Vec(300, 80), ColBtnGrey);

            // ── HUD ───────────────────────────────────────────────────────────────
            GameObject hudPanel = MakePanel(canvasGO, "HUDPanel",
                new Color(0f, 0f, 0f, 0f)); // fully transparent background
            CanvasGroup hudCG   = hudPanel.GetComponent<CanvasGroup>();
            HideGroup(hudCG);

            var playerScoreTxt = MakeTMP(hudPanel, "PlayerScoreText", "0",
                Vec(-380, 860), Vec(400, 80), 56, ColText, TMPro.FontStyles.Bold);
            playerScoreTxt.alignment = TextAlignmentOptions.Left;

            // AI score container — shown only in vs-AI mode
            GameObject aiScoreContainer = MakeRTChild(hudPanel, "AIScoreContainer",
                Vec(380, 860), Vec(400, 80));
            aiScoreContainer.SetActive(false);

            var aiScoreTxt = MakeTMP(aiScoreContainer, "AIScoreText", "AI: 0",
                Vec(0, 0), Vec(400, 80), 56, ColAI, TMPro.FontStyles.Bold);
            aiScoreTxt.alignment = TextAlignmentOptions.Right;

            // Challenge container — shown in Normal mode once profile exists
            GameObject challengeContainer = MakeRTChild(hudPanel, "ChallengeContainer",
                Vec(0, 760), Vec(800, 60));
            challengeContainer.SetActive(false);

            var challengeTargetTxt = MakeTMP(challengeContainer, "ChallengeTargetText",
                "Target: 0", Vec(-200, 0), Vec(380, 55), 38, ColText);

            var challengeProgressTxt = MakeTMP(challengeContainer, "ChallengeProgressText",
                "0 / 0", Vec(200, 0), Vec(380, 55), 38, ColText);

            // ── GAME OVER ─────────────────────────────────────────────────────────
            GameObject gameOverPanel = MakePanel(canvasGO, "GameOverPanel", ColPanel);
            CanvasGroup gameOverCG   = gameOverPanel.GetComponent<CanvasGroup>();
            HideGroup(gameOverCG);

            // Capture return value directly — no Find() required
            var gameOverTitleTxt = MakeTMP(gameOverPanel, "GameOverTitleText", "Game Over",
                Vec(0, 680), Vec(800, 130), 80, ColText, TMPro.FontStyles.Bold);

            var playerFinalTxt = MakeTMP(gameOverPanel, "PlayerFinalScoreText",
                "Your Score: 0", Vec(0, 530), Vec(700, 80), 52, ColText);

            // AI final score container
            GameObject aiFinalContainer = MakeRTChild(gameOverPanel, "AIFinalScoreContainer",
                Vec(0, 440), Vec(700, 80));
            aiFinalContainer.SetActive(false);

            var aiFinalTxt = MakeTMP(aiFinalContainer, "AIFinalScoreText", "AI Score: 0",
                Vec(0, 0), Vec(700, 80), 52, ColAI);

            var winnerTxt = MakeTMP(gameOverPanel, "WinnerText", "",
                Vec(0, 350), Vec(700, 90), 60,
                new Color(0.85f, 0.30f, 0.10f), TMPro.FontStyles.Bold);

            var aiLearnedTxt = MakeTMP(gameOverPanel, "AILearnedText", "",
                Vec(0, 260), Vec(700, 60), 34, ColText);

            // Challenge result container
            GameObject challengeResultContainer =
                MakeRTChild(gameOverPanel, "ChallengeResultContainer",
                    Vec(0, 170), Vec(700, 100));
            challengeResultContainer.SetActive(false);

            var challengeResultTxt = MakeTMP(challengeResultContainer,
                "ChallengeResultText", "", Vec(0, 30), Vec(700, 60), 40, ColText);

            var newTargetTxt = MakeTMP(challengeResultContainer, "NewTargetText", "",
                Vec(0, -30), Vec(700, 50), 32, ColText);

            // Skill breakdown container
            GameObject skillBreakdown = MakeRTChild(gameOverPanel,
                "SkillBreakdownContainer", Vec(0, -80), Vec(700, 280));
            skillBreakdown.SetActive(false);

            MakeTMP(skillBreakdown, "SkillBreakdownTitle", "── Your AI Profile ──",
                Vec(0, 120), Vec(700, 50), 32, ColText, TMPro.FontStyles.Bold);

            var skillTierResultTxt  = MakeTMP(skillBreakdown, "SkillTierResultText",
                "Skill: Novice",       Vec(0,  70), Vec(700, 44), 30, ColText);
            var jumpPrecisionTxt    = MakeTMP(skillBreakdown, "JumpPrecisionText",
                "Jump Precision:  0%", Vec(0,  26), Vec(700, 44), 28, ColText);
            var moveSmoothnessTxt   = MakeTMP(skillBreakdown, "MoveSmoothText",
                "Smoothness:      0%", Vec(0, -18), Vec(700, 44), 28, ColText);
            var landingAccTxt       = MakeTMP(skillBreakdown, "LandingAccText",
                "Landing Accuracy:0%", Vec(0, -62), Vec(700, 44), 28, ColText);
            var riskLevelTxt        = MakeTMP(skillBreakdown, "RiskLevelText",
                "Risk Level:      0%", Vec(0,-106), Vec(700, 44), 28, ColText);

            var btnRestart  = MakeButton(gameOverPanel, "RestartButton", "PLAY AGAIN",
                Vec(0, -490), Vec(500, 110), ColBtnNorm);

            var btnMainMenu = MakeButton(gameOverPanel, "MainMenuButton", "MAIN MENU",
                Vec(0, -630), Vec(500, 80), ColBtnGrey);

            // ── Wire UIManager ────────────────────────────────────────────────────
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
            ui.modeSelectBackButton = btnBack;
            ui.aiProfileStatusText  = aiStatusTxt;

            ui.playerScoreText      = playerScoreTxt;
            ui.aiScoreText          = aiScoreTxt;
            ui.aiScoreContainer     = aiScoreContainer;
            ui.challengeContainer   = challengeContainer;
            ui.challengeTargetText  = challengeTargetTxt;
            ui.challengeProgressText = challengeProgressTxt;

            ui.gameOverTitleText        = gameOverTitleTxt;  // captured directly — no Find()
            ui.playerFinalScoreText     = playerFinalTxt;
            ui.aiFinalScoreText         = aiFinalTxt;
            ui.winnerText               = winnerTxt;
            ui.aiLearnedText            = aiLearnedTxt;
            ui.aiFinalScoreContainer    = aiFinalContainer;
            ui.challengeResultContainer = challengeResultContainer;
            ui.challengeResultText      = challengeResultTxt;
            ui.newTargetText            = newTargetTxt;
            ui.skillBreakdownContainer  = skillBreakdown;
            ui.skillTierResultText      = skillTierResultTxt;
            ui.jumpPrecisionText        = jumpPrecisionTxt;
            ui.movementSmoothnessText   = moveSmoothnessTxt;
            ui.landingAccuracyText      = landingAccTxt;
            ui.riskLevelText            = riskLevelTxt;
            ui.restartButton            = btnRestart;
            ui.mainMenuButton           = btnMainMenu;

            return ui;
        }

        // ── UI Factory Helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Creates a full-screen panel with an Image background and CanvasGroup.
        /// Uses new GameObject(name, typeof(RectTransform)) so the RectTransform
        /// is set from the start — AddComponent&lt;RectTransform&gt;() is invalid on a
        /// GameObject that already has a regular Transform.
        /// </summary>
        private static GameObject MakePanel(GameObject parent, string name, Color bg)
        {
            // Create with RectTransform from the start (replaces plain Transform)
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);

            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image img = go.AddComponent<Image>();
            img.color = bg;

            CanvasGroup cg = go.AddComponent<CanvasGroup>();
            cg.alpha          = 1f;
            cg.interactable   = true;
            cg.blocksRaycasts = true;
            return go;
        }

        /// <summary>
        /// Creates a child RectTransform container (no Image) at a given position/size.
        /// Used for sub-containers like aiScoreContainer, challengeContainer, etc.
        /// </summary>
        private static GameObject MakeRTChild(
            GameObject parent, string name, Vector2 pos, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);

            RectTransform rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta        = size;
            return go;
        }

        /// <summary>Creates a TextMeshProUGUI label. Parameter is TMPro.FontStyles.</summary>
        private static TextMeshProUGUI MakeTMP(
            GameObject parent, string name, string text,
            Vector2 pos, Vector2 size, float fontSize, Color color,
            TMPro.FontStyles style = TMPro.FontStyles.Normal)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);

            RectTransform rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta        = size;

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text               = text;
            tmp.fontSize           = fontSize;
            tmp.color              = color;
            tmp.alignment          = TextAlignmentOptions.Center;
            tmp.fontStyle          = style;
            tmp.enableWordWrapping = true;
            return tmp;
        }

        /// <summary>Creates a styled button with a centred label.</summary>
        private static Button MakeButton(
            GameObject parent, string name, string label,
            Vector2 pos, Vector2 size, Color bgColor)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);

            RectTransform rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta        = size;

            Image img = go.AddComponent<Image>();
            img.color  = bgColor;
            img.sprite = BuiltinSprite();

            Button btn = go.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor      = bgColor;
            cb.highlightedColor = bgColor * 1.15f;
            cb.pressedColor     = bgColor * 0.80f;
            cb.selectedColor    = bgColor;
            btn.colors          = cb;

            // Label child
            GameObject lblGO = new GameObject("Label", typeof(RectTransform));
            lblGO.transform.SetParent(go.transform, false);

            RectTransform lblRT = (RectTransform)lblGO.transform;
            lblRT.anchorMin = Vector2.zero;
            lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = new Vector2(10f, 0f);
            lblRT.offsetMax = new Vector2(-10f, 0f);

            TextMeshProUGUI tmp = lblGO.AddComponent<TextMeshProUGUI>();
            tmp.text      = label;
            tmp.fontSize  = Mathf.Clamp(size.y * 0.38f, 22f, 52f);
            tmp.color     = ColTextLight;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = TMPro.FontStyles.Bold;
            return btn;
        }

        // ── Low-level helpers ─────────────────────────────────────────────────────
        private static Transform CreateMarker(string name, Vector3 pos)
        {
            GameObject go = new GameObject(name);
            go.transform.position = pos;
            return go.transform;
        }

        private static void HideGroup(CanvasGroup cg)
        {
            cg.alpha          = 0f;
            cg.interactable   = false;
            cg.blocksRaycasts = false;
        }

        private static Vector2 Vec(float x, float y) => new Vector2(x, y);

        private static Sprite BuiltinSprite() =>
            AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
    }
}
#endif
