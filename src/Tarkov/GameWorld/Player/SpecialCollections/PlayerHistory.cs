using eft_dma_radar;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.Common.Misc;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace eft_dma_radar.Tarkov.EFTPlayer.SpecialCollections
{
    /// <summary>
    /// Wrapper class to manage Player History.
    /// Thread Safe.
    /// </summary>
    public sealed class PlayerHistory
    {
        private readonly object _lock = new object();
        private readonly HashSet<Player> _logged = new();
        private readonly BindingList<PlayerHistoryEntry> _entries = new();

        /// <summary>
        /// Event that fires when entries are added, updated, or cleared
        /// </summary>
        public event EventHandler EntriesChanged;

        /// <summary>
        /// Adds an entry to the Player History.
        /// </summary>
        /// <param name="player">Player to add/update.</param>
        public void AddOrUpdate(Player player)
        {
            try
            {
                if (player.IsHumanOther)
                {
                    var entry = new PlayerHistoryEntry(player);
                    var changed = false;

                    lock (_lock)
                    {
                        var existingEntryById = _entries.FirstOrDefault(x => !string.IsNullOrEmpty(x.ID) && x.ID == entry.ID);

                        if (existingEntryById != null)
                        {
                            existingEntryById.UpdateLastSeen();
                            changed = true;

                            _logged.Add(player);
                        }
                        else if (_logged.Add(player))
                        {
                            _entries.Insert(0, entry);
                            changed = true;
                        }
                        else
                        {
                            var oldEntry = _entries.FirstOrDefault(x => x.Player == player);

                            if (oldEntry != null)
                            {
                                oldEntry.UpdateLastSeen();
                                changed = true;
                            }
                        }
                    }

                    if (changed)
                        EntriesChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[PlayerHistory] Error in AddOrUpdate: {ex.Message}");
            }
        }

        /// <summary>
        /// Resets the Player History state for a new raid.
        /// Does not clear existing entries, but clears the HashSet that prevents players from being added multiple times.
        /// </summary>
        public void Reset()
        {
            try
            {
                lock (_lock)
                {
                    var existingIds = new HashSet<string>();
                    foreach (var entry in _entries)
                    {
                        if (!string.IsNullOrEmpty(entry.ID))
                            existingIds.Add(entry.ID);
                    }

                    _logged.Clear();
                }

                EntriesChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[PlayerHistory] Error in Reset: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears all entries from the player history
        /// </summary>
        public void Clear()
        {
            try
            {
                lock (_lock)
                {
                    _logged.Clear();
                    _entries.Clear();
                }

                EntriesChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[PlayerHistory] Error in Clear: {ex.Message}");
            }
        }

        /// <summary>
        /// Get a reference to the backing collection.
        /// UNSAFE! Should only be done for binding purposes.
        /// </summary>
        /// <returns>List reference.</returns>
        public BindingList<PlayerHistoryEntry> GetReferenceUnsafe() => _entries;
    }
}