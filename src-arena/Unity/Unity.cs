using ArenaUtils = eft_dma_radar.Arena.Misc.Utils;

namespace eft_dma_radar.Arena.Unity
{
    internal static class UnityOffsets
    {
        // All zero on purpose — the UnityPlayer dumper is responsible for
        // resolving these at runtime. Starting from working values would let
        // a regression hide behind the fallback. Keep ALL Unity GO/Comp
        // header offsets at 0 so the dumper has to prove each one.
        public const uint GO_ObjectClass   = 0x0;
        public const uint GO_Components    = 0x0;
        public const uint GO_Name          = 0x0;
        public const uint Comp_ObjectClass = 0x0;
        public const uint Comp_GameObject  = 0x0;
        public static readonly uint[] ObjClass_ToNamePtr = [0x0, 0x10];
        public static readonly uint[] TransformChain =
        [
            0x10,
            Comp_GameObject,
            GO_Components,
            0x08,
            Comp_ObjectClass,
            0x10,
        ];
        public const uint GomFallback = 0x21A4450;
        public const uint AllCameras  = 0x19F3080;
        public static class ObjectClass { public const uint MonoBehaviourOffset = 0x10; }
        public static class Camera
        {
            // Arena Unity 6000.3.6.1f fallbacks (sig-scan still runs first):
            //   ViewMatrix: Camera::GetWorldToCameraMatrix body -> `lea rax, [rcx+88h]`
            //   FOV:        Camera_CUSTOM_GetGateFittedFieldOfView -> 2nd movss `[rcx+188h]`
            public static uint ViewMatrix = 0x88;
            public static uint FOV = 0x188;
            public static uint AspectRatio = 0x4F8;
            public const uint DerefIsAddedOffset = 0x35;
        }
        public static class List { public const uint ArrOffset = 0x10; public const uint ArrStartOffset = 0x20; }
        public static class ManagedList { public const uint ItemsPtr = 0x10; public const uint Count = 0x18; }
        public static class ManagedArray { public const uint FirstElement = 0x20; public const int ElementSize = 0x8; }
        public static class MongoID { public const uint TimeStamp = 0x00; public const uint Counter = 0x08; public const uint StringID = 0x10; }
        public static class IL2CPPHashSet { public const uint Entries = 0x18; public const uint Count = 0x1C; public const int EntrySize = 0x20; public const uint EntryValueOffset = 0x08; }
        public static class TransformAccess
        {
            /// <summary>
            /// Offset to the real TransformHierarchy pointer. Live dumps across all players on
            /// Arena_Prison consistently show a valid hierarchy pointer here, while the older
            /// 0x40 slot pointed to a self+0x58 wrapper whose +0x48 aliased a managed pointer
            /// (producing the "taIndex out of range" / garbage-negative index reads).
            /// </summary>
            public const uint HierarchyOffset = 0x58;
            /// <summary>
            /// int32 transform index paired with HierarchyOffset. Values consistently fall in
            /// the small scene range (e.g. 0x23=35, 0x5F=95, 0x6F=111, 0x77=119) across players.
            /// </summary>
            public const uint IndexOffset = 0x60;
        }
        public static class TransformHierarchy
        {
            /// <summary>
            /// Cached world-space position (Vector3) of the hierarchy root.
            /// Live dumps across 7 different players on Arena_Bay5 consistently put the
            /// player's world position at h+0xB0 (x,y,z floats), followed by the world
            /// rotation quaternion at h+0xC0 and the world scale at h+0xD0.
            /// For idle / sentinel hierarchies the value is &lt;0, -1000, 0&gt; (y = 0xC47A0000).
            /// Since each player owns its own hierarchy, reading this single Vector3 is
            /// sufficient — no index walking or parent chain traversal required.
            /// </summary>
            public const uint WorldPositionOffset = 0xB0;
            /// <summary>
            /// Cached world-space rotation (Quaternion x,y,z,w) at h+0xC0.
            /// </summary>
            public const uint WorldRotationOffset = 0xC0;

