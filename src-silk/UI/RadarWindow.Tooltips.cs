using eft_dma_radar.Silk.Tarkov;

namespace eft_dma_radar.Silk.UI
{
    internal static partial class RadarWindow
    {
        #region Radar Mouseover Tooltip

        // Reusable list for tooltip lines — avoids per-frame allocation
        private static readonly List<(string text, SKPaint paint)> _tooltipLines = new(16);

        /// <summary>
        /// Draws a SkiaSharp tooltip near the hovered entity on the radar canvas.
        /// </summary>
        private static void DrawMouseoverTooltip(SKCanvas canvas, MapParams mapParams, MapConfig mapConfig, Player localPlayer)
        {
            var hoveredPlayer = _mouseOverPlayer;
            var hoveredLoot = _mouseOverLoot;
            var hoveredCorpse = _mouseOverCorpse;
            var hoveredExfil = _mouseOverExfil;
            var hoveredTransit = _mouseOverTransit;

            if (hoveredPlayer is not null)
            {
                var screenPos = mapParams.ToScreenPos(MapParams.ToMapPos(hoveredPlayer.Position, mapConfig));
                BuildPlayerTooltipLines(hoveredPlayer, localPlayer);
                DrawTooltipBox(canvas, screenPos, _tooltipLines);
            }
            else if (hoveredCorpse is not null)
            {
                var screenPos = mapParams.ToScreenPos(MapParams.ToMapPos(hoveredCorpse.Position, mapConfig));
                BuildCorpseTooltipLines(hoveredCorpse, localPlayer);
                DrawTooltipBox(canvas, screenPos, _tooltipLines);
            }
            else if (hoveredLoot is not null)
            {
                var screenPos = mapParams.ToScreenPos(MapParams.ToMapPos(hoveredLoot.Position, mapConfig));
                BuildLootTooltipLines(hoveredLoot, localPlayer);
                DrawTooltipBox(canvas, screenPos, _tooltipLines);
            }
            else if (hoveredExfil is not null)
            {
                var screenPos = mapParams.ToScreenPos(MapParams.ToMapPos(hoveredExfil.Position, mapConfig));
                BuildExfilTooltipLines(hoveredExfil, localPlayer);
                DrawTooltipBox(canvas, screenPos, _tooltipLines);
            }
            else if (hoveredTransit is not null)
            {
                var screenPos = mapParams.ToScreenPos(MapParams.ToMapPos(hoveredTransit.Position, mapConfig));
                BuildTransitTooltipLines(hoveredTransit, localPlayer);
                DrawTooltipBox(canvas, screenPos, _tooltipLines);
            }
        }

