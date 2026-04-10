using eft_dma_radar.Silk.Tarkov.Unity;
using static eft_dma_radar.Silk.Tarkov.Unity.UnityOffsets;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Exits
{
    /// <summary>
    /// An exfiltration point with position, status, and eligibility data.
    /// Rendered on the radar as a colored circle with name/distance label.
    /// </summary>
    internal sealed class Exfil
    {
        private readonly ulong _addr;
        private readonly bool _isPmc;

        /// <summary>Display name (resolved from <see cref="ExfilNames"/> or raw game name).</summary>
        public string Name { get; }

        /// <summary>World position (static — read once at construction).</summary>
        public Vector3 Position { get; }

        /// <summary>Current exfil status (updated periodically by <see cref="ExfilManager"/>).</summary>
        public ExfilStatus Status { get; set; } = ExfilStatus.Closed;

        /// <summary>Whether this exfil is a secret extract.</summary>
        public bool IsSecret { get; }

        /// <summary>PMC entry points that can use this exfil.</summary>
        public HashSet<string> PmcEntries { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Scav profile IDs eligible for this exfil.</summary>
        public HashSet<string> ScavIds { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Raw address for scatter status reads.</summary>
        public ulong StatusAddr => _addr + Offsets.Exfil._status;

        public Exfil(ulong baseAddr, bool isPmc, string mapId)
        {
            _addr = baseAddr;
            _isPmc = isPmc;

            // Read name from ExfilSettings
            string rawName = "Unknown";
            if (Memory.TryReadPtrChain(baseAddr,
                    [Offsets.Exfil.Settings, Offsets.ExfilSettings.Name], out var namePtr, false)
                && Memory.TryReadUnityString(namePtr, out var nameStr)
                && !string.IsNullOrWhiteSpace(nameStr))
            {
                rawName = nameStr.Trim();
            }

            // Resolve display name from static data
            if (ExfilNames.Names.TryGetValue(mapId, out var mapExfils)
                && mapExfils.TryGetValue(rawName, out var displayName))
            {
                Name = displayName;
            }
            else
            {
                Name = rawName;
            }

            IsSecret = Name.Contains("Secret", StringComparison.OrdinalIgnoreCase);

            // Read position via transform chain
            Position = ReadPosition(baseAddr);
        }

        /// <summary>
        /// Updates status and reads eligible entry points / scav IDs.
        /// </summary>
        public void Update(int rawStatus)
        {
            Status = rawStatus switch
            {
                3 or 4 => ExfilStatus.Open,     // Countdown, RegularMode
                2 or 5 or 6 => ExfilStatus.Pending, // UncompleteRequirements, Pending, AwaitsManualActivation
                _ => ExfilStatus.Closed,          // NotPresent(1), Hidden(7), unknown
            };

            // Read eligible entry points (PMC) or eligible IDs (Scav)
            try
            {
                if (_isPmc)
                {
                    if (Memory.TryReadPtr(_addr + Offsets.Exfil.EligibleEntryPoints, out var entriesArrPtr))
                        ReadStringArray(entriesArrPtr, PmcEntries);
                }
                else
                {
                    if (Memory.TryReadPtr(_addr + Offsets.ScavExfil.EligibleIds, out var idsListPtr))
                        ReadStringList(idsListPtr, ScavIds);
                }
            }
            catch { }
        }

        /// <summary>
        /// Checks whether this exfil is available for the given local player.
        /// </summary>
        public bool IsAvailableFor(Player.LocalPlayer localPlayer)
        {
            bool eligible = (localPlayer.IsPmc && PmcEntries.Contains(localPlayer.EntryPoint ?? "NULL"))
                         || (localPlayer.IsScav && ScavIds.Contains(localPlayer.LocalProfileId ?? "NULL"))
                         || IsSecret;

            return eligible && Status != ExfilStatus.Closed;
        }

        /// <summary>
        /// Draws this exfil on the radar canvas.
        /// </summary>
        public void Draw(SKCanvas canvas, MapParams mapParams, MapConfig mapConfig, Player.Player localPlayer)
        {
            var mapPos = MapParams.ToMapPos(Position, mapConfig);
            var screenPos = mapParams.ToScreenPos(mapPos);

            var (dot, text) = GetPaints(localPlayer as Player.LocalPlayer);

            // Draw marker circle
            canvas.DrawCircle(screenPos, 5f, SKPaints.ShapeBorder);
            canvas.DrawCircle(screenPos, 5f, dot);

            // Draw name label
            float lx = screenPos.X + 7f;
            float ly = screenPos.Y + 4.5f;
            canvas.DrawText(Name, lx + 1, ly + 1, SKTextAlign.Left, SKPaints.FontRegular11, SKPaints.TextShadow);
            canvas.DrawText(Name, lx, ly, SKTextAlign.Left, SKPaints.FontRegular11, text);

            // Draw distance
            var dist = Vector3.Distance(localPlayer.Position, Position);
            var distText = $"{(int)dist}m";
            var distWidth = SKPaints.FontRegular11.MeasureText(distText);
            float dx = screenPos.X - distWidth / 2;
            float dy = screenPos.Y + 16f;
            canvas.DrawText(distText, dx + 1, dy + 1, SKTextAlign.Left, SKPaints.FontRegular11, SKPaints.TextShadow);
            canvas.DrawText(distText, dx, dy, SKTextAlign.Left, SKPaints.FontRegular11, text);
        }

        private (SKPaint dot, SKPaint text) GetPaints(Player.LocalPlayer? localPlayer)
        {
            if (localPlayer is not null && !IsAvailableFor(localPlayer))
                return (SKPaints.PaintExfilInactive, SKPaints.TextExfilInactive);

            return Status switch
            {
                ExfilStatus.Open => (SKPaints.PaintExfilOpen, SKPaints.TextExfilOpen),
                ExfilStatus.Pending => (SKPaints.PaintExfilPending, SKPaints.TextExfilPending),
                _ => (SKPaints.PaintExfilClosed, SKPaints.TextExfilClosed),
            };
        }

        #region Position Reading

        /// <summary>
        /// Reads world position from the exfil's transform via the standard 6-step pointer chain.
        /// Chain: baseAddr → +0x10 (MonoBehaviour) → +0x58 (GameObject) → +0x58 (Components)
        ///        → +0x08 (Transform) → +0x20 (ObjectClass) → +0x10 (TransformInternal).
        /// Same chain used by LootManager for loot items and corpses.
        /// </summary>
        private static Vector3 ReadPosition(ulong baseAddr)
        {
            try
            {
                if (!Memory.TryReadPtrChain(baseAddr, UnityOffsets.TransformChain, out var transformInternal, false))
                    return Vector3.Zero;

                return ReadTransformPosition(transformInternal);
            }
            catch
            {
                return Vector3.Zero;
            }
        }

        /// <summary>
        /// Reads world position from a TransformInternal pointer using the hierarchy walk.
        /// </summary>
        private static Vector3 ReadTransformPosition(ulong transformInternal)
        {
            var hierarchy = Memory.ReadValue<ulong>(transformInternal + TransformAccess.HierarchyOffset);
            if (!Utils.IsValidVirtualAddress(hierarchy))
                return Vector3.Zero;

            var index = Memory.ReadValue<int>(transformInternal + TransformAccess.IndexOffset);
            if (index < 0 || index > 150_000)
                return Vector3.Zero;

            var verticesPtr = Memory.ReadValue<ulong>(hierarchy + TransformHierarchy.VerticesOffset);
            var indicesPtr = Memory.ReadValue<ulong>(hierarchy + TransformHierarchy.IndicesOffset);
            if (!Utils.IsValidVirtualAddress(verticesPtr) || !Utils.IsValidVirtualAddress(indicesPtr))
                return Vector3.Zero;

            int count = index + 1;
            var vertices = Memory.ReadArray<TrsX>(verticesPtr, count);
            var indices = Memory.ReadArray<int>(indicesPtr, count);

            if (vertices.Length < count || indices.Length < count)
                return Vector3.Zero;

            var pos = vertices[index].T;
            int parent = indices[index];
            int iter = 0;

            while (parent >= 0 && parent < count && iter++ < 4096)
            {
                ref readonly var p = ref vertices[parent];
                pos = Vector3.Transform(pos, p.Q);
                pos *= p.S;
                pos += p.T;
                parent = indices[parent];
            }

            if (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y) || !float.IsFinite(pos.Z))
                return Vector3.Zero;

            return pos;
        }

        #endregion

        #region Array/List Helpers

        /// <summary>
        /// Reads a C# array of Unity strings (string[]) and adds them to the target set.
        /// IL2CPP Array: [0x18] = count, [0x20] = first element.
        /// </summary>
        private static void ReadStringArray(ulong arrayPtr, HashSet<string> target)
        {
            var count = Memory.ReadValue<int>(arrayPtr + 0x18, false);
            if (count <= 0 || count > 100)
                return;

            for (int i = 0; i < count; i++)
            {
                if (Memory.TryReadPtr(arrayPtr + 0x20 + (ulong)(i * 8), out var strPtr))
                {
                    if (Memory.TryReadUnityString(strPtr, out var str) && str is not null)
                        target.Add(str);
                }
            }
        }

        /// <summary>
        /// Reads a C# List of Unity strings and adds them to the target set.
        /// IL2CPP List: [0x10] = _items (array), [0x18] = _size.
        /// </summary>
        private static void ReadStringList(ulong listPtr, HashSet<string> target)
        {
            if (!Memory.TryReadPtr(listPtr + 0x10, out var arrPtr))
                return;
            var count = Memory.ReadValue<int>(listPtr + 0x18, false);
            if (count <= 0 || count > 100)
                return;

            for (int i = 0; i < count; i++)
            {
                if (Memory.TryReadPtr(arrPtr + 0x20 + (ulong)(i * 8), out var strPtr))
                {
                    if (Memory.TryReadUnityString(strPtr, out var str) && str is not null)
                        target.Add(str);
                }
            }
        }

        #endregion
    }
}
