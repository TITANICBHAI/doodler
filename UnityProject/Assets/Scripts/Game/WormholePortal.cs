using System.Collections;
using UnityEngine;

namespace DoodleClimb.Game
{
    /// <summary>
    /// Wormhole portal — spawns in Deep Space zone (score 850+).
    /// Player walks into it to teleport upward 6-8 units instantly
    /// and receive a score bonus.
    ///
    /// Visual: two rotating ring sprites + purple inner glow (driven by shader
    /// or simple SpriteRenderer tint cycle). Falls back gracefully with no assets.
    /// </summary>
    public class WormholePortal : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────────────────────────
        [Header("Teleport")]
        public float teleportMinUnits = 6f;
        public float teleportMaxUnits = 8f;
        public float scoreBonusPerUnit = 0.55f;   // pts per unit teleported

        [Header("Visuals")]
        public float outerRingSpeed  = 120f;      // deg/s
        public float innerRingSpeed  = -180f;     // opposite rotation
        public float pulsePeriod     = 0.8f;      // seconds per pulse cycle

        // ── Runtime ────────────────────────────────────────────────────────────────
        private bool    _used;
        private float   _time;
        private Transform _outerRing;
        private Transform _innerRing;
        private SpriteRenderer _glowSR;

        // ── Events ─────────────────────────────────────────────────────────────────
        public System.Action<float, int> OnEntered; // (teleportDist, scoreBonus)

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            _outerRing = transform.Find("OuterRing");
            _innerRing = transform.Find("InnerRing");
            _glowSR    = transform.Find("Glow")?.GetComponent<SpriteRenderer>();
        }

        private void OnEnable()
        {
            _used = false;
            _time = 0f;
        }

        private void Update()
        {
            if (_used) return;

            _time += Time.deltaTime;

            // Rotate rings
            if (_outerRing != null)
                _outerRing.Rotate(Vector3.forward, outerRingSpeed * Time.deltaTime);
            if (_innerRing != null)
                _innerRing.Rotate(Vector3.forward, innerRingSpeed * Time.deltaTime);

            // Pulse glow alpha
            if (_glowSR != null)
            {
                float a = 0.38f + 0.22f * Mathf.Sin(_time * (2f * Mathf.PI / pulsePeriod));
                Color c = _glowSR.color;
                _glowSR.color = new Color(c.r, c.g, c.b, a);
            }
        }

        // ── Collision ─────────────────────────────────────────────────────────────
        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_used) return;
            var player = other.GetComponent<Player.PlayerController>();
            if (player == null) return;

            _used = true;
            StartCoroutine(TeleportRoutine(player));
        }

        private IEnumerator TeleportRoutine(Player.PlayerController player)
        {
            float dist  = Random.Range(teleportMinUnits, teleportMaxUnits);
            int   bonus = Mathf.RoundToInt(dist * scoreBonusPerUnit);

            // Flash
            VisualEffects.Instance?.PlayDeathBurst(transform.position, new Color(0.63f, 0.25f, 1f));
            VisualEffects.Instance?.PlayDeathBurst(transform.position, new Color(0f, 0.8f, 1f));

            // Move player up
            Vector3 p = player.transform.position;
            player.transform.position = new Vector3(p.x, p.y + dist, p.z);

            // Give velocity boost
            var rb = player.Rigidbody;
            if (rb != null) rb.velocity = new Vector2(rb.velocity.x, 14f);

            // Camera snap
            FindObjectOfType<CameraFollow>()?.ShakeCamera(0.45f, 0.45f);

            OnEntered?.Invoke(dist, bonus);

            yield return new WaitForSeconds(0.1f);
            gameObject.SetActive(false);
        }

        // ── Gizmo ─────────────────────────────────────────────────────────────────
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.63f, 0.25f, 1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, 1.0f);
        }
    }
}
