using System.Collections.Frozen;

using eft_dma_radar.Silk.Tarkov.Unity.Collections;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Profile
{
    /// <summary>
    /// Reads the local profile's in-game WishlistManager and exposes a fast
    /// lookup from BSG ID → wishlist group. Mirrors EFT's <c>WishlistManager</c>
    /// at <c>Profile + 0x108</c> with <c>_userItems</c> at <c>+0x28</c>
    /// (<c>Dictionary&lt;MongoID, EWishlistGroup&gt;</c>).
    /// <para>
    /// Groups (<c>EWishlistGroup</c>):
    /// 0 = Quests, 1 = Hideout, 2 = Trading, 3 = Equipment, 4 = Other.
    /// </para>
    /// </summary>
    internal sealed class WishlistManager
    {
        private const int MaxItems = 2048;

        private readonly ulong _profilePtr;
        private readonly Stopwatch _rateLimit = new();

        /// <summary>BSG ID → wishlist group. Rebuilt on each refresh.</summary>
        public FrozenDictionary<string, int> Items { get; private set; } =
            FrozenDictionary<string, int>.Empty;

        public WishlistManager(ulong profilePtr)
        {
            _profilePtr = profilePtr;
            Refresh();
        }

        /// <summary>Fast O(1) check — is this BSG ID on the in-game wishlist?</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(string bsgId) => Items.ContainsKey(bsgId);

        /// <summary>Gets the wishlist group for an item, or -1 if not wishlisted.</summary>
        public int GetGroup(string bsgId) =>
            Items.TryGetValue(bsgId, out var group) ? group : -1;

        /// <summary>
        /// Reload the wishlist from memory. Rate-limited to once every 3 seconds.
        /// Called periodically from the game-world secondary worker.
        /// </summary>
        public void Refresh()
        {
            if (_rateLimit.IsRunning && _rateLimit.Elapsed.TotalSeconds < 3d)
                return;

            try
            {
                RefreshCore();
            }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, "wishlist_refresh",
                    TimeSpan.FromSeconds(30),
                    $"[WishlistManager] Refresh error: {ex.Message}");
            }

            _rateLimit.Restart();
        }

        private void RefreshCore()
        {
            if (!Memory.TryReadPtr(_profilePtr + Offsets.Profile.WishlistManager, out var wishlistMgrPtr)
                || !wishlistMgrPtr.IsValidVirtualAddress())
                return;

            if (!Memory.TryReadPtr(wishlistMgrPtr + Offsets.WishlistManager.Items, out var userItemsPtr)
                || !userItemsPtr.IsValidVirtualAddress())
                return;

            Dictionary<string, int>? next = null;
            try
            {
                using var entries = MemDictionary<Types.MongoID, int>.Get(userItemsPtr, useCache: false);
                int count = entries.Count;
                if (count <= 0 || count > MaxItems)
                    return;

                next = new Dictionary<string, int>(count, StringComparer.Ordinal);
                foreach (var entry in entries)
                {
                    try
                    {
                        var sidPtr = entry.Key.StringID;
                        if (!sidPtr.IsValidVirtualAddress())
                            continue;

                        if (!Memory.TryReadUnityString(sidPtr, out var id) || string.IsNullOrWhiteSpace(id))
                            continue;

                        next[id] = entry.Value;
                    }
                    catch { /* skip bad entry */ }
                }
            }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Debug, "wishlist_dict",
                    TimeSpan.FromSeconds(30),
                    $"[WishlistManager] Dictionary read failed: {ex.Message}");
                return;
            }

            if (next is not null && next.Count > 0)
                Items = next.ToFrozenDictionary(StringComparer.Ordinal);
            else
                Items = FrozenDictionary<string, int>.Empty;
        }
    }
}
