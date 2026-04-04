/*  
 *  VmmSharpEx by Lone (Lone DMA)
 *  Copyright (C) 2025 AGPL-3.0
*/

namespace VmmSharpEx.Refresh;

/// <summary>
/// Controls the registration and management of refreshers for Vmm instances.
/// </summary>
internal static class RefreshManager
{
    private static readonly Lock _lock = new();
    private static readonly Dictionary<Vmm, Dictionary<RefreshOption, VmmRefresher>> _refreshers = new();

    /// <summary>
    /// Register a refresher for the given Vmm instance and refresh option.
    /// </summary>
    /// <param name="instance"></param>
    /// <param name="option"></param>
    /// <param name="interval"></param>
    /// <exception cref="VmmException"></exception>
    public static void Register(Vmm instance, RefreshOption option, TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(instance, nameof(instance));
        lock (_lock)
        {
            if (!_refreshers.TryGetValue(instance, out var dict))
                _refreshers[instance] = dict = new Dictionary<RefreshOption, VmmRefresher>();
            if (dict.ContainsKey(option))
            {
                throw new VmmException("Refresher already registered for this option!");
            }
            dict[option] = new VmmRefresher(instance, option, interval);
        }
    }

    /// <summary>
    /// Unregister a refresher for the given Vmm instance and refresh option.
    /// </summary>
    /// <param name="instance"></param>
    /// <param name="option"></param>
    public static void Unregister(Vmm instance, RefreshOption option)
    {
        if (instance is null)
            return;
        lock (_lock)
        {
            if (_refreshers.TryGetValue(instance, out var dict) && dict.TryGetValue(option, out var refresher))
            {
                refresher.Dispose();
                _ = dict.Remove(option);
            }
        }
    }

    /// <summary>
    /// Unregister all refreshers for the given Vmm instance.
    /// Usually called when the parent Vmm instance is disposed or no longer needed.
    /// </summary>
    /// <param name="instance"></param>
    public static void UnregisterAll(Vmm instance)
    {
        if (instance is null)
            return;
        lock (_lock)
        {
            if (_refreshers.TryGetValue(instance, out var dict))
            {
                foreach (var refresher in dict.Values)
                {
                    refresher.Dispose();
                }
                _ = _refreshers.Remove(instance);
            }
        }
    }
}