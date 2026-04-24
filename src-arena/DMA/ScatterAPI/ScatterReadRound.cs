namespace eft_dma_radar.Arena.DMA.ScatterAPI
{
    /// <summary>
    /// Single scatter read round. Each round executes one scatter read batch.
    /// Chain multiple rounds for dependent reads (pointer chains).
    /// </summary>
    public sealed class ScatterReadRound : IPooledObject<ScatterReadRound>
    {
        private readonly Dictionary<int, ScatterReadIndex> _indexes = new();
        public bool UseCache { get; private set; }

        [Obsolete("Rent via IPooledObject<ScatterReadRound>.Rent().")]
        public ScatterReadRound() { }

        public static ScatterReadRound Get(bool useCache)
        {
            var rd = IPooledObject<ScatterReadRound>.Rent();
            rd.UseCache = useCache;
            return rd;
        }

        public ScatterReadIndex this[int index]
        {
            get
            {
                if (_indexes.TryGetValue(index, out var existing))
                    return existing;
                return _indexes[index] = IPooledObject<ScatterReadIndex>.Rent();
            }
        }

        internal void Run()
        {
            int total = 0;
            foreach (var idx in _indexes.Values)
                total += idx.Entries.Count;

            if (total == 0) return;

            var entries = ArrayPool<IScatterEntry>.Shared.Rent(total);
            try
            {
                int pos = 0;
                foreach (var idx in _indexes.Values)
                    foreach (var entry in idx.Entries.Values)
                        entries[pos++] = entry;

                Memory.ReadScatter(entries, total, UseCache);

                foreach (var idx in _indexes.Values)
                    idx.ExecuteCallback();
            }
            finally
            {
                Array.Clear(entries, 0, total);
                ArrayPool<IScatterEntry>.Shared.Return(entries, false);
            }
        }

        public void Dispose() => IPooledObject<ScatterReadRound>.Return(this);

        public void SetDefault()
        {
            foreach (var idx in _indexes.Values)
                idx.Dispose();
            _indexes.Clear();
            UseCache = default;
        }
    }
}
