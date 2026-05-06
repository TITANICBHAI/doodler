#!/usr/bin/env node
const fs = require("fs");
const path = require("path");
const zlib = require("zlib");

const crcTable = new Uint32Array(256);
for (let n = 0; n < 256; n++) {
  let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  crcTable[n] = c;
}
function crc32(buf) {
  let crc = 0xffffffff;
  for (let i = 0; i < buf.length; i++)
    crc = crcTable[(crc ^ buf[i]) & 0xff] ^ (crc >>> 8);
  return (crc ^ 0xffffffff) >>> 0;
}

function chunk(type, data) {
  const typeBytes = Buffer.from(type, "ascii");
  const lenBuf = Buffer.alloc(4);
  lenBuf.writeUInt32BE(data.length, 0);
  const crcBuf = Buffer.alloc(4);
  crcBuf.writeUInt32BE(crc32(Buffer.concat([typeBytes, data])), 0);
  return Buffer.concat([lenBuf, typeBytes, data, crcBuf]);
}

function makePNG(w, h, r, g, b, a = 255) {
  const rowSize = 1 + w * 4;
  const raw = Buffer.alloc(h * rowSize);
  for (let y = 0; y < h; y++) {
    raw[y * rowSize] = 0;
    for (let x = 0; x < w; x++) {
      const i = y * rowSize + 1 + x * 4;
      raw[i] = r; raw[i + 1] = g; raw[i + 2] = b; raw[i + 3] = a;
    }
  }
  const compressed = zlib.deflateSync(raw);
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4);
  ihdr[8] = 8; ihdr[9] = 6;
  const sig = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);
  return Buffer.concat([sig, chunk("IHDR", ihdr), chunk("IDAT", compressed), chunk("IEND", Buffer.alloc(0))]);
}

function makeGradientPNG(w, h, r1, g1, b1, r2, g2, b2) {
  const rowSize = 1 + w * 4;
  const raw = Buffer.alloc(h * rowSize);
  for (let y = 0; y < h; y++) {
    const t = y / (h - 1);
    const r = Math.round(r1 + (r2 - r1) * t);
    const gv = Math.round(g1 + (g2 - g1) * t);
    const bv = Math.round(b1 + (b2 - b1) * t);
    raw[y * rowSize] = 0;
    for (let x = 0; x < w; x++) {
      const i = y * rowSize + 1 + x * 4;
      raw[i] = r; raw[i + 1] = gv; raw[i + 2] = bv; raw[i + 3] = 255;
    }
  }
  const compressed = zlib.deflateSync(raw);
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4);
  ihdr[8] = 8; ihdr[9] = 6;
  const sig = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);
  return Buffer.concat([sig, chunk("IHDR", ihdr), chunk("IDAT", compressed), chunk("IEND", Buffer.alloc(0))]);
}

function makeCirclePNG(size, r, g, b, a = 255) {
  const rowSize = 1 + size * 4;
  const raw = Buffer.alloc(size * rowSize);
  const cx = size / 2, cy = size / 2, radius = size / 2 - 1;
  for (let y = 0; y < size; y++) {
    raw[y * rowSize] = 0;
    for (let x = 0; x < size; x++) {
      const dx = x - cx, dy = y - cy;
      const dist = Math.sqrt(dx * dx + dy * dy);
      const alpha = dist < radius - 1 ? a : dist < radius ? Math.round(a * (radius - dist)) : 0;
      const i = y * rowSize + 1 + x * 4;
      raw[i] = r; raw[i + 1] = g; raw[i + 2] = b; raw[i + 3] = alpha;
    }
  }
  const compressed = zlib.deflateSync(raw);
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(size, 0); ihdr.writeUInt32BE(size, 4);
  ihdr[8] = 8; ihdr[9] = 6;
  const sig = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);
  return Buffer.concat([sig, chunk("IHDR", ihdr), chunk("IDAT", compressed), chunk("IEND", Buffer.alloc(0))]);
}

function makeRoundRectPNG(w, h, r, g, b, radius = 8) {
  const rowSize = 1 + w * 4;
  const raw = Buffer.alloc(h * rowSize);
  for (let y = 0; y < h; y++) {
    raw[y * rowSize] = 0;
    for (let x = 0; x < w; x++) {
      let inside = true;
      if (x < radius && y < radius) inside = Math.hypot(x - radius, y - radius) < radius;
      else if (x > w - radius && y < radius) inside = Math.hypot(x - (w - radius), y - radius) < radius;
      else if (x < radius && y > h - radius) inside = Math.hypot(x - radius, y - (h - radius)) < radius;
      else if (x > w - radius && y > h - radius) inside = Math.hypot(x - (w - radius), y - (h - radius)) < radius;
      const i = y * rowSize + 1 + x * 4;
      raw[i] = r; raw[i + 1] = g; raw[i + 2] = b; raw[i + 3] = inside ? 255 : 0;
    }
  }
  const compressed = zlib.deflateSync(raw);
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4);
  ihdr[8] = 8; ihdr[9] = 6;
  const sig = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);
  return Buffer.concat([sig, chunk("IHDR", ihdr), chunk("IDAT", compressed), chunk("IEND", Buffer.alloc(0))]);
}

