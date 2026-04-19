/* ═══════════════════════════════════════════════════════════════════════════
   EFT WebRadar — Canvas-based radar with HTTP polling
   Modern UI — Map + Players
   ═══════════════════════════════════════════════════════════════════════════ */

const canvas = document.getElementById("radar");
const ctx    = canvas.getContext("2d", { alpha: true });

const aimviewEl     = document.getElementById("aimview");
const aimviewCanvas = document.getElementById("aimviewCanvas");
const avCtx         = aimviewCanvas.getContext("2d", { alpha: true });

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

  showLoot: true,
  showLootNames: true,
  lootMinPrice: 50000,
  showContainers: true,
  showContainerNames: true,
  containerMaxDist: 0,
  selectedContainers: [],
  showCorpses: true,
  showExfils: true,
  showAimview: false,
  aimviewSize: 250,
  aimviewX: null,
  aimviewY: null,
  followTarget: null,

  colors: {
    local:    "#22c55e",
    teammate: "#4ade80",
    pmc:      "#38bdf8",
    scav:     "#f59e0b",
    pscav:    "#facc15",
    raider:   "#fb7185",
    boss:     "#ef4444",
    dead:     "#9ca3af",
    loot:     "#a78bfa",
    lootImportant: "#4ade80",
    lootWishlist:  "#fbbf24",
    container:     "#60a5fa",
    corpse:        "#9ca3af",
    exfilOpen:     "#4ade80",
    exfilPending:  "#facc15",
    exfilClosed:   "#f87171",
  }
};

let state = deepClone(defaults);

function mergeState(parsed) {
  return {
    ...deepClone(defaults),
    ...parsed,
    colors: { ...deepClone(defaults.colors), ...(parsed.colors || {}) },
    zoom: clamp(Number(parsed.zoom) || 1, ZOOM_MIN, ZOOM_MAX),
    selectedContainers: Array.isArray(parsed.selectedContainers) ? parsed.selectedContainers : [],
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
  showAimview:     $("showAimview"),
  aimviewSize:     $("aimviewSize"),

  showPlayers:     $("showPlayers"),
  showAim:         $("showAim"),
  showNames:       $("showNames"),
  showHeight:      $("showHeight"),
  showGroups:      $("showGroups"),
  playerSize:      $("playerSize"),

  showLoot:        $("showLoot"),
  showLootNames:   $("showLootNames"),
  lootMinPrice:    $("lootMinPrice"),
  showContainers:  $('showContainers'),
  showContainerNames: $('showContainerNames'),
  containerMaxDist: $('containerMaxDist'),
  showCorpses:     $('showCorpses'),
  showExfils:      $("showExfils"),

  localColor:      $("localColor"),
  teammateColor:   $("teammateColor"),
  pmcColor:        $("pmcColor"),
  scavColor:       $("scavColor"),
  pscavColor:      $("pscavColor"),
  raiderColor:     $("raiderColor"),
  bossColor:       $("bossColor"),
  deadColor:       $("deadColor"),
  lootColor:       $("lootColor"),
  lootImportantColor: $("lootImportantColor"),
  lootWishlistColor:  $("lootWishlistColor"),
  containerColor:  $("containerColor"),
  corpseColor:     $("corpseColor"),
  exfilOpenColor:  $("exfilOpenColor"),
  exfilPendingColor: $("exfilPendingColor"),
  exfilClosedColor:  $("exfilClosedColor"),

  resetSettings:   $("resetSettings"),
};

/* Range value display elements */
const rangeValueEls = {
  playerSize: $("playerSizeVal"),
  zoom:       $("zoomVal"),
  pollMs:     $("pollMsVal"),
  lootMinPrice: $("lootMinPriceVal"),
  aimviewSize:  $("aimviewSizeVal"),
  containerMaxDist: $("containerMaxDistVal"),
};

function updateRangeValue(key) {
  const el = rangeValueEls[key];
  if (!el) return;
  const v = state[key];
  if (key === "zoom") {
    el.textContent = Number(v).toFixed(2);
  } else if (key === "pollMs") {
    el.innerHTML = v + "<small>ms</small>";
  } else if (key === "lootMinPrice") {
    el.textContent = formatPrice(v);
  } else if (key === "containerMaxDist") {
    el.textContent = v <= 0 ? "Off" : v + "m";
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
  bind(inputs.showAimview, "showAimview");
  bind(inputs.aimviewSize, "aimviewSize");

  bind(inputs.showPlayers, "showPlayers");
  bind(inputs.showAim, "showAim");
  bind(inputs.showNames, "showNames");
  bind(inputs.showHeight, "showHeight");
  bind(inputs.showGroups, "showGroups");
  bind(inputs.playerSize, "playerSize");

  bind(inputs.showLoot, "showLoot");
  bind(inputs.showLootNames, "showLootNames");
  bind(inputs.lootMinPrice, "lootMinPrice");
  bind(inputs.showContainers, "showContainers");
  bind(inputs.showContainerNames, "showContainerNames");
  bind(inputs.containerMaxDist, "containerMaxDist");
  bind(inputs.showCorpses, "showCorpses");
  bind(inputs.showExfils, "showExfils");

  bind(inputs.localColor, "local", true);
  bind(inputs.teammateColor, "teammate", true);
  bind(inputs.pmcColor, "pmc", true);
  bind(inputs.scavColor, "scav", true);
  bind(inputs.pscavColor, "pscav", true);
  bind(inputs.raiderColor, "raider", true);
  bind(inputs.bossColor, "boss", true);
  bind(inputs.deadColor, "dead", true);
  bind(inputs.lootColor, "loot", true);
  bind(inputs.lootImportantColor, "lootImportant", true);
  bind(inputs.lootWishlistColor, "lootWishlist", true);
  bind(inputs.containerColor, "container", true);
  bind(inputs.corpseColor, "corpse", true);
  bind(inputs.exfilOpenColor, "exfilOpen", true);
  bind(inputs.exfilPendingColor, "exfilPending", true);
  bind(inputs.exfilClosedColor, "exfilClosed", true);
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
    if (key === "freeMode") updateFollowBadge();
    if (key === "pollMs") startPolling();
  });
}

