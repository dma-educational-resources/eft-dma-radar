using eft_dma_radar.Silk.Tarkov.GameWorld.Quests;
using eft_dma_radar.Silk.Tarkov.Unity;
using eft_dma_radar.Silk.Tarkov.Unity.IL2CPP;

using SilkUtils = eft_dma_radar.Silk.Misc.Utils;

namespace eft_dma_radar.Silk.DMA
{
    /// <summary>
    /// Background lobby quest reader that polls the player's profile from TarkovApplication
    /// when NOT in a raid. Provides quest data to the Quest Panel while in the main menu/lobby.
    /// <para>
    /// Profile resolution chain:
    /// GOM → TarkovApplication (klass scan) → _menuOperation → _profile → QuestsData
    /// </para>
    /// Automatically suspends while in raid (the in-raid QuestManager takes over).
    /// </summary>
    internal static class LobbyQuestReader
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

        private static Thread? _thread;
        private static volatile bool _shutdown;

        // ── Cached klass pointer for TarkovApplication ───────────────────────
        private static ulong _cachedKlassPtr;
        private static ulong _cachedObjectClass;

        /// <summary>
        /// The lobby QuestManager, valid when connected but not in a raid.
        /// Null when in raid (the in-raid QuestManager is used instead) or disconnected.
        /// </summary>
        public static QuestManager? QuestManager { get; private set; }

        /// <summary>
        /// Start the lobby quest reader background thread.
        /// Called once from <see cref="Memory.ModuleInit"/>.
        /// </summary>
        internal static void Start()
        {
            if (_thread is not null)
                return;

            _shutdown = false;
            _thread = new Thread(Worker)
            {
                IsBackground = true,
                Name = "LobbyQuestReader"
            };
            _thread.Start();
        }

        /// <summary>
        /// Signal the background thread to stop.
        /// </summary>
        internal static void Stop()
        {
            _shutdown = true;
        }

        /// <summary>
        /// Invalidate cached pointers. Called on game stop / process detach.
        /// </summary>
        internal static void InvalidateCache()
        {
            _cachedObjectClass = 0;
            // Don't clear _cachedKlassPtr — valid for game process lifetime
            QuestManager = null;
        }

        private static void Worker()
        {
            Log.WriteLine("[LobbyQuestReader] Thread started.");

            while (!_shutdown)
            {
                try
                {
                    Tick();
                }
                catch (Exception ex)
                {
                    Log.WriteRateLimited(AppLogLevel.Warning, "lobby_quest_err", TimeSpan.FromSeconds(30),
                        $"[LobbyQuestReader] Error: {ex.Message}");
                }

                Thread.Sleep((int)PollInterval.TotalMilliseconds);
            }

            Log.WriteLine("[LobbyQuestReader] Thread exiting.");
        }

        private static void Tick()
        {
            // Only run when game is connected but NOT in a raid or hideout
            if (!Memory.Ready || Memory.InRaid || Memory.InHideout)
            {
                // Clear lobby data when entering a raid (in-raid QuestManager takes over)
                if (Memory.InRaid)
                    QuestManager = null;
                return;
            }

            // Resolve profile from TarkovApplication
            var profilePtr = GetLobbyProfile();
            if (profilePtr == 0)
                return;

            // Create or refresh QuestManager
            var qm = QuestManager;
            if (qm is null)
            {
                qm = new QuestManager(profilePtr, "");
                QuestManager = qm;
                Log.WriteLine($"[LobbyQuestReader] QuestManager created — profile @ 0x{profilePtr:X}, " +
                    $"{qm.ActiveQuests.Count} active quests");
            }
            else
            {
                qm.Refresh();
            }
        }

        /// <summary>
        /// Resolves the player Profile pointer from TarkovApplication in the lobby.
        /// Chain: GOM → TarkovApplication → _menuOperation → _profile
        /// Returns 0 on failure — never throws.
        /// </summary>
        private static ulong GetLobbyProfile()
        {
            try
            {
                var gomAddr = Memory.GOM;
                if (!SilkUtils.IsValidVirtualAddress(gomAddr))
                    return 0;

                var gom = GOM.Get(gomAddr);
                ulong objectClass = _cachedObjectClass;

                // Try cached object class first
                if (!SilkUtils.IsValidVirtualAddress(objectClass))
                {
                    // Primary: klass-pointer-based GOM scan (fast)
                    var klassPtr = _cachedKlassPtr;
                    if (!SilkUtils.IsValidVirtualAddress(klassPtr))
                    {
                        klassPtr = Il2CppDumper.ResolveKlassByTypeIndex(
                            Offsets.Special.TarkovApplication_TypeIndex);
                        if (SilkUtils.IsValidVirtualAddress(klassPtr))
                            _cachedKlassPtr = klassPtr;
                    }

                    if (SilkUtils.IsValidVirtualAddress(klassPtr))
                        objectClass = gom.FindBehaviourByKlassPtr(klassPtr);

                    // Fallback: class name scan
                    if (!SilkUtils.IsValidVirtualAddress(objectClass))
                        objectClass = gom.FindBehaviourByClassName("TarkovApplication");

                    if (SilkUtils.IsValidVirtualAddress(objectClass))
                        _cachedObjectClass = objectClass;
                    else
                        return 0;
                }

                // TarkovApplication → _menuOperation
                if (!Memory.TryReadPtr(objectClass + Offsets.TarkovApplication._menuOperation, out var menuOp, false)
                    || menuOp == 0)
                    return 0;

                // _menuOperation → _profile
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
