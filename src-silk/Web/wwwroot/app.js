/* ═══════════════════════════════════════════════════════════════════════════
   EFT WebRadar — Canvas-based radar with HTTP polling
   Modern UI — Map + Players
   ═══════════════════════════════════════════════════════════════════════════ */

const canvas = document.getElementById("radar");
const ctx    = canvas.getContext("2d", { alpha: true });

const statusEl    = document.getElementById("status");
const statusLabel = statusEl.querySelector(".label");
const subline     = document.getElementById("subline");
const sidebar     = document.getElementById("sidebar");
const toggle      = document.getElementById("toggle");
const menuBtn     = document.getElementById("menuBtn");
const edgeZone    = document.getElementById("edgeZone");
const tooltipEl   = document.getElementById("tooltip");
const playerCountsEl = document.getElementById("playerCounts");

let dpr = window.devicePixelRatio || 1;
let cw = 0, ch = 0;

const ZOOM_MIN = 0.05;
const ZOOM_MAX = 4.0;

/* ═══════════════════════════════════════════════════════════════════════════
   SIDEBAR
   ═══════════════════════════════════════════════════════════════════════════ */
let sidebarTempOpen = false;
let sidebarCloseTimer = null;

function isSidebarOpen() { return !sidebar.classList.contains("collapsed"); }

function setSidebarCollapsed(collapsed, temp = false) {
  sidebar.classList.toggle("collapsed", collapsed);
  sidebarTempOpen = (!collapsed && temp);
  toggle.textContent = collapsed ? ">" : "<";
}
function toggleSidebarPinned() {
  const nowOpen = !isSidebarOpen();
  setSidebarCollapsed(!nowOpen, false);
  state.sidebarCollapsed = !nowOpen;
  saveSettings();
}

toggle.onclick = () => toggleSidebarPinned();
menuBtn.onclick = () => toggleSidebarPinned();

edgeZone.addEventListener("mouseenter", () => {
  if (!state.hoverOpenSidebar || isSidebarOpen()) return;
  if (state.sidebarCollapsed) setSidebarCollapsed(false, true);
});
sidebar.addEventListener("mouseenter", () => clearTimeout(sidebarCloseTimer));
sidebar.addEventListener("mouseleave", () => {
  if (!sidebarTempOpen) return;
  clearTimeout(sidebarCloseTimer);
  sidebarCloseTimer = setTimeout(() => {
    if (sidebarTempOpen && state.sidebarCollapsed) setSidebarCollapsed(true, false);
  }, 250);
});

/* ═══════════════════════════════════════════════════════════════════════════
   TABS
   ═══════════════════════════════════════════════════════════════════════════ */
function activateTab(tabId) {
  document.querySelectorAll(".tab,.tab-content").forEach(e => e.classList.remove("active"));
  document.querySelectorAll(`.tab[data-tab='${tabId}']`).forEach(e => e.classList.add("active"));
  const content = document.getElementById(tabId);
  if (content) content.classList.add("active");
}
document.querySelectorAll(".tab").forEach(tab => tab.onclick = () => activateTab(tab.dataset.tab));

/* ═══════════════════════════════════════════════════════════════════════════
   CANVAS RESIZE
   ═══════════════════════════════════════════════════════════════════════════ */
function resizeCanvas() {
  dpr = window.devicePixelRatio || 1;
  const rect = canvas.getBoundingClientRect();
  cw = Math.max(1, rect.width);
  ch = Math.max(1, rect.height);
  const bw = Math.max(1, Math.round(cw * dpr));
  const bh = Math.max(1, Math.round(ch * dpr));
  if (canvas.width !== bw) canvas.width = bw;
  if (canvas.height !== bh) canvas.height = bh;
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
}
window.addEventListener("resize", resizeCanvas);
resizeCanvas();

/* ═══════════════════════════════════════════════════════════════════════════
   PERSISTENCE
   ═══════════════════════════════════════════════════════════════════════════ */
const LS_KEY = "eft_webradar_settings_v1";

function deepClone(obj) {
  try { return structuredClone(obj); } catch { return JSON.parse(JSON.stringify(obj)); }
}

const defaults = {
  __savedAt: 0,
  sidebarCollapsed: false,
  hoverOpenSidebar: true,

  showMap: true,
  zoom: 1.0,
  rotateWithLocal: false,
  pollMs: 50,
  freeMode: false,

  showPlayers: true,
  showAim: true,
  showNames: false,
  showHeight: true,
  showGroups: true,
  playerSize: 6,

  colors: {
    local:    "#22c55e",
    teammate: "#4ade80",
    pmc:      "#38bdf8",
    scav:     "#f59e0b",
    pscav:    "#facc15",
    raider:   "#fb7185",
    boss:     "#ef4444",
    dead:     "#9ca3af",
  }
};

