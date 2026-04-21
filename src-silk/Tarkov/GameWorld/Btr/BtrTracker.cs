namespace eft_dma_radar.Silk.Tarkov.GameWorld.Btr
{
    /// <summary>
    /// Tracks the BTR vehicle position and renders it on the radar.
    /// The BTR only spawns on Streets and Woods maps.
    /// Position is read from BTRView._previousPosition via BtrController.
    ///
    /// <para>
    /// <b>Update model:</b> Resolution of the BtrView pointer is slow/rare and runs on the
    /// explosives worker (~100ms tick) via <see cref="Refresh"/>. Position / state / gunner
    /// reads are fast and run on the realtime worker (~8ms tick) via
    /// <see cref="UpdatePosition"/> so the BTR moves at the same sample rate as players —
    /// no visible jitter on radar or ESP.
    /// </para>
    /// <para>
    /// <b>Failure handling:</b> Transient DMA failures keep the last valid position so
    /// the marker does not flicker. Only after <see cref="MaxConsecutiveFailures"/>
    /// consecutive failures does the tracker invalidate its pointer and attempt to
    /// re-resolve on the next explosives tick.
    /// </para>
    /// <para>
    /// <b>Passenger snapping:</b> <see cref="TrySnapPassengerXZ"/> snaps the horizontal
    /// position of any player standing/sitting on the BTR to the BTR's own XZ. This
    /// removes jitter caused by passenger transforms being sampled slightly out-of-phase
    /// with the vehicle transform. The turret gunner is additionally identified directly
    /// via <see cref="GunnerPtr"/> (<c>BTRTurretView._bot</c>), which is an exact
    /// <c>ObservedPlayerView</c> pointer match.
    /// </para>
    /// </summary>
    internal sealed class BtrTracker
    {
        private const int MaxConsecutiveFailures = 10;

        /// <summary>Horizontal radius (meters) within which a player is considered to be on the BTR.</summary>
        private const float PassengerXZRadius = 3.0f;
        private const float PassengerXZRadiusSq = PassengerXZRadius * PassengerXZRadius;

        /// <summary>Vertical window (meters) around the BTR in which a player can be considered a passenger.</summary>
        private const float PassengerYBelow = 1.0f;
        private const float PassengerYAbove = 3.5f;

        private readonly ulong _localGameWorld;
        private ulong _btrController;
        private ulong _btrView;
        private ulong _btrTurretView;
        private Vector3 _position;
        private float _currentSpeed;
        private byte _state;
        private byte _routeState;
        private int _timeToEndPauseMs;
        private bool _isPaid;
        private ulong _gunnerPtr;
        private float _turretYawDeg;
        private bool _initialized;
        private bool _hasValidPosition;
        private int _failureCount;

        /// <summary>BTR world position (last known valid value).</summary>
        public Vector3 Position => _position;

        /// <summary>Current BTR speed in m/s (from <c>BTRView.CurrentSpeed</c>).</summary>
        public float CurrentSpeed => _currentSpeed;

        /// <summary>True while the BTR is driving (any non-zero speed over a small threshold).</summary>
        public bool IsMoving => _currentSpeed > 0.1f;

        /// <summary>Raw <c>EBtrState</c> byte from <c>BTRView._btrState</c>.</summary>
        public byte State => _state;

        /// <summary>Raw <c>EBtrRouteState</c> byte from <c>BTRView.RouteState</c> (approach / at-stop / leaving).</summary>
        public byte RouteState => _routeState;

        /// <summary>
        /// Remaining pause time (milliseconds) at the current passenger stop,
        /// counted down live by <c>BTRView._timeToEndPause</c>. 0 when not paused.
        /// </summary>
        public int TimeToEndPauseMs => _timeToEndPauseMs;

        /// <summary>True when a player has paid for the BTR taxi service this raid (<c>BtrController.IsBtrPaid</c>).</summary>
        public bool IsPaid => _isPaid;

        /// <summary>
        /// Pointer to the turret gunner's <c>ObservedPlayerView</c>, or 0 if no gunner.
        /// Source: <c>BTRView.turret (0x60) → BTRTurretView._bot (0x60)</c>.
        /// </summary>
        public ulong GunnerPtr => _gunnerPtr;

        /// <summary>
        /// Current turret yaw in world degrees (0..360). Source: <c>BTRTurretView._targetTurretRotate</c>.
        /// Returns 0 if no turret has been resolved yet.
        /// </summary>
        public float TurretYawDeg => _turretYawDeg;

        /// <summary>True if the BTR has been found and has a valid last-known position.</summary>
        public bool IsActive => _initialized && _hasValidPosition;

        public BtrTracker(ulong localGameWorld)
        {
            _localGameWorld = localGameWorld;
        }

        /// <summary>
        /// Slow resolution tick — runs on the explosives worker (~100ms).
        /// Resolves the BtrView + turret pointers if not yet known.
        /// </summary>
        public void Refresh()
        {
            if (_initialized)
                return;

            try
            {
                if (!TryResolveBtrView())
                    return;

                // Cache turret once; the reference rarely changes for the life of the raid.
                if (Memory.TryReadPtr(_btrView + Offsets.BTRView.turret, out var turret, false) && turret != 0)
                    _btrTurretView = turret;

                _initialized = true;
                _failureCount = 0;
                Log.WriteLine($"[BTR] BTR vehicle found — BtrView @ 0x{_btrView:X}, Turret @ 0x{_btrTurretView:X}");
            }
            catch
            {
                // BTR may not exist yet on this map — silently retry next tick
            }
        }

        /// <summary>
        /// Fast update — runs on the realtime worker (~8ms). Reads position, speed, state,
        /// and the turret gunner pointer. Keeps the last valid values on transient failure.
        /// </summary>
        public void UpdatePosition()
        {
            if (!_initialized)
                return;

            if (!Memory.TryReadValue<Vector3>(_btrView + Offsets.BTRView._previousPosition, out var pos, false))
            {
                OnReadFailure();
                return;
            }

            if (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y) || !float.IsFinite(pos.Z))
            {
                OnReadFailure();
                return;
            }

            _position = pos;
            _hasValidPosition = true;
            _failureCount = 0;

            // Cheap auxiliary reads — failures are non-fatal.
            if (Memory.TryReadValue<float>(_btrView + Offsets.BTRView.CurrentSpeed, out var spd, false)
                && float.IsFinite(spd))
                _currentSpeed = spd;

            if (Memory.TryReadValue<byte>(_btrView + Offsets.BTRView._btrState, out var st, false))
                _state = st;

            if (Memory.TryReadValue<byte>(_btrView + Offsets.BTRView.RouteState, out var rs, false))
                _routeState = rs;

            if (Memory.TryReadValue<int>(_btrView + Offsets.BTRView._timeToEndPause, out var ttp, false)
                && ttp >= 0 && ttp < 600_000) // sanity clamp (<10min)
                _timeToEndPauseMs = ttp;

            if (_btrController != 0
                && Memory.TryReadValue<byte>(_btrController + Offsets.BtrController.IsBtrPaid, out var paid, false))
            {
                _isPaid = paid != 0;
            }

            if (_btrTurretView != 0
                && Memory.TryReadPtr(_btrTurretView + Offsets.BTRTurretView.Bot, out var gunner, false))
            {
                _gunnerPtr = gunner.IsValidVirtualAddress() ? gunner : 0;
            }

            if (_btrTurretView != 0
                && Memory.TryReadValue<float>(_btrTurretView + Offsets.BTRTurretView.TargetTurretRotate, out var yaw, false)
                && float.IsFinite(yaw))
            {
                _turretYawDeg = yaw;
            }
        }

        /// <summary>
        /// Returns true if <paramref name="observedPlayerViewPtr"/> is the current BTR turret gunner.
        /// This is an authoritative identity match (not a proximity heuristic).
        /// </summary>
        public bool IsGunner(ulong observedPlayerViewPtr) =>
            _gunnerPtr != 0 && observedPlayerViewPtr == _gunnerPtr;

        /// <summary>
        /// If <paramref name="worldPos"/> lies within the BTR's passenger envelope, snaps
        /// its X/Z to the BTR's current X/Z (keeping the original Y). This removes jitter
        /// for the BTR turret operator / "scav on top" whose transform is sampled slightly
        /// out-of-phase with the vehicle itself.
        /// Returns true if a snap occurred.
        /// </summary>
        public bool TrySnapPassengerXZ(ref Vector3 worldPos)
        {
            if (!IsActive)
                return false;

            float dy = worldPos.Y - _position.Y;
            if (dy < -PassengerYBelow || dy > PassengerYAbove)
                return false;

            float dx = worldPos.X - _position.X;
            float dz = worldPos.Z - _position.Z;
            if (dx * dx + dz * dz > PassengerXZRadiusSq)
                return false;

            worldPos.X = _position.X;
            worldPos.Z = _position.Z;
            return true;
        }

        private void OnReadFailure()
        {
            if (++_failureCount < MaxConsecutiveFailures)
                return;

            // Pointer likely stale — force re-resolve on next explosives tick.
            _initialized = false;
            _hasValidPosition = false;
            _btrController = 0;
            _btrView = 0;
            _btrTurretView = 0;
            _gunnerPtr = 0;
            _currentSpeed = 0f;
            _state = 0;
            _routeState = 0;
            _timeToEndPauseMs = 0;
            _isPaid = false;
            _turretYawDeg = 0f;
            _failureCount = 0;
            _position = Vector3.Zero;
            Unity.IL2CPP.BtrControllerResolver.InvalidateCache();
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

            const float size = 8f;
            canvas.DrawCircle(point, size, SKPaints.ShapeBorder);
            canvas.DrawCircle(point, size, SKPaints.PaintBtr);

            // Turret aimline — short line pointing where the BTR gun is aimed.
            // Unity Y-yaw is clockwise from +Z; convert to 2D screen (X right, Y down):
            //   dx =  sin(yaw), dz = cos(yaw)  → on map, Y axis is inverted.
            if (_btrTurretView != 0)
            {
                const float lineLen = 28f;
                float rad = _turretYawDeg * (MathF.PI / 180f);
                float dx = MathF.Sin(rad);
                float dz = MathF.Cos(rad);
                // Map-space direction: +X right, -Z up in EFT → screen Y is inverted.
                var tip = new SKPoint(point.X + dx * lineLen, point.Y - dz * lineLen);
                canvas.DrawLine(point, tip, SKPaints.PaintBtr);
            }

            // "BTR" label. When stopped, show the remaining pause countdown if we have
            // one (from BTRView._timeToEndPause); otherwise just "idle". Append "$" when
            // a player has paid for the taxi service this raid (useful on Streets/Woods).
            string label;
            if (IsMoving)
            {
                label = "BTR";
            }
            else if (_timeToEndPauseMs > 0)
            {
                int secs = (_timeToEndPauseMs + 999) / 1000;
                label = $"BTR ({secs}s)";
            }
            else
            {
                label = "BTR (idle)";
            }
            if (_isPaid)
                label += " $";
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
            // Preferred path: resolve BtrController directly from its IL2CPP singleton
            // (<Instance>k__BackingField via TypeInfoTable + StaticFields). This avoids
            // an extra LocalGameWorld dereference and is stable across the raid.
            var btrController = Unity.IL2CPP.BtrControllerResolver.GetInstance();

            // Fallback: legacy chain via ClientLocalGameWorld.BtrController — kept so
            // the BTR still works on builds where the TypeIndex hasn't been dumped yet.
            if (!btrController.IsValidVirtualAddress())
            {
                if (!Memory.TryReadPtr(_localGameWorld + Offsets.ClientLocalGameWorld.BtrController, out btrController, false)
                    || btrController == 0)
                    return false;
            }

            if (!Memory.TryReadPtr(btrController + Offsets.BtrController.BtrView, out var btrView, false)
                || btrView == 0)
                return false;

            _btrController = btrController;
            _btrView = btrView;
            return true;
        }
    }
}
