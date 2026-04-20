using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    internal static partial class SettingsPanel
    {
        private static SilkConfig Config => SilkProgram.Config;
        private static readonly string[] _wideLeanDirNames = ["Off", "Left", "Right", "Up"];

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
            using (var scope = PanelWindow.Begin("\u2699 Settings", ref isOpen, new Vector2(440, 520)))
            {
                IsOpen = isOpen;
                if (!scope.Visible)
                    return;

                if (ImGui.BeginTabBar("SettingsTabs"))
                {
                    DrawGeneralTab();
                    DrawPlayersTab();
                    DrawEspTab();
                    DrawMapTab();
                    DrawHotkeysTab();
                    DrawMemWritesTab();

                    ImGui.EndTabBar();
                }

                // Config auto-saves via SilkConfig.MarkDirty + FlushIfDirty —
                // no "Save" button needed. A small footer hint reinforces this.
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                ImGui.TextColored(UITheme.AccentGreen, "\u2713 Changes auto-saved");
            }
        }






    }
}