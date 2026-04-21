using eft_dma_radar.Silk.Tarkov.Unity;
using VmmSharpEx.Scatter;
using static eft_dma_radar.Silk.Tarkov.Unity.UnityOffsets;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Explosives
{
    /// <summary>
    /// A live grenade/throwable tracked on the radar.
    /// Caches transform hierarchy at construction; per-tick updates use VmmScatter.
    /// </summary>
    internal sealed class Grenade : IExplosiveItem
    {
        public static implicit operator ulong(Grenade x) => x.Addr;

        private static readonly Dictionary<string, float> EffectiveDistances =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "F-1", 7f }, { "M67", 8f }, { "RGD-5", 7f }, { "RGN", 5f },
                { "RGO", 7f }, { "V40", 5f }, { "VOG-17", 6f }, { "VOG-25", 7f }
            };

        private readonly ConcurrentDictionary<ulong, IExplosiveItem> _parent;
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly bool _isSmoke;

        // Cached transform hierarchy (read once at construction)
        private readonly ulong _verticesAddr;
        private readonly int _vertexCount;
        private readonly ReadOnlyMemory<int> _indices;
        private readonly int _transformIndex;

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

            // Check if smoke grenade (they never leave the list, skip transform work)
            var type = Il2CppClass.ReadName(baseAddr, 64, false);
            if (type is not null && type.Contains("SmokeGrenade"))
            {
                _isSmoke = true;
                Name = "Smoke";
                return;
            }

            // Read and cache transform hierarchy
            var ti = Memory.ReadPtrChain(baseAddr, TransformChain, false);

            var hierarchy = Memory.ReadValue<ulong>(ti + TransformAccess.HierarchyOffset, false);
            if (!Extensions.IsValidVirtualAddress(hierarchy))
                throw new InvalidOperationException("Invalid hierarchy pointer");

            _transformIndex = Memory.ReadValue<int>(ti + TransformAccess.IndexOffset, false);
            if (_transformIndex < 0 || _transformIndex > 128_000)
                throw new ArgumentOutOfRangeException(nameof(baseAddr), _transformIndex, "Transform index out of range");

            _vertexCount = _transformIndex + 1;

            var verticesPtr = Memory.ReadValue<ulong>(hierarchy + TransformHierarchy.VerticesOffset, false);
            var indicesPtr = Memory.ReadValue<ulong>(hierarchy + TransformHierarchy.IndicesOffset, false);
            if (!Extensions.IsValidVirtualAddress(verticesPtr) || !Extensions.IsValidVirtualAddress(indicesPtr))
                throw new InvalidOperationException("Invalid vertices/indices pointer");

            _verticesAddr = verticesPtr;

            // Cache indices once (they don't change for the life of the grenade)
            _indices = Memory.ReadArray<int>(indicesPtr, _vertexCount);

            // Check detonated
            if (Memory.ReadValue<bool>(baseAddr + Offsets.Grenade.IsDestroyed, false))
                throw new InvalidOperationException("Grenade detonated at creation");

            // Resolve name
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

            // Initial position (direct read)
            UpdatePositionDirect();
        }

        public void OnRefresh(VmmScatter scatter)
        {
            if (_isSmoke || !IsActive)
                return;

            scatter.PrepareReadValue<bool>(this + Offsets.Grenade.IsDestroyed);
            scatter.PrepareReadArray<TrsX>(_verticesAddr, _vertexCount);
            scatter.Completed += (_, s) =>
            {
                if (s.ReadValue<bool>(this + Offsets.Grenade.IsDestroyed, out bool destroyed) && destroyed)
                {
                    _parent.TryRemove(Addr, out IExplosiveItem? _);
                    _forceInactive = true;
                    return;
                }

                if (s.ReadPooled<TrsX>(_verticesAddr, _vertexCount) is IMemoryOwner<TrsX> vertices)
                {
                    using (vertices)
                    {
                        var pos = TrsX.ComputeWorldPosition(vertices.Memory.Span, _indices.Span, _transformIndex);
                        if (float.IsFinite(pos.X) && float.IsFinite(pos.Y) && float.IsFinite(pos.Z))
                            _position = pos;
                    }
                }
            };
        }

        public void Draw(SKCanvas canvas, MapParams mapParams, MapConfig mapCfg, Player.Player localPlayer)
        {
            if (!IsActive || _isSmoke || _position == Vector3.Zero)
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

        private void UpdatePositionDirect()
        {
            try
            {
                var vertices = Memory.ReadArray<TrsX>(_verticesAddr, _vertexCount);
                var pos = TrsX.ComputeWorldPosition(vertices, _indices.Span, _transformIndex);
                if (float.IsFinite(pos.X) && float.IsFinite(pos.Y) && float.IsFinite(pos.Z))
                    _position = pos;
            }
            catch { }
        }
    }
}
