using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    internal static partial class SettingsPanel
    {
        private static readonly string[] _espRenderModes = ["None", "Bones", "Box", "Head Dot"];
        private static readonly string[] _espCrosshairTypes = ["Plus", "Cross", "Circle", "Dot", "Square", "Diamond"];

        private static void DrawEspTab()
        {
            if (!ImGui.BeginTabItem("ESP"))
                return;

            ImGui.Spacing();

            // ── Window state ──
            bool open = eft_dma_radar.Silk.UI.ESP.EspWindow.IsOpen;
            if (ImGui.Checkbox("ESP Window Open", ref open))
            {
                eft_dma_radar.Silk.UI.ESP.EspWindow.Toggle();
                Config.ShowEspWidget = eft_dma_radar.Silk.UI.ESP.EspWindow.IsOpen;
            }

            ImGui.SetNextItemWidth(200);
            int espFps = Config.EspTargetFps;
            string espFpsLabel = espFps == 0 ? "Unlimited" : $"{espFps}";
            if (ImGui.SliderInt("ESP Target FPS", ref espFps, 0, 360, espFpsLabel))
            {
                Config.EspTargetFps = espFps;
                eft_dma_radar.Silk.UI.ESP.EspWindow.ApplyTargetFps();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Render rate of the ESP window (0 = unlimited).\nIndependent of the radar FPS.");

            ImGui.SeparatorText("Players");

            bool showPlayers = Config.EspShowPlayers;
            if (ImGui.Checkbox("Show Players", ref showPlayers))
                Config.EspShowPlayers = showPlayers;

            ImGui.SetNextItemWidth(200);
            int mode = Config.EspRenderMode;
            if (ImGui.Combo("Render Mode", ref mode, _espRenderModes, _espRenderModes.Length))
                Config.EspRenderMode = mode;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("How each player is drawn.\nAlso cyclable via hotkey.");

            if (mode == 2) // Box
            {
                bool bones = Config.EspShowBones;
                if (ImGui.Checkbox("Show Bones Inside Box", ref bones))
                    Config.EspShowBones = bones;
            }

            ImGui.SetNextItemWidth(200);
            float pDist = Config.EspPlayerDistance;
            if (ImGui.SliderFloat("Max Distance##esp_p", ref pDist, 10f, 2000f, "%.0fm"))
                Config.EspPlayerDistance = pDist;

            ImGui.SeparatorText("Loot");

            bool showLoot = Config.EspShowLoot;
            if (ImGui.Checkbox("Show Loot", ref showLoot))
                Config.EspShowLoot = showLoot;

            ImGui.SetNextItemWidth(200);
            float lDist = Config.EspLootDistance;
            if (ImGui.SliderFloat("Max Distance##esp_l", ref lDist, 10f, 500f, "%.0fm"))
                Config.EspLootDistance = lDist;

            ImGui.SeparatorText("Crosshair");

            bool crosshair = Config.EspShowCrosshair;
            if (ImGui.Checkbox("Show Crosshair", ref crosshair))
                Config.EspShowCrosshair = crosshair;

            if (Config.EspShowCrosshair)
            {
                ImGui.Indent(16);

                ImGui.SetNextItemWidth(160);
                int cType = Config.EspCrosshairType;
                if (ImGui.Combo("Style", ref cType, _espCrosshairTypes, _espCrosshairTypes.Length))
                    Config.EspCrosshairType = cType;

                ImGui.SetNextItemWidth(160);
                float cScale = Config.EspCrosshairScale;
                if (ImGui.SliderFloat("Scale", ref cScale, 0.5f, 5f, "%.1fx"))
                    Config.EspCrosshairScale = cScale;

                ImGui.Unindent(16);
            }

            ImGui.SeparatorText("HUD");

            bool showFps = Config.EspShowFps;
            if (ImGui.Checkbox("Show FPS", ref showFps))
                Config.EspShowFps = showFps;

            bool showStatus = Config.EspShowStatusText;
            if (ImGui.Checkbox("Show Status Text", ref showStatus))
                Config.EspShowStatusText = showStatus;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Banner listing active memory-write features (LEAN, 3P, NV, THERMAL, ...)");

            bool showEnergyHydration = Config.EspShowEnergyHydration;
            if (ImGui.Checkbox("Show Energy / Hydration", ref showEnergyHydration))
                Config.EspShowEnergyHydration = showEnergyHydration;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Bottom-right bars showing local player energy + hydration");

            ImGui.EndTabItem();
        }
    }
}
