#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Common.Unity;
using SDK;

namespace eft_dma_radar.Tarkov.Unity.IL2CPP
{
    public static class GameWorldExtensions
    {
        // ?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è
        // ENTRY POINT
        // ?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è

        /// <summary>
        /// Cached GamePlayerOwner class pointer — resolved once from the TypeInfoTable.
        /// </summary>
        private static ulong _cachedGamePlayerOwnerKlass;

        public static ulong GetGameWorld(
            ulong gomAddress,
            CancellationToken ct,
            out string? map)
        {
            map = null;
            var totalSw = Stopwatch.StartNew();

            Log.WriteLine("[IL2CPP] Resolving GameWorld");

            // Phase 0: IL2CPP direct path via GamePlayerOwner (fastest — ~5 reads)
            if (TryGetGameWorldViaIL2CPP(out var il2cppResult))
            {
                map = il2cppResult!.Map;
                Log.WriteLine($"[IL2CPP] GameWorld found (IL2CPP direct) in {totalSw.ElapsedMilliseconds}ms");
                return il2cppResult.GameWorld;
            }

            // Phase 1: TypeIndex scan (preferred, robust)
            var phaseSw = Stopwatch.StartNew();
            for (int i = 0; i < 3; i++)
            {
                ct.ThrowIfCancellationRequested();

                if (TryTypeIndexScan(gomAddress, ct, out var result))
                {
                    map = result!.Map;
                    Log.WriteLine($"[IL2CPP] GameWorld found (TypeIndex) attempt {i + 1} in {totalSw.ElapsedMilliseconds}ms");
                    return result.GameWorld;
                }

                Thread.Sleep(50);
            }
            Log.WriteLine($"[IL2CPP] Phase 1 (TypeIndex) failed after {phaseSw.ElapsedMilliseconds}ms");

            // Phase 2: Name-based shallow scan (backup)
            phaseSw.Restart();
            for (int i = 0; i < 3; i++)
            {
                ct.ThrowIfCancellationRequested();

                if (TryShallowScan(gomAddress, ct, out var result))
                {
                    map = result!.Map;
                    Log.WriteLine($"[IL2CPP] GameWorld found (shallow) attempt {i + 1} in {totalSw.ElapsedMilliseconds}ms total (phase2: {phaseSw.ElapsedMilliseconds}ms)");
                    return result.GameWorld;
                }

                Thread.Sleep(50);
            }
            Log.WriteLine($"[IL2CPP] Phase 2 (shallow) failed after {phaseSw.ElapsedMilliseconds}ms");

            // Phase 3: Full legacy scan
            phaseSw.Restart();
            var gameWorld = FullScan(gomAddress, ct, out map);
            Log.WriteLine($"[IL2CPP] GameWorld found (full scan) in {totalSw.ElapsedMilliseconds}ms total (phase3: {phaseSw.ElapsedMilliseconds}ms)");
            return gameWorld;
        }

        // ════════════════════════════════════════════════════════════════════
        // PHASE 0 — IL2CPP DIRECT PATH (GamePlayerOwner → myPlayer → GameWorld)
        // ════════════════════════════════════════════════════════════════════

