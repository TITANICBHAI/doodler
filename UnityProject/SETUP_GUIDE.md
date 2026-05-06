# DoodleClimb Unity — Setup Guide

## Prerequisites
- Unity Hub with Unity **2022.3 LTS** (or newer 2D-capable version)
- Android Build Support module installed via Unity Hub
- TextMeshPro package

---

## Step 1 — Open the Project
1. Open **Unity Hub**
2. Click **Add → Add project from disk**
3. Select the `UnityProject/` folder
4. Open with Unity 2022.3 LTS (2D Core template)

## Step 2 — Install Packages
In Unity: **Window → Package Manager**
- Install **TextMeshPro** (import TMP Essentials when prompted)
- Install **2D Sprite** (usually pre-installed with 2D template)

## Step 3 — Import Sprites
The sprites are in `Assets/Sprites/` — Unity will auto-import them.
For each sprite:
1. Select it in the Project panel
2. Set **Texture Type → Sprite (2D and UI)**
3. Set **Pixels Per Unit → 100**
4. Click **Apply**

### Sprite → GameObject mapping
| Sprite file           | Used by                    |
|-----------------------|---------------------------|
| `player.png`          | Player prefab Visual       |
| `platform_static.png` | Platform_Static prefab     |
| `platform_moving.png` | Platform_Moving prefab     |
| `platform_spring.png` | Platform_Spring prefab     |
| `platform_breakable.png` | Platform_Breakable prefab |
| `platform_crumble.png` | Platform_Crumble prefab   |
| `platform_golden.png` | Platform_Golden prefab     |
| `platform_rocket.png` | Platform_Rocket prefab     |
| `platform_ice.png`    | Platform_Ice prefab        |
| `platform_bomb.png`   | Platform_Bomb prefab       |
| `platform_conveyor.png` | Platform_Conveyor prefab |
| `enemy_bird.png`      | Enemy_Bird prefab          |
| `enemy_ghost.png`     | Enemy_Ghost prefab         |
| `enemy_ufo.png`       | Enemy_UFO prefab           |
| `enemy_bat.png`       | Enemy_Bat prefab           |
| `enemy_asteroid.png`  | Enemy_Asteroid prefab      |
| `boss.png`            | Boss prefab                |
| `coin.png`            | Coin pickup prefab         |
| `gem.png`             | Gem pickup prefab          |
| `wormhole.png`        | WormholePortal prefab      |
| `powerup_*.png`       | PowerUp prefabs            |
| `particle_*.png`      | Particle systems           |
| `background_*.png`    | Background scrollers       |
| `cloud.png`           | Cloud spawner              |
| `planet.png`          | Planet spawner             |

## Step 4 — Wire Up the Scene
Open `Assets/Scenes/MainScene.unity`

Assign references in the **GameManager** Inspector:
- **Player** → drag the Player GameObject
- **AI Player** → drag the AIPlayer GameObject
- **Platform Spawner** → drag PlatformSpawner
- **Enemy Spawner** → drag EnemySpawner
- **Camera Follow** → drag Main Camera
- **UI Manager** → drag UIManager Canvas

## Step 5 — Configure Platform Spawner
Select PlatformSpawner in the Hierarchy:
- Drag each Platform prefab into the matching slot
- Set **Initial Platform Count → 12**
- Set **Spawn Distance → 3**

## Step 6 — Android Build
1. **File → Build Settings**
2. Switch Platform to **Android**
3. **Player Settings → Company Name / Package Name**
   - Example: `com.yourname.doodleclimb`
4. Enable **Development Build** for debug APK
5. Click **Build** — choose an output folder
6. Unity produces a `.apk` file ready to install on Android

## Step 7 — EAS Cloud APK (React Native version)
For the Expo/React Native version, the GitHub Actions workflow at
`.github/workflows/build-apk.yml` auto-builds an APK on every push to `main`.

**Required GitHub Secret:**
- Go to your GitHub repo → Settings → Secrets → Actions
- Add secret: `EXPO_TOKEN`
- Get it from: https://expo.dev/accounts/[your-username]/settings/access-tokens

The APK will be available as a downloadable artifact after the workflow completes.
