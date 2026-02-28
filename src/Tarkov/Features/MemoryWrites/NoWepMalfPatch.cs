using System;
using System.Collections.Generic;
using eft_dma_radar.Tarkov.Features;
using eft_dma_shared.Common.Unity;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.Unity.Collections;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites.Patches
{
    /// <summary>
    /// IL2CPP-safe replacement for the old Mono-based NoWeaponMalfunctions patch.
    ///
    /// Instead of patching GetMalfunctionState(), this enforces
    /// weapon-template invariants via memory writes.
    ///
    /// Supports both primary and secondary weapons.
    /// </summary>
    public sealed class NoWepMalfPatch : MemWriteFeature<NoWepMalfPatch>
    {
        /// <summary>
        /// Config toggle.
        /// </summary>
        public override bool Enabled
        {
            get => MemWrites.Config.NoWeaponMalfunctions;
            set => MemWrites.Config.NoWeaponMalfunctions = value;
        }

        /// <summary>
        /// Template writes persist, no need to spam.
        /// </summary>
        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// Tracks weapon templates already patched this raid.
        /// Prevents redundant writes when swapping weapons.
        /// </summary>
        private readonly HashSet<ulong> _patchedTemplates = new();

        /// <summary>
        /// Apply IL2CPP-safe "no weapon malfunctions" logic.
        /// </summary>
        public override void TryApply(ScatterWriteHandle writes)
        {
            if (!Enabled)
                return;

            if (Memory.LocalPlayer is not LocalPlayer lp)
                return;

            // Collect weapon templates from:
            // - currently held weapon
            // - primary
            // - secondary (if present)
            foreach (var template in EnumerateWeaponTemplates(lp))
            {
                if (!template.IsValidVirtualAddress())
                    continue;

                // Already processed this template
                if (!_patchedTemplates.Add(template))
                    continue;

                DisableTemplateFlag(template + Offsets.WeaponTemplate.AllowJam, writes);
                DisableTemplateFlag(template + Offsets.WeaponTemplate.AllowFeed, writes);
                DisableTemplateFlag(template + Offsets.WeaponTemplate.AllowMisfire, writes);
                DisableTemplateFlag(template + Offsets.WeaponTemplate.AllowSlide, writes);
            }
        }

        /// <summary>
        /// Reset state when leaving raid / game.
        /// </summary>
        public override void OnGameStop()
        {
            _patchedTemplates.Clear();
        }

        /// <summary>
        /// Enumerates all relevant weapon templates for the local player.
        /// Covers held weapon + primary + secondary.
        /// </summary>
        private static IEnumerable<ulong> EnumerateWeaponTemplates(LocalPlayer lp)
        {
            // 1) Currently held weapon
            ulong heldTemplate = 0;
            try
            {
                if (lp.Firearm?.HandsController is { Item2: true })
                {
                    var hands = lp.Firearm.HandsController.Item1;
                    if (hands.IsValidVirtualAddress())
                    {
                        var item = Memory.ReadPtr(hands + Offsets.ItemHandsController.Item);
                        var template = Memory.ReadPtr(item + Offsets.LootItem.Template);
                        if (template.IsValidVirtualAddress())
                            heldTemplate = template;

                            //XMLogging.WriteLine($"NoWepMalfPatch: Held weapon template addr=0x{heldTemplate:X}");
                    }
                }
            }
            catch { }

            if (heldTemplate != 0)
                yield return heldTemplate;

            // 2) Inventory primary / secondary weapons
            List<ulong> inventoryTemplates = new();
            try
            {
                var inventoryController = Memory.ReadPtr(lp.InventoryControllerAddr);
                var inventory = Memory.ReadPtr(inventoryController + Offsets.InventoryController.Inventory);
                var equipment = Memory.ReadPtr(inventory + Offsets.Inventory.Equipment);
                var slotsPtr = Memory.ReadPtr(equipment + Offsets.Equipment.Slots);

                using var slots = MemArray<ulong>.Get(slotsPtr);
                foreach (var slot in slots)
                {
                    ulong slotTemplate = 0;
                    try
                    {
                        var namePtr = Memory.ReadPtr(slot + Offsets.Slot.ID);
                        var name = Memory.ReadUnityString(namePtr);

                        // Only weapon slots
                        if (name != "FirstPrimaryWeapon" &&
                            name != "SecondPrimaryWeapon" &&
                            name != "Holster")
                            continue;

                        var item = Memory.ReadPtr(slot + Offsets.Slot.ContainedItem);
                        if (!item.IsValidVirtualAddress())
                            continue;

                        var template = Memory.ReadPtr(item + Offsets.LootItem.Template);
                        if (template.IsValidVirtualAddress())
                            slotTemplate = template;

                            //XMLogging.WriteLine($"NoWepMalfPatch: Inventory slot '{name}' weapon template addr=0x{slotTemplate:X}");
                    }
                    catch { }

                    if (slotTemplate != 0)
                        inventoryTemplates.Add(slotTemplate);
                }
            }
            catch { }

            foreach (var template in inventoryTemplates)
                yield return template;
        }

        /// <summary>
        /// Disable a weapon-template boolean flag if currently enabled.
        /// </summary>
        private static void DisableTemplateFlag(
            ulong addr,
            ScatterWriteHandle writes)
        {
            if (!addr.IsValidVirtualAddress())
                return;

            // Guard read to avoid dirty writes
            if (Memory.ReadValue<bool>(addr))
            {
                writes.AddValueEntry(addr, false);
            }
        }
    }
}
