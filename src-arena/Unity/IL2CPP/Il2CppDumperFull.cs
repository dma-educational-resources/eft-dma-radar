#pragma warning disable IDE0130

using System.IO;
using UTF8String = eft_dma_radar.Arena.Misc.UTF8String;
using ArenaUtils = eft_dma_radar.Arena.Misc.Utils;
using IScatterEntry = eft_dma_radar.Arena.DMA.ScatterAPI.IScatterEntry;
using ScatterReadEntry = eft_dma_radar.Arena.DMA.ScatterAPI.ScatterReadEntry<eft_dma_radar.Arena.Misc.UTF8String>;

namespace eft_dma_radar.Arena.Unity.IL2CPP
{
    public static partial class Il2CppDumper
    {
        private static readonly string FullDumpFilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "il2cpp_full_dump.txt");

        // ── Extended FieldInfo struct ────────────────────────────────────────────
        [StructLayout(LayoutKind.Explicit, Size = 0x20)]
        private struct RawFieldInfoFullEx
        {
            [FieldOffset(0x00)] public ulong NamePtr;
            [FieldOffset(0x08)] public ulong TypePtr;
            [FieldOffset(0x18)] public int Offset;
        }

        // ── Il2CppType header ────────────────────────────────────────────────────
        [StructLayout(LayoutKind.Explicit, Size = 0x10)]
        private struct RawIl2CppTypeEx
        {
            [FieldOffset(0x00)] public ulong Data;
            [FieldOffset(0x08)] public ushort Attrs;
            [FieldOffset(0x0A)] public byte TypeEnum;
            [FieldOffset(0x0B)] public byte Flags;
        }

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
            0x11 => "valuetype",
            0x12 => "class",
            0x14 => "[,]",
            0x15 => "generic<>",
            0x18 => "IntPtr",
            0x19 => "UIntPtr",
            0x1C => "object",
            0x1D => "[]",
            0x55 => "enum",
            _ => $"type_0x{t:X2}",
        };

        private static string ResolveClassTypeName(
            ulong data,
            Dictionary<int, string> indexToName,
            Dictionary<ulong, string> ptrToName,
            string fallback)
        {
            int idx = (int)(uint)(data & 0xFFFF_FFFF);
            if (idx >= 0 && indexToName.TryGetValue(idx, out var byIndex))
                return byIndex;
            if (ArenaUtils.IsValidVirtualAddress(data) && ptrToName.TryGetValue(data, out var byPtr))
                return byPtr;
            return fallback;
        }

        // ── ReadClassFieldsFull ──────────────────────────────────────────────────

        private static List<(string Name, int Offset, string TypeName)> ReadClassFieldsFull(
            ulong klassPtr,
            Dictionary<int, string> typeIndexToName,
            Dictionary<ulong, string> typePtrToName)
        {
            var result = new List<(string, int, string)>();

            var fieldCount = Memory.ReadValue<ushort>(klassPtr + K_FieldCount, false);
            if (fieldCount == 0 || fieldCount > 4096) return result;

            var fieldsBase = ReadPtr(klassPtr + K_Fields);
            if (!ArenaUtils.IsValidVirtualAddress(fieldsBase)) return result;

            RawFieldInfoFullEx[] rawFields;
            try { rawFields = Memory.ReadArray<RawFieldInfoFullEx>(fieldsBase, fieldCount, false); }
            catch { return result; }

            // Scatter round: name strings + Il2CppType structs
            var nameEntries = new Arena.DMA.ScatterAPI.ScatterReadEntry<UTF8String>[rawFields.Length];
            var typeEntries = new Arena.DMA.ScatterAPI.ScatterReadEntry<RawIl2CppTypeEx>[rawFields.Length];
            var scatter = new List<IScatterEntry>(rawFields.Length * 2);

            for (int i = 0; i < rawFields.Length; i++)
            {
                if (ArenaUtils.IsValidVirtualAddress(rawFields[i].NamePtr))
                {
                    nameEntries[i] = Arena.DMA.ScatterAPI.ScatterReadEntry<UTF8String>.Get(rawFields[i].NamePtr, MaxNameLen);
                    scatter.Add(nameEntries[i]);
                }
                if (ArenaUtils.IsValidVirtualAddress(rawFields[i].TypePtr))
                {
                    typeEntries[i] = Arena.DMA.ScatterAPI.ScatterReadEntry<RawIl2CppTypeEx>.Get(rawFields[i].TypePtr, 0);
                    scatter.Add(typeEntries[i]);
                }
            }

            if (scatter.Count > 0)
                Memory.ReadScatter(scatter.ToArray(), false);

            for (int i = 0; i < rawFields.Length; i++)
            {
                string? name = nameEntries[i] is not null && !nameEntries[i].IsFailed
                    ? (string?)(UTF8String?)nameEntries[i].Result : null;
                if (string.IsNullOrEmpty(name)) continue;

                string typeName = "?";
                if (typeEntries[i] is not null && !typeEntries[i].IsFailed)
                {
                    var t = typeEntries[i].Result;
                    typeName = (t.TypeEnum is 0x11 or 0x12 or 0x55)
                        ? ResolveClassTypeName(t.Data, typeIndexToName, typePtrToName, Il2CppTypeEnumName(t.TypeEnum))
                        : Il2CppTypeEnumName(t.TypeEnum);
                }

                result.Add((name, rawFields[i].Offset, typeName));
            }

            return result;
        }

        // ── BuildInflatedGenericLookup ───────────────────────────────────────────

        private static Dictionary<ulong, ulong> BuildInflatedGenericLookup(
            List<(string Name, string Namespace, ulong KlassPtr, int Index)> classes)
        {
            var result = new Dictionary<ulong, ulong>();

            var genericDefs = new Dictionary<string, ulong>(StringComparer.Ordinal);
            foreach (var (name, _, ptr, _) in classes)
                if (name.Contains('`'))
                    genericDefs.TryAdd(name, ptr);

            if (genericDefs.Count == 0)
                return result;

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
            const int ScatterChunkSize = 4096;
            int unresolvedGenericCount = genericDefs.Count;

            for (int depth = 0; depth < MaxParentDepth && active.Count > 0 && unresolvedGenericCount > 0; depth++)
            {
                var parentPtrs = new ulong[active.Count];

                for (int chunkStart = 0; chunkStart < active.Count; chunkStart += ScatterChunkSize)
                {
                    int chunkLen = Math.Min(ScatterChunkSize, active.Count - chunkStart);
                    var entries = new IScatterEntry[chunkLen];
                    var typed = new Arena.DMA.ScatterAPI.ScatterReadEntry<ulong>[chunkLen];
                    for (int j = 0; j < chunkLen; j++)
                    {
                        typed[j] = Arena.DMA.ScatterAPI.ScatterReadEntry<ulong>.Get(
                            walkPtrs[active[chunkStart + j]] + SDK.Offsets.Il2CppClass.Parent, 0);
                        entries[j] = typed[j];
                    }
                    Memory.ReadScatter(entries, false);
                    for (int j = 0; j < chunkLen; j++)
                        if (!typed[j].IsFailed && ArenaUtils.IsValidVirtualAddress(typed[j].Result))
                            parentPtrs[chunkStart + j] = typed[j].Result;
                }

                var validIdx = new List<int>(active.Count);
                for (int j = 0; j < active.Count; j++)
                    if (parentPtrs[j] != 0) validIdx.Add(j);
                if (validIdx.Count == 0) break;

                var namePtrs = new ulong[validIdx.Count];
                for (int chunkStart = 0; chunkStart < validIdx.Count; chunkStart += ScatterChunkSize)
                {
                    int chunkLen = Math.Min(ScatterChunkSize, validIdx.Count - chunkStart);
                    var entries = new IScatterEntry[chunkLen];
                    var typed = new Arena.DMA.ScatterAPI.ScatterReadEntry<ulong>[chunkLen];
                    for (int k = 0; k < chunkLen; k++)
                    {
                        int j = validIdx[chunkStart + k];
                        typed[k] = Arena.DMA.ScatterAPI.ScatterReadEntry<ulong>.Get(parentPtrs[j] + K_Name, 0);
                        entries[k] = typed[k];
                    }
                    Memory.ReadScatter(entries, false);
                    for (int k = 0; k < chunkLen; k++)
                        if (!typed[k].IsFailed && ArenaUtils.IsValidVirtualAddress(typed[k].Result))
                            namePtrs[chunkStart + k] = typed[k].Result;
                }

                var nameStrings = new string?[validIdx.Count];
                for (int chunkStart = 0; chunkStart < validIdx.Count; chunkStart += ScatterChunkSize)
                {
                    int chunkLen = Math.Min(ScatterChunkSize, validIdx.Count - chunkStart);
                    var typed = new Arena.DMA.ScatterAPI.ScatterReadEntry<UTF8String>[chunkLen];
                    var batch = new List<IScatterEntry>(chunkLen);
                    for (int k = 0; k < chunkLen; k++)
                    {
                        if (namePtrs[chunkStart + k] != 0)
                        {
                            typed[k] = Arena.DMA.ScatterAPI.ScatterReadEntry<UTF8String>.Get(namePtrs[chunkStart + k], MaxNameLen);
                            batch.Add(typed[k]);
                        }
                    }
                    if (batch.Count > 0) Memory.ReadScatter(batch.ToArray(), false);
                    for (int k = 0; k < chunkLen; k++)
                        if (typed[k] is not null && !typed[k].IsFailed)
                            nameStrings[chunkStart + k] = (string?)(UTF8String?)typed[k].Result;
                }

                var nextActive = new List<int>(validIdx.Count);
                for (int k = 0; k < validIdx.Count; k++)
                {
                    int j = validIdx[k];
                    int classIdx = active[j];

                    string? parentName = nameStrings[k];
                    if (parentName != null && genericDefs.TryGetValue(parentName, out var defKlass))
                        if (result.TryAdd(defKlass, parentPtrs[j]))
                            unresolvedGenericCount--;

                    walkPtrs[classIdx] = parentPtrs[j];
                    nextActive.Add(classIdx);
                }
                active = nextActive;
            }

            Log.WriteLine($"[Il2CppDumper] DumpAll: Resolved {result.Count}/{genericDefs.Count} inflated generic classes.");
            return result;
        }

        // ── DumpAll ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Dumps every IL2CPP class, field (offset + type), and method RVA to
        /// <c>il2cpp_full_dump.txt</c> next to the executable.
        /// Call once after game has fully loaded for reverse-engineering work.
        /// </summary>
        public static void DumpAll()
        {
            Log.WriteLine("[Il2CppDumper] DumpAll starting...");

            var gaBase = Memory.GameAssemblyBase;
            if (gaBase == 0)
            {
                Log.WriteLine("[Il2CppDumper] DumpAll ERROR: GameAssemblyBase is 0.");
                return;
            }

            if (!ResolveTypeInfoTableRva(gaBase))
            {
                Log.WriteLine("[Il2CppDumper] DumpAll ABORT: TypeInfoTable resolution failed.");
                return;
            }

            ulong tablePtr;
            try { tablePtr = Memory.ReadPtr(gaBase + SDK.Offsets.Special.TypeInfoTableRva, false); }
            catch (Exception ex)
            {
                Log.WriteLine($"[Il2CppDumper] DumpAll ReadPtr failed: {ex.Message}");
                return;
            }

            if (!ArenaUtils.IsValidVirtualAddress(tablePtr))
            {
                Log.WriteLine("[Il2CppDumper] DumpAll: TypeInfoTable pointer is invalid.");
                return;
            }

            Log.WriteLine("[Il2CppDumper] DumpAll: Scanning type table...");
            var classes = ReadAllClassesFromTable(tablePtr);
            Log.WriteLine($"[Il2CppDumper] DumpAll: {classes.Count} classes found. Writing dump...");

            var typeIndexToName = new Dictionary<int, string>(classes.Count);
            var typePtrToName   = new Dictionary<ulong, string>(classes.Count);
            foreach (var (name, ns, ptr, idx) in classes)
            {
                var full = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                typeIndexToName.TryAdd(idx, full);
                typePtrToName.TryAdd(ptr, full);
            }

            var inflated = BuildInflatedGenericLookup(classes);

            try
            {
                using var sw = new StreamWriter(FullDumpFilePath, false, Encoding.UTF8, 1 << 16);

                sw.WriteLine($"// IL2CPP Full Dump — {DateTime.UtcNow:u}");
                sw.WriteLine($"// GameAssembly Base : 0x{gaBase:X16}");
                sw.WriteLine($"// TypeInfoTable RVA : 0x{SDK.Offsets.Special.TypeInfoTableRva:X}");
                sw.WriteLine($"// Total Classes     : {classes.Count}");
                sw.WriteLine();

                int processed = 0;
                foreach (var (name, ns, klassPtr, index) in classes)
                {
                    string fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

                    bool isGenericDef = name.Contains('`');
                    ulong fieldKlassPtr = klassPtr;
                    string? inflatedNote = null;
                    if (isGenericDef && inflated.TryGetValue(klassPtr, out var inflatedPtr))
                    {
                        fieldKlassPtr = inflatedPtr;
                        inflatedNote = $"inflated via 0x{inflatedPtr:X16}";
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

                        // Only dump methods for classes with a small count — avoids
                        // flooding the dump with thousands of compiler-generated methods.
                        var methodCount = Memory.ReadValue<ushort>(klassPtr + K_MethodCount, false);
                        if (methodCount > 0 && methodCount <= 512)
                        {
                            var methods = ReadClassMethods(klassPtr, gaBase);
                            if (methods.Count > 0)
                            {
                                sw.WriteLine($"//   Methods ({methods.Count}):");
                                foreach (var (methodName, rva) in methods.OrderBy(m => m.Value))
                                    sw.WriteLine($"//     +0x{rva:X}  {methodName}");
                            }
                        }
                        else if (methodCount > 512)
                        {
                            sw.WriteLine($"//   Methods ({methodCount}): <skipped — too many>");
                        }
                    }
                    catch
                    {
                        sw.WriteLine($"//   <read error>");
                    }

                    sw.WriteLine();
                    processed++;

                    if (processed % 500 == 0)
                        Thread.Sleep(10);

                    if (processed % 5000 == 0)
                    {
                        Log.WriteLine($"[Il2CppDumper] DumpAll: {processed}/{classes.Count} classes processed...");
                        GC.Collect(0, GCCollectionMode.Default, false);
                    }
                }

                Log.WriteLine($"[Il2CppDumper] DumpAll complete — {processed} classes → {FullDumpFilePath}");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[Il2CppDumper] DumpAll write failed: {ex.Message}");
            }
        }
    }
}