const outDir = path.join(__dirname, "..", "UnityProject", "Assets", "Sprites");
fs.mkdirSync(outDir, { recursive: true });

const sprites = [
  { name: "player",             fn: () => makeRoundRectPNG(42, 50,  39, 192,  99, 12) },
  { name: "platform_static",    fn: () => makeRoundRectPNG(80, 14,  45, 201, 104,  8) },
  { name: "platform_moving",    fn: () => makeRoundRectPNG(80, 14,  78, 170, 245,  8) },
  { name: "platform_spring",    fn: () => makeRoundRectPNG(80, 14, 245, 209,  35,  8) },
  { name: "platform_breakable", fn: () => makeRoundRectPNG(80, 14, 240,  82,  50,  8) },
  { name: "platform_crumble",   fn: () => makeRoundRectPNG(80, 14, 255, 108,  32,  8) },
  { name: "platform_golden",    fn: () => makeRoundRectPNG(80, 14, 255, 215,   0,  8) },
  { name: "platform_rocket",    fn: () => makeRoundRectPNG(80, 14, 255,  68,  32,  8) },
  { name: "platform_ice",       fn: () => makeRoundRectPNG(80, 14, 168, 238, 255,  8) },
  { name: "platform_bomb",      fn: () => makeRoundRectPNG(80, 14, 255,  51,   0,  8) },
  { name: "platform_conveyor",  fn: () => makeRoundRectPNG(80, 14, 255, 128,  64,  8) },
  { name: "enemy_bird",         fn: () => makeCirclePNG(32, 255,  80,  80) },
  { name: "enemy_ghost",        fn: () => makeCirclePNG(32, 180, 130, 255, 200) },
  { name: "enemy_ufo",          fn: () => makeCirclePNG(32,  80, 200, 180) },
  { name: "enemy_bat",          fn: () => makeCirclePNG(32,  80,  40, 120) },
  { name: "enemy_asteroid",     fn: () => makeCirclePNG(32, 150, 110,  80) },
  { name: "boss",               fn: () => makeCirclePNG(80, 123,   0,   0) },
  { name: "wormhole",           fn: () => makeCirclePNG(64, 100,   0, 240) },
  { name: "coin",               fn: () => makeCirclePNG(18, 245, 209,  35) },
  { name: "gem",                fn: () => makeRoundRectPNG(24, 24, 155,  48, 255,  4) },
  { name: "powerup_jetpack",    fn: () => makeCirclePNG(32, 255, 140,   0) },
  { name: "powerup_shield",     fn: () => makeCirclePNG(32,   0, 160, 255, 200) },
  { name: "powerup_magnet",     fn: () => makeCirclePNG(32, 192,  80, 255) },
  { name: "powerup_star",       fn: () => makeCirclePNG(32, 255, 215,   0) },
  { name: "powerup_boots",      fn: () => makeCirclePNG(32, 255, 176,  32) },
  { name: "powerup_heart",      fn: () => makeCirclePNG(32, 255,  68,  85) },
  { name: "powerup_speed",      fn: () => makeCirclePNG(32,   0, 232, 255) },
  { name: "particle_white",     fn: () => makeCirclePNG( 8, 255, 255, 255) },
  { name: "particle_yellow",    fn: () => makeCirclePNG( 8, 255, 220,  60) },
  { name: "particle_red",       fn: () => makeCirclePNG( 8, 255,  60,  30) },
  { name: "particle_blue",      fn: () => makeCirclePNG( 8,  80, 160, 255) },
  { name: "particle_purple",    fn: () => makeCirclePNG( 8, 192,  80, 255) },
  { name: "particle_green",     fn: () => makeCirclePNG( 8,  60, 210, 120) },
  { name: "background_sunrise", fn: () => makeGradientPNG(2, 64, 245, 240, 232, 235, 215, 185) },
  { name: "background_sunset",  fn: () => makeGradientPNG(2, 64, 238, 190, 148, 200,  90,  50) },
  { name: "background_night",   fn: () => makeGradientPNG(2, 64,  18,  28,  60,   8,  12,  28) },
  { name: "background_space",   fn: () => makeGradientPNG(2, 64,   6,   8,  18,   2,   3,   8) },
  { name: "cloud",              fn: () => makeRoundRectPNG(96, 32, 255, 255, 255, 16) },
  { name: "planet",             fn: () => makeCirclePNG(64, 155,  93, 229) },
  { name: "shield_bubble",      fn: () => makeCirclePNG(64,  80, 160, 255, 120) },
  { name: "star_particle",      fn: () => makeCirclePNG( 4, 255, 248, 216) },
  { name: "rain_drop",          fn: () => makePNG(2, 10, 180, 200, 255, 120) },
  { name: "snow_flake",         fn: () => makeCirclePNG( 6, 240, 245, 255) },
  { name: "white_pixel",        fn: () => makePNG(1, 1, 255, 255, 255) },
];

let count = 0;
for (const { name, fn } of sprites) {
  const png = fn();
  fs.writeFileSync(path.join(outDir, `${name}.png`), png);
  console.log(`  ✓ ${name}.png`);
  count++;
}

console.log(`\n✅ Generated ${count} sprites → UnityProject/Assets/Sprites/`);
