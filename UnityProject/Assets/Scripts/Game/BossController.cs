using System.Collections;
using UnityEngine;

namespace DoodleClimb.Game
{
    /// <summary>
    /// Boss enemy — spawns at score 480+, patrols and dives, requires 3 stomps to kill.
    ///
    /// Behaviour:
    ///   - Bounces horizontally, reversing at screen edges
    ///   - Dive cycle: every 5 s, dives downward for 1.4 s then returns
    ///   - Player stomps from above deal 1 HP; body contact removes shield/life
    ///   - On kill: drops 3 gems + 5 coins, awards 280 pts × combo multiplier
    /// </summary>
    public class BossController : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────────────────────────
        [Header("Movement")]
        public float patrolSpeed   = 2.8f;    // horizontal patrol speed
        public float diveSpeed     = 6.8f;    // downward dive speed
        public float diveDuration  = 1.4f;    // seconds diving down
        public float diveInterval  = 5.0f;    // seconds between dives

        [Header("Stats")]
        public int   maxHp         = 3;
        public float hitInvincTime = 0.45f;   // brief flash after stomp hit

        [Header("Loot")]
        public GameObject gemPrefab;
        public GameObject coinPrefab;
        public int        gemDropCount  = 3;
        public int        coinDropCount = 5;
        public int        baseScoreReward = 280;

        [Header("Projectile")]
        [Tooltip("Optional prefab for boss projectiles. If null, no shots are fired.")]
        public GameObject projectilePrefab;
        [Tooltip("Seconds between shots (halved when enraged).")]
        public float      shootInterval  = 4.5f;

        // ── Runtime ────────────────────────────────────────────────────────────────
        private int     _hp;
        private float   _diveTimer;
        private float   _hitTimer;
        private float   _shootTimer;
        private bool    _isDiving;
        private bool    _isDead;
        private bool    _isEnraged;
        private float   _halfScreenWidth;
        private int     _comboAtKill;

