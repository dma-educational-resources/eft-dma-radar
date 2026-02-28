using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static eft_dma_radar.Tarkov.EFTPlayer.Player;

namespace eft_dma_radar.UI.SKWidgetControl
{
    public sealed class PlayerInfoWidget : SKWidget
    {
        private static Config Config => Program.Config;
        private const int COL_NAME  = 25;
        private const int COL_GRP   = 5;
        private const int COL_VALUE = 8;
        private const int COL_HANDS = 30;
        private const int COL_DIST  = 5;
        private const int COL_KD     = 7;
        private const int COL_HOURS  = 7;
        private const int COL_RAIDS  = 7;
        private const int COL_SR     = 6;        
        private readonly float _padding;
        private readonly List<(float TopY, float BottomY, string PlayerName)> _playerRows = new();

        /// <summary>
        /// Constructs a Player Info Overlay.
        /// </summary>
        public PlayerInfoWidget(
            SKGLElement parent,
            SKRect location,
            bool minimized,
            float scale)
            : base(
                parent,
                "Player Info",
                new SKPoint(location.Left, location.Top),
                new SKSize(location.Width, location.Height),
                scale,
                false)
        {
            Minimized = minimized;
            _padding = 2.5f * scale;
            SetScaleFactor(scale);
        }

        public override void Draw(SKCanvas canvas)
        {
            base.Draw(canvas);
        }

        public void Draw(SKCanvas canvas, Player localPlayer, IEnumerable<Player> players)
        {
            if (Minimized)
            {
                base.Draw(canvas);
                return;
            }

            var localPlayerPos = localPlayer.Position;

            var hostiles = players.Where(x => x.IsHostileActive).ToList();
            var hostileCount = hostiles.Count;
            var pmcCount = hostiles.Count(x => x.IsPmc);
            var pscavCount = hostiles.Count(x => x.Type is Player.PlayerType.PScav);
            var aiCount = hostiles.Count(x => x.IsAI && x.Type is not Player.PlayerType.AIBoss);
            var bossCount = hostiles.Count(x => x.Type is Player.PlayerType.AIBoss);

            var filteredPlayers = players
                .Where(x => x.IsHumanHostileActive)
                .OrderBy(x => Vector3.Distance(localPlayerPos, x.Position))
                .ToList();

            _playerRows.Clear();

            var sb = new StringBuilder();

            sb.AppendFormat("{0,-" + COL_NAME  + "}", "Fac/Name/Lvl ")
              .AppendFormat("{0,-" + COL_GRP   + "}", "Grp")
              .AppendFormat("{0,-" + COL_VALUE + "}", "Value")
              .AppendFormat("{0,-" + COL_HANDS + "}", "In Hands")
              .AppendFormat("{0,-" + COL_DIST  + "}", "Dist")
              .AppendFormat("{0,-" + COL_KD    + "}", "K/D")
              .AppendFormat("{0,-" + COL_HOURS + "}", "Hours")
              .AppendFormat("{0,-" + COL_RAIDS + "}", "Raids")
              .AppendFormat("{0,-" + COL_SR    + "}", "S/R%")
              .AppendLine();

            foreach (var player in filteredPlayers)
                AppendPlayerData(sb, player, localPlayerPos);

            var lines = sb
                .ToString()
                .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            var lineSpacing = _textPlayersOverlay.FontSpacing;
            float maxWidth = 0f;

            foreach (var line in lines)
            {
                var width = _textPlayersOverlay.MeasureText(line);
                if (width > maxWidth)
                    maxWidth = width;
            }

            Size = new SKSize(
                maxWidth + _padding * 2,
                lines.Count * lineSpacing + _padding * 1.5f);

            Location = Location;

            Title = "Player Info";
            RightTitleInfo =
                $"Hostiles: {hostileCount} | PMC: {pmcCount} | PScav: {pscavCount} | AI: {aiCount} | Boss: {bossCount}";

            base.Draw(canvas);

            var drawPt = new SKPoint(
                ClientRectangle.Left + _padding,
                ClientRectangle.Top + lineSpacing / 2 + _padding);

            for (int i = 0; i < lines.Count; i++)
            {
                canvas.DrawText(lines[i], drawPt, _textPlayersOverlay);

                if (i > 0 && i < lines.Count - 1)
                {
                    var y = drawPt.Y + lineSpacing * 0.2f;
                    canvas.DrawLine(
                        ClientRectangle.Left + _padding,
                        y,
                        ClientRectangle.Right - _padding,
                        y,
                        _rowSeparatorPaint);
                }

                if (i > 0 && (i - 1) < filteredPlayers.Count)
                {
                    _playerRows.Add((
                        drawPt.Y - lineSpacing,
                        drawPt.Y,
                        filteredPlayers[i - 1].Name));
                }

                drawPt.Y += lineSpacing;
            }

            if (lines.Count > 1)
            {
                var headerY = ClientRectangle.Top
                              + lineSpacing / 2
                              + _padding
                              + lineSpacing * 0.2f;

                canvas.DrawLine(
                    ClientRectangle.Left + _padding,
                    headerY,
                    ClientRectangle.Right - _padding,
                    headerY,
                    _headerSeparatorPaint);
            }
        }

