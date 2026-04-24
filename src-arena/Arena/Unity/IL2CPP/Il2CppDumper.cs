#pragma warning disable IDE0130

using System.IO;
using UTF8String = eft_dma_radar.Arena.Misc.UTF8String;
using ArenaUtils = eft_dma_radar.Arena.Misc.Utils;

namespace eft_dma_radar.Arena.Unity.IL2CPP
{
    /// <summary>
    /// Arena IL2CPP offset dumper â€” adapted from src-silk Il2CppDumper.
    /// Resolves IL2CPP offsets at runtime and applies them to <see cref="SDK.Offsets"/>
    /// via reflection. Cache is stored in %AppData%\eft-dma-radar-arena\il2cpp_offsets.json.
    /// </summary>
    public static partial class Il2CppDumper
    {
        // â”€â”€ Internal constants â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private const int MaxClasses  = 50_000;
        private const int MaxNameLen  = 128;

        private static readonly uint K_Name       = SDK.Offsets.Il2CppClass.Name;
        private static readonly uint K_FieldCount = SDK.Offsets.Il2CppClass.FieldCount;
        private static readonly uint K_Fields     = SDK.Offsets.Il2CppClass.Fields;
        private static readonly uint K_MethodCount = SDK.Offsets.Il2CppClass.MethodCount;
        private static readonly uint K_Methods    = SDK.Offsets.Il2CppClass.Methods;

        [StructLayout(LayoutKind.Sequential)]
        private struct ClassNamePtrs { public ulong NamePtr; public ulong NamespacePtr; }

        [StructLayout(LayoutKind.Explicit, Size = 0x20)]
        private struct RawFieldInfo
        {
            [FieldOffset(0x00)] public ulong NamePtr;
            [FieldOffset(0x08)] public ulong TypePtr;
            [FieldOffset(0x18)] public int Offset;
        }

        [StructLayout(LayoutKind.Explicit, Size = 0x20)]
        private struct RawFieldInfoFull
        {
            [FieldOffset(0x00)] public ulong NamePtr;
            [FieldOffset(0x08)] public ulong TypePtr;
            [FieldOffset(0x18)] public int Offset;
        }

        [StructLayout(LayoutKind.Explicit, Size = 0x20)]
        private struct RawMethodInfo
        {
            [FieldOffset(0x00)] public ulong MethodPointer; // void* methodPointer
            [FieldOffset(0x18)] public ulong NamePtr;       // char* name
        }

        // Il2CppType layout (0x10 bytes):
        //   [0x00..0x07] data       (union: Il2CppClass*, TypeDefinitionIndex, etc.)
        //   [0x08..0x09] attrs      (uint16)
        //   [0x0A]       type       (uint8) â€” Il2CppTypeEnum
        //   [0x0B]       flags      (num_mods(5) + byref(1) + pinned(1))
        [StructLayout(LayoutKind.Explicit, Size = 0x10)]
        private struct RawIl2CppType
        {
            [FieldOffset(0x00)] public ulong Data;
            [FieldOffset(0x08)] public ushort Attrs;
            [FieldOffset(0x0A)] public byte TypeEnum;
            [FieldOffset(0x0B)] public byte Flags;
        }

        // â”€â”€ State â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static volatile bool _dumped;

