# DoodleClimb — Unity 2D AI Game

A Doodle Jump-inspired vertical platformer with an AI clone system that learns
from your own gameplay and challenges you to beat your recorded self.

---

## Project Structure

```
Assets/
  Scripts/
    Player/
      PlayerController.cs       — Human player: movement, jump, recording hooks
    Platforms/
      Platform.cs               — Static / Moving / Breakable / Temporary logic
      PlatformSpawner.cs        — Procedural spawning with difficulty scaling
    AI/
      AIRecorder.cs             — Records player actions + ghost replay frames
      AITrainer.cs              — Builds statistical AIProfile from recorded data
      AIPlayerController.cs     — AI clone: Ghost Replay + Behaviour AI (hybrid)
    Game/
      GameManager.cs            — Central coordinator, death detection, score
      GameModeManager.cs        — Mode state (Normal / vs AI)
      CameraFollow.cs           — Upward-only smooth camera
    UI/
      UIManager.cs              — All panels: Start Menu, HUD, Game Over
```

---

## Unity Setup Steps

### 1. Create a New Unity Project
- Open Unity Hub → New Project → 2D (Core) template
- Target platform: **Android** (File → Build Settings → Switch Platform)

### 2. Install TextMeshPro
- Window → Package Manager → search **TextMeshPro** → Install
- When prompted, also import the TMP Essentials

### 3. Copy Scripts
- Drag the `Assets/` folder from this project into your Unity project's Assets folder.
- Unity will compile all scripts automatically.

### 4. Create the Scene

#### Game Objects needed:
| Object | Components |
|---|---|
| Player | `PlayerController`, `Rigidbody2D`, `BoxCollider2D`, `SpriteRenderer` |
| AIPlayer | `AIPlayerController`, `Rigidbody2D`, `BoxCollider2D`, `SpriteRenderer` |
| Platform Prefab | `Platform`, `Rigidbody2D` (Kinematic), `BoxCollider2D`, `SpriteRenderer` |
| GameManager | `GameManager`, `GameModeManager` |
| AIRecorder | `AIRecorder` |
| AITrainer | `AITrainer` |
| Main Camera | `CameraFollow` |
| Canvas | `UIManager` + all UI panels (see below) |

#### Rigidbody2D settings for Player / AIPlayer:
- Body Type: **Dynamic**
- Collision Detection: **Continuous**
- Freeze Rotation Z: ✓
- Gravity Scale: **1**

#### Platform Prefab:
- Create a 2D Sprite (default white square, tinted by code at runtime)
- Add `BoxCollider2D` — set Is Trigger: **OFF**
- Add `Platform` script
- Save as a Prefab in `Assets/Prefabs/`

### 5. Wire Up References in Inspector

**GameManager:**
- `player` → Player GameObject
- `aiPlayer` → AIPlayer GameObject
- `platformSpawner` → PlatformSpawner GameObject
- `cameraFollow` → Main Camera
- `uiManager` → Canvas/UIManager
- `playerSpawnPoint` → an empty Transform at (0, 1, 0)
- `aiPlayerSpawnPoint` → an empty Transform at (0.6, 1, 0)

**PlatformSpawner:**
- `platformPrefab` → your Platform prefab

**CameraFollow:**
- `playerTransform` → Player Transform

---

## AI System — How It Works

### Phase 1 — Player Runs (Normal Mode)
Every run, `AIRecorder` captures:
- **Behaviour samples** every 50 ms: position, velocity, jump timing, platform type
- **Ghost frames** every 50 ms: exact position timeline for replay

### Phase 2 — Training (automatic at run end)
`AITrainer.TrainFromLatestRun()` calculates:
- `avgMoveSpeed` — mean |velocityX| across all jumps
- `avgJumpDelay` — mean time from landing to jumping
- `directionBias` — net left/right tendency (−1 to +1)
- `reactionTime` — how fast the player reacts to moving platforms
- `riskLevel` — how often the player lands on dangerous platforms
- `jumpPrecision` — consistency of jump timing (inverse std-dev)
- `movementSmoothness` — how rarely the player changes direction

Each new run **blends** the new data into the existing profile using a 35% learning
rate — so the AI gets better gradually over many runs rather than forgetting old ones.

### Phase 3 — vs AI Mode
`AIPlayerController` uses a **Hybrid** system:
1. **Ghost Replay** — plays back the exact positions from your best run
2. **Behaviour AI** — when ghost ends, switches to statistical movement:
   - Looks at the **next 3 platforms** to plan ahead
   - Moves with learned speed + direction bias + noise
   - Delays jumps using learned timing
   - Predicts moving platform position at arrival time

---

## Game Modes

| Mode | Description |
|---|---|
| **Normal Play** | Classic endless climb. Every run trains the AI. |
| **vs AI Clone** | Both you and your AI spawn on the same procedural level. Whoever climbs higher wins. |

The "vs AI" button is locked until at least one Normal run has been completed.

---

## Platform Types

| Type | Colour | Behaviour |
|---|---|---|
| Static | Green | Always stable |
| Moving | Blue | Oscillates horizontally. Width/speed increase with height. |
| Breakable | Orange-Red | Collapses 0.15 s after contact |
| Temporary | Yellow | Disappears after 2 s — fades out with collider disabled |

Moving and special platforms only start appearing after the player reaches defined
height thresholds (configurable in `PlatformSpawner` Inspector fields).

---

## Building to Android

1. File → Build Settings → Android → Switch Platform
2. Player Settings:
   - Company Name / Product Name
   - Minimum API Level: **21** (Android 5.0)
   - Scripting Backend: **IL2CPP**
   - Target Architecture: ✓ ARM64
3. Connect Android device (USB debugging ON) → Build and Run

---

## Extending the Project

| Feature | Where to add |
|---|---|
| Persistent high scores | `GameManager` → `PlayerPrefs.SetFloat` |
| Save/Load AI Profile | `AITrainer` → `JsonUtility.ToJson` + `PlayerPrefs` |
| Sound effects | `Platform.OnCollisionEnter2D`, `PlayerController.Jump` |
| Particle effects on break | `Platform.BreakPlatform` coroutine |
| Leaderboard | New `LeaderboardManager` script |
| More platform types | Extend `Platform.PlatformType` enum + add case in `Update` |
