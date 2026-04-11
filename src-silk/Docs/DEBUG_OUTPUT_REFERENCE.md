# Debug Output Reference

Real annotated output from a live Interchange raid session.  
Use this when debugging memory reads, verifying offset chains, or understanding the IL2CPP dump format.

> **Source**: Full `Debug` output window in Visual Studio, captured during a standard online raid.

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

---

## 1. Startup Sequence

Normal boot → DMA init → process attach → GOM resolve → IL2CPP dump → raid detection:

```
[00:19:05.272] [SilkConfig] Config loaded OK.
[00:19:05.279] [SilkProgram] Config loaded OK.
[00:19:05.399] [SilkProgram] High performance mode set.
[00:19:05.414] [Memory] Initializing DMA...
[00:19:06.213] [Memory] State → WaitingForProcess
[00:19:06.215] [Memory] DMA initialized OK.
[00:19:06.238] [Memory] Worker thread started.
[00:19:06.334] [EftDataManager] Loaded 5000 items.
[00:19:06.350] [MapManager] Loaded 13 map configs (16 IDs), skipped 0.
[00:19:06.360] [RadarWindow] Initialize starting...
[00:19:06.363] [RadarWindow] Creating window: 1920x1009, FPS=60, API=Silk.NET.Windowing.GraphicsAPI
[00:19:06.611] [RadarWindow] OpenGL: 3.3.0 - Build 32.0.101.7085
[00:19:06.657] [RadarWindow] SkiaSharp GPU context ready.
```

**Key milestone**: `GameAssembly.dll base` is the VA of the game module, used for all RVA calculations:

```
[00:19:06.746] [Memory] GameAssembly.dll base: 0x7FFD6E620000
[00:19:06.789] [GOM] Located via direct sig: mov [rip+rel32],rax (GOM init store)
[00:19:06.791] [Memory] GOM: 0x1E1400B7730
```

---

## 2. IL2CPP Cache & Offset Resolution

The fast-path: if the PE checksum matches the cached dump, all offsets are applied instantly:

```
[00:19:06.796] [Il2CppDumper] Dump starting...
[00:19:06.816] [Il2CppDumper] Fast cache loaded (PE match) — 378/378 fields applied.
[00:19:06.817] [Memory] State → Initializing
```

- **378 fields** = total schema fields across all `SchemaClass` definitions in `Il2CppDumperSchema.cs`
- Cache file: `%AppData%\eft-dma-radar-silk\il2cpp_offsets.json`
- If PE changes (game update), a full scatter-based re-dump runs instead

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

First dump after raid detection — the `ClientLocalGameWorld` object:

```
[00:19:06.939] [Il2CppDumper] ── Fields of 'ClientLocalGameWorld (raid start)' @ 0x1E3CC9DD960 (full hierarchy) ──
[00:19:06.964] [Il2CppDumper]   ┌ ClientNetworkGameWorld (klass=0x1E162B82DE0, 0 field(s))
[00:19:06.967] [Il2CppDumper]   ┌ EFT.ClientGameWorld (klass=0x1E1611A1AC0, 4 field(s))
[00:19:06.992] [Il2CppDumper]   │  [0x2E0] float        <LastServerWorldTime>k__BackingField = 56.9178
[00:19:06.998] [Il2CppDumper]   │  [0x2F8] ulong        _totalOutgoingBytes = 0x7710
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

| Offset | Type | Name | Purpose |
|--------|------|------|---------|
| `0xD0` | string | `<LocationId>k__BackingField` | Map ID (`"Interchange"`) |
| `0x1B8` | generic<> | `RegisteredPlayers` | Player list we iterate |
| `0x210` | class | `MainPlayer` | Local player pointer |
| `0x58` | class | `<ExfiltrationController>k__BackingField` | Exfil manager |
| `0x168` | generic<> | `AllLoot` | Loot items list |
| `0x198` | generic<> | `LootList` | Alternative loot list |

**Static fields** always show `[0x0]` offset — they live in a separate static fields region, not on the instance:

```
  │  [0x0] int          _obstaclesCollider = 0
  │  [0x8] int          _interactiveLootMask = 0
```

### 3.2 ClientPlayer (LocalPlayer)

The local player is a `ClientPlayer` which inherits a deep chain. **427 fields** on `EFT.Player` alone:

```
[00:19:07.271] [Il2CppDumper] ── Fields of 'LocalPlayer (ClientPlayer)' @ 0x1EAF22D5000 (full hierarchy) ──
[00:19:07.273] [Il2CppDumper]   ┌ EFT.ClientPlayer (klass=0x1E1608C92F0, 66 field(s))
  ...
[00:19:07.406] [Il2CppDumper]   ┌ EFT.NetworkPlayer (klass=0x1E1608CA3A0, 3 field(s))
  ...
[00:19:07.414] [Il2CppDumper]   ┌ EFT.Player (klass=0x1E1608B0040, 427 field(s))
  ...
[00:19:08.246] [Il2CppDumper]   ┌ UnityEngine.MonoBehaviour (klass=0x1E16088E230, 1 field(s))
  ...
