#pragma warning disable IDE0130
using UTF8String = eft_dma_radar.Silk.Misc.UTF8String;
using System.IO;

namespace eft_dma_radar.Silk.Tarkov.Unity.IL2CPP
{
    public static partial class Il2CppDumper
    {
        // ── Cache file path ──────────────────────────────────────────────────────

        private static readonly string CacheFilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "eft-dma-radar-silk", "il2cpp_offsets.json");

        // ── Serialization model ──────────────────────────────────────────────────

        /// <summary>
        /// Root cache document. Versioned by the GameAssembly base address so that
        /// a cache written against one build of the game is automatically discarded
        /// when the game updates (base address changes with ASLR per-boot, but the
        /// RVA embedded in the cache is what matters — we use the resolved
        /// TypeInfoTableRva as the version fingerprint since it is stable within a
        /// single game build and changes when the binary changes).
        /// </summary>
        /// <summary>
        /// Bump this whenever the schema's <em>meaning</em> changes in a way that
        /// makes previously-cached values wrong (e.g. switching which IL2CPP field
        /// a C# offset is sourced from). Old caches with a lower version are
        /// discarded, forcing a fresh live dump.
        /// </summary>
        private const int CacheSchemaVersion = 2;

        /// <summary>
        /// Radar assembly ModuleVersionId — changes on every rebuild. Used to invalidate
        /// caches automatically when a developer hardcodes a new offset in <see cref="Offsets"/>
        /// without the game (PE fingerprint) having changed. No need to bump
        /// <see cref="CacheSchemaVersion"/> manually.
        /// </summary>
        private static readonly Guid RadarAssemblyMvid =
            typeof(Il2CppDumper).Assembly.ManifestModule.ModuleVersionId;

        private sealed class OffsetCache
        {
            /// <summary>
            /// Schema version the cache was written with. See <see cref="CacheSchemaVersion"/>.
            /// Missing / older values cause the cache to be discarded.
            /// </summary>
            public int SchemaVersion { get; set; }

            /// <summary>
            /// Radar assembly MVID at the time the cache was written. Mismatch means
            /// the radar binary has been rebuilt (potentially with new hardcoded
            /// offsets) and the cache must be discarded.
            /// </summary>
            public Guid RadarAssemblyMvid { get; set; }

            /// <summary>
            /// <see cref="Offsets.Special.TypeInfoTableRva"/> at the time the cache
            /// was written. Used as a build-version fingerprint: if this no longer
            /// matches what sig-scan resolves, the cache is stale.
            /// </summary>
            public ulong TypeInfoTableRva { get; set; }

            /// <summary>
            /// PE header TimeDateStamp of GameAssembly.dll when the cache was written.
            /// Together with <see cref="GameAssemblySizeOfImage"/> this forms a cheap
            /// fingerprint that lets us skip the expensive TypeInfoTableRva sig scan
            /// when the game binary has not changed.
            /// </summary>
            public uint GameAssemblyTimestamp { get; set; }

            /// <summary>
            /// PE header SizeOfImage of GameAssembly.dll when the cache was written.
            /// </summary>
            public uint GameAssemblySizeOfImage { get; set; }

            /// <summary>
            /// All static offset fields from every nested struct inside
            /// <see cref="Offsets"/>, keyed as "StructName.FieldName".
            /// Values are stored as strings to handle both uint and ulong cleanly.
            /// </summary>
            public Dictionary<string, string> Fields { get; set; } = new();
        }

        // ── Persistence helpers ──────────────────────────────────────────────────

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>
        /// Serializes all resolved static offset fields from <see cref="Offsets"/>
        /// nested structs to <see cref="CacheFilePath"/>.
        /// Called once after a successful live dump.
        /// </summary>
        internal static void SaveCache()
        {
            try
            {
                var (timestamp, sizeOfImage) = Memory.ReadPeFingerprint(Memory.GameAssemblyBase);

                var cache = new OffsetCache
                {
                    SchemaVersion = CacheSchemaVersion,
                    RadarAssemblyMvid = RadarAssemblyMvid,
                    TypeInfoTableRva = Offsets.Special.TypeInfoTableRva,
                    GameAssemblyTimestamp = timestamp,
                    GameAssemblySizeOfImage = sizeOfImage,
                    Fields = CollectAllFields(),
                };

                var json = JsonSerializer.Serialize(cache, _jsonOpts);
                Directory.CreateDirectory(Path.GetDirectoryName(CacheFilePath)!);
                File.WriteAllText(CacheFilePath, json);
                Log.WriteLine($"[Il2CppDumper] Cache saved → {CacheFilePath}");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[Il2CppDumper] Cache save FAILED: {ex.Message}");
            }
        }