listen(inputs.showMap, "showMap");
listen(inputs.zoom, "zoom", false, Number);
listen(inputs.rotateWithLocal, "rotateWithLocal");
listen(inputs.pollMs, "pollMs", false, Number);
listen(inputs.freeMode, "freeMode");
listen(inputs.hoverOpenSidebar, "hoverOpenSidebar");
listen(inputs.showAimview, "showAimview");
listen(inputs.aimviewSize, "aimviewSize", false, Number);

listen(inputs.showPlayers, "showPlayers");
listen(inputs.showAim, "showAim");
listen(inputs.showNames, "showNames");
listen(inputs.showHeight, "showHeight");
listen(inputs.showGroups, "showGroups");
listen(inputs.playerSize, "playerSize", false, Number);

listen(inputs.showLoot, "showLoot");
listen(inputs.showLootNames, "showLootNames");
listen(inputs.lootMinPrice, "lootMinPrice", false, Number);
listen(inputs.showContainers, "showContainers");
listen(inputs.showContainerNames, "showContainerNames");
listen(inputs.containerMaxDist, "containerMaxDist", false, Number);
listen(inputs.showCorpses, "showCorpses");
listen(inputs.showExfils, "showExfils");

listen(inputs.localColor, "local", true);
listen(inputs.teammateColor, "teammate", true);
listen(inputs.pmcColor, "pmc", true);
listen(inputs.scavColor, "scav", true);
listen(inputs.pscavColor, "pscav", true);
listen(inputs.raiderColor, "raider", true);
listen(inputs.bossColor, "boss", true);
listen(inputs.deadColor, "dead", true);
listen(inputs.lootColor, "loot", true);
listen(inputs.lootImportantColor, "lootImportant", true);
listen(inputs.lootWishlistColor, "lootWishlist", true);
listen(inputs.containerColor, "container", true);
listen(inputs.corpseColor, "corpse", true);
listen(inputs.exfilOpenColor, "exfilOpen", true);
listen(inputs.exfilPendingColor, "exfilPending", true);
listen(inputs.exfilClosedColor, "exfilClosed", true);

if (inputs.centerOnLocal) {
  inputs.centerOnLocal.onclick = () => {
    state.freeMode = false;
    state.followTarget = null;
    freeAnchor = { x: 0, y: 0, mapId: "" };
    if (inputs.freeMode) inputs.freeMode.checked = false;
    updateFollowBadge();
    saveSettings();
  };
}

// Double-click on a player to follow them
canvas.addEventListener("dblclick", e => {
  const mx = e.clientX, my = e.clientY;
  let best = null, bestDist = Infinity;
  for (const h of hitList) {
    if (h.kind !== "player") continue;
    const dx = mx - h.px, dy = my - h.py;
    const d = dx * dx + dy * dy;
    if (d < h.r * h.r && d < bestDist) { bestDist = d; best = h; }
  }
  if (best) {
    const p = best.data;
    if (p.isLocal) {
      state.followTarget = null;
    } else {
      state.followTarget = p.name || null;
    }
    state.freeMode = false;
    freeAnchor = { x: 0, y: 0, mapId: "" };
    if (inputs.freeMode) inputs.freeMode.checked = false;
    updateFollowBadge();
    saveSettings();
  }
});

