using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov.Unity.IL2CPP;
using System.Collections.Generic;

namespace eft_dma_radar.Common.Unity
{
    internal static class MaterialResolver
    {
        private static readonly Dictionary<int, ulong> _cache = new();

        public static ulong ResolveMaterialPtr(int instanceId)
        {
            if (instanceId == 0)
                return 0;

            if (_cache.TryGetValue(instanceId, out var cached))
                return cached;

            ulong unityBase = Memory.UnityBase;
            ulong gomAddr = GameObjectManager.GetAddr(unityBase);
            var gom = GameObjectManager.Get(gomAddr);

            var node = Memory.ReadValue<LinkedListObject>(gom.ActiveNodes);
            var last = Memory.ReadValue<LinkedListObject>(gom.LastActiveNode);

            while (node.ThisObject != 0 && node.ThisObject != last.ThisObject)
            {
                ulong obj = node.ThisObject;

                int id = Memory.ReadValue<int>(obj + ObjectClass.InstanceID);
                if (id == instanceId)
                {
                    ulong native = Memory.ReadPtr(obj + UnityOffsets.ObjectClass.MonoBehaviourOffset);
                    if (native.IsValidVirtualAddress())
                    {
                        _cache[instanceId] = native;
                        return native;
                    }
                    return 0;
                }

                node = Memory.ReadValue<LinkedListObject>(node.NextObjectLink);
            }

            return 0;
        }

        public static void ClearCache() => _cache.Clear();
    }
}
