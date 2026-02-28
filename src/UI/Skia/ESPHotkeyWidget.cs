using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data.EFT;
using eft_dma_radar.Tarkov.Features.MemoryWrites;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.Pages;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.Collections.Generic;
using System.Linq;

namespace eft_dma_radar.UI.ESP
{
    public sealed class ESPHotkeyWidget : ESPWidget
    {
        private static Config Config => Program.Config;
        private readonly float _padding;
        private bool _showKey = true;
        private bool _showKeyType = true;
        private bool _onlyActive = false;
        private float _lastCalculatedHeight = 300f;

        /// <summary>
        /// Constructs an ESP Hotkey Widget.
        /// </summary>
        public ESPHotkeyWidget(SKGLControl parent, SKPoint location, bool minimized, float scale)
            : base(parent, "Hotkeys", location, new SKSize(350, 300), scale, true)
        {
            Minimized = minimized;
            _padding = 6f * scale;
            SetScaleFactor(scale);
        }

        public override void SetScaleFactor(float scale)
        {
            base.SetScaleFactor(scale);

            lock (_hotkeyTextPaint)
            {
                _hotkeyTextPaint.TextSize = 13 * scale;
            }

            lock (_hotkeyActivePaint)
            {
                _hotkeyActivePaint.TextSize = 13 * scale;
            }

            lock (_hotkeyInactivePaint)
            {
                _hotkeyInactivePaint.TextSize = 13 * scale;
            }

            lock (_hotkeyKeyPaint)
            {
                _hotkeyKeyPaint.TextSize = 13 * scale;
            }

            lock (_hotkeyTypePaint)
            {
                _hotkeyTypePaint.TextSize = 13 * scale;
            }

            _lastCalculatedHeight = 0f;
        }

        /// <summary>
        /// Calculate the required height for the widget based on content
        /// </summary>
        private float CalculateRequiredHeight(List<HotkeyDisplayModel> hotkeysToDisplay)
        {
            var lineSpacing = _hotkeyTextPaint.FontSpacing;
            var height = _padding * 2;
            height += lineSpacing * 1.5f;

            if (hotkeysToDisplay?.Any() == true)
                height += hotkeysToDisplay.Count * lineSpacing;
            else
                height += lineSpacing;

            height += lineSpacing * 0.5f;

            return height;
        }

        /// <summary>
        /// Update widget size if the required height has changed
        /// </summary>
        private void UpdateWidgetSize(float requiredHeight)
        {
            if (Math.Abs(requiredHeight - _lastCalculatedHeight) > 1f)
            {
                var newSize = new SKSize(Size.Width, requiredHeight);

                if (this.GetType().BaseType?.GetMethod("UpdateSize") != null)
                {
                    this.GetType().BaseType.GetMethod("UpdateSize")?.Invoke(this, new object[] { newSize });
                }
                else
                {
                    var sizeProperty = this.GetType().BaseType?.GetProperty("Size");
                    if (sizeProperty?.CanWrite == true)
                        sizeProperty.SetValue(this, newSize);
                }

                _lastCalculatedHeight = requiredHeight;
            }
        }

