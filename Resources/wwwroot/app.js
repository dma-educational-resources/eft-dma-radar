const canvas   = document.getElementById("radar");
const ctx      = canvas.getContext("2d", { alpha:true });

const statusEl = document.getElementById("status");
const subline  = document.getElementById("subline");

const sidebar  = document.getElementById("sidebar");
const toggle   = document.getElementById("toggle");
const menuBtn  = document.getElementById("menuBtn");
const edgeZone = document.getElementById("edgeZone");

const tooltipEl = document.getElementById("tooltip");

/* Widgets */
const lootWidget = document.getElementById("lootWidget");
const lootWidgetMinBtn = document.getElementById("lootWidgetMinBtn");
const lootWidgetSearch = document.getElementById("lootWidgetSearch");
const lootWidgetList = document.getElementById("lootWidgetList");
const lootWidgetCount = document.getElementById("lootWidgetCount");
const lootWidgetSub = document.getElementById("lootWidgetSub");

const playersWidget = document.getElementById("playersWidget");
const playersWidgetMinBtn = document.getElementById("playersWidgetMinBtn");
const playersWidgetOnlyPMCs = document.getElementById("playersWidgetOnlyPMCs");
const playersWidgetList = document.getElementById("playersWidgetList");
const playersWidgetCount = document.getElementById("playersWidgetCount");
const playersWidgetSub = document.getElementById("playersWidgetSub");

const lootFilterModal = document.getElementById("lootFilterModal");
const lootFilterCard  = lootFilterModal.querySelector(".card");
const lootFilterHeader= lootFilterModal.querySelector(".header");
const lootFilterBody  = document.getElementById("lootFilterBody");

const openLootFiltersBtn = document.getElementById("openLootFilters");
const closeLootFiltersBtn = document.getElementById("closeLootFilters");
const addLootGroupBtn = document.getElementById("addLootGroup");
const copyLootGroupsBtn = document.getElementById("copyLootGroups");
const pasteLootGroupsBtn = document.getElementById("pasteLootGroups");

const lootGroupsList = document.getElementById("lootGroupsList");
const lootDbStatus = document.getElementById("lootDbStatus");
const lootGroupsMeta = document.getElementById("lootGroupsMeta");
const lootFilterBadge = document.getElementById("lootFilterBadge");

const srPortal = document.getElementById("srPortal");

let dpr = window.devicePixelRatio || 1;
let cw = 0, ch = 0;

const ZOOM_MIN = 0.05;
const ZOOM_MAX = 4.0;

/* =========================
   SIDEBAR
========================= */
let sidebarTempOpen = false;
let sidebarCloseTimer = null;

function isSidebarOpen(){
  return !sidebar.classList.contains("collapsed");
}
function setSidebarCollapsed(collapsed, temp=false){
  sidebar.classList.toggle("collapsed", collapsed);
  sidebarTempOpen = (!collapsed && temp);
  toggle.textContent = collapsed ? ">" : "<";
}
function toggleSidebarPinned(){
  const nowOpen = !isSidebarOpen();
  setSidebarCollapsed(!nowOpen, false);
  state.sidebarCollapsed = !nowOpen;
  saveSettings();
  hideTooltip();
}
toggle.onclick = () => toggleSidebarPinned();
menuBtn.onclick = () => toggleSidebarPinned();

function openSidebarTemp(){
  if(!state.hoverOpenSidebar) return;
  if(isSidebarOpen()) return;
  if(state.sidebarCollapsed) setSidebarCollapsed(false, true);
}
function scheduleCloseSidebarTemp(){
  if(!state.hoverOpenSidebar) return;
  if(!sidebarTempOpen) return;
  clearTimeout(sidebarCloseTimer);
  sidebarCloseTimer = setTimeout(() => {
    if(sidebarTempOpen && state.sidebarCollapsed){
      setSidebarCollapsed(true, false);
    }
  }, 250);
}
edgeZone.addEventListener("mouseenter", openSidebarTemp);
sidebar.addEventListener("mouseenter", () => { clearTimeout(sidebarCloseTimer); });
sidebar.addEventListener("mouseleave", scheduleCloseSidebarTemp);

/* =========================
   TABS
========================= */
function activateTab(tabId){
  document.querySelectorAll(".tab,.tab-content").forEach(e => e.classList.remove("active"));
  document.querySelectorAll(".tab[data-tab='"+tabId+"']").forEach(e => e.classList.add("active"));
  const content = document.getElementById(tabId);
  if(content) content.classList.add("active");
}
document.querySelectorAll(".tab").forEach(tab => tab.onclick = () => activateTab(tab.dataset.tab));

/* =========================
   CANVAS RESIZE
========================= */
function resizeCanvas(){
  dpr = window.devicePixelRatio || 1;
  const rect = canvas.getBoundingClientRect();
  cw = Math.max(1, rect.width);
  ch = Math.max(1, rect.height);

  const bw = Math.max(1, Math.round(cw * dpr));
  const bh = Math.max(1, Math.round(ch * dpr));

  if(canvas.width !== bw) canvas.width = bw;
  if(canvas.height !== bh) canvas.height = bh;

  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
}
window.addEventListener("resize", resizeCanvas);
resizeCanvas();

/* =========================
   PERSISTENCE
========================= */
const LS_KEY_V3 = "xm_webradar_settings_v3";
const LS_KEY_V2 = "xm_webradar_settings_v2";

const IDB_DB = "xm_webradar";
const IDB_STORE = "kv";

function deepClone(obj){
  try{ return structuredClone(obj); }catch{ return JSON.parse(JSON.stringify(obj)); }
}

function idbOpen(){
  return new Promise((resolve, reject) => {
    if(!("indexedDB" in window)) return reject(new Error("IndexedDB unavailable"));
    const req = indexedDB.open(IDB_DB, 1);
    req.onupgradeneeded = () => {
      const db = req.result;
      if(!db.objectStoreNames.contains(IDB_STORE)){
        db.createObjectStore(IDB_STORE);
      }
    };
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error || new Error("IDB open failed"));
  });
}
async function idbGet(key){
  const db = await idbOpen();
  return new Promise((resolve, reject) => {
    const tx = db.transaction(IDB_STORE, "readonly");
    const st = tx.objectStore(IDB_STORE);
    const req = st.get(key);
    req.onsuccess = () => resolve(req.result ?? null);
    req.onerror = () => reject(req.error || new Error("IDB get failed"));
  });
}
async function idbSet(key, val){
  const db = await idbOpen();
  return new Promise((resolve, reject) => {
    const tx = db.transaction(IDB_STORE, "readwrite");
    const st = tx.objectStore(IDB_STORE);
    const req = st.put(val, key);
    req.onsuccess = () => resolve(true);
    req.onerror = () => reject(req.error || new Error("IDB set failed"));
  });
}
async function idbDel(key){
  const db = await idbOpen();
  return new Promise((resolve, reject) => {
    const tx = db.transaction(IDB_STORE, "readwrite");
    const st = tx.objectStore(IDB_STORE);
    const req = st.delete(key);
    req.onsuccess = () => resolve(true);
    req.onerror = () => reject(req.error || new Error("IDB del failed"));
  });
}

const defaults = {
  __savedAt: 0,

  sidebarCollapsed: false,
  hoverOpenSidebar: true,

  showMap: true,
  zoom: 1.0,
  rotateWithLocal: false,
  pollMs: 50,

  showPlayers: true,
  showAim: true,
  showNames: false,
  showHeight: true,
  playerSize: 6,
  freeMode: false,

  centerTarget: "local",

  showGroups: true,
  groupAlpha: 0.35,

  showLoot: true,
  showLootName: false,
  showLootPrice: false,

  minPrice: 0,
  importantPrice: 200000,

  corpseMinValue: 0,

  lootSize: 3,

  lootFiltersEnabled: false,
  lootGroups: [],

  lootFilterWindow: { x: null, y: null, w: 860, h: 640 },

  showLootWidget: false,
  showPlayersWidget: false,

  lootWidget: { x: 14, y: 64, minimized: false },
  playersWidget: { x: 14, y: 420, minimized: false },

  lootWidgetSearch: "",
  playersWidgetOnlyPMCs: false,

  showExtracts: true,
  showTransits: true,
  showPoiNames: true,
  extractColor: "#34d399",
  transitColor: "#a78bfa",

  colors: {
    local:    "#22c55e",
    teammate: "#4ade80",
    usec:     "#38bdf8",
    bear:     "#60a5fa",
    scav:     "#f59e0b",
    psav:     "#facc15",
    raider:   "#fb7185",
    boss:     "#ef4444",
    dead:     "#9ca3af",

    basicLoot: "#fbbf24",
    importantLoot: "#ef4444",
  }
};

let state = deepClone(defaults);

function mergeState(parsed){
  const out = {
    ...deepClone(defaults),
    ...parsed,
    colors: { ...deepClone(defaults.colors), ...(parsed.colors || {}) },
    lootFilterWindow: { ...deepClone(defaults.lootFilterWindow), ...(parsed.lootFilterWindow || {}) },
    lootWidget: { ...deepClone(defaults.lootWidget), ...(parsed.lootWidget || {}) },
    playersWidget: { ...deepClone(defaults.playersWidget), ...(parsed.playersWidget || {}) },
  };

  if(out.lootFilterWindow){
    const w = Number(out.lootFilterWindow.w);
    const h = Number(out.lootFilterWindow.h);
    const x = out.lootFilterWindow.x;
    const y = out.lootFilterWindow.y;
    out.lootFilterWindow.w = Number.isFinite(w) ? Math.max(520, Math.min(w, window.innerWidth - 24)) : defaults.lootFilterWindow.w;
    out.lootFilterWindow.h = Number.isFinite(h) ? Math.max(320, Math.min(h, window.innerHeight - 24)) : defaults.lootFilterWindow.h;
    out.lootFilterWindow.x = (x == null) ? null : Number(x);
    out.lootFilterWindow.y = (y == null) ? null : Number(y);
    if(!Number.isFinite(out.lootFilterWindow.x)) out.lootFilterWindow.x = null;
    if(!Number.isFinite(out.lootFilterWindow.y)) out.lootFilterWindow.y = null;
  }

  const normWidget = (w) => {
    const x = Number(w?.x);
    const y = Number(w?.y);
    return { x: Number.isFinite(x) ? x : 14, y: Number.isFinite(y) ? y : 64, minimized: !!w?.minimized };
  };
  out.lootWidget = normWidget(out.lootWidget);
  out.playersWidget = normWidget(out.playersWidget);

  if(!Array.isArray(out.lootGroups)) out.lootGroups = [];
  for(const g of out.lootGroups){
    if(!g || typeof g !== "object") continue;
    if(!Array.isArray(g.items)) g.items = [];
    if(typeof g.enabled !== "boolean") g.enabled = true;
    if(typeof g.name !== "string") g.name = "Group";
    if(typeof g.color !== "string") g.color = "#fbbf24";
    if(typeof g.id !== "string") g.id = "g_" + Math.random().toString(36).slice(2,8);
    for(const it of g.items){
      if(!it || typeof it !== "object") continue;
      if(typeof it.id !== "string") it.id = "";
      if(typeof it.enabled !== "boolean") it.enabled = true;
    }
    g.items = g.items.filter(x => x && typeof x.id === "string" && x.id.length > 0);
  }
  out.lootGroups = out.lootGroups.filter(g => g && typeof g.id === "string");

  out.minPrice = Math.max(0, Number(out.minPrice) || 0);
  out.importantPrice = Math.max(0, Number(out.importantPrice) || 0);

  out.corpseMinValue = Math.max(0, Number(out.corpseMinValue) || 0);

  if(typeof out.centerTarget !== "string" || !out.centerTarget) out.centerTarget = "local";

  out.zoom = clamp(Number(out.zoom) || 1, ZOOM_MIN, ZOOM_MAX);

  return out;
}

async function initState(){
  let lsRawV3 = localStorage.getItem(LS_KEY_V3);
  let lsRawV2 = localStorage.getItem(LS_KEY_V2);

  let lsParsed = null;
  try{
    const raw = lsRawV3 || lsRawV2;
    if(raw) lsParsed = mergeState(JSON.parse(raw));
  }catch{}

  let idbParsed = null;
  try{
    const raw = await idbGet(LS_KEY_V3);
    if(raw && typeof raw === "string") idbParsed = mergeState(JSON.parse(raw));
    else if(raw && typeof raw === "object") idbParsed = mergeState(raw);
  }catch{}

  if(lsParsed && idbParsed){
    state = (Number(idbParsed.__savedAt) > Number(lsParsed.__savedAt)) ? idbParsed : lsParsed;
  }else if(idbParsed) state = idbParsed;
  else if(lsParsed) state = lsParsed;
  else state = deepClone(defaults);

  bindAllInputs();
  applyUiFromState();
  applyWidgetsFromState();
  rebuildLootFilterIndex();
  updateLootFilterBadges();
  updateCenterTargetSelect();
  startPolling();
}
initState();

function saveSettings(){
  state.__savedAt = Date.now();
  try{ localStorage.setItem(LS_KEY_V3, JSON.stringify(state)); }catch{}
  idbSet(LS_KEY_V3, JSON.stringify(state)).catch(()=>{});

  const badge = document.getElementById("cacheBadge");
  if(badge){
    badge.textContent = "saved";
    clearTimeout(saveSettings._t);
    saveSettings._t = setTimeout(()=> badge.textContent = "cache", 800);
  }
}