        private static void BuildPlayerTooltipLines(Player player, Player localPlayer)
        {
            _tooltipLines.Clear();
            var textPaint = player.TextPaint;
            int dist = (int)Vector3.Distance(localPlayer.Position, player.Position);

            // Name + faction
            string faction = player.Type switch
            {
                PlayerType.USEC => "USEC",
                PlayerType.BEAR => "BEAR",
                PlayerType.PScav => "PScav",
                PlayerType.SpecialPlayer => "Special",
                PlayerType.Streamer => "Streamer",
                _ => "?"
            };

            string namePrefix = player.Level > 0 ? $"Lvl {player.Level} " : "";
            _tooltipLines.Add(($"{faction}: {namePrefix}{player.Name}", textPaint));

            // Profile stats (K/D, hours, survival rate)
            if (player.Profile is { HasData: true } prof)
            {
                _tooltipLines.Add(($"K/D: {prof.KD:F1}  Raids: {prof.Sessions}  SR: {prof.SurvivedRate:F0}%  Hrs: {prof.Hours}  {prof.AccountType}", SKPaints.TooltipLabel));
            }
            else if (player.AccountId is not null && player.Profile is null
                     && ProfileService.TryGetProfile(player.AccountId, out var fetchedProfile)
                     && fetchedProfile.HasData)
            {
                player.Profile = fetchedProfile;
                _tooltipLines.Add(($"K/D: {fetchedProfile.KD:F1}  Raids: {fetchedProfile.Sessions}  SR: {fetchedProfile.SurvivedRate:F0}%  Hrs: {fetchedProfile.Hours}  {fetchedProfile.AccountType}", SKPaints.TooltipLabel));
            }

            // Group
            if (player.SpawnGroupID != -1)
                _tooltipLines.Add(($"Group: {player.SpawnGroupID}", SKPaints.TooltipText));

            // Health status (only show if not Healthy)
            if (player.HealthStatus != EHealthStatus.Healthy)
            {
                string healthLabel = player.HealthStatus switch
                {
                    EHealthStatus.Dying => "Dying",
                    EHealthStatus.BadlyInjured => "Badly Injured",
                    EHealthStatus.Injured => "Injured",
                    _ => "Healthy",
                };
                _tooltipLines.Add(($"Health: {healthLabel}", SKPaints.TooltipAccent));
            }

            // Distance
            _tooltipLines.Add(($"Distance: {dist}m", SKPaints.TooltipLabel));

            // In hands
            if (player.HandsReady && player.InHandsItem is not null)
            {
                string handsText = player.InHandsAmmo is not null
                    ? $"Hands: {player.InHandsItem} ({player.InHandsAmmo})"
                    : $"Hands: {player.InHandsItem}";
                _tooltipLines.Add((handsText, SKPaints.TooltipText));
            }

            // Gear summary
            if (player.GearReady)
            {
                if (player.GearValue > 0)
                    _tooltipLines.Add(($"Value: {LootFilter.FormatPrice(player.GearValue)}", SKPaints.TooltipAccent));

                if (player.HasThermal && player.HasNVG)
                    _tooltipLines.Add(("Thermal + NVG", SKPaints.TooltipAccent));
                else if (player.HasThermal)
                    _tooltipLines.Add(("Thermal", SKPaints.TooltipAccent));
                else if (player.HasNVG)
                    _tooltipLines.Add(("NVG", SKPaints.TooltipAccent));

                // Equipment list — compact
                foreach (var kvp in player.Equipment)
                {
                    string price = kvp.Value.Price > 0 ? $" ({LootFilter.FormatPrice(kvp.Value.Price)})" : "";
                    _tooltipLines.Add(($"  {kvp.Value.Short}{price}", SKPaints.TooltipText));
                }
            }
        }

        private static void BuildLootTooltipLines(LootItem loot, Player localPlayer)
        {
            _tooltipLines.Clear();
            int dist = (int)Vector3.Distance(localPlayer.Position, loot.Position);
            var filterData = LootFilter.FilterData;
            bool wishlisted = filterData.IsWishlisted(loot.Id);
            var paint = wishlisted ? SKPaints.TooltipWishlist
                      : loot.IsImportant ? SKPaints.TooltipAccent
                      : SKPaints.TooltipText;

            _tooltipLines.Add((loot.Name, paint));

            if (wishlisted)
                _tooltipLines.Add(("\u2605 Wishlisted", SKPaints.TooltipWishlist));

            if (loot.DisplayPrice > 0)
                _tooltipLines.Add(($"Price: {LootFilter.FormatPrice(loot.DisplayPrice)}", SKPaints.TooltipAccent));

            _tooltipLines.Add(($"Distance: {dist}m", SKPaints.TooltipLabel));

            // Warn if loot is far below the player (likely under the map / inaccessible)
            if (loot.Position.Y < localPlayer.Position.Y - 15f)
                _tooltipLines.Add(("Under map (inaccessible)", SKPaints.TooltipLabel));
        }

        private static void BuildCorpseTooltipLines(LootCorpse corpse, Player localPlayer)
        {
            _tooltipLines.Clear();
            int dist = (int)Vector3.Distance(localPlayer.Position, corpse.Position);

            _tooltipLines.Add((corpse.Name, SKPaints.TextCorpse));

            if (corpse.TotalValue > 0)
                _tooltipLines.Add(($"Value: {LootFilter.FormatPrice(corpse.TotalValue)}", SKPaints.TooltipAccent));

            _tooltipLines.Add(($"Distance: {dist}m", SKPaints.TooltipLabel));

            if (corpse.GearReady && corpse.Equipment.Count > 0)
            {
                foreach (var kvp in corpse.Equipment)
                {
                    string price = kvp.Value.Price > 0 ? $" ({LootFilter.FormatPrice(kvp.Value.Price)})" : "";
                    _tooltipLines.Add(($"  {kvp.Value.ShortName}{price}", SKPaints.TooltipText));
                }
            }
        }

