import React, { useRef, useState, useEffect, useCallback } from "react";
import {
  View,
  Text,
  StyleSheet,
  Dimensions,
  Pressable,
  Platform,
} from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { StatusBar } from "expo-status-bar";

// ─── Screen dimensions ────────────────────────────────────────────────────────
const { width: SW, height: SH } = Dimensions.get("window");

// ─── Physics constants ────────────────────────────────────────────────────────
const GRAVITY      = 1750;   // px / s²
const JUMP_VEL     = -680;   // px / s  (negative = upward)
const SPRING_VEL   = -1140;  // px / s  (spring platform)
const PLAYER_SPD   = 245;    // px / s  horizontal
const FALL_MULT    = 1.85;   // extra gravity while falling

// ─── Player geometry ──────────────────────────────────────────────────────────
const PW = 42;   // player width
const PH = 50;   // player height

// ─── Platform geometry ────────────────────────────────────────────────────────
const PLH = 14;  // platform height

// ─── Camera ───────────────────────────────────────────────────────────────────
const CAM_LEAD    = 0.38;   // player sits at this fraction from top
const DEATH_OFF   = 300;    // px below camera before death

// ─── Misc ─────────────────────────────────────────────────────────────────────
const MAX_PLATS   = 24;
const TRAIL_LEN   = 7;

// ─── Types ────────────────────────────────────────────────────────────────────
type PType = "static" | "moving" | "spring" | "breakable";
type Phase = "menu" | "play" | "dead";

interface Plat {
  id: number;
  x: number; y: number; w: number;
  type: PType;
  broken: boolean;
  ox: number;          // origin x for moving platforms
  dir: number;         // oscillation direction seed
  range: number;       // oscillation half-width
}

interface Particle {
  x: number; y: number;
  vx: number; vy: number;
  life: number;        // 1 → 0
  color: string;
  r: number;
}

interface TrailPt { x: number; y: number; }

interface GS {
  // player
  px: number; py: number;
  pvx: number; pvy: number;
  psx: number; psy: number;   // squash-x / squash-y
  facing: number;             // 1 = right, -1 = left

  // camera
  scrollY: number;            // world-Y at top of screen
  minY: number;               // highest world-Y reached (lowest value)
  startY: number;

  // scores
  score: number;
  best: number;
  needsSaveBest: boolean;

  // combo
  combo: number;
  comboTimer: number;

  // entities
  plats: Plat[];
  parts: Particle[];
  trail: TrailPt[];

  // game state
  phase: Phase;
  input: number;              // -1 | 0 | 1

  // screen shake
  shakeX: number;
  shakeY: number;
  shakeT: number;

  // id counter
  pid: number;
}

// ─── Colours ──────────────────────────────────────────────────────────────────
const BG        = "#F5F0E8";
const LINE_COL  = "#A0C8E8";
const MARGIN    = "#E88888";
const P_GREEN   = "#27C063";
const P_DARK    = "#1E9E50";

const PLAT_COL: Record<PType, string> = {
  static:    "#2DC968",
  moving:    "#4EAAF5",
  spring:    "#F5D123",
  breakable: "#F05232",
};

const PLAT_GLOW: Record<PType, string> = {
  static:    "#1AA850",
  moving:    "#2D88D8",
  spring:    "#D4A800",
  breakable: "#C83018",
};

// ─── Platform factory ─────────────────────────────────────────────────────────
function pickType(score: number): PType {
  const r = Math.random();
  if (score < 80)  return r < 0.06 ? "moving" : "static";
  if (score < 200) return r < 0.08 ? "spring" : r < 0.24 ? "moving" : r < 0.36 ? "breakable" : "static";
  if (score < 500) return r < 0.11 ? "spring" : r < 0.28 ? "moving" : r < 0.46 ? "breakable" : "static";
  return r < 0.13 ? "spring" : r < 0.30 ? "moving" : r < 0.52 ? "breakable" : "static";
}

