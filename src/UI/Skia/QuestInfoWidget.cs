using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace eft_dma_radar.UI.SKWidgetControl
{
    public sealed class QuestInfoWidget : SKWidget
    {
        private static Config Config => Program.Config;
        private readonly float _padding;
        private readonly Dictionary<string, bool> _collapsedQuests = new();
        private bool _showKeys = false;
        private bool _showRequiredItems = false;
        private bool _hideCompleted = false;
        private bool _showOtherQuests = false;

        /// <summary>
        /// Constructs a Quest Info Widget.
        /// </summary>
        public QuestInfoWidget(SKGLElement parent, SKRect location, bool minimized, float scale)
            : base(parent, "Quest Info", new SKPoint(location.Left, location.Top),
                new SKSize(location.Width, location.Height), scale, true)
        {
            Minimized = minimized;
            _padding = 2f * scale;
            SetScaleFactor(scale);
        }

        public override void SetScaleFactor(float scale)
        {
            base.SetScaleFactor(scale);

            lock (_questTextPaint)
            {
                _questTextPaint.TextSize = 12 * scale;
            }

            lock (_questKeyPaint)
            {
                _questKeyPaint.TextSize = 12 * scale;
            }

            lock (_questSeparatorPaint)
            {
                _questSeparatorPaint.TextSize = 12 * scale;
            }

            lock (_questCompletedPaint)
            {
                _questCompletedPaint.TextSize = 12 * scale;
            }

            lock (_questIncompletePaint)
            {
                _questIncompletePaint.TextSize = 12 * scale;
            }

            lock (_questNamePaint)
            {
                _questNamePaint.TextSize = 12 * scale;
            }

            lock (_questItemPaint)
            {
                _questItemPaint.TextSize = 12 * scale;
            }

            lock (_questOptionalPaint)
            {
                _questOptionalPaint.TextSize = 12 * scale;
            }

            lock (_questStrikethroughPaint)
            {
                _questStrikethroughPaint.StrokeWidth = 1.2f * scale;
            }
        }

        public override void Draw(SKCanvas canvas)
        {
            base.Draw(canvas);

            if (Minimized)
                return;

            if (!Config.QuestHelper.Enabled)
            {
                canvas.Save();
                canvas.ClipRect(ClientRectangle);

                var disabledLineSpacing = _questTextPaint.FontSpacing;
                var disabledDrawPt = new SKPoint(ClientRectangle.Left + _padding, ClientRectangle.Top + disabledLineSpacing * 0.8f + _padding);

                var disabledText = "Quest Helper not enabled";
                canvas.DrawText(disabledText, disabledDrawPt, _questTextPaint);

                canvas.Restore();
                return;
            }

            var questManager = Memory.QuestManager;

            if (questManager == null)
                return;

            var allCurrentMapQuests = questManager.GetQuestsForCurrentMap().ToList();
            var currentMapQuests = Config.QuestHelper.KappaFilter ? allCurrentMapQuests.Where(q => q.KappaRequired).ToList() : allCurrentMapQuests;

            var allOtherQuests = _showOtherQuests ? questManager.GetOtherQuests().ToList() : new List<Quest>();
            var otherQuests = Config.QuestHelper.KappaFilter ? allOtherQuests.Where(q => q.KappaRequired).ToList() : allOtherQuests;

            var textData = new List<(string text, bool isCompleted, bool isObjective)>();

            var keyFilterSymbol = _showKeys ? "[x]" : "[ ]";
            var itemFilterSymbol = _showRequiredItems ? "[x]" : "[ ]";
            var otherQuestsSymbol = _showOtherQuests ? "[x]" : "[ ]";
            var hideCompletedSymbol = _hideCompleted ? "[x]" : "[ ]";
            textData.Add(($"Filters: {keyFilterSymbol} Keys  {itemFilterSymbol} Items  {otherQuestsSymbol} Other Quests  {hideCompletedSymbol} Hide Completed", false, false));

            var mapName = GetMapDisplayName(GetCurrentMapId());
            textData.Add(($"Active Quests on {mapName}:", false, false));

            foreach (var quest in currentMapQuests)
            {
                AddQuestToDisplay(quest, textData);
            }

            if (_showOtherQuests && otherQuests.Any())
            {
                textData.Add(("", false, false));
                textData.Add(("Other Quests:", false, false));

                foreach (var quest in otherQuests)
                {
                    AddQuestToDisplay(quest, textData);
                }
            }

            canvas.Save();
            canvas.ClipRect(ClientRectangle);

            var lineSpacing = _questTextPaint.FontSpacing;
            var drawPt = new SKPoint(ClientRectangle.Left + _padding, ClientRectangle.Top + lineSpacing * 0.8f + _padding);

            foreach (var (text, isCompleted, isObjective) in textData)
            {
                if (drawPt.Y > ClientRectangle.Bottom - _padding)
                    break;

                SKPaint paint;
                var useStrikethrough = false;

                if (isObjective)
                {
                    if (text.Contains("[Optional]"))
                    {
                        paint = isCompleted ? _questCompletedPaint : _questOptionalPaint;
                        useStrikethrough = isCompleted;
                    }
                    else
                    {
                        paint = isCompleted ? _questCompletedPaint : _questIncompletePaint;
                        useStrikethrough = isCompleted;
                    }
                }
                else if (text.TrimStart().StartsWith("Key:"))
                {
                    paint = _questKeyPaint;
                }
                else if (text.TrimStart().StartsWith("Item:"))
                {
                    paint = _questItemPaint;
                }
                else if (text.TrimStart().StartsWith("[-]") || text.TrimStart().StartsWith("[+]"))
                {
                    paint = _questNamePaint;
                }
                else
                {
                    paint = _questTextPaint;
                }

                DrawTextWithStrikethrough(canvas, text, drawPt, paint, useStrikethrough);

                drawPt.Y += lineSpacing;
            }

            canvas.Restore();
        }

        private void AddQuestToDisplay(Quest quest, List<(string text, bool isCompleted, bool isObjective)> textData)
        {
            var isCollapsed = _collapsedQuests.GetValueOrDefault(quest.Id, false);
            var collapseSymbol = isCollapsed ? "[+]" : "[-]";
            var maxQuestNameLength = GetDynamicMaxLength(30);

            var questLine = $"{collapseSymbol} {TruncateString(quest.Name, maxQuestNameLength)}";
            textData.Add((questLine, false, false));

            if (!isCollapsed)
            {
                if (EftDataManager.TaskData.TryGetValue(quest.Id, out var taskData) && taskData.Objectives != null)
                {
                    for (int i = 0; i < taskData.Objectives.Count; i++)
                    {
                        var objective = taskData.Objectives[i];
                        var maxObjectiveLength = GetDynamicMaxLength(45);
                        var isCompleted = quest.CompletedConditions.Contains(objective.Id);
                        var completionSymbol = isCompleted ? "✓" : "○";
                        var description = objective.Description ?? "Complete objective";

                        QuestObjective parsedObjective = null;
                        if (i < quest.Objectives.Count)
                            parsedObjective = quest.Objectives[i];

                        var isOptional = objective.Optional || (parsedObjective?.Optional == true);

                        if (isOptional && !Config.QuestHelper.OptionalTaskFilter)
                            continue;

                        if (_hideCompleted && isCompleted)
                            continue;

                        var optionalPrefix = (isOptional && Config.QuestHelper.OptionalTaskFilter) ? "[Optional] " : "";
                        var objectiveText = $"  {completionSymbol} {optionalPrefix}{TruncateString(description, maxObjectiveLength - 4)}";
                        textData.Add((objectiveText, isCompleted, true));

                        if (_showKeys && !isCompleted && !isOptional && objective.RequiredKeys != null)
                        {
                            foreach (var keyGroup in objective.RequiredKeys)
                            {
                                foreach (var key in keyGroup)
                                {
                                    var maxKeyLength = GetDynamicMaxLength(40);
                                    var keyText = $"    Key: {TruncateString(key.Name ?? "Unknown Key", maxKeyLength)}";
                                    textData.Add((keyText, false, false));
                                }
                            }
                        }

                        if (_showRequiredItems && !isCompleted && !isOptional)
                        {
                            if (parsedObjective != null && parsedObjective.HasItemRequirement)
                            {
                                foreach (var itemId in parsedObjective.RequiredItemIds)
                                {
                                    var itemName = GetItemName(itemId);
                                    var maxItemLength = GetDynamicMaxLength(40);
                                    var itemText = $"    Item: {TruncateString(itemName, maxItemLength)}";
                                    textData.Add((itemText, false, false));
                                }
                            }
                        }
                    }
                }
                else
                {
                    foreach (var objective in quest.Objectives)
                    {
                        if (objective.Optional && !Config.QuestHelper.OptionalTaskFilter)
                            continue;

                        if (_hideCompleted && objective.IsCompleted)
                            continue;

                        var maxObjectiveLength = GetDynamicMaxLength(45);
                        var completionSymbol = objective.IsCompleted ? "✓" : "○";

                        var optionalPrefix = (objective.Optional && Config.QuestHelper.OptionalTaskFilter) ? "[Optional] " : "";
                        var objectiveText = $"  {completionSymbol} {optionalPrefix}{TruncateString(objective.Description, maxObjectiveLength - 4)}";

                        textData.Add((objectiveText, objective.IsCompleted, true));

                        if (_showRequiredItems && !objective.IsCompleted && !objective.Optional && objective.HasItemRequirement)
                        {
                            foreach (var itemId in objective.RequiredItemIds)
                            {
                                var itemName = GetItemName(itemId);
                                var maxItemLength = GetDynamicMaxLength(40);
                                var itemText = $"    Item: {TruncateString(itemName, maxItemLength)}";
                                textData.Add((itemText, false, false));
                            }
                        }
                    }
                }
            }
        }

        private string GetItemName(string itemId)
        {
            if (EftDataManager.AllItems.TryGetValue(itemId, out var item))
                return item.ShortName ?? item.Name ?? "Unknown Item";

            return "Unknown Item";
        }

        private void DrawTextWithStrikethrough(SKCanvas canvas, string text, SKPoint point, SKPaint textPaint, bool strikethrough)
        {
            canvas.DrawText(text, point, textPaint);

            if (strikethrough)
            {
                var textWidth = textPaint.MeasureText(text);
                var strikethroughY = point.Y - (textPaint.TextSize * 0.3f);

                canvas.DrawLine(
                    point.X,
                    strikethroughY,
                    point.X + textWidth,
                    strikethroughY,
                    _questStrikethroughPaint
                );
            }
        }

        public override bool HandleClientAreaClick(SKPoint point)
        {
            var lineSpacing = _questTextPaint.FontSpacing;
            var startY = ClientRectangle.Top + lineSpacing * 0.8f + _padding;
            var filterLineY = startY;

            if (point.Y >= filterLineY - lineSpacing / 2 && point.Y <= filterLineY + lineSpacing / 2)
            {
                var startX = ClientRectangle.Left + _padding;
                var currentX = startX;

                var filtersText = "Filters: ";
                var filtersWidth = _questTextPaint.MeasureText(filtersText);
                currentX += filtersWidth;

                var keysCheckbox = _showKeys ? "[x] Keys  " : "[ ] Keys  ";
                var keysWidth = _questTextPaint.MeasureText(keysCheckbox);
                if (point.X >= currentX && point.X <= currentX + keysWidth)
                {
                    _showKeys = !_showKeys;
                    return true;
                }
                currentX += keysWidth;

                var itemsCheckbox = _showRequiredItems ? "[x] Items  " : "[ ] Items  ";
                var itemsWidth = _questTextPaint.MeasureText(itemsCheckbox);
                if (point.X >= currentX && point.X <= currentX + itemsWidth)
                {
                    _showRequiredItems = !_showRequiredItems;
                    return true;
                }
                currentX += itemsWidth;

                var otherQuestsCheckbox = _showOtherQuests ? "[x] Other Quests  " : "[ ] Other Quests  ";
                var otherQuestsWidth = _questTextPaint.MeasureText(otherQuestsCheckbox);
                if (point.X >= currentX && point.X <= currentX + otherQuestsWidth)
                {
                    _showOtherQuests = !_showOtherQuests;
                    return true;
                }
                currentX += otherQuestsWidth;

                var hideCompletedCheckbox = _hideCompleted ? "[x] Hide Completed" : "[ ] Hide Completed";
                var hideCompletedWidth = _questTextPaint.MeasureText(hideCompletedCheckbox);
                if (point.X >= currentX && point.X <= currentX + hideCompletedWidth)
                {
                    _hideCompleted = !_hideCompleted;
                    return true;
                }
            }

            var questManager = Memory.QuestManager;
            if (questManager == null) return false;

            var currentY = startY + (lineSpacing * 3);

            var allCurrentMapQuests = questManager.GetQuestsForCurrentMap().ToList();
            var currentMapQuests = Config.QuestHelper.KappaFilter ? allCurrentMapQuests.Where(q => q.KappaRequired).ToList() : allCurrentMapQuests;

            var allOtherQuests = _showOtherQuests ? questManager.GetOtherQuests().ToList() : new List<Quest>();
            var otherQuests = Config.QuestHelper.KappaFilter ? allOtherQuests.Where(q => q.KappaRequired).ToList() : allOtherQuests;

            foreach (var quest in currentMapQuests)
            {
                if (point.Y >= currentY - lineSpacing / 2 && point.Y <= currentY + lineSpacing / 2)
                {
                    var collapseSymbol = _collapsedQuests.GetValueOrDefault(quest.Id, false) ? "[+] " : "[-] ";
                    var collapseWidth = _questTextPaint.MeasureText(collapseSymbol);

                    if (point.X >= ClientRectangle.Left + _padding && point.X <= ClientRectangle.Left + _padding + collapseWidth)
                    {
                        _collapsedQuests[quest.Id] = !_collapsedQuests.GetValueOrDefault(quest.Id, false);
                        return true;
                    }
                }

                currentY += lineSpacing;

                if (!_collapsedQuests.GetValueOrDefault(quest.Id, false))
                {
                    if (EftDataManager.TaskData.TryGetValue(quest.Id, out var taskData) && taskData.Objectives != null)
                    {
                        for (int i = 0; i < taskData.Objectives.Count; i++)
                        {
                            var objective = taskData.Objectives[i];

                            QuestObjective parsedObjective = null;
                            if (i < quest.Objectives.Count)
                                parsedObjective = quest.Objectives[i];

                            var isOptional = objective.Optional || (parsedObjective?.Optional == true);
                            var isCompleted = quest.CompletedConditions.Contains(objective.Id);

                            if (isOptional && !Config.QuestHelper.OptionalTaskFilter)
                                continue;

                            if (_hideCompleted && isCompleted)
                                continue;

                            currentY += lineSpacing;

                            if (_showKeys && !isCompleted && !isOptional && objective.RequiredKeys != null)
                            {
                                var keyCount = objective.RequiredKeys.Sum(keyGroup => keyGroup.Count);
                                currentY += lineSpacing * keyCount;
                            }

                            if (_showRequiredItems && !isCompleted && !isOptional)
                                if (parsedObjective != null && parsedObjective.HasItemRequirement)
                                    currentY += lineSpacing * parsedObjective.RequiredItemIds.Count;
                        }
                    }
                    else
                    {
                        foreach (var objective in quest.Objectives)
                        {
                            if (objective.Optional && !Config.QuestHelper.OptionalTaskFilter)
                                continue;

                            if (_hideCompleted && objective.IsCompleted)
                                continue;

                            currentY += lineSpacing;

                            if (_showRequiredItems && !objective.IsCompleted && !objective.Optional && objective.HasItemRequirement)
                                currentY += lineSpacing * objective.RequiredItemIds.Count;
                        }
                    }
                }
            }

            if (_showOtherQuests && otherQuests.Any())
            {
                currentY += lineSpacing * 2;

                foreach (var quest in otherQuests)
                {
                    if (point.Y >= currentY - lineSpacing / 2 && point.Y <= currentY + lineSpacing / 2)
                    {
                        var collapseSymbol = _collapsedQuests.GetValueOrDefault(quest.Id, false) ? "[+] " : "[-] ";
                        var collapseWidth = _questTextPaint.MeasureText(collapseSymbol);

                        if (point.X >= ClientRectangle.Left + _padding && point.X <= ClientRectangle.Left + _padding + collapseWidth)
                        {
                            _collapsedQuests[quest.Id] = !_collapsedQuests.GetValueOrDefault(quest.Id, false);
                            return true;
                        }
                    }

                    currentY += lineSpacing;

                    if (!_collapsedQuests.GetValueOrDefault(quest.Id, false))
                    {
                        if (EftDataManager.TaskData.TryGetValue(quest.Id, out var taskData) && taskData.Objectives != null)
                        {
                            for (int i = 0; i < taskData.Objectives.Count; i++)
                            {
                                var objective = taskData.Objectives[i];

                                QuestObjective parsedObjective = null;
                                if (i < quest.Objectives.Count)
                                    parsedObjective = quest.Objectives[i];

                                var isOptional = objective.Optional || (parsedObjective?.Optional == true);
                                var isCompleted = quest.CompletedConditions.Contains(objective.Id);

                                if (isOptional && !Config.QuestHelper.OptionalTaskFilter)
                                    continue;

                                currentY += lineSpacing;

                                if (_showKeys && !isCompleted && !isOptional && objective.RequiredKeys != null)
                                {
                                    var keyCount = objective.RequiredKeys.Sum(keyGroup => keyGroup.Count);
                                    currentY += lineSpacing * keyCount;
                                }

                                if (_showRequiredItems && !isCompleted && !isOptional)
                                    if (parsedObjective != null && parsedObjective.HasItemRequirement)
                                        currentY += lineSpacing * parsedObjective.RequiredItemIds.Count;
                            }
                        }
                        else
                        {
                            foreach (var objective in quest.Objectives)
                            {
                                if (objective.Optional && !Config.QuestHelper.OptionalTaskFilter)
                                    continue;

                                currentY += lineSpacing;

                                if (_showRequiredItems && !objective.IsCompleted && !objective.Optional && objective.HasItemRequirement)
                                    currentY += lineSpacing * objective.RequiredItemIds.Count;
                            }
                        }
                    }
                }
            }

            return false;
        }

        private string GetCurrentMapId()
        {
            return Memory.MapID ?? "unknown";
        }

        private string GetMapDisplayName(string mapId)
        {
            return mapId switch
            {
                "factory4_day" => "Factory (Day)",
                "factory4_night" => "Factory (Night)",
                "bigmap" => "Customs",
                "woods" => "Woods",
                "lighthouse" => "Lighthouse",
                "shoreline" => "Shoreline",
                "labyrinth" => "Labyrinth",
                "rezervbase" => "Reserve",
                "interchange" => "Interchange",
                "tarkovstreets" => "Streets",
                "laboratory" => "Labs",
                "Sandbox" => "Ground Zero",
                "Sandbox_high" => "Ground Zero (High)",
                _ => mapId
            };
        }

        private string TruncateString(string input, int maxLength)
        {
            if (string.IsNullOrEmpty(input) || input.Length <= maxLength)
                return input ?? "";

            return input.Substring(0, Math.Max(0, maxLength - 3)) + "...";
        }

        private int GetDynamicMaxLength(int baseLength)
        {
            var availableWidth = Size.Width - (_padding * 2);
            var avgCharWidth = _questTextPaint.MeasureText("A");
            var maxChars = (int)(availableWidth / avgCharWidth);
            return Math.Max(10, maxChars);
        }

        private static readonly SKPaint _questTextPaint = new()
        {
            SubpixelText = true,
            Color = SKColors.White,
            IsStroke = false,
            TextSize = 10,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Consolas"),
            FilterQuality = SKFilterQuality.High
        };

        private static readonly SKPaint _questKeyPaint = new()
        {
            SubpixelText = true,
            Color = SKColors.Yellow,
            IsStroke = false,
            TextSize = 10,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Consolas"),
            FilterQuality = SKFilterQuality.High
        };

        private static readonly SKPaint _questSeparatorPaint = new()
        {
            SubpixelText = true,
            Color = SKColors.Gray,
            IsStroke = false,
            TextSize = 10,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Consolas"),
            FilterQuality = SKFilterQuality.High
        };

        private static readonly SKPaint _questCompletedPaint = new()
        {
            SubpixelText = true,
            Color = SKColors.Green,
            IsStroke = false,
            TextSize = 10,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Consolas"),
            FilterQuality = SKFilterQuality.High
        };

        private static readonly SKPaint _questIncompletePaint = new()
        {
            SubpixelText = true,
            Color = SKColors.White,
            IsStroke = false,
            TextSize = 10,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Consolas"),
            FilterQuality = SKFilterQuality.High
        };

        private static readonly SKPaint _questNamePaint = new()
        {
            SubpixelText = true,
            Color = SKColors.LightBlue,
            IsStroke = false,
            TextSize = 10,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Consolas"),
            FilterQuality = SKFilterQuality.High
        };

        private static readonly SKPaint _questItemPaint = new()
        {
            SubpixelText = true,
            Color = SKColors.Orange,
            IsStroke = false,
            TextSize = 10,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Consolas"),
            FilterQuality = SKFilterQuality.High
        };

        private static readonly SKPaint _questOptionalPaint = new()
        {
            SubpixelText = true,
            Color = SKColors.Gray,
            IsStroke = false,
            TextSize = 10,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Consolas"),
            FilterQuality = SKFilterQuality.High
        };

        private static readonly SKPaint _questStrikethroughPaint = new()
        {
            Color = SKColors.Green,
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
    }
}