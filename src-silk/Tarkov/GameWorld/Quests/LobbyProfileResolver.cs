using eft_dma_radar.Silk.Tarkov.Unity;
using eft_dma_radar.Silk.Tarkov.Unity.IL2CPP;

using SilkUtils = eft_dma_radar.Silk.Misc.Utils;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Quests
{
    /// <summary>
    /// Shared resolver for the lobby player profile pointer.
    /// Chain: GOM → TarkovApplication (klass scan) → _menuOperation → _profile.
    /// <para>
    /// Used by <see cref="LobbyQuestReader"/> and
    /// <see cref="QuestPlanner.QuestPlannerWorker"/> to avoid duplicating the
    /// scan logic. Each caller owns its own cached object-class pointer to keep
    /// cache lifetimes independent.
    /// </para>
    /// </summary>
    internal static class LobbyProfileResolver
    {
        private static ulong _cachedKlassPtr;

        /// <summary>
        /// Resolves the lobby profile pointer. Returns 0 on failure. Never throws.
        /// </summary>
        /// <param name="cachedObjectClass">
        /// Caller-owned cache slot for the TarkovApplication behaviour pointer.
        /// Reset to 0 to force a re-scan (e.g. on game stop).
        /// </param>
        public static ulong Resolve(ref ulong cachedObjectClass)
        {
            try
            {
                var gomAddr = Memory.GOM;
                if (!SilkUtils.IsValidVirtualAddress(gomAddr))
                    return 0;

                if (!SilkUtils.IsValidVirtualAddress(cachedObjectClass))
                {
                    var gom = GOM.Get(gomAddr);

                    // Primary: klass-pointer-based GOM scan (fast)
                    var klassPtr = _cachedKlassPtr;
                    if (!SilkUtils.IsValidVirtualAddress(klassPtr))
                    {
                        klassPtr = Il2CppDumper.ResolveKlassByTypeIndex(
                            Offsets.Special.TarkovApplication_TypeIndex);
                        if (SilkUtils.IsValidVirtualAddress(klassPtr))
                            _cachedKlassPtr = klassPtr;
                    }

                    ulong objectClass = 0;
                    if (SilkUtils.IsValidVirtualAddress(klassPtr))
                        objectClass = gom.FindBehaviourByKlassPtr(klassPtr);

                    // Fallback: class name scan
                    if (!SilkUtils.IsValidVirtualAddress(objectClass))
                        objectClass = gom.FindBehaviourByClassName("TarkovApplication");

                    if (!SilkUtils.IsValidVirtualAddress(objectClass))
                        return 0;

                    cachedObjectClass = objectClass;
                }

                if (!Memory.TryReadPtr(cachedObjectClass + Offsets.TarkovApplication._menuOperation, out var menuOp, false)
                    || menuOp == 0)
                    return 0;

                if (!Memory.TryReadPtr(menuOp + Offsets.MainMenuShowOperation._profile, out var profile, false)
                    || profile == 0)
                    return 0;

                return SilkUtils.IsValidVirtualAddress(profile) ? profile : 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