function updateFollowBadge() {
  if (inputs.modeBadge) {
    if (state.followTarget) {
      inputs.modeBadge.textContent = state.followTarget;
      inputs.modeBadge.style.color = "var(--accent)";
    } else {
      inputs.modeBadge.textContent = state.freeMode ? "free" : "follow";
      inputs.modeBadge.style.color = "";
    }
  }
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

function drawPlayers(players, map, cx, cy, rotRad, mapRect, localWorldY, hitList, distOrigin) {
  const size = Number(state.playerSize) || 6;
  const haveHeights = (localWorldY != null);
  const hasDistOrigin = distOrigin && Number.isFinite(distOrigin.worldX);

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

    // Names + Distance
    if (state.showNames) {
      let label = p.name || "";
      if (hasDistOrigin && p !== distOrigin) {
        const dx = p.worldX - distOrigin.worldX;
        const dy = (p.worldY ?? 0) - (distOrigin.worldY ?? 0);
        const dz = p.worldZ - distOrigin.worldZ;
        const dist = Math.sqrt(dx * dx + dy * dy + dz * dz);
        label += " [" + Math.round(dist) + "m]";
      }
      ctx.save();
      ctx.fillStyle = "rgba(229, 231, 235, 0.85)";
      ctx.font = "600 11px system-ui, sans-serif";
      ctx.textAlign = "center";
      ctx.shadowColor = "rgba(0,0,0,.7)";
      ctx.shadowBlur = 3;
      ctx.fillText(label, px, py - size - 6);
      ctx.restore();
    }
  }
}

/* ═══════════════════════════════════════════════════════════════════════════
   PRICE FORMATTER
   ═══════════════════════════════════════════════════════════════════════════ */
function formatPrice(p) {
  if (p >= 1000000) return (p / 1000000).toFixed(1) + "M";
  if (p >= 1000) return (p / 1000).toFixed(0) + "K";
  return String(p);
}

/* ═══════════════════════════════════════════════════════════════════════════
   LOOT DRAWING
   ═══════════════════════════════════════════════════════════════════════════ */
function lootColor(item) {
  if (item.wishlisted) return state.colors.lootWishlist;
  if (item.important)  return state.colors.lootImportant;
  return state.colors.loot;
}

function drawLoot(lootItems, map, cx, cy, rotRad, mapRect, localWorldY, hitList) {
  if (!lootItems || !lootItems.length) return;

  for (const item of lootItems) {
    if (!item) continue;
    if (item.price < state.lootMinPrice && !item.wishlisted && !item.questItem) continue;

    const pm = worldToMapUnzoomed(item.worldX, item.worldZ, map);
    const s = mapXYToScreen(pm.x, pm.y, mapRect, cx, cy, rotRad);
    const px = s.px, py = s.py;
    const col = lootColor(item);

    // Diamond marker
    const r = 3.5;
    ctx.save();
    ctx.fillStyle = col;
    ctx.globalAlpha = 0.9;
    ctx.beginPath();
    ctx.moveTo(px, py - r);
    ctx.lineTo(px + r, py);
    ctx.lineTo(px, py + r);
    ctx.lineTo(px - r, py);
    ctx.closePath();
    ctx.fill();
    ctx.restore();

    // Label
    if (state.showLootNames) {
      const label = item.price > 0 ? `${item.shortName} (${formatPrice(item.price)})` : item.shortName;
      ctx.save();
      ctx.fillStyle = col;
      ctx.font = "500 10px system-ui, sans-serif";
      ctx.textAlign = "left";
      ctx.shadowColor = "rgba(0,0,0,.7)";
      ctx.shadowBlur = 2;
      ctx.fillText(label, px + 6, py + 3.5);
      ctx.restore();
    }

    hitList.push({
      kind: "loot", px, py, r: 12,
      data: { name: item.shortName, price: item.price, wishlisted: item.wishlisted, questItem: item.questItem }
    });
  }
}

/* ═══════════════════════════════════════════════════════════════════════════
   CONTAINER DRAWING
   ═══════════════════════════════════════════════════════════════════════════ */
