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

const { width: SW, height: SH } = Dimensions.get("window");

// ─── Physics ──────────────────────────────────────────────────────────────────
const GRAVITY    = 1750;
const JUMP_VEL   = -690;
const SPRING_VEL = -1150;
const PLAYER_SPD = 248;
const FALL_MULT  = 1.88;

const PW = 42;
const PH = 50;
const PLH = 14;

const CAM_LEAD  = 0.38;
const DEATH_OFF = 300;
const TRAIL_LEN = 7;

// ─── Altitude zone defs ────────────────────────────────────────────────────────
// Each zone: score threshold, sky RGB, line RGB, line opacity, text RGB, isDark
const ZONES = [
  { at: 0,   sky: [245,240,232] as C3, line: [160,200,232] as C3, lop: 0.30, txt: [26,26,42]    as C3, dark: false, label: "☁  Sunrise"  },
  { at: 150, sky: [238,190,148] as C3, line: [200,120,70]  as C3, lop: 0.35, txt: [55,20,5]     as C3, dark: false, label: "🌅  Sunset"   },
  { at: 400, sky: [18,28,60]    as C3, line: [55,95,135]   as C3, lop: 0.45, txt: [200,228,255] as C3, dark: true,  label: "🌙  Night Sky" },
  { at: 800, sky: [6, 8, 18]    as C3, line: [20,45,80]    as C3, lop: 0.55, txt: [90,170,255]  as C3, dark: true,  label: "🚀  Deep Space"},
] as const;

type C3 = readonly [number, number, number];

function lerp(a: number, b: number, t: number) { return a + (b - a) * t; }
function lerpC(c1: C3, c2: C3, t: number): string {
  return `rgb(${Math.round(lerp(c1[0],c2[0],t))},${Math.round(lerp(c1[1],c2[1],t))},${Math.round(lerp(c1[2],c2[2],t))})`;
}
function zoneColors(score: number) {
  let i = ZONES.length - 1;
  for (let j = 0; j < ZONES.length - 1; j++) {
    if (score < ZONES[j + 1].at) { i = j; break; }
  }
  if (i === ZONES.length - 1) return { ...ZONES[i], sky: `rgb(${ZONES[i].sky.join(",")})`, line: `rgb(${ZONES[i].line.join(",")})`, txt: `rgb(${ZONES[i].txt.join(",")})`, t: 1 };
  const z0 = ZONES[i], z1 = ZONES[i + 1];
  const t = Math.max(0, Math.min(1, (score - z0.at) / (z1.at - z0.at)));
  return {
    sky:   lerpC(z0.sky,  z1.sky,  t),
    line:  lerpC(z0.line, z1.line, t),
    lop:   lerp(z0.lop, z1.lop, t),
    txt:   lerpC(z0.txt,  z1.txt,  t),
    dark:  t > 0.5 ? z1.dark : z0.dark,
    label: t > 0.5 ? z1.label : z0.label,
    t,
  };
}

// ─── Platform colours ──────────────────────────────────────────────────────────
type PType = "static" | "moving" | "spring" | "breakable";
const PCOL: Record<PType, string>  = { static: "#2DC968", moving: "#4EAAF5", spring: "#F5D123", breakable: "#F05232" };
const PBORD: Record<PType, string> = { static: "#1AA850", moving: "#2D88D8", spring: "#D4A800", breakable: "#C83018" };
const P_GREEN = "#27C063";
const P_DARK  = "#1E9E50";

// ─── Types ────────────────────────────────────────────────────────────────────
type Phase = "menu" | "play" | "dead";

interface Plat {
  id: number; x: number; y: number; w: number; type: PType;
  broken: boolean; ox: number; dir: number; range: number;
  sq: number; // squeeze anim 0→0.6 then lerp back to 0
}
interface Particle { x: number; y: number; vx: number; vy: number; life: number; color: string; r: number; }
interface TrailPt   { x: number; y: number; }
interface Cloud     { id: number; x: number; y: number; w: number; h: number; par: number; op: number; }
interface Coin      { id: number; x: number; y: number; collected: boolean; popT: number; }

interface GS {
  px: number; py: number; pvx: number; pvy: number;
  psx: number; psy: number; facing: number; eyeY: number; // eye look offset
  scrollY: number; minY: number; startY: number;
  score: number; best: number;
  combo: number; comboT: number;
  milestoneText: string; milestoneT: number; lastMilestone: number;
  plats: Plat[]; parts: Particle[]; trail: TrailPt[]; clouds: Cloud[]; coins: Coin[];
  phase: Phase; input: number;
  shakeX: number; shakeY: number; shakeT: number;
  pid: number;
}

