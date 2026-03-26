using System.Collections.Frozen;
using eft_dma_radar.Common.DMA.ScatterAPI;
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

        /// <summary>
        /// Maps each needed item template ID to the maximum <see cref="HideoutRequirement.StillNeeded"/>
        /// count across all requirements that reference it.
        /// Rebuilt after every <see cref="ReadAreas"/> call.
        /// </summary>
        public FrozenDictionary<string, int> NeededItemCounts { get; private set; } = FrozenDictionary<string, int>.Empty;

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
                if (Memory.InRaid)
                {
                    XMLogging.WriteLine("[HideoutManager] TryFind skipped — player is in raid.");
                    return false;
                }

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

                var gridsPtr = Memory.ReadPtrChain(behaviour,
                    [OffStashCtrl, OffInvCtrl, OffInventory, OffStash, OffGrids]);
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
            if (Memory.InRaid)
                return;
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
        /// Uses scatter reads to minimise the number of DMA round-trips.
        /// Results are stored in <see cref="Areas"/>.
        /// </summary>
        public void ReadAreas()
        {
            if (Memory.InRaid)
                return;
            if (!IsAreasValid)
                return;
            try
            {
                // ── Round 1 – dict pointer (dependent chain, must be sequential) ────────
                var dictPtr = Memory.ReadPtr(AreasControllerBase + OffAreas);
                if (!dictPtr.IsValidVirtualAddress()) return;

                // ── Round 2 – count + entriesPtr ─────────────────────────────────────────
                int count;
                ulong entriesPtr;
                using (var r2 = ScatterReadRound.Get(false))
                {
                    r2[0].AddEntry<int>(0, dictPtr + DictCountOff);
                    r2[0].AddEntry<ulong>(1, dictPtr + DictEntriesOff);
                    r2.Run();
                    if (!r2[0].TryGetResult<int>(0, out count) || count <= 0 || count > 64) return;
                    if (!r2[0].TryGetResult<ulong>(1, out entriesPtr) || !entriesPtr.IsValidVirtualAddress()) return;
                }

                var dataBase = entriesPtr + DictDataOff;

                // ── Round 3 – per-entry: areaType + areaPtr ───────────────────────────────
                var areaTypes = new int[count];
                var areaPtrs  = new ulong[count];
                using (var r3 = ScatterReadRound.Get(false))
                {
                    for (int i = 0; i < count; i++)
                    {
                        var entry = dataBase + (ulong)(i * DictEntrySize);
                        r3[0].AddEntry<int>(i, entry + 8);
                        r3[1].AddEntry<ulong>(i, entry + (uint)DictValueOff);
                    }
                    r3.Run();
                    for (int i = 0; i < count; i++)
                    {
                        r3[0].TryGetResult(i, out areaTypes[i]);
                        r3[1].TryGetResult(i, out areaPtrs[i]);
                    }
                }

                // ── Round 4 – per-area: dataPtr ───────────────────────────────────────────
                var dataPtrs = new ulong[count];
                using (var r4 = ScatterReadRound.Get(false))
                {
                    for (int i = 0; i < count; i++)
                        if (areaPtrs[i].IsValidVirtualAddress())
                            r4[0].AddEntry<ulong>(i, areaPtrs[i] + OffAreaData);
                    r4.Run();
                    for (int i = 0; i < count; i++)
                        r4[0].TryGetResult(i, out dataPtrs[i]);
                }

                // ── Round 5 – per-area: level + status ────────────────────────────────────
                var levels   = new int[count];
                var statuses = new int[count];
                using (var r5 = ScatterReadRound.Get(false))
                {
                    for (int i = 0; i < count; i++)
                        if (dataPtrs[i].IsValidVirtualAddress())
                        {
                            r5[0].AddEntry<int>(i, dataPtrs[i] + OffCurLevel);
                            r5[1].AddEntry<int>(i, dataPtrs[i] + OffStatus);
                        }
                    r5.Run();
                    for (int i = 0; i < count; i++)
                    {
                        r5[0].TryGetResult(i, out levels[i]);
                        r5[1].TryGetResult(i, out statuses[i]);
                    }
                }

                // Identify areas that can still be upgraded
                var upgIdx = Enumerable.Range(0, count)
                    .Where(i => areaPtrs[i].IsValidVirtualAddress()
                             && (EAreaStatus)statuses[i] != EAreaStatus.NoFutureUpgrades)
                    .ToList();

                // ── Round 6 – upgradeable areas: arrayPtr (_areaLevels) ───────────────────
                var arrayPtrs = new ulong[count];
                using (var r6 = ScatterReadRound.Get(false))
                {
                    foreach (var i in upgIdx)
                        r6[0].AddEntry<ulong>(i, areaPtrs[i] + OffAreaLevels);
                    r6.Run();
                    foreach (var i in upgIdx)
                        r6[0].TryGetResult(i, out arrayPtrs[i]);
                }

                // ── Round 7 – arrayCount + levelObjPtr per upgradeable area ───────────────
                var arrayCounts  = new int[count];
                var levelObjPtrs = new ulong[count];
                using (var r7 = ScatterReadRound.Get(false))
                {
                    foreach (var i in upgIdx)
                    {
                        if (!arrayPtrs[i].IsValidVirtualAddress()) continue;
                        var targetIdx = levels[i] + 1;
                        r7[0].AddEntry<int>(i, arrayPtrs[i] + MemArray<ulong>.CountOffset);
                        r7[1].AddEntry<ulong>(i,
                            arrayPtrs[i] + MemArray<ulong>.ArrBaseOffset
                            + (ulong)(targetIdx * (int)UnityOffsets.ManagedArray.ElementSize));
                    }
                    r7.Run();
                    foreach (var i in upgIdx)
                    {
                        r7[0].TryGetResult(i, out arrayCounts[i]);
                        r7[1].TryGetResult(i, out levelObjPtrs[i]);
                    }
                }

                var validUpgIdx = upgIdx
                    .Where(i => levelObjPtrs[i].IsValidVirtualAddress()
                             && arrayCounts[i] > levels[i] + 1)
                    .ToList();

                // ── Round 8 – stagePtr per valid area ──────────────────────────────────────
                var stagePtrs = new ulong[count];
                using (var r8 = ScatterReadRound.Get(false))
                {
                    foreach (var i in validUpgIdx)
                        r8[0].AddEntry<ulong>(i, levelObjPtrs[i] + OffStage);
                    r8.Run();
                    foreach (var i in validUpgIdx)
                        r8[0].TryGetResult(i, out stagePtrs[i]);
                }

                // ── Round 9 – relReqPtr ────────────────────────────────────────────────────
                var relReqPtrs = new ulong[count];
                using (var r9 = ScatterReadRound.Get(false))
                {
                    foreach (var i in validUpgIdx)
                        if (stagePtrs[i].IsValidVirtualAddress())
                            r9[0].AddEntry<ulong>(i, stagePtrs[i] + OffRequirements);
                    r9.Run();
                    foreach (var i in validUpgIdx)
                        r9[0].TryGetResult(i, out relReqPtrs[i]);
                }

                // ── Round 10 – listPtr ─────────────────────────────────────────────────────
                var listPtrs = new ulong[count];
                using (var r10 = ScatterReadRound.Get(false))
                {
                    foreach (var i in validUpgIdx)
                        if (relReqPtrs[i].IsValidVirtualAddress())
                            r10[0].AddEntry<ulong>(i, relReqPtrs[i] + OffRelData);
                    r10.Run();
                    foreach (var i in validUpgIdx)
                        r10[0].TryGetResult(i, out listPtrs[i]);
                }

                // ── Round 11 – reqCount + itemsArrPtr per area ─────────────────────────────
                var reqCounts    = new int[count];
                var itemsArrPtrs = new ulong[count];
                using (var r11 = ScatterReadRound.Get(false))
                {
                    foreach (var i in validUpgIdx)
                        if (listPtrs[i].IsValidVirtualAddress())
                        {
                            r11[0].AddEntry<int>(i, listPtrs[i] + MemList<ulong>.CountOffset);
                            r11[1].AddEntry<ulong>(i, listPtrs[i] + MemList<ulong>.ArrOffset);
                        }
                    r11.Run();
                    foreach (var i in validUpgIdx)
                    {
                        r11[0].TryGetResult(i, out reqCounts[i]);
                        r11[1].TryGetResult(i, out itemsArrPtrs[i]);
                    }
                }

                // Build flat list of (areaIdx, reqSlot) — one entry per requirement pointer
                var flatMap = new List<(int areaIdx, int slot)>();
                foreach (var i in validUpgIdx)
                {
                    int rc = reqCounts[i];
                    if (rc <= 0 || rc > 256 || !itemsArrPtrs[i].IsValidVirtualAddress()) continue;
                    for (int j = 0; j < rc; j++)
                        flatMap.Add((i, j));
                }

                int flat = flatMap.Count;
                var reqPtrs    = new ulong[flat];
                var fulfilled  = new bool[flat];
                var vtablePtrs = new ulong[flat];
                var namePtrs   = new ulong[flat];

                // ── Round 12 – reqPtr per flat requirement ─────────────────────────────────
                using (var r12 = ScatterReadRound.Get(false))
                {
                    for (int k = 0; k < flat; k++)
                    {
                        var (ai, slot) = flatMap[k];
                        var dataStart  = itemsArrPtrs[ai] + MemList<ulong>.ArrStartOffset;
                        r12[0].AddEntry<ulong>(k, dataStart + (ulong)(slot * (int)UnityOffsets.ManagedArray.ElementSize));
                    }
                    r12.Run();
                    for (int k = 0; k < flat; k++)
                        r12[0].TryGetResult(k, out reqPtrs[k]);
                }

                // ── Round 13 – fulfilled + vtablePtr ──────────────────────────────────────
                using (var r13 = ScatterReadRound.Get(false))
                {
                    for (int k = 0; k < flat; k++)
                        if (reqPtrs[k].IsValidVirtualAddress())
                        {
                            r13[0].AddEntry<bool>(k, reqPtrs[k] + OffReqFulfilled);
                            r13[1].AddEntry<ulong>(k, reqPtrs[k]);  // vtable is at +0x0
                        }
                    r13.Run();
                    for (int k = 0; k < flat; k++)
                    {
                        r13[0].TryGetResult(k, out fulfilled[k]);
                        r13[1].TryGetResult(k, out vtablePtrs[k]);
                    }
                }

                // ── Round 14 – namePtr (vtable + 0x10) ────────────────────────────────────
                using (var r14 = ScatterReadRound.Get(false))
                {
                    for (int k = 0; k < flat; k++)
                        if (vtablePtrs[k].IsValidVirtualAddress())
                            r14[0].AddEntry<ulong>(k, vtablePtrs[k] + 0x10);
                    r14.Run();
                    for (int k = 0; k < flat; k++)
                        r14[0].TryGetResult(k, out namePtrs[k]);
                }

                // Sequential class name reads — UTF-8 C strings, cached, ~10 unique types
                var classNames = new string[flat];
                for (int k = 0; k < flat; k++)
                    if (namePtrs[k].IsValidVirtualAddress())
                        classNames[k] = Memory.ReadString(namePtrs[k], 64, useCache: true);

                // ── Round 15 – type-specific fields (multi-index) ─────────────────────────
                // r[0] = field @ 0x48 (ptr: tplId / traderId)
                // r[1] = baseCount @ 0x5C (int: Item/Tool)
                // r[2] = userCount @ 0x54 (int: Item/Tool)
                // r[3] = areaType  @ 0x38 (int: Area requirement)
                // r[4] = reqLevel  @ 0x3C (int: Area requirement)
                // r[5] = skillName @ 0x38 (ptr: Skill)
                // r[6] = skillLevel / loyaltyLevel @ 0x40 (int)
                var field48Ptrs   = new ulong[flat];
                var baseCounts    = new int[flat];
                var userCounts    = new int[flat];
                var areaTypeVals  = new int[flat];
                var reqLevels     = new int[flat];
                var skillNamePtrs = new ulong[flat];
                var intAt40       = new int[flat];

                using (var r15 = ScatterReadRound.Get(false))
                {
                    for (int k = 0; k < flat; k++)
                    {
                        var cn = classNames[k];
                        if (cn is null || !reqPtrs[k].IsValidVirtualAddress()) continue;

                        if (cn.Contains("Item", StringComparison.OrdinalIgnoreCase)
                         || cn.Contains("Tool", StringComparison.OrdinalIgnoreCase))
                        {
                            r15[0].AddEntry<ulong>(k, reqPtrs[k] + 0x48);
                            r15[1].AddEntry<int>(k, reqPtrs[k] + OffReqBaseCount);
                            r15[2].AddEntry<int>(k, reqPtrs[k] + OffReqUserCount);
                        }
                        else if (cn.Contains("Area", StringComparison.OrdinalIgnoreCase))
                        {
                            r15[3].AddEntry<int>(k, reqPtrs[k] + 0x38);
                            r15[4].AddEntry<int>(k, reqPtrs[k] + 0x3C);
                        }
                        else if (cn.Contains("Skill", StringComparison.OrdinalIgnoreCase))
                        {
                            r15[5].AddEntry<ulong>(k, reqPtrs[k] + 0x38);
                            r15[6].AddEntry<int>(k, reqPtrs[k] + 0x40);
                        }
                        else if (cn.Contains("Loyalty", StringComparison.OrdinalIgnoreCase))
                        {
                            r15[0].AddEntry<ulong>(k, reqPtrs[k] + 0x48); // traderId ptr
                            r15[6].AddEntry<int>(k, reqPtrs[k] + 0x40);   // loyaltyLevel
                        }
                    }
                    r15.Run();
                    for (int k = 0; k < flat; k++)
                    {
                        r15[0].TryGetResult(k, out field48Ptrs[k]);
                        r15[1].TryGetResult(k, out baseCounts[k]);
                        r15[2].TryGetResult(k, out userCounts[k]);
                        r15[3].TryGetResult(k, out areaTypeVals[k]);
                        r15[4].TryGetResult(k, out reqLevels[k]);
                        r15[5].TryGetResult(k, out skillNamePtrs[k]);
                        r15[6].TryGetResult(k, out intAt40[k]);
                    }
                }

                // ── Round 16 – UnicodeString scatter for string fields ─────────────────────
                // r[0] = field48 (tplId for Item/Tool, traderId for Loyalty) at ptr + 0x14
                // r[1] = skillName at ptr + 0x14
                const int StringCB = 128;
                var tplOrTraderIds = new string[flat];
                var skillNames     = new string[flat];

                using (var r16 = ScatterReadRound.Get(false))
                {
                    for (int k = 0; k < flat; k++)
                    {
                        var cn = classNames[k];
                        if (cn is null) continue;

                        if ((cn.Contains("Item", StringComparison.OrdinalIgnoreCase)
                          || cn.Contains("Tool", StringComparison.OrdinalIgnoreCase)
                          || cn.Contains("Loyalty", StringComparison.OrdinalIgnoreCase))
                         && field48Ptrs[k].IsValidVirtualAddress())
                        {
                            r16[0].AddEntry<UnicodeString>(k, field48Ptrs[k] + 0x14, StringCB);
                        }
                        else if (cn.Contains("Skill", StringComparison.OrdinalIgnoreCase)
                              && skillNamePtrs[k].IsValidVirtualAddress())
                        {
                            r16[1].AddEntry<UnicodeString>(k, skillNamePtrs[k] + 0x14, StringCB);
                        }
                    }
                    r16.Run();
                    for (int k = 0; k < flat; k++)
                    {
                        if (r16[0].TryGetResult<UnicodeString>(k, out var s0)) tplOrTraderIds[k] = s0;
                        if (r16[1].TryGetResult<UnicodeString>(k, out var s1)) skillNames[k]     = s1;
                    }
                }

                // ── Build result list ──────────────────────────────────────────────────────
                // Group requirements back to their area index
                var reqsByArea = new Dictionary<int, List<HideoutRequirement>>();
                foreach (var i in validUpgIdx)
                    reqsByArea[i] = new List<HideoutRequirement>(reqCounts[i]);

                for (int k = 0; k < flat; k++)
                {
                    var (ai, slot) = flatMap[k];
                    if (!reqsByArea.TryGetValue(ai, out var reqList)) continue;

                    var cn = classNames[k];
                    if (cn is null || !reqPtrs[k].IsValidVirtualAddress()) continue;

                    HideoutRequirement req;
                    bool isFulfilled = fulfilled[k];

                    if (cn.Contains("Tool", StringComparison.OrdinalIgnoreCase))
                    {
                        req = BuildItemOrToolReq(ERequirementType.Tool, isFulfilled,
                            tplOrTraderIds[k], baseCounts[k], userCounts[k]);
                    }
                    else if (cn.Contains("Item", StringComparison.OrdinalIgnoreCase))
                    {
                        req = BuildItemOrToolReq(ERequirementType.Item, isFulfilled,
                            tplOrTraderIds[k], baseCounts[k], userCounts[k]);
                    }
                    else if (cn.Contains("Area", StringComparison.OrdinalIgnoreCase))
                    {
                        req = new HideoutRequirement(ERequirementType.Area, isFulfilled,
                            RequiredArea: (EAreaType)areaTypeVals[k], RequiredLevel: reqLevels[k]);
                    }
                    else if (cn.Contains("Skill", StringComparison.OrdinalIgnoreCase))
                    {
                        req = new HideoutRequirement(ERequirementType.Skill, isFulfilled,
                            SkillName: skillNames[k], SkillLevel: intAt40[k]);
                    }
                    else if (cn.Contains("Loyalty", StringComparison.OrdinalIgnoreCase))
                    {
                        string traderId   = tplOrTraderIds[k];
                        string traderName = null;
                        if (traderId is not null)
                            EftDataManager.AllTraders.TryGetValue(traderId, out traderName);
                        req = new HideoutRequirement(ERequirementType.TraderLoyalty, isFulfilled,
                            TraderId: traderId, TraderName: traderName, LoyaltyLevel: intAt40[k]);
                    }
                    else if (cn.Contains("Trader", StringComparison.OrdinalIgnoreCase))
                        req = new HideoutRequirement(ERequirementType.TraderUnlock, isFulfilled);
                    else if (cn.Contains("Quest", StringComparison.OrdinalIgnoreCase))
                        req = new HideoutRequirement(ERequirementType.QuestComplete, isFulfilled);
                    else
                        req = new HideoutRequirement(ERequirementType.Resource, isFulfilled);

                    var label = $"{(EAreaType)areaTypes[ai]} lv{levels[ai]}→{levels[ai] + 1}";
                    XMLogging.WriteLine($"[HideoutManager] [{label}] req[{slot}] {FormatReq(req)}");
                    reqList.Add(req);
                }

                var areas = new List<HideoutAreaInfo>(count);
                for (int i = 0; i < count; i++)
                {
                    if (!areaPtrs[i].IsValidVirtualAddress()) continue;
                    var reqs = reqsByArea.TryGetValue(i, out var list) ? list : (IReadOnlyList<HideoutRequirement>)[];
                    areas.Add(new HideoutAreaInfo(
                        (EAreaType)areaTypes[i],
                        levels[i],
                        (EAreaStatus)statuses[i],
                        reqs));
                }

                Areas = areas;

                // Rebuild the set of item template IDs still needed for upgrades
                var neededReqs = areas
                    .SelectMany(a => a.NextLevelRequirements)
                    .Where(r => r.Type is ERequirementType.Item or ERequirementType.Tool
                                && r.StillNeeded > 0
                                && r.ItemTemplateId is not null)
                    .ToList();

                NeededItemIds = neededReqs
                    .Select(r => r.ItemTemplateId)
                    .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

                // For each template ID keep the highest StillNeeded count across all requirements
                NeededItemCounts = neededReqs
                    .GroupBy(r => r.ItemTemplateId, StringComparer.OrdinalIgnoreCase)
                    .ToFrozenDictionary(
                        g => g.Key,
                        g => g.Max(r => r.StillNeeded),
                        StringComparer.OrdinalIgnoreCase);

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
        /// Constructs an Item or Tool requirement from pre-read scattered field values.
        /// </summary>
        private static HideoutRequirement BuildItemOrToolReq(
            ERequirementType type, bool fulfilled, string tplId, int required, int current)
        {
            string itemName = null;
            if (tplId is not null && EftDataManager.AllItems.TryGetValue(tplId, out var entry))
                itemName = entry.ShortName;
            return new HideoutRequirement(type, fulfilled,
                ItemTemplateId: tplId,
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
        /// Pulls fresh market prices from Tarkov.Dev, re-scans the stash pointer chain if
        /// needed, then re-reads all item stacks. Returns a short status message for the UI.
        /// </summary>
        public async Task<string> RefreshAsync()
        {
            try
            {
                if (Memory.InRaid)
                {
                    XMLogging.WriteLine("[HideoutManager] RefreshAsync skipped — player is in raid.");
                    return "Not available in raid — return to your hideout.";
                }

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
        /// Top-level items in each grid are read via scatter; recursive container contents
        /// remain sequential (uncommon path, bounded by recurseDepth).
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

                    int itemCount = itemList.Count;
                    if (itemCount == 0) continue;

                    // ── Scatter A – templatePtr per item ─────────────────────────────────
                    var templatePtrs = new ulong[itemCount];
                    using (var rA = ScatterReadRound.Get(false))
                    {
                        for (int k = 0; k < itemCount; k++)
                            rA[0].AddEntry<ulong>(k, itemList[k] + LootItem.Template);
                        rA.Run();
                        for (int k = 0; k < itemCount; k++)
                            rA[0].TryGetResult(k, out templatePtrs[k]);
                    }

                    // ── Scatter B – MongoID struct + stackCount + childGridsPtr ──────────
                    // r[0] = MongoID (value type, reads struct at templatePtr + ItemTemplate._id)
                    // r[1] = stackCount (int at item + StackObjectsCount)
                    // r[2] = childGridsPtr (ulong at item + LootItemMod.Grids)
                    var mongoIds       = new SDK.Types.MongoID[itemCount];
                    var stackCounts    = new int[itemCount];
                    var childGridsPtrs = new ulong[itemCount];
                    using (var rB = ScatterReadRound.Get(false))
                    {
                        for (int k = 0; k < itemCount; k++)
                        {
                            if (!templatePtrs[k].IsValidVirtualAddress()) continue;
                            rB[0].AddEntry<SDK.Types.MongoID>(k, templatePtrs[k] + ItemTemplate._id);
                            rB[1].AddEntry<int>(k, itemList[k] + LootItem.StackObjectsCount);
                            rB[2].AddEntry<ulong>(k, itemList[k] + LootItemMod.Grids);
                        }
                        rB.Run();
                        for (int k = 0; k < itemCount; k++)
                        {
                            rB[0].TryGetResult(k, out mongoIds[k]);
                            rB[1].TryGetResult(k, out stackCounts[k]);
                            rB[2].TryGetResult(k, out childGridsPtrs[k]);
                        }
                    }

                    // ── Scatter C – UnicodeString for each MongoID.StringID ───────────────
                    var ids = new string[itemCount];
                    using (var rC = ScatterReadRound.Get(false))
                    {
                        for (int k = 0; k < itemCount; k++)
                        {
                            var sid = mongoIds[k].StringID;
                            if (sid.IsValidVirtualAddress())
                                rC[0].AddEntry<UnicodeString>(k, sid + 0x14, 48);
                        }
                        rC.Run();
                        for (int k = 0; k < itemCount; k++)
                            if (rC[0].TryGetResult<UnicodeString>(k, out var s)) ids[k] = s;
                    }

                    // ── Resolve and record ────────────────────────────────────────────────
                    for (int k = 0; k < itemCount; k++)
                    {
                        try
                        {
                            var id = ids[k];
                            if (id is null) continue;

                            if (EftDataManager.AllItems.TryGetValue(id, out var entry))
                            {
                                results.Add(new StashItem(
                                    Id:             entry.BsgId,
                                    Name:           entry.Name,
                                    TraderPrice:    entry.TraderPrice,
                                    BestTraderName: entry.BestTraderName,
                                    FleaPrice:      entry.FleaPrice,
                                    StackCount:     Math.Max(1, stackCounts[k])));
                            }

                            // Recurse into nested containers (bags, cases, etc.)
                            GetItemsInGrid(childGridsPtrs[k], results, recurseDepth);
                        }
                        catch { }
                    }
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
            NeededItemCounts    = FrozenDictionary<string, int>.Empty;
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