function drawContainers(containers, map, cx, cy, rotRad, mapRect, hitList, local) {
  if (!containers || !containers.length) return;
  const col = state.colors.container;
  const maxDist = Number(state.containerMaxDist) || 0;
  const hasLocal = local && Number.isFinite(local.worldX);
  const selNames = state.selectedContainers;
  const hasFilter = Array.isArray(selNames) && selNames.length > 0;

  for (const c of containers) {
    if (!c) continue;

    // Name-based selection filter (client-side)
    if (hasFilter && !selNames.includes(c.name)) continue;

    // Distance filter
    if (maxDist > 0 && hasLocal) {
      const dx = c.worldX - local.worldX;
      const dz = c.worldZ - local.worldZ;
      const dist = Math.sqrt(dx * dx + dz * dz);
      if (dist > maxDist) continue;
    }

    const pm = worldToMapUnzoomed(c.worldX, c.worldZ, map);
    const s = mapXYToScreen(pm.x, pm.y, mapRect, cx, cy, rotRad);
    const px = s.px, py = s.py;

    // Square marker
    const hs = 3.5;
    ctx.save();
    ctx.strokeStyle = col;
    ctx.lineWidth = 1.6;
    ctx.globalAlpha = c.searched ? 0.4 : 0.9;
    ctx.strokeRect(px - hs, py - hs, hs * 2, hs * 2);
    ctx.restore();

    if (state.showContainerNames) {
      ctx.save();
      ctx.fillStyle = col;
      ctx.globalAlpha = c.searched ? 0.4 : 0.85;
      ctx.font = "500 10px system-ui, sans-serif";
      ctx.textAlign = "left";
      ctx.shadowColor = "rgba(0,0,0,.7)";
      ctx.shadowBlur = 2;
      ctx.fillText(c.name, px + 6, py + 3.5);
      ctx.restore();
    }

    hitList.push({ kind: "container", px, py, r: 10, data: c });
  }
}

/* ═══════════════════════════════════════════════════════════════════════════
   CORPSE DRAWING
   ═══════════════════════════════════════════════════════════════════════════ */
function drawCorpses(corpses, map, cx, cy, rotRad, mapRect, hitList) {
  if (!corpses || !corpses.length) return;
  const col = state.colors.corpse;

  for (const c of corpses) {
    if (!c) continue;
    const pm = worldToMapUnzoomed(c.worldX, c.worldZ, map);
    const s = mapXYToScreen(pm.x, pm.y, mapRect, cx, cy, rotRad);
    const px = s.px, py = s.py;

    // X marker
    const d = 4;
    ctx.save();
    ctx.strokeStyle = col;
    ctx.lineWidth = 2;
    ctx.lineCap = "round";
    ctx.globalAlpha = 0.7;
    ctx.beginPath();
    ctx.moveTo(px - d, py - d); ctx.lineTo(px + d, py + d);
    ctx.moveTo(px + d, py - d); ctx.lineTo(px - d, py + d);
    ctx.stroke();
    ctx.restore();

    hitList.push({ kind: "corpse", px, py, r: 10, data: c });
  }
}

/* ═══════════════════════════════════════════════════════════════════════════
   EXFIL DRAWING
   ═══════════════════════════════════════════════════════════════════════════ */
function exfilColor(status) {
  // 0=Closed, 1=Pending, 2=Open
  switch (status) {
    case 2: return state.colors.exfilOpen;
    case 1: return state.colors.exfilPending;
    default: return state.colors.exfilClosed;
  }
}

function drawExfils(exfils, map, cx, cy, rotRad, mapRect, hitList) {
  if (!exfils || !exfils.length) return;

  for (const e of exfils) {
    if (!e) continue;
    const pm = worldToMapUnzoomed(e.worldX, e.worldZ, map);
    const s = mapXYToScreen(pm.x, pm.y, mapRect, cx, cy, rotRad);
    const px = s.px, py = s.py;
    const col = exfilColor(e.status);

    // Circle marker
    ctx.save();
    ctx.beginPath();
    ctx.arc(px, py, 5, 0, Math.PI * 2);
    ctx.strokeStyle = "rgba(0,0,0,.5)";
    ctx.lineWidth = 2.5;
    ctx.stroke();
    ctx.strokeStyle = col;
    ctx.lineWidth = 1.6;
    ctx.stroke();
    ctx.restore();

    // Name label
    ctx.save();
    ctx.fillStyle = col;
    ctx.font = "600 10px system-ui, sans-serif";
    ctx.textAlign = "center";
    ctx.shadowColor = "rgba(0,0,0,.7)";
    ctx.shadowBlur = 3;
    ctx.fillText(e.name, px, py - 9);
    ctx.restore();

    hitList.push({ kind: "exfil", px, py, r: 14, data: e });
  }
}

/* ═══════════════════════════════════════════════════════════════════════════
   AIMVIEW DRAG
   ═══════════════════════════════════════════════════════════════════════════ */
let avDragging = false;
let avDragStart = { x: 0, y: 0 };
let avDragOrigin = { x: 0, y: 0 };

