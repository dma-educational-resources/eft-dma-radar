using System.Collections.Frozen;
using eft_dma_radar.Silk.DMA;
using eft_dma_radar.Silk.DMA.ScatterAPI;
using eft_dma_radar.Silk.Misc;
using eft_dma_radar.Silk.Misc.Data;
using eft_dma_radar.Silk.Tarkov.Unity;
using eft_dma_radar.Silk.Tarkov.Unity.Collections;
using eft_dma_radar.Silk.Tarkov.Unity.IL2CPP;
using static SDK.Offsets;

namespace eft_dma_radar.Silk.Tarkov.Hideout
{
    // ── Enums ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps EFT's EAreaType enum integer values to a readable name.
    /// Values confirmed from IL2CPP dump.
    /// </summary>
    internal enum EAreaType
    {
        Vents = 0,
        Security = 1,
        WaterCloset = 2,
        Stash = 3,
        Generator = 4,
        Heating = 5,
        WaterCollector = 6,
        MedStation = 7,
        Kitchen = 8,
        RestSpace = 9,
        Workbench = 10,
        IntelligenceCenter = 11,
        ShootingRange = 12,
        Library = 13,
        ScavCase = 14,
        Illumination = 15,
        PlaceOfFame = 16,
        AirFilteringUnit = 17,
        SolarPower = 18,
        BoozeGenerator = 19,
        BitcoinFarm = 20,
        ChristmasIllumination = 21,
        EmergencyWall = 22,
        Gym = 23,
        WeaponStand = 24,
        WeaponStandSecondary = 25,
        EquipmentPresetsStand = 26,
        CircleOfCultists = 27,
    }

    /// <summary>
    /// EFT hideout area upgrade/construction status. Values confirmed from IL2CPP dump.
    /// </summary>
    internal enum EAreaStatus
    {
        NotSet = 0,
        LockedToConstruct = 1,
        ReadyToConstruct = 2,
        Constructing = 3,
        ReadyToInstallConstruct = 4,
        LockedToUpgrade = 5,
        ReadyToUpgrade = 6,
        Upgrading = 7,
        ReadyToInstallUpgrade = 8,
        NoFutureUpgrades = 9,
        AutoUpgrading = 10,
    }

    /// <summary>
    /// Discriminates the kind of requirement on a hideout upgrade stage.
    /// </summary>
    internal enum ERequirementType
    {
        Area = 0,
        Item = 1,
        TraderUnlock = 2,
        TraderLoyalty = 3,
        Skill = 4,
        Resource = 5,
        Tool = 6,
        QuestComplete = 7,
        Health = 8,
        BodyPartBuff = 9,
        GameVersion = 10,
    }

    // ── Records ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// A single requirement read from a hideout upgrade stage.
    /// Fields are populated based on <see cref="Type"/>; irrelevant fields remain at their defaults.
    /// </summary>
    internal sealed record HideoutRequirement(
        ERequirementType Type,
        bool Fulfilled,
        /// <summary>Item BSG template id (only when <see cref="Type"/> is Item or Tool).</summary>
        string? ItemTemplateId = null,
        /// <summary>Resolved item name from market data (only when <see cref="Type"/> is Item or Tool).</summary>
        string? ItemName = null,
        /// <summary>Number of items required (only when <see cref="Type"/> is Item or Tool).</summary>
        int RequiredCount = 0,
        /// <summary>Number of matching items the player currently has in stash (only when <see cref="Type"/> is Item or Tool).</summary>
        int CurrentCount = 0,
        /// <summary>Required area type (only when <see cref="Type"/> is Area).</summary>
        EAreaType RequiredArea = default,
        /// <summary>Required area level (only when <see cref="Type"/> is Area).</summary>
        int RequiredLevel = 0,
        /// <summary>Skill name, e.g. "Strength" (only when <see cref="Type"/> is Skill).</summary>
        string? SkillName = null,
        /// <summary>Required skill level (only when <see cref="Type"/> is Skill).</summary>
        int SkillLevel = 0,
        /// <summary>Trader BSG id (only when <see cref="Type"/> is TraderLoyalty).</summary>
        string? TraderId = null,
        /// <summary>Required loyalty level (only when <see cref="Type"/> is TraderLoyalty).</summary>
        int LoyaltyLevel = 0)
    {
        /// <summary>How many more items are still needed (only meaningful for Item/Tool).</summary>
        public int StillNeeded => Math.Max(0, RequiredCount - CurrentCount);
    }

    /// <summary>
    /// Current level snapshot for one hideout area read from memory.
    /// </summary>
    internal sealed record HideoutAreaInfo(
        EAreaType AreaType,
        int CurrentLevel,
        EAreaStatus Status,
        IReadOnlyList<HideoutRequirement> NextLevelRequirements)
    {
        /// <summary>True when the area has no further upgrades available.</summary>
        public bool IsMaxLevel => Status == EAreaStatus.NoFutureUpgrades;
    }