async function resetSettings(){
  try{ localStorage.removeItem(LS_KEY_V3); }catch{}
  try{ localStorage.removeItem(LS_KEY_V2); }catch{}
  await idbDel(LS_KEY_V3).catch(()=>{});

  state = deepClone(defaults);
  freeAnchor = { x: 0, y: 0, mapId: "" };

  bindAllInputs();
  applyUiFromState();
  applyWidgetsFromState();
  rebuildLootFilterIndex();
  updateLootFilterBadges();
  updateCenterTargetSelect();
  saveSettings();
  hideTooltip();
}

function $(id){ return document.getElementById(id); }

const inputs = {
  hoverOpenSidebar: $("hoverOpenSidebar"),

  showMap: $("showMap"),
  zoom: $("zoom"),
  rotateWithLocal: $("rotateWithLocal"),
  pollMs: $("pollMs"),
  freeMode: $("freeMode"),
  centerOnLocal: $("centerOnLocal"),
  modeBadge: $("modeBadge"),

  centerTargetSelect: $("centerTargetSelect"),

  showPlayers: $("showPlayers"),
  showAim: $("showAim"),
  showNames: $("showNames"),
  showHeight: $("showHeight"),
  playerSize: $("playerSize"),
  showPlayersWidget: $("showPlayersWidget"),

  showGroups: $("showGroups"),
  groupAlpha: $("groupAlpha"),

  showLoot: $("showLoot"),
  showLootName: $("showLootName"),
  showLootPrice: $("showLootPrice"),

  basicPrice: $("basicPrice"),
  basicPriceText: $("basicPriceText"),
  importantPrice: $("importantPrice"),
  importantPriceText: $("importantPriceText"),

  corpseMinValue: $("deadMinValue"),
  corpseMinValueText: $("deadMinValueText"),

  lootSize: $("lootSize"),
  lootFiltersEnabled: $("lootFiltersEnabled"),
  showLootWidget: $("showLootWidget"),

  localColor: $("localColor"),
  teammateColor: $("teammateColor"),
  usecColor: $("usecColor"),
  bearColor: $("bearColor"),
  scavColor: $("scavColor"),
  psavColor: $("psavColor"),
  raiderColor: $("raiderColor"),
  bossColor: $("bossColor"),
  deadColor: $("deadColor"),

  basicLootColor: $("basicLootColor"),
  importantLootColor: $("importantLootColor"),

  showExtracts: $("showExtracts"),
  showTransits: $("showTransits"),
  showPoiNames: $("showPoiNames"),
  extractColor: $("extractColor"),
  transitColor: $("transitColor"),
  poiStatus: $("poiStatus"),

  resetSettings: $("resetSettings"),
};

function applyUiFromState(){
  setSidebarCollapsed(!!state.sidebarCollapsed, false);
}

function bindAllInputs(){
  inputs.hoverOpenSidebar.checked = !!state.hoverOpenSidebar;
  inputs.hoverOpenSidebar.onchange = () => { state.hoverOpenSidebar = !!inputs.hoverOpenSidebar.checked; saveSettings(); };

  inputs.showMap.checked = !!state.showMap;
  inputs.zoom.value = String(state.zoom);
  inputs.rotateWithLocal.checked = !!state.rotateWithLocal;
  inputs.pollMs.value = String(state.pollMs);

  inputs.freeMode.checked = !!state.freeMode;
  if(inputs.modeBadge) inputs.modeBadge.textContent = state.freeMode ? "free" : "follow";

  if(inputs.centerTargetSelect){
    inputs.centerTargetSelect.value = state.centerTarget || "local";
    inputs.centerTargetSelect.onchange = () => {
      state.centerTarget = inputs.centerTargetSelect.value || "local";
      saveSettings();
      hideTooltip();
    };
  }

  inputs.showPlayers.checked = !!state.showPlayers;
  inputs.showAim.checked = !!state.showAim;
  inputs.showNames.checked = !!state.showNames;
  inputs.showHeight.checked = !!state.showHeight;
  inputs.playerSize.value = String(state.playerSize);
  inputs.showPlayersWidget.checked = !!state.showPlayersWidget;

  inputs.showGroups.checked = !!state.showGroups;
  inputs.groupAlpha.value = String(state.groupAlpha);

  inputs.showLoot.checked = !!state.showLoot;
  inputs.showLootName.checked = !!state.showLootName;
  inputs.showLootPrice.checked = !!state.showLootPrice;

  inputs.basicPrice.value = String(state.minPrice);
  if(inputs.basicPriceText) inputs.basicPriceText.textContent = String(state.minPrice);

  inputs.importantPrice.value = String(state.importantPrice);
  if(inputs.importantPriceText) inputs.importantPriceText.textContent = String(state.importantPrice);

  if(inputs.corpseMinValue){
    inputs.corpseMinValue.value = String(state.corpseMinValue);
    if(inputs.corpseMinValueText) inputs.corpseMinValueText.textContent = String(state.corpseMinValue);
  }

  inputs.lootSize.value = String(state.lootSize);
  inputs.lootFiltersEnabled.checked = !!state.lootFiltersEnabled;
  inputs.showLootWidget.checked = !!state.showLootWidget;

  inputs.showExtracts.checked = !!state.showExtracts;
  inputs.showTransits.checked = !!state.showTransits;
  inputs.showPoiNames.checked = !!state.showPoiNames;
  inputs.extractColor.value = state.extractColor || defaults.extractColor;
  inputs.transitColor.value = state.transitColor || defaults.transitColor;

  inputs.localColor.value = state.colors.local;
  inputs.teammateColor.value = state.colors.teammate;
  inputs.usecColor.value = state.colors.usec;
  inputs.bearColor.value = state.colors.bear;
  inputs.scavColor.value = state.colors.scav;
  inputs.psavColor.value = state.colors.psav;
  inputs.raiderColor.value = state.colors.raider;
  inputs.bossColor.value = state.colors.boss;
  inputs.deadColor.value = state.colors.dead;

  inputs.basicLootColor.value = state.colors.basicLoot || defaults.colors.basicLoot;
  inputs.importantLootColor.value = state.colors.importantLoot || defaults.colors.importantLoot;

  const onBool = (key, el, after=null) => el.onchange = () => { state[key] = !!el.checked; if(after) after(); saveSettings(); };
  const onNum  = (key, el, conv=(v)=>Number(v), after=null) => el.oninput = () => { state[key] = conv(el.value); if(after) after(); saveSettings(); };

  onBool("showMap", inputs.showMap);
  onNum("zoom", inputs.zoom, (v)=>clamp(Number(v) || 1, ZOOM_MIN, ZOOM_MAX));
  onBool("rotateWithLocal", inputs.rotateWithLocal);

  onBool("showPlayers", inputs.showPlayers);
  onBool("showAim", inputs.showAim);
  onBool("showNames", inputs.showNames);
  onBool("showHeight", inputs.showHeight);
  onNum("playerSize", inputs.playerSize, (v)=>Math.max(1, Number(v)));
  onBool("showPlayersWidget", inputs.showPlayersWidget, applyWidgetsFromState);

  onBool("showGroups", inputs.showGroups);
  onNum("groupAlpha", inputs.groupAlpha, (v)=>Math.min(1, Math.max(0, Number(v))));

  onBool("showLoot", inputs.showLoot);
  onBool("showLootName", inputs.showLootName);
  onBool("showLootPrice", inputs.showLootPrice);

  inputs.basicPrice.oninput = () => {
    state.minPrice = Math.max(0, Number(inputs.basicPrice.value));
    if(inputs.basicPriceText) inputs.basicPriceText.textContent = String(state.minPrice);
    saveSettings();
  };
  inputs.importantPrice.oninput = () => {
    state.importantPrice = Math.max(0, Number(inputs.importantPrice.value));
    if(inputs.importantPriceText) inputs.importantPriceText.textContent = String(state.importantPrice);
    saveSettings();
  };

  if(inputs.corpseMinValue){
    inputs.corpseMinValue.oninput = () => {
      state.corpseMinValue = Math.max(0, Number(inputs.corpseMinValue.value));
      if(inputs.corpseMinValueText) inputs.corpseMinValueText.textContent = String(state.corpseMinValue);
      saveSettings();
      refreshPlayersWidget();
    };
  }

  onNum("lootSize", inputs.lootSize, (v)=>Math.max(1, Number(v)));

  onBool("lootFiltersEnabled", inputs.lootFiltersEnabled, () => {
    rebuildLootFilterIndex();
    updateLootFilterBadges();
  });

  onBool("showLootWidget", inputs.showLootWidget, applyWidgetsFromState);

  onBool("showExtracts", inputs.showExtracts);
  onBool("showTransits", inputs.showTransits);
  onBool("showPoiNames", inputs.showPoiNames);

  inputs.extractColor.oninput = () => { state.extractColor = inputs.extractColor.value; saveSettings(); };
  inputs.transitColor.oninput = () => { state.transitColor = inputs.transitColor.value; saveSettings(); };

  inputs.localColor.oninput    = () => { state.colors.local = inputs.localColor.value; saveSettings(); };
  inputs.teammateColor.oninput = () => { state.colors.teammate = inputs.teammateColor.value; saveSettings(); };
  inputs.usecColor.oninput     = () => { state.colors.usec = inputs.usecColor.value; saveSettings(); };
  inputs.bearColor.oninput     = () => { state.colors.bear = inputs.bearColor.value; saveSettings(); };
  inputs.scavColor.oninput     = () => { state.colors.scav = inputs.scavColor.value; saveSettings(); };
  inputs.psavColor.oninput     = () => { state.colors.psav = inputs.psavColor.value; saveSettings(); };
  inputs.raiderColor.oninput   = () => { state.colors.raider = inputs.raiderColor.value; saveSettings(); };
  inputs.bossColor.oninput     = () => { state.colors.boss = inputs.bossColor.value; saveSettings(); };
  inputs.deadColor.oninput     = () => { state.colors.dead = inputs.deadColor.value; saveSettings(); };

  inputs.basicLootColor.oninput = () => { state.colors.basicLoot = inputs.basicLootColor.value; saveSettings(); };
  inputs.importantLootColor.oninput = () => { state.colors.importantLoot = inputs.importantLootColor.value; saveSettings(); };

  inputs.freeMode.onchange = () => {
    state.freeMode = !!inputs.freeMode.checked;
    if(inputs.modeBadge) inputs.modeBadge.textContent = state.freeMode ? "free" : "follow";
    if(state.freeMode && lastLocalMap){
      freeAnchor.x = lastLocalMap.x; freeAnchor.y = lastLocalMap.y; freeAnchor.mapId = lastMapId || "";
    }
    saveSettings();
    hideTooltip();
  };

  inputs.centerOnLocal.onclick = () => {
    if(lastLocalMap){
      freeAnchor.x = lastLocalMap.x; freeAnchor.y = lastLocalMap.y; freeAnchor.mapId = lastMapId || "";
    }
    state.centerTarget = "local";
    if(inputs.centerTargetSelect) inputs.centerTargetSelect.value = "local";
    saveSettings();
    hideTooltip();
  };

  inputs.resetSettings.onclick = () => resetSettings();
}

/* pollMs restart timer */
inputs.pollMs.oninput = () => {
  state.pollMs = Math.max(20, Number(inputs.pollMs.value));
  saveSettings();
  startPolling();
};

/* =========================
   WIDGETS
========================= */
function clamp(v, a, b){ return Math.max(a, Math.min(b, v)); }

function applyWidgetsFromState(){
  lootWidget.classList.toggle("hidden", !state.showLootWidget);
  playersWidget.classList.toggle("hidden", !state.showPlayersWidget);

  lootWidget.classList.toggle("minimized", !!state.lootWidget?.minimized);
  playersWidget.classList.toggle("minimized", !!state.playersWidget?.minimized);

  const lw = state.lootWidget || defaults.lootWidget;
  const pw = state.playersWidget || defaults.playersWidget;

  const place = (el, w) => {
    const rect = el.getBoundingClientRect();
    const ww = rect.width || 320;
    const hh = rect.height || 120;
    const x = clamp(Number(w.x) || 14, 12, window.innerWidth - ww - 12);
    const y = clamp(Number(w.y) || 14, 12, window.innerHeight - hh - 12);
    el.style.left = `${x}px`;
    el.style.top  = `${y}px`;
  };

  place(lootWidget, lw);
  place(playersWidget, pw);

  if(lootWidgetSearch) lootWidgetSearch.value = String(state.lootWidgetSearch || "");
  if(playersWidgetOnlyPMCs) playersWidgetOnlyPMCs.checked = !!state.playersWidgetOnlyPMCs;

  syncMinButtons();
}

function syncMinButtons(){
  if(lootWidgetMinBtn){
    const min = !!state.lootWidget?.minimized;
    lootWidgetMinBtn.textContent = min ? "+" : "-";
    lootWidgetMinBtn.title = min ? "Restore" : "Minimize";
  }
  if(playersWidgetMinBtn){
    const min = !!state.playersWidget?.minimized;
    playersWidgetMinBtn.textContent = min ? "+" : "-";
    playersWidgetMinBtn.title = min ? "Restore" : "Minimize";
  }
}

