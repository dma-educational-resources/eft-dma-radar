using eft_dma_radar.Silk.DMA.ScatterAPI;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Exits
{
    /// <summary>
    /// Manages exfiltration points — reads from the ExfilController, refreshes status via scatter reads.
    /// Initialized lazily by <see cref="LocalGameWorld"/> on the registration worker thread.
    /// </summary>
    internal sealed class ExfilManager
    {
        private readonly ulong _lgw;
        private readonly string _mapId;
        private readonly bool _isPmc;
        private volatile IReadOnlyList<Exfil> _exfils = [];
        private int _initAttempts;
        private const int MaxInitAttempts = 20;
        private DateTime _lastRefresh;
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);

        /// <summary>Current exfil snapshot (thread-safe read).</summary>
        public IReadOnlyList<Exfil> Exfils => _exfils;

        public ExfilManager(ulong localGameWorld, string mapId, bool isPmc)
        {
            _lgw = localGameWorld;
            _mapId = mapId;
            _isPmc = isPmc;
        }

        /// <summary>
        /// Refreshes exfil status via scatter reads. Initializes on first call (with retry).
        /// Called from the registration worker thread.
        /// </summary>
        public void Refresh()
        {
            var now = DateTime.UtcNow;
            if (now - _lastRefresh < RefreshInterval)
                return;
            _lastRefresh = now;

            var exfils = _exfils;

            // Initialize or retry if empty (ExfilController may not be ready immediately)
            if (exfils.Count == 0 && _initAttempts < MaxInitAttempts)
            {
                _initAttempts++;
                Init();
                exfils = _exfils;

                if (exfils.Count > 0)
                    Log.WriteLine($"[ExfilManager] Initialized {exfils.Count} exfils on attempt {_initAttempts}");
            }

            if (exfils.Count == 0)
                return;

            // Scatter-read status for all exfils in a single DMA round-trip
            using var map = ScatterReadMap.Get();
            var round1 = map.AddRound();

            for (int ix = 0; ix < exfils.Count; ix++)
            {
                int i = ix;
                var exfil = exfils[i];
                round1[i].AddEntry<int>(0, exfil.StatusAddr);
                round1[i].Callbacks += index =>
                {
                    if (index.TryGetResult<int>(0, out var status))
                        exfil.Update(status);
                };
            }

            map.Execute();
        }

        /// <summary>
        /// Reads exfil arrays from the ExfilController — PMC/Scav array + Secret array.
        /// </summary>
        private void Init()
        {
            var list = new List<Exfil>();

            try
            {
                if (!Memory.TryReadPtr(_lgw + Offsets.ClientLocalGameWorld.ExfilController, out var exfilController, false)
                    || exfilController == 0)
                {
                    return;
                }

                // Read PMC or Scav exfil array
                var arrayOffset = _isPmc
                    ? Offsets.ExfilController.ExfiltrationPointArray
                    : Offsets.ExfilController.ScavExfiltrationPointArray;

                ReadExfilArray(exfilController + arrayOffset, _isPmc, list);

                // Read secret exfil array (always PMC-style)
                ReadExfilArray(exfilController + Offsets.ExfilController.SecretExfiltrationPointArray, true, list);

                _exfils = list;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[ExfilManager] Init failed: {ex.Message}");
                _exfils = list;
            }
        }

        /// <summary>
        /// Reads an IL2CPP array of exfil pointers and creates <see cref="Exfil"/> objects.
        /// IL2CPP Array: [0x18] = count, [0x20..] = elements.
        /// </summary>
        private void ReadExfilArray(ulong arrayPtrAddr, bool isPmc, List<Exfil> list)
        {
            if (!Memory.TryReadPtr(arrayPtrAddr, out var arrayPtr, false) || arrayPtr == 0)
                return;

            var count = Memory.ReadValue<int>(arrayPtr + 0x18, false);
            if (count <= 0 || count > 64)
                return;

            for (int i = 0; i < count; i++)
            {
                try
                {
                    if (!Memory.TryReadPtr(arrayPtr + 0x20 + (ulong)(i * 8), out var exfilAddr, false)
                        || exfilAddr == 0)
                        continue;

                    var exfil = new Exfil(exfilAddr, isPmc, _mapId);

                    if (exfil.Position == Vector3.Zero)
                    {
                        Log.Write(AppLogLevel.Debug, $"[ExfilManager] Skipped exfil '{exfil.Name}' — zero position");
                        continue;
                    }

                    list.Add(exfil);
                    Log.Write(AppLogLevel.Debug, $"[ExfilManager] Loaded exfil: '{exfil.Name}' @ {exfil.Position}");
                }
                catch (Exception ex)
                {
                    Log.Write(AppLogLevel.Debug, $"[ExfilManager] Failed to read exfil[{i}]: {ex.Message}");
                }
            }
        }
    }
}