    /// <summary>
    /// A single item resolved from the hideout stash.
    /// </summary>
    internal sealed record StashItem(
        string Id,
        string Name,
        long TraderPrice,
        long FleaPrice,
        int StackCount)
    {
        /// <summary>Best sell value for this stack (max of trader vs flea × stack count).</summary>
        public long BestPrice => Math.Max(TraderPrice, FleaPrice) * StackCount;
        /// <summary>True when flea beats trader for this item.</summary>
        public bool SellOnFlea => FleaPrice > TraderPrice;
    }

    // ── HideoutManager ───────────────────────────────────────────────────────────

    /// <summary>
    /// Manages reading the hideout stash and area upgrade levels via the IL2CPP GOM.
    /// Confirmed chain: HideoutArea(+0xA8) → HideoutAreaStashController(+0x10)
    ///   → OfflineInventoryController(+0x100) → Inventory(+0x20) → Grid[](+0x78)
    /// </summary>
    internal sealed class HideoutManager
    {
        private const string HideoutAreaClassName = "HideoutArea";
        private const string HideoutControllerClassName = "HideoutController";

        // ── Stash pointer-chain ──────────────────────────────────────────────
        // HideoutArea(+0xA8) → HideoutAreaStashController(+0x10)
        //   → OfflineInventoryController(+0x100) → Inventory(+0x20) → Grid[](+0x78)
        private const uint OffStashCtrl = 0xA8;
        private const uint OffInvCtrl   = 0x10;
        private const uint OffInventory = 0x100;
        private const uint OffStash     = 0x20;
        private const uint OffGrids     = 0x78;

        // ── HideoutController._areas Dictionary<EAreaType, HideoutArea> ──────
        private const uint OffAreas        = 0x80;
        private const uint DictCountOff    = 0x20;
        private const uint DictEntriesOff  = 0x18;
        private const ulong DictDataOff    = 0x20;
        private const int DictEntrySize    = 24;
        private const uint DictValueOff    = 16;

        // ── HideoutArea fields ───────────────────────────────────────────────
        private const uint OffAreaData   = 0x70;
        private const uint OffAreaLevels = 0x48;

        // ── AreaData fields ──────────────────────────────────────────────────
        private const uint OffCurLevel = 0xA8;
        private const uint OffStatus   = 0xC8;

        // ── HideoutAreaLevel._stage ──────────────────────────────────────────
        private const uint OffStage = 0xA0;

        // ── Stage fields ─────────────────────────────────────────────────────
        private const uint OffRequirements = 0x18;

        // ── RelatedRequirements.Data ─────────────────────────────────────────
        private const uint OffRelData = 0x10;

        // ── Requirement base fields ──────────────────────────────────────────
        private const uint OffReqFulfilled = 0x18;

        // ── ItemRequirement / ToolRequirement item count fields ──────────────
        private const uint OffReqUserCount = 0x54;
        private const uint OffReqBaseCount = 0x5C;

        // ── Managed array element size ───────────────────────────────────────
        private const int ManagedElementSize = 8;

        // ── Cached klass pointers (resolved once, then reused) ────────────
        private static ulong _cachedHideoutAreaKlass;
        private static ulong _cachedHideoutControllerKlass;

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
        public long TotalBestValue { get; private set; }
        /// <summary>Sum of trader prices for every item in the stash.</summary>
        public long TotalTraderValue { get; private set; }
        /// <summary>Sum of flea prices for every item in the stash.</summary>
        public long TotalFleaValue { get; private set; }

        public bool IsValid => Base.IsValidVirtualAddress() && StashGridPtr.IsValidVirtualAddress();
        public bool IsAreasValid => AreasControllerBase.IsValidVirtualAddress();

