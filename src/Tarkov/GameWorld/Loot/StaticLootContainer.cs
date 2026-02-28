using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.Common.Maps;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Common.Unity;

namespace eft_dma_radar.Tarkov.Loot
{
    public sealed class StaticLootContainer : LootContainer
    {
        private static readonly IReadOnlyList<LootItem> _defaultLoot = new List<LootItem>(1);
        public static EntityTypeSettings Settings => Program.Config.EntityTypeSettings.GetSettings("StaticContainer");
        public static EntityTypeSettingsESP ESPSettings => ESP.Config.EntityTypeESPSettings.GetSettings("StaticContainer");
        private const float HEIGHT_INDICATOR_THRESHOLD = 1.85f;

        public override string Name { get; } = "Container";
        public override string ID { get; }
        public ulong GameObject { get; set; }
        /// <summary>
        /// True if the container has been searched by LocalPlayer or another Networked Entity.
        /// </summary>
        public bool Searched { get; }

        public StaticLootContainer(string containerId, bool opened) : base(_defaultLoot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(containerId, nameof(containerId));
            this.ID = containerId;
            this.Searched = opened;

            if (EftDataManager.AllContainers.TryGetValue(containerId, out var container))
                this.Name = container.ShortName ?? "Container";
        }

        public override void Draw(SKCanvas canvas, XMMapParams mapParams, ILocalPlayer localPlayer)
        {
            var dist = Vector3.Distance(localPlayer.Position, Position);

            if (dist > Settings.RenderDistance)
                return;

            var heightDiff = Position.Y - localPlayer.Position.Y;
            var point = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            MouseoverPosition = new Vector2(point.X, point.Y);
            SKPaints.ShapeOutline.StrokeWidth = 2f;

            float distanceYOffset;
            float nameXOffset = 7f * MainWindow.UIScale;
            float nameYOffset;

            if (heightDiff > HEIGHT_INDICATOR_THRESHOLD)
            {
                using var path = point.GetUpArrow(4);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, SKPaints.PaintContainerLoot);
                distanceYOffset = 18f * MainWindow.UIScale;
                nameYOffset = 6f * MainWindow.UIScale;
            }
            else if (heightDiff < -HEIGHT_INDICATOR_THRESHOLD)
            {
                using var path = point.GetDownArrow(4);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, SKPaints.PaintContainerLoot);
                distanceYOffset = 12f * MainWindow.UIScale;
                nameYOffset = 1f * MainWindow.UIScale;
            }
            else
            {
                var size = 4 * MainWindow.UIScale;
                canvas.DrawCircle(point, size, SKPaints.ShapeOutline);
                canvas.DrawCircle(point, size, SKPaints.PaintContainerLoot);
                distanceYOffset = 16f * MainWindow.UIScale;
                nameYOffset = 4f * MainWindow.UIScale;
            }

            if (Settings.ShowName)
            {
                var namePoint = point;
                namePoint.Offset(nameXOffset, nameYOffset);
                canvas.DrawText(Name, namePoint, SKPaints.TextOutline);
                canvas.DrawText(Name, namePoint, SKPaints.TextContainer);
            }

            if (Settings.ShowDistance)
            {
                var distText = $"{(int)dist}m";
                var distWidth = SKPaints.TextContainer.MeasureText($"{(int)dist}");
                var distPoint = new SKPoint(
                    point.X - (distWidth / 2),
                    point.Y + distanceYOffset
                );
                canvas.DrawText(distText, distPoint, SKPaints.TextOutline);
                canvas.DrawText(distText, distPoint, SKPaints.TextContainer);
            }
        }

        public override void DrawESP(SKCanvas canvas, LocalPlayer localPlayer)
        {
            var dist = Vector3.Distance(localPlayer.Position, Position);
            
            if (dist > ESPSettings.RenderDistance)
                return;
            
            if (!CameraManagerBase.WorldToScreen(ref Position, out var scrPos))
                return;

            var scale = ESP.Config.FontScale;

            switch (ESPSettings.RenderMode)
            {
                case EntityRenderMode.None:
                    break;

                case EntityRenderMode.Dot:
                    var dotSize = 3f * scale;
                    canvas.DrawCircle(scrPos.X, scrPos.Y, dotSize, SKPaints.PaintContainerLootESP);
                    break;

                case EntityRenderMode.Cross:
                    var crossSize = 5f * scale;

                    using (var thickPaint = new SKPaint
                    {
                        Color = SKPaints.PaintContainerLootESP.Color,
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
                        Color = SKPaints.PaintContainerLootESP.Color,
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
                default:
                    var boxHalf = 3f * scale;
                    var boxPt = new SKRect(
                        scrPos.X - boxHalf, scrPos.Y - boxHalf,
                        scrPos.X + boxHalf, scrPos.Y + boxHalf);
                    canvas.DrawRect(boxPt, SKPaints.PaintContainerLootESP);
                    break;

                case EntityRenderMode.Diamond:
                    var diamondSize = 3.5f * scale;
                    using (var diamondPath = new SKPath())
                    {
                        diamondPath.MoveTo(scrPos.X, scrPos.Y - diamondSize);
                        diamondPath.LineTo(scrPos.X + diamondSize, scrPos.Y);
                        diamondPath.LineTo(scrPos.X, scrPos.Y + diamondSize);
                        diamondPath.LineTo(scrPos.X - diamondSize, scrPos.Y);
                        diamondPath.Close();
                        canvas.DrawPath(diamondPath, SKPaints.PaintContainerLootESP);
                    }
                    break;
            }

            if (ESPSettings.ShowName || ESPSettings.ShowDistance)
            {
                var textY = scrPos.Y + 16f * scale;
                var textPt = new SKPoint(scrPos.X, textY);
                textPt.DrawESPText(
                    canvas,
                    this,
                    localPlayer,
                    ESPSettings.ShowDistance,
                    SKPaints.TextContainerLootESP,
                    ESPSettings.ShowName ? this.Name : null);
            }
        }

        public override void DrawMouseover(SKCanvas canvas, XMMapParams mapParams, LocalPlayer localPlayer)
        {
            var lines = new List<string>()
            {
                this.Name
            };
            Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams).DrawMouseoverText(canvas, lines);
        }
    }
}