function isInteractiveInHeader(el){
  if(!(el instanceof HTMLElement)) return false;
  return !!el.closest("button,input,select,textarea,a,[role='button'],label");
}

function initWidgetDrag(widgetEl, headerEl, stateKey){
  let drag = null;

  headerEl.addEventListener("pointerdown", (e) => {
    if(isInteractiveInHeader(e.target)) return;
    e.preventDefault();

    const r = widgetEl.getBoundingClientRect();
    drag = { ox: e.clientX - r.left, oy: e.clientY - r.top, w: r.width, h: r.height };
    headerEl.classList.add("dragging");
    headerEl.setPointerCapture(e.pointerId);
    hideTooltip();
  });

  headerEl.addEventListener("pointermove", (e) => {
    if(!drag) return;
    const w = drag.w;
    const h = drag.h;
    const x = clamp(e.clientX - drag.ox, 12, window.innerWidth  - w - 12);
    const y = clamp(e.clientY - drag.oy, 12, window.innerHeight - h - 12);
    widgetEl.style.left = `${x}px`;
    widgetEl.style.top  = `${y}px`;
  });

  const end = () => {
    if(!drag) return;
    drag = null;
    headerEl.classList.remove("dragging");

    const r = widgetEl.getBoundingClientRect();
    state[stateKey] = state[stateKey] || { x: 14, y: 14, minimized:false };
    state[stateKey].x = Math.round(r.left);
    state[stateKey].y = Math.round(r.top);
    saveSettings();
  };

  headerEl.addEventListener("pointerup", end);
  headerEl.addEventListener("pointercancel", end);
}

initWidgetDrag(lootWidget, lootWidget.querySelector(".w-header"), "lootWidget");
initWidgetDrag(playersWidget, playersWidget.querySelector(".w-header"), "playersWidget");

lootWidgetMinBtn.onclick = () => {
  state.lootWidget = state.lootWidget || deepClone(defaults.lootWidget);
  state.lootWidget.minimized = !state.lootWidget.minimized;
  applyWidgetsFromState();
  saveSettings();
};
playersWidgetMinBtn.onclick = () => {
  state.playersWidget = state.playersWidget || deepClone(defaults.playersWidget);
  state.playersWidget.minimized = !state.playersWidget.minimized;
  applyWidgetsFromState();
  saveSettings();
};

lootWidgetSearch.addEventListener("input", () => {
  state.lootWidgetSearch = lootWidgetSearch.value || "";
  saveSettings();
  refreshLootWidget();
});
playersWidgetOnlyPMCs.addEventListener("change", () => {
  state.playersWidgetOnlyPMCs = !!playersWidgetOnlyPMCs.checked;
  saveSettings();
  refreshPlayersWidget();
});

window.addEventListener("resize", () => {
  if(state.showLootWidget || state.showPlayersWidget) applyWidgetsFromState();
});

/* =========================
   LOOT FILTER WINDOW (drag+resize)
========================= */
function centerLootWindowIfNeeded(){
  const wf = state.lootFilterWindow || defaults.lootFilterWindow;
  if(wf.x != null && wf.y != null) return;

  const w = Number(wf.w) || 860;
  const h = Number(wf.h) || 640;

  const x = Math.round((window.innerWidth  - w) / 2);
  const y = Math.round((window.innerHeight - h) / 2);

  state.lootFilterWindow = { ...wf, x: clamp(x, 12, window.innerWidth - w - 12), y: clamp(y, 12, window.innerHeight - h - 12) };
}

function applyLootWindowFromState(){
  if(!lootFilterCard) return;
  const wf = state.lootFilterWindow || defaults.lootFilterWindow;

  const w = clamp(Number(wf.w) || 860, 520, window.innerWidth - 24);
  const h = clamp(Number(wf.h) || 640, 320, window.innerHeight - 24);

  if(wf.x == null || wf.y == null) centerLootWindowIfNeeded();

  const x = clamp(Number(state.lootFilterWindow.x) || 12, 12, window.innerWidth  - w - 12);
  const y = clamp(Number(state.lootFilterWindow.y) || 12, 12, window.innerHeight - h - 12);

  lootFilterCard.style.width = `${w}px`;
  lootFilterCard.style.height = `${h}px`;
  lootFilterCard.style.left = `${x}px`;
  lootFilterCard.style.top = `${y}px`;
}

let _drag = null;

lootFilterHeader.addEventListener("pointerdown", (e) => {
  if(isInteractiveInHeader(e.target)) return;
  e.preventDefault();

  applyLootWindowFromState();
  const rect = lootFilterCard.getBoundingClientRect();

  _drag = { ox: e.clientX - rect.left, oy: e.clientY - rect.top, w: rect.width, h: rect.height };
  lootFilterHeader.classList.add("dragging");
  lootFilterHeader.setPointerCapture(e.pointerId);
});

lootFilterHeader.addEventListener("pointermove", (e) => {
  if(!_drag) return;
  const w = _drag.w, h = _drag.h;
  const x = clamp(e.clientX - _drag.ox, 12, window.innerWidth  - w - 12);
  const y = clamp(e.clientY - _drag.oy, 12, window.innerHeight - h - 12);
  lootFilterCard.style.left = `${x}px`;
  lootFilterCard.style.top  = `${y}px`;
});

function endLootDrag(){
  if(!_drag) return;
  _drag = null;
  lootFilterHeader.classList.remove("dragging");

  const rect = lootFilterCard.getBoundingClientRect();
  state.lootFilterWindow = state.lootFilterWindow || deepClone(defaults.lootFilterWindow);
  state.lootFilterWindow.x = Math.round(rect.left);
  state.lootFilterWindow.y = Math.round(rect.top);
  saveSettings();

  repositionSearchPortal();
}
lootFilterHeader.addEventListener("pointerup", endLootDrag);
lootFilterHeader.addEventListener("pointercancel", endLootDrag);

let _resizeObs = null;
function ensureResizeObserver(){
  if(_resizeObs) return;
  if(!("ResizeObserver" in window)) return;

  _resizeObs = new ResizeObserver(() => {
    const rect = lootFilterCard.getBoundingClientRect();
    state.lootFilterWindow = state.lootFilterWindow || deepClone(defaults.lootFilterWindow);
    state.lootFilterWindow.w = Math.round(rect.width);
    state.lootFilterWindow.h = Math.round(rect.height);
    saveSettings();
    repositionSearchPortal();
  });
  _resizeObs.observe(lootFilterCard);
}
ensureResizeObserver();

window.addEventListener("resize", () => {
  if(lootFilterModal.classList.contains("hidden")) return;
  applyLootWindowFromState();
  repositionSearchPortal();
});

/* =========================
   DEFAULT DATA: ITEMS + POIs
========================= */
const DEFAULT_DATA_URLS = [
  "/api/default-data",
  "/DEFAULT_DATA.json",
  "/default_data.json",
  "/default-data.json"
];

/* =========================
   HTTP POLLING
========================= */
let radarData = null;
let pollTimer = null;

async function fetchRadar(){
  try{
    const res = await fetch("/api/radar", { cache: "no-store" });
    if(!res.ok) throw new Error("HTTP "+res.status);
    radarData = await res.json();

    const inRaid = !!(radarData?.inRaid ?? radarData?.inGame);
    statusEl.textContent = inRaid ? "In raid" : "Waiting for raid...";
    statusEl.className = inRaid ? "ok" : "warn";

    setPoiStatus();
    updateCenterTargetSelect();

    refreshLootWidget();
    refreshPlayersWidget();
  }catch{
    radarData = null;
    statusEl.textContent = "Disconnected";
    statusEl.className = "bad";
    setPoiStatus();
    updateCenterTargetSelect();
  }
}

function startPolling(){
  if(pollTimer) clearInterval(pollTimer);
  pollTimer = setInterval(fetchRadar, state.pollMs);
}

startPolling();
fetchRadar();

let itemDbReady = false;
let itemDb = { byId: new Map(), list: [] };

let poiDbReady = false;
let poiDb = { byMapId: new Map() };

function normStr(s){ return String(s ?? "").trim(); }

function setPoiStatus(){
  const el = inputs.poiStatus;
  if(!el) return;

  const exCount = Array.isArray(radarData?.exfils) ? radarData.exfils.length : 0;
  const exPart = `extracts: live (${exCount})`;

  const trCount = Array.isArray(radarData?.transits) ? radarData.transits.length : 0;
  const trPart = `transits: live (${trCount})`;

  el.textContent = `${exPart} | ${trPart}`;
}

function getItemDbStatusText(){
  if(itemDbReady) return `item db: loaded (${itemDb.list.length})`;
  return `item db: not loaded`;
}

function pickAny(obj, keys){
  for(const k of keys){
    const v = obj?.[k];
    if(v !== undefined && v !== null) return v;
  }
  return null;
}

function parseMapsFromDefaultData(json){
  const candidates = [
    pickAny(json, ["maps","Maps"]),
    pickAny(json, ["locations","Locations"]),
    pickAny(json, ["mapData","MapData"]),
    pickAny(json, ["worlds","Worlds"]),
  ];
  for(const c of candidates){
    if(Array.isArray(c)) return c;
  }
  if(Array.isArray(json?.data?.maps)) return json.data.maps;
  return [];
}

async function loadDefaultData(){
  itemDbReady = false;
  itemDb.byId.clear();
  itemDb.list = [];

  poiDbReady = false;
  poiDb.byMapId.clear();
  setPoiStatus();

  for(const url of DEFAULT_DATA_URLS){
    try{
      const res = await fetch(url, { cache: "force-cache" });
      if(!res.ok) throw new Error(url + " HTTP " + res.status);
      const json = await res.json();

      const items = Array.isArray(json?.items) ? json.items : (Array.isArray(json?.Items) ? json.Items : []);
      for(const it of items){
        const id = normStr(it?.bsgID ?? it?.bsgId ?? it?.BsgId ?? it?.BsgID);
        if(!id) continue;

        const rec = {
          id,
          name: normStr(it?.name ?? it?.Name),
          shortName: normStr(it?.shortName ?? it?.ShortName),
          price: Number(it?.price ?? it?.Price ?? 0) || 0,
          fleaPrice: Number(it?.fleaPrice ?? it?.FleaPrice ?? 0) || 0,
          categories: Array.isArray(it?.categories) ? it.categories : [],
        };

        itemDb.byId.set(id, rec);
        itemDb.list.push(rec);
      }
      itemDbReady = itemDb.list.length > 0;

      const mapsArr = parseMapsFromDefaultData(json);
      for(const m of mapsArr){
        const nameId = normStr(m?.nameId ?? m?.NameId ?? m?.id ?? m?.Id);
        if(!nameId) continue;

        poiDb.byMapId.set(nameId, {
          nameId,
          name: normStr(m?.name ?? m?.Name ?? nameId),
          extracts: [],
          transits: []
        });
      }
      poiDbReady = poiDb.byMapId.size > 0;
      setPoiStatus();

      break;
    }catch{}
  }

  if(lootDbStatus) lootDbStatus.textContent = getItemDbStatusText();
  if(!lootFilterModal.classList.contains("hidden")) renderLootFilterModal();
}
loadDefaultData();

/* =========================
   SEARCH
========================= */
function searchItemsTop10(query){
  const q = normStr(query).toLowerCase();
  if(!q || q.length < 1) return [];

  const starts = [];
  const contains = [];

  for(const it of itemDb.list){
    const sn = (it.shortName || "").toLowerCase();
    const nm = (it.name || "").toLowerCase();

    if(sn.startsWith(q) || nm.startsWith(q)) starts.push(it);
    else if(sn.includes(q) || nm.includes(q)) contains.push(it);
  }

  const score = (a) => {
    const sn = (a.shortName || "").toLowerCase();
    const nm = (a.name || "").toLowerCase();
    return (sn.includes(q) ? 0 : 1) * 1000 + Math.min(sn.length || 999, nm.length || 999);
  };

  starts.sort((a,b)=>score(a)-score(b));
  contains.sort((a,b)=>score(a)-score(b));

  return starts.concat(contains).slice(0, 10);
}

/* =========================
   LOOT FILTER INDEX
========================= */
let lootFilterIndex = new Map(); // bsgId -> { color, groupId }

function rebuildLootFilterIndex(){
  lootFilterIndex.clear();
  if(!Array.isArray(state.lootGroups)) return;

  for(const g of state.lootGroups){
    if(!g?.enabled) continue;
    const col = (typeof g.color === "string" && g.color) ? g.color : "#fbbf24";
    for(const it of (g.items || [])){
      if(!it?.enabled) continue;
      const id = normStr(it.id);
      if(!id) continue;
      if(!lootFilterIndex.has(id)) lootFilterIndex.set(id, { color: col, groupId: g.id });
    }
  }
}

function updateLootFilterBadges(){
  const gCount = Array.isArray(state.lootGroups) ? state.lootGroups.length : 0;
  const enabledGroups = (state.lootGroups || []).filter(g => g?.enabled).length;

  if(lootGroupsMeta) lootGroupsMeta.textContent = `${gCount} groups`;
  if(lootFilterBadge){
    lootFilterBadge.textContent = state.lootFiltersEnabled ? `${enabledGroups}/${gCount} groups` : `${gCount} groups`;
  }
}
updateLootFilterBadges();

