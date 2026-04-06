#pragma warning disable IDE0130
using UTF8String = eft_dma_radar.Silk.Misc.UTF8String;
using System.IO;
using eft_dma_radar.Silk.DMA.ScatterAPI;

namespace eft_dma_radar.Silk.Tarkov.Unity.IL2CPP
{
    public static partial class Il2CppDumper
    {
        private static readonly string FullDumpFilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "il2cpp_full_dump.txt");

        // ── Extended FieldInfo struct ────────────────────────────────────────────
        // Same stride (0x20) as RawFieldInfo, adds TypePtr at 0x08.
        [StructLayout(LayoutKind.Explicit, Size = 0x20)]
        private struct RawFieldInfoFull
        {
            [FieldOffset(0x00)] public ulong NamePtr;   // char* name
            [FieldOffset(0x08)] public ulong TypePtr;   // Il2CppType*
            [FieldOffset(0x18)] public int Offset;    // int32 offset (signed)
        }

        // ── Il2CppType header ────────────────────────────────────────────────────
        // data union (0x00, 8 bytes):
        //   CLASS / VALUETYPE → TypeDefinitionIndex (int32, lower 4 bytes)
        //   SZARRAY           → Il2CppArrayType*
        //   GENERICINST       → Il2CppGenericInst*
        //   PTR / BYREF       → Il2CppType*
        // Bitfield layout at 0x08 (Little-Endian x64):
        //   [0x08..0x09] attrs     (16 bits)
        //   [0x0A]       type      (8 bits) — Il2CppTypeEnum
        //   [0x0B]       num_mods(5) + byref(1) + pinned(1) + pad(1)
        [StructLayout(LayoutKind.Explicit, Size = 0x10)]
        private struct RawIl2CppType
        {
            [FieldOffset(0x00)] public ulong Data;      // union (see above)
            [FieldOffset(0x08)] public ushort Attrs;    // field / param attribute flags (16 bits)
            [FieldOffset(0x0A)] public byte TypeEnum;   // Il2CppTypeEnum value
            [FieldOffset(0x0B)] public byte Flags;      // num_mods(5) + byref(1) + pinned(1)
        }

        // ── Il2CppTypeEnum → C# type name ────────────────────────────────────────
        private static string Il2CppTypeEnumName(byte t) => t switch
        {
            0x01 => "void",
            0x02 => "bool",
            0x03 => "char",
            0x04 => "sbyte",
            0x05 => "byte",
            0x06 => "short",
            0x07 => "ushort",
            0x08 => "int",
            0x09 => "uint",
            0x0A => "long",
            0x0B => "ulong",
            0x0C => "float",
            0x0D => "double",
            0x0E => "string",
            0x0F => "ptr",
            0x10 => "byref",
            0x11 => "valuetype",   // resolved to class name when possible
            0x12 => "class",       // resolved to class name when possible
            0x14 => "[,]",
            0x15 => "generic<>",
            0x18 => "IntPtr",
            0x19 => "UIntPtr",
            0x1C => "object",
            0x1D => "[]",
            0x55 => "enum",        // resolved to class name when possible
            _ => $"type_0x{t:X2}",
        };

        // ── Type name resolution ─────────────────────────────────────────────────

        /// <summary>
        /// Resolves the human-readable type name for a CLASS, VALUETYPE, or ENUM
        /// Il2CppType entry.
        /// <para>
        /// Tries two strategies in order:
        /// 1. Treat <paramref name="data"/> as a TypeDefinitionIndex (int32, lower
        ///    4 bytes) and look it up in <paramref name="indexToName"/>.
        /// 2. Treat <paramref name="data"/> as an <c>Il2CppClass*</c> and look it
        ///    up in <paramref name="ptrToName"/> (some Unity builds embed the klass
        ///    pointer directly rather than an index).
        /// </para>
        /// Falls back to <paramref name="fallback"/> when neither lookup succeeds.
        /// </summary>
        private static string ResolveClassTypeName(
            ulong data,
            Dictionary<int, string> indexToName,
            Dictionary<ulong, string> ptrToName,
            string fallback)
        {
            // Strategy 1: TypeDefinitionIndex (lower 32 bits, sign-extended would
            // be negative for large pointers, so we mask to uint first).
            int idx = (int)(uint)(data & 0xFFFF_FFFF);
            if (idx >= 0 && indexToName.TryGetValue(idx, out var byIndex))
                return byIndex;

            // Strategy 2: direct Il2CppClass* pointer embedded in the union.
            if (eft_dma_radar.Silk.Misc.Utils.IsValidVirtualAddress(data) && ptrToName.TryGetValue(data, out var byPtr))
                return byPtr;

            return fallback;
        }

