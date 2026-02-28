using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.Common.Maps;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Common.Unity;
using System.Xml.Linq;
using static eft_dma_radar.Tarkov.GameWorld.Exits.Exfil;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace eft_dma_radar.Tarkov.GameWorld.Interactables
{
    public sealed class Door : IMouseoverEntity, IMapEntity, IESPEntity
    {
        public static EntityTypeSettings Settings => Program.Config.EntityTypeSettings.GetSettings("Door");
        public static EntityTypeSettingsESP ESPSettings => ESP.Config.EntityTypeESPSettings.GetSettings("Door");
        private const float HEIGHT_INDICATOR_THRESHOLD = 1.85f;

        // IL2CPP transform chain (Dec 2025)
        private static readonly uint[] _transformInternalChain = UnityOffsets.TransformChain;

        public ulong Base { get; set; }
        public EDoorState DoorState { get; set; }
        public string Id { get; set; }
        public string? KeyId { get; set; }
        public string? KeyName { get; private set; }
        public string? KeyItemID { get; private set; }
        public string? KeyFullName { get; private set; }
        private Vector3 _positionBackingField;
        public ref Vector3 Position => ref _positionBackingField; // for interface use

        /// <summary>
        /// Cached mouseover position for hover detection
        /// </summary>
        public Vector2 MouseoverPosition { get; set; }

        public Door(ulong ptr)
        {
            try
            {
                Base = ptr;

                try
                {
                    var keyidPtr = Memory.ReadPtr(Base + Offsets.Interactable.KeyId, false);
                    KeyId = Memory.ReadUnityString(keyidPtr);

                    if (!string.IsNullOrWhiteSpace(KeyId) && EftDataManager.AllItems.TryGetValue(KeyId, out var keyItem))
                    {
                        KeyName = keyItem.ShortName;
                        KeyItemID = keyItem.BsgId;
                        KeyFullName = keyItem.Name;
                    }
                }
                catch (Exception e)
                {
                    XMLogging.WriteLine($"[DOOR] Failed to read KeyId or resolve name: {e.Message}");
                }

                try
                {
                    var doorIdPtr = Memory.ReadPtr(Base + Offsets.Interactable.Id, false);
                    Id = Memory.ReadUnityString(doorIdPtr);
                }
                catch (Exception e)
                {
                    XMLogging.WriteLine($"[DOOR] Failed to read Id: {e.Message}");
                }

                try
                {
                    var transformInternal = Memory.ReadPtrChain(Base, _transformInternalChain, false);
                    var transform = new UnityTransform(transformInternal);
                    _positionBackingField = transform.UpdatePosition();
                    _positionBackingField = Position;
                }
                catch (Exception e)
                {
                    XMLogging.WriteLine($"[DOOR] Failed to get Position: {e.Message}");
                }

                try
                {
                    DoorState = (EDoorState)Memory.ReadValue<byte>(Base + Offsets.Interactable._doorState);
                }
                catch (Exception e)
                {
                    XMLogging.WriteLine($"[DOOR] Failed to read DoorState: {e.Message}");
                }
            }
            catch (Exception e)
            {
                XMLogging.WriteLine($"[DOOR] Fatal error initializing door: {e}");
            }
        }

        public void Refresh()
        {
            DoorState = (EDoorState)Memory.ReadValue<byte>(Base + Offsets.Interactable._doorState);
        }

        /// <summary>
        /// Draw the Door on the radar map
        /// </summary>
        /// <summary>
        /// Draw the Door on the radar map, respecting the ShowLockedDoors and ShowUnlockedDoors settings
        /// </summary>
        public void Draw(SKCanvas canvas, XMMapParams mapParams, ILocalPlayer localPlayer)
        {
            if (DoorState == EDoorState.None || string.IsNullOrWhiteSpace(KeyName) || string.IsNullOrWhiteSpace(KeyId))
                return;

            var isLocked = DoorState == EDoorState.Locked;
            if ((isLocked && !Settings.ShowLockedDoors) || (!isLocked && !Settings.ShowUnlockedDoors))
                return;

            var dist = Vector3.Distance(localPlayer.Position, Position);
            if (dist > Settings.RenderDistance)
                return;

            var heightDiff = Position.Y - localPlayer.Position.Y;
            var paint = GetPaints();
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
                canvas.DrawPath(path, paint.Item1);
                distanceYOffset = 18f * MainWindow.UIScale;
                nameYOffset = 6f * MainWindow.UIScale;
            }
            else if (heightDiff < -HEIGHT_INDICATOR_THRESHOLD)
            {
                using var path = point.GetDownArrow(5f);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paint.Item1);
                distanceYOffset = 12f * MainWindow.UIScale;
                nameYOffset = 1f * MainWindow.UIScale;
            }
            else
            {
                canvas.DrawText("i", point, SKPaints.TextOutline);
                canvas.DrawText("i", point, paint.Item2);
                distanceYOffset = 12f * MainWindow.UIScale;
                nameYOffset = 0f * MainWindow.UIScale;
            }

            if (Settings.ShowName)
            {
                var namePoint = point;
                namePoint.Offset(nameXOffset, nameYOffset);
                canvas.DrawText(KeyName, namePoint, SKPaints.TextOutline);
                canvas.DrawText(KeyName, namePoint, paint.Item2);
            }

            if (Settings.ShowDistance)
            {
                var distText = $"{(int)dist}m";
                var distWidth = paint.Item2.MeasureText($"{(int)dist}");
                var distPoint = new SKPoint(
                    point.X - (distWidth / 2),
                    point.Y + distanceYOffset
                );
                canvas.DrawText(distText, distPoint, SKPaints.TextOutline);
                canvas.DrawText(distText, distPoint, paint.Item2);
            }
        }

        private ValueTuple<SKPaint, SKPaint> GetPaints()
        {
            switch (DoorState)
            {
                case EDoorState.Open:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintDoorOpen, SKPaints.TextDoorOpen);
                case EDoorState.Shut:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintDoorShut, SKPaints.TextDoorShut);
                case EDoorState.Interacting:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintDoorInteracting, SKPaints.TextDoorInteracting);
                case EDoorState.Breaching:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintDoorBreaching, SKPaints.TextDoorBreaching);
                case EDoorState.Locked:
                default:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintDoorLocked, SKPaints.TextDoorLocked);
            }
        }

        private ValueTuple<SKPaint, SKPaint> GetESPPaints()
        {
            switch (DoorState)
            {
                case EDoorState.Open:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintDoorOpenESP, SKPaints.TextDoorOpenESP);
                case EDoorState.Shut:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintDoorShutESP, SKPaints.TextDoorShutESP);
                case EDoorState.Interacting:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintDoorInteractingESP, SKPaints.TextDoorInteractingESP);
                case EDoorState.Breaching:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintDoorBreachingESP, SKPaints.TextDoorBreachingESP);
                case EDoorState.Locked:
                default:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintDoorLockedESP, SKPaints.TextDoorLockedESP);
            }
        }

        public void DrawMouseover(SKCanvas canvas, XMMapParams mapParams, LocalPlayer localPlayer)
        {
            List<string> lines = new();
            var stateText = DoorState != EDoorState.None ? DoorState.ToString() : null;
            var keyText = KeyFullName ?? KeyId;

            if (!string.IsNullOrWhiteSpace(keyText))
                lines.Add(keyText);

            if (!string.IsNullOrWhiteSpace(stateText))
                lines.Add(stateText);

            if (lines.Count > 0)
                Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams).DrawMouseoverText(canvas, lines);
        }

        public void DrawESP(SKCanvas canvas, LocalPlayer localPlayer)
        {
            if (DoorState == EDoorState.None || string.IsNullOrWhiteSpace(KeyName) || string.IsNullOrWhiteSpace(KeyId))
                return;

            var isLocked = DoorState == EDoorState.Locked;
            if ((isLocked && !ESPSettings.ShowLockedDoors) || (!isLocked && !ESPSettings.ShowUnlockedDoors))
                return;

            var dist = Vector3.Distance(localPlayer.Position, Position);

            if (dist > ESPSettings.RenderDistance)
                return;

            if (!CameraManagerBase.WorldToScreen(ref _positionBackingField, out var scrPos))
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
                var textY = scrPos.Y + 16f * scale;
                var textPt = new SKPoint(scrPos.X, textY);

                textPt.DrawESPText(
                    canvas,
                    this,
                    localPlayer,
                    ESPSettings.ShowDistance,
                    paint.Item2,
                    ESPSettings.ShowName ? (KeyName ?? KeyId ?? "Unknown Key") : null
                );
            }
        }
    }

    public enum EDoorState
    {
        // Token: 0x0400E90E RID: 59662
        None = 0,
        // Token: 0x0400E90F RID: 59663
        Locked = 1,
        // Token: 0x0400E910 RID: 59664
        Shut = 2,
        // Token: 0x0400E911 RID: 59665
        Open = 4,
        // Token: 0x0400E912 RID: 59666
        Interacting = 8,
        // Token: 0x0400E913 RID: 59667
        Breaching = 16
    }
}