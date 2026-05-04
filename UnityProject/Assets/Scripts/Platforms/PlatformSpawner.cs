using System.Collections.Generic;
using UnityEngine;

namespace DoodleClimb.Platforms
{
    /// <summary>
    /// Procedurally spawns and recycles platforms as the player climbs.
    ///
    /// Object pooling:
    ///   Platforms are never Destroyed — they're disabled and returned to a pool
    ///   queue.  The pool auto-expands when all platforms are in use.
    ///
    /// Difficulty scales in two ways:
    ///   1. Height-based  — gaps grow, widths shrink, harder types unlock over time.
    ///   2. Skill-based (Platform Personality System) — once enough runs have been
    ///      recorded the spawner reads the player's AIProfile and adjusts difficulty
    ///      to match their specific weaknesses and strengths.
    ///
    /// Performance:
    ///   GetPlatformsAbove() iterates _activePlatforms in insertion order (already
    ///   bottom-to-top) and returns early — no sort needed every frame.
    ///   All component references are cached in Awake/Start.
    /// </summary>
    public class PlatformSpawner : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Prefab")]
        public GameObject platformPrefab;

        [Header("Spawn Settings")]
        [Tooltip("Initial pool size — expands automatically if needed.")]
        public int platformPoolSize = 24;

        [Tooltip("Vertical gap range between consecutive platforms (units).")]
        public Vector2 verticalGapRange = new Vector2(1.5f, 2.8f);

        [Tooltip("Gap increase per 100 units of player height.")]
        public float gapScalePerHeight = 0.002f;

        [Header("Platform Width")]
        public float startPlatformWidth    = 2.2f;
        public float minPlatformWidth      = 0.9f;
        [Tooltip("Width reduction per 100 units of height.")]
        public float widthReductionPerHeight = 0.03f;

        [Header("Height Thresholds for Special Platform Types")]
        public float movingStartHeight    =  40f;
        public float breakableStartHeight =  80f;
        public float temporaryStartHeight = 130f;

        [Header("Max Probabilities at Full Height Difficulty")]
        public float maxMovingChance    = 0.30f;
        public float maxBreakableChance = 0.20f;
        public float maxTemporaryChance = 0.15f;
        public float maxSpringChance    = 0.12f;
        public float maxIceChance       = 0.08f;
        public float maxConveyorChance  = 0.06f;
        public float maxBombChance      = 0.04f;
        public float maxGoldenChance    = 0.06f;
        public float maxRocketChance    = 0.05f;

        [Header("Height Thresholds — Special Types")]
        public float springStartHeight  = 15f;
        public float iceStartHeight     = 160f;
        public float conveyorStartHeight= 200f;
        public float bombStartHeight    = 240f;
        public float goldenStartHeight  = 100f;
        public float rocketStartHeight  = 300f;

        // ── Platform Personality System ───────────────────────────────────────────
        // Additive modifiers set by ApplySkillProfile() after training.
        private float _skillMovingBonus    = 0f;
        private float _skillBreakableBonus = 0f;
        private float _skillTemporaryBonus = 0f;
        private float _skillSpringBonus    = 0f;
        private float _skillWidthPenalty   = 0f;

        // ── Internal state ────────────────────────────────────────────────────────
        // _activePlatforms is maintained in insertion order (bottom → top)
        // so GetPlatformsAbove() never needs to sort.
        private readonly List<Platform>  _activePlatforms = new List<Platform>();
        private readonly Queue<Platform> _pool            = new Queue<Platform>();