        // ── ReadClassFieldsFull ──────────────────────────────────────────────────

        /// <summary>
        /// Like <see cref="ReadClassFields"/>, but also reads each field's
        /// <c>Il2CppType*</c> in the same scatter round and resolves a human-
        /// readable type name.
        /// </summary>
        private static List<(string Name, int Offset, string TypeName)> ReadClassFieldsFull(
            ulong klassPtr,
            Dictionary<int, string> typeIndexToName,
            Dictionary<ulong, string> typePtrToName)
        {
            var result = new List<(string, int, string)>();

            var fieldCount = Memory.ReadValue<ushort>(klassPtr + K_FieldCount, false);
            if (fieldCount == 0 || fieldCount > 4096) return result;

            var fieldsBase = ReadPtr(klassPtr + K_Fields);
            if (!eft_dma_radar.Silk.Misc.Utils.IsValidVirtualAddress(fieldsBase)) return result;

            RawFieldInfoFull[] rawFields;
            try { rawFields = Memory.ReadArray<RawFieldInfoFull>(fieldsBase, fieldCount, false); }
            catch { return result; }

            // Single scatter round: name strings + Il2CppType structs together.
            var nameEntries = new ScatterReadEntry<UTF8String>[rawFields.Length];
            var typeEntries = new ScatterReadEntry<RawIl2CppType>[rawFields.Length];
            var scatter = new List<IScatterEntry>(rawFields.Length * 2);

            for (int i = 0; i < rawFields.Length; i++)
            {
                if (eft_dma_radar.Silk.Misc.Utils.IsValidVirtualAddress(rawFields[i].NamePtr))
                {
                    nameEntries[i] = ScatterReadEntry<UTF8String>.Get(rawFields[i].NamePtr, MaxNameLen);
                    scatter.Add(nameEntries[i]);
                }
                if (eft_dma_radar.Silk.Misc.Utils.IsValidVirtualAddress(rawFields[i].TypePtr))
                {
                    typeEntries[i] = ScatterReadEntry<RawIl2CppType>.Get(rawFields[i].TypePtr, 0);
                    scatter.Add(typeEntries[i]);
                }
            }

            if (scatter.Count > 0)
                Memory.ReadScatter(scatter.ToArray(), false);

            for (int i = 0; i < rawFields.Length; i++)
            {
                string name = nameEntries[i] is not null && !nameEntries[i].IsFailed
                    ? (string)(UTF8String)nameEntries[i].Result
                    : null;
                if (string.IsNullOrEmpty(name)) continue;

                string typeName = "?";
                if (typeEntries[i] is not null && !typeEntries[i].IsFailed)
                {
                    ref var t = ref typeEntries[i].Result;
                    typeName = (t.TypeEnum is 0x11 or 0x12 or 0x55)
                        ? ResolveClassTypeName(t.Data, typeIndexToName, typePtrToName,
                              Il2CppTypeEnumName(t.TypeEnum))
                        : Il2CppTypeEnumName(t.TypeEnum);
                }

                result.Add((name, rawFields[i].Offset, typeName));
            }

            return result;
        }

