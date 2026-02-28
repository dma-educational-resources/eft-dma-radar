using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.Common.Maps;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Common.Unity.Collections;
using static eft_dma_radar.Tarkov.EFTPlayer.Player;

namespace eft_dma_radar.Tarkov.GameWorld.Exits
{
    public sealed class Exfil : IExitPoint, IWorldEntity, IMapEntity, IMouseoverEntity, IESPEntity
    {
        public static EntityTypeSettings Settings => Program.Config.EntityTypeSettings.GetSettings("Exfil");
        public static EntityTypeSettingsESP ESPSettings => ESP.Config.EntityTypeESPSettings.GetSettings("Exfil");

        public static implicit operator ulong(Exfil x) => x._addr;

        private readonly bool _isPMC;
        private HashSet<string> PmcEntries { get; } = new(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> ScavIds { get; } = new(StringComparer.OrdinalIgnoreCase);

        private readonly ulong _addr;
        public string Name { get; private set; }
        public bool IsSecret { get; private set; } = false;
        public EStatus Status { get; private set; } = EStatus.Closed;

        private const float HEIGHT_INDICATOR_THRESHOLD = 1.85f;

        public Exfil(ulong baseAddr, bool isPMC)
        {
            _addr = baseAddr;
            _isPMC = isPMC;
            var transformInternal = Memory.ReadPtrChain(baseAddr, UnityOffsets.TransformChain, false);
            var namePtr = Memory.ReadPtrChain(baseAddr, new[] { Offsets.Exfil.Settings, Offsets.ExfilSettings.Name });
            Name = Memory.ReadUnityString(namePtr)?.Trim();

            if (string.IsNullOrEmpty(Name))
                Name = "default";

            // Lookup real map name (if possible)
            if (GameData.ExfilNames.TryGetValue(Memory.MapID, out var mapExfils)
                && mapExfils.TryGetValue(Name, out var exfilName))
                Name = exfilName;

            IsSecret = Name.ToLower().Contains("secret");

            _position = new UnityTransform(transformInternal, false).UpdatePosition();
        }

        /// <summary>
        /// Update Exfil Information/Status.
        /// </summary>
        public void Update(Enums.EExfiltrationStatus status)
        {
            /// Update Status
            switch (status)
            {
                case Enums.EExfiltrationStatus.NotPresent:
                    Status = EStatus.Closed;
                    break;
                case Enums.EExfiltrationStatus.UncompleteRequirements:
                    Status = EStatus.Pending;
                    break;
                case Enums.EExfiltrationStatus.Countdown:
                    Status = EStatus.Open;
                    break;
                case Enums.EExfiltrationStatus.RegularMode:
                    Status = EStatus.Open;
                    break;
                case Enums.EExfiltrationStatus.Pending:
                    Status = EStatus.Pending;
                    break;
                case Enums.EExfiltrationStatus.AwaitsManualActivation:
                    Status = EStatus.Pending;
                    break;
                case Enums.EExfiltrationStatus.Hidden:
                    Status = EStatus.Closed;
                    break;
            }

            /// Update Entry Points
            if (_isPMC)
            {
                var entriesArrPtr = Memory.ReadPtr(_addr + Offsets.Exfil.EligibleEntryPoints);
                using var entriesArr = MemArray<ulong>.Get(entriesArrPtr);
                foreach (var entryNamePtr in entriesArr)
                {
                    var entryName = Memory.ReadUnityString(entryNamePtr);
                    PmcEntries.Add(entryName);
                }
            }
            else // Scav Exfils
            {
                var eligibleIdsPtr = Memory.ReadPtr(_addr + Offsets.ScavExfil.EligibleIds);
                using var idsArr = MemList<ulong>.Get(eligibleIdsPtr);
                foreach (var idPtr in idsArr)
                {
                    var idName = Memory.ReadUnityString(idPtr);
                    ScavIds.Add(idName);
                }
            }
        }

        /// <summary>
        /// Determines whether this exfil can be used by the local player
        /// </summary>
        public bool IsAvailableForPlayer(LocalPlayer player)
        {
            if (player == null)
                return false;

            var isEligiblePlayer = (player.IsPmc && PmcEntries.Contains(player.EntryPoint ?? "NULL")) ||
                                    (player.IsScav && ScavIds.Contains(player.ProfileId)) ||
                                    IsSecret;

            if (!isEligiblePlayer)
                return false;

            return Status != EStatus.Closed;
        }

        /// <summary>
        /// Determines if the exfil is completely inactive (closed or not available to player)
        /// </summary>
        public bool IsInactive(LocalPlayer player)
        {
            return Status == EStatus.Closed || !IsAvailableForPlayer(player);
        }

        #region Interfaces

        private Vector3 _position;
        public ref Vector3 Position => ref _position;
        public Vector2 MouseoverPosition { get; set; }

        public void Draw(SKCanvas canvas, XMMapParams mapParams, ILocalPlayer localPlayer)
        {
            if (Settings.HideInactiveExfils && IsInactive(localPlayer as LocalPlayer))
                return;

            var dist = Vector3.Distance(localPlayer.Position, Position);
            if (dist > Settings.RenderDistance)
                return;

            var heightDiff = Position.Y - localPlayer.Position.Y;
            var paint = GetPaints();
            var point = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);

            MouseoverPosition = new Vector2(point.X, point.Y);
            SKPaints.ShapeOutline.StrokeWidth = 2f;

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
                var size = 4.75f * MainWindow.UIScale;
                canvas.DrawCircle(point, size, SKPaints.ShapeOutline);
                canvas.DrawCircle(point, size, paint.Item1);
                distanceYOffset = 16f * MainWindow.UIScale;
                nameYOffset = 4f * MainWindow.UIScale;
            }

