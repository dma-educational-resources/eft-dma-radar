namespace eft_dma_radar.Common.Unity.LowLevel.Types
{
    public interface IMonoType
    {
        /// <summary>
        /// Data for this Mono object. May include padding for Mono interop.
        /// </summary>
        Span<byte> Data { get; }

        /// <summary>
        /// Convert the object to a RemoteBytes object and persists it to Remote Memory.
        /// Incurs a Memory Write.
        /// </summary>
        /// <returns></returns>
        RemoteBytes ToRemoteBytes();
    }
}
