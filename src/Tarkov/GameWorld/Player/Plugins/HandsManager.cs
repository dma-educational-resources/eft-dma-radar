using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Common.Unity.Collections;
using System.Collections.Frozen;
using static eft_dma_radar.Tarkov.EFTPlayer.Plugins.FirearmManager;

namespace eft_dma_radar.Tarkov.EFTPlayer.Plugins
{
    public sealed class HandsManager
    {
        private readonly Player _parent;

        private string _ammo;
        private LootItem _cachedItem;
        private ulong _cached = 0x0;
        /// <summary>
        /// Item in hands currently (Short Name).
        /// Also contains ammo/thermal info.
        /// </summary>
        public string CurrentItem
        {
            get
            {
                var item = _cachedItem?.ShortName;
                if (item is null)
                    return "--";

                return item;
            }
        }

        public string CurrentAmmo => _ammo;

        public string CurrentItemId
        {
            get
            {
                return _cachedItem?.ID ?? "null";
            }
        }

        public HandsManager(Player player)
        {
            _parent = player;
        }

        public float ZoomLevel { get; set; }

        /// <summary>
        /// Check if item in player's hands has changed.
        /// </summary>
        public void Refresh()
        {
            try
            {
                var handsController = Memory.ReadPtr(_parent.HandsControllerAddr); // or FirearmController
                var itemBase = Memory.ReadPtr(handsController + (_parent is ClientPlayer ? Offsets.ItemHandsController.Item : Offsets.ObservedHandsController.ItemInHands));

                if (itemBase != _cached)
                {
                    _cachedItem = null;
                    _ammo = null;

                    var itemTemplate = Memory.ReadPtr(itemBase + Offsets.LootItem.Template);
                    var itemIDPtr = Memory.ReadValue<Types.MongoID>(itemTemplate + Offsets.ItemTemplate._id);
                    var itemID = Memory.ReadUnityString(itemIDPtr.StringID);
                    if (EftDataManager.AllItems.TryGetValue(itemID, out var heldItem)) // Item exists in DB
                    {
                        _cachedItem = new LootItem(heldItem);
                    }
                    else // Item doesn't exist in DB , use name from game memory
                    {
                        var itemNamePtr = Memory.ReadPtr(itemTemplate + Offsets.ItemTemplate.ShortName);
                        var itemName = Memory.ReadUnityString(itemNamePtr)?.Trim();
                        if (string.IsNullOrEmpty(itemName))
                            itemName = "Item";

                        if (itemName.Contains("nsv_utes"))
                        {
                            itemName = "NSV Utyos";
                        }
                        else if (itemName.Contains("ags30_30"))
                        {
                            itemName = "AGS-30";
                            _ammo = "VOG-30";
                        }
                        else if (itemName.Contains("izhmash_rpk16"))
                            itemName = "RPK-16";

                        _cachedItem = new("NULL", itemName);
                    }
                    _cached = itemBase;
                }

                if (_cachedItem?.IsWeapon ?? false)
                {
                    try
                    {
                        var chambers = Memory.ReadPtr(itemBase + Offsets.LootItemWeapon.Chambers);
                        var slotPtr = Memory.ReadPtr(chambers + MemList<byte>.ArrStartOffset + 0 * 0x8); // One in the chamber ;)
                        var slotItem = Memory.ReadPtr(slotPtr + Offsets.Slot.ContainedItem);
                        var ammoTemplate = Memory.ReadPtr(slotItem + Offsets.LootItem.Template);
                        var ammoIDPtr = Memory.ReadValue<Types.MongoID>(ammoTemplate + Offsets.ItemTemplate._id);
                        var ammoID = Memory.ReadUnityString(ammoIDPtr.StringID);

                        if (EftDataManager.AllItems.TryGetValue(ammoID, out var ammo))
                            _ammo = ammo?.ShortName;
                    }
                    catch // gun doesnt have a chamber
                    {
                        var ammoTemplate_ = MagazineManager.GetAmmoTemplateFromWeapon(itemBase);
                        var ammoIdPtr = Memory.ReadValue<Types.MongoID>(ammoTemplate_ + Offsets.ItemTemplate._id);
                        var ammoId = Memory.ReadUnityString(ammoIdPtr.StringID);

                        if (EftDataManager.AllItems.TryGetValue(ammoId, out var ammo))
                            _ammo = ammo?.ShortName;
                    }
                }
            }
            catch
            {
                _cached = 0x0;
            }
        }
    }
}