[00:19:08.284] [Il2CppDumper]   ┌ System.Object (klass=0x1E1602523F0, 0 field(s))
[00:19:08.310] [Il2CppDumper] ── End of 'LocalPlayer (ClientPlayer)' (8 class(es) in hierarchy) ──
```

**Hierarchy** (8 classes):
1. `EFT.ClientPlayer` — 66 fields (network sync, client-specific)
2. `EFT.NetworkPlayer` — 3 fields
3. `EFT.Player` — 427 fields (the monster class)
4–8. Unity base classes + System.Object

**Key fields from EFT.Player** (used by radar):

| Offset | Type | Name | Live Value |
|--------|------|------|------------|
| `0x40` | class | `_characterController` | `0x1EB9E31A7E0` |
| `0x60` | class | `<MovementContext>k__BackingField` | `0x1E472C92000` |
| `0x190` | class | `_playerBody` | `0x1EB8DC49690` |
| `0x5F8` | class | `<GameWorld>k__BackingField` | `0x1E3CC9DD960` |
| `0x900` | int | `<PlayerId>k__BackingField` | `2` |
| `0x980` | class | `_inventoryController` | `0x1E7EE41BB40` |
| `0x988` | class | `_handsController` | `0x1E472A7FA80` |
| `0xA91` | bool | `<IsYourPlayer>k__BackingField` | `true` |

**`<unreadable>` fields**: Many fields in `EFT.Player` show `<unreadable>` — this means the field name pointer couldn't be resolved (likely obfuscated by BSG). The type and offset are still valid:

```
  │  [0x309] bool         <unreadable> = false
  │  [0x310] class        <unreadable> = 0x1EBA25C7F00
  │  [0x338] class        <unreadable> = 0x1E7684FCA80   ← this is ProceduralWeaponAnimation at Player offset 0x338
```

### 3.3 ObservedPlayerView (Network Players)

Network (non-local) players use the `ObservedPlayerView` component — a completely different class hierarchy:

```
[00:19:09.407] [Il2CppDumper] ── Fields of 'ObservedPlayer (ObservedPlayerView)' @ 0x1E3D7467CF0 (full hierarchy) ──
[00:19:09.409] [Il2CppDumper]   ┌ EFT.NextObservedPlayer.ObservedPlayerView (klass=0x1E16126CA80, 51 field(s))
  ...
[00:19:09.533] [Il2CppDumper]   ┌ UnityEngine.MonoBehaviour (klass=0x1E16088E230, 1 field(s))
  ...
[00:19:09.577] [Il2CppDumper] ── End of 'ObservedPlayer (ObservedPlayerView)' (6 class(es) in hierarchy) ──
```

**Hierarchy** (6 classes — much flatter than ClientPlayer):
1. `EFT.NextObservedPlayer.ObservedPlayerView` — 51 fields
2–6. Unity base classes + System.Object

**Key fields**:

| Offset | Type | Name | Example |
|--------|------|------|---------|
| `0x28` | class | `<ObservedPlayerController>k__BackingField` | `0x1E7ED74A2A0` |
| `0x40` | string | `<Voice>k__BackingField` | `"BossTagilla"` |
| `0x7C` | int | `<Id>k__BackingField` | `1` |
| `0xA0` | bool | `<IsAI>k__BackingField` | `true` |
| `0xA8` | string | `<ProfileId>k__BackingField` | `"100800000000000000000000"` |
| `0xB8` | string | `<NickName>k__BackingField` | `""` (empty for network players!) |
| `0xC0` | string | `<AccountId>k__BackingField` | `""` (empty!) |
| `0xD8` | class | `<PlayerBody>k__BackingField` | `0x1EB8DC49B40` |
| `0xE0` | class | `<CharacterController>k__BackingField` | `0x1EB84621C80` |

> **Important**: `NickName` and `AccountId` are **always empty strings** for ObservedPlayerView.
> Player names come from the `Voice` field (for bosses) or the `Id` field combined with profile lookups.

**Human PMC example** (note `IsAI=false`):

```
[00:19:10.305] [Il2CppDumper] ── Fields of 'ObservedHumanPMC (ObservedPlayerView)' @ 0x1EAF6A59170 (full hierarchy) ──
  │  [0x40] string       <Voice>k__BackingField = "Usec_1"
  │  [0x7C] int          <Id>k__BackingField = 4
  │  [0xA0] bool         <IsAI>k__BackingField = false
  │  [0xA8] string       <ProfileId>k__BackingField = "108800000000000000000000"
```

### 3.4 Sub-Object Dumps

After the main player dump, each sub-object gets its own hierarchy dump. These include:

**Profile** (2 classes in hierarchy):
```
── Fields of 'Profile' @ 0x1E3DBAA4730 (full hierarchy) ──
  ┌ EFT.Profile (klass=0x1E162C08F80, 39 field(s))
  │  [0x10] string       Id = "5e1351fe246aeb28245760ab"
  │  [0x18] string       AccountId = "2909689"
  │  [0x48] class        Info = 0x1EAF20BD9A0
  │  [0x70] class        Inventory = 0x1EB90D6A820
