namespace eft_dma_radar.Silk.Tarkov.GameWorld.Explosives
{
    /// <summary>
    /// Tracks the BTR vehicle position and renders it on the radar.
    /// The BTR only spawns on Streets and Woods maps.
    /// Position is read from BTRView._previousPosition via BtrController.
    /// </summary>
    internal sealed class BtrTracker
    {
        private readonly ulong _localGameWorld;
        private ulong _btrView;
        private Vector3 _position;
        private bool _initialized;

        /// <summary>BTR world position (updated per-tick).</summary>
        public Vector3 Position => _position;

        /// <summary>True if the BTR has been found and is being tracked.</summary>
        public bool IsActive => _initialized && _position != Vector3.Zero;

        public BtrTracker(ulong localGameWorld)
        {
            _localGameWorld = localGameWorld;
        }

        /// <summary>
        /// Attempts to resolve the BTR and update its position.
        /// Called each tick from the explosives/secondary worker.
        /// </summary>
        public void Refresh()
        {
            try
            {
                if (!_initialized)
                {
                    if (!TryResolveBtrView())
                        return;
                    _initialized = true;
                    Log.WriteLine($"[BTR] BTR vehicle found — BtrView @ 0x{_btrView:X}");
                }

                _position = Memory.ReadValue<Vector3>(_btrView + Offsets.BTRView._previousPosition, false);

                // Validate position — zero or extreme values indicate invalid data
                if (!float.IsFinite(_position.X) || !float.IsFinite(_position.Y) || !float.IsFinite(_position.Z))
                    _position = Vector3.Zero;
            }
            catch
            {
                // BTR may not exist yet or data is invalid — silently ignore
                _position = Vector3.Zero;
                _initialized = false;
                _btrView = 0;
            }
        }

        /// <summary>
        /// Draws the BTR on the radar as a large orange/raider-colored marker.
        /// </summary>
        public void Draw(SKCanvas canvas, MapParams mapParams, MapConfig mapCfg, Player.Player localPlayer)
        {
            if (!IsActive)
                return;

            var dist = Vector3.Distance(localPlayer.Position, _position);
            var point = mapParams.ToScreenPos(MapParams.ToMapPos(_position, mapCfg));

            // Draw BTR marker — large circle with border
            const float size = 8f;
            canvas.DrawCircle(point, size, SKPaints.ShapeBorder);
            canvas.DrawCircle(point, size, SKPaints.PaintBtr);

            // "BTR" label
            const string label = "BTR";
            var labelWidth = SKPaints.FontRegular11.MeasureText(label, SKPaints.TextBtr);
            var labelPt = new SKPoint(point.X - labelWidth / 2f, point.Y - 12f);
            canvas.DrawText(label, labelPt, SKTextAlign.Left, SKPaints.FontRegular11, SKPaints.TextShadow);
            canvas.DrawText(label, labelPt, SKTextAlign.Left, SKPaints.FontRegular11, SKPaints.TextBtr);

            // Distance label
            var distText = $"{(int)dist}m";
            var distWidth = SKPaints.FontRegular11.MeasureText(distText, SKPaints.TextBtr);
            var distPt = new SKPoint(point.X - distWidth / 2f, point.Y + 18f);
            canvas.DrawText(distText, distPt, SKTextAlign.Left, SKPaints.FontRegular11, SKPaints.TextShadow);
            canvas.DrawText(distText, distPt, SKTextAlign.Left, SKPaints.FontRegular11, SKPaints.TextBtr);
        }

        private bool TryResolveBtrView()
        {
            if (!Memory.TryReadPtr(_localGameWorld + Offsets.ClientLocalGameWorld.BtrController, out var btrController, false)
                || btrController == 0)
                return false;

            if (!Memory.TryReadPtr(btrController + Offsets.BtrController.BtrView, out var btrView, false)
                || btrView == 0)
                return false;

            _btrView = btrView;
            return true;
        }
    }
}
