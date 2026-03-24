using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Common.Unity.Collections;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.Unity.IL2CPP;
using static SDK.Offsets;

// Work in progress

namespace eft_dma_radar.Tarkov.Hideout
{
    /// <summary>
    /// A single item resolved from the hideout stash.
    /// </summary>
    public sealed record StashItem(
        string Id,
        string Name,
        long   TraderPrice,
        long   FleaPrice,
        int    StackCount)
    {
        /// <summary>Best sell value for this stack (max of trader vs flea × stack count).</summary>
        public long BestPrice  => Math.Max(TraderPrice, FleaPrice) * StackCount;
        /// <summary>True when flea beats trader for this item.</summary>
        public bool SellOnFlea => FleaPrice > TraderPrice;
    }

    /// <summary>
    /// Manages reading the hideout stash via the IL2CPP GOM.
    /// Confirmed chain: HideoutArea(+0xA8) → HideoutAreaStashController(+0x10)
    ///   → OfflineInventoryController(+0x100) → Inventory(+0x20) → Grid[](+0x78)
    /// </summary>
    public sealed class HideoutManager
    {
        private const string HideoutAreaClassName = "HideoutArea";

        // Confirmed pointer-chain offsets
        private const uint OffStashCtrl  = 0xA8;  // HideoutArea._StashController
        private const uint OffInvCtrl    = 0x10;  // HideoutAreaStashController → OfflineInventoryController
        private const uint OffInventory  = 0x100; // OfflineInventoryController._Inventory
        private const uint OffStash      = 0x20;  // Inventory.Stash  (CompoundItem)
        private const uint OffGrids      = 0x78;  // CompoundItem.Grids (Grid[])

        /// <summary>HideoutArea behaviour address.</summary>
        public ulong Base { get; private set; }

        /// <summary>Grid[] array pointer (0 until <see cref="TryFind"/> succeeds).</summary>
        public ulong StashGridPtr { get; private set; }

        /// <summary>Items populated by the last <see cref="Refresh"/> call.</summary>
        public IReadOnlyList<StashItem> Items { get; private set; } = [];

        /// <summary>Sum of the best sell price (trader vs flea) for every item in the stash.</summary>
        public long TotalBestValue   => Items.Sum(i => i.BestPrice);
        /// <summary>Sum of trader prices for every item in the stash.</summary>
        public long TotalTraderValue => Items.Sum(i => i.TraderPrice * i.StackCount);
        /// <summary>Sum of flea prices for every item in the stash.</summary>
        public long TotalFleaValue   => Items.Sum(i => i.FleaPrice   * i.StackCount);

        public bool IsValid => Base.IsValidVirtualAddress() && StashGridPtr.IsValidVirtualAddress();

        /// <summary>
        /// Called once during application startup (after EftDataManager is ready).
        /// Runs on a background thread — finds the stash and performs the initial item read.
        /// </summary>
        public void InitAsync() =>
            Task.Run(() =>
            {
                try
                {
                    if (TryFind())
                        Refresh();
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"[HideoutManager] InitAsync error: {ex.Message}");
                }
            });

