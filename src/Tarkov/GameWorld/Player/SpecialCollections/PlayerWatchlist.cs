using eft_dma_radar.UI.Misc;
using eft_dma_radar.Common.Misc;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace eft_dma_radar.Tarkov.EFTPlayer.SpecialCollections
{
    /// <summary>
    /// Wrapper class to manage Player Watchlist.
    /// Thread Safe.
    /// </summary>
    public sealed class PlayerWatchlist
    {
        private readonly object _lock = new object();
        private readonly SortableBindingList<PlayerWatchlistEntry> _bindingList;

        public event EventHandler EntriesChanged;

        /// <summary>
        /// Entries in this watchlist.
        /// </summary>
        public IReadOnlyDictionary<string, PlayerWatchlistEntry> Entries { get; private set; } = new Dictionary<string, PlayerWatchlistEntry>();

        /// <summary>
        /// Constructor.
        /// </summary>
        public PlayerWatchlist()
        {
            _bindingList = new SortableBindingList<PlayerWatchlistEntry>(new List<PlayerWatchlistEntry>());
            _bindingList.ListChanged += Entries_ListChanged;
            Update();
        }

        private void Entries_ListChanged(object sender, ListChangedEventArgs e) => Update();

        /// <summary>
        /// Update the backing dictionary to reflect the BindingList.
        /// </summary>
        private void Update()
        {
            lock (_lock)
            {
                this.Entries = _bindingList
                    .DistinctBy(x => x.AccountID)
                    .ToDictionary(
                        item => item.AccountID,
                        item => item);
            }

            EntriesChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Manually add an entry into the Player Watchlist.
        /// </summary>
        /// <param name="entry">Entry to add.</param>
        public void ManualAdd(PlayerWatchlistEntry entry)
        {
            try
            {
                if (!string.IsNullOrEmpty(entry.AccountID))
                {
                    lock (_lock)
                    {
                        if (Entries.ContainsKey(entry.AccountID))
                        {
                            var existingEntry = _bindingList.FirstOrDefault(e => e.AccountID == entry.AccountID);
                            if (existingEntry != null)
                                existingEntry.Reason = entry.Reason;
                        }
                        else
                        {
                            _bindingList.Add(entry);
                        }
                    }

                    Update();

                    if (MainWindow.Window?.WatchlistControl != null)
                        MainWindow.Window.WatchlistControl.SaveWatchlistToFile();
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[PlayerWatchlist] Error in ManualAdd: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear all entries from the watchlist.
        /// </summary>
        public void Clear()
        {
            try
            {
                lock (_lock)
                {
                    _bindingList.Clear();
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[PlayerWatchlist] Error in Clear: {ex.Message}");
            }
        }

        /// <summary>
        /// Get a reference to the backing collection.
        /// UNSAFE! Should only be done for binding purposes.
        /// </summary>
        /// <returns>BindingList reference.</returns>
        internal SortableBindingList<PlayerWatchlistEntry> GetReferenceUnsafe() => _bindingList;
    }
}