using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.Misc.Pools;
using System.Buffers;

namespace eft_dma_radar.Common.DMA.ScatterAPI
{
    /// <summary>
    /// Defines a Scatter Read Round. Each round will execute a single scatter read. If you have reads that
    /// are dependent on previous reads (chained pointers for example), you may need multiple rounds.
    /// </summary>
    public sealed class ScatterReadRound : IPooledObject<ScatterReadRound>
    {
        private readonly Dictionary<int, ScatterReadIndex> _indexes = new();
        public bool UseCache { get; private set; }

        [Obsolete("You must rent this object via IPooledObject!")]
        public ScatterReadRound() { }

        /// <summary>
        /// Get a Scatter Read Round from the Object Pool.
        /// </summary>
        /// <returns>Rented ScatterReadRound instance.</returns>
        public static ScatterReadRound Get(bool usecache)
        {
            var rd = IPooledObject<ScatterReadRound>.Rent();
            rd.UseCache = usecache;
            return rd;
        }

        /// <summary>
        /// Returns the requested ScatterReadIndex.
        /// </summary>
        /// <param name="index">Index to retrieve.</param>
        /// <returns>ScatterReadIndex object.</returns>
        public ScatterReadIndex this[int index]
        {
            get
            {
                if (_indexes.TryGetValue(index, out var existing))
                    return existing;
                return _indexes[index] = IPooledObject<ScatterReadIndex>.Rent();
            }
        }

        /// <summary>
        /// ** Internal use only do not use **
        /// </summary>
        internal void Run()
        {
            int totalEntries = 0;
            foreach (var idx in _indexes.Values)
                totalEntries += idx.Entries.Count;

            if (totalEntries == 0)
                return;

            var entries = ArrayPool<IScatterEntry>.Shared.Rent(totalEntries);
            try
            {
                int pos = 0;
                foreach (var idx in _indexes.Values)
                {
                    foreach (var entry in idx.Entries.Values)
                        entries[pos++] = entry;
                }

                Memory.ReadScatter(entries, totalEntries, UseCache);
                foreach (var index in _indexes)
                    index.Value.ExecuteCallback();
            }
            finally
            {
                Array.Clear(entries, 0, totalEntries);
                ArrayPool<IScatterEntry>.Shared.Return(entries, false);
            }
        }

        public void Dispose()
        {
            IPooledObject<ScatterReadRound>.Return(this);
        }

        public void SetDefault()
        {
            foreach (var index in _indexes.Values)
                index.Dispose();
            _indexes.Clear();
            UseCache = default;
        }
    }
}
