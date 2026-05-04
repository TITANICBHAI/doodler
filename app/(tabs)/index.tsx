import React, { useRef, useState, useEffect, useCallback } from "react";
import { View, Text, StyleSheet, Dimensions, Pressable, Platform } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { StatusBar } from "expo-status-bar";

const { width: SW, height: SH } = Dimensions.get("window");

// ─── Physics ──────────────────────────────────────────────────────────────────
const GRAVITY    = 1750;
const JUMP_VEL   = -690;
const SPRING_VEL = -1155;
const ROCKET_VEL = -1760;
const DJUMP_VEL  = -570;
const JETPACK_VY = -345;
const PLAYER_SPD = 248;
const FALL_MULT  = 1.88;
const PW = 42; const PH = 50; const PLH = 14;
const CAM_LEAD = 0.38; const DEATH_OFF = 350; const TRAIL_LEN = 8;
const MAX_GAP = 112; // physics max jump height ≈ 136px → cap at 82% for safety

// ─── Persistent storage ───────────────────────────────────────────────────────
function getBest(): number {
  try { return Math.max(0, parseInt((globalThis as any).localStorage?.getItem?.("dc_best") || "0") || 0); } catch { return 0; }
}
function saveBest(v: number): void {
  try { (globalThis as any).localStorage?.setItem?.("dc_best", String(v)); } catch {}
}
function getBestCombo(): number {
  try { return Math.max(0, parseInt((globalThis as any).localStorage?.getItem?.("dc_bc") || "0") || 0); } catch { return 0; }
}
function saveBestCombo(v: number): void {
  try { (globalThis as any).localStorage?.setItem?.("dc_bc", String(v)); } catch {}
}
function getDailyKey():string{const d=new Date();return`dc_db_${d.getFullYear()}-${d.getMonth()+1}-${d.getDate()}`;}
function getDailyBest():number{try{return Math.max(0,parseInt((globalThis as any).localStorage?.getItem?.(getDailyKey())||"0")||0);}catch{return 0;}}
function saveDailyBest(v:number):void{try{(globalThis as any).localStorage?.setItem?.(getDailyKey(),String(v));}catch{}}
function getLeaderboard(): number[] {
  try { return JSON.parse((globalThis as any).localStorage?.getItem?.("dc_lb") || "[]") || []; } catch { return []; }
}
function addToLeaderboard(score: number): number[] {
  if (score <= 0) return getLeaderboard();
  const lb = getLeaderboard(); lb.push(score); lb.sort((a, b) => b - a);
  const top = lb.slice(0, 3);
  try { (globalThis as any).localStorage?.setItem?.("dc_lb", JSON.stringify(top)); } catch {}
  return top;
}

// ─── Achievements ─────────────────────────────────────────────────────────────
interface Achievement{id:string;icon:string;title:string;desc:string;}
const ACHIEVEMENTS:Achievement[]=[
  {id:"first_100",  icon:"🏔", title:"Summit",        desc:"Reach 100m"},
  {id:"night",      icon:"🌙", title:"Night Climber",  desc:"Reach 400m"},
  {id:"space",      icon:"🚀", title:"Deep Space",     desc:"Reach 800m"},
  {id:"gem_5",      icon:"💎", title:"Gem Hoarder",    desc:"Collect 5 gems in one run"},
  {id:"boss_slayer",icon:"👹", title:"Boss Slayer",    desc:"Defeat a boss"},
  {id:"combo_king", icon:"⚡", title:"Combo King",     desc:"Reach x10 combo"},
  {id:"wormhole",   icon:"🌀", title:"Wormhole Rider", desc:"Use a wormhole"},
  {id:"coin_20",    icon:"🪙", title:"Coin Collector", desc:"Collect 20 coins"},
  {id:"stomp_10",   icon:"💀", title:"Stomper",        desc:"Stomp 10 enemies"},
  {id:"bomb_rider", icon:"💣", title:"Bomb Rider",     desc:"Land on a bomb platform"},
  {id:"lucky_duck", icon:"😅", title:"Lucky Duck",     desc:"5 near misses in one run"},
  {id:"speed_racer",icon:"⚡", title:"Speed Racer",    desc:"Grab the speed boost"},
  {id:"air_king",   icon:"✈",  title:"Air King",       desc:"Air chain ×5 stomps in one chain"},
];
function getAllTimeAch():string[]{try{return JSON.parse((globalThis as any).localStorage?.getItem?.("dc_ach")||"[]")||[];}catch{return[];}}
function saveAch(ids:string[]):void{try{(globalThis as any).localStorage?.setItem?.("dc_ach",JSON.stringify(ids));}catch{}}
// Module-level achievement cache — only reads localStorage once per session
let _achCache:Set<string>|null=null;
function getAchCache():Set<string>{if(!_achCache)_achCache=new Set(getAllTimeAch());return _achCache;}
function unlockAch(id:string,runNew:string[]):boolean{
  const cache=getAchCache();if(cache.has(id))return false;
  cache.add(id);saveAch([...cache]);runNew.push(id);return true;
}

// ─── Colour helpers ───────────────────────────────────────────────────────────
type C3 = readonly [number, number, number];
function lerp(a:number,b:number,t:number){return a+(b-a)*t;}
function lerpC(c1:C3,c2:C3,t:number):string{
  return `rgb(${Math.round(lerp(c1[0],c2[0],t))},${Math.round(lerp(c1[1],c2[1],t))},${Math.round(lerp(c1[2],c2[2],t))})`;
}
function hslToRgb(h:number,s:number,l:number):string{
  s/=100;l/=100;const k=(n:number)=>(n+h/30)%12,a=s*Math.min(l,1-l);
  const f=(n:number)=>Math.round((l-a*Math.max(-1,Math.min(k(n)-3,Math.min(9-k(n),1))))*255);
  return `rgb(${f(0)},${f(8)},${f(4)})`;
}

// ─── Zone palette ─────────────────────────────────────────────────────────────
const ZONES = [
  { at:0,   sky:[245,240,232]as C3,line:[160,200,232]as C3,lop:0.30,txt:[26,26,42]   as C3,dark:false,label:"☁  Sunrise",   plrBg:[39,192,99]  as C3,plrBrd:[30,158,80]  as C3 },
  { at:150, sky:[238,190,148]as C3,line:[200,120,70] as C3,lop:0.35,txt:[55,20,5]    as C3,dark:false,label:"🌅  Sunset",    plrBg:[245,130,48] as C3,plrBrd:[210,95,25]  as C3 },
  { at:400, sky:[18,28,60]   as C3,line:[55,95,135]  as C3,lop:0.45,txt:[200,228,255]as C3,dark:true, label:"🌙  Night Sky", plrBg:[210,235,255]as C3,plrBrd:[140,190,235]as C3 },
  { at:800, sky:[6,8,18]     as C3,line:[20,45,80]   as C3,lop:0.55,txt:[90,170,255] as C3,dark:true, label:"🚀  Deep Space",plrBg:[0,230,255]  as C3,plrBrd:[0,175,210] as C3 },
] as const;
const ZONE_FLASH_COLS=["rgba(255,210,80,0.38)","rgba(255,110,30,0.38)","rgba(60,100,220,0.38)","rgba(0,200,255,0.38)"];

function zoneIdx(score:number):number{ return score<150?0:score<400?1:score<800?2:3; }
function zoneColors(score:number){
  for(let i=0;i<ZONES.length-1;i++){
    if(score<ZONES[i+1].at){
      const t=Math.max(0,Math.min(1,(score-ZONES[i].at)/(ZONES[i+1].at-ZONES[i].at)));
      const z0=ZONES[i],z1=ZONES[i+1];
      return{sky:lerpC(z0.sky,z1.sky,t),line:lerpC(z0.line,z1.line,t),lop:lerp(z0.lop,z1.lop,t),
        txt:lerpC(z0.txt,z1.txt,t),dark:t>0.5?z1.dark:z0.dark,label:t>0.5?z1.label:z0.label,
        plrBg:lerpC(z0.plrBg,z1.plrBg,t),plrBrd:lerpC(z0.plrBrd,z1.plrBrd,t),
        rawSky:[...z0.sky].map((c,j)=>Math.round(lerp(c,(z1.sky as any)[j],t))) as [number,number,number]};
    }
  }
  const z=ZONES[ZONES.length-1];
  return{sky:lerpC(z.sky,z.sky,0),line:lerpC(z.line,z.line,0),lop:z.lop,txt:lerpC(z.txt,z.txt,0),
    dark:z.dark,label:z.label,plrBg:lerpC(z.plrBg,z.plrBg,0),plrBrd:lerpC(z.plrBrd,z.plrBrd,0),
    rawSky:[...z.sky] as [number,number,number]};
}

// ─── Platform & power-up types ────────────────────────────────────────────────
type PType="static"|"moving"|"spring"|"breakable"|"crumble"|"golden"|"rocket"|"ice"|"bomb"|"conveyor";
const PCOL:Record<PType,string>={static:"#2DC968",moving:"#4EAAF5",spring:"#F5D123",breakable:"#F05232",crumble:"#FF6C20",golden:"#FFD700",rocket:"#FF4420",ice:"#A8EEFF",bomb:"#FF3300",conveyor:"#FF8040"};
const PBORD:Record<PType,string>={static:"#1AA850",moving:"#2D88D8",spring:"#D4A800",breakable:"#C83018",crumble:"#CC4A10",golden:"#BF9B00",rocket:"#CC2010",ice:"#60C8E8",bomb:"#AA1100",conveyor:"#CC4A10"};
const P_GREEN="#27C063"; const P_DARK="#1E9E50";

type PUType="jetpack"|"shield"|"magnet"|"boots"|"heart"|"star"|"speed";
const PU_ICON:Record<PUType,string>={jetpack:"🚀",shield:"🛡️",magnet:"🧲",boots:"🥾",heart:"❤️",star:"⭐",speed:"⚡"};
const PU_COL:Record<PUType,string>={jetpack:"#FF8C00",shield:"#00A0FF",magnet:"#C050FF",boots:"#FFB020",heart:"#FF4455",star:"#FFD700",speed:"#00E8FF"};
const PU_GLW:Record<PUType,string>={jetpack:"rgba(255,140,0,0.28)",shield:"rgba(0,160,255,0.28)",magnet:"rgba(192,80,255,0.28)",boots:"rgba(255,176,32,0.28)",heart:"rgba(255,68,85,0.32)",star:"rgba(255,215,0,0.40)",speed:"rgba(0,232,255,0.35)"};

// ─── City silhouette (pre-computed) ──────────────────────────────────────────
const CITY_BLDGS = Array.from({length:20},(_,i)=>({
  x:(i*(SW/11))+Math.sin(i*2.73)*18-24,
  w:16+Math.abs(Math.sin(i*1.73))*38,
  h:42+Math.abs(Math.cos(i*2.11))*168,
}));

// ─── Types ───────────────────────────────────────────────────────────────────
type Phase="menu"|"play"|"dead";
type EnemyType="bird"|"ghost"|"ufo"|"asteroid"|"bat";

interface Plat     {id:number;x:number;y:number;w:number;type:PType;broken:boolean;ox:number;dir:number;range:number;sq:number;crumbleT:number;ringT:number;}
interface Particle {x:number;y:number;vx:number;vy:number;life:number;color:string;r:number;}
interface TrailPt  {x:number;y:number;}
interface Cloud    {id:number;x:number;y:number;w:number;h:number;par:number;op:number;}
interface Coin     {id:number;x:number;y:number;collected:boolean;popT:number;}
interface PU       {id:number;x:number;y:number;tp:PUType;col:boolean;bt:number;}
interface Planet   {id:number;x:number;y:number;r:number;c:string;ring:boolean;par:number;}
interface SShoot   {id:number;sx:number;sy:number;vx:number;vy:number;life:number;}
interface Enemy    {id:number;x:number;y:number;vx:number;vy:number;type:EnemyType;wt:number;dead:boolean;}
interface WarpStar {id:number;x:number;y:number;vy:number;r:number;}
interface TextPop  {id:number;x:number;y:number;vy:number;life:number;text:string;color:string;}
interface WeatherP {id:number;x:number;y:number;vy:number;vx:number;}
interface Gem      {id:number;x:number;y:number;collected:boolean;popT:number;}
interface Boss     {id:number;x:number;y:number;vx:number;vy:number;hp:number;maxHp:number;ang:number;t:number;dead:boolean;deathT:number;hitT:number;enraged:boolean;}
interface Wormhole {id:number;x:number;y:number;r:number;t:number;used:boolean;}

interface GS {
  px:number;py:number;pvx:number;pvy:number;psx:number;psy:number;facing:number;eyeY:number;
  scrollY:number;minY:number;startY:number;
  score:number;best:number;bestCombo:number;displayScore:number;
  combo:number;comboT:number;maxCombo:number;coinsCollected:number;enemiesDefeated:number;
  coinStreak:number;coinStreakT:number;
  lives:number;jetpackT:number;magnetT:number;bootsT:number;rocketFlashT:number;starT:number;speedT:number;shielded:boolean;
  canDJump:boolean;invincT:number;djumpFlashT:number;stompFlashT:number;airStomps:number;
  milestoneText:string;milestoneT:number;lastMilestone:number;
  lastZoneIdx:number;zoneFlashT:number;zoneFlashCol:string;
  windF:number;windT:number;windNextT:number;
  ssTimer:number;
  plats:Plat[];parts:Particle[];trail:TrailPt[];clouds:Cloud[];
  coins:Coin[];powerUps:PU[];planets:Planet[];shootStars:SShoot[];enemies:Enemy[];
  warpStars:WarpStar[];textPops:TextPop[];weatherParts:WeatherP[];gems:Gem[];bosses:Boss[];wormholes:Wormhole[];
  gemsCollected:number;nearMissCooldown:number;nearMissCount:number;bossKills:number;
  iceHits:number;wormholeUsed:boolean;bombRidden:boolean;achNewRun:string[];achPopT:number;achPopText:string;
  savedBest:number;newBestFlashed:boolean;
  conveyorDirT:number;conveyorDir:number;
  phase:Phase;input:number;
  shakeX:number;shakeY:number;shakeT:number;
  pid:number;
}

