using eft_dma_radar.Common.Misc.Pools;
using VmmSharpEx.Scatter;

namespace eft_dma_radar.Common.DMA.ScatterAPI
{
    public interface IScatterEntry : IPooledObject<IScatterEntry>
    {
        /// <summary>
        /// Virtual Address to read from.
        /// </summary>
        ulong Address { get; }
        /// <summary>
        /// Count of bytes to read.
        /// </summary>
        int CB { get; }
        /// <summary>
        /// True if this read has failed, otherwise False.
        /// </summary>
        bool IsFailed { get; set; }

        /// <summary>
        /// Extracts the result from the executed native scatter handle.
        /// Called after <see cref="VmmScatter.Execute"/> has completed.
        /// </summary>
        void ReadResult(VmmScatter scatter);
    }
}
