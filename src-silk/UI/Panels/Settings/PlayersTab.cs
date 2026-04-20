using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    internal static partial class SettingsPanel
    {
        private static void DrawPlayersTab()
        {
            if (!ImGui.BeginTabItem("Players"))
                return;

            ImGui.Spacing();

            ImGui.SeparatorText("Rendering");

            bool playersOnTop = Config.PlayersOnTop;
            if (ImGui.Checkbox("Players On Top", ref playersOnTop))
                Config.PlayersOnTop = playersOnTop;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Draw players above all other entities");

            bool connectGroups = Config.ConnectGroups;
            if (ImGui.Checkbox("Connect Groups", ref connectGroups))
                Config.ConnectGroups = connectGroups;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Draw lines connecting players in the same group");

            ImGui.Spacing();
            ImGui.SeparatorText("Aimline");

            bool showAimlines = Config.ShowAimlines;
            if (ImGui.Checkbox("Show Aimlines", ref showAimlines))
                Config.ShowAimlines = showAimlines;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show facing direction lines on player markers");

            if (Config.ShowAimlines)
            {
                ImGui.Indent(16);

                ImGui.SetNextItemWidth(180);
                int aimlineLength = Config.AimlineLength;
                if (ImGui.SliderInt("Length", ref aimlineLength, 0, 100))
                    Config.AimlineLength = aimlineLength;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Aimline length in pixels (human players)");

                bool highAlert = Config.HighAlert;
                if (ImGui.Checkbox("High Alert", ref highAlert))
                    Config.HighAlert = highAlert;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Extend aimline when an enemy is aiming at you");

                ImGui.Unindent(16);
            }

            ImGui.Spacing();
            ImGui.SeparatorText("Aimview");

            bool showAimview = Config.ShowAimview;
            if (ImGui.Checkbox("Show Aimview", ref showAimview))
                Config.ShowAimview = showAimview;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("First-person projection widget showing nearby players");

            if (Config.ShowAimview)
            {
                ImGui.Indent(16);

                bool aimviewLoot = Config.AimviewShowLoot;
                if (ImGui.Checkbox("Show Loot", ref aimviewLoot))
                    Config.AimviewShowLoot = aimviewLoot;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show nearby filtered loot items in the aimview");

                bool aimviewCorpses = Config.AimviewShowCorpses;
                if (ImGui.Checkbox("Show Corpses", ref aimviewCorpses))
                    Config.AimviewShowCorpses = aimviewCorpses;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show nearby corpses with gear value in the aimview");

                bool aimviewContainers = Config.AimviewShowContainers;
                if (ImGui.Checkbox("Show Containers", ref aimviewContainers))
                    Config.AimviewShowContainers = aimviewContainers;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show nearby static containers in the aimview");

                bool aimviewSkeleton = Config.AimviewShowSkeleton;
                if (ImGui.Checkbox("Show Skeleton", ref aimviewSkeleton))
                    Config.AimviewShowSkeleton = aimviewSkeleton;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Draw bone skeleton for players (advanced aimview only).\nFalls back to a dot when off or skeleton data isn't ready yet.");

                bool aimviewPlayerLabels = Config.AimviewShowPlayerLabels;
                if (ImGui.Checkbox("Show Player Labels", ref aimviewPlayerLabels))
                    Config.AimviewShowPlayerLabels = aimviewPlayerLabels;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show \"Name (distance)\" labels under each player");

                bool aimviewItemLabels = Config.AimviewShowItemLabels;
                if (ImGui.Checkbox("Show Item Labels", ref aimviewItemLabels))
                    Config.AimviewShowItemLabels = aimviewItemLabels;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show labels under loot, corpse, and container markers.\nTurn off for a less cluttered view — markers stay visible.");

                bool aimviewHideAI = Config.AimviewHideAIPlayers;
                if (ImGui.Checkbox("Hide AI Players", ref aimviewHideAI))
                    Config.AimviewHideAIPlayers = aimviewHideAI;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Hide Scav / Raider / Boss AI from the aimview.\nUseful on raids with many AI.");

                ImGui.Spacing();

                ImGui.SetNextItemWidth(160);
                float playerDist = Config.AimviewPlayerDistance;
                if (ImGui.SliderFloat("Player Range", ref playerDist, 50f, 500f, "%.0fm"))
                    Config.AimviewPlayerDistance = playerDist;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Max distance for players to appear in the aimview");

                ImGui.SetNextItemWidth(160);
                float lootDist = Config.AimviewLootDistance;
                if (ImGui.SliderFloat("Loot Range", ref lootDist, 5f, 50f, "%.0fm"))
                    Config.AimviewLootDistance = lootDist;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Max distance for loot and corpses in the aimview");

                ImGui.SetNextItemWidth(160);
                float eyeHeight = Config.AimviewEyeHeight;
                if (ImGui.SliderFloat("Eye Height", ref eyeHeight, 0.5f, 2.0f, "%.2fm"))
                    Config.AimviewEyeHeight = eyeHeight;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Camera height above body root — adjust if loot\nappears too high or too low (default: 1.50m)");

                ImGui.SetNextItemWidth(160);
                float zoom = Config.AimviewZoom;
                if (ImGui.SliderFloat("Zoom", ref zoom, 0.5f, 3.0f, "%.1fx"))
                    Config.AimviewZoom = zoom;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Zoom level (1.0 = ~90\u00b0 FOV, higher = zoomed in)");

                ImGui.SetNextItemWidth(160);
                int minLootValue = Config.AimviewMinLootValue;
                if (ImGui.InputInt("Min Loot Value (\u20bd)", ref minLootValue, 1000, 10000))
                    Config.AimviewMinLootValue = Math.Max(minLootValue, 0);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Hide loot cheaper than this price to reduce clutter.\nWishlisted items are always shown. 0 = no filter.");

                ImGui.SetNextItemWidth(160);
                int maxLoot = Config.AimviewMaxLoot;
                if (ImGui.SliderInt("Max Loot", ref maxLoot, 0, 64))
                    Config.AimviewMaxLoot = maxLoot;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Maximum number of loot markers drawn at once");

                ImGui.SetNextItemWidth(160);
                int maxCorpses = Config.AimviewMaxCorpses;
                if (ImGui.SliderInt("Max Corpses", ref maxCorpses, 0, 32))
                    Config.AimviewMaxCorpses = maxCorpses;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Maximum number of corpse markers drawn at once");

                ImGui.SetNextItemWidth(160);
                int maxContainers = Config.AimviewMaxContainers;
                if (ImGui.SliderInt("Max Containers", ref maxContainers, 0, 32))
                    Config.AimviewMaxContainers = maxContainers;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Maximum number of container markers drawn at once");

                ImGui.Spacing();
                ImGui.SeparatorText("Advanced Aimview");

                bool advancedAimview = Config.UseAdvancedAimview;
                if (ImGui.Checkbox("Use Advanced Aimview", ref advancedAimview))
                    Config.UseAdvancedAimview = advancedAimview;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Use real game camera data (ViewMatrix) for pixel-accurate\nprojection. Requires CameraManager — falls back to synthetic\ncamera if unavailable.");

                if (Config.UseAdvancedAimview)
                {
                    ImGui.SetNextItemWidth(160);
                    int monW = Config.GameMonitorWidth;
                    if (ImGui.InputInt("Game Monitor Width", ref monW, 0, 0))
                    {
                        monW = Math.Clamp(monW, 640, 7680);
                        Config.GameMonitorWidth = monW;
                        CameraManager.UpdateViewportRes(Config.GameMonitorWidth, Config.GameMonitorHeight);
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Width of the monitor running EFT (pixels)");

                    ImGui.SetNextItemWidth(160);
                    int monH = Config.GameMonitorHeight;
                    if (ImGui.InputInt("Game Monitor Height", ref monH, 0, 0))
                    {
                        monH = Math.Clamp(monH, 480, 4320);
                        Config.GameMonitorHeight = monH;
                        CameraManager.UpdateViewportRes(Config.GameMonitorWidth, Config.GameMonitorHeight);
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Height of the monitor running EFT (pixels)");

                    if (!CameraManager.IsActive)
                    {
                        ImGui.TextColored(new Vector4(1f, 0.6f, 0.2f, 1f), "CameraManager not active — waiting for raid");
                    }
                }

                ImGui.Unindent(16);
            }

            ImGui.Spacing();
            ImGui.SeparatorText("Profile");

            bool profileLookups = Config.ProfileLookups;
            if (ImGui.Checkbox("Profile Lookups", ref profileLookups))
                Config.ProfileLookups = profileLookups;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Fetch player stats from tarkov.dev (K/D, hours, survival rate)");

            ImGui.EndTabItem();
        }
    }
}
