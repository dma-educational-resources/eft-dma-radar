using eft_dma_radar.Silk.Misc.Pools;
using VmmSharpEx.Scatter;

namespace eft_dma_radar.Silk.DMA.ScatterAPI
{
    public interface IScatterEntry : IPooledObject<IScatterEntry>
    {
        ulong Address { get; }
        int CB { get; }
        bool IsFailed { get; set; }
        void ReadResult(VmmScatter scatter);
    }
}