aimviewEl.addEventListener("mousedown", e => {
  if (e.button !== 0) return;
  e.preventDefault();
  avDragging = true;
  aimviewEl.classList.add("dragging");
  const rect = aimviewEl.getBoundingClientRect();
  avDragOrigin = { x: rect.left, y: rect.top };
  avDragStart = { x: e.clientX, y: e.clientY };
});

window.addEventListener("mousemove", e => {
  if (!avDragging) return;
  const nx = avDragOrigin.x + (e.clientX - avDragStart.x);
  const ny = avDragOrigin.y + (e.clientY - avDragStart.y);
  applyAimviewPos(nx, ny);
});

window.addEventListener("mouseup", () => {
  if (!avDragging) return;
  avDragging = false;
  aimviewEl.classList.remove("dragging");
  saveSettings();
});

// Touch support
aimviewEl.addEventListener("touchstart", e => {
  if (e.touches.length !== 1) return;
  e.preventDefault();
  avDragging = true;
  aimviewEl.classList.add("dragging");
  const rect = aimviewEl.getBoundingClientRect();
  avDragOrigin = { x: rect.left, y: rect.top };
  avDragStart = { x: e.touches[0].clientX, y: e.touches[0].clientY };
}, { passive: false });

aimviewEl.addEventListener("touchmove", e => {
  if (!avDragging || e.touches.length !== 1) return;
  e.preventDefault();
  const nx = avDragOrigin.x + (e.touches[0].clientX - avDragStart.x);
  const ny = avDragOrigin.y + (e.touches[0].clientY - avDragStart.y);
  applyAimviewPos(nx, ny);
}, { passive: false });

aimviewEl.addEventListener("touchend", () => {
  if (!avDragging) return;
  avDragging = false;
  aimviewEl.classList.remove("dragging");
  saveSettings();
});

function applyAimviewPos(x, y) {
  const size = Number(state.aimviewSize) || 250;
  const maxX = window.innerWidth - size;
  const maxY = window.innerHeight - size;
  x = Math.max(0, Math.min(x, maxX));
  y = Math.max(0, Math.min(y, maxY));
  state.aimviewX = Math.round(x);
  state.aimviewY = Math.round(y);
  aimviewEl.style.left = x + "px";
  aimviewEl.style.top = y + "px";
  aimviewEl.style.right = "auto";
  aimviewEl.style.bottom = "auto";
}

/* ═══════════════════════════════════════════════════════════════════════════
   AIMVIEW WIDGET
   ═══════════════════════════════════════════════════════════════════════════ */
