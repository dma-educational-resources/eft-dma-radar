using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.Misc;
using SDK;

namespace eft_dma_radar.Tarkov.Unity.IL2CPP
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
            [FieldOffset(0x18)] public int   Offset;    // int32 offset (signed)
        }

        // ── Il2CppType header ────────────────────────────────────────────────────
        // data union (0x00, 8 bytes):
        //   CLASS / VALUETYPE → TypeDefinitionIndex (int32, lower 4 bytes)
        //   SZARRAY           → Il2CppArrayType*
        //   GENERICINST       → Il2CppGenericInst*
        //   PTR / BYREF       → Il2CppType*
        [StructLayout(LayoutKind.Explicit, Size = 0x10)]
        private struct RawIl2CppType
        {
            [FieldOffset(0x00)] public ulong Data;      // union (see above)
            [FieldOffset(0x08)] public uint  Attrs;     // field / param attribute flags
            [FieldOffset(0x0C)] public byte  TypeEnum;  // Il2CppTypeEnum value
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
            _    => $"type_0x{t:X2}",
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
            Dictionary<int, string>   indexToName,
            Dictionary<ulong, string> ptrToName,
            string fallback)
        {
            // Strategy 1: TypeDefinitionIndex (lower 32 bits, sign-extended would
            // be negative for large pointers, so we mask to uint first).
            int idx = (int)(uint)(data & 0xFFFF_FFFF);
            if (idx >= 0 && indexToName.TryGetValue(idx, out var byIndex))
                return byIndex;

            // Strategy 2: direct Il2CppClass* pointer embedded in the union.
            if (data.IsValidVirtualAddress() && ptrToName.TryGetValue(data, out var byPtr))
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
            Dictionary<int, string>   typeIndexToName,
            Dictionary<ulong, string> typePtrToName)
        {
            var result = new List<(string, int, string)>();

            var fieldCount = Memory.ReadValue<ushort>(klassPtr + K_FieldCount, false);
            if (fieldCount == 0 || fieldCount > 4096) return result;

            var fieldsBase = ReadPtr(klassPtr + K_Fields);
            if (!fieldsBase.IsValidVirtualAddress()) return result;

            RawFieldInfoFull[] rawFields;
            try { rawFields = Memory.ReadArray<RawFieldInfoFull>(fieldsBase, fieldCount, false); }
            catch { return result; }

            // Single scatter round: name strings + Il2CppType structs together.
            var nameEntries = new ScatterReadEntry<UTF8String>[rawFields.Length];
            var typeEntries = new ScatterReadEntry<RawIl2CppType>[rawFields.Length];
            var scatter     = new List<IScatterEntry>(rawFields.Length * 2);

            for (int i = 0; i < rawFields.Length; i++)
            {
                if (rawFields[i].NamePtr.IsValidVirtualAddress())
                {
                    nameEntries[i] = ScatterReadEntry<UTF8String>.Get(rawFields[i].NamePtr, MaxNameLen);
                    scatter.Add(nameEntries[i]);
                }
                if (rawFields[i].TypePtr.IsValidVirtualAddress())
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
            XMLogging.WriteLine("[Il2CppDumper] DumpAll starting...");

            var gaBase = Memory.GameAssemblyBase;
            if (gaBase == 0)
            {
                XMLogging.WriteLine("[Il2CppDumper] DumpAll ERROR: GameAssemblyBase is 0 — game not ready.");
                return;
            }

            if (!ResolveTypeInfoTableRva(gaBase))
            {
                XMLogging.WriteLine("[Il2CppDumper] DumpAll ABORT: TypeInfoTable resolution failed.");
                return;
            }

            ulong tablePtr;
            try { tablePtr = Memory.ReadPtr(gaBase + Offsets.Special.TypeInfoTableRva, false); }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Il2CppDumper] DumpAll ReadPtr failed: {ex.Message}");
                return;
            }

            if (!tablePtr.IsValidVirtualAddress())
            {
                XMLogging.WriteLine("[Il2CppDumper] DumpAll: TypeInfoTable pointer is invalid.");
                return;
            }

            XMLogging.WriteLine("[Il2CppDumper] DumpAll: Scanning type table...");
            var classes = ReadAllClassesFromTable(tablePtr);
            XMLogging.WriteLine($"[Il2CppDumper] DumpAll: {classes.Count} classes found. Writing dump...");

            // Build type-resolution lookup tables used by ReadClassFieldsFull.
            // TypeDefinitionIndex (= position in typeInfos array) → full class name.
            // Il2CppClass* pointer                                 → full class name.
            var typeIndexToName = new Dictionary<int, string>(classes.Count);
            var typePtrToName   = new Dictionary<ulong, string>(classes.Count);
            foreach (var (name, ns, ptr, idx) in classes)
            {
                var full = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                typeIndexToName.TryAdd(idx, full);
                typePtrToName.TryAdd(ptr, full);
            }

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

                    sw.WriteLine($"// [{index}] {fullName}");
                    sw.WriteLine($"//   Ptr        : 0x{klassPtr:X16}");

                    var fields = ReadClassFieldsFull(klassPtr, typeIndexToName, typePtrToName);
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

                    sw.WriteLine();
                    processed++;

                    if (processed % 5000 == 0)
                        XMLogging.WriteLine($"[Il2CppDumper] DumpAll: {processed}/{classes.Count} classes processed...");
                }

                XMLogging.WriteLine($"[Il2CppDumper] DumpAll complete — {processed} classes written to: {FullDumpFilePath}");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Il2CppDumper] DumpAll write failed: {ex.Message}");
            }
        }
    }
}
