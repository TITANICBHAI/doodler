import React, { useRef, useState, useEffect, useCallback } from "react";
import { View, Text, StyleSheet, Dimensions, Pressable, Platform } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { StatusBar } from "expo-status-bar";

const { width: SW, height: SH } = Dimensions.get("window");

// ─── Physics ──────────────────────────────────────────────────────────────────
const GRAVITY    = 1750;
const JUMP_VEL   = -690;
const SPRING_VEL = -1155;
const DJUMP_VEL  = -570;
const JETPACK_VY = -345;
const PLAYER_SPD = 248;
const FALL_MULT  = 1.88;
const PW = 42; const PH = 50; const PLH = 14;
const CAM_LEAD = 0.38; const DEATH_OFF = 350; const TRAIL_LEN = 7;

// ─── Persistent best ──────────────────────────────────────────────────────────
function getBest(): number {
  try { return Math.max(0, parseInt((globalThis as any).localStorage?.getItem?.("dc_best") || "0") || 0); } catch { return 0; }
}
function saveBest(v: number): void {
  try { (globalThis as any).localStorage?.setItem?.("dc_best", String(v)); } catch {}
}

// ─── Zone palette ─────────────────────────────────────────────────────────────
type C3 = readonly [number, number, number];
const ZONES = [
  { at:0,   sky:[245,240,232]as C3,line:[160,200,232]as C3,lop:0.30,txt:[26,26,42]   as C3,dark:false,label:"☁  Sunrise",   plrBg:[39,192,99]  as C3,plrBrd:[30,158,80]  as C3 },
  { at:150, sky:[238,190,148]as C3,line:[200,120,70] as C3,lop:0.35,txt:[55,20,5]    as C3,dark:false,label:"🌅  Sunset",    plrBg:[245,130,48] as C3,plrBrd:[210,95,25]  as C3 },
  { at:400, sky:[18,28,60]   as C3,line:[55,95,135]  as C3,lop:0.45,txt:[200,228,255]as C3,dark:true, label:"🌙  Night Sky", plrBg:[210,235,255]as C3,plrBrd:[140,190,235]as C3 },
  { at:800, sky:[6,8,18]     as C3,line:[20,45,80]   as C3,lop:0.55,txt:[90,170,255] as C3,dark:true, label:"🚀  Deep Space",plrBg:[0,230,255]  as C3,plrBrd:[0,175,210] as C3 },
] as const;

function lerp(a:number,b:number,t:number){return a+(b-a)*t;}
function lerpC(c1:C3,c2:C3,t:number):string{
  return `rgb(${Math.round(lerp(c1[0],c2[0],t))},${Math.round(lerp(c1[1],c2[1],t))},${Math.round(lerp(c1[2],c2[2],t))})`;
}
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

// ─── Platform colours ─────────────────────────────────────────────────────────
type PType="static"|"moving"|"spring"|"breakable";
const PCOL:Record<PType,string>={static:"#2DC968",moving:"#4EAAF5",spring:"#F5D123",breakable:"#F05232"};
const PBORD:Record<PType,string>={static:"#1AA850",moving:"#2D88D8",spring:"#D4A800",breakable:"#C83018"};
const P_GREEN="#27C063"; const P_DARK="#1E9E50";

// ─── Types ────────────────────────────────────────────────────────────────────
type Phase="menu"|"play"|"dead";
type PUType="jetpack"|"shield"|"magnet";
type EnemyType="bird"|"ghost"|"ufo"|"asteroid";

interface Plat    {id:number;x:number;y:number;w:number;type:PType;broken:boolean;ox:number;dir:number;range:number;sq:number;}
interface Particle{x:number;y:number;vx:number;vy:number;life:number;color:string;r:number;}
interface TrailPt  {x:number;y:number;}
interface Cloud    {id:number;x:number;y:number;w:number;h:number;par:number;op:number;}
interface Coin     {id:number;x:number;y:number;collected:boolean;popT:number;}
interface PU       {id:number;x:number;y:number;tp:PUType;col:boolean;bt:number;}
interface Planet   {id:number;x:number;y:number;r:number;c:string;ring:boolean;par:number;}
interface SShoot   {id:number;sx:number;sy:number;vx:number;vy:number;life:number;}
interface Enemy    {id:number;x:number;y:number;vx:number;vy:number;type:EnemyType;wt:number;dead:boolean;}

interface GS {
  px:number;py:number;pvx:number;pvy:number;psx:number;psy:number;facing:number;eyeY:number;
  scrollY:number;minY:number;startY:number;
  score:number;best:number;
  combo:number;comboT:number;
  maxCombo:number;coinsCollected:number;enemiesDefeated:number;
  lives:number;jetpackT:number;magnetT:number;shielded:boolean;canDJump:boolean;invincT:number;
  djumpFlashT:number;stompFlashT:number;
  milestoneText:string;milestoneT:number;lastMilestone:number;
  ssTimer:number;
  plats:Plat[];parts:Particle[];trail:TrailPt[];clouds:Cloud[];
  coins:Coin[];powerUps:PU[];planets:Planet[];shootStars:SShoot[];enemies:Enemy[];
  phase:Phase;input:number;
  shakeX:number;shakeY:number;shakeT:number;
  pid:number;
}

// ─── Factories ────────────────────────────────────────────────────────────────
function pickType(score:number):PType{
  const r=Math.random();
  if(score<80) return r<0.06?"moving":"static";
  if(score<200) return r<0.09?"spring":r<0.24?"moving":r<0.37?"breakable":"static";
  if(score<500) return r<0.13?"spring":r<0.30?"moving":r<0.48?"breakable":"static";
  return r<0.16?"spring":r<0.32?"moving":r<0.53?"breakable":"static";
}
function mkPlat(gs:GS,y:number,sc:number):Plat{
  const w=Math.max(48,92-sc*0.02),x=Math.random()*(SW-w);
  return{id:gs.pid++,x,y,w,type:pickType(sc),broken:false,ox:x,dir:Math.random()<0.5?1:-1,range:38+Math.random()*55,sq:0};
}
function mkStat(gs:GS,y:number,x:number,w:number):Plat{
  return{id:gs.pid++,x,y,w,type:"static",broken:false,ox:x,dir:1,range:0,sq:0};
}
function mkCloud(gs:GS,wy:number):Cloud{
  return{id:gs.pid++,x:Math.random()*(SW-120),y:wy,w:90+Math.random()*100,h:28+Math.random()*24,par:0.22+Math.random()*0.18,op:0.55+Math.random()*0.35};
}
function mkPU(gs:GS,y:number,tp:PUType):PU{
  return{id:gs.pid++,x:20+Math.random()*(SW-60),y,tp,col:false,bt:0};
}
function mkPlanet(gs:GS,wy:number):Planet{
  const cols=["#9B5DE5","#F15BB5","#00BBF9","#FF6B6B","#5BC0EB","#FF9F1C"];
  return{id:gs.pid++,x:30+Math.random()*(SW-80),y:wy,r:32+Math.random()*44,c:cols[Math.floor(Math.random()*cols.length)],ring:Math.random()<0.6,par:0.10+Math.random()*0.12};
}
function getEType(score:number):EnemyType{
  const r=Math.random();
  if(score<180) return"bird";
  if(score<450) return r<0.55?"bird":"ghost";
  if(score<850) return r<0.25?"bird":r<0.65?"ghost":"ufo";
  return r<0.45?"ufo":"asteroid";
}
function mkEnemy(gs:GS,y:number,score:number):Enemy{
  const tp=getEType(score),ml=Math.random()<0.5;
  const spd=tp==="bird"?200+Math.random()*110:tp==="asteroid"?160+Math.random()*90:100+Math.random()*70;
  return{id:gs.pid++,x:ml?SW+30:-65,y:y+10+Math.random()*55,vx:(ml?-1:1)*spd,vy:tp==="asteroid"?40+Math.random()*70:0,type:tp,wt:Math.random()*6,dead:false};
}

