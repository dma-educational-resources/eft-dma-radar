namespace eft_dma_radar.Silk.Tarkov.GameWorld.Player.Plugins
{
    /// <summary>
    /// Reads held-weapon firearm state that <see cref="HandsManager"/> does NOT already read:
    /// <list type="bullet">
    ///   <item>Fire mode (single / burst / auto / semi / doublesemi)</item>
    ///   <item>Current magazine ammo count and capacity</item>
    /// </list>
    /// Chambered ammo shortname is owned by <see cref="HandsManager"/> — this class does not touch <c>InHandsAmmo</c>.
    /// </summary>
    internal static class FirearmManager
    {
        /// <summary>
        /// Refresh firearm details. Skips entirely if the player is not holding a weapon
        /// (determined by <see cref="Player.InHandsItem"/> / <see cref="Player.InHandsAmmo"/> —
        /// HandsManager sets <c>InHandsAmmo</c> only for weapons).
        /// </summary>
        internal static void Refresh(ulong playerBase, Player player)
        {
            // Fast skip: if the held item isn't a weapon, there are no mags/chambers/fire-mode to read.
            // HandsManager sets IsWeaponInHands based on the item DB classification, so this reliably
            // short-circuits melee/medkit/grenade/food items without walking any pointer chains.
            if (!player.IsWeaponInHands)
            {
                ClearFirearmState(player);
                return;
            }

            try
            {
                // Reuse HandsManager's cached weapon pointer — avoids re-walking the hands chain.
                ulong weaponBase = HandsManager.GetCachedItem(playerBase);
                if (weaponBase == 0)
                {
                    ClearFirearmState(player);
                    return;
                }

                player.FireMode = TryReadFireMode(weaponBase);

                if (TryReadMagazineCounts(weaponBase, out int current, out int capacity))
                {
                    player.AmmoInMag = current;
                    player.MagCapacity = capacity;
                }
                else
                {
                    player.AmmoInMag = -1;
                    player.MagCapacity = -1;
                }
            }
            catch
            {
                ClearFirearmState(player);
            }
        }

        private static void ClearFirearmState(Player player)
        {
            player.FireMode = null;
            player.AmmoInMag = -1;
            player.MagCapacity = -1;
        }

        private static string? TryReadFireMode(ulong weaponBase)
        {
            if (!Memory.TryReadPtr(weaponBase + Offsets.LootItemWeapon.FireMode, out var fireModePtr, false) || fireModePtr == 0)
                return null;
            if (!Memory.TryReadValue<byte>(fireModePtr + Offsets.FireModeComponent.FireMode, out var raw, false))
                return null;
            return raw switch
            {
                0 => "single",
                1 => "burst",
                2 => "auto",
                3 => "semi",
                4 => "doublesemi",
                _ => null,
            };
        }

        /// <summary>
        /// Reads total current loaded rounds and maximum capacity for the weapon's magazine (or internal chambers).
        /// Mirrors WPF <c>FirearmManager.CheckMag</c> but simplified: counts only, no ammo mapping
        /// (HandsManager already owns <see cref="Player.InHandsAmmo"/>).
        /// </summary>
        private static bool TryReadMagazineCounts(ulong weaponBase, out int current, out int capacity)
        {
            current = 0;
            capacity = 0;

            int chamberSlotCount = 0;

            // Chambers (always present — some shotguns use this as their mag).
            if (Memory.TryReadPtr(weaponBase + Offsets.LootItemWeapon.Chambers, out var chambersPtr, false) && chambersPtr != 0)
            {
                if (TryReadChamberArray(chambersPtr, out int chambers, out int loaded))
                {
                    chamberSlotCount = chambers;
                    capacity += chambers;
                    current += loaded;
                }
            }

            // Magazine slot cache.
            if (Memory.TryReadPtr(weaponBase + Offsets.LootItemWeapon._magSlotCache, out var magSlot, false) && magSlot != 0
                && Memory.TryReadPtr(magSlot + Offsets.Slot.ContainedItem, out var magItem, false) && magItem != 0)
            {
                // Inspect mag slots (revolvers expose chambers here).
                if (Memory.TryReadPtr(magItem + Offsets.LootItemMod.Slots, out var magChambersPtr, false) && magChambersPtr != 0
                    && TryReadChamberArray(magChambersPtr, out int magChambers, out int magLoaded)
                    && magChambers > 0)
                {
                    capacity += magChambers;
                    current += magLoaded;
                }
                else if (Memory.TryReadPtr(magItem + Offsets.LootItemMagazine.Cartridges, out var cartridges, false) && cartridges != 0)
                {
                    if (chamberSlotCount > 0)
                        capacity -= chamberSlotCount; // chamber slot is not part of mag capacity
                    if (Memory.TryReadValue<int>(cartridges + Offsets.StackSlot.MaxCount, out var slotMax, false))
                        capacity += slotMax;

                    if (Memory.TryReadPtr(cartridges + Offsets.StackSlot._items, out var stackListPtr, false) && stackListPtr != 0)
                    {
                        // Unity List<T>._items at +0x10, size at +0x18.
                        if (Memory.TryReadPtr(stackListPtr + 0x10, out var stackArr, false)
                            && Memory.TryReadValue<int>(stackListPtr + 0x18, out var stackCount, false)
                            && stackArr != 0 && stackCount > 0)
                        {
                            int safeCount = Math.Min(stackCount, 8);
                            for (int i = 0; i < safeCount; i++)
                            {
                                if (!Memory.TryReadPtr(stackArr + 0x20 + (ulong)i * 0x8, out var stack, false) || stack == 0)
                                    continue;
                                if (Memory.TryReadValue<int>(stack + Offsets.MagazineClass.StackObjectsCount, out var cnt, false))
                                    current += cnt;
                            }
                        }
                    }
                }
            }

            return capacity > 0;
        }

        /// <summary>
        /// Reads a chamber array at <paramref name="arrayPtr"/>. Returns slot count + loaded count.
        /// Layout: IL2CPP array with element pointers at +0x20 + i*8 (size at +0x18).
        /// </summary>
        private static bool TryReadChamberArray(ulong arrayPtr, out int count, out int loaded)
        {
            count = 0;
            loaded = 0;

            if (!Memory.TryReadValue<int>(arrayPtr + 0x18, out var arrCount, false) || arrCount <= 0 || arrCount > 16)
                return false;

            count = arrCount;
            for (int i = 0; i < arrCount; i++)
            {
                if (!Memory.TryReadPtr(arrayPtr + 0x20 + (ulong)i * 0x8, out var slotPtr, false) || slotPtr == 0)
                    continue;
                if (!Memory.TryReadPtr(slotPtr + Offsets.Slot.ContainedItem, out var slotItem, false) || slotItem == 0)
                    continue;
                loaded++;
            }
            return true;
        }
    }
}
