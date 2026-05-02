# DoodleClimb — Project Notes

## Architecture
- **Frontend**: Expo / React Native (port 8081) — entire game lives in `app/(tabs)/index.tsx`
- **Backend**: Express + TypeScript (port 5000) — serves API + static landing page at `server/templates/landing-page.html`
- **Unity**: Complete C# codebase mirror in `UnityProject/Assets/Scripts/`

## Game: `app/(tabs)/index.tsx` (~1013 lines)
Single-file 60fps canvas-less game using React Native Views + `requestAnimationFrame`.
All mutable state lives in a `useRef<GS>` game-state object; re-renders triggered via `setT(t=>t+1)`.

### Physics constants
`GRAVITY=1750, JUMP_VEL=-690, SPRING_VEL=-1155, DJUMP_VEL=-570, JETPACK_VY=-345, PLAYER_SPD=248, FALL_MULT=1.88`
Player size: `PW=42, PH=50, PLH=14`

### Platform types (`PType`)
`"static" | "moving" | "spring" | "breakable" | "crumble"`
- **crumble**: shakes for 0.52 s (orange tint, ⚠ icons) then breaks with particle burst

### Power-up types (`PUType`)
`"jetpack" | "shield" | "magnet" | "boots"`
- **boots** (🥾): 5 s of boosted jumps (SPRING_VEL × 0.82); blinking HUD bar when < 1 s left

### Enemy types (`EnemyType`)
`"bird" | "ghost" | "ufo" | "asteroid"`
Off-screen enemies show blinking edge arrows (◀ ▶) as warning indicators.

### Zone system (4 zones)
| Zone | Score | Label |
|------|-------|-------|
| 0 | 0 | ☁ Sunrise |
| 1 | 150 | 🌅 Sunset |
| 2 | 400 | 🌙 Night Sky |
| 3 | 800 | 🚀 Deep Space |

Zone transitions trigger a full-screen tinted flash (0.75 s).

### Key GS fields added across rounds
- `displayScore` — smoothly animated score counter (approaches `score` at 10× gap/s)
- `windF / windT / windNextT` — episodic wind gusts in Night/Space zones (score > 350)
- `bootsT` — bounce boots power-up timer
- `coinStreak / coinStreakT` — consecutive coin bonuses (×3 +5, ×5 +12, ×10 +28)
- `weatherParts: WeatherP[]` — screen-space rain (Sunrise/Sunset) and snow (Night)

### Persistent storage (`localStorage`)
- `dc_best` — all-time best score
- `dc_lb` — top-3 leaderboard (JSON array of numbers)

### Visual features (cumulative)
Parallax clouds, twinkling stars, aurora borealis, city silhouette with lit windows,
sun (Sunrise + Sunset), crescent moon, planets with rings, shooting stars,
warp star field (Deep Space), rainbow trail (combo ≥ 10), speed lines (fast fall),
weather particles (rain / snow), wind streaks, floating score pop-ups, zone flash,
platform pre-glow, ground decoration, combo burst particles, squash-and-stretch.

### HUD
Score (animated) · Best · Lives (♥♥♥) · Zone label · Height progress bar ·
Power-up timer bars (blink when expiring) · Coin counter · Wind direction label ·
Combo multiplier · Milestone banner · Double-jump / stomp flash texts

---

## GitHub Integration — NOT CONNECTED
The user dismissed the GitHub OAuth flow twice. The GitHub connector ID is:
`connector:ccfg_github_01K4B9XD3VRVD2F99YM91YTCAF`

To create a repo named **"doodler"** on the user's GitHub account, one of the following
is required:
1. User completes the Replit GitHub OAuth flow (preferred — use `proposeIntegration` again)
2. User provides a **GitHub Personal Access Token** (PAT) with `repo` scope as a secret
   — store it as `GITHUB_TOKEN` via the environment-secrets skill, then use the
   GitHub REST API: `POST https://api.github.com/user/repos`

## Workflows
- `Start Backend`: `npm run server:dev` (Express on port 5000)
- `Start Frontend`: `npm run expo:dev` (Expo on port 8081)
HMR handles most frontend changes — no restart needed for code edits.

## Unity Mirror
Complete C# scripts in `UnityProject/Assets/Scripts/`:
- `Game/GameManager.cs` — score, lives, zones, spawning
- `Player/PlayerController.cs` — physics, input, power-ups
- `Editor/SceneBuilder.cs` — auto-builds Unity scene from script
