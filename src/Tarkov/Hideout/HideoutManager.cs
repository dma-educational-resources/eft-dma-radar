using System.Collections.Frozen;
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
    /// Maps EFT's EAreaType enum integer values to a readable name.
    /// Values confirmed from IL2CPP dump.
    /// </summary>
    public enum EAreaType
    {
        Vents                = 0,
        Security             = 1,
        WaterCloset          = 2,
        Stash                = 3,
        Generator            = 4,
        Heating              = 5,
        WaterCollector       = 6,
        MedStation           = 7,
        Kitchen              = 8,
        RestSpace            = 9,
        Workbench            = 10,
        IntelligenceCenter   = 11,
        ShootingRange        = 12,
        Library              = 13,
        ScavCase             = 14,
        Illumination         = 15,
        PlaceOfFame          = 16,
        AirFilteringUnit     = 17,
        SolarPower           = 18,
        BoozeGenerator       = 19,
        BitcoinFarm          = 20,
        ChristmasIllumination= 21,
        EmergencyWall        = 22,
        Gym                  = 23,
        WeaponStand          = 24,
        WeaponStandSecondary = 25,
        EquipmentPresetsStand= 26,
        CircleOfCultists     = 27,
    }

    /// <summary>
    /// EFT hideout area upgrade/construction status. Values confirmed from IL2CPP dump.
    /// </summary>
    public enum EAreaStatus
    {
        NotSet                  = 0,
        LockedToConstruct       = 1,
        ReadyToConstruct        = 2,
        Constructing            = 3,
        ReadyToInstallConstruct = 4,
        LockedToUpgrade         = 5,
        ReadyToUpgrade          = 6,
        Upgrading               = 7,
        ReadyToInstallUpgrade   = 8,
        NoFutureUpgrades        = 9,
        AutoUpgrading           = 10,
    }

    /// <summary>
    /// Discriminates the kind of requirement on a hideout upgrade stage.
    /// </summary>
    public enum ERequirementType
    {
        Area           = 0,
        Item           = 1,
        TraderUnlock   = 2,
        TraderLoyalty  = 3,
        Skill          = 4,
        Resource       = 5,
        Tool           = 6,
        QuestComplete  = 7,
        Health         = 8,
        BodyPartBuff   = 9,
        GameVersion    = 10,
    }

    /// <summary>
    /// A single requirement read from a hideout upgrade stage.
    /// Fields are populated based on <see cref="Type"/>; irrelevant fields remain at their defaults.
    /// </summary>
    public sealed record HideoutRequirement(
        ERequirementType Type,
        bool             Fulfilled,
        /// <summary>Item BSG template id (only when <see cref="Type"/> is Item or Tool).</summary>
        string           ItemTemplateId  = null,
        /// <summary>Resolved item name from market data (only when <see cref="Type"/> is Item or Tool).</summary>
        string           ItemName        = null,
        /// <summary>Number of items required (only when <see cref="Type"/> is Item or Tool).</summary>
        int              RequiredCount   = 0,
        /// <summary>Number of matching items the player currently has in stash (only when <see cref="Type"/> is Item or Tool).</summary>
        int              CurrentCount    = 0,
        /// <summary>Required area type (only when <see cref="Type"/> is Area).</summary>
        EAreaType        RequiredArea    = default,
        /// <summary>Required area level (only when <see cref="Type"/> is Area).</summary>
        int              RequiredLevel   = 0,
        /// <summary>Skill name, e.g. "Strength" (only when <see cref="Type"/> is Skill).</summary>
        string           SkillName       = null,
        /// <summary>Required skill level (only when <see cref="Type"/> is Skill).</summary>
        int              SkillLevel      = 0,
        /// <summary>Trader BSG id (only when <see cref="Type"/> is TraderLoyalty).</summary>
        string           TraderId        = null,
        /// <summary>Resolved trader display name (only when <see cref="Type"/> is TraderLoyalty).</summary>
        string           TraderName      = null,
        /// <summary>Required loyalty level (only when <see cref="Type"/> is TraderLoyalty).</summary>
        int              LoyaltyLevel    = 0)
    {
        /// <summary>How many more items are still needed (only meaningful for Item/Tool).</summary>
        public int StillNeeded => Math.Max(0, RequiredCount - CurrentCount);
    }

    /// <summary>
    /// Current level snapshot for one hideout area read from memory.
    /// </summary>
    public sealed record HideoutAreaInfo(
        EAreaType                         AreaType,
        int                               CurrentLevel,
        EAreaStatus                       Status,
        IReadOnlyList<HideoutRequirement> NextLevelRequirements)
    {
        /// <summary>True when the area has no further upgrades available.</summary>
        public bool IsMaxLevel => Status == EAreaStatus.NoFutureUpgrades;
    }

    /// <summary>
    /// A single item resolved from the hideout stash.
    /// </summary>
    public sealed record StashItem(
        string Id,
        string Name,
        long   TraderPrice,
        string BestTraderName,
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
        private const string HideoutAreaClassName       = "HideoutArea";
        private const string HideoutControllerClassName  = "HideoutController";

        // ── Stash pointer-chain ───────────────────────────────────────────────────────────
        // HideoutArea(+0xA8) → HideoutAreaStashController(+0x10)
        //   → OfflineInventoryController(+0x100) → Inventory(+0x20) → Grid[](+0x78)
        private const uint OffStashCtrl = 0xA8;  // HideoutArea.<StashController>
        private const uint OffInvCtrl   = 0x10;  // HideoutAreaStashController → OfflineInventoryController
        private const uint OffInventory = 0x100; // OfflineInventoryController._Inventory
        private const uint OffStash     = 0x20;  // Inventory.Stash (CompoundItem)
        private const uint OffGrids     = 0x78;  // CompoundItem.Grids (Grid[])

        // ── HideoutController._areas Dictionary<EAreaType, HideoutArea> ──────────────────
        private const uint  OffAreas       = 0x80; // HideoutController._areas
        private const uint  DictCountOff   = 0x20; // Dictionary.count
        private const uint  DictEntriesOff = 0x18; // Dictionary.entries (Entry[])
        private const ulong DictDataOff    = 0x20; // Entry[] data start
        private const int   DictEntrySize  = 24;   // sizeof(Entry<int,ulong>)
        private const uint  DictValueOff   = 16;   // Entry.value offset

        // ── HideoutArea fields ────────────────────────────────────────────────────────────
        private const uint OffAreaData    = 0x70; // HideoutArea._data (AreaData)
        private const uint OffAreaLevels  = 0x48; // HideoutArea._areaLevels (HideoutAreaLevel[])
        // HideoutArea._currentLevel (+0x78) is the CURRENT built level — NOT the next one.
        // Do NOT use it for requirement lookups; always index _areaLevels[currentLevel + 1].

        // ── AreaData fields ───────────────────────────────────────────────────────────────
        private const uint OffCurLevel = 0xA8; // AreaData._currentLevel (int)
        private const uint OffStatus   = 0xC8; // AreaData._status (EAreaStatus, int)

        // ── HideoutAreaLevel._stage ───────────────────────────────────────────────────────
        // SerializedMonoBehaviour user fields start at 0x60; _stage is at 0xA0
        private const uint OffStage = 0xA0; // HideoutAreaLevel._stage (Stage)

        // ── Stage fields ─────────────────────────────────────────────────────────────────
        private const uint OffRequirements = 0x18; // Stage.Requirements (RelatedRequirements)

        // ── RelatedRequirements.Data ──────────────────────────────────────────────────────
        private const uint OffRelData = 0x10; // RelatedRequirements.Data (List<Requirement>)

        // ── Requirement base fields ───────────────────────────────────────────────────────
        private const uint OffReqFulfilled = 0x18; // Requirement.<Fulfilled>

        // ── ItemRequirement / ToolRequirement item count fields ───────────────────────────
        private const uint OffReqUserCount = 0x54; // ItemRequirement.<UserItemsCount> (int)
        private const uint OffReqBaseCount = 0x5C; // ItemRequirement._baseCount (int)

        // ── ItemRequirement / ToolRequirement ─────────────────────────────────────────────
        // _userValue @ 0x30 is the last base field
        // ItemRequirement: <TemplateId> (string*) @ 0x48
        // ToolRequirement: <TemplateId> (string*) @ 0x48

        /// <summary>HideoutArea behaviour address.</summary>
        public ulong Base { get; private set; }

        /// <summary>HideoutController ObjectClass address (for area level reading).</summary>
        public ulong AreasControllerBase { get; private set; }

        /// <summary>Grid[] array pointer (0 until <see cref="TryFind"/> succeeds).</summary>
        public ulong StashGridPtr { get; private set; }

        /// <summary>Items populated by the last <see cref="Refresh"/> call.</summary>
        public IReadOnlyList<StashItem> Items { get; private set; } = [];

        /// <summary>Area levels populated by the last <see cref="ReadAreas"/> call.</summary>
        public IReadOnlyList<HideoutAreaInfo> Areas { get; private set; } = [];

        /// <summary>
        /// Template IDs of items/tools still needed across all unfulfilled upgrade requirements.
        /// Rebuilt after every <see cref="ReadAreas"/> call.
        /// </summary>
        public FrozenSet<string> NeededItemIds { get; private set; } = FrozenSet<string>.Empty;

        /// <summary>Sum of the best sell price (trader vs flea) for every item in the stash.</summary>
        public long TotalBestValue   => Items.Sum(i => i.BestPrice);
        /// <summary>Sum of trader prices for every item in the stash.</summary>
        public long TotalTraderValue => Items.Sum(i => i.TraderPrice * i.StackCount);
        /// <summary>Sum of flea prices for every item in the stash.</summary>
        public long TotalFleaValue   => Items.Sum(i => i.FleaPrice   * i.StackCount);

        public bool IsValid      => Base.IsValidVirtualAddress() && StashGridPtr.IsValidVirtualAddress();
        public bool IsAreasValid => AreasControllerBase.IsValidVirtualAddress();

        /// <summary>
        /// Scans the GOM for the HideoutArea component and walks the pointer chain
        /// to the stash Grid[] array. Returns true when the grid is reachable.
        /// </summary>
        public bool TryFind()
        {
            try
            {
                //DumpGOM();

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

                // Also locate HideoutController for area-level reading
                var ctrlBehaviour = gom.FindBehaviourByClassName(HideoutControllerClassName);
                if (ctrlBehaviour.IsValidVirtualAddress())
                {
                    AreasControllerBase = ctrlBehaviour;
                    XMLogging.WriteLine($"[HideoutManager] HideoutController @ 0x{AreasControllerBase:X}");
                }
                else
                {
                    XMLogging.WriteLine("[HideoutManager] HideoutController not found in GOM.");
                }

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
        /// Reads the current level, status, and next-upgrade requirements for every
        /// hideout area from memory via HideoutController._areas.
        /// Results are stored in <see cref="Areas"/>.
        /// </summary>
        public void ReadAreas()
        {
            if (!IsAreasValid)
                return;
            try
            {
                var dictPtr = Memory.ReadPtr(AreasControllerBase + OffAreas);
                if (!dictPtr.IsValidVirtualAddress()) return;

                var count = Memory.ReadValue<int>(dictPtr + DictCountOff);
                if (count <= 0 || count > 64) return;

                var entriesPtr = Memory.ReadPtr(dictPtr + DictEntriesOff);
                if (!entriesPtr.IsValidVirtualAddress()) return;

                var dataBase = entriesPtr + DictDataOff;
                var areas    = new List<HideoutAreaInfo>(count);

                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        var entryAddr = dataBase + (ulong)(i * DictEntrySize);
                        var areaType  = (EAreaType)Memory.ReadValue<int>(entryAddr + 8);
                        var areaPtr   = Memory.ReadPtr(entryAddr + (uint)DictValueOff);
                        if (!areaPtr.IsValidVirtualAddress()) continue;

                        var dataPtr = Memory.ReadPtr(areaPtr + OffAreaData);
                        if (!dataPtr.IsValidVirtualAddress()) continue;

                        var level  = Memory.ReadValue<int>(dataPtr + OffCurLevel);
                        var status = (EAreaStatus)Memory.ReadValue<int>(dataPtr + OffStatus);

                        // No point reading requirements for areas that are already max level
                        var reqs = status == EAreaStatus.NoFutureUpgrades
                            ? []
                            : ReadNextLevelRequirements(areaPtr, level, areaType);

                        areas.Add(new HideoutAreaInfo(areaType, level, status, reqs));
                    }
                    catch (Exception ex)
                    {
                        XMLogging.WriteLine($"[HideoutManager] ReadAreas entry error: {ex.Message}");
                    }
                }

                Areas = areas;

                // Rebuild the set of item template IDs still needed for upgrades
                NeededItemIds = areas
                    .SelectMany(a => a.NextLevelRequirements)
                    .Where(r => r.Type is ERequirementType.Item or ERequirementType.Tool && r.StillNeeded > 0)
                    .Select(r => r.ItemTemplateId)
                    .Where(id => id is not null)
                    .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

                var upgradeable = areas.Where(a => !a.IsMaxLevel).ToList();
                XMLogging.WriteLine(
                    $"[HideoutManager] ReadAreas: {areas.Count} area(s), " +
                    $"{upgradeable.Count} upgradeable, " +
                    $"{areas.Count - upgradeable.Count} max level.");
                foreach (var a in upgradeable)
                    XMLogging.WriteLine(
                        $"  {a.AreaType,-24} lv{a.CurrentLevel} [{a.Status}] " +
                        $"{a.NextLevelRequirements.Count} req(s)");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[HideoutManager] ReadAreas error: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads the next-level upgrade requirements for a single hideout area.
        /// <para>
        /// Always reads via the managed array:<br/>
        /// <c>HideoutArea._areaLevels (0x48)[currentLevel + 1] → _stage (0xA0) → Requirements (0x18) → Data (0x10)</c>
        /// </para>
        /// <para>
        /// <c>_currentLevel</c> (+0x78) points to the <b>current</b> built level, not the next —
        /// it must not be used for requirement lookups.
        /// </para>
        /// </summary>
        private static IReadOnlyList<HideoutRequirement> ReadNextLevelRequirements(
            ulong areaPtr, int currentLevel, EAreaType areaType)
        {
            try
            {
                // Always use _areaLevels[currentLevel + 1] — index 1 for lv0, 2 for lv1, etc.
                var arrayPtr = Memory.ReadPtr(areaPtr + OffAreaLevels);
                if (!arrayPtr.IsValidVirtualAddress()) return [];

                var arrayCount = Memory.ReadValue<int>(arrayPtr + MemArray<ulong>.CountOffset);
                var targetIndex = currentLevel + 1;
                if (arrayCount <= targetIndex) return [];

                var levelObjPtr = Memory.ReadPtr(
                    arrayPtr + MemArray<ulong>.ArrBaseOffset
                    + (ulong)(targetIndex * (int)UnityOffsets.ManagedArray.ElementSize));

                if (!levelObjPtr.IsValidVirtualAddress()) return [];

                // _stage (0xA0) → Requirements (0x18) → Data (0x10)
                var stagePtr = Memory.ReadPtr(levelObjPtr + OffStage);
                if (!stagePtr.IsValidVirtualAddress()) return [];

                var relReqPtr = Memory.ReadPtr(stagePtr + OffRequirements);
                if (!relReqPtr.IsValidVirtualAddress()) return [];

                var listPtr = Memory.ReadPtr(relReqPtr + OffRelData);
                if (!listPtr.IsValidVirtualAddress()) return [];

                var reqCount = Memory.ReadValue<int>(listPtr + MemList<ulong>.CountOffset);
                if (reqCount <= 0 || reqCount > 256) return [];

                var itemsArrPtr = Memory.ReadPtr(listPtr + MemList<ulong>.ArrOffset);
                if (!itemsArrPtr.IsValidVirtualAddress()) return [];

                var label = $"{areaType} lv{currentLevel}→{currentLevel + 1}";
                return ReadRequirementsFromDataStart(
                    itemsArrPtr + MemList<ulong>.ArrStartOffset, reqCount, label);
            }
            catch
            {
                return [];
            }
        }

        /// <summary>
        /// Reads <paramref name="count"/> <see cref="HideoutRequirement"/> objects from a
        /// contiguous block of pointer-sized elements starting at <paramref name="dataStart"/>.
        /// </summary>
        private static List<HideoutRequirement> ReadRequirementsFromDataStart(
            ulong dataStart, int count, string areaLabel)
        {
            var results = new List<HideoutRequirement>(count);
            for (int r = 0; r < count; r++)
            {
                try
                {
                    var reqPtr = Memory.ReadPtr(
                        dataStart + (ulong)(r * (int)UnityOffsets.ManagedArray.ElementSize));
                    if (!reqPtr.IsValidVirtualAddress()) continue;

                    var fulfilled = Memory.ReadValue<bool>(reqPtr + OffReqFulfilled);

                    var className = ObjectClass.ReadName(reqPtr, 64, useCache: false);
                    if (className is null) continue;

                    HideoutRequirement req;

                    if (className.Contains("Tool", StringComparison.OrdinalIgnoreCase))
                    {
                        req = ReadItemOrToolRequirement(reqPtr, ERequirementType.Tool, fulfilled);
                    }
                    else if (className.Contains("Item", StringComparison.OrdinalIgnoreCase))
                    {
                        req = ReadItemOrToolRequirement(reqPtr, ERequirementType.Item, fulfilled);
                    }
                    else if (className.Contains("Area", StringComparison.OrdinalIgnoreCase))
                    {
                        // AreaRequirement: AreaType @ 0x38, RequiredLevel @ 0x3C
                        var area = (EAreaType)Memory.ReadValue<int>(reqPtr + 0x38);
                        var lvl  = Memory.ReadValue<int>(reqPtr + 0x3C);
                        req = new HideoutRequirement(ERequirementType.Area, fulfilled, RequiredArea: area, RequiredLevel: lvl);
                    }
                    else if (className.Contains("Skill", StringComparison.OrdinalIgnoreCase))
                    {
                        // SkillRequirement: SkillName @ 0x38, SkillLevel @ 0x40
                        var skill = TryReadString(reqPtr + 0x38);
                        var lvl   = Memory.ReadValue<int>(reqPtr + 0x40);
                        req = new HideoutRequirement(ERequirementType.Skill, fulfilled, SkillName: skill, SkillLevel: lvl);
                    }
                    else if (className.Contains("Loyalty", StringComparison.OrdinalIgnoreCase))
                    {
                        // TraderLoyaltyRequirement: LoyaltyLevel @ 0x40, TraderId @ 0x48
                        var traderId   = TryReadString(reqPtr + 0x48);
                        var lvl        = Memory.ReadValue<int>(reqPtr + 0x40);
                        string traderName = null;
                        if (traderId is not null)
                            EftDataManager.AllTraders.TryGetValue(traderId, out traderName);
                        req = new HideoutRequirement(ERequirementType.TraderLoyalty, fulfilled,
                            TraderId: traderId, TraderName: traderName, LoyaltyLevel: lvl);
                    }
                    else if (className.Contains("Trader", StringComparison.OrdinalIgnoreCase))
                        req = new HideoutRequirement(ERequirementType.TraderUnlock, fulfilled);
                    else if (className.Contains("Quest", StringComparison.OrdinalIgnoreCase))
                        req = new HideoutRequirement(ERequirementType.QuestComplete, fulfilled);
                    else
                        req = new HideoutRequirement(ERequirementType.Resource, fulfilled);

                    XMLogging.WriteLine($"[HideoutManager] [{areaLabel}] req[{r}] {FormatReq(req)}");
                    results.Add(req);
                }
                catch { }
            }
            return results;
        }

        /// <summary>
        /// Reads an ItemRequirement or ToolRequirement from memory, including template id,
        /// required/current counts, and resolved item name.
        /// </summary>
        private static HideoutRequirement ReadItemOrToolRequirement(
            ulong reqPtr, ERequirementType type, bool fulfilled)
        {
            // <TemplateId> @ 0x48, _baseCount @ 0x5C, <UserItemsCount> @ 0x54
            var tpl      = TryReadString(reqPtr + 0x48);
            var required = Memory.ReadValue<int>(reqPtr + OffReqBaseCount);
            var current  = Memory.ReadValue<int>(reqPtr + OffReqUserCount);

            // Resolve human-readable name from market data
            string itemName = null;
            if (tpl is not null && EftDataManager.AllItems.TryGetValue(tpl, out var entry))
                itemName = entry.ShortName;

            return new HideoutRequirement(type, fulfilled,
                ItemTemplateId: tpl,
                ItemName:       itemName,
                RequiredCount:  required,
                CurrentCount:   current);
        }

        /// <summary>Formats the subtype-specific fields of a requirement for debug logging.</summary>
        private static string FormatReq(HideoutRequirement req) => req.Type switch
        {
            ERequirementType.Item or ERequirementType.Tool
                => $"{req.ItemName ?? req.ItemTemplateId ?? "-"} {req.CurrentCount}/{req.RequiredCount}{(req.Fulfilled ? " ✓" : $" need {req.StillNeeded}")}",
            ERequirementType.Area
                => $"{req.RequiredArea} lvl {req.RequiredLevel}{(req.Fulfilled ? " ✓" : "")}",
            ERequirementType.Skill
                => $"{req.SkillName ?? "-"} lvl {req.SkillLevel}{(req.Fulfilled ? " ✓" : "")}",
            ERequirementType.TraderLoyalty
                => $"{req.TraderName ?? req.TraderId ?? "-"} loyalty {req.LoyaltyLevel}{(req.Fulfilled ? " ✓" : "")}",
            _ => req.Fulfilled ? "✓" : ""
        };


        /// <summary>
        /// Reads a Unity string from the pointer stored at <paramref name="fieldAddr"/>.
        /// Returns null if the pointer is invalid or the read fails.
        /// </summary>
        private static string TryReadString(ulong fieldAddr)
        {
            try
            {
                var ptr = Memory.ReadPtr(fieldAddr);
                return ptr.IsValidVirtualAddress() ? Memory.ReadUnityString(ptr) : null;
            }
            catch { return null; }
        }

        /// <summary>
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

                // 4. Re-read hideout area levels from memory
                ReadAreas();

                return
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
                                    Id:              entry.BsgId,
                                    Name:            entry.Name,
                                    TraderPrice:     entry.TraderPrice,
                                    BestTraderName:  entry.BestTraderName,
                                    FleaPrice:       entry.FleaPrice,
                                    StackCount:      Math.Max(1, stackCount)));
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
            Base                = 0;
            StashGridPtr        = 0;
            AreasControllerBase = 0;
            Items               = [];
            Areas               = [];
            NeededItemIds       = FrozenSet<string>.Empty;
        }

        /// <summary>
        /// Walks the entire GOM linked list and logs every GameObject name together with
        /// all component class names attached to it. Runs on a background thread.
        /// Output goes to the standard XMLogging / debug output (same as other log lines).
        /// </summary>
        public static void DumpGOM() =>
            Task.Run(() =>
            {
                try
                {
                    var unityBase = Memory.UnityBase;
                    if (unityBase == 0)
                    {
                        XMLogging.WriteLine("[GOM Dump] UnityBase is 0 — not ready.");
                        return;
                    }

                    var gomAddr = GameObjectManager.GetAddr(unityBase);
                    var gom     = GameObjectManager.Get(gomAddr);

                    var sb    = new System.Text.StringBuilder();
                    int goIdx = 0;

                    var current = Memory.ReadValue<LinkedListObject>(gom.ActiveNodes);
                    var last    = Memory.ReadValue<LinkedListObject>(gom.LastActiveNode);

                    sb.AppendLine("[GOM Dump] ============================================================");

                    for (int i = 0; i < 200_000; i++)
                    {
                        if (!current.ThisObject.IsValidVirtualAddress())
                            break;

                        // Read the GameObject
                        string goName;
                        try
                        {
                            var namePtr = Memory.ReadPtr(
                                current.ThisObject + UnityOffsets.GameObject.NameOffset, false);
                            goName = namePtr.IsValidVirtualAddress()
                                ? Memory.ReadString(namePtr, 128, useCache: false) ?? "<null>"
                                : "<no-name>";
                        }
                        catch { goName = "<err>"; }

                        // Read the ComponentArray on this GameObject
                        var components = new List<string>();
                        try
                        {
                            var go = Memory.ReadValue<GameObject>(current.ThisObject, false);
                            var ca = go.Components;
                            if (ca.ArrayBase.IsValidVirtualAddress() && ca.Size > 0)
                            {
                                int count = (int)Math.Min(ca.Size, 64u);
                                var entries = new ComponentArray.Entry[count];
                                Memory.ReadBuffer(ca.ArrayBase, entries.AsSpan());

                                foreach (var entry in entries)
                                {
                                    if (!entry.Component.IsValidVirtualAddress())
                                        continue;
                                    try
                                    {
                                        var ocPtr = Memory.ReadPtr(
                                            entry.Component + UnityOffsets.Component.ObjectClassOffset,
                                            false);
                                        if (!ocPtr.IsValidVirtualAddress()) continue;

                                        var name = ObjectClass.ReadName(ocPtr, 128, false);
                                        if (!string.IsNullOrWhiteSpace(name))
                                            components.Add(name);
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch { }

                        sb.AppendLine($"[{goIdx++,5}] 0x{current.ThisObject:X16}  \"{goName}\"");
                        foreach (var c in components)
                            sb.AppendLine($"         component: {c}");

                        if (current.ThisObject == last.ThisObject)
                            break;

                        try { current = Memory.ReadValue<LinkedListObject>(current.NextObjectLink); }
                        catch { break; }
                    }

                    sb.AppendLine($"[GOM Dump] Total GameObjects: {goIdx}");
                    sb.AppendLine("[GOM Dump] ============================================================");

                    // Write in one shot so it doesn't interleave with other log lines
                    XMLogging.WriteLine(sb.ToString());
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"[GOM Dump] ERROR: {ex.Message}");
                }
            });
    }
}