```

**PlayerInfo** (2 classes):
```
── Fields of 'PlayerInfo' @ 0x1EAF20BD9A0 (full hierarchy) ──
  ┌ EFT.<unknown> (klass=0x1E1624DA460, 25 field(s))
  │  [0x10] string       Nickname = "Nadeus"
  │  [0x28] string       EntryPoint = "MallSE"
  │  [0x60] string       GameVersion = "standard"
  │  [0x68] valuetype    Type               ← PlayerType enum (PMC/Scav/Boss)
```

> Note: PlayerInfo class shows as `EFT.<unknown>` because the actual class name is obfuscated.

**MovementContext** (3 classes — `ClientPlayerMovementContext` → `MovementContext` → `System.Object`):
```
── Fields of 'MovementContext' @ 0x1E472C92000 (full hierarchy) ──
  ┌ EFT.MovementContext (klass=0x1E1612263C0, 192 field(s))
  │  [0x40] class        _player = 0x1EAF22D5000       ← back-reference to player
  │  [0xC0] valuetype    _rotation                       ← THE rotation we read (Vector2)
  │  [0xC8] valuetype    _previousRotation
  │  [0x380] float       <CharacterMovementSpeed>k__BackingField = 0.6434
```

**HealthController** — different for client vs observed:

Client (4 classes in hierarchy):
```
── Fields of 'HealthController (client)' @ 0x1E3DB438170 (full hierarchy) ──
  ┌ EFT.HealthSystem.ClientPlayerHealthController (klass=..., 1 field(s))
  │  [0x160] class        _player = 0x1EAF22D5000
  ┌ EFT.HealthSystem.NetworkHealthController (klass=..., 18 field(s))
  ┌ EFT.HealthSystem.BaseHealthController`1 (klass=..., 32 field(s))
  │  [0x24] bool         <IsAlive>k__BackingField = true
```

Observed (2 classes):
```
── Fields of 'ObservedHealthController' @ 0x1E3D7467A10 (full hierarchy) ──
  ┌ ObservedPlayerHealthController (klass=..., 43 field(s))
  │  [0x10] valuetype    HealthStatus               ← value 1024 = alive
  │  [0x14] bool         <IsAlive>k__BackingField = true
  │  [0x18] class        _player = 0x1E3D7467CF0    ← back-ref to ObservedPlayerView
  │  [0x20] class        _playerCorpse = null        ← null when alive
```

**ObservedPlayerController** (2 classes — acts as the "hub" for observed players):
```
── Fields of 'ObservedPlayerController' @ 0x1E7ED74A2A0 (full hierarchy) ──
  ┌ EFT.NextObservedPlayer.ObservedPlayerController (klass=..., 21 field(s))
  │  [0x10] class        <InventoryController>k__BackingField = 0x1EA662C08C0
  │  [0x18] class        <PlayerView>k__BackingField = 0x1E3D7467CF0    ← back to OPV
  │  [0xD0] class        <InfoContainer>k__BackingField = 0x1EA7135E7E0
  │  [0xD8] class        <MovementController>k__BackingField = 0x1EA7006DA50
  │  [0xE8] class        <HealthController>k__BackingField = 0x1E3D7467A10
  │  [0x120] class       <HandsController>k__BackingField = 0x1EA710ED850
```

**ObservedMovementController** (2 classes — `ObservedPlayerStateContext`):
```
── Fields of 'ObservedMovementController' @ 0x1E3CC873B80 (full hierarchy) ──
  ┌ EFT.NextObservedPlayer.ObservedPlayerStateContext (klass=..., 61 field(s))
  │  [0x28] valuetype    <Rotation>k__BackingField          ← the rotation we read
  │  [0xF8] valuetype    _velocity                           ← the velocity we read
  │  [0xE0] float        _characterMovementSpeed = 0.643137
  │  [0x138] class       _player = 0x1E3D7467CF0            ← back-ref
```

**ObservedInfoContainer** — sometimes fails to read:
```
── Fields of 'ObservedInfoContainer' @ 0x1EA7135E7E0 (full hierarchy) ──
  ┌ EFT.NextObservedPlayer.ObservedPlayerInfoContainer (klass=..., 18 field(s))
  │  (failed to read field array: Memory read failed.)
  ┌ System.Object (klass=..., 0 field(s))
── End of 'ObservedInfoContainer' (2 class(es) in hierarchy) ──
```

> This VmmException is non-fatal — the info container klass field array pointer was temporarily invalid.

**PlayerBody** (6 classes in hierarchy, same struct for client and observed):
```
── Fields of 'PlayerBody (client)' @ 0x1EB8DC49690 (full hierarchy) ──
  ┌ EFT.PlayerBody (klass=..., 28 field(s))
  │  [0x28] class        PlayerBones = 0x1EA9F96B000
  │  [0x30] class        SkeletonRootJoint = 0x1EA9FA4B3C0   ← DizSkinningSkeleton
  │  [0x58] generic<>    BodySkins = 0x1E7D97B6000
  │  [0x80] class        _equipment = 0x1ECBBBDD1A0
  │  [0x90] generic<>    SlotViews = 0x1EAB2324030
  │  [0xD8] bool         _isYourPlayer = true
```

