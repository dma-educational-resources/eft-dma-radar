# WPF → Silk.NET Migration Roadmap

## Current State
**Phase 2 complete.** `src-silk` is a fully standalone executable with a full player model
(gear, dogtag identity, profile lookups), aimlines, hover tooltips, and polished rendering.

- **Silk.NET project** (`src-silk`): Silk.NET + SkiaSharp + ImGui window — **running independently**
  - Own `Memory.cs` (DMA layer): state machine, worker thread, full scatter read/write API
  - Own IL2CPP dumper (`src-silk/Tarkov/Unity/IL2CPP/Dumper/`) — silk-native namespace,
    isolated cache at `%AppData%\eft-dma-radar-silk\il2cpp_offsets.json`
  - Own `Offsets.cs` (game SDK) — all 379 offsets, fully independent from WPF `SDK.cs`
  - Own `Unity.cs` — IL2CPP engine constants, `GOM`, `ComponentArray`, `GameObject`, `TrsX`,
    `UnityOffsets` (named constants replacing magic numbers throughout)
  - Own game model:
    - `Tarkov/GameWorld/Player/Player.cs` — base player class with identity, gear, profile stats
    - `Tarkov/GameWorld/Player/Player.Draw.cs` — rendering: dot, chevron, aimline, labels
    - `Tarkov/GameWorld/Player/LocalPlayer.cs` — sealed subclass, `IsLocalPlayer => true`
    - `Tarkov/GameWorld/Player/PlayerType.cs` — player type enum (10 types)
    - `Tarkov/GameWorld/Player/GearManager.cs` — scatter-batched equipment/dogtag reader
    - `Tarkov/GameWorld/Player/GearItem.cs` — equipment slot model
    - `Tarkov/GameWorld/RegisteredPlayers.cs` — player collection (partial class)
    - `Tarkov/GameWorld/RegisteredPlayers.Discovery.cs` — player discovery & classification
    - `Tarkov/GameWorld/RegisteredPlayers.Scatter.cs` — scatter-batched transform reads
    - `Tarkov/GameWorld/LocalGameWorld.cs` — raid lifecycle, non-blocking startup, two-tier workers
    - `Tarkov/GameWorld/Loot/LootManager.cs` — loose loot + corpse dogtag extraction
    - `Tarkov/GameWorld/Loot/LootItem.cs` — loot rendering with price tiers + shadow
    - `Tarkov/GameWorld/Loot/DogtagCache.cs` — persistent ProfileId→AccountId database
    - `Tarkov/GameWorld/Loot/LootFilter.cs` — price source, per-slot, threshold filtering
    - `Tarkov/ProfileService.cs` — tarkov.dev profile fetcher (KD, hours, survival rate)
  - Own data layer:
    - `Misc/Data/EftDataManager.cs` — embedded item database (FrozenDictionary)
    - `Misc/Data/TarkovMarketItem.cs` — minimal item model
  - Own map system mirroring WPF structure:
    - `UI/Radar/Maps/IRadarMap.cs`, `IMapEntity.cs` — interfaces
    - `UI/Radar/Maps/MapConfig.cs`, `MapParams.cs`, `MapManager.cs`, `RadarMap.cs`
  - Own `SilkConfig` (`%AppData%\eft-dma-radar-silk\config.json`)
  - Own `SKPaints.cs`, `CustomFonts.cs` — blur-based text shadows, loot shadows
  - `RadarWindow` draws via `Player.Draw()` — rendering logic lives on the player, not the window
  - Hover tooltips on radar canvas (SkiaSharp) + PlayerInfoWidget (ImGui) with column-aligned layout
  - **No WPF ProjectReference** — fully standalone
- **WPF project** (`src-wpf`): renamed from `src`, removed from solution, still functional standalone