        /// <summary>
        /// Attempts to load a previously saved offset cache and apply it to
        /// <see cref="Offsets"/> via reflection.
        /// </summary>
        /// <param name="expectedRva">
        /// The TypeInfoTableRva resolved by sig-scan this session.
        /// If it does not match the cached value the cache is considered stale
        /// and is discarded.
        /// </param>
        /// <returns>
        /// <c>true</c> if the cache was loaded and applied successfully;
        /// <c>false</c> if it was absent, stale, or corrupt.
        /// </returns>
        internal static bool TryLoadCache(ulong expectedRva)
        {
            try
            {
                if (!File.Exists(CacheFilePath))
                {
                    Log.WriteLine("[Il2CppDumper] No cache file found — will perform live dump.");
                    return false;
                }

                var json = File.ReadAllText(CacheFilePath);
                var cache = JsonSerializer.Deserialize<OffsetCache>(json, _jsonOpts);

                if (cache is null || cache.Fields.Count == 0)
                {
                    Log.WriteLine("[Il2CppDumper] Cache file is empty or corrupt — will perform live dump.");
                    return false;
                }

                if (cache.RadarAssemblyMvid != RadarAssemblyMvid)
                {
                    Log.WriteLine($"[Il2CppDumper] Radar build changed (MVID {cache.RadarAssemblyMvid} → {RadarAssemblyMvid}) — will perform live dump.");
                    return false;
                }

                if (cache.TypeInfoTableRva != expectedRva)
                {
                    Log.WriteLine(
                        $"[Il2CppDumper] Cache RVA mismatch: cached=0x{cache.TypeInfoTableRva:X} " +
                        $"current=0x{expectedRva:X} — cache is stale, performing live dump.");
                    return false;
                }

                int applied = ApplyCachedFields(cache.Fields);
                Log.WriteLine($"[Il2CppDumper] Cache loaded — {applied}/{cache.Fields.Count} fields applied.");
                return applied > 0;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[Il2CppDumper] Cache load FAILED: {ex.Message} — will perform live dump.");
                return false;
            }
        }

        /// <summary>
        /// Fast-path cache loader that uses the GameAssembly.dll PE header fingerprint
        /// (TimeDateStamp + SizeOfImage) to validate the cache <em>before</em> the
        /// expensive TypeInfoTableRva sig scan.  When the game binary has not changed,
        /// this lets us restore all offsets in &lt;1 ms and skip the sig scan entirely.
        /// </summary>
        /// <returns>
        /// <c>true</c> if the cache was loaded and applied successfully;
        /// <c>false</c> if the PE fingerprint does not match, the cache is absent/corrupt, etc.
        /// </returns>
        internal static bool TryFastLoadCache(ulong gaBase)
        {
            try
            {
                if (!File.Exists(CacheFilePath))
                    return false;

                var (timestamp, sizeOfImage) = Memory.ReadPeFingerprint(gaBase);
                if (timestamp == 0 || sizeOfImage == 0)
                    return false;

                var json = File.ReadAllText(CacheFilePath);
                var cache = JsonSerializer.Deserialize<OffsetCache>(json, _jsonOpts);

                if (cache is null || cache.Fields.Count == 0)
                    return false;

                if (cache.RadarAssemblyMvid != RadarAssemblyMvid)
                {
                    Log.WriteLine($"[Il2CppDumper] Radar build changed (MVID {cache.RadarAssemblyMvid} → {RadarAssemblyMvid}) — will perform fresh dump.");
                    return false;
                }

                if (cache.SchemaVersion < CacheSchemaVersion)
                {
                    Log.WriteLine(
                        $"[Il2CppDumper] Fast cache schema outdated (cached={cache.SchemaVersion} current={CacheSchemaVersion}) — will perform fresh dump.");
                    return false;
                }

                // Old cache files won't have PE fields → values default to 0 → mismatch → fall through.
                if (cache.GameAssemblyTimestamp != timestamp || cache.GameAssemblySizeOfImage != sizeOfImage)
                {
                    Log.WriteLine("[Il2CppDumper] PE fingerprint mismatch (game updated?) — will perform fresh dump.");
                    return false;
                }

                // Restore the TypeInfoTableRva that was stored alongside the offsets
                // so downstream code doesn't need to sig-scan for it.
                if (cache.TypeInfoTableRva != 0)
                    Offsets.Special.TypeInfoTableRva = cache.TypeInfoTableRva;

                int applied = ApplyCachedFields(cache.Fields);
                Log.WriteLine($"[Il2CppDumper] Fast cache loaded (PE match) — {applied}/{cache.Fields.Count} fields applied.");
                return applied > 0;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[Il2CppDumper] Fast cache load failed: {ex.Message}");
                return false;
            }
        }

