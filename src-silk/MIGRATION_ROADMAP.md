# WPF → Silk.NET Migration Roadmap

## Current State
**Phase 1 complete (including structural restructuring).** `src-silk` is a fully standalone executable — no runtime dependency
on the WPF project's `Memory.cs`, `Player.cs`, or `LocalGameWorld.cs`.

- **Silk.NET project** (`src-silk`): Silk.NET + SkiaSharp + ImGui window — **running independently**
  - Own `Memory.cs` (DMA layer): state machine, worker thread, full scatter read/write API
  - Own IL2CPP dumper (`src-silk/Tarkov/Unity/IL2CPP/Dumper/`) — silk-native namespace,
    isolated cache at `%AppData%\eft-dma-radar-silk\il2cpp_offsets.json`
  - Own game model mirroring WPF structure:
    - `Tarkov/GameWorld/Player/Player.cs` — base player class with `Draw()`, `DrawPriority`
    - `Tarkov/GameWorld/Player/LocalPlayer.cs` — sealed subclass, `IsLocalPlayer => true`
    - `Tarkov/GameWorld/RegisteredPlayers.cs` — player collection with refresh logic
    - `Tarkov/GameWorld/LocalGameWorld.cs` — raid lifecycle, background refresh thread
  - Own map system mirroring WPF structure:
    - `UI/Radar/Maps/IRadarMap.cs`, `IMapEntity.cs` — interfaces
    - `UI/Radar/Maps/MapConfig.cs`, `MapParams.cs`, `MapManager.cs`, `RadarMap.cs`
  - Own `SilkConfig` (`%AppData%\eft-dma-radar-silk\config.json`)
  - `RadarWindow` draws via `Player.Draw()` — rendering logic lives on the player, not the window
  - `WPF ProjectReference` still present in `.csproj` — referenced only for shared SDK types
    (`Offsets`, `EftDataManager`, `SKPaints`) that have not yet been ported; no WPF runtime types used
- **WPF project** (`src`): unchanged, still fully functional in parallel

## Architecture Goals
- **Standalone `src-silk`**: Own `Memory.cs` + minimal game types, no WPF project reference
- **Start minimal**: Map render, player positions/rotations, raid begin/end — nothing more
- **Separation of concerns**: DMA layer, game model, UI are distinct layers
- **Graceful lifecycle**: Proper state machine, error recovery, clean restart
- **Incremental migration**: Pull features from WPF project one at a time as needed
- **Shared `VmmSharpEx`**: Both projects keep referencing `lib/VmmSharpEx` directly

---

## Phase 0 — Foundation & Structure ✅ (Done)
> Extracted the monolithic RadarWindow into clean, separated components.

- [x] Make `RadarWindow` a `partial class` split across focused files
- [x] Extract `SettingsPanel` → `src-silk/UI/Panels/SettingsPanel.cs`
- [x] Extract `LootFiltersPanel` → `src-silk/UI/Panels/LootFiltersPanel.cs`
- [x] Create `PlayerInfoWidget` → `src-silk/UI/Widgets/PlayerInfoWidget.cs`
  - ImGui window showing human hostiles in a sortable table
  - Columns: Name, Group, In Hands, Value, Distance
  - Color-coded by player type
- [x] Wire widgets into the render loop via `DrawWindows()`

---

## Phase 1 — Standalone Memory & Minimal Game Model
> Give `src-silk` its own DMA layer and lightweight game types.
> Goal: map rendering with player dots (position + rotation), raid lifecycle, restart support.
> **No loot, no gear, no quests, no chams, no hideout — just the basics.**

### 1A. Standalone `Memory.cs` (`src-silk/DMA/Memory.cs`) ✅
> Clean rewrite, not a copy of the WPF one. Minimal, well-structured, easy to extend later.

- [x] **State enum** — `MemoryState` enum: `NotStarted → WaitingForProcess → Initializing → ProcessFound → InRaid → Restarting`
- [x] **Init & VMM setup** — `ModuleInit(SilkConfig)` creates `Vmm`, registers auto-refresh, starts the background worker. No `ResourceJanitor`, no `Hideout`, no `FeatureManager` hooks.
- [x] **Worker thread** — `MemoryWorker()` with outer `while(true)` loop:
  1. `RunStartupLoop()` — find process, load `UnityPlayer.dll` base, run IL2CPP
     dumper (resolves GOM + runtime offsets), load GOM address
  2. `RunGameLoop()` — poll for `LocalGameWorld`, create a minimal game instance,
     refresh players in a loop, detect raid end
  3. On any fatal error → reset state, wait, retry