        // â”€â”€ Entry point â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public static void Dump()
        {
            if (_dumped)
            {
                Log.WriteLine("[Il2CppDumper] Already dumped this session â€” skipping.");
                return;
            }

            Log.WriteLine("[Il2CppDumper] Dump starting...");

            var gaBase = Memory.GameAssemblyBase;
            if (gaBase == 0)
            {
                Log.WriteLine("[Il2CppDumper] ERROR: GameAssemblyBase is 0 â€” game not ready.");
                return;
            }

            // Fast path: PE fingerprint match
            if (TryFastLoadCache(gaBase))
            {
                _dumped = true;
                return;
            }

            // Resolve TypeInfoTableRva via sig scan
            const int maxRvaRetries = 30;
            bool rvaResolved = false;
            for (int rvaAttempt = 1; rvaAttempt <= maxRvaRetries; rvaAttempt++)
            {
                if (ResolveTypeInfoTableRva(gaBase, quiet: rvaAttempt < maxRvaRetries))
                {
                    rvaResolved = true;
                    break;
                }
                if (rvaAttempt < maxRvaRetries)
                {
                    int delay = rvaAttempt <= 10 ? 1000 : 2000;
                    Log.WriteLine($"[Il2CppDumper] TypeInfoTable not ready, retrying in {delay}ms... ({rvaAttempt}/{maxRvaRetries})");
                    Thread.Sleep(delay);
                }
            }

            if (!rvaResolved)
            {
                Log.WriteLine("[Il2CppDumper] ABORT: TypeInfoTable resolution failed after all retries.");
                return;
            }

            // Cache path: RVA match
            if (TryLoadCache(SDK.Offsets.Special.TypeInfoTableRva))
            {
                _dumped = true;
                Log.WriteLine("[Il2CppDumper] Offsets restored from cache â€” live dump skipped.");
                SaveCache();
                return;
            }

            // Live path: read TypeInfoTable from memory
            ulong tablePtr;
            try { tablePtr = Memory.ReadPtr(gaBase + SDK.Offsets.Special.TypeInfoTableRva, false); }
            catch (Exception ex)
            {
                Log.WriteLine($"[Il2CppDumper] ReadPtr(TypeInfoTableRva) failed: {ex.Message}");
                return;
            }

            if (!ArenaUtils.IsValidVirtualAddress(tablePtr))
            {
                Log.WriteLine("[Il2CppDumper] TypeInfoTable pointer is invalid.");
                return;
            }

            const int MinExpectedClasses = 1_000;
            const int maxRetries = 10;
            List<(string Name, string Namespace, ulong KlassPtr, int Index)> classes = [];

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                classes = ReadAllClassesFromTable(tablePtr);
                if (classes.Count >= MinExpectedClasses) break;
                if (attempt < maxRetries)
                {
                    Log.WriteLine($"[Il2CppDumper] Only {classes.Count} classes found (expected â‰¥{MinExpectedClasses}), retrying... ({attempt}/{maxRetries})");
                    Thread.Sleep(1000);
                }
            }

            if (classes.Count < MinExpectedClasses)
            {
                Log.WriteLine($"[Il2CppDumper] ABORT: Only {classes.Count} classes found â€” TypeInfoTable likely corrupt or stale.");
                return;
            }

            var nameLookup = new Dictionary<string, ulong>(classes.Count * 2, StringComparer.Ordinal);
            var nameToIndex = new Dictionary<string, int>(classes.Count * 2, StringComparer.Ordinal);
            var baseNameSeen = new Dictionary<string, int>(classes.Count, StringComparer.Ordinal);

            foreach (var (name, _, ptr, idx) in classes)
            {
                var san = SanitizeName(name);
                nameLookup.TryAdd(name, ptr);
                nameToIndex.TryAdd(name, idx);
                if (san != name) { nameLookup.TryAdd(san, ptr); nameToIndex.TryAdd(san, idx); }

                if (baseNameSeen.TryGetValue(san, out int seen))
                {
                    int next = seen + 1;
                    baseNameSeen[san] = next;
                    nameLookup.TryAdd($"{san}_{next}", ptr);
                    nameToIndex.TryAdd($"{san}_{next}", idx);
                }
                else baseNameSeen[san] = 1;
            }

            ResolveTypeIndices(nameToIndex, classes);

            var schema = BuildSchema();

            var offsetsType = typeof(SDK.Offsets);
            const BindingFlags bf = BindingFlags.Public | BindingFlags.Static;

            int updated = 0, fallback = 0, classesSkipped = 0;