// ─── Factories ────────────────────────────────────────────────────────────────
function pickType(score:number):PType{
  const r=Math.random();
  if(score<80) return r<0.06?"moving":"static";
  if(score<200) return r<0.09?"spring":r<0.24?"moving":r<0.37?"breakable":"static";
  if(score<400) return r<0.10?"spring":r<0.25?"moving":r<0.40?"breakable":r<0.50?"crumble":r<0.55?"golden":r<0.59?"ice":r<0.62?"bomb":r<0.65?"conveyor":"static";
  if(score<700) return r<0.11?"spring":r<0.26?"moving":r<0.43?"breakable":r<0.55?"crumble":r<0.60?"golden":r<0.63?"rocket":r<0.67?"ice":r<0.69?"bomb":r<0.72?"conveyor":"static";
  return       r<0.12?"spring":r<0.26?"moving":r<0.44?"breakable":r<0.57?"crumble":r<0.61?"golden":r<0.65?"rocket":r<0.69?"ice":r<0.71?"bomb":r<0.74?"conveyor":"static";
}
function mkPlat(gs:GS,y:number,sc:number):Plat{
  // Width narrows with altitude for difficulty; gap is capped for fairness
  const w=Math.max(46,92-sc*0.030),x=Math.random()*(SW-w);
  return{id:gs.pid++,x,y,w,type:pickType(sc),broken:false,ox:x,dir:Math.random()<0.5?1:-1,range:38+Math.random()*55,sq:0,crumbleT:-1,ringT:0};
}
function mkStat(gs:GS,y:number,x:number,w:number):Plat{
  return{id:gs.pid++,x,y,w,type:"static",broken:false,ox:x,dir:1,range:0,sq:0,crumbleT:-1,ringT:0};
}
function mkCloud(gs:GS,wy:number):Cloud{
  return{id:gs.pid++,x:Math.random()*(SW-120),y:wy,w:90+Math.random()*100,h:28+Math.random()*24,par:0.22+Math.random()*0.18,op:0.55+Math.random()*0.35};
}
function mkPU(gs:GS,y:number,tp:PUType):PU{ return{id:gs.pid++,x:20+Math.random()*(SW-60),y,tp,col:false,bt:0}; }
function mkPlanet(gs:GS,wy:number):Planet{
  const cols=["#9B5DE5","#F15BB5","#00BBF9","#FF6B6B","#5BC0EB","#FF9F1C"];
  return{id:gs.pid++,x:30+Math.random()*(SW-80),y:wy,r:32+Math.random()*44,c:cols[Math.floor(Math.random()*cols.length)],ring:Math.random()<0.6,par:0.10+Math.random()*0.12};
}
function getEType(score:number):EnemyType{
  const r=Math.random();
  if(score<180) return"bird";
  if(score<350) return r<0.55?"bird":r<0.80?"ghost":"bat";
  if(score<650) return r<0.20?"bird":r<0.55?"ghost":r<0.75?"bat":"ufo";
  if(score<850) return r<0.20?"bat":r<0.45?"ghost":r<0.75?"ufo":"asteroid";
  return r<0.35?"ufo":r<0.55?"bat":"asteroid";
}
function mkEnemy(gs:GS,y:number,score:number):Enemy{
  const tp=getEType(score);
  const ml=Math.random()<0.5;
  // Bats hover near centre, don't need a start X that's off-screen
  const startX=tp==="bat"?20+Math.random()*(SW-70):(ml?SW+30:-65);
  const spd=tp==="bird"?200+Math.random()*110:tp==="asteroid"?160+Math.random()*90:tp==="bat"?72+Math.random()*44:100+Math.random()*70;
  return{id:gs.pid++,x:startX,y:y+10+Math.random()*55,vx:(ml?-1:1)*spd,vy:tp==="asteroid"?40+Math.random()*70:0,type:tp,wt:Math.random()*6,dead:false};
}

function mkBoss(gs:GS,y:number):Boss{
  return{id:gs.pid++,x:20+Math.random()*(SW-110),y,vx:(Math.random()<0.5?1:-1)*88,vy:0,hp:3,maxHp:3,ang:0,t:0,dead:false,deathT:0,hitT:0,enraged:false};
}
function mkWormhole(gs:GS,y:number):Wormhole{
  return{id:gs.pid++,x:44+Math.random()*(SW-88),y,r:32,t:0,used:false};
}

// ─── Coin cluster spawner ─────────────────────────────────────────────────────
function spawnCluster(gs:GS,cx:number,y:number){
  const count=3+Math.floor(Math.random()*3); // 3, 4, or 5
  const arc=Math.random()<0.55;
  for(let i=0;i<count;i++){
    const t=count>1?i/(count-1):0.5;
    const dx=(t-0.5)*count*18;
    const dy=arc?-Math.sin(t*Math.PI)*24:0;
    gs.coins.push({id:gs.pid++,x:Math.max(14,Math.min(SW-14,cx+dx)),y:y-28+dy,collected:false,popT:0});
  }
}

const MILESTONES=[100,250,500,750,1000,1500,2000,3000];
const COMBO_TIERS=[5,10,15,20,30];
const PU_TYPES:PUType[]=["jetpack","shield","magnet","boots","star","speed"]; // heart spawns separately; star/speed are rarer

function mkGS(best:number):GS{
  const startY=SH*0.70;
  const gs:GS={
    px:SW/2-PW/2,py:startY,pvx:0,pvy:JUMP_VEL,psx:1,psy:1,facing:1,eyeY:0,
    scrollY:0,minY:startY,startY,score:0,best,bestCombo:getBestCombo(),displayScore:0,
    combo:0,comboT:0,maxCombo:0,coinsCollected:0,enemiesDefeated:0,
    coinStreak:0,coinStreakT:0,
    lives:3,jetpackT:0,magnetT:0,bootsT:0,rocketFlashT:0,starT:0,speedT:0,shielded:false,
    canDJump:true,invincT:0,djumpFlashT:0,stompFlashT:0,airStomps:0,
    milestoneText:"",milestoneT:0,lastMilestone:0,
    lastZoneIdx:-1,zoneFlashT:0,zoneFlashCol:"",
    windF:0,windT:0,windNextT:12,
    ssTimer:9,
    plats:[],parts:[],trail:[],clouds:[],coins:[],powerUps:[],planets:[],shootStars:[],enemies:[],
    warpStars:[],textPops:[],weatherParts:[],gems:[],bosses:[],wormholes:[],
    gemsCollected:0,nearMissCooldown:0,nearMissCount:0,bossKills:0,
    iceHits:0,wormholeUsed:false,bombRidden:false,achNewRun:[],achPopT:0,achPopText:"",
    savedBest:best,newBestFlashed:false,
    conveyorDirT:0,conveyorDir:1,
    phase:"menu",input:0,shakeX:0,shakeY:0,shakeT:0,pid:0,
  };
  gs.plats.push(mkStat(gs,startY+PH,SW/2-52,104));
  let y=startY;
  for(let i=0;i<6;i++){y-=58+Math.random()*26;const w=80+Math.random()*22,cx=SW*0.2+Math.random()*SW*0.6;gs.plats.push(mkStat(gs,y,cx-w/2,w));}
  for(let i=0;i<18;i++){y-=70+Math.random()*38;gs.plats.push(mkPlat(gs,y,0));if(Math.random()<0.32)gs.coins.push({id:gs.pid++,x:gs.plats[gs.plats.length-1].x+20+Math.random()*30,y:y-28,collected:false,popT:0});}
  let cy=startY;
  for(let i=0;i<8;i++){cy-=SH*(0.35+Math.random()*0.3);gs.clouds.push(mkCloud(gs,cy));}
  return gs;
}

// ─── Particles & helpers ──────────────────────────────────────────────────────
function emit(gs:GS,cx:number,cy:number,col:string,n:number){
  for(let i=0;i<n;i++){
    const ang=(Math.PI*2*i)/n+Math.random()*0.5,spd=65+Math.random()*190;
    gs.parts.push({x:cx,y:cy,color:col,vx:Math.cos(ang)*spd,vy:Math.sin(ang)*spd-85,life:1,r:3+Math.random()*5});
  }
}
function pop(gs:GS,x:number,y:number,text:string,color:string){
  gs.textPops.push({id:gs.pid++,x,y,vy:-96,life:1,text,color});
}
function comboCol(n:number){return n>=20?"#FF0000":n>=15?"#FF2200":n>=10?"#FF9900":n>=5?"#F5D123":"#50F0AA";}
function comboMult(n:number){return n>=20?3:n>=15?2.5:n>=10?2:n>=5?1.5:1;}

