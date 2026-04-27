using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    internal static partial class SettingsPanel
    {
        private static async Task ToggleWebRadarAsync(bool enable)
        {
            try
            {
                if (enable)
                {
                    await eft_dma_radar.Silk.Web.WebRadarServer.StartAsync(
                        Config.WebRadarPort,
                        TimeSpan.FromMilliseconds(Config.WebRadarTickMs),
                        Config.WebRadarUPnP);
                }
                else
                {
                    await eft_dma_radar.Silk.Web.WebRadarServer.StopAsync();
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[WebRadar] Toggle error: {ex.Message}");
            }
        }

        private static void DrawGeneralTab()
        {
            if (!ImGui.BeginTabItem("General"))
                return;

            ImGui.Spacing();

            ImGui.SeparatorText("Display");

            ImGui.SetNextItemWidth(200);
            float uiScale = Config.UIScale;
            if (ImGui.SliderFloat("UI Scale", ref uiScale, 0.5f, 2.0f, "%.1fx"))
                Config.UIScale = uiScale;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Scale the radar canvas rendering");

            ImGui.SetNextItemWidth(200);
            int fps = Config.TargetFps;
            string fpsLabel = fps == 0 ? "Unlimited" : $"{fps}";
            if (ImGui.SliderInt("Target FPS", ref fps, 0, 360, fpsLabel))
            {
                Config.TargetFps = fps;
                RadarWindow.Window.FramesPerSecond = fps;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Max frames per second (0 = unlimited)");

            ImGui.Spacing();
            ImGui.SeparatorText("Modes");

            bool battleMode = Config.BattleMode;
            if (ImGui.Checkbox("Battle Mode", ref battleMode))
                Config.BattleMode = battleMode;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Hide loot and clutter; focus on players only  [B]");

            ImGui.Spacing();
            ImGui.SeparatorText("Hideout");

            bool hideoutEnabled = Config.HideoutEnabled;
            if (ImGui.Checkbox("Enable Hideout", ref hideoutEnabled))
                Config.HideoutEnabled = hideoutEnabled;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Read stash items and area upgrades when entering the hideout");

            if (Config.HideoutEnabled)
            {
                ImGui.Indent(16);
                bool autoRefresh = Config.HideoutAutoRefresh;
                if (ImGui.Checkbox("Auto Refresh", ref autoRefresh))
                    Config.HideoutAutoRefresh = autoRefresh;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Automatically refresh stash and area data on hideout entry");
                ImGui.Unindent(16);
            }

            ImGui.Spacing();
            ImGui.SeparatorText("Radar");

            {
                bool canRestart = Memory.InRaid || Memory.InHideout;
                if (!canRestart)
                    ImGui.BeginDisabled();

                if (ImGui.Button("\u21bb Restart Radar"))
                    Memory.RestartRadar = true;

                if (!canRestart)
                    ImGui.EndDisabled();

                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(canRestart
                        ? "Restart the radar (re-detect game world, players, loot)"
                        : "Only available during a raid or in the hideout");
            }

            ImGui.Spacing();
            ImGui.SeparatorText("Web Radar");

            bool webEnabled = Config.WebRadarEnabled;
            if (ImGui.Checkbox("Enable Web Radar", ref webEnabled))
            {
                Config.WebRadarEnabled = webEnabled;
                _ = ToggleWebRadarAsync(webEnabled);
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Start/stop the web radar HTTP server.\nAccess from a browser on any device on your network.");

            if (Config.WebRadarEnabled)
            {
                ImGui.Indent(16);

                ImGui.SetNextItemWidth(120);
                int port = Config.WebRadarPort;
                if (ImGui.InputInt("Port", ref port, 0, 0))
                {
                    if (port is >= 1024 and <= 65535)
                        Config.WebRadarPort = port;
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("HTTP port (requires restart to take effect)");

                ImGui.SetNextItemWidth(120);
                int tickMs = Config.WebRadarTickMs;
                if (ImGui.SliderInt("Tick (ms)", ref tickMs, 20, 200))
                    Config.WebRadarTickMs = tickMs;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Update interval for the web radar data");

                bool upnp = Config.WebRadarUPnP;
                if (ImGui.Checkbox("UPnP / NAT-PMP", ref upnp))
                    Config.WebRadarUPnP = upnp;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Automatically forward the port on your router via UPnP.\nEnables access from outside your network.\nTakes effect on next server start.");

                if (eft_dma_radar.Silk.Web.WebRadarServer.IsRunning)
                {
                    ImGui.TextColored(new Vector4(0.26f, 0.84f, 0.50f, 1f),
                        $"\u25cf Running on port {Config.WebRadarPort}");

                    // Private address
                    ImGui.Spacing();
                    var privateAddr = eft_dma_radar.Silk.Web.WebRadarServer.PrivateAddress;
                    if (!string.IsNullOrEmpty(privateAddr))
                    {
                        ImGui.Text("Private:");
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(0.55f, 0.83f, 1f, 1f), privateAddr);
                        ImGui.SameLine();
                        if (ImGui.SmallButton("\uf0c5 Copy##private"))
                            ImGui.SetClipboardText(privateAddr);
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Copy private (LAN) address to clipboard");
                        ImGui.SameLine();
                        if (ImGui.SmallButton("\u2197 Open##private"))
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo(privateAddr) { UseShellExecute = true });
                            }
                            catch { }
                        }
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Open in default browser");
                    }

                    // Public address
                    var publicAddr = eft_dma_radar.Silk.Web.WebRadarServer.PublicAddress;
                    if (!string.IsNullOrEmpty(publicAddr))
                    {
                        ImGui.Text("Public: ");
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(1f, 0.85f, 0.40f, 1f), publicAddr);
                        ImGui.SameLine();
                        if (ImGui.SmallButton("\uf0c5 Copy##public"))
                            ImGui.SetClipboardText(publicAddr);
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Copy public (WAN) address to clipboard");
                        ImGui.SameLine();
                        if (ImGui.SmallButton("\u2197 Open##public"))
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo(publicAddr) { UseShellExecute = true });
                            }
                            catch { }
                        }
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Open in default browser");
                    }
                    else if (string.IsNullOrEmpty(publicAddr) && !string.IsNullOrEmpty(privateAddr))
                    {
                        ImGui.TextColored(new Vector4(0.60f, 0.60f, 0.60f, 1f),
                            "Public:  Detecting...");
                    }
                }
                else
                {
                    ImGui.TextColored(new Vector4(0.60f, 0.60f, 0.60f, 1f),
                        "\u25cb Stopped");
                }

                ImGui.Unindent(16);
            }

            ImGui.EndTabItem();
        }
    }
}