function drawAimview(camera, players, lootItems, containers) {
  if (!state.showAimview || !camera) {
    aimviewEl.classList.add("hidden");
    return;
  }

  aimviewEl.classList.remove("hidden");

  const size = Number(state.aimviewSize) || 250;
  aimviewEl.style.width = size + "px";
  aimviewEl.style.height = size + "px";

  // Apply saved position (or default to bottom-right)
  if (state.aimviewX != null && state.aimviewY != null) {
    const maxX = window.innerWidth - size;
    const maxY = window.innerHeight - size;
    aimviewEl.style.left = Math.max(0, Math.min(state.aimviewX, maxX)) + "px";
    aimviewEl.style.top = Math.max(0, Math.min(state.aimviewY, maxY)) + "px";
    aimviewEl.style.right = "auto";
    aimviewEl.style.bottom = "auto";
  }

  const avDpr = window.devicePixelRatio || 1;
  const bw = Math.round(size * avDpr);
  const bh = Math.round(size * avDpr);
  if (aimviewCanvas.width !== bw) aimviewCanvas.width = bw;
  if (aimviewCanvas.height !== bh) aimviewCanvas.height = bh;

  avCtx.setTransform(avDpr, 0, 0, avDpr, 0, 0);
  avCtx.clearRect(0, 0, size, size);

  // Background
  avCtx.fillStyle = "rgba(11, 18, 32, 0.6)";
  avCtx.fillRect(0, 0, size, size);

  // Crosshair
  const mid = size / 2;
  avCtx.strokeStyle = "rgba(255,255,255,.15)";
  avCtx.lineWidth = 1;
  avCtx.beginPath();
  avCtx.moveTo(mid - 8, mid); avCtx.lineTo(mid + 8, mid);
  avCtx.moveTo(mid, mid - 8); avCtx.lineTo(mid, mid + 8);
  avCtx.stroke();

  // Build synthetic forward/right/up from camera player's raw yaw + pitch
  const yaw = Number(camera.rawYaw) || 0;
  const pitch = Number(camera.pitch) || 0;
  const cosY = Math.cos(yaw);
  const sinY = Math.sin(yaw);
  const cosP = Math.cos(pitch);
  const sinP = Math.sin(pitch);

  // EFT coordinate system — matches AimviewWidget.cs exactly
  const fwd   = { x: sinY * cosP, y: -sinP, z: cosY * cosP };
  const right = { x: cosY, y: 0, z: -sinY };
  // Up = -(right × forward)
  const upX = right.y * fwd.z - right.z * fwd.y;
  const upY = right.z * fwd.x - right.x * fwd.z;
  const upZ = right.x * fwd.y - right.y * fwd.x;
  const up = { x: -upX, y: -upY, z: -upZ };

  // Eye position: body root + eye height offset
  const eyeHeight = 1.35;
  const localPos = { x: camera.worldX, y: camera.worldY + eyeHeight, z: camera.worldZ };
  const zoom = 1.0;
  const maxDist = 300;
  const cameraName = camera.name;

  function projectAV(wx, wy, wz) {
    const dx = wx - localPos.x;
    const dy = wy - localPos.y;
    const dz = wz - localPos.z;

    const dotF = dx * fwd.x + dy * fwd.y + dz * fwd.z;
    if (dotF < 0.5) return null; // Behind
    if (dotF > maxDist) return null; // Too far

    const dotR = dx * right.x + dy * right.y + dz * right.z;
    const dotU = dx * up.x + dy * up.y + dz * up.z;

    const sx = mid + (dotR / dotF) * zoom * mid;
    const sy = mid - (dotU / dotF) * zoom * mid;

    if (sx < -10 || sx > size + 10 || sy < -10 || sy > size + 10) return null;
    return { sx, sy, dist: dotF };
  }

  // Draw players (skip the camera player — they ARE the viewpoint)
  if (players) {
    for (const p of players) {
      if (!p || !p.isActive || p.isAlive === false) continue;
      if (p.name === cameraName) continue;
      const proj = projectAV(p.worldX, p.worldY, p.worldZ);
      if (!proj) continue;
      const col = playerColor(p);
      const r = Math.max(2, 5 - proj.dist * 0.03);
      avCtx.beginPath();
      avCtx.arc(proj.sx, proj.sy, r, 0, Math.PI * 2);
      avCtx.fillStyle = col;
      avCtx.fill();
    }
  }

  // Draw loot
  if (state.showLoot && lootItems) {
    for (const item of lootItems) {
      if (!item) continue;
      if (item.price < state.lootMinPrice && !item.wishlisted && !item.questItem) continue;
      const proj = projectAV(item.worldX, item.worldY, item.worldZ);
      if (!proj) continue;
      const col = lootColor(item);
      const r = 2;
      avCtx.save();
      avCtx.fillStyle = col;
      avCtx.globalAlpha = 0.8;
      avCtx.beginPath();
      avCtx.moveTo(proj.sx, proj.sy - r);
      avCtx.lineTo(proj.sx + r, proj.sy);
      avCtx.lineTo(proj.sx, proj.sy + r);
      avCtx.lineTo(proj.sx - r, proj.sy);
      avCtx.closePath();
      avCtx.fill();
      avCtx.restore();
    }
  }

  // Draw containers
  if (state.showContainers && containers) {
    for (const c of containers) {
      if (!c) continue;
      const proj = projectAV(c.worldX, c.worldY, c.worldZ);
      if (!proj) continue;
      avCtx.save();
      avCtx.strokeStyle = state.colors.container;
      avCtx.lineWidth = 1.2;
      avCtx.globalAlpha = 0.7;
      avCtx.strokeRect(proj.sx - 2, proj.sy - 2, 4, 4);
      avCtx.restore();
    }
  }

  // Border
  avCtx.strokeStyle = "rgba(255,255,255,.08)";
  avCtx.lineWidth = 1;
  avCtx.strokeRect(0.5, 0.5, size - 1, size - 1);
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

  let html = "";

  if (found.kind === "player") {
    const p = found.data;
    const col = playerColor(p);
    const typeName = typeNames[p.type] || "Bot";
    const status = p.isAlive === false ? "Dead" : "Alive";
    const statusClass = p.isAlive === false ? "bad" : "ok";

    html += `<div class="t-header"><span class="t-dot" style="background:${col}"></span><span class="t-name">${esc(p.name || "Unknown")}</span></div>`;
    html += `<div class="t-type">${typeName} · <span style="color:var(--${statusClass})">${status}</span></div>`;

    // Compute distance from local player
    let distStr = null;
    const distOrigin = lastFocusPlayer || lastLocalPlayer;
    if (distOrigin && distOrigin !== p && Number.isFinite(distOrigin.worldX) && Number.isFinite(p.worldX)) {
      const dx = p.worldX - distOrigin.worldX;
      const dy = (p.worldY ?? 0) - (distOrigin.worldY ?? 0);
      const dz = p.worldZ - distOrigin.worldZ;
      distStr = Math.round(Math.sqrt(dx * dx + dy * dy + dz * dz)) + "m";
    }

    const hasExtra = (p.gearValue > 0) || (readWorldY(p) != null) || distStr;
    if (hasExtra) {
      html += `<div class="t-sep"></div><div class="t-grid">`;
      if (distStr) {
        html += `<span class="k">Distance</span><span class="v">${distStr}</span>`;
      }
      if (p.gearValue > 0) {
        html += `<span class="k">Gear</span><span class="v">₽${p.gearValue.toLocaleString()}</span>`;
      }
      const wy = readWorldY(p);
      if (wy != null) {
        html += `<span class="k">Height</span><span class="v">${wy.toFixed(1)}</span>`;
      }
      html += `</div>`;
    }
  } else if (found.kind === "loot") {
    const d = found.data;
    const col = d.wishlisted ? state.colors.lootWishlist : (d.price >= (state.lootMinPrice * 2) ? state.colors.lootImportant : state.colors.loot);
    html += `<div class="t-header"><span class="t-dot" style="background:${col}"></span><span class="t-name">${esc(d.name)}</span></div>`;
    html += `<div class="t-type">Loot</div>`;
    html += `<div class="t-sep"></div><div class="t-grid">`;
    if (d.price > 0) html += `<span class="k">Price</span><span class="v">₽${d.price.toLocaleString()}</span>`;
    if (d.wishlisted) html += `<span class="k">Status</span><span class="v" style="color:${state.colors.lootWishlist}">★ Wishlist</span>`;
    if (d.questItem) html += `<span class="k">Status</span><span class="v" style="color:${state.colors.lootImportant}">Quest Item</span>`;
    html += `</div>`;
  } else if (found.kind === "container") {
    const c = found.data;
    const col = state.colors.container;
    html += `<div class="t-header"><span class="t-dot" style="background:${col}"></span><span class="t-name">${esc(c.name)}</span></div>`;
    html += `<div class="t-type">Container${c.searched ? " · <span style='color:var(--text-dim)'>Searched</span>" : ""}</div>`;
  } else if (found.kind === "corpse") {
    const c = found.data;
    const col = state.colors.corpse;
    html += `<div class="t-header"><span class="t-dot" style="background:${col}"></span><span class="t-name">${esc(c.name)}</span></div>`;
    html += `<div class="t-type">Corpse</div>`;
    if (c.totalValue > 0) {
      html += `<div class="t-sep"></div><div class="t-grid">`;
      html += `<span class="k">Gear Value</span><span class="v">₽${c.totalValue.toLocaleString()}</span>`;
      html += `</div>`;
    }
  } else if (found.kind === "exfil") {
    const e = found.data;
    const col = exfilColor(e.status);
    const statusText = e.status === 2 ? "Open" : e.status === 1 ? "Pending" : "Closed";
    const statusClass = e.status === 2 ? "ok" : e.status === 1 ? "warn" : "bad";
    html += `<div class="t-header"><span class="t-dot" style="background:${col}"></span><span class="t-name">${esc(e.name)}</span></div>`;
    html += `<div class="t-type">Exfil · <span style="color:var(--${statusClass})">${statusText}</span></div>`;
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
let lastFocusPlayer = null;

function frame() {
  requestAnimationFrame(frame);
  resizeCanvas();

  ctx.clearRect(0, 0, cw, ch);
  hitList = [];

  if (!radarData) {
    aimviewEl.classList.add("hidden");
    return;
  }

  const map = radarData.map || null;
  const players = Array.isArray(radarData.players) ? radarData.players : [];
  const { cx, cy } = getViewportCenter();

  // Find local player
  const local = players.find(p => p?.isLocal) || null;
  lastLocalPlayer = local;

  // Resolve follow target: a specific player name, or fall back to local
  let focusPlayer = local;
  if (state.followTarget) {
    const target = players.find(p => p && p.name === state.followTarget && p.isActive);
    if (target) {
      focusPlayer = target;
    } else {
      // Target no longer available — clear follow
      state.followTarget = null;
      updateFollowBadge();
    }
  }
  lastFocusPlayer = focusPlayer;

  // Rotation — always based on local player yaw for map orientation
  const localYaw = local ? (Number(local.yaw) || 0) : 0;
  lastRotRad = localYaw;

  // Anchor — center on the focus player
  let anchor = null;
  if (state.freeMode) {
    const mapId = radarData.mapID ?? "";
    if (freeAnchor.mapId !== mapId) {
      if (focusPlayer && map) {
        const lm = readPlayerMapXY(focusPlayer, map);
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
    if (focusPlayer && map) {
      const tm = readPlayerMapXY(focusPlayer, map);
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

  // Map entities (require map for world-to-screen projection)
  if (map) {
    const mapRect = getMapScreenRect(map, cx, cy, state.zoom, anchor);
    if (mapRect) {
      if (state.showGroups) drawGroupConnectors(players, map, cx, cy, lastRotRad, mapRect);
      if (state.showExfils) drawExfils(radarData.exfils, map, cx, cy, lastRotRad, mapRect, hitList);
      if (state.showCorpses) drawCorpses(radarData.corpses, map, cx, cy, lastRotRad, mapRect, hitList);
      if (state.showContainers) drawContainers(radarData.containers, map, cx, cy, lastRotRad, mapRect, hitList, local);
      if (state.showLoot) drawLoot(radarData.loot, map, cx, cy, lastRotRad, mapRect, readWorldY(local), hitList);
      if (state.showPlayers) drawPlayers(players, map, cx, cy, lastRotRad, mapRect, readWorldY(local), hitList, focusPlayer);
    }
  }

  // Aimview (independent of map — uses world-space projection)
  drawAimview(focusPlayer, players, radarData.loot, radarData.containers);

  // Tooltip
  updateHover();
}

/* ═══════════════════════════════════════════════════════════════════════════
   CONTAINER SELECTION
   ═══════════════════════════════════════════════════════════════════════════ */
let containerTypes = []; // { id, name, selected }

async function fetchContainerTypes() {
  try {
    const res = await fetch("/api/containers", { cache: "no-store" });
    if (!res.ok) return;
    const data = await res.json();
    if (!Array.isArray(data)) return;
    containerTypes = data;
    buildContainerList();
  } catch { /* ignore */ }
}

function buildContainerList() {
  const wrap = document.getElementById("containerList");
  if (!wrap) return;
  wrap.innerHTML = "";
  const sel = state.selectedContainers;

  // Sort alphabetically by name
  const sorted = [...containerTypes].sort((a, b) => (a.name || "").localeCompare(b.name || ""));

  for (const ct of sorted) {
    const isOn = sel.length === 0 || sel.includes(ct.name);
    const lbl = document.createElement("label");
    lbl.className = "container-item";
    lbl.innerHTML = `<span class="toggle-switch small"><input type="checkbox" ${isOn ? "checked" : ""}><span class="slider"></span></span><span class="cname">${esc(ct.name)}</span>`;
    const cb = lbl.querySelector("input");
    cb.addEventListener("change", () => {
      if (cb.checked) {
        // Remove from filter (show it)
        const idx = state.selectedContainers.indexOf(ct.name);
        if (idx >= 0) state.selectedContainers.splice(idx, 1);
        // If all are now checked, clear the array (= show all)
        const allChecked = wrap.querySelectorAll("input[type=checkbox]:not(:checked)").length === 0;
        if (allChecked) state.selectedContainers = [];
      } else {
        // First time unchecking: populate all names then remove this one
        if (state.selectedContainers.length === 0) {
          state.selectedContainers = sorted.map(c => c.name);
        }
        const idx = state.selectedContainers.indexOf(ct.name);
        if (idx >= 0) state.selectedContainers.splice(idx, 1);
      }
      saveSettings();
    });
    wrap.appendChild(lbl);
  }

  // Select All / Deselect All buttons
  const btnWrap = document.getElementById("containerBtns");
  if (btnWrap) {
    btnWrap.innerHTML = "";
    const btnAll = document.createElement("button");
    btnAll.className = "small";
    btnAll.textContent = "Select All";
    btnAll.onclick = () => {
      state.selectedContainers = [];
      wrap.querySelectorAll("input[type=checkbox]").forEach(cb => cb.checked = true);
      saveSettings();
    };
    const btnNone = document.createElement("button");
    btnNone.className = "small";
    btnNone.textContent = "Deselect All";
    btnNone.onclick = () => {
      state.selectedContainers = ["__none__"];
      wrap.querySelectorAll("input[type=checkbox]").forEach(cb => cb.checked = false);
      saveSettings();
    };
    btnWrap.appendChild(btnAll);
    btnWrap.appendChild(btnNone);
  }
}

/* ═══════════════════════════════════════════════════════════════════════════
   INIT
   ═══════════════════════════════════════════════════════════════════════════ */
loadSettings();
bindAllInputs();
applyUiFromState();
updateAllRangeValues();
updateFollowBadge();
startPolling();
fetchRadar();
fetchContainerTypes();
requestAnimationFrame(frame);