        /// <summary>
        /// Scans the GOM for the HideoutArea component and walks the pointer chain
        /// to the stash Grid[] array. Returns true when the grid is reachable.
        /// </summary>
        public bool TryFind()
        {
            try
            {
                var unityBase = Memory.UnityBase;
                if (unityBase == 0)
                    return false;

                var gomAddr = GameObjectManager.GetAddr(unityBase);
                var gom     = GameObjectManager.Get(gomAddr);

                var behaviour = gom.FindBehaviourByClassName(HideoutAreaClassName);
                if (!behaviour.IsValidVirtualAddress())
                {
                    XMLogging.WriteLine($"[HideoutManager] \"{HideoutAreaClassName}\" not found in GOM.");
                    return false;
                }

                var stashCtrl = Memory.ReadPtr(behaviour + OffStashCtrl);
                if (!stashCtrl.IsValidVirtualAddress()) return false;

                var invCtrl = Memory.ReadPtr(stashCtrl + OffInvCtrl);
                if (!invCtrl.IsValidVirtualAddress()) return false;

                var inventory = Memory.ReadPtr(invCtrl + OffInventory);
                if (!inventory.IsValidVirtualAddress()) return false;

                var stash = Memory.ReadPtr(inventory + OffStash);
                if (!stash.IsValidVirtualAddress()) return false;

                var gridsPtr = Memory.ReadPtr(stash + OffGrids);
                if (!gridsPtr.IsValidVirtualAddress()) return false;

                Base         = behaviour;
                StashGridPtr = gridsPtr;
                XMLogging.WriteLine($"[HideoutManager] Ready. Base=0x{Base:X} StashGridPtr=0x{StashGridPtr:X}");
                return true;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[HideoutManager] TryFind error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Reads all items from the stash grids, resolves each against
        /// <see cref="EftDataManager.AllItems"/>, and stores results in <see cref="Items"/>.
        /// </summary>
        public void Refresh()
        {
            if (!IsValid)
                return;

            try
            {
                var items = new List<StashItem>();
                GetItemsInGrid(StashGridPtr, items);
                Items = items;
                XMLogging.WriteLine(
                    $"[HideoutManager] Refresh: {Items.Count} item(s) | " +
                    $"best ₽{TotalBestValue:N0} | trader ₽{TotalTraderValue:N0} | flea ₽{TotalFleaValue:N0}");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[HideoutManager] Refresh error: {ex.Message}");
            }
        }

        /// <summary>
        /// Triggered by the UI Refresh button.
        /// Pulls fresh market prices from Tarkov.Dev, re-scans the stash pointer chain if
        /// needed, then re-reads all item stacks. Returns a short status message for the UI.
        /// </summary>
        public async Task<string> RefreshAsync()
        {
            try
            {
                // 1. Pull fresh market data from the API
                XMLogging.WriteLine("[HideoutManager] RefreshAsync: updating market data...");
                bool marketUpdated = await EftDataManager.UpdateDataFileAsync();
                XMLogging.WriteLine(marketUpdated
                    ? "[HideoutManager] Market data updated."
                    : "[HideoutManager] Market data update skipped/failed — using cached prices.");

                // 2. Re-validate pointer chain (game might have reloaded)
                if (!IsValid && !TryFind())
                    return "Stash not found — are you in the hideout?";

                // 3. Re-read all stash items with the (possibly refreshed) prices
                Refresh();

                return $"{Items.Count} items · best ₽{TotalBestValue / 1_000_000.0:0.##}M" +
                       (marketUpdated ? " (prices updated)" : " (cached prices)");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[HideoutManager] RefreshAsync error: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Recursively walks a Grid[] array, resolves each item via <see cref="EftDataManager.AllItems"/>
        /// and appends matched items (with stack count and prices) to <paramref name="results"/>.
        /// </summary>
        private static void GetItemsInGrid(ulong gridsArrayPtr, List<StashItem> results, int recurseDepth = 0)
        {
            if (!gridsArrayPtr.IsValidVirtualAddress()) return;
            if (recurseDepth++ > 3) return;

            using var gridsArray = MemArray<ulong>.Get(gridsArrayPtr);
            foreach (var grid in gridsArray)
            {
                try
                {
                    var containedItems = Memory.ReadPtr(grid + Grids.ContainedItems);
                    var itemListPtr    = Memory.ReadPtr(containedItems + GridContainedItems.Items);
                    using var itemList = MemList<ulong>.Get(itemListPtr);

                    foreach (var item in itemList)
                        try
                        {
                            var template = Memory.ReadPtr(item + LootItem.Template);
                            var idMongo  = Memory.ReadValue<SDK.Types.MongoID>(template + ItemTemplate._id);
                            var id       = Memory.ReadUnityString(idMongo.StringID);

                            if (EftDataManager.AllItems.TryGetValue(id, out var entry))
                            {
                                var stackCount = Memory.ReadValue<int>(item + LootItem.StackObjectsCount);
                                results.Add(new StashItem(
                                    Id:          entry.BsgId,
                                    Name:        entry.Name,
                                    TraderPrice: entry.TraderPrice,
                                    FleaPrice:   entry.FleaPrice,
                                    StackCount:  Math.Max(1, stackCount)));
                            }

                            // recurse into nested containers (bags, cases, etc.)
                            var childGridsPtr = Memory.ReadValue<ulong>(item + LootItemMod.Grids);
                            GetItemsInGrid(childGridsPtr, results, recurseDepth);
                        }
                        catch { }
                }
                catch { }
            }
        }

        /// <summary>
        /// Clears all cached pointers and items, forcing full re-discovery on the next call.
        /// </summary>
        public void Reset()
        {
            Base         = 0;
            StashGridPtr = 0;
            Items        = [];
        }
    }
}
