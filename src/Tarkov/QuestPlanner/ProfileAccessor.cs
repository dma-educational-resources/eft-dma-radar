using static eft_dma_radar.Tarkov.MemoryInterface;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Tarkov.Unity.IL2CPP;
using SDK;

namespace eft_dma_radar.Tarkov.QuestPlanner
{
    /// <summary>
    /// Resolves the player Profile pointer from EFT memory.
    ///
    /// PRIMARY PATH (lobby):
    ///   GOM.FindBehaviourByClassName("TarkovApplication") -> _menuOperation (0x130) -> _profile (0x50)
    ///
    /// FALLBACK PATH (in-raid):
    ///   ClientLocalGameWorld.MainPlayer (0x208) -> Player.Profile (0x900)
    ///
    /// Context: This class reads quest STATUS from the lobby for mission planning.
    /// It is completely independent of QuestManagerV2, which reads quest ZONES
    /// from LocalGameWorld during raids for radar display.
    /// </summary>
    public static class ProfileAccessor
    {
        private static ulong _cachedProfile;
        private static DateTime _lastCacheTime;
        private static readonly TimeSpan _cacheExpiry = TimeSpan.FromSeconds(5);
        private const bool DEBUG_ENABLED = false;

        /// <summary>
        /// Resolves the player Profile pointer.
        /// Tries lobby path first; falls back to in-raid path.
        /// Returns 0 on failure - never throws.
        /// </summary>
        public static ulong GetProfile()
        {
            try
            {
                // Check cache (profile address is stable while in lobby/in-raid)
                if (_cachedProfile != 0 && DateTime.UtcNow - _lastCacheTime < _cacheExpiry)
                {
                    // Verify cached profile is still valid
                    if (IsProfileValid(_cachedProfile))
                        return _cachedProfile;
                }

                // Try lobby path first (works in lobby and during raid)
                var lobbyProfile = GetLobbyProfile();
                if (lobbyProfile != 0)
                {
                    _cachedProfile = lobbyProfile;
                    _lastCacheTime = DateTime.UtcNow;
                    return lobbyProfile;
                }

                // Fallback to in-raid path
                var inRaidProfile = GetInRaidProfile();
                if (inRaidProfile != 0)
                {
                    _cachedProfile = inRaidProfile;
                    _lastCacheTime = DateTime.UtcNow;
                    return inRaidProfile;
                }

                XMLogging.WriteLine("[ProfileAccessor] Could not resolve profile (lobby and in-raid paths both failed)");
                return 0;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[ProfileAccessor] Error getting profile: {ex.Message}");
                _cachedProfile = 0;
                return 0;
            }
        }

        /// <summary>
        /// Gets the player profile while in lobby via TarkovApplication -> MenuOperation -> Profile.
        /// Uses FindBehaviourByClassName for more reliable resolution.
        /// Chain: GOM.FindBehaviourByClassName("TarkovApplication") -> _menuOperation (0x130) -> _profile (0x50)
        /// </summary>
        private static ulong GetLobbyProfile()
        {
            try
            {
                var gom = GameObjectManager.Get(Memory.GOM);
                ulong appInstance = gom.FindBehaviourByClassName("TarkovApplication");
                if (!appInstance.IsValidVirtualAddress()) return 0;

                ulong menuOperation = Memory.ReadPtr(appInstance + Offsets.TarkovApplication._menuOperation);
                if (menuOperation == 0) return 0;

                ulong profile = Memory.ReadPtr(menuOperation + Offsets.MainMenuShowOperation._profile);
                if (!IsProfileValid(profile)) return 0;

                if (DEBUG_ENABLED)
                    XMLogging.WriteLine($"[ProfileAccessor] Lobby profile resolved at 0x{profile:X}");
                return profile;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Gets the player profile from the active LocalGameWorld if in raid.
        /// Chain: GOM["GameWorld"] -> LocalGameWorld -> MainPlayer (0x208) -> Profile (0x900)
        /// </summary>
        private static ulong GetInRaidProfile()
        {
            try
            {
                var gom = GameObjectManager.Get(Memory.GOM);
                ulong gameWorldObj = gom.GetGameObjectByName("GameWorld");
                if (gameWorldObj == 0) return 0;

                ulong localGameWorld = Memory.ReadPtrChain(gameWorldObj, UnityOffsets.GameWorldChain);
                if (!localGameWorld.IsValidVirtualAddress()) return 0;

                ulong mainPlayer = Memory.ReadPtr(localGameWorld + Offsets.ClientLocalGameWorld.MainPlayer);
                if (mainPlayer == 0) return 0;

                ulong profile = Memory.ReadPtr(mainPlayer + Offsets.Player.Profile);
                if (!IsProfileValid(profile)) return 0;

                if (DEBUG_ENABLED)
                    XMLogging.WriteLine($"[ProfileAccessor] In-raid profile resolved at 0x{profile:X}");
                return profile;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Validates that a profile pointer is readable by checking the ID field.
        /// </summary>
        /// <param name="profileAddress">The profile address to validate.</param>
        /// <returns>True if the profile appears valid, false otherwise.</returns>
        public static bool IsProfileValid(ulong profileAddress)
        {
            if (profileAddress == 0 || !profileAddress.IsValidVirtualAddress())
                return false;

            try
            {
                var idPtr = Memory.ReadPtr(profileAddress + Offsets.Profile.Id);
                return idPtr.IsValidVirtualAddress();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Clears cached addresses (call on game detach or state change).
        /// </summary>
        public static void ClearCache()
        {
            _cachedProfile = 0;
        }
    }
}