            // ── Bone-chain offsets (used by per-player Skeleton) ──────────
            // The hierarchy's cached world position at +0xB0 only describes the
            // hierarchy's ROOT. Bones are joints inside the hierarchy; to compute
            // their world positions we must walk the vertices/parent-index arrays.
            // Arena Unity 6 layout — confirmed by live runtime pointer-classification
            // dump across 5 players: +0x50 → TrsX[] (first entry is the idle
            // sentinel <0,-1000,0>), +0xA0 → int[] parent indices (first entry = -1
            // for root, branching pattern matches humanoid skeleton).
            /// <summary>TransformHierarchy + X → pointer to parent-indices array (int[]).</summary>
            public const uint IndicesOffset  = 0xA0;
            /// <summary>TransformHierarchy + X → pointer to vertices array (TrsX[]).</summary>
            public const uint VerticesOffset = 0x50;
        }
        public static class UnityAnimator { public const uint Speed = 0x4B0; }
    }

    // TrsX entry stride is 0x30 (48 bytes) per IDA: *(__m128*)(v3 + 48 * idx) for position,
    // *(__m128*)(v3 + 48*idx + 0x10) for rotation, +0x20 for scale/matrix column.
    // Using LayoutKind.Explicit with Size=0x30 guarantees correct 48-byte stride regardless of
    // field packing — Vector3(12) + Quaternion(16) + Vector3(12) = 40, so 8 bytes of trailing
    // padding (4 after T, 4 after S) are required.
    [StructLayout(LayoutKind.Explicit, Size = 0x30)]
    internal readonly struct TrsX
    {
        [FieldOffset(0x00)] public readonly Vector3 T;
        [FieldOffset(0x10)] public readonly Quaternion Q;
        [FieldOffset(0x20)] public readonly Vector3 S;

        internal static Vector3 ComputeWorldPosition(
            ReadOnlySpan<TrsX> vertices,
            ReadOnlySpan<int> parentIndices,
            int index,
            int maxIterations = 4096)
        {
            // Arena respawns can leave a cached TransformIndex that points past a freshly-
            // reallocated (smaller) hierarchy. Guard explicitly so we surface a sentinel
            // instead of throwing IndexOutOfRangeException that the caller would have to
            // catch as a first-chance exception on every realtime tick.
            if ((uint)index >= (uint)vertices.Length || (uint)index >= (uint)parentIndices.Length)
                return new Vector3(float.NaN, float.NaN, float.NaN);

            var pos = vertices[index].T;
            int parent = parentIndices[index];
            int iter = 0;
            int maxParent = Math.Min(vertices.Length, parentIndices.Length);
            while ((uint)parent < (uint)maxParent && iter++ < maxIterations)
            {
                ref readonly var p = ref vertices[parent];
                pos = Vector3.Transform(pos, p.Q);
                pos *= p.S;
                pos += p.T;
                parent = parentIndices[parent];
            }
            return pos;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal readonly struct LinkedListObject
    {
        public readonly ulong PreviousObjectLink;
        public readonly ulong NextObjectLink;
        public readonly ulong ThisObject;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct ComponentArray
    {
        public readonly ulong ArrayBase;
        public readonly ulong MemLabelId;
        public readonly ulong Size;
        public readonly ulong Capacity;

        [StructLayout(LayoutKind.Explicit, Pack = 1)]
        internal readonly struct Entry
        {
            [FieldOffset(0x8)]
            public readonly ulong Component;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    internal readonly struct GameObject
    {
        [FieldOffset(0x08)]
        public readonly int InstanceID;
        [FieldOffset((int)UnityOffsets.GO_ObjectClass)]
        public readonly ulong ObjectClass;
        [FieldOffset((int)UnityOffsets.GO_Components)]
        public readonly ComponentArray Components;
        [FieldOffset((int)UnityOffsets.GO_Name)]
        public readonly ulong NamePtr;
    }

    internal readonly struct GOM
    {
        private const int MaxWalkNodes = 100_000;

        public readonly ulong LastActiveNode;
        public readonly ulong ActiveNodes;

        private GOM(ulong lastActiveNode, ulong activeNodes)
        {
            LastActiveNode = lastActiveNode;
            ActiveNodes    = activeNodes;
        }

        private static readonly Dictionary<string, ulong> _nameCache = new();
        private static readonly Lock _cacheLock = new();

        public static void ClearCache() { lock (_cacheLock) _nameCache.Clear(); }

        private static ulong _cachedGomAddr;
        // 0 = unknown, 1 = (0x20,0x28), 2 = (0x18,0x20)
        private static int _cachedLayout;

        // GOM head layout differs across Arena builds — probe at runtime so the
        // walk uses the build's real LastActiveNode / ActiveNodes pair instead of
        // hard-coded offsets that may produce a structurally-valid-but-wrong list.
        public static GOM Get(ulong gomAddress)
        {
            if (!ArenaUtils.IsValidVirtualAddress(gomAddress))
                return default;

            int layout = _cachedLayout;
            if (layout == 1 && TryReadLayout(gomAddress, 0x20, 0x28, out var g1)) return g1;
            if (layout == 2 && TryReadLayout(gomAddress, 0x18, 0x20, out var g2)) return g2;

            if (TryReadLayout(gomAddress, 0x20, 0x28, out var probed1))
            {
                _cachedLayout = 1;
                return probed1;
            }
            if (TryReadLayout(gomAddress, 0x18, 0x20, out var probed2))
            {
                _cachedLayout = 2;
                return probed2;
            }
            return default;
        }

        private static bool TryReadLayout(ulong gomAddress, uint lastOff, uint activeOff, out GOM gom)
        {
            gom = default;
            if (!Memory.TryReadValue<ulong>(gomAddress + lastOff,   out var last,   false)) return false;
            if (!Memory.TryReadValue<ulong>(gomAddress + activeOff, out var active, false)) return false;
            if (!ArenaUtils.IsValidVirtualAddress(last) || !ArenaUtils.IsValidVirtualAddress(active))
                return false;
            // Sanity: ActiveNodes must be a LinkedListObject whose ThisObject is a valid VA.
            if (!Memory.TryReadValue<ulong>(active + 0x10, out var firstThis, false)) return false;
            if (!ArenaUtils.IsValidVirtualAddress(firstThis)) return false;
            gom = new GOM(last, active);
            return true;
        }

        private static readonly (string Sig, int RelOff, int InstrLen, string Desc)[] GomDirectSigs =
        [
            ("48 8B 0D ? ? ? ? 8B 41 ? 48 83 C0 ? ? ? ? ? ? ? ? 83 79", 3, 7, "mov [rip+rel32],rax (GOM init store)"),
        ];

        private static readonly (string Sig, int RelOff, int InstrLen, string Desc)[] GomCallSiteSigs =
        [
            ("E8 ? ? ? ? 4C 8D 45 ? 89 5D ? 48 8D 55", 1, 5, "call GomGetter (variant 1)"),
            ("E8 ? ? ? ? 8B 48 ? ? ? ? ? ? ? ? 48 8D 77", 1, 5, "call GomGetter (variant 2)"),
            ("E8 ? ? ? ? 48 8B 58 ? 48 8D 78 ? 48 3B DF 74 ? ? ? ? 48 8B 53", 1, 5, "call GomGetter (variant 3)"),
            ("E8 ? ? ? ? 8B 48 ? ? ? ? ? ? ? ? 48 8D 6F", 1, 5, "call GomGetter (variant 4)"),
            ("E8 ? ? ? ? 48 8B 58 ? 48 8D 78 ? 48 3B DF 74 ? 66 66 66 0F 1F 84 00", 1, 5, "call GomGetter (variant 5)"),
            ("E8 ? ? ? ? 4C 8D 44 24 ? C7 44 24 ? ? ? ? ? 48 8D 54 24 ? 48 8B C8", 1, 5, "call GomGetter (variant 6)"),
            ("E8 ? ? ? ? 4C 8D 44 24 ? 89 5C 24 ? 48 8D 54 24", 1, 5, "call GomGetter (variant 7)"),
        ];

        private static readonly (string Sig, int RelOff, int InstrLen, string Desc)[] GomBroadSigs =
        [
            ("48 89 05 ? ? ? ? 48 83 C8", 3, 7, "mov [rip+rel32],rax; add rsp (broad)"),
        ];

        private const int BroadSigMaxMatches = 256;

        public static ulong GetAddr(ulong unityBase)
        {
            if (ArenaUtils.IsValidVirtualAddress(_cachedGomAddr))
                return _cachedGomAddr;

            foreach (var (sig, relOff, instrLen, desc) in GomDirectSigs)
            {
                try
                {
                    ulong addr = Memory.FindSignature(sig, "UnityPlayer.dll");
                    if (!ArenaUtils.IsValidVirtualAddress(addr)) continue;
                    int rva = Memory.ReadValue<int>(addr + (ulong)relOff, false);
                    ulong ptr = Memory.ReadPtr(addr + (ulong)instrLen + (ulong)rva, false);
                    if (ArenaUtils.IsValidVirtualAddress(ptr))
                    {
                        Log.WriteLine($"[GOM] Located via direct sig: {desc}");
                        _cachedGomAddr = ptr;
                        return ptr;
                    }
                }
                catch { }
            }

            foreach (var (sig, relOff, instrLen, desc) in GomCallSiteSigs)
            {
                try
                {
                    ulong callAddr = Memory.FindSignature(sig, "UnityPlayer.dll");
                    if (!ArenaUtils.IsValidVirtualAddress(callAddr)) continue;
                    int callRel = Memory.ReadValue<int>(callAddr + (ulong)relOff, false);
                    ulong targetFunc = callAddr + (ulong)instrLen + (ulong)callRel;
                    if (!ArenaUtils.IsValidVirtualAddress(targetFunc)) continue;
                    if (TryResolveGetterGlobal(targetFunc, out var globalPtr))
                    {
                        Log.WriteLine($"[GOM] Located via call-site sig: {desc}");
                        _cachedGomAddr = globalPtr;
                        return globalPtr;
                    }
                }
                catch { }
            }

            foreach (var (sig, relOff, instrLen, desc) in GomBroadSigs)
            {
                try
                {
                    var matches = Memory.FindSignatures(sig, "UnityPlayer.dll", BroadSigMaxMatches);
                    foreach (var addr in matches)
                    {
                        if (!ArenaUtils.IsValidVirtualAddress(addr)) continue;
                        int rva = Memory.ReadValue<int>(addr + (ulong)relOff, false);
                        ulong ptr = addr + (ulong)instrLen + (ulong)rva;
                        if (!Memory.TryReadPtr(ptr, out var gomAddr, false)) continue;
                        if (IsValidGomPtr(gomAddr))
                        {
                            Log.WriteLine($"[GOM] Located via broad sig: {desc} (matched {matches.Length} sites)");
                            _cachedGomAddr = gomAddr;
                            return gomAddr;
                        }
                    }
                }
                catch { }
            }

            try
            {
                ulong fallback = Memory.ReadPtr(unityBase + UnityOffsets.GomFallback, false);
                if (ArenaUtils.IsValidVirtualAddress(fallback))
                {
                    Log.WriteLine("[GOM] Located via hardcoded offset");
                    _cachedGomAddr = fallback;
                    return fallback;
                }
            }
            catch { }

            throw new InvalidOperationException("Failed to locate GameObjectManager");
        }

        private static bool TryResolveGetterGlobal(ulong funcAddr, out ulong result)
        {
            result = 0;
            Span<byte> header = stackalloc byte[7];
            if (!Memory.TryReadBuffer(funcAddr, header, false)) return false;
            if (header[0] != 0x48 || header[1] != 0x8B || header[2] != 0x05) return false;
            int innerRel = BitConverter.ToInt32(header[3..]);
            ulong globalAddr = funcAddr + 7 + (ulong)innerRel;
            if (!Memory.TryReadPtr(globalAddr, out result, false)) return false;
            return ArenaUtils.IsValidVirtualAddress(result);
        }

        private static bool IsValidGomPtr(ulong ptr)
        {
            if (!ArenaUtils.IsValidVirtualAddress(ptr))
                return false;

            // Try Silk standard offsets first (0x20, 0x28)
            if (Memory.TryReadValue<ulong>(ptr + 0x20, out var lastActive, false) &&
                Memory.TryReadValue<ulong>(ptr + 0x28, out var activeNodes, false) &&
                ArenaUtils.IsValidVirtualAddress(lastActive) &&
                ArenaUtils.IsValidVirtualAddress(activeNodes) &&
                Memory.TryReadValue<LinkedListObject>(activeNodes, out var firstNode, false) &&
                ArenaUtils.IsValidVirtualAddress(firstNode.ThisObject))
            {
                return true;
            }

            // Try alternate offsets (0x18, 0x20)
            if (Memory.TryReadValue<ulong>(ptr + 0x18, out var altLastActive, false) &&
                Memory.TryReadValue<ulong>(ptr + 0x20, out var altActiveNodes, false) &&
                ArenaUtils.IsValidVirtualAddress(altLastActive) &&
                ArenaUtils.IsValidVirtualAddress(altActiveNodes) &&
                Memory.TryReadValue<LinkedListObject>(altActiveNodes, out var altFirstNode, false) &&
                ArenaUtils.IsValidVirtualAddress(altFirstNode.ThisObject))
            {
                return true;
            }

            return false;
        }

        internal static void ResetCachedAddresses()
        {
            _cachedGomAddr = 0;
            _cachedLayout  = 0;
            ClearCache();
        }

        public static ulong GetGameObjectByName(string name, bool ignoreCase = true, bool useCache = true, bool logCount = false)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            if (useCache)
            {
                lock (_cacheLock)
                {
                    if (_nameCache.TryGetValue(name, out var cached) && ArenaUtils.IsValidVirtualAddress(cached))
                        return cached;
                }
            }
            var gom = Get(Memory.GOM);
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!Memory.TryReadValue<LinkedListObject>(gom.ActiveNodes, out var first, false)) return 0;
            if (!Memory.TryReadValue<LinkedListObject>(gom.LastActiveNode, out var last, false)) return 0;
            ulong result = WalkList(first, last, forward: true,
                (node) => MatchName(node.ThisObject, name, comparison) ? node.ThisObject : 0);
            if (result == 0)
                result = WalkList(last, first, forward: false,
                    (node) => MatchName(node.ThisObject, name, comparison) ? node.ThisObject : 0);
            if (ArenaUtils.IsValidVirtualAddress(result) && useCache)
            {
                lock (_cacheLock)
                    _nameCache[name] = result;
            }
            return result;
        }

        public static ulong FindBehaviourByKlassPtr(ulong klassPtr)
        {
            if (!ArenaUtils.IsValidVirtualAddress(klassPtr)) return 0;
            var gom = Get(Memory.GOM);
            if (!Memory.TryReadValue<LinkedListObject>(gom.ActiveNodes, out var first, true)) return 0;
            if (!Memory.TryReadValue<LinkedListObject>(gom.LastActiveNode, out var last, true)) return 0;
            ulong result = WalkList(first, last, forward: true,
                node => GetComponentByKlassPtr(node.ThisObject, klassPtr), useCache: true);
            if (result == 0)
                result = WalkList(last, first, forward: false,
                    node => GetComponentByKlassPtr(node.ThisObject, klassPtr), useCache: true);
            return result;
        }

        public static ulong FindBehaviourByClassName(string className, bool logCount = false)
        {
            if (string.IsNullOrEmpty(className)) return 0;
            var gom = Get(Memory.GOM);
            if (!Memory.TryReadValue<LinkedListObject>(gom.ActiveNodes, out var first, true)) return 0;
            if (!Memory.TryReadValue<LinkedListObject>(gom.LastActiveNode, out var last, true)) return 0;
            ulong result = WalkList(first, last, forward: true,
                node => GetComponentByClassName(node.ThisObject, className), useCache: true);
            if (result == 0)
                result = WalkList(last, first, forward: false,
                    node => GetComponentByClassName(node.ThisObject, className), useCache: true);
            return result;
        }

        private static ulong GetComponentByKlassPtr(ulong gameObject, ulong klassPtr)
        {
            if (!Memory.TryReadValue<GameObject>(gameObject, out var go, true)) return 0;
            ref readonly var compArr = ref go.Components;
            if (!ArenaUtils.IsValidVirtualAddress(compArr.ArrayBase) || compArr.Size == 0) return 0;
            int count = (int)Math.Min(compArr.Size, 0x400);
            Span<ComponentArray.Entry> entries = count <= 64
                ? stackalloc ComponentArray.Entry[count]
                : new ComponentArray.Entry[count];
            if (!Memory.TryReadBuffer(compArr.ArrayBase, entries, true)) return 0;
            for (int i = 0; i < count; i++)
            {
                var compPtr = entries[i].Component;
                if (!ArenaUtils.IsValidVirtualAddress(compPtr)) continue;
                if (!Memory.TryReadPtr(compPtr + UnityOffsets.Comp_ObjectClass, out var objectClass, true)
                    || !ArenaUtils.IsValidVirtualAddress(objectClass)) continue;
                if (!Memory.TryReadPtr(objectClass, out var klass, true)) continue;
                if (klass == klassPtr) return objectClass;
            }
            return 0;
        }

        private static ulong GetComponentByClassName(ulong gameObject, string className)
        {
            if (!Memory.TryReadValue<GameObject>(gameObject, out var go, true)) return 0;
            ref readonly var compArr = ref go.Components;
            if (!ArenaUtils.IsValidVirtualAddress(compArr.ArrayBase) || compArr.Size == 0) return 0;
            int count = (int)Math.Min(compArr.Size, 0x400);
            Span<ComponentArray.Entry> entries = count <= 64
                ? stackalloc ComponentArray.Entry[count]
                : new ComponentArray.Entry[count];
            if (!Memory.TryReadBuffer(compArr.ArrayBase, entries, true)) return 0;
            for (int i = 0; i < count; i++)
            {
                var compPtr = entries[i].Component;
                if (!ArenaUtils.IsValidVirtualAddress(compPtr)) continue;
                if (!Memory.TryReadPtr(compPtr + UnityOffsets.Comp_ObjectClass, out var objectClass, true)
                    || !ArenaUtils.IsValidVirtualAddress(objectClass)) continue;
                var name = Il2CppClass.ReadName(objectClass, useCache: true);
                if (name is not null && name.Equals(className, StringComparison.Ordinal))
                    return objectClass;
            }
            return 0;
        }

        public static ulong GetComponentFromBehaviour(ulong behaviour, string className)
        {
            if (!ArenaUtils.IsValidVirtualAddress(behaviour)) return 0;
            if (!Memory.TryReadPtr(behaviour + UnityOffsets.Comp_GameObject, out var gameObject, false)
                || !ArenaUtils.IsValidVirtualAddress(gameObject)) return 0;
            return GetComponentByClassName(gameObject, className);
        }

        /// <summary>
        /// DEBUG: Dumps all GameObjects found in the GOM for debugging purposes.
        /// </summary>
        public static void DebugDumpAllGameObjects()
        {
            // Intentionally empty — kept as a stub for ad-hoc debugging. Previous implementation
            // was removed because the verbose dumps were no longer needed in normal operation.
        }

        private static ulong WalkList(
            LinkedListObject start,
            LinkedListObject end,
            bool forward,
            Func<LinkedListObject, ulong> visitor,
            bool useCache = false)
        {
            var current = start;
            for (int i = 0; i < MaxWalkNodes; i++)
            {
                if (!ArenaUtils.IsValidVirtualAddress(current.ThisObject)) break;
                var hit = visitor(current);
                if (ArenaUtils.IsValidVirtualAddress(hit)) return hit;
                if (current.ThisObject == end.ThisObject) break;
                var nextLink = forward ? current.NextObjectLink : current.PreviousObjectLink;
                if (!Memory.TryReadValue<LinkedListObject>(nextLink, out current, useCache)) break;
            }
            return 0;
        }

        private static bool MatchName(ulong gameObject, string name, StringComparison comparison)
        {
            if (!Memory.TryReadValue<ulong>(gameObject + UnityOffsets.GO_Name, out var namePtr, false)) return false;
            if (!ArenaUtils.IsValidVirtualAddress(namePtr)) return false;
            return Memory.TryReadString(namePtr, out var goName, 64, false)
                && goName is not null
                && goName.Contains(name, comparison);
        }
    }

    internal static class Il2CppClass
    {
        public static string? ReadName(ulong objectClass, int maxLength = 64, bool useCache = false)
        {
            if (!Memory.TryReadPtrChain(objectClass, UnityOffsets.ObjClass_ToNamePtr, out ulong namePtr, useCache))
                return null;
            if (!ArenaUtils.IsValidVirtualAddress(namePtr)) return null;
            return Memory.TryReadString(namePtr, out var name, maxLength, useCache) ? name : null;
        }
    }
}
