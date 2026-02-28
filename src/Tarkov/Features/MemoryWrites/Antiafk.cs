
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
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
                var unityBase = Memory.UnityBase;
                var gomAddr   = GameObjectManager.GetAddr(unityBase);
                var gom       = GameObjectManager.Get(gomAddr);

                // ? DO NOT USE GAMEOBJECT NAME
                ulong tarkovApplication =
                    gom.FindBehaviourByClassName("TarkovApplication");

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
                    XMLogging.WriteLine($"Game Version: {Memory.ReadUnityString(_alphaVersionText)}");
            }
            catch (Exception ex)
            {
                throw new Exception($"ERROR Setting Anti-AFK", ex);
            }
        }
    }
}
