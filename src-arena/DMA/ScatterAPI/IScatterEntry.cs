using VmmSharpEx.Scatter;

namespace eft_dma_radar.Arena.DMA.ScatterAPI
{
    public interface IScatterEntry : IDisposable
    {
        ulong Address { get; }
        int CB { get; }
        bool IsFailed { get; set; }
        void ReadResult(VmmScatter scatter);
    }
}
