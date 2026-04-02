
using eft_dma_radar.DMA.Features;
using eft_dma_radar.Misc;
using eft_dma_radar.Tarkov.Unity.IL2CPP;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class AntiAfk : MemWriteFeature<AntiAfk>
    {
        private const float AFK_DELAY = 604800f; // 1 week
        /// <summary>
        /// Set Anti-Afk.
        /// </summary>
        /// <exception cref="Exception"></exception>
        public void Set()
        {
            try
            {
                ulong tarkovApplication = TarkovApplicationHelper.GetObjectClass();
                tarkovApplication.ThrowIfInvalidVirtualAddress();

                // Continue EXACTLY as before
                ulong menuOperation = Memory.ReadPtr(
                    tarkovApplication + Offsets.TarkovApplication._menuOperation);

                menuOperation.ThrowIfInvalidVirtualAddress();

                ulong afkMonitor = Memory.ReadPtr(
                    menuOperation + Offsets.MainMenuShowOperation._afkMonitor);

                afkMonitor.ThrowIfInvalidVirtualAddress();

                Memory.WriteValue(
                    afkMonitor + Offsets.AfkMonitor.Delay,
                    AFK_DELAY);

                var preloaderui = Memory.ReadPtr(
                menuOperation + 0x60);
                var _alphaVersionText = Memory.ReadPtr(
                preloaderui + 0x110);
                Log.WriteLine($"Game Version: {Memory.ReadUnityString(_alphaVersionText)}");
            }
            catch (Exception ex)
            {
                throw new Exception($"ERROR Setting Anti-AFK", ex);
            }
        }
    }
}