        // ── DumpAll ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Dumps every IL2CPP class, field (offset + type), and method RVA found
        /// in the TypeInfoTable to <c>il2cpp_full_dump.txt</c> next to the
        /// executable.
        /// Intended for reverse-engineering and SDK authoring — call once after
        /// the game has fully loaded.
        /// </summary>
        public static void DumpAll()
        {
            Log.WriteLine("[Il2CppDumper] DumpAll starting...");

            var gaBase = Memory.GameAssemblyBase;
            if (gaBase == 0)
            {
                Log.WriteLine("[Il2CppDumper] DumpAll ERROR: GameAssemblyBase is 0 — game not ready.");
                return;
            }

            if (!ResolveTypeInfoTableRva(gaBase))
            {
                Log.WriteLine("[Il2CppDumper] DumpAll ABORT: TypeInfoTable resolution failed.");
                return;
            }

            ulong tablePtr;
            try { tablePtr = Memory.ReadPtr(gaBase + Offsets.Special.TypeInfoTableRva, false); }
            catch (Exception ex)
            {
                Log.WriteLine($"[Il2CppDumper] DumpAll ReadPtr failed: {ex.Message}");
                return;
            }

            if (!eft_dma_radar.Silk.Misc.Utils.IsValidVirtualAddress(tablePtr))
            {
                Log.WriteLine("[Il2CppDumper] DumpAll: TypeInfoTable pointer is invalid.");
                return;
            }

            Log.WriteLine("[Il2CppDumper] DumpAll: Scanning type table...");
            var classes = ReadAllClassesFromTable(tablePtr);
            Log.WriteLine($"[Il2CppDumper] DumpAll: {classes.Count} classes found. Writing dump...");

            // Build type-resolution lookup tables used by ReadClassFieldsFull.
            // TypeDefinitionIndex (= position in typeInfos array) → full class name.
            // Il2CppClass* pointer                                 → full class name.
            var typeIndexToName = new Dictionary<int, string>(classes.Count);
            var typePtrToName = new Dictionary<ulong, string>(classes.Count);
            foreach (var (name, ns, ptr, idx) in classes)
            {
                var full = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                typeIndexToName.TryAdd(idx, full);
                typePtrToName.TryAdd(ptr, full);
            }

            // Build a lookup for inflated generic parents.
            // Generic type definitions in the TypeInfoTable have all field offsets = 0.
            // To get real offsets, we walk from concrete (non-generic) children up the
            // parent chain to find the inflated generic instance.
            // Key = TypeInfoTable klassPtr of the generic definition,
            // Value = inflated klass pointer from a child's parent chain.
            var inflatedGenericLookup = BuildInflatedGenericLookup(classes);

            try
            {
                using var sw = new StreamWriter(FullDumpFilePath, false, Encoding.UTF8, 1 << 16);

                sw.WriteLine($"// IL2CPP Full Dump — {DateTime.UtcNow:u}");
                sw.WriteLine($"// GameAssembly Base : 0x{gaBase:X16}");
                sw.WriteLine($"// TypeInfoTable RVA : 0x{Offsets.Special.TypeInfoTableRva:X}");
                sw.WriteLine($"// Total Classes     : {classes.Count}");
                sw.WriteLine();

                int processed = 0;

                foreach (var (name, ns, klassPtr, index) in classes)
                {
                    string fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

                    // For generic definitions, try to use the inflated klass for field offsets.
                    bool isGenericDef = name.Contains('`');
                    ulong fieldKlassPtr = klassPtr;
                    string inflatedNote = null;
                    if (isGenericDef && inflatedGenericLookup.TryGetValue(klassPtr, out var inflated))
                    {
                        fieldKlassPtr = inflated;
                        inflatedNote = $"inflated via 0x{inflated:X16}";
                    }

                    sw.WriteLine($"// [{index}] {fullName}");
                    sw.WriteLine($"//   Ptr        : 0x{klassPtr:X16}");
                    if (inflatedNote != null)
                        sw.WriteLine($"//   Note       : {inflatedNote}");

                    try
                    {
                        var fields = ReadClassFieldsFull(fieldKlassPtr, typeIndexToName, typePtrToName);
                        if (fields.Count > 0)
                        {
                            sw.WriteLine($"//   Fields ({fields.Count}):");
                            foreach (var (fieldName, offset, typeName) in fields.OrderBy(f => f.Offset))
                            {
                                if (offset >= 0)
                                    sw.WriteLine($"//     0x{(uint)offset:X4}  {fieldName,-40} : {typeName}");
                                else
                                    sw.WriteLine($"//     static    {fieldName,-36} : {typeName}");
                            }
                        }

                        var methods = ReadClassMethods(klassPtr, gaBase);
                        if (methods.Count > 0)
                        {
                            sw.WriteLine($"//   Methods ({methods.Count}):");
                            foreach (var (methodName, rva) in methods.OrderBy(m => m.Value))
                                sw.WriteLine($"//     +0x{rva:X}  {methodName}");
                        }
                    }
                    catch
                    {
                        sw.WriteLine($"//   <read error>");
                    }

                    sw.WriteLine();
                    processed++;

                    // Throttle DMA: brief pause every 500 classes to avoid
                    // overwhelming the native VmmScatter handle pool.
                    if (processed % 500 == 0)
                        Thread.Sleep(10);

                    if (processed % 5000 == 0)
                    {
                        Log.WriteLine($"[Il2CppDumper] DumpAll: {processed}/{classes.Count} classes processed...");
                        GC.Collect(0, GCCollectionMode.Default, false);
                    }
                }

                Log.WriteLine($"[Il2CppDumper] DumpAll complete — {processed} classes written to: {FullDumpFilePath}");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[Il2CppDumper] DumpAll write failed: {ex.Message}");
            }
        }