// ─── Factory helpers ──────────────────────────────────────────────────────────
function pickType(score: number): PType {
  const r = Math.random();
  if (score < 80)  return r < 0.06 ? "moving" : "static";
  if (score < 200) return r < 0.09 ? "spring" : r < 0.24 ? "moving" : r < 0.37 ? "breakable" : "static";
  if (score < 500) return r < 0.13 ? "spring" : r < 0.30 ? "moving" : r < 0.48 ? "breakable" : "static";
  return r < 0.16 ? "spring" : r < 0.32 ? "moving" : r < 0.53 ? "breakable" : "static";
}

function mkPlat(gs: GS, y: number, score: number): Plat {
  const w  = Math.max(48, 92 - score * 0.02);
  const x  = Math.random() * (SW - w);
  return { id: gs.pid++, x, y, w, type: pickType(score), broken: false, ox: x, dir: Math.random() < 0.5 ? 1 : -1, range: 38 + Math.random() * 55, sq: 0 };
}
function mkStatPlat(gs: GS, y: number, x: number, w: number): Plat {
  return { id: gs.pid++, x, y, w, type: "static", broken: false, ox: x, dir: 1, range: 0, sq: 0 };
}
function mkCloud(gs: GS, worldY: number): Cloud {
  return {
    id: gs.pid++,
    x: Math.random() * (SW - 120),
    y: worldY,
    w: 90 + Math.random() * 100,
    h: 28 + Math.random() * 24,
    par: 0.22 + Math.random() * 0.18, // parallax factor
    op: 0.55 + Math.random() * 0.35,
  };
}
function mkCoin(gs: GS, x: number, y: number): Coin {
  return { id: gs.pid++, x, y, collected: false, popT: 0 };
}

// ─── Initial state ────────────────────────────────────────────────────────────
const MILESTONES = [100, 250, 500, 750, 1000, 1500, 2000];

function mkGS(best: number): GS {
  const startY = SH * 0.70;
  const gs: GS = {
    px: SW / 2 - PW / 2, py: startY, pvx: 0, pvy: JUMP_VEL,
    psx: 1, psy: 1, facing: 1, eyeY: 0,
    scrollY: 0, minY: startY, startY,
    score: 0, best,
    combo: 0, comboT: 0,
    milestoneText: "", milestoneT: 0, lastMilestone: 0,
    plats: [], parts: [], trail: [], clouds: [], coins: [],
    phase: "menu", input: 0,
    shakeX: 0, shakeY: 0, shakeT: 0,
    pid: 0,
  };

  // Starting platform right under player
  gs.plats.push(mkStatPlat(gs, startY + PH, SW / 2 - 52, 104));

  // Easy starter platforms
  let y = startY;
  for (let i = 0; i < 6; i++) {
    y -= 58 + Math.random() * 26;
    const w = 80 + Math.random() * 22;
    const cx = SW * 0.2 + Math.random() * SW * 0.6;
    gs.plats.push(mkStatPlat(gs, y, cx - w / 2, w));
  }
  // Rest of initial platforms
  for (let i = 0; i < 18; i++) {
    y -= 70 + Math.random() * 46;
    gs.plats.push(mkPlat(gs, y, 0));
    if (Math.random() < 0.35) {
      gs.coins.push(mkCoin(gs, gs.plats[gs.plats.length-1].x + 20 + Math.random() * 30, y - 28));
    }
  }

  // Initial clouds
  let cy = startY;
  for (let i = 0; i < 8; i++) {
    cy -= SH * 0.35 + Math.random() * SH * 0.3;
    gs.clouds.push(mkCloud(gs, cy));
  }

  return gs;
}

// ─── Particles ────────────────────────────────────────────────────────────────
function emit(gs: GS, cx: number, cy: number, col: string, n: number) {
  for (let i = 0; i < n; i++) {
    const ang = (Math.PI * 2 * i) / n + Math.random() * 0.5;
    const spd = 65 + Math.random() * 190;
    gs.parts.push({ x: cx, y: cy, color: col, vx: Math.cos(ang) * spd, vy: Math.sin(ang) * spd - 85, life: 1, r: 3 + Math.random() * 5 });
  }
}

