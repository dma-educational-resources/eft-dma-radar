namespace eft_dma_radar.Silk.Tarkov.GameWorld.Quests
{
    /// <summary>
    /// Background lobby quest reader that polls the player's profile from TarkovApplication
    /// when NOT in a raid. Provides quest data to the Quest Panel while in the main menu/lobby.
    /// <para>
    /// Profile resolution is delegated to <see cref="LobbyProfileResolver"/>.
    /// </para>
    /// Automatically suspends while in raid (the in-raid QuestManager takes over).
    /// </summary>
    internal static class LobbyQuestReader
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

        private static Thread? _thread;
        private static volatile bool _shutdown;

        /// <summary>Caller-owned TarkovApplication behaviour cache slot.</summary>
        private static ulong _cachedObjectClass;

        /// <summary>
        /// The lobby QuestManager, valid when connected but not in a raid.
        /// Null when in raid (the in-raid QuestManager is used instead) or disconnected.
        /// </summary>
        public static QuestManager? QuestManager { get; private set; }

        /// <summary>
        /// Start the lobby quest reader background thread.
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

        internal static void Stop() => _shutdown = true;

        /// <summary>Invalidate cached pointers. Called on game stop / process detach.</summary>
        internal static void InvalidateCache()
        {
            _cachedObjectClass = 0;
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
            if (!Memory.Ready || Memory.InRaid || Memory.InHideout)
            {
                if (Memory.InRaid)
                    QuestManager = null;
                return;
            }

            var profilePtr = LobbyProfileResolver.Resolve(ref _cachedObjectClass);
            if (profilePtr == 0)
                return;

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
    }
}
