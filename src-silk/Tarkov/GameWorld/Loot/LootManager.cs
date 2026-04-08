using eft_dma_radar.Silk.DMA.ScatterAPI;
using eft_dma_radar.Silk.Misc.Data;
using eft_dma_radar.Silk.Tarkov.Unity;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Loot
{
    /// <summary>
    /// Reads loose loot from the GameWorld LootList.
    /// Also scans corpses for dogtag identity data (nickname, accountId, profileId).
    /// </summary>
    internal sealed class LootManager
    {

        private readonly ulong _lgw;
        private volatile IReadOnlyList<LootItem> _loot = [];
        private DateTime _lastRefresh;
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

        // Track corpses we've already read dogtags from (by interactiveClass address)
        private readonly HashSet<ulong> _processedCorpses = [];

        /// <summary>Current loot snapshot (thread-safe read).</summary>
        public IReadOnlyList<LootItem> Loot => _loot;

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

            try
            {
                var items = ReadLoot();
                _loot = items;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[LootManager] Refresh failed: {ex.Message}");
            }

            try
            {
                ReadCorpseDogtags();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[LootManager] Corpse dogtag scan failed: {ex.Message}");
            }
        }

        private List<LootItem> ReadLoot()
        {
            // Read LootList (Unity List<LootItemPositionClass>)
            if (!Memory.TryReadPtr(_lgw + Offsets.ClientLocalGameWorld.LootList, out var lootListAddr))
                return [];

            // Read list backing array + count
            if (!Memory.TryReadPtr(lootListAddr + UnityOffsets.List.ArrOffset, out var arrBase))
                return [];

            var count = Memory.ReadValue<int>(lootListAddr + 0x18); // List._size
            if (count <= 0 || count > 4096)
                return [];

            // Read all item pointers
            var ptrs = Memory.ReadArray<ulong>(arrBase + UnityOffsets.List.ArrStartOffset, count);
            if (ptrs.Length == 0)
                return [];

            // 6-round scatter chain (same structure as WPF LootManager)
            using var map = ScatterReadMap.Get();
            var round1 = map.AddRound();
            var round2 = map.AddRound();
            var round3 = map.AddRound();
            var round4 = map.AddRound();
            var round5 = map.AddRound();
            var round6 = map.AddRound();

            var result = new List<LootItem>(count);

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

        #region Corpse Dogtag Reading

        /// <summary>
        /// Iterates the LootList for corpse items, walks their equipment slots,
        /// finds BarterOther (dogtag) items, reads DogtagComponent fields, and seeds
        /// <see cref="DogtagCache"/> with victim identity data.
        /// </summary>
        private void ReadCorpseDogtags()
        {
            if (!Memory.TryReadPtr(_lgw + Offsets.ClientLocalGameWorld.LootList, out var lootListAddr))
                return;

            if (!Memory.TryReadPtr(lootListAddr + UnityOffsets.List.ArrOffset, out var arrBase))
                return;

            var count = Memory.ReadValue<int>(lootListAddr + 0x18);
            if (count <= 0 || count > 4096)
                return;

            var ptrs = Memory.ReadArray<ulong>(arrBase + UnityOffsets.List.ArrStartOffset, count);

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
