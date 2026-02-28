using eft_dma_radar.UI.Misc;
using SkiaSharp.Views.WPF;

namespace eft_dma_radar.UI
{
    public enum EspColorOption
    {
        Visible,
        Friendly,
        USEC,
        BEAR,
        Focused,
        Streamer,
        AimbotTarget,
        Special,
        PlayerScav,
        Scav,
        Raider,
        Boss,
        RegularLoot,
        ValuableLoot,
        WishlistLoot,
        ContainerLoot,
        MedsFilterLoot,
        FoodFilterLoot,
        WeaponsFilterLoot,
        BackpackFilterLoot,
        QuestLoot,
        Airdrop,
        StaticQuestItemsAndZones,
        Corpse,
        Explosives,
        ExfilOpen,
        ExfilPending,
        ExfilClosed,
        ExfilInactive,
        ExfilTransit,
        Switches,
        DoorOpen,
        DoorLocked,
        DoorShut,
        FPS,
        RaidStats,
        StatusText,
        MagazineInfo,
        EnergyBar,
        HydrationBar,
        Crosshair,
        FireportAim,
        AimbotFOV,
        AimbotLock,
        ClosestPlayer,
        TopLoot,
        MiniRadarTheme
    }

    internal static class EspColorOptions
    {
        #region Static Interfaces

        /// <summary>
        /// Load all ESP Color Config. Run once at start of application.
        /// </summary>
        internal static void LoadColors(Config config)
        {
            config.ESP.Colors ??= new Dictionary<EspColorOption, string>();
            foreach (var defaultColor in GetDefaultColors())
                config.ESP.Colors.TryAdd(defaultColor.Key, defaultColor.Value);
            SetColors(config.ESP.Colors);
        }

        /// <summary>
        /// Returns all default color combinations for ESP.
        /// </summary>
        internal static Dictionary<EspColorOption, string> GetDefaultColors() =>
            new()
            {
                [EspColorOption.Visible] = SKColors.LimeGreen.ToString(),
                [EspColorOption.Friendly] = SKColors.LimeGreen.ToString(),
                [EspColorOption.USEC] = SKColors.Red.ToString(),
                [EspColorOption.BEAR] = SKColors.Blue.ToString(),
                [EspColorOption.Focused] = SKColors.Coral.ToString(),
                [EspColorOption.Streamer] = SKColors.MediumPurple.ToString(),
                [EspColorOption.AimbotTarget] = SKColor.Parse("4654ff").ToString(), // Blue doesnt work on some fusers
                [EspColorOption.Special] = SKColors.MediumPurple.ToString(),
                [EspColorOption.PlayerScav] = SKColors.Orange.ToString(),
                [EspColorOption.Scav] = SKColors.Yellow.ToString(),
                [EspColorOption.Raider] = SKColor.Parse("ffc70f").ToString(),
                [EspColorOption.Boss] = SKColors.Fuchsia.ToString(),
                [EspColorOption.RegularLoot] = SKColors.WhiteSmoke.ToString(),
                [EspColorOption.ValuableLoot] = SKColors.Turquoise.ToString(),
                [EspColorOption.WishlistLoot] = SKColors.Red.ToString(),
                [EspColorOption.ContainerLoot] = SKColor.Parse("FFFFCC").ToString(),
                [EspColorOption.QuestLoot] = SKColors.YellowGreen.ToString(),
                [EspColorOption.Airdrop] = SKColors.Red.ToString(),
                [EspColorOption.StaticQuestItemsAndZones] = SKColors.DeepPink.ToString(),
                [EspColorOption.Corpse] = SKColors.Silver.ToString(),
                [EspColorOption.MedsFilterLoot] = SKColors.LightSalmon.ToString(),
                [EspColorOption.FoodFilterLoot] = SKColors.CornflowerBlue.ToString(),
                [EspColorOption.WeaponsFilterLoot] = SKColor.Parse("FFE068").ToString(),
                [EspColorOption.BackpackFilterLoot] = SKColor.Parse("00b02c").ToString(),
                [EspColorOption.Explosives] = SKColors.OrangeRed.ToString(),
                [EspColorOption.Switches] = SKColors.Orange.ToString(),
                [EspColorOption.DoorOpen] = SKColors.Green.ToString(),
                [EspColorOption.DoorLocked] = SKColors.Red.ToString(),
                [EspColorOption.DoorShut] = SKColors.Orange.ToString(),
                [EspColorOption.ExfilOpen] = SKColors.MediumSeaGreen.ToString(),
                [EspColorOption.ExfilPending] = SKColors.Yellow.ToString(),
                [EspColorOption.ExfilClosed] = SKColors.Red.ToString(),
                [EspColorOption.ExfilInactive] = SKColors.Gray.ToString(),
                [EspColorOption.ExfilTransit] = SKColors.Orange.ToString(),

                [EspColorOption.FPS] = SKColors.WhiteSmoke.ToString(),
                [EspColorOption.RaidStats] = SKColors.WhiteSmoke.ToString(),
                [EspColorOption.StatusText] = SKColors.Red.ToString(),
                [EspColorOption.MagazineInfo] = SKColors.WhiteSmoke.ToString(),
                [EspColorOption.EnergyBar] = SKColor.Parse("D4C48A").ToString(),
                [EspColorOption.HydrationBar] = SKColor.Parse("5B9BD5").ToString(),
                [EspColorOption.Crosshair] = SKColors.WhiteSmoke.ToString(),
                [EspColorOption.FireportAim] = SKColors.WhiteSmoke.ToString(),
                [EspColorOption.AimbotFOV] = SKColors.WhiteSmoke.ToString(),
                [EspColorOption.AimbotLock] = SKColors.WhiteSmoke.ToString(),
                [EspColorOption.ClosestPlayer] = SKColors.WhiteSmoke.ToString(),
                [EspColorOption.TopLoot] = SKColors.WhiteSmoke.ToString(),
                [EspColorOption.MiniRadarTheme] = SKColors.WhiteSmoke.ToString()
            };