        private float         _nextSpawnY;
        private float         _halfScreenWidth;
        private float         _cameraHalfHeight;   // cached to avoid Camera.main per frame
        private System.Random _rng;

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            if (Camera.main != null)
            {
                _halfScreenWidth  = Camera.main.orthographicSize * Camera.main.aspect;
                _cameraHalfHeight = Camera.main.orthographicSize;
            }
        }

        private void Start()
        {
            if (platformPrefab != null)
                InitPool();
        }

        // ── Platform Personality System ───────────────────────────────────────────
        /// <summary>
        /// Called by GameManager after training.
        /// Adjusts spawn difficulty to match the player's measured skill traits.
        /// </summary>
        public void ApplySkillProfile(AI.AIProfile p)
        {
            // Risk-takers face more volatile platforms
            _skillBreakableBonus = Mathf.Lerp(0f, 0.12f, p.riskLevel);
            _skillTemporaryBonus = Mathf.Lerp(0f, 0.10f, p.riskLevel);

            // Imprecise movers face more moving platforms
            _skillMovingBonus = Mathf.Lerp(0f, 0.15f, 1f - p.movementSmoothness);

            // Better players get more spring platforms as a reward
            _skillSpringBonus = Mathf.Lerp(0f, 0.06f, p.jumpPrecision);

            // Accurate landers face narrower platforms (fair challenge)
            _skillWidthPenalty = Mathf.Lerp(0f, 0.4f, p.landingAccuracy);

            Debug.Log($"[PlatformSpawner] Skill profile applied — " +
                      $"Moving+:{_skillMovingBonus:0.00} " +
                      $"Break+:{_skillBreakableBonus:0.00} " +
                      $"Temp+:{_skillTemporaryBonus:0.00} " +
                      $"Width-:{_skillWidthPenalty:0.00}");
        }

        // ── Level initialisation ──────────────────────────────────────────────────
        public void InitLevel(int seed)
        {
            _rng = new System.Random(seed);

            // Return all active platforms to the pool
            foreach (Platform p in _activePlatforms)
            {
                p.gameObject.SetActive(false);
                _pool.Enqueue(p);
            }
            _activePlatforms.Clear();

            _nextSpawnY = 0f;

            // Guaranteed safe starting platform directly beneath the player
            SpawnPlatformAt(0f, 0f, Platform.PlatformType.Static, startPlatformWidth);
            _nextSpawnY = verticalGapRange.x;

            // Pre-fill the visible screen area
            for (int i = 0; i < platformPoolSize - 1; i++)
                SpawnNextPlatform(0f);
        }

        // ── Per-frame update (called by GameManager.Update) ───────────────────────
        public void UpdateSpawner(float cameraTopY, float currentMaxPlayerHeight)
        {
            // Spawn ahead of the camera top
            while (_nextSpawnY < cameraTopY + 4f)
                SpawnNextPlatform(currentMaxPlayerHeight);

            // Recycle platforms well below the camera bottom (two screen-heights below top)
            float recycleY = cameraTopY - (_cameraHalfHeight > 0f
                ? _cameraHalfHeight * 2f + 3f
                : 20f);

            // Iterate forward (lowest platforms first) — stop once we hit active ones
            for (int i = 0; i < _activePlatforms.Count; )
            {
                Platform p = _activePlatforms[i];
                if (p == null || !p.gameObject.activeSelf)
                {
                    _activePlatforms.RemoveAt(i);
                    continue;
                }
                if (p.transform.position.y < recycleY)
                {
                    p.gameObject.SetActive(false);
                    _pool.Enqueue(p);
                    _activePlatforms.RemoveAt(i);
                    // don't increment i — next element shifted into this index
                }
                else
                {
                    // Since list is sorted bottom-to-top, once we hit an active
                    // platform above recycleY all remaining ones are too
                    break;
                }
            }
        }

        // ── Spawning ──────────────────────────────────────────────────────────────
        private void SpawnNextPlatform(float currentHeight)
        {
            float gapExtra = currentHeight * gapScalePerHeight;
            float gap = (float)(_rng.NextDouble()
                * (verticalGapRange.y - verticalGapRange.x + gapExtra))
                + verticalGapRange.x;

            _nextSpawnY += gap;

            // Width shrinks with height and with skill-based penalty
            float width = Mathf.Max(
                minPlatformWidth,
                startPlatformWidth
                    - currentHeight * widthReductionPerHeight
                    - _skillWidthPenalty);

            Platform.PlatformType type = PickPlatformType(currentHeight);

            // Random X within playfield, accounting for platform half-width
            float halfW = width * 0.5f;
            float maxX  = Mathf.Max(0f, _halfScreenWidth - halfW);
            float x     = (float)(_rng.NextDouble() * 2.0 - 1.0) * maxX;

            SpawnPlatformAt(x, _nextSpawnY, type, width);
        }

        private void SpawnPlatformAt(
            float x, float y, Platform.PlatformType type, float width)
        {
            Platform p = GetFromPool();
            p.platformType         = type;
            p.transform.position   = new Vector3(x, y, 0f);
            p.transform.localScale = new Vector3(
                width,
                p.transform.localScale.y,
                1f);
            p.gameObject.SetActive(true);
            _activePlatforms.Add(p); // appended at end = top of the sorted list
        }

        // ── Platform type selection ───────────────────────────────────────────────
        private Platform.PlatformType PickPlatformType(float height)
        {
            double roll = _rng.NextDouble();

            // Height-based probabilities (ramp up gradually from threshold)
            float movingBase = height > movingStartHeight
                ? Mathf.Min(maxMovingChance,
                    (height - movingStartHeight) / 200f * maxMovingChance)
                : 0f;
            float breakableBase = height > breakableStartHeight
                ? Mathf.Min(maxBreakableChance,
                    (height - breakableStartHeight) / 200f * maxBreakableChance)
                : 0f;
            float temporaryBase = height > temporaryStartHeight
                ? Mathf.Min(maxTemporaryChance,
                    (height - temporaryStartHeight) / 200f * maxTemporaryChance)
                : 0f;
            float springBase = height > springStartHeight
                ? Mathf.Min(maxSpringChance,
                    (height - springStartHeight) / 100f * maxSpringChance)
                : 0f;
            float iceBase = height > iceStartHeight
                ? Mathf.Min(maxIceChance,
                    (height - iceStartHeight) / 150f * maxIceChance)
                : 0f;
            float conveyorBase = height > conveyorStartHeight
                ? Mathf.Min(maxConveyorChance,
                    (height - conveyorStartHeight) / 150f * maxConveyorChance)
                : 0f;
            float bombBase = height > bombStartHeight
                ? Mathf.Min(maxBombChance,
                    (height - bombStartHeight) / 200f * maxBombChance)
                : 0f;
            float goldenBase = height > goldenStartHeight
                ? Mathf.Min(maxGoldenChance,
                    (height - goldenStartHeight) / 200f * maxGoldenChance)
                : 0f;
            float rocketBase = height > rocketStartHeight
                ? Mathf.Min(maxRocketChance,
                    (height - rocketStartHeight) / 200f * maxRocketChance)
                : 0f;

            // Add skill-based modifiers
            float movingChance    = Mathf.Clamp01(movingBase    + _skillMovingBonus);
            float breakableChance = Mathf.Clamp01(breakableBase + _skillBreakableBonus);
            float temporaryChance = Mathf.Clamp01(temporaryBase + _skillTemporaryBonus);
            float springChance    = Mathf.Clamp01(springBase    + _skillSpringBonus);
            float iceChance       = Mathf.Clamp01(iceBase);
            float conveyorChance  = Mathf.Clamp01(conveyorBase);
            float bombChance      = Mathf.Clamp01(bombBase);
            float goldenChance    = Mathf.Clamp01(goldenBase);
            float rocketChance    = Mathf.Clamp01(rocketBase);
            // Crumble: same curve as breakable but slightly rarer; appears above 400 m
            float crumbleBase   = height > 400f ? Mathf.Lerp(0f, 0.06f, Mathf.InverseLerp(400f, 900f, height)) : 0f;
            float crumbleChance = Mathf.Clamp01(crumbleBase);

            // Cap total special chance at 72% so there's always a floor of statics
            float total = movingChance + breakableChance + temporaryChance + springChance
                        + iceChance + conveyorChance + bombChance + goldenChance + rocketChance
                        + crumbleChance;
            if (total > 0.72f)
            {
                float scale   = 0.72f / total;
                movingChance    *= scale;
                breakableChance *= scale;
                temporaryChance *= scale;
                springChance    *= scale;
                iceChance       *= scale;
                conveyorChance  *= scale;
                bombChance      *= scale;
                goldenChance    *= scale;
                rocketChance    *= scale;
                crumbleChance   *= scale;
            }

            if (roll < springChance)    return Platform.PlatformType.Spring;
            roll -= springChance;
            if (roll < temporaryChance) return Platform.PlatformType.Temporary;
            roll -= temporaryChance;
            if (roll < breakableChance) return Platform.PlatformType.Breakable;
            roll -= breakableChance;
            if (roll < movingChance)    return Platform.PlatformType.Moving;
            roll -= movingChance;
            if (roll < goldenChance)    return Platform.PlatformType.Golden;
            roll -= goldenChance;
            if (roll < iceChance)       return Platform.PlatformType.Ice;
            roll -= iceChance;
            if (roll < conveyorChance)  return Platform.PlatformType.Conveyor;
            roll -= conveyorChance;
            if (roll < rocketChance)    return Platform.PlatformType.Rocket;
            roll -= rocketChance;
            if (roll < bombChance)      return Platform.PlatformType.Bomb;
            roll -= bombChance;
            if (roll < crumbleChance)   return Platform.PlatformType.Crumble;
            return Platform.PlatformType.Static;
        }

        // ── AI platform lookahead (called by AIPlayerController + GameManager) ─────
        /// <summary>
        /// Returns the next N platforms above aboveY, ordered bottom to top.
        /// _activePlatforms is already insertion-ordered (bottom→top) so
        /// no sort is required — just iterate forward and return early.
        /// </summary>
        public List<Platform> GetPlatformsAbove(float aboveY, int count = 3)
        {
            List<Platform> result = new List<Platform>(count);

            // _activePlatforms order: index 0 = lowest, last index = highest
            foreach (Platform p in _activePlatforms)
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
                Platform p = CreatePooledPlatform();
                _pool.Enqueue(p);
            }
        }

        private Platform GetFromPool()
        {
            if (_pool.Count > 0)
                return _pool.Dequeue();

            // Pool exhausted — expand automatically
            Debug.Log("[PlatformSpawner] Pool expanded.");
            return CreatePooledPlatform();
        }

        private Platform CreatePooledPlatform()
        {
            GameObject go = Instantiate(platformPrefab, Vector3.zero,
                                        Quaternion.identity, transform);
            go.SetActive(false);
            return go.GetComponent<Platform>();
        }

        /// <summary>Return a platform to the pool (called by Platform.cs).</summary>
        public void RecyclePlatform(Platform p)
        {
            if (p == null) return;
            p.gameObject.SetActive(false);
            _activePlatforms.Remove(p);
            _pool.Enqueue(p);
        }
    }
}
