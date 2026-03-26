using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using eft_dma_radar.Common.Misc;
using SDK;

namespace eft_dma_radar.Tarkov.Unity.IL2CPP
{
    public static partial class Il2CppDumper
    {
        // ── Cache file path ──────────────────────────────────────────────────────

        private static readonly string CacheFilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "il2cpp_offsets.json");

        // ── Serialization model ──────────────────────────────────────────────────

        /// <summary>
        /// Root cache document. Versioned by the GameAssembly base address so that
        /// a cache written against one build of the game is automatically discarded
        /// when the game updates (base address changes with ASLR per-boot, but the
        /// RVA embedded in the cache is what matters — we use the resolved
        /// TypeInfoTableRva as the version fingerprint since it is stable within a
        /// single game build and changes when the binary changes).
        /// </summary>
        private sealed class OffsetCache
        {
            /// <summary>
            /// <see cref="Offsets.Special.TypeInfoTableRva"/> at the time the cache
            /// was written. Used as a build-version fingerprint: if this no longer
            /// matches what sig-scan resolves, the cache is stale.
            /// </summary>
            public ulong TypeInfoTableRva { get; set; }

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
                var cache = new OffsetCache
                {
                    TypeInfoTableRva = Offsets.Special.TypeInfoTableRva,
                    Fields = CollectAllFields(),
                };

                var json = JsonSerializer.Serialize(cache, _jsonOpts);
                File.WriteAllText(CacheFilePath, json);
                XMLogging.WriteLine($"[Il2CppDumper] Cache saved → {CacheFilePath}");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Il2CppDumper] Cache save FAILED: {ex.Message}");
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
                    XMLogging.WriteLine("[Il2CppDumper] No cache file found — will perform live dump.");
                    return false;
                }

                var json = File.ReadAllText(CacheFilePath);
                var cache = JsonSerializer.Deserialize<OffsetCache>(json, _jsonOpts);

                if (cache is null || cache.Fields.Count == 0)
                {
                    XMLogging.WriteLine("[Il2CppDumper] Cache file is empty or corrupt — will perform live dump.");
                    return false;
                }

                if (cache.TypeInfoTableRva != expectedRva)
                {
                    XMLogging.WriteLine(
                        $"[Il2CppDumper] Cache RVA mismatch: cached=0x{cache.TypeInfoTableRva:X} " +
                        $"current=0x{expectedRva:X} — cache is stale, performing live dump.");
                    return false;
                }

                int applied = ApplyCachedFields(cache.Fields);
                XMLogging.WriteLine($"[Il2CppDumper] Cache loaded — {applied}/{cache.Fields.Count} fields applied.");
                return applied > 0;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Il2CppDumper] Cache load FAILED: {ex.Message} — will perform live dump.");
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

                    string value = raw switch
                    {
                        uint[] arr  => arr.Length > 0 ? arr[0].ToString() : null,
                        uint u      => u.ToString(),
                        ulong ul    => ul.ToString(),
                        int i       => i.ToString(),
                        _           => null,
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
                var fieldName  = key[(dot + 1)..];

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
                            var arr = (uint[])fi.GetValue(null);
                            if (arr is { Length: > 0 }) { arr[0] = v; applied++; }
                        }
                    }
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"[Il2CppDumper] Cache: failed to apply {key}: {ex.Message}");
                }
            }

            return applied;
        }
    }
}
