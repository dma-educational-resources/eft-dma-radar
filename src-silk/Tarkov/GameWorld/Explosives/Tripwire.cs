using VmmSharpEx.Scatter;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Explosives
{
    /// <summary>
    /// A placed tripwire tracked on the radar.
    /// Position is static (read once); per-tick scatter reads only check state.
    /// </summary>
    internal sealed class Tripwire : IExplosiveItem
    {
        public static implicit operator ulong(Tripwire x) => x.Addr;

        private Vector3 _position;
        private Vector3 _fromPosition;
        private bool _destroyed;

        public ulong Addr { get; }
        public bool IsActive { get; private set; }
        public string Name { get; private set; }
        public ref Vector3 Position => ref _position;
        public ref Vector3 FromPosition => ref _fromPosition;

        public Tripwire(ulong baseAddr)
        {
            Addr = baseAddr;

            // Position is static — read once at construction
            _position = ReadToPosition(false);
            _fromPosition = ReadFromPosition(false);
            IsActive = ReadIsActive(false);
            Name = ResolveName();
        }

        public void OnRefresh(VmmScatter scatter)
        {
            if (_destroyed)
                return;

            scatter.PrepareReadValue<int>(this + Offsets.TripwireSynchronizableObject._tripwireState);
            scatter.Completed += (_, s) =>
            {
                if (s.ReadValue(this + Offsets.TripwireSynchronizableObject._tripwireState, out int nState))
                {
                    var state = (SDK.ETripwireState)nState;
                    _destroyed = state is SDK.ETripwireState.Exploded or SDK.ETripwireState.Inert;
                    IsActive = state is SDK.ETripwireState.Wait or SDK.ETripwireState.Active;
                }
            };
        }

        public void Draw(SKCanvas canvas, MapParams mapParams, MapConfig mapCfg, Player.Player localPlayer)
        {
            if (!IsActive)
                return;

            var dist = Vector3.Distance(localPlayer.Position, _position);

            var toScreenPos = mapParams.ToScreenPos(MapParams.ToMapPos(_position, mapCfg));
            var fromScreenPos = mapParams.ToScreenPos(MapParams.ToMapPos(_fromPosition, mapCfg));

            const float size = 5f;

            // Draw tripwire line between endpoints
            if (SilkProgram.Config.ShowTripwireLines)
            {
                canvas.DrawLine(fromScreenPos, toScreenPos, SKPaints.ShapeBorder);
                canvas.DrawLine(fromScreenPos, toScreenPos, SKPaints.PaintTripwireLine);
            }

            // Draw endpoint markers
            canvas.DrawCircle(toScreenPos, size, SKPaints.ShapeBorder);
            canvas.DrawCircle(toScreenPos, size, SKPaints.PaintExplosives);

            if (SilkProgram.Config.ShowTripwireLines)
            {
                canvas.DrawCircle(fromScreenPos, size, SKPaints.ShapeBorder);
                canvas.DrawCircle(fromScreenPos, size, SKPaints.PaintExplosives);
            }

            // Name label above the ToPosition endpoint
            if (!string.IsNullOrEmpty(Name))
            {
                var nameWidth = SKPaints.FontRegular11.MeasureText(Name, SKPaints.TextExplosives);
                var namePt = new SKPoint(toScreenPos.X - nameWidth / 2f, toScreenPos.Y - 10f);
                canvas.DrawText(Name, namePt, SKTextAlign.Left, SKPaints.FontRegular11, SKPaints.TextShadow);
                canvas.DrawText(Name, namePt, SKTextAlign.Left, SKPaints.FontRegular11, SKPaints.TextExplosives);
            }

            // Distance label
            var distText = $"{(int)dist}m";
            var distWidth = SKPaints.FontRegular11.MeasureText(distText, SKPaints.TextExplosives);
            var distPt = new SKPoint(toScreenPos.X - distWidth / 2f, toScreenPos.Y + 16f);
            canvas.DrawText(distText, distPt, SKTextAlign.Left, SKPaints.FontRegular11, SKPaints.TextShadow);
            canvas.DrawText(distText, distPt, SKTextAlign.Left, SKPaints.FontRegular11, SKPaints.TextExplosives);
        }

        private bool ReadIsActive(bool useCache = true)
        {
            var state = (SDK.ETripwireState)Memory.ReadValue<int>(
                Addr + Offsets.TripwireSynchronizableObject._tripwireState, useCache);
            return state is SDK.ETripwireState.Wait or SDK.ETripwireState.Active;
        }

        private Vector3 ReadToPosition(bool useCache = true)
        {
            var pos = Memory.ReadValue<Vector3>(
                Addr + Offsets.TripwireSynchronizableObject.ToPosition, useCache);
            pos.Y += 0.175f;
            return pos;
        }

        private Vector3 ReadFromPosition(bool useCache = true)
        {
            var pos = Memory.ReadValue<Vector3>(
                Addr + Offsets.TripwireSynchronizableObject.FromPosition, useCache);
            pos.Y += 0.175f;
            return pos;
        }

        private string ResolveName()
        {
            if (!IsActive)
                return "";

            try
            {
                var id = Memory.ReadValue<SDK.Types.MongoID>(
                    Addr + Offsets.TripwireSynchronizableObject.GrenadeTemplateId);
                var name = Memory.ReadUnityString(id.StringID, useCache: false);

                if (!string.IsNullOrEmpty(name) && EftDataManager.AllItems.TryGetValue(name, out var item))
                {
                    var resultName = item.ShortName;
                    if (item.BsgId == "67b49e7335dec48e3e05e057")
                        resultName = $"{resultName} (SHORT)";
                    return resultName;
                }
            }
            catch { }

            return "Tripwire";
        }
    }
}