            if (Settings.ShowName)
            {
                var namePoint = point;
                namePoint.Offset(nameXOffset, nameYOffset);
                canvas.DrawText(Name, namePoint, SKPaints.TextOutline);
                canvas.DrawText(Name, namePoint, paint.Item2);
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
            var localPlayer = Memory.LocalPlayer;

            if (localPlayer != null && !IsAvailableForPlayer(localPlayer))
                return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintExfilInactive, SKPaints.TextExfilInactive);

            switch (Status)
            {
                case EStatus.Open:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintExfilOpen, SKPaints.TextExfilOpen);
                case EStatus.Pending:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintExfilPending, SKPaints.TextExfilPending);
                case EStatus.Closed:
                default:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintExfilClosed, SKPaints.TextExfilClosed);
            }
        }

        private ValueTuple<SKPaint, SKPaint> GetESPPaints()
        {
            var localPlayer = Memory.LocalPlayer;

            if (localPlayer != null && !IsAvailableForPlayer(localPlayer))
                return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintExfilInactiveESP, SKPaints.TextExfilInactiveESP);

            switch (Status)
            {
                case EStatus.Open:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintExfilOpenESP, SKPaints.TextExfilOpenESP);
                case EStatus.Pending:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintExfilPendingESP, SKPaints.TextExfilPendingESP);
                case EStatus.Closed:
                default:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintExfilClosedESP, SKPaints.TextExfilClosedESP);
            }
        }

        public void DrawMouseover(SKCanvas canvas, XMMapParams mapParams, LocalPlayer localPlayer)
        {
            List<string> lines = new();
            var exfilName = Name;
            exfilName ??= "unknown";
            lines.Add($"{exfilName} ({Status.GetDescription()})");

            Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams).DrawMouseoverText(canvas, lines);
        }

        public void DrawESP(SKCanvas canvas, LocalPlayer localPlayer)
        {
            var dist = Vector3.Distance(localPlayer.Position, Position);

            if (dist > ESPSettings.RenderDistance)
                return;

            if (!CameraManagerBase.WorldToScreen(ref _position, out var scrPos))
                return;

            var isRelevantSecret = IsSecret && (Status is EStatus.Pending || Status is EStatus.Open);

            if (!isRelevantSecret)
            {
                if ((Status is EStatus.Closed) ||
                    (localPlayer.IsPmc && !PmcEntries.Contains(localPlayer.EntryPoint)) ||
                    (localPlayer.IsScav && !ScavIds.Contains(localPlayer.ProfileId)))
                    return;
            }

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
                    ESPSettings.ShowName ? Name : null
                );
            }
        }

        #endregion

        public enum EStatus
        {
            [Description(nameof(Open))] Open,
            [Description(nameof(Pending))] Pending,
            [Description(nameof(Closed))] Closed
        }
    }
}