        private static void BuildExfilTooltipLines(Exfil exfil, Player localPlayer)
        {
            _tooltipLines.Clear();
            int dist = (int)Vector3.Distance(localPlayer.Position, exfil.Position);

            // Name colored by status
            var (_, textPaint) = exfil.Status switch
            {
                ExfilStatus.Open => (SKPaints.PaintExfilOpen, SKPaints.TextExfilOpen),
                ExfilStatus.Pending => (SKPaints.PaintExfilPending, SKPaints.TextExfilPending),
                _ => (SKPaints.PaintExfilClosed, SKPaints.TextExfilClosed),
            };

            _tooltipLines.Add((exfil.Name, textPaint));

            // Status
            string statusText = exfil.Status switch
            {
                ExfilStatus.Open => "Open",
                ExfilStatus.Pending => "Pending",
                _ => "Closed",
            };
            _tooltipLines.Add(($"Status: {statusText}", SKPaints.TooltipLabel));

            // Distance
            _tooltipLines.Add(($"Distance: {dist}m", SKPaints.TooltipLabel));

            // Availability for local player
            if (localPlayer is LocalPlayer lp)
            {
                if (!exfil.IsAvailableFor(lp))
                    _tooltipLines.Add(("Not available", SKPaints.TextExfilInactive));
            }
        }

        private static void BuildTransitTooltipLines(TransitPoint transit, Player localPlayer)
        {
            _tooltipLines.Clear();
            int dist = (int)Vector3.Distance(localPlayer.Position, transit.Position);

            _tooltipLines.Add((transit.Name, SKPaints.TextTransit));
            _tooltipLines.Add(($"Status: {(transit.IsActive ? "Active" : "Inactive")}", SKPaints.TooltipLabel));
            _tooltipLines.Add(($"Distance: {dist}m", SKPaints.TooltipLabel));
        }

        /// <summary>
        /// Draws a rounded-rect tooltip box at an entity screen position, clamped to canvas bounds.
        /// </summary>
        private static void DrawTooltipBox(SKCanvas canvas, SKPoint anchor, List<(string text, SKPaint paint)> lines)
        {
            if (lines.Count == 0)
                return;

            const float padX = 6f;
            const float padY = 4f;
            const float lineH = 13f;
            const float offsetX = 14f;
            const float offsetY = -6f;
            const float cornerRadius = 4f;
            const float margin = 4f;

            // Measure max line width
            float maxWidth = 0;
            foreach (var (text, paint) in lines)
            {
                float w = SKPaints.FontTooltip.MeasureText(text, paint);
                if (w > maxWidth) maxWidth = w;
            }

            float boxW = maxWidth + padX * 2;
            float boxH = lines.Count * lineH + padY * 2;

            float left = anchor.X + offsetX;
            float top = anchor.Y + offsetY;

            // Clamp to canvas bounds
            float canvasW = _window.Size.X;
            float canvasH = _window.Size.Y;

            if (left + boxW > canvasW - margin)
                left = anchor.X - offsetX - boxW;
            if (left < margin)
                left = margin;
            if (top + boxH > canvasH - margin)
                top = canvasH - margin - boxH;
            if (top < margin)
                top = margin;

            var rect = new SKRect(left, top, left + boxW, top + boxH);

            canvas.DrawRoundRect(rect, cornerRadius, cornerRadius, SKPaints.TooltipBackground);
            canvas.DrawRoundRect(rect, cornerRadius, cornerRadius, SKPaints.TooltipBorder);

            float textX = rect.Left + padX;
            float textY = rect.Top + padY + SKPaints.FontTooltip.Size;

            foreach (var (text, paint) in lines)
            {
                canvas.DrawText(text, textX, textY, SKPaints.FontTooltip, paint);
                textY += lineH;
            }
        }

        #endregion
    }
}