**HandsController** — for client, deep hierarchy (9 classes!):
```
── Fields of 'HandsController (client)' @ 0x1E472A7FA80 (full hierarchy) ──
  ┌ EFT.ClientFirearmController (klass=..., 7 field(s))
  ┌ FirearmController (klass=..., 72 field(s))
  │  [0x105] bool         _isAiming = false
  ┌ ItemHandsController (klass=..., 11 field(s))
  │  [0x70] class        _item = 0x1EB9E31A120            ← item in hands
  │  [0x80] class        _player = 0x1EAF22D5000
  ┌ AbstractHandsController (klass=..., 6 field(s))
  ┌ UnityEngine.MonoBehaviour ... Object ... System.Object
```

For observed, it's `ObservedPlayerHandsController` (2 classes only):
```
── Fields of 'ObservedHandsController' @ 0x1EA710ED850 (full hierarchy) ──
  ┌ EFT.NextObservedPlayer.ObservedPlayerHandsController (klass=..., 39 field(s))
  │  [0x58] class        _item = 0x1EB9E3A16C0            ← ItemInHands
  │  [0xA8] class        _bundleAnimationBones = 0x1E3D7467B80
  │  [0xB8] bool         _isWeaponInHands = true
```

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
║ DATA CHAIN DUMP: LocalPlayer (ClientPlayer) @ 0x1EAF22D5000  (observed=False)
╚══════════════════════════════════════════════════════════════════════════
── ClientPlayer chains ────────────────────────────────────────
  playerBase                             = 0x1EAF22D5000
  Profile                              = 0x1E3DBAA4730  (playerBase + Player.Profile  [addr=0x1EAF22D5908])
  Info                                 = 0x1EAF20BD9A0  (profile + Profile.Info  [addr=0x1E3DBAA4778])
  CharacterController                  = 0x1EB9E31A7E0  (playerBase + Player._characterController  [addr=0x1EAF22D5040])
  PWA                                  = 0x1E7684FCA80  (playerBase + Player.ProceduralWeaponAnimation  [addr=0x1EAF22D5338])
  PlayerBody                           = 0x1EB8DC49690  (playerBase + Player._playerBody  [addr=0x1EAF22D5190])
  InventoryController                  = 0x1E7EE41BB40  (playerBase + Player._inventoryController  [addr=0x1EAF22D5980])
  HandsController                      = 0x1E472A7FA80  (playerBase + Player._handsController  [addr=0x1EAF22D5988])
  HC.Item                              = 0x1EB9E31A120  (handsCtrl + ItemHandsController.Item  [addr=0x1E472A7FAF0])
  CorpsePtr                            = READ FAILED: ReadPtr(0x1EAF22D5680) → invalid VA 0x0
  HealthController                     = 0x1E3DB438170  (playerBase + Player._healthController  [addr=0x1EAF22D5968])
```

**Address arithmetic example**:
- `playerBase` = `0x1EAF22D5000`
- `Player._playerBody` offset = `0x190`
- Read address = `0x1EAF22D5000 + 0x190` = `0x1EAF22D5190`
- Value at that address = `0x1EB8DC49690` (the PlayerBody pointer)

**MovementContext chain** (includes back-reference validation):
```
── MovementContext chain ──
  MovementContext                      = 0x1E472C92000  (playerBase + Player.MovementContext  [addr=0x1EAF22D5060])
  MC.Player (back-ref)                 = 2108597030912  (movCtx + MovementContext.Player  [addr=0x1E472C92040])
    → back-ref match: YES ✓
  RotationAddr                           = 0x1E472C920C0  (movCtx + 0xC0)
    → rotation value: (133.31, 13.29)
```

> `back-ref match: YES ✓` means `MovementContext._player` points back to our `playerBase`. This validates the chain is correct.

**CorpsePtr = READ FAILED** is normal for alive players — the corpse pointer is null (0x0).

### 4.2 ObservedPlayerView Chains

Different pointer chain structure — goes through `ObservedPlayerController`:

```
╔══════════════════════════════════════════════════════════════════════════
║ DATA CHAIN DUMP: ObservedPlayer (ObservedPlayerView) @ 0x1E3D7467CF0  (observed=True)
╚══════════════════════════════════════════════════════════════════════════
── ObservedPlayerView chains ──────────────────────────────────
  playerBase                             = 0x1E3D7467CF0
  ObservedPlayerController             = 0x1E7ED74A2A0  (playerBase + ObservedPlayerView.ObservedPlayerController  [addr=0x1E3D7467D18])
  OPC.Player (back-ref)                = 2078080924912  (opc + ObservedPlayerController.Player  [addr=0x1E7ED74A2B8])
    → back-ref match: YES ✓
  HealthController                     = 0x1E3D7467A10  (opc + ObservedPlayerController.HealthController  [addr=0x1E7ED74A388])
  HC.Player (back-ref)                 = 2078080924912  (hc + ObservedHealthController.Player  [addr=0x1E3D7467A28])
    → back-ref match: YES ✓
  HC.HealthStatus                      = 1024  (hc + ObservedHealthController.HealthStatus  [addr=0x1E3D7467A20])
  PlayerBody                           = 0x1EB8DC49B40  (playerBase + ObservedPlayerView.PlayerBody  [addr=0x1E3D7467DC8])
  InventoryController                  = 0x1EA662C08C0  (opc + ObservedPlayerController.InventoryController  [addr=0x1E7ED74A2B0])
  HandsController                      = 0x1EA710ED850  (opc + ObservedPlayerController.HandsController  [addr=0x1E7ED74A3C0])
  HC.ItemInHands                       = 0x1EB9E3A16C0  (handsCtrl + ObservedHandsController.ItemInHands  [addr=0x1EA710ED8A8])
