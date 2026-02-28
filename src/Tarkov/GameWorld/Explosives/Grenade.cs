using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.Common.Maps;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Misc;
using SkiaSharp;

namespace eft_dma_radar.Tarkov.GameWorld.Explosives
{
    public sealed class Grenade : IExplosiveItem, IWorldEntity, IMapEntity, IESPEntity
    {
        private static void Log(string msg) =>
            XMLogging.WriteLine($"[GRENADE] {msg}");

        public static EntityTypeSettings Settings =>
            Program.Config.EntityTypeSettings.GetSettings("Grenade");

        public static EntityTypeSettingsESP ESPSettings =>
            ESP.Config.EntityTypeESPSettings.GetSettings("Grenade");

        private static readonly uint[] _toPosChain = UnityOffsets.TransformChain;
        private static readonly uint[] _toWeaponTemplate =
            { Offsets.Grenade.WeaponSource, Offsets.LootItem.Template };

        public static implicit operator ulong(Grenade x) => x.Addr;

        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly ConcurrentDictionary<ulong, IExplosiveItem> _parent;

        private readonly Queue<TrailPoint> _trailPositions = new();
        private Vector3 _lastTrailPosition;

        private float TRAIL_DURATION_SECONDS => ESPSettings.TrailDuration;
        private float MIN_TRAIL_DISTANCE     => ESPSettings.MinTrailDistance;
        private const int GRENADE_RADIUS_POINTS = 16;

        private UnityTransform _transform;
        private ulong TransformInternal;
        private ulong PosAddr { get; }

        public ulong Addr { get; }
        public string Name { get; }

        public float EffectiveDistance { get; private set; }

        private struct TrailPoint
        {
            public Vector3 Position;
            public DateTime Timestamp;
            public TrailPoint(Vector3 pos)
            {
                Position = pos;
                Timestamp = DateTime.UtcNow;
            }
        }

