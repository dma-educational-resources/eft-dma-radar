using ImGuiNET;
using eft_dma_radar.Silk.Config;

#nullable enable
namespace eft_dma_radar.Silk.UI.Panels
{
    internal static class SettingsPanel
    {
        private static SilkConfig Config => SilkProgram.Config;

        /// <summary>
        /// Whether the settings panel is open.
        /// </summary>
        public static bool IsOpen { get; set; }

        /// <summary>
        /// Draw the settings panel.
        /// </summary>
        public static void Draw()
        {
            bool isOpen = IsOpen;
            ImGui.SetNextWindowSize(new Vector2(500, 600), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("Radar Settings", ref isOpen))
            {
                IsOpen = isOpen;
                ImGui.End();
                return;
            }
            IsOpen = isOpen;

            if (ImGui.BeginTabBar("SettingsTabs"))
            {
                DrawGeneralTab();
                DrawPlayersTab();
                DrawMapTab();

                ImGui.EndTabBar();
            }

            ImGui.End();
        }

        private static void DrawGeneralTab()
        {
            if (!ImGui.BeginTabItem("General"))
                return;

            ImGui.SeparatorText("Display");

            float uiScale = Config.UIScale;
            if (ImGui.SliderFloat("UI Scale", ref uiScale, 0.5f, 2.0f))
                Config.UIScale = uiScale;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Scale the radar canvas rendering");

            int fps = Config.TargetFps;
            if (ImGui.SliderInt("Target FPS", ref fps, 30, 300))
            {
                Config.TargetFps = fps;
                RadarWindow.Window.FramesPerSecond = fps;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Maximum frames per second for the radar window");

            ImGui.SeparatorText("Modes");

            bool battleMode = Config.BattleMode;
            if (ImGui.Checkbox("Battle Mode", ref battleMode))
                Config.BattleMode = battleMode;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Hide loot and other clutter; show only players");

            ImGui.SeparatorText("Actions");

            if (ImGui.Button("Save Config"))
                Config.Save();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Save current configuration to disk");

            ImGui.EndTabItem();
        }

        private static void DrawPlayersTab()
        {
            if (!ImGui.BeginTabItem("Players"))
                return;

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

            ImGui.EndTabItem();
        }

        private static void DrawMapTab()
        {
            if (!ImGui.BeginTabItem("Map"))
                return;

            int zoom = RadarWindow.Zoom;
            if (ImGui.SliderInt("Zoom", ref zoom, 1, 200))
                RadarWindow.Zoom = zoom;

            bool freeMode = RadarWindow.FreeMode;
            if (ImGui.Checkbox("Free Mode", ref freeMode))
                RadarWindow.FreeMode = freeMode;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Toggle between player-follow and free-pan map mode");

            ImGui.EndTabItem();
        }
    }
}