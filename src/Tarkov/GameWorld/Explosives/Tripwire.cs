using System;
using System.Collections.Generic;
using System.Numerics;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.Common.Maps;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Tarkov.EFTPlayer;
using SDK;
using static SDK.Offsets;
using SkiaSharp;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.ESP;

namespace eft_dma_radar.Tarkov.GameWorld.Explosives
{
    public sealed class Tripwire : IExplosiveItem, IWorldEntity, IMapEntity, IESPEntity
    {
        private static void Log(string msg) =>
            XMLogging.WriteLine($"[TRIPWIRE] {msg}");

        public static implicit operator ulong(Tripwire x) => x.Addr;

        public ulong Addr { get; }

        public bool IsActive { get; private set; }
        public string Name { get; private set; }

        public static EntityTypeSettings Settings =>
            Program.Config.EntityTypeSettings.GetSettings("Tripwire");

        public static EntityTypeSettingsESP ESPSettings =>
            ESP.Config.EntityTypeESPSettings.GetSettings("Tripwire");

        // scatter IDs per tripwire
        private static int _nextScatterBaseId = 300_000;
        private readonly int _scatterIdState;
        private readonly int _scatterIdToPos;
        private readonly int _scatterIdFromPos;

        public Tripwire(ulong baseAddr)
        {
            Addr = baseAddr;
            //Log($"Created Tripwire @ 0x{Addr:X}");

            var baseId = System.Threading.Interlocked.Add(ref _nextScatterBaseId, 3) - 3;
            _scatterIdState   = baseId;
            _scatterIdToPos   = baseId + 1;
            _scatterIdFromPos = baseId + 2;

            IsActive      = GetIsTripwireActive(false);
            _position     = GetPosition(false);
            _fromPosition = GetFromPosition(false);
            Name          = GetName();

            //Log($"Initial: IsActive={IsActive}, Pos={_position}, FromPos={_fromPosition}, Name='{Name}'");
        }

        // ─────────────────────────────────────────────────────
        // Slow-path fallback (direct DMA)
        // ─────────────────────────────────────────────────────
        public void Refresh()
        {
            bool prevActive = IsActive;
            IsActive = GetIsTripwireActive();

            if (!IsActive)
                return;

            _position = GetPosition();
            _fromPosition = GetFromPosition();
        }

        private bool GetIsTripwireActive(bool useCache = true)
        {
            var state = (Enums.ETripwireState)Memory.ReadValue<int>(
                this + TripwireSynchronizableObject._tripwireState, useCache);

            bool result = state is Enums.ETripwireState.Wait or Enums.ETripwireState.Active;
            return result;
        }

        private Vector3 GetPosition(bool useCache = true)
        {
            var pos = Memory.ReadValue<Vector3>(
                this + TripwireSynchronizableObject.ToPosition, useCache);
            pos.Y += 0.175f;
            return pos;
        }

        private Vector3 GetFromPosition(bool useCache = true)
        {
            var pos = Memory.ReadValue<Vector3>(
                this + TripwireSynchronizableObject.FromPosition, useCache);
            pos.Y += 0.175f;
            return pos;
        }

        private string GetName()
        {
            if (!IsActive)
                return "";

            var id = Memory.ReadValue<Types.MongoID>(this + TripwireSynchronizableObject.GrenadeTemplateId);
            var name = Memory.ReadUnityString(id.StringID, useCache: false);

            if (EftDataManager.AllItems.TryGetValue(name, out var item))
            {
                var resultName = item.ShortName;

                if (item.BsgId == "67b49e7335dec48e3e05e057")
                    resultName = $"{resultName} (SHORT)";

                return resultName;
            }

            return "Tripwire";
        }

        // ─────────────────────────────────────────────────────
        // Scatter: queue reads
        // ─────────────────────────────────────────────────────
        public void QueueScatterReads(ScatterReadIndex idx)
        {
            // Always read state
            idx.AddEntry<int>(
                id: _scatterIdState,
                address: Addr + TripwireSynchronizableObject._tripwireState
            );

            // Positions if previously active (no point reading coords for inactive)
            if (IsActive)
            {
                idx.AddEntry<Vector3>(
                    id: _scatterIdToPos,
                    address: Addr + TripwireSynchronizableObject.ToPosition
                );

                idx.AddEntry<Vector3>(
                    id: _scatterIdFromPos,
                    address: Addr + TripwireSynchronizableObject.FromPosition
                );
            }
        }

