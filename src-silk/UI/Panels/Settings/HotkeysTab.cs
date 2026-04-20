using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    internal static partial class SettingsPanel
    {
        private static void DrawHotkeysTab()
        {
            if (!ImGui.BeginTabItem("Hotkeys"))
                return;

            ImGui.Spacing();

            if (!InputManager.IsReady)
            {
                ImGui.TextColored(new Vector4(1f, 0.6f, 0.2f, 1f),
                    "\u26a0 Input manager not initialized.");
                ImGui.TextWrapped("Hotkeys require an active DMA connection. They will activate once a raid starts.");
                ImGui.EndTabItem();
                return;
            }

            ImGui.TextWrapped("Manage hotkeys in the dedicated Hotkeys panel.");
            ImGui.Spacing();

            if (ImGui.Button("\u2328 Open Hotkey Manager", new Vector2(200, 0)))
                HotkeyManagerPanel.IsOpen = true;

            ImGui.Spacing();
            ImGui.SeparatorText("Active Hotkeys");

            var hotkeys = Config.Hotkeys;
            if (hotkeys.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "No hotkeys configured.");
            }
            else
            {
                foreach (var (id, entry) in hotkeys)
                {
                    if (!entry.Enabled || entry.Key < 1)
                        continue;

                    var def = HotkeyManager.GetAction(id);
                    string name = def?.DisplayName ?? id;
                    string mode = entry.Mode == HotkeyMode.Toggle ? "Toggle" : "OnKey";

                    ImGui.BulletText($"{name}  [{VK.GetName(entry.Key)}]  ({mode})");
                }
            }

            ImGui.EndTabItem();
        }
    }
}