let state = deepClone(defaults);

function mergeState(parsed) {
  return {
    ...deepClone(defaults),
    ...parsed,
    colors: { ...deepClone(defaults.colors), ...(parsed.colors || {}) },
    zoom: clamp(Number(parsed.zoom) || 1, ZOOM_MIN, ZOOM_MAX),
  };
}

function loadSettings() {
  try {
    const raw = localStorage.getItem(LS_KEY);
    if (raw) state = mergeState(JSON.parse(raw));
  } catch { /* use defaults */ }
}

function saveSettings() {
  state.__savedAt = Date.now();
  try { localStorage.setItem(LS_KEY, JSON.stringify(state)); } catch { /* ignore */ }
  const badge = document.getElementById("cacheBadge");
  if (badge) {
    badge.textContent = "saved ✓";
    clearTimeout(saveSettings._t);
    saveSettings._t = setTimeout(() => badge.textContent = "cache", 1000);
  }
}

function resetSettings() {
  try { localStorage.removeItem(LS_KEY); } catch { /* ignore */ }
  state = deepClone(defaults);
  freeAnchor = { x: 0, y: 0, mapId: "" };
  bindAllInputs();
  applyUiFromState();
  updateAllRangeValues();
  saveSettings();
}

/* ═══════════════════════════════════════════════════════════════════════════
   INPUT BINDING
   ═══════════════════════════════════════════════════════════════════════════ */
const $ = id => document.getElementById(id);

const inputs = {
  showMap:         $("showMap"),
  zoom:            $("zoom"),
  rotateWithLocal: $("rotateWithLocal"),
  pollMs:          $("pollMs"),
  freeMode:        $("freeMode"),
  centerOnLocal:   $("centerOnLocal"),
  modeBadge:       $("modeBadge"),
  hoverOpenSidebar:$("hoverOpenSidebar"),

  showPlayers:     $("showPlayers"),
  showAim:         $("showAim"),
  showNames:       $("showNames"),
  showHeight:      $("showHeight"),
  showGroups:      $("showGroups"),
  playerSize:      $("playerSize"),

  localColor:      $("localColor"),
  teammateColor:   $("teammateColor"),
  pmcColor:        $("pmcColor"),
  scavColor:       $("scavColor"),
  pscavColor:      $("pscavColor"),
  raiderColor:     $("raiderColor"),
  bossColor:       $("bossColor"),
  deadColor:       $("deadColor"),

  resetSettings:   $("resetSettings"),
};

/* Range value display elements */
const rangeValueEls = {
  playerSize: $("playerSizeVal"),
  zoom:       $("zoomVal"),
  pollMs:     $("pollMsVal"),
};

function updateRangeValue(key) {
  const el = rangeValueEls[key];
  if (!el) return;
  const v = state[key];
  if (key === "zoom") {
    el.textContent = Number(v).toFixed(2);
  } else if (key === "pollMs") {
    el.innerHTML = v + "<small>ms</small>";
  } else {
    el.textContent = v;
  }
}

function updateAllRangeValues() {
  for (const key of Object.keys(rangeValueEls)) updateRangeValue(key);
}

function applyUiFromState() {
  setSidebarCollapsed(!!state.sidebarCollapsed, false);
  if (inputs.modeBadge) inputs.modeBadge.textContent = state.freeMode ? "free" : "follow";
}

function bindAllInputs() {
  const bind = (el, key, isColor) => {
    if (!el) return;
    const src = isColor ? state.colors : state;
    if (el.type === "checkbox") el.checked = !!src[key];
    else el.value = src[key] ?? el.value;
  };

  bind(inputs.showMap, "showMap");
  bind(inputs.zoom, "zoom");
  bind(inputs.rotateWithLocal, "rotateWithLocal");
  bind(inputs.pollMs, "pollMs");
  bind(inputs.freeMode, "freeMode");
  bind(inputs.hoverOpenSidebar, "hoverOpenSidebar");

  bind(inputs.showPlayers, "showPlayers");
  bind(inputs.showAim, "showAim");
  bind(inputs.showNames, "showNames");
  bind(inputs.showHeight, "showHeight");
  bind(inputs.showGroups, "showGroups");
  bind(inputs.playerSize, "playerSize");

  bind(inputs.localColor, "local", true);
  bind(inputs.teammateColor, "teammate", true);
  bind(inputs.pmcColor, "pmc", true);
  bind(inputs.scavColor, "scav", true);
  bind(inputs.pscavColor, "pscav", true);
  bind(inputs.raiderColor, "raider", true);
  bind(inputs.bossColor, "boss", true);
  bind(inputs.deadColor, "dead", true);
}