        private static bool TryGetGameWorldViaIL2CPP(out GameWorldResult? result)
        {
            result = null;

            try
            {
                // Resolve GamePlayerOwner class pointer from TypeInfoTable (once)
                var klassPtr = _cachedGamePlayerOwnerKlass;
                if (!klassPtr.IsValidVirtualAddress())
                {
                    klassPtr = ResolveGamePlayerOwnerKlass();
                    if (!klassPtr.IsValidVirtualAddress())
                        return false;

                    _cachedGamePlayerOwnerKlass = klassPtr;
                    Log.WriteLine($"[IL2CPP] GamePlayerOwner class resolved @ 0x{klassPtr:X}");
                }

                // Read static_fields from the Il2CppClass struct
                var staticFields = Memory.ReadValue<ulong>(
                    klassPtr + Offsets.Il2CppClass.StaticFields);

                if (!staticFields.IsValidVirtualAddress())
                    return false;

                // Read _myPlayer from static fields
                var myPlayer = Memory.ReadPtr(
                    staticFields + Offsets.GamePlayerOwner._myPlayer);

                if (!myPlayer.IsValidVirtualAddress())
                    return false;

                // Read GameWorld from the player
                var gameWorld = Memory.ReadPtr(
                    myPlayer + Offsets.Player.GameWorld);

                if (!gameWorld.IsValidVirtualAddress())
                    return false;

                // Resolve map name
                if (TryResolveMap(gameWorld, out var map))
                {
                    result = new GameWorldResult
                    {
                        GameWorld = gameWorld,
                        Map = map
                    };
                    return true;
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Resolves the EFT.GamePlayerOwner Il2CppClass pointer from the TypeInfoTable.
        /// Uses the TypeIndex resolved by the Il2CppDumper if available,
        /// otherwise falls back to scanning the table by class name.
        /// </summary>
        private static ulong ResolveGamePlayerOwnerKlass()
        {
            var gaBase = Memory.GameAssemblyBase;
            if (!gaBase.IsValidVirtualAddress() || Offsets.Special.TypeInfoTableRva == 0)
                return 0;

            ulong tablePtr;
            try { tablePtr = Memory.ReadPtr(gaBase + Offsets.Special.TypeInfoTableRva, false); }
            catch { return 0; }

            if (!tablePtr.IsValidVirtualAddress())
                return 0;

            // Fast path: use cached TypeIndex from the dumper
            var typeIndex = Offsets.Special.GamePlayerOwner_TypeIndex;
            if (typeIndex != 0)
            {
                try
                {
                    var ptr = Memory.ReadValue<ulong>(
                        tablePtr + (ulong)typeIndex * 8);

                    if (ptr.IsValidVirtualAddress())
                        return ptr;
                }
                catch { }
            }

            // Slow fallback: scan first N entries for class named "GamePlayerOwner"
            Log.WriteLine("[IL2CPP] GamePlayerOwner TypeIndex not cached, scanning TypeInfoTable...");
            const int maxEntries = 20_000;
            for (int i = 0; i < maxEntries; i++)
            {
                try
                {
                    var ptr = Memory.ReadValue<ulong>(tablePtr + (ulong)i * 8);
                    if (!ptr.IsValidVirtualAddress())
                        continue;

                    var namePtr = Memory.ReadValue<ulong>(ptr + Offsets.Il2CppClass.Name);
                    if (!namePtr.IsValidVirtualAddress())
                        continue;

                    var name = Memory.ReadString(namePtr, 64, useCache: false);
                    if (name == "GamePlayerOwner")
                    {
                        Log.WriteLine($"[IL2CPP] GamePlayerOwner found at TypeIndex {i}");
                        Offsets.Special.GamePlayerOwner_TypeIndex = (uint)i;
                        return ptr;
                    }
                }
                catch { }
            }

            return 0;
        }

        // ?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è
        // PHASE 1 ?? TYPE INDEX SCAN (NEW, CORRECT)
        // ?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è

        private static bool TryTypeIndexScan(
            ulong gomAddress,
            CancellationToken ct,
            out GameWorldResult? result)
        {
            result = null;

            try
            {
                var sw = Stopwatch.StartNew();
                var gom = GameObjectManager.Get(gomAddress);
                var current = Memory.ReadValue<LinkedListObject>(gom.ActiveNodes);
                int nodesScanned = 0;

                for (int i = 0; i < 5000; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    ulong go = current.ThisObject;
                    if (!go.IsValidVirtualAddress())
                        break;

                    nodesScanned++;

                    if (TryParseGameWorldByTypeIndex(go) is GameWorldResult gw)
                    {
                        Log.WriteLine($"[IL2CPP] TypeIndex scan: found at node {nodesScanned} in {sw.ElapsedMilliseconds}ms");
                        result = gw;
                        return true;
                    }

                    current = Memory.ReadValue<LinkedListObject>(current.NextObjectLink);
                }

                Log.WriteLine($"[IL2CPP] TypeIndex scan: exhausted {nodesScanned} nodes in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.WriteLine($"[IL2CPP] TypeIndex scan: exception - {ex.Message}");
            }

            return false;
        }

        private static GameWorldResult? TryParseGameWorldByTypeIndex(ulong gameObject)
        {
            try
            {
                ulong components = Memory.ReadValue<ulong>(
                    gameObject + UnityOffsets.GameObject.ComponentsOffset);

                if (!components.IsValidVirtualAddress())
                    return null;

                int count = Memory.ReadValue<int>(
                    components + UnityOffsets.Il2CppArray.Length);

                if (count <= 0 || count > 64)
                    return null;

                ulong data = components + UnityOffsets.Il2CppArray.Data;

                for (int i = 0; i < count; i++)
                {
                    ulong component = Memory.ReadValue<ulong>(
                        data + (ulong)(i * sizeof(ulong)));

                    if (!component.IsValidVirtualAddress())
                        continue;

                    ulong gameWorld = Memory.ReadPtrChain(
                        gameObject, UnityOffsets.GameWorldChain);

                    if (!gameWorld.IsValidVirtualAddress())
                        continue;

                    if (TryResolveMap(gameWorld, out var map))
                    {
                        return new GameWorldResult
                        {
                            GameWorld = gameWorld,
                            Map = map
                        };
                    }
                }
            }
            catch { }

            return null;
        }

        // ?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è
        // PHASE 2 ?? NAME SHALLOW SCAN (UNCHANGED BACKUP)
        // ?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è

        private static bool TryShallowScan(
            ulong gomAddress,
            CancellationToken ct,
            out GameWorldResult? result)
        {
            result = null;

            try
            {
                var sw = Stopwatch.StartNew();
                var gom = GameObjectManager.Get(gomAddress);
                var current = Memory.ReadValue<LinkedListObject>(gom.ActiveNodes);
                int nodesScanned = 0;

                for (int i = 0; i < 5000; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    if (!current.ThisObject.IsValidVirtualAddress())
                        break;

                    nodesScanned++;

                    if (ParseGameWorldByName(ref current) is GameWorldResult gw)
                    {
                        Log.WriteLine($"[IL2CPP] Shallow scan: found at node {nodesScanned} in {sw.ElapsedMilliseconds}ms");
                        result = gw;
                        return true;
                    }

                    current = Memory.ReadValue<LinkedListObject>(current.NextObjectLink);
                }

                Log.WriteLine($"[IL2CPP] Shallow scan: exhausted {nodesScanned} nodes in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.WriteLine($"[IL2CPP] Shallow scan: exception - {ex.Message}");
            }

            return false;
        }

        // ?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è
        // PHASE 3 ?? FULL LEGACY SCAN (UNCHANGED)
        // ?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è

        private static ulong FullScan(
            ulong gomAddress,
            CancellationToken ct,
            out string? map)
        {
            map = null;
            var sw = Stopwatch.StartNew();

            var gom = GameObjectManager.Get(gomAddress);
            var first = Memory.ReadValue<LinkedListObject>(gom.ActiveNodes);
            var last = Memory.ReadValue<LinkedListObject>(gom.LastActiveNode);

            var current = first;
            int depth = 0;

            while (current.ThisObject.IsValidVirtualAddress() && depth++ < 100_000)
            {
                ct.ThrowIfCancellationRequested();

                if (ParseGameWorldByName(ref current) is GameWorldResult result)
                {
                    Log.WriteLine($"[IL2CPP] Full scan: found at node {depth} in {sw.ElapsedMilliseconds}ms");
                    map = result.Map;
                    return result.GameWorld;
                }

                if (current.ThisObject == last.ThisObject)
                    break;

                current = Memory.ReadValue<LinkedListObject>(current.NextObjectLink);
            }

            Log.WriteLine($"[IL2CPP] Full scan: exhausted {depth} nodes in {sw.ElapsedMilliseconds}ms");
            throw new InvalidOperationException("GameWorld not found");
        }

        private static GameWorldResult? ParseGameWorldByName(ref LinkedListObject node)
        {
            try
            {
                var go = node.ThisObject;
                if (!go.IsValidVirtualAddress())
                    return null;

                var namePtr = Memory.ReadValue<ulong>(
                    go + UnityOffsets.GameObject.NameOffset);

                if (!namePtr.IsValidVirtualAddress())
                    return null;

                var name = Memory.ReadString(namePtr, 64, useCache: false);
                if (!name.Equals("GameWorld", StringComparison.OrdinalIgnoreCase))
                    return null;

                var gameWorld = Memory.ReadPtrChain(go, UnityOffsets.GameWorldChain);
                if (!gameWorld.IsValidVirtualAddress())
                    return null;

                if (TryResolveMap(gameWorld, out var map))
                {
                    return new GameWorldResult
                    {
                        GameWorld = gameWorld,
                        Map = map
                    };
                }
            }
            catch { }

            return null;
        }

        // ?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è
        // SHARED HELPERS
        // ?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è

        private static bool TryResolveMap(ulong gameWorld, out string? map)
        {
            map = null;

            ulong mapPtr = Memory.ReadValue<ulong>(
                gameWorld + Offsets.ClientLocalGameWorld.LocationId);

            if (!mapPtr.IsValidVirtualAddress())
            {
                var lp = Memory.ReadValue<ulong>(
                    gameWorld + Offsets.ClientLocalGameWorld.MainPlayer);

                if (!lp.IsValidVirtualAddress())
                    return false;

                mapPtr = Memory.ReadValue<ulong>(
                    lp + Offsets.Player.Location);
            }

            var mapName = Memory.ReadUnityString(mapPtr, 128);
            if (string.IsNullOrEmpty(mapName) ||
                !GameData.MapNames.ContainsKey(mapName))
                return false;

            map = mapName;
            return true;
        }

        private sealed class GameWorldResult
        {
            public ulong GameWorld { get; init; }
            public string? Map { get; init; }
        }
    }
}