```

**Key differences from ClientPlayer**:
- Goes through `ObservedPlayerController` (OPC) as hub
- HealthStatus `1024` = alive (not a simple bool like client)
- Movement goes through a 2-hop chain: `OPC → MovementController(step1) → StateContext(final)`

**MovementController chain** (2-hop for observed):
```
── MovementController chain ──
  MC.step1                             = 0x1EA7006DA50  (opc + 0xD8  [addr=0x1E7ED74A378])
  MC.final                             = 0x1E3CC873B80  (mcStep1 + 0x98  [addr=0x1EA7006DAE8])
  RotationAddr                           = 0x1E3CC873BA8  (mc + 0x28)
    → rotation value: (187.14, -8.79)
  VelocityAddr                           = 0x1E3CC873C78  (mc + 0xF8)
```

### 4.3 TransformInternal Chain (HOTPATH)

This is the **most performance-critical chain** — read every frame to get player world positions.

**8 hops total**: `Body → SkelRoot → _values → arr → bone0 → TI → Hierarchy → Vertices`

**ClientPlayer** (Body at +0x190):
```
── TransformInternal chain (HOTPATH) ─────────────────────────
  [Body]              0x1EB8DC49690  (playerBase + 0x190 [Player._playerBody])
  [SkeletonRootJoint]                  = 0x1EA9FA4B3C0  (body + 0x30  [addr=0x1EB8DC496C0])
  [DizSkel._values]                    = 0x1EAB2324060  (skelRoot + 0x30  [addr=0x1EA9FA4B3F0])
  [List._items]                        = 0x1E767C98A80  (dizValues + 0x10  [addr=0x1EAB2324070])
    → list count: 138
  [Bone0]                              = 0x1EAF4BBA8E0  (arr + 0x20  [addr=0x1E767C98AA0])
  [Bone1]             0x1EAF4BBA8C0  (arr + 0x28)
  [TransformInternal]                  = 0x1EA059DBB70  (bone0 + 0x10  [addr=0x1EAF4BBA8F0])
  [TI.Index addr]     0x1EA059DBBE8  (ti + 0x78)
    → taIndex value: 1
  [TI.Hierarchy addr] 0x1EA059DBBE0  (ti + 0x70)
  [Hierarchy]                          = 0x1E562C9AED0  (ti + 0x70  [addr=0x1EA059DBBE0])
  [Hierarchy.Vertices]                 = 0x1E562C9AF80  (hierarchy + 0x68  [addr=0x1E562C9AF38])
  [Hierarchy.Indices]                  = 0x1E562CAF080  (hierarchy + 0x40  [addr=0x1E562C9AF10])
```

**Summary line** (always logged):
```
── TransformInternal summary ──
  playerBase → Body(+0x190) → SkelRoot(+0x30) → _values(+0x30) → arr(+0x10) → bone0(+0x20) → TI(+0x10) → Hierarchy(+0x70) → Vertices(+0x68)
  Total hops: 8 (body→skel→diz→arr→bone→TI→hier→verts)
  [Cross-ref] Bone1.TI=0x1E9E2585EB0, Bone1.Hierarchy=0x1E562C9AED0  (same hierarchy=True)
```

> `same hierarchy=True` — Bone0 and Bone1 share the same Hierarchy object. This is expected and validates the transform tree.

**ObservedPlayerView** (Body at +0xD8 — different offset!):
```
── TransformInternal chain (HOTPATH) ─────────────────────────
  [Body]              0x1EB8DC49B40  (playerBase + 0xD8 [ObservedPlayerView.PlayerBody])
  ...same chain from here...
```

**Inventory chain** (appended to both):
```
── Inventory chain ───────────────────────────────────────────
  Inventory                            = 0x1EB90D6A820  (invCtrl + InventoryController.Inventory  [addr=0x1E7EE41BC40])
  Equipment                            = 0x1ECBBBDD1A0  (inventory + Inventory.Equipment  [addr=0x1EB90D6A838])
