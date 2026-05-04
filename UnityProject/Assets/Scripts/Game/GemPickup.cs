using UnityEngine;

namespace DoodleClimb.Game
{
    /// <summary>
    /// Gem collectible — worth 20 pts × combo multiplier.
    /// Rotates as a visual. Magnet power-up pulls gems within 5 units.
    ///
    /// Spawn conditions: score > 280, 6 % chance per platform row.
    /// Boss kill always drops 3 gems.
    /// </summary>
    public class GemPickup : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────────────────────────
        public float rotateSpeed  = 180f;     // deg/s around Z-axis
        public float bobAmplitude = 0.08f;    // vertical bob units
        public float bobFrequency = 2.0f;     // Hz
        public int   baseValue    = 20;       // pts before combo multiplier

        public float magnetRadius = 5.0f;     // units pulled by magnet power-up

        // ── Runtime ────────────────────────────────────────────────────────────────
        private float   _startY;
        private float   _time;
        private bool    _collected;

        // ── Events ─────────────────────────────────────────────────────────────────
        public System.Action<int> OnCollected;   // passes score awarded

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void OnEnable()
        {
            _startY    = transform.position.y;
            _time      = 0f;
            _collected = false;
        }

        private void Update()
        {
            if (_collected) return;

            _time += Time.deltaTime;

            // Rotate
            transform.Rotate(Vector3.forward, rotateSpeed * Time.deltaTime);

            // Bob
            Vector3 p = transform.position;
            p.y = _startY + Mathf.Sin(_time * bobFrequency * 2f * Mathf.PI) * bobAmplitude;
            transform.position = p;

            // Magnet pull — check if player has magnet active
            var player = FindObjectOfType<Player.PlayerController>();
            if (player != null && player.MagnetActive)
            {
                float dist = Vector2.Distance(transform.position, player.transform.position);
                if (dist < magnetRadius)
                {
                    Vector3 dir = (player.transform.position - transform.position).normalized;
                    transform.position += dir * 8f * Time.deltaTime;
                }
            }
        }

        // ── Collision ─────────────────────────────────────────────────────────────
        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_collected) return;
            var player = other.GetComponent<Player.PlayerController>();
            if (player == null) return;

            _collected = true;

            int combo  = player.Combo;
            int reward = Mathf.RoundToInt(baseValue * ComboMult(combo));

            VisualEffects.Instance?.PlayLandDust(transform.position, new Color(0.61f, 0.19f, 1f));
            OnCollected?.Invoke(reward);

            gameObject.SetActive(false);
        }

        private static float ComboMult(int combo)
        {
            if (combo >= 20) return 4.0f;
            if (combo >= 15) return 3.0f;
            if (combo >= 10) return 2.5f;
            if (combo >= 5)  return 2.0f;
            if (combo >= 3)  return 1.5f;
            return 1.0f;
        }
    }
}
