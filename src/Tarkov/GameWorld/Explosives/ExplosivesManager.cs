using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity.Collections;

namespace eft_dma_radar.Tarkov.GameWorld.Explosives
{
    public sealed class ExplosivesManager : IReadOnlyCollection<IExplosiveItem>
    {
        private static readonly uint[] _toSyncObjects = new[] { Offsets.GameWorld.SynchronizableObjectLogicProcessor, Offsets.SynchronizableObjectLogicProcessor._activeSynchronizableObjects };
        private readonly ulong _localGameWorld;
        private readonly ConcurrentDictionary<ulong, IExplosiveItem> _explosives = new();
        private ulong _grenadesBase;

        public ExplosivesManager(ulong localGameWorld)
        {
            _localGameWorld = localGameWorld;
        }

        private void Init()
        {
            var grenadesPtr = Memory.ReadPtr(_localGameWorld + Offsets.ClientLocalGameWorld.Grenades, false);
            _grenadesBase = Memory.ReadPtr(grenadesPtr + 0x18, false);
        }

        /// <summary>
        /// Check for "hot" explosives in LocalGameWorld.
        /// Uses ScatterRead for fast per-frame updates, and direct DMA for discovery.
        /// </summary>
        public void Refresh()
        {
            try
            {
                // just to see if this is even firing
                // NOTE: comment out later if too spammy
                // XMLogging.WriteLine($"[EXP-RTL] Refresh start. Count={_explosives.Count}");

                // ─────────────────────────────────────────────────────
                // 1) Fast path: update ALL existing explosives with scatter
                // ─────────────────────────────────────────────────────
                if (!_explosives.IsEmpty)
                {
                    using var map = ScatterReadMap.Get();
                    var round = map.AddRound(useCache: true);
                    var idx = round[0]; // ScatterReadRound indexer

                    // Queue reads
                    foreach (var explosive in _explosives.Values)
                    {
                        try
                        {
                            // 🔥 Always let the explosive decide what to queue.
                            // Grenade/Tripwire already check IsActive internally if they want.
                            explosive.QueueScatterReads(idx);
                        }
                        catch (Exception ex)
                        {
                            XMLogging.WriteLine($"[EXP-RTL] QueueScatterReads error for 0x{explosive.Addr:X}: {ex}");
                        }
                    }

                    // If nobody actually queued anything, DO NOT call scatter
                    if (idx.EntryCount > 0)
                    {
                        //XMLogging.WriteLine($"[EXP-RTL] Scatter executing. Entries={idx.EntryCount}");

                        try
                        {
                            map.Execute();
                        }
                        catch (Exception ex)
                        {
                            //XMLogging.WriteLine($"[EXP-RTL] Scatter Execute error: {ex}");
                        }

                        // Apply results
                        foreach (var explosive in _explosives.Values)
                        {
                            try
                            {
                                explosive.OnRefresh(idx);
                            }
                            catch (Exception ex)
                            {
                                //XMLogging.WriteLine($"[EXP-RTL] OnRefresh error for 0x{explosive.Addr:X}: {ex}");
                            }
                        }
                    }
                    else
                    {
                        //XMLogging.WriteLine("[EXP-RTL] Scatter skipped (no entries queued).");
                    }

                    // Cleanup inactive / expired explosives
                    foreach (var kv in _explosives.ToArray())
                    {
                        if (!kv.Value.IsActive)
                            _explosives.TryRemove(kv.Key, out _);
                    }
                }

                // ─────────────────────────────────────────────────────
                // 2) Discovery path: find NEW explosives
                //    (still uses direct DMA, but this is cheap & infrequent)
                // ─────────────────────────────────────────────────────
                GetGrenades();
                GetTripwires();
                GetMortarProjectiles();

                // XMLogging.WriteLine($"[EXP-RTL] Refresh end. Count={_explosives.Count}");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[EXP-RTL] Refresh error: {ex}");
            }
        }

        // ─────────────────────────────────────────────────────────
        // GRENADE DISCOVERY (uses MemList; per-frame updates use scatter)
        // ─────────────────────────────────────────────────────────
        private void GetGrenades()
        {
            try
            {
                if (_grenadesBase == 0x0)
                    Init();

                if (_grenadesBase == 0x0)
                    return;

                using var allGrenades = MemList<ulong>.Get(_grenadesBase, false);
                foreach (var grenadeAddr in allGrenades)
                {
                    try
                    {
                        if (grenadeAddr == 0)
                            continue;

                        if (!_explosives.ContainsKey(grenadeAddr))
                        {
                            var grenade = new Grenade(grenadeAddr, _explosives);
                            _explosives[grenade] = grenade;
                            // XMLogging.WriteLine($"[EXP-RTL] New grenade @ 0x{grenadeAddr:X}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Silently skip invalid grenades to reduce log spam
                        // XMLogging.WriteLine($"[EXP-RTL] Grenade create error @ 0x{grenadeAddr:X}: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                _grenadesBase = 0x0;
                XMLogging.WriteLine($"Grenades Error: {ex}");
            }
        }

        // ─────────────────────────────────────────────────────────
        // TRIPWIRE DISCOVERY (synchronizable objects)
        // ─────────────────────────────────────────────────────────
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
                        var type = (Enums.SynchronizableObjectType)Memory.ReadValue<int>(syncObject + Offsets.SynchronizableObject.Type);
                        //XMLogging.WriteLine($"Type: {type}");
                        if (type is not Enums.SynchronizableObjectType.Tripwire)
                            continue;
                        if (!_explosives.ContainsKey(syncObject))
                        {
                            var tripwire = new Tripwire(syncObject);
                            _explosives[tripwire] = tripwire;
                        }
                    }
                    catch (Exception ex)
                    {
                        XMLogging.WriteLine($"Error Processing SyncObject @ 0x{syncObject.ToString("X")}: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"Sync Objects Error: {ex}");
            }
        }

        // ─────────────────────────────────────────────────────────
        // MORTAR PROJECTILES DISCOVERY
        // (per-frame updates can later be moved to scatter like grenades)
        // ─────────────────────────────────────────────────────────
        private void GetMortarProjectiles()
        {
            try
            {
                var clientShellingController =
                    Memory.ReadValue<ulong>(_localGameWorld + Offsets.ClientLocalGameWorld.ClientShellingController);

                if (clientShellingController == 0x0)
                    return;

                var activeProjectilesPtr =
                    Memory.ReadValue<ulong>(clientShellingController + Offsets.ClientShellingController.ActiveClientProjectiles);

                if (activeProjectilesPtr == 0x0)
                    return;

                using var activeProjectiles = MemDictionary<int, ulong>.Get(activeProjectilesPtr);
                foreach (var activeProjectile in activeProjectiles)
                {
                    if (activeProjectile.Value == 0x0)
                        continue;

                    try
                    {
                        if (!_explosives.ContainsKey(activeProjectile.Value))
                        {
                            var mortarProjectile = new MortarProjectile(activeProjectile.Value, _explosives);
                            _explosives[mortarProjectile] = mortarProjectile;
                            // XMLogging.WriteLine($"[EXP-RTL] New mortar @ 0x{activeProjectile.Value:X}");
                        }
                    }
                    catch (Exception ex)
                    {
                        XMLogging.WriteLine($"Error Processing Mortar Projectile @ 0x{activeProjectile.Value:X}: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"Mortar Projectiles Error: {ex}");
            }
        }

        #region IReadOnlyCollection

        public int Count => _explosives.Values.Count;
        public IEnumerator<IExplosiveItem> GetEnumerator() => _explosives.Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        #endregion
    }
}