```

---

## 5. BatchInit Logs

After players are discovered, `BatchInitTransforms` and `BatchInitRotations` run scatter reads in rounds:

```
[00:19:11.311] [RegisteredPlayers] BatchInitTransforms R1 (PlayerBody): 13/13 valid
[00:19:11.313] [RegisteredPlayers] BatchInitTransforms R2 (SkeletonRootJoint): 13/13 valid
[00:19:11.316] [RegisteredPlayers] BatchInitTransforms R3 (_values): 13/13 valid
[00:19:11.318] [RegisteredPlayers] BatchInitTransforms R4 (ListArr): 13/13 valid
[00:19:11.341] [RegisteredPlayers] BatchInitTransforms R5 (Bone0): 13/13 valid
[00:19:11.343] [RegisteredPlayers] BatchInitTransforms R6 (TransformInternal): 13/13 valid
[00:19:11.346] [RegisteredPlayers] BatchInitTransforms R7 (Index+Hierarchy): 13/13 valid
[00:19:11.350] [RegisteredPlayers] BatchInitTransforms R8 (Vertices+Indices): 13/13 valid
```

Each round corresponds to one hop in the TransformInternal chain:
- **R1**: Read `PlayerBody` from each player
- **R2**: Read `SkeletonRootJoint` from each body
- **R3–R4**: Read `_values` → array items pointer
- **R5–R6**: Read `Bone0` → `TransformInternal`
- **R7**: Read `Index` + `Hierarchy` pointer
- **R8**: Read `Vertices` + `Indices` arrays

**Rotation init** (3 rounds for observed, different chain):
```
[00:19:11.362] [RegisteredPlayers] BatchInitRotations R1 (OPC/MovCtx): 13/13 valid
[00:19:11.365] [RegisteredPlayers] BatchInitRotations R2 (MC step1): 13/13 valid
[00:19:11.368] [RegisteredPlayers] BatchInitRotations R3 (MC step2): 13/13 valid
[00:19:11.371] [RegisteredPlayers] BatchInitRotations DONE: 13 entries, 13 succeeded
```

**Summary line**:
```
[00:19:11.373] [RegisteredPlayers] BatchInit: 14 players, transform(13 candidates, 13 OK, 1 already, 0 maxed), rotation(13 candidates, 13 OK, 1 already, 0 maxed), elapsed=66.2ms
```

- `14 players` total tracked
- `13 candidates` needed init (the 14th = local player, already initialized)
- `1 already` = local player was done in the one-shot dump
- `0 maxed` = none hit max retry count

**Partial success** (some fail, retry next tick):
```
[00:19:25.591] [RegisteredPlayers] BatchInit: 23 players, transform(1 candidates, 1 OK, 22 already, 0 maxed), rotation(2 candidates, 1 OK, 21 already, 0 maxed), elapsed=30.0ms
```

Here 2 needed rotation init, only 1 succeeded — the other will retry next tick.

---

## 6. Player Discovery & Registration

**Discovery log format**:
```
[RegisteredPlayers] Discovered: <TYPE> [<NAME>] @ <ADDRESS> (class='<CLASS>', observed=<Bool>, transformReady=<Bool>, rotationReady=<Bool>, pos=<x, y, z>)
```

**Examples**:
```
[00:19:09.398] [RegisteredPlayers] Discovered: Default [Nadeus] @ 0x1EAF22D5000 (class='ClientPlayer', observed=False, transformReady=True, rotationReady=True, pos=<0, 0, 0>)
[00:19:09.400] [RegisteredPlayers] LocalPlayer found: Nadeus (class='ClientPlayer')
[00:19:10.273] [RegisteredPlayers] Discovered: AIBoss [Tagilla] @ 0x1E3D7467CF0 (class='ObservedPlayerView', observed=True, transformReady=False, rotationReady=False, pos=<0, 0, 0>)
[00:19:10.278] [RegisteredPlayers] Discovered: AIScav [Scav] @ 0x1E3CC873A10 (class='ObservedPlayerView', observed=True, transformReady=False, rotationReady=False, pos=<0, 0, 0>)
[00:19:11.266] [RegisteredPlayers] Discovered: USEC [Usec4] @ 0x1EAF6A59170 (class='ObservedPlayerView', observed=True, transformReady=False, rotationReady=False, pos=<0, 0, 0>)
[00:19:11.303] [RegisteredPlayers] Discovered: BEAR [Bear12] @ 0x1EAE6F39CF0 (class='ObservedPlayerView', observed=True, transformReady=False, rotationReady=False, pos=<0, 0, 0>)
```

**Player types**: `Default` (local PMC), `AIBoss`, `AIScav`, `USEC`, `BEAR`

**ObservedPlayerView field reads** (for human PMCs with profile lookups):
```
[00:19:10.303] [RegisteredPlayers] ObservedPlayerView fields for 'Usec4' @ 0x1EAF6A59170:
  NickName  = '' (ptr=0x1E132215F40)
  ProfileId = '108800000000000000000000' (ptr=0x1ED4BF07D70)
  AccountId = '' (ptr=0x1E132215F40)
  GroupID   = '' (ptr=0x1E132215F40)
