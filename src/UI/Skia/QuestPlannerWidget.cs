using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov.MissionPlanner;
using eft_dma_radar.Tarkov.MissionPlanner.Models;
using eft_dma_radar.UI.Misc;
using SkiaSharp;
using SkiaSharp.Views.WPF;

namespace eft_dma_radar.UI.SKWidgetControl
{
    public sealed class QuestPlannerWidget : SKWidget
    {
        private static Config Config => Program.Config;
        private readonly float _padding;

        public QuestPlannerWidget(SKGLElement parent, SKRect location, bool minimized, float scale)
            : base(parent, "Quest Planner", new SKPoint(location.Left, location.Top),
                new SKSize(location.Width, location.Height), scale, true)
        {
            Minimized = minimized;
            _padding = 2f * scale;
            SetScaleFactor(scale);
        }

        public override void SetScaleFactor(float scale)
        {
            base.SetScaleFactor(scale);
            lock (_textPaint) { _textPaint.TextSize = 12 * scale; }
            lock (_dimTextPaint) { _dimTextPaint.TextSize = 11 * scale; }
            lock (_greenPaint) { _greenPaint.TextSize = 11 * scale; }
            lock (_yellowPaint) { _yellowPaint.TextSize = 11 * scale; }
        }

        public override void Draw(SKCanvas canvas)
        {
            base.Draw(canvas);
            if (Minimized) return;

            canvas.Save();
            canvas.ClipRect(ClientRectangle);

            var lineSpacing = _textPaint.FontSpacing;
            var drawPt = new SKPoint(
                ClientRectangle.Left + _padding,
                ClientRectangle.Top + lineSpacing * 0.8f + _padding);

            var summary = MissionPlannerService.Current;
            var state = MissionPlannerService.State;

            if (summary == null || state == MissionConnectionState.Disconnected)
            {
                canvas.DrawText("No plan available", drawPt, _dimTextPaint);
            }
            else
            {
                // Line 1: totals
                canvas.DrawText(
                    $"Quests: {summary.TotalActiveQuests}  Objectives: {summary.TotalCompletableObjectives}",
                    drawPt, _textPaint);
                drawPt.Y += lineSpacing;

                // Line 2: next map
                var nextLine = summary.Maps.Count > 0
                    ? $"Next: {summary.Maps[0].MapName} ({summary.Maps[0].CompletableObjectiveCount} obj)"
                    : "Next: \u2014";
                canvas.DrawText(nextLine, drawPt, _textPaint);
                drawPt.Y += lineSpacing;

                // Line 3: bring count
                var bringLine = summary.Maps.Count > 0
                    ? $"Bring: {summary.Maps[0].BringList.Count} items"
                    : "Bring: \u2014";
                canvas.DrawText(bringLine, drawPt, _textPaint);
                drawPt.Y += lineSpacing;

                // Line 4: connection state
                if (state == MissionConnectionState.Lobby)
                    canvas.DrawText("[Lobby]", drawPt, _greenPaint);
                else if (state == MissionConnectionState.InRaid)
                    canvas.DrawText("[In Raid]", drawPt, _yellowPaint);
            }

            canvas.Restore();
        }

        private static readonly SKPaint _textPaint = new()
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

        private static readonly SKPaint _dimTextPaint = new()
        {
            SubpixelText = true,
            Color = SKColors.LightGray,
            IsStroke = false,
            TextSize = 11,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Consolas"),
            FilterQuality = SKFilterQuality.High
        };

        private static readonly SKPaint _greenPaint = new()
        {
            SubpixelText = true,
            Color = SKColors.LimeGreen,
            IsStroke = false,
            TextSize = 11,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Consolas"),
            FilterQuality = SKFilterQuality.High
        };

        private static readonly SKPaint _yellowPaint = new()
        {
            SubpixelText = true,
            Color = SKColors.Yellow,
            IsStroke = false,
            TextSize = 11,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Consolas"),
            FilterQuality = SKFilterQuality.High
        };
    }
}
