using eft_dma_radar.Silk.Misc.Pools;

namespace eft_dma_radar.Silk.DMA.ScatterAPI
{
    /// <summary>
    /// Single scatter read index. Contains multiple typed read entries keyed by int ID.
    /// </summary>
    public sealed class ScatterReadIndex : IPooledObject<ScatterReadIndex>
    {
        internal Dictionary<int, IScatterEntry> Entries { get; } = new();
        public Action<ScatterReadIndex>? Callbacks { get; set; }

        [Obsolete("Rent via IPooledObject<ScatterReadIndex>.Rent().")]
        public ScatterReadIndex() { }

        internal void ExecuteCallback()
        {
            try { Callbacks?.Invoke(this); }
            catch { }
        }

        public ScatterReadEntry<T> AddEntry<T>(int id, ulong address, int cb = 0)
        {
            var entry = ScatterReadEntry<T>.Get(address, cb);
            Entries.Add(id, entry);
            return entry;
        }

        public bool TryGetResult<TOut>(int id, out TOut result)
        {
            if (Entries.TryGetValue(id, out var e) && e is ScatterReadEntry<TOut> typed && !typed.IsFailed)
            {
                result = typed.Result;
                return true;
            }
            result = default!;
            return false;
        }

        public void Dispose() => IPooledObject<ScatterReadIndex>.Return(this);

        public void SetDefault()
        {
            foreach (var e in Entries.Values)
                e.Dispose();
            Entries.Clear();
            Callbacks = default;
        }
    }
}