        /// <summary>
        /// Scans the GOM for the HideoutArea component and walks the pointer chain
        /// to the stash Grid[] array. Returns true when the grid is reachable.
        /// Uses class name lookup with klass pointer caching for fast subsequent calls.
        /// </summary>
        public bool TryFind()
        {
            try
            {
                // Only works in hideout or main menu — not during an actual raid
                if (Memory.InRaid)
                {
                    Log.WriteLine("[HideoutManager] TryFind skipped — player is in raid.");
                    return false;
                }

                var gomAddr = Memory.GOM;
                if (!gomAddr.IsValidVirtualAddress())
                    return false;

                var gom = GOM.Get(gomAddr);

                // ── Resolve HideoutArea ──────────────────────────────────────
                var behaviour = FindBehaviourCached(gom,
                    ref _cachedHideoutAreaKlass,
                    HideoutAreaClassName);

                if (!behaviour.IsValidVirtualAddress())
                {
                    Log.WriteLine($"[HideoutManager] \"{HideoutAreaClassName}\" not found in GOM.");
                    return false;
                }

                var gridsPtr = Memory.ReadPtrChain(behaviour,
                    [OffStashCtrl, OffInvCtrl, OffInventory, OffStash, OffGrids]);
                if (!gridsPtr.IsValidVirtualAddress()) return false;

                Base = behaviour;
                StashGridPtr = gridsPtr;

                // ── Resolve HideoutController ────────────────────────────────
                var ctrlBehaviour = FindBehaviourCached(gom,
                    ref _cachedHideoutControllerKlass,
                    HideoutControllerClassName);

                if (ctrlBehaviour.IsValidVirtualAddress())
                {
                    AreasControllerBase = ctrlBehaviour;
                    Log.WriteLine($"[HideoutManager] HideoutController @ 0x{AreasControllerBase:X}");
                }
                else
                {
                    Log.WriteLine("[HideoutManager] HideoutController not found in GOM.");
                }

                Log.WriteLine($"[HideoutManager] Ready. Base=0x{Base:X} StashGridPtr=0x{StashGridPtr:X}");
                return true;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[HideoutManager] TryFind error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Resolves a GOM behaviour using a cached klass pointer for fast repeat lookups.
        /// First call uses a reliable class name scan, then caches the klass pointer from
        /// the found object for O(1) subsequent lookups.
        /// </summary>
        private static ulong FindBehaviourCached(GOM gom, ref ulong cachedKlass, string className)
        {
            // Fast path: use cached klass pointer from a previous successful lookup
            if (cachedKlass.IsValidVirtualAddress())
            {
                var result = gom.FindBehaviourByKlassPtr(cachedKlass);
                if (result.IsValidVirtualAddress())
                    return result;

                // Klass pointer went stale (game restart?), clear and fall through
                cachedKlass = 0;
            }

            // Reliable path: class name scan (same approach as WPF)
            var found = gom.FindBehaviourByClassName(className);
            if (!found.IsValidVirtualAddress())
                return 0;

            // Cache klass from found object for fast future lookups
            if (Memory.TryReadPtr(found, out var klass, false) && klass.IsValidVirtualAddress())
            {
                cachedKlass = klass;
                Log.WriteLine($"[HideoutManager] Cached {className} klass=0x{klass:X}");
            }

            return found;
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
                GetAllStashItems(StashGridPtr, items);
                Items = items;

                // Pre-compute totals to avoid LINQ in hot paths
                long totalBest = 0, totalTrader = 0, totalFlea = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    var si = items[i];
                    totalBest += si.BestPrice;
                    totalTrader += si.TraderPrice * si.StackCount;
                    totalFlea += si.FleaPrice * si.StackCount;
                }
                TotalBestValue = totalBest;
                TotalTraderValue = totalTrader;
                TotalFleaValue = totalFlea;

                Log.WriteLine(
                    $"[HideoutManager] Refresh: {Items.Count} item(s) | " +
                    $"best ₽{TotalBestValue:N0} | trader ₽{TotalTraderValue:N0} | flea ₽{TotalFleaValue:N0}");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[HideoutManager] Refresh error: {ex.Message}");
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
                // ── Round 1 – dict pointer (dependent chain, must be sequential) ────
                var dictPtr = Memory.ReadPtr(AreasControllerBase + OffAreas);
                if (!dictPtr.IsValidVirtualAddress()) return;

                // ── Round 2 – count + entriesPtr ─────────────────────────────────────
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

                // ── Round 3 – per-entry: areaType + areaPtr ──────────────────────────
                var areaTypes = new int[count];
                var areaPtrs = new ulong[count];
                using (var r3 = ScatterReadRound.Get(false))
                {
                    for (int i = 0; i < count; i++)
                    {
                        var entry = dataBase + (ulong)(i * DictEntrySize);
                        r3[0].AddEntry<int>(i, entry + 8);
                        r3[1].AddEntry<ulong>(i, entry + DictValueOff);
                    }
                    r3.Run();
                    for (int i = 0; i < count; i++)
                    {
                        r3[0].TryGetResult(i, out areaTypes[i]);
                        r3[1].TryGetResult(i, out areaPtrs[i]);
                    }
                }

                // ── Round 4 – per-area: dataPtr + arrayPtr (_areaLevels) ─────────────
                var dataPtrs = new ulong[count];
                var arrayPtrs = new ulong[count];
                using (var r4 = ScatterReadRound.Get(false))
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (!areaPtrs[i].IsValidVirtualAddress()) continue;
                        r4[0].AddEntry<ulong>(i, areaPtrs[i] + OffAreaData);
                        r4[1].AddEntry<ulong>(i, areaPtrs[i] + OffAreaLevels);
                    }
                    r4.Run();
                    for (int i = 0; i < count; i++)
                    {
                        r4[0].TryGetResult(i, out dataPtrs[i]);
                        r4[1].TryGetResult(i, out arrayPtrs[i]);
                    }
                }