        public override void Draw(SKCanvas canvas)
        {
            base.Draw(canvas);

            if (Minimized)
                return;

            var mainWindow = MainWindow.Window;
            var generalSettingsControl = mainWindow?.GeneralSettingsControl;

            var hotkeyList = generalSettingsControl?.hotkeyListView?.ItemsSource as System.Collections.ObjectModel.ObservableCollection<HotkeyDisplayModel>;
            var hotkeysToDisplay = new List<HotkeyDisplayModel>();

            if (hotkeyList?.Count > 0)
            {
                hotkeysToDisplay = hotkeyList.ToList();

                if (_onlyActive)
                    hotkeysToDisplay = hotkeysToDisplay.Where(h => IsHotkeyActive(h.Action, generalSettingsControl)).ToList();
            }

            var requiredHeight = CalculateRequiredHeight(hotkeysToDisplay);
            UpdateWidgetSize(requiredHeight);

            if (generalSettingsControl?.hotkeyListView?.ItemsSource == null || hotkeyList == null || hotkeyList.Count == 0)
            {
                canvas.Save();
                canvas.ClipRect(ClientRectangle);

                var emptyLineSpacing = _hotkeyTextPaint.FontSpacing;
                var emptyDrawPt = new SKPoint(ClientRectangle.Left + _padding, ClientRectangle.Top + emptyLineSpacing * 0.8f + _padding);

                var emptyText = "No hotkeys configured";
                canvas.DrawText(emptyText, emptyDrawPt, _hotkeyTextPaint);

                canvas.Restore();
                return;
            }

            canvas.Save();
            canvas.ClipRect(ClientRectangle);

            var lineSpacing = _hotkeyTextPaint.FontSpacing;
            var drawPt = new SKPoint(ClientRectangle.Left + _padding, ClientRectangle.Top + lineSpacing * 0.8f + _padding);

            var showKeySymbol = _showKey ? "[x]" : "[ ]";
            var showKeyTypeSymbol = _showKeyType ? "[x]" : "[ ]";
            var onlyActiveSymbol = _onlyActive ? "[x]" : "[ ]";

            var filtersText = $"Filters: {showKeySymbol} Show Key  {showKeyTypeSymbol} Show Type  {onlyActiveSymbol} Only Active";
            canvas.DrawText(filtersText, drawPt, _hotkeyTextPaint);

            drawPt.Y += lineSpacing * 1f;

            var nameColumnWidth = hotkeysToDisplay.Any() ? hotkeysToDisplay.Max(x => _hotkeyTextPaint.MeasureText(x.Action)) : 100f;
            var keyColumnWidth = _showKey && hotkeysToDisplay.Any() ? hotkeysToDisplay.Max(x => _hotkeyKeyPaint.MeasureText($"[{x.Key}]")) : 0f;
            var typeColumnWidth = _showKeyType && hotkeysToDisplay.Any() ? hotkeysToDisplay.Max(x => _hotkeyTypePaint.MeasureText($"({x.Type})")) : 0f;

            var columnPadding = 15f * ScaleFactor;

            foreach (var hotkey in hotkeysToDisplay)
            {
                var isActive = IsHotkeyActive(hotkey.Action, generalSettingsControl);
                var textPaint = isActive ? _hotkeyActivePaint : _hotkeyInactivePaint;

                var currentX = drawPt.X;

                canvas.DrawText(hotkey.Action, currentX, drawPt.Y, textPaint);
                currentX += nameColumnWidth + columnPadding;

                if (_showKey)
                {
                    canvas.DrawText($"[{hotkey.Key}]", currentX, drawPt.Y, _hotkeyKeyPaint);
                    currentX += keyColumnWidth + columnPadding;
                }

                if (_showKeyType)
                    canvas.DrawText($"({hotkey.Type})", currentX, drawPt.Y, _hotkeyTypePaint);

                drawPt.Y += lineSpacing;
            }

            canvas.Restore();
        }

        public override bool HandleClientAreaClick(SKPoint point)
        {
            var lineSpacing = _hotkeyTextPaint.FontSpacing;
            var startY = ClientRectangle.Top + lineSpacing * 0.8f + _padding;
            var filterLineY = startY;

            if (point.Y >= filterLineY - lineSpacing / 2 && point.Y <= filterLineY + lineSpacing / 2)
            {
                var startX = ClientRectangle.Left + _padding;
                var currentX = startX;

                var filtersText = "Filters: ";
                var filtersWidth = _hotkeyTextPaint.MeasureText(filtersText);
                currentX += filtersWidth;

                var showKeyCheckbox = _showKey ? "[x] Show Key  " : "[ ] Show Key  ";
                var showKeyWidth = _hotkeyTextPaint.MeasureText(showKeyCheckbox);
                if (point.X >= currentX && point.X <= currentX + showKeyWidth)
                {
                    _showKey = !_showKey;
                    _lastCalculatedHeight = 0f;
                    return true;
                }

                currentX += showKeyWidth;

                var showKeyTypeCheckbox = _showKeyType ? "[x] Show Type  " : "[ ] Show Type  ";
                var showKeyTypeWidth = _hotkeyTextPaint.MeasureText(showKeyTypeCheckbox);
                if (point.X >= currentX && point.X <= currentX + showKeyTypeWidth)
                {
                    _showKeyType = !_showKeyType;
                    _lastCalculatedHeight = 0f;
                    return true;
                }

                currentX += showKeyTypeWidth;

                var onlyActiveCheckbox = _onlyActive ? "[x] Only Active" : "[ ] Only Active";
                var onlyActiveWidth = _hotkeyTextPaint.MeasureText(onlyActiveCheckbox);
                if (point.X >= currentX && point.X <= currentX + onlyActiveWidth)
                {
                    _onlyActive = !_onlyActive;
                    _lastCalculatedHeight = 0f;
                    return true;
                }
            }

            return false;
        }