            foreach (var sc in schema)
            {
                ulong klassPtr;
                string resolvedVia;

                if (sc.TypeIndex.HasValue)
                {
                    klassPtr = ReadPtr(tablePtr + (ulong)sc.TypeIndex.Value * 8UL);
                    resolvedVia = $"TypeIndex={sc.TypeIndex.Value}";
                    if (!ArenaUtils.IsValidVirtualAddress(klassPtr))
                    {
                        Log.WriteLine($"[Il2CppDumper] SKIP '{sc.CsName}': TypeIndex={sc.TypeIndex.Value} resolved to invalid pointer.");
                        classesSkipped++;
                        continue;
                    }
                }
                else if (sc.ResolveViaChild is not null)
                {
                    if (!nameLookup.TryGetValue(sc.ResolveViaChild, out var childKlass))
                    {
                        Log.WriteLine($"[Il2CppDumper] SKIP '{sc.CsName}': child class '{sc.ResolveViaChild}' not found in type table.");
                        classesSkipped++;
                        continue;
                    }
                    klassPtr = 0;
                    ulong walkPtr = childKlass;
                    for (int depth = 0; depth < 16 && ArenaUtils.IsValidVirtualAddress(walkPtr); depth++)
                    {
                        ulong parentPtr = ReadPtr(walkPtr + SDK.Offsets.Il2CppClass.Parent);
                        if (!ArenaUtils.IsValidVirtualAddress(parentPtr)) break;
                        ulong parentNamePtr = ReadPtr(parentPtr + K_Name);
                        string? parentName = ReadStr(parentNamePtr);
                        if (parentName != null && parentName == sc.Il2CppName) { klassPtr = parentPtr; break; }
                        walkPtr = parentPtr;
                    }
                    if (klassPtr == 0)
                    {
                        Log.WriteLine($"[Il2CppDumper] SKIP '{sc.CsName}': parent '{sc.Il2CppName}' not found.");
                        classesSkipped++;
                        continue;
                    }
                    resolvedVia = $"child=\"{sc.ResolveViaChild}\"â†’parent=\"{sc.Il2CppName}\"";
                }
                else
                {
                    if (!nameLookup.TryGetValue(sc.Il2CppName, out klassPtr))
                    {
                        Log.WriteLine($"[Il2CppDumper] SKIP '{sc.Il2CppName}': not found in type table.");
                        classesSkipped++;
                        continue;
                    }
                    resolvedVia = $"name=\"{sc.Il2CppName}\"";
                }

                var nestedType = offsetsType.GetNestedType(sc.CsName, BindingFlags.Public | BindingFlags.NonPublic);
                if (nestedType is null)
                {
                    Log.WriteLine($"[Il2CppDumper] WARN: struct SDK.Offsets.{sc.CsName} not found via reflection â€” skipping.");
                    classesSkipped++;
                    continue;
                }

                var fieldMap  = ReadClassFields(klassPtr);
                var methodMap = sc.Fields.Any(sf => sf.Kind == FieldKind.MethodRva)
                    ? ReadClassMethods(klassPtr, gaBase) : null;

                foreach (var sf in sc.Fields)
                {
                    if (sf.Kind == FieldKind.MethodRva)
                    {
                        var methodName = sf.Il2CppName.EndsWith("_RVA", StringComparison.Ordinal)
                            ? sf.Il2CppName[..^4] : sf.Il2CppName;
                        if (methodMap is not null && methodMap.TryGetValue(methodName, out var rva))
                        {
                            if (TrySetField(nestedType, sf.CsName, rva, bf)) updated++;
                            else fallback++;
                        }
                        else { Log.WriteLine($"[Il2CppDumper] WARN: method '{methodName}' not found in '{sc.CsName}'."); fallback++; }
                    }
                    else
                    {
                        if (!fieldMap.TryGetValue(sf.Il2CppName, out var offset))
                        {
                            var alt = FlipBackingFieldConvention(sf.Il2CppName);
                            if (alt is null || !fieldMap.TryGetValue(alt, out offset))
                            {
                                Log.WriteLine($"[Il2CppDumper] WARN: field '{sf.Il2CppName}' not found in '{sc.CsName}' â€” using fallback.");
                                fallback++;
                                continue;
                            }
                        }
                        object value = offset >= 0 ? (object)(uint)offset : (object)offset;
                        if (TrySetField(nestedType, sf.CsName, value, bf)) updated++;
                        else fallback++;
                    }
                }
            }

