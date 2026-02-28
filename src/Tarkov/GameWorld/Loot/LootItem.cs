using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Common.Maps;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.Pages;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.Common.Misc.Pools;
using eft_dma_radar.UI.LootFilters;

namespace eft_dma_radar.Tarkov.Loot
{
    public class LootItem : IMouseoverEntity, IMapEntity, IWorldEntity, IESPEntity
    {
        private static Config Config => Program.Config;
        private readonly TarkovMarketItem _item;
        private DateTime? _lastNotifyTime;
        private static readonly Dictionary<string, DateTime> _lastNotifyTimes = new();
        private static readonly TimeSpan NotifyCooldown = TimeSpan.FromSeconds(30);

        public static EntityTypeSettings LootSettings => Config.EntityTypeSettings.GetSettings("RegularLoot");
        public static EntityTypeSettingsESP LootESPSettings => ESP.Config.EntityTypeESPSettings.GetSettings("RegularLoot");

        public static EntityTypeSettings ImportantLootSettings => Config.EntityTypeSettings.GetSettings("ImportantLoot");
        public static EntityTypeSettingsESP ImportantLootESPSettings => ESP.Config.EntityTypeESPSettings.GetSettings("ImportantLoot");

        public static EntityTypeSettings CorpseSettings => Config.EntityTypeSettings.GetSettings("Corpse");
        public static EntityTypeSettingsESP CorpseESPSettings => ESP.Config.EntityTypeESPSettings.GetSettings("Corpse");

        public static EntityTypeSettings QuestItemSettings => Config.EntityTypeSettings.GetSettings("QuestItem");
        public static EntityTypeSettingsESP QuestItemESPSettings => ESP.Config.EntityTypeESPSettings.GetSettings("QuestItem");

        public static EntityTypeSettings AirdropSettings => Config.EntityTypeSettings.GetSettings("Airdrop");
        public static EntityTypeSettingsESP AirdropESPSettings => ESP.Config.EntityTypeESPSettings.GetSettings("Airdrop");

        private static bool QuestHelperEnabled = Config.QuestHelper.Enabled;

        private const float HEIGHT_INDICATOR_THRESHOLD = 1.85f;

        public LootItem(TarkovMarketItem item)
        {
            ArgumentNullException.ThrowIfNull(item, nameof(item));
            _item = item;
        }

        public LootItem(string id, string name)
        {
            ArgumentNullException.ThrowIfNull(id, nameof(id));
            ArgumentNullException.ThrowIfNull(name, nameof(name));
            _item = new TarkovMarketItem
            {
                Name = name,
                ShortName = name,
                FleaPrice = -1,
                TraderPrice = -1,
                BsgId = id
            };
        }

        /// <summary>
        /// Item's BSG ID.
        /// </summary>
        public virtual string ID => _item.BsgId;

        /// <summary>
        /// Item's Long Name.
        /// </summary>
        public virtual string Name => _item.Name;

        /// <summary>
        /// Item's Short Name.
        /// </summary>
        public string ShortName => _item.ShortName;

        public ulong InteractiveClass { get; set; }
        static Dictionary<ulong, List<int>> _originalMaterials = new();
        /// <summary>
        /// Item's Price (In roubles).
        /// </summary>
        public int Price
        {
            get
            {
                long price;
                if (Config.LootPPS)
                {
                    if (Config.LootPriceMode is LootPriceMode.FleaMarket)
                        price = (long)((float)_item.FleaPrice / GridCount);
                    else
                        price = (long)((float)_item.TraderPrice / GridCount);
                }
                else
                {
                    if (Config.LootPriceMode is LootPriceMode.FleaMarket)
                        price = _item.FleaPrice;
                    else
                        price = _item.TraderPrice;
                }
                if (price <= 0)
                    price = Math.Max(_item.FleaPrice, _item.TraderPrice);
                return (int)price;
            }
        }

        /// <summary>
        /// Number of grid spaces this item takes up.
        /// </summary>
        public int GridCount => _item.Slots == 0 ? 1 : _item.Slots;

        /// <summary>
        /// Custom filter for this item (if set).
        /// </summary>
        public LootFilterEntry CustomFilter => _item.CustomFilter;

        /// <summary>
        /// True if the item is important via the UI.
        /// </summary>
        public bool Important => CustomFilter?.Important ?? false;

        /// <summary>
        /// True if this item is wishlisted.
        /// </summary>
        public bool IsWishlisted => Config.LootWishlist && LocalPlayer.WishlistItems.Contains(ID);

        public GroupedLootFilterEntry MatchedFilter
        {
            get
            {
                var groups = LootFilterManager.CurrentGroups?.Groups?
                    .OrderBy(g => g.Index);

                foreach (var group in groups)
                {
                    if (!group.Enabled)
                        continue;

                    var match = group.Items.FirstOrDefault(i => i.Enabled && i.ItemID == ID);
                    if (match != null)
                        return match;
                }

                return null;
            }
        }

        public bool IsGroupedBlacklisted
        {
            get
            {
                var matchedFilter = MatchedFilter;
                return matchedFilter?.Blacklist == true;
            }
        }

        public (LootFilterGroup Group, GroupedLootFilterEntry Entry)? GetMatchedGroupAndEntry()
        {
            var groups = LootFilterManager.CurrentGroups?.Groups?
                .OrderBy(g => g.Index);
        
            foreach (var group in groups)
            {
                if (!group.Enabled)
                    continue;
        
                var match = group.Items.FirstOrDefault(i => i.Enabled && i.ItemID == ID);
                if (match != null)
                    return (group, match);
            }
        
            return null;
        }