function listen(el, key, isColor, transform) {
  if (!el) return;
  const evt = (el.type === "color" || el.type === "range") ? "input" : "change";
  el.addEventListener(evt, () => {
    const v = el.type === "checkbox" ? el.checked : (transform ? transform(el.value) : el.value);
    if (isColor) state.colors[key] = v;
    else state[key] = v;
    saveSettings();
    updateRangeValue(key);
    if (key === "freeMode" && inputs.modeBadge)
      inputs.modeBadge.textContent = state.freeMode ? "free" : "follow";
    if (key === "pollMs") startPolling();
  });
}

listen(inputs.showMap, "showMap");
listen(inputs.zoom, "zoom", false, Number);
listen(inputs.rotateWithLocal, "rotateWithLocal");
listen(inputs.pollMs, "pollMs", false, Number);
listen(inputs.freeMode, "freeMode");
listen(inputs.hoverOpenSidebar, "hoverOpenSidebar");

listen(inputs.showPlayers, "showPlayers");
listen(inputs.showAim, "showAim");
listen(inputs.showNames, "showNames");
listen(inputs.showHeight, "showHeight");
listen(inputs.showGroups, "showGroups");
listen(inputs.playerSize, "playerSize", false, Number);

listen(inputs.localColor, "local", true);
listen(inputs.teammateColor, "teammate", true);
listen(inputs.pmcColor, "pmc", true);
listen(inputs.scavColor, "scav", true);
listen(inputs.pscavColor, "pscav", true);
listen(inputs.raiderColor, "raider", true);
listen(inputs.bossColor, "boss", true);
listen(inputs.deadColor, "dead", true);

if (inputs.centerOnLocal) {
  inputs.centerOnLocal.onclick = () => {
    state.freeMode = false;
    freeAnchor = { x: 0, y: 0, mapId: "" };
    if (inputs.freeMode) inputs.freeMode.checked = false;
    if (inputs.modeBadge) inputs.modeBadge.textContent = "follow";
    saveSettings();
  };
}
if (inputs.resetSettings) {
  inputs.resetSettings.onclick = () => resetSettings();
}

/* ═══════════════════════════════════════════════════════════════════════════
   HTTP POLLING
   ═══════════════════════════════════════════════════════════════════════════ */
let radarData = null;
let pollTimer = null;

async function fetchRadar() {
  try {
    const res = await fetch("/api/radar", { cache: "no-store" });
    if (!res.ok) throw new Error("HTTP " + res.status);
    radarData = await res.json();

    const inRaid = !!(radarData?.inRaid ?? radarData?.inGame);
    statusLabel.textContent = inRaid ? "In Raid" : "Waiting for raid\u2026";
    statusEl.className = inRaid ? "ok" : "warn";

    const mapId = radarData?.mapID ?? "unknown";
    const pc = Array.isArray(radarData?.players) ? radarData.players.length : 0;
    subline.textContent = `${mapId} · ${pc} player${pc !== 1 ? "s" : ""}`;

    updatePlayerCounts(radarData?.players);
  } catch {
    radarData = null;
    statusLabel.textContent = "Disconnected";
    statusEl.className = "bad";
    subline.textContent = "waiting\u2026";
    updatePlayerCounts(null);
  }
}

function startPolling() {
  if (pollTimer) clearInterval(pollTimer);
  pollTimer = setInterval(fetchRadar, state.pollMs);
}

/* ═══════════════════════════════════════════════════════════════════════════
   PLAYER COUNT CHIPS
   ═══════════════════════════════════════════════════════════════════════════ */
const typeNames  = ["Bot", "You", "Team", "PMC", "PScav", "Raider", "Boss"];
const typeColors = () => [
  state.colors.scav,
  state.colors.local,
  state.colors.teammate,
  state.colors.pmc,
  state.colors.pscav,
  state.colors.raider,
  state.colors.boss,
];