// ─── Game update ──────────────────────────────────────────────────────────────
function update(gs: GS, dt: number, now: number) {
  if (gs.phase !== "play") return;

  // Horizontal
  gs.pvx = gs.input * PLAYER_SPD;
  if (gs.input !== 0) gs.facing = gs.input;

  // Gravity
  gs.pvy += GRAVITY * (gs.pvy > 0 ? FALL_MULT : 1) * dt;
  gs.pvy = Math.min(gs.pvy, 1450);

  // Move
  gs.px += gs.pvx * dt;
  gs.py += gs.pvy * dt;

  // Wrap
  if (gs.px + PW < 0) gs.px = SW;
  if (gs.px > SW)     gs.px = -PW;

  // Eye direction
  const eyeTarget = gs.pvy < -180 ? -2.5 : gs.pvy > 180 ? 2.5 : 0;
  gs.eyeY += (eyeTarget - gs.eyeY) * Math.min(1, dt * 8);

  // Trail
  if (!gs.trail.length || Math.hypot(gs.px - gs.trail[0].x, gs.py - gs.trail[0].y) > 5) {
    gs.trail.unshift({ x: gs.px + PW / 2, y: gs.py + PH / 2 });
    if (gs.trail.length > TRAIL_LEN) gs.trail.pop();
  }

  // Platform collisions (falling only)
  if (gs.pvy > 0) {
    for (const p of gs.plats) {
      if (p.broken) continue;
      const feet = gs.py + PH;
      if (feet >= p.y && feet <= p.y + PLH + 14 && gs.px + PW > p.x + 4 && gs.px < p.x + p.w - 4) {
        gs.py = p.y - PH;
        const sp = p.type === "spring";
        gs.pvy = sp ? SPRING_VEL : JUMP_VEL;
        gs.psx = sp ? 1.58 : 1.40;
        gs.psy = sp ? 0.50 : 0.63;
        p.sq   = sp ? 0.65 : 0.45; // platform squashes too
        gs.combo++;
        gs.comboT = 2.6;
        const cx = p.x + p.w / 2;
        emit(gs, cx, p.y, PCOL[p.type], sp ? 14 : 6);
        if (p.type === "breakable") p.broken = true;
        break;
      }
    }
  }

  // Coin collection
  const pcx = gs.px + PW / 2, pcy = gs.py + PH / 2;
  for (const c of gs.coins) {
    if (c.collected) { c.popT = Math.max(0, c.popT - dt * 3); continue; }
    if (Math.hypot(pcx - c.x, pcy - c.y) < 22) {
      c.collected = true;
      c.popT = 1;
      gs.score += 5;
      emit(gs, c.x, c.y, "#F5D123", 5);
    }
  }

  // Camera
  const ideal = gs.py - SH * CAM_LEAD;
  if (ideal < gs.scrollY) gs.scrollY = ideal;

  // Score + milestones
  if (gs.py < gs.minY) gs.minY = gs.py;
  gs.score = Math.max(gs.score, Math.floor((gs.startY - gs.minY) / 5));
  if (gs.score > gs.best) gs.best = gs.score;
  for (const m of MILESTONES) {
    if (gs.score >= m && m > gs.lastMilestone) {
      gs.lastMilestone = m;
      gs.milestoneText = `${m} m`;
      gs.milestoneT = 2.0;
    }
  }

  // Generate platforms
  const topGenY = gs.scrollY - SH * 0.55;
  let topY = gs.plats.length ? Math.min(...gs.plats.map((p) => p.y)) : gs.startY;
  let safe = 0;
  while (topY > topGenY && safe++ < 25) {
    const gap = 72 + Math.random() * 48 + Math.min(gs.score * 0.05, 38);
    topY -= gap;
    gs.plats.push(mkPlat(gs, topY, gs.score));
    if (Math.random() < 0.30) gs.coins.push(mkCoin(gs, gs.plats[gs.plats.length-1].x + Math.random() * 50, topY - 30));
  }

  // Generate clouds
  const topCloud = gs.clouds.length ? Math.min(...gs.clouds.map((c) => c.y)) : gs.startY;
  if (topCloud > gs.scrollY - SH * 1.5) {
    gs.clouds.push(mkCloud(gs, topCloud - SH * (0.4 + Math.random() * 0.5)));
  }

  // Remove off-screen objects
  const cutY = gs.scrollY + SH + 280;
  gs.plats  = gs.plats.filter((p) => p.y < cutY);
  gs.coins  = gs.coins.filter((c) => c.y < cutY || !c.collected);
  gs.clouds = gs.clouds.filter((c) => c.y - gs.scrollY * c.par < SH + 150);

  // Moving platforms
  for (const p of gs.plats) {
    if (p.type === "moving") p.x = p.ox + Math.sin(now / 1000 * 1.6 + p.id * 1.3) * p.range;
  }

  // Particles
  for (const pt of gs.parts) {
    pt.x += pt.vx * dt; pt.y += pt.vy * dt;
    pt.vy += 560 * dt;
    pt.life -= dt * 2.2;
  }
  gs.parts = gs.parts.filter((p) => p.life > 0);

  // Squash lerp (player)
  const sr = 1 - Math.exp(-13 * dt);
  gs.psx += (1 - gs.psx) * sr;
  gs.psy += (1 - gs.psy) * sr;

  // Squeeze lerp (platforms)
  for (const p of gs.plats) p.sq += (0 - p.sq) * Math.min(1, dt * 14);

  // Shake
  if (gs.shakeT > 0) {
    gs.shakeT -= dt;
    const s = gs.shakeT * 9;
    gs.shakeX = (Math.random() - 0.5) * s;
    gs.shakeY = (Math.random() - 0.5) * s;
  } else { gs.shakeX = 0; gs.shakeY = 0; }

  // Combo / milestone timers
  if (gs.comboT > 0)    gs.comboT    -= dt;
  if (gs.milestoneT > 0) gs.milestoneT -= dt;

  // Death
  if (gs.py > gs.scrollY + SH + DEATH_OFF) {
    gs.phase  = "dead";
    gs.shakeT = 0.65;
    emit(gs, gs.px + PW / 2, gs.py, P_GREEN, 24);
  }
}