        // ── Reflection over Offsets ──────────────────────────────────────────────

        private const BindingFlags _bf = BindingFlags.Public | BindingFlags.Static;

        /// <summary>
        /// Walks every public static non-const field of every nested struct inside
        /// <see cref="Offsets"/> and returns them as "StructName.FieldName" → value string.
        /// Handles uint, ulong, int, and uint[] (stores first element for deref chains).
        /// </summary>
        private static Dictionary<string, string> CollectAllFields()
        {
            var result = new Dictionary<string, string>(256);
            var offsetsType = typeof(Offsets);

            foreach (var nested in offsetsType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (var fi in nested.GetFields(_bf))
                {
                    if (fi.IsLiteral) continue; // skip const fields

                    var raw = fi.GetValue(null);
                    if (raw is null) continue;

                    string? value = raw switch
                    {
                        uint[] arr => arr.Length > 0 ? arr[0].ToString() : null,
                        uint u => u.ToString(),
                        ulong ul => ul.ToString(),
                        int i => i.ToString(),
                        _ => null,
                    };

                    if (value is not null)
                        result[$"{nested.Name}.{fi.Name}"] = value;
                }
            }

            return result;
        }

        /// <summary>
        /// Applies a set of "StructName.FieldName" → value-string entries back onto
        /// the static fields of the corresponding nested structs inside <see cref="Offsets"/>.
        /// </summary>
        private static int ApplyCachedFields(Dictionary<string, string> fields)
        {
            var offsetsType = typeof(Offsets);
            int applied = 0;

            foreach (var (key, rawValue) in fields)
            {
                var dot = key.IndexOf('.');
                if (dot < 0) continue;

                var structName = key[..dot];
                var fieldName = key[(dot + 1)..];

                var nested = offsetsType.GetNestedType(structName, BindingFlags.Public | BindingFlags.NonPublic);
                if (nested is null) continue;

                var fi = nested.GetField(fieldName, _bf);
                if (fi is null || fi.IsLiteral) continue;

                try
                {
                    var target = fi.FieldType;

                    if (target == typeof(uint))
                    {
                        if (uint.TryParse(rawValue, out var v)) { fi.SetValue(null, v); applied++; }
                    }
                    else if (target == typeof(ulong))
                    {
                        if (ulong.TryParse(rawValue, out var v)) { fi.SetValue(null, v); applied++; }
                    }
                    else if (target == typeof(int))
                    {
                        if (int.TryParse(rawValue, out var v)) { fi.SetValue(null, v); applied++; }
                    }
                    else if (target == typeof(uint[]))
                    {
                        if (uint.TryParse(rawValue, out var v))
                        {
                            var arr = (uint[]?)fi.GetValue(null);
                            if (arr is { Length: > 0 }) { arr[0] = v; applied++; }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"[Il2CppDumper] Cache: failed to apply {key}: {ex.Message}");
                }
            }

            return applied;
        }
    }
}