        /// <summary>
        /// True if the item is blacklisted via the UI.
        /// </summary>
        public bool Blacklisted => CustomFilter?.Blacklisted ?? IsGroupedBlacklisted;

        public bool IsMeds
        {
            get
            {
                if (this is LootContainer container)
                    return container.Loot.Any(x => x.IsMeds);

                return _item.IsMed;
            }
        }

        public bool IsFood
        {
            get
            {
                if (this is LootContainer container)
                    return container.Loot.Any(x => x.IsFood);

                return _item.IsFood;
            }
        }

        public bool IsBackpack
        {
            get
            {
                if (this is LootContainer container)
                    return container.Loot.Any(x => x.IsBackpack);

                return _item.IsBackpack;
            }
        }

        public bool IsWeapon => _item.IsWeapon;

        public bool IsWeaponMod => _item.IsWeaponMod;

        public bool IsCurrency => _item.IsCurrency;

        /// <summary>
        /// Checks if an item exceeds regular loot price threshold.
        /// </summary>
        public bool IsRegularLoot
        {
            get
            {
                if (Blacklisted || IsGroupedBlacklisted)
                    return false;

                if (this is LootContainer container)
                    return container.Loot.Any(x => x.IsRegularLoot);

                return MatchedFilter != null || Price >= Config.MinLootValue;
            }
        }

        /// <summary>
        /// Checks if a corpse meets the minimum value threshold to be displayed
        /// </summary>
        public bool MeetsCorpseValueThreshold
        {
            get
            {
                if (this is LootCorpse corpse)
                {
                    var sumPrice = corpse.Loot?.Sum(x => x.Price) ?? 0;
                    return sumPrice >= Config.MinCorpseValue;
                }

                return true;
            }
        }

        /// <summary>
        /// Checks if this item/container contains any important/valuable items that should always be displayed
        /// </summary>
        public bool HasImportantItems
        {
            get
            {
                if (this is LootCorpse corpse)
                {
                    return corpse.Loot?.Any(item =>
                        !item.IsGroupedBlacklisted &&
                        (item.Important ||
                         item.IsWishlisted ||
                         item is QuestItem ||
                         (Config.QuestHelper.Enabled && item.IsQuestCondition) ||
                         (LootFilterControl.ShowBackpacks && item.IsBackpack) ||
                         (LootFilterControl.ShowMeds && item.IsMeds) ||
                         (LootFilterControl.ShowFood && item.IsFood) ||
                         (LootFilterControl.ShowWeapons && item.IsWeapon) ||
                         item.IsValuableLoot ||
                         (item.MatchedFilter?.Color != null && !string.IsNullOrEmpty(item.MatchedFilter.Color)))
                    ) ?? false;
                }

                if (this is LootContainer container)
                {
                    return container.Loot?.Any(item =>
                        !item.IsGroupedBlacklisted &&
                        (item.Important ||
                         item.IsWishlisted ||
                         item is QuestItem ||
                         (Config.QuestHelper.Enabled && item.IsQuestCondition) ||
                         (LootFilterControl.ShowBackpacks && item.IsBackpack) ||
                         (LootFilterControl.ShowMeds && item.IsMeds) ||
                         (LootFilterControl.ShowFood && item.IsFood) ||
                         (LootFilterControl.ShowWeapons && item.IsWeapon) ||
                         item.IsValuableLoot ||
                         (item.MatchedFilter?.Color != null && !string.IsNullOrEmpty(item.MatchedFilter.Color)))
                    ) ?? false;
                }

                if (IsGroupedBlacklisted)
                    return false;

                return Important ||
                       IsWishlisted ||
                       this is QuestItem ||
                       (Config.QuestHelper.Enabled && IsQuestCondition) ||
                       (LootFilterControl.ShowBackpacks && IsBackpack) ||
                       (LootFilterControl.ShowMeds && IsMeds) ||
                       (LootFilterControl.ShowFood && IsFood) ||
                       (LootFilterControl.ShowWeapons && IsWeapon) ||
                       IsValuableLoot ||
                       (MatchedFilter?.Color != null && !string.IsNullOrEmpty(MatchedFilter.Color));
            }
        }

        /// <summary>
        /// Checks if an item exceeds valuable loot price threshold.
        /// </summary>
        public bool IsValuableLoot
        {
            get
            {
                if (Blacklisted || IsGroupedBlacklisted)
                    return false;

                if (this is LootContainer container)
                    return container.Loot.Any(x => x.IsValuableLoot);

                return Price >= Config.MinValuableLootValue;
            }
        }

        /// <summary>
        /// Checks if an item/container is important.
        /// </summary>
        public bool IsImportant
        {
            get
            {
                if (Blacklisted || IsGroupedBlacklisted)
                    return false;

                if (this is LootContainer container)
                    return container.Loot.Any(x => x.IsImportant);

                if (MatchedFilter != null)
                    return true;

                return _item.Important || IsWishlisted;
            }
        }

        /// <summary>
        /// True if a condition for a quest.
        /// </summary>
        public bool IsQuestCondition
        {
            get
            {
                if (Blacklisted || IsGroupedBlacklisted)
                    return false;

                if (IsCurrency)
                    return false;

                if (this is LootContainer container)
                    return container.Loot.Any(x => x.IsQuestCondition);

                var questManager = Memory.QuestManager;
                if (questManager == null)
                    return false;

                if (!questManager.IsItemRequired(ID))
                    return false;

                if (!Config.QuestHelper.OptionalTaskFilter)
                {
                    var hasNonOptionalRequirement = questManager.ActiveQuests.Any(quest =>
                        quest.Objectives.Any(obj =>
                            !obj.Optional &&
                            !obj.IsCompleted &&
                            obj.RequiredItemIds.Contains(ID)));

                    return hasNonOptionalRequirement;
                }

                return true;
            }
        }