                // ── Round 5 – per-area: level + status ───────────────────────────────
                var levels = new int[count];
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
                var upgIdx = new List<int>(count);
                for (int i = 0; i < count; i++)
                {
                    if (areaPtrs[i].IsValidVirtualAddress()
                        && (EAreaStatus)statuses[i] != EAreaStatus.NoFutureUpgrades)
                        upgIdx.Add(i);
                }

                // ── Round 6 – arrayCount + levelObjPtr per upgradeable area ──────────
                var arrayCounts = new int[count];
                var levelObjPtrs = new ulong[count];
                using (var r6 = ScatterReadRound.Get(false))
                {
                    for (int u = 0; u < upgIdx.Count; u++)
                    {
                        int i = upgIdx[u];
                        if (!arrayPtrs[i].IsValidVirtualAddress()) continue;
                        int targetIdx = levels[i] + 1;
                        r6[0].AddEntry<int>(i, arrayPtrs[i] + MemArray<ulong>.CountOffset);
                        r6[1].AddEntry<ulong>(i,
                            arrayPtrs[i] + MemArray<ulong>.ArrBaseOffset
                            + (ulong)(targetIdx * ManagedElementSize));
                    }
                    r6.Run();
                    for (int u = 0; u < upgIdx.Count; u++)
                    {
                        int i = upgIdx[u];
                        r6[0].TryGetResult(i, out arrayCounts[i]);
                        r6[1].TryGetResult(i, out levelObjPtrs[i]);
                    }
                }

                var validUpgIdx = new List<int>(upgIdx.Count);
                for (int u = 0; u < upgIdx.Count; u++)
                {
                    int i = upgIdx[u];
                    if (levelObjPtrs[i].IsValidVirtualAddress()
                        && arrayCounts[i] > levels[i] + 1)
                        validUpgIdx.Add(i);
                }

                // ── Round 7 – stagePtr per valid area ────────────────────────────────
                var stagePtrs = new ulong[count];
                using (var r7 = ScatterReadRound.Get(false))
                {
                    for (int u = 0; u < validUpgIdx.Count; u++)
                    {
                        int i = validUpgIdx[u];
                        r7[0].AddEntry<ulong>(i, levelObjPtrs[i] + OffStage);
                    }
                    r7.Run();
                    for (int u = 0; u < validUpgIdx.Count; u++)
                    {
                        int i = validUpgIdx[u];
                        r7[0].TryGetResult(i, out stagePtrs[i]);
                    }
                }

                // ── Sequential ptr-chain: stagePtr → relReqPtr → listPtr ─────────────
                var listPtrs = new ulong[count];
                for (int u = 0; u < validUpgIdx.Count; u++)
                {
                    int i = validUpgIdx[u];
                    if (!stagePtrs[i].IsValidVirtualAddress()) continue;
                    try
                    {
                        listPtrs[i] = Memory.ReadPtrChain(stagePtrs[i],
                            [OffRequirements, OffRelData], useCache: false);
                    }
                    catch { /* leave as 0 */ }
                }

                // ── Round 8 – reqCount + itemsArrPtr per area ────────────────────────
                var reqCounts = new int[count];
                var itemsArrPtrs = new ulong[count];
                using (var r8 = ScatterReadRound.Get(false))
                {
                    for (int u = 0; u < validUpgIdx.Count; u++)
                    {
                        int i = validUpgIdx[u];
                        if (!listPtrs[i].IsValidVirtualAddress()) continue;
                        r8[0].AddEntry<int>(i, listPtrs[i] + MemList<ulong>.CountOffset);
                        r8[1].AddEntry<ulong>(i, listPtrs[i] + MemList<ulong>.ArrOffset);
                    }
                    r8.Run();
                    for (int u = 0; u < validUpgIdx.Count; u++)
                    {
                        int i = validUpgIdx[u];
                        r8[0].TryGetResult(i, out reqCounts[i]);
                        r8[1].TryGetResult(i, out itemsArrPtrs[i]);
                    }
                }

                // Build flat list of (areaIdx, reqSlot)
                var flatMap = new List<(int areaIdx, int slot)>();
                for (int u = 0; u < validUpgIdx.Count; u++)
                {
                    int i = validUpgIdx[u];
                    int rc = reqCounts[i];
                    if (rc <= 0 || rc > 256 || !itemsArrPtrs[i].IsValidVirtualAddress()) continue;
                    for (int j = 0; j < rc; j++)
                        flatMap.Add((i, j));
                }

                int flat = flatMap.Count;
                var reqPtrs = new ulong[flat];
                var fulfilled = new bool[flat];
                var vtablePtrs = new ulong[flat];
                var namePtrs = new ulong[flat];

                // ── Round 9 – reqPtr per flat requirement ────────────────────────────
                using (var r9 = ScatterReadRound.Get(false))
                {
                    for (int k = 0; k < flat; k++)
                    {
                        var (ai, slot) = flatMap[k];
                        var dataStart = itemsArrPtrs[ai] + MemList<ulong>.ArrStartOffset;
                        r9[0].AddEntry<ulong>(k, dataStart + (ulong)(slot * ManagedElementSize));
                    }
                    r9.Run();
                    for (int k = 0; k < flat; k++)
                        r9[0].TryGetResult(k, out reqPtrs[k]);
                }

