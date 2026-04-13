using static SDK.Offsets;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Player
{
    /// <summary>
    /// Reads the item in a player's hands from memory and updates player properties.
    /// Called from the registration worker alongside gear refresh.
    /// </summary>
    internal static class HandsManager
    {
        /// <summary>
        /// Cached address of the last item base pointer per player — avoids redundant DB lookups
        /// when the item hasn't changed. Keyed by player base address.
        /// </summary>
        private static readonly ConcurrentDictionary<ulong, ulong> _cachedItemAddr = new();

        /// <summary>
        /// Refreshes the item in a player's hands. Only performs the full read chain if the
        /// held item pointer has changed since the last call.
        /// </summary>
        internal static void Refresh(ulong playerBase, Player player, bool isObserved)
        {
            try
            {
                // Resolve the hands controller address
                ulong handsControllerAddr;
                uint itemOffset;

                if (isObserved)
                {
                    // Observed: playerBase → ObservedPlayerController → HandsController field
                    if (!Memory.TryReadPtr(playerBase + ObservedPlayerView.ObservedPlayerController, out var opc, false)
                        || opc == 0)
                        return;
                    handsControllerAddr = opc + ObservedPlayerController.HandsController;
                    itemOffset = Offsets.ObservedHandsController.ItemInHands;
                }
                else
                {
                    // Client/Local: playerBase → _handsController field
                    handsControllerAddr = playerBase + Offsets.Player._handsController;
                    itemOffset = Offsets.ItemHandsController.Item;
                }

                // Read the actual hands controller pointer, then the item pointer
                if (!Memory.TryReadPtr(handsControllerAddr, out var handsController, false) || handsController == 0)
                    return;
                if (!Memory.TryReadPtr(handsController + itemOffset, out var itemBase, false) || itemBase == 0)
                    return;

                // Fast path — item hasn't changed
                if (_cachedItemAddr.TryGetValue(playerBase, out var cached) && cached == itemBase)
                    return;

                // Item changed — read the new item identity
                _cachedItemAddr[playerBase] = itemBase;
                ReadItem(player, itemBase);
            }
            catch
            {
                // Non-critical — silently reset on failure
                player.InHandsItem = null;
                player.InHandsAmmo = null;
                _cachedItemAddr.TryRemove(playerBase, out _);
            }
        }

        /// <summary>
        /// Reads the BSG item ID from memory, looks it up in the database, and optionally reads chambered ammo.
        /// </summary>
        private static void ReadItem(Player player, ulong itemBase)
        {
            // item → Template → _id (MongoID) → StringID → Unity string
            if (!Memory.TryReadPtr(itemBase + Offsets.LootItem.Template, out var template, false) || template == 0)
            {
                player.InHandsItem = null;
                player.InHandsAmmo = null;
                return;
            }

            var mongoId = Memory.ReadValue<Types.MongoID>(template + ItemTemplate._id, false);
            if (!Memory.TryReadUnityString(mongoId.StringID, out var bsgId) || string.IsNullOrWhiteSpace(bsgId))
            {
                player.InHandsItem = null;
                player.InHandsAmmo = null;
                return;
            }

            player.InHandsItemId = bsgId;

            if (EftDataManager.AllItems.TryGetValue(bsgId, out var dbItem))
            {
                player.InHandsItem = dbItem.ShortName;

                // If weapon, try to read chambered ammo
                bool isWeapon = Array.Exists(dbItem.Categories,
                    static c => c.Equals("Weapon", StringComparison.OrdinalIgnoreCase));
                if (isWeapon)
                    TryReadChamberedAmmo(player, itemBase);
                else
                    player.InHandsAmmo = null;
            }
            else
            {
                // Item not in database — read name from memory as fallback
                if (Memory.TryReadPtr(template + ItemTemplate.ShortName, out var namePtr, false)
                    && Memory.TryReadUnityString(namePtr, out var itemName)
                    && !string.IsNullOrWhiteSpace(itemName))
                {
                    player.InHandsItem = itemName.Trim();
                }
                else
                {
                    player.InHandsItem = "Item";
                }
                player.InHandsAmmo = null;
            }
        }

        /// <summary>
        /// Reads the ammo in the weapon's chamber.
        /// Chain: item → Chambers (List) → first slot → ContainedItem → Template → _id
        /// </summary>
        private static void TryReadChamberedAmmo(Player player, ulong weaponBase)
        {
            try
            {
                if (!Memory.TryReadPtr(weaponBase + LootItemWeapon.Chambers, out var chambers, false) || chambers == 0)
                {
                    player.InHandsAmmo = null;
                    return;
                }

                // First chamber slot: Chambers array element[0] — IL2CPP array data starts at 0x20
                if (!Memory.TryReadPtr(chambers + 0x20 + 0 * 0x8, out var slotPtr, false) || slotPtr == 0)
                {
                    player.InHandsAmmo = null;
                    return;
                }

                if (!Memory.TryReadPtr(slotPtr + Slot.ContainedItem, out var ammoItem, false) || ammoItem == 0)
                {
                    player.InHandsAmmo = null;
                    return;
                }

                if (!Memory.TryReadPtr(ammoItem + Offsets.LootItem.Template, out var ammoTemplate, false) || ammoTemplate == 0)
                {
                    player.InHandsAmmo = null;
                    return;
                }

                var ammoMongoId = Memory.ReadValue<Types.MongoID>(ammoTemplate + ItemTemplate._id, false);
                if (Memory.TryReadUnityString(ammoMongoId.StringID, out var ammoId)
                    && !string.IsNullOrWhiteSpace(ammoId)
                    && EftDataManager.AllItems.TryGetValue(ammoId, out var ammoDbItem))
                {
                    player.InHandsAmmo = ammoDbItem.ShortName;
                }
                else
                {
                    player.InHandsAmmo = null;
                }
            }
            catch
            {
                player.InHandsAmmo = null;
            }
        }

        /// <summary>
        /// Clears cached data for a player that has been removed.
        /// </summary>
        internal static void ClearCache(ulong playerBase)
        {
            _cachedItemAddr.TryRemove(playerBase, out _);
        }
    }
}