            DebugDumpResolverState(classes.Count, updated, fallback, classesSkipped);
            Log.WriteLine($"[Il2CppDumper] Done. {updated} offsets updated, {fallback} fallback, {classesSkipped} skipped.");
            if (fallback > 0 || classesSkipped > 0)
                Log.WriteLine($"IL2CPP dump partial: {fallback} fallback, {classesSkipped} skipped. Check logs.");

            _dumped = true;
            SaveCache();
        }

        #region DumpClassFields

        /// <summary>
        /// Diagnostic helper: reads the IL2CPP klass pointer from an object instance,
        /// walks the entire inheritance chain, and logs every field with its offset,
        /// IL2CPP type name, field name, and live value read from the object instance.
        /// <para>
        /// Output format:
        /// <code>
        /// â”€â”€ Fields of 'label' @ 0xADDR (full hierarchy) â”€â”€
        ///   â”Œ ClassName (klass=0xPTR, N field(s))
        ///   â”‚  [0xOFFSET] type  name = value
        /// </code>
        /// </para>
        /// </summary>
        public static void DumpClassFields(ulong objectAddress, string? label = null)
        {
            try
            {
                if (!ArenaUtils.IsValidVirtualAddress(objectAddress))
                {
                    Log.WriteLine($"[Il2CppDumper] DumpClassFields: invalid object address 0x{objectAddress:X}");
                    return;
                }

                // Il2CppObject layout: first 8 bytes = klass pointer
                ulong klassPtr = ReadPtr(objectAddress);
                if (!ArenaUtils.IsValidVirtualAddress(klassPtr))
                {
                    Log.WriteLine($"[Il2CppDumper] DumpClassFields: invalid klass pointer at 0x{objectAddress:X}");
                    return;
                }

                // Read top-level class name for the header
                ulong topNamePtr = ReadPtr(klassPtr + K_Name);
                string topClassName = ReadStr(topNamePtr) ?? "<unknown>";
                var tag = label ?? topClassName;

                Log.WriteLine($"[Il2CppDumper] â”€â”€ Fields of '{tag}' @ 0x{objectAddress:X} (full hierarchy) â”€â”€");

                // Walk the parent chain: klass â†’ parent â†’ parent â†’ ... â†’ null
                const int MaxDepth = 32;
                int depth = 0;
                ulong currentKlass = klassPtr;

                while (ArenaUtils.IsValidVirtualAddress(currentKlass) && depth < MaxDepth)
                {
                    depth++;
                    DumpSingleClassFieldsWithValues(currentKlass, objectAddress);
                    currentKlass = ReadPtr(currentKlass + SDK.Offsets.Il2CppClass.Parent);
                }

                Log.WriteLine($"[Il2CppDumper] â”€â”€ End of '{tag}' ({depth} class(es) in hierarchy) â”€â”€");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[Il2CppDumper] DumpClassFields error: {ex.Message}");
            }
        }