const MILESTONES=[100,250,500,750,1000,1500,2000,3000];
const COMBO_TIERS=[5,10,15,20,30];
const PU_TYPES:PUType[]=["jetpack","shield","magnet"];

function mkGS(best:number):GS{
  const startY=SH*0.70;
  const gs:GS={
    px:SW/2-PW/2,py:startY,pvx:0,pvy:JUMP_VEL,psx:1,psy:1,facing:1,eyeY:0,
    scrollY:0,minY:startY,startY,
    score:0,best,
    combo:0,comboT:0,maxCombo:0,coinsCollected:0,enemiesDefeated:0,
    lives:3,jetpackT:0,magnetT:0,shielded:false,canDJump:true,invincT:0,
    djumpFlashT:0,stompFlashT:0,
    milestoneText:"",milestoneT:0,lastMilestone:0,
    ssTimer:9,
    plats:[],parts:[],trail:[],clouds:[],coins:[],powerUps:[],planets:[],shootStars:[],enemies:[],
    phase:"menu",input:0,shakeX:0,shakeY:0,shakeT:0,pid:0,
  };
  gs.plats.push(mkStat(gs,startY+PH,SW/2-52,104));
  let y=startY;
  for(let i=0;i<6;i++){y-=58+Math.random()*26;const w=80+Math.random()*22,cx=SW*0.2+Math.random()*SW*0.6;gs.plats.push(mkStat(gs,y,cx-w/2,w));}
  for(let i=0;i<18;i++){
    y-=70+Math.random()*46;gs.plats.push(mkPlat(gs,y,0));
    if(Math.random()<0.32) gs.coins.push({id:gs.pid++,x:gs.plats[gs.plats.length-1].x+20+Math.random()*30,y:y-28,collected:false,popT:0});
  }
  let cy=startY;
  for(let i=0;i<8;i++){cy-=SH*(0.35+Math.random()*0.3);gs.clouds.push(mkCloud(gs,cy));}
  return gs;
}

// ─── Particles ────────────────────────────────────────────────────────────────
function emit(gs:GS,cx:number,cy:number,col:string,n:number){
  for(let i=0;i<n;i++){
    const ang=(Math.PI*2*i)/n+Math.random()*0.5,spd=65+Math.random()*190;
    gs.parts.push({x:cx,y:cy,color:col,vx:Math.cos(ang)*spd,vy:Math.sin(ang)*spd-85,life:1,r:3+Math.random()*5});
  }
}
function comboCol(n:number){return n>=15?"#FF2200":n>=10?"#FF9900":n>=5?"#F5D123":"#50F0AA";}
function comboMult(n:number){return n>=15?2.5:n>=10?2:n>=5?1.5:1;}

