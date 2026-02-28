using System.Collections.Frozen;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov.EFTPlayer;

using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Common.Unity.Collections;
using eft_dma_radar.UI.Pages;
using eft_dma_radar.Tarkov.Features.MemoryWrites;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.GameWorld;
using System.IO;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Tarkov.API;
using eft_dma_radar.UI.ESP.eft_dma_radar.UI.ESP;
using HandyControl.Controls;
using System.Drawing.Imaging.Effects;
using eft_dma_radar.UI.Misc;
using static eft_dma_radar.Tarkov.EFTPlayer.Player;

namespace eft_dma_radar.Tarkov.Loot
{
    public sealed class LootManager
    {
        #region Fields/Properties/Constructor

        private readonly ulong _lgw;
        private readonly CancellationToken _ct;
        private readonly Lock _filterSync = new();

        // Loot refresh caching - only refresh every X seconds
        private const int LOOT_REFRESH_INTERVAL_MS = 5000; // 5 seconds
        private DateTime _lastLootRefresh = DateTime.MinValue;
        private bool _initialRefreshDone = false;

        // ============================================
        // IL2CPP OFFSETS - Centralized in UnityOffsets.cs
        // These aliases make the scatter read code more readable
        // Update UnityOffsets.cs when game updates break things!
        // ============================================
        private const uint MONOBEHAVIOUR_OFFSET = UnityOffsets.ObjectClass.MonoBehaviourOffset;  // 0x10
        private const uint COMPONENT_OBJECTCLASS = UnityOffsets.Component.ObjectClassOffset;     // 0x30
        private const uint COMPONENT_GAMEOBJECT = UnityOffsets.Component.GameObject;             // 0x58
        private const uint GAMEOBJECT_COMPONENTS = UnityOffsets.GameObject.ComponentsOffset;     // 0x58
        private const uint GAMEOBJECT_NAME = UnityOffsets.GameObject.NameOffset;                 // 0x88
        private const uint COMPONENTARRAY_ITEMS = UnityOffsets.ComponentArray.Items;             // 0x08
        private const uint TRANSFORM_OBJECTCLASS = UnityOffsets.Transform.ObjectClassOffset;     // 0x30
        private const uint TRANSFORM_INTERNAL = UnityOffsets.Transform.InternalOffset;           // 0x10
        private static readonly uint[] CLASS_NAME_CHAIN = UnityOffsets.ObjectClass.ToNamePtr;    // [0x0, 0x10]

        /// <summary>
        /// All loot (unfiltered).
        /// </summary>
        public IReadOnlyList<LootItem> UnfilteredLoot { get; private set; }

        /// <summary>
        /// All loot (with filter applied).
        /// </summary>
        public IReadOnlyList<LootItem> FilteredLoot { get; private set; }

        /// <summary>
        /// All Static Loot Containers on the map.
        /// </summary>
        public IReadOnlyList<StaticLootContainer> StaticLootContainers { get; private set; }

