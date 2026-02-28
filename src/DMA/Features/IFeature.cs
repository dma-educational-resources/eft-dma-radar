using System.Collections.Concurrent;

namespace eft_dma_radar.Common.DMA.Features
{
    public interface IFeature
    {
        bool CanRun { get; }
        void OnApply();
        void OnGameStart();
        void OnRaidStart();
        void OnRaidEnd();
        void OnGameStop();

        #region Static Interface
        private static readonly ConcurrentBag<IFeature> _features = new();
        /// <summary>
        /// All Memory Write Features.
        /// </summary>
        public static IEnumerable<IFeature> AllFeatures => _features;

        /// <summary>
        /// Add a feature to the collection.
        /// </summary>
        /// <param name="feature">Feature to add.</param>
        protected static void Register(IFeature feature) => _features.Add(feature);
        #endregion
    }
}