// ─── Background ───────────────────────────────────────────────────────────────
const LINE_SPACING = 44;
const LINE_COUNT   = Math.ceil(SH / LINE_SPACING) + 3;
const STAR_SEEDS   = Array.from({ length: 30 }, (_, i) => ({
  x: (Math.sin(i * 2.39) * 0.5 + 0.5) * SW,
  y: (Math.cos(i * 1.61) * 0.5 + 0.5) * SH,
  r: 1 + (i % 3) * 0.8,
}));

function Background({ scrollY, score }: { scrollY: number; score: number }) {
  const zc = zoneColors(score);
  const lineOffset = ((scrollY * 0.18) % LINE_SPACING + LINE_SPACING) % LINE_SPACING;
  const starOp = Math.max(0, Math.min(1, (score - 300) / 180));

  return (
    <View style={[StyleSheet.absoluteFillObject, { backgroundColor: zc.sky }]} pointerEvents="none">
      {/* Ruled lines */}
      {Array.from({ length: LINE_COUNT }, (_, i) => (
        <View key={i} style={[b.line, { top: i * LINE_SPACING - LINE_SPACING + lineOffset, backgroundColor: zc.line, opacity: zc.lop }]} />
      ))}
      {/* Margin line */}
      <View style={[b.margin, { backgroundColor: zc.dark ? "#203858" : "#E88888", opacity: zc.dark ? 0.35 : 0.24 }]} />
      {/* Stars (night/space) */}
      {starOp > 0 && STAR_SEEDS.map((s, i) => {
        const sy = ((s.y + scrollY * 0.06) % SH + SH) % SH;
        return <View key={i} style={[b.star, { left: s.x, top: sy, width: s.r * 2, height: s.r * 2, borderRadius: s.r, opacity: starOp * (0.5 + (i % 3) * 0.25) }]} />;
      })}
    </View>
  );
}

// ─── Cloud renderer ───────────────────────────────────────────────────────────
function CloudLayer({ clouds, scrollY, dark }: { clouds: Cloud[]; scrollY: number; dark: boolean }) {
  const col = dark ? "rgba(80,120,200," : "rgba(255,255,255,";
  return (
    <View style={StyleSheet.absoluteFillObject} pointerEvents="none">
      {clouds.map((c) => {
        const sy = c.y - scrollY * c.par;
        if (sy > SH + 80 || sy < -80) return null;
        return (
          <View key={c.id} style={{ position: "absolute", left: c.x, top: sy, width: c.w, height: c.h, opacity: c.op }}>
            {/* Main body */}
            <View style={[b.cloudBody, { width: c.w * 0.75, height: c.h, left: c.w * 0.12, backgroundColor: col + "0.9)" }]} />
            {/* Top puffs */}
            <View style={[b.cloudPuff, { width: c.h * 1.1, height: c.h * 1.1, left: c.w * 0.2, top: -c.h * 0.4, backgroundColor: col + "0.85)" }]} />
            <View style={[b.cloudPuff, { width: c.h * 0.9, height: c.h * 0.9, left: c.w * 0.48, top: -c.h * 0.35, backgroundColor: col + "0.8)"  }]} />
          </View>
        );
      })}
    </View>
  );
}

// ─── Platform component ───────────────────────────────────────────────────────
const PlatView = React.memo(function PlatView({ p }: { p: Plat }) {
  const broken = p.broken;
  const bg   = broken ? "#999" : PCOL[p.type];
  const bord = broken ? "#777" : PBORD[p.type];
  const sqSY = 1 - p.sq * 0.44;
  return (
    <View style={[g.plat, { left: p.x, top: p.y + (PLH * p.sq * 0.22), width: p.w, backgroundColor: bg, borderColor: bord, opacity: broken ? 0.45 : 1, transform: [{ scaleY: sqSY }] }]}>
      <View style={g.platShine} />
      {p.type === "spring" && !broken && <View style={g.springWrap}><View style={g.springLine} /><View style={g.springLine} /><View style={g.springLine} /></View>}
      {p.type === "moving" && !broken && (
        <View style={g.movingArrows}>
          <Text style={g.arrowText}>{"◀  ▶"}</Text>
        </View>
      )}
    </View>
  );
});

