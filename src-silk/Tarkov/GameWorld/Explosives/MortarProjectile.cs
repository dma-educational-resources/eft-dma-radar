using eft_dma_radar.Silk.DMA.ScatterAPI;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Explosives
{
    /// <summary>
    /// A mortar/artillery projectile tracked on the radar.
    /// Per-tick updates use scatter reads; initial state uses direct DMA.
    /// </summary>
    internal sealed class MortarProjectile : IExplosiveItem
    {
        private static int _nextScatterId = 300_000;

        private readonly ConcurrentDictionary<ulong, IExplosiveItem> _parent;
        private readonly int _scatterIdActive;
        private readonly int _scatterIdPos;
        private Vector3 _position;

        public ulong Addr { get; }
        public bool IsActive { get; private set; }
        public ref Vector3 Position => ref _position;

        public MortarProjectile(ulong baseAddr, ConcurrentDictionary<ulong, IExplosiveItem> parent)
        {
            _parent = parent;
            Addr = baseAddr;

            var baseId = Interlocked.Add(ref _nextScatterId, 2);
            _scatterIdActive = baseId;
            _scatterIdPos = baseId + 1;

            Refresh();
            if (!IsActive)
                throw new InvalidOperationException("Mortar projectile already exploded");
        }

        public void Refresh()
        {
            var projectile = Memory.ReadValue<ArtilleryProjectile>(Addr, false);
            IsActive = projectile.IsActive;
            if (IsActive)
            {
                _position = projectile.Position;
            }
            else
            {
                _parent.TryRemove(Addr, out _);
            }
        }

        public void QueueScatterReads(ScatterReadIndex idx)
        {
            if (!IsActive)
                return;

            idx.AddEntry<bool>(_scatterIdActive, Addr + Offsets.ArtilleryProjectileClient.IsActive);
            idx.AddEntry<Vector3>(_scatterIdPos, Addr + Offsets.ArtilleryProjectileClient.Position);
        }

        public void ApplyScatterResults(ScatterReadIndex idx)
        {
            if (idx.TryGetResult<bool>(_scatterIdActive, out var active))
            {
                IsActive = active;
            }

            if (!IsActive)
            {
                _parent.TryRemove(Addr, out _);
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

            const float size = 5f;
            canvas.DrawCircle(point, size, SKPaints.ShapeBorder);
            canvas.DrawCircle(point, size, SKPaints.PaintExplosives);

            // Name label
            var namePt = new SKPoint(point.X + 7f, point.Y + 4f);
            canvas.DrawText("Mortar", namePt, SKTextAlign.Left, SKPaints.FontRegular11, SKPaints.TextShadow);
            canvas.DrawText("Mortar", namePt, SKTextAlign.Left, SKPaints.FontRegular11, SKPaints.TextExplosives);

            // Distance label
            var distText = $"{(int)dist}m";
            var distWidth = SKPaints.FontRegular11.MeasureText(distText, SKPaints.TextExplosives);
            var distPt = new SKPoint(point.X - distWidth / 2f, point.Y + 16f);
            canvas.DrawText(distText, distPt, SKTextAlign.Left, SKPaints.FontRegular11, SKPaints.TextShadow);
            canvas.DrawText(distText, distPt, SKTextAlign.Left, SKPaints.FontRegular11, SKPaints.TextExplosives);
        }

        [StructLayout(LayoutKind.Explicit, Pack = 1)]
        private readonly struct ArtilleryProjectile
        {
            [FieldOffset((int)Offsets.ArtilleryProjectileClient.Position)]
            public readonly Vector3 Position;

            [FieldOffset((int)Offsets.ArtilleryProjectileClient.IsActive)]
            public readonly bool IsActive;
        }
    }
}