- [x] **Events** — `GameStarted` / `GameStopped` / `RaidStarted` / `RaidStopped`
- [x] **Restart support** — `RequestRestart()` with `CancellationTokenSource` swap. `RestartRadar` property.
- [x] **Read/Write API** — All read/write methods implemented:
  - `ReadValue<T>`, `ReadPtr`, `ReadPtrChain`, `ReadBuffer<T>`, `ReadString`, `ReadUnityString`
  - `TryReadValue<T>`, `TryReadPtr`, `TryReadPtrChain`, `TryReadBuffer<T>`, `TryReadString`
  - `ReadScatter`, `ReadValueEnsure<T>`, `WriteValue<T>`, `WriteBuffer<T>`, `WriteValueEnsure<T>`
  - `FindSignature`, `FindSignatures`, `GetScatter`, `FullRefresh`, `ThrowIfNotInGame`
  - `ReadPeFingerprint`, `Close`
- [x] **No WPF types** — uses `SilkConfig`, no `Program.Config`, no `FeatureManager`, no `Hideout`
- [x] **Supporting files** — `ScatterAPI/` (IScatterEntry, MemPointer, ScatterReadMap/Round/Index/Entry), `Misc/` (Utils, Extensions, SizeChecker, Pools, Log shared from WPF)

### 1B. Import IL2CPP Dumper & SDK Offsets ✅
> IL2CPP dumper files copied into `src-silk` with silk-native namespace/dependencies.

- [x] `Il2CppDumper.Dump()` called in `RunStartupLoop()` via silk namespace `eft_dma_radar.Silk.Tarkov.Unity.IL2CPP`
- [x] `GameObjectManager.GetAddr()` used in `LoadModules()`
- [x] `SDK.cs` `Offsets` used in `GameSession.cs` for player/profile/transform offsets
- [x] **Full file copies** into `src-silk/Tarkov/Unity/IL2CPP/Dumper/`:
  - `Il2CppDumper.cs` — core dump entry point, field/method readers, reflection helpers
  - `Il2CppDumperCache.cs` — JSON cache load/save, PE fingerprint fast path
  - `Il2CppDumperFull.cs` — full dump-to-file (DumpAll), inflated generic lookup
  - `Il2CppDumperSchema.cs` — field schema (all EFT classes mapped to `Offsets.*` structs)
  - `TypeInfoTableResolver.cs` — sig-scan TypeInfoTableRva, diagnostic report
  - All `NotificationsShared.*` calls replaced with `Log.WriteLine`
  - All `IsValidVirtualAddress()` calls use `eft_dma_radar.Silk.Misc.Utils` to avoid WPF ambiguity
  - `UTF8String` aliased to `eft_dma_radar.Silk.Misc.UTF8String`

### 1C. Minimal Game Types (`src-silk/Tarkov/`) ✅

- [x] **`PlayerBase`** — `Name`, `Type` (`PlayerType` enum), `Position` (Vector3), `RotationYaw` (float), `GroupID`, `SpawnGroupID`, `IsAlive`, `IsActive`, `IsLocalPlayer`, `IsHuman`, `IsHostile`
- [x] **`GameSession`** — `MapID`, `InRaid`, `Players` (IReadOnlyCollection), `LocalPlayer`, `Create()` factory (scans GOM for GameWorld), `Start()` (starts refresh thread), `Dispose()`

### 1C′. Structural Restructuring (WPF-mirrored hierarchy) ✅
> Reorganized flat file layout to mirror WPF's well-organized folder hierarchy.

**Player hierarchy** (mirrors `src/Tarkov/GameWorld/Player/`):
- [x] `PlayerBase` → `Player` base class in `Tarkov/GameWorld/Player/Player.cs`
  - `virtual bool IsLocalPlayer => false` (replaces `init` property)
  - `internal virtual void Draw(SKCanvas, MapParams, MapConfig)` — rendering logic moved from RadarWindow
  - `int DrawPriority` property — replaces static `DrawPriority()` method in RadarWindow
  - `protected virtual GetPaints()` — returns dot/text paints by PlayerType