        /// <summary>
        /// Save all ESP Color Changes.
        /// </summary>
        internal static void SetColors(IReadOnlyDictionary<EspColorOption, string> colors)
        {
            try
            {
                foreach (var color in colors)
                {
                    if (!SKColor.TryParse(color.Value, out var skColor))
                        throw new Exception($"Invalid Color Value for {color.Key}!");
                    switch (color.Key)
                    {
                        case EspColorOption.Visible:
                            SKPaints.PaintVisible.Color = skColor;
                            SKPaints.TextVisible.Color = skColor;
                            break;
                        case EspColorOption.Friendly:
                            SKPaints.PaintFriendlyESP.Color = skColor;
                            SKPaints.TextFriendlyESP.Color = skColor;
                            break;
                        case EspColorOption.USEC:
                            SKPaints.PaintUSECESP.Color = skColor;
                            SKPaints.TextUSECESP.Color = skColor;
                            break;
                        case EspColorOption.BEAR:
                            SKPaints.PaintBEARESP.Color = skColor;
                            SKPaints.TextBEARESP.Color = skColor;
                            break;
                        case EspColorOption.Focused:
                            SKPaints.PaintFocusedESP.Color = skColor;
                            SKPaints.TextFocusedESP.Color = skColor;
                            break;
                        case EspColorOption.Streamer:
                            SKPaints.PaintStreamerESP.Color = skColor;
                            SKPaints.TextStreamerESP.Color = skColor;
                            break;
                        case EspColorOption.AimbotTarget:
                            SKPaints.PaintAimbotLockedESP.Color = skColor;
                            SKPaints.TextAimbotLockedESP.Color = skColor;
                            break;
                        case EspColorOption.Special:
                            SKPaints.PaintSpecialESP.Color = skColor;
                            SKPaints.TextSpecialESP.Color = skColor;
                            break;
                        case EspColorOption.PlayerScav:
                            SKPaints.PaintPlayerScavESP.Color = skColor;
                            SKPaints.TextPlayerScavESP.Color = skColor;
                            break;
                        case EspColorOption.Scav:
                            SKPaints.PaintScavESP.Color = skColor;
                            SKPaints.TextScavESP.Color = skColor;
                            break;
                        case EspColorOption.Raider:
                            SKPaints.PaintRaiderESP.Color = skColor;
                            SKPaints.TextRaiderESP.Color = skColor;
                            break;
                        case EspColorOption.Boss:
                            SKPaints.PaintBossESP.Color = skColor;
                            SKPaints.TextBossESP.Color = skColor;
                            break;
                        case EspColorOption.RegularLoot:
                            SKPaints.PaintLootESP.Color = skColor;
                            SKPaints.TextLootESP.Color = skColor;
                            break;
                        case EspColorOption.ValuableLoot:
                            SKPaints.PaintImpLootESP.Color = skColor;
                            SKPaints.TextImpLootESP.Color = skColor;
                            break;
                        case EspColorOption.WishlistLoot:
                            SKPaints.PaintWishlistItemESP.Color = skColor;
                            SKPaints.TextWishlistItemESP.Color = skColor;
                            break;
                        case EspColorOption.QuestLoot:
                            SKPaints.PaintQuestItemESP.Color = skColor;
                            SKPaints.TextQuestItemESP.Color = skColor;
                            break;
                        case EspColorOption.Airdrop:
                            SKPaints.PaintAirdropESP.Color = skColor;
                            SKPaints.TextAirdropESP.Color = skColor;
                            break;
                        case EspColorOption.StaticQuestItemsAndZones:
                            SKPaints.PaintQuestHelperESP.Color = skColor;
                            SKPaints.TextQuestHelperESP.Color = skColor;
                            break;
                        case EspColorOption.Corpse:
                            SKPaints.PaintCorpseESP.Color = skColor;
                            SKPaints.TextCorpseESP.Color = skColor;
                            break;
                        case EspColorOption.MedsFilterLoot:
                            SKPaints.PaintMedsESP.Color = skColor;
                            SKPaints.TextMedsESP.Color = skColor;
                            break;
                        case EspColorOption.FoodFilterLoot:
                            SKPaints.PaintFoodESP.Color = skColor;
                            SKPaints.TextFoodESP.Color = skColor;
                            break;
                        case EspColorOption.WeaponsFilterLoot:
                            SKPaints.PaintWeaponsESP.Color = skColor;
                            SKPaints.TextWeaponsESP.Color = skColor;
                            break;
                        case EspColorOption.BackpackFilterLoot:
                            SKPaints.PaintBackpackESP.Color = skColor;
                            SKPaints.TextBackpackESP.Color = skColor;
                            break;
                        case EspColorOption.ContainerLoot:
                            SKPaints.PaintContainerLootESP.Color = skColor;
                            SKPaints.TextContainerLootESP.Color = skColor;
                            break;
                        case EspColorOption.Explosives:
                            SKPaints.PaintExplosiveESP.Color = skColor;
                            SKPaints.PaintExplosiveRadiusESP.Color = skColor;
                            SKPaints.TextExplosiveESP.Color = skColor;
                            break;
                        case EspColorOption.Switches:
                            SKPaints.TextSwitchesESP.Color = skColor;
                            SKPaints.PaintSwitchESP.Color = skColor;
                            break;
                        case EspColorOption.DoorOpen:
                            SKPaints.TextDoorOpenESP.Color = skColor;
                            SKPaints.PaintDoorOpenESP.Color = skColor;
                            break;
                        case EspColorOption.DoorShut:
                            SKPaints.TextDoorShutESP.Color = skColor;
                            SKPaints.PaintDoorShutESP.Color = skColor;
                            break;
                        case EspColorOption.DoorLocked:
                            SKPaints.TextDoorLockedESP.Color = skColor;
                            SKPaints.PaintDoorLockedESP.Color = skColor;
                            break;
                        case EspColorOption.ExfilOpen:
                            SKPaints.TextExfilOpenESP.Color = skColor;
                            SKPaints.PaintExfilOpenESP.Color = skColor;
                            break;
                        case EspColorOption.ExfilPending:
                            SKPaints.TextExfilPendingESP.Color = skColor;
                            SKPaints.PaintExfilPendingESP.Color = skColor;
                            break;
                        case EspColorOption.ExfilClosed:
                            SKPaints.TextExfilClosedESP.Color = skColor;
                            SKPaints.PaintExfilClosedESP.Color = skColor;
                            break;
                        case EspColorOption.ExfilTransit:
                            SKPaints.TextExfilTransitESP.Color = skColor;
                            SKPaints.PaintExfilTransitESP.Color = skColor;
                            break;
                        case EspColorOption.FPS:
                            SKPaints.TextESPFPS.Color = skColor;
                            break;
                        case EspColorOption.RaidStats:
                            SKPaints.TextESPRaidStats.Color = skColor;
                            break;
                        case EspColorOption.StatusText:
                            SKPaints.TextESPStatusText.Color = skColor;
                            break;
                        case EspColorOption.MagazineInfo:
                            SKPaints.TextMagazineESP.Color = skColor;
                            SKPaints.TextMagazineInfoESP.Color = skColor;
                            break;
                        case EspColorOption.EnergyBar:
                            SKPaints.PaintEnergyFillESP.Color = skColor;
                            break;
                        case EspColorOption.HydrationBar:
                            SKPaints.PaintHydrationFillESP.Color = skColor;
                            break;
                        case EspColorOption.Crosshair:
                            SKPaints.PaintCrosshairESP.Color = skColor;
                            SKPaints.PaintCrosshairESPDot.Color = skColor;
                            break;
                        case EspColorOption.FireportAim:
                            SKPaints.PaintFireportAimESP.Color = skColor;
                            break;
                        case EspColorOption.AimbotFOV:
                            SKPaints.PaintAimbotFOVESP.Color = skColor;
                            break;
                        case EspColorOption.AimbotLock:
                            SKPaints.PaintAimbotLockedLineESP.Color = skColor;
                            break;
                        case EspColorOption.ClosestPlayer:
                            SKPaints.TextESPClosestPlayer.Color = skColor;
                            break;
                        case EspColorOption.TopLoot:
                            SKPaints.TextESPTopLoot.Color = skColor;
                            break;
                        case EspColorOption.MiniRadarTheme:
                            SKPaints.PaintMiniRadarOutlineESP.Color = skColor;
                            SKPaints.PaintMiniRadarResizeHandleESP.Color = skColor;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("ERROR Setting ESP Colors", ex);
            }
        }

        #endregion
    }
}