        /// <summary>
        /// Dumps all fields declared on a single Il2CppClass with their types and
        /// live values read from the object instance at <paramref name="objectAddress"/>.
        /// </summary>
        private static void DumpSingleClassFieldsWithValues(ulong klassPtr, ulong objectAddress)
        {
            // Read class name + namespace
            ulong namePtr = ReadPtr(klassPtr + K_Name);
            string className = ReadStr(namePtr) ?? "<unknown>";

            ulong nsPtr = ReadPtr(klassPtr + 0x18); // Il2CppClass::namespaze
            string ns = ReadStr(nsPtr) ?? string.Empty;
            string fullName = string.IsNullOrEmpty(ns) ? className : $"{ns}.{className}";

            var fieldCount = Memory.ReadValue<ushort>(klassPtr + K_FieldCount, false);

            Log.WriteLine($"[Il2CppDumper]   â”Œ {fullName} (klass=0x{klassPtr:X}, {fieldCount} field(s))");

            if (fieldCount == 0 || fieldCount > 4096)
                return;

            ulong fieldsBase = ReadPtr(klassPtr + K_Fields);
            if (!ArenaUtils.IsValidVirtualAddress(fieldsBase))
            {
                Log.WriteLine($"[Il2CppDumper]   â”‚  (fields pointer invalid)");
                return;
            }

            RawFieldInfoFull[] rawFields;
            try { rawFields = Memory.ReadArray<RawFieldInfoFull>(fieldsBase, fieldCount, false); }
            catch (Exception ex)
            {
                Log.WriteLine($"[Il2CppDumper]   â”‚  (failed to read field array: {ex.Message})");
                return;
            }

            // Scatter read: field name strings + Il2CppType structs
            var nameEntries = new DMA.ScatterAPI.ScatterReadEntry<UTF8String>[rawFields.Length];
            var typeEntries = new DMA.ScatterAPI.ScatterReadEntry<RawIl2CppType>[rawFields.Length];
            var scatter = new List<DMA.ScatterAPI.IScatterEntry>(rawFields.Length * 2);

            for (int i = 0; i < rawFields.Length; i++)
            {
                if (ArenaUtils.IsValidVirtualAddress(rawFields[i].NamePtr))
                {
                    nameEntries[i] = DMA.ScatterAPI.ScatterReadEntry<UTF8String>.Get(rawFields[i].NamePtr, MaxNameLen);
                    scatter.Add(nameEntries[i]);
                }
                if (ArenaUtils.IsValidVirtualAddress(rawFields[i].TypePtr))
                {
                    typeEntries[i] = DMA.ScatterAPI.ScatterReadEntry<RawIl2CppType>.Get(rawFields[i].TypePtr, 0);
                    scatter.Add(typeEntries[i]);
                }
            }

            if (scatter.Count > 0)
                Memory.ReadScatter([.. scatter], false);

            for (int i = 0; i < rawFields.Length; i++)
            {
                string? name = nameEntries[i] is not null && !nameEntries[i].IsFailed
                    ? (string?)(UTF8String?)nameEntries[i].Result
                    : "<unreadable>";

                string typeName = "?";
                byte typeEnum = 0;
                if (typeEntries[i] is not null && !typeEntries[i].IsFailed)
                {
                    ref var t = ref typeEntries[i].Result;
                    typeEnum = t.TypeEnum;
                    typeName = Il2CppTypeEnumName(typeEnum);
                }

                int offset = rawFields[i].Offset;

                string valueStr;
                if (offset < 0)
                    valueStr = "(static)";
                else
                    valueStr = ReadFieldValueString(objectAddress, (uint)offset, typeEnum);

                Log.WriteLine($"[Il2CppDumper]   â”‚  [0x{(uint)offset:X}] {typeName,-12} {name} = {valueStr}");
            }
        }

        private static string ReadFieldValueString(ulong objectAddress, uint offset, byte typeEnum)
        {
            try
            {
                ulong addr = objectAddress + offset;
                return typeEnum switch
                {
                    0x02 => Memory.ReadValue<bool>(addr, false).ToString().ToLowerInvariant(),
                    0x03 => $"'{(char)Memory.ReadValue<ushort>(addr, false)}'",
                    0x04 => Memory.ReadValue<sbyte>(addr, false).ToString(),
                    0x05 => $"0x{Memory.ReadValue<byte>(addr, false):X2}",
                    0x06 => Memory.ReadValue<short>(addr, false).ToString(),
                    0x07 => $"0x{Memory.ReadValue<ushort>(addr, false):X4}",
                    0x08 => Memory.ReadValue<int>(addr, false).ToString(),
                    0x09 => $"0x{Memory.ReadValue<uint>(addr, false):X}",
                    0x0A => Memory.ReadValue<long>(addr, false).ToString(),
                    0x0B => $"0x{Memory.ReadValue<ulong>(addr, false):X}",
                    0x0C => Memory.ReadValue<float>(addr, false).ToString("G6"),
                    0x0D => Memory.ReadValue<double>(addr, false).ToString("G6"),
                    0x0E => ReadStringFieldValue(addr),
                    0x12 or 0x15 or 0x1D or 0x14 => ReadPointerFieldValue(addr),
                    0x18 => $"0x{Memory.ReadValue<ulong>(addr, false):X}",
                    _ => ReadPointerOrValueFieldValue(addr),
                };
            }
            catch
            {
                return "<read failed>";
            }
        }

