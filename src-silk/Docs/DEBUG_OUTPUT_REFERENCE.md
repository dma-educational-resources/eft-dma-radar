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
10. [TransformPath Comparison](#10-transformpath-comparison)

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
[20:57:52.052] [Il2CppDumper] Dump starting...
[20:57:52.085] [Il2CppDumper] Fast cache loaded (PE match) — 380/380 fields applied.
[20:57:52.087] [Memory] State → Initializing
```

- **380 fields** = total schema fields across all `SchemaClass` definitions in `Il2CppDumperSchema.cs`
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
  │  [0x10] string       Id = "xxxxxxxxxxxxxxxxxxxxxxxx"
  │  [0x18] string       AccountId = "0000000"
  │  [0x48] class        Info = 0x1EAF20BD9A0
  │  [0x70] class        Inventory = 0x1EB90D6A820
```

**PlayerInfo** (2 classes):
```
── Fields of 'PlayerInfo' @ 0x1EAF20BD9A0 (full hierarchy) ──
  ┌ EFT.<unknown> (klass=0x1E1624DA460, 25 field(s))
  │  [0x10] string       Nickname = "LocalPlayer"
  │  [0x28] string       EntryPoint = "MallSE"
  │  [0x60] string       GameVersion = "standard"
  │  [0x68] valuetype    Type               ← PlayerType enum (PMC/Scav/Boss)
```

> Note: PlayerInfo class shows as `EFT.<unknown>` because the actual class name is obfuscated.

**MovementContext** (3 classes — `ClientPlayerMovementContext` → `MovementContext` → `System.Object`):
```
── Fields of 'MovementContext (LocalPlayer)' @ 0x1B59D392A80 (full hierarchy) ──
  ┌ EFT.ClientPlayerMovementContext (klass=0x1B2A29A9270, 0 field(s))
  ┌ EFT.MovementContext (klass=0x1B2A12263C0, 192 field(s))
  │  [0x10] class        _playerTransform = 0x1B511A5A680  ← BifacialTransform (NOT a Unity Transform!)
  │  [0x40] class        _player = 0x1B5AF28A000             ← back-reference to player
  │  [0xC0] valuetype    _rotation                            ← THE rotation we read (Vector2)
  │  [0xC8] valuetype    _previousRotation
  │  [0x380] float       <CharacterMovementSpeed>k__BackingField = 0.6449
  ┌ System.Object (klass=0x1B2A02523F0, 0 field(s))
```

> **Key discovery**: `_playerTransform` at offset `0x10` is a `BifacialTransform`, not a Unity `Transform`.
> `BifacialTransform.Original` at offset `0x10` is the real Unity `Transform`.
> Chain: `MovementContext+0x10 → BifacialTransform+0x10 → Transform+0x10 → TransformInternal`.
> This resolves to a **different** TransformInternal than `_playerLookRaycastTransform` — it's the body root (index 0) rather than the eye-level transform (index 35). See [TransformPath Comparison](#10-transformpath-comparison).

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
── Fields of 'ObservedMovementController (Usec14)' @ 0x1B5A1E05450 (full hierarchy) ──
  ┌ EFT.NextObservedPlayer.ObservedPlayerStateContext (klass=..., 61 field(s))
  │  [0x28] valuetype    <Rotation>k__BackingField          ← the rotation we read
  │  [0x88] class        _playerTransform = 0x1B515F0E270   ← BifacialTransform (same type as local)
  │  [0x90] class        _characterController = 0x1B59D4B4BE0
  │  [0xF8] valuetype    _velocity                           ← the velocity we read
  │  [0xE0] float        _characterMovementSpeed = 0.192157
  │  [0x138] class       _player = 0x1B5A1E058A0            ← back-ref to ObservedPlayerView
```

> `_playerTransform` at offset `0x88` on observed is also a `BifacialTransform`, same chain as local but at a different offset.

**OPC→Player (ObservedPlayerView)** — the inner `Player` reached via `OPC+0x18` is actually the `ObservedPlayerView` itself (circular reference):
```
── Fields of 'ObservedPlayer (OPC→Player) (Usec14)' @ 0x1B5A1E058A0 (full hierarchy) ──
  ┌ EFT.NextObservedPlayer.ObservedPlayerView (klass=..., 51 field(s))
  │  [0x28] class        <ObservedPlayerController>k__BackingField = 0x1B5A1E1BD20  ← circular!
  │  [0x60] valuetype    <VisibleToCameraType>k__BackingField = 0x100000000
  │  [0x100] class       _playerLookRaycastTransform = 0x1B596AF55C0
```

> **Important**: `ObservedPlayerView+0x60` is `VisibleToCameraType` (not `MovementContext`).
> Attempting to dump `+0x60` as a class pointer yields `0x100000000` → invalid klass pointer.
> The observed movement data lives on `ObservedPlayerStateContext`, NOT on any `MovementContext` subclass.

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

**Examples**:
```
[00:19:09.398] [RegisteredPlayers] Discovered: Default [LocalPlayer] @ 0x1EAF22D5000 (class='ClientPlayer', observed=False, transformReady=True, rotationReady=True, pos=<0, 0, 0>)
[00:19:09.400] [RegisteredPlayers] LocalPlayer found: LocalPlayer (class='ClientPlayer')
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
[00:19:11.490] [GearManager] Resolved ProfileId for USEC [Usec4]: xxxxxxxxxxxxxxxxxxxxxxxx
[00:19:11.612] [GearManager] Resolved ProfileId for BEAR [Bear12]: xxxxxxxxxxxxxxxxxxxxxxxx
[00:19:11.662] [GearManager] Resolved ProfileId for Default [LocalPlayer]: xxxxxxxxxxxxxxxxxxxxxxxx
[00:19:13.035] [ProfileService] Fetched profile for 0000000: LocalPlayer
```

**RealtimeWorker status** (periodic):
```
[20:57:52.743] [RealtimeWorker] Scatter: active=1 (position=1, rotation=1), total=1
[20:58:02.748] [RealtimeWorker] Scatter: active=21 (position=21, rotation=21), total=21
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

## 10. TransformPath Comparison

Diagnostic comparison of two paths to `TransformInternal` (now removed from code — results documented here for reference).

**Path A** — current, via `_playerLookRaycastTransform`:
```
playerBase + lookOffset → Transform + 0x10 → TransformInternal
```

**Path B** — alternative, via `MovementContext._playerTransform` (`BifacialTransform`):
```
MovementContext + 0x10 → BifacialTransform + 0x10 (Original) → Transform + 0x10 → TransformInternal
```

**Live comparison result** (local player on Interchange):
```
[TransformPath] ── Compare for 'LocalPlayer' (local) ──
[TransformPath]   Path A (_playerLookRaycastTransform): Transform=0x1B596547400 → TI=0x1B6F3ABE4B0
[TransformPath]   Path B (_playerTransform on MC):      BifacialTransform=0x1B511A5A680 → Transform=0x1B59654E560 → TI=0x1B6F3AB4380
[TransformPath]   Same TransformInternal: False
[TransformPath]   Index A=35, Index B=0  (lower = less vertex data to read)
[TransformPath]   Position A=<542.24786, 18.209715, -331.2256>
[TransformPath]   Position B=<541.7549, 17.905117, -331.61902>
[TransformPath]   Distance=0.7004m  (0 = identical transform)
[TransformPath] ── End ──
```

**Key findings**:

| Aspect | Path A (lookRaycast) | Path B (BifacialTransform) |
|--------|---------------------|---------------------------|
| TransformInternal | `0x1B6F3ABE4B0` | `0x1B6F3AB4380` |
| Index | 35 | 0 (root) |
| Position | `<542.25, 18.21, -331.23>` | `<541.75, 17.91, -331.62>` |
| Body part | Eye/head level | Body root (feet/pelvis) |
| Pointer hops | 2 | 3 |
| Distance apart | — | 0.70m |

**Decision**: Path A (`_playerLookRaycastTransform`) is kept because:
1. Fewer DMA reads (2 hops vs 3)
2. Eye-level position is better for radar dot placement and aimview
3. Already proven reliable (21/21 players tracked)

---

## Timeline Summary

This raid session on Interchange:

| Time | Event | Players |
|------|-------|---------|
| 20:57:52.085 | IL2CPP cache loaded (380 fields) | — |
| 20:57:52.180 | GameWorld found, map=Interchange | — |
| 20:57:52.731 | LocalPlayer discovered | 1 |
| 20:57:52.751 | First observed players (Killa, Tagilla) | 3 |
| 20:57:52.782 | First human PMCs (Usec11, Usec14) | ~8 |
| 20:57:52.854 | BatchInitTransforms DONE (18/20 OK) | 21 |
| 20:57:52.980 | Retry batch (4 remaining, 4 OK) | 21 |
| 20:57:54.072 | Exfils initialized (8 total) | 21 |
| 20:58:02.748 | All 21 players active in realtime loop | 21 |
| 20:58:16.768 | First player removal (Scav dead) | 20 |
| 20:58:24.190 | Radar shutdown | 20 |

Total time from raid detection to all 21 players tracked: **~0.8 seconds** (4-round transform chain)  
Retry for 2 vertex-failed players: **~0.1 seconds** additional
