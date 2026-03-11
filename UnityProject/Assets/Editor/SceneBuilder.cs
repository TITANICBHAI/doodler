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
    /// HOW TO USE:
    ///   1. Open Unity and create a new 2D project.
    ///   2. Install TextMeshPro (Window → Package Manager → TextMeshPro → Install).
    ///      When prompted, click "Import TMP Essentials".
    ///   3. Copy ALL scripts from this project into your Assets folder.
    ///   4. Wait for Unity to compile (bottom status bar turns green).
    ///   5. In the top menu bar click:
    ///          DoodleClimb → Build Scene
    ///   6. Press Play to test.
    ///   7. File → Build Settings → Android → Build And Run to get your APK.
    ///
    /// The builder creates every GameObject, component, prefab, and reference
    /// automatically.  You do not need to drag anything in the Inspector.
    /// </summary>
    public static class SceneBuilder
    {
        // ── colours ────────────────────────────────────────────────────────────────
        private static readonly Color ColBg         = new Color(0.96f, 0.94f, 0.88f); // cream
        private static readonly Color ColPlayer      = new Color(0.18f, 0.80f, 0.44f); // green
        private static readonly Color ColAI          = new Color(0.95f, 0.38f, 0.27f); // red-orange
        private static readonly Color ColPlatform    = new Color(0.22f, 0.76f, 0.38f); // green
        private static readonly Color ColButtonNorm  = new Color(0.22f, 0.76f, 0.38f);
        private static readonly Color ColButtonVsAI  = new Color(0.95f, 0.38f, 0.27f);
        private static readonly Color ColText        = new Color(0.12f, 0.12f, 0.12f);
        private static readonly Color ColTextLight   = new Color(1f, 1f, 1f);
        private static readonly Color ColPanel       = new Color(0.96f, 0.94f, 0.88f, 0.97f);

        // ── menu item ──────────────────────────────────────────────────────────────
        [MenuItem("DoodleClimb/Build Scene %#b")]
        public static void BuildScene()
        {
            if (!EditorUtility.DisplayDialog(
                "Build DoodleClimb Scene",
                "This will clear the current scene and build the full DoodleClimb game.\n\nContinue?",
                "Build", "Cancel"))
                return;

            // Clear scene
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                Object.DestroyImmediate(go);

            // ── Background ────────────────────────────────────────────────────────
            Camera mainCam = CreateCamera();

            // ── Platform prefab ───────────────────────────────────────────────────
            GameObject platformPrefab = CreatePlatformPrefab();

            // ── Spawn points ──────────────────────────────────────────────────────
            Transform playerSpawn = CreateEmptyTransform("PlayerSpawn", new Vector3(0f, 1f, 0f));
            Transform aiSpawn     = CreateEmptyTransform("AISpawn",     new Vector3(0.8f, 1f, 0f));

            // ── Core game objects ─────────────────────────────────────────────────
            GameObject playerGO   = CreateCharacter("Player",   ColPlayer, 0f);
            GameObject aiPlayerGO = CreateCharacter("AIPlayer", ColAI,     0.8f);
            aiPlayerGO.SetActive(false);

            // ── Systems ───────────────────────────────────────────────────────────
            GameObject gameManagerGO = CreateEmptyGO("GameManager");
            gameManagerGO.AddComponent<Game.GameModeManager>();
            var gameManager = gameManagerGO.AddComponent<Game.GameManager>();
            gameManagerGO.AddComponent<AI.AIRecorder>();
            gameManagerGO.AddComponent<AI.AITrainer>();

            // ── Platform spawner ──────────────────────────────────────────────────
            GameObject spawnerGO = CreateEmptyGO("PlatformSpawner");
            var spawner = spawnerGO.AddComponent<Platforms.PlatformSpawner>();
            spawner.platformPrefab = platformPrefab;

            // ── Camera follow ─────────────────────────────────────────────────────
            var camFollow = mainCam.gameObject.AddComponent<Game.CameraFollow>();
            camFollow.playerTransform    = playerGO.transform;
            camFollow.aiPlayerTransform  = null;

            // ── Canvas + all UI ───────────────────────────────────────────────────
            UI.UIManager uiManager = BuildCanvas(gameManagerGO);

            // ── Wire up GameManager ───────────────────────────────────────────────
            gameManager.player           = playerGO.GetComponent<Player.PlayerController>();
            gameManager.aiPlayer         = aiPlayerGO.GetComponent<AI.AIPlayerController>();
            gameManager.platformSpawner  = spawner;
            gameManager.cameraFollow     = camFollow;
            gameManager.uiManager        = uiManager;
            gameManager.playerSpawnPoint = playerSpawn;
            gameManager.aiPlayerSpawnPoint = aiSpawn;

            // ── Physics settings ──────────────────────────────────────────────────
            Physics2D.gravity = new Vector2(0f, -20f);

            // ── Background colour ─────────────────────────────────────────────────
            mainCam.backgroundColor = ColBg;

            EditorUtility.SetDirty(gameManagerGO);

            Debug.Log("[SceneBuilder] Scene built successfully! Press Play to test.");
            EditorUtility.DisplayDialog(
                "Scene Built!",
                "The DoodleClimb scene is ready.\n\n" +
                "• Press Play to test in the editor.\n" +
                "• File → Build Settings → Android → Build and Run to get your APK.",
                "Got it!");
        }

        // ── Camera ────────────────────────────────────────────────────────────────
        private static Camera CreateCamera()
        {
            GameObject camGO = new GameObject("Main Camera");
            camGO.tag = "MainCamera";
            Camera cam = camGO.AddComponent<Camera>();
            cam.orthographic     = true;
            cam.orthographicSize = 9f;
            cam.clearFlags       = CameraClearFlags.SolidColor;
            cam.backgroundColor  = ColBg;
            camGO.transform.position = new Vector3(0f, 0f, -10f);
            camGO.AddComponent<AudioListener>();
            return cam;
        }

        // ── Platform prefab ───────────────────────────────────────────────────────
        private static GameObject CreatePlatformPrefab()
        {
            GameObject go = new GameObject("Platform");

            // Sprite
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GetDefaultSprite();
            sr.color  = ColPlatform;
            go.transform.localScale = new Vector3(2.2f, 0.25f, 1f);

            // Collider
            BoxCollider2D col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(1f, 1f);

            // Rigidbody (kinematic — platforms don't fall)
            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType              = RigidbodyType2D.Kinematic;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            // Script
            go.AddComponent<Platforms.Platform>();

            // Save as prefab
            string prefabPath = "Assets/Prefabs/Platform.prefab";
            System.IO.Directory.CreateDirectory("Assets/Prefabs");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.Refresh();

            return prefab;
        }

        // ── Character (Player / AIPlayer) ─────────────────────────────────────────
        private static GameObject CreateCharacter(string name, Color colour, float spawnX)
        {
            GameObject go = new GameObject(name);
            go.transform.position = new Vector3(spawnX, 1f, 0f);

            // Visual — small doodle square
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite        = GetDefaultSprite();
            sr.color         = colour;
            go.transform.localScale = new Vector3(0.6f, 0.75f, 1f);

            // Physics
            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale           = 1f;
            rb.constraints            = RigidbodyConstraints2D.FreezeRotation;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            BoxCollider2D col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.9f, 0.95f);

            // Controller scripts
            if (name == "Player")
                go.AddComponent<Player.PlayerController>();
            else
                go.AddComponent<AI.AIPlayerController>();

            return go;
        }

        // ── Canvas + full UI ──────────────────────────────────────────────────────
        private static UI.UIManager BuildCanvas(GameObject gameManagerGO)
        {
            // EventSystem
            GameObject esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<StandaloneInputModule>();

            // Canvas
            GameObject canvasGO = new GameObject("Canvas");
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            UI.UIManager uiManager = canvasGO.AddComponent<UI.UIManager>();

            // ── Start Menu Panel ──────────────────────────────────────────────────
            GameObject startPanel = CreatePanel(canvasGO, "StartMenuPanel", ColPanel);
            CanvasGroup startCG   = startPanel.GetComponent<CanvasGroup>();

            AddTMPText(startPanel, "TitleText", "DOODLE CLIMB",
                new Vector2(0f, 500f), new Vector2(900f, 150f), 80, ColText, FontStyle.Bold);

            var bestScoreTxt = AddTMPText(startPanel, "BestScoreText", "No runs yet",
                new Vector2(0f, 360f), new Vector2(700f, 70f), 40, ColText);

            var skillTierTxt = AddTMPText(startPanel, "SkillTierText", "",
                new Vector2(0f, 290f), new Vector2(700f, 60f), 36, ColText);

            var btnPlayNormal = AddButton(startPanel, "PlayNormalBtn", "PLAY",
                new Vector2(0f, 140f), new Vector2(500f, 110f), ColButtonNorm);

            var btnPlayVsAI = AddButton(startPanel, "PlayVsAIBtn", "vs AI CLONE",
                new Vector2(0f, 0f), new Vector2(500f, 110f), ColButtonVsAI);

            var btnModeSelect = AddButton(startPanel, "ModeSelectBtn", "SELECT MODE",
                new Vector2(0f, -150f), new Vector2(500f, 80f),
                new Color(0.50f, 0.50f, 0.50f));

            // ── Mode Select Panel ─────────────────────────────────────────────────
            GameObject modePanel = CreatePanel(canvasGO, "ModeSelectPanel", ColPanel);
            CanvasGroup modeCG   = modePanel.GetComponent<CanvasGroup>();
            SetCanvasGroup(modeCG, false);

            AddTMPText(modePanel, "ModeTitleText", "CHOOSE MODE",
                new Vector2(0f, 600f), new Vector2(800f, 120f), 64, ColText, FontStyle.Bold);

            var aiStatusTxt = AddTMPText(modePanel, "AIProfileStatusText",
                "No AI data yet.\nPlay a Normal run first!",
                new Vector2(0f, 450f), new Vector2(800f, 120f), 34, ColText);

            var btnNormal  = AddButton(modePanel, "NormalModeBtn", "NORMAL PLAY",
                new Vector2(0f, 280f), new Vector2(600f, 110f), ColButtonNorm);

            var btnVsAI2   = AddButton(modePanel, "VsAIModeBtn", "vs AI CLONE",
                new Vector2(0f, 130f), new Vector2(600f, 110f), ColButtonVsAI);

            var btnBack    = AddButton(modePanel, "ModeSelectBackBtn", "BACK",
                new Vector2(0f, -50f), new Vector2(300f, 80f),
                new Color(0.55f, 0.55f, 0.55f));

            // ── HUD Panel ─────────────────────────────────────────────────────────
            GameObject hudPanel = CreatePanel(canvasGO, "HUDPanel",
                new Color(0f, 0f, 0f, 0f)); // transparent
            CanvasGroup hudCG   = hudPanel.GetComponent<CanvasGroup>();
            SetCanvasGroup(hudCG, false);

            var playerScoreTxt = AddTMPText(hudPanel, "PlayerScoreText", "0",
                new Vector2(-400f, 860f), new Vector2(400f, 80f), 56, ColText, FontStyle.Bold);
            playerScoreTxt.alignment = TextAlignmentOptions.Left;

            // AI score (hidden by default, shown in vs-AI mode)
            GameObject aiScoreContainer = CreateChildGO(hudPanel, "AIScoreContainer");
            RectTransform aiScoreRT = aiScoreContainer.AddComponent<RectTransform>();
            aiScoreRT.anchoredPosition = new Vector2(400f, 860f);
            aiScoreRT.sizeDelta        = new Vector2(400f, 80f);
            aiScoreContainer.SetActive(false);

            var aiScoreTxt = AddTMPText(aiScoreContainer, "AIScoreText", "AI: 0",
                Vector2.zero, new Vector2(400f, 80f), 56, ColAI, FontStyle.Bold);
            aiScoreTxt.alignment = TextAlignmentOptions.Right;

            // Challenge container (shown in Normal mode once profile exists)
            GameObject challengeContainer = CreateChildGO(hudPanel, "ChallengeContainer");
            RectTransform challengeRT = challengeContainer.AddComponent<RectTransform>();
            challengeRT.anchoredPosition = new Vector2(0f, 760f);
            challengeRT.sizeDelta        = new Vector2(800f, 60f);
            challengeContainer.SetActive(false);

            var challengeTargetTxt = AddTMPText(challengeContainer, "ChallengeTargetText",
                "Target: 0", new Vector2(-200f, 0f), new Vector2(380f, 55f), 38, ColText);
            var challengeProgressTxt = AddTMPText(challengeContainer, "ChallengeProgressText",
                "0 / 0", new Vector2(200f, 0f), new Vector2(380f, 55f), 38, ColText);

            // ── Game Over Panel ───────────────────────────────────────────────────
            GameObject gameOverPanel = CreatePanel(canvasGO, "GameOverPanel", ColPanel);
            CanvasGroup gameOverCG   = gameOverPanel.GetComponent<CanvasGroup>();
            SetCanvasGroup(gameOverCG, false);

            AddTMPText(gameOverPanel, "GameOverTitleText", "Game Over",
                new Vector2(0f, 680f), new Vector2(800f, 130f), 80, ColText, FontStyle.Bold);

            var playerFinalTxt = AddTMPText(gameOverPanel, "PlayerFinalScoreText",
                "Your Score: 0",
                new Vector2(0f, 530f), new Vector2(700f, 80f), 52, ColText);

            // AI final score (hidden in Normal mode)
            GameObject aiFinalContainer = CreateChildGO(gameOverPanel, "AIFinalScoreContainer");
            RectTransform aiFinalRT = aiFinalContainer.AddComponent<RectTransform>();
            aiFinalRT.anchoredPosition = new Vector2(0f, 440f);
            aiFinalRT.sizeDelta        = new Vector2(700f, 80f);
            aiFinalContainer.SetActive(false);

            var aiFinalTxt = AddTMPText(aiFinalContainer, "AIFinalScoreText", "AI Score: 0",
                Vector2.zero, new Vector2(700f, 80f), 52, ColAI);

            var winnerTxt = AddTMPText(gameOverPanel, "WinnerText", "",
                new Vector2(0f, 350f), new Vector2(700f, 90f), 60,
                new Color(0.85f, 0.30f, 0.10f), FontStyle.Bold);

            var aiLearnedTxt = AddTMPText(gameOverPanel, "AILearnedText", "",
                new Vector2(0f, 260f), new Vector2(700f, 60f), 34, ColText);

            // Challenge result
            GameObject challengeResultContainer =
                CreateChildGO(gameOverPanel, "ChallengeResultContainer");
            RectTransform crRT = challengeResultContainer.AddComponent<RectTransform>();
            crRT.anchoredPosition = new Vector2(0f, 170f);
            crRT.sizeDelta        = new Vector2(700f, 100f);
            challengeResultContainer.SetActive(false);

            var challengeResultTxt = AddTMPText(challengeResultContainer,
                "ChallengeResultText", "",
                new Vector2(0f, 30f), new Vector2(700f, 60f), 40, ColText);
            var newTargetTxt = AddTMPText(challengeResultContainer, "NewTargetText", "",
                new Vector2(0f, -30f), new Vector2(700f, 50f), 32, ColText);

            // Skill breakdown
            GameObject skillBreakdown = CreateChildGO(gameOverPanel, "SkillBreakdownContainer");
            RectTransform sbRT = skillBreakdown.AddComponent<RectTransform>();
            sbRT.anchoredPosition = new Vector2(0f, -80f);
            sbRT.sizeDelta        = new Vector2(700f, 280f);
            skillBreakdown.SetActive(false);

            AddTMPText(skillBreakdown, "SkillBreakdownTitle", "── Your AI Profile ──",
                new Vector2(0f, 120f), new Vector2(700f, 50f), 32, ColText, FontStyle.Bold);
            var skillTierResultTxt  = AddTMPText(skillBreakdown, "SkillTierResultText", "Skill: Novice",      new Vector2(0f,  70f), new Vector2(700f, 44f), 30, ColText);
            var jumpPrecisionTxt    = AddTMPText(skillBreakdown, "JumpPrecisionText",   "Jump Precision: 0%", new Vector2(0f,  26f), new Vector2(700f, 44f), 28, ColText);
            var moveSmoothnessTxt   = AddTMPText(skillBreakdown, "MoveSmoothText",      "Smoothness:      0%",new Vector2(0f, -18f), new Vector2(700f, 44f), 28, ColText);
            var landingAccTxt       = AddTMPText(skillBreakdown, "LandingAccText",      "Landing Accuracy: 0%",new Vector2(0f,-62f), new Vector2(700f, 44f), 28, ColText);
            var riskLevelTxt        = AddTMPText(skillBreakdown, "RiskLevelText",       "Risk Level:      0%", new Vector2(0f,-106f), new Vector2(700f, 44f), 28, ColText);

            var btnRestart  = AddButton(gameOverPanel, "RestartButton", "PLAY AGAIN",
                new Vector2(0f, -490f), new Vector2(500f, 110f), ColButtonNorm);

            var btnMainMenu = AddButton(gameOverPanel, "MainMenuButton", "MAIN MENU",
                new Vector2(0f, -630f), new Vector2(500f, 80f),
                new Color(0.55f, 0.55f, 0.55f));

            // ── Wire UIManager ────────────────────────────────────────────────────
            uiManager.startMenuPanel   = startCG;
            uiManager.modeSelectPanel  = modeCG;
            uiManager.hudPanel         = hudCG;
            uiManager.gameOverPanel    = gameOverCG;

            uiManager.bestScoreText    = bestScoreTxt;
            uiManager.skillTierText    = skillTierTxt;
            uiManager.playNormalButton = btnPlayNormal;
            uiManager.playVsAIButton   = btnPlayVsAI;
            uiManager.modeSelectButton = btnModeSelect;

            uiManager.normalModeButton     = btnNormal;
            uiManager.vsAIModeButton       = btnVsAI2;
            uiManager.modeSelectBackButton = btnBack;
            uiManager.aiProfileStatusText  = aiStatusTxt;

            uiManager.playerScoreText     = playerScoreTxt;
            uiManager.aiScoreText         = aiScoreTxt;
            uiManager.aiScoreContainer    = aiScoreContainer;
            uiManager.challengeContainer  = challengeContainer;
            uiManager.challengeTargetText = challengeTargetTxt;
            uiManager.challengeProgressText = challengeProgressTxt;

            uiManager.gameOverTitleText         = (TextMeshProUGUI)gameOverPanel.transform
                .Find("GameOverTitleText").GetComponent<TextMeshProUGUI>();
            uiManager.playerFinalScoreText      = playerFinalTxt;
            uiManager.aiFinalScoreText          = aiFinalTxt;
            uiManager.winnerText                = winnerTxt;
            uiManager.aiLearnedText             = aiLearnedTxt;
            uiManager.aiFinalScoreContainer     = aiFinalContainer;
            uiManager.challengeResultContainer  = challengeResultContainer;
            uiManager.challengeResultText       = challengeResultTxt;
            uiManager.newTargetText             = newTargetTxt;
            uiManager.skillBreakdownContainer   = skillBreakdown;
            uiManager.skillTierResultText       = skillTierResultTxt;
            uiManager.jumpPrecisionText         = jumpPrecisionTxt;
            uiManager.movementSmoothnessText    = moveSmoothnessTxt;
            uiManager.landingAccuracyText       = landingAccTxt;
            uiManager.riskLevelText             = riskLevelTxt;
            uiManager.restartButton             = btnRestart;
            uiManager.mainMenuButton            = btnMainMenu;

            return uiManager;
        }

        // ── UI Factory Helpers ────────────────────────────────────────────────────
        private static GameObject CreatePanel(GameObject parent, string name, Color bgColor)
        {
            GameObject go = CreateChildGO(parent, name);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin       = Vector2.zero;
            rt.anchorMax       = Vector2.one;
            rt.offsetMin       = Vector2.zero;
            rt.offsetMax       = Vector2.zero;

            Image img = go.AddComponent<Image>();
            img.color = bgColor;

            CanvasGroup cg = go.AddComponent<CanvasGroup>();
            cg.alpha          = 1f;
            cg.interactable   = true;
            cg.blocksRaycasts = true;
            return go;
        }

        private static TextMeshProUGUI AddTMPText(
            GameObject parent, string name, string text,
            Vector2 pos, Vector2 size, float fontSize,
            Color color, FontStyle style = FontStyle.Normal)
        {
            GameObject go = CreateChildGO(parent, name);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta        = size;

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.fontSize  = fontSize;
            tmp.color     = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = (TMPro.FontStyles)(int)style;
            tmp.enableWordWrapping = true;
            return tmp;
        }

        private static Button AddButton(
            GameObject parent, string name, string label,
            Vector2 pos, Vector2 size, Color bgColor)
        {
            GameObject go = CreateChildGO(parent, name);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta        = size;

            Image img  = go.AddComponent<Image>();
            img.color  = bgColor;
            img.sprite = GetRoundedSprite();

            Button btn = go.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor      = bgColor;
            cb.highlightedColor = bgColor * 1.1f;
            cb.pressedColor     = bgColor * 0.85f;
            btn.colors          = cb;

            // Label
            GameObject txtGO = CreateChildGO(go, "Text");
            RectTransform txtRT = txtGO.AddComponent<RectTransform>();
            txtRT.anchorMin = Vector2.zero;
            txtRT.anchorMax = Vector2.one;
            txtRT.offsetMin = new Vector2(10f, 0f);
            txtRT.offsetMax = new Vector2(-10f, 0f);

            TextMeshProUGUI tmp = txtGO.AddComponent<TextMeshProUGUI>();
            tmp.text      = label;
            tmp.fontSize  = Mathf.Clamp(size.y * 0.40f, 24f, 54f);
            tmp.color     = ColTextLight;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = TMPro.FontStyles.Bold;

            return btn;
        }

        // ── Low-level helpers ─────────────────────────────────────────────────────
        private static GameObject CreateEmptyGO(string name)
        {
            GameObject go = new GameObject(name);
            return go;
        }

        private static GameObject CreateChildGO(GameObject parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        private static Transform CreateEmptyTransform(string name, Vector3 pos)
        {
            GameObject go = new GameObject(name);
            go.transform.position = pos;
            return go.transform;
        }

        private static void SetCanvasGroup(CanvasGroup cg, bool visible)
        {
            cg.alpha          = visible ? 1f : 0f;
            cg.interactable   = visible;
            cg.blocksRaycasts = visible;
        }

        private static Sprite GetDefaultSprite() =>
            AssetDatabase.GetBuiltinExtraResource<Sprite>(
                "UI/Skin/UISprite.psd") ??
            AssetDatabase.GetBuiltinExtraResource<Sprite>(
                "UI/Skin/Background.psd");

        private static Sprite GetRoundedSprite() =>
            AssetDatabase.GetBuiltinExtraResource<Sprite>(
                "UI/Skin/UISprite.psd") ??
            GetDefaultSprite();
    }
}
#endif
