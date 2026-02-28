using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace eft_dma_radar.Tarkov.Unity.IL2CPP
{
    [StructLayout(LayoutKind.Explicit)]
    public readonly struct GameObjectManager
    {
        [FieldOffset(0x20)]
        public readonly ulong LastActiveNode;

        [FieldOffset(0x28)]
        public readonly ulong ActiveNodes;

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // Internal cache (name ¡ú GameObject)
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        private static readonly Dictionary<string, ulong> _nameCache = new();
        private static readonly object _cacheLock = new();

        public static void ClearCache()
        {
            lock (_cacheLock)
                _nameCache.Clear();
        }

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤

        public static ulong GetAddr(ulong unityBase)
        {
            try
            {
                try
                {
                    const string sig = "48 89 05 ?? ?? ?? ?? 48 83 C4 ?? C3 33 C9";
                    ulong addr = Memory.FindSignature(sig, "UnityPlayer.dll");

                    if (addr.IsValidVirtualAddress())
                    {
                        int rva = Memory.ReadValue<int>(addr + 3);
                        ulong ptr = Memory.ReadPtr(addr + 7 + (ulong)rva, false);

                        if (ptr.IsValidVirtualAddress())
                        {
                            XMLogging.WriteLine("[GOM] Located via signature");
                            return ptr;
                        }
                    }
                }
                catch { }

                ulong fallback = Memory.ReadPtr(
                    unityBase + UnityOffsets.ModuleBase.GameObjectManager,
                    false);

                if (fallback.IsValidVirtualAddress())
                {
                    XMLogging.WriteLine("[GOM] Located via hardcoded offset");
                    return fallback;
                }

                throw new InvalidOperationException("Invalid GOM pointer");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("ERROR locating GOM", ex);
            }
        }

        public static GameObjectManager Get(ulong gomAddress)
        {
            return Memory.ReadValue<GameObjectManager>(gomAddress, false);
        }

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // ? NEW: Robust name-based lookup
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        public ulong GetGameObjectByName(
            string name,
            bool ignoreCase = true,
            bool useCache = true)
        {
            if (string.IsNullOrEmpty(name))
                return 0;

            if (useCache)
            {
                lock (_cacheLock)
                {
                    if (_nameCache.TryGetValue(name, out var cached) &&
                        cached.IsValidVirtualAddress())
                        return cached;
                }
            }

            var first = Memory.ReadValue<LinkedListObject>(ActiveNodes);
            var last  = Memory.ReadValue<LinkedListObject>(LastActiveNode);

            ulong result =
                ScanForward(first, last, name, ignoreCase);
            
            if (result == 0)
                result = ScanBackward(last, first, name, ignoreCase);

            if (result.IsValidVirtualAddress() && useCache)
            {
                lock (_cacheLock)
                    _nameCache[name] = result;
            }

            return result;
        }
        public ulong FindBehaviourByClassName(string className)
        {
            var first = Memory.ReadValue<LinkedListObject>(ActiveNodes);
            var last  = Memory.ReadValue<LinkedListObject>(LastActiveNode);
        
            ulong result =
                ScanForwardForComponent(first, last, className);
            if(result == 0)
                ScanBackwardForComponent(last, first, className);
        
            return result;
        }
        
        private static ulong ScanForwardForComponent(
            LinkedListObject start,
            LinkedListObject end,
            string className)
        {
            var current = start;
        
            for (int i = 0; i < 100_000; i++)
            {
                if (!current.ThisObject.IsValidVirtualAddress())
                    break;
        
                ulong comp = eft_dma_radar.Common.Unity.GameObject.GetComponent(
                    current.ThisObject,
                    className);
        
                if (comp.IsValidVirtualAddress())
                    return comp;
        
                if (current.ThisObject == end.ThisObject)
                    break;
        
                current = Memory.ReadValue<LinkedListObject>(current.NextObjectLink);
            }
        
            return 0;
        }
        
        private static ulong ScanBackwardForComponent(
            LinkedListObject start,
            LinkedListObject end,
            string className)
        {
            var current = start;
        
            for (int i = 0; i < 100_000; i++)
            {
                if (!current.ThisObject.IsValidVirtualAddress())
                    break;
        
                ulong comp = eft_dma_radar.Common.Unity.GameObject.GetComponent(
                    current.ThisObject,
                    className);
        
                if (comp.IsValidVirtualAddress())
                    return comp;
        
                if (current.ThisObject == end.ThisObject)
                    break;
        
                current = Memory.ReadValue<LinkedListObject>(current.PreviousObjectLink);
            }
        
            return 0;
        }

        private static ulong ScanForward(
            LinkedListObject start,
            LinkedListObject end,
            string name,
            bool ignoreCase)
        {
            var current = start;

            for (int i = 0; i < 100_000; i++)
            {
                if (!current.ThisObject.IsValidVirtualAddress())
                    break;

                if (MatchName(current.ThisObject, name, ignoreCase))
                    return current.ThisObject;

                if (current.ThisObject == end.ThisObject)
                    break;

                current = Memory.ReadValue<LinkedListObject>(current.NextObjectLink);
            }

            return 0;
        }

        private static ulong ScanBackward(
            LinkedListObject start,
            LinkedListObject end,
            string name,
            bool ignoreCase)
        {
            var current = start;

            for (int i = 0; i < 100_000; i++)
            {
                if (!current.ThisObject.IsValidVirtualAddress())
                    break;

                if (MatchName(current.ThisObject, name, ignoreCase))
                    return current.ThisObject;

                if (current.ThisObject == end.ThisObject)
                    break;

                current = Memory.ReadValue<LinkedListObject>(current.PreviousObjectLink);
            }

            return 0;
        }

        private static bool MatchName(
            ulong gameObject,
            string name,
            bool ignoreCase)
        {
            try
            {
                ulong namePtr = Memory.ReadPtr(
                    gameObject + UnityOffsets.GameObject.NameOffset,
                    false);

                if (!namePtr.IsValidVirtualAddress())
                    return false;

                string goName = Memory.ReadString(namePtr, 64, useCache: false);

                return string.Equals(
                    goName,
                    name,
                    ignoreCase
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal);
            }
            catch
            {
                // paging / transient failure ¡ú skip node
                return false;
            }
        }

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // Existing helpers stay untouched
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤

        public static ulong GetComponentFromBehaviour(
            ulong componentObject,
            string className,
            bool throwIfMissing = false)
        {
            if (!componentObject.IsValidVirtualAddress())
            {
                if (throwIfMissing)
                    throw new ArgumentOutOfRangeException(nameof(componentObject));
                return 0;
            }

            ulong gameObjectPtr = Memory.ReadPtr(
                componentObject + UnityOffsets.GameObject.ComponentsOffset);

            if (!gameObjectPtr.IsValidVirtualAddress())
            {
                if (throwIfMissing)
                    throw new ArgumentOutOfRangeException(nameof(gameObjectPtr));
                return 0;
            }

            return eft_dma_radar.Common.Unity.GameObject.GetComponent(
                gameObjectPtr,
                className);
        }
    }

    // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public readonly struct LinkedListObject
    {
        public readonly ulong PreviousObjectLink;
        public readonly ulong NextObjectLink;
        public readonly ulong ThisObject;
    }
}