// ─── Update ───────────────────────────────────────────────────────────────────
function update(gs:GS,dt:number,now:number){
  if(gs.phase!=="play") return;

  // Wind force (Night + Space zones)
  if(gs.score>350){
    if(gs.windT>0){
      gs.windT-=dt;
      if(gs.windT<=0){gs.windF=0;gs.windNextT=9+Math.random()*10;}
    } else {
      gs.windNextT-=dt;
      if(gs.windNextT<=0){gs.windF=(Math.random()<0.5?1:-1)*(52+Math.random()*44);gs.windT=2.2+Math.random()*2.4;}
    }
  } else { gs.windF=0;gs.windT=0; }

  // Power-up timers
  if(gs.speedT>0){gs.speedT=Math.max(0,gs.speedT-dt);if(Math.random()<0.4)emit(gs,gs.px+PW/2,gs.py+PH,"#00E8FF",1);}

  // Player horizontal (wind push when active, not on jetpack)
  const curSpd=PLAYER_SPD*(gs.speedT>0?1.65:1);
  gs.pvx=gs.input*curSpd+(gs.windT>0&&gs.jetpackT<=0?gs.windF*0.38:0);
  if(gs.conveyorDirT>0){gs.conveyorDirT=Math.max(0,gs.conveyorDirT-dt);gs.pvx+=gs.conveyorDir*PLAYER_SPD*1.1*(gs.conveyorDirT/0.5);}
  if(gs.input!==0) gs.facing=gs.input;

  // Vertical
  if(gs.jetpackT>0){
    gs.jetpackT-=dt;
    gs.pvy+=(JETPACK_VY-gs.pvy)*Math.min(1,dt*5);
    if(Math.random()<0.6) emit(gs,gs.px+PW/2,gs.py+PH+4,Math.random()<0.5?"#FF8800":"#FFE020",1);
  } else {
    gs.pvy+=GRAVITY*(gs.pvy>0?FALL_MULT:1)*dt;
    gs.pvy=Math.min(gs.pvy,1450);
  }
  if(gs.bootsT>0)      gs.bootsT-=dt;
  if(gs.rocketFlashT>0)gs.rocketFlashT-=dt;
  if(gs.starT>0){gs.starT=Math.max(0,gs.starT-dt);gs.invincT=Math.max(gs.invincT,gs.starT>0?gs.starT+0.05:0);}

  gs.px+=gs.pvx*dt;gs.py+=gs.pvy*dt;
  if(gs.px+PW<0){gs.px=SW;if(gs.pvy>80){gs.pvy=Math.max(gs.pvy-90,gs.pvy*0.86);emit(gs,SW,gs.py+PH/2,"#80EEFF",7);}}
  if(gs.px>SW){gs.px=-PW;if(gs.pvy>80){gs.pvy=Math.max(gs.pvy-90,gs.pvy*0.86);emit(gs,0,gs.py+PH/2,"#80EEFF",7);}}
  gs.eyeY+=((gs.pvy<-180?-2.5:gs.pvy>180?2.5:0)-gs.eyeY)*Math.min(1,dt*8);

  if(!gs.trail.length||Math.hypot(gs.px-gs.trail[0].x,gs.py-gs.trail[0].y)>5){
    gs.trail.unshift({x:gs.px+PW/2,y:gs.py+PH/2});
    if(gs.trail.length>TRAIL_LEN) gs.trail.pop();
  }

  // Platform collision
  if(gs.pvy>0){
    for(const p of gs.plats){
      if(p.broken) continue;
      const feet=gs.py+PH;
      if(feet>=p.y&&feet<=p.y+PLH+14&&gs.px+PW>p.x+4&&gs.px<p.x+p.w-4){
        gs.py=p.y-PH;gs.airStomps=0; // reset air-stomp chain on platform landing
        // Jump velocity based on platform type
        const isRocket=p.type==="rocket",isSpring=p.type==="spring",isIce=p.type==="ice",bootBoost=gs.bootsT>0&&!isSpring&&!isRocket;
        if(isIce){gs.pvx=gs.pvx*(1.55+Math.random()*0.35)+(Math.random()-0.5)*30;gs.iceHits++;} // slippery slide
        if(p.type==="conveyor"){gs.conveyorDirT=0.55;gs.conveyorDir=p.dir;emit(gs,p.x+p.w/2,p.y,"#FF8040",8);}
        gs.pvy=isRocket?ROCKET_VEL:isSpring?SPRING_VEL:bootBoost?Math.round(SPRING_VEL*0.82):JUMP_VEL;
        gs.psx=isRocket?0.70:isSpring?1.58:bootBoost?1.52:1.40;
        gs.psy=isRocket?1.45:isSpring?0.50:bootBoost?0.55:0.63;
        p.sq=isRocket?0.80:isSpring?0.65:0.45;
        p.ringT=1.0;
        gs.canDJump=true;gs.combo++;gs.comboT=2.8;
        if(gs.combo>gs.maxCombo) gs.maxCombo=gs.combo;
        if(gs.combo>gs.bestCombo){gs.bestCombo=gs.combo;saveBestCombo(gs.bestCombo);}
        for(const ct of COMBO_TIERS){if(gs.combo===ct){emit(gs,gs.px+PW/2,gs.py,comboCol(ct),20);gs.shakeT=ct>=20?0.28:ct>=15?0.20:ct>=10?0.16:0.12;break;}}
        emit(gs,p.x+p.w/2,p.y,PCOL[p.type],isRocket?22:isSpring?14:6);
        if(p.type==="breakable") p.broken=true;
        if(p.type==="bomb"){
          p.broken=true;gs.pvy=SPRING_VEL*1.18;gs.psx=0.72;gs.psy=1.52;gs.shakeT=0.32;
          emit(gs,p.x+p.w/2,p.y,"#FF3300",24);emit(gs,p.x+p.w/2,p.y,"#FF9900",14);
          pop(gs,p.x+p.w/2,p.y-30,"💣 BOOM!","#FF6600");
          if(!gs.bombRidden){gs.bombRidden=true;if(unlockAch("bomb_rider",gs.achNewRun)){const a=ACHIEVEMENTS.find(x=>x.id==="bomb_rider");if(a){gs.achPopText=`${a.icon} ${a.title}`;gs.achPopT=3.0;}}}
        }
        if(p.type==="crumble"&&p.crumbleT<0) p.crumbleT=0.52;
        if(p.type==="golden"){
          const bonus=25+Math.round(comboMult(gs.combo)*5);
          gs.score+=bonus;emit(gs,p.x+p.w/2,p.y,"#FFD700",18);
          pop(gs,p.x+p.w/2,p.y-26,`✦ +${bonus} BONUS!`,"#FFD700");gs.shakeT=0.12;
        }
        if(p.type==="rocket"){
          gs.rocketFlashT=0.80;
          pop(gs,gs.px+PW/2,gs.py-20,"🚀 ROCKET LAUNCH!","#FF6820");gs.shakeT=0.22;
          // Reset combo streak—this is a momentum tool not a skill reward
        }
        break;
      }
    }
  }

  // Crumble timers
  for(const p of gs.plats){
    if(p.crumbleT>0){
      p.crumbleT-=dt;
      if(p.crumbleT<=0){p.broken=true;emit(gs,p.x+p.w/2,p.y,"#FF6C20",10);}
    }
  }

  // Coin streak timer
  if(gs.coinStreakT>0){gs.coinStreakT-=dt;if(gs.coinStreakT<=0)gs.coinStreak=0;}

  // Magnet + coins
  const pcx=gs.px+PW/2,pcy=gs.py+PH/2;
  if(gs.magnetT>0){
    gs.magnetT-=dt;
    for(const c of gs.coins){if(c.collected)continue;const dx=pcx-c.x,dy=pcy-c.y,d=Math.hypot(dx,dy);if(d<170&&d>0){const spd=210*(1-d/170)+60;c.x+=dx/d*spd*dt;c.y+=dy/d*spd*dt;}}
  }
  for(const c of gs.coins){
    if(c.collected){c.popT=Math.max(0,c.popT-dt*3);continue;}
    if(Math.hypot(pcx-c.x,pcy-c.y)<22){
      c.collected=true;c.popT=1;gs.coinsCollected++;
      const bonus=Math.round(5*comboMult(gs.combo));
      gs.score+=bonus;emit(gs,c.x,c.y,"#F5D123",5);pop(gs,c.x,c.y-10,`+${bonus}`,"#F5D123");
      // Coin streak
      gs.coinStreak++;gs.coinStreakT=2.2;
      if(gs.coinStreak===3){gs.score+=5;pop(gs,c.x,c.y-28,"×3 STREAK! +5","#FFD700");}
      else if(gs.coinStreak===5){gs.score+=12;pop(gs,c.x,c.y-28,"🔥 ×5 STREAK! +12","#FF9900");emit(gs,c.x,c.y,"#FFD700",12);}
      else if(gs.coinStreak===10){gs.score+=28;pop(gs,c.x,c.y-28,"⚡ ×10 STREAK! +28","#FF4400");emit(gs,c.x,c.y,"#FF8800",22);gs.shakeT=0.15;}
    }
  }

  // Gems
  for(const gm of gs.gems){
    if(gm.collected){gm.popT=Math.max(0,gm.popT-dt*3);continue;}
    if(gs.magnetT>0){const dx=pcx-gm.x,dy=pcy-gm.y,d=Math.hypot(dx,dy);if(d<180&&d>0){const spd=220*(1-d/180)+70;gm.x+=dx/d*spd*dt;gm.y+=dy/d*spd*dt;}}
    if(Math.hypot(pcx-gm.x,pcy-gm.y)<24){
      gm.collected=true;gm.popT=1;gs.gemsCollected++;
      const bonus=Math.round(20*comboMult(gs.combo));
      gs.score+=bonus;emit(gs,gm.x,gm.y,"#C060FF",10);pop(gs,gm.x,gm.y-14,`💎 +${bonus}`,"#D080FF");gs.shakeT=0.08;
    }
  }

  // Bosses
  for(const boss of gs.bosses){
    if(boss.dead){boss.deathT=Math.max(0,boss.deathT-dt);continue;}
    if(boss.hitT>0) boss.hitT=Math.max(0,boss.hitT-dt);
    boss.t+=dt;boss.ang=(boss.ang+dt*2.2)%(Math.PI*2);
    boss.x+=boss.vx*dt;
    if(boss.x<12||boss.x>SW-100){boss.vx*=-1;}
    const divePeriod=boss.enraged?3.2:5;
    const divePhase=boss.t%divePeriod;
    const diveVy=boss.enraged?340:220;
    if(divePhase<1.2){boss.vy+=(diveVy-boss.vy)*dt*(boss.enraged?5:3.5);}else{boss.vy+=(0-boss.vy)*dt*3;}
    boss.y+=boss.vy*dt;
    const minBossY=gs.scrollY-250;if(boss.y<minBossY)boss.y=minBossY;
    if(gs.pvy>0){
      if(Math.hypot(gs.px+PW/2-(boss.x+40),gs.py+PH-(boss.y+40))<46){
        boss.hp--;boss.hitT=0.45;gs.pvy=SPRING_VEL*0.88;gs.canDJump=true;gs.shakeT=0.25;
        gs.airStomps++;
        emit(gs,boss.x+40,boss.y+40,"#FF6633",16);
        if(boss.hp<=0){
          boss.dead=true;boss.deathT=1.2;gs.bossKills++;gs.enemiesDefeated++;
          const bonus=Math.round(280*comboMult(gs.combo));gs.score+=bonus;
          emit(gs,boss.x+40,boss.y+40,"#FFD700",36);emit(gs,boss.x+40,boss.y+40,"#FF4400",20);
          gs.shakeT=0.6;pop(gs,boss.x+40,boss.y-28,`👹 BOSS DOWN! +${bonus}`,"#FF8800");
          for(let gi=0;gi<3;gi++) gs.gems.push({id:gs.pid++,x:boss.x+40+(gi-1)*28,y:boss.y+20,collected:false,popT:0});
          for(let ci=0;ci<5;ci++) gs.coins.push({id:gs.pid++,x:boss.x+40+(ci-2)*20,y:boss.y+30,collected:false,popT:0});
        }else{
          if(!boss.enraged&&boss.hp===1){
            boss.enraged=true;boss.vx*=1.85;
            pop(gs,boss.x+40,boss.y-44,"🔥 ENRAGED!","#FF2200");gs.shakeT=0.4;
            emit(gs,boss.x+40,boss.y+40,"#FF0000",22);
          } else {pop(gs,boss.x+40,boss.y-24,`💥 ${boss.hp} HP LEFT`,"#FF6633");}
        }
      }
    }
    if(!boss.dead&&gs.invincT<=0&&gs.starT<=0){
      if(Math.hypot(gs.px+PW/2-(boss.x+40),gs.py+PH/2-(boss.y+40))<44&&!(gs.pvy>0&&gs.py+PH<boss.y+28)){
        if(gs.shielded){gs.shielded=false;gs.invincT=2.0;gs.airStomps=0;emit(gs,boss.x+40,boss.y+40,"#50A0FF",14);}
        else if(gs.lives>1){gs.lives--;gs.pvy=JUMP_VEL*1.1;gs.invincT=2.5;gs.shakeT=0.4;gs.combo=0;gs.comboT=0;gs.airStomps=0;}
        else{gs.phase="dead";gs.shakeT=0.7;emit(gs,gs.px+PW/2,gs.py,P_GREEN,24);}
      }
    }
  }

  // Wormholes
  for(const wh of gs.wormholes){
    if(wh.used)continue;
    wh.t+=dt;
    if(Math.hypot(pcx-wh.x,pcy-wh.y)<wh.r+10){
      wh.used=true;gs.wormholeUsed=true;
      const dist=Math.round(180+Math.random()*80);gs.py-=dist;gs.scrollY-=dist;
      const bonus=Math.round(dist*0.55);gs.score+=bonus;
      emit(gs,wh.x,wh.y,"#A040FF",30);emit(gs,wh.x,wh.y,"#00CCFF",18);
      pop(gs,wh.x,wh.y-44,`🌀 WORMHOLE! +${bonus}`,"#C060FF");gs.shakeT=0.45;gs.pvy=JUMP_VEL;
    }
  }

  // Power-ups
  for(const pu of gs.powerUps){
    if(pu.col){pu.bt=Math.max(0,pu.bt-dt);continue;}
    pu.bt=(pu.bt+dt*2)%(Math.PI*2);
    if(Math.hypot(pcx-pu.x,pcy-pu.y)<30){
      pu.col=true;
      if(pu.tp==="jetpack"){gs.jetpackT=4.0;emit(gs,pu.x,pu.y,"#FF8800",18);pop(gs,pu.x,pu.y-22,"JETPACK!","#FF8C00");}
      else if(pu.tp==="shield"){gs.shielded=true;emit(gs,pu.x,pu.y,"#50A0FF",18);pop(gs,pu.x,pu.y-22,"SHIELD!","#00A0FF");}
      else if(pu.tp==="magnet"){gs.magnetT=6.0;emit(gs,pu.x,pu.y,"#C050FF",18);pop(gs,pu.x,pu.y-22,"MAGNET!","#C050FF");}
      else if(pu.tp==="boots"){gs.bootsT=5.0;emit(gs,pu.x,pu.y,"#FFB020",18);pop(gs,pu.x,pu.y-22,"BOUNCE BOOTS!","#FFB020");}
      else if(pu.tp==="heart"){
        if(gs.lives<3){gs.lives++;emit(gs,pu.x,pu.y,"#FF4455",24);pop(gs,pu.x,pu.y-22,"❤️ EXTRA LIFE!","#FF4455");gs.shakeT=0.22;}
        else{gs.score+=30;pop(gs,pu.x,pu.y-22,"❤️ +30 FULL HEART","#FF4455");emit(gs,pu.x,pu.y,"#FF8899",12);}
      }
      else if(pu.tp==="star"){
        gs.starT=3.5;gs.invincT=3.6;
        for(const e of gs.enemies){if(!e.dead){e.dead=true;emit(gs,e.x+18,e.y,"#FFD700",14);for(let d=0;d<2;d++)gs.coins.push({id:gs.pid++,x:e.x+18+(Math.random()-0.5)*24,y:e.y,collected:false,popT:0});}}
        emit(gs,pu.x,pu.y,"#FFD700",32);pop(gs,pu.x,pu.y-24,"⭐ STAR POWER!","#FFD700");gs.shakeT=0.35;
      }
      else if(pu.tp==="speed"){gs.speedT=4.0;emit(gs,pu.x,pu.y,"#00E8FF",22);pop(gs,pu.x,pu.y-22,"⚡ SPEED BOOST!","#00E8FF");gs.shakeT=0.18;if(unlockAch("speed_racer",gs.achNewRun)){const a=ACHIEVEMENTS.find(x=>x.id==="speed_racer");if(a){gs.achPopText=`${a.icon} ${a.title}`;gs.achPopT=3.0;}}}
    }
  }

  // Enemies move
  for(const e of gs.enemies){
    if(e.dead) continue;
    if(e.type==="bat"){
      // Bat slowly homes toward player's X; sine-wave vertical drift
      const dx=gs.px+PW/2-(e.x+19);
      e.vx+=(dx>0?1:-1)*120*dt; // accelerate toward player
      e.vx=Math.max(-130,Math.min(130,e.vx)); // cap speed
      e.x+=e.vx*dt;e.wt+=dt;
      e.y+=Math.sin(e.wt*1.8)*42*dt;
    } else {
      e.x+=e.vx*dt;e.y+=e.vy*dt;e.wt+=dt;
      if(e.type==="ghost"||e.type==="ufo") e.y+=Math.sin(e.wt*2.5)*28*dt;
    }
  }

  // Enemy collision
  if(gs.invincT<=0){
    const EW=36,EH=30;
    for(const e of gs.enemies){
      if(e.dead) continue;
      const overX=gs.px+PW>e.x+5&&gs.px<e.x+EW-5;if(!overX) continue;
      const feet=gs.py+PH;
      if(gs.pvy>0&&feet>=e.y-2&&feet<=e.y+EH*0.42){
        e.dead=true;gs.pvy=JUMP_VEL*0.88;gs.py=e.y-PH;
        gs.psx=1.45;gs.psy=0.60;gs.canDJump=true;
        gs.combo++;gs.comboT=2.8;gs.stompFlashT=0.65;gs.enemiesDefeated++;gs.airStomps++;
        const airBonus=gs.airStomps>=2?gs.airStomps*20:15;gs.score+=airBonus;
        if(gs.airStomps>=5&&unlockAch("air_king",gs.achNewRun)){const a=ACHIEVEMENTS.find(x=>x.id==="air_king");if(a){gs.achPopText=`${a.icon} ${a.title}`;gs.achPopT=3.0;}}
        if(gs.airStomps>=2) pop(gs,e.x,e.y-20,`✈ AIR CHAIN ×${gs.airStomps}!`,"#FF88AA");
        if(gs.combo>gs.maxCombo) gs.maxCombo=gs.combo;
        if(gs.combo>gs.bestCombo){gs.bestCombo=gs.combo;saveBestCombo(gs.bestCombo);}
        for(let d=0;d<2;d++) gs.coins.push({id:gs.pid++,x:e.x+EW/2+(Math.random()-0.5)*32,y:e.y+(Math.random()-0.5)*16,collected:false,popT:0});
        emit(gs,e.x+EW/2,e.y,"#FF8844",20);
        if(gs.airStomps<2) pop(gs,e.x+EW/2,e.y-14,`+${airBonus} STOMP!`,"#FF8844");
        gs.shakeT=0.10;break;
      }
      if(feet>e.y+EH*0.35&&gs.py<e.y+EH-5){
        e.dead=true;emit(gs,e.x+EW/2,e.y,"#FF5533",14);
        if(gs.shielded){gs.shielded=false;gs.invincT=1.5;gs.airStomps=0;gs.enemiesDefeated++;}
        else if(gs.lives>1){gs.lives--;gs.pvy=JUMP_VEL*1.1;gs.invincT=2.2;gs.shakeT=0.4;gs.combo=0;gs.comboT=0;gs.airStomps=0;}
        else{gs.phase="dead";gs.shakeT=0.7;emit(gs,gs.px+PW/2,gs.py,P_GREEN,24);}
        break;
      }
    }
  }

  // Near-miss bonus
  if(gs.nearMissCooldown>0){gs.nearMissCooldown-=dt;}else{
    const EW=36,EH=30;
    for(const e of gs.enemies){
      if(e.dead) continue;
      const d=Math.hypot(gs.px+PW/2-e.x-EW/2,gs.py+PH/2-e.y-EH/2);
      if(d>24&&d<60){gs.score+=5;gs.nearMissCount++;pop(gs,gs.px+PW/2,gs.py-26,"😅 CLOSE CALL! +5","#FFCC44");emit(gs,gs.px+PW/2,gs.py+PH/2,"#FFCC44",8);gs.nearMissCooldown=2.2;break;}
    }
  }

  // Camera — smooth lead, hard clamp so player never exits top
  const targetY=gs.py-SH*CAM_LEAD;
  if(targetY<gs.scrollY){
    gs.scrollY+=(targetY-gs.scrollY)*Math.min(1,dt*14);
    if(gs.scrollY>targetY+8) gs.scrollY=targetY; // snap if lagging too far
  }

  // Score + milestones + best
  if(gs.py<gs.minY) gs.minY=gs.py;
  gs.score=Math.max(gs.score,Math.floor((gs.startY-gs.minY)/5));
  if(gs.score>gs.best){gs.best=gs.score;saveBest(gs.best);}
  if(!gs.newBestFlashed&&gs.savedBest>0&&gs.score>gs.savedBest){
    gs.newBestFlashed=true;
    pop(gs,SW/2-50,gs.scrollY+SH*0.42,"✦ NEW BEST! ✦","#FFD700");
    emit(gs,SW/2,gs.scrollY+SH*0.42,"#FFD700",28);gs.shakeT=0.25;
  }
  for(const m of MILESTONES){if(gs.score>=m&&m>gs.lastMilestone){gs.lastMilestone=m;gs.milestoneText=`${m} m`;gs.milestoneT=2.2;const fc=["#FF4444","#FFD700","#00FF88","#FF88FF","#00CCFF","#FF8844","#AAFFAA","#FF44FF"][MILESTONES.indexOf(m)%8];for(let fx=0;fx<5;fx++)emit(gs,SW*0.1+fx*(SW*0.2),gs.py-SH*0.08,fc,18);}}

  // Achievement checks
  (function checkAch(){
    function tryUnlock(id:string){
      if(unlockAch(id,gs.achNewRun)){
        const a=ACHIEVEMENTS.find(x=>x.id===id);
        if(a){gs.achPopText=`${a.icon} ${a.title}`;gs.achPopT=3.0;}
      }
    }
    if(gs.score>=100)  tryUnlock("first_100");
    if(gs.score>=400)  tryUnlock("night");
    if(gs.score>=800)  tryUnlock("space");
    if(gs.gemsCollected>=5)  tryUnlock("gem_5");
    if(gs.bossKills>=1)      tryUnlock("boss_slayer");
    if(gs.combo>=10)         tryUnlock("combo_king");
    if(gs.wormholeUsed)      tryUnlock("wormhole");
    if(gs.coinsCollected>=20)tryUnlock("coin_20");
    if(gs.enemiesDefeated>=10)tryUnlock("stomp_10");
    if(gs.nearMissCount>=5)  tryUnlock("lucky_duck");
  })();
  if(gs.achPopT>0) gs.achPopT-=dt;

  // Animated display score
  gs.displayScore=Math.min(gs.score,gs.displayScore+(gs.score-gs.displayScore)*Math.min(1,dt*10));

  // Zone flash
  const cz=zoneIdx(gs.score);
  if(cz!==gs.lastZoneIdx){if(gs.lastZoneIdx>=0){gs.zoneFlashT=0.75;gs.zoneFlashCol=ZONE_FLASH_COLS[cz];}gs.lastZoneIdx=cz;}

  // World generation — fixed gap cap ensures platforms are always reachable
  const topGenY=gs.scrollY-SH*0.55;
  let topY=gs.plats.length?Math.min(...gs.plats.map(p=>p.y)):gs.startY;let safety=0;
  while(topY>topGenY&&safety++<25){
    // Gap grows slightly with score (harder), but never exceeds MAX_GAP for fairness
    const baseGap=Math.min(92,68+gs.score*0.022);
    const gap=Math.min(MAX_GAP,baseGap+Math.random()*28);
    topY-=gap;
    const plat=mkPlat(gs,topY,gs.score);
    gs.plats.push(plat);
    // Coin spawn: 40% chance of cluster, 60% single coin
    if(Math.random()<0.34){
      if(Math.random()<0.42) spawnCluster(gs,plat.x+plat.w/2,topY);
      else gs.coins.push({id:gs.pid++,x:plat.x+Math.random()*plat.w,y:topY-30,collected:false,popT:0});
    }
    // Gem: rarer than coins, only above 280m; more common at high altitude
    const gemChance=gs.score>800?0.10:gs.score>500?0.078:gs.score>280?0.055:0;
    if(gemChance>0&&Math.random()<gemChance)
      gs.gems.push({id:gs.pid++,x:plat.x+Math.random()*plat.w,y:topY-36,collected:false,popT:0});
    // Heart: very rare, only when hurt
    if(gs.score>120&&gs.lives<=2&&Math.random()<0.032&&!gs.powerUps.some(p=>!p.col&&p.tp==="heart"))
      gs.powerUps.push(mkPU(gs,topY-45,"heart"));
    else if(gs.score>20&&Math.random()<0.08&&!gs.powerUps.some(p=>!p.col&&Math.abs(p.y-topY)<300)){
      // Weighted spawn: star & speed are 2× rarer than the rest
      const pool:PUType[]=["jetpack","shield","magnet","boots","speed","star","jetpack","shield","magnet","boots"];
      gs.powerUps.push(mkPU(gs,topY-45,pool[Math.floor(Math.random()*pool.length)]));
    }
    if(gs.score>80&&Math.random()<0.10&&!gs.enemies.some(e=>!e.dead&&Math.abs(e.y-topY)<200)){
      gs.enemies.push(mkEnemy(gs,topY,gs.score));
      // Double Trouble: at score 600+, occasionally spawn a second enemy nearby
      if(gs.score>600&&Math.random()<0.35){
        gs.enemies.push(mkEnemy(gs,topY+Math.random()*90,gs.score));
        pop(gs,SW/2-60,gs.scrollY+SH*0.35,"👿 DOUBLE TROUBLE!","#FF4488");
      }
    }
    if(gs.score>600&&Math.random()<0.12&&!gs.planets.some(p=>Math.abs(p.y-topY)<SH*0.6))
      gs.planets.push(mkPlanet(gs,topY+Math.random()*SH*0.5));
    if(gs.score>480&&Math.random()<0.035&&!gs.bosses.some(b=>!b.dead)){
      gs.bosses.push(mkBoss(gs,topY-90));
      pop(gs,SW/2-40,gs.scrollY+SH*0.25,"👹 BOSS INCOMING!","#FF3300");
      emit(gs,SW/2,gs.scrollY+SH*0.3,"#FF3300",18);gs.shakeT=0.5;
    }
    if(gs.score>850&&Math.random()<0.022&&!gs.wormholes.some(w=>!w.used&&Math.abs(w.y-topY)<SH*1.5))
      gs.wormholes.push(mkWormhole(gs,topY-110));
  }
  const topCloud=gs.clouds.length?Math.min(...gs.clouds.map(c=>c.y)):gs.startY;
  if(topCloud>gs.scrollY-SH*1.5) gs.clouds.push(mkCloud(gs,topCloud-SH*(0.4+Math.random()*0.5)));
  for(const p of gs.plats) if(p.type==="moving") p.x=p.ox+Math.sin(now/1000*1.6+p.id*1.3)*p.range;

  // Shooting stars
  if(gs.score>280){
    gs.ssTimer-=dt;
    if(gs.ssTimer<0){gs.ssTimer=6+Math.random()*6;gs.shootStars.push({id:gs.pid++,sx:Math.random()*SW,sy:Math.random()*SH*0.4,vx:-130-Math.random()*80,vy:85+Math.random()*60,life:1});}
    for(const ss of gs.shootStars){ss.sx+=ss.vx*dt;ss.sy+=ss.vy*dt;ss.life-=dt*1.4;}
    gs.shootStars=gs.shootStars.filter(s=>s.life>0&&s.sx>-60);
  }

  // Warp stars (screen-space)
  if(gs.score>750){
    const depth=Math.min(1,(gs.score-750)/300);
    if(Math.random()<depth*14*dt) gs.warpStars.push({id:gs.pid++,x:Math.random()*SW,y:SH+10,vy:-(320+Math.random()*680*(1+depth)),r:0.6+Math.random()*1.8});
    for(const ws of gs.warpStars) ws.y+=ws.vy*dt;
    gs.warpStars=gs.warpStars.filter(ws=>ws.y>-24);
  } else { if(gs.warpStars.length) gs.warpStars=[]; }

  // Weather particles
  const rainActive=gs.score>=30&&gs.score<380;
  const snowActive=gs.score>=420&&gs.score<800;
  if(rainActive||snowActive){
    const rate=rainActive?7:2.5;
    if(Math.random()<rate*dt){
      gs.weatherParts.push({id:gs.pid++,
        x:Math.random()*SW+(rainActive?-20:0),y:-10,
        vy:rainActive?310+Math.random()*140:58+Math.random()*42,
        vx:rainActive?(gs.windF||15)*0.22+(Math.random()-0.3)*12:(Math.random()-0.5)*32,
      });
    }
    for(const wp of gs.weatherParts){wp.x+=wp.vx*dt;wp.y+=wp.vy*dt;}
    gs.weatherParts=gs.weatherParts.filter(wp=>wp.y<SH+12&&wp.x>-24&&wp.x<SW+24).slice(-90);
  } else { if(gs.weatherParts.length) gs.weatherParts=[]; }

  // Particles + text pops
  for(const pt of gs.parts){pt.x+=pt.vx*dt;pt.y+=pt.vy*dt;pt.vy+=560*dt;pt.life-=dt*2.2;}
  gs.parts=gs.parts.filter(p=>p.life>0);
  for(const tp of gs.textPops){tp.y+=tp.vy*dt;tp.vy*=0.92;tp.life-=dt*1.6;}
  gs.textPops=gs.textPops.filter(t=>t.life>0);

  // Squash lerps + shake
  const sr=1-Math.exp(-13*dt);
  gs.psx+=(1-gs.psx)*sr;gs.psy+=(1-gs.psy)*sr;
  for(const p of gs.plats){p.sq+=(0-p.sq)*Math.min(1,dt*14);if(p.ringT>0)p.ringT=Math.max(0,p.ringT-dt*2.2);}
  if(gs.shakeT>0){gs.shakeT-=dt;const s=gs.shakeT*9;gs.shakeX=(Math.random()-0.5)*s;gs.shakeY=(Math.random()-0.5)*s;}else{gs.shakeX=0;gs.shakeY=0;}

  // Timers
  if(gs.comboT>0)      gs.comboT-=dt;
  if(gs.milestoneT>0)  gs.milestoneT-=dt;
  if(gs.invincT>0)     gs.invincT-=dt;
  if(gs.djumpFlashT>0) gs.djumpFlashT-=dt;
  if(gs.stompFlashT>0) gs.stompFlashT-=dt;
  if(gs.zoneFlashT>0)  gs.zoneFlashT-=dt;

  // Cleanup
  const cutY=gs.scrollY+SH+300;
  gs.plats   =gs.plats.filter(p=>p.y<cutY);
  gs.coins   =gs.coins.filter(c=>c.y<cutY||!c.collected);
  gs.gems      =gs.gems.filter(g=>g.y<cutY||!g.collected);
  gs.powerUps  =gs.powerUps.filter(p=>p.y<cutY);
  gs.enemies   =gs.enemies.filter(e=>!e.dead&&e.y<cutY&&(e.type==="bat"||(e.vx<0?e.x>-90:e.x<SW+90)));
  gs.bosses    =gs.bosses.filter(b=>!b.dead||b.deathT>0);
  gs.wormholes =gs.wormholes.filter(w=>!w.used&&w.y<cutY);
  gs.planets =gs.planets.filter(p=>p.y-gs.scrollY*p.par<SH+200);
  gs.clouds  =gs.clouds.filter(c=>c.y-gs.scrollY*c.par<SH+150);

  // Death
  if(gs.py>gs.scrollY+SH+DEATH_OFF&&gs.invincT<=0){
    if(gs.shielded){gs.shielded=false;gs.pvy=SPRING_VEL*1.1;gs.py=gs.scrollY+SH*0.55;gs.invincT=2.2;gs.shakeT=0.3;gs.airStomps=0;emit(gs,gs.px+PW/2,gs.py,"#50A0FF",20);}
    else if(gs.lives>1){gs.lives--;gs.pvy=SPRING_VEL;gs.py=gs.scrollY+SH*0.55;gs.invincT=2.5;gs.shakeT=0.45;gs.canDJump=true;gs.combo=0;gs.comboT=0;gs.airStomps=0;emit(gs,gs.px+PW/2,gs.py,"#FF4422",16);}
    else{gs.phase="dead";gs.shakeT=0.65;emit(gs,gs.px+PW/2,gs.py,P_GREEN,24);}
  }
}

