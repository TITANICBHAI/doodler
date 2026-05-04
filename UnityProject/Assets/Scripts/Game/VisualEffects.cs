using UnityEngine;

namespace DoodleClimb.Game
{
    /// <summary>
    /// Central particle / visual-effects coordinator.
    /// All particle systems are built entirely in C# — no asset imports required.
    ///
    /// Effects:
    ///   PlayLandDust   — small puff when a character lands on a platform
    ///   PlayJumpDust   — quick upward puff on jump (optional, subtle)
    ///   PlaySpringBounce — yellow-green burst when landing on a Spring platform
    ///   PlayDeathBurst — colourful explosion when a character dies
    ///   PlayComboFlash — bright ring when combo threshold is crossed
    /// </summary>
    public class VisualEffects : MonoBehaviour
    {
        public static VisualEffects Instance { get; private set; }

        // ── Particle systems ──────────────────────────────────────────────────────
        private ParticleSystem _landDust;
        private ParticleSystem _jumpDust;
        private ParticleSystem _springBurst;
        private ParticleSystem _deathBurst;
        private ParticleSystem _comboFlash;

        // Shared circle texture for all particle systems
        private Texture2D _circleTex;

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            _circleTex   = CreateCircleTexture(32);

            _landDust    = BuildSystem("LandDust",    count:  8, lifetime: 0.40f,
                                       speedMin: 1.5f, speedMax: 3.5f,
                                       sizeMin: 0.06f, sizeMax: 0.16f, gravity: 1.2f,
                                       coneAngle: 160f);

            _jumpDust    = BuildSystem("JumpDust",    count:  6, lifetime: 0.30f,
                                       speedMin: 0.8f, speedMax: 2.0f,
                                       sizeMin: 0.05f, sizeMax: 0.12f, gravity: 0.8f,
                                       coneAngle: 60f);

            _springBurst = BuildSystem("SpringBurst", count: 14, lifetime: 0.55f,
                                       speedMin: 3.0f, speedMax: 6.0f,
                                       sizeMin: 0.10f, sizeMax: 0.22f, gravity: 0.6f,
                                       coneAngle: 80f);

            _deathBurst  = BuildSystem("DeathBurst",  count: 28, lifetime: 0.80f,
                                       speedMin: 2.5f, speedMax: 6.5f,
                                       sizeMin: 0.12f, sizeMax: 0.30f, gravity: 0.5f,
                                       coneAngle: 360f);

            _comboFlash  = BuildSystem("ComboFlash",  count: 16, lifetime: 0.45f,
                                       speedMin: 2.0f, speedMax: 4.0f,
                                       sizeMin: 0.08f, sizeMax: 0.18f, gravity: 0.3f,
                                       coneAngle: 360f);
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>Landing puff. charColor is blended with white for variation.</summary>
        public void PlayLandDust(Vector3 pos, Color charColor)
        {
            if (_landDust == null) return;
            _landDust.transform.position = pos + Vector3.down * 0.3f;
            SetColor(_landDust, Color.Lerp(charColor, Color.white, 0.55f),
                                Color.Lerp(charColor, Color.white, 0.85f));
            _landDust.Emit(_landDust.main.maxParticles);
        }

        /// <summary>Subtle upward puff on jump.</summary>
        public void PlayJumpDust(Vector3 pos, Color charColor)
        {
            if (_jumpDust == null) return;
            _jumpDust.transform.position = pos + Vector3.down * 0.2f;
            SetColor(_jumpDust, Color.Lerp(charColor, Color.white, 0.7f),
                                Color.Lerp(charColor, Color.white, 0.9f));
            _jumpDust.Emit(_jumpDust.main.maxParticles);
        }

        /// <summary>Yellow-green burst for Spring platform landings.</summary>
        public void PlaySpringBounce(Vector3 pos)
        {
            if (_springBurst == null) return;
            _springBurst.transform.position = pos;
            SetColor(_springBurst,
                     new Color(0.40f, 0.90f, 0.25f),
                     new Color(0.90f, 1.00f, 0.20f));
            _springBurst.Emit(_springBurst.main.maxParticles);
        }