```

> All empty strings share the same pointer (`0x1E132215F40`) — this is the IL2CPP interned empty string `""`.

**Null pointers** (player still spawning):
```
[00:19:25.557] [RegisteredPlayers] ObservedPlayerView fields for 'Bear15' @ 0x1EAF28AD170:
  NickName  = '<null>' (ptr=0x0)
  ProfileId = '<null>' (ptr=0x0)
  AccountId = '<null>' (ptr=0x0)
  GroupID   = '<null>' (ptr=0x0)
```

> This player's string fields haven't been populated yet. The radar still discovers them and retries reads later.

**Refresh summary** (each registration tick):
```
[RegisteredPlayers] Refresh: list=18, valid=18, invalidPtrs=0, new=2, failed=0, total=19
```

- `list=18` — raw entries in the RegisteredPlayers collection
- `valid=18` — entries with valid pointers
- `new=2` — newly discovered this tick
- `failed=0` — CreatePlayerEntry failures
- `total=19` — total tracked players

**Profile resolution** (background async):
```
[00:19:11.490] [GearManager] Resolved ProfileId for USEC [Usec4]: 652a7671a7198e8d230097c5
[00:19:11.612] [GearManager] Resolved ProfileId for BEAR [Bear12]: 5dfe7478f45ded063e41dde2
[00:19:11.662] [GearManager] Resolved ProfileId for Default [Nadeus]: 5e1351fe246aeb28245760ab
[00:19:13.035] [ProfileService] Fetched profile for 2909689: Nadeus
```

**RealtimeWorker status** (periodic):
```
[00:19:09.412] [RealtimeWorker] Active=1, transformReady=1, rotationReady=1, total=1
[00:19:19.411] [RealtimeWorker] Active=16, transformReady=16, rotationReady=15, total=16
[00:19:29.415] [RealtimeWorker] Active=23, transformReady=23, rotationReady=23, total=23
```

---

## 7. Common Error Patterns

### BadPtrException — Null Pointer in Chain

```
Exception thrown: 'eft_dma_radar.Silk.Misc.BadPtrException' in eft-dma-radar-silk.dll
[00:19:19.261] WARNING [RegisteredPlayers] TryInitRotation FAILED 'Scav' 0x1EAE6F39170: ReadPtr(0x1EAEA490A38) → invalid VA 0x0
```

**Cause**: The observed player's `ObservedPlayerController` or `MovementController` hasn't been fully initialized yet by the game.  
**Resolution**: Automatic retry on next tick. Eventually succeeds or hits max retries.

### CreatePlayerEntry FAILED — Repeated Retries

```
[00:19:20.242] WARNING [RegisteredPlayers] CreatePlayerEntry FAILED 0x1EAEB2608A0 isLocal=False: ReadPtr(0x1EAF42D5B48) → invalid VA 0x0
[00:19:20.342] WARNING [RegisteredPlayers] CreatePlayerEntry FAILED 0x1EAEB2608A0 isLocal=False: ReadPtr(0x1EAF42D5B48) → invalid VA 0x0
...repeats ~16 times over 2 seconds...
[00:19:22.317] [RegisteredPlayers] Discovered: AIScav [Scav] @ 0x1EAEB2608A0
```

**Cause**: Player is in the RegisteredPlayers list but the object is still being constructed by the game server. The `ObservedPlayerView.ObservedPlayerController` field is null.  
**Resolution**: Retries every ~100ms. Eventually the game finishes spawning the player and the read succeeds.

### VmmException — DMA Read Failure

```
Exception thrown: 'VmmSharpEx.VmmException' in eft-dma-radar-silk.dll
[00:19:10.076] [Il2CppDumper]   │  (failed to read field array: Memory read failed.)
```

**Cause**: The DMA hardware couldn't read the requested memory region. Could be:
- Page not yet committed
- TLB miss during scatter read
- Transient FPGA communication error

**Resolution**: Non-fatal for diagnostic dumps. The field dump logs the failure and continues.

### Position Scatter Read Failed

```
[00:19:11.355] WARNING [RegisteredPlayers] Position scatter read failed for 'Bear12' (verts=0x1E9E1427DB0, count=2)
```

**Cause**: The Vertices array pointer was valid but the actual position data couldn't be read. The player's transform hierarchy exists but position data isn't populated yet.  
**Resolution**: Position will be read successfully on the next realtime tick (typically within 1-2 frames).

### Rotation Scatter Read Failed

```
[00:19:22.243] WARNING [RegisteredPlayers] Rotation scatter read failed for 'Scav' (addr=0x1EAF7E5DD18)
```

**Cause**: The rotation address was resolved but the scatter read returned invalid data.  
**Resolution**: Rotation recalculated on next tick.

### Equipment Chain Failed

```
[00:19:19.265] WARNING [GearManager] Equipment chain failed for 'Scav' (observed=True)
```

**Cause**: The `InventoryController → Inventory → Equipment` chain had a null or invalid pointer. Common for players that just spawned.  
**Resolution**: Gear manager retries on subsequent registration ticks.

---

## 8. Loot & Exfil Dumps

### LootItem Dump

```
[00:19:11.795] [Il2CppDumper] ── Fields of 'LootItem InteractiveClass (ObservedLootItem)' @ 0x1E4C53E1260 (full hierarchy) ──
  ┌ EFT.Interactive.ObservedLootItem (klass=..., 1 field(s))
  ┌ EFT.Interactive.LootItem (klass=..., 33 field(s))
  │  [0x68] string       Name = "657025c9cfc010a0f5006a38 ShortName"
  │  [0x78] string       ItemId = "69d992c9b6f89742a2112440"
  │  [0x80] string       TemplateId = "657025c9cfc010a0f5006a38"
  │  [0xF0] class        _item = 0x1EB8401B120
  ┌ EFT.Interactive.InteractableObject (klass=..., 5 field(s))
  ┌ EFT.AssetsManager.PoolSafeMonoBehaviour (klass=..., 2 field(s))
  ┌ UnityEngine.MonoBehaviour ... (9 classes total)
