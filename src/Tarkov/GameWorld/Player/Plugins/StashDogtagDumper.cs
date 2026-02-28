using System;
using System.Collections.Generic;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Common.Unity.Collections;
using eft_dma_radar.Tarkov.API;
using eft_dma_radar.Tarkov.Unity.IL2CPP;

namespace eft_dma_radar.Tarkov.EFTPlayer.Plugins
{
    public sealed class MenuStashDogtagDumper
    {
        private const bool EnableDebug = true;

        // per-run dedupe only
        private static readonly object _sync = new();
        private static readonly HashSet<string> _seenProfileIds = new();

        // =========================================================
        // ENTRY
        // =========================================================
        public static void Dump()
        {
            try
            {
                lock (_sync)
                    _seenProfileIds.Clear();

                ulong unityBase = Memory.UnityBase;
                ulong gomAddr   = GameObjectManager.GetAddr(unityBase);
                var   gom       = GameObjectManager.Get(gomAddr);

                ulong tarkovApplication =
                    gom.FindBehaviourByClassName("TarkovApplication");
                tarkovApplication.ThrowIfInvalidVirtualAddress();

                ulong menuOperation = Memory.ReadPtr(
                    tarkovApplication + Offsets.TarkovApplication._menuOperation);
                menuOperation.ThrowIfInvalidVirtualAddress();

                ulong profile = Memory.ReadPtr(
                    menuOperation + Offsets.MainMenuShowOperation._profile);
                profile.ThrowIfInvalidVirtualAddress();

                ulong inventory = Memory.ReadPtr(
                    profile + Offsets.Profile.Inventory);
                inventory.ThrowIfInvalidVirtualAddress();

                ulong stash = Memory.ReadPtr(
                    inventory + Offsets.Inventory.Stash);
                stash.ThrowIfInvalidVirtualAddress();

                ulong grid = Memory.ReadPtr(stash + Offsets.Stash.Grids);
                grid.ThrowIfInvalidVirtualAddress();

                ulong itemCollection = Memory.ReadPtr(
                    grid + Offsets.Grid.ItemCollection);
                itemCollection.ThrowIfInvalidVirtualAddress();

                ulong itemsListPtr = Memory.ReadPtr(
                    itemCollection + Offsets.GridItemCollection.ItemsList);
                itemsListPtr.ThrowIfInvalidVirtualAddress();

                using var items = MemList<ulong>.Get(itemsListPtr);

                foreach (var item in items)
                {
                    if (!item.IsValidVirtualAddress())
                        continue;

                    TryProcessDogtag(item);
                    TryWalkContainer(item, 1);
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[MenuStashDogtag] ERROR: {ex}");
            }
        }

        // =========================================================
        // SAFE CONTAINER WALK
        // =========================================================
        private static void TryWalkContainer(ulong item, int depth)
        {
            if (depth > 8 || !item.IsValidVirtualAddress())
                return;

            ulong gridsPtr;
            try
            {
                gridsPtr = Memory.ReadValue<ulong>(
                    item + Offsets.LootItemMod.Grids);
            }
            catch
            {
                return;
            }

            if (!gridsPtr.IsValidVirtualAddress())
                return;

            MemArray<ulong> grids;
            try
            {
                grids = MemArray<ulong>.Get(gridsPtr);
            }
            catch
            {
                return;
            }

            using (grids)
            {
                foreach (var grid in grids)
                {
                    if (!grid.IsValidVirtualAddress())
                        continue;

                    ulong itemCollection;
                    try
                    {
                        itemCollection = Memory.ReadPtr(
                            grid + Offsets.Grid.ItemCollection);
                    }
                    catch
                    {
                        continue;
                    }

                    if (!itemCollection.IsValidVirtualAddress())
                        continue;

                    ulong listPtr;
                    try
                    {
                        listPtr = Memory.ReadPtr(
                            itemCollection + Offsets.GridItemCollection.ItemsList);
                    }
                    catch
                    {
                        continue;
                    }

                    using var list = MemList<ulong>.Get(listPtr);

                    foreach (var child in list)
                    {
                        if (!child.IsValidVirtualAddress())
                            continue;

                        TryProcessDogtag(child);
                        TryWalkContainer(child, depth + 1);
                    }
                }
            }
        }

        // =========================================================
        // DOGTAG EXTRACTION
        // =========================================================
        private static void TryProcessDogtag(ulong item)
        {
            ulong template;
            try
            {
                template = Memory.ReadPtr(item + Offsets.LootItem.Template);
            }
            catch
            {
                return;
            }

            if (!template.IsValidVirtualAddress())
                return;

            string name = null;
            try
            {
                ulong namePtr = Memory.ReadPtr(
                    template + Offsets.ItemTemplate.Name);
                if (namePtr.IsValidVirtualAddress())
                    name = Memory.ReadUnityString(namePtr);
            }
            catch { }

            if (string.IsNullOrEmpty(name) ||
                name.IndexOf("dog", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            ulong dogtag;
            try
            {
                dogtag = Memory.ReadPtr(
                    item + Offsets.BarterOtherOffsets.Dogtag);
            }
            catch
            {
                return;
            }

            if (!dogtag.IsValidVirtualAddress())
                return;

            string profileId = ReadStr(
                dogtag + Offsets.DogtagComponent.ProfileId);
            if (string.IsNullOrEmpty(profileId))
                return;
            string killerProfileId = ReadStr(
                dogtag + Offsets.DogtagComponent.KillerProfileId);
            if (string.IsNullOrEmpty(killerProfileId))
                return;

            string killerAccountId = ReadStr(
                dogtag + Offsets.DogtagComponent.KillerAccountId);
            if (string.IsNullOrEmpty(killerAccountId))
                return;
            string killerName = ReadStr(
                dogtag + Offsets.DogtagComponent.KillerName);
            if (string.IsNullOrEmpty(killerName))
                return;
            lock (_sync)
            {
                if (!_seenProfileIds.Add(profileId))
                    return;
            }

            string accountId = ReadStr(
                dogtag + Offsets.DogtagComponent.AccountId);
            string nickname = ReadStr(
                dogtag + Offsets.DogtagComponent.Nickname);

            if (EnableDebug)
                Log($"DOGTAG ? {nickname} ({profileId})");

            // ? SEND TO API
            DogtagApiClient.Send(
                accountId,
                profileId,
                nickname);
            if (!string.IsNullOrEmpty(killerAccountId) &&
                !string.IsNullOrEmpty(killerProfileId) &&
                !string.IsNullOrEmpty(killerName))
            {
                if (EnableDebug)
                    Log($"KILLER ? {killerName} ({killerProfileId})");
            
                DogtagApiClient.Send(
                    killerAccountId,
                    killerProfileId,
                    killerName);
            }                
        }

        private static string ReadStr(ulong addr)
        {
            try
            {
                ulong ptr = Memory.ReadPtr(addr);
                return ptr.IsValidVirtualAddress()
                    ? Memory.ReadUnityString(ptr)
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static void Log(string msg)
        {
            if (EnableDebug)
                XMLogging.WriteLine($"[MenuStashDogtag] {msg}");
        }
    }
}
