# EFT DMA Radar — Game Update & Developer Reference Guide

> **Target audience:** Developers maintaining this radar after an EFT patch.  
> Everything here is keyed to the actual source files so you always know exactly where to look.

---

## Table of Contents

1. [How the Radar Works (Quick Concept Map)](#1-how-the-radar-works-quick-concept-map)
2. [File & Folder Reference](#2-file--folder-reference)
3. [The Offset System — How It All Fits Together](#3-the-offset-system--how-it-all-fits-together)
4. [What Breaks on a Game Update (Checklist)](#4-what-breaks-on-a-game-update-checklist)
5. [Step-by-Step Update Procedure](#5-step-by-step-update-procedure)
6. [SDK.cs — Master Offset File (Detailed)](#6-sdkcs--master-offset-file-detailed)
7. [SDK_Manual.cs — Enums, Structs & Computed Chains](#7-sdk_manualcs--enums-structs--computed-chains)
8. [UnityOffsets.cs — Unity Engine Offsets](#8-unityoffsetscs--unity-engine-offsets)
9. [Special Struct — TypeInfoTable & TypeIndex Values](#9-special-struct--typeinfotable--typeindex-values)
10. [Auto-Resolution System (Il2CppDumper + Sig-Scan)](#10-auto-resolution-system-il2cppdumper--sig-scan)
11. [il2cpp_offsets.json — Offset Cache File](#11-il2cpp_offsetsjson--offset-cache-file)
12. [Memory Write Features](#12-memory-write-features)
13. [How to Find New Offsets with IDA / dumper Tools](#13-how-to-find-new-offsets-with-ida--dumper-tools)
14. [Enum Update Reference](#14-enum-update-reference)
15. [Common Crash / Failure Patterns & Fixes](#15-common-crash--failure-patterns--fixes)

---

## 1. How the Radar Works (Quick Concept Map)

```
    DMA card
        │
        ▼
  VmmSharp / LeechCore
  (kernel-level memory read/write)
        │
        ▼
   MemDMA.cs  ───► reads EFT process RAM
        │
        ▼
   LocalGameWorld.cs  ─── follows pointer chains defined in SDK.cs
        │           ├── RegisteredPlayers.cs   (players on map)
        │           ├── LootManager.cs         (loot items)
        │           ├── ExitManager.cs         (exfil points)
        │           └── ExplosivesManager.cs   (grenades / tripwires)
        │
        ▼
   UI (Radar overlay / ESP form / Web radar)
```

The radar **never injects code** into EFT.  
It reads memory from the side via a DMA card.  
All pointer offsets that describe EFT's internal IL2CPP class layout live in `SDK.cs`.

---

## 2. File & Folder Reference

### Core Entry Points

| File | Purpose |
|------|---------|
| `src/Program.cs` | Global `using` declarations, assembly metadata, `ApplicationMode` enum |
| `src/SharedProgram.cs` | Singleton init, HTTP client, high-performance mode setup, dependency verification |

### DMA Layer (`src/DMA/`)

| File | Purpose |
|------|---------|
| `DMA/MemDMA.cs` | Main DMA interface — wraps VmmSharp, exposes `Memory` static. Fires `GameStarted / GameStopped / RaidStarted / RaidStopped` events |
| `DMA/MemDMABase.cs` | Abstract base with process-waiting helpers, `InRaid`, `LocalPlayer`, `Ready` properties |
| `DMA/MemPointer.cs` | Cached pointer with validity helpers |
| `DMA/ScatterAPI/ScatterReadMap.cs` | Batches multiple memory reads into a single scatter read |
| `DMA/ScatterAPI/ScatterReadRound.cs` | One "round" of scatter reads |
| `DMA/ScatterAPI/ScatterWriteHandle.cs` | Batches memory writes |
| `DMA/Features/IFeature.cs` | Interface for all memory-write features |
| `DMA/Features/MemWriteFeature.cs` | Base class for memory-write features (singleton pattern) |
| `DMA/Features/MemPatchFeature.cs` | Base class for byte-patch features |

### Tarkov Game Layer (`src/Tarkov/`)

| File | Purpose |
|------|---------|
| `Tarkov/SDK.cs` | ⭐ **Master offset file** — all IL2CPP class field offsets, enums, and type indices. **Update this first after a patch.** |
| `Tarkov/SDK_Manual.cs` | Manual additions to SDK: enums with `[Description]` attrs, computed pointer chains, blittable structs (`Types.*`) |
| `Tarkov/GameWorld/LocalGameWorld.cs` | Represents one active raid — owns all sub-managers. Spawns worker threads T1–T5 |
| `Tarkov/GameWorld/RegisteredPlayers.cs` | Reads the `RegisteredPlayers` list; builds `Player` and `ObservedPlayer` objects |
| `Tarkov/GameWorld/Loot/LootManager.cs` | Reads `LootList` from game memory, builds `LootItem` / `LootContainer` objects |
| `Tarkov/GameWorld/Exits/ExitManager.cs` | Reads exfil controller, builds `Exfil` and `TransitPoint` objects |
| `Tarkov/GameWorld/Explosives/ExplosivesManager.cs` | Reads grenade and tripwire objects |
| `Tarkov/GameWorld/WorldInteractablesManager.cs` | Reads doors and interactive objects |
| `Tarkov/GameWorld/Player/LocalPlayer.cs` | Represents the player running the radar (YOU) |
| `Tarkov/GameWorld/Player/Player.cs` | Base player class (movement, health, inventory) |
| `Tarkov/GameWorld/Player/ObservedPlayer.cs` | Remote (observed) players visible on map |
| `Tarkov/GameWorld/Player/ClientPlayer.cs` | Client-side player variant |
| `Tarkov/GameWorld/Player/BtrOperator.cs` | BTR vehicle operator class |
| `Tarkov/GameWorld/CameraManager.cs` | Resolves the in-game camera for ESP |
| `Tarkov/GameWorld/QuestManager.cs` | Quest tracking (v1 path) |
| `Tarkov/GameWorld/QuestManagerV2.cs` | Quest tracking (v2 — task condition counters) |

### Player Plugins (`src/Tarkov/GameWorld/Player/Plugins/`)

| File | Purpose |
|------|---------|
| `FirearmManager.cs` | Reads weapon in hands, fireport position, COI |
| `HandsManager.cs` | Reads hands controller (item in hands) |
| `GearManager.cs` | Reads equipped gear slots |
| `PlayerProfile.cs` | Reads profile info (nickname, level, side, account ID) |
| `Skeleton.cs` | Reads bone transform chains for ESP bone drawing |
| `PlayerListWorker.cs` | Background worker that keeps the player list fresh |
| `TeammatesWorker.cs` | Tracks group/team members |
| `GuardManager.cs` | AI guard logic |
| `HighAlert.cs` | High-alert AI state detection |

### Memory Write Features (`src/Tarkov/Features/MemoryWrites/`)

Each file is one feature. All extend `MemWriteFeature<T>` or `MemPatchFeature<T>`.

| File | What It Writes |
|------|---------------|
| `NoRecoil.cs` | `BreathEffector.Intensity`, `NewShotRecoil.IntensitySeparateFactors` |
| `NoVisor.cs` | `VisorEffect.Intensity` |
| `ThermalVision.cs` | `ThermalVision.*` flags |
| `Nightvision.cs` | `NightVision._on` |
| `MoveSpeed.cs` | `MovementContext.StateSpeedLimit` |
| `InfiniteStamina.cs` | `PhysicalValue.Current` (stamina / oxygen) |
| `MagDrills.cs` | `SkillManager.MagDrillsLoadSpeed` |
| `NoInertia.cs` | `MovementContext.WalkInertia`, `SprintBrakeInertia` |
| `WideLean.cs` | `EFTHardSettings.MOUSE_LOOK_HORIZONTAL_LIMIT` |
| `FastWeaponOps.cs` | Weapon operation speed multipliers |
| `NoWepMalfPatch.cs` | `WeaponTemplate.AllowJam/Feed/Misfire/Slide` |
| `RageMode.cs` | Combines no-recoil + speed + stamina into a single mode |
| `MuleMode.cs` | Overweight / encumbered state flags |
| `BigHead.cs` | Head bone scale write |
| `LongJump.cs` | `SkillManager.StrengthBuffJumpHeightInc` |
| `ClearWeather.cs` | `WeatherDebug.*` |
| `TimeOfDay.cs` | `TOD_CycleParameters.Hour` |
| `FullBright.cs` | `LevelSettings.AmbientMode` |
| `DisableGrass.cs` | Grass renderer writes |
| `InstantPlant.cs` | `MovementState.PlantTime` |
| `FastDuck.cs` | `EFTHardSettings.POSE_CHANGING_SPEED` |
| `DisableInventoryBlur.cs` | `InventoryBlur._blurCount` |
| `DisableHeadBobbing.cs` | `ProceduralWeaponAnimation` mask |
| `DisableFrostBite.cs` | `FrostbiteEffect._opacity` |
| `ExtendedReach.cs` | `EFTHardSettings.LOOT_RAYCAST_DISTANCE` |
| `LootThroughWalls.cs` | `EFTHardSettings.WEAPON_OCCLUSION_LAYERS` |
| `ThirdPerson.cs` | Camera offset write |
| `DisableWeaponCollision.cs` | Weapon collider flags |
| `Aimbot.cs` | Aim direction write |
| `Antiafk.cs` | `AfkMonitor.Delay` |
| `HideRaidCode.cs` | Hides raid code from UI |
| `MedPanel.cs` | `EFTHardSettings.MED_EFFECT_USING_PANEL` |
| `OwlMode.cs` | Vertical look limit removal |
| `Chams/` | Shader-based chams (player highlighting) |

### Unity Layer (`src/Tarkov/Unity/`)

| File | Purpose |
|------|---------|
| `UnityOffsets.cs` | Unity engine structural offsets (GameObject, Component, Transform, etc.) — **separate from EFT class offsets** |
| `UnityCore.cs` | Blittable structs: `BaseObject`, `GameObject`, `ComponentArray` |
| `UnityTransform.cs` | Transform/bone reading helpers |
| `Bones.cs` | Bone index enum (HumanBoneIndex) |
| `Behaviour.cs` | MonoBehaviour enable/disable helpers |
| `CameraManagerBase.cs` | Camera matrix reading |
| `PhysXManager.cs` | PhysX-related reads |
| `Monolib.cs` | Mono/IL2CPP library utilities |
| `Collections/MemList.cs` | Reads IL2CPP `List<T>` |
| `Collections/MemArray.cs` | Reads IL2CPP arrays |
| `Collections/MemDictionary.cs` | Reads IL2CPP `Dictionary<K,V>` |
| `Collections/UnityList.cs` | Unity-specific list variant |
| `Collections/UnityHashSet.cs` | Unity-specific hash set |
| `LowLevel/LowLevelCache.cs` | Low-level read cache |
| `LowLevel/ShellKeeper.cs` | Native shell/hook state |
| `LowLevel/Types/RemoteBytes.cs` | Remote raw byte reads |
| `LowLevel/Types/MonoString.cs` | Reads IL2CPP managed strings |
| `LowLevel/Hooks/NativeOffsets.cs` | RVA stubs for a native hook system — **currently unused** (hook never initialized) |
| `LowLevel/Hooks/NativeMethods.cs` | Wrappers around the hook — **currently unused** |
| `LowLevel/Hooks/Il2cppNativeHook.cs` | PlayerLoop hook installer — **currently unused** |
| `LowLevel/Hooks/Il2cppNativeMethods.cs` | IL2CPP native method stubs — **currently unused** |

### IL2CPP Resolver Layer (`src/Tarkov/Unity/IL2CPP/`)

| File | Purpose |
|------|---------|
| `Il2CppDumper.cs` | Entry point — orchestrates the full dump/resolve pipeline |
| `Il2CppDumperCache.cs` | Saves / loads `il2cpp_offsets.json` (build-fingerprinted cache) |
| `Il2CppDumperSchema.cs` | JSON schema models for the cache |
| `TypeInfoTableResolver.cs` | Sig-scans `GameAssembly.dll` to find `TypeInfoTableRva` automatically |
| `Il2CppClass.cs` | Reads a live `Il2CppClass*` from memory (name, fields, methods) |
| `EftHardSettingsResolver.cs` | Resolves `EFTHardSettings` singleton via TypeIndex lookup |
| `WeatherResolver.cs` | Resolves `WeatherController` singleton |
| `GpuResolver.cs` | Resolves `GPUInstancerManager` singleton |
| `GameWorldExtensions.cs` | Extension helpers for GameWorld pointer chains |
| `LevelSettingsResolver.cs` | Resolves `LevelSettings` from scene |
| `MatchingProgressResolver.cs` | Resolves `MatchingProgressView` for lobby stage display |
| `UnityStructures.cs` | Blittable IL2CPP runtime structs |

### Misc / Config (`src/Misc/`)

| File | Purpose |
|------|---------|
| `Data/OffsetManager.cs` | Runtime offset parser — reads `SDK.cs` text and loads `const uint` values into a dictionary for hot-reload without recompilation |
| `Data/GameData.cs` | Item database, map data |
| `Data/EftDataManager.cs` | Manages EFT data files (items, quests, etc.) |
| `Data/TarkovMarket/` | Flea market price fetching via tarkov.dev API |
| `Config/IConfig.cs` | Config interface |
| `Config/HotKeyManager.cs` | Hotkey binding system |
| `Extensions.cs` | General C# extension methods |
| `LoneLogging.cs` | File + console logging wrapper |
| `Native.cs` | Win32 P/Invoke declarations |

### UI Layer

| Folder | Purpose |
|--------|---------|
| `UI/Radar/` | 2D radar map rendering (SkiaSharp) |
| `UI/ESP/` | ESP overlay window (WinForms, SkiaSharp) |
| `UI/Skia/` | Reusable Skia widgets (player info, loot, quest info, aimview, etc.) |
| `UI/Pages/` | WPF settings pages |
| `UI/Loot/` | Loot filter UI logic |
| `UI/Misc/` | Shared UI helpers (colors, converters, monitors, config binding) |

### Web Radar (`src/Web/`)

| File | Purpose |
|------|---------|
| `WebRadar/WebRadarServer.cs` | Hosts a local HTTP/WebSocket server; streams radar data to a browser |
| `WebRadar/Data/` | JSON-serializable data models for the web stream |
| `ProfileApi/` | EFT profile lookup API client |

---

## 3. The Offset System — How It All Fits Together

EFT is built on **Unity IL2CPP**. IL2CPP compiles C# to native code but retains metadata. The radar reads EFT's heap directly by following pointer chains. Every number like `0x190` in the code means:

> *"At byte offset 0x190 from the base address of this object, there is a pointer (or value) for the next thing."*

The offset system has **three layers**, updated in different ways:

```
┌─────────────────────────────────────────────────────────────────────┐
│ Layer 1 — SDK.cs                                                    │
│   EFT class field offsets (Player, GameWorld, Inventory, etc.)      │
│   Updated manually after each EFT patch using a dump tool.         │
│   File: src/Tarkov/SDK.cs                                           │
├─────────────────────────────────────────────────────────────────────┤
│ Layer 2 — SDK_Manual.cs                                             │
│   Enums, computed pointer chains, blittable structs.                │
│   Updated manually if EFT adds/removes enums or changes struct      │
│   layouts. Usually more stable than layer 1.                        │
│   File: src/Tarkov/SDK_Manual.cs                                    │
├─────────────────────────────────────────────────────────────────────┤
│ Layer 3 — UnityOffsets.cs                                           │
│   Unity Engine internal structure offsets (GameObject, Transform,   │
│   Component, etc.). Changes only when Unity version changes.        │
│   File: src/Tarkov/Unity/UnityOffsets.cs                            │
└─────────────────────────────────────────────────────────────────────┘
```

Additionally, two **runtime-resolved** values exist:

| Value | Location | How Resolved |
|-------|----------|-------------|
| `Offsets.Special.TypeInfoTableRva` | `SDK.cs` → `Special` struct | **Auto** via sig-scan (`TypeInfoTableResolver.cs`); hardcoded value is the fallback. `il2cpp_offsets.json` cache also auto-invalidates when this changes. |
| `Offsets.Special.*_TypeIndex` (5 values) | `SDK.cs` → `Special` struct | Manually looked up; used to index into the TypeInfoTable to find singleton class pointers |

---

## 4. What Breaks on a Game Update (Checklist)

After BSG releases a patch, work through this list **top to bottom**:

- [ ] **1. `il2cpp_offsets.json` — nothing to do.** The cache fingerprints itself with `TypeInfoTableRva`. When the game updates the sig-scanner resolves a new RVA, `TryLoadCache()` detects the mismatch (`cache.TypeInfoTableRva != expectedRva`) and discards the cache automatically, triggering a fresh live dump. Manual deletion is only ever needed if the file itself is corrupt.
- [ ] **2. `Offsets.Special.TypeInfoTableRva` — automatic.** The sig-scanner in `TypeInfoTableResolver.cs` updates this at runtime on every startup. Only intervene manually if the log shows `WARNING: All TypeInfoTable resolution strategies failed` (see §9).
- [ ] **3. Update `Offsets.Special.*_TypeIndex`** — six values that are indices into the TypeInfoTable for key singletons. Find via IL2CPP dump.
- [ ] **4. `Offsets.EFTCameraManager.GetInstance_RVA` — update when possible, not breaking.** `FindCameraManagerInstance()` reads 128 bytes from this RVA and dynamically parses the function body for RIP-relative patterns to locate the class metadata pointer — so the instance resolution itself is dynamic. If the RVA is stale the primary path fails gracefully and the camera system automatically falls back to `TryResolveViaAllCamerasByName()` (Unity AllCameras list + GameObject name search). The log will print `Update GetInstance_RVA! Current: 0x...` as a hint. Update it to restore the faster primary path.
- [ ] **5. Update class field offsets in `SDK.cs`** — use an IL2CPP dumper tool. Focus on classes listed in the output log as "may be stale".
- [ ] **6. Update `UnityOffsets.cs`** — only if Unity version changed (rare; check Unity version in EFT release notes).
- [ ] **7. Update enums in `SDK.cs` and `SDK_Manual.cs`** — e.g., `WildSpawnType` when new bosses are added.
- [ ] **8. Rebuild and test** — check the Output window for `[Il2CppDumper]` and `[EftHardSettingsResolver]` log lines.

---

## 5. Step-by-Step Update Procedure

### Tools needed
- **Il2CppDumper** (Perfare) or **Il2CppInspector** — dump `GameAssembly.dll` + `global-metadata.dat`
- **IDA Pro / Ghidra** — for manual RVA hunting when needed
- **dnSpy / dotPeek** — to browse the C# metadata from the dump

### Step 1 — Get a fresh dump

```
Il2CppDumper.exe GameAssembly.dll global-metadata.dat output/
```

This produces:
- `dump.cs` — all class definitions with field offsets as comments
- `script.json` — method RVAs
- `stringliteral.json` — string literals

### Step 2 — Update `SDK.cs` field offsets

Open `dump.cs` and for every struct in `SDK.cs`, search the corresponding class name. Compare field offsets. Example:

In `SDK.cs`:
```csharp
public readonly partial struct Player
{
    public static uint MovementContext = 0x60;
    public static uint Profile = 0x900;
    ...
}
```

In `dump.cs` (search `// Namespace: EFT   class Player`):
```
// Fields (0x0 = object header)
// +0x10  : ...
// +0x60  : MovementContext_k__BackingField  ← verify this matches 0x60
// +0x900 : Profile_k__BackingField
```

Update any offsets that have changed.

> **Tip:** The comment on each line in `SDK.cs` (e.g., `// _MovementContext_k__BackingField`) shows the original C# field name — search that name in `dump.cs` for a quick match.

### Step 3 — Update `Offsets.Special.TypeInfoTableRva`

The radar auto-scans for this on startup via `TypeInfoTableResolver.cs`. Check the log output:
```
[Il2CppDumper] TypeInfoTableRva UPDATED: 0x5AA0000 → 0x5BB1234
```
If it says `WARNING: All TypeInfoTable resolution strategies failed`, find it manually:

In IDA, search for the `il2cpp_TypeInfoTable` xref or look for the write pattern:
```
mov [rip+XXXXXXXX], rax    ; stores table pointer
```
Compute the RVA = target VA − `GameAssembly.dll` base. Update in `SDK.cs`:
```csharp
public static ulong TypeInfoTableRva = 0x5BB1234; // NEW VALUE
```

### Step 4 — Update TypeIndex values

In `SDK.cs` the `Special` struct has values like:
```csharp
public static uint EFTHardSettings_TypeIndex = 225;
public static uint GPUInstancerManager_TypeIndex = 4917;
public static uint WeatherController_TypeIndex = 10104;
public static uint GlobalConfiguration_TypeIndex = 6406;
public static uint MatchingProgress_TypeIndex = 15331;
public static uint MatchingProgressView_TypeIndex = 15334;
```

To find the new index for a class, use the IL2CPP dump's `script.json` or search `dump.cs` for the class name and note its **typeDefinitionIndex** or ordinal order in the type list. Alternatively, brute-force scan the TypeInfoTable at runtime by iterating and comparing class names.

### Step 5 — Update `EFTCameraManager.GetInstance_RVA` (optional)

This RVA lives in `SDK.cs` inside `Offsets.EFTCameraManager`:
```csharp
public static uint GetInstance_RVA = 0x2CF8AB0; // get_Instance RVA
```

`FindCameraManagerInstance()` reads 128 bytes from this address and dynamically parses the function body for two x64 patterns (`lea rcx,[rip+...]` and `mov rax,[rip+...]`) to extract the class metadata pointer at runtime — so the instance address itself is resolved dynamically once the function is found.

If the RVA is wrong after a patch, the primary resolution path fails gracefully and `CameraManager` automatically falls back to scanning Unity's `AllCameras` list by GameObject name (`"FPS Camera"` / `"Optic Camera"`). The log will print:
```
[CameraManager] Update GetInstance_RVA! Current: 0x2CF8AB0
```
Find the new RVA in `script.json` (search `CameraManager$$get_Instance`) or IDA and update it to restore the faster primary path. Cameras work either way.

### Step 6 — Rebuild and test

```powershell
dotnet build src/eft-dma-radar.csproj
```

Run the radar and check Output → `[Il2CppDumper]` lines. On first run after a patch the cache will be discarded automatically and re-built. A successful run ends with:
```
[Il2CppDumper] Cache saved → ...\il2cpp_offsets.json
```

---

## 6. SDK.cs — Master Offset File (Detailed)

**Location:** `src/Tarkov/SDK.cs`  
**Namespace:** `SDK`  
**Contains:** `Offsets` partial struct + `Enums` partial struct

### How offsets are declared

```csharp
public readonly partial struct Player
{
    public static uint MovementContext = 0x60; // _MovementContext_k__BackingField
    public static uint Profile = 0x900;        // _Profile_k__BackingField
}
```

- `public static uint` — **runtime-mutable** (can be updated by `OffsetManager` or the `Il2CppDumper` cache at startup without recompilation)
- `public const uint` — **compile-time fixed** — these are used in performance-critical paths and should only be changed by editing the source

### How they are consumed in code

```csharp
// Example: reading a player's profile address
var profileAddr = Memory.ReadPtr(playerBase + Offsets.Player.Profile);
```

### AssemblyCSharp struct — IL2CPP metadata bounds

```csharp
public readonly partial struct AssemblyCSharp
{
    public static uint TypeStart = 0;
    public static uint TypeCount = 16336; // total number of types in this build
}
```
`TypeCount` should be updated if the dump shows a different number of types. The dumper uses this to bound its type scan.

### Special struct — singleton access

```csharp
public readonly partial struct Special
{
    public static ulong TypeInfoTableRva = 0x5AA9118;       // RVA of the TypeInfoTable global
    public static uint EFTHardSettings_TypeIndex   = 225;   // index into TypeInfoTable
    public static uint GPUInstancerManager_TypeIndex = 4917;
    public static uint WeatherController_TypeIndex  = 10104;
    public static uint GlobalConfiguration_TypeIndex = 6406;
    public static uint MatchingProgress_TypeIndex   = 15331;
    public static uint MatchingProgressView_TypeIndex = 15334;
}
```

These five `*_TypeIndex` values are used like this (see `EftHardSettingsResolver.cs`):
```
TypeInfoTablePtr[TypeIndex * 8]  →  Il2CppClass*  →  StaticFields  →  _instance
```

---

## 7. SDK_Manual.cs — Enums, Structs & Computed Chains

**Location:** `src/Tarkov/SDK_Manual.cs`

This file **extends** the partial structs in `SDK.cs` with things that cannot be auto-generated from a dump:

| Section | What it contains |
|---------|-----------------|
| `Player.GetTransformChain(Bones)` | Computes the multi-hop pointer chain to reach a specific bone |
| `ObservedPlayerView.GetTransformChain(Bones)` | Same for remote players |
| `FirearmController.To_FirePortTransformInternal` | Chained pointer array to the muzzle position |
| `HealthSystem`, `HealthValue` offsets | EFT.HealthSystem namespace offsets |
| `TaskConditionCounter.Value` | Quest counter offset |
| `Enums.EPlayerSide` | USEC / BEAR / SCAV with `[Description]` attributes |
| `Enums.ETagStatus` | AI awareness flags (Unaware/Aware/Combat/etc.) |
| `Types.MongoID` | Blittable struct for MongoDB ObjectID |
| `Types.HealthSystem` | Blittable value struct (current/max/min HP floats) |
| `Types.BodyRendererContainer` | Body renderer struct layout |
| `SDKExtensions` | Extension methods on SDK types |

**When to update:** When EFT changes health system layout, quest system, or adds new player sides.

---

## 8. UnityOffsets.cs — Unity Engine Offsets

**Location:** `src/Tarkov/Unity/UnityOffsets.cs`

These are **not** EFT-specific. They describe the memory layout of Unity Engine's own runtime structures:

| Struct | Key offsets |
|--------|------------|
| `ObjectClass` | `MonoBehaviourOffset = 0x10` |
| `Component` | `GameObject = 0x58`, `ObjectClassOffset = 0x20` |
| `GameObject` | `ObjectClassOffset = 0x80`, `ComponentsOffset = 0x58`, `NameOffset = 0x88` |
| `Transform` | `InternalOffset = 0x10` |
| `TransformInternal` | `TransformAccess`, `Vertices` |
| `TransformAccess` | packed transform data |

**When to update:** Only when EFT ships a new major Unity version (check with `strings UnityPlayer.dll | grep "Unity "` — look for the version string).

---

## 9. Special Struct — TypeInfoTable & TypeIndex Values

The `Offsets.Special` struct bridges the auto-resolution system with hardcoded fallbacks.

### TypeInfoTableRva

This is the RVA of the **`s_Il2CppMetadataRegistration→typeInfoTable`** global pointer.  
- Auto-updated by `TypeInfoTableResolver.cs` via sig-scan on startup.
- If sig-scan fails the hardcoded value is used as fallback.
- The sig-scanner logs `TypeInfoTableRva UPDATED: old → new` when it succeeds.

### TypeIndex values

Each singleton class in EFT is at a fixed index in the TypeInfoTable array.  
Access pattern: `TypeInfoTable[index * 8]` → `Il2CppClass*` → `StaticFields + _instance_offset` → instance address.

| Constant | Class | Used by |
|----------|-------|---------|
| `EFTHardSettings_TypeIndex` | `EFT.EFTHardSettings` | Movement speed, reach, pose speed features |
| `GPUInstancerManager_TypeIndex` | `GPUInstancer.GPUInstancerManager` | GPU instancer (grass / terrain) |
| `WeatherController_TypeIndex` | `EFT.Weather.WeatherController` | ClearWeather feature |
| `GlobalConfiguration_TypeIndex` | `EFT.GlobalConfiguration` | Config access |
| `MatchingProgress_TypeIndex` | `EFT.UI.Matchmaking.MatchingProgress` | Lobby stage display |
| `MatchingProgressView_TypeIndex` | `EFT.UI.Matchmaking.MatchingProgressView` | Lobby stage display |

**To find new TypeIndex values after a patch:**  
Iterate the TypeInfoTable (use the new `TypeInfoTableRva`) and compare `Il2CppClass.Name` strings until you find the target class name. The zero-based index you used to get there is the new TypeIndex.  
Or: search `dump.cs` for `typeDefinitionIndex` comments (some dumper versions print these).

---

## 10. Auto-Resolution System (Il2CppDumper + Sig-Scan)

The radar has a **self-healing offset resolution pipeline** that runs on every startup:

```
startup
  │
  ├─ 1. Load il2cpp_offsets.json (if exists and fingerprint matches)
  │        └─ if loaded: skip sig-scan, apply cached offsets → done
  │
  ├─ 2. Sig-scan GameAssembly.dll for TypeInfoTable
  │        └─ TypeInfoTableResolver.cs → tries 2 byte patterns
  │        └─ on success: Offsets.Special.TypeInfoTableRva is updated live
  │
  ├─ 3. Walk TypeInfoTable for each registered singleton
  │        └─ EftHardSettingsResolver, WeatherResolver, GpuResolver, MatchingProgressResolver
  │
  ├─ 4. Live-dump class field offsets via Il2CppDumper
  │        └─ reads Il2CppClass.Fields[], compares to SDK.cs values
  │        └─ can UPDATE Offsets.* static fields at runtime
  │
  └─ 5. Save new il2cpp_offsets.json
```

**The sig-scan patterns** (in `TypeInfoTableResolver.cs`) look for two known x64 instruction patterns that reference the TypeInfoTable global. If either pattern matches and the resolved address passes the validation probe, the RVA is updated.

---

## 11. il2cpp_offsets.json — Offset Cache File

**Location:** `<EXE directory>/il2cpp_offsets.json`  
**Generated by:** `Il2CppDumperCache.cs`

```json
{
  "TypeInfoTableRva": 5938399000,
  "Fields": {
    "Player.MovementContext": "0x60",
    "Player.Profile": "0x900",
    ...
  }
}
```

The `TypeInfoTableRva` field acts as a **build fingerprint**. When the radar starts:
1. It sig-scans for the current `TypeInfoTableRva`.
2. If the scanned value **differs** from the cached `TypeInfoTableRva`, the cache is **discarded** and a fresh live dump is performed.
3. If they match, the cached `Fields` are applied via reflection without a live dump.

**You do not need to delete this file after a game update.** The fingerprint check (`cache.TypeInfoTableRva != expectedRva`) means the cache discards itself automatically whenever the sig-scanner resolves a new `TypeInfoTableRva`. Manual deletion is only needed if the JSON file itself becomes corrupt (e.g. truncated write).

---

## 12. Memory Write Features

All memory write features live in `src/Tarkov/Features/MemoryWrites/`.

### Architecture

```
FeatureManager.cs  ─── background thread, runs every ~50 ms
    │
    └── calls TryApply(ScatterWriteHandle) on each enabled feature
            │
            └── ScatterWriteHandle batches all writes into one PCIe transaction
```

### Adding a new feature

1. Create `MyFeature.cs` in `MemoryWrites/`.
2. Extend `MemWriteFeature<MyFeature>`.
3. Override `Enabled` (backed by a config property) and `Delay`.
4. Implement `TryApply(ScatterWriteHandle writes)` — read the current value, conditionally queue a write.
5. Register in `FeatureManager.cs` `Worker()` loop.
6. Add a config property in `UI/Misc/Config.cs`.

### Hard-disable switch

In `FeatureManager.cs`:
```csharp
private const bool HARD_DISABLE_ALL_MEMWRITES = false;
```
Set to `true` to disable ALL writes unconditionally (overrides config). Useful for debugging radar-only mode.

### Safe-mode

When `Program.CurrentMode != ApplicationMode.Normal`, `IsDMAAvailable` returns false and the DMA layer returns zero/null for all reads. All features check `Memory.Ready` before operating.

---

## 13. How to Find New Offsets with IDA / Dumper Tools

### Quick lookup with Il2CppDumper output

`dump.cs` format for a class:
```
// Namespace: EFT
// class Player : ...
// Offset: 0x...
{
    // Fields:
    // 0x40: EFT.CharacterController _characterController
    // 0x60: EFT.MovementContext MovementContext_k__BackingField
    // 0x190: EFT.PlayerBody _playerBody
    ...
}
```

Each hex number is the byte offset from the object pointer. This maps directly to `SDK.cs`.

### Using IDA for RVA hunting

1. Load `GameAssembly.dll`. Rebase to `0x0` (Edit → Segments → Rebase → 0).
2. Open `script.json` from Il2CppDumper and run the provided IDAPython script to rename functions.
3. Search `Functions` for the method name (e.g., `EFTCameraManager$$get_Instance`).
4. The function offset shown = the RVA to put in `SDK.cs` (e.g. `GetInstance_RVA`).

### Verifying struct layout changes

If the radar crashes reading a specific class (e.g., null pointer on `Inventory`), the offset for that field probably shifted. In `dump.cs`, find the class and compare the `+0xXX` values line by line against `SDK.cs`.

---

## 14. Enum Update Reference

| Enum | File | Update trigger |
|------|------|---------------|
| `WildSpawnType` | `SDK.cs` | New boss / AI type added by BSG |
| `EExfiltrationStatus` | `SDK.cs` | New exfil status added |
| `EPlayerState` | `SDK.cs` | New animation state / movement state |
| `EMatchingStage` | `SDK.cs` | New lobby loading stage |
| `SynchronizableObjectType` | `SDK.cs` | New synchronizable object (e.g., new trap type) |
| `ETripwireState` | `SDK.cs` | Tripwire state machine change |
| `EPlayerSide` | `SDK_Manual.cs` | New faction |
| `ETagStatus` | `SDK_Manual.cs` | New AI alert status |
| `EMemberCategory` | `SDK.cs` | New BSG account category |

**How to check:** search `dump.cs` for the enum name. The enum values are listed with their integer assignments. Compare to `SDK.cs` and add any new entries.

---

## 15. Common Crash / Failure Patterns & Fixes

| Symptom | Likely cause | Fix |
|---------|-------------|-----|
| All players show at 0,0 | `Player.MovementContext` or `MovementContext._rotation` offset wrong | Update those offsets in `SDK.cs` |
| No players visible at all | `ClientLocalGameWorld.RegisteredPlayers` wrong | Update offset |
| Radar shows blank / no map | `ClientLocalGameWorld.LocationId` wrong | Update offset |
| Memory writes do nothing | Feature offset in `SDK.cs` shifted (e.g. `ProceduralWeaponAnimation`, `MovementContext`) | Update the relevant struct offsets |
| `TypeInfoTableRva FAILED` in log | Sig-scan patterns no longer match (game recompiled) | Update `TypeInfoTableSigs` patterns in `TypeInfoTableResolver.cs` OR manually find and hardcode the new RVA |
| `[EftHardSettingsResolver] Failed` | TypeIndex or StaticFields offset wrong | Update `EFTHardSettings_TypeIndex` and `Il2CppClass.StaticFields` |
| Loot shows wrong items | `LootItem.Template` or `ItemTemplate._id` offset shifted | Update those offsets |
| Health always shows max | `HealthController.Energy/Hydration` or `HealthValue.Value` offset wrong | Update |
| No exfils shown | `ExfilController.ExfiltrationPointArray` offset wrong | Update |
| Startup crash / access violation | Bad offsets in `SDK.cs` causing a read from an invalid address in the startup chain | Check the Output log for which pointer chain caused the AV; update the relevant struct offsets |
| `il2cpp_offsets.json` causes wrong values | File corrupted (truncated write) — cache normally auto-invalidates | Delete `il2cpp_offsets.json` manually |
| `AssemblyCSharp.TypeCount` mismatch log | EFT added more types | Update `TypeCount` in `SDK.cs` |

---