function mkPlat(gs: GS, y: number, score: number): Plat {
  const w  = Math.max(50, 90 - score * 0.018);
  const x  = Math.random() * (SW - w);
  const tp = pickType(score);
  return { id: gs.pid++, x, y, w, type: tp, broken: false, ox: x, dir: Math.random() < 0.5 ? 1 : -1, range: 38 + Math.random() * 55 };
}

function mkStatPlat(gs: GS, y: number, x: number, w: number): Plat {
  return { id: gs.pid++, x, y, w, type: "static", broken: false, ox: x, dir: 1, range: 0 };
}

// ─── Initial game state ───────────────────────────────────────────────────────
function mkGS(best: number): GS {
  const startY = SH * 0.70;
  const gs: GS = {
    px: SW / 2 - PW / 2, py: startY, pvx: 0, pvy: JUMP_VEL,
    psx: 1, psy: 1, facing: 1,
    scrollY: 0, minY: startY, startY,
    score: 0, best, needsSaveBest: false,
    combo: 0, comboTimer: 0,
    plats: [], parts: [], trail: [],
    phase: "menu", input: 0,
    shakeX: 0, shakeY: 0, shakeT: 0,
    pid: 0,
  };

  // guaranteed wide platform right below player
  gs.plats.push(mkStatPlat(gs, startY + PH, SW / 2 - 52, 104));

  // easy starter platforms, biased toward centre
  let y = startY;
  for (let i = 0; i < 6; i++) {
    y -= 60 + Math.random() * 28;
    const w = 78 + Math.random() * 22;
    const cx = SW * 0.2 + Math.random() * SW * 0.6;
    gs.plats.push(mkStatPlat(gs, y, cx - w / 2, w));
  }

  // rest
  for (let i = 0; i < 18; i++) {
    y -= 70 + Math.random() * 45;
    gs.plats.push(mkPlat(gs, y, 0));
  }

  return gs;
}

// ─── Particles ────────────────────────────────────────────────────────────────
function emit(gs: GS, cx: number, cy: number, col: string, n: number) {
  for (let i = 0; i < n; i++) {
    const ang = (Math.PI * 2 * i) / n + Math.random() * 0.6;
    const spd = 70 + Math.random() * 180;
    gs.parts.push({
      x: cx, y: cy, color: col,
      vx: Math.cos(ang) * spd,
      vy: Math.sin(ang) * spd - 80,
      life: 1,
      r: 3 + Math.random() * 5,
    });
  }
}

