using ImGuiNET;

#nullable enable
namespace eft_dma_radar.Silk.UI.Panels
{
    /// <summary>
    /// Loot Filters Panel for the ImGui-based Radar.
    /// </summary>
    internal static class LootFiltersPanel
    {
        /// <summary>
        /// Whether the loot filters panel is open.
        /// </summary>
        public static bool IsOpen { get; set; }

        /// <summary>
        /// Draw the loot filters panel.
        /// </summary>
        public static void Draw()
        {
            bool isOpen = IsOpen;
            ImGui.SetNextWindowSize(new Vector2(600, 400), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("Loot Filters", ref isOpen))
            {
                IsOpen = isOpen;
                ImGui.End();
                return;
            }
            IsOpen = isOpen;

            ImGui.Text("Loot filter configuration.");
            ImGui.Separator();
            ImGui.TextWrapped(
                "Configure loot filters in the existing config file. " +
                "Full ImGui loot filter UI will be added in a future update.");

            ImGui.End();
        }
    }
}