        private static bool IsHotkeyActive(string actionName, GeneralSettingsControl generalSettingsControl)
        {
            try
            {
                var configActive = CheckFeatureActiveState(actionName);

                if (configActive)
                    return true;

                var toggleStatesField = typeof(GeneralSettingsControl)
                    .GetField("_toggleStates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (toggleStatesField?.GetValue(generalSettingsControl) is Dictionary<string, bool> toggleStates)
                {
                    var configName = GetHotkeyConfigName(actionName);
                    if (toggleStates.TryGetValue(configName, out bool toggleState))
                        return toggleState;
                }

                return false;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"Exception in IsHotkeyActive: {ex.Message}");
                return false;
            }
        }

        private static string GetHotkeyConfigName(string displayName)
        {
            var configName = displayName.Replace(" ", "");
            var mapping = new Dictionary<string, string>
            {
                // Loot
                { "ShowLoot", nameof(HotkeyConfig.ShowLoot) },
                { "ShowWishlistLoot", nameof(HotkeyConfig.ShowWishlistLoot) },
                { "ShowMeds", nameof(HotkeyConfig.ShowMeds) },
                { "ShowFood", nameof(HotkeyConfig.ShowFood) },
                { "ShowWeapons", nameof(HotkeyConfig.ShowWeapons) },
                { "ShowBackpacks", nameof(HotkeyConfig.ShowBackpacks) },
                { "ShowContainers", nameof(HotkeyConfig.ShowContainers) },
                { "ImportantCorpseLoot", nameof(HotkeyConfig.ImportantCorpseLoot) },
                { "ImportantPlayerLoot", nameof(HotkeyConfig.ImportantPlayerLoot) },

                // ESP
                { "ToggleFuserESP", nameof(HotkeyConfig.ToggleFuserESP) },
                { "MiniRadarZoomIn", nameof(HotkeyConfig.MiniRadarZoomIn) },
                { "MiniRadarZoomOut", nameof(HotkeyConfig.MiniRadarZoomOut) },

                // Memory Writes - Global
                { "ToggleRageMode", nameof(HotkeyConfig.ToggleRageMode) },

                // Memory Writes - Aimbot
                { "ToggleAimbot", nameof(HotkeyConfig.ToggleAimbot) },
                { "EngageAimbot", nameof(HotkeyConfig.EngageAimbot) },
                { "ToggleAimbotMode", nameof(HotkeyConfig.ToggleAimbotMode) },
                { "AimbotBone", nameof(HotkeyConfig.AimbotBone) },
                { "SafeLock", nameof(HotkeyConfig.SafeLock) },
                { "RandomBone", nameof(HotkeyConfig.RandomBone) },
                { "AutoBone", nameof(HotkeyConfig.AutoBone) },
                { "HeadshotAI", nameof(HotkeyConfig.HeadshotAI) },

                // Memory Writes - Weapons
                { "NoMalfunctions", nameof(HotkeyConfig.NoMalfunctions) },
                { "FastWeaponOps", nameof(HotkeyConfig.FastWeaponOps) },
                { "DisableWeaponCollision", nameof(HotkeyConfig.DisableWeaponCollision) },
                { "NoRecoil", nameof(HotkeyConfig.NoRecoil) },

                // Memory Writes - Movement
                { "InfiniteStamina", nameof(HotkeyConfig.InfiniteStamina) },
                { "WideLean", nameof(HotkeyConfig.WideLean) },
                { "WideLeanUp", nameof(HotkeyConfig.WideLeanUp) },
                { "WideLeanRight", nameof(HotkeyConfig.WideLeanRight) },
                { "WideLeanLeft", nameof(HotkeyConfig.WideLeanLeft) },
                { "MoveSpeed", nameof(HotkeyConfig.MoveSpeed) },

                // Memory Writes - World
                { "DisableShadows", nameof(HotkeyConfig.DisableShadows) },
                { "DisableGrass", nameof(HotkeyConfig.DisableGrass) },
                { "ClearWeather", nameof(HotkeyConfig.ClearWeather) },
                { "TimeOfDay", nameof(HotkeyConfig.TimeOfDay) },
                { "FullBright", nameof(HotkeyConfig.FullBright) },
                { "LootThroughWalls", nameof(HotkeyConfig.LootThroughWalls) },
                { "ExtendedReach", nameof(HotkeyConfig.ExtendedReach) },
                { "EngageLTW", nameof(HotkeyConfig.EngageLTW) },

                // Memory Writes - Camera
                { "NoVisor", nameof(HotkeyConfig.NoVisor) },
                { "NightVision", nameof(HotkeyConfig.NightVision) },
                { "ThermalVision", nameof(HotkeyConfig.ThermalVision) },
                { "ThirdPerson", nameof(HotkeyConfig.ThirdPerson) },
                { "OwlMode", nameof(HotkeyConfig.OwlMode) },
                { "InstantZoom", nameof(HotkeyConfig.InstantZoom) },

                // Memory Writes - Misc
                { "BigHeads", nameof(HotkeyConfig.BigHeads) },

                // General Settings
                { "AimviewWidget", nameof(HotkeyConfig.AimviewWidget) },
                { "DebugWidget", nameof(HotkeyConfig.DebugWidget) },
                { "PlayerInfoWidget", nameof(HotkeyConfig.PlayerInfoWidget) },
                { "ConnectGroups", nameof(HotkeyConfig.ConnectGroups) },
                { "MaskNames", nameof(HotkeyConfig.MaskNames) },
                { "ZoomOut", nameof(HotkeyConfig.ZoomOut) },
                { "ZoomIn", nameof(HotkeyConfig.ZoomIn) },
                { "BattleMode", nameof(HotkeyConfig.BattleMode) },
                { "QuestHelper", nameof(HotkeyConfig.QuestHelper) }
            };

            return mapping.TryGetValue(configName, out var mappedName) ? mappedName : configName;
        }

