using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.Pages;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Common.Unity;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace eft_dma_radar.UI.SKWidgetControl
{
    public sealed class AimviewWidget : SKWidget
    {
        private SKBitmap _aimviewBitmap;
        private SKCanvas _aimviewCanvas;
        private readonly float _textOffsetY;
        private readonly float _boxHalfSize;
        private static Config Config => Program.Config;

        public AimviewWidget(SKGLElement parent, SKRect location, bool minimized, float scale)
            : base(parent, "Aimview", new SKPoint(location.Left, location.Top), new SKSize(location.Width, location.Height), scale)
        {
            _aimviewBitmap = new SKBitmap((int)location.Width, (int)location.Height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
            _aimviewCanvas = new SKCanvas(_aimviewBitmap);
            _textOffsetY = 12.5f * scale;
            _boxHalfSize = 4f * scale;
            Minimized = minimized;
            SetScaleFactor(scale);
        }

        private static LocalPlayer LocalPlayer => Memory.LocalPlayer;
        private static IReadOnlyCollection<Player> AllPlayers => Memory.Players;
        private static bool InRaid => Memory.InRaid;
        private static IEnumerable<LootItem> Loot => Memory.Loot?.FilteredLoot;
        private static IEnumerable<StaticLootContainer> Containers => Memory.Loot?.StaticLootContainers;

        public override void Draw(SKCanvas canvas)
        {
            base.Draw(canvas);
            if (!Minimized)
                RenderESPWidget(canvas, ClientRectangle);
        }

        private void RenderESPWidget(SKCanvas parent, SKRect dest)
        {
            EnsureBitmapSize();

            _aimviewCanvas.Clear(SKColors.Transparent);

            try
            {
                var inRaid = InRaid;
                var localPlayer = LocalPlayer;

                if (inRaid && localPlayer != null)
                {
                    if (Config.ProcessLoot)
                    {
                        DrawLoot(localPlayer);
                        DrawContainers(localPlayer);
                    }

                    DrawPlayers();
                    DrawCrosshair();
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"CRITICAL ESP WIDGET RENDER ERROR: {ex}");
            }

            _aimviewCanvas.Flush();
            parent.DrawBitmap(_aimviewBitmap, dest, SharedPaints.PaintBitmap);
        }

        private void EnsureBitmapSize()
        {
            var size = Size;
            if (_aimviewBitmap == null || _aimviewCanvas == null ||
                _aimviewBitmap.Width != size.Width || _aimviewBitmap.Height != size.Height)
            {
                _aimviewCanvas?.Dispose();
                _aimviewCanvas = null;
                _aimviewBitmap?.Dispose();
                _aimviewBitmap = null;

                _aimviewBitmap = new SKBitmap((int)size.Width, (int)size.Height,
                    SKImageInfo.PlatformColorType, SKAlphaType.Premul);
                _aimviewCanvas = new SKCanvas(_aimviewBitmap);
            }
        }

        private void DrawLoot(LocalPlayer localPlayer)
        {
            var loot = Loot;
            if (loot == null) return;

            foreach (var item in loot)
            {
                var dist = Vector3.Distance(localPlayer.Position, item.Position);
                if (dist >= 10f) continue;

                if (!CameraManagerBase.WorldToScreen(ref item.Position, out var itemScrPos)) continue;

                var adjPos = ScaleESPPoint(itemScrPos);
                var boxPt = new SKRect(
                    adjPos.X - _boxHalfSize,
                    adjPos.Y + _boxHalfSize,
                    adjPos.X + _boxHalfSize,
                    adjPos.Y - _boxHalfSize);

                var textPt = new SKPoint(adjPos.X, adjPos.Y + _textOffsetY);

                _aimviewCanvas.DrawRect(boxPt, PaintESPWidgetLoot);
                var label = item.GetUILabel() + $" ({dist:n1}m)";
                _aimviewCanvas.DrawText(label, textPt, TextESPWidgetLoot);
            }
        }

        private void DrawContainers(LocalPlayer localPlayer)
        {
            if (!Config.Containers.Show) return;

            var containers = Containers;
            if (containers == null) return;

            foreach (var container in containers)
            {
                if (!LootSettingsControl.ContainerIsTracked(container.ID ?? "NULL")) continue;

                if (Config.Containers.HideSearched && container.Searched) continue;

                var dist = Vector3.Distance(localPlayer.Position, container.Position);
                if (dist >= 10f) continue;

                if (!CameraManagerBase.WorldToScreen(ref container.Position, out var containerScrPos)) continue;

                var adjPos = ScaleESPPoint(containerScrPos);
                var boxPt = new SKRect(
                    adjPos.X - _boxHalfSize,
                    adjPos.Y + _boxHalfSize,
                    adjPos.X + _boxHalfSize,
                    adjPos.Y - _boxHalfSize);

                var textPt = new SKPoint(adjPos.X, adjPos.Y + _textOffsetY);

                _aimviewCanvas.DrawRect(boxPt, PaintESPWidgetLoot);
                var label = $"{container.Name} ({dist:n1}m)";
                _aimviewCanvas.DrawText(label, textPt, TextESPWidgetLoot);
            }
        }

        private void DrawPlayers()
        {
            var allPlayers = AllPlayers?
                .Where(x => x.IsActive && x.IsAlive && x is not Tarkov.EFTPlayer.LocalPlayer);

            if (allPlayers == null) return;

            var scaleX = _aimviewBitmap.Width / (float)CameraManagerBase.Viewport.Width;
            var scaleY = _aimviewBitmap.Height / (float)CameraManagerBase.Viewport.Height;

            foreach (var player in allPlayers)
            {
                if (player.Skeleton.UpdateESPWidgetBuffer(scaleX, scaleY))
                {
                    _aimviewCanvas.DrawPoints(
                        SKPointMode.Lines,
                        player.Skeleton.ESPWidgetBuffer.ToArray(),
                        GetPlayerPaint(player)
                    );
                }
            }
        }

        private void DrawCrosshair()
        {
            var bounds = _aimviewBitmap.Info.Rect;
            float centerX = bounds.Left + bounds.Width / 2;
            float centerY = bounds.Top + bounds.Height / 2;

            _aimviewCanvas.DrawLine(bounds.Left, centerY, bounds.Right, centerY, PaintESPWidgetCrosshair);
            _aimviewCanvas.DrawLine(centerX, bounds.Top, centerX, bounds.Bottom, PaintESPWidgetCrosshair);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private SKPoint ScaleESPPoint(SKPoint original)
        {
            var scaleX = _aimviewBitmap.Width / (float)CameraManagerBase.Viewport.Width;
            var scaleY = _aimviewBitmap.Height / (float)CameraManagerBase.Viewport.Height;

            return new SKPoint(original.X * scaleX, original.Y * scaleY);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static SKPaint GetPlayerPaint(Player player)
        {
            if (player.IsAimbotLocked)
                return PaintESPWidgetAimbotLocked;

            if (player.IsFocused)
                return PaintESPWidgetFocused;

            if (player is LocalPlayer)
                return PaintESPWidgetLocalPlayer;

            switch (player.Type)
            {
                case Player.PlayerType.Teammate:
                    return PaintESPWidgetTeammate;
                case Player.PlayerType.USEC:
                    return PaintESPWidgetUSEC;
                case Player.PlayerType.BEAR:
                    return PaintESPWidgetBEAR;
                case Player.PlayerType.AIScav:
                    return PaintESPWidgetScav;
                case Player.PlayerType.AIRaider:
                    return PaintESPWidgetRaider;
                case Player.PlayerType.AIBoss:
                    return PaintESPWidgetBoss;
                case Player.PlayerType.PScav:
                    return PaintESPWidgetPScav;
                case Player.PlayerType.SpecialPlayer:
                    return PaintESPWidgetSpecial;
                case Player.PlayerType.Streamer:
                    return PaintESPWidgetStreamer;
                default:
                    return PaintESPWidgetUSEC;
            }
        }

        public override void SetScaleFactor(float newScale)
        {
            base.SetScaleFactor(newScale);

            lock (PaintESPWidgetCrosshair)
            {
                PaintESPWidgetCrosshair.StrokeWidth = 1 * newScale;
                PaintESPWidgetLocalPlayer.StrokeWidth = 1 * newScale;
                PaintESPWidgetUSEC.StrokeWidth = 1 * newScale;
                PaintESPWidgetBEAR.StrokeWidth = 1 * newScale;
                PaintESPWidgetSpecial.StrokeWidth = 1 * newScale;
                PaintESPWidgetStreamer.StrokeWidth = 1 * newScale;
                PaintESPWidgetTeammate.StrokeWidth = 1 * newScale;
                PaintESPWidgetBoss.StrokeWidth = 1 * newScale;
                PaintESPWidgetAimbotLocked.StrokeWidth = 1 * newScale;
                PaintESPWidgetScav.StrokeWidth = 1 * newScale;
                PaintESPWidgetRaider.StrokeWidth = 1 * newScale;
                PaintESPWidgetPScav.StrokeWidth = 1 * newScale;
                PaintESPWidgetFocused.StrokeWidth = 1 * newScale;
                PaintESPWidgetLoot.StrokeWidth = 0.75f * newScale;
                TextESPWidgetLoot.TextSize = 9f * newScale;
            }
        }

        public override void Dispose()
        {
            _aimviewBitmap?.Dispose();
            _aimviewCanvas?.Dispose();
            _aimviewBitmap = null;
            _aimviewCanvas = null;
            base.Dispose();
        }

        #region Mini ESP Paints
        private static SKPaint PaintESPWidgetCrosshair { get; } = new()
        {
            Color = SKColors.White,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        internal static SKPaint PaintESPWidgetLocalPlayer { get; } = new()
        {
            Color = SKColors.Green,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        internal static SKPaint PaintESPWidgetUSEC { get; } = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        internal static SKPaint PaintESPWidgetBEAR { get; } = new()
        {
            Color = SKColors.Blue,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        internal static SKPaint PaintESPWidgetAimbotLocked { get; } = new()
        {
            Color = SKColors.Blue,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        internal static SKPaint PaintESPWidgetSpecial { get; } = new()
        {
            Color = SKColors.HotPink,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        internal static SKPaint PaintESPWidgetStreamer { get; } = new()
        {
            Color = SKColors.MediumPurple,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        internal static SKPaint PaintESPWidgetTeammate { get; } = new()
        {
            Color = SKColors.LimeGreen,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        internal static SKPaint PaintESPWidgetBoss { get; } = new()
        {
            Color = SKColors.Fuchsia,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        internal static SKPaint PaintESPWidgetScav { get; } = new()
        {
            Color = SKColors.Yellow,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        internal static SKPaint PaintESPWidgetRaider { get; } = new()
        {
            Color = SKColor.Parse("ffc70f"),
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        internal static SKPaint PaintESPWidgetPScav { get; } = new()
        {
            Color = SKColors.White,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        internal static SKPaint PaintESPWidgetFocused { get; } = new()
        {
            Color = SKColors.Coral,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        internal static SKPaint PaintESPWidgetLoot { get; } = new()
        {
            Color = SKColors.WhiteSmoke,
            StrokeWidth = 0.75f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High
        };

        internal static SKPaint TextESPWidgetLoot { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.WhiteSmoke,
            IsStroke = false,
            TextSize = 9f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.High
        };

        #endregion
    }
}