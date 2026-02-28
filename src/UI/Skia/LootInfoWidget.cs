using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace eft_dma_radar.UI.SKWidgetControl
{
    public sealed class LootInfoWidget : SKWidget
    {
        private readonly List<(float TopY, float BottomY, string ItemName)> _itemRows = new List<(float, float, string)>();

        /// <summary>
        /// Constructs a Loot Info Overlay.
        /// </summary>
        public LootInfoWidget(SKGLElement parent, SKRect location, bool minimized, float scale)
            : base(parent, "Loot Info", new SKPoint(location.Left, location.Top),
                new SKSize(location.Width, location.Height), scale, false)
        {
            Minimized = minimized;
            SetScaleFactor(scale);
        }

        internal static SKPaint TextLootOverlay { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.White,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Consolas"), // Do NOT change this font
            FilterQuality = SKFilterQuality.High
        };

        /// <summary>
        /// Represents a grouped loot item with quantity and total value
        /// </summary>
        private class GroupedLootItem
        {
            public string Name { get; set; }
            public int Quantity { get; set; }
            public int PricePerItem { get; set; }
            public int TotalValue => PricePerItem * Quantity;

            public GroupedLootItem(string name, int price)
            {
                Name = name;
                PricePerItem = price;
                Quantity = 1;
            }
        }

        public void Draw(SKCanvas canvas, IEnumerable<LootItem> lootItems)
        {
            if (Minimized)
            {
                base.Draw(canvas);
                return;
            }

            _itemRows.Clear();

            var groupedLoot = new Dictionary<string, GroupedLootItem>();
            var totalLootCount = 0;
            var totalLootValue = 0;

            if (lootItems != null)
            {
                foreach (var item in lootItems)
                {
                    if (item is LootCorpse || item is StaticLootContainer || item is QuestItem)
                        continue;

                    totalLootCount++;
                    totalLootValue += item.Price;

                    var itemName = item.Name?.Trim() ?? "Unknown Item";

                    if (groupedLoot.TryGetValue(itemName, out var existingItem))
                    {
                        existingItem.Quantity++;
                    }
                    else
                    {
                        groupedLoot[itemName] = new GroupedLootItem(itemName, item.Price);
                    }
                }
            }

            var sortedLoot = groupedLoot.Values
                .Where(x => x.PricePerItem >= 1000)
                .OrderByDescending(x => x.PricePerItem)
                .Take(15)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendFormat("{0,-30} {1,-10} {2,-5}", "Item Name", "Price", "Qty")
                .AppendLine();

            foreach (var item in sortedLoot)
            {
                var displayName = item.Name;
                if (displayName.Length > 27)
                    displayName = displayName.Substring(0, 24) + "...";

                sb.AppendFormat("{0,-30} {1,-10} {2,-5}",
                    displayName,
                    TarkovMarketItem.FormatPrice(item.PricePerItem),
                    item.Quantity)
                  .AppendLine();
            }

            var data = sb.ToString().Split(Environment.NewLine);
            var lineSpacing = TextLootOverlay.FontSpacing;
            var maxLength = 0f;
            foreach (var line in data)
            {
                var lineLength = TextLootOverlay.MeasureText(line);
                if (lineLength > maxLength)
                    maxLength = lineLength;
            }

            var pad = 2.5f * ScaleFactor;
            Size = new SKSize(maxLength + pad * 2, data.Length * lineSpacing + pad * 2);
            Location = Location; // Bounds check

            base.Draw(canvas);

            var drawPt = new SKPoint(ClientRectangle.Left + pad, ClientRectangle.Top + lineSpacing / 2 + pad);
            canvas.DrawText($"Total Loot: {totalLootCount} items (Value: {TarkovMarketItem.FormatPrice(totalLootValue)})",
                drawPt, TextLootOverlay);
            drawPt.Y += lineSpacing;

            var itemIndex = 0;
            foreach (var line in data)
            {
                if (string.IsNullOrEmpty(line?.Trim()))
                    continue;

                canvas.DrawText(line, drawPt, TextLootOverlay);

                if (itemIndex > 0 && itemIndex - 1 < sortedLoot.Count)
                {
                    var topY = drawPt.Y - lineSpacing;
                    var bottomY = drawPt.Y;
                    var itemName = sortedLoot[itemIndex - 1].Name;

                    _itemRows.Add((topY, bottomY, itemName));
                }

                drawPt.Y += lineSpacing;
                itemIndex++;
            }
        }

        /// <summary>
        /// Handle client area click in this widget
        /// </summary>
        public override bool HandleClientAreaClick(SKPoint point)
        {
            foreach (var row in _itemRows)
            {
                if (point.Y >= row.TopY && point.Y <= row.BottomY)
                {
                    MainWindow.PingItem(row.ItemName);
                    return true;
                }
            }

            return false;
        }

        public override void SetScaleFactor(float newScale)
        {
            base.SetScaleFactor(newScale);
            TextLootOverlay.TextSize = 12 * newScale;
        }
    }
}