        private static readonly Dictionary<string, float> EffectiveDistances =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "F-1", 7f }, { "M67", 8f }, { "RGD-5", 7f }, { "RGN", 5f },
                { "RGO", 7f }, { "V40", 5f }, { "VOG-17", 6f }, { "VOG-25", 7f }
            };

        private bool IsDetonatedDirect =>
            Memory.ReadValue<bool>(this + Offsets.Grenade.IsDestroyed, false);

        public bool IsActive => _sw.Elapsed.TotalSeconds < 12f && !_forceInactive;

        private bool _forceInactive;

        // ─────────────────────────────────────────────────────
        // Scatter IDs — unique per grenade instance
        // ─────────────────────────────────────────────────────
        private static int _nextScatterBaseId = 100_000;
        private readonly int _scatterIdIsDead;
        private readonly int _scatterIdPos;

        // small flags to avoid log spam
        private bool _loggedFirstScatterQueue;
        private bool _loggedFirstScatterApply;
        private bool _loggedFirstPos;

        public Grenade(ulong baseAddr, ConcurrentDictionary<ulong, IExplosiveItem> parent)
        {
            Addr = baseAddr;
            _parent = parent;

            ////Log($$"Created grenade @ 0x{Addr:X}");

            // allocate unique scatter IDs for this grenade
            var baseId = System.Threading.Interlocked.Add(ref _nextScatterBaseId, 2) - 2;
            _scatterIdIsDead = baseId;
            _scatterIdPos    = baseId + 1;

            TransformInternal = Memory.ReadPtrChain(baseAddr, UnityOffsets.TransformChain, false);
            PosAddr           = Memory.ReadPtrChain(baseAddr, _toPosChain, false);
            _transform        = new UnityTransform(TransformInternal, false);

            ////Log($$"TransformInternal=0x{TransformInternal:X}, PosAddr=0x{PosAddr:X}");

            if (IsDetonatedDirect)
            {
                //Log($"Detonated at creation → discarding.");
                throw new Exception("Grenade detonated (at creation)");
            }

            // TEMPLATE → NAME RESOLUTION
            var templatePtr = Memory.ReadPtrChain(baseAddr, _toWeaponTemplate, false);
            ////Log($$"TemplatePtr = 0x{templatePtr:X}");

            var id = Memory.ReadValue<Types.MongoID>(templatePtr + Offsets.ItemTemplate._id);
            ////Log($$"MongoID.StringID = 0x{id.StringID:X}");

            var rawName = Memory.ReadUnityString(id.StringID, useCache: false) ?? "<null>";
            ////Log($$"Raw template name = '{rawName}'");

            if (EftDataManager.AllItems.TryGetValue(rawName, out var grenadeItem))
            {
                Name = grenadeItem.ShortName;
                if (grenadeItem.BsgId == "67b49e7335dec48e3e05e057")
                {
                    Name = $"{Name} (SHORT)";
                    //Log($"Marked as SHORT variant (by BsgId).");
                }
            }
            else
            {
                Name = rawName;
                //Log($"Template not found in AllItems → using rawName.");
            }

            if (!string.IsNullOrEmpty(Name) &&
                EffectiveDistances.TryGetValue(Name, out float dist))
            {
                EffectiveDistance = dist;
            }
            else
            {
                EffectiveDistance = 0;
            }

            ////Log($$"Final Name='{Name}', EffectiveDistance={EffectiveDistance}m");

            // initial slow refresh to populate trail + position
            Refresh();
            ////Log($$"Post-constructor Refresh → Initial Position={_position}");
        }

        // ─────────────────────────────────────────────────────
        // SLOW PATH REFRESH (direct DMA)
        // ─────────────────────────────────────────────────────
        public void Refresh()
        {
            if (!IsActive)
            {
                _trailPositions.Clear();
                return;
            }

            if (IsDetonatedDirect)
            {
                //Log($"Refresh: detonated (direct DMA) → removing from parent.");
                _trailPositions.Clear();
                _parent.TryRemove(Addr, out _);
                _forceInactive = true;
                return;
            }

            if (TransformInternal == 0)
            {
                //Log($"ERROR: TransformInternal == 0 in Refresh, cannot update position.");
                return;
            }

            var newPos = _transform.UpdatePosition();
            ////Log($$"Slow Refresh position = {newPos}");
            UpdateTrailAndPosition(newPos);
        }

        // ─────────────────────────────────────────────────────
        // FAST PATH (scatter) — queue reads
        // ─────────────────────────────────────────────────────
        public void QueueScatterReads(ScatterReadIndex idx)
        {
            if (!IsActive)
                return;

            // first-time log only
            if (!_loggedFirstScatterQueue)
            {
                _loggedFirstScatterQueue = true;
                ////Log($$"QueueScatterReads first time. Addr=0x{Addr:X}, TransformInternal=0x{TransformInternal:X}, IsActive={IsActive}");
            }

            // destroyed flag
            idx.AddEntry<bool>(
                id: _scatterIdIsDead,
                address: Addr + Offsets.Grenade.IsDestroyed
            );

            // position as Vector3 from TransformInternal + 0x90 (Unity world position)
            if (TransformInternal != 0)
            {
                idx.AddEntry<Vector3>(
                    id: _scatterIdPos,
                    address: TransformInternal + 0x90
                );
            }
        }

        // ─────────────────────────────────────────────────────
        // FAST PATH (scatter) — apply results
        // ─────────────────────────────────────────────────────
         public void OnRefresh(ScatterReadIndex idx)
        {
            if (!IsActive)
            {
                _trailPositions.Clear();
                return;
            }

            // destroyed?
            if (idx.TryGetResult<bool>(_scatterIdIsDead, out var isDead) && isDead)
            {
                //Log($"OnRefresh: scatter says IsDestroyed == true → removing.");
                _trailPositions.Clear();
                _parent.TryRemove(Addr, out _);
                _forceInactive = true;
                return;
            }

            if (TransformInternal == 0)
                return;

            // Use the trusted UnityTransform code for position
            var newPos = _transform.UpdatePosition();

            if (!_loggedFirstScatterApply)
            {
                _loggedFirstScatterApply = true;
                ////Log($$"OnRefresh: using UnityTransform.UpdatePosition → {newPos}");
            }

            UpdateTrailAndPosition(newPos);
        }


        // ─────────────────────────────────────────────────────
        // Shared trail update logic
        // ─────────────────────────────────────────────────────
        private void UpdateTrailAndPosition(in Vector3 newPos)
        {
            _position = newPos;

            if (!_loggedFirstPos)
            {
                _loggedFirstPos = true;
                ////Log($$"UpdateTrailAndPosition: first Position set to {newPos}");
            }

            var now = DateTime.UtcNow;
            if (_trailPositions.Count == 0 ||
                Vector3.Distance(_lastTrailPosition, newPos) >= MIN_TRAIL_DISTANCE)
            {
                _trailPositions.Enqueue(new TrailPoint(newPos));
                _lastTrailPosition = newPos;
            }

            while (_trailPositions.Count > 0)
            {
                var oldest = _trailPositions.Peek();
                if ((now - oldest.Timestamp).TotalSeconds > TRAIL_DURATION_SECONDS)
                    _trailPositions.Dequeue();
                else
                    break;
            }
        }

        #region Interfaces

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

            // one-time draw log just to prove we ever render them on map
            if (!_drawLoggedOnce)
            {
                _drawLoggedOnce = true;
                ////Log($$"Draw(Map): Pos={Position}, Dist={dist}, MapPoint=({point.X},{point.Y})");
            }

            var isPlayerInDanger = EffectiveDistance > 0 && dist <= EffectiveDistance;

            SKPaints.ShapeOutline.StrokeWidth =
                SKPaints.PaintExplosives.StrokeWidth + 2f * MainWindow.UIScale;
            var size = 5 * MainWindow.UIScale;
            canvas.DrawCircle(point, size, SKPaints.ShapeOutline);

            var paintToUse = isPlayerInDanger ? SKPaints.PaintExplosivesDanger : SKPaints.PaintExplosives;
            var textPaintToUse = isPlayerInDanger ? SKPaints.TextExplosivesDanger : SKPaints.TextExplosives;

            canvas.DrawCircle(point, size, paintToUse);

            if (Settings.ShowRadius && EffectiveDistance > 0)
            {
                var radiusUnscaled = EffectiveDistance * mapParams.Map.Scale * mapParams.Map.SvgScale;
                var radius = radiusUnscaled * mapParams.XScale;

                using (var radiusPaint = new SKPaint
                {
                    Color = paintToUse.Color.WithAlpha(80),
                    StrokeWidth = 1.5f * MainWindow.UIScale,
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke
                })
                {
                    canvas.DrawCircle(point, radius, radiusPaint);
                }

                using (var fillPaint = new SKPaint
                {
                    Color = paintToUse.Color.WithAlpha(30),
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill
                })
                {
                    canvas.DrawCircle(point, radius, fillPaint);
                }
            }

            var distanceYOffset = 20f * MainWindow.UIScale;
            var nameXOffset = 10f * MainWindow.UIScale;
            var nameYOffset = 4f * MainWindow.UIScale;

            if (Settings.ShowName && !string.IsNullOrEmpty(Name))
            {
                var namePoint = new SKPoint(point.X + nameXOffset, point.Y + nameYOffset);
                canvas.DrawText(Name, namePoint, SKPaints.TextOutline);
                canvas.DrawText(Name, namePoint, textPaintToUse);
            }

            if (Settings.ShowDistance)
            {
                var distText = $"{(int)dist}m";
                var distWidth = SKPaints.TextExplosives.MeasureText($"{(int)dist}");
                var distPoint = new SKPoint(
                    point.X - (distWidth / 2),
                    point.Y + distanceYOffset
                );
                canvas.DrawText(distText, distPoint, SKPaints.TextOutline);
                canvas.DrawText(distText, distPoint, textPaintToUse);
            }
        }

        private bool _drawLoggedOnce;

        public void DrawESP(SKCanvas canvas, LocalPlayer localPlayer)
        {
            if (!IsActive)
                return;

            var dist = Vector3.Distance(localPlayer.Position, Position);
            if (dist > ESPSettings.RenderDistance)
                return;

            if (!CameraManagerBase.WorldToScreen(ref _position, out var scrPos))
                return;

            var scale = ESP.Config.FontScale;
            var isPlayerInDanger = EffectiveDistance > 0 && dist <= EffectiveDistance;

            if (ESPSettings.ShowGrenadeTrail)
                DrawTrajectoryTrail(canvas, scrPos);

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
                        canvas.DrawLine(scrPos.X, scrPos.Y - plusSize,
                                        scrPos.X, scrPos.Y + plusSize, thickPaint);
                        canvas.DrawLine(scrPos.X - plusSize, scrPos.Y,
                                        scrPos.X + plusSize, scrPos.Y, thickPaint);
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

            if (ESPSettings.ShowRadius && EffectiveDistance > 0)
            {
                var circlePoints = new List<SKPoint>();

                for (int i = 0; i < GRENADE_RADIUS_POINTS; i++)
                {
                    var angle = (float)(2 * Math.PI * i / GRENADE_RADIUS_POINTS);
                    var x = Position.X + EffectiveDistance * (float)Math.Cos(angle);
                    var z = Position.Z + EffectiveDistance * (float)Math.Sin(angle);
                    var circleWorldPos = new Vector3(x, Position.Y, z);

                    if (CameraManagerBase.WorldToScreen(ref circleWorldPos, out var circleScreenPos))
                        circlePoints.Add(circleScreenPos);
                }

                if (circlePoints.Count > 2)
                {
                    using (var path = new SKPath())
                    {
                        path.MoveTo(circlePoints[0]);
                        for (int i = 1; i < circlePoints.Count; i++)
                            path.LineTo(circlePoints[i]);
                        path.Close();
                        canvas.DrawPath(path, SKPaints.PaintExplosiveRadiusESP);
                    }
                }
            }

            if (isPlayerInDanger || ESPSettings.ShowName || ESPSettings.ShowDistance)
            {
                var textY = scrPos.Y + 16f * scale;
                var textPt = new SKPoint(scrPos.X, textY);

                string nameText = null;
                if (isPlayerInDanger)
                    nameText = "*DANGER*";

                if (ESPSettings.ShowName && !string.IsNullOrEmpty(Name))
                {
                    if (isPlayerInDanger)
                        nameText += " " + Name;
                    else
                        nameText = Name;
                }

                textPt.DrawESPText(
                    canvas,
                    this,
                    localPlayer,
                    ESPSettings.ShowDistance,
                    SKPaints.TextExplosiveESP,
                    nameText
                );
            }
        }

        private void DrawTrajectoryTrail(SKCanvas canvas, SKPoint currentScreenPos)
        {
            if (_trailPositions.Count < 2)
                return;

            var trailPoints = _trailPositions.ToArray();
            var screenPoints = new List<(SKPoint screenPos, double age)>();
            var now = DateTime.UtcNow;

            for (int i = 0; i < trailPoints.Length - 1; i++)
            {
                var trailPoint = trailPoints[i];
                var pos = trailPoint.Position;
                if (CameraManagerBase.WorldToScreen(ref pos, out var screenPos))
                {
                    var ageInSeconds = (now - trailPoint.Timestamp).TotalSeconds;
                    screenPoints.Add((screenPos, ageInSeconds));
                }
            }

            if (trailPoints.Length > 0)
            {
                var mostRecentPoint = trailPoints[^1];
                var currentAge = (now - mostRecentPoint.Timestamp).TotalSeconds;
                screenPoints.Add((currentScreenPos, currentAge));
            }

            if (screenPoints.Count < 2)
                return;

            var scale = ESP.Config.FontScale;
            var baseColor = SKPaints.PaintExplosiveESP.Color;

            using (var trailPaint = new SKPaint
            {
                StrokeWidth = 2.5f * scale,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round
            })
            {
                for (int i = 0; i < screenPoints.Count - 1; i++)
                {
                    var currentPoint = screenPoints[i];
                    var nextPoint = screenPoints[i + 1];

                    var ageProgress = 1.0 - (currentPoint.age / TRAIL_DURATION_SECONDS);
                    var alpha = (byte)(60 + (195 * Math.Max(0, ageProgress)));

                    trailPaint.Color = baseColor.WithAlpha(alpha);

                    canvas.DrawLine(currentPoint.screenPos, nextPoint.screenPos, trailPaint);
                }
            }
        }

        #endregion
    }
}
