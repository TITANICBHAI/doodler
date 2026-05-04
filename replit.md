# DoodleClimb — Project Notes

## Architecture
- **Frontend**: Expo / React Native (port 8081) — entire game in `app/(tabs)/index.tsx` (~1355 lines)
- **Backend**: Express + TypeScript (port 5000) — API + static landing page at `server/templates/landing-page.html`
- **Unity**: Complete C# codebase mirror in `UnityProject/Assets/Scripts/`

## Game: `app/(tabs)/index.tsx`
Single-file 60fps game using React Native Views + `requestAnimationFrame`.
All mutable state lives in a `useRef<GS>` game-state object; re-renders triggered via `setT(t=>t+1)`.

### Physics constants
`GRAVITY=1750, JUMP_VEL=-690, SPRING_VEL=-1155, ROCKET_VEL=-1760, DJUMP_VEL=-570`
`JETPACK_VY=-345, PLAYER_SPD=248, FALL_MULT=1.88, MAX_GAP=112`
Player size: `PW=42, PH=50, PLH=14`

### Platform types (`PType`) — 9 types
`"static" | "moving" | "spring" | "breakable" | "crumble" | "golden" | "rocket" | "ice" | "bomb"`
- **ice** 🧊: slippery (amplified horizontal momentum on landing; tracks `iceHits`)
- **golden** ✦: score bonus (25 + combo × 5)
- **rocket** 🚀: ultra-high bounce (ROCKET_VEL = -1760)
- **bomb** 💣: explodes on landing — SPRING_VEL × 1.18 + particles + 💣 BOOM! pop; unlocks "Bomb Rider" achievement

### Power-up types (`PUType`) — 6 types
`"jetpack" | "shield" | "magnet" | "boots" | "heart" | "star"`
- **star** ⭐: 3.5 s invincibility + rainbow aura + clears all enemies
- **heart** ❤️: extra life (or +30 pts if full)
- **magnet** 🧲: pulls coins AND gems within 180 px

### Enemy types (`EnemyType`) — 5 types
`"bird" | "ghost" | "ufo" | "asteroid" | "bat"`
- **bat** 🦇: horizontally homes toward player's X; sine-wave vertical drift; spawns at 200+; stays on-screen (not cleaned up by X boundary)

### Boss enemy (`Boss` interface)
- Spawns at score 480+; one at a time; 80×80 red orb (👹)
- 3 HP — stomp 3 times to kill; HP bar displayed above
- On kill: +280 pts × combo, drops 3 gems + 5 coins; screen shake
- Touch damage (not stomp): removes shield/life

### Wormhole portals (`Wormhole` interface)
- Spawns at score 850+ (Deep Space zone)
- Enter to teleport up 180-260 px instantly + score bonus; sets `wormholeUsed=true`
- Animated double-ring with rotating glow (🌀)

### Achievement system (10 achievements)
Persisted to `dc_ach` in localStorage (JSON array of IDs).
| ID | Icon | Title | Condition |
|----|------|-------|-----------|
| `first_100` | 🏔 | Summit | Reach 100m |
| `night` | 🌙 | Night Climber | Reach 400m |
| `space` | 🚀 | Deep Space | Reach 800m |
| `gem_5` | 💎 | Gem Hoarder | Collect 5 gems in one run |
| `boss_slayer` | 👹 | Boss Slayer | Defeat a boss |
| `combo_king` | ⚡ | Combo King | Reach x10 combo |
| `wormhole` | 🌀 | Wormhole Rider | Use a wormhole |
| `coin_20` | 🪙 | Coin Collector | Collect 20 coins |
| `stomp_10` | 💀 | Stomper | Stomp 10 enemies |
| `bomb_rider` | 💣 | Bomb Rider | Land on a bomb platform |

When first unlocked: in-game toast pop-up appears for 3 s (bottom area, purple pill).
Game-over screen shows all achievements unlocked that run.

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
`displayScore, windF/windT, coinStreak, starT, bestCombo, gemsCollected, nearMissCooldown`
`bosses[], wormholes[], bossKills, iceHits, wormholeUsed, bombRidden, achNewRun[], achPopT, achPopText`

### Persistent storage (`localStorage`)
- `dc_best` — all-time best score
- `dc_lb` — top-3 leaderboard
- `dc_bc` — all-time best combo
- `dc_db_YYYY-M-D` — daily best score (resets each calendar day)
- `dc_ach` — JSON array of all-time unlocked achievement IDs

### Visual features
Parallax clouds, twinkling stars, aurora borealis, city silhouette, sun/moon/planets,
shooting stars, warp star field, rainbow trail, speed lines, weather (rain/snow),
wind streaks, floating score pop-ups, zone flash, platform pre-glow, squash-and-stretch,
combo fire aura, star rainbow aura, wormhole rings, boss HP bar, gem glow,
bat flapping animation, bomb explosion particles, achievement toast pop-up.

### HUD (in-game)
Score · Best · Lives (♥♥♥) · Zone label · Height bar · Power-up timer bars ·
Coin + Gem counter · Wind label · Combo × · Milestone banner · Double-jump / stomp flash ·
Achievement pop-up toast (3 s, purple pill, bottom of screen)

### Game-Over screen
Score · Medal (🥉🥈🥇💎) · NEW BEST · Stats row (Coins / Gems / Combo / Stomped) ·
Boss Kills · All-time Best Combo · Daily Best · Achievements Unlocked This Run · Leaderboard top-3

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

## Unity Mirror (`UnityProject/Assets/Scripts/`)
- `Game/GameManager.cs` — score, boss kills, gems, daily best tracking
- `Game/BossController.cs` — 3-HP boss patrol + dive AI, loot drop
- `Game/WormholePortal.cs` — teleport trigger with visual rings
- `Game/GemPickup.cs` — rotating gem, magnet pull, score reward
- `Game/DailyBestTracker.cs` — PlayerPrefs daily best (matches Expo localStorage key format)
- `Game/VisualEffects.cs` — particle systems
- `Player/PlayerController.cs` — physics, input, power-ups (shield, magnet, star, lives, `TakeContactDamage`)