- [x] `LocalPlayer : Player` sealed subclass in `Tarkov/GameWorld/Player/LocalPlayer.cs`
  - `override bool IsLocalPlayer => true`
  - `override GetPaints()` returns LocalPlayer-specific paints

**Game world** (mirrors `src/Tarkov/GameWorld/`):
- [x] `GameSession` split into:
  - `LocalGameWorld` in `Tarkov/GameWorld/LocalGameWorld.cs` — raid lifecycle, factory, worker thread
  - `RegisteredPlayers` in `Tarkov/GameWorld/RegisteredPlayers.cs` — player collection, refresh, transform reads
- [x] `RegisteredPlayers : IReadOnlyCollection<Player>` — owns PlayerEntry, TrsX, all offset constants

**Map system** (mirrors `src/UI/Radar/Maps/`):
- [x] `UI/Map/*.cs` → `UI/Radar/Maps/*.cs` with namespace `eft_dma_radar.Silk.UI.Radar.Maps`
- [x] `IRadarMap` interface — mirrors WPF `IXMMap` (ID, Config, Draw, GetParameters)
- [x] `IMapEntity` interface — `Draw(SKCanvas, MapParams, MapConfig)`
- [x] `RadarMap` now implements `IRadarMap`

**Consumer updates**:
- [x] `Memory.cs` — `GameSession` → `LocalGameWorld`, `PlayerBase` → `Player`
- [x] `RadarWindow.cs` — removed `DrawPlayer()`, `GetPlayerPaints()`, `DrawPriority()`; uses `player.Draw()`
- [x] `PlayerInfoWidget.cs` — `PlayerBase` → `Player`
- [x] `GlobalUsings.cs` / `Program.cs` — updated namespace imports

### 1D. Update `SilkProgram` (entry point) ✅
- [x] WPF `ProjectReference` kept for now (types still needed by RadarWindow draw code)
- [x] `SilkConfig` — own JSON config: `DeviceStr`, `MemMapEnabled`, `UIScale`, `TargetFps`, `MemWritesEnabled`
- [x] Startup: Load `SilkConfig` → `Memory.ModuleInit(config)` → `RadarWindow.Run()` → `Memory.Close()`
- [x] `SilkProgram.Config` is now `SilkConfig` (not WPF `Config`)
- [x] `SilkProgram.State` driven by `Memory.State` (MemoryState enum)

### 1E. Update `RadarWindow` to use new types ✅
- [x] Replace `Memory.Players` (was `IReadOnlyCollection<Player>`) with `Memory.Players`
  (now `IReadOnlyCollection<Player>` via silk `Player` type)