```

### ExfilController & ExfiltrationPoint

```
[00:19:12.416] [Il2CppDumper] ── Fields of 'ExfilController' @ 0x1E7DB2E1EA0 (full hierarchy) ──
  ┌ CommonAssets.Scripts.Game.ExfiltrationController (klass=..., 8 field(s))
  │  [0x20] []           <ExfiltrationPoints>k__BackingField = 0x1EAF3D2C660
  │  [0x28] []           <ScavExfiltrationPoints>k__BackingField = 0x1EAA705C680
  │  [0x30] []           <SecretExfiltrationPoints>k__BackingField = 0x1E775483E70

[00:19:12.442] [Il2CppDumper] ── Fields of 'ExfiltrationPoint' @ 0x1ECCF1C8D00 (full hierarchy) ──
  ┌ EFT.Interactive.ExfiltrationPoint (klass=..., 26 field(s))
  │  [0x48] string       _currentTip = ""
  │  [0x58] valuetype    _status                      ← exfil status enum
  │  [0x60] string       <Description>k__BackingField = "ExfiltrationPoint"
  │  [0xC0] []           EligibleEntryPoints = 0x1E775483DE0

[00:19:12.555] [ExfilManager] Initialized 8 exfils on attempt 1
```

---

## 9. Shutdown Sequence

Normal shutdown when radar window is closed:

```
[00:19:32.155] [WorkerThread] 'Realtime Worker' stopped.
[00:19:32.157] [Memory] Radar restart requested.
[00:19:32.160] [Memory] State → ProcessFound
[00:19:32.225] [Memory] Closed.
[00:19:32.235] [RadarWindow] Closed.
[00:19:32.282] [LocalGameWorld] Cooldown active — waiting 11872ms before next raid detection...
[00:19:32.370] [RadarWindow] Run() returned.
[00:19:32.372] [SilkProgram] RadarWindow.Run() returned normally.
The program '[5412] eft-dma-radar-silk.exe' has exited with code 0 (0x0).
```

**Expected exceptions during shutdown**:
- `System.OperationCanceledException` — cancellation tokens triggered
- `System.ObjectDisposedException` — objects accessed after disposal during teardown

These are normal and do not indicate bugs.

---

## Quick Reference: Offset Chains

### LocalPlayer (ClientPlayer) Position

```
playerBase + 0x190     → PlayerBody
  + 0x30               → SkeletonRootJoint (DizSkinningSkeleton)
    + 0x30             → _values (List)
      + 0x10           → _items (array)
        + 0x20         → Bone[0] (Transform)
          + 0x10       → TransformInternal
            + 0x70     → Hierarchy
              + 0x68   → Vertices (float[] — position at index*12)
```

### LocalPlayer Rotation

```
playerBase + 0x60      → MovementContext
  + 0xC0               → _rotation (Vector2: yaw, pitch)
```

### ObservedPlayer Position

```
playerBase + 0xD8      → PlayerBody
  + 0x30               → SkeletonRootJoint
    ...same from here as ClientPlayer...
```

### ObservedPlayer Rotation

```
playerBase + 0x28      → ObservedPlayerController
  + 0xD8               → MovementController (step1)
    + 0x98             → StateContext (step2 / final)
      + 0x28           → Rotation (Vector2)
      + 0xF8           → Velocity (Vector3)
```

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

## Timeline Summary

This raid session on Interchange:

| Time | Event | Players |
|------|-------|---------|
| 00:19:06.816 | IL2CPP cache loaded (378 fields) | — |
| 00:19:07.237 | GameWorld found, map=Interchange | — |
| 00:19:09.398 | LocalPlayer discovered (Nadeus) | 1 |
| 00:19:10.273 | First observed player (Tagilla) | 2 |
| 00:19:11.266 | First human PMC (Usec4) | ~14 |
| 00:19:11.373 | BatchInit complete (66.2ms) | 14 |
| 00:19:12.555 | Exfils initialized (8 total) | 14 |
| 00:19:22.349 | Late spawners initialized | 19 |
| 00:19:25.567 | More late spawners | 23 |
| 00:19:32.155 | Radar shutdown | 23 |

Total time from raid detection to all 23 players tracked: **~18 seconds**  
Total time for initial 14-player batch: **~4 seconds** (including all diagnostic dumps)