// ─── Update ───────────────────────────────────────────────────────────────────
function update(gs:GS,dt:number,now:number){
  if(gs.phase!=="play") return;

  gs.pvx=gs.input*PLAYER_SPD;
  if(gs.input!==0) gs.facing=gs.input;

  if(gs.jetpackT>0){
    gs.jetpackT-=dt;
    gs.pvy+=(JETPACK_VY-gs.pvy)*Math.min(1,dt*5);
    if(Math.random()<0.5) emit(gs,gs.px+PW/2,gs.py+PH+4,Math.random()<0.5?"#FF8800":"#FFE020",1);
  } else {
    gs.pvy+=GRAVITY*(gs.pvy>0?FALL_MULT:1)*dt;
    gs.pvy=Math.min(gs.pvy,1450);
  }

  gs.px+=gs.pvx*dt;gs.py+=gs.pvy*dt;
  if(gs.px+PW<0) gs.px=SW; if(gs.px>SW) gs.px=-PW;
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
        gs.py=p.y-PH;
        const sp=p.type==="spring";
        gs.pvy=sp?SPRING_VEL:JUMP_VEL;
        gs.psx=sp?1.58:1.40;gs.psy=sp?0.50:0.63;p.sq=sp?0.65:0.45;
        gs.canDJump=true;gs.combo++;gs.comboT=2.8;
        if(gs.combo>gs.maxCombo) gs.maxCombo=gs.combo;
        for(const ct of COMBO_TIERS){if(gs.combo===ct){emit(gs,gs.px+PW/2,gs.py,comboCol(ct),20);gs.shakeT=0.12;break;}}
        emit(gs,p.x+p.w/2,p.y,PCOL[p.type],sp?14:6);
        if(p.type==="breakable") p.broken=true;
        break;
      }
    }
  }

  // Coins — with magnet attraction
  const pcx=gs.px+PW/2,pcy=gs.py+PH/2;
  if(gs.magnetT>0){
    gs.magnetT-=dt;
    for(const c of gs.coins){
      if(c.collected) continue;
      const dx=pcx-c.x,dy=pcy-c.y,dist=Math.hypot(dx,dy);
      if(dist<160&&dist>0){const spd=(200*(1-dist/160)+60);c.x+=dx/dist*spd*dt;c.y+=dy/dist*spd*dt;}
    }
  }
  for(const c of gs.coins){
    if(c.collected){c.popT=Math.max(0,c.popT-dt*3);continue;}
    if(Math.hypot(pcx-c.x,pcy-c.y)<22){
      c.collected=true;c.popT=1;
      gs.coinsCollected++;
      const bonus=Math.round(5*comboMult(gs.combo));
      gs.score+=bonus;
      emit(gs,c.x,c.y,"#F5D123",5);
    }
  }

  // Power-ups
  for(const pu of gs.powerUps){
    if(pu.col){pu.bt=Math.max(0,pu.bt-dt);continue;}
    pu.bt=(pu.bt+dt*2)%(Math.PI*2);
    if(Math.hypot(pcx-pu.x,pcy-pu.y)<30){
      pu.col=true;
      if(pu.tp==="jetpack"){gs.jetpackT=4.0;emit(gs,pu.x,pu.y,"#FF8800",18);}
      else if(pu.tp==="shield"){gs.shielded=true;emit(gs,pu.x,pu.y,"#50A0FF",18);}
      else{gs.magnetT=6.0;emit(gs,pu.x,pu.y,"#C050FF",18);}
    }
  }

  // Enemies
  for(const e of gs.enemies){
    if(e.dead) continue;
    e.x+=e.vx*dt;e.y+=e.vy*dt;e.wt+=dt;
    if(e.type==="ghost"||e.type==="ufo") e.y+=Math.sin(e.wt*2.5)*28*dt;
  }
  // Enemy collision — stomp first, then side-hit
  if(gs.invincT<=0){
    const EW=36,EH=30;
    for(const e of gs.enemies){
      if(e.dead) continue;
      const overX=gs.px+PW>e.x+5&&gs.px<e.x+EW-5;
      if(!overX) continue;
      const feet=gs.py+PH;
      // Stomp: falling + feet hit top-half of enemy
      if(gs.pvy>0&&feet>=e.y-2&&feet<=e.y+EH*0.42){
        e.dead=true;
        gs.pvy=JUMP_VEL*0.88;gs.py=e.y-PH;
        gs.psx=1.45;gs.psy=0.60;gs.canDJump=true;
        gs.combo++;gs.comboT=2.8;gs.stompFlashT=0.65;
        gs.enemiesDefeated++;gs.score+=15;
        if(gs.combo>gs.maxCombo) gs.maxCombo=gs.combo;
        emit(gs,e.x+EW/2,e.y,"#FF8844",20);gs.shakeT=0.10;break;
      }
      // Side hit
      if(feet>e.y+EH*0.35&&gs.py<e.y+EH-5){
        e.dead=true;emit(gs,e.x+EW/2,e.y,"#FF5533",14);
        if(gs.shielded){gs.shielded=false;gs.invincT=1.5;gs.enemiesDefeated++;}
        else if(gs.lives>1){gs.lives--;gs.pvy=JUMP_VEL*1.1;gs.invincT=2.2;gs.shakeT=0.4;}
        else{gs.phase="dead";gs.shakeT=0.7;emit(gs,gs.px+PW/2,gs.py,P_GREEN,24);}
        break;
      }
    }
  }

  // Camera
  const ideal=gs.py-SH*CAM_LEAD;
  if(ideal<gs.scrollY) gs.scrollY=ideal;

  // Score + milestones + save best
  if(gs.py<gs.minY) gs.minY=gs.py;
  gs.score=Math.max(gs.score,Math.floor((gs.startY-gs.minY)/5));
  if(gs.score>gs.best){gs.best=gs.score;saveBest(gs.best);}
  for(const m of MILESTONES){
    if(gs.score>=m&&m>gs.lastMilestone){gs.lastMilestone=m;gs.milestoneText=`${m} m`;gs.milestoneT=2.2;}
  }

  // Generate world
  const topGenY=gs.scrollY-SH*0.55;
  let topY=gs.plats.length?Math.min(...gs.plats.map(p=>p.y)):gs.startY;
  let safety=0;
  while(topY>topGenY&&safety++<25){
    const gap=72+Math.random()*48+Math.min(gs.score*0.05,38);
    topY-=gap;
    gs.plats.push(mkPlat(gs,topY,gs.score));
    if(Math.random()<0.30) gs.coins.push({id:gs.pid++,x:gs.plats[gs.plats.length-1].x+Math.random()*50,y:topY-30,collected:false,popT:0});
    if(gs.score>20&&Math.random()<0.08&&!gs.powerUps.some(p=>!p.col&&Math.abs(p.y-topY)<300))
      gs.powerUps.push(mkPU(gs,topY-45,PU_TYPES[Math.floor(Math.random()*PU_TYPES.length)]));
    if(gs.score>80&&Math.random()<0.10&&!gs.enemies.some(e=>!e.dead&&Math.abs(e.y-topY)<200))
      gs.enemies.push(mkEnemy(gs,topY,gs.score));
    if(gs.score>600&&Math.random()<0.12&&!gs.planets.some(p=>Math.abs(p.y-topY)<SH*0.6))
      gs.planets.push(mkPlanet(gs,topY+Math.random()*SH*0.5));
  }
  const topCloud=gs.clouds.length?Math.min(...gs.clouds.map(c=>c.y)):gs.startY;
  if(topCloud>gs.scrollY-SH*1.5) gs.clouds.push(mkCloud(gs,topCloud-SH*(0.4+Math.random()*0.5)));

  // Moving platforms
  for(const p of gs.plats) if(p.type==="moving") p.x=p.ox+Math.sin(now/1000*1.6+p.id*1.3)*p.range;

  // Shooting stars
  if(gs.score>280){
    gs.ssTimer-=dt;
    if(gs.ssTimer<0){gs.ssTimer=6+Math.random()*6;gs.shootStars.push({id:gs.pid++,sx:Math.random()*SW,sy:Math.random()*SH*0.4,vx:-130-Math.random()*80,vy:85+Math.random()*60,life:1});}
    for(const ss of gs.shootStars){ss.sx+=ss.vx*dt;ss.sy+=ss.vy*dt;ss.life-=dt*1.4;}
    gs.shootStars=gs.shootStars.filter(s=>s.life>0&&s.sx>-60);
  }

  // Particles
  for(const pt of gs.parts){pt.x+=pt.vx*dt;pt.y+=pt.vy*dt;pt.vy+=560*dt;pt.life-=dt*2.2;}
  gs.parts=gs.parts.filter(p=>p.life>0);

  // Lerps
  const sr=1-Math.exp(-13*dt);
  gs.psx+=(1-gs.psx)*sr;gs.psy+=(1-gs.psy)*sr;
  for(const p of gs.plats) p.sq+=(0-p.sq)*Math.min(1,dt*14);
  if(gs.shakeT>0){gs.shakeT-=dt;const s=gs.shakeT*9;gs.shakeX=(Math.random()-0.5)*s;gs.shakeY=(Math.random()-0.5)*s;}else{gs.shakeX=0;gs.shakeY=0;}

  // Timers
  if(gs.comboT>0)       gs.comboT-=dt;
  if(gs.milestoneT>0)   gs.milestoneT-=dt;
  if(gs.invincT>0)      gs.invincT-=dt;
  if(gs.djumpFlashT>0)  gs.djumpFlashT-=dt;
  if(gs.stompFlashT>0)  gs.stompFlashT-=dt;

  // Cleanup
  const cutY=gs.scrollY+SH+300;
  gs.plats   =gs.plats.filter(p=>p.y<cutY);
  gs.coins   =gs.coins.filter(c=>c.y<cutY||!c.collected);
  gs.powerUps=gs.powerUps.filter(p=>p.y<cutY);
  gs.enemies =gs.enemies.filter(e=>!e.dead&&(e.vx<0?e.x>-90:e.x<SW+90)&&e.y<cutY);
  gs.planets =gs.planets.filter(p=>p.y-gs.scrollY*p.par<SH+200);
  gs.clouds  =gs.clouds.filter(c=>c.y-gs.scrollY*c.par<SH+150);

  // Death / life-loss
  if(gs.py>gs.scrollY+SH+DEATH_OFF&&gs.invincT<=0){
    if(gs.shielded){gs.shielded=false;gs.pvy=SPRING_VEL*1.1;gs.py=gs.scrollY+SH*0.55;gs.invincT=2.2;gs.shakeT=0.3;emit(gs,gs.px+PW/2,gs.py,"#50A0FF",20);}
    else if(gs.lives>1){gs.lives--;gs.pvy=SPRING_VEL;gs.py=gs.scrollY+SH*0.55;gs.invincT=2.5;gs.shakeT=0.45;gs.canDJump=true;emit(gs,gs.px+PW/2,gs.py,"#FF4422",16);}
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
    <View style={[StyleSheet.absoluteFillObject,{backgroundColor:zc.sky}]} pointerEvents="none">
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

// ─── Bg decorations: clouds + planets + sun + moon + shooting stars ────────────
function BgLayer({clouds,planets,shootStars,scrollY,dark,score}:{
  clouds:Cloud[];planets:Planet[];shootStars:SShoot[];scrollY:number;dark:boolean;score:number;
}){
  const zc=zoneColors(score);
  const moonOp=Math.max(0,Math.min(1,(score-320)/120));
  const moonSky=`rgb(${zc.rawSky.join(",")})`;
  const cc=dark?"rgba(80,120,200,":"rgba(255,255,255,";
  // Sun transitions
  const riseSunOp=Math.max(0,1-score/110);
  const setSunOp=Math.max(0,Math.min(1,score<230?(score-80)/150:1-(score-230)/200));
  const now=Date.now();
  return(
    <View style={StyleSheet.absoluteFillObject} pointerEvents="none">
      {/* Sunrise sun (top-right, small, bright) */}
      {riseSunOp>0&&(
        <View style={{position:"absolute",right:22,top:100,opacity:riseSunOp}}>
          <View style={{width:42,height:42,borderRadius:21,backgroundColor:"#FFE840"}}/>
          <View style={{position:"absolute",left:-10,top:-10,right:-10,bottom:-10,borderRadius:42,backgroundColor:"rgba(255,220,60,0.22)"}}/>
          <View style={{position:"absolute",left:-22,top:-22,right:-22,bottom:-22,borderRadius:70,backgroundColor:"rgba(255,200,40,0.10)"}}/>
        </View>
      )}
      {/* Sunset sun (large, warm, horizon) */}
      {setSunOp>0.05&&(
        <View style={{position:"absolute",right:28,top:SH*0.52,opacity:setSunOp*0.88}}>
          <View style={{width:78,height:78,borderRadius:39,backgroundColor:"#FF7820"}}/>
          <View style={{position:"absolute",left:-14,top:-14,right:-14,bottom:-14,borderRadius:70,backgroundColor:"rgba(255,120,30,0.20)"}}/>
          <View style={{position:"absolute",left:-28,top:-28,right:-28,bottom:-28,borderRadius:100,backgroundColor:"rgba(255,90,20,0.10)"}}/>
          {/* Lens flare */}
          <View style={{position:"absolute",left:-55,top:20,width:22,height:22,borderRadius:11,backgroundColor:"rgba(255,180,60,0.22)"}}/>
          <View style={{position:"absolute",left:-90,top:30,width:12,height:12,borderRadius:6,backgroundColor:"rgba(255,200,100,0.18)"}}/>
          {/* Sun shimmer */}
          <View style={{position:"absolute",left:18,top:14,width:20,height:20,borderRadius:10,backgroundColor:"rgba(255,220,160,0.35)"}}/>
        </View>
      )}
      {/* Planets */}
      {planets.map(p=>{
        const sy=p.y-scrollY*p.par;
        if(sy>SH+80||sy<-80) return null;
        return(
          <View key={p.id} style={{position:"absolute",left:p.x-p.r,top:sy-p.r,width:p.r*2,height:p.r*2,borderRadius:p.r,backgroundColor:p.c,opacity:0.72}}>
            {p.ring&&<View style={{position:"absolute",left:-p.r*0.35,right:-p.r*0.35,top:p.r*0.5,bottom:p.r*0.5,borderRadius:p.r*2,borderWidth:4,borderColor:p.c,opacity:0.55,transform:[{scaleY:0.28}]}}/>}
            <View style={{position:"absolute",left:p.r*0.2,top:p.r*0.15,width:p.r*0.5,height:p.r*0.5,borderRadius:p.r*0.25,backgroundColor:"rgba(255,255,255,0.26)"}}/>
          </View>
        );
      })}
      {/* Clouds */}
      {clouds.map(c=>{
        const sy=c.y-scrollY*c.par;
        if(sy>SH+80||sy<-80) return null;
        return(
          <View key={c.id} style={{position:"absolute",left:c.x,top:sy,width:c.w,height:c.h,opacity:c.op}}>
            <View style={[b.cloudBody,{width:c.w*0.75,height:c.h,left:c.w*0.12,backgroundColor:cc+"0.9)"}]}/>
            <View style={[b.cloudPuff,{width:c.h*1.1,height:c.h*1.1,left:c.w*0.18,top:-c.h*0.4,backgroundColor:cc+"0.85)"}]}/>
            <View style={[b.cloudPuff,{width:c.h*0.9,height:c.h*0.9,left:c.w*0.48,top:-c.h*0.35,backgroundColor:cc+"0.8)"}]}/>
          </View>
        );
      })}
      {/* Crescent moon */}
      {moonOp>0&&(
        <View style={{position:"absolute",right:28,top:120,opacity:moonOp}}>
          <View style={{width:46,height:46,borderRadius:23,backgroundColor:"#FFF8D8"}}/>
          <View style={{position:"absolute",left:11,top:-7,width:42,height:42,borderRadius:21,backgroundColor:moonSky}}/>
          <View style={{position:"absolute",left:-9,top:-9,right:-9,bottom:-9,borderRadius:37,backgroundColor:"rgba(255,248,216,0.14)"}}/>
        </View>
      )}
      {/* Shooting stars */}
      {shootStars.map(ss=>(
        <View key={ss.id} style={{position:"absolute",left:ss.sx-1,top:ss.sy-22,width:2,height:30,borderRadius:2,backgroundColor:"white",opacity:ss.life*0.95,transform:[{rotate:"30deg"}]}}/>
      ))}
    </View>
  );
}

// ─── Platform ─────────────────────────────────────────────────────────────────
const PlatView=React.memo(function PlatView({p}:{p:Plat}){
  const br=p.broken,bg=br?"#999":PCOL[p.type],bord=br?"#777":PBORD[p.type];
  return(
    <View style={[g.plat,{left:p.x,top:p.y+(PLH*p.sq*0.22),width:p.w,backgroundColor:bg,borderColor:bord,opacity:br?0.45:1,transform:[{scaleY:1-p.sq*0.44}]}]}>
      <View style={g.platShine}/>
      {p.type==="spring"&&!br&&<View style={g.springWrap}><View style={g.springLine}/><View style={g.springLine}/><View style={g.springLine}/></View>}
      {p.type==="moving"&&!br&&<View style={g.movArrows}><Text style={g.arrowTxt}>{"◀  ▶"}</Text></View>}
    </View>
  );
});

// ─── Enemy ────────────────────────────────────────────────────────────────────
const ENEMY_ICON:Record<EnemyType,string>={bird:"🐦",ghost:"👻",ufo:"🛸",asteroid:"☄️"};
function EnemyView({e}:{e:Enemy}){
  if(e.dead) return null;
  return(
    <View style={{position:"absolute",left:e.x,top:e.y,width:38,height:32,alignItems:"center",justifyContent:"center"}}>
      <Text style={{fontSize:26,transform:[{scaleX:e.vx>0?-1:1}]}}>{ENEMY_ICON[e.type]}</Text>
    </View>
  );
}

// ─── Power-up ─────────────────────────────────────────────────────────────────
const PU_ICON:Record<PUType,string>={jetpack:"🚀",shield:"🛡️",magnet:"🧲"};
const PU_COL:Record<PUType,string>={jetpack:"#FF8C00",shield:"#00A0FF",magnet:"#C050FF"};
const PU_GLW:Record<PUType,string>={jetpack:"rgba(255,140,0,0.28)",shield:"rgba(0,160,255,0.28)",magnet:"rgba(192,80,255,0.28)"};
function PUView({pu}:{pu:PU}){
  if(pu.col&&pu.bt<=0) return null;
  const bob=Math.sin(pu.bt)*6;
  return(
    <View style={{position:"absolute",left:pu.x-22,top:pu.y-22+bob,width:44,height:44,borderRadius:22,backgroundColor:PU_GLW[pu.tp],borderWidth:2,borderColor:PU_COL[pu.tp],alignItems:"center",justifyContent:"center",opacity:pu.col?Math.min(1,pu.bt):1}}>
      <Text style={{fontSize:22}}>{PU_ICON[pu.tp]}</Text>
    </View>
  );
}

// ─── Coin (spinning) ──────────────────────────────────────────────────────────
function CoinView({c,now}:{c:Coin;now:number}){
  if(c.collected&&c.popT<=0) return null;
  const spin=c.collected?1+(1-c.popT)*0.5:Math.abs(Math.cos(now/320))*0.75+0.25;
  return(
    <View style={{position:"absolute",left:c.x-9,top:c.y-9,width:18,height:18,borderRadius:9,backgroundColor:"#F5D123",borderWidth:2,borderColor:"#D4A800",opacity:c.collected?c.popT:1,transform:[{scaleX:spin}],alignItems:"center",justifyContent:"center"}}>
      <View style={{width:6,height:6,borderRadius:3,backgroundColor:"rgba(255,255,255,0.7)"}}/>
    </View>
  );
}

// ─── Player ───────────────────────────────────────────────────────────────────
function PlayerView({gs,plrBg,plrBrd}:{gs:GS;plrBg:string;plrBrd:string}){
  const flip=gs.facing<0?-1:1;
  const ey=Math.round(gs.eyeY*10)/10;
  const inv=gs.invincT>0&&Math.sin(gs.invincT*18)>0;
  return(
    <View style={[g.player,{left:gs.px,top:gs.py,backgroundColor:plrBg,borderColor:plrBrd,transform:[{scaleX:gs.psx*flip},{scaleY:gs.psy}],opacity:inv?0.32:1}]}>
      <View style={[g.playerGlow,{backgroundColor:plrBg}]}/>
      <View style={[g.antenna,{left:PW*0.26,backgroundColor:plrBrd}]}/>
      <View style={[g.antenna,{right:PW*0.26,backgroundColor:plrBrd}]}/>
      <View style={g.eyeRow}>
        <View style={g.eye}><View style={[g.pupil,{marginTop:2+ey}]}/><View style={g.eyeShine}/></View>
        <View style={g.eye}><View style={[g.pupil,{marginTop:2+ey}]}/><View style={g.eyeShine}/></View>
      </View>
      <View style={[g.mouth,gs.pvy<-50?g.mouthH:g.mouthN]}/>
      <View style={g.feet}><View style={[g.foot,{backgroundColor:plrBrd}]}/><View style={[g.foot,{backgroundColor:plrBrd}]}/></View>
      {gs.shielded&&<View style={g.shield}/>}
      {gs.magnetT>0&&<View style={g.magnetAura}/>}
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
  return<>{[0,1,2,3,4].map(i=>{const ox=Math.sin(t+i*2.1)*26,len=12+Math.cos(t*0.8+i*1.5)*7;return <View key={i} style={{position:"absolute",left:gs.px+PW/2+ox-1.5,top:gs.py-len-8,width:3,height:len,borderRadius:2,backgroundColor:col,opacity:0.22*intensity}}/>;})}</>;
}

// ─── Height bar ───────────────────────────────────────────────────────────────
function HeightBar({score,best,dark,topPad}:{score:number;best:number;dark:boolean;topPad:number}){
  const maxH=Math.max(best,score,50);
  const barH=SH-topPad-80;
  const curFrac=Math.min(1,score/maxH);
  const bestFrac=best>0?Math.min(1,best/maxH):0;
  return(
    <View style={{position:"absolute",right:7,top:topPad+20,height:barH,width:4,borderRadius:2,backgroundColor:dark?"rgba(255,255,255,0.12)":"rgba(0,0,0,0.10)"}}>
      <View style={{position:"absolute",left:0,bottom:0,right:0,height:barH*curFrac,borderRadius:2,backgroundColor:dark?"rgba(100,200,255,0.45)":"rgba(39,192,99,0.45)"}}/>
      {best>0&&<View style={{position:"absolute",left:-4,top:barH*(1-bestFrac),width:12,height:2,backgroundColor:"#F5D123",borderRadius:1}}/>}
      <View style={{position:"absolute",left:-4,top:barH*(1-curFrac)-5,width:12,height:12,borderRadius:6,backgroundColor:dark?"#80C8FF":"#27C063",borderWidth:2,borderColor:"rgba(255,255,255,0.8)"}}/>
    </View>
  );
}

// ─── HUD components ───────────────────────────────────────────────────────────
function LivesDisplay({lives,dark}:{lives:number;dark:boolean}){
  return(<View style={s.livesRow}>{[0,1,2].map(i=>(<Text key={i} style={[s.heart,{opacity:i<lives?1:0.22,color:i<lives?"#FF4455":dark?"#884455":"#FF4455"}]}>♥</Text>))}</View>);
}
function TimerBar({t,max,col,icon}:{t:number;max:number;col:string;icon:string}){
  if(t<=0) return null;
  return(
    <View style={s.timerRow}>
      <Text style={s.timerIcon}>{icon}</Text>
      <View style={[s.timerTrack,{backgroundColor:col+"33"}]}><View style={[s.timerFill,{width:`${Math.max(0,Math.min(1,t/max))*100}%` as any,backgroundColor:col}]}/></View>
    </View>
  );
}

// ─── Stat pill ────────────────────────────────────────────────────────────────
function StatPill({icon,val,label}:{icon:string;val:number;label:string}){
  return(
    <View style={s.statPill}>
      <Text style={s.statIcon}>{icon}</Text>
      <Text style={s.statVal}>{val}</Text>
      <Text style={s.statLabel}>{label}</Text>
    </View>
  );
}

// ─── Main game screen ─────────────────────────────────────────────────────────
export default function GameScreen(){
  const insets=useSafeAreaInsets();
  const gs=useRef<GS>(mkGS(getBest()));
  const [,setT]=useState(0);
  const rafRef=useRef<number>(0);
  const prevTs=useRef<number>(0);
  const nowRef=useRef<number>(0);
  const [phase,setPhase]=useState<Phase>("menu");

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
    if(g.canDJump&&g.pvy>60&&g.invincT<=0&&g.jetpackT<=0){
      g.pvy=DJUMP_VEL;g.canDJump=false;g.psx=0.80;g.psy=1.24;g.djumpFlashT=0.65;
      emit(g,g.px+PW/2,g.py+PH,"#50FFBB",8);
    }
  },[]);

  const startGame=useCallback(()=>{
    const best=gs.current.best;
    gs.current=mkGS(best);gs.current.phase="play";
    prevTs.current=0;setPhase("play");
  },[]);

  const onTouchStart=useCallback((e:any)=>{
    if(gs.current.phase==="menu"||gs.current.phase==="dead") return;
    gs.current.input=e.nativeEvent.locationX<SW/2?-1:1;
    triggerDJump();
  },[triggerDJump]);
  const onTouchMove=useCallback((e:any)=>{
    if(gs.current.phase!=="play") return;
    gs.current.input=e.nativeEvent.locationX<SW/2?-1:1;
  },[]);
  const onTouchEnd=useCallback(()=>{gs.current.input=0;},[]);

  const r=gs.current;
  const zc=zoneColors(r.score);
  const webTop=Platform.OS==="web"?67:0;
  const topPad=insets.top+webTop+8;
  const now=nowRef.current;
  const fallDark=Math.min(0.30,Math.max(0,r.pvy-620)/3800);
  const mult=comboMult(r.combo);

  // Platform target highlight (closest platform player is falling toward)
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
        <View style={[StyleSheet.absoluteFillObject,{transform:[{translateX:r.shakeX},{translateY:-r.scrollY+r.shakeY}]}]} pointerEvents="none">
          <Shadow gs={r}/>
          {/* Platform highlight ring */}
          {hlPlatId>=0&&r.plats.filter(p=>p.id===hlPlatId).map(p=>(
            <View key={p.id+"hl"} style={{position:"absolute",left:p.x-5,top:p.y-8,width:p.w+10,height:PLH+16,borderRadius:12,borderWidth:2.5,borderColor:PCOL[p.type],opacity:0.72}}/>
          ))}
          {r.plats.map(p=><PlatView key={p.id} p={p}/>)}
          {r.powerUps.map(p=><PUView key={p.id} pu={p}/>)}
          {r.coins.map(c=><CoinView key={c.id} c={c} now={now}/>)}
          {r.enemies.map(e=><EnemyView key={e.id} e={e}/>)}
          {r.trail.map((t,i)=>(
            <View key={i} style={{position:"absolute",left:t.x-PW*0.22,top:t.y-PH*0.22,width:PW*0.44,height:PH*0.44,borderRadius:PW*0.22,backgroundColor:zc.plrBg,opacity:(1-i/TRAIL_LEN)*0.28}}/>
          ))}
          {r.parts.map((p,i)=>(
            <View key={i} style={{position:"absolute",left:p.x-p.r,top:p.y-p.r,width:p.r*2,height:p.r*2,borderRadius:p.r,backgroundColor:p.color,opacity:Math.max(0,p.life)}}/>
          ))}
          <SpeedLines gs={r} col={zc.txt} now={now}/>
          <PlayerView gs={r} plrBg={zc.plrBg} plrBrd={zc.plrBrd}/>
        </View>

        {/* Fall vignette */}
        {phase==="play"&&fallDark>0&&(<View style={{...StyleSheet.absoluteFillObject as any,backgroundColor:`rgba(0,0,0,${fallDark.toFixed(3)})`}} pointerEvents="none"/>)}
        {/* Milestone flash */}
        {phase==="play"&&r.milestoneT>1.85&&(<View style={{...StyleSheet.absoluteFillObject as any,backgroundColor:"rgba(255,255,255,0.28)",opacity:Math.min(1,(r.milestoneT-1.85)*8)}} pointerEvents="none"/>)}

        {/* HUD */}
        <View style={[s.hud,{paddingTop:topPad}]} pointerEvents="none">
          <Text style={[s.scoreNum,{color:zc.txt}]}>{r.score}</Text>
          <Text style={[s.mLabel,{color:zc.txt}]}>m</Text>
          {mult>1&&r.comboT>0&&<Text style={[s.multTxt,{color:comboCol(r.combo)}]}>×{mult.toFixed(1)}</Text>}
          {r.best>0&&<Text style={[s.bestLabel,{color:zc.txt}]}>BEST  {r.best} m</Text>}
        </View>

        {/* Lives */}
        {phase==="play"&&(<View style={[s.livesWrap,{top:topPad+6}]} pointerEvents="none"><LivesDisplay lives={r.lives} dark={zc.dark}/></View>)}

        {/* Power-up timers */}
        <View style={[s.timersStack,{top:topPad+52}]} pointerEvents="none">
          <TimerBar t={r.jetpackT} max={4} col="#FF8C00" icon="🚀"/>
          <TimerBar t={r.magnetT} max={6} col="#C050FF" icon="🧲"/>
        </View>

        {/* Shield badge */}
        {phase==="play"&&r.shielded&&(<View style={[s.shieldBadge,{top:topPad+54}]} pointerEvents="none"><Text style={s.shieldTxt}>🛡️</Text></View>)}

        {/* Zone label */}
        {phase==="play"&&(<View style={[s.zoneLabel,{top:topPad+12}]} pointerEvents="none"><Text style={[s.zoneTxt,{color:zc.txt}]}>{zc.label}</Text></View>)}

        {/* Height bar */}
        {phase==="play"&&<HeightBar score={r.score} best={r.best} dark={zc.dark} topPad={topPad}/>}

        {/* Stomp flash */}
        {phase==="play"&&r.stompFlashT>0&&(
          <View style={s.stompWrap} pointerEvents="none">
            <Text style={[s.stompTxt,{opacity:Math.min(1,r.stompFlashT*4)}]}>💀  STOMP!</Text>
          </View>
        )}
        {/* Double jump flash */}
        {phase==="play"&&r.djumpFlashT>0&&(
          <View style={s.djumpWrap} pointerEvents="none">
            <Text style={[s.djumpTxt,{opacity:Math.min(1,r.djumpFlashT*3)}]}>↑ DOUBLE JUMP</Text>
          </View>
        )}
        {/* Combo */}
        {phase==="play"&&r.comboT>0&&r.combo>=3&&(
          <View style={s.comboWrap} pointerEvents="none">
            <Text style={[s.comboTxt,{color:comboCol(r.combo)}]}>
              {r.combo>=15?`🔥 x${r.combo}`:r.combo>=10?`⚡ x${r.combo}`:r.combo>=5?`✨ x${r.combo}`:`x${r.combo}`}
            </Text>
          </View>
        )}
        {/* Milestone */}
        {phase==="play"&&r.milestoneT>0&&(
          <View style={s.msWrap} pointerEvents="none">
            <View style={[s.msPill,{opacity:Math.min(1,r.milestoneT*2)}]}>
              <Text style={s.msTxt}>🏔  {r.milestoneText}</Text>
            </View>
          </View>
        )}
        {/* Hint */}
        {phase==="play"&&r.score===0&&(
          <View style={s.hintWrap} pointerEvents="none">
            <Text style={[s.hintTxt,{color:zc.txt}]}>← left  ·  right →  ·  tap mid-air = double jump</Text>
            <Text style={[s.hintTxt,{color:zc.txt,marginTop:3}]}>land on top of enemies to stomp them  💀</Text>
          </View>
        )}

        {/* ── Menu ── */}
        {phase==="menu"&&(
          <Pressable style={[s.overlay,{backgroundColor:"rgba(245,240,232,0.93)"}]} onPress={startGame}>
            <View style={s.titleCard}><Text style={s.t1}>DOODLE</Text><Text style={s.t2}>CLIMB</Text><View style={s.titleBar}/></View>
            {r.best>0&&<Text style={s.ovBest}>BEST  {r.best} m</Text>}
            <View style={[s.btn,{marginTop:18}]}><Text style={s.btnTxt}>TAP TO PLAY</Text></View>
            <Text style={s.ovHint}>3 lives · jetpack · shield · magnet</Text>
            <Text style={s.ovHint}>stomp enemies · double jump · ← A D →</Text>
          </Pressable>
        )}

        {/* ── Game over ── */}
        {phase==="dead"&&(
          <Pressable style={[s.overlay,{backgroundColor:"rgba(6,8,18,0.91)"}]} onPress={startGame}>
            <Text style={s.goLabel}>SCORE</Text>
            <Text style={s.goScore}>{r.score}</Text>
            <Text style={s.goM}>m</Text>
            {r.score>=r.best&&r.score>0&&<Text style={s.newBest}>✦ NEW BEST ✦</Text>}
            {r.best>r.score&&<Text style={s.ovBestDark}>BEST  {r.best} m</Text>}
            {/* Stats row */}
            <View style={s.statsRow}>
              <StatPill icon="🪙" val={r.coinsCollected} label="COINS"/>
              <StatPill icon="⚡" val={r.maxCombo} label="COMBO"/>
              <StatPill icon="💀" val={r.enemiesDefeated} label="STOMPED"/>
            </View>
            <View style={[s.btn,{marginTop:12}]}><Text style={s.btnTxt}>TRY AGAIN</Text></View>
          </Pressable>
        )}
      </View>
    </View>
  );
}