// ─── Player component ─────────────────────────────────────────────────────────
function PlayerView({ gs }: { gs: GS }) {
  const flip = gs.facing < 0 ? -1 : 1;
  const ey   = Math.round(gs.eyeY * 10) / 10;
  return (
    <View style={[g.player, { left: gs.px, top: gs.py, transform: [{ scaleX: gs.psx * flip }, { scaleY: gs.psy }] }]}>
      <View style={g.playerGlow} />
      {/* Antennae */}
      <View style={[g.antenna, { left: PW * 0.28 }]} />
      <View style={[g.antenna, { right: PW * 0.28 }]} />
      {/* Eyes */}
      <View style={g.eyeRow}>
        <View style={g.eye}><View style={[g.pupil, { marginTop: 2 + ey }]} /><View style={g.eyeShine} /></View>
        <View style={g.eye}><View style={[g.pupil, { marginTop: 2 + ey }]} /><View style={g.eyeShine} /></View>
      </View>
      {/* Mouth */}
      <View style={[g.mouth, gs.pvy < -50 ? g.mouthHappy : g.mouthNeutral]} />
      {/* Feet */}
      <View style={g.feet}><View style={g.foot} /><View style={g.foot} /></View>
    </View>
  );
}

// ─── Coin component ───────────────────────────────────────────────────────────
function CoinView({ c }: { c: Coin }) {
  if (c.collected && c.popT <= 0) return null;
  const scale = c.collected ? 1 + (1 - c.popT) * 0.6 : 1;
  const op    = c.collected ? c.popT : 1;
  return (
    <View style={{ position: "absolute", left: c.x - 9, top: c.y - 9, width: 18, height: 18, borderRadius: 9, backgroundColor: "#F5D123", borderWidth: 2, borderColor: "#D4A800", opacity: op, transform: [{ scale }], alignItems: "center", justifyContent: "center" }}>
      <View style={{ width: 6, height: 6, borderRadius: 3, backgroundColor: "rgba(255,255,255,0.7)" }} />
    </View>
  );
}

// ─── Shadow helper ────────────────────────────────────────────────────────────
function PlayerShadow({ gs }: { gs: GS }) {
  let closest: Plat | null = null, dist = Infinity;
  const pBottom = gs.py + PH;
  for (const p of gs.plats) {
    if (p.broken || p.y <= pBottom) continue;
    const d = p.y - pBottom;
    if (d < 260 && d < dist && gs.px + PW > p.x + 4 && gs.px < p.x + p.w - 4) {
      dist = d; closest = p;
    }
  }
  if (!closest) return null;
  const op = 0.38 * (1 - dist / 260);
  const sw = Math.max(14, PW * 0.85 * (1 - dist / 400));
  return (
    <View style={{ position: "absolute", left: gs.px + PW / 2 - sw / 2, top: closest.y - 4, width: sw, height: 6, borderRadius: sw / 2, backgroundColor: "#000", opacity: op }} />
  );
}

// ─── Speed lines ──────────────────────────────────────────────────────────────
function SpeedLines({ gs, textColor, now }: { gs: GS; textColor: string; now: number }) {
  if (gs.pvy < 520) return null;
  const intensity = Math.min(1, (gs.pvy - 520) / 700);
  const t = now / 70;
  return (
    <>
      {[0, 1, 2, 3, 4].map((i) => {
        const ox  = (Math.sin(t + i * 2.1) * 26);
        const len = 12 + Math.cos(t * 0.8 + i * 1.5) * 7;
        return (
          <View key={i} style={{ position: "absolute", left: gs.px + PW / 2 + ox - 1.5, top: gs.py - len - 8, width: 3, height: len, borderRadius: 2, backgroundColor: textColor, opacity: 0.22 * intensity }} />
        );
      })}
    </>
  );
}

// ─── Combo colours ────────────────────────────────────────────────────────────
function comboCol(n: number) {
  return n >= 12 ? "#FF4422" : n >= 7 ? "#F5D123" : "#50F0AA";
}