        private static string ReadStringFieldValue(ulong addr)
        {
            var ptr = ReadPtr(addr);
            if (ptr == 0) return "null";
            if (!ArenaUtils.IsValidVirtualAddress(ptr)) return $"<bad ptr 0x{ptr:X}>";
            try
            {
                var s = Memory.ReadUnityString(ptr, 128, false);
                return $"\"{s}\"";
            }
            catch
            {
                return $"0x{ptr:X}";
            }
        }

        private static string ReadPointerFieldValue(ulong addr)
        {
            var ptr = ReadPtr(addr);
            return ptr == 0 ? "null" : $"0x{ptr:X}";
        }

        private static string ReadPointerOrValueFieldValue(ulong addr)
        {
            var raw = Memory.ReadValue<ulong>(addr, false);
            if (raw <= uint.MaxValue)
                return $"{(int)(uint)raw}";
            return $"0x{raw:X}";
        }

        #endregion

        // â”€â”€ Reflection helper â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static bool TrySetField(Type type, string fieldName, object value, BindingFlags bf)
        {
            var fi = type.GetField(fieldName, bf);
            if (fi is null)
            {
                Log.WriteLine($"[Il2CppDumper] WARN: field '{fieldName}' not found on '{type.Name}'.");
                return false;
            }
            if (fi.IsLiteral) return true;
            try
            {
                var target = fi.FieldType;
                object converted;
                if      (target == typeof(uint))   converted = Convert.ToUInt32(value);
                else if (target == typeof(ulong))  converted = Convert.ToUInt64(value);
                else if (target == typeof(int))    converted = Convert.ToInt32(value);
                else if (target == typeof(uint[]))
                {
                    var arr = (uint[]?)fi.GetValue(null);
                    if (arr is { Length: > 0 }) { arr[0] = Convert.ToUInt32(value); return true; }
                    return false;
                }
                else
                {
                    Log.WriteLine($"[Il2CppDumper] WARN: unsupported field type '{target}' for '{type.Name}.{fieldName}'.");
                    return false;
                }
                fi.SetValue(null, converted);
                return true;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[Il2CppDumper] ERROR: Failed to set '{type.Name}.{fieldName}': {ex.Message}");
                return false;
            }
        }


