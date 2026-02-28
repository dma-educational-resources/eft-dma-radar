using System;
using System.Collections.Generic;
using System.Threading;

using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Common.Unity;
using SDK;

namespace eft_dma_radar.Tarkov.Unity.IL2CPP
{
    public static class GameWorldExtensions
    {
        // ?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่
        // ENTRY POINT
        // ?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่

        public static ulong GetGameWorld(
            ulong gomAddress,
            CancellationToken ct,
            out string map)
        {
            map = null;

            XMLogging.WriteLine("[IL2CPP] Resolving GameWorld");

            // Phase 1: TypeIndex scan (preferred, robust)
            for (int i = 0; i < 3; i++)
            {
                ct.ThrowIfCancellationRequested();

                if (TryTypeIndexScan(gomAddress, ct, out var result))
                {
                    map = result.Map;
                    XMLogging.WriteLine("[IL2CPP] GameWorld found (TypeIndex)");
                    return result.GameWorld;
                }

                Thread.Sleep(50);
            }

            // Phase 2: Name-based shallow scan (backup)
            for (int i = 0; i < 3; i++)
            {
                ct.ThrowIfCancellationRequested();

                if (TryShallowScan(gomAddress, ct, out var result))
                {
                    map = result.Map;
                    XMLogging.WriteLine("[IL2CPP] GameWorld found (shallow)");
                    return result.GameWorld;
                }

                Thread.Sleep(50);
            }

            // Phase 3: Full legacy scan
            return FullScan(gomAddress, ct, out map);
        }

        // ?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่
        // PHASE 1 ?? TYPE INDEX SCAN (NEW, CORRECT)
        // ?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่

        private static bool TryTypeIndexScan(
            ulong gomAddress,
            CancellationToken ct,
            out GameWorldResult result)
        {
            result = null;

            try
            {
                var gom = GameObjectManager.Get(gomAddress);
                var current = Memory.ReadValue<LinkedListObject>(gom.ActiveNodes);

                for (int i = 0; i < 5000; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    ulong go = current.ThisObject;
                    if (!go.IsValidVirtualAddress())
                        break;

                    if (TryParseGameWorldByTypeIndex(go) is GameWorldResult gw)
                    {
                        result = gw;
                        return true;
                    }

                    current = Memory.ReadValue<LinkedListObject>(current.NextObjectLink);
                }
            }
            catch { }

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

        // ?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่
        // PHASE 2 ?? NAME SHALLOW SCAN (UNCHANGED BACKUP)
        // ?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่

        private static bool TryShallowScan(
            ulong gomAddress,
            CancellationToken ct,
            out GameWorldResult result)
        {
            result = null;

            try
            {
                var gom = GameObjectManager.Get(gomAddress);
                var current = Memory.ReadValue<LinkedListObject>(gom.ActiveNodes);

                for (int i = 0; i < 5000; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    if (!current.ThisObject.IsValidVirtualAddress())
                        break;

                    if (ParseGameWorldByName(ref current) is GameWorldResult gw)
                    {
                        result = gw;
                        return true;
                    }

                    current = Memory.ReadValue<LinkedListObject>(current.NextObjectLink);
                }
            }
            catch { }

            return false;
        }

        // ?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่
        // PHASE 3 ?? FULL LEGACY SCAN (UNCHANGED)
        // ?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่

        private static ulong FullScan(
            ulong gomAddress,
            CancellationToken ct,
            out string map)
        {
            map = null;

            var gom = GameObjectManager.Get(gomAddress);
            var first = Memory.ReadValue<LinkedListObject>(gom.ActiveNodes);
            var last  = Memory.ReadValue<LinkedListObject>(gom.LastActiveNode);

            var current = first;
            int depth = 0;

            while (current.ThisObject.IsValidVirtualAddress() && depth++ < 100_000)
            {
                ct.ThrowIfCancellationRequested();

                if (ParseGameWorldByName(ref current) is GameWorldResult result)
                {
                    map = result.Map;
                    return result.GameWorld;
                }

                if (current.ThisObject == last.ThisObject)
                    break;

                current = Memory.ReadValue<LinkedListObject>(current.NextObjectLink);
            }

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

        // ?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่
        // SHARED HELPERS
        // ?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่

        private static bool TryResolveMap(ulong gameWorld, out string map)
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
            public string Map { get; init; }
        }
    }
}