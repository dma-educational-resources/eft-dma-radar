# Arena DMA Radar — New Project Roadmap

## Current Status (snapshot)

Phases 0 – 4 are effectively landed; the runtime now reaches a live player pipeline
and renders a minimal diagnostic radar. Phase 5 (map radar) is now wired up with
Arena-specific SVG maps sourced from the pre-1.0 arena radar reference
(`C:\Temp\arena examples\eft-dma-radar-pre1.0-main\Maps\Arena_*`).

| Phase | Status | Notes |
|---|---|---|
| 0 – Project Scaffold | ✅ Done | `src-arena\arena-dma-radar.csproj` builds, references `lib\VmmSharpEx`. No loose native DLLs in `src-arena` — `VmmSharpEx` owns native asset copy. |
| 1 – DMA + ScatterAPI | ✅ Done | `src-arena\DMA\Memory.cs` locates `EscapeFromTarkovArena.exe`, resolves `VmmHandle`, `UnityBase`, `GOM`, `GameAssemblyBase`. |
| 2 – IL2CPP Dumper | ✅ Done | Dumper writes `%AppData%\eft-dma-radar-arena\il2cpp_offsets.json`; `Arena\Offsets.cs` consumes it. |
| 3 – Unity + GameWorld | ✅ Done | `LocalGameWorld.cs` finds `ClientLocalGameWorld` via GOM, reads `MapID` / `IsInRaid`, starts workers. |
| 4 – Player model | 🟡 Functional but unstable | `RegisteredPlayers.cs` enumerates players and resolves positions, but runtime logs show intermittent `VmmSharpEx.VmmException` reads and transform invalidations. |
| 5 – Minimal Radar UI + Map | ✅ Done | `UI\Maps\*` ported from silk; arena-specific `src-arena\Maps\` (Airpit, Bay5, Block/Yard, Bowl, ChopShop, Equator, Fort, Iceberg, Sawmill, Skybridge, default) wired via csproj glob; `RadarWindow` switches between SVG map mode and grid fallback automatically. |
| 6+ | ⏳ Not started | Camera/aimview, loot, grenades, settings panels, live map calibration. |

Known open issues carried into the next phase:
- Repeated `VmmSharpEx.VmmException` read failures for some players during scatter rounds.
- Occasional transform auto-invalidation / re-init cycles.
- Arena maps ship with pre-1.0 calibration (`x`, `y`, `scale`); needs live
  re-tuning once Arena runs end-to-end again.
- No EFT maps (Factory / Ground Zero / Woods) — Arena doesn't use those scenes;
  they were intentionally dropped from the project.

---

## Arena Map Inventory

Arena does **not** share scene IDs with EFT. The Arena-specific maps are copied
into `src-arena\Maps\` and deployed to `<output>\wwwroot\Maps\` via a
`<Content Include="Maps\**\*.*">` glob in `arena-dma-radar.csproj`. Scene IDs come
directly from the pre-1.0 arena radar config files and also drive
`Arena\GameWorld\Exits\MapNames.cs`.

| File | Scene ID (`MapID`) | Display Name |
|---|---|---|
| `Arena_Airpit.json` | `Arena_AirPit` | Air Pit |
| `Arena_Bay5.json` | `Arena_Bay5` | Bay 5 |
| `Arena_Block.json` | `Arena_Yard` | Block (Yard) |
| `Arena_Bowl.json` | `Arena_Bowl` | Bowl |
| `Arena_ChopShop.json` | `Arena_AutoService` | Chop Shop |
| `Arena_Equator.json` | `Arena_equator_TDM_02` | Equator |
| `Arena_Fort.json` | `Arena_Prison` | Fort |
| `Arena_Iceberg.json` | `Arena_Iceberg` | Iceberg |
| `Arena_Sawmill.json` | `Arena_saw` | Sawmill |
| `Arena_Skybridge.json` | `Arena_RailwayStation` | Skybridge |
| `default.json` | `default` | Unknown scene fallback |

If a live `MapID` value is not in this list, `MapManager` falls back to
`default`, logs `"[MapManager] No config for '<id>', using default."`, and the
render loop continues to work. New Arena maps only need a JSON + SVG drop into
`src-arena\Maps\` plus a `MapNames` entry.

---

## Next Port: Map Radar (Phase 5 completion)

**Goal:** replace the bare XZ diagnostic plot in `UI\RadarWindow.cs` with a real
SVG-based radar map using the Silk map system, so players render on the correct
Arena map with proper world → screen projection.

**Why this next:** Phases 0-4 already produce live world positions for the local
player and observed players. The highest-value next step is to make those
positions *visually correct* on an actual map. It is isolated to UI + map config
and does not require touching the still-flaky memory read path. Hardening
`RegisteredPlayers` reads can happen in parallel but should not block map work.

### Scope

In scope:
1. Port the silk map subsystem (`src-silk\UI\Maps\*`) into `src-arena\UI\Maps\`.
2. Introduce an Arena maps directory (SVG layers + JSON configs) resolved from
   the output tree, mirroring silk's `wwwroot\Maps` pattern.
3. Hook `MapManager` into Arena startup and drive it from `LocalGameWorld.MapID`.
4. Rewrite `UI\RadarWindow.cs` draw loop to use `MapParams` world → screen
   projection instead of the current ad-hoc XZ plot, while keeping the existing
   pan/zoom/diagnostic knobs as a fallback when no map is loaded.
5. Ship an initial map set for the three known Arena scenes observed live:
   `factory4_day`, `Sandbox` / `Sandbox_High`, `Woods`.

Explicitly **out of scope** for this phase:
- Fixing `VmmSharpEx.VmmException` read failures (tracked separately, Phase 4.5).
- Aimview, loot, grenade rendering (Phases 6–8).
- Settings panel / ImGui (Phase 9).

### Files to port verbatim (or near-verbatim)

| Source (`src-silk\UI\Maps\`) | Destination (`src-arena\UI\Maps\`) | Adaptation |
|---|---|---|
| `IRadarMap.cs` | same | Namespace → `eft_dma_radar.Arena.UI.Maps`. |
| `MapConfig.cs` | same | Trim `_names` to Arena-relevant entries (reuse `Arena\GameWorld\Exits\MapNames.cs` instead — single source of truth). |
| `MapParams.cs` | same | Namespace only. |
| `RadarMap.cs` | same | Namespace only. |
| `MapManager.cs` | same | Namespace + change `MapsDir` to point at Arena's maps output folder. |

### New / modified files

- `src-arena\UI\Maps\*` — the ported files above.
- `src-arena\Maps\` (new, at repo root of `src-arena` or under `src-arena\wwwroot\Maps`)
  with a `CopyToOutputDirectory=PreserveNewest` rule in `arena-dma-radar.csproj`.
  Initial JSON configs:
  - `Maps\Factory.json` → `mapID: ["factory4_day", "factory4_night"]`
  - `Maps\GroundZero.json` → `mapID: ["Sandbox", "Sandbox_High"]`
  - `Maps\Woods.json` → `mapID: ["Woods"]`
  SVG layers reused from silk's existing assets where the scene geometry is
  identical (Factory/GroundZero/Woods share geometry with EFT counterparts).
- `src-arena\Program.cs` — call `MapManager.ModuleInit()` at startup (after
  config load, before the window runs). Already pattern-matches silk.
- `src-arena\UI\RadarWindow.cs` — add a map render pass:
  1. On each frame, read `LocalGameWorld.Current?.MapID`.
  2. Call `MapManager.LoadMapForId(mapId)` (non-blocking; shows "Loading map…"
     overlay while `IsLoading`).
  3. When `MapManager.Map` is non-null, compute `MapParams` from the current
     local player position + zoom + window size and blit the map image under
     the players.
  4. Replace the hand-rolled XZ → screen math with `MapParams.WorldToScreen`
     so player dots and chevrons line up with the map.
  5. Keep the current grid/axis rendering as a fallback for when no map config
     matches the live `MapID` (useful while discovering unknown Arena scene
     names).
- `src-arena\Arena\GameWorld\Exits\MapNames.cs` — no change needed, but this
  becomes the canonical source of Arena map IDs; `MapConfig` display-name
  resolution should delegate to it instead of duplicating the dictionary.

### Implementation steps (sequence)

1. **Copy map subsystem**: create `src-arena\UI\Maps\` and drop in the four
   ported files with only namespace adjustments.
2. **Wire maps output folder**: add a `<Content Include="Maps\**\*.*">` glob
   to `arena-dma-radar.csproj` with `CopyToOutputDirectory=PreserveNewest`. Confirm
   files land under `bin\...\wwwroot\Maps\` (or whatever path `MapManager.MapsDir`
   resolves to — align paths both ways, prefer `wwwroot\Maps` to match silk).
3. **Seed initial configs**: commit `Factory.json`, `GroundZero.json`,
   `Woods.json` plus their SVG layers (reuse silk assets where legal/possible).
4. **Startup wiring**: in `Program.cs`, call `MapManager.ModuleInit()` after
   `ArenaConfig.Load()` and before `RadarWindow.Run()`.
5. **MapID-driven loading**: in `RadarWindow.OnRender` (or a small
   `MapManager.TrySelectForRaid(mapId)` helper), request map load whenever
   `LocalGameWorld.Current.MapID` changes. Guard with `IsLoading`.
6. **Render pass**: insert map blit before player rendering; switch player
   projection to `MapParams.WorldToScreen`.
7. **Fallback**: if no `MapConfig` matches the live `MapID`, log the raw ID
   once and keep the current grid view so diagnostic use is preserved.
8. **Validate live**: run against a real Arena match on each of the three
   initial maps; capture `MapID` values and verify player dots stay on
   walkable geometry during movement.
9. **Document results**: update this roadmap with confirmed Arena scene names
   and any map config offsets (x / y / scale / svgScale) found during tuning.

### Acceptance criteria

- `src-arena` builds cleanly on .NET 10 with the new `UI\Maps\` subsystem.
- Launching against a live Arena match on Factory / Ground Zero / Woods shows
  the correct SVG map under the player dots.
- Local player dot stays on valid walkable terrain during movement for at
  least a full round.
- Unknown Arena `MapID` values fall back to the current grid view and are
  logged exactly once per session.
- No regression in the existing diagnostic knobs (`_yawOffsetDeg`, `_yawSign`,
  `_useRotYasYaw`, pan, zoom) — they should still be reachable as a debug mode.

### Risks

| Risk | Mitigation |
|---|---|
| Arena scene coordinates differ from EFT even when scene name matches | Tune `x`, `y`, `scale` per Arena map; treat silk configs as a starting point only. |
| `MapID` values from Arena IL2CPP don't match the silk names | Log raw value on first frame; extend `MapNames` + `MapConfig.mapID` lists as discovered. |
| Asset licensing / duplication with silk maps | Prefer `<Link>` references or a shared `Maps` root at the solution level if feasible; otherwise copy only what's needed. |
| Memory read instability (`VmmSharpEx.VmmException`) masks map regressions | Treat map correctness as independent; run Phase 4.5 stabilization in parallel. |

### Parallel track — Phase 4.5: read-path stabilization

Not a blocker for the map port, but should be worked alongside it:
- Add targeted retries / scatter splitting for the hot paths in
  `RegisteredPlayers.Scatter` that currently emit `VmmSharpEx.VmmException`.
- Debounce transform auto-invalidation so a single bad read does not cycle
  through init/invalidate repeatedly.
- Log per-player failure counts so we can identify whether failures correlate
  with specific player states (dead, spectated, far away, just-joined).

---

## Overview

This document defines the plan to build a brand-new **Arena DMA Radar** as a third project
in this solution, targeting **EscapeFromTarkovArena.exe** (IL2CPP). The old `arena-dma-radar`
project (reference: `C:\Temp\arena examples\eft-dma-radar-pre1.0-main\arena-dma-radar\`) was
written for **Mono** and is entirely broken on IL2CPP. Rather than patching it, we build fresh
using `src-silk` as the base template — copying its architecture, DMA layer, IL2CPP dumper,
and UI verbatim, then replacing only the game-specific layer.

---

## Why Start From Silk, Not the Old Arena

| Old Arena (`arena-dma-radar`) | New Arena (this plan) |
|---|---|
| Mono (`MonoLib.InitializeArena()`, `MonoLib.GameWorldField`) | IL2CPP (same as `src-silk`) |
| `MemDMABase` + `SafeMemoryProxy` pattern | `Memory.cs` static state machine (silk pattern) |
| Shared `eft-dma-shared` library | Standalone — no shared lib, mirrors silk |
| WPF UI (xaml + SkiaSharp forms) | Silk.NET + ImGui + SkiaSharp GPU (silk pattern) |
| `MonoClass.Find(...)` for class resolution | `Il2CppDumper` + `TypeInfoTableResolver` |
| Hardcoded `ClassNames` MDTokens (Mono) | IL2CPP-dumped field/method offsets at runtime |
| `EscapeFromTarkovArena.exe` process | **Same process name** — still valid |
| Arena-specific game SDK (`SDK.cs`) | New `ArenaOffsets.cs` — verified at runtime by dumper |

---

## Solution Structure After This Project

```
eft-dma-radar.sln
├── lib/VmmSharpEx/           ← shared DMA lib (referenced by all)
├── src-silk/                 ← EFT radar (existing, untouched)
└── src-arena/                ← NEW: Arena radar
	├── arena-dma-radar.csproj
	├── Program.cs
	├── GlobalUsings.cs
	├── Config/
	│   └── ArenaConfig.cs
	├── DMA/
	│   ├── Memory.cs         ← arena-specific state machine (EscapeFromTarkovArena.exe)
	│   └── ScatterAPI/       ← copy from silk (identical)
	├── Arena/
	│   ├── Offsets.cs        ← ArenaOffsets — IL2CPP-resolved at runtime
	│   ├── Unity/
	│   │   ├── Unity.cs      ← copy from silk (IL2CPP engine constants, GOM, TrsX)
	│   │   ├── Bones.cs      ← copy from silk
	│   │   ├── ViewMatrix.cs ← copy from silk
	│   │   ├── Collections/  ← MemArray, MemList, MemDictionary (copy from silk)
	│   │   └── IL2CPP/
	│   │       ├── Dumper/   ← copy + adapt from silk (arena-specific cache path)
	│   │       └── Resolvers/← arena-specific resolvers (EftHardSettings, CameraManager, etc.)
	│   ├── GameWorld/
	│   │   ├── LocalGameWorld.cs       ← port from old arena, IL2CPP path (no MonoLib)
	│   │   ├── RegisteredPlayers.cs    ← port from old arena, scatter pattern from silk
	│   │   ├── RegisteredPlayers.Discovery.cs
	│   │   ├── RegisteredPlayers.Scatter.cs
	│   │   ├── CameraManager.cs        ← port from old arena, IL2CPP resolver for OpticCameraManagerContainer
	│   │   ├── Player/
	│   │   │   ├── Player.cs           ← arena-specific player base (armband, team, ERaidMode)
	│   │   │   ├── Player.Draw.cs      ← team-color rendering, armband indicators
	│   │   │   ├── LocalPlayer.cs
	│   │   │   ├── ArenaObservedPlayer.cs ← arena's ObservedPlayerView + ObservedPlayerController pattern
	│   │   │   ├── PlayerType.cs       ← simplified (no PMC/Scav/Boss — teams/solo only)
	│   │   │   ├── GearManager.cs      ← copy from silk (gear reads are same IL2CPP layout)
	│   │   │   ├── HandsManager.cs     ← copy from silk
	│   │   │   └── Skeleton.cs         ← copy from silk
	│   │   ├── Grenades/
	│   │   │   └── GrenadeManager.cs   ← port from old arena GrenadeManager
	│   │   └── Loot/
	│   │       ├── LootManager.cs      ← simplified vs EFT (Arena has limited loot)
	│   │       ├── LootItem.cs
	│   │       ├── LootContainer.cs    ← ArenaPresetRefillContainer support
	│   │       └── LootFilter.cs
	├── Misc/                 ← copy from silk (Log, Misc, Extensions, Workers, Pools, SizeChecker)
	└── UI/
		├── RadarWindow.cs    ← copy from silk, stripped of EFT-specific panels
		├── SKPaints.cs       ← arena team colors (red/blue/yellow/green armband palette)
		├── CustomFonts.cs    ← copy from silk
		├── Maps/             ← copy from silk map system; add Arena maps
		└── Panels/
			├── SettingsPanel.cs  ← arena settings (match mode display, team colors, etc.)
			└── ...
