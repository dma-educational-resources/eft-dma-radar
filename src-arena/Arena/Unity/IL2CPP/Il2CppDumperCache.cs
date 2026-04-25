#pragma warning disable IDE0130

using System.IO;

namespace eft_dma_radar.Arena.Unity.IL2CPP
{
    public static partial class Il2CppDumper
    {
        private static readonly string CacheFilePath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "eft-dma-radar-arena",
                "il2cpp_offsets.json");

        // Bump when static (non-dumped) offsets in SDK.Offsets change, so stale caches
        // that captured the old source defaults are rejected on load.
        //  v2: PlayerBody.SkeletonRootJoint 0x28 -> 0x30; DizSkinningSkeleton._values confirmed 0x30.
        private const int CacheSchemaVersion = 2;

        private sealed class OffsetCache
        {
            public int SchemaVersion { get; set; }
            public ulong TypeInfoTableRva { get; set; }
            public uint GameAssemblyTimestamp { get; set; }
            public uint GameAssemblySizeOfImage { get; set; }
            public Dictionary<string, string> Fields { get; set; } = new();
        }

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        internal static void SaveCache()
        {
            try
            {
                var (timestamp, sizeOfImage) = Memory.ReadPeFingerprint(Memory.GameAssemblyBase);
                var cache = new OffsetCache
                {
                    SchemaVersion = CacheSchemaVersion,
                    TypeInfoTableRva = SDK.Offsets.Special.TypeInfoTableRva,
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

        internal static bool TryLoadCache(ulong expectedRva)
        {
            try
            {
                if (!File.Exists(CacheFilePath)) { Log.WriteLine("[Il2CppDumper] No cache file found — will perform live dump."); return false; }
                var json = File.ReadAllText(CacheFilePath);
                var cache = JsonSerializer.Deserialize<OffsetCache>(json, _jsonOpts);
                if (cache is null || cache.Fields.Count == 0) { Log.WriteLine("[Il2CppDumper] Cache file is empty or corrupt."); return false; }
                if (cache.TypeInfoTableRva != expectedRva)
                {
                    Log.WriteLine($"[Il2CppDumper] Cache RVA mismatch: cached=0x{cache.TypeInfoTableRva:X} current=0x{expectedRva:X} — stale.");
                    return false;
                }
                int applied = ApplyCachedFields(cache.Fields);
                Log.WriteLine($"[Il2CppDumper] Cache loaded — {applied}/{cache.Fields.Count} fields applied.");
                return applied > 0;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[Il2CppDumper] Cache load FAILED: {ex.Message}");
                return false;
            }
        }

        internal static bool TryFastLoadCache(ulong gaBase)
        {
            try
            {
                if (!File.Exists(CacheFilePath)) return false;
                var (timestamp, sizeOfImage) = Memory.ReadPeFingerprint(gaBase);
                if (timestamp == 0 || sizeOfImage == 0) return false;
                var json = File.ReadAllText(CacheFilePath);
                var cache = JsonSerializer.Deserialize<OffsetCache>(json, _jsonOpts);
                if (cache is null || cache.Fields.Count == 0) return false;
                if (cache.SchemaVersion < CacheSchemaVersion)
                {
                    Log.WriteLine($"[Il2CppDumper] Fast cache schema outdated ({cache.SchemaVersion} < {CacheSchemaVersion}) — fresh dump required.");
                    return false;
                }
                if (cache.GameAssemblyTimestamp != timestamp || cache.GameAssemblySizeOfImage != sizeOfImage)
                {
                    Log.WriteLine("[Il2CppDumper] PE fingerprint mismatch (game updated?) — fresh dump required.");
                    return false;
                }
                if (cache.TypeInfoTableRva != 0)
                    SDK.Offsets.Special.TypeInfoTableRva = cache.TypeInfoTableRva;
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

        private const BindingFlags _bf = BindingFlags.Public | BindingFlags.Static;

        private static Dictionary<string, string> CollectAllFields()
        {
            var result = new Dictionary<string, string>(256);
            var offsetsType = typeof(SDK.Offsets);
            foreach (var nested in offsetsType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (var fi in nested.GetFields(_bf))
                {
                    if (fi.IsLiteral) continue;
                    var raw = fi.GetValue(null);
                    if (raw is null) continue;
                    string? value = raw switch
                    {
                        uint[] arr => arr.Length > 0 ? arr[0].ToString() : null,
                        uint u  => u.ToString(),
                        ulong ul => ul.ToString(),
                        int i   => i.ToString(),
                        _       => null,
                    };
                    if (value is not null)
                        result[$"{nested.Name}.{fi.Name}"] = value;
                }
            }
            return result;
        }

        private static int ApplyCachedFields(Dictionary<string, string> fields)
        {
            var offsetsType = typeof(SDK.Offsets);
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
                    if      (fi.FieldType == typeof(uint)  && uint.TryParse(rawValue, out var uv))  { fi.SetValue(null, uv);  applied++; }
                    else if (fi.FieldType == typeof(ulong) && ulong.TryParse(rawValue, out var ulv)) { fi.SetValue(null, ulv); applied++; }
                    else if (fi.FieldType == typeof(int)   && int.TryParse(rawValue, out var iv))    { fi.SetValue(null, iv);  applied++; }
                    else if (fi.FieldType == typeof(uint[]) && uint.TryParse(rawValue, out var av))
                    {
                        var arr = (uint[]?)fi.GetValue(null);
                        if (arr is { Length: > 0 }) { arr[0] = av; applied++; }
                    }
                }
                catch (Exception ex) { Log.WriteLine($"[Il2CppDumper] Cache: failed to apply {key}: {ex.Message}"); }
            }
            return applied;
        }
    }
}
