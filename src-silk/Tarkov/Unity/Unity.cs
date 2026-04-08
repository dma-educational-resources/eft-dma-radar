using System.Buffers;
using System.Runtime.InteropServices;
using eft_dma_radar.Silk.DMA;
using SilkUtils = eft_dma_radar.Silk.Misc.Utils;

namespace eft_dma_radar.Silk.Tarkov.Unity
{
    // ─────────────────────────────────────────────────────────────────────────────
    // IL2CPP Unity engine constants, layout structs, and GOM resolution.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// All Unity engine offsets — both IL2CPP object layout and native transform hierarchy.
    /// Update when game patches break functionality.
    /// </summary>
    internal static class UnityOffsets
    {
        // ── GameObject ──────────────────────────────────────────────────────
        public const uint GO_ObjectClass   = 0x80;  // GameObject → ObjectClass (m_Object)
        public const uint GO_Components    = 0x58;  // GameObject → ComponentArray
        public const uint GO_Name          = 0x88;  // GameObject → Name string pointer

        // ── Component ───────────────────────────────────────────────────────
        public const uint Comp_ObjectClass = 0x20;  // Component → ObjectClass (InteractiveClass)
        public const uint Comp_GameObject  = 0x58;  // Component → parent GameObject pointer
        public const uint Comp_Size        = 0x58;  // Component entry stride

        // ── ObjectClass name chain ──────────────────────────────────────────
        public static readonly uint[] ObjClass_ToNamePtr = [0x0, 0x10];

        // ── ModuleBase (UnityPlayer.dll offsets) ────────────────────────────
        public const uint GomFallback        = 0x1A233A0;  // UnityPlayer.dll Dec 2025
        public const uint AllCamerasFallback = 0x19F3080;   // UnityPlayer.dll Dec 2025

        // ── Il2Cpp generic List<T> layout ────────────────────────────────────
        public static class List
        {
            /// <summary>Offset from List base to _items (the backing array pointer).</summary>
            public const uint ArrOffset = 0x10;

            /// <summary>Offset from the array base to the first element (element[0]).</summary>
            public const uint ArrStartOffset = 0x20;
        }

        // ── TransformInternal native layout ──────────────────────────────────
        public static class TransformAccess
        {
            /// <summary>TransformInternal + 0x70 → pointer to TransformHierarchy.</summary>
            public const uint HierarchyOffset = 0x70;

            /// <summary>TransformInternal + 0x78 → int index into the hierarchy arrays.</summary>
            public const uint IndexOffset = 0x78;
        }

        // ── TransformHierarchy native layout ─────────────────────────────────
        public static class TransformHierarchy
        {
            /// <summary>TransformHierarchy + 0x40 → pointer to indices array (int[]).</summary>
            public const uint IndicesOffset = 0x40;