// ─── Main game screen ─────────────────────────────────────────────────────────
export default function GameScreen() {
  const insets   = useSafeAreaInsets();
  const gs       = useRef<GS>(mkGS(0));
  const [, setT] = useState(0);
  const rafRef   = useRef<number>(0);
  const prevTs   = useRef<number>(0);
  const nowRef   = useRef<number>(0);
  const [phase, setPhase] = useState<Phase>("menu");

  const loop = useCallback((ts: number) => {
    if (prevTs.current === 0) prevTs.current = ts;
    const dt = Math.min((ts - prevTs.current) / 1000, 0.033);
    prevTs.current = ts;
    nowRef.current = ts;
    update(gs.current, dt, ts);
    if (gs.current.phase !== phase) setPhase(gs.current.phase);
    setT((t) => t + 1);
    rafRef.current = requestAnimationFrame(loop);
  }, [phase]);

  useEffect(() => {
    rafRef.current = requestAnimationFrame(loop);
    return () => cancelAnimationFrame(rafRef.current);
  }, [loop]);

  // Keyboard (web)
  useEffect(() => {
    if (Platform.OS !== "web") return;
    const dn = (e: KeyboardEvent) => {
      if (e.key === "ArrowLeft"  || e.key === "a") gs.current.input = -1;
      if (e.key === "ArrowRight" || e.key === "d") gs.current.input =  1;
      if ((e.key === " " || e.key === "Enter") && gs.current.phase !== "play") startGame();
    };
    const up = () => { gs.current.input = 0; };
    window.addEventListener("keydown", dn);
    window.addEventListener("keyup",   up);
    return () => { window.removeEventListener("keydown", dn); window.removeEventListener("keyup", up); };
  }, []);

  const startGame = useCallback(() => {
    const best = gs.current.best;
    gs.current  = mkGS(best);
    gs.current.phase = "play";
    prevTs.current   = 0;
    setPhase("play");
  }, []);

  const onTouchStart = useCallback((e: any) => {
    if (gs.current.phase !== "play") return;
    gs.current.input = e.nativeEvent.locationX < SW / 2 ? -1 : 1;
  }, []);
  const onTouchMove = useCallback((e: any) => {
    if (gs.current.phase !== "play") return;
    gs.current.input = e.nativeEvent.locationX < SW / 2 ? -1 : 1;
  }, []);
  const onTouchEnd = useCallback(() => { gs.current.input = 0; }, []);

  const r         = gs.current;
  const zc        = zoneColors(r.score);
  const webTop    = Platform.OS === "web" ? 67 : 0;
  const topPad    = insets.top + webTop + 8;
  const darkMode  = zc.dark;
  const now       = nowRef.current;

  return (
    <View style={s.root}>
      <StatusBar hidden />

      {/* ── Notebook background ── */}
      <Background scrollY={r.scrollY} score={r.score} />

      {/* ── Touch area ── */}
      <View style={StyleSheet.absoluteFillObject} onTouchStart={onTouchStart} onTouchMove={onTouchMove} onTouchEnd={onTouchEnd}>

        {/* ── Clouds (parallax, behind world) ── */}
        <CloudLayer clouds={r.clouds} scrollY={r.scrollY} dark={darkMode} />

        {/* ── World layer ── */}
        <View
          style={[StyleSheet.absoluteFillObject, { transform: [{ translateX: r.shakeX }, { translateY: -r.scrollY + r.shakeY }] }]}
          pointerEvents="none"
        >
          {/* Shadow */}
          <PlayerShadow gs={r} />

          {/* Platforms */}
          {r.plats.map((p) => <PlatView key={p.id} p={p} />)}

          {/* Coins */}
          {r.coins.map((c) => <CoinView key={c.id} c={c} />)}

          {/* Trail */}
          {r.trail.map((t, i) => (
            <View key={i} style={{ position: "absolute", left: t.x - PW * 0.22, top: t.y - PH * 0.22, width: PW * 0.44, height: PH * 0.44, borderRadius: PW * 0.22, backgroundColor: P_GREEN, opacity: (1 - i / TRAIL_LEN) * 0.30 }} />
          ))}

          {/* Particles */}
          {r.parts.map((p, i) => (
            <View key={i} style={{ position: "absolute", left: p.x - p.r, top: p.y - p.r, width: p.r * 2, height: p.r * 2, borderRadius: p.r, backgroundColor: p.color, opacity: Math.max(0, p.life) }} />
          ))}

          {/* Speed lines */}
          <SpeedLines gs={r} textColor={zc.txt} now={now} />

          {/* Player */}
          <PlayerView gs={r} />
        </View>

        {/* ── HUD ── */}
        <View style={[s.hud, { paddingTop: topPad }]} pointerEvents="none">
          <Text style={[s.scoreNum, { color: zc.txt }]}>{r.score}</Text>
          <Text style={[s.mLabel, { color: zc.txt, opacity: 0.55 }]}>m</Text>
          {r.best > 0 && <Text style={[s.bestLabel, { color: zc.txt }]}>BEST  {r.best} m</Text>}
        </View>

        {/* Zone label */}
        {phase === "play" && (
          <View style={[s.zoneLabel, { top: topPad + 12 }]} pointerEvents="none">
            <Text style={[s.zoneTxt, { color: zc.txt }]}>{zc.label}</Text>
          </View>
        )}

        {/* Combo */}
        {phase === "play" && r.comboT > 0 && r.combo >= 3 && (
          <View style={s.comboWrap} pointerEvents="none">
            <Text style={[s.comboText, { color: comboCol(r.combo) }]}>
              {r.combo >= 12 ? `🔥 x${r.combo}` : r.combo >= 7 ? `⚡ x${r.combo}` : `x${r.combo}`}
            </Text>
          </View>
        )}

        {/* Milestone banner */}
        {phase === "play" && r.milestoneT > 0 && (
          <View style={s.milestoneWrap} pointerEvents="none">
            <View style={[s.milestonePill, { opacity: Math.min(1, r.milestoneT * 2) }]}>
              <Text style={s.milestoneTxt}>🏔  {r.milestoneText}</Text>
            </View>
          </View>
        )}

        {/* Hint */}
        {phase === "play" && r.score === 0 && (
          <View style={s.hintWrap} pointerEvents="none">
            <Text style={[s.hintTxt, { color: zc.txt }]}>← tap left  ·  tap right →</Text>
          </View>
        )}

        {/* ── Menu ── */}
        {phase === "menu" && (
          <View style={[s.overlay, { backgroundColor: "rgba(245,240,232,0.91)" }]}>
            <View style={s.titleCard}>
              <Text style={s.t1}>DOODLE</Text>
              <Text style={s.t2}>CLIMB</Text>
              <View style={s.titleBar} />
            </View>
            {r.best > 0 && <Text style={s.ovBest}>BEST  {r.best} m</Text>}
            <Pressable onPress={startGame} style={({ pressed }) => [s.btn, pressed && s.btnP]}>
              <Text style={s.btnTxt}>TAP TO PLAY</Text>
            </Pressable>
            <Text style={s.ovHint}>← left half  ·  right half →</Text>
            <Text style={s.ovHint}>or  A / D  on  keyboard</Text>
          </View>
        )}

        {/* ── Game Over ── */}
        {phase === "dead" && (
          <View style={[s.overlay, { backgroundColor: "rgba(8,10,22,0.88)" }]}>
            <Text style={s.goLabel}>SCORE</Text>
            <Text style={s.goScore}>{r.score}</Text>
            <Text style={s.goM}>m</Text>
            {r.score >= r.best && r.score > 0 && <Text style={s.newBest}>✦ NEW BEST ✦</Text>}
            {r.best > r.score  && <Text style={s.ovBestDark}>BEST  {r.best} m</Text>}
            <Pressable onPress={startGame} style={({ pressed }) => [s.btn, pressed && s.btnP]}>
              <Text style={s.btnTxt}>TRY AGAIN</Text>
            </Pressable>
          </View>
        )}
      </View>
    </View>
  );
}

