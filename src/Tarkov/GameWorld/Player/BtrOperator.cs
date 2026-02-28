using System.Numerics;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.Maps;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Misc;
using SkiaSharp;

namespace eft_dma_radar.Tarkov.EFTPlayer
{
    /// <summary>
    /// BTR Bot Operator / Vehicle Entity.
    /// Self-contained ESP + Map rendering.
    /// </summary>
    public sealed class BtrOperator : ObservedPlayer
    {
        private readonly ulong _btrView;
        private Vector3 _position;
        public static EntityTypeSettingsESP ESPSettings => ESP.Config.EntityTypeESPSettings.GetSettings("BTR");
        public static EntityTypeSettings Settings =>
            Program.Config.EntityTypeSettings.GetSettings("BTR");        
        // ─────────────────────────────────────────────
        // Identity overrides
        // ─────────────────────────────────────────────

        public override ref Vector3 Position => ref _position;

        public override string Name
        {
            get => "BTR";
            set { /* ignored */ }
        }

        public new PlayerType Type => PlayerType.AIRaider;

        // ─────────────────────────────────────────────
        // Construction
        // ─────────────────────────────────────────────

        public BtrOperator(ulong btrView, ulong playerBase)
            : base(playerBase)
        {
            _btrView = btrView;

            // Hard force — never allow reassignment
            UpdatePlayerType(PlayerType.AIRaider);
        }

        // ─────────────────────────────────────────────
        // Realtime update (position only)
        // ─────────────────────────────────────────────

        public override void OnRealtimeLoop(ScatterReadIndex index)
        {
            // Unique index space, no collision with players
            index.AddEntry<Vector3>(0, _btrView + Offsets.BTRView._previousPosition);

            index.Callbacks += x =>
            {
                if (x.TryGetResult<Vector3>(0, out var pos))
                    _position = pos;
            };
        }

        // ─────────────────────────────────────────────
        // ESP DRAW (screen space)
        // ─────────────────────────────────────────────

        public void DrawESP(SKCanvas canvas, LocalPlayer localPlayer)
        {
            var dist = Vector3.Distance(localPlayer.Position, _position);
            if (dist > ESPSettings.RenderDistance)
                return;

            if (!CameraManagerBase.WorldToScreen(ref _position, out var scrPos))
                return;

            var paint = GetESPPaints();
            var scale = ESP.Config.FontScale;

            switch (ESPSettings.RenderMode)
            {
                case EntityRenderMode.None:
                    break;

                case EntityRenderMode.Dot:
                    var dotSize = 3f * scale;
                    canvas.DrawCircle(scrPos.X, scrPos.Y, dotSize, paint.Item1);
                    break;

                case EntityRenderMode.Cross:
                    var crossSize = 5f * scale;

                    using (var thickPaint = new SKPaint
                    {
                        Color = paint.Item1.Color,
                        StrokeWidth = 1.5f * scale,
                        IsAntialias = true,
                        Style = SKPaintStyle.Stroke
                    })
                    {
                        canvas.DrawLine(
                            scrPos.X - crossSize, scrPos.Y - crossSize,
                            scrPos.X + crossSize, scrPos.Y + crossSize,
                            thickPaint);
                        canvas.DrawLine(
                            scrPos.X - crossSize, scrPos.Y + crossSize,
                            scrPos.X + crossSize, scrPos.Y - crossSize,
                            thickPaint);
                    }
                    break;

                case EntityRenderMode.Plus:
                    var plusSize = 5f * scale;

                    using (var thickPaint = new SKPaint
                    {
                        Color = paint.Item1.Color,
                        StrokeWidth = 1.5f * scale,
                        IsAntialias = true,
                        Style = SKPaintStyle.Stroke
                    })
                    {
                        canvas.DrawLine(
                            scrPos.X, scrPos.Y - plusSize,
                            scrPos.X, scrPos.Y + plusSize,
                            thickPaint);
                        canvas.DrawLine(
                            scrPos.X - plusSize, scrPos.Y,
                            scrPos.X + plusSize, scrPos.Y,
                            thickPaint);
                    }
                    break;

                case EntityRenderMode.Square:
                    var boxHalf = 3f * scale;
                    var boxPt = new SKRect(
                        scrPos.X - boxHalf, scrPos.Y - boxHalf,
                        scrPos.X + boxHalf, scrPos.Y + boxHalf);
                    canvas.DrawRect(boxPt, paint.Item1);
                    break;

                case EntityRenderMode.Diamond:
                default:
                    var diamondSize = 3.5f * scale;
                    using (var diamondPath = new SKPath())
                    {
                        diamondPath.MoveTo(scrPos.X, scrPos.Y - diamondSize);
                        diamondPath.LineTo(scrPos.X + diamondSize, scrPos.Y);
                        diamondPath.LineTo(scrPos.X, scrPos.Y + diamondSize);
                        diamondPath.LineTo(scrPos.X - diamondSize, scrPos.Y);
                        diamondPath.Close();
                        canvas.DrawPath(diamondPath, paint.Item1);
                    }
                    break;
            }

            if (ESPSettings.ShowName || ESPSettings.ShowDistance)
            {
                scrPos.DrawESPText(
                    canvas,
                    this,
                    localPlayer,
                    ESPSettings.ShowDistance,
                    paint.Item2,
                    ESPSettings.ShowName ? "BTR" : null
                );
            }
        }