                // ── Round 10 – fulfilled + vtablePtr ─────────────────────────────────
                using (var r10 = ScatterReadRound.Get(false))
                {
                    for (int k = 0; k < flat; k++)
                        if (reqPtrs[k].IsValidVirtualAddress())
                        {
                            r10[0].AddEntry<bool>(k, reqPtrs[k] + OffReqFulfilled);
                            r10[1].AddEntry<ulong>(k, reqPtrs[k]); // vtable at +0x0
                        }
                    r10.Run();
                    for (int k = 0; k < flat; k++)
                    {
                        r10[0].TryGetResult(k, out fulfilled[k]);
                        r10[1].TryGetResult(k, out vtablePtrs[k]);
                    }
                }

                // ── Round 11 – namePtr (vtable + 0x10) ───────────────────────────────
                using (var r11 = ScatterReadRound.Get(false))
                {
                    for (int k = 0; k < flat; k++)
                        if (vtablePtrs[k].IsValidVirtualAddress())
                            r11[0].AddEntry<ulong>(k, vtablePtrs[k] + 0x10);
                    r11.Run();
                    for (int k = 0; k < flat; k++)
                        r11[0].TryGetResult(k, out namePtrs[k]);
                }

                // Sequential class name reads — UTF-8 C strings, cached, ~10 unique types
                var classNames = new string?[flat];
                for (int k = 0; k < flat; k++)
                    if (namePtrs[k].IsValidVirtualAddress())
                        classNames[k] = Memory.ReadString(namePtrs[k], 64, useCache: true);

                // ── Round 12 – type-specific fields (multi-index) ────────────────────
                var field48Ptrs = new ulong[flat];
                var baseCounts = new int[flat];
                var userCounts = new int[flat];
                var areaTypeVals = new int[flat];
                var reqLevels = new int[flat];
                var skillNamePtrs = new ulong[flat];
                var intAt40 = new int[flat];

                using (var r12 = ScatterReadRound.Get(false))
                {
                    for (int k = 0; k < flat; k++)
                    {
                        var cn = classNames[k];
                        if (cn is null || !reqPtrs[k].IsValidVirtualAddress()) continue;

                        if (cn.Contains("Item", StringComparison.OrdinalIgnoreCase)
                         || cn.Contains("Tool", StringComparison.OrdinalIgnoreCase))
                        {
                            r12[0].AddEntry<ulong>(k, reqPtrs[k] + 0x48);
                            r12[1].AddEntry<int>(k, reqPtrs[k] + OffReqBaseCount);
                            r12[2].AddEntry<int>(k, reqPtrs[k] + OffReqUserCount);
                        }
                        else if (cn.Contains("Area", StringComparison.OrdinalIgnoreCase))
                        {
                            r12[3].AddEntry<int>(k, reqPtrs[k] + 0x38);
                            r12[4].AddEntry<int>(k, reqPtrs[k] + 0x3C);
                        }
                        else if (cn.Contains("Skill", StringComparison.OrdinalIgnoreCase))
                        {
                            r12[5].AddEntry<ulong>(k, reqPtrs[k] + 0x38);
                            r12[6].AddEntry<int>(k, reqPtrs[k] + 0x40);
                        }
                        else if (cn.Contains("Loyalty", StringComparison.OrdinalIgnoreCase))
                        {
                            r12[0].AddEntry<ulong>(k, reqPtrs[k] + 0x48);
                            r12[6].AddEntry<int>(k, reqPtrs[k] + 0x40);
                        }
                    }
                    r12.Run();
                    for (int k = 0; k < flat; k++)
                    {
                        r12[0].TryGetResult(k, out field48Ptrs[k]);
                        r12[1].TryGetResult(k, out baseCounts[k]);
                        r12[2].TryGetResult(k, out userCounts[k]);
                        r12[3].TryGetResult(k, out areaTypeVals[k]);
                        r12[4].TryGetResult(k, out reqLevels[k]);
                        r12[5].TryGetResult(k, out skillNamePtrs[k]);
                        r12[6].TryGetResult(k, out intAt40[k]);
                    }
                }

                // ── Round 13 – UnicodeString scatter for string fields ───────────────
                const int StringCB = 128;
                var tplOrTraderIds = new string?[flat];
                var skillNames = new string?[flat];