```

---

## Key Differences: Arena vs EFT Game Logic

### 1. Process Name
```
EscapeFromTarkovArena.exe   (was correct in old arena, still valid)
```

### 2. Game World Discovery — The Critical Mono→IL2CPP Break
The old arena used `MonoLib` to find the game world:
```csharp
// OLD (Mono - BROKEN on IL2CPP)
var localGameWorld = Memory.ReadPtr(MonoLib.GameWorldField, false);
var networkGame    = Memory.ReadPtr(MonoLib.AbstractGameField, false);
MonoLib.InitializeArena();
```

The new approach mirrors silk exactly — GOM-based lookup:
```csharp
// NEW (IL2CPP — same as src-silk LocalGameWorld.cs)
var gom         = Memory.ReadPtr(Memory.GOM + UnityOffsets.GOM.ActiveScene, false);
var gameWorld   = GOM.FindComponent(gom, "ClientLocalGameWorld");
var networkGame = GOM.FindComponent(gom, "NetworkGame"); // for ERaidMode
```
`ClientLocalGameWorld` is the same class name in Arena — confirmed in old `SDK.cs`.

### 3. Match Mode / ERaidMode
Arena has its own `ERaidMode` enum the EFT project doesn't have. It must be read after
game world discovery to configure match behaviour:
```csharp
// Offsets.NetworkGame.NetworkGameData → Offsets.NetworkGameData.raidMode → (int)ERaidMode
```
This drives: `MatchHasTeams`, `MatchMode`, whether `InteractiveManager` initializes.

### 4. Offsets Differences (Arena SDK vs EFT Offsets)
Critical Arena-specific offsets that differ from `src-silk/Tarkov/Offsets.cs`:

| Struct | Field | Old Arena value | Note |
|---|---|---|---|
| `ClientLocalGameWorld` | `LocationId` | `0x90` | EFT silk uses `0xD0` — must dump |
| `ClientLocalGameWorld` | `LootList` | `0x118` | EFT silk uses `0x198` |
| `ClientLocalGameWorld` | `RegisteredPlayers` | `0x140` | EFT silk uses `0x1B8` |
| `ClientLocalGameWorld` | `MainPlayer` | `0x1B0` | EFT silk uses `0x210` |
| `ClientLocalGameWorld` | `Grenades` | `0x210` | EFT silk uses `0x288` |
| `ClientLocalGameWorld` | `IsInRaid` | `0x290` | EFT silk has no direct IsInRaid bool |
| `GameWorld` | `Location` | `0x90` | Different from EFT |

**All of these must be IL2CPP-dumped at runtime** — the old Arena values are Mono-era
and almost certainly stale. The `Il2CppDumper` in Silk resolves offsets by class/field name;
the same mechanism will be used for Arena with an arena-specific cache:
```
%AppData%\eft-dma-radar-arena\il2cpp_offsets.json
```

### 5. Player Model — ObservedPlayerView Pattern
Arena uses a different player class layout:
- `EFT.Player` → still the base (same as EFT)
- `ObservedPlayerView` — arena's observed player wrapper (has `PlayerBody` at different offset)
- `ObservedPlayerController.Player` → dereferences to `EFT.Player`
- `ArenaClientPlayer` → local player type
- No `PMC`/`Scav`/`Boss` classification — instead **team identity by armband color** (`ArmbandColorType`)

### 6. Player Type Classification
Old EFT radar has 10 player types (PMC, Scav, Boss, Raider…). Arena needs:
- `LocalPlayer` — you
- `TeamPlayer` — ally (same team color armband)
- `EnemyPlayer` — opponent
- `SpectatedPlayer` — when local player is dead (spectator mode)
(No AI scavs in Arena matches — only human players)

### 7. No Hideout, No Exfils, No BTR, No Quests
These EFT-specific systems are entirely absent from Arena. The radar will skip:
- `HideoutManager`
- `ExfilManager` / `TransitController`
- `BtrTracker`
- `QuestManager` / `QuestPlanner`
- `LootAirdrop`

### 8. Loot Is Simplified
Arena has limited looting (preset refill containers at fixed positions). `LootManager`
will be heavily simplified vs EFT — no dogtag, no corpse gear, no nested grid scan.
Only `ArenaPresetRefillContainer` (ammo boxes) need tracking.

### 9. InteractiveManager — Conditional
Only active for `CheckPoint` and `LastHero` game modes (door interactables). Other
modes (`ShootOut`, `TeamFight`, etc.) skip it.

---

## IL2CPP Dumper — What Needs to Change

The Silk IL2CPP dumper (`src-silk/Tarkov/Unity/IL2CPP/Dumper/`) resolves field offsets
from `GameAssembly.dll`'s type info table. For Arena:

1. **Cache path** → `%AppData%\eft-dma-radar-arena\il2cpp_offsets.json`
2. **Assembly name** → `Assembly-CSharp` (same binary name, different game binary)
3. **Type range** → Arena's `AssemblyCSharp` has a different `TypeStart`/`TypeCount` — must scan
4. **Class names** → same EFT namespace (`EFT.*`), Arena just has fewer classes
5. **Resolver targets** → different set: `ClientLocalGameWorld`, `NetworkGame`, `NetworkGameData`,
   `ObservedPlayerView`, `ObservedPlayerController`, `MovementContext`, `ProceduralWeaponAnimation`

The dumper code itself needs **no structural changes** — only the config values above.

---

## Phase Plan

### Phase 0 — Project Scaffold *(start here)*
**Goal:** Compilable empty Arena project in the solution.

1. Create `src-arena/arena-dma-radar.csproj` — target `net10.0-windows7.0`, same `Directory.Build.props`
2. Add `ProjectReference` to `lib/VmmSharpEx/VmmSharpEx.csproj`
3. Add project to `eft-dma-radar.sln`
4. Copy `GlobalUsings.cs` from silk, update namespace to `eft_dma_radar.Arena`
5. Create stub `Program.cs` (console entry point, no window yet)
6. Copy `Misc/` layer from silk verbatim (`Log.cs`, `Misc.cs`, `Extensions.cs`, `Workers/`, `Pools/`, `ExceptionTracer.cs`)
7. Verify solution builds

### Phase 1 — DMA + ScatterAPI Layer
**Goal:** Arena `Memory.cs` finds `EscapeFromTarkovArena.exe` and reaches `ProcessFound` state.

1. Copy `DMA/ScatterAPI/` from silk verbatim (identical scatter API)
2. Create `DMA/Memory.cs` — clone silk `Memory.cs`, change:
   - `ProcessName = "EscapeFromTarkovArena.exe"`
   - Remove EFT-specific properties (`Hideout`, `Loot`, `Exfils`, etc.)
   - Keep core state machine: `NotStarted → ProcessFound → InRaid`
   - Remove `InHideout` state (Arena has no hideout)
3. Confirm `VmmHandle`, `UnityBase`, `GOM`, `GameAssemblyBase` resolve correctly

### Phase 2 — IL2CPP Dumper
**Goal:** Dumper resolves Arena's field offsets into arena-specific cache.

1. Copy `Tarkov/Unity/IL2CPP/Dumper/` from silk verbatim into `Arena/Unity/IL2CPP/Dumper/`
2. Update namespace and cache path to `%AppData%\eft-dma-radar-arena\`
3. Create `Arena/Offsets.cs` — stub with the Arena-specific struct names:
   - `ClientLocalGameWorld` (LocationId, LootList, RegisteredPlayers, MainPlayer, Grenades, IsInRaid)
   - `GameWorld` (Location)
   - `NetworkGame` (NetworkGameData)
   - `NetworkGameData` (raidMode)
   - `Player` (all existing EFT player offsets — many will be same)
   - `ObservedPlayerView` (PlayerBody)
   - `ObservedPlayerController` (Player)
   - `MovementContext`, `ProceduralWeaponAnimation`, etc.
4. Wire dumper into `Memory.cs` startup (same call site as silk)
5. **Verify:** run Arena, confirm `il2cpp_offsets.json` is written with valid values
6. **Cross-check all offsets** against `%AppData%\eft-dma-radar-arena\il2cpp_offsets.json` — 
   discard any old Arena `SDK.cs` values that don't match

### Phase 3 — Unity Layer + Game World Discovery
**Goal:** `LocalGameWorld` instantiates successfully in Arena and reads `MapID` + `IsInRaid`.

1. Copy `Arena/Unity/` from silk: `Unity.cs`, `Bones.cs`, `ViewMatrix.cs`, `Collections/`
2. Create `Arena/GameWorld/LocalGameWorld.cs`:
   - GOM-based `ClientLocalGameWorld` discovery (no MonoLib)
   - Read `MapID` from `Offsets.GameWorld.Location` → `Offsets.ClientLocalGameWorld.LocationId`
   - Read `ERaidMode` from `NetworkGame → NetworkGameData → raidMode`
   - Read `IsInRaid` bool from `Offsets.ClientLocalGameWorld.IsInRaid`
   - Validate against Arena's map name list (different maps than EFT)
   - Two-worker design: fast (player transforms) + slow (loot/misc)
3. Integrate into `Memory.cs` `RunGameLoop()` (same lifecycle as silk)
4. Add Arena map names: `factory4_day` (Arena Factory), `Sandbox` (Ground Zero Arena),
   `Woods` (Arena Woods), etc. — confirm by reading `MapID` live

### Phase 4 — Player Model
**Goal:** Players render on radar with correct team identification.

1. Copy `Arena/Unity/Bones.cs` from silk (same skeleton layout)
2. Create `Arena/GameWorld/Player/PlayerType.cs` — `LocalPlayer`, `TeamPlayer`, `EnemyPlayer`, `Spectator`
3. Create `Arena/GameWorld/Player/Player.cs` — abstract base:
   - Position/rotation from `TrsX` (same IL2CPP transform chain as silk)
   - `ArmbandColor` read from `Offsets.Player.ArmbandColorType`
   - `IsTeammate` — compare armband color to local player's armband
   - `IsAlive` — from `Offsets.Player.HealthController` chain
   - `Name` — from Profile (same path as EFT)
4. Create `Arena/GameWorld/Player/LocalPlayer.cs` — `ArenaClientPlayer` type
5. Create `Arena/GameWorld/Player/ArenaObservedPlayer.cs` — `ObservedPlayerController → ObservedPlayerView → EFT.Player`
6. Create `Arena/GameWorld/RegisteredPlayers.cs` + `.Discovery.cs` + `.Scatter.cs` — port from silk scatter pattern
7. Confirm players appear and classify correctly by armband

### Phase 5 — Minimal Radar UI
**Goal:** Radar window shows map + players. Equivalent to silk Phase 1 deliverable.

1. Copy `UI/CustomFonts.cs`, `UI/SKPaints.cs` from silk — extend with arena team color paints
2. Create `Arena/GameWorld/Player/Player.Draw.cs` — dot + name label + team color
3. Copy map system from silk (`UI/Maps/`) — initially use a placeholder map
4. Create `Config/ArenaConfig.cs` — minimal config (team colors, radar scale, etc.)
5. Create `UI/RadarWindow.cs` — stripped silk RadarWindow (no loot, no exfil, no hideout panels)
6. Wire into `Program.cs` — Silk.NET window startup, same flow as silk
7. **Milestone:** arena radar window opens, shows players moving on map

### Phase 6 — CameraManager + Aimview
**Goal:** Aimview widget functional; view matrix resolves for Arena.

1. Create `Arena/Unity/IL2CPP/Resolvers/OpticCameraManagerResolver.cs`:
   - IL2CPP path: find `OpticCameraManagerContainer` singleton via GOM or TypeInfoTable
   - Old Arena used `MonoClass.Find(...)` — replace with `Il2CppDumper` class lookup
2. Create `Arena/GameWorld/CameraManager.cs` — port from old arena `CameraManager.cs`:
   - `FPSCamera`, `OpticCamera` properties
   - `Refresh()` on fast worker thread
   - Same `CheckIfScoped()` logic (ProceduralWeaponAnimation path)
3. Add `ViewMatrix.cs` reads (copy from silk — same IL2CPP camera layout)
4. Add `AimviewWidget` to UI (copy from silk)

### Phase 7 — Loot (ArenaPresetRefillContainer)
**Goal:** Ammo box refill containers visible on radar.

1. Create `Arena/GameWorld/Loot/LootManager.cs` — simplified (no corpse, no dogtag, no nested grid):
   - Reads `Offsets.ClientLocalGameWorld.LootList`
   - Filters for `ArenaPresetRefillContainer` type by class name check
   - Reads static position only (containers don't move)
2. Create `Arena/GameWorld/Loot/LootContainer.cs` — position + display name
3. Render containers on radar (box icon, fixed positions)

### Phase 8 — GrenadeManager
**Goal:** Live grenades visible on radar.

1. Port `Arena/GameWorld/GrenadeManager.cs` from old arena to IL2CPP:
   - Reads `Offsets.ClientLocalGameWorld.Grenades` (dictionary)
   - IL2CPP `MemDictionary<int, ulong>` (silk pattern)
   - Position from Throwable transform chain
2. Render grenades on radar (same pattern as silk explosives)

### Phase 9 — UI Polish + Settings Panel
**Goal:** Usable settings UI via ImGui.

1. `UI/Panels/SettingsPanel.cs` — Arena-specific settings:
   - Team colors (per armband color type)
   - Match mode display
   - Render toggles (loot, grenades, names, aimview)
   - Hotkey bindings
2. `UI/Panels/PlayerHistoryPanel.cs` — copy from silk
3. `UI/Panels/PlayerWatchlistPanel.cs` — copy from silk
4. PlayerInfoWidget (hover tooltips) — copy from silk, strip EFT-only fields (K/D, survival rate)

### Phase 10 — Maps
**Goal:** Accurate maps for all Arena game modes.

Arena maps (scene names to confirm live):
- `factory4_day` → Arena Factory (confirm — may differ from EFT factory)
- `Sandbox` / `Sandbox_High` → Ground Zero Arena
- `Woods` → Arena Woods variant
- Custom arena-only maps (verify via live `MapID` reads)

Action:
1. Create `Maps/` directory under `src-arena/` with SVG maps
2. Create per-map `*.json` config (scale, origin, floor bounds)
3. Add map names to `Arena/GameWorld/Exits/MapNames.cs`

---

## Files to Copy Verbatim From Silk (No Changes Needed)

These are pure infrastructure — game-agnostic:

| Source (`src-silk/`) | Destination (`src-arena/`) |
|---|---|
| `DMA/ScatterAPI/*` | `DMA/ScatterAPI/*` |
| `Misc/Log.cs` | `Misc/Log.cs` |
| `Misc/Misc.cs` | `Misc/Misc.cs` |
| `Misc/Extensions.cs` | `Misc/Extensions.cs` |
| `Misc/ExceptionTracer.cs` | `Misc/ExceptionTracer.cs` |
| `Misc/SizeChecker.cs` | `Misc/SizeChecker.cs` |
| `Misc/Workers/WorkerThread.cs` | `Misc/Workers/WorkerThread.cs` |
| `Misc/Pools/*` | `Misc/Pools/*` |
| `Misc/Input/InputManager.cs` | `Misc/Input/InputManager.cs` |
| `Misc/Input/HotkeyManager.cs` | `Misc/Input/HotkeyManager.cs` |
| `Tarkov/Unity/Unity.cs` | `Arena/Unity/Unity.cs` |
| `Tarkov/Unity/Bones.cs` | `Arena/Unity/Bones.cs` |
| `Tarkov/Unity/ViewMatrix.cs` | `Arena/Unity/ViewMatrix.cs` |
| `Tarkov/Unity/Collections/*` | `Arena/Unity/Collections/*` |
| `Tarkov/Unity/IL2CPP/Dumper/*` | `Arena/Unity/IL2CPP/Dumper/*` |
| `UI/CustomFonts.cs` | `UI/CustomFonts.cs` |
| `UI/Maps/*` | `UI/Maps/*` |
| `UI/Widgets/AimviewWidget.cs` | `UI/Widgets/AimviewWidget.cs` |

---

## Files to Port From Old Arena (Adapt, Not Copy)

These files have the right *logic* but use Mono APIs that must be replaced:

| Old Arena File | New Arena File | Key Change |
|---|---|---|
| `Arena/GameWorld/LocalGameWorld.cs` | `Arena/GameWorld/LocalGameWorld.cs` | Remove `MonoLib.*` → GOM lookup |
| `Arena/GameWorld/RegisteredPlayers.cs` | same | Port to silk scatter pattern |
| `Arena/GameWorld/CameraManager.cs` | same | `MonoClass.Find` → IL2CPP resolver |
| `Arena/ArenaPlayer/Player.cs` | `Arena/GameWorld/Player/Player.cs` | Remove Mono types, use silk IL2CPP transforms |
| `Arena/ArenaPlayer/LocalPlayer.cs` | `Arena/GameWorld/Player/LocalPlayer.cs` | Same |
| `Arena/ArenaPlayer/ArenaObservedPlayer.cs` | `Arena/GameWorld/Player/ArenaObservedPlayer.cs` | Same |
| `Arena/GameWorld/GrenadeManager.cs` | same | Use silk `MemDictionary` instead of Mono collection |
| `Arena/Loot/LootManager.cs` | `Arena/GameWorld/Loot/LootManager.cs` | Use silk scatter chain |

---

## Files NOT to Port (Do Not Bring Over)

- Any WPF UI (`*.xaml`, `ESPForm`, `MainWindow`, `Pages/`)
- `MonoLib.cs` — entire file is irrelevant on IL2CPP
- `SafeMemoryProxy` / `MemoryInterface` — silk's `Memory.cs` static class replaces this
- `Aimbot.cs` — memory write, excluded per instructions
- `Chams.cs`, `PlayerChamsManager.cs`, `GrenadeChamsManager.cs` — memory write
- `RageMode.cs`, `BigHead.cs`, `ClearWeather.cs`, `DisableGrass.cs`, `FastWeaponOps.cs`,
  `HideRaidCode.cs`, `NoWepMalfPatch.cs`, `RemoveableAttachments.cs`, `StreamerMode.cs`,
  `DisableShadows.cs`, `DisableScreenEffects.cs`, `FOV.cs`, `TimeOfDay.cs` — memory writes, frozen

---

## First Step to Take Now — Verify the Dumper

Before writing any game logic, the IL2CPP dumper must be proven to work against
`EscapeFromTarkovArena.exe`. This is the single highest-risk item.

**Action plan for Phase 0 → Phase 2 validation:**

1. Scaffold the project (Phase 0)
2. Bring `Memory.cs` up with `ProcessName = "EscapeFromTarkovArena.exe"` (Phase 1)
3. Copy dumper, point cache to arena path, stub out `ArenaOffsets.cs` (Phase 2)
4. Run Arena in background, launch the new project
5. Check `%AppData%\eft-dma-radar-arena\il2cpp_offsets.json` — if it populates, the
   hardest part is done. Then cross-check every offset value before touching game logic.

The dumper scans `GameAssembly.dll`'s type info table by class/field **name** — it is
runtime-agnostic between EFT and Arena as long as the class names match (they do, both
use `EFT.*` namespace from the same SDK base).

---

## Open Questions / Risks

| Risk | Mitigation |
|---|---|
| Arena `TypeStart`/`TypeCount` in `AssemblyCSharp` differs from EFT | Dumper scans from `TypeStart`; adjust range if needed or do a broader scan |
| `ClientLocalGameWorld` class name may differ in Arena's IL2CPP binary | Read live `MapID` field first; if garbage, the class name or offset is wrong |
| `IsInRaid` bool may not exist at the same offset | Use `GetPlayerCount() > 0` as fallback (same as EFT silk) |
| Arena `ObservedPlayerView` layout may have changed since old radar | Validate player position reads against known player count |
| Arena map IDs — unknown exact scene names | Log raw `MapID` string on first run to discover them |
| `ERaidMode` values — old `SDK.cs` enum may be stale | Read raw int, log it, compare to expected match type |
| Arena-specific `GameAssembly.dll` offset for `GameWorld` singleton in GOM | Try `"ClientLocalGameWorld"` as component name; fall back to scanning GOM active objects |