// ─── Background ───────────────────────────────────────────────────────────────
const LINE_SPACING=44;
const LINE_COUNT=Math.ceil(SH/LINE_SPACING)+3;
const STAR_SEEDS=Array.from({length:32},(_,i)=>({x:(Math.sin(i*2.39)*0.5+0.5)*SW,y:(Math.cos(i*1.61)*0.5+0.5)*SH,r:1+(i%3)*0.8,ph:i*1.7}));

function Background({scrollY,score}:{scrollY:number;score:number}){
  const zc=zoneColors(score);
  const lineOff=((scrollY*0.18)%LINE_SPACING+LINE_SPACING)%LINE_SPACING;
  const starOp=Math.max(0,Math.min(1,(score-300)/180));
  const auroraOp=Math.max(0,Math.min(0.9,(score-650)/200));
  const now=Date.now();
  return(
    <View style={[StyleSheet.absoluteFillObject,{backgroundColor:zc.sky,pointerEvents:"none"}]}>
      {Array.from({length:LINE_COUNT},(_,i)=>(<View key={i} style={[b.line,{top:i*LINE_SPACING-LINE_SPACING+lineOff,backgroundColor:zc.line,opacity:zc.lop}]}/>))}
      <View style={[b.margin,{backgroundColor:zc.dark?"#203858":"#E88888",opacity:zc.dark?0.35:0.24}]}/>
      {auroraOp>0&&(
        <View style={{position:"absolute",left:0,right:0,top:0,height:SH*0.5,opacity:auroraOp}}>
          <View style={{position:"absolute",left:-20,right:-20,top:0,height:110,borderRadius:55,backgroundColor:`rgba(0,255,150,${(0.05+Math.sin(now/3100)*0.02).toFixed(3)})`}}/>
          <View style={{position:"absolute",left:-20,right:-20,top:80,height:90,borderRadius:45,backgroundColor:`rgba(100,0,255,${(0.04+Math.sin(now/2700+1)*0.015).toFixed(3)})`}}/>
          <View style={{position:"absolute",left:-20,right:-20,top:155,height:75,borderRadius:38,backgroundColor:`rgba(0,130,255,${(0.04+Math.sin(now/3500+2)*0.015).toFixed(3)})`}}/>
        </View>
      )}
      {starOp>0&&STAR_SEEDS.map((s,i)=>{
        const sy=((s.y+scrollY*0.06)%SH+SH)%SH;
        const tw=0.55+Math.sin(now/420+s.ph)*0.45;
        return <View key={i} style={[b.star,{left:s.x,top:sy,width:s.r*2,height:s.r*2,borderRadius:s.r,opacity:starOp*(0.4+(i%3)*0.22)*tw}]}/>;
      })}
    </View>
  );
}

