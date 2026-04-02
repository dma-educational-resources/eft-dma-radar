#nullable enable
using System;
using System.Diagnostics;
using System.Threading;

using eft_dma_radar.Misc;
using eft_dma_radar.Misc.Data;
using eft_dma_radar.Tarkov.Unity;
using SDK;

namespace eft_dma_radar.Tarkov.Unity.IL2CPP
{
    public static class GameWorldExtensions
    {
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

            Log.WriteRateLimited(AppLogLevel.Info, "ResolveGameWorld", TimeSpan.FromSeconds(30), "Resolving GameWorld", "IL2CPP");

            // Primary: IL2CPP direct path via GamePlayerOwner (fastest — ~5 reads)
            if (TryGetGameWorldViaIL2CPP(out var il2cppResult))
            {
                map = il2cppResult!.Map;
                Log.WriteLine($"[IL2CPP] GameWorld found (IL2CPP direct) in {totalSw.ElapsedMilliseconds}ms");
                return il2cppResult.GameWorld;
            }

            // Fallback: GOM name-based scan
            if (TryGOMScan(gomAddress, ct, out var gomResult))
            {
                map = gomResult!.Map;
                Log.WriteLine($"[IL2CPP] GameWorld found (GOM) in {totalSw.ElapsedMilliseconds}ms");
                return gomResult.GameWorld;
            }

            Log.WriteLine($"[IL2CPP] GameWorld not found after {totalSw.ElapsedMilliseconds}ms");
            throw new InvalidOperationException("GameWorld not found");
        }

        // --------------------------------------------------------------------
        // IL2CPP DIRECT PATH (GamePlayerOwner ? myPlayer ? GameWorld)
        // --------------------------------------------------------------------

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

                        // Persist the newly discovered TypeIndex to the cache so
                        // subsequent startups use the fast path immediately.
                        try { Il2CppDumper.SaveCache(); }
                        catch { }

                        return ptr;
                    }
                }
                catch { }
            }

            return 0;
        }

        // --------------------------------------------------------------------
        // GOM FALLBACK — Name-based scan
        // --------------------------------------------------------------------

        private static bool TryGOMScan(
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
                const int maxDepth = 10_000;

                while (current.ThisObject.IsValidVirtualAddress() && nodesScanned < maxDepth)
                {
                    ct.ThrowIfCancellationRequested();
                    nodesScanned++;

                    if (TryParseGameWorldByName(ref current) is GameWorldResult gw)
                    {
                        Log.WriteLine($"[IL2CPP] GOM scan: found at node {nodesScanned} in {sw.ElapsedMilliseconds}ms");
                        result = gw;
                        return true;
                    }

                    current = Memory.ReadValue<LinkedListObject>(current.NextObjectLink);
                }

                Log.WriteLine($"[IL2CPP] GOM scan: exhausted {nodesScanned} nodes in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.WriteLine($"[IL2CPP] GOM scan: exception - {ex.Message}");
            }

            return false;
        }

        private static GameWorldResult? TryParseGameWorldByName(ref LinkedListObject node)
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

        // --------------------------------------------------------------------
        // SHARED HELPERS
        // --------------------------------------------------------------------

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