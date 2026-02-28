using System;
using System.Diagnostics;
using System.Threading;
using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Common.Unity;
using SDK;

namespace eft_dma_radar.Tarkov.Unity.IL2CPP
{
    internal static class LevelSettingsResolver
    {
        private const string TargetGoName = "---Custom_levelsettings---";

        // Last successfully resolved LevelSettings instance
        private static ulong _cachedLevelSettings;
        private static readonly object _lock = new();

        // Simple flag to avoid spamming async resolves
        private static volatile bool _resolvingAsync;

        /// <summary>
        /// Clear cached LevelSettings pointer (call on raid start/stop).
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _cachedLevelSettings = 0;
            }
            _resolvingAsync = false;
        }

        /// <summary>
        /// Non-blocking cache read: returns true if we have a valid cached pointer.
        /// </summary>
        public static bool TryGetCached(out ulong levelSettings)
        {
            lock (_lock)
            {
                levelSettings = _cachedLevelSettings;
                return levelSettings.IsValidVirtualAddress();
            }
        }

        /// <summary>
        /// Fire-and-forget background resolve.
        /// Safe to call from any thread; does not block caller.
        /// </summary>
        public static void ResolveAsync()
        {
            if (_resolvingAsync)
                return;

            _resolvingAsync = true;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var ls = GetLevelSettings();
                    if (ls.IsValidVirtualAddress())
                    {
                        XMLogging.WriteLine($"[LevelSettingsResolver] Async resolved LevelSettings @ 0x{ls:X}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LevelSettingsResolver] ResolveAsync error: {ex}");
                }
                finally
                {
                    _resolvingAsync = false;
                }
            });
        }

        /// <summary>
        /// Synchronous resolver with full GOM walk.
        /// Intended for background threads only (can be slow).
        /// </summary>
        public static ulong GetLevelSettings()
        {
            // 1) Fast path ¨C cached value
            if (TryGetCached(out var cached))
                return cached;

            // 2) Do a full scan (no global lock around the scan itself)
            ulong result = 0;

            try
            {
                // 1) Resolve UnityPlayer.dll base
                var unityBase = Memory.UnityBase;
                if (unityBase == 0)
                {
                    Debug.WriteLine("[LevelSettingsResolver] UnityPlayer.dll base not found.");
                    return 0;
                }

                // 2) Global pointer to GameObjectManager
                var gomGlobal = unityBase + UnityOffsets.ModuleBase.GameObjectManager;
                var gomPtr    = Memory.ReadPtr(gomGlobal);

                if (!gomPtr.IsValidVirtualAddress())
                {
                    Debug.WriteLine($"[LevelSettingsResolver] GameObjectManager pointer invalid: 0x{gomPtr:X}");
                    return 0;
                }

                var gom = GameObjectManager.Get(gomPtr);

                // 3) Read first / last active nodes
                var firstNode = Memory.ReadValue<LinkedListObject>(gom.ActiveNodes);
                var lastNode  = Memory.ReadValue<LinkedListObject>(gom.LastActiveNode);

                firstNode.ThisObject.ThrowIfInvalidVirtualAddress();
                firstNode.NextObjectLink.ThrowIfInvalidVirtualAddress();
                lastNode.ThisObject.ThrowIfInvalidVirtualAddress();
                lastNode.PreviousObjectLink.ThrowIfInvalidVirtualAddress();

                // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
                // Forward scan: firstNode ¡ú lastNode
                // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
                result = ScanForward(firstNode, lastNode);
                if (result.IsValidVirtualAddress())
                {
                    Cache(result);
                    return result;
                }

                // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
                // Backward scan: lastNode ¡ú firstNode
                // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
                result = ScanBackward(lastNode, firstNode);
                if (result.IsValidVirtualAddress())
                {
                    Cache(result);
                    return result;
                }

                Debug.WriteLine("[LevelSettingsResolver] LevelSettings GameObject not found (forward/backward).");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LevelSettingsResolver] Exception while searching: {ex.Message}");
                result = 0;
            }

            return result;
        }

        /// <summary>
        /// Forward traversal: firstNode ¡ú lastNode over the GOM activeObjects list.
        /// </summary>
        private static ulong ScanForward(LinkedListObject firstNode, LinkedListObject lastNode)
        {
            const int maxDepth = 100_000;
            int iterations = 0;

            var current = firstNode;

            while (true)
            {
                if (++iterations > maxDepth)
                {
                    Debug.WriteLine("[LevelSettingsResolver] Forward scan hit maxDepth; aborting.");
                    break;
                }

                if (!current.ThisObject.IsValidVirtualAddress())
                    break;

                if (TryMatchLevelSettings(current, out var instance))
                {
                    Debug.WriteLine("[LevelSettingsResolver] LevelSettings found (forward scan).");
                    return instance;
                }

                if (current.ThisObject == lastNode.ThisObject)
                    break;

                current = Memory.ReadValue<LinkedListObject>(current.NextObjectLink);
            }

            return 0;
        }

        /// <summary>
        /// Backward traversal: lastNode ¡ú firstNode over the GOM activeObjects list.
        /// </summary>
        private static ulong ScanBackward(LinkedListObject lastNode, LinkedListObject firstNode)
        {
            const int maxDepth = 100_000;
            int iterations = 0;

            var current = lastNode;

            while (true)
            {
                if (++iterations > maxDepth)
                {
                    Debug.WriteLine("[LevelSettingsResolver] Backward scan hit maxDepth; aborting.");
                    break;
                }

                if (!current.ThisObject.IsValidVirtualAddress())
                    break;

                if (TryMatchLevelSettings(current, out var instance))
                {
                    Debug.WriteLine("[LevelSettingsResolver] LevelSettings found (backward scan).");
                    return instance;
                }

                if (current.ThisObject == firstNode.ThisObject)
                    break;

                current = Memory.ReadValue<LinkedListObject>(current.PreviousObjectLink);
            }

            return 0;
        }

        /// <summary>
        /// Checks a single GOM linked-list node for the target
        /// "---Custom_levelsettings---" GameObject and resolves the instance via
        /// UnityOffsets.LevelSettings.LevelSettingsChain.
        /// </summary>
        private static bool TryMatchLevelSettings(LinkedListObject node, out ulong levelSettings)
        {
            levelSettings = 0;

            try
            {
                if (!node.ThisObject.IsValidVirtualAddress())
                    return false;

                var namePtr = Memory.ReadPtr(node.ThisObject + UnityOffsets.GameObject.NameOffset);
                if (!namePtr.IsValidVirtualAddress())
                    return false;

                string name;
                try
                {
                    // useCache: false to avoid fighting the cache on rapidly changing memory
                    name = Memory.ReadString(namePtr, 64, useCache: false);
                }
                catch
                {
                    // Most likely VmmException / transient mapping issue.
                    // Just skip this node silently.
                    return false;
                }

                if (!string.Equals(name, TargetGoName, StringComparison.Ordinal))
                    return false;

                // Same chain from your dumper: GO ¡ú component ¡ú instance
                var instance = Memory.ReadPtrChain(
                    node.ThisObject,
                    UnityOffsets.LevelSettings.LevelSettingsChain,
                    useCache: true);

                if (!instance.IsValidVirtualAddress())
                {
                    Debug.WriteLine(
                        $"[LevelSettingsResolver] Matched GO '{name}' but chain resolved to invalid addr 0x{instance:X}");
                    return false;
                }

                Debug.WriteLine(
                    $"[LevelSettingsResolver] FOUND '{TargetGoName}' ¨C LevelSettings instance 0x{instance:X}");

                levelSettings = instance;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LevelSettingsResolver] TryMatchLevelSettings hard fail: {ex}");
                return false;
            }
        }

        private static void Cache(ulong instance)
        {
            if (!instance.IsValidVirtualAddress())
                return;

            lock (_lock)
            {
                _cachedLevelSettings = instance;
            }
        }
    }
}