function updatePlayerCounts(players) {
  if (!playerCountsEl) return;
  if (!players || !players.length) {
    playerCountsEl.innerHTML = "";
    return;
  }

  const counts = {};
  let alive = 0;
  for (const p of players) {
    if (!p || !p.isActive) continue;
    if (p.isLocal) continue;
    const t = p.type ?? 0;
    counts[t] = (counts[t] || 0) + 1;
    if (p.isAlive !== false) alive++;
  }

  const cols = typeColors();
  let html = "";
  // Show PMC, PScav, Raider, Boss, Bot (skip local=1, teammate=2 from chip display)
  const order = [3, 4, 5, 6, 0];
  for (const t of order) {
    if (!counts[t]) continue;
    const name = typeNames[t] || "Bot";
    const col = cols[t] || "#9ca3af";
    html += `<span class="pcount-chip"><span class="chip-dot" style="background:${col}"></span>${counts[t]} ${name}</span>`;
  }

  // Teammates
  if (counts[2]) {
    const col = cols[2] || "#4ade80";
    html += `<span class="pcount-chip"><span class="chip-dot" style="background:${col}"></span>${counts[2]} Team</span>`;
  }

  playerCountsEl.innerHTML = html;
}

/* ═══════════════════════════════════════════════════════════════════════════
   SVG MAP CACHE
   ═══════════════════════════════════════════════════════════════════════════ */
const svgImgCache = new Map();
const svgMetaCache = new Map();

