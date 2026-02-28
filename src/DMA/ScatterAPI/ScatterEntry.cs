namespace eft_dma_radar.Common.DMA.ScatterAPI
{
    public static class ScatterEntry
    {
        public readonly struct PtrEntry
        {
            public readonly IScatterEntry Entry;
            public readonly Func<ulong> Transfer;

            public PtrEntry(IScatterEntry entry, Func<ulong> transfer)
            {
                Entry = entry;
                Transfer = transfer;
            }
        }

        /// <summary>
        /// Creates a scatter entry reading a pointer; caller executes .Transfer() AFTER Memory.ReadScatter(...) finishes.
        /// </summary>
        public static PtrEntry CreatePtr(ulong address)
        {
            ulong local = 0;

            var entry = ScatterReadEntry<ulong>.Get(address, sizeof(ulong));

            entry.ActionOnComplete = e =>
            {
                if (!entry.IsFailed)
                    local = entry.Result;
            };

            Func<ulong> transfer = () => local;

            return new PtrEntry(entry, transfer);
        }
    }
}