                using (var r13 = ScatterReadRound.Get(false))
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
                            r13[0].AddEntry<UnicodeString>(k, field48Ptrs[k] + 0x14, StringCB);
                        }
                        else if (cn.Contains("Skill", StringComparison.OrdinalIgnoreCase)
                              && skillNamePtrs[k].IsValidVirtualAddress())
                        {
                            r13[1].AddEntry<UnicodeString>(k, skillNamePtrs[k] + 0x14, StringCB);
                        }
                    }
                    r13.Run();
                    for (int k = 0; k < flat; k++)
                    {
                        if (r13[0].TryGetResult<UnicodeString>(k, out var s0)) tplOrTraderIds[k] = s0;
                        if (r13[1].TryGetResult<UnicodeString>(k, out var s1)) skillNames[k] = s1;
                    }
                }

                // ── Build result list ────────────────────────────────────────────────
                var reqsByArea = new Dictionary<int, List<HideoutRequirement>>(validUpgIdx.Count);
                for (int u = 0; u < validUpgIdx.Count; u++)
                {
                    int i = validUpgIdx[u];
                    reqsByArea[i] = new List<HideoutRequirement>(reqCounts[i]);
                }

                for (int k = 0; k < flat; k++)
                {
                    var (ai, _) = flatMap[k];
                    if (!reqsByArea.TryGetValue(ai, out var reqList)) continue;

                    var cn = classNames[k];
                    if (cn is null || !reqPtrs[k].IsValidVirtualAddress()) continue;

                    bool isFulfilled = fulfilled[k];
                    HideoutRequirement req;

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
                        req = new HideoutRequirement(ERequirementType.TraderLoyalty, isFulfilled,
                            TraderId: tplOrTraderIds[k], LoyaltyLevel: intAt40[k]);
                    }
                    else if (cn.Contains("Trader", StringComparison.OrdinalIgnoreCase))
                        req = new HideoutRequirement(ERequirementType.TraderUnlock, isFulfilled);
                    else if (cn.Contains("Quest", StringComparison.OrdinalIgnoreCase))
                        req = new HideoutRequirement(ERequirementType.QuestComplete, isFulfilled);
                    else
                        req = new HideoutRequirement(ERequirementType.Resource, isFulfilled);

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
                var neededBuilder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int a = 0; a < areas.Count; a++)
                {
                    var reqs = areas[a].NextLevelRequirements;
                    for (int r = 0; r < reqs.Count; r++)
                    {
                        var req = reqs[r];
                        if (req.Type is not (ERequirementType.Item or ERequirementType.Tool)) continue;
                        if (req.StillNeeded <= 0 || req.ItemTemplateId is null) continue;

                        if (neededBuilder.TryGetValue(req.ItemTemplateId, out int existing))
                            neededBuilder[req.ItemTemplateId] = Math.Max(existing, req.StillNeeded);
                        else
                            neededBuilder[req.ItemTemplateId] = req.StillNeeded;
                    }
                }

                NeededItemIds = neededBuilder.Keys.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
                NeededItemCounts = neededBuilder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

                int upgradeable = 0, maxed = 0;
                for (int a = 0; a < areas.Count; a++)
                {
                    if (areas[a].IsMaxLevel) maxed++;
                    else upgradeable++;
                }

                Log.WriteLine(
                    $"[HideoutManager] ReadAreas: {areas.Count} area(s), " +
                    $"{upgradeable} upgradeable, {maxed} max level, " +
                    $"{NeededItemIds.Count} unique items needed.");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[HideoutManager] ReadAreas error: {ex.Message}");
            }
        }

        /// <summary>
        /// Re-validates pointer chain (if needed), then re-reads stash items and area levels.
        /// Returns a short status message for the UI.
        /// </summary>
        public string RefreshAll()
        {
            try
            {
                if (Memory.InRaid)
                {
                    Log.WriteLine("[HideoutManager] RefreshAll skipped — player is in raid.");
                    return "Not available in raid — return to your hideout.";
                }

                if ((!IsValid || !IsAreasValid) && !TryFind())
                    return "Stash not found — are you in the hideout?";

                Refresh();
                ReadAreas();

                return $"{Items.Count} items  ·  ₽{TotalBestValue:N0} total";
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[HideoutManager] RefreshAll error: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Constructs an Item or Tool requirement from pre-read scattered field values.
        /// </summary>
        private static HideoutRequirement BuildItemOrToolReq(
            ERequirementType type, bool fulfilled, string? tplId, int required, int current)
        {
            string? itemName = null;
            if (tplId is not null && EftDataManager.AllItems.TryGetValue(tplId, out var entry))
                itemName = entry.ShortName;
            return new HideoutRequirement(type, fulfilled,
                ItemTemplateId: tplId,
                ItemName: itemName,
                RequiredCount: required,
                CurrentCount: current);
        }

        /// <summary>
        /// Reads all stash items using a breadth-first approach. Each "level" collects all
        /// items from all grids at that depth, resolves them in batched scatter reads, then
        /// queues any child grid pointers for the next level. This minimises DMA round-trips
        /// compared to the previous recursive per-grid approach.
        /// </summary>
        private static void GetAllStashItems(ulong gridsArrayPtr, List<StashItem> results)
        {
            // Pending grids to process (breadth-first queue, bounded by depth)
            var pendingGrids = new List<ulong>(16);
            var nextGrids = new List<ulong>(16);

            // Seed with top-level grids array
            CollectGridPtrs(gridsArrayPtr, pendingGrids);

            const int maxDepth = 4;
            for (int depth = 0; depth < maxDepth && pendingGrids.Count > 0; depth++)
            {
                nextGrids.Clear();

                // ── Phase 1: Collect all item pointers from all grids at this depth ──
                var allItems = new List<ulong>(depth == 0 ? 512 : 64);

                // Read item collections from each grid (sequential ptr chains, but batched where possible)
                var collectionPtrs = new ulong[pendingGrids.Count];
                using (var rCol = ScatterReadRound.Get(false))
                {
                    for (int g = 0; g < pendingGrids.Count; g++)
                        rCol[0].AddEntry<ulong>(g, pendingGrids[g] + Offsets.Grid.ItemCollection);
                    rCol.Run();
                    for (int g = 0; g < pendingGrids.Count; g++)
                        rCol[0].TryGetResult(g, out collectionPtrs[g]);
                }

                var itemListPtrs = new ulong[pendingGrids.Count];
                using (var rList = ScatterReadRound.Get(false))
                {
                    for (int g = 0; g < pendingGrids.Count; g++)
                        if (collectionPtrs[g].IsValidVirtualAddress())
                            rList[0].AddEntry<ulong>(g, collectionPtrs[g] + Offsets.GridItemCollection.ItemsList);
                    rList.Run();
                    for (int g = 0; g < pendingGrids.Count; g++)
                        rList[0].TryGetResult(g, out itemListPtrs[g]);
                }

                // Read each MemList to get item pointers
                for (int g = 0; g < pendingGrids.Count; g++)
                {
                    if (!itemListPtrs[g].IsValidVirtualAddress()) continue;
                    try
                    {
                        using var itemList = MemList<ulong>.Get(itemListPtrs[g]);
                        for (int j = 0; j < itemList.Count; j++)
                            allItems.Add(itemList[j]);
                    }
                    catch { }
                }

                int n = allItems.Count;
                if (n == 0) break;

                // ── Phase 2: Scatter A — templatePtr per item ────────────────────
                var templatePtrs = new ulong[n];
                using (var rA = ScatterReadRound.Get(false))
                {
                    for (int k = 0; k < n; k++)
                        rA[0].AddEntry<ulong>(k, allItems[k] + Offsets.LootItem.Template);
                    rA.Run();
                    for (int k = 0; k < n; k++)
                        rA[0].TryGetResult(k, out templatePtrs[k]);
                }

                // ── Phase 3: Scatter B — MongoID + stackCount ────────────────────
                // Note: we do NOT read LootItemMod.Grids here. Most items are not
                // containers, so reading that offset produces garbage and causes
                // hundreds of exceptions when recursing. Container grids are read
                // separately below, only for items that resolve to a container type.
                var mongoIds = new Types.MongoID[n];
                var stackCounts = new int[n];
                using (var rB = ScatterReadRound.Get(false))
                {
                    for (int k = 0; k < n; k++)
                    {
                        if (!templatePtrs[k].IsValidVirtualAddress()) continue;
                        rB[0].AddEntry<Types.MongoID>(k, templatePtrs[k] + Offsets.ItemTemplate._id);
                        rB[1].AddEntry<int>(k, allItems[k] + Offsets.LootItem.StackObjectsCount);
                    }
                    rB.Run();
                    for (int k = 0; k < n; k++)
                    {
                        rB[0].TryGetResult(k, out mongoIds[k]);
                        rB[1].TryGetResult(k, out stackCounts[k]);
                    }
                }

                // ── Phase 4: Scatter C — UnicodeString for MongoID.StringID ──────
                var ids = new string?[n];
                using (var rC = ScatterReadRound.Get(false))
                {
                    for (int k = 0; k < n; k++)
                    {
                        var sid = mongoIds[k].StringID;
                        if (sid.IsValidVirtualAddress())
                            rC[0].AddEntry<UnicodeString>(k, sid + 0x14, 48);
                    }
                    rC.Run();
                    for (int k = 0; k < n; k++)
                        if (rC[0].TryGetResult<UnicodeString>(k, out var s)) ids[k] = s;
                }

                // ── Phase 5: Resolve items and identify containers ───────────────
                var containerIndices = new List<int>();
                for (int k = 0; k < n; k++)
                {
                    var id = ids[k];
                    if (id is null) continue;

                    if (EftDataManager.AllItems.TryGetValue(id, out var entry))
                    {
                        results.Add(new StashItem(
                            Id: entry.BsgId,
                            Name: entry.Name,
                            TraderPrice: entry.TraderPrice,
                            FleaPrice: entry.FleaPrice,
                            StackCount: Math.Max(1, stackCounts[k])));

                        // Only attempt grid recursion for items with >1 grid slot
                        // (backpacks, rigs, cases, etc.). Single-slot items (ammo,
                        // meds, keys, barter, money) never have child grids.
                        if (entry.Slots > 1)
                            containerIndices.Add(k);
                    }
                }

                // ── Phase 6: Scatter D — read child Grid[] ptrs for containers ───
                if (containerIndices.Count > 0 && depth + 1 < maxDepth)
                {
                    var childGridsPtrs = new ulong[containerIndices.Count];
                    using (var rD = ScatterReadRound.Get(false))
                    {
                        for (int c = 0; c < containerIndices.Count; c++)
                            rD[0].AddEntry<ulong>(c, allItems[containerIndices[c]] + Offsets.LootItemMod.Grids);
                        rD.Run();
                        for (int c = 0; c < containerIndices.Count; c++)
                            rD[0].TryGetResult(c, out childGridsPtrs[c]);
                    }

                    for (int c = 0; c < containerIndices.Count; c++)
                        CollectGridPtrs(childGridsPtrs[c], nextGrids);
                }

                // Swap for next depth level
                (pendingGrids, nextGrids) = (nextGrids, pendingGrids);
            }
        }

        /// <summary>
        /// Reads a Grid[] managed array and appends valid grid pointers to <paramref name="output"/>.
        /// Silently ignores invalid or empty arrays.
        /// </summary>
        private static void CollectGridPtrs(ulong gridsArrayPtr, List<ulong> output)
        {
            if (!gridsArrayPtr.IsValidVirtualAddress()) return;
            try
            {
                // Pre-read count to validate before doing a full array read
                var count = Memory.ReadValue<int>(gridsArrayPtr + MemArray<ulong>.CountOffset, false);
                if (count <= 0 || count > 64) return;

                using var arr = MemArray<ulong>.Get(gridsArrayPtr, useCache: false);
                for (int i = 0; i < arr.Count; i++)
                {
                    var ptr = arr[i];
                    if (ptr.IsValidVirtualAddress())
                        output.Add(ptr);
                }
            }
            catch { }
        }

        /// <summary>
        /// Clears all cached pointers and items, forcing full re-discovery on the next call.
        /// </summary>
        public void Reset()
        {
            Base = 0;
            StashGridPtr = 0;
            AreasControllerBase = 0;
            Items = [];
            Areas = [];
            TotalBestValue = 0;
            TotalTraderValue = 0;
            TotalFleaValue = 0;
            NeededItemIds = FrozenSet<string>.Empty;
            NeededItemCounts = FrozenDictionary<string, int>.Empty;
        }

        // ── Display Helpers (used by HideoutPanel) ───────────────────────────

        /// <summary>Formats a price value as a human-readable string.</summary>
        internal static string FormatPrice(long price) => price switch
        {
            >= 1_000_000 => $"{price / 1_000_000.0:0.##}M ₽",
            >= 1_000 => $"{price / 1_000.0:0}K ₽",
            _ => $"{price} ₽",
        };

        /// <summary>Inserts spaces before capital letters in PascalCase names.</summary>
        internal static string FormatAreaName(string raw)
        {
            if (raw.Length <= 1) return raw;
            var sb = new System.Text.StringBuilder(raw.Length + 4);
            sb.Append(raw[0]);
            for (int i = 1; i < raw.Length; i++)
            {
                if (char.IsUpper(raw[i]) && char.IsLower(raw[i - 1]))
                    sb.Append(' ');
                sb.Append(raw[i]);
            }
            return sb.ToString();
        }

        /// <summary>Gets a readable label for an area status.</summary>
        internal static string FormatStatus(EAreaStatus s) => s switch
        {
            EAreaStatus.ReadyToConstruct => "BUILD",
            EAreaStatus.ReadyToUpgrade => "UPGRADE",
            EAreaStatus.ReadyToInstallConstruct or
            EAreaStatus.ReadyToInstallUpgrade => "INSTALL",
            EAreaStatus.Constructing => "Building…",
            EAreaStatus.Upgrading => "Upgrading…",
            EAreaStatus.AutoUpgrading => "Auto",
            EAreaStatus.LockedToConstruct or EAreaStatus.LockedToUpgrade => "Locked",
            EAreaStatus.NoFutureUpgrades => "Max",
            _ => s.ToString()
        };

        /// <summary>Returns a sort priority for area status (lower = more actionable).</summary>
        internal static int GetStatusPriority(EAreaStatus s) => s switch
        {
            EAreaStatus.ReadyToUpgrade or EAreaStatus.ReadyToConstruct => 0,
            EAreaStatus.ReadyToInstallUpgrade or EAreaStatus.ReadyToInstallConstruct => 1,
            EAreaStatus.Upgrading or EAreaStatus.Constructing => 2,
            EAreaStatus.AutoUpgrading => 3,
            EAreaStatus.LockedToUpgrade or EAreaStatus.LockedToConstruct => 4,
            _ => 5
        };
    }
}
