using System.Collections.Frozen;
using eft_dma_radar.Silk.DMA.ScatterAPI;
using eft_dma_radar.Silk.Misc.Data;
using eft_dma_radar.Silk.Tarkov.Unity;
using VmmSharpEx;
using VmmSharpEx.Options;

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

            // ── Unified scatter pass: identifies loot + corpses in one batched read ──
            // This eliminates hundreds of serial Il2CppClass.ReadName() calls that were
            // previously needed to filter corpses from the LootList.
            List<LootItem> lootResult = [];
            List<LootCorpse> corpseResult = [];

            try
            {
                ReadLootAndCorpses(ptrs, out lootResult, out corpseResult);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[LootManager] Unified loot/corpse scatter failed: {ex.Message}");
            }
            finally
            {
                ptrs.Dispose();
            }

            _loot = lootResult;

            // Carry over previously-read gear/name data to new corpse objects
            var oldCorpses = _corpses;
            if (oldCorpses.Count > 0 && corpseResult.Count > 0)
            {
                // Build lookup by address for O(1) matching instead of O(n*m) nested loop
                Dictionary<ulong, LootCorpse>? oldByAddr = null;
                foreach (var oc in oldCorpses)
                {
                    if (oc.Name != "Corpse" || oc.GearReady)
                    {
                        (oldByAddr ??= new(oldCorpses.Count))[oc.InteractiveClass] = oc;
                    }
                }

                if (oldByAddr is not null)
                {
                    foreach (var nc in corpseResult)
                    {
                        if (oldByAddr.TryGetValue(nc.InteractiveClass, out var oc))
                        {
                            nc.Name = oc.Name;
                            nc.GearReady = oc.GearReady;
                            nc.Equipment = oc.Equipment;
                            nc.TotalValue = oc.TotalValue;
                        }
                    }
                }
            }

            _corpses = corpseResult;

            // Dogtag + equipment reads — now iterate only known corpses (typically 0-5),
            // not the entire LootList (hundreds of items).
            try
            {
                ReadCorpseDogtags();
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
        private bool TryReadLootListPtrs(out MemList<ulong> list)
        {
            list = null!;

            if (!Memory.TryReadPtr(_lgw + Offsets.ClientLocalGameWorld.LootList, out var lootListAddr))
                return false;

            try
            {
                list = MemList<ulong>.Get(lootListAddr, false);
            }
            catch
            {
                return false;
            }

            return list.Count > 0;
        }

        #endregion

        /// <summary>
        /// Unified scatter pass — reads ALL LootList entries in one batched operation,
        /// routing items to loot or corpse lists based on class name (resolved in round 3).
        /// <para>
        /// <b>Two-phase design:</b>
        /// <list type="number">
        ///   <item><b>Phase 1</b> — 6-round ScatterReadMap resolves pointer chains and collects
        ///     <c>transformInternal</c> + <c>mongoId</c> for each item into pending lists.</item>
        ///   <item><b>Phase 2</b> — batched VmmScatter reads resolve all transform positions
        ///     and BSG ID strings in ~3 DMA round-trips instead of serial reads per item.</item>
        /// </list>
        /// </para>
        /// </summary>
        private void ReadLootAndCorpses(MemList<ulong> ptrs, out List<LootItem> lootResult, out List<LootCorpse> corpseResult)
        {
            // Pending items collected during Phase 1 scatter callbacks
            var pendingLoot = new List<PendingLoot>(ptrs.Count);
            var pendingCorpses = new List<PendingCorpse>();

            // ── Phase 1: 6-round scatter to resolve pointer chains ──────────────
            using (var map = ScatterReadMap.Get())
            {
                var round1 = map.AddRound();
                var round2 = map.AddRound();
                var round3 = map.AddRound();
                var round4 = map.AddRound();
                var round5 = map.AddRound();
                var round6 = map.AddRound();

                for (int ix = 0; ix < ptrs.Count; ix++)
                {
                    var i = ix;
                    var lootBase = ptrs[i];
                    if (!Utils.IsValidVirtualAddress(lootBase))
                        continue;

                    // ROUND 1: MonoBehaviour (LootItemPositionClass + 0x10) + class name chain start
                    round1[i].AddEntry<MemPointer>(0, lootBase + 0x10);
                    round1[i].AddEntry<MemPointer>(1, lootBase);

                    round1[i].Callbacks += x1 =>
                    {
                        if (!x1.TryGetResult<MemPointer>(0, out var monoBehaviour) ||
                            !x1.TryGetResult<MemPointer>(1, out var c1))
                            return;

                        // ROUND 2: InteractiveClass, GameObject, class name ptr
                        round2[i].AddEntry<MemPointer>(2, monoBehaviour + UnityOffsets.Comp_ObjectClass);
                        round2[i].AddEntry<MemPointer>(3, monoBehaviour + UnityOffsets.Comp_GameObject);
                        round2[i].AddEntry<MemPointer>(4, c1 + 0x10);

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

                                if (className.Equals("ObservedLootItem", StringComparison.OrdinalIgnoreCase))
                                    CollectLootItem(round4, round5, round6, i, interactiveClass, components, pendingLoot);
                                else if (className.Contains("Corpse", StringComparison.OrdinalIgnoreCase))
                                    CollectCorpseItem(round4, round5, round6, i, interactiveClass, components, pendingCorpses);
                            };
                        };
                    };
                }

                map.Execute();
            }

            // ── Phase 2: Batched transform + BSG ID resolution ──────────────────
            lootResult = ResolveLootBatched(pendingLoot);
            corpseResult = ResolveCorpsesBatched(pendingCorpses);
        }

        #region Phase 1 — Scatter Callbacks (collect pending items, no serial reads)

        /// <summary>
        /// Intermediate loot data collected during Phase 1 scatter — no serial DMA reads here.
        /// </summary>
        private readonly record struct PendingLoot(ulong TransformInternal, ulong BsgIdStringAddr);

        /// <summary>
        /// Intermediate corpse data collected during Phase 1 scatter — no serial DMA reads here.
        /// </summary>
        private readonly record struct PendingCorpse(ulong InteractiveClass, ulong TransformInternal);

        /// <summary>
        /// Scatter callback for loose loot — resolves transform + BSG ID pointers (rounds 4-6),
        /// then adds to pending list. No serial reads.
        /// </summary>
        private static void CollectLootItem(
            ScatterReadRound round4, ScatterReadRound round5, ScatterReadRound round6,
            int i, ulong interactiveClass, ulong components, List<PendingLoot> pending)
        {
            round4[i].AddEntry<MemPointer>(7, components + 0x08);
            round4[i].AddEntry<MemPointer>(8, interactiveClass + Offsets.InteractiveLootItem.Item);

            round4[i].Callbacks += x4 =>
            {
                if (!x4.TryGetResult<MemPointer>(7, out var t1) ||
                    !x4.TryGetResult<MemPointer>(8, out var item))
                    return;

                round5[i].AddEntry<MemPointer>(9, t1 + UnityOffsets.Comp_ObjectClass);
                round5[i].AddEntry<MemPointer>(10, item + Offsets.LootItem.Template);

                round5[i].Callbacks += x5 =>
                {
                    if (!x5.TryGetResult<MemPointer>(9, out var t2) ||
                        !x5.TryGetResult<MemPointer>(10, out var template))
                        return;

                    round6[i].AddEntry<MemPointer>(11, t2 + 0x10);
                    round6[i].AddEntry<Types.MongoID>(12, template + Offsets.ItemTemplate._id);

                    round6[i].Callbacks += x6 =>
                    {
                        if (!x6.TryGetResult<MemPointer>(11, out var transformInternal) ||
                            !x6.TryGetResult<Types.MongoID>(12, out var mongoId))
                            return;

                        if (!mongoId.StringID.IsValidVirtualAddress())
                            return;

                        lock (pending)
                            pending.Add(new PendingLoot(transformInternal, mongoId.StringID));
                    };
                };
            };
        }

        /// <summary>
        /// Scatter callback for corpse items — resolves transform pointer (rounds 4-6),
        /// then adds to pending list. No serial reads.
        /// </summary>
        private static void CollectCorpseItem(
            ScatterReadRound round4, ScatterReadRound round5, ScatterReadRound round6,
            int i, ulong interactiveClass, ulong components, List<PendingCorpse> pending)
        {
            round4[i].AddEntry<MemPointer>(7, components + 0x08);

            round4[i].Callbacks += x4 =>
            {
                if (!x4.TryGetResult<MemPointer>(7, out var t1))
                    return;

                round5[i].AddEntry<MemPointer>(9, t1 + UnityOffsets.Comp_ObjectClass);

                round5[i].Callbacks += x5 =>
                {
                    if (!x5.TryGetResult<MemPointer>(9, out var t2))
                        return;

                    round6[i].AddEntry<MemPointer>(11, t2 + 0x10);

                    round6[i].Callbacks += x6 =>
                    {
                        if (!x6.TryGetResult<MemPointer>(11, out var transformInternal))
                            return;

                        lock (pending)
                            pending.Add(new PendingCorpse(interactiveClass, transformInternal));
                    };
                };
            };
        }

        #endregion

        #region Phase 2 — Batched Transform + BSG ID Resolution

        /// <summary>
        /// Resolves all pending loot items in batched DMA operations:
        /// <list type="number">
        ///   <item>Batch 1: Read hierarchy + index + BSG ID strings for all items</item>
        ///   <item>Batch 2: Read verticesPtr + indicesPtr from hierarchies</item>
        ///   <item>Batch 3: Read vertices + indices arrays for all valid items</item>
        ///   <item>Compute positions locally (pure math, no DMA)</item>
        /// </list>
        /// </summary>
        private static List<LootItem> ResolveLootBatched(List<PendingLoot> pending)
        {
            if (pending.Count == 0)
                return [];

            var result = new List<LootItem>(pending.Count);

            // Arrays to hold intermediate state across batches
            var hierarchies = new ulong[pending.Count];
            var indices = new int[pending.Count];
            var bsgIds = new string?[pending.Count];
            var verticesPtrs = new ulong[pending.Count];
            var indicesPtrs = new ulong[pending.Count];

            // ── Batch 1: hierarchy + index + BSG ID strings ─────────────────────
            using (var s1 = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    var ti = pending[i].TransformInternal;
                    s1.PrepareReadValue<ulong>(ti + UnityOffsets.TransformAccess.HierarchyOffset);
                    s1.PrepareReadValue<int>(ti + UnityOffsets.TransformAccess.IndexOffset);
                    // Unity string: length at +0x10 (int), chars at +0x14 (UTF-16)
                    s1.PrepareRead(pending[i].BsgIdStringAddr + 0x14, 128);
                }
                s1.Execute();

                for (int i = 0; i < pending.Count; i++)
                {
                    var ti = pending[i].TransformInternal;
                    s1.ReadValue<ulong>(ti + UnityOffsets.TransformAccess.HierarchyOffset, out hierarchies[i]);
                    s1.ReadValue<int>(ti + UnityOffsets.TransformAccess.IndexOffset, out indices[i]);
                    bsgIds[i] = s1.ReadString(pending[i].BsgIdStringAddr + 0x14, 128, Encoding.Unicode);
                }
            }

            // ── Batch 2: verticesPtr + indicesPtr from hierarchies ──────────────
            using (var s2 = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    if (!hierarchies[i].IsValidVirtualAddress() || indices[i] < 0 || indices[i] > 150_000)
                        continue;
                    s2.PrepareReadValue<ulong>(hierarchies[i] + UnityOffsets.TransformHierarchy.VerticesOffset);
                    s2.PrepareReadValue<ulong>(hierarchies[i] + UnityOffsets.TransformHierarchy.IndicesOffset);
                }
                s2.Execute();

                for (int i = 0; i < pending.Count; i++)
                {
                    if (!hierarchies[i].IsValidVirtualAddress() || indices[i] < 0 || indices[i] > 150_000)
                        continue;
                    s2.ReadValue<ulong>(hierarchies[i] + UnityOffsets.TransformHierarchy.VerticesOffset, out verticesPtrs[i]);
                    s2.ReadValue<ulong>(hierarchies[i] + UnityOffsets.TransformHierarchy.IndicesOffset, out indicesPtrs[i]);
                }
            }

            // ── Batch 3: vertices + indices arrays ──────────────────────────────
            using (var s3 = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    if (!verticesPtrs[i].IsValidVirtualAddress() || !indicesPtrs[i].IsValidVirtualAddress())
                        continue;
                    int vertCount = indices[i] + 1;
                    s3.PrepareReadArray<TrsX>(verticesPtrs[i], vertCount);
                    s3.PrepareReadArray<int>(indicesPtrs[i], vertCount);
                }
                s3.Execute();

                // Compute positions and create LootItems
                for (int i = 0; i < pending.Count; i++)
                {
                    try
                    {
                        // Validate BSG ID
                        var rawId = bsgIds[i];
                        if (string.IsNullOrEmpty(rawId))
                            continue;

                        // Trim null terminator if present
                        int nt = rawId.IndexOf('\0');
                        var bsgId = nt >= 0 ? rawId[..nt] : rawId;
                        if (bsgId.Length == 0)
                            continue;

                        if (!EftDataManager.AllItems.TryGetValue(bsgId, out var marketItem))
                            continue;

                        if (!verticesPtrs[i].IsValidVirtualAddress() || !indicesPtrs[i].IsValidVirtualAddress())
                            continue;

                        int vertCount = indices[i] + 1;
                        var vertices = s3.ReadArray<TrsX>(verticesPtrs[i], vertCount);
                        var parentIndices = s3.ReadArray<int>(indicesPtrs[i], vertCount);
                        if (vertices is null || parentIndices is null ||
                            vertices.Length < vertCount || parentIndices.Length < vertCount)
                            continue;

                        var pos = ComputeTransformPosition(vertices, parentIndices, indices[i]);
                        if (pos == Vector3.Zero)
                            continue;

                        result.Add(new LootItem(marketItem, pos));
                    }
                    catch { }
                }
            }

            return result;
        }

        /// <summary>
        /// Resolves all pending corpse items in batched DMA operations (same 3-batch pattern as loot).
        /// </summary>
        private static List<LootCorpse> ResolveCorpsesBatched(List<PendingCorpse> pending)
        {
            if (pending.Count == 0)
                return [];

            var result = new List<LootCorpse>(pending.Count);
            var hierarchies = new ulong[pending.Count];
            var indices = new int[pending.Count];
            var verticesPtrs = new ulong[pending.Count];
            var indicesPtrs = new ulong[pending.Count];

            // ── Batch 1: hierarchy + index ──────────────────────────────────────
            using (var s1 = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    var ti = pending[i].TransformInternal;
                    s1.PrepareReadValue<ulong>(ti + UnityOffsets.TransformAccess.HierarchyOffset);
                    s1.PrepareReadValue<int>(ti + UnityOffsets.TransformAccess.IndexOffset);
                }
                s1.Execute();

                for (int i = 0; i < pending.Count; i++)
                {
                    var ti = pending[i].TransformInternal;
                    s1.ReadValue<ulong>(ti + UnityOffsets.TransformAccess.HierarchyOffset, out hierarchies[i]);
                    s1.ReadValue<int>(ti + UnityOffsets.TransformAccess.IndexOffset, out indices[i]);
                }
            }

            // ── Batch 2: verticesPtr + indicesPtr ───────────────────────────────
            using (var s2 = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    if (!hierarchies[i].IsValidVirtualAddress() || indices[i] < 0 || indices[i] > 150_000)
                        continue;
                    s2.PrepareReadValue<ulong>(hierarchies[i] + UnityOffsets.TransformHierarchy.VerticesOffset);
                    s2.PrepareReadValue<ulong>(hierarchies[i] + UnityOffsets.TransformHierarchy.IndicesOffset);
                }
                s2.Execute();

                for (int i = 0; i < pending.Count; i++)
                {
                    if (!hierarchies[i].IsValidVirtualAddress() || indices[i] < 0 || indices[i] > 150_000)
                        continue;
                    s2.ReadValue<ulong>(hierarchies[i] + UnityOffsets.TransformHierarchy.VerticesOffset, out verticesPtrs[i]);
                    s2.ReadValue<ulong>(hierarchies[i] + UnityOffsets.TransformHierarchy.IndicesOffset, out indicesPtrs[i]);
                }
            }

            // ── Batch 3: vertices + indices arrays ──────────────────────────────
            using (var s3 = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    if (!verticesPtrs[i].IsValidVirtualAddress() || !indicesPtrs[i].IsValidVirtualAddress())
                        continue;
                    int vertCount = indices[i] + 1;
                    s3.PrepareReadArray<TrsX>(verticesPtrs[i], vertCount);
                    s3.PrepareReadArray<int>(indicesPtrs[i], vertCount);
                }
                s3.Execute();

                for (int i = 0; i < pending.Count; i++)
                {
                    try
                    {
                        if (!verticesPtrs[i].IsValidVirtualAddress() || !indicesPtrs[i].IsValidVirtualAddress())
                            continue;

                        int vertCount = indices[i] + 1;
                        var vertices = s3.ReadArray<TrsX>(verticesPtrs[i], vertCount);
                        var parentIndices = s3.ReadArray<int>(indicesPtrs[i], vertCount);
                        if (vertices is null || parentIndices is null ||
                            vertices.Length < vertCount || parentIndices.Length < vertCount)
                            continue;

                        var pos = ComputeTransformPosition(vertices, parentIndices, indices[i]);
                        if (pos == Vector3.Zero)
                            continue;

                        result.Add(new LootCorpse(pending[i].InteractiveClass, pos));
                    }
                    catch { }
                }
            }

            return result;
        }

        /// <summary>
        /// Pure math — computes world position from pre-read vertices + indices.
        /// No DMA reads. Shared by loot and corpse resolution.
        /// </summary>
        private static Vector3 ComputeTransformPosition(TrsX[] vertices, int[] parentIndices, int index)
        {
            var pos = TrsX.ComputeWorldPosition(vertices, parentIndices, index);

            if (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y) || !float.IsFinite(pos.Z))
                return Vector3.Zero;

            if (pos.LengthSquared() < 16f)
                return Vector3.Zero;

            return pos;
        }

        #endregion

        #region Corpse Dogtag Reading

        /// <summary>
        /// Iterates only the known corpses (from the unified scatter pass) and walks their
        /// equipment slots to find dogtag items and seed <see cref="DogtagCache"/>.
        /// This is O(corpses) not O(lootList) — typically 0-5 iterations, not hundreds.
        /// </summary>
        private void ReadCorpseDogtags()
        {
            var corpses = _corpses;
            if (corpses.Count == 0)
                return;

            foreach (var corpse in corpses)
            {
                var interactiveClass = corpse.InteractiveClass;

                // Already processed this corpse?
                if (_processedCorpses.Contains(interactiveClass))
                    continue;

                _processedCorpses.Add(interactiveClass);

                try
                {
                    // Corpse: InteractiveLootItem.Item → base item → LootItemMod.Slots → array of slots
                    if (!Memory.TryReadPtr(interactiveClass + Offsets.InteractiveLootItem.Item, out var itemBase)
                        || !itemBase.IsValidVirtualAddress())
                        continue;

                    if (!Memory.TryReadPtr(itemBase + Offsets.LootItemMod.Slots, out var slotsArr)
                        || !slotsArr.IsValidVirtualAddress())
                        continue;

                    using var slotPtrs = MemArray<ulong>.Get(slotsArr, false);
                    if (slotPtrs.Count < 1 || slotPtrs.Count > 64)
                        continue;

                    for (int si = 0; si < slotPtrs.Count; si++)
                    {
                        var slotPtr = slotPtrs[si];
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

                using var slotPtrs = MemArray<ulong>.Get(slotsArr, false);
                if (slotPtrs.Count < 1 || slotPtrs.Count > 64)
                    return;

                var gear = new Dictionary<string, CorpseGearItem>(slotPtrs.Count, StringComparer.OrdinalIgnoreCase);
                int totalValue = 0;

                for (int si = 0; si < slotPtrs.Count; si++)
                {
                    var slotPtr = slotPtrs[si];
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
    }
}