        // â”€â”€ Memory helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static List<(string Name, string Namespace, ulong KlassPtr, int Index)> ReadAllClassesFromTable(ulong tablePtr)
        {
            var result = new List<(string, string, ulong, int)>(4096);
            const int chunkSize = 4096;
            var allPtrs = new List<ulong>(MaxClasses);

            for (int offset = 0; offset < MaxClasses; offset += chunkSize)
            {
                int toRead = Math.Min(chunkSize, MaxClasses - offset);
                ulong[] chunk;
                try { chunk = Memory.ReadArray<ulong>(tablePtr + (ulong)offset * 8, toRead, false); }
                catch (Exception ex) { if (allPtrs.Count == 0) Log.WriteLine($"[Il2CppDumper] ReadArray failed: {ex.Message}"); break; }

                bool hasValid = false;
                for (int i = 0; i < chunk.Length; i++)
                    if (ArenaUtils.IsValidVirtualAddress(chunk[i])) hasValid = true;
                allPtrs.AddRange(chunk);
                if (!hasValid) break;
            }

            if (allPtrs.Count == 0) return result;

            var validIndices = new List<int>(allPtrs.Count / 2);
            for (int i = 0; i < allPtrs.Count; i++)
                if (ArenaUtils.IsValidVirtualAddress(allPtrs[i])) validIndices.Add(i);

            if (validIndices.Count == 0) return result;

            var ptrEntries  = new DMA.ScatterAPI.ScatterReadEntry<ClassNamePtrs>[validIndices.Count];
            var scatterBatch = new DMA.ScatterAPI.IScatterEntry[validIndices.Count];
            for (int j = 0; j < validIndices.Count; j++)
            {
                ptrEntries[j]  = DMA.ScatterAPI.ScatterReadEntry<ClassNamePtrs>.Get(allPtrs[validIndices[j]] + K_Name, 0);
                scatterBatch[j] = ptrEntries[j];
            }
            Memory.ReadScatter(scatterBatch, false);

            var nameEntries = new DMA.ScatterAPI.ScatterReadEntry<UTF8String>[validIndices.Count];
            var nsEntries   = new DMA.ScatterAPI.ScatterReadEntry<UTF8String>[validIndices.Count];
            var stringBatch = new List<DMA.ScatterAPI.IScatterEntry>(validIndices.Count * 2);
            for (int j = 0; j < validIndices.Count; j++)
            {
                if (ptrEntries[j].IsFailed) continue;
                ref var p = ref ptrEntries[j].Result;
                if (ArenaUtils.IsValidVirtualAddress(p.NamePtr))
                {
                    nameEntries[j] = DMA.ScatterAPI.ScatterReadEntry<UTF8String>.Get(p.NamePtr, MaxNameLen);
                    stringBatch.Add(nameEntries[j]);
                }
                if (ArenaUtils.IsValidVirtualAddress(p.NamespacePtr))
                {
                    nsEntries[j] = DMA.ScatterAPI.ScatterReadEntry<UTF8String>.Get(p.NamespacePtr, MaxNameLen);
                    stringBatch.Add(nsEntries[j]);
                }
            }
            Memory.ReadScatter([.. stringBatch], false);

            for (int j = 0; j < validIndices.Count; j++)
            {
                int i = validIndices[j];
                string? name = nameEntries[j] is not null && !nameEntries[j].IsFailed
                    ? (string?)(UTF8String?)nameEntries[j].Result : null;
                if (string.IsNullOrEmpty(name)) continue;
                string? ns = nsEntries[j] is not null && !nsEntries[j].IsFailed
                    ? (string?)(UTF8String?)nsEntries[j].Result : string.Empty;
                result.Add((name, ns ?? string.Empty, allPtrs[i], i));
            }

            return result;
        }

        private static Dictionary<string, int> ReadClassFields(ulong klassPtr)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            var fieldCount = Memory.ReadValue<ushort>(klassPtr + K_FieldCount, false);
            if (fieldCount == 0 || fieldCount > 4096) return result;
            var fieldsBase = ReadPtr(klassPtr + K_Fields);
            if (!ArenaUtils.IsValidVirtualAddress(fieldsBase)) return result;
            RawFieldInfo[] rawFields;
            try { rawFields = Memory.ReadArray<RawFieldInfo>(fieldsBase, fieldCount, false); }
            catch { return result; }

            var nameEntries = new DMA.ScatterAPI.ScatterReadEntry<UTF8String>[rawFields.Length];
            var scatter = new List<DMA.ScatterAPI.IScatterEntry>(rawFields.Length);
            for (int i = 0; i < rawFields.Length; i++)
            {
                if (ArenaUtils.IsValidVirtualAddress(rawFields[i].NamePtr))
                {
                    nameEntries[i] = DMA.ScatterAPI.ScatterReadEntry<UTF8String>.Get(rawFields[i].NamePtr, MaxNameLen);
                    scatter.Add(nameEntries[i]);
                }
            }
            if (scatter.Count > 0) Memory.ReadScatter([.. scatter], false);