        /// <summary>
        /// True if this item contains the specified Search Predicate.
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns>True if search matches, otherwise False.</returns>
        public bool ContainsSearchPredicate(Predicate<LootItem> predicate)
        {
            if (this is LootContainer container)
                return container.Loot.Any(x => x.ContainsSearchPredicate(predicate));

            return predicate(this);
        }

public virtual void Draw(SKCanvas canvas, XMMapParams mapParams, ILocalPlayer localPlayer)
{
    if (this is LootCorpse && (!CorpseSettings.Enabled || !MeetsCorpseValueThreshold) && !HasImportantItems)
        return;

    EntityTypeSettings entitySettings;

    if (this is LootAirdrop)
        entitySettings = AirdropSettings;
    else if (this is LootCorpse)
        entitySettings = CorpseSettings;
    else if (this is QuestItem || (QuestHelperEnabled && IsQuestCondition))
        entitySettings = QuestItemSettings;
    else if (IsImportant || IsValuableLoot)
        entitySettings = ImportantLootSettings;
    else
        entitySettings = LootSettings;

    var dist = Vector3.Distance(localPlayer.Position, Position);
    if (dist > entitySettings.RenderDistance)
        return;

    var label      = GetEntityUILabel(entitySettings);
    var paints     = GetPaints();
    var heightDiff = Position.Y - localPlayer.Position.Y;

    var point = Position
        .ToMapPos(mapParams.Map)
        .ToZoomedPos(mapParams);

    MouseoverPosition = new Vector2(point.X, point.Y);
    SKPaints.ShapeOutline.StrokeWidth = 2f;

    // ─────────────────────────────────────────────
    // SNAPSHOT CORPSE LOOT ONCE (CRITICAL FIX)
    // ─────────────────────────────────────────────
    List<LootItem> importantLootItems = null;

    if (this is LootCorpse corpse && CorpseSettings.ShowImportantLoot)
    {
        var corpseLootSnapshot = corpse.Loot?.ToList();
        if (corpseLootSnapshot is not null)
        {
            importantLootItems = corpseLootSnapshot
                .Where(item =>
                       item.IsImportant ||
                       item is QuestItem ||
                       (Config.QuestHelper.Enabled && item.IsQuestCondition) ||
                       item.IsWishlisted ||
                       (LootFilterControl.ShowBackpacks && item.IsBackpack) ||
                       (LootFilterControl.ShowMeds && item.IsMeds) ||
                       (LootFilterControl.ShowFood && item.IsFood) ||
                       (LootFilterControl.ShowWeapons && item.IsWeapon) ||
                       item.IsValuableLoot ||
                       (!item.IsGroupedBlacklisted &&
                        item.MatchedFilter?.Color != null &&
                        !string.IsNullOrEmpty(item.MatchedFilter.Color)))
                .OrderLoot()
                .Take(5)
                .ToList();
        }
    }

    float distanceYOffset;
    float nameXOffset = 7f * MainWindow.UIScale;
    float nameYOffset;

    if (heightDiff > HEIGHT_INDICATOR_THRESHOLD)
    {
        using var path = point.GetUpArrow(5);
        canvas.DrawPath(path, SKPaints.ShapeOutline);
        canvas.DrawPath(path, paints.Item1);
        distanceYOffset = 18f * MainWindow.UIScale;
        nameYOffset     = 6f * MainWindow.UIScale;
    }
    else if (heightDiff < -HEIGHT_INDICATOR_THRESHOLD)
    {
        using var path = point.GetDownArrow(5);
        canvas.DrawPath(path, SKPaints.ShapeOutline);
        canvas.DrawPath(path, paints.Item1);
        distanceYOffset = 12f * MainWindow.UIScale;
        nameYOffset     = 1f * MainWindow.UIScale;
    }
    else
    {
        var size = 5 * MainWindow.UIScale;
        canvas.DrawCircle(point, size, SKPaints.ShapeOutline);
        canvas.DrawCircle(point, size, paints.Item1);
        distanceYOffset = 16f * MainWindow.UIScale;
        nameYOffset     = 4f * MainWindow.UIScale;
    }

    if (entitySettings.ShowName || entitySettings.ShowValue)
    {
        point.Offset(nameXOffset, nameYOffset);

        if (!string.IsNullOrEmpty(label))
        {
            canvas.DrawText(label, point, SKPaints.TextOutline);
            canvas.DrawText(label, point, paints.Item2);
        }
    }

    var currentBottomY = point.Y + distanceYOffset - nameYOffset;

    if (entitySettings.ShowDistance)
    {
        var distText  = $"{(int)dist}m";
        var distWidth = paints.Item2.MeasureText(distText);

        var distPoint = new SKPoint(
            point.X - (distWidth / 2) - nameXOffset,
            currentBottomY
        );

        canvas.DrawText(distText, distPoint, SKPaints.TextOutline);
        canvas.DrawText(distText, distPoint, paints.Item2);
    }

    if (importantLootItems?.Count > 0)
    {
        var spacing  = 1 * MainWindow.UIScale;
        var textSize = 12 * MainWindow.UIScale;

        currentBottomY += textSize + spacing;

        foreach (var item in importantLootItems)
        {
            var itemText  = item.GetUILabel();
            var itemPaint = GetItemTextPaint(item);
            var itemWidth = itemPaint.MeasureText(itemText);

            var itemPoint = new SKPoint(
                point.X - (itemWidth / 2) - nameXOffset,
                currentBottomY
            );

            canvas.DrawText(itemText, itemPoint, SKPaints.TextOutline);
            canvas.DrawText(itemText, itemPoint, itemPaint);

            currentBottomY += textSize + spacing;
        }
    }
}


