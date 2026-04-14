using eft_dma_radar.Silk.DMA.ScatterAPI;

namespace eft_dma_radar.Silk.DMA.Features
{
    public interface IMemWriteFeature : IFeature
    {
        /// <summary>Apply the feature by queuing scatter-write entries. Must not throw.</summary>
        void TryApply(ScatterWriteHandle writes);
    }
}