// ─── BgLayer ──────────────────────────────────────────────────────────────────
function BgLayer({clouds,planets,shootStars,scrollY,dark,score}:{
  clouds:Cloud[];planets:Planet[];shootStars:SShoot[];scrollY:number;dark:boolean;score:number;
}){
  const zc=zoneColors(score);
  const moonOp=Math.max(0,Math.min(1,(score-320)/120));
  const moonSky=`rgb(${zc.rawSky.join(",")})`;
  const cc=dark?"rgba(80,120,200,":"rgba(255,255,255,";
  const riseSunOp=Math.max(0,1-score/110);
  const setSunOp=Math.max(0,Math.min(1,score<230?(score-80)/150:1-(score-230)/200));
  const cityOp=Math.max(0,1-score/220);
  return(
    <View style={[StyleSheet.absoluteFillObject,{pointerEvents:"none"}]}>
      {cityOp>0.02&&(
        <View style={{position:"absolute",left:0,right:0,bottom:0,height:220,opacity:cityOp*0.72}}>
          {CITY_BLDGS.map((bld,i)=>(
            <View key={i} style={{position:"absolute",left:bld.x,bottom:0,width:bld.w,height:bld.h,backgroundColor:dark?"rgba(10,15,35,0.9)":"rgba(28,18,10,0.52)"}}>
              {Array.from({length:Math.floor(bld.h/28)},(_,j)=>(
                <View key={j} style={{position:"absolute",left:3,right:3,top:j*28+6,height:8,flexDirection:"row",gap:2}}>
                  <View style={{flex:1,backgroundColor:`rgba(255,220,100,${Math.sin(i*3.7+j*1.9)>0.1?0.45:0.05})`}}/>
                  <View style={{flex:1,backgroundColor:`rgba(255,220,100,${Math.sin(i*2.1+j*3.3)>0.15?0.40:0.05})`}}/>
                </View>
              ))}
            </View>
          ))}
        </View>
      )}
      {riseSunOp>0&&(
        <View style={{position:"absolute",right:22,top:100,opacity:riseSunOp}}>
          <View style={{width:42,height:42,borderRadius:21,backgroundColor:"#FFE840"}}/>
          <View style={{position:"absolute",left:-10,top:-10,right:-10,bottom:-10,borderRadius:42,backgroundColor:"rgba(255,220,60,0.22)"}}/>
          <View style={{position:"absolute",left:-22,top:-22,right:-22,bottom:-22,borderRadius:70,backgroundColor:"rgba(255,200,40,0.10)"}}/>
        </View>
      )}
      {setSunOp>0.05&&(
        <View style={{position:"absolute",right:28,top:SH*0.52,opacity:setSunOp*0.88}}>
          <View style={{width:78,height:78,borderRadius:39,backgroundColor:"#FF7820"}}/>
          <View style={{position:"absolute",left:-14,top:-14,right:-14,bottom:-14,borderRadius:70,backgroundColor:"rgba(255,120,30,0.20)"}}/>
          <View style={{position:"absolute",left:-28,top:-28,right:-28,bottom:-28,borderRadius:100,backgroundColor:"rgba(255,90,20,0.10)"}}/>
          <View style={{position:"absolute",left:-55,top:20,width:22,height:22,borderRadius:11,backgroundColor:"rgba(255,180,60,0.22)"}}/>
          <View style={{position:"absolute",left:-90,top:30,width:12,height:12,borderRadius:6,backgroundColor:"rgba(255,200,100,0.18)"}}/>
          <View style={{position:"absolute",left:18,top:14,width:20,height:20,borderRadius:10,backgroundColor:"rgba(255,220,160,0.35)"}}/>
        </View>
      )}
      {planets.map(p=>{const sy=p.y-scrollY*p.par;if(sy>SH+80||sy<-80)return null;return(
        <View key={p.id} style={{position:"absolute",left:p.x-p.r,top:sy-p.r,width:p.r*2,height:p.r*2,borderRadius:p.r,backgroundColor:p.c,opacity:0.72}}>
          {p.ring&&<View style={{position:"absolute",left:-p.r*0.35,right:-p.r*0.35,top:p.r*0.5,bottom:p.r*0.5,borderRadius:p.r*2,borderWidth:4,borderColor:p.c,opacity:0.55,transform:[{scaleY:0.28}]}}/>}
          <View style={{position:"absolute",left:p.r*0.2,top:p.r*0.15,width:p.r*0.5,height:p.r*0.5,borderRadius:p.r*0.25,backgroundColor:"rgba(255,255,255,0.26)"}}/>
        </View>
      );})}
      {clouds.map(c=>{const sy=c.y-scrollY*c.par;if(sy>SH+80||sy<-80)return null;return(
        <View key={c.id} style={{position:"absolute",left:c.x,top:sy,width:c.w,height:c.h,opacity:c.op}}>
          <View style={[b.cloudBody,{width:c.w*0.75,height:c.h,left:c.w*0.12,backgroundColor:cc+"0.9)"}]}/>
          <View style={[b.cloudPuff,{width:c.h*1.1,height:c.h*1.1,left:c.w*0.18,top:-c.h*0.4,backgroundColor:cc+"0.85)"}]}/>
          <View style={[b.cloudPuff,{width:c.h*0.9,height:c.h*0.9,left:c.w*0.48,top:-c.h*0.35,backgroundColor:cc+"0.8)"}]}/>
        </View>
      );})}
      {moonOp>0&&(<View style={{position:"absolute",right:28,top:120,opacity:moonOp}}>
        <View style={{width:46,height:46,borderRadius:23,backgroundColor:"#FFF8D8"}}/>
        <View style={{position:"absolute",left:11,top:-7,width:42,height:42,borderRadius:21,backgroundColor:moonSky}}/>
        <View style={{position:"absolute",left:-9,top:-9,right:-9,bottom:-9,borderRadius:37,backgroundColor:"rgba(255,248,216,0.14)"}}/>
      </View>)}
      {shootStars.map(ss=>(<View key={ss.id} style={{position:"absolute",left:ss.sx-1,top:ss.sy-22,width:2,height:30,borderRadius:2,backgroundColor:"white",opacity:ss.life*0.95,transform:[{rotate:"30deg"}]}}/>))}
    </View>
  );
}

// ─── Platform ─────────────────────────────────────────────────────────────────
function PlatView({p}:{p:Plat}){
  const crumbling=p.crumbleT>0;
  const shakeX=crumbling?Math.sin(Date.now()/44)*3.8:0;
  const br=p.broken;
  const bg=br?"#999":crumbling?"#FF6C20":PCOL[p.type];
  const bord=br?"#777":crumbling?"#CC4A10":PBORD[p.type];
  const op=br?0.45:crumbling?Math.max(0.3,p.crumbleT/0.52):1;
  const ringScale=p.ringT>0?1+(1-p.ringT)*1.6:1;
  const ringOp=p.ringT>0?p.ringT*0.55:0;
  return(
    <>
      {p.ringT>0&&<View style={{position:"absolute",left:p.x-p.w*0.3,top:p.y-4,width:p.w*1.6,height:PLH+8,borderRadius:12,borderWidth:2.5,borderColor:PCOL[p.type],opacity:ringOp,transform:[{scaleX:ringScale}],pointerEvents:"none"}}/>}
      <View style={[g.plat,{left:p.x+shakeX,top:p.y+(PLH*p.sq*0.22),width:p.w,backgroundColor:bg,borderColor:bord,opacity:op,transform:[{scaleY:1-p.sq*0.44}]}]}>
        <View style={[g.platShine,p.type==="golden"?{backgroundColor:"rgba(255,255,255,0.55)"}:p.type==="ice"?{backgroundColor:"rgba(255,255,255,0.72)"}:{}]}/>
        {p.type==="spring"&&!br&&<View style={g.springWrap}><View style={g.springLine}/><View style={g.springLine}/><View style={g.springLine}/></View>}
        {p.type==="moving"&&!br&&<View style={g.movArrows}><Text style={g.arrowTxt}>{"◀  ▶"}</Text></View>}
        {p.type==="crumble"&&!br&&<View style={g.movArrows}><Text style={[g.arrowTxt,{fontSize:9,color:"rgba(255,255,255,0.65)"}]}>⚠ ⚠ ⚠</Text></View>}
        {p.type==="golden"&&!br&&<View style={g.movArrows}><Text style={[g.arrowTxt,{fontSize:10,letterSpacing:4,color:"rgba(40,20,0,0.55)"}]}>✦ ✦ ✦</Text></View>}
        {p.type==="rocket"&&!br&&<View style={g.movArrows}><Text style={[g.arrowTxt,{fontSize:14}]}>🚀</Text></View>}
        {p.type==="ice"&&!br&&<View style={g.movArrows}><Text style={[g.arrowTxt,{fontSize:9,letterSpacing:3,color:"rgba(20,80,140,0.65)"}]}>❄  ❄  ❄</Text></View>}
        {p.type==="bomb"&&!br&&<View style={g.movArrows}><Text style={[g.arrowTxt,{fontSize:16}]}>💣</Text></View>}
        {p.type==="conveyor"&&!br&&<View style={g.movArrows}><Text style={[g.arrowTxt,{fontSize:12,letterSpacing:2,color:"rgba(255,255,255,0.9)"}]}>{p.dir>0?"▶▶▶":"◀◀◀"}</Text></View>}
      </View>
    </>
  );
}

// ─── Enemy ────────────────────────────────────────────────────────────────────
const ENEMY_ICON:Record<EnemyType,string>={bird:"🐦",ghost:"👻",ufo:"🛸",asteroid:"☄️",bat:"🦇"};
function EnemyView({e,now}:{e:Enemy;now:number}){
  if(e.dead) return null;
  const batFlap=e.type==="bat"&&Math.sin(now/80)>0;
  return(
    <View style={{position:"absolute",left:e.x,top:e.y,width:38,height:32,alignItems:"center",justifyContent:"center"}}>
      <Text style={{fontSize:e.type==="bat"?22:26,transform:[{scaleX:e.vx>0?-1:1},{scaleY:batFlap?1.15:0.85}],opacity:e.type==="bat"?0.88:1}}>
        {ENEMY_ICON[e.type]}
      </Text>
    </View>
  );
}

// ─── Power-up ─────────────────────────────────────────────────────────────────
function PUView({pu}:{pu:PU}){
  if(pu.col&&pu.bt<=0) return null;
  const bob=Math.sin(pu.bt)*6;
  return(
    <View style={{position:"absolute",left:pu.x-22,top:pu.y-22+bob,width:44,height:44,borderRadius:22,backgroundColor:PU_GLW[pu.tp],borderWidth:2,borderColor:PU_COL[pu.tp],alignItems:"center",justifyContent:"center",opacity:pu.col?Math.min(1,pu.bt):1}}>
      <Text style={{fontSize:22}}>{PU_ICON[pu.tp]}</Text>
    </View>
  );
}

// ─── Coin ─────────────────────────────────────────────────────────────────────
function CoinView({c,now}:{c:Coin;now:number}){
  if(c.collected&&c.popT<=0) return null;
  const spin=c.collected?1+(1-c.popT)*0.5:Math.abs(Math.cos(now/320))*0.75+0.25;
  return(
    <View style={{position:"absolute",left:c.x-9,top:c.y-9,width:18,height:18,borderRadius:9,backgroundColor:"#F5D123",borderWidth:2,borderColor:"#D4A800",opacity:c.collected?c.popT:1,transform:[{scaleX:spin}],alignItems:"center",justifyContent:"center"}}>
      <View style={{width:6,height:6,borderRadius:3,backgroundColor:"rgba(255,255,255,0.7)"}}/>
    </View>
  );
}

