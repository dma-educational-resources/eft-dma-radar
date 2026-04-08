using System.Collections.Frozen;
using eft_dma_radar.Silk.Tarkov.GameWorld.Loot;
using eft_dma_radar.Silk.Tarkov.Unity;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Player
{
    /// <summary>
    /// Reads player equipment from memory and builds gear data.
    /// Called from the registration worker for each active player.
    /// </summary>
    internal static class GearManager
    {
        #region Special Item IDs

        private static readonly FrozenSet<string> ThermalIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "5c110624d174af029e69734c",
                "6478641c19d732620e045e17",
                "609bab8b455afd752b2e6138",
                "63fc44e2429a8a166c7f61e6",
                "5d1b5e94d7ad1a2b865a96b0",
                "606f2696f2cb2e02a42aceb1",
                "5a1eaa87fcdbcb001865f75e"
            }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenSet<string> NvgIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "5c066e3a0db834001b7353f0",
                "5c0696830db834001d23f5da",
                "5c0558060db834001b735271",
                "57235b6f24597759bf5a30f1",
                "5b3b6e495acfc4330140bd88",
                "5a7c74b3e899ef0014332c29"
            }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenSet<string> SkipSlots =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Compass",
                "ArmBand"
            }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        private const string SecureSlot = "SecuredContainer";

        // C# array: count at offset 0x18, data starts at offset 0x20
        private const uint ArrayCountOffset = 0x18;
        private const uint ArrayDataOffset = 0x20;

        // Maximum recursion depth for nested mod slots (safety guard)
        private const int MaxRecursionDepth = 8;

        #endregion

        /// <summary>
        /// Reads all equipment slots for a player and updates gear properties on the <see cref="Player"/> instance.
        /// </summary>
        /// <param name="playerBase">Base address of the player/observed player view.</param>
        /// <param name="player">The player to update.</param>
        /// <param name="isObserved">Whether this is an observed player (different offset chain).</param>
        internal static void Refresh(ulong playerBase, Player player, bool isObserved)
        {
            try
            {
                if (!TryReadEquipmentSlots(playerBase, isObserved, out var slotsPtr, out var slotPtrs))
                    return;

                BuildGear(player, slotsPtr, slotPtrs);
            }
            catch
            {
                // Non-fatal — skip this tick
            }
        }

        /// <summary>
        /// Walks the inventory controller → inventory → equipment → slots pointer chain
        /// and reads all slot pointers from the C# array.
        /// </summary>
        private static bool TryReadEquipmentSlots(
            ulong playerBase,
            bool isObserved,
            out ulong slotsPtr,
            out ulong[] slotPtrs)
        {
            slotsPtr = 0;
            slotPtrs = [];

            // Step 1: Get InventoryController address (different path for observed vs client players)
            ulong invController;
            if (isObserved)
            {
                // ObservedPlayerView → ObservedPlayerController → InventoryController
                if (!Memory.TryReadPtr(playerBase + Offsets.ObservedPlayerView.ObservedPlayerController, out var obsController)
                    || !obsController.IsValidVirtualAddress())
                    return false;

                if (!Memory.TryReadPtr(obsController + Offsets.ObservedPlayerController.InventoryController, out invController)
                    || !invController.IsValidVirtualAddress())
                    return false;
            }
            else
            {
                // ClientPlayer → _inventoryController
                if (!Memory.TryReadPtr(playerBase + Offsets.Player._inventoryController, out invController)
                    || !invController.IsValidVirtualAddress())
                    return false;
            }

            // Step 2: InventoryController → Inventory → Equipment → Slots array
            if (!Memory.TryReadPtr(invController + Offsets.InventoryController.Inventory, out var inventory)
                || !inventory.IsValidVirtualAddress())
                return false;

            if (!Memory.TryReadPtr(inventory + Offsets.Inventory.Equipment, out var equipment)
                || !equipment.IsValidVirtualAddress())
                return false;

            if (!Memory.TryReadPtr(equipment + Offsets.Equipment.Slots, out slotsPtr)
                || !slotsPtr.IsValidVirtualAddress())
                return false;

            // Step 3: Read C# array (count at +0x18, data at +0x20)
            if (!Memory.TryReadValue<int>(slotsPtr + ArrayCountOffset, out var count) || count < 1 || count > 64)
                return false;

            try
            {
                slotPtrs = Memory.ReadArray<ulong>(slotsPtr + ArrayDataOffset, count, false);
            }
            catch
            {
                return false;
            }

            return slotPtrs.Length > 0;
        }

        /// <summary>
        /// Iterates equipment slots, resolves item templates, builds gear dictionary and computes totals.
        /// Also attempts to read ProfileId from the player's alive dogtag for identity resolution.
        /// </summary>
        private static void BuildGear(Player player, ulong slotsPtr, ulong[] slotPtrs)
        {
            var gear = new Dictionary<string, GearItem>(slotPtrs.Length, StringComparer.OrdinalIgnoreCase);
            int totalValue = 0;
            bool hasNvg = false, hasThermal = false;
            bool needsProfileId = player.IsHuman && player.ProfileId is null;

            foreach (var slotPtr in slotPtrs)
            {
                if (!slotPtr.IsValidVirtualAddress())
                    continue;

                try
                {
                    // Read slot name
                    if (!Memory.TryReadPtr(slotPtr + Offsets.Slot.ID, out var namePtr)
                        || !namePtr.IsValidVirtualAddress())
                        continue;

                    if (!Memory.TryReadUnityString(namePtr, out var slotName) || slotName is null)
                        continue;

                    if (SkipSlots.Contains(slotName))
                        continue;

                    // Secure container — only record name, don't add value
                    if (slotName.Equals(SecureSlot, StringComparison.OrdinalIgnoreCase))
                    {
                        ReadSecureContainer(slotPtr, gear);
                        continue;
                    }

                    // Read contained item
                    if (!Memory.TryReadPtr(slotPtr + Offsets.Slot.ContainedItem, out var item)
                        || !item.IsValidVirtualAddress())
                        continue;

                    // Dogtag detection — find BarterOther item for ProfileId resolution
                    if (needsProfileId)
                    {
                        var className = Il2CppClass.ReadName(item);
                        if (className is not null && className.Equals("BarterOther", StringComparison.Ordinal))
                        {
                            if (TryResolveProfileId(item, player))
                                needsProfileId = false;
                            continue; // Dogtag slots have no gear value
                        }
                    }

                    // Resolve BSG ID via template → MongoID
                    if (!TryReadBsgId(item, out var bsgId))
                        continue;

                    // Look up in item database
                    if (EftDataManager.AllItems.TryGetValue(bsgId, out var marketItem))
                    {
                        gear[slotName] = new GearItem
                        {
                            Long = marketItem.Name,
                            Short = marketItem.ShortName,
                            Price = marketItem.BestPrice
                        };
                        totalValue += marketItem.BestPrice;

                        // Check special flags on the item itself
                        if (!hasNvg && NvgIds.Contains(bsgId)) hasNvg = true;
                        if (!hasThermal && ThermalIds.Contains(bsgId)) hasThermal = true;
                    }

                    // Recurse into weapons/headwear for mods (thermal scopes, NVGs, etc.)
                    if (slotName is "FirstPrimaryWeapon" or "SecondPrimaryWeapon" or "Holster" or "Headwear")
                    {
                        RecurseModSlots(item, ref totalValue, ref hasNvg, ref hasThermal, depth: 0);
                    }
                }
                catch
                {
                    // Skip individual slot failures
                }
            }

            // Atomic update — all properties set together so the UI never sees partial state
            player.Equipment = gear.Count > 0
                ? gear.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase)
                : FrozenDictionary<string, GearItem>.Empty;
            player.GearValue = totalValue;
            player.HasNVG = hasNvg;
            player.HasThermal = hasThermal;
        }

        /// <summary>
        /// Reads the secure container slot and adds it to gear (name only, no value contribution).
        /// </summary>
        private static void ReadSecureContainer(ulong slotPtr, Dictionary<string, GearItem> gear)
        {
            try
            {
                if (!Memory.TryReadPtr(slotPtr + Offsets.Slot.ContainedItem, out var item)
                    || !item.IsValidVirtualAddress())
                    return;

                if (!TryReadBsgId(item, out var bsgId))
                    return;

                if (EftDataManager.AllItems.TryGetValue(bsgId, out var entry))
                {
                    gear[SecureSlot] = new GearItem
                    {
                        Long = entry.Name,
                        Short = entry.ShortName,
                        Price = 0 // Secure containers don't count toward gear value
                    };
                }
            }
            catch { }
        }

        #region Alive Dogtag — ProfileId Resolution

        /// <summary>
        /// Reads the ProfileId from a player's alive dogtag (BarterOther item).
        /// <para>
        /// Chain: BarterOther → DogtagComponent (offset 0x80) → ProfileId (offset 0x28)
        /// </para>
        /// The alive dogtag only contains the player's own ProfileId — nickname and accountId
        /// fields are empty. To resolve the real name, the ProfileId must be matched against
        /// corpse dogtag data in <see cref="DogtagCache"/>.
        /// </summary>
        private static bool TryResolveProfileId(ulong barterOtherAddr, Player player)
        {
            try
            {
                if (!Memory.TryReadPtr(barterOtherAddr + Offsets.BarterOtherOffsets.Dogtag, out var dogtag)
                    || !dogtag.IsValidVirtualAddress())
                    return false;

                if (!Memory.TryReadPtr(dogtag + Offsets.DogtagComponent.ProfileId, out var profileIdPtr)
                    || !profileIdPtr.IsValidVirtualAddress())
                    return false;

                if (!Memory.TryReadUnityString(profileIdPtr, out var profileId)
                    || string.IsNullOrWhiteSpace(profileId))
                    return false;

                player.ProfileId = profileId;
                Log.WriteLine($"[GearManager] Resolved ProfileId for {player}: {profileId}");

                // Try to resolve name immediately from the corpse dogtag cache
                DogtagCache.TryApplyIdentity(player);

                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        /// <summary>
        /// Recursively walks mod slots (LootItemMod → Slots) to find nested items
        /// like thermal scopes, NVGs mounted on headwear, etc.
        /// </summary>
        private static void RecurseModSlots(
            ulong lootItemBase,
            ref int totalValue,
            ref bool hasNvg,
            ref bool hasThermal,
            int depth)
        {
            if (depth >= MaxRecursionDepth)
                return;

            try
            {
                if (!Memory.TryReadPtr(lootItemBase + Offsets.LootItemMod.Slots, out var modSlotsPtr)
                    || !modSlotsPtr.IsValidVirtualAddress())
                    return;

                if (!Memory.TryReadValue<int>(modSlotsPtr + ArrayCountOffset, out var count)
                    || count < 1 || count > 64)
                    return;

                ulong[] modSlots;
                try
                {
                    modSlots = Memory.ReadArray<ulong>(modSlotsPtr + ArrayDataOffset, count, false);
                }
                catch
                {
                    return;
                }

                foreach (var modSlotPtr in modSlots)
                {
                    if (!modSlotPtr.IsValidVirtualAddress())
                        continue;

                    try
                    {
                        if (!Memory.TryReadPtr(modSlotPtr + Offsets.Slot.ContainedItem, out var modItem)
                            || !modItem.IsValidVirtualAddress())
                            continue;

                        if (!TryReadBsgId(modItem, out var modBsgId))
                            continue;

                        if (EftDataManager.AllItems.TryGetValue(modBsgId, out var modEntry))
                        {
                            totalValue += modEntry.BestPrice;

                            if (!hasNvg && NvgIds.Contains(modBsgId)) hasNvg = true;
                            if (!hasThermal && ThermalIds.Contains(modBsgId)) hasThermal = true;
                        }

                        // Continue recursing into nested mods
                        RecurseModSlots(modItem, ref totalValue, ref hasNvg, ref hasThermal, depth + 1);
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Reads a BSG item ID from a loot item's template MongoID.
        /// </summary>
        private static bool TryReadBsgId(ulong itemAddr, out string bsgId)
        {
            bsgId = string.Empty;

            if (!Memory.TryReadPtr(itemAddr + Offsets.LootItem.Template, out var template)
                || !template.IsValidVirtualAddress())
                return false;

            if (!Memory.TryReadValue<Types.MongoID>(template + Offsets.ItemTemplate._id, out var mongoId))
                return false;

            if (!Memory.TryReadUnityString(mongoId.StringID, out var id) || id is null)
                return false;

            bsgId = id;
            return true;
        }
    }
}