        private static bool CheckFeatureActiveState(string actionName)
        {
            try
            {
                switch (actionName)
                {
                    // Loot
                    case "Show Loot":
                        return Config.ProcessLoot;
                    case "Show Wishlist Loot":
                        return Config.LootWishlist;
                    case "Show Meds":
                        return LootFilterControl.ShowMeds;
                    case "Show Food":
                        return LootFilterControl.ShowFood;
                    case "Show Weapons":
                        return LootFilterControl.ShowWeapons;
                    case "Show Backpacks":
                        return LootFilterControl.ShowBackpacks;
                    case "Show Containers":
                        return Config.Containers.Show;
                    case "Important Corpse Loot":
                        return Config.EntityTypeSettings?.GetSettings("Corpse")?.ShowImportantLoot ?? false;
                    case "Important Player Loot":
                        return Config.PlayerTypeSettings?.Settings?.Values?.Any(s => s.ShowImportantLoot) ?? false;

                    // Fuser ESP
                    case "Toggle Fuser ESP":
                        return ESPForm.ShowESP;
                    case "Mini Radar Zoom In":
                    case "Mini Radar Zoom Out":
                        return false;
                    case "Fuser Quest Info":
                        return Config.ESP.ShowQuestInfoWidget;
                    case "Fuser Important Corpse Loot":
                        return Config.ESP.EntityTypeESPSettings?.GetSettings("Corpse")?.ShowImportantLoot ?? false;
                    case "Fuser Important Player Loot":
                        return Config.ESP.PlayerTypeESPSettings?.Settings?.Values?.Any(s => s.ShowImportantLoot) ?? false;

                    // Memory Writes - Global
                    case "Toggle Rage Mode":
                        return Config.MemWrites.RageMode;

                    // Memory Writes - Aimbot
                    case "Toggle Aimbot":
                        return Config.MemWrites.Aimbot.Enabled;
                    case "Engage Aimbot":
                        return Aimbot.Engaged;
                    case "Toggle Aimbot Mode":
                        return false;
                    case "Aimbot Bone":
                        return false;
                    case "Safe Lock":
                        return Config.MemWrites.Aimbot.SilentAim.SafeLock;
                    case "Random Bone":
                        return Config.MemWrites.Aimbot.RandomBone.Enabled;
                    case "Auto Bone":
                        return Config.MemWrites.Aimbot.SilentAim.AutoBone;
                    case "Headshot AI":
                        return Config.MemWrites.Aimbot.HeadshotAI;

                    // Memory Writes - Weapons
                    //case "No Malfunctions":
                    //    return Config.MemWrites.NoWeaponMalfunctions;
                    case "Fast Weapon Ops":
                        return Config.MemWrites.FastWeaponOps;
                    case "Disable Weapon Collision":
                        return Config.MemWrites.DisableWeaponCollision;
                    case "No Recoil":
                        return Config.MemWrites.NoRecoil;

                    // Memory Writes - Movement
                    case "Infinite Stamina":
                        return Config.MemWrites.InfStamina;
                    case "Wide Lean":
                        return Config.MemWrites.WideLean.Enabled;
                    case "Wide Lean Up":
                        return Config.MemWrites.WideLean.Enabled && WideLean.Direction == WideLean.EWideLeanDirection.Up;
                    case "Wide Lean Right":
                        return Config.MemWrites.WideLean.Enabled && WideLean.Direction == WideLean.EWideLeanDirection.Right;
                    case "Wide Lean Left":
                        return Config.MemWrites.WideLean.Enabled && WideLean.Direction == WideLean.EWideLeanDirection.Left;
                    //case "Move Speed":
                    //    return Config.MemWrites.MoveSpeed.Enabled;

                    // Memory Writes - World
                    //case "Disable Shadows":
                    //    return Config.MemWrites.DisableShadows;
                    case "Disable Grass":
                        return Config.MemWrites.DisableGrass;
                    case "Clear Weather":
                        return Config.MemWrites.ClearWeather;
                    case "Time Of Day":
                        return Config.MemWrites.TimeOfDay.Enabled;
                    case "Full Bright":
                        return Config.MemWrites.FullBright.Enabled;
                   //case "Loot Through Walls":
                   //    return Config.MemWrites.LootThroughWalls.Enabled;
                   //case "Extended Reach":
                   //    return Config.MemWrites.ExtendedReach.Enabled;
                   //case "Engage LTW":
                   //    return LootThroughWalls.ZoomEngaged;

                    // Memory Writes - Camera
                    case "No Visor":
                        return Config.MemWrites.NoVisor;
                    case "Night Vision":
                        return Config.MemWrites.NightVision;
                    case "Thermal Vision":
                        return Config.MemWrites.ThermalVision;
                    case "Third Person":
                        return Config.MemWrites.ThirdPerson;
                    case "Owl Mode":
                        return Config.MemWrites.OwlMode;
                    //case "Instant Zoom":
                    //    return Config.MemWrites.FOV.InstantZoomActive;

                    // Memory Writes - Misc
                    case "Big Heads":
                        return Config.MemWrites.BigHead.Enabled;

                    // General Settings
                    case "Aimview Widget":
                        return Config.AimviewWidgetEnabled;
                    case "Debug Widget":
                        return Config.ShowDebugWidget;
                    case "Player Info Widget":
                        return Config.ShowInfoTab;
                    case "Loot Info Widget":
                        return Config.ShowLootInfoWidget;
                    case "Quest Info Widget":
                        return Config.ShowQuestInfoWidget;
                    case "Connect Groups":
                        return Config.ConnectGroups;
                    case "Mask Names":
                        return Config.MaskNames;
                    case "Players On Top":
                        return Config.PlayersOnTop;
                    case "Zoom Out":
                    case "Zoom In":
                        return false;
                    case "Battle Mode":
                        return Config.BattleMode;
                    case "Quest Helper":
                        return Config.QuestHelper.Enabled;

                    default:
                        XMLogging.WriteLine($"Unknown action name in CheckFeatureActiveState: '{actionName}'");
                        return false;
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"Exception in CheckFeatureActiveState for '{actionName}': {ex.Message}");
                return false;
            }
        }

