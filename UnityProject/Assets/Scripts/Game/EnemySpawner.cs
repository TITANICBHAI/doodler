using System.Collections.Generic;
using UnityEngine;

namespace DoodleClimb.Game
{
    /// <summary>
    /// Procedural enemy spawner — places enemies above the camera as the player climbs.
    ///
    /// Enemy types:
    ///   Bird   — horizontal patrol, appears from height 50
    ///   Bat    — diagonal swoop, appears from height 120
    ///   Ghost  — slow vertical float, appears from height 200
    ///   UFO    — horizontal patrol + vertical drift, appears from height 280
    ///
    /// All enemies are pooled (no Destroy calls at runtime).
    /// Score thresholds and spawn rates are inspector-tunable.
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Enemy Prefabs (all optional)")]
        public GameObject birdPrefab;
        public GameObject batPrefab;
        public GameObject ghostPrefab;
        public GameObject ufoPrefab;

        [Header("Spawn Settings")]
        [Tooltip("Minimum vertical gap between enemy spawns.")]
        public float spawnGapMin    = 8f;
        [Tooltip("Maximum vertical gap between enemy spawns.")]
        public float spawnGapMax    = 16f;
        [Tooltip("How far above the camera top to spawn enemies.")]
        public float spawnAheadY    = 12f;

        [Header("Height Thresholds")]
        public float birdStartHeight  =  50f;
        public float batStartHeight   = 120f;
        public float ghostStartHeight = 200f;
        public float ufoStartHeight   = 280f;

        [Header("Max Spawn Chances (per gap)")]
        [Range(0f, 1f)] public float maxBirdChance  = 0.35f;
        [Range(0f, 1f)] public float maxBatChance   = 0.25f;
        [Range(0f, 1f)] public float maxGhostChance = 0.15f;
        [Range(0f, 1f)] public float maxUfoChance   = 0.12f;

        // ── Pool ──────────────────────────────────────────────────────────────────
        private readonly Queue<GameObject> _birdPool  = new Queue<GameObject>();
        private readonly Queue<GameObject> _batPool   = new Queue<GameObject>();
        private readonly Queue<GameObject> _ghostPool = new Queue<GameObject>();
        private readonly Queue<GameObject> _ufoPool   = new Queue<GameObject>();

        private readonly List<GameObject>  _active    = new List<GameObject>();

        // ── Internal state ────────────────────────────────────────────────────────
        private float         _nextSpawnY;
        private float         _halfScreenWidth;
        private System.Random _rng;

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            if (Camera.main != null)
                _halfScreenWidth = Camera.main.orthographicSize * Camera.main.aspect;
        }

        /// <summary>
        /// Called once per frame by GameManager after the session starts.
        /// </summary>
        /// <param name="cameraTopY">Top of the visible camera area.</param>
        /// <param name="currentHeight">Player's max height for difficulty scaling.</param>
        public void UpdateSpawner(float cameraTopY, float currentHeight)
        {
            if (_rng == null)
            {
                _rng = new System.Random();
                _nextSpawnY = cameraTopY + spawnAheadY;
            }

            // Recycle enemies that have scrolled below the camera bottom
            float recycleY = cameraTopY - Camera.main.orthographicSize * 2f - 4f;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i] == null || !_active[i].activeInHierarchy)
                { _active.RemoveAt(i); continue; }
                if (_active[i].transform.position.y < recycleY)
                { ReturnToPool(_active[i]); _active.RemoveAt(i); }
            }

            // Spawn ahead of camera
            while (_nextSpawnY < cameraTopY + spawnAheadY)
                SpawnEnemyAt(currentHeight);
        }

        // ── Spawn ─────────────────────────────────────────────────────────────────
        private void SpawnEnemyAt(float height)
        {
            float gap   = Mathf.Lerp(spawnGapMin, spawnGapMax, (float)_rng.NextDouble());
            _nextSpawnY += gap;

            GameObject prefab = PickPrefab(height);
            if (prefab == null) return;

            Queue<GameObject> pool = PoolFor(prefab);
            GameObject go;
            if (pool.Count > 0)
            {
                go = pool.Dequeue();
                go.SetActive(true);
            }
            else
            {
                go = Instantiate(prefab, transform);
            }

            float x = (float)(_rng.NextDouble() * 2.0 - 1.0) * (_halfScreenWidth * 0.85f);
            go.transform.position = new Vector3(x, _nextSpawnY, 0f);

            // Wire death callback to GameManager
            var ec = go.GetComponent<EnemyController>();
            if (ec != null)
            {
                ec.OnDefeated -= HandleEnemyDefeated;   // prevent double-subscribe
                ec.OnDefeated += HandleEnemyDefeated;
            }

            _active.Add(go);
        }

        private void HandleEnemyDefeated(EnemyController ec)
        {
            GameManager.Instance?.NotifyEnemyDefeated();
        }

        // ── Pool helpers ──────────────────────────────────────────────────────────
        private void ReturnToPool(GameObject go)
        {
            go.SetActive(false);
            Queue<GameObject> pool = PoolFor(go);
            if (pool != null) pool.Enqueue(go);
        }

        private Queue<GameObject> PoolFor(GameObject go)
        {
            if (birdPrefab  != null && go.name.Contains(birdPrefab.name))  return _birdPool;
            if (batPrefab   != null && go.name.Contains(batPrefab.name))   return _batPool;
            if (ghostPrefab != null && go.name.Contains(ghostPrefab.name)) return _ghostPool;
            if (ufoPrefab   != null && go.name.Contains(ufoPrefab.name))   return _ufoPool;
            return _birdPool; // fallback
        }

        // ── Enemy selection ───────────────────────────────────────────────────────
        private GameObject PickPrefab(float height)
        {
            // Build a weighted list of available enemy types
            var candidates = new List<(GameObject prefab, float weight)>();

            if (birdPrefab != null && height >= birdStartHeight)
            {
                float w = Mathf.Min(maxBirdChance,
                    (height - birdStartHeight) / 100f * maxBirdChance);
                candidates.Add((birdPrefab, w));
            }
            if (batPrefab != null && height >= batStartHeight)
            {
                float w = Mathf.Min(maxBatChance,
                    (height - batStartHeight) / 100f * maxBatChance);
                candidates.Add((batPrefab, w));
            }
            if (ghostPrefab != null && height >= ghostStartHeight)
            {
                float w = Mathf.Min(maxGhostChance,
                    (height - ghostStartHeight) / 120f * maxGhostChance);
                candidates.Add((ghostPrefab, w));
            }
            if (ufoPrefab != null && height >= ufoStartHeight)
            {
                float w = Mathf.Min(maxUfoChance,
                    (height - ufoStartHeight) / 150f * maxUfoChance);
                candidates.Add((ufoPrefab, w));
            }

            if (candidates.Count == 0) return null;

            // Weighted random selection
            float total = 0f;
            foreach (var (_, w) in candidates) total += w;
            float roll  = (float)_rng.NextDouble() * total;
            float accum = 0f;
            foreach (var (prefab, w) in candidates)
            {
                accum += w;
                if (roll <= accum) return prefab;
            }
            return candidates[0].prefab;
        }
    }
}
