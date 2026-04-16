using System.Collections;
using eft_dma_radar.Silk.DMA.ScatterAPI;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Explosives
{
    /// <summary>
    /// Discovers and refreshes explosives (grenades, tripwires, mortar projectiles) in the current raid.
    /// Uses scatter-batched reads for per-tick updates and direct DMA for discovery.
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
        /// Full refresh cycle: scatter-update existing items, discover new ones, prune inactive.
        /// Called each tick from the explosives worker thread.
        /// </summary>
        public void Refresh()
        {
            try
            {
                // 1) Fast path: scatter-batched update of all existing explosives
                if (!_explosives.IsEmpty)
                {
                    using var map = ScatterReadMap.Get();
                    var round = map.AddRound(useCache: true);
                    var idx = round[0];

                    // Queue reads
                    foreach (var explosive in _explosives.Values)
                    {
                        try
                        {
                            explosive.QueueScatterReads(idx);
                        }
                        catch { }
                    }

                    // Execute scatter if anything was queued
                    if (idx.Entries.Count > 0)
                    {
                        try
                        {
                            map.Execute();
                        }
                        catch { }

                        // Apply results
                        foreach (var explosive in _explosives.Values)
                        {
                            try
                            {
                                explosive.ApplyScatterResults(idx);
                            }
                            catch { }
                        }
                    }

                    // Prune inactive
                    _expiredKeys.Clear();
                    foreach (var kv in _explosives)
                    {
                        if (!kv.Value.IsActive)
                            _expiredKeys.Add(kv.Key);
                    }
                    for (int i = 0; i < _expiredKeys.Count; i++)
                        _explosives.TryRemove(_expiredKeys[i], out _);
                }

                // 2) Discovery: find new explosives (direct DMA — cheap & infrequent)
                GetGrenades();
                GetTripwires();
                GetMortarProjectiles();
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (NullReferenceException)
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
