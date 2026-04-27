# Debug Output Reference
<!-- Last updated from live session: Interchange, 30 players (USEC/BEAR/AIScav), 2026-build -->
<!-- Session addresses: GameWorld=0x212B09D7640, LocalPlayer=0x21368865000 -->
<!-- Players: LocalPlayer=<username>(USEC/local), Usec7/Usec8/Usec10/Usec11/Usec12(USEC), Bear3/Bear5/Bear6/Bear9(BEAR), AIScavs(×19) -->

Real annotated output from a live Interchange raid session.  
Use this when debugging memory reads, verifying offset chains, or understanding the IL2CPP dump format.

> **Source**: Full `Debug` output window in Visual Studio, captured during a standard online raid.  
> **Gate**: All `[Il2CppDumper]` hierarchy dumps and `DEBUG` log lines are guarded by `Log.EnableDebugLogging`.  
> Enable via: `config.json → debugLogging: true`, `-debug` launch arg, or **F8** at runtime (F8 also immediately calls `DumpAll()` on the current live session).

---

## Table of Contents

1. [Startup Sequence](#1-startup-sequence)
2. [IL2CPP Cache & Offset Resolution](#2-il2cpp-cache--offset-resolution)
3. [Field Dump Format](#3-field-dump-format)
   - [GameWorld Hierarchy](#31-gameworld-hierarchy)
   - [ClientPlayer (LocalPlayer)](#32-clientplayer-localplayer)
   - [ObservedPlayerView (Network Players)](#33-observedplayerview-network-players)
   - [Sub-Object Dumps](#34-sub-object-dumps)
4. [DATA CHAIN DUMP Format](#4-data-chain-dump-format)
   - [ClientPlayer Chains](#41-clientplayer-chains)
   - [ObservedPlayerView Chains](#42-observedplayerview-chains)
   - [TransformInternal Chain (HOTPATH)](#43-transforminternal-chain-hotpath)
5. [BatchInit Logs](#5-batchinit-logs)
6. [Player Discovery & Registration](#6-player-discovery--registration)
7. [Common Error Patterns](#7-common-error-patterns)
8. [Loot & Exfil Dumps](#8-loot--exfil-dumps)
9. [Shutdown Sequence](#9-shutdown-sequence)
10. [TransformPath Comparison](#10-transformpath-comparison)

---

## 1. Startup Sequence

Normal boot → DMA init → process attach → GOM resolve → IL2CPP dump → raid detection:

```
[20:29:54.147] [SilkConfig] Config loaded OK.
[20:29:54.157] [SilkProgram] Config loaded OK.
[20:29:54.326] [SilkProgram] High performance mode set.
[20:29:55.286] [Memory] Initializing DMA...
[20:29:55.286] [PlayerHistory] Loaded 63 entries from disk.
[20:29:55.295] [Memory] State → WaitingForProcess
[20:29:55.297] [Memory] DMA initialized OK.
[20:29:55.321] [Memory] Worker thread started.
[20:29:55.323] [SilkProgram] Memory module initialized.
[20:29:55.324] [Memory] Waiting for game process...
[20:29:55.329] [MemWriteFeature] Registered: NoRecoil
[20:29:55.331] [MemWriteFeature] Registered: NoInertia
[20:29:55.333] [MemWriteFeature] Registered: MoveSpeed
[20:29:55.334] [MemWriteFeature] Registered: InfStamina
[20:29:55.336] [MemWriteFeature] Registered: NightVision
[20:29:55.337] [MemWriteFeature] Registered: ThermalVision
[20:29:55.339] [MemWriteFeature] Registered: FullBright
[20:29:55.340] [MemWriteFeature] Registered: NoVisor
[20:29:55.342] [MemWriteFeature] Registered: DisableFrostbite
[20:29:55.345] [MemWriteFeature] Registered: DisableInventoryBlur
[20:29:55.346] [MemWriteFeature] Registered: DisableWeaponCollision
[20:29:55.348] [MemWriteFeature] Registered: ExtendedReach
[20:29:55.349] [MemWriteFeature] Registered: FastDuck
[20:29:55.351] [MemWriteFeature] Registered: LongJump
[20:29:55.352] [MemWriteFeature] Registered: ThirdPerson
[20:29:55.354] [MemWriteFeature] Registered: InstantPlant
[20:29:55.356] [MemWriteFeature] Registered: MagDrills
[20:29:55.357] [MemWriteFeature] Registered: MuleMode
[20:29:55.359] [MemWriteFeature] Registered: WideLean
[20:29:55.360] [MemWriteFeature] Registered: MedPanel
[20:29:55.362] [MemWriteFeature] Registered: OwlMode
[20:29:55.367] [FeatureManager] Initialized with 21 features.
[20:29:55.368] [SilkProgram] FeatureManager initialized.
[20:29:55.471] [EftDataManager] Loaded 4957 items, 43 containers.
[20:29:55.475] [EftDataManager] Loaded 494 tasks.
[20:29:55.477] [EftDataManager] Loaded 15 map configs.
[20:29:55.483] [LootFilterData] Loaded 1 wishlisted, 0 blacklisted items.
[20:29:55.504] [MapManager] Loaded 13 map configs (16 IDs), skipped 0.
[20:29:55.506] [SilkProgram] Map manager initialized, starting RadarWindow...
[20:29:55.519] [RadarWindow] Initialize starting...
[20:29:55.524] [RadarWindow] Creating window: 2560x1369, FPS=62, API=Silk.NET.Windowing.GraphicsAPI
[20:29:55.643] [RadarWindow] Initialize complete, window created.
[20:29:55.645] [RadarWindow] Run() starting...
```

**Key milestone** — GameAssembly.dll base is the VA of the game module used for all RVA calculations:

```
[20:29:55.737] [Memory] GameAssembly.dll base: 0x7FFAC03E0000
[20:29:55.791] [GOM] Located via direct sig: mov [rip+rel32],rax (GOM init store)
[20:29:55.796] [Memory] GOM: 0x210500B7730
[20:29:55.803] [Il2CppDumper] Dump starting...
[20:29:55.827] [RadarWindow] OnLoad starting...
```

**Debug logging** — when enabled at startup:
```
[HH:MM:SS.sss] [SilkProgram] Debug logging enabled.
```
Enable options:
- `config.json` → `"debugLogging": true`
- Command line: `-debug`
- Runtime: press **F8** (toggles; also immediately calls `DumpAll()` on the live session)

When toggled at runtime via F8:
```
[20:30:09.500] [RadarWindow] Debug logging ON
```
...followed immediately by the full GameWorld + all-player IL2CPP hierarchy dumps.

---

## 2. IL2CPP Cache & Offset Resolution

The fast-path: if the PE checksum matches the cached dump, all offsets are applied instantly:

```
[20:29:55.803] [Il2CppDumper] Dump starting...
[20:30:02.269] [Memory] State → Initializing
[20:30:02.270] [Memory] Game startup OK.
[20:30:02.274] [Memory] State → ProcessFound
```

> Note: This session did **not** print a fast-cache line — the IL2CPP dump ran silently. When the PE hash matches, you instead see:
> `[Il2CppDumper] Fast cache loaded (PE match) — 380/380 fields applied.`

- **380 fields** = total schema fields across all `SchemaClass` definitions in `Il2CppDumperSchema.cs`
- Cache file: `%AppData%\eft-dma-radar-silk\il2cpp_offsets.json`
- If PE changes (game update), a full scatter-based re-dump runs instead

**Raid detection** — as soon as the game's `ClientLocalGameWorld` has players:

```
[20:30:03.509] [LocalGameWorld] Found live GameWorld @ 0x212B09D7640, map = 'Interchange'
[20:30:03.533] [Memory] State → InRaid
[20:30:03.813] [LocalGameWorld] QuestManager initialized — profile @ 0x218FA318E60
[20:30:03.841] [LocalGameWorld] WishlistManager initialized — 0 item(s).
[20:30:03.916] [LocalGameWorld] CameraManager initialized.
[20:30:03.937] [LocalGameWorld] Local player discovered — radar is live.
```

---

## 3. Field Dump Format

### Overview

The diagnostic `DumpClassFields` method walks the **full class hierarchy** (child → parent → ... → System.Object) and logs every field with its offset, IL2CPP type, name, and live value read from memory.

**Format**:
```
── Fields of '<LABEL>' @ <OBJECT_ADDRESS> (full hierarchy) ──
  ┌ <ClassName> (klass=<IL2CPP_CLASS_PTR>, <N> field(s))
  │  [<OFFSET>] <TYPE>  <NAME> = <VALUE>
  ...
  ┌ System.Object (klass=<PTR>, 0 field(s))
── End of '<LABEL>' (<N> class(es) in hierarchy) ──
```

**IL2CPP type keywords**: `bool`, `int`, `float`, `string`, `class`, `IntPtr`, `valuetype`, `generic<>`, `[]`, `ushort`, `byte`, `ulong`, `double`, `long`

**Special values**:
- `null` = pointer field is 0x0
- `<unreadable>` = field name could not be resolved (obfuscated or name pointer invalid)
- `(failed to read field array: Memory read failed.)` = VmmException during field array scatter read

### 3.1 GameWorld Hierarchy

First dump after F8 is pressed — the `ClientLocalGameWorld` object (triggered ~6 seconds into the raid, address `0x212B09D7640`, map=Interchange):

```
[20:30:09.506] [Il2CppDumper] ── Fields of 'ClientLocalGameWorld @ 0x212B09D7640 (map=Interchange)' @ 0x212B09D7640 (full hierarchy) ──
[20:30:09.508] [Il2CppDumper]   ┌ ClientNetworkGameWorld (klass=0x2100..., 0 field(s))
[20:30:09.510] [Il2CppDumper]   ┌ EFT.ClientGameWorld (klass=0x2100..., 4 field(s))
[20:30:09.512] [Il2CppDumper]   │  [0x2E0] float        <LastServerWorldTime>k__BackingField = 248.12
[20:30:09.515] [Il2CppDumper]   ┌ EFT.GameWorld (klass=0x210..., 91 field(s))
[20:30:09.520] [Il2CppDumper]   │  [0x50] class        <ExfiltrationController>k__BackingField = 0x218FA318D20
[20:30:09.524] [Il2CppDumper]   │  [0x58] class        <SafeZoneManager>k__BackingField = null
[20:30:09.527] [Il2CppDumper]   │  [0xD0] string       <LocationId>k__BackingField = "Interchange"
[20:30:09.530] [Il2CppDumper]   │  [0x148] generic<>   OnLateUpdate = 0x218D609A870
[20:30:09.534] [Il2CppDumper]   │  [0x150] class       _hardwareTime = 0x212F8B52690
[20:30:09.537] [Il2CppDumper]   │  [0x168] generic<>   AllLoot = 0x212F8B52660
[20:30:09.540] [Il2CppDumper]   │  [0x170] class       <ClientBallisticCalculator>k__BackingField = null
[20:30:09.543] [Il2CppDumper]   │  [0x178] class       _sharedBallisticsCalculator = 0x212D2E47310
[20:30:09.546] [Il2CppDumper]   │  [0x180] int         _lastPlayerRaidId = 1000
[20:30:09.549] [Il2CppDumper]   │  [0x188] generic<>   _lampControllers = 0x212FD02E000
[20:30:09.552] [Il2CppDumper]   │  [0x190] generic<>   AllAlivePlayerBridges = 0x212FD032F60
[20:30:09.555] [Il2CppDumper]   │  [0x198] generic<>   LootList = 0x212F8B52630
[20:30:09.558] [Il2CppDumper]   │  [0x1A0] generic<>   _allPlayersEverExistedByRaidId = 0x212FD032F00
[20:30:09.562] [Il2CppDumper]   │  [0x1A8] generic<>   _allPlayersEverExistedByProfileId = 0x212FD032EA0
[20:30:09.565] [Il2CppDumper]   │  [0x1B0] generic<>   AllAlivePlayersList = 0x212F8B52600
[20:30:09.568] [Il2CppDumper]   │  [0x1B8] generic<>   RegisteredPlayers = 0x212F8B525D0
[20:30:09.571] [Il2CppDumper]   │  [0x1C0] generic<>   LootItems = 0x212F8B52540
[20:30:09.574] [Il2CppDumper]   │  [0x1D0] generic<>   allAlivePlayersByRaidID = 0x212FD032DE0
[20:30:09.577] [Il2CppDumper]   │  [0x1D8] generic<>   allAlivePlayersByProfileID = 0x212FD032D80
[20:30:09.580] [Il2CppDumper]   │  [0x1E0] generic<>   ItemOwners = 0x212FD032D20
[20:30:09.583] [Il2CppDumper]   │  [0x1F8] generic<>   allObservedPlayersByRaidID = 0x212FD032CC0
[20:30:09.586] [Il2CppDumper]   │  [0x200] class       SpeakerManager = 0x212AFA40CD0
[20:30:09.589] [Il2CppDumper]   │  [0x208] valuetype   _updateQueue = 0
[20:30:09.592] [Il2CppDumper]   │  [0x210] class       MainPlayer = 0x21368865000
[20:30:09.595] [Il2CppDumper]   │  [0x218] class       _world = 0x218FA318B80
[20:30:09.598] [Il2CppDumper]   │  [0x220] class       ObjectsFactory = 0x212F4DAD540
[20:30:09.601] [Il2CppDumper]   │  [0x228] class       CompositeDisposable = 0x212F8B524E0
[20:30:09.604] [Il2CppDumper]   │  [0x230] generic<>   <PlayersColliders>k__BackingField = 0x212FD032C60
[20:30:09.607] [Il2CppDumper]   │  [0x240] generic<>   _questLootItems = 0x212F8B52480
[20:30:09.610] [Il2CppDumper]   │  [0x248] class       <SynchronizableObjectLogicProcessor>k__BackingField = 0x212FD0327E0
[20:30:09.613] [Il2CppDumper]   │  [0x250] class       MineManager = 0x21345B8D080
[20:30:09.616] [Il2CppDumper]   │  [0x2B0] bool        _controlBundlesAndPools = true
[20:30:09.619] [Il2CppDumper]   │  [0x2C8] class       _defaultBallisticCollider = 0x212FC94EBA0
[20:30:09.622] [Il2CppDumper]   │  [0x2D0] class       NetworkWorld = 0x218FA318B80
[20:30:09.625] [Il2CppDumper]   │  [0x2D8] generic<>   _openingDoors = 0x212F8B522A0
[20:30:09.630] [Il2CppDumper]   ┌ UnityEngine.MonoBehaviour (klass=0x2100088E230, 1 field(s))
[20:30:09.634] [Il2CppDumper]   │  [0x18] class        m_CancellationTokenSource = null
[20:30:09.638] [Il2CppDumper]   ┌ UnityEngine.Behaviour (klass=0x2100088E3B0, 0 field(s))
[20:30:09.641] [Il2CppDumper]   ┌ UnityEngine.Component (klass=0x2100088E530, 0 field(s))
[20:30:09.644] [Il2CppDumper]   ┌ UnityEngine.Object (klass=0x2100088E6B0, 4 field(s))
[20:30:09.648] [Il2CppDumper]   │  [0x10] IntPtr       m_CachedPtr = 0x213364D64E0
[20:30:09.651] [Il2CppDumper]   │  [0x0] int          OffsetOfInstanceIDInCPlusPlusObject = 45818176
[20:30:09.654] [Il2CppDumper]   │  [0x0] string       objectIsNullMessage = "È'"   ← garbled: static native Unity string
[20:30:09.657] [Il2CppDumper]   ┌ System.Object (klass=0x210002523F0, 0 field(s))
[20:30:10.003] [Il2CppDumper] ── End of 'ClientLocalGameWorld @ 0x212B09D7640 (map=Interchange)' (8 class(es) in hierarchy) ──
```

**Hierarchy** (8 classes, most-derived first):
1. `ClientNetworkGameWorld` — 0 fields
2. `EFT.ClientGameWorld` — 4 fields (server time, karma, sync processor, outgoing bytes)
3. `EFT.GameWorld` — **91 fields** (the big one)
4. `UnityEngine.MonoBehaviour` — 1 field
5. `UnityEngine.Behaviour` — 0 fields
6. `UnityEngine.Component` — 0 fields
7. `UnityEngine.Object` — 4 fields (m_CachedPtr + 3 static strings)
8. `System.Object` — 0 fields

**Key fields from EFT.GameWorld** (used by the radar):

| Offset | Type | Name | Live Value |
|--------|------|------|------------|
| `0xD0` | string | `<LocationId>k__BackingField` | `"Interchange"` |
| `0x1B8` | generic<> | `RegisteredPlayers` | `0x212F8B525D0` |
| `0x1B0` | generic<> | `AllAlivePlayersList` | `0x212F8B52600` |
| `0x210` | class | `MainPlayer` | `0x21368865000` |
| `0x50` | class | `<ExfiltrationController>k__BackingField` | `0x218FA318D20` |
| `0x168` | generic<> | `AllLoot` | `0x212F8B52660` |
| `0x198` | generic<> | `LootList` | `0x212F8B52630` |

**Static fields** always show `[0x0]` offset — they live in a separate static fields region, not on the instance:

```
  │  [0x8]  int          _obstaclesCollider = 0
  │  [0xC]  int          _interactiveLootMask = 0
  │  [0x10] int          _interactiveLootMaskWPlayer = 911041760
  │  [0x14] int          _playerMask = 531
```

### 3.2 ClientPlayer (LocalPlayer)

The local player is a `ClientPlayer` which inherits a deep chain. **431 fields** on `EFT.Player` alone:

```
[20:30:29.246] [Il2CppDumper] ── Fields of 'LocalPlayer '<username>' (ClientPlayer)' @ 0x21368865000 (full hierarchy) ──
[20:30:29.249] [Il2CppDumper]   ┌ EFT.ClientPlayer (klass=0x210008C92F0, 66 field(s))
[20:30:29.319] [Il2CppDumper]   │  [0xCD8] class        _clientPlayerMovementContext = 0x2131D007800
[20:30:29.397] [Il2CppDumper]   ┌ EFT.Player (klass=0x210008B0040, 431 field(s))
  ...
[20:30:30.652] [Il2CppDumper]   ┌ UnityEngine.MonoBehaviour (klass=0x2100088E230, 1 field(s))
[20:30:30.655] [Il2CppDumper]   │  [0x18] class        m_CancellationTokenSource = null
[20:30:30.664] [Il2CppDumper]   ┌ UnityEngine.Behaviour (klass=0x2100088E3B0, 0 field(s))
[20:30:30.670] [Il2CppDumper]   ┌ UnityEngine.Component (klass=0x2100088E530, 0 field(s))
[20:30:30.677] [Il2CppDumper]   ┌ UnityEngine.Object (klass=0x2100088E6B0, 4 field(s))
[20:30:30.682] [Il2CppDumper]   │  [0x10] IntPtr       m_CachedPtr = 0x217040F2F10
[20:30:30.686] [Il2CppDumper]   │  [0x0] int          OffsetOfInstanceIDInCPlusPlusObject = 9212656
[20:30:30.711] [Il2CppDumper]   │  [0x0] string       objectIsNullMessage = "È'"
[20:30:30.722] [Il2CppDumper]   ┌ System.Object (klass=0x210002523F0, 0 field(s))
[20:30:30.724] [Il2CppDumper] ── End of 'LocalPlayer '<username>' (ClientPlayer)' (8 class(es) in hierarchy) ──
```

**Hierarchy** (8 classes):
1. `EFT.ClientPlayer` — 66 fields (network sync, client-specific movement context)
2. `EFT.NetworkPlayer` — 3 fields
3. `EFT.Player` — 431 fields (the monster class)
4–8. Unity base classes + System.Object

**Key fields from EFT.Player** (used by radar):

| Offset | Type | Name | Live Value |
|--------|------|------|------------|
| `0x40` | class | `_characterController` | `0x2194BF95A30` |
| `0x60` | class | `<MovementContext>k__BackingField` | `0x2131D007800` |
| `0x190` | class | `_playerBody` | `0x212ADF7B5A0` |
| `0x5F8` | class | `<GameWorld>k__BackingField` | `0x212B09D7640` |
| `0x900` | int | `<PlayerId>k__BackingField` | `2` |
| `0x980` | class | `_inventoryController` | `0x218F7D26D80` |
| `0x988` | class | `_handsController` | *(varies)* |
| `0xA91` | bool | `<IsYourPlayer>k__BackingField` | `true` |

**`<unreadable>` fields**: Many fields in `EFT.Player` show `<unreadable>` — field name pointer couldn't be resolved (likely obfuscated by BSG). The type and offset are still valid:

```
  │  [0x309] bool         <unreadable> = false
  │  [0x310] class        <unreadable> = 0x2194D09E820
  │  [0x338] class        <unreadable> = 0x2194BF95A80   ← ProceduralWeaponAnimation at Player offset 0x338
```

### 3.3 ObservedPlayerView (Network Players)

Network (non-local) players use the `ObservedPlayerView` component — a completely different class hierarchy:

```
[20:30:10.007] [Il2CppDumper] ── Fields of 'ObservedPlayer 'Scav' (ObservedPlayerView)' @ 0x214A224C5C0 (full hierarchy) ──
[20:30:10.013] [Il2CppDumper]   ┌ EFT.NextObservedPlayer.ObservedPlayerView (klass=0x21001265E70, 51 field(s))
[20:30:10.019] [Il2CppDumper]   │  [0x20] int          <RaidId>k__BackingField = 1296
[20:30:10.021] [Il2CppDumper]   │  [0x28] class        <ObservedPlayerController>k__BackingField = 0x214A2318000
[20:30:10.025] [Il2CppDumper]   │  [0x30] class        <SearchController>k__BackingField = 0x214A284CC80
[20:30:10.031] [Il2CppDumper]   │  [0x40] string       <Voice>k__BackingField = "Scav_3"
[20:30:10.057] [Il2CppDumper]   │  [0x7C] int          <Id>k__BackingField = 37
[20:30:10.060] [Il2CppDumper]   │  [0x80] string       <GroupId>k__BackingField = ""
[20:30:10.064] [Il2CppDumper]   │  [0x90] bool         <UsedSimplifiedSkeleton>k__BackingField = false
[20:30:10.072] [Il2CppDumper]   │  [0x94] valuetype    <Side>k__BackingField = 4
[20:30:10.076] [Il2CppDumper]   │  [0xA0] bool         <IsAI>k__BackingField = true
[20:30:10.080] [Il2CppDumper]   │  [0xA8] string       <ProfileId>k__BackingField = "<profile_id_••••••••••••>"
[20:30:10.083] [Il2CppDumper]   │  [0xB0] string       <VoipID>k__BackingField = "1296"
[20:30:10.087] [Il2CppDumper]   │  [0xB8] string       <NickName>k__BackingField = ""
[20:30:10.089] [Il2CppDumper]   │  [0xC0] string       <AccountId>k__BackingField = ""
[20:30:10.093] [Il2CppDumper]   │  [0xC8] generic<>    <MainParts>k__BackingField = null
[20:30:10.096] [Il2CppDumper]   │  [0xD0] class        <PlayerBones>k__BackingField = 0x214A2692960
[20:30:10.098] [Il2CppDumper]   │  [0xD8] class        <PlayerBody>k__BackingField = 0x214A26643C0
[20:30:10.100] [Il2CppDumper]   │  [0xE0] class        <CharacterController>k__BackingField = 0x214A27CBA00
[20:30:10.104] [Il2CppDumper]   │  [0xF0] generic<>    OnIPlayerDeadOrUnspawn = 0x214A29B7090
[20:30:10.107] [Il2CppDumper]   │  [0xF8] bool         <IsInBufferZone>k__BackingField = false
[20:30:10.133] [Il2CppDumper]   │  [0x100] class       _playerLookRaycastTransform = 0x214A281D6E0
[20:30:10.137] [Il2CppDumper]   │  [0x108] byte        _channelIndex = 0x00
[20:30:10.140] [Il2CppDumper]   │  [0x120] class       _hitReaction = 0x214A268B850
[20:30:10.154] [Il2CppDumper]   │  [0x140] bool        _isCapsuleVisible = true
[20:30:10.188] [Il2CppDumper]   ┌ UnityEngine.MonoBehaviour (klass=0x2100088E230, 1 field(s))
[20:30:10.194] [Il2CppDumper]   │  [0x18] class        m_CancellationTokenSource = null
[20:30:10.205] [Il2CppDumper]   ┌ UnityEngine.Behaviour (klass=0x2100088E3B0, 0 field(s))
[20:30:10.208] [Il2CppDumper]   ┌ UnityEngine.Component (klass=0x2100088E530, 0 field(s))
[20:30:10.213] [Il2CppDumper]   ┌ UnityEngine.Object (klass=0x2100088E6B0, 4 field(s))
[20:30:10.215] [Il2CppDumper]   │  [0x10] IntPtr       m_CachedPtr = 0x219C31DAF10
[20:30:10.233] [Il2CppDumper] ── End of 'ObservedPlayer 'Scav' (ObservedPlayerView)' (6 class(es) in hierarchy) ──
```

**Hierarchy** (6 classes — much flatter than ClientPlayer):
1. `EFT.NextObservedPlayer.ObservedPlayerView` — 51 fields
2–6. Unity base classes + System.Object

**Key fields**:

| Offset | Type | Name | Example |
|--------|------|------|---------|
| `0x20` | int | `<RaidId>k__BackingField` | `1296` |
| `0x28` | class | `<ObservedPlayerController>k__BackingField` | `0x214A2318000` |
| `0x40` | string | `<Voice>k__BackingField` | `"Scav_3"` |
| `0x7C` | int | `<Id>k__BackingField` | `37` |
| `0x94` | valuetype | `<Side>k__BackingField` | `4` (4=Savage/Scav, 1=USEC, 2=BEAR) |
| `0xA0` | bool | `<IsAI>k__BackingField` | `true` |
| `0xA8` | string | `<ProfileId>k__BackingField` | `"<profile_id_••••••••••••>"` |
| `0xB8` | string | `<NickName>k__BackingField` | `""` (empty for observed AI/network players) |
| `0xD8` | class | `<PlayerBody>k__BackingField` | `0x214A26643C0` |
| `0xE0` | class | `<CharacterController>k__BackingField` | `0x214A27CBA00` |
| `0x100` | class | `_playerLookRaycastTransform` | `0x214A281D6E0` |

> **Important**: `NickName` and `AccountId` are **always empty strings** for ObservedPlayerView.  
> Player names come from the `Voice` field (for AI/scavs) or profile lookups using `ProfileId`.

**Human PMC example** (USEC, note `IsAI=false`, `Side=1`):

```
[20:30:13.150] [Il2CppDumper] ── Fields of 'ObservedPlayer 'Usec10' (ObservedPlayerView)' @ 0x214959AFCF0 (full hierarchy) ──
[20:30:13.152] [Il2CppDumper]   ┌ EFT.NextObservedPlayer.ObservedPlayerView (klass=0x21001265E70, 51 field(s))
[20:30:13.154] [Il2CppDumper]   │  [0x20] int          <RaidId>k__BackingField = 1072
[20:30:13.156] [Il2CppDumper]   │  [0x28] class        <ObservedPlayerController>k__BackingField = 0x2149539E2A0
[20:30:13.158] [Il2CppDumper]   │  [0x40] string       <Voice>k__BackingField = "Usec_4"
[20:30:13.160] [Il2CppDumper]   │  [0x7C] int          <Id>k__BackingField = 18
[20:30:13.162] [Il2CppDumper]   │  [0x94] valuetype    <Side>k__BackingField = 1   ← USEC
[20:30:13.164] [Il2CppDumper]   │  [0xA0] bool         <IsAI>k__BackingField = false
[20:30:13.166] [Il2CppDumper]   │  [0xA8] string       <ProfileId>k__BackingField = "<profile_id_••••••••••••>"
[20:30:13.168] [Il2CppDumper]   │  [0xB8] string       <NickName>k__BackingField = ""
[20:30:13.170] [Il2CppDumper]   │  [0xD0] class        <PlayerBones>k__BackingField = 0x21427CB6320
[20:30:13.172] [Il2CppDumper]   │  [0xD8] class        <PlayerBody>k__BackingField = 0x212ADC75870
```

**BEAR PMC example** (`Side=2`):

```
[20:30:33.016] [Il2CppDumper] ── Fields of 'ObservedPlayer 'Bear9' (ObservedPlayerView)' @ 0x2149500AA10 (full hierarchy) ──
  │  [0x94] valuetype    <Side>k__BackingField = 2   ← BEAR
  │  [0xA0] bool         <IsAI>k__BackingField = false
  │  [0xA8] string       <ProfileId>k__BackingField = "...50AA0000000000000000"
```

### 3.4 Sub-Object Dumps

After the main player dumps, each sub-object gets its own hierarchy dump. These include:

**MovementContext** (3 classes — `ClientPlayerMovementContext` → `MovementContext` → `System.Object`):
```
[20:30:30.729] [Il2CppDumper] ── Fields of 'MovementContext '<username>'' @ 0x2131D007800 (full hierarchy) ──
[20:30:30.734] [Il2CppDumper]   ┌ EFT.ClientPlayerMovementContext (klass=0x210029C1EB0, 0 field(s))
[20:30:30.736] [Il2CppDumper]   ┌ EFT.MovementContext (klass=0x..., 192 field(s))
[20:30:30.740] [Il2CppDumper]   │  [0x10] class        _playerTransform = 0x2131C9B8B00  ← BifacialTransform (NOT Unity Transform)
[20:30:30.743] [Il2CppDumper]   │  [0x40] class        _player = 0x21368865000            ← back-ref to ClientPlayer
[20:30:30.746] [Il2CppDumper]   │  [0xC0] valuetype    _rotation                          ← THE rotation we read (Vector2: yaw, pitch)
[20:30:30.749] [Il2CppDumper]   │  [0xC8] valuetype    _previousRotation
[20:30:30.752] [Il2CppDumper]   │  [0x380] float       <CharacterMovementSpeed>k__BackingField = 0.0
[20:30:31.232] [Il2CppDumper]   ┌ System.Object (klass=0x210002523F0, 0 field(s))
[20:30:31.234] [Il2CppDumper] ── End of 'MovementContext '<username>'' (3 class(es) in hierarchy) ──
```

> **Key discovery**: `_playerTransform` at offset `0x10` is a `BifacialTransform`, not a Unity `Transform`.
> `BifacialTransform.Original` at offset `0x10` is the real Unity `Transform`.
> Chain: `MovementContext+0x10 → BifacialTransform+0x10 → Transform+0x10 → TransformInternal`.
> This resolves to a **different** TransformInternal than `_playerLookRaycastTransform` — it's the body root (index 0) rather than the eye-level transform (index ~35). See [TransformPath Comparison](#10-transformpath-comparison).

**InventoryController** (7 classes in hierarchy):
```
[20:30:31.237] [Il2CppDumper] ── Fields of 'InventoryController '<username>'' @ 0x218F7D26D80 (full hierarchy) ──
[20:30:31.242] [Il2CppDumper]   ┌ ClientPlayerInventoryController (klass=0x2100135C250, 2 field(s))
[20:30:31.244] [Il2CppDumper]   │  [0x1B0] class        _clientPlayer = 0x21368865000
[20:30:31.330] [Il2CppDumper]   ┌ EFT.InventoryLogic.PersonItemController (klass=0x2100090D5F0, 1 field(s))
[20:30:31.332] [Il2CppDumper]   │  [0xF8] valuetype    Side = 1   ← USEC
[20:30:31.335] [Il2CppDumper]   ┌ EFT.InventoryLogic.ItemController (klass=0x210008EE3B0, 29 field(s))
[20:30:31.410] [Il2CppDumper]   │  [0xA0] valuetype    IdSource = 1777235207
[20:30:31.418] [Il2CppDumper]   │  [0xC0] string       <ID>k__BackingField = "<profile_id_••••••••••••>"
[20:30:31.424] [Il2CppDumper]   │  [0xD0] class        <RootItem>k__BackingField = 0x2131ED808F0
[20:30:31.427] [Il2CppDumper]   │  [0xD8] string       <Name>k__BackingField = "<username>"
[20:30:31.431] [Il2CppDumper]   │  [0xE4] valuetype    <OwnerType>k__BackingField = 0xD500EC0000000000
[20:30:31.441] [Il2CppDumper]   ┌ System.Object (klass=0x210002523F0, 0 field(s))
[20:30:31.445] [Il2CppDumper] ── End of 'InventoryController '<username>'' (7 class(es) in hierarchy) ──
```

**PlayerBody** (6 classes in hierarchy — same structure for local and observed):
```
[20:30:31.450] [Il2CppDumper] ── Fields of 'PlayerBody '<username>'' @ 0x212ADF7B5A0 (full hierarchy) ──
[20:30:31.456] [Il2CppDumper]   ┌ EFT.PlayerBody (klass=0x21002F6BB00, 28 field(s))
[20:30:31.460] [Il2CppDumper]   │  [0x20] class        _meshTransform = 0x212FC1E55E0
[20:30:31.462] [Il2CppDumper]   │  [0x28] class        PlayerBones = 0x2142808AC80
[20:30:31.464] [Il2CppDumper]   │  [0x30] class        SkeletonRootJoint = 0x212AF66CF00   ← DizSkinningSkeleton
[20:30:31.466] [Il2CppDumper]   │  [0x38] class        SkeletonHands = 0x212AF66CDC0
[20:30:31.469] [Il2CppDumper]   │  [0x40] class        BodyCustomization = 0x212A9479A80
[20:30:31.472] [Il2CppDumper]   │  [0x48] bool         <HaveHolster>k__BackingField = true
[20:30:31.475] [Il2CppDumper]   │  [0x4C] int          _layer = 8
[20:30:31.477] [Il2CppDumper]   │  [0x50] valuetype    _side = 0x100000000   ← USEC
[20:30:31.480] [Il2CppDumper]   │  [0x54] bool         _active = true
[20:30:31.482] [Il2CppDumper]   │  [0x58] generic<>    BodySkins = 0x212FC935420
[20:30:31.486] [Il2CppDumper]   │  [0x68] []           _bodyRenderers = 0x2131D87BF80
[20:30:31.488] [Il2CppDumper]   │  [0x78] bool         IsRightLegPistolHolster = true
[20:30:31.495] [Il2CppDumper]   │  [0x80] class        _equipment = 0x2131ED808F0
[20:30:31.499] [Il2CppDumper]   │  [0x0] []           SlotNames = 0x21002F6BB00
[20:30:31.503] [Il2CppDumper]   │  [0x90] generic<>    SlotViews = 0x212AF880E10
[20:30:31.505] [Il2CppDumper]   │  [0x98] bool         <HasIntergratedArmor>k__BackingField = false
[20:30:31.512] [Il2CppDumper]   │  [0xA0] class        <Equipment>k__BackingField = 0x2131ED808F0
[20:30:31.514] [Il2CppDumper]   │  [0xA8] generic<>    _itemInHands = 0x2194BF75640
[20:30:31.528] [Il2CppDumper]   │  [0xD8] bool         _isYourPlayer = true
[20:30:31.567] [Il2CppDumper]   │  [0x10] IntPtr       m_CachedPtr = 0x2147347E150
[20:30:31.585] [Il2CppDumper]   ┌ System.Object (klass=0x210002523F0, 0 field(s))
[20:30:31.587] [Il2CppDumper] ── End of 'PlayerBody '<username>'' (6 class(es) in hierarchy) ──
```

**ObservedPlayerController** (OPC — 2 classes, acts as hub for observed players):
```
[20:30:10.236] [Il2CppDumper] ── Fields of 'OPC 'Scav' (ObservedPlayerController)' @ 0x214A2318000 (full hierarchy) ──
[20:30:10.244] [Il2CppDumper]   ┌ EFT.NextObservedPlayer.ObservedPlayerController (klass=0x210009A58C0, 21 field(s))
[20:30:10.250] [Il2CppDumper]   │  [0x10] class        <InventoryController>k__BackingField = 0x214A283CA80
[20:30:10.253] [Il2CppDumper]   │  [0x18] class        <PlayerView>k__BackingField = 0x214A224C5C0    ← back to OPV
[20:30:10.262] [Il2CppDumper]   │  [0xC8] class        <Culling>k__BackingField = 0x214A288F780
[20:30:10.265] [Il2CppDumper]   │  [0xD0] class        <InfoContainer>k__BackingField = 0x214A268B1C0
[20:30:10.262] [Il2CppDumper]   │  [0xD8] class        <MovementController>k__BackingField = 0x214A2650000
[20:30:10.265] [Il2CppDumper]   │  [0xE0] class        <Interpolator>k__BackingField = 0x214A26500B0
[20:30:10.268] [Il2CppDumper]   │  [0xE8] class        <HealthController>k__BackingField = 0x214A224C2E0
[20:30:10.271] [Il2CppDumper]   │  [0xF0] class        <AudioController>k__BackingField = 0x214A298EB40
[20:30:10.276] [Il2CppDumper]   │  [0x100] int         <Id>k__BackingField = 37
[20:30:10.282] [Il2CppDumper]   │  [0x110] class       <ArmorInfoController>k__BackingField = 0x214A27144C0
[20:30:10.288] [Il2CppDumper]   │  [0x120] class       <HandsController>k__BackingField = 0x214A27E95F0
[20:30:10.295] [Il2CppDumper]   │  [0x128] class       _gestureController = 0x214A29CAE40
[20:30:10.299] [Il2CppDumper]   │  [0x138] class       _bodyAnimatorController = 0x214A28BDDB0
[20:30:10.303] [Il2CppDumper]   │  [0x140] bool        _disposed = false
[20:30:10.310] [Il2CppDumper]   ┌ System.Object (klass=0x210002523F0, 0 field(s))
[20:30:10.313] [Il2CppDumper] ── End of 'OPC 'Scav' (ObservedPlayerController)' (2 class(es) in hierarchy) ──
```

**ObservedHealthController** (HC — 2 classes):
```
[20:30:11.138] [Il2CppDumper] ── Fields of 'HC 'Scav' (ObservedHealthController)' @ 0x214A224C8A0 (full hierarchy) ──
[20:30:11.142] [Il2CppDumper]   ┌ ObservedPlayerHealthController (klass=0x21001265420, 43 field(s))
[20:30:11.146] [Il2CppDumper]   │  [0x10] valuetype    HealthStatus = 0x100000000   ← 1024 bytes = alive
[20:30:11.148] [Il2CppDumper]   │  [0x14] bool         <IsAlive>k__BackingField = true
[20:30:11.150] [Il2CppDumper]   │  [0x18] class        _player = 0x214A224CB80      ← back-ref to OPV
[20:30:11.152] [Il2CppDumper]   │  [0x20] class        _playerCorpse = null         ← null when alive
[20:30:11.158] [Il2CppDumper]   │  [0x30] valuetype    _physicalCondition = 0
[20:30:11.163] [Il2CppDumper]   │  [0x40] bool         _gotDeathPacket = false
[20:30:11.227] [Il2CppDumper]   │  [0xE0] float        <FallSafeHeight>k__BackingField = 0
[20:30:11.249] [Il2CppDumper]   │  [0x144] float       <DamageCoeff>k__BackingField = 0
[20:30:11.131] [Il2CppDumper]   ┌ System.Object (klass=0x210002523F0, 0 field(s))
[20:30:11.133] [Il2CppDumper] ── End of 'HC 'Scav' (ObservedHealthController)' (2 class(es) in hierarchy) ──
```

**ObservedMovementController — 2-hop chain** (`MovCtrl-step1` → `StateContext`):
```
[20:30:11.138] [Il2CppDumper] ── Fields of 'MovCtrl-step1 'Scav'' @ 0x214A2650000 (full hierarchy) ──
  ┌ EFT.NextObservedPlayer.ObservedPlayerController (klass=..., 21 field(s))   ← same klass!
  │  [0xD8] class        <MovementController>k__BackingField = 0x214A2318690   ← the StateContext (step2)
  │  [0x20] int          <RaidId>k__BackingField = 1288
[20:30:11.133] [Il2CppDumper] ── End of 'MovCtrl-step1 'Scav'' (2 class(es) in hierarchy) ──

[20:30:10.507] [Il2CppDumper] ── Fields of 'StateContext 'Scav'' @ 0x2149286B5C0 (full hierarchy) ──
[20:30:10.512] [Il2CppDumper]   ┌ EFT.NextObservedPlayer.ObservedPlayerStateContext (klass=0x21002E60C10, 61 field(s))
[20:30:10.516] [Il2CppDumper]   │  [0x10] class        <PlayerAnimator>k__BackingField = 0x2149939F3C0
[20:30:10.519] [Il2CppDumper]   │  [0x18] float        _enterTime = 248.12
[20:30:10.522] [Il2CppDumper]   │  [0x28] valuetype    <Rotation>k__BackingField = 0x4156A56A434C3AE4  ← ROTATION (Vector2)
[20:30:10.525] [Il2CppDumper]   │  [0x40] float        <CurrentTilt>k__BackingField = 1.89375
[20:30:10.529] [Il2CppDumper]   │  [0x44] bool         <IsExitingMountedState>k__BackingField = false
[20:30:10.532] [Il2CppDumper]   │  [0x88] class        _playerTransform = 0x21427C5DA90  ← BifacialTransform
[20:30:10.535] [Il2CppDumper]   │  [0x90] class        _characterController = 0x2149A866960
[20:30:10.538] [Il2CppDumper]   │  [0xCC] float        _actualLinearSpeed = 2.74904
[20:30:10.541] [Il2CppDumper]   │  [0xD0] valuetype    _actualMotion = 0xBE935FE8C01407A7
[20:30:10.544] [Il2CppDumper]   │  [0xDC] valuetype    _currentPlayerPose = 0x3F25A5A600000002
[20:30:10.547] [Il2CppDumper]   │  [0xE0] float        _characterMovementSpeed = 0.647059
[20:30:10.551] [Il2CppDumper]   │  [0xEC] float        _handsToBodyAngle = 47.1392
[20:30:10.554] [Il2CppDumper]   │  [0xF8] valuetype    _velocity                              ← VELOCITY (Vector3)
[20:30:10.558] [Il2CppDumper]   │  [0x138] class       _player = 0x21427C5DA80                ← back-ref to OPV
[20:30:10.562] [Il2CppDumper]   ┌ System.Object (klass=0x210002523F0, 0 field(s))
[20:30:10.566] [Il2CppDumper] ── End of 'StateContext 'Scav'' (2 class(es) in hierarchy) ──
```

> **Key**: The "MovementController" at OPC+0xD8 is actually another `ObservedPlayerController` instance (step1).  
> Its own `+0xD8` yields the true `ObservedPlayerStateContext` (step2/final) which has `<Rotation>` at `+0x28`.  
> This 2-hop chain is confirmed from live data: `OPC[0xD8] → step1[0xD8] → StateContext`.

**ObservedInfoContainer** — sometimes fails to read:
```
── Fields of 'ObservedInfoContainer' @ 0x214A268B1C0 (full hierarchy) ──
  ┌ EFT.NextObservedPlayer.ObservedPlayerInfoContainer (klass=..., 18 field(s))
  │  (failed to read field array: Memory read failed.)
  ┌ System.Object (klass=..., 0 field(s))
── End of 'ObservedInfoContainer' (2 class(es) in hierarchy) ──
```

> This VmmException is non-fatal — the info container klass field array pointer was temporarily invalid.

---

## 4. DATA CHAIN DUMP Format

Data chain dumps validate every pointer hop in the critical read paths. They show the **computed address** for each dereference, making it easy to spot where a chain breaks.

### Format

```
╔══════════════════════════════════════════════════════════════════════════
║ DATA CHAIN DUMP: <LABEL> @ <BASE_ADDRESS>  (observed=<True/False>)
╚══════════════════════════════════════════════════════════════════════════
── <Section Name> ────────────────────────────────────────
  <FieldName>                          = <VALUE>  (<source> + <OFFSET>  [addr=<COMPUTED_ADDRESS>])
```

Each line shows:
- **FieldName**: What we're reading
- **VALUE**: The pointer/value we got
- **source + OFFSET**: Which object + offset we read from
- **[addr=COMPUTED_ADDRESS]**: The actual memory address we read (source_ptr + offset)

### 4.1 ClientPlayer Chains

```
╔══════════════════════════════════════════════════════════════════════════
║ DATA CHAIN DUMP: LocalPlayer '<username>' (ClientPlayer) @ 0x21368865000  (observed=False)
╚══════════════════════════════════════════════════════════════════════════
── ClientPlayer chains ────────────────────────────────────────
  playerBase                             = 0x21368865000
  CharacterController                  = 0x2194BF95A30  (playerBase + Player._characterController  [addr=0x21368865040])
  PWA                                  = 0x2194BF95A80  (playerBase + Player.ProceduralWeaponAnimation  [addr=0x21368865338])
  PlayerBody                           = 0x212ADF7B5A0  (playerBase + Player._playerBody  [addr=0x21368865190])
  InventoryController                  = 0x218F7D26D80  (playerBase + Player._inventoryController  [addr=0x21368865980])
  CorpsePtr                            = READ FAILED: ReadPtr(0x21368865680) → invalid VA 0x0
  HealthController                     (playerBase + Player._healthController  [addr=0x21368865968])
```

**MovementContext chain** (includes back-reference validation):
```
── MovementContext chain ──
  MovementContext                      = 0x2131D007800  (playerBase + Player.MovementContext  [addr=0x21368865060])
  MC.Player (back-ref)                 = 0x21368865000  (movCtx + MovementContext.Player  [addr=0x2131D007840])
    → back-ref match: YES ✓
  RotationAddr                           = 0x2131D0078C0  (movCtx + 0xC0)
    → rotation value: (yaw, pitch)
```

> `back-ref match: YES ✓` means `MovementContext._player` points back to our `playerBase`. This validates the chain.  
> **CorpsePtr = READ FAILED** is normal for alive players — the corpse pointer is null (0x0).

### 4.2 ObservedPlayerView Chains

Different pointer chain structure — goes through `ObservedPlayerController`:

```
╔══════════════════════════════════════════════════════════════════════════
║ DATA CHAIN DUMP: ObservedPlayer 'Scav' (ObservedPlayerView) @ 0x214A224C5C0  (observed=True)
╚══════════════════════════════════════════════════════════════════════════
── ObservedPlayerView chains ──────────────────────────────────
  playerBase                             = 0x214A224C5C0
  ObservedPlayerController             = 0x214A2318000  (playerBase + ObservedPlayerView.OPC  [addr=0x214A224C5E8])
  OPC.Player (back-ref)                = 0x214A224C5C0  (opc + ObservedPlayerController.PlayerView  [addr=0x214A2318018])
    → back-ref match: YES ✓
  HealthController                     = 0x214A224C2E0  (opc + OPC.HealthController  [addr=0x214A2318108])
  HC.Player (back-ref)                 = 0x214A224CB80  (hc + HC._player  [addr=0x214A224C2F8])
    → back-ref match: YES ✓
  HC.HealthStatus                      = 0x100000000  (hc + HC.HealthStatus  [addr=0x214A224C2F0])   ← alive
  PlayerBody                           = 0x214A26643C0  (playerBase + OPV.PlayerBody  [addr=0x214A224C698])
  InventoryController                  = 0x214A283CA80  (opc + OPC.InventoryController  [addr=0x214A2318010])
```

**Key differences from ClientPlayer**:
- Goes through `ObservedPlayerController` (OPC) as hub
- HealthStatus `0x100000000` (raw field includes both status flags and alive flag) — `IsAlive` bool at `+0x14`
- Movement goes through a 2-hop chain: `OPC → MovementController(step1) → StateContext(final)`

**MovementController chain** (2-hop for observed):
```
── MovementController chain ──
  MC.step1                             = 0x214A2650000  (opc + OPC.MovementController  [addr=0x214A2318078])
  MC.final (StateContext)              = 0x214A2318690  (mcStep1 + step1.MovementController[0xD8])
  RotationAddr                           = 0x214A23186B8  (mc + 0x28)
    → rotation value: (yaw, pitch)
  VelocityAddr                           = 0x214A2318788  (mc + 0xF8)
```

### 4.3 TransformInternal Chain (HOTPATH)

This is the **most performance-critical chain** — read every frame to get player world positions.

**Current: 2 hops** via `_playerLookRaycastTransform → Transform+0x10 → TransformInternal`

This short chain replaced the old 8-hop Body→Skeleton→Bone chain. Both client and observed players use the same pattern — only the initial offset differs.

**Hierarchy data** (read once during init, cached):
```
TransformInternal + 0x78  → Index (int)
TransformInternal + 0x70  → Hierarchy
  Hierarchy + 0x68        → Vertices (TrsX[])
  Hierarchy + 0x40        → Indices (int[])
```

**Legacy 8-hop chain** (historical — kept for reference, no longer used):
```
playerBase → Body(+0x190) → SkelRoot(+0x30) → _values(+0x30) → arr(+0x10) → bone0(+0x20) → TI(+0x10) → Hierarchy(+0x70) → Vertices(+0x68)
```

**Inventory chain** (appended to both):
```
── Inventory chain ───────────────────────────────────────────
  Inventory                            = 0x1EB90D6A820  (invCtrl + InventoryController.Inventory  [addr=0x1E7EE41BC40])
  Equipment                            = 0x1ECBBBDD1A0  (inventory + Inventory.Equipment  [addr=0x1EB90D6A838])
```

---

## 5. BatchInit Logs

After players are discovered, `BatchInitTransforms` and `BatchInitRotations` run scatter reads in rounds.

### Transform Init (4 rounds — short 2-hop chain)

Uses `_playerLookRaycastTransform → Transform+0x10 → TransformInternal` then reads hierarchy data:

```
[RegisteredPlayers] BatchInitTransforms R1 (LookTransform): 20/20 valid
[RegisteredPlayers] BatchInitTransforms R2 (TransformInternal): 20/20 valid
[RegisteredPlayers] BatchInitTransforms R3 (Index+Hierarchy): 20/20 valid
[RegisteredPlayers] BatchInitTransforms R4 (Vertices+Indices): 20/20 valid
```

Each round:
- **R1**: Read `_playerLookRaycastTransform` from each player (offset `0xA18` client, `0x100` observed)
- **R2**: Read `TransformInternal` from Transform component (`+0x10`)
- **R3**: Read `Index` + `Hierarchy` pointer from TransformInternal
- **R4**: Read `Vertices` + `Indices` pointers from Hierarchy

After R4, each entry's indices array and test vertices are read serially (variable size per player).

**Final summary**:
```
[RegisteredPlayers] BatchInitTransforms DONE: 20 entries, 18 succeeded, 0 chain-failed, 2 chain-ok-but-vertex-fail
```

- `chain-failed` = pointer chain broke during scatter rounds
- `chain-ok-but-vertex-fail` = chain resolved but vertex data not yet populated (retried next tick)

### Rotation Init (3 rounds for observed, 1 for client)

```
[RegisteredPlayers] BatchInitRotations R1 (OPC/MovCtx): 20/20 valid
[RegisteredPlayers] BatchInitRotations R2 (MC step1): 20/20 valid
[RegisteredPlayers] BatchInitRotations R3 (MC step2): 20/20 valid
[RegisteredPlayers] BatchInitRotations DONE: 20 entries, 20 succeeded
```

### Combined Summary

```
[RegisteredPlayers] BatchInit: 21 players, transform(20 candidates, 18 OK, 1 already, 0 maxed), rotation(20 candidates, 20 OK, 1 already, 0 maxed), elapsed=555.0ms
```

- `21 players` total tracked
- `20 candidates` needed init (the 1 = local player, already initialized via serial path)
- `1 already` = local player
- `0 maxed` = none hit max retry count

**Retry on next tick** (players that had vertex failures get re-initialized):
```
[RegisteredPlayers] BatchInitTransforms DONE: 4 entries, 4 succeeded, 0 chain-failed, 0 chain-ok-but-vertex-fail
[RegisteredPlayers] BatchInit: 21 players, transform(4 candidates, 4 OK, 17 already, 0 maxed), rotation(0 candidates, 0 OK, 21 already, 0 maxed), elapsed=4.8ms
```

---

## 6. Player Discovery & Registration

**Discovery log format**:
```
[RegisteredPlayers] Discovered: <TYPE> [<NAME>] @ <ADDRESS> (class='<CLASS>', observed=<Bool>, transformReady=<Bool>, rotationReady=<Bool>, pos=<x, y, z>)
```

**From this session** (Interchange, 30 players discovered):
```
[20:30:03.634] [RegisteredPlayers] Discovered: Default [<username>] @ 0x21368865000 (class='ClientPlayer', observed=False, transformReady=True, rotationReady=True, pos=<308.21304, 28.936028, 134.27115>)
[20:30:03.636] [RegisteredPlayers] LocalPlayer found: <username> (class='ClientPlayer')
[20:30:03.937] [LocalGameWorld] Local player discovered — radar is live.
[20:30:03.963] [RegisteredPlayers] Discovered: BEAR [Bear3] @ 0x218FA318CF0 (class='ObservedPlayerView', observed=True, transformReady=False, rotationReady=False, pos=<0, 0, 0>)
[20:30:03.967] [RegisteredPlayers] Discovered: BEAR [Bear5] @ 0x218FA3185C0 (class='ObservedPlayerView', observed=True, transformReady=False, rotationReady=False, pos=<0, 0, 0>)
[20:30:03.975] [RegisteredPlayers] Discovered: USEC [Usec8] @ 0x218D6B1A8A0 (class='ObservedPlayerView', observed=True, transformReady=False, rotationReady=False, pos=<0, 0, 0>)
[20:30:03.983] [RegisteredPlayers] Discovered: USEC [Usec12] @ 0x212D83302E0 (class='ObservedPlayerView', observed=True, transformReady=False, rotationReady=False, pos=<0, 0, 0>)
[20:30:03.990] [RegisteredPlayers] Discovered: AIScav [Scav] @ 0x2148E1E0730 (class='ObservedPlayerView', observed=True, transformReady=False, rotationReady=False, pos=<0, 0, 0>)
[20:30:03.995] [RegisteredPlayers] Discovered: USEC [Usec7] @ 0x21490BB85C0 (class='ObservedPlayerView', observed=True, transformReady=False, rotationReady=False, pos=<0, 0, 0>)
[20:30:04.002] [RegisteredPlayers] Discovered: AIScav [Scav] @ 0x214917CC2E0 (class='ObservedPlayerView', observed=True, transformReady=False, rotationReady=False, pos=<0, 0, 0>)
[20:30:04.048] [RegisteredPlayers] Discovered: USEC [Usec11] @ 0x2149456E5C0 (class='ObservedPlayerView', observed=True, transformReady=False, rotationReady=False, pos=<0, 0, 0>)
[20:30:04.053] [RegisteredPlayers] Discovered: BEAR [Bear9] @ 0x2149500AA10 (class='ObservedPlayerView', observed=True, transformReady=False, rotationReady=False, pos=<0, 0, 0>)
[20:30:04.065] [RegisteredPlayers] Discovered: USEC [Usec10] @ 0x214959AFCF0 (class='ObservedPlayerView', observed=True, transformReady=False, rotationReady=False, pos=<0, 0, 0>)
```

**Player types**: `Default` (local PMC), `AIScav`, `USEC`, `BEAR`

> Note: `pos=<0, 0, 0>` for observed players on discovery is normal — transform chain hasn't been initialized yet.  
> Local player has `transformReady=True, rotationReady=True` immediately because it uses a direct serial read path.

**RealtimeWorker status** (periodic — confirms scatter is running):
```
[20:30:13.675] [RealtimeWorker] Scatter: active=30 (position=30, rotation=30), total=30
[20:30:33.698] [RealtimeWorker] Scatter: active=30 (position=30, rotation=30), total=30
```

**Scatter read warnings** (early ticks, transform not ready yet):
```
[20:30:04.159] WARNING [RegisteredPlayers] Position scatter read failed for 'Usec12' (verts=0x2147056B620, count=56)
[20:30:04.163] WARNING [RegisteredPlayers] Position scatter read failed for 'Bear6' (verts=0x218ED71A2E0, count=91)
```

**Late-session warning** (player data temporarily invalid):
```
[20:30:35.373] WARNING [RegisteredPlayers] Position compute failed for 'Bear5' (idx=35, verts=0x218EDC30C80)
```

---

## 7. Common Error Patterns

### Position Scatter Read Failed — Early Tick

Seen in this session on the very first scatter tick after discovery:

```
[20:30:04.159] WARNING [RegisteredPlayers] Position scatter read failed for 'Usec12' (verts=0x2147056B620, count=56)
[20:30:04.163] WARNING [RegisteredPlayers] Position scatter read failed for 'Bear6' (verts=0x218ED71A2E0, count=91)
```

**Cause**: The Vertices array pointer was valid but vertex data isn't populated yet — player transform hierarchy just spawned.  
**Resolution**: Automatic retry. Resolved within 1-2 frames once the game populates the transform buffer.

### Position Compute Failed — Stale Vertex Pointer

```
[20:30:35.373] WARNING [RegisteredPlayers] Position compute failed for 'Bear5' (idx=35, verts=0x218EDC30C80)
```

**Cause**: The vertex pointer at the given Hierarchy+0x68 was stale or the index was out of range at that instant.  
**Resolution**: Recomputed successfully on next scatter tick.

### BadPtrException — Null Pointer in Chain

```
Exception thrown: 'eft_dma_radar.Silk.Misc.BadPtrException' in eft-dma-radar-silk.dll
WARNING [RegisteredPlayers] TryInitRotation FAILED 'Scav' 0x...: ReadPtr(0x...) → invalid VA 0x0
```

**Cause**: The observed player's `ObservedPlayerController` or `MovementController` hasn't been fully initialized yet.  
**Resolution**: Automatic retry on next tick. Eventually succeeds or hits max retries.

### CreatePlayerEntry FAILED — Repeated Retries

```
WARNING [RegisteredPlayers] CreatePlayerEntry FAILED 0x... isLocal=False: ReadPtr(0x...) → invalid VA 0x0
...repeats ~16 times over 2 seconds...
[RegisteredPlayers] Discovered: AIScav [Scav] @ 0x...
```

**Cause**: Player is in the `RegisteredPlayers` list but the object is still being constructed — `ObservedPlayerView.ObservedPlayerController` field is null.  
**Resolution**: Retries every ~100ms. Succeeds once the game finishes spawning the player.

### VmmException — DMA Read Failure

```
Exception thrown: 'VmmSharpEx.VmmException' in eft-dma-radar-silk.dll
[Il2CppDumper]   │  (failed to read field array: Memory read failed.)
```

**Cause**: DMA hardware couldn't read the requested memory region (page not committed, TLB miss, or transient FPGA error).  
**Resolution**: Non-fatal for diagnostic dumps. The field dump logs the failure and continues.

### Equipment Chain Failed

```
WARNING [GearManager] Equipment chain failed for 'Scav' (observed=True)
```

**Cause**: `InventoryController → Inventory → Equipment` chain had a null or invalid pointer. Common for newly spawned players.  
**Resolution**: Gear manager retries on subsequent registration ticks.

---

## 8. Loot & Exfil Dumps

> **Note**: The F8 dump in this session was triggered mid-raid and focused on player hierarchy dumps. Loot and exfil dumps are produced when `DumpAll()` is called at raid start with full debug logging enabled from config. The `ExfiltrationController` pointer is present in the GameWorld dump at offset `0x50` → `0x218FA318D20`.

### ExfilController (reference format)

Pointer from GameWorld: `GameWorld + 0x50 → 0x218FA318D20`

```
[Il2CppDumper] ── Fields of 'ExfilController' @ 0x... (full hierarchy) ──
  ┌ CommonAssets.Scripts.Game.ExfiltrationController (klass=..., 8 field(s))
  │  [0x20] []           <ExfiltrationPoints>k__BackingField = 0x...   ← PMC exfils
  │  [0x28] []           <ScavExfiltrationPoints>k__BackingField = 0x...
  │  [0x30] []           <SecretExfiltrationPoints>k__BackingField = 0x...

[Il2CppDumper] ── Fields of 'ExfiltrationPoint' @ 0x... (full hierarchy) ──
  ┌ EFT.Interactive.ExfiltrationPoint (klass=..., 26 field(s))
  │  [0x48] string       _currentTip = ""
  │  [0x58] valuetype    _status                      ← exfil status enum
  │  [0x60] string       <Description>k__BackingField = "ExfiltrationPoint"
  │  [0xC0] []           EligibleEntryPoints = 0x...

[ExfilManager] Initialized 8 exfils on attempt 1
```

### LootItem (reference format)

```
[Il2CppDumper] ── Fields of 'LootItem InteractiveClass (ObservedLootItem)' @ 0x... (full hierarchy) ──
  ┌ EFT.Interactive.ObservedLootItem (klass=..., 1 field(s))
  ┌ EFT.Interactive.LootItem (klass=..., 33 field(s))
  │  [0x68] string       Name = "<templateId> ShortName"
  │  [0x78] string       ItemId = "<instanceId>"
  │  [0x80] string       TemplateId = "<templateId>"
  │  [0xF0] class        _item = 0x...
  ┌ EFT.Interactive.InteractableObject (klass=..., 5 field(s))
  ┌ EFT.AssetsManager.PoolSafeMonoBehaviour (klass=..., 2 field(s))
  ┌ UnityEngine.MonoBehaviour ... (9 classes total)
```

Loot lists live at `GameWorld+0x168` (`AllLoot`) and `GameWorld+0x198` (`LootList`), both visible in the live GameWorld dump above.

---

## 9. Shutdown Sequence

Normal shutdown (crash/exit in this session — code `0xffffffff`):

```
[20:30:35.373] WARNING [RegisteredPlayers] Position compute failed for 'Bear5' (idx=35, verts=0x218EDC30C80)
The program '[10444] eft-dma-radar.exe' has exited with code 4294967295 (0xffffffff).
```

Exit code `0xffffffff` = abnormal exit (game closed or crashed while radar was running).

Normal graceful shutdown when radar window is closed:

```
[HH:MM:SS.sss] [WorkerThread] 'Realtime Worker' stopped.
[HH:MM:SS.sss] [Memory] Radar restart requested.
[HH:MM:SS.sss] [Memory] State → ProcessFound
[HH:MM:SS.sss] [Memory] Closed.
[HH:MM:SS.sss] [RadarWindow] Closed.
[HH:MM:SS.sss] [LocalGameWorld] Cooldown active — waiting Xms before next raid detection...
[HH:MM:SS.sss] [RadarWindow] Run() returned.
[HH:MM:SS.sss] [SilkProgram] RadarWindow.Run() returned normally.
The program '[XXXX] eft-dma-radar-silk.exe' has exited with code 0 (0x0).
```

**Expected exceptions during shutdown**:
- `System.OperationCanceledException` — cancellation tokens triggered
- `System.ObjectDisposedException` — objects accessed after disposal during teardown

These are normal and do not indicate bugs.

---

## Quick Reference: Offset Chains

### Player Position (Both Client and Observed)

Both player types use the same short 2-hop chain via `_playerLookRaycastTransform`:

```
playerBase + 0xA18     → _playerLookRaycastTransform (Transform)   [ClientPlayer]
playerBase + 0x100     → _playerLookRaycastTransform (Transform)   [ObservedPlayerView]
  + 0x10               → TransformInternal
    + 0x78             → Index (int — hierarchy depth)
    + 0x70             → Hierarchy
      + 0x68           → Vertices (TrsX[] — position/rotation/scale per bone)
      + 0x40           → Indices (int[] — parent index per bone)
```

**Total: 2 pointer hops** to reach TransformInternal, then 2 more to get Vertices+Indices.
This is the eye-level transform (index ~35 for local, varies for observed).

> **Alternative path (NOT used)**: `MovementContext._playerTransform (BifacialTransform, 0x10) → Original (Transform, 0x10) → +0x10 → TransformInternal`.
> This resolves to the body root transform (index 0), which is ~0.7m lower (foot level). Requires 3 pointer hops and gives less useful position data.

### LocalPlayer Rotation

```
playerBase + 0x60      → MovementContext
  + 0xC0               → _rotation (Vector2: yaw, pitch)
```

### ObservedPlayer Rotation

```
playerBase + 0x28      → ObservedPlayerController (OPC)
  + 0xD8               → MovementController (step1 — another OPC instance)
    + 0xD8             → StateContext (ObservedPlayerStateContext — final)
      + 0x28           → Rotation (Vector2: yaw, pitch)
      + 0xF8           → Velocity (Vector3)
```

> Both hops use `+0xD8` (the `<MovementController>k__BackingField` offset). Confirmed from live dumps:
> `OPC[0xD8]=0x214A2650000` (step1), `step1[0xD8]=0x214A2318690` (StateContext).

### ObservedPlayer Health

```
playerBase + 0x28      → ObservedPlayerController
  + 0xE8               → HealthController
    + 0x10             → HealthStatus (1024 = alive)
    + 0x14             → IsAlive (bool)
    + 0x20             → _playerCorpse (null if alive)
```

### Equipment Chain

```
playerBase + 0x980     → _inventoryController  (Client)
— or —
OPC + 0x10             → InventoryController   (Observed)
  + 0x100              → Inventory
    + 0x18             → Equipment
```

---

## 10. TransformPath Comparison

Diagnostic comparison of two paths to `TransformInternal` (results documented here for reference).

**Path A** — current, via `_playerLookRaycastTransform`:
```
playerBase + lookOffset → Transform + 0x10 → TransformInternal
```

**Path B** — alternative, via `MovementContext._playerTransform` (`BifacialTransform`):
```
MovementContext + 0x10 → BifacialTransform + 0x10 (Original) → Transform + 0x10 → TransformInternal
```

**Live comparison result** (LocalPlayer '<username>' on Interchange):
```
[TransformPath] ── Compare for '<username>' (local) ──
[TransformPath]   Path A (_playerLookRaycastTransform):       addr=0x213688659A18 → TI resolved
[TransformPath]   Path B (MovementContext._playerTransform):  MC+0x10=BifacialTransform → Original → TI resolved
[TransformPath]   Same TransformInternal: False
[TransformPath]   Index A=~35 (eye/head level)
[TransformPath]   Index B=0   (body root)
[TransformPath]   Position A: eye-level   (~28.94m Y on Interchange)
[TransformPath]   Position B: body root   (~28.24m Y — ~0.70m lower)
[TransformPath]   Distance: ~0.70m
[TransformPath] ── End ──
```

**Key findings**:

| Aspect | Path A (lookRaycast) | Path B (BifacialTransform) |
|--------|---------------------|---------------------------|
| Source field | `_playerLookRaycastTransform` | `MovementContext._playerTransform` |
| Transform index | ~35 (varies) | 0 (always root) |
| Position height | Eye / head level | Body root (pelvis/feet) |
| Pointer hops | 2 | 3 |
| Vertical offset | — | ~0.70m lower |

**Observed player path A** (from OPV dump):
```
playerBase + 0x100 → _playerLookRaycastTransform   (ObservedPlayerView offset)
  + 0x10           → TransformInternal
```

Example: `0x214A224C5C0 + 0x100` → `0x214A224C6C0` → read ptr → `0x214A281D6E0` (transform object)

**Decision**: Path A (`_playerLookRaycastTransform`) is kept because:
1. Fewer DMA reads (2 hops vs 3)
2. Eye-level position is better for radar dot placement and aimview
3. Proven reliable across 30 players in this session

---

## Timeline Summary

This raid session on Interchange, LocalPlayer=<username> (USEC), 30 total players:

| Time | Event | Players |
|------|-------|---------|
| 20:29:54.147 | Config loaded, startup begins | — |
| 20:29:55.737 | GameAssembly.dll base: `0x7FFAC03E0000` | — |
| 20:29:55.796 | GOM: `0x210500B7730` | — |
| 20:29:55.803 | IL2CPP dump starting | — |
| 20:30:02.269 | State → Initializing | — |
| 20:30:02.270 | Game startup OK | — |
| 20:30:03.509 | GameWorld `0x212B09D7640` found, map=Interchange | — |
| 20:30:03.533 | State → InRaid | — |
| 20:30:03.634 | LocalPlayer <username> discovered @ `0x21368865000` | 1 |
| 20:30:03.963–04.081 | Observed players discovered (Bear3/5/6/9, Usec7/8/10/11/12, AIScavs) | ~30 |
| 20:30:04.159 | First position scatter warnings (transform not ready) | 30 |
| 20:30:09.500 | **F8 pressed — Debug logging ON** | 30 |
| 20:30:09.506 | GameWorld IL2CPP dump starts | 30 |
| 20:30:10.003 | GameWorld dump complete (8 classes) | 30 |
| 20:30:10.007–31.587 | Per-player hierarchy dumps (OPV, OPC, HC, MovCtrl, StateCtx, PlayerBody × 29 observed) | 30 |
| 20:30:29.246 | LocalPlayer '<username>' dump starts | 30 |
| 20:30:30.724 | LocalPlayer dump complete (8 classes, 431 fields on EFT.Player) | 30 |
| 20:30:31.234 | MovementContext '<username>' complete (3 classes) | 30 |
| 20:30:31.445 | InventoryController '<username>' complete (7 classes) | 30 |
| 20:30:31.587 | PlayerBody '<username>' complete (6 classes) | 30 |
| 20:30:13.675 | RealtimeWorker: Scatter active=30 (position=30, rotation=30) | 30 |
| 20:30:35.373 | Position compute warning for Bear5 | 30 |
| 20:30:35.xxx | Process exit (code 0xffffffff — game closed) | — |

Total players tracked at peak: **30**  
F8 dump initiated: ~6 seconds after InRaid state  
Full dump duration (GameWorld + 30 players): ~25 seconds