        // ─────────────────────────────────────────────
        // MAP DRAW (radar / minimap)
        // ─────────────────────────────────────────────

        public void Draw(
            SKCanvas canvas,
            XMMapParams mapParams,
            ILocalPlayer localPlayer)
        {
            if (_position == Vector3.Zero)
                return;
        
            var dist = Vector3.Distance(localPlayer.Position, _position);
            if (dist > Settings.RenderDistance)
                return;
        
            var heightDiff = _position.Y - localPlayer.Position.Y;
            var point = _position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            MouseoverPosition = new Vector2(point.X, point.Y);
        
            var paints = GetPaints();
        
            SKPaints.ShapeOutline.StrokeWidth = 1f;
        
            float distanceYOffset;
            float nameYOffset;
        
            if (heightDiff > 1.85f)
            {
                using var path = point.GetUpArrow(5f);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paints.Item1);
                distanceYOffset = 18f * MainWindow.UIScale;
                nameYOffset = 6f * MainWindow.UIScale;
            }
            else if (heightDiff < -1.85f)
            {
                using var path = point.GetDownArrow(5f);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paints.Item1);
                distanceYOffset = 12f * MainWindow.UIScale;
                nameYOffset = 1f * MainWindow.UIScale;
            }
            else
            {
                canvas.DrawText("■", point, SKPaints.TextOutline);
                canvas.DrawText("■", point, paints.Item2);
                distanceYOffset = 12f * MainWindow.UIScale;
                nameYOffset = 0f;
            }
        
            if (Settings.ShowName)
            {
                var namePoint = point;
                namePoint.Offset(7f * MainWindow.UIScale, nameYOffset);
                canvas.DrawText("BTR", namePoint, SKPaints.TextOutline);
                canvas.DrawText("BTR", namePoint, paints.Item2);
            }
        
            if (Settings.ShowDistance)
            {
                var distText = $"{(int)dist}m";
                var width = paints.Item2.MeasureText(distText);
                var distPoint = new SKPoint(
                    point.X - (width / 2),
                    point.Y + distanceYOffset);
        
                canvas.DrawText(distText, distPoint, SKPaints.TextOutline);
                canvas.DrawText(distText, distPoint, paints.Item2);
            }
        }
        private ValueTuple<SKPaint, SKPaint> GetPaints()
        {
            return new(
                SKPaints.PaintRaider,
                SKPaints.TextRaider
            );
        }

        private ValueTuple<SKPaint, SKPaint> GetESPPaints()
        {
            return new(
                SKPaints.PaintRaiderESP,
                SKPaints.TextRaiderESP
            );
        }

    }
}