/* =========================
   LOOT FILTER MODAL
========================= */
function openLootFilterModal(){
  if(lootDbStatus) lootDbStatus.textContent = getItemDbStatusText();
  lootFilterModal.classList.remove("hidden");
  lootFilterModal.setAttribute("aria-hidden", "false");

  applyLootWindowFromState();
  renderLootFilterModal();
}
function closeLootFilterModal(){
  closeSearchPortal();
  lootFilterModal.classList.add("hidden");
  lootFilterModal.setAttribute("aria-hidden", "true");
}

openLootFiltersBtn.onclick = openLootFilterModal;
closeLootFiltersBtn.onclick = closeLootFilterModal;
lootFilterModal.querySelector(".backdrop").onclick = closeLootFilterModal;

addLootGroupBtn.onclick = () => {
  const gid = "g_" + Math.random().toString(36).slice(2, 10);
  state.lootGroups.push({ id: gid, name: `Group ${state.lootGroups.length + 1}`, enabled: true, color: "#fbbf24", items: [] });
  rebuildLootFilterIndex();
  updateLootFilterBadges();
  saveSettings();
  renderLootFilterModal();
};

function escapeHtml(s){
  return String(s ?? "")
    .replace(/&/g,"&amp;")
    .replace(/</g,"&lt;")
    .replace(/>/g,"&gt;")
    .replace(/"/g,"&quot;")
    .replace(/'/g,"&#039;");
}

function tarkovDevImgUrl(bsgId){
  const id = normStr(bsgId);
  if(!id) return null;
  return `https://assets.tarkov.dev/${encodeURIComponent(id)}-base-image.webp`;
}

/* COPY/PASTE GROUPS */
async function copyLootGroupsToClipboard(){
  try{
    const text = JSON.stringify(state.lootGroups || [], null, 0);
    await navigator.clipboard.writeText(text);
    toastSaved("copied");
  }catch{
    const text = JSON.stringify(state.lootGroups || [], null, 2);
    prompt("Copy Loot Groups:", text);
  }
}
async function pasteLootGroupsFromClipboard(){
  try{
    const text = await navigator.clipboard.readText();
    if(!text) return;

    let parsed = null;
    try{ parsed = JSON.parse(text); }catch{ return; }

    let groups = null;
    if(Array.isArray(parsed)) groups = parsed;
    else if(Array.isArray(parsed?.lootGroups)) groups = parsed.lootGroups;

    if(!Array.isArray(groups)) return;

    // validate by merging through mergeState
    const tmp = mergeState({ ...deepClone(state), lootGroups: groups });
    state.lootGroups = tmp.lootGroups;

    rebuildLootFilterIndex();
    updateLootFilterBadges();
    saveSettings();
    renderLootFilterModal();
    refreshLootWidget();
    toastSaved("pasted");
  }catch{
    // fallback prompt
    const text = prompt("Paste Loot Groups JSON (array OR {lootGroups:[...]})");
    if(!text) return;
    try{
      const parsed = JSON.parse(text);
      let groups = null;
      if(Array.isArray(parsed)) groups = parsed;
      else if(Array.isArray(parsed?.lootGroups)) groups = parsed.lootGroups;
      if(!Array.isArray(groups)) return;

      const tmp = mergeState({ ...deepClone(state), lootGroups: groups });
      state.lootGroups = tmp.lootGroups;

      rebuildLootFilterIndex();
      updateLootFilterBadges();
      saveSettings();
      renderLootFilterModal();
      refreshLootWidget();
      toastSaved("pasted");
    }catch{}
  }
}
function toastSaved(text){
  const badge = document.getElementById("cacheBadge");
  if(!badge) return;
  badge.textContent = text;
  clearTimeout(toastSaved._t);
  toastSaved._t = setTimeout(()=> badge.textContent = "cache", 900);
}

if(copyLootGroupsBtn) copyLootGroupsBtn.onclick = copyLootGroupsToClipboard;
if(pasteLootGroupsBtn) pasteLootGroupsBtn.onclick = pasteLootGroupsFromClipboard;

/* =========================
   CENTER TARGET SELECT
========================= */
function getPlayerKey(p, idx){
  const key =
    normStr(p?.profileId ?? p?.ProfileId ?? p?.profileID ?? p?.ProfileID ??
            p?.accountId ?? p?.AccountId ?? p?.accountID ?? p?.AccountID ??
            p?.id ?? p?.Id);
  if(key) return key;
  const nm = normStr(p?.name ?? p?.Name);
  const tn = normStr(p?.typeName ?? p?.TypeName);
  return `idx:${idx}:${nm}:${tn}`;
}

function playerLabelForSelect(p){
  const nm = normStr(p?.name ?? p?.Name) || "Unknown";
  const alive = (p?.isAlive ?? p?.IsAlive);
  const friendly = !!(p?.isFriendly || p?.IsFriendly);
  const flags = [];
  if(friendly) flags.push("friendly");
  if(alive === false) flags.push("dead");
  return flags.length ? `${nm} (${flags.join(", ")})` : nm;
}

function isExtracted(p){
  const alive = (p?.isAlive ?? p?.IsAlive);
  const active = (p?.isActive ?? p?.IsActive);
  return (alive === true) && (active === false);
}

function updateCenterTargetSelect(){
  const sel = inputs.centerTargetSelect;
  if(!(sel instanceof HTMLSelectElement)) return;

  const players = Array.isArray(radarData?.players) ? radarData.players : [];
  const opts = [];
  opts.push({ v: "local", t: "Local Player" });

  for(let i=0;i<players.length;i++){
    const p = players[i];
    if(p?.isLocal || p?.IsLocal) continue;
    if(isExtracted(p)) continue;
    opts.push({ v: getPlayerKey(p, i), t: playerLabelForSelect(p) });
  }

  const seen = new Set();
  const final = [];
  for(const o of opts){
    if(seen.has(o.v)) continue;
    seen.add(o.v);
    final.push(o);
  }

  const current = (typeof state.centerTarget === "string" && state.centerTarget) ? state.centerTarget : "local";
  const html = final.map(o => `<option value="${escapeHtml(o.v)}">${escapeHtml(o.t)}</option>`).join("");

  if(sel._lastHtml !== html){
    sel.innerHTML = html;
    sel._lastHtml = html;
  }

  const exists = final.some(o => o.v === current);
  sel.value = exists ? current : "local";
  if(!exists && state.centerTarget !== "local"){
    state.centerTarget = "local";
    saveSettings();
  }
}

/* =========================
   LOOT FILTER MODAL RENDER
========================= */
function renderLootFilterModal(){
  if(!lootGroupsList) return;

  if(lootDbStatus) lootDbStatus.textContent = getItemDbStatusText();
  updateLootFilterBadges();

  const groups = Array.isArray(state.lootGroups) ? state.lootGroups : [];
  if(groups.length === 0){
    lootGroupsList.innerHTML = `<div class="mono" style="color:var(--muted);font-size:12px;padding:10px;">No groups yet. Click "Add Group".</div>`;
    return;
  }

  let html = "";
  for(const g of groups){
    const gname = escapeHtml(g.name ?? "Group");
    const gcol = escapeHtml(g.color ?? "#fbbf24");
    const gEnabled = !!g.enabled;

    const items = Array.isArray(g.items) ? g.items : [];
    const itemRows = items.map(it => {
      const enabled = !!it.enabled;
      const id = normStr(it.id);
      const rec = itemDb.byId.get(id);
      const title = escapeHtml(rec?.shortName || rec?.name || id || "item");
      const sub = escapeHtml(id || "");
      const img = tarkovDevImgUrl(id);
      return `
        <div class="itemRow">
          <input type="checkbox" data-action="toggle-item" data-gid="${escapeHtml(g.id)}" data-itemid="${escapeHtml(id)}" ${enabled ? "checked" : ""}>
          ${img ? `<img class="li-ico" src="${img}" loading="lazy" onerror="this.style.display='none'">` : `<div class="li-ico"></div>`}
          <div class="name">
            <strong>${title}</strong>
            <span class="mono">${sub}</span>
          </div>
          <button class="btn" data-action="remove-item" data-gid="${escapeHtml(g.id)}" data-itemid="${escapeHtml(id)}">Remove</button>
        </div>
      `;
    }).join("");

    html += `
      <div class="groupCard" data-gid="${escapeHtml(g.id)}">
        <div class="groupHeader">
          <input type="checkbox" data-action="toggle-group" data-gid="${escapeHtml(g.id)}" ${gEnabled ? "checked" : ""} title="Enable group">
          <input type="color" data-action="color-group" data-gid="${escapeHtml(g.id)}" value="${gcol}" title="Group color">
          <div class="grow">
            <input type="text" data-action="name-group" data-gid="${escapeHtml(g.id)}" value="${gname}" placeholder="Group name">
          </div>
          <button class="btn" data-action="delete-group" data-gid="${escapeHtml(g.id)}">Delete</button>
          <span class="mini mono">${items.length} items</span>
        </div>

        <div class="groupBody">
          <div class="searchWrap">
            <input type="text"
                   data-role="item-search"
                   data-gid="${escapeHtml(g.id)}"
                   placeholder="${itemDbReady ? "Type item name or shortName..." : "Item db not loaded (GET /api/default-data)"}"
                   ${itemDbReady ? "" : "disabled"}>
          </div>

          <div class="itemList">
            ${itemRows || `<div class="mono" style="color:var(--muted);font-size:12px;">No items in this group</div>`}
          </div>
        </div>
      </div>
    `;
  }

  lootGroupsList.innerHTML = html;
}

/* =========================
   SEARCH PORTAL
========================= */
let activeSearch = null;

function closeSearchPortal(){
  activeSearch = null;
  if(srPortal){
    srPortal.classList.remove("open");
    srPortal.innerHTML = "";
    srPortal.style.left = "0px";
    srPortal.style.top = "0px";
    srPortal.style.width = "320px";
  }
}

function repositionSearchPortal(){
  if(!activeSearch || !srPortal || !srPortal.classList.contains("open")) return;
  const inputEl = activeSearch.inputEl;
  if(!(inputEl instanceof HTMLElement)) return;
  const r = inputEl.getBoundingClientRect();

  const pad = 6;
  const maxW = Math.min(r.width, window.innerWidth - 24);
  const left = clamp(r.left, 12, window.innerWidth - maxW - 12);
  const top  = clamp(r.bottom + pad, 12, window.innerHeight - 12);

  srPortal.style.left = `${left}px`;
  srPortal.style.top = `${top}px`;
  srPortal.style.width = `${maxW}px`;
}

function openSearchPortal(gid, inputEl, matches){
  if(!srPortal) return;
  activeSearch = { gid, inputEl };

  srPortal.classList.add("open");
  srPortal.innerHTML = matches.map(it => {
    const sn = escapeHtml(it.shortName || it.name || "item");
    const nm = escapeHtml(it.name || "");
    const id = escapeHtml(it.id);
    const img = tarkovDevImgUrl(it.id);
    return `
      <div class="srRow" data-action="pick-item" data-gid="${escapeHtml(gid)}" data-itemid="${id}">
        ${img ? `<img class="srIco" src="${img}" loading="lazy" onerror="this.style.display='none'">` : `<div class="srIco"></div>`}
        <div class="srMain">
          <div class="srTop"><strong>${sn}</strong><span class="mono">${id}</span></div>
          <div class="srSub">${nm}</div>
        </div>
      </div>
    `;
  }).join("");

  repositionSearchPortal();
}

lootFilterBody.addEventListener("scroll", () => repositionSearchPortal());
window.addEventListener("scroll", repositionSearchPortal, true);

srPortal.addEventListener("click", (e) => {
  const target = e.target;
  if(!(target instanceof HTMLElement)) return;

  const actionEl = target.closest("[data-action]");
  if(!(actionEl instanceof HTMLElement)) return;

  const action = actionEl.getAttribute("data-action");
  if(action !== "pick-item") return;

  const gid = actionEl.getAttribute("data-gid") || "";
  const itemId = actionEl.getAttribute("data-itemid") || "";

  const group = findGroup(gid);
  if(group && itemId){
    ensureItemInGroup(group, itemId);
    rebuildLootFilterIndex();
    updateLootFilterBadges();
    saveSettings();
    renderLootFilterModal();

    const input = lootGroupsList.querySelector(`input[data-role="item-search"][data-gid="${CSS.escape(gid)}"]`);
    if(input instanceof HTMLInputElement) input.value = "";
    closeSearchPortal();
  }
});

function findGroup(gid){
  return (state.lootGroups || []).find(g => g.id === gid) || null;
}
function ensureItemInGroup(group, itemId){
  if(!group || !itemId) return;
  if(!Array.isArray(group.items)) group.items = [];
  if(group.items.some(x => x.id === itemId)) return;
  group.items.push({ id: itemId, enabled: true });
}

lootGroupsList.addEventListener("click", (e) => {
  const t = e.target;
  if(!(t instanceof HTMLElement)) return;

  const actionEl = t.closest("[data-action]");
  if(!(actionEl instanceof HTMLElement)) return;

  const action = actionEl.getAttribute("data-action");
  if(!action) return;

  const gid = actionEl.getAttribute("data-gid") || "";
  const group = findGroup(gid);

  if(action === "delete-group"){
    state.lootGroups = (state.lootGroups || []).filter(g => g.id !== gid);
    rebuildLootFilterIndex();
    updateLootFilterBadges();
    saveSettings();
    renderLootFilterModal();
    closeSearchPortal();
    return;
  }

  if(action === "remove-item"){
    const itemId = actionEl.getAttribute("data-itemid") || "";
    if(group){
      group.items = (group.items || []).filter(x => x.id !== itemId);
      rebuildLootFilterIndex();
      updateLootFilterBadges();
      saveSettings();
      renderLootFilterModal();
      closeSearchPortal();
    }
    return;
  }
});

lootGroupsList.addEventListener("change", (e) => {
  const t = e.target;
  if(!(t instanceof HTMLElement)) return;

  const actionEl = t.closest("[data-action]");
  if(!(actionEl instanceof HTMLElement)) return;

  const action = actionEl.getAttribute("data-action");
  if(!action) return;

  const gid = actionEl.getAttribute("data-gid") || "";
  const group = findGroup(gid);

  if(action === "toggle-group" && actionEl instanceof HTMLInputElement){
    if(group){
      group.enabled = !!actionEl.checked;
      rebuildLootFilterIndex();
      updateLootFilterBadges();
      saveSettings();
    }
    return;
  }

  if(action === "toggle-item" && actionEl instanceof HTMLInputElement){
    const itemId = actionEl.getAttribute("data-itemid") || "";
    if(group){
      const it = (group.items || []).find(x => x.id === itemId);
      if(it){
        it.enabled = !!actionEl.checked;
        rebuildLootFilterIndex();
        saveSettings();
      }
    }
    return;
  }

  if(action === "color-group" && actionEl instanceof HTMLInputElement){
    if(group){
      group.color = actionEl.value;
      rebuildLootFilterIndex();
      saveSettings();
    }
    return;
  }
});

lootGroupsList.addEventListener("input", (e) => {
  const t = e.target;
  if(!(t instanceof HTMLElement)) return;

  const actionEl = t.closest("[data-action]");
  const action = actionEl?.getAttribute("data-action");

  if(action === "name-group" && actionEl instanceof HTMLInputElement){
    const gid = actionEl.getAttribute("data-gid") || "";
    const group = findGroup(gid);
    if(group){
      group.name = actionEl.value;
      saveSettings();
      updateLootFilterBadges();
    }
    return;
  }

  const roleEl = t.closest("[data-role]");
  const role = roleEl?.getAttribute("data-role");

  if(role === "item-search" && roleEl instanceof HTMLInputElement){
    const gid = roleEl.getAttribute("data-gid") || "";
    const q = roleEl.value;

    if(!itemDbReady){ closeSearchPortal(); return; }

    const matches = searchItemsTop10(q);
    if(matches.length === 0){ closeSearchPortal(); return; }

    openSearchPortal(gid, roleEl, matches);
  }
});

document.addEventListener("click", (e) => {
  const inModal = lootFilterModal && !lootFilterModal.classList.contains("hidden");
  if(!inModal) return;

  const target = e.target;
  if(!(target instanceof HTMLElement)) return;

  const isInPortal = !!target.closest("#srPortal");
  const isInSearchInput = !!target.closest('input[data-role="item-search"]');

  if(!isInPortal && !isInSearchInput) closeSearchPortal();
});

document.addEventListener("keydown", (e) => {
  if(e.key === "Escape"){
    if(!lootFilterModal.classList.contains("hidden")) closeSearchPortal();
  }
});

/* =========================
   SVG CACHE
========================= */
const svgImgCache = new Map();
const svgMetaCache = new Map();

function ensureSvgMeta(filename){
  if(svgMetaCache.has(filename)) return;
  svgMetaCache.set(filename, { w:0, h:0, ready:false });

  fetch("/Maps/" + filename, { cache: "force-cache" })
    .then(r => r.text())
    .then(txt => {
      let w = 0, h = 0;
      const vb = /viewBox\s*=\s*["']\s*([-0-9.eE]+)\s+([-0-9.eE]+)\s+([-0-9.eE]+)\s+([-0-9.eE]+)\s*["']/i.exec(txt);
      if(vb){ w = Number(vb[3]) || 0; h = Number(vb[4]) || 0; }
      if(!(w > 0 && h > 0)){
        const mw = /width\s*=\s*["']\s*([-0-9.eE]+)\s*(?:px)?\s*["']/i.exec(txt);
        const mh = /height\s*=\s*["']\s*([-0-9.eE]+)\s*(?:px)?\s*["']/i.exec(txt);
        const ww = mw ? Number(mw[1]) : 0;
        const hh = mh ? Number(mh[1]) : 0;
        if(ww > 0 && hh > 0){ w = ww; h = hh; }
      }
      const meta = svgMetaCache.get(filename);
      if(meta){ meta.w = (w > 0 ? w : 0); meta.h = (h > 0 ? h : 0); meta.ready = true; }
    })
    .catch(() => {});
}

function getSvg(filename){
  if(svgImgCache.has(filename)) return svgImgCache.get(filename);
  ensureSvgMeta(filename);
  const img = new Image();
  img.src = "/Maps/" + filename;
  svgImgCache.set(filename, img);
  const el = document.getElementById("mapCacheInfo");
  if(el) el.textContent = String(svgImgCache.size);
  return img;
}

function getSvgDims(filename, img){
  const meta = svgMetaCache.get(filename);
  if(meta && meta.ready && meta.w > 0 && meta.h > 0) return { w: meta.w, h: meta.h };
  const nw = img?.naturalWidth || 0, nh = img?.naturalHeight || 0;
  if(nw > 0 && nh > 0) return { w: nw, h: nh };
  return null;
}

/* =========================
   MAP HELPERS
========================= */
function getMapLayers(map){
  const a = Array.isArray(map?.layers) ? map.layers : [];
  const b = Array.isArray(map?.mapLayers) ? map.mapLayers : [];
  return a.length ? a : b;
}
function hmin(l){ return l?.minHeight ?? l?.MinHeight ?? null; }
function hmax(l){ return l?.maxHeight ?? l?.MaxHeight ?? null; }

function getBaseLayer(map){
  const layers = getMapLayers(map);
  if(!layers.length) return null;
  const base = layers.find(l => l && hmin(l) == null && hmax(l) == null);
  return base || layers[0];
}

function getHeightLayer(map, localWorldY){
  const layers = getMapLayers(map);
  if(!layers.length) return null;
  if(localWorldY == null) return null;

  const candidates = layers.filter(l => {
    if(!l) return false;
    if(hmin(l) == null && hmax(l) == null) return false;
    const minOk = (hmin(l) == null) || (localWorldY >= hmin(l));
    const maxOk = (hmax(l) == null) || (localWorldY <  hmax(l));
    return minOk && maxOk;
  });

  if(!candidates.length) return null;
  candidates.sort((a,b) => (hmin(a) ?? -999999) - (hmin(b) ?? -999999));
  return candidates[candidates.length - 1];
}

function toRadMaybe(v){
  const n = Number(v);
  if(!Number.isFinite(n)) return 0;
  return (Math.abs(n) > (Math.PI * 2 + 0.25)) ? (n * Math.PI / 180) : n;
}

function rotatePoint(px, py, rad){
  const c = Math.cos(rad), s = Math.sin(rad);
  return { x: px*c - py*s, y: px*s + py*c };
}

function getViewportCenter(){
  const sbOpen = isSidebarOpen();
  const insetRight = sbOpen ? sidebar.getBoundingClientRect().width : 0;
  return { cx: (cw - insetRight) / 2, cy: ch / 2 };
}

function worldToMapUnzoomed(worldX, worldZ, map){
  const ox = (map.x ?? map.X ?? 0);
  const oy = (map.y ?? map.Y ?? 0);
  const sc = (map.scale ?? map.Scale ?? 1);
  const svgScale = (map.svgScale ?? map.SvgScale ?? 1);

  return {
    x: (ox * svgScale) + (worldX * (sc * svgScale)),
    y: (oy * svgScale) - (worldZ * (sc * svgScale))
  };
}

function readPlayerMapXY(p, map){
  const mx1 = p?.mapX ?? p?.mapx ?? p?.MapX ?? p?.mapPos?.x ?? p?.mapPos?.X ?? p?.MapPos?.X;
  const my1 = p?.mapY ?? p?.mapy ?? p?.MapY ?? p?.mapPos?.y ?? p?.mapPos?.Y ?? p?.MapPos?.Y;
  if(Number.isFinite(Number(mx1)) && Number.isFinite(Number(my1))) return { x: Number(mx1), y: Number(my1) };

  const mx2 = p?.X ?? p?.x;
  const my2 = p?.Y ?? p?.y;
  if(Number.isFinite(Number(mx2)) && Number.isFinite(Number(my2))) return { x: Number(mx2), y: Number(my2) };

  const wx = p?.worldX ?? p?.wx ?? p?.WorldX ?? p?.position?.x ?? p?.position?.X ?? p?.pos?.x ?? p?.pos?.X;
  const wz = p?.worldZ ?? p?.wz ?? p?.WorldZ ?? p?.position?.z ?? p?.position?.Z ?? p?.pos?.z ?? p?.pos?.Z;

  if(Number.isFinite(Number(wx)) && Number.isFinite(Number(wz)) && map){
    return worldToMapUnzoomed(Number(wx), Number(wz), map);
  }
  return { x: 0, y: 0 };
}

function readLootMapXY(l, map){
  const mx2 = l?.X ?? l?.x;
  const my2 = l?.Y ?? l?.y;
  if(Number.isFinite(Number(mx2)) && Number.isFinite(Number(my2))) return { x: Number(mx2), y: Number(my2) };

  const mx1 = l?.mapX ?? l?.mapx ?? l?.MapX ?? l?.mapPos?.x ?? l?.mapPos?.X;
  const my1 = l?.mapY ?? l?.mapy ?? l?.MapY ?? l?.mapPos?.y ?? l?.mapPos?.Y;
  if(Number.isFinite(Number(mx1)) && Number.isFinite(Number(my1))) return { x: Number(mx1), y: Number(my1) };

  const wx = l?.worldX ?? l?.WorldX ?? l?.position?.x ?? l?.position?.X ?? l?.pos?.x ?? l?.pos?.X;
  const wz = l?.worldZ ?? l?.WorldZ ?? l?.position?.z ?? l?.position?.Z ?? l?.pos?.z ?? l?.pos?.Z;

  if(Number.isFinite(Number(wx)) && Number.isFinite(Number(wz)) && map){
    return worldToMapUnzoomed(Number(wx), Number(wz), map);
  }
  return { x: 0, y: 0 };
}

function readWorldY(e){
  const wy = e?.worldY ?? e?.WorldY ?? e?.position?.y ?? e?.position?.Y ?? e?.pos?.y ?? e?.pos?.Y ?? e?.height ?? e?.Height;
  return Number.isFinite(Number(wy)) ? Number(wy) : null;
}

function drawSvgLayerAnchored(filename, map, cx, cy, zoom, rotateRad, anchorMap, alpha=1){
  if(!filename) return false;
  const img = getSvg(filename);
  if(!img.complete) return false;

  const dims = getSvgDims(filename, img);
  if(!dims) return false;

  const svgScale = (map.svgScale ?? map.SvgScale ?? 1);
  const w = dims.w * svgScale * zoom;
  const h = dims.h * svgScale * zoom;

  ctx.save();
  ctx.globalAlpha = alpha;

  ctx.translate(cx, cy);
  if(state.rotateWithLocal) ctx.rotate(-rotateRad);

  const ax = (anchorMap?.x ?? 0) * zoom;
  const ay = (anchorMap?.y ?? 0) * zoom;
  ctx.translate(-ax, -ay);

  ctx.drawImage(img, 0, 0, w, h);
  ctx.restore();
  return true;
}

function getMapScreenRectAnchored(map, cx, cy, zoom, anchorMap){
  const base = getBaseLayer(map);
  if(!base) return null;
  const bFile = base.filename || base.Filename;
  if(!bFile) return null;

  const img = getSvg(bFile);
  if(!img.complete) return null;

  const dims = getSvgDims(bFile, img);
  if(!dims) return null;

  const svgScale = (map.svgScale ?? map.SvgScale ?? 1);
  const w = dims.w * svgScale * zoom;
  const h = dims.h * svgScale * zoom;

  const ax = (anchorMap?.x ?? 0) * zoom;
  const ay = (anchorMap?.y ?? 0) * zoom;

  const left = cx - ax;
  const top  = cy - ay;

  return { left, top, w, h };
}

function drawMap(map, localWorldY, cx, cy, zoom, rotateRad, anchorMap){
  const base = getBaseLayer(map);
  if(!base) return false;

  const disableDimming = !!(map.disableDimming ?? map.DisableDimming);
  const overlay = (!disableDimming) ? getHeightLayer(map, localWorldY) : null;

  let baseAlpha = 1;
  if(!disableDimming && overlay){
    if(overlay.dimBaseLayer === true || overlay.DimBaseLayer === true){
      baseAlpha = 0.55;
    }
  }

  const bFile = base.filename || base.Filename;
  const ok = drawSvgLayerAnchored(bFile, map, cx, cy, zoom, rotateRad, anchorMap, baseAlpha);
  if(!ok) return false;

  if(overlay){
    const oFile = overlay.filename || overlay.Filename;
    if(oFile && oFile !== bFile) drawSvgLayerAnchored(oFile, map, cx, cy, zoom, rotateRad, anchorMap, 1);
  }
  return true;
}

function mapXYToScreen(mx, my, mapRect, cx, cy, rotRad){
  let px = mapRect.left + mx * state.zoom;
  let py = mapRect.top  + my * state.zoom;

  if(state.rotateWithLocal){
    const v = rotatePoint(px - cx, py - cy, -rotRad);
    px = cx + v.x;
    py = cy + v.y;
  }
  return { px, py };
}

// Inverse: screen -> map coords (used for pinch)
function screenToMap(sx, sy, cx, cy, anchorMap, zoom, rotRad){
  let dx = sx - cx;
  let dy = sy - cy;

  // undo rotation (screen is rotated by -rot, so we rotate vector by +rot)
  if(state.rotateWithLocal){
    const v = rotatePoint(dx, dy, rotRad);
    dx = v.x; dy = v.y;
  }

  return {
    x: (anchorMap?.x ?? 0) + (dx / zoom),
    y: (anchorMap?.y ?? 0) + (dy / zoom)
  };
}

/* =========================
   COLORS + TOOLTIP
========================= */
function playerColor(p){
  if(p?.isAlive === false || p?.IsAlive === false) return state.colors.dead;
  if(p?.isLocal || p?.IsLocal) return state.colors.local;
  if(p?.isFriendly || p?.IsFriendly) return state.colors.teammate;

  const t = p?.typeName ?? p?.TypeName ?? "";
  switch (t){
    case "Player":     return state.colors.usec;
    case "PlayerScav": return state.colors.psav;
    case "Bot":        return state.colors.scav;
    case "Raider":     return state.colors.raider;
    case "Boss":       return state.colors.boss;
    default:           return "#ffffff";
  }
}

function pick(obj, keys){
  for(const k of keys){
    const v = obj?.[k];
    if(v !== undefined && v !== null && v !== "") return v;
  }
  return null;
}
function fmtMoney(n){
  const v = Number(n);
  if(!Number.isFinite(v)) return null;
  return v.toLocaleString("en-US");
}

function showTooltip(html, vx, vy){
  if(!tooltipEl) return;
  tooltipEl.innerHTML = html;
  tooltipEl.classList.remove("hidden");

  const pad = 14;
  const margin = 10;

  tooltipEl.style.left = "0px";
  tooltipEl.style.top = "0px";

  const rect = tooltipEl.getBoundingClientRect();
  const w = window.innerWidth;
  const h = window.innerHeight;

  let left = vx + pad;
  let top  = vy + pad;

  if(left + rect.width + margin > w) left = Math.max(margin, w - rect.width - margin);
  if(top  + rect.height + margin > h) top  = Math.max(margin, h - rect.height - margin);

  tooltipEl.style.left = `${left}px`;
  tooltipEl.style.top  = `${top}px`;
}
function hideTooltip(){
  if(!tooltipEl) return;
  tooltipEl.classList.add("hidden");
}

let hitList = [];
let lastLocalPlayer = null;
let lastMouse = { inside:false, cx:0, cy:0, vx:0, vy:0 };

function pickHoverTarget(mx, my){
  let best = null;
  let bestScore = Infinity;

  for(const h of hitList){
    const dx = mx - h.px;
    const dy = my - h.py;
    const d2 = dx*dx + dy*dy;

    const r = (h.r ?? 10) + 4;
    if(d2 > r*r) continue;

    const prio =
      (h.kind === "player") ? 0 :
      (h.kind === "poi") ? 1 :
      2;

    const score = prio * 1e12 + d2;
    if(score < bestScore){
      bestScore = score;
      best = h;
    }
  }
  return best;
}

function kvRow(k, v, mono=false){
  if(v === null || v === undefined || v === "") return "";
  return `<div class="k">${escapeHtml(k)}</div><div class="v ${mono ? "mono" : ""}">${escapeHtml(v)}</div>`;
}

function tryWorldXZ(e){
  const wx = pick(e, ["worldX","WorldX","wx","WX"]);
  const wz = pick(e, ["worldZ","WorldZ","wz","WZ"]);
  if(Number.isFinite(Number(wx)) && Number.isFinite(Number(wz))) return { x:Number(wx), z:Number(wz) };
  const p = e?.position || e?.Position || e?.pos || e?.Pos;
  const x = p?.x ?? p?.X;
  const z = p?.z ?? p?.Z;
  if(Number.isFinite(Number(x)) && Number.isFinite(Number(z))) return { x:Number(x), z:Number(z) };
  return null;
}
function distanceMeters(a, b){
  const aa = tryWorldXZ(a);
  const bb = tryWorldXZ(b);
  if(!aa || !bb) return null;
  const dx = aa.x - bb.x;
  const dz = aa.z - bb.z;
  return Math.sqrt(dx*dx + dz*dz);
}

function buildPlayerTooltip(p){
  const name = pick(p, ["name","Name"]) || "Unknown";
  const type = pick(p, ["typeName","TypeName","type","Type"]) || "Player";

  const alive = (p?.isAlive ?? p?.IsAlive);
  const isLocal = !!(p?.isLocal || p?.IsLocal);
  const friendly = !!(p?.isFriendly || p?.IsFriendly);

  const hp = pick(p, ["health","Health","hp","Hp"]);
  const lvl = pick(p, ["level","Level"]);
  const dist = lastLocalPlayer ? distanceMeters(p, lastLocalPlayer) : null;

  const gearValue = pick(p, ["gearValue","GearValue","value","Value","gearPrice","GearPrice","totalValue","TotalValue"]);
  const weapon = pick(p, ["weapon","Weapon","weaponName","WeaponName","primary","Primary"]);
  const armor  = pick(p, ["armor","Armor","armorName","ArmorName","armorClass","ArmorClass"]);
  const helmet = pick(p, ["helmet","Helmet","helmetName","HelmetName"]);

  const dot = playerColor(p);
  const flags = [isLocal ? "local" : null, friendly ? "friendly" : null, isExtracted(p) ? "extracted" : null, (alive === false) ? "dead" : "alive"].filter(Boolean).join(" | ");

  return `
    <div class="t-title">
      <span class="t-dot" style="background:${escapeHtml(dot)}"></span>
      <span>${escapeHtml(name)}</span>
    </div>
    <div class="t-sub mono">${escapeHtml(type)}${flags ? " | " + escapeHtml(flags) : ""}</div>
    <div class="t-grid">
      ${kvRow("Level", lvl, true)}
      ${kvRow("HP", hp, true)}
      ${kvRow("Distance", dist != null ? `${dist.toFixed(1)} m` : null, true)}
      ${kvRow("Gear", gearValue != null ? `$${fmtMoney(gearValue)}` : null, true)}
      ${kvRow("Weapon", weapon)}
      ${kvRow("Armor", armor)}
      ${kvRow("Helmet", helmet)}
    </div>
  `;
}

/* Loot helpers */
function collectLootIds(o){
  const out = [];
  const push = (v) => { const s = normStr(v); if(s && !out.includes(s)) out.push(s); };
  if(!o || typeof o !== "object") return out;

  push(pick(o, ["bsgId","BsgId","bsgID","BsgID","bsgid"]));
  push(pick(o, ["templateId","TemplateId","tpl","Tpl","itemTpl","ItemTpl","itemTemplateId","ItemTemplateId"]));
  push(pick(o, ["itemId","ItemId"]));
  push(pick(o, ["id","Id"]));

  return out;
}
function getLootBsgId(l){
  const inner = l?.item || l?.Item || l?.data || l?.Data || l?.template || l?.Template || l?.itemData || l?.ItemData || null;
  const cands = [...collectLootIds(l), ...collectLootIds(inner)];

  for(const s of cands) if(lootFilterIndex.has(s)) return s;
  for(const s of cands) if(itemDb?.byId?.has?.(s)) return s;

  return cands[0] || "";
}
function getLootNameFromPayloadOrDb(l){
  const sn = pick(l, ["shortName","ShortName","name","Name","itemName","ItemName"]);
  if(sn) return normStr(sn);

  const id = getLootBsgId(l);
  const rec = itemDb.byId.get(id);
  return normStr(rec?.shortName || rec?.name || "");
}
function getLootPriceFromPayloadOrDb(l){
  const p = pick(l, ["price","Price","value","Value","fleaPrice","FleaPrice"]);
  const pn = Number(p);
  if(Number.isFinite(pn) && pn > 0) return pn;

  const id = getLootBsgId(l);
  const rec = itemDb.byId.get(id);
  const fp = Number(rec?.fleaPrice || 0);
  if(Number.isFinite(fp) && fp > 0) return fp;
  const bp = Number(rec?.price || 0);
  if(Number.isFinite(bp) && bp > 0) return bp;

  return 0;
}
function getLootGroupInfo(l){
  const id = getLootBsgId(l);
  if(!id) return null;
  return lootFilterIndex.get(id) || null;
}
function lootDefaultColor(price){
  const imp = Number(state.importantPrice) || 0;
  if(price >= imp && imp > 0) return state.colors.importantLoot || "#ef4444";
  return state.colors.basicLoot || "#fbbf24";
}
function buildLootTooltip(l){
  const name = getLootNameFromPayloadOrDb(l) || "Loot";
  const price = getLootPriceFromPayloadOrDb(l);
  const count = pick(l, ["count","Count","qty","Qty","stack","Stack"]);
  const category = pick(l, ["category","Category","tag","Tag","type","Type"]);

  const bsgId = getLootBsgId(l);
  const imgUrl = bsgId ? `https://assets.tarkov.dev/${encodeURIComponent(String(bsgId))}-base-image.webp` : null;

  const gi = getLootGroupInfo(l);
  const dotCol = gi?.color || lootDefaultColor(price);

  const head = `
    <div class="t-loothead">
      ${imgUrl ? `<img class="t-img" src="${imgUrl}" loading="lazy" onerror="this.style.display='none'">` : `<div class="t-img"></div>`}
      <div class="t-lootcol">
        <div class="t-title" style="margin:0;">
          <span class="t-dot" style="background:${escapeHtml(dotCol)}"></span>
          <span>${escapeHtml(name)}</span>
        </div>
        <div class="t-sub mono" style="margin:0;">loot${bsgId ? " | " + escapeHtml(String(bsgId)) : ""}</div>
      </div>
    </div>
  `;

  return `
    ${head}
    <div class="t-sep"></div>
    <div class="t-grid">
      ${kvRow("Price", price ? `$${fmtMoney(price)}` : null, true)}
      ${kvRow("Count", count, true)}
      ${kvRow("Category", category)}
    </div>
  `;
}

function buildPoiTooltip(p){
  const title = p?.label || "POI";
  const type = p?.poiType || "poi";
  const faction = p?.faction || null;
  return `
    <div class="t-title">
      <span class="t-dot" style="background:${escapeHtml(p?.color || "#a78bfa")}"></span>
      <span>${escapeHtml(title)}</span>
    </div>
    <div class="t-sub mono">${escapeHtml(type)}${faction ? " | " + escapeHtml(faction) : ""}</div>
  `;
}

function updateHover(){
  if(dragging || !lastMouse.inside){
    hideTooltip();
    return;
  }

  const h = pickHoverTarget(lastMouse.cx, lastMouse.cy);
  if(!h){ hideTooltip(); return; }

  const html =
    (h.kind === "player") ? buildPlayerTooltip(h.data) :
    (h.kind === "loot") ? buildLootTooltip(h.data) :
    buildPoiTooltip(h.data);

  showTooltip(html, lastMouse.vx, lastMouse.vy);
}
canvas.addEventListener("pointermove", (e) => {
  const rect = canvas.getBoundingClientRect();
  lastMouse.cx = e.clientX - rect.left;
  lastMouse.cy = e.clientY - rect.top;
  lastMouse.vx = e.clientX;
  lastMouse.vy = e.clientY;
  lastMouse.inside = true;
  if(!dragging) updateHover();
});
canvas.addEventListener("pointerleave", () => {
  lastMouse.inside = false;
  hideTooltip();
});

/* =========================
   PING
========================= */
let ping = null;
function setPing(mx, my, label, color){
  if(!Number.isFinite(Number(mx)) || !Number.isFinite(Number(my))) return;
  ping = { mx:Number(mx), my:Number(my), label:String(label||""), color:String(color||"#fbbf24"), expiresAt: Date.now() + 3000 };
}
function drawPing(mapRect, cx, cy, rotRad){
  if(!ping) return;
  if(Date.now() > ping.expiresAt){ ping = null; return; }
  const s = mapXYToScreen(ping.mx, ping.my, mapRect, cx, cy, rotRad);
  const t = 1 - ((ping.expiresAt - Date.now()) / 3000);
  const r = 12 + t * 26;
  const a = 0.95 * (1 - t);

  ctx.save();
  ctx.globalAlpha = a;
  ctx.strokeStyle = ping.color;
  ctx.lineWidth = 3;
  ctx.beginPath();
  ctx.arc(s.px, s.py, r, 0, Math.PI*2);
  ctx.stroke();

  ctx.globalAlpha = a * 0.6;
  ctx.lineWidth = 1.5;
  ctx.beginPath();
  ctx.arc(s.px, s.py, r * 0.6, 0, Math.PI*2);
  ctx.stroke();

  if(ping.label){
    ctx.globalAlpha = 1;
    ctx.font = "12px system-ui";
    ctx.fillStyle = "#e5e7eb";
    ctx.textAlign = "center";
    ctx.textBaseline = "bottom";
    ctx.shadowColor = "rgba(0,0,0,.75)";
    ctx.shadowBlur = 8;
    ctx.fillText(ping.label, s.px, s.py - (r + 6));
  }
  ctx.restore();
}

/* =========================
   DRAW
========================= */
function drawHeightArrow(px, py, above){
  ctx.font = "12px system-ui";
  ctx.textAlign = "center";
  ctx.textBaseline = "middle";
  ctx.fillText(above ? "^" : "v", px, py);
}
function drawName(px, py, name){
  ctx.font = "12px system-ui";
  ctx.textAlign = "center";
  ctx.textBaseline = "top";
  ctx.fillText(name, px, py + 10);
}

function drawGroupConnectors(players, map, cx, cy, rotRad, mapRect){
  const groups = new Map();
  for(const p of players){
    if(isExtracted(p)) continue;
    if(p?.isAlive === false || p?.IsAlive === false) continue;

    const gid = p?.groupId ?? p?.GroupId ?? -1;
    if(typeof gid === "number" && gid >= 0){
      if(!groups.has(gid)) groups.set(gid, []);
      groups.get(gid).push(p);
    }
  }
  if(groups.size === 0) return;

  ctx.save();
  ctx.globalAlpha = state.groupAlpha;
  ctx.strokeStyle = "#9ca3af";
  ctx.lineWidth = 1;

  for(const g of groups.values()){
    if(g.length < 2) continue;

    const leader = g[0];
    const lm = readPlayerMapXY(leader, map);
    const ls = mapXYToScreen(lm.x, lm.y, mapRect, cx, cy, rotRad);

    for(let i=1;i<g.length;i++){
      const pm = readPlayerMapXY(g[i], map);
      const ps = mapXYToScreen(pm.x, pm.y, mapRect, cx, cy, rotRad);
      ctx.beginPath();
      ctx.moveTo(ls.px, ls.py);
      ctx.lineTo(ps.px, ps.py);
      ctx.stroke();
    }
  }
  ctx.restore();
}

function drawPlayerMarker(px, py, r, color, ang, isDead){
  ctx.save();
  ctx.strokeStyle = color;
  ctx.fillStyle = color;

  const lw = Math.max(2, r * 0.45);
  ctx.lineWidth = lw;
  ctx.lineCap = "round";

  if(isDead){
    const d = r * 1.00;
    ctx.beginPath();
    ctx.moveTo(px - d, py - d);
    ctx.lineTo(px + d, py + d);
    ctx.moveTo(px + d, py - d);
    ctx.lineTo(px - d, py + d);
    ctx.stroke();
    ctx.restore();
    return;
  }

  const gap = Math.PI / 3;
  const start = ang + gap * 0.5;
  const end   = ang + (Math.PI * 2) - gap * 0.5;

  ctx.beginPath();
  ctx.arc(px, py, r, start, end, false);
  ctx.stroke();

  ctx.restore();
}

function drawPlayers(players, map, cx, cy, rotRad, mapRect, localWorldY, hits){
  const size = Number(state.playerSize) || 6;
  const haveHeights = (localWorldY != null);
  const corpseMin = Number(state.corpseMinValue) || 0;

  for(let i=0;i<players.length;i++){
    const p = players[i];
    if(isExtracted(p)) continue;

    const isDead = (p?.isAlive === false || p?.IsAlive === false);

    if(isDead && corpseMin > 0){
      const v = Number(p?.value ?? p?.Value ?? p?.gearValue ?? p?.GearValue ?? 0) || 0;
      if(v < corpseMin) continue;
    }

    const pm = readPlayerMapXY(p, map);
    const s = mapXYToScreen(pm.x, pm.y, mapRect, cx, cy, rotRad);
    const px = s.px, py = s.py;

    hits.push({ kind:"player", px, py, r: Math.max(10, size + 8), data: p });

    const col = playerColor(p);

    const yaw = toRadMaybe(p?.yaw ?? p?.Yaw ?? 0);
    const ang = state.rotateWithLocal ? (yaw - rotRad) : yaw;

    drawPlayerMarker(px, py, size, col, ang, isDead);

    if(state.showAim && !isDead){
      const len = 20;
      ctx.save();
      ctx.strokeStyle = col;
      ctx.lineWidth = 2;
      ctx.lineCap = "round";
      ctx.beginPath();
      ctx.moveTo(px + Math.cos(ang) * (size * 0.15), py + Math.sin(ang) * (size * 0.15));
      ctx.lineTo(px + Math.cos(ang) * len,           py + Math.sin(ang) * len);
      ctx.stroke();
      ctx.restore();
    }

    if(state.showHeight && haveHeights){
      const pyWorld = readWorldY(p);
      if(pyWorld != null){
        const dy = pyWorld - localWorldY;
        if(dy > 1.0) drawHeightArrow(px, py - (size + 10), true);
        else if(dy < -1.0) drawHeightArrow(px, py + (size + 10), false);
      }
    }

    if(state.showNames){
      ctx.fillStyle = "#e5e7eb";
      const nm = p?.name ?? p?.Name ?? "";
      drawName(px, py, nm);
    }
  }
}

function buildLootLabel(l){
  const showName = !!state.showLootName;
  const showPrice = !!state.showLootPrice;
  if(!showName && !showPrice) return null;

  const nm = showName ? (getLootNameFromPayloadOrDb(l) || "Loot") : null;
  const pr = showPrice ? getLootPriceFromPayloadOrDb(l) : 0;

  if(showName && showPrice) return `${nm}($${fmtMoney(pr || 0)})`;
  if(showName) return nm;
  if(showPrice) return `$${fmtMoney(pr || 0)}`;
  return null;
}

function drawLoot(loot, map, cx, cy, rotRad, mapRect, hits){
  const size = Number(state.lootSize) || 3;
  const minPrice = Number(state.minPrice) || 0;

  const useFilters = !!state.lootFiltersEnabled;

  const doLabel = !!state.showLootName || !!state.showLootPrice;
  if(doLabel){
    ctx.save();
    ctx.font = "11px system-ui";
    ctx.textAlign = "left";
    ctx.textBaseline = "middle";
    ctx.shadowColor = "rgba(0,0,0,.85)";
    ctx.shadowBlur = 6;
  }

  for(const l of loot){
    const price = getLootPriceFromPayloadOrDb(l);

    let gi = null;
    if (useFilters) {
      gi = getLootGroupInfo(l);
      if (!gi) continue;
    } else {
      gi = getLootGroupInfo(l);
    }

    if (!gi && price < minPrice) continue;

    const lm = readLootMapXY(l, map);
    if(!Number.isFinite(lm.x) || !Number.isFinite(lm.y)) continue;

    const s = mapXYToScreen(lm.x, lm.y, mapRect, cx, cy, rotRad);
    const px = s.px, py = s.py;

    hits.push({ kind:"loot", px, py, r: Math.max(10, size * 3), data: l });

    const col = gi?.color || lootDefaultColor(price);
    ctx.fillStyle = col;
    ctx.fillRect(px - size/2, py - size/2, size, size);

    if(doLabel){
      const label = buildLootLabel(l);
      if(label){
        ctx.fillStyle = "#e5e7eb";
        ctx.fillText(label, px + size + 4, py);
      }
    }
  }

  if(doLabel) ctx.restore();
}

function drawPois(map, cx, cy, rotRad, mapRect, hits){
  /* EXTRACTS (live map-space) */
  if(state.showExtracts){
    const exfils = Array.isArray(radarData?.exfils) ? radarData.exfils : [];
    for(const ex of exfils){
      if(!Number.isFinite(ex?.x) || !Number.isFinite(ex?.y)) continue;

      const s = mapXYToScreen(ex.x, ex.y, mapRect, cx, cy, rotRad);
      const px = s.px, py = s.py;

      const col = state.extractColor || "#34d399";
      const alpha = ex.isAvailableForPlayer ? 1.0 : 0.45;

      ctx.save();
      ctx.globalAlpha = alpha;
      ctx.fillStyle = col;
      ctx.beginPath();
      ctx.arc(px, py, 5, 0, Math.PI * 2);
      ctx.fill();
      ctx.restore();

      hits.push({
        kind: "poi",
        px, py,
        r: 14,
        data: {
          poiType: "extract",
          label: ex.name,
          color: col
        }
      });

      if(state.showPoiNames){
        ctx.fillStyle = "#e5e7eb";
        ctx.font = "11px system-ui";
        ctx.fillText(ex.name, px + 8, py);
      }
    }
  }

  /* TRANSITS (live map-space) */
  if(state.showTransits){
    const transits = Array.isArray(radarData?.transits) ? radarData.transits : [];
    const col = state.transitColor || "#a78bfa";

    for(const tr of transits){
      if(!Number.isFinite(tr?.x) || !Number.isFinite(tr?.y)) continue;

      const s = mapXYToScreen(tr.x, tr.y, mapRect, cx, cy, rotRad);
      const px = s.px, py = s.py;

      ctx.save();
      ctx.fillStyle = col;
      ctx.beginPath();
      ctx.moveTo(px, py - 6);
      ctx.lineTo(px + 6, py);
      ctx.lineTo(px, py + 6);
      ctx.lineTo(px - 6, py);
      ctx.closePath();
      ctx.fill();
      ctx.restore();

      hits.push({
        kind: "poi",
        px, py,
        r: 16,
        data: {
          poiType: "transit",
          label: tr.name || "Transit",
          color: col
        }
      });

      if(state.showPoiNames){
        ctx.fillStyle = "#e5e7eb";
        ctx.font = "11px system-ui";
        ctx.fillText(tr.name || "Transit", px + 10, py);
      }
    }
  }
}

/* =========================
   TOUCH: pinch-to-zoom + pan
   (IMPORTANT: set up ONCE, not inside bindAllInputs)
========================= */
let touchPan = null; // { id, lastX, lastY }
let pinch = null;    // { id1,id2,startDist,startZoom,before }

function dist2(a, b){
  const dx = a.x - b.x;
  const dy = a.y - b.y;
  return Math.sqrt(dx*dx + dy*dy);
}
function mid(a, b){
  return { x: (a.x + b.x)/2, y: (a.y + b.y)/2 };
}
function getCanvasLocalXY(clientX, clientY){
  const r = canvas.getBoundingClientRect();
  return { x: clientX - r.left, y: clientY - r.top };
}
const activePtrs = new Map(); // pointerId -> {x,y,clientX,clientY}

canvas.addEventListener("pointerdown", (e) => {
  if(e.pointerType === "mouse") return;

  canvas.setPointerCapture(e.pointerId);
  activePtrs.set(e.pointerId, { clientX: e.clientX, clientY: e.clientY, ...getCanvasLocalXY(e.clientX, e.clientY) });
  hideTooltip();

  if(activePtrs.size === 2){
    const ids = [...activePtrs.keys()];
    const p1 = activePtrs.get(ids[0]);
    const p2 = activePtrs.get(ids[1]);

    const m = mid(p1, p2);
    const startDist = dist2(p1, p2);

    const { cx, cy } = getViewportCenter();
    const rot = lastRotRad;

    // use current anchor (freeAnchor if free, otherwise current follow anchor)
    const anchor = state.freeMode
      ? freeAnchor
      : (followAnchorMap || { x:0, y:0 });

    pinch = {
      id1: ids[0],
      id2: ids[1],
      startDist: Math.max(1, startDist),
      startZoom: state.zoom,
      before: screenToMap(m.x, m.y, cx, cy, anchor, state.zoom, rot),
    };

    touchPan = null;
    return;
  }

  if(activePtrs.size === 1 && state.freeMode){
    touchPan = { id: e.pointerId, lastX: e.clientX, lastY: e.clientY };
  }
});

canvas.addEventListener("pointermove", (e) => {
  if(e.pointerType === "mouse") return;
  if(!activePtrs.has(e.pointerId)) return;

  activePtrs.set(e.pointerId, { clientX: e.clientX, clientY: e.clientY, ...getCanvasLocalXY(e.clientX, e.clientY) });

  // pinch update
  if(pinch && activePtrs.size >= 2){
    const p1 = activePtrs.get(pinch.id1);
    const p2 = activePtrs.get(pinch.id2);
    if(!p1 || !p2) return;

    const d = dist2(p1, p2);
    const ratio = d / pinch.startDist;

    const nz = clamp(pinch.startZoom * ratio, ZOOM_MIN, ZOOM_MAX);

    const m = mid(p1, p2);
    const { cx, cy } = getViewportCenter();
    const rot = lastRotRad;

    const anchor = state.freeMode
      ? freeAnchor
      : (followAnchorMap || { x:0, y:0 });

    state.zoom = nz;
    if(inputs.zoom) inputs.zoom.value = String(state.zoom);

    // keep point under fingers stable by adjusting anchor
    let dx = m.x - cx;
    let dy = m.y - cy;
    if(state.rotateWithLocal){
      const v = rotatePoint(dx, dy, rot);
      dx = v.x; dy = v.y;
    }

    anchor.x = pinch.before.x - (dx / nz);
    anchor.y = pinch.before.y - (dy / nz);

    if(state.freeMode){
      freeAnchor.x = anchor.x;
      freeAnchor.y = anchor.y;
      freeAnchor.mapId = lastMapId || freeAnchor.mapId || "";
    }else{
      // follow mode: just update temporary follow anchor used in render
      followAnchorMap = { x: anchor.x, y: anchor.y };
    }

    saveSettings();
    return;
  }

  // 1-finger pan in freeMode
  if(touchPan && state.freeMode && touchPan.id === e.pointerId){
    const dx = e.clientX - touchPan.lastX;
    const dy = e.clientY - touchPan.lastY;
    touchPan.lastX = e.clientX;
    touchPan.lastY = e.clientY;

    // pan direction should respect rotation
    const v = (state.rotateWithLocal ? rotatePoint(dx, dy, lastRotRad) : { x: dx, y: dy });
    freeAnchor.x -= (v.x / state.zoom);
    freeAnchor.y -= (v.y / state.zoom);
    saveSettings();
  }
});

function endPointer(e){
  if(e.pointerType === "mouse") return;
  activePtrs.delete(e.pointerId);

  if(pinch && (e.pointerId === pinch.id1 || e.pointerId === pinch.id2)){
    pinch = null;
  }
  if(touchPan && e.pointerId === touchPan.id){
    touchPan = null;
  }
}
canvas.addEventListener("pointerup", endPointer);
canvas.addEventListener("pointercancel", endPointer);
canvas.addEventListener("pointerleave", endPointer);

/* =========================
   MOUSE PAN (freeMode)
========================= */
let dragging = false;
let dragStart = null;

canvas.addEventListener("pointerdown", (e) => {
  if(e.pointerType !== "mouse") return;
  if(!state.freeMode) return;

  dragging = true;
  dragStart = { x: e.clientX, y: e.clientY };
  canvas.setPointerCapture(e.pointerId);
  hideTooltip();
});

canvas.addEventListener("pointermove", (e) => {
  if(!dragging || !dragStart || !state.freeMode) return;
  const dx = e.clientX - dragStart.x;
  const dy = e.clientY - dragStart.y;
  dragStart.x = e.clientX;
  dragStart.y = e.clientY;

  const v = (state.rotateWithLocal ? rotatePoint(dx, dy, lastRotRad) : { x: dx, y: dy });
  freeAnchor.x -= (v.x / state.zoom);
  freeAnchor.y -= (v.y / state.zoom);
  saveSettings();
});

canvas.addEventListener("pointerup", () => {
  dragging = false;
  dragStart = null;
});

canvas.addEventListener("wheel", (e) => {
  // wheel zoom around center
  e.preventDefault();
  const delta = Math.sign(e.deltaY);
  const z = state.zoom * (delta > 0 ? 0.92 : 1.08);
  state.zoom = clamp(z, ZOOM_MIN, ZOOM_MAX);
  if(inputs.zoom) inputs.zoom.value = String(state.zoom);
  saveSettings();
}, { passive:false });

/* =========================
   WIDGET LISTS + PING FIX
========================= */
function isPMC(p){
  const ai = (p?.isAI ?? p?.IsAI);
  if(typeof ai === "boolean") return (ai === false);

  const tn = String(p?.typeName ?? p?.TypeName ?? "").toLowerCase();
  if(p?.isPmc === true || p?.IsPmc === true) return true;
  if(tn === "player") return true;
  const role = String(p?.role ?? p?.Role ?? "").toLowerCase();
  if(role.includes("pmc")) return true;
  return false;
}

function getFollowTarget(players){
  if(!Array.isArray(players)) return null;
  if(state.centerTarget === "local") return players.find(p => p?.isLocal || p?.IsLocal) || null;
  const key = state.centerTarget;
  for(let i=0;i<players.length;i++){
    const p = players[i];
    if(getPlayerKey(p, i) === key) return p;
  }
  return players.find(p => p?.isLocal || p?.IsLocal) || null;
}

function refreshLootWidget(){
  if(!state.showLootWidget) return;
  if(!lootWidgetList) return;

  const map = radarData?.map || null;
  const loot = Array.isArray(radarData?.loot) ? radarData.loot : [];

  const q = normStr(state.lootWidgetSearch).toLowerCase();
  const items = [];

  for(const l of loot){
    const name = getLootNameFromPayloadOrDb(l) || "Loot";
    const price = getLootPriceFromPayloadOrDb(l);
    const id = getLootBsgId(l);
    const gi = getLootGroupInfo(l);
    const col = gi?.color || lootDefaultColor(price);

    if(q){
      const hay = (name + " " + id).toLowerCase();
      if(!hay.includes(q)) continue;
    }

    items.push({ l, name, price, id, col });
  }

  items.sort((a,b)=> (b.price - a.price));

  const top = items.slice(0, 50);
  if(lootWidgetCount) lootWidgetCount.textContent = String(top.length);
  if(lootWidgetSub) lootWidgetSub.textContent = top.length ? `top ${top.length}` : "empty";

  const html = top.map(it => {
    const img = it.id ? `https://assets.tarkov.dev/${encodeURIComponent(it.id)}-base-image.webp` : "";
    const mxmy = (map ? readLootMapXY(it.l, map) : null);
    const mx = mxmy?.x ?? null;
    const my = mxmy?.y ?? null;

    return `
      <div class="w-row"
           data-kind="loot"
           data-mx="${mx ?? ""}"
           data-my="${my ?? ""}"
           data-label="${escapeHtml(it.name)}"
           data-color="${escapeHtml(it.col)}">
        ${img ? `<img class="w-ico" src="${img}" loading="lazy" onerror="this.style.display='none'">` : `<div class="w-ico"></div>`}
        <div class="w-main">
          <div class="w-name">${escapeHtml(it.name)}</div>
          <div class="w-sub mono">${escapeHtml(it.id || "")}</div>
        </div>
        <div class="w-right">
          <div class="w-price">$${escapeHtml(fmtMoney(it.price) || "0")}</div>
        </div>
      </div>
    `;
  }).join("");

  lootWidgetList.innerHTML = html || `<div class="mono" style="color:var(--muted);font-size:12px;padding:10px;">No loot</div>`;
}

// FIX: make taps reliable (click + pointerup, delegated)
function handleWidgetPingFromEvent(e, listEl){
  const t = e.target;
  if(!(t instanceof HTMLElement)) return;
  const row = t.closest(".w-row");
  if(!(row instanceof HTMLElement)) return;

  const mx = Number(row.getAttribute("data-mx"));
  const my = Number(row.getAttribute("data-my"));
  if(!Number.isFinite(mx) || !Number.isFinite(my)) return;

  const label = row.getAttribute("data-label") || "";
  const color = row.getAttribute("data-color") || "#fbbf24";
  setPing(mx, my, label, color);
  hideTooltip();
}
lootWidgetList.addEventListener("click", (e) => handleWidgetPingFromEvent(e, lootWidgetList));
lootWidgetList.addEventListener("pointerup", (e) => {
  if(e.pointerType === "touch" || e.pointerType === "pen"){
    handleWidgetPingFromEvent(e, lootWidgetList);
  }
});

function refreshPlayersWidget(){
  if(!state.showPlayersWidget) return;
  if(!playersWidgetList) return;

  const players = Array.isArray(radarData?.players) ? radarData.players : [];
  const map = radarData?.map || null;

  const out = [];
  for(let i=0;i<players.length;i++){
    const p = players[i];
    if(p?.isLocal || p?.IsLocal) continue;
    if(isExtracted(p)) continue;

    const alive = (p?.isAlive ?? p?.IsAlive);
    const corpseMin = Number(state.corpseMinValue) || 0;
    if(alive === false && corpseMin > 0){
      const v = Number(p?.value ?? p?.Value ?? p?.gearValue ?? p?.GearValue ?? 0) || 0;
      if(v < corpseMin) continue;
    }

    if(state.playersWidgetOnlyPMCs && !isPMC(p)) continue;

    const name = normStr(p?.name ?? p?.Name) || "Unknown";
    const col = playerColor(p);
    const pm = map ? readPlayerMapXY(p, map) : { x:null, y:null };

    out.push({ p, name, col, mx: pm.x, my: pm.y });
  }

  if(playersWidgetCount) playersWidgetCount.textContent = String(out.length);
  if(playersWidgetSub) playersWidgetSub.textContent = out.length ? `${out.length} shown` : "empty";

  const html = out.map(it => {
    return `
      <div class="w-row"
           data-kind="player"
           data-mx="${it.mx ?? ""}"
           data-my="${it.my ?? ""}"
           data-label="${escapeHtml(it.name)}"
           data-color="${escapeHtml(it.col)}">
        <div class="w-dot" style="background:${escapeHtml(it.col)}"></div>
        <div class="w-main">
          <div class="w-name">${escapeHtml(it.name)}</div>
          <div class="w-sub mono">${escapeHtml(isPMC(it.p) ? "PMC" : "AI")}</div>
        </div>
      </div>
    `;
  }).join("");

  playersWidgetList.innerHTML = html || `<div class="mono" style="color:var(--muted);font-size:12px;padding:10px;">No players</div>`;
}

playersWidgetList.addEventListener("click", (e) => handleWidgetPingFromEvent(e, playersWidgetList));
playersWidgetList.addEventListener("pointerup", (e) => {
  if(e.pointerType === "touch" || e.pointerType === "pen"){
    handleWidgetPingFromEvent(e, playersWidgetList);
  }
});

/* =========================
   RENDER LOOP
========================= */
let lastMapId = "";
let lastRotRad = 0;
let lastLocalMap = null;

// free mode anchor (map coords)
let freeAnchor = { x: 0, y: 0, mapId: "" };

// follow-mode anchor (used for pinch in follow mode)
let followAnchorMap = null;

function pickMapId(){
  return normStr(radarData?.mapId ?? radarData?.MapId ?? radarData?.nameId ?? radarData?.NameId ?? radarData?.map?.id ?? radarData?.map?.Id ?? "");
}

function frame(){
  requestAnimationFrame(frame);

  ctx.clearRect(0, 0, cw, ch);
  hitList = [];

  const map = radarData?.map || null;
  const players = Array.isArray(radarData?.players) ? radarData.players : [];
  const loot = Array.isArray(radarData?.loot) ? radarData.loot : [];

  const inRaid = !!(radarData?.inRaid ?? radarData?.inGame);

  if(subline){
    const mid = pickMapId() || "unknown";
    subline.textContent = `menu | ${mid}${inRaid ? "" : " (idle)"}`;
  }

  const { cx, cy } = getViewportCenter();

  // find local
  const local = players.find(p => p?.isLocal || p?.IsLocal) || null;
  lastLocalPlayer = local;

  // rotation based on local yaw
  const localYaw = local ? toRadMaybe(local?.yaw ?? local?.Yaw ?? 0) : 0;
  lastRotRad = localYaw;

  // choose anchor
  let anchor = null;
  if(state.freeMode){
    const mapId = pickMapId();
    if(freeAnchor.mapId !== mapId){
      // reset anchor when map changes
      if(local && map) {
        const lm = readPlayerMapXY(local, map);
        freeAnchor.x = lm.x; freeAnchor.y = lm.y;
      } else {
        freeAnchor.x = 0; freeAnchor.y = 0;
      }
      freeAnchor.mapId = mapId;
    }
    anchor = freeAnchor;
    followAnchorMap = null;
  }else{
    const tgt = getFollowTarget(players) || local;
    if(tgt && map){
      const tm = readPlayerMapXY(tgt, map);
      anchor = { x: tm.x, y: tm.y };
      followAnchorMap = anchor; // used for follow pinch
    }else{
      anchor = { x:0, y:0 };
      followAnchorMap = anchor;
    }
  }

  lastLocalMap = (local && map) ? readPlayerMapXY(local, map) : null;
  lastMapId = pickMapId();

  // draw map
  if(state.showMap && map){
    const localY = readWorldY(local);
    drawMap(map, localY, cx, cy, state.zoom, lastRotRad, anchor);
  }

  if(!map) return;

  const mapRect = getMapScreenRectAnchored(map, cx, cy, state.zoom, anchor);
  if(!mapRect) return;

  // draws
  if(state.showGroups) drawGroupConnectors(players, map, cx, cy, lastRotRad, mapRect);
  if(state.showLoot) drawLoot(loot, map, cx, cy, lastRotRad, mapRect, hitList);
  if(state.showPlayers) drawPlayers(players, map, cx, cy, lastRotRad, mapRect, readWorldY(local), hitList);
  drawPois(map, cx, cy, lastRotRad, mapRect, hitList);

  drawPing(mapRect, cx, cy, lastRotRad);

  // tooltip
  updateHover();
}
requestAnimationFrame(frame);
