using eft_dma_radar.Silk.DMA.ScatterAPI;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Explosives
{
    /// <summary>
    /// A placed tripwire tracked on the radar.
    /// Per-tick updates use scatter reads; initial state uses direct DMA.
    /// </summary>
    internal sealed class Tripwire : IExplosiveItem
    {
        private static int _nextScatterId = 200_000;

        private readonly int _scatterIdState;
        private readonly int _scatterIdToPos;
        private readonly int _scatterIdFromPos;

        private Vector3 _position;
        private Vector3 _fromPosition;

        public ulong Addr { get; }
        public bool IsActive { get; private set; }
        public string Name { get; private set; }
        public ref Vector3 Position => ref _position;
        public ref Vector3 FromPosition => ref _fromPosition;

        public Tripwire(ulong baseAddr)
        {
            Addr = baseAddr;

            var baseId = Interlocked.Add(ref _nextScatterId, 3);
            _scatterIdState = baseId;
            _scatterIdToPos = baseId + 1;
            _scatterIdFromPos = baseId + 2;

            IsActive = ReadIsActive(false);
            _position = ReadToPosition(false);
            _fromPosition = ReadFromPosition(false);
            Name = ResolveName();
        }

        public void Refresh()
        {
            IsActive = ReadIsActive();

            if (!IsActive)
                return;

            _position = ReadToPosition();
            _fromPosition = ReadFromPosition();

            if (string.IsNullOrEmpty(Name))
                Name = ResolveName();
        }

        public void QueueScatterReads(ScatterReadIndex idx)
        {
            if (!IsActive)
                return;

            idx.AddEntry<int>(_scatterIdState, Addr + Offsets.TripwireSynchronizableObject._tripwireState);
            idx.AddEntry<Vector3>(_scatterIdToPos, Addr + Offsets.TripwireSynchronizableObject.ToPosition);
            idx.AddEntry<Vector3>(_scatterIdFromPos, Addr + Offsets.TripwireSynchronizableObject.FromPosition);
        }

        public void ApplyScatterResults(ScatterReadIndex idx)
        {
            if (idx.TryGetResult<int>(_scatterIdState, out var state))
            {
                var tripState = (SDK.ETripwireState)state;
                IsActive = tripState is SDK.ETripwireState.Wait or SDK.ETripwireState.Active;
            }

            if (!IsActive)
                return;

            if (idx.TryGetResult<Vector3>(_scatterIdToPos, out var toPos))
            {
                toPos.Y += 0.175f;
                _position = toPos;
            }

            if (idx.TryGetResult<Vector3>(_scatterIdFromPos, out var fromPos))
            {
                fromPos.Y += 0.175f;
                _fromPosition = fromPos;
            }

            if (string.IsNullOrEmpty(Name))
                Name = ResolveName();
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
