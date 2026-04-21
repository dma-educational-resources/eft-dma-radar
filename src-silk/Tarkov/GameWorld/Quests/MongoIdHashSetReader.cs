using System.Runtime.CompilerServices;

using eft_dma_radar.Silk.Tarkov.Unity;
using static eft_dma_radar.Silk.Tarkov.Unity.UnityOffsets;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Quests
{
    /// <summary>
    /// Shared helper for reading IL2CPP <c>HashSet&lt;MongoID&gt;</c> structures used by
    /// the profile's completed-conditions store. Mirrors the validation rules used by
    /// <see cref="QuestManager"/> and <see cref="QuestPlanner.QuestPlannerMemoryReader"/>.
    /// </summary>
    internal static class MongoIdHashSetReader
    {
        private const int MaxHashCount = 100;
        private const int MaxResolved = 50;
        private const int MinIdLength = 11; // > 10
        private const int MaxIdLength = 99; // < 100
        private const ulong MinStringIdPtr = 0x10000000UL;

        /// <summary>
        /// Reads up to <see cref="MaxResolved"/> MongoIDs from the HashSet and adds each
        /// resolved ID to <paramref name="primary"/>. If <paramref name="secondary"/> is
        /// non-null, IDs are added there too (used to aggregate per-quest + all-quests sets).
        /// Silently returns on bad pointers / out-of-range counts.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Read(ulong hashSetPtr, HashSet<string> primary, HashSet<string>? secondary = null)
        {
            if (hashSetPtr == 0) return;

            if (!Memory.TryReadPtr(hashSetPtr + IL2CPPHashSet.Entries, out var entriesPtr, false))
                return;

            if (!Memory.TryReadValue(hashSetPtr + IL2CPPHashSet.Count, out int hashCount, false))
                return;

            // Fallback count offsets if primary fails / reports garbage
            if (hashCount <= 0 || hashCount > MaxHashCount)
                Memory.TryReadValue(hashSetPtr + 0x20, out hashCount, false);
            if (hashCount <= 0 || hashCount > MaxHashCount)
                Memory.TryReadValue(hashSetPtr + 0x3C, out hashCount, false);
            if (hashCount <= 0 || hashCount > MaxHashCount)
                return;

            int found = 0;
            for (int i = 0; i < hashCount && found < MaxResolved; i++)
            {
                var entryOffset = (ulong)(i * IL2CPPHashSet.EntrySize);
                var entryBase = entriesPtr + ManagedArray.FirstElement + entryOffset;

                if (!Memory.TryReadPtr(entryBase + IL2CPPHashSet.EntryValueOffset + MongoID.StringID, out var stringIdPtr, false))
                    continue;
                if (stringIdPtr < MinStringIdPtr)
                    continue;

                var id = Memory.ReadUnityString(stringIdPtr);
                if (!string.IsNullOrEmpty(id) && id.Length >= MinIdLength && id.Length <= MaxIdLength)
                {
                    primary.Add(id);
                    secondary?.Add(id);
                    found++;
                }
            }
        }
    }
}
