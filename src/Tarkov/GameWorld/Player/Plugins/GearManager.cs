using System.Collections.Frozen;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Common.Unity.Collections;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Tarkov.API;

namespace eft_dma_radar.Tarkov.EFTPlayer.Plugins
{
    public sealed class GearManager
    {
        private static readonly FrozenSet<string> THERMAL_IDS =
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

        private static readonly FrozenSet<string> NVG_IDS =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "5c066e3a0db834001b7353f0",
                "5c0696830db834001d23f5da",
                "5c0558060db834001b735271",
                "57235b6f24597759bf5a30f1",
                "5b3b6e495acfc4330140bd88",
                "5a7c74b3e899ef0014332c29"
            }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenSet<string> UBGL_IDS =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "62e7e7bbe6da9612f743f1e0",
                "6357c98711fb55120211f7e1"
            }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenSet<string> _skipSlots =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Compass",
                "ArmBand"
            }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        private const string SECURE_SLOT = "SecuredContainer";
        private readonly bool _isPMC;
        private readonly Player _player;
        private IReadOnlyDictionary<string, ulong> _slots =
            FrozenDictionary<string, ulong>.Empty;        
        public GearManager(Player player, bool isPMC = false)
        {
            _player = player;
            _isPMC = isPMC;

            Equipment = FrozenDictionary<string, GearItem>.Empty;
            Loot = Array.Empty<LootItem>();
        }

        private IReadOnlyDictionary<string, ulong> Slots { get; set; }


        public IReadOnlyDictionary<string, GearItem> Equipment { get; private set; }
        public IReadOnlyList<LootItem> Loot { get; private set; }

        public bool HasQuestItems => Loot?.Any(x => x.IsQuestCondition) ?? false;
        public bool HasNVG { get; private set; }
        public bool HasThermal { get; private set; }
        public bool HasUBGL { get; private set; }
        public int Value { get; private set; }
        private ulong _equipmentSlotsPtr;

        public void Refresh()
        {
            try
            {
                if (!TryBuildSlots())
                    return;

                BuildGear();
            }
            catch
            {
                // NON-FATAL — skip this frame
            }
        }
        private bool TryBuildSlots()
        {
            ulong invController = Memory.ReadPtr(_player.InventoryControllerAddr);
            if (!invController.IsValidVirtualAddress())
                return false;

            ulong inventory = Memory.ReadPtr(invController + Offsets.InventoryController.Inventory);
            if (!inventory.IsValidVirtualAddress())
                return false;

            ulong equipment = Memory.ReadPtr(inventory + Offsets.Inventory.Equipment);
            if (!equipment.IsValidVirtualAddress())
                return false;

            _equipmentSlotsPtr = Memory.ReadPtr(equipment + Offsets.Equipment.Slots);

            ulong slotsPtr = Memory.ReadPtr(equipment + Offsets.Equipment.Slots);
            if (!slotsPtr.IsValidVirtualAddress())
                return false;

            var dict = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);

            using var slots = MemArray<ulong>.Get(slotsPtr);
            foreach (var slotPtr in slots)
            {
                try
                {
                    var namePtr = Memory.ReadPtr(slotPtr + Offsets.Slot.ID);
                    if (!namePtr.IsValidVirtualAddress())
                        continue;

                    var name = Memory.ReadUnityString(namePtr);
                    if (_skipSlots.Contains(name))
                        continue;

                    dict[name] = slotPtr;
                }
                catch { }
            }
            TryResolveAliveDogtagProfileId(_player, _equipmentSlotsPtr);  

            _slots = dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
            return _slots.Count > 0;
        }

        // ─────────────────────────────────────────────

        private void BuildGear()
        {
            var loot = new List<LootItem>();
            var gear = new Dictionary<string, GearItem>(StringComparer.OrdinalIgnoreCase);

            foreach (var slot in _slots)
            {
                try
                {
                    if (_isPMC && slot.Key == "Scabbard")
                        continue;

                    if (slot.Key == SECURE_SLOT)
                    {
                        HandleSecureContainer(slot.Value, gear);
                        continue;
                    }

                    var item = Memory.ReadPtr(slot.Value + Offsets.Slot.ContainedItem);
                    if (!item.IsValidVirtualAddress())
                        continue;

                    var template = Memory.ReadPtr(item + Offsets.LootItem.Template);
                    var idPtr = Memory.ReadValue<Types.MongoID>(template + Offsets.ItemTemplate._id);
                    var id = Memory.ReadUnityString(idPtr.StringID);

                    if (EftDataManager.AllItems.TryGetValue(id, out var entry))
                        loot.Add(new LootItem(entry));

                    if (slot.Key is "FirstPrimaryWeapon" or "SecondPrimaryWeapon" or "Holster" or "Headwear")
                    {
                        try { RecursePlayerGearSlots(item, loot); }
                        catch { }
                    }

                    if (EftDataManager.AllItems.TryGetValue(id, out var entry2))
                    {
                        gear[slot.Key] = new GearItem
                        {
                            Long = entry2.Name ?? "None",
                            Short = entry2.ShortName ?? "None"
                        };
                    }
                }
                catch { }
            }

            Loot = loot.OrderLoot().ToList();
            Equipment = gear;

            Value = Loot.Sum(x => x.Price);
            HasNVG = Loot.Any(x => NVG_IDS.Contains(x.ID));
            HasThermal = Loot.Any(x => THERMAL_IDS.Contains(x.ID));
            HasUBGL = Loot.Any(x => UBGL_IDS.Contains(x.ID));
        }
        // ============================================================
        // ALIVE DOGTAG → PROFILE ID RESOLUTION
        // ============================================================

        private static void TryResolveAliveDogtagProfileId(Player player, ulong slotsPtr)
        {
            if (!string.IsNullOrEmpty(player.ProfileID))
                return;
            if (player.IsAI)
                return;
        
            ulong barterOther = 0;
        
            using var slots = MemArray<ulong>.Get(slotsPtr);
            foreach (var slotPtr in slots)
            {
                ulong item;
                try
                {
                    item = Memory.ReadPtr(slotPtr + Offsets.Slot.ContainedItem);
                    if (!item.IsValidVirtualAddress())
                        continue;
                }
                catch { continue; }
        
                string className;
                try
                {
                    className = ObjectClass.ReadName(item);
                }
                catch { continue; }
        
                if (!className.Equals("BarterOther", StringComparison.Ordinal))
                    continue;
        
                barterOther = item;
                break;
            }
        
            if (barterOther == 0)
                return;
        
            try
            {
                var dogtag = Memory.ReadPtr(barterOther + Offsets.BarterOtherOffsets.Dogtag);
                if (!dogtag.IsValidVirtualAddress())
                    return;
        
                var profileIdPtr = Memory.ReadPtr(dogtag + Offsets.DogtagComponent.ProfileId);
                if (!profileIdPtr.IsValidVirtualAddress())
                    return;
        
                var profileId = Memory.ReadUnityString(profileIdPtr, 32);
                if (string.IsNullOrWhiteSpace(profileId))
                    return;
        
                // ✅ SET PROFILE ID ONCE
                player.ProfileID = profileId;
        
                XMLogging.WriteLine(
                    $"[GearManager] Resolved ProfileID for {player}: {profileId}");
        
                // ✅ FIRE API INGESTION HERE (ONCE)
                PlayerLookupApiClient.TryResolve(player);
            }
            catch { }
        }
        private static void HandleSecureContainer(
            ulong slotPtr,
            Dictionary<string, GearItem> gear)
        {
            try
            {
                var item = Memory.ReadPtr(slotPtr + Offsets.Slot.ContainedItem);
                if (!item.IsValidVirtualAddress())
                    return;

                var template = Memory.ReadPtr(item + Offsets.LootItem.Template);
                var idPtr = Memory.ReadValue<Types.MongoID>(template + Offsets.ItemTemplate._id);
                var id = Memory.ReadUnityString(idPtr.StringID);

                if (EftDataManager.AllItems.TryGetValue(id, out var entry))
                {
                    gear[SECURE_SLOT] = new GearItem
                    {
                        Long  = entry.Name ?? "Secure Container",
                        Short = entry.ShortName ?? "Secure"
                    };
                }
            }
            catch { }
        }

        public static bool TryGetWeaponTemplateFromSlot(
            ulong slotPtr,
            out ulong weaponTemplate)
        {
            weaponTemplate = 0;

            var item = Memory.ReadPtr(slotPtr + Offsets.Slot.ContainedItem);
            if (!item.IsValidVirtualAddress())
                return false;

            var template = Memory.ReadPtr(item + Offsets.LootItem.Template);
            if (!template.IsValidVirtualAddress())
                return false;

            var idPtr = Memory.ReadValue<Types.MongoID>(template + Offsets.ItemTemplate._id);
            var id = Memory.ReadUnityString(idPtr.StringID, 64);

            if (!EftDataManager.AllItems.TryGetValue(id, out var itemDef))
                return false;

            if (!itemDef.IsWeapon)
                return false;

            weaponTemplate = template;
            return true;
        }

        private static void RecursePlayerGearSlots(
            ulong lootItemBase,
            List<LootItem> loot)
        {
            try
            {
                var slotsPtr = Memory.ReadPtr(lootItemBase + Offsets.LootItemMod.Slots);
                using var slots = MemArray<ulong>.Get(slotsPtr);

                foreach (var slotPtr in slots)
                {
                    try
                    {
                        var item = Memory.ReadPtr(slotPtr + Offsets.Slot.ContainedItem);
                        var template = Memory.ReadPtr(item + Offsets.LootItem.Template);
                        var idPtr = Memory.ReadValue<Types.MongoID>(template + Offsets.ItemTemplate._id);
                        var id = Memory.ReadUnityString(idPtr.StringID);

                        if (EftDataManager.AllItems.TryGetValue(id, out var entry))
                            loot.Add(new LootItem(entry));

                        RecursePlayerGearSlots(item, loot);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
