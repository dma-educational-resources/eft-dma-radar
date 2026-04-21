using VmmSharpEx.Scatter;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Explosives
{
    /// <summary>
    /// A mortar/artillery projectile tracked on the radar.
    /// Per-tick updates use VmmScatter with Completed callback.
    /// </summary>
    internal sealed class MortarProjectile : IExplosiveItem
    {
        public static implicit operator ulong(MortarProjectile x) => x.Addr;

        private readonly ConcurrentDictionary<ulong, IExplosiveItem> _parent;
        private Vector3 _position;

        public ulong Addr { get; }
        public bool IsActive { get; private set; }
        public ref Vector3 Position => ref _position;

        public MortarProjectile(ulong baseAddr, ConcurrentDictionary<ulong, IExplosiveItem> parent)
        {
            _parent = parent;
            Addr = baseAddr;

            var projectile = Memory.ReadValue<ArtilleryProjectile>(Addr, false);
            IsActive = projectile.IsActive;
            if (!IsActive)
                throw new InvalidOperationException("Mortar projectile already exploded");
            _position = projectile.Position;
        }

        public void OnRefresh(VmmScatter scatter)
        {
            if (!IsActive)
                return;

            scatter.PrepareReadValue<ArtilleryProjectile>(this);
            scatter.Completed += (_, s) =>
            {
                if (s.ReadValue<ArtilleryProjectile>(this, out var projectile))
                {
                    IsActive = projectile.IsActive;
                    if (IsActive)
                    {
                        if (float.IsFinite(projectile.Position.X) &&
                            float.IsFinite(projectile.Position.Y) &&
                            float.IsFinite(projectile.Position.Z))
                        {
                            _position = projectile.Position;
                        }
                    }
                    else
                    {
                        _parent.TryRemove(Addr, out IExplosiveItem? _);
                    }
                }
            };
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
