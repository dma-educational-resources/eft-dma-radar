# WPF → Silk.NET Migration Roadmap

## Current State
**Phase 5 complete. Phase 6 not yet started.** `src-silk` is a feature-rich standalone radar with full player model
(gear, hands, dogtag identity, profile lookups), aimview widget, exfils/transits/doors,
loot filtering with wishlist/blacklist, static loot containers, web radar server, DMA-based
input/hotkeys with standalone panel, matching progress tracking, hardened raid lifecycle,
hideout stash/area reading with persistent data across raid transitions, a full
quest system (QuestManager, QuestPanel, quest zone radar rendering, lobby quest reader,
loot quest-item integration, and LobbyQuestReader lifecycle), CameraManager with ViewMatrix
and bone-based skeleton rendering, and a complete memory write system (FeatureManager with
21 features including movement, vision, weapon, and interaction categories).
**121 source files, ~26.1K lines of C#.**

- **Silk.NET project** (`src-silk`): Silk.NET + SkiaSharp + ImGui window — **running independently**
  - Own `Memory.cs` (DMA layer): state machine, worker thread, full scatter read/write API
  - Own IL2CPP dumper (`src-silk/Tarkov/Unity/IL2CPP/Dumper/`) — silk-native namespace,
    isolated cache at `%AppData%\eft-dma-radar-silk\il2cpp_offsets.json`
  - Own `Offsets.cs` (game SDK) — all 379 offsets, fully independent from WPF `SDK.cs`
  - Own `Unity.cs` — IL2CPP engine constants, `GOM`, `ComponentArray`, `GameObject`, `TrsX`,
    `UnityOffsets` (named constants replacing magic numbers throughout)
  - Unity collection wrappers: `MemArray<T>`, `MemList<T>` (pooled DMA wrappers for C# arrays/lists)
  - Own game model:
    - `Tarkov/GameWorld/Player/Player.cs` — base player class with identity, gear, profile stats
    - `Tarkov/GameWorld/Player/Player.Draw.cs` — rendering: dot, chevron, aimline, labels, per-type paints
    - `Tarkov/GameWorld/Player/LocalPlayer.cs` — sealed subclass, `IsLocalPlayer => true`
    - `Tarkov/GameWorld/Player/PlayerType.cs` — player type enum (10 types)
    - `Tarkov/GameWorld/Player/GearManager.cs` — scatter-batched equipment/dogtag reader
    - `Tarkov/GameWorld/Player/GearItem.cs` — equipment slot model
    - `Tarkov/GameWorld/Player/HandsManager.cs` — reads item in hands from memory (cached, change-detection)
    - `Tarkov/GameWorld/RegisteredPlayers.cs` — player collection (partial class)
    - `Tarkov/GameWorld/RegisteredPlayers.Discovery.cs` — player discovery & classification
    - `Tarkov/GameWorld/RegisteredPlayers.Scatter.cs` — scatter-batched transform reads
    - `Tarkov/GameWorld/LocalGameWorld.cs` — raid lifecycle, non-blocking startup, two-tier workers
    - `Tarkov/GameWorld/Loot/LootManager.cs` — 6-round scatter chain + corpse dogtag + equipment + container extraction
    - `Tarkov/GameWorld/Loot/LootItem.cs` — loot rendering with price tiers, wishlist/category awareness
    - `Tarkov/GameWorld/Loot/LootContainer.cs` — static container model (BSG ID, name, searched state, radar + aimview rendering)
    - `Tarkov/GameWorld/Loot/LootCorpse.cs` — corpse model with equipment, total value, dogtag name
    - `Tarkov/GameWorld/Loot/LootFilter.cs` — full pipeline: blacklist → wishlist → category → price → name
    - `Tarkov/GameWorld/Loot/LootFilterData.cs` — persistent wishlist/blacklist (FrozenSet lookups)
    - `Tarkov/GameWorld/Loot/DogtagCache.cs` — persistent ProfileId→AccountId database
    - `Tarkov/GameWorld/Exits/ExfilManager.cs` — scatter-batched exfil/transit reader with retry
    - `Tarkov/GameWorld/Exits/Exfil.cs` — exfil point with status, eligibility, cached drawing
    - `Tarkov/GameWorld/Exits/TransitPoint.cs` — transit point with static position from JSON data
    - `Tarkov/GameWorld/Exits/ExfilStatus.cs` — Closed/Pending/Open enum
    - `Tarkov/GameWorld/Exits/ExfilNames.cs` — per-map friendly name mappings (FrozenDictionary)
    - `Tarkov/GameWorld/Exits/MapNames.cs` — map ID → display name mapping
    - `Tarkov/GameWorld/Interactables/Door.cs` — keyed door with state, key identity, near-loot flag
    - `Tarkov/GameWorld/Interactables/InteractablesManager.cs` — door discovery + state refresh
    - `Tarkov/ProfileService.cs` — tarkov.dev profile fetcher (KD, hours, survival rate)
  - DMA input system:
    - `DMA/InputManager.cs` — DMA-based keyboard input via VmmSharpEx (~100 Hz polling)
    - `DMA/HotkeyManager.cs` — configurable hotkey bindings with rebind UI support
  - Pre-raid tracking:
    - `Tarkov/Unity/IL2CPP/MatchingProgressResolver.cs` — matching stage tracking with timer-based polling
  - Own data layer:
    - `Misc/Data/EftDataManager.cs` — embedded item + container + map database (FrozenDictionary), transit positions
    - `Misc/Data/TarkovMarketItem.cs` — item model with category helpers (IsMeds, IsKey, IsStaticContainer, etc.)
  - Own map system mirroring WPF structure:
    - `UI/Radar/Maps/IRadarMap.cs`, `IMapEntity.cs` — interfaces
    - `UI/Radar/Maps/MapConfig.cs`, `MapParams.cs`, `MapManager.cs`, `RadarMap.cs`
  - Own `SilkConfig` (`%AppData%\eft-dma-radar-silk\config.json`) with validation + debounced save
  - Own `SKPaints.cs`, `CustomFonts.cs` — immutable per-type paints, blur-based text shadows
  - `RadarWindow` draws via `Player.Draw()` — rendering logic lives on the player, not the window
  - Hover tooltips on radar canvas (SkiaSharp) + PlayerInfoWidget (ImGui) with column-aligned layout
  - Aimview widget — synthetic camera from player position + rotation, projected players/loot/corpses/containers
  - Standalone HotkeyManagerPanel — full rebind + clear UI with DMA status
  - Web radar server — Kestrel HTTP, `/api/radar` JSON endpoint, background worker thread
  - **No WPF ProjectReference** — fully standalone
- **WPF project** (`src-wpf`): renamed from `src`, removed from solution, still functional standalone

## Architecture Goals
- **Standalone `src-silk`**: Own `Memory.cs`, `Offsets.cs`, `Unity.cs`, loot, data — no WPF reference ✅
- **Non-blocking startup**: Workers start immediately; local player discovered in background;
  radar shows "Waiting for Raid Start" until position available, then transitions seamlessly ✅
- **Start minimal**: Map render, player positions/rotations, raid begin/end — nothing more ✅
- **Separation of concerns**: DMA layer, game model, UI are distinct layers ✅
- **Graceful lifecycle**: Proper state machine, error recovery, clean restart, graceful shutdown ✅
- **Incremental migration**: Pull features from WPF project one at a time as needed ✅
- **Shared `VmmSharpEx`**: Both projects keep referencing `lib/VmmSharpEx` directly ✅

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
- [x] `RegisteredPlayers : IReadOnlyCollection<Player>` — owns PlayerEntry, all offset constants
  - `TrsX` shared struct extracted to `Unity.cs` (used by both RegisteredPlayers and LootManager)

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
- [x] ~~WPF `ProjectReference`~~ — removed (SDK independence achieved in Phase 1I)
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

### 1I. SDK Independence & WPF Decoupling ✅
> Full separation from the WPF project — no `ProjectReference`, no shared types.

- [x] **Own `Offsets.cs`** (`Tarkov/Offsets.cs`) — `GameSDK` struct with all game offsets,
  renamed from `SDK.cs` to avoid collision; 379 offsets updated from `il2cpp_offsets.json`
- [x] **Own `Unity.cs`** (`Tarkov/Unity/Unity.cs`) — `UnityOffsets` static class with named
  constants for all IL2CPP engine offsets (`Comp_ObjectClass`, `GO_Components`, `List.*`,
  `TransformAccess.*`, `TransformHierarchy.*`); `GOM`, `ComponentArray`, `GameObject`,
  `LinkedListObject` structs; `TrsX` shared transform vertex struct
- [x] **Own `SKPaints.cs`** + **`CustomFonts.cs`** — silk-native paint/font definitions
- [x] **WPF `ProjectReference` removed** from `eft-dma-radar-silk.csproj`
- [x] **WPF project renamed** `src/` → `src-wpf/` and removed from solution
  (still functional standalone, just not co-built)
- [x] **`global using SDK = ...Offsets`** added to `GlobalUsings.cs` for compatibility
- [x] **Naming cleanup** — `SilkUnity` → `Unity`, `GameSDK` → `Offsets`, `SilkGOM` → `GOM`,
  `SilkGameObject` → `GameObject`, etc.

### 1J. Loot System Port ✅
> Loose loot reading and rendering — pulled from Phase 3 since dependencies were ready.

- [x] **`LootManager`** (`Tarkov/GameWorld/Loot/LootManager.cs`) — 6-round scatter chain
  reading `ObservedLootItem` objects from the `GameWorld.LootList`; rate-limited refresh (5s)
- [x] **`LootItem`** (`Tarkov/GameWorld/Loot/LootItem.cs`) — minimal loot representation
  with position, name, price, and `IsValuable` threshold from config
- [x] **`EftDataManager`** (`Misc/Data/EftDataManager.cs`) — loads embedded `DEFAULT_DATA.json`
  into a `FrozenDictionary<string, TarkovMarketItem>` at startup
- [x] **`TarkovMarketItem`** (`Misc/Data/TarkovMarketItem.cs`) — BSG ID, name, short name,
  trader/flea prices, best price
- [x] **Radar integration** — loot drawn on radar with price labels; `BattleMode` hides loot;
  `LootPriceThreshold` config controls "important" highlighting

### 1K. IL2CPP Dumper Audit & Offset Cleanup ✅
> Verified all 5 dumper files (~2100 lines) and offset definitions for correctness.

- [x] **Removed `To_FirePortTransformInternal` / `To_FirePortVertices`** from `Offsets.cs` —
  present in code but not in IL2CPP schema, wrong containing type, never used in Silk
- [x] **Added `GamePlayerOwner`** to IL2CPP dumper schema with `TypeIndex` — enables
  direct IL2CPP class resolution for the primary GameWorld discovery path
- [x] **Verified `_afkMonitor` fallback** is harmless (field renamed in game, not used in Silk)
- [x] **55 offset updates** applied from `il2cpp_offsets.json` + 2 new fields + Special type indices

### 1L. Non-Blocking Startup Architecture ✅
> Radar becomes responsive immediately — no blocking wait for local player.

- [x] **`LocalGameWorld.Start()`** — starts both workers immediately, no blocking call
- [x] **`RegistrationWorker`** — discovers local player on first tick(s); skips other work
  until found; radar shows "Waiting for Raid Start" and transitions seamlessly
- [x] **`RegisteredPlayers.TryDiscoverLocalPlayer()`** — non-blocking single-attempt method
  replacing the old blocking `WaitForLocalPlayer()` loop
- [x] **`RadarWindow`** — already handles `LocalPlayer == null` gracefully (shows status message)

### 1M. Code Quality & Documentation Pass ✅
> Comprehensive cleanup across the entire codebase.

- [x] **XML doc comments** added to all undocumented public/internal members:
  `Utils`, `Extensions` (8 methods), `Player` (12 properties), `PlayerType` (10 enum values),
  `LocalGameWorld` (6 properties + `Dispose`), `RegisteredPlayers` (3 properties),
  `LootManager`, `LootItem`
- [x] **`TrsX` struct deduplicated** — extracted from `RegisteredPlayers` and `LootManager`
  into shared `Unity.cs`; both files now reference the single definition
- [x] **`MultiplyQuaternionVector3` removed** from `LootManager` — replaced with standard
  `Vector3.Transform(Vector3, Quaternion)` (already used by `RegisteredPlayers`)
- [x] **Dead code removed** — unused `heightDiff` variable in `LootItem.Draw()`
- [x] **Magic numbers replaced** — `FindGameWorldViaGOM` now uses `GO_Components` /
  `Comp_ObjectClass` instead of raw hex; LootManager scatter chain uses `UnityOffsets.*`
  for all applicable offsets with inline comments for Unity-internal ones
- [x] **Removed unused NuGet packages** — `Collections.Pooled.V2`, `System.Management`,
  ASP.NET framework ref, `wwwroot` content
- [x] **Removed unused global using** — `System.Collections` (non-generic)

### What gets left behind (pulled in later phases)
| WPF Feature | Silk Phase | Status |
|---|---|---|
| ~~Full Player model (Gear, Hands, Health)~~ | ~~Phase 2~~ | ✅ Done (Phase 2 + 3) |
| ~~Loot system (LootManager, FilteredLoot)~~ | ~~Phase 3~~ | ✅ Done (Phase 1) |
| ~~Exits, Transits~~ | ~~Phase 3~~ | ✅ Done (Phase 3) |
| ~~Keyed Doors~~ | ~~Phase 3~~ | ✅ Done (Phase 3) |
| ~~EftDataManager (item database)~~ | ~~Phase 3~~ | ✅ Done (Phase 1) |
| ~~Aimview Widget~~ | ~~Phase 4~~ | ✅ Done (Phase 4) |
| ~~Web Radar~~ | ~~Phase 8~~ | ✅ Done (Phase 3) |
| ~~Loot Filter (wishlist/blacklist/categories)~~ | ~~Phase 3~~ | ✅ Done (Phase 3) |
| ~~Config system (multi-profile, IConfig)~~ | ~~Phase 2~~ | ✅ Done (Phase 2 — simplified) |
| ~~Own SKPaints (remove WPF project ref)~~ | ~~Phase 2~~ | ✅ Done (Phase 1) |
| ~~InputManager (DMA-based input)~~ | ~~Phase 5~~ | ✅ Done (Phase 5A) |
| ~~HotkeyManager (configurable bindings)~~ | ~~Phase 5~~ | ✅ Done (Phase 5A) |
| ~~FeatureManager (memory writes)~~ | ~~Phase 5~~ | ✅ Done (Phase 5H) — 21 features; chams still pending |
| ResourceJanitor (GC pressure mgmt) | Phase 6+ | ❌ Not started |
| ~~HideoutManager~~ | ~~Phase 5~~ | ✅ Done (Phase 5E) |
| ~~QuestManager & quest rendering~~ | ~~Phase 5~~ | ✅ Done (Phase 5F) |
| ~~StaticLootContainers~~ | ~~Phase 5~~ | ✅ Done (Phase 5D) |
| ~~HotkeyManagerPanel~~ | ~~Phase 5~~ | ✅ Done (Phase 5D) |

---

## Phase 2 — Full Player Model, Gear, Profiles & Rendering Polish ✅ (Done)
> Complete player model with gear, dogtag identity, tarkov.dev profiles, aimlines, and UI polish.

### 2A. Player Refactor & Gear System ✅
- [x] **Player partial class split** — `Player.cs` (data model) + `Player.Draw.cs` (rendering)
- [x] **PlayerType enum** — extracted to `PlayerType.cs` (USEC, BEAR, PScav, AIScav, AIRaider, AIBoss, etc.)
- [x] **GearManager** (`Player/GearManager.cs`) — scatter-batched equipment reader:
  - 3-round scatter chain reading equipment slots + dogtag ProfileId
  - Equipment value calculation with EftDataManager price lookup
  - Thermal/NVG detection from slot items
  - Dogtag-based identity resolution (ProfileId → DogtagCache → name/level/AccountId)
- [x] **GearItem** (`Player/GearItem.cs`) — equipment slot model (BSG ID, short name, price)
- [x] **RegisteredPlayers split** into 3 partial files:
  - `RegisteredPlayers.cs` — core collection, public API
  - `RegisteredPlayers.Discovery.cs` — player discovery, classification, registration
  - `RegisteredPlayers.Scatter.cs` — scatter-batched transform/rotation reads

### 2B. Dogtag Identity & Profile Lookup ✅
- [x] **DogtagCache** (`Loot/DogtagCache.cs`) — persistent ProfileId→AccountId database
  - JSON file at `%AppData%\eft-dma-radar-silk\DogtagDb.json`
  - Compatible format with WPF radar's DogtagDb.json
  - Per-raid level cache (in-memory only)
  - Background flush thread (30s interval)
- [x] **Corpse dogtag extraction** — LootManager reads dogtags from corpse loot,
  seeds DogtagCache with ProfileId→AccountId→Nickname→Level
- [x] **ProfileService** (`Tarkov/ProfileService.cs`) — tarkov.dev profile fetcher:
  - Background worker thread fetching from `https://players.tarkov.dev/profile/{accountId}.json`
  - ConcurrentDictionary cache, 1.5s rate limiting, 429 backoff (60s)
  - JSON models: ProfileData, ProfileInfo, ProfileStats, OverAllCounterItem
  - Computed stats: KD, Kills, Deaths, Sessions, SurvivedRate, Hours, AccountType (STD/EOD/UH)
  - Lifecycle: starts on GameStarted, stops on GameStopped
- [x] **Player.Profile** — cached ProfileData reference, populated on first UI access

### 2C. Aimlines & High Alert ✅
- [x] **DrawAimline** — direction line extending from dot edge in facing direction
  - Human players: configurable length (default 15px)
  - AI players: half human length, capped at 10px
  - Dark outline (2.6px) + colored stroke (1.2px) two-pass rendering
- [x] **IsFacingTarget** — ported from WPF HighAlert module
  - 3D yaw→forward vector, dot product angle check
  - Non-linear distance-based threshold (tight at range, loose close)
  - Extends aimline to 2000px when hostile aims at local player
- [x] **Config**: `ShowAimlines`, `AimlineLength` (0–100), `HighAlert`

### 2D. Rendering Polish ✅
- [x] **Blur-based text shadows** — replaced stroked text outlines (`IsStroke=true`)
  with `MaskFilter.CreateBlur()` for smoother rendering
- [x] **Font weight fix** — `FontMedium11` → `FontRegular11` for player/loot labels
  (Medium weight was too heavy at 11pt)
- [x] **Loot text shadow** — added `LootShadow` paint (loot had no shadow previously)
- [x] **Text alignment** — adjusted vertical offsets for player labels (+4.5f) and loot (+4.5f)
- [x] **Shadow contrast** — increased alpha (140→200) and sigma (0.8→1.0) for readability

### 2E. UI & Tooltip Improvements ✅
- [x] **Radar hover tooltips** — profile stats line (KD, Raids, SR%, Hours, AccountType)
- [x] **PlayerInfoWidget K/D column** — 7-column table (Name, Lvl, K/D, Grp, Value, Gear, Dist)
- [x] **Tooltip column alignment** — `ImGui.SameLine(fixedCol)` for clean label-value pairs
  (replaced variable-width SameLine that caused jagged text)
- [x] **Tooltip layout** — three sections (Identity → Profile → Equipment) with separators
- [x] **Settings Panel** — Aimline section (Show, Length slider, High Alert toggle),
  Profile section (Profile Lookups toggle)

### 2F. Code Quality ✅
- [x] **Nullable project-wide** — `Directory.Build.props` upgraded from `warnings` to `enable`,
  removed 11 per-file `#nullable enable` directives
- [x] **Bug fixes** — `SpawnGroupID`/`GroupID` default 0→-1 (valid IDs start at 1),
  `Refresh()` dead code removed, tooltip canvas clamping, FPS timer dispose guard
- [x] **Copilot instructions** — `.github/copilot-instructions.md` added (no WPF type references)
- [x] ~~Map loader~~ — already done (Phase 1, MapManager)
- [x] ~~EftDataManager~~ — already done (Phase 1, embedded FrozenDictionary)
- [x] ~~Own SKPaints~~ — already done (Phase 1)

## Phase 3 — Exits, Doors, Corpses, Hands, Web Radar & Advanced Loot ✅ (Done)
> Complete game world state, interactables, player hands, web radar, and advanced loot filtering.

### 3A. Exfil & Transit System ✅
- [x] **ExfilManager** (`Exits/ExfilManager.cs`) — scatter-batched exfil/transit reader:
  - Reads `ExfiltrationPoint[]` from GameWorld, discovers eligible exfils for local player
  - Multi-round scatter chain for status, eligibility, name resolution
  - Transit discovery from separate `TransitController` pointer chain
  - Retry logic for late-loading exfil data (common in first seconds of raid)
- [x] **Exfil** (`Exits/Exfil.cs`) — exfil point model:
  - Status enum: `Closed` / `UncompleteRequirements` / `NotPresent` / `Pending` / `RegularMode` / `Countdown`
  - Eligibility tracking, cached paints/strings for zero-alloc rendering
  - `Draw()` method with status-colored dot + name label + shadow
- [x] **TransitPoint** (`Exits/TransitPoint.cs`) — transit extraction point:
  - Static positions loaded from `EftDataManager` map data (not read from memory)
  - Active/inactive state with configurable rendering
- [x] **ExfilStatus** (`Exits/ExfilStatus.cs`) — status enum
- [x] **ExfilNames** (`Exits/ExfilNames.cs`) — per-map friendly name mappings (`FrozenDictionary`)
- [x] **MapNames** (`Exits/MapNames.cs`) — map ID → display name mapping
- [x] **Config**: `ShowExfils`, `ShowTransits` toggles in Settings Panel

### 3B. Interactables System (Keyed Doors) ✅
- [x] **InteractablesManager** (`Interactables/InteractablesManager.cs`) — door discovery + state refresh:
  - Reads `WorldInteractiveObject[]` from GameWorld, filters to keyed doors only
  - Background state refresh detecting open/closed/locked/breach transitions
  - Near-loot flag for doors close to valuable items
- [x] **Door** (`Interactables/Door.cs`) — keyed door model:
  - State tracking (Open/Closed/Locked/Breached), key identity from EftDataManager
  - `Draw()` method with state-colored icon + key name label
- [x] **Config**: `ShowDoors` toggle in Settings Panel

### 3C. Hands Manager (In-Hands Item) ✅
- [x] **HandsManager** (`Player/HandsManager.cs`) — reads item currently in player's hands:
  - Cached with change-detection (only re-reads when hands controller pointer changes)
  - Reads item template ID → resolves to short name via EftDataManager
  - Ammo count for weapons (magazine + chamber)
- [x] **Player integration** — `InHandsItem`, `InHandsItemId`, `InHandsAmmo`, `HandsReady` properties
- [x] **PlayerInfoWidget** — "In Hands" column shows current weapon/item name

### 3D. Corpse Equipment & Loot Corpse Model ✅
- [x] **LootCorpse** (`Loot/LootCorpse.cs`) — corpse model with full equipment reading:
  - Equipment slots with item names + prices from EftDataManager
  - Total corpse value calculation
  - Dogtag name/level display from DogtagCache
- [x] **LootManager** — corpse discovery + equipment scatter reads alongside loose loot
- [x] **Config**: `ShowCorpses` toggle, corpse rendering on radar + aimview

### 3E. Loot Filter Overhaul (Wishlist/Blacklist/Categories) ✅
- [x] **LootFilter** (`Loot/LootFilter.cs`) — full rewrite with `FilterResult` struct:
  - Pipeline: Blacklist → Wishlist → Category → Price → Name matching
  - `FilterResult` carries `Visible`, `Important`, `Wishlisted`, `CategoryMatch` flags
  - `Evaluate()` method used by radar, aimview, and loot widget
- [x] **LootFilterData** (`Loot/LootFilterData.cs`) — persistent wishlist/blacklist:
  - JSON file at `%AppData%\eft-dma-radar-silk\lootfilters.json`
  - `FrozenSet<string>` for O(1) lookups, cross-list deconfliction
  - `AddToWishlist()` / `AddToBlacklist()` / `Remove()` with auto-rebuild
- [x] **TarkovMarketItem** — 9 category helpers: `IsMeds`, `IsFood`, `IsBackpack`, `IsKey`,
  `IsAmmo`, `IsBarter`, `IsWeapon`, `IsWeaponMod`, `IsCurrency`
- [x] **LootFiltersPanel** — complete UI redesign:
  - Category toggles section with emoji-prefixed checkboxes
  - Wishlist & Blacklist collapsible sections with search-to-add
  - +W / +B quick-add buttons, remove buttons, item counts
- [x] **LootItem** — `Evaluate()` method, wishlisted items rendered cyan with ★ suffix
- [x] **Config**: `LootShowMeds`, `LootShowFood`, `LootShowBackpacks`, `LootShowKeys`, `LootShowWishlist`
- [x] **SKPaints** — `LootWishlist`, `LootWishlistDimmed`, `TooltipWishlist` (all cyan)

### 3F. Web Radar Server ✅
- [x] **WebRadarServer** (`Web/WebRadar/WebRadarServer.cs`) — Kestrel HTTP server:
  - `/api/radar` JSON endpoint returning full raid state
  - Background worker thread collecting player/loot/exfil data
  - Configurable port, start/stop lifecycle tied to Memory events
- [x] **Data models** — `WebRadarUpdate`, `WebRadarPlayer`, `WebRadarMapInfo`,
  `WebRadarMapConverter`, `WebPlayerType`
- [x] **Config**: `WebRadarEnabled`, `WebRadarPort` in Settings Panel

### 3G. Unity Collections ✅
- [x] **MemArray<T>** (`Unity/Collections/MemArray.cs`) — pooled DMA wrapper for IL2CPP `T[]`
- [x] **MemList<T>** (`Unity/Collections/MemList.cs`) — pooled DMA wrapper for `System.Collections.Generic.List<T>`
- [x] Both use `SharedArray<T>` / `ArrayPool<T>` for zero-alloc read patterns

## Phase 4 — Aimview Widget ✅ (Done)
> FBO-backed SkiaSharp aimview rendered as an ImGui image.

- [x] **AimviewWidget** (`UI/Widgets/AimviewWidget.cs`) — fully implemented:
  - OpenGL FBO + texture for off-screen SkiaSharp rendering
  - Synthetic camera from local player position + rotation (forward/right/up vectors)
  - 3D→2D perspective projection with configurable FOV and range
  - Renders players as colored dots (per-PlayerType colors), loot as price-colored dots,
    corpses as grey dots — all with proper depth sorting
  - Uses `LootFilter.Evaluate()` for consistent loot visibility/color with radar
  - ImGui window wrapping the texture with drag/resize
  - Configurable: FOV, range, player/loot/corpse toggle, size
- [x] **Config**: `AimviewEnabled`, `AimviewFov`, `AimviewRange`, `AimviewShowPlayers`,
  `AimviewShowLoot`, `AimviewShowCorpses`, `AimviewWidth`, `AimviewHeight`
- [x] **Settings Panel** — Aimview section with all toggles and sliders

## Phase 5 — Input, Hotkeys, Raid Lifecycle Hardening & Performance ✅ (Partial — 5A–5C Done)
> DMA-based input, configurable hotkeys, hardened raid lifecycle, and registration worker performance.

### 5A. DMA Input & Hotkeys ✅
- [x] **InputManager** (`DMA/InputManager.cs`) — DMA-based keyboard input via VmmSharpEx:
  - ~100 Hz polling on dedicated worker thread (10ms interval)
  - Win10/Win11 automatic resolution with fallback
  - Edge detection: `IsKeyDown`, `IsKeyUp`, `IsKeyHeld` per-frame state
  - Double-tap detection (300ms window)
  - Safe mode after 3 failed init attempts (graceful degradation)
  - Clean lifecycle: `Initialize()` / `Shutdown()` with CancellationToken
- [x] **HotkeyManager** (`DMA/HotkeyManager.cs`) — configurable key bindings:
  - 5 default bindings: Battle Mode (B), Free Mode (F), Toggle Loot (L), Zoom In/Out (Num+/-)
  - `HotkeyAction` model with getter/setter/callback delegates
  - Live rebinding support (wait-for-keypress flow)
  - `RegisterAll()` / `UnregisterAll()` lifecycle
  - Config-backed via `SilkConfig.HotkeyBattleMode`, etc.
- [x] **Settings Panel** — Hotkeys section in SettingsPanel with rebind buttons

### 5B. Matching Progress Tracking ✅
- [x] **MatchingProgressResolver** (`Tarkov/Unity/IL2CPP/MatchingProgressResolver.cs`):
  - GOM scan by klass pointer (primary) or class name (fallback) to find `MatchingProgressView`
  - Background timer polls `CurrentStage` every 100ms and logs transitions
  - Stage enum: `None → Matching → MapLoading → GamePreparing → GameStarting → GameStarted`
  - High-water mark tracking, per-stage timing, total elapsed timer
  - Integrated into `LocalGameWorld.Create()` loop — polls during pre-raid waiting
  - `NotifyRaidStarted()` freezes timer when raid is confirmed

### 5C. Raid Lifecycle Hardening & Performance ✅
- [x] **Thread-safe disposal** — `Interlocked.Exchange` replaces `bool _disposed`; only first
  caller performs cleanup, second+ callers are no-ops
- [x] **LocalPlayerLost fast-bail** — `RegisteredPlayers.LocalPlayerLost` volatile flag set
  immediately when local player leaves the registered list; both Realtime and Registration
  workers check this flag at tick start, preventing scatter reads against freed transform
  data (was causing native AVE crashes)
- [x] **Stale GameWorld guard** — `_lastDisposedBase` tracks the disposed GameWorld address;
  `Create()` rejects matching addresses on the post-raid menu screen (Unity keeps them alive)
- [x] **Suppress stale guard** — `ClearStaleGuard()` allows user-initiated restarts to
  re-detect the same live GameWorld without waiting for address change
- [x] **Post-raid cooldown** — 12-second cooldown after raid ends prevents rapid re-detection
  of stale GameWorld; `WaitForCooldown()` blocks with `WaitHandle` (no busy-loop)
- [x] **ThrowIfRaidEnded per tick** — runs at the start of every worker tick for sub-8ms
  raid-end detection; 5 retry attempts (50ms total) absorbs transient DMA failures
- [x] **Player recovery speed** — `TestPositionCompute` outputs `Vector3 worldPos` via `out`
  parameter; all transform init paths (`TryInitTransform`, `TryReinitFromTransformInternal`,
  `BatchInitTransforms`) apply position immediately so players appear on radar without
  waiting for the next realtime scatter tick
- [x] **RealtimeEstablished flag** — new field on `PlayerEntry`; gates error-state entry and
  selects reinit threshold: `ReinitThresholdNew=2` for spawning players vs `ReinitThreshold=5`
  for established ones — faster recovery for newly-discovered players
- [x] **Registration worker tick stability** — gear/hands refresh thundering-herd fix:
  - `MaxRefreshesPerTick=2` — caps expensive serial DMA reads per tick
  - Stagger initial gear/hands refresh times via `_staggerIndex` (gear: slot×250ms,
    hands: slot×150ms) so bulk-discovered players don't all fire simultaneously
  - Random jitter (0–2s) on gear refresh interval prevents long-term timer re-alignment
  - Hands refresh now shares the per-tick budget
- [x] **Code quality** — fixed all 15 CS8600/CS8601/CS8625 nullable warnings across IL2CPP
  Dumper (5 files), ProfileService, LootManager; removed redundant usings across 20+ files;
  removed dead PingEffect code from RadarWindow

### 5D. Remaining Phase 5 Work
- [x] `StaticLootContainers` — static container discovery, scatter-batched resolution
  (4-batch Phase 2: MongoID → BSG ID → AllContainers lookup), radar + aimview rendering,
  searched-state filtering, config toggles (ShowContainers, ShowContainerNames, AimviewShowContainers)
- [x] `HotkeyManagerPanel` — standalone ImGui panel extracted from Settings hotkeys tab;
  own open/close toggle in View menu, config-persisted visibility, full rebind + clear UI
- [x] `FeatureManager` port (memory write features) — see Phase 5H below
- [x] Memory writes gated by config flag (`MemWritesEnabled` master toggle + per-feature toggles)
- [x] `QuestManager` & quest rendering on radar — see Phase 5F below
- [x] **Hot-path performance audit & optimizations** \u2014 see Phase 5G below
- [ ] Chams / visual ESP features (requires FeatureManager infrastructure \u2705, pending implementation)
- [ ] `ResourceJanitor` (GC pressure management)

### 5E. Hideout Manager ✅
- [x] **HideoutManager** (`Tarkov/Hideout/HideoutManager.cs`) — hideout stash & area reader:
  - GOM component lookup for HideoutArea + HideoutController with klass caching
  - Scatter-batched stash item reading (grid → slot → item template → EftDataManager lookup)
  - Area level + upgrade requirement reading (items, tools, traders, skills)
  - Per-area `NeededItemIds` / `NeededItemCounts` for upgrade tracking
  - `InvalidatePointers()` — clears GOM pointers on hideout exit while preserving data
  - `Reset()` — full clear on game process stop
  - GOM walk performance: `useCache: true` for all reads (matches WPF ~3.7s discovery time)
- [x] **HideoutPanel** (`UI/Panels/HideoutPanel.cs`) — ImGui stash/upgrade UI:
  - Stash item table with search, grouping, sorting (name/qty/price columns)
  - Area upgrade progress display with requirement breakdown
  - Manual refresh button, auto-refresh on hideout entry (config toggle)
  - Price totals (best/trader/flea)
- [x] **Memory integration** — `Memory.Hideout` singleton, hideout loop with auto-refresh,
  data persists across hideout→raid transitions (only pointers invalidated on exit)
- [x] **Config**: `HideoutEnabled`, `HideoutAutoRefresh` toggles in Settings Panel

### 5F. Quest System ✅
- [x] **QuestManager** (`Tarkov/GameWorld/Quests/QuestManager.cs`) — reads active quests from
  local player profile via scatter reads; resolves `ConditionZone`/`ConditionVisitPlace`
  conditions per map; rate-limited 2s refresh; exposes `LocationConditions` list and
  `IsItemRequired(bsgId)` for loot integration
- [x] **QuestLocation** (`Tarkov/GameWorld/Quests/QuestLocation.cs`) — quest zone data model
  with `Draw()` (gold dot + name label) and `DrawOutlineProjected()` (polygon outline on radar)
- [x] **TaskElement** (`Misc/Data/TaskElement.cs`) — task/condition data model (task ID,
  trader, map filter, required keys, Kappa flag)
- [x] **QuestPanel** (`UI/Panels/QuestPanel.cs`) — ImGui panel with trader-grouped quest list,
  per-map filter, required keys display, optional objective toggle; opened with Q hotkey
- [x] **LobbyQuestReader** (`DMA/LobbyQuestReader.cs`) — background reader for quest data
  while in lobby; started/stopped with Memory lifecycle; `InvalidateCache()` on game stop
- [x] **LocalPlayer.ProfilePtr** — stored during player discovery for QuestManager initialization
- [x] **Memory integration** — `Memory.QuestManager` + `Memory.QuestLocations` accessors;
  `LobbyQuestReader.Start/Stop/InvalidateCache` wired to `GameStarted`/`GameStopped` events
- [x] **LocalGameWorld integration** — `QuestManager` initialized when local player confirmed;
  `Refresh()` called in `DoSecondaryWork` (rate-limited internally to 2s)
- [x] **Radar rendering** — quest zone layer drawn after containers; respects `BattleMode`
  and `ShowQuests` config; outline polygon rendered behind marker; optional zones filtered
- [x] **LootFilter integration** — `Evaluate()` checks `QuestManager.IsItemRequired(bsgId)`
  and flags matching items as `Important + QuestRequired` (shown highlighted on radar/aimview)
- [x] **SKPaints** — `PaintQuest`, `TextQuest`, `PaintQuestOutlineFill`, `PaintQuestOutlineStroke`
  (gold/amber theme)
- [x] **Unity layout constants** — `ManagedList`, `ManagedArray`, `MongoID`, `IL2CPPHashSet`
  added to `Unity.cs` for QuestManager scatter reads
- [x] **Config** — `ShowQuests`, `QuestKappaFilter`, `QuestShowOptional`, `QuestShowNames`,
  `QuestShowDistance`, `ShowQuestPanel`, `QuestBlacklist` in `SilkConfig`
- [x] **SettingsPanel** — Quest section with Kappa-only, show optional, show names,
  show distance toggles
- [x] **RadarWindow** — Q hotkey toggles QuestPanel; Escape closes QuestPanel;
  `ShowQuestPanel` persisted to config
- [x] **GlobalUsings** — `eft_dma_radar.Silk.Tarkov.GameWorld.Quests` added

### 5G. Hot-Path Performance Optimizations ✅
- [x] **LootItem.IsImportant cached** — replaced per-call `LootFilter.GetDisplayPrice` +
  `LootFilter.IsImportant` recomputation with a cached `_cachedImportant` bool field;
  `RefreshImportance()` called once after construction in `LootManager.ResolveLootBatched`;
  eliminates O(doors × loot) redundant price calculations in `Door.UpdateNearLootFlag` per
  registration worker tick
- [x] **LootWidget LootGroup object pooling** — replaced per-frame `new LootGroup()` heap
  allocations with a pool (`_groupPool` list + `_groupPoolIndex` reset pattern); zero
  allocations after warm-up; `RentGroup()` grows pool to high-water mark automatically
- [x] **LootWidget static color fields** — extracted all inline `new Vector4(r,g,b,a)`
  ImGui color literals to `static readonly` fields (`ColorDistLabel`, `ColorDimLabel`,
  `ColorDimQty`, `ColorDimDash`); eliminates per-row struct construction in tight table loops
- [x] **LootWidget distance/overflow string caching** — `LootGroup.DistText` property caches
  formatted distance string (only reallocates when distance int changes); overflow message
  cached in `_lastOverflow`/`_overflowText` fields
- [x] **LootContainer distance string caching** — added `_cachedDistVal`/`_cachedDistText`
  fields matching the Door/Exfil pattern; eliminates per-frame `$"{(int)dist}m"` allocation
- [x] **RadarWindow container loop** — pass `0f` directly to `container.Draw()` when
  `showDistance=false` instead of computing `Vector3.Distance` for every container

### 5H. Memory Write System (FeatureManager + 21 Features) ✅
- [x] **ScatterWriteHandle** (`DMA/ScatterAPI/ScatterWriteHandle.cs`) — batched DMA write API:
  - `VmmScatter` wrapper with `AddValueEntry<T>`, `Execute(validationFunc)`, `Callbacks` action
  - Validation callback pattern: write → read-back → verify; retry on failure
- [x] **Feature abstractions** (`DMA/Features/`):
  - `IFeature` — base interface with static `_features` ConcurrentBag for auto-registration
  - `IMemWriteFeature` — extends IFeature with `Execute(LocalPlayer, ScatterWriteHandle)`
  - `MemWriteFeature<T>` — abstract singleton base class; `IsActive()` config check,
    `OnRaidStart()` cache invalidation, `SetIfChanged<V>` dedup helper
- [x] **FeatureManager** (`Tarkov/Features/FeatureManager.cs`) — background feature driver:
  - `ModuleInit()` triggers 21 `RuntimeHelpers.RunClassConstructor` calls (singleton registration)
  - Background worker thread: creates `ScatterWriteHandle`, iterates all `IMemWriteFeature`,
    calls `Execute()` on active features, disposes handle each tick
  - Lifecycle: starts on `RaidStarted`, stops on `RaidStopped`; fires `OnRaidStart()` for cache reset
- [x] **EftHardSettingsResolver** (`Tarkov/Unity/IL2CPP/EftHardSettingsResolver.cs`) —
  IL2CPP TypeInfoTable → singleton pointer; used by 8 features
- [x] **LevelSettingsResolver** (`Tarkov/Unity/IL2CPP/LevelSettingsResolver.cs`) —
  GOM-based LevelSettings component lookup; used by FullBright
- [x] **21 memory write features** (`Tarkov/Features/MemoryWrites/`):
  - **Weapon**: NoRecoil (ProceduralWeaponAnimation masks)
  - **Movement**: NoInertia (EFTHardSettings), MoveSpeed (configurable multiplier),
    InfStamina (Physical.Stamina + HandsStamina + Oxygen), FastDuck (POSE_CHANGING_SPEED),
    LongJump (AIR_CONTROL multipliers, configurable), MuleMode (14 writes: Physical +
    MovementContext walk/sprint/overweight), InstantPlant (PlantState.PlantTime),
    MagDrills (Skills.MagDrillsLoadSpeed/UnloadSpeed)
  - **Vision**: NightVision (GOM camera component toggle), ThermalVision (GOM camera
    component toggle), FullBright (LevelSettings ambient color), NoVisor (VisorEffect.Intensity),
    DisableFrostbite (EffectsController→_frostbiteEffect→_opacity),
    DisableInventoryBlur (InventoryBlur _blurCount + _upsampleTexDimension),
    OwlMode (MOUSE_LOOK_HORIZONTAL/VERTICAL_LIMIT)
  - **Interaction**: DisableWeaponCollision (WEAPON_OCCLUSION_LAYERS),
    ExtendedReach (LOOT/DOOR_RAYCAST_DISTANCE, configurable), ThirdPerson
    (HandsContainer.CameraOffset), WideLean (PWA.PositionZeroSum + direction enum),
    MedPanel (MED_EFFECT_USING_PANEL)
- [x] **Config** — `MemWritesConfig` with master `Enabled` toggle + 21 per-feature booleans;
  5 sub-configs: `MoveSpeedConfig`, `FullBrightConfig`, `ExtendedReachConfig`,
  `LongJumpConfig`, `WideLeanConfig`; validation/clamping in `Validate()`
- [x] **Settings Panel** — "Mem Writes" tab with master toggle, grouped sections
  (Weapon, Movement, Vision, Interaction), sliders for configurable features
- [x] **Memory.cs integration** — `FeatureManager.ModuleInit()` in startup,
  `FeatureManager.Start/Stop` wired to raid lifecycle events
- [x] **GOM.GetComponentFromBehaviour** — static method on `GOM` struct for camera
  component lookup; used by NightVision, ThermalVision, NoVisor, DisableFrostbite,
  DisableInventoryBlur

## Phase 6 — Color Picker, Theming & Advanced UI
> Customizable colors and additional panels.

- [ ] `ColorPickerPanel` using ImGui's built-in `ColorEdit3`/`ColorEdit4`
- [ ] Color categories: Players, Loot tiers, UI elements
- [ ] Map Setup Helper panel
- [ ] Debug Info Widget (memory stats, FPS graph)

## Phase 7 — Platform Polish
> Production quality touches.

- [ ] Window icon (Win32 interop)
- [ ] Dark mode title bar (DwmSetWindowAttribute)
- [ ] ImGui layout persistence (imgui.ini)
- [x] ~~Custom ImGui fonts (load from embedded resources)~~ — done (CustomFonts.cs, 13px + symbol merge)
- [ ] ImGui DPI scaling
- [ ] SilkDispatcher for async UI operations

## Phase 8 — WPF Deprecation
> Once Silk.NET is feature-complete, remove the WPF project.

- [ ] Feature parity checklist vs WPF MainWindow
- [ ] ESP overlay (if applicable)
- [x] ~~Web Radar~~ — done (Phase 3F, Kestrel HTTP server)
- [x] ~~Remove WPF ProjectReference from silk `.csproj`~~ — done (Phase 1I)
- [x] ~~Remove WPF project from solution~~ — done (Phase 1I, renamed `src/` → `src-wpf/`)
- [ ] Delete `src-wpf/` entirely once feature parity confirmed

---

## File Structure (current — 121 source files, ~26.1K LOC)

```
src-silk/
├── DMA/
│   ├── Memory.cs                          ← Standalone DMA layer (state machine, worker, R/W API)
│   ├── InputManager.cs                    ← DMA-based keyboard input (~100 Hz, Win10/Win11)
│   ├── HotkeyManager.cs                   ← Configurable hotkey bindings + rebind support
│   ├── LobbyQuestReader.cs                ← Background quest reader for lobby (lifecycle-managed)
│   ├── Features/                          ← Feature abstractions
│   │   ├── IFeature.cs                    ← Base interface + static ConcurrentBag registry
│   │   ├── IMemWriteFeature.cs            ← Memory write feature contract
│   │   └── MemWriteFeature.cs             ← Abstract singleton base (IsActive, OnRaidStart, SetIfChanged)
│   └── ScatterAPI/                        ← Scatter read/write API
│       ├── IScatterEntry.cs
│       ├── MemPointer.cs
│       ├── ScatterReadMap.cs
│       ├── ScatterReadRound.cs
│       ├── ScatterReadIndex.cs
│       ├── ScatterReadEntry.cs
│       └── ScatterWriteHandle.cs          ← Batched DMA write API (VmmScatter wrapper)
├── Tarkov/
│   ├── Offsets.cs                         ← Game SDK offsets (379 fields, IL2CPP-updated)
│   ├── ProfileService.cs                  ← tarkov.dev profile fetcher (KD, hours, SR%)
│   ├── Features/
│   │   ├── FeatureManager.cs              ← Background feature driver (21 features, raid lifecycle)
│   │   └── MemoryWrites/                  ← 21 memory write feature implementations
│   │       ├── NoRecoil.cs                ← PWA shot/breath/walk/moto masks
│   │       ├── NoInertia.cs               ← EFTHardSettings inertia values
│   │       ├── MoveSpeed.cs               ← Configurable speed multiplier
│   │       ├── InfStamina.cs              ← Physical stamina/hands/oxygen
│   │       ├── NightVision.cs             ← Camera component toggle
│   │       ├── ThermalVision.cs           ← Camera component toggle
│   │       ├── FullBright.cs              ← LevelSettings ambient color
│   │       ├── NoVisor.cs                 ← VisorEffect.Intensity
│   │       ├── DisableFrostbite.cs        ← EffectsController frostbite opacity
│   │       ├── DisableInventoryBlur.cs    ← InventoryBlur blur count + upsample
│   │       ├── DisableWeaponCollision.cs  ← WEAPON_OCCLUSION_LAYERS
│   │       ├── ExtendedReach.cs           ← Loot/door raycast distance (configurable)
│   │       ├── FastDuck.cs                ← POSE_CHANGING_SPEED
│   │       ├── LongJump.cs               ← AIR_CONTROL multipliers (configurable)
│   │       ├── ThirdPerson.cs             ← HandsContainer.CameraOffset
│   │       ├── InstantPlant.cs            ← PlantState.PlantTime
│   │       ├── MagDrills.cs               ← Skills mag load/unload speed
│   │       ├── MuleMode.cs                ← 14 writes: Physical + MovementContext
│   │       ├── WideLean.cs                ← PWA.PositionZeroSum + direction
│   │       ├── MedPanel.cs                ← MED_EFFECT_USING_PANEL
│   │       └── OwlMode.cs                 ← Mouse look H/V limits
│   ├── Hideout/
│   │   └── HideoutManager.cs              ← Stash items, area upgrades, klass-cached GOM lookup
│   ├── Unity/
│   │   ├── Unity.cs                       ← UnityOffsets, GOM, ComponentArray, GameObject, TrsX
│   │   ├── ViewMatrix.cs                  ← 4x4 view matrix extraction from camera
│   │   ├── Bones.cs                       ← Bone definitions (16 bones, BoneID enum)
│   │   ├── Collections/
│   │   │   ├── MemArray.cs                ← Pooled DMA wrapper for IL2CPP T[]
│   │   │   └── MemList.cs                 ← Pooled DMA wrapper for List<T>
│   │   └── IL2CPP/
│   │       ├── MatchingProgressResolver.cs ← Pre-raid matching stage tracking + timer
│   │       ├── EftHardSettingsResolver.cs ← IL2CPP TypeInfoTable → singleton pointer
│   │       ├── LevelSettingsResolver.cs   ← GOM-based LevelSettings component lookup
│   │       └── Dumper/                    ← IL2CPP dumper (5 partial files)
│   │           ├── Il2CppDumper.cs
│   │           ├── Il2CppDumperCache.cs
│   │           ├── Il2CppDumperFull.cs
│   │           ├── Il2CppDumperSchema.cs
│   │           └── TypeInfoTableResolver.cs
│   └── GameWorld/
│       ├── LocalGameWorld.cs              ← Raid lifecycle, non-blocking startup, two-tier workers
│       ├── CameraManager.cs               ← ViewMatrix extraction, 3-worker model integration
│       ├── RegisteredPlayers.cs           ← Player collection (partial — core + public API)
│       ├── RegisteredPlayers.Discovery.cs ← Player discovery, classification, registration
│       ├── RegisteredPlayers.Scatter.cs   ← Scatter-batched transform/rotation reads
│       ├── Player/
│       │   ├── Player.cs                  ← Data model: identity, gear, hands, profile, properties
│       │   ├── Player.Draw.cs             ← Rendering: dot, chevron, aimline, labels, shadows
│       │   ├── LocalPlayer.cs             ← Sealed subclass (IsLocalPlayer => true)
│       │   ├── PlayerType.cs              ← Player type enum (10 types)
│       │   ├── GearManager.cs             ← Scatter-batched equipment + dogtag reader
│       │   ├── GearItem.cs                ← Equipment slot model (BSG ID, short name, price)
│       │   ├── HandsManager.cs            ← In-hands item reader (cached, change-detection)
│       │   └── Skeleton.cs                ← Bone transform scatter reads (16 bones, O(1) lookup)
│       ├── Loot/
│       │   ├── LootManager.cs             ← 6-round scatter chain + corpse dogtag/equipment + containers
│       │   ├── LootItem.cs                ← Loot rendering with price tiers, wishlist awareness
│       │   ├── LootContainer.cs           ← Static container model (BSG ID, name, searched state)
│       │   ├── LootCorpse.cs              ← Corpse model with equipment, value, dogtag name
│       │   ├── LootFilter.cs              ← Full pipeline: blacklist→wishlist→category→price→name
│       │   ├── LootFilterData.cs           ← Persistent wishlist/blacklist (FrozenSet lookups)
│       │   └── DogtagCache.cs             ← Persistent ProfileId→AccountId DB + level cache
│       ├── Exits/
│       │   ├── ExfilManager.cs            ← Scatter-batched exfil/transit reader with retry
│       │   ├── Exfil.cs                   ← Exfil point: status, eligibility, cached drawing
│       │   ├── TransitPoint.cs            ← Transit point with static position from JSON data
│       │   ├── ExfilStatus.cs             ← Closed/Pending/Open/Countdown enum
│       │   ├── ExfilNames.cs              ← Per-map friendly name mappings (FrozenDictionary)
│       │   └── MapNames.cs                ← Map ID → display name mapping
│       ├── Interactables/
│       │   ├── InteractablesManager.cs    ← Door discovery + state refresh
│       │   └── Door.cs                    ← Keyed door: state, key identity, near-loot flag
│       └── Quests/
│           ├── QuestManager.cs            ← Scatter-based quest reader, zone resolver, item filter
│           └── QuestLocation.cs           ← Quest zone: dot + outline polygon rendering
├── Config/
│   └── SilkConfig.cs                      ← JSON config + Validate() + debounced save
├── Misc/
│   ├── Misc.cs                            ← Utils, UTF8String, UnicodeString, DmaException
│   ├── Extensions.cs                      ← VA validation, angle math, vector checks
│   ├── Log.cs                             ← Rate-limited logger
│   ├── SizeChecker.cs                     ← Struct size validation
│   ├── Workers/WorkerThread.cs            ← Background worker with DynamicSleep
│   ├── Pools/
│   │   ├── IPooledObject.cs
│   │   └── SharedArray.cs                 ← Pooled buffer with struct Enumerator
│   └── Data/
│       ├── EftDataManager.cs              ← Embedded item + map database (FrozenDictionary)
│       ├── TarkovMarketItem.cs            ← Item model with 9 category helpers
│       └── TaskElement.cs                 ← Quest task/condition data model
├── UI/
│   ├── RadarWindow.cs                     ← Silk.NET window, SkiaSharp GPU + ImGui overlay
│   ├── SKPaints.cs                        ← Immutable per-type paints, shadows, wishlist colors
│   ├── CustomFonts.cs                     ← Embedded font loading
│   ├── Panels/
│   │   ├── SettingsPanel.cs               ← ImGui settings (General, Players, Loot, Map, Quest, Mem Writes tabs)
│   │   ├── LootFiltersPanel.cs            ← Wishlist/blacklist/category filter editor (ImGui)
│   │   ├── HideoutPanel.cs                ← Stash items, area upgrades, search/sort/group (ImGui)
│   │   ├── HotkeyManagerPanel.cs          ← Standalone hotkey editing panel (rebind + clear)
│   │   └── QuestPanel.cs                  ← Quest list grouped by trader, map filter, keys display
│   ├── Widgets/
│   │   ├── PlayerInfoWidget.cs            ← Human hostile table + column-aligned tooltips
│   │   ├── LootWidget.cs                  ← Sortable loot table with wishlist coloring (ImGui)
│   │   └── AimviewWidget.cs               ← FBO-backed 3D aimview (SkiaSharp + ImGui)
│   └── Radar/Maps/
│       ├── IRadarMap.cs                   ← Map interface
│       ├── MapConfig.cs, MapParams.cs     ← Map math
│       ├── MapManager.cs                  ← Map loading
│       └── RadarMap.cs                    ← Map rendering
├── Web/
│   └── WebRadar/
│       ├── WebRadarServer.cs              ← Kestrel HTTP server (/api/radar JSON endpoint)
│       └── Data/
│           ├── WebRadarUpdate.cs           ← Full raid state snapshot
│           ├── WebRadarPlayer.cs           ← Player data for web clients
│           ├── WebRadarMapInfo.cs          ← Map metadata
│           ├── WebRadarMapConverter.cs     ← Map coordinate conversion
│           ├── WebPlayerType.cs            ← Web-friendly player type enum
│           ├── WebRadarLootItem.cs         ← Loot snapshot for web clients
│           ├── WebRadarCorpse.cs           ← Corpse snapshot for web clients
│           ├── WebRadarContainer.cs        ← Container snapshot for web clients
│           └── WebRadarExfil.cs            ← Exfil snapshot for web clients
├── Docs/
│   ├── MIGRATION_ROADMAP.md               ← This file
│   └── DEBUG_OUTPUT_REFERENCE.md          ← Annotated live debug output reference
├── GlobalUsings.cs                        ← SkiaSharp, System.Numerics, SDK alias
├── Program.cs                             ← Entry point, high-perf mode, P/Invoke
└── DEFAULT_DATA.json                      ← Embedded item + map database resource
```

## Key Principles
1. **Don't copy the WPF Memory.cs** — write fresh, keep it simple, extend as needed
2. **Import self-contained subsystems as-is** — IL2CPP dumper, ScatterAPI, SDK offsets
   don't need rewriting; just copy and adjust namespaces
3. **Each phase should build and run** — no half-broken intermediate states
4. **VmmSharpEx is shared** — both projects reference `lib/VmmSharpEx` directly
5. **Pull, don't push** — only bring WPF code into silk when a phase specifically needs it
6. **The WPF project stays working** — silk migration doesn't break the existing app
7. **Zero-alloc in hot paths** — cache paints, use struct enumerators, manual loops over LINQ
8. **FrozenDictionary / FrozenSet for immutable lookups** — item DB, exfil names, loot filters

## Reference
- WPF project: `src-wpf/` (renamed from `src/`, removed from solution, still functional standalone)
- WPF `Memory.cs`: `src-wpf/DMA/Memory.cs` — reference for read/write API surface
- WPF `Player.cs`: `src-wpf/Tarkov/GameWorld/Player/Player.cs` — reference for full player model
- WPF `LocalGameWorld.cs`: `src-wpf/Tarkov/GameWorld/LocalGameWorld.cs` — reference for raid lifecycle
- ImGui.NET docs: https://github.com/ImGuiNET/ImGui.NET