        // ─────────────────────────────────────────────────────
        // Scatter: apply results
        // ─────────────────────────────────────────────────────
        public void OnRefresh(ScatterReadIndex idx)
        {
            if (idx.TryGetResult<int>(_scatterIdState, out var stateVal))
            {
                var state = (Enums.ETripwireState)stateVal;
                var wasActive = IsActive;
                IsActive = state is Enums.ETripwireState.Wait or Enums.ETripwireState.Active;

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
                Name = GetName();
        }


        private List<SKPoint> GetTripwireLine()
        {
            var toPos = Position;
            var fromPos = FromPosition;

            if (!CameraManagerBase.WorldToScreen(ref toPos, out var toScreen))
                return null;
            if (!CameraManagerBase.WorldToScreen(ref fromPos, out var fromScreen))
                return null;

            return new List<SKPoint> { toScreen, fromScreen };
        }

        #region Interfaces

        private Vector3 _position;
        private Vector3 _fromPosition;

        public ref Vector3 Position => ref _position;
        public ref Vector3 FromPosition => ref _fromPosition;

        public void Draw(SKCanvas canvas, XMMapParams mapParams, ILocalPlayer localPlayer)
        {
            if (!IsActive)
                return;

            var dist = Vector3.Distance(localPlayer.Position, Position);
            if (dist > Settings.RenderDistance)
                return;

            var toPos = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            var fromPos = FromPosition.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);

            var size = 5 * MainWindow.UIScale;
            var lineWidth = 2f * MainWindow.UIScale;

            SKPaints.PaintExplosives.StrokeWidth = lineWidth;

            if (Settings.ShowTripwireLine)
            {
                SKPaints.ShapeOutline.StrokeWidth = lineWidth + 1f * MainWindow.UIScale;
                canvas.DrawLine(fromPos, toPos, SKPaints.ShapeOutline);
                canvas.DrawLine(fromPos, toPos, SKPaints.PaintExplosives);
            }

            canvas.DrawCircle(toPos, size, SKPaints.ShapeOutline);
            canvas.DrawCircle(toPos, size, SKPaints.PaintExplosives);

            if (Settings.ShowTripwireLine)
            {
                canvas.DrawCircle(fromPos, size, SKPaints.ShapeOutline);
                canvas.DrawCircle(fromPos, size, SKPaints.PaintExplosives);
            }

            if (Settings.ShowName && !string.IsNullOrEmpty(Name))
            {
                var nameWidth = SKPaints.TextExplosives.MeasureText(Name);
                var namePt = new SKPoint(
                    toPos.X - (nameWidth / 2),
                    toPos.Y - 10f * MainWindow.UIScale);

                canvas.DrawText(Name, namePt, SKPaints.TextOutline);
                canvas.DrawText(Name, namePt, SKPaints.TextExplosives);
            }

            if (Settings.ShowDistance)
            {
                var distText = $"{(int)dist}m";
                var distWidth = SKPaints.TextExplosives.MeasureText(distText);
                var distPt = new SKPoint(
                    toPos.X - (distWidth / 2),
                    toPos.Y + 18f * MainWindow.UIScale);

                canvas.DrawText(distText, distPt, SKPaints.TextOutline);
                canvas.DrawText(distText, distPt, SKPaints.TextExplosives);
            }
        }

        public void DrawESP(SKCanvas canvas, LocalPlayer localPlayer)
        {
            if (!IsActive)
                return;

            var dist = Vector3.Distance(localPlayer.Position, Position);
            if (dist > ESPSettings.RenderDistance)
                return;

            var pos = Position;
            if (!CameraManagerBase.WorldToScreen(ref pos, out var scrPos))
                return;

            var scale = ESP.Config.FontScale;

            if (ESPSettings.ShowTripwireLine)
            {
                var line = GetTripwireLine();
                if (line != null)
                {
                    SKPaints.PaintExplosiveESP.StrokeWidth = 2f * ESP.Config.LineScale;
                    canvas.DrawLine(line[0], line[1], SKPaints.PaintExplosiveESP);
                }
            }

            var dot = 3f * scale;
            canvas.DrawCircle(scrPos.X, scrPos.Y, dot, SKPaints.PaintExplosiveESP);

            if (ESPSettings.ShowName || ESPSettings.ShowDistance)
            {
                var textPt = new SKPoint(scrPos.X, scrPos.Y + 16f * scale);

                textPt.DrawESPText(
                    canvas,
                    this,
                    localPlayer,
                    ESPSettings.ShowDistance,
                    SKPaints.TextExplosiveESP,
                    ESPSettings.ShowName ? Name : null
                );
            }
        }

        #endregion
    }
}