        private void AppendPlayerData(
            StringBuilder sb,
            Player player,
            Vector3 localPlayerPos)
        {
            // -------- NAME / FACTION / LEVEL --------
            string fac =
                player.Type is Player.PlayerType.USEC ? "USEC" :
                player.Type is Player.PlayerType.PScav ? "PScav" :
                player.Type is Player.PlayerType.BEAR ? "BEAR" :
                player.IsAI ? "AI" : "?";

            string name =
                Config.MaskNames && player.IsHuman
                    ? "<Hidden>"
                    : player.Name ?? "--";

            string level = "--";

            string kd = "--";
            string hours = "--";
            string raids = "--";
            string sr = "--";

            if (player is ObservedPlayer op)
            {
                if (op.Profile?.Level is int lvl)
                    level = lvl.ToString();

                if (op.Profile?.Overall_KD is float kdVal)
                    kd = kdVal.ToString("n2");

                if (op.Profile?.Hours is int hrs)
                    hours = hrs.ToString();

                if (op.Profile?.RaidCount is int rc)
                    raids = rc.ToString();

                if (op.Profile?.SurvivedRate is float srVal)
                    sr = srVal.ToString("n1");
            }

            string nameCol = $"{fac}/{name}/L{level} ";

            // -------- GROUP --------
            string group = "--";
            if (player is ObservedPlayer op2)
            {
                if (op2.SpawnGroupID != -1)
                    group = op2.SpawnGroupID.ToString();
                else if (op2.NetworkGroupID != -1)
                    group = op2.NetworkGroupID.ToString();
            }

            // -------- VALUE --------
            string value =
                TarkovMarketItem.FormatPrice(player.Gear?.Value ?? 0);

            // -------- IN HANDS --------
            string inHands =
                $"{player.Hands?.CurrentItem ?? "--"}/{player.Hands?.CurrentAmmo ?? "--"}";

            if (player.Gear?.Equipment != null &&
                player.Gear.Equipment.TryGetValue("SecuredContainer", out var secure))
            {
                inHands += $" | {secure.Short ?? "Secure"}";
            }

            // -------- DISTANCE --------
            int distance =
                (int)Math.Round(Vector3.Distance(localPlayerPos, player.Position));

            // -------- WRITE ROW --------
            sb.AppendFormat("{0,-" + COL_NAME  + "}", nameCol)
              .AppendFormat("{0,-" + COL_GRP   + "}", group)
              .AppendFormat("{0,-" + COL_VALUE + "}", value)
              .AppendFormat("{0,-" + COL_HANDS + "}", inHands)
              .AppendFormat("{0,-" + COL_DIST  + "}", distance)
              .AppendFormat("{0,-" + COL_KD    + "}", kd)
              .AppendFormat("{0,-" + COL_HOURS + "}", hours)
              .AppendFormat("{0,-" + COL_RAIDS + "}", raids)
              .AppendFormat("{0,-" + COL_SR    + "}", sr)
              .AppendLine();
        }


        public override void SetScaleFactor(float newScale)
        {
            base.SetScaleFactor(newScale);

            lock (_textPlayersOverlay)
            {
                _textPlayersOverlay.TextSize = 12 * newScale;
            }

            _rowSeparatorPaint.StrokeWidth = 1.0f * newScale;
            _headerSeparatorPaint.StrokeWidth = 1.5f * newScale;
        }

        private static readonly SKPaint _textPlayersOverlay = new()
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

        private static readonly SKPaint _rowSeparatorPaint = new()
        {
            Color = SKColors.LightGray.WithAlpha(160),
            StrokeWidth = 1.0f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        private static readonly SKPaint _headerSeparatorPaint = new()
        {
            Color = SKColors.LightGray.WithAlpha(200),
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
    }
}