            for (int i = 0; i < rawFields.Length; i++)
            {
                string? name = nameEntries[i] is not null && !nameEntries[i].IsFailed
                    ? (string?)(UTF8String?)nameEntries[i].Result : null;
                if (string.IsNullOrEmpty(name)) continue;
                result.TryAdd(name, rawFields[i].Offset);
            }
            return result;
        }

        private static Dictionary<string, ulong> ReadClassMethods(ulong klassPtr, ulong gaBase)
        {
            var result = new Dictionary<string, ulong>(StringComparer.Ordinal);
            var methodCount = Memory.ReadValue<ushort>(klassPtr + K_MethodCount, false);
            if (methodCount == 0 || methodCount > 4096) return result;
            var methodsBase = ReadPtr(klassPtr + K_Methods);
            if (!ArenaUtils.IsValidVirtualAddress(methodsBase)) return result;
            ulong[] methodPtrs;
            try { methodPtrs = Memory.ReadArray<ulong>(methodsBase, methodCount, false); }
            catch { return result; }

            // Round 1: scatter-read RawMethodInfo (0x20 bytes each) for all method pointers
            var infoEntries = new DMA.ScatterAPI.ScatterReadEntry<RawMethodInfo>[methodPtrs.Length];
            var scatter1 = new List<DMA.ScatterAPI.IScatterEntry>(methodPtrs.Length);
            for (int i = 0; i < methodPtrs.Length; i++)
            {
                if (!ArenaUtils.IsValidVirtualAddress(methodPtrs[i])) continue;
                infoEntries[i] = DMA.ScatterAPI.ScatterReadEntry<RawMethodInfo>.Get(methodPtrs[i], 0);
                scatter1.Add(infoEntries[i]);
            }
            if (scatter1.Count > 0) Memory.ReadScatter([.. scatter1], false);

            // Round 2: scatter-read name strings using NamePtr from RawMethodInfo[0x18]
            var nameEntries = new DMA.ScatterAPI.ScatterReadEntry<UTF8String>[methodPtrs.Length];
            var scatter2 = new List<DMA.ScatterAPI.IScatterEntry>(methodPtrs.Length);
            for (int i = 0; i < methodPtrs.Length; i++)
            {
                if (infoEntries[i] is null || infoEntries[i].IsFailed) continue;
                ref var info = ref infoEntries[i].Result;
                if (!ArenaUtils.IsValidVirtualAddress(info.MethodPointer) || info.MethodPointer < gaBase) continue;
                if (!ArenaUtils.IsValidVirtualAddress(info.NamePtr)) continue;
                nameEntries[i] = DMA.ScatterAPI.ScatterReadEntry<UTF8String>.Get(info.NamePtr, MaxNameLen);
                scatter2.Add(nameEntries[i]);
            }
            if (scatter2.Count > 0) Memory.ReadScatter([.. scatter2], false);

            for (int i = 0; i < methodPtrs.Length; i++)
            {
                if (nameEntries[i] is null || nameEntries[i].IsFailed) continue;
                if (infoEntries[i] is null || infoEntries[i].IsFailed) continue;
                string? name = (string?)(UTF8String)nameEntries[i].Result;
                if (string.IsNullOrEmpty(name)) continue;
                result.TryAdd(name, infoEntries[i].Result.MethodPointer - gaBase);
            }
            return result;
        }

        // â”€â”€ String / pointer helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static string? FlipBackingFieldConvention(string name)
        {
            const string suffix = "k__BackingField";
            if (!name.EndsWith(suffix, StringComparison.Ordinal)) return null;
            if (name.Length > suffix.Length + 2 && name[0] == '<')
            {
                var inner = name[1..name.IndexOf('>')];
                return $"_{inner}_{suffix}";
            }
            if (name.Length > suffix.Length + 2 && name[0] == '_')
            {
                var inner = name[1..^suffix.Length];
                if (inner.EndsWith('_')) inner = inner[..^1];
                return $"<{inner}>{suffix}";
            }
            return null;
        }

        private static ulong ReadPtr(ulong addr)
        {
            if (!ArenaUtils.IsValidVirtualAddress(addr)) return 0;
            try { return Memory.ReadValue<ulong>(addr, false); }
            catch { return 0; }
        }

        private static string? ReadStr(ulong addr)
        {
            if (!ArenaUtils.IsValidVirtualAddress(addr)) return null;
            try { return Memory.ReadString(addr, MaxNameLen, false); }
            catch { return null; }
        }

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var sb = new char[name.Length];
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                sb[i] = char.IsLetterOrDigit(c) || c == '_' ? c : '_';
            }
            return new string(sb);
        }
    }
}
