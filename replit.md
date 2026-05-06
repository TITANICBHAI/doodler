# DoodleClimb — Doodle Jump-inspired vertical platformer (Expo + Unity + Express)

## Run & Operate
- `npm run server:dev` — Express backend on port 5000 (uses `npx tsx`)
- `npm run expo:dev` — Expo dev server on port 8081
- `npm run db:push` — push Drizzle schema to Postgres
- `npm run expo:static:build` — production static web build
- `npm run server:build` + `npm run server:prod` — production server

Required env vars: `PORT=5000` (set in `.replit`)
Optional: `DATABASE_URL` (Postgres), `GITHUB_PERSONAL_ACCESS_TOKEN` (GitHub push)

## Stack
- **Frontend**: React Native + Expo v54, Expo Router v6, react-native-web
- **Backend**: Express v5, TypeScript via `npx tsx`, Node 22
- **DB/ORM**: Drizzle ORM + `pg`, Zod validation
- **Unity**: C# 2D game in `UnityProject/` (separate, not built by npm scripts)
- **CI/CD**: GitHub Actions (`build-apk.yml`) via EAS Build — needs `EXPO_TOKEN` GitHub secret

## Where things live
- Game logic: `app/(tabs)/index.tsx` (~1492 lines, single-file 60fps game)
- Express server: `server/index.ts`, `server/routes.ts`, `server/storage.ts`
- DB schema: `shared/schema.ts`
- Unity scripts: `UnityProject/Assets/Scripts/`
- Unity sprites (generated): `UnityProject/Assets/Sprites/` (44 PNGs)
- Unity scene: `UnityProject/Assets/Scenes/MainScene.unity`
- Unity prefabs: `UnityProject/Assets/Prefabs/`
- Sprite generator: `scripts/generate-unity-sprites.js`
- APK workflow: `.github/workflows/build-apk.yml`
- Unity setup guide: `UnityProject/SETUP_GUIDE.md`

## Architecture decisions
- Game state lives in a `useRef<GS>` — never in `useState` — to avoid React re-renders from state mutations; only `setT(t=>t+1)` triggers re-render each frame
- RAF loop uses a stable `loopFn.current` ref pattern so the loop is never cancelled/restarted on phase changes (eliminates frame-drop stutters at game-start/over)
- Express serves both the landing page and acts as a proxy/manifest host for the Expo static build; single port 5000 in production
- Unity project is a parallel implementation with an AI Ghost system — fully independent of the Expo app, shares no code
- Sprites are programmatically generated PNGs (solid/gradient/circle shapes) via `scripts/generate-unity-sprites.js` using only Node built-ins

## Product
- 60fps vertical platformer with 10 platform types, 5 enemy types, bosses, wormholes, 7 power-ups
- 18 achievements, 4 zone themes (Sunrise→Sunset→Night→Space), leaderboard, daily best
- AI Ghost system in Unity: records player movement, trains a clone that mimics your style
- Landing page with Expo Go QR code for mobile play

## User preferences
- GitHub repo: `github.com/TITANICBHAI/doodler`
- Token stored as `GITHUB_PERSONAL_ACCESS_TOKEN` Replit secret
- APK via GitHub Actions EAS Build (needs `EXPO_TOKEN` added as GitHub Actions secret)
- Unity target: Android APK via Unity Hub locally or EAS for Expo version

## Gotchas
- `tsx` must be called via `npx tsx` in npm scripts (not bare `tsx`) — not in PATH by default
- Expo dev server requires `EXPO_PACKAGER_PROXY_URL` and `REPLIT_DEV_DOMAIN` env vars set in the npm script
- Unity project has no `.unitypackage` — open `UnityProject/` folder directly in Unity Hub
- EAS Build APK workflow requires `EXPO_TOKEN` secret in GitHub repo settings (expo.dev → access tokens)
- Particle count is capped at 60 and text pops at 12 to prevent frame drops at high score

## Pointers
- Unity setup: `UnityProject/SETUP_GUIDE.md`
- EAS docs: https://docs.expo.dev/build/introduction/
- Drizzle: `drizzle.config.ts`
