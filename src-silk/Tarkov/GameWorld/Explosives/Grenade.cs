using eft_dma_radar.Silk.DMA.ScatterAPI;
using eft_dma_radar.Silk.Tarkov.Unity;
using static eft_dma_radar.Silk.Tarkov.Unity.UnityOffsets;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Explosives
{
    /// <summary>
    /// A live grenade/throwable tracked on the radar.
    /// Per-tick updates use scatter reads; initial position uses direct DMA.
    /// </summary>
    internal sealed class Grenade : IExplosiveItem
    {
        private static readonly Dictionary<string, float> EffectiveDistances =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "F-1", 7f }, { "M67", 8f }, { "RGD-5", 7f }, { "RGN", 5f },
                { "RGO", 7f }, { "V40", 5f }, { "VOG-17", 6f }, { "VOG-25", 7f }
            };

        /// <summary>Offset for cached world position in Unity TransformInternal.</summary>
        private const uint TRANSFORM_WORLD_POS = 0x90;

        private static int _nextScatterId;

        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly ConcurrentDictionary<ulong, IExplosiveItem> _parent;
        private readonly ulong _transformInternal;
        private readonly int _scatterIdDestroyed;
        private readonly int _scatterIdPos;

        private Vector3 _position;
        private bool _forceInactive;

        public ulong Addr { get; }
        public string Name { get; }
        public float EffectiveDistance { get; }
        public bool IsActive => _sw.Elapsed.TotalSeconds < 12f && !_forceInactive;
        public ref Vector3 Position => ref _position;

        public Grenade(ulong baseAddr, ConcurrentDictionary<ulong, IExplosiveItem> parent)
        {
            Addr = baseAddr;
            _parent = parent;

            // Allocate unique scatter IDs
            var baseId = Interlocked.Add(ref _nextScatterId, 2);
            _scatterIdDestroyed = baseId;
            _scatterIdPos = baseId + 1;

            _transformInternal = Memory.ReadPtrChain(baseAddr, TransformChain, false);

            if (IsDetonatedDirect())
                throw new InvalidOperationException("Grenade detonated at creation");

            // Resolve grenade name from template
            var templatePtr = Memory.ReadPtrChain(baseAddr,
                [Offsets.Grenade.WeaponSource, Offsets.LootItem.Template], false);
            var id = Memory.ReadValue<SDK.Types.MongoID>(templatePtr + Offsets.ItemTemplate._id);
            var rawName = Memory.ReadUnityString(id.StringID, useCache: false) ?? "";

            if (EftDataManager.AllItems.TryGetValue(rawName, out var grenadeItem))
            {
                Name = grenadeItem.ShortName;
                if (grenadeItem.BsgId == "67b49e7335dec48e3e05e057")
                    Name = $"{Name} (SHORT)";
            }
            else
            {
                Name = rawName;
            }

            EffectiveDistance = !string.IsNullOrEmpty(Name) && EffectiveDistances.TryGetValue(Name, out float dist)
                ? dist
                : 0f;

            // Initial position read (direct DMA — only at creation)
            Refresh();
        }

        public void Refresh()
        {
            if (!IsActive)
                return;

            if (IsDetonatedDirect())
            {
                _parent.TryRemove(Addr, out _);
                _forceInactive = true;
                return;
            }

            if (_transformInternal == 0)
                return;

            _position = ReadTransformPosition(_transformInternal);
        }

        public void QueueScatterReads(ScatterReadIndex idx)
        {
            if (!IsActive)
                return;

            idx.AddEntry<bool>(_scatterIdDestroyed, Addr + Offsets.Grenade.IsDestroyed);

            if (_transformInternal != 0)
                idx.AddEntry<Vector3>(_scatterIdPos, _transformInternal + TRANSFORM_WORLD_POS);
        }

        public void ApplyScatterResults(ScatterReadIndex idx)
        {
            if (!IsActive)
                return;

            if (idx.TryGetResult<bool>(_scatterIdDestroyed, out var isDead) && isDead)
            {
                _parent.TryRemove(Addr, out _);
                _forceInactive = true;
                return;
            }

            if (idx.TryGetResult<Vector3>(_scatterIdPos, out var pos) &&
                float.IsFinite(pos.X) && float.IsFinite(pos.Y) && float.IsFinite(pos.Z))
            {
                _position = pos;
            }
        }

        public void Draw(SKCanvas canvas, MapParams mapParams, MapConfig mapCfg, Player.Player localPlayer)
        {
            if (!IsActive || _position == Vector3.Zero)
                return;

            var dist = Vector3.Distance(localPlayer.Position, _position);
            var point = mapParams.ToScreenPos(MapParams.ToMapPos(_position, mapCfg));

            var isInDanger = EffectiveDistance > 0 && dist <= EffectiveDistance;
            var fillPaint = isInDanger ? SKPaints.PaintExplosivesDanger : SKPaints.PaintExplosives;
            var textPaint = isInDanger ? SKPaints.TextExplosivesDanger : SKPaints.TextExplosives;

            const float size = 5f;
            canvas.DrawCircle(point, size, SKPaints.ShapeBorder);
            canvas.DrawCircle(point, size, fillPaint);

            // Blast radius circle
            if (EffectiveDistance > 0)
            {
                float radiusUnscaled = EffectiveDistance * mapCfg.Scale * mapCfg.SvgScale;
                float radius = radiusUnscaled * mapParams.XScale;
                canvas.DrawCircle(point, radius, SKPaints.PaintExplosivesRadius);
            }

            // Name label
            if (!string.IsNullOrEmpty(Name))
            {
                var nameWidth = SKPaints.FontRegular11.MeasureText(Name, textPaint);
                var namePt = new SKPoint(point.X - nameWidth / 2f, point.Y - 10f);
                canvas.DrawText(Name, namePt, SKTextAlign.Left, SKPaints.FontRegular11, SKPaints.TextShadow);
                canvas.DrawText(Name, namePt, SKTextAlign.Left, SKPaints.FontRegular11, textPaint);
            }

            // Distance label
            var distText = $"{(int)dist}m";
            var distWidth = SKPaints.FontRegular11.MeasureText(distText, textPaint);
            var distPt = new SKPoint(point.X - distWidth / 2f, point.Y + 16f);
            canvas.DrawText(distText, distPt, SKTextAlign.Left, SKPaints.FontRegular11, SKPaints.TextShadow);
            canvas.DrawText(distText, distPt, SKTextAlign.Left, SKPaints.FontRegular11, textPaint);
        }

        private bool IsDetonatedDirect() =>
            Memory.ReadValue<bool>(Addr + Offsets.Grenade.IsDestroyed, false);

        /// <summary>
        /// Reads world position from a TransformInternal pointer using the hierarchy walk.
        /// </summary>
        private static Vector3 ReadTransformPosition(ulong transformInternal)
        {
            try
            {
                var hierarchy = Memory.ReadValue<ulong>(transformInternal + TransformAccess.HierarchyOffset);
                if (!Extensions.IsValidVirtualAddress(hierarchy))
                    return Vector3.Zero;

                var index = Memory.ReadValue<int>(transformInternal + TransformAccess.IndexOffset);
                if (index < 0 || index > 150_000)
                    return Vector3.Zero;

                var verticesPtr = Memory.ReadValue<ulong>(hierarchy + TransformHierarchy.VerticesOffset);
                var indicesPtr = Memory.ReadValue<ulong>(hierarchy + TransformHierarchy.IndicesOffset);
                if (!Extensions.IsValidVirtualAddress(verticesPtr) || !Extensions.IsValidVirtualAddress(indicesPtr))
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
            catch
            {
                return Vector3.Zero;
            }
        }
    }
}
