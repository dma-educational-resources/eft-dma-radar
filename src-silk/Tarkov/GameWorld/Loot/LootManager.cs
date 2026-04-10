using System.Collections.Frozen;
using eft_dma_radar.Silk.DMA.ScatterAPI;
using eft_dma_radar.Silk.Misc.Data;
using eft_dma_radar.Silk.Tarkov.Unity;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Loot
{
    /// <summary>
    /// Reads loose loot from the GameWorld LootList.
    /// Also scans corpses for dogtag identity data and equipment.
    /// </summary>
    internal sealed class LootManager
    {
        private readonly ulong _lgw;
        private volatile IReadOnlyList<LootItem> _loot = [];
        private volatile IReadOnlyList<LootCorpse> _corpses = [];
        private DateTime _lastRefresh;
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

        // Track corpses we've already read dogtags from (by interactiveClass address)
        private readonly HashSet<ulong> _processedCorpses = [];

        // Track corpses we've already read equipment from (by interactiveClass address)
        private readonly HashSet<ulong> _processedCorpseGear = [];

        // interactiveClass → dogtag nickname (populated by ReadCorpseDogtags)
        private readonly ConcurrentDictionary<ulong, string> _corpseNicknames = new();

        // Slot names to skip when reading corpse equipment
        private static readonly FrozenSet<string> _skipSlots =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Compass", "ArmBand", "SecuredContainer" }
                .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        /// <summary>Current loot snapshot (thread-safe read).</summary>
        public IReadOnlyList<LootItem> Loot => _loot;

        /// <summary>Current corpse snapshot (thread-safe read).</summary>
        public IReadOnlyList<LootCorpse> Corpses => _corpses;

        public LootManager(ulong localGameWorld)
        {
            _lgw = localGameWorld;
        }

        /// <summary>
        /// Refreshes loot from memory. Rate-limited to once per <see cref="RefreshInterval"/>.
        /// Call from the registration worker thread.
        /// </summary>
        public void Refresh()
        {
            var now = DateTime.UtcNow;
            if (now - _lastRefresh < RefreshInterval)
                return;
            _lastRefresh = now;

            // Read the LootList pointer array once — shared by all phases
            if (!TryReadLootListPtrs(out var ptrs))
            {
                _loot = [];
                _corpses = [];
                return;
            }

            try
            {
                _loot = ReadLoot(ptrs);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[LootManager] Refresh failed: {ex.Message}");
            }

            try
            {
                var newCorpses = ReadCorpsePositions(ptrs);

                // Carry over previously-read gear/name data to new corpse objects
                // (ReadCorpsePositions recreates the list each cycle)
                var oldCorpses = _corpses;
                if (oldCorpses.Count > 0 && newCorpses.Count > 0)
                {
                    foreach (var nc in newCorpses)
                    {
                        for (int i = 0; i < oldCorpses.Count; i++)
                        {
                            var oc = oldCorpses[i];
                            if (oc.InteractiveClass == nc.InteractiveClass)
                            {
                                nc.Name = oc.Name;
                                nc.GearReady = oc.GearReady;
                                nc.Equipment = oc.Equipment;
                                nc.TotalValue = oc.TotalValue;
                                break;
                            }
                        }
                    }
                }

                _corpses = newCorpses;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[LootManager] Corpse position read failed: {ex.Message}");
            }

            try
            {
                ReadCorpseDogtags(ptrs);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[LootManager] Corpse dogtag scan failed: {ex.Message}");
            }

            // Resolve corpse display names from dogtag nicknames + read equipment
            foreach (var corpse in _corpses)
            {
                if (_corpseNicknames.TryGetValue(corpse.InteractiveClass, out var nickname))
                    corpse.Name = nickname;

                if (!corpse.GearReady)
                    ReadCorpseEquipment(corpse);
            }
        }

        #region LootList Helper

        /// <summary>
        /// Reads the LootList pointer array once, shared by loot, corpse, and dogtag phases.
        /// </summary>
        private bool TryReadLootListPtrs(out ulong[] ptrs)
        {
            ptrs = [];

            if (!Memory.TryReadPtr(_lgw + Offsets.ClientLocalGameWorld.LootList, out var lootListAddr))
                return false;

            if (!Memory.TryReadPtr(lootListAddr + UnityOffsets.List.ArrOffset, out var arrBase))
                return false;

            var count = Memory.ReadValue<int>(lootListAddr + 0x18); // List._size
            if (count <= 0 || count > 4096)
                return false;

            ptrs = Memory.ReadArray<ulong>(arrBase + UnityOffsets.List.ArrStartOffset, count);
            return ptrs.Length > 0;
        }

        #endregion

        private List<LootItem> ReadLoot(ulong[] ptrs)
        {
            // 6-round scatter chain
            using var map = ScatterReadMap.Get();
            var round1 = map.AddRound();
            var round2 = map.AddRound();
            var round3 = map.AddRound();
            var round4 = map.AddRound();
            var round5 = map.AddRound();
            var round6 = map.AddRound();

            var result = new List<LootItem>(ptrs.Length);

            for (int ix = 0; ix < ptrs.Length; ix++)
            {
                var i = ix;
                var lootBase = ptrs[i];
                if (!Utils.IsValidVirtualAddress(lootBase))
                    continue;

                // ROUND 1: MonoBehaviour (LootItemPositionClass + 0x10) + class name chain start
                round1[i].AddEntry<MemPointer>(0, lootBase + 0x10);
                round1[i].AddEntry<MemPointer>(1, lootBase); // First ptr for class name chain [0x0]

                round1[i].Callbacks += x1 =>
                {
                    if (!x1.TryGetResult<MemPointer>(0, out var monoBehaviour) ||
                        !x1.TryGetResult<MemPointer>(1, out var c1))
                        return;

                    // ROUND 2: InteractiveClass, GameObject, class name ptr
                    round2[i].AddEntry<MemPointer>(2, monoBehaviour + UnityOffsets.Comp_ObjectClass);
                    round2[i].AddEntry<MemPointer>(3, monoBehaviour + UnityOffsets.Comp_GameObject);
                    round2[i].AddEntry<MemPointer>(4, c1 + 0x10); // [0x10] for class name

                    round2[i].Callbacks += x2 =>
                    {
                        if (!x2.TryGetResult<MemPointer>(2, out var interactiveClass) ||
                            !x2.TryGetResult<MemPointer>(3, out var gameObject) ||
                            !x2.TryGetResult<MemPointer>(4, out var classNamePtr))
                            return;

                        // ROUND 3: Components array, class name string
                        round3[i].AddEntry<MemPointer>(5, gameObject + UnityOffsets.GO_Components);
                        round3[i].AddEntry<UTF8String>(6, classNamePtr, 64);

                        round3[i].Callbacks += x3 =>
                        {
                            if (!x3.TryGetResult<MemPointer>(5, out var components) ||
                                !x3.TryGetResult<UTF8String>(6, out var classNameRaw))
                                return;

                            string className = classNameRaw;
                            if (string.IsNullOrEmpty(className))
                                return;

                            // Phase 1: only process ObservedLootItem (loose loot)
                            if (!className.Equals("ObservedLootItem", StringComparison.OrdinalIgnoreCase))
                                return;

                            // ROUND 4: First transform component (ComponentArray + 0x08) + item ID chain
                            round4[i].AddEntry<MemPointer>(7, components + 0x08);
                            round4[i].AddEntry<MemPointer>(8, interactiveClass + Offsets.InteractiveLootItem.Item);

                            round4[i].Callbacks += x4 =>
                            {
                                if (!x4.TryGetResult<MemPointer>(7, out var t1) ||
                                    !x4.TryGetResult<MemPointer>(8, out var item))
                                    return;

                                // ROUND 5: Transform chain + item template
                                round5[i].AddEntry<MemPointer>(9, t1 + UnityOffsets.Comp_ObjectClass);
                                round5[i].AddEntry<MemPointer>(10, item + Offsets.LootItem.Template);

                                round5[i].Callbacks += x5 =>
                                {
                                    if (!x5.TryGetResult<MemPointer>(9, out var t2) ||
                                        !x5.TryGetResult<MemPointer>(10, out var template))
                                        return;

                                    // ROUND 6: TransformInternal (ObjectClass + 0x10) + BSG ID
                                    round6[i].AddEntry<MemPointer>(11, t2 + 0x10);
                                    round6[i].AddEntry<Types.MongoID>(12, template + Offsets.ItemTemplate._id);

                                    round6[i].Callbacks += x6 =>
                                    {
                                        if (!x6.TryGetResult<MemPointer>(11, out var transformInternal) ||
                                            !x6.TryGetResult<Types.MongoID>(12, out var mongoId))
                                            return;

                                        try
                                        {
                                            var bsgId = Memory.ReadUnityString(mongoId.StringID);
                                            if (string.IsNullOrEmpty(bsgId))
                                                return;

                                            if (!EftDataManager.AllItems.TryGetValue(bsgId, out var marketItem))
                                                return;

                                            var pos = ReadTransformPosition(transformInternal);
                                            if (pos == Vector3.Zero)
                                                return;

                                            lock (result)
                                                result.Add(new LootItem(marketItem, pos));
                                        }
                                        catch { }
                                    };
                                };
                            };
                        };
                    };
                };
            }

            map.Execute();

            return result;
        }

        #region Corpse Position Reading

        /// <summary>
        /// Reads corpse positions from the LootList using linear memory reads.
        /// Returns a list of <see cref="LootCorpse"/> with their world positions.
        /// Names and equipment are resolved in later phases.
        /// </summary>
        private List<LootCorpse> ReadCorpsePositions(ulong[] ptrs)
        {
            var corpses = new List<LootCorpse>();

            foreach (var lootBase in ptrs)
            {
                if (!lootBase.IsValidVirtualAddress())
                    continue;

                try
                {
                    var className = Il2CppClass.ReadName(lootBase);
                    if (className is null || !className.Contains("Corpse", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // MonoBehaviour → InteractiveClass
                    if (!Memory.TryReadPtr(lootBase + 0x10, out var monoBehaviour))
                        continue;
                    if (!Memory.TryReadPtr(monoBehaviour + UnityOffsets.Comp_ObjectClass, out var interactiveClass))
                        continue;

                    // GameObject → Components → Transform chain
                    if (!Memory.TryReadPtr(monoBehaviour + UnityOffsets.Comp_GameObject, out var gameObject))
                        continue;
                    if (!Memory.TryReadPtr(gameObject + UnityOffsets.GO_Components, out var components))
                        continue;
                    if (!Memory.TryReadPtr(components + 0x08, out var t1))
                        continue;
                    if (!Memory.TryReadPtr(t1 + UnityOffsets.Comp_ObjectClass, out var t2))
                        continue;
                    if (!Memory.TryReadPtr(t2 + 0x10, out var transformInternal))
                        continue;

                    var pos = ReadTransformPosition(transformInternal);
                    if (pos == Vector3.Zero)
                        continue;

                    corpses.Add(new LootCorpse(interactiveClass, pos));
                }
                catch { }
            }

            return corpses;
        }

        #endregion

        #region Corpse Dogtag Reading

        /// <summary>
        /// Iterates the LootList for corpse items, walks their equipment slots,
        /// finds BarterOther (dogtag) items, reads DogtagComponent fields, and seeds
        /// <see cref="DogtagCache"/> with victim identity data.
        /// </summary>
        private void ReadCorpseDogtags(ulong[] ptrs)
        {
            foreach (var lootBase in ptrs)
            {
                if (!lootBase.IsValidVirtualAddress())
                    continue;

                try
                {
                    // Read MonoBehaviour (LootItemPositionClass + 0x10)
                    if (!Memory.TryReadPtr(lootBase + 0x10, out var monoBehaviour))
                        continue;

                    // Read InteractiveClass
                    if (!Memory.TryReadPtr(monoBehaviour + UnityOffsets.Comp_ObjectClass, out var interactiveClass))
                        continue;

                    if (!interactiveClass.IsValidVirtualAddress())
                        continue;

                    // Already processed this corpse?
                    if (_processedCorpses.Contains(interactiveClass))
                        continue;

                    // Read class name to identify corpses
                    var className = Il2CppClass.ReadName(lootBase);
                    if (className is null || !className.Contains("Corpse", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Mark as processed
                    _processedCorpses.Add(interactiveClass);

                    // Corpse: InteractiveLootItem.Item → base item → LootItemMod.Slots → array of slots
                    if (!Memory.TryReadPtr(interactiveClass + Offsets.InteractiveLootItem.Item, out var itemBase)
                        || !itemBase.IsValidVirtualAddress())
                        continue;

                    if (!Memory.TryReadPtr(itemBase + Offsets.LootItemMod.Slots, out var slotsArr)
                        || !slotsArr.IsValidVirtualAddress())
                        continue;

                    if (!Memory.TryReadValue<int>(slotsArr + 0x18, out var slotCount) || slotCount < 1 || slotCount > 64)
                        continue;

                    var slotPtrs = Memory.ReadArray<ulong>(slotsArr + 0x20, slotCount);

                    foreach (var slotPtr in slotPtrs)
                    {
                        if (!slotPtr.IsValidVirtualAddress())
                            continue;

                        if (!Memory.TryReadPtr(slotPtr + Offsets.Slot.ContainedItem, out var slotItem)
                            || !slotItem.IsValidVirtualAddress())
                            continue;

                        // Check if this is a BarterOther (dogtag)
                        var slotClassName = Il2CppClass.ReadName(slotItem);
                        if (slotClassName is null || !slotClassName.Equals("BarterOther", StringComparison.Ordinal))
                            continue;

                        // Read DogtagComponent
                        if (!Memory.TryReadPtr(slotItem + Offsets.BarterOtherOffsets.Dogtag, out var dogtag)
                            || !dogtag.IsValidVirtualAddress())
                            continue;

                        // Read victim identity fields
                        var profileId = ReadDogtagString(dogtag + Offsets.DogtagComponent.ProfileId);
                        if (string.IsNullOrWhiteSpace(profileId))
                            continue;

                        var nickname = ReadDogtagString(dogtag + Offsets.DogtagComponent.Nickname);
                        var accountId = ReadDogtagString(dogtag + Offsets.DogtagComponent.AccountId);
                        Memory.TryReadValue<int>(dogtag + Offsets.DogtagComponent.Level, out var level);

                        DogtagCache.Seed(profileId, nickname, accountId, level);

                        // Store nickname for corpse name resolution
                        if (!string.IsNullOrWhiteSpace(nickname))
                            _corpseNicknames[interactiveClass] = nickname;

                        // Also seed killer identity if available
                        var killerProfileId = ReadDogtagString(dogtag + Offsets.DogtagComponent.KillerProfileId);
                        var killerAccountId = ReadDogtagString(dogtag + Offsets.DogtagComponent.KillerAccountId);
                        var killerName = ReadDogtagString(dogtag + Offsets.DogtagComponent.KillerName);

                        if (!string.IsNullOrWhiteSpace(killerProfileId))
                            DogtagCache.Seed(killerProfileId, killerName, killerAccountId, 0);

                        break; // Only one dogtag per corpse
                    }
                }
                catch
                {
                    // Non-fatal — skip this corpse
                }
            }
        }

        /// <summary>
        /// Reads a Unity string from a dogtag component field (ptr → Unity string).
        /// </summary>
        private static string? ReadDogtagString(ulong fieldAddr)
        {
            if (!Memory.TryReadPtr(fieldAddr, out var strPtr) || !strPtr.IsValidVirtualAddress())
                return null;
            return Memory.TryReadUnityString(strPtr, out var result) ? result : null;
        }

        #endregion

        #region Corpse Equipment Reading

        /// <summary>
        /// Reads the equipment slots of a corpse and populates its <see cref="LootCorpse.Equipment"/>
        /// and <see cref="LootCorpse.TotalValue"/>. Only runs once per corpse (tracked by
        /// <see cref="_processedCorpseGear"/>).
        /// </summary>
        private void ReadCorpseEquipment(LootCorpse corpse)
        {
            if (_processedCorpseGear.Contains(corpse.InteractiveClass))
                return;

            _processedCorpseGear.Add(corpse.InteractiveClass);

            try
            {
                // InteractiveClass → Item → Slots array
                if (!Memory.TryReadPtr(corpse.InteractiveClass + Offsets.InteractiveLootItem.Item, out var itemBase)
                    || !itemBase.IsValidVirtualAddress())
                    return;

                if (!Memory.TryReadPtr(itemBase + Offsets.LootItemMod.Slots, out var slotsArr)
                    || !slotsArr.IsValidVirtualAddress())
                    return;

                if (!Memory.TryReadValue<int>(slotsArr + 0x18, out var slotCount) || slotCount < 1 || slotCount > 64)
                    return;

                var slotPtrs = Memory.ReadArray<ulong>(slotsArr + 0x20, slotCount);
                var gear = new Dictionary<string, CorpseGearItem>(slotPtrs.Length, StringComparer.OrdinalIgnoreCase);
                int totalValue = 0;

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

                        if (_skipSlots.Contains(slotName))
                            continue;

                        // Read contained item
                        if (!Memory.TryReadPtr(slotPtr + Offsets.Slot.ContainedItem, out var slotItem)
                            || !slotItem.IsValidVirtualAddress())
                            continue;

                        // Resolve BSG ID via template → MongoID
                        if (!TryReadBsgId(slotItem, out var bsgId))
                            continue;

                        if (!EftDataManager.AllItems.TryGetValue(bsgId, out var marketItem))
                            continue;

                        gear[slotName] = new CorpseGearItem
                        {
                            ShortName = marketItem.ShortName,
                            Name = marketItem.Name,
                            Price = marketItem.BestPrice
                        };
                        totalValue += marketItem.BestPrice;
                    }
                    catch
                    {
                        // Skip individual slot failures
                    }
                }

                corpse.Equipment = gear.Count > 0
                    ? gear.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase)
                    : FrozenDictionary<string, CorpseGearItem>.Empty;
                corpse.TotalValue = totalValue;
                corpse.GearReady = true;
            }
            catch
            {
                // Non-fatal — will retry next refresh
            }
        }

        /// <summary>
        /// Reads a BSG ID string from an item via its template → MongoID chain.
        /// </summary>
        private static bool TryReadBsgId(ulong itemAddr, out string bsgId)
        {
            bsgId = string.Empty;

            if (!Memory.TryReadPtr(itemAddr + Offsets.LootItem.Template, out var template)
                || !template.IsValidVirtualAddress())
                return false;

            if (!Memory.TryReadValue<Types.MongoID>(template + Offsets.ItemTemplate._id, out var mongoId))
                return false;

            if (!mongoId.StringID.IsValidVirtualAddress())
                return false;

            if (!Memory.TryReadUnityString(mongoId.StringID, out var id) || string.IsNullOrEmpty(id))
                return false;

            bsgId = id;
            return true;
        }

        #endregion

        /// <summary>
        /// Reads world position from a TransformInternal pointer using the hierarchy walk.
        /// Simplified single-read version (non-scatter) — acceptable for loot which is static.
        /// </summary>
        private static Vector3 ReadTransformPosition(ulong transformInternal)
        {
            try
            {
                var hierarchy = Memory.ReadValue<ulong>(transformInternal + UnityOffsets.TransformAccess.HierarchyOffset);
                if (!Utils.IsValidVirtualAddress(hierarchy))
                    return Vector3.Zero;

                var index = Memory.ReadValue<int>(transformInternal + UnityOffsets.TransformAccess.IndexOffset);
                if (index < 0 || index > 150_000)
                    return Vector3.Zero;

                var verticesPtr = Memory.ReadValue<ulong>(hierarchy + UnityOffsets.TransformHierarchy.VerticesOffset);
                var indicesPtr = Memory.ReadValue<ulong>(hierarchy + UnityOffsets.TransformHierarchy.IndicesOffset);
                if (!Utils.IsValidVirtualAddress(verticesPtr) || !Utils.IsValidVirtualAddress(indicesPtr))
                    return Vector3.Zero;

                int vertCount = index + 1;

                // Read vertices and indices
                var vertices = Memory.ReadArray<TrsX>(verticesPtr, vertCount);
                var indices = Memory.ReadArray<int>(indicesPtr, vertCount);

                if (vertices.Length < vertCount || indices.Length < vertCount)
                    return Vector3.Zero;

                // Walk the transform hierarchy
                var pos = vertices[index].T;
                int parent = indices[index];
                int iter = 0;

                while (parent >= 0 && parent < vertCount && iter++ < 4096)
                {
                    ref readonly var p = ref vertices[parent];
                    pos = Vector3.Transform(pos, p.Q);
                    pos *= p.S;
                    pos += p.T;
                    parent = indices[parent];
                }

                if (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y) || !float.IsFinite(pos.Z))
                    return Vector3.Zero;

                if (pos.LengthSquared() < 16f) // Skip origin-ish positions
                    return Vector3.Zero;

                return pos;
            }
            catch
            {
                return Vector3.Zero;
            }
        }
    }
}
