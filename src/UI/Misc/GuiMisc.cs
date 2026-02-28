using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Pages;
using eft_dma_radar.Common.Maps;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Common.Unity;
using System.Collections.Generic;

namespace eft_dma_radar.UI.Misc
{
    /// <summary>
    /// Contains long/short names for player gear.
    /// </summary>
    public sealed class GearItem
    {
        public string Long { get; init; }
        public string Short { get; init; }
    }

    /// <summary>
    /// Represents a PMC in the PMC History log.
    /// </summary>
    public sealed class PlayerHistoryEntry
    {
        private readonly Player _player;
        private DateTime _lastSeen;

        /// <summary>
        /// The Player Object that this entry is bound to.
        /// </summary>
        public Player Player => _player;

        public string Name => _player.Name;

        public string ID => _player.AccountID;

        public string Acct
        {
            get
            {
                if (_player is ObservedPlayer observed)
                    return observed.Profile?.Acct;
                return "--";
            }
        }

        public string Type => $"{_player.Type.GetDescription()}";

        public string KD
        {
            get
            {
                if (_player is ObservedPlayer observed && observed.Profile?.Overall_KD is float kd)
                    return kd.ToString("n2");
                return "--";
            }
        }

        public string Hours
        {
            get
            {
                if (_player is ObservedPlayer observed && observed.Profile?.Hours is int hours)
                    return hours.ToString();
                return "--";
            }
        }

        /// <summary>
        /// When this player was last seen
        /// </summary>
        public DateTime LastSeen
        {
            get => _lastSeen;
            private set => _lastSeen = value;
        }

        /// <summary>
        /// Formatted LastSeen for display in UI
        /// </summary>
        public string LastSeenFormatted
        {
            get
            {
                var timeSpan = DateTime.Now - _lastSeen;

                if (timeSpan.TotalMinutes < 1)
                    return "Just now";
                else if (timeSpan.TotalMinutes < 60)
                    return $"{(int)timeSpan.TotalMinutes}m ago";
                else if (timeSpan.TotalHours < 24)
                    return $"{(int)timeSpan.TotalHours}h ago";
                else if (timeSpan.TotalDays < 7)
                    return $"{(int)timeSpan.TotalDays}d ago";
                else
                    return _lastSeen.ToString("MM/dd/yyyy");
            }
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="player">Player Object to bind to.</param>
        public PlayerHistoryEntry(Player player)
        {
            ArgumentNullException.ThrowIfNull(player, nameof(player));
            _player = player;
            _lastSeen = DateTime.Now;
        }

        /// <summary>
        /// Updates the LastSeen timestamp to current time
        /// </summary>
        public void UpdateLastSeen()
        {
            LastSeen = DateTime.Now;
        }

        /// <summary>
        /// Updates the LastSeen timestamp to a specific time
        /// </summary>
        /// <param name="timestamp">The timestamp when the player was seen</param>
        public void UpdateLastSeen(DateTime timestamp)
        {
            LastSeen = timestamp;
        }
    }
    /// <summary>
    /// JSON Wrapper for Player Watchlist.
    /// </summary>
    public sealed class PlayerWatchlistEntry
    {
        /// <summary>
        /// Player's Account ID as obtained from Player History.
        /// </summary>
        [JsonPropertyName("acctID")]
        public string AccountID { get; set; } = string.Empty;

        /// <summary>
        /// Reason for adding player to Watchlist (ex: Cheater, streamer name,etc.)
        /// </summary>
        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// The streaming platform (Twitch, YouTube, etc.)
        /// </summary>
        [JsonPropertyName("platform")]
        public StreamingPlatform StreamingPlatform { get; set; } = StreamingPlatform.None;

        /// <summary>
        /// The platform username
        /// </summary>
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;
    }
    public sealed class ScreenEntry
    {
        private readonly int _screenNumber;

        /// <summary>
        /// Screen Index Number.
        /// </summary>
        public int ScreenNumber => _screenNumber;

        public ScreenEntry(int screenNumber)
        {
            _screenNumber = screenNumber;
        }