        /// <summary>
        /// For every generic type definition in the TypeInfoTable (name contains <c>`</c>),
        /// scans non-generic classes to find one whose parent chain contains the generic
        /// definition. Returns the *inflated* parent klass pointer (which has real field
        /// offsets) keyed by the TypeInfoTable klass pointer of the generic definition.
        /// <para>
        /// Uses batched scatter reads (level-by-level) instead of individual DMA reads
        /// to avoid overwhelming the DMA device with hundreds of thousands of calls.
        /// </para>
        /// </summary>
        private static Dictionary<ulong, ulong> BuildInflatedGenericLookup(
            List<(string Name, string Namespace, ulong KlassPtr, int Index)> classes)
        {
            var result = new Dictionary<ulong, ulong>();

            // Collect all generic definition klass pointers by name.
            var genericDefs = new Dictionary<string, ulong>(StringComparer.Ordinal);
            foreach (var (name, _, ptr, _) in classes)
            {
                if (name.Contains('`'))
                    genericDefs.TryAdd(name, ptr);
            }

            if (genericDefs.Count == 0)
                return result;

            // Build the working set: indices of non-generic classes and their current walk pointer.
            var walkPtrs = new ulong[classes.Count];
            var active = new List<int>(classes.Count);
            for (int i = 0; i < classes.Count; i++)
            {
                if (!classes[i].Name.Contains('`'))
                {
                    walkPtrs[i] = classes[i].KlassPtr;
                    active.Add(i);
                }
            }

            const int MaxParentDepth = 8;
            const int ScatterChunkSize = 4096; // max entries per scatter call
            int unresolvedGenericCount = genericDefs.Count;

            for (int depth = 0; depth < MaxParentDepth && active.Count > 0 && unresolvedGenericCount > 0; depth++)
            {
                var parentPtrs = new ulong[active.Count];

                // Round 1: Scatter-read parent pointers in chunks.
                for (int chunkStart = 0; chunkStart < active.Count; chunkStart += ScatterChunkSize)
                {
                    int chunkEnd = Math.Min(chunkStart + ScatterChunkSize, active.Count);
                    int chunkLen = chunkEnd - chunkStart;
                    var entries = new IScatterEntry[chunkLen];
                    var typed = new ScatterReadEntry<ulong>[chunkLen];
                    for (int j = 0; j < chunkLen; j++)
                    {
                        ulong addr = walkPtrs[active[chunkStart + j]] + Offsets.Il2CppClass.Parent;
                        typed[j] = ScatterReadEntry<ulong>.Get(addr, 0);
                        entries[j] = typed[j];
                    }
                    Memory.ReadScatter(entries, false);
                    for (int j = 0; j < chunkLen; j++)
                    {
                        if (!typed[j].IsFailed && eft_dma_radar.Silk.Misc.Utils.IsValidVirtualAddress(typed[j].Result))
                            parentPtrs[chunkStart + j] = typed[j].Result;
                    }
                }

                // Collect valid parents.
                var validParentIndices = new List<int>(active.Count);
                for (int j = 0; j < active.Count; j++)
                {
                    if (parentPtrs[j] != 0)
                        validParentIndices.Add(j);
                }
                if (validParentIndices.Count == 0)
                    break;

                // Round 2: Scatter-read name pointers in chunks.
                var namePtrs = new ulong[validParentIndices.Count];
                for (int chunkStart = 0; chunkStart < validParentIndices.Count; chunkStart += ScatterChunkSize)
                {
                    int chunkEnd = Math.Min(chunkStart + ScatterChunkSize, validParentIndices.Count);
                    int chunkLen = chunkEnd - chunkStart;
                    var entries = new IScatterEntry[chunkLen];
                    var typed = new ScatterReadEntry<ulong>[chunkLen];
                    for (int k = 0; k < chunkLen; k++)
                    {
                        int j = validParentIndices[chunkStart + k];
                        typed[k] = ScatterReadEntry<ulong>.Get(parentPtrs[j] + K_Name, 0);
                        entries[k] = typed[k];
                    }
                    Memory.ReadScatter(entries, false);
                    for (int k = 0; k < chunkLen; k++)
                    {
                        if (!typed[k].IsFailed && eft_dma_radar.Silk.Misc.Utils.IsValidVirtualAddress(typed[k].Result))
                            namePtrs[chunkStart + k] = typed[k].Result;
                    }
                }

                // Round 3: Scatter-read name strings in chunks.
                var nameStrings = new string[validParentIndices.Count];
                for (int chunkStart = 0; chunkStart < validParentIndices.Count; chunkStart += ScatterChunkSize)
                {
                    int chunkEnd = Math.Min(chunkStart + ScatterChunkSize, validParentIndices.Count);
                    int chunkLen = chunkEnd - chunkStart;
                    var typed = new ScatterReadEntry<UTF8String>[chunkLen];
                    var scatter = new List<IScatterEntry>(chunkLen);
                    for (int k = 0; k < chunkLen; k++)
                    {
                        if (namePtrs[chunkStart + k] != 0)
                        {
                            typed[k] = ScatterReadEntry<UTF8String>.Get(namePtrs[chunkStart + k], MaxNameLen);
                            scatter.Add(typed[k]);
                        }
                    }
                    if (scatter.Count > 0)
                        Memory.ReadScatter(scatter.ToArray(), false);
                    for (int k = 0; k < chunkLen; k++)
                    {
                        if (typed[k] is not null && !typed[k].IsFailed)
                            nameStrings[chunkStart + k] = (string)(UTF8String)typed[k].Result;
                    }
                }

                // Process results and advance walk pointers.
                var nextActive = new List<int>(validParentIndices.Count);
                for (int k = 0; k < validParentIndices.Count; k++)
                {
                    int j = validParentIndices[k];
                    int classIdx = active[j];

                    string parentName = nameStrings[k];
                    if (parentName != null && genericDefs.TryGetValue(parentName, out var defKlass))
                    {
                        if (result.TryAdd(defKlass, parentPtrs[j]))
                            unresolvedGenericCount--;
                    }

                    walkPtrs[classIdx] = parentPtrs[j];
                    nextActive.Add(classIdx);
                }

                active = nextActive;
            }

            Log.WriteLine($"[Il2CppDumper] DumpAll: Resolved {result.Count}/{genericDefs.Count} inflated generic classes via parent-chain walk.");
            return result;
        }
    }
}