function ensureSvgMeta(filename) {
  if (svgMetaCache.has(filename)) return;
  svgMetaCache.set(filename, { w: 0, h: 0, ready: false });

  fetch("/Maps/" + filename, { cache: "force-cache" })
    .then(r => r.text())
    .then(txt => {
      let w = 0, h = 0;
      const vb = /viewBox\s*=\s*["']\s*([-\d.eE]+)\s+([-\d.eE]+)\s+([-\d.eE]+)\s+([-\d.eE]+)\s*["']/i.exec(txt);
      if (vb) { w = Number(vb[3]) || 0; h = Number(vb[4]) || 0; }
      if (!(w > 0 && h > 0)) {
        const mw = /width\s*=\s*["']\s*([-\d.eE]+)\s*(?:px)?\s*["']/i.exec(txt);
        const mh = /height\s*=\s*["']\s*([-\d.eE]+)\s*(?:px)?\s*["']/i.exec(txt);
        if (mw && mh) { w = Number(mw[1]) || 0; h = Number(mh[1]) || 0; }
      }
      const meta = svgMetaCache.get(filename);
      if (meta) { meta.w = w > 0 ? w : 0; meta.h = h > 0 ? h : 0; meta.ready = true; }
    })
    .catch(() => {});
}

function getSvg(filename) {
  if (svgImgCache.has(filename)) return svgImgCache.get(filename);
  ensureSvgMeta(filename);
  const img = new Image();
  img.src = "/Maps/" + filename;
  svgImgCache.set(filename, img);
  const el = document.getElementById("mapCacheInfo");
  if (el) el.textContent = String(svgImgCache.size);
  return img;
}

function getSvgDims(filename, img) {
  const meta = svgMetaCache.get(filename);
  if (meta && meta.ready && meta.w > 0 && meta.h > 0) return { w: meta.w, h: meta.h };
  const nw = img?.naturalWidth || 0, nh = img?.naturalHeight || 0;
  if (nw > 0 && nh > 0) return { w: nw, h: nh };
  return null;
}

/* ═══════════════════════════════════════════════════════════════════════════
   MAP HELPERS
   ═══════════════════════════════════════════════════════════════════════════ */
function clamp(v, lo, hi) { return Math.max(lo, Math.min(v, hi)); }

function getMapLayers(map) {
  const a = Array.isArray(map?.layers) ? map.layers : [];
  const b = Array.isArray(map?.mapLayers) ? map.mapLayers : [];
  return a.length ? a : b;
}
function hmin(l) { return l?.minHeight ?? l?.MinHeight ?? null; }
function hmax(l) { return l?.maxHeight ?? l?.MaxHeight ?? null; }

function getBaseLayer(map) {
  const layers = getMapLayers(map);
  if (!layers.length) return null;
  return layers.find(l => l && hmin(l) == null && hmax(l) == null) || layers[0];
}

function getHeightLayer(map, localWorldY) {
  const layers = getMapLayers(map);
  if (!layers.length || localWorldY == null) return null;
  const candidates = layers.filter(l => {
    if (!l || (hmin(l) == null && hmax(l) == null)) return false;
    return (hmin(l) == null || localWorldY >= hmin(l)) &&
           (hmax(l) == null || localWorldY < hmax(l));
  });
  if (!candidates.length) return null;
  candidates.sort((a, b) => (hmin(a) ?? -999999) - (hmin(b) ?? -999999));
  return candidates[candidates.length - 1];
}

function rotatePoint(px, py, rad) {
  const c = Math.cos(rad), s = Math.sin(rad);
  return { x: px * c - py * s, y: px * s + py * c };
}

function worldToMapUnzoomed(worldX, worldZ, map) {
  const ox = map.originX ?? map.x ?? 0;
  const oy = map.originY ?? map.y ?? 0;
  const sc = map.scale ?? 1;
  const svgSc = map.svgScale ?? 1;
  return {
    x: (ox * svgSc) + (worldX * sc * svgSc),
    y: (oy * svgSc) - (worldZ * sc * svgSc)
  };
}

function readPlayerMapXY(p, map) {
  const wx = p?.worldX;
  const wz = p?.worldZ;
  if (Number.isFinite(wx) && Number.isFinite(wz) && map) {
    return worldToMapUnzoomed(wx, wz, map);
  }
  return { x: 0, y: 0 };
}

function readWorldY(e) {
  const wy = e?.worldY;
  return Number.isFinite(wy) ? wy : null;
}

function getViewportCenter() {
  const sbOpen = isSidebarOpen();
  const insetRight = sbOpen ? sidebar.getBoundingClientRect().width : 0;
  return { cx: (cw - insetRight) / 2, cy: ch / 2 };
}

/* ═══════════════════════════════════════════════════════════════════════════
   MAP DRAWING
   ═══════════════════════════════════════════════════════════════════════════ */
function drawSvgLayerAnchored(filename, map, cx, cy, zoom, rotRad, anchor, alpha = 1) {
  if (!filename) return false;
  const img = getSvg(filename);
  if (!img.complete) return false;
  const dims = getSvgDims(filename, img);
  if (!dims) return false;

  const svgSc = map.svgScale ?? 1;
  const w = dims.w * svgSc * zoom;
  const h = dims.h * svgSc * zoom;

  ctx.save();
  ctx.globalAlpha = alpha;
  ctx.translate(cx, cy);
  if (state.rotateWithLocal) ctx.rotate(-rotRad);
  ctx.translate(-(anchor?.x ?? 0) * zoom, -(anchor?.y ?? 0) * zoom);
  ctx.drawImage(img, 0, 0, w, h);
  ctx.restore();
  return true;
}

function getMapScreenRect(map, cx, cy, zoom, anchor) {
  const base = getBaseLayer(map);
  if (!base) return null;
  const bFile = base.filename || base.Filename;
  if (!bFile) return null;
  const img = getSvg(bFile);
  if (!img.complete) return null;
  const dims = getSvgDims(bFile, img);
  if (!dims) return null;

  const svgSc = map.svgScale ?? 1;
  const w = dims.w * svgSc * zoom;
  const h = dims.h * svgSc * zoom;
  const ax = (anchor?.x ?? 0) * zoom;
  const ay = (anchor?.y ?? 0) * zoom;

  return { left: cx - ax, top: cy - ay, w, h };
}

function drawMap(map, localWorldY, cx, cy, zoom, rotRad, anchor) {
  const base = getBaseLayer(map);
  if (!base) return false;

  const disableDimming = !!(map.disableDimming ?? map.DisableDimming);
  const overlay = (!disableDimming) ? getHeightLayer(map, localWorldY) : null;

  let baseAlpha = 1;
  if (!disableDimming && overlay) {
    if (overlay.dimBaseLayer === true || overlay.DimBaseLayer === true) baseAlpha = 0.55;
  }

  const bFile = base.filename || base.Filename;
  const ok = drawSvgLayerAnchored(bFile, map, cx, cy, zoom, rotRad, anchor, baseAlpha);
  if (!ok) return false;

  if (overlay) {
    const oFile = overlay.filename || overlay.Filename;
    if (oFile && oFile !== bFile) drawSvgLayerAnchored(oFile, map, cx, cy, zoom, rotRad, anchor, 1);
  }
  return true;
}

function mapXYToScreen(mx, my, mapRect, cx, cy, rotRad) {
  let px = mapRect.left + mx * state.zoom;
  let py = mapRect.top + my * state.zoom;
  if (state.rotateWithLocal) {
    const v = rotatePoint(px - cx, py - cy, -rotRad);
    px = cx + v.x;
    py = cy + v.y;
  }
  return { px, py };
}

/* ═══════════════════════════════════════════════════════════════════════════
   PLAYER DRAWING
   ═══════════════════════════════════════════════════════════════════════════ */
// WebPlayerType enum from C#:
// Bot=0, LocalPlayer=1, Teammate=2, Player=3, PlayerScav=4, Raider=5, Boss=6
function playerColor(p) {
  const isDead = p?.isAlive === false;
  if (isDead) return state.colors.dead;
  switch (p?.type) {
    case 1: return state.colors.local;
    case 2: return state.colors.teammate;
    case 3: return state.colors.pmc;
    case 4: return state.colors.pscav;
    case 5: return state.colors.raider;
    case 6: return state.colors.boss;
    default: return state.colors.scav;
  }
}

function drawPlayerMarker(px, py, r, color, ang, isDead) {
  ctx.save();
  ctx.strokeStyle = color;
  ctx.fillStyle = color;
  const lw = Math.max(2, r * 0.45);
  ctx.lineWidth = lw;
  ctx.lineCap = "round";

  if (isDead) {
    const d = r * 0.7;
    ctx.globalAlpha = 0.6;
    ctx.beginPath();
    ctx.moveTo(px - d, py - d);
    ctx.lineTo(px + d, py + d);
    ctx.moveTo(px + d, py - d);
    ctx.lineTo(px - d, py + d);
    ctx.stroke();
    ctx.restore();
    return;
  }

  // Outer glow
  ctx.shadowColor = color;
  ctx.shadowBlur = 6;

  // Open arc (chevron facing direction)
  const gap = Math.PI / 3;
  const start = ang + gap * 0.5;
  const end = ang + (Math.PI * 2) - gap * 0.5;
  ctx.beginPath();
  ctx.arc(px, py, r, start, end, false);
  ctx.stroke();

  ctx.shadowBlur = 0;
  ctx.restore();
}

function drawHeightArrow(px, py, above) {
  const sz = 5;
  ctx.beginPath();
  if (above) {
    ctx.moveTo(px, py - sz);
    ctx.lineTo(px - sz, py + sz);
    ctx.lineTo(px + sz, py + sz);
  } else {
    ctx.moveTo(px, py + sz);
    ctx.lineTo(px - sz, py - sz);
    ctx.lineTo(px + sz, py - sz);
  }
  ctx.closePath();
  ctx.fill();
}

function drawGroupConnectors(players, map, cx, cy, rotRad, mapRect) {
  const groups = new Map();
  for (const p of players) {
    if (!p || p.isAlive === false) continue;
    const gid = p.groupId ?? -1;
    if (gid <= 0) continue;
    if (!groups.has(gid)) groups.set(gid, []);
    groups.get(gid).push(p);
  }

  ctx.save();
  ctx.globalAlpha = 0.35;
  ctx.lineWidth = 1.5;
  ctx.lineCap = "round";
  ctx.setLineDash([4, 6]);

  for (const [, members] of groups) {
    if (members.length < 2) continue;
    const col = playerColor(members[0]);
    ctx.strokeStyle = col;

    for (let i = 0; i < members.length - 1; i++) {
      const a = readPlayerMapXY(members[i], map);
      const b = readPlayerMapXY(members[i + 1], map);
      const sa = mapXYToScreen(a.x, a.y, mapRect, cx, cy, rotRad);
      const sb = mapXYToScreen(b.x, b.y, mapRect, cx, cy, rotRad);
      ctx.beginPath();
      ctx.moveTo(sa.px, sa.py);
      ctx.lineTo(sb.px, sb.py);
      ctx.stroke();
    }
  }

  ctx.setLineDash([]);
  ctx.restore();
}

function drawPlayers(players, map, cx, cy, rotRad, mapRect, localWorldY, hitList) {
  const size = Number(state.playerSize) || 6;
  const haveHeights = (localWorldY != null);

  for (const p of players) {
    if (!p || !p.isActive) continue;

    const isDead = p.isAlive === false;
    const pm = readPlayerMapXY(p, map);
    const s = mapXYToScreen(pm.x, pm.y, mapRect, cx, cy, rotRad);
    const px = s.px, py = s.py;

    hitList.push({ kind: "player", px, py, r: Math.max(10, size + 8), data: p });

    const col = playerColor(p);
    const yaw = Number(p.yaw) || 0;
    const ang = state.rotateWithLocal ? (yaw - rotRad) : yaw;

    drawPlayerMarker(px, py, size, col, ang, isDead);

    // Aimline
    if (state.showAim && !isDead) {
      const len = 22;
      ctx.save();
      ctx.strokeStyle = col;
      ctx.globalAlpha = 0.7;
      ctx.lineWidth = 1.5;
      ctx.lineCap = "round";
      ctx.beginPath();
      ctx.moveTo(px + Math.cos(ang) * (size * 0.15), py + Math.sin(ang) * (size * 0.15));
      ctx.lineTo(px + Math.cos(ang) * len, py + Math.sin(ang) * len);
      ctx.stroke();
      ctx.restore();
    }

    // Height arrows
    if (state.showHeight && haveHeights) {
      const pyWorld = readWorldY(p);
      if (pyWorld != null) {
        const dy = pyWorld - localWorldY;
        ctx.fillStyle = col;
        if (dy > 1.0) drawHeightArrow(px, py - (size + 10), true);
        else if (dy < -1.0) drawHeightArrow(px, py + (size + 10), false);
      }
    }

    // Names
    if (state.showNames) {
      ctx.save();
      ctx.fillStyle = "rgba(229, 231, 235, 0.85)";
      ctx.font = "600 11px system-ui, sans-serif";
      ctx.textAlign = "center";
      ctx.shadowColor = "rgba(0,0,0,.7)";
      ctx.shadowBlur = 3;
      ctx.fillText(p.name || "", px, py - size - 6);
      ctx.restore();
    }
  }
}

/* ═══════════════════════════════════════════════════════════════════════════
   TOOLTIP
   ═══════════════════════════════════════════════════════════════════════════ */
let hitList = [];
let mouseX = 0, mouseY = 0;

canvas.addEventListener("mousemove", e => {
  mouseX = e.clientX;
  mouseY = e.clientY;
});
canvas.addEventListener("mouseleave", () => hideTooltip());

function hideTooltip() { tooltipEl.classList.add("hidden"); }

function updateHover() {
  let found = null;
  let bestDist = Infinity;

  for (const h of hitList) {
    const dx = mouseX - h.px;
    const dy = mouseY - h.py;
    const dist = dx * dx + dy * dy;
    if (dist < h.r * h.r && dist < bestDist) {
      bestDist = dist;
      found = h;
    }
  }

  if (!found) { hideTooltip(); return; }

  const p = found.data;
  const col = playerColor(p);
  const typeName = typeNames[p.type] || "Bot";
  const status = p.isAlive === false ? "Dead" : "Alive";
  const statusClass = p.isAlive === false ? "bad" : "ok";

  let html = `<div class="t-header"><span class="t-dot" style="background:${col}"></span><span class="t-name">${esc(p.name || "Unknown")}</span></div>`;
  html += `<div class="t-type">${typeName} · <span style="color:var(--${statusClass})">${status}</span></div>`;

  const hasExtra = (p.gearValue > 0) || (readWorldY(p) != null);
  if (hasExtra) {
    html += `<div class="t-sep"></div><div class="t-grid">`;
    if (p.gearValue > 0) {
      html += `<span class="k">Gear</span><span class="v">₽${p.gearValue.toLocaleString()}</span>`;
    }
    const wy = readWorldY(p);
    if (wy != null) {
      html += `<span class="k">Height</span><span class="v">${wy.toFixed(1)}</span>`;
    }
    html += `</div>`;
  }

  tooltipEl.innerHTML = html;
  tooltipEl.classList.remove("hidden");

  // Position with bounds checking
  const pad = 14;
  let tx = mouseX + pad;
  let ty = mouseY + pad;
  const tw = tooltipEl.offsetWidth;
  const th = tooltipEl.offsetHeight;
  if (tx + tw > window.innerWidth - 8) tx = mouseX - tw - pad;
  if (ty + th > window.innerHeight - 8) ty = mouseY - th - pad;

  tooltipEl.style.left = tx + "px";
  tooltipEl.style.top = ty + "px";
}

function esc(s) {
  const d = document.createElement("div");
  d.textContent = s;
  return d.innerHTML;
}

/* ═══════════════════════════════════════════════════════════════════════════
   MOUSE / TOUCH — PAN & ZOOM
   ═══════════════════════════════════════════════════════════════════════════ */
let freeAnchor = { x: 0, y: 0, mapId: "" };
let isDragging = false;
let dragStart = { x: 0, y: 0 };

canvas.addEventListener("mousedown", e => {
  if (e.button !== 0 || !state.freeMode) return;
  isDragging = true;
  dragStart = { x: e.clientX, y: e.clientY };
});
window.addEventListener("mousemove", e => {
  if (!isDragging) return;
  const dx = e.clientX - dragStart.x;
  const dy = e.clientY - dragStart.y;
  dragStart = { x: e.clientX, y: e.clientY };

  const zoom = state.zoom;
  if (state.rotateWithLocal) {
    const v = rotatePoint(dx, dy, lastRotRad);
    freeAnchor.x -= v.x / zoom;
    freeAnchor.y -= v.y / zoom;
  } else {
    freeAnchor.x -= dx / zoom;
    freeAnchor.y -= dy / zoom;
  }
});
window.addEventListener("mouseup", () => { isDragging = false; });

canvas.addEventListener("wheel", e => {
  e.preventDefault();
  const delta = e.deltaY > 0 ? -0.1 : 0.1;
  state.zoom = clamp(state.zoom + delta, ZOOM_MIN, ZOOM_MAX);
  if (inputs.zoom) inputs.zoom.value = state.zoom;
  updateRangeValue("zoom");
  saveSettings();
}, { passive: false });

/* ── Touch: pinch-to-zoom ── */
let touches = [];
let lastPinchDist = 0;

canvas.addEventListener("touchstart", e => {
  touches = [...e.touches];
  if (touches.length === 2) {
    lastPinchDist = pinchDist(touches);
    e.preventDefault();
  } else if (touches.length === 1 && state.freeMode) {
    isDragging = true;
    dragStart = { x: touches[0].clientX, y: touches[0].clientY };
    e.preventDefault();
  }
}, { passive: false });

canvas.addEventListener("touchmove", e => {
  touches = [...e.touches];
  if (touches.length === 2) {
    const d = pinchDist(touches);
    if (lastPinchDist > 0) {
      const scale = d / lastPinchDist;
      state.zoom = clamp(state.zoom * scale, ZOOM_MIN, ZOOM_MAX);
      if (inputs.zoom) inputs.zoom.value = state.zoom;
      updateRangeValue("zoom");
      saveSettings();
    }
    lastPinchDist = d;
    e.preventDefault();
  } else if (touches.length === 1 && isDragging) {
    const dx = touches[0].clientX - dragStart.x;
    const dy = touches[0].clientY - dragStart.y;
    dragStart = { x: touches[0].clientX, y: touches[0].clientY };
    const zoom = state.zoom;
    if (state.rotateWithLocal) {
      const v = rotatePoint(dx, dy, lastRotRad);
      freeAnchor.x -= v.x / zoom;
      freeAnchor.y -= v.y / zoom;
    } else {
      freeAnchor.x -= dx / zoom;
      freeAnchor.y -= dy / zoom;
    }
    e.preventDefault();
  }
}, { passive: false });

canvas.addEventListener("touchend", e => {
  touches = [...e.touches];
  if (touches.length < 2) lastPinchDist = 0;
  if (touches.length === 0) isDragging = false;
});

function pinchDist(t) {
  const dx = t[0].clientX - t[1].clientX;
  const dy = t[0].clientY - t[1].clientY;
  return Math.sqrt(dx * dx + dy * dy);
}

/* ═══════════════════════════════════════════════════════════════════════════
   RENDER LOOP
   ═══════════════════════════════════════════════════════════════════════════ */
let lastRotRad = 0;
let lastLocalPlayer = null;

function frame() {
  requestAnimationFrame(frame);
  resizeCanvas();

  ctx.clearRect(0, 0, cw, ch);
  hitList = [];

  if (!radarData) return;

  const map = radarData.map || null;
  const players = Array.isArray(radarData.players) ? radarData.players : [];
  const { cx, cy } = getViewportCenter();

  // Find local player
  const local = players.find(p => p?.isLocal) || null;
  lastLocalPlayer = local;

  // Rotation
  const localYaw = local ? (Number(local.yaw) || 0) : 0;
  lastRotRad = localYaw;

  // Anchor
  let anchor = null;
  if (state.freeMode) {
    const mapId = radarData.mapID ?? "";
    if (freeAnchor.mapId !== mapId) {
      if (local && map) {
        const lm = readPlayerMapXY(local, map);
        freeAnchor.x = lm.x;
        freeAnchor.y = lm.y;
      } else {
        freeAnchor.x = 0;
        freeAnchor.y = 0;
      }
      freeAnchor.mapId = mapId;
    }
    anchor = freeAnchor;
  } else {
    if (local && map) {
      const tm = readPlayerMapXY(local, map);
      anchor = { x: tm.x, y: tm.y };
    } else {
      anchor = { x: 0, y: 0 };
    }
  }

  // Draw map
  if (state.showMap && map) {
    const localY = readWorldY(local);
    drawMap(map, localY, cx, cy, state.zoom, lastRotRad, anchor);
  }

  if (!map) return;

  const mapRect = getMapScreenRect(map, cx, cy, state.zoom, anchor);
  if (!mapRect) return;

  // Draw
  if (state.showGroups) drawGroupConnectors(players, map, cx, cy, lastRotRad, mapRect);
  if (state.showPlayers) drawPlayers(players, map, cx, cy, lastRotRad, mapRect, readWorldY(local), hitList);

  // Tooltip
  updateHover();
}

/* ═══════════════════════════════════════════════════════════════════════════
   INIT
   ═══════════════════════════════════════════════════════════════════════════ */
loadSettings();
bindAllInputs();
applyUiFromState();
updateAllRangeValues();
startPolling();
fetchRadar();
requestAnimationFrame(frame);
