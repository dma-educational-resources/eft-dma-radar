using SkiaSharp;
using SkiaSharp.Views.WPF;
using System;
using System.Linq;
using eft_dma_radar.Common.Misc;

namespace eft_dma_radar.UI.SKWidgetControl
{
    public sealed class DebugInfoWidget : SKWidget
    {
        private int _displayedFps = 0;
        private readonly float _textPadding;
        private bool _inputManagerInitialized => InputManager.IsReady;
        private int _lootCount => Memory.Loot?.UnfilteredLoot.Count ?? 0;
        private int _playerCount => Memory.Players?.Count ?? 0;
        private string _aimbotTarget => Memory.Players?.FirstOrDefault(p => p.IsAimbotLocked)?.Name ?? "None";

        public DebugInfoWidget(SKGLElement parent, SKRect location, bool minimized, float scale)
            : base(parent, "Debug Info", new SKPoint(location.Left, location.Top),
                new SKSize(location.Width, location.Height), scale, false)
        {
            Minimized = minimized;
            _textPadding = 8 * scale;
            SetScaleFactor(scale);
        }

        public override void Draw(SKCanvas canvas)
        {
            if (Minimized)
            {
                base.Draw(canvas);
                return;
            }

            var lineSpacing = _debugTextPaint.FontSpacing;
            var textLines = new[]
            {
                $"Radar FPS: {_displayedFps}",
                $"InputManager: {_inputManagerInitialized}",
                $"Loot: {_lootCount}",
                $"Players: {_playerCount}",
                $"AimbotTarget: {_aimbotTarget}"
            };

            var maxTextWidth = textLines.Max(text => _debugTextPaint.MeasureText(text));

            Size = new SKSize(maxTextWidth + _textPadding * 2, (lineSpacing * textLines.Length) + _textPadding * 2);
            Location = Location;
            base.Draw(canvas);

            for (int i = 0; i < textLines.Length; i++)
            {
                var textPoint = new SKPoint(
                    ClientRectangle.Left + _textPadding,
                    ClientRectangle.Top + lineSpacing * (i + 0.5f) + _textPadding);
                canvas.DrawText(textLines[i], textPoint, _debugTextPaint);
            }
        }

        public void UpdateFps(int fps)
        {
            _displayedFps = fps;
        }

        public override void SetScaleFactor(float newScale)
        {
            base.SetScaleFactor(newScale);
            lock (_debugTextPaint)
            {
                _debugTextPaint.TextSize = 12 * newScale;
            }
        }

        private static readonly SKPaint _debugTextPaint = new()
        {
            SubpixelText = true,
            Color = SKColors.White,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Consolas"),
            FilterQuality = SKFilterQuality.High
        };
    }
}