## Architecture Goals
- **Standalone `src-silk`**: Own `Memory.cs`, `Offsets.cs`, `Unity.cs`, loot, data — no WPF reference ✅
- **Non-blocking startup**: Workers start immediately; local player discovered in background;
  radar shows "Waiting for Raid Start" until position available, then transitions seamlessly ✅
- **Start minimal**: Map render, player positions/rotations, raid begin/end — nothing more ✅
- **Separation of concerns**: DMA layer, game model, UI are distinct layers ✅
- **Graceful lifecycle**: Proper state machine, error recovery, clean restart ✅
- **Incremental migration**: Pull features from WPF project one at a time as needed
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
| ~~Full Player model (Gear, Hands, Health)~~ | ~~Phase 2~~ | ✅ Done (Phase 2) |
| ~~Loot system (LootManager, FilteredLoot)~~ | ~~Phase 3~~ | ✅ Done (Phase 1) |
| Exits, Explosives, Quests | Phase 3 | ❌ Not started |
| ~~EftDataManager (item database)~~ | ~~Phase 3~~ | ✅ Done (Phase 1) |
| FeatureManager (chams, memory writes) | Phase 5+ | ❌ Not started |
| ~~Config system (multi-profile, IConfig)~~ | ~~Phase 2~~ | ✅ Done (Phase 2 — simplified) |
| ~~Own SKPaints (remove WPF project ref)~~ | ~~Phase 2~~ | ✅ Done (Phase 1) |
| ResourceJanitor (GC pressure mgmt) | Phase 4+ | ❌ Not started |
| HideoutManager | Phase 6+ | ❌ Not started |
| InputManager (DMA-based input) | Phase 5 | ❌ Not started |

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

## Phase 3 — Exits, Containers & Advanced Loot
> Complete game world state beyond loose loot.

- [x] ~~`LootManager` (loose loot)~~ — done (6-round scatter chain, ObservedLootItem)
- [x] ~~`LootFilter` (price filtering)~~ — done (Phase 1, price source/per-slot/threshold)
- [x] ~~`LootWidget` (ImGui table)~~ — done (Phase 1, sortable loot table)
- [x] ~~`LootFiltersPanel` (ImGui editor)~~ — done (Phase 1)
- [ ] `StaticLootContainers` — containers with item lists
- [ ] `ExitPoints`, `Explosives`
- [ ] `QuestManager` & quest rendering on radar
- [ ] `CameraManager` for aimview prep

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
- [x] ~~Remove WPF ProjectReference from silk `.csproj`~~ — done (Phase 1I)
- [x] ~~Remove WPF project from solution~~ — done (Phase 1I, renamed `src/` → `src-wpf/`)
- [ ] Delete `src-wpf/` entirely once feature parity confirmed

---

## File Structure (current)

