using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.Common.Maps;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Common.Unity;
using System.Xml.Linq;
using static HandyControl.Tools.Interop.InteropValues;
using eft_dma_radar.Common.DMA.ScatterAPI;

namespace eft_dma_radar.Tarkov.GameWorld.Explosives
{
    public sealed class MortarProjectile : IExplosiveItem
    {
        public static EntityTypeSettings Settings => Program.Config.EntityTypeSettings.GetSettings("MortarProjectile");
        public static EntityTypeSettingsESP ESPSettings => ESP.Config.EntityTypeESPSettings.GetSettings("MortarProjectile");

        public static implicit operator ulong(MortarProjectile x) => x.Addr;
        private readonly ConcurrentDictionary<ulong, IExplosiveItem> _parent;

        public MortarProjectile(ulong baseAddr, ConcurrentDictionary<ulong, IExplosiveItem> parent)
        {
            _parent = parent;
            this.Addr = baseAddr;
            this.Refresh();
            if (!this.IsActive)
            {
                throw new Exception("Already exploded!");
            }
        }

        public ulong Addr { get; }

        public bool IsActive { get; private set; }

        private Vector3 _position;
        public ref Vector3 Position => ref _position;

        public void Draw(SKCanvas canvas, XMMapParams mapParams, ILocalPlayer localPlayer)
        {
            if (!IsActive)
                return;

            var dist = Vector3.Distance(localPlayer.Position, Position);
            if (dist > Settings.RenderDistance)
                return;

            var point = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);

            SKPaints.ShapeOutline.StrokeWidth = SKPaints.PaintExplosives.StrokeWidth + 2f * MainWindow.UIScale;

            var size = 5 * MainWindow.UIScale;
            canvas.DrawCircle(point, size, SKPaints.ShapeOutline);
            canvas.DrawCircle(point, size, SKPaints.PaintExplosives);

            var distanceYOffset = 16f * MainWindow.UIScale;
            var nameXOffset = 7f * MainWindow.UIScale;
            var nameYOffset = 4f * MainWindow.UIScale;

            if (Settings.ShowName)
            {
                var namePoint = new SKPoint(point.X + nameXOffset, point.Y + nameYOffset);
                canvas.DrawText("Mortar", namePoint, SKPaints.TextOutline);
                canvas.DrawText("Mortar", namePoint, SKPaints.TextExplosives);
            }

            if (Settings.ShowDistance)
            {
                var distText = $"{(int)dist}m";
                var distWidth = SKPaints.TextExplosives.MeasureText(distText);
                var distPoint = new SKPoint(
                    point.X - (distWidth / 2),
                    point.Y + distanceYOffset
                );
                canvas.DrawText(distText, distPoint, SKPaints.TextOutline);
                canvas.DrawText(distText, distPoint, SKPaints.TextExplosives);
            }
        }

        public void DrawESP(SKCanvas canvas, LocalPlayer localPlayer)
        {
            if (!IsActive)
                return;

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
                    canvas.DrawCircle(scrPos.X, scrPos.Y, dotSize, SKPaints.PaintExplosiveESP);
                    break;

                case EntityRenderMode.Cross:
                    var crossSize = 5f * scale;

                    using (var thickPaint = new SKPaint
                    {
                        Color = SKPaints.PaintExplosiveESP.Color,
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
                        Color = SKPaints.PaintExplosiveESP.Color,
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
                    canvas.DrawRect(boxPt, SKPaints.PaintExplosiveESP);
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
                        canvas.DrawPath(diamondPath, SKPaints.PaintExplosiveESP);
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
                    SKPaints.TextExplosiveESP,
                    ESPSettings.ShowName ? "Motar" : null
                );
            }
        }
        // ─────────────────────────────────────────────────────
        // FAST PATH (scatter) — queue reads
        // ─────────────────────────────────────────────────────
        public void QueueScatterReads(ScatterReadIndex idx)
        {

        }

        // ─────────────────────────────────────────────────────
        // FAST PATH (scatter) — apply results
        // ─────────────────────────────────────────────────────
        public void OnRefresh(ScatterReadIndex idx)
        {

        }
        public void Refresh()
        {
            var artilleryProjectile = Memory.ReadValue<ArtilleryProjectile>(this, false);
            this.IsActive = artilleryProjectile.IsActive;
            if (this.IsActive)
            {
                _position = artilleryProjectile.Position;
            }
            else
            {
                _parent.TryRemove(this, out _);
            }
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