        private Vector3 _position;
        public ref Vector3 Position => ref _position;
        public Vector2 MouseoverPosition { get; set; }

        public virtual void DrawESP(SKCanvas canvas, LocalPlayer localPlayer)
        {
            if (this is LootCorpse && (!CorpseESPSettings.Enabled || !MeetsCorpseValueThreshold) && !HasImportantItems)
                return;

            EntityTypeSettingsESP espSettings;

            if (this is LootAirdrop)
                espSettings = AirdropESPSettings;
            else if (this is LootCorpse)
                espSettings = CorpseESPSettings;
            else if (this is QuestItem || (QuestHelperEnabled && IsQuestCondition))
                espSettings = QuestItemESPSettings;
            else if (IsImportant || IsValuableLoot)
                espSettings = ImportantLootESPSettings;
            else
                espSettings = LootESPSettings;

            var dist = Vector3.Distance(localPlayer.Position, Position);
            if (dist > espSettings.RenderDistance)
                return;

            if (!CameraManagerBase.WorldToScreen(ref _position, out var scrPos))
                return;

            var paints = GetESPPaints();
            var label = GetEntityUILabel(espSettings);
            var scale = ESP.Config.FontScale;

            switch (espSettings.RenderMode)
            {
                case EntityRenderMode.None:
                    break;

                case EntityRenderMode.Dot:
                    var dotSize = 3f * scale;
                    canvas.DrawCircle(scrPos.X, scrPos.Y, dotSize, paints.Item1);
                    break;

                case EntityRenderMode.Cross:
                    var crossSize = 5f * scale;

                    using (var thickPaint = new SKPaint
                    {
                        Color = paints.Item1.Color,
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
                        Color = paints.Item1.Color,
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
                    canvas.DrawRect(boxPt, paints.Item1);
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
                        canvas.DrawPath(diamondPath, paints.Item1);
                    }
                    break;
            }

            if (espSettings.ShowName || espSettings.ShowValue || espSettings.ShowDistance)
            {
                var textY = scrPos.Y + 16f * scale;
                var textPt = new SKPoint(scrPos.X, textY);

                if (this is LootCorpse corpse)
                {
                    var importantItems = CorpseESPSettings.ShowImportantLoot ? corpse.Loot?
                        .Where(item => item.IsImportant ||
                                      item is QuestItem ||
                                      (Config.QuestHelper.Enabled && item.IsQuestCondition) ||
                                      item.IsWishlisted ||
                                      (LootFilterControl.ShowBackpacks && item.IsBackpack) ||
                                      (LootFilterControl.ShowMeds && item.IsMeds) ||
                                      (LootFilterControl.ShowFood && item.IsFood) ||
                                      (LootFilterControl.ShowWeapons && item.IsWeapon) ||
                                      item.IsValuableLoot ||
                                      (!item.IsGroupedBlacklisted && item.MatchedFilter?.Color != null && !string.IsNullOrEmpty(item.MatchedFilter.Color)))
                        .OrderLoot() : null;

                    textPt.DrawESPText(
                        canvas,
                        this,
                        localPlayer,
                        espSettings.ShowDistance,
                        paints.Item2,
                        (espSettings.ShowName || espSettings.ShowValue) ? label : null,
                        importantItems
                    );
                }
                else
                {
                    textPt.DrawESPText(
                        canvas,
                        this,
                        localPlayer,
                        espSettings.ShowDistance,
                        paints.Item2,
                        (espSettings.ShowName || espSettings.ShowValue) ? label : null
                    );
                }
            }
        }

        public virtual void DrawMouseover(SKCanvas canvas, XMMapParams mapParams, LocalPlayer localPlayer)
        {
            if (this is LootContainer container)
            {
                var lines = new List<(string text, SKPaint paint)>();
                var loot = container.FilteredLoot;

                if (container is LootCorpse corpse)
                {
                    var corpseLoot = corpse.Loot?.OrderLoot();
                    var sumPrice = corpseLoot?.Sum(x => x.Price) ?? 0;
                    var corpseValue = TarkovMarketItem.FormatPrice(sumPrice);
                    var playerObj = corpse.PlayerObject;

                    if (!CorpseSettings.Enabled && (corpseLoot == null || !corpseLoot.Any()) && !HasImportantItems)
                        return;

                    if (playerObj is not null)
                    {
                        var playerTypeKey = playerObj.DeterminePlayerTypeKey();
                        var typeSettings = Config.PlayerTypeSettings.GetSettings(playerTypeKey);
                        var name = Config.MaskNames && playerObj.IsHuman ? "<Hidden>" : playerObj.Name;

                        lines.Add(($"{playerObj.Type.GetDescription()}:{name}", SKPaints.TextMouseover));
                        string g = null;
                        if (playerObj.GroupID != -1) g = $"G:{playerObj.GroupID} ";
                        if (g is not null) lines.Add((g, SKPaints.TextMouseover));
                        lines.Add(($"Value: {corpseValue}", SKPaints.TextMouseover));
                    }
                    else
                    {
                        lines.Add(($"{corpse.Name} (Value:{corpseValue})", SKPaints.TextMouseover));
                    }

                    if (corpseLoot?.Any() == true)
                    {
                        foreach (var item in corpseLoot)
                        {
                            var itemPaint = GetItemTextPaint(item);
                            lines.Add((item.GetUILabel(), itemPaint));
                        }
                    }
                    else
                    {
                        lines.Add(("Empty", SKPaints.TextMouseover));
                    }
                }
                else if (loot is not null && loot.Count() > 1)
                {
                    foreach (var item in loot)
                    {
                        var itemPaint = GetItemTextPaint(item);
                        lines.Add((item.GetUILabel(), itemPaint));
                    }
                }
                else
                {
                    return;
                }

                Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams).DrawMouseoverText(canvas, lines);
            }
            else if (this is LootItem lootItem)
            {
                var lines = new List<(string text, SKPaint paint)>();
                var itemPaint = GetItemTextPaint(lootItem);
                lines.Add((lootItem.Name, itemPaint));

                Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams).DrawMouseoverText(canvas, lines);
            }
        }