// ─── Main update ─────────────────────────────────────────────────────────────
function update(gs: GS, dt: number, now: number) {
  if (gs.phase !== "play") return;

  // horizontal
  gs.pvx = gs.input * PLAYER_SPD;
  if (gs.input !== 0) gs.facing = gs.input;

  // gravity
  const gm = gs.pvy > 0 ? FALL_MULT : 1;
  gs.pvy += GRAVITY * gm * dt;
  gs.pvy = Math.min(gs.pvy, 1400); // terminal velocity

  // move
  gs.px += gs.pvx * dt;
  gs.py += gs.pvy * dt;

  // horizontal wrap
  if (gs.px + PW < 0) gs.px = SW;
  if (gs.px > SW)     gs.px = -PW;

  // trail
  if (
    gs.trail.length === 0 ||
    Math.hypot(gs.px - gs.trail[0].x, gs.py - gs.trail[0].y) > 5
  ) {
    gs.trail.unshift({ x: gs.px + PW / 2, y: gs.py + PH / 2 });
    if (gs.trail.length > TRAIL_LEN) gs.trail.pop();
  }

  // platform collision (only when falling)
  if (gs.pvy > 0) {
    for (const p of gs.plats) {
      if (p.broken) continue;
      const feet = gs.py + PH;
      if (
        feet >= p.y &&
        feet <= p.y + PLH + 14 &&
        gs.px + PW > p.x + 5 &&
        gs.px < p.x + p.w - 5
      ) {
        // land
        gs.py  = p.y - PH;
        const spring = p.type === "spring";
        gs.pvy = spring ? SPRING_VEL : JUMP_VEL;

        // squash → lerp stretches back naturally
        gs.psx = spring ? 1.55 : 1.38;
        gs.psy = spring ? 0.52 : 0.65;

        // combo
        gs.combo++;
        gs.comboTimer = 2.5;

        // particles
        const cx = p.x + p.w / 2;
        emit(gs, cx, p.y, PLAT_COL[p.type], spring ? 14 : 6);

        // breakable
        if (p.type === "breakable") p.broken = true;

        break;
      }
    }
  }

  // camera
  const ideal = gs.py - SH * CAM_LEAD;
  if (ideal < gs.scrollY) gs.scrollY = ideal;

  // score
  if (gs.py < gs.minY) gs.minY = gs.py;
  gs.score = Math.floor((gs.startY - gs.minY) / 5);
  if (gs.score > gs.best) {
    gs.best = gs.score;
    gs.needsSaveBest = true;
  }

  // generate platforms above
  const topGenY = gs.scrollY - SH * 0.55;
  let topY = gs.plats.length > 0 ? Math.min(...gs.plats.map((p) => p.y)) : gs.startY;
  let safety = 0;
  while (topY > topGenY && safety < 25) {
    const gap = 72 + Math.random() * 48 + Math.min(gs.score * 0.05, 35);
    topY -= gap;
    gs.plats.push(mkPlat(gs, topY, gs.score));
    safety++;
  }

  // remove far-below platforms
  const cutY = gs.scrollY + SH + 250;
  gs.plats = gs.plats.filter((p) => p.y < cutY);

  // update moving platforms
  for (const p of gs.plats) {
    if (p.type === "moving") {
      p.x = p.ox + Math.sin(now / 1000 * 1.6 + p.id * 1.3) * p.range;
    }
  }

  // update particles
  for (const pt of gs.parts) {
    pt.x   += pt.vx * dt;
    pt.y   += pt.vy * dt;
    pt.vy  += 580 * dt;
    pt.life -= dt * 2.2;
  }
  gs.parts = gs.parts.filter((p) => p.life > 0);

  // squash lerp
  const sr = 1 - Math.exp(-13 * dt);
  gs.psx += (1 - gs.psx) * sr;
  gs.psy += (1 - gs.psy) * sr;

  // screen shake
  if (gs.shakeT > 0) {
    gs.shakeT -= dt;
    const s = gs.shakeT * 9;
    gs.shakeX = (Math.random() - 0.5) * s;
    gs.shakeY = (Math.random() - 0.5) * s;
  } else {
    gs.shakeX = 0; gs.shakeY = 0;
  }

  // combo timer
  if (gs.comboTimer > 0) {
    gs.comboTimer -= dt;
    if (gs.comboTimer <= 0) gs.combo = 0;
  }

  // death
  if (gs.py > gs.scrollY + SH + DEATH_OFF) {
    gs.phase = "dead";
    gs.shakeT = 0.6;
    emit(gs, gs.px + PW / 2, gs.py, P_GREEN, 22);
  }
}

// ─── Background notebook lines ────────────────────────────────────────────────
const LINE_SPACING = 44;
const LINE_COUNT   = Math.ceil(SH / LINE_SPACING) + 3;

function Background({ scrollY }: { scrollY: number }) {
  const offset = ((scrollY * 0.18) % LINE_SPACING + LINE_SPACING) % LINE_SPACING;
  return (
    <View style={StyleSheet.absoluteFillObject} pointerEvents="none">
      <View style={s.bgBase} />
      {Array.from({ length: LINE_COUNT }, (_, i) => (
        <View key={i} style={[s.bgLine, { top: i * LINE_SPACING - LINE_SPACING + offset }]} />
      ))}
      <View style={s.bgMargin} />
      {/* faint dot grid for depth */}
      {Array.from({ length: 8 }, (_, row) =>
        Array.from({ length: 5 }, (_, col) => (
          <View
            key={`d-${row}-${col}`}
            style={[
              s.bgDot,
              {
                top:  ((row * 110 - scrollY * 0.04) % (SH + 110) + SH + 110) % (SH + 110) - 55,
                left: col * (SW / 4) + 30,
              },
            ]}
          />
        ))
      )}
    </View>
  );
}

