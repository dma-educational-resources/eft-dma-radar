# Arena Debug Output Reference
<!-- Last updated from live session: Arena_Bay5 (Bay 5), 6 players, 2026-build -->
<!-- Session addresses: GameWorld=0x285046F0320, LocalPlayer=0x28A4A54C000 -->
<!-- Players: LocalPlayer(USEC/local), <OPV-1>(USEC), <OPV-2>/<OPV-3>/<OPV-4>/<OPV-5>(BEAR) -->
<!-- Unity versions: Arena=6000.3.6.1f (Unity 6) | EFT=2022.3.43f2 (Unity 2022 LTS) -->

> **Real annotated output from a live Arena match session.**  
> All `[Il2CppDumper]` dumps are gated by `Log.EnableDebugLogging`.  
> Enable via: `config.json → DebugLogging: true`, `-debug` launch arg, or **F8** at runtime (F8 also immediately calls `DumpAll()` on the current live match).

---

## Table of Contents

1. [Startup Sequence](#1-startup-sequence)
2. [IL2CPP Cache & Offset Resolution](#2-il2cpp-cache--offset-resolution)
3. [GameWorld Discovery](#3-gameworld-discovery)
4. [Field Dump Format & Full Hierarchy Sections](#4-field-dump-format--full-hierarchy-sections)
   - 4.1 [ClientLocalGameWorld (ClientNetworkGameWorld)](#41-clientlocalgameworld-clientnetworkgameworld)
   - 4.2 [FPSCamera / OpticCamera — Garbled Dumps](#42-fpscamera--opticcamera--garbled-dumps)
   - 4.3 [LocalPlayer (Arena.ArenaClientPlayer → EFT.Player)](#43-localplayer-arenaarenaclientplayer--eftplayer)
   - 4.4 [LocalPlayer MovementContext](#44-localplayer-movementcontext)
   - 4.5 [LocalPlayer InventoryController](#45-localplayer-inventorycontroller)
   - 4.6 [ObservedPlayerView (Arena.ArenaObservedPlayerView)](#46-observedplayerview-arenaarenaobservedplayerview)
   - 4.7 [ObservedPlayerController (Arena.ArenaObservedPlayerController)](#47-observedplayercontroller-arenaarenaobservedplayercontroller)
   - 4.8 [ObservedHealthController (Arena.ArenaObservedPlayerHealthController)](#48-observedhealthcontroller-arenaarenaobservedplayerhealthcontroller)
   - 4.9 [ObservedMovementController](#49-observedmovementcontroller)
   - 4.10 [ObservedPlayerStateContext](#410-observedplayerstatecontext)
   - 4.11 [ObservedInventoryController](#411-observedinventorycontroller)
   - 4.12 [PlayerBody](#412-playerbody)
5. [DATA CHAIN DUMP Format (Reference Template)](#5-data-chain-dump-format-reference-template)
6. [BatchInit Logs](#6-batchinit-logs)
7. [Player Discovery & Registration](#7-player-discovery--registration)
8. [Arena-Specific: Respawn & Round Transitions](#8-arena-specific-respawn--round-transitions)
9. [Armband / TeamID Chain](#9-armband--teamid-chain)
10. [Common Error Patterns](#10-common-error-patterns)
11. [Shutdown & Match End](#11-shutdown--match-end)
12. [Quick Reference: Offset Chains](#12-quick-reference-offset-chains)
13. [Arena vs EFT-Silk Differences](#13-arena-vs-eft-silk-differences)
   - 13.1 [Unity Engine Version](#131-unity-engine-version)
   - 13.2 [UnityPlayer.dll Offsets (GOM / AllCameras)](#132-unityplayerdll-offsets-gom--allcameras)
   - 13.3 [Camera Offsets](#133-camera-offsets)
   - 13.4 [Transform Hierarchy Layout](#134-transform-hierarchy-layout)
   - 13.5 [Game Class Hierarchy](#135-game-class-hierarchy)
   - 13.6 [Critical Field Offsets (IL2CPP)](#136-critical-field-offsets-il2cpp)
   - 13.7 [Observed Player Rotation Chain](#137-observed-player-rotation-chain)
   - 13.8 [Player Data Model & Player Types](#138-player-data-model--player-types)
   - 13.9 [PlayerEntry Model](#139-playerentry-model)
   - 13.10 [Worker Thread Architecture](#1310-worker-thread-architecture)
   - 13.11 [Match / Session Lifecycle](#1311-match--session-lifecycle)
   - 13.12 [Name / Identity Resolution](#1312-name--identity-resolution)
   - 13.13 [Features Present in EFT-Silk Only](#1313-features-present-in-eft-silk-only)
   - 13.14 [Configuration & Data Paths](#1314-configuration--data-paths)
   - 13.15 [Same / Shared Across Both Projects](#1315-same--shared-across-both-projects)

---

## 1. Startup Sequence

```
[ArenaConfig] Loaded from C:\Users\...\AppData\Roaming\eft-dma-radar-arena\config.json
[ArenaProgram] Config loaded OK.
[ArenaProgram] High performance mode set.
[Memory] Initializing DMA...
[Memory] State → WaitingForProcess
[Memory] DMA initialized OK.
[ArenaProgram] Memory module initialized — waiting for game...
[Memory] Worker thread started.
[Memory] Waiting for Arena game process...
[Memory] State → WaitingForProcess
[MapManager] Loaded 11 map configs (11 IDs), skipped 0.
[RadarWindow] Run() starting...
[Memory] GameAssembly.dll base: 0x7FFABF4C0000
[RadarWindow] OpenGL: 3.3.0 - Build 32.0.101.7085
[GOM] Located via direct sig: mov [rip+rel32],rax (GOM init store)
[Memory] GOM: 0x285200D0F70
[Memory] TypeInfoTableRva is 0 — skipping pre-dump wait.
[Il2CppDumper] Dump starting...
[Il2CppDumper] Fast cache loaded (PE match) — 40/40 fields applied.
[CameraManager] Offsets restored from cache (VM=0x88, FOV=0x188, AR=0x4F8)
[Memory] State → Initializing
[Memory] Game startup OK.
[Memory] State → ProcessFound
[Memory] Searching for Arena match...
```

---

## 2. IL2CPP Cache & Offset Resolution

```
[Il2CppDumper] Fast cache loaded (PE match) — 40/40 fields applied.
```

Fast-path: PE hash matches last run → all 40 cached field offsets applied instantly, no live IL2CPP walk needed.

If hash mismatches (game update):
```
[Il2CppDumper] Dump starting...
[Il2CppDumper] Walking IL2CPP metadata...
[Il2CppDumper] Cache saved — 40 fields resolved.
```

Camera offsets are cached separately:
```
[CameraManager] Offsets restored from cache (VM=0x88, FOV=0x188, AR=0x4F8)
```

- `VM` (ViewMatrix) = `0x88` — offset from FPSCamera object to the ViewMatrix transform  
- `FOV` = `0x188` — camera field-of-view float  
- `AR` = `0x4F8` — aspect ratio float

---

## 3. GameWorld Discovery

```
[LocalGameWorld] Scanning for match... (elapsed 0s)
[IL2CPP] GamePlayerOwner class @ 0x28541C4CCC0
[LocalGameWorld] @ 0x28892F0A640 — RegisteredPlayers not ready yet
[LocalGameWorld] Found live GameWorld @ 0x285046F0320, map = 'Arena_Bay5' (Bay 5), players = 6
[CameraManager] Viewport set to 1920x1080
[Memory] State → InGame
[LocalGameWorld] Workers started. Map: Bay 5 (Arena_Bay5)
[WorkerThread] 'Registration Worker' starting...
[WorkerThread] 'Realtime Worker' starting...
[WorkerThread] 'Camera Worker' starting...
[CameraWorker] Waiting for LocalPlayer before camera init...
[MapManager] Loading map 'Arena_Bay5' (Bay 5)...
```

Discovery flow:
1. `GamePlayerOwner` IL2CPP class pointer resolved via sig → used to find GameWorld chain
2. Scan loop reads `ClientLocalGameWorld` address from GOM chain
3. Validates `RegisteredPlayers` list count > 0 and `MainPlayer` != null
4. On success: starts 3 worker threads and triggers camera init

**Map ID → Display name** (from MapManager config):
- `Arena_Bay5` → Bay 5
- `Arena_Factory4_Day` → Factory
- etc. (11 maps total at time of this session)

---

## 4. Field Dump Format & Full Hierarchy Sections

### Format

`DumpClassFields` walks the full class hierarchy (most-derived → ... → `System.Object`) and logs every field, including static fields.

```
── Fields of '<LABEL>' @ <OBJECT_ADDRESS> (full hierarchy) ──
  ┌ <ClassName> (klass=<IL2CPP_CLASS_PTR>, <N> field(s))
  │  [<OFFSET>] <TYPE>  <NAME> = <VALUE>
  ...
  ┌ System.Object (klass=<PTR>, 0 field(s))
── End of '<LABEL>' (<N> class(es) in hierarchy) ──
```

**Static fields** always show `[0x0]` — they live in the class's static-fields region, not on the instance.  
**Garbled class names** with `[0x0]` offsets = static storage artifact (see §4.2).

---

### 4.1 ClientLocalGameWorld (ClientNetworkGameWorld)

**8 classes in hierarchy**: `ClientNetworkGameWorld` → `EFT.ClientGameWorld` → `EFT.GameWorld` → `UnityEngine.MonoBehaviour` → `UnityEngine.Behaviour` → `UnityEngine.Component` → `UnityEngine.Object` → `System.Object`

> **Important**: The top-level concrete class in Arena is `ClientNetworkGameWorld`, not `ClientGameWorld`. The C# field dump correctly shows `ClientNetworkGameWorld (klass=0x285425241C0, 0 field(s))` as the leaf class.

```
── Fields of 'ClientLocalGameWorld @ 0x285046F0320 (map=Arena_Bay5)' @ 0x285046F0320 (full hierarchy) ──
  ┌ ClientNetworkGameWorld (klass=0x285425241C0, 0 field(s))
  ┌ EFT.ClientGameWorld (klass=0x285420EB000, 5 field(s))
  │  [0x2F0] float        <LastServerWorldTime>k__BackingField = 130.522
  │  [0x2F8] class        <ClientSynchronizableObjectLogicProcessor>k__BackingField = 0x28504BD1360
  │  [0x300] class        <KarmaClientController>k__BackingField = 0x2850BF03680
  │  [0x308] generic<>    NeedApplyGrenadePacketAfterCreate = 0x28504BD1F60
  │  [0x310] ulong        _totalOutgoingBytes = 0x54D0
  ┌ EFT.GameWorld (klass=0x28541ED7590, 96 field(s))
  │  [0x28] class        <BtrController>k__BackingField = null
  │  [0x40] generic<>    ObservedPlayersCorpses = 0x28504BD1F00
  │  [0x58] class        <ExfiltrationController>k__BackingField = 0x28504BD1600
  │  [0xD0] string       <LocationId>k__BackingField = "Arena_Bay5"          ← MAP ID
  │  [0xD8] class        GameDateTime = 0x2885B618F40
  │  [0x168] generic<>   AllLoot = 0x2850421D1E0
  │  [0x180] int         _lastPlayerRaidId = 1000
  │  [0x190] generic<>   AllAlivePlayerBridges = 0x28504BD1CC0
  │  [0x198] generic<>   LootList = 0x2850421D1B0
  │  [0x1B0] generic<>   AllAlivePlayersList = 0x2850421D180
  │  [0x1B8] generic<>   RegisteredPlayers = 0x2850421D150    ← PLAYER LIST
  │  [0x1C0] generic<>   LootItems = 0x2850421D0F0
  │  [0x210] valuetype   _updateQueue = 0
  │  [0x218] class       MainPlayer = 0x28A4A54C000            ← LOCAL PLAYER PTR (changes on respawn)
  │  [0x220] class       _world = 0x289624E7B80
  │  [0x228] class       ObjectsFactory = 0x28884913F60
  │  [0x240] generic<>   _ammoShells = 0x2850421D060
  │  [0x258] class       MineManager = 0x28507864DA0
  │  [0x290] generic<>   Windows = 0x2850421D000
  │  [0x298] generic<>   Grenades = 0x285041C9F90
  │  [0x2E0] class       NetworkWorld = 0x289624E7B80
  │  [0x2E8] generic<>   _openingDoors = 0x285041C9EA0
  ┌ UnityEngine.MonoBehaviour (klass=0x2854035B7A0, 1 field(s))
  │  [0x18] class        m_CancellationTokenSource = null
  ┌ UnityEngine.Object (klass=0x28540353C70, 5 field(s))
  │  [0x10] IntPtr       m_CachedPtr = 0x28546C94A70          ← native Unity ptr
  ┌ System.Object (klass=0x28540268090, 0 field(s))
── End of 'ClientLocalGameWorld @ 0x285046F0320 (map=Arena_Bay5)' (8 class(es) in hierarchy) ──
```

**Key `EFT.GameWorld` fields used by Arena radar**:

| Offset | Type | Name | Purpose |
|--------|------|------|---------|
| `0xD0` | string | `<LocationId>k__BackingField` | Map ID (`"Arena_Bay5"`) |
| `0x1B8` | generic<> | `RegisteredPlayers` | Player list (IL2CPP List<IPlayer>) |
| `0x218` | class | `MainPlayer` | Local player ptr — **changes each respawn** |
| `0x220` | class | `_world` | NetworkWorld back-ref |

**Key `EFT.ClientGameWorld` fields** (potentially useful):

| Offset | Type | Name | Notes |
|--------|------|------|-------|
| `0x2F0` | float | `<LastServerWorldTime>k__BackingField` | Server time in seconds (~130s into match at dump) |
| `0x310` | ulong | `_totalOutgoingBytes` | Outgoing network bytes — useful for detecting activity |
| `0x300` | class | `<KarmaClientController>k__BackingField` | Karma/team-kill system |

**Other potentially useful `EFT.GameWorld` fields**:

| Offset | Type | Name | Notes |
|--------|------|------|-------|
| `0x40` | generic<> | `ObservedPlayersCorpses` | Corpse list — could be used for loot ESP |
| `0x168` | generic<> | `AllLoot` | All loot in world |
| `0x198` | generic<> | `LootList` | Loot items list |
| `0x1C0` | generic<> | `LootItems` | Another loot reference |
| `0x1B0` | generic<> | `AllAlivePlayersList` | Alive-only player list (separate from RegisteredPlayers) |
| `0x58` | class | `<ExfiltrationController>k__BackingField` | Exfil points |
| `0x298` | generic<> | `Grenades` | Live grenade tracking |
| `0x290` | generic<> | `Windows` | Breakable windows |

---

### 4.2 FPSCamera / OpticCamera — Garbled Dumps

```
── Fields of 'FPSCamera @ 0x28C559DACD0' @ 0x28C559DACD0 (full hierarchy) ──
  ┌ <garbled_bytes>.@SH... (klass=0x7FFACA5C28B8, 3 field(s))
  │  [0x202444] ?            <unreadable> = 0x5413C92800000000
  │  [0x5164] ?             <unreadable> = 0
  │  [0x8B4C30EC] ?         <unreadable> = (static)
  ┌ <unknown> (klass=0x7FFAC8A713E0, 20592 field(s))
── End of 'FPSCamera @ 0x28C559DACD0' (2 class(es) in hierarchy) ──
```

**Why garbled**: `FPSCamera` and `OpticCamera` addresses (`0x28C559DACD0`, `0x28B85250B50`) come from `CameraManager` which resolves them via a native Unity `Camera` component lookup. The klass pointers (`0x7FFACA5C28B8`, `0x7FFAC8A713E0`) are **native Unity engine code addresses** (inside GameAssembly.dll's non-IL2CPP region), not managed IL2CPP class metadata. The dumper reads garbage from those addresses.

**Conclusion**: Camera objects cannot be IL2CPP-dumped. Use the `CameraManager` offset constants instead:
- `ViewMatrix` (VM) at `+0x88` from FPSCamera object
- `FOV` at `+0x188`
- `AR` (aspect ratio) at `+0x4F8`

These offsets are validated at startup via the offset cache.

---

### 4.3 LocalPlayer (Arena.ArenaClientPlayer → EFT.Player)

**9 classes in hierarchy**: `Arena.ArenaClientPlayer` → `EFT.ClientPlayer` → `EFT.NetworkPlayer` → `EFT.Player` → `UnityEngine.MonoBehaviour` → `UnityEngine.Behaviour` → `UnityEngine.Component` → `UnityEngine.Object` → `System.Object`

> **Arena-specific top class**: `Arena.ArenaClientPlayer` is the concrete Arena class. It adds `<SimpleArenaProfile>`, `_renderersCache`, and `<SpectatorModeChanged>`.

```
── Fields of 'LocalPlayer (EFT.Player) 'LocalPlayer'' @ 0x28A4A54C000 (full hierarchy) ──
  ┌ Arena.ArenaClientPlayer (klass=0x285421CFF50, 3 field(s))
  │  [0x1C90] generic<>    _renderersCache = 0x28A4A53EAE0
  │  [0x1C98] class        <SimpleArenaProfile>k__BackingField = 0x288A04DDA80  ← Arena profile data
  │  [0x1CA0] generic<>    <SpectatorModeChanged>k__BackingField = null
  ┌ EFT.ClientPlayer (klass=0x28541F1AE40, 67 field(s))
  │  [0xC00] generic<>    _operationCallbacks = 0x28A4A53B420
  │  [0xC18] long         _criticalPacketsSent = 290
  │  [0xC20] long         _criticalPacketsProcessedByServer = 287
  │  [0xC34] bool         <IsInitialized>k__BackingField = true
  │  [0xC38] class        _clientInventoryController = 0x2887AC1AB40
  │  [0xC40] class        _clientHealthController = 0x289624E78A0
  │  [0xC48] float        _clientTime = 5.236
  │  [0xC80] class        _game = 0x2889B6D6A80
  │  [0xC88] class        _clientGameWorld = 0x285046F0320               ← back-ref to GameWorld
  │  [0xD30] class        _clientPlayerMovementContext = 0x28A4A812800
  │  [0xD38] bool         TranslateNetCommands = true
  │  [0xEC8] bool         <IsBot>k__BackingField = false
  │  [0xF00] class        _session = 0x287FA805900
  │  [0xF0C] float        _lastTimeFullSyncPosition = 988.998
  ┌ EFT.NetworkPlayer (klass=0x28541E9F5E0, 3 field(s))
  │  [0xBF0] bool         <IsVisible>k__BackingField = false
  │  [0xBF8] class        <FrameIndexer>k__BackingField = 0x2889B6D6A80
  ┌ EFT.Player (klass=0x28541C8EB20, 443 field(s))
  │  [0x50] class        _characterController = 0x2885D0B55A0
  │  [0x70] class        <MovementContext>k__BackingField = 0x28A4A812800         ← ROTATION SOURCE
  │  [0x1C0] class       _playerBody = 0x289691EB700                              ← SKELETON ROOT
  │  [0x640] class       <GameWorld>k__BackingField = 0x285046F0320               ← back-ref
  │  [0x968] class       <Profile>k__BackingField = 0x2896CF248C0
  │  [0x980] class       Physical = 0x2896CF24700
  │  [0x9C8] class       _healthController = 0x289624E78A0
  │  [0x9E0] class       _inventoryController = 0x2887AC1AB40                    ← INVENTORY (armband chain)
  │  [0x9E8] class       _handsController = 0x28A4A820000
  │  [0xA88] class       _playerLookRaycastTransform = 0x28505CA30A0              ← POSITION TRANSFORM
  │  [0xAB8] class       <PlayerBones>k__BackingField = 0x28969228C80
  │  [0xB01] bool        <IsYourPlayer>k__BackingField = true                     ← local player flag
  │  [0x940] int         <RaidId>k__BackingField = 1008
  │  [0x958] string      <VoipID>k__BackingField = "69eb723930a0d164a602e71c"
  │  [0x960] int         <PlayerId>k__BackingField = 5
  ┌ UnityEngine.Object (klass=0x28540353C70, 5 field(s))
  │  [0x10] IntPtr       m_CachedPtr = 0x28542B83E70
  ┌ System.Object (klass=0x28540268090, 0 field(s))
── End of 'LocalPlayer (EFT.Player) 'LocalPlayer'' (9 class(es) in hierarchy) ──
```

**Key `EFT.Player` fields for Arena radar**:

| Offset | Type | Name | Used for |
|--------|------|------|---------|
| `0x70` | class | `<MovementContext>k__BackingField` | Local rotation chain |
| `0x1C0` | class | `_playerBody` | Skeleton root |
| `0x640` | class | `<GameWorld>k__BackingField` | Back-ref to GameWorld |
| `0x9E0` | class | `_inventoryController` | Armband/team-ID chain |
| `0xA88` | class | `_playerLookRaycastTransform` | Eye-level position transform |
| `0xAB8` | class | `<PlayerBones>k__BackingField` | Bone references |
| `0xB01` | bool | `<IsYourPlayer>k__BackingField` | Local player identification |

**Potentially useful but not currently read**:

| Offset | Type | Name | Notes |
|--------|------|------|-------|
| `0xC88` | class | `_clientGameWorld` (ClientPlayer) | Alt back-ref to GameWorld |
| `0xC38` | class | `_clientInventoryController` (ClientPlayer) | Alt inventory ref (same obj as `0x9E0`) |
| `0x968` | class | `<Profile>k__BackingField` | Full profile object |
| `0x940` | int | `<RaidId>k__BackingField` | Integer raid ID (1008 = local player) |
| `0xEC8` | bool | `<IsBot>k__BackingField` | Bot detection |
| `0xBF0` | bool | `<IsVisible>k__BackingField` (NetworkPlayer) | Server-side visibility |

---

### 4.4 LocalPlayer MovementContext

**3 classes in hierarchy**: `EFT.ClientPlayerMovementContext` → `EFT.MovementContext` → `System.Object`

```
── Fields of 'LocalPlayer MovementContext 'LocalPlayer'' @ 0x28A4A812800 (full hierarchy) ──
  ┌ EFT.ClientPlayerMovementContext (klass=0x285422A9460, 0 field(s))
  ┌ EFT.MovementContext (klass=0x28542180190, 206 field(s))
  │  [0x10] class        _playerTransform = 0x289691E1270      ← BifacialTransform (NOT the look transform)
  │  [0x18] class        _playerAnimator = 0x288A04EC000
  │  [0x48] class        _player = 0x28A4A54C000               ← back-ref to Player
  │  [0x50] bool         _isBot = false
  │  [0x94] valuetype    _discreteDirection = 0x3F80000000000000
  │  [0x98] float        _smoothedPoseLevel = 1
  │  [0xA8] float        _smoothedCharacterMovementSpeed = 0.671
  │  [0xD4] valuetype    _rotation = 942869662                  ← THE LOCAL ROTATION (Vector2: yaw/pitch)
  │  [0xDC] valuetype    _previousRotation = 942869662
  │  [0x3B0] float       <CharacterMovementSpeed>k__BackingField = 0.671
  │  [0x208] class       <CurrentState>k__BackingField = 0x28A4A7F9900
  ┌ System.Object (klass=0x28540268090, 0 field(s))
── End of 'LocalPlayer MovementContext 'LocalPlayer'' (3 class(es) in hierarchy) ──
```

**`_rotation` at `0xD4`** is a `Vector2` (8 bytes): first `float` = yaw (X), second `float` = pitch (Y).

**Rotation address chain**:
```
Player + 0x70 → MovementContext ptr → MC + 0xD4 → _rotation (Vector2)
```

**Potentially useful fields**:

| Offset | Type | Name | Notes |
|--------|------|------|-------|
| `0xA8` | float | `_smoothedCharacterMovementSpeed` | Movement speed (0.0–1.0 normalized) |
| `0x94` | valuetype | `_discreteDirection` | Movement direction flags |
| `0x208` | class | `<CurrentState>k__BackingField` | Movement state machine current state |
| `0x10` | class | `_playerTransform` | BifacialTransform — different from `_playerLookRaycastTransform` |

---

### 4.5 LocalPlayer InventoryController

**7 classes in hierarchy**: `ClientPlayerInventoryController` → `PlayerOwnerInventoryController` → `PlayerInventoryController` → `EFT.InventoryLogic.InventoryController` → `EFT.InventoryLogic.PersonItemController` → `EFT.InventoryLogic.ItemController` → `System.Object`

```
── Fields of 'LocalPlayer InventoryController 'LocalPlayer'' @ 0x2887AC1AB40 (full hierarchy) ──
  ┌ ClientPlayerInventoryController (klass=0x285E1F0F850, 2 field(s))
  │  [0x1D0] class        _clientPlayer = 0x28A4A54C000
  │  [0x1D8] class        <PlayerSearchController>k__BackingField = 0x28A4A724DE0
  ┌ PlayerOwnerInventoryController (klass=0x285E1E9EA90, 0 field(s))
  ┌ PlayerInventoryController (klass=0x285E1E71220, 8 field(s))
  │  [0x190] class        Player = 0x28A4A54C000
  │  [0x1C8] class        <Profile>k__BackingField = 0x2896CF248C0
  ┌ EFT.InventoryLogic.InventoryController (klass=0x28541D684C0, 19 field(s))
  │  [0x118] bool         ForceUncoverAll = false
  │  [0x120] class        <Inventory>k__BackingField = 0x288A989A460   ← INVENTORY OBJECT (→ armband chain)
  │  [0x128] class        <Profile>k__BackingField = 0x2896CF248C0
  ┌ EFT.InventoryLogic.PersonItemController (klass=0x28541D69040, 1 field(s))
  │  [0x110] valuetype    Side = 1                                      ← 1=USEC, 2=BEAR (local player = USEC)
  ┌ EFT.InventoryLogic.ItemController (klass=0x28541D698C0, 32 field(s))
  │  [0xD8] string       <ID>k__BackingField = "69eb723930a0d164a602e71c"  ← Profile ID = VoipID
  │  [0xF0] string       <Name>k__BackingField = "<username>"                  ← PLAYER NAME (local player)
  │  [0xF8] bool         <CanBeLocalized>k__BackingField = false
  │  [0xE8] class        <RootItem>k__BackingField = 0x28A199425A0
── End of 'LocalPlayer InventoryController 'LocalPlayer'' (7 class(es) in hierarchy) ──
```

> **Player name is available from `ItemController.<Name>k__BackingField` at `0xF0`** — this is how `"<username>"` (local player's in-game name) is readable. The same offset works for observed players' `ObservedPlayerInventoryController`.

**Key offsets**:

| Class | Offset | Name | Notes |
|-------|--------|------|-------|
| `InventoryController` | `0x120` | `<Inventory>k__BackingField` | Leads to armband chain |
| `PersonItemController` | `0x110` | `Side` | `1`=USEC, `2`=BEAR |
| `ItemController` | `0xD8` | `<ID>k__BackingField` | Profile/VoipID string |
| `ItemController` | `0xF0` | `<Name>k__BackingField` | **Player's in-game name** |

---

### 4.6 ObservedPlayerView (Arena.ArenaObservedPlayerView)

**7 classes in hierarchy**: `Arena.ArenaObservedPlayerView` → `EFT.NextObservedPlayer.ObservedPlayerView` → `UnityEngine.MonoBehaviour` → `UnityEngine.Behaviour` → `UnityEngine.Component` → `UnityEngine.Object` → `System.Object`

> **Arena-specific top class**: `Arena.ArenaObservedPlayerView` (klass=`0x285429FF3F0`) wraps `ObservedPlayerView` and adds Arena-specific fields including `<SimpleArenaProfile>`.

```
── Fields of 'ObservedPlayer (OPV) '<OPV-1>'' @ 0x28A4B28B480 (full hierarchy) ──
  ┌ Arena.ArenaObservedPlayerView (klass=0x285429FF3F0, 19 field(s))
  │  [0x178] class        <SimpleArenaProfile>k__BackingField = 0x288A0ADEE80   ← Arena profile
  │  [0x180] class        <CameraPosition>k__BackingField = 0x28505D34E60
  │  [0x188] valuetype    <UpdateQueue>k__BackingField = 0
  │  [0x190] class        <PointOfViewChanged>k__BackingField = 0x2850D0171A0
  │  [0x198] class        <SpectatorModeChanged>k__BackingField = 0x2850D017180
  │  [0x1A0] float        <RibcageScaleCurrent>k__BackingField = 1
  │  [0x1A4] float        <RibcageScaleCurrentTarget>k__BackingField = 1
  │  [0x1B0] []           _checkPositions = 0x28A4B2706C0                        ← visibility check pts
  │  [0x1C8] bool         <IsActive>k__BackingField = true
  ┌ EFT.NextObservedPlayer.ObservedPlayerView (klass=0x285427FC220, 54 field(s))
  │  [0x20] int          <RaidId>k__BackingField = 1048                           ← integer raid ID
  │  [0x28] class        <ObservedPlayerController>k__BackingField = 0x28A4AD3D380  ← HUB OBJECT
  │  [0x40] string       <Voice>k__BackingField = "Usec_4"                         ← voice/AI name
  │  [0x7C] int          <Id>k__BackingField = 3                                   ← numeric player ID
  │  [0x9C] valuetype    <Side>k__BackingField = 1                                 ← 1=USEC, 2=BEAR, 4=Scav
  │  [0xA8] bool         <IsAI>k__BackingField = false
  │  [0xB0] string       <ProfileId>k__BackingField = "658492417e32522d830f897b"
  │  [0xB8] string       <VoipID>k__BackingField = "658492417e32522d830f897b"
  │  [0xC0] string       <NickName>k__BackingField = "<OPV-1>"                   ← PLAYER NAME (populated!)
  │  [0xC8] string       <AccountId>k__BackingField = ""                           ← empty in Arena
  │  [0xD8] class        <PlayerBones>k__BackingField = 0x28969BE7320
  │  [0xE0] class        <PlayerBody>k__BackingField = 0x28969371700               ← SKELETON ROOT
  │  [0xE8] class        <CharacterController>k__BackingField = 0x288A9D9BB40
  │  [0x110] class       _playerLookRaycastTransform = 0x28505D34E60               ← POSITION TRANSFORM
  │  [0x120] []          _animators = 0x28A4B2D0A40
  │  [0x150] bool        _isCapsuleVisible = true
  │  [0x158] class       Physical = 0x28A4B185500
  ┌ UnityEngine.Object (klass=0x28540353C70, 5 field(s))
  │  [0x10] IntPtr       m_CachedPtr = 0x28B897D10A0
  ┌ System.Object (klass=0x28540268090, 0 field(s))
── End of 'ObservedPlayer (OPV) '<OPV-1>'' (7 class(es) in hierarchy) ──
```

**Key `ObservedPlayerView` fields**:

| Offset | Type | Name | Notes |
|--------|------|------|-------|
| `0x28` | class | `<ObservedPlayerController>k__BackingField` | Hub for all sub-objects |
| `0x40` | string | `<Voice>k__BackingField` | Voice line ID (AI name detection: `"Reshala"`, `"Tagilla"`, etc.) |
| `0x7C` | int | `<Id>k__BackingField` | Integer player ID; used as fallback display name |
| `0x9C` | valuetype | `<Side>k__BackingField` | **1=USEC, 2=BEAR, 4=Savage** |
| `0xA8` | bool | `<IsAI>k__BackingField` | AI scav/boss detection |
| `0xB0` | string | `<ProfileId>k__BackingField` | Profile ID for team lookup |
| `0xC0` | string | `<NickName>k__BackingField` | **PLAYER NAME — IS populated in Arena** (unlike what was previously documented) |
| `0xE0` | class | `<PlayerBody>k__BackingField` | Skeleton root |
| `0x110` | class | `_playerLookRaycastTransform` | Eye-level transform for position reads |

> **Correction from previous doc**: `<NickName>k__BackingField` **IS populated** in Arena with the player's actual name (e.g. `"<OPV-1>"`, `"<OPV-5>"`, `"<OPV-3>"`, `"<OPV-2>"`, `"<OPV-4>"`). The `<AccountId>` field is empty (`""`), but `<NickName>` and `<ProfileId>` are valid.

> **`EPlayerSide` values**: `1=USEC`, `2=BEAR`, `4=Savage` — confirmed from live data (<OPV-1>=USEC=1, all BEARs=2). Previous docs had this reversed.

**Arena-specific fields (`ArenaObservedPlayerView`)** (potentially useful):

| Offset | Type | Name | Notes |
|--------|------|------|-------|
| `0x178` | class | `<SimpleArenaProfile>k__BackingField` | Arena-specific profile data |
| `0x180` | class | `<CameraPosition>k__BackingField` | Camera position object |
| `0x1B0` | [] | `_checkPositions` | Array of positions used for visibility raycasts |
| `0x1C8` | bool | `<IsActive>k__BackingField` | Whether player is currently active in the round |

---

### 4.7 ObservedPlayerController (Arena.ArenaObservedPlayerController)

**3 classes in hierarchy**: `Arena.ArenaObservedPlayerController` → `EFT.NextObservedPlayer.ObservedPlayerController` → `System.Object`

```
── Fields of 'ObservedPlayerController '<OPV-1>'' @ 0x28A4AD3D380 (full hierarchy) ──
  ┌ Arena.ArenaObservedPlayerController (klass=0x285424E8E40, 1 field(s))
  │  [0x188] bool         <StateChanged>k__BackingField = false
  ┌ EFT.NextObservedPlayer.ObservedPlayerController (klass=0x285424E9010, 22 field(s))
  │  [0x10] class        <InventoryController>k__BackingField = 0x2887AC1A000   ← INVENTORY (armband chain)
  │  [0x18] class        <PlayerView>k__BackingField = 0x28A4B28B480            ← back-ref to OPV
  │  [0x20] valuetype    <Model>k__BackingField = 0x14304CD34
  │  [0x100] class       <Culling>k__BackingField = 0x288A0410780
  │  [0x108] class       <InfoContainer>k__BackingField = 0x28A4B267380
  │  [0x110] class       <MovementController>k__BackingField = 0x28A4B274D00   ← ROTATION CHAIN (step 1)
  │  [0x118] class       <Interpolator>k__BackingField = 0x28A4B31C000
  │  [0x120] class       <HealthController>k__BackingField = 0x28A4AD3D1C0    ← HEALTH
  │  [0x128] class       <AudioController>k__BackingField = 0x28A4B1CA540
  │  [0x130] class       <VoIPController>k__BackingField = 0x288A0410280
  │  [0x138] int         <Id>k__BackingField = 3
  │  [0x140] class       <EquipmentViewController>k__BackingField = 0x28A4B343D90
  │  [0x158] class       <HandsController>k__BackingField = 0x28998382DC0
  │  [0x178] bool        _disposed = false
  │  [0x17C] int         _evenOrNotEvenUpdate = 0
  ┌ System.Object (klass=0x28540268090, 0 field(s))
── End of 'ObservedPlayerController '<OPV-1>'' (3 class(es) in hierarchy) ──
```

**Key OPC fields**:

| Offset | Type | Name | Notes |
|--------|------|------|-------|
| `0x10` | class | `<InventoryController>k__BackingField` | Armband/TeamID chain start |
| `0x110` | class | `<MovementController>k__BackingField` | → rotation chain step 1 |
| `0x120` | class | `<HealthController>k__BackingField` | → alive check |

**Potentially useful**:

| Offset | Type | Name | Notes |
|--------|------|------|-------|
| `0x140` | class | `<EquipmentViewController>k__BackingField` | Equipment visual controller |
| `0x158` | class | `<HandsController>k__BackingField` | What's in player's hands |
| `0x108` | class | `<InfoContainer>k__BackingField` | Info container (group/team data) |
| `0x100` | class | `<Culling>k__BackingField` | Culling state |
| `0x17C` | int | `_evenOrNotEvenUpdate` | Even/odd frame update toggle (0 or 1) |

---

### 4.8 ObservedHealthController (Arena.ArenaObservedPlayerHealthController)

**3 classes in hierarchy**: `Arena.ArenaObservedPlayerHealthController` → `ObservedPlayerHealthController` → `System.Object`

```
── Fields of 'ObservedHealthController '<OPV-1>'' @ 0x28A4AD3D1C0 (full hierarchy) ──
  ┌ Arena.ArenaObservedPlayerHealthController (klass=0x28541D0B870, 2 field(s))
  │  [0x170] generic<>    _activeEffects = 0x28A4B2FD4B0       ← active status effects (not currently used)
  │  [0x178] generic<>    _bodyState = 0x28A4B2ECF00           ← body part health state (not currently used)
  ┌ ObservedPlayerHealthController (klass=0x28541D0C070, 44 field(s))
  │  [0x10] valuetype    HealthStatus = 0x100000400             ← health flags (alive = 0x100000400)
  │  [0x14] bool         <IsAlive>k__BackingField = true        ← PRIMARY alive check
  │  [0x18] class        _player = 0x28A4B28B480                ← back-ref to OPV
  │  [0x20] class        _playerCorpse = null                   ← non-null when dead
  │  [0x30] valuetype    _physicalCondition = 0                 ← physical condition flags
  │  [0x40] bool         _gotDeathPacket = false
── End of 'ObservedHealthController '<OPV-1>'' (3 class(es) in hierarchy) ──
```

**`HealthStatus` flag**: `0x100000400` = alive. After death this changes and `<IsAlive>` flips to `false`.

> **Note**: The old doc said `HealthStatus = 1024` — this is incorrect. The actual live value is `0x100000400` (4295033856 decimal). The `<IsAlive>` bool at `+0x14` is the reliable alive check.

**Potentially useful (not currently read)**:

| Offset | Type | Name | Notes |
|--------|------|------|-------|
| `0x170` | generic<> | `_activeEffects` (ArenaHC) | List of active status effects (bleeds, fractures, etc.) |
| `0x178` | generic<> | `_bodyState` (ArenaHC) | Per-body-part health state |
| `0x30` | valuetype | `_physicalCondition` | Physical condition bitmask (fractures, pain, etc.) |
| `0x20` | class | `_playerCorpse` | Corpse object ptr — non-null when dead/downed |

---

### 4.9 ObservedMovementController

**2 classes in hierarchy**: `EFT.NextObservedPlayer.ObservedPlayerMovementController` → `System.Object`

```
── Fields of 'ObservedMovementController '<OPV-1>'' @ 0x28A4B274D00 (full hierarchy) ──
  ┌ EFT.NextObservedPlayer.ObservedPlayerMovementController (klass=0x28541F78030, 3 field(s))
  │  [0x10] valuetype    <Model>k__BackingField = 0xC07676C942BD1687   ← movement model (NOT rotation)
  │  [0xB0] class        <ObservedPlayerStateContext>k__BackingField = 0x28A4AC938A0  ← ROTATION CHAIN step 2
  │  [0xB8] class        <ObservedVaultingParameters>k__BackingField = 0x28A4B3220C0  ← vaulting state
  ┌ System.Object (klass=0x28540268090, 0 field(s))
── End of 'ObservedMovementController '<OPV-1>'' (2 class(es) in hierarchy) ──
```

> **`StateContext` at `0xB0`** — do NOT use `<Model>k__BackingField` at `0x10` for rotation; it is a movement network model struct, not a rotation.

**Potentially useful**:

| Offset | Type | Name | Notes |
|--------|------|------|-------|
| `0xB8` | class | `<ObservedVaultingParameters>k__BackingField` | Vaulting state — could detect vaulting |

---

### 4.10 ObservedPlayerStateContext

**2 classes in hierarchy**: `EFT.NextObservedPlayer.ObservedPlayerStateContext` → `System.Object`

```
── Fields of 'ObservedPlayerStateContext '<OPV-1>'' @ 0x28A4AC938A0 (full hierarchy) ──
  ┌ EFT.NextObservedPlayer.ObservedPlayerStateContext (klass=0x285427FE940, 51 field(s))
  │  [0x10] class        <PlayerAnimator>k__BackingField = 0x288A0410800
  │  [0x18] class        <PlayerAnimationBones>k__BackingField = 0x288A3928000
  │  [0x20] valuetype    <Rotation>k__BackingField = 0x401C676942AD19B8    ← THE ROTATION (Vector2: yaw/pitch)
  │  [0x28] valuetype    <TargetBodyRotation>k__BackingField = ...
  │  [0x4C] float        <CurrentTilt>k__BackingField = 0
  │  [0x50] bool         <IsExitingMountedState>k__BackingField = false
  │  [0x51] bool         <IsInMountedState>k__BackingField = false
  │  [0x98] class        _playerTransform = 0x289695A19C0
  │  [0xA0] class        _characterController = 0x288A9D9BB40
  │  [0xB8] class        _currentState = 0x28A4B267310
  │  [0xDC] float        _actualLinearSpeed = 0
  │  [0xE0] valuetype    _actualMotion = 0
  │  [0xEC] valuetype    _currentPlayerPose = 0x3F274E9D00000002
  │  [0xF0] float        _characterMovementSpeed = 0.653543
  │  [0xF4] float        _poseLevel = 1
  │  [0xF8] float        _fallTime = 0
  │  [0xFC] float        _handsToBodyAngle = -6.54906
  │  [0x104] bool        _isGrounded = true
  │  [0x108] valuetype   _velocity = 0                               ← velocity Vector3
  │  [0x114] valuetype   _movementDirection = 0
  │  [0x140] class       _player = 0x28A4B28B480                    ← back-ref to OPV
  │  [0x148] class       _observedPlayerHandsController = 0x28998382DC0
  ┌ System.Object (klass=0x28540268090, 0 field(s))
── End of 'ObservedPlayerStateContext '<OPV-1>'' (2 class(es) in hierarchy) ──
```

**`<Rotation>k__BackingField` at `0x20`** is a `Vector2` (8 bytes): `float X` = yaw, `float Y` = pitch.

**Full 4-hop rotation chain** (observed players):
```
OPV + 0x28 → OPC + 0x110 → ObsMovCtrl + 0xB0 → StateContext + 0x20 → Rotation (Vector2)
```

**Potentially useful**:

| Offset | Type | Name | Notes |
|--------|------|------|-------|
| `0xDC` | float | `_actualLinearSpeed` | Actual movement speed (useful for detecting sprinting) |
| `0xEC` | valuetype | `_currentPlayerPose` | Current pose (standing/crouching/prone) |
| `0xF4` | float | `_poseLevel` | Pose level 0.0–1.0 |
| `0x104` | bool | `_isGrounded` | Whether player is on the ground |
| `0x108` | valuetype | `_velocity` | Velocity Vector3 |
| `0x50`/`0x51` | bool | `<IsExitingMountedState>` / `<IsInMountedState>` | Mounting state |
| `0x4C` | float | `<CurrentTilt>k__BackingField` | Lean/tilt value |

---

### 4.11 ObservedInventoryController

**5 classes in hierarchy**: `EFT.NextObservedPlayer.ObservedPlayerInventoryController` → `EFT.InventoryLogic.InventoryController` → `EFT.InventoryLogic.PersonItemController` → `EFT.InventoryLogic.ItemController` → `System.Object`

```
── Fields of 'ObservedInventoryController '<OPV-1>'' @ 0x2887AC1A000 (full hierarchy) ──
  ┌ EFT.NextObservedPlayer.ObservedPlayerInventoryController (klass=0x285424F5860, 7 field(s))
  │  [0x190] class        _player = 0x28A4B28B480
  │  [0x198] class        _profile = 0x28A4B29B050
  │  [0x1A0] class        _handsController = 0x28998382DC0
  │  [0x1A8] class        _gameWorld = 0x285046F0320
  │  [0x1B0] bool         _isAlive = true
  │  [0x1B8] class        _temporaryGrid = 0x288A9D9B820
  │  [0x1C0] bool         <EndApplyDeathAsync>k__BackingField = false
  ┌ EFT.InventoryLogic.InventoryController (klass=0x28541D684C0, 19 field(s))
  │  [0x120] class        <Inventory>k__BackingField = 0x288A9D9BBE0   ← INVENTORY (→ armband chain)
  │  [0x128] class        <Profile>k__BackingField = 0x28A4B29B050
  ┌ EFT.InventoryLogic.PersonItemController (klass=0x28541D69040, 1 field(s))
  │  [0x110] valuetype    Side = 1                                       ← 1=USEC (<OPV-1>)
  ┌ EFT.InventoryLogic.ItemController (klass=0x28541D698C0, 32 field(s))
  │  [0xD8] string        <ID>k__BackingField = "658492417e32522d830f897b"
  │  [0xF0] string        <Name>k__BackingField = "<OPV-1>"             ← PLAYER NAME via IC
  │  [0xE8] class         <RootItem>k__BackingField = 0x28A4B159000       ← root equipment item
── End of 'ObservedInventoryController '<OPV-1>'' (5 class(es) in hierarchy) ──
```

> **Player name via IC**: `ItemController.<Name>k__BackingField` at `0xF0` contains the player's name. This path `OPC + 0x10 → OIC + 0xF0` is an alternative name source to `OPV.<NickName>` at `0xC0`.

**Key offsets** (shared with local player IC):

| Class | Offset | Name | Notes |
|-------|--------|------|-------|
| `ObservedPlayerInventoryController` | `0x1B0` | `_isAlive` | Secondary alive check |
| `InventoryController` | `0x120` | `<Inventory>k__BackingField` | Armband/TeamID chain |
| `PersonItemController` | `0x110` | `Side` | 1=USEC, 2=BEAR |
| `ItemController` | `0xF0` | `<Name>k__BackingField` | Player name |
| `ItemController` | `0xD8` | `<ID>k__BackingField` | Profile ID |

---

### 4.12 PlayerBody

**6 classes in hierarchy**: `EFT.PlayerBody` → `UnityEngine.MonoBehaviour` → `UnityEngine.Behaviour` → `UnityEngine.Component` → `UnityEngine.Object` → `System.Object`

Same structure for both local and observed players.

```
── Fields of 'PlayerBody '<OPV-1>'' @ 0x28969371700 (full hierarchy) ──
  ┌ EFT.PlayerBody (klass=0x2854295D610, 30 field(s))
  │  [0x20] class        _meshTransform = 0x28505D348E0
  │  [0x28] class        PlayerBones = 0x28969BE7320
  │  [0x30] class        SkeletonRootJoint = 0x2887B3DFC40    ← DizSkinningSkeleton → bone positions
  │  [0x38] class        SkeletonHands = 0x2887B3DFA40
  │  [0x40] class        BodyCustomization = 0x28A4B34AEA0
  │  [0x48] bool         <HaveHolster>k__BackingField = false
  │  [0x4C] int          _layer = 8
  │  [0x50] valuetype    _side = 0x100000000
  │  [0x54] bool         _active = true
  │  [0x60] generic<>    BodySkins = 0x2850B2BA960
  │  [0x70] []           _bodyRenderers = 0x2887F29BCC0
  │  [0x80] class        _hoodedDress = null
  │  [0x88] bool         IsRightLegPistolHolster = false
  │  [0x90] class        _equipment = 0x28A4B159000             ← equipment root (same as IC RootItem)
  │  [0xB0] class        <Equipment>k__BackingField = 0x28A4B159000
  │  [0xD8] string       _playerProfileID = "658492417e32522d830f897b"
  │  [0xE8] bool         _isYourPlayer = false
  ┌ UnityEngine.Object (klass=0x28540353C70, 5 field(s))
  │  [0x10] IntPtr       m_CachedPtr = 0x28C54C998E0
  ┌ System.Object (klass=0x28540268090, 0 field(s))
── End of 'PlayerBody '<OPV-1>'' (6 class(es) in hierarchy) ──
```

**Key fields**:

| Offset | Type | Name | Notes |
|--------|------|------|-------|
| `0x30` | class | `SkeletonRootJoint` | `DizSkinningSkeleton` — `+0x30` → `_values` → `TrsX[]` bone array |
| `0x28` | class | `PlayerBones` | Named bone references |
| `0xE8` | bool | `_isYourPlayer` | Alternative local player check (on body) |

**`TrsX` bone position chain**:
```
PlayerBody + 0x30 → SkeletonRootJoint (DizSkinningSkeleton)
  + 0x30 → _values (TrsX[])
    TrsX[boneIndex] + 0x90 → Vector3 (world position)
```

**Potentially useful**:

| Offset | Type | Name | Notes |
|--------|------|------|-------|
| `0x70` | [] | `_bodyRenderers` | Body renderers — visibility/highlighting |
| `0x60` | generic<> | `BodySkins` | Skin customization data |
| `0x48` | bool | `<HaveHolster>k__BackingField` | Whether player has a holster equipped |

---

## 5. DATA CHAIN DUMP Format (Reference Template)

> This section is **not currently emitted** by the Arena radar but is preserved as a template for future diagnostics or if chain-dump logging is re-enabled.

```
╔══════════════════════════════════════════════════════════════════════════
║ DATA CHAIN DUMP: <LABEL> @ <BASE_ADDRESS>  (observed=<True/False>)
╚══════════════════════════════════════════════════════════════════════════
── <Section> ────────────────────────────────────────
  <FieldName>  = <VALUE>  (<source> + <OFFSET>  [addr=<COMPUTED>])
```

### LocalPlayer Chain (verified offsets)

```
╔══════════════════════════════════════════════════════════════════════════
║ DATA CHAIN DUMP: LocalPlayer @ 0x28A4A54C000  (observed=False)
╚══════════════════════════════════════════════════════════════════════════
── Rotation ───────────────────────────────────────────────────────────────
  MovementContext     = 0x28A4A812800  (player + 0x70)
  _rotation addr      = MC + 0xD4      → Vector2 (yaw, pitch)
── Position ───────────────────────────────────────────────────────────────
  LookTransform       = 0x28505CA30A0  (player + 0xA88)
  TransformInternal   = LT + 0x10
  Index               = TI + 0x78      → int
  Hierarchy           = TI + 0x70      → TransformHierarchy
  Vertices            = H + 0x68       → TrsX[]
  Position            = Vertices[Index] + 0x90 → Vector3
── Inventory/TeamID ────────────────────────────────────────────────────────
  InventoryController = 0x2887AC1AB40  (player + 0x9E0)
  Inventory           = IC + 0x120
  Equipment           = Inventory + 0x18
  Slots               = Equipment + 0x98   → CompoundItem slots
```

### ObservedPlayer Chain (verified offsets)

```
╔══════════════════════════════════════════════════════════════════════════
║ DATA CHAIN DUMP: ObservedPlayer '<OPV-1>' @ 0x28A4B28B480  (observed=True)
╚══════════════════════════════════════════════════════════════════════════
── Rotation (4 hops) ──────────────────────────────────────────────────────
  OPC           = OPV + 0x28   → 0x28A4AD3D380
  MovCtrl       = OPC + 0x110  → 0x28A4B274D00
  StateCtx      = MC  + 0xB0   → 0x28A4AC938A0
  Rotation addr = SC  + 0x20   → Vector2 (yaw, pitch)
── Health ─────────────────────────────────────────────────────────────────
  HealthCtrl    = OPC + 0x120  → 0x28A4AD3D1C0
  IsAlive       = HC  + 0x14   → bool (true)
── Position ───────────────────────────────────────────────────────────────
  LookTransform = OPV + 0x110  → 0x28505D34E60
  (same TI/Index/Hierarchy chain as local player)
── Inventory/Name ─────────────────────────────────────────────────────────
  OInventoryCtrl= OPC + 0x10   → 0x2887AC1A000
  Name          = OIC + 0xF0   → "<OPV-1>"
  Inventory     = OIC + 0x120  → armband chain
  Side          = OIC + 0x110  → 1 (USEC)
```

---

## 6. BatchInit Logs

After players are discovered, scatter-based initialization runs in rounds.

### Transform Init (4 rounds)

```
[RegisteredPlayers] BatchInitTransforms R1 (LookTransform): N/N valid
[RegisteredPlayers] BatchInitTransforms R2 (TransformInternal): N/N valid
[RegisteredPlayers] BatchInitTransforms R3 (Index+Hierarchy): N/N valid
[RegisteredPlayers] BatchInitTransforms R4 (Vertices+Indices): N/N valid
[RegisteredPlayers] BatchInitTransforms DONE: N entries, M succeeded, K chain-failed, J chain-ok-but-vertex-fail
```

- **R1**: Read `_playerLookRaycastTransform` (`0xA88` local, `0x110` observed)
- **R2**: Read `TransformInternal` from Transform (`+0x10`)
- **R3**: Read `Index` + `Hierarchy` from TransformInternal (`+0x78`, `+0x70`)
- **R4**: Read `Vertices` + `Indices` from Hierarchy (`+0x68`, `+0x40`)

### Rotation Init (3 rounds for observed, 1 serial for local)

```
[RegisteredPlayers] BatchInitRotations R1 (OPC): N/N valid
[RegisteredPlayers] BatchInitRotations R2 (MovCtrl): N/N valid
[RegisteredPlayers] BatchInitRotations R3 (StateCtx): N/N valid
[RegisteredPlayers] BatchInitRotations DONE: N entries, M succeeded
```

### Skeleton Init

```
[Skeleton] Created — 16/16 bones ready (base=0x28A4AC1AB40)
```

16 bones = full skeleton. `base` is the player's base address.

### Combined Summary

```
[RegisteredPlayers] BatchInit: N players, transform(M candidates, M OK, 1 already, 0 maxed), rotation(M candidates, M OK, 1 already, 0 maxed), elapsed=Xms
```

- `1 already` = LocalPlayer (initialized via serial path on discovery)
- `0 maxed` = no player hit max retry count
- `elapsed` = total scatter time in ms

---

## 7. Player Discovery & Registration

```
[RegisteredPlayers] Discovered: LocalPlayer (LocalPlayer) @ <0, 0, 0> yaw=0.0° @ 0x28A4A54C000 (local=True)
[RegisteredPlayers] LocalPlayer: LocalPlayer (LocalPlayer) @ <0, 0, 0> yaw=0.0°
[RegisteredPlayers] Discovered: <OPV-2> (BEAR) @ <0, 0, 0> yaw=0.0° prof=6766715a3f9362de3c0cd880 @ 0x28A4AC1AB40 (local=False)
[RegisteredPlayers] Discovered: <OPV-3> (BEAR) @ <0, 0, 0> yaw=0.0° prof=6581228b210d6bbe710d1c67 @ 0x28A4ACBF6C0 (local=False)
[RegisteredPlayers] Discovered: <OPV-4> (BEAR) @ <0, 0, 0> yaw=0.0° prof=658313704e8b3233b806ad6c @ 0x28A4AD91240 (local=False)
[RegisteredPlayers] Discovered: <OPV-5> (BEAR) @ <0, 0, 0> yaw=0.0° prof=69eba1fa05a89d739f0c12a7 @ 0x28A4B0636C0 (local=False)
[RegisteredPlayers] Discovered: <OPV-1> (USEC) @ <0, 0, 0> yaw=0.0° prof=658492417e32522d830f897b @ 0x28A4B28B480 (local=False)
```

> **Position `<0, 0, 0>`** at discovery is expected — transforms are not yet initialized when the player object is first read from the RegisteredPlayers list.

When `EnableDebugLogging` is true, each discovery immediately triggers `DumpPlayerHierarchy`:
```
[Il2CppDumper] ── Fields of 'ObservedPlayer (OPV) '<OPV-1>'' @ ... ──
[Il2CppDumper] ── Fields of 'ObservedPlayerController '<OPV-1>'' @ ... ──
[Il2CppDumper] ── Fields of 'ObservedHealthController '<OPV-1>'' @ ... ──
[Il2CppDumper] ── Fields of 'ObservedMovementController '<OPV-1>'' @ ... ──
[Il2CppDumper] ── Fields of 'ObservedPlayerStateContext '<OPV-1>'' @ ... ──
[Il2CppDumper] ── Fields of 'ObservedInventoryController '<OPV-1>'' @ ... ──
[Il2CppDumper] ── Fields of 'PlayerBody '<OPV-1>'' @ ... ──
```

Camera init on first observed player arrival:
```
[CameraManager] Using CameraManager.Instance — FPS: 0x28C559DACD0, Optic: 0x28B85250B50
[CameraManager] FPSCamera: 0x28C559DACD0  OpticCamera: 0x28B85250B50
[CameraWorker] CameraManager initialized on attempt #1 (FPSCamera=0x28C559DACD0).
[CameraManager] READY — FPSCamera=0x28C559DACD0 FOV=75.0 AR=1.778 VM.T=<198.71,-7.69,-334.54> viewport=1920x1080 — ESP enabled.
```

Refresh summary (each registration tick):
```
[RegisteredPlayers] Refresh: list=N, valid=N, invalidPtrs=0, new=M, failed=0, total=N
```

RealtimeWorker status:
```
[RealtimeWorker] Scatter: active=N (position=N, rotation=N), total=N
```

Player removal (death or disconnect):
```
[RegisteredPlayers] Removed '<OPV-1>' (USEC) @ 0x28A4B28B480
[RegisteredPlayers] Removed '<OPV-3>' (BEAR) @ 0x28A4ACBF6C0
```

---

## 8. Arena-Specific: Respawn & Round Transitions

**Arena key behavior**: The `ClientLocalGameWorld` / `ClientNetworkGameWorld` instance is **reused** across rounds. Only `MainPlayer` at `GameWorld + 0x218` is updated by the server on death/respawn.

### LocalPlayer pointer change (respawn)

```
[RegisteredPlayers] LocalPlayer pointer changed 0x<OLD> -> 0x<NEW> (respawn)
[RegisteredPlayers] LocalPlayer: LocalPlayer (LocalPlayer) @ <0, 0, 0> yaw=0.0°
```

After pointer change, full `DumpClassFields` fires again for the new `LocalPlayer` and `MovementContext` if debug logging is on.

### Between rounds: empty player list

```
[RegisteredPlayers] Invalid player count: 0 (addr=0x__________), streak=1
[RegisteredPlayers] Invalid player count: 0 (addr=0x__________), streak=2
...
[RegisteredPlayers] RegisteredPlayers list empty for N ticks — match ended.
[LocalGameWorld] LocalPlayerLost — disposing match.
```

The `InvalidCountTicksBeforeLost` threshold (~10 ticks @ 100ms = ~1 second) prevents false positives during brief server pauses.

### Stale GameWorld guard

After a match ends, the same GameWorld address is recorded as "stale" and rejected:

```
[LocalGameWorld] Stale GameWorld @ 0x__________ — waiting for new match...
```

Use `LocalGameWorld.ClearStaleGuard()` to force re-attach during development.

### Round RaidID pattern

From live data: `RaidId` values for the 6-player match were 1008 (local), 1016, 1024, 1032, 1040, 1048 — sequential multiples of 8 starting at `_lastPlayerRaidId = 1000`. This can be used to detect match freshness.

---

## 9. Armband / TeamID Chain

Arena uses armband color to determine team. Chain from `ObservedPlayerView`:

```
OPV + 0x28 → ObservedPlayerController
  OPC + 0x10 → ObservedInventoryController (OIC)
    OIC + 0x120 → Inventory object
      Inventory + 0x18 → Equipment (CompoundItem)
        Equipment + 0x98 → Slots list
          Slots[ArmbandSlotIndex] → Slot
            Slot → contained item → template ID → armband color → team ID
```

For **local player** the chain starts from `Player + 0x9E0 → InventoryController`, then same from `IC + 0x120`.

> **Note**: `<GroupId>k__BackingField` at OPV `0x88` and `<TeamId>k__BackingField` at OPV `0x90` are both `""` (empty string) in Arena — these are not populated by the server. Team assignment relies entirely on armband color resolution.

**Armband slot index** is probed at runtime via `RegisteredPlayers.ProbeLocalInventoryControllerOffset` if the default fails.

**TeamID fallback**: If armband chain fails, players are grouped by `ProfileId` prefix matching or left ungrouped.

---

## 10. Common Error Patterns

### ArgumentOutOfRangeException (first-chance, non-fatal)

```
Exception thrown: 'System.ArgumentOutOfRangeException' in System.Private.CoreLib.dll
Exception thrown: 'System.ArgumentOutOfRangeException' in arena-dma-radar.dll
```

These appear several times during normal operation — at startup, during dump processing, and during player removal. They are **first-chance exceptions caught internally** (scatter read range mismatches, list boundary checks) and do not affect operation. Observed pattern: appear 1–2× at map load, 1–2× during IL2CPP dump of MovementContext large field set, and during player removal.

### RegisteredPlayers not ready

```
[LocalGameWorld] @ 0x28892F0A640 — RegisteredPlayers not ready yet
```

Normal — GameWorld object exists but player list count is still 0 during initial server sync. The scan loop retries until count > 0.

### MainPlayer not ready

```
[LocalGameWorld] @ 0x__________ — MainPlayer not ready yet
```

GameWorld found but `MainPlayer` pointer is null — player hasn't spawned yet. Scan continues.

### Camera init delayed

```
[CameraWorker] Waiting for LocalPlayer before camera init...
```

Camera manager requires at least one observed/local player in the list before attempting init. Resolves automatically.

---

## 11. Shutdown & Match End

```
[RegisteredPlayers] Removed '<OPV-1>' (USEC) @ 0x28A4B28B480
[RegisteredPlayers] Removed '<OPV-3>' (BEAR) @ 0x28A4ACBF6C0
The program '[7732] arena-dma-radar.exe' has exited with code 4294967295 (0xffffffff).
```

Exit code `0xFFFFFFFF` (-1) = abnormal exit (game closed while radar was attached, or OS-level termination). Normal user quit would be `0`.

Match end normal flow:
```
[RegisteredPlayers] RegisteredPlayers list empty for N ticks — match ended.
[LocalGameWorld] LocalPlayerLost — disposing match.
[Memory] State → ProcessFound         ← back to searching for new match
[LocalGameWorld] Scanning for match... (elapsed 0s)
```

---

## 12. Quick Reference: Offset Chains

### GameWorld → Everything

```
GOM chain → ClientNetworkGameWorld
  + 0xD0  → LocationId (string)        map ID
  + 0x1B8 → RegisteredPlayers (List)   all players
  + 0x218 → MainPlayer (ptr)           local player — changes on respawn!
```

### Local Player → Rotation

```
Player + 0x70 → MovementContext
  MC + 0xD4  → _rotation (Vector2: yaw, pitch)
```

### Local Player → Position

```
Player + 0xA88 → _playerLookRaycastTransform
  LT + 0x10   → TransformInternal
  TI + 0x78   → Index (int)
  TI + 0x70   → Hierarchy
  H  + 0x68   → Vertices (TrsX[])
  Vertices[Index] + 0x90 → Vector3 (world position)
```

### Local Player → Inventory / TeamID

```
Player + 0x9E0 → InventoryController
  IC + 0x120  → Inventory
  IC + 0x110  → Side (1=USEC, 2=BEAR)
  IC + 0xF0   → Name (player name string)
  Inventory + 0x18 → Equipment
  Equipment + 0x98 → Slots → armband → team color
```

### Observed Player → Rotation (4 hops)

```
OPV + 0x28  → OPC
OPC + 0x110 → ObsMovCtrl
OMC + 0xB0  → StateContext
SC  + 0x20  → <Rotation> (Vector2: yaw, pitch)
```

### Observed Player → Position

```
OPV + 0x110 → _playerLookRaycastTransform
  (same TI/Index/Hierarchy/Vertices chain as local)
```

### Observed Player → Health

```
OPV + 0x28  → OPC
OPC + 0x120 → HealthController
HC  + 0x14  → <IsAlive> (bool)
HC  + 0x10  → HealthStatus (0x100000400 = alive)
```

### Observed Player → Name / Side / TeamID

```
OPV + 0xC0          → <NickName> (string)         — direct name
OPV + 0x9C          → <Side> (1=USEC, 2=BEAR)
OPV + 0xB0          → <ProfileId> (string)
OPV + 0x28  → OPC
OPC + 0x10  → OIC (ObservedInventoryController)
OIC + 0xF0  → <Name> (string)                     — alt name via IC
OIC + 0x110 → Side (same enum)
OIC + 0x120 → Inventory → Equipment → armband slots → team color
```

### Observed Player → Skeleton

```
OPV + 0xE0  → PlayerBody
PB  + 0x30  → SkeletonRootJoint (DizSkinningSkeleton)
SRJ + 0x30  → _values (TrsX[])
TrsX[boneIndex] + 0x90 → Vector3
```

---

## 13. Arena vs EFT-Silk Differences

This section documents every structural, offset, and feature difference confirmed from source code and live dumps.  
Both projects share the same DMA layer (`VmmSharpEx`, scatter API, `Il2CppDumper`) but diverge at the Unity version and EFT game-class level.

---

### 13.1 Unity Engine Version

| Item | Arena | EFT-Silk |
|------|-------|----------|
| **Unity version** | `6000.3.6.1f` (Unity 6) | `2022.3.43f2` (Unity 2022 LTS) |
| **Source** | Comment in `src-arena\Arena\Unity\Unity.cs` | Fallback comments in `src-silk\Tarkov\Unity\Unity.cs` |

The Unity version difference is the root cause of **every** transform-hierarchy offset difference listed below.

---

### 13.2 UnityPlayer.dll Offsets (GOM / AllCameras)

| Symbol | Arena | EFT-Silk | Notes |
|--------|-------|----------|-------|
| `GomFallback` | `0x21A4450` | `0x1A233A0` | Both sig-scan first; fallback used only if scan fails |
| `AllCameras` | `0x19F3080` | `0x19F3080` | **Same** |

---

### 13.3 Camera Offsets

Sig-scanned at runtime; fallback values reflect each Unity version.

| Symbol | Arena | EFT-Silk | Notes |
|--------|-------|----------|-------|
| `Camera.ViewMatrix` | `0x88` | `0x128` | **Different** |
| `Camera.FOV` | `0x188` | `0x1A8` | **Different** |
| `Camera.AspectRatio` | `0x4F8` | `0x518` | **Different** |
| `Camera.DerefIsAddedOffset` | `0x35` | `0x35` | Same |
| `OpticCameraManager.Camera` | `0x70` | `0x70` | Same |

---

### 13.4 Transform Hierarchy Layout

Unity 6 restructured the native `TransformInternal` and `TransformHierarchy` structs compared to older Unity.

#### TransformAccess (TransformInternal)

| Symbol | Arena (`0x58`/`0x60`) | EFT-Silk (`0x70`/`0x78`) | Notes |
|--------|-----------------------|--------------------------|-------|
| `HierarchyOffset` | `0x58` | `0x70` | **Different** |
| `IndexOffset` | `0x60` | `0x78` | **Different** |

#### TransformHierarchy arrays

| Symbol | Arena | EFT-Silk | Notes |
|--------|-------|----------|-------|
| `VerticesOffset` | `0x50` | `0x68` | **Different** — ptr to `TrsX[]` |
| `IndicesOffset` | `0xA0` | `0x40` | **Different** — ptr to parent `int[]` |
| `WorldPositionOffset` | `0xB0` | *not used* | **Arena only** — Unity 6 caches world pos directly |

#### Position resolution strategy

**Arena (Unity 6):** Unity 6 pre-caches the world-space position at a fixed offset inside `TransformHierarchy`. Arena takes advantage of this: `VerticesAddr` in the scatter map is pre-computed as `hierarchy + 0xB0`, and a single `Vector3` scatter read resolves the world position. **No vertex walk needed.**

```
TransformInternal + 0x58 → TransformHierarchy ptr
TransformInternal + 0x60 → int transform index          ← stored but NOT used for position
hierarchy + 0xB0 → Vector3 (world position, cached)     ← direct single read
```

**EFT-Silk (older Unity):** No fixed world-position cache. Silk reads the entire `TrsX[]` vertices array (up to `index + 1` elements) plus the parent `int[]` array, then walks the parent chain to accumulate the world transform:

```
TransformInternal + 0x70 → TransformHierarchy ptr
TransformInternal + 0x78 → int transform index
hierarchy + 0x68 → TrsX[] vertices ptr  (read N elements)
hierarchy + 0x40 → int[]  parent indices (walk chain)
```

This is a **fundamental per-tick cost difference**: Arena does 1 scatter read per player for position; Silk does 2+ scatter reads (vertices array + index bookkeeping) and CPU-walks the chain.

#### TrsX struct layout

Both projects use the same `TrsX` struct layout (`Translation + padding + Rotation + Scale + padding = 0x30 bytes`), but Arena's `ComputeWorldPosition` adds explicit bounds guards for respawn churn, while Silk's version relies on the parent-index sentinel (`parent < 0`) for loop termination.

---

### 13.5 Game Class Hierarchy

| Item | Arena | EFT-Silk | Notes |
|------|-------|----------|-------|
| **Top GameWorld class** | `ClientNetworkGameWorld` | `ClientLocalGameWorld` | Arena has extra network layer |
| **Local player class** | `Arena.ArenaClientPlayer` | `EFT.ClientPlayer` | Arena adds profile/spectator fields |
| **Observed player class** | `Arena.ArenaObservedPlayerView` | `EFT.NextObservedPlayer.ObservedPlayerView` | Arena adds `SimpleArenaProfile` |
| **OPC class** | `Arena.ArenaObservedPlayerController` | `EFT.NextObservedPlayer.ObservedPlayerController` | Arena adds `<StateChanged>` |
| **OHC class** | `Arena.ArenaObservedPlayerHealthController` | `ObservedPlayerHealthController` | Arena adds `_activeEffects`, `_bodyState` |
| **`EFT.Player` field count** | 443 | ~392 | Arena ~50 extra fields |
| **`EFT.MovementContext` field count** | 206 | 192 | Arena has more state fields |

---

### 13.6 Critical Field Offsets (IL2CPP)

All values confirmed from live dumps and source code. **Bold = must not copy between projects.**

#### EFT.Player

| Field | Arena | EFT-Silk | Notes |
|-------|-------|----------|-------|
| `MovementContext` | `0x70` | `0x60` | **Different** |
| `GameWorld` | `0x640` | `0x600` | **Different** |
| `_inventoryController` | `0x9E0` | `0x980` | **Different** — auto-probed in Arena |
| `_playerLookRaycastTransform` | `0xA88` | `0xA18` | **Different** |
| `_playerBody` / `PlayerBody` | *via Skeleton chain* | `0x190` | Silk reads `_playerBody` direct |

#### EFT.MovementContext

| Field | Arena | EFT-Silk | Notes |
|-------|-------|----------|-------|
| `_rotation` (local yaw/pitch) | `0xD4` | `0xC0` | **Different** — confirmed dump |

#### ObservedPlayerView (OPV)

| Field | Arena | EFT-Silk | Notes |
|-------|-------|----------|-------|
| `ObservedPlayerController` | `0x28` | `0x28` | Same ✓ |
| `Voice` | `0x40` | `0x40` | Same ✓ |
| `Id` | `0x7C` | `0x7C` | Same ✓ |
| `GroupID` | *not read* | `0x80` | Silk uses server GroupID |
| `Side` | `0x9C` | `0x94` | **Different** |
| `IsAI` | `0xA8` | `0xA0` | **Different** |
| `VoipId` | *not read* | `0xB0` | Silk only |
| `NickName` | `0xC0` | `0xB8` | **Different** |
| `AccountId` | *not sent by Arena* | `0xC0` | Silk only (Arena always `""`) |
| `PlayerBody` | `0xE0` | `0xD8` | **Different** |
| `_playerLookRaycastTransform` | `0x110` | `0x100` | **Different** |
| `VisibleToCameraType` | *not read* | `0x60` | Silk only |

#### ObservedPlayerController (OPC)

| Field | Arena | EFT-Silk | Notes |
|-------|-------|----------|-------|
| `InventoryController` | `0x10` | `0x10` | Same ✓ |
| `Player` | *not read* | `0x18` | Silk only |
| `MovementController` | `0x110` (direct) | `[0xD8, 0x98]` (two hops) | **Different** — see §13.7 |
| `HealthController` | `0x120` | `0xE8` | **Different** |
| `InfoContainer` | *not read* | `0xD0` | Silk only |
| `HandsController` | *not read* | `0x120` | Silk only |

#### InventoryController

| Field | Arena | EFT-Silk | Notes |
|-------|-------|----------|-------|
| `Inventory` | `0x120` | `0x100` | **Different** |

---

### 13.7 Observed Player Rotation Chain

This is the **most critical divergence** for radar heading accuracy.

**Arena** — 4 hops through `StateContext`:
```
OPV + 0x28  → ObservedPlayerController (OPC)
OPC + 0x110 → ObservedMovementController (OMC)   ← direct single ptr
OMC + 0xB0  → ObservedPlayerStateContext (SC)
SC  + 0x20  → Vector2 (yaw X, pitch Y)
```

**EFT-Silk** — 3 hops; rotation lives directly on `ObservedMovementController`:
```
OPV + 0x28       → ObservedPlayerController (OPC)
OPC + [0xD8,0x98] → ObservedMovementController (OMC)   ← two-hop deref
OMC + 0x28       → Vector2 (yaw X, pitch Y)            ← NO StateContext
```

Key differences:
- Silk uses a two-hop chain (`0xD8` then `0x98`) to reach `OMC`; Arena uses a single pointer at `0x110`.
- Arena stores rotation in `StateContext.Rotation` (`+0x20`); Silk reads it directly from `OMC` (`+0x28`).
- There is **no `ObservedPlayerStateContext` object** in the Silk rotation path.

---

### 13.8 Player Data Model & Player Types

#### Arena `Player` class (flat, single file)
```
Name, AccountId, ProfileId, Type, IsLocalPlayer, IsAI, TeamID
Position, RotationYaw, RotationPitch
TransformInternal, VerticesAddr, TransformIndex, TransformReady
RotationAddr, RotationReady
ConsecutiveErrors, RealtimeEstablished, MissingTicks
```

#### Silk `Player.Player` class (partial — `Player.cs`, `Player.Draw.cs`, `Player.Plugins.cs`)
```
Name, Type, Position, HasValidPosition
RotationYaw, RotationPitch, MapRotation (pre-computed)
GroupID (server), SpawnGroupID (position-based)
IsAlive, IsActive, IsError, DrawPriority
EHealthStatus (Healthy/Injured/BadlyInjured/Dying)
Base, AccountId (resolved async)
Gear (GearManager), Hands (HandsManager)
...plus plugin fields (GuardManager, FirearmManager, etc.)
```

#### Player type enums

| Type | Arena | EFT-Silk |
|------|-------|----------|
| `Default` | ✓ | ✓ |
| `LocalPlayer` | ✓ | ✓ |
| `Teammate` | ✓ | ✓ |
| `USEC` / `BEAR` | ✓ | ✓ |
| `PScav` | ✓ | ✓ |
| `AIScav` | ✓ | ✓ |
| `AIRaider` | ✓ | ✓ |
| `AIBoss` | ✓ | ✓ |
| `AIGuard` | ✓ | ✓ |
| `SpecialPlayer` (watchlist) | ✗ | ✓ |
| `BtrOperator` | ✗ | ✓ |

#### Team / group detection

| Mechanism | Arena | EFT-Silk |
|-----------|-------|----------|
| **Armband `TeamID`** | ✓ Primary — `OPC → OIC → Inventory → Equipment → Slots → template ID` | ✓ Also used |
| **Server `GroupID`** | ✗ (field always `""`) | ✓ `OPV + 0x80` |
| **Position-based spawn group** | ✗ | ✓ `SpawnGroupID` within 5m radius at spawn |
| **Lock on local team** | ✓ 3-tick stability gate prevents respawn flip | ✓ Not needed (no respawn) |

---

### 13.9 PlayerEntry Model

| Item | Arena | EFT-Silk |
|------|-------|----------|
| **Discovery wrapper** | `Player` (no wrapper) | `PlayerEntry` wraps `Player.Player` |
| **Scatter metadata** | Fields directly on `Player` | Fields on `PlayerEntry` (`TransformReady`, `RotationAddr`, etc.) |
| **Stagger index** | Not used | `_staggerIndex` staggers gear/hands/health refreshes |
| **Health lazy resolve** | Simple `HC + 0x14 IsAlive` | Deferred `ObservedHealthController` chain, `ETagStatus` bitmask |
| **Gear refresh** | Not tracked | `NextGearRefresh`, `NextHandsRefresh`, `NextHealthRefresh` per entry |

---

### 13.10 Worker Thread Architecture

| Worker | Arena | EFT-Silk |
|--------|-------|----------|
| **RealtimeWorker** | 8ms, scatter pos+rot, `AboveNormal` | 8ms, scatter pos+rot+ADS, `AboveNormal` |
| **RegistrationWorker** | 100ms, player list + local discovery, `BelowNormal` | 100ms, player list + loot + exfils + quest + health, `BelowNormal` |
| **CameraWorker** | Optional, adaptive backoff, deferred init | 16ms, camera ViewMatrix + skeleton bones, `Normal` |
| **ExplosivesWorker** | ✗ | ✓ Grenades / tripwires |
| **Camera retry budget** | 2 minutes | 5 minutes (longer for EFT map streaming) |

---

### 13.11 Match / Session Lifecycle

| Item | Arena | EFT-Silk |
|------|-------|----------|
| **GameWorld reuse** | YES — Arena reuses the same `ClientNetworkGameWorld` object across rounds | NO — new object per raid |
| **Round transitions** | `LocalGameWorld` disposes + recreates; `RegisteredPlayers` rebuilt fresh | Raid end → full disposal |
| **Cooldown guard** | `_matchCooldownUntilTicks` prevents immediate re-discovery after disposal | `_localPlayerAddr` change detection |
| **`_lastPlayerRaidId`** | Base `1000`, increments `+8` per player per round | Sequential per raid |
| **Missing-player grace** | 5 ticks (~500ms) before removal; handles respawn list churn | Standard removal when absent |
| **Local player detection** | `GamePlayerOwner._myPlayer` → `Player.GameWorld` back-reference | `ClientLocalGameWorld.MainPlayer` |

---

### 13.12 Name / Identity Resolution

| Item | Arena | EFT-Silk |
|------|-------|----------|
| **Local player name** | Fixed `"LocalPlayer"` label | Profile chain: `Player → Profile → Info → Nickname` |
| **Observed PMC name** | `OPV.NickName` (`+0xC0`, confirmed live) | Fallback `"UsecN"` / `"BearN"` from `OPV.Id` |
| **Observed AI name** | Voice line → `GetAIName()` | Voice line → `GetInitialAIRole().Name` |
| **AccountId** | Always `""` — not sent by Arena server | Resolved async, enables watchlist |
| **ProfileId** | Optional, read from `OPV.ProfileId` | Not separately tracked |
| **`GroupID` from server** | Always empty | `OPV + 0x80` (used for teammate detection) |
| **Class-name check** | Not used | `ReadClassName()` gates observed vs local path |

---

### 13.13 Features Present in EFT-Silk Only

These subsystems do not exist in Arena:

| Feature | Notes |
|---------|-------|
| **Loot** (`LootManager`) | Full container/item/corpse pipeline |
| **Exfils** (`ExfilManager`) | Exfil status, scav exfils, transit points |
| **Quests** (`QuestManager`) | Quest tracking, lobby profile resolver |
| **Explosives** (`ExplosivesManager`) | Grenades, tripwires, mortar projectiles |
| **BTR** (`BtrTracker`) | BTR speed, route state, turret, paid state |
| **Interactables** (`InteractablesManager`) | Doors (locked/open), switches |
| **Killfeed** (`KillfeedManager`) | Kill/death events with dogtag data |
| **Memory writes** (`FeatureManager`) | NoRecoil, FullBright, NoInertia, MoveSpeed, etc. |
| **Player history** (`PlayerHistory`) | Cross-raid player log |
| **Watchlist** (`PlayerWatchlist`) | `SpecialPlayer` promotion via AccountId |
| **Web radar** (`WebRadarServer`) | Browser-based live radar |
| **Gear / Hands** (`GearManager`, `HandsManager`) | Per-player equipment and weapon-in-hands |
| **Quest planner** (`QuestPlannerWorker`) | AI-assisted quest routing |
| **Hideout** (`HideoutManager`) | Hideout state in main menu |
| **Health detail** (`ETagStatus`) | `Injured` / `BadlyInjured` / `Dying` states |
| **HotkeyManager** | Global rebindable hotkeys (not just F8) |
| **InputManager** | Extended input subsystem |

Arena has only player radar (position + rotation + team color) and camera (ESP/aimview).

---

### 13.14 Configuration & Data Paths

| Item | Arena | EFT-Silk |
|------|-------|----------|
| **Config type** | `ArenaConfig` | `SilkConfig` |
| **Config path** | `%AppData%\eft-dma-radar-arena\config.json` | `%AppData%\eft-dma-radar-silk\config.json` |
| **IL2CPP cache path** | `%AppData%\eft-dma-radar-arena\il2cpp_offsets.json` | `%AppData%\eft-dma-radar-silk\il2cpp_offsets.json` |
| **Namespace root** | `eft_dma_radar.Arena` | `eft_dma_radar.Silk` |
| **Debug enable** | `config.json → DebugLogging: true` or `-debug` | Same |
| **F8 runtime dump** | ✓ `DumpAll()` → `DumpPlayerHierarchy()` | ✓ Same path |
| **Map files** | 11 Arena-specific maps | ~14 EFT maps (multi-floor SVGs) |

---

### 13.15 Same / Shared Across Both Projects

| Item | Value | Notes |
|------|-------|-------|
| `EPlayerSide` enum | `1=USEC, 2=BEAR, 4=Savage` | Same in both (previous Arena docs had it reversed) |
| `StateContext.<Rotation>` offset | `0x20` | Same — only used by Arena (Silk doesn't use StateContext) |
| `DizSkinningSkeleton._values` | `0x30` | Same |
| `PlayerBody.SkeletonRootJoint` | `0x30` | Same |
| `TrsX` struct size | `0x30` (48 bytes) | Same layout |
| `Il2CppClass` layout | All fields identical | `Name=0x10`, `Fields=0x80`, etc. |
| `GOM` tagged-node walk | Same logic | `LastTaggedNode=0x0`, `TaggedNodes=0x8`, etc. |
| `UnityString` layout | `Length=0x10`, `Data=0x14` | Same |
| `Silk.NET` packages | Both `2.23.0` | Same windowing + input + GL packages |
| `SkiaSharp` | Both `3.119.2` | Same |
| `ImGui.NET` | Both `1.91.6.1` | Same |
| Account IDs on `<AccountId>` OPV field | Arena `""`, Silk populated | Arena server doesn't send them |
| FPS/OpticCamera dumpable via IL2CPP | NO (native Unity pointers) | Both unreadable via managed dump |

---

Real annotated output from a live Arena match session.  
Use this when debugging memory reads, verifying offset chains, or understanding the Arena IL2CPP dump format.

> **Source**: Full `Debug` output window in Visual Studio, captured during a standard online Arena match.  
> **Gate**: All dumps are guarded by `Log.EnableDebugLogging` — no overhead in release.