        public static void ApplyItemChams(ulong interactiveClass, int desiredMaterialId)
        {
            try
            {
                if (interactiveClass == 0)
                {
                    XMLogging.WriteLine("[ApplyItemChams] Skipped: interactiveClass is 0");
                    return;
                }
        
                var rendererList = Memory.ReadPtr(interactiveClass + 0x90);
                if (rendererList == 0)
                {
                    XMLogging.WriteLine($"[ApplyItemChams] Skipped: rendererList is 0 for {interactiveClass:X}");
                    return;
                }
        
                int rendererCount = Memory.ReadValue<int>(rendererList + 0x18);
                if (rendererCount <= 0 || rendererCount > 1000)
                {
                    XMLogging.WriteLine($"[ApplyItemChams] Skipped: invalid rendererCount ({rendererCount}) for {interactiveClass:X}");
                    return;
                }
        
                var rendererBase = Memory.ReadPtr(rendererList + 0x10);
                if (rendererBase == 0)
                {
                    XMLogging.WriteLine($"[ApplyItemChams] Skipped: rendererBase is 0 for {interactiveClass:X}");
                    return;
                }
        
                for (int i = 0; i < rendererCount; i++)
                {
                    var renderer = Memory.ReadPtr(rendererBase + 0x20 + (ulong)(i * 0x8));
                    if (renderer == 0) continue;
        
                    var materialDict = Memory.ReadPtr(renderer + 0x10);
                    if (materialDict == 0) continue;
        
                    int matCount = Memory.ReadValue<int>(materialDict + 0x158);
                    if (matCount <= 0 || matCount > 100)
                    {
                        XMLogging.WriteLine($"[ApplyItemChams] Skipped: invalid matCount ({matCount}) at {materialDict:X}");
                        continue;
                    }
        
                    var matArray = Memory.ReadPtr(materialDict + 0x148);
                    if (matArray == 0)
                    {
                        XMLogging.WriteLine($"[ApplyItemChams] Skipped: matArray is 0 at {materialDict:X}");
                        continue;
                    }
        
                    for (int j = 0; j < matCount; j++)
                    {
                        Memory.WriteValue(matArray + (ulong)(j * 0x4), desiredMaterialId);
                    }
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[ApplyItemChams] Failed for {interactiveClass:X}: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets a UI Friendly Label.
        /// </summary>
        /// <param name="showPrice">Show price in label.</param>
        /// <param name="showImportant">Show Important !! in label.</param>
        /// <returns>Item Label string cleaned up for UI usage.</returns>
        public string GetUILabel()
        {
            var label = "";
            if (this is LootContainer container)
            {
                var important = container.Loot.Any(x => x.IsImportant);
                var loot = container.FilteredLoot;

                if (this is not LootCorpse && loot.Count() == 1)
                {
                    var firstItem = loot.First();
                    label = firstItem.ShortName;
                }
                else
                {
                    label = container.Name;
                }
            }
            else
            {
                if (Price > 0)
                    label += $"[{TarkovMarketItem.FormatPrice(Price)}] ";

                label += ShortName;
            }

            if (string.IsNullOrEmpty(label))
                label = "Item";

            return label;
        }

        private string GetEntityUILabel(EntityTypeSettings settings)
        {
            var label = "";
            if (this is LootContainer container)
            {
                var important = container.Loot.Any(x => x.IsImportant);

                if (this is LootCorpse corpse)
                {
                    int sumPrice = corpse.Loot?.Sum(x => x.Price) ?? 0;

                    if (settings.ShowName && settings.ShowValue && sumPrice > 0)
                    {
                        var typeSettings = Config.PlayerTypeSettings.GetSettings("Corpse");
                        var name = Config.MaskNames && (corpse.PlayerObject is not null && corpse.PlayerObject.IsHuman) ? "<Hidden>" : corpse.Name;
                        label = $"{name} [{TarkovMarketItem.FormatPrice(sumPrice)}]";
                    }
                    else if (settings.ShowName)
                    {
                        var typeSettings = Config.PlayerTypeSettings.GetSettings("Corpse");
                        label = Config.MaskNames && (corpse.PlayerObject is not null && corpse.PlayerObject.IsHuman) ? "<Hidden>" : corpse.Name;
                    }
                    else if (settings.ShowValue && sumPrice > 0)
                    {
                        label = $"[{TarkovMarketItem.FormatPrice(sumPrice)}]";
                    }
                }
                else
                {
                    var loot = container.FilteredLoot;

                    if (settings.ShowName || settings.ShowValue)
                    {
                        if (loot.Count() == 1)
                        {
                            var firstItem = loot.First();

                            if (settings.ShowValue && firstItem.Price > 0)
                            {
                                if (settings.ShowName)
                                    label = $"{firstItem.ShortName} [{TarkovMarketItem.FormatPrice(firstItem.Price)}]";
                                else
                                    label = $"[{TarkovMarketItem.FormatPrice(firstItem.Price)}]";
                            }
                            else if (settings.ShowName)
                            {
                                label = firstItem.ShortName;
                            }
                        }
                        else
                        {
                            label = container.Name;
                        }
                    }
                }
            }
            else
            {
                if (settings.ShowValue && Price > 0)
                {
                    if (settings.ShowName)
                        label = $"{ShortName} [{TarkovMarketItem.FormatPrice(Price)}]";
                    else
                        label = $"[{TarkovMarketItem.FormatPrice(Price)}]";
                }
                else if (settings.ShowName)
                {
                    label = ShortName;
                }
            }

            return label;
        }

        private string GetEntityUILabel(EntityTypeSettingsESP settings)
        {
            var label = "";
            if (this is LootContainer container)
            {
                var important = container.Loot.Any(x => x.IsImportant);

                if (this is LootCorpse corpse)
                {
                    int sumPrice = corpse.Loot?.Sum(x => x.Price) ?? 0;

                    if (settings.ShowName && settings.ShowValue && sumPrice > 0)
                    {
                        label = $"{corpse.Name} [{TarkovMarketItem.FormatPrice(sumPrice)}]";
                    }
                    else if (settings.ShowName)
                    {
                        label = corpse.Name;
                    }
                    else if (settings.ShowValue && sumPrice > 0)
                    {
                        label = $"[{TarkovMarketItem.FormatPrice(sumPrice)}]";
                    }
                }
                else
                {
                    var loot = container.FilteredLoot;

                    if (settings.ShowName || settings.ShowValue)
                    {
                        if (loot.Count() == 1)
                        {
                            var firstItem = loot.First();

                            if (settings.ShowValue && firstItem.Price > 0)
                            {
                                if (settings.ShowName)
                                    label = $"{firstItem.ShortName} [{TarkovMarketItem.FormatPrice(firstItem.Price)}]";
                                else
                                    label = $"[{TarkovMarketItem.FormatPrice(firstItem.Price)}]";
                            }
                            else if (settings.ShowName)
                            {
                                label = firstItem.ShortName;
                            }
                        }
                        else
                        {
                            label = container.Name;
                        }
                    }
                }
            }
            else
            {
                if (settings.ShowValue && Price > 0)
                {
                    if (settings.ShowName)
                        label = $"{ShortName} [{TarkovMarketItem.FormatPrice(Price)}]";
                    else
                        label = $"[{TarkovMarketItem.FormatPrice(Price)}]";
                }
                else if (settings.ShowName)
                {
                    label = ShortName;
                }
            }

            return label;
        }

        private static SKColor? GetCorpseFilterColor(LootCorpse corpse)
        {
            if (corpse.Loot != null && corpse.Loot.Any())
            {
                var topItem = corpse.Loot.OrderLoot().FirstOrDefault();

                if (topItem != null)
                {
                    var matchedFilter = topItem.MatchedFilter;
                    if (matchedFilter != null && !string.IsNullOrEmpty(matchedFilter.Color))
                    {
                        if (SKColor.TryParse(matchedFilter.Color, out var filterColor))
                            return filterColor;
                    }

                    if (topItem is QuestItem || (Config.QuestHelper.Enabled && topItem.IsQuestCondition))
                        return SKPaints.PaintQuestItem.Color;

                    if (topItem.IsWishlisted)
                        return SKPaints.PaintWishlistItem.Color;

                    if (topItem.IsValuableLoot)
                        return SKPaints.PaintImportantLoot.Color;
                }
            }

            return null;
        }

        /// <summary>
        /// Helper method to get the appropriate text paint for an item based on its importance/filter
        /// </summary>
        private static SKPaint GetItemTextPaint(LootItem item)
        {
            var isImportant = (!item.IsGroupedBlacklisted && item.MatchedFilter?.Color != null && !string.IsNullOrEmpty(item.MatchedFilter.Color)) ||
                               item is QuestItem ||
                               (Config.QuestHelper.Enabled && item.IsQuestCondition) ||
                               item.IsWishlisted ||
                               (LootFilterControl.ShowBackpacks && item.IsBackpack) ||
                               (LootFilterControl.ShowMeds && item.IsMeds) ||
                               (LootFilterControl.ShowFood && item.IsFood) ||
                               (LootFilterControl.ShowWeapons && item.IsWeapon) ||
                               item.IsValuableLoot;

            return isImportant ? item.GetPaints().Item2 : SKPaints.TextMouseover;
        }

        public ValueTuple<SKPaint, SKPaint> GetPaints()
        {
            if (this is LootAirdrop)
                return new(SKPaints.PaintAirdrop, SKPaints.TextAirdrop);

            if (this is LootCorpse corpse)
            {
                var filterColor = GetCorpseFilterColor(corpse);
                if (filterColor.HasValue)
                {
                    var filterPaints = GetFilterPaints(filterColor.Value.ToString());
                    return new(filterPaints.Item1, filterPaints.Item2);
                }
                return new(SKPaints.PaintCorpse, SKPaints.TextCorpse);
            }

            if (this is QuestItem)
                return new(SKPaints.QuestHelperPaint, SKPaints.QuestHelperText);
            if (Config.QuestHelper.Enabled && IsQuestCondition)
                return new(SKPaints.PaintQuestItem, SKPaints.TextQuestItem);
            if (IsWishlisted)
                return new(SKPaints.PaintWishlistItem, SKPaints.TextWishlistItem);
            if (LootFilterControl.ShowBackpacks && IsBackpack)
                return new(SKPaints.PaintBackpacks, SKPaints.TextBackpacks);
            if (LootFilterControl.ShowMeds && IsMeds)
                return new(SKPaints.PaintMeds, SKPaints.TextMeds);
            if (LootFilterControl.ShowFood && IsFood)
                return new(SKPaints.PaintFood, SKPaints.TextFood);
            if (LootFilterControl.ShowWeapons && IsWeapon)
                return new(SKPaints.PaintWeapons, SKPaints.TextWeapons);

            var color = this is LootContainer ctr
                ? ctr.Loot.FirstOrDefault(x => x.Important)?.MatchedFilter?.Color
                : MatchedFilter?.Color;

            if (!string.IsNullOrEmpty(color))
            {
                var filterPaints = GetFilterPaints(color);
                return new(filterPaints.Item1, filterPaints.Item2);
            }

            if (IsValuableLoot)
                return new(SKPaints.PaintImportantLoot, SKPaints.TextImportantLoot);

            return new(SKPaints.PaintLoot, SKPaints.TextLoot);
        }

        public SKPaint GetMiniRadarPaint()
        {
            if (this is LootAirdrop)
                return SKPaints.PaintMiniAirdrop;

            if (this is LootCorpse corpse)
            {
                var filterColor = GetCorpseFilterColor(corpse);
                if (filterColor.HasValue)
                {
                    var filterPaints = GetFilterPaints(filterColor.Value.ToString());
                    return filterPaints.Item1;
                }
                return SKPaints.PaintMiniCorpse;
            }

            if (this is QuestItem)
                return SKPaints.MiniQuestHelperPaint;
            if (Config.QuestHelper.Enabled && IsQuestCondition)
                return SKPaints.PaintMiniQuestItem;
            if (IsWishlisted)
                return SKPaints.PaintMiniWishlistItem;
            if (LootFilterControl.ShowBackpacks && IsBackpack)
                return SKPaints.PaintMiniBackpacks;
            if (LootFilterControl.ShowMeds && IsMeds)
                return SKPaints.PaintMiniMeds;
            if (LootFilterControl.ShowFood && IsFood)
                return SKPaints.PaintMiniFood;
            if (LootFilterControl.ShowWeapons && IsWeapon)
                return SKPaints.PaintMiniWeapons;

            var color = this is LootContainer ctr
                ? ctr.Loot.FirstOrDefault(x => x.Important)?.MatchedFilter?.Color
                : MatchedFilter?.Color;

            if (!string.IsNullOrEmpty(color))
            {
                var filterPaints = GetFilterPaints(color);
                return filterPaints.Item1;
            }

            if (IsValuableLoot)
                return SKPaints.PaintMiniImportantLoot;

            return SKPaints.PaintMiniLoot;
        }

        public ValueTuple<SKPaint, SKPaint> GetESPPaints()
        {
            if (this is LootAirdrop)
                return new(SKPaints.PaintAirdropESP, SKPaints.TextAirdropESP);

            if (this is LootCorpse corpse)
            {
                var filterColor = GetCorpseFilterColor(corpse);
                if (filterColor.HasValue)
                {
                    var filterPaints = GetFilterPaints(filterColor.Value.ToString());
                    return new(filterPaints.Item3, filterPaints.Item4);
                }
                return new(SKPaints.PaintCorpseESP, SKPaints.TextCorpseESP);
            }

            if (this is QuestItem)
                return new(SKPaints.PaintQuestHelperESP, SKPaints.TextQuestHelperESP);
            if (Config.QuestHelper.Enabled && IsQuestCondition)
                return new(SKPaints.PaintQuestItemESP, SKPaints.TextQuestItemESP);
            if (IsWishlisted)
                return new(SKPaints.PaintWishlistItemESP, SKPaints.TextWishlistItemESP);
            if (LootFilterControl.ShowBackpacks && IsBackpack)
                return new(SKPaints.PaintBackpackESP, SKPaints.TextBackpackESP);
            if (LootFilterControl.ShowMeds && IsMeds)
                return new(SKPaints.PaintMedsESP, SKPaints.TextMedsESP);
            if (LootFilterControl.ShowFood && IsFood)
                return new(SKPaints.PaintFoodESP, SKPaints.TextFoodESP);
            if (LootFilterControl.ShowWeapons && IsWeapon)
                return new(SKPaints.PaintWeaponsESP, SKPaints.TextWeaponsESP);

            var color = this is LootContainer ctr
                ? ctr.Loot.FirstOrDefault(x => x.Important)?.MatchedFilter?.Color
                : MatchedFilter?.Color;

            if (!string.IsNullOrEmpty(color))
            {
                var filterPaints = GetFilterPaints(color);
                return new(filterPaints.Item3, filterPaints.Item4);
            }

            return IsImportant || IsValuableLoot ? new(SKPaints.PaintImpLootESP, SKPaints.TextImpLootESP) : new(SKPaints.PaintLootESP, SKPaints.TextLootESP);
        }

        public static void ClearPaintCache()
        {
            _paints.Clear();
        }

        public void CheckNotify()
        {
            var matched = GetMatchedGroupAndEntry();
            if (matched == null)
                return;

            var (group, entry) = matched.Value;

            if (!group.Enabled || !entry.Enabled || !group.Notify || !entry.Notify)
                return;

            var now = DateTime.UtcNow;
            var notifyKey = ID;

            if (group.NotTime == 0)
            {
                if (_lastNotifyTimes.ContainsKey(notifyKey))
                    return;

                _lastNotifyTimes[notifyKey] = now;
                NotificationsShared.Info($"[Loot] {entry.Name} found ({TarkovMarketItem.FormatPrice(Price)})");
                return;
            }

            var interval = group.NotTime;

            if (_lastNotifyTimes.TryGetValue(notifyKey, out var lastNotifyTime))
            {
                if ((now - lastNotifyTime).TotalSeconds < interval)
                    return;
            }

            _lastNotifyTimes[notifyKey] = now;
            NotificationsShared.Info($"[Loot] {entry.Name} present ({TarkovMarketItem.FormatPrice(Price)})");
        }

        /// <summary>
        /// Clears notification history for items that are no longer present
        /// Call this periodically or when loot is refreshed
        /// </summary>
        public static void CleanupNotificationHistory(IEnumerable<LootItem> currentLoot)
        {
            if (currentLoot == null)
            {
                _lastNotifyTimes.Clear();
                return;
            }

            var currentItemIds = new HashSet<string>(currentLoot.Select(item => item.ID));
            var keysToRemove = _lastNotifyTimes.Keys.Where(key => !currentItemIds.Contains(key)).ToList();

            foreach (var key in keysToRemove)
            {
                _lastNotifyTimes.Remove(key);
            }

            if (keysToRemove.Count > 0)
            {
                XMLogging.WriteLine($"[Notifications] Cleaned up {keysToRemove.Count} old notification entries");
            }
        }

        /// <summary>
        /// Clears all notification history (useful when starting a new raid)
        /// </summary>
        public static void ClearNotificationHistory()
        {
            _lastNotifyTimes.Clear();
            XMLogging.WriteLine("[Notifications] Cleared all notification history");
        }

        #region Custom Loot Paints
        private static readonly ConcurrentDictionary<string, Tuple<SKPaint, SKPaint, SKPaint, SKPaint>> _paints = new();

        /// <summary>
        /// Returns the Paints for this color value.
        /// </summary>
        /// <param name="color">Color rgba hex string.</param>
        /// <returns>Tuple of paints. Item1 = Paint, Item2 = Text. Item3 = ESP Paint, Item4 = ESP Text</returns>
        private static Tuple<SKPaint, SKPaint, SKPaint, SKPaint> GetFilterPaints(string color)
        {
            if (!SKColor.TryParse(color, out var skColor))
                return new Tuple<SKPaint, SKPaint, SKPaint, SKPaint>(SKPaints.PaintLoot, SKPaints.TextLoot, SKPaints.PaintLootESP, SKPaints.TextBasicESP);
            var result = _paints.AddOrUpdate(color,
                key =>
                {
                    var paint = new SKPaint
                    {
                        Color = skColor,
                        StrokeWidth = 3f * MainWindow.UIScale,
                        Style = SKPaintStyle.Fill,
                        IsAntialias = true,
                        FilterQuality = SKFilterQuality.High
                    };
                    var text = new SKPaint
                    {
                        SubpixelText = true,
                        Color = skColor,
                        IsStroke = false,
                        TextSize = 12f * MainWindow.UIScale,
                        TextEncoding = SKTextEncoding.Utf8,
                        IsAntialias = true,
                        Typeface = CustomFonts.SKFontFamilyRegular,
                        FilterQuality = SKFilterQuality.High
                    };
                    var espPaint = new SKPaint()
                    {
                        Color = skColor,
                        StrokeWidth = 0.25f,
                        Style = SKPaintStyle.Fill,
                        IsAntialias = true,
                        FilterQuality = SKFilterQuality.High
                    };
                    var espText = new SKPaint()
                    {
                        SubpixelText = true,
                        Color = skColor,
                        IsStroke = false,
                        TextSize = 12f,
                        TextAlign = SKTextAlign.Center,
                        TextEncoding = SKTextEncoding.Utf8,
                        IsAntialias = true,
                        Typeface = CustomFonts.SKFontFamilyMedium,
                        FilterQuality = SKFilterQuality.High
                    };
                    return new Tuple<SKPaint, SKPaint, SKPaint, SKPaint>(paint, text, espPaint, espText);
                },
                (key, existingValue) =>
                {
                    existingValue.Item1.StrokeWidth = 3f * MainWindow.UIScale;
                    existingValue.Item2.TextSize = 12f * MainWindow.UIScale;
                    existingValue.Item4.TextSize = 12f; // * ESP.Config.FontScale;
                    return existingValue;
                });
            return result;
        }
        #endregion
    }

    public static class LootItemExtensions
    {
        /// <summary>
        /// Order loot (important first, then by price).
        /// </summary>
        /// <param name="loot"></param>
        /// <returns>Ordered loot.</returns>
        public static IEnumerable<LootItem> OrderLoot(this IEnumerable<LootItem> loot)
        {
            return loot
                .OrderByDescending(x => x.IsImportant && !x.IsWishlisted)
                .ThenByDescending(x => (Program.Config.QuestHelper.Enabled && x.IsQuestCondition))
                .ThenByDescending(x => x.IsWishlisted)
                .ThenByDescending(x => x.Price);
        }
    }
}