- [x] Replace `Memory.LocalPlayer` (was WPF's `LocalPlayer`) with silk `Player`
- [x] Radar drawing: `player.Draw(canvas, mapParams, mapConfig)` — rendering logic on the entity, not the window
- [x] Removed all WPF-only properties: `FilteredLoot`, `Containers`, `Explosives`, `Exits`, `QuestManager`
- [x] Status bar shows: `MemoryState`, `MapID`, player count, FPS
- [x] `SettingsPanel` rewired to `SilkConfig` (removed WpfConfig); Loot tab removed (Phase 3)
- [x] `PlayerInfoWidget` rewired to silk `Memory.Players` / `Player`
- [x] `SilkConfig` extended: `WindowWidth/Height`, `WindowMaximized`, `BattleMode`, `PlayersOnTop`, `ConnectGroups`
- [x] `DrawSkiaScene` conditions on silk `Memory.InRaid` + `Player` local player
- [x] `DrawGroupConnectors` works on `List<Player>`
- [x] `UISharedState` dependency removed; `MouseoverGroup` is a plain backing field
- [x] Mouseover hit-testing uses silk player positions projected through `MapParams`

### 1F. Graceful Error Handling & Restart ✅
- [x] `Memory` catches all exceptions in the worker loop, logs them, resets state, retries
- [x] `RadarWindow` shows current `MemoryState` as a status indicator
  (e.g., "Waiting for game...", "In Raid — 8 players", "Error — retrying...")
- [x] If DMA init fails → show error in ImGui overlay (not a WPF MessageBox)
- [x] `RequestRestart()` callable from UI (menu item or hotkey)
- [x] Clean disposal on window close: `Memory.Close()` disposes VMM handle

### 1G. Startup Sequencing & Cache Path Isolation ✅
> Correct launch order and full path isolation from the WPF project.

- [x] **Cache path isolation** — IL2CPP cache writes to `%AppData%\eft-dma-radar-silk\il2cpp_offsets.json`
  (previously shared WPF folder `eft-dma-radar-public` — caused stale-cache fast-loads)
- [x] **`WaitForTypeInfoTable()`** — inserted between `LoadModules()` and `Il2CppDumper.Dump()`;
  polls `GameAssembly.dll + TypeInfoTableRva` every 500 ms until the pointer is a valid VA,
  capped at 60 s; prevents dump from firing before EFT's IL2CPP runtime has initialized
- [x] **Startup order confirmed**: `LoadProcess → LoadModules → WaitForTypeInfoTable → Dump → SetState(Initializing)`

### 1H. Diagnostics & Error Visibility ✅
> Replace silent failures with structured, actionable log output.

- [x] **`BadPtrException`** — new `sealed class` in `Misc.cs`; carries `Address` + `Value`;
  thrown by `Memory.ReadPtr` instead of `ArgumentException`; allows VS exception settings
  to ignore expected DMA control-flow failures without suppressing real `ArgumentException`s
- [x] **`RefreshPlayers` split** — list-header read and pointer-array read each wrapped in
  their own `try/catch` with `[tag]`, exception type name, and rate-limited `Warning` log
- [x] **`RefreshWorker` catch** — now logs `ex.GetType().Name + ex.Message + ex.StackTrace`
- [x] **`TryInitTransform` / `TryInitRotation` / `TryUpdatePlayer`** — bare `catch {}` replaced
  with `Debug`-level log including exception type, address, and message
- [x] **`ReadPlayer` catch** — exception type added alongside message
- [x] **Per-tick `[GW] ClientLocalGameWorld` log removed** — was flooding output every ~780 ms;
  chain-failure logs (`[GW] Chain[0xNN] failed`) are kept

### What gets left behind (pulled in later phases)
| WPF Feature | Silk Phase |
|---|---|
| Full Player model (Gear, Hands, Health) | Phase 2 |
| Loot system (LootManager, FilteredLoot) | Phase 3 |
| Exits, Explosives, Quests | Phase 3 |
| EftDataManager (item database) | Phase 3 |
| FeatureManager (chams, memory writes) | Phase 5+ |
| Config system (multi-profile, IConfig) | Phase 2 |
| Own SKPaints (remove WPF project ref) | Phase 2 |
| ResourceJanitor (GC pressure mgmt) | Phase 4+ |
| HideoutManager | Phase 6+ |
| InputManager (DMA-based input) | Phase 5 |

---

## Phase 2 — Full Game Model & Config
> Bring over the complete player model, map loading, and config system.

- [ ] Full `Player` model: Hands (CurrentItem), Gear (Value, Equipment), Health, Skills
- [ ] `XMMapManager` port or simplified map loader for SkiaSharp
- [ ] `EftDataManager` for item names/prices (needed for loot in Phase 3)
- [ ] Silk's own `Config` system: JSON-based, multi-profile, matching WPF feature set
- [ ] Settings Panel: full tabs (General, Players, Map) with real config bindings
- [ ] `CameraManager` for aimview prep

## Phase 3 — Loot, Exits & Game World
> Complete game world state.

- [ ] `LootManager`, `FilteredLoot`, `StaticLootContainers`
- [ ] `ExitPoints`, `Explosives`
- [ ] `QuestManager` & quest rendering on radar
- [ ] Loot Filters Panel (full ImGui editor)
- [ ] Loot Widget (sortable ImGui table)

## Phase 4 — Aimview Widget
> FBO-backed SkiaSharp aimview rendered as an ImGui image.

- [ ] Create OpenGL FBO + texture for off-screen SkiaSharp rendering
- [ ] Port aimview camera math (forward/right/up from localPlayer rotation)
- [ ] Render players as 3D-projected dots/boxes
- [ ] Render nearby loot and containers
- [ ] ImGui window wrapping the texture with drag/resize
- [ ] `ResourceJanitor` port for memory pressure management

## Phase 5 — Hotkeys, Input & Memory Writes
> Interactive features that need DMA-based input or memory writing.

- [ ] `HotkeyManager` class with configurable key bindings
- [ ] `HotkeyManagerPanel` ImGui UI for editing bindings
- [ ] `InputManager` port (DMA-based keyboard/mouse input via VmmSharpEx)
- [ ] `FeatureManager` port (chams, memory write features)
- [ ] Memory writes gated by config flag

## Phase 6 — Color Picker, Theming & Advanced UI
> Customizable colors and additional panels.

- [ ] `ColorPickerPanel` using ImGui's built-in `ColorEdit3`/`ColorEdit4`
- [ ] Color categories: Players, Loot tiers, UI elements
- [ ] Map Setup Helper panel
- [ ] Debug Info Widget (memory stats, FPS graph)
- [ ] `HideoutManager` port

## Phase 7 — Platform Polish
> Production quality touches.

- [ ] Window icon (Win32 interop)
- [ ] Dark mode title bar (DwmSetWindowAttribute)
- [ ] ImGui layout persistence (imgui.ini)
- [ ] Custom ImGui fonts (load from embedded resources)
- [ ] ImGui DPI scaling
- [ ] SilkDispatcher for async UI operations

## Phase 8 — WPF Deprecation
> Once Silk.NET is feature-complete, remove the WPF project.

- [ ] Feature parity checklist vs WPF MainWindow
- [ ] ESP overlay (if applicable)
- [ ] Web Radar panel
- [ ] Remove WPF ProjectReference from silk `.csproj`
- [ ] Remove WPF project from solution (or mark as legacy)

---

## File Structure Target (after Phase 1)

```
src-silk/
├── DMA/
│   ├── Memory.cs               ← NEW: standalone, ~300-400 lines
│   └── ScatterAPI/             ← IMPORTED: IScatterEntry + scatter types
├── Tarkov/
│   ├── SDK.cs                  ← IMPORTED: Offsets (minimal subset for Phase 1)
│   ├── Unity/IL2CPP/           ← IMPORTED: dumper, Il2CppClass, GameObjectManager
│   │   ├── Dumper/             ← Il2CppDumper (4 partial files, no changes needed)
│   │   ├── Il2CppClass.cs
│   │   ├── TypeInfoTableResolver.cs
│   │   └── UnityStructures.cs  ← GameObjectManager.GetAddr()
│   ├── PlayerBase.cs           ← NEW: minimal player (pos, rot, type, name)
│   └── GameSession.cs          ← NEW: minimal raid state (map, players, lifecycle)
├── Config/
│   └── SilkConfig.cs           ← NEW: simple JSON config (device, memmap, UI prefs)
├── UI/
│   ├── RadarWindow.cs          ← UPDATED: uses new types, no WPF dependencies
│   ├── Panels/
│   │   ├── SettingsPanel.cs    ← UPDATED: binds to SilkConfig
│   │   └── LootFiltersPanel.cs ← placeholder (Phase 3)
│   └── Widgets/
│       └── PlayerInfoWidget.cs ← UPDATED: uses PlayerBase instead of Player
├── Program.cs                  ← UPDATED: no WPF project reference in startup
└── MIGRATION_ROADMAP.md
```

## Key Principles
1. **Don't copy the WPF Memory.cs** — write fresh, keep it simple, extend as needed
2. **Import self-contained subsystems as-is** — IL2CPP dumper, ScatterAPI, SDK offsets
   don't need rewriting; just copy and adjust namespaces
3. **Each phase should build and run** — no half-broken intermediate states
4. **VmmSharpEx is shared** — both projects reference `lib/VmmSharpEx` directly
5. **Pull, don't push** — only bring WPF code into silk when a phase specifically needs it
6. **The WPF project stays working** — silk migration doesn't break the existing app

## Reference
- WPF `Memory.cs`: `src/DMA/Memory.cs` (~918 lines) — reference for read/write API surface
- WPF `Player.cs`: `src/Tarkov/GameWorld/Player/Player.cs` — reference for full player model
- WPF `LocalGameWorld.cs`: `src/Tarkov/GameWorld/LocalGameWorld.cs` — reference for raid lifecycle
- ImGui.NET docs: https://github.com/ImGuiNET/ImGui.NET