// ─── Gem ──────────────────────────────────────────────────────────────────────
function GemView({gm,now}:{gm:Gem;now:number}){
  if(gm.collected&&gm.popT<=0) return null;
  const pulse=gm.collected?1+(1-gm.popT)*0.6:0.88+Math.sin(now/260+gm.id)*0.12;
  const glow=`rgba(${180+Math.round(Math.sin(now/200+gm.id)*30)},60,255,${(0.22+Math.sin(now/180)*0.10).toFixed(2)})`;
  return(
    <View style={{position:"absolute",left:gm.x-12,top:gm.y-12,width:24,height:24,opacity:gm.collected?gm.popT:1,transform:[{scale:pulse},{rotate:"45deg"}],borderRadius:4,backgroundColor:"#9B30FF",borderWidth:2.5,borderColor:"#C870FF",alignItems:"center",justifyContent:"center",boxShadow:`0px 0px 8px ${glow}`} as any}>
      <View style={{width:9,height:9,borderRadius:2,backgroundColor:"rgba(255,255,255,0.52)",transform:[{rotate:"45deg"}]}}/>
    </View>
  );
}

// ─── Boss ─────────────────────────────────────────────────────────────────────
function BossView({boss}:{boss:Boss}){
  if(boss.dead&&boss.deathT<=0) return null;
  const op=boss.dead?boss.deathT:1;
  const flash=boss.hitT>0.2;
  const hpPct=boss.hp/boss.maxHp;
  const hpCol=hpPct>0.6?"#44EE44":hpPct>0.3?"#FFD700":"#FF2222";
  const sc=1+(boss.hitT>0?boss.hitT*0.3:0);
  return(
    <View style={{position:"absolute",left:boss.x,top:boss.y,opacity:op,alignItems:"center",width:80}}>
      {!boss.dead&&<View style={{width:80,height:7,backgroundColor:"rgba(0,0,0,0.45)",borderRadius:4,marginBottom:5}}>
        <View style={{width:80*hpPct,height:7,backgroundColor:hpCol,borderRadius:4}}/>
      </View>}
      <View style={{width:80,height:80,borderRadius:40,backgroundColor:flash?"#FF6633":boss.enraged?"#AA0000":"#7B0000",borderWidth:boss.enraged?5:3.5,borderColor:flash?"#FFAA44":boss.enraged?"#FF6600":"#FF2200",alignItems:"center",justifyContent:"center",transform:[{scale:sc*(boss.enraged?1.08:1)}]}}>
        {boss.enraged&&!flash&&<View style={{position:"absolute",width:90,height:90,borderRadius:45,borderWidth:2,borderColor:"rgba(255,60,0,0.45)",pointerEvents:"none"} as any}/>}
        <Text style={{fontSize:36,lineHeight:40}}>{boss.enraged?"😡":"👹"}</Text>
      </View>
    </View>
  );
}

// ─── Wormhole ─────────────────────────────────────────────────────────────────
function WormholeView({wh,now}:{wh:Wormhole;now:number}){
  if(wh.used) return null;
  const pulse=0.76+Math.sin(now/240)*0.24;
  const rot1=`${((now/1200)%(Math.PI*2)).toFixed(3)}rad`;
  const rot2=`${(-(now/800)%(Math.PI*2)).toFixed(3)}rad`;
  return(
    <View style={{position:"absolute",left:wh.x-wh.r,top:wh.y-wh.r,width:wh.r*2,height:wh.r*2,alignItems:"center",justifyContent:"center"}}>
      <View style={{position:"absolute",width:wh.r*2,height:wh.r*2,borderRadius:wh.r,borderWidth:3,borderColor:`rgba(160,64,255,${(0.55+Math.sin(now/300)*0.2).toFixed(2)})`,transform:[{rotate:rot1}]}}/>
      <View style={{position:"absolute",width:wh.r*1.4,height:wh.r*1.4,borderRadius:wh.r,borderWidth:2.5,borderColor:`rgba(0,200,255,${(0.45+Math.sin(now/220)*0.2).toFixed(2)})`,transform:[{rotate:rot2}]}}/>
      <View style={{width:wh.r*0.85,height:wh.r*0.85,borderRadius:wh.r,backgroundColor:`rgba(100,0,240,${(0.38*pulse).toFixed(2)})`,alignItems:"center",justifyContent:"center"}}>
        <Text style={{fontSize:18,transform:[{scale:pulse}]}}>🌀</Text>
      </View>
    </View>
  );
}

// ─── Player ───────────────────────────────────────────────────────────────────
function PlayerView({gs,plrBg,plrBrd,combo,comboT}:{gs:GS;plrBg:string;plrBrd:string;combo:number;comboT:number}){
  const flip=gs.facing<0?-1:1;
  const ey=Math.round(gs.eyeY*10)/10;
  const inv=gs.invincT>0&&gs.starT<=0&&Math.sin(gs.invincT*18)>0;
  const fireAura=combo>=20&&comboT>0;
  const pulseBord=fireAura?`rgba(255,${Math.floor(60+Math.sin(Date.now()/120)*60)},0,0.85)`:"transparent";
  const starActive=gs.starT>0;
  const starBg=starActive?hslToRgb((Date.now()/22)%360,100,62):plrBg;
  const starBord=starActive?hslToRgb((Date.now()/22+120)%360,100,55):plrBrd;
  return(
    <View style={[g.player,{left:gs.px,top:gs.py,backgroundColor:starActive?starBg:plrBg,borderColor:starActive?starBord:plrBrd,transform:[{scaleX:gs.psx*flip},{scaleY:gs.psy}],opacity:inv?0.42:1}]}>
      {starActive&&<View style={[g.comboAura,{borderColor:hslToRgb((Date.now()/18)%360,100,60),borderWidth:4,backgroundColor:"rgba(255,220,0,0.14)"}]}/>}
      {fireAura&&!starActive&&<View style={[g.comboAura,{borderColor:pulseBord,backgroundColor:combo>=30?"rgba(255,60,0,0.12)":"rgba(255,120,0,0.07)"}]}/>}
      <View style={[g.playerGlow,{backgroundColor:plrBg}]}/>
      <View style={[g.antenna,{left:PW*0.26,backgroundColor:plrBrd}]}/>
      <View style={[g.antenna,{right:PW*0.26,backgroundColor:plrBrd}]}/>
      <View style={g.eyeRow}>
        <View style={g.eye}><View style={[g.pupil,{marginTop:2+ey}]}/><View style={g.eyeShine}/></View>
        <View style={g.eye}><View style={[g.pupil,{marginTop:2+ey}]}/><View style={g.eyeShine}/></View>
      </View>
      <View style={[g.mouth,gs.pvy<-50?g.mouthH:g.mouthN]}/>
      <View style={g.feet}>
        <View style={[g.foot,{backgroundColor:gs.bootsT>0?"#FFB020":gs.rocketFlashT>0?"#FF4420":plrBrd}]}/>
        <View style={[g.foot,{backgroundColor:gs.bootsT>0?"#FFB020":gs.rocketFlashT>0?"#FF4420":plrBrd}]}/>
      </View>
      {gs.shielded&&<View style={g.shield}/>}
      {gs.magnetT>0&&<View style={g.magnetAura}/>}
      {gs.bootsT>0&&<View style={g.bootsGlow}/>}
      {gs.speedT>0&&<View style={g.speedAura}/>}
      {gs.rocketFlashT>0&&<View style={g.rocketGlow}/>}
    </View>
  );
}

// ─── Shadow ───────────────────────────────────────────────────────────────────
function Shadow({gs}:{gs:GS}){
  let cl:Plat|null=null,dist=Infinity;
  const pb=gs.py+PH;
  for(const p of gs.plats){if(p.broken||p.y<=pb)continue;const d=p.y-pb;if(d<260&&d<dist&&gs.px+PW>p.x+4&&gs.px<p.x+p.w-4){dist=d;cl=p;}}
  if(!cl) return null;
  const op=0.38*(1-dist/260),sw=Math.max(14,PW*0.85*(1-dist/400));
  return <View style={{position:"absolute",left:gs.px+PW/2-sw/2,top:cl.y-4,width:sw,height:6,borderRadius:sw/2,backgroundColor:"#000",opacity:op}}/>;
}

// ─── Speed lines ──────────────────────────────────────────────────────────────
function SpeedLines({gs,col,now}:{gs:GS;col:string;now:number}){
  if(gs.pvy<520) return null;
  const intensity=Math.min(1,(gs.pvy-520)/700),t=now/70;
  return<>{[0,1,2,3,4].map(i=>{const ox=Math.sin(t+i*2.1)*26,len=12+Math.cos(t*0.8+i*1.5)*7;return<View key={i} style={{position:"absolute",left:gs.px+PW/2+ox-1.5,top:gs.py-len-8,width:3,height:len,borderRadius:2,backgroundColor:col,opacity:0.22*intensity}}/>;})}</>;
}

// ─── Height bar ───────────────────────────────────────────────────────────────
function HeightBar({score,best,dark,topPad}:{score:number;best:number;dark:boolean;topPad:number}){
  const maxH=Math.max(best,score,50),barH=SH-topPad-80;
  const curFrac=Math.min(1,score/maxH),bestFrac=best>0?Math.min(1,best/maxH):0;
  return(
    <View style={{position:"absolute",right:7,top:topPad+20,height:barH,width:4,borderRadius:2,backgroundColor:dark?"rgba(255,255,255,0.12)":"rgba(0,0,0,0.10)"}}>
      <View style={{position:"absolute",left:0,bottom:0,right:0,height:barH*curFrac,borderRadius:2,backgroundColor:dark?"rgba(100,200,255,0.45)":"rgba(39,192,99,0.45)"}}/>
      {best>0&&<View style={{position:"absolute",left:-4,top:barH*(1-bestFrac),width:12,height:2,backgroundColor:"#F5D123",borderRadius:1}}/>}
      <View style={{position:"absolute",left:-4,top:barH*(1-curFrac)-5,width:12,height:12,borderRadius:6,backgroundColor:dark?"#80C8FF":"#27C063",borderWidth:2,borderColor:"rgba(255,255,255,0.8)"}}/>
    </View>
  );
}

// ─── HUD helpers ──────────────────────────────────────────────────────────────
function LivesDisplay({lives,dark}:{lives:number;dark:boolean}){
  return(<View style={s.livesRow}>{[0,1,2].map(i=>(<Text key={i} style={[s.heart,{opacity:i<lives?1:0.22,color:i<lives?"#FF4455":dark?"#884455":"#FF4455"}]}>♥</Text>))}</View>);
}
function TimerBar({t,max,col,icon}:{t:number;max:number;col:string;icon:string}){
  if(t<=0) return null;
  const expiring=t<1.0;
  const blinkOp=expiring?(Date.now()/130)%1>0.5?0.28:1:1;
  return(
    <View style={[s.timerRow,{opacity:blinkOp}]}>
      <Text style={s.timerIcon}>{icon}</Text>
      <View style={[s.timerTrack,{backgroundColor:col+"33"}]}>
        <View style={[s.timerFill,{width:`${Math.max(0,Math.min(1,t/max))*100}%` as any,backgroundColor:col}]}/>
      </View>
    </View>
  );
}
function StatPill({icon,val,label}:{icon:string;val:number;label:string}){
  return(<View style={s.statPill}><Text style={s.statIcon}>{icon}</Text><Text style={s.statVal}>{val}</Text><Text style={s.statLabel}>{label}</Text></View>);
}
function LbRow({score,rank,dark}:{score:number;rank:number;dark:boolean}){
  const medals=["🥇","🥈","🥉"];
  return(<View style={s.lbRow}><Text style={s.lbMedal}>{medals[rank]??""}</Text><Text style={[s.lbScore,{color:dark?"white":"#1A1A2A"}]}>{score}</Text><Text style={[s.lbM,{color:dark?"rgba(255,255,255,0.45)":"rgba(0,0,0,0.45)"}]}>m</Text></View>);
}

// ─── Medal helper ─────────────────────────────────────────────────────────────
function getMedal(s:number):{icon:string;label:string;col:string}|null{
  if(s>=1500) return{icon:"💎",label:"DIAMOND",col:"#80DFFF"};
  if(s>=750)  return{icon:"🥇",label:"GOLD",col:"#FFD700"};
  if(s>=300)  return{icon:"🥈",label:"SILVER",col:"#C0C8D8"};
  if(s>=100)  return{icon:"🥉",label:"BRONZE",col:"#CD8B52"};
  return null;
}