```
src-silk/
├── DMA/
│   ├── Memory.cs                         ← Standalone DMA layer (~750 lines)
│   └── ScatterAPI/                       ← Scatter read/write API
│       ├── IScatterEntry.cs
│       ├── MemPointer.cs
│       ├── ScatterReadMap.cs
│       ├── ScatterReadRound.cs
│       ├── ScatterReadIndex.cs
│       └── ScatterReadEntry.cs
├── Tarkov/
│   ├── Offsets.cs                        ← Game SDK offsets (379 fields, IL2CPP-updated)
│   ├── ProfileService.cs                ← tarkov.dev profile fetcher (KD, hours, SR%)
│   ├── Unity/
│   │   ├── Unity.cs                      ← UnityOffsets, GOM, ComponentArray, GameObject, TrsX
│   │   └── IL2CPP/Dumper/                ← IL2CPP dumper (5 partial files)
│   │       ├── Il2CppDumper.cs
│   │       ├── Il2CppDumperCache.cs
│   │       ├── Il2CppDumperFull.cs
│   │       ├── Il2CppDumperSchema.cs
│   │       └── TypeInfoTableResolver.cs
│   └── GameWorld/
│       ├── LocalGameWorld.cs             ← Raid lifecycle, non-blocking startup, two-tier workers
│       ├── RegisteredPlayers.cs          ← Player collection (partial — core + public API)
│       ├── RegisteredPlayers.Discovery.cs ← Player discovery, classification, registration
│       ├── RegisteredPlayers.Scatter.cs  ← Scatter-batched transform/rotation reads
│       ├── Player/
│       │   ├── Player.cs                 ← Data model: identity, gear, profile, properties
│       │   ├── Player.Draw.cs            ← Rendering: dot, chevron, aimline, labels, shadows
│       │   ├── LocalPlayer.cs            ← Sealed subclass (IsLocalPlayer => true)
│       │   ├── PlayerType.cs             ← Player type enum (10 types)
│       │   ├── GearManager.cs            ← Scatter-batched equipment + dogtag reader
│       │   └── GearItem.cs               ← Equipment slot model (BSG ID, short name, price)
│       └── Loot/
│           ├── LootManager.cs            ← 6-round scatter chain + corpse dogtag extraction
│           ├── LootItem.cs               ← Loot rendering with price tiers + shadow
│           ├── LootFilter.cs             ← Price source, per-slot, threshold filtering
│           └── DogtagCache.cs            ← Persistent ProfileId→AccountId DB + level cache
├── Config/
│   └── SilkConfig.cs                     ← JSON config (%AppData%\eft-dma-radar-silk\)
├── Misc/
│   ├── Misc.cs                           ← Utils, UTF8String, UnicodeString, BadPtrException
│   ├── Extensions.cs                     ← VA validation, angle math, vector checks
│   ├── Log.cs                            ← Rate-limited logger
│   ├── SizeChecker.cs                    ← Struct size validation
│   ├── Workers/WorkerThread.cs           ← Background worker with DynamicSleep
│   ├── Pools/                            ← IPooledObject, SharedArray (used by ScatterAPI)
│   └── Data/
│       ├── EftDataManager.cs             ← Embedded item database (FrozenDictionary)
│       └── TarkovMarketItem.cs           ← Minimal item model (BSG ID, name, prices)
├── UI/
│   ├── RadarWindow.cs                    ← Silk.NET window, SkiaSharp GPU + ImGui overlay
│   ├── SKPaints.cs                       ← Shared paint instances (shadows, player/loot colors)
│   ├── CustomFonts.cs                    ← Embedded font loading
│   ├── Panels/
│   │   ├── SettingsPanel.cs              ← ImGui settings (General, Players, Map tabs)
│   │   └── LootFiltersPanel.cs           ← Loot filter editor (ImGui)
│   ├── Widgets/
│   │   ├── PlayerInfoWidget.cs           ← Human hostile table + column-aligned tooltips
│   │   └── LootWidget.cs                 ← Sortable loot table (ImGui)
│   └── Radar/Maps/
│       ├── IRadarMap.cs, IMapEntity.cs   ← Interfaces
│       ├── MapConfig.cs, MapParams.cs    ← Map math
│       ├── MapManager.cs                 ← Map loading
│       └── RadarMap.cs                   ← Map rendering
├── GlobalUsings.cs                       ← SkiaSharp, System.Numerics, SDK alias
├── Program.cs                            ← Entry point, high-perf mode, P/Invoke
├── DEFAULT_DATA.json                     ← Embedded item database resource
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
- WPF project: `src-wpf/` (renamed from `src/`, removed from solution, still functional standalone)
- WPF `Memory.cs`: `src-wpf/DMA/Memory.cs` — reference for read/write API surface
- WPF `Player.cs`: `src-wpf/Tarkov/GameWorld/Player/Player.cs` — reference for full player model
- WPF `LocalGameWorld.cs`: `src-wpf/Tarkov/GameWorld/LocalGameWorld.cs` — reference for raid lifecycle
- ImGui.NET docs: https://github.com/ImGuiNET/ImGui.NET
