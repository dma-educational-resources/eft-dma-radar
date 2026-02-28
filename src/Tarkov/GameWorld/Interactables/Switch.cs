using eft_dma_radar;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.Common.Maps;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Common.Unity;
using SkiaSharp;
using System;
using System.Collections.Generic;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace eft_dma_radar.Tarkov.GameWorld.Exits
{
    /// <summary>
    /// Represents an interactive switch on the map
    /// </summary>
    public sealed class Switch : IMouseoverEntity, IMapEntity, IESPEntity
    {
        #region Properties
        private Vector3 _position;
        public ref Vector3 Position => ref _position;

        public string Name { get; }
        public string SwitchInfo { get; }
        public Vector2 MouseoverPosition { get; set; }

        public static EntityTypeSettings Settings => Program.Config.EntityTypeSettings.GetSettings("Switch");
        public static EntityTypeSettingsESP ESPSettings => ESP.Config.EntityTypeESPSettings.GetSettings("Switch");
        private const float HEIGHT_INDICATOR_THRESHOLD = 1.85f;

        /// <summary>
        /// The type of switch for determining display and interaction behavior
        /// </summary>
        public SwitchType Type { get; }

        #endregion

        #region Constructor

        public Switch(Vector3 position, string name)
        {
            _position = position;
            Name = name;
            SwitchInfo = GetDescriptionFromName(name);
            Type = DetermineSwitchType(name);
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Generate a description based on the switch name
        /// </summary>
        private string GetDescriptionFromName(string name)
        {
            if (name.Contains("power", StringComparison.OrdinalIgnoreCase))
                return "Controls power to an area";
            else if (name.Contains("alarm", StringComparison.OrdinalIgnoreCase))
                return "Triggers an alarm when activated";
            else if (name.Contains("door", StringComparison.OrdinalIgnoreCase))
                return "Opens a locked door";
            else if (name.Contains("extract", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("exfil", StringComparison.OrdinalIgnoreCase))
                return "Activates an extraction point";
            else if (name.Contains("light", StringComparison.OrdinalIgnoreCase))
                return "Controls lighting in an area";

            return "Activates something on the map";
        }

        /// <summary>
        /// Determine the type of switch based on its name
        /// </summary>
        private SwitchType DetermineSwitchType(string name)
        {
            if (name.Contains("power", StringComparison.OrdinalIgnoreCase))
                return SwitchType.Power;
            else if (name.Contains("alarm", StringComparison.OrdinalIgnoreCase))
                return SwitchType.Alarm;
            else if (name.Contains("door", StringComparison.OrdinalIgnoreCase))
                return SwitchType.Door;
            else if (name.Contains("extract", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("exfil", StringComparison.OrdinalIgnoreCase))
                return SwitchType.Extraction;
            else if (name.Contains("light", StringComparison.OrdinalIgnoreCase))
                return SwitchType.Light;

            return SwitchType.Generic;
        }

        /// <summary>
        /// Get the appropriate paint based on switch type
        /// </summary>
        private SKPaint GetPaint()
        {
            switch (Type)
            {
                case SwitchType.Power:
                    return SKPaints.PaintSwitch;
                case SwitchType.Alarm:
                    return SKPaints.PaintSwitch;
                case SwitchType.Door:
                    return SKPaints.PaintSwitch;
                case SwitchType.Extraction:
                    return SKPaints.PaintSwitch;
                case SwitchType.Light:
                    return SKPaints.PaintSwitch;
                case SwitchType.Generic:
                default:
                    return SKPaints.PaintSwitch;
            }
        }

        #endregion

        #region Drawing

        /// <summary>
        /// Draw the switch on the radar map
        /// </summary>
        public void Draw(SKCanvas canvas, XMMapParams mapParams, ILocalPlayer localPlayer)
        {
            var dist = Vector3.Distance(localPlayer.Position, Position);
            if (dist > Settings.RenderDistance)
                return;

            var heightDiff = Position.Y - localPlayer.Position.Y;
            var paint = GetPaint();
            var point = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            MouseoverPosition = new Vector2(point.X, point.Y);

            SKPaints.ShapeOutline.StrokeWidth = 1f;

            float distanceYOffset;
            float nameXOffset = 7f * MainWindow.UIScale;
            float nameYOffset;

            if (heightDiff > HEIGHT_INDICATOR_THRESHOLD)
            {
                using var path = point.GetUpArrow(5f);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paint);
                distanceYOffset = 18f * MainWindow.UIScale;
                nameYOffset = 6f * MainWindow.UIScale;
            }
            else if (heightDiff < -HEIGHT_INDICATOR_THRESHOLD)
            {
                using var path = point.GetDownArrow(5f);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paint);
                distanceYOffset = 12f * MainWindow.UIScale;
                nameYOffset = 1f * MainWindow.UIScale;
            }
            else
            {
                var size = 4.75f * MainWindow.UIScale;
                canvas.DrawRect(point.X - size / 2, point.Y - size / 2, size, size, SKPaints.ShapeOutline);
                canvas.DrawRect(point.X - size / 2, point.Y - size / 2, size, size, paint);
                distanceYOffset = 16f * MainWindow.UIScale;
                nameYOffset = 4f * MainWindow.UIScale;
            }

            if (Settings.ShowName)
            {
                var namePoint = point;
                namePoint.Offset(nameXOffset, nameYOffset);
                canvas.DrawText(Name, namePoint, SKPaints.TextOutline);
                canvas.DrawText(Name, namePoint, SKPaints.TextLoot);
            }

            if (Settings.ShowDistance)
            {
                var distText = $"{(int)dist}m";
                var distWidth = SKPaints.TextLoot.MeasureText($"{(int)dist}");
                var distPoint = new SKPoint(
                    point.X - (distWidth / 2),
                    point.Y + distanceYOffset
                );
                canvas.DrawText(distText, distPoint, SKPaints.TextOutline);
                canvas.DrawText(distText, distPoint, SKPaints.TextLoot);
            }
        }

        /// <summary>
        /// Draw mouseover information when hovering over the switch
        /// </summary>
        public void DrawMouseover(SKCanvas canvas, XMMapParams mapParams, LocalPlayer localPlayer)
        {
            List<string> lines = new();
            var switchName = Name;
            switchName ??= "unknown";
            lines.Add($"{switchName} ({SwitchInfo})");
            Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams).DrawMouseoverText(canvas, lines);
        }

        /// <summary>
        /// Draw ESP for the switch, similar to exfil points
        /// </summary>
        public void DrawESP(SKCanvas canvas, LocalPlayer localPlayer)
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
                    canvas.DrawCircle(scrPos.X, scrPos.Y, dotSize, SKPaints.PaintSwitchESP);
                    break;

                case EntityRenderMode.Cross:
                    var crossSize = 5f * scale;

                    using (var thickPaint = new SKPaint
                    {
                        Color = SKPaints.PaintSwitchESP.Color,
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
                        Color = SKPaints.PaintSwitchESP.Color,
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
                    canvas.DrawRect(boxPt, SKPaints.PaintSwitchESP);
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
                        canvas.DrawPath(diamondPath, SKPaints.PaintSwitchESP);
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
                    SKPaints.TextSwitchesESP,
                    ESPSettings.ShowName ? Name : null
                );
            }
        }

        #endregion

        #region Enums

        /// <summary>
        /// Defines the different types of switches
        /// </summary>
        public enum SwitchType
        {
            [Description("Generic")] Generic,
            [Description("Power")] Power,
            [Description("Alarm")] Alarm,
            [Description("Door")] Door,
            [Description("Extraction")] Extraction,
            [Description("Light")] Light
        }

        #endregion
    }
}