// ─── Main screen ──────────────────────────────────────────────────────────────
export default function GameScreen(){
  const insets=useSafeAreaInsets();
  const gs=useRef<GS>(mkGS(getBest()));
  const [,setT]=useState(0);
  const rafRef=useRef<number>(0);
  const prevTs=useRef<number>(0);
  const nowRef=useRef<number>(0);
  const [phase,setPhase]=useState<Phase>("menu");
  const [leaderboard,setLeaderboard]=useState<number[]>(()=>getLeaderboard());
  const [dailyBest,setDailyBest]=useState(()=>getDailyBest());

  const loop=useCallback((ts:number)=>{
    if(prevTs.current===0) prevTs.current=ts;
    const dt=Math.min((ts-prevTs.current)/1000,0.033);
    prevTs.current=ts;nowRef.current=ts;
    update(gs.current,dt,ts);
    if(gs.current.phase!==phase) setPhase(gs.current.phase);
    setT(t=>t+1);
    rafRef.current=requestAnimationFrame(loop);
  },[phase]);

  useEffect(()=>{rafRef.current=requestAnimationFrame(loop);return()=>cancelAnimationFrame(rafRef.current);},[loop]);

  useEffect(()=>{
    if(phase==="dead"){
      setLeaderboard(addToLeaderboard(gs.current.score));
      const sc=gs.current.score;
      if(sc>getDailyBest()){saveDailyBest(sc);setDailyBest(sc);}
    }
  },[phase]);

  useEffect(()=>{
    if(Platform.OS!=="web") return;
    const dn=(e:KeyboardEvent)=>{
      if(e.key==="ArrowLeft"||e.key==="a") gs.current.input=-1;
      if(e.key==="ArrowRight"||e.key==="d") gs.current.input=1;
      if((e.key===" "||e.key==="Enter")&&gs.current.phase!=="play") startGame();
      if(e.key===" "&&gs.current.phase==="play") triggerDJump();
    };
    const up=()=>{gs.current.input=0;};
    window.addEventListener("keydown",dn);window.addEventListener("keyup",up);
    return()=>{window.removeEventListener("keydown",dn);window.removeEventListener("keyup",up);};
  },[]);

  const triggerDJump=useCallback(()=>{
    const g=gs.current;
    if(g.canDJump&&g.pvy>60&&g.invincT<=0&&g.jetpackT<=0){g.pvy=DJUMP_VEL;g.canDJump=false;g.psx=0.80;g.psy=1.24;g.djumpFlashT=0.65;emit(g,g.px+PW/2,g.py+PH,"#50FFBB",8);}
  },[]);

  const startGame=useCallback(()=>{
    const best=gs.current.best;
    gs.current=mkGS(best);gs.current.phase="play";
    prevTs.current=0;setPhase("play");
  },[]);

  const onTouchStart=useCallback((e:any)=>{
    if(gs.current.phase==="menu"||gs.current.phase==="dead") return;
    gs.current.input=e.nativeEvent.locationX<SW/2?-1:1;triggerDJump();
  },[triggerDJump]);
  const onTouchMove=useCallback((e:any)=>{if(gs.current.phase!=="play")return;gs.current.input=e.nativeEvent.locationX<SW/2?-1:1;},[]);
  const onTouchEnd=useCallback(()=>{gs.current.input=0;},[]);

  const r=gs.current;
  const zc=zoneColors(r.score);
  const webTop=Platform.OS==="web"?67:0;
  const topPad=insets.top+webTop+8;
  const now=nowRef.current;
  const fallDark=Math.min(0.30,Math.max(0,r.pvy-620)/3800);
  const mult=comboMult(r.combo);
  const rainbow=r.combo>=10&&r.comboT>0;
  const rainZone=r.score>=30&&r.score<380;
  const snowZone=r.score>=420&&r.score<800;
  // Atmospheric haze: fades in above Night zone, deepens into Space
  const hazeDark=Math.max(0,Math.min(0.16,(r.score-560)/1400));
  const flameTrail=r.jetpackT>0||r.rocketFlashT>0;

  const hlPlatId=(()=>{
    if(r.phase!=="play"||r.pvy<0) return -1;
    const feet=r.py+PH;let best:Plat|null=null,minD=140;
    for(const p of r.plats){if(p.broken)continue;const d=p.y-feet;if(d>0&&d<minD&&r.px+PW>p.x+4&&r.px<p.x+p.w-4){minD=d;best=p;}}
    return best?best.id:-1;
  })();

  return(
    <View style={s.root}>
      <StatusBar hidden/>
      <Background scrollY={r.scrollY} score={r.score}/>
      <View style={StyleSheet.absoluteFillObject} onTouchStart={onTouchStart} onTouchMove={onTouchMove} onTouchEnd={onTouchEnd}>
        <BgLayer clouds={r.clouds} planets={r.planets} shootStars={r.shootStars} scrollY={r.scrollY} dark={zc.dark} score={r.score}/>

        {/* World layer */}
        <View style={[StyleSheet.absoluteFillObject,{transform:[{translateX:r.shakeX},{translateY:-r.scrollY+r.shakeY}],pointerEvents:"none"}]}>
          <View style={{position:"absolute",left:-30,top:r.startY+PH+PLH,right:-30,height:220,backgroundColor:"#7A5C3A"}}>
            <View style={{height:12,backgroundColor:"#3DB060"}}/>
          </View>
          <Shadow gs={r}/>
          {hlPlatId>=0&&r.plats.filter(p=>p.id===hlPlatId).map(p=>(
            <View key={p.id+"hl"} style={{position:"absolute",left:p.x-5,top:p.y-8,width:p.w+10,height:PLH+16,borderRadius:12,borderWidth:2.5,borderColor:PCOL[p.type],opacity:0.72}}/>
          ))}
          {r.plats.map(p=><PlatView key={p.id} p={p}/>)}
          {r.powerUps.map(p=><PUView key={p.id} pu={p}/>)}
          {r.coins.map(c=><CoinView key={c.id} c={c} now={now}/>)}
          {r.gems.map(gm=><GemView key={gm.id} gm={gm} now={now}/>)}
          {r.wormholes.map(w=><WormholeView key={w.id} wh={w} now={now}/>)}
          {r.bosses.map(b=><BossView key={b.id} boss={b}/>)}
          {r.enemies.map(e=><EnemyView key={e.id} e={e} now={now}/>)}

          {/* Trail — flame colors when boosting */}
          {r.trail.map((t,i)=>{
            const flamePhase=(i*55+now/10)%360;
            const col=flameTrail?hslToRgb(flamePhase<180?12+flamePhase*0.08:40,95,58):rainbow?hslToRgb((i*52+now/12)%360,90,62):zc.plrBg;
            const sz=flameTrail?PW*(0.7-i*0.06):PW*0.5;
            const op=(1-i/TRAIL_LEN)*(flameTrail?0.62:rainbow?0.65:0.28);
            return <View key={i} style={{position:"absolute",left:t.x-sz/2,top:t.y-sz/2,width:sz,height:sz,borderRadius:sz/2,backgroundColor:col,opacity:Math.max(0,op)}}/>;
          })}

          {r.parts.map((p,i)=>(<View key={i} style={{position:"absolute",left:p.x-p.r,top:p.y-p.r,width:p.r*2,height:p.r*2,borderRadius:p.r,backgroundColor:p.color,opacity:Math.max(0,p.life)}}/>))}
          {r.textPops.map(tp=>(<Text key={tp.id} style={{position:"absolute",left:tp.x-38,top:tp.y,color:tp.color,fontSize:13,fontWeight:"900",opacity:Math.max(0,tp.life),letterSpacing:0.5,textShadow:"0px 1px 3px rgba(0,0,0,0.35)"} as any}>{tp.text}</Text>))}
          <SpeedLines gs={r} col={zc.txt} now={now}/>
          <PlayerView gs={r} plrBg={zc.plrBg} plrBrd={zc.plrBrd} combo={r.combo} comboT={r.comboT}/>
        </View>

        {/* Atmospheric depth haze (Night → Space) */}
        {hazeDark>0&&<View style={{...StyleSheet.absoluteFillObject as any,backgroundColor:`rgba(6,8,18,${hazeDark.toFixed(3)})`,pointerEvents:"none"}}/>}

        {/* Weather particles */}
        {(rainZone||snowZone)&&r.weatherParts.map(wp=>(
          <View key={wp.id} style={{position:"absolute",left:wp.x,top:wp.y,width:rainZone?1.5:4,height:rainZone?10:4,borderRadius:rainZone?1:2,backgroundColor:rainZone?"rgba(180,200,255,0.42)":"rgba(240,245,255,0.65)",transform:rainZone?[{rotate:"13deg"}]:undefined,pointerEvents:"none"}}/>
        ))}

        {/* Warp stars */}
        {r.score>750&&r.warpStars.map(ws=>(
          <View key={ws.id} style={{position:"absolute",left:ws.x-ws.r,top:ws.y-ws.r*7,width:ws.r*2,height:Math.max(ws.r*2,ws.r*12),borderRadius:ws.r,backgroundColor:"rgba(255,255,255,0.82)",pointerEvents:"none"}}/>
        ))}

        {/* Wind streaks */}
        {r.windT>0&&[0,1,2,3].map(i=>{
          const wx=((now/(r.windF>0?5:-5)+i*(SW/4))%SW+SW)%SW;
          return <View key={i} style={{position:"absolute",left:wx,top:SH*0.15+i*SH*0.18,width:26,height:2,borderRadius:1,backgroundColor:"rgba(200,220,255,0.25)",pointerEvents:"none"}}/>;
        })}

        {/* Enemy warning arrows */}
        {phase==="play"&&r.enemies.filter(e=>!e.dead).map(e=>{
          const sy=e.y-r.scrollY;
          if(sy<-60||sy>SH+60) return null;
          const offL=e.x+36<0,offR=e.x>SW;
          if(!offL&&!offR) return null;
          const blink=Math.sin(now/160)>0;
          return(<View key={e.id} style={{position:"absolute",left:offL?4:SW-26,top:Math.max(topPad+50,Math.min(SH-60,sy-10)),opacity:blink?0.92:0.22,pointerEvents:"none"}}>
            <Text style={{fontSize:16,color:"#FF6644"}}>{offL?"◀":"▶"}</Text>
          </View>);
        })}

        {/* Screen overlays */}
        {phase==="play"&&fallDark>0&&(<View style={{...StyleSheet.absoluteFillObject as any,backgroundColor:`rgba(0,0,0,${fallDark.toFixed(3)})`,pointerEvents:"none"}}/>)}
        {phase==="play"&&r.milestoneT>1.85&&(<View style={{...StyleSheet.absoluteFillObject as any,backgroundColor:"rgba(255,255,255,0.28)",opacity:Math.min(1,(r.milestoneT-1.85)*8),pointerEvents:"none"}}/>)}
        {r.zoneFlashT>0&&(<View style={{...StyleSheet.absoluteFillObject as any,backgroundColor:r.zoneFlashCol,opacity:Math.min(1,r.zoneFlashT*4),pointerEvents:"none"}}/>)}

        {/* HUD */}
        <View style={[s.hud,{paddingTop:topPad}]}>
          <Text style={[s.scoreNum,{color:r.best>0&&r.score>=r.best?hslToRgb((now/12)%360,100,62):zc.txt}]}>{Math.round(r.displayScore)}</Text>
          <Text style={[s.mLabel,{color:zc.txt}]}>m</Text>
          {mult>1&&r.comboT>0&&<Text style={[s.multTxt,{color:comboCol(r.combo)}]}>×{mult.toFixed(1)}</Text>}
          {r.best>0&&<Text style={[s.bestLabel,{color:zc.txt}]}>BEST  {r.best} m</Text>}
        </View>

        {phase==="play"&&r.coinsCollected>0&&(
          <View style={[s.coinHud,{top:topPad+46}]}>
            <Text style={[s.coinHudTxt,{color:zc.txt}]}>🪙 {r.coinsCollected}{r.gemsCollected>0?`  💎 ${r.gemsCollected}`:""}</Text>
            {r.coinStreak>=3&&r.coinStreakT>0&&<Text style={[s.coinHudTxt,{color:"#FFD700",fontSize:11,marginTop:1}]}>🔥 ×{r.coinStreak} STREAK</Text>}
          </View>
        )}
        {phase==="play"&&r.windT>0&&(
          <View style={[s.coinHud,{top:topPad+66}]}>
            <Text style={[s.coinHudTxt,{color:zc.txt,fontSize:11,opacity:0.50}]}>{r.windF>0?"→ WIND":"WIND ←"}</Text>
          </View>
        )}

        {phase==="play"&&(<View style={[s.livesWrap,{top:topPad+6}]}><LivesDisplay lives={r.lives} dark={zc.dark}/></View>)}

        <View style={[s.timersStack,{top:topPad+52}]}>
          <TimerBar t={r.jetpackT}      max={4}   col="#FF8C00" icon="🚀"/>
          <TimerBar t={r.magnetT}       max={6}   col="#C050FF" icon="🧲"/>
          <TimerBar t={r.bootsT}        max={5}   col="#FFB020" icon="🥾"/>
          <TimerBar t={r.speedT}        max={4}   col="#00E8FF" icon="⚡"/>
          <TimerBar t={r.rocketFlashT}  max={0.8} col="#FF4420" icon="🔥"/>
          <TimerBar t={r.starT}         max={3.5} col="#FFD700" icon="⭐"/>
        </View>

        {phase==="play"&&r.shielded&&(<View style={[s.shieldBadge,{top:topPad+54}]}><Text style={s.shieldTxt}>🛡️</Text></View>)}
        {phase==="play"&&(<View style={[s.zoneLabel,{top:topPad+12}]}><Text style={[s.zoneTxt,{color:zc.txt}]}>{zc.label}</Text></View>)}
        {phase==="play"&&<HeightBar score={r.score} best={r.best} dark={zc.dark} topPad={topPad}/>}

        {phase==="play"&&r.stompFlashT>0&&(<View style={s.stompWrap}><Text style={[s.stompTxt,{opacity:Math.min(1,r.stompFlashT*4)}]}>💀 STOMP!</Text></View>)}
        {phase==="play"&&r.djumpFlashT>0&&(<View style={s.djumpWrap}><Text style={[s.djumpTxt,{opacity:Math.min(1,r.djumpFlashT*3)}]}>↑ DOUBLE JUMP</Text></View>)}

        {phase==="play"&&r.comboT>0&&r.combo>=3&&(
          <View style={s.comboWrap}>
            <Text style={[s.comboTxt,{color:comboCol(r.combo)}]}>
              {r.combo>=20?`🔥🔥 x${r.combo}`:r.combo>=15?`🔥 x${r.combo}`:r.combo>=10?`⚡ x${r.combo}`:r.combo>=5?`✨ x${r.combo}`:`x${r.combo}`}
            </Text>
          </View>
        )}

        {phase==="play"&&r.milestoneT>0&&(
          <View style={s.msWrap}>
            <View style={[s.msPill,{opacity:Math.min(1,r.milestoneT*2)}]}><Text style={s.msTxt}>🏔  {r.milestoneText}</Text></View>
          </View>
        )}

        {phase==="play"&&r.achPopT>0&&(
          <View style={s.achWrap}>
            <View style={s.achPill}>
              <Text style={s.achPopIcon}>🏆</Text>
              <View><Text style={s.achPopTitle}>ACHIEVEMENT!</Text><Text style={s.achPopName}>{r.achPopText}</Text></View>
            </View>
          </View>
        )}

        {phase==="play"&&r.score===0&&(
          <View style={s.hintWrap}>
            <Text style={[s.hintTxt,{color:zc.txt}]}>← left  ·  right →  ·  tap mid-air = double jump</Text>
            <Text style={[s.hintTxt,{color:zc.txt,marginTop:3}]}>💣 bomb · 🦇 bat · ⚡ speed · air chain stomps · 👹 boss (enrages at 1HP!)</Text>
          </View>
        )}

        {/* MENU */}
        {phase==="menu"&&(
          <Pressable style={[s.overlay,{backgroundColor:"rgba(245,240,232,0.94)"}]} onPress={startGame}>
            <View style={s.titleCard}><Text style={s.t1}>DOODLE</Text><Text style={s.t2}>CLIMB</Text><View style={s.titleBar}/></View>
            {leaderboard.length>0&&(
              <View style={s.lbWrap}>
                {leaderboard.slice(0,3).map((sc,i)=><LbRow key={i} score={sc} rank={i} dark={false}/>)}
              </View>
            )}
            <View style={s.btn}><Text style={s.btnTxt}>TAP TO PLAY</Text></View>
            <Text style={s.ovHint}>🚀 jetpack · 🛡️ shield · 🧲 magnet · 🥾 boots · ❤️ life · ⭐ star</Text>
            <Text style={s.ovHint}>✦ golden · 🚀 rocket · 🧊 ice · 💣 bomb · ⚡ speed · ▶▶ conveyor · 👹 boss (enrages!) · 🦇 bat</Text>
          </Pressable>
        )}

        {/* GAME OVER */}
        {phase==="dead"&&(
          <Pressable style={[s.overlay,{backgroundColor:"rgba(6,8,18,0.91)"}]} onPress={startGame}>
            <Text style={s.goLabel}>SCORE</Text>
            <Text style={s.goScore}>{r.score}</Text>
            <Text style={s.goM}>m</Text>
            {r.best>0&&r.score>=r.best&&<Text style={s.newBest}>✦ NEW BEST ✦</Text>}
            {(()=>{const m=getMedal(r.score);return m?<Text style={[s.newBest,{color:m.col,fontSize:28,letterSpacing:4}]}>{m.icon} {m.label}</Text>:null;})()}
            <View style={s.statsRow}>
              <StatPill icon="🪙" val={r.coinsCollected} label="COINS"/>
              <StatPill icon="💎" val={r.gemsCollected} label="GEMS"/>
              <StatPill icon="⚡" val={r.maxCombo} label="COMBO"/>
              <StatPill icon="💀" val={r.enemiesDefeated} label="STOMPED"/>
            </View>
            <View style={[s.statsRow,{marginTop:6}]}>
              {r.bossKills>0&&<StatPill icon="👹" val={r.bossKills} label="BOSSES"/>}
              {r.nearMissCount>0&&<StatPill icon="😅" val={r.nearMissCount} label="CLOSE!"/>}
              {r.wormholeUsed&&<StatPill icon="🌀" val={1} label="WORMHOLE"/>}
              {r.bombRidden&&<StatPill icon="💣" val={1} label="BOMB"/>}
              {r.iceHits>0&&<StatPill icon="🧊" val={r.iceHits} label="ICE"/>}
            </View>
            {r.bestCombo>0&&<Text style={[s.goLabel,{fontSize:13,letterSpacing:3,marginTop:2}]}>ALL-TIME BEST COMBO  ⚡{r.bestCombo}</Text>}
            {dailyBest>0&&<Text style={[s.goLabel,{fontSize:12,letterSpacing:2,marginTop:1,opacity:0.65}]}>📅 TODAY'S BEST  {dailyBest} m</Text>}
            {r.achNewRun.length>0&&(
              <View style={s.achRow}>
                <Text style={s.achRowLabel}>🏆 UNLOCKED:</Text>
                {r.achNewRun.map(id=>{const a=ACHIEVEMENTS.find(x=>x.id===id);return a?(<View key={id} style={s.achBadge}><Text style={s.achBadgeIcon}>{a.icon}</Text><Text style={s.achBadgeTxt}>{a.title}</Text></View>):null;})}
              </View>
            )}
            {leaderboard.length>0&&(
              <View style={[s.lbWrap,{borderColor:"rgba(255,255,255,0.12)"}]}>
                {leaderboard.slice(0,3).map((sc,i)=><LbRow key={i} score={sc} rank={i} dark={true}/>)}
              </View>
            )}
            <View style={s.btn}><Text style={s.btnTxt}>TRY AGAIN</Text></View>
          </Pressable>
        )}
      </View>
    </View>
  );
}

// ─── Styles ───────────────────────────────────────────────────────────────────
const b=StyleSheet.create({
  line:     {position:"absolute",left:0,right:0,height:1.5},
  margin:   {position:"absolute",left:40,top:0,bottom:0,width:1.5},
  star:     {position:"absolute",backgroundColor:"#FFFFFF"},
  cloudBody:{position:"absolute",borderRadius:30},
  cloudPuff:{position:"absolute",borderRadius:99},
});
const g=StyleSheet.create({
  plat:      {position:"absolute",height:PLH,borderRadius:8,borderBottomWidth:3,overflow:"hidden"},
  platShine: {position:"absolute",top:0,left:6,right:6,height:4,borderRadius:3,backgroundColor:"rgba(255,255,255,0.42)"},
  springWrap:{position:"absolute",top:2,left:"25%",right:"25%",gap:1.5},
  springLine:{height:2.5,borderRadius:1.5,backgroundColor:"rgba(255,255,255,0.58)",marginBottom:1},
  movArrows: {position:"absolute",inset:0,alignItems:"center",justifyContent:"center"},
  arrowTxt:  {fontSize:7,color:"rgba(255,255,255,0.55)",letterSpacing:2},
  player:    {position:"absolute",width:PW,height:PH,borderRadius:12,alignItems:"center",paddingTop:5,borderBottomWidth:3,overflow:"visible"},
  playerGlow:{position:"absolute",top:-4,left:-4,right:-4,bottom:-4,borderRadius:16,opacity:0.16},
  antenna:   {position:"absolute",top:-9,width:3,height:11,borderRadius:2},
  eyeRow:    {flexDirection:"row",gap:8,marginTop:4},
  eye:       {width:12,height:12,borderRadius:6,backgroundColor:"white",justifyContent:"center",alignItems:"center"},
  pupil:     {width:5,height:5,borderRadius:2.5,backgroundColor:"#0A0A1A"},
  eyeShine:  {position:"absolute",top:1,right:1,width:3,height:3,borderRadius:1.5,backgroundColor:"white"},
  mouth:     {marginTop:4,width:14,height:5,borderRadius:3,borderBottomWidth:2,borderLeftWidth:1.5,borderRightWidth:1.5,borderTopWidth:0},
  mouthH:    {borderColor:"rgba(255,255,255,0.88)"},
  mouthN:    {borderColor:"rgba(255,255,255,0.52)"},
  feet:      {flexDirection:"row",gap:8,marginTop:2},
  foot:      {width:8,height:5,borderRadius:3},
  shield:    {position:"absolute",top:-8,left:-8,right:-8,bottom:-8,borderRadius:36,borderWidth:3,borderColor:"#50AAFF",backgroundColor:"rgba(80,160,255,0.12)"},
  magnetAura:{position:"absolute",top:-12,left:-12,right:-12,bottom:-12,borderRadius:40,borderWidth:2,borderColor:"#C050FF",backgroundColor:"rgba(192,80,255,0.09)",borderStyle:"dashed"},
  bootsGlow: {position:"absolute",left:-4,bottom:-6,right:-4,height:10,borderRadius:5,backgroundColor:"rgba(255,176,32,0.55)"},
  speedAura: {position:"absolute",top:-8,left:-14,right:-14,bottom:-4,borderRadius:28,borderWidth:2.5,borderColor:"#00E8FF",backgroundColor:"rgba(0,232,255,0.10)"},
  rocketGlow:{position:"absolute",left:-6,bottom:-8,right:-6,height:16,borderRadius:8,backgroundColor:"rgba(255,80,20,0.70)"},
  comboAura: {position:"absolute",top:-14,left:-14,right:-14,bottom:-14,borderRadius:38,borderWidth:3},
});
const s=StyleSheet.create({
  root:      {flex:1},
  hud:       {position:"absolute",top:0,left:0,right:0,alignItems:"center",pointerEvents:"none"},
  scoreNum:  {fontSize:56,fontWeight:"900",letterSpacing:-2,lineHeight:60},
  mLabel:    {fontSize:16,fontWeight:"700",letterSpacing:2,marginTop:-8,opacity:0.55},
  multTxt:   {fontSize:20,fontWeight:"900",letterSpacing:1,marginTop:2},
  bestLabel: {fontSize:15,fontWeight:"700",letterSpacing:3,marginTop:2,opacity:0.42},
  coinHud:   {position:"absolute",left:16,pointerEvents:"none"},
  coinHudTxt:{fontSize:14,fontWeight:"700",opacity:0.82},
  livesWrap: {position:"absolute",right:16,pointerEvents:"none"},
  livesRow:  {flexDirection:"row",gap:4},
  heart:     {fontSize:20,fontWeight:"900"},
  timersStack:{position:"absolute",right:16,gap:4,pointerEvents:"none"},
  timerRow:  {flexDirection:"row",alignItems:"center",gap:6},
  timerIcon: {fontSize:14},
  timerTrack:{width:46,height:6,borderRadius:3,overflow:"hidden"},
  timerFill: {height:"100%",borderRadius:3},
  shieldBadge:{position:"absolute",right:16,backgroundColor:"rgba(0,120,220,0.20)",borderRadius:12,paddingHorizontal:8,paddingVertical:4,pointerEvents:"none"},
  shieldTxt: {fontSize:18},
  zoneLabel: {position:"absolute",left:16,pointerEvents:"none"},
  zoneTxt:   {fontSize:12,fontWeight:"700",opacity:0.55,letterSpacing:0.5},
  stompWrap: {position:"absolute",left:0,right:0,top:SH*0.24,alignItems:"center",pointerEvents:"none"},
  stompTxt:  {fontSize:26,fontWeight:"900",color:"#FF8844",letterSpacing:2,textShadow:"0px 2px 6px rgba(0,0,0,0.25)"} as any,
  djumpWrap: {position:"absolute",left:0,right:0,top:SH*0.30,alignItems:"center",pointerEvents:"none"},
  djumpTxt:  {fontSize:16,fontWeight:"900",color:"#50FFBB",letterSpacing:2},
  comboWrap: {position:"absolute",left:0,right:0,top:SH*0.35,alignItems:"center",pointerEvents:"none"},
  comboTxt:  {fontSize:42,fontWeight:"900",letterSpacing:1,textShadow:"0px 2px 6px rgba(0,0,0,0.2)"} as any,
  msWrap:    {position:"absolute",left:0,right:0,top:SH*0.20,alignItems:"center",pointerEvents:"none"},
  msPill:    {backgroundColor:"rgba(39,192,99,0.92)",paddingHorizontal:24,paddingVertical:10,borderRadius:30},
  msTxt:     {fontSize:22,fontWeight:"900",color:"white",letterSpacing:1},
  hintWrap:  {position:"absolute",left:0,right:0,bottom:70,alignItems:"center",pointerEvents:"none"},
  hintTxt:   {fontSize:12,opacity:0.38,letterSpacing:0.5},
  overlay:   {...StyleSheet.absoluteFillObject,justifyContent:"center",alignItems:"center",gap:10},
  titleCard: {alignItems:"center",marginBottom:4},
  t1:{fontSize:72,fontWeight:"900",color:"#1A1A2A",letterSpacing:-3,lineHeight:70},
  t2:{fontSize:72,fontWeight:"900",color:P_GREEN,letterSpacing:-3,lineHeight:74},
  titleBar:  {marginTop:8,width:80,height:4,borderRadius:2,backgroundColor:P_GREEN,opacity:0.65},
  lbWrap:    {alignItems:"center",gap:3,padding:10,borderRadius:14,borderWidth:1,borderColor:"rgba(0,0,0,0.08)",backgroundColor:"rgba(255,255,255,0.10)"},
  lbRow:     {flexDirection:"row",alignItems:"center",gap:8},
  lbMedal:   {fontSize:18,width:26,textAlign:"center"},
  lbScore:   {fontSize:24,fontWeight:"900",letterSpacing:-1,minWidth:60},
  lbM:       {fontSize:14},
  ovHint:    {fontSize:13,color:"#1A1A2A",opacity:0.38,letterSpacing:0.3,marginTop:-4},
  goLabel:   {fontSize:18,fontWeight:"700",color:"#90C8FF",opacity:0.55,letterSpacing:6},
  goScore:   {fontSize:92,fontWeight:"900",color:P_GREEN,letterSpacing:-4,lineHeight:96},
  goM:       {fontSize:22,fontWeight:"700",color:P_GREEN,opacity:0.7,marginTop:-8},
  newBest:   {fontSize:22,fontWeight:"900",color:"#F5B820",letterSpacing:2},
  statsRow:  {flexDirection:"row",gap:12,marginTop:4},
  statPill:  {alignItems:"center",backgroundColor:"rgba(255,255,255,0.08)",borderRadius:14,paddingHorizontal:14,paddingVertical:8},
  statIcon:  {fontSize:20},
  statVal:   {fontSize:22,fontWeight:"900",color:"white",letterSpacing:-1},
  statLabel: {fontSize:10,fontWeight:"700",color:"rgba(255,255,255,0.45)",letterSpacing:2,marginTop:1},
  btn:       {backgroundColor:P_GREEN,paddingHorizontal:44,paddingVertical:17,borderRadius:16,borderBottomWidth:4,borderColor:P_DARK,marginTop:6},
  btnTxt:    {fontSize:22,fontWeight:"900",color:"white",letterSpacing:2},
  // Achievement pop-up (in-game)
  achWrap:   {position:"absolute",left:0,right:0,bottom:SH*0.22,alignItems:"center",pointerEvents:"none"},
  achPill:   {flexDirection:"row",alignItems:"center",gap:10,backgroundColor:"rgba(30,20,60,0.88)",borderRadius:20,paddingHorizontal:20,paddingVertical:10,borderWidth:1.5,borderColor:"rgba(160,100,255,0.55)"},
  achPopIcon:{fontSize:26},
  achPopTitle:{fontSize:9,fontWeight:"700",color:"#C080FF",letterSpacing:3},
  achPopName:{fontSize:15,fontWeight:"900",color:"white",letterSpacing:0.5},
  // Achievement row (game-over)
  achRow:    {alignItems:"center",gap:6,marginTop:2},
  achRowLabel:{fontSize:11,fontWeight:"700",color:"#C080FF",letterSpacing:3},
  achBadge:  {flexDirection:"row",alignItems:"center",gap:6,backgroundColor:"rgba(160,80,255,0.15)",borderRadius:12,paddingHorizontal:12,paddingVertical:5,borderWidth:1,borderColor:"rgba(160,80,255,0.35)"},
  achBadgeIcon:{fontSize:16},
  achBadgeTxt:{fontSize:13,fontWeight:"700",color:"rgba(255,255,255,0.85)"},
});
