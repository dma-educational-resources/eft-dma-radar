namespace eft_dma_radar.Common.DMA.Features
{
    public interface IMemPatchFeature : IFeature
    {
        /// <summary>
        /// Try Apply the MemPatch.
        /// Does not throw.
        /// </summary>
        /// <returns></returns>
        bool TryApply();
    }
}
