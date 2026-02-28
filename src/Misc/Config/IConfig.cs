using eft_dma_radar.Common.Unity.LowLevel;
using eft_dma_radar.Tarkov.Features.MemoryWrites.Chams;

namespace eft_dma_radar.Common.Misc.Config
{
    public interface IConfig
    {
        LowLevelCache LowLevelCache { get; }
        ChamsConfig ChamsConfig { get; }
        bool MemWritesEnabled { get; }
        int MonitorWidth { get; }
        int MonitorHeight { get; }

        void Save();
        Task SaveAsync();
    }
}
