#nullable enable
using eft_dma_radar.Misc.Data;

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
            if (!Memory.TryReadValue<ulong>(
                klassPtr + Offsets.Il2CppClass.StaticFields, out var staticFields))
                return false;

            if (!staticFields.IsValidVirtualAddress())
                return false;

            // Read _myPlayer from static fields
            if (!Memory.TryReadPtr(
                staticFields + Offsets.GamePlayerOwner._myPlayer, out var myPlayer))
                return false;

            // Read GameWorld from the player
            if (!Memory.TryReadPtr(
                myPlayer + Offsets.Player.GameWorld, out var gameWorld))
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

            if (!Memory.TryReadPtr(gaBase + Offsets.Special.TypeInfoTableRva, out var tablePtr, false))
                return 0;

            // Fast path: use cached TypeIndex from the dumper
            var typeIndex = Offsets.Special.GamePlayerOwner_TypeIndex;
            if (typeIndex != 0)
            {
                if (Memory.TryReadValue<ulong>(
                    tablePtr + (ulong)typeIndex * 8, out var ptr) && ptr.IsValidVirtualAddress())
                    return ptr;
            }

            // Slow fallback: scan first N entries for class named "GamePlayerOwner"
            Log.WriteLine("[IL2CPP] GamePlayerOwner TypeIndex not cached, scanning TypeInfoTable...");
            const int maxEntries = 20_000;
            for (int i = 0; i < maxEntries; i++)
            {
                if (!Memory.TryReadValue<ulong>(tablePtr + (ulong)i * 8, out var ptr) || !ptr.IsValidVirtualAddress())
                    continue;

                if (!Memory.TryReadValue<ulong>(ptr + Offsets.Il2CppClass.Name, out var namePtr) || !namePtr.IsValidVirtualAddress())
                    continue;

                if (!Memory.TryReadString(namePtr, out var name, 64, useCache: false) || name is null)
                    continue;

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
                if (!Memory.TryReadValue<LinkedListObject>(gom.ActiveNodes, out var current))
                    return false;
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

                    if (!Memory.TryReadValue<LinkedListObject>(current.NextObjectLink, out current))
                        break;
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
            var go = node.ThisObject;
            if (!go.IsValidVirtualAddress())
                return null;

            if (!Memory.TryReadValue<ulong>(
                go + UnityOffsets.GameObject.NameOffset, out var namePtr) || !namePtr.IsValidVirtualAddress())
                return null;

            if (!Memory.TryReadString(namePtr, out var name, 64, useCache: false) || name is null)
                return null;

            if (!name.Equals("GameWorld", StringComparison.OrdinalIgnoreCase))
                return null;

            if (!Memory.TryReadPtrChain(go, UnityOffsets.GameWorldChain, out var gameWorld))
                return null;

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

            return null;
        }

        // --------------------------------------------------------------------
        // SHARED HELPERS
        // --------------------------------------------------------------------

        private static bool TryResolveMap(ulong gameWorld, out string? map)
        {
            map = null;

            if (!Memory.TryReadValue<ulong>(
                gameWorld + Offsets.ClientLocalGameWorld.LocationId, out var mapPtr) || !mapPtr.IsValidVirtualAddress())
            {
                if (!Memory.TryReadValue<ulong>(
                    gameWorld + Offsets.ClientLocalGameWorld.MainPlayer, out var lp) || !lp.IsValidVirtualAddress())
                    return false;

                if (!Memory.TryReadValue<ulong>(
                    lp + Offsets.Player.Location, out mapPtr) || !mapPtr.IsValidVirtualAddress())
                    return false;
            }

            if (!Memory.TryReadUnityString(mapPtr, out var mapName, 128) ||
                string.IsNullOrEmpty(mapName) ||
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