// ─── Styles ───────────────────────────────────────────────────────────────────
const b=StyleSheet.create({
  line:      {position:"absolute",left:0,right:0,height:1.5},
  margin:    {position:"absolute",left:40,top:0,bottom:0,width:1.5},
  star:      {position:"absolute",backgroundColor:"#FFFFFF"},
  cloudBody: {position:"absolute",borderRadius:30},
  cloudPuff: {position:"absolute",borderRadius:99},
});
const g=StyleSheet.create({
  plat:       {position:"absolute",height:PLH,borderRadius:8,borderBottomWidth:3,overflow:"hidden"},
  platShine:  {position:"absolute",top:0,left:6,right:6,height:4,borderRadius:3,backgroundColor:"rgba(255,255,255,0.42)"},
  springWrap: {position:"absolute",top:2,left:"25%",right:"25%",gap:1.5},
  springLine: {height:2.5,borderRadius:1.5,backgroundColor:"rgba(255,255,255,0.58)",marginBottom:1},
  movArrows:  {position:"absolute",inset:0,alignItems:"center",justifyContent:"center"},
  arrowTxt:   {fontSize:7,color:"rgba(255,255,255,0.55)",letterSpacing:2},
  player:     {position:"absolute",width:PW,height:PH,borderRadius:12,alignItems:"center",paddingTop:5,borderBottomWidth:3,overflow:"visible"},
  playerGlow: {position:"absolute",top:-4,left:-4,right:-4,bottom:-4,borderRadius:16,opacity:0.16},
  antenna:    {position:"absolute",top:-9,width:3,height:11,borderRadius:2},
  eyeRow:     {flexDirection:"row",gap:8,marginTop:4},
  eye:        {width:12,height:12,borderRadius:6,backgroundColor:"white",justifyContent:"center",alignItems:"center"},
  pupil:      {width:5,height:5,borderRadius:2.5,backgroundColor:"#0A0A1A"},
  eyeShine:   {position:"absolute",top:1,right:1,width:3,height:3,borderRadius:1.5,backgroundColor:"white"},
  mouth:      {marginTop:4,width:14,height:5,borderRadius:3,borderBottomWidth:2,borderLeftWidth:1.5,borderRightWidth:1.5,borderTopWidth:0},
  mouthH:     {borderColor:"rgba(255,255,255,0.88)"},
  mouthN:     {borderColor:"rgba(255,255,255,0.52)"},
  feet:       {flexDirection:"row",gap:8,marginTop:2},
  foot:       {width:8,height:5,borderRadius:3},
  shield:     {position:"absolute",top:-8,left:-8,right:-8,bottom:-8,borderRadius:36,borderWidth:3,borderColor:"#50AAFF",backgroundColor:"rgba(80,160,255,0.12)"},
  magnetAura: {position:"absolute",top:-12,left:-12,right:-12,bottom:-12,borderRadius:40,borderWidth:2,borderColor:"#C050FF",backgroundColor:"rgba(192,80,255,0.09)",borderStyle:"dashed"},
});
const s=StyleSheet.create({
  root:     {flex:1},
  hud:      {position:"absolute",top:0,left:0,right:0,alignItems:"center"},
  scoreNum: {fontSize:56,fontWeight:"900",letterSpacing:-2,lineHeight:60},
  mLabel:   {fontSize:16,fontWeight:"700",letterSpacing:2,marginTop:-8,opacity:0.55},
  multTxt:  {fontSize:20,fontWeight:"900",letterSpacing:1,marginTop:2},
  bestLabel:{fontSize:15,fontWeight:"700",letterSpacing:3,marginTop:2,opacity:0.42},
  livesWrap:{position:"absolute",right:16},
  livesRow: {flexDirection:"row",gap:4},
  heart:    {fontSize:20,fontWeight:"900"},
  timersStack:{position:"absolute",right:16,gap:4},
  timerRow: {flexDirection:"row",alignItems:"center",gap:6},
  timerIcon:{fontSize:14},
  timerTrack:{width:46,height:6,borderRadius:3,overflow:"hidden"},
  timerFill: {height:"100%",borderRadius:3},
  shieldBadge:{position:"absolute",right:16,backgroundColor:"rgba(0,120,220,0.20)",borderRadius:12,paddingHorizontal:8,paddingVertical:4},
  shieldTxt:  {fontSize:18},
  zoneLabel:  {position:"absolute",left:16},
  zoneTxt:    {fontSize:12,fontWeight:"700",opacity:0.55,letterSpacing:0.5},
  stompWrap:  {position:"absolute",left:0,right:0,top:SH*0.24,alignItems:"center"},
  stompTxt:   {fontSize:26,fontWeight:"900",color:"#FF8844",letterSpacing:2,textShadowColor:"rgba(0,0,0,0.25)",textShadowOffset:{width:0,height:2},textShadowRadius:6},
  djumpWrap:  {position:"absolute",left:0,right:0,top:SH*0.30,alignItems:"center"},
  djumpTxt:   {fontSize:16,fontWeight:"900",color:"#50FFBB",letterSpacing:2},
  comboWrap:  {position:"absolute",left:0,right:0,top:SH*0.35,alignItems:"center"},
  comboTxt:   {fontSize:42,fontWeight:"900",letterSpacing:1,textShadowColor:"rgba(0,0,0,0.2)",textShadowOffset:{width:0,height:2},textShadowRadius:6},
  msWrap:     {position:"absolute",left:0,right:0,top:SH*0.20,alignItems:"center"},
  msPill:     {backgroundColor:"rgba(39,192,99,0.92)",paddingHorizontal:24,paddingVertical:10,borderRadius:30},
  msTxt:      {fontSize:22,fontWeight:"900",color:"white",letterSpacing:1},
  hintWrap:   {position:"absolute",left:0,right:0,bottom:70,alignItems:"center"},
  hintTxt:    {fontSize:12,opacity:0.38,letterSpacing:0.5},
  overlay:    {...StyleSheet.absoluteFillObject,justifyContent:"center",alignItems:"center",gap:10},
  titleCard:  {alignItems:"center",marginBottom:6},
  t1:{fontSize:72,fontWeight:"900",color:"#1A1A2A",letterSpacing:-3,lineHeight:70},
  t2:{fontSize:72,fontWeight:"900",color:P_GREEN,letterSpacing:-3,lineHeight:74},
  titleBar:   {marginTop:8,width:80,height:4,borderRadius:2,backgroundColor:P_GREEN,opacity:0.65},
  ovBest:     {fontSize:18,fontWeight:"700",color:"#1A1A2A",opacity:0.50,letterSpacing:3},
  ovBestDark: {fontSize:18,fontWeight:"700",color:"#90C8FF",opacity:0.65,letterSpacing:3},
  ovHint:     {fontSize:13,color:"#1A1A2A",opacity:0.38,letterSpacing:0.3,marginTop:-4},
  goLabel:    {fontSize:18,fontWeight:"700",color:"#90C8FF",opacity:0.55,letterSpacing:6},
  goScore:    {fontSize:92,fontWeight:"900",color:P_GREEN,letterSpacing:-4,lineHeight:96},
  goM:        {fontSize:22,fontWeight:"700",color:P_GREEN,opacity:0.7,marginTop:-8},
  newBest:    {fontSize:22,fontWeight:"900",color:"#F5B820",letterSpacing:2},
  statsRow:   {flexDirection:"row",gap:12,marginTop:4},
  statPill:   {alignItems:"center",backgroundColor:"rgba(255,255,255,0.08)",borderRadius:14,paddingHorizontal:14,paddingVertical:8},
  statIcon:   {fontSize:20},
  statVal:    {fontSize:22,fontWeight:"900",color:"white",letterSpacing:-1},
  statLabel:  {fontSize:10,fontWeight:"700",color:"rgba(255,255,255,0.45)",letterSpacing:2,marginTop:1},
  btn:        {backgroundColor:P_GREEN,paddingHorizontal:44,paddingVertical:17,borderRadius:16,borderBottomWidth:4,borderColor:P_DARK,marginTop:6},
  btnTxt:     {fontSize:22,fontWeight:"900",color:"white",letterSpacing:2},
});
