using System.Runtime.InteropServices;
using eft_dma_radar.Silk.DMA;
using SilkUtils = eft_dma_radar.Silk.Misc.Utils;

namespace eft_dma_radar.Silk.Tarkov.Unity
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Standalone IL2CPP Unity helpers for the Silk layer.
    // Replaces WPF GameObjectManager / ObjectClass without any WPF Memory calls.
    // ─────────────────────────────────────────────────────────────────────────────

    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mirrors the IL2CPP LinkedListObject struct (Sequential, Pack=8).
    /// [0x00] PreviousObjectLink
    /// [0x08] NextObjectLink
    /// [0x10] ThisObject (the actual GameObject ptr)
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal readonly struct LinkedListObject
    {
        public readonly ulong PreviousObjectLink;
        public readonly ulong NextObjectLink;
        public readonly ulong ThisObject;
    }

    /// <summary>
    /// Mirrors the IL2CPP GameObjectManager struct.
    /// [0x20] LastActiveNode  — pointer to last LinkedListObject
    /// [0x28] ActiveNodes     — pointer to first LinkedListObject
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    internal readonly struct SilkGOM
    {
        [FieldOffset(0x20)] public readonly ulong LastActiveNode;
        [FieldOffset(0x28)] public readonly ulong ActiveNodes;

        // ── GOM address resolution ────────────────────────────────────────────

        private const uint GomFallbackOffset = 0x1A233A0; // UnityPlayer.dll Dec 2025

        public static ulong GetAddr(ulong unityBase)
        {
            try
            {
                const string sig = "48 89 05 ? ? ? ? 48 83 C4 ? C3 33 C9";
                ulong addr = Memory.FindSignature(sig, "UnityPlayer.dll");
                if (SilkUtils.IsValidVirtualAddress(addr))
                {
                    int rva = Memory.ReadValue<int>(addr + 3, false);
                    ulong ptr = Memory.ReadPtr(addr + 7 + (ulong)rva, false);
                    if (SilkUtils.IsValidVirtualAddress(ptr))
                    {
                        Log.WriteLine("[GOM] Located via signature");
                        return ptr;
                    }
                }
            }
            catch { }

            ulong fallback = Memory.ReadPtr(unityBase + GomFallbackOffset, false);
            if (SilkUtils.IsValidVirtualAddress(fallback))
            {
                Log.WriteLine("[GOM] Located via hardcoded offset");
                return fallback;
            }

            throw new InvalidOperationException("Failed to locate GameObjectManager");
        }

        // ── Linked-list walk ──────────────────────────────────────────────────

        /// <summary>
        /// Searches the GOM active linked list for a GameObject whose name matches <paramref name="name"/>.
        /// Matches WPF GetGameObjectByName: reads LinkedListObject structs at ActiveNodes/LastActiveNode,
        /// walks forward via NextObjectLink then backward via PreviousObjectLink.
        /// Returns 0 if not found.
        /// </summary>
        public ulong GetGameObjectByName(string name, bool ignoreCase = true)
        {
            const int maxNodes = 100_000;
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            // Read the first and last node structs (ActiveNodes and LastActiveNode are pointers TO structs)
            if (!Memory.TryReadValue<LinkedListObject>(ActiveNodes, out var first, false)) return 0;
            if (!Memory.TryReadValue<LinkedListObject>(LastActiveNode, out var last, false)) return 0;

            // Forward scan: first → last via NextObjectLink
            ulong result = ScanForward(first, last, name, comparison, maxNodes);
            if (result != 0) return result;

            // Backward scan: last → first via PreviousObjectLink
            return ScanBackward(last, first, name, comparison, maxNodes);
        }

        private static ulong ScanForward(LinkedListObject start, LinkedListObject end,
            string name, StringComparison comparison, int maxNodes)
        {
            var current = start;
            for (int i = 0; i < maxNodes; i++)
            {
                if (!SilkUtils.IsValidVirtualAddress(current.ThisObject)) break;

                var goName = TryReadGameObjectName(current.ThisObject);
                if (goName is not null && goName.Contains(name, comparison))
                    return current.ThisObject;

                if (current.ThisObject == end.ThisObject) break;

                if (!Memory.TryReadValue<LinkedListObject>(current.NextObjectLink, out current, false)) break;
            }
            return 0;
        }

        private static ulong ScanBackward(LinkedListObject start, LinkedListObject end,
            string name, StringComparison comparison, int maxNodes)
        {
            var current = start;
            for (int i = 0; i < maxNodes; i++)
            {
                if (!SilkUtils.IsValidVirtualAddress(current.ThisObject)) break;

                var goName = TryReadGameObjectName(current.ThisObject);
                if (goName is not null && goName.Contains(name, comparison))
                    return current.ThisObject;

                if (current.ThisObject == end.ThisObject) break;

                if (!Memory.TryReadValue<LinkedListObject>(current.PreviousObjectLink, out current, false)) break;
            }
            return 0;
        }

        private static string? TryReadGameObjectName(ulong gameObject)
        {
            try
            {
                var namePtr = Memory.ReadValue<ulong>(gameObject + 0x88, false);
                if (!SilkUtils.IsValidVirtualAddress(namePtr)) return null;
                return Memory.ReadString(namePtr, 64, false);
            }
            catch
            {
                return null;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the IL2CPP class name from a Unity object.
    /// Chain: objectClass → [0x0, 0x10] → name C-string
    /// </summary>
    internal static class SilkObjectClass
    {
        private static readonly uint[] NamePtrChain = [0x0, 0x10];

        /// <summary>
        /// Returns the IL2CPP class name for <paramref name="objectClass"/>, or null on failure.
        /// </summary>
        public static string? ReadName(ulong objectClass, int maxLength = 64)
        {
            try
            {
                if (!Memory.TryReadPtrChain(objectClass, NamePtrChain, out ulong namePtr, false))
                    return null;
                if (!SilkUtils.IsValidVirtualAddress(namePtr)) return null;
                return Memory.ReadString(namePtr, maxLength, false);
            }
            catch
            {
                return null;
            }
        }
    }
}