// ─── Styles ───────────────────────────────────────────────────────────────────
const b = StyleSheet.create({
  line:   { position: "absolute", left: 0, right: 0, height: 1.5 },
  margin: { position: "absolute", left: 40, top: 0, bottom: 0, width: 1.5 },
  star:   { position: "absolute", backgroundColor: "#FFFFFF" },
  cloudBody: { position: "absolute", borderRadius: 30 },
  cloudPuff: { position: "absolute", borderRadius: 99 },
});

const g = StyleSheet.create({
  plat: {
    position: "absolute", height: PLH,
    borderRadius: 8, borderBottomWidth: 3, overflow: "hidden",
  },
  platShine: {
    position: "absolute", top: 0, left: 6, right: 6, height: 4,
    borderRadius: 3, backgroundColor: "rgba(255,255,255,0.42)",
  },
  springWrap: { position: "absolute", top: 2, left: "25%", right: "25%", gap: 1.5 },
  springLine: { height: 2.5, borderRadius: 1.5, backgroundColor: "rgba(255,255,255,0.58)", marginBottom: 1 },
  movingArrows: { position: "absolute", inset: 0, alignItems: "center", justifyContent: "center" },
  arrowText: { fontSize: 7, color: "rgba(255,255,255,0.55)", letterSpacing: 2 },

  player: {
    position: "absolute", width: PW, height: PH,
    backgroundColor: P_GREEN, borderRadius: 12,
    alignItems: "center", paddingTop: 5,
    borderBottomWidth: 3, borderColor: P_DARK, overflow: "hidden",
  },
  playerGlow: {
    position: "absolute", top: -4, left: -4, right: -4, bottom: -4,
    borderRadius: 16, backgroundColor: P_GREEN, opacity: 0.16,
  },
  antenna: {
    position: "absolute", top: -8, width: 3, height: 10,
    borderRadius: 2, backgroundColor: P_DARK,
  },
  eyeRow: { flexDirection: "row", gap: 8, marginTop: 4 },
  eye: { width: 12, height: 12, borderRadius: 6, backgroundColor: "white", justifyContent: "center", alignItems: "center" },
  pupil: { width: 5, height: 5, borderRadius: 2.5, backgroundColor: "#0A0A1A" },
  eyeShine: { position: "absolute", top: 1, right: 1, width: 3, height: 3, borderRadius: 1.5, backgroundColor: "white" },
  mouth: { marginTop: 4, width: 14, height: 5, borderRadius: 3, borderBottomWidth: 2, borderLeftWidth: 1.5, borderRightWidth: 1.5, borderColor: "rgba(255,255,255,0.72)", borderTopWidth: 0 },
  mouthHappy:   { borderColor: "rgba(255,255,255,0.85)" },
  mouthNeutral: { borderColor: "rgba(255,255,255,0.55)" },
  feet: { flexDirection: "row", gap: 8, marginTop: 2 },
  foot: { width: 8, height: 5, borderRadius: 3, backgroundColor: P_DARK },
});

