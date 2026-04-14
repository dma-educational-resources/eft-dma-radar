namespace eft_dma_radar.Silk.DMA.Features
{
    public interface IFeature
    {
        bool CanRun { get; }
        void OnApply();
        void OnGameStart();
        void OnRaidStart();
        void OnRaidEnd();
        void OnGameStop();

        #region Static Registry
        private static readonly System.Collections.Concurrent.ConcurrentBag<IFeature> _features = new();

        /// <summary>All registered feature instances.</summary>
        public static IEnumerable<IFeature> AllFeatures => _features;

        /// <summary>Register a feature instance.</summary>
        protected static void Register(IFeature feature) => _features.Add(feature);
        #endregion
    }
}
