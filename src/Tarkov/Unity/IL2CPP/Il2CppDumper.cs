using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Misc;
using SDK;

namespace eft_dma_radar.Tarkov.Unity.IL2CPP
{
    public static partial class Il2CppDumper
    {
        // ── IL2CPP struct field offsets ──────────────────────────────────────────
        private const uint K_Name        = 0x10;   // char*    Il2CppClass::name
        private const uint K_Fields      = 0x80;   // FieldInfo*  (direct array)
        private const uint K_Methods     = 0x98;   // MethodInfo** (array of pointers)
        private const uint K_MethodCount = 0x120;  // uint16
        private const uint K_FieldCount  = 0x124;  // uint16

        private const int  MaxClasses    = 80_000;
        private const int  MaxNameLen    = 256;

        // ── Scatter-read raw structs ─────────────────────────────────────────────

        /// <summary>
        /// Contiguous name + namespace pointers read from Il2CppClass at offset 0x10.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct ClassNamePtrs
        {
            public ulong NamePtr;      // 0x10  char* name
            public ulong NamespacePtr; // 0x18  char* namespaze
        }

        /// <summary>
        /// Raw FieldInfo entry (0x20 bytes stride). Only the fields we need are mapped.
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Size = 0x20)]
        private struct RawFieldInfo
        {
            [FieldOffset(0x00)] public ulong NamePtr; // char* name
            [FieldOffset(0x18)] public int   Offset;  // int32 offset (signed!)
        }

        /// <summary>
        /// Raw MethodInfo header. We read 0x20 bytes starting at the MethodInfo address
        /// to capture the method pointer (0x00) and name pointer (0x18).
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Size = 0x20)]
        private struct RawMethodInfo
        {
            [FieldOffset(0x00)] public ulong MethodPointer; // void* methodPointer
            [FieldOffset(0x18)] public ulong NamePtr;       // char* name
        }

        // ── Run-once guard ────────────────────────────────────────────────────────

        /// <summary>
        /// Set to <c>true</c> after the first successful dump (live or from cache).
        /// Prevents re-running the expensive type-table scan on subsequent game
        /// restarts within the same process lifetime — the resolved offsets are
        /// already in memory and the TypeInfoTable may no longer be readable.
        /// </summary>
        private static volatile bool _dumped;

        // ── Entry point ──────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves IL2CPP offsets at runtime and applies them to
        /// <see cref="Offsets"/> via reflection. Hardcoded defaults in SDK.cs
        /// serve as fallback for any field that cannot be resolved.
        /// 
        /// Runs only once per process lifetime: after a successful dump the
        /// results are persisted to <c>il2cpp_offsets.json</c> next to the
        /// executable.  On subsequent calls (e.g. game restarts) the cache is
        /// loaded instead of re-reading the TypeInfoTable, which may no longer
        /// be accessible after the first ~10 minutes in-game.
        /// </summary>
        /// <summary>
        /// Forces a full live re-dump, bypassing the run-once guard and cache.
        /// Deletes the existing cache file so offsets are read fresh from memory.
        /// </summary>
        public static void ForceRedump()
        {
            _dumped = false;
            if (File.Exists(CacheFilePath))
                File.Delete(CacheFilePath);
            XMLogging.WriteLine("[Il2CppDumper] Force re-dump requested — cache cleared.");
            Dump();
        }

        public static void Dump()
        {
            if (_dumped)
            {
                XMLogging.WriteLine("[Il2CppDumper] Already dumped this session — skipping.");
                return;
            }

            XMLogging.WriteLine("[Il2CppDumper] Dump starting...");

            var gaBase = Memory.GameAssemblyBase;
            if (gaBase == 0)
            {
                XMLogging.WriteLine("[Il2CppDumper] ERROR: GameAssemblyBase is 0 — game not ready.");
                NotificationsShared.Error("IL2CPP dump failed: GameAssembly not found.");
                return;
            }

            // Dynamically resolve TypeInfoTableRva via sig scan (falls back to hardcoded).
            // We must do this even for the cache path so we have the RVA fingerprint
            // needed to validate whether the cache matches the current game build.
            if (!ResolveTypeInfoTableRva(gaBase))
            {
                XMLogging.WriteLine("[Il2CppDumper] ABORT: TypeInfoTable resolution failed — cannot dump offsets.");
                NotificationsShared.Error("IL2CPP dump failed: TypeInfoTable not found. Offsets may be stale.");
                return;
            }

            // ── Fast path: load from cache ───────────────────────────────────────
            // If the cache was written against the same TypeInfoTableRva (i.e. the
            // same game binary), skip the expensive live memory read entirely.
            if (TryLoadCache(Offsets.Special.TypeInfoTableRva))
            {
                _dumped = true;
                XMLogging.WriteLine("[Il2CppDumper] Offsets restored from cache — live dump skipped.");
                return;
            }

            // ── Live path: read TypeInfoTable from memory ────────────────────────

            // Resolve the type-info table pointer once — used by both paths.
            ulong tablePtr;
            try { tablePtr = Memory.ReadPtr(gaBase + Offsets.Special.TypeInfoTableRva, false); }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Il2CppDumper] ReadPtr(TypeInfoTableRva) failed: {ex.Message}");
                return;
            }

            if (!tablePtr.IsValidVirtualAddress())
            {
                XMLogging.WriteLine("[Il2CppDumper] TypeInfoTable pointer is invalid.");
                return;
            }

            // Scan the full type table — needed for name lookups AND TypeIndex resolution.
            // Retry up to 10 times (with 1s delay) for transient DMA failures during loading.
            const int MinExpectedClasses = 1_000;
            const int maxRetries = 10;
            List<(string Name, string Namespace, ulong KlassPtr, int Index)> classes = [];

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                classes = ReadAllClassesFromTable(tablePtr);

                if (classes.Count >= MinExpectedClasses)
                    break;

                if (attempt < maxRetries)
                {
                    XMLogging.WriteLine($"[Il2CppDumper] Only {classes.Count} classes found (expected ≥{MinExpectedClasses}), retrying... ({attempt}/{maxRetries})");
                    Thread.Sleep(1000);
                }
            }

            // Sanity gate: a healthy IL2CPP binary has tens of thousands of classes.
            // If we found very few, the table pointer is likely stale or corrupt.
            if (classes.Count < MinExpectedClasses)
            {
                XMLogging.WriteLine($"[Il2CppDumper] ABORT: Only {classes.Count} classes found (expected ≥{MinExpectedClasses}) — TypeInfoTable likely corrupt or stale.");
                return;
            }

            var nameLookup  = new Dictionary<string, ulong>(classes.Count * 2, StringComparer.Ordinal);
            var nameToIndex = new Dictionary<string, int>(classes.Count * 2, StringComparer.Ordinal);

            // Dedup numbering: when multiple classes share the same sanitized base name,
            // the first is keyed as "World", the second as "World_2", third as "World_3", etc.
            // This matches the C++ AppSDK naming convention used by the schema.
            var baseNameSeen = new Dictionary<string, int>(classes.Count, StringComparer.Ordinal);

            foreach (var (name, _, ptr, idx) in classes)
            {
                var san = SanitizeName(name);

                // Index by raw name and sanitized name (first-wins via TryAdd).
                nameLookup.TryAdd(name, ptr);
                nameToIndex.TryAdd(name, idx);
                if (san != name)
                {
                    nameLookup.TryAdd(san, ptr);
                    nameToIndex.TryAdd(san, idx);
                }

                // Dedup numbering by sanitized base name:
                // First "World" → key "World", second → "World_2", third → "World_3", etc.
                if (baseNameSeen.TryGetValue(san, out int seen))
                {
                    int next = seen + 1;
                    baseNameSeen[san] = next;
                    var dedupKey = $"{san}_{next}";
                    nameLookup.TryAdd(dedupKey, ptr);
                    nameToIndex.TryAdd(dedupKey, idx);
                }
                else
                {
                    baseNameSeen[san] = 1;
                }
            }

            // Dynamically resolve TypeIndex values for known singleton classes.
            ResolveTypeIndices(nameToIndex);

            // Build schema AFTER TypeIndex resolution so it picks up updated values.
            var schema = BuildSchema();

            // Reflection: locate nested types inside Offsets once.
            var offsetsType = typeof(Offsets);
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

                    if (!klassPtr.IsValidVirtualAddress())
                    {
                        XMLogging.WriteLine($"[Il2CppDumper] SKIP '{sc.CsName}': TypeIndex={sc.TypeIndex.Value} resolved to invalid pointer.");
                        classesSkipped++;
                        continue;
                    }
                }
                else
                {
                    if (!nameLookup.TryGetValue(sc.Il2CppName, out klassPtr))
                    {
                        XMLogging.WriteLine($"[Il2CppDumper] SKIP '{sc.Il2CppName}': not found in type table.");
                        classesSkipped++;
                        continue;
                    }
                    resolvedVia = $"name=\"{sc.Il2CppName}\"";
                }

                // Find the target struct in Offsets via reflection.
                var nestedType = offsetsType.GetNestedType(sc.CsName, BindingFlags.Public | BindingFlags.NonPublic);
                if (nestedType is null)
                {
                    XMLogging.WriteLine($"[Il2CppDumper] WARN: struct Offsets.{sc.CsName} not found via reflection — skipping.");
                    classesSkipped++;
                    continue;
                }

                var fieldMap  = ReadClassFields(klassPtr);
                var methodMap = sc.Fields.Any(sf => sf.Kind == FieldKind.MethodRva)
                    ? ReadClassMethods(klassPtr, gaBase)
                    : null;

                foreach (var sf in sc.Fields)
                {
                    if (sf.Kind == FieldKind.MethodRva)
                    {
                        var methodName = sf.Il2CppName.EndsWith("_RVA", StringComparison.Ordinal)
                            ? sf.Il2CppName[..^4]
                            : sf.Il2CppName;

                        if (methodMap is not null && methodMap.TryGetValue(methodName, out var rva))
                        {
                            if (TrySetField(nestedType, sf.CsName, rva, bf))
                                updated++;
                            else
                                fallback++;
                        }
                        else
                        {
                            XMLogging.WriteLine($"[Il2CppDumper] WARN: method '{methodName}' not found in '{sc.CsName}' — using fallback.");
                            fallback++;
                        }
                    }
                    else
                    {
                        if (!fieldMap.TryGetValue(sf.Il2CppName, out var offset))
                        {
                            var alt = FlipBackingFieldConvention(sf.Il2CppName);
                            if (alt is null || !fieldMap.TryGetValue(alt, out offset))
                            {
                                XMLogging.WriteLine($"[Il2CppDumper] WARN: field '{sf.Il2CppName}' not found in '{sc.CsName}' — using fallback.");
                                fallback++;
                                continue;
                            }
                        }

                        // FieldInfo::offset is signed. Positive → uint, negative → int.
                        object value = offset >= 0 ? (object)(uint)offset : (object)offset;
                        if (TrySetField(nestedType, sf.CsName, value, bf))
                            updated++;
                        else
                            fallback++;
                    }
                }
            }

            DebugDumpResolverState(classes.Count, updated, fallback, classesSkipped);
            XMLogging.WriteLine($"[Il2CppDumper] Done. {updated} offsets updated, {fallback} fallback, {classesSkipped} skipped.");

            if (fallback > 0 || classesSkipped > 0)
                NotificationsShared.Warning($"IL2CPP dump partial: {fallback} fallback offset(s), {classesSkipped} class(es) skipped. Check logs.");

            // Persist to cache so future game restarts (where the TypeInfoTable may
            // no longer be readable) can skip the live dump entirely.
            _dumped = true;
            SaveCache();
        }

        // ── Reflection helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Attempts to set a static field on a type via reflection.
        /// Handles uint/ulong/int type conversion automatically.
        /// For const fields (IsLiteral), skips silently (cannot set at runtime).
        /// For uint[] fields (deref chains), updates only the first element.
        /// </summary>
        private static bool TrySetField(Type type, string fieldName, object value, BindingFlags bf)
        {
            var fi = type.GetField(fieldName, bf);
            if (fi is null)
            {
                XMLogging.WriteLine($"[Il2CppDumper] WARN: field '{fieldName}' not found on '{type.Name}' via reflection.");
                return false;
            }

            // const (literal) fields cannot be changed at runtime — skip silently.
            if (fi.IsLiteral)
                return true;

            try
            {
                // Convert the dumped value to the declared field type.
                var target = fi.FieldType;
                object converted;

                if (target == typeof(uint))
                    converted = Convert.ToUInt32(value);
                else if (target == typeof(ulong))
                    converted = Convert.ToUInt64(value);
                else if (target == typeof(int))
                    converted = Convert.ToInt32(value);
                else if (target == typeof(uint[]))
                {
                    // Deref-chain field: update only the first element with the dumped offset.
                    var arr = (uint[])fi.GetValue(null);
                    if (arr is not null && arr.Length > 0)
                    {
                        arr[0] = Convert.ToUInt32(value);
                        return true; // array is reference type — already mutated in place
                    }
                    return false;
                }
                else
                {
                    XMLogging.WriteLine($"[Il2CppDumper] WARN: unsupported field type '{target}' for '{type.Name}.{fieldName}'.");
                    return false;
                }

                fi.SetValue(null, converted);
                return true;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Il2CppDumper] ERROR: Failed to set '{type.Name}.{fieldName}': {ex.Message}");
                return false;
            }
        }

        // ── Memory helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Reads all IL2CppClass* entries from a pre-resolved type-info table pointer.
        /// Uses scatter reads to batch all DMA operations (2 scatter rounds instead of ~4 reads per class).
        /// Reads the pointer array in chunks to avoid oversized DMA reads during early loading.
        /// </summary>
        private static List<(string Name, string Namespace, ulong KlassPtr, int Index)> ReadAllClassesFromTable(ulong tablePtr)
        {
            var result = new List<(string, string, ulong, int)>(4096);

            // Step 1: Read all class pointers in chunks to handle partially-mapped memory.
            const int chunkSize = 4096;
            var allPtrs = new List<ulong>(MaxClasses);

            for (int offset = 0; offset < MaxClasses; offset += chunkSize)
            {
                int toRead = Math.Min(chunkSize, MaxClasses - offset);
                ulong[] chunk;
                try { chunk = Memory.ReadArray<ulong>(tablePtr + (ulong)offset * 8, toRead, false); }
                catch (Exception ex)
                {
                    if (allPtrs.Count == 0)
                        XMLogging.WriteLine($"[Il2CppDumper] ReadArray failed: {ex.Message}");
                    break; // DMA failure — use whatever we've read so far
                }

                // Check if this chunk has any valid entries.
                bool hasValid = false;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (chunk[i].IsValidVirtualAddress())
                        hasValid = true;
                }

                allPtrs.AddRange(chunk);

                // If this chunk had no valid entries, we've passed the end of the table.
                if (!hasValid)
                    break;
            }

            if (allPtrs.Count == 0)
                return result;

            var ptrs = allPtrs;

            // Collect indices of valid class pointers.
            var validIndices = new List<int>(ptrs.Count / 2);
            for (int i = 0; i < ptrs.Count; i++)
            {
                if (ptrs[i].IsValidVirtualAddress())
                    validIndices.Add(i);
            }

            if (validIndices.Count == 0)
                return result;

            // Step 2: Scatter read name_ptr + namespace_ptr for every valid class (one 16-byte read each).
            var ptrEntries = new ScatterReadEntry<ClassNamePtrs>[validIndices.Count];
            var scatterBatch = new IScatterEntry[validIndices.Count];

            for (int j = 0; j < validIndices.Count; j++)
            {
                ptrEntries[j] = ScatterReadEntry<ClassNamePtrs>.Get(ptrs[validIndices[j]] + K_Name, 0);
                scatterBatch[j] = ptrEntries[j];
            }

            Memory.ReadScatter(scatterBatch, false);

            // Step 3: Scatter read all name and namespace strings in one batch.
            var nameEntries = new ScatterReadEntry<UTF8String>[validIndices.Count];
            var nsEntries   = new ScatterReadEntry<UTF8String>[validIndices.Count];
            var stringBatch = new List<IScatterEntry>(validIndices.Count * 2);

            for (int j = 0; j < validIndices.Count; j++)
            {
                if (ptrEntries[j].IsFailed)
                    continue;

                ref var p = ref ptrEntries[j].Result;

                if (p.NamePtr.IsValidVirtualAddress())
                {
                    nameEntries[j] = ScatterReadEntry<UTF8String>.Get(p.NamePtr, MaxNameLen);
                    stringBatch.Add(nameEntries[j]);
                }

                if (p.NamespacePtr.IsValidVirtualAddress())
                {
                    nsEntries[j] = ScatterReadEntry<UTF8String>.Get(p.NamespacePtr, MaxNameLen);
                    stringBatch.Add(nsEntries[j]);
                }
            }

            Memory.ReadScatter(stringBatch.ToArray(), false);

            // Step 4: Build results.
            for (int j = 0; j < validIndices.Count; j++)
            {
                int i = validIndices[j];

                string name = nameEntries[j] is not null && !nameEntries[j].IsFailed
                    ? (string)(UTF8String)nameEntries[j].Result
                    : null;

                if (string.IsNullOrEmpty(name))
                    continue;

                string ns = nsEntries[j] is not null && !nsEntries[j].IsFailed
                    ? (string)(UTF8String)nsEntries[j].Result
                    : string.Empty;

                result.Add((name, ns ?? string.Empty, ptrs[i], i));
            }

            return result;
        }

        private static Dictionary<string, int> ReadClassFields(ulong klassPtr)
        {
            var result     = new Dictionary<string, int>(StringComparer.Ordinal);
            var fieldCount = Memory.ReadValue<ushort>(klassPtr + K_FieldCount, false);
            if (fieldCount == 0 || fieldCount > 4096) return result;

            var fieldsBase = ReadPtr(klassPtr + K_Fields);
            if (!fieldsBase.IsValidVirtualAddress()) return result;

            // Bulk read the entire field array in one DMA operation.
            RawFieldInfo[] rawFields;
            try { rawFields = Memory.ReadArray<RawFieldInfo>(fieldsBase, fieldCount, false); }
            catch { return result; }

            // Scatter read all field name strings in one batch.
            var nameEntries = new ScatterReadEntry<UTF8String>[rawFields.Length];
            var scatter = new List<IScatterEntry>(rawFields.Length);

            for (int i = 0; i < rawFields.Length; i++)
            {
                if (rawFields[i].NamePtr.IsValidVirtualAddress())
                {
                    nameEntries[i] = ScatterReadEntry<UTF8String>.Get(rawFields[i].NamePtr, MaxNameLen);
                    scatter.Add(nameEntries[i]);
                }
            }

            if (scatter.Count > 0)
                Memory.ReadScatter(scatter.ToArray(), false);

            // Build results.
            for (int i = 0; i < rawFields.Length; i++)
            {
                string name = nameEntries[i] is not null && !nameEntries[i].IsFailed
                    ? (string)(UTF8String)nameEntries[i].Result
                    : null;

                if (string.IsNullOrEmpty(name)) continue;
                result.TryAdd(name, rawFields[i].Offset);
            }

            return result;
        }

        private static Dictionary<string, ulong> ReadClassMethods(ulong klassPtr, ulong gaBase)
        {
            var result      = new Dictionary<string, ulong>(StringComparer.Ordinal);
            var methodCount = Memory.ReadValue<ushort>(klassPtr + K_MethodCount, false);
            if (methodCount == 0 || methodCount > 4096) return result;

            var methodsBase = ReadPtr(klassPtr + K_Methods);
            if (!methodsBase.IsValidVirtualAddress()) return result;

            ulong[] methodPtrs;
            try { methodPtrs = Memory.ReadArray<ulong>(methodsBase, methodCount, false); }
            catch { return result; }

            // Scatter read MethodPointer + NamePtr for all methods in one batch.
            var infoEntries = new ScatterReadEntry<RawMethodInfo>[methodPtrs.Length];
            var scatter1 = new List<IScatterEntry>(methodPtrs.Length);

            for (int i = 0; i < methodPtrs.Length; i++)
            {
                if (!methodPtrs[i].IsValidVirtualAddress()) continue;
                infoEntries[i] = ScatterReadEntry<RawMethodInfo>.Get(methodPtrs[i], 0);
                scatter1.Add(infoEntries[i]);
            }

            if (scatter1.Count > 0)
                Memory.ReadScatter(scatter1.ToArray(), false);

            // Scatter read all method name strings in one batch.
            var nameEntries = new ScatterReadEntry<UTF8String>[methodPtrs.Length];
            var scatter2 = new List<IScatterEntry>(methodPtrs.Length);

            for (int i = 0; i < methodPtrs.Length; i++)
            {
                if (infoEntries[i] is null || infoEntries[i].IsFailed) continue;

                ref var info = ref infoEntries[i].Result;
                if (!info.MethodPointer.IsValidVirtualAddress() || info.MethodPointer < gaBase) continue;
                if (!info.NamePtr.IsValidVirtualAddress()) continue;

                nameEntries[i] = ScatterReadEntry<UTF8String>.Get(info.NamePtr, MaxNameLen);
                scatter2.Add(nameEntries[i]);
            }

            if (scatter2.Count > 0)
                Memory.ReadScatter(scatter2.ToArray(), false);

            // Build results.
            for (int i = 0; i < methodPtrs.Length; i++)
            {
                if (nameEntries[i] is null || nameEntries[i].IsFailed) continue;
                if (infoEntries[i] is null || infoEntries[i].IsFailed) continue;

                string name = (string)(UTF8String)nameEntries[i].Result;
                if (string.IsNullOrEmpty(name)) continue;

                var rva = infoEntries[i].Result.MethodPointer - gaBase;
                result.TryAdd(name, rva);
            }

            return result;
        }

        // ── String / pointer helpers ─────────────────────────────────────────────

        /// <summary>
        /// Converts between the two IL2CPP backing field naming conventions:
        ///   "&lt;Name&gt;k__BackingField"  ↔  "_Name_k__BackingField"
        /// Returns null if the input is not a backing field name.
        /// </summary>
        private static string FlipBackingFieldConvention(string name)
        {
            const string suffix = "k__BackingField";
            if (!name.EndsWith(suffix, StringComparison.Ordinal))
                return null;

            if (name.Length > suffix.Length + 2 && name[0] == '<')
            {
                // <Name>k__BackingField → _Name_k__BackingField
                var inner = name[1..name.IndexOf('>')];
                return $"_{inner}_{suffix}";
            }

            if (name.Length > suffix.Length + 2 && name[0] == '_')
            {
                // _Name_k__BackingField → <Name>k__BackingField
                var inner = name[1..^suffix.Length];
                if (inner.EndsWith('_'))
                    inner = inner[..^1];
                return $"<{inner}>{suffix}";
            }

            return null;
        }

        private static ulong ReadPtr(ulong addr)
        {
            if (!addr.IsValidVirtualAddress()) return 0;
            try { return Memory.ReadValue<ulong>(addr, false); }
            catch { return 0; }
        }

        private static string ReadStr(ulong addr)
        {
            if (!addr.IsValidVirtualAddress()) return null;
            try { return Memory.ReadString(addr, MaxNameLen, false); }
            catch { return null; }
        }

        /// <summary>
        /// Replaces non-alphanumeric/non-underscore characters with '_'.
        /// e.g. "World`2" → "World_2", "SlotView`2" → "SlotView_2"
        /// </summary>
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