        // ── Events ─────────────────────────────────────────────────────────────────
        public System.Action<int> OnBossKilled;   // passes score reward

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            Camera cam = Camera.main;
            _halfScreenWidth = cam != null ? cam.orthographicSize * cam.aspect : 5f;
        }

        private void OnEnable()
        {
            _hp          = maxHp;
            _diveTimer   = 0f;
            _hitTimer    = 0f;
            _shootTimer  = shootInterval * 0.5f; // first shot sooner
            _isDiving    = false;
            _isDead      = false;
            _isEnraged   = false;

            // Start moving in a random direction
            float sign = Random.value < 0.5f ? 1f : -1f;
            GetComponent<Rigidbody2D>().velocity = new Vector2(sign * patrolSpeed, 0f);
        }

        private void Update()
        {
            if (_isDead) return;
            if (_hitTimer > 0f) _hitTimer -= Time.deltaTime;

            _diveTimer += Time.deltaTime;
            float phase = _diveTimer % diveInterval;
            _isDiving = phase < diveDuration;

            var rb = GetComponent<Rigidbody2D>();
            float vy = _isDiving ? -diveSpeed : Mathf.MoveTowards(rb.velocity.y, 0f, 20f * Time.deltaTime);
            float vx = rb.velocity.x;

            // Reverse at screen edges
            float edge = _halfScreenWidth - 0.8f;
            if (transform.position.x >  edge && vx > 0f) vx = -patrolSpeed;
            if (transform.position.x < -edge && vx < 0f) vx =  patrolSpeed;

            rb.velocity = new Vector2(vx, vy);

            // Flash red when hit
            var sr = GetComponentInChildren<SpriteRenderer>();
            if (sr != null)
                sr.color = _hitTimer > 0f ? new Color(1f, 0.4f, 0.1f)
                         : _isEnraged      ? new Color(0.65f, 0.06f, 0.01f)
                         : Color.red;

            // Boss projectile — fires while NOT diving (to give player a break)
            if (projectilePrefab != null && !_isDiving)
            {
                float interval = _isEnraged ? shootInterval * 0.5f : shootInterval;
                _shootTimer += Time.deltaTime;
                if (_shootTimer >= interval)
                {
                    _shootTimer = 0f;
                    GameObject proj = Instantiate(
                        projectilePrefab,
                        transform.position + Vector3.down * 0.5f,
                        Quaternion.identity);
                    // Give the projectile a downward velocity toward where the player is
                    var projRb = proj.GetComponent<Rigidbody2D>();
                    if (projRb != null)
                    {
                        var player = FindObjectOfType<Player.PlayerController>();
                        Vector2 dir = player != null
                            ? ((Vector2)(player.transform.position - transform.position)).normalized
                            : Vector2.down;
                        projRb.velocity = dir * 7f;
                    }
                }
            }
        }

        // ── Collision ─────────────────────────────────────────────────────────────
        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_isDead || _hitTimer > 0f) return;

            var player = other.GetComponent<Player.PlayerController>();
            if (player == null) return;

            var rb = player.Rigidbody;
            bool isStomping = rb.velocity.y < -0.5f &&
                              other.transform.position.y > transform.position.y + 0.1f;

            if (isStomping)
            {
                TakeHit(player);
            }
            else
            {
                player.TakeContactDamage();
            }
        }

        private void TakeHit(Player.PlayerController player)
        {
            _hp--;
            _hitTimer = hitInvincTime;

            // Bounce player up on stomp
            player.Rigidbody.velocity = new Vector2(player.Rigidbody.velocity.x, 14f);

            VisualEffects.Instance?.PlayLandDust(transform.position, Color.red);

            // Enrage at 1 HP — faster patrol and shorter dive interval
            if (_hp == 1 && !_isEnraged)
            {
                _isEnraged    = true;
                patrolSpeed  *= 1.65f;
                diveInterval *= 0.55f;

                var sr = GetComponentInChildren<SpriteRenderer>();
                if (sr != null) sr.color = new Color(1f, 0.18f, 0.02f);

                FindObjectOfType<CameraFollow>()?.ShakeCamera(0.35f, 0.4f);
                VisualEffects.Instance?.PlayDeathBurst(transform.position, new Color(1f, 0.3f, 0f));
            }

            if (_hp <= 0)
                StartCoroutine(DieRoutine(player));
        }

        private IEnumerator DieRoutine(Player.PlayerController player)
        {
            _isDead = true;
            GetComponent<Rigidbody2D>().velocity = Vector2.zero;

            VisualEffects.Instance?.PlayDeathBurst(transform.position, new Color(1f, 0.5f, 0f));
            VisualEffects.Instance?.PlayDeathBurst(transform.position, Color.yellow);

            // Shake camera
            FindObjectOfType<CameraFollow>()?.ShakeCamera(0.6f, 0.6f);

            // Drop loot
            DropLoot();

            // Score reward
            int combo  = player != null ? player.Combo : 1;
            int reward = Mathf.RoundToInt(baseScoreReward * ComboMult(combo));
            OnBossKilled?.Invoke(reward);

            yield return new WaitForSeconds(0.05f);
            gameObject.SetActive(false);
        }

        private void DropLoot()
        {
            if (gemPrefab != null)
                for (int i = 0; i < gemDropCount; i++)
                {
                    Vector3 p = transform.position + new Vector3((i - 1) * 0.7f, 0.5f, 0f);
                    Instantiate(gemPrefab, p, Quaternion.identity);
                }

            if (coinPrefab != null)
                for (int i = 0; i < coinDropCount; i++)
                {
                    Vector3 p = transform.position + new Vector3((i - 2) * 0.55f, 0.8f, 0f);
                    Instantiate(coinPrefab, p, Quaternion.identity);
                }
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

        // ── Gizmo ─────────────────────────────────────────────────────────────────
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, 0.9f);
        }
    }
}
