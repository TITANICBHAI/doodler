using UnityEngine;

namespace DoodleClimb.Game
{
    /// <summary>
    /// Generates an infinite, procedural "notebook paper" background that follows
    /// the camera upward as the player climbs.
    ///
    /// Layers (back → front):
    ///   • Sky gradient quad        (sorting order –30)
    ///   • Horizontal ruled lines   (sorting order –20, parallax 1× = locked to cam)
    ///   • Red margin line          (sorting order –20, fixed X = –5 world units)
    ///   • Floating doodle dots     (sorting order –19, parallax 0.6× = mild depth)
    ///
    /// All sprites are created from code — no texture assets needed.
    /// </summary>
    public class BackgroundScroller : MonoBehaviour
    {
        [Header("Notebook Lines")]
        public int   lineCount   = 26;
        public float lineSpacing = 1.4f;
        public Color lineColor   = new Color(0.68f, 0.83f, 0.96f, 0.32f);

        [Header("Margin Line")]
        public float marginX     = -5.2f;
        public Color marginColor = new Color(0.92f, 0.55f, 0.55f, 0.22f);

        [Header("Background Dots (parallax depth)")]
        public int   dotCount    = 55;
        public float dotSpread   = 8f;   // half-width from centre
        public float dotBandH    = 40f;  // total vertical band (dots wrap in this)

        // ── Internal ──────────────────────────────────────────────────────────────
        private Transform[] _lines;
        private Transform[] _dots;
        private float[]     _dotOffsetY;  // offset from camera band origin (per dot)
        private float[]     _dotX;        // fixed world X per dot
        private float[]     _dotParallax; // per-dot 0..1 parallax factor

        private Camera  _cam;
        private Sprite  _whiteSprite;

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            _cam         = Camera.main;
            _whiteSprite = CreateWhiteSprite();

            CreateSkyBackdrop();
            CreateLines();
            CreateMarginLine();
            CreateDots();
        }

        private void LateUpdate()
        {
            if (_cam == null) return;
            float camY  = _cam.transform.position.y;

            // ── Snap horizontal lines to always fill screen ───────────────────────
            float baseY = Mathf.Floor(camY / lineSpacing) * lineSpacing;
            for (int i = 0; i < _lines.Length; i++)
            {
                int   k = i - _lines.Length / 2;
                float y = baseY + k * lineSpacing;
                _lines[i].position = new Vector3(0f, y, 1f);
            }

            // ── Parallax dots ─────────────────────────────────────────────────────
            for (int i = 0; i < _dots.Length; i++)
            {
                // Dot Y = camera_scroll * parallax + fixed offset, wrapped in dotBandH
                float rawY = camY * _dotParallax[i] + _dotOffsetY[i];
                float wrappedY = camY
                    + Mathf.Repeat(rawY - camY + dotBandH * 0.5f, dotBandH)
                    - dotBandH * 0.5f;
                _dots[i].position = new Vector3(_dotX[i], wrappedY, 1f);
            }
        }

        // ── Construction helpers ──────────────────────────────────────────────────
        private void CreateSkyBackdrop()
        {
            // Large quad behind everything, very light cream colour — acts as paper
            var go   = new GameObject("BG_Sky");
            go.transform.SetParent(transform, false);
            go.transform.localScale = new Vector3(200f, 2000f, 1f);
            go.transform.position   = new Vector3(0f, 0f, 5f);

            var sr        = go.AddComponent<SpriteRenderer>();
            sr.sprite     = _whiteSprite;
            sr.color      = new Color(0.97f, 0.96f, 0.91f, 1f); // warm cream
            sr.sortingOrder = -30;
        }

        private void CreateLines()
        {
            _lines = new Transform[lineCount];
            for (int i = 0; i < lineCount; i++)
            {
                var go = new GameObject($"BG_Line{i}");
                go.transform.SetParent(transform, false);

                var sr        = go.AddComponent<SpriteRenderer>();
                sr.sprite     = _whiteSprite;
                sr.color      = lineColor;
                sr.sortingOrder = -20;

                go.transform.localScale = new Vector3(120f, 0.032f, 1f);
                _lines[i] = go.transform;
            }
        }

        private void CreateMarginLine()
        {
            var go   = new GameObject("BG_Margin");
            go.transform.SetParent(transform, false);
            go.transform.position   = new Vector3(marginX, 0f, 1f);
            go.transform.localScale = new Vector3(0.025f, 2000f, 1f);

            var sr        = go.AddComponent<SpriteRenderer>();
            sr.sprite     = _whiteSprite;
            sr.color      = marginColor;
            sr.sortingOrder = -20;
        }

        private void CreateDots()
        {
            _dots       = new Transform[dotCount];
            _dotOffsetY = new float[dotCount];
            _dotX       = new float[dotCount];
            _dotParallax = new float[dotCount];

            var rng = new System.Random(77);

            for (int i = 0; i < dotCount; i++)
            {
                float x        = (float)(rng.NextDouble() * dotSpread * 2 - dotSpread);
                float offY     = (float)(rng.NextDouble() * dotBandH - dotBandH * 0.5f);
                float parallax = (float)(rng.NextDouble() * 0.5 + 0.3);  // 0.3–0.8
                float size     = (float)(rng.NextDouble() * 0.35 + 0.08);

                _dotX[i]       = x;
                _dotOffsetY[i] = offY;
                _dotParallax[i] = parallax;

                var go   = new GameObject($"BG_Dot{i}");
                go.transform.SetParent(transform, false);
                go.transform.localScale = new Vector3(size, size, 1f);

                // Mix of round pastel dots — 3 colour groups
                Color dotCol;
                int   group = i % 3;
                if      (group == 0) dotCol = new Color(0.70f, 0.84f, 0.96f, 0.15f); // blue
                else if (group == 1) dotCol = new Color(0.70f, 0.93f, 0.78f, 0.13f); // green
                else                 dotCol = new Color(0.96f, 0.80f, 0.70f, 0.12f); // peach

                var sr        = go.AddComponent<SpriteRenderer>();
                sr.sprite     = _whiteSprite;
                sr.color      = dotCol;
                sr.sortingOrder = -19;

                _dots[i] = go.transform;
            }
        }

        // ── Sprite factory ────────────────────────────────────────────────────────
        private static Sprite CreateWhiteSprite()
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
        }
    }
}
