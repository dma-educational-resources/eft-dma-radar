using System.Collections;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Explosives
{
    /// <summary>
    /// Discovers and refreshes explosives (grenades, tripwires, mortar projectiles) in the current raid.
    /// Uses VmmScatter for per-tick updates and direct DMA for discovery.
    /// Runs on a dedicated worker thread via <see cref="LocalGameWorld"/>.
    /// </summary>
    internal sealed class ExplosivesManager : IReadOnlyCollection<IExplosiveItem>
    {
        private static readonly uint[] _toSyncObjects =
        [
            Offsets.GameWorld.SynchronizableObjectLogicProcessor,
            Offsets.SynchronizableObjectLogicProcessor._activeSynchronizableObjects
        ];

        private readonly ulong _localGameWorld;
        private readonly ConcurrentDictionary<ulong, IExplosiveItem> _explosives = new();
        private readonly List<ulong> _expiredKeys = [];
        // Addresses that have repeatedly failed scatter — skip in discovery for the rest of the raid.
        private readonly HashSet<ulong> _badAddrs = [];
        // Per-address consecutive scatter-failure count.
        private readonly Dictionary<ulong, int> _failCounts = [];
        private const int MaxConsecutiveFails = 3;
        private ulong _grenadesBase;

        public ExplosivesManager(ulong localGameWorld)
        {
            _localGameWorld = localGameWorld;
        }

        /// <summary>
        /// Returns a snapshot of all active explosive items for rendering.
        /// </summary>
        public ICollection<IExplosiveItem> Snapshot => _explosives.Values;

        /// <summary>
        /// Full refresh cycle: discover new items, scatter-update existing ones, prune inactive.
        /// Called each tick from the explosives worker thread.
        /// </summary>
        public void Refresh()
        {
            try
            {
                // 1) Discovery: find new explosives (direct DMA)
                GetGrenades();
                GetTripwires();
                GetMortarProjectiles();

                // 2) Scatter-batched update of all existing explosives
                var explosives = _explosives.Values;
                if (explosives.Count == 0)
                    return;

                using var scatter = Memory.CreateScatter(useCache: false);
                int queued = 0;
                // Track which explosives contributed entries so we can identify the bad one on failure
                var contributors = new List<(ulong Addr, string Kind)>(explosives.Count);
                foreach (var explosive in explosives)
                {
                    try
                    {
                        explosive.OnRefresh(scatter);
                        queued++;
                        contributors.Add((explosive.Addr, explosive.GetType().Name));
                    }
                    catch (Exception ex)
                    {
                        Log.WriteRateLimited(AppLogLevel.Warning, $"explosive_{explosive.Addr:X}", TimeSpan.FromSeconds(5),
                            $"[Explosives] Error refreshing 0x{explosive.Addr:X}: {ex.Message}");
                    }
                }

                if (queued > 0)
                {
                    bool scatterOk = false;
                    try
                    {
                        scatter.Execute();
                        scatterOk = true;
                    }
                    catch (VmmSharpEx.VmmException ex)
                    {
                        // Surface the addresses so we can diagnose which explosive has a stale pointer.
                        // Rate-limited so it doesn't spam each tick.
                        var sb = new StringBuilder();
                        sb.Append("[Explosives] Scatter failed (").Append(queued).Append(" entries): ")
                          .Append(ex.Message).Append(" | contributors=[");
                        for (int i = 0; i < contributors.Count; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            sb.Append(contributors[i].Kind).Append("@0x").Append(contributors[i].Addr.ToString("X"));
                        }
                        sb.Append(']');
                        Log.WriteRateLimited(AppLogLevel.Warning, "explosives_scatter_fail", TimeSpan.FromSeconds(5), sb.ToString());

                        // Count failures per address — remove from live set now, and if a given address
                        // keeps failing (likely a permanently stale/recycled pointer that the game still
                        // re-reports in its grenade list), blacklist it so discovery stops re-adding it.
                        for (int i = 0; i < contributors.Count; i++)
                        {
                            var addr = contributors[i].Addr;
                            _explosives.TryRemove(addr, out _);
                            int count = _failCounts.TryGetValue(addr, out var c) ? c + 1 : 1;
                            if (count >= MaxConsecutiveFails)
                            {
                                _badAddrs.Add(addr);
                                _failCounts.Remove(addr);
                                Log.WriteLine($"[Explosives] Blacklisting stale 0x{addr:X} after {count} failures");
                            }
                            else
                            {
                                _failCounts[addr] = count;
                            }
                        }
                    }

                    // Scatter succeeded — any contributor's prior failure streak is over.
                    if (scatterOk && _failCounts.Count > 0)
                    {
                        for (int i = 0; i < contributors.Count; i++)
                            _failCounts.Remove(contributors[i].Addr);
                    }
                }

                // 3) Prune inactive
                _expiredKeys.Clear();
                foreach (var kv in _explosives)
                {
                    if (!kv.Value.IsActive)
                        _expiredKeys.Add(kv.Key);
                }
                for (int i = 0; i < _expiredKeys.Count; i++)
                    _explosives.TryRemove(_expiredKeys[i], out _);
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, "explosives_refresh", TimeSpan.FromSeconds(5),
                    $"[Explosives] Refresh error: {ex.Message}");
            }
        }

        #region Grenade Discovery

        private void GetGrenades()
        {
            try
            {
                if (_grenadesBase == 0)
                    InitGrenades();

                if (_grenadesBase == 0)
                    return;

                using var allGrenades = MemList<ulong>.Get(_grenadesBase, false);
                foreach (var grenadeAddr in allGrenades)
                {
                    if (grenadeAddr == 0)
                        continue;

                    if (_badAddrs.Contains(grenadeAddr))
                        continue;

                    if (!_explosives.ContainsKey(grenadeAddr))
                    {
                        try
                        {
                            var grenade = new Grenade(grenadeAddr, _explosives);
                            _explosives[grenadeAddr] = grenade;
                        }
                        catch { }
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                _grenadesBase = 0;
                throw;
            }
            catch (NullReferenceException)
            {
                _grenadesBase = 0;
                throw;
            }
            catch (Exception ex)
            {
                _grenadesBase = 0;
                Log.WriteRateLimited(AppLogLevel.Warning, "grenades_err", TimeSpan.FromSeconds(10),
                    $"[Explosives] Grenades error: {ex.Message}");
            }
        }

        private void InitGrenades()
        {
            var grenadesPtr = Memory.ReadPtr(_localGameWorld + Offsets.ClientLocalGameWorld.Grenades, false);
            _grenadesBase = Memory.ReadPtr(grenadesPtr + 0x18, false);
        }

        #endregion

        #region Tripwire Discovery

        private void GetTripwires()
        {
            try
            {
                var syncObjectsPtr = Memory.ReadPtrChain(_localGameWorld, _toSyncObjects);
                using var syncObjects = MemList<ulong>.Get(syncObjectsPtr);
                foreach (var syncObject in syncObjects)
                {
                    try
                    {
                        var type = (SDK.SynchronizableObjectType)Memory.ReadValue<int>(
                            syncObject + Offsets.SynchronizableObject.Type);

                        if (type is not SDK.SynchronizableObjectType.Tripwire)
                            continue;

                        if (!_explosives.ContainsKey(syncObject))
                        {
                            var tripwire = new Tripwire(syncObject);
                            _explosives[syncObject] = tripwire;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.WriteRateLimited(AppLogLevel.Warning, $"tripwire_{syncObject:X}", TimeSpan.FromSeconds(10),
                            $"[Explosives] Error processing SyncObject @ 0x{syncObject:X}: {ex.Message}");
                    }
                }
            }
            catch (ObjectDisposedException) { throw; }
            catch (NullReferenceException) { throw; }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, "tripwires_err", TimeSpan.FromSeconds(10),
                    $"[Explosives] Tripwires error: {ex.Message}");
            }
        }

        #endregion

        #region Mortar Discovery

        private void GetMortarProjectiles()
        {
            try
            {
                var clientShellingController = Memory.ReadValue<ulong>(
                    _localGameWorld + Offsets.ClientLocalGameWorld.ClientShellingController);

                if (clientShellingController == 0)
                    return;

                var activeProjectilesPtr = Memory.ReadValue<ulong>(
                    clientShellingController + Offsets.ClientShellingController.ActiveClientProjectiles);

                if (activeProjectilesPtr == 0)
                    return;

                using var activeProjectiles = MemDictionary<int, ulong>.Get(activeProjectilesPtr);
                foreach (var entry in activeProjectiles)
                {
                    if (entry.Value == 0)
                        continue;

                    if (!_explosives.ContainsKey(entry.Value))
                    {
                        try
                        {
                            var mortar = new MortarProjectile(entry.Value, _explosives);
                            _explosives[entry.Value] = mortar;
                        }
                        catch (Exception ex)
                        {
                            Log.WriteRateLimited(AppLogLevel.Warning, $"mortar_{entry.Value:X}", TimeSpan.FromSeconds(10),
                                $"[Explosives] Error processing mortar @ 0x{entry.Value:X}: {ex.Message}");
                        }
                    }
                }
            }
            catch (ObjectDisposedException) { throw; }
            catch (NullReferenceException) { throw; }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, "mortar_err", TimeSpan.FromSeconds(10),
                    $"[Explosives] Mortar error: {ex.Message}");
            }
        }

        #endregion

        #region IReadOnlyCollection

        public int Count => _explosives.Count;
        public IEnumerator<IExplosiveItem> GetEnumerator() => _explosives.Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        #endregion
    }
}