        /// <summary>Yellow-green burst when landing on a Spring platform.</summary>
        public void PlaySpringBounce(Vector3 pos)
        {
            if (_springBurst == null) return;
            _springBurst.transform.position = pos;
            SetColor(_springBurst, new Color(0.8f, 1f, 0.1f), Color.white);
            _springBurst.Emit(_springBurst.main.maxParticles);
        }

        /// <summary>Big colourful explosion on character death.</summary>
        public void PlayDeathBurst(Vector3 pos, Color charColor)
        {
            if (_deathBurst == null) return;
            _deathBurst.transform.position = pos;
            SetColor(_deathBurst, charColor, Color.white);
            _deathBurst.Emit(_deathBurst.main.maxParticles);
        }

        /// <summary>Radial ring of sparks when a combo threshold is hit.</summary>
        public void PlayComboFlash(Vector3 pos, Color color)
        {
            if (_comboFlash == null) return;
            _comboFlash.transform.position = pos;
            SetColor(_comboFlash, color, Color.white);
            _comboFlash.Emit(_comboFlash.main.maxParticles);
        }

        /// <summary>Gold coin-burst when landing on a Golden platform.</summary>
        public void PlayGoldBurst(Vector3 pos)
        {
            // Reuse deathBurst with a warm gold tint — cheap and looks great
            if (_deathBurst == null) return;
            _deathBurst.transform.position = pos;
            SetColor(_deathBurst, new Color(1f, 0.85f, 0.10f), new Color(1f, 1f, 0.60f));
            _deathBurst.Emit(Mathf.Min(12, _deathBurst.main.maxParticles));
        }

        // ── Factory helpers ───────────────────────────────────────────────────────
        private ParticleSystem BuildSystem(
            string id, int count,
            float lifetime, float speedMin, float speedMax,
            float sizeMin,  float sizeMax,
            float gravity,  float coneAngle)
        {
            var go = new GameObject("VFX_" + id);
            go.transform.SetParent(transform, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            // Main module
            var main              = ps.main;
            main.loop             = false;
            main.playOnAwake      = false;
            main.maxParticles     = count;
            main.startLifetime    = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime);
            main.startSpeed       = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize        = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.gravityModifier  = gravity;
            main.startRotation    = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);

            // Emission — burst only (no continuous)
            var emission = ps.emission;
            emission.enabled = false;

            // Shape — cone / sphere based on angle
            var shape = ps.shape;
            shape.enabled   = true;
            shape.shapeType = coneAngle >= 350f
                ? ParticleSystemShapeType.Sphere
                : ParticleSystemShapeType.Cone;
            shape.angle     = coneAngle >= 350f ? 0f : coneAngle * 0.5f;
            shape.radius    = 0.15f;

            // Colour over lifetime — fade out
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad    = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f),
                         new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f),
                         new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            // Size over lifetime — shrink toward end
            var sizeOL   = ps.sizeOverLifetime;
            sizeOL.enabled = true;
            var sizeCurve  = new AnimationCurve();
            sizeCurve.AddKey(0f,  1f);
            sizeCurve.AddKey(0.6f, 0.9f);
            sizeCurve.AddKey(1f,  0f);
            sizeOL.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            // Renderer
            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.renderMode  = ParticleSystemRenderMode.Billboard;
            rend.sortingOrder = 10;
            Material mat      = new Material(Shader.Find("Sprites/Default"));
            mat.mainTexture   = _circleTex;
            rend.material     = mat;

            return ps;
        }

        private static void SetColor(ParticleSystem ps, Color from, Color to)
        {
            var main        = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(from, to);
        }

        // ── Texture factory ───────────────────────────────────────────────────────
        private static Texture2D CreateCircleTexture(int res)
        {
            var   tex = new Texture2D(res, res, TextureFormat.ARGB32, false);
            float ctr = res / 2f;
            float rad = ctr - 0.5f;
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f),
                                           new Vector2(ctr, ctr));
                // Smooth anti-aliased edge
                float alpha = Mathf.Clamp01(1f - (d - (rad - 1f)));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
            tex.Apply();
            return tex;
        }
    }
}
