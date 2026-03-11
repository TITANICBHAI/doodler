using System.Collections.Generic;
using UnityEngine;

namespace DoodleClimb.Platforms
{
    /// <summary>
    /// Procedurally spawns and recycles platforms as the player climbs.
    /// Difficulty increases with height:
    ///   - Platform width decreases
    ///   - Gap between platforms increases slightly
    ///   - Proportion of special platform types grows
    /// </summary>
    public class PlatformSpawner : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Prefabs")]
        public GameObject platformPrefab;

        [Header("Spawn Settings")]
        [Tooltip("Number of platforms to keep active at once.")]
        public int platformPoolSize = 20;

        [Tooltip("Vertical gap range between platforms (min, max).")]
        public Vector2 verticalGapRange = new Vector2(1.5f, 2.8f);

        [Tooltip("Vertical gap increase per 100 units of height.")]
        public float gapScalePerHeight = 0.002f;

        [Header("Platform Width")]
        public float startPlatformWidth = 2.2f;
        public float minPlatformWidth = 0.9f;
        [Tooltip("Width reduction per 100 units of height.")]
        public float widthReductionPerHeight = 0.03f;

        [Header("Type Thresholds (height)")]
        public float movingStartHeight = 40f;
        public float breakableStartHeight = 80f;
        public float temporaryStartHeight = 130f;

        [Header("Type Probabilities at max difficulty [0-1]")]
        public float maxMovingChance = 0.30f;
        public float maxBreakableChance = 0.20f;
        public float maxTemporaryChance = 0.15f;

        // ── Internal state ────────────────────────────────────────────────────────
        private List<Platform> _activePlatforms = new List<Platform>();
        private Queue<Platform> _pool = new Queue<Platform>();
        private float _nextSpawnY;
        private float _halfScreenWidth;

        // ── Seed / shared level generation ────────────────────────────────────────
        private int _levelSeed;
        private System.Random _rng;

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            _halfScreenWidth = Camera.main.orthographicSize * Camera.main.aspect;
        }

        private void Start()
        {
            InitPool();
        }

        // ── Initialisation ────────────────────────────────────────────────────────
        public void InitLevel(int seed)
        {
            _levelSeed = seed;
            _rng = new System.Random(seed);

            // Clear existing platforms
            foreach (Platform p in _activePlatforms)
                p.gameObject.SetActive(false);
            _activePlatforms.Clear();

            _nextSpawnY = 0f;

            // Guarantee a safe starting platform under the player
            SpawnPlatformAt(0f, 0f, Platform.PlatformType.Static, startPlatformWidth);
            _nextSpawnY = verticalGapRange.x;

            // Pre-fill the visible area
            for (int i = 0; i < platformPoolSize - 1; i++)
                SpawnNextPlatform(0f);
        }

        // ── Per-frame update (called by GameManager) ──────────────────────────────
        public void UpdateSpawner(float cameraTopY, float currentMaxPlayerHeight)
        {
            // Spawn new platforms above the camera
            while (_nextSpawnY < cameraTopY + 4f)
                SpawnNextPlatform(currentMaxPlayerHeight);

            // Recycle platforms that have fallen off the bottom of the screen
            float recycleY = cameraTopY - Camera.main.orthographicSize * 2f - 3f;
            for (int i = _activePlatforms.Count - 1; i >= 0; i--)
            {
                if (_activePlatforms[i] == null || !_activePlatforms[i].gameObject.activeSelf)
                {
                    _activePlatforms.RemoveAt(i);
                    continue;
                }
                if (_activePlatforms[i].transform.position.y < recycleY)
                {
                    RecyclePlatform(_activePlatforms[i]);
                    _activePlatforms.RemoveAt(i);
                }
            }
        }

        // ── Spawning logic ────────────────────────────────────────────────────────
        private void SpawnNextPlatform(float currentHeight)
        {
            float gapExtra = currentHeight * gapScalePerHeight;
            float gap = (float)(_rng.NextDouble()
                * (verticalGapRange.y - verticalGapRange.x + gapExtra))
                + verticalGapRange.x;

            _nextSpawnY += gap;

            float width = Mathf.Max(
                minPlatformWidth,
                startPlatformWidth - currentHeight * widthReductionPerHeight
            );

            Platform.PlatformType type = PickPlatformType(currentHeight);
            float x = (float)(_rng.NextDouble() * 2.0 - 1.0) * (_halfScreenWidth - width * 0.5f);

            SpawnPlatformAt(x, _nextSpawnY, type, width);
        }

        private void SpawnPlatformAt(float x, float y, Platform.PlatformType type, float width)
        {
            Platform p = GetFromPool();
            p.platformType = type;
            p.transform.position = new Vector3(x, y, 0f);
            p.transform.localScale = new Vector3(width, p.transform.localScale.y, 1f);
            p.gameObject.SetActive(true);
            _activePlatforms.Add(p);
        }

        // ── Difficulty / type selection ───────────────────────────────────────────
        private Platform.PlatformType PickPlatformType(float height)
        {
            double roll = _rng.NextDouble();
            float movingChance = height > movingStartHeight
                ? Mathf.Min(maxMovingChance, (height - movingStartHeight) / 200f * maxMovingChance) : 0f;
            float breakableChance = height > breakableStartHeight
                ? Mathf.Min(maxBreakableChance, (height - breakableStartHeight) / 200f * maxBreakableChance) : 0f;
            float temporaryChance = height > temporaryStartHeight
                ? Mathf.Min(maxTemporaryChance, (height - temporaryStartHeight) / 200f * maxTemporaryChance) : 0f;

            if (roll < temporaryChance) return Platform.PlatformType.Temporary;
            roll -= temporaryChance;
            if (roll < breakableChance) return Platform.PlatformType.Breakable;
            roll -= breakableChance;
            if (roll < movingChance) return Platform.PlatformType.Moving;
            return Platform.PlatformType.Static;
        }

        // ── Platform lookahead (used by AI) ──────────────────────────────────────
        /// <summary>
        /// Returns the next N platforms above a given Y position, ordered bottom to top.
        /// Used by the AI to plan ahead.
        /// </summary>
        public List<Platform> GetPlatformsAbove(float aboveY, int count = 3)
        {
            List<Platform> result = new List<Platform>();
            List<Platform> candidates = new List<Platform>(_activePlatforms);
            candidates.Sort((a, b) => a.transform.position.y.CompareTo(b.transform.position.y));

            foreach (Platform p in candidates)
            {
                if (p == null || !p.gameObject.activeSelf || p.IsBroken) continue;
                if (p.transform.position.y > aboveY)
                {
                    result.Add(p);
                    if (result.Count >= count) break;
                }
            }
            return result;
        }

        // ── Pool management ───────────────────────────────────────────────────────
        private void InitPool()
        {
            for (int i = 0; i < platformPoolSize; i++)
            {
                GameObject go = Instantiate(platformPrefab, Vector3.zero, Quaternion.identity, transform);
                go.SetActive(false);
                _pool.Enqueue(go.GetComponent<Platform>());
            }
        }

        private Platform GetFromPool()
        {
            if (_pool.Count > 0)
                return _pool.Dequeue();

            // Pool exhausted — expand it
            GameObject go = Instantiate(platformPrefab, Vector3.zero, Quaternion.identity, transform);
            return go.GetComponent<Platform>();
        }

        public void RecyclePlatform(Platform p)
        {
            p.gameObject.SetActive(false);
            _activePlatforms.Remove(p);
            _pool.Enqueue(p);
        }
    }
}
