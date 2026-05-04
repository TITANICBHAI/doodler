# DoodleClimb — Project Notes

## Architecture
- **Frontend**: Expo / React Native (port 8081) — entire game in `app/(tabs)/index.tsx` (~1250 lines)
- **Backend**: Express + TypeScript (port 5000) — API + static landing page at `server/templates/landing-page.html`
- **Unity**: Complete C# codebase mirror in `UnityProject/Assets/Scripts/`

## Game: `app/(tabs)/index.tsx`
Single-file 60fps game using React Native Views + `requestAnimationFrame`.
All mutable state lives in a `useRef<GS>` game-state object; re-renders triggered via `setT(t=>t+1)`.

### Physics constants
`GRAVITY=1750, JUMP_VEL=-690, SPRING_VEL=-1155, ROCKET_VEL=-1760, DJUMP_VEL=-570`
`JETPACK_VY=-345, PLAYER_SPD=248, FALL_MULT=1.88, MAX_GAP=112`
Player size: `PW=42, PH=50, PLH=14`

### Platform types (`PType`) — 8 types
`"static" | "moving" | "spring" | "breakable" | "crumble" | "golden" | "rocket" | "ice"`
- **ice** 🧊: slippery (amplified horizontal momentum on landing)
- **golden** ✦: score bonus (25 + combo × 5)
- **rocket** 🚀: ultra-high bounce (ROCKET_VEL = -1760)

### Power-up types (`PUType`) — 6 types
`"jetpack" | "shield" | "magnet" | "boots" | "heart" | "star"`
- **star** ⭐: 3.5 s invincibility + rainbow aura + clears all enemies
- **heart** ❤️: extra life (or +30 pts if full)
- **magnet** 🧲: pulls coins AND gems within 180 px

### Enemy types (`EnemyType`) — 4 types
`"bird" | "ghost" | "ufo" | "asteroid"`

### Boss enemy (`Boss` interface)
- Spawns at score 480+; one at a time; 80×80 red orb (👹)
- 3 HP — stomp 3 times to kill; HP bar displayed above
- On kill: +280 pts × combo, drops 3 gems + 5 coins; screen shake
- Touch damage (not stomp): removes shield/life

### Wormhole portals (`Wormhole` interface)
- Spawns at score 850+ (Deep Space zone)
- Enter to teleport up 180-260 px instantly + score bonus
- Animated double-ring with rotating glow (🌀)

### Collectibles
- **Coins** 🪙: clusters + singles; magnet-attracted; streak bonuses (×3, ×5, ×10)
- **Gems** 💎: spawn above 280m; 20 pts × combo; magnet-attracted; purple rotating diamond

### Zone system (4 zones)
| Score | Zone |
|-------|------|
| 0     | ☁ Sunrise |
| 150   | 🌅 Sunset |
| 400   | 🌙 Night Sky |
| 800   | 🚀 Deep Space |

### Key GS fields
`displayScore, windF/windT, coinStreak, starT, bestCombo, gemsCollected, nearMissCooldown, bosses[], wormholes[], bossKills`

### Persistent storage (`localStorage`)
- `dc_best` — all-time best score
- `dc_lb` — top-3 leaderboard
- `dc_bc` — all-time best combo
- `dc_db_YYYY-M-D` — daily best score (resets each calendar day)

### Visual features
Parallax clouds, twinkling stars, aurora borealis, city silhouette, sun/moon/planets,
shooting stars, warp star field, rainbow trail, speed lines, weather (rain/snow),
wind streaks, floating score pop-ups, zone flash, platform pre-glow, squash-and-stretch,
combo fire aura, star rainbow aura, wormhole rings, boss HP bar, gem glow.

### HUD (in-game)
Score · Best · Lives (♥♥♥) · Zone label · Height bar · Power-up timer bars ·
Coin + Gem counter · Wind label · Combo × · Milestone banner · Double-jump / stomp flash

### Game-Over screen
Score · Medal (🥉🥈🥇💎) · NEW BEST · Stats row (Coins / Gems / Combo / Stomped) ·
Boss Kills · All-time Best Combo · Daily Best · Leaderboard top-3

---

## GitHub Integration
- Repo: `github.com/TITANICBHAI/doodler`
- Push via: `bash scripts/push-github.sh` (uses `GITHUB_PERSONAL_ACCESS_TOKEN` secret)
- Auth format: `https://${TOKEN}@github.com/...` (token-only, no username prefix)

## Workflows
- `Start Backend`: `npm run server:dev` (Express port 5000)
- `Start Frontend`: `npm run expo:dev` (Expo port 8081)
- `Push to GitHub`: `bash scripts/push-github.sh`
- `Build Debug APK`: `bash scripts/build-debug-apk.sh` (exports JS bundle; prints APK build instructions)

HMR handles most frontend changes — no restart needed for code edits.

## Unity Mirror
Complete C# scripts in `UnityProject/Assets/Scripts/`:
- `Game/GameManager.cs` — score, lives, zones, spawning
- `Player/PlayerController.cs` — physics, input, power-ups
- `Editor/SceneBuilder.cs` — auto-builds Unity scene from script
