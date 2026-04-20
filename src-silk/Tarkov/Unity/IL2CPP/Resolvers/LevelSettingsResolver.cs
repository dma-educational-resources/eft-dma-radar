namespace eft_dma_radar.Silk.Tarkov.Unity.IL2CPP
{
    /// <summary>
    /// Resolves the LevelSettings component instance by scanning the GOM active-objects list
    /// for the "---Custom_levelsettings---" GameObject.
    /// Cached after first successful resolution; call <see cref="Reset"/> on raid start/stop.
    /// </summary>
    internal static class LevelSettingsResolver
    {
        private const string TargetGoName = "---Custom_levelsettings---";

        private static ulong _cachedLevelSettings;
        private static readonly Lock _lock = new();
        private static volatile bool _resolving;

        public static void Reset()
        {
            lock (_lock)
                _cachedLevelSettings = 0;
            _resolving = false;
        }

        public static bool TryGetCached(out ulong levelSettings)
        {
            lock (_lock)
            {
                levelSettings = _cachedLevelSettings;
                return levelSettings.IsValidVirtualAddress();
            }
        }

        /// <summary>
        /// Fire-and-forget background resolve. Safe to call from any thread.
        /// </summary>
        public static void ResolveAsync()
        {
            if (_resolving) return;
            _resolving = true;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var ls = GetLevelSettings();
                    if (ls.IsValidVirtualAddress())
                        Log.WriteLine($"[LevelSettingsResolver] Resolved @ 0x{ls:X}");
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"[LevelSettingsResolver] Async resolve failed: {ex.Message}");
                }
                finally
                {
                    _resolving = false;
                }
            });
        }

        public static ulong GetLevelSettings()
        {
            if (TryGetCached(out var cached))
                return cached;

            try
            {
                var gomAddr = GOM.GetAddr(Memory.UnityBase);
                if (!gomAddr.IsValidVirtualAddress())
                    return 0;

                var gom = GOM.Get(gomAddr);

                if (!Memory.TryReadValue<LinkedListObject>(gom.ActiveNodes, out var first, false))
                    return 0;
                if (!Memory.TryReadValue<LinkedListObject>(gom.LastActiveNode, out var last, false))
                    return 0;

                // Forward scan
                var result = ScanForward(first, last);
                if (result == 0)
                    result = ScanBackward(last, first);

                if (result.IsValidVirtualAddress())
                {
                    lock (_lock)
                        _cachedLevelSettings = result;
                }
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LevelSettingsResolver] GetLevelSettings failed: {ex.Message}");
                return 0;
            }
        }

        private static ulong ScanForward(LinkedListObject start, LinkedListObject end)
        {
            var current = start;
            for (int i = 0; i < 100_000; i++)
            {
                if (!current.ThisObject.IsValidVirtualAddress()) break;
                if (TryMatchLevelSettings(current, out var ls)) return ls;
                if (current.ThisObject == end.ThisObject) break;
                if (!Memory.TryReadValue<LinkedListObject>(current.NextObjectLink, out current, false)) break;
            }
            return 0;
        }

        private static ulong ScanBackward(LinkedListObject start, LinkedListObject end)
        {
            var current = start;
            for (int i = 0; i < 100_000; i++)
            {
                if (!current.ThisObject.IsValidVirtualAddress()) break;
                if (TryMatchLevelSettings(current, out var ls)) return ls;
                if (current.ThisObject == end.ThisObject) break;
                if (!Memory.TryReadValue<LinkedListObject>(current.PreviousObjectLink, out current, false)) break;
            }
            return 0;
        }

        private static bool TryMatchLevelSettings(LinkedListObject node, out ulong levelSettings)
        {
            levelSettings = 0;
            try
            {
                if (!node.ThisObject.IsValidVirtualAddress()) return false;

                var namePtr = Memory.ReadPtr(node.ThisObject + UnityOffsets.GO_Name);
                if (!namePtr.IsValidVirtualAddress()) return false;

                string name;
                try { name = Memory.ReadString(namePtr, 64, useCache: false); }
                catch { return false; }

                if (!string.Equals(name, TargetGoName, StringComparison.Ordinal))
                    return false;

                var instance = Memory.ReadPtrChain(
                    node.ThisObject,
                    UnityOffsets.LevelSettings.LevelSettingsChain,
                    useCache: true);

                if (!instance.IsValidVirtualAddress()) return false;

                levelSettings = instance;
                return true;
            }
            catch { return false; }
        }
    }
}
