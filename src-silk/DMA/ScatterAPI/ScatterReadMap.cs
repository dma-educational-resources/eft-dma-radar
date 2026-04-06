using eft_dma_radar.Silk.Misc.Pools;

namespace eft_dma_radar.Silk.DMA.ScatterAPI
{
    /// <summary>
    /// Provides mapping for a scatter read operation. May contain multiple rounds.
    /// Not thread-safe — keep operations synchronous.
    /// </summary>
    public sealed class ScatterReadMap : IPooledObject<ScatterReadMap>
    {
        private readonly List<ScatterReadRound> _rounds = [];

        /// <summary>Callbacks executed after all rounds complete. Handle exceptions inside!</summary>
        public Action? CompletionCallbacks { get; set; }

        [Obsolete("Rent via IPooledObject<ScatterReadMap>.Rent().")]
        public ScatterReadMap() { }

        public static ScatterReadMap Get() => IPooledObject<ScatterReadMap>.Rent();

        public void Execute()
        {
            if (_rounds.Count == 0) return;
            foreach (var round in _rounds)
                round.Run();
            CompletionCallbacks?.Invoke();
        }

        public ScatterReadRound AddRound(bool useCache = true)
        {
            var round = ScatterReadRound.Get(useCache);
            _rounds.Add(round);
            return round;
        }

        public void Dispose() => IPooledObject<ScatterReadMap>.Return(this);

        public void SetDefault()
        {
            foreach (var r in _rounds)
                r.Dispose();
            _rounds.Clear();
            CompletionCallbacks = default;
        }
    }
}