        public override string ToString() => $"Screen {_screenNumber}";
    }

    public sealed class BonesListItem
    {
        public string Name { get; }
        public Bones Bone { get; }
        public BonesListItem(Bones bone)
        {
            Name = bone.GetDescription();
            Bone = bone;
        }
        public override string ToString() => Name;
    }

    public sealed class QuestListItem : INotifyPropertyChanged
    {
        public string Name { get; }
        public string Id { get; }
        public string DebugTooltip { get; private set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public QuestListItem(string id, bool isSelected)
        {
            Id = id;
            if (EftDataManager.TaskData.TryGetValue(id, out var task))
            {
                Name = task.Name ?? id;
            }
            else
                Name = id;

            IsSelected = isSelected;
            BuildDebugTooltip();
        }

        private void BuildDebugTooltip()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Quest ID: {Id}");
            sb.AppendLine($"Name: {Name}");
            
            // Get quest data from QuestManager if available
            if (Memory.Game is Tarkov.GameWorld.LocalGameWorld lgw && lgw.QuestManager != null)
            {
                var quest = lgw.QuestManager.ActiveQuests.FirstOrDefault(q => q.Id == Id);
                if (quest != null)
                {
                    sb.AppendLine($"Kappa Required: {quest.KappaRequired}");
                    sb.AppendLine($"Completed: {quest.CompletedObjectivesCount}/{quest.TotalObjectivesCount}");
                    sb.AppendLine();
                    sb.AppendLine("=== OBJECTIVES ===");
                    foreach (var obj in quest.Objectives)
                    {
                        var status = obj.IsCompleted ? "✓" : "○";
                        var optional = obj.Optional ? " (Optional)" : "";
                        sb.AppendLine($"{status} [{obj.Id}]{optional}");
                        sb.AppendLine($"   Type: {obj.Type}");
                        if (!string.IsNullOrEmpty(obj.Description))
                            sb.AppendLine($"   Desc: {obj.Description}");
                        if (obj.LocationObjectives.Any())
                        {
                            sb.AppendLine($"   Locations: {obj.LocationObjectives.Count}");
                            foreach (var loc in obj.LocationObjectives)
                                sb.AppendLine($"      - {loc.LocationName} ({loc.MapId})");
                        }
                        if (obj.RequiredItemIds.Any())
                            sb.AppendLine($"   Items: {string.Join(", ", obj.RequiredItemIds)}");
                    }
                    
                    if (quest.CompletedConditions.Any())
                    {
                        sb.AppendLine();
                        sb.AppendLine("=== COMPLETED CONDITIONS ===");
                        foreach (var cond in quest.CompletedConditions)
                            sb.AppendLine($"  ✓ {cond}");
                    }
                }
                else
                {
                    sb.AppendLine("(Quest not found in ActiveQuests)");
                }
            }
            else
            {
                sb.AppendLine("(QuestManager not available)");
            }
            
            DebugTooltip = sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Refreshes the debug tooltip with current quest data.
        /// </summary>
        public void RefreshTooltip()
        {
            BuildDebugTooltip();
            OnPropertyChanged(nameof(DebugTooltip));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string prop) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public sealed class HotkeyDisplayModel
    {
        public string Action { get; set; }
        public string Key { get; set; }
        public string Type { get; set; }

        public string Display => $"{Action} ({Key})";
    }

    /// <summary>
    /// Wrapper class for displaying container info in the UI.
    /// </summary>
    public sealed class ContainerListItem : INotifyPropertyChanged
    {
        public string Name { get; }
        public string Id { get; }
        public List<string> GroupedIds { get; set; } = new();

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public ContainerListItem(TarkovMarketItem container)
        {
            Name = container.ShortName;
            Id = container.BsgId;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string prop) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public static class SkiaResourceTracker
    {
        private static DateTime _lastMainWindowPurge = DateTime.UtcNow;
        private static DateTime _lastESPPurge = DateTime.UtcNow;
        private static int _mainWindowFrameCount = 0;
        private static int _espFrameCount = 0;

        public static void TrackMainWindowFrame()
        {
            _mainWindowFrameCount++;

            var now = DateTime.UtcNow;
            var timeSincePurge = (now - _lastMainWindowPurge).TotalSeconds;

            if (timeSincePurge >= 5.0 && _mainWindowFrameCount % 300 == 0)
            {
                _lastMainWindowPurge = now;
                MainWindow.Window?.PurgeSKResources();
            }
        }

        public static void TrackESPFrame()
        {
            _espFrameCount++;

            var now = DateTime.UtcNow;
            var timeSincePurge = (now - _lastESPPurge).TotalSeconds;

            if (timeSincePurge >= 10.0 && _espFrameCount % 600 == 0)
            {
                _lastESPPurge = now;
                ESPForm.Window?.PurgeSKResources();
            }
        }
    }

    public enum LootPriceMode : int
    {
        /// <summary>
        /// Optimal Flea Price.
        /// </summary>
        FleaMarket = 0,
        /// <summary>
        /// Highest Trader Price.
        /// </summary>
        Trader = 1
    }

    public enum ApplicationMode
    {
        Normal,
        SafeMode
    }

    /// <summary>
    /// Defines how entity types are rendered on the map
    /// </summary>
    public enum EntityRenderMode
    {
        [Description("None")]
        None,
        [Description("Dot")]
        Dot,
        [Description("Cross")]
        Cross,
        [Description("Plus")]
        Plus,
        [Description("Square")]
        Square,
        [Description("Diamond")]
        Diamond
    }

    /// <summary>
    /// Enum representing different streaming platforms
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum StreamingPlatform
    {
        /// <summary>
        /// No streaming platform
        /// </summary>
        None,

        /// <summary>
        /// Twitch.tv streaming platform
        /// </summary>
        Twitch,

        /// <summary>
        /// YouTube streaming platform
        /// </summary>
        YouTube
    }

    /// <summary>
    /// Serializable RectF Structure.
    /// </summary>
    public struct RectFSer
    {
        [JsonPropertyName("left")] public float Left { get; set; }
        [JsonPropertyName("top")] public float Top { get; set; }
        [JsonPropertyName("right")] public float Right { get; set; }
        [JsonPropertyName("bottom")] public float Bottom { get; set; }

        public RectFSer(float left, float top, float right, float bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }
    }

    public struct PointFSer
    {
        [JsonPropertyName("x")]
        public float X { get; set; }

        [JsonPropertyName("y")]
        public float Y { get; set; }

        public PointFSer() { }

        public PointFSer(float x, float y)
        {
            X = x;
            Y = y;
        }
    }

    public static class GuiExtensions
    {
        #region GUI Extensions
        /// <summary>
        /// Convert Unity Position (X,Y,Z) to an unzoomed Map Position..
        /// </summary>
        /// <param name="vector">Unity Vector3</param>
        /// <param name="map">Current Map</param>
        /// <returns>Unzoomed 2D Map Position.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 ToMapPos(this System.Numerics.Vector3 vector, XMMapConfig map) =>
            new()
            {
                X = (map.X * map.SvgScale) + (vector.X * (map.Scale * map.SvgScale)),
                Y = (map.Y * map.SvgScale) - (vector.Z * (map.Scale * map.SvgScale))
            };

        /// <summary>
        /// Convert an Unzoomed Map Position to a Zoomed Map Position ready for 2D Drawing.
        /// </summary>
        /// <param name="mapPos">Unzoomed Map Position.</param>
        /// <param name="mapParams">Current Map Parameters.</param>
        /// <returns>Zoomed 2D Map Position.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SKPoint ToZoomedPos(this Vector2 mapPos, XMMapParams mapParams) =>
            new SKPoint()
            {
                X = (mapPos.X - mapParams.Bounds.Left) * mapParams.XScale,
                Y = (mapPos.Y - mapParams.Bounds.Top) * mapParams.YScale
            };

        /// <summary>
        /// Gets a drawable 'Up Arrow'. IDisposable. Applies UI Scaling internally.
        /// </summary>
        public static SKPath GetUpArrow(this SKPoint point, float size = 6, float offsetX = 0, float offsetY = 0)
        {
            float x = point.X + offsetX;
            float y = point.Y + offsetY;

            size *= MainWindow.UIScale;
            var path = new SKPath();
            path.MoveTo(x, y);
            path.LineTo(x - size, y + size);
            path.LineTo(x + size, y + size);
            path.Close();

            return path;
        }

        /// <summary>
        /// Gets a drawable 'Down Arrow'. IDisposable. Applies UI Scaling internally.
        /// </summary>
        public static SKPath GetDownArrow(this SKPoint point, float size = 6, float offsetX = 0, float offsetY = 0)
        {
            float x = point.X + offsetX;
            float y = point.Y + offsetY;

            size *= MainWindow.UIScale;
            var path = new SKPath();
            path.MoveTo(x, y);
            path.LineTo(x - size, y - size);
            path.LineTo(x + size, y - size);
            path.Close();

            return path;
        }

        /// <summary>
        /// Draws a Mine/Explosive Marker on this zoomed location.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DrawMineMarker(this SKPoint zoomedMapPos, SKCanvas canvas)
        {
            float length = 3.5f * MainWindow.UIScale;
            canvas.DrawLine(new SKPoint(zoomedMapPos.X - length, zoomedMapPos.Y + length), new SKPoint(zoomedMapPos.X + length, zoomedMapPos.Y - length), SKPaints.PaintExplosives);
            canvas.DrawLine(new SKPoint(zoomedMapPos.X - length, zoomedMapPos.Y - length), new SKPoint(zoomedMapPos.X + length, zoomedMapPos.Y + length), SKPaints.PaintExplosives);
        }

        /// <summary>
        /// Draws Mouseover Text (with backer) on this zoomed location.
        /// </summary>
        public static void DrawMouseoverText(this SKPoint zoomedMapPos, SKCanvas canvas, IEnumerable<string> lines)
        {
            float maxLength = 0;
            foreach (var line in lines)
            {
                var length = SKPaints.TextMouseover.MeasureText(line);
                if (length > maxLength)
                    maxLength = length;
            }
            var backer = new SKRect()
            {
                Bottom = zoomedMapPos.Y + ((lines.Count() * 12f) - 2) * MainWindow.UIScale,
                Left = zoomedMapPos.X + (9 * MainWindow.UIScale),
                Top = zoomedMapPos.Y - (9 * MainWindow.UIScale),
                Right = zoomedMapPos.X + (9 * MainWindow.UIScale) + maxLength + (6 * MainWindow.UIScale)
            };
            canvas.DrawRect(backer, SKPaints.PaintTransparentBacker); // Draw tooltip backer
            zoomedMapPos.Offset(11 * MainWindow.UIScale, 3 * MainWindow.UIScale);
            foreach (var line in lines) // Draw tooltip text
            {
                if (string.IsNullOrEmpty(line?.Trim()))
                    continue;
                canvas.DrawText(line, zoomedMapPos, SKPaints.TextMouseover); // draw line text
                zoomedMapPos.Offset(0, 12f * MainWindow.UIScale);
            }
        }

        /// <summary>
        /// Draw mouseover text with colored entries for important items
        /// </summary>
        public static void DrawMouseoverText(this SKPoint zoomedMapPos, SKCanvas canvas, IEnumerable<(string text, SKPaint paint)> coloredLines)
        {
            var lineList = coloredLines.ToList();
            if (!lineList.Any()) return;

            float maxLength = 0;
            foreach (var line in lineList)
            {
                var length = line.paint.MeasureText(line.text);
                if (length > maxLength)
                    maxLength = length;
            }

            var backer = new SKRect()
            {
                Bottom = zoomedMapPos.Y + ((lineList.Count * 12f) - 2) * MainWindow.UIScale,
                Left = zoomedMapPos.X + (9 * MainWindow.UIScale),
                Top = zoomedMapPos.Y - (9 * MainWindow.UIScale),
                Right = zoomedMapPos.X + (9 * MainWindow.UIScale) + maxLength + (6 * MainWindow.UIScale)
            };
            canvas.DrawRect(backer, SKPaints.PaintTransparentBacker);
            zoomedMapPos.Offset(11 * MainWindow.UIScale, 3 * MainWindow.UIScale);

            foreach (var line in lineList)
            {
                if (string.IsNullOrEmpty(line.text?.Trim()))
                    continue;
                canvas.DrawText(line.text, zoomedMapPos, line.paint);
                zoomedMapPos.Offset(0, 12f * MainWindow.UIScale);
            }
        }

        /// <summary>
        /// Draw ESP text with optional distance display for entities or static objects like mines
        /// </summary>
        public static void DrawESPText(this SKPoint screenPos, SKCanvas canvas, IESPEntity entity, LocalPlayer localPlayer, bool printDist, SKPaint paint, params string[] lines)
        {
            if (printDist && lines.Length > 0)
            {
                string distStr;

                if (entity != null)
                {
                    var dist = Vector3.Distance(entity.Position, localPlayer.Position);

                    if (entity is LootItem && dist < 10f)
                        distStr = $" {dist.ToString("n1")}m";
                    else
                        distStr = $" {(int)dist}m";

                    lines[0] += distStr;
                }
            }

            foreach (var x in lines)
            {
                if (string.IsNullOrEmpty(x?.Trim()))
                    continue;

                canvas.DrawText(x, screenPos, paint);
                screenPos.Y += paint.TextSize;
            }
        }

        /// <summary>
        /// Overload for static objects like mines where we calculate the distance with a provided value
        /// </summary>
        public static void DrawESPText(this SKPoint screenPos, SKCanvas canvas, IESPEntity entity, LocalPlayer localPlayer, bool printDist, SKPaint paint, string label, float distance)
        {
            if (string.IsNullOrEmpty(label))
                return;

            var textWithDist = label;

            if (printDist)
            {
                var distStr = distance < 10f ? $" {distance:n1}m" : $" {(int)distance}m";

                textWithDist += distStr;
            }

            canvas.DrawText(textWithDist, screenPos, paint);
        }

        /// <summary>
        /// Draw ESP text with colored entries for important items in a corpse
        /// </summary>
        public static void DrawESPText(this SKPoint screenPos, SKCanvas canvas, IESPEntity entity, LocalPlayer localPlayer, bool printDist, SKPaint paint, string mainLabel, IEnumerable<LootItem> importantItems = null)
        {
            var scale = ESP.ESP.Config.FontScale;
            var currentPos = screenPos;

            if (!string.IsNullOrEmpty(mainLabel))
            {
                var textWithDist = mainLabel;

                if (printDist && entity != null)
                {
                    var dist = Vector3.Distance(entity.Position, localPlayer.Position);
                    var distStr = dist < 10f ? $" {dist:n1}m" : $" {(int)dist}m";
                    textWithDist += distStr;
                }

                var scaledMainPaint = new SKPaint
                {
                    SubpixelText = paint.SubpixelText,
                    Color = paint.Color,
                    IsStroke = paint.IsStroke,
                    TextSize = 12f * scale,
                    TextAlign = paint.TextAlign,
                    TextEncoding = paint.TextEncoding,
                    IsAntialias = paint.IsAntialias,
                    Typeface = paint.Typeface,
                    FilterQuality = paint.FilterQuality
                };

                canvas.DrawText(textWithDist, currentPos, scaledMainPaint);
                currentPos.Y += scaledMainPaint.TextSize * 1.2f;
            }

            if (importantItems != null)
            {
                foreach (var item in importantItems.Take(5))
                {
                    var basePaint = GetItemESPPaint(item);
                    var scaledItemPaint = new SKPaint
                    {
                        SubpixelText = basePaint.SubpixelText,
                        Color = basePaint.Color,
                        IsStroke = basePaint.IsStroke,
                        TextSize = 12f * scale,
                        TextAlign = basePaint.TextAlign,
                        TextEncoding = basePaint.TextEncoding,
                        IsAntialias = basePaint.IsAntialias,
                        Typeface = basePaint.Typeface,
                        FilterQuality = basePaint.FilterQuality
                    };

                    canvas.DrawText(item.ShortName, currentPos, scaledItemPaint);
                    currentPos.Y += scaledItemPaint.TextSize * 1.2f;
                }
            }
        }

        /// <summary>
        /// Draw ESP text for living players with weapon info and important loot
        /// </summary>
        public static void DrawESPText(this SKPoint screenPos, SKCanvas canvas, Player player, LocalPlayer localPlayer, bool printDist, SKPaint paint, string weaponInfo, IEnumerable<LootItem> importantLoot = null)
        {
            var scale = ESP.ESP.Config.FontScale;
            var currentPos = screenPos;

            if (!string.IsNullOrEmpty(weaponInfo))
            {
                var weaponLines = weaponInfo.Split('\n');

                foreach (var line in weaponLines)
                {
                    if (string.IsNullOrEmpty(line?.Trim()))
                        continue;

                    var scaledPaint = new SKPaint
                    {
                        SubpixelText = paint.SubpixelText,
                        Color = paint.Color,
                        IsStroke = paint.IsStroke,
                        TextSize = 12f * scale,
                        TextAlign = paint.TextAlign,
                        TextEncoding = paint.TextEncoding,
                        IsAntialias = paint.IsAntialias,
                        Typeface = paint.Typeface,
                        FilterQuality = paint.FilterQuality
                    };

                    canvas.DrawText(line, currentPos, scaledPaint);
                    currentPos.Y += scaledPaint.TextSize;
                }
            }

            if (importantLoot != null)
            {
                foreach (var item in importantLoot.Take(5))
                {
                    var basePaint = GetItemESPPaint(item);
                    var scaledItemPaint = new SKPaint
                    {
                        SubpixelText = basePaint.SubpixelText,
                        Color = basePaint.Color,
                        IsStroke = basePaint.IsStroke,
                        TextSize = 12f * scale,
                        TextAlign = basePaint.TextAlign,
                        TextEncoding = basePaint.TextEncoding,
                        IsAntialias = basePaint.IsAntialias,
                        Typeface = basePaint.Typeface,
                        FilterQuality = basePaint.FilterQuality
                    };

                    canvas.DrawText(item.ShortName, currentPos, scaledItemPaint);
                    currentPos.Y += scaledItemPaint.TextSize;
                }
            }
        }

        /// <summary>
        /// Helper method to get the appropriate ESP paint for an item based on its importance/filter
        /// </summary>
        private static SKPaint GetItemESPPaint(LootItem item)
        {
            var matchedFilter = item.MatchedFilter;
            if (matchedFilter != null && !string.IsNullOrEmpty(matchedFilter.Color))
            {
                if (SKColor.TryParse(matchedFilter.Color, out var filterColor))
                {
                    return new SKPaint
                    {
                        SubpixelText = true,
                        Color = filterColor,
                        IsStroke = false,
                        TextSize = 12f,
                        TextAlign = SKTextAlign.Center,
                        TextEncoding = SKTextEncoding.Utf8,
                        IsAntialias = true,
                        Typeface = CustomFonts.SKFontFamilyMedium,
                        FilterQuality = SKFilterQuality.Low
                    };
                }
            }

            if (item is QuestItem)
                return SKPaints.TextQuestHelperESP;
            if (Program.Config.QuestHelper.Enabled && item.IsQuestCondition)
                return SKPaints.TextQuestItemESP;
            if (item.IsWishlisted)
                return SKPaints.TextWishlistItemESP;
            if (LootFilterControl.ShowBackpacks && item.IsBackpack)
                return SKPaints.TextBackpackESP;
            if (LootFilterControl.ShowMeds && item.IsMeds)
                return SKPaints.TextMedsESP;
            if (LootFilterControl.ShowFood && item.IsFood)
                return SKPaints.TextFoodESP;
            if (LootFilterControl.ShowWeapons && item.IsWeapon)
                return SKPaints.TextWeaponsESP;
            if (item.IsValuableLoot)
                return SKPaints.TextImpLootESP;

            return SKPaints.TextBasicESP;
        }

        #endregion
    }
}