            /// <summary>TransformHierarchy + 0x68 → pointer to vertices array (TrsX[]).</summary>
            public const uint VerticesOffset = 0x68;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// TRS element in a Unity TransformHierarchy vertices array.
    /// Layout: Translation(Vector3) + pad(float) + Rotation(Quaternion) + Scale(Vector3) + pad(float) = 48 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct TrsX
    {
        public readonly Vector3 T;        // translation (12 bytes)
        private readonly float _pad0;     // padding (4 bytes)
        public readonly Quaternion Q;     // rotation (16 bytes)
        public readonly Vector3 S;        // scale (12 bytes)
        private readonly float _pad1;     // padding (4 bytes)
    }

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

    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// IL2CPP ComponentArray layout — embedded inside a GameObject at offset <see cref="UnityOffsets.GO_Components"/>.
    /// </summary>
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

    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mirrors the IL2CPP GameObject struct for component iteration.
    /// Read via <see cref="GameObject.Read(ulong, bool)"/>.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    internal readonly struct GameObject
    {
        private const int MaxStackEntries = 32;

        [FieldOffset(0x08)]
        public readonly int InstanceID;

        [FieldOffset((int)UnityOffsets.GO_ObjectClass)]
        public readonly ulong ObjectClass;

        [FieldOffset((int)UnityOffsets.GO_Components)]
        public readonly ComponentArray Components;

        [FieldOffset((int)UnityOffsets.GO_Name)]
        public readonly ulong NamePtr;

        /// <summary>Reads a <see cref="GameObject"/> from the given pointer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GameObject Read(ulong gameObjectPtr, bool useCache = false)
            => Memory.ReadValue<GameObject>(gameObjectPtr, useCache);

        /// <summary>Reads the name string from this GameObject.</summary>
        public string GetName(int maxLen = 64)
        {
            if (!SilkUtils.IsValidVirtualAddress(NamePtr))
                return string.Empty;
            return Memory.TryReadString(NamePtr, out var s, maxLen, false) ? s! : string.Empty;
        }

        // ── Component entries reader (stackalloc fast-path) ──────────────────

        private readonly bool TryReadEntries(out ComponentArray.Entry[] entries, out int count)
        {
            entries = [];
            count = 0;

            if (!SilkUtils.IsValidVirtualAddress(Components.ArrayBase) || Components.Size == 0)
                return false;

            count = (int)Math.Min(Components.Size, 0x400);
            entries = count <= MaxStackEntries
                ? new ComponentArray.Entry[count]
                : ArrayPool<ComponentArray.Entry>.Shared.Rent(count);

            return Memory.TryReadBuffer(Components.ArrayBase, entries.AsSpan(0, count), false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ReturnEntries(ComponentArray.Entry[] entries, int count)
        {
            if (count > MaxStackEntries)
                ArrayPool<ComponentArray.Entry>.Shared.Return(entries);
        }

        // ── Component by class name ─────────────────────────────────────────

        /// <summary>
        /// Iterates the ComponentArray looking for a component whose ObjectClass
        /// has the specified IL2CPP class name. Returns the ObjectClass pointer, or 0.
        /// </summary>
        public ulong GetComponent(string className)
        {
            if (string.IsNullOrWhiteSpace(className))
                return 0;

            if (!TryReadEntries(out var entries, out int count))
                return 0;

            try
            {
                for (int i = 0; i < count; i++)
                {
                    var compPtr = entries[i].Component;
                    if (!SilkUtils.IsValidVirtualAddress(compPtr))
                        continue;

                    if (!Memory.TryReadPtr(compPtr + UnityOffsets.Comp_ObjectClass, out var objectClass, false))
                        continue;

                    var name = Il2CppClass.ReadName(objectClass, 128);
                    if (name is not null && name.Equals(className, StringComparison.OrdinalIgnoreCase))
                        return objectClass;
                }
            }
            finally
            {
                ReturnEntries(entries, count);
            }

            return 0;
        }

        /// <summary>
        /// Static helper: reads a GameObject from <paramref name="gameObjectPtr"/> and finds
        /// a component by class name. Returns the ObjectClass pointer, or 0.
        /// </summary>
        public static ulong GetComponent(ulong gameObjectPtr, string className)
        {
            if (!SilkUtils.IsValidVirtualAddress(gameObjectPtr))
                return 0;
            return Read(gameObjectPtr).GetComponent(className);
        }

        // ── Component by klass pointer ──────────────────────────────────────

        /// <summary>
        /// Iterates the ComponentArray comparing each component's klass pointer (at objectClass+0x0)
        /// against a pre-resolved Il2CppClass pointer. Avoids reading class name strings.
        /// Returns the ObjectClass pointer of the matching component, or 0.
        /// </summary>
        public ulong GetComponentByKlassPtr(ulong klassPtr)
        {
            if (!SilkUtils.IsValidVirtualAddress(klassPtr))
                return 0;

            if (!TryReadEntries(out var entries, out int count))
                return 0;

            try
            {
                for (int i = 0; i < count; i++)
                {
                    var compPtr = entries[i].Component;
                    if (!SilkUtils.IsValidVirtualAddress(compPtr))
                        continue;

                    if (!Memory.TryReadPtr(compPtr + UnityOffsets.Comp_ObjectClass, out var objectClass, false))
                        continue;

                    if (Memory.TryReadValue<ulong>(objectClass, out var klass, false) && klass == klassPtr)
                        return objectClass;
                }
            }
            finally
            {
                ReturnEntries(entries, count);
            }

            return 0;
        }

        /// <summary>
        /// Static helper: reads a GameObject from <paramref name="gameObjectPtr"/> and finds
        /// a component by klass pointer. Returns the ObjectClass pointer, or 0.
        /// </summary>
        public static ulong GetComponentByKlassPtr(ulong gameObjectPtr, ulong klassPtr)
        {
            if (!SilkUtils.IsValidVirtualAddress(gameObjectPtr) || !SilkUtils.IsValidVirtualAddress(klassPtr))
                return 0;
            return Read(gameObjectPtr).GetComponentByKlassPtr(klassPtr);
        }

        // ── GetComponentFromBehaviour ────────────────────────────────────────

        /// <summary>
        /// Navigates from a behaviour/component's ObjectClass back to its parent
        /// GameObject, then searches that GameObject's components for the specified
        /// class name. Returns the ObjectClass pointer of the sibling component, or 0.
        /// </summary>
        public static ulong GetComponentFromBehaviour(ulong componentObject, string className)
        {
            if (!SilkUtils.IsValidVirtualAddress(componentObject))
                return 0;

            if (!Memory.TryReadPtr(componentObject + UnityOffsets.Comp_GameObject, out var gameObjectPtr, false))
                return 0;

            return GetComponent(gameObjectPtr, className);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mirrors the IL2CPP GameObjectManager struct.
    /// [0x20] LastActiveNode  — pointer to last LinkedListObject
    /// [0x28] ActiveNodes     — pointer to first LinkedListObject
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    internal readonly struct GOM
    {
        private const int MaxWalkNodes = 100_000;

        [FieldOffset(0x20)] public readonly ulong LastActiveNode;
        [FieldOffset(0x28)] public readonly ulong ActiveNodes;

        // ── Name cache ───────────────────────────────────────────────────────
        private static readonly Dictionary<string, ulong> _nameCache = new();
        private static readonly Lock _cacheLock = new();

        public static void ClearCache()
        {
            lock (_cacheLock)
                _nameCache.Clear();
        }

        // ── Cached resolved addresses ────────────────────────────────────────
        private static ulong _cachedGomAddr;
        private static ulong _cachedAllCamerasAddr;

        /// <summary>Reads the GOM struct from a resolved GOM address.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GOM Get(ulong gomAddress)
            => Memory.ReadValue<GOM>(gomAddress, false);

        // ── GOM address resolution ────────────────────────────────────────────

        // ── Direct signatures: mov [rip+rel32] / mov reg,[rip+rel32] ─────
        // These reference the GOM global directly via a RIP-relative operand.
        // (Sig, RelOffset, InstrLen, Desc)
        private static readonly (string Sig, int RelOff, int InstrLen, string Desc)[] GomDirectSigs =
        [
            // mov [rip+rel32], rax — GOM init store
            ("48 89 05 ? ? ? ? 48 83 C4 ? C3 33 C9", 3, 7, "mov [rip+rel32],rax (GOM init store)"),
            // mov [rip+rel32], rbp — GOM store variant
            ("48 89 2D ? ? ? ? 48 8B 6C 24 ? 48 83 C4 ? 5E C3 33 ED", 3, 7, "mov [rip+rel32],rbp (GOM store)"),
            // mov rsi, [rip+rel32] — GOM read
            ("48 8B 35 ? ? ? ? 48 85 F6 0F 84 ? ? ? ? 8B 46", 3, 7, "mov rsi,[rip+rel32] (GOM read)"),
            // mov rdx, [rip+rel32] — GOM read variant
            ("48 8B 15 ? ? ? ? 48 83 C2 ? 48 3B DA", 3, 7, "mov rdx,[rip+rel32] (GOM read)"),
            // mov rcx, [rip+rel32] — GOM read variant
            ("48 8B 0D ? ? ? ? 4C 8D 4C 24 ? 4C 8D 44 24 ? 89 44 24", 3, 7, "mov rcx,[rip+rel32] (GOM read)"),
        ];

        // ── Call-site signatures: E8 rel32 → sub_180A40AB0 (GOM getter) ──
        // These are call-site patterns that invoke the GOM getter function
        // (sub_180A40AB0: mov rax, cs:qword_181A233A0; retn).
        // We resolve the E8 target, then read the getter body to find the global.
        // (Sig, RelOffset, InstrLen, Desc)
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

        // ── Broad signatures: short patterns that may match many sites ─────
        // Generic mov [rip+rel32] store patterns — extremely patch-resilient.
        // Will match 100+ locations, so we use FindSignatures (multi-match)
        // and validate each candidate with IsValidGomPtr().
        // (Sig, RelOffset, InstrLen, Desc)
        private static readonly (string Sig, int RelOff, int InstrLen, string Desc)[] GomBroadSigs =
        [
            // mov [rip+rel32],reg; add rsp,imm8 — generic store before epilogue
            ("48 89 05 ? ? ? ? 48 83 C4", 3, 7, "mov [rip+rel32],rax; add rsp (broad)"),
        ];

        private const int BroadSigMaxMatches = 256;

        public static ulong GetAddr(ulong unityBase)
        {
            if (SilkUtils.IsValidVirtualAddress(_cachedGomAddr))
                return _cachedGomAddr;

            // Phase 1: Try direct mov [rip+rel32] signatures — read the GOM global directly
            foreach (var (sig, relOff, instrLen, desc) in GomDirectSigs)
            {
                try
                {
                    ulong addr = Memory.FindSignature(sig, "UnityPlayer.dll");
                    if (!SilkUtils.IsValidVirtualAddress(addr))
                        continue;

                    int rva = Memory.ReadValue<int>(addr + (ulong)relOff, false);
                    ulong ptr = Memory.ReadPtr(addr + (ulong)instrLen + (ulong)rva, false);
                    if (SilkUtils.IsValidVirtualAddress(ptr))
                    {
                        Log.WriteLine($"[GOM] Located via direct sig: {desc}");
                        _cachedGomAddr = ptr;
                        return ptr;
                    }
                }
                catch { }
            }

            // Phase 2: Try E8 call-site signatures — resolve call target then read getter body
            foreach (var (sig, relOff, instrLen, desc) in GomCallSiteSigs)
            {
                try
                {
                    ulong callAddr = Memory.FindSignature(sig, "UnityPlayer.dll");
                    if (!SilkUtils.IsValidVirtualAddress(callAddr))
                        continue;

                    int callRel = Memory.ReadValue<int>(callAddr + (ulong)relOff, false);
                    ulong targetFunc = callAddr + (ulong)instrLen + (ulong)callRel;

                    if (!SilkUtils.IsValidVirtualAddress(targetFunc))
                        continue;

                    if (TryResolveGetterGlobal(targetFunc, out var globalPtr))
                    {
                        Log.WriteLine($"[GOM] Located via call-site sig: {desc}");
                        _cachedGomAddr = globalPtr;
                        return globalPtr;
                    }
                }
                catch { }
            }

            // Phase 3: Try broad/generic signatures (multi-match with validation)
            foreach (var (sig, relOff, instrLen, desc) in GomBroadSigs)
            {
                try
                {
                    var matches = Memory.FindSignatures(sig, "UnityPlayer.dll", BroadSigMaxMatches);
                    foreach (var addr in matches)
                    {
                        if (!SilkUtils.IsValidVirtualAddress(addr))
                            continue;

                        int rva = Memory.ReadValue<int>(addr + (ulong)relOff, false);
                        ulong ptr = addr + (ulong)instrLen + (ulong)rva;

                        if (!Memory.TryReadPtr(ptr, out var gomAddr, false))
                            continue;

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

            // Phase 4: Fallback — hardcoded offset
            try
            {
                ulong fallback = Memory.ReadPtr(unityBase + UnityOffsets.GomFallback, false);
                if (SilkUtils.IsValidVirtualAddress(fallback))
                {
                    Log.WriteLine("[GOM] Located via hardcoded offset");
                    _cachedGomAddr = fallback;
                    return fallback;
                }
            }
            catch { }

            throw new InvalidOperationException("Failed to locate GameObjectManager");
        }

        // ── AllCameras address resolution ─────────────────────────────────────

        /// <summary>
        /// Resolves the AllCameras list pointer via hardcoded fallback.
        /// The returned pointer points to a structure where:
        ///   +0x08 = items pointer (ulong[])
        ///   +0x10 = count (int) or end pointer
        /// Returns 0 on failure. Phase 4 — aimview will use this.
        /// AllCameras-specific signatures will be added when identified in IDA.
        /// </summary>
        public static ulong GetAllCamerasAddr(ulong unityBase)
        {
            if (SilkUtils.IsValidVirtualAddress(_cachedAllCamerasAddr))
                return _cachedAllCamerasAddr;

            // Fallback: hardcoded offset
            try
            {
                ulong fallback = Memory.ReadPtr(unityBase + UnityOffsets.AllCamerasFallback, false);
                if (SilkUtils.IsValidVirtualAddress(fallback))
                {
                    Log.WriteLine("[AllCameras] Located via hardcoded offset");
                    _cachedAllCamerasAddr = fallback;
                    return fallback;
                }
            }
            catch { }

            Log.WriteLine("[AllCameras] Resolution failed — Phase 4 features unavailable.");
            return 0;
        }

        /// <summary>
        /// Reads the first 7 bytes of a getter function and checks for:
        ///   48 8B 05 XX XX XX XX  (mov rax, [rip+rel32])
        /// If matched, resolves the RIP-relative global and dereferences it.
        /// </summary>
        private static bool TryResolveGetterGlobal(ulong funcAddr, out ulong result)
        {
            result = 0;
            Span<byte> header = stackalloc byte[7];
            if (!Memory.TryReadBuffer(funcAddr, header, false))
                return false;

            // 48 8B 05 = REX.W mov rax, [rip+rel32]
            if (header[0] != 0x48 || header[1] != 0x8B || header[2] != 0x05)
                return false;

            int innerRel = BitConverter.ToInt32(header[3..]);
            ulong globalAddr = funcAddr + 7 + (ulong)innerRel;

            if (!Memory.TryReadPtr(globalAddr, out result, false))
                return false;

            return SilkUtils.IsValidVirtualAddress(result);
        }

        /// <summary>
        /// Validates that <paramref name="ptr"/> points to a plausible GOM struct.
        /// A valid GOM has readable ActiveNodes (0x28) and LastActiveNode (0x20)
        /// pointers, and the first linked-list node has a valid ThisObject.
        /// </summary>
        private static bool IsValidGomPtr(ulong ptr)
        {
            if (!SilkUtils.IsValidVirtualAddress(ptr))
                return false;

            // Read the two key fields: LastActiveNode (0x20) and ActiveNodes (0x28)
            if (!Memory.TryReadValue<ulong>(ptr + 0x20, out var lastActive, false))
                return false;
            if (!SilkUtils.IsValidVirtualAddress(lastActive))
                return false;

            if (!Memory.TryReadValue<ulong>(ptr + 0x28, out var activeNodes, false))
                return false;
            if (!SilkUtils.IsValidVirtualAddress(activeNodes))
                return false;

            // Probe the first node — it should be a valid LinkedListObject with a valid ThisObject
            if (!Memory.TryReadValue<LinkedListObject>(activeNodes, out var firstNode, false))
                return false;

            return SilkUtils.IsValidVirtualAddress(firstNode.ThisObject);
        }

        /// <summary>
        /// Resets the cached GOM / AllCameras addresses. Call on game stop.
        /// </summary>
        internal static void ResetCachedAddresses()
        {
            _cachedGomAddr = 0;
            _cachedAllCamerasAddr = 0;
            ClearCache();
        }

        // ── Linked-list walk ──────────────────────────────────────────────────

        /// <summary>
        /// Searches the GOM active linked list for a GameObject whose name matches <paramref name="name"/>.
        /// Caches the result for faster subsequent lookups.
        /// Returns 0 if not found.
        /// </summary>
        public ulong GetGameObjectByName(string name, bool ignoreCase = true, bool useCache = true)
        {
            if (string.IsNullOrEmpty(name))
                return 0;

            if (useCache)
            {
                lock (_cacheLock)
                {
                    if (_nameCache.TryGetValue(name, out var cached) && SilkUtils.IsValidVirtualAddress(cached))
                        return cached;
                }
            }

            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            if (!Memory.TryReadValue<LinkedListObject>(ActiveNodes, out var first, false)) return 0;
            if (!Memory.TryReadValue<LinkedListObject>(LastActiveNode, out var last, false)) return 0;

            ulong result = WalkList(first, last, forward: true,
                (node) => MatchName(node.ThisObject, name, comparison) ? node.ThisObject : 0);

            if (result == 0)
                result = WalkList(last, first, forward: false,
                    (node) => MatchName(node.ThisObject, name, comparison) ? node.ThisObject : 0);

            if (SilkUtils.IsValidVirtualAddress(result) && useCache)
            {
                lock (_cacheLock)
                    _nameCache[name] = result;
            }

            return result;
        }

        /// <summary>
        /// Walks the GOM and finds a behaviour component by IL2CPP class name.
        /// Returns the ObjectClass pointer of the matching component, or 0.
        /// </summary>
        public ulong FindBehaviourByClassName(string className)
        {
            if (!Memory.TryReadValue<LinkedListObject>(ActiveNodes, out var first, false)) return 0;
            if (!Memory.TryReadValue<LinkedListObject>(LastActiveNode, out var last, false)) return 0;

            ulong result = WalkList(first, last, forward: true,
                (node) => GameObject.GetComponent(node.ThisObject, className));

            if (result == 0)
                result = WalkList(last, first, forward: false,
                    (node) => GameObject.GetComponent(node.ThisObject, className));

            return result;
        }

        /// <summary>
        /// Walks the GOM and compares each component's klass pointer against a pre-resolved
        /// Il2CppClass pointer. Much faster than name-based lookup (avoids string reads).
        /// Returns the ObjectClass pointer of the matching component, or 0.
        /// </summary>
        public ulong FindBehaviourByKlassPtr(ulong klassPtr)
        {
            if (!SilkUtils.IsValidVirtualAddress(klassPtr))
                return 0;

            if (!Memory.TryReadValue<LinkedListObject>(ActiveNodes, out var first, false)) return 0;
            if (!Memory.TryReadValue<LinkedListObject>(LastActiveNode, out var last, false)) return 0;

            ulong result = WalkList(first, last, forward: true,
                (node) => GameObject.GetComponentByKlassPtr(node.ThisObject, klassPtr));

            if (result == 0)
                result = WalkList(last, first, forward: false,
                    (node) => GameObject.GetComponentByKlassPtr(node.ThisObject, klassPtr));

            return result;
        }

        // ── Generic linked-list walker ───────────────────────────────────────

        /// <summary>
        /// Walks the GOM linked list from <paramref name="start"/> toward <paramref name="end"/>.
        /// For each valid node, invokes <paramref name="visitor"/>. If the visitor returns
        /// a non-zero value, the walk stops and that value is returned.
        /// </summary>
        private static ulong WalkList(
            LinkedListObject start,
            LinkedListObject end,
            bool forward,
            Func<LinkedListObject, ulong> visitor)
        {
            var current = start;
            for (int i = 0; i < MaxWalkNodes; i++)
            {
                if (!SilkUtils.IsValidVirtualAddress(current.ThisObject))
                    break;

                var hit = visitor(current);
                if (SilkUtils.IsValidVirtualAddress(hit))
                    return hit;

                if (current.ThisObject == end.ThisObject)
                    break;

                var nextLink = forward ? current.NextObjectLink : current.PreviousObjectLink;
                if (!Memory.TryReadValue<LinkedListObject>(nextLink, out current, false))
                    break;
            }
            return 0;
        }

        private static bool MatchName(ulong gameObject, string name, StringComparison comparison)
        {
            if (!Memory.TryReadValue<ulong>(gameObject + UnityOffsets.GO_Name, out var namePtr, false))
                return false;
            if (!SilkUtils.IsValidVirtualAddress(namePtr))
                return false;
            return Memory.TryReadString(namePtr, out var goName, 64, false)
                && goName is not null
                && goName.Contains(name, comparison);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the IL2CPP class name from a Unity object.
    /// Chain: objectClass → [0x0, 0x10] → name C-string
    /// </summary>
    internal static class Il2CppClass
    {
        /// <summary>
        /// Returns the IL2CPP class name for <paramref name="objectClass"/>, or null on failure.
        /// </summary>
        public static string? ReadName(ulong objectClass, int maxLength = 64)
        {
            if (!Memory.TryReadPtrChain(objectClass, UnityOffsets.ObjClass_ToNamePtr, out ulong namePtr, false))
                return null;
            if (!SilkUtils.IsValidVirtualAddress(namePtr))
                return null;
            return Memory.TryReadString(namePtr, out var name, maxLength, false) ? name : null;
        }
    }
}