        public LootManager(ulong localGameWorld, CancellationToken ct)
        {
            _lgw = localGameWorld;
            _ct = ct;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Force a filter refresh.
        /// Thread Safe.
        /// </summary>
        public void RefreshFilter()
        {
            if (_filterSync.TryEnter())
            {
                try
                {
                    var filter = LootFilterControl.Create();
                    FilteredLoot = UnfilteredLoot?
                        .Where(x => filter(x))
                        .OrderByDescending(x => x.Important)
                        .ThenByDescending(x => (Program.Config.QuestHelper.Enabled && x.IsQuestCondition))
                        .ThenByDescending(x => x.IsWishlisted)
                        .ThenByDescending(x => x.IsValuableLoot)
                        .ThenByDescending(x => x?.Price ?? 0)
                        .ToList();
                }
                catch { }
                finally
                {
                    _filterSync.Exit();
                }
            }
        }

        /// <summary>
        /// Refreshes loot, only call from a memory thread (Non-GUI).
        /// Uses caching to avoid refreshing too frequently.
        /// </summary>
        public void Refresh()
        {
            try
            {
                // Only refresh loot every LOOT_REFRESH_INTERVAL_MS to reduce CPU/memory load
                var now = DateTime.UtcNow;
                if (_initialRefreshDone && (now - _lastLootRefresh).TotalMilliseconds < LOOT_REFRESH_INTERVAL_MS)
                {
                    // Just refresh the filter (fast operation) without re-reading all loot
                    RefreshFilter();
                    return;
                }
                
                _lastLootRefresh = now;
                GetLoot();
                RefreshFilter();
                
                if (!_initialRefreshDone)
                {
                    _initialRefreshDone = true;
                    XMLogging.WriteLine($"[LootManager] Initial load: {UnfilteredLoot?.Count ?? 0} items, {StaticLootContainers?.Count ?? 0} containers");
                }

                LootItem.CleanupNotificationHistory(UnfilteredLoot);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"CRITICAL ERROR - Failed to refresh loot: {ex}");
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Updates referenced Loot List with fresh values.
        /// Uses hardcoded IL2CPP offsets matching XM's working implementation.
        /// Now uses full 6-round chain to get proper TransformInternal.
        /// </summary>
        private void GetLoot()
        {
            var lootListAddr = Memory.ReadPtr(_lgw + Offsets.ClientLocalGameWorld.LootList);
            using var lootList = MemList<ulong>.Get(lootListAddr);
            var loot = new List<LootItem>(lootList.Count);
            var containers = new List<StaticLootContainer>(64);
            var deadPlayers = Memory.Players?
                .Where(x => x.Corpse is not null)?.ToList();
            
            using var map = ScatterReadMap.Get();
            var round1 = map.AddRound();
            var round2 = map.AddRound();
            var round3 = map.AddRound();
            var round4 = map.AddRound();
            var round5 = map.AddRound(); // Extra round for transform chain
            var round6 = map.AddRound(); // Final transform dereference
            
            for (int ix = 0; ix < lootList.Count; ix++)
            {
                var i = ix;
                _ct.ThrowIfCancellationRequested();
                var lootBase = lootList[i];
                
                // ROUND 1: Get MonoBehaviour and start of class name chain
                round1[i].AddEntry<MemPointer>(0, lootBase + MONOBEHAVIOUR_OFFSET);  // 0x10 → MonoBehaviour
                round1[i].AddEntry<MemPointer>(1, lootBase + CLASS_NAME_CHAIN[0]);   // 0x0 → C1 for class name
                
                round1[i].Callbacks += x1 =>
                {
                    if (x1.TryGetResult<MemPointer>(0, out var monoBehaviour) && 
                        x1.TryGetResult<MemPointer>(1, out var c1))
                    {
                        // ROUND 2: Get InteractiveClass, GameObject, and continue class name chain
                        round2[i].AddEntry<MemPointer>(2, monoBehaviour + COMPONENT_OBJECTCLASS);  // 0x30 → InteractiveClass
                        round2[i].AddEntry<MemPointer>(3, monoBehaviour + COMPONENT_GAMEOBJECT);   // 0x58 → GameObject
                        round2[i].AddEntry<MemPointer>(4, c1 + CLASS_NAME_CHAIN[1]);               // 0x10 → ClassNamePtr
                        
                        round2[i].Callbacks += x2 =>
                        {
                            if (x2.TryGetResult<MemPointer>(2, out var interactiveClass) &&
                                x2.TryGetResult<MemPointer>(3, out var gameObject) &&
                                x2.TryGetResult<MemPointer>(4, out var classNamePtr))
                            {
                                // ROUND 3: Get Components array, GameObject name
                                round3[i].AddEntry<MemPointer>(5, gameObject + GAMEOBJECT_COMPONENTS);  // 0x58 → Components
                                round3[i].AddEntry<MemPointer>(6, gameObject + GAMEOBJECT_NAME);        // 0x88 → Name pointer
                                
                                round3[i].Callbacks += x3 =>
                                {
                                    if (x3.TryGetResult<MemPointer>(5, out var components) &&
                                        x3.TryGetResult<MemPointer>(6, out var pGameObjectName))
                                    {
                                        // ROUND 4: Get class name string, object name string, and first transform entry
                                        round4[i].AddEntry<UTF8String>(7, classNamePtr, 64);                  // ClassName
                                        round4[i].AddEntry<UTF8String>(8, pGameObjectName, 64);               // ObjectName
                                        round4[i].AddEntry<MemPointer>(9, components + COMPONENTARRAY_ITEMS); // 0x8 → T1 (first transform component)
                                        
                                        round4[i].Callbacks += x4 =>
                                        {
                                            if (x4.TryGetResult<UTF8String>(7, out var className) &&
                                                x4.TryGetResult<UTF8String>(8, out var objectName) &&
                                                x4.TryGetResult<MemPointer>(9, out var t1))
                                            {
                                                // ROUND 5: Dereference T1 + 0x30 to get T2
                                                round5[i].AddEntry<MemPointer>(10, t1 + TRANSFORM_OBJECTCLASS); // 0x30 → T2
                                                
                                                round5[i].Callbacks += x5 =>
                                                {
                                                    if (x5.TryGetResult<MemPointer>(10, out var t2))
                                                    {
                                                        // ROUND 6: Final dereference T2 + 0x10 to get TransformInternal
                                                        round6[i].AddEntry<MemPointer>(11, t2 + TRANSFORM_INTERNAL); // 0x10 → TransformInternal
                                                        
                                                        round6[i].Callbacks += x6 =>
                                                        {
                                                            if (x6.TryGetResult<MemPointer>(11, out var transformInternal))
                                                            {
                                                                // Defer processing until all scatter reads complete
                                                                map.CompletionCallbacks += () =>
                                                {
                                                    _ct.ThrowIfCancellationRequested();
                                                    try
                                                    {
                                                                        var classNameStr = (string)className;
                                                                        var objectNameStr = (string)objectName;
                                                                        
                                                        ProcessLootIndex(loot, containers, deadPlayers,
                                                                            interactiveClass, objectNameStr,
                                                                            transformInternal, classNameStr, gameObject);
                                                    }
                                                    catch
                                                    {
                                                                        // Silently ignore processing errors
                                                                    }
                                                                };
                                                            }
                                                        };
                                                    }
                                                };
                                            }
                                        };
                                    }
                                };
                            }
                        };
                    }
                };
            }

            map.Execute(); // Execute scatter read
            
            this.UnfilteredLoot = loot;
            this.StaticLootContainers = containers;
        }

        /// <summary>
        /// Process a single loot index.
        /// </summary>
        private static void ProcessLootIndex(List<LootItem> loot, List<StaticLootContainer> containers, IReadOnlyList<Player> deadPlayers,
            ulong interactiveClass, string objectName, ulong transformInternal, string className, ulong gameObject)
        {
            var isCorpse = className.Contains("Corpse", StringComparison.OrdinalIgnoreCase);
            var isLooseLoot = className.Equals("ObservedLootItem", StringComparison.OrdinalIgnoreCase);
            var isContainer = className.Equals("LootableContainer", StringComparison.OrdinalIgnoreCase);
            if (objectName.Contains("script", StringComparison.OrdinalIgnoreCase))
            {
                //skip these. These are scripts which I think are things like landmines but not sure
            }
            else
            {
                // Get Item Position
                var pos = new UnityTransform(transformInternal, true).UpdatePosition();
                if (isCorpse)
                {
                    var player = deadPlayers?.FirstOrDefault(x => x.Corpse == interactiveClass);
                    var corpseLoot = new List<LootItem>();
                    bool isPMC = player?.IsPmc ?? true;

                    GetCorpseLoot(interactiveClass, corpseLoot, isPMC);

                    // 🔥 LOG DOGTAG ONCE HERE
                    CorpseDogtagLogger.TryLog(player, corpseLoot);

                    var corpse = new LootCorpse(corpseLoot)
                    {
                        Position = pos,
                        PlayerObject = player
                    };

                    loot.Add(corpse);

                    if (player is not null)
                        player.LootObject = corpse;
                }
                else if (isContainer)
                {
                    try
                    {
                        if (objectName.Equals("loot_collider", StringComparison.OrdinalIgnoreCase))
                        {
                            loot.Add(new LootAirdrop()
                            {
                                Position = pos,
                                InteractiveClass = interactiveClass
                            });
                        }
                        else
                        {
                            var itemOwner = Memory.ReadPtr(interactiveClass + Offsets.LootableContainer.ItemOwner);
                            var ownerItemBase = Memory.ReadPtr(itemOwner + Offsets.LootableContainerItemOwner.RootItem);
                            var ownerItemTemplate = Memory.ReadPtr(ownerItemBase + Offsets.LootItem.Template);
                            var ownerItemBsgIdPtr = Memory.ReadValue<Types.MongoID>(ownerItemTemplate + Offsets.ItemTemplate._id);
                            var ownerItemBsgId = Memory.ReadUnityString(ownerItemBsgIdPtr.StringID);
                            bool containerOpened = Memory.ReadValue<ulong>(interactiveClass + Offsets.LootableContainer.InteractingPlayer) != 0;
                            containers.Add(new StaticLootContainer(ownerItemBsgId, containerOpened)
                            {
                                Position = pos,
                                InteractiveClass = interactiveClass,
                                GameObject = gameObject
                            });
                        }
                    }
                    catch { }
                }
                else if (isLooseLoot)
                {
                    var item = Memory.ReadPtr(interactiveClass +
                                              Offsets.InteractiveLootItem.Item); //EFT.InventoryLogic.Item
                    var itemTemplate = Memory.ReadPtr(item + Offsets.LootItem.Template); //EFT.InventoryLogic.ItemTemplate
                    var isQuestItem = Memory.ReadValue<bool>(itemTemplate + Offsets.ItemTemplate.QuestItem);

                    //If NOT a quest item. Quest items are like the quest related things you need to find like the pocket watch or Jaeger's Letter etc. We want to ignore these quest items.
                    var BSGIdPtr = Memory.ReadValue<Types.MongoID>(itemTemplate + Offsets.ItemTemplate._id);
                    var id = Memory.ReadUnityString(BSGIdPtr.StringID);
                    if (isQuestItem)
                    {
                        QuestItem questItem;
                        if (EftDataManager.AllItems.TryGetValue(id, out var entry))
                        {
                            questItem = new QuestItem(entry)
                            {
                                Position = pos,
                                InteractiveClass = interactiveClass
                            };
                        }
                        else
                        {
                            var shortNamePtr = Memory.ReadPtr(itemTemplate + Offsets.ItemTemplate.ShortName);
                            var shortName = Memory.ReadUnityString(shortNamePtr)?.Trim();
                            if (string.IsNullOrEmpty(shortName))
                                shortName = "Item";
                            questItem = new QuestItem(id, $"Q_{shortName}")
                            {
                                Position = pos,
                                InteractiveClass = interactiveClass
                            };
                        }
                        loot.Add(questItem);
                    }
                    else // Regular Loose Loot Item
                    {
                        if (EftDataManager.AllItems.TryGetValue(id, out var entry))
                        {
                            loot.Add(new LootItem(entry)
                            {
                                Position = pos,
                                InteractiveClass = interactiveClass
                            });
                        }
                    }
                }
            }
        }
        private static readonly FrozenSet<string> _skipSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SecuredContainer", "Compass", "Eyewear", "ArmBand"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Recurse slots for gear.
        /// </summary>
        private static void GetItemsInSlots(ulong slotsPtr, List<LootItem> loot, bool isPMC)
        {
            var slotDict = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
            using var slots = MemArray<ulong>.Get(slotsPtr);

            foreach (var slot in slots)
            {
                var namePtr = Memory.ReadPtr(slot + Offsets.Slot.ID);
                var name = Memory.ReadUnityString(namePtr);
                if (!_skipSlots.Contains(name))
                    slotDict.TryAdd(name, slot);
            }

            foreach (var slot in slotDict)
            {
                try
                {
                    if (isPMC && slot.Key == "Scabbard")
                        continue;
                    var containedItem = Memory.ReadPtr(slot.Value + Offsets.Slot.ContainedItem);
                    var inventorytemplate = Memory.ReadPtr(containedItem + Offsets.LootItem.Template);
                    var idPtr = Memory.ReadValue<Types.MongoID>(inventorytemplate + Offsets.ItemTemplate._id);
                    var id = Memory.ReadUnityString(idPtr.StringID);
                    if (EftDataManager.AllItems.TryGetValue(id, out var entry))
                        loot.Add(new LootItem(entry)
                        {
                            InteractiveClass = containedItem // <-- THIS IS THE ITEM
                        });
                    var childGrids = Memory.ReadPtr(containedItem + Offsets.LootItemMod.Grids);
                    GetItemsInGrid(childGrids, loot); // Recurse the grids (if possible)
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Gets all loot on a corpse.
        /// </summary>
        private static void GetCorpseLoot(ulong lootInteractiveClass, List<LootItem> loot, bool isPMC)
        {
            var itemBase = Memory.ReadPtr(lootInteractiveClass + Offsets.InteractiveLootItem.Item);
            var slots = Memory.ReadPtr(itemBase + Offsets.LootItemMod.Slots);
            try
            {
                GetItemsInSlots(slots, loot, isPMC);
            }
            catch
            {
            }
        }

        #endregion
        #region DogTags
        /// <summary>
        /// Gets all dogtags on a corpse.
        /// </summary>
        internal static class CorpseDogtagLogger
        {
            // =========================
            // CONFIG
            // =========================
            public static bool EnableDebug = false;

            // =========================
            // STATE
            // =========================
            private static readonly HashSet<string> _loggedProfileIds = new();
            private static readonly object _sync = new();

            // =========================
            // PUBLIC ENTRY
            // =========================
            public static void TryLog(Player player, List<LootItem> corpseLoot)
            {
                Debug($"[Dogtag] Checking corpse: Name='{player?.Name}' PMC={player?.IsPmc} LootCount={corpseLoot?.Count ?? 0}");

                    
                if (corpseLoot == null || corpseLoot.Count == 0)
                {
                    Debug("[Dogtag] SKIP: corpseLoot empty");
                    return;
                }

                foreach (var item in corpseLoot)
                {
                    if (item == null)
                        continue;

                    if (string.IsNullOrEmpty(item.Name) ||
                        !item.Name.Contains("dogtag", StringComparison.OrdinalIgnoreCase))
                        continue;

                    ulong itemBase = item.InteractiveClass;
                    if (!itemBase.IsValidVirtualAddress())
                        continue;

                    // -------------------------------------------------
                    // DogtagComponent
                    // -------------------------------------------------
                    ulong dogtagComp;
                    try
                    {
                        dogtagComp = Memory.ReadPtr(itemBase + Offsets.BarterOtherOffsets.Dogtag);
                    }
                    catch
                    {
                        continue;
                    }

                    if (!dogtagComp.IsValidVirtualAddress())
                        continue;

                    // -------------------------------------------------
                    // VICTIM
                    // -------------------------------------------------
                    string victimName = ReadStringPtr(dogtagComp + Offsets.DogtagComponent.Nickname, "VictimName");
                    string victimProfileId = ReadStringPtr(dogtagComp + Offsets.DogtagComponent.ProfileId, "VictimProfileId");
                    string victimAccountId = ReadStringPtr(dogtagComp + Offsets.DogtagComponent.AccountId, "VictimAccountId");

                    if (string.IsNullOrEmpty(victimProfileId) ||
                        string.IsNullOrEmpty(victimAccountId) ||
                        string.IsNullOrEmpty(victimName))
                        continue;

                    lock (_sync)
                    {
                        if (!_loggedProfileIds.Add(victimProfileId))
                        {
                            Debug($"[Dogtag] DUPLICATE Victim ProfileId '{victimProfileId}'");
                            continue;
                        }
                    }

                    //XMLogging.WriteLine($"[Dogtag] ✔ VICTIM {victimName} ({victimProfileId})");

                    DogtagApiClient.Send(
                        victimAccountId,
                        victimProfileId,
                        victimName);

                    // -------------------------------------------------
                    // KILLER (AS SEPARATE PLAYER)
                    // -------------------------------------------------
                    string killerProfileId = ReadStringPtr(dogtagComp + Offsets.DogtagComponent.KillerProfileId, "KillerProfileId");
                    string killerAccountId = ReadStringPtr(dogtagComp + Offsets.DogtagComponent.KillerAccountId, "KillerAccountId");
                    string killerName = ReadStringPtr(dogtagComp + Offsets.DogtagComponent.KillerName, "KillerName");
                    string killerWeapon = ReadStringPtr(dogtagComp + Offsets.DogtagComponent.WeaponName, "KillerWeapon");

                    if (!string.IsNullOrEmpty(killerProfileId) &&
                        !string.IsNullOrEmpty(killerAccountId) &&
                        !string.IsNullOrEmpty(killerName))
                    {
                        //XMLogging.WriteLine($"[Dogtag] ✔ KILLER {killerName} ({killerProfileId})");
                        PlayerListWorker.UpdateIdentity(
                            profileId: killerProfileId,
                            nickname: killerName,
                            accountId: killerAccountId);
                        DogtagApiClient.Send(
                            killerAccountId,
                            killerProfileId,
                            killerName);
                        // -------------------------------------------------
                        // RESOLVE KILLER WEAPON FROM LIVE PLAYER LIST
                        // -------------------------------------------------
                        string weapon = "UNKNOWN";
                        PlayerType side = PlayerType.Default;
                        string level = ""; 
                        string ammo = "";

                        if (!string.IsNullOrEmpty(killerProfileId))
                        {
                            try
                            {
                                var killerPlayer = Memory.Players?
                                    .FirstOrDefault(p =>
                                        p is ObservedPlayer op &&
                                        op.ProfileID == killerProfileId);

                                if (killerPlayer?.Hands?.CurrentItem is string w &&
                                    !string.IsNullOrWhiteSpace(w))
                                {
                                    weapon = w;
                                    ammo = killerPlayer?.Hands?.CurrentAmmo;
                                    side = killerPlayer.Type;
                                    if (killerPlayer is ObservedPlayer op)
                                    {
                                        if (op.Profile?.Level is int lvl)
                                            level = lvl.ToString();
                                    }
                                }
                            }
                            catch
                            {
                                // ignore — fallback to UNKNOWN
                            }
                        }

                        // -------------------------------------------------
                        // PUSH KILLFEED EVENT
                        // -------------------------------------------------
                        KillfeedManager.Push(
                            killerName,
                            victimName,
                            weapon,
                            side,
                            ammo,
                            level
                        );                      
                    }                 
                }
            }

            // =========================
            // HELPERS
            // =========================
            private static string ReadStringPtr(ulong addr, string label)
            {
                try
                {
                    ulong ptr = Memory.ReadPtr(addr);
                    if (!ptr.IsValidVirtualAddress())
                        return null;

                    return Memory.ReadUnityString(ptr);
                }
                catch
                {
                    return null;
                }
            }

            private static void Debug(string msg)
            {
                if (EnableDebug)
                    XMLogging.WriteLine(msg);
            }

            // =========================
            // RESET
            // =========================
            public static void Reset()
            {
                lock (_sync)
                {
                    _loggedProfileIds.Clear();
                    XMLogging.WriteLine("[Dogtag] Reset logged profile IDs");
                }
            }
        }

        #endregion
        #region Static Public Methods

        ///This method recursively searches grids. Grids work as follows:
        ///Take a Groundcache which holds a Blackrock which holds a pistol.
        ///The Groundcache will have 1 grid array, this method searches for whats inside that grid.
        ///Then it finds a Blackrock. This method then invokes itself recursively for the Blackrock.
        ///The Blackrock has 11 grid arrays (not to be confused with slots!! - a grid array contains slots. Look at the blackrock and you'll see it has 20 slots but 11 grids).
        ///In one of those grid arrays is a pistol. This method would recursively search through each item it finds
        ///To Do: add slot logic, so we can recursively search through the pistols slots...maybe it has a high value scope or something.
        public static void GetItemsInGrid(ulong gridsArrayPtr, List<LootItem> containerLoot,
            int recurseDepth = 0)
        {
            ArgumentOutOfRangeException.ThrowIfZero(gridsArrayPtr, nameof(gridsArrayPtr));
            if (recurseDepth++ > 3) return; // Only recurse 3 layers deep (this should be plenty)
            using var gridsArray = MemArray<ulong>.Get(gridsArrayPtr);

            try
            {
                // Check all sections of the container
                foreach (var grid in gridsArray)
                {
                    var gridEnumerableClass =
                        Memory.ReadPtr(grid +
                                       Offsets.Grids
                                           .ContainedItems); // -.GClass178A->gClass1797_0x40 // Offset: 0x0040 (Type: -.GClass1797)

                    var itemListPtr =
                        Memory.ReadPtr(gridEnumerableClass +
                                       Offsets.GridContainedItems.Items); // -.GClass1797->list_0x18 // Offset: 0x0018 (Type: System.Collections.Generic.List<Item>)
                    using var itemList = MemList<ulong>.Get(itemListPtr);

                    foreach (var childItem in itemList)
                        try
                        {
                            var childItemTemplate =
                                Memory.ReadPtr(childItem +
                                               Offsets.LootItem
                                                   .Template); // EFT.InventoryLogic.Item->_template // Offset: 0x0038 (Type: EFT.InventoryLogic.ItemTemplate)
                            var childItemIdPtr = Memory.ReadValue<Types.MongoID>(childItemTemplate + Offsets.ItemTemplate._id);
                            var childItemIdStr = Memory.ReadUnityString(childItemIdPtr.StringID);
                            if (EftDataManager.AllItems.TryGetValue(childItemIdStr, out var entry))
                                containerLoot.Add(new LootItem(entry));

                            // Check to see if the child item has children
                            // Don't throw on nullPtr since GetItemsInGrid needs to record the current item still
                            var childGridsArrayPtr = Memory.ReadValue<ulong>(childItem + Offsets.LootItemMod.Grids); // Pointer
                            GetItemsInGrid(childGridsArrayPtr, containerLoot,
                                recurseDepth); // Recursively add children to the entity
                        }
                        catch { }
                }
            }
            catch { }
        }
        #endregion
    }
}