        #region Static Paint Objects
        private static readonly SKPaint _hotkeyTextPaint = new()
        {
            SubpixelText = true,
            Color = SKColors.White,
            IsStroke = false,
            TextSize = 13,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Consolas"),
            FilterQuality = SKFilterQuality.High
        };

        private static readonly SKPaint _hotkeyActivePaint = new()
        {
            SubpixelText = true,
            Color = SKColors.LightGreen,
            IsStroke = false,
            TextSize = 13,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Consolas"),
            FilterQuality = SKFilterQuality.High
        };

        private static readonly SKPaint _hotkeyInactivePaint = new()
        {
            SubpixelText = true,
            Color = SKColors.Gray,
            IsStroke = false,
            TextSize = 13,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Consolas"),
            FilterQuality = SKFilterQuality.High
        };

        private static readonly SKPaint _hotkeyKeyPaint = new()
        {
            SubpixelText = true,
            Color = SKColors.Yellow,
            IsStroke = false,
            TextSize = 13,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Consolas"),
            FilterQuality = SKFilterQuality.High
        };

        private static readonly SKPaint _hotkeyTypePaint = new()
        {
            SubpixelText = true,
            Color = SKColors.Cyan,
            IsStroke = false,
            TextSize = 13,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Consolas"),
            FilterQuality = SKFilterQuality.High
        };
        #endregion
    }
}