// ─── Platform view ────────────────────────────────────────────────────────────
const PlatView = React.memo(function PlatView({ p }: { p: Plat }) {
  const broken = p.broken;
  const bg     = broken ? "#999" : PLAT_COL[p.type];
  const border = broken ? "#777" : PLAT_GLOW[p.type];
  return (
    <View
      style={[
        s.plat,
        {
          left: p.x,
          top:  p.y,
          width: p.w,
          backgroundColor: bg,
          borderColor: border,
          opacity: broken ? 0.45 : 1,
        },
      ]}
    >
      {/* highlight stripe */}
      <View style={s.platShine} />
      {p.type === "spring" && !broken && (
        <View style={s.springCoil}>
          <View style={s.springLine} />
          <View style={s.springLine} />
          <View style={s.springLine} />
        </View>
      )}
    </View>
  );
});

// ─── Player view ──────────────────────────────────────────────────────────────
function PlayerView({ g }: { g: GS }) {
  const flip = g.facing < 0 ? -1 : 1;
  return (
    <View
      style={[
        s.player,
        {
          left:      g.px,
          top:       g.py,
          transform: [{ scaleX: g.psx * flip }, { scaleY: g.psy }],
        },
      ]}
    >
      {/* body glow */}
      <View style={s.playerGlow} />

      {/* eyes row */}
      <View style={s.eyeRow}>
        <View style={s.eye}>
          <View style={s.pupil} />
          <View style={s.eyeShine} />
        </View>
        <View style={s.eye}>
          <View style={s.pupil} />
          <View style={s.eyeShine} />
        </View>
      </View>

      {/* mouth */}
      <View style={s.mouth} />

      {/* feet / legs */}
      <View style={s.feet}>
        <View style={s.foot} />
        <View style={s.foot} />
      </View>
    </View>
  );
}

// ─── Combo colours ────────────────────────────────────────────────────────────
function comboColor(n: number) {
  if (n >= 12) return "#FF4422";
  if (n >= 7)  return "#F5D123";
  return "#50F0AA";
}

// ─── Main component ───────────────────────────────────────────────────────────
export default function GameScreen() {
  const insets   = useSafeAreaInsets();
  const gs       = useRef<GS>(mkGS(0));
  const [, setT] = useState(0);
  const rafRef   = useRef<number>(0);
  const prevTs   = useRef<number>(0);
  const [phase, setPhase] = useState<Phase>("menu");

  // ── Game loop ──────────────────────────────────────────────────────────────
  const loop = useCallback((ts: number) => {
    if (prevTs.current === 0) prevTs.current = ts;
    const dt = Math.min((ts - prevTs.current) / 1000, 0.033);
    prevTs.current = ts;

    update(gs.current, dt, ts);

    if (gs.current.phase !== phase) {
      setPhase(gs.current.phase);
    }

    setT((t) => t + 1);
    rafRef.current = requestAnimationFrame(loop);
  }, [phase]);

  useEffect(() => {
    rafRef.current = requestAnimationFrame(loop);
    return () => cancelAnimationFrame(rafRef.current);
  }, [loop]);

  // ── Keyboard (web) ─────────────────────────────────────────────────────────
  useEffect(() => {
    if (Platform.OS !== "web") return;
    const down = (e: KeyboardEvent) => {
      if (e.key === "ArrowLeft"  || e.key === "a") gs.current.input = -1;
      if (e.key === "ArrowRight" || e.key === "d") gs.current.input =  1;
      if (e.key === " " && gs.current.phase === "menu")  startGame();
      if (e.key === " " && gs.current.phase === "dead")  startGame();
    };
    const up = () => { gs.current.input = 0; };
    window.addEventListener("keydown", down);
    window.addEventListener("keyup",   up);
    return () => {
      window.removeEventListener("keydown", down);
      window.removeEventListener("keyup",   up);
    };
  }, []);

  // ── Start / restart ────────────────────────────────────────────────────────
  const startGame = useCallback(() => {
    const best = gs.current.best;
    gs.current  = mkGS(best);
    gs.current.phase = "play";
    prevTs.current   = 0;
    setPhase("play");
  }, []);

  // ── Touch input ────────────────────────────────────────────────────────────
  const onTouchStart = useCallback((e: any) => {
    if (gs.current.phase !== "play") return;
    gs.current.input = e.nativeEvent.locationX < SW / 2 ? -1 : 1;
  }, []);
  const onTouchMove = useCallback((e: any) => {
    if (gs.current.phase !== "play") return;
    gs.current.input = e.nativeEvent.locationX < SW / 2 ? -1 : 1;
  }, []);
  const onTouchEnd = useCallback(() => {
    gs.current.input = 0;
  }, []);

  const g   = gs.current;
  const webTop = Platform.OS === "web" ? 67 : 0;

  // ── Render ─────────────────────────────────────────────────────────────────
  return (
    <View style={s.root}>
      <StatusBar hidden />

      {/* Notebook paper background */}
      <Background scrollY={g.scrollY} />

      {/* Full-screen touch layer */}
      <View
        style={StyleSheet.absoluteFillObject}
        onTouchStart={onTouchStart}
        onTouchMove={onTouchMove}
        onTouchEnd={onTouchEnd}
      >
        {/* ── World (camera-shifted) ────────────────────────────────────── */}
        <View
          style={[
            StyleSheet.absoluteFillObject,
            {
              transform: [
                { translateX: g.shakeX },
                { translateY: -g.scrollY + g.shakeY },
              ],
            },
          ]}
          pointerEvents="none"
        >
          {/* Platforms */}
          {g.plats.map((p) => <PlatView key={p.id} p={p} />)}

          {/* Trail */}
          {g.trail.map((t, i) => (
            <View
              key={i}
              style={{
                position: "absolute",
                left:         t.x - PW * 0.22,
                top:          t.y - PH * 0.22,
                width:        PW * 0.44,
                height:       PH * 0.44,
                borderRadius: PW * 0.22,
                backgroundColor: P_GREEN,
                opacity: (1 - i / TRAIL_LEN) * 0.32,
              }}
            />
          ))}

          {/* Particles */}
          {g.parts.map((p, i) => (
            <View
              key={i}
              style={{
                position: "absolute",
                left:   p.x - p.r,
                top:    p.y - p.r,
                width:  p.r * 2,
                height: p.r * 2,
                borderRadius: p.r,
                backgroundColor: p.color,
                opacity: Math.max(0, p.life),
              }}
            />
          ))}

          {/* Player */}
          <PlayerView g={g} />
        </View>

        {/* ── HUD ───────────────────────────────────────────────────────── */}
        <View
          style={[s.hud, { paddingTop: insets.top + webTop + 8 }]}
          pointerEvents="none"
        >
          <Text style={s.scoreNum}>{g.score}</Text>
          {g.best > 0 && (
            <Text style={s.bestLabel}>BEST  {g.best}</Text>
          )}
        </View>

        {/* ── Combo ────────────────────────────────────────────────────── */}
        {phase === "play" && g.comboTimer > 0 && g.combo >= 3 && (
          <View style={s.comboWrap} pointerEvents="none">
            <Text style={[s.comboText, { color: comboColor(g.combo) }]}>
              {g.combo >= 12 ? `🔥 x${g.combo}` : g.combo >= 7 ? `⚡ x${g.combo}` : `x${g.combo}`}
            </Text>
          </View>
        )}

        {/* ── Touch hints (playing) ──────────────────────────────────── */}
        {phase === "play" && g.score === 0 && (
          <View style={s.hintWrap} pointerEvents="none">
            <Text style={s.hintArrow}>← tap left · tap right →</Text>
          </View>
        )}

        {/* ── Menu ─────────────────────────────────────────────────────── */}
        {phase === "menu" && (
          <View style={s.overlay}>
            <View style={s.titleCard}>
              <Text style={s.titleLine1}>DOODLE</Text>
              <Text style={s.titleLine2}>CLIMB</Text>
              <View style={s.titleDeco} />
            </View>
            {g.best > 0 && (
              <Text style={s.overlayBest}>BEST  {g.best}</Text>
            )}
            <Pressable onPress={startGame} style={({ pressed }) => [s.btn, pressed && s.btnPressed]}>
              <Text style={s.btnText}>TAP TO PLAY</Text>
            </Pressable>
            <Text style={s.overlayHint}>← left half · right half →</Text>
            <Text style={s.overlayHint}>or  A / D  keys  on  web</Text>
          </View>
        )}

        {/* ── Game Over ─────────────────────────────────────────────────── */}
        {phase === "dead" && (
          <View style={s.overlay}>
            <Text style={s.goLabel}>SCORE</Text>
            <Text style={s.goScore}>{g.score}</Text>
            {g.score > 0 && g.score >= g.best && (
              <Text style={s.newBest}>✦ NEW BEST ✦</Text>
            )}
            {g.best > g.score && (
              <Text style={s.overlayBest}>BEST  {g.best}</Text>
            )}
            <Pressable onPress={startGame} style={({ pressed }) => [s.btn, pressed && s.btnPressed]}>
              <Text style={s.btnText}>TRY AGAIN</Text>
            </Pressable>
          </View>
        )}
      </View>
    </View>
  );
}

// ─── Styles ───────────────────────────────────────────────────────────────────
const s = StyleSheet.create({
  root: { flex: 1, backgroundColor: BG },

  // background
  bgBase:   { ...StyleSheet.absoluteFillObject, backgroundColor: BG },
  bgLine: {
    position: "absolute", left: 0, right: 0,
    height: 1.5,
    backgroundColor: LINE_COL,
    opacity: 0.32,
  },
  bgMargin: {
    position: "absolute",
    left: 40, top: 0, bottom: 0,
    width: 1.5,
    backgroundColor: MARGIN,
    opacity: 0.24,
  },
  bgDot: {
    position: "absolute",
    width: 4, height: 4,
    borderRadius: 2,
    backgroundColor: LINE_COL,
    opacity: 0.18,
  },

  // platforms
  plat: {
    position: "absolute",
    height: PLH,
    borderRadius: 8,
    borderBottomWidth: 3,
    overflow: "hidden",
    justifyContent: "center",
  },
  platShine: {
    position: "absolute",
    top: 0, left: 6, right: 6,
    height: 4,
    borderRadius: 3,
    backgroundColor: "rgba(255,255,255,0.40)",
  },
  springCoil: {
    position: "absolute",
    top: 2, left: "25%", right: "25%",
    gap: 2,
  },
  springLine: {
    height: 2,
    borderRadius: 1,
    backgroundColor: "rgba(255,255,255,0.55)",
    marginBottom: 1,
  },

  // player
  player: {
    position: "absolute",
    width:  PW, height: PH,
    backgroundColor: P_GREEN,
    borderRadius: 12,
    alignItems: "center",
    paddingTop: 8,
    borderBottomWidth: 3,
    borderColor: P_DARK,
    overflow: "hidden",
  },
  playerGlow: {
    position: "absolute",
    top: -4, left: -4, right: -4, bottom: -4,
    borderRadius: 16,
    backgroundColor: P_GREEN,
    opacity: 0.18,
  },
  eyeRow: { flexDirection: "row", gap: 8 },
  eye: {
    width: 12, height: 12,
    borderRadius: 6,
    backgroundColor: "white",
    justifyContent: "center",
    alignItems: "center",
  },
  pupil: {
    width: 5, height: 5,
    borderRadius: 2.5,
    backgroundColor: "#0A0A1A",
    marginTop: 2,
  },
  eyeShine: {
    position: "absolute",
    top: 1, right: 1,
    width: 3, height: 3,
    borderRadius: 1.5,
    backgroundColor: "white",
  },
  mouth: {
    marginTop: 5,
    width: 14, height: 5,
    borderRadius: 4,
    borderBottomWidth: 2,
    borderLeftWidth:   1.5,
    borderRightWidth:  1.5,
    borderColor: "rgba(255,255,255,0.7)",
    borderTopWidth: 0,
  },
  feet: { flexDirection: "row", gap: 8, marginTop: 3 },
  foot: {
    width: 7, height: 5,
    borderRadius: 3,
    backgroundColor: P_DARK,
  },

  // HUD
  hud: {
    position: "absolute",
    top: 0, left: 0, right: 0,
    alignItems: "center",
  },
  scoreNum: {
    fontSize: 58,
    fontWeight: "900",
    color: "#1A1A2A",
    letterSpacing: -2,
    textShadowColor: "rgba(39,192,99,0.35)",
    textShadowOffset: { width: 0, height: 2 },
    textShadowRadius: 8,
  },
  bestLabel: {
    fontSize: 16,
    fontWeight: "700",
    color: "#1A1A2A",
    opacity: 0.40,
    letterSpacing: 3,
    marginTop: -4,
  },

  // combo
  comboWrap: {
    position: "absolute",
    left: 0, right: 0,
    top: SH * 0.33,
    alignItems: "center",
  },
  comboText: {
    fontSize: 40,
    fontWeight: "900",
    letterSpacing: 1,
    textShadowColor: "rgba(0,0,0,0.18)",
    textShadowOffset: { width: 0, height: 2 },
    textShadowRadius: 6,
  },

  // hint
  hintWrap: {
    position: "absolute",
    left: 0, right: 0,
    bottom: 60,
    alignItems: "center",
  },
  hintArrow: {
    fontSize: 14,
    color: "#1A1A2A",
    opacity: 0.38,
    letterSpacing: 1,
  },

  // overlay (menu + game-over)
  overlay: {
    ...StyleSheet.absoluteFillObject,
    justifyContent: "center",
    alignItems: "center",
    backgroundColor: "rgba(245,240,232,0.90)",
    gap: 14,
  },
  titleCard: { alignItems: "center", marginBottom: 8 },
  titleLine1: {
    fontSize: 70,
    fontWeight: "900",
    color: "#1A1A2A",
    letterSpacing: -3,
    lineHeight: 68,
  },
  titleLine2: {
    fontSize: 70,
    fontWeight: "900",
    color: P_GREEN,
    letterSpacing: -3,
    lineHeight: 72,
  },
  titleDeco: {
    marginTop: 6,
    width: 80, height: 4,
    borderRadius: 2,
    backgroundColor: P_GREEN,
    opacity: 0.6,
  },

  goLabel: {
    fontSize: 18,
    fontWeight: "700",
    color: "#1A1A2A",
    letterSpacing: 6,
    opacity: 0.45,
  },
  goScore: {
    fontSize: 92,
    fontWeight: "900",
    color: P_GREEN,
    letterSpacing: -4,
    lineHeight: 96,
  },
  newBest: {
    fontSize: 22,
    fontWeight: "900",
    color: "#F5B820",
    letterSpacing: 2,
  },
  overlayBest: {
    fontSize: 18,
    fontWeight: "700",
    color: "#1A1A2A",
    opacity: 0.48,
    letterSpacing: 3,
  },
  overlayHint: {
    fontSize: 13,
    color: "#1A1A2A",
    opacity: 0.40,
    letterSpacing: 0.5,
    marginTop: -6,
  },

  btn: {
    backgroundColor: P_GREEN,
    paddingHorizontal: 44,
    paddingVertical: 17,
    borderRadius: 16,
    borderBottomWidth: 4,
    borderColor: P_DARK,
    marginTop: 8,
  },
  btnPressed: { opacity: 0.80, transform: [{ translateY: 2 }] },
  btnText: {
    fontSize: 22,
    fontWeight: "900",
    color: "white",
    letterSpacing: 2,
  },
});
