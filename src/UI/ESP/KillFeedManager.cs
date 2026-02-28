using static eft_dma_radar.Tarkov.EFTPlayer.Player;

namespace eft_dma_radar.UI.ESP
{
    namespace eft_dma_radar.UI.ESP
    {
    public static class KillfeedManager
    {
        private const int MAX_ENTRIES = 5;
        private static readonly List<KillfeedEntry> _entries = new(MAX_ENTRIES);
    
        public static IReadOnlyList<KillfeedEntry> Entries => _entries;
    
        public static void Push(
            string killer,
            string victim,
            string weapon,
            PlayerType side,
            string ammo,
            string level)
        {
            // Shift existing entries DOWN
            for (int i = 0; i < _entries.Count; i++)
                _entries[i].Index++;
    
            // Insert newest at top
            _entries.Insert(0, new KillfeedEntry
            {
                Killer = killer,
                Victim = victim,
                Weapon = weapon,
                Side = side,
                Ammo = ammo,
                Level = level,
                Index = 0
            });
    
            // Clamp size
            if (_entries.Count > MAX_ENTRIES)
                _entries.RemoveAt(_entries.Count - 1);
        }
    
        public static void Reset()
        {
            _entries.Clear();
        }
    }

    }
    public sealed class KillfeedEntry
    {
        public string Killer;
        public string Victim;
        public string Weapon;
        public PlayerType Side;
        public string Ammo;
        public string Level;

        // Assigned when pushed
        internal int Index;
    }

}