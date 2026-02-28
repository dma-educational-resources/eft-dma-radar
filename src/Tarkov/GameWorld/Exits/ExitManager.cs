using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.Unity.Collections;

namespace eft_dma_radar.Tarkov.GameWorld.Exits
{
    /// <summary>
    /// List of PMC/Scav 'Exits' in Local Game World and their position/status.
    /// </summary>
    public sealed class ExitManager : IReadOnlyCollection<IExitPoint>
    {
        private readonly ulong _localGameWorld;
        private readonly bool _isPMC;
        private IReadOnlyList<IExitPoint> _exits;
        private int _initAttempts = 0;
        private const int MAX_INIT_ATTEMPTS = 20; // Try for ~10 seconds (500ms between refreshes)

        public ExitManager(ulong localGameWorld, bool isPMC)
        {
            _localGameWorld = localGameWorld;
            _isPMC = isPMC;
        }

        /// <summary>
        /// Initialize ExfilManager.
        /// </summary>
        private void Init()
        {
            var list = new List<IExitPoint>();
            try
            {
                var exfilController = Memory.ReadPtr(_localGameWorld + Offsets.ClientLocalGameWorld.ExfilController, false);
                if (exfilController == 0)
                {
                    XMLogging.WriteLine($"[ExitManager] ExfilController is null");
                    _exits = list;
                    return;
                }

                // Exfils
                try
                {
                    var listOffset = _isPMC ?
                        Offsets.ExfilController.ExfiltrationPointArray : Offsets.ExfilController.ScavExfiltrationPointArray;
                    var exfilPoints = Memory.ReadPtr(exfilController + listOffset, false);
                    
                    if (exfilPoints != 0)
                    {
                        using var exfils = MemArray<ulong>.Get(exfilPoints, false);
                        XMLogging.WriteLine($"[ExitManager] {(_isPMC ? "PMC" : "Scav")} exfil array @ 0x{exfilPoints:X}, count: {exfils.Count}");
                        
                        foreach (var exfilAddr in exfils)
                        {
                            try
                            {
                                var exfil = new Exfil(exfilAddr, _isPMC);
                                list.Add(exfil);
                            }
                            catch (Exception ex)
                            {
                                XMLogging.WriteLine($"[ExitManager] Failed to read exfil @ 0x{exfilAddr:X}: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        XMLogging.WriteLine($"[ExitManager] {(_isPMC ? "PMC" : "Scav")} exfil array is null");
                    }
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"[ExitManager] Exfil array read failed: {ex.Message}");
                }

                // Secret Extracts
                try
                {
                    var secretExfilPtr = Memory.ReadPtr(exfilController + Offsets.ExfilController.SecretExfiltrationPointArray, false);
                    if (secretExfilPtr != 0)
                    {
                        using var secrets = MemArray<ulong>.Get(secretExfilPtr, false);
                        XMLogging.WriteLine($"[ExitManager] Secret exfil array @ 0x{secretExfilPtr:X}, count: {secrets.Count}");
                        
                        foreach (var secretAddr in secrets)
                        {
                            try
                            {
                                var exfil = new Exfil(secretAddr, true);
                                list.Add(exfil);
                                XMLogging.WriteLine($"[ExitManager] Secret exfil loaded: {exfil.Name}");
                            }
                            catch (Exception ex)
                            {
                                XMLogging.WriteLine($"[ExitManager] Failed to read secret exfil @ 0x{secretAddr:X}: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        XMLogging.WriteLine($"[ExitManager] SecretExfiltrationPointArray is null (offset 0x{Offsets.ExfilController.SecretExfiltrationPointArray:X})");
                    }
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"[ExitManager] Secret exfil array read failed: {ex.Message}");
                }

                // Transits - Using hardcoded IL2CPP dictionary offsets (different from Mono)
                // IL2CPP Dictionary<K,V> structure:
                //   0x18: _entries (Entry[])
                //   0x20: _count (int)
                // Entry structure: hashCode(4) + next(4) + key(4) + padding(4) + value(8) = 24 bytes per entry
                try
                {
                    var transitController = Memory.ReadPtr(_localGameWorld + Offsets.ClientLocalGameWorld.TransitController, false);
                    if (transitController != 0)
                    {
                        var transitsPtr = Memory.ReadPtr(transitController + Offsets.TransitController.TransitPoints, false);
                        if (transitsPtr != 0)
                        {
                            // IL2CPP Dictionary offsets (hardcoded to not break existing MemDictionary)
                            const uint IL2CPP_DICT_COUNT = 0x20;      // _count offset in IL2CPP
                            const uint IL2CPP_DICT_ENTRIES = 0x18;    // _entries offset
                            const uint IL2CPP_ENTRIES_START = 0x20;   // Array data start offset
                            const int IL2CPP_ENTRY_SIZE = 24;         // Size of each dictionary entry
                            const int IL2CPP_ENTRY_VALUE_OFFSET = 16; // Offset to value within entry (after hashCode+next+key+pad)
                            
                            var count = Memory.ReadValue<int>(transitsPtr + IL2CPP_DICT_COUNT, false);
                            
                            if (count > 0 && count < 100) // Sanity check
                            {
                                var entriesPtr = Memory.ReadPtr(transitsPtr + IL2CPP_DICT_ENTRIES, false);
                                if (entriesPtr != 0)
                                {
                                    var entriesBase = entriesPtr + IL2CPP_ENTRIES_START;
                                    
                                    for (int i = 0; i < count; i++)
                                    {
                                        try
                                        {
                                            // Read the TransitPoint pointer from the entry's value field
                                            var entryAddr = entriesBase + (ulong)(i * IL2CPP_ENTRY_SIZE);
                                            var transitAddr = Memory.ReadPtr(entryAddr + IL2CPP_ENTRY_VALUE_OFFSET, false);
                                            
                                            if (transitAddr != 0)
                                            {
                                                var transit = new TransitPoint(transitAddr);
                                                list.Add(transit);
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            XMLogging.WriteLine($"[ExitManager] Failed to read transit[{i}]: {ex.Message}");
                                        }
                                    }
                                    
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"[ExitManager] Transit read error: {ex.Message}");
                }

                _exits = list;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[ExitManager] Init failed: {ex.Message}");
                _exits = list;
            }
        }

        /// <summary>
        /// Updates exfil statuses.
        /// </summary>
        public void Refresh()
        {
            try
            {
                // Initialize or retry if empty and we haven't exceeded max attempts
                if (_exits is null || (_exits.Count == 0 && _initAttempts < MAX_INIT_ATTEMPTS))
                {
                    _initAttempts++;
                    Init();
                    
                    if (_exits?.Count > 0)
                        XMLogging.WriteLine($"[ExitManager] Successfully initialized {_exits.Count} exits on attempt {_initAttempts}");
                    else if (_initAttempts % 5 == 0)
                        XMLogging.WriteLine($"[ExitManager] Still waiting for exits... attempt {_initAttempts}/{MAX_INIT_ATTEMPTS}");
                }

                ArgumentNullException.ThrowIfNull(_exits, nameof(_exits));
                
                if (_exits.Count == 0)
                    return; // Still no exits, wait for next refresh
                    
                using var map = ScatterReadMap.Get();
                var round1 = map.AddRound();
                for (int ix = 0; ix < _exits.Count; ix++)
                {
                    int i = ix;
                    var entry = _exits[i];
                    if (entry is Exfil exfil)
                    {
                        round1[i].AddEntry<int>(0, exfil + Offsets.Exfil._status);
                        round1[i].Callbacks += index =>
                        {
                            if (index.TryGetResult<int>(0, out var status))
                                exfil.Update((Enums.EExfiltrationStatus)status);
                        };
                    }
                }
                map.Execute();
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[ExitManager] Refresh Error: {ex}");
            }
        }

        #region IReadOnlyCollection

        public int Count => _exits?.Count ?? 0;
        public IEnumerator<IExitPoint> GetEnumerator() => _exits?.GetEnumerator() ?? Enumerable.Empty<IExitPoint>().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        #endregion
    }
}