const s = StyleSheet.create({
  root: { flex: 1 },

  // HUD
  hud: { position: "absolute", top: 0, left: 0, right: 0, alignItems: "center" },
  scoreNum: { fontSize: 58, fontWeight: "900", letterSpacing: -2, lineHeight: 62 },
  mLabel:   { fontSize: 18, fontWeight: "700", letterSpacing: 2, marginTop: -8 },
  bestLabel: { fontSize: 16, fontWeight: "700", letterSpacing: 3, marginTop: 2, opacity: 0.45 },

  zoneLabel: { position: "absolute", left: 16 },
  zoneTxt:   { fontSize: 12, fontWeight: "700", letterSpacing: 0.5, opacity: 0.55 },

  // Combo
  comboWrap: { position: "absolute", left: 0, right: 0, top: SH * 0.33, alignItems: "center" },
  comboText: { fontSize: 42, fontWeight: "900", letterSpacing: 1, textShadowColor: "rgba(0,0,0,0.2)", textShadowOffset: { width: 0, height: 2 }, textShadowRadius: 6 },

  // Milestone
  milestoneWrap: { position: "absolute", left: 0, right: 0, top: SH * 0.20, alignItems: "center" },
  milestonePill: { backgroundColor: "rgba(39,192,99,0.92)", paddingHorizontal: 24, paddingVertical: 10, borderRadius: 30 },
  milestoneTxt:  { fontSize: 22, fontWeight: "900", color: "white", letterSpacing: 1 },

  // Hint
  hintWrap: { position: "absolute", left: 0, right: 0, bottom: 70, alignItems: "center" },
  hintTxt:  { fontSize: 13, opacity: 0.38, letterSpacing: 0.5 },

  // Overlays
  overlay: { ...StyleSheet.absoluteFillObject, justifyContent: "center", alignItems: "center", gap: 12 },
  titleCard: { alignItems: "center", marginBottom: 8 },
  t1: { fontSize: 72, fontWeight: "900", color: "#1A1A2A", letterSpacing: -3, lineHeight: 70 },
  t2: { fontSize: 72, fontWeight: "900", color: P_GREEN,   letterSpacing: -3, lineHeight: 74 },
  titleBar: { marginTop: 8, width: 80, height: 4, borderRadius: 2, backgroundColor: P_GREEN, opacity: 0.65 },
  ovBest:     { fontSize: 18, fontWeight: "700", color: "#1A1A2A", opacity: 0.50, letterSpacing: 3 },
  ovBestDark: { fontSize: 18, fontWeight: "700", color: "#90C8FF", opacity: 0.65, letterSpacing: 3 },
  ovHint: { fontSize: 13, color: "#1A1A2A", opacity: 0.38, letterSpacing: 0.3, marginTop: -4 },

  // Game over
  goLabel: { fontSize: 18, fontWeight: "700", color: "#90C8FF", opacity: 0.55, letterSpacing: 6 },
  goScore: { fontSize: 94, fontWeight: "900", color: P_GREEN, letterSpacing: -4, lineHeight: 98 },
  goM:     { fontSize: 24, fontWeight: "700", color: P_GREEN, opacity: 0.7, marginTop: -10 },
  newBest: { fontSize: 22, fontWeight: "900", color: "#F5B820", letterSpacing: 2 },

  // Button
  btn:  { backgroundColor: P_GREEN, paddingHorizontal: 44, paddingVertical: 17, borderRadius: 16, borderBottomWidth: 4, borderColor: P_DARK, marginTop: 10 },
  btnP: { opacity: 0.80, transform: [{ translateY: 2 }] },
  btnTxt: { fontSize: 22, fontWeight: "900", color: "white", letterSpacing: 2 },
});
