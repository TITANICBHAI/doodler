using UnityEngine;

namespace DoodleClimb.Game
{
    /// <summary>
    /// Base class for all enemy types in DoodleClimb.
    ///
    /// Subclass and override MoveTick() for custom patrol / swoop behaviour.
    /// Call TakeDamage() when stomped or hit by player.
    ///
    /// Concrete subclasses: BirdEnemy, BatEnemy, GhostEnemy, UfoEnemy.
    /// The EnemySpawner pools these via their shared EnemyController component.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public abstract class EnemyController : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Stats")]
        [Tooltip("Hits required to defeat this enemy. 1 = one stomp; 2+ = armoured.")]
        public int maxHealth = 1;

        [Tooltip("Score awarded to the player on defeat.")]
        public int scoreOnDefeat = 15;

        [Tooltip("Does landing on top of this enemy defeat it? (false = contact damage only)")]
        public bool stompable = true;

        [Header("Movement")]
        public float moveSpeed    = 2.5f;
        public float patrolRange  = 3.0f;

        // ── Runtime ───────────────────────────────────────────────────────────────
        protected int   _health;
        protected float _startX;
        protected bool  _defeated;

        // ── Events ────────────────────────────────────────────────────────────────
        /// <summary>Fired by TakeDamage once health reaches 0.</summary>
        public System.Action<EnemyController> OnDefeated;

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        protected virtual void OnEnable()
        {
            _health   = maxHealth;
            _defeated = false;
            _startX   = transform.position.x;
        }

        private void Update()
        {
            if (_defeated) return;
            MoveTick();
        }

        // ── Overridable movement ──────────────────────────────────────────────────
        /// <summary>Called every Update frame while alive. Override for per-type movement.</summary>
        protected virtual void MoveTick()
        {
            // Default: left-right patrol using PingPong
            float x = Mathf.PingPong(Time.time * moveSpeed, patrolRange * 2f)
                      - patrolRange + _startX;
            transform.position = new Vector3(x, transform.position.y, 0f);
        }

        // ── Damage API ────────────────────────────────────────────────────────────
        /// <summary>
        /// Apply one hit of damage. Returns true if the enemy was defeated.
        /// </summary>
        public bool TakeDamage(int amount = 1)
        {
            if (_defeated) return false;
            _health -= amount;
            OnHit();
            if (_health <= 0)
            {
                Defeat();
                return true;
            }
            return false;
        }

        // ── Overridable callbacks ────────────────────────────────────────────────
        /// <summary>Called every time this enemy takes a hit (before defeat check).</summary>
        protected virtual void OnHit()
        {
            VisualEffects.Instance?.PlayLandDust(transform.position, Color.red);
        }

        /// <summary>Called once when health reaches zero.</summary>
        protected virtual void OnDefeatedCallback()
        {
            VisualEffects.Instance?.PlayDeathBurst(transform.position, Color.yellow);
        }

        // ── Collision ─────────────────────────────────────────────────────────────
        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_defeated) return;
            var player = other.GetComponent<Player.PlayerController>();
            if (player == null) return;

            if (stompable && IsStompedBy(player))
            {
                // Player bounced on top — damage enemy, give player a mini-jump
                TakeDamage();
                player.OnLanded(
                    platformType:    "Normal",
                    isMovingPlatform: false,
                    platformCentreX:  transform.position.x);
            }
            else
            {
                // Contact damage to player
                player.TakeDamage();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────
        private bool IsStompedBy(Player.PlayerController player)
        {
            // Player is moving downward and is above the enemy centre
            var rb = player.Rigidbody;
            if (rb == null) return false;
            return rb.velocity.y < -0.5f
                && player.transform.position.y > transform.position.y + 0.1f;
        }

        private void Defeat()
        {
            _defeated = true;
            OnDefeatedCallback();
            GameManager.Instance?.AddScore(scoreOnDefeat);
            OnDefeated?.Invoke(this);
            gameObject.SetActive(